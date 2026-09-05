using System;
using System.IO;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Providers.Plugins.StudioImages;
using Moq;
using Xunit;

namespace Jellyfin.Providers.Tests.StudioImages;

public sealed class StudioArtworkManagerTests : IDisposable
{
    private readonly string _pluginsPath;
    private readonly string _artworkRoot;

    public StudioArtworkManagerTests()
    {
        _pluginsPath = Path.Combine(Path.GetTempPath(), "jf-studioimages-" + Guid.NewGuid().ToString("N"));
        var applicationPaths = new Mock<IApplicationPaths>();
        applicationPaths.SetupGet(p => p.PluginsPath).Returns(_pluginsPath);

        // Constructing the plugin is what pins StudioArtworkManager's data folder.
        _ = new Plugin(applicationPaths.Object, Mock.Of<IXmlSerializer>());

        _artworkRoot = Path.Combine(_pluginsPath, "Jellyfin.Plugin.StudioImages", "artwork");
        Directory.CreateDirectory(Path.Combine(_artworkRoot, "studios", "2", "20th-television"));
        File.WriteAllText(Path.Combine(_artworkRoot, "studios", "2", "20th-television", "thumb.webp"), "thumb");
        File.WriteAllText(Path.Combine(_artworkRoot, "placeholder-primary.webp"), "placeholder");
        File.WriteAllText(Path.Combine(_artworkRoot, "studios.json"), "[]");
        File.WriteAllText(Path.Combine(_pluginsPath, "Jellyfin.Plugin.StudioImages", "release.tag"), "tag");
    }

    public void Dispose()
    {
        if (Directory.Exists(_pluginsPath))
        {
            Directory.Delete(_pluginsPath, recursive: true);
        }
    }

    [Fact]
    public void ToApiPath_StudioImage_RoundTripsToTheSameFile()
    {
        Assert.True(StudioArtworkManager.TryGetStudioImagePath("20th-television", "thumb", out var diskPath));

        var apiPath = StudioArtworkManager.ToApiPath(diskPath!);
        Assert.Equal("/StudioImages/studios/2/20th-television/thumb.webp", apiPath);

        Assert.True(StudioArtworkManager.TryResolveArtworkFile(apiPath[StudioArtworkManager.ApiPathPrefix.Length..], out var resolved));
        Assert.Equal(diskPath, resolved);
    }

    [Fact]
    public void ToApiPath_Placeholder_RoundTripsToTheSameFile()
    {
        Assert.True(StudioArtworkManager.TryGetPlaceholderImagePath("primary", out var diskPath));

        var apiPath = StudioArtworkManager.ToApiPath(diskPath!);
        Assert.Equal("/StudioImages/placeholder-primary.webp", apiPath);

        Assert.True(StudioArtworkManager.TryResolveArtworkFile(apiPath[StudioArtworkManager.ApiPathPrefix.Length..], out var resolved));
        Assert.Equal(diskPath, resolved);
    }

    [Theory]
    // Nothing outside the bundle, however it is spelled.
    [InlineData("../release.tag")]
    [InlineData("studios/../../release.tag")]
    [InlineData("%2E%2E/release.tag")]
    [InlineData("/etc/passwd")]
    [InlineData("studios/2/20th-television/../../../../release.tag")]
    // Non-image files inside the bundle stay private.
    [InlineData("studios.json")]
    // Missing files, and the empty path.
    [InlineData("studios/2/20th-television/logo.webp")]
    [InlineData("")]
    public void TryResolveArtworkFile_RejectsPathsOutsideTheBundle(string relativePath)
    {
        Assert.False(StudioArtworkManager.TryResolveArtworkFile(relativePath, out var resolved));
        Assert.Null(resolved);
    }
}
