namespace Jellyfin.Api.Constants;

/// <summary>
/// Constants describing Jellyfin-issued JWTs.
/// </summary>
public static class JellyfinTokenConstants
{
    /// <summary>
    /// Audience for full-session tokens.
    /// </summary>
    public const string ApiAudience = "urn:jellyfin:api";

    /// <summary>
    /// Audience for short-lived temp tokens.
    /// </summary>
    public const string TempAudience = "urn:jellyfin:temp";

    /// <summary>
    /// Issuer prefix; the full issuer is this prefix followed by the server instance id.
    /// </summary>
    public const string IssuerPrefix = "urn:jellyfin:";

    /// <summary>
    /// The standard JWT subject claim ("sub").
    /// </summary>
    public const string SubjectClaim = "sub";

    /// <summary>
    /// Maximum lifetime allowed for a temp token.
    /// </summary>
    public const int TempTokenMaxLifetimeSeconds = 3600;

    /// <summary>
    /// Minimum lifetime allowed for a temp token.
    /// </summary>
    public const int TempTokenMinLifetimeSeconds = 60;
}
