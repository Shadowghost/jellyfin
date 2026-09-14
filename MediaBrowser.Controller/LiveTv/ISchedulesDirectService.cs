using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Controller.LiveTv;

/// <summary>
/// Provides Schedules Direct specific operations.
/// </summary>
public interface ISchedulesDirectService
{
    /// <summary>
    /// Gets the available countries from the Schedules Direct API, using a file cache.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A stream containing the raw JSON response.</returns>
    Task<Stream> GetAvailableCountries(CancellationToken cancellationToken);

    /// <summary>
    /// Gets a value indicating whether the Schedules Direct daily image download limit is currently active.
    /// </summary>
    /// <returns><c>true</c> if the image limit has been hit and has not yet reset; otherwise <c>false</c>.</returns>
    bool IsImageDailyLimitActive();

    /// <summary>
    /// Gets a value indicating whether an image may be downloaded from the given url.
    /// </summary>
    /// <param name="imageUrl">The image url.</param>
    /// <returns><c>false</c> for a Schedules Direct url while the daily image limit is active; otherwise <c>true</c>.</returns>
    bool CanDownloadImage(string imageUrl);

    /// <summary>
    /// Reports a successful image download, so that a Schedules Direct failure streak is reset.
    /// </summary>
    /// <param name="imageUrl">The image url that was downloaded.</param>
    void ReportImageDownloadSuccess(string imageUrl);

    /// <summary>
    /// Reports a failed image download so that repeated rejections from Schedules Direct
    /// stop further image requests instead of running into an account lockout.
    /// </summary>
    /// <param name="imageUrl">The image url that failed.</param>
    /// <param name="statusCode">The HTTP status code of the failure, if any.</param>
    void ReportImageDownloadFailure(string imageUrl, HttpStatusCode? statusCode);

    /// <summary>
    /// Gets a value indicating whether the Schedules Direct service is available.
    /// Returns <c>false</c> if a permanent account error has occurred or a transient backoff is active.
    /// </summary>
    /// <returns><c>true</c> if the service can accept requests; otherwise <c>false</c>.</returns>
    bool IsServiceAvailable();
}
