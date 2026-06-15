using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Auth;
using Jellyfin.Api.Constants;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Auth
{
    public class PluginAuthenticationHandlerTests
    {
        private const string PluginId = "demo";

        [Fact]
        public async Task HandleAuthenticateAsync_UnknownPlugin_Fails()
        {
            var registry = new Mock<IAuthenticationPluginRegistry>();
            registry.Setup(r => r.GetById(PluginId)).Returns((IAuthenticationPlugin?)null);

            var handler = await CreateHandlerAsync(registry.Object, new Mock<IUserManager>().Object, "Bearer pt_demo_token");
            var result = await handler.AuthenticateAsync();

            Assert.False(result.Succeeded);
            Assert.NotNull(result.Failure);
        }

        [Fact]
        public async Task HandleAuthenticateAsync_NoToken_NoResult()
        {
            var registry = new Mock<IAuthenticationPluginRegistry>();
            var handler = await CreateHandlerAsync(registry.Object, new Mock<IUserManager>().Object, null);
            var result = await handler.AuthenticateAsync();

            Assert.True(result.None);
        }

        [Fact]
        public async Task HandleAuthenticateAsync_NonPluginToken_NoResult()
        {
            var registry = new Mock<IAuthenticationPluginRegistry>();
            var handler = await CreateHandlerAsync(registry.Object, new Mock<IUserManager>().Object, "Bearer abc.def.ghi");
            var result = await handler.AuthenticateAsync();

            Assert.True(result.None);
        }

        [Fact]
        public async Task HandleAuthenticateAsync_ThrowingPlugin_FailsAndRecordsFailure()
        {
            var plugin = new Mock<IAuthenticationPlugin>();
            var registry = new Mock<IAuthenticationPluginRegistry>();
            registry.Setup(r => r.GetById(PluginId)).Returns(plugin.Object);
            registry.Setup(r => r.IsCircuitOpen(PluginId)).Returns(false);
            registry.Setup(r => r.ValidateAsync(plugin.Object, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("boom"));

            var handler = await CreateHandlerAsync(registry.Object, new Mock<IUserManager>().Object, "Bearer pt_demo_token");
            var result = await handler.AuthenticateAsync();

            Assert.False(result.Succeeded);
            registry.Verify(r => r.RecordFailure(PluginId), Times.Once);
        }

        [Fact]
        public async Task HandleAuthenticateAsync_CircuitOpen_FailsWithRetryAfterHeader()
        {
            var plugin = new Mock<IAuthenticationPlugin>();
            var registry = new Mock<IAuthenticationPluginRegistry>();
            registry.Setup(r => r.GetById(PluginId)).Returns(plugin.Object);
            registry.Setup(r => r.IsCircuitOpen(PluginId)).Returns(true);

            var (handler, context) = await CreateHandlerWithContextAsync(registry.Object, new Mock<IUserManager>().Object, "Bearer pt_demo_token");
            var result = await handler.AuthenticateAsync();

            Assert.False(result.Succeeded);
            Assert.Equal("30", context.Response.Headers["Retry-After"]);
        }

        [Fact]
        public async Task HandleAuthenticateAsync_ValidToken_SucceedsWithClaims()
        {
            var plugin = new Mock<IAuthenticationPlugin>();
            var identity = new PluginUserIdentity
            {
                ExternalUserId = "ext-1",
                Username = "alice",
                IsAdministrator = true
            };
            var validation = new PluginTokenValidationResult { Valid = true, Identity = identity, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5) };

            var registry = new Mock<IAuthenticationPluginRegistry>();
            registry.Setup(r => r.GetById(PluginId)).Returns(plugin.Object);
            registry.Setup(r => r.IsCircuitOpen(PluginId)).Returns(false);
            registry.Setup(r => r.ValidateAsync(plugin.Object, "token", It.IsAny<CancellationToken>()))
                .ReturnsAsync(validation);

            var user = new User("alice", "pt:demo", "reset");
            var userManager = new Mock<IUserManager>();
            userManager.Setup(u => u.ResolveOrProvisionPluginUserAsync(PluginId, identity, It.IsAny<CancellationToken>()))
                .ReturnsAsync(user);

            var handler = await CreateHandlerAsync(registry.Object, userManager.Object, "Bearer pt_demo_token");
            var result = await handler.AuthenticateAsync();

            Assert.True(result.Succeeded);
            Assert.True(result.Principal!.HasClaim(JellyfinClaimTypes.PluginId, PluginId));
            Assert.True(result.Principal!.HasClaim(JellyfinClaimTypes.Scope, EndpointScopes.Full));
            registry.Verify(r => r.RecordSuccess(PluginId), Times.Once);
        }

        private static async Task<PluginAuthenticationHandler> CreateHandlerAsync(
            IAuthenticationPluginRegistry registry,
            IUserManager userManager,
            string? authorizationHeader)
        {
            var (handler, _) = await CreateHandlerWithContextAsync(registry, userManager, authorizationHeader);
            return handler;
        }

        private static async Task<(PluginAuthenticationHandler Handler, HttpContext Context)> CreateHandlerWithContextAsync(
            IAuthenticationPluginRegistry registry,
            IUserManager userManager,
            string? authorizationHeader)
        {
            var optionsMonitor = new Mock<IOptionsMonitor<AuthenticationSchemeOptions>>();
            optionsMonitor.Setup(o => o.Get(It.IsAny<string>())).Returns(new AuthenticationSchemeOptions());

            var handler = new PluginAuthenticationHandler(
                registry,
                userManager,
                optionsMonitor.Object,
                new NullLoggerFactory(),
                System.Text.Encodings.Web.UrlEncoder.Default);

            var context = new DefaultHttpContext();
            if (authorizationHeader is not null)
            {
                context.Request.Headers[HeaderNames.Authorization] = authorizationHeader;
            }

            var scheme = new AuthenticationScheme(AuthenticationSchemes.PluginToken, null, typeof(PluginAuthenticationHandler));
            await handler.InitializeAsync(scheme, context);
            return (handler, context);
        }
    }
}
