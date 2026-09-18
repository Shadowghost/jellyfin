using System.Collections.Generic;
using Jellyfin.MediaEncoding.Pipeline;
using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Filters.Devices;
using Jellyfin.MediaEncoding.Pipeline.Filters.Transfers;
using Jellyfin.MediaEncoding.Pipeline.Frames;
using Jellyfin.MediaEncoding.Pipeline.Input;
using Jellyfin.MediaEncoding.Pipeline.Subtitles;
using MediaBrowser.Model.MediaEncoding.Hardware;

namespace Jellyfin.MediaEncoding.Pipeline.Accelerators.Rkrga;

/// <summary>
/// Runs the filter chain on a Rockchip RGA device.
/// </summary>
/// <param name="Afbc">
/// Whether the frames stay compressed, which the device only allows when they never leave it.
/// </param>
public sealed record RkrgaAccelerator(bool Afbc) : IHardwareAccelerator
{
    private const string ScaleFilterName = "vpp_rkrga";

    private static readonly PixelFormat _p010 = new("p010", 10, false);

    /// <inheritdoc />
    public FrameSurface Surface => FrameSurface.Rkmpp;

    /// <inheritdoc />
    public string? EncoderSuffix => "rkmpp";

    /// <inheritdoc />
    public PixelFormat? SubtitleFormat => PixelFormat.Bgra;

    /// <inheritdoc />
    public bool OverlayPinsPixelFormat => true;

    /// <inheritdoc />
    public PixelFormat GetDeviceFormat(FrameState state)
        => state.PixelFormat.BitDepth >= 10 ? _p010 : PixelFormat.Nv12;

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
    public IVideoFilter? SelectFilter(IVideoFilter filter, FrameState state, IPipelineCapabilities capabilities)
    {
        switch (filter)
        {
            case ScaleFilter scale when scale.Request.Resolve(state.Size) == state.Size && !state.RequiresResize:
                return null;

            case ScaleFilter scale when capabilities.SupportsFilter(ScaleFilterName)
                && capabilities.CanPerform(HwVppKind.Scale, scale.Request.Resolve(state.Size)):
                return new DeviceScaleFilter(ScaleFilterName, Surface)
                {
                    Size = scale.Request.Resolve(state.Size),
                    Format = GetDeviceFormat(state),
                    ExtraOptions = Afbc ? "afbc=1" : null
                };

            // The device has no deinterlacer and a round trip home is not worth one, so interlaced
            // content stays interlaced.
            // The video processor turns the picture as part of its own pass.
            case TransposeFilter transpose:
                return new DeviceScaleFilter(ScaleFilterName, Surface)
                {
                    ExtraOptions = $"transpose={transpose.Direction}",
                    SwapsOutputDimensions = transpose.SwapsDimensions
                };

            case DeinterlaceFilter:
                return null;

            case ToneMapFilter tonemap when capabilities.SupportsFilter("tonemap_opencl"):
                return new DeviceToneMapFilter(
                    "opencl",
                    FrameSurface.OpenCl,
                    PixelFormat.Nv12,
                    tonemap.Algorithm,
                    tonemap.Peak,
                    tonemap.Desaturation)
                {
                    RequiredInputFormat = _p010
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
        => new CompositeFilter(["hwupload=derive_device=rkmpp"], Surface, PixelFormat.Bgra);

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
