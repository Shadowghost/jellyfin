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
using Microsoft.Extensions.Logging;

namespace MediaBrowser.Providers.Plugins.Tmdb.TV;

/// <summary>
/// TMDb-based similar items provider for TV series.
/// </summary>
public class TmdbSeriesSimilarProvider : ISimilarItemsProvider<Series>
{
    private const int CacheDurationInDays = 2;

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
        if (!item.TryGetProviderId(MetadataProvider.Tmdb, out var tmdbIdStr)
            || !int.TryParse(tmdbIdStr, CultureInfo.InvariantCulture, out var tmdbId))
        {
            return [];
        }

        var requestedLimit = query.Limit ?? 50;
        var similarTmdbIds = GetSimilarTmdbIdsAsync(tmdbId, requestedLimit, CancellationToken.None)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

        if (similarTmdbIds.Count == 0)
        {
            return [];
        }

        var results = new List<BaseItem>();
        var seenIds = new HashSet<Guid>(query.ExcludeItemIds);
        var providerName = MetadataProvider.Tmdb.ToString();

        foreach (var similarId in similarTmdbIds)
        {
            if (results.Count >= requestedLimit)
            {
                break;
            }

            var tmdbQuery = new InternalItemsQuery(query.User)
            {
                IncludeItemTypes = [BaseItemKind.Series],
                HasAnyProviderId = new Dictionary<string, string>
                {
                    { providerName, similarId.ToString(CultureInfo.InvariantCulture) }
                },
                Limit = 1,
                DtoOptions = query.DtoOptions ?? new DtoOptions()
            };

            var items = _libraryManager.GetItemList(tmdbQuery);
            foreach (var foundItem in items)
            {
                if (seenIds.Add(foundItem.Id))
                {
                    results.Add(foundItem);
                    if (results.Count >= requestedLimit)
                    {
                        break;
                    }
                }
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<int>> GetSimilarTmdbIdsAsync(int tmdbId, int limit, CancellationToken cancellationToken)
    {
        var cachePath = GetCachePath(tmdbId);

        var fileInfo = _fileSystem.GetFileSystemInfo(cachePath);
        if (fileInfo.Exists
            && (DateTime.UtcNow - _fileSystem.GetLastWriteTimeUtc(fileInfo)).TotalDays <= CacheDurationInDays)
        {
            try
            {
                var stream = File.OpenRead(cachePath);
                await using (stream.ConfigureAwait(false))
                {
                    var cached = await JsonSerializer.DeserializeAsync<TmdbSimilarCache>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false);
                    if (cached?.SimilarIds is not null)
                    {
                        return cached.SimilarIds;
                    }
                }
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Failed to read TMDb series similar cache for {TmdbId}", tmdbId);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to read TMDb series similar cache for {TmdbId}", tmdbId);
            }
        }

        try
        {
            var similar = await _tmdbClientManager.GetSeriesSimilarAsync(tmdbId, limit, TmdbUtils.GetImageLanguagesParam(string.Empty), cancellationToken).ConfigureAwait(false);
            var similarIds = similar.Select(s => s.Id).ToList();

            await SaveCacheAsync(cachePath, similarIds, cancellationToken).ConfigureAwait(false);

            return similarIds;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch TMDb similar series for {TmdbId}", tmdbId);
            return [];
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
