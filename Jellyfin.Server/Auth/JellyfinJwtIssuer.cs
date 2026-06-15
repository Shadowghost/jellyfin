using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using Jellyfin.Api.Constants;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Security;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Jellyfin.Server.Auth;

/// <summary>
/// Issues Jellyfin-signed JWTs using the symmetric signing key from <see cref="IJellyfinSigningKeyProvider"/>.
/// </summary>
public sealed class JellyfinJwtIssuer : IJellyfinJwtIssuer
{
    private readonly IJellyfinSigningKeyProvider _keyProvider;
    private readonly string _issuer;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinJwtIssuer"/> class.
    /// </summary>
    /// <param name="keyProvider">The signing key provider.</param>
    /// <param name="applicationHost">The server application host (for the instance id).</param>
    public JellyfinJwtIssuer(IJellyfinSigningKeyProvider keyProvider, IServerApplicationHost applicationHost)
    {
        _keyProvider = keyProvider;
        _issuer = JellyfinTokenConstants.IssuerPrefix + applicationHost.SystemId;
    }

    /// <inheritdoc />
    public string IssueSession(
        Guid userId,
        string? deviceId,
        string? client,
        string? version,
        string? deviceName,
        TimeSpan lifetime)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString("N", CultureInfo.InvariantCulture)),
            new(JellyfinClaimTypes.Scope, EndpointScopes.Full)
        };

        AddIfPresent(claims, InternalClaimTypes.DeviceId, deviceId);
        AddIfPresent(claims, InternalClaimTypes.Device, deviceName);
        AddIfPresent(claims, InternalClaimTypes.Client, client);
        AddIfPresent(claims, InternalClaimTypes.Version, version);

        return CreateToken(claims, JellyfinTokenConstants.ApiAudience, lifetime, out _);
    }

    /// <inheritdoc />
    public string IssueTemp(
        Guid actingUserId,
        IReadOnlyList<string> scopes,
        string? itemId,
        TimeSpan lifetime,
        string label,
        out string jti)
    {
        var claims = new List<Claim>
        {
            new(JellyfinClaimTypes.ActingUser, actingUserId.ToString("N", CultureInfo.InvariantCulture)),
            new(JellyfinClaimTypes.Label, label)
        };

        foreach (var scope in scopes)
        {
            claims.Add(new Claim(JellyfinClaimTypes.Scope, scope));
        }

        if (!string.IsNullOrEmpty(itemId))
        {
            claims.Add(new Claim(JellyfinClaimTypes.ItemId, itemId));
        }

        return CreateToken(claims, JellyfinTokenConstants.TempAudience, ClampTempLifetime(lifetime), out jti);
    }

    private static void AddIfPresent(List<Claim> claims, string type, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            claims.Add(new Claim(type, value));
        }
    }

    private static TimeSpan ClampTempLifetime(TimeSpan lifetime)
    {
        var seconds = Math.Clamp(
            lifetime.TotalSeconds,
            JellyfinTokenConstants.TempTokenMinLifetimeSeconds,
            JellyfinTokenConstants.TempTokenMaxLifetimeSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    private string CreateToken(List<Claim> claims, string audience, TimeSpan lifetime, out string jti)
    {
        jti = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        claims.Add(new Claim(JwtRegisteredClaimNames.Jti, jti));

        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _issuer,
            Audience = audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(lifetime),
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = new SigningCredentials(_keyProvider.GetCurrentSigningKey(), SecurityAlgorithms.HmacSha256)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
