using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.Affiliate;

namespace PointOfSale.Infrastructure.Data.Configurations.Affiliate;

/// <summary>
/// EF Core configuration for the append-only AffiliateCommissionLedger.
/// This entity does NOT inherit from BaseEntity — it has no IsDeleted, no UpdatedAt.
/// </summary>
public class AffiliateCommissionLedgerConfiguration : IEntityTypeConfiguration<AffiliateCommissionLedger>
{
    public void Configure(EntityTypeBuilder<AffiliateCommissionLedger> builder)
    {
        builder.ToTable("AffiliateCommissionLedger");
        builder.HasKey(e => e.Id);

        // AUDIT FIX F-D3: Idempotency unique constraint MUST be on IdempotencyKey alone.
        // TenantId is ALWAYS NULL for global affiliates (AF-19B), and PostgreSQL treats
        // NULL ≠ NULL in unique constraints — so (TenantId, IdempotencyKey) would allow
        // unlimited duplicate entries, defeating the entire idempotency mechanism.
        // The IdempotencyKey already encodes EntryType + OrderItemId, ensuring global uniqueness.
        builder.HasIndex(e => e.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("UQ_AffiliateCommLedger_Idempotency");

        // Indexes for common queries
        builder.HasIndex(e => e.AffiliateAccountId)
            .HasDatabaseName("IX_AffiliateCommLedger_AffiliateAccountId");

        builder.HasIndex(e => e.OrderId)
            .HasDatabaseName("IX_AffiliateCommLedger_OrderId");

        builder.HasIndex(e => new { e.TenantId, e.RecordedAtUtc })
            .IsDescending(false, true)
            .HasDatabaseName("IX_AffiliateCommLedger_TenantRecorded");

        builder.HasIndex(e => e.TenantId)
            .HasDatabaseName("IX_AffiliateCommLedger_TenantId");

        // Y3 Audit Fix: composite index for reversal lookups (OrderItemId + EntryType)
        builder.HasIndex(e => new { e.OrderItemId, e.EntryType })
            .HasDatabaseName("IX_AffiliateCommLedger_OrderItemEntryType");

        // Properties
        builder.Property(e => e.BaseAmount)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(e => e.CommissionRate)
            .HasPrecision(8, 6)
            .IsRequired();

        builder.Property(e => e.CommissionAmount)
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

        // CHECK: commission > 0
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_AffiliateCommLedger_Amount",
            "\"CommissionAmount\" > 0"));

        // CHECK: base amount > 0
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_AffiliateCommLedger_BaseAmount",
            "\"BaseAmount\" > 0"));

        // CHECK: rate valid (strict: 0 < rate <= 0.50)
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_AffiliateCommLedger_Rate",
            "\"CommissionRate\" > 0 AND \"CommissionRate\" <= 0.50"));

        // CHECK: reversal/approved/paid must reference original, earned must not
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_AffiliateCommLedger_ReversalRef",
            "(\"EntryType\" IN (1, 2, 3) AND \"OriginalEntryId\" IS NOT NULL) OR (\"EntryType\" = 0 AND \"OriginalEntryId\" IS NULL)"));

        // CHECK: entry type valid (AF-38C: Payout removed)
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_AffiliateCommLedger_EntryType",
            "\"EntryType\" IN (0, 1, 2, 3)"));

        // CHECK: IdempotencyKey cannot be blank or whitespace-only
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_AffiliateCommLedger_IdempotencyKey_NotBlank",
            "btrim(\"IdempotencyKey\") <> ''"));

        // FK: Attribution (AF-38C: NOT NULL)
        builder.HasOne<AffiliateAttribution>()
            .WithMany()
            .HasForeignKey(e => e.AttributionId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // FK: AffiliateAccount
        builder.HasOne<AffiliateAccount>()
            .WithMany()
            .HasForeignKey(e => e.AffiliateAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK: Order (AF-38C: NOT NULL)
        builder.HasOne<PointOfSale.Core.Entities.Order>()
            .WithMany()
            .HasForeignKey(e => e.OrderId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // FK: OrderItem (AF-38C: NOT NULL)
        builder.HasOne<PointOfSale.Core.Entities.OrderItem>()
            .WithMany()
            .HasForeignKey(e => e.OrderItemId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // FK: self-referencing for reversals
        builder.HasOne<AffiliateCommissionLedger>()
            .WithMany()
            .HasForeignKey(e => e.OriginalEntryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
