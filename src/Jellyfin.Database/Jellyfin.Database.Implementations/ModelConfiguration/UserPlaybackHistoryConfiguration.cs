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

        // UserId is a plain column (no FK to User). No cascade deletes; retention removes streams explicitly.
        builder.HasMany(e => e.Streams).WithOne(e => e.History).HasForeignKey(e => e.HistoryId).OnDelete(DeleteBehavior.Restrict);
    }
}
