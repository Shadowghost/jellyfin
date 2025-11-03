using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;

namespace MediaBrowser.Providers.Plugins.Omdb;

/// <summary>
/// OMDb episode metadata provider.
/// </summary>
public class OmdbEpisodeProvider : IRemoteMetadataProvider<Episode, EpisodeInfo>, IHasOrder
{
    private readonly OmdbItemProvider _itemProvider;
    private readonly OmdbProvider _omdbProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="OmdbEpisodeProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="fileSystem">Instance of the <see cref="IFileSystem"/> interface.</param>
    /// <param name="configurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    public OmdbEpisodeProvider(
        IHttpClientFactory httpClientFactory,
        ILibraryManager libraryManager,
        IFileSystem fileSystem,
        IServerConfigurationManager configurationManager)
    {
        _itemProvider = new OmdbItemProvider(httpClientFactory, libraryManager, fileSystem, configurationManager);
        _omdbProvider = new OmdbProvider(httpClientFactory, fileSystem, configurationManager);
    }

    /// <inheritdoc />
    public int Order => 1;

    /// <inheritdoc />
    public string Name => "The Open Movie Database";

    /// <inheritdoc />
    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
    {
        return _itemProvider.GetSearchResults(searchInfo, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Episode>
        {
            Item = new Episode(),
            QueriedById = true
        };

        // Allowing this will dramatically increase scan times
        if (info.IsMissingEpisode)
        {
            return result;
        }

        if (info.SeriesProviderIds.TryGetValue(MetadataProvider.Imdb.ToString(), out string? seriesImdbId)
            && !string.IsNullOrEmpty(seriesImdbId)
            && info.IndexNumber.HasValue)
        {
            result.HasMetadata = await _omdbProvider.FetchEpisodeData(
                result,
                info.IndexNumber.Value,
                info.ParentIndexNumber ?? 1,
                info.GetProviderId(MetadataProvider.Imdb),
                seriesImdbId,
                info.MetadataLanguage,
                info.MetadataCountryCode,
                cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return _itemProvider.GetImageResponse(url, cancellationToken);
    }
}
