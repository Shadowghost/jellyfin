using System;
using System.IO;
using System.Text.Json;
using Jellyfin.Extensions.Json;
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
    private const string DevicePath = "/dev/dri/renderD128";
    private const string EncoderVersion = "8.1.2";

    private static readonly EncodingOptions _options = new()
    {
        HardwareAccelerationType = HardwareAccelerationType.vaapi,
        VaapiDevice = DevicePath
    };

    private readonly string _cachePath = Path.Join(Path.GetTempPath(), "jf-hwcaps-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Queries_WithoutReport_KeepStaticBehaviour()
    {
        var provider = CreateProvider();

        Assert.False(provider.IsPopulated);
        Assert.Null(provider.GetCapabilities(HardwareAccelerationType.vaapi));
        Assert.True(provider.CanDecode(HardwareAccelerationType.vaapi, new EncodingOptions(), "hevc", 3840, 2160, "p010le", "Main 10"));
        Assert.True(provider.CanEncode(HardwareAccelerationType.vaapi, new EncodingOptions(), "h264", 1920, 1080, "nv12", "High"));
        Assert.True(provider.CanFilter(HardwareAccelerationType.vaapi, new EncodingOptions(), HwVppKind.Scale, 1920, 1080, "nv12"));
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

        Assert.False(provider.CanDecode(HardwareAccelerationType.nvenc, options, "hevc", 3840, 2160, null, null));
        Assert.True(provider.CanDecode(HardwareAccelerationType.nvenc, options, "hevc", 1920, 1080, null, null));
    }

    [Theory]

    // The report spells profiles as ffprobe does, and VA-API drivers put the codec in front of them.
    [InlineData("Main 10", true)]
    [InlineData("main10", true)]
    [InlineData("Main", true)]
    [InlineData("Rext", false)]
    [InlineData(null, true)]
    public void CanDecode_ChecksTheReportedProfile(string? profile, bool expected)
    {
        var provider = CreateProviderWith(new HwDeviceCapabilities
        {
            DrmPath = DevicePath,
            Decoders =
            [
                new HwCodecCapabilities
                {
                    CodecName = "hevc",
                    Profiles = ["HEVCMain", "HEVCMain10"],
                    PixelFormats = ["nv12", "p010le"]
                }
            ]
        });

        Assert.Equal(expected, provider.CanDecode(HardwareAccelerationType.vaapi, _options, "hevc", 1920, 1080, "p010le", profile));
    }

    [Fact]
    public void CanEncode_ChecksTheReportedPixelFormatAndProfile()
    {
        var provider = CreateProviderWith(new HwDeviceCapabilities
        {
            DrmPath = DevicePath,
            Encoders =
            [
                new HwCodecCapabilities
                {
                    CodecName = "h264",
                    MaxWidth = 4096,
                    MaxHeight = 4096,
                    Profiles = ["Main", "High"],
                    PixelFormats = ["nv12"]
                }
            ]
        });

        Assert.True(provider.CanEncode(HardwareAccelerationType.vaapi, _options, "h264", 1920, 1080, "nv12", "High"));
        Assert.False(provider.CanEncode(HardwareAccelerationType.vaapi, _options, "h264", 1920, 1080, "p010le", "High"));
        Assert.False(provider.CanEncode(HardwareAccelerationType.vaapi, _options, "h264", 1920, 1080, "nv12", "High 4:2:2 10"));
        Assert.False(provider.CanEncode(HardwareAccelerationType.vaapi, _options, "hevc", 1920, 1080, "nv12", "Main"));
    }

    [Fact]
    public void CanFilter_ChecksTheReportedPixelFormat()
    {
        var provider = CreateProviderWith(new HwDeviceCapabilities
        {
            DrmPath = DevicePath,
            Filters = [new HwFilterCapabilities { Kind = HwVppKind.Scale, PixelFormats = ["nv12"] }]
        });

        Assert.True(provider.CanFilter(HardwareAccelerationType.vaapi, _options, HwVppKind.Scale, 1920, 1080, "nv12"));
        Assert.True(provider.CanFilter(HardwareAccelerationType.vaapi, _options, HwVppKind.Scale, 1920, 1080, null));
        Assert.False(provider.CanFilter(HardwareAccelerationType.vaapi, _options, HwVppKind.Scale, 1920, 1080, "p010le"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_cachePath))
        {
            Directory.Delete(_cachePath, true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Builds a provider that has already read a report for the device, by way of its own cache.
    /// </summary>
    /// <param name="device">The device the report describes.</param>
    /// <returns>The populated provider.</returns>
    private HardwareCapabilitiesProvider CreateProviderWith(HwDeviceCapabilities device)
    {
        var entry = new HwCapsCacheEntry
        {
            CacheVersion = 1,
            EncoderVersion = EncoderVersion,
            DeviceKey = DevicePath,
            Capabilities = new HwAccelCapabilities
            {
                AccelerationType = HardwareAccelerationType.vaapi,
                EncoderVersion = EncoderVersion,
                IsReported = true,
                Devices = [device]
            }
        };

        Directory.CreateDirectory(Path.Join(_cachePath, "hwcaps"));
        File.WriteAllText(
            Path.Join(_cachePath, "hwcaps", "vaapi.json"),
            JsonSerializer.Serialize(entry, JsonDefaults.Options));

        var provider = CreateProvider();
        provider.Refresh("ffmpeg", "ffprobe", EncoderVersion, false, _options);

        Assert.True(provider.IsPopulated);

        return provider;
    }

    private HardwareCapabilitiesProvider CreateProvider()
    {
        var paths = new Mock<IApplicationPaths>();
        paths.SetupGet(p => p.CachePath).Returns(_cachePath);

        return new HardwareCapabilitiesProvider(NullLogger<HardwareCapabilitiesProvider>.Instance, paths.Object);
    }
}
