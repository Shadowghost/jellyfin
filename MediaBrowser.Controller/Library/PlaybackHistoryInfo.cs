using System;
using System.Collections.Generic;

namespace MediaBrowser.Controller.Library;

/// <summary>
/// The captured details of a completed playback session, used when recording playback history.
/// </summary>
public class PlaybackHistoryInfo
{
    /// <summary>
    /// Gets or sets the wall-clock time the session began.
    /// </summary>
    public DateTime DateStarted { get; set; }

    /// <summary>
    /// Gets or sets the wall-clock time the session ended.
    /// </summary>
    public DateTime DateStopped { get; set; }

    /// <summary>
    /// Gets or sets the playback position in the media at session start.
    /// </summary>
    public long StartPositionTicks { get; set; }

    /// <summary>
    /// Gets or sets the playback position in the media at session end.
    /// </summary>
    public long StopPositionTicks { get; set; }

    /// <summary>
    /// Gets or sets the snapshot of the item duration.
    /// </summary>
    public long? RunTimeTicks { get; set; }

    /// <summary>
    /// Gets or sets the actual time spent watching, in ticks (pause- and seek-excluded).
    /// </summary>
    public long PlayedDurationTicks { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the session reached completion.
    /// </summary>
    public bool PlayedToCompletion { get; set; }

    /// <summary>
    /// Gets or sets the play session id.
    /// </summary>
    public string? PlaySessionId { get; set; }

    /// <summary>
    /// Gets or sets the media source id that was played.
    /// </summary>
    public string? MediaSourceId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether delivery differed from the source.
    /// </summary>
    public bool Transcoded { get; set; }

    /// <summary>
    /// Gets or sets the network throughput of the session in bits per second (transcode output
    /// bitrate when transcoding, otherwise the source file bitrate).
    /// </summary>
    public int? Bitrate { get; set; }

    /// <summary>
    /// Gets or sets the device id.
    /// </summary>
    public string? DeviceId { get; set; }

    /// <summary>
    /// Gets or sets the friendly device name.
    /// </summary>
    public string? DeviceName { get; set; }

    /// <summary>
    /// Gets or sets the client/app name.
    /// </summary>
    public string? ClientName { get; set; }

    /// <summary>
    /// Gets or sets the per-stream details captured for this session.
    /// </summary>
    public IReadOnlyList<PlaybackHistoryStreamInfo> Streams { get; set; } = Array.Empty<PlaybackHistoryStreamInfo>();
}
