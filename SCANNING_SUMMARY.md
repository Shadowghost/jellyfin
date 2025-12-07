# Jellyfin Media Scanner - Quick Reference Summary

## Core Architecture

### Main Entry Points
- **LibraryManager** (`Emby.Server.Implementations/Library/LibraryManager.cs`) - Central orchestrator
- **Folder** (`MediaBrowser.Controller/Entities/Folder.cs`) - Recursive validation
- **ItemResolver** - Media type-specific resolvers

### Scanning Pipeline
```
File System → Resolvers (priority-based) → Item Creation → DB Storage → Provider Refresh → Post-Scan Tasks
```

## Media Type Resolution Priority

1. **Priority First** - Special resolvers
2. **Priority Second** - SeriesResolver (TV Shows)
3. **Priority Third** - MusicAlbumResolver
4. **Priority Fourth** - MovieResolver
5. **Priority Fifth** - AudioResolver

## Media Types & File Locations

### Movies
- **Resolver**: `MovieResolver.cs`
- **File**: Video files in movie collection
- **Naming**: `Movie Title (2020).mkv` or in folder with `movie.nfo`
- **Detection**: DVD/Blu-ray folders, individual files, multi-version support

### TV Shows
- **Resolvers**: `SeriesResolver.cs`, `SeasonResolver.cs`, `EpisodeResolver.cs`
- **Files**: Episodes in Series → Season → Episode hierarchy
- **Naming Patterns**: 
  - `s01e01` (Season 1, Episode 1)
  - `1x01` (alternative)
  - `2020-01-15` (by-date)
- **Detection**: `tvshow.nfo`, `season.nfo`, episode file patterns

### Music
- **Resolvers**: `MusicAlbumResolver.cs`, `AudioResolver.cs`, `MusicArtistResolver.cs`
- **Files**: Artist → Album → Track hierarchy
- **Detection**: Folder structure, audio file extensions, multi-disc detection
- **Supported**: MP3, FLAC, AAC, M4A, WMA, Opus, etc.

### Photos
- **Resolver**: `PhotoResolver.cs`
- **Files**: Image files in photo collection
- **Ignored**: `folder`, `thumb`, `poster`, `fanart`, `backdrop`, etc.
- **Supported**: JPG, PNG, GIF, WebP, BMP, etc.

### Books
- **Resolver**: `BookResolver.cs`
- **Formats**: EPUB, PDF, MOBI, AZW, CBZ, CBR, etc.
- **Detection**: Single book per directory OR individual files in collection

### AudioBooks
- **Parser**: `AudioBookFilePathParser.cs`
- **Naming**: `Title - Part 01.mp3` or `Title - Chapter 01.mp3`
- **Supported**: MP3, M4B, FLAC, AAC, Opus, etc.

## Metadata Extraction

### Local Sources
- **NFO Files**: XBMC XML format (movie.nfo, tvshow.nfo, season.nfo, etc.)
- **File Tags**: ID3v2 (MP3), Vorbis (FLAC), iTunes (M4A), etc.
- **External Files**: 
  - Subtitles: `.srt`, `.ass`, `.ssa`, `.vtt`
  - Audio: `.aac`, `.ac3`, `.flac`, `.opus`
  - Pattern: `basename[.language[.flags]].ext`

### Provider Integration
- **TMDB**: Movies and TV shows (primary)
- **TVDB**: TV shows (fallback)
- **MusicBrainz**: Music metadata
- **AudioDB**: Music artwork
- **Priority**: Local → Remote → Parsed from filename

## File Organization Patterns

### Multi-Version Movies
```
Movie Title (2020)/
├── Movie Title (2020).mkv (main)
├── Movie Title (2020) - UHD.mkv (alternate)
└── Movie Title (2020) - Extended.mkv (alternate)
```

### 3D Detection
- `Movie.3D.mkv`, `Movie.SBS.mkv`, `Movie.TAB.mkv`, `Movie.HSBS.mkv`

### Extras
- `Movie-trailer.mkv`, `Movie-featurette.mkv`, `Movie-deleted.mkv`
- Automatically detected and linked to parent item

### Provider IDs in Paths
- `Movie [imdbid=tt1234567].mkv`
- `Movie [tmdbid=12345].mkv`
- Parsed during scan for metadata pre-linking

## Performance Optimization

### Parallelization
- **Sequential**: File resolution, item comparison
- **Parallel**: Subfolder validation, metadata refresh via `LimitedConcurrencyLibraryScheduler`
- **Async**: Provider refresh in background queue

### Caching Levels
1. **In-Memory LRU Cache** - Recently accessed items
2. **Database Cache** - Retrieved at scan start
3. **Directory Service Cache** - File listings per directory

### Scanning Strategies
- **Full Scan**: Every 12 hours (default), ~O(n) complexity
- **Incremental**: File watcher changes, ~O(1) complexity
- **Large Libraries**: 10,000+ items benefit from staggered refresh

## Key Code Paths

### Adding New Media Type
1. Create resolver in `Resolvers/{Type}/`
2. Implement `IItemResolver` or add to `IMultiItemResolver`
3. Set appropriate `ResolverPriority`
4. Naming parser in `Emby.Naming/{Type}/`
5. Register in DI container

### During Scan (GetNonCachedChildren)
```
DirectoryService.GetFileSystemEntries()
  ↓
LibraryManager.ResolvePaths()
  ↓
For each file/folder:
  - Apply resolvers in priority order
  - Multi-item resolution for groups
  ↓
Compare with cached items
  - Match: Update if changed
  - New: Add to DB
  - Removed: Delete from DB
```

### Post-Scan Tasks
Located in `/Emby.Server.Implementations/Library/Validators/`:
- ArtistsPostScanTask
- GenresPostScanTask
- StudiosPostScanTask
- MusicGenresPostScanTask
- CollectionPostScanTask
- ChannelPostScanTask

## Configuration

### NamingOptions (`Emby.Naming/Common/NamingOptions.cs`)
- Video extensions, audio extensions
- Episode expression patterns
- Video flag delimiters
- Album stacking prefixes
- 3D format patterns
- Extra naming rules

### Library Options (per-collection)
- `EnablePhotos` - Include photos in home videos
- `EnableArchiveSupport` - Support archive formats
- `SaveArtworkInMediaFolders` - Store metadata locally
- `ExtractChapterImages` - Extract video chapters

## Troubleshooting

### Items Not Detected
1. Check collection type is correct
2. Verify file extensions are in supported list
3. Check naming convention matches parser patterns
4. Look for files that should be ignored (samples, extras)

### Metadata Not Fetching
1. Verify provider is not disabled
2. Check API keys configured (for remote providers)
3. Look for provider-specific errors in logs
4. Ensure NFO files exist and are valid if using local

### Performance Issues
1. Disable unnecessary providers
2. Increase `LimitedConcurrencyLibraryScheduler` threads (carefully)
3. Run full scan during off-peak hours
4. Check database indexes are present
5. Monitor disk I/O and memory usage

## Important Files & Locations

```
Emby.Server.Implementations/Library/
├── LibraryManager.cs (main orchestrator)
├── Resolvers/ (media type detection)
│   ├── Movies/MovieResolver.cs
│   ├── TV/SeriesResolver.cs, EpisodeResolver.cs
│   ├── Audio/AudioResolver.cs, MusicAlbumResolver.cs
│   ├── PhotoResolver.cs
│   └── Books/BookResolver.cs
└── Validators/ (post-scan tasks)

Emby.Naming/
├── Video/ (video file parsing)
├── TV/ (episode/series naming)
├── Audio/ (album parsing)
└── AudioBook/ (audiobook parsing)

MediaBrowser.Providers/
├── Manager/ProviderManager.cs
└── MediaInfo/MediaInfoResolver.cs (external files/streams)

MediaBrowser.XbmcMetadata/Providers/ (NFO parsing)
```

## API Integration Points

### Manual Scan Trigger
```
POST /Library/Refresh
```

### Get Scan Status
```
GET /Library (includes IsScanRunning property)
```

### Virtual Folders (Collections)
```
POST /Library/VirtualFolders
GET /Library/VirtualFolders
```

## Notes

- Scanner runs automatically on startup, file changes, and scheduled intervals
- During scan: LibraryMonitor paused to prevent race conditions
- Item IDs are generated deterministically from paths
- Metadata refresh is async and queued, doesn't block scan completion
- Database is always updated immediately on file system changes
- Provider refresh happens after initial item creation/detection

