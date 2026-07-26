using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class SmartOfferConfiguration : IEntityTypeConfiguration<SmartOffer>
{
    public void Configure(EntityTypeBuilder<SmartOffer> builder)
    {
        builder.ToTable("SmartOffers", table =>
        {
            table.HasCheckConstraint(
                "CK_SmartOffers_CouponDiscountType",
                "\"CouponDiscountType\" IS NULL OR \"CouponDiscountType\" IN ('Percentage', 'FixedAmount')");
            table.HasCheckConstraint(
                "CK_SmartOffers_CouponDiscountValue_Positive",
                "\"CouponDiscountValue\" IS NULL OR \"CouponDiscountValue\" > 0");
            table.HasCheckConstraint(
                "CK_SmartOffers_CouponDiscountValue_Percentage",
                "\"CouponDiscountValue\" IS NULL OR COALESCE(\"CouponDiscountType\", 'Percentage') <> 'Percentage' OR \"CouponDiscountValue\" <= 100");
            table.HasCheckConstraint(
                "CK_SmartOffers_CouponDiscountValue_FixedAmount",
                "\"CouponDiscountValue\" IS NULL OR COALESCE(\"CouponDiscountType\", 'Percentage') <> 'FixedAmount' OR \"CouponDiscountValue\" <= 1000000.00");
            
            // Enforce entity invariant: CouponCode must be present (non-null and non-whitespace) when any coupon settings exist
            // Use REGEXP_REPLACE to strip all whitespace (tabs, newlines, spaces) to match string.IsNullOrWhiteSpace behavior
            table.HasCheckConstraint(
                "CK_SmartOffers_CouponCode_Presence",
                "NULLIF(REGEXP_REPLACE(\"CouponCode\", '\\s', '', 'g'), '') IS NOT NULL OR (\"CouponDiscountType\" IS NULL AND \"CouponDiscountValue\" IS NULL AND \"CouponMinOrderAmount\" IS NULL AND \"CouponMaxDiscountAmount\" IS NULL)");
            
            // Enforce entity invariant: CouponDiscountValue must be present when CouponCode exists (non-null and non-whitespace)
            // Use REGEXP_REPLACE to strip all whitespace (tabs, newlines, spaces) to match string.IsNullOrWhiteSpace behavior
            table.HasCheckConstraint(
                "CK_SmartOffers_CouponDiscountValue_Presence",
                "NULLIF(REGEXP_REPLACE(\"CouponCode\", '\\s', '', 'g'), '') IS NULL OR \"CouponDiscountValue\" IS NOT NULL");
        });

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.TenantId, x.Name })
            .IsUnique()
            .HasDatabaseName("IX_SmartOffers_TenantId_Name");

        builder.HasIndex(x => new { x.TenantId, x.Status, x.Priority })
            .HasDatabaseName("IX_SmartOffers_TenantId_Status_Priority");

        builder.HasIndex(x => new { x.TenantId, x.TriggerType, x.PagesScope })
            .HasDatabaseName("IX_SmartOffers_TenantId_TriggerType_PagesScope");

        builder.Property(x => x.RowVersion)
            .HasColumnName("xmin")
            .IsRowVersion();

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(x => x.TriggerType)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(x => x.PagesScope)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(x => x.PopupTitle)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.PopupDescription)
            .HasMaxLength(1000);

        builder.Property(x => x.ButtonText)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.CouponCode)
            .HasMaxLength(100);

        builder.Property(x => x.CouponDiscountType)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(x => x.CouponDiscountValue)
            .HasPrecision(18, 2);

        builder.Property(x => x.CouponMinOrderAmount)
            .HasPrecision(18, 2);

        builder.Property(x => x.CouponMaxDiscountAmount)
            .HasPrecision(18, 2);

        builder.Property(x => x.ImageUrl)
            .HasMaxLength(1000);

        builder.Property(x => x.BackgroundColor)
            .HasMaxLength(32);
    }
}
