using System;
using System.IO;
using MediaBrowser.Common.Configuration;
using MediaBrowser.MediaEncoding.Encoder;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaEncoding.Hardware;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.MediaEncoding.Tests.Encoder;

public sealed class HardwareCapabilitiesProviderTests : IDisposable
{
    private readonly string _cachePath = Path.Join(Path.GetTempPath(), "jf-hwcaps-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Queries_WithoutReport_KeepStaticBehaviour()
    {
        var provider = CreateProvider();

        Assert.False(provider.IsPopulated);
        Assert.Null(provider.GetCapabilities(HardwareAccelerationType.vaapi));
        Assert.True(provider.CanDecode(HardwareAccelerationType.vaapi, new EncodingOptions(), "hevc", 3840, 2160, "p010le"));
        Assert.True(provider.CanEncode(HardwareAccelerationType.vaapi, new EncodingOptions(), "h264", 1920, 1080));
        Assert.True(provider.CanFilter(HardwareAccelerationType.vaapi, new EncodingOptions(), HwVppKind.Scale, 1920, 1080));
    }

    [Fact]
    public void Refresh_DetectionDisabled_CollectsNothing()
    {
        var provider = CreateProvider();
        var options = new EncodingOptions
        {
            HardwareAccelerationType = HardwareAccelerationType.nvenc,
            HardwareCapabilityDetection = HardwareCapabilityDetectionMode.Disabled
        };

        provider.Refresh("ffmpeg", "ffprobe", "8.1.2", true, options);

        Assert.False(provider.IsPopulated);
    }

    [Fact]
    public void RecordDecodeFailure_OnlyDisablesTheMatchingResolutionTier()
    {
        var provider = CreateProvider();

        provider.RecordDecodeFailure(HardwareAccelerationType.nvenc, "hevc", 3840, 2160);

        Assert.True(provider.IsDecodeKnownBad(HardwareAccelerationType.nvenc, "hevc", 3840, 2160));
        Assert.False(provider.IsDecodeKnownBad(HardwareAccelerationType.nvenc, "hevc", 1920, 1080));
        Assert.False(provider.IsDecodeKnownBad(HardwareAccelerationType.nvenc, "h264", 3840, 2160));
        Assert.False(provider.IsDecodeKnownBad(HardwareAccelerationType.vaapi, "hevc", 3840, 2160));
    }

    [Fact]
    public void RecordDecodeFailure_IsHonouredByCanDecode()
    {
        var provider = CreateProvider();
        var options = new EncodingOptions { HardwareAccelerationType = HardwareAccelerationType.nvenc };

        provider.RecordDecodeFailure(HardwareAccelerationType.nvenc, "hevc", 3840, 2160);

        Assert.False(provider.CanDecode(HardwareAccelerationType.nvenc, options, "hevc", 3840, 2160, null));
        Assert.True(provider.CanDecode(HardwareAccelerationType.nvenc, options, "hevc", 1920, 1080, null));
    }

    public void Dispose()
    {
        if (Directory.Exists(_cachePath))
        {
            Directory.Delete(_cachePath, true);
        }

        GC.SuppressFinalize(this);
    }

    private HardwareCapabilitiesProvider CreateProvider()
    {
        var paths = new Mock<IApplicationPaths>();
        paths.SetupGet(p => p.CachePath).Returns(_cachePath);

        return new HardwareCapabilitiesProvider(NullLogger<HardwareCapabilitiesProvider>.Instance, paths.Object);
    }
}
