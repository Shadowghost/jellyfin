using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.LiveTv;

/// <summary>
/// Helpers for keeping Live TV channel icons in sync with guide data.
/// </summary>
internal static class LiveTvChannelImageHelper
{
    /// <summary>
    /// Provider id used to remember the icon source and its HTTP cache validators between refreshes.
    /// The value is an opaque, newline-separated triple of <c>source</c>, <c>ETag</c> and
    /// <c>Last-Modified</c>; it is stored in a single key to keep the exposed metadata surface small.
    /// </summary>
    internal const string ImageCacheKey = "ChannelImageCache";

    private static readonly char[] _cacheSeparator = ['\n'];

    /// <summary>
    /// Applies the channel icon from guide or tuner metadata when it actually changed.
    /// </summary>
    /// <remarks>
    /// Re-applying the icon resets the primary image to the (possibly remote) source, which forces the
    /// following metadata refresh to re-download and re-encode it.
    /// </remarks>
    /// <param name="item">The channel item.</param>
    /// <param name="imagePath">The local image path from the tuner, if any.</param>
    /// <param name="imageUrl">The remote image URL from the guide provider, if any.</param>
    /// <param name="httpClientFactory">The <see cref="IHttpClientFactory"/> used for change detection.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> when the item image metadata was updated.</returns>
    internal static async Task<bool> UpdateChannelImageIfNeededAsync(
        BaseItem item,
        string? imagePath,
        string? imageUrl,
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var newImageSource = !string.IsNullOrWhiteSpace(imagePath)
            ? imagePath
            : imageUrl;

        if (string.IsNullOrWhiteSpace(newImageSource))
        {
            return false;
        }

        // Only remote http(s) sources can be probed for changes; a local tuner path is treated as-is.
        var isRemote = string.IsNullOrWhiteSpace(imagePath)
            && (newImageSource.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || newImageSource.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

        var (cachedSource, cachedETag, cachedLastModified) = ReadCache(item);

        // Apply unconditionally when the channel has no primary image yet or the source path/URL changed.
        if (!item.HasImage(ImageType.Primary) || !string.Equals(cachedSource, newImageSource, StringComparison.Ordinal))
        {
            ApplySource(item, newImageSource);
            // Reset validators; they are recorded on the next refresh once the icon has been downloaded.
            WriteCache(item, newImageSource, null, null);
            return true;
        }

        // Same local (tuner) path or a non-http source: keep the cached image, nothing to detect.
        if (!isRemote)
        {
            return false;
        }

        // Same remote URL: only re-apply (and re-download) when the picon content actually changed.
        var probe = await ProbeRemoteAsync(newImageSource, cachedETag, cachedLastModified, httpClientFactory, logger, cancellationToken).ConfigureAwait(false);
        if (probe.StoreValidators)
        {
            WriteCache(item, newImageSource, probe.ETag, probe.LastModified);
        }

        if (probe.Changed)
        {
            ApplySource(item, newImageSource);
            return true;
        }

        return false;
    }

    private static void ApplySource(BaseItem item, string source)
        => item.SetImagePath(ImageType.Primary, source);

    private static async Task<RemoteProbeResult> ProbeRemoteAsync(
        string url,
        string? storedETag,
        string? storedLastModified,
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(storedETag))
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", storedETag);
            }

            if (!string.IsNullOrEmpty(storedLastModified))
            {
                request.Headers.TryAddWithoutValidation("If-Modified-Since", storedLastModified);
            }

            var client = httpClientFactory.CreateClient(NamedClient.Default);
            // ResponseHeadersRead avoids buffering the body: on a 200 we inspect the validators and
            // dispose without downloading the payload (the actual download happens later, only if needed).
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new RemoteProbeResult(false, false, storedETag, storedLastModified);
            }

            if (!response.IsSuccessStatusCode)
            {
                // Can't determine the state; keep the cached icon rather than re-downloading every refresh.
                logger.LogDebug("Channel icon {Url} returned {StatusCode}; keeping cached image", url, response.StatusCode);
                return new RemoteProbeResult(false, false, storedETag, storedLastModified);
            }

            var newETag = response.Headers.ETag?.ToString();
            var newLastModified = response.Content.Headers.LastModified?.ToString("R", CultureInfo.InvariantCulture);

            var hadValidators = !string.IsNullOrEmpty(storedETag) || !string.IsNullOrEmpty(storedLastModified);
            var hasValidators = !string.IsNullOrEmpty(newETag) || !string.IsNullOrEmpty(newLastModified);

            if (!hasValidators)
            {
                // The server exposes no cache validators, so we can't tell whether it changed: re-download.
                return new RemoteProbeResult(true, true, null, null);
            }

            if (!hadValidators)
            {
                // First time we record validators for an already-cached icon; assume the cache is current.
                return new RemoteProbeResult(false, true, newETag, newLastModified);
            }

            // Prefer the ETag when both sides have one: it is authoritative, while Last-Modified is often
            // inconsistent across load-balanced origins and would otherwise cause spurious re-downloads.
            bool unchanged;
            if (!string.IsNullOrEmpty(newETag) && !string.IsNullOrEmpty(storedETag))
            {
                unchanged = string.Equals(newETag, storedETag, StringComparison.Ordinal);
            }
            else
            {
                unchanged = !string.IsNullOrEmpty(newLastModified)
                    && string.Equals(newLastModified, storedLastModified, StringComparison.Ordinal);
            }

            return new RemoteProbeResult(!unchanged, true, newETag, newLastModified);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Network error, timeout or unsupported method: keep the cached icon.
            logger.LogDebug(ex, "Unable to check channel icon {Url} for changes; keeping cached image", url);
            return new RemoteProbeResult(false, false, storedETag, storedLastModified);
        }
    }

    private static (string? Source, string? ETag, string? LastModified) ReadCache(BaseItem item)
    {
        var raw = item.GetProviderId(ImageCacheKey);
        if (string.IsNullOrEmpty(raw))
        {
            return (null, null, null);
        }

        var parts = raw.Split(_cacheSeparator);
        return (
            parts.Length > 0 ? NullIfEmpty(parts[0]) : null,
            parts.Length > 1 ? NullIfEmpty(parts[1]) : null,
            parts.Length > 2 ? NullIfEmpty(parts[2]) : null);
    }

    private static void WriteCache(BaseItem item, string source, string? etag, string? lastModified)
        => item.SetProviderId(ImageCacheKey, string.Join('\n', source, etag ?? string.Empty, lastModified ?? string.Empty));

    private static string? NullIfEmpty(string value)
        => string.IsNullOrEmpty(value) ? null : value;

    private readonly record struct RemoteProbeResult(bool Changed, bool StoreValidators, string? ETag, string? LastModified);
}
