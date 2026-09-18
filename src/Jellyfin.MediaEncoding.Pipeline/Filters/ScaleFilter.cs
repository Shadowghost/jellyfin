using Jellyfin.MediaEncoding.Pipeline;
using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Filters;

/// <summary>
/// Scales a frame in system memory.
/// </summary>
/// <param name="Request">The size that was asked for.</param>
public sealed record ScaleFilter(ScalingRequest Request) : IVideoFilter
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScaleFilter"/> class for a fixed size.
    /// </summary>
    /// <param name="size">The size to scale to.</param>
    public ScaleFilter(FrameSize size)
        : this(new ScalingRequest(size.Width, size.Height, null, null))
    {
    }

    /// <summary>
    /// Gets the scaler to use, or <c>null</c> for the default one.
    /// </summary>
    public string? Flags { get; init; }

    /// <inheritdoc />
    public FrameSurface? RequiredSurface => FrameSurface.System;

    /// <inheritdoc />
    public bool PinsPixelFormat => false;

    /// <inheritdoc />
    public IVideoFilter? Evaluate(FrameState state, IPipelineCapabilities capabilities)
        => Request.IsRequested || state.RequiresResize ? this : null;

    /// <inheritdoc />
    public FrameState Apply(FrameState state) => state with
    {
        Size = Request.Resolve(state.Size),
               RequiresResize = false
    };

    /// <inheritdoc />
    public string? ToFilterArgument()
    {
        var argument = Request.ToSoftwareFilterArgument();

        return argument is null || string.IsNullOrEmpty(Flags) ? argument : argument + ":flags=" + Flags;
    }
}
