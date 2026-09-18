using System.Collections.Generic;
using Jellyfin.MediaEncoding.Pipeline.BitStreams;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Controller.Tests.MediaEncoding.FilterChains;

/// <summary>
/// A stream being copied, described in terms both bitstream filter builders understand.
/// </summary>
internal sealed record BitStreamCase
{
    public required string Name { get; init; }

    public string Codec { get; init; } = "hevc";

    public MediaStreamType StreamType { get; init; } = MediaStreamType.Video;

    public int? DvProfile { get; init; }

    public int? DvBlSignalCompatibilityId { get; init; }

    public bool DoviFlags { get; init; }

    public bool Hdr10Plus { get; init; }

    public bool PqTransfer { get; init; }

    public string? RequestedRangeTypes { get; init; }

    public override string ToString() => Name;

    public string BuildLegacy(EncodingHelper helper)
        => helper.GetBitStreamArgs(BuildState(), StreamType) ?? string.Empty;

    public string BuildPipeline(bool bitStreamFilterOptions = true)
    {
        var stream = BuildStream();
        var copied = new CopiedTrack(
            Codec,
            stream.VideoRange,
            stream.VideoRangeType,
            (RequestedRangeTypes ?? string.Empty).Split(['|', ','], System.StringSplitOptions.RemoveEmptyEntries));

        var capabilities = bitStreamFilterOptions
            ? PermissiveCapabilities.Instance
            : PermissiveCapabilities.WithoutBitStreamFilterOptions;

        return BitStreamFilters.For(copied, capabilities) ?? string.Empty;
    }

    private MediaStream BuildStream() => new()
    {
        Index = 0,
        Type = StreamType,
        Codec = Codec,
        Width = 1920,
        Height = 1080,
        BitDepth = 10,
        DvProfile = DvProfile,
        DvBlSignalCompatibilityId = DvBlSignalCompatibilityId,
        RpuPresentFlag = DoviFlags ? 1 : 0,
        BlPresentFlag = DoviFlags ? 1 : 0,
        Hdr10PlusPresentFlag = Hdr10Plus ? true : null,
        ColorTransfer = PqTransfer ? "smpte2084" : null,
        ColorSpace = PqTransfer ? "bt2020nc" : null,
        ColorPrimaries = PqTransfer ? "bt2020" : null
    };

    private EncodingJobInfo BuildState()
    {
        var stream = BuildStream();
        var video = StreamType == MediaStreamType.Video ? stream : new MediaStream
        {
            Index = 0,
            Type = MediaStreamType.Video,
            Codec = "hevc",
            Width = 1920,
            Height = 1080
        };

        return new EncodingJobInfo(TranscodingJobType.Progressive)
        {
            MediaSource = new MediaSourceInfo
            {
                Container = "mkv",
                MediaStreams = new List<MediaStream> { video }
            },
            VideoStream = video,
            AudioStream = StreamType == MediaStreamType.Audio ? stream : null,
            BaseRequest = new VideoRequestDto { VideoRangeType = RequestedRangeTypes },
            IsVideoRequest = true,
            IsInputVideo = true,
            VideoType = VideoType.VideoFile,
            OutputVideoCodec = "copy"
        };
    }
}
