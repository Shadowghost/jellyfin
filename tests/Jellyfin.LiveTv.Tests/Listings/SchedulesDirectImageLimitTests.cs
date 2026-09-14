using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Xunit;
using SchedulesDirectProvider = Jellyfin.LiveTv.Listings.SchedulesDirect;

namespace Jellyfin.LiveTv.Tests.Listings;

public class SchedulesDirectImageLimitTests : IDisposable
{
    private const string ImageUrl = "https://json.schedulesdirect.org/20141201/image/assets/p1_b.jpg?token=abc";
    private const int FailureLimit = 10;

    private readonly string _cachePath = Path.Combine(Path.GetTempPath(), "jf-sd-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ReportImageDownloadFailure_RepeatedRejections_StopsFurtherDownloads()
    {
        using var provider = CreateProvider();

        for (var i = 0; i < FailureLimit - 1; i++)
        {
            provider.ReportImageDownloadFailure(ImageUrl, HttpStatusCode.Forbidden);
            Assert.True(provider.CanDownloadImage(ImageUrl));
        }

        provider.ReportImageDownloadFailure(ImageUrl, HttpStatusCode.Forbidden);

        Assert.True(provider.IsImageDailyLimitActive());
        Assert.False(provider.CanDownloadImage(ImageUrl));
    }

    [Fact]
    public void ReportImageDownloadFailure_NotFound_DoesNotCountTowardsLimit()
    {
        using var provider = CreateProvider();

        // A missing artwork is a per-image problem and must not disable the whole day.
        for (var i = 0; i < FailureLimit * 2; i++)
        {
            provider.ReportImageDownloadFailure(ImageUrl, HttpStatusCode.NotFound);
        }

        Assert.False(provider.IsImageDailyLimitActive());
        Assert.True(provider.CanDownloadImage(ImageUrl));
    }

    [Fact]
    public void ReportImageDownloadSuccess_ResetsFailureStreak()
    {
        using var provider = CreateProvider();

        for (var i = 0; i < FailureLimit - 1; i++)
        {
            provider.ReportImageDownloadFailure(ImageUrl, HttpStatusCode.Forbidden);
        }

        provider.ReportImageDownloadSuccess(ImageUrl);
        provider.ReportImageDownloadFailure(ImageUrl, HttpStatusCode.Forbidden);

        Assert.False(provider.IsImageDailyLimitActive());
    }

    [Fact]
    public void ReportImageDownloadFailure_ForeignUrl_IsIgnored()
    {
        using var provider = CreateProvider();

        for (var i = 0; i < FailureLimit * 2; i++)
        {
            provider.ReportImageDownloadFailure("https://example.com/image.jpg", HttpStatusCode.Forbidden);
        }

        Assert.False(provider.IsImageDailyLimitActive());
    }

    [Fact]
    public void CanDownloadImage_NonSchedulesDirectUrl_AlwaysAllowed()
    {
        using var provider = CreateProvider();

        for (var i = 0; i < FailureLimit; i++)
        {
            provider.ReportImageDownloadFailure(ImageUrl, HttpStatusCode.Forbidden);
        }

        Assert.False(provider.CanDownloadImage(ImageUrl));
        Assert.True(provider.CanDownloadImage("https://example.com/image.jpg"));
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing && Directory.Exists(_cachePath))
        {
            Directory.Delete(_cachePath, true);
        }
    }

    private SchedulesDirectProvider CreateProvider()
    {
        var messageHandler = new Mock<HttpMessageHandler>();
        messageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(() => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(messageHandler.Object));

        var appPaths = new Mock<IApplicationPaths>();
        appPaths.SetupGet(x => x.CachePath).Returns(_cachePath);

        return new SchedulesDirectProvider(
            NullLogger<SchedulesDirectProvider>.Instance,
            httpClientFactory.Object,
            appPaths.Object);
    }
}
