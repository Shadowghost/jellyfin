using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Api.Helpers.DynamicStreamObserver;

/// <summary>
/// Service for observing and tracking stream transfer metrics for multiple concurrent streams.
/// Per-stream metrics are debounced: <see cref="MetricsUpdated"/> fires at most once per second per stream.
/// </summary>
public sealed class StreamObserverService : IStreamObserverService, IAsyncDisposable
{
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromSeconds(1);
    private readonly PeriodicTimer _metricsTimer;
    private readonly CancellationTokenSource _timerCancellationTokenSource = new();
    private readonly Task _timerTask;
    private readonly ConcurrentDictionary<Guid, StreamState> _streams = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamObserverService"/> class.
    /// </summary>
    public StreamObserverService()
    {
        _metricsTimer = new(DebounceInterval);
        _timerTask = RunDebounceLoopAsync(_timerCancellationTokenSource.Token);
    }

    /// <inheritdoc />
    public event EventHandler<StreamMetricsEventArgs>? MetricsUpdated;

    /// <inheritdoc />
    public StreamMetricCollector BeginStream(Guid itemId, Guid userId, string? playSessionId = null)
    {
        var id = Guid.NewGuid();
        var streamState = _streams.GetOrAdd(id, key => new StreamState
        {
            ItemId = itemId,
            UserId = userId,
            PlaySessionId = playSessionId
        });

        return new StreamMetricCollector(streamState);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await _timerCancellationTokenSource.CancelAsync().ConfigureAwait(false);
        _metricsTimer.Dispose();

        try
        {
            await _timerTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // this is expected to be possible but we can ignore it safely.
        }

        _timerCancellationTokenSource.Dispose();

        _streams.Clear();
    }

    private async Task RunDebounceLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _metricsTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                DebounceMetrics();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void DebounceMetrics()
    {
        foreach (var item in _streams.ToArray())
        {
            var state = item.Value;
            var bytesTransferred = state.GetBytesTransferred();

            if (bytesTransferred == state.LastSnapshotBytesTransferred)
            {
                continue;
            }

            MetricsUpdated?.Invoke(this, new StreamMetricsEventArgs(new StreamMetrics()
            {
                BytesTransferred = bytesTransferred,
                ElapsedTime = DateTime.UtcNow - state.StartTime,
                ItemId = state.ItemId,
                UserId = state.UserId,
                PlaySessionId = state.PlaySessionId,
                TransferSpeedBytesPerSecond = bytesTransferred - state.LastSnapshotBytesTransferred,
                Timestamp = DateTime.UtcNow,
                SessionEnd = item.Value.EndSnapshot
            }));
            if (item.Value.EndSnapshot)
            {
                _streams.TryRemove(item.Key, out _);
            }
            else
            {
                state.LastSnapshotBytesTransferred = bytesTransferred;
            }
        }
    }

    internal sealed class StreamState
    {
        private readonly DateTime _startTime = DateTime.UtcNow;
        private long _bytesTransferred;

        public DateTime StartTime => _startTime;

        public long LastSnapshotBytesTransferred { get; set; }

        public Guid UserId { get; set; }

        public Guid ItemId { get; set; }

        public string? PlaySessionId { get; set; }

        public bool EndSnapshot { get; set; }

        public void AddBytes(long bytesTransferred)
        {
            Interlocked.Add(ref _bytesTransferred, bytesTransferred);
        }

        public long GetBytesTransferred()
        {
            return Interlocked.Read(ref _bytesTransferred);
        }
    }
}
