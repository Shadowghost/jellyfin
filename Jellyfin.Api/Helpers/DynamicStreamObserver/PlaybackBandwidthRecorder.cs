using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Api.Helpers.DynamicStreamObserver;

/// <summary>
/// Subscribes to stream-transfer metrics and folds the measured network bytes into playback history,
/// keyed by play session id, so bandwidth statistics reflect the actual delivered bytes.
/// </summary>
public sealed class PlaybackBandwidthRecorder : IHostedService
{
    private readonly IStreamObserverService _streamObserverService;
    private readonly IPlaybackHistoryManager _playbackHistoryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackBandwidthRecorder"/> class.
    /// </summary>
    /// <param name="streamObserverService">The stream observer service that emits transfer metrics.</param>
    /// <param name="playbackHistoryManager">The playback history manager that records the measured bytes.</param>
    public PlaybackBandwidthRecorder(IStreamObserverService streamObserverService, IPlaybackHistoryManager playbackHistoryManager)
    {
        _streamObserverService = streamObserverService;
        _playbackHistoryManager = playbackHistoryManager;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _streamObserverService.MetricsUpdated += OnMetricsUpdated;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _streamObserverService.MetricsUpdated -= OnMetricsUpdated;
        return Task.CompletedTask;
    }

    private void OnMetricsUpdated(object? sender, StreamMetricsEventArgs e)
    {
        var metrics = e.Metrics;
        if (string.IsNullOrEmpty(metrics.PlaySessionId))
        {
            return;
        }

        // TransferSpeedBytesPerSecond carries the byte delta since the previous report; summing the
        // deltas across the session yields the total bytes streamed.
        var delta = (long)metrics.TransferSpeedBytesPerSecond;
        if (delta > 0)
        {
            _playbackHistoryManager.ReportTransferredBytes(metrics.PlaySessionId, delta);
        }
    }
}
