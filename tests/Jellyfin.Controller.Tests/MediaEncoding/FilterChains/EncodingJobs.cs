using System.Collections.Generic;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Controller.Tests.MediaEncoding.FilterChains;

/// <summary>
/// Turns a case into the job <see cref="EncodingHelper"/> takes, and asks it for the arguments.
/// </summary>
internal static class EncodingJobs
{
    public static string BuildFullParam(TranscodeCase testCase, EncodingHelper helper)
        => helper.GetVideoProcessingFilterParam(BuildState(testCase), BuildOptions(testCase), OutputCodecName(testCase)).Trim();

    public static string BuildInputArgs(TranscodeCase testCase, EncodingHelper helper)
        => helper.GetInputVideoHwaccelArgs(BuildState(testCase), BuildOptions(testCase));

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
