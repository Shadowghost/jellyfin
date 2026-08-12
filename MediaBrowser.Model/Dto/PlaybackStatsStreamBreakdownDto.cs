using System;
using System.Collections.Generic;

namespace MediaBrowser.Model.Dto;

/// <summary>
/// Breakdown of the selected-source stream characteristics across a filter window,
/// plus the direct-play vs transcode split.
/// </summary>
public class PlaybackStatsStreamBreakdownDto
{
    /// <summary>
    /// Gets or sets the selected-source video resolution distribution.
    /// </summary>
    public IReadOnlyList<NameCountDto> Resolutions { get; set; } = Array.Empty<NameCountDto>();

    /// <summary>
    /// Gets or sets the delivered (post-transcode) video resolution distribution.
    /// </summary>
    public IReadOnlyList<NameCountDto> DeliveredResolutions { get; set; } = Array.Empty<NameCountDto>();

    /// <summary>
    /// Gets or sets the selected-source video range distribution (SDR/HDR types).
    /// </summary>
    public IReadOnlyList<NameCountDto> VideoRanges { get; set; } = Array.Empty<NameCountDto>();

    /// <summary>
    /// Gets or sets the delivered video range distribution. Output range isn't tracked for transcodes,
    /// so transcoded sessions contribute an "Unknown" bucket here.
    /// </summary>
    public IReadOnlyList<NameCountDto> DeliveredVideoRanges { get; set; } = Array.Empty<NameCountDto>();

    /// <summary>
    /// Gets or sets the selected-source video codec distribution.
    /// </summary>
    public IReadOnlyList<NameCountDto> VideoCodecs { get; set; } = Array.Empty<NameCountDto>();

    /// <summary>
    /// Gets or sets the delivered (post-transcode) video codec distribution.
    /// </summary>
    public IReadOnlyList<NameCountDto> DeliveredVideoCodecs { get; set; } = Array.Empty<NameCountDto>();

    /// <summary>
    /// Gets or sets the selected-source audio codec distribution.
    /// </summary>
    public IReadOnlyList<NameCountDto> AudioCodecs { get; set; } = Array.Empty<NameCountDto>();

    /// <summary>
    /// Gets or sets the delivered (post-transcode) audio codec distribution.
    /// </summary>
    public IReadOnlyList<NameCountDto> DeliveredAudioCodecs { get; set; } = Array.Empty<NameCountDto>();

    /// <summary>
    /// Gets or sets the selected-source audio channel-layout distribution.
    /// </summary>
    public IReadOnlyList<NameCountDto> AudioChannels { get; set; } = Array.Empty<NameCountDto>();

    /// <summary>
    /// Gets or sets the delivered (post-transcode) audio channel-layout distribution.
    /// </summary>
    public IReadOnlyList<NameCountDto> DeliveredAudioChannels { get; set; } = Array.Empty<NameCountDto>();

    /// <summary>
    /// Gets or sets the audio language distribution.
    /// </summary>
    public IReadOnlyList<NameCountDto> AudioLanguages { get; set; } = Array.Empty<NameCountDto>();

    /// <summary>
    /// Gets or sets the subtitle language distribution.
    /// </summary>
    public IReadOnlyList<NameCountDto> SubtitleLanguages { get; set; } = Array.Empty<NameCountDto>();

    /// <summary>
    /// Gets or sets the number of direct (non-transcoded) plays.
    /// </summary>
    public int DirectPlays { get; set; }

    /// <summary>
    /// Gets or sets the number of transcoded plays.
    /// </summary>
    public int TranscodedPlays { get; set; }
}
