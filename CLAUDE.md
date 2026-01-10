# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

Jellyfin is a Free Software Media System - a cross-platform media server and streaming solution built on .NET 9.0. It provides media management, streaming, transcoding, and metadata management capabilities through a REST API and web interface.

## Build and Development Commands

### Building the Project
```bash
dotnet build                                    # Build entire solution
dotnet build Jellyfin.Server                    # Build server only
dotnet build --configuration Release            # Release build
```

### Running the Server
```bash
# Run server with web client (requires jellyfin-web to be installed separately)
dotnet run --project Jellyfin.Server --webdir /absolute/path/to/jellyfin-web/dist

# Run server without web client (for API development)
dotnet run --project Jellyfin.Server --nowebclient

# Run pre-built executable
cd Jellyfin.Server/bin/Debug/net9.0
./jellyfin --help                               # View all command-line options
```

Server runs at: `http://localhost:8096`
API documentation: `http://localhost:8096/api-docs/swagger/index.html`

### Testing
```bash
dotnet test                                     # Run all tests
dotnet test tests/Jellyfin.Api.Tests            # Run specific test project
dotnet test --filter "FullyQualifiedName~SomeTestName"  # Run single test
```

### Code Quality
```bash
dotnet format                                   # Format code according to .editorconfig
dotnet build /p:Configuration=Debug             # Enables all analyzers (AnalysisMode=AllEnabledByDefault)
```

### Deploying to Local Instance
```bash
# Build and publish
dotnet publish Jellyfin.Server --configuration Debug --output="./out" --self-contained --runtime linux-amd64

# Deploy and restart service
rm -r /usr/lib/jellyfin/bin && mv /opt/src/jellyfin/out /usr/lib/jellyfin/bin && chown jellyfin:jellyfin /usr/lib/jellyfin/bin -R && systemctl restart jellyfin
```

## Architecture Overview

### Project Structure

The solution follows a layered architecture with clear separation of concerns:

**Core Layers:**
- `MediaBrowser.Model` - DTOs, enums, and data contracts (no dependencies)
- `MediaBrowser.Common` - Common utilities and abstractions
- `MediaBrowser.Controller` - Core interfaces and business logic contracts
- `Jellyfin.Data` - EF Core entity models and data layer

**Implementation Layers:**
- `Emby.Server.Implementations` - Core business logic implementations (ApplicationHost, LibraryManager, etc.)
- `Jellyfin.Server.Implementations` - Jellyfin-specific server implementations
- `Jellyfin.Api` - ASP.NET Core REST API controllers
- `Jellyfin.Server` - Entry point and host configuration

**Supporting Libraries:**
- `Emby.Naming` - File/folder naming convention parsing (TV shows, movies, etc.)
- `MediaBrowser.MediaEncoding` - FFmpeg integration for transcoding
- `MediaBrowser.Providers` - External metadata providers (TMDb, IMDb, MusicBrainz, etc.)
- `MediaBrowser.XbmcMetadata` - NFO file support
- `MediaBrowser.LocalMetadata` - Local metadata handling
- `Jellyfin.Drawing` / `Jellyfin.Drawing.Skia` - Image processing
- `Jellyfin.Networking` - Network utilities
- `Jellyfin.Database.Implementations` - EF Core database implementations

### Key Architectural Patterns

**Dependency Injection:**
All components use constructor injection. Services are registered in `Jellyfin.Server/Extensions` and `Emby.Server.Implementations/ApplicationHost.cs`.

**Entity System:**
- `BaseItem` is the root entity class for all media items (movies, episodes, songs, etc.)
- Entities are stored in SQLite via custom ItemRepository and EF Core for user data
- `ILibraryManager` is the central service for media library operations

**API Layer:**
- Controllers inherit from `BaseJellyfinApiController`
- Use standard ASP.NET Core attributes for routing and authorization
- Authorization policies defined in `Jellyfin.Api.Constants.Policies`
- API follows REST conventions with versioning support

**Metadata Providers:**
- Implement `IMetadataProvider<TItemType>` for type-specific metadata
- Providers are discovered via DI and executed in priority order
- Support for remote providers (TMDb, MusicBrainz) and local (NFO files)

**Library Scanning:**
- File system is monitored via `ILibraryMonitor`
- Items are resolved through `IItemResolver` implementations
- Naming conventions parsed by `Emby.Naming` library

**Plugin System:**
- Plugins implement interfaces from `MediaBrowser.Controller`
- Loaded dynamically at runtime from plugin directory

**Transcoding:**
- FFmpeg integration via `MediaBrowser.MediaEncoding`
- Dynamic HLS streaming via `DynamicHlsController`
- Media source selection through `IMediaSourceManager`

## Important Files & Directories

### Entry Point & Configuration
- **`Jellyfin.Server/Program.cs`** - Application entry point, host builder, logging setup
- **`Emby.Server.Implementations/ApplicationHost.cs`** - Core application host, DI container registration, plugin loading
- **`Jellyfin.Server/Extensions/`** - Service registration extensions for ASP.NET Core
- **`Directory.Build.props`** - Global MSBuild properties (nullable enabled, warnings as errors)
- **`Directory.Packages.props`** - Centralized package version management
- **`global.json`** - .NET SDK version specification (9.0)

### API Layer (`Jellyfin.Api/`)
- **`Controllers/`** - 60+ REST API controllers for all endpoints
  - `LibraryController.cs` - Media library queries and management
  - `ItemsController.cs` - Item retrieval and filtering
  - `PlaystateController.cs` - Playback progress tracking
  - `UserController.cs` - User management
  - `SessionController.cs` - Client session management
  - `VideosController.cs`, `AudioController.cs` - Media streaming
  - `DynamicHlsController.cs` - HLS transcoding streams
  - `ImageController.cs` - Image serving and resizing
- **`Auth/`** - Authorization policies and handlers
  - `CustomAuthenticationHandler.cs` - JWT and API key authentication
  - Policy folders define authorization requirements (elevation, first-time setup, etc.)
- **`Middleware/`** - HTTP request pipeline middleware
  - `ExceptionMiddleware.cs` - Global exception handling
  - `WebSocketHandlerMiddleware.cs` - WebSocket connection handling
  - `IpBasedAccessValidationMiddleware.cs` - IP-based access control
- **`Helpers/`** - Request/response helpers, DTO conversion utilities
- **`ModelBinders/`** - Custom model binding for complex query parameters

### Core Interfaces (`MediaBrowser.Controller/`)
Define contracts for all major subsystems:
- **`Library/`** - Core library management interfaces
  - `ILibraryManager.cs` - Central interface for all library operations (item queries, metadata updates, library scanning)
  - `IUserDataManager.cs` - User playback state and favorites
  - `IUserManager.cs` - User authentication and management
  - `IMediaSourceManager.cs` - Media source selection and transcoding decisions
  - `ISearchEngine.cs` - Search across all media types
- **`Entities/`** - Base entity classes and media type definitions
  - `BaseItem.cs` - Root class for all media items (movies, episodes, songs, photos, etc.)
  - `Audio/` - Music-specific entities (MusicAlbum, MusicArtist, Audio)
  - `TV/` - TV-specific entities (Series, Season, Episode)
  - `Movies/` - Movie entity
  - `InternalItemsQuery.cs` - Query builder for complex library queries
- **`Providers/`** - Metadata provider interfaces
  - `IMetadataProvider.cs`, `IRemoteMetadataProvider.cs`, `ILocalMetadataProvider.cs`
  - `IImageProvider.cs` - Image fetching (posters, backdrops, etc.)
- **`Session/`** - Client session and playback reporting interfaces
- **`Persistence/`** - Database repository interfaces
- **`MediaEncoding/`** - Transcoding and media info interfaces
- **`Plugins/`** - Plugin system interfaces
- **`Streaming/`** - Media streaming and transcoding interfaces

### Business Logic (`Emby.Server.Implementations/`)
- **`Library/`** - Library management implementations
  - `LibraryManager.cs` - Main library manager (item resolution, metadata updates, library scanning)
  - `UserDataManager.cs` - Tracks play state, favorites, ratings
  - `MediaSourceManager.cs` - Selects appropriate media sources for playback
  - `SearchEngine.cs` - Full-text search implementation
  - `Resolvers/` - File-to-entity resolvers (identifies movies, TV shows, music from files)
  - `Validators/` - Post-scan validators (people, music genres, etc.)
- **`ScheduledTasks/`** - Background task execution
  - `TaskManager.cs` - Scheduled task orchestrator
  - `Tasks/` - Built-in tasks (library refresh, chapter extraction, cleanup, optimization)
- **`Session/`** - Session and playback management
  - `SessionManager.cs` - Manages active client connections and playback sessions
  - `SessionWebSocketListener.cs` - Real-time updates to clients
- **`Data/`** - Data access layer and repositories
- **`EntryPoints/`** - Startup initialization tasks
  - `LibraryChangedNotifier.cs` - Broadcasts library change events
  - `UserDataChangeNotifier.cs` - Broadcasts user data changes
- **`Images/`** - Image processing, caching, and generation
- **`Localization/`** - Internationalization and string resources
- **`Plugins/`** - Plugin loader and manager
- **`HttpServer/`** - HTTP server utilities
- **`IO/`** - File system operations and monitoring
- **`Dto/`** - DTO construction and mapping

### Metadata Providers (`MediaBrowser.Providers/`)
Organized by media type:
- **`Movies/`** - Movie metadata (TMDb integration)
- **`TV/`** - TV show metadata (TMDb, TheTVDB)
- **`Music/`** - Music metadata (MusicBrainz, LastFM)
- **`People/`** - Actor/director metadata
- **`Subtitles/`** - Subtitle search and download
- **`Plugins/`** - Plugin metadata providers
- **`MediaInfo/`** - FFprobe integration for media file analysis
- **`Manager/`** - Provider orchestration and scheduling

### Data Layer
- **`Jellyfin.Data/`** - EF Core entities
  - `Entities/` - User, authentication, display preferences
  - `Enums/` - Shared enumerations
  - `Queries/` - Query object definitions
- **`src/Jellyfin.Database/`** - Database abstractions
  - `Jellyfin.Database.Implementations/` - EF Core DbContext implementations
  - `Jellyfin.Database.Providers.Sqlite/` - SQLite provider for media library

### Naming & Parsing (`Emby.Naming/`)
File and folder naming convention parsers:
- **`Video/`** - Movie and video file naming patterns
- **`TV/`** - TV show episode naming (S01E05, 1x05, etc.)
- **`Audio/`** - Music file naming conventions
- **`AudioBook/`** - Audiobook naming patterns
- **`ExternalFiles/`** - Subtitle, lyric, and external file detection

### Media Encoding (`MediaBrowser.MediaEncoding/`)
- **`Encoder/`** - FFmpeg wrapper and command building
- **`Probing/`** - Media file probing (format, streams, codecs)
- **`Subtitles/`** - Subtitle extraction and conversion
- **`Attachments/`** - Media attachment handling (fonts, etc.)

### Supporting Libraries
- **`src/Jellyfin.Extensions/`** - Extension methods and utilities
- **`src/Jellyfin.Drawing/`** - Image abstraction layer
- **`src/Jellyfin.Drawing.Skia/`** - Skia-based image processor
- **`src/Jellyfin.Networking/`** - Network utilities (IP parsing, subnet checking)
- **`src/Jellyfin.LiveTv/`** - Live TV and EPG functionality
- **`src/Jellyfin.MediaEncoding.Keyframes/`** - Keyframe extraction
- **`src/Jellyfin.MediaEncoding.Hls/`** - HLS playlist generation
- **`RSSDP/`** - DLNA/UPnP SSDP discovery
- **`Emby.Dlna/`** - DLNA server implementation
- **`Emby.Photos/`** - Photo library support

### Testing (`tests/`)
Each main project has corresponding test project:
- **`Jellyfin.Api.Tests/`** - API controller and integration tests
- **`Jellyfin.Server.Integration.Tests/`** - Full integration tests with TestServer
- **`Jellyfin.Naming.Tests/`** - Naming convention parser tests
- **`Jellyfin.MediaEncoding.Tests/`** - FFmpeg integration tests
- **`Jellyfin.*.Tests/`** - Unit tests for each component

### Configuration Files
- **`.editorconfig`** - Code style rules (indentation, naming conventions, C# preferences)
- **`stylecop.json`** - StyleCop analyzer configuration
- **`BannedSymbols.txt`** - Prohibited APIs (security, deprecated methods)
- **`SharedVersion.cs`** - Assembly version information
- **`.github/workflows/`** - CI/CD pipelines (tests, OpenAPI validation, CodeQL)

## Code Style and Conventions

### Naming Conventions (from .editorconfig)
- Private fields: `_camelCase` with underscore prefix
- Static fields: `_camelCase` with underscore prefix
- Constants: `PascalCase`
- Public members: `PascalCase`

### General Guidelines
- Nullable reference types enabled (`<Nullable>enable</Nullable>`)
- Warnings treated as errors in all configurations
- Use `var` for built-in types and when type is apparent
- File-scoped namespaces preferred (e.g., `namespace Jellyfin.Api.Controllers;`)
- 4 spaces for indentation (except YAML/XML uses 2)
- LF line endings

### Custom Analyzers
- Jellyfin.CodeAnalysis project provides custom Roslyn analyzers
- StyleCop.Analyzers enforced with configuration in `stylecop.json`
- BannedSymbols.txt defines prohibited API usage

## Database Architecture

The application uses a hybrid database approach:

**SQLite with Custom Repository (Legacy):**
- `Jellyfin.Database.Providers.Sqlite` - Library items stored in `library.db`
- Direct SQLite access for performance-critical media queries
- Item metadata, paths, and relationships

**Entity Framework Core:**
- `Jellyfin.Database.Implementations` - User data, authentication, preferences
- Migrations in individual database provider projects
- Supports future database provider plugins

When working with data:
- Library item queries go through `ILibraryManager`
- User data through `IUserDataManager`
- User management through `IUserManager`

## Common Development Patterns

### Adding a New API Endpoint
1. Create controller in `Jellyfin.Api/Controllers` inheriting from `BaseJellyfinApiController`
2. Add route attribute: `[Route("ControllerName")]`
3. Add authorization: `[Authorize(Policy = Policies.DefaultAuthorization)]`
4. Inject required services via constructor
5. Add XML documentation comments for Swagger

### Adding a New Metadata Provider
1. Create class in `MediaBrowser.Providers/{Category}`
2. Implement `IRemoteMetadataProvider<TItemType>` or `ILocalMetadataProvider<TItemType>`
3. Return priority order (lower executes first)
4. Provider auto-discovered via DI

### Adding a Scheduled Task
1. Implement `IScheduledTask` in appropriate project
2. Define task category, key, and triggers
3. Implement `ExecuteAsync` with cancellation support
4. Task auto-registered at startup

### Working with Media Items
1. Query via `ILibraryManager.GetItemsResult()` with `InternalItemsQuery`
2. Retrieve by ID: `ILibraryManager.GetItemById()`
3. Update metadata: modify item, then `ILibraryManager.UpdateItemAsync()`
4. Type-check using `BaseItemKind` enum, not `is` patterns where possible

## Testing Guidelines

- Unit tests use xUnit, Moq, and AutoFixture
- Integration tests in `Jellyfin.Server.Integration.Tests` use `WebApplicationFactory`
- Test structure: `tests/{ProjectName}.Tests`
- Follow AAA pattern (Arrange, Act, Assert)
- Mock dependencies using Moq (`Mock<IService>`)

## Important Notes

- The web client is a separate repository (jellyfin-web) and must be built/downloaded separately
- FFmpeg is required for transcoding functionality
- Setup wizard cannot run with `--nowebclient` flag
- Target framework: .NET 9.0 (see `global.json`)
- Central package management enabled (`Directory.Packages.props`)
