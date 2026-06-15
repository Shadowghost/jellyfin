using System;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using MediaBrowser.Controller;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Jellyfin.Server.Auth;

/// <summary>
/// Configures the <see cref="JwtBearerOptions"/> for the Jellyfin-issued JWT schemes
/// (<see cref="AuthenticationSchemes.JellyfinJwt"/> and <see cref="AuthenticationSchemes.JellyfinTempJwt"/>).
/// </summary>
public sealed class ConfigureJellyfinJwtBearerOptions : IConfigureNamedOptions<JwtBearerOptions>
{
    private readonly IJellyfinSigningKeyProvider _keyProvider;
    private readonly string _issuer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigureJellyfinJwtBearerOptions"/> class.
    /// </summary>
    /// <param name="keyProvider">The signing key provider.</param>
    /// <param name="applicationHost">The server application host (for the instance id).</param>
    public ConfigureJellyfinJwtBearerOptions(IJellyfinSigningKeyProvider keyProvider, IServerApplicationHost applicationHost)
    {
        _keyProvider = keyProvider;
        _issuer = JellyfinTokenConstants.IssuerPrefix + applicationHost.SystemId;
    }

    /// <inheritdoc />
    public void Configure(string? name, JwtBearerOptions options)
    {
        var isTemp = string.Equals(name, AuthenticationSchemes.JellyfinTempJwt, StringComparison.Ordinal);
        if (!isTemp && !string.Equals(name, AuthenticationSchemes.JellyfinJwt, StringComparison.Ordinal))
        {
            return;
        }

        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _issuer,
            ValidateAudience = true,
            ValidAudience = isTemp ? JellyfinTokenConstants.TempAudience : JellyfinTokenConstants.ApiAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
            // Resolve keys live so signing-key rotation is picked up without reconfiguring the scheme.
            IssuerSigningKeyResolver = (_, _, _, _) => _keyProvider.GetValidationKeys(),
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };

        var events = new JwtBearerEvents();

        if (isTemp)
        {
            // Temp tokens may be supplied via the "token" query parameter and must carry a scope claim.
            events.OnMessageReceived = context =>
            {
                if (string.IsNullOrEmpty(context.Token))
                {
                    var queryToken = context.Request.Query["token"];
                    if (!string.IsNullOrEmpty(queryToken))
                    {
                        context.Token = queryToken;
                    }
                }

                return Task.CompletedTask;
            };

            events.OnTokenValidated = context =>
            {
                if (context.Principal?.Claims.Any(c => c.Type == JellyfinClaimTypes.Scope) != true)
                {
                    context.Fail("Temp token is missing a scope claim.");
                }

                return Task.CompletedTask;
            };

            options.TokenValidationParameters.LifetimeValidator = ValidateTempLifetime;
        }

        options.Events = events;
    }

    /// <inheritdoc />
    public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);

    private static bool ValidateTempLifetime(DateTime? notBefore, DateTime? expires, SecurityToken securityToken, TokenValidationParameters validationParameters)
    {
        if (expires is null || expires.Value < DateTime.UtcNow)
        {
            return false;
        }

        var start = notBefore ?? DateTime.UtcNow;
        // Reject temp tokens whose total lifetime exceeds the ceiling (plus clock skew allowance).
        return expires.Value - start <= TimeSpan.FromSeconds(JellyfinTokenConstants.TempTokenMaxLifetimeSeconds + 120);
    }
}
