using Jellyfin.Database.Implementations.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jellyfin.Database.Implementations.ModelConfiguration;

/// <summary>
/// FluentAPI configuration for the UserPlaybackHistoryStream entity.
/// </summary>
public class UserPlaybackHistoryStreamConfiguration : IEntityTypeConfiguration<UserPlaybackHistoryStream>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<UserPlaybackHistoryStream> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.HistoryId);
        builder.HasIndex(e => new { e.StreamType, e.Origin, e.VideoRange });
        builder.HasIndex(e => new { e.StreamType, e.Origin, e.Language });
    }
}
