using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Controller.Library;

/// <summary>
/// Folds duplicate items into one.
/// </summary>
public interface IItemMerger
{
    /// <summary>
    /// Points everything referencing the duplicates at the keeper, then deletes them.
    /// </summary>
    /// <param name="keeperId">The item to keep.</param>
    /// <param name="duplicateIds">The items to fold into it.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task.</returns>
    Task MergeAsync(Guid keeperId, IReadOnlyList<Guid> duplicateIds, CancellationToken cancellationToken);
}
