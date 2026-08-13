using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Emby.Server.Implementations.Library.Validators;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Library;

public class ByNameProviderIdMergeTests
{
    [Fact]
    public async Task MergeAsync_TwoNamesOneProviderIdentifies_MergesIntoTheOldest()
    {
        var keeper = Genre("Adventure", ("Tmdb", "12"));
        var duplicate = Genre("Abenteuer", ("Tmdb", "12"));
        var merger = new Mock<IItemMerger>();

        var merged = await ByNameProviderIdMerge.MergeAsync([keeper, duplicate], merger.Object, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(1, merged);
        merger.Verify(
            e => e.MergeAsync(keeper.Id, It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0].Equals(duplicate.Id)), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MergeAsync_IdsThatDisagree_AreLeftApart()
    {
        var merger = new Mock<IItemMerger>();

        var merged = await ByNameProviderIdMerge.MergeAsync(
            [Genre("Action", ("Tmdb", "28")), Genre("Action & Adventure", ("Tmdb", "10759"))],
            merger.Object,
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Equal(0, merged);
        merger.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MergeAsync_NoIdsAtAll_AreLeftApart()
    {
        var merger = new Mock<IItemMerger>();

        var merged = await ByNameProviderIdMerge.MergeAsync(
            [Genre("Adventure"), Genre("Abenteuer")],
            merger.Object,
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Equal(0, merged);
        merger.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MergeAsync_ThreeOfOneGenre_AreOneCall()
    {
        var keeper = Genre("Adventure", ("Tmdb", "12"));
        var merger = new Mock<IItemMerger>();

        var merged = await ByNameProviderIdMerge.MergeAsync(
            [keeper, Genre("Abenteuer", ("Tmdb", "12")), Genre("Aventure", ("Tmdb", "12"))],
            merger.Object,
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Equal(2, merged);
        merger.Verify(
            e => e.MergeAsync(keeper.Id, It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 2), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MergeAsync_AgreeingOnOneProviderAndDisagreeingOnAnother_StillMerges()
    {
        // A provider reissuing an id is likelier than two genres sharing a name and an id, which is the
        // same reading the save path takes.
        var keeper = Genre("Adventure", ("Tmdb", "12"), ("Imdb", "a"));
        var duplicate = Genre("Abenteuer", ("Tmdb", "12"), ("Imdb", "b"));
        var merger = new Mock<IItemMerger>();

        var merged = await ByNameProviderIdMerge.MergeAsync([keeper, duplicate], merger.Object, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(1, merged);
    }

    private static BaseItem Genre(string name, params (string Provider, string Value)[] providerIds)
    {
        var genre = new Genre { Id = Guid.NewGuid(), Name = name };
        foreach (var (provider, value) in providerIds)
        {
            genre.SetProviderId(provider, value);
        }

        return genre;
    }
}
