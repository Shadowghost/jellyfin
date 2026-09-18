using Jellyfin.MediaEncoding.Pipeline;
using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Filters.Transfers;

/// <summary>
/// Hands a frame from one hardware surface to another without a round trip through system memory.
/// </summary>
/// <param name="TargetSurface">The surface to map onto.</param>
/// <param name="Reverse">Whether the mapping is reversed, as needed to get back to the source device.</param>
/// <param name="Options">The options the device needs on the map, already in ffmpeg's order.</param>
public sealed record MapFilter(FrameSurface TargetSurface, bool Reverse, string? Options = null) : IVideoFilter
{
    /// <inheritdoc />
    public FrameSurface? RequiredSurface => null;

    /// <inheritdoc />
    public bool PinsPixelFormat => TargetSurface.GetMappedFormatName() is not null;

    /// <inheritdoc />
    public IVideoFilter Evaluate(FrameState state, IPipelineCapabilities capabilities) => this;

    /// <inheritdoc />
    public FrameState Apply(FrameState state) => state with { Surface = TargetSurface };

    /// <inheritdoc />
    public string? ToFilterArgument()
    {
        var device = TargetSurface.GetDeviceName();
        if (device is null)
        {
            return null;
        }

        var map = $"hwmap=derive_device={device}";
        if (Reverse)
        {
            map += ":reverse=1";
        }

        if (!string.IsNullOrEmpty(Options))
        {
            map += ":" + Options;
        }

        var mappedFormat = TargetSurface.GetMappedFormatName();
        return mappedFormat is null ? map : map + ",format=" + mappedFormat;
    }
}
