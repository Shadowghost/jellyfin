using System.Collections.Generic;
using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Frames;
using Jellyfin.MediaEncoding.Pipeline.Input;
using Jellyfin.MediaEncoding.Pipeline.Subtitles;

namespace Jellyfin.MediaEncoding.Pipeline;

/// <summary>
/// Translates device independent filters into the ones a single hardware vendor provides.
/// </summary>
public interface IHardwareAccelerator
{
    /// <summary>
    /// Gets the surface the accelerator filters on.
    /// </summary>
    FrameSurface Surface { get; }

    /// <summary>
    /// Gets a value indicating whether the chain declares the colour properties of its output up
    /// front, which most devices need because their filters do not carry the information along.
    /// </summary>
    bool DeclaresColorProperties => true;

    /// <summary>
    /// Gets the memory a hardware decoder leaves frames in, which is the surface the chain starts on.
    /// </summary>
    FrameSurface DecodeSurface => Surface;

    /// <summary>
    /// Gets the memory the encoder reads from, which differs from the filtering surface on the
    /// devices that are handed their frames by mapping.
    /// </summary>
    FrameSurface EncoderSurface => Surface;

    /// <summary>
    /// Gets the suffix the device's encoders are named with, or <c>null</c> when it has none.
    /// </summary>
    string? EncoderSuffix => null;

    /// <summary>
    /// Gets a value indicating whether the overlay settles the output format itself, so the picture
    /// does not need a pass of its own just to convert.
    /// </summary>
    bool OverlayPinsPixelFormat => false;

    /// <summary>
    /// Gets the format the device wants a subtitle uploaded in, or <c>null</c> when the subtitle is
    /// drawn over the picture in system memory.
    /// </summary>
    PixelFormat? SubtitleFormat => null;

    /// <summary>
    /// Gets the pixel format the device holds frames of this state in.
    /// </summary>
    /// <param name="state">The state of the frame.</param>
    /// <returns>The device format.</returns>
    PixelFormat GetDeviceFormat(FrameState state);

    /// <summary>
    /// Gets the pixel format the device's encoder reads.
    /// </summary>
    /// <param name="bitDepth">The bits per colour component the output carries.</param>
    /// <returns>The format.</returns>
    PixelFormat GetEncoderFormat(int bitDepth)
        => GetDeviceFormat(new FrameState { PixelFormat = bitDepth >= 10 ? PixelFormat.Yuv420p10le : PixelFormat.Yuv420p });

    /// <summary>
    /// Builds the devices the job runs on and the decoder that feeds it.
    /// </summary>
    /// <param name="request">What the job needs.</param>
    /// <returns>The plan.</returns>
    InputPlan CreateInputPlan(InputPlanRequest request) => InputPlan.Empty;

    /// <summary>
    /// Gets the chain to build when the source is decoded in system memory instead of on the device.
    /// </summary>
    /// <returns>The accelerator to build with.</returns>
    IHardwareAccelerator ForSoftwareDecode() => Accelerators.SoftwareAccelerator.Instance;

    /// <summary>
    /// Settles anything the device has to decide for the chain as a whole before any one filter is
    /// picked, such as a route through a second device that changes which filters are used at all.
    /// </summary>
    /// <param name="requested">The operations the transcode needs.</param>
    /// <param name="input">The state of the frame entering the chain.</param>
    /// <param name="capabilities">What ffmpeg and the device can do.</param>
    /// <returns>The accelerator to build with, and the order to run the operations in.</returns>
    (IHardwareAccelerator Accelerator, IReadOnlyList<IVideoFilter> Filters) Plan(
        IReadOnlyList<IVideoFilter> requested,
        FrameState input,
        IPipelineCapabilities capabilities) => (this, requested);

    /// <summary>
    /// Picks the best filter the device can run for the requested operation.
    /// </summary>
    /// <param name="filter">The requested, device independent filter.</param>
    /// <param name="state">The state of the frame entering the filter.</param>
    /// <param name="capabilities">What ffmpeg and the device can do.</param>
    /// <returns>
    /// The vendor filter, the requested one when the device cannot help, or <c>null</c> when the
    /// device drops the operation rather than leaving the frame to have it done in software.
    /// </returns>
    IVideoFilter? SelectFilter(IVideoFilter filter, FrameState state, IPipelineCapabilities capabilities);

    /// <summary>
    /// Gets the filter that converts a frame already on the device to the given format.
    /// </summary>
    /// <param name="format">The target pixel format.</param>
    /// <returns>The filter, or <c>null</c> when the device cannot convert on its own.</returns>
    IVideoFilter? CreateFormatFilter(PixelFormat format);

    /// <summary>
    /// Settles anything the device decides differently once it knows a subtitle is being burned in.
    /// </summary>
    /// <param name="subtitle">The subtitle being burned in.</param>
    /// <returns>The accelerator to build the picture chain with.</returns>
    IHardwareAccelerator ForOverlay(SubtitleSource subtitle) => this;

    /// <summary>
    /// Builds the filter that puts a prepared subtitle onto the device.
    /// </summary>
    /// <returns>The filter, or <c>null</c> when the overlay happens in system memory.</returns>
    IVideoFilter? CreateSubtitleUpload() => null;

    /// <summary>
    /// Builds the filter that draws the subtitle over the picture.
    /// </summary>
    /// <param name="size">The size of the frame the subtitle is drawn over.</param>
    /// <param name="subtitleIsRendered">Whether the subtitle was drawn onto a canvas of its own.</param>
    /// <returns>The overlay filter.</returns>
    IVideoFilter CreateOverlay(FrameSize size, bool subtitleIsRendered)
        => new OverlayFilter("overlay", FrameSurface.System);

    /// <summary>
    /// Builds the filter that moves a frame onto another surface, where the device does it
    /// differently from a plain upload, download or map.
    /// </summary>
    /// <param name="target">The surface to move the frame to.</param>
    /// <param name="state">The state of the frame.</param>
    /// <param name="reverse">Whether the frame is going back to the device it was mapped from.</param>
    /// <returns>The transfer filter, or <c>null</c> to let the builder pick one.</returns>
    IVideoFilter? CreateTransferFilter(FrameSurface target, FrameState state, bool reverse) => null;

    /// <summary>
    /// Merges two adjacent filters into one invocation where the vendor filter supports it.
    /// </summary>
    /// <param name="first">The earlier filter.</param>
    /// <param name="second">The later filter.</param>
    /// <returns>The merged filter, or <c>null</c> when they cannot be merged.</returns>
    IVideoFilter? TryFuse(IVideoFilter first, IVideoFilter second);
}
