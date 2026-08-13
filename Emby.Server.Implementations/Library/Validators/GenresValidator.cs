using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.Library.Validators;

/// <summary>
/// Class GenresValidator.
/// </summary>
public class GenresValidator
{
    // The share that may look unused before the cleanup is read as a link problem instead. A library
    // that genuinely lost this many genres gets them on the next scan.
    private const double MaxDeadShare = 0.25;

    /// <summary>
    /// The library manager.
    /// </summary>
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<GenresValidator> _logger;

    private readonly IItemMerger _itemMerger;

    private readonly IFileSystem _fileSystem;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenresValidator"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="itemMerger">The item merger.</param>
    /// <param name="fileSystem">The file system.</param>
    public GenresValidator(ILibraryManager libraryManager, ILogger<GenresValidator> logger, IItemMerger itemMerger, IFileSystem fileSystem)
    {
        _libraryManager = libraryManager;
        _logger = logger;
        _itemMerger = itemMerger;
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Runs the specified progress.
    /// </summary>
    /// <param name="progress">The progress.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Task.</returns>
    public async Task Run(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var genres = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Genre]
        });

        var numComplete = 0;
        var count = genres.Count;
        var refreshed = 0;

        foreach (var item in genres)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Also the ones carrying no ids: a provider that can name them gives them one, and that
                // is what tells two names for one genre apart from two genres. Asked for in full,
                // because a provider that reports no file change is left out of any lighter refresh.
                if (item.DateLastRefreshed == default || item.ProviderIds.Count == 0)
                {
                    await item.RefreshMetadata(
                        new MetadataRefreshOptions(new DirectoryService(_fileSystem))
                        {
                            MetadataRefreshMode = MetadataRefreshMode.FullRefresh
                        },
                        cancellationToken).ConfigureAwait(false);
                    refreshed++;
                }
            }
            catch (OperationCanceledException)
            {
                // Don't clutter the log
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing {GenreName}", item.Name);
            }

            numComplete++;
            double percent = numComplete;
            percent /= count;
            percent *= 100;

            progress.Report(percent);
        }

        _logger.LogInformation("Refreshed metadata for {RefreshedCount} new genres out of {TotalCount} total", refreshed, count);

        var merged = await ByNameProviderIdMerge
            .MergeAsync(RefreshedInOrder(), _itemMerger, _logger, cancellationToken)
            .ConfigureAwait(false);
        if (merged > 0)
        {
            _logger.LogInformation("Merged {Count} genres that a provider gives the same id.", merged);
        }

        var deadEntities = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Genre, BaseItemKind.MusicGenre],
            IsDeadGenre = true,
            IsLocked = false
        });

        // A link table that lost its rows reads as genres being unused, and deleting one takes its
        // artwork with it. Past a small share of the library that is a link problem rather than a
        // genre problem, so the genres are left alone and the names logged for an admin to judge.
        var totalGenres = _libraryManager.GetCount(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Genre, BaseItemKind.MusicGenre],
            IsLocked = false
        });

        if (totalGenres > 0 && deadEntities.Count > totalGenres * MaxDeadShare)
        {
            _logger.LogWarning(
                "{DeadCount} of {TotalCount} genres look unused, which reads as missing links rather than unused genres. Skipping cleanup of {Genres}",
                deadEntities.Count,
                totalGenres,
                string.Join(", ", deadEntities.Select(e => e.Name)));
            progress.Report(100);
            return;
        }

        foreach (var item in deadEntities)
        {
            _logger.LogInformation("Deleting dead {ItemType} {ItemId} {ItemName}", item.GetType().Name, item.Id.ToString("N", CultureInfo.InvariantCulture), item.Name);
        }

        _libraryManager.DeleteItemsUnsafeFast(deadEntities, deleteSourceFiles: true);

        progress.Report(100);
    }

    // Read again, because the refresh above is what learned the ids. Oldest first, so the one that
    // survives a merge is the one resolving a name by hand picks.
    private IReadOnlyList<BaseItem> RefreshedInOrder()
    {
        return _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Genre],
            OrderBy = [(ItemSortBy.DateCreated, SortOrder.Ascending)],
            DtoOptions = new DtoOptions(true)
        });
    }
}
