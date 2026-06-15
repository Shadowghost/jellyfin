using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Jellyfin.Api.Constants;

namespace Jellyfin.Api.Extensions;

/// <summary>
/// Extensions for <see cref="ClaimsPrincipal"/>.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Get user id from claims.
    /// </summary>
    /// <param name="user">Current claims principal.</param>
    /// <returns>User id.</returns>
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var value = GetClaimValue(user, InternalClaimTypes.UserId);
        return string.IsNullOrEmpty(value)
            ? default
            : Guid.Parse(value);
    }

    /// <summary>
    /// Get device id from claims.
    /// </summary>
    /// <param name="user">Current claims principal.</param>
    /// <returns>Device id.</returns>
    public static string? GetDeviceId(this ClaimsPrincipal user)
        => GetClaimValue(user, InternalClaimTypes.DeviceId);

    /// <summary>
    /// Get device from claims.
    /// </summary>
    /// <param name="user">Current claims principal.</param>
    /// <returns>Device.</returns>
    public static string? GetDevice(this ClaimsPrincipal user)
        => GetClaimValue(user, InternalClaimTypes.Device);

    /// <summary>
    /// Get client from claims.
    /// </summary>
    /// <param name="user">Current claims principal.</param>
    /// <returns>Client.</returns>
    public static string? GetClient(this ClaimsPrincipal user)
        => GetClaimValue(user, InternalClaimTypes.Client);

    /// <summary>
    /// Get version from claims.
    /// </summary>
    /// <param name="user">Current claims principal.</param>
    /// <returns>Version.</returns>
    public static string? GetVersion(this ClaimsPrincipal user)
        => GetClaimValue(user, InternalClaimTypes.Version);

    /// <summary>
    /// Get token from claims.
    /// </summary>
    /// <param name="user">Current claims principal.</param>
    /// <returns>Token.</returns>
    public static string? GetToken(this ClaimsPrincipal user)
        => GetClaimValue(user, InternalClaimTypes.Token);

    /// <summary>
    /// Gets a flag specifying whether the request is using an api key.
    /// </summary>
    /// <param name="user">Current claims principal.</param>
    /// <returns>The flag specifying whether the request is using an api key.</returns>
    public static bool GetIsApiKey(this ClaimsPrincipal user)
    {
        var claimValue = GetClaimValue(user, InternalClaimTypes.IsApiKey);
        return bool.TryParse(claimValue, out var parsedClaimValue)
               && parsedClaimValue;
    }

    /// <summary>
    /// Gets the endpoint scopes granted to the principal.
    /// </summary>
    /// <param name="user">Current claims principal.</param>
    /// <returns>The granted scopes.</returns>
    public static IReadOnlyList<string> GetScopes(this ClaimsPrincipal user)
        => user.FindAll(JellyfinClaimTypes.Scope).Select(claim => claim.Value).ToList();

    /// <summary>
    /// Gets a value indicating whether the principal holds the given scope (or the <c>full</c> scope).
    /// </summary>
    /// <param name="user">Current claims principal.</param>
    /// <param name="scope">The required scope.</param>
    /// <returns>Whether the principal satisfies the scope.</returns>
    public static bool HasScope(this ClaimsPrincipal user, string scope)
        => user.HasClaim(JellyfinClaimTypes.Scope, scope)
           || user.HasClaim(JellyfinClaimTypes.Scope, EndpointScopes.Full);

    /// <summary>
    /// Gets the item id a token is bound to, if any.
    /// </summary>
    /// <param name="user">Current claims principal.</param>
    /// <returns>The bound item id, or <c>null</c>.</returns>
    public static string? GetBoundItemId(this ClaimsPrincipal user)
        => GetClaimValue(user, JellyfinClaimTypes.ItemId);

    private static string? GetClaimValue(in ClaimsPrincipal user, string name)
        => user.Claims.FirstOrDefault(claim => claim.Type.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
}
