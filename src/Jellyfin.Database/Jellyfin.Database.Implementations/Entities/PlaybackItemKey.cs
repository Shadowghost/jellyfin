using System;

namespace Jellyfin.Database.Implementations.Entities;

/// <summary>
/// One entry of a <see cref="PlaybackItem"/>'s provider-derived key set
/// (from <c>BaseItem.GetUserDataKeys()</c>). Stored once per logical item; used to reattach
/// history when an item is re-added. The key is globally unique, so a single key always
/// belongs to exactly one <see cref="PlaybackItem"/>.
/// </summary>
public class PlaybackItemKey
{
    /// <summary>
    /// Gets or sets the row identity.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the owning <see cref="PlaybackItem"/> id.
    /// </summary>
    public required Guid PlaybackItemId { get; set; }

    /// <summary>
    /// Gets or sets the owning <see cref="PlaybackItem"/>.
    /// </summary>
    public PlaybackItem? PlaybackItem { get; set; }

    /// <summary>
    /// Gets or sets the user-data key.
    /// </summary>
    public required string Key { get; set; }
}
