using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.Affiliate;

namespace PointOfSale.Infrastructure.Data.Configurations.Affiliate;

/// <summary>
/// AF-38C: EF Core configuration for the append-only AffiliateBalanceLedger.
/// This entity does NOT inherit from BaseEntity — it has no IsDeleted, no UpdatedAt.
/// </summary>
public class AffiliateBalanceLedgerConfiguration : IEntityTypeConfiguration<AffiliateBalanceLedger>
{
    public void Configure(EntityTypeBuilder<AffiliateBalanceLedger> builder)
    {
        builder.ToTable("AffiliateBalanceLedger");
        builder.HasKey(e => e.Id);

        // Unique constraint on IdempotencyKey (global uniqueness)
        builder.HasIndex(e => e.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("UQ_AffiliateBalanceLedger_Idempotency");

        // Index for affiliate balance queries
        builder.HasIndex(e => e.AffiliateAccountId)
            .HasDatabaseName("IX_AffiliateBalanceLedger_AffiliateAccountId");

        // Index for tenant + time queries
        builder.HasIndex(e => new { e.StoreTenantId, e.RecordedAtUtc })
            .IsDescending(false, true)
            .HasDatabaseName("IX_AffiliateBalanceLedger_StoreTenantRecorded");

        // Index for reference lookups (withdrawal requests)
        builder.HasIndex(e => e.ReferenceId)
            .HasDatabaseName("IX_AffiliateBalanceLedger_ReferenceId")
            .HasFilter("\"ReferenceId\" IS NOT NULL");

        // Properties
        builder.Property(e => e.Amount)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(e => e.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(e => e.EntryType)
            .IsRequired();

        builder.Property(e => e.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.RecordedAtUtc)
            .IsRequired();

        builder.Property(e => e.Description)
            .HasColumnType("text");

        // CHECK: Amount cannot be zero
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_AffiliateBalanceLedger_Amount",
            "\"Amount\" != 0"));

        // CHECK: EntryType valid
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_AffiliateBalanceLedger_EntryType",
            "\"EntryType\" IN (1, 2, 3, 4)"));

        // CHECK: Withdrawal must be negative
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_AffiliateBalanceLedger_WithdrawalNegative",
            "(\"EntryType\" != 1 OR \"Amount\" < 0)"));

        // CHECK: IdempotencyKey cannot be blank or whitespace-only
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_AffiliateBalanceLedger_IdempotencyKey_NotBlank",
            "btrim(\"IdempotencyKey\") <> ''"));

        // FK: AffiliateAccount
        builder.HasOne<AffiliateAccount>()
            .WithMany()
            .HasForeignKey(e => e.AffiliateAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
