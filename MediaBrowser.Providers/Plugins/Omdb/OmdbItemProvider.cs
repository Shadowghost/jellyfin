using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using MediaBrowser.Providers.Plugins.Omdb.Converter;
using MediaBrowser.Providers.Plugins.Omdb.Dto;

namespace MediaBrowser.Providers.Plugins.Omdb;

/// <summary>
/// OMDb item metadata provider for movies, series, and trailers.
/// </summary>
public class OmdbItemProvider : IRemoteMetadataProvider<Series, SeriesInfo>,
    IRemoteMetadataProvider<Movie, MovieInfo>, IRemoteMetadataProvider<Trailer, TrailerInfo>, IHasOrder
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILibraryManager _libraryManager;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly OmdbProvider _omdbProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="OmdbItemProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="fileSystem">Instance of the <see cref="IFileSystem"/> interface.</param>
    /// <param name="configurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    public OmdbItemProvider(
        IHttpClientFactory httpClientFactory,
        ILibraryManager libraryManager,
        IFileSystem fileSystem,
        IServerConfigurationManager configurationManager)
    {
        _httpClientFactory = httpClientFactory;
        _libraryManager = libraryManager;
        _omdbProvider = new OmdbProvider(_httpClientFactory, fileSystem, configurationManager);

        _jsonOptions = new JsonSerializerOptions(JsonDefaults.Options);
        _jsonOptions.Converters.Add(new JsonOmdbNotAvailableStringConverter());
        _jsonOptions.Converters.Add(new JsonOmdbNotAvailableInt32Converter());
    }

    /// <inheritdoc />
    public string Name => "The Open Movie Database";

    /// <inheritdoc />
    public int Order => 2;

    /// <inheritdoc />
    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(TrailerInfo searchInfo, CancellationToken cancellationToken)
    {
        return GetSearchResultsInternal(searchInfo, true, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
    {
        return GetSearchResultsInternal(searchInfo, true, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
    {
        return GetSearchResultsInternal(searchInfo, true, cancellationToken);
    }

    /// <summary>
    /// Gets search results for episode information.
    /// </summary>
    /// <param name="searchInfo">The episode search information.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the search results.</returns>
    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
    {
        return GetSearchResultsInternal(searchInfo, true, cancellationToken);
    }

    private async Task<IEnumerable<RemoteSearchResult>> GetSearchResultsInternal(ItemLookupInfo searchInfo, bool isSearch, CancellationToken cancellationToken)
    {
        var type = searchInfo switch
        {
            EpisodeInfo => "episode",
            SeriesInfo => "series",
            _ => "movie"
        };

        var imdbId = searchInfo.GetProviderId(MetadataProvider.Imdb);
        int? indexNumberEnd = null;
        var urlQuery = new StringBuilder("plot=full&r=json");
        if (searchInfo is EpisodeInfo episodeSearchInfo && episodeSearchInfo is not null)
        {
            indexNumberEnd = episodeSearchInfo.IndexNumberEnd;
            episodeSearchInfo.SeriesProviderIds.TryGetValue(MetadataProvider.Imdb.ToString(), out imdbId);
            if (searchInfo.IndexNumber.HasValue)
            {
                urlQuery.Append("&Episode=").Append(searchInfo.IndexNumber.Value);
            }

            if (searchInfo.ParentIndexNumber.HasValue)
            {
                urlQuery.Append("&Season=").Append(searchInfo.ParentIndexNumber.Value);
            }
        }

        if (string.IsNullOrWhiteSpace(imdbId))
        {
            var name = searchInfo.Name;
            var year = searchInfo.Year;
            if (!string.IsNullOrWhiteSpace(name))
            {
                var parsedName = _libraryManager.ParseName(name);
                var yearInName = parsedName.Year;
                name = parsedName.Name;
                year ??= yearInName;
            }

            if (year.HasValue)
            {
                urlQuery.Append("&y=").Append(year);
            }

            // &s means search and returns a list of results as opposed to t
            urlQuery.Append(isSearch ? "&s=" : "&t=");
            urlQuery.Append(WebUtility.UrlEncode(name));
            urlQuery.Append("&type=")
                .Append(type);
        }
        else
        {
            urlQuery.Append("&i=")
                .Append(imdbId);
            isSearch = false;
        }

        var url = OmdbProvider.GetOmdbUrl(urlQuery.ToString());

        using var response = await _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (isSearch)
        {
            var searchResultList = await response.Content.ReadFromJsonAsync<SearchResultList>(_jsonOptions, cancellationToken).ConfigureAwait(false);
            if (searchResultList?.Search is not null)
            {
                var resultCount = searchResultList.Search.Count;
                var result = new RemoteSearchResult[resultCount];
                for (var i = 0; i < resultCount; i++)
                {
                    result[i] = ResultToMetadataResult(searchResultList.Search[i], searchInfo, indexNumberEnd);
                }

                return result;
            }
        }
        else
        {
            var result = await response.Content.ReadFromJsonAsync<SearchResult>(_jsonOptions, cancellationToken).ConfigureAwait(false);
            if (string.Equals(result?.Response, "true", StringComparison.OrdinalIgnoreCase))
            {
                return [ResultToMetadataResult(result!, searchInfo, indexNumberEnd)];
            }
        }

        return [];
    }

    /// <inheritdoc />
    public Task<MetadataResult<Trailer>> GetMetadata(TrailerInfo info, CancellationToken cancellationToken)
    {
        return GetResult<Trailer>(info, cancellationToken);
    }

    /// <inheritdoc />
    public Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
    {
        return GetResult<Series>(info, cancellationToken);
    }

    /// <inheritdoc />
    public Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
    {
        return GetResult<Movie>(info, cancellationToken);
    }

    private RemoteSearchResult ResultToMetadataResult(SearchResult result, ItemLookupInfo searchInfo, int? indexNumberEnd)
    {
        var item = new RemoteSearchResult
        {
            IndexNumber = searchInfo.IndexNumber,
            Name = result.Title,
            ParentIndexNumber = searchInfo.ParentIndexNumber,
            SearchProviderName = Name,
            IndexNumberEnd = indexNumberEnd
        };

        if (!string.IsNullOrEmpty(result.ImdbId))
        {
            item.SetProviderId(MetadataProvider.Imdb, result.ImdbId);
        }

        if (OmdbProvider.TryParseYear(result.Year, out var parsedYear))
        {
            item.ProductionYear = parsedYear;
        }

        if (!string.IsNullOrEmpty(result.Released)
            && DateTime.TryParse(result.Released, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var released))
        {
            item.PremiereDate = released;
        }

        if (!string.IsNullOrWhiteSpace(result.Poster))
        {
            item.ImageUrl = result.Poster;
        }

        return item;
    }

    private async Task<MetadataResult<T>> GetResult<T>(ItemLookupInfo info, CancellationToken cancellationToken)
        where T : BaseItem, new()
    {
        var result = new MetadataResult<T>
        {
            Item = new T(),
            QueriedById = true
        };

        var imdbId = info.GetProviderId(MetadataProvider.Imdb);
        if (string.IsNullOrWhiteSpace(imdbId))
        {
            imdbId = await GetImdbId(info, cancellationToken).ConfigureAwait(false);
            result.QueriedById = false;
        }

        if (!string.IsNullOrEmpty(imdbId))
        {
            result.Item.SetProviderId(MetadataProvider.Imdb, imdbId);
            result.HasMetadata = true;

            await _omdbProvider.Fetch(result, imdbId, info.MetadataLanguage, info.MetadataCountryCode, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private async Task<string?> GetImdbId(ItemLookupInfo info, CancellationToken cancellationToken)
    {
        var results = await GetSearchResultsInternal(info, false, cancellationToken).ConfigureAwait(false);
        var first = results.FirstOrDefault();
        return first?.GetProviderId(MetadataProvider.Imdb);
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken);
    }
}
