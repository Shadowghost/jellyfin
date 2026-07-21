using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Api.Controllers;

/// <summary>
/// Aggregate playback statistics for the admin dashboard. Administrator only.
/// </summary>
[Route("Playback/Statistics")]
[Authorize(Policy = Policies.RequiresElevation)]
[Tags("Playback History")]
public class PlaybackStatisticsController : BaseJellyfinApiController
{
    private readonly IPlaybackHistoryManager _playbackHistoryManager;
    private readonly IUserManager _userManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackStatisticsController"/> class.
    /// </summary>
    /// <param name="playbackHistoryManager">Instance of the <see cref="IPlaybackHistoryManager"/> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    public PlaybackStatisticsController(IPlaybackHistoryManager playbackHistoryManager, IUserManager userManager)
    {
        _playbackHistoryManager = playbackHistoryManager;
        _userManager = userManager;
    }

    /// <summary>
    /// Gets headline playback statistics.
    /// </summary>
    /// <param name="startDate">Optional. Inclusive start of the window (by stop time).</param>
    /// <param name="endDate">Optional. Inclusive end of the window (by stop time).</param>
    /// <param name="userId">Optional. Scope to a single user.</param>
    /// <param name="mediaType">Optional. Scope to a media type (Movie, Episode, Audio, …).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Summary returned.</response>
    /// <returns>The summary.</returns>
    [HttpGet("Summary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<PlaybackStatsSummaryDto>> GetPlaybackStatsSummary(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] Guid? userId,
        [FromQuery] string? mediaType,
        CancellationToken cancellationToken)
        => await _playbackHistoryManager.GetStatsSummaryAsync(startDate, endDate, userId, mediaType, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Gets the playback activity timeline.
    /// </summary>
    /// <param name="startDate">Optional. Inclusive start of the window (by stop time).</param>
    /// <param name="endDate">Optional. Inclusive end of the window (by stop time).</param>
    /// <param name="userId">Optional. Scope to a single user.</param>
    /// <param name="mediaType">Optional. Scope to a media type (Movie, Episode, Audio, …).</param>
    /// <param name="interval">The bucket size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Timeline returned.</response>
    /// <returns>The ordered timeline buckets.</returns>
    [HttpGet("Timeline")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PlaybackStatsTimelineEntryDto>>> GetPlaybackStatsTimeline(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] Guid? userId,
        [FromQuery] string? mediaType,
        [FromQuery] PlaybackStatsInterval interval = PlaybackStatsInterval.Day,
        CancellationToken cancellationToken = default)
    {
        var result = await _playbackHistoryManager.GetStatsTimelineAsync(startDate, endDate, userId, mediaType, interval, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// Gets a sorted, paged page of items.
    /// </summary>
    /// <param name="startDate">Optional. Inclusive start of the window (by stop time).</param>
    /// <param name="endDate">Optional. Inclusive end of the window (by stop time).</param>
    /// <param name="userId">Optional. Scope to a single user.</param>
    /// <param name="mediaType">Optional. Scope to a media type (Movie, Episode, Audio, …).</param>
    /// <param name="sortBy">Column to sort by: Plays, Completions, WatchTimeTicks (default) or LastPlayed.</param>
    /// <param name="descending">Sort descending.</param>
    /// <param name="startIndex">The page offset.</param>
    /// <param name="limit">The page size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Items returned.</response>
    /// <returns>The paged items.</returns>
    [HttpGet("TopItems")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<QueryResult<PlaybackStatsItemDto>>> GetPlaybackStatsTopItems(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] Guid? userId,
        [FromQuery] string? mediaType,
        [FromQuery] string? sortBy,
        [FromQuery] bool descending = true,
        [FromQuery] int startIndex = 0,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
        => await _playbackHistoryManager.GetTopItemsAsync(startDate, endDate, userId, mediaType, sortBy, descending, startIndex, limit, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Gets a sorted, paged page of per-user playback statistics.
    /// </summary>
    /// <param name="startDate">Optional. Inclusive start of the window (by stop time).</param>
    /// <param name="endDate">Optional. Inclusive end of the window (by stop time).</param>
    /// <param name="mediaType">Optional. Scope to a media type (Movie, Episode, Audio, …).</param>
    /// <param name="sortBy">Column to sort by: Plays, Completions, WatchTimeTicks (default) or LastActivity.</param>
    /// <param name="descending">Sort descending.</param>
    /// <param name="startIndex">The page offset.</param>
    /// <param name="limit">The page size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Per-user statistics returned.</response>
    /// <returns>The paged per-user breakdown.</returns>
    [HttpGet("Users")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<QueryResult<PlaybackStatsUserDto>>> GetPlaybackStatsUsers(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] string? mediaType,
        [FromQuery] string? sortBy,
        [FromQuery] bool descending = true,
        [FromQuery] int startIndex = 0,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _playbackHistoryManager.GetUserBreakdownAsync(startDate, endDate, mediaType, sortBy, descending, startIndex, limit, cancellationToken).ConfigureAwait(false);
        foreach (var entry in result.Items)
        {
            entry.UserName = _userManager.GetUserById(entry.UserId)?.Username;
        }

        return result;
    }

    /// <summary>
    /// Gets the selected-source stream characteristic breakdown.
    /// </summary>
    /// <param name="startDate">Optional. Inclusive start of the window (by stop time).</param>
    /// <param name="endDate">Optional. Inclusive end of the window (by stop time).</param>
    /// <param name="userId">Optional. Scope to a single user.</param>
    /// <param name="mediaType">Optional. Scope to a media type (Movie, Episode, Audio, …).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Stream breakdown returned.</response>
    /// <returns>The breakdown.</returns>
    [HttpGet("Streams")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<PlaybackStatsStreamBreakdownDto>> GetPlaybackStatsStreamBreakdown(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] Guid? userId,
        [FromQuery] string? mediaType,
        CancellationToken cancellationToken)
        => await _playbackHistoryManager.GetStreamBreakdownAsync(startDate, endDate, userId, mediaType, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Gets the play distribution by client, device, and media type.
    /// </summary>
    /// <param name="startDate">Optional. Inclusive start of the window (by stop time).</param>
    /// <param name="endDate">Optional. Inclusive end of the window (by stop time).</param>
    /// <param name="userId">Optional. Scope to a single user.</param>
    /// <param name="mediaType">Optional. Scope to a media type (Movie, Episode, Audio, …).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Context breakdown returned.</response>
    /// <returns>The breakdown.</returns>
    [HttpGet("Context")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<PlaybackStatsContextBreakdownDto>> GetPlaybackStatsContext(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] Guid? userId,
        [FromQuery] string? mediaType,
        CancellationToken cancellationToken)
        => await _playbackHistoryManager.GetContextBreakdownAsync(startDate, endDate, userId, mediaType, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Gets the day-of-week × hour-of-day activity heatmap.
    /// </summary>
    /// <param name="startDate">Optional. Inclusive start of the window (by stop time).</param>
    /// <param name="endDate">Optional. Inclusive end of the window (by stop time).</param>
    /// <param name="userId">Optional. Scope to a single user.</param>
    /// <param name="mediaType">Optional. Scope to a media type (Movie, Episode, Audio, …).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Heatmap returned.</response>
    /// <returns>The populated heatmap cells.</returns>
    [HttpGet("Heatmap")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PlaybackStatsHeatmapEntryDto>>> GetPlaybackStatsHeatmap(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] Guid? userId,
        [FromQuery] string? mediaType,
        CancellationToken cancellationToken)
    {
        var result = await _playbackHistoryManager.GetHeatmapAsync(startDate, endDate, userId, mediaType, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// Gets recent playback sessions across all users (or a single user), newest first.
    /// </summary>
    /// <param name="startDate">Optional. Inclusive start of the window (by stop time).</param>
    /// <param name="endDate">Optional. Inclusive end of the window (by stop time).</param>
    /// <param name="userId">Optional. Scope to a single user; omit for every user.</param>
    /// <param name="mediaType">Optional. Scope to a media type (Movie, Episode, Audio, …).</param>
    /// <param name="limit">Optional. Maximum number of sessions to return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Sessions returned.</response>
    /// <returns>The recent sessions, with user names resolved.</returns>
    [HttpGet("Sessions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PlaybackHistoryDto>>> GetPlaybackStatsSessions(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] Guid? userId,
        [FromQuery] string? mediaType,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var history = await _playbackHistoryManager
            .GetHistoryAsync(userId, null, startDate, endDate, mediaType, limit, cancellationToken)
            .ConfigureAwait(false);

        var names = new Dictionary<Guid, string?>();
        var result = new List<PlaybackHistoryDto>(history.Count);
        foreach (var entry in history)
        {
            if (!names.TryGetValue(entry.UserId, out var name))
            {
                name = _userManager.GetUserById(entry.UserId)?.Username;
                names[entry.UserId] = name;
            }

            var dto = Map(entry);
            dto.UserName = name;
            result.Add(dto);
        }

        return Ok((IReadOnlyList<PlaybackHistoryDto>)result);
    }

    private static PlaybackHistoryDto Map(Jellyfin.Database.Implementations.Entities.UserPlaybackHistory entry)
    {
        return new PlaybackHistoryDto
        {
            Id = entry.Id,
            UserId = entry.UserId,
            ItemId = entry.PlaybackItem?.ItemId,
            Title = entry.PlaybackItem?.Title,
            DateStarted = entry.DateStarted,
            DateStopped = entry.DateStopped,
            StartPositionTicks = entry.StartPositionTicks,
            StopPositionTicks = entry.StopPositionTicks,
            RunTimeTicks = entry.RunTimeTicks,
            PlayedDurationTicks = entry.PlayedDurationTicks,
            PlayedToCompletion = entry.PlayedToCompletion,
            Transcoded = entry.Transcoded,
            Bitrate = entry.Bitrate,
            ActualBytesTransferred = entry.ActualBytesTransferred,
            DeviceId = entry.DeviceId,
            ClientName = entry.ClientName,
            Streams = entry.Streams is null
                ? Array.Empty<PlaybackHistoryStreamDto>()
                : entry.Streams.Select(s => new PlaybackHistoryStreamDto
                {
                    StreamType = s.StreamType,
                    Origin = s.Origin,
                    Width = s.Width,
                    Height = s.Height,
                    VideoRange = s.VideoRange,
                    Codec = s.Codec,
                    Bitrate = s.Bitrate,
                    Channels = s.Channels,
                    Language = s.Language,
                    IsForced = s.IsForced,
                    IsHearingImpaired = s.IsHearingImpaired
                }).ToList()
        };
    }
}
