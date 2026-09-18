using System.Collections.Generic;
using System.Linq;
using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Graph;

/// <summary>
/// A resolved chain of video filters and the state it leaves the frame in.
/// </summary>
/// <param name="Filters">The filters, in the order they run.</param>
/// <param name="OutputState">The state of the frame leaving the chain.</param>
public sealed record VideoFilterChain(IReadOnlyList<IVideoFilter> Filters, FrameState OutputState)
{
    /// <summary>
    /// An empty chain.
    /// </summary>
    /// <param name="state">The state of the frame.</param>
    /// <returns>The chain.</returns>
    public static VideoFilterChain Empty(FrameState state) => new([], state);

    /// <summary>
    /// Renders the chain into the form ffmpeg expects behind <c>-vf</c>.
    /// </summary>
    /// <returns>The filtergraph, empty when no filter emits anything.</returns>
    public string ToFilterArgument()
        => string.Join(',', Filters.Select(f => f.ToFilterArgument()).Where(a => !string.IsNullOrEmpty(a)));
}
