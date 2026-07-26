using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.Affiliate;

namespace PointOfSale.Infrastructure.Data.Configurations.Affiliate;

public class AffiliateAttributionConfiguration : IEntityTypeConfiguration<AffiliateAttribution>
{
    public void Configure(EntityTypeBuilder<AffiliateAttribution> builder)
    {
        builder.ToTable("AffiliateAttributions");
        builder.HasKey(e => e.Id);

        // CRITICAL: one attribution per order — ever
        builder.HasIndex(e => new { e.TenantId, e.OrderId })
            .IsUnique()
            .HasDatabaseName("UQ_AffiliateAttributions_Tenant_Order");

        builder.HasIndex(e => new { e.TenantId, e.OrderId, e.IsDeleted })
            .HasDatabaseName("IX_AffiliateAttributions_Tenant_Order_Active");

        builder.HasIndex(e => e.AffiliateAccountId)
            .HasDatabaseName("IX_AffiliateAttributions_AffiliateAccountId");

        builder.HasIndex(e => e.TenantId)
            .HasDatabaseName("IX_AffiliateAttributions_TenantId");

        // Properties
        builder.Property(e => e.ReferralCode)
            .IsRequired()
            .HasMaxLength(12);

        builder.Property(e => e.Source)
            .IsRequired();

        builder.Property(e => e.AttributedAtUtc)
            .IsRequired();

        // CHECK: source is valid enum
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_AffiliateAttributions_Source",
            "\"Source\" IN (0, 1, 2)"));

        // FK: Order
        builder.HasOne<PointOfSale.Core.Entities.Order>()
            .WithMany()
            .HasForeignKey(e => e.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK: AffiliateAccount
        builder.HasOne<AffiliateAccount>()
            .WithMany()
            .HasForeignKey(e => e.AffiliateAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // AF-15: PromotionLinkId (nullable — only populated for v2 flow)
        builder.HasIndex(e => e.PromotionLinkId)
            .HasFilter("\"PromotionLinkId\" IS NOT NULL")
            .HasDatabaseName("IX_AffiliateAttributions_PromotionLinkId");
    }
}
