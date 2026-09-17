using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Server.Migrations.Stages;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Server.Migrations.Routines;

/// <summary>
/// Keeps the hardware decoding capability settings of existing servers under manual control.
/// </summary>
/// <remarks>
/// New installations start in <see cref="HardwareTuningMode.Auto"/> and let the detected hardware
/// decide. Existing servers have a hand maintained codec list that is almost always narrower than
/// what the hardware can do, so switching them over would enable decoders they never asked for.
/// <para>
/// This relies on <c>RunMigrationOnSetup</c> staying false: a fresh install records this migration
/// as applied without running it, which is what leaves new servers on automatic tuning.
/// </para>
/// </remarks>
[JellyfinMigration("2026-09-18T12:00:00", nameof(GrandfatherHardwareTuning), Stage = JellyfinMigrationStageTypes.AppInitialisation)]
internal class GrandfatherHardwareTuning : IAsyncMigrationRoutine
{
    private readonly IConfigurationManager _configurationManager;
    private readonly ILogger<GrandfatherHardwareTuning> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrandfatherHardwareTuning"/> class.
    /// </summary>
    /// <param name="configurationManager">The configuration manager.</param>
    /// <param name="logger">The logger.</param>
    public GrandfatherHardwareTuning(IConfigurationManager configurationManager, ILogger<GrandfatherHardwareTuning> logger)
    {
        _configurationManager = configurationManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task PerformAsync(CancellationToken cancellationToken)
    {
        var options = _configurationManager.GetEncodingOptions();
        if (options.HardwareTuning == HardwareTuningMode.Manual)
        {
            return Task.CompletedTask;
        }

        _logger.LogInformation(
            "Keeping the hardware decoding settings of this server under manual control; switch to automatic tuning to use everything the hardware reports");

        options.HardwareTuning = HardwareTuningMode.Manual;
        _configurationManager.SaveConfiguration("encoding", options);

        return Task.CompletedTask;
    }
}
