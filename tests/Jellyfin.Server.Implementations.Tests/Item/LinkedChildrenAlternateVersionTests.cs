using System;
using System.Linq;
using Emby.Server.Implementations.Data;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Server.Implementations.Item;
using MediaBrowser.Controller.Persistence;
using Moq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Item;

/// <summary>
/// Covers which link types <see cref="LinkedChildrenService.GetItemIdsWithAlternateVersions"/> counts as
/// a second media source. DtoService treats an item missing from that set as having a single source, so a
/// type left out here silently drops the version count from every batched item query.
/// </summary>
public sealed class LinkedChildrenAlternateVersionTests : SqliteDbTestFixture
{
    private const string MovieType = "MediaBrowser.Controller.Entities.Movies.Movie";

    private readonly LinkedChildrenService _service;

    public LinkedChildrenAlternateVersionTests()
    {
        _service = new LinkedChildrenService(
            CreateDbContextFactory(),
            new ItemTypeLookup(),
            new Mock<IItemQueryHelpers>().Object);
    }

    [Theory]
    [InlineData(LinkedChildType.LocalAlternateVersion, true)]
    [InlineData(LinkedChildType.LinkedAlternateVersion, true)]
    [InlineData(LinkedChildType.AutoLinkedAlternateVersion, true)]
    [InlineData(LinkedChildType.ExcludedAlternateVersion, false)]
    [InlineData(LinkedChildType.Manual, false)]
    [InlineData(LinkedChildType.Shortcut, false)]
    public void GetItemIdsWithAlternateVersions_CountsOnlyVersionLinks(LinkedChildType linkType, bool expectedToCount)
    {
        var primary = Guid.NewGuid();
        var alternate = Guid.NewGuid();

        using (var ctx = CreateDbContext())
        {
            ctx.BaseItems.Add(new BaseItemEntity { Id = primary, Type = MovieType, Name = "Movie" });
            ctx.BaseItems.Add(new BaseItemEntity
            {
                Id = alternate,
                Type = MovieType,
                Name = "Movie 4K",
                // An exclusion records a split, so that partner is not part of the version group.
                PrimaryVersionId = linkType == LinkedChildType.ExcludedAlternateVersion ? null : primary
            });
            ctx.LinkedChildren.Add(new LinkedChildEntity
            {
                ParentId = primary,
                ChildId = alternate,
                ChildType = linkType,
                SortOrder = 0
            });
            ctx.SaveChanges();
        }

        var withVersions = _service.GetItemIdsWithAlternateVersions([primary]);

        Assert.Equal(expectedToCount, withVersions.Contains(primary));
    }
}
