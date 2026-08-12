namespace MediaBrowser.Model.Dto;

/// <summary>
/// A single cell of the day-of-week × hour-of-day activity heatmap.
/// </summary>
public class PlaybackStatsHeatmapEntryDto
{
    /// <summary>
    /// Gets or sets the day of week (0 = Sunday … 6 = Saturday).
    /// </summary>
    public int DayOfWeek { get; set; }

    /// <summary>
    /// Gets or sets the hour of day (0–23).
    /// </summary>
    public int Hour { get; set; }

    /// <summary>
    /// Gets or sets the number of plays started in this slot.
    /// </summary>
    public int Plays { get; set; }

    /// <summary>
    /// Gets or sets the watched span started in this slot, in ticks.
    /// </summary>
    public long WatchTimeTicks { get; set; }
}
