namespace Jellyfin.Api.Constants;

/// <summary>
/// Claim types carried by Jellyfin-issued JWTs.
/// </summary>
public static class JellyfinClaimTypes
{
    /// <summary>
    /// A granted endpoint scope. May appear multiple times.
    /// </summary>
    public const string Scope = "jellyfin:scope";

    /// <summary>
    /// The item id a token is bound to (resource binding for temp tokens).
    /// </summary>
    public const string ItemId = "jellyfin:iid";

    /// <summary>
    /// A human-readable label for an issued token.
    /// </summary>
    public const string Label = "jellyfin:label";

    /// <summary>
    /// The id of the user a temp token acts on behalf of.
    /// </summary>
    public const string ActingUser = "act";

    /// <summary>
    /// The id of the authentication plugin that owns the request's token.
    /// </summary>
    public const string PluginId = "jellyfin:plugin";

    /// <summary>
    /// A token identifier: the jti for JWTs, or the token hash for plugin tokens.
    /// </summary>
    public const string TokenId = "jellyfin:tid";
}
