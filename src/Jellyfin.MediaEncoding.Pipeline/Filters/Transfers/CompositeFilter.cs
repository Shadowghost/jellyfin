using System.Collections.Generic;
using System.Linq;
using Jellyfin.MediaEncoding.Pipeline;
using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Filters.Transfers;

/// <summary>
/// Several filters that only make sense together, such as the hops a frame takes between two
/// devices that cannot map to one another directly.
/// </summary>
/// <param name="Arguments">The filters, in the order they run.</param>
/// <param name="TargetSurface">The surface the frame is on afterwards.</param>
/// <param name="TargetFormat">The format the frame is in afterwards, if the hops settle one.</param>
public sealed record CompositeFilter(
    IReadOnlyList<string> Arguments,
    FrameSurface TargetSurface,
    PixelFormat? TargetFormat = null) : IVideoFilter
{
    /// <inheritdoc />
    public FrameSurface? RequiredSurface => null;

    /// <inheritdoc />
    public bool PinsPixelFormat => TargetFormat.HasValue;

    /// <inheritdoc />
    public IVideoFilter Evaluate(FrameState state, IPipelineCapabilities capabilities) => this;

    /// <inheritdoc />
    public FrameState Apply(FrameState state)
    {
        var next = state with { Surface = TargetSurface };
        return TargetFormat.HasValue ? next with { PixelFormat = TargetFormat.Value } : next;
    }

    /// <inheritdoc />
    public string? ToFilterArgument()
        => Arguments.Count == 0 ? null : string.Join(',', Arguments.Where(a => !string.IsNullOrEmpty(a)));
}
