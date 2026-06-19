using System;
using System.Collections.Generic;

namespace MediaBrowser.Model.Dto;

/// <summary>
/// A single playback history session.
/// </summary>
public class PlaybackHistoryDto
{
    /// <summary>
    /// Gets or sets the session id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the user id.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the user name. Only populated by the administrative sessions endpoint.
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// Gets or sets the live item id, or <c>null</c> if the item has been removed.
    /// </summary>
    public Guid? ItemId { get; set; }

    /// <summary>
    /// Gets or sets the snapshot of the item title.
    /// </summary>
    public string? Title { get; set; }

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
    /// Gets or sets a value indicating whether delivery differed from the source.
    /// </summary>
    public bool Transcoded { get; set; }

    /// <summary>
    /// Gets or sets the network throughput of the session in bits per second.
    /// </summary>
    public int? Bitrate { get; set; }

    /// <summary>
    /// Gets or sets the device id.
    /// </summary>
    public string? DeviceId { get; set; }

    /// <summary>
    /// Gets or sets the client/app name.
    /// </summary>
    public string? ClientName { get; set; }

    /// <summary>
    /// Gets or sets the per-stream details.
    /// </summary>
    public IReadOnlyList<PlaybackHistoryStreamDto> Streams { get; set; } = Array.Empty<PlaybackHistoryStreamDto>();
}
