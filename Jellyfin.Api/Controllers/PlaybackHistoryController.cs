using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Extensions;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Api.Controllers;

/// <summary>
/// Playback history controller. Exposes the authenticated user's recorded playback sessions.
/// </summary>
[Route("UserPlaybackHistory")]
[Authorize]
[Tags("Playback History")]
public class PlaybackHistoryController : BaseJellyfinApiController
{
    private readonly IPlaybackHistoryManager _playbackHistoryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackHistoryController"/> class.
    /// </summary>
    /// <param name="playbackHistoryManager">Instance of the <see cref="IPlaybackHistoryManager"/> interface.</param>
    public PlaybackHistoryController(IPlaybackHistoryManager playbackHistoryManager)
    {
        _playbackHistoryManager = playbackHistoryManager;
    }

    /// <summary>
    /// Gets the authenticated user's playback history, newest first.
    /// </summary>
    /// <param name="itemId">Optional. Scope to a single item.</param>
    /// <param name="startDate">Optional. Inclusive start of the date range (by stop time).</param>
    /// <param name="endDate">Optional. Inclusive end of the date range (by stop time).</param>
    /// <param name="limit">Optional. Maximum number of entries to return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Playback history returned.</response>
    /// <returns>The playback history entries.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PlaybackHistoryDto>>> GetPlaybackHistory(
        [FromQuery] Guid? itemId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();

        var history = await _playbackHistoryManager
            .GetHistoryAsync(userId, itemId, startDate, endDate, null, limit, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<PlaybackHistoryDto> result = history.Select(Map).ToList();
        return Ok(result);
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
