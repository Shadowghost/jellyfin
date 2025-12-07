# Jellyfin Media Scanner - Code Examples & Implementation Details

## 1. Core Scanning Flow Examples

### Example 1: Starting a Library Scan

```csharp
// From LibraryManager.ValidateMediaLibrary()
public Task ValidateMediaLibrary(IProgress<double> progress, CancellationToken cancellationToken)
{
    // Queue the scan to run in background
    _taskManager.CancelIfRunningAndQueue<RefreshMediaLibraryTask>();
    return Task.CompletedTask;
}

// Then executed by RefreshMediaLibraryTask.ExecuteAsync()
public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    progress.Report(0);
    
    // This is where the actual scanning happens
    await ((LibraryManager)_libraryManager)
        .ValidateMediaLibraryInternal(progress, cancellationToken)
        .ConfigureAwait(false);
}
```

### Example 2: Resolver Priority Order

```csharp
// From MovieResolver
public class MovieResolver : BaseVideoResolver<Video>, IMultiItemResolver
{
    // Priority: Fourth - runs after Series (Second), Books, and Music Album (Third)
    public override ResolverPriority Priority => ResolverPriority.Fourth;
}

// From EpisodeResolver
public class EpisodeResolver : BaseVideoResolver<Episode>
{
    // Priority not explicitly set, inherited from base
    // Also runs relatively late, after Series detection
}

// Resolver execution order (IItemResolver.cs):
public enum ResolverPriority
{
    First,    // Special resolvers
    Second,   // SeriesResolver
    Third,    // MusicAlbumResolver  
    Fourth,   // MovieResolver
    Fifth     // AudioResolver
}
```

### Example 3: Multi-Item Resolution for Movies

```csharp
// From MovieResolver.ResolveMultiple()
public MultiItemResolverResult ResolveMultiple(
    Folder parent,
    List<FileSystemMetadata> files,
    CollectionType? collectionType,
    IDirectoryService directoryService)
{
    var result = ResolveMultipleInternal(parent, files, collectionType);
    
    if (result is not null)
    {
        foreach (var item in result.Items)
        {
            SetInitialItemValues((Video)item, null);
        }
    }
    
    return result;
}

// Internal resolution
private MultiItemResolverResult ResolveMultipleInternal(
    Folder parent,
    List<FileSystemMetadata> files,
    CollectionType? collectionType)
{
    if (collectionType == CollectionType.movies)
    {
        // For movie collection: resolve all as movies
        return ResolveVideos<Movie>(parent, files, true, collectionType, true);
    }
    
    if (collectionType == CollectionType.musicvideos)
    {
        // For music videos: resolve as music videos
        return ResolveVideos<MusicVideo>(parent, files, true, collectionType, false);
    }
    
    // ... other cases
}
```

## 2. Media Type-Specific Resolution

### Example 4: Episode Detection from Filename

```csharp
// From Emby.Naming.TV.EpisodeResolver
public EpisodeInfo? Resolve(
    string path,
    bool isDirectory,
    bool? isNamed = null,
    bool? isOptimistic = null,
    bool? supportsAbsoluteNumbers = null,
    bool fillExtendedInfo = true)
{
    // Check file extension
    if (!isDirectory)
    {
        var extension = Path.GetExtension(path);
        if (!_options.VideoFileExtensions.Contains(extension, StringComparison.OrdinalIgnoreCase))
        {
            // Check if it's a stub type
            if (!StubResolver.TryResolveFile(path, _options, out stubType))
            {
                return null;
            }
            isStub = true;
        }
        container = extension.TrimStart('.');
    }
    
    // Parse 3D format (e.g., "Show s01e01.3D.mkv")
    var format3DResult = Format3DParser.Parse(path, _options);
    
    // Parse episode info from path
    var parsingResult = new EpisodePathParser(_options)
        .Parse(path, isDirectory, isNamed, isOptimistic, supportsAbsoluteNumbers, fillExtendedInfo);
    
    if (!parsingResult.Success && !isStub)
    {
        return null;
    }
    
    return new EpisodeInfo(path)
    {
        Container = container,
        IsStub = isStub,
        EndingEpisodeNumber = parsingResult.EndingEpisodeNumber,
        EpisodeNumber = parsingResult.EpisodeNumber,
        SeasonNumber = parsingResult.SeasonNumber,
        SeriesName = parsingResult.SeriesName,
        StubType = stubType,
        Is3D = format3DResult.Is3D,
        Format3D = format3DResult.Format3D,
        IsByDate = parsingResult.IsByDate,
        Day = parsingResult.Day,
        Month = parsingResult.Month,
        Year = parsingResult.Year
    };
}
```

### Example 5: Music Album Detection

```csharp
// From MusicAlbumResolver.cs
protected override MusicAlbum Resolve(ItemResolveArgs args)
{
    var collectionType = args.GetCollectionType();
    var isMusicMediaFolder = collectionType == CollectionType.music;
    
    // Must be in music collection
    if (!isMusicMediaFolder)
    {
        return null;
    }
    
    // Must be a directory
    if (!args.IsDirectory)
    {
        return null;
    }
    
    // Can't be nested album
    if (args.HasParent<MusicAlbum>())
    {
        return null;
    }
    
    // Can't be root
    if (args.Parent.IsRoot)
    {
        return null;
    }
    
    // Must contain music files
    return IsMusicAlbum(args) ? new MusicAlbum() : null;
}

// Check if folder contains audio files
private bool IsMusicAlbum(ItemResolveArgs args)
{
    return ContainsMusic(
        args.GetActualFileSystemChildren().ToList(),
        true,
        _directoryService);
}
```

### Example 6: Photo vs Metadata Image Detection

```csharp
// From PhotoResolver.cs
protected override Photo? Resolve(ItemResolveArgs args)
{
    if (!args.IsDirectory)
    {
        var collectionType = args.CollectionType;
        
        if (collectionType == CollectionType.photos 
            || (collectionType == CollectionType.homevideos && args.LibraryOptions.EnablePhotos))
        {
            if (IsImageFile(args.Path, _imageProcessor))
            {
                var filename = Path.GetFileNameWithoutExtension(args.Path.AsSpan());
                
                // Get all files in directory
                var files = _directoryService.GetFiles(
                    Path.GetDirectoryName(args.Path) 
                    ?? throw new InvalidOperationException());
                
                // Check if this image is owned by a video
                foreach (var file in files)
                {
                    if (IsOwnedByMedia(_namingOptions, file.FullName, filename))
                    {
                        return null;  // It's metadata, not a photo
                    }
                }
                
                return new Photo { Path = args.Path };
            }
        }
    }
    
    return null;
}

// Determines if image file belongs to video metadata
internal static bool IsOwnedByMedia(
    NamingOptions namingOptions,
    string file,
    ReadOnlySpan<char> imageFilename)
{
    return VideoResolver.IsVideoFile(file, namingOptions) 
        && IsOwnedByResolvedMedia(file, imageFilename);
}

// Checks if image filename matches video filename
internal static bool IsOwnedByResolvedMedia(
    ReadOnlySpan<char> file,
    ReadOnlySpan<char> imageFilename)
    => imageFilename.StartsWith(
        Path.GetFileNameWithoutExtension(file),
        StringComparison.OrdinalIgnoreCase);
```

## 3. Provider ID Extraction

### Example 7: Extracting IMDb and TMDb IDs from Paths

```csharp
// From MovieResolver.SetProviderIdsFromPath()
private static void SetProviderIdsFromPath(Video item)
{
    if (item is Movie || item is MusicVideo)
    {
        // Get just the item's name (not parent path)
        var justName = item.IsInMixedFolder 
            ? Path.GetFileName(item.Path.AsSpan()) 
            : Path.GetFileName(item.ContainingFolderPath.AsSpan());
        
        if (!justName.IsEmpty)
        {
            // Look for TMDb ID in brackets
            // Example: Movie Title (2020) [tmdbid=12345]
            var tmdbid = justName.GetAttributeValue("tmdbid");
            item.TrySetProviderId(MetadataProvider.Tmdb, tmdbid);
        }
        
        // Look for IMDb ID in the full path
        // Example: Movie [imdbid=tt1234567].mkv
        if (!string.IsNullOrEmpty(item.Path))
        {
            var imdbid = item.Path.AsSpan().GetAttributeValue("imdbid");
            item.TrySetProviderId(MetadataProvider.Imdb, imdbid);
        }
    }
}
```

## 4. External File & Metadata Detection

### Example 8: Detecting External Subtitles

```csharp
// From MediaInfoResolver.GetExternalFiles()
public IReadOnlyList<ExternalPathParserResult> GetExternalFiles(
    Video video,
    IDirectoryService directoryService,
    bool clearCache)
{
    if (!video.IsFileProtocol)
    {
        return Array.Empty<ExternalPathParserResult>();
    }
    
    string folder = video.ContainingFolderPath;
    if (!_fileSystem.DirectoryExists(folder))
    {
        return Array.Empty<ExternalPathParserResult>();
    }
    
    // Get all files in folder and internal metadata path
    var files = directoryService.GetFilePaths(folder, clearCache, true).ToList();
    files.Remove(video.Path);  // Remove the video file itself
    
    var internalMetadataPath = video.GetInternalMetadataPath();
    if (_fileSystem.DirectoryExists(internalMetadataPath))
    {
        files.AddRange(directoryService.GetFilePaths(internalMetadataPath, clearCache, true));
    }
    
    if (files.Count == 0)
    {
        return Array.Empty<ExternalPathParserResult>();
    }
    
    var externalPathInfos = new List<ExternalPathParserResult>();
    ReadOnlySpan<char> prefix = video.FileNameWithoutExtension;
    
    // Match files that start with video filename
    foreach (var file in files)
    {
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(file.AsSpan());
        
        // File must start with video filename
        if (fileNameWithoutExtension.Length >= prefix.Length
            && prefix.Equals(fileNameWithoutExtension[..prefix.Length], StringComparison.OrdinalIgnoreCase)
            && (fileNameWithoutExtension.Length == prefix.Length || 
                _namingOptions.MediaFlagDelimiters.Contains(fileNameWithoutExtension[prefix.Length])))
        {
            // Parse the remaining part for language/flags
            // Example: movie.en.forced.srt → language=en, forced=true
            var externalPathInfo = _externalPathParser.ParseFile(
                file,
                fileNameWithoutExtension[prefix.Length..].ToString());
            
            if (externalPathInfo is not null)
            {
                externalPathInfos.Add(externalPathInfo);
            }
        }
    }
    
    return externalPathInfos;
}
```

### Example 9: External File Naming Patterns

```csharp
// Supported patterns from ExternalPathParser

// Subtitles
movie.srt                          // Default English
movie.en.srt                       // English with language code
movie.eng.srt                      // English with 3-letter code
movie.fr-CA.srt                    // French Canadian
movie.en.forced.srt                // English, forced
movie.en.sdh.srt                   // English, Deaf and Hard of Hearing
movie.en.title.srt                 // English, with title flag

// Audio
movie.aac                          // AAC audio
movie.ac3                          // AC3 audio
movie.en.aac                       // English AAC
movie.fr.flac                      // French FLAC

// Flags
.forced    → IsForced = true
.sdh       → IsHearingImpaired = true
.hi        → IsHearingImpaired = true
.title     → Title flag
```

## 5. Parallel Processing

### Example 10: Recursive Validation with Parallelization

```csharp
// From Folder.cs
private async Task RefreshMetadataRecursive(
    IList<BaseItem> children,
    MetadataRefreshOptions refreshOptions,
    bool recursive,
    IProgress<double> progress,
    CancellationToken cancellationToken)
{
    // Use RunTasks to parallelize child metadata refresh
    await RunTasks(
        (baseItem, innerProgress) => RefreshChildMetadata(
            baseItem,
            refreshOptions,
            recursive && baseItem.IsFolder,
            innerProgress,
            cancellationToken),
        children,
        progress,
        cancellationToken)
        .ConfigureAwait(false);
}

// RunTasks implementation
private async Task RunTasks<T>(
    Func<T, IProgress<double>, Task> task,
    IList<T> children,
    IProgress<double> progress,
    CancellationToken cancellationToken)
{
    // Delegate to scheduler for parallel execution with concurrency limits
    await LimitedConcurrencyLibraryScheduler
        .Enqueue(
            children.ToArray(),
            task,
            progress,
            cancellationToken)
        .ConfigureAwait(false);
}
```

## 6. NFO File Parsing

### Example 11: Reading NFO Metadata

```csharp
// From BaseNfoProvider<T>
public async Task<MetadataResult<T>> GetMetadata(
    ItemInfo info,
    IDirectoryService directoryService,
    CancellationToken cancellationToken)
{
    var result = new MetadataResult<T>();
    
    // Find NFO file associated with item
    var file = GetXmlFile(info, directoryService);
    
    if (file is null)
    {
        return result;
    }
    
    var path = file.FullName;
    
    try
    {
        result.Item = new T
        {
            IndexNumber = info.IndexNumber
        };
        
        // Parse XML and populate item
        Fetch(result, path, cancellationToken);
        result.HasMetadata = true;
    }
    catch (FileNotFoundException)
    {
        result.HasMetadata = false;
    }
    catch (IOException)
    {
        result.HasMetadata = false;
    }
    
    return result;
}

// Check if NFO file changed since last refresh
public bool HasChanged(BaseItem item, IDirectoryService directoryService)
{
    var file = GetXmlFile(new ItemInfo(item), directoryService);
    
    if (file is null)
    {
        return false;
    }
    
    // File is considered changed if newer than item's last save time
    return file.Exists && _fileSystem.GetLastWriteTimeUtc(file) > item.DateLastSaved;
}
```

## 7. Post-Scan Tasks

### Example 12: Running Post-Scan Tasks

```csharp
// From LibraryManager.RunPostScanTasks()
private async Task RunPostScanTasks(
    IProgress<double> progress,
    CancellationToken cancellationToken)
{
    var tasks = PostScanTasks.ToList();
    var numComplete = 0;
    var numTasks = tasks.Count;
    
    foreach (var task in tasks)
    {
        // Prevent access to modified closure
        var currentNumComplete = numComplete;
        
        var innerProgress = new Progress<double>(pct =>
        {
            // Calculate progress across all tasks
            double innerPercent = pct;
            innerPercent /= 100;
            innerPercent += currentNumComplete;
            
            innerPercent /= numTasks;
            innerPercent *= 100;
            
            progress.Report(innerPercent);
        });
        
        _logger.LogDebug("Running post-scan task {0}", task.GetType().Name);
        
        try
        {
            await task.Run(innerProgress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Post-scan task cancelled: {0}", task.GetType().Name);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running post-scan task");
        }
        
        numComplete++;
        double percent = numComplete;
        percent /= numTasks;
        progress.Report(percent * 100);
    }
    
    // Update inherited metadata values in database
    _itemRepository.UpdateInheritedValues();
    
    progress.Report(100);
}
```

## 8. Creating Custom Resolver

### Example 13: Implementing Custom Media Type Resolver

```csharp
// Template for custom resolver
public class CustomMediaResolver : ItemResolver<CustomMedia>
{
    private readonly NamingOptions _namingOptions;
    
    public CustomMediaResolver(NamingOptions namingOptions)
    {
        _namingOptions = namingOptions;
    }
    
    // Set priority to avoid conflicts
    public override ResolverPriority Priority => ResolverPriority.Fourth;
    
    protected override CustomMedia Resolve(ItemResolveArgs args)
    {
        // Get collection type
        var collectionType = args.GetCollectionType();
        
        // Only process in specific collection
        if (collectionType != CollectionType.YourCustomType)
        {
            return null;
        }
        
        // Check if it's a file or directory
        if (args.IsDirectory)
        {
            // Directory resolution logic
            var files = args.FileSystemChildren
                .Where(f => IsValidExtension(f.Name))
                .ToList();
            
            if (files.Any())
            {
                return new CustomMedia
                {
                    Path = args.Path,
                    Name = Path.GetFileName(args.Path)
                };
            }
        }
        else
        {
            // File resolution logic
            if (IsValidExtension(args.Path))
            {
                return new CustomMedia
                {
                    Path = args.Path,
                    IsInMixedFolder = true
                };
            }
        }
        
        return null;
    }
    
    private bool IsValidExtension(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".custom", StringComparison.OrdinalIgnoreCase);
    }
}
```

## 9. Caching & Performance

### Example 14: Multi-Level Item Caching

```csharp
// From LibraryManager - In-memory LRU cache
private readonly FastConcurrentLru<Guid, BaseItem> _cache;

// Usage when retrieving items
public BaseItem? GetItemById(Guid id)
{
    // Try in-memory cache first
    if (_cache.TryGetValue(id, out var item))
    {
        return item;
    }
    
    // Fall back to database
    var dbItem = ItemRepository.GetItemById(id);
    if (dbItem != null)
    {
        _cache.AddOrUpdate(id, dbItem);
    }
    
    return dbItem;
}

// From Folder - Get cached children at scan start
protected IReadOnlyList<BaseItem> GetCachedChildren()
{
    return ItemRepository.GetItemList(new InternalItemsQuery
    {
        Parent = this,
        GroupByPresentationUniqueKey = false,
        DtoOptions = new DtoOptions(true)
    });
}

// From DirectoryService - File listing cache
public IFileSystemMetadata[] GetFileSystemEntries(
    string path,
    bool clearCache = false,
    bool allowLongPaths = false)
{
    // Caches directory listings to avoid repeated file system calls
    if (clearCache)
    {
        // Clear cache if fresh data needed
    }
    
    return _fileSystem.GetDirectoryContents(path);
}
```

---

## Implementation Patterns

### Pattern 1: Adding Support for New File Format

1. Create naming parser in `/Emby.Naming/YourType/`
2. Create resolver in `/Emby.Server.Implementations/Library/Resolvers/YourType/`
3. Implement `IItemResolver` interface
4. Register in dependency injection container
5. Add to appropriate collection type in `NamingOptions`

### Pattern 2: Handling File Variations

```csharp
// Check for multiple versions/editions
var videos = files
    .Select(f => VideoResolver.Resolve(f.FullName, f.IsDirectory, namingOptions))
    .Where(v => v is not null)
    .ToList();

// Use VideoListResolver to group them
var resolvedVideos = VideoListResolver.Resolve(
    videos,
    namingOptions,
    supportMultiEditions: true,
    parseName: true);

// Handle alternate versions
foreach (var video in resolvedVideos)
{
    var item = new Video
    {
        Path = video.Files[0].Path,
        AdditionalParts = video.Files.Skip(1).Select(f => f.Path).ToArray(),
        LocalAlternateVersions = video.AlternateVersions
            .Select(a => a.Path)
            .ToArray()
    };
}
```

---

These code examples demonstrate the key patterns and implementations used throughout the Jellyfin media scanner system.

