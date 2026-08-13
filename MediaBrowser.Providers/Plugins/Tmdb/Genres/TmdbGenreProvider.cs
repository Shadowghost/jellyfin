using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Extensions;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace MediaBrowser.Providers.Plugins.Tmdb.Genres
{
    /// <summary>
    /// Gives a genre the id TMDb knows it by.
    /// </summary>
    /// <remarks>
    /// TMDb publishes its whole genre vocabulary rather than a search, so a genre is identified by its
    /// name alone, in the language the item was named in. That is what a genre taken from an NFO file or
    /// from another provider is otherwise missing, and it is what lets two names for one genre be
    /// recognised as the same genre after a library changes its metadata language.
    /// </remarks>
    public class TmdbGenreProvider : IRemoteMetadataProvider<Genre, ItemLookupInfo>
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly TmdbClientManager _tmdbClientManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="TmdbGenreProvider"/> class.
        /// </summary>
        /// <param name="httpClientFactory">The <see cref="IHttpClientFactory"/>.</param>
        /// <param name="tmdbClientManager">The <see cref="TmdbClientManager"/>.</param>
        public TmdbGenreProvider(IHttpClientFactory httpClientFactory, TmdbClientManager tmdbClientManager)
        {
            _httpClientFactory = httpClientFactory;
            _tmdbClientManager = tmdbClientManager;
        }

        /// <inheritdoc />
        public string Name => TmdbUtils.ProviderName;

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(ItemLookupInfo searchInfo, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(searchInfo);

            var tmdbId = await GetGenreIdAsync(searchInfo, cancellationToken).ConfigureAwait(false);
            if (tmdbId is null)
            {
                return [];
            }

            var result = new RemoteSearchResult
            {
                Name = searchInfo.Name,
                SearchProviderName = Name
            };

            result.SetProviderId(MetadataProvider.Tmdb, tmdbId.Value.ToString(CultureInfo.InvariantCulture));

            return [result];
        }

        /// <inheritdoc />
        public async Task<MetadataResult<Genre>> GetMetadata(ItemLookupInfo info, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(info);

            var result = new MetadataResult<Genre>();

            var tmdbId = await GetGenreIdAsync(info, cancellationToken).ConfigureAwait(false);
            if (tmdbId is null)
            {
                return result;
            }

            result.HasMetadata = true;

            // The name it already carries, so a refresh that replaces everything writes the same one
            // back: which language a genre is named in is the library's decision, not TMDb's.
            result.Item = new Genre { Name = info.Name };
            result.Item.SetProviderId(MetadataProvider.Tmdb, tmdbId.Value.ToString(CultureInfo.InvariantCulture));

            return result;
        }

        /// <inheritdoc />
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(new Uri(url), cancellationToken);
        }

        private async Task<int?> GetGenreIdAsync(ItemLookupInfo info, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(info.Name))
            {
                return null;
            }

            // Both lists, because the two share one id space and a mixed library holds genres from each.
            var movieGenres = await _tmdbClientManager.GetMovieGenresAsync(info.MetadataLanguage, cancellationToken).ConfigureAwait(false);
            var seriesGenres = await _tmdbClientManager.GetTvGenresAsync(info.MetadataLanguage, cancellationToken).ConfigureAwait(false);

            var names = new List<TmdbGenreName>(movieGenres.Count + seriesGenres.Count);
            foreach (var genre in movieGenres.Concat(seriesGenres))
            {
                if (genre.Name is not null)
                {
                    names.Add(new TmdbGenreName(genre.Name, genre.Id));
                }
            }

            var vocabulary = TmdbGenreVocabulary.Build(names);

            return vocabulary.TryGetValue(info.Name.GetCleanValue(), out var tmdbId) ? tmdbId : null;
        }
    }
}
