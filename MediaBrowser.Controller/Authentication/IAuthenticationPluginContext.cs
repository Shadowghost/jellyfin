using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Controller.Authentication
{
    /// <summary>
    /// Lets authentication plugins signal cache invalidation to the server when their internal state
    /// changes (e.g. an IdP webhook fired, or permissions were updated externally).
    /// </summary>
    public interface IAuthenticationPluginContext
    {
        /// <summary>
        /// Invalidates the server-side validation cache for a specific token.
        /// </summary>
        /// <param name="opaqueToken">The opaque token (without the "pt_{id}_" prefix).</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the invalidation.</returns>
        Task InvalidateTokenAsync(string opaqueToken, CancellationToken cancellationToken);

        /// <summary>
        /// Invalidates the server-side validation cache for all of a user's tokens from a plugin.
        /// </summary>
        /// <param name="pluginId">The plugin id.</param>
        /// <param name="externalUserId">The external user id.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the invalidation.</returns>
        Task InvalidateUserAsync(string pluginId, string externalUserId, CancellationToken cancellationToken);
    }
}
