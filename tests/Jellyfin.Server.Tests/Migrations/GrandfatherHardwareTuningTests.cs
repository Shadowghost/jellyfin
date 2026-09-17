using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Server.Migrations.Routines;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Server.Tests.Migrations;

/// <summary>
/// Covers the upgrade rule: a server that already exists keeps the hardware decoding settings it has,
/// and only a fresh install, which never runs this migration, starts on automatic tuning.
/// </summary>
public class GrandfatherHardwareTuningTests
{
    [Fact]
    public async Task PerformAsync_ExistingServer_SwitchesToManualTuning()
    {
        var options = new EncodingOptions();
        var configurationManager = CreateConfigurationManager(options);

        Assert.Equal(HardwareTuningMode.Auto, options.HardwareTuning);

        await CreateMigration(configurationManager.Object).PerformAsync(CancellationToken.None);

        Assert.Equal(HardwareTuningMode.Manual, options.HardwareTuning);
        configurationManager.Verify(c => c.SaveConfiguration("encoding", options), Times.Once);
    }

    [Fact]
    public async Task PerformAsync_ExistingServer_LeavesTheCapabilitySettingsAlone()
    {
        var options = new EncodingOptions
        {
            HardwareDecodingCodecs = ["h264", "vc1"],
            EnableDecodingColorDepth10Hevc = false,
            EnableDecodingColorDepth10Vp9 = true
        };

        await CreateMigration(CreateConfigurationManager(options).Object).PerformAsync(CancellationToken.None);

        Assert.Equal(["h264", "vc1"], options.HardwareDecodingCodecs);
        Assert.False(options.EnableDecodingColorDepth10Hevc);
        Assert.True(options.EnableDecodingColorDepth10Vp9);
    }

    [Fact]
    public async Task PerformAsync_AlreadyManual_SavesNothing()
    {
        var options = new EncodingOptions { HardwareTuning = HardwareTuningMode.Manual };
        var configurationManager = CreateConfigurationManager(options);

        await CreateMigration(configurationManager.Object).PerformAsync(CancellationToken.None);

        configurationManager.Verify(c => c.SaveConfiguration(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
    }

    private static Mock<IConfigurationManager> CreateConfigurationManager(EncodingOptions options)
    {
        var configurationManager = new Mock<IConfigurationManager>();
        configurationManager.Setup(c => c.GetConfiguration("encoding")).Returns(options);

        return configurationManager;
    }

    private static GrandfatherHardwareTuning CreateMigration(IConfigurationManager configurationManager)
        => new(configurationManager, NullLogger<GrandfatherHardwareTuning>.Instance);
}
