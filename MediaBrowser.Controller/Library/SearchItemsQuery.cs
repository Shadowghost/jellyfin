using System;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Querying;

namespace MediaBrowser.Controller.Library;

/// <summary>
/// Query object for searching items with full data and filtering.
/// </summary>
public class SearchItemsQuery
{
    /// <summary>
    /// Gets the search term.
    /// </summary>
    public required string SearchTerm { get; init; }

    /// <summary>
    /// Gets the user ID for user-specific searches.
    /// </summary>
    public Guid? UserId { get; init; }

    /// <summary>
    /// Gets the item types to include in the search.
    /// </summary>
    public BaseItemKind[] IncludeItemTypes { get; init; } = [];

    /// <summary>
    /// Gets the item types to exclude from the search.
    /// </summary>
    public BaseItemKind[] ExcludeItemTypes { get; init; } = [];

    /// <summary>
    /// Gets the media types to include in the search.
    /// </summary>
    public MediaType[] MediaTypes { get; init; } = [];

    /// <summary>
    /// Gets the maximum number of results to return.
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>
    /// Gets the starting index for paging.
    /// </summary>
    public int? StartIndex { get; init; }

    /// <summary>
    /// Gets the parent ID to scope the search.
    /// </summary>
    public Guid? ParentId { get; init; }

    /// <summary>
    /// Gets the filter for played status.
    /// </summary>
    public bool? IsPlayed { get; init; }

    /// <summary>
    /// Gets the filter for favorite status.
    /// </summary>
    public bool? IsFavorite { get; init; }

    /// <summary>
    /// Gets the filter for movie items.
    /// </summary>
    public bool? IsMovie { get; init; }

    /// <summary>
    /// Gets the filter for series items.
    /// </summary>
    public bool? IsSeries { get; init; }

    /// <summary>
    /// Gets the additional item filters.
    /// </summary>
    public ItemFilter[] Filters { get; init; } = [];

    /// <summary>
    /// Gets the genres to filter by.
    /// </summary>
    public string[]? Genres { get; init; }

    /// <summary>
    /// Gets the years to filter by.
    /// </summary>
    public int[]? Years { get; init; }
}
