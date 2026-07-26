using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.Affiliate;

namespace PointOfSale.Infrastructure.Data.Configurations.Affiliate;

/// <summary>
/// AF-10: EF Core configuration for AffiliateUsers table.
///
/// Constraints:
/// - UQ_AffiliateUsers_Email_Tenant: (TenantId, Email) unique
/// - UQ_AffiliateUsers_AffiliateAccount: (AffiliateAccountId) unique
/// - FK → AffiliateAccounts.Id
/// </summary>
public class AffiliateUserConfiguration : IEntityTypeConfiguration<AffiliateUser>
{
    public void Configure(EntityTypeBuilder<AffiliateUser> builder)
    {
        builder.ToTable("AffiliateUsers");
        builder.HasKey(e => e.Id);

        // UNIQUE: one login per email per tenant
        builder.HasIndex(e => new { e.TenantId, e.Email })
            .IsUnique()
            .HasDatabaseName("UQ_AffiliateUsers_Email_Tenant");

        // UNIQUE: one AffiliateUser per AffiliateAccount
        builder.HasIndex(e => e.AffiliateAccountId)
            .IsUnique()
            .HasDatabaseName("UQ_AffiliateUsers_AffiliateAccount");

        // Tenant index
        builder.HasIndex(e => e.TenantId)
            .HasDatabaseName("IX_AffiliateUsers_TenantId");

        // Properties
        builder.Property(e => e.Email)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.PasswordHash)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.EmailVerified)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.RefreshToken)
            .HasMaxLength(500);

        builder.Property(e => e.PreviousRefreshToken)
            .HasMaxLength(500);

        builder.Property(e => e.TokensValidAfterUtc);

        // FK: AffiliateAccount (1:1)
        builder.HasOne(e => e.AffiliateAccount)
            .WithOne()
            .HasForeignKey<AffiliateUser>(e => e.AffiliateAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
