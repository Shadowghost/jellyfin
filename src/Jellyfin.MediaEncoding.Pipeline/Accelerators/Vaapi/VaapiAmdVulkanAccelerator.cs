using System.Collections.Generic;
using System.Linq;
using Jellyfin.MediaEncoding.Pipeline;
using Jellyfin.MediaEncoding.Pipeline.Accelerators;
using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Filters.Devices;
using Jellyfin.MediaEncoding.Pipeline.Filters.Transfers;
using Jellyfin.MediaEncoding.Pipeline.Frames;
using Jellyfin.MediaEncoding.Pipeline.Input;
using MediaBrowser.Model.MediaEncoding.Hardware;

namespace Jellyfin.MediaEncoding.Pipeline.Accelerators.Vaapi;

/// <summary>
/// Runs the filter chain on a VA-API device driven by the AMD open source driver, which has no
/// video processor beyond scaling and deinterlacing but shares memory with Vulkan.
/// </summary>
/// <remarks>
/// Anything the video processor cannot do sends the whole picture down a detour: the frame is mapped
/// to Vulkan and libplacebo resizes, crops and tone maps it in one pass, so the VA-API scaler is not
/// used at all on that route.
/// </remarks>
/// <param name="DoubleRateDeinterlace">Whether the deinterlacer emits one frame per field.</param>
/// <param name="TakesVulkanRoute">Whether this chain goes through Vulkan.</param>
/// <param name="ToneMapping">Whether the Vulkan pass also tone maps.</param>
/// <param name="FullRangeOutput">Whether the encoder wants full range frames, which MJPEG does.</param>
/// <param name="ImportNeedsScaleVulkan">
/// Whether the imported frame has to pass through <c>scale_vulkan</c> before libplacebo will take
/// it, which older AMD parts need.
/// </param>
public sealed record VaapiAmdVulkanAccelerator(
    bool DoubleRateDeinterlace = false,
    bool TakesVulkanRoute = false,
    bool ToneMapping = false,
    bool FullRangeOutput = false,
    bool ImportNeedsScaleVulkan = true) : IHardwareAccelerator
{
    private const string ScaleFilterName = "scale_vaapi";

    /// <inheritdoc />
    public FrameSurface Surface => FrameSurface.Vaapi;

    /// <inheritdoc />
    public FrameSurface DecodeSurface => FrameSurface.Vaapi;

    /// <inheritdoc />
    public string? EncoderSuffix => "vaapi";

    /// <inheritdoc />
    public PixelFormat GetDeviceFormat(FrameState state)
        => state.PixelFormat.BitDepth >= 10 && !TakesVulkanRoute ? PixelFormat.P010le : PixelFormat.Nv12;

    /// <inheritdoc />
    public IHardwareAccelerator ForSoftwareDecode()
        => new CopyBackAccelerator(PixelFormat.Nv12, FrameSurface.Vaapi, "vaapi");

    /// <inheritdoc />
    public (IHardwareAccelerator Accelerator, IReadOnlyList<IVideoFilter> Filters) Plan(
        IReadOnlyList<IVideoFilter> requested,
        FrameState input,
        IPipelineCapabilities capabilities)
    {
        var toneMaps = input.HdrFormat != HdrFormat.None
            && requested.OfType<ToneMapFilter>().Any()
            && capabilities.SupportsFilter("libplacebo");

        var flattens = requested.OfType<Flatten3DFilter>().Any(f => f.OnDevice && f.ToFilterArgument() is not null);
        var turns = requested.OfType<TransposeFilter>().Any(f => !string.IsNullOrEmpty(f.Direction));

        if (!toneMaps && !flattens && !turns)
        {
            return (this, requested);
        }

        // The deinterlacer lives on the VA-API side, so it only runs once the frame is back.
        var ordered = requested.Where(f => f is not DeinterlaceFilter)
            .Concat(requested.OfType<DeinterlaceFilter>())
            .ToList();

        return (this with { TakesVulkanRoute = true, ToneMapping = toneMaps }, ordered);
    }

    /// <inheritdoc />
    public InputPlan CreateInputPlan(InputPlanRequest request) => new()
    {
        Decoder = request.HardwareDecode
            ? new VideoDecoder("vaapi", FrameSurface.Vaapi, "vaapi")
            {
                StripDisplayRotation = request.Rotation != 0
            }
            : null
    };

    /// <inheritdoc />
    public IVideoFilter? SelectFilter(IVideoFilter filter, FrameState state, IPipelineCapabilities capabilities)
    {
        switch (filter)
        {
            case DeinterlaceFilter when capabilities.SupportsFilter("deinterlace_vaapi")
                && capabilities.CanPerform(HwVppKind.Deinterlace, state.Size):
                return new DeviceDeinterlaceFilter(
                    DoubleRateDeinterlace ? "deinterlace_vaapi=rate=field" : "deinterlace_vaapi=rate=frame",
                    FrameSurface.Vaapi);

            case TransposeFilter transpose when TakesVulkanRoute:
                return new DeviceTransposeFilter(
                    transpose.Direction == "reversal" ? "flip_vulkan" : $"transpose_vulkan=dir={transpose.Direction}",
                    FrameSurface.Vulkan,
                    transpose.SwapsDimensions);

            case Flatten3DFilter crop when crop.OnDevice && TakesVulkanRoute:
                return crop with { DeviceSurface = FrameSurface.Vulkan };

            case ScaleFilter scale when scale.Request.Resolve(state.Size) == state.Size && !state.RequiresResize:
                return null;

            case ScaleFilter scale when TakesVulkanRoute:
                return new LibplaceboFilter
                {
                    Size = scale.Request.Resolve(state.Size),
                    Format = ToneMapping ? PixelFormat.Bgra : PixelFormat.Nv12
                };

            case ScaleFilter scale when capabilities.SupportsFilter(ScaleFilterName)
                && capabilities.CanPerform(HwVppKind.Scale, scale.Request.Resolve(state.Size)):
                return new DeviceScaleFilter(ScaleFilterName, FrameSurface.Vaapi) { Size = scale.Request.Resolve(state.Size) };

            case ToneMapFilter tonemap when TakesVulkanRoute:
                return new LibplaceboFilter
                {
                    Format = PixelFormat.Bgra,
                    ToneMap = true,
                    Algorithm = ToLibplaceboAlgorithm(tonemap.Algorithm),

                    // libplacebo only writes full range RGB.
                    Range = "pc"
                };

            default:
                return filter;
        }
    }

    /// <inheritdoc />
    public IVideoFilter CreateFormatFilter(PixelFormat format)
    {
        if (!TakesVulkanRoute)
        {
            return new DeviceScaleFilter(ScaleFilterName, FrameSurface.Vaapi)
            {
                Format = format,
                ExtraOptions = FullRangeOutput ? "out_range=pc:mode=hq" : null
            };
        }

        // Coming back from Vulkan the scaler also clears the surface metadata offset.
        return new DeviceScaleFilter(ScaleFilterName, FrameSurface.Vaapi)
        {
            Format = format,
            ExtraOptions = ToneMapping ? "out_range=tv" : null
        };
    }

    /// <inheritdoc />
    public IVideoFilter? CreateTransferFilter(FrameSurface target, FrameState state, bool reverse)
    {
        if (target == FrameSurface.Vulkan && state.Surface == FrameSurface.Vaapi)
        {
            var hops = new List<string>
            {
                "hwmap=derive_device=drm",
                "format=drm_prime",
                "hwmap=derive_device=vulkan",
                "format=vulkan"
            };

            if (ImportNeedsScaleVulkan)
            {
                hops.Add("scale_vulkan");
            }

            return new CompositeFilter(hops, FrameSurface.Vulkan);
        }

        if (target == FrameSurface.Vaapi && state.Surface == FrameSurface.Vulkan)
        {
            return new CompositeFilter(
                ["format=vulkan", "hwmap=derive_device=vaapi", "format=vaapi"],
                FrameSurface.Vaapi);
        }

        return null;
    }

    /// <inheritdoc />
    public IVideoFilter? TryFuse(IVideoFilter first, IVideoFilter second) => (first, second) switch
    {
        (DeviceScaleFilter a, DeviceScaleFilter b) => DeviceScaleFilter.Fuse(a, b),
        (LibplaceboFilter a, LibplaceboFilter b) => LibplaceboFilter.Fuse(a, b),
        _ => null
    };

    private static string ToLibplaceboAlgorithm(string algorithm)
        => string.Equals(algorithm, "bt2390", System.StringComparison.OrdinalIgnoreCase) ? "bt.2390" : algorithm;
}
