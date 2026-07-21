using System;

namespace MediaBrowser.Model.Dto;

/// <summary>
/// A top-watched item entry.
/// </summary>
public class PlaybackStatsItemDto
{
    /// <summary>
    /// Gets or sets the live item id, or null if the item has been removed.
    /// </summary>
    public Guid? ItemId { get; set; }

    /// <summary>
    /// Gets or sets the item title (snapshot, so removed items remain readable).
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the number of plays (all recorded sessions, completed and partial).
    /// </summary>
    public int Plays { get; set; }

    /// <summary>
    /// Gets or sets the number of plays that reached completion.
    /// </summary>
    public int Completions { get; set; }

    /// <summary>
    /// Gets or sets the total watched span in ticks.
    /// </summary>
    public long WatchTimeTicks { get; set; }

    /// <summary>
    /// Gets or sets the most recent play date.
    /// </summary>
    public DateTime LastPlayed { get; set; }
}
