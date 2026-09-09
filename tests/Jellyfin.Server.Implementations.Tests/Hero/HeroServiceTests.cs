using Emby.Server.Implementations.Hero;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Hero;

public static class HeroServiceTests
{
    [Fact]
    public static void SelectRemoteTrailer_NoTrailers_ReturnsNull()
    {
        Assert.Null(HeroService.SelectRemoteTrailer([]));
    }

    [Fact]
    public static void SelectRemoteTrailer_UnknownHost_ReturnsNull()
    {
        MediaUrl[] trailers =
        [
            new() { Url = "https://vimeo.com/12345", Name = "Official Trailer" }
        ];

        Assert.Null(HeroService.SelectRemoteTrailer(trailers));
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ&t=42")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://m.youtube.com/embed/dQw4w9WgXcQ?rel=0")]
    public static void SelectRemoteTrailer_ExtractsVideoIdFromAnyYouTubeUrlShape(string url)
    {
        MediaUrl[] trailers = [new() { Url = url, Name = "Trailer" }];

        var trailer = HeroService.SelectRemoteTrailer(trailers);

        Assert.NotNull(trailer);
        Assert.Equal(HeroTrailerKind.Remote, trailer.Kind);
        Assert.Equal("YouTube", trailer.Provider);
        Assert.Equal("dQw4w9WgXcQ", trailer.ProviderId);
    }

    [Fact]
    public static void SelectRemoteTrailer_PrefersOfficialTrailerOverProviderOrder()
    {
        MediaUrl[] trailers =
        [
            new() { Url = "https://www.youtube.com/watch?v=teaser00000", Name = "Teaser" },
            new() { Url = "https://www.youtube.com/watch?v=official000", Name = "Official Trailer" },
            new() { Url = "https://www.youtube.com/watch?v=clip0000000", Name = "Opening Scene" }
        ];

        Assert.Equal("official000", HeroService.SelectRemoteTrailer(trailers)?.ProviderId);
    }

    [Fact]
    public static void SelectRemoteTrailer_DemotesAlternateCutWithinItsTier()
    {
        MediaUrl[] trailers =
        [
            new() { Url = "https://www.youtube.com/watch?v=signlanguag", Name = "Official Trailer (Sign Language)" },
            new() { Url = "https://www.youtube.com/watch?v=ordinary000", Name = "Official Trailer" }
        ];

        Assert.Equal("ordinary000", HeroService.SelectRemoteTrailer(trailers)?.ProviderId);
    }

    [Fact]
    public static void SelectRemoteTrailer_AlternateCutStillBeatsLowerTier()
    {
        MediaUrl[] trailers =
        [
            new() { Url = "https://www.youtube.com/watch?v=teaser00000", Name = "Teaser" },
            new() { Url = "https://www.youtube.com/watch?v=described00", Name = "Official Trailer (Audio Description)" }
        ];

        Assert.Equal("described00", HeroService.SelectRemoteTrailer(trailers)?.ProviderId);
    }

    [Fact]
    public static void SelectRemoteTrailer_SkipsUnusableEntriesInsteadOfLosingTheTrailer()
    {
        MediaUrl[] trailers =
        [
            new() { Url = "not a url", Name = "Official Trailer" },
            new() { Url = "https://www.youtube.com/watch?v=fallback000", Name = "Teaser" }
        ];

        Assert.Equal("fallback000", HeroService.SelectRemoteTrailer(trailers)?.ProviderId);
    }

    [Fact]
    public static void SelectRemoteTrailer_KeepsFirstOfEquallyRankedEntries()
    {
        MediaUrl[] trailers =
        [
            new() { Url = "https://www.youtube.com/watch?v=first000000", Name = "Trailer" },
            new() { Url = "https://www.youtube.com/watch?v=second00000", Name = "Trailer" }
        ];

        Assert.Equal("first000000", HeroService.SelectRemoteTrailer(trailers)?.ProviderId);
    }
}
