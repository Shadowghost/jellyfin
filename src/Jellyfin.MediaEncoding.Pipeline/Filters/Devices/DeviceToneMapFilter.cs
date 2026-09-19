using System.Globalization;
using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Filters.Devices;

/// <summary>
/// A hardware tone mapper.
/// </summary>
/// <param name="Suffix">The device suffix of the filter, such as <c>cuda</c>.</param>
/// <param name="Surface">The surface the filter runs on.</param>
/// <param name="OutputFormat">The pixel format the frame is left in.</param>
/// <param name="Algorithm">The tone mapping algorithm.</param>
/// <param name="Peak">The signal peak.</param>
/// <param name="Desaturation">The desaturation strength.</param>
public sealed record DeviceToneMapFilter(
    string Suffix,
    FrameSurface Surface,
    PixelFormat OutputFormat,
    string Algorithm,
    double Peak,
    double Desaturation) : IVideoFilter
{
    /// <inheritdoc />
    public PixelFormat? RequiredInputFormat { get; init; }

    /// <inheritdoc />
    public FrameSurface? RequiredSurface => Surface;

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
        $"tonemap_{Suffix}=format={OutputFormat.Name}:p=bt709:t=bt709:m=bt709:tonemap={Algorithm}:peak={Peak}:desat={Desaturation}");
}
