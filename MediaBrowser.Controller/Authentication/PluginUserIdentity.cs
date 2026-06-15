using System.Collections.Generic;
using Jellyfin.Database.Implementations.Enums;

namespace MediaBrowser.Controller.Authentication
{
    /// <summary>
    /// The current identity of a user as reported by an authentication plugin. Returned from both
    /// authentication and token validation; on validation it is authoritative and reconciled onto the
    /// Jellyfin user record.
    /// </summary>
    public sealed class PluginUserIdentity
    {
        /// <summary>
        /// Gets the stable identifier from the external system. The (plugin id, external user id) pair
        /// is the user's identity in Jellyfin.
        /// </summary>
        public string ExternalUserId { get; init; } = string.Empty;

        /// <summary>
        /// Gets the username.
        /// </summary>
        public string Username { get; init; } = string.Empty;

        /// <summary>
        /// Gets the email address, if any.
        /// </summary>
        public string? Email { get; init; }

        /// <summary>
        /// Gets a value indicating whether a missing user should be auto-provisioned. Governs creation
        /// only; it has no effect on returning users.
        /// </summary>
        public bool ShouldAutoProvision { get; init; }

        /// <summary>
        /// Gets a value indicating whether the user is an administrator.
        /// </summary>
        public bool IsAdministrator { get; init; }

        /// <summary>
        /// Gets optional permission overrides to reconcile onto the user record.
        /// </summary>
        public IReadOnlyDictionary<PermissionKind, bool>? PermissionOverrides { get; init; }

        /// <summary>
        /// Gets optional additional scopes to grant the resulting principal.
        /// </summary>
        public IReadOnlyList<string>? AdditionalScopes { get; init; }
    }
}
