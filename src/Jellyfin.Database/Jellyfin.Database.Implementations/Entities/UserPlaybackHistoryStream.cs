#pragma warning disable CA1711 // Identifiers should not have incorrect suffix

using System;
using Jellyfin.Database.Implementations.Enums;

namespace Jellyfin.Database.Implementations.Entities;

/// <summary>
/// The characteristics of a single stream involved in a playback session.
/// Captured as descriptive attributes (not stream indices), once per origin (selected source
/// and delivered), so format/language breakdowns can be aggregated.
/// </summary>
public class UserPlaybackHistoryStream
{
    /// <summary>
    /// Gets or sets the row identity.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the owning session id.
    /// </summary>
    public required Guid HistoryId { get; set; }

    /// <summary>
    /// Gets or sets the owning session.
    /// </summary>
    public UserPlaybackHistory? History { get; set; }

    /// <summary>
    /// Gets or sets the stream type.
    /// </summary>
    public PlaybackHistoryStreamType StreamType { get; set; }

    /// <summary>
    /// Gets or sets whether this is the selected source stream or the delivered stream.
    /// </summary>
    public PlaybackHistoryStreamOrigin Origin { get; set; }

    /// <summary>
    /// Gets or sets the video width in pixels (the conventional basis for the resolution label).
    /// </summary>
    public int? Width { get; set; }

    /// <summary>
    /// Gets or sets the video height in pixels.
    /// </summary>
    public int? Height { get; set; }

    /// <summary>
    /// Gets or sets the video range (e.g. SDR, HDR10, HDR10Plus, DolbyVision, HLG).
    /// </summary>
    public string? VideoRange { get; set; }

    /// <summary>
    /// Gets or sets the codec (e.g. hevc/h264/av1, eac3/aac/truehd, srt/pgs/ass).
    /// </summary>
    public string? Codec { get; set; }

    /// <summary>
    /// Gets or sets the stream bitrate in bits per second. Populated for source streams; left
    /// null for transcoded delivered streams (per-stream output bitrate is not exposed).
    /// </summary>
    public int? Bitrate { get; set; }

    /// <summary>
    /// Gets or sets the audio channel count.
    /// </summary>
    public int? Channels { get; set; }

    /// <summary>
    /// Gets or sets the ISO language (audio/subtitle).
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the subtitle track is forced.
    /// </summary>
    public bool? IsForced { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the subtitle track is for the hearing impaired (SDH).
    /// </summary>
    public bool? IsHearingImpaired { get; set; }
}
