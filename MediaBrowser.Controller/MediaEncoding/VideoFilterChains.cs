using System.Collections.Generic;

namespace MediaBrowser.Controller.MediaEncoding;

/// <summary>
/// The three branches of a video filter graph.
/// </summary>
/// <param name="Main">The filters the picture goes through.</param>
/// <param name="Subtitle">The filters the subtitle goes through before it is drawn over the picture.</param>
/// <param name="Overlay">The filters that draw the subtitle over the picture, and anything after them.</param>
public sealed record VideoFilterChains(
    IReadOnlyList<string> Main,
    IReadOnlyList<string> Subtitle,
    IReadOnlyList<string> Overlay)
{
    /// <summary>
    /// Gets a graph with nothing in it.
    /// </summary>
    public static VideoFilterChains Empty { get; } = new([], [], []);
}
