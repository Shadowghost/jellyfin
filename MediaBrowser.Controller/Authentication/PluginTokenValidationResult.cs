using System;

namespace MediaBrowser.Controller.Authentication
{
    /// <summary>
    /// The result of validating a plugin-owned token. The identity is authoritative and reconciled onto
    /// the Jellyfin user record on every successful validation.
    /// </summary>
    public sealed class PluginTokenValidationResult
    {
        /// <summary>
        /// Gets a value indicating whether the token is valid.
        /// </summary>
        public bool Valid { get; init; }

        /// <summary>
        /// Gets the current identity, or <c>null</c> when invalid.
        /// </summary>
        public PluginUserIdentity? Identity { get; init; }

        /// <summary>
        /// Gets when the token expires.
        /// </summary>
        public DateTimeOffset ExpiresAt { get; init; }
    }
}
