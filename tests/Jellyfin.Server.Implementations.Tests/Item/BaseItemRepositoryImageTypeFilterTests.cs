using System;
using System.Linq;
using Emby.Server.Implementations.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Server.Implementations.Item;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Item;

/// <summary>
/// Covers <see cref="InternalItemsQuery.ImageTypes"/> and its match-all companion. The default is a
/// match on any one of the requested types; a caller that needs every type present, such as the hero
/// section wanting both a logo and a backdrop, sets
/// <see cref="InternalItemsQuery.ImageTypesMatchAll"/>.
/// </summary>
public sealed class BaseItemRepositoryImageTypeFilterTests : SqliteDbTestFixture
{
    private const string MovieType = "MediaBrowser.Controller.Entities.Movies.Movie";

    private readonly BaseItemRepository _repository;

    private readonly Guid _logoAndBackdrop = Guid.NewGuid();
    private readonly Guid _logoOnly = Guid.NewGuid();
    private readonly Guid _backdropOnly = Guid.NewGuid();
    private readonly Guid _primaryOnly = Guid.NewGuid();

    public BaseItemRepositoryImageTypeFilterTests()
    {
        using (var ctx = CreateDbContext())
        {
            Seed(ctx);
        }

        _repository = CreateBaseItemRepository(new ItemTypeLookup());
    }

    [Fact]
    public void ImageTypes_MatchesAnyRequestedTypeByDefault()
    {
        var ids = Query(false, ImageType.Logo, ImageType.Backdrop);

        Assert.Equal(
            new[] { _logoAndBackdrop, _logoOnly, _backdropOnly }.Order(),
            ids.Order());
    }

    [Fact]
    public void ImageTypesMatchAll_RequiresEveryRequestedType()
    {
        var ids = Query(true, ImageType.Logo, ImageType.Backdrop);

        Assert.Equal([_logoAndBackdrop], ids);
    }

    [Fact]
    public void ImageTypesMatchAll_WithOneTypeBehavesLikeAnyMatch()
    {
        Assert.Equal(
            Query(false, ImageType.Logo).Order(),
            Query(true, ImageType.Logo).Order());
    }

    [Fact]
    public void ImageTypesMatchAll_ReturnsNothingWhenNoItemHasEveryType()
    {
        Assert.Empty(Query(true, ImageType.Logo, ImageType.Backdrop, ImageType.Primary));
    }

    private System.Collections.Generic.IReadOnlyList<Guid> Query(bool matchAll, params ImageType[] imageTypes)
        => _repository.GetItemIdsList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie],
            ImageTypes = imageTypes,
            ImageTypesMatchAll = matchAll
        });

    private void Seed(JellyfinDbContext context)
    {
        AddMovie(context, _logoAndBackdrop, "Logo and backdrop", ImageInfoImageType.Logo, ImageInfoImageType.Backdrop);
        AddMovie(context, _logoOnly, "Logo only", ImageInfoImageType.Logo);
        AddMovie(context, _backdropOnly, "Backdrop only", ImageInfoImageType.Backdrop);
        AddMovie(context, _primaryOnly, "Primary only", ImageInfoImageType.Primary);

        context.SaveChanges();
    }

    private void AddMovie(
        JellyfinDbContext context,
        Guid id,
        string name,
        params ImageInfoImageType[] imageTypes)
    {
        context.BaseItems.Add(new BaseItemEntity { Id = id, Type = MovieType, Name = name });

        foreach (var imageType in imageTypes)
        {
            context.BaseItemImageInfos.Add(new BaseItemImageInfo
            {
                Id = Guid.NewGuid(),
                ItemId = id,
                Item = null!,
                Path = $"/{name}/{imageType}.png",
                ImageType = imageType
            });
        }
    }
}
