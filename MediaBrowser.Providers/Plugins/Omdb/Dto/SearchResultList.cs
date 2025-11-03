using System.Collections.Generic;

namespace MediaBrowser.Providers.Plugins.Omdb.Dto;

internal class SearchResultList
{
    /// <summary>
    /// Gets or sets the results.
    /// </summary>
    /// <value>The results.</value>
    public List<SearchResult>? Search { get; set; }
}
