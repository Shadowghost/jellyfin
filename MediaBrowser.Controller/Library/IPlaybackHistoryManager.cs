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
    /// Gets playback history, optionally scoped to a user, an item, and/or a date range.
    /// </summary>
    /// <param name="userId">Optional user id; <c>null</c> returns every user's sessions.</param>
    /// <param name="itemId">Optional item id (the live <see cref="BaseItemEntity"/> id) to scope to.</param>
    /// <param name="startDate">Optional inclusive start of the date range (by <c>DateStopped</c>).</param>
    /// <param name="endDate">Optional inclusive end of the date range (by <c>DateStopped</c>).</param>
    /// <param name="mediaType">Optional media-type filter (Movie, Episode, Audio, …).</param>
    /// <param name="limit">Optional maximum number of rows.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching history rows, newest first.</returns>
    Task<IReadOnlyList<UserPlaybackHistory>> GetHistoryAsync(
        Guid? userId,
        Guid? itemId,
        DateTime? startDate,
        DateTime? endDate,
        string? mediaType,
        int? limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets headline playback statistics for the given filter window.
    /// </summary>
    /// <param name="startDate">Optional inclusive start (by stop time).</param>
    /// <param name="endDate">Optional inclusive end (by stop time).</param>
    /// <param name="userId">Optional user filter.</param>
    /// <param name="mediaType">Optional media-type filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The summary.</returns>
    Task<PlaybackStatsSummaryDto> GetStatsSummaryAsync(DateTime? startDate, DateTime? endDate, Guid? userId, string? mediaType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the playback activity timeline, bucketed by the given interval.
    /// </summary>
    /// <param name="startDate">Optional inclusive start (by stop time).</param>
    /// <param name="endDate">Optional inclusive end (by stop time).</param>
    /// <param name="userId">Optional user filter.</param>
    /// <param name="mediaType">Optional media-type filter.</param>
    /// <param name="interval">The bucket size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ordered timeline buckets.</returns>
    Task<IReadOnlyList<PlaybackStatsTimelineEntryDto>> GetStatsTimelineAsync(DateTime? startDate, DateTime? endDate, Guid? userId, string? mediaType, PlaybackStatsInterval interval, CancellationToken cancellationToken = default);

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
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The populated heatmap cells (empty slots omitted).</returns>
    Task<IReadOnlyList<PlaybackStatsHeatmapEntryDto>> GetHeatmapAsync(DateTime? startDate, DateTime? endDate, Guid? userId, string? mediaType, CancellationToken cancellationToken = default);
}
