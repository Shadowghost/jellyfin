using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Controller.Security
{
    /// <summary>
    /// Persists issued temp tokens for listing and revocation.
    /// </summary>
    public interface ITempTokenStore
    {
        /// <summary>
        /// Records an issued temp token.
        /// </summary>
        /// <param name="jti">The unique token id.</param>
        /// <param name="actingUserId">The acting user id.</param>
        /// <param name="issuedAt">When the token was issued.</param>
        /// <param name="expiresAt">When the token expires.</param>
        /// <param name="scopes">The granted scopes.</param>
        /// <param name="itemId">The bound item id, if any.</param>
        /// <param name="label">The label.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task.</returns>
        Task RecordAsync(string jti, Guid actingUserId, DateTime issuedAt, DateTime expiresAt, IReadOnlyList<string> scopes, string? itemId, string label, CancellationToken cancellationToken = default);

        /// <summary>
        /// Determines whether the token with the given jti has been revoked.
        /// </summary>
        /// <param name="jti">The token id.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Whether the token is revoked.</returns>
        Task<bool> IsRevokedAsync(string jti, CancellationToken cancellationToken = default);

        /// <summary>
        /// Revokes a token. The requesting user must be the acting user or an administrator.
        /// </summary>
        /// <param name="tokenId">The token database id.</param>
        /// <param name="requestingUserId">The requesting user id.</param>
        /// <param name="isAdministrator">Whether the requesting user is an administrator.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Whether a token was revoked.</returns>
        Task<bool> RevokeAsync(int tokenId, Guid requestingUserId, bool isAdministrator, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the outstanding (non-revoked, non-expired) tokens for a user.
        /// </summary>
        /// <param name="actingUserId">The acting user id.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The outstanding tokens.</returns>
        Task<IReadOnlyList<TempTokenInfo>> GetForUserAsync(Guid actingUserId, CancellationToken cancellationToken = default);
    }
}
