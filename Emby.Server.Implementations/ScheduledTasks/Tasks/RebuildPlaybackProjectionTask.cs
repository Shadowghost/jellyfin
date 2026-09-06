using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Tasks;

namespace Emby.Server.Implementations.ScheduledTasks.Tasks;

/// <summary>
/// Rebuilds the played state, play count, and played date every user has from the recorded playback
/// history.
/// </summary>
/// <remarks>
/// Those columns are a projection: the history is what actually happened, and they exist so that
/// played/unplayed filters and folder counts do not have to aggregate a table holding one row per
/// play. Playback keeps the projection current, so this is a repair tool rather than routine
/// maintenance - for after a restore, a manual edit, or a bug that let the two drift apart. It has no
/// default trigger for that reason. It is idempotent, so it is safe to run repeatedly; the first run
/// on a library whose watch state predates the history store can still move numbers, because it makes
/// every data key of an item agree on the item's totals rather than only the key the read path
/// happens to resolve to.
/// </remarks>
public class RebuildPlaybackProjectionTask : IScheduledTask
{
    private readonly ILocalizationManager _localization;
    private readonly IPlaybackHistoryManager _playbackHistoryManager;
    private readonly IUserDataManager _userDataManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="RebuildPlaybackProjectionTask"/> class.
    /// </summary>
    /// <param name="localization">The localisation provider.</param>
    /// <param name="playbackHistoryManager">The playback history manager.</param>
    /// <param name="userDataManager">The user data manager.</param>
    public RebuildPlaybackProjectionTask(
        ILocalizationManager localization,
        IPlaybackHistoryManager playbackHistoryManager,
        IUserDataManager userDataManager)
    {
        _localization = localization;
        _playbackHistoryManager = playbackHistoryManager;
        _userDataManager = userDataManager;
    }

    /// <inheritdoc />
    public string Name => _localization.GetLocalizedString("TaskRebuildPlaybackProjection");

    /// <inheritdoc />
    public string Description => _localization.GetLocalizedString("TaskRebuildPlaybackProjectionDescription");

    /// <inheritdoc />
    public string Category => _localization.GetLocalizedString("TasksMaintenanceCategory");

    /// <inheritdoc />
    public string Key => nameof(RebuildPlaybackProjectionTask);

    /// <inheritdoc/>
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await _playbackHistoryManager.RebuildUserDataProjectionAsync(progress, cancellationToken).ConfigureAwait(false);

        // The rebuild writes straight to the database, so nothing invalidated the cached copies of the
        // rows it just replaced.
        _userDataManager.ClearCache();
    }

    /// <inheritdoc/>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
}
