using System.Globalization;
using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Filters;

/// <summary>
/// Maps a high dynamic range frame down to standard dynamic range in system memory.
/// </summary>
/// <param name="Algorithm">The tone mapping algorithm.</param>
/// <param name="Desaturation">The desaturation strength.</param>
/// <param name="Peak">The signal peak, zero to let the filter detect it.</param>
/// <param name="OutputFormat">The pixel format the frame is left in.</param>
public sealed record ToneMapFilter(
    string Algorithm,
    double Desaturation,
    double Peak,
    PixelFormat OutputFormat) : IVideoFilter
{
    /// <inheritdoc />
    public FrameSurface? RequiredSurface => FrameSurface.System;

    /// <inheritdoc />
    public bool PinsPixelFormat => true;

    /// <inheritdoc />
    public IVideoFilter? Evaluate(FrameState state, IPipelineCapabilities capabilities)
        => state.HdrFormat == HdrFormat.None ? null : this;

    /// <inheritdoc />
    public FrameState Apply(FrameState state) => state with
    {
        PixelFormat = OutputFormat,
        HdrFormat = HdrFormat.None
    };

    /// <inheritdoc />
    public string ToFilterArgument() => string.Create(
        CultureInfo.InvariantCulture,
        $"tonemapx=tonemap={Algorithm}:desat={Desaturation}:peak={Peak}:t=bt709:m=bt709:p=bt709:format={OutputFormat.Name}");
}
