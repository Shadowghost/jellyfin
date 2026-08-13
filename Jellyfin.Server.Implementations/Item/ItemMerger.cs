using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Extensions;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Server.Implementations.Item;

/// <inheritdoc />
public class ItemMerger : IItemMerger
{
    private readonly IDbContextFactory<JellyfinDbContext> _dbProvider;
    private readonly ILibraryManager _libraryManager;
    private readonly IItemPersistenceService _persistenceService;
    private readonly ILogger<ItemMerger> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemMerger"/> class.
    /// </summary>
    /// <param name="dbProvider">The database provider.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="persistenceService">The item persistence service.</param>
    /// <param name="logger">The logger.</param>
    public ItemMerger(
        IDbContextFactory<JellyfinDbContext> dbProvider,
        ILibraryManager libraryManager,
        IItemPersistenceService persistenceService,
        ILogger<ItemMerger> logger)
    {
        _dbProvider = dbProvider;
        _libraryManager = libraryManager;
        _persistenceService = persistenceService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task MergeAsync(Guid keeperId, IReadOnlyList<Guid> duplicateIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(duplicateIds);

        if (keeperId.IsEmpty() || duplicateIds.Count == 0)
        {
            return;
        }

        var context = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            foreach (var duplicateId in duplicateIds)
            {
                if (duplicateId.Equals(keeperId))
                {
                    continue;
                }

                await DuplicateItemMerge
                    .RedirectReferencesAsync(context, duplicateId, keeperId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        // Deleted last, so nothing still points at them when they go.
        await DuplicateItemMerge
            .DeleteMergedItemsAsync(duplicateIds, "items", _logger, _libraryManager, _persistenceService, cancellationToken)
            .ConfigureAwait(false);
    }
}
