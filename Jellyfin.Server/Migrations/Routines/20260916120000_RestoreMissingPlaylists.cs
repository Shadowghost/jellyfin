using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Emby.Server.Implementations.Playlists;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using LinkedChildType = Jellyfin.Database.Implementations.Entities.LinkedChildType;

namespace Jellyfin.Server.Migrations.Routines;

/// <summary>
/// Recreates playlists whose folder is still on disk but whose item is no longer in the database.
/// </summary>
[JellyfinMigration("2026-09-16T12:00:00", nameof(RestoreMissingPlaylists))]
[JellyfinMigrationBackup(JellyfinDb = true)]
internal class RestoreMissingPlaylists : IAsyncMigrationRoutine
{
    private const string PlaylistFileName = "playlist.xml";

    private readonly ILogger<RestoreMissingPlaylists> _logger;
    private readonly IDbContextFactory<JellyfinDbContext> _dbProvider;
    private readonly ILibraryManager _libraryManager;
    private readonly IServerApplicationHost _appHost;
    private readonly IServerApplicationPaths _appPaths;

    public RestoreMissingPlaylists(
        ILoggerFactory loggerFactory,
        IDbContextFactory<JellyfinDbContext> dbProvider,
        ILibraryManager libraryManager,
        IServerApplicationHost appHost,
        IServerApplicationPaths appPaths)
    {
        _logger = loggerFactory.CreateLogger<RestoreMissingPlaylists>();
        _dbProvider = dbProvider;
        _libraryManager = libraryManager;
        _appHost = appHost;
        _appPaths = appPaths;
    }

    /// <inheritdoc />
    public async Task PerformAsync(CancellationToken cancellationToken)
    {
        var playlistsPath = Path.Combine(_appPaths.DataPath, "playlists");
        if (!Directory.Exists(playlistsPath))
        {
            return;
        }

        var playlistsFolderId = _libraryManager.GetNewItemId(playlistsPath, typeof(PlaylistsFolder));
        if (_libraryManager.GetItemById(playlistsFolderId) is not Folder playlistsFolder)
        {
            // Without that folder there is nothing to parent a playlist to, and the next scan
            // recreates both anyway.
            _logger.LogInformation("No playlists folder in the library, skipping playlist recovery.");
            return;
        }

        var context = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var idByPath = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
#pragma warning disable RS0030 // Do not use banned APIs
            var storedItems = await context.BaseItems
                .Where(b => b.Path != null)
                .Select(b => new { b.Id, b.Path })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var storedIds = await context.BaseItems
                .Select(b => b.Id)
                .ToHashSetAsync(cancellationToken)
                .ConfigureAwait(false);
#pragma warning restore RS0030 // Do not use banned APIs

            foreach (var item in storedItems)
            {
                idByPath.TryAdd(_appHost.ExpandVirtualPath(item.Path!), item.Id);
            }

            // A playlist whose item is still there under a path that no longer matches its folder is
            // not missing, and recreating it would collide with the row that already holds that id.
            var missing = Directory.EnumerateDirectories(playlistsPath)
                .Where(dir => !idByPath.ContainsKey(dir)
                    && !storedIds.Contains(_libraryManager.GetNewItemId(dir, typeof(Playlist))))
                .OrderBy(dir => dir, StringComparer.Ordinal)
                .ToList();

            if (missing.Count == 0)
            {
                _logger.LogInformation("Every playlist folder still has an item, nothing to restore.");
                return;
            }

            _logger.LogInformation("Found {Count} playlist folders without an item, restoring them.", missing.Count);

            var restoredEntries = 0;
            foreach (var dir in missing)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var playlist = new Playlist
                {
                    Path = dir,
                    Name = Path.GetFileName(dir),
                    // Ownership is not recorded on disk, so the playlist comes back shared, which is
                    // what a library scan would have recreated it as.
                    OpenAccess = true,
                    Id = _libraryManager.GetNewItemId(dir, typeof(Playlist)),
                    DateCreated = Directory.GetCreationTimeUtc(dir),
                    DateModified = Directory.GetLastWriteTimeUtc(dir)
                };

                playlist.SetMediaType(MediaType.Audio);
                _libraryManager.CreateItem(playlist, playlistsFolder);

                var metadataPath = Path.Combine(dir, PlaylistFileName);
                var sortOrder = 0;
                foreach (var storedPath in File.Exists(metadataPath) ? ReadEntryPaths(metadataPath) : [])
                {
                    if (!idByPath.TryGetValue(storedPath, out var childId))
                    {
                        continue;
                    }

                    context.LinkedChildren.Add(new LinkedChildEntity
                    {
                        ParentId = playlist.Id,
                        ChildId = childId,
                        ChildType = LinkedChildType.Manual,
                        SortOrder = sortOrder
                    });

                    sortOrder++;
                }

                restoredEntries += sortOrder;
                _logger.LogInformation("Restored playlist {Name} with {Count} entries.", playlist.Name, sortOrder);
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Restored {PlaylistCount} playlists holding {EntryCount} entries.",
                missing.Count,
                restoredEntries);
        }
    }

    private List<string> ReadEntryPaths(string metadataPath)
    {
        var paths = new List<string>();
        var settings = new XmlReaderSettings
        {
            IgnoreComments = true,
            IgnoreWhitespace = true,
            IgnoreProcessingInstructions = true,
            DtdProcessing = DtdProcessing.Prohibit
        };

        try
        {
            using var reader = XmlReader.Create(metadataPath, settings);
            var inEntry = false;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (string.Equals(reader.Name, "PlaylistItem", StringComparison.Ordinal))
                {
                    inEntry = true;
                }
                else if (inEntry && string.Equals(reader.Name, "Path", StringComparison.Ordinal))
                {
                    inEntry = false;
                    var value = reader.ReadElementContentAsString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        paths.Add(value.Trim());
                    }
                }
            }
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not read playlist metadata {MetadataPath}.", metadataPath);
        }

        return paths;
    }
}
