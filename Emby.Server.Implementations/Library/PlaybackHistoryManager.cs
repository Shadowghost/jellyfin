using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Server.Implementations.Item;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.Library;

/// <summary>
/// Manages the append-only playback history store and its logical-item identities.
/// </summary>
public class PlaybackHistoryManager : IPlaybackHistoryManager
{
    // A play session whose stop report never arrives (client vanished, server restarted mid-stream)
    // would otherwise pin its accumulator forever, so entries older than this are swept.
    private static readonly TimeSpan _pendingTransferTtl = TimeSpan.FromHours(12);

    private readonly IDbContextFactory<JellyfinDbContext> _dbProvider;

    // Network bytes measured during delivery, accumulated per play session until the session is recorded.
    private readonly ConcurrentDictionary<string, (long Bytes, DateTime LastUpdatedUtc)> _pendingTransferredBytes = new(StringComparer.Ordinal);

    private readonly ILogger<PlaybackHistoryManager> _logger;

    private long _lastSweepTicks = DateTime.UtcNow.Ticks;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackHistoryManager"/> class.
    /// </summary>
    /// <param name="dbProvider">The database context factory.</param>
    /// <param name="logger">The logger.</param>
    public PlaybackHistoryManager(IDbContextFactory<JellyfinDbContext> dbProvider, ILogger<PlaybackHistoryManager> logger)
    {
        _dbProvider = dbProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public void ReportTransferredBytes(string playSessionId, long bytes)
    {
        if (string.IsNullOrEmpty(playSessionId) || bytes <= 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        _pendingTransferredBytes.AddOrUpdate(playSessionId, (bytes, now), (_, existing) => (existing.Bytes + bytes, now));
        SweepAbandonedTransfers(now);
    }

    /// <inheritdoc/>
    public void DiscardPendingTransfer(string playSessionId)
    {
        if (!string.IsNullOrEmpty(playSessionId))
        {
            _pendingTransferredBytes.TryRemove(playSessionId, out _);
        }
    }

    private void SweepAbandonedTransfers(DateTime now)
    {
        var lastSweep = new DateTime(Interlocked.Read(ref _lastSweepTicks), DateTimeKind.Utc);
        if (now - lastSweep < _pendingTransferTtl)
        {
            return;
        }

        // Only the thread that wins the exchange sweeps.
        if (Interlocked.CompareExchange(ref _lastSweepTicks, now.Ticks, lastSweep.Ticks) != lastSweep.Ticks)
        {
            return;
        }

        foreach (var (key, value) in _pendingTransferredBytes)
        {
            if (now - value.LastUpdatedUtc >= _pendingTransferTtl)
            {
                _pendingTransferredBytes.TryRemove(key, out _);
            }
        }
    }

    /// <inheritdoc/>
    public async Task RecordPlaybackAsync(User user, BaseItem item, PlaybackHistoryInfo info, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(info);

        var playbackItemId = await ResolvePlaybackItemAsync(item, cancellationToken).ConfigureAwait(false);

        // Fold in any network bytes measured during delivery for this play session.
        long? actualBytes = null;
        if (!string.IsNullOrEmpty(info.PlaySessionId) && _pendingTransferredBytes.TryRemove(info.PlaySessionId, out var measured))
        {
            actualBytes = measured.Bytes;
        }

        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var historyId = Guid.NewGuid();
            dbContext.UserPlaybackHistory.Add(new UserPlaybackHistory
            {
                Id = historyId,
                UserId = user.Id,
                PlaybackItemId = playbackItemId,
                DateStarted = info.DateStarted,
                DateStopped = info.DateStopped,
                StartPositionTicks = info.StartPositionTicks,
                StopPositionTicks = info.StopPositionTicks,
                RunTimeTicks = info.RunTimeTicks,
                PlayedDurationTicks = info.PlayedDurationTicks,
                PlayedToCompletion = info.PlayedToCompletion,
                PlaySessionId = info.PlaySessionId,
                MediaSourceId = info.MediaSourceId,
                Transcoded = info.Transcoded,
                Bitrate = info.Bitrate,
                ActualBytesTransferred = actualBytes,
                DeviceId = info.DeviceId,
                DeviceName = info.DeviceName,
                ClientName = info.ClientName
            });

            foreach (var stream in info.Streams)
            {
                dbContext.UserPlaybackHistoryStreams.Add(new UserPlaybackHistoryStream
                {
                    Id = Guid.NewGuid(),
                    HistoryId = historyId,
                    StreamType = stream.StreamType,
                    Origin = stream.Origin,
                    Width = stream.Width,
                    Height = stream.Height,
                    VideoRange = stream.VideoRange,
                    Codec = stream.Codec,
                    Bitrate = stream.Bitrate,
                    Channels = stream.Channels,
                    Language = stream.Language,
                    IsForced = stream.IsForced,
                    IsHearingImpaired = stream.IsHearingImpaired
                });
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<Guid> ResolvePlaybackItemAsync(BaseItem item, CancellationToken cancellationToken = default)
    {
        // Always resolves to an id, creating an identity if none exists.
        var id = await ResolveAsync(item, createIfMissing: true, cancellationToken).ConfigureAwait(false);
        return id!.Value;
    }

    /// <inheritdoc/>
    public async Task ReattachItemAsync(BaseItem item, CancellationToken cancellationToken = default)
    {
        // Re-links existing identities (and merges) by key; never creates one for an unplayed item.
        await ResolveAsync(item, createIfMissing: false, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<PlaybackItemStats> GetUserItemStatsAsync(Guid userId, BaseItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var keys = item.GetUserDataKeys();

        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            // Resolved through the key set rather than the item id, so a delete/re-add cycle or a
            // library move does not read as "never played". Identities are merged on resolve, but a
            // merge can be pending, so every identity the keys reach is counted.
            var identityIds = dbContext.PlaybackItemKeys
                .Where(k => keys.Contains(k.Key))
                .Select(k => k.PlaybackItemId)
                .Distinct();

            // Imported entries whose date was never recorded carry the sentinel; folding them to null
            // keeps them out of the maximum, since SQL MAX ignores nulls. Reporting the sentinel would
            // date a play to 1970 and drop the item into the wrong end of every date-played sort.
            var unknownDate = UserPlaybackHistory.UnknownDate;
            var stats = await dbContext.UserPlaybackHistory
                .AsNoTracking()
                .Where(h => h.UserId.Equals(userId) && identityIds.Contains(h.PlaybackItemId))
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    PlayCount = g.Count(),
                    LastPlayedDate = g.Max(h => h.DateStarted == unknownDate ? (DateTime?)null : h.DateStarted),
                    HasCompletion = g.Any(h => h.PlayedToCompletion)
                })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return stats is null
                ? default
                : new PlaybackItemStats(stats.PlayCount, stats.LastPlayedDate, stats.HasCompletion);
        }
    }

    /// <inheritdoc/>
    public async Task<int> RebuildUserDataProjectionAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var unknownDate = UserPlaybackHistory.UnknownDate;
        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            // One aggregate per (user, logical item), then spread across every data key that identity
            // answers to. A key set is what links history to user data - the same link the runtime path
            // uses - so an item that was removed and re-added under a new id still lands on its row.
            var aggregates = await dbContext.UserPlaybackHistory
                .AsNoTracking()
                .GroupBy(h => new { h.UserId, h.PlaybackItemId })
                .Select(g => new
                {
                    g.Key.UserId,
                    g.Key.PlaybackItemId,
                    PlayCount = g.Count(),
                    LastPlayedDate = g.Max(h => h.DateStarted == unknownDate ? (DateTime?)null : h.DateStarted),
                    HasCompletion = g.Any(h => h.PlayedToCompletion)
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (aggregates.Count == 0)
            {
                progress?.Report(100);
                return 0;
            }

            var keysByIdentity = (await dbContext.PlaybackItemKeys
                    .AsNoTracking()
                    .Select(k => new { k.PlaybackItemId, k.Key })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .GroupBy(k => k.PlaybackItemId)
                .ToDictionary(g => g.Key, g => g.Select(k => k.Key).ToList());

            var updated = 0;
            var processed = 0;
            foreach (var aggregate in aggregates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (keysByIdentity.TryGetValue(aggregate.PlaybackItemId, out var keys))
                {
                    var userId = aggregate.UserId;
                    var playCount = aggregate.PlayCount;
                    var lastPlayedDate = aggregate.LastPlayedDate;
                    var hasCompletion = aggregate.HasCompletion;

                    var rows = dbContext.UserData
                        .Where(u => u.UserId.Equals(userId) && keys.Contains(u.CustomDataKey))

                        // Rows detached from a deleted item are held only for the retention window and
                        // were never part of the backfill, so there is nothing to reconcile them against.
                        .Where(u => !u.ItemId.Equals(BaseItemRepository.PlaceholderId));

                    updated += await rows
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(u => u.PlayCount, playCount)

                                // An observed completion retires an earlier manual choice and settles
                                // the state on its own; without one the surviving override decides, and
                                // absent that, nothing was completed so nothing is played. This is the
                                // per-item rule spelled out, so running the rebuild settles nothing
                                // differently from what playback already had.
                                //
                                // SupportsPlayedStatus cannot be consulted here - it is a runtime
                                // property of the item class, not a column. Nothing writes history for
                                // an item that has no played state to set, so the two cannot disagree.
                                .SetProperty(u => u.PlayedOverride, u => hasCompletion ? (bool?)null : u.PlayedOverride)
                                .SetProperty(u => u.Played, u => hasCompletion || (u.PlayedOverride ?? false))

                                // Only overwritten when the history actually has a date to offer. An
                                // item can carry one from before this store existed, or from a metadata
                                // import, and neither is contradicted by an empty aggregate.
                                .SetProperty(u => u.LastPlayedDate, u => lastPlayedDate ?? u.LastPlayedDate),
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                if (++processed % 500 == 0)
                {
                    progress?.Report(processed * 100d / aggregates.Count);
                }
            }

            progress?.Report(100);
            _logger.LogInformation("Rebuilt {Rows} user data rows from {Aggregates} playback aggregates.", updated, aggregates.Count);
            return updated;
        }
    }

    /// <inheritdoc/>
    public async Task DetachItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            // Keep the identity and its history; only the link to the now-deleted live item goes.
            // This is what marks the identity as an orphan for the retention task, and what lets
            // ReattachItemAsync adopt it again if the item comes back.
            await dbContext.PlaybackItems
                .Where(p => p.ItemId.HasValue && p.ItemId.Value.Equals(itemId))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ItemId, (Guid?)null), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Finds the identity for an item by its user-data key set, merging duplicates and re-linking the
    /// live item. Optionally creates a new identity when none exists.
    /// </summary>
    private async Task<Guid?> ResolveAsync(BaseItem item, bool createIfMissing, CancellationToken cancellationToken)
    {
        try
        {
            return await ResolveOnceAsync(item, createIfMissing, cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            return await ResolveOnceAsync(item, createIfMissing, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<Guid?> ResolveOnceAsync(BaseItem item, bool createIfMissing, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        var keys = item.GetUserDataKeys();
        var mediaType = item.GetBaseItemKind().ToString();

        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var matchIds = await dbContext.PlaybackItemKeys
                    .Where(k => keys.Contains(k.Key))
                    .Select(k => k.PlaybackItemId)
                    .Distinct()
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                Guid survivorId;
                if (matchIds.Count == 0)
                {
                    if (!createIfMissing)
                    {
                        // Nothing to reattach (the item has no playback history).
                        return null;
                    }

                    // No identity yet - create one.
                    survivorId = Guid.NewGuid();
                    dbContext.PlaybackItems.Add(new PlaybackItem
                    {
                        Id = survivorId,
                        ItemId = item.Id,
                        Title = item.Name,
                        MediaType = mediaType,
                        DateCreated = DateTime.UtcNow
                    });

                    foreach (var key in keys.Distinct())
                    {
                        dbContext.PlaybackItemKeys.Add(new PlaybackItemKey { Id = Guid.NewGuid(), PlaybackItemId = survivorId, Key = key });
                    }

                    await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // Existing identity (or several - merge them). Pick the oldest as survivor.
                    var ordered = await dbContext.PlaybackItems
                        .AsNoTracking()
                        .Where(p => matchIds.Contains(p.Id))
                        .OrderBy(p => p.DateCreated)
                        .ThenBy(p => p.Id)
                        .Select(p => p.Id)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);

                    survivorId = ordered[0];

                    if (ordered.Count > 1)
                    {
                        // Auto-merge: an item carrying keys owned by 2+ identities proves they are one item.
                        var loserIds = ordered.Skip(1).ToList();

                        await dbContext.UserPlaybackHistory
                            .Where(h => loserIds.Contains(h.PlaybackItemId))
                            .ExecuteUpdateAsync(s => s.SetProperty(h => h.PlaybackItemId, survivorId), cancellationToken)
                            .ConfigureAwait(false);

                        // Loser keys are disjoint from survivor keys (a key belongs to one identity), so re-point is safe.
                        await dbContext.PlaybackItemKeys
                            .Where(k => loserIds.Contains(k.PlaybackItemId))
                            .ExecuteUpdateAsync(s => s.SetProperty(k => k.PlaybackItemId, survivorId), cancellationToken)
                            .ConfigureAwait(false);

                        await dbContext.PlaybackItems
                            .Where(p => loserIds.Contains(p.Id))
                            .ExecuteDeleteAsync(cancellationToken)
                            .ConfigureAwait(false);
                    }

                    // Reattach the survivor to the (possibly new) live item.
                    await dbContext.PlaybackItems
                        .Where(p => p.Id.Equals(survivorId))
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(p => p.ItemId, (Guid?)item.Id)
                                .SetProperty(p => p.Title, item.Name)
                                .SetProperty(p => p.MediaType, mediaType),
                            cancellationToken)
                        .ConfigureAwait(false);

                    // Add any newly-seen keys.
                    var existingKeys = await dbContext.PlaybackItemKeys
                        .Where(k => k.PlaybackItemId.Equals(survivorId))
                        .Select(k => k.Key)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);

                    var newKeys = keys.Distinct().Except(existingKeys).ToList();
                    if (newKeys.Count > 0)
                    {
                        foreach (var key in newKeys)
                        {
                            dbContext.PlaybackItemKeys.Add(new PlaybackItemKey { Id = Guid.NewGuid(), PlaybackItemId = survivorId, Key = key });
                        }

                        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return survivorId;
            }
        }
    }

    /// <inheritdoc/>
    public async Task DeleteUserHistoryAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            // Children first, in dependency order - there are no cascades in this store.
            // Contains over an IQueryable stays a SQL subquery, so no per-row variables are bound.
            var historyIds = dbContext.UserPlaybackHistory
                .Where(h => h.UserId.Equals(userId))
                .Select(h => h.Id);

            await dbContext.UserPlaybackHistoryStreams
                .Where(s => historyIds.Contains(s.HistoryId))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await dbContext.UserPlaybackHistory
                .Where(h => h.UserId.Equals(userId))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            // The identities themselves are shared across users, so they stay; the retention task
            // ages out any that are now both orphaned and unplayed.
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<UserPlaybackHistory>> GetHistoryAsync(
        Guid? userId,
        Guid? itemId,
        DateTime? startDate,
        DateTime? endDate,
        string? mediaType,
        int? limit,
        bool includeImported = true,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            IQueryable<UserPlaybackHistory> query = dbContext.UserPlaybackHistory.AsNoTracking();

            if (!includeImported)
            {
                query = query.Where(h => h.Source == PlaybackHistorySource.Recorded);
            }

            if (userId.HasValue)
            {
                query = query.Where(h => h.UserId.Equals(userId.Value));
            }

            if (itemId.HasValue)
            {
                query = query.Where(h => h.PlaybackItem!.ItemId.Equals(itemId.Value));
            }

            if (!string.IsNullOrEmpty(mediaType))
            {
                query = query.Where(h => h.PlaybackItem!.MediaType == mediaType);
            }

            if (startDate.HasValue)
            {
                query = query.Where(h => h.DateStopped >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                query = query.Where(h => h.DateStopped <= endDate.Value);
            }

            query = query.OrderByDescending(h => h.DateStopped);

            if (limit.HasValue)
            {
                query = query.Take(limit.Value);
            }

            return await query
                .Include(h => h.PlaybackItem)
                .Include(h => h.Streams)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<PlaybackStatsSummaryDto> GetStatsSummaryAsync(DateTime? startDate, DateTime? endDate, Guid? userId, string? mediaType, int utcOffsetMinutes = 0, CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var query = Filter(dbContext, startDate, endDate, userId, mediaType);
            var totals = await query
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Plays = g.Count(),
                    Completions = g.Count(h => h.PlayedToCompletion),
                    TranscodedPlays = g.Count(h => h.Transcoded),
                    TotalWatchTimeTicks = g.Sum(h => (long?)h.PlayedDurationTicks) ?? 0,
                    AverageBitrate = g.Average(h => (double?)h.Bitrate),
                    MeasuredBytes = g.Sum(h => (double?)h.ActualBytesTransferred) ?? 0,

                    // Bitrate-based estimate for the rows delivery could not measure (e.g. HLS segments).
                    EstimatedBits = g.Sum(h => h.ActualBytesTransferred == null && h.Bitrate != null
                        ? (double)h.Bitrate!.Value * h.PlayedDurationTicks
                        : 0d)
                })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (totals is null)
            {
                return new PlaybackStatsSummaryDto();
            }

            var activeDays = await query
                .Select(h => h.DateStopped.AddMinutes(utcOffsetMinutes).Date)
                .Distinct()
                .CountAsync(cancellationToken)
                .ConfigureAwait(false);
            var uniqueItems = await query.Select(h => h.PlaybackItemId).Distinct().CountAsync(cancellationToken).ConfigureAwait(false);
            var activeUsers = await query.Select(h => h.UserId).Distinct().CountAsync(cancellationToken).ConfigureAwait(false);

            return new PlaybackStatsSummaryDto
            {
                Plays = totals.Plays,
                Completions = totals.Completions,
                TranscodedPlays = totals.TranscodedPlays,
                TotalWatchTimeTicks = totals.TotalWatchTimeTicks,
                ActiveDays = activeDays,
                AverageDailyWatchTimeTicks = activeDays > 0 ? totals.TotalWatchTimeTicks / activeDays : 0,
                UniqueItems = uniqueItems,
                ActiveUsers = activeUsers,
                AverageBitrate = (long)(totals.AverageBitrate ?? 0),
                TotalDataTransferredBytes = (long)(totals.MeasuredBytes + (totals.EstimatedBits / TimeSpan.TicksPerSecond / 8))
            };
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PlaybackStatsTimelineEntryDto>> GetStatsTimelineAsync(DateTime? startDate, DateTime? endDate, Guid? userId, string? mediaType, PlaybackStatsInterval interval, int utcOffsetMinutes = 0, CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var daily = await Filter(dbContext, startDate, endDate, userId, mediaType)
                .GroupBy(h => h.DateStopped.AddMinutes(utcOffsetMinutes).Date)
                .Select(g => new
                {
                    Day = g.Key,
                    Plays = g.Count(),
                    Completions = g.Count(h => h.PlayedToCompletion),
                    WatchTimeTicks = g.Sum(h => (long?)h.PlayedDurationTicks) ?? 0
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return daily
                .GroupBy(d => BucketStart(d.Day, interval))
                .OrderBy(g => g.Key)
                .Select(g => new PlaybackStatsTimelineEntryDto
                {
                    Date = g.Key,
                    Plays = g.Sum(d => d.Plays),
                    Completions = g.Sum(d => d.Completions),
                    WatchTimeTicks = g.Sum(d => d.WatchTimeTicks)
                })
                .ToList();
        }
    }

    /// <inheritdoc/>
    public async Task<QueryResult<PlaybackStatsItemDto>> GetTopItemsAsync(DateTime? startDate, DateTime? endDate, Guid? userId, string? mediaType, string? sortBy, bool descending, int startIndex, int limit, CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            // A lifetime counter: an item's play total should not drop because the plays predate the
            // history store. Imported entries carry old dates, so a windowed request still excludes
            // all but the ones that genuinely fall inside the window.
            var grouped = Filter(dbContext, startDate, endDate, userId, mediaType, includeImported: true)
                .GroupBy(h => h.PlaybackItemId)
                .Select(g => new
                {
                    PlaybackItemId = g.Key,
                    Plays = g.Count(),
                    Completions = g.Count(h => h.PlayedToCompletion),
                    WatchTimeTicks = g.Sum(h => (long?)h.PlayedDurationTicks) ?? 0,
                    LastPlayed = g.Max(h => h.DateStopped)
                });

            var total = await grouped.CountAsync(cancellationToken).ConfigureAwait(false);

            var ordered = sortBy?.ToLowerInvariant() switch
            {
                "plays" => descending ? grouped.OrderByDescending(x => x.Plays) : grouped.OrderBy(x => x.Plays),
                "completions" => descending ? grouped.OrderByDescending(x => x.Completions) : grouped.OrderBy(x => x.Completions),
                "lastplayed" => descending ? grouped.OrderByDescending(x => x.LastPlayed) : grouped.OrderBy(x => x.LastPlayed),
                _ => descending ? grouped.OrderByDescending(x => x.WatchTimeTicks) : grouped.OrderBy(x => x.WatchTimeTicks)
            };

            var page = await ordered.Skip(startIndex).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);

            var ids = page.Select(x => x.PlaybackItemId).ToList();
            var identities = await dbContext.PlaybackItems
                .AsNoTracking()
                .Where(p => ids.Contains(p.Id))
                .Select(p => new { p.Id, p.ItemId, p.Title })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var items = page
                .Select(x =>
                {
                    var identity = identities.Find(p => p.Id.Equals(x.PlaybackItemId));
                    return new PlaybackStatsItemDto
                    {
                        ItemId = identity?.ItemId,
                        Title = identity?.Title,
                        Plays = x.Plays,
                        Completions = x.Completions,
                        WatchTimeTicks = x.WatchTimeTicks,
                        LastPlayed = x.LastPlayed
                    };
                })
                .ToList();

            return new QueryResult<PlaybackStatsItemDto>(startIndex, total, items);
        }
    }

    /// <inheritdoc/>
    public async Task<QueryResult<PlaybackStatsUserDto>> GetUserBreakdownAsync(DateTime? startDate, DateTime? endDate, string? mediaType, string? sortBy, bool descending, int startIndex, int limit, CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            // Lifetime counters, for the same reason as GetTopItemsAsync.
            var grouped = Filter(dbContext, startDate, endDate, null, mediaType, includeImported: true)
                .GroupBy(h => h.UserId)
                .Select(g => new PlaybackStatsUserDto
                {
                    UserId = g.Key,
                    Plays = g.Count(),
                    Completions = g.Count(h => h.PlayedToCompletion),
                    WatchTimeTicks = g.Sum(h => (long?)h.PlayedDurationTicks) ?? 0,
                    LastActivity = g.Max(h => h.DateStopped)
                });

            var total = await grouped.CountAsync(cancellationToken).ConfigureAwait(false);

            var ordered = sortBy?.ToLowerInvariant() switch
            {
                "plays" => descending ? grouped.OrderByDescending(x => x.Plays) : grouped.OrderBy(x => x.Plays),
                "completions" => descending ? grouped.OrderByDescending(x => x.Completions) : grouped.OrderBy(x => x.Completions),
                "lastactivity" => descending ? grouped.OrderByDescending(x => x.LastActivity) : grouped.OrderBy(x => x.LastActivity),
                _ => descending ? grouped.OrderByDescending(x => x.WatchTimeTicks) : grouped.OrderBy(x => x.WatchTimeTicks)
            };

            var items = await ordered.Skip(startIndex).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);

            return new QueryResult<PlaybackStatsUserDto>(startIndex, total, items);
        }
    }

    /// <inheritdoc/>
    public async Task<PlaybackStatsStreamBreakdownDto> GetStreamBreakdownAsync(DateTime? startDate, DateTime? endDate, Guid? userId, string? mediaType, CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var query = Filter(dbContext, startDate, endDate, userId, mediaType);
            var historyIds = query.Select(h => h.Id);
            var facets = await dbContext.UserPlaybackHistoryStreams
                .AsNoTracking()
                .Where(s => historyIds.Contains(s.HistoryId))
                .GroupBy(s => new { s.StreamType, s.Origin, s.Width, s.Height, s.VideoRange, s.Codec, s.Channels, s.Language })
                .Select(g => new StreamFacet(
                    g.Key.StreamType,
                    g.Key.Origin,
                    g.Key.Width,
                    g.Key.Height,
                    g.Key.VideoRange,
                    g.Key.Codec,
                    g.Key.Channels,
                    g.Key.Language,
                    g.Count()))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var totals = await query
                .GroupBy(_ => 1)
                .Select(g => new { Total = g.Count(), Transcoded = g.Count(h => h.Transcoded) })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            var total = totals?.Total ?? 0;
            var transcoded = totals?.Transcoded ?? 0;

            List<StreamFacet> Of(PlaybackHistoryStreamType type, PlaybackHistoryStreamOrigin origin)
                => facets.FindAll(f => f.StreamType == type && f.Origin == origin);

            var sourceVideo = Of(PlaybackHistoryStreamType.Video, PlaybackHistoryStreamOrigin.Source);
            var deliveredVideo = Of(PlaybackHistoryStreamType.Video, PlaybackHistoryStreamOrigin.Delivered);
            var sourceAudio = Of(PlaybackHistoryStreamType.Audio, PlaybackHistoryStreamOrigin.Source);
            var deliveredAudio = Of(PlaybackHistoryStreamType.Audio, PlaybackHistoryStreamOrigin.Delivered);
            var sourceSubtitle = Of(PlaybackHistoryStreamType.Subtitle, PlaybackHistoryStreamOrigin.Source);

            return new PlaybackStatsStreamBreakdownDto
            {
                Resolutions = BucketResolutions(sourceVideo),
                DeliveredResolutions = BucketResolutions(deliveredVideo),
                VideoRanges = Tally(sourceVideo, f => f.VideoRange),
                DeliveredVideoRanges = Tally(deliveredVideo, f => f.VideoRange),
                VideoCodecs = Tally(sourceVideo, f => f.Codec),
                DeliveredVideoCodecs = Tally(deliveredVideo, f => f.Codec),
                AudioCodecs = Tally(sourceAudio, f => f.Codec),
                DeliveredAudioCodecs = Tally(deliveredAudio, f => f.Codec),
                AudioChannels = Tally(sourceAudio, f => ChannelLabel(f.Channels)),
                DeliveredAudioChannels = Tally(deliveredAudio, f => ChannelLabel(f.Channels)),
                AudioLanguages = Tally(sourceAudio, f => f.Language),
                SubtitleLanguages = Tally(sourceSubtitle, f => f.Language),
                DirectPlays = total - transcoded,
                TranscodedPlays = transcoded
            };
        }
    }

    /// <inheritdoc/>
    public async Task<PlaybackStatsContextBreakdownDto> GetContextBreakdownAsync(DateTime? startDate, DateTime? endDate, Guid? userId, string? mediaType, CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var query = Filter(dbContext, startDate, endDate, userId, mediaType);
            return new PlaybackStatsContextBreakdownDto
            {
                Clients = await CountHistoryByAsync(query, h => h.ClientName, cancellationToken).ConfigureAwait(false),
                Devices = await CountHistoryByAsync(query, h => h.DeviceName, cancellationToken).ConfigureAwait(false),
                MediaTypes = await CountHistoryByAsync(query, h => h.PlaybackItem!.MediaType, cancellationToken).ConfigureAwait(false)
            };
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PlaybackStatsHeatmapEntryDto>> GetHeatmapAsync(DateTime? startDate, DateTime? endDate, Guid? userId, string? mediaType, int utcOffsetMinutes = 0, CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var rows = await Filter(dbContext, startDate, endDate, userId, mediaType)
                .GroupBy(h => new
                {
                    Day = h.DateStarted.AddMinutes(utcOffsetMinutes).DayOfWeek,
                    h.DateStarted.AddMinutes(utcOffsetMinutes).Hour
                })
                .Select(g => new
                {
                    g.Key.Day,
                    g.Key.Hour,
                    Plays = g.Count(),
                    WatchTimeTicks = g.Sum(h => (long?)h.PlayedDurationTicks) ?? 0
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return rows
                .Select(r => new PlaybackStatsHeatmapEntryDto
                {
                    DayOfWeek = (int)r.Day,
                    Hour = r.Hour,
                    Plays = r.Plays,
                    WatchTimeTicks = r.WatchTimeTicks
                })
                .ToList();
        }
    }

    /// <summary>
    /// Builds the base query for a statistics aggregate.
    /// </summary>
    /// <remarks>
    /// Imported entries are reconstructed from the pre-existing watched status: they know that a play
    /// happened and (for the most recent one) roughly when, but nothing about the hour it ran, the
    /// device it ran on, or how it was delivered. Any aggregate describing *when* or *how* something
    /// was watched therefore has to leave them out, or the answer is dominated by entries that cannot
    /// support it - every imported entry would otherwise read as an unmeasured direct play on an
    /// unnamed device. Only the lifetime counters, which ask *how much* rather than *when*, include them.
    /// </remarks>
    private static IQueryable<UserPlaybackHistory> Filter(JellyfinDbContext dbContext, DateTime? startDate, DateTime? endDate, Guid? userId, string? mediaType, bool includeImported = false)
    {
        IQueryable<UserPlaybackHistory> query = dbContext.UserPlaybackHistory.AsNoTracking();

        if (!includeImported)
        {
            query = query.Where(h => h.Source == PlaybackHistorySource.Recorded);
        }

        if (userId.HasValue)
        {
            query = query.Where(h => h.UserId.Equals(userId.Value));
        }

        if (startDate.HasValue)
        {
            query = query.Where(h => h.DateStopped >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(h => h.DateStopped <= endDate.Value);
        }

        if (!string.IsNullOrEmpty(mediaType))
        {
            query = query.Where(h => h.PlaybackItem!.MediaType == mediaType);
        }

        return query;
    }

    private static IReadOnlyList<NameCountDto> BucketResolutions(List<StreamFacet> videoFacets)
    {
        // Reuse Jellyfin's canonical resolution labels (handles letterboxing, e.g. 1920x804 -> "1080p").
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var facet in videoFacets)
        {
            if (facet.Width is null && facet.Height is null)
            {
                continue;
            }

            var label = new MediaStream { Width = facet.Width, Height = facet.Height }.GetResolutionText() ?? "Unknown";
            counts[label] = counts.GetValueOrDefault(label) + facet.Count;
        }

        return counts
            .Select(kv => new NameCountDto { Name = kv.Key, Count = kv.Value })
            .OrderByDescending(n => n.Count)
            .ToList();
    }

    private static IReadOnlyList<NameCountDto> Tally(List<StreamFacet> facets, Func<StreamFacet, string?> labelSelector)
        => facets
            .GroupBy(labelSelector)
            .Select(g => new NameCountDto { Name = g.Key, Count = g.Sum(f => f.Count) })
            .OrderByDescending(n => n.Count)
            .ToList();

    // Maps an audio channel count to a friendly layout label.
    private static string ChannelLabel(int? channels)
        => channels switch
        {
            null => "Unknown",
            1 => "Mono",
            2 => "Stereo",
            6 => "5.1",
            8 => "7.1",
            _ => $"{channels} ch"
        };

    private static async Task<IReadOnlyList<NameCountDto>> CountHistoryByAsync(
        IQueryable<UserPlaybackHistory> source,
        System.Linq.Expressions.Expression<Func<UserPlaybackHistory, string?>> keySelector,
        CancellationToken cancellationToken)
    {
        var grouped = await source
            .GroupBy(keySelector)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return grouped
            .Select(g => new NameCountDto { Name = g.Key, Count = g.Count })
            .OrderByDescending(n => n.Count)
            .ToList();
    }

    private static DateTime BucketStart(DateTime date, PlaybackStatsInterval interval)
    {
        switch (interval)
        {
            case PlaybackStatsInterval.Month:
                return new DateTime(date.Year, date.Month, 1, 0, 0, 0, date.Kind);
            case PlaybackStatsInterval.Week:
                var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
                return date.Date.AddDays(-diff);
            default:
                return date.Date;
        }
    }

    /// <summary>
    /// One distinct stream-attribute combination and how many recorded streams carry it. Every facet
    /// of the stream breakdown is a pivot over these, so they are fetched once per request.
    /// </summary>
    private sealed record StreamFacet(
        PlaybackHistoryStreamType StreamType,
        PlaybackHistoryStreamOrigin Origin,
        int? Width,
        int? Height,
        string? VideoRange,
        string? Codec,
        int? Channels,
        string? Language,
        int Count);
}
