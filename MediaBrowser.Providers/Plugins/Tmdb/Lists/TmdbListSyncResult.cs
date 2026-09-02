using System;

namespace MediaBrowser.Providers.Plugins.Tmdb.Lists
{
    /// <summary>
    /// The outcome of creating or re-syncing a collection from a TMDb list.
    /// </summary>
    public class TmdbListSyncResult
    {
        /// <summary>
        /// Gets or sets the TMDb id of the list that was synced.
        /// </summary>
        public required int ListId { get; set; }

        /// <summary>
        /// Gets or sets the name of the list on TMDb.
        /// </summary>
        public string? ListName { get; set; }

        /// <summary>
        /// Gets or sets the id of the local collection.
        /// </summary>
        public Guid CollectionId { get; set; }

        /// <summary>
        /// Gets or sets the name of the local collection. This is the list name for a newly created
        /// collection, but an existing collection keeps whatever name it has.
        /// </summary>
        public string? CollectionName { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the collection was created by this sync, as opposed
        /// to an existing collection for the same list being re-synced.
        /// </summary>
        public bool Created { get; set; }

        /// <summary>
        /// Gets or sets the number of entries on the TMDb list.
        /// </summary>
        public int ListItemCount { get; set; }

        /// <summary>
        /// Gets or sets the number of library items the list entries were matched to. This can exceed
        /// the number of matched entries when the same movie or series is present more than once.
        /// </summary>
        public int MatchedItemCount { get; set; }

        /// <summary>
        /// Gets or sets the number of list entries that are not in the library.
        /// </summary>
        public int MissingItemCount { get; set; }

        /// <summary>
        /// Gets or sets the number of items added to the collection.
        /// </summary>
        public int ItemsAdded { get; set; }

        /// <summary>
        /// Gets or sets the number of items removed from the collection because they are no longer
        /// on the list.
        /// </summary>
        public int ItemsRemoved { get; set; }
    }
}
