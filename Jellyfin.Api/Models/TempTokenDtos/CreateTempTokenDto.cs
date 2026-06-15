using System.Collections.Generic;

namespace Jellyfin.Api.Models.TempTokenDtos;

/// <summary>
/// Request to mint a short-lived, scoped temp token.
/// </summary>
public class CreateTempTokenDto
{
    /// <summary>
    /// Gets or sets the requested scopes (must be endpoint scopes; the full scope is not grantable).
    /// </summary>
    public IReadOnlyList<string> Scopes { get; set; } = new List<string>();

    /// <summary>
    /// Gets or sets the optional item id to bind the token to.
    /// </summary>
    public string? ItemId { get; set; }

    /// <summary>
    /// Gets or sets the requested lifetime in seconds (clamped to [60, 3600]).
    /// </summary>
    public int TtlSeconds { get; set; }

    /// <summary>
    /// Gets or sets a human-readable label.
    /// </summary>
    public string Label { get; set; } = string.Empty;
}
