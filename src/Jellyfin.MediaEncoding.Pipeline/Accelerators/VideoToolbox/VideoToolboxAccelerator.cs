using System.Globalization;
using Jellyfin.MediaEncoding.Pipeline;
using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Filters.Devices;
using Jellyfin.MediaEncoding.Pipeline.Filters.Transfers;
using Jellyfin.MediaEncoding.Pipeline.Frames;
using Jellyfin.MediaEncoding.Pipeline.Input;
using MediaBrowser.Model.MediaEncoding.Hardware;

namespace Jellyfin.MediaEncoding.Pipeline.Accelerators.VideoToolbox;

/// <summary>
/// Runs the filter chain on an Apple VideoToolbox device.
/// </summary>
/// <param name="DeinterlaceMethod">The deinterlacer to ask the device for.</param>
/// <param name="DoubleRateDeinterlace">Whether the deinterlacer emits one frame per field.</param>
public sealed record VideoToolboxAccelerator(string DeinterlaceMethod = "yadif", bool DoubleRateDeinterlace = false)
    : IHardwareAccelerator
{
    private const string ScaleFilterName = "scale_vt";

    /// <inheritdoc />
    public FrameSurface Surface => FrameSurface.VideoToolbox;

    /// <inheritdoc />
    public string? EncoderSuffix => "videotoolbox";

    /// <summary>
    /// Gets a value indicating whether the chain declares its colour properties up front.
    /// </summary>
    /// <remarks>
    /// It does not: the VideoToolbox scaler carries the colour information with the frame, and the
    /// tone mapper rewrites it, so there is nothing for a <c>setparams</c> to fix.
    /// </remarks>
    public bool DeclaresColorProperties => false;

    /// <inheritdoc />
    /// <remarks>
    /// The device takes whatever the decoder produced, so nothing has to be pinned on it.
    /// </remarks>
    public PixelFormat GetDeviceFormat(FrameState state) => PixelFormat.Unknown;

    /// <inheritdoc />
    public PixelFormat GetEncoderFormat(int bitDepth)
        => bitDepth >= 10 ? PixelFormat.P010le : PixelFormat.Nv12;

    /// <inheritdoc />
    public InputPlan CreateInputPlan(InputPlanRequest request) => new()
    {
        Devices = [new HardwareDevice("videotoolbox", HardwareDeviceAliases.VideoToolbox)],
        Decoder = request.HardwareDecode
            ? new VideoDecoder("videotoolbox", FrameSurface.VideoToolbox, "videotoolbox") { StripDisplayRotation = request.Rotation != 0 }
            : null
    };

    /// <inheritdoc />
    public IVideoFilter? SelectFilter(IVideoFilter filter, FrameState state, IPipelineCapabilities capabilities)
    {
        switch (filter)
        {
            case ScaleFilter scale when scale.Request.Resolve(state.Size) == state.Size && !state.RequiresResize:
                return null;

            case ScaleFilter scale when capabilities.SupportsFilter(ScaleFilterName)
                && capabilities.CanPerform(HwVppKind.Scale, scale.Request.Resolve(state.Size)):
                return new DeviceScaleFilter(ScaleFilterName, Surface) { Size = scale.Request.Resolve(state.Size) };

            case TransposeFilter transpose when capabilities.SupportsFilter("transpose_vt"):
                return new DeviceTransposeFilter(
                    $"transpose_vt=dir={transpose.Direction}",
                    Surface,
                    transpose.SwapsDimensions);

            case DeinterlaceFilter when capabilities.CanPerform(HwVppKind.Deinterlace, state.Size):
                return SelectDeinterlacer(capabilities) ?? filter;

            case ToneMapFilter tonemap when capabilities.SupportsFilter("tonemap_videotoolbox")
                && capabilities.CanPerform(HwVppKind.Tonemap, state.Size):
                return new DeviceToneMapFilter(
                    "videotoolbox",
                    Surface,
                    PixelFormat.Nv12,
                    tonemap.Algorithm,
                    tonemap.Peak,
                    tonemap.Desaturation);

            default:
                return filter;
        }
    }

    /// <inheritdoc />
    public IVideoFilter CreateFormatFilter(PixelFormat format)
        => new DeviceScaleFilter(ScaleFilterName, Surface) { Format = format };

    /// <inheritdoc />
    /// <remarks>
    /// Frames always come home as 8 bit, whatever the device held them as.
    /// </remarks>
    public IVideoFilter? CreateTransferFilter(FrameSurface target, FrameState state, bool reverse)
        => target == FrameSurface.System && state.Surface == Surface
            ? new DownloadFilter(PixelFormat.Nv12)
            : null;

    /// <inheritdoc />
    public IVideoFilter? TryFuse(IVideoFilter first, IVideoFilter second)
        => first is DeviceScaleFilter a && second is DeviceScaleFilter b ? DeviceScaleFilter.Fuse(a, b) : null;

    private IVideoFilter? SelectDeinterlacer(IPipelineCapabilities capabilities)
    {
        var useBwdif = string.Equals(DeinterlaceMethod, "bwdif", System.StringComparison.Ordinal)
            && capabilities.SupportsFilter("bwdif_videotoolbox");
        var method = useBwdif ? "bwdif" : "yadif";

        if (!capabilities.SupportsFilter(method + "_videotoolbox"))
        {
            return null;
        }

        var rate = DoubleRateDeinterlace ? 1 : 0;
        return new DeviceDeinterlaceFilter(
            string.Create(CultureInfo.InvariantCulture, $"{method}_videotoolbox={rate}:-1:0"),
            Surface);
    }
}
