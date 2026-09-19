using System;
using System.Globalization;
using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Subtitles;

/// <summary>
/// Fits a bitmap subtitle to the frame the chain produces.
/// </summary>
/// <param name="TargetSize">The size of the frame the subtitle is drawn over.</param>
/// <param name="SubtitleSize">The size the subtitle arrives at, if it is known.</param>
public sealed record GraphicalSubtitleScaleFilter(FrameSize TargetSize, FrameSize? SubtitleSize) : IVideoFilter
{
    /// <inheritdoc />
    public FrameSurface? RequiredSurface => FrameSurface.System;

    /// <inheritdoc />
    public bool PinsPixelFormat => false;

    /// <inheritdoc />
    public IVideoFilter Evaluate(FrameState state, IPipelineCapabilities capabilities) => this;

    /// <inheritdoc />
    public FrameState Apply(FrameState state) => state with { Size = TargetSize };

    /// <inheritdoc />
    public string ToFilterArgument()
    {
        var culture = CultureInfo.InvariantCulture;

        // A subtitle shaped like the frame only needs resizing; anything else is padded onto it.
        if (SubtitleSize is { } size && size.Width > 0 && size.Height > 0 && SharesAspect(size))
        {
            return string.Format(culture, "scale,scale={0}:{1}:fast_bilinear", TargetSize.Width, TargetSize.Height);
        }

        return string.Format(
            culture,
            @"scale,scale=-1:{1}:fast_bilinear,crop,pad=max({0}\,iw):max({1}\,ih):(ow-iw)/2:(oh-ih)/2:black@0,crop={0}:{1}",
            TargetSize.Width,
            TargetSize.Height);
    }

    private bool SharesAspect(FrameSize size)
    {
        var frameAspect = (double)TargetSize.Width / TargetSize.Height;
        var subtitleAspect = (double)size.Width / size.Height;

        return Math.Abs(frameAspect - subtitleAspect) < 0.01f;
    }
}
