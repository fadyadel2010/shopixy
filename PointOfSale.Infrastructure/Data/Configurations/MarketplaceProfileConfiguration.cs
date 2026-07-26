using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class MarketplaceProfileConfiguration : IEntityTypeConfiguration<MarketplaceProfile>
{
    public void Configure(EntityTypeBuilder<MarketplaceProfile> builder)
    {
        builder.ToTable("MarketplaceProfiles");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.DisplayName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.Bio)
            .HasColumnType("text");

        builder.Property(p => p.ProfileImageUrl)
            .HasMaxLength(500);

        builder.Property(p => p.IsActive)
            .IsRequired();

        builder.HasIndex(p => p.TenantId);
        builder.HasIndex(p => p.UserId);
        builder.HasIndex(p => p.IsActive)
            .HasFilter("\"IsDeleted\" = false");

        builder.HasIndex(p => new { p.TenantId, p.UserId })
            .IsUnique()
            .HasDatabaseName("UQ_MarketplaceProfiles_TenantUser");
    }
}
