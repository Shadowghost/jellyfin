using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MediaBrowser.Providers.Plugins.Omdb.Dto;

internal class SearchResultList
{
    /// <summary>
    /// Gets or sets the results.
    /// </summary>
    /// <value>The results.</value>
    public List<SearchResult>? Search { get; set; }

    /// <summary>
    /// Gets or sets the result count.
    /// </summary>
    /// <value>The result count.</value>
    [JsonPropertyName("totalResults")]
    public string? TotalResults { get; set; }

    /// <summary>
    /// Gets or sets the response status from OMDB API.
    /// </summary>
    public string? Response { get; set; }
}
