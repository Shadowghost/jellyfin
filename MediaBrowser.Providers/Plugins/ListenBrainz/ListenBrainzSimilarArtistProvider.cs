using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions.Json;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Querying;
using MediaBrowser.Providers.Plugins.ListenBrainz.Api;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.Providers.Plugins.ListenBrainz;

/// <summary>
/// ListenBrainz-based similar items provider for music artists.
/// </summary>
public class ListenBrainzSimilarArtistProvider : ISimilarItemsProvider<MusicArtist>
{
    private const int CacheDurationInDays = 14;

    private readonly ILibraryManager _libraryManager;
    private readonly IApplicationPaths _appPaths;
    private readonly IFileSystem _fileSystem;
    private readonly ListenBrainzLabsClient _labsClient;
    private readonly ILogger<ListenBrainzSimilarArtistProvider> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListenBrainzSimilarArtistProvider"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="appPaths">The application paths.</param>
    /// <param name="fileSystem">The file system.</param>
    /// <param name="labsClient">The ListenBrainz Labs API client.</param>
    /// <param name="logger">The logger.</param>
    public ListenBrainzSimilarArtistProvider(
        ILibraryManager libraryManager,
        IApplicationPaths appPaths,
        IFileSystem fileSystem,
        ListenBrainzLabsClient labsClient,
        ILogger<ListenBrainzSimilarArtistProvider> logger)
    {
        _libraryManager = libraryManager;
        _appPaths = appPaths;
        _fileSystem = fileSystem;
        _labsClient = labsClient;
        _logger = logger;
        _jsonOptions = JsonDefaults.Options;
    }

    /// <inheritdoc/>
    public string Name => "ListenBrainz";

    /// <inheritdoc/>
    public MetadataPluginType Type => MetadataPluginType.SimilarityProvider;

    /// <inheritdoc/>
    public IReadOnlyList<BaseItem> GetSimilarItems(MusicArtist item, SimilarItemsQuery query)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(query);

        // Extract MusicBrainz Artist ID
        if (!item.TryGetProviderId(MetadataProvider.MusicBrainzArtist, out var mbidStr) || !Guid.TryParse(mbidStr, out var mbid))
        {
            _logger.LogDebug("No MusicBrainz Artist ID found for {ArtistName}", item.Name);
            return [];
        }

        var requestedLimit = query.Limit ?? 10;

        return GetSimilarItemsWithLibraryCheckAsync(mbid, requestedLimit, query, CancellationToken.None)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
    }

    private async Task<List<BaseItem>> GetSimilarItemsWithLibraryCheckAsync(
        Guid artistMbid,
        int requestedLimit,
        SimilarItemsQuery query,
        CancellationToken cancellationToken)
    {
        var cachePath = GetCachePath(artistMbid);
        var results = new List<BaseItem>();
        var seenIds = new HashSet<Guid>(query.ExcludeItemIds);
        var providerName = MetadataProvider.MusicBrainzArtist.ToString();

        // Try to use cache first
        var cachedMbids = await TryReadCacheAsync(cachePath, cancellationToken).ConfigureAwait(false);
        if (cachedMbids is not null)
        {
            var libraryItems = FindItemsInLibrary(cachedMbids, providerName, query);
            foreach (var similarMbid in cachedMbids)
            {
                if (results.Count >= requestedLimit)
                {
                    break;
                }

                if (libraryItems.TryGetValue(similarMbid, out var foundItem) && seenIds.Add(foundItem.Id))
                {
                    results.Add(foundItem);
                }
            }

            return results;
        }

        // No valid cache - fetch from ListenBrainz API
        var allFetchedMbids = new List<Guid>();
        try
        {
            var similarMbids = await _labsClient.GetSimilarArtistsAsync(artistMbid, cancellationToken).ConfigureAwait(false);
            allFetchedMbids.AddRange(similarMbids);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch similar artists from ListenBrainz for {ArtistMbid}", artistMbid);
            return results;
        }

        var fetchedLibraryItems = FindItemsInLibrary(allFetchedMbids, providerName, query);
        foreach (var similarMbid in allFetchedMbids)
        {
            if (results.Count >= requestedLimit)
            {
                break;
            }

            if (fetchedLibraryItems.TryGetValue(similarMbid, out var foundItem) && seenIds.Add(foundItem.Id))
            {
                results.Add(foundItem);
            }
        }

        if (allFetchedMbids.Count > 0)
        {
            await SaveCacheAsync(cachePath, allFetchedMbids, cancellationToken).ConfigureAwait(false);
        }

        return results;
    }

    private Dictionary<Guid, BaseItem> FindItemsInLibrary(IReadOnlyList<Guid> mbids, string providerName, SimilarItemsQuery query)
    {
        if (mbids.Count == 0)
        {
            return [];
        }

        // Ensure ProviderIds field is included so we can map results back
        var dtoOptions = new DtoOptions(false) { Fields = [ItemFields.ProviderIds] };

        var mbQuery = new InternalItemsQuery(query.User)
        {
            IncludeItemTypes = [BaseItemKind.MusicArtist],
            HasAnyProviderIds = new Dictionary<string, string[]>
            {
                { providerName, mbids.Select(id => id.ToString()).ToArray() }
            },
            DtoOptions = dtoOptions
        };

        var items = _libraryManager.GetItemList(mbQuery);
        var result = new Dictionary<Guid, BaseItem>(items.Count);
        foreach (var item in items)
        {
            if (item.TryGetProviderId(MetadataProvider.MusicBrainzArtist, out var itemMbidStr)
                && Guid.TryParse(itemMbidStr, out var itemMbid))
            {
                result.TryAdd(itemMbid, item);
            }
        }

        return result;
    }

    private async Task<List<Guid>?> TryReadCacheAsync(string cachePath, CancellationToken cancellationToken)
    {
        var fileInfo = _fileSystem.GetFileSystemInfo(cachePath);
        if (!fileInfo.Exists || (DateTime.UtcNow - _fileSystem.GetLastWriteTimeUtc(fileInfo)).TotalDays > CacheDurationInDays)
        {
            return null;
        }

        try
        {
            var stream = File.OpenRead(cachePath);
            await using (stream.ConfigureAwait(false))
            {
                var cached = await JsonSerializer.DeserializeAsync<ListenBrainzSimilarCache>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false);
                if (cached?.SimilarMbids is not null)
                {
                    _logger.LogDebug("Using cached similar artists for {CachePath}", cachePath);
                    return cached.SimilarMbids;
                }
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to read ListenBrainz similar cache for {CachePath}", cachePath);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse ListenBrainz similar cache for {CachePath}", cachePath);
        }

        return null;
    }

    private async Task SaveCacheAsync(string cachePath, List<Guid> similarMbids, CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var cache = new ListenBrainzSimilarCache { SimilarMbids = similarMbids };
            var stream = File.Create(cachePath);
            await using (stream.ConfigureAwait(false))
            {
                await JsonSerializer.SerializeAsync(stream, cache, _jsonOptions, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to save ListenBrainz similar cache to {CachePath}", cachePath);
        }
    }

    private string GetCachePath(Guid artistMbid)
    {
        var dataPath = Path.Combine(_appPaths.CachePath, "listenbrainz-similar-artist", artistMbid.ToString());
        return Path.Combine(dataPath, "similar.json");
    }

    private sealed class ListenBrainzSimilarCache
    {
        public List<Guid>? SimilarMbids { get; set; }
    }
}
