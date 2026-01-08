using System;
using System.Collections.Generic;
using System.IO;
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
        if (!item.TryGetProviderId(MetadataProvider.MusicBrainzArtist, out var mbidStr)
            || !Guid.TryParse(mbidStr, out var mbid))
        {
            _logger.LogDebug("No MusicBrainz Artist ID found for {ArtistName}", item.Name);
            return [];
        }

        var requestedLimit = query.Limit ?? 50;
        var similarMbids = GetSimilarMbidsAsync(mbid, CancellationToken.None)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

        if (similarMbids.Count == 0)
        {
            return [];
        }

        var results = new List<BaseItem>();
        var seenIds = new HashSet<Guid>(query.ExcludeItemIds);
        var providerName = MetadataProvider.MusicBrainzArtist.ToString();

        foreach (var similarMbid in similarMbids)
        {
            if (results.Count >= requestedLimit)
            {
                break;
            }

            var mbQuery = new InternalItemsQuery(query.User)
            {
                IncludeItemTypes = [BaseItemKind.MusicArtist],
                HasAnyProviderId = new Dictionary<string, string>
                {
                    { providerName, similarMbid.ToString() }
                },
                Limit = 1,
                DtoOptions = query.DtoOptions ?? new DtoOptions()
            };

            var items = _libraryManager.GetItemList(mbQuery);
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

        _logger.LogDebug("Found {Count} similar artists in library for {ArtistName}", results.Count, item.Name);

        return results;
    }

    private async Task<IReadOnlyList<Guid>> GetSimilarMbidsAsync(Guid artistMbid, CancellationToken cancellationToken)
    {
        var cachePath = GetCachePath(artistMbid);

        var fileInfo = _fileSystem.GetFileSystemInfo(cachePath);
        if (fileInfo.Exists
            && (DateTime.UtcNow - _fileSystem.GetLastWriteTimeUtc(fileInfo)).TotalDays <= CacheDurationInDays)
        {
            try
            {
                var stream = File.OpenRead(cachePath);
                await using (stream.ConfigureAwait(false))
                {
                    var cached = await JsonSerializer.DeserializeAsync<ListenBrainzSimilarCache>(
                        stream, _jsonOptions, cancellationToken).ConfigureAwait(false);
                    if (cached?.SimilarMbids is not null)
                    {
                        _logger.LogDebug("Using cached similar artists for {ArtistMbid}", artistMbid);
                        return cached.SimilarMbids;
                    }
                }
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Failed to read ListenBrainz similar cache for {ArtistMbid}", artistMbid);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse ListenBrainz similar cache for {ArtistMbid}", artistMbid);
            }
        }

        try
        {
            var similarMbids = await _labsClient.GetSimilarArtistsAsync(artistMbid, cancellationToken)
                .ConfigureAwait(false);

            var mbidList = new List<Guid>(similarMbids);

            if (mbidList.Count > 0)
            {
                await SaveCacheAsync(cachePath, mbidList, cancellationToken).ConfigureAwait(false);
            }

            return mbidList;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch similar artists from ListenBrainz for {ArtistMbid}", artistMbid);
            return [];
        }
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
