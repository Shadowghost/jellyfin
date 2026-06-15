using System;
using Jellyfin.Api.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Api.Auth;

/// <summary>
/// Selects the concrete authentication scheme for an incoming request. Used as the
/// <c>ForwardDefaultSelector</c> of the <see cref="AuthenticationSchemes.JellyfinSelector"/> policy scheme.
/// </summary>
public static class JellyfinSchemeSelector
{
    /// <summary>
    /// Resolve the authentication scheme to forward the request to.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>The name of the scheme that should authenticate the request.</returns>
    public static string Select(HttpContext context)
    {
        // Precedence:
        //   1. "Authorization: Bearer <jwt>"        -> JellyfinJwt (full session). External-provider
        //                                              routing by issuer is added in Phase 7.
        //   2. "?token=<jwt>" (query only)          -> JellyfinTempJwt (short-lived, scoped tokens only;
        //                                              long-lived tokens must never travel in the query).
        //   3. "Authorization: MediaBrowser ..." / "?api_key=" / X-Emby-Token / none
        //                                           -> LegacyMediaBrowserToken (transitional).
        string authHeader = context.Request.Headers[HeaderNames.Authorization].ToString();
        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var bearerToken = authHeader["Bearer ".Length..].Trim();
            if (bearerToken.StartsWith(AuthenticationSchemes.PluginTokenPrefix, StringComparison.Ordinal))
            {
                return AuthenticationSchemes.PluginToken;
            }

            return AuthenticationSchemes.JellyfinJwt;
        }

        if (!string.IsNullOrEmpty(context.Request.Query["token"]))
        {
            return AuthenticationSchemes.JellyfinTempJwt;
        }

        return AuthenticationSchemes.LegacyMediaBrowserToken;
    }
}
