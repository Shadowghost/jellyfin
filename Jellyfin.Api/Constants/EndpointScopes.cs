namespace Jellyfin.Api.Constants;

/// <summary>
/// The catalog of endpoint scopes. A scope gates access to a family of endpoints; tokens carry
/// one or more scope claims and the <c>full</c> scope satisfies every requirement.
/// </summary>
public static class EndpointScopes
{
    /// <summary>
    /// Unrestricted access. Default for full-session JWTs.
    /// </summary>
    public const string Full = "full";

    /// <summary>
    /// Media stream endpoints (e.g. /Videos/{id}/stream, /Audio/{id}/stream, master playlists).
    /// </summary>
    public const string MediaStream = "media.stream";

    /// <summary>
    /// HLS segment endpoints.
    /// </summary>
    public const string MediaHls = "media.hls";

    /// <summary>
    /// Image endpoints.
    /// </summary>
    public const string MediaImage = "media.image";

    /// <summary>
    /// Subtitle endpoints.
    /// </summary>
    public const string MediaSubtitle = "media.subtitle";

    /// <summary>
    /// Read-only library/item metadata endpoints.
    /// </summary>
    public const string MetadataRead = "metadata.read";

    /// <summary>
    /// DLNA/UPnP endpoints.
    /// </summary>
    public const string Dlna = "dlna";

    /// <summary>
    /// Quick Connect endpoints.
    /// </summary>
    public const string QuickConnect = "quickconnect";

    /// <summary>
    /// Prefix for scope authorization policy names.
    /// </summary>
    public const string PolicyPrefix = "scope:";

    /// <summary>
    /// All gateable scopes (excludes <see cref="Full"/>, which is the implicit wildcard). Used to
    /// register one authorization policy per scope.
    /// </summary>
    public static readonly string[] All =
    {
        MediaStream,
        MediaHls,
        MediaImage,
        MediaSubtitle,
        MetadataRead,
        Dlna,
        QuickConnect
    };

    /// <summary>
    /// Gets the authorization policy name for a scope.
    /// </summary>
    /// <param name="scope">The scope.</param>
    /// <returns>The policy name.</returns>
    public static string PolicyFor(string scope) => PolicyPrefix + scope;
}
