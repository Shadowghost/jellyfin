using System;

namespace Jellyfin.Api.Models.TempTokenDtos;

/// <summary>
/// The result of minting a temp token.
/// </summary>
public class TempTokenResultDto
{
    /// <summary>
    /// Gets or sets the issued token.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the token expires.
    /// </summary>
    public DateTime ExpiresAt { get; set; }
}
