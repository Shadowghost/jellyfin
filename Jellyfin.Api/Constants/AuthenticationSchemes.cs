namespace Jellyfin.Api.Constants;

/// <summary>
/// Authentication schemes for user authentication in the API.
/// </summary>
public static class AuthenticationSchemes
{
    /// <summary>
    /// Scheme name for the custom legacy authentication.
    /// </summary>
    /// <remarks>
    /// Kept for compile-compatibility within the codebase (e.g. Swagger). Identical in value to
    /// <see cref="LegacyMediaBrowserToken"/>.
    /// </remarks>
    public const string CustomAuthentication = "CustomAuthentication";

    /// <summary>
    /// Scheme name for the transitional legacy MediaBrowser token handler. Sunset in a later phase.
    /// </summary>
    public const string LegacyMediaBrowserToken = "CustomAuthentication";

    /// <summary>
    /// The policy scheme that selects the concrete authentication scheme per request.
    /// </summary>
    public const string JellyfinSelector = "Jellyfin";

    /// <summary>
    /// Scheme name for full-session Jellyfin-issued JWTs.
    /// </summary>
    public const string JellyfinJwt = "JellyfinJwt";

    /// <summary>
    /// Scheme name for short-lived, narrowly-scoped Jellyfin-issued JWTs.
    /// </summary>
    public const string JellyfinTempJwt = "JellyfinTempJwt";

    /// <summary>
    /// Scheme name for plugin-owned opaque tokens. A single scheme fronts all authentication plugins;
    /// the token's <see cref="PluginTokenPrefix"/> identifies the owning plugin.
    /// </summary>
    public const string PluginToken = "PluginToken";

    /// <summary>
    /// Prefix for plugin-owned wire tokens: "pt_{pluginId}_{opaqueToken}".
    /// </summary>
    public const string PluginTokenPrefix = "pt_";
}
