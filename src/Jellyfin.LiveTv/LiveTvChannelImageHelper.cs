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
    /// Applies the channel icon from guide or tuner metadata when it actually changed.
    /// </summary>
    /// <remarks>
    /// Re-applying the icon resets the primary image to the (possibly remote) source, which forces the
    /// following metadata refresh to re-download and re-encode it. Doing that unconditionally on every
    /// guide refresh made refreshes take hours on large channel lists (see issue #17259). For remote
    /// icons the picon file on the server can change while the URL stays the same, so a conditional
    /// request is used to detect real changes cheaply instead of always re-downloading. The source URL
    /// and its HTTP cache validators are stored on the image itself. This method only mutates
    /// <paramref name="item"/>, so it is safe to call concurrently for distinct channels.
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

        var primary = item.GetImageInfo(ImageType.Primary, 0);

        // Apply unconditionally when the channel has no primary image yet or the source path/URL changed.
        if (primary is null || !string.Equals(primary.Source, newImageSource, StringComparison.Ordinal))
        {
            ApplySource(item, newImageSource, etag: null, lastModified: null);
            return true;
        }

        // Same local (tuner) path or a non-http source: keep the cached image, nothing to detect.
        if (!isRemote)
        {
            return false;
        }

        // Same remote URL: only re-apply (and re-download) when the picon content actually changed.
        var probe = await ProbeRemoteAsync(newImageSource, primary.ETag, primary.SourceLastModified, httpClientFactory, logger, cancellationToken).ConfigureAwait(false);

        if (probe.Changed)
        {
            ApplySource(item, newImageSource, probe.ETag, probe.LastModified);
            return true;
        }

        if (probe.StoreValidators)
        {
            primary.ETag = probe.ETag;
            primary.SourceLastModified = probe.LastModified;
        }

        return false;
    }

    private static void ApplySource(BaseItem item, string source, string? etag, DateTime? lastModified)
    {
        item.SetImagePath(ImageType.Primary, source);

        // SetImagePath preserves fields it does not know about, so update the source/validators explicitly.
        var image = item.GetImageInfo(ImageType.Primary, 0);
        if (image is not null)
        {
            image.Source = source;
            image.ETag = etag;
            image.SourceLastModified = lastModified;
        }
    }

    private static async Task<RemoteProbeResult> ProbeRemoteAsync(
        string url,
        string? storedETag,
        DateTime? storedLastModified,
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

            if (storedLastModified.HasValue)
            {
                request.Headers.TryAddWithoutValidation(
                    "If-Modified-Since",
                    storedLastModified.Value.ToUniversalTime().ToString("R", CultureInfo.InvariantCulture));
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
            var newLastModified = response.Content.Headers.LastModified?.UtcDateTime;

            var hadValidators = !string.IsNullOrEmpty(storedETag) || storedLastModified.HasValue;
            var hasValidators = !string.IsNullOrEmpty(newETag) || newLastModified.HasValue;

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
                unchanged = newLastModified.HasValue && newLastModified == storedLastModified;
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

    private readonly record struct RemoteProbeResult(bool Changed, bool StoreValidators, string? ETag, DateTime? LastModified);
}
