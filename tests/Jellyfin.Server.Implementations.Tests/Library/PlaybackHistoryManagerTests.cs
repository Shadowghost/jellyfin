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

        _manager = new PlaybackHistoryManager(factory.Object);
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

        var history = await _manager.GetHistoryAsync(user.Id, null, null, null, null, null, Token);
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

        Assert.Single(await _manager.GetHistoryAsync(user.Id, movie.Id, null, null, null, null, Token));
        Assert.Empty(await _manager.GetHistoryAsync(user.Id, Guid.NewGuid(), null, null, null, null, Token));
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

        var summary = await _manager.GetStatsSummaryAsync(null, null, null, null, Token);

        Assert.Equal(2, summary.Plays);
        Assert.Equal(6_000_000, summary.AverageBitrate);

        // (8 Mbps + 4 Mbps) over 60s each = 720 Mbit = 90,000,000 bytes.
        Assert.Equal(90_000_000, summary.TotalDataTransferredBytes);

        // 120s total watch time spread over 2 distinct active days = 60s/day.
        Assert.Equal(TimeSpan.FromSeconds(60).Ticks, summary.AverageDailyWatchTimeTicks);
    }

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
