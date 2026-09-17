using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaEncoding.Hardware;

namespace MediaBrowser.Controller.MediaEncoding;

/// <summary>
/// Reports what the installed hardware can actually decode, encode and filter.
/// </summary>
public interface IHardwareCapabilitiesProvider
{
    /// <summary>
    /// Gets a value indicating whether capabilities have been collected.
    /// </summary>
    /// <remarks>
    /// While this is <c>false</c> every query returns <c>true</c> so callers keep their static behaviour.
    /// </remarks>
    bool IsPopulated { get; }

    /// <summary>
    /// Collects the capabilities of the configured hardware acceleration type.
    /// </summary>
    /// <param name="ffmpegPath">The ffmpeg path.</param>
    /// <param name="ffprobePath">The ffprobe path.</param>
    /// <param name="encoderVersion">The ffmpeg version string used to key the cache.</param>
    /// <param name="canReportHwCaps">Whether ffprobe can report capabilities directly.</param>
    /// <param name="options">The encoding options.</param>
    void Refresh(string ffmpegPath, string ffprobePath, string encoderVersion, bool canReportHwCaps, EncodingOptions options);

    /// <summary>
    /// Gets the collected capabilities of the given hardware acceleration type.
    /// </summary>
    /// <param name="type">The hardware acceleration type.</param>
    /// <returns>The capabilities, or <c>null</c> when they were never collected.</returns>
    HwAccelCapabilities? GetCapabilities(HardwareAccelerationType type);

    /// <summary>
    /// Gets the device the given options select, or the first reported one.
    /// </summary>
    /// <param name="type">The hardware acceleration type.</param>
    /// <param name="options">The encoding options.</param>
    /// <returns>The device, or <c>null</c> when no device was reported.</returns>
    HwDeviceCapabilities? GetActiveDevice(HardwareAccelerationType type, EncodingOptions options);

    /// <summary>
    /// Whether the hardware can decode the given stream.
    /// </summary>
    /// <param name="type">The hardware acceleration type.</param>
    /// <param name="options">The encoding options.</param>
    /// <param name="codec">The codec name.</param>
    /// <param name="width">The frame width.</param>
    /// <param name="height">The frame height.</param>
    /// <param name="pixelFormat">The pixel format, if known.</param>
    /// <returns><c>true</c> if the stream can be decoded in hardware, <c>false</c> otherwise.</returns>
    bool CanDecode(HardwareAccelerationType type, EncodingOptions options, string? codec, int width, int height, string? pixelFormat);

    /// <summary>
    /// Whether the hardware can encode the given stream.
    /// </summary>
    /// <param name="type">The hardware acceleration type.</param>
    /// <param name="options">The encoding options.</param>
    /// <param name="codec">The codec name.</param>
    /// <param name="width">The frame width.</param>
    /// <param name="height">The frame height.</param>
    /// <returns><c>true</c> if the stream can be encoded in hardware, <c>false</c> otherwise.</returns>
    bool CanEncode(HardwareAccelerationType type, EncodingOptions options, string? codec, int width, int height);

    /// <summary>
    /// Whether the hardware can run the given video processing operation.
    /// </summary>
    /// <param name="type">The hardware acceleration type.</param>
    /// <param name="options">The encoding options.</param>
    /// <param name="kind">The operation.</param>
    /// <param name="width">The frame width.</param>
    /// <param name="height">The frame height.</param>
    /// <returns><c>true</c> if the operation can run in hardware, <c>false</c> otherwise.</returns>
    bool CanFilter(HardwareAccelerationType type, EncodingOptions options, HwVppKind kind, int width, int height);

    /// <summary>
    /// Records a hardware transcoding failure so similar requests skip hardware.
    /// </summary>
    /// <param name="type">The hardware acceleration type.</param>
    /// <param name="codec">The codec name.</param>
    /// <param name="width">The frame width.</param>
    /// <param name="height">The frame height.</param>
    void RecordDecodeFailure(HardwareAccelerationType type, string? codec, int width, int height);

    /// <summary>
    /// Whether a previous hardware transcode of a comparable stream failed.
    /// </summary>
    /// <param name="type">The hardware acceleration type.</param>
    /// <param name="codec">The codec name.</param>
    /// <param name="width">The frame width.</param>
    /// <param name="height">The frame height.</param>
    /// <returns><c>true</c> if hardware decoding is known to fail, <c>false</c> otherwise.</returns>
    bool IsDecodeKnownBad(HardwareAccelerationType type, string? codec, int width, int height);
}
