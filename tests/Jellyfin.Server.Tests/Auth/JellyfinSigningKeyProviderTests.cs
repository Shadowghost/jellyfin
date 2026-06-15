using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Server.Auth;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace Jellyfin.Server.Tests.Auth
{
    public sealed class JellyfinSigningKeyProviderTests : IDisposable
    {
        private readonly string _configDir;

        public JellyfinSigningKeyProviderTests()
        {
            _configDir = Path.Combine(Path.GetTempPath(), "jf-keytest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_configDir);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_configDir, true);
            }
            catch (IOException)
            {
                // best effort cleanup
            }
        }

        private JellyfinSigningKeyProvider Create()
        {
            var paths = new Mock<IApplicationPaths>();
            paths.Setup(p => p.ConfigurationDirectoryPath).Returns(_configDir);
            return new JellyfinSigningKeyProvider(paths.Object, NullLogger<JellyfinSigningKeyProvider>.Instance);
        }

        private static byte[] KeyBytes(SecurityKey key) => ((SymmetricSecurityKey)key).Key;

        [Fact]
        public void Generates_TwoDistinctKeys()
        {
            var sut = Create();
            var keys = sut.GetValidationKeys();

            Assert.Equal(2, keys.Count);
            Assert.NotEqual(Convert.ToBase64String(KeyBytes(keys[0])), Convert.ToBase64String(KeyBytes(keys[1])));
            Assert.Equal(64, KeyBytes(sut.GetCurrentSigningKey()).Length);
        }

        [Fact]
        public void Persists_AcrossInstances()
        {
            var first = Create();
            var firstCurrent = Convert.ToBase64String(KeyBytes(first.GetCurrentSigningKey()));

            // A fresh provider over the same config dir loads the persisted keys.
            var second = Create();
            var secondCurrent = Convert.ToBase64String(KeyBytes(second.GetCurrentSigningKey()));

            Assert.Equal(firstCurrent, secondCurrent);
        }

        [Fact]
        public async Task Rotate_MovesCurrentToPrevious()
        {
            var sut = Create();
            var oldCurrent = Convert.ToBase64String(KeyBytes(sut.GetCurrentSigningKey()));

            await sut.RotateAsync(TestContext.Current.CancellationToken);

            var newCurrent = Convert.ToBase64String(KeyBytes(sut.GetCurrentSigningKey()));
            var validationKeys = sut.GetValidationKeys().Select(k => Convert.ToBase64String(KeyBytes(k))).ToList();

            Assert.NotEqual(oldCurrent, newCurrent);
            // The old current key remains valid for validation (now as the previous key).
            Assert.Contains(oldCurrent, validationKeys);
            Assert.Contains(newCurrent, validationKeys);
        }

        [Fact]
        public void KeyFile_HasRestrictivePermissions()
        {
            if (OperatingSystem.IsWindows())
            {
                // Unix file modes are not applicable on Windows.
                return;
            }

            Create();
            var keyFile = Path.Combine(_configDir, "auth.xml");

            Assert.True(File.Exists(keyFile));
            var mode = File.GetUnixFileMode(keyFile);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
        }
    }
}
