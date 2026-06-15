using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Controller.Authentication
{
    /// <summary>
    /// Registry of authentication plugins. Fronts plugin lookup, the cross-request validation cache,
    /// and the per-plugin circuit breaker.
    /// </summary>
    public interface IAuthenticationPluginRegistry
    {
        /// <summary>
        /// Gets an enabled plugin by id (case-insensitive), or <c>null</c> if not registered or disabled.
        /// </summary>
        /// <param name="pluginId">The plugin id.</param>
        /// <returns>The plugin, or <c>null</c>.</returns>
        IAuthenticationPlugin? GetById(string pluginId);

        /// <summary>
        /// Gets all currently-enabled plugins (for the discovery endpoint).
        /// </summary>
        /// <returns>The enabled plugins.</returns>
        IReadOnlyList<IAuthenticationPlugin> GetEnabledPlugins();

        /// <summary>
        /// Gets a value indicating whether the circuit breaker is open for a plugin.
        /// </summary>
        /// <param name="pluginId">The plugin id.</param>
        /// <returns>Whether the circuit is open.</returns>
        bool IsCircuitOpen(string pluginId);

        /// <summary>
        /// Records a successful validation for the plugin's circuit breaker.
        /// </summary>
        /// <param name="pluginId">The plugin id.</param>
        void RecordSuccess(string pluginId);

        /// <summary>
        /// Records a failed validation for the plugin's circuit breaker.
        /// </summary>
        /// <param name="pluginId">The plugin id.</param>
        void RecordFailure(string pluginId);

        /// <summary>
        /// Validates an opaque token through the cross-request cache, falling through to the plugin on a miss.
        /// </summary>
        /// <param name="plugin">The owning plugin.</param>
        /// <param name="opaqueToken">The opaque token (without the "pt_{id}_" prefix).</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The validation result.</returns>
        Task<PluginTokenValidationResult> ValidateAsync(IAuthenticationPlugin plugin, string opaqueToken, CancellationToken cancellationToken);
    }
}
