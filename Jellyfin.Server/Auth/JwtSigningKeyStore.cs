namespace Jellyfin.Server.Auth;

/// <summary>
/// Persisted form of the JWT signing keys (base64-encoded).
/// </summary>
public sealed class JwtSigningKeyStore
{
    /// <summary>
    /// Gets or sets the base64-encoded current signing key.
    /// </summary>
    public string Current { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the base64-encoded previous signing key (still valid for validation).
    /// </summary>
    public string Previous { get; set; } = string.Empty;
}
