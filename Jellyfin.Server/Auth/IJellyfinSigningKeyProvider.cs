using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;

namespace Jellyfin.Server.Auth;

/// <summary>
/// Provides the symmetric signing key(s) used for Jellyfin-issued JWTs.
/// </summary>
public interface IJellyfinSigningKeyProvider
{
    /// <summary>
    /// Gets the current signing key used to issue new tokens.
    /// </summary>
    /// <returns>The current signing key.</returns>
    SecurityKey GetCurrentSigningKey();

    /// <summary>
    /// Gets all keys valid for token validation (current and the previous key, so tokens issued
    /// before a rotation still validate).
    /// </summary>
    /// <returns>The validation keys.</returns>
    IReadOnlyList<SecurityKey> GetValidationKeys();

    /// <summary>
    /// Rotates the signing keys: the current key becomes the previous key and a new current key is
    /// generated and persisted.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the rotation.</returns>
    Task RotateAsync(CancellationToken cancellationToken = default);
}
