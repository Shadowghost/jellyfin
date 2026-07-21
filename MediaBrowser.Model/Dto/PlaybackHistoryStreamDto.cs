using Jellyfin.Database.Implementations.Enums;

namespace MediaBrowser.Model.Dto;

/// <summary>
/// Per-stream details of a playback history session.
/// </summary>
public class PlaybackHistoryStreamDto
{
    /// <summary>
    /// Gets or sets the stream type.
    /// </summary>
    public PlaybackHistoryStreamType StreamType { get; set; }

    /// <summary>
    /// Gets or sets the stream origin (selected source or delivered).
    /// </summary>
    public PlaybackHistoryStreamOrigin Origin { get; set; }

    /// <summary>
    /// Gets or sets the video width in pixels.
    /// </summary>
    public int? Width { get; set; }

    /// <summary>
    /// Gets or sets the video resolution proxy (height in pixels).
    /// </summary>
    public int? Height { get; set; }

    /// <summary>
    /// Gets or sets the video range.
    /// </summary>
    public string? VideoRange { get; set; }

    /// <summary>
    /// Gets or sets the codec.
    /// </summary>
    public string? Codec { get; set; }

    /// <summary>
    /// Gets or sets the stream bitrate in bits per second.
    /// </summary>
    public int? Bitrate { get; set; }

    /// <summary>
    /// Gets or sets the audio channel count.
    /// </summary>
    public int? Channels { get; set; }

    /// <summary>
    /// Gets or sets the ISO language.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the subtitle track is forced.
    /// </summary>
    public bool? IsForced { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the subtitle track is for the hearing impaired.
    /// </summary>
    public bool? IsHearingImpaired { get; set; }
}
