using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Server.Implementations.Library;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Database.Implementations.Locking;
using Jellyfin.Database.Providers.Sqlite;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Library;

public sealed class PlaybackHistoryManagerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<JellyfinDbContext> _dbOptions;
    private readonly PlaybackHistoryManager _manager;

    public PlaybackHistoryManagerTests()
    {
        // SQLite in-memory: a real relational DB (the manager uses transactions + ExecuteUpdate/Delete,
        // which the EF InMemory provider does not support). The connection stays open for the test's
        // lifetime so the in-memory database persists across context instances.
        // GetUserDataKeys() -> Video.SourceType -> IsActiveRecording() touches this static.
        Video.RecordingsManager ??= Mock.Of<IRecordingsManager>();

        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _dbOptions = new DbContextOptionsBuilder<JellyfinDbContext>()
            .UseSqlite(_connection)
            .Options;

        using (var ctx = CreateDbContext())
        {
            ctx.Database.EnsureCreated();
        }

        var factory = new Mock<IDbContextFactory<JellyfinDbContext>>();
        factory.Setup(f => f.CreateDbContext()).Returns(CreateDbContext);
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CreateDbContext);

        _manager = new PlaybackHistoryManager(factory.Object, NullLogger<PlaybackHistoryManager>.Instance);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public async Task ResolvePlaybackItemAsync_NewItem_CreatesIdentityWithKeys()
    {
        var movie = CreateMovie("Up", (MetadataProvider.Imdb, "tt1049413"));

        var id = await _manager.ResolvePlaybackItemAsync(movie, Token);

        Assert.NotEqual(Guid.Empty, id);

        using var ctx = CreateDbContext();
        var item = await ctx.PlaybackItems.SingleAsync(Token);
        Assert.Equal(id, item.Id);
        Assert.Equal(movie.Id, item.ItemId);
        Assert.Equal("Up", item.Title);

        // The provider-derived keys are stored once per identity.
        var keys = await ctx.PlaybackItemKeys.Select(k => k.Key).ToListAsync(Token);
        Assert.Contains("tt1049413", keys);
    }

    [Fact]
    public async Task ResolvePlaybackItemAsync_SameProviderId_ReturnsSameIdentity()
    {
        // Two distinct BaseItem GUIDs sharing a provider id (e.g. removed and re-added) map to one identity.
        var first = CreateMovie("Movie", (MetadataProvider.Imdb, "tt0000001"));
        var second = CreateMovie("Movie", (MetadataProvider.Imdb, "tt0000001"));

        var firstId = await _manager.ResolvePlaybackItemAsync(first, Token);
        var secondId = await _manager.ResolvePlaybackItemAsync(second, Token);

        Assert.Equal(firstId, secondId);

        using var ctx = CreateDbContext();
        Assert.Equal(1, await ctx.PlaybackItems.CountAsync(Token));

        // Reattached to the most recently seen live item.
        var item = await ctx.PlaybackItems.SingleAsync(Token);
        Assert.Equal(second.Id, item.ItemId);
    }

    [Fact]
    public async Task RecordPlaybackAsync_PersistsSessionWithStreams()
    {
        var user = CreateUser();
        var movie = CreateMovie("Movie", (MetadataProvider.Imdb, "tt0000002"));
        var info = MinimalInfo();
        info.Streams = new List<PlaybackHistoryStreamInfo>
        {
            new() { StreamType = PlaybackHistoryStreamType.Video, Origin = PlaybackHistoryStreamOrigin.Source, Height = 1080, VideoRange = "SDR", Codec = "h264" },
            new() { StreamType = PlaybackHistoryStreamType.Audio, Origin = PlaybackHistoryStreamOrigin.Source, Codec = "aac", Channels = 2, Language = "eng" }
        };

        await _manager.RecordPlaybackAsync(user, movie, info, Token);

        var history = await _manager.GetHistoryAsync(user.Id, null, null, null, null, null, true, Token);
        var session = Assert.Single(history);
        Assert.Equal(user.Id, session.UserId);
        Assert.True(session.PlayedToCompletion);
        Assert.NotNull(session.Streams);
        Assert.Equal(2, session.Streams!.Count);
    }

    [Fact]
    public async Task GetHistoryAsync_ScopedToItem_ReturnsOnlyMatching()
    {
        var user = CreateUser();
        var movie = CreateMovie("Movie", (MetadataProvider.Imdb, "tt0000003"));
        await _manager.RecordPlaybackAsync(user, movie, MinimalInfo(), Token);

        Assert.Single(await _manager.GetHistoryAsync(user.Id, movie.Id, null, null, null, null, true, Token));
        Assert.Empty(await _manager.GetHistoryAsync(user.Id, Guid.NewGuid(), null, null, null, null, true, Token));
    }

    [Fact]
    public async Task ReattachItemAsync_RelinksDetachedIdentity()
    {
        var user = CreateUser();
        var movie = CreateMovie("Movie", (MetadataProvider.Imdb, "tt0000004"));
        await _manager.RecordPlaybackAsync(user, movie, MinimalInfo(), Token);

        // Simulate item deletion: ItemId is nulled (detached).
        using (var ctx = CreateDbContext())
        {
            var item = await ctx.PlaybackItems.SingleAsync(Token);
            item.ItemId = null;
            await ctx.SaveChangesAsync(Token);
        }

        // Item re-added (new GUID, same provider id) -> reattach restores the live link.
        var readded = CreateMovie("Movie", (MetadataProvider.Imdb, "tt0000004"));
        await _manager.ReattachItemAsync(readded, Token);

        using var verify = CreateDbContext();
        Assert.Equal(1, await verify.PlaybackItems.CountAsync(Token));
        var reattached = await verify.PlaybackItems.SingleAsync(Token);
        Assert.Equal(readded.Id, reattached.ItemId);
    }

    [Fact]
    public async Task ReattachItemAsync_ItemWithoutHistory_DoesNothing()
    {
        var movie = CreateMovie("Never Played", (MetadataProvider.Imdb, "tt0000005"));

        await _manager.ReattachItemAsync(movie, Token);

        using var ctx = CreateDbContext();
        Assert.Equal(0, await ctx.PlaybackItems.CountAsync(Token));
    }

    [Fact]
    public async Task ResolvePlaybackItemAsync_OverlappingKeys_MergesIdentities()
    {
        var user = CreateUser();

        // Two identities created independently: one known only by IMDb, one only by TMDb.
        var imdbOnly = CreateMovie("Movie", (MetadataProvider.Imdb, "tt0000006"));
        var tmdbOnly = CreateMovie("Movie", (MetadataProvider.Tmdb, "654321"));
        await _manager.RecordPlaybackAsync(user, imdbOnly, MinimalInfo(), Token);
        await _manager.RecordPlaybackAsync(user, tmdbOnly, MinimalInfo(), Token);

        using (var ctx = CreateDbContext())
        {
            Assert.Equal(2, await ctx.PlaybackItems.CountAsync(Token));
        }

        // An item carrying BOTH keys proves the two identities are the same logical item -> merge.
        var bridging = CreateMovie("Movie", (MetadataProvider.Imdb, "tt0000006"), (MetadataProvider.Tmdb, "654321"));
        var survivorId = await _manager.ResolvePlaybackItemAsync(bridging, Token);

        using var verify = CreateDbContext();
        Assert.Equal(1, await verify.PlaybackItems.CountAsync(Token));
        var survivor = await verify.PlaybackItems.SingleAsync(Token);
        Assert.Equal(survivorId, survivor.Id);
        Assert.Equal(bridging.Id, survivor.ItemId);

        // Both sessions were re-pointed to the surviving identity.
        Assert.Equal(2, await verify.UserPlaybackHistory.CountAsync(Token));
        var orphanedHistory = await verify.UserPlaybackHistory.CountAsync(h => !h.PlaybackItemId.Equals(survivor.Id), Token);
        Assert.Equal(0, orphanedHistory);
    }

    [Fact]
    public async Task GetStatsSummaryAsync_AggregatesBitrateAndDataTransferred()
    {
        var user = CreateUser();
        var movieA = CreateMovie("A", (MetadataProvider.Imdb, "tt0000007"));
        var movieB = CreateMovie("B", (MetadataProvider.Imdb, "tt0000008"));

        var infoA = MinimalInfo();
        infoA.Bitrate = 8_000_000; // 8 Mbps for 60s
        infoA.PlayedDurationTicks = TimeSpan.FromSeconds(60).Ticks;

        var infoB = MinimalInfo();
        infoB.Bitrate = 4_000_000; // 4 Mbps for 60s
        infoB.PlayedDurationTicks = TimeSpan.FromSeconds(60).Ticks;
        // A different calendar day, so there are two active days for the daily average.
        infoB.DateStarted = infoB.DateStarted.AddDays(-2);
        infoB.DateStopped = infoB.DateStopped.AddDays(-2);

        await _manager.RecordPlaybackAsync(user, movieA, infoA, Token);
        await _manager.RecordPlaybackAsync(user, movieB, infoB, Token);

        var summary = await _manager.GetStatsSummaryAsync(null, null, null, null, 0, Token);

        Assert.Equal(2, summary.Plays);
        Assert.Equal(6_000_000, summary.AverageBitrate);

        // (8 Mbps + 4 Mbps) over 60s each = 720 Mbit = 90,000,000 bytes.
        Assert.Equal(90_000_000, summary.TotalDataTransferredBytes);

        // 120s total watch time spread over 2 distinct active days = 60s/day.
        Assert.Equal(TimeSpan.FromSeconds(60).Ticks, summary.AverageDailyWatchTimeTicks);
    }

    [Fact]
    public async Task GetHeatmapAsync_BucketsInTheViewersLocalTime()
    {
        var user = CreateUser();
        var movie = CreateMovie("Late Night", (MetadataProvider.Imdb, "tt0000010"));

        // 23:30 UTC on a Monday.
        var start = new DateTime(2026, 6, 15, 23, 30, 0, DateTimeKind.Utc);
        var info = MinimalInfo();
        info.DateStarted = start;
        info.DateStopped = start.AddMinutes(10);

        await _manager.RecordPlaybackAsync(user, movie, info, Token);

        // Read as UTC: Monday (1), hour 23.
        var utc = Assert.Single(await _manager.GetHeatmapAsync(null, null, null, null, 0, Token));
        Assert.Equal(1, utc.DayOfWeek);
        Assert.Equal(23, utc.Hour);

        // UTC+2 pushes it past midnight into Tuesday (2), hour 1.
        var local = Assert.Single(await _manager.GetHeatmapAsync(null, null, null, null, 120, Token));
        Assert.Equal(2, local.DayOfWeek);
        Assert.Equal(1, local.Hour);
    }

    [Fact]
    public async Task GetStatsTimelineAsync_BucketsInTheViewersLocalDay()
    {
        var user = CreateUser();
        var movie = CreateMovie("Midnight", (MetadataProvider.Imdb, "tt0000011"));

        var stopped = new DateTime(2026, 6, 15, 23, 30, 0, DateTimeKind.Utc);
        var info = MinimalInfo();
        info.DateStarted = stopped.AddMinutes(-10);
        info.DateStopped = stopped;

        await _manager.RecordPlaybackAsync(user, movie, info, Token);

        var utc = Assert.Single(await _manager.GetStatsTimelineAsync(null, null, null, null, PlaybackStatsInterval.Day, 0, Token));
        Assert.Equal(new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Unspecified), utc.Date);
        Assert.Equal(1, utc.Plays);

        var local = Assert.Single(await _manager.GetStatsTimelineAsync(null, null, null, null, PlaybackStatsInterval.Day, 120, Token));
        Assert.Equal(new DateTime(2026, 6, 16, 0, 0, 0, DateTimeKind.Unspecified), local.Date);
    }

    [Fact]
    public async Task GetStreamBreakdownAsync_PivotsSourceAndDeliveredStreams()
    {
        var user = CreateUser();
        var movie = CreateMovie("Transcoded", (MetadataProvider.Imdb, "tt0000012"));

        var info = MinimalInfo();
        info.Transcoded = true;
        info.Streams =
        [
            new PlaybackHistoryStreamInfo
            {
                StreamType = PlaybackHistoryStreamType.Video,
                Origin = PlaybackHistoryStreamOrigin.Source,
                Width = 3840,
                Height = 2160,
                Codec = "hevc"
            },
            new PlaybackHistoryStreamInfo
            {
                StreamType = PlaybackHistoryStreamType.Video,
                Origin = PlaybackHistoryStreamOrigin.Delivered,
                Width = 1920,
                Height = 1080,
                Codec = "h264"
            },
            new PlaybackHistoryStreamInfo
            {
                StreamType = PlaybackHistoryStreamType.Audio,
                Origin = PlaybackHistoryStreamOrigin.Source,
                Codec = "truehd",
                Channels = 8,
                Language = "eng"
            }
        ];

        await _manager.RecordPlaybackAsync(user, movie, info, Token);

        var breakdown = await _manager.GetStreamBreakdownAsync(null, null, null, null, Token);

        Assert.Equal("4K", Assert.Single(breakdown.Resolutions).Name);
        Assert.Equal("1080p", Assert.Single(breakdown.DeliveredResolutions).Name);
        Assert.Equal("hevc", Assert.Single(breakdown.VideoCodecs).Name);
        Assert.Equal("h264", Assert.Single(breakdown.DeliveredVideoCodecs).Name);
        Assert.Equal("7.1", Assert.Single(breakdown.AudioChannels).Name);
        Assert.Equal("eng", Assert.Single(breakdown.AudioLanguages).Name);
        Assert.Equal(1, breakdown.TranscodedPlays);
        Assert.Equal(0, breakdown.DirectPlays);
    }

    [Fact]
    public async Task DetachItemAsync_UnlinksIdentityButKeepsHistory()
    {
        var user = CreateUser();
        var movie = CreateMovie("Deleted Later", (MetadataProvider.Imdb, "tt0000013"));

        await _manager.RecordPlaybackAsync(user, movie, MinimalInfo(), Token);
        await _manager.DetachItemAsync(movie.Id, Token);

        using var ctx = CreateDbContext();
        var identity = await ctx.PlaybackItems.SingleAsync(Token);
        Assert.Null(identity.ItemId);

        // The session itself survives; only the link to the live item is gone.
        Assert.Equal(1, await ctx.UserPlaybackHistory.CountAsync(Token));
    }

    [Fact]
    public async Task DeleteUserHistoryAsync_RemovesSessionsAndStreamsButKeepsIdentity()
    {
        var user = CreateUser();
        var other = CreateUser();
        var movie = CreateMovie("Shared", (MetadataProvider.Imdb, "tt0000014"));

        var info = MinimalInfo();
        info.Streams =
        [
            new PlaybackHistoryStreamInfo
            {
                StreamType = PlaybackHistoryStreamType.Video,
                Origin = PlaybackHistoryStreamOrigin.Source,
                Width = 1920,
                Height = 1080
            }
        ];

        await _manager.RecordPlaybackAsync(user, movie, info, Token);
        await _manager.RecordPlaybackAsync(other, movie, MinimalInfo(), Token);

        await _manager.DeleteUserHistoryAsync(user.Id, Token);

        using var ctx = CreateDbContext();

        // The deleted user's session and its streams are gone; the other user's remains.
        Assert.Equal(0, await ctx.UserPlaybackHistory.CountAsync(h => h.UserId.Equals(user.Id), Token));
        Assert.Equal(1, await ctx.UserPlaybackHistory.CountAsync(h => h.UserId.Equals(other.Id), Token));
        Assert.Equal(0, await ctx.UserPlaybackHistoryStreams.CountAsync(Token));

        // The identity is shared, so it stays.
        Assert.Equal(1, await ctx.PlaybackItems.CountAsync(Token));
    }

    [Fact]
    public async Task GetUserItemStatsAsync_NoHistory_ReturnsEmpty()
    {
        var user = CreateUser();
        var movie = CreateMovie("Movie", (MetadataProvider.Imdb, "tt0000010"));

        var stats = await _manager.GetUserItemStatsAsync(user.Id, movie, Token);

        Assert.False(stats.HasHistory);
        Assert.Equal(0, stats.PlayCount);
        Assert.Null(stats.LastPlayedDate);
        Assert.False(stats.HasCompletion);
    }

    [Fact]
    public async Task GetUserItemStatsAsync_AggregatesRecordedSessions()
    {
        var user = CreateUser();
        var movie = CreateMovie("Movie", (MetadataProvider.Imdb, "tt0000011"));

        var first = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var second = new DateTime(2024, 6, 1, 21, 0, 0, DateTimeKind.Utc);

        await _manager.RecordPlaybackAsync(user, movie, PartialInfo(first), Token);
        await _manager.RecordPlaybackAsync(user, movie, PartialInfo(second), Token);

        var stats = await _manager.GetUserItemStatsAsync(user.Id, movie, Token);

        Assert.Equal(2, stats.PlayCount);
        Assert.Equal(second, stats.LastPlayedDate);
        Assert.False(stats.HasCompletion);

        await _manager.RecordPlaybackAsync(user, movie, MinimalInfo(), Token);
        Assert.True((await _manager.GetUserItemStatsAsync(user.Id, movie, Token)).HasCompletion);
    }

    [Fact]
    public async Task GetUserItemStatsAsync_UndatedImports_ReportNoPlayedDate()
    {
        var user = CreateUser();
        var movie = CreateMovie("Movie", (MetadataProvider.Imdb, "tt0000012"));
        await _manager.RecordPlaybackAsync(user, movie, MinimalInfo(), Token);

        await using (var ctx = CreateDbContext())
        {
            var identity = await ctx.PlaybackItems.SingleAsync(Token);
            ctx.UserPlaybackHistory.Add(new UserPlaybackHistory
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                PlaybackItemId = identity.Id,
                DateStarted = UserPlaybackHistory.UnknownDate,
                DateStopped = UserPlaybackHistory.UnknownDate,
                PlayedToCompletion = true,
                Source = PlaybackHistorySource.Imported
            });
            await ctx.SaveChangesAsync(Token);
        }

        var stats = await _manager.GetUserItemStatsAsync(user.Id, movie, Token);

        // The import counts as a play, but its sentinel date must not surface as a 1970 played date.
        Assert.Equal(2, stats.PlayCount);
        Assert.NotNull(stats.LastPlayedDate);
        Assert.True(stats.LastPlayedDate > UserPlaybackHistory.UnknownDate);
    }

    [Fact]
    public async Task GetUserItemStatsAsync_SurvivesItemReAdd()
    {
        var user = CreateUser();
        var first = CreateMovie("Movie", (MetadataProvider.Imdb, "tt0000013"));
        await _manager.RecordPlaybackAsync(user, first, MinimalInfo(), Token);

        // Same logical item, new library id: the totals are resolved by key, not by item id.
        var readded = CreateMovie("Movie", (MetadataProvider.Imdb, "tt0000013"));
        await _manager.ReattachItemAsync(readded, Token);

        Assert.Equal(1, (await _manager.GetUserItemStatsAsync(user.Id, readded, Token)).PlayCount);
    }

    [Fact]
    public async Task RebuildUserDataProjectionAsync_MatchesWhatPlaybackWrote()
    {
        var user = CreateUser();
        var movie = CreateMovie("Movie", (MetadataProvider.Imdb, "tt0000020"));
        await _manager.RecordPlaybackAsync(user, movie, PartialInfo(new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc)), Token);
        await _manager.RecordPlaybackAsync(user, movie, MinimalInfo(), Token);

        await SeedUserDataAsync(user, movie, playCount: 0, played: false, lastPlayed: null, playedOverride: null);

        var rewritten = await _manager.RebuildUserDataProjectionAsync(null, Token);
        Assert.Equal(1, rewritten);

        // The rebuild has to reproduce the incremental path exactly, or running it becomes a way to
        // change played state rather than to repair it.
        var stats = await _manager.GetUserItemStatsAsync(user.Id, movie, Token);
        var row = await GetUserDataAsync(user, movie);
        Assert.Equal(stats.PlayCount, row.PlayCount);
        Assert.Equal(stats.LastPlayedDate, row.LastPlayedDate);
        Assert.True(row.Played);
        Assert.Null(row.PlayedOverride);
    }

    [Fact]
    public async Task RebuildUserDataProjectionAsync_KeepsAnExplicitUnplayed()
    {
        var user = CreateUser();
        var movie = CreateMovie("Movie", (MetadataProvider.Imdb, "tt0000021"));
        await _manager.RecordPlaybackAsync(user, movie, PartialInfo(new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc)), Token);

        await SeedUserDataAsync(user, movie, playCount: 1, played: true, lastPlayed: null, playedOverride: false);

        await _manager.RebuildUserDataProjectionAsync(null, Token);

        // Nothing in the history completed, so the user's choice is the only thing that can settle it.
        var row = await GetUserDataAsync(user, movie);
        Assert.False(row.Played);
        Assert.False(row.PlayedOverride);
    }

    [Fact]
    public async Task RebuildUserDataProjectionAsync_CompletionRetiresTheOverride()
    {
        var user = CreateUser();
        var movie = CreateMovie("Movie", (MetadataProvider.Imdb, "tt0000022"));
        await _manager.RecordPlaybackAsync(user, movie, MinimalInfo(), Token);

        await SeedUserDataAsync(user, movie, playCount: 0, played: false, lastPlayed: null, playedOverride: false);

        await _manager.RebuildUserDataProjectionAsync(null, Token);

        // An observed completion outranks a stale "mark unplayed", the same way it does on playback.
        var row = await GetUserDataAsync(user, movie);
        Assert.True(row.Played);
        Assert.Null(row.PlayedOverride);
    }

    [Fact]
    public async Task RebuildUserDataProjectionAsync_LeavesRowsWithNoHistoryAlone()
    {
        var user = CreateUser();
        var untouched = CreateMovie("Untouched", (MetadataProvider.Imdb, "tt0000023"));
        var lastPlayed = new DateTime(2015, 4, 5, 18, 0, 0, DateTimeKind.Utc);

        await SeedUserDataAsync(user, untouched, playCount: 7, played: true, lastPlayed: lastPlayed, playedOverride: null);

        var rewritten = await _manager.RebuildUserDataProjectionAsync(null, Token);

        // An absent aggregate is not evidence that nothing was played - only that this store has no
        // record of it. Zeroing these would erase every watch that predates the history table.
        Assert.Equal(0, rewritten);
        var row = await GetUserDataAsync(user, untouched);
        Assert.Equal(7, row.PlayCount);
        Assert.True(row.Played);
        Assert.Equal(lastPlayed, row.LastPlayedDate);
    }

    [Fact]
    public async Task RebuildUserDataProjectionAsync_IsIdempotent()
    {
        var user = CreateUser();
        var movie = CreateMovie("Movie", (MetadataProvider.Imdb, "tt0000024"));
        await _manager.RecordPlaybackAsync(user, movie, MinimalInfo(), Token);
        await SeedUserDataAsync(user, movie, playCount: 0, played: false, lastPlayed: null, playedOverride: null);

        await _manager.RebuildUserDataProjectionAsync(null, Token);
        var first = await GetUserDataAsync(user, movie);

        await _manager.RebuildUserDataProjectionAsync(null, Token);
        var second = await GetUserDataAsync(user, movie);

        Assert.Equal(first.PlayCount, second.PlayCount);
        Assert.Equal(first.Played, second.Played);
        Assert.Equal(first.LastPlayedDate, second.LastPlayedDate);
    }

    [Fact]
    public async Task Statistics_ImportedEntries_CountTowardsTotalsButNotActivity()
    {
        var user = CreateUser();
        var movie = CreateMovie("Movie", (MetadataProvider.Imdb, "tt0000004"));
        await _manager.RecordPlaybackAsync(user, movie, MinimalInfo(), Token);

        await using (var ctx = CreateDbContext())
        {
            // Two plays carried over from the pre-existing watched status: no device, no bitrate, no
            // stream detail, and a date nobody recorded.
            var identity = await ctx.PlaybackItems.SingleAsync(Token);
            for (var i = 0; i < 2; i++)
            {
                ctx.UserPlaybackHistory.Add(new UserPlaybackHistory
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    PlaybackItemId = identity.Id,
                    DateStarted = DateTime.UnixEpoch,
                    DateStopped = DateTime.UnixEpoch,
                    PlayedToCompletion = true,
                    Source = PlaybackHistorySource.Imported
                });
            }

            await ctx.SaveChangesAsync(Token);
        }

        // Lifetime counters: the item really was played three times.
        var top = await _manager.GetTopItemsAsync(null, null, null, null, "plays", true, 0, 10, Token);
        Assert.Equal(3, Assert.Single(top.Items).Plays);

        // Activity: only the recorded session can say when it ran or how it was delivered. Counting the
        // imported pair here would report two extra direct plays on an unnamed device.
        var summary = await _manager.GetStatsSummaryAsync(null, null, null, null, 0, Token);
        Assert.Equal(1, summary.Plays);

        var streams = await _manager.GetStreamBreakdownAsync(null, null, null, null, Token);
        Assert.Equal(1, streams.DirectPlays + streams.TranscodedPlays);

        var timeline = await _manager.GetStatsTimelineAsync(null, null, null, null, PlaybackStatsInterval.Day, 0, Token);
        Assert.Equal(1, timeline.Sum(t => t.Plays));
    }

    private async Task SeedUserDataAsync(User user, BaseItem item, int playCount, bool played, DateTime? lastPlayed, bool? playedOverride)
    {
        await using var ctx = CreateDbContext();
        if (!await ctx.Users.AnyAsync(u => u.Id.Equals(user.Id), Token))
        {
            ctx.Users.Add(user);
        }

        ctx.BaseItems.Add(new BaseItemEntity { Id = item.Id, Type = item.GetType().FullName!, Name = item.Name });
        ctx.UserData.Add(new UserData
        {
            UserId = user.Id,
            ItemId = item.Id,
            CustomDataKey = item.GetUserDataKeys()[0],
            PlayCount = playCount,
            Played = played,
            LastPlayedDate = lastPlayed,
            PlayedOverride = playedOverride,
            Item = null,
            User = null
        });

        await ctx.SaveChangesAsync(Token);
    }

    private async Task<UserData> GetUserDataAsync(User user, BaseItem item)
    {
        await using var ctx = CreateDbContext();
        return await ctx.UserData
            .AsNoTracking()
            .SingleAsync(u => u.UserId.Equals(user.Id) && u.ItemId.Equals(item.Id), Token);
    }

    private static PlaybackHistoryInfo PartialInfo(DateTime startedAt) => new()
    {
        DateStarted = startedAt,
        DateStopped = startedAt.AddMinutes(10),
        StartPositionTicks = 0,
        StopPositionTicks = TimeSpan.FromMinutes(10).Ticks,
        RunTimeTicks = TimeSpan.FromMinutes(90).Ticks,
        PlayedDurationTicks = TimeSpan.FromMinutes(10).Ticks,
        PlayedToCompletion = false
    };

    private static PlaybackHistoryInfo MinimalInfo() => new()
    {
        DateStarted = DateTime.UtcNow.AddMinutes(-10),
        DateStopped = DateTime.UtcNow,
        StartPositionTicks = 0,
        StopPositionTicks = TimeSpan.FromMinutes(10).Ticks,
        RunTimeTicks = TimeSpan.FromMinutes(10).Ticks,
        PlayedToCompletion = true
    };

    private static Movie CreateMovie(string name, params (MetadataProvider Provider, string Value)[] providerIds)
    {
        var movie = new Movie { Id = Guid.NewGuid(), Name = name };
        foreach (var (provider, value) in providerIds)
        {
            movie.SetProviderId(provider, value);
        }

        return movie;
    }

    private static User CreateUser()
        => new User("test", "AuthProvider", "ResetProvider");

    private JellyfinDbContext CreateDbContext()
        => new JellyfinDbContext(
            _dbOptions,
            NullLogger<JellyfinDbContext>.Instance,
            new SqliteDatabaseProvider(null!, NullLogger<SqliteDatabaseProvider>.Instance),
            new NoLockBehavior(NullLogger<NoLockBehavior>.Instance));
}
