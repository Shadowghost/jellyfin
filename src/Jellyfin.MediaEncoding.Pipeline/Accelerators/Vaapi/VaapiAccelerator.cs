using System.Collections.Generic;
using Jellyfin.MediaEncoding.Pipeline;
using Jellyfin.MediaEncoding.Pipeline.Accelerators;
using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Filters.Devices;
using Jellyfin.MediaEncoding.Pipeline.Filters.Transfers;
using Jellyfin.MediaEncoding.Pipeline.Frames;
using Jellyfin.MediaEncoding.Pipeline.Input;
using Jellyfin.MediaEncoding.Pipeline.Subtitles;
using MediaBrowser.Model.MediaEncoding.Hardware;

namespace Jellyfin.MediaEncoding.Pipeline.Accelerators.Vaapi;

/// <summary>
/// Runs the filter chain on a VA-API device that only offers scaling and deinterlacing, which is
/// the Intel i965 and legacy AMD driver path.
/// </summary>
/// <param name="DoubleRateDeinterlace">Whether the deinterlacer emits one frame per field.</param>
/// <param name="GraphicalOverlay">Whether a bitmap subtitle is drawn over the picture in system memory.</param>
/// <param name="FullRangeOutput">Whether the encoder wants full range frames, which MJPEG does.</param>
public record VaapiAccelerator(
    bool DoubleRateDeinterlace = false,
    bool GraphicalOverlay = false,
    bool FullRangeOutput = false) : IHardwareAccelerator
{
    /// <summary>
    /// The pool the VA-API video processor needs on top of the frames the chain holds.
    /// </summary>
    private const string ExtraFrames = "extra_hw_frames=24";

    private const string ScaleFilterName = "scale_vaapi";

    /// <summary>
    /// Gets the options every pass of the video processor carries.
    /// </summary>
    protected string ScaleOptions => FullRangeOutput ? "out_range=pc:mode=hq:" + ExtraFrames : ExtraFrames;

    /// <inheritdoc />
    public virtual FrameSurface Surface => FrameSurface.Vaapi;

    /// <inheritdoc />
    public FrameSurface DecodeSurface => FrameSurface.Vaapi;

    /// <inheritdoc />
    public virtual FrameSurface EncoderSurface => FrameSurface.Vaapi;

    /// <inheritdoc />
    public virtual string? EncoderSuffix => "vaapi";

    /// <summary>
    /// Gets the format the device wants a subtitle uploaded in, or <c>null</c> when the subtitle is
    /// drawn over the picture in system memory.
    /// </summary>
    /// <remarks>
    /// This driver has no overlay of its own, so the picture comes home and the overlay is software.
    /// </remarks>
    public virtual PixelFormat? SubtitleFormat => null;

    /// <inheritdoc />
    public PixelFormat GetDeviceFormat(FrameState state)
        => state.PixelFormat.BitDepth >= 10 ? PixelFormat.P010le : PixelFormat.Nv12;

    /// <inheritdoc />
    public virtual InputPlan CreateInputPlan(InputPlanRequest request)
    {
        var devices = new List<HardwareDevice>();
        string? filterDevice = null;

        if (request.NeedsOpenCl)
        {
            devices.Add(new HardwareDevice("opencl", HardwareDeviceAliases.OpenCl) { Spec = "0.0" });
            filterDevice = HardwareDeviceAliases.OpenCl;
        }

        return new InputPlan
        {
            Devices = devices,
            FilterDeviceAlias = filterDevice,
            Decoder = request.HardwareDecode ? new VideoDecoder("vaapi", FrameSurface.Vaapi, "vaapi") { StripDisplayRotation = request.Rotation != 0 } : null
        };
    }

    /// <inheritdoc />
    public virtual IHardwareAccelerator ForSoftwareDecode()
        => new CopyBackAccelerator(PixelFormat.Nv12, FrameSurface.Vaapi, "vaapi");

    /// <inheritdoc />
    public virtual IVideoFilter? SelectFilter(IVideoFilter filter, FrameState state, IPipelineCapabilities capabilities)
    {
        switch (filter)
        {
            case ScaleFilter scale when scale.Request.Resolve(state.Size) == state.Size && !state.RequiresResize:
                return null;

            case ScaleFilter scale when capabilities.SupportsFilter(ScaleFilterName)
                && capabilities.CanPerform(HwVppKind.Scale, scale.Request.Resolve(state.Size)):
                return new DeviceScaleFilter(ScaleFilterName, FrameSurface.Vaapi)
                {
                    Size = scale.Request.Resolve(state.Size),
                    ExtraOptions = ScaleOptions
                };

            case DeinterlaceFilter when capabilities.SupportsFilter("deinterlace_vaapi")
                && capabilities.CanPerform(HwVppKind.Deinterlace, state.Size):
                return new DeviceDeinterlaceFilter(
                    DoubleRateDeinterlace ? "deinterlace_vaapi=rate=field" : "deinterlace_vaapi=rate=frame",
                    FrameSurface.Vaapi);

            case ToneMapFilter tonemap when capabilities.SupportsFilter("tonemap_opencl"):
                return new DeviceToneMapFilter(
                    "opencl",
                    FrameSurface.OpenCl,
                    PixelFormat.Nv12,
                    tonemap.Algorithm,
                    tonemap.Peak,
                    tonemap.Desaturation);

            default:
                return filter;
        }
    }

    /// <inheritdoc />
    public IVideoFilter CreateFormatFilter(PixelFormat format) => new DeviceScaleFilter(ScaleFilterName, FrameSurface.Vaapi)
    {
        Format = format,
        ExtraOptions = ScaleOptions
    };

    /// <summary>
    /// Settles how the picture comes home for an overlay it cannot draw itself.
    /// </summary>
    /// <param name="subtitle">The subtitle being burned in.</param>
    /// <returns>The accelerator to build the picture chain with.</returns>
    public virtual IHardwareAccelerator ForOverlay(SubtitleSource subtitle)
        => this with { GraphicalOverlay = !subtitle.IsText };

    /// <inheritdoc />
    public virtual IVideoFilter? CreateTransferFilter(FrameSurface target, FrameState state, bool reverse)
    {
        // A frame that was mapped home is mapped back, not uploaded.
        if (target == FrameSurface.Vaapi && state.Surface == FrameSurface.System && !GraphicalOverlay)
        {
            return new CompositeFilter(["hwmap", "format=vaapi"], FrameSurface.Vaapi, state.PixelFormat);
        }

        if (target != FrameSurface.System || state.Surface != FrameSurface.Vaapi)
        {
            return null;
        }

        // A bitmap subtitle is drawn onto a frame the overlay has to read, which mapping does not
        // give it, so that one is copied home instead.
        return new DownloadFilter(state.PixelFormat, GraphicalOverlay ? "hwdownload" : "hwmap");
    }

    /// <summary>
    /// Builds the filter that puts a prepared subtitle onto the device.
    /// </summary>
    /// <returns>The filter, or <c>null</c> when the overlay happens in system memory.</returns>
    public virtual IVideoFilter? CreateSubtitleUpload() => null;

    /// <summary>
    /// Builds the filter that draws the subtitle over the picture.
    /// </summary>
    /// <param name="size">The size of the frame the subtitle is drawn over.</param>
    /// <param name="subtitleIsRendered">Whether the subtitle was drawn onto a canvas of its own.</param>
    /// <returns>The overlay filter.</returns>
    public virtual IVideoFilter CreateOverlay(FrameSize size, bool subtitleIsRendered)
        => new OverlayFilter("overlay", FrameSurface.System);

    /// <inheritdoc />
    public IVideoFilter? TryFuse(IVideoFilter first, IVideoFilter second)
        => first is DeviceScaleFilter a && second is DeviceScaleFilter b ? DeviceScaleFilter.Fuse(a, b) : null;
}
