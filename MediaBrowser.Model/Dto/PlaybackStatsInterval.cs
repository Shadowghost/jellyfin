namespace MediaBrowser.Model.Dto;

/// <summary>
/// The bucket size for the playback activity timeline.
/// </summary>
public enum PlaybackStatsInterval
{
    /// <summary>
    /// One bucket per day.
    /// </summary>
    Day = 0,

    /// <summary>
    /// One bucket per week.
    /// </summary>
    Week = 1,

    /// <summary>
    /// One bucket per month.
    /// </summary>
    Month = 2
}
