using System;
using System.Collections.Generic;
using System.Linq;
using Emby.Server.Implementations.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Database.Implementations.Locking;
using Jellyfin.Database.Providers.Sqlite;
using Jellyfin.Extensions;
using Jellyfin.Server.Implementations.Item;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using BaseItemKind = Jellyfin.Data.Enums.BaseItemKind;

namespace Jellyfin.Server.Implementations.Tests.Item;

/// <summary>
/// Covers a music credit pointing at the artist item rather than at a person of its own.
/// </summary>
public sealed class MusicArtistCreditTests : IDisposable
{
    private const string ArtistPresentationKey = "Artist-Miles Davis";

    private static readonly Guid _albumId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid _artistItemId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid _guestAlbumId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid _unrelatedAlbumId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid _albumArtistCreditId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid _artistCreditId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _folderArtistItemId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid _folderAlbumId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid _folderCreditId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<JellyfinDbContext> _dbOptions;
    private readonly BaseItemRepository _repository;
    private readonly ItemCountService _countService;
    private readonly string _artistTypeName;
    private readonly string _albumTypeName;

    public MusicArtistCreditTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _dbOptions = new DbContextOptionsBuilder<JellyfinDbContext>()
            .UseSqlite(_connection)
            .Options;

        var itemTypeLookup = new ItemTypeLookup();
        _artistTypeName = itemTypeLookup.BaseItemKindNames[BaseItemKind.MusicArtist];
        _albumTypeName = itemTypeLookup.BaseItemKindNames[BaseItemKind.MusicAlbum];

        using (var ctx = CreateDbContext())
        {
            ctx.Database.EnsureCreated();
            ctx.BaseItems.Add(new BaseItemEntity
            {
                Id = _albumId,
                Type = itemTypeLookup.BaseItemKindNames[BaseItemKind.MusicAlbum],
                Name = "Kind of Blue",
                CleanName = "kind of blue",
                IsFolder = true,
                IsVirtualItem = false
            });
            ctx.BaseItems.Add(new BaseItemEntity
            {
                Id = _artistItemId,
                Type = _artistTypeName,
                Name = "Miles Davis",
                CleanName = "miles davis",
                PresentationUniqueKey = ArtistPresentationKey,
                IsFolder = false,
                IsVirtualItem = false
            });
            ctx.BaseItems.Add(new BaseItemEntity
            {
                Id = _guestAlbumId,
                Type = itemTypeLookup.BaseItemKindNames[BaseItemKind.MusicAlbum],
                Name = "Someone Else's Record",
                CleanName = "someone elses record",
                IsFolder = true,
                IsVirtualItem = false
            });
            ctx.BaseItems.Add(new BaseItemEntity
            {
                Id = _unrelatedAlbumId,
                Type = itemTypeLookup.BaseItemKindNames[BaseItemKind.MusicAlbum],
                Name = "Unrelated",
                CleanName = "unrelated",
                IsFolder = true,
                IsVirtualItem = false
            });

            AddCredit(ctx, _albumArtistCreditId, PersonKind.AlbumArtist, _albumId);
            AddCredit(ctx, _artistCreditId, PersonKind.Artist, _guestAlbumId);
            ctx.SaveChanges();
        }

        var factory = new Mock<IDbContextFactory<JellyfinDbContext>>();
        factory.Setup(f => f.CreateDbContext()).Returns(CreateDbContext);

        var serverConfigurationManager = new Mock<IServerConfigurationManager>();
        serverConfigurationManager.Setup(c => c.Configuration).Returns(new ServerConfiguration());

        _repository = new BaseItemRepository(
            factory.Object,
            new Mock<IServerApplicationHost>().Object,
            itemTypeLookup,
            serverConfigurationManager.Object,
            NullLogger<BaseItemRepository>.Instance);

        var queryHelpers = new Mock<IItemQueryHelpers>();
        queryHelpers
            .Setup(h => h.ApplyAccessFiltering(
                It.IsAny<JellyfinDbContext>(),
                It.IsAny<IQueryable<BaseItemEntity>>(),
                It.IsAny<InternalItemsQuery>()))
            .Returns((JellyfinDbContext _, IQueryable<BaseItemEntity> query, InternalItemsQuery _) => query);

        _countService = new ItemCountService(
            factory.Object,
            itemTypeLookup,
            queryHelpers.Object);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public void GetItemList_ByArtistItemId_ReturnsWhatTheArtistIsCreditedOn()
    {
        // The credit link never looks at the target's type.
        var result = _repository.GetItemList(QueryByCreditedItem(_artistItemId));

        Assert.Equal(
            [_albumId, _guestAlbumId],
            result.Select(e => e.Id).OrderBy(e => e.ToString(), StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void GetItemList_ByArtistItemId_AfterTheArtistWasRenamed_StillReturnsIt()
    {
        using (var ctx = CreateDbContext())
        {
            var artist = ctx.BaseItems.Single(e => e.Id.Equals(_artistItemId));
            artist.Name = "Miles Dewey Davis III";
            artist.CleanName = artist.Name.GetCleanValue();
            ctx.SaveChanges();
        }

        var result = _repository.GetItemList(QueryByCreditedItem(_artistItemId));

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetItemList_ArtistIds_ReturnsEveryReleaseTheArtistIsOn()
    {
        var result = _repository.GetItemList(MusicQuery(q => q.ArtistIds = [_artistItemId]));

        Assert.Equal(
            [_albumId, _guestAlbumId],
            result.Select(e => e.Id).OrderBy(e => e.ToString(), StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void GetItemList_AlbumArtistIds_ReturnsOnlyWhatIsFiledUnderTheArtist()
    {
        var result = _repository.GetItemList(MusicQuery(q => q.AlbumArtistIds = [_artistItemId]));

        var item = Assert.Single(result);
        Assert.Equal(_albumId, item.Id);
    }

    [Fact]
    public void GetItemList_ContributingArtistIds_ReturnsOnlyTheGuestAppearance()
    {
        var result = _repository.GetItemList(MusicQuery(q => q.ContributingArtistIds = [_artistItemId]));

        var item = Assert.Single(result);
        Assert.Equal(_guestAlbumId, item.Id);
    }

    [Fact]
    public void GetItemList_ExcludeArtistIds_DropsEverythingTheArtistIsOn()
    {
        var result = _repository.GetItemList(MusicQuery(q => q.ExcludeArtistIds = [_artistItemId]));

        var item = Assert.Single(result);
        Assert.Equal(_unrelatedAlbumId, item.Id);
    }

    [Fact]
    public void GetItemList_ArtistIds_AfterTheArtistWasRenamed_StillFilters()
    {
        Rename("Miles Dewey Davis III");

        var result = _repository.GetItemList(MusicQuery(q => q.ArtistIds = [_artistItemId]));

        Assert.Equal(2, result.Count);
    }

    [Theory]
    [InlineData(ItemSortBy.AlbumArtist)]
    [InlineData(ItemSortBy.Artist)]
    public void GetItemList_SortedByArtist_OrdersOnTheCreditedName(ItemSortBy sortBy)
    {
        Rename("Aaron Anonymous");

        var result = _repository.GetItemList(MusicQuery(q =>
        {
            q.OrderBy = [(sortBy, SortOrder.Ascending)];
        }));

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void GetItemCountsForNameItem_CountsWhatTheArtistIsCreditedOn()
    {
        var counts = _countService.GetItemCountsForNameItem(
            BaseItemKind.MusicArtist,
            _artistItemId,
            [BaseItemKind.MusicAlbum],
            new InternalItemsQuery());

        Assert.Equal(2, counts.AlbumCount);
    }

    [Fact]
    public void GetItemCountsForNameItem_AfterTheArtistWasRenamed_StillCounts()
    {
        Rename("Aaron Anonymous");

        var counts = _countService.GetItemCountsForNameItem(
            BaseItemKind.MusicArtist,
            _artistItemId,
            [BaseItemKind.MusicAlbum],
            new InternalItemsQuery());

        Assert.Equal(2, counts.AlbumCount);
    }

    [Fact]
    public void GetAllArtists_ListsTheArtistWithItsCounts()
    {
        var result = _repository.GetAllArtists(ByNameQuery());

        var entry = Assert.Single(result.Items);
        Assert.Equal(_artistItemId, entry.Item.Id);
        Assert.Equal(2, entry.ItemCounts!.AlbumCount);
    }

    [Fact]
    public void GetAllArtists_AfterTheArtistWasRenamed_StillListsIt()
    {
        // A clean name that stops matching used to drop the artist out of /Artists.
        Rename("Miles Dewey Davis III");

        var result = _repository.GetAllArtists(ByNameQuery());

        var entry = Assert.Single(result.Items);
        Assert.Equal("Miles Dewey Davis III", entry.Item.Name);
        Assert.Equal(2, entry.ItemCounts!.AlbumCount);
    }

    [Fact]
    public void GetAlbumArtists_ListsOnlyWhatIsFiledUnderTheArtist()
    {
        var result = _repository.GetAlbumArtists(ByNameQuery());

        var entry = Assert.Single(result.Items);
        Assert.Equal(1, entry.ItemCounts!.AlbumCount);
    }

    [Fact]
    public void GetAllArtists_ArtistNothingCredits_IsNotListed()
    {
        using (var ctx = CreateDbContext())
        {
            ctx.PeopleBaseItemMap.RemoveRange(ctx.PeopleBaseItemMap);
            ctx.SaveChanges();
        }

        Assert.Empty(_repository.GetAllArtists(ByNameQuery()).Items);
    }

    [Fact]
    public void GetAllArtistNames_AfterTheArtistWasRenamed_ReturnsTheNameItCarriesNow()
    {
        Rename("Miles Dewey Davis III");

        Assert.Equal(["Miles Dewey Davis III"], _repository.GetAllArtistNames());
    }

    [Fact]
    public void GetItemList_IsDeadArtist_FindsAnArtistNothingCredits()
    {
        using (var ctx = CreateDbContext())
        {
            ctx.PeopleBaseItemMap.RemoveRange(ctx.PeopleBaseItemMap);
            ctx.Peoples.RemoveRange(ctx.Peoples);
            ctx.SaveChanges();
        }

        var result = _repository.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.MusicArtist],
            IsDeadArtist = true
        });

        var item = Assert.Single(result);
        Assert.Equal(_artistItemId, item.Id);
    }

    [Fact]
    public void GetItemList_IsDeadArtist_DoesNotClaimARenamedArtist()
    {
        Rename("Miles Dewey Davis III");

        var result = _repository.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.MusicArtist],
            IsDeadArtist = true
        });

        Assert.Empty(result);
    }

    [Fact]
    public void GetItemList_DeadPerson_DoesNotClaimAnArtistItem()
    {
        var result = _repository.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.MusicArtist],
            IsDeadPerson = true
        });

        Assert.Empty(result);
    }

    [Fact]
    public void GetItemList_AlbumArtistIds_ReachesWhatIsFiledUnderTheArtistsOtherItem()
    {
        AddScannedArtistFolderOfTheSameName();

        // The client only ever holds the one id GetItemsByName collapsed the group onto, and it is not
        // the same one every release was filed under, so either has to stand for the whole artist.
        foreach (var artistId in new[] { _artistItemId, _folderArtistItemId })
        {
            var result = _repository.GetItemList(MusicQuery(q => q.AlbumArtistIds = [artistId]));

            Assert.Equal(
                new[] { _albumId, _folderAlbumId }.OrderBy(e => e.ToString(), StringComparer.Ordinal).ToArray(),
                result.Select(e => e.Id).OrderBy(e => e.ToString(), StringComparer.Ordinal).ToArray());
        }
    }

    [Fact]
    public void GetItemCountsForNameItem_CountsTheWholeArtistNotOneOfItsItems()
    {
        AddScannedArtistFolderOfTheSameName();

        var counts = _countService.GetItemCountsForNameItem(
            BaseItemKind.MusicArtist,
            _artistItemId,
            [BaseItemKind.MusicAlbum],
            new InternalItemsQuery());

        Assert.Equal(3, counts.AlbumCount);
    }

    // A library that holds an artist folder resolves credits to it, while credits written before it was
    // scanned point at the by-name entry. Both items are the one artist, and share its key.
    private void AddScannedArtistFolderOfTheSameName()
    {
        using var ctx = CreateDbContext();

        ctx.BaseItems.Add(new BaseItemEntity
        {
            Id = _folderArtistItemId,
            Type = _artistTypeName,
            Name = "Miles Davis",
            CleanName = "miles davis",
            PresentationUniqueKey = ArtistPresentationKey,
            IsFolder = true,
            IsVirtualItem = false
        });
        ctx.BaseItems.Add(new BaseItemEntity
        {
            Id = _folderAlbumId,
            Type = _albumTypeName,
            Name = "Bitches Brew",
            CleanName = "bitches brew",
            IsFolder = true,
            IsVirtualItem = false
        });
        ctx.Peoples.Add(new People
        {
            Id = _folderCreditId,
            Name = "Miles Davis",
            CleanName = "miles davis",
            ItemId = _folderArtistItemId,
            PersonType = nameof(PersonKind.AlbumArtist)
        });
        ctx.PeopleBaseItemMap.Add(new PeopleBaseItemMap
        {
            Item = null!,
            ItemId = _folderAlbumId,
            People = null!,
            PeopleId = _folderCreditId,
            ListOrder = 0,
            Role = string.Empty
        });

        ctx.SaveChanges();
    }

    private static void AddCredit(JellyfinDbContext ctx, Guid creditId, PersonKind kind, Guid onItemId)
    {
        ctx.Peoples.Add(new People
        {
            Id = creditId,
            Name = "Miles Davis",
            CleanName = "miles davis",
            ItemId = _artistItemId,
            PersonType = kind.ToString()
        });
        ctx.PeopleBaseItemMap.Add(new PeopleBaseItemMap
        {
            Item = null!,
            ItemId = onItemId,
            People = null!,
            PeopleId = creditId,
            ListOrder = 0,
            Role = string.Empty
        });
    }

    private static InternalItemsQuery QueryByCreditedItem(Guid itemId)
    {
        return MusicQuery(q => q.PersonIds = [itemId]);
    }

    private static InternalItemsQuery ByNameQuery()
    {
        return new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.MusicAlbum],
            EnableTotalRecordCount = true
        };
    }

    private static InternalItemsQuery MusicQuery(Action<InternalItemsQuery> configure)
    {
        var query = new InternalItemsQuery { IncludeItemTypes = [BaseItemKind.MusicAlbum] };
        configure(query);

        return query;
    }

    private void Rename(string newName)
    {
        using var ctx = CreateDbContext();
        var artist = ctx.BaseItems.Single(e => e.Id.Equals(_artistItemId));
        artist.Name = newName;
        artist.CleanName = newName.GetCleanValue();
        ctx.SaveChanges();
    }

    private JellyfinDbContext CreateDbContext()
    {
        return new JellyfinDbContext(
            _dbOptions,
            NullLogger<JellyfinDbContext>.Instance,
            new SqliteDatabaseProvider(null!, NullLogger<SqliteDatabaseProvider>.Instance),
            new NoLockBehavior(NullLogger<NoLockBehavior>.Instance));
    }
}
