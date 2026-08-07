using System;

namespace MediaBrowser.Controller.Library;

/// <summary>
/// The playback totals a user has accumulated against one logical item, aggregated from the
/// recorded playback history.
/// </summary>
/// <remarks>
/// This is the input to the projection <see cref="IUserDataManager"/> maintains: the history is the
/// source of truth for how often and when something was played, but every played/unplayed filter,
/// sort, and folder count in the item queries reads the projected columns instead, because none of
/// them can afford an aggregate over a table holding one row per play.
/// </remarks>
/// <param name="PlayCount">The number of recorded sessions.</param>
/// <param name="LastPlayedDate">When the most recent session began, or <c>null</c> if there are none.</param>
/// <param name="HasCompletion">Whether any recorded session reached completion.</param>
public readonly record struct PlaybackItemStats(int PlayCount, DateTime? LastPlayedDate, bool HasCompletion)
{
    /// <summary>
    /// Gets a value indicating whether any playback has been recorded at all. When nothing has, the
    /// projection leaves the existing values alone rather than clearing them: an item can carry a
    /// played date that predates the history store, or one set by a metadata import.
    /// </summary>
    public bool HasHistory => PlayCount > 0;
}
