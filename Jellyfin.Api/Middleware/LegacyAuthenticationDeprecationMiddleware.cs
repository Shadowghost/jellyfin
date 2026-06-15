using System;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Api.Middleware;

/// <summary>
/// Adds RFC 8594 <c>Sunset</c> and RFC 9745 <c>Deprecation</c> response headers to requests that
/// authenticated via the legacy MediaBrowser token scheme, signalling clients to migrate to
/// Jellyfin-issued JWTs.
/// </summary>
public class LegacyAuthenticationDeprecationMiddleware
{
    private const string DeprecationDocs = "https://jellyfin.org/docs/general/server/authentication";

    private readonly RequestDelegate _next;

    /// <summary>
    /// Initializes a new instance of the <see cref="LegacyAuthenticationDeprecationMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next delegate in the pipeline.</param>
    public LegacyAuthenticationDeprecationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Executes the middleware action.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <returns>The async task.</returns>
    public async Task Invoke(HttpContext httpContext)
    {
        if (string.Equals(
            httpContext.User.Identity?.AuthenticationType,
            AuthenticationSchemes.LegacyMediaBrowserToken,
            StringComparison.Ordinal))
        {
            var headers = httpContext.Response.Headers;
            headers["Deprecation"] = "true";
            headers[HeaderNames.Link] = $"<{DeprecationDocs}>; rel=\"deprecation\"; type=\"text/html\"";
        }

        await _next(httpContext).ConfigureAwait(false);
    }
}
