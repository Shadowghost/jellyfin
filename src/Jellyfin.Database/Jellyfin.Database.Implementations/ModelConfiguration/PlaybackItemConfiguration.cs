using Jellyfin.Database.Implementations.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jellyfin.Database.Implementations.ModelConfiguration;

/// <summary>
/// FluentAPI configuration for the PlaybackItem entity.
/// </summary>
public class PlaybackItemConfiguration : IEntityTypeConfiguration<PlaybackItem>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<PlaybackItem> builder)
    {
        builder.HasKey(e => e.Id);

        // ItemId is a plain column (no FK to BaseItem) so item deletion never cascades here.
        builder.HasIndex(e => e.ItemId);

        // No cascade deletes: the retention task removes children explicitly in dependency order.
        builder.HasMany(e => e.Keys).WithOne(e => e.PlaybackItem).HasForeignKey(e => e.PlaybackItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(e => e.History).WithOne(e => e.PlaybackItem).HasForeignKey(e => e.PlaybackItemId).OnDelete(DeleteBehavior.Restrict);
    }
}
