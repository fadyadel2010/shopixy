using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class PromoCodeUsageConfiguration : IEntityTypeConfiguration<PromoCodeUsage>
{
    public void Configure(EntityTypeBuilder<PromoCodeUsage> builder)
    {
        builder.ToTable("PromoCodeUsages");
        builder.HasKey(usage => usage.Id);

        builder.HasIndex(usage => new { usage.PromoCodeId, usage.MarketplaceOrderId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false")
            .HasDatabaseName("UX_PromoCodeUsages_PromoCodeId_MarketplaceOrderId");

        builder.HasIndex(usage => usage.PromoCodeId)
            .HasDatabaseName("IX_PromoCodeUsages_PromoCodeId");

        builder.HasIndex(usage => new { usage.PromoCodeId, usage.CustomerId })
            .HasFilter("\"CustomerId\" IS NOT NULL AND \"IsDeleted\" = false")
            .HasDatabaseName("IX_PromoCodeUsages_PromoCodeId_CustomerId");

        builder.HasIndex(usage => usage.MarketplaceOrderId)
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false")
            .HasDatabaseName("UX_PromoCodeUsages_MarketplaceOrderId");

        builder.HasIndex(usage => new { usage.MarketplaceOrderId, usage.AppliedAtUtc })
            .HasDatabaseName("IX_PromoCodeUsages_MarketplaceOrderId_AppliedAtUtc");

        builder.Property(usage => usage.PromoCodeSnapshot)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(usage => usage.DiscountAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(usage => usage.EligibleBaseCommissionAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(usage => usage.CorrelationId)
            .HasMaxLength(200);

        builder.Property(usage => usage.AppliedAtUtc)
            .IsRequired();

        builder.HasOne(usage => usage.MarketplaceOrder)
            .WithMany()
            .HasForeignKey(usage => usage.MarketplaceOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_PromoCodeUsages_DiscountAmount",
                "\"DiscountAmount\" >= 0");

            table.HasCheckConstraint(
                "CK_PromoCodeUsages_EligibleBaseCommissionAmount",
                "\"EligibleBaseCommissionAmount\" >= 0");
        });
    }
}
