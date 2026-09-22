using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Server.Migrations.Stages;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Server.Migrations.Routines;

/// <summary>
/// Enables metadata based keyframe extraction for the mp4 family of containers.
/// </summary>
[JellyfinMigration("2026-09-22T12:00:00", nameof(AllowMp4KeyframeExtraction), Stage = JellyfinMigrationStageTypes.AppInitialisation)]
internal class AllowMp4KeyframeExtraction : IAsyncMigrationRoutine
{
    private static readonly string[] _addedExtensions = ["mp4", "m4v", "mov"];

    private readonly IConfigurationManager _configurationManager;
    private readonly ILogger<AllowMp4KeyframeExtraction> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AllowMp4KeyframeExtraction"/> class.
    /// </summary>
    /// <param name="configurationManager">The configuration manager.</param>
    /// <param name="logger">The logger.</param>
    public AllowMp4KeyframeExtraction(IConfigurationManager configurationManager, ILogger<AllowMp4KeyframeExtraction> logger)
    {
        _configurationManager = configurationManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task PerformAsync(CancellationToken cancellationToken)
    {
        // Reading the sample tables of an mp4 only costs a few seeks, and without them the segment
        // boundaries of a remuxed stream are guessed, which makes clients replay the last GOP.
        var encoding = _configurationManager.GetEncodingOptions();
        var allowed = encoding.AllowOnDemandMetadataBasedKeyframeExtractionForExtensions ?? [];
        var missing = _addedExtensions
            .Where(extension => !allowed.Any(existing => string.Equals(existing.TrimStart('.'), extension, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (missing.Length == 0)
        {
            return Task.CompletedTask;
        }

        encoding.AllowOnDemandMetadataBasedKeyframeExtractionForExtensions = [.. allowed, .. missing];
        _configurationManager.SaveConfiguration("encoding", encoding);
        _logger.LogInformation("Enabled metadata based keyframe extraction for {Extensions}", missing);

        return Task.CompletedTask;
    }
}
