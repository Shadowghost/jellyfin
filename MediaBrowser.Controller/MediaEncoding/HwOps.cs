using System;

namespace MediaBrowser.Controller.MediaEncoding;

/// <summary>
/// The per pixel operations a filter chain has to perform.
/// </summary>
[Flags]
public enum HwOps
{
    /// <summary>
    /// No operation.
    /// </summary>
    None = 0,

    /// <summary>
    /// Scaling and pixel format conversion.
    /// </summary>
    Scale = 1,

    /// <summary>
    /// Cropping, either to flatten frame packed 3D or to honour cropping metadata.
    /// </summary>
    Crop = 2,

    /// <summary>
    /// Deinterlacing.
    /// </summary>
    Deinterlace = 4,

    /// <summary>
    /// HDR to SDR tone mapping.
    /// </summary>
    Tonemap = 8,

    /// <summary>
    /// Burning subtitles into the picture.
    /// </summary>
    SubtitleOverlay = 16,

    /// <summary>
    /// Rotating the picture.
    /// </summary>
    Transpose = 32
}
