using System;

namespace MediaBrowser.Model.Dto;

/// <summary>
/// The trailer a hero slide should play, already picked out of everything the item has.
/// </summary>
public class HeroTrailerDto
{
    /// <summary>
    /// Gets or sets the kind of trailer.
    /// </summary>
    /// <value>The trailer kind.</value>
    public HeroTrailerKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the item id of a local trailer.
    /// </summary>
    /// <value>The trailer item id, or <c>null</c> for a remote trailer.</value>
    public Guid? ItemId { get; set; }

    /// <summary>
    /// Gets or sets the url of a remote trailer.
    /// </summary>
    /// <value>The trailer url, or <c>null</c> for a local trailer.</value>
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the name of the host serving a remote trailer.
    /// </summary>
    /// <value>The provider name, for example <c>YouTube</c>.</value>
    public string? Provider { get; set; }

    /// <summary>
    /// Gets or sets the id identifying the trailer at its provider.
    /// </summary>
    /// <value>The provider specific id, for example a YouTube video id.</value>
    public string? ProviderId { get; set; }

    /// <summary>
    /// Gets or sets the trailer name.
    /// </summary>
    /// <value>The trailer name.</value>
    public string? Name { get; set; }
}
