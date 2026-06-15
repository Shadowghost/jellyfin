using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Auth;
using Jellyfin.Api.Constants;
using Jellyfin.Api.Extensions;
using Jellyfin.Api.Models.TempTokenDtos;
using Jellyfin.Api.Models.UserDtos;
using Jellyfin.Extensions;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Jellyfin.Api.Controllers;

/// <summary>
/// Issuance and management of short-lived, scoped temp tokens.
/// </summary>
[ApiController]
[Authorize]
[Route("Auth/TempTokens")]
public class AuthController : BaseJellyfinApiController
{
    /// <summary>
    /// Rate limiting policy name for temp token issuance.
    /// </summary>
    public const string IssuanceRateLimitPolicy = "TempTokenIssuance";

    private readonly IJellyfinJwtIssuer _jwtIssuer;
    private readonly ITempTokenStore _tempTokenStore;
    private readonly IAuthenticationPluginRegistry _pluginRegistry;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthController"/> class.
    /// </summary>
    /// <param name="jwtIssuer">The JWT issuer.</param>
    /// <param name="tempTokenStore">The temp token store.</param>
    /// <param name="pluginRegistry">The authentication plugin registry.</param>
    public AuthController(IJellyfinJwtIssuer jwtIssuer, ITempTokenStore tempTokenStore, IAuthenticationPluginRegistry pluginRegistry)
    {
        _jwtIssuer = jwtIssuer;
        _tempTokenStore = tempTokenStore;
        _pluginRegistry = pluginRegistry;
    }

    /// <summary>
    /// Lists the authentication providers available for the login picker.
    /// </summary>
    /// <response code="200">Providers retrieved.</response>
    /// <returns>The available authentication providers.</returns>
    [AllowAnonymous]
    [HttpGet("/Auth/Providers")]
    [Tags("Authentication")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<AuthenticationProviderInfo>> GetProviders()
    {
        var providers = new List<AuthenticationProviderInfo>
        {
            new AuthenticationProviderInfo
            {
                Id = "builtin",
                DisplayName = "Jellyfin",
                Capabilities = "UsernamePassword"
            }
        };

        foreach (var plugin in _pluginRegistry.GetEnabledPlugins())
        {
            providers.Add(new AuthenticationProviderInfo
            {
                Id = plugin.Id,
                DisplayName = plugin.DisplayName,
                Capabilities = plugin.Capabilities.ToString()
            });
        }

        return providers;
    }

    /// <summary>
    /// Mints a short-lived, scoped temp token on behalf of the calling user.
    /// </summary>
    /// <param name="request">The token request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Token issued.</response>
    /// <response code="400">The request was invalid.</response>
    /// <response code="403">The caller may not mint temp tokens or requested a disallowed scope.</response>
    /// <returns>The issued token.</returns>
    [HttpPost]
    [EnableRateLimiting(IssuanceRateLimitPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TempTokenResultDto>> CreateTempToken([FromBody, Required] CreateTempTokenDto request, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        if (userId.IsEmpty())
        {
            return Forbid();
        }

        // A scoped (temp) token may not mint further temp tokens — only full sessions / legacy tokens can.
        var callerScopes = User.GetScopes();
        if (callerScopes.Count != 0 && !callerScopes.Contains(EndpointScopes.Full))
        {
            return Forbid();
        }

        if (request.Scopes.Count == 0)
        {
            return BadRequest("At least one scope is required.");
        }

        // Only grantable endpoint scopes are allowed (never 'full' or admin/unknown scopes).
        if (request.Scopes.Any(scope => !EndpointScopes.All.Contains(scope, StringComparer.Ordinal)))
        {
            return BadRequest("One or more requested scopes are not grantable.");
        }

        var lifetime = TimeSpan.FromSeconds(Math.Clamp(
            request.TtlSeconds,
            JellyfinTokenConstants.TempTokenMinLifetimeSeconds,
            JellyfinTokenConstants.TempTokenMaxLifetimeSeconds));

        var token = _jwtIssuer.IssueTemp(userId, request.Scopes, request.ItemId, lifetime, request.Label, out var jti);
        var issuedAt = DateTime.UtcNow;
        var expiresAt = issuedAt.Add(lifetime);

        await _tempTokenStore.RecordAsync(jti, userId, issuedAt, expiresAt, request.Scopes, request.ItemId, request.Label, cancellationToken).ConfigureAwait(false);

        AuthMetrics.TempTokenIssued();
        return new TempTokenResultDto { Token = token, ExpiresAt = expiresAt };
    }

    /// <summary>
    /// Lists the caller's outstanding temp tokens.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Tokens retrieved.</response>
    /// <returns>The outstanding tokens.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TempTokenInfo>>> GetTempTokens(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        if (userId.IsEmpty())
        {
            return Forbid();
        }

        var tokens = await _tempTokenStore.GetForUserAsync(userId, cancellationToken).ConfigureAwait(false);
        return Ok(tokens);
    }

    /// <summary>
    /// Revokes one of the caller's temp tokens (or any token, for administrators).
    /// </summary>
    /// <param name="tokenId">The token id to revoke.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="204">Token revoked.</response>
    /// <response code="404">Token not found or not owned by the caller.</response>
    /// <returns>A <see cref="NoContentResult"/>.</returns>
    [HttpDelete("{tokenId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RevokeTempToken([FromRoute, Required] int tokenId, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        if (userId.IsEmpty())
        {
            return Forbid();
        }

        var revoked = await _tempTokenStore.RevokeAsync(tokenId, userId, User.IsInRole(UserRoles.Administrator), cancellationToken).ConfigureAwait(false);
        if (revoked)
        {
            AuthMetrics.TempTokenRevoked();
            return NoContent();
        }

        return NotFound();
    }
}
