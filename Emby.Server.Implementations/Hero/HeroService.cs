using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Extensions;
using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Hero;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.Hero;

/// <summary>
/// Picks and describes the items shown by the hero section.
/// </summary>
public class HeroService : IHeroService
{
    private const int DedupeHeadroom = 3;

    private static readonly string[] AlternateCutTerms =
    [
        "sign language",
        "asl trailer",
        "audio description",
        "audio described",
        "described audio",
        "vertical"
    ];

    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;
    private readonly IImageProcessor _imageProcessor;
    private readonly IServerConfigurationManager _configurationManager;
    private readonly IApplicationHost _appHost;
    private readonly ILogger<HeroService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HeroService"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    /// <param name="imageProcessor">Instance of the <see cref="IImageProcessor"/> interface.</param>
    /// <param name="configurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    /// <param name="appHost">Instance of the <see cref="IApplicationHost"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{HeroService}"/> interface.</param>
    public HeroService(
        ILibraryManager libraryManager,
        IUserDataManager userDataManager,
        IImageProcessor imageProcessor,
        IServerConfigurationManager configurationManager,
        IApplicationHost appHost,
        ILogger<HeroService> logger)
    {
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
        _imageProcessor = imageProcessor;
        _configurationManager = configurationManager;
        _appHost = appHost;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<HeroItemDto> GetHeroItems(User user, Guid? parentId, int? limit)
    {
        ArgumentNullException.ThrowIfNull(user);

        var options = _configurationManager.GetConfiguration<HeroOptions>("hero");

        if (!options.Enabled)
        {
            return [];
        }

        var configuredLimit = Math.Clamp(
            options.MaxItems < 1 ? 1 : options.MaxItems,
            1,
            HeroOptions.MaxItemLimit);
        var effectiveLimit = Math.Clamp(limit ?? configuredLimit, 1, configuredLimit);

        var items = options.Source switch
        {
            HeroSourceMode.Playlist or HeroSourceMode.Collection =>
                GetCuratedItems(user, options, effectiveLimit),
            _ => GetRandomItems(user, options, parentId, effectiveLimit)
        };

        return items.Count == 0 ? [] : Map(user, options, items);
    }

    private IReadOnlyList<BaseItem> GetRandomItems(User user, HeroOptions options, Guid? parentId, int limit)
    {
        // Logo and backdrop are both required for a slide to render, so the query has to match
        // every requested image type rather than any one of them.
        var imageTypes = options.RequireLogo
            ? new[] { ImageType.Backdrop, ImageType.Logo }
            : [ImageType.Backdrop];

        var includeItemTypes = GetIncludeItemTypes(options);

        var items = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IncludeItemTypes = includeItemTypes,
            Recursive = true,
            IsVirtualItem = false,
            ParentId = parentId ?? Guid.Empty,
            HasOverview = options.RequireOverview ? true : null,
            IsPlayed = options.IncludeWatched ? null : false,
            ImageTypes = imageTypes,
            ImageTypesMatchAll = true,
            OrderBy = [(ItemSortBy.Random, SortOrder.Descending)],
            GroupByPresentationUniqueKey = false,
            Limit = limit * DedupeHeadroom,

            DtoOptions = new DtoOptions(false)
        });

        return Deduplicate(items, user, options, limit);
    }

    private IReadOnlyList<BaseItem> GetCuratedItems(User user, HeroOptions options, int limit)
    {
        if (options.SourceId.IsEmpty())
        {
            _logger.LogDebug("Hero source is set to {Source} but no source id is configured", options.Source);
            return [];
        }

        // Resolved for the requesting user, so a curated source can never surface an item the user
        // is not allowed to see.
        if (_libraryManager.GetItemById<Folder>(options.SourceId, user) is not { } source)
        {
            _logger.LogDebug("Hero source {SourceId} was not found or is not visible to the user", options.SourceId);
            return [];
        }

        var includeItemTypes = GetIncludeItemTypes(options);
        var candidates = new List<BaseItem>();
        var seen = new HashSet<Guid>();

        foreach (var child in source.GetChildren(user, true, new InternalItemsQuery(user)
        {
            DtoOptions = new DtoOptions(false)
        }))
        {
            // An episode in a curated list stands in for its series, which is what a hero slide
            // has the artwork and the overview for.
            var item = child is Episode episode ? episode.Series ?? child : child;

            if (includeItemTypes.Length > 0 && !includeItemTypes.Contains(item.GetBaseItemKind()))
            {
                continue;
            }

            if (!seen.Add(item.Id) || !QualifiesForHero(item, options))
            {
                continue;
            }

            candidates.Add(item);
        }

        candidates.Shuffle();

        return candidates.Count > limit ? candidates.GetRange(0, limit) : candidates;
    }

    private static IReadOnlyList<BaseItem> Deduplicate(
        IReadOnlyList<BaseItem> items,
        User user,
        HeroOptions options,
        int limit)
    {
        var needsVisibilityCheck = options.IncludeItemTypes.Contains(BaseItemKind.BoxSet)
            || options.IncludeItemTypes.Contains(BaseItemKind.Playlist);

        var result = new List<BaseItem>(limit);
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            var key = item.PresentationUniqueKey;
            if (!string.IsNullOrEmpty(key) && !seenKeys.Add(key))
            {
                continue;
            }

            if (needsVisibilityCheck && !item.IsVisibleStandalone(user))
            {
                continue;
            }

            result.Add(item);

            if (result.Count == limit)
            {
                break;
            }
        }

        return result;
    }

    private static BaseItemKind[] GetIncludeItemTypes(HeroOptions options)
        => options.IncludeItemTypes.Length > 0
            ? options.IncludeItemTypes
            : [BaseItemKind.Movie, BaseItemKind.Series];

    private static bool QualifiesForHero(BaseItem item, HeroOptions options)
    {
        if (options.RequireOverview && string.IsNullOrWhiteSpace(item.Overview))
        {
            return false;
        }

        if (options.RequireLogo && !item.HasImage(ImageType.Logo))
        {
            return false;
        }

        return item.HasImage(ImageType.Backdrop);
    }

    private IReadOnlyList<HeroItemDto> Map(User user, HeroOptions options, IReadOnlyList<BaseItem> items)
    {
        // Warms the user data cache in one query so the per-item lookups below do not each hit the
        // database.
        _ = _userDataManager.GetUserDataBatch(items, user);

        var folderIds = items.OfType<Folder>().Select(e => e.Id).ToList();
        var childCounts = folderIds.Count == 0
            ? new Dictionary<Guid, int>()
            : _libraryManager.GetChildCountBatch(folderIds, user);

        var localTrailers = GetLocalTrailers(items);

        var serverId = _appHost.SystemId;
        var result = new List<HeroItemDto>(items.Count);

        foreach (var item in items)
        {
            result.Add(new HeroItemDto
            {
                Id = item.Id,
                ServerId = serverId,
                Type = item.GetBaseItemKind(),
                Name = item.Name,
                Overview = item.Overview,
                Genres = item.Genres,
                ProductionYear = item.ProductionYear,
                OfficialRating = item.OfficialRating,
                CommunityRating = item.CommunityRating,
                CriticRating = item.CriticRating,
                RunTimeTicks = item is Folder ? null : item.RunTimeTicks,
                ChildCount = childCounts.TryGetValue(item.Id, out var childCount) ? childCount : (int?)null,
                LogoImageTag = GetImageTag(item, ImageType.Logo),
                BackdropImageTags = GetImageTags(item, ImageType.Backdrop),
                ThumbImageTag = GetImageTag(item, ImageType.Thumb),
                PrimaryImageTag = GetImageTag(item, ImageType.Primary),
                Trailer = GetTrailer(item, options, localTrailers),
                UserData = _userDataManager.GetUserDataDto(item, user)
            });
        }

        return result;
    }

    /// <summary>
    /// Gets the local trailer for each of the given items, in one query rather than one per item.
    /// </summary>
    /// <param name="items">The items to get trailers for.</param>
    /// <returns>The first local trailer per owning item id.</returns>
    private Dictionary<Guid, BaseItem> GetLocalTrailers(IReadOnlyList<BaseItem> items)
    {
        var trailers = _libraryManager.GetItemList(new InternalItemsQuery
        {
            OwnerIds = items.Select(e => e.Id).ToArray(),
            ExtraTypes = [ExtraType.Trailer],
            OrderBy = [(ItemSortBy.SortName, SortOrder.Ascending)],
            DtoOptions = new DtoOptions(false)
        });

        var result = new Dictionary<Guid, BaseItem>();
        foreach (var trailer in trailers)
        {
            result.TryAdd(trailer.OwnerId, trailer);
        }

        return result;
    }

    private static HeroTrailerDto? GetTrailer(
        BaseItem item,
        HeroOptions options,
        Dictionary<Guid, BaseItem> localTrailers)
    {
        // A local trailer streams through the server, so it wins over anything that would send the
        // client off to a third party.
        if (localTrailers.TryGetValue(item.Id, out var localTrailer))
        {
            return new HeroTrailerDto
            {
                Kind = HeroTrailerKind.Local,
                ItemId = localTrailer.Id,
                Name = localTrailer.Name
            };
        }

        if (!options.EnableRemoteTrailers || item is not IHasTrailers hasTrailers)
        {
            return null;
        }

        return SelectRemoteTrailer(hasTrailers.RemoteTrailers);
    }

    /// <summary>
    /// Picks the trailer a hero slide should play out of an item's remote trailers.
    /// </summary>
    /// <remarks>
    /// Remote trailers arrive in metadata provider order, which is not preference order: the first
    /// entry is often a teaser, a promo clip or an accessibility variant. Entries are ranked by
    /// name with provider order breaking ties, and entries whose host is not recognised are skipped
    /// rather than losing the trailer for the slide.
    /// </remarks>
    /// <param name="remoteTrailers">The item's remote trailers.</param>
    /// <returns>The trailer to play, or <c>null</c> when none is usable.</returns>
    internal static HeroTrailerDto? SelectRemoteTrailer(IReadOnlyList<MediaUrl> remoteTrailers)
    {
        HeroTrailerDto? best = null;
        var bestRank = double.MinValue;

        foreach (var trailer in remoteTrailers)
        {
            if (!TryGetYouTubeVideoId(trailer.Url, out var videoId))
            {
                continue;
            }

            var rank = RankTrailerName(trailer.Name);
            if (best is not null && rank <= bestRank)
            {
                continue;
            }

            best = new HeroTrailerDto
            {
                Kind = HeroTrailerKind.Remote,
                Url = trailer.Url,
                Provider = "YouTube",
                ProviderId = videoId,
                Name = trailer.Name
            };
            bestRank = rank;
        }

        return best;
    }

    private static double RankTrailerName(string? name)
    {
        var text = name ?? string.Empty;

        var rank = text.Contains("official trailer", StringComparison.OrdinalIgnoreCase) ? 5
            : text.Contains("final trailer", StringComparison.OrdinalIgnoreCase)
                || text.Contains("main trailer", StringComparison.OrdinalIgnoreCase) ? 4
            : text.Contains("trailer", StringComparison.OrdinalIgnoreCase) ? 3
            : text.Contains("teaser", StringComparison.OrdinalIgnoreCase) ? 2
            : 1;

        // Alternate cuts are demoted within their tier: they lose to an ordinary entry of the same
        // tier but still beat a lower tier promo clip, so an item whose only trailer is an
        // alternate cut still plays something.
        return IsAlternateCut(text) ? rank - 0.5 : rank;
    }

    private static bool IsAlternateCut(string name)
    {
        foreach (var term in AlternateCutTerms)
        {
            if (name.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetYouTubeVideoId(string? url, out string? videoId)
    {
        videoId = null;

        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? uri.Host[4..]
            : uri.Host;

        if (string.Equals(host, "youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            videoId = uri.AbsolutePath.Trim('/').Split('/').FirstOrDefault();
        }
        else if ((string.Equals(host, "youtube.com", StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, "m.youtube.com", StringComparison.OrdinalIgnoreCase))
            && uri.AbsolutePath.StartsWith("/embed/", StringComparison.OrdinalIgnoreCase))
        {
            videoId = uri.AbsolutePath["/embed/".Length..].Trim('/').Split('/').FirstOrDefault();
        }
        else
        {
            videoId = GetQueryParameter(uri.Query, "v");
        }

        return !string.IsNullOrEmpty(videoId);
    }

    /// <summary>
    /// Reads a single parameter out of a url query string.
    /// </summary>
    /// <param name="query">The query string, leading <c>?</c> included.</param>
    /// <param name="name">The parameter name.</param>
    /// <returns>The parameter value, or <c>null</c> when it is absent or empty.</returns>
    private static string? GetQueryParameter(string query, string name)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator < 1 || !pair.AsSpan(0, separator).Equals(name, StringComparison.Ordinal))
            {
                continue;
            }

            var value = Uri.UnescapeDataString(pair[(separator + 1)..]);

            return string.IsNullOrEmpty(value) ? null : value;
        }

        return null;
    }

    private string? GetImageTag(BaseItem item, ImageType imageType)
    {
        var image = item.GetImageInfo(imageType, 0);

        return image is null ? null : _imageProcessor.GetImageCacheTag(item, image);
    }

    private IReadOnlyList<string> GetImageTags(BaseItem item, ImageType imageType)
        => item.GetImages(imageType)
            .Select(e => _imageProcessor.GetImageCacheTag(item, e))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}
