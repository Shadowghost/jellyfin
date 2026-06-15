using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Controller.Authentication
{
    /// <summary>
    /// Contract for plugins that own an authentication flow end-to-end: they authenticate against their
    /// backend (LDAP, OIDC, header-trust, custom HTTP), return an opaque token of their own choosing, and
    /// validate that token on every request. Jellyfin never speaks to the external backend itself.
    /// </summary>
    public interface IAuthenticationPlugin
    {
        /// <summary>
        /// Gets the stable plugin id. Tokens issued by this plugin are prefixed "pt_{Id}_".
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Gets the display name for the client-side login picker.
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// Gets a value indicating whether the plugin is currently enabled in its own configuration.
        /// </summary>
        bool Enabled { get; }

        /// <summary>
        /// Gets the client-side input this plugin requires.
        /// </summary>
        PluginAuthenticationCapabilities Capabilities { get; }

        /// <summary>
        /// Authenticates a login request against the plugin's backend and returns identity plus an opaque token.
        /// </summary>
        /// <param name="request">The login request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The authentication result.</returns>
        Task<PluginAuthenticationResult> AuthenticateAsync(PluginAuthenticationRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Validates a previously-issued opaque token and returns the current identity and permissions.
        /// Called on every authenticated request for this plugin's tokens; implementations cache aggressively.
        /// </summary>
        /// <param name="opaqueToken">The opaque token (without the "pt_{id}_" prefix).</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The validation result.</returns>
        Task<PluginTokenValidationResult> ValidateTokenAsync(string opaqueToken, CancellationToken cancellationToken);

        /// <summary>
        /// Revokes a token on logout. Best-effort; the plugin should evict caches and call upstream revocation if available.
        /// </summary>
        /// <param name="opaqueToken">The opaque token (without the "pt_{id}_" prefix).</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the revocation.</returns>
        Task RevokeAsync(string opaqueToken, CancellationToken cancellationToken);
    }
}
