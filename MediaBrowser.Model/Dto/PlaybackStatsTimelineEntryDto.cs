using System;

namespace MediaBrowser.Model.Dto;

/// <summary>
/// A single bucket of the playback activity timeline.
/// </summary>
public class PlaybackStatsTimelineEntryDto
{
    /// <summary>
    /// Gets or sets the bucket start date (UTC).
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// Gets or sets the number of plays (all recorded sessions) in the bucket.
    /// </summary>
    public int Plays { get; set; }

    /// <summary>
    /// Gets or sets the number of completions in the bucket.
    /// </summary>
    public int Completions { get; set; }

    /// <summary>
    /// Gets or sets the watched span in the bucket, in ticks.
    /// </summary>
    public long WatchTimeTicks { get; set; }
}
