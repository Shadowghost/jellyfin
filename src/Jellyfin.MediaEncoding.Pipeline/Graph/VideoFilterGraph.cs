using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Jellyfin.MediaEncoding.Pipeline.Graph;

/// <summary>
/// A filter graph: the picture, optionally a subtitle drawn over it, and how the two are joined.
/// </summary>
/// <param name="Main">The chain the picture goes through.</param>
/// <param name="Subtitle">The chain the subtitle goes through, or <c>null</c> when nothing is drawn over the picture.</param>
/// <param name="Overlay">The chain that joins the two, and anything that follows it.</param>
public sealed record VideoFilterGraph(
    VideoFilterChain Main,
    VideoFilterChain? Subtitle,
    VideoFilterChain? Overlay)
{
    /// <summary>
    /// Gets a value indicating whether the subtitle is rendered by a filter rather than read from an input.
    /// </summary>
    public bool SubtitleIsRendered { get; init; }

    /// <summary>
    /// Renders the graph into the arguments ffmpeg takes.
    /// </summary>
    /// <param name="labels">Which inputs the branches read from.</param>
    /// <returns>A <c>-vf</c> or <c>-filter_complex</c> argument, empty when there is nothing to do.</returns>
    public string ToArgument(FilterGraphLabels labels)
    {
        var main = Main.ToFilterArgument();
        var overlay = Overlay?.ToFilterArgument();

        if (string.IsNullOrEmpty(overlay))
        {
            return string.IsNullOrEmpty(main) ? string.Empty : " -vf \"" + main + "\"";
        }

        var subtitle = Subtitle?.ToFilterArgument() ?? string.Empty;
        var culture = CultureInfo.InvariantCulture;

        // A rendered subtitle makes its own canvas, so it reads from no input.
        var subtitleBranch = SubtitleIsRendered
            ? subtitle
            : string.Format(culture, "[{0}:{1}]{2}", labels.SubtitleInputIndex, labels.SubtitleStreamIndex, subtitle);

        var pictureBranch = string.IsNullOrEmpty(main)
            ? string.Format(culture, "[0:{0}][sub]", labels.VideoStreamIndex)
            : string.Format(culture, "[0:{0}]{1}[main];[main][sub]", labels.VideoStreamIndex, main);

        return string.Format(culture, " -filter_complex \"{0}[sub];{1}{2}\"", subtitleBranch, pictureBranch, overlay);
    }
}
