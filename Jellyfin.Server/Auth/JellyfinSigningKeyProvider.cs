using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Jellyfin.Server.Auth;

/// <summary>
/// Generates, persists and rotates the symmetric keys used to sign Jellyfin-issued JWTs.
/// </summary>
public sealed class JellyfinSigningKeyProvider : IJellyfinSigningKeyProvider
{
    private const int KeySizeBytes = 64;

    private readonly string _keyFilePath;
    private readonly ILogger<JellyfinSigningKeyProvider> _logger;
    private readonly Lock _lock = new();

    private SymmetricSecurityKey _current;
    private SymmetricSecurityKey _previous;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinSigningKeyProvider"/> class.
    /// </summary>
    /// <param name="applicationPaths">The application paths.</param>
    /// <param name="logger">The logger.</param>
    public JellyfinSigningKeyProvider(IApplicationPaths applicationPaths, ILogger<JellyfinSigningKeyProvider> logger)
    {
        _logger = logger;
        _keyFilePath = Path.Combine(applicationPaths.ConfigurationDirectoryPath, "auth.xml");

        var store = Load() ?? CreateAndPersist();
        (_current, _previous) = FromStore(store);
    }

    /// <inheritdoc />
    public SecurityKey GetCurrentSigningKey()
    {
        lock (_lock)
        {
            return _current;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<SecurityKey> GetValidationKeys()
    {
        lock (_lock)
        {
            return new SecurityKey[] { _current, _previous };
        }
    }

    /// <inheritdoc />
    public Task RotateAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var store = new JwtSigningKeyStore
            {
                Current = Convert.ToBase64String(GenerateKeyBytes()),
                Previous = Convert.ToBase64String(_current.Key)
            };

            Persist(store);
            (_current, _previous) = FromStore(store);
            _logger.LogInformation("Rotated JWT signing keys");
        }

        return Task.CompletedTask;
    }

    private static byte[] GenerateKeyBytes() => RandomNumberGenerator.GetBytes(KeySizeBytes);

    private static SymmetricSecurityKey MakeKey(byte[] keyBytes)
        => new(keyBytes) { KeyId = Convert.ToHexString(SHA256.HashData(keyBytes))[..16] };

    private static (SymmetricSecurityKey Current, SymmetricSecurityKey Previous) FromStore(JwtSigningKeyStore store)
        => (MakeKey(Convert.FromBase64String(store.Current)), MakeKey(Convert.FromBase64String(store.Previous)));

    private JwtSigningKeyStore CreateAndPersist()
    {
        var store = new JwtSigningKeyStore
        {
            Current = Convert.ToBase64String(GenerateKeyBytes()),
            Previous = Convert.ToBase64String(GenerateKeyBytes())
        };

        Persist(store);
        _logger.LogInformation("Generated new JWT signing keys");
        return store;
    }

    private JwtSigningKeyStore? Load()
    {
        if (!File.Exists(_keyFilePath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(_keyFilePath);
            var serializer = new XmlSerializer(typeof(JwtSigningKeyStore));
            if (serializer.Deserialize(stream) is JwtSigningKeyStore store
                && !string.IsNullOrEmpty(store.Current)
                && !string.IsNullOrEmpty(store.Previous))
            {
                return store;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or FormatException)
        {
            _logger.LogError(ex, "Failed to read JWT signing keys; regenerating");
        }

        return null;
    }

    private void Persist(JwtSigningKeyStore store)
    {
        using (var stream = File.Create(_keyFilePath))
        {
            var serializer = new XmlSerializer(typeof(JwtSigningKeyStore));
            serializer.Serialize(stream, store);
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_keyFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
