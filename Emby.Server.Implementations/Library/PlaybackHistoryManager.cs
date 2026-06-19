using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.EntityFrameworkCore;

namespace Emby.Server.Implementations.Library;

/// <summary>
/// Manages the append-only playback history store and its logical-item identities.
/// </summary>
public class PlaybackHistoryManager : IPlaybackHistoryManager
{
    private readonly IDbContextFactory<JellyfinDbContext> _dbProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackHistoryManager"/> class.
    /// </summary>
    /// <param name="dbProvider">The database context factory.</param>
    public PlaybackHistoryManager(IDbContextFactory<JellyfinDbContext> dbProvider)
    {
        _dbProvider = dbProvider;
    }

    /// <inheritdoc/>
    public async Task RecordPlaybackAsync(User user, BaseItem item, PlaybackHistoryInfo info, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(info);

        var playbackItemId = await ResolvePlaybackItemAsync(item, cancellationToken).ConfigureAwait(false);

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

    /// <summary>
    /// Finds the identity for an item by its user-data key set, merging duplicates and re-linking the
    /// live item. Optionally creates a new identity when none exists.
    /// </summary>
    private async Task<Guid?> ResolveAsync(BaseItem item, bool createIfMissing, CancellationToken cancellationToken)
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
    public async Task<IReadOnlyList<UserPlaybackHistory>> GetHistoryAsync(
        Guid? userId,
        Guid? itemId,
        DateTime? startDate,
        DateTime? endDate,
        string? mediaType,
        int? limit,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            IQueryable<UserPlaybackHistory> query = dbContext.UserPlaybackHistory.AsNoTracking();

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
    public async Task<PlaybackStatsSummaryDto> GetStatsSummaryAsync(DateTime? startDate, DateTime? endDate, Guid? userId, string? mediaType, CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var query = Filter(dbContext, startDate, endDate, userId, mediaType);
            var withBitrate = query.Where(h => h.Bitrate != null);

            var averageBitrate = await withBitrate.AverageAsync(h => (double?)h.Bitrate, cancellationToken).ConfigureAwait(false);
            var transferredBits = await withBitrate
                .SumAsync(h => (double)h.Bitrate!.Value * h.PlayedDurationTicks, cancellationToken)
                .ConfigureAwait(false);

            var totalWatchTime = await query.SumAsync(h => (long?)h.PlayedDurationTicks, cancellationToken).ConfigureAwait(false) ?? 0;
            var activeDays = await query.Select(h => h.DateStopped.Date).Distinct().CountAsync(cancellationToken).ConfigureAwait(false);

            return new PlaybackStatsSummaryDto
            {
                Plays = await query.CountAsync(cancellationToken).ConfigureAwait(false),
                Completions = await query.CountAsync(h => h.PlayedToCompletion, cancellationToken).ConfigureAwait(false),
                TranscodedPlays = await query.CountAsync(h => h.Transcoded, cancellationToken).ConfigureAwait(false),
                TotalWatchTimeTicks = totalWatchTime,
                ActiveDays = activeDays,
                AverageDailyWatchTimeTicks = activeDays > 0 ? totalWatchTime / activeDays : 0,
                UniqueItems = await query.Select(h => h.PlaybackItemId).Distinct().CountAsync(cancellationToken).ConfigureAwait(false),
                ActiveUsers = await query.Select(h => h.UserId).Distinct().CountAsync(cancellationToken).ConfigureAwait(false),
                AverageBitrate = (long)(averageBitrate ?? 0),
                TotalDataTransferredBytes = (long)(transferredBits / TimeSpan.TicksPerSecond / 8)
            };
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PlaybackStatsTimelineEntryDto>> GetStatsTimelineAsync(DateTime? startDate, DateTime? endDate, Guid? userId, string? mediaType, PlaybackStatsInterval interval, CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            // Bucket client-side: SQLite date-truncation in GROUP BY is brittle across providers.
            var rows = await Filter(dbContext, startDate, endDate, userId, mediaType)
                .Select(h => new { h.DateStopped, h.PlayedToCompletion, Span = h.PlayedDurationTicks })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return rows
                .GroupBy(r => BucketStart(r.DateStopped, interval))
                .OrderBy(g => g.Key)
                .Select(g => new PlaybackStatsTimelineEntryDto
                {
                    Date = g.Key,
                    Plays = g.Count(),
                    Completions = g.Count(r => r.PlayedToCompletion),
                    WatchTimeTicks = g.Sum(r => r.Span)
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
            var grouped = Filter(dbContext, startDate, endDate, userId, mediaType)
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
            var grouped = Filter(dbContext, startDate, endDate, null, mediaType)
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
            var historyIds = Filter(dbContext, startDate, endDate, userId, mediaType).Select(h => h.Id);
            var sourceStreams = dbContext.UserPlaybackHistoryStreams
                .AsNoTracking()
                .Where(s => historyIds.Contains(s.HistoryId) && s.Origin == PlaybackHistoryStreamOrigin.Source);
            var deliveredStreams = dbContext.UserPlaybackHistoryStreams
                .AsNoTracking()
                .Where(s => historyIds.Contains(s.HistoryId) && s.Origin == PlaybackHistoryStreamOrigin.Delivered);

            var transcoded = await Filter(dbContext, startDate, endDate, userId, mediaType).CountAsync(h => h.Transcoded, cancellationToken).ConfigureAwait(false);
            var total = await Filter(dbContext, startDate, endDate, userId, mediaType).CountAsync(cancellationToken).ConfigureAwait(false);

            return new PlaybackStatsStreamBreakdownDto
            {
                Resolutions = await BucketResolutionsAsync(
                    sourceStreams.Where(s => s.StreamType == PlaybackHistoryStreamType.Video && (s.Width != null || s.Height != null)),
                    cancellationToken).ConfigureAwait(false),
                DeliveredResolutions = await BucketResolutionsAsync(
                    deliveredStreams.Where(s => s.StreamType == PlaybackHistoryStreamType.Video && (s.Width != null || s.Height != null)),
                    cancellationToken).ConfigureAwait(false),
                VideoRanges = await CountByAsync(
                    sourceStreams.Where(s => s.StreamType == PlaybackHistoryStreamType.Video),
                    s => s.VideoRange,
                    v => v,
                    cancellationToken).ConfigureAwait(false),
                DeliveredVideoRanges = await CountByAsync(
                    deliveredStreams.Where(s => s.StreamType == PlaybackHistoryStreamType.Video),
                    s => s.VideoRange,
                    v => v,
                    cancellationToken).ConfigureAwait(false),
                VideoCodecs = await CountByAsync(
                    sourceStreams.Where(s => s.StreamType == PlaybackHistoryStreamType.Video),
                    s => s.Codec,
                    v => v,
                    cancellationToken).ConfigureAwait(false),
                DeliveredVideoCodecs = await CountByAsync(
                    deliveredStreams.Where(s => s.StreamType == PlaybackHistoryStreamType.Video),
                    s => s.Codec,
                    v => v,
                    cancellationToken).ConfigureAwait(false),
                AudioCodecs = await CountByAsync(
                    sourceStreams.Where(s => s.StreamType == PlaybackHistoryStreamType.Audio),
                    s => s.Codec,
                    v => v,
                    cancellationToken).ConfigureAwait(false),
                DeliveredAudioCodecs = await CountByAsync(
                    deliveredStreams.Where(s => s.StreamType == PlaybackHistoryStreamType.Audio),
                    s => s.Codec,
                    v => v,
                    cancellationToken).ConfigureAwait(false),
                AudioChannels = await CountByAsync(
                    sourceStreams.Where(s => s.StreamType == PlaybackHistoryStreamType.Audio),
                    s => s.Channels,
                    ChannelLabel,
                    cancellationToken).ConfigureAwait(false),
                DeliveredAudioChannels = await CountByAsync(
                    deliveredStreams.Where(s => s.StreamType == PlaybackHistoryStreamType.Audio),
                    s => s.Channels,
                    ChannelLabel,
                    cancellationToken).ConfigureAwait(false),
                AudioLanguages = await CountByAsync(
                    sourceStreams.Where(s => s.StreamType == PlaybackHistoryStreamType.Audio),
                    s => s.Language,
                    v => v,
                    cancellationToken).ConfigureAwait(false),
                SubtitleLanguages = await CountByAsync(
                    sourceStreams.Where(s => s.StreamType == PlaybackHistoryStreamType.Subtitle),
                    s => s.Language,
                    v => v,
                    cancellationToken).ConfigureAwait(false),
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
    public async Task<IReadOnlyList<PlaybackStatsHeatmapEntryDto>> GetHeatmapAsync(DateTime? startDate, DateTime? endDate, Guid? userId, string? mediaType, CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var rows = await Filter(dbContext, startDate, endDate, userId, mediaType)
                .Select(h => new { h.DateStarted, Span = h.PlayedDurationTicks })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return rows
                .GroupBy(r => new { Day = (int)r.DateStarted.DayOfWeek, r.DateStarted.Hour })
                .Select(g => new PlaybackStatsHeatmapEntryDto
                {
                    DayOfWeek = g.Key.Day,
                    Hour = g.Key.Hour,
                    Plays = g.Count(),
                    WatchTimeTicks = g.Sum(r => r.Span)
                })
                .ToList();
        }
    }

    private static IQueryable<UserPlaybackHistory> Filter(JellyfinDbContext dbContext, DateTime? startDate, DateTime? endDate, Guid? userId, string? mediaType)
    {
        IQueryable<UserPlaybackHistory> query = dbContext.UserPlaybackHistory.AsNoTracking();
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

    private static async Task<IReadOnlyList<NameCountDto>> BucketResolutionsAsync(IQueryable<UserPlaybackHistoryStream> videoStreams, CancellationToken cancellationToken)
    {
        var raw = await videoStreams
            .GroupBy(s => new { s.Width, s.Height })
            .Select(g => new { g.Key.Width, g.Key.Height, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Reuse Jellyfin's canonical resolution labels (handles letterboxing, e.g. 1920x804 -> "1080p").
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in raw)
        {
            var label = new MediaStream { Width = r.Width, Height = r.Height }.GetResolutionText() ?? "Unknown";
            counts[label] = counts.GetValueOrDefault(label) + r.Count;
        }

        return counts
            .Select(kv => new NameCountDto { Name = kv.Key, Count = kv.Value })
            .OrderByDescending(n => n.Count)
            .ToList();
    }

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

    private static async Task<IReadOnlyList<NameCountDto>> CountByAsync<TKey>(
        IQueryable<UserPlaybackHistoryStream> source,
        System.Linq.Expressions.Expression<Func<UserPlaybackHistoryStream, TKey>> keySelector,
        Func<TKey, string?> labelSelector,
        CancellationToken cancellationToken)
    {
        var grouped = await source
            .GroupBy(keySelector)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return grouped
            .Select(g => new NameCountDto { Name = labelSelector(g.Key), Count = g.Count })
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
}
