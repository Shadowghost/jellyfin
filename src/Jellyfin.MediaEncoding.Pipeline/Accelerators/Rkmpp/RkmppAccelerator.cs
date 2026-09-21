using System.Collections.Generic;
using System.Linq;
using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Filters.Devices;
using Jellyfin.MediaEncoding.Pipeline.Filters.Transfers;
using Jellyfin.MediaEncoding.Pipeline.Frames;
using Jellyfin.MediaEncoding.Pipeline.Input;
using Jellyfin.MediaEncoding.Pipeline.Subtitles;
using MediaBrowser.Model.MediaEncoding.Hardware;

namespace Jellyfin.MediaEncoding.Pipeline.Accelerators.Rkmpp;

/// <summary>
/// Runs the filter chain on a Rockchip MPP device, whose video processor is RGA.
/// </summary>
/// <param name="Afbc">
/// Whether the frames stay compressed, which the device only allows when they never leave it.
/// </param>
/// <param name="MjpegEncoder">Whether the encoder is MJPEG, which the device feeds through RGB.</param>
public sealed record RkmppAccelerator(bool Afbc, bool MjpegEncoder = false) : IHardwareAccelerator
{
    private const string ScaleFilterName = "vpp_rkrga";

    /// <inheritdoc />
    public FrameSurface Surface => FrameSurface.Rkmpp;

    /// <inheritdoc />
    public string? EncoderSuffix => "rkmpp";

    /// <inheritdoc />
    public PixelFormat? SubtitleFormat => PixelFormat.BGRA;

    /// <inheritdoc />
    public bool OverlayPinsPixelFormat => true;

    /// <inheritdoc />
    public FrameSurface DecodeSurface => FrameSurface.Rkmpp;

    /// <inheritdoc />
    public PixelFormat GetDeviceFormat(FrameState state)
        => state.PixelFormat.BitDepth >= 10 ? PixelFormat.P010LE : PixelFormat.NV12;

    /// <inheritdoc />
    public InputPlan CreateInputPlan(InputPlanRequest request)
    {
        var devices = new List<HardwareDevice> { new("rkmpp", HardwareDeviceAliases.Rkmpp) };
        string? filterDevice = null;

        // A hardware decoded frame derives its OpenCL device from the one it already lives on.
        if (request.NeedsOpenCl && !request.HardwareDecode)
        {
            devices.Add(new HardwareDevice("opencl", HardwareDeviceAliases.OpenCl) { SourceAlias = HardwareDeviceAliases.Rkmpp });
            filterDevice = HardwareDeviceAliases.OpenCl;
        }

        return new InputPlan
        {
            Devices = devices,
            FilterDeviceAlias = filterDevice,
            Decoder = request.HardwareDecode
                ? new VideoDecoder("rkmpp", FrameSurface.Rkmpp, "drm_prime") { StripDisplayRotation = request.Rotation != 0, ExtraOptions = ["-afbc rga"] }
                : null
        };
    }

    /// <inheritdoc />
    public IHardwareAccelerator ForSoftwareDecode()
        => new CopyBackAccelerator(PixelFormat.NV12, FrameSurface.System, "rkmpp", "fast_bilinear");

    /// <inheritdoc />
    /// <remarks>
    /// The MJPEG encoder on the newer chips will not take RGB from the same pass that scales, so the
    /// conversion gets one of its own.
    /// </remarks>
    public (IHardwareAccelerator Accelerator, IReadOnlyList<IVideoFilter> Filters) Plan(
        IReadOnlyList<IVideoFilter> requested,
        FrameState input,
        IPipelineCapabilities capabilities)
    {
        if (!MjpegEncoder)
        {
            return (this, requested);
        }

        var first = new DeviceScaleFilter(ScaleFilterName, Surface)
        {
            Format = PixelFormat.BGRA,
            ExtraOptions = "afbc=1",
            Fusable = false
        };

        // It goes behind the colour declaration, which touches nothing and leads the chain.
        var planned = requested.ToList();
        planned.Insert(planned.FindIndex(f => f is not SetParamsFilter) is var at && at < 0 ? planned.Count : at, first);

        return (this, planned);
    }

    /// <inheritdoc />
    public IVideoFilter? SelectFilter(IVideoFilter filter, FrameState state, IPipelineCapabilities capabilities)
    {
        switch (filter)
        {
            case ScaleFilter scale when scale.Request.Resolve(state.Size) == state.Size && !state.RequiresResize:
                return null;

            case ScaleFilter scale when capabilities.SupportsFilter(ScaleFilterName)
                && capabilities.CanPerform(HwVppKind.Scale, scale.Request.Resolve(state.Size), state.PixelFormat):
                return new DeviceScaleFilter(ScaleFilterName, Surface)
                {
                    Size = scale.Request.Resolve(state.Size),
                    Format = GetDeviceFormat(state),
                    ExtraOptions = Afbc ? "afbc=1" : null
                };

            // The video processor turns the picture as part of its own pass.
            case TransposeFilter transpose:
                return new DeviceScaleFilter(ScaleFilterName, Surface)
                {
                    ExtraOptions = $"transpose={transpose.Direction}",
                    SwapsOutputDimensions = transpose.SwapsDimensions
                };

            // The rkmpp decoder deinterlaces on its own, and a round trip home is not worth one
            // for the software decoded frames it does not see.
            case DeinterlaceFilter:
                return null;

            case ToneMapFilter tonemap when capabilities.SupportsFilter("tonemap_opencl"):
                return new DeviceToneMapFilter(
                    "opencl",
                    FrameSurface.OpenCl,
                    GetDeviceFormat(state with { PixelFormat = tonemap.OutputFormat }),
                    tonemap.Algorithm,
                    tonemap.Peak,
                    tonemap.Desaturation)
                {
                    RequiredInputFormat = PixelFormat.P010LE
                };

            default:
                return filter;
        }
    }

    /// <inheritdoc />
    public IVideoFilter CreateFormatFilter(PixelFormat format) => new DeviceScaleFilter(ScaleFilterName, Surface)
    {
        Format = format,
        ExtraOptions = Afbc ? "afbc=1" : null
    };

    /// <inheritdoc />
    public IVideoFilter CreateSubtitleUpload()
        => new UploadFilter(Surface, PixelFormat.BGRA, false, true);

    /// <inheritdoc />
    public IVideoFilter CreateOverlay(FrameSize size, bool subtitleIsRendered)
        => new OverlayFilter("overlay_rkrga", Surface)
        {
            ExtraOptions = Afbc ? "format=nv12:afbc=1" : "format=nv12"
        };

    /// <inheritdoc />
    public IVideoFilter? TryFuse(IVideoFilter first, IVideoFilter second)
        => first is DeviceScaleFilter a && second is DeviceScaleFilter b ? DeviceScaleFilter.Fuse(a, b) : null;
}
