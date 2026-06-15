namespace Jellyfin.Api.Models.UserDtos;

/// <summary>
/// Describes a selectable authentication provider for the client-side login picker.
/// </summary>
public record AuthenticationProviderInfo
{
    /// <summary>
    /// Gets the provider id. "builtin" denotes the native Jellyfin username/password flow.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the declared capabilities as a string.
    /// </summary>
    public required string Capabilities { get; init; }
}
