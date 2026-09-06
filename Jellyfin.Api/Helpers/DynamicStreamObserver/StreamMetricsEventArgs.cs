using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Jellyfin.Api.Helpers.DynamicStreamObserver;

/// <summary>
/// Event args for stream metrics.
/// </summary>
public class StreamMetricsEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StreamMetricsEventArgs"/> class.
    /// </summary>
    /// <param name="metrics">The stream metrics.</param>
    public StreamMetricsEventArgs(StreamMetrics metrics)
    {
        Metrics = metrics;
    }

    /// <summary>
    /// Gets the stream metrics.
    /// </summary>
    public StreamMetrics Metrics { get; }
}
