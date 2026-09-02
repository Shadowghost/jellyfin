using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Movie = MediaBrowser.Controller.Entities.Movies.Movie;

namespace MediaBrowser.Providers.Plugins.Tmdb.Movies;

/// <summary>
/// TMDb-based similar items provider for movies.
/// </summary>
public class TmdbMovieSimilarProvider : IRemoteSimilarItemsProvider<Movie>
{
    private readonly TmdbClientManager _tmdbClientManager;
    private readonly ILogger<TmdbMovieSimilarProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbMovieSimilarProvider"/> class.
    /// </summary>
    /// <param name="tmdbClientManager">The TMDb client manager.</param>
    /// <param name="logger">The logger.</param>
    public TmdbMovieSimilarProvider(TmdbClientManager tmdbClientManager, ILogger<TmdbMovieSimilarProvider> logger)
    {
        _tmdbClientManager = tmdbClientManager;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => TmdbUtils.ProviderName;

    /// <inheritdoc/>
    public MetadataPluginType Type => MetadataPluginType.SimilarityProvider;

    /// <inheritdoc/>
    public TimeSpan? CacheDuration
    {
        get
        {
            var days = Plugin.Instance?.Configuration.SimilarItemsCacheDays ?? 0;
            return days > 0 ? TimeSpan.FromDays(days) : null;
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<SimilarItemReference> GetSimilarItemsAsync(
        Movie item,
        SimilarItemsQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!item.TryGetProviderId(MetadataProvider.Tmdb, out var tmdbIdStr) || !int.TryParse(tmdbIdStr, CultureInfo.InvariantCulture, out var tmdbId))
        {
            yield break;
        }

        var providerName = MetadataProvider.Tmdb.ToString();
        var similarMovies = _tmdbClientManager
            .GetMovieSimilarAsync(tmdbId, null, cancellationToken)
            .StopOnError(ex => _logger.LogWarning(ex, "Failed to get similar movies from TMDb for {TmdbId}", tmdbId), cancellationToken);

        await foreach (var similar in similarMovies.ConfigureAwait(false))
        {
            yield return new SimilarItemReference
            {
                ProviderName = providerName,
                ProviderId = similar.Id.ToString(CultureInfo.InvariantCulture)
            };
        }
    }
}
