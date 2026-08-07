using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.EntryPoints;

/// <summary>
/// Keeps playback-history identities in step with the library.
/// <para>
/// Playback history outlives the items it describes, so the link between an identity and a live item
/// has to be maintained from both ends: removing an item detaches its identity (which is what makes
/// the identity eligible for retention), and adding one re-adopts any identity whose key set matches,
/// so history survives a delete/re-add or a library move.
/// </para>
/// </summary>
public sealed class PlaybackHistorySync : IHostedService
{
    private readonly ILibraryManager _libraryManager;
    private readonly IPlaybackHistoryManager _playbackHistoryManager;
    private readonly ILogger<PlaybackHistorySync> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackHistorySync"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="playbackHistoryManager">The playback history manager.</param>
    /// <param name="logger">The logger.</param>
    public PlaybackHistorySync(
        ILibraryManager libraryManager,
        IPlaybackHistoryManager playbackHistoryManager,
        ILogger<PlaybackHistorySync> logger)
    {
        _libraryManager = libraryManager;
        _playbackHistoryManager = playbackHistoryManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemAdded;
        _libraryManager.ItemRemoved += OnItemRemoved;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        _libraryManager.ItemRemoved -= OnItemRemoved;
        return Task.CompletedTask;
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        // Only items that can actually be played carry history.
        if (e.Item is null || e.Item.IsFolder)
        {
            return;
        }

        Run(item => _playbackHistoryManager.ReattachItemAsync(item), e.Item, "reattach");
    }

    private void OnItemRemoved(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is null || e.Item.IsFolder)
        {
            return;
        }

        var itemId = e.Item.Id;
        Run(_ => _playbackHistoryManager.DetachItemAsync(itemId), e.Item, "detach");
    }

    // The library events are synchronous, so the database work is handed off. A failure here must not
    // take down a library scan: the worst case is a stale link, which the next scan repairs.
    private void Run(Func<BaseItem, Task> action, BaseItem item, string operation)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await action(item).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to {Operation} playback history identity for item {ItemId}", operation, item.Id);
            }
        });
    }
}
