#pragma warning disable RS0030 // Do not use banned APIs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Server.Implementations.Item;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.ScheduledTasks.Tasks;

/// <summary>
/// Forgets the watch state left behind by items that were removed from the library.
/// </summary>
/// <remarks>
/// A deleted item leaves two records of having been watched, and both are kept so that re-adding the
/// item restores it: the user data rows are parked on a placeholder item with a
/// <c>RetentionDate</c>, and the playback identity is detached from the item it described. They are
/// matched by the same data keys and have to expire together, or the surviving half is left describing
/// something the other half has forgotten - a re-added item would restore a play count that the next
/// playback then overwrites with a count of one.
/// <para>
/// Deliberately has no default trigger. Watch state is the one thing a media server cannot
/// regenerate, so discarding it stays a decision an administrator makes.
/// </para>
/// </remarks>
public class CleanupUserDataTask : IScheduledTask
{
    private const int LimitDays = 90;

    private readonly ILocalizationManager _localization;
    private readonly IDbContextFactory<JellyfinDbContext> _dbProvider;
    private readonly ILogger<CleanupUserDataTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CleanupUserDataTask"/> class.
    /// </summary>
    /// <param name="localization">The localisation Provider.</param>
    /// <param name="dbProvider">The DB context factory.</param>
    /// <param name="logger">A logger.</param>
    public CleanupUserDataTask(ILocalizationManager localization, IDbContextFactory<JellyfinDbContext> dbProvider, ILogger<CleanupUserDataTask> logger)
    {
        _localization = localization;
        _dbProvider = dbProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => _localization.GetLocalizedString("CleanupUserDataTask");

    /// <inheritdoc />
    public string Description => _localization.GetLocalizedString("CleanupUserDataTaskDescription");

    /// <inheritdoc />
    public string Category => _localization.GetLocalizedString("TasksMaintenanceCategory");

    /// <inheritdoc />
    public string Key => nameof(CleanupUserDataTask);

    /// <inheritdoc/>
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddDays(LimitDays * -1);
        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            await PurgeDetachedPlaybackHistoryAsync(dbContext, cutoff, cancellationToken).ConfigureAwait(false);
            progress.Report(50);

            var detachedUserData = dbContext.UserData
                .Where(e => e.ItemId == BaseItemRepository.PlaceholderId)
                .Where(e => e.RetentionDate < cutoff);

            var removed = await detachedUserData.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Removed {Count} detached user data entries older than {Limit} days.", removed, LimitDays);
        }

        progress.Report(100);
    }

    /// <inheritdoc/>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield break;
    }

    /// <summary>
    /// Removes playback history for identities that no longer describe anything in the library.
    /// Children are deleted explicitly in dependency order rather than via the schema's cascades,
    /// which only fire when SQLite has foreign keys enabled.
    /// </summary>
    private async Task PurgeDetachedPlaybackHistoryAsync(JellyfinDbContext dbContext, DateTime cutoff, CancellationToken cancellationToken)
    {
        // Only recorded entries count as recent playback. An imported one describes a play at an
        // unknown time before the upgrade, so treating it as recent would keep history for
        // long-deleted items alive for a further retention window after every upgrade.
        var recentlyPlayed = dbContext.UserPlaybackHistory
            .Where(h => h.Source == PlaybackHistorySource.Recorded && h.DateStopped >= cutoff)
            .Select(h => h.PlaybackItemId);

        // The user data an identity's keys still reach. An attached row means the item is back in the
        // library under a new id, and a placeholder row inside its retention window is still waiting to
        // be restored - in both cases the identity is the other half of live watch state, not litter.
        var liveKeys = dbContext.UserData
            .Where(u => u.ItemId != BaseItemRepository.PlaceholderId || u.RetentionDate >= cutoff)
            .Select(u => u.CustomDataKey);

        var staleOrphanIds = await dbContext.PlaybackItems
            .Where(p => !p.ItemId.HasValue)
            .Where(p => !recentlyPlayed.Contains(p.Id))
            .Where(p => !dbContext.PlaybackItemKeys.Any(k => k.PlaybackItemId.Equals(p.Id) && liveKeys.Contains(k.Key)))
            .Select(p => p.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (staleOrphanIds.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Purging playback history for {Count} removed items not played in {Limit} days.", staleOrphanIds.Count, LimitDays);

        var staleHistoryIds = dbContext.UserPlaybackHistory
            .WhereOneOrMany(staleOrphanIds, h => h.PlaybackItemId)
            .Select(h => h.Id);

        await dbContext.UserPlaybackHistoryStreams
            .Where(s => staleHistoryIds.Contains(s.HistoryId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.UserPlaybackHistory
            .WhereOneOrMany(staleOrphanIds, h => h.PlaybackItemId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.PlaybackItemKeys
            .WhereOneOrMany(staleOrphanIds, k => k.PlaybackItemId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.PlaybackItems
            .WhereOneOrMany(staleOrphanIds, p => p.Id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
