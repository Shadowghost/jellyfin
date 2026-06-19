namespace MediaBrowser.Model.Dto;

/// <summary>
/// Headline playback statistics for a filter window.
/// </summary>
public class PlaybackStatsSummaryDto
{
    /// <summary>
    /// Gets or sets the number of plays (all recorded sessions, completed and partial).
    /// </summary>
    public int Plays { get; set; }

    /// <summary>
    /// Gets or sets the number of plays that reached completion.
    /// </summary>
    public int Completions { get; set; }

    /// <summary>
    /// Gets or sets the number of sessions that were transcoded.
    /// </summary>
    public int TranscodedPlays { get; set; }

    /// <summary>
    /// Gets or sets the total watched span (sum of stop-minus-start positions) in ticks.
    /// </summary>
    public long TotalWatchTimeTicks { get; set; }

    /// <summary>
    /// Gets or sets the number of distinct days that had any playback (the denominator of
    /// <see cref="AverageDailyWatchTimeTicks"/>).
    /// </summary>
    public int ActiveDays { get; set; }

    /// <summary>
    /// Gets or sets the average watch time per active day (total watch time divided by the number
    /// of distinct days that had any playback), in ticks.
    /// </summary>
    public long AverageDailyWatchTimeTicks { get; set; }

    /// <summary>
    /// Gets or sets the number of distinct items played.
    /// </summary>
    public int UniqueItems { get; set; }

    /// <summary>
    /// Gets or sets the number of distinct users with activity.
    /// </summary>
    public int ActiveUsers { get; set; }

    /// <summary>
    /// Gets or sets the average network bitrate across sessions that reported one, in bits per second.
    /// </summary>
    public long AverageBitrate { get; set; }

    /// <summary>
    /// Gets or sets the estimated total data transferred over the network, in bytes
    /// (sum of bitrate × watch time across sessions that reported a bitrate).
    /// </summary>
    public long TotalDataTransferredBytes { get; set; }
}
