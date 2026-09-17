using System;
using System.Collections.Generic;

namespace MediaBrowser.Model.MediaEncoding.Hardware;

/// <summary>
/// The reported capabilities of a single hardware decoder or encoder.
/// </summary>
public class HwCodecCapabilities
{
    /// <summary>
    /// Gets or sets the codec name as reported by ffmpeg, e.g. <c>hevc</c>.
    /// </summary>
    public string CodecName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human readable codec description.
    /// </summary>
    public string? CodecDescription { get; set; }

    /// <summary>
    /// Gets or sets the minimum supported width, if reported.
    /// </summary>
    public int? MinWidth { get; set; }

    /// <summary>
    /// Gets or sets the minimum supported height, if reported.
    /// </summary>
    public int? MinHeight { get; set; }

    /// <summary>
    /// Gets or sets the maximum supported width, if reported.
    /// </summary>
    public int? MaxWidth { get; set; }

    /// <summary>
    /// Gets or sets the maximum supported height, if reported.
    /// </summary>
    public int? MaxHeight { get; set; }

    /// <summary>
    /// Gets the supported profile names.
    /// </summary>
    public IReadOnlyList<string> Profiles { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the supported pixel format names.
    /// </summary>
    public IReadOnlyList<string> PixelFormats { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Checks whether the given frame size fits within the reported limits.
    /// </summary>
    /// <param name="width">The frame width.</param>
    /// <param name="height">The frame height.</param>
    /// <returns><c>true</c> if the size is supported, <c>false</c> otherwise.</returns>
    public bool SupportsSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return true;
        }

        return width >= (MinWidth ?? 0)
            && height >= (MinHeight ?? 0)
            && width <= (MaxWidth ?? int.MaxValue)
            && height <= (MaxHeight ?? int.MaxValue);
    }
}
