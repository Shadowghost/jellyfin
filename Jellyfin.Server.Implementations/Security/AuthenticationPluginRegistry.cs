using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Server.Implementations.Security
{
    /// <summary>
    /// Default implementation of <see cref="IAuthenticationPluginRegistry"/> and
    /// <see cref="IAuthenticationPluginContext"/>. Fronts plugin lookup, the cross-request validation cache
    /// and a per-plugin circuit breaker.
    /// </summary>
    public sealed class AuthenticationPluginRegistry : IAuthenticationPluginRegistry, IAuthenticationPluginContext
    {
        private const string CacheKeyPrefix = "authplugin:";
        private const int WindowSeconds = 60;
        private const int OpenSeconds = 30;
        private const int MinimumSamples = 20;
        private const double FailureThreshold = 0.5;

        private readonly Dictionary<string, IAuthenticationPlugin> _plugins;
        private readonly IMemoryCache _cache;
        private readonly ILogger<AuthenticationPluginRegistry> _logger;
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _userTokenIndex = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, CircuitBreaker> _breakers = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Initializes a new instance of the <see cref="AuthenticationPluginRegistry"/> class.
        /// </summary>
        /// <param name="plugins">The registered authentication plugins.</param>
        /// <param name="cache">The shared memory cache.</param>
        /// <param name="logger">The logger.</param>
        public AuthenticationPluginRegistry(
            IEnumerable<IAuthenticationPlugin> plugins,
            IMemoryCache cache,
            ILogger<AuthenticationPluginRegistry> logger)
        {
            _cache = cache;
            _logger = logger;
            _plugins = new Dictionary<string, IAuthenticationPlugin>(StringComparer.OrdinalIgnoreCase);
            foreach (var plugin in plugins)
            {
                _plugins[plugin.Id] = plugin;
            }
        }

        /// <inheritdoc />
        public IAuthenticationPlugin? GetById(string pluginId)
        {
            if (string.IsNullOrEmpty(pluginId) || !_plugins.TryGetValue(pluginId, out var plugin))
            {
                return null;
            }

            return plugin.Enabled ? plugin : null;
        }

        /// <inheritdoc />
        public IReadOnlyList<IAuthenticationPlugin> GetEnabledPlugins()
            => _plugins.Values.Where(p => p.Enabled).ToList();

        /// <inheritdoc />
        public bool IsCircuitOpen(string pluginId)
            => _breakers.TryGetValue(pluginId, out var breaker) && breaker.IsOpen();

        /// <inheritdoc />
        public void RecordSuccess(string pluginId)
            => GetBreaker(pluginId).RecordSuccess();

        /// <inheritdoc />
        public void RecordFailure(string pluginId)
            => GetBreaker(pluginId).RecordFailure();

        /// <inheritdoc />
        public async Task<PluginTokenValidationResult> ValidateAsync(IAuthenticationPlugin plugin, string opaqueToken, CancellationToken cancellationToken)
        {
            var hash = HashToken(opaqueToken);
            var cacheKey = CacheKeyPrefix + hash;
            if (_cache.TryGetValue(cacheKey, out PluginTokenValidationResult? cached) && cached is not null)
            {
                return cached;
            }

            var result = await plugin.ValidateTokenAsync(opaqueToken, cancellationToken).ConfigureAwait(false);
            if (result.Valid)
            {
                CacheResult(cacheKey, result);
                if (result.Identity is not null)
                {
                    IndexUserToken(plugin.Id, result.Identity.ExternalUserId, hash);
                }
            }

            return result;
        }

        /// <inheritdoc />
        public Task InvalidateTokenAsync(string opaqueToken, CancellationToken cancellationToken)
        {
            _cache.Remove(CacheKeyPrefix + HashToken(opaqueToken));
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task InvalidateUserAsync(string pluginId, string externalUserId, CancellationToken cancellationToken)
        {
            if (_userTokenIndex.TryRemove(UserIndexKey(pluginId, externalUserId), out var hashes))
            {
                foreach (var hash in hashes.Keys)
                {
                    _cache.Remove(CacheKeyPrefix + hash);
                }
            }

            return Task.CompletedTask;
        }

        private static string UserIndexKey(string pluginId, string externalUserId)
            => pluginId + "|" + externalUserId;

        private static string HashToken(string token)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(bytes);
        }

        private void CacheResult(string cacheKey, PluginTokenValidationResult result)
        {
            var ttl = result.ExpiresAt - DateTimeOffset.UtcNow;
            if (ttl <= TimeSpan.Zero)
            {
                // Already expired; do not cache.
                return;
            }

            if (ttl > TimeSpan.FromSeconds(30))
            {
                ttl = TimeSpan.FromSeconds(30);
            }

            if (ttl < TimeSpan.FromSeconds(1))
            {
                ttl = TimeSpan.FromSeconds(1);
            }

            _cache.Set(cacheKey, result, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl
            });
        }

        private void IndexUserToken(string pluginId, string externalUserId, string hash)
        {
            var set = _userTokenIndex.GetOrAdd(UserIndexKey(pluginId, externalUserId), _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
            set[hash] = 0;
        }

        private CircuitBreaker GetBreaker(string pluginId)
            => _breakers.GetOrAdd(pluginId, _ => new CircuitBreaker());

        /// <summary>
        /// Hand-rolled, thread-safe per-plugin circuit breaker over a rolling 60s window.
        /// </summary>
        private sealed class CircuitBreaker
        {
            private readonly Lock _lock = new();
            private readonly Queue<(long Timestamp, bool Success)> _events = new();
            private long _openedAt;
            private bool _open;
            private bool _probing;

            public bool IsOpen()
            {
                lock (_lock)
                {
                    if (!_open)
                    {
                        return false;
                    }

                    if (Stopwatch.GetElapsedTime(_openedAt) >= TimeSpan.FromSeconds(OpenSeconds))
                    {
                        // Half-open: allow a single probe through.
                        if (!_probing)
                        {
                            _probing = true;
                            return false;
                        }
                    }

                    return true;
                }
            }

            public void RecordSuccess()
            {
                lock (_lock)
                {
                    Add(true);
                    if (_probing)
                    {
                        // Probe succeeded; close the circuit.
                        _open = false;
                        _probing = false;
                        return;
                    }

                    if (!_open)
                    {
                        Evaluate();
                    }
                }
            }

            public void RecordFailure()
            {
                lock (_lock)
                {
                    Add(false);
                    if (_probing)
                    {
                        // Probe failed; re-open.
                        _open = true;
                        _probing = false;
                        _openedAt = Stopwatch.GetTimestamp();
                        return;
                    }

                    if (_open)
                    {
                        return;
                    }

                    Evaluate();
                }
            }

            private void Add(bool success)
            {
                var now = Stopwatch.GetTimestamp();
                _events.Enqueue((now, success));
                Trim(now);
            }

            private void Trim(long now)
            {
                while (_events.Count > 0
                    && Stopwatch.GetElapsedTime(_events.Peek().Timestamp, now) >= TimeSpan.FromSeconds(WindowSeconds))
                {
                    _events.Dequeue();
                }
            }

            private void Evaluate()
            {
                var total = _events.Count;
                if (total < MinimumSamples)
                {
                    return;
                }

                var failures = 0;
                foreach (var entry in _events)
                {
                    if (!entry.Success)
                    {
                        failures++;
                    }
                }

                if ((double)failures / total > FailureThreshold)
                {
                    _open = true;
                    _probing = false;
                    _openedAt = Stopwatch.GetTimestamp();
                }
            }
        }
    }
}
