using MediaBrowser.MediaEncoding.Probing;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.MediaEncoding.Tests.Probing;

public class FramePackingDetectionTests
{
    [Theory]
    // A frame that displays at a normal aspect squeezed both views into it.
    [InlineData("side by side", 1920, 1080, "16:9", Video3DFormat.HalfSideBySide)]
    [InlineData("side by side", 1280, 720, "16:9", Video3DFormat.HalfSideBySide)]
    [InlineData("side by side", 720, 576, "4:3", Video3DFormat.HalfSideBySide)]
    // A frame that displays about twice as wide carries both views at full size.
    [InlineData("side by side", 3840, 1080, "32:9", Video3DFormat.FullSideBySide)]
    [InlineData("side by side", 1440, 576, "8:3", Video3DFormat.FullSideBySide)]
    // The same both ways round for top and bottom.
    [InlineData("top and bottom", 1920, 1080, "16:9", Video3DFormat.HalfTopAndBottom)]
    [InlineData("top and bottom", 1920, 2160, "8:9", Video3DFormat.FullTopAndBottom)]
    [InlineData("top and bottom", 720, 1152, "5:8", Video3DFormat.FullTopAndBottom)]
    public void ResolveFramePacking_TellsSqueezedViewsFromFullOnes(
        string layout,
        int width,
        int height,
        string displayAspectRatio,
        Video3DFormat expected)
        => Assert.Equal(expected, ProbeResultNormalizer.ResolveFramePacking(layout, width, height, displayAspectRatio));

    [Fact]
    public void ResolveFramePacking_FallsBackToTheCodedAspectWithoutADisplayOne()
        => Assert.Equal(
            Video3DFormat.FullSideBySide,
            ProbeResultNormalizer.ResolveFramePacking("side by side", 3840, 1080, null));

    [Theory]
    [InlineData("")]
    [InlineData("2D")]
    [InlineData("frame alternate")]
    [InlineData("columns")]
    public void ResolveFramePacking_IgnoresLayoutsThatCannotBeFlattened(string layout)
        => Assert.Null(ProbeResultNormalizer.ResolveFramePacking(layout, 1920, 1080, "16:9"));

    [Fact]
    public void ResolveFramePacking_IgnoresASourceThatReportsNoLayout()
        => Assert.Null(ProbeResultNormalizer.ResolveFramePacking(null!, 1920, 1080, "16:9"));
}
