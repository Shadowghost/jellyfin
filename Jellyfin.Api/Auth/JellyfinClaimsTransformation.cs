using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authentication;

namespace Jellyfin.Api.Auth;

/// <summary>
/// Normalizes the claims of an authenticated principal into Jellyfin's internal claim shape,
/// resolving the user (and role) from the token. Runs after every successful authentication.
/// </summary>
/// <remarks>
/// Plugin-token principals are already fully populated by the plugin authentication handler
/// (which resolves/provisions the user and reconciles permissions), so they short-circuit on the
/// existing <see cref="InternalClaimTypes.UserId"/> claim.
/// </remarks>
public sealed class JellyfinClaimsTransformation : IClaimsTransformation
{
    private readonly IUserManager _userManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinClaimsTransformation"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    public JellyfinClaimsTransformation(IUserManager userManager)
    {
        _userManager = userManager;
    }

    /// <inheritdoc />
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity { IsAuthenticated: true } identity)
        {
            return Task.FromResult(principal);
        }

        // Already populated (legacy handler and the plugin handler set the internal claims).
        if (principal.HasClaim(claim => claim.Type == InternalClaimTypes.UserId))
        {
            return Task.FromResult(principal);
        }

        var scheme = identity.AuthenticationType;
        if (string.Equals(scheme, AuthenticationSchemes.JellyfinJwt, StringComparison.Ordinal))
        {
            return Task.FromResult(PopulateFromSession(principal));
        }

        if (string.Equals(scheme, AuthenticationSchemes.JellyfinTempJwt, StringComparison.Ordinal))
        {
            return Task.FromResult(PopulateFromTemp(principal));
        }

        return Task.FromResult(principal);
    }

    private ClaimsPrincipal PopulateFromSession(ClaimsPrincipal principal)
    {
        var subject = principal.FindFirstValue(JellyfinTokenConstants.SubjectClaim);
        if (!Guid.TryParseExact(subject, "N", out var userId))
        {
            return principal;
        }

        var user = _userManager.GetUserById(userId);
        if (user is null)
        {
            // Unknown user -> leave the principal without internal claims; handlers will reject it.
            return principal;
        }

        var clone = principal.Clone();
        var identity = (ClaimsIdentity)clone.Identity!;
        identity.AddClaim(new Claim(InternalClaimTypes.UserId, userId.ToString("N", CultureInfo.InvariantCulture)));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.Username));
        identity.AddClaim(new Claim(
            ClaimTypes.Role,
            user.HasPermission(PermissionKind.IsAdministrator) ? UserRoles.Administrator : UserRoles.User));
        return clone;
    }

    private ClaimsPrincipal PopulateFromTemp(ClaimsPrincipal principal)
    {
        // Temp tokens act on behalf of a user but are constrained by their scopes (never admin).
        var actingUser = principal.FindFirstValue(JellyfinClaimTypes.ActingUser);
        if (!Guid.TryParseExact(actingUser, "N", out var userId) || _userManager.GetUserById(userId) is null)
        {
            return principal;
        }

        var clone = principal.Clone();
        ((ClaimsIdentity)clone.Identity!).AddClaim(new Claim(InternalClaimTypes.UserId, userId.ToString("N", CultureInfo.InvariantCulture)));
        return clone;
    }
}
