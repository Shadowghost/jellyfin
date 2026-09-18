using Jellyfin.MediaEncoding.Pipeline;
using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Filters;

/// <summary>
/// Declares the colour properties of the frames leaving the chain.
/// </summary>
/// <remarks>
/// It rewrites metadata only, so it neither moves the frame nor touches a pixel, and it runs
/// wherever the chain happens to be.
/// </remarks>
/// <param name="ColorPrimaries">The colour primaries to declare.</param>
/// <param name="ColorTransfer">The transfer characteristics to declare.</param>
/// <param name="ColorSpace">The matrix coefficients to declare.</param>
/// <param name="Range">The colour range to declare, or <c>null</c> to leave it alone.</param>
public sealed record SetParamsFilter(
    string ColorPrimaries,
    string ColorTransfer,
    string ColorSpace,
    string? Range = null) : IVideoFilter
{
    /// <summary>
    /// The properties of an SDR bt709 output.
    /// </summary>
    public static readonly SetParamsFilter Sdr = new("bt709", "bt709", "bt709");

    /// <summary>
    /// The properties of an HDR10 input.
    /// </summary>
    public static readonly SetParamsFilter Hdr10 = new("bt2020", "smpte2084", "bt2020nc");

    /// <summary>
    /// The properties of a hybrid log-gamma input.
    /// </summary>
    public static readonly SetParamsFilter Hlg = new("bt2020", "arib-std-b67", "bt2020nc");

    /// <inheritdoc />
    public FrameSurface? RequiredSurface => null;

    /// <inheritdoc />
    public bool PinsPixelFormat => false;

    /// <inheritdoc />
    public IVideoFilter Evaluate(FrameState state, IPipelineCapabilities capabilities) => this;

    /// <inheritdoc />
    public FrameState Apply(FrameState state) => state;

    /// <inheritdoc />
    public string ToFilterArgument()
    {
        var argument = $"setparams=color_primaries={ColorPrimaries}:color_trc={ColorTransfer}:colorspace={ColorSpace}";
        return Range is null ? argument : argument + ":range=" + Range;
    }
}
