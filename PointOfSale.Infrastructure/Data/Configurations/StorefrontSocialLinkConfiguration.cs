using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class StorefrontSocialLinkConfiguration : IEntityTypeConfiguration<StorefrontSocialLink>
{
    public void Configure(EntityTypeBuilder<StorefrontSocialLink> builder)
    {
        builder.ToTable("StorefrontSocialLinks");
        builder.HasKey(link => link.Id);

        builder.Property(link => link.Platform)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(link => link.Url)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(link => link.Icon)
            .HasMaxLength(80);

        builder.Property(link => link.SortOrder)
            .IsRequired();

        builder.Property(link => link.IsActive)
            .IsRequired();

        builder.Property(link => link.CreatedAt)
            .IsRequired();

        builder.Property(link => link.UpdatedAt)
            .IsRequired();

        builder.HasIndex(link => new { link.TenantId, link.SortOrder })
            .HasDatabaseName("IX_StorefrontSocialLinks_TenantId_SortOrder");

        builder.HasIndex(link => new { link.TenantId, link.Platform })
            .IsUnique()
            .HasFilter("\"TenantId\" IS NOT NULL")
            .HasDatabaseName("UQ_StorefrontSocialLinks_TenantId_Platform");

        builder.HasIndex(link => link.Platform)
            .IsUnique()
            .HasFilter("\"TenantId\" IS NULL")
            .HasDatabaseName("UQ_StorefrontSocialLinks_Global_Platform");
    }
}
