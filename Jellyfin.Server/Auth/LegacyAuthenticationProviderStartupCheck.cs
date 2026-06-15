using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Server.Auth;

/// <summary>
/// Logs a clear error at startup for any loaded plugin that still implements the removed
/// <c>IAuthenticationProvider</c> interface. Best-effort and reflective (the interface no longer
/// exists); never crashes startup.
/// </summary>
public sealed class LegacyAuthenticationProviderStartupCheck : IHostedService
{
    private const string RemovedInterfaceName = "IAuthenticationProvider";

    private readonly IPluginManager _pluginManager;
    private readonly ILogger<LegacyAuthenticationProviderStartupCheck> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LegacyAuthenticationProviderStartupCheck"/> class.
    /// </summary>
    /// <param name="pluginManager">The plugin manager.</param>
    /// <param name="logger">The logger.</param>
    public LegacyAuthenticationProviderStartupCheck(IPluginManager pluginManager, ILogger<LegacyAuthenticationProviderStartupCheck> logger)
    {
        _pluginManager = pluginManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var plugin in _pluginManager.Plugins)
        {
            var assembly = plugin.Instance?.GetType().Assembly;
            if (assembly is null)
            {
                continue;
            }

            if (ImplementsRemovedInterface(assembly))
            {
                _logger.LogError(
                    "Plugin {Name} implements the removed IAuthenticationProvider interface and will not function. Migrate to IAuthenticationPlugin.",
                    plugin.Name);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool ImplementsRemovedInterface(Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes().Any(type =>
                type.GetInterfaces().Any(i => string.Equals(i.Name, RemovedInterfaceName, StringComparison.Ordinal)));
        }
        catch (ReflectionTypeLoadException ex)
        {
            // The interface type is gone, so types that referenced it fail to load. Treat a loader
            // exception that mentions the removed interface as a positive match.
            return ex.LoaderExceptions.Any(e => e?.Message.Contains(RemovedInterfaceName, StringComparison.Ordinal) == true);
        }
        catch (Exception)
        {
            // Any other reflection failure: do not block startup, do not claim a match.
            return false;
        }
    }
}
