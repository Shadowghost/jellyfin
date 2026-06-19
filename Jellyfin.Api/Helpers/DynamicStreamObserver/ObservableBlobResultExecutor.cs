using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Extensions;
using MediaBrowser.Controller.Streaming;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Api.Helpers.DynamicStreamObserver;

/// <summary>
/// Provides methods to throttle the download of a physical file.
/// </summary>
public class ObservableBlobResultExecutor : FileResultExecutorBase, IActionResultExecutor<ObservableBlobActionResult>
{
    private readonly IStreamObserverService _streamObserverService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ObservableBlobResultExecutor"/> class.
    /// </summary>
    /// <param name="logger">The Logger.</param>
    /// <param name="streamObserverService">Service to provide access to the set limits for download.</param>
    public ObservableBlobResultExecutor(
        ILogger<ObservableBlobResultExecutor> logger,
        IStreamObserverService streamObserverService)
        : base(logger)
    {
        _streamObserverService = streamObserverService;
    }

    /// <summary>
    /// Gets the itemID associated with this stream.
    /// </summary>
    public Guid ItemId { get; internal set; }

    /// <inheritdoc />
    public Task ExecuteAsync(ActionContext context, ObservableBlobActionResult result)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(result);

        DateTimeOffset? lastModified = null;
        long? fileLength = null;
        if (result.FileName is not null)
        {
            var fileInfo = GetFileInfo(result.FileName);
            if (!fileInfo.Exists)
            {
                throw new FileNotFoundException(result.FileName);
            }

            if (!Path.IsPathRooted(result.FileName))
            {
                throw new NotSupportedException("Path is not rooted");
            }

            lastModified = result.LastModified ?? fileInfo.LastModified;
            fileLength = fileInfo.Length;
        }
        else if (result.FileStream is not null)
        {
            lastModified = result.LastModified;
            try
            {
                if (result.FileStream is not ProgressiveFileStream)
                {
                    fileLength = result.FileStream.CanRead ? result.FileStream.Length : 0;
                }
            }
            catch (NotSupportedException)
            {
                // the source stream may or may not support length for example LiveTV doesn't.
            }
        }

        var (range, rangeLength, serveBody) = SetHeadersAndLog(
            context,
            result,
            fileLength,
            result.EnableRangeProcessing,
            lastModified,
            result.EntityTag);

        if (serveBody)
        {
            return WriteFileAsyncInternal(context.HttpContext, result, range, rangeLength, Logger);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Get the file metadata for a path.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The <see cref="FileMetadata"/> for the path.</returns>
    private FileMetadata GetFileInfo(string path)
    {
        var fileInfo = new FileInfo(path);

        // It means we are dealing with a symlink and need to get the information
        // from the target file instead.
        if (fileInfo.Exists && !string.IsNullOrEmpty(fileInfo.LinkTarget))
        {
            fileInfo = (FileInfo?)fileInfo.ResolveLinkTarget(returnFinalTarget: true) ?? fileInfo;
        }

        return new FileMetadata
        {
            Exists = fileInfo.Exists,
            Length = fileInfo.Length,
            LastModified = fileInfo.LastWriteTimeUtc,
        };
    }

    internal Task WriteFileAsyncInternal(
        HttpContext httpContext,
        ObservableBlobActionResult result,
        RangeItemHeaderValue? range,
        long rangeLength,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        ArgumentNullException.ThrowIfNull(result);

        if (range != null && rangeLength == 0)
        {
            return Task.CompletedTask;
        }

        var response = httpContext.Response;

        var userId = httpContext.User.GetUserId();

        if (range != null)
        {
            return SendFileAsync(
                response.Body,
                result,
                offset: range.From ?? 0L,
                count: rangeLength,
                userId,
                ItemId,
                CancellationToken.None);
        }

        return SendFileAsync(
            response.Body,
            result,
            offset: 0,
            count: null,
            userId,
            ItemId,
            CancellationToken.None);
    }

    private async Task SendFileAsync(
        Stream destination,
        ObservableBlobActionResult result,
        long offset,
        long? count,
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        Stream fileStream;
        const int bufferSize = 1024 * 16;
        if (result.FileName is not null)
        {
            var fileInfo = new FileInfo(result.FileName);
            if (offset < 0 || offset > fileInfo.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), offset, string.Empty);
            }

            if (count.HasValue &&
                (count.Value < 0 || count.Value > fileInfo.Length - offset))
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, string.Empty);
            }

            cancellationToken.ThrowIfCancellationRequested();

            fileStream = new FileStream(
                result.FileName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: bufferSize,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        else
        {
            fileStream = result.FileStream!;
        }

        await using (fileStream)
        {
            if (fileStream.CanSeek && offset > 0)
            {
                fileStream.Seek(offset, SeekOrigin.Begin);
            }

            await CopyToAsync(fileStream, destination, count, bufferSize, userId, itemId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task CopyToAsync(
        Stream source,
        Stream destination,
        long? count,
        int bufferSize,
        Guid userId,
        Guid itemId,
        CancellationToken cancel)
    {
        var bytesRemaining = count;
        var streamMetricCollector = _streamObserverService.BeginStream(itemId, userId);

        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            while (true)
            {
                // The natural end of the range.
                if (bytesRemaining is <= 0)
                {
                    return;
                }

                cancel.ThrowIfCancellationRequested();

                var readLength = buffer.Length;
                if (bytesRemaining.HasValue)
                {
                    readLength = (int)Math.Min(bytesRemaining.GetValueOrDefault(), (long)readLength);
                }

                var read = await source.ReadAsync(buffer.AsMemory(0, readLength), cancel)
                    .ConfigureAwait(false);

                if (bytesRemaining.HasValue)
                {
                    bytesRemaining -= read;
                }

                // End of the source stream.
                if (read == 0)
                {
                    return;
                }

                cancel.ThrowIfCancellationRequested();
                await destination.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
                streamMetricCollector.Collect(read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            streamMetricCollector.Dispose();
        }
    }

    private class FileMetadata
    {
        public bool Exists { get; set; }

        public long Length { get; set; }

        public DateTimeOffset LastModified { get; set; }
    }
}
