using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Jellyfin.MediaEncoding.Pipeline.Output;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Controller.Tests.MediaEncoding.FilterChains;

/// <summary>
/// Measures the pipeline package's encoder selection against the legacy one.
/// </summary>
public class VideoEncoderSelectionTests
{
    public static TheoryData<string> AllCases()
    {
        var data = new TheoryData<string>();
        foreach (var testCase in EncoderCorpus.All)
        {
            data.Add(testCase.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllCases))]
    public void Pipeline_ReproducesTheLegacyEncoder(string name)
    {
        var testCase = EncoderCorpus.All.Single(c => string.Equals(c.Name, name, StringComparison.Ordinal));

        Assert.Equal(BuildLegacy(testCase, TestEncodingHelper.Create()), BuildPipeline(testCase));
    }

    [Fact]
    public void Report()
    {
        var helper = TestEncodingHelper.Create();
        var report = new StringBuilder();

        foreach (var testCase in EncoderCorpus.All)
        {
            var legacy = BuildLegacy(testCase, helper);
            var pipeline = BuildPipeline(testCase);

            report.Append(string.Equals(legacy, pipeline, StringComparison.Ordinal) ? "== " : "!= ")
                .Append(testCase.Name)
                .Append("  legacy=").Append(legacy)
                .Append("  pipeline=").AppendLine(pipeline);
        }

        TestContext.Current.TestOutputHelper?.WriteLine(report.ToString());
    }

    private static string BuildLegacy(EncoderCase testCase, EncodingHelper helper)
    {
        var video = new MediaStream
        {
            Index = 0,
            Type = MediaStreamType.Video,
            Codec = "h264",
            Width = 1920,
            Height = 1080
        };

        var state = new EncodingJobInfo(TranscodingJobType.Progressive)
        {
            MediaSource = new MediaSourceInfo { Container = "mkv", MediaStreams = new List<MediaStream> { video } },
            VideoStream = video,
            BaseRequest = new VideoRequestDto(),
            IsVideoRequest = true,
            IsInputVideo = true,
            VideoType = VideoType.VideoFile,
            OutputVideoCodec = testCase.OutputCodec,
            HardwareAccelerationDisabled = testCase.HardwareDisabled
        };

        var options = new EncodingOptions
        {
            HardwareAccelerationType = testCase.Acceleration,
            EnableHardwareEncoding = testCase.HardwareEncoding
        };

        return helper.GetVideoEncoder(state, options);
    }

    private static string BuildPipeline(EncoderCase testCase) => VideoEncoderSelector.Select(
        string.Equals(testCase.OutputCodec, "copy", StringComparison.OrdinalIgnoreCase) ? null : testCase.OutputCodec,
        EncoderCase.AcceleratorFor(testCase.Acceleration),
        new VideoEncoderRequest(testCase.HardwareEncoding, testCase.HardwareDisabled, 8),
        PermissiveCapabilities.Instance).Name;
}
