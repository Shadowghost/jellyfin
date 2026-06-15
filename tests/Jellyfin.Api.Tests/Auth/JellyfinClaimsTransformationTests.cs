using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading.Tasks;
using Jellyfin.Api.Auth;
using Jellyfin.Api.Constants;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Server.Implementations.Users;
using MediaBrowser.Controller.Library;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Auth
{
    public class JellyfinClaimsTransformationTests
    {
        private readonly Mock<IUserManager> _userManagerMock = new();
        private readonly JellyfinClaimsTransformation _sut;

        public JellyfinClaimsTransformationTests()
        {
            _sut = new JellyfinClaimsTransformation(_userManagerMock.Object);
        }

        private User SetupUser(bool isAdmin)
        {
            var user = new User(
                "jellyfin",
                typeof(PasswordValidator).FullName!,
                typeof(DefaultPasswordResetProvider).FullName!);
            user.AddDefaultPermissions();
            user.SetPermission(PermissionKind.IsAdministrator, isAdmin);
            _userManagerMock.Setup(u => u.GetUserById(It.IsAny<Guid>())).Returns(user);
            return user;
        }

        private static ClaimsPrincipal Principal(string authType, params Claim[] claims)
            => new(new ClaimsIdentity(claims, authType));

        [Fact]
        public async Task Unauthenticated_ReturnedUnchanged()
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity());
            var result = await _sut.TransformAsync(principal);
            Assert.Same(principal, result);
        }

        [Fact]
        public async Task LegacyPrincipalWithUserId_ReturnedUnchanged()
        {
            var principal = Principal(
                AuthenticationSchemes.LegacyMediaBrowserToken,
                new Claim(InternalClaimTypes.UserId, Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)));

            var result = await _sut.TransformAsync(principal);

            Assert.Same(principal, result);
        }

        [Fact]
        public async Task SessionJwt_AdminUser_GetsUserIdAndAdminRole()
        {
            var userId = Guid.NewGuid();
            SetupUser(isAdmin: true);
            var principal = Principal(
                AuthenticationSchemes.JellyfinJwt,
                new Claim(JellyfinTokenConstants.SubjectClaim, userId.ToString("N", CultureInfo.InvariantCulture)));

            var result = await _sut.TransformAsync(principal);

            Assert.Equal(userId.ToString("N", CultureInfo.InvariantCulture), result.FindFirstValue(InternalClaimTypes.UserId));
            Assert.True(result.IsInRole(UserRoles.Administrator));
        }

        [Fact]
        public async Task SessionJwt_NonAdminUser_GetsUserRole()
        {
            var userId = Guid.NewGuid();
            SetupUser(isAdmin: false);
            var principal = Principal(
                AuthenticationSchemes.JellyfinJwt,
                new Claim(JellyfinTokenConstants.SubjectClaim, userId.ToString("N", CultureInfo.InvariantCulture)));

            var result = await _sut.TransformAsync(principal);

            Assert.True(result.IsInRole(UserRoles.User));
            Assert.False(result.IsInRole(UserRoles.Administrator));
        }

        [Fact]
        public async Task SessionJwt_UnknownUser_NoUserIdClaim()
        {
            _userManagerMock.Setup(u => u.GetUserById(It.IsAny<Guid>())).Returns((User?)null);
            var principal = Principal(
                AuthenticationSchemes.JellyfinJwt,
                new Claim(JellyfinTokenConstants.SubjectClaim, Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)));

            var result = await _sut.TransformAsync(principal);

            Assert.Null(result.FindFirstValue(InternalClaimTypes.UserId));
        }

        [Fact]
        public async Task TempJwt_GetsUserIdFromActingUser_NoAdminRole()
        {
            var userId = Guid.NewGuid();
            SetupUser(isAdmin: true); // even if the acting user is an admin, temp tokens never get the admin role
            var principal = Principal(
                AuthenticationSchemes.JellyfinTempJwt,
                new Claim(JellyfinClaimTypes.ActingUser, userId.ToString("N", CultureInfo.InvariantCulture)),
                new Claim(JellyfinClaimTypes.Scope, EndpointScopes.MediaStream));

            var result = await _sut.TransformAsync(principal);

            Assert.Equal(userId.ToString("N", CultureInfo.InvariantCulture), result.FindFirstValue(InternalClaimTypes.UserId));
            Assert.False(result.IsInRole(UserRoles.Administrator));
        }
    }
}
