using System;
using System.IO;
using Jellyfin.MediaEncoding.Keyframes.Mp4;
using Xunit;

namespace Jellyfin.MediaEncoding.Keyframes.Tests.Mp4;

public class Mp4KeyframeExtractorTests
{
    [Fact]
    public void GetKeyframeData_SparseKeyframes_ReturnsPresentationTimes()
    {
        // 30 fps, a keyframe every 270 samples, i.e. every 9 seconds.
        using var stream = new Mp4FileBuilder()
            .AddVideoTrack(
                mediaTimescale: 30,
                mediaDuration: 1800,
                timeToSample: [(1800, 1)],
                syncSamples: [1, 271, 541, 811, 1081, 1351, 1621])
            .Build();

        var result = Mp4KeyframeExtractor.GetKeyframeData(stream);

        Assert.Equal(Seconds(60), result.TotalDuration);
        Assert.Equal(
            [Seconds(0), Seconds(9), Seconds(18), Seconds(27), Seconds(36), Seconds(45), Seconds(54)],
            result.KeyframeTicks);
    }

    [Fact]
    public void GetKeyframeData_VariableSampleDeltas_UsesMatchingRun()
    {
        using var stream = new Mp4FileBuilder()
            .AddVideoTrack(
                mediaTimescale: 100,
                mediaDuration: 1000,
                timeToSample: [(4, 100), (6, 100)],
                syncSamples: [1, 5, 9])
            .Build();

        var result = Mp4KeyframeExtractor.GetKeyframeData(stream);

        Assert.Equal([Seconds(0), Seconds(4), Seconds(8)], result.KeyframeTicks);
    }

    [Fact]
    public void GetKeyframeData_CompositionOffsets_AddsOffsetOfMatchingRun()
    {
        using var stream = new Mp4FileBuilder()
            .AddVideoTrack(
                mediaTimescale: 100,
                mediaDuration: 1000,
                timeToSample: [(10, 100)],
                syncSamples: [1, 6],
                compositionOffsets: [(5, 200), (5, 0)])
            .Build();

        var result = Mp4KeyframeExtractor.GetKeyframeData(stream);

        // Sample 1 is shifted by two seconds, sample 6 is not shifted at all.
        Assert.Equal([Seconds(2), Seconds(5)], result.KeyframeTicks);
    }

    [Fact]
    public void GetKeyframeData_EditListTrimsCompositionDelay_ShiftsKeyframes()
    {
        using var stream = new Mp4FileBuilder()
            .AddVideoTrack(
                mediaTimescale: 100,
                mediaDuration: 1000,
                timeToSample: [(10, 100)],
                syncSamples: [1, 6],
                compositionOffsets: [(10, 200)],
                editList: [(10000, 200)])
            .Build();

        var result = Mp4KeyframeExtractor.GetKeyframeData(stream);

        // The edit removes the composition delay, so the presentation still starts at zero
        // and keeps its full length.
        Assert.Equal([Seconds(0), Seconds(5)], result.KeyframeTicks);
        Assert.Equal(Seconds(10), result.TotalDuration);
    }

    [Fact]
    public void GetKeyframeData_EmptyEdit_DelaysPresentation()
    {
        using var stream = new Mp4FileBuilder()
            .AddVideoTrack(
                mediaTimescale: 100,
                mediaDuration: 1000,
                timeToSample: [(10, 100)],
                syncSamples: [1, 6],
                editList: [(2000, -1), (10000, 0)])
            .Build();

        var result = Mp4KeyframeExtractor.GetKeyframeData(stream);

        Assert.Equal([Seconds(2), Seconds(7)], result.KeyframeTicks);
        Assert.Equal(Seconds(12), result.TotalDuration);
    }

    [Fact]
    public void GetKeyframeData_TrimmingEditList_CapsDuration()
    {
        using var stream = new Mp4FileBuilder()
            .AddVideoTrack(
                mediaTimescale: 100,
                mediaDuration: 1000,
                timeToSample: [(10, 100)],
                syncSamples: [1, 6],
                editList: [(4000, 0)])
            .Build();

        var result = Mp4KeyframeExtractor.GetKeyframeData(stream);

        Assert.Equal(Seconds(4), result.TotalDuration);
    }

    [Fact]
    public void GetKeyframeData_SkipsNonVideoTracks()
    {
        using var stream = new Mp4FileBuilder()
            .AddVideoTrack(
                mediaTimescale: 100,
                mediaDuration: 1000,
                timeToSample: [(10, 100)],
                syncSamples: [1, 6],
                handler: "soun")
            .AddVideoTrack(
                mediaTimescale: 100,
                mediaDuration: 2000,
                timeToSample: [(20, 100)],
                syncSamples: [1, 11])
            .Build();

        var result = Mp4KeyframeExtractor.GetKeyframeData(stream);

        Assert.Equal(Seconds(20), result.TotalDuration);
        Assert.Equal([Seconds(0), Seconds(10)], result.KeyframeTicks);
    }

    [Fact]
    public void GetKeyframeData_PartialSyncSamples_AreKeyframesToo()
    {
        using var stream = new Mp4FileBuilder()
            .AddVideoTrack(
                mediaTimescale: 100,
                mediaDuration: 1000,
                timeToSample: [(10, 100)],
                syncSamples: [1, 7],
                partialSyncSamples: [4])
            .Build();

        var result = Mp4KeyframeExtractor.GetKeyframeData(stream);

        Assert.Equal([Seconds(0), Seconds(3), Seconds(6)], result.KeyframeTicks);
    }

    [Fact]
    public void GetKeyframeData_RandomAccessPointGroup_ReturnsNoKeyframes()
    {
        // The group marks keyframes this extractor does not model, and reporting a subset would
        // describe segments that are longer than the ones the muxer cuts.
        using var stream = new Mp4FileBuilder()
            .AddVideoTrack(
                mediaTimescale: 100,
                mediaDuration: 1000,
                timeToSample: [(10, 100)],
                syncSamples: [1, 6],
                randomAccessPointGroup: true)
            .Build();

        Assert.Empty(Mp4KeyframeExtractor.GetKeyframeData(stream).KeyframeTicks);
    }

    [Fact]
    public void GetKeyframeData_WithoutSyncSampleBox_ReturnsNoKeyframes()
    {
        // Every sample is a keyframe, so the muxer can cut anywhere and equally sized
        // segments already describe the output.
        using var stream = new Mp4FileBuilder()
            .AddVideoTrack(
                mediaTimescale: 100,
                mediaDuration: 1000,
                timeToSample: [(10, 100)],
                syncSamples: [])
            .Build();

        Assert.Empty(Mp4KeyframeExtractor.GetKeyframeData(stream).KeyframeTicks);
    }

    [Fact]
    public void GetKeyframeData_FragmentedFile_ReturnsNoKeyframes()
    {
        using var stream = new Mp4FileBuilder
        {
            Fragmented = true
        }
            .AddVideoTrack(
                mediaTimescale: 100,
                mediaDuration: 1000,
                timeToSample: [(10, 100)],
                syncSamples: [1, 6])
            .Build();

        Assert.Empty(Mp4KeyframeExtractor.GetKeyframeData(stream).KeyframeTicks);
    }

    [Fact]
    public void GetKeyframeData_NotAnMp4_ReturnsNoKeyframes()
    {
        using var stream = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8, 9]);

        Assert.Empty(Mp4KeyframeExtractor.GetKeyframeData(stream).KeyframeTicks);
    }

    [Fact]
    public void GetKeyframeData_FileFromAMuxer_MatchesFfprobe()
    {
        // The synthetic files above are written to this parser's own reading of the format, so one
        // real file guards against both sharing the same misunderstanding. Regenerate with:
        // ffmpeg -f lavfi -i "color=c=black:s=64x64:r=5:d=60" -c:v libx264 -preset ultrafast -crf 51 -g 45 -keyint_min 45 -pix_fmt yuv420p keyframes_sparse.mp4
        using var stream = File.OpenRead(Path.Combine("Mp4", "Test Data", "keyframes_sparse.mp4"));

        var result = Mp4KeyframeExtractor.GetKeyframeData(stream);

        // ffprobe -select_streams v:0 -skip_frame nokey reports exactly these presentation times.
        Assert.Equal(Seconds(60), result.TotalDuration);
        Assert.Equal(
            [Seconds(0), Seconds(9), Seconds(18), Seconds(27), Seconds(36), Seconds(45), Seconds(54)],
            result.KeyframeTicks);
    }

    private static long Seconds(double value) => Convert.ToInt64(value * TimeSpan.TicksPerSecond);
}
