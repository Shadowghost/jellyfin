using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using Jellyfin.Data;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Api.Auth
{
    /// <summary>
    /// Authentication handler for plugin-owned opaque tokens ("pt_{pluginId}_{opaqueToken}"). Resolves the
    /// owning plugin, validates the token through the registry (cache + circuit breaker) and reconciles the
    /// resulting identity onto the Jellyfin user record.
    /// </summary>
    public class PluginAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private const string PerRequestKey = "plugin-validation";

        private readonly IAuthenticationPluginRegistry _registry;
        private readonly IUserManager _userManager;
        private readonly ILogger<PluginAuthenticationHandler> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginAuthenticationHandler"/> class.
        /// </summary>
        /// <param name="registry">The authentication plugin registry.</param>
        /// <param name="userManager">The user manager.</param>
        /// <param name="options">Options monitor.</param>
        /// <param name="logger">The logger factory.</param>
        /// <param name="encoder">The url encoder.</param>
        public PluginAuthenticationHandler(
            IAuthenticationPluginRegistry registry,
            IUserManager userManager,
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
            _registry = registry;
            _userManager = userManager;
            _logger = logger.CreateLogger<PluginAuthenticationHandler>();
        }

        /// <inheritdoc />
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var token = ExtractToken();
            if (string.IsNullOrEmpty(token))
            {
                return AuthenticateResult.NoResult();
            }

            if (!token.StartsWith(AuthenticationSchemes.PluginTokenPrefix, StringComparison.Ordinal))
            {
                return AuthenticateResult.NoResult();
            }

            var body = token[AuthenticationSchemes.PluginTokenPrefix.Length..];
            var separatorIndex = body.IndexOf('_', StringComparison.Ordinal);
            if (separatorIndex <= 0 || separatorIndex == body.Length - 1)
            {
                return AuthenticateResult.Fail("Malformed plugin token.");
            }

            var pluginId = body[..separatorIndex];
            var inner = body[(separatorIndex + 1)..];

            var plugin = _registry.GetById(pluginId);
            if (plugin is null)
            {
                return AuthenticateResult.Fail("Unknown plugin.");
            }

            if (_registry.IsCircuitOpen(pluginId))
            {
                Response.Headers["Retry-After"] = "30";
                return AuthenticateResult.Fail("Plugin temporarily unavailable.");
            }

            PluginTokenValidationResult result;
            if (Context.Items.TryGetValue(PerRequestKey, out var existing) && existing is PluginTokenValidationResult perRequest)
            {
                result = perRequest;
            }
            else
            {
                try
                {
                    result = await _registry.ValidateAsync(plugin, inner, Context.RequestAborted).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Plugin {PluginId} threw while validating a token.", pluginId);
                    _registry.RecordFailure(pluginId);
                    return AuthenticateResult.Fail("Authentication failed.");
                }
            }

            if (!result.Valid || result.Identity is null)
            {
                _registry.RecordFailure(pluginId);
                return AuthenticateResult.Fail("Invalid token.");
            }

            Context.Items[PerRequestKey] = result;
            _registry.RecordSuccess(pluginId);

            var user = await _userManager.ResolveOrProvisionPluginUserAsync(pluginId, result.Identity, Context.RequestAborted).ConfigureAwait(false);
            if (user is null)
            {
                return AuthenticateResult.Fail("User could not be resolved.");
            }

            var claims = new List<Claim>
            {
                new Claim(InternalClaimTypes.UserId, user.Id.ToString("N", CultureInfo.InvariantCulture)),
                new Claim(ClaimTypes.Name, result.Identity.Username),
                new Claim(ClaimTypes.Role, result.Identity.IsAdministrator ? UserRoles.Administrator : UserRoles.User),
                new Claim(JellyfinClaimTypes.PluginId, pluginId),
                new Claim(JellyfinClaimTypes.Scope, EndpointScopes.Full)
            };

            if (result.Identity.AdditionalScopes is not null)
            {
                foreach (var scope in result.Identity.AdditionalScopes)
                {
                    claims.Add(new Claim(JellyfinClaimTypes.Scope, scope));
                }
            }

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return AuthenticateResult.Success(ticket);
        }

        private string? ExtractToken()
        {
            var authHeader = Request.Headers[HeaderNames.Authorization].ToString();
            if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return authHeader["Bearer ".Length..].Trim();
            }

            var queryToken = Request.Query["token"].ToString();
            return string.IsNullOrEmpty(queryToken) ? null : queryToken;
        }
    }
}
