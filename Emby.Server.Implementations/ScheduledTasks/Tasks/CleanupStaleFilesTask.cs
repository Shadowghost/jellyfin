#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.ScheduledTasks.Tasks;

/// <summary>
/// Deletes items which reference non-existing paths.
/// </summary>
public class CleanupStaleFilesTask : IScheduledTask
{
    private readonly ILocalizationManager _localization;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<CleanupStaleFilesTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CleanupStaleFilesTask"/> class.
    /// </summary>
    /// <param name="localization">Instance of the <see cref="ILocalizationManager"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger"/> interface.</param>
    public CleanupStaleFilesTask(
        ILocalizationManager localization,
        ILibraryManager libraryManager,
        ILogger<CleanupStaleFilesTask> logger)
    {
        _localization = localization;
        _logger = logger;
        _libraryManager = libraryManager;
    }

    /// <inheritdoc />
    public string Name => _localization.GetLocalizedString("TaskCleanStale");

    /// <inheritdoc />
    public string Key => "CleanupStaleFilesTask";

    /// <inheritdoc />
    public string Description => _localization.GetLocalizedString("TaskCleanStaleDescription");

    /// <inheritdoc />
    public string Category => _localization.GetLocalizedString("TasksMaintenanceCategory");

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var startIndex = 0;
        var pagesize = 1000;

        while (true)
        {
            var result = _libraryManager.GetItemsResult(new InternalItemsQuery
            {
                MediaTypes = [MediaType.Video, MediaType.Audio, MediaType.Photo],
                IsVirtualItem = false,
                OrderBy = [(ItemSortBy.SortName, SortOrder.Ascending)],
                StartIndex = startIndex,
                Limit = pagesize,
                Recursive = true,
                EnableTotalRecordCount = true
            });

            foreach (var item in result.Items)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                var itemPath = item.Path;
                if (string.IsNullOrEmpty(itemPath))
                {
                    continue;
                }

                if (item is Folder)
                {
                    if (!Directory.Exists(itemPath))
                    {
                        _logger.LogInformation("Referenced folder for {Item} at {Path} not found - removing item.", itemPath, item.Id);
                        _libraryManager.DeleteItem(
                            item,
                            new DeleteOptions
                            {
                                DeleteFileLocation = false
                            },
                            true);
                    }
                }
                else if (!File.Exists(itemPath))
                {
                    _logger.LogInformation("Referenced file for {Item} at {Path} not found - removing item.", itemPath, item.Id);
                    _libraryManager.DeleteItem(
                        item,
                        new DeleteOptions
                        {
                            DeleteFileLocation = false
                        },
                        true);
                }
            }

            if (result.Items.Count < pagesize)
            {
                break;
            }

            startIndex += pagesize;
            var total = result.TotalRecordCount;
            double percent = startIndex;
            percent /= total;

            progress.Report(percent * 100);
            _logger.LogInformation("Processed {Processed} out of {Total} items.", startIndex, total);
        }

        progress.Report(100);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return [];
    }
}
