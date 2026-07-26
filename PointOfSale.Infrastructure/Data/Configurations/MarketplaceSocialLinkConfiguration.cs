using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class MarketplaceSocialLinkConfiguration : IEntityTypeConfiguration<MarketplaceSocialLink>
{
    public void Configure(EntityTypeBuilder<MarketplaceSocialLink> builder)
    {
        builder.ToTable("MarketplaceSocialLinks");
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

        builder.HasIndex(link => link.SortOrder)
            .HasDatabaseName("IX_MarketplaceSocialLinks_SortOrder");

        builder.HasIndex(link => link.Platform)
            .IsUnique()
            .HasDatabaseName("UQ_MarketplaceSocialLinks_Platform");
    }
}
