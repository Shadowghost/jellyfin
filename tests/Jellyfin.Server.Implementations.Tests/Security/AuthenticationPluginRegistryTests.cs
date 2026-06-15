using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Server.Implementations.Security;
using MediaBrowser.Controller.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Security
{
    public class AuthenticationPluginRegistryTests
    {
        private static AuthenticationPluginRegistry CreateRegistry(params IAuthenticationPlugin[] plugins)
            => new AuthenticationPluginRegistry(
                plugins,
                new MemoryCache(new MemoryCacheOptions()),
                NullLogger<AuthenticationPluginRegistry>.Instance);

        [Fact]
        public void GetById_DisabledPlugin_ReturnsNull()
        {
            var plugin = new StubPlugin("demo") { Enabled = false };
            var registry = CreateRegistry(plugin);

            Assert.Null(registry.GetById("demo"));
        }

        [Fact]
        public void GetById_IsCaseInsensitive()
        {
            var plugin = new StubPlugin("Demo");
            var registry = CreateRegistry(plugin);

            Assert.NotNull(registry.GetById("demo"));
        }

        [Fact]
        public async Task ValidateAsync_CachesResult_PluginCalledOnce()
        {
            var plugin = new StubPlugin("demo")
            {
                ValidationResult = Valid(TimeSpan.FromMinutes(5))
            };
            var registry = CreateRegistry(plugin);

            await registry.ValidateAsync(plugin, "token-1", CancellationToken.None);
            await registry.ValidateAsync(plugin, "token-1", CancellationToken.None);

            Assert.Equal(1, plugin.ValidateCallCount);
        }

        [Fact]
        public async Task ValidateAsync_DifferentTokensDoNotCollide()
        {
            var plugin = new StubPlugin("demo")
            {
                ValidationResult = Valid(TimeSpan.FromMinutes(5))
            };
            var registry = CreateRegistry(plugin);

            await registry.ValidateAsync(plugin, "token-a", CancellationToken.None);
            await registry.ValidateAsync(plugin, "token-b", CancellationToken.None);

            Assert.Equal(2, plugin.ValidateCallCount);
        }

        [Fact]
        public async Task InvalidateTokenAsync_EvictsCacheEntry()
        {
            var plugin = new StubPlugin("demo")
            {
                ValidationResult = Valid(TimeSpan.FromMinutes(5))
            };
            var registry = CreateRegistry(plugin);

            await registry.ValidateAsync(plugin, "token-1", CancellationToken.None);
            await ((IAuthenticationPluginContext)registry).InvalidateTokenAsync("token-1", CancellationToken.None);
            await registry.ValidateAsync(plugin, "token-1", CancellationToken.None);

            Assert.Equal(2, plugin.ValidateCallCount);
        }

        [Fact]
        public async Task InvalidateUserAsync_EvictsAllUserTokens()
        {
            var plugin = new StubPlugin("demo")
            {
                ValidationResult = Valid(TimeSpan.FromMinutes(5))
            };
            var registry = CreateRegistry(plugin);

            await registry.ValidateAsync(plugin, "token-1", CancellationToken.None);
            await registry.ValidateAsync(plugin, "token-2", CancellationToken.None);
            Assert.Equal(2, plugin.ValidateCallCount);

            await ((IAuthenticationPluginContext)registry).InvalidateUserAsync("demo", "ext-1", CancellationToken.None);

            await registry.ValidateAsync(plugin, "token-1", CancellationToken.None);
            await registry.ValidateAsync(plugin, "token-2", CancellationToken.None);
            Assert.Equal(4, plugin.ValidateCallCount);
        }

        [Fact]
        public async Task ValidateAsync_ExpiredResult_NotCached()
        {
            var plugin = new StubPlugin("demo")
            {
                ValidationResult = Valid(TimeSpan.FromSeconds(-1))
            };
            var registry = CreateRegistry(plugin);

            await registry.ValidateAsync(plugin, "token-1", CancellationToken.None);
            await registry.ValidateAsync(plugin, "token-1", CancellationToken.None);

            Assert.Equal(2, plugin.ValidateCallCount);
        }

        [Fact]
        public void CircuitBreaker_OpensAfterMajorityFailuresOverThreshold()
        {
            var registry = CreateRegistry();

            // 12 failures, 8 successes over 20 samples => 60% failures > 50% threshold.
            for (var i = 0; i < 12; i++)
            {
                registry.RecordFailure("demo");
            }

            for (var i = 0; i < 8; i++)
            {
                registry.RecordSuccess("demo");
            }

            Assert.True(registry.IsCircuitOpen("demo"));
        }

        [Fact]
        public void CircuitBreaker_DoesNotOpenBelowMinimumSamples()
        {
            var registry = CreateRegistry();

            // 10 failures only — below the 20-sample minimum.
            for (var i = 0; i < 10; i++)
            {
                registry.RecordFailure("demo");
            }

            Assert.False(registry.IsCircuitOpen("demo"));
        }

        [Fact]
        public void CircuitBreaker_DoesNotOpenBelowFailureRatio()
        {
            var registry = CreateRegistry();

            // 10 failures, 15 successes over 25 samples => 40% < 50% threshold.
            for (var i = 0; i < 10; i++)
            {
                registry.RecordFailure("demo");
            }

            for (var i = 0; i < 15; i++)
            {
                registry.RecordSuccess("demo");
            }

            Assert.False(registry.IsCircuitOpen("demo"));
        }

        private static PluginTokenValidationResult Valid(TimeSpan ttl)
            => new PluginTokenValidationResult
            {
                Valid = true,
                ExpiresAt = DateTimeOffset.UtcNow.Add(ttl),
                Identity = new PluginUserIdentity { ExternalUserId = "ext-1", Username = "alice" }
            };

        private sealed class StubPlugin : IAuthenticationPlugin
        {
            public StubPlugin(string id)
            {
                Id = id;
            }

            public string Id { get; }

            public string DisplayName => Id;

            public bool Enabled { get; init; } = true;

            public PluginAuthenticationCapabilities Capabilities => PluginAuthenticationCapabilities.UsernamePassword;

            public PluginTokenValidationResult ValidationResult { get; init; } = new PluginTokenValidationResult();

            public int ValidateCallCount { get; private set; }

            public Task<PluginAuthenticationResult> AuthenticateAsync(PluginAuthenticationRequest request, CancellationToken cancellationToken)
                => Task.FromResult(new PluginAuthenticationResult());

            public Task<PluginTokenValidationResult> ValidateTokenAsync(string opaqueToken, CancellationToken cancellationToken)
            {
                ValidateCallCount++;
                return Task.FromResult(ValidationResult);
            }

            public Task RevokeAsync(string opaqueToken, CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
