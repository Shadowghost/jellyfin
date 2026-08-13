using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Locking;
using Jellyfin.Database.Providers.Sqlite;
using Jellyfin.Server.Implementations.Item;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Item;

/// <summary>
/// Covers a merge carrying what pointed at the duplicate over to the item that survives.
/// </summary>
public sealed class ItemMergerTests : IDisposable
{
    private const string GenreType = "MediaBrowser.Controller.Entities.Genre";
    private const string MovieType = "MediaBrowser.Controller.Entities.Movies.Movie";

    private static readonly Guid _movieId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid _otherMovieId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid _keeperId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid _duplicateId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<JellyfinDbContext> _dbOptions;
    private readonly Mock<IItemPersistenceService> _persistenceService = new();
    private readonly ItemMerger _merger;

    public ItemMergerTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _dbOptions = new DbContextOptionsBuilder<JellyfinDbContext>().UseSqlite(_connection).Options;

        using (var context = CreateDbContext())
        {
            context.Database.EnsureCreated();
            AddItem(context, _movieId, MovieType, "Blade Runner");
            AddItem(context, _otherMovieId, MovieType, "Alien");
            AddItem(context, _keeperId, GenreType, "Adventure");
            AddItem(context, _duplicateId, GenreType, "Abenteuer");
            context.SaveChanges();
        }

        var factory = new Mock<IDbContextFactory<JellyfinDbContext>>();
        factory.Setup(e => e.CreateDbContextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CreateDbContext);

        _merger = new ItemMerger(
            factory.Object,
            new Mock<ILibraryManager>().Object,
            _persistenceService.Object,
            NullLogger<ItemMerger>.Instance);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task MergeAsync_MovesTheGenreLinksOfTheDuplicate()
    {
        using (var context = CreateDbContext())
        {
            AddGenreLink(context, _movieId, _duplicateId);
            AddGenreLink(context, _otherMovieId, _keeperId);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await _merger.MergeAsync(_keeperId, [_duplicateId], TestContext.Current.CancellationToken);

        using var assertContext = CreateDbContext();
        Assert.Equal(
            [_movieId, _otherMovieId],
            assertContext.BaseItemGenres.Where(e => e.GenreItemId.Equals(_keeperId)).Select(e => e.ItemId).OrderBy(e => e).ToArray());
        Assert.Empty(assertContext.BaseItemGenres.Where(e => e.GenreItemId.Equals(_duplicateId)));
    }

    [Fact]
    public async Task MergeAsync_ItemLinkedToBoth_KeepsOneLink()
    {
        using (var context = CreateDbContext())
        {
            AddGenreLink(context, _movieId, _keeperId);
            AddGenreLink(context, _movieId, _duplicateId);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // The link table is keyed on (ItemId, GenreItemId), so the colliding row goes rather than the
        // merge failing on it.
        await _merger.MergeAsync(_keeperId, [_duplicateId], TestContext.Current.CancellationToken);

        using var assertContext = CreateDbContext();
        Assert.Single(assertContext.BaseItemGenres.Where(e => e.ItemId.Equals(_movieId)));
    }

    [Fact]
    public async Task MergeAsync_MovesTheUserDataOfTheDuplicate()
    {
        var userId = Guid.NewGuid();
        using (var context = CreateDbContext())
        {
            context.Users.Add(new User("merge-test", "Default", "Default") { Id = userId });
            context.UserData.Add(new UserData
            {
                Item = null!,
                User = null!,
                ItemId = _duplicateId,
                UserId = userId,
                CustomDataKey = "Genre-Abenteuer",
                IsFavorite = true
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await _merger.MergeAsync(_keeperId, [_duplicateId], TestContext.Current.CancellationToken);

        using var assertContext = CreateDbContext();
        var userData = Assert.Single(assertContext.UserData);
        Assert.Equal(_keeperId, userData.ItemId);
        Assert.True(userData.IsFavorite);
    }

    [Fact]
    public async Task MergeAsync_DeletesWhatItMergedAway()
    {
        await _merger.MergeAsync(_keeperId, [_duplicateId], TestContext.Current.CancellationToken);

        // The library manager cannot resolve the item, so deletion falls through to the persistence service.
        _persistenceService.Verify(
            e => e.DeleteItem(It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0].Equals(_duplicateId))),
            Times.Once);
    }

    [Fact]
    public async Task MergeAsync_NothingToMerge_LeavesTheLibraryAlone()
    {
        await _merger.MergeAsync(_keeperId, [], TestContext.Current.CancellationToken);
        await _merger.MergeAsync(Guid.Empty, [_duplicateId], TestContext.Current.CancellationToken);

        using var assertContext = CreateDbContext();
        Assert.True(assertContext.BaseItems.Any(e => e.Id.Equals(_duplicateId)));
        _persistenceService.Verify(e => e.DeleteItem(It.IsAny<IReadOnlyList<Guid>>()), Times.Never);
    }

    private static void AddItem(JellyfinDbContext context, Guid id, string type, string name)
        => context.BaseItems.Add(new BaseItemEntity { Id = id, Type = type, Name = name });

    private static void AddGenreLink(JellyfinDbContext context, Guid itemId, Guid genreItemId)
        => context.BaseItemGenres.Add(new BaseItemGenre { Item = null!, ItemId = itemId, GenreItemId = genreItemId });

    private JellyfinDbContext CreateDbContext()
        => new(
            _dbOptions,
            NullLogger<JellyfinDbContext>.Instance,
            new SqliteDatabaseProvider(null!, NullLogger<SqliteDatabaseProvider>.Instance),
            new NoLockBehavior(NullLogger<NoLockBehavior>.Instance));
}
