using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using Jellyfin.Server.Auth;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace Jellyfin.Server.Tests.Auth
{
    public sealed class JellyfinJwtIssuerTests : IDisposable
    {
        private const string SystemId = "11111111111111111111111111111111";

        private readonly string _configDir;
        private readonly JellyfinSigningKeyProvider _keyProvider;
        private readonly JellyfinJwtIssuer _issuer;
        private readonly string _expectedIssuer = JellyfinTokenConstants.IssuerPrefix + SystemId;

        public JellyfinJwtIssuerTests()
        {
            _configDir = Path.Combine(Path.GetTempPath(), "jf-jwttest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_configDir);

            var paths = new Mock<IApplicationPaths>();
            paths.Setup(p => p.ConfigurationDirectoryPath).Returns(_configDir);
            _keyProvider = new JellyfinSigningKeyProvider(paths.Object, NullLogger<JellyfinSigningKeyProvider>.Instance);

            var appHost = new Mock<IServerApplicationHost>();
            appHost.Setup(h => h.SystemId).Returns(SystemId);
            _issuer = new JellyfinJwtIssuer(_keyProvider, appHost.Object);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_configDir, true);
            }
            catch (IOException)
            {
                // best effort
            }
        }

        private async Task<TokenValidationResult> Validate(string token, string audience)
        {
            return await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
            {
                ValidIssuer = _expectedIssuer,
                ValidAudience = audience,
                IssuerSigningKeys = _keyProvider.GetValidationKeys(),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2)
            });
        }

        [Fact]
        public async Task IssueSession_ProducesValidToken()
        {
            var userId = Guid.NewGuid();
            var token = _issuer.IssueSession(userId, "dev1", "client1", "1.0", "Device 1", TimeSpan.FromHours(1));

            var result = await Validate(token, JellyfinTokenConstants.ApiAudience);

            Assert.True(result.IsValid);
            Assert.Equal(userId.ToString("N", CultureInfo.InvariantCulture), result.ClaimsIdentity.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
            Assert.Equal(EndpointScopes.Full, result.ClaimsIdentity.FindFirst(JellyfinClaimTypes.Scope)?.Value);
            Assert.Equal("dev1", result.ClaimsIdentity.FindFirst(InternalClaimTypes.DeviceId)?.Value);
        }

        [Fact]
        public async Task IssueSession_RejectedUnderWrongAudience()
        {
            var token = _issuer.IssueSession(Guid.NewGuid(), null, null, null, null, TimeSpan.FromHours(1));

            var result = await Validate(token, JellyfinTokenConstants.TempAudience);

            Assert.False(result.IsValid);
        }

        [Fact]
        public async Task IssueTemp_ProducesValidScopedToken()
        {
            var actingUser = Guid.NewGuid();
            var token = _issuer.IssueTemp(actingUser, new[] { EndpointScopes.MediaStream }, "item42", TimeSpan.FromMinutes(5), "AppleTV", out var jti);

            Assert.False(string.IsNullOrEmpty(jti));

            var result = await Validate(token, JellyfinTokenConstants.TempAudience);

            Assert.True(result.IsValid);
            Assert.Equal(actingUser.ToString("N", CultureInfo.InvariantCulture), result.ClaimsIdentity.FindFirst(JellyfinClaimTypes.ActingUser)?.Value);
            Assert.Equal(EndpointScopes.MediaStream, result.ClaimsIdentity.FindFirst(JellyfinClaimTypes.Scope)?.Value);
            Assert.Equal("item42", result.ClaimsIdentity.FindFirst(JellyfinClaimTypes.ItemId)?.Value);
        }

        [Fact]
        public async Task IssueTemp_ClampsLifetimeToCeiling()
        {
            var token = _issuer.IssueTemp(Guid.NewGuid(), new[] { EndpointScopes.MediaStream }, null, TimeSpan.FromHours(5), "x", out _);

            var result = await Validate(token, JellyfinTokenConstants.TempAudience);
            var jwt = (JsonWebToken)result.SecurityToken;

            Assert.True(jwt.ValidTo - jwt.ValidFrom <= TimeSpan.FromSeconds(JellyfinTokenConstants.TempTokenMaxLifetimeSeconds));
        }

        [Fact]
        public async Task IssueTemp_RaisesLifetimeToFloor()
        {
            var token = _issuer.IssueTemp(Guid.NewGuid(), new[] { EndpointScopes.MediaStream }, null, TimeSpan.FromSeconds(5), "x", out _);

            var result = await Validate(token, JellyfinTokenConstants.TempAudience);
            var jwt = (JsonWebToken)result.SecurityToken;

            Assert.True(jwt.ValidTo - jwt.ValidFrom >= TimeSpan.FromSeconds(JellyfinTokenConstants.TempTokenMinLifetimeSeconds));
        }
    }
}
