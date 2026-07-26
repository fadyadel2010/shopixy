using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.Affiliate;

namespace PointOfSale.Infrastructure.Data.Configurations.Affiliate;

public class AffiliateAccountConfiguration : IEntityTypeConfiguration<AffiliateAccount>
{
    public void Configure(EntityTypeBuilder<AffiliateAccount> builder)
    {
        builder.ToTable("AffiliateAccounts");
        builder.HasKey(e => e.Id);

        // UNIQUE: one affiliate account per user per tenant
        builder.HasIndex(e => new { e.TenantId, e.UserId })
            .IsUnique()
            .HasDatabaseName("UQ_AffiliateAccounts_Tenant_User");

        // UNIQUE: referral code unique per tenant (legacy, backward-compatible)
        builder.HasIndex(e => new { e.TenantId, e.ReferralCode })
            .IsUnique()
            .HasDatabaseName("UQ_AffiliateAccounts_Tenant_ReferralCode");

        // AUDIT FIX F-D1: Global uniqueness on ReferralCode.
        // AF-19 makes affiliates global, so ReferralCode must be globally unique.
        // The service layer already checks globally via IgnoreQueryFilters(),
        // but this DB constraint is the safety net against race conditions.
        builder.HasIndex(e => e.ReferralCode)
            .IsUnique()
            .HasDatabaseName("UQ_AffiliateAccounts_ReferralCode_Global");

        builder.HasIndex(e => e.TenantId)
            .HasDatabaseName("IX_AffiliateAccounts_TenantId");

        // Properties
        builder.Property(e => e.ReferralCode)
            .IsRequired()
            .HasMaxLength(12)
            .UsePropertyAccessMode(PropertyAccessMode.PreferFieldDuringConstruction);

        builder.Property(e => e.DisplayName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Status)
            .IsRequired();

        builder.Property(e => e.SuspensionReason)
            .HasMaxLength(500);

        builder.Property(e => e.ClosureReason)
            .HasMaxLength(500);

        // CHECK: status is valid enum value
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_AffiliateAccounts_Status",
            "\"Status\" IN (0, 1, 2, 3)"));

        // AF-10: Decoupled from POS User — UserId now logically relates to AffiliateUser.Id
        // Removed the hard database constraint FK_AffiliateAccounts_Users_UserId
    }
}
