#pragma warning disable CA2227 // Collection properties should be read only

using System;
using System.Collections.Generic;

namespace Jellyfin.Database.Implementations.Entities;

/// <summary>
/// An append-only record of a single completed playback session.
/// </summary>
public class UserPlaybackHistory
{
    /// <summary>
    /// Gets or sets the row identity.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the user this session belongs to. A plain column, not a foreign key
    /// (the history store is decoupled from <see cref="User"/>).
    /// </summary>
    public required Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the logical item (the aggregation key). The live item is reached via
    /// <see cref="PlaybackItem.ItemId"/>.
    /// </summary>
    public required Guid PlaybackItemId { get; set; }

    /// <summary>
    /// Gets or sets the logical item.
    /// </summary>
    public PlaybackItem? PlaybackItem { get; set; }

    /// <summary>
    /// Gets or sets the wall-clock time the session began.
    /// </summary>
    public DateTime DateStarted { get; set; }

    /// <summary>
    /// Gets or sets the wall-clock time the session ended.
    /// </summary>
    public DateTime DateStopped { get; set; }

    /// <summary>
    /// Gets or sets the playback position in the media at session start (resume point).
    /// </summary>
    public long StartPositionTicks { get; set; }

    /// <summary>
    /// Gets or sets the playback position in the media at session end.
    /// </summary>
    public long StopPositionTicks { get; set; }

    /// <summary>
    /// Gets or sets a snapshot of the item duration, so played-percentage stays computable
    /// even if the item is later replaced or re-encoded.
    /// </summary>
    public long? RunTimeTicks { get; set; }

    /// <summary>
    /// Gets or sets the actual time spent watching, in ticks (pause- and seek-excluded), accumulated
    /// from progress reports. This is the accurate "watch time" metric; the start/stop positions only
    /// describe the content span reached.
    /// </summary>
    public long PlayedDurationTicks { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the session reached completion.
    /// </summary>
    public bool PlayedToCompletion { get; set; }

    /// <summary>
    /// Gets or sets the play session id, used as a correlation / idempotency key.
    /// </summary>
    public string? PlaySessionId { get; set; }

    /// <summary>
    /// Gets or sets the media source id that was played.
    /// </summary>
    public string? MediaSourceId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether delivery differed from the source (transcode/remux).
    /// </summary>
    public bool Transcoded { get; set; }

    /// <summary>
    /// Gets or sets the network throughput of the session in bits per second: the transcode output
    /// bitrate when transcoding, otherwise the source file bitrate (direct play). Used for
    /// bandwidth / network-utilization reporting.
    /// </summary>
    public int? Bitrate { get; set; }

    /// <summary>
    /// Gets or sets the actual number of bytes streamed to the client over the network, as measured
    /// during delivery. Null when not measured (e.g. HLS segment paths); callers fall back to the
    /// bitrate-based estimate.
    /// </summary>
    public long? ActualBytesTransferred { get; set; }

    /// <summary>
    /// Gets or sets the device id (privacy-gated snapshot).
    /// </summary>
    public string? DeviceId { get; set; }

    /// <summary>
    /// Gets or sets the friendly device name (privacy-gated snapshot).
    /// </summary>
    public string? DeviceName { get; set; }

    /// <summary>
    /// Gets or sets the client/app name (privacy-gated snapshot).
    /// </summary>
    public string? ClientName { get; set; }

    /// <summary>
    /// Gets or sets the per-stream details captured for this session.
    /// </summary>
    public ICollection<UserPlaybackHistoryStream>? Streams { get; set; }
}
