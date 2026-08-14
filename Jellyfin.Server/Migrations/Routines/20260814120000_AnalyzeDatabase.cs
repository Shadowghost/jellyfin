using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Server.ServerSetupApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Server.Migrations.Routines;

/// <summary>
/// Rebuilds the query planner statistics after the schema and data migrations have run.
/// </summary>
[JellyfinMigration("2026-08-14T12:00:00", nameof(AnalyzeDatabase))]
internal class AnalyzeDatabase : IAsyncMigrationRoutine
{
    private readonly IStartupLogger<AnalyzeDatabase> _logger;
    private readonly IDbContextFactory<JellyfinDbContext> _dbProvider;

    public AnalyzeDatabase(IStartupLogger<AnalyzeDatabase> logger, IDbContextFactory<JellyfinDbContext> dbProvider)
    {
        _logger = logger;
        _dbProvider = dbProvider;
    }

    /// <inheritdoc/>
    public async Task PerformAsync(CancellationToken cancellationToken)
    {
        // Must be a full ANALYZE: `PRAGMA optimize` samples, and underestimates cardinality by
        // more than an order of magnitude on a large library regardless of analysis_limit.
        var context = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            _logger.LogInformation("Rebuilding database statistics, this can take a while on a large library.");
            await context.Database.ExecuteSqlRawAsync("ANALYZE", cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Database statistics rebuilt.");
        }
    }
}
