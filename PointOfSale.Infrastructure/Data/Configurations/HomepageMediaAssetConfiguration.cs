using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class HomepageMediaAssetConfiguration : IEntityTypeConfiguration<HomepageMediaAsset>
{
    public void Configure(EntityTypeBuilder<HomepageMediaAsset> builder)
    {
        builder.ToTable("HomepageMediaAssets");
        builder.HasKey(asset => asset.Id);
        builder.Property(asset => asset.ObjectKey).HasMaxLength(512).IsRequired();
        builder.Property(asset => asset.ContentType).HasMaxLength(100).IsRequired();
        builder.HasIndex(asset => new { asset.TenantId, asset.Id });
        builder.HasIndex(asset => asset.ObjectKey).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(asset => asset.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
