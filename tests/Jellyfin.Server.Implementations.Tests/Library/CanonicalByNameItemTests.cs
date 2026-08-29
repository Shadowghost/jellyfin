using System;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using Xunit;
using LibraryManagerImpl = Emby.Server.Implementations.Library.LibraryManager;

namespace Jellyfin.Server.Implementations.Tests.Library;

/// <summary>
/// Covers the one item every writer files a credit under, so the releases crediting an artist cannot
/// split across the several items one artist name can hold.
/// </summary>
public class CanonicalByNameItemTests
{
    [Fact]
    public void IsCreditTarget_TheByNameEntry_Qualifies()
    {
        Assert.True(LibraryManagerImpl.IsCreditTarget(Artist("by-name", scanned: false)));
    }

    [Fact]
    public void IsCreditTarget_AScannedFolder_DoesNot()
    {
        // A tag names an artist, not a directory.
        Assert.False(LibraryManagerImpl.IsCreditTarget(Artist("scanned", scanned: true)));
    }

    [Fact]
    public void IsCreditTarget_ATypeWithNoFolderForm_AlwaysQualifies()
    {
        Assert.True(LibraryManagerImpl.IsCreditTarget(new Person { Name = "Miles Davis", Id = Guid.NewGuid() }));
        Assert.True(LibraryManagerImpl.IsCreditTarget(new Genre { Name = "Jazz", Id = Guid.NewGuid() }));
    }

    [Fact]
    public void PickCreditTarget_PrefersTheByNameEntryOverEveryScannedFolder()
    {
        // One artist, a folder per quality tree, and the by-name entry created after all of them.
        var flac = Artist("FLAC", scanned: true, created: new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc));
        var mp3 = Artist("MP3", scanned: true, created: new DateTime(2026, 5, 21, 0, 0, 0, DateTimeKind.Utc));
        var byName = Artist("by-name", scanned: false, created: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(byName, LibraryManagerImpl.PickCreditTarget([flac, mp3, byName]));
        Assert.Equal(byName, LibraryManagerImpl.PickCreditTarget([byName, flac, mp3]));
    }

    [Fact]
    public void PickCreditTarget_OnlyScannedFolders_PicksNothingSoTheEntryGetsCreated()
    {
        var flac = Artist("FLAC", scanned: true);
        var wav = Artist("WAV", scanned: true);

        Assert.Null(LibraryManagerImpl.PickCreditTarget([flac, wav]));
    }

    [Fact]
    public void PickCreditTarget_SeveralEntriesOfOneName_IsTotallyOrdered()
    {
        var created = new DateTime(2026, 5, 24, 0, 0, 0, DateTimeKind.Utc);
        var low = Artist("low", scanned: false, created: created, id: Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var high = Artist("high", scanned: false, created: created, id: Guid.Parse("22222222-2222-2222-2222-222222222222"));

        Assert.Equal(low, LibraryManagerImpl.PickCreditTarget([high, low]));
        Assert.Equal(low, LibraryManagerImpl.PickCreditTarget([low, high]));
    }

    [Fact]
    public void PickCreditTarget_NothingToPickFrom_IsNull()
    {
        Assert.Null(LibraryManagerImpl.PickCreditTarget([]));
    }

    // A MusicArtist reports IsAccessedByName off its ParentId: a scanned folder has a parent, the
    // by-name entry under the metadata path has none.
    private static MusicArtist Artist(string name, bool scanned, DateTime created = default, Guid? id = null)
    {
        var artist = new MusicArtist
        {
            Name = name,
            Id = id ?? Guid.NewGuid(),
            DateCreated = created
        };

        if (scanned)
        {
            artist.ParentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        }

        return artist;
    }
}
