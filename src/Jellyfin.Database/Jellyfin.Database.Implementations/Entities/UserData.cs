using System;

namespace Jellyfin.Database.Implementations.Entities;

/// <summary>
/// Provides <see cref="BaseItemEntity"/> and <see cref="User"/> related data.
/// </summary>
public class UserData
{
    /// <summary>
    /// Gets or sets the custom data key.
    /// </summary>
    /// <value>The rating.</value>
    public required string CustomDataKey { get; set; }

    /// <summary>
    /// Gets or sets the users 0-10 rating.
    /// </summary>
    /// <value>The rating.</value>
    public double? Rating { get; set; }

    /// <summary>
    /// Gets or sets the playback position ticks.
    /// </summary>
    /// <value>The playback position ticks.</value>
    public long PlaybackPositionTicks { get; set; }

    /// <summary>
    /// Gets or sets the play count.
    /// </summary>
    /// <value>The play count.</value>
    public int PlayCount { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this instance is favorite.
    /// </summary>
    /// <value><c>true</c> if this instance is favorite; otherwise, <c>false</c>.</value>
    public bool IsFavorite { get; set; }

    /// <summary>
    /// Gets or sets the last played date.
    /// </summary>
    /// <value>The last played date.</value>
    public DateTime? LastPlayedDate { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this <see cref="UserData" /> is played.
    /// </summary>
    /// <remarks>
    /// A projection of the playback history, not an independent fact: it is recomputed from the
    /// recorded sessions (or from <see cref="PlayedOverride"/> when one is set) rather than written
    /// directly. It stays a stored column because every played/unplayed filter, sort, and folder
    /// count in the item queries reads it, and none of them can afford an aggregate over the history.
    /// </remarks>
    /// <value><c>true</c> if played; otherwise, <c>false</c>.</value>
    public bool Played { get; set; }

    /// <summary>
    /// Gets or sets an explicit played state that wins over the recorded history.
    /// </summary>
    /// <remarks>
    /// Playback history is append-only, so "mark as unplayed" cannot be expressed by removing the
    /// plays that happened - and "mark as played" should not invent plays that did not. Both are
    /// recorded here instead, which keeps the history a truthful record of observed playback while
    /// still letting a user decide what the library shows. <c>null</c> means no explicit choice has
    /// been made and <see cref="Played"/> follows the history.
    /// </remarks>
    public bool? PlayedOverride { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user has dismissed this item from Continue Watching.
    /// </summary>
    /// <remarks>
    /// Purely a visibility choice: the resume position is left untouched, so the item still resumes
    /// where it was left if it is played again. Playing it (or, for a series, playing any episode of
    /// it) clears the dismissal, on the same principle as <see cref="PlayedOverride"/> - a fresh
    /// observation retires an earlier choice about what the library should show.
    /// </remarks>
    public bool ExcludedFromResume { get; set; }

    /// <summary>
    /// Gets or sets the index of the audio stream.
    /// </summary>
    /// <value>The index of the audio stream.</value>
    public int? AudioStreamIndex { get; set; }

    /// <summary>
    /// Gets or sets the index of the subtitle stream.
    /// </summary>
    /// <value>The index of the subtitle stream.</value>
    public int? SubtitleStreamIndex { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the item is liked or not.
    /// This should never be serialized.
    /// </summary>
    /// <value><c>null</c> if [likes] contains no value, <c>true</c> if [likes]; otherwise, <c>false</c>.</value>
    public bool? Likes { get; set; }

    /// <summary>
    /// Gets or Sets the date the referenced <see cref="Item"/> has been deleted.
    /// </summary>
    public DateTime? RetentionDate { get; set; }

    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    /// <value>The key.</value>
    public required Guid ItemId { get; set; }

    /// <summary>
    /// Gets or Sets the BaseItem.
    /// </summary>
    public required BaseItemEntity? Item { get; set; }

    /// <summary>
    /// Gets or Sets the UserId.
    /// </summary>
    public required Guid UserId { get; set; }

    /// <summary>
    /// Gets or Sets the User.
    /// </summary>
    public required User? User { get; set; }
}
