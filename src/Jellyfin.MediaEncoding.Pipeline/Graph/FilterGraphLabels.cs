namespace Jellyfin.MediaEncoding.Pipeline.Graph;

/// <summary>
/// Which inputs the branches of a filter graph read from.
/// </summary>
/// <param name="VideoStreamIndex">The index of the video stream within the first input.</param>
/// <param name="SubtitleStreamIndex">The index of the subtitle stream.</param>
/// <param name="SubtitleInputIndex">The input the subtitle stream belongs to, which is a second file when it is external.</param>
public readonly record struct FilterGraphLabels(
    int VideoStreamIndex,
    int SubtitleStreamIndex,
    int SubtitleInputIndex);
