using Jellyfin.Database.Implementations.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jellyfin.Database.Implementations.ModelConfiguration;

/// <summary>
/// FluentAPI configuration for the PlaybackItemKey entity.
/// </summary>
public class PlaybackItemKeyConfiguration : IEntityTypeConfiguration<PlaybackItemKey>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<PlaybackItemKey> builder)
    {
        builder.HasKey(e => e.Id);

        // A key is globally unique: a single key belongs to exactly one PlaybackItem.
        builder.HasIndex(e => e.Key).IsUnique();
    }
}
