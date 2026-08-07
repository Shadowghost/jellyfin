using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;

namespace MediaBrowser.Controller.Library;

/// <summary>
/// Manages the append-only playback history store and its logical-item identities.
/// This is an internal analytics/event store, separate from <see cref="IUserDataManager"/>.
/// </summary>
public interface IPlaybackHistoryManager
{
    /// <summary>
    /// Records a completed playback session, resolving (and reattaching/merging) the logical item identity.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="item">The item that was played.</param>
    /// <param name="info">The captured session details.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    Task RecordPlaybackAsync(User user, BaseItem item, PlaybackHistoryInfo info, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports network bytes streamed to the client for an in-flight play session, accumulated until the
    /// session is recorded. Measured during delivery (see the stream observer) and folded into the
    /// session's actual-bytes-transferred figure when it is persisted.
    /// </summary>
    /// <param name="playSessionId">The play session id correlating the transfer to a session.</param>
    /// <param name="bytes">The number of bytes transferred since the last report.</param>
    void ReportTransferredBytes(string playSessionId, long bytes);

    /// <summary>
    /// Drops the accumulated transfer measurement for a play session that will never be recorded
    /// (playback failed, or fell below the minimum-view threshold).
    /// </summary>
    /// <param name="playSessionId">The play session id.</param>
    void DiscardPendingTransfer(string playSessionId);

    /// <summary>
    /// Resolves the <see cref="PlaybackItem"/> identity for an item, creating it if needed and
    /// reattaching/merging by the item's user-data key set. Updates the live item link.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resolved <see cref="PlaybackItem"/> id.</returns>
    Task<Guid> ResolvePlaybackItemAsync(BaseItem item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-links existing playback-history identities to an item that was (re-)created or updated,
    /// matching by the item's user-data key set and merging duplicates. Does nothing if the item has
    /// no recorded history. Mirrors how <see cref="IUserDataManager"/> data is reattached.
    /// </summary>
    /// <param name="item">The item that was created or updated.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    Task ReattachItemAsync(BaseItem item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unlinks any identity pointing at an item that was removed from the library, keeping the
    /// identity and its history. Detached identities are what the retention task ages out, and what
    /// <see cref="ReattachItemAsync"/> re-adopts if the item returns.
    /// </summary>
    /// <param name="itemId">The id of the removed item.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    Task DetachItemAsync(Guid itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aggregates a user's recorded playback of one item, for the projection <see cref="IUserDataManager"/>
    /// maintains. Resolves the identity by the item's user-data key set, so plays survive the item being
    /// removed and re-added, and returns empty totals when nothing has been recorded.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="item">The item to aggregate.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The user's totals for the item.</returns>
    Task<PlaybackItemStats> GetUserItemStatsAsync(Guid userId, BaseItem item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds the played state, play count, and played date every user has, from the recorded
    /// playback history, for every item the history knows about.
    /// </summary>
    /// <remarks>
    /// The batch equivalent of <see cref="IUserDataManager.ApplyPlaybackStats"/>, and it settles every
    /// row the same way that does. It is idempotent, but the first run over data that predates the
    /// history store is not a no-op: user data is stored once per data key, and this writes the logical
    /// item's totals to every key it answers to, so rows left behind under a stale key stop disagreeing
    /// with the one the read path resolves to. Rows the history says nothing about are left alone
    /// rather than zeroed - an absent aggregate is not evidence that nothing was ever played, only
    /// that this store has no record of it.
    /// <para>
    /// Written straight to the database rather than through <see cref="IUserDataManager"/>, so callers
    /// must follow it with <see cref="IUserDataManager.ClearCache"/>.
    /// </para>
    /// </remarks>
    /// <param name="progress">Reports progress between 0 and 100.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of user data rows rewritten.</returns>
    Task<int> RebuildUserDataProjectionAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Purges all playback history belonging to a deleted user. <c>UserId</c> is not a foreign key,
    /// so nothing removes these rows on the user's behalf; without this the per-user breakdown keeps
    /// reporting an id that no longer resolves to anyone.
    /// </summary>
    /// <param name="userId">The id of the deleted user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    Task DeleteUserHistoryAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets playback history, optionally scoped to a user, an item, and/or a date range.
    /// </summary>
    /// <param name="userId">Optional user id; <c>null</c> returns every user's sessions.</param>
    /// <param name="itemId">Optional item id (the live <see cref="BaseItemEntity"/> id) to scope to.</param>
    /// <param name="startDate">Optional inclusive start of the date range (by <c>DateStopped</c>).</param>
    /// <param name="endDate">Optional inclusive end of the date range (by <c>DateStopped</c>).</param>
    /// <param name="mediaType">Optional media-type filter (Movie, Episode, Audio, …).</param>
    /// <param name="limit">Optional maximum number of rows.</param>
    /// <param name="includeImported">
    /// Whether to include entries reconstructed from the pre-existing watched status. A user reading
    /// back what they have watched wants them; an activity feed describing recent sessions does not,
    /// because an imported entry has no device, no client, and no meaningful time of day.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching history rows, newest first.</returns>
    Task<IReadOnlyList<UserPlaybackHistory>> GetHistoryAsync(
        Guid? userId,
        Guid? itemId,
        DateTime? startDate,
        DateTime? endDate,
        string? mediaType,
        int? limit,
        bool includeImported = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets headline playback statistics for the given filter window.
    /// </summary>
    /// <param name="startDate">Optional inclusive start (by stop time).</param>
    /// <param name="endDate">Optional inclusive end (by stop time).</param>
    /// <param name="userId">Optional user filter.</param>
    /// <param name="mediaType">Optional media-type filter.</param>
    /// <param name="utcOffsetMinutes">Viewer offset from UTC, used to decide which calendar day a session falls on.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The summary.</returns>
    Task<PlaybackStatsSummaryDto> GetStatsSummaryAsync(DateTime? startDate, DateTime? endDate, Guid? userId, string? mediaType, int utcOffsetMinutes = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the playback activity timeline, bucketed by the given interval.
    /// </summary>
    /// <param name="startDate">Optional inclusive start (by stop time).</param>
    /// <param name="endDate">Optional inclusive end (by stop time).</param>
    /// <param name="userId">Optional user filter.</param>
    /// <param name="mediaType">Optional media-type filter.</param>
    /// <param name="interval">The bucket size.</param>
    /// <param name="utcOffsetMinutes">Viewer offset from UTC, used to decide which calendar day a session falls on.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ordered timeline buckets.</returns>
    Task<IReadOnlyList<PlaybackStatsTimelineEntryDto>> GetStatsTimelineAsync(DateTime? startDate, DateTime? endDate, Guid? userId, string? mediaType, PlaybackStatsInterval interval, int utcOffsetMinutes = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a sorted, paged page of items for the given filter window.
    /// </summary>
    /// <param name="startDate">Optional inclusive start (by stop time).</param>
    /// <param name="endDate">Optional inclusive end (by stop time).</param>
    /// <param name="userId">Optional user filter.</param>
    /// <param name="mediaType">Optional media-type filter.</param>
    /// <param name="sortBy">Column to sort by: Plays, Completions, WatchTimeTicks (default) or LastPlayed.</param>
    /// <param name="descending">Sort descending.</param>
    /// <param name="startIndex">Page offset.</param>
    /// <param name="limit">Page size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The paged items with a total count.</returns>
    Task<QueryResult<PlaybackStatsItemDto>> GetTopItemsAsync(DateTime? startDate, DateTime? endDate, Guid? userId, string? mediaType, string? sortBy, bool descending, int startIndex, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a sorted, paged page of per-user playback statistics for the given filter window.
    /// </summary>
    /// <param name="startDate">Optional inclusive start (by stop time).</param>
    /// <param name="endDate">Optional inclusive end (by stop time).</param>
    /// <param name="mediaType">Optional media-type filter.</param>
    /// <param name="sortBy">Column to sort by: Plays, Completions, WatchTimeTicks (default) or LastActivity.</param>
    /// <param name="descending">Sort descending.</param>
    /// <param name="startIndex">Page offset.</param>
    /// <param name="limit">Page size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The paged per-user breakdown (user names are not resolved here).</returns>
    Task<QueryResult<PlaybackStatsUserDto>> GetUserBreakdownAsync(DateTime? startDate, DateTime? endDate, string? mediaType, string? sortBy, bool descending, int startIndex, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the selected-source stream characteristic breakdown for the given filter window.
    /// </summary>
    /// <param name="startDate">Optional inclusive start (by stop time).</param>
    /// <param name="endDate">Optional inclusive end (by stop time).</param>
    /// <param name="userId">Optional user filter.</param>
    /// <param name="mediaType">Optional media-type filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The breakdown.</returns>
    Task<PlaybackStatsStreamBreakdownDto> GetStreamBreakdownAsync(DateTime? startDate, DateTime? endDate, Guid? userId, string? mediaType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the play distribution by client, device, and media type for the given filter window.
    /// </summary>
    /// <param name="startDate">Optional inclusive start (by stop time).</param>
    /// <param name="endDate">Optional inclusive end (by stop time).</param>
    /// <param name="userId">Optional user filter.</param>
    /// <param name="mediaType">Optional media-type filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The context breakdown.</returns>
    Task<PlaybackStatsContextBreakdownDto> GetContextBreakdownAsync(DateTime? startDate, DateTime? endDate, Guid? userId, string? mediaType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the day-of-week × hour-of-day activity heatmap for the given filter window.
    /// </summary>
    /// <param name="startDate">Optional inclusive start (by stop time).</param>
    /// <param name="endDate">Optional inclusive end (by stop time).</param>
    /// <param name="userId">Optional user filter.</param>
    /// <param name="mediaType">Optional media-type filter.</param>
    /// <param name="utcOffsetMinutes">Viewer offset from UTC. Without it the day/hour cells are UTC, which
    /// misplaces evening viewing for anyone not on UTC.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The populated heatmap cells (empty slots omitted).</returns>
    Task<IReadOnlyList<PlaybackStatsHeatmapEntryDto>> GetHeatmapAsync(DateTime? startDate, DateTime? endDate, Guid? userId, string? mediaType, int utcOffsetMinutes = 0, CancellationToken cancellationToken = default);
}
