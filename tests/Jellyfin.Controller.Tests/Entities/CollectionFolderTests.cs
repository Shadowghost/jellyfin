using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Xunit;

namespace Jellyfin.Controller.Tests.Entities;

[Collection("LibraryManagerTests")]
public class CollectionFolderTests
{
    [Fact]
    public void UpdateFromResolvedItem_CollectionTypeChanged_AdoptsResolvedType()
    {
        var stored = new CollectionFolder { Path = "/libraries/movies", CollectionType = CollectionType.homevideos };
        var resolved = new CollectionFolder { Path = "/libraries/movies", CollectionType = CollectionType.movies };

        var updateType = stored.UpdateFromResolvedItem(resolved);

        Assert.Equal(CollectionType.movies, stored.CollectionType);
        Assert.True(updateType >= ItemUpdateType.MetadataImport);
        Assert.True(stored.RequiresRefresh());
    }

    [Fact]
    public void UpdateFromResolvedItem_CollectionTypeMissing_AdoptsResolvedType()
    {
        var stored = new CollectionFolder { Path = "/libraries/movies", CollectionType = null };
        var resolved = new CollectionFolder { Path = "/libraries/movies", CollectionType = CollectionType.movies };

        var updateType = stored.UpdateFromResolvedItem(resolved);

        Assert.Equal(CollectionType.movies, stored.CollectionType);
        Assert.True(updateType >= ItemUpdateType.MetadataImport);
    }

    [Fact]
    public void UpdateFromResolvedItem_ResolvedTypeMissing_KeepsKnownType()
    {
        var stored = new CollectionFolder { Path = "/libraries/movies", CollectionType = CollectionType.movies };
        var resolved = new CollectionFolder { Path = "/libraries/movies", CollectionType = null };

        var updateType = stored.UpdateFromResolvedItem(resolved);

        Assert.Equal(CollectionType.movies, stored.CollectionType);
        Assert.Equal(ItemUpdateType.None, updateType);
    }

    [Fact]
    public void UpdateFromResolvedItem_SameCollectionType_ReportsNoUpdate()
    {
        var stored = new CollectionFolder { Path = "/libraries/movies", CollectionType = CollectionType.movies };
        var resolved = new CollectionFolder { Path = "/libraries/movies", CollectionType = CollectionType.movies };

        var updateType = stored.UpdateFromResolvedItem(resolved);

        Assert.Equal(CollectionType.movies, stored.CollectionType);
        Assert.Equal(ItemUpdateType.None, updateType);
    }
}
