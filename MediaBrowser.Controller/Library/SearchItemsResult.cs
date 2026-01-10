using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Controller.Library;

/// <summary>
/// Result from searching items with full data.
/// </summary>
public class SearchItemsResult
{
    /// <summary>
    /// Gets the items ordered by relevance score.
    /// </summary>
    public required IReadOnlyList<BaseItem> Items { get; init; }

    /// <summary>
    /// Gets the relevance scores for each item.
    /// </summary>
    public required IReadOnlyDictionary<Guid, float> Scores { get; init; }

    /// <summary>
    /// Gets the total record count before paging.
    /// </summary>
    public int TotalRecordCount { get; init; }
}
