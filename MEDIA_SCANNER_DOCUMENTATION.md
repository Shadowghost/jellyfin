# Jellyfin Media Scanner Architecture - Comprehensive Documentation

## Overview

The Jellyfin media scanner is a sophisticated, multi-threaded system designed to efficiently discover, identify, and catalog media files across various collection types. It integrates file system monitoring, intelligent naming convention parsing, metadata extraction, and provider-based enrichment into a cohesive scanning pipeline.

---

## 1. Overall Scanning Architecture

### 1.1 Main Components

#### Core Classes
- **LibraryManager** (`/opt/src/jellyfin/Emby.Server.Implementations/Library/LibraryManager.cs`)
  - Central orchestrator for all library operations
  - Manages resolvers, post-scan tasks, and folder validation
  - Caches items using LRU cache (`FastConcurrentLru<Guid, BaseItem>`)
  - Thread-safe root folder access with locks

- **Folder** (`/opt/src/jellyfin/MediaBrowser.Controller/Entities/Folder.cs`)
  - Represents directories in the library hierarchy
  - Implements recursive validation via `ValidateChildren()`
  - Uses `LimitedConcurrencyLibraryScheduler` for parallel processing
  - Manages child item resolution and metadata refresh

- **ItemResolver** (`/opt/src/jellyfin/MediaBrowser.Controller/Resolvers/IItemResolver.cs`)
  - Base interface for all item type-specific resolvers
  - Each resolver has a priority (First through Fifth)
  - Returns `BaseItem` or null from `ResolvePath()`
  - Supports multi-item resolution via `IMultiItemResolver`

#### Interfaces
```csharp
public interface IItemResolver
{
    ResolverPriority Priority { get; }
    BaseItem? ResolvePath(ItemResolveArgs args);
}

public interface IMultiItemResolver
{
    MultiItemResolverResult ResolveMultiple(
        Folder parent,
        List<FileSystemMetadata> files,
        CollectionType? collectionType,
        IDirectoryService directoryService);
}
```

### 1.2 Scanning Workflow

#### High-Level Process Flow

```
1. Trigger Scan (Manual, Scheduled, or File Watcher)
   ↓
2. ValidateMediaLibrary() / ValidateMediaLibraryInternal()
   ↓
3. LibraryMonitor.Stop() (Pause file watching)
   ↓
4. PerformLibraryValidation()
   ├─ ValidateTopLibraryFolders()
   │  └─ Refresh collection folder metadata
   │
   ├─ RootFolder.ValidateChildren() (recursive)
   │  ├─ GetNonCachedChildren()
   │  │  └─ LibraryManager.ResolvePaths()
   │  │     └─ Apply resolvers in priority order
   │  │
   │  ├─ Compare with cached children (in-memory DB)
   │  │  ├─ Match: Update if changed
   │  │  ├─ New: Add to DB
   │  │  └─ Removed: Delete from DB
   │  │
   │  ├─ ValidateSubFolders() (recursive)
   │  │  └─ RunTasks() via LimitedConcurrencyLibraryScheduler
   │  │
   │  └─ RefreshMetadataRecursive() / RefreshAllMetadataForContainer()
   │     └─ Provider-based metadata enrichment
   │
   └─ RunPostScanTasks()
      ├─ ArtistsPostScanTask
      ├─ GenresPostScanTask
      ├─ StudiosPostScanTask
      └─ etc.
   
5. LibraryMonitor.Start() (Resume file watching)
```

#### Key Methods

**LibraryManager.ValidateMediaLibraryInternal()**
```csharp
public async Task ValidateMediaLibraryInternal(IProgress<double> progress, CancellationToken cancellationToken)
{
    IsScanRunning = true;
    LibraryMonitor.Stop();
    
    try
    {
        await PerformLibraryValidation(progress, cancellationToken);
    }
    finally
    {
        LibraryMonitor.Start();
        IsScanRunning = false;
    }
}
```

**Folder.GetNonCachedChildren()**
```csharp
protected virtual IEnumerable<BaseItem> GetNonCachedChildren(IDirectoryService directoryService)
{
    var collectionType = LibraryManager.GetContentType(this);
    var libraryOptions = LibraryManager.GetLibraryOptions(this);
    
    return LibraryManager.ResolvePaths(
        GetFileSystemChildren(directoryService),
        directoryService,
        this,
        libraryOptions,
        collectionType);
}
```

### 1.3 Library Scan Triggering

#### Manual Triggers
- API endpoint: `POST /Library/Refresh` in LibraryController
- Queues `RefreshMediaLibraryTask`

#### Scheduled Triggers
- **RefreshMediaLibraryTask** (`/opt/src/jellyfin/Emby.Server.Implementations/ScheduledTasks/Tasks/RefreshMediaLibraryTask.cs`)
  - Default interval: Every 12 hours
  - Executes via task scheduler
  - Can be configured by users

#### File Watcher Triggers
- **LibraryMonitor** (`/opt/src/jellyfin/Emby.Server.Implementations/IO/LibraryMonitor.cs`)
  - Monitors file system changes in real-time
  - Detects file additions, deletions, modifications
  - Triggers incremental rescans
  - Paused during full scan to prevent conflicts

### 1.4 Database Integration

#### Item Repository
- All items stored in entity database
- Uses EF Core with SQLite provider
- **Key tables**: Items, ItemMetadata, ItemImages, MediaStreams

#### Caching Strategy
1. **In-Memory Cache**: LRU cache in LibraryManager for frequently accessed items
2. **Database Cache**: Full item list retrieved during scan start
3. **Cache Invalidation**: Manual triggers via LibraryManager methods

#### Flow with DB
```
File System → Resolve → Compare with DB Cache → Update/Create/Delete → Save to DB
                                                      ↓
                                            Provider Refresh (async)
                                                      ↓
                                            Update DB with metadata
```

---

## 2. Media Type-Specific Scanning

### 2.1 Movie Scanning

#### Movie Resolver
**File**: `/opt/src/jellyfin/Emby.Server.Implementations/Library/Resolvers/Movies/MovieResolver.cs`

**Characteristics**:
- Priority: Fourth (executed after Series, Books, Music)
- Implements both `IItemResolver` and `IMultiItemResolver`
- Resolves both single-file and multi-file movies

**Valid Collection Types**:
- `movies`, `homevideos`, `musicvideos`, `tvshows`, `photos`

**Resolution Logic**:
```csharp
// For movies with own folders
if (args.IsDirectory)
{
    // Check for DVD/Blu-ray directories
    if (IsDvdDirectory(path)) → VideoType.Dvd
    if (IsBluRayDirectory(path)) → VideoType.BluRay
    
    // Check for multi-disc movies
    // Then resolve video files
}

// For single movie files
if (!args.IsDirectory)
{
    // Resolve video file
    ResolveVideo<Movie>(args, true);
}
```

**File Detection**:
- Uses `VideoResolver.Resolve()` from `Emby.Naming.Video`
- Supported extensions configured in `NamingOptions.VideoFileExtensions`
- Filters out "sample" files via regex

**Multi-Version Support**:
- Detects alternate versions (e.g., different editions, qualities)
- Stores in `LocalAlternateVersions` and `AdditionalParts`

**Example Movie Structure**:
```
Movies/
├── Movie Title (2020) [imdbid=tt1234567]/
│   ├── Movie Title (2020).mkv
│   ├── Movie Title (2020)-bluray.mkv  (alternate version)
│   └── movie.nfo
├── Another Movie.mp4
└── Folder/
    ├── BDMV/  (Blu-ray structure)
    │   └── ...
```

### 2.2 TV Show/Series Scanning

#### Hierarchy
```
Series (tvshow.nfo)
  ├─ Season (season.nfo)
  │   └─ Episode
  └─ Season
      └─ Episode
```

#### Series Resolver
**File**: `/opt/src/jellyfin/Emby.Server.Implementations/Library/Resolvers/TV/SeriesResolver.cs`

**Priority**: Second (high priority)

**Detection**:
```csharp
// Look for tvshow.nfo file in directory
if (args.ContainsFileSystemEntryByName("tvshow.nfo"))
{
    return new Series { Path = args.Path };
}

// Or use naming conventions from Emby.Naming.TV.SeriesResolver
var seriesInfo = Naming.TV.SeriesResolver.Resolve(_namingOptions, args.Path);
```

#### Season Resolver
**File**: `/opt/src/jellyfin/Emby.Server.Implementations/Library/Resolvers/TV/SeasonResolver.cs`

**Detection**:
- `season.nfo` files
- Folder naming: `Season 1`, `Specials`, etc.
- Parsed from folder names using `SeasonPathParser`

#### Episode Resolver
**File**: `/opt/src/jellyfin/Emby.Server.Implementations/Library/Resolvers/TV/EpisodeResolver.cs`

**Detection Rules**:
- Must have Season or Series parent
- Parsed via `Emby.Naming.TV.EpisodeResolver`

**Naming Convention Parser**: `/opt/src/jellyfin/Emby.Naming/TV/EpisodePathParser.cs`

**Supported Episode Formats**:
```
s01e01           Standard: Season 1, Episode 1
1x01             Alternative format
S01E01           Uppercase variant
01x01            Alternative with leading zeros
S01E01-E05       Episode range: Episodes 1-5
1x01-05          Range alternative format
2020-01-15       By-date format (if enabled)
```

**Episode Info Extracted**:
```csharp
public class EpisodeInfo
{
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public int? EndingEpisodeNumber { get; set; }
    public string SeriesName { get; set; }
    public bool IsByDate { get; set; }
    public int? Year { get; set; }
    public int? Month { get; set; }
    public int? Day { get; set; }
    public bool Is3D { get; set; }
    public string Format3D { get; set; }
    public bool IsStub { get; set; }
    public string StubType { get; set; }
}
```

**Example TV Structure**:
```
TV Shows/
├── Breaking Bad/
│   ├── tvshow.nfo
│   ├── Season 1/
│   │   ├── season.nfo
│   │   ├── Breaking Bad - s01e01 - Pilot.mkv
│   │   ├── Breaking Bad - s01e02 - Cat's in the Bag.mkv
│   │   └── Breaking Bad - s01e02 - Cat's in the Bag.srt
│   └── Season 2/
│       └── ...
```

### 2.3 Music Scanning

#### Music Album Resolver
**File**: `/opt/src/jellyfin/Emby.Server.Implementations/Library/Resolvers/Audio/MusicAlbumResolver.cs`

**Priority**: Third

**Album Detection**:
```csharp
public bool IsMusicAlbum(ItemResolveArgs args)
{
    // Must be in music collection
    if (collectionType != CollectionType.music) return null;
    
    // Must be a directory
    if (!args.IsDirectory) return null;
    
    // Must not be nested album
    if (args.HasParent<MusicAlbum>()) return null;
    
    // Must contain audio files
    return ContainsMusic(args.FileSystemChildren);
}
```

#### Audio Track Resolver
**File**: `/opt/src/jellyfin/Emby.Server.Implementations/Library/Resolvers/Audio/AudioResolver.cs`

**Priority**: Fifth (lowest for audio)

**Detection**:
- Audio file extensions from `NamingOptions.AudioFileExtensions`
- Typical: `.mp3`, `.flac`, `.opus`, `.aac`, `.m4a`, `.wma`, etc.

#### Album Parsing
**File**: `/opt/src/jellyfin/Emby.Naming/Audio/AlbumParser.cs`

**Multi-Part Detection**:
```csharp
// Detects multi-part albums like "CD 1", "Disc 1"
public bool IsMultiPart(string path)
{
    // Checks for stacking prefixes in NamingOptions.AlbumStackingPrefixes
    // Examples: "CD 1", "Disc 1", "Part 1"
}
```

#### Music Artist Resolver
**File**: `/opt/src/jellyfin/Emby.Server.Implementations/Library/Resolvers/Audio/MusicArtistResolver.cs`

**Hierarchy**:
```
MusicArtist (folder)
  └─ MusicAlbum (subfolder)
     └─ Audio files (tracks)
```

**Example Music Structure**:
```
Music/
├── Artist Name/
│   ├── Album 1 (2020)/
│   │   ├── 01 - Track Title.flac
│   │   ├── 02 - Another Track.flac
│   │   └── album.nfo
│   └── Album 2 (2021)/
│       └── ...
```

### 2.4 Photo/Image Scanning

#### Photo Resolver
**File**: `/opt/src/jellyfin/Emby.Server.Implementations/Library/Resolvers/PhotoResolver.cs`

**Detection**:
```csharp
if (collectionType == CollectionType.photos 
    || (collectionType == CollectionType.homevideos && libraryOptions.EnablePhotos))
{
    if (IsImageFile(args.Path, imageProcessor))
    {
        // Check it's not owned by a video file
        if (!IsOwnedByMedia(file))
        {
            return new Photo { Path = args.Path };
        }
    }
}
```

**Supported Formats**:
- Controlled by `IImageProcessor.SupportedInputFormats`
- Typically: `.jpg`, `.jpeg`, `.png`, `.gif`, `.webp`, `.bmp`, etc.

**Ignored Filenames**:
```csharp
private static readonly string[] _ignoreFiles = new[]
{
    "folder",      // Folder thumbnails
    "thumb",       // Thumbnails
    "landscape",   // Wide artwork
    "fanart",      // Fanart images
    "backdrop",    // Backdrops
    "poster",      // Poster images
    "cover",       // Album covers
    "logo",        // Logos
    "default"      // Default images
};
```

**Ownership Detection**:
- If image filename matches video filename, it's treated as metadata
- Example: `movie.mkv` owns `movie.jpg`, `movie-poster.jpg`

**Example Photo Structure**:
```
Photos/
├── Vacation 2024/
│   ├── photo001.jpg
│   ├── photo002.jpg
│   └── folder.jpg (ignored)
```

### 2.5 Book/eBook Scanning

#### Book Resolver
**File**: `/opt/src/jellyfin/Emby.Server.Implementations/Library/Resolvers/Books/BookResolver.cs`

**Supported Formats**:
```csharp
private readonly string[] _validExtensions = 
{ 
    ".azw",    // Amazon Kindle
    ".azw3",   // Kindle Format 8
    ".cb7",    // Comic Book 7-zip
    ".cbr",    // Comic Book RAR
    ".cbt",    // Comic Book TAR
    ".cbz",    // Comic Book ZIP
    ".epub",   // EPUB format
    ".mobi",   // Mobipocket
    ".pdf"     // PDF documents
};
```

**Detection**:
```csharp
if (collectionType != CollectionType.books) return null;

if (args.IsDirectory)
{
    // Directory with exactly one book file → treat as book
    var bookFiles = args.FileSystemChildren
        .Where(f => _validExtensions.Contains(Path.GetExtension(f.FullName)))
        .ToList();
    
    if (bookFiles.Count == 1)
        return new Book { Path = bookFiles[0].FullName };
}
else
{
    // Single book file
    if (_validExtensions.Contains(Path.GetExtension(args.Path)))
        return new Book { Path = args.Path };
}
```

**Example Book Structure**:
```
Books/
├── Author Name/
│   ├── Book Title.epub
│   └── Another Book.pdf
├── Comics/
│   └── Comic Title.cbz
```

### 2.6 AudioBook Scanning

#### AudioBook Resolver
**File**: `/opt/src/jellyfin/Emby.Naming/AudioBook/AudioBookResolver.cs`

**File Detection**:
- Uses `NamingOptions.AudioFileExtensions`
- Supports: `.mp3`, `.m4b`, `.opus`, `.flac`, `.aac`, etc.

**AudioBook File Parser**: `/opt/src/jellyfin/Emby.Naming/AudioBook/AudioBookFilePathParser.cs`

**Info Extracted**:
```csharp
public class AudioBookFileInfo
{
    public string Path { get; set; }
    public string Container { get; set; }
    public int? PartNumber { get; set; }
    public int? ChapterNumber { get; set; }
}
```

**Naming Patterns**:
```
AudiobookTitle - Part 01.mp3
AudiobookTitle - Chapter 01.mp3
AudiobookTitle - 01.mp3
```

---

## 3. File Organization & Detection

### 3.1 File Discovery Pipeline

#### DirectoryService
- Lists all files in directory (optionally recursive)
- Supports caching
- Used by all resolvers

#### Resolution Path
```
Directory Contents → File/Folder Analysis
                           ↓
                    Apply Resolvers in Priority Order
                           ↓
                    Priority 1: (Special handling)
                           ↓
                    Priority 2: SeriesResolver
                           ↓
                    Priority 3: MusicAlbumResolver
                           ↓
                    Priority 4: MovieResolver
                           ↓
                    Priority 5: AudioResolver
                           ↓
                    Multi-item resolution for groups
```

### 3.2 Naming Convention Parsers

All parsers located in `/opt/src/jellyfin/Emby.Naming/`

#### Video Naming
**File**: `Video/VideoResolver.cs`
```csharp
VideoFileInfo? Resolve(
    string path,
    bool isDirectory,
    NamingOptions namingOptions,
    bool parseName = true,
    string? libraryRoot = "")
```

**Extracts**:
- Container/extension
- 3D format (if present)
- Stub type
- File name and extension

#### Episode/Series Naming
**Files**: 
- `TV/EpisodePathParser.cs`
- `TV/SeriesPathParser.cs`
- `TV/SeasonPathParser.cs`
- `TV/TvParserHelpers.cs`

**Supported Expressions** (configurable in `NamingOptions`):
```
// Standard
s01e01, S01E01, 1x01
// With range
s01e01-e05, 1x01-05
// By date
2020-01-15
// With show name
Show Name s01e01
```

### 3.3 Multi-Version & Quality Detection

#### Multi-Version Movies
Detected by `MovieResolver` and `VideoResolver`:
```csharp
// Multiple files with same base name (different editions/qualities)
Movie (2020).mkv        → Main version
Movie (2020) - UHD.mkv  → Alternate version
Movie (2020) - 4K.mkv   → Another alternate

// Result
videoItem.LocalAlternateVersions = [
    "path/to/Movie (2020) - UHD.mkv",
    "path/to/Movie (2020) - 4K.mkv"
];
```

#### 3D Detection
**File**: `Emby.Naming/Video/Format3DParser.cs`

**Recognized 3D Patterns**:
```
Movie.3D.mkv
Movie.SBS.mkv        (Side-by-side)
Movie.TAB.mkv        (Top-and-bottom)
Movie.HSBS.mkv       (Half SBS)
Movie.HTAB.mkv       (Half TAB)
```

**Extracted Info**:
```csharp
public class Format3DResult
{
    public bool Is3D { get; set; }
    public string? Format3D { get; set; }
}
```

#### Extra Content Detection
**File**: `/opt/src/jellyfin/Emby.Server.Implementations/Library/Resolvers/ExtraResolver.cs`

**Naming Rules** (Emby.Naming/Video/ExtraRuleResolver.cs):
```
Trailers:
  Movie-trailer.mkv
  Movie-preview.mkv
  
Behind the Scenes:
  Movie-behindthescenes.mkv
  
Deleted Scenes:
  Movie-deleted.mkv
  
Interviews:
  Movie-interview.mkv
  
Featurettes:
  Movie-featurette.mkv
  
Shorts:
  Movie-short.mkv
```

**Example Structure with Extras**:
```
Movies/
├── Movie Title (2020)/
│   ├── Movie Title (2020).mkv
│   ├── Movie Title (2020)-trailer.mkv
│   ├── Movie Title (2020)-featurette.mkv
│   ├── Trailers/
│   │   └── Another Trailer.mkv
│   └── movie.nfo
```

### 3.4 Provider ID Extraction from Paths

#### IMDB IDs
```
Movie [imdbid=tt1234567].mkv
Movie Title (tt1234567).mkv
```

#### TMDB IDs
```
Movie [tmdbid=12345].mkv
Movie Title (12345).mkv
```

**Extraction Logic** (in `MovieResolver.SetProviderIdsFromPath()`):
```csharp
var justName = item.IsInMixedFolder 
    ? Path.GetFileName(item.Path) 
    : Path.GetFileName(item.ContainingFolderPath);

var tmdbid = justName.GetAttributeValue("tmdbid");
item.TrySetProviderId(MetadataProvider.Tmdb, tmdbid);

var imdbid = item.Path.GetAttributeValue("imdbid");
item.TrySetProviderId(MetadataProvider.Imdb, imdbid);
```

---

## 4. Metadata Extraction

### 4.1 Local Metadata Sources

#### NFO Files (XBMC Format)
**Directory**: `/opt/src/jellyfin/MediaBrowser.XbmcMetadata/Providers/`

**Providers by Type**:
- `MovieNfoProvider` - Movies
- `EpisodeNfoProvider` - Episodes
- `SeriesNfoProvider` - Series
- `SeasonNfoProvider` - Seasons
- `AlbumNfoProvider` - Music Albums
- `ArtistNfoProvider` - Music Artists

**Base Class**: `/opt/src/jellyfin/MediaBrowser.XbmcMetadata/Providers/BaseNfoProvider.cs`

**File Locations**:
```
Movie Folder/
  ├── movie.nfo (for folder movies)
  ├── Movie Title.nfo (for file movies)

Episode Folder/
  ├── Series.nfo
  ├── Season.nfo
  ├── Episode s01e01.nfo

Music/
  ├── artist.nfo
  ├── album.nfo
  ├── subfolder.nfo
```

**NFO Detection**:
```csharp
// Gets NFO file for item
protected abstract void Fetch(MetadataResult<T> result, string path, CancellationToken cancellationToken);

// Checks if NFO is newer than cached metadata
public bool HasChanged(BaseItem item, IDirectoryService directoryService)
{
    var file = GetXmlFile(...);
    return file.Exists && FileSystem.GetLastWriteTimeUtc(file) > item.DateLastSaved;
}
```

### 4.2 Media Stream Information

#### MediaInfoResolver
**File**: `/opt/src/jellyfin/MediaBrowser.Providers/MediaInfo/MediaInfoResolver.cs`

**Responsibilities**:
1. Detects external audio files (`.aac`, `.ac3`, `.dts`, `.aiff`, `.flac`, `.mp3`, `.opus`, `.wav`, `.wma`, `.weba`)
2. Detects external subtitle files (`.srt`, `.ass`, `.ssa`, `.sub`, `.vtt`, `.sbv`, `.idx`, `.sup`)
3. Parses file flags for language, forced status, hearing impaired, title
4. Detects lyrics files

**External File Detection**:
```csharp
// For video file: movie.mkv
// Looks for:
movie.srt          // English subtitle
movie.en.srt       // English with language code
movie.en.forced.srt    // Forced English
movie.pt.srt       // Portuguese

// For audio: movie.aac, movie.ac3
movie.aac
movie.ac3
movie.mka
```

**Extraction Process**:
```csharp
public IReadOnlyList<ExternalPathParserResult> GetExternalFiles(
    Video video,
    IDirectoryService directoryService,
    bool clearCache)
{
    // 1. Get all files in video folder + internal metadata path
    // 2. Parse each with ExternalPathParser
    // 3. Match against video filename prefix
    // 4. Extract metadata flags from filename
    // 5. Get codec info via MediaEncoder
}
```

**Filename Parsing** (ExternalPathParser):
```
Pattern: basename[.language[.flags]].extension

Examples:
movie.en.srt           → English
movie.fr-CA.srt        → French (Canada)
movie.en.forced.srt    → English, forced
movie.en.sdh.srt       → English, hearing impaired
movie.en.title.srt     → English, with title flag
```

### 4.3 Metadata Providers

#### Provider Manager
**File**: `/opt/src/jellyfin/MediaBrowser.Providers/Manager/ProviderManager.cs`

**Provider Types**:
1. **ILocalMetadataProvider** - NFO, local databases
2. **IRemoteMetadataProvider** - Online sources (TMDB, TVDB, etc.)
3. **ILocalImageProvider** - Local artwork
4. **IRemoteImageProvider** - Online artwork
5. **IDynamicImageProvider** - Generated imagery
6. **IPreRefreshProvider** - Pre-refresh data enrichment
7. **IForcedProvider** - Forced metadata overrides

#### Provider Priority System
```
1. IPreRefreshProvider
   ↓
2. ILocalMetadataProvider (highest priority for local data)
   ↓
3. IRemoteMetadataProvider (if local data insufficient)
   ↓
4. Image Providers
   ↓
5. Metadata Savers (persist data)
```

**Refresh Queue**: Priority queue (`PriorityQueue<(Guid ItemId, MetadataRefreshOptions), RefreshPriority>`)

### 4.4 Tag-Based Metadata (Music/Audio)

**Metadata Extraction**:
- ID3v2 tags for audio files
- Vorbis comments for FLAC
- iTunes tags for M4A
- WMA metadata

**Parsed Fields**:
- Title
- Album
- Artist
- Album Artist
- Genre
- Year
- Track number
- Duration

---

## 5. Provider Integration

### 5.1 How Providers Work During Scanning

#### Metadata Refresh Pipeline

```
Item Resolved from File System
         ↓
    Item Created/Updated in Database
         ↓
    MetadataRefreshOptions set
         ↓
    ProviderManager.RefreshMetadata(item)
         ↓
    For each Provider (by priority):
    ├─ IPreRefreshProvider.GetMetadata()
    ├─ ILocalMetadataProvider.GetMetadata()    ← NFO provider
    ├─ IRemoteMetadataProvider.GetMetadata()   ← TMDB, TVDB
    ├─ IImageProvider.GetImages()
    └─ IMetadataSaver.Save()
         ↓
    Item Updated with metadata
         ↓
    Item saved to database
```

### 5.2 Provider Plugins

#### Built-in Providers
- **TheMovieDb** (TMDB) - Movies, TV Shows
- **TvDb** (TheTVDB) - TV Shows, Episodes
- **MusicBrainz** - Music metadata
- **AudioDB** - Music artwork

#### Provider Execution
```csharp
// ProviderManager queues items for refresh
_refreshQueue.Enqueue(itemId, refreshOptions, priority);

// Background task processes queue
async Task ProcessRefreshQueue()
{
    while (true)
    {
        var (itemId, options) = _refreshQueue.Dequeue();
        
        try
        {
            await RefreshMetadataForItem(itemId, options);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error refreshing {ItemId}", itemId);
        }
    }
}
```

### 5.3 Provider Fallback

#### Multi-Provider Strategy
```
Try Provider 1 (preferred)
    ↓ (if not found or insufficient data)
Try Provider 2 (fallback)
    ↓ (if not found)
Try Provider 3 (last resort)
    ↓ (if still not found)
Use local data only
```

**Example for Movies**:
1. TMDB (primary)
2. IMDB (fallback)
3. Local NFO (always checked)
4. Filename parsing (last resort)

### 5.4 Provider Configuration

#### Disabled Providers
- Configurable per item type
- Stored in server configuration
- Respects user preferences

**Configuration File**: `config/system.xml`
```xml
<DisabledMetadataFetchers>
    <Item>Type1:Provider1</Item>
    <Item>Type2:Provider2</Item>
</DisabledMetadataFetchers>
```

---

## 6. Performance & Optimization

### 6.1 Parallel Scanning

#### LimitedConcurrencyLibraryScheduler

**Location**: Dependency injected into `Folder.LimitedConcurrencyLibraryScheduler`

**Purpose**: Limits concurrent tasks to prevent resource exhaustion

```csharp
// Usage in Folder.cs
private async Task RunTasks<T>(
    Func<T, IProgress<double>, Task> task,
    IList<T> children,
    IProgress<double> progress,
    CancellationToken cancellationToken)
{
    await LimitedConcurrencyLibraryScheduler
        .Enqueue(
            children.ToArray(),
            task,
            progress,
            cancellationToken)
        .ConfigureAwait(false);
}
```

**Parallelization Stages**:
1. **GetNonCachedChildren** - Sequential (resolves all items)
2. **Child Comparison** - Sequential (updates existing items)
3. **ValidateSubFolders** - **PARALLEL** (per subfolder via scheduler)
4. **RefreshMetadataRecursive** - **PARALLEL** (per child via scheduler)
5. **Provider Refresh** - **PARALLEL** (managed by ProviderManager)

### 6.2 Incremental Updates vs Full Scans

#### Full Library Scan
```csharp
// RootFolder.ValidateChildren(recursive: true)
// Traverses entire library tree
// Resolves all items from filesystem
// Updates all changes
```

**Performance Impact**:
- Time: O(n) where n = total items + files
- Memory: High (entire tree in memory)
- Frequency: Default every 12 hours or manual

#### Incremental Updates (via File Watcher)
```csharp
// LibraryMonitor detects changes
// Triggers targeted refresh
// Only affected items re-scanned
```

**Performance Impact**:
- Time: O(1) per change
- Memory: Low
- Frequency: Real-time

### 6.3 Caching Mechanisms

#### Multi-Level Caching

**Level 1: In-Memory Item Cache**
```csharp
private readonly FastConcurrentLru<Guid, BaseItem> _cache;
// LRU cache of recently accessed items
// Prevents redundant DB queries
```

**Level 2: Database Item Cache**
```csharp
// GetCachedChildren() loads items from DB at scan start
protected IReadOnlyList<BaseItem> GetCachedChildren()
{
    return ItemRepository.GetItemList(new InternalItemsQuery
    {
        Parent = this,
        GroupByPresentationUniqueKey = false,
        DtoOptions = new DtoOptions(true)
    });
}
```

**Level 3: Directory Service Cache**
```csharp
// Caches file listings per directory
directoryService.GetFileSystemEntries(path, useCache: true);

// Cleared between scans to ensure fresh data
// Can be cleared on demand via clearCache parameter
```

#### Invalidation Strategy
```
When item created/updated/deleted:
├─ Clear in-memory cache for item
├─ Update database
├─ Invalidate parent folder's cache
└─ Update search indexes
```

### 6.4 Large Library Optimization

#### Strategies for 10,000+ Items

1. **Stagger Metadata Refresh**
   - Don't refresh all items immediately
   - Use priority queue in ProviderManager
   - Spread over days/weeks

2. **Disable Unnecessary Providers**
   - Remove unused metadata sources
   - Configuration in `system.xml`

3. **Increase Concurrency Limits**
   - Configure `LimitedConcurrencyLibraryScheduler` max threads
   - Balance with system resources

4. **Faster Scan Intervals**
   - For file-watcher changes only
   - Avoid scheduled full scans during peak usage

5. **Dedicated Scan Time**
   - Configure scheduled task for off-peak hours
   - Allows 12+ hour scan to complete

### 6.5 Database Query Optimization

#### Item Lookup
```csharp
// Indexed by:
// - Id (primary key)
// - Path (unique per library)
// - Parent Id + Name (for hierarchy)

// Query results cached by Folder.GetCachedChildren()
```

#### Batch Operations
```csharp
// CreateItems() batch inserts
LibraryManager.CreateItems(newItems, parent, cancellationToken);

// UpdateItemsAsync() batch updates
await LibraryManager.UpdateItemsAsync(items, parent, updateReason, cancellationToken);
```

---

## 7. Post-Scan Tasks

### 7.1 Task Pipeline

**Interface**: `/opt/src/jellyfin/MediaBrowser.Controller/Library/ILibraryPostScanTask.cs`

```csharp
public interface ILibraryPostScanTask
{
    Task Run(IProgress<double> progress, CancellationToken cancellationToken);
}
```

### 7.2 Built-in Post-Scan Tasks

**Location**: `/opt/src/jellyfin/Emby.Server.Implementations/Library/Validators/`

1. **ArtistsPostScanTask** - Validates and creates Artist items from Album metadata
2. **GenresPostScanTask** - Validates and creates Genre items
3. **StudiosPostScanTask** - Validates and creates Studio items
4. **MusicGenresPostScanTask** - Validates and creates MusicGenre items
5. **CollectionPostScanTask** - Validates collection membership
6. **ChannelPostScanTask** - Updates channel items

### 7.3 Execution

```csharp
// From LibraryManager.RunPostScanTasks()
private async Task RunPostScanTasks(IProgress<double> progress, CancellationToken cancellationToken)
{
    var tasks = PostScanTasks.ToList();
    var numComplete = 0;
    var numTasks = tasks.Count;
    
    foreach (var task in tasks)
    {
        var innerProgress = new Progress<double>(pct =>
        {
            double percent = (pct / 100 + numComplete) / numTasks * 100;
            progress.Report(percent);
        });
        
        await task.Run(innerProgress, cancellationToken);
        numComplete++;
    }
    
    // Final DB update
    _itemRepository.UpdateInheritedValues();
}
```

---

## 8. Example: Complete Scan Walkthrough

### Scenario: Scanning Mixed Movie + TV Library

```
Libraries/
├── Movies/
│   ├── Movie A (2020) [imdbid=tt1111111]/
│   │   ├── Movie A (2020).mkv
│   │   ├── movie.nfo
│   │   └── Movie A (2020)-trailer.mkv
│   └── Movie B (2019).mkv
└── TV Shows/
    └── Show X/
        ├── tvshow.nfo
        ├── Season 1/
        │   ├── season.nfo
        │   ├── Show X - s01e01 - Pilot.mkv
        │   └── Show X - s01e01 - Pilot.srt
```

### Step-by-Step Execution

**1. Trigger**: User clicks "Refresh Library" or scheduled task fires

**2. ValidateMediaLibraryInternal()**
```
IsScanRunning = true
LibraryMonitor.Stop()
```

**3. ValidateTopLibraryFolders()**
- Refresh Movies folder metadata
- Refresh TV Shows folder metadata

**4. RootFolder.ValidateChildren(recursive: true)**

**5. Process Movies Folder**
```
GetNonCachedChildren():
├─ DirectoryService.GetFileSystemEntries("/Libraries/Movies/")
│  ├─ "Movie A (2020)" (folder)
│  ├─ "Movie B (2019).mkv" (file)
│
├─ LibraryManager.ResolvePaths() → Apply Resolvers
│
├─ MovieResolver.ResolveMultiple()
│  ├─ Process "Movie A (2020)" folder
│  │  ├─ Check for DVD/Blu-ray → No
│  │  ├─ ResolveVideos<Movie>() 
│  │  │  ├─ VideoResolver.Resolve("Movie A (2020).mkv")
│  │  │  │  → VideoFileInfo (container=mkv, name="Movie A")
│  │  │  │
│  │  │  ├─ VideoResolver.Resolve("Movie A (2020)-trailer.mkv")
│  │  │  │  → Identified as trailer (not returned here)
│  │  │  │
│  │  │  └─ VideoListResolver.Resolve()
│  │  │     → Combines to single movie with LocalAlternateVersions/ExtraFiles
│  │  │
│  │  └─ Return Movie item
│  │     ├─ Path = "/Libraries/Movies/Movie A (2020)"
│  │     ├─ Name = "Movie A"
│  │     ├─ ProductionYear = 2020
│  │     ├─ IsInMixedFolder = false
│  │     └─ ExtraFiles = [Movie A (2020)-trailer.mkv]
│  │
│  └─ Process "Movie B (2019).mkv" file
│     → ResolveVideo<Movie>()
│     → Return Movie item
│        ├─ Path = "/Libraries/Movies/Movie B (2019).mkv"
│        ├─ IsInMixedFolder = true
│        └─ Name = "Movie B (2019)"

Compare with DB Cache:
├─ Movie A: Exists in DB
│  └─ UpdateFromResolvedItem() → No changes → Skip DB update
│
└─ Movie B: New
   └─ Add to newItems list

NewItems: [Movie B (2019)]
LibraryManager.CreateItems([Movie B], parent)
└─ Insert into database

ValidateSubFolders(): (parallel per subfolder)
└─ Movie A folder
   └─ ExtraResolver.GetResolversForExtraType()
      └─ Resolve trailer → Trailer item created

RefreshMetadataRecursive():
├─ Movie A
│  ├─ MovieNfoProvider.GetMetadata()
│  │  └─ Parse "movie.nfo" → metadata
│  │
│  ├─ TMDB Provider (if configured)
│  │  ├─ Extract IMDb ID from path
│  │  └─ Fetch from TMDB
│  │
│  └─ Image providers
│     ├─ Local images in folder
│     └─ Remote images from TMDB
│
└─ Movie B
   ├─ No local NFO
   ├─ Query TMDB by title+year
   └─ Download metadata + images
```

**6. Process TV Shows Folder**
```
GetNonCachedChildren():
├─ DirectoryService.GetFileSystemEntries()
│  └─ "Show X" (folder)
│
├─ SeriesResolver
│  ├─ Check for "tvshow.nfo" → Found
│  ├─ Naming.TV.SeriesResolver.Resolve()
│  │  → SeriesInfo (name="Show X", tvshow.nfo detected)
│  │
│  └─ Return Series item
│     ├─ Path = "/Libraries/TV Shows/Show X"
│     └─ Name = "Show X"

Compare with DB Cache:
└─ Show X: Exists → No changes

ValidateSubFolders():
└─ Show X folder (series)
   ├─ GetNonCachedChildren()
   │  ├─ DirectoryService.GetFileSystemEntries()
   │  │  └─ "Season 1" (folder)
   │  │
   │  ├─ SeasonResolver
   │  │  ├─ Check for "season.nfo" → Found
   │  │  └─ Return Season item
   │  │     ├─ Path = "/Libraries/TV Shows/Show X/Season 1"
   │  │     ├─ ParentIndexNumber = 1
   │  │     └─ Name = "Season 1"
   │  │
   │  └─ Compare with DB → Season exists
   │
   └─ Season 1 folder
      ├─ GetNonCachedChildren()
      │  ├─ DirectoryService.GetFileSystemEntries()
      │  │  └─ "Show X - s01e01 - Pilot.mkv" (file)
      │  │     "Show X - s01e01 - Pilot.srt" (file)
      │  │
      │  ├─ EpisodeResolver
      │  │  ├─ Parent = Season → Valid episode location
      │  │  ├─ VideoResolver.Resolve("Show X - s01e01 - Pilot.mkv")
      │  │  │  → VideoFileInfo
      │  │  │
      │  │  └─ ResolveVideo<Episode>()
      │  │     └─ Return Episode item
      │  │        ├─ Path = "/Libraries/.../Show X - s01e01 - Pilot.mkv"
      │  │        ├─ SeasonId = Season.Id
      │  │        ├─ SeriesId = Series.Id
      │  │        ├─ ParentIndexNumber = 1 (season)
      │  │        └─ IndexNumber = 1 (episode)
      │  │
      │  ├─ MediaInfoResolver.GetExternalFiles()
      │  │  ├─ Find "Show X - s01e01 - Pilot.srt"
      │  │  └─ Parse filename
      │  │     └─ ExternalPathParserResult
      │  │        ├─ Path = "...srt"
      │  │        ├─ Language = "eng" (from extension .srt, if tagged)
      │  │        ├─ IsDefault = true
      │  │        └─ IsForced = false
      │  │
      │  └─ Attach external files to Episode
      │
      └─ RefreshMetadataRecursive()
         ├─ Episode s01e01
         │  ├─ SeriesNfoProvider.GetMetadata()
         │  │  └─ Parse Series tvshow.nfo
         │  │
         │  ├─ TVDB Provider (if configured)
         │  │  └─ Query TVDB for episode metadata
         │  │
         │  ├─ MediaInfoResolver.GetExternalStreams()
         │  │  └─ Extract codec info from subtitle
         │  │
         │  └─ Save to database
```

**7. RunPostScanTasks()**
```
├─ ArtistsPostScanTask → Create artist items from album metadata
├─ GenresPostScanTask → Create/validate genre items
├─ StudiosPostScanTask → Create/validate studio items
└─ CollectionPostScanTask → Validate collections
```

**8. Completion**
```
LibraryMonitor.Start()
IsScanRunning = false
progress.Report(100)
```

---

## 9. Key Code Locations Reference

### Core Scanning
- `LibraryManager.cs` - `/opt/src/jellyfin/Emby.Server.Implementations/Library/LibraryManager.cs`
- `Folder.cs` - `/opt/src/jellyfin/MediaBrowser.Controller/Entities/Folder.cs`
- `IItemResolver.cs` - `/opt/src/jellyfin/MediaBrowser.Controller/Resolvers/IItemResolver.cs`

### Media Type Resolvers
- Movies: `/opt/src/jellyfin/Emby.Server.Implementations/Library/Resolvers/Movies/MovieResolver.cs`
- TV: `/opt/src/jellyfin/Emby.Server.Implementations/Library/Resolvers/TV/SeriesResolver.cs`, `EpisodeResolver.cs`, `SeasonResolver.cs`
- Music: `/opt/src/jellyfin/Emby.Server.Implementations/Library/Resolvers/Audio/MusicAlbumResolver.cs`, `AudioResolver.cs`, `MusicArtistResolver.cs`
- Photos: `/opt/src/jellyfin/Emby.Server.Implementations/Library/Resolvers/PhotoResolver.cs`
- Books: `/opt/src/jellyfin/Emby.Server.Implementations/Library/Resolvers/Books/BookResolver.cs`

### Naming Conventions
- Video: `/opt/src/jellyfin/Emby.Naming/Video/VideoResolver.cs`
- Episodes: `/opt/src/jellyfin/Emby.Naming/TV/EpisodeResolver.cs`, `EpisodePathParser.cs`
- Series: `/opt/src/jellyfin/Emby.Naming/TV/SeriesResolver.cs`
- Audio: `/opt/src/jellyfin/Emby.Naming/Audio/AlbumParser.cs`, `AudioFileParser.cs`
- AudioBooks: `/opt/src/jellyfin/Emby.Naming/AudioBook/AudioBookResolver.cs`

### Metadata & Providers
- ProviderManager: `/opt/src/jellyfin/MediaBrowser.Providers/Manager/ProviderManager.cs`
- NFO Providers: `/opt/src/jellyfin/MediaBrowser.XbmcMetadata/Providers/`
- MediaInfo: `/opt/src/jellyfin/MediaBrowser.Providers/MediaInfo/MediaInfoResolver.cs`

### Post-Scan Tasks
- Base Interface: `/opt/src/jellyfin/MediaBrowser.Controller/Library/ILibraryPostScanTask.cs`
- Implementations: `/opt/src/jellyfin/Emby.Server.Implementations/Library/Validators/`

### File Monitoring
- LibraryMonitor: `/opt/src/jellyfin/Emby.Server.Implementations/IO/LibraryMonitor.cs`
- Scheduled Task: `/opt/src/jellyfin/Emby.Server.Implementations/ScheduledTasks/Tasks/RefreshMediaLibraryTask.cs`

---

## 10. Configuration & Tuning

### NamingOptions
**File**: `/opt/src/jellyfin/Emby.Naming/Common/NamingOptions.cs`

**Key Settings**:
- `VideoFileExtensions` - Supported video formats
- `AudioFileExtensions` - Supported audio formats
- `EpisodeExpressions` - Episode naming patterns
- `VideoFlagDelimiters` - Characters separating flags
- `MediaFlagDelimiters` - Characters in media flags
- `CleanDateTimeRegexes` - Patterns to clean from names
- `AlbumStackingPrefixes` - Multi-part album detection

### Library Options
**Per-Collection Settings**:
- `EnablePhotos` - Enable photo scanning in home videos
- `EnableArchiveSupport` - Support archive formats
- `ExtractChapterImagesDuringLibraryScan` - Extract chapters from video
- `SaveArtworkInMediaFolders` - Store metadata locally

---

This comprehensive documentation covers all major aspects of Jellyfin's media scanning architecture. The system is designed for flexibility, performance, and support for diverse media types and storage structures.
