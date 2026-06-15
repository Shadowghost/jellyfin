using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Server.Implementations.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Server.Migrations.Routines;

/// <summary>
/// Normalizes stored <c>AuthenticationProviderId</c> values after the auth overhaul: the removed
/// built-in provider remaps to <see cref="PasswordValidator"/>; plugin-owned users (<c>pt:</c>) are
/// left untouched; values from removed auth-provider plugins are quarantined as <c>orphaned</c> so
/// those accounts cannot log in until an admin reassigns or deletes them.
/// </summary>
[JellyfinMigration("2026-06-15T12:00:00", nameof(NormalizeAuthProviderIds), Stage = Stages.JellyfinMigrationStageTypes.CoreInitialisation)]
#pragma warning disable SA1649 // File name should match first type name
public class NormalizeAuthProviderIds : IAsyncMigrationRoutine
#pragma warning restore SA1649 // File name should match first type name
{
    private readonly ILogger<NormalizeAuthProviderIds> _logger;
    private readonly IDbContextFactory<JellyfinDbContext> _contextFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="NormalizeAuthProviderIds"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="contextFactory">The database context factory.</param>
    public NormalizeAuthProviderIds(
        ILogger<NormalizeAuthProviderIds> logger,
        IDbContextFactory<JellyfinDbContext> contextFactory)
    {
        _logger = logger;
        _contextFactory = contextFactory;
    }

    /// <inheritdoc/>
    public async Task PerformAsync(CancellationToken cancellationToken)
    {
        const string legacyDefaultProviderId = "Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider";
        const string orphanedProviderId = "orphaned";
        const string pluginProviderPrefix = "pt:";

        var builtInProviderId = typeof(PasswordValidator).FullName!;

        var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var users = await dbContext.Users.ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var user in users)
            {
                var providerId = user.AuthenticationProviderId;

                // Built-in, plugin-owned, or already quarantined users need no change (idempotent).
                if (string.Equals(providerId, builtInProviderId, StringComparison.Ordinal)
                    || string.Equals(providerId, orphanedProviderId, StringComparison.Ordinal)
                    || (providerId is not null && providerId.StartsWith(pluginProviderPrefix, StringComparison.Ordinal)))
                {
                    continue;
                }

                // The removed built-in provider (and empty values) remap to the new built-in validator.
                if (string.IsNullOrEmpty(providerId) || string.Equals(providerId, legacyDefaultProviderId, StringComparison.Ordinal))
                {
                    user.AuthenticationProviderId = builtInProviderId;
                    continue;
                }

                // Any other unresolvable value belonged to a removed auth-provider plugin: quarantine
                // the account until an admin reassigns or deletes it.
                if (Type.GetType(providerId) is null)
                {
                    _logger.LogWarning(
                        "User {User} used a removed authentication provider '{ProviderId}' and has been quarantined; reassign or delete the account",
                        user.Username,
                        providerId);
                    user.AuthenticationProviderId = orphanedProviderId;
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
