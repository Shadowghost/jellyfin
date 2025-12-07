# Jellyfin Database Architecture

This document describes the database setup, architecture, and access patterns used in Jellyfin.

## Overview

Jellyfin uses **SQLite** as its primary database, managed through **Entity Framework Core** with a factory-based DbContext pattern. The architecture is designed to be extensible, allowing for future database provider plugins while maintaining clean separation of concerns.

**Database Location**: `{DataPath}/jellyfin.db`

## Database Technology

### SQLite Provider

The default and currently only built-in database provider is SQLite, implemented in:
- `src/Jellyfin.Database/Jellyfin.Database.Providers.Sqlite/SqliteDatabaseProvider.cs`

**Key Features**:
- Configurable connection pooling and caching
- WAL (Write-Ahead Logging) mode for better concurrency
- Custom PRAGMA support for performance tuning
- Built-in backup/restore functionality
- Automatic optimization tasks (VACUUM, PRAGMA optimize, WAL checkpoints)
- Health checks on shutdown

**Connection String Configuration**:
```csharp
var sqliteConnectionBuilder = new SqliteConnectionStringBuilder();
sqliteConnectionBuilder.DataSource = Path.Combine(_applicationPaths.DataPath, "jellyfin.db");
sqliteConnectionBuilder.Cache = SqliteCacheMode.Default;  // Configurable
sqliteConnectionBuilder.Pooling = true;                    // Configurable
sqliteConnectionBuilder.DefaultTimeout = 30;               // Configurable (seconds)
```

### Custom Database Options

Administrators can configure SQLite behavior via custom options:
- `cache`: Cache mode (Default, Shared, Private)
- `pooling`: Enable/disable connection pooling
- `command-timeout`: Command timeout in seconds
- `cacheSize`: Page cache size
- `lockingmode`: SQLite locking mode (NORMAL, EXCLUSIVE)
- `journalsizelimit`: Journal size limit in bytes
- `tempstoremode`: Temporary storage mode
- `syncmode`: Synchronization mode
- Custom PRAGMAs via `#PRAGMA:key=value`

## Database Setup and Configuration

### Configuration Store

Database configuration is managed through:
- **Options Class**: `src/Jellyfin.Database/Jellyfin.Database.Implementations/DbConfiguration/DatabaseConfigurationOptions.cs`
- **Store**: `Jellyfin.Server.Implementations/DbConfiguration/DatabaseConfigurationStore.cs`

**Configuration Properties**:
```csharp
public class DatabaseConfigurationOptions
{
    public string DatabaseType { get; set; }  // Default: "Jellyfin-SQLite"
    public CustomDatabaseOptions CustomProviderOptions { get; set; }
    public DatabaseLockingBehaviorTypes LockingBehavior { get; set; }  // Default: NoLock
}
```

### Dependency Injection Registration

Database services are registered in `Jellyfin.Server.Implementations/Extensions/ServiceCollectionExtensions.cs`:

```csharp
serviceCollection.AddJellyfinDbContext(efCoreConfiguration);

// Registers:
// - IJellyfinDatabaseProvider (SQLite or custom plugin)
// - IEntityFrameworkCoreLockingBehavior
// - IDbContextFactory<JellyfinDbContext> (pooled)
```

The factory pattern creates pooled DbContext instances, improving performance by reusing context objects across requests.

## DbContext and Entity Framework Core

### JellyfinDbContext

The main DbContext is located at: `src/Jellyfin.Database/Jellyfin.Database.Implementations/JellyfinDbContext.cs`

**DbSets (Tables)**:
```csharp
public DbSet<AccessSchedule> AccessSchedules { get; set; }
public DbSet<ActivityLog> ActivityLogs { get; set; }
public DbSet<ApiKey> ApiKeys { get; set; }
public DbSet<Device> Devices { get; set; }
public DbSet<DeviceOptions> DeviceOptions { get; set; }
public DbSet<DisplayPreferences> DisplayPreferences { get; set; }
public DbSet<ImageInfo> ImageInfos { get; set; }
public DbSet<ItemDisplayPreferences> ItemDisplayPreferences { get; set; }
public DbSet<CustomItemDisplayPreferences> CustomItemDisplayPreferences { get; set; }
public DbSet<Permission> Permissions { get; set; }
public DbSet<Preference> Preferences { get; set; }
public DbSet<User> Users { get; set; }
public DbSet<TrickplayInfo> TrickplayInfos { get; set; }
public DbSet<MediaSegment> MediaSegments { get; set; }
public DbSet<UserData> UserData { get; set; }
public DbSet<AncestorId> AncestorIds { get; set; }
public DbSet<AttachmentStreamInfo> AttachmentStreamInfos { get; set; }
public DbSet<BaseItemEntity> BaseItems { get; set; }
public DbSet<Chapter> Chapters { get; set; }
public DbSet<ItemValue> ItemValues { get; set; }
public DbSet<ItemValueMap> ItemValuesMap { get; set; }
public DbSet<MediaStreamInfo> MediaStreamInfos { get; set; }
public DbSet<People> Peoples { get; set; }
public DbSet<PeopleBaseItemMap> PeopleBaseItemMap { get; set; }
public DbSet<BaseItemProvider> BaseItemProviders { get; set; }
public DbSet<BaseItemImageInfo> BaseItemImageInfos { get; set; }
public DbSet<BaseItemMetadataField> BaseItemMetadataFields { get; set; }
public DbSet<BaseItemTrailerType> BaseItemTrailerTypes { get; set; }
public DbSet<KeyframeData> KeyframeData { get; set; }
```

**SaveChanges Override**:

The DbContext overrides SaveChanges to:
1. Update concurrency tokens for entities implementing `IHasConcurrencyToken`
2. Apply the configured locking behavior
3. Catch and log exceptions

### Model Configuration

Entity configurations use EF Core's `IEntityTypeConfiguration<T>` pattern, located in:
`src/Jellyfin.Database/Jellyfin.Database.Implementations/ModelConfiguration/`

**Example Configuration** (BaseItemConfiguration.cs):
```csharp
public class BaseItemConfiguration : IEntityTypeConfiguration<BaseItemEntity>
{
    public void Configure(EntityTypeBuilder<BaseItemEntity> builder)
    {
        builder.HasKey(e => e.Id);

        // Relationships
        builder.HasMany(e => e.DirectChildren)
            .WithOne(e => e.DirectParent)
            .HasForeignKey(e => e.ParentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Performance indexes
        builder.HasIndex(e => e.Path);
        builder.HasIndex(e => new { e.Type, e.TopParentId, e.IsFolder, e.IsVirtualItem });

        // Seed data for placeholder
        builder.HasData(new BaseItemEntity()
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Type = "PLACEHOLDER",
            Name = "This is a placeholder item for UserData that has been detached...",
        });
    }
}
```

Configurations are automatically applied via:
```csharp
modelBuilder.ApplyConfigurationsFromAssembly(typeof(JellyfinDbContext).Assembly);
```

## Database Entities

All entity classes are located in: `src/Jellyfin.Database/Jellyfin.Database.Implementations/Entities/`

### User Management Entities

| Entity | Purpose |
|--------|---------|
| **User** | User accounts with authentication, password settings, profile information |
| **AccessSchedule** | Time-based access restrictions for users |
| **Permission** | User permissions and access rights |
| **Preference** | User preferences and settings |
| **ApiKey** | API authentication keys |
| **Device** | Connected client devices |
| **DeviceOptions** | Device-specific configuration options |

### Media/Item Entities

| Entity | Purpose |
|--------|---------|
| **BaseItemEntity** | Core media item entity (movies, TV shows, songs, photos, etc.) |
| **UserData** | User-specific item data (ratings, play counts, watch progress) |
| **Chapter** | Video chapter markers and bookmarks |
| **MediaStreamInfo** | Audio/video/subtitle stream metadata |
| **AttachmentStreamInfo** | Attachment streams (fonts, cover art, etc.) |
| **MediaSegment** | Media segments for intro/credits detection |
| **KeyframeData** | Video keyframe locations for seeking |

### Metadata & Relationship Entities

| Entity | Purpose |
|--------|---------|
| **ItemValue** | Generic key-value data for items (genres, studios, tags, etc.) |
| **ItemValueMap** | Many-to-many relationships between items and values |
| **People** | Actors, directors, writers, and other contributors |
| **PeopleBaseItemMap** | Links people to media items with role information |
| **BaseItemProvider** | External metadata provider IDs (IMDB, TVDB, etc.) |
| **AncestorId** | Hierarchical parent/collection relationships |
| **BaseItemImageInfo** | Image metadata for items |

### UI/Display Entities

| Entity | Purpose |
|--------|---------|
| **DisplayPreferences** | User interface preferences |
| **ItemDisplayPreferences** | Per-item display settings |
| **CustomItemDisplayPreferences** | Custom display fields and values |
| **ImageInfo** | Image metadata, dimensions, and types |

### Special Entities

| Entity | Purpose |
|--------|---------|
| **ActivityLog** | Server activity and audit log |
| **TrickplayInfo** | Quick-play thumbnail metadata for scrubbing |

### BaseItemEntity Structure

The most important entity is **BaseItemEntity** (`Entities/BaseItemEntity.cs`), which represents all media items in the library.

**Key Properties**:
```csharp
public required Guid Id { get; set; }
public required string Type { get; set; }  // Movie, Episode, Audio, Photo, etc.
public string? Name { get; set; }
public string? Path { get; set; }
public DateTime? StartDate { get; set; }
public DateTime? EndDate { get; set; }
public bool IsMovie { get; set; }
public float? CommunityRating { get; set; }
public int? ProductionYear { get; set; }
public string? Genres { get; set; }  // JSON serialized array
public long? RunTimeTicks { get; set; }
public bool IsFolder { get; set; }
public int? InheritedParentalRatingValue { get; set; }
public Guid? ParentId { get; set; }
public string? TopParentId { get; set; }
// ... 40+ additional properties for metadata
```

## Database Access Patterns

Jellyfin uses direct EF Core LINQ queries via the DbContext factory pattern. There is no additional ORM abstraction layer.

### Primary Pattern: DbContextFactory

Services receive an injected `IDbContextFactory<JellyfinDbContext>` and create context instances as needed.

**Synchronous Access**:
```csharp
using var dbContext = _dbProvider.CreateDbContext();
var users = dbContext.Users
    .AsSplitQuery()
    .Include(u => u.Permissions)
    .Include(u => u.Preferences)
    .ToList();
```

**Asynchronous Access**:
```csharp
await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
var user = await dbContext.Users
    .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
```

**Example from UserManager** (`Jellyfin.Server.Implementations/Users/UserManager.cs:94-100`):
```csharp
public UserManager(IDbContextFactory<JellyfinDbContext> dbProvider, ...)
{
    _dbProvider = dbProvider;

    using var dbContext = _dbProvider.CreateDbContext();
    _users = dbContext.Users
        .AsSplitQuery()
        .Include(u => u.Permissions)
        .Include(u => u.Preferences)
        .Include(u => u.AccessSchedules)
        .ToList();
}
```

### Repository Pattern

Some components use a repository pattern for data access abstraction.

**Example: BaseItemRepository** (`Jellyfin.Server.Implementations/Item/BaseItemRepository.cs`):
```csharp
public sealed class BaseItemRepository : IItemRepository
{
    private readonly IDbContextFactory<JellyfinDbContext> _dbProvider;

    public BaseItemRepository(IDbContextFactory<JellyfinDbContext> dbProvider, ...)
    {
        _dbProvider = dbProvider;
    }

    public async Task SaveItem(BaseItem item)
    {
        await using var dbContext = await _dbProvider.CreateDbContextAsync();
        // ... perform operations
        await dbContext.SaveChangesAsync();
    }
}
```

**Key Repositories**:
- `BaseItemRepository.cs` - Media item data access
- `ChapterRepository.cs` - Chapter data access
- `PeopleRepository.cs` - People/cast data access

### Context Lifecycle

1. DbContext instances are created from a **pooled factory**
2. Each operation creates its own context instance
3. Contexts are disposed after the operation completes
4. The pool reuses context objects for better performance

## Migrations

Jellyfin uses two types of migrations:

### 1. EF Core Migrations

Located in: `src/Jellyfin.Database/Jellyfin.Database.Providers.Sqlite/Migrations/`

These are standard Entity Framework Core migrations that modify the database schema.

**Example Migration** (20240729140605_AddMediaSegments.cs):
```csharp
public partial class AddMediaSegments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MediaSegments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                Type = table.Column<int>(type: "INTEGER", nullable: false),
                EndTicks = table.Column<long>(type: "INTEGER", nullable: false),
                StartTicks = table.Column<long>(type: "INTEGER", nullable: false),
                SegmentProviderId = table.Column<string>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MediaSegments", x => x.Id);
            });
    }
}
```

### 2. Custom Migration Routines

Located in: `Jellyfin.Server/Migrations/Routines/`

These are code-based migrations for data transformations and complex updates.

**Migration Service**: `Jellyfin.Server/Migrations/JellyfinMigrationService.cs`

**Migration Interfaces**:
- `IAsyncMigrationRoutine` - Modern async migration pattern
- `IDatabaseMigrationRoutine` - Obsolete sync migration pattern (deprecated)

**Migration Attributes**:
- `[JellyfinMigration(MigrationStage)]` - Marks when the migration runs
  - `PreStartup` - Before server initialization
  - `Startup` - During server startup
  - `PostStartup` - After server is running
- `[JellyfinMigrationBackup]` - Indicates backup should be created before migration

**Example Migration** (CleanMusicArtist.cs):
```csharp
[JellyfinMigration(JellyfinMigrationStageTypes.PostStartup)]
public class CleanMusicArtist : IAsyncMigrationRoutine
{
    private readonly IDbContextFactory<JellyfinDbContext> _dbProvider;

    public async Task PerformAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken);
        // ... perform data migration
    }
}
```

**Common Migration Routines**:
- `MigrateUserDb.cs` - Migrates user data from old database format
- `MigrateActivityLogDb.cs` - Migrates activity logs
- `MigrateDisplayPreferencesDb.cs` - Migrates UI preferences
- `FixPlaylistOwner.cs` - Data integrity fixes
- `RefreshInternalDateModified.cs` - Updates timestamps

## Locking Strategies

Located in: `src/Jellyfin.Database/Jellyfin.Database.Implementations/Locking/`

Jellyfin provides three configurable database locking strategies to handle concurrency.

### Interface

```csharp
public interface IEntityFrameworkCoreLockingBehavior
{
    void Initialise(DbContextOptionsBuilder optionsBuilder);
    void OnSaveChanges(JellyfinDbContext context, Action saveChanges);
    Task OnSaveChangesAsync(JellyfinDbContext context, Func<Task> saveChanges);
}
```

### 1. NoLockBehavior (Default)

**File**: `NoLockBehavior.cs`

- No application-level locking
- Relies entirely on SQLite's built-in locking mechanisms
- Best performance, lowest overhead
- Suitable for most single-server deployments

### 2. OptimisticLockBehavior

**File**: `OptimisticLockBehavior.cs`

- Retries operations on "database is locked" exceptions
- Uses **Polly** retry policy with exponential backoff
- 15 retry attempts with increasing delays (50ms, 250ms, 500ms, ..., up to 3 seconds)
- Ideal for high-concurrency scenarios
- Custom interceptors for transactions and commands

**Retry Configuration**:
```csharp
var retryPolicy = Policy
    .Handle<DbUpdateException>(IsRetryable)
    .WaitAndRetryAsync(
        retryCount: 15,
        sleepDurationProvider: retryAttempt => TimeSpan.FromMilliseconds(
            Math.Min(Math.Pow(2, retryAttempt) * 50, 3000))
    );
```

### 3. PessimisticLockBehavior

**File**: `PessimisticLockBehavior.cs`

- Acquires exclusive locks before operations
- Blocks other readers during writes
- Prevents conflicts but reduces concurrency
- Useful for scenarios requiring strict serialization

### Concurrency Tokens

Entities can implement `IHasConcurrencyToken` for optimistic concurrency control:

```csharp
public interface IHasConcurrencyToken
{
    uint RowVersion { get; }
    void OnSavingChanges();
}
```

The **User** entity uses this for conflict detection during concurrent updates.

## Database Provider Architecture

The database layer is designed to support multiple database providers through plugins.

### Provider Interface

**File**: `src/Jellyfin.Database/Jellyfin.Implementations/IJellyfinDatabaseProvider.cs`

```csharp
public interface IJellyfinDatabaseProvider
{
    void Initialise(DbContextOptionsBuilder optionsBuilder, DatabaseConfigurationOptions options);
    void OnModelCreating(ModelBuilder modelBuilder);
    void ConfigureConventions(ModelConfigurationBuilder configurationBuilder);
    Task RunScheduledOptimisation(CancellationToken cancellationToken);
    Task RunShutdownTask();
    Task<string> MigrationBackupFast(string outputPath);
    Task RestoreBackupFast(string backupPath);
    Task PurgeDatabase();
}
```

### Provider Registration

Providers are marked with `JellyfinDatabaseProviderKeyAttribute`:

```csharp
[JellyfinDatabaseProviderKey("Jellyfin-SQLite")]
public sealed class SqliteDatabaseProvider : IJellyfinDatabaseProvider
{
    // Implementation
}
```

This design allows third-party plugins to register alternative database providers (PostgreSQL, MySQL, etc.) without modifying core code.

## Directory Structure

```
/opt/src/jellyfin/
├── src/Jellyfin.Database/
│   ├── Jellyfin.Database.Implementations/
│   │   ├── JellyfinDbContext.cs              # Main DbContext
│   │   ├── Entities/                          # 30+ entity classes
│   │   │   ├── User.cs
│   │   │   ├── BaseItemEntity.cs
│   │   │   ├── UserData.cs
│   │   │   ├── MediaStreamInfo.cs
│   │   │   └── ...
│   │   ├── ModelConfiguration/                # Entity configurations
│   │   │   ├── BaseItemConfiguration.cs
│   │   │   ├── UserConfiguration.cs
│   │   │   └── ...
│   │   ├── Locking/                           # Locking strategies
│   │   │   ├── IEntityFrameworkCoreLockingBehavior.cs
│   │   │   ├── NoLockBehavior.cs
│   │   │   ├── OptimisticLockBehavior.cs
│   │   │   └── PessimisticLockBehavior.cs
│   │   ├── DbConfiguration/                   # Configuration classes
│   │   │   ├── DatabaseConfigurationOptions.cs
│   │   │   ├── CustomDatabaseOptions.cs
│   │   │   └── DatabaseLockingBehaviorTypes.cs
│   │   └── Interfaces/                        # Entity interfaces
│   │       ├── IHasConcurrencyToken.cs
│   │       └── IHasPermissions.cs
│   └── Jellyfin.Database.Providers.Sqlite/
│       ├── SqliteDatabaseProvider.cs          # SQLite implementation
│       └── Migrations/                        # 40+ EF Core migrations
│           ├── 20240729140605_AddMediaSegments.cs
│           └── ...
├── Jellyfin.Server.Implementations/
│   ├── Extensions/
│   │   └── ServiceCollectionExtensions.cs     # DI registration
│   ├── DbConfiguration/
│   │   └── DatabaseConfigurationStore.cs      # Config persistence
│   ├── Item/                                  # Repository implementations
│   │   ├── BaseItemRepository.cs
│   │   ├── ChapterRepository.cs
│   │   └── PeopleRepository.cs
│   └── Users/
│       └── UserManager.cs
└── Jellyfin.Server/
    └── Migrations/
        ├── JellyfinMigrationService.cs        # Migration orchestrator
        ├── IAsyncMigrationRoutine.cs
        └── Routines/                          # 30+ custom migrations
            ├── MigrateUserDb.cs
            ├── MigrateActivityLogDb.cs
            └── CleanMusicArtist.cs
```

## Summary

| Aspect | Technology/Pattern | Location |
|--------|-------------------|----------|
| **Database** | SQLite | `{DataPath}/jellyfin.db` |
| **ORM** | Entity Framework Core 8+ | Microsoft.EntityFrameworkCore |
| **Connection** | Pooled DbContextFactory | `ServiceCollectionExtensions.AddJellyfinDbContext()` |
| **Configuration** | JSON-based options | Stored in `database.json` |
| **Locking** | Configurable (NoLock/Optimistic/Pessimistic) | `IEntityFrameworkCoreLockingBehavior` |
| **Migrations** | EF Core + custom routines | `/Migrations/` directories |
| **Backup** | SQLite file copy | `IJellyfinDatabaseProvider.MigrationBackupFast()` |
| **Model Mapping** | Configuration classes | `IEntityTypeConfiguration<T>` pattern |
| **Data Access** | Direct LINQ-to-Entities | Via DbContext factory |
| **Concurrency** | Row version tokens | `IHasConcurrencyToken` interface |
| **Extensibility** | Plugin-based providers | `IJellyfinDatabaseProvider` interface |

This architecture provides a clean, type-safe, and extensible database layer with excellent performance through connection pooling, flexible concurrency strategies, and support for future database provider plugins.
