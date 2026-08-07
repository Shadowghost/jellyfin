namespace Jellyfin.Database.Implementations.Enums;

/// <summary>
/// Where a playback history entry came from, separating sessions the server actually
/// observed from ones reconstructed after the fact.
/// </summary>
public enum PlaybackHistorySource
{
    /// <summary>
    /// Observed live by the server, with full timing and delivery detail.
    /// </summary>
    Recorded = 0,

    /// <summary>
    /// Reconstructed from the pre-existing watched status. Carries a play count and, at best, the
    /// date of the most recent play; it has no per-play timing, device, or stream information, so it
    /// must stay out of activity- and delivery-based statistics.
    /// </summary>
    Imported = 1
}
