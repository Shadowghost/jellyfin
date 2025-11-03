using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;

namespace MediaBrowser.Providers.Plugins.Omdb;

/// <summary>
/// OMDb image provider.
/// </summary>
public class OmdbImageProvider : IRemoteImageProvider, IHasOrder
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OmdbProvider _omdbProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="OmdbImageProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
    /// <param name="fileSystem">Instance of the <see cref="IFileSystem"/> interface.</param>
    /// <param name="configurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    public OmdbImageProvider(IHttpClientFactory httpClientFactory, IFileSystem fileSystem, IServerConfigurationManager configurationManager)
    {
        _httpClientFactory = httpClientFactory;
        _omdbProvider = new OmdbProvider(_httpClientFactory, fileSystem, configurationManager);
    }

    /// <inheritdoc />
    public string Name => "The Open Movie Database";

    /// <inheritdoc />
    // After other internet providers, because they're better
    // But before fallback providers like screengrab
    public int Order => 90;

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        yield return ImageType.Primary;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        var imdbId = item.GetProviderId(MetadataProvider.Imdb);
        if (string.IsNullOrWhiteSpace(imdbId))
        {
            return [];
        }

        var rootObject = await _omdbProvider.GetRootObject(imdbId, cancellationToken).ConfigureAwait(false);

        if (rootObject is null || string.IsNullOrEmpty(rootObject.Poster))
        {
            return [];
        }

        // the poster url is sometimes higher quality than the poster api
        return
        [
            new RemoteImageInfo
            {
                ProviderName = Name,
                Url = rootObject.Poster
            }
        ];
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken);
    }

    /// <inheritdoc />
    public bool Supports(BaseItem item)
    {
        return item is Movie || item is Trailer || item is Episode;
    }
}
