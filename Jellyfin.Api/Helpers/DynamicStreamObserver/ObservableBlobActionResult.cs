using System;
using System.IO;
using System.Threading.Tasks;
using MediaBrowser.Controller.Streaming;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Api.Helpers.DynamicStreamObserver;

/// <summary>
///     Acts like a <see cref="PhysicalFileResult"/> but limits the transfer speed of the requested file.
/// </summary>
public class ObservableBlobActionResult : FileResult
{
    private readonly string? _fileName;
    private readonly Stream? _fileStream;
    private readonly Guid _itemId;

    /// <summary>
    /// Initializes a new instance of the <see cref="ObservableBlobActionResult"/> class.
    /// </summary>
    /// <param name="fileStream">The Progressive filestream.</param>
    /// <param name="contentType">The Content Type.</param>
    /// <param name="itemId">The item Id.</param>
    public ObservableBlobActionResult(Stream fileStream, string contentType, Guid itemId)
        : base(contentType)
    {
        _fileStream = fileStream;
        _itemId = itemId;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ObservableBlobActionResult"/> class.
    /// </summary>
    /// <param name="fileName">The Filename.</param>
    /// <param name="contentType">The Content Type.</param>
    /// <param name="itemId">The item Id.</param>
    public ObservableBlobActionResult(string fileName, string contentType, Guid itemId)
        : base(contentType)
    {
        _fileName = fileName;
        _itemId = itemId;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ObservableBlobActionResult"/> class.
    /// </summary>
    /// <param name="fileName">The Filename.</param>
    /// <param name="contentType">The Content Type.</param>
    /// <param name="itemId">The item Id.</param>
    public ObservableBlobActionResult(string fileName, MediaTypeHeaderValue contentType, Guid itemId)
        : this(fileName, contentType.ToString(), itemId)
    {
    }

    internal string? FileName => _fileName;

    internal Stream? FileStream => _fileStream;

    /// <inheritdoc />
    public override Task ExecuteResultAsync(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var executor = context.HttpContext.RequestServices
            .GetRequiredService<IActionResultExecutor<ObservableBlobActionResult>>()
            as ObservableBlobResultExecutor;
        executor!.ItemId = _itemId;
        return executor.ExecuteAsync(context, this);
    }
}
