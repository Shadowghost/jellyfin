using System;

namespace Jellyfin.Api.Helpers.DynamicStreamObserver;

/// <summary>
/// Collects byte samples for a single active stream.
/// </summary>
public sealed class StreamMetricCollector : IDisposable
{
    private readonly StreamObserverService.StreamState _streamState;
    private bool _disposed;

    internal StreamMetricCollector(StreamObserverService.StreamState streamState)
    {
        _streamState = streamState;
    }

    /// <summary>
    /// Adds bytes from the latest response write.
    /// </summary>
    /// <param name="sendData">Bytes written for the current buffer.</param>
    public void Collect(long sendData)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _streamState.AddBytes(sendData);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _streamState.EndSnapshot = true;
    }
}
