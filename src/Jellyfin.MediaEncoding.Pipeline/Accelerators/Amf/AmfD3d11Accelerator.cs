using System;
using System.Globalization;
using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Filters.Devices;
using Jellyfin.MediaEncoding.Pipeline.Filters.Transfers;
using Jellyfin.MediaEncoding.Pipeline.Frames;
using Jellyfin.MediaEncoding.Pipeline.Input;
using Jellyfin.MediaEncoding.Pipeline.Subtitles;
using MediaBrowser.Model.MediaEncoding.Hardware;

namespace Jellyfin.MediaEncoding.Pipeline.Accelerators.Amf;

/// <summary>
/// Runs the filter chain for an AMD AMF encoder, which has no video processor of its own.
/// </summary>
/// <remarks>
/// The frame is decoded and encoded as a Direct3D 11 surface but filtered in OpenCL, mapped between
/// the two rather than copied, so the chain runs on one surface and ends on another.
/// </remarks>
/// <param name="DeinterlaceMethod">The deinterlacer to ask OpenCL for.</param>
/// <param name="DoubleRateDeinterlace">Whether the deinterlacer emits one frame per field.</param>
public sealed record AmfD3d11Accelerator(string DeinterlaceMethod = "yadif", bool DoubleRateDeinterlace = false)
    : IHardwareAccelerator
{
    private const string ScaleFilterName = "scale_opencl";

    /// <inheritdoc />
    public FrameSurface Surface => FrameSurface.OpenCl;

    /// <inheritdoc />
    public FrameSurface DecodeSurface => FrameSurface.D3d11;

    /// <inheritdoc />
    public FrameSurface EncoderSurface => FrameSurface.D3d11;

    /// <inheritdoc />
    public string? EncoderSuffix => "amf";

    /// <inheritdoc />
    public PixelFormat? SubtitleFormat => PixelFormat.YUVA420P;

    /// <inheritdoc />
    public PixelFormat GetDeviceFormat(FrameState state)
        => state.PixelFormat.BitDepth >= 10 ? PixelFormat.P010LE : PixelFormat.NV12;

    /// <inheritdoc />
    public InputPlan CreateInputPlan(InputPlanRequest request) => new()
    {
        Devices =
        [
            new HardwareDevice("d3d11va", HardwareDeviceAliases.D3d11va) { Spec = SelectD3d11Device(request) },
            new HardwareDevice("opencl", HardwareDeviceAliases.OpenCl) { SourceAlias = HardwareDeviceAliases.D3d11va }
        ],
        FilterDeviceAlias = HardwareDeviceAliases.OpenCl,
        Decoder = request.HardwareDecode
            ? new VideoDecoder("d3d11va", FrameSurface.D3d11, "d3d11") { StripDisplayRotation = request.Rotation != 0 }
            : null
    };

    /// <inheritdoc />
    public IHardwareAccelerator ForSoftwareDecode()
        => new CopyBackAccelerator(PixelFormat.NV12, FrameSurface.System, "amf");

    /// <inheritdoc />
    public IVideoFilter CreateSubtitleUpload()
        => new UploadFilter(Surface, PixelFormat.YUVA420P, false, true);

    /// <inheritdoc />
    public IVideoFilter CreateOverlay(FrameSize size, bool subtitleIsRendered) => new OverlayFilter("overlay_opencl", Surface)
    {
        // Only the canvas the text subtitle is drawn onto carries premultiplied alpha.
        ExtraOptions = subtitleIsRendered ? "alpha_format=premultiplied" : null
    };

    /// <inheritdoc />
    public IVideoFilter? SelectFilter(IVideoFilter filter, FrameState state, IPipelineCapabilities capabilities)
    {
        switch (filter)
        {
            case Flatten3DFilter crop when crop.OnDevice:
                return crop with { DeviceSurface = Surface };

            case ScaleFilter scale when scale.Request.Resolve(state.Size) == state.Size && !state.RequiresResize:
                return null;

            case ScaleFilter scale when capabilities.SupportsFilter(ScaleFilterName)
                && capabilities.CanPerform(HwVppKind.Scale, scale.Request.Resolve(state.Size), state.PixelFormat):
                return new DeviceScaleFilter(ScaleFilterName, Surface) { Size = scale.Request.Resolve(state.Size) };

            case TransposeFilter transpose when capabilities.SupportsFilter("transpose_opencl"):
                return new DeviceTransposeFilter(
                    $"transpose_opencl=dir={transpose.Direction}",
                    Surface,
                    transpose.SwapsDimensions);

            case DeinterlaceFilter when capabilities.CanPerform(HwVppKind.Deinterlace, state.Size, state.PixelFormat):
                return SelectDeinterlacer(capabilities) ?? filter;

            case ToneMapFilter tonemap when capabilities.SupportsFilter("tonemap_opencl")
                && capabilities.CanPerform(HwVppKind.Tonemap, state.Size, state.PixelFormat):
                return new DeviceToneMapFilter(
                    "opencl",
                    Surface,
                    GetDeviceFormat(state with { PixelFormat = tonemap.OutputFormat }),
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
    public IVideoFilter? CreateTransferFilter(FrameSurface target, FrameState state, bool reverse)
        => (target, state.Surface) switch
        {
            // OpenCL is derived from the Direct3D 11 device, so only a frame already on one of them
            // is mapped onto the other; anything else is uploaded the ordinary way.
            (FrameSurface.OpenCl, FrameSurface.D3d11) => new MapFilter(target, false, "mode=read"),
            (FrameSurface.D3d11, FrameSurface.OpenCl) => new MapFilter(target, false, "mode=write:reverse=1"),

            // Only OpenCL maps a frame into system memory; a Direct3D 11 one has to be copied.
            (FrameSurface.System, FrameSurface.OpenCl) => new DownloadFilter(state.PixelFormat, "hwmap=mode=read"),
            _ => null
        };

    /// <inheritdoc />
    public IVideoFilter? TryFuse(IVideoFilter first, IVideoFilter second)
        => first is DeviceScaleFilter a && second is DeviceScaleFilter b ? DeviceScaleFilter.Fuse(a, b) : null;

    /// <summary>
    /// Names the Direct3D 11 adapter to run on, by the index the capability report gave it, or by
    /// the vendor when nothing was reported.
    /// </summary>
    private static string SelectD3d11Device(InputPlanRequest request)
        => request.DeviceIndex is { } index
            ? index.ToString(CultureInfo.InvariantCulture)
            : ",vendor=0x1002";

    private IVideoFilter? SelectDeinterlacer(IPipelineCapabilities capabilities)
    {
        // OpenCL only gets a deinterlacer when ffmpeg has both, which is how the legacy chain tests it.
        if (!capabilities.SupportsFilter("yadif_opencl") || !capabilities.SupportsFilter("bwdif_opencl"))
        {
            return null;
        }

        var method = string.Equals(DeinterlaceMethod, "bwdif", StringComparison.Ordinal) ? "bwdif" : "yadif";
        var rate = DoubleRateDeinterlace ? 1 : 0;

        return new DeviceDeinterlaceFilter(
            string.Create(CultureInfo.InvariantCulture, $"{method}_opencl={rate}:-1:0"),
            Surface);
    }
}
