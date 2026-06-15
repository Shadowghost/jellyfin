using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities.Security;
using MediaBrowser.Controller.Security;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Server.Implementations.Security
{
    /// <inheritdoc />
    public class TempTokenStore : ITempTokenStore
    {
        private readonly IDbContextFactory<JellyfinDbContext> _dbProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="TempTokenStore"/> class.
        /// </summary>
        /// <param name="dbProvider">The database provider.</param>
        public TempTokenStore(IDbContextFactory<JellyfinDbContext> dbProvider)
        {
            _dbProvider = dbProvider;
        }

        /// <inheritdoc />
        public async Task RecordAsync(string jti, Guid actingUserId, DateTime issuedAt, DateTime expiresAt, IReadOnlyList<string> scopes, string? itemId, string label, CancellationToken cancellationToken = default)
        {
            var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (dbContext.ConfigureAwait(false))
            {
                dbContext.TempTokens.Add(new TempToken(jti, actingUserId, issuedAt, expiresAt, string.Join(' ', scopes), label)
                {
                    ItemId = itemId
                });
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async Task<bool> IsRevokedAsync(string jti, CancellationToken cancellationToken = default)
        {
            var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (dbContext.ConfigureAwait(false))
            {
                // A token is treated as revoked if it has an explicit revocation timestamp or is unknown.
                var token = await dbContext.TempTokens
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Jti == jti, cancellationToken)
                    .ConfigureAwait(false);
                return token is null || token.RevokedAt is not null;
            }
        }

        /// <inheritdoc />
        public async Task<bool> RevokeAsync(int tokenId, Guid requestingUserId, bool isAdministrator, CancellationToken cancellationToken = default)
        {
            var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (dbContext.ConfigureAwait(false))
            {
                var token = await dbContext.TempTokens
                    .FirstOrDefaultAsync(t => t.Id == tokenId, cancellationToken)
                    .ConfigureAwait(false);

                if (token is null || (!isAdministrator && !token.ActingUserId.Equals(requestingUserId)))
                {
                    return false;
                }

                token.RevokedAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<TempTokenInfo>> GetForUserAsync(Guid actingUserId, CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (dbContext.ConfigureAwait(false))
            {
                var tokens = await dbContext.TempTokens
                    .AsNoTracking()
                    .Where(t => t.ActingUserId.Equals(actingUserId) && t.RevokedAt == null && t.ExpiresAt > now)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                return tokens.Select(t => new TempTokenInfo
                {
                    Id = t.Id,
                    Label = t.Label,
                    Scopes = string.IsNullOrEmpty(t.Scopes) ? Array.Empty<string>() : t.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                    ItemId = t.ItemId,
                    IssuedAt = t.IssuedAt,
                    ExpiresAt = t.ExpiresAt
                }).ToList();
            }
        }
    }
}
