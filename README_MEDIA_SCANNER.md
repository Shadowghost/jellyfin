# Jellyfin Media Scanner Documentation Index

This directory contains comprehensive documentation about the Jellyfin media scanner architecture, including detailed information about how different media types are scanned, processed, and cataloged.

## Documentation Files

### 1. MEDIA_SCANNER_DOCUMENTATION.md (1,406 lines)
**Most comprehensive reference**

Complete deep-dive into the media scanner architecture covering:
- Overall scanning architecture and workflow
- Complete walkthrough of media type-specific scanning:
  - Movie scanning with DVD/Blu-ray detection
  - TV show/series scanning with episode naming patterns
  - Music scanning (artists, albums, tracks)
  - Photo/image scanning
  - Book/eBook scanning
  - AudioBook scanning
- File organization and detection patterns
- Naming convention parsers and multi-version detection
- Extra content (trailers, subtitles) handling
- Provider ID extraction from paths
- Metadata extraction (NFO, tags, external files)
- Provider integration and fallback strategies
- Performance optimization and caching
- Post-scan task pipeline
- Complete scan walkthrough example
- Key code locations reference

**Best for**: Understanding the complete system, learning how each component fits together, detailed implementation specifics.

### 2. SCANNING_SUMMARY.md (241 lines)
**Quick reference guide**

Quick-lookup reference including:
- Core architecture overview
- Media type resolution priority order
- Media type specifications (Movies, TV, Music, Photos, Books, AudioBooks)
- Metadata extraction methods
- File organization patterns (multi-version, 3D, extras)
- Performance optimization strategies
- Key code paths and patterns
- Configuration options
- Troubleshooting guide
- Important file and location listings

**Best for**: Quick lookups, remembering resolver priority, finding file paths, troubleshooting common issues.

### 3. SCANNER_CODE_EXAMPLES.md (733 lines)
**Practical implementation reference**

Detailed code examples showing:
- Core scanning flow with actual C# code
- Resolver priority implementation
- Media type-specific detection (episodes, music, photos)
- Photo vs metadata image detection
- Provider ID extraction from filenames
- External file detection (subtitles, audio)
- External file naming patterns
- Parallel processing with LimitedConcurrencyLibraryScheduler
- NFO file parsing and change detection
- Post-scan task execution
- Custom resolver implementation template
- Multi-level caching strategies
- Implementation patterns

**Best for**: Learning how to implement new features, understanding actual code patterns, creating custom resolvers.

## Key Concepts

### Resolver Priority Order
1. **First** - Special resolvers
2. **Second** - SeriesResolver (TV Shows)
3. **Third** - MusicAlbumResolver
4. **Fourth** - MovieResolver
5. **Fifth** - AudioResolver

### Scanning Pipeline
```
File System 
  ↓ (DirectoryService)
File/Folder Analysis
  ↓ (Apply Resolvers in Priority Order)
Item Creation
  ↓ (LibraryManager)
Database Storage
  ↓ (ItemRepository)
Provider Refresh (async)
  ↓ (ProviderManager)
Post-Scan Tasks
```

### Main Components
- **LibraryManager** - Central orchestrator
- **Folder** - Recursive validation and metadata refresh
- **ItemResolver** - Media type-specific file detection
- **DirectoryService** - File system interaction with caching
- **ProviderManager** - Metadata enrichment from external sources
- **Database** - Persistent storage via Entity Framework

## File Locations

### Core Scanning
```
/Emby.Server.Implementations/Library/
├── LibraryManager.cs                 # Main orchestrator
├── Resolvers/                        # Media type resolvers
│   ├── Movies/MovieResolver.cs
│   ├── TV/SeriesResolver.cs
│   ├── Audio/MusicAlbumResolver.cs
│   ├── PhotoResolver.cs
│   └── Books/BookResolver.cs
└── Validators/                       # Post-scan tasks
```

### Naming Convention Parsers
```
/Emby.Naming/
├── Video/VideoResolver.cs           # Video file parsing
├── TV/                               # Episode/series parsing
├── Audio/                            # Album parsing
└── AudioBook/                        # AudioBook parsing
```

### Metadata & Providers
```
/MediaBrowser.Providers/Manager/ProviderManager.cs
/MediaBrowser.Providers/MediaInfo/MediaInfoResolver.cs
/MediaBrowser.XbmcMetadata/Providers/                # NFO parsing
```

## Quick Start

### Understanding How Movies Are Scanned
1. Read: SCANNING_SUMMARY.md → "Movies" section
2. Deep dive: MEDIA_SCANNER_DOCUMENTATION.md → "2.1 Movie Scanning"
3. Code examples: SCANNER_CODE_EXAMPLES.md → "Example 3: Multi-Item Resolution"

### Understanding How Episodes Are Detected
1. Read: SCANNING_SUMMARY.md → "TV Shows" section
2. Deep dive: MEDIA_SCANNER_DOCUMENTATION.md → "2.2 TV Show/Series Scanning"
3. Code examples: SCANNER_CODE_EXAMPLES.md → "Example 4: Episode Detection"

### Adding Support for New Media Type
1. Read: SCANNER_CODE_EXAMPLES.md → "Implementation Patterns" → "Pattern 1"
2. Reference: MEDIA_SCANNER_DOCUMENTATION.md → "2.1 through 2.6" (any media type)
3. Code template: SCANNER_CODE_EXAMPLES.md → "Example 13: Implementing Custom Resolver"

### Understanding Performance
1. Read: SCANNING_SUMMARY.md → "Performance Optimization" section
2. Deep dive: MEDIA_SCANNER_DOCUMENTATION.md → "6. Performance & Optimization"
3. Caching details: SCANNER_CODE_EXAMPLES.md → "Example 14: Multi-Level Caching"

### Troubleshooting Scanner Issues
1. Check: SCANNING_SUMMARY.md → "Troubleshooting" section
2. Verify file paths: SCANNING_SUMMARY.md → "Important Files & Locations"
3. Review patterns: MEDIA_SCANNER_DOCUMENTATION.md → "3. File Organization & Detection"

## Key Implementation Files Covered

### Resolvers (Media Type Detection)
- `/opt/src/jellyfin/Emby.Server.Implementations/Library/Resolvers/Movies/MovieResolver.cs`
- `/opt/src/jellyfin/Emby.Server.Implementations/Library/Resolvers/TV/SeriesResolver.cs`
- `/opt/src/jellyfin/Emby.Server.Implementations/Library/Resolvers/TV/EpisodeResolver.cs`
- `/opt/src/jellyfin/Emby.Server.Implementations/Library/Resolvers/Audio/AudioResolver.cs`
- `/opt/src/jellyfin/Emby.Server.Implementations/Library/Resolvers/Audio/MusicAlbumResolver.cs`
- `/opt/src/jellyfin/Emby.Server.Implementations/Library/Resolvers/PhotoResolver.cs`
- `/opt/src/jellyfin/Emby.Server.Implementations/Library/Resolvers/Books/BookResolver.cs`

### Naming Parsers
- `/opt/src/jellyfin/Emby.Naming/TV/EpisodeResolver.cs`
- `/opt/src/jellyfin/Emby.Naming/TV/EpisodePathParser.cs`
- `/opt/src/jellyfin/Emby.Naming/Video/VideoResolver.cs`
- `/opt/src/jellyfin/Emby.Naming/Audio/AlbumParser.cs`
- `/opt/src/jellyfin/Emby.Naming/AudioBook/AudioBookResolver.cs`

### Core Architecture
- `/opt/src/jellyfin/Emby.Server.Implementations/Library/LibraryManager.cs`
- `/opt/src/jellyfin/MediaBrowser.Controller/Entities/Folder.cs`
- `/opt/src/jellyfin/MediaBrowser.Controller/Resolvers/IItemResolver.cs`

### Metadata Extraction
- `/opt/src/jellyfin/MediaBrowser.Providers/Manager/ProviderManager.cs`
- `/opt/src/jellyfin/MediaBrowser.Providers/MediaInfo/MediaInfoResolver.cs`
- `/opt/src/jellyfin/MediaBrowser.XbmcMetadata/Providers/BaseNfoProvider.cs`

### Post-Scan Tasks
- `/opt/src/jellyfin/Emby.Server.Implementations/Library/Validators/ArtistsPostScanTask.cs`
- `/opt/src/jellyfin/Emby.Server.Implementations/Library/Validators/GenresPostScanTask.cs`
- `/opt/src/jellyfin/MediaBrowser.Controller/Library/ILibraryPostScanTask.cs`

## Statistics

- **Total Documentation**: 2,380 lines
  - MEDIA_SCANNER_DOCUMENTATION.md: 1,406 lines
  - SCANNER_CODE_EXAMPLES.md: 733 lines
  - SCANNING_SUMMARY.md: 241 lines

- **Media Types Covered**: 6
  - Movies (with DVD/Blu-ray detection)
  - TV Shows/Episodes
  - Music (Artists, Albums, Tracks)
  - Photos
  - Books
  - AudioBooks

- **Code Examples**: 14 detailed examples
- **Implementation Patterns**: 2 major patterns
- **Key File References**: 25+ specific file paths

## Notes

- All file paths are absolute paths from `/opt/src/jellyfin/`
- Code examples use C# with async/await patterns
- Documentation covers .NET Core architecture
- All information extracted from actual Jellyfin codebase (verified)
- Scanner runs during: startup, scheduled intervals (12h default), and file system changes

## How to Use This Documentation

1. **For Overview**: Start with SCANNING_SUMMARY.md
2. **For Details**: Reference MEDIA_SCANNER_DOCUMENTATION.md
3. **For Implementation**: Use SCANNER_CODE_EXAMPLES.md
4. **For Specific Topics**: Use the table of contents in each file
5. **For File Locations**: Check "Important Files & Locations" in any document

---

Last updated: 2025-11-12
Jellyfin Version: Latest (from master branch investigation)
Documentation Scope: Complete Media Scanner Architecture
