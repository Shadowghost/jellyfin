using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.Library.Validators;

/// <summary>
/// Folds by-name items that a provider gives the same id together.
/// </summary>
internal static class ByNameProviderIdMerge
{
    /// <summary>
    /// Merges the items that share a provider id into the one that was there first.
    /// </summary>
    /// <param name="items">The by-name items, oldest first.</param>
    /// <param name="itemMerger">The item merger.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many items were merged away.</returns>
    /// <remarks>
    /// Two names one provider gives the same id are one entity: a library that changed its metadata
    /// language, or took a name from somewhere else, holds both. Ids that disagree keep them apart, so
    /// only agreement folds them, and the oldest survives to match what resolving a name by hand picks.
    /// </remarks>
    public static async Task<int> MergeAsync(
        IReadOnlyList<BaseItem> items,
        IItemMerger itemMerger,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var keeperOf = new Dictionary<(string Provider, string Value), BaseItem>();
        var duplicatesOf = new Dictionary<Guid, List<BaseItem>>();

        foreach (var item in items)
        {
            if (item.ProviderIds is null)
            {
                continue;
            }

            foreach (var (provider, value) in item.ProviderIds.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                var key = (provider, value);
                if (!keeperOf.TryGetValue(key, out var keeper))
                {
                    keeperOf[key] = item;
                    continue;
                }

                if (!keeper.Id.Equals(item.Id))
                {
                    if (!duplicatesOf.TryGetValue(keeper.Id, out var duplicates))
                    {
                        duplicatesOf[keeper.Id] = duplicates = [];
                    }

                    if (duplicates.TrueForAll(e => !e.Id.Equals(item.Id)))
                    {
                        duplicates.Add(item);
                    }
                }

                break;
            }
        }

        var merged = 0;
        foreach (var (keeperId, duplicates) in duplicatesOf)
        {
            cancellationToken.ThrowIfCancellationRequested();

            logger.LogInformation(
                "Merging {Names} into {Keeper}, which a provider gives the same id.",
                string.Join(", ", duplicates.Select(e => e.Name)),
                items.First(e => e.Id.Equals(keeperId)).Name);

            await itemMerger
                .MergeAsync(keeperId, [.. duplicates.Select(e => e.Id)], cancellationToken)
                .ConfigureAwait(false);

            merged += duplicates.Count;
        }

        return merged;
    }
}
