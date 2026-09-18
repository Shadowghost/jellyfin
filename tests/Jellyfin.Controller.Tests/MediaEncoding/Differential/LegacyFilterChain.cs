using System.Collections.Generic;
using System.Linq;
using Jellyfin.MediaEncoding.Pipeline.Frames;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Controller.Tests.MediaEncoding.Differential;

/// <summary>
/// Runs a case through the vendor chains in <see cref="EncodingHelper"/>, which stay the oracle
/// until a vendor is cut over to the pipeline package.
/// </summary>
internal static class LegacyFilterChain
{
    public static string Build(TranscodeCase testCase, EncodingHelper helper)
    {
        var state = BuildState(testCase);
        var options = BuildOptions(testCase);
        var outputCodec = OutputCodecName(testCase);

        if (testCase.Variant != ChainVariant.Auto)
        {
            var decoder = testCase.EnableHardwareDecoding ? DecoderArgsFor(testCase.Variant) : string.Empty;
            var variantChains = testCase.Variant switch
            {
                ChainVariant.VideoToolbox => helper.GetAppleVidFiltersPreferred(state, options, decoder, outputCodec),
                ChainVariant.AmdD3d11 => helper.GetAmdDx11VidFiltersPrefered(state, options, decoder, outputCodec),
                ChainVariant.QsvD3d11 => helper.GetIntelQsvDx11VidFiltersPrefered(state, options, decoder, outputCodec),
                ChainVariant.VaapiIntelFull => helper.GetIntelVaapiFullVidFiltersPrefered(state, options, decoder, outputCodec),
                _ => helper.GetAmdVaapiFullVidFiltersPrefered(state, options, decoder, outputCodec)
            };

            return string.Join(',', variantChains.Main.Where(f => !string.IsNullOrEmpty(f)));
        }

        var chains = options.HardwareAccelerationType switch
        {
            HardwareAccelerationType.vaapi => helper.GetVaapiVidFilterChain(state, options, outputCodec),
            HardwareAccelerationType.amf => helper.GetAmdVidFilterChain(state, options, outputCodec),
            HardwareAccelerationType.qsv => helper.GetIntelVidFilterChain(state, options, outputCodec),
            HardwareAccelerationType.nvenc => helper.GetNvidiaVidFilterChain(state, options, outputCodec),
            HardwareAccelerationType.videotoolbox => helper.GetAppleVidFilterChain(state, options, outputCodec),
            HardwareAccelerationType.rkmpp => helper.GetRkmppVidFilterChain(state, options, outputCodec),
            _ => helper.GetSwVidFilterChain(state, options, outputCodec)
        };

        return string.Join(',', chains.Main.Where(f => !string.IsNullOrEmpty(f)));
    }

    public static string BuildFullParam(TranscodeCase testCase, EncodingHelper helper)
        => helper.GetVideoProcessingFilterParam(BuildState(testCase), BuildOptions(testCase), OutputCodecName(testCase)).Trim();

    public static string BuildInputArgs(TranscodeCase testCase, EncodingHelper helper)
        => helper.GetInputVideoHwaccelArgs(BuildState(testCase), BuildOptions(testCase));

    /// <summary>
    /// The decoder arguments the matching hardware decoder would have produced, which the vendor
    /// chains only ever inspect for the device name.
    /// </summary>
    private static string DecoderArgsFor(ChainVariant variant) => variant switch
    {
        ChainVariant.VideoToolbox => " -hwaccel videotoolbox -hwaccel_output_format videotoolbox",
        ChainVariant.AmdD3d11 => " -hwaccel d3d11va -hwaccel_output_format d3d11",
        ChainVariant.QsvD3d11 => " -hwaccel qsv -hwaccel_output_format qsv",
        _ => " -hwaccel vaapi -hwaccel_output_format vaapi"
    };

    public static string OutputCodecName(TranscodeCase testCase)
    {
        if (testCase.Mjpeg)
        {
            var hasMjpegEncoder = testCase.Acceleration is HardwareAccelerationType.vaapi
                or HardwareAccelerationType.qsv
                or HardwareAccelerationType.videotoolbox
                or HardwareAccelerationType.rkmpp;

            return testCase.EnableHardwareEncoding && hasMjpegEncoder
                ? "mjpeg_" + testCase.Acceleration
                : "mjpeg";
        }

        if (!testCase.EnableHardwareEncoding)
        {
            return "libx264";
        }

        return testCase.Acceleration switch
        {
            HardwareAccelerationType.qsv => "h264_qsv",
            HardwareAccelerationType.vaapi => "h264_vaapi",
            HardwareAccelerationType.nvenc => "h264_nvenc",
            HardwareAccelerationType.amf => "h264_amf",
            HardwareAccelerationType.videotoolbox => "h264_videotoolbox",
            HardwareAccelerationType.rkmpp => "h264_rkmpp",
            _ => "libx264"
        };
    }

    public static EncodingOptions BuildOptions(TranscodeCase testCase) => new()
    {
        HardwareAccelerationType = testCase.Acceleration,
        EnableHardwareEncoding = testCase.EnableHardwareEncoding,
        EnableTonemapping = testCase.EnableTonemapping,
        HardwareDecodingCodecs = testCase.EnableHardwareDecoding ? ["h264", "hevc"] : []
    };

    public static EncodingJobInfo BuildState(TranscodeCase testCase)
    {
        var video = new MediaStream
        {
            Index = 0,
            Type = MediaStreamType.Video,
            Codec = testCase.InputCodec,
            Width = testCase.InputWidth,
            Height = testCase.InputHeight,
            PixelFormat = testCase.InputPixelFormat,
            IsInterlaced = testCase.IsInterlaced,
            Rotation = testCase.Rotation,
            BitDepth = testCase.IsHdr10 ? 10 : 8,
            ColorTransfer = testCase.IsHdr10 ? "smpte2084" : null,
            ColorSpace = testCase.IsHdr10 ? "bt2020nc" : null,
            ColorPrimaries = testCase.IsHdr10 ? "bt2020" : null
        };

        MediaStream? subtitle = testCase.Subtitle switch
        {
            SubtitleKind.Graphical => new MediaStream
            {
                Index = 1,
                Type = MediaStreamType.Subtitle,
                Codec = "pgssub",
                Width = 1920,
                Height = 1080
            },
            SubtitleKind.Text => new MediaStream
            {
                Index = 1,
                Type = MediaStreamType.Subtitle,
                Codec = "ass",
                Path = "/tmp/subs.ass",
                IsExternal = false
            },
            _ => null
        };

        var streams = new List<MediaStream> { video };
        if (subtitle is not null)
        {
            streams.Add(subtitle);
        }

        return new EncodingJobInfo(TranscodingJobType.Progressive)
        {
            MediaSource = new MediaSourceInfo
            {
                Container = "mkv",
                MediaStreams = streams,
                Video3DFormat = testCase.Video3DFormat
            },
            VideoStream = video,
            SubtitleStream = subtitle,
            SubtitleDeliveryMethod = subtitle is null ? SubtitleDeliveryMethod.Drop : SubtitleDeliveryMethod.Encode,
            BaseRequest = new VideoRequestDto
            {
                Width = testCase.RequestedWidth,
                Height = testCase.RequestedHeight,
                MaxWidth = testCase.RequestedMaxWidth,
                MaxHeight = testCase.RequestedMaxHeight
            },
            IsVideoRequest = true,
            IsInputVideo = true,
            VideoType = VideoType.VideoFile,
            OutputVideoCodec = OutputCodecName(testCase)
        };
    }
}
