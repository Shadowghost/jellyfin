#pragma warning disable RS0030 // Do not use banned APIs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations;
using Jellyfin.Extensions;
using Jellyfin.Server.ServerSetupApp;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Server.Migrations.Routines;

/// <summary>
/// Moves artist credits off the folders a library scanned and onto the by-name entry of that name.
/// </summary>
/// <remarks>
/// A tag names an artist, not a directory. A library split into quality or genre trees holds a folder
/// per tree for one artist, and each was a credit target of its own, so an artist's albums ended up
/// filed under one folder and its tracks under another - whichever id a listing handed the client then
/// answered for only part of the artist. The folders stay exactly as they are, browsable and with their
/// images; they simply stop being what a credit points at.
/// </remarks>
[JellyfinMigration("2026-08-29T00:00:00", nameof(AnchorArtistCreditsOnByNameItems))]
[JellyfinMigrationBackup(JellyfinDb = true)]
public class AnchorArtistCreditsOnByNameItems : IAsyncMigrationRoutine
{
    private const string MusicArtistItemType = "MediaBrowser.Controller.Entities.Audio.MusicArtist";

    private readonly IStartupLogger<AnchorArtistCreditsOnByNameItems> _logger;
    private readonly IDbContextFactory<JellyfinDbContext> _dbContextFactory;
    private readonly ILibraryManager _libraryManager;
    private readonly IItemPersistenceService _persistenceService;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnchorArtistCreditsOnByNameItems"/> class.
    /// </summary>
    /// <param name="logger">The startup logger.</param>
    /// <param name="dbContextFactory">The database context factory.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="persistenceService">The item persistence service.</param>
    public AnchorArtistCreditsOnByNameItems(
        IStartupLogger<AnchorArtistCreditsOnByNameItems> logger,
        IDbContextFactory<JellyfinDbContext> dbContextFactory,
        ILibraryManager libraryManager,
        IItemPersistenceService persistenceService)
    {
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _libraryManager = libraryManager;
        _persistenceService = persistenceService;
    }

    /// <inheritdoc/>
    public async Task PerformAsync(CancellationToken cancellationToken)
    {
        var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            // A scanned folder has a parent; the by-name entry under the metadata path has none.
            var scannedArtistIds = await context.BaseItems
                .AsNoTracking()
                .Where(b => b.Type == MusicArtistItemType && b.ParentId != null)
                .Select(b => b.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (scannedArtistIds.Count == 0)
            {
                _logger.LogInformation("No scanned artist folders, so no credits to move.");
                return;
            }

            var scanned = scannedArtistIds.ToHashSet();

            var anchoredOnFolders = (await context.Peoples
                    .AsNoTracking()
                    .Where(p => p.PersonType == nameof(PersonKind.Artist) || p.PersonType == nameof(PersonKind.AlbumArtist))
                    .Select(p => new { p.Id, p.Name, p.PersonType, p.ItemId })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .Where(p => scanned.Contains(p.ItemId))
                .ToList();

            if (anchoredOnFolders.Count == 0)
            {
                _logger.LogInformation("No artist credits are anchored on a scanned folder.");
                return;
            }

            _logger.LogInformation("Moving {Count} artist credits onto their by-name entry.", anchoredOnFolders.Count);

            // Resolved through the library manager, so a credit lands on exactly the entry the next
            // refresh would write it to, created here if this name never had one.
            var entryByName = new Dictionary<string, Guid>(StringComparer.Ordinal);
            var moved = 0;
            var unresolved = 0;

            foreach (var credit in anchoredOnFolders)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var cleanName = credit.Name.GetCleanValue();
                if (!entryByName.TryGetValue(cleanName, out var entryId))
                {
                    var kind = string.Equals(credit.PersonType, nameof(PersonKind.AlbumArtist), StringComparison.Ordinal)
                        ? PersonKind.AlbumArtist
                        : PersonKind.Artist;

                    var entry = _libraryManager.GetOrCreateCreditItem(credit.Name, kind);
                    if (entry is null)
                    {
                        unresolved++;
                        continue;
                    }

                    entryId = entry.Id;
                    entryByName[cleanName] = entryId;
                }

                var creditId = credit.Id;
                var target = entryId;
                moved += await context.Peoples
                    .Where(p => p.Id == creditId)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.ItemId, target), cancellationToken)
                    .ConfigureAwait(false);
            }

            _logger.LogInformation(
                "Moved {Moved} credits onto {Entries} by-name entries ({Unresolved} could not be resolved).",
                moved,
                entryByName.Count,
                unresolved);

            // The folders an artist was spread over each held a credit row of their own, and those rows
            // now name the same entry. Left apart they would be two credits for one artist.
            var merger = new DuplicatePeopleMerger(_logger, _libraryManager, _persistenceService);
            await merger.MergePeoplesRowsAsync(context, KeyOf, "artist-folder", cancellationToken).ConfigureAwait(false);
        }
    }

    private static string KeyOf(string name) => name.GetCleanValue();
}
