using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaEncoding.Hardware;
using MediaBrowser.Model.MediaInfo;
using Moq;
using Xunit;

using IConfiguration = Microsoft.Extensions.Configuration.IConfiguration;

namespace Jellyfin.Controller.Tests.MediaEncoding;

public class EncodingHelperHwPipelineTests
{
    public static TheoryData<HardwareAccelerationType> AllAccelerationTypes() =>
    [
        HardwareAccelerationType.none,
        HardwareAccelerationType.qsv,
        HardwareAccelerationType.nvenc,
        HardwareAccelerationType.amf,
        HardwareAccelerationType.vaapi,
        HardwareAccelerationType.videotoolbox,
        HardwareAccelerationType.rkmpp
    ];

    [Theory]
    [MemberData(nameof(AllAccelerationTypes))]
    public void GetVideoProcessingFilterParam_FullSideBySide_AlwaysFlattensToOneView(HardwareAccelerationType type)
    {
        var state = BuildState(Video3DFormat.FullSideBySide);
        var filters = CreateHelper().GetVideoProcessingFilterParam(state, BuildOptions(type), "libx264");

        // Either the software crop or the hardware crop rectangle, but never neither.
        Assert.True(
            filters.Contains("crop=trunc(iw/4)*2:ih:0:0", StringComparison.Ordinal)
            || filters.Contains("cw=1920:ch=1080:cx=0:cy=0", StringComparison.Ordinal)
            || filters.Contains("crop_w=1920:crop_h=1080", StringComparison.Ordinal),
            $"{type} emitted no crop: {filters}");
    }

    [Theory]
    [MemberData(nameof(AllAccelerationTypes))]
    public void GetVideoProcessingFilterParam_FullSideBySide_NeverCropsTwice(HardwareAccelerationType type)
    {
        var state = BuildState(Video3DFormat.FullSideBySide);
        var filters = CreateHelper().GetVideoProcessingFilterParam(state, BuildOptions(type), "libx264");

        var softwareCrop = filters.Contains("crop=trunc(iw/4)*2:ih:0:0", StringComparison.Ordinal);
        var hardwareCrop = filters.Contains("cw=1920:ch=1080", StringComparison.Ordinal)
            || filters.Contains("crop_w=1920:crop_h=1080", StringComparison.Ordinal);

        Assert.False(softwareCrop && hardwareCrop, $"{type} cropped twice: {filters}");
    }

    [Theory]
    [MemberData(nameof(AllAccelerationTypes))]
    public void GetVideoProcessingFilterParam_NotThreeD_EmitsNoCrop(HardwareAccelerationType type)
    {
        var state = BuildState(null);
        var filters = CreateHelper().GetVideoProcessingFilterParam(state, BuildOptions(type), "libx264");

        Assert.DoesNotContain("crop=trunc(iw/4)*2", filters, StringComparison.Ordinal);
        Assert.DoesNotContain("cw=1920:ch=1080", filters, StringComparison.Ordinal);
        Assert.DoesNotContain("crop_w=1920", filters, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AllAccelerationTypes))]
    public void GetVideoProcessingFilterParam_ThreeDWithGraphicalSubs_ScalesSubsToTheFlattenedFrame(HardwareAccelerationType type)
    {
        var state = BuildState(Video3DFormat.FullSideBySide, withGraphicalSubtitle: true);
        var filters = CreateHelper().GetVideoProcessingFilterParam(state, BuildOptions(type), "libx264");

        // The packed width must never reach the subtitle pre-processing, only the flattened one.
        Assert.DoesNotContain("3840", filters, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AllAccelerationTypes))]
    public void GetVideoProcessingFilterParam_DecoderCannotHandleTheSize_CropsOnceInSoftware(HardwareAccelerationType type)
    {
        var capabilities = new Mock<IHardwareCapabilitiesProvider>();
        capabilities
            .Setup(p => p.CanDecode(
                It.IsAny<HardwareAccelerationType>(),
                It.IsAny<EncodingOptions>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>()))
            .Returns(false);
        capabilities
            .Setup(p => p.CanFilter(
                It.IsAny<HardwareAccelerationType>(),
                It.IsAny<EncodingOptions>(),
                It.IsAny<HwVppKind>(),
                It.IsAny<int>(),
                It.IsAny<int>()))
            .Returns(true);

        var state = BuildState(Video3DFormat.FullSideBySide);
        var filters = CreateHelper(capabilities.Object).GetVideoProcessingFilterParam(state, BuildOptions(type), "libx264");

        var crops = filters.Split("crop=trunc(iw/4)*2:ih:0:0", StringSplitOptions.None).Length - 1;
        Assert.True(crops == 1, $"{type} emitted {crops} crops: {filters}");
    }

    [Fact]
    public void GetVideoProcessingFilterParam_HardwareDisabledOnTheJob_StaysInSoftware()
    {
        var state = BuildState(Video3DFormat.FullSideBySide);
        state.HardwareAccelerationDisabled = true;

        var filters = CreateHelper().GetVideoProcessingFilterParam(state, BuildOptions(HardwareAccelerationType.qsv), "libx264");

        Assert.Contains("crop=trunc(iw/4)*2:ih:0:0", filters, StringComparison.Ordinal);
        Assert.DoesNotContain("_qsv", filters, StringComparison.Ordinal);
    }

    private static EncodingOptions BuildOptions(HardwareAccelerationType type) => new()
    {
        HardwareAccelerationType = type,
        EnableHardwareEncoding = true,
        HardwareDecodingCodecs = ["h264", "hevc"]
    };

    private static EncodingJobInfo BuildState(Video3DFormat? threeDFormat, bool withGraphicalSubtitle = false)
    {
        var video = new MediaStream
        {
            Index = 0,
            Type = MediaStreamType.Video,
            Codec = "h264",
            Width = 3840,
            Height = 1080,
            PixelFormat = "yuv420p"
        };

        var streams = new List<MediaStream> { video };
        MediaStream? subtitle = null;
        if (withGraphicalSubtitle)
        {
            subtitle = new MediaStream
            {
                Index = 1,
                Type = MediaStreamType.Subtitle,
                Codec = "pgssub",
                Width = 1920,
                Height = 1080
            };

            streams.Add(subtitle);
        }

        return new EncodingJobInfo(TranscodingJobType.Progressive)
        {
            MediaSource = new MediaSourceInfo
            {
                Container = "mkv",
                MediaStreams = streams,
                Video3DFormat = threeDFormat
            },
            VideoStream = video,
            SubtitleStream = subtitle,
            SubtitleDeliveryMethod = withGraphicalSubtitle ? SubtitleDeliveryMethod.Encode : SubtitleDeliveryMethod.Drop,
            BaseRequest = new VideoRequestDto(),
            IsVideoRequest = true,
            IsInputVideo = true,
            VideoType = VideoType.VideoFile,
            OutputVideoCodec = "h264"
        };
    }

    /// <summary>
    /// Builds a helper over an ffmpeg that has every codec and filter, so the hardware chains are reached.
    /// </summary>
    /// <param name="capabilities">The capability provider, permissive when not given.</param>
    /// <returns>The helper.</returns>
    private static EncodingHelper CreateHelper(IHardwareCapabilitiesProvider? capabilities = null)
    {
        var mediaEncoder = new Mock<IMediaEncoder>();
        mediaEncoder.SetupGet(e => e.EncoderVersion).Returns(new Version(8, 1, 2));
        mediaEncoder.Setup(e => e.SupportsEncoder(It.IsAny<string>())).Returns(true);
        mediaEncoder.Setup(e => e.SupportsDecoder(It.IsAny<string>())).Returns(true);
        mediaEncoder.Setup(e => e.SupportsHwaccel(It.IsAny<string>())).Returns(true);
        mediaEncoder.Setup(e => e.SupportsFilter(It.IsAny<string>())).Returns(true);
        mediaEncoder.Setup(e => e.SupportsFilterWithOption(It.IsAny<FilterOptionType>())).Returns(true);

        return new EncodingHelper(
            Mock.Of<IApplicationPaths>(),
            mediaEncoder.Object,
            Mock.Of<ISubtitleEncoder>(),
            Mock.Of<IConfiguration>(),
            Mock.Of<IConfigurationManager>(),
            Mock.Of<IPathManager>(),
            capabilities ?? HardwareCapabilitiesMock.CreatePermissive());
    }
}
