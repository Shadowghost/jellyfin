using System;

namespace Jellyfin.Api.Helpers.DynamicStreamObserver;

/// <summary>
/// Service for observing and tracking stream transfer metrics.
/// </summary>
public interface IStreamObserverService
{
    /// <summary>
    /// Event that fires when stream metrics are updated.
    /// </summary>
    event EventHandler<StreamMetricsEventArgs>? MetricsUpdated;

    /// <summary>
    /// Starts collecting stream metrics for the given stream key.
    /// </summary>
    /// <param name="itemId">The item id of the stream.</param>
    /// <param name="userId">The user id of the stream.</param>
    /// <param name="playSessionId">The play session id of the stream, if known.</param>
    /// <returns>A collector tied to the stream key.</returns>
    StreamMetricCollector BeginStream(Guid itemId, Guid userId, string? playSessionId = null);
}
