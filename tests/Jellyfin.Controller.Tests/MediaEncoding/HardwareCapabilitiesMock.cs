using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaEncoding.Hardware;
using Moq;

namespace Jellyfin.Controller.Tests.MediaEncoding;

internal static class HardwareCapabilitiesMock
{
    /// <summary>
    /// Builds a provider that reports nothing, which is how an unprobed or third-party ffmpeg behaves.
    /// </summary>
    /// <returns>The permissive provider.</returns>
    public static IHardwareCapabilitiesProvider CreatePermissive()
    {
        var provider = new Mock<IHardwareCapabilitiesProvider>();
        provider
            .Setup(p => p.CanDecode(
                It.IsAny<HardwareAccelerationType>(),
                It.IsAny<EncodingOptions>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>()))
            .Returns(true);
        provider
            .Setup(p => p.CanEncode(
                It.IsAny<HardwareAccelerationType>(),
                It.IsAny<EncodingOptions>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>()))
            .Returns(true);
        provider
            .Setup(p => p.CanFilter(
                It.IsAny<HardwareAccelerationType>(),
                It.IsAny<EncodingOptions>(),
                It.IsAny<HwVppKind>(),
                It.IsAny<int>(),
                It.IsAny<int>()))
            .Returns(true);

        return provider.Object;
    }
}
