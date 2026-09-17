using System;
using System.Collections.Generic;
using System.Linq;

namespace MediaBrowser.Model.MediaEncoding.Hardware;

/// <summary>
/// The reported capabilities of a single hardware device.
/// </summary>
public class HwDeviceCapabilities
{
    /// <summary>
    /// Gets or sets the device index, as used by cuda and d3d11va.
    /// </summary>
    public int? Index { get; set; }

    /// <summary>
    /// Gets or sets the DRM render node backing this device, as used by vaapi and qsv on Linux.
    /// </summary>
    public string? DrmPath { get; set; }

    /// <summary>
    /// Gets or sets the device name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the driver name.
    /// </summary>
    public string? DriverName { get; set; }

    /// <summary>
    /// Gets or sets the driver version.
    /// </summary>
    public string? DriverVersion { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an OpenCL context can be derived from this device.
    /// </summary>
    public bool SupportsOpenCl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a Vulkan context can be derived from this device.
    /// </summary>
    public bool SupportsVulkan { get; set; }

    /// <summary>
    /// Gets the supported hardware decoders.
    /// </summary>
    public IReadOnlyList<HwCodecCapabilities> Decoders { get; init; } = Array.Empty<HwCodecCapabilities>();

    /// <summary>
    /// Gets the supported hardware encoders.
    /// </summary>
    public IReadOnlyList<HwCodecCapabilities> Encoders { get; init; } = Array.Empty<HwCodecCapabilities>();

    /// <summary>
    /// Gets the supported hardware video processing operations.
    /// </summary>
    public IReadOnlyList<HwFilterCapabilities> Filters { get; init; } = Array.Empty<HwFilterCapabilities>();

    /// <summary>
    /// Gets the decoder entry for the given codec, if any.
    /// </summary>
    /// <param name="codec">The codec name.</param>
    /// <returns>The decoder capabilities, or <c>null</c>.</returns>
    public HwCodecCapabilities? GetDecoder(string codec)
        => Decoders.FirstOrDefault(d => string.Equals(d.CodecName, codec, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets the encoder entry for the given codec, if any.
    /// </summary>
    /// <param name="codec">The codec name.</param>
    /// <returns>The encoder capabilities, or <c>null</c>.</returns>
    public HwCodecCapabilities? GetEncoder(string codec)
        => Encoders.FirstOrDefault(e => string.Equals(e.CodecName, codec, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets the filter entry for the given operation, if any.
    /// </summary>
    /// <param name="kind">The operation.</param>
    /// <returns>The filter capabilities, or <c>null</c>.</returns>
    public HwFilterCapabilities? GetFilter(HwVppKind kind)
        => Filters.FirstOrDefault(f => f.Kind == kind);
}
