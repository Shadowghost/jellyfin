using Jellyfin.Database.Implementations.Entities.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jellyfin.Database.Implementations.ModelConfiguration
{
    /// <summary>
    /// FluentAPI configuration for the TempToken entity.
    /// </summary>
    public class TempTokenConfiguration : IEntityTypeConfiguration<TempToken>
    {
        /// <inheritdoc/>
        public void Configure(EntityTypeBuilder<TempToken> builder)
        {
            builder
                .HasIndex(entity => entity.Jti)
                .IsUnique();

            builder
                .HasIndex(entity => entity.ActingUserId);
        }
    }
}
