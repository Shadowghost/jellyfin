namespace MediaBrowser.Model.MediaEncoding.Hardware;

/// <summary>
/// Enum containing the video processing operations a hardware device can report.
/// </summary>
public enum HwVppKind
{
    /// <summary>
    /// An operation that could not be mapped.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Scaling and pixel format conversion.
    /// </summary>
    Scale = 1,

    /// <summary>
    /// Deinterlacing.
    /// </summary>
    Deinterlace = 2,

    /// <summary>
    /// Overlaying a secondary input.
    /// </summary>
    Overlay = 3,

    /// <summary>
    /// Rotation.
    /// </summary>
    Rotate = 4,

    /// <summary>
    /// Horizontal or vertical flipping.
    /// </summary>
    Flip = 5,

    /// <summary>
    /// Noise reduction.
    /// </summary>
    Denoise = 6,

    /// <summary>
    /// Sharpening.
    /// </summary>
    Detail = 7,

    /// <summary>
    /// Colour balance.
    /// </summary>
    Procamp = 8,

    /// <summary>
    /// HDR tone mapping.
    /// </summary>
    Tonemap = 9,

    /// <summary>
    /// Frame rate conversion.
    /// </summary>
    Framerate = 10
}
