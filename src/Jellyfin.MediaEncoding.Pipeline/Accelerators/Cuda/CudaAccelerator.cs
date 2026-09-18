using System.Globalization;
using Jellyfin.MediaEncoding.Pipeline;
using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Filters.Devices;
using Jellyfin.MediaEncoding.Pipeline.Filters.Transfers;
using Jellyfin.MediaEncoding.Pipeline.Frames;
using Jellyfin.MediaEncoding.Pipeline.Input;
using Jellyfin.MediaEncoding.Pipeline.Subtitles;
using MediaBrowser.Model.MediaEncoding.Hardware;

namespace Jellyfin.MediaEncoding.Pipeline.Accelerators.Cuda;

/// <summary>
/// Runs the filter chain on an NVIDIA CUDA device.
/// </summary>
/// <param name="DeinterlaceMethod">The deinterlacer to ask the device for.</param>
/// <param name="DoubleRateDeinterlace">Whether the deinterlacer emits one frame per field.</param>
public sealed record CudaAccelerator(string DeinterlaceMethod = "yadif", bool DoubleRateDeinterlace = false)
    : IHardwareAccelerator
{
    private const string ScaleFilterName = "scale_cuda";

    /// <inheritdoc />
    public FrameSurface Surface => FrameSurface.Cuda;

    /// <inheritdoc />
    public string? EncoderSuffix => "nvenc";

    /// <inheritdoc />
    public PixelFormat? SubtitleFormat => PixelFormat.Yuva420p;

    /// <inheritdoc />
    public PixelFormat GetDeviceFormat(FrameState state) => PixelFormat.Yuv420p;

    /// <inheritdoc />
    public InputPlan CreateInputPlan(InputPlanRequest request) => new()
    {
        Devices = [new HardwareDevice("cuda", HardwareDeviceAliases.Cuda) { Spec = "0" }],
        FilterDeviceAlias = HardwareDeviceAliases.Cuda,
        Decoder = request.HardwareDecode
            ? new VideoDecoder("cuda", FrameSurface.Cuda, "cuda")
            {
                StripDisplayRotation = request.Rotation != 0,

                // The decoder may hand on frames the filters still hold, and it only runs on one thread.
                ExtraOptions = ["-hwaccel_flags +unsafe_output", "-threads 1"]
            }
            : null
    };

    /// <inheritdoc />
    public IVideoFilter CreateSubtitleUpload()
        => new UploadFilter(Surface, PixelFormat.Yuva420p, false, true);

    /// <inheritdoc />
    public IVideoFilter CreateOverlay(FrameSize size, bool subtitleIsRendered) => new OverlayFilter("overlay_cuda", Surface)
    {
        // Only the canvas the text subtitle is drawn onto carries premultiplied alpha.
        ExtraOptions = subtitleIsRendered ? "alpha_format=premultiplied" : null
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

            case TransposeFilter transpose when capabilities.SupportsFilter("transpose_cuda"):
                return new DeviceTransposeFilter(
                    $"transpose_cuda=dir={transpose.Direction}",
                    Surface,
                    transpose.SwapsDimensions);

            case DeinterlaceFilter deinterlace when capabilities.CanPerform(HwVppKind.Deinterlace, state.Size):
                return SelectDeinterlacer(deinterlace, capabilities);

            case ToneMapFilter tonemap when capabilities.SupportsFilter("tonemap_cuda")
                && capabilities.CanPerform(HwVppKind.Tonemap, state.Size):
                return new DeviceToneMapFilter(
                    "cuda",
                    Surface,
                    tonemap.OutputFormat,
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
    public IVideoFilter? TryFuse(IVideoFilter first, IVideoFilter second)
        => first is DeviceScaleFilter a && second is DeviceScaleFilter b ? DeviceScaleFilter.Fuse(a, b) : null;

    private IVideoFilter SelectDeinterlacer(DeinterlaceFilter requested, IPipelineCapabilities capabilities)
    {
        var useBwdif = string.Equals(DeinterlaceMethod, "bwdif", System.StringComparison.Ordinal)
            && capabilities.SupportsFilter("bwdif_cuda");
        var method = useBwdif ? "bwdif" : "yadif";

        if (!capabilities.SupportsFilter(method + "_cuda"))
        {
            return requested;
        }

        var rate = DoubleRateDeinterlace ? 1 : 0;
        return new DeviceDeinterlaceFilter(
            string.Create(CultureInfo.InvariantCulture, $"{method}_cuda={rate}:-1:0"),
            Surface);
    }
}
