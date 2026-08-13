using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.Library.Validators;

/// <summary>
/// Class MusicGenresPostScanTask.
/// </summary>
public class StudiosPostScanTask : ILibraryPostScanTask
{
    /// <summary>
    /// The _library manager.
    /// </summary>
    private readonly ILibraryManager _libraryManager;

    private readonly ILogger<StudiosValidator> _logger;
    private readonly IItemMerger _itemMerger;
    private readonly IFileSystem _fileSystem;

    /// <summary>
    /// Initializes a new instance of the <see cref="StudiosPostScanTask" /> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="itemMerger">The item merger.</param>
    /// <param name="fileSystem">The file system.</param>
    public StudiosPostScanTask(
        ILibraryManager libraryManager,
        ILogger<StudiosValidator> logger,
        IItemMerger itemMerger,
        IFileSystem fileSystem)
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
    public Task Run(IProgress<double> progress, CancellationToken cancellationToken)
    {
        return new StudiosValidator(_libraryManager, _logger, _itemMerger, _fileSystem).Run(progress, cancellationToken);
    }
}
