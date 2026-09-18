using Jellyfin.MediaEncoding.Pipeline;
using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Filters;

/// <summary>
/// Converts a frame in system memory to a pixel format.
/// </summary>
/// <param name="Format">The target pixel format.</param>
public sealed record FormatFilter(PixelFormat Format) : IVideoFilter
{
    /// <inheritdoc />
    public FrameSurface? RequiredSurface => FrameSurface.System;

    /// <inheritdoc />
    public bool PinsPixelFormat => true;

    /// <inheritdoc />
    public IVideoFilter? Evaluate(FrameState state, IPipelineCapabilities capabilities)
        => state.PixelFormat == Format ? null : this;

    /// <inheritdoc />
    public FrameState Apply(FrameState state) => state with { PixelFormat = Format };

    /// <inheritdoc />
    public string ToFilterArgument() => $"format={Format.Name}";
}
