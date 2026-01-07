using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Configuration;

namespace MediaBrowser.Controller.Library;

/// <summary>
/// Marker interface for similar items providers.
/// </summary>
public interface ISimilarItemsProvider
{
    /// <summary>
    /// Gets the name of the provider.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the type of the provider.
    /// </summary>
    MetadataPluginType Type { get; }
}

/// <summary>
/// Provides similar items for a specific item type.
/// </summary>
/// <typeparam name="TItemType">The type of item this provider handles.</typeparam>
public interface ISimilarItemsProvider<TItemType> : ISimilarItemsProvider
    where TItemType : BaseItem
{
    /// <summary>
    /// Gets similar items for the specified item.
    /// </summary>
    /// <param name="item">The source item to find similar items for.</param>
    /// <param name="query">The query options (user, limit, exclusions, etc.).</param>
    /// <returns>The list of similar items.</returns>
    IReadOnlyList<BaseItem> GetSimilarItems(TItemType item, SimilarItemsQuery query);
}
