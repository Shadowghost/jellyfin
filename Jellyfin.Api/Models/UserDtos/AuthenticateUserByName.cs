namespace Jellyfin.Api.Models.UserDtos;

/// <summary>
/// The authenticate user by name request body.
/// </summary>
public class AuthenticateUserByName
{
    /// <summary>
    /// Gets or sets the username.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Gets or sets the plain text password.
    /// </summary>
    public string? Pw { get; set; }

    /// <summary>
    /// Gets or sets the id of the authentication plugin to authenticate against. When null, empty or
    /// "builtin", the built-in Jellyfin authentication flow is used.
    /// </summary>
    public string? AuthProvider { get; set; }
}
