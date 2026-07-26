using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class PromoCodeConfiguration : IEntityTypeConfiguration<PromoCode>
{
    public void Configure(EntityTypeBuilder<PromoCode> builder)
    {
        builder.ToTable("PromoCodes");
        builder.HasKey(promo => promo.Id);

        builder.HasIndex(promo => promo.Code)
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false")
            .HasDatabaseName("UX_PromoCodes_Code");

        builder.HasIndex(promo => new { promo.IsActive, promo.ExpiryDate })
            .HasDatabaseName("IX_PromoCodes_IsActive_ExpiryDate");

        builder.HasIndex(promo => new { promo.VendorId, promo.IsActive })
            .HasDatabaseName("IX_PromoCodes_VendorId_IsActive");

        builder.HasIndex(promo => new { promo.CategoryId, promo.IsActive })
            .HasDatabaseName("IX_PromoCodes_CategoryId_IsActive");

        builder.Property(promo => promo.Code)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(promo => promo.DiscountValue)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(promo => promo.MaxDiscountAmount)
            .HasPrecision(18, 2);

        builder.Property(promo => promo.MaxUsagePerCustomer);

        builder.Property(promo => promo.UsedCount)
            .IsRequired();

        builder.HasMany(promo => promo.Usages)
            .WithOne(usage => usage.PromoCode)
            .HasForeignKey(usage => usage.PromoCodeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_PromoCodes_Code_Uppercase",
                "\"Code\" = UPPER(\"Code\")");

            table.HasCheckConstraint(
                "CK_PromoCodes_DiscountType",
                "\"DiscountType\" IN (0, 1)");

            table.HasCheckConstraint(
                "CK_PromoCodes_DiscountValue",
                "\"DiscountValue\" > 0");

            table.HasCheckConstraint(
                "CK_PromoCodes_MaxDiscountAmount",
                "\"MaxDiscountAmount\" IS NULL OR \"MaxDiscountAmount\" > 0");

            table.HasCheckConstraint(
                "CK_PromoCodes_UsageLimit",
                "\"UsageLimit\" IS NULL OR \"UsageLimit\" > 0");

            table.HasCheckConstraint(
                "CK_PromoCodes_MaxUsagePerCustomer",
                "\"MaxUsagePerCustomer\" IS NULL OR \"MaxUsagePerCustomer\" > 0");

            table.HasCheckConstraint(
                "CK_PromoCodes_MaxUsagePerCustomerWithinUsageLimit",
                "\"UsageLimit\" IS NULL OR \"MaxUsagePerCustomer\" IS NULL OR \"MaxUsagePerCustomer\" <= \"UsageLimit\"");

            table.HasCheckConstraint(
                "CK_PromoCodes_UsedCount",
                "\"UsedCount\" >= 0");

            table.HasCheckConstraint(
                "CK_PromoCodes_Scope",
                "(CASE WHEN \"VendorId\" IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN \"CategoryId\" IS NOT NULL THEN 1 ELSE 0 END) <= 1");
        });
    }
}
