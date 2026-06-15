using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using Jellyfin.Api.Controllers;
using Jellyfin.Api.Models.TempTokenDtos;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Controllers
{
    public class AuthControllerTests
    {
        private readonly Mock<IJellyfinJwtIssuer> _issuer = new();
        private readonly Mock<ITempTokenStore> _store = new();
        private readonly Mock<IAuthenticationPluginRegistry> _pluginRegistry = new();

        private AuthController CreateController(ClaimsPrincipal user)
        {
            return new AuthController(_issuer.Object, _store.Object, _pluginRegistry.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = user }
                }
            };
        }

        private static ClaimsPrincipal FullSession(Guid userId, bool admin = false)
            => new(new ClaimsIdentity(
                new[]
                {
                    new Claim(InternalClaimTypes.UserId, userId.ToString("N", CultureInfo.InvariantCulture)),
                    new Claim(ClaimTypes.Role, admin ? UserRoles.Administrator : UserRoles.User),
                    new Claim(JellyfinClaimTypes.Scope, EndpointScopes.Full)
                },
                "test"));

        private static ClaimsPrincipal TempCaller(Guid userId)
            => new(new ClaimsIdentity(
                new[]
                {
                    new Claim(InternalClaimTypes.UserId, userId.ToString("N", CultureInfo.InvariantCulture)),
                    new Claim(JellyfinClaimTypes.Scope, EndpointScopes.MediaStream)
                },
                "test"));

        [Fact]
        public async Task CreateTempToken_ValidRequest_IssuesAndRecords()
        {
            var jti = "jti-1";
            _issuer.Setup(i => i.IssueTemp(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<TimeSpan>(), It.IsAny<string>(), out jti))
                .Returns("the-token");

            var controller = CreateController(FullSession(Guid.NewGuid()));
            var request = new CreateTempTokenDto { Scopes = new[] { EndpointScopes.MediaStream }, TtlSeconds = 300, Label = "AppleTV" };

            var result = await controller.CreateTempToken(request, CancellationToken.None);

            Assert.Equal("the-token", result.Value?.Token);
            _store.Verify(s => s.RecordAsync("jti-1", It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<IReadOnlyList<string>>(), null, "AppleTV", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CreateTempToken_EmptyScopes_BadRequest()
        {
            var controller = CreateController(FullSession(Guid.NewGuid()));
            var request = new CreateTempTokenDto { Scopes = Array.Empty<string>(), TtlSeconds = 300, Label = "x" };

            var result = await controller.CreateTempToken(request, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result.Result);
        }

        [Fact]
        public async Task CreateTempToken_FullScopeNotGrantable_BadRequest()
        {
            var controller = CreateController(FullSession(Guid.NewGuid()));
            var request = new CreateTempTokenDto { Scopes = new[] { EndpointScopes.Full }, TtlSeconds = 300, Label = "x" };

            var result = await controller.CreateTempToken(request, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result.Result);
        }

        [Fact]
        public async Task CreateTempToken_FromTempToken_Forbidden()
        {
            var controller = CreateController(TempCaller(Guid.NewGuid()));
            var request = new CreateTempTokenDto { Scopes = new[] { EndpointScopes.MediaStream }, TtlSeconds = 300, Label = "x" };

            var result = await controller.CreateTempToken(request, CancellationToken.None);

            Assert.IsType<ForbidResult>(result.Result);
        }

        [Fact]
        public async Task RevokeTempToken_Found_NoContent()
        {
            _store.Setup(s => s.RevokeAsync(7, It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var controller = CreateController(FullSession(Guid.NewGuid()));

            var result = await controller.RevokeTempToken(7, CancellationToken.None);

            Assert.IsType<NoContentResult>(result);
        }

        [Fact]
        public async Task RevokeTempToken_NotFound_Returns404()
        {
            _store.Setup(s => s.RevokeAsync(It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
            var controller = CreateController(FullSession(Guid.NewGuid()));

            var result = await controller.RevokeTempToken(99, CancellationToken.None);

            Assert.IsType<NotFoundResult>(result);
        }
    }
}
