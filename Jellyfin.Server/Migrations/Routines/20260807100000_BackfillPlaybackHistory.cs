using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Server.Migrations.Routines;

/// <summary>
/// Seeds the playback-history store from the existing watched status (UserData). Old data has an
/// aggregate play count but no per-play timing, so each played (user, item) becomes
/// <c>PlayCount</c> entries marked <see cref="PlaybackHistorySource.Imported"/>: the play count is
/// preserved, but only the most recent entry carries a real date, and none of them carry the device,
/// bitrate, or stream detail a recorded session has.
/// </summary>
[JellyfinMigration("2026-08-07T10:00:00", nameof(BackfillPlaybackHistory), Stage = Stages.JellyfinMigrationStageTypes.CoreInitialisation)]
#pragma warning disable SA1649 // File name should match first type name
public class BackfillPlaybackHistory : IAsyncMigrationRoutine
#pragma warning restore SA1649 // File name should match first type name
{
    private const int SaveBatchSize = 10_000;

    private static readonly Guid _placeholderId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // Stamped on every play whose date is unknown, which is all but the most recent one for any given
    // (user, item). Writing DateTime.UtcNow instead would pile the entire library onto upgrade day.
    private static readonly DateTime _unknownDate = UserPlaybackHistory.UnknownDate;

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
            // Keyed on imported entries, not on the table being empty: a server that recorded real
            // sessions before this ran still needs its pre-existing watched status brought across.
            if (await dbContext.UserPlaybackHistory
                    .AnyAsync(h => h.Source == PlaybackHistorySource.Imported, cancellationToken)
                    .ConfigureAwait(false))
            {
                _logger.LogInformation("Playback history has already been backfilled; skipping.");
                return;
            }

            var now = DateTime.UtcNow;

            // --- Watched status from UserData ---
            // Projected, so nothing is tracked; the rows are folded into the two maps below and the
            // list is dropped before any writing starts.
            var userData = await dbContext.UserData
                .AsNoTracking()
                .Where(u => u.Played || u.PlayCount > 0 || u.LastPlayedDate != null)
                .Where(u => !u.ItemId.Equals(_placeholderId))
                .Select(u => new { u.UserId, u.ItemId, u.CustomDataKey, u.Played, u.PlayCount, u.LastPlayedDate, u.PlaybackPositionTicks })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            // Fold the rows a (user, item) has under different data keys into one record. The keys are
            // alternate names for the same logical item, so the fullest row of the set is the truthful
            // one: any key claiming it was played settles played, and the highest count settles the count.
            var watched = userData
                .GroupBy(u => (u.UserId, u.ItemId))
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var latest = g.OrderByDescending(u => u.LastPlayedDate ?? DateTime.MinValue).First();
                        return new WatchedPlay(
                            g.Any(u => u.Played),
                            g.Max(u => u.PlayCount),
                            latest.LastPlayedDate,
                            latest.PlaybackPositionTicks);
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

            // --- Create one identity per item ---
            // A key identifies a logical item and is globally unique, so two items sharing a key are
            // the same logical item and must share one identity - exactly what ResolveAsync does at
            // runtime. Claiming the key for the first item and silently dropping it from the second
            // would instead leave that second identity with no keys at all, unreachable forever.
            // Flushed in batches for the same reason as the sessions below; the two maps are plain
            // dictionaries rather than tracked entities, so clearing the tracker between batches is safe.
            var playbackItemByItem = new Dictionary<Guid, Guid>();
            var identityByKey = new Dictionary<string, Guid>(StringComparer.Ordinal);
            var pendingIdentities = 0;
            foreach (var itemId in itemIds)
            {
                var keys = keysByItem.TryGetValue(itemId, out var k) && k.Count > 0
                    ? k
                    : [itemId.ToString()];

                // Reuse the identity already claimed by any of this item's keys.
                Guid? existing = null;
                foreach (var key in keys)
                {
                    if (identityByKey.TryGetValue(key, out var claimed))
                    {
                        existing = claimed;
                        break;
                    }
                }

                Guid playbackItemId;
                if (existing is not null)
                {
                    playbackItemId = existing.Value;
                }
                else
                {
                    playbackItemId = Guid.NewGuid();
                    baseInfo.TryGetValue(itemId, out var info);

                    dbContext.PlaybackItems.Add(new PlaybackItem
                    {
                        Id = playbackItemId,
                        ItemId = itemId,
                        Title = info.Name,
                        MediaType = ShortType(info.Type),
                        DateCreated = now
                    });

                    pendingIdentities++;
                }

                playbackItemByItem[itemId] = playbackItemId;

                foreach (var key in keys)
                {
                    if (identityByKey.TryAdd(key, playbackItemId))
                    {
                        dbContext.PlaybackItemKeys.Add(new PlaybackItemKey { Id = Guid.NewGuid(), PlaybackItemId = playbackItemId, Key = key });
                        pendingIdentities++;
                    }
                }

                if (pendingIdentities >= SaveBatchSize)
                {
                    await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    dbContext.ChangeTracker.Clear();
                    pendingIdentities = 0;
                }
            }

            // Every identity must be committed before the sessions that reference it.
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            dbContext.ChangeTracker.Clear();

            // --- Create one entry per play ---
            // One row per play rather than one per (user, item), so a play count survives the move into
            // the history store instead of being flattened to 1. PlayCount is unbounded and music
            // libraries push it hard, so the total is logged before any of it is written.
            var projected = watched.Values.Sum(p => (long)Math.Max(p.PlayCount, 1));
            _logger.LogInformation("Backfilling {Sessions} playback history entries across {Items} items.", projected, itemIds.Count);

            // Flushed in batches: holding every row in the change tracker at once is what makes this
            // migration expensive. Clearing after each batch keeps the tracker flat.
            var sessions = 0;
            var pending = 0;
            foreach (var ((userId, itemId), play) in watched)
            {
                var playbackItemId = playbackItemByItem[itemId];
                baseInfo.TryGetValue(itemId, out var info);
                var runtime = info.RunTimeTicks;
                var plays = Math.Max(play.PlayCount, 1);

                for (var i = 0; i < plays; i++)
                {
                    // Everything known about this (user, item) describes its most recent play, so the
                    // evidence goes on the last entry and the earlier ones record only that a play
                    // happened. A completed item is the exception: it was watched to the end every time
                    // it was played, so every entry is a completion worth a full runtime.
                    var isLatest = i == plays - 1;
                    var stopPositionTicks = play.Played
                        ? (runtime ?? 0)
                        : (isLatest ? play.PositionTicks : 0);

                    // LastPlayedDate is stamped when playback begins, so it is the start of the session
                    // and the stop follows once the watched span has elapsed. Collapsing both onto one
                    // instant would leave a two-hour film claiming two hours of watch time in a
                    // zero-length session, which is neither orderable nor bucketable by hour.
                    var started = isLatest ? (play.LastPlayedDate ?? _unknownDate) : _unknownDate;

                    dbContext.UserPlaybackHistory.Add(BuildHistory(playbackItemId, userId, started, play.Played, stopPositionTicks, runtime));
                    sessions++;

                    if (++pending >= SaveBatchSize)
                    {
                        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                        dbContext.ChangeTracker.Clear();
                        pending = 0;
                    }
                }
            }

            if (pending > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                dbContext.ChangeTracker.Clear();
            }

            // UserData is deliberately left alone. It stays the source of truth for played state and
            // resume positions, so this migration cannot change what any user sees as watched; it only
            // adds the record of how that state was reached.
            _logger.LogInformation("Backfilled {Sessions} playback history entries across {Items} items.", sessions, itemIds.Count);
        }
    }

    private static UserPlaybackHistory BuildHistory(Guid playbackItemId, Guid userId, DateTime started, bool playedToCompletion, long positionTicks, long? runTimeTicks)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PlaybackItemId = playbackItemId,
            DateStarted = started,
            DateStopped = started.AddTicks(positionTicks),
            StartPositionTicks = 0,
            StopPositionTicks = positionTicks,
            RunTimeTicks = runTimeTicks,
            // No per-play event history exists for backfilled rows; best-effort watch time = the span.
            PlayedDurationTicks = positionTicks,
            PlayedToCompletion = playedToCompletion,
            Source = PlaybackHistorySource.Imported
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

    private sealed record WatchedPlay(bool Played, int PlayCount, DateTime? LastPlayedDate, long PositionTicks);
}
