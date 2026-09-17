using System.IO;
using System.Linq;
using MediaBrowser.MediaEncoding.Probing.HwCaps;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaEncoding.Hardware;
using Xunit;

namespace Jellyfin.MediaEncoding.Tests.Probing
{
    public class HwCapsResultNormalizerTests
    {
        [Fact]
        public void Parse_EmptyDeviceList_ReturnsNoDevices()
        {
            var caps = HwCapsResultNormalizer.Parse("{ \"hwaccel_devices\": [] }", HardwareAccelerationType.vaapi, "8.1.2");

            Assert.NotNull(caps);
            Assert.True(caps.IsReported);
            Assert.Empty(caps.Devices);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("{ \"streams\": [] }")]
        public void Parse_InvalidInput_ReturnsNull(string? json)
        {
            Assert.Null(HwCapsResultNormalizer.Parse(json, HardwareAccelerationType.nvenc, null));
        }

        [Fact]
        public void Parse_CudaReport_ReadsDecodersAndEncoders()
        {
            var caps = HwCapsResultNormalizer.Parse(ReadFixture("cuda.json"), HardwareAccelerationType.nvenc, "8.1.2");

            Assert.NotNull(caps);
            var device = Assert.Single(caps.Devices);
            Assert.Equal(0, device.Index);
            Assert.Equal("NVIDIA T600", device.Name);

            var hevc = device.GetDecoder("hevc");
            Assert.NotNull(hevc);
            Assert.Contains("nv12", hevc.PixelFormats);
            Assert.True(hevc.MaxWidth > 0);

            var h264Encoder = device.GetEncoder("h264");
            Assert.NotNull(h264Encoder);
            Assert.Contains("High", h264Encoder.Profiles);

            // nvdec reports no fixed function video processing.
            Assert.Empty(device.Filters);
        }

        [Fact]
        public void Parse_CudaReport_AppliesDecoderSizeLimits()
        {
            var caps = HwCapsResultNormalizer.Parse(ReadFixture("cuda.json"), HardwareAccelerationType.nvenc, "8.1.2");
            var mpeg4 = caps!.Devices[0].GetDecoder("mpeg4");

            Assert.NotNull(mpeg4);
            Assert.True(mpeg4.SupportsSize(1920, 1080));
            Assert.False(mpeg4.SupportsSize(3840, 2160));
        }

        [Fact]
        public void Parse_VaapiReport_ReadsFilters()
        {
            var caps = HwCapsResultNormalizer.Parse(ReadFixture("vaapi.json"), HardwareAccelerationType.vaapi, "8.1.2");

            Assert.NotNull(caps);
            var device = Assert.Single(caps.Devices);
            Assert.Equal("/dev/dri/renderD128", device.DrmPath);
            Assert.Equal("radeonsi", device.DriverName);

            var scale = device.GetFilter(HwVppKind.Scale);
            Assert.NotNull(scale);
            Assert.Equal(4096, scale.MaxWidth);
            Assert.False(scale.SupportsSize(7680, 4320));

            var deinterlace = device.GetFilter(HwVppKind.Deinterlace);
            Assert.NotNull(deinterlace);
            Assert.True(deinterlace.Modes["deint_mode_bob"]);
            Assert.False(deinterlace.Modes["deint_mode_weave"]);

            // There is no tonemap entry in the fixture, so it must not be invented.
            Assert.Null(device.GetFilter(HwVppKind.Tonemap));
        }

        private static string ReadFixture(string name)
            => File.ReadAllText(Path.Join("Test Data", "HwCaps", name));
    }
}
