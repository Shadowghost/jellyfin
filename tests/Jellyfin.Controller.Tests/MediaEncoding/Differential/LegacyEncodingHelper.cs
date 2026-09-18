using System;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.MediaEncoding;
using Moq;

using IConfiguration = Microsoft.Extensions.Configuration.IConfiguration;

namespace Jellyfin.Controller.Tests.MediaEncoding.Differential;

/// <summary>
/// Builds the helper over an ffmpeg that has everything, so the hardware chains are reached.
/// </summary>
internal static class LegacyEncodingHelper
{
    public static EncodingHelper Create(bool bitStreamFilterOptions = true)
    {
        var mediaEncoder = new Mock<IMediaEncoder>();
        mediaEncoder.SetupGet(e => e.EncoderVersion).Returns(new Version(8, 1, 2));
        mediaEncoder.Setup(e => e.SupportsEncoder(It.IsAny<string>())).Returns(true);
        mediaEncoder.Setup(e => e.SupportsDecoder(It.IsAny<string>())).Returns(true);
        mediaEncoder.Setup(e => e.SupportsHwaccel(It.IsAny<string>())).Returns(true);
        mediaEncoder.Setup(e => e.SupportsFilter(It.IsAny<string>())).Returns(true);
        mediaEncoder.Setup(e => e.SupportsFilterWithOption(It.IsAny<FilterOptionType>())).Returns(true);
        mediaEncoder
            .Setup(e => e.SupportsBitStreamFilterWithOption(It.IsAny<BitStreamFilterOptionType>()))
            .Returns(bitStreamFilterOptions);

        return new EncodingHelper(
            Mock.Of<IApplicationPaths>(),
            mediaEncoder.Object,
            Mock.Of<ISubtitleEncoder>(),
            Mock.Of<IConfiguration>(),
            Mock.Of<IConfigurationManager>(),
            Mock.Of<IPathManager>(),
            HardwareCapabilitiesMock.CreatePermissive());
    }
}
