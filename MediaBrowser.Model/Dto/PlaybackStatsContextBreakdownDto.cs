using System;
using System.Collections.Generic;

namespace MediaBrowser.Model.Dto;

/// <summary>
/// Breakdown of playback by client app, device, and media type across a filter window.
/// </summary>
public class PlaybackStatsContextBreakdownDto
{
    /// <summary>
    /// Gets or sets the play distribution by client/app.
    /// </summary>
    public IReadOnlyList<NameCountDto> Clients { get; set; } = Array.Empty<NameCountDto>();

    /// <summary>
    /// Gets or sets the play distribution by device.
    /// </summary>
    public IReadOnlyList<NameCountDto> Devices { get; set; } = Array.Empty<NameCountDto>();

    /// <summary>
    /// Gets or sets the play distribution by media type (Movie, Episode, Audio, …).
    /// </summary>
    public IReadOnlyList<NameCountDto> MediaTypes { get; set; } = Array.Empty<NameCountDto>();
}
