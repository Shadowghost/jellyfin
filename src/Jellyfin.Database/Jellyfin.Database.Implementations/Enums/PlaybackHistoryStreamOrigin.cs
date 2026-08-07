namespace Jellyfin.Database.Implementations.Enums;

/// <summary>
/// Whether a captured stream represents what the user selected from the source
/// or what was actually delivered to the device after any transcoding.
/// </summary>
public enum PlaybackHistoryStreamOrigin
{
    /// <summary>
    /// The stream the user selected from the media source.
    /// </summary>
    Source = 0,

    /// <summary>
    /// The stream actually delivered to the device (post-transcode).
    /// </summary>
    Delivered = 1
}
