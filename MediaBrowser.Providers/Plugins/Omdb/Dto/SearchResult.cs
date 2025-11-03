using System.Text.Json.Serialization;

namespace MediaBrowser.Providers.Plugins.Omdb.Dto;

/// <summary>
/// Represents a search result from OMDB API.
/// </summary>
internal class SearchResult
{
    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the release year.
    /// </summary>
    public string? Year { get; set; }

    /// <summary>
    /// Gets or sets the content rating.
    /// </summary>
    public string? Rated { get; set; }

    /// <summary>
    /// Gets or sets the release date.
    /// </summary>
    public string? Released { get; set; }

    /// <summary>
    /// Gets or sets the season number.
    /// </summary>
    public string? Season { get; set; }

    /// <summary>
    /// Gets or sets the episode number.
    /// </summary>
    public string? Episode { get; set; }

    /// <summary>
    /// Gets or sets the runtime.
    /// </summary>
    public string? Runtime { get; set; }

    /// <summary>
    /// Gets or sets the genre.
    /// </summary>
    public string? Genre { get; set; }

    /// <summary>
    /// Gets or sets the director.
    /// </summary>
    public string? Director { get; set; }

    /// <summary>
    /// Gets or sets the writer.
    /// </summary>
    public string? Writer { get; set; }

    /// <summary>
    /// Gets or sets the actors.
    /// </summary>
    public string? Actors { get; set; }

    /// <summary>
    /// Gets or sets the plot summary.
    /// </summary>
    public string? Plot { get; set; }

    /// <summary>
    /// Gets or sets the language.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Gets or sets the country of origin.
    /// </summary>
    public string? Country { get; set; }

    /// <summary>
    /// Gets or sets the awards.
    /// </summary>
    public string? Awards { get; set; }

    /// <summary>
    /// Gets or sets the poster URL.
    /// </summary>
    public string? Poster { get; set; }

    /// <summary>
    /// Gets or sets the Metascore.
    /// </summary>
    public string? Metascore { get; set; }

    /// <summary>
    /// Gets or sets the IMDB rating.
    /// </summary>
    [JsonPropertyName("imdbRating")]
    public string? ImdbRating { get; set; }

    /// <summary>
    /// Gets or sets the IMDB votes.
    /// </summary>
    [JsonPropertyName("imdbVotes")]
    public string? ImdbVotes { get; set; }

    /// <summary>
    /// Gets or sets the IMDB ID.
    /// </summary>
    [JsonPropertyName("imdbID")]
    public string? ImdbId { get; set; }

    /// <summary>
    /// Gets or sets the series IMDB ID.
    /// </summary>
    [JsonPropertyName("seriesID")]
    public string? SeriesId { get; set; }

    /// <summary>
    /// Gets or sets the type (movie, series, episode).
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// Gets or sets the response status from OMDB API.
    /// </summary>
    public string? Response { get; set; }
}
