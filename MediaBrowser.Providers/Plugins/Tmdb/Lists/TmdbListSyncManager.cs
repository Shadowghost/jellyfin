using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using TMDbLib.Objects.General.Schema;
using TmdbMediaType = TMDbLib.Objects.General.MediaType;

namespace MediaBrowser.Providers.Plugins.Tmdb.Lists
{
    /// <summary>
    /// Creates and re-syncs local collections from TMDb lists. The collection is linked to the list by
    /// the <see cref="TmdbUtils.ListProviderId"/> provider id, which is how a later sync of the same
    /// list finds the collection it already created instead of creating a second one.
    /// </summary>
    public class TmdbListSyncManager
    {
        private readonly TmdbClientManager _tmdbClientManager;
        private readonly ILibraryManager _libraryManager;
        private readonly ICollectionManager _collectionManager;
        private readonly ILogger<TmdbListSyncManager> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="TmdbListSyncManager"/> class.
        /// </summary>
        /// <param name="tmdbClientManager">The <see cref="TmdbClientManager"/>.</param>
        /// <param name="libraryManager">The <see cref="ILibraryManager"/>.</param>
        /// <param name="collectionManager">The <see cref="ICollectionManager"/>.</param>
        /// <param name="logger">The <see cref="ILogger{TmdbListSyncManager}"/>.</param>
        public TmdbListSyncManager(
            TmdbClientManager tmdbClientManager,
            ILibraryManager libraryManager,
            ICollectionManager collectionManager,
            ILogger<TmdbListSyncManager> logger)
        {
            _tmdbClientManager = tmdbClientManager;
            _libraryManager = libraryManager;
            _collectionManager = collectionManager;
            _logger = logger;
        }

        /// <summary>
        /// Creates a collection from the given TMDb list, or re-syncs the contents of the collection
        /// previously created from that list. The collection mirrors the list: items that are no longer
        /// on the list are removed from it.
        /// </summary>
        /// <param name="listId">The TMDb id of the list.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The result of the sync, or null if TMDb has no public list with that id.</returns>
        public async Task<TmdbListSyncResult?> SyncListAsync(int listId, CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(listId);

            var list = await _tmdbClientManager.GetListAsync(listId, language: null, cancellationToken).ConfigureAwait(false);
            if (list is null)
            {
                return null;
            }

            var listItems = list.Items ?? [];
            var matchedItems = FindLibraryItems(listItems, out var matchedListEntries);

            var listIdValue = listId.ToString(CultureInfo.InvariantCulture);
            var result = new TmdbListSyncResult
            {
                ListId = listId,
                ListName = list.Name,
                ListItemCount = listItems.Count,
                MatchedItemCount = matchedItems.Count,
                MissingItemCount = listItems.Count - matchedListEntries
            };

            var boxSet = FindBoxSet(listIdValue);
            if (boxSet is null)
            {
                var name = string.IsNullOrWhiteSpace(list.Name)
                    ? string.Format(CultureInfo.InvariantCulture, "TMDb list {0}", listIdValue)
                    : list.Name;

                _logger.LogInformation(
                    "Creating collection {CollectionName} from TMDb list {ListId} with {ItemCount} item(s)",
                    name,
                    listId,
                    matchedItems.Count);

                boxSet = await _collectionManager.CreateCollectionAsync(new CollectionCreationOptions
                {
                    Name = name,
                    ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [TmdbUtils.ListProviderId] = listIdValue
                    },
                    ItemIdList = matchedItems.Select(id => id.ToString("N", CultureInfo.InvariantCulture)).ToArray()
                }).ConfigureAwait(false);

                result.Created = true;
                result.CollectionId = boxSet.Id;
                result.CollectionName = boxSet.Name;
                result.ItemsAdded = matchedItems.Count;

                return result;
            }

            result.CollectionId = boxSet.Id;
            result.CollectionName = boxSet.Name;

            var currentIds = boxSet.LinkedChildren
                .Where(child => child.ItemId.HasValue)
                .Select(child => child.ItemId!.Value)
                .ToList();

            var desiredIds = new HashSet<Guid>(matchedItems);
            var existingIds = new HashSet<Guid>(currentIds);

            var toAdd = matchedItems.Where(id => !existingIds.Contains(id)).ToList();
            var toRemove = currentIds.Where(id => !desiredIds.Contains(id)).ToList();

            if (toAdd.Count > 0)
            {
                await _collectionManager.AddToCollectionAsync(boxSet.Id, toAdd).ConfigureAwait(false);
            }

            if (toRemove.Count > 0)
            {
                await _collectionManager.RemoveFromCollectionAsync(boxSet.Id, toRemove).ConfigureAwait(false);
            }

            _logger.LogInformation(
                "Synced collection {CollectionName} from TMDb list {ListId}: {Added} item(s) added, {Removed} item(s) removed",
                boxSet.Name,
                listId,
                toAdd.Count,
                toRemove.Count);

            result.ItemsAdded = toAdd.Count;
            result.ItemsRemoved = toRemove.Count;

            return result;
        }

        /// <summary>
        /// Finds the collection previously created for the given list, if it still exists.
        /// </summary>
        private BoxSet? FindBoxSet(string listId)
        {
            return _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.BoxSet],
                HasAnyProviderId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [TmdbUtils.ListProviderId] = listId
                },
                CollapseBoxSetItems = false,
                Recursive = true
            }).OfType<BoxSet>().FirstOrDefault();
        }

        /// <summary>
        /// Resolves the list entries to items in the library, in list order. A list entry can match more
        /// than one library item (the same movie in two libraries, for instance), in which case all of
        /// them are included.
        /// </summary>
        /// <param name="listItems">The entries on the TMDb list.</param>
        /// <param name="matchedListEntries">The number of list entries that matched at least one item.</param>
        /// <returns>The ids of the matching library items.</returns>
        private List<Guid> FindLibraryItems(IReadOnlyList<TmdbEntity> listItems, out int matchedListEntries)
        {
            var movieIds = new List<string>();
            var seriesIds = new List<string>();

            foreach (var listItem in listItems)
            {
                var tmdbId = listItem.Id.ToString(CultureInfo.InvariantCulture);
                if (listItem.MediaType == TmdbMediaType.Movie)
                {
                    movieIds.Add(tmdbId);
                }
                else if (listItem.MediaType == TmdbMediaType.Tv)
                {
                    seriesIds.Add(tmdbId);
                }
            }

            var moviesByTmdbId = GroupByTmdbId(QueryByTmdbIds(BaseItemKind.Movie, movieIds));
            var seriesByTmdbId = GroupByTmdbId(QueryByTmdbIds(BaseItemKind.Series, seriesIds));

            matchedListEntries = 0;
            var itemIds = new List<Guid>();

            foreach (var listItem in listItems)
            {
                var tmdbId = listItem.Id.ToString(CultureInfo.InvariantCulture);
                var lookup = listItem.MediaType switch
                {
                    TmdbMediaType.Movie => moviesByTmdbId,
                    TmdbMediaType.Tv => seriesByTmdbId,
                    _ => null
                };

                if (lookup is null || !lookup.TryGetValue(tmdbId, out var matches))
                {
                    continue;
                }

                matchedListEntries++;

                foreach (var match in matches)
                {
                    if (!itemIds.Contains(match))
                    {
                        itemIds.Add(match);
                    }
                }
            }

            return itemIds;
        }

        private IReadOnlyList<BaseItem> QueryByTmdbIds(BaseItemKind itemKind, IReadOnlyList<string> tmdbIds)
        {
            if (tmdbIds.Count == 0)
            {
                return [];
            }

            return _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [itemKind],
                HasAnyProviderIds = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    [MetadataProvider.Tmdb.ToString()] = tmdbIds.Distinct(StringComparer.Ordinal).ToArray()
                },
                IsVirtualItem = false,
                CollapseBoxSetItems = false,
                Recursive = true
            });
        }

        private static Dictionary<string, List<Guid>> GroupByTmdbId(IReadOnlyList<BaseItem> items)
        {
            var lookup = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);

            foreach (var item in items)
            {
                if (!item.TryGetProviderId(MetadataProvider.Tmdb, out var tmdbId))
                {
                    continue;
                }

                if (!lookup.TryGetValue(tmdbId, out var ids))
                {
                    ids = new List<Guid>();
                    lookup[tmdbId] = ids;
                }

                ids.Add(item.Id);
            }

            return lookup;
        }
    }
}
