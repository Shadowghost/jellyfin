using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Filters;

/// <summary>
/// One operation in a video filter chain.
/// </summary>
/// <remarks>
/// A filter is written device independently and is swapped for a vendor variant by
/// <see cref="IHardwareAccelerator.SelectFilter"/> while the chain is resolved.
/// </remarks>
public interface IVideoFilter
{
    /// <summary>
    /// Gets the surface the filter has to run on, or <c>null</c> when it runs on any.
    /// </summary>
    FrameSurface? RequiredSurface { get; }

    /// <summary>
    /// Gets the format the frame has to be in before the filter runs, or <c>null</c> when the
    /// filter takes whatever the device holds.
    /// </summary>
    PixelFormat? RequiredInputFormat => null;

    /// <summary>
    /// Gets a value indicating whether the filter guarantees the pixel format it leaves behind.
    /// </summary>
    bool PinsPixelFormat { get; }

    /// <summary>
    /// Decides whether the filter is needed at all, and refines it against the incoming frame.
    /// </summary>
    /// <param name="state">The state of the frame entering the filter.</param>
    /// <param name="capabilities">What ffmpeg and the device can do.</param>
    /// <returns>The filter to run, or <c>null</c> to drop it from the chain.</returns>
    IVideoFilter? Evaluate(FrameState state, IPipelineCapabilities capabilities);

    /// <summary>
    /// Reports the state of the frame leaving the filter.
    /// </summary>
    /// <param name="state">The state of the frame entering the filter.</param>
    /// <returns>The state of the frame leaving the filter.</returns>
    FrameState Apply(FrameState state);

    /// <summary>
    /// Renders the filter into its ffmpeg filtergraph form.
    /// </summary>
    /// <returns>The filter argument, or <c>null</c> when the filter emits nothing.</returns>
    string? ToFilterArgument();
}
