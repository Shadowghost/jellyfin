namespace Jellyfin.MediaEncoding.Pipeline.Frames;

/// <summary>
/// The high dynamic range format a video frame carries.
/// </summary>
public enum HdrFormat
{
    /// <summary>
    /// Standard dynamic range.
    /// </summary>
    None = 0,

    /// <summary>
    /// HDR10.
    /// </summary>
    Hdr10 = 1,

    /// <summary>
    /// HDR10+.
    /// </summary>
    Hdr10Plus = 2,

    /// <summary>
    /// Hybrid log-gamma.
    /// </summary>
    Hlg = 3,

    /// <summary>
    /// Dolby Vision.
    /// </summary>
    DolbyVision = 4
}
