using MediaBrowser.Providers.Plugins.Tmdb;
using Xunit;

namespace Jellyfin.Providers.Tests.Tmdb;

public class TmdbGenreVocabularyTests
{
    [Fact]
    public void Build_TranslationsOfOneGenre_AllMapToItsId()
    {
        var map = TmdbGenreVocabulary.Build([
            new TmdbGenreName("Adventure", 12),
            new TmdbGenreName("Abenteuer", 12),
            new TmdbGenreName("Aventure", 12)
        ]);

        Assert.Equal(12, map["adventure"]);
        Assert.Equal(12, map["abenteuer"]);
        Assert.Equal(12, map["aventure"]);
    }

    [Fact]
    public void Build_KeyedOnTheCleanName()
    {
        var map = TmdbGenreVocabulary.Build([new TmdbGenreName("Science Fiction", 878)]);

        Assert.Equal(878, map["science fiction"]);
        Assert.False(map.ContainsKey("Science Fiction"));
    }

    [Fact]
    public void Build_TheMovieAndSeriesListsDisagreeing_KeepsBothNames()
    {
        // The two lists share an id space but not their vocabulary, and these are genuinely different.
        var map = TmdbGenreVocabulary.Build([
            new TmdbGenreName("Science Fiction", 878),
            new TmdbGenreName("Sci-Fi & Fantasy", 10765),
            new TmdbGenreName("War", 10752),
            new TmdbGenreName("War & Politics", 10768)
        ]);

        Assert.Equal(878, map["science fiction"]);
        Assert.Equal(10765, map["sci fi fantasy"]);
        Assert.Equal(10752, map["war"]);
        Assert.Equal(10768, map["war politics"]);
    }

    [Fact]
    public void Build_OneNameForTwoIds_DropsIt()
    {
        // A translation that fuses two genres would otherwise merge them and delete one's artwork.
        var map = TmdbGenreVocabulary.Build([
            new TmdbGenreName("Action", 28),
            new TmdbGenreName("Action", 10759),
            new TmdbGenreName("Drama", 18)
        ]);

        Assert.False(map.ContainsKey("action"));
        Assert.Equal(18, map["drama"]);
    }

    [Fact]
    public void Build_ANameSeenAgainAfterTheClash_StaysDropped()
    {
        var map = TmdbGenreVocabulary.Build([
            new TmdbGenreName("Action", 28),
            new TmdbGenreName("Action", 10759),
            new TmdbGenreName("Action", 28)
        ]);

        Assert.Empty(map);
    }

    [Fact]
    public void Build_NamesAndIdsItCannotUse_AreSkipped()
    {
        var map = TmdbGenreVocabulary.Build([
            new TmdbGenreName(string.Empty, 28),
            new TmdbGenreName("   ", 28),
            new TmdbGenreName("-", 28),
            new TmdbGenreName("Drama", 0),
            new TmdbGenreName("Drama", 18)
        ]);

        Assert.Equal(18, Assert.Single(map).Value);
    }
}
