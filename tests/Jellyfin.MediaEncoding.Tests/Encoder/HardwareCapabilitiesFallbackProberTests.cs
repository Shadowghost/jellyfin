using System.Threading;
using MediaBrowser.MediaEncoding.Encoder;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.MediaEncoding.Tests.Encoder;

public class HardwareCapabilitiesFallbackProberTests
{
    [Theory]
    [InlineData(HardwareAccelerationType.none)]
    [InlineData(HardwareAccelerationType.v4l2m2m)]
    public void Probe_TypeWithoutHwDevice_ReturnsNull(HardwareAccelerationType type)
    {
        var prober = new HardwareCapabilitiesFallbackProber(NullLogger.Instance, "ffmpeg");
        var options = new EncodingOptions { HardwareAccelerationType = type };

        Assert.Null(prober.Probe(options, "8.1.2", CancellationToken.None));
    }

    [Fact]
    public void Probe_MissingEncoder_ReturnsNull()
    {
        var prober = new HardwareCapabilitiesFallbackProber(NullLogger.Instance, "/nonexistent/ffmpeg");
        var options = new EncodingOptions { HardwareAccelerationType = HardwareAccelerationType.nvenc };

        Assert.Null(prober.Probe(options, "8.1.2", CancellationToken.None));
    }

    [Fact]
    public void Probe_CancelledUpFront_ReturnsNull()
    {
        var prober = new HardwareCapabilitiesFallbackProber(NullLogger.Instance, "ffmpeg");
        var options = new EncodingOptions { HardwareAccelerationType = HardwareAccelerationType.vaapi };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Null(prober.Probe(options, "8.1.2", cancellation.Token));
    }
}
