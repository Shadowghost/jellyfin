using Jellyfin.MediaEncoding.Pipeline;
using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Filters.Devices;

/// <summary>
/// A hardware filter that turns the picture the right way up.
/// </summary>
/// <param name="Argument">The filter and its options, as ffmpeg takes them.</param>
/// <param name="Surface">The surface the filter runs on.</param>
/// <param name="SwapsDimensions">Whether the width and height change places.</param>
public sealed record DeviceTransposeFilter(string Argument, FrameSurface Surface, bool SwapsDimensions) : IVideoFilter
{
    /// <inheritdoc />
    public FrameSurface? RequiredSurface => Surface;

    /// <inheritdoc />
    public bool PinsPixelFormat => false;

    /// <inheritdoc />
    public IVideoFilter Evaluate(FrameState state, IPipelineCapabilities capabilities) => this;

    /// <inheritdoc />
    public FrameState Apply(FrameState state) => SwapsDimensions
        ? state with { Size = new FrameSize(state.Size.Height, state.Size.Width) }
        : state;

    /// <inheritdoc />
    public string ToFilterArgument() => Argument;
}
