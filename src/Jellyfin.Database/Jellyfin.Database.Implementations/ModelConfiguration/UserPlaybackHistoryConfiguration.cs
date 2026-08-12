using Jellyfin.Database.Implementations.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jellyfin.Database.Implementations.ModelConfiguration;

/// <summary>
/// FluentAPI configuration for the UserPlaybackHistory entity.
/// </summary>
public class UserPlaybackHistoryConfiguration : IEntityTypeConfiguration<UserPlaybackHistory>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<UserPlaybackHistory> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.PlaybackItemId, e.PlayedToCompletion });
        builder.HasIndex(e => new { e.UserId, e.PlaybackItemId, e.DateStopped });
        builder.HasIndex(e => new { e.UserId, e.DateStopped });
        builder.HasIndex(e => e.DateStopped);

        // Activity statistics filter to recorded entries before applying the date window, so Source
        // leads. Imported entries far outnumber recorded ones on an upgraded server.
        builder.HasIndex(e => new { e.Source, e.DateStopped });
        builder.HasMany(e => e.Streams).WithOne(e => e.History).HasForeignKey(e => e.HistoryId).OnDelete(DeleteBehavior.Restrict);
    }
}
