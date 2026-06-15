using System;

namespace MediaBrowser.Controller.Authentication
{
    /// <summary>
    /// The result of a plugin login attempt.
    /// </summary>
    public sealed class PluginAuthenticationResult
    {
        /// <summary>
        /// Gets a value indicating whether authentication succeeded.
        /// </summary>
        public bool Success { get; init; }

        /// <summary>
        /// Gets the failure reason (logged, not returned to the client verbatim).
        /// </summary>
        public string? FailureReason { get; init; }

        /// <summary>
        /// Gets the opaque token the plugin chose. The server prefixes it with "pt_{pluginId}_" before
        /// returning it to the client.
        /// </summary>
        public string OpaqueToken { get; init; } = string.Empty;

        /// <summary>
        /// Gets when the token expires.
        /// </summary>
        public DateTimeOffset ExpiresAt { get; init; }

        /// <summary>
        /// Gets the authenticated user's identity.
        /// </summary>
        public PluginUserIdentity Identity { get; init; } = default!;
    }
}
