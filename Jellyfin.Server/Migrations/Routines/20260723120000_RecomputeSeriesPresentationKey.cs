using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Server.ServerSetupApp;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Server.Migrations.Routines;

/// <summary>
/// Recomputes the presentation unique key for every series so existing items adopt the folder-set-free key format.
/// </summary>
[JellyfinMigration("2026-07-23T12:00:00", nameof(RecomputeSeriesPresentationKey))]
[JellyfinMigrationBackup(JellyfinDb = true)]
internal class RecomputeSeriesPresentationKey : IAsyncMigrationRoutine
{
    private readonly IStartupLogger<RecomputeSeriesPresentationKey> _logger;
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecomputeSeriesPresentationKey"/> class.
    /// </summary>
    /// <param name="logger">The startup logger.</param>
    /// <param name="libraryManager">The library manager.</param>
    public RecomputeSeriesPresentationKey(
        IStartupLogger<RecomputeSeriesPresentationKey> logger,
        ILibraryManager libraryManager)
    {
        _logger = logger;
        _libraryManager = libraryManager;
    }

    /// <inheritdoc />
    public async Task PerformAsync(CancellationToken cancellationToken)
    {
        var series = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Series }
        }).OfType<Series>().ToArray();

        _logger.LogInformation("Recomputing presentation unique key for {Count} series", series.Length);

        const int ProgressInterval = 500;
        var sw = Stopwatch.StartNew();
        var processed = 0;
        var updated = 0;
        foreach (var item in series)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (++processed % ProgressInterval == 0)
            {
                _logger.LogInformation("Processed {Processed}/{Total} series - Updated: {Updated} - Time: {Elapsed}", processed, series.Length, updated, sw.Elapsed);
            }

            var oldKey = item.PresentationUniqueKey;
            var newKey = item.CreatePresentationUniqueKey();
            if (string.Equals(oldKey, newKey, StringComparison.Ordinal))
            {
                continue;
            }

            item.PresentationUniqueKey = newKey;
            await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataImport, cancellationToken).ConfigureAwait(false);

            // Seasons and episodes cache the series key in SeriesPresentationUniqueKey and are matched
            // to the series by it. Look them up by the old key (they still carry it) and re-point
            // them at the new key so they stay attached without waiting for the next scan.
            if (!string.IsNullOrEmpty(oldKey))
            {
                var children = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    SeriesPresentationUniqueKey = oldKey,
                    IncludeItemTypes = [BaseItemKind.Season, BaseItemKind.Episode]
                });

                foreach (var child in children)
                {
                    if (child is IHasSeries hasSeries
                        && !string.Equals(hasSeries.SeriesPresentationUniqueKey, newKey, StringComparison.Ordinal))
                    {
                        hasSeries.SeriesPresentationUniqueKey = newKey;
                        await child.UpdateToRepositoryAsync(ItemUpdateType.MetadataImport, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            updated++;
        }

        _logger.LogInformation("Recomputed presentation unique key for {Updated} of {Count} series in {Elapsed}", updated, series.Length, sw.Elapsed);
    }
}
