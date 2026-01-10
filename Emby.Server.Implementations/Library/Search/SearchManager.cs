using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Search;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.Library.Search;

/// <summary>
/// Manages search providers and orchestrates search operations.
/// </summary>
public class SearchManager : ISearchManager
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly ILogger<SearchManager> _logger;
    private ISearchProvider[] _configuredProviders = [];
    private ISearchProvider? _fallbackProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchManager"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="logger">The logger.</param>
    public SearchManager(
        ILibraryManager libraryManager,
        IUserManager userManager,
        ILogger<SearchManager> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _logger = logger;
    }

    /// <inheritdoc/>
    public void AddParts(IEnumerable<ISearchProvider> providers)
    {
        var allProviders = providers
            .OfType<ISearchProvider>()
            .OrderBy(p => p.Priority)
            .ToArray();

        // Separate the SQL fallback provider from configured providers
        _fallbackProvider = allProviders.OfType<SqlSearchProvider>().FirstOrDefault();
        _configuredProviders = allProviders.Where(p => p is not SqlSearchProvider).ToArray();

        _logger.LogInformation(
            "Registered {Count} search providers: {Providers}. Fallback: {Fallback}",
            _configuredProviders.Length,
            string.Join(", ", _configuredProviders.Select(p => $"{p.Name} (priority {p.Priority})")),
            _fallbackProvider?.Name ?? "none");
    }

    /// <inheritdoc/>
    public IReadOnlyList<ISearchProvider> GetProviders()
    {
        if (_fallbackProvider is null)
        {
            return _configuredProviders;
        }

        return [.. _configuredProviders, _fallbackProvider];
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SearchResult>> GetSearchResultsWithItemsAsync(
        SearchProviderQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.SearchTerm);

        var searchTerm = query.SearchTerm.Trim().RemoveDiacritics();

        var queryWithItems = new SearchProviderQuery
        {
            SearchTerm = query.SearchTerm,
            UserId = query.UserId,
            IncludeItemTypes = query.IncludeItemTypes,
            ExcludeItemTypes = query.ExcludeItemTypes,
            MediaTypes = query.MediaTypes,
            Limit = query.Limit,
            ParentId = query.ParentId,
            IncludeItemData = true
        };

        var candidates = await GetResultsWithDataFromProvidersAsync(_configuredProviders, queryWithItems, searchTerm, cancellationToken).ConfigureAwait(false);
        if (candidates.Count == 0 && _fallbackProvider is not null)
        {
            _logger.LogDebug("No results from configured providers, falling back to {Provider}", _fallbackProvider.Name);
            candidates = await GetResultsWithDataFromProvidersAsync([_fallbackProvider], queryWithItems, searchTerm, cancellationToken).ConfigureAwait(false);
        }

        return candidates;
    }

    /// <inheritdoc/>
    public async Task<QueryResult<SearchHintInfo>> GetSearchHintsAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.SearchTerm);

        var searchTerm = query.SearchTerm.Trim().RemoveDiacritics();

        var providerQuery = BuildProviderQuery(query, searchTerm);
        var candidateScores = await GetSearchResultsAsync(providerQuery, searchTerm, cancellationToken).ConfigureAwait(false);
        if (candidateScores.Count == 0)
        {
            return new QueryResult<SearchHintInfo>();
        }

        var user = !query.UserId.IsEmpty() ? _userManager.GetUserById(query.UserId) : null;

        var excludeItemTypes = BuildExcludeItemTypes(query);
        var includeItemTypes = BuildIncludeItemTypes(query);

        var internalQuery = new InternalItemsQuery(user)
        {
            ItemIds = candidateScores.Keys.ToArray(),
            ExcludeItemTypes = excludeItemTypes.ToArray(),
            IncludeItemTypes = includeItemTypes.Count > 0 ? includeItemTypes.ToArray() : [],
            MediaTypes = query.MediaTypes.ToArray(),
            IncludeItemsByName = !query.ParentId.HasValue,
            ParentId = query.ParentId ?? Guid.Empty,
            Recursive = true,
            IsKids = query.IsKids,
            IsMovie = query.IsMovie,
            IsNews = query.IsNews,
            IsSeries = query.IsSeries,
            IsSports = query.IsSports,
            DtoOptions = new DtoOptions
            {
                Fields =
                [
                    ItemFields.AirTime,
                    ItemFields.DateCreated,
                    ItemFields.ChannelInfo,
                    ItemFields.ParentId
                ]
            }
        };

        // MusicArtist items are "ItemsByName" entities - virtual items that aggregate content by artist name
        // rather than being stored as regular library items. They require special handling:
        // 1. Convert ParentId to AncestorIds (to filter by library folder)
        // 2. Set IncludeItemsByName = true (to include these virtual items in results)
        // 3. Clear IncludeItemTypes (GetAllArtists handles type filtering internally)
        // 4. Use GetAllArtists() instead of GetItemList() to query the artist index
        IReadOnlyList<BaseItem> items;
        if (internalQuery.IncludeItemTypes.Length == 1 && internalQuery.IncludeItemTypes[0] == BaseItemKind.MusicArtist)
        {
            if (!internalQuery.ParentId.IsEmpty())
            {
                internalQuery.AncestorIds = [internalQuery.ParentId];
                internalQuery.ParentId = Guid.Empty;
            }

            internalQuery.IncludeItemsByName = true;
            internalQuery.IncludeItemTypes = [];
            items = _libraryManager.GetAllArtists(internalQuery).Items.Select(i => i.Item).ToList();
        }
        else
        {
            items = _libraryManager.GetItemList(internalQuery);
        }

        var orderedResults = items
            .Select(item => new SearchHintInfo { Item = item })
            .OrderByDescending(hint => candidateScores.GetValueOrDefault(hint.Item.Id, 0f))
            .ToList();

        var totalCount = orderedResults.Count;

        if (query.StartIndex.HasValue)
        {
            orderedResults = orderedResults.Skip(query.StartIndex.Value).ToList();
        }

        if (query.Limit.HasValue)
        {
            orderedResults = orderedResults.Take(query.Limit.Value).ToList();
        }

        return new QueryResult<SearchHintInfo>(query.StartIndex, totalCount, orderedResults);
    }

    private async Task<IReadOnlyDictionary<Guid, float>> GetSearchResultsAsync(
        SearchProviderQuery query,
        string searchTerm,
        CancellationToken cancellationToken)
    {
        var scores = await GetSearchResultsFromProvidersAsync(_configuredProviders, query, searchTerm, cancellationToken).ConfigureAwait(false);
        if (scores.Count == 0 && _fallbackProvider is not null)
        {
            _logger.LogDebug("No results from configured providers, falling back to {Provider}", _fallbackProvider.Name);
            scores = await GetSearchResultsFromProvidersAsync([_fallbackProvider], query, searchTerm, cancellationToken).ConfigureAwait(false);
        }

        return scores;
    }

    private async Task<List<SearchResult>> GetResultsWithDataFromProvidersAsync(
        IEnumerable<ISearchProvider> providers,
        SearchProviderQuery providerQuery,
        string searchTerm,
        CancellationToken cancellationToken)
    {
        var eligibleProviders = providers.Where(p => p.CanSearch(providerQuery)).ToList();
        var searchTasks = eligibleProviders.Select(async provider =>
        {
            try
            {
                var results = await provider.SearchAsync(providerQuery, cancellationToken).ConfigureAwait(false);
                _logger.LogDebug(
                    "Provider {Provider} returned {Count} results for search term '{SearchTerm}'",
                    provider.Name,
                    results.Count,
                    searchTerm);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Search provider {Provider} failed for term '{SearchTerm}'", provider.Name, searchTerm);
                return [];
            }
        });

        var allProviderResults = await Task.WhenAll(searchTasks).ConfigureAwait(false);

        return allProviderResults
            .SelectMany(results => results)
            .GroupBy(r => r.ItemId)
            .Select(g => g
                .OrderByDescending(r => r.Score)
                .ThenByDescending(r => r.Item is not null)
                .First())
            .ToList();
    }

    private async Task<Dictionary<Guid, float>> GetSearchResultsFromProvidersAsync(
        IEnumerable<ISearchProvider> providers,
        SearchProviderQuery providerQuery,
        string searchTerm,
        CancellationToken cancellationToken)
    {
        var eligibleProviders = providers.Where(p => p.CanSearch(providerQuery)).ToList();
        var searchTasks = eligibleProviders.Select(async provider =>
        {
            try
            {
                var candidates = await provider.SearchAsync(providerQuery, cancellationToken).ConfigureAwait(false);
                _logger.LogDebug(
                    "Provider {Provider} returned {Count} candidates for search term '{SearchTerm}'",
                    provider.Name,
                    candidates.Count,
                    searchTerm);
                return candidates;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Search provider {Provider} failed for term '{SearchTerm}'", provider.Name, searchTerm);
                return [];
            }
        });

        var allProviderResults = await Task.WhenAll(searchTasks).ConfigureAwait(false);

        return allProviderResults
            .SelectMany(results => results)
            .GroupBy(r => r.ItemId)
            .ToDictionary(g => g.Key, g => g.Max(r => r.Score));
    }

    private static SearchProviderQuery BuildProviderQuery(SearchQuery query, string searchTerm)
    {
        var excludeItemTypes = BuildExcludeItemTypes(query);
        var includeItemTypes = BuildIncludeItemTypes(query);

        // Remove any excluded types from includes
        if (includeItemTypes.Count > 0 && excludeItemTypes.Count > 0)
        {
            includeItemTypes.RemoveAll(excludeItemTypes.Contains);
        }

        return new SearchProviderQuery
        {
            SearchTerm = searchTerm,
            UserId = query.UserId.IsEmpty() ? null : query.UserId,
            IncludeItemTypes = includeItemTypes.ToArray(),
            ExcludeItemTypes = excludeItemTypes.ToArray(),
            MediaTypes = query.MediaTypes.ToArray(),
            Limit = query.Limit,
            ParentId = query.ParentId
        };
    }

    private static List<BaseItemKind> BuildExcludeItemTypes(SearchQuery query)
    {
        var excludeItemTypes = query.ExcludeItemTypes.ToList();

        excludeItemTypes.Add(BaseItemKind.Year);
        excludeItemTypes.Add(BaseItemKind.Folder);
        excludeItemTypes.Add(BaseItemKind.CollectionFolder);

        if (!query.IncludeGenres)
        {
            AddIfMissing(excludeItemTypes, BaseItemKind.Genre);
            AddIfMissing(excludeItemTypes, BaseItemKind.MusicGenre);
        }

        if (!query.IncludePeople)
        {
            AddIfMissing(excludeItemTypes, BaseItemKind.Person);
        }

        if (!query.IncludeStudios)
        {
            AddIfMissing(excludeItemTypes, BaseItemKind.Studio);
        }

        if (!query.IncludeArtists)
        {
            AddIfMissing(excludeItemTypes, BaseItemKind.MusicArtist);
        }

        return excludeItemTypes;
    }

    private static List<BaseItemKind> BuildIncludeItemTypes(SearchQuery query)
    {
        var includeItemTypes = query.IncludeItemTypes.ToList();
        if (query.IncludeGenres && (includeItemTypes.Count == 0 || includeItemTypes.Contains(BaseItemKind.Genre)))
        {
            if (!query.IncludeMedia)
            {
                AddIfMissing(includeItemTypes, BaseItemKind.Genre);
                AddIfMissing(includeItemTypes, BaseItemKind.MusicGenre);
            }
        }

        if (query.IncludePeople && (includeItemTypes.Count == 0 || includeItemTypes.Contains(BaseItemKind.Person)))
        {
            if (!query.IncludeMedia)
            {
                AddIfMissing(includeItemTypes, BaseItemKind.Person);
            }
        }

        if (query.IncludeStudios && (includeItemTypes.Count == 0 || includeItemTypes.Contains(BaseItemKind.Studio)))
        {
            if (!query.IncludeMedia)
            {
                AddIfMissing(includeItemTypes, BaseItemKind.Studio);
            }
        }

        if (query.IncludeArtists && (includeItemTypes.Count == 0 || includeItemTypes.Contains(BaseItemKind.MusicArtist)))
        {
            if (!query.IncludeMedia)
            {
                AddIfMissing(includeItemTypes, BaseItemKind.MusicArtist);
            }
        }

        return includeItemTypes;
    }

    private static void AddIfMissing(List<BaseItemKind> list, BaseItemKind value)
    {
        if (!list.Contains(value))
        {
            list.Add(value);
        }
    }
}
