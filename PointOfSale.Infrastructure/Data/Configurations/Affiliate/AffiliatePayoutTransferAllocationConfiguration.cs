using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.Affiliate;

namespace PointOfSale.Infrastructure.Data.Configurations.Affiliate;

public class AffiliatePayoutTransferAllocationConfiguration : IEntityTypeConfiguration<AffiliatePayoutTransferAllocation>
{
    public void Configure(EntityTypeBuilder<AffiliatePayoutTransferAllocation> builder)
    {
        builder.ToTable("AffiliatePayoutTransferAllocations", table =>
        {
            table.HasCheckConstraint(
                "CK_AffiliatePayoutTransferAllocation_Amount_Positive",
                "\"Amount\" > 0");
            table.HasCheckConstraint(
                "CK_AffiliatePayoutTransferAllocation_Tenant_Required",
                "\"StoreTenantId\" <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint(
                "CK_AffiliatePayoutTransferAllocation_Affiliate_Required",
                "\"AffiliateAccountId\" <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint(
                "CK_AffiliatePayoutTransferAllocation_Reference_Required",
                "\"ReferenceId\" <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint(
                "CK_AffiliatePayoutTransferAllocation_IdempotencyKey_NotBlank",
                "btrim(\"IdempotencyKey\") <> ''");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Amount)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(e => e.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(e => e.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(255);

        builder.HasIndex(e => e.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("UQ_AffiliatePayoutTransferAllocation_Idempotency");

        builder.HasIndex(e => e.EvidenceId)
            .HasDatabaseName("IX_AffiliatePayoutTransferAllocation_EvidenceId");

        builder.HasIndex(e => new { e.StoreTenantId, e.CreatedAt })
            .HasDatabaseName("IX_AffiliatePayoutTransferAllocation_Tenant_CreatedAt");

        builder.HasOne(e => e.Evidence)
            .WithMany()
            .HasForeignKey(e => e.EvidenceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
