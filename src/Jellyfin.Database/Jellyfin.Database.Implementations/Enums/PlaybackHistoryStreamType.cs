namespace Jellyfin.Database.Implementations.Enums;

/// <summary>
/// The type of a stream captured in a playback history entry.
/// </summary>
public enum PlaybackHistoryStreamType
{
    /// <summary>
    /// A video stream.
    /// </summary>
    Video = 0,

    /// <summary>
    /// An audio stream.
    /// </summary>
    Audio = 1,

    /// <summary>
    /// A subtitle stream.
    /// </summary>
    Subtitle = 2
}
