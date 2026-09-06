using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Jellyfin.Api.Helpers.DynamicStreamObserver;

/// <summary>
/// Represents stream metrics data.
/// </summary>
public record StreamMetrics
{
    /// <summary>
    /// Gets the number of bytes transferred.
    /// </summary>
    public long BytesTransferred { get; init; }

    /// <summary>
    /// Gets the transfer speed in bytes per second.
    /// </summary>
    public double TransferSpeedBytesPerSecond { get; init; }

    /// <summary>
    /// Gets a value indicating whether the stream has ended or not. If true this is the last event for that session.
    /// </summary>
    public bool SessionEnd { get; init; }

    /// <summary>
    /// Gets the User id associated with the stream.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Gets the Item id associated with the stream.
    /// </summary>
    public Guid ItemId { get; init; }

    /// <summary>
    /// Gets the play session id associated with the stream, if known.
    /// </summary>
    public string? PlaySessionId { get; init; }

    /// <summary>
    /// Gets the elapsed time.
    /// </summary>
    public TimeSpan ElapsedTime { get; init; }

    /// <summary>
    /// Gets the timestamp of the metrics.
    /// </summary>
    public DateTime Timestamp { get; init; }
}
