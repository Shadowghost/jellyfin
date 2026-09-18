using System.Globalization;
using Jellyfin.MediaEncoding.Pipeline;
using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Subtitles;

/// <summary>
/// Makes the transparent canvas a rendered text subtitle is drawn onto.
/// </summary>
/// <param name="Size">The size of the frame the subtitle is drawn over.</param>
/// <param name="FrameRate">How many frames a second the canvas produces.</param>
/// <param name="StartTime">Where the canvas starts, as ffmpeg spells a timestamp.</param>
public sealed record AlphaSrcFilter(FrameSize Size, double FrameRate, string StartTime) : IVideoFilter
{
    /// <inheritdoc />
    public FrameSurface? RequiredSurface => FrameSurface.System;

    /// <inheritdoc />
    public bool PinsPixelFormat => false;

    /// <inheritdoc />
    public IVideoFilter Evaluate(FrameState state, IPipelineCapabilities capabilities) => this;

    /// <inheritdoc />
    public FrameState Apply(FrameState state) => state with { Size = Size };

    /// <inheritdoc />
    public string ToFilterArgument() => string.Create(
        CultureInfo.InvariantCulture,
        $"alphasrc=s={Size.Width}x{Size.Height}:r={FrameRate}:start='{StartTime}'");
}
