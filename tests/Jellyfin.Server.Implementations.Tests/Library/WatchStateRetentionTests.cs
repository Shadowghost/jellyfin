using System;
using System.Threading;
using System.Threading.Tasks;
using Emby.Server.Implementations.ScheduledTasks.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Database.Implementations.Locking;
using Jellyfin.Database.Providers.Sqlite;
using Jellyfin.Server.Implementations.Item;
using MediaBrowser.Model.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Library;

/// <summary>
/// A deleted item leaves watch state in two places - parked user data rows and a detached playback
/// identity - matched by the same data keys. These cover them expiring together, because whichever
/// half outlives the other ends up describing something the other has forgotten.
/// </summary>
public sealed class WatchStateRetentionTests : IDisposable
{
    private static readonly Guid _userId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<JellyfinDbContext> _dbOptions;
    private readonly CleanupUserDataTask _task;

    public WatchStateRetentionTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _dbOptions = new DbContextOptionsBuilder<JellyfinDbContext>().UseSqlite(_connection).Options;
        using (var ctx = CreateDbContext())
        {
            ctx.Database.EnsureCreated();
        }

        var factory = new Mock<IDbContextFactory<JellyfinDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CreateDbContext);

        _task = new CleanupUserDataTask(
            Mock.Of<ILocalizationManager>(),
            factory.Object,
            NullLogger<CleanupUserDataTask>.Instance);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Cleanup_ExpiredOnBothSides_RemovesEverything()
    {
        await SeedAsync(retentionDate: DateTime.UtcNow.AddDays(-120), attached: false);

        await _task.ExecuteAsync(new Progress<double>(), Token);

        await using var ctx = CreateDbContext();
        Assert.Equal(0, await ctx.UserData.CountAsync(Token));
        Assert.Equal(0, await ctx.UserPlaybackHistory.CountAsync(Token));
        Assert.Equal(0, await ctx.PlaybackItems.CountAsync(Token));
        Assert.Equal(0, await ctx.PlaybackItemKeys.CountAsync(Token));
    }

    [Fact]
    public async Task Cleanup_UserDataStillInRetention_KeepsTheHistoryToo()
    {
        await SeedAsync(retentionDate: DateTime.UtcNow.AddDays(-10), attached: false);

        await _task.ExecuteAsync(new Progress<double>(), Token);

        // The old failure mode: history was purged on its own 24-hour schedule while the user data it
        // belonged to sat waiting to be restored. Re-adding the item then brought back a play count
        // that the next playback overwrote with one, because the history behind it was gone.
        await using var ctx = CreateDbContext();
        Assert.Equal(1, await ctx.UserData.CountAsync(Token));
        Assert.Equal(1, await ctx.UserPlaybackHistory.CountAsync(Token));
        Assert.Equal(1, await ctx.PlaybackItems.CountAsync(Token));
    }

    [Fact]
    public async Task Cleanup_ItemBackInTheLibrary_KeepsEverything()
    {
        // An attached row means the item returned under a new id; the identity is waiting to be
        // re-adopted by key, not litter.
        await SeedAsync(retentionDate: null, attached: true);

        await _task.ExecuteAsync(new Progress<double>(), Token);

        await using var ctx = CreateDbContext();
        Assert.Equal(1, await ctx.UserData.CountAsync(Token));
        Assert.Equal(1, await ctx.UserPlaybackHistory.CountAsync(Token));
        Assert.Equal(1, await ctx.PlaybackItems.CountAsync(Token));
    }

    [Fact]
    public async Task Cleanup_RecentlyRecordedPlayback_KeepsTheHistory()
    {
        await SeedAsync(retentionDate: DateTime.UtcNow.AddDays(-120), attached: false, playedAt: DateTime.UtcNow.AddDays(-5));

        await _task.ExecuteAsync(new Progress<double>(), Token);

        await using var ctx = CreateDbContext();
        Assert.Equal(1, await ctx.UserPlaybackHistory.CountAsync(Token));
    }

    [Fact]
    public async Task Cleanup_ImportedEntriesDoNotCountAsRecentPlayback()
    {
        // An imported entry describes a play at an unknown time before the upgrade. Stamping it as
        // recent would renew the retention window for long-deleted items on every upgrade.
        await SeedAsync(
            retentionDate: DateTime.UtcNow.AddDays(-120),
            attached: false,
            playedAt: DateTime.UtcNow.AddDays(-5),
            source: PlaybackHistorySource.Imported);

        await _task.ExecuteAsync(new Progress<double>(), Token);

        await using var ctx = CreateDbContext();
        Assert.Equal(0, await ctx.UserPlaybackHistory.CountAsync(Token));
    }

    private async Task SeedAsync(
        DateTime? retentionDate,
        bool attached,
        DateTime? playedAt = null,
        PlaybackHistorySource source = PlaybackHistorySource.Recorded)
    {
        const string Key = "retention-key";
        var liveItemId = Guid.NewGuid();
        var identityId = Guid.NewGuid();

        await using var ctx = CreateDbContext();
        ctx.Users.Add(new User("retention", "Provider", "Reset") { Id = _userId });

        if (attached)
        {
            ctx.BaseItems.Add(new BaseItemEntity { Id = liveItemId, Type = "Movie", Name = "Movie" });
        }

        ctx.UserData.Add(new UserData
        {
            UserId = _userId,
            ItemId = attached ? liveItemId : BaseItemRepository.PlaceholderId,
            CustomDataKey = Key,
            RetentionDate = retentionDate,
            Played = true,
            PlayCount = 4,
            Item = null,
            User = null
        });

        ctx.PlaybackItems.Add(new PlaybackItem { Id = identityId, ItemId = null, DateCreated = DateTime.UtcNow.AddYears(-1) });
        ctx.PlaybackItemKeys.Add(new PlaybackItemKey { Id = Guid.NewGuid(), PlaybackItemId = identityId, Key = Key });
        ctx.UserPlaybackHistory.Add(new UserPlaybackHistory
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            PlaybackItemId = identityId,
            DateStarted = playedAt ?? DateTime.UtcNow.AddYears(-1),
            DateStopped = playedAt ?? DateTime.UtcNow.AddYears(-1),
            PlayedToCompletion = true,
            Source = source
        });

        await ctx.SaveChangesAsync(Token);
    }

    private JellyfinDbContext CreateDbContext()
        => new JellyfinDbContext(
            _dbOptions,
            NullLogger<JellyfinDbContext>.Instance,
            new SqliteDatabaseProvider(null!, NullLogger<SqliteDatabaseProvider>.Instance),
            new NoLockBehavior(NullLogger<NoLockBehavior>.Instance));
}
