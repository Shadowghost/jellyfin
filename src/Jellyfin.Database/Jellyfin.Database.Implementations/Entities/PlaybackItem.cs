#pragma warning disable CA2227 // Collection properties should be read only

using System;
using System.Collections.Generic;

namespace Jellyfin.Database.Implementations.Entities;

/// <summary>
/// A stable logical-item identity for playback history.
/// Decouples history from the volatile <see cref="BaseItemEntity"/> id so that history
/// survives library rescans, moved files, and remove/re-add cycles.
/// </summary>
public class PlaybackItem
{
    /// <summary>
    /// Gets or sets the stable surrogate identity. History references and aggregates on this.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the currently-linked live item. This is a plain column, not a foreign key:
    /// playback history is a decoupled event store. It is set to <c>null</c> when the item is deleted
    /// and set again (by key) when the item is re-added.
    /// </summary>
    public Guid? ItemId { get; set; }

    /// <summary>
    /// Gets or sets a snapshot of the item's display name, so removed items stay readable in history.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets a snapshot of the item kind (e.g. Movie, Episode, Audio) for type breakdowns.
    /// </summary>
    public string? MediaType { get; set; }

    /// <summary>
    /// Gets or sets the date this identity was created. Used to pick a deterministic survivor on merge.
    /// </summary>
    public DateTime DateCreated { get; set; }

    /// <summary>
    /// Gets or sets the provider-derived key set used to reattach this identity across re-adds.
    /// </summary>
    public ICollection<PlaybackItemKey>? Keys { get; set; }

    /// <summary>
    /// Gets or sets the playback history entries belonging to this logical item.
    /// </summary>
    public ICollection<UserPlaybackHistory>? History { get; set; }
}
