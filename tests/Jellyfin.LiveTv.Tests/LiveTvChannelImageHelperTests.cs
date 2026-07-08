using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Xunit;

namespace Jellyfin.LiveTv.Tests;

public class LiveTvChannelImageHelperTests
{
    private const string IconUrl = "https://example.com/icon.png";

    [Fact]
    public async Task UpdateChannelImageIfNeeded_NoSource_DoesNotUpdate()
    {
        var channel = new LiveTvChannel { Name = "Test Channel" };

        var updated = await LiveTvChannelImageHelper.UpdateChannelImageIfNeededAsync(
            channel,
            null,
            null,
            CreateHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.NotModified)),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.False(updated);
        Assert.False(channel.HasImage(ImageType.Primary));
    }

    [Fact]
    public async Task UpdateChannelImageIfNeeded_NewChannelWithUrl_AppliesUrl()
    {
        var channel = new LiveTvChannel { Name = "Test Channel" };

        var updated = await LiveTvChannelImageHelper.UpdateChannelImageIfNeededAsync(
            channel,
            null,
            IconUrl,
            CreateHttpClientFactory(_ => throw new InvalidOperationException("No request expected for a new channel")),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.True(updated);
        Assert.True(channel.HasImage(ImageType.Primary));
        Assert.Equal(IconUrl, channel.GetImagePath(ImageType.Primary));
    }

    [Fact]
    public async Task UpdateChannelImageIfNeeded_ChangedUrl_Updates()
    {
        var channel = new LiveTvChannel { Name = "Test Channel" };
        SeedCachedIcon(channel, IconUrl);

        const string NewUrl = "https://example.com/new-icon.png";
        var updated = await LiveTvChannelImageHelper.UpdateChannelImageIfNeededAsync(
            channel,
            null,
            NewUrl,
            CreateHttpClientFactory(_ => throw new InvalidOperationException("No request expected when the URL changed")),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.True(updated);
        Assert.Equal(NewUrl, channel.GetImagePath(ImageType.Primary));
    }

    [Fact]
    public async Task UpdateChannelImageIfNeeded_SameUrlNotModified_DoesNotUpdate()
    {
        var channel = new LiveTvChannel { Name = "Test Channel" };
        SeedCachedIcon(channel, IconUrl, etag: "\"cached\"");

        var updated = await LiveTvChannelImageHelper.UpdateChannelImageIfNeededAsync(
            channel,
            null,
            IconUrl,
            CreateHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.NotModified)),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.False(updated);
    }

    [Fact]
    public async Task UpdateChannelImageIfNeeded_SameUrlChangedContent_Updates()
    {
        var channel = new LiveTvChannel { Name = "Test Channel" };
        SeedCachedIcon(channel, IconUrl, etag: "\"old\"");

        var updated = await LiveTvChannelImageHelper.UpdateChannelImageIfNeededAsync(
            channel,
            null,
            IconUrl,
            CreateHttpClientFactory(_ => CreateResponse(etag: "\"new\"")),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.True(updated);
    }

    [Fact]
    public async Task UpdateChannelImageIfNeeded_SameUrlStableETagFlakyLastModified_DoesNotUpdate()
    {
        // A stable strong ETag is authoritative even if Last-Modified differs (common on CDNs).
        var channel = new LiveTvChannel { Name = "Test Channel" };
        SeedCachedIcon(channel, IconUrl, etag: "\"stable\"", lastModified: "Mon, 01 Jan 2024 00:00:00 GMT");

        var updated = await LiveTvChannelImageHelper.UpdateChannelImageIfNeededAsync(
            channel,
            null,
            IconUrl,
            CreateHttpClientFactory(_ => CreateResponse(etag: "\"stable\"", lastModified: "Tue, 02 Jan 2024 00:00:00 GMT")),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.False(updated);
    }

    [Fact]
    public async Task UpdateChannelImageIfNeeded_SameUrlFirstValidatorSeen_DoesNotUpdate()
    {
        var channel = new LiveTvChannel { Name = "Test Channel" };
        SeedCachedIcon(channel, IconUrl);

        var updated = await LiveTvChannelImageHelper.UpdateChannelImageIfNeededAsync(
            channel,
            null,
            IconUrl,
            CreateHttpClientFactory(_ => CreateResponse(etag: "\"first\"")),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.False(updated);
    }

    [Fact]
    public async Task UpdateChannelImageIfNeeded_SameUrlNoValidators_Updates()
    {
        var channel = new LiveTvChannel { Name = "Test Channel" };
        SeedCachedIcon(channel, IconUrl);

        var updated = await LiveTvChannelImageHelper.UpdateChannelImageIfNeededAsync(
            channel,
            null,
            IconUrl,
            CreateHttpClientFactory(_ => CreateResponse()),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.True(updated);
    }

    [Fact]
    public async Task UpdateChannelImageIfNeeded_SameNonHttpSource_DoesNotProbeOrUpdate()
    {
        const string LocalPath = "/tuner/icons/channel.png";
        var channel = new LiveTvChannel { Name = "Test Channel" };
        SeedCachedIcon(channel, LocalPath);

        // A non-http source must not trigger an HTTP request.
        var updated = await LiveTvChannelImageHelper.UpdateChannelImageIfNeededAsync(
            channel,
            null,
            LocalPath,
            CreateHttpClientFactory(_ => throw new InvalidOperationException("No request expected for a non-http source")),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.False(updated);
    }

    private static void SeedCachedIcon(LiveTvChannel channel, string source, string? etag = null, string? lastModified = null)
    {
        // Set the image directly (SetImagePath would resolve local paths against the static file system,
        // which is not initialized in unit tests).
        channel.SetImage(new ItemImageInfo { Path = source, Type = ImageType.Primary }, 0);
        channel.SetProviderId(
            LiveTvChannelImageHelper.ImageCacheKey,
            string.Join('\n', source, etag ?? string.Empty, lastModified ?? string.Empty));
    }

    private static HttpResponseMessage CreateResponse(string? etag = null, string? lastModified = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Array.Empty<byte>())
        };

        if (etag is not null)
        {
            response.Headers.ETag = new EntityTagHeaderValue(etag);
        }

        if (lastModified is not null)
        {
            response.Content.Headers.LastModified = DateTimeOffset.Parse(lastModified, System.Globalization.CultureInfo.InvariantCulture);
        }

        return response;
    }

    private static IHttpClientFactory CreateHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((request, _) => Task.FromResult(responder(request)));

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler.Object));

        return factory.Object;
    }
}
