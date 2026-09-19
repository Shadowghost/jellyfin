using System.Globalization;
using System.Text;
using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Subtitles;

/// <summary>
/// Renders a text subtitle, either onto its own canvas for an overlay or straight into the picture.
/// </summary>
/// <param name="Path">The subtitle file.</param>
/// <param name="OntoCanvas">
/// Whether the subtitle is rendered onto a transparent canvas of its own, which an overlay then
/// draws over the picture, rather than into the picture directly.
/// </param>
public sealed record TextSubtitleFilter(string Path, bool OntoCanvas) : IVideoFilter
{
    /// <summary>
    /// Gets the directory the fonts are loaded from.
    /// </summary>
    public string? FontsDirectory { get; init; }

    /// <summary>
    /// Gets the character set the file is read as.
    /// </summary>
    public string? CharacterEncoding { get; init; }

    /// <summary>
    /// Gets the seconds the subtitle is shifted by to follow a seek.
    /// </summary>
    public double SeekSeconds { get; init; }

    /// <summary>
    /// Gets a value indicating whether the timestamps are copied through, which leaves the shift out.
    /// </summary>
    public bool CopyTimestamps { get; init; }

    /// <inheritdoc />
    public FrameSurface? RequiredSurface => FrameSurface.System;

    /// <inheritdoc />
    public bool PinsPixelFormat => false;

    /// <inheritdoc />
    public IVideoFilter Evaluate(FrameState state, IPipelineCapabilities capabilities) => this;

    /// <inheritdoc />
    public FrameState Apply(FrameState state) => state;

    /// <inheritdoc />
    public string ToFilterArgument()
    {
        var argument = new StringBuilder("subtitles=f='").Append(Path).Append('\'');

        if (!string.IsNullOrEmpty(CharacterEncoding))
        {
            argument.Append(":charenc=").Append(CharacterEncoding);
        }

        if (OntoCanvas)
        {
            argument.Append(":alpha=1:sub2video=1");
        }

        if (!string.IsNullOrEmpty(FontsDirectory))
        {
            argument.Append(":fontsdir='").Append(FontsDirectory).Append('\'');
        }

        if (!CopyTimestamps)
        {
            argument.Append(CultureInfo.InvariantCulture, $",setpts=PTS -{SeekSeconds}/TB");
        }

        return argument.ToString();
    }
}
