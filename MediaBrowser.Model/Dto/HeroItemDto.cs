using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;

namespace MediaBrowser.Model.Dto;

/// <summary>
/// An item shown by the hero section.
/// </summary>
/// <remarks>
/// Deliberately narrower than <see cref="BaseItemDto"/>: it carries only what a hero slide renders,
/// with everything the client would otherwise have to derive already resolved. Anything beyond this
/// is a regular item lookup away.
/// </remarks>
public class HeroItemDto
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HeroItemDto"/> class.
    /// </summary>
    public HeroItemDto()
    {
        Name = string.Empty;
        ServerId = string.Empty;
        Genres = [];
        BackdropImageTags = [];
    }

    /// <summary>
    /// Gets or sets the item id.
    /// </summary>
    /// <value>The item id.</value>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the server id.
    /// </summary>
    /// <value>The server id.</value>
    public string ServerId { get; set; }

    /// <summary>
    /// Gets or sets the item type.
    /// </summary>
    /// <value>The item type.</value>
    public BaseItemKind Type { get; set; }

    /// <summary>
    /// Gets or sets the item name.
    /// </summary>
    /// <value>The item name.</value>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the overview.
    /// </summary>
    /// <value>The overview.</value>
    public string? Overview { get; set; }

    /// <summary>
    /// Gets or sets the genres.
    /// </summary>
    /// <value>The genres.</value>
    public IReadOnlyList<string> Genres { get; set; }

    /// <summary>
    /// Gets or sets the production year.
    /// </summary>
    /// <remarks>
    /// A year rather than a premiere date, because that is all a hero slide shows and deriving it
    /// from a date on the client gets it wrong either side of new year.
    /// </remarks>
    /// <value>The production year.</value>
    public int? ProductionYear { get; set; }

    /// <summary>
    /// Gets or sets the official rating.
    /// </summary>
    /// <value>The official rating.</value>
    public string? OfficialRating { get; set; }

    /// <summary>
    /// Gets or sets the community rating.
    /// </summary>
    /// <value>The community rating.</value>
    public float? CommunityRating { get; set; }

    /// <summary>
    /// Gets or sets the critic rating.
    /// </summary>
    /// <value>The critic rating.</value>
    public float? CriticRating { get; set; }

    /// <summary>
    /// Gets or sets the run time in ticks.
    /// </summary>
    /// <value>The run time in ticks, set for items that are a single playable thing.</value>
    public long? RunTimeTicks { get; set; }

    /// <summary>
    /// Gets or sets the child count.
    /// </summary>
    /// <value>
    /// The number of children visible to the user, which for a series is its season count. Set for
    /// folder items only.
    /// </value>
    public int? ChildCount { get; set; }

    /// <summary>
    /// Gets or sets the logo image tag.
    /// </summary>
    /// <value>The logo image tag.</value>
    public string? LogoImageTag { get; set; }

    /// <summary>
    /// Gets or sets the backdrop image tags.
    /// </summary>
    /// <value>The backdrop image tags, in image index order.</value>
    public IReadOnlyList<string> BackdropImageTags { get; set; }

    /// <summary>
    /// Gets or sets the thumb image tag.
    /// </summary>
    /// <value>The thumb image tag, used by compact layouts.</value>
    public string? ThumbImageTag { get; set; }

    /// <summary>
    /// Gets or sets the primary image tag.
    /// </summary>
    /// <value>The primary image tag, used by portrait layouts.</value>
    public string? PrimaryImageTag { get; set; }

    /// <summary>
    /// Gets or sets the trailer to play behind the slide.
    /// </summary>
    /// <value>The trailer, or <c>null</c> if the item has none or trailers are disabled.</value>
    public HeroTrailerDto? Trailer { get; set; }

    /// <summary>
    /// Gets or sets the user data.
    /// </summary>
    /// <value>The user data.</value>
    public UserItemDataDto? UserData { get; set; }
}
