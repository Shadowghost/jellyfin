using System;
using System.Collections.Generic;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Model.MediaEncoding.Hardware;

/// <summary>
/// The reported capabilities of one hardware acceleration type.
/// </summary>
public class HwAccelCapabilities
{
    /// <summary>
    /// Gets or sets the hardware acceleration type these capabilities belong to.
    /// </summary>
    public HardwareAccelerationType AccelerationType { get; set; }

    /// <summary>
    /// Gets or sets the ffmpeg version the capabilities were collected with.
    /// </summary>
    public string? EncoderVersion { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the capabilities were reported by ffprobe rather than probed.
    /// </summary>
    public bool IsReported { get; set; }

    /// <summary>
    /// Gets the devices of this type.
    /// </summary>
    public IReadOnlyList<HwDeviceCapabilities> Devices { get; init; } = Array.Empty<HwDeviceCapabilities>();
}
