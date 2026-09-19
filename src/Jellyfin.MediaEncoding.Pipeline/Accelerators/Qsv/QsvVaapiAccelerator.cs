using System.Collections.Generic;
using Jellyfin.MediaEncoding.Pipeline.Accelerators.Vaapi;
using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Filters.Devices;
using Jellyfin.MediaEncoding.Pipeline.Filters.Transfers;
using Jellyfin.MediaEncoding.Pipeline.Frames;
using Jellyfin.MediaEncoding.Pipeline.Input;
using Jellyfin.MediaEncoding.Pipeline.Subtitles;

namespace Jellyfin.MediaEncoding.Pipeline.Accelerators.Qsv;

/// <summary>
/// Runs the filter chain for an Intel Quick Sync Video encoder on the VA-API device backing it,
/// which is how Quick Sync works on Linux.
/// </summary>
/// <remarks>
/// The frames are filtered as VA-API surfaces and handed to the encoder by mapping rather than
/// copying, so the chain runs on one surface and ends on another.
/// </remarks>
/// <param name="DoubleRateDeinterlace">Whether the deinterlacer emits one frame per field.</param>
/// <param name="FullRangeOutput">Whether the encoder wants full range frames, which MJPEG does.</param>
public sealed record QsvVaapiAccelerator(bool DoubleRateDeinterlace = false, bool FullRangeOutput = false)
    : VaapiAccelerator(DoubleRateDeinterlace, false, FullRangeOutput)
{
    /// <inheritdoc />
    public override FrameSurface EncoderSurface => FrameSurface.Qsv;

    /// <inheritdoc />
    public override string? EncoderSuffix => "qsv";

    /// <inheritdoc />
    public override PixelFormat? SubtitleFormat => PixelFormat.Bgra;

    /// <inheritdoc />
    public override InputPlan CreateInputPlan(InputPlanRequest request)
    {
        var devices = new List<HardwareDevice>
        {
            new("vaapi", HardwareDeviceAliases.Vaapi) { Spec = ",vendor_id=0x8086,driver=iHD" },
            new("qsv", HardwareDeviceAliases.Qsv) { SourceAlias = HardwareDeviceAliases.Vaapi }
        };

        if (request.NeedsOpenCl)
        {
            devices.Add(new HardwareDevice("opencl", HardwareDeviceAliases.OpenCl) { SourceAlias = HardwareDeviceAliases.Vaapi });
        }

        return new InputPlan
        {
            Devices = devices,
            FilterDeviceAlias = HardwareDeviceAliases.Qsv,
            Decoder = request.HardwareDecode ? new VideoDecoder("vaapi", FrameSurface.Vaapi, "vaapi") { StripDisplayRotation = request.Rotation != 0 } : null
        };
    }

    /// <inheritdoc />
    public override IHardwareAccelerator ForSoftwareDecode()
        => new CopyBackAccelerator(PixelFormat.Nv12, FrameSurface.System, "qsv");

    /// <inheritdoc />
    public override IVideoFilter? SelectFilter(IVideoFilter filter, FrameState state, IPipelineCapabilities capabilities)
        => filter is TransposeFilter transpose && capabilities.SupportsFilter("transpose_vaapi")
            ? new DeviceTransposeFilter(
                $"transpose_vaapi=dir={transpose.Direction}",
                FrameSurface.Vaapi,
                transpose.SwapsDimensions)
            : base.SelectFilter(filter, state, capabilities);

    /// <inheritdoc />
    public override IVideoFilter CreateSubtitleUpload()
        => new CompositeFilter(["hwupload=derive_device=qsv:extra_hw_frames=64"], FrameSurface.Qsv, PixelFormat.Bgra);

    /// <inheritdoc />
    public override IVideoFilter CreateOverlay(FrameSize size, bool subtitleIsRendered)
        => new OverlayFilter("overlay_qsv", FrameSurface.Qsv, size);

    /// <inheritdoc />
    public override IVideoFilter? CreateTransferFilter(FrameSurface target, FrameState state, bool reverse)
    {
        if (target == FrameSurface.System)
        {
            return new DownloadFilter(state.PixelFormat, "hwmap=mode=read");
        }

        if (target == FrameSurface.OpenCl)
        {
            return new MapFilter(target, false, "mode=read");
        }

        if (target == FrameSurface.Qsv && state.Surface == FrameSurface.OpenCl)
        {
            return new MapFilter(target, false, "mode=write:reverse=1:extra_hw_frames=16");
        }

        return null;
    }
}
