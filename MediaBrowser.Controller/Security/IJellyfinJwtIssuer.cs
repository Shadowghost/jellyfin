using System;
using System.Collections.Generic;

namespace MediaBrowser.Controller.Security
{
    /// <summary>
    /// Issues Jellyfin-signed JSON Web Tokens.
    /// </summary>
    public interface IJellyfinJwtIssuer
    {
        /// <summary>
        /// Issues a full-session JWT for an authenticated user.
        /// </summary>
        /// <param name="userId">The user id.</param>
        /// <param name="deviceId">The device id.</param>
        /// <param name="client">The client app name.</param>
        /// <param name="version">The client app version.</param>
        /// <param name="deviceName">The device name.</param>
        /// <param name="lifetime">The token lifetime.</param>
        /// <returns>The serialized JWT.</returns>
        string IssueSession(
            Guid userId,
            string? deviceId,
            string? client,
            string? version,
            string? deviceName,
            TimeSpan lifetime);

        /// <summary>
        /// Issues a short-lived, narrowly-scoped temp JWT.
        /// </summary>
        /// <param name="actingUserId">The id of the user on whose behalf the token acts.</param>
        /// <param name="scopes">The granted scopes.</param>
        /// <param name="itemId">The optional item id the token is bound to.</param>
        /// <param name="lifetime">The token lifetime (capped by the issuer).</param>
        /// <param name="label">A human-readable label.</param>
        /// <param name="jti">The unique token id (jti) assigned to the issued token.</param>
        /// <returns>The serialized JWT.</returns>
        string IssueTemp(
            Guid actingUserId,
            IReadOnlyList<string> scopes,
            string? itemId,
            TimeSpan lifetime,
            string label,
            out string jti);
    }
}
