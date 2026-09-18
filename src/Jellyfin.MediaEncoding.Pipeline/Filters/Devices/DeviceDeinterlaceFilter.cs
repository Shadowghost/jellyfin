using Jellyfin.MediaEncoding.Pipeline;
using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Filters.Devices;

/// <summary>
/// A hardware deinterlacer.
/// </summary>
/// <param name="Argument">The filter and its options, as ffmpeg takes them.</param>
/// <param name="Surface">The surface the filter runs on.</param>
public sealed record DeviceDeinterlaceFilter(string Argument, FrameSurface Surface) : IVideoFilter
{
    /// <inheritdoc />
    public FrameSurface? RequiredSurface => Surface;

    /// <inheritdoc />
    public bool PinsPixelFormat => false;

    /// <inheritdoc />
    public IVideoFilter? Evaluate(FrameState state, IPipelineCapabilities capabilities)
        => state.IsInterlaced ? this : null;

    /// <inheritdoc />
    public FrameState Apply(FrameState state) => state with { IsInterlaced = false };

    /// <inheritdoc />
    public string ToFilterArgument() => Argument;
}
