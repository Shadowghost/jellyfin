using System.Collections.Generic;
using Jellyfin.MediaEncoding.Pipeline;
using Jellyfin.MediaEncoding.Pipeline.Accelerators;
using Jellyfin.MediaEncoding.Pipeline.Accelerators.Amf;
using Jellyfin.MediaEncoding.Pipeline.Accelerators.Cuda;
using Jellyfin.MediaEncoding.Pipeline.Accelerators.Qsv;
using Jellyfin.MediaEncoding.Pipeline.Accelerators.Rkrga;
using Jellyfin.MediaEncoding.Pipeline.Accelerators.Vaapi;
using Jellyfin.MediaEncoding.Pipeline.Accelerators.VideoToolbox;
using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Frames;
using Jellyfin.MediaEncoding.Pipeline.Graph;
using Jellyfin.MediaEncoding.Pipeline.Input;
using Jellyfin.MediaEncoding.Pipeline.Subtitles;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Controller.Tests.MediaEncoding.Differential;

/// <summary>
/// Runs a case through the pipeline package.
/// </summary>
internal static class PipelineFilterChain
{
    public static string Build(TranscodeCase testCase)
    {
        var accelerator = SelectAccelerator(testCase);
        var builder = new VideoFilterChainBuilder(accelerator, PermissiveCapabilities.Instance);
        var (input, requested, outputSurface, outputFormat) = Describe(testCase, accelerator);

        return builder.Build(input, requested, outputSurface, outputFormat).ToFilterArgument();
    }

    private static (FrameState Input, List<IVideoFilter> Requested, FrameSurface OutputSurface, PixelFormat OutputFormat)
        Describe(TranscodeCase testCase, IHardwareAccelerator accelerator)
    {
        var onDevice = accelerator.Surface != FrameSurface.System && testCase.EnableHardwareDecoding;
        var decodeSurface = testCase.Variant == ChainVariant.AmdD3d11 ? FrameSurface.D3d11 : accelerator.Surface;
        var doToneMap = testCase.IsHdr10 && testCase.EnableTonemapping;

        var decoded = new FrameState { PixelFormat = PixelFormat.Parse(testCase.InputPixelFormat) };

        var input = new FrameState
        {
            Surface = onDevice ? decodeSurface : FrameSurface.System,

            // A hardware decoder hands the chain the device's own format, not the source one.
            PixelFormat = onDevice && accelerator.GetDeviceFormat(decoded).IsKnown
                ? accelerator.GetDeviceFormat(decoded)
                : decoded.PixelFormat,
            Size = new FrameSize(testCase.InputWidth, testCase.InputHeight),
            IsInterlaced = testCase.IsInterlaced,
            HdrFormat = testCase.IsHdr10 ? HdrFormat.Hdr10 : HdrFormat.None
        };

        var encoderSurface = testCase.Variant switch
        {
            ChainVariant.AmdD3d11 => FrameSurface.D3d11,
            _ => testCase.Acceleration == HardwareAccelerationType.qsv ? FrameSurface.Qsv : accelerator.Surface
        };
        var outputSurface = testCase.EnableHardwareEncoding && accelerator.Surface != FrameSurface.System
            ? encoderSurface
            : FrameSurface.System;
        var sdr8Bit = new FrameState { PixelFormat = PixelFormat.Yuv420p };
        var outputFormat = accelerator.Surface == FrameSurface.System
            ? PixelFormat.Yuv420p
            : accelerator.GetDeviceFormat(sdr8Bit);

        var requested = new List<IVideoFilter>();

        if (accelerator.DeclaresColorProperties)
        {
            requested.Add(doToneMap ? SetParamsFilter.Hdr10 : SetParamsFilter.Sdr);
        }

        if (testCase.IsInterlaced)
        {
            requested.Add(new DeinterlaceFilter("yadif", false));
        }

        var transposeDirection = TransposeFilter.DirectionFor(testCase.Rotation);
        if (!string.IsNullOrEmpty(transposeDirection))
        {
            requested.Add(new TransposeFilter(transposeDirection));
        }

        if (testCase.Video3DFormat is not null)
        {
            requested.Add(new Flatten3DFilter(testCase.Video3DFormat, onDevice));
        }

        var scaling = new ScalingRequest(
            testCase.RequestedWidth,
            testCase.RequestedHeight,
            testCase.RequestedMaxWidth,
            testCase.RequestedMaxHeight);

        // Always asked for, the way the legacy chains do: it drops itself when there is nothing to do.
        requested.Add(new ScaleFilter(scaling));

        if (doToneMap)
        {
            requested.Add(new ToneMapFilter("bt2390", 0, 100, PixelFormat.Yuv420p));
        }

        return (input, requested, outputSurface, outputFormat);
    }

    public static string BuildFullParam(TranscodeCase testCase)
    {
        var accelerator = SelectAccelerator(testCase);
        var builder = new VideoFilterChainBuilder(accelerator, PermissiveCapabilities.Instance);
        var (input, requested, outputSurface, outputFormat) = Describe(testCase, accelerator);

        SubtitleSource? subtitle = testCase.Subtitle switch
        {
            SubtitleKind.Graphical => new SubtitleSource(false) { Size = new FrameSize(1920, 1080) },
            SubtitleKind.Text => new SubtitleSource(true) { Path = string.Empty, FrameRate = 25 },
            _ => null
        };

        var graph = builder.BuildGraph(input, requested, outputSurface, outputFormat, subtitle);

        return graph.ToArgument(new FilterGraphLabels(0, 1, 0)).Trim();
    }

    public static string BuildInputArgs(TranscodeCase testCase)
    {
        var accelerator = SelectAccelerator(testCase);
        var doToneMap = testCase.IsHdr10 && testCase.EnableTonemapping;

        return accelerator
            .CreateInputPlan(new InputPlanRequest(testCase.EnableHardwareDecoding, doToneMap) { Rotation = testCase.Rotation })
            .ToArgument();
    }

    internal static IHardwareAccelerator SelectAccelerator(TranscodeCase testCase)
    {
        return testCase.Variant switch
        {
            ChainVariant.VideoToolbox => new VideoToolboxAccelerator(),
            ChainVariant.AmdD3d11 => new AmfD3d11Accelerator(),
            ChainVariant.QsvD3d11 => new QsvD3d11Accelerator(),
            ChainVariant.VaapiIntelFull => new VaapiIntelFullAccelerator(),
            ChainVariant.VaapiAmdFull => new VaapiAmdVulkanAccelerator(),
            _ => SelectByAcceleration(testCase)
        };
    }

    private static IHardwareAccelerator SelectByAcceleration(TranscodeCase testCase) => testCase.Acceleration switch
    {
        HardwareAccelerationType.nvenc => new CudaAccelerator(),
        HardwareAccelerationType.vaapi => new VaapiAccelerator(),
        // Frames only stay compressed while they never leave the device.
        HardwareAccelerationType.rkmpp => new RkrgaAccelerator(
            testCase.EnableHardwareEncoding && !(testCase.IsHdr10 && testCase.EnableTonemapping)),
        HardwareAccelerationType.qsv => new QsvVaapiAccelerator(),
        _ => SoftwareAccelerator.Instance
    };
}
