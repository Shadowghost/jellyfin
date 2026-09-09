using System;
using Jellyfin.Data.Enums;

namespace MediaBrowser.Model.Configuration;

/// <summary>
/// The hero section options.
/// </summary>
public class HeroOptions
{
    /// <summary>
    /// The highest number of items a single request may return, regardless of configuration.
    /// </summary>
    public const int MaxItemLimit = 50;

    /// <summary>
    /// Initializes a new instance of the <see cref="HeroOptions"/> class.
    /// </summary>
    public HeroOptions()
    {
        IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series];
    }

    /// <summary>
    /// Gets or sets a value indicating whether the hero section is enabled.
    /// </summary>
    /// <value><c>true</c> if the hero section is enabled; otherwise <c>false</c>.</value>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the source the hero items are taken from.
    /// </summary>
    /// <value>The source mode.</value>
    public HeroSourceMode Source { get; set; } = HeroSourceMode.Random;

    /// <summary>
    /// Gets or sets the id of the playlist or collection to take items from.
    /// </summary>
    /// <remarks>
    /// Only used when <see cref="Source"/> is <see cref="HeroSourceMode.Playlist"/>
    /// or <see cref="HeroSourceMode.Collection"/>.
    /// </remarks>
    /// <value>The source id.</value>
    public Guid SourceId { get; set; }

    /// <summary>
    /// Gets or sets the item types the hero section may show.
    /// </summary>
    /// <value>The included item types.</value>
    public BaseItemKind[] IncludeItemTypes { get; set; }

    /// <summary>
    /// Gets or sets the number of items to return.
    /// </summary>
    /// <value>The maximum item count.</value>
    public int MaxItems { get; set; } = 12;

    /// <summary>
    /// Gets or sets a value indicating whether watched items may be shown.
    /// </summary>
    /// <value><c>true</c> if watched items may be shown; otherwise <c>false</c>.</value>
    public bool IncludeWatched { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an item needs a logo image to be shown.
    /// </summary>
    /// <remarks>
    /// A hero slide without a logo falls back to rendering the item name as text, which reads as
    /// broken artwork. Turning this off is meant for libraries with sparse logo art.
    /// </remarks>
    /// <value><c>true</c> if a logo image is required; otherwise <c>false</c>.</value>
    public bool RequireLogo { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether an item needs an overview to be shown.
    /// </summary>
    /// <value><c>true</c> if an overview is required; otherwise <c>false</c>.</value>
    public bool RequireOverview { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether remote trailer metadata is served to clients.
    /// </summary>
    /// <remarks>
    /// When disabled the trailer is omitted from the response entirely, so no client can reach out
    /// to the trailer host.
    /// </remarks>
    /// <value><c>true</c> if remote trailers are served; otherwise <c>false</c>.</value>
    public bool EnableRemoteTrailers { get; set; } = true;
}
