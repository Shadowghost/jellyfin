using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Database.Implementations.Locking;
using Jellyfin.Database.Providers.Sqlite;
using Jellyfin.Server.Migrations.Routines;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Server.Tests.Migrations;

public sealed class BackfillPlaybackHistoryTests : IDisposable
{
    private const long Runtime = TimeSpan.TicksPerHour * 2;

    private static readonly Guid _userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _itemId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<JellyfinDbContext> _dbOptions;
    private readonly BackfillPlaybackHistory _migration;

    public BackfillPlaybackHistoryTests()
    {
        // Real SQLite rather than the EF InMemory provider: the migration batches SaveChanges and the
        // assertions below depend on values surviving a round trip through actual column types.
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
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CreateDbContext);

        _migration = new BackfillPlaybackHistory(factory.Object, NullLogger<BackfillPlaybackHistory>.Instance);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private JellyfinDbContext CreateDbContext()
        => new JellyfinDbContext(
            _dbOptions,
            NullLogger<JellyfinDbContext>.Instance,
            new SqliteDatabaseProvider(null!, NullLogger<SqliteDatabaseProvider>.Instance),
            new NoLockBehavior(NullLogger<NoLockBehavior>.Instance));

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task PerformAsync_PlayedItem_CreatesOneEntryPerPlay()
    {
        var lastPlayed = new DateTime(2024, 3, 1, 20, 0, 0, DateTimeKind.Utc);
        Seed(played: true, playCount: 3, lastPlayed: lastPlayed, positionTicks: 0);

        await _migration.PerformAsync(Token);

        var entries = await GetEntriesAsync();

        // The play count is what survived the old schema, so it is what the history has to reproduce.
        Assert.Equal(3, entries.Count);
        Assert.All(entries, e => Assert.Equal(PlaybackHistorySource.Imported, e.Source));
        Assert.All(entries, e => Assert.True(e.PlayedToCompletion));
        Assert.All(entries, e => Assert.Equal(Runtime, e.PlayedDurationTicks));
    }

    [Fact]
    public async Task PerformAsync_PlayedItem_DatesOnlyTheMostRecentPlay()
    {
        var lastPlayed = new DateTime(2024, 3, 1, 20, 0, 0, DateTimeKind.Utc);
        Seed(played: true, playCount: 3, lastPlayed: lastPlayed, positionTicks: 0);

        await _migration.PerformAsync(Token);

        var entries = await GetEntriesAsync();

        // LastPlayedDate describes one play. The other two are known to have happened but not when,
        // so they carry the sentinel rather than a plausible-looking invention.
        var dated = Assert.Single(entries, e => e.DateStarted != UserPlaybackHistory.UnknownDate);
        Assert.Equal(lastPlayed, dated.DateStarted);
        Assert.Equal(2, entries.Count(e => e.DateStarted == UserPlaybackHistory.UnknownDate));

        // LastPlayedDate is stamped at playback start, so the session has to span the watch time.
        Assert.Equal(lastPlayed.AddTicks(Runtime), dated.DateStopped);
    }

    [Fact]
    public async Task PerformAsync_NoKnownDate_PutsNothingOnUpgradeDay()
    {
        var before = DateTime.UtcNow;
        Seed(played: true, playCount: 2, lastPlayed: null, positionTicks: 0);

        await _migration.PerformAsync(Token);

        var entries = await GetEntriesAsync();

        // The regression this guards: stamping DateTime.UtcNow piled every pre-existing watch onto the
        // day of the upgrade, which then became the busiest day the dashboard had ever seen.
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.True(e.DateStopped < before, $"entry dated {e.DateStopped:O} lands on upgrade day"));
    }

    [Fact]
    public async Task PerformAsync_PartiallyWatchedItem_AttributesTheResumePositionOnce()
    {
        var position = TimeSpan.TicksPerMinute * 30;
        Seed(played: false, playCount: 3, lastPlayed: new DateTime(2024, 3, 1, 20, 0, 0, DateTimeKind.Utc), positionTicks: position);

        await _migration.PerformAsync(Token);

        var entries = await GetEntriesAsync();

        Assert.Equal(3, entries.Count);
        Assert.All(entries, e => Assert.False(e.PlayedToCompletion));

        // Three sittings reached a single resume point. Giving each entry the full position would
        // claim 90 minutes of watch time for 30 minutes of content.
        Assert.Equal(position, entries.Sum(e => e.PlayedDurationTicks));
    }

    [Fact]
    public async Task PerformAsync_LeavesUserDataUntouched()
    {
        Seed(played: true, playCount: 3, lastPlayed: new DateTime(2024, 3, 1, 20, 0, 0, DateTimeKind.Utc), positionTicks: 0);

        await _migration.PerformAsync(Token);

        await using var context = CreateDbContext();
        var userData = await context.UserData.AsNoTracking().SingleAsync(Token);

        // UserData stays the source of truth for played state, so the migration cannot change what
        // any user sees as watched.
        Assert.True(userData.Played);
        Assert.Equal(3, userData.PlayCount);
    }

    [Fact]
    public async Task PerformAsync_RunTwice_DoesNotDuplicate()
    {
        Seed(played: true, playCount: 3, lastPlayed: new DateTime(2024, 3, 1, 20, 0, 0, DateTimeKind.Utc), positionTicks: 0);

        await _migration.PerformAsync(Token);
        await _migration.PerformAsync(Token);

        Assert.Equal(3, (await GetEntriesAsync()).Count);
    }

    [Fact]
    public async Task PerformAsync_WithRecordedSessions_StillBackfills()
    {
        Seed(played: true, playCount: 1, lastPlayed: new DateTime(2024, 3, 1, 20, 0, 0, DateTimeKind.Utc), positionTicks: 0);

        await using (var context = CreateDbContext())
        {
            // A server that recorded live sessions before the migration ran still needs its
            // pre-existing watched status brought across, so the skip cannot key on an empty table.
            var identity = Guid.NewGuid();
            context.PlaybackItems.Add(new PlaybackItem { Id = identity, ItemId = Guid.NewGuid(), DateCreated = DateTime.UtcNow });
            context.UserPlaybackHistory.Add(new UserPlaybackHistory
            {
                Id = Guid.NewGuid(),
                UserId = _userId,
                PlaybackItemId = identity,
                DateStarted = DateTime.UtcNow,
                DateStopped = DateTime.UtcNow,
                Source = PlaybackHistorySource.Recorded
            });
            await context.SaveChangesAsync(Token);
        }

        await _migration.PerformAsync(Token);

        Assert.Single(await GetEntriesAsync());
    }

    private async Task<System.Collections.Generic.List<UserPlaybackHistory>> GetEntriesAsync()
    {
        await using var context = CreateDbContext();
        return await context.UserPlaybackHistory
            .AsNoTracking()
            .Where(h => h.Source == PlaybackHistorySource.Imported)
            .ToListAsync(Token);
    }

    private void Seed(bool played, int playCount, DateTime? lastPlayed, long positionTicks)
    {
        using var context = CreateDbContext();

        context.Users.Add(new User("backfill", "Provider", "Reset") { Id = _userId });
        context.BaseItems.Add(new BaseItemEntity
        {
            Id = _itemId,
            Type = "MediaBrowser.Controller.Entities.Movies.Movie",
            Name = "Movie",
            RunTimeTicks = Runtime
        });
        context.UserData.Add(new UserData
        {
            UserId = _userId,
            ItemId = _itemId,
            CustomDataKey = "movie-key",
            Played = played,
            PlayCount = playCount,
            LastPlayedDate = lastPlayed,
            PlaybackPositionTicks = positionTicks,
            Item = null,
            User = null
        });

        context.SaveChanges();
    }
}
