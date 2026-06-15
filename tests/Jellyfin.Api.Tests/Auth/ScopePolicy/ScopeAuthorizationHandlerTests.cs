using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Jellyfin.Api.Auth.ScopePolicy;
using Jellyfin.Api.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Auth.ScopePolicy
{
    public class ScopeAuthorizationHandlerTests
    {
        private readonly ScopeAuthorizationHandler _sut;

        public ScopeAuthorizationHandlerTests()
        {
            _sut = new ScopeAuthorizationHandler(new Mock<IHttpContextAccessor>().Object);
        }

        private static ClaimsPrincipal Principal(params Claim[] claims)
            => new(new ClaimsIdentity(claims, "test"));

        private async Task<bool> Evaluate(ClaimsPrincipal principal, string requiredScope, HttpContext? resource = null)
        {
            var requirement = new ScopeRequirement(requiredScope);
            var context = new AuthorizationHandlerContext(new[] { requirement }, principal, resource);
            await _sut.HandleAsync(context);
            return context.HasSucceeded;
        }

        [Fact]
        public async Task UnscopedPrincipal_IsUnrestricted()
        {
            // No scope claims -> legacy/full token -> unrestricted.
            Assert.True(await Evaluate(Principal(), EndpointScopes.MediaStream));
        }

        [Fact]
        public async Task MatchingScope_Succeeds()
        {
            var principal = Principal(new Claim(JellyfinClaimTypes.Scope, EndpointScopes.MediaStream));
            Assert.True(await Evaluate(principal, EndpointScopes.MediaStream));
        }

        [Fact]
        public async Task FullScope_SatisfiesAnyScope()
        {
            var principal = Principal(new Claim(JellyfinClaimTypes.Scope, EndpointScopes.Full));
            Assert.True(await Evaluate(principal, EndpointScopes.MediaImage));
        }

        [Fact]
        public async Task MissingScope_Fails()
        {
            var principal = Principal(new Claim(JellyfinClaimTypes.Scope, EndpointScopes.MediaImage));
            Assert.False(await Evaluate(principal, EndpointScopes.MediaStream));
        }

        [Fact]
        public async Task BoundItem_MatchingRoute_Succeeds()
        {
            var principal = Principal(
                new Claim(JellyfinClaimTypes.Scope, EndpointScopes.MediaStream),
                new Claim(JellyfinClaimTypes.ItemId, "item-1"));

            var httpContext = new DefaultHttpContext();
            httpContext.Request.RouteValues["itemId"] = "item-1";

            Assert.True(await Evaluate(principal, EndpointScopes.MediaStream, httpContext));
        }

        [Fact]
        public async Task BoundItem_DifferentRoute_Fails()
        {
            var principal = Principal(
                new Claim(JellyfinClaimTypes.Scope, EndpointScopes.MediaStream),
                new Claim(JellyfinClaimTypes.ItemId, "item-1"));

            var httpContext = new DefaultHttpContext();
            httpContext.Request.RouteValues["itemId"] = "item-2";

            Assert.False(await Evaluate(principal, EndpointScopes.MediaStream, httpContext));
        }
    }
}
