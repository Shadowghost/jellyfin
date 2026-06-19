using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.ScheduledTasks.Tasks;

/// <summary>
/// Task to purge playback history for items that were removed from the library and have not been
/// played within the retention window. History for items still present is never touched.
/// Because there are no cascade deletes, children are removed explicitly in dependency order.
/// </summary>
public class CleanupPlaybackHistoryTask : IScheduledTask
{
    private const int LimitDays = 90;

    private readonly ILocalizationManager _localization;
    private readonly IDbContextFactory<JellyfinDbContext> _dbProvider;
    private readonly ILogger<CleanupPlaybackHistoryTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CleanupPlaybackHistoryTask"/> class.
    /// </summary>
    /// <param name="localization">The localisation provider.</param>
    /// <param name="dbProvider">The DB context factory.</param>
    /// <param name="logger">A logger.</param>
    public CleanupPlaybackHistoryTask(ILocalizationManager localization, IDbContextFactory<JellyfinDbContext> dbProvider, ILogger<CleanupPlaybackHistoryTask> logger)
    {
        _localization = localization;
        _dbProvider = dbProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Clean up playback history";

    /// <inheritdoc />
    public string Description => "Removes playback history for items that were deleted and not played within the retention window.";

    /// <inheritdoc />
    public string Category => _localization.GetLocalizedString("TasksMaintenanceCategory");

    /// <inheritdoc />
    public string Key => nameof(CleanupPlaybackHistoryTask);

    /// <inheritdoc/>
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddDays(LimitDays * -1);
        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            // Identities detached from their item (ItemId nulled on delete) with no recent playback.
            var staleOrphanIds = await dbContext.PlaybackItems
                .Where(p => !p.ItemId.HasValue)
                .Where(p => !dbContext.UserPlaybackHistory.Any(h => h.PlaybackItemId.Equals(p.Id) && h.DateStopped >= cutoff))
                .Select(p => p.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (staleOrphanIds.Count == 0)
            {
                progress.Report(100);
                return;
            }

            _logger.LogInformation("Purging playback history for {Count} removed items not played in {Limit} days.", staleOrphanIds.Count, LimitDays);

            // Explicit ordered deletes (no cascades): streams -> history -> keys -> identities.
            await dbContext.UserPlaybackHistoryStreams
                .Where(s => dbContext.UserPlaybackHistory.Any(h => h.Id.Equals(s.HistoryId) && staleOrphanIds.Contains(h.PlaybackItemId)))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await dbContext.UserPlaybackHistory
                .Where(h => staleOrphanIds.Contains(h.PlaybackItemId))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await dbContext.PlaybackItemKeys
                .Where(k => staleOrphanIds.Contains(k.PlaybackItemId))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await dbContext.PlaybackItems
                .Where(p => staleOrphanIds.Contains(p.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        progress.Report(100);
    }

    /// <inheritdoc/>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield break;
    }
}
