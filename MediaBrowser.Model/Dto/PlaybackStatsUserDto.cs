using System;

namespace MediaBrowser.Model.Dto;

/// <summary>
/// Per-user playback statistics entry.
/// </summary>
public class PlaybackStatsUserDto
{
    /// <summary>
    /// Gets or sets the user id.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the user name (resolved at read time).
    /// </summary>
    public string? UserName { get; set; }

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
    /// Gets or sets the most recent activity date.
    /// </summary>
    public DateTime LastActivity { get; set; }
}
