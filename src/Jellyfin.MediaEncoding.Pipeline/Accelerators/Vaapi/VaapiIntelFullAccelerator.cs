using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Filters.Devices;
using Jellyfin.MediaEncoding.Pipeline.Filters.Transfers;
using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Accelerators.Vaapi;

/// <summary>
/// Runs the filter chain on a VA-API device driven by the Intel iHD driver, whose video processor
/// can do more than scale and deinterlace.
/// </summary>
/// <remarks>
/// It also shares memory with OpenCL, so a tone mapper borrowed from there is reached by mapping the
/// frame rather than copying it home and back.
/// </remarks>
/// <param name="DoubleRateDeinterlace">Whether the deinterlacer emits one frame per field.</param>
/// <param name="FullRangeOutput">Whether the encoder wants full range frames, which MJPEG does.</param>
public sealed record VaapiIntelFullAccelerator(bool DoubleRateDeinterlace = false, bool FullRangeOutput = false)
    : VaapiAccelerator(DoubleRateDeinterlace, false, FullRangeOutput)
{
    /// <inheritdoc />
    public override IVideoFilter? SelectFilter(IVideoFilter filter, FrameState state, IPipelineCapabilities capabilities)
        => filter is TransposeFilter transpose && capabilities.SupportsFilter("transpose_vaapi")
            ? new DeviceTransposeFilter(
                $"transpose_vaapi=dir={transpose.Direction}",
                FrameSurface.Vaapi,
                transpose.SwapsDimensions)
            : base.SelectFilter(filter, state, capabilities);

    /// <inheritdoc />
    public override IVideoFilter? CreateTransferFilter(FrameSurface target, FrameState state, bool reverse)
        => target switch
        {
            FrameSurface.OpenCl => new MapFilter(target, false, "mode=read"),
            FrameSurface.Vaapi when state.Surface == FrameSurface.OpenCl
                => new MapFilter(target, false, "mode=write:reverse=1"),
            FrameSurface.System => new DownloadFilter(state.PixelFormat, "hwmap=mode=read"),
            _ => null
        };
}
