using System.Text.Json.Serialization;

namespace MediaBrowser.Providers.Plugins.Omdb.Dto;

/// <summary>
/// Represents the root object for OMDB season data.
/// </summary>
internal class SeasonRootObject
{
    /// <summary>
    /// Gets or sets the series title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the series IMDB ID.
    /// </summary>
    [JsonPropertyName("seriesID")]
    public string? SeriesID { get; set; }

    /// <summary>
    /// Gets or sets the season number.
    /// </summary>
    public int? Season { get; set; }

    /// <summary>
    /// Gets or sets the total number of seasons.
    /// </summary>
    [JsonPropertyName("totalSeasons")]
    public int? TotalSeasons { get; set; }

    /// <summary>
    /// Gets or sets the array of episodes in the season.
    /// </summary>
    public RootObject[]? Episodes { get; set; }

    /// <summary>
    /// Gets or sets the response status from OMDB API.
    /// </summary>
    public string? Response { get; set; }
}
