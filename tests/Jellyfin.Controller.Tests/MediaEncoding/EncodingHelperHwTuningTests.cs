using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaEncoding.Hardware;
using Moq;
using Xunit;

using IConfiguration = Microsoft.Extensions.Configuration.IConfiguration;

namespace Jellyfin.Controller.Tests.MediaEncoding;

public class EncodingHelperHwTuningTests
{
    [Fact]
    public void GetHwaccelType_Manual_HonoursTheConfiguredCodecList()
    {
        var options = BuildOptions(HardwareTuningMode.Manual);
        options.HardwareDecodingCodecs = ["h264"];

        var helper = CreateHelper(isPopulated: true);

        Assert.NotNull(helper.GetHwaccelType(BuildState("h264"), options, "h264", 8, true));
        Assert.Null(helper.GetHwaccelType(BuildState("hevc"), options, "hevc", 8, true));
    }

    [Fact]
    public void GetHwaccelType_Auto_IgnoresTheConfiguredCodecList()
    {
        var options = BuildOptions(HardwareTuningMode.Auto);
        options.HardwareDecodingCodecs = ["h264"];

        var helper = CreateHelper(isPopulated: true);

        // The detected decoders decide, so a codec the user never ticked is still accelerated.
        Assert.NotNull(helper.GetHwaccelType(BuildState("hevc"), options, "hevc", 8, true));
    }

    [Fact]
    public void GetHwaccelType_Auto_StillDefersToTheReportedDecoders()
    {
        var options = BuildOptions(HardwareTuningMode.Auto);
        options.HardwareDecodingCodecs = ["h264", "hevc"];

        var helper = CreateHelper(isPopulated: true, canDecode: false);

        Assert.Null(helper.GetHwaccelType(BuildState("hevc"), options, "hevc", 8, true));
    }

    [Fact]
    public void GetHwaccelType_AutoWithoutAReport_FallsBackToTheConfiguredCodecList()
    {
        var options = BuildOptions(HardwareTuningMode.Auto);
        options.HardwareDecodingCodecs = ["h264"];

        // Nothing was detected, so the configuration is all there is to go on.
        var helper = CreateHelper(isPopulated: false);

        Assert.Null(helper.GetHwaccelType(BuildState("hevc"), options, "hevc", 8, true));
    }

    [Fact]
    public void GetHwaccelType_AutoWithDetectionDisabled_FallsBackToTheConfiguredCodecList()
    {
        var options = BuildOptions(HardwareTuningMode.Auto);
        options.HardwareCapabilityDetection = HardwareCapabilityDetectionMode.Disabled;
        options.HardwareDecodingCodecs = ["h264"];

        var helper = CreateHelper(isPopulated: true);

        Assert.Null(helper.GetHwaccelType(BuildState("hevc"), options, "hevc", 8, true));
    }

    [Theory]
    [InlineData(HardwareTuningMode.Manual, false, null)]
    [InlineData(HardwareTuningMode.Manual, true, " -hwaccel vaapi")]
    [InlineData(HardwareTuningMode.Auto, false, " -hwaccel vaapi")]
    public void GetHwaccelType_TenBitHevc_FollowsTheTuningMode(HardwareTuningMode tuning, bool allowTenBit, string? expectedPrefix)
    {
        var options = BuildOptions(tuning);
        options.HardwareDecodingCodecs = ["hevc"];
        options.EnableDecodingColorDepth10Hevc = allowTenBit;

        var result = CreateHelper(isPopulated: true).GetHwaccelType(BuildState("hevc"), options, "hevc", 10, false);

        if (expectedPrefix is null)
        {
            Assert.Null(result);
        }
        else
        {
            Assert.StartsWith(expectedPrefix, result, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(true, "h264_vaapi")]
    [InlineData(false, "libx264")]
    public void GetVideoEncoder_FallsBackToSoftwareWhenTheDeviceCannotEncodeTheOutput(bool canEncode, string expected)
    {
        var options = BuildOptions(HardwareTuningMode.Auto);
        var helper = CreateHelper(isPopulated: true, canEncode: canEncode);

        Assert.Equal(expected, helper.GetVideoEncoder(BuildState("h264"), options));
    }

    private static EncodingOptions BuildOptions(HardwareTuningMode tuning) => new()
    {
        HardwareAccelerationType = HardwareAccelerationType.vaapi,
        HardwareTuning = tuning,
        EnableHardwareEncoding = true,
        EnableDecodingColorDepth10Hevc = false
    };

    private static EncodingJobInfo BuildState(string codec)
    {
        var video = new MediaStream
        {
            Index = 0,
            Type = MediaStreamType.Video,
            Codec = codec,
            Width = 1920,
            Height = 1080,
            PixelFormat = "yuv420p"
        };

        return new EncodingJobInfo(TranscodingJobType.Progressive)
        {
            MediaSource = new MediaSourceInfo { Container = "mkv", MediaStreams = new List<MediaStream> { video } },
            VideoStream = video,
            BaseRequest = new VideoRequestDto(),
            IsVideoRequest = true,
            IsInputVideo = true,
            VideoType = VideoType.VideoFile,
            OutputVideoCodec = "h264"
        };
    }

    private static EncodingHelper CreateHelper(bool isPopulated, bool canDecode = true, bool canEncode = true)
    {
        var capabilities = new Mock<IHardwareCapabilitiesProvider>();
        capabilities.SetupGet(p => p.IsPopulated).Returns(isPopulated);
        capabilities
            .Setup(p => p.CanEncode(
                It.IsAny<HardwareAccelerationType>(),
                It.IsAny<EncodingOptions>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(canEncode);
        capabilities
            .Setup(p => p.CanDecode(
                It.IsAny<HardwareAccelerationType>(),
                It.IsAny<EncodingOptions>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(canDecode);
        capabilities
            .Setup(p => p.CanFilter(
                It.IsAny<HardwareAccelerationType>(),
                It.IsAny<EncodingOptions>(),
                It.IsAny<HwVppKind>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>()))
            .Returns(true);

        var mediaEncoder = new Mock<IMediaEncoder>();
        mediaEncoder.SetupGet(e => e.EncoderVersion).Returns(new Version(8, 1, 2));
        mediaEncoder.Setup(e => e.SupportsHwaccel(It.IsAny<string>())).Returns(true);
        mediaEncoder.Setup(e => e.SupportsFilter(It.IsAny<string>())).Returns(true);
        mediaEncoder.Setup(e => e.SupportsFilterWithOption(It.IsAny<FilterOptionType>())).Returns(true);
        mediaEncoder.Setup(e => e.SupportsEncoder(It.IsAny<string>())).Returns(true);

        return new EncodingHelper(
            Mock.Of<IApplicationPaths>(),
            mediaEncoder.Object,
            Mock.Of<ISubtitleEncoder>(),
            Mock.Of<IConfiguration>(),
            Mock.Of<IConfigurationManager>(),
            Mock.Of<IPathManager>(),
            capabilities.Object);
    }
}
