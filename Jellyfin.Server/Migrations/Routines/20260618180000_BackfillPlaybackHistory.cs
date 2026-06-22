using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Server.Migrations.Routines;

/// <summary>
/// Seeds the playback-history store from the existing watched status (UserData). Old data only has an
/// aggregate play count with no per-play timing, so each played (user, item) is collapsed to a single
/// session (count capped to 1): completed items become one complete play with watch time = runtime;
/// partially-watched items become one partial play with watch time = the saved resume position.
///
/// This works purely at the database level. It deliberately does NOT use the domain
/// <c>BaseItem</c>/<c>GetUserDataKeys()</c> path, because the static services those rely on are not
/// wired up yet at migration time. Instead it reuses the keys already persisted on
/// <c>UserData.CustomDataKey</c> (which are exactly what <c>GetUserDataKeys()</c> produced).
/// </summary>
[JellyfinMigration("2026-06-18T18:00:00", nameof(BackfillPlaybackHistory), Stage = Stages.JellyfinMigrationStageTypes.CoreInitialisation)]
#pragma warning disable SA1649 // File name should match first type name
public class BackfillPlaybackHistory : IAsyncMigrationRoutine
#pragma warning restore SA1649 // File name should match first type name
{
    private static readonly Guid _placeholderId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private readonly IDbContextFactory<JellyfinDbContext> _contextFactory;
    private readonly ILogger<BackfillPlaybackHistory> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BackfillPlaybackHistory"/> class.
    /// </summary>
    /// <param name="contextFactory">The database context factory.</param>
    /// <param name="logger">The logger.</param>
    public BackfillPlaybackHistory(IDbContextFactory<JellyfinDbContext> contextFactory, ILogger<BackfillPlaybackHistory> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task PerformAsync(CancellationToken cancellationToken)
    {
        var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            if (await dbContext.UserPlaybackHistory.AnyAsync(cancellationToken).ConfigureAwait(false))
            {
                _logger.LogInformation("Playback history is not empty; skipping backfill.");
                return;
            }

            var now = DateTime.UtcNow;

            // --- Watched status from UserData ---
            var userData = await dbContext.UserData
                .Where(u => u.Played || u.PlayCount > 0 || u.LastPlayedDate != null)
                .Where(u => !u.ItemId.Equals(_placeholderId))
                .Select(u => new { u.UserId, u.ItemId, u.CustomDataKey, u.Played, u.LastPlayedDate, u.PlaybackPositionTicks })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            // Collapse to a single representative play per (user, item) - old data has no per-play timing.
            var watched = userData
                .GroupBy(u => (u.UserId, u.ItemId))
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var latest = g.OrderByDescending(u => u.LastPlayedDate ?? DateTime.MinValue).First();
                        return new WatchedPlay(g.Any(u => u.Played), latest.LastPlayedDate, latest.PlaybackPositionTicks);
                    });

            // The provider-derived key set already persisted per item.
            var keysByItem = userData
                .GroupBy(u => u.ItemId)
                .ToDictionary(g => g.Key, g => g.Select(u => u.CustomDataKey).Distinct().ToList());

            var itemIds = new HashSet<Guid>(watched.Keys.Select(k => k.ItemId));
            if (itemIds.Count == 0)
            {
                _logger.LogInformation("No prior playback data found; nothing to backfill.");
                return;
            }

            // Title/runtime snapshots for the involved items (missing => deleted item).
            // WhereOneOrMany binds the id set as a single json_each parameter; a raw Contains on this
            // (potentially library-sized) list would emit one SQL variable per id and overflow SQLite's limit.
            var itemIdList = itemIds.ToList();
            var baseInfo = (await dbContext.BaseItems
                    .WhereOneOrMany(itemIdList, b => b.Id)
                    .Select(b => new { b.Id, b.Name, b.RunTimeTicks, b.Type })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .ToDictionary(b => b.Id, b => (b.Name, b.RunTimeTicks, b.Type));

            // --- Create one identity per item (keys deduplicated globally to respect the unique index) ---
            var playbackItemByItem = new Dictionary<Guid, Guid>();
            var usedKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var itemId in itemIds)
            {
                var playbackItemId = Guid.NewGuid();
                playbackItemByItem[itemId] = playbackItemId;
                baseInfo.TryGetValue(itemId, out var info);

                dbContext.PlaybackItems.Add(new PlaybackItem
                {
                    Id = playbackItemId,
                    ItemId = itemId,
                    Title = info.Name,
                    MediaType = ShortType(info.Type),
                    DateCreated = now
                });

                var keys = keysByItem.TryGetValue(itemId, out var k) && k.Count > 0
                    ? k
                    : [itemId.ToString()];

                foreach (var key in keys)
                {
                    if (usedKeys.Add(key))
                    {
                        dbContext.PlaybackItemKeys.Add(new PlaybackItemKey { Id = Guid.NewGuid(), PlaybackItemId = playbackItemId, Key = key });
                    }
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // --- Create one session per played (user, item) ---
            var sessions = 0;
            foreach (var ((userId, itemId), play) in watched)
            {
                var playbackItemId = playbackItemByItem[itemId];
                baseInfo.TryGetValue(itemId, out var info);
                var runtime = info.RunTimeTicks;
                var date = play.LastPlayedDate ?? now;

                // Completed -> a complete play (watch time = runtime); otherwise a partial play
                // (watch time = the saved resume position).
                var stopPositionTicks = play.Played ? (runtime ?? 0) : play.PositionTicks;

                dbContext.UserPlaybackHistory.Add(BuildHistory(playbackItemId, userId, date, play.Played, stopPositionTicks, runtime));
                sessions++;
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Bring the legacy UserData.PlayCount in sync with the completion count we just recorded
            // (PlayCount now means "completed plays"). With history collapsed to one play per item, this
            // is 1 for completed items and 0 otherwise.
            var completed = await dbContext.UserData
                .Where(u => u.Played && !u.ItemId.Equals(_placeholderId))
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.PlayCount, 1), cancellationToken)
                .ConfigureAwait(false);

            await dbContext.UserData
                .Where(u => !u.Played && u.PlayCount > 0 && !u.ItemId.Equals(_placeholderId))
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.PlayCount, 0), cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("Backfilled {Sessions} playback history sessions across {Items} items; reset {Completed} UserData play counts.", sessions, itemIds.Count, completed);
        }
    }

    private static UserPlaybackHistory BuildHistory(Guid playbackItemId, Guid userId, DateTime date, bool playedToCompletion, long positionTicks, long? runTimeTicks)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PlaybackItemId = playbackItemId,
            DateStarted = date,
            DateStopped = date,
            StartPositionTicks = 0,
            StopPositionTicks = positionTicks,
            RunTimeTicks = runTimeTicks,
            // No per-play event history exists for backfilled rows; best-effort watch time = the span.
            PlayedDurationTicks = positionTicks,
            PlayedToCompletion = playedToCompletion
        };

    // BaseItemEntity.Type is a fully-qualified type name; the last segment matches the item kind
    // (e.g. "...Entities.Movies.Movie" -> "Movie"), good enough for backfilled type breakdowns.
    private static string? ShortType(string? type)
    {
        if (string.IsNullOrEmpty(type))
        {
            return null;
        }

        var lastDot = type.LastIndexOf('.');
        return lastDot >= 0 && lastDot < type.Length - 1 ? type[(lastDot + 1)..] : type;
    }

    private sealed record WatchedPlay(bool Played, DateTime? LastPlayedDate, long PositionTicks);
}
