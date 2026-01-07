using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;

namespace Emby.Server.Implementations.Library.SimilarItems;

/// <summary>
/// Provides similar items for music albums.
/// </summary>
public class MusicAlbumSimilarItemsProvider : ISimilarItemsProvider<MusicAlbum>
{
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicAlbumSimilarItemsProvider"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    public MusicAlbumSimilarItemsProvider(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <inheritdoc/>
    public string Name => "Local Genre/Tag";

    /// <inheritdoc/>
    public MetadataPluginType Type => MetadataPluginType.LocalSimilarityProvider;

    /// <inheritdoc/>
    public IReadOnlyList<BaseItem> GetSimilarItems(MusicAlbum item, SimilarItemsQuery query)
    {
        var internalQuery = new InternalItemsQuery(query.User)
        {
            Genres = item.Genres,
            Tags = item.Tags,
            Limit = query.Limit,
            DtoOptions = query.DtoOptions ?? new DtoOptions(),
            ExcludeItemIds = [.. query.ExcludeItemIds],
            ExcludeArtistIds = [.. query.ExcludeArtistIds],
            IncludeItemTypes = [BaseItemKind.MusicAlbum],
            EnableGroupByMetadataKey = false,
            EnableTotalRecordCount = true,
            OrderBy = [(ItemSortBy.Random, SortOrder.Ascending)]
        };

        return _libraryManager.GetItemList(internalQuery);
    }
}
