using System;

namespace MediaBrowser.Model.Dto
{
    /// <summary>
    /// This is used by the api to get information about a item user data.
    /// </summary>
    public class UpdateUserItemDataDto
    {
        /// <summary>
        /// Gets or sets the rating.
        /// </summary>
        /// <value>The rating.</value>
        public double? Rating { get; set; }

        /// <summary>
        /// Gets or sets the played percentage.
        /// </summary>
        /// <value>The played percentage.</value>
        public double? PlayedPercentage { get; set; }

        /// <summary>
        /// Gets or sets the unplayed item count.
        /// </summary>
        /// <value>The unplayed item count.</value>
        public int? UnplayedItemCount { get; set; }

        /// <summary>
        /// Gets or sets the playback position ticks.
        /// </summary>
        /// <value>The playback position ticks.</value>
        public long? PlaybackPositionTicks { get; set; }

        /// <summary>
        /// Gets or sets the play count.
        /// </summary>
        /// <value>The play count.</value>
        public int? PlayCount { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is favorite.
        /// </summary>
        /// <value><c>true</c> if this instance is favorite; otherwise, <c>false</c>.</value>
        public bool? IsFavorite { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this <see cref="UpdateUserItemDataDto" /> is likes.
        /// </summary>
        /// <value><c>null</c> if [likes] contains no value, <c>true</c> if [likes]; otherwise, <c>false</c>.</value>
        public bool? Likes { get; set; }

        /// <summary>
        /// Gets or sets the last played date.
        /// </summary>
        /// <value>The last played date.</value>
        public DateTime? LastPlayedDate { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this <see cref="UserItemDataDto" /> is played.
        /// </summary>
        /// <value><c>true</c> if played; otherwise, <c>false</c>.</value>
        public bool? Played { get; set; }

        /// <summary>
        /// Gets or sets the audio stream index. Null leaves the stored value unchanged.
        /// </summary>
        public int? AudioStreamIndex { get; set; }

        /// <summary>
        /// Gets or sets the subtitle stream index. Null leaves the stored value unchanged.
        /// </summary>
        public int? SubtitleStreamIndex { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether <see cref="LastPlayedDate"/> should be cleared.
        /// When true, the stored value is set to null and <see cref="LastPlayedDate"/> is ignored.
        /// </summary>
        public bool ResetLastPlayedDate { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether <see cref="Rating"/> (and therefore <see cref="Likes"/>) should be cleared.
        /// When true, the stored rating is set to null and <see cref="Rating"/>/<see cref="Likes"/> are ignored.
        /// </summary>
        public bool ResetRating { get; set; }

        /// <summary>
        /// Gets or sets the key.
        /// </summary>
        /// <value>The key.</value>
        public string? Key { get; set; }

        /// <summary>
        /// Gets or sets the item identifier.
        /// </summary>
        /// <value>The item identifier.</value>
        public string? ItemId { get; set; }
    }
}
