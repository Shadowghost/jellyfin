using System.Collections.Generic;
using System.Globalization;
using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Filters.Devices;
using Jellyfin.MediaEncoding.Pipeline.Filters.Transfers;
using Jellyfin.MediaEncoding.Pipeline.Frames;
using Jellyfin.MediaEncoding.Pipeline.Input;
using Jellyfin.MediaEncoding.Pipeline.Subtitles;
using MediaBrowser.Model.MediaEncoding.Hardware;

namespace Jellyfin.MediaEncoding.Pipeline.Accelerators.Qsv;

/// <summary>
/// Runs the filter chain on an Intel Quick Sync Video device that filters with <c>vpp_qsv</c>,
/// which is the Direct3D 11 backed path.
/// </summary>
/// <remarks>
/// The video processor scales and converts, but tone mapping is borrowed from OpenCL, which the
/// frame is mapped to and back rather than copied.
/// </remarks>
/// <param name="FullRangeOutput">Whether the encoder wants full range frames, which MJPEG does.</param>
public sealed record QsvD3d11Accelerator(bool FullRangeOutput = false) : IHardwareAccelerator
{
    private const string QsvVideoProcessorName = "vpp_qsv";

    /// <inheritdoc />
    public FrameSurface Surface => FrameSurface.Qsv;

    /// <inheritdoc />
    public FrameSurface DecodeSurface => FrameSurface.Qsv;

    /// <inheritdoc />
    public string? EncoderSuffix => "qsv";

    /// <inheritdoc />
    public PixelFormat? SubtitleFormat => PixelFormat.BGRA;

    /// <inheritdoc />
    public PixelFormat GetDeviceFormat(FrameState state)
        => state.PixelFormat.BitDepth >= 10 ? PixelFormat.P010LE : PixelFormat.NV12;

    /// <inheritdoc />
    public InputPlan CreateInputPlan(InputPlanRequest request)
    {
        var devices = new List<HardwareDevice>
        {
            new("d3d11va", HardwareDeviceAliases.D3d11va)
            {
                Spec = request.DeviceIndex is { } index
                    ? index.ToString(CultureInfo.InvariantCulture)
                    : "0,vendor=0x8086"
            },
            new("qsv", HardwareDeviceAliases.Qsv) { SourceAlias = HardwareDeviceAliases.D3d11va }
        };

        if (request.NeedsOpenCl)
        {
            devices.Add(new HardwareDevice("opencl", HardwareDeviceAliases.OpenCl) { SourceAlias = HardwareDeviceAliases.D3d11va });
        }

        return new InputPlan
        {
            Devices = devices,
            FilterDeviceAlias = HardwareDeviceAliases.Qsv,
            Decoder = request.HardwareDecode ? new VideoDecoder("qsv", FrameSurface.Qsv, "qsv") { StripDisplayRotation = request.Rotation != 0 } : null
        };
    }

    /// <inheritdoc />
    public IHardwareAccelerator ForSoftwareDecode()
        => new CopyBackAccelerator(PixelFormat.NV12, FrameSurface.System, "qsv");

    /// <inheritdoc />
    public IVideoFilter? SelectFilter(IVideoFilter filter, FrameState state, IPipelineCapabilities capabilities)
    {
        if (!capabilities.SupportsFilter(QsvVideoProcessorName))
        {
            return filter;
        }

        switch (filter)
        {
            case ScaleFilter scale when scale.Request.Resolve(state.Size) == state.Size && !state.RequiresResize:
                return null;

            case ScaleFilter scale when capabilities.CanPerform(HwVppKind.Scale, scale.Request.Resolve(state.Size), state.PixelFormat):
                return QsvVideoProcessorFilter.Scale(scale.Request.Resolve(state.Size));

            // The video processor turns the picture as part of its own pass.
            case TransposeFilter transpose:
                return QsvVideoProcessorFilter.ToTranspose(transpose.Direction);

            case DeinterlaceFilter when capabilities.SupportsFilter("deinterlace_qsv")
                && capabilities.CanPerform(HwVppKind.Deinterlace, state.Size, state.PixelFormat):
                return new DeviceDeinterlaceFilter("deinterlace_qsv=mode=2", Surface);

            case ToneMapFilter tonemap when capabilities.SupportsFilter("tonemap_opencl"):
                return new DeviceToneMapFilter(
                    "opencl",
                    FrameSurface.OpenCl,
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
        => QsvVideoProcessorFilter.ToFormat(format) with
        {
            ExtraOptions = FullRangeOutput ? "out_range=pc:scale_mode=hq" : null
        };

    /// <inheritdoc />
    public IVideoFilter? CreateTransferFilter(FrameSurface target, FrameState state, bool reverse) => target switch
    {
        FrameSurface.OpenCl when state.Surface == FrameSurface.Qsv
            => new MapFilter(target, false, "mode=read"),
        FrameSurface.Qsv when state.Surface == FrameSurface.OpenCl
            => new MapFilter(target, false, "mode=write:reverse=1"),
        _ => null
    };

    /// <inheritdoc />
    public IVideoFilter CreateSubtitleUpload()
        => new UploadFilter(FrameSurface.Qsv, PixelFormat.BGRA, false, true) { ExtraOptions = "extra_hw_frames=64" };

    /// <inheritdoc />
    public IVideoFilter CreateOverlay(FrameSize size, bool subtitleIsRendered)
        => new OverlayFilter("overlay_qsv", FrameSurface.Qsv, size);

    /// <inheritdoc />
    public IVideoFilter? TryFuse(IVideoFilter first, IVideoFilter second)
        => first is QsvVideoProcessorFilter a && second is QsvVideoProcessorFilter b ? a.Fuse(b) : null;
}
