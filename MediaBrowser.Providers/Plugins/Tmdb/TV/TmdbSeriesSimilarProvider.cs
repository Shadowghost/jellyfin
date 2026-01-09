using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions.Json;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.Providers.Plugins.Tmdb.TV;

/// <summary>
/// TMDb-based similar items provider for TV series.
/// </summary>
public class TmdbSeriesSimilarProvider : ISimilarItemsProvider<Series>
{
    private const int CacheDurationInDays = 14;

    private readonly ILibraryManager _libraryManager;
    private readonly IApplicationPaths _appPaths;
    private readonly IFileSystem _fileSystem;
    private readonly TmdbClientManager _tmdbClientManager;
    private readonly ILogger<TmdbSeriesSimilarProvider> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbSeriesSimilarProvider"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="appPaths">The application paths.</param>
    /// <param name="fileSystem">The file system.</param>
    /// <param name="tmdbClientManager">The TMDb client manager.</param>
    /// <param name="logger">The logger.</param>
    public TmdbSeriesSimilarProvider(
        ILibraryManager libraryManager,
        IApplicationPaths appPaths,
        IFileSystem fileSystem,
        TmdbClientManager tmdbClientManager,
        ILogger<TmdbSeriesSimilarProvider> logger)
    {
        _libraryManager = libraryManager;
        _appPaths = appPaths;
        _fileSystem = fileSystem;
        _tmdbClientManager = tmdbClientManager;
        _logger = logger;
        _jsonOptions = JsonDefaults.Options;
    }

    /// <inheritdoc/>
    public string Name => TmdbUtils.ProviderName;

    /// <inheritdoc/>
    public MetadataPluginType Type => MetadataPluginType.SimilarityProvider;

    /// <inheritdoc/>
    public IReadOnlyList<BaseItem> GetSimilarItems(Series item, SimilarItemsQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!item.TryGetProviderId(MetadataProvider.Tmdb, out var tmdbIdStr) || !int.TryParse(tmdbIdStr, CultureInfo.InvariantCulture, out var tmdbId))
        {
            return [];
        }

        var requestedLimit = query.Limit ?? 50;

        return GetSimilarItemsWithLibraryCheckAsync(tmdbId, requestedLimit, query, CancellationToken.None)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
    }

    private async Task<List<BaseItem>> GetSimilarItemsWithLibraryCheckAsync(
        int tmdbId,
        int requestedLimit,
        SimilarItemsQuery query,
        CancellationToken cancellationToken)
    {
        var cachePath = GetCachePath(tmdbId);
        var results = new List<BaseItem>();
        var seenIds = new HashSet<Guid>(query.ExcludeItemIds);
        var providerName = MetadataProvider.Tmdb.ToString();
        var allFetchedTmdbIds = new List<int>();

        // Try to use cache first
        var cachedIds = await TryReadCacheAsync(cachePath, cancellationToken).ConfigureAwait(false);
        if (cachedIds is not null)
        {
            var libraryItems = FindItemsInLibrary(cachedIds, providerName, query);
            foreach (var similarId in cachedIds)
            {
                if (results.Count >= requestedLimit)
                {
                    break;
                }

                if (libraryItems.TryGetValue(similarId, out var foundItem) && seenIds.Add(foundItem.Id))
                {
                    results.Add(foundItem);
                }
            }

            return results;
        }

        // No valid cache - fetch from TMDB page by page until we have enough library matches
        var page = 1;
        var totalPages = int.MaxValue;

        while (results.Count < requestedLimit && page <= totalPages)
        {
            var (pageResults, fetchedTotalPages) = await _tmdbClientManager
                .GetSeriesSimilarPageAsync(tmdbId, page, TmdbUtils.GetImageLanguagesParam(string.Empty), cancellationToken)
                .ConfigureAwait(false);

            if (pageResults.Count == 0)
            {
                break;
            }

            totalPages = fetchedTotalPages;
            var pageIds = pageResults.Select(s => s.Id).ToList();
            allFetchedTmdbIds.AddRange(pageIds);

            var libraryItems = FindItemsInLibrary(pageIds, providerName, query);
            foreach (var similar in pageResults)
            {
                if (results.Count >= requestedLimit)
                {
                    break;
                }

                if (libraryItems.TryGetValue(similar.Id, out var foundItem) && seenIds.Add(foundItem.Id))
                {
                    results.Add(foundItem);
                }
            }

            page++;
        }

        if (allFetchedTmdbIds.Count > 0)
        {
            await SaveCacheAsync(cachePath, allFetchedTmdbIds, cancellationToken).ConfigureAwait(false);
        }

        return results;
    }

    private Dictionary<int, BaseItem> FindItemsInLibrary(IReadOnlyList<int> tmdbIds, string providerName, SimilarItemsQuery query)
    {
        if (tmdbIds.Count == 0)
        {
            return [];
        }

        // Ensure ProviderIds field is included so we can map results back
        var dtoOptions = new DtoOptions(false) { Fields = [ItemFields.ProviderIds] };

        var tmdbQuery = new InternalItemsQuery(query.User)
        {
            IncludeItemTypes = [BaseItemKind.Series],
            HasAnyProviderIds = new Dictionary<string, string[]>
            {
                { providerName, tmdbIds.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray() }
            },
            DtoOptions = dtoOptions
        };

        var items = _libraryManager.GetItemList(tmdbQuery);
        var result = new Dictionary<int, BaseItem>(items.Count);
        foreach (var item in items)
        {
            if (item.TryGetProviderId(MetadataProvider.Tmdb, out var itemTmdbIdStr)
                && int.TryParse(itemTmdbIdStr, CultureInfo.InvariantCulture, out var itemTmdbId))
            {
                result.TryAdd(itemTmdbId, item);
            }
        }

        return result;
    }

    private async Task<List<int>?> TryReadCacheAsync(string cachePath, CancellationToken cancellationToken)
    {
        var fileInfo = _fileSystem.GetFileSystemInfo(cachePath);
        if (!fileInfo.Exists
            || (DateTime.UtcNow - _fileSystem.GetLastWriteTimeUtc(fileInfo)).TotalDays > CacheDurationInDays)
        {
            return null;
        }

        try
        {
            var stream = File.OpenRead(cachePath);
            await using (stream.ConfigureAwait(false))
            {
                var cached = await JsonSerializer.DeserializeAsync<TmdbSimilarCache>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false);
                return cached?.SimilarIds;
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to read TMDb series similar cache for {CachePath}", cachePath);
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to read TMDb series similar cache for {CachePath}", cachePath);
            return null;
        }
    }

    private async Task SaveCacheAsync(string cachePath, List<int> similarIds, CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var cache = new TmdbSimilarCache { SimilarIds = similarIds };
            var stream = File.Create(cachePath);
            await using (stream.ConfigureAwait(false))
            {
                await JsonSerializer.SerializeAsync(stream, cache, _jsonOptions, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to save TMDb series similar cache to {CachePath}", cachePath);
        }
    }

    private string GetCachePath(int tmdbId)
    {
        var dataPath = Path.Combine(_appPaths.CachePath, "tmdb-similar-series", tmdbId.ToString(CultureInfo.InvariantCulture));
        return Path.Combine(dataPath, "similar.json");
    }

    private sealed class TmdbSimilarCache
    {
        public List<int>? SimilarIds { get; set; }
    }
}
