using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.Affiliate;

namespace PointOfSale.Infrastructure.Data.Configurations.Affiliate;

public class AffiliateProductPolicyConfiguration : IEntityTypeConfiguration<AffiliateProductPolicy>
{
    public void Configure(EntityTypeBuilder<AffiliateProductPolicy> builder)
    {
        builder.ToTable("AffiliateProductPolicies");
        builder.HasKey(e => e.Id);

        // UNIQUE: one policy per product per tenant
        builder.HasIndex(e => new { e.TenantId, e.ProductId })
            .IsUnique()
            .HasDatabaseName("UQ_AffiliateProductPolicies_Tenant_Product");

        builder.HasIndex(e => e.TenantId)
            .HasDatabaseName("IX_AffiliateProductPolicies_TenantId");

        // Properties
        builder.Property(e => e.CommissionRate)
            .HasPrecision(8, 6)
            .IsRequired();

        builder.Property(e => e.MaxCommissionPerItem)
            .HasPrecision(18, 4);

        builder.Property(e => e.IsEnabled)
            .IsRequired();

        builder.Property(e => e.AffiliateCategoryId)
            .IsRequired(false);

        // CHECK: rate [0.0001, 0.50]
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_AffiliateProductPolicies_Rate",
            "\"CommissionRate\" >= 0.0001 AND \"CommissionRate\" <= 0.50"));

        // CHECK: max cap positive when set
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_AffiliateProductPolicies_MaxCap",
            "\"MaxCommissionPerItem\" IS NULL OR \"MaxCommissionPerItem\" > 0"));

        // FK: Product
        builder.HasOne<PointOfSale.Core.Entities.Product>()
            .WithMany()
            .HasForeignKey(e => e.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK: AffiliateCategory
        builder.HasOne<PointOfSale.Core.Entities.Affiliate.AffiliateCategory>()
            .WithMany()
            .HasForeignKey(e => e.AffiliateCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // AF-30A FIX D-2: CHECK — enabled policies MUST have a category assigned.
        // Defense-in-depth: domain validation guards this, but if bypassed, DB rejects.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_AffiliateProductPolicies_EnabledRequiresCategory",
            "NOT (\"IsEnabled\" = true AND \"AffiliateCategoryId\" IS NULL)"));
    }
}
