using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Extensions;
using Jellyfin.Api.Helpers;
using Jellyfin.Api.ModelBinders;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Extensions;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Api.Controllers;

/// <summary>
/// Movies controller.
/// </summary>
[Authorize]
[Tags("Movie")]
public class MoviesController : BaseJellyfinApiController
{
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IDtoService _dtoService;
    private readonly IServerConfigurationManager _serverConfigurationManager;
    private readonly ISimilarItemsManager _similarItemsManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="MoviesController"/> class.
    /// </summary>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="dtoService">Instance of the <see cref="IDtoService"/> interface.</param>
    /// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    /// <param name="similarItemsManager">Instance of the <see cref="ISimilarItemsManager"/> interface.</param>
    public MoviesController(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        IServerConfigurationManager serverConfigurationManager,
        ISimilarItemsManager similarItemsManager)
    {
        _userManager = userManager;
        _libraryManager = libraryManager;
        _dtoService = dtoService;
        _serverConfigurationManager = serverConfigurationManager;
        _similarItemsManager = similarItemsManager;
    }

    /// <summary>
    /// Gets movie recommendations.
    /// </summary>
    /// <param name="userId">Optional. Filter by user id, and attach user data.</param>
    /// <param name="parentId">Specify this to localize the search to a specific item or folder. Omit to use the root.</param>
    /// <param name="fields">Optional. The fields to return.</param>
    /// <param name="categoryLimit">The max number of categories to return.</param>
    /// <param name="itemLimit">The max number of items to return per category.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Movie recommendations returned.</response>
    /// <returns>The list of movie recommendations.</returns>
    [HttpGet("Recommendations")]
    public Task<ActionResult<IEnumerable<RecommendationDto>>> GetMovieRecommendations(
        [FromQuery] Guid? userId,
        [FromQuery] Guid? parentId,
        [FromQuery, ModelBinder(typeof(CommaDelimitedCollectionModelBinder))] ItemFields[] fields,
        [FromQuery] int categoryLimit = 5,
        [FromQuery] int itemLimit = 8,
        CancellationToken cancellationToken = default)
    {
        userId = RequestHelpers.GetUserId(User, userId);
        var user = userId.IsNullOrEmpty()
            ? null
            : _userManager.GetUserById(userId.Value);
        var dtoOptions = new DtoOptions { Fields = fields };

        var categories = new List<RecommendationDto>();

        var parentIdGuid = parentId ?? Guid.Empty;

        var query = new InternalItemsQuery(user)
        {
            IncludeItemTypes = new[]
            {
                BaseItemKind.Movie,
            },
            OrderBy = new[] { (ItemSortBy.DatePlayed, SortOrder.Descending), (ItemSortBy.Random, SortOrder.Descending) },
            Limit = 7,
            ParentId = parentIdGuid,
            Recursive = true,
            IsPlayed = true,
            EnableGroupByMetadataKey = true,
            DtoOptions = dtoOptions
        };

        var recentlyPlayedMovies = _libraryManager.GetItemList(query);

        var itemTypes = new List<BaseItemKind> { BaseItemKind.Movie };
        if (_serverConfigurationManager.Configuration.EnableExternalContentInSuggestions)
        {
            itemTypes.Add(BaseItemKind.Trailer);
            itemTypes.Add(BaseItemKind.LiveTvProgram);
        }

        var likedMovies = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IncludeItemTypes = itemTypes.ToArray(),
            IsMovie = true,
            OrderBy = new[] { (ItemSortBy.Random, SortOrder.Descending) },
            Limit = 10,
            IsFavoriteOrLiked = true,
            ExcludeItemIds = recentlyPlayedMovies.Select(i => i.Id).ToArray(),
            EnableGroupByMetadataKey = true,
            ParentId = parentIdGuid,
            Recursive = true,
            DtoOptions = dtoOptions
        });

        var mostRecentMovies = recentlyPlayedMovies.Take(Math.Min(recentlyPlayedMovies.Count, 6)).ToList();
        var recentDirectors = GetDirectors(mostRecentMovies).ToList();
        var recentActors = GetActors(mostRecentMovies).ToList();

        // Cap baseline items to categoryLimit - the round-robin can't use more categories than that.
        var recentlyPlayedBaseline = recentlyPlayedMovies.Count > categoryLimit
            ? recentlyPlayedMovies.Take(categoryLimit).ToList()
            : recentlyPlayedMovies;
        var likedBaseline = likedMovies.Count > categoryLimit
            ? likedMovies.Take(categoryLimit).ToList()
            : likedMovies;

        var batchQuery = new SimilarItemsQuery
        {
            User = user,
            Limit = itemLimit,
            DtoOptions = dtoOptions
        };

        var similarToRecentlyPlayed = BuildPendingFromBatch(
            _similarItemsManager.GetBatchSimilarItemsAsync(recentlyPlayedBaseline, batchQuery),
            recentlyPlayedBaseline,
            RecommendationType.SimilarToRecentlyPlayed);

        var similarToLiked = BuildPendingFromBatch(
            _similarItemsManager.GetBatchSimilarItemsAsync(likedBaseline, batchQuery),
            likedBaseline,
            RecommendationType.SimilarToLikedItem);

        var hasDirectorFromRecentlyPlayed = GetWithPerson(user, recentDirectors, itemLimit, dtoOptions, RecommendationType.HasDirectorFromRecentlyPlayed);
        var hasActorFromRecentlyPlayed = GetWithPerson(user, recentActors, itemLimit, dtoOptions, RecommendationType.HasActorFromRecentlyPlayed);

        // Use a single enumerator per list, listed twice so MoveNext advances it
        // twice per round-robin pass (giving these categories double weight).
        // IMPORTANT: Declare as IEnumerator<T> to box the List<T>.Enumerator struct once;
        // using var would box separately per list insertion, creating independent copies.
        IEnumerator<PendingRecommendation> similarToRecentlyPlayedEnum = similarToRecentlyPlayed.GetEnumerator();
        IEnumerator<PendingRecommendation> similarToLikedEnum = similarToLiked.GetEnumerator();

        var categoryTypes = new List<IEnumerator<PendingRecommendation>>
            {
                similarToRecentlyPlayedEnum,
                similarToRecentlyPlayedEnum,
                similarToLikedEnum,
                similarToLikedEnum,
                hasDirectorFromRecentlyPlayed.GetEnumerator(),
                hasActorFromRecentlyPlayed.GetEnumerator()
            };

        while (categories.Count < categoryLimit)
        {
            var allEmpty = true;

            foreach (var category in categoryTypes)
            {
                if (category.MoveNext())
                {
                    var pending = category.Current;
                    var returnItems = _dtoService.GetBaseItemDtos(pending.Items, dtoOptions, user);

                    categories.Add(new RecommendationDto
                    {
                        BaselineItemName = pending.BaselineItemName,
                        CategoryId = pending.CategoryId,
                        RecommendationType = pending.RecommendationType,
                        Items = returnItems
                    });

                    allEmpty = false;

                    if (categories.Count >= categoryLimit)
                    {
                        break;
                    }
                }
            }

            if (allEmpty)
            {
                break;
            }
        }

        return Task.FromResult<ActionResult<IEnumerable<RecommendationDto>>>(
            Ok(categories.OrderBy(i => i.RecommendationType).AsEnumerable()));
    }

    private static List<PendingRecommendation> BuildPendingFromBatch(
        Task<Dictionary<Guid, IReadOnlyList<BaseItem>>> batchTask,
        IReadOnlyList<BaseItem> baselineItems,
        RecommendationType type)
    {
        var batchResults = batchTask.GetAwaiter().GetResult();
        var results = new List<PendingRecommendation>();

        foreach (var item in baselineItems)
        {
            if (batchResults.TryGetValue(item.Id, out var similar) && similar.Count > 0)
            {
                results.Add(new PendingRecommendation
                {
                    BaselineItemName = item.Name,
                    CategoryId = item.Id,
                    RecommendationType = type,
                    Items = similar
                });
            }
        }

        return results;
    }

    private IEnumerable<PendingRecommendation> GetWithPerson(
        User? user,
        IEnumerable<string> names,
        int itemLimit,
        DtoOptions dtoOptions,
        RecommendationType type)
    {
        var itemTypes = new List<BaseItemKind> { BaseItemKind.Movie };
        if (_serverConfigurationManager.Configuration.EnableExternalContentInSuggestions)
        {
            itemTypes.Add(BaseItemKind.Trailer);
            itemTypes.Add(BaseItemKind.LiveTvProgram);
        }

        var personTypes = type == RecommendationType.HasDirectorFromRecentlyPlayed
            ? [PersonType.Director]
            : Array.Empty<string>();

        foreach (var name in names)
        {
            var items = _libraryManager.GetItemList(
                new InternalItemsQuery(user)
                {
                    Person = name,
                    Limit = itemLimit + 2,
                    PersonTypes = personTypes,
                    IncludeItemTypes = itemTypes.ToArray(),
                    IsMovie = true,
                    IsPlayed = false,
                    EnableGroupByMetadataKey = true,
                    DtoOptions = dtoOptions
                }).DistinctBy(i => i.GetProviderId(MediaBrowser.Model.Entities.MetadataProvider.Imdb) ?? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture))
                .Take(itemLimit)
                .ToList();

            if (items.Count > 0)
            {
                yield return new PendingRecommendation
                {
                    BaselineItemName = name,
                    CategoryId = name.GetMD5(),
                    RecommendationType = type,
                    Items = items
                };
            }
        }
    }

    private IReadOnlyList<string> GetActors(IReadOnlyList<BaseItem> items)
    {
        var itemIds = items.Select(i => i.Id).ToArray();
        return _libraryManager.GetPeopleNamesByItems(
            itemIds,
            new[] { PersonType.Actor, PersonType.GuestStar },
            limit: 0);
    }

    private IReadOnlyList<string> GetDirectors(IReadOnlyList<BaseItem> items)
    {
        var itemIds = items.Select(i => i.Id).ToArray();
        return _libraryManager.GetPeopleNamesByItems(
            itemIds,
            [PersonType.Director],
            limit: 0);
    }

    /// <summary>
    /// Holds a recommendation category's BaseItems before DTO conversion.
    /// DTO conversion is deferred until the round-robin actually selects the category.
    /// </summary>
    private sealed class PendingRecommendation
    {
        public required string BaselineItemName { get; init; }

        public required Guid CategoryId { get; init; }

        public required RecommendationType RecommendationType { get; init; }

        public required IReadOnlyList<BaseItem> Items { get; init; }
    }
}
