using System;
using System.Threading;
using System.Threading.Tasks;
using Emby.Server.Implementations.Library;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Locking;
using Jellyfin.Database.Providers.Sqlite;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Library;

/// <summary>
/// Covers UserData as a projection of the playback history: what the history settles, what an
/// explicit mark-played/unplayed overrides, and which fields the projection must not touch.
/// </summary>
public sealed class UserDataProjectionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<JellyfinDbContext> _dbOptions;
    private readonly UserDataManager _manager;

    public UserDataProjectionTests()
    {
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

        var config = new Mock<IServerConfigurationManager>();
        config.SetupGet(c => c.Configuration).Returns(new ServerConfiguration());

        var factory = new Mock<IDbContextFactory<JellyfinDbContext>>();
        factory.Setup(f => f.CreateDbContext()).Returns(CreateDbContext);
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CreateDbContext);

        _manager = new UserDataManager(config.Object, factory.Object);
        BaseItem.UserDataManager = _manager;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task ApplyPlaybackStats_CompletedHistory_MarksPlayed()
    {
        var (user, book) = await SeedAsync();

        _manager.ApplyPlaybackStats(user, book, new PlaybackItemStats(3, new DateTime(2024, 5, 1, 20, 0, 0, DateTimeKind.Utc), true));

        var data = _manager.GetUserData(user, book)!;
        Assert.True(data.Played);
        Assert.Equal(3, data.PlayCount);
        Assert.Equal(new DateTime(2024, 5, 1, 20, 0, 0, DateTimeKind.Utc), data.LastPlayedDate);
    }

    [Fact]
    public async Task ApplyPlaybackStats_NoHistory_LeavesPlayedDateAlone()
    {
        var (user, book) = await SeedAsync();
        var imported = new DateTime(2019, 2, 3, 9, 0, 0, DateTimeKind.Utc);

        book.MarkPlayed(user, imported, true);
        _manager.ApplyPlaybackStats(user, book, default);

        // An empty aggregate is not evidence that a metadata-imported play never happened.
        Assert.Equal(imported, _manager.GetUserData(user, book)!.LastPlayedDate);
    }

    [Fact]
    public async Task MarkUnplayed_ThenProjection_StaysUnplayed()
    {
        var (user, book) = await SeedAsync();
        _manager.ApplyPlaybackStats(user, book, new PlaybackItemStats(1, DateTime.UtcNow, true));

        book.MarkUnplayed(user);

        // The history still holds the completion, so without the override the next projection would
        // silently flip the item back to played.
        _manager.ApplyPlaybackStats(user, book, new PlaybackItemStats(1, DateTime.UtcNow, false));

        Assert.False(_manager.GetUserData(user, book)!.Played);
    }

    [Fact]
    public async Task MarkUnplayed_KeepsThePlaysThatHappened()
    {
        var (user, book) = await SeedAsync();
        _manager.ApplyPlaybackStats(user, book, new PlaybackItemStats(4, new DateTime(2024, 5, 1, 20, 0, 0, DateTimeKind.Utc), true));

        book.MarkUnplayed(user);

        // Playback history is append-only; marking something unplayed does not unmake four plays.
        var data = _manager.GetUserData(user, book)!;
        Assert.False(data.Played);
        Assert.Equal(4, data.PlayCount);
        Assert.Equal(0, data.PlaybackPositionTicks);
    }

    [Fact]
    public async Task MarkUnplayed_ThenActuallyWatched_ReadsPlayedAgain()
    {
        var (user, book) = await SeedAsync();
        book.MarkUnplayed(user);

        // A newly observed completion retires the manual choice, so watching something you had marked
        // unplayed does not leave it stuck.
        _manager.ApplyPlaybackStats(user, book, new PlaybackItemStats(1, DateTime.UtcNow, true));

        Assert.True(_manager.GetUserData(user, book)!.Played);
    }

    [Fact]
    public async Task MarkPlayed_WithoutHistory_DoesNotInventPlays()
    {
        var (user, book) = await SeedAsync();

        book.MarkPlayed(user, null, true);

        var data = _manager.GetUserData(user, book)!;
        Assert.True(data.Played);
        Assert.Equal(0, data.PlayCount);
    }

    [Fact]
    public async Task ApplyPlaybackStats_ItemWithoutPlayedStatus_StaysUnplayed()
    {
        var (user, _) = await SeedAsync();
        var album = new PhotoAlbum { Id = Guid.NewGuid(), Name = "Album" };

        await using (var context = CreateDbContext())
        {
            context.BaseItems.Add(new BaseItemEntity { Id = album.Id, Type = typeof(PhotoAlbum).FullName!, Name = album.Name });
            await context.SaveChangesAsync(Token);
        }

        // A recorded completion still cannot make something played that has no played state to set.
        _manager.ApplyPlaybackStats(user, album, new PlaybackItemStats(1, DateTime.UtcNow, true));

        Assert.False(_manager.GetUserData(user, album)!.Played);
    }

    [Fact]
    public async Task ExcludedFromResume_RoundTripsAndKeepsTheResumePosition()
    {
        var (user, book) = await SeedAsync();

        var data = _manager.GetUserData(user, book)!;
        data.PlaybackPositionTicks = TimeSpan.TicksPerMinute * 20;
        data.ExcludedFromResume = true;
        _manager.SaveUserData(user, book, data, UserDataSaveReason.UpdateUserData, Token);

        // Dismissing from Continue Watching hides the item; it does not forget where the user got to,
        // so playing it again still resumes.
        var reloaded = _manager.GetUserData(user, book)!;
        Assert.True(reloaded.ExcludedFromResume);
        Assert.Equal(TimeSpan.TicksPerMinute * 20, reloaded.PlaybackPositionTicks);
        Assert.False(reloaded.Played);
    }

    [Fact]
    public async Task ExcludedFromResume_SurvivesAProjectionRefresh()
    {
        var (user, book) = await SeedAsync();

        var data = _manager.GetUserData(user, book)!;
        data.ExcludedFromResume = true;
        _manager.SaveUserData(user, book, data, UserDataSaveReason.UpdateUserData, Token);

        // The projection owns played state, play count, and played date. A dismissal is a separate
        // choice and a stray progress report must not quietly undo it - only playing the item does.
        _manager.ApplyPlaybackStats(user, book, new PlaybackItemStats(2, DateTime.UtcNow, false));

        Assert.True(_manager.GetUserData(user, book)!.ExcludedFromResume);
    }

    [Fact]
    public async Task ExcludedFromResume_IsReportedToClients()
    {
        var (user, book) = await SeedAsync();

        var data = _manager.GetUserData(user, book)!;
        data.ExcludedFromResume = true;
        _manager.SaveUserData(user, book, data, UserDataSaveReason.UpdateUserData, Token);

        Assert.True(_manager.GetUserDataDto(book, user)!.ExcludedFromResume);
    }

    private async Task<(User User, Book Book)> SeedAsync()
    {
        var user = new User("projection", "AuthProvider", "ResetProvider");
        // A Book rather than a Video: mark-played on a Video fans out through PropagatePlayedState
        // into the alternate-version lookup, which needs a live LibraryManager this test has no use for.
        var book = new Book { Id = Guid.NewGuid(), Name = "Book" };
        book.SetProviderId(MetadataProvider.Imdb, "tt0000100");

        await using var context = CreateDbContext();
        context.Users.Add(user);
        context.BaseItems.Add(new BaseItemEntity
        {
            Id = book.Id,
            Type = typeof(Book).FullName!,
            Name = book.Name
        });
        await context.SaveChangesAsync(Token);

        return (user, book);
    }

    private JellyfinDbContext CreateDbContext()
        => new JellyfinDbContext(
            _dbOptions,
            NullLogger<JellyfinDbContext>.Instance,
            new SqliteDatabaseProvider(null!, NullLogger<SqliteDatabaseProvider>.Instance),
            new NoLockBehavior(NullLogger<NoLockBehavior>.Instance));
}
