using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.Affiliate;

namespace PointOfSale.Infrastructure.Data.Configurations.Affiliate;

/// <summary>
/// AF-15: EF Core configuration for AffiliatePromotionLinks table.
///
/// Constraints:
/// - UQ_AffiliatePromotionLinks_User_Store_Product: (AffiliateUserId, TenantId, ProductId) unique
/// - UQ_AffiliatePromotionLinks_ReferralCode: (ReferralCode) globally unique
/// - FK → AffiliateUsers.Id
/// - FK → Products.Id
/// </summary>
public class AffiliatePromotionLinkConfiguration : IEntityTypeConfiguration<AffiliatePromotionLink>
{
    public void Configure(EntityTypeBuilder<AffiliatePromotionLink> builder)
    {
        builder.ToTable("AffiliatePromotionLinks");
        builder.HasKey(e => e.Id);

        // UNIQUE: one promotion link per affiliate user + store + product
        builder.HasIndex(e => new { e.AffiliateUserId, e.TenantId, e.ProductId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false")
            .HasDatabaseName("UQ_AffiliatePromotionLinks_User_Store_Product");

        // UNIQUE: referral code is globally unique
        builder.HasIndex(e => e.ReferralCode)
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false")
            .HasDatabaseName("UQ_AffiliatePromotionLinks_ReferralCode");

        // Performance indexes
        builder.HasIndex(e => e.TenantId)
            .HasDatabaseName("IX_AffiliatePromotionLinks_TenantId");

        builder.HasIndex(e => e.AffiliateUserId)
            .HasDatabaseName("IX_AffiliatePromotionLinks_AffiliateUserId");

        builder.HasIndex(e => e.ProductId)
            .HasDatabaseName("IX_AffiliatePromotionLinks_ProductId");

        // Covering index for middleware hot path: ReferralCode + TenantId lookup
        builder.HasIndex(e => new { e.ReferralCode, e.TenantId })
            .HasDatabaseName("IX_AffiliatePromotionLinks_Code_Store");

        // Properties
        builder.Property(e => e.ReferralCode)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(e => e.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // FK: AffiliateUser
        builder.HasOne(e => e.AffiliateUser)
            .WithMany()
            .HasForeignKey(e => e.AffiliateUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK: Product (no navigation needed — lightweight reference)
        builder.HasOne<PointOfSale.Core.Entities.Product>()
            .WithMany()
            .HasForeignKey(e => e.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
