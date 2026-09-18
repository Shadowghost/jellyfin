using Jellyfin.MediaEncoding.Pipeline.BitStreams;
using Jellyfin.MediaEncoding.Pipeline.Frames;
using MediaBrowser.Model.MediaEncoding.Hardware;

namespace Jellyfin.MediaEncoding.Pipeline;

/// <summary>
/// What the builder is allowed to ask about the installed ffmpeg and hardware.
/// </summary>
/// <remarks>
/// Deliberately narrower than the server side capability provider so the builder stays testable
/// without a probe and free of a dependency on the controller assembly.
/// </remarks>
public interface IPipelineCapabilities
{
    /// <summary>
    /// Whether ffmpeg was built with the given filter.
    /// </summary>
    /// <param name="filterName">The filter name.</param>
    /// <returns><c>true</c> if the filter exists, <c>false</c> otherwise.</returns>
    bool SupportsFilter(string filterName);

    /// <summary>
    /// Whether ffmpeg was built with the given encoder.
    /// </summary>
    /// <param name="encoderName">The encoder name.</param>
    /// <returns><c>true</c> if the encoder exists, <c>false</c> otherwise.</returns>
    bool SupportsEncoder(string encoderName);

    /// <summary>
    /// Whether ffmpeg was built with the given bitstream filter option.
    /// </summary>
    /// <param name="option">The option.</param>
    /// <returns><c>true</c> if the option exists, <c>false</c> otherwise.</returns>
    bool SupportsBitStreamFilterOption(BitStreamFilterOption option);

    /// <summary>
    /// Whether the device can run the given video processing operation on a frame of this size.
    /// </summary>
    /// <param name="kind">The operation.</param>
    /// <param name="size">The frame size.</param>
    /// <returns><c>true</c> if the device reports the operation, <c>false</c> otherwise.</returns>
    bool CanPerform(HwVppKind kind, FrameSize size);

    /// <summary>
    /// Whether the device accepts frames of the given format, either uploaded or produced by its own filters.
    /// </summary>
    /// <param name="format">The pixel format.</param>
    /// <returns><c>true</c> if the format is accepted, <c>false</c> otherwise.</returns>
    bool SupportsSurfaceFormat(PixelFormat format);
}
