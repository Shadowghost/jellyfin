using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
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
using Jellyfin.MediaEncoding.Pipeline.Subtitles;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using PipelineCapabilities = Jellyfin.MediaEncoding.Pipeline.IPipelineCapabilities;
using PipelinePixelFormat = Jellyfin.MediaEncoding.Pipeline.Frames.PixelFormat;

namespace MediaBrowser.Controller.MediaEncoding;

/// <summary>
/// Builds a filter chain with <see cref="Jellyfin.MediaEncoding.Pipeline"/> instead of the vendor
/// chains in this class, for the devices that have been handed over to it.
/// </summary>
public partial class EncodingHelper
{
    /// <summary>
    /// Builds the filter chain for a job with the pipeline package.
    /// </summary>
    /// <param name="state">Encoding state.</param>
    /// <param name="options">Encoding options.</param>
    /// <param name="vidEncoder">Video encoder to use.</param>
    /// <returns>The three filter lists, or <c>null</c> when the job is not one the package builds.</returns>
    internal (List<string> MainFilters, List<string> SubFilters, List<string> OverlayFilters)? GetPipelineVidFilterChain(
        EncodingJobInfo state,
        EncodingOptions options,
        string vidEncoder)
    {
        var videoStream = state.VideoStream;
        if (videoStream is null || !videoStream.Width.HasValue || !videoStream.Height.HasValue)
        {
            return null;
        }

        var vidDecoder = GetHardwareVideoDecoder(state, options) ?? string.Empty;
        var accelerator = SelectPipelineAccelerator(state, options, vidDecoder, vidEncoder);
        if (accelerator is null)
        {
            return null;
        }

        var capabilities = new ServerPipelineCapabilities(_mediaEncoder, _hardwareCapabilities, options);
        var graph = new VideoFilterChainBuilder(accelerator, capabilities).BuildGraph(
            DescribeSource(state, accelerator, vidDecoder),
            DescribeOperations(state, options, accelerator, vidDecoder),
            DescribeEncoderSurface(accelerator, vidEncoder),
            DescribeEncoderFormat(accelerator),
            DescribeSubtitle(state, options));

        return (
            Render(graph.Main),
            graph.Subtitle is null ? [] : Render(graph.Subtitle),
            graph.Overlay is null ? [] : Render(graph.Overlay));
    }

    private static List<string> Render(VideoFilterChain chain)
        => chain.Filters.Select(f => f.ToFilterArgument()).Where(a => !string.IsNullOrEmpty(a)).ToList()!;

    private static FrameSurface DescribeEncoderSurface(IHardwareAccelerator accelerator, string vidEncoder)
    {
        if (accelerator.EncoderSuffix is null
            || !vidEncoder.Contains(accelerator.EncoderSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return FrameSurface.System;
        }

        return accelerator switch
        {
            QsvVaapiAccelerator => FrameSurface.Qsv,
            AmfD3d11Accelerator => FrameSurface.D3d11,
            _ => accelerator.Surface
        };
    }

    private static PipelinePixelFormat DescribeEncoderFormat(IHardwareAccelerator accelerator)
        => accelerator.Surface == FrameSurface.System
            ? PipelinePixelFormat.Yuv420p
            : accelerator.GetDeviceFormat(new FrameState { PixelFormat = PipelinePixelFormat.Yuv420p });

    private static FrameState DescribeSource(
        EncodingJobInfo state,
        IHardwareAccelerator accelerator,
        string vidDecoder)
    {
        var videoStream = state.VideoStream;
        var decoded = new FrameState { PixelFormat = PipelinePixelFormat.Parse(videoStream.PixelFormat) };
        var onDevice = accelerator.Surface != FrameSurface.System && !string.IsNullOrEmpty(vidDecoder);
        var deviceFormat = accelerator.GetDeviceFormat(decoded);

        return new FrameState
        {
            Surface = onDevice ? DescribeDecodeSurface(accelerator) : FrameSurface.System,
            PixelFormat = onDevice && deviceFormat.IsKnown ? deviceFormat : decoded.PixelFormat,
            Size = new FrameSize(videoStream.Width!.Value, videoStream.Height!.Value),
            IsInterlaced = videoStream.IsInterlaced,
            HdrFormat = videoStream.VideoRange == VideoRange.HDR ? HdrFormat.Hdr10 : HdrFormat.None
        };
    }

    private static FrameSurface DescribeDecodeSurface(IHardwareAccelerator accelerator)
        => accelerator is AmfD3d11Accelerator ? FrameSurface.D3d11 : accelerator.Surface;

    private List<IVideoFilter> DescribeOperations(
        EncodingJobInfo state,
        EncodingOptions options,
        IHardwareAccelerator accelerator,
        string vidDecoder)
    {
        var doToneMap = IsHwTonemapAvailable(state, options) || IsSwTonemapAvailable(state, options);
        var operations = new List<IVideoFilter>();

        if (accelerator.DeclaresColorProperties)
        {
            operations.Add(doToneMap
                ? ColorPropertiesOf(GetInputHdrParam(state.VideoStream?.ColorTransfer))
                : SetParamsFilter.Sdr);
        }

        if (IsDeinterlaceAvailable(state))
        {
            var doubleRate = options.DeinterlaceDoubleRate && state.VideoStream?.ReferenceFrameRate <= 30;
            operations.Add(new DeinterlaceFilter(options.DeinterlaceMethod.ToString().ToLowerInvariant(), doubleRate));
        }

        var direction = TransposeFilter.DirectionFor(state.VideoStream?.Rotation ?? 0);
        if (!string.IsNullOrEmpty(direction))
        {
            operations.Add(new TransposeFilter(direction));
        }

        var threeDFormat = state.MediaSource?.Video3DFormat;
        if (threeDFormat.HasValue)
        {
            operations.Add(new Flatten3DFilter(threeDFormat, CanFlatten3DInVram(state, options)));
        }

        operations.Add(new ScaleFilter(new ScalingRequest(
            state.BaseRequest.Width,
            state.BaseRequest.Height,
            state.BaseRequest.MaxWidth,
            state.BaseRequest.MaxHeight)));

        if (doToneMap)
        {
            operations.Add(new ToneMapFilter(
                options.TonemappingAlgorithm.ToString().ToLowerInvariant(),
                options.TonemappingDesat,
                options.TonemappingPeak,
                PipelinePixelFormat.Yuv420p));
        }

        return operations;
    }

    private static SetParamsFilter ColorPropertiesOf(string inputHdrParam)
        => inputHdrParam.Contains("arib-std-b67", StringComparison.OrdinalIgnoreCase)
            ? SetParamsFilter.Hlg
            : SetParamsFilter.Hdr10;

    private SubtitleSource? DescribeSubtitle(EncodingJobInfo state, EncodingOptions options)
    {
        var (hasSubs, hasTextSubs, _, hasAssSubs) = GetChainSubtitles(state);
        if (!hasSubs)
        {
            return null;
        }

        if (!hasTextSubs)
        {
            return new SubtitleSource(false)
            {
                Size = state.SubtitleStream?.Width > 0 && state.SubtitleStream?.Height > 0
                    ? new FrameSize(state.SubtitleStream.Width!.Value, state.SubtitleStream.Height!.Value)
                    : null
            };
        }

        var frameRate = hasAssSubs ? Math.Min(state.VideoStream?.RealFrameRate ?? 25, 60) : 10;

        return new SubtitleSource(true)
        {
            Path = GetTextSubtitlePath(state),
            FontsDirectory = _pathManager.GetAttachmentFolderPath(state.MediaSource.Id) is { } fonts
                ? _mediaEncoder.EscapeSubtitleFilterPath(fonts)
                : null,
            FrameRate = frameRate,
            SeekSeconds = Math.Round(TimeSpan.FromTicks(state.StartTimeTicks ?? 0).TotalSeconds),
            CopyTimestamps = state.CopyTimestamps || state.TranscodingType != TranscodingJobType.Progressive
        };
    }

    private string GetTextSubtitlePath(EncodingJobInfo state)
    {
        if (state.SubtitleStream.IsExternal)
        {
            return _mediaEncoder.EscapeSubtitleFilterPath(state.SubtitleStream.Path);
        }

        var path = _subtitleEncoder
            .GetSubtitleFilePath(state.SubtitleStream, state.MediaSource, System.Threading.CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        return _mediaEncoder.EscapeSubtitleFilterPath(path);
    }

    /// <summary>
    /// Picks the accelerator for a job, mirroring which vendor chain the legacy dispatchers would
    /// have chosen, and returns <c>null</c> for anything the package has not been handed.
    /// </summary>
    private IHardwareAccelerator? SelectPipelineAccelerator(
        EncodingJobInfo state,
        EncodingOptions options,
        string vidDecoder,
        string vidEncoder)
    {
        if (vidEncoder.Contains("mjpeg", StringComparison.OrdinalIgnoreCase) || IsCopyCodec(vidEncoder))
        {
            return null;
        }

        var doubleRateDeint = options.DeinterlaceDoubleRate && (state.VideoStream?.ReferenceFrameRate ?? 60) <= 30;
        var isSwDecoder = string.IsNullOrEmpty(vidDecoder);
        var deinterlaceMethod = options.DeinterlaceMethod.ToString().ToLowerInvariant();

        switch (options.HardwareAccelerationType)
        {
            case HardwareAccelerationType.none:
                return SoftwareAccelerator.Instance;

            case HardwareAccelerationType.nvenc:
                return isSwDecoder ? null : new CudaAccelerator(deinterlaceMethod, doubleRateDeint);

            case HardwareAccelerationType.rkmpp:
                var isRkmppEncoder = vidEncoder.Contains("rkmpp", StringComparison.OrdinalIgnoreCase);
                var afbc = isRkmppEncoder
                    && !IsHwTonemapAvailable(state, options)
                    && (vidEncoder.Contains("h264", StringComparison.OrdinalIgnoreCase)
                        || vidEncoder.Contains("hevc", StringComparison.OrdinalIgnoreCase));
                return isSwDecoder ? null : new RkrgaAccelerator(afbc);

            case HardwareAccelerationType.videotoolbox:
                return isSwDecoder ? null : new VideoToolboxAccelerator(deinterlaceMethod, doubleRateDeint);

            case HardwareAccelerationType.qsv:
                return isSwDecoder || !OperatingSystem.IsLinux() || !IsVaapiSupported(state)
                    ? null
                    : new QsvVaapiAccelerator(doubleRateDeint);

            case HardwareAccelerationType.vaapi:
                return SelectVaapiPipelineAccelerator(state, isSwDecoder, doubleRateDeint);

            default:
                return null;
        }
    }

    private IHardwareAccelerator? SelectVaapiPipelineAccelerator(
        EncodingJobInfo state,
        bool isSwDecoder,
        bool doubleRateDeint)
    {
        if (isSwDecoder || !OperatingSystem.IsLinux() || !IsVaapiSupported(state) || !IsVaapiFullSupported())
        {
            return null;
        }

        if (_mediaEncoder.IsVaapiDeviceInteliHD)
        {
            return new VaapiIntelFullAccelerator(doubleRateDeint);
        }

        if (_mediaEncoder.IsVaapiDeviceAmd)
        {
            return IsVulkanFullSupported()
                && _mediaEncoder.IsVaapiDeviceSupportVulkanDrmInterop
                && Environment.OSVersion.Version >= _minKernelVersionAmdVkFmtModifier
                    ? new VaapiAmdVulkanAccelerator(
                        doubleRateDeint,
                        ImportNeedsScaleVulkan: !_mediaEncoder.IsVaapiDeviceSupportVulkanDrmModifier)
                    : null;
        }

        return new VaapiAccelerator(doubleRateDeint);
    }

    /// <summary>
    /// Answers the pipeline package's questions from the installed ffmpeg and the probed hardware.
    /// </summary>
    private sealed class ServerPipelineCapabilities : PipelineCapabilities
    {
        private readonly IMediaEncoder _mediaEncoder;
        private readonly IHardwareCapabilitiesProvider _hardwareCapabilities;
        private readonly EncodingOptions _options;

        public ServerPipelineCapabilities(
            IMediaEncoder mediaEncoder,
            IHardwareCapabilitiesProvider hardwareCapabilities,
            EncodingOptions options)
        {
            _mediaEncoder = mediaEncoder;
            _hardwareCapabilities = hardwareCapabilities;
            _options = options;
        }

        public bool SupportsFilter(string filterName) => _mediaEncoder.SupportsFilter(filterName);

        public bool SupportsEncoder(string encoderName) => _mediaEncoder.SupportsEncoder(encoderName);

        public bool SupportsBitStreamFilterOption(Jellyfin.MediaEncoding.Pipeline.BitStreams.BitStreamFilterOption option)
            => _mediaEncoder.SupportsBitStreamFilterWithOption(option switch
            {
                Jellyfin.MediaEncoding.Pipeline.BitStreams.BitStreamFilterOption.HevcMetadataRemoveDovi
                    => BitStreamFilterOptionType.HevcMetadataRemoveDovi,
                Jellyfin.MediaEncoding.Pipeline.BitStreams.BitStreamFilterOption.HevcMetadataRemoveHdr10Plus
                    => BitStreamFilterOptionType.HevcMetadataRemoveHdr10Plus,
                Jellyfin.MediaEncoding.Pipeline.BitStreams.BitStreamFilterOption.Av1MetadataRemoveDovi
                    => BitStreamFilterOptionType.Av1MetadataRemoveDovi,
                Jellyfin.MediaEncoding.Pipeline.BitStreams.BitStreamFilterOption.Av1MetadataRemoveHdr10Plus
                    => BitStreamFilterOptionType.Av1MetadataRemoveHdr10Plus,
                _ => BitStreamFilterOptionType.DoviRpuStrip
            });

        public bool CanPerform(MediaBrowser.Model.MediaEncoding.Hardware.HwVppKind kind, FrameSize size)
            => _hardwareCapabilities.CanFilter(
                _options.HardwareAccelerationType,
                _options,
                kind,
                size.Width,
                size.Height);

        public bool SupportsSurfaceFormat(PipelinePixelFormat format)
        {
            var device = _hardwareCapabilities.GetActiveDevice(_options.HardwareAccelerationType, _options);
            var scale = device?.GetFilter(MediaBrowser.Model.MediaEncoding.Hardware.HwVppKind.Scale);

            return scale is null
                || scale.PixelFormats.Count == 0
                || scale.PixelFormats.Contains(format.Name, StringComparer.OrdinalIgnoreCase);
        }
    }
}
