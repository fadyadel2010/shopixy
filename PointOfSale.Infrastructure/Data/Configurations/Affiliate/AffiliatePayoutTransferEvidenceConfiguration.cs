using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.Affiliate;

namespace PointOfSale.Infrastructure.Data.Configurations.Affiliate;

public class AffiliatePayoutTransferEvidenceConfiguration : IEntityTypeConfiguration<AffiliatePayoutTransferEvidence>
{
    public void Configure(EntityTypeBuilder<AffiliatePayoutTransferEvidence> builder)
    {
        builder.ToTable("AffiliatePayoutTransferEvidence", table =>
        {
            table.HasCheckConstraint(
                "CK_AffiliatePayoutTransferEvidence_ExpectedAmount_Positive",
                "\"ExpectedAmount\" > 0");
            table.HasCheckConstraint(
                "CK_AffiliatePayoutTransferEvidence_AllocatedAmount_Valid",
                "\"AllocatedAmount\" >= 0 AND \"AllocatedAmount\" <= \"ExpectedAmount\"");
            table.HasCheckConstraint(
                "CK_AffiliatePayoutTransferEvidence_Tenant_Required",
                "\"StoreTenantId\" <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint(
                "CK_AffiliatePayoutTransferEvidence_RailNamespace_NotBlank",
                "btrim(\"RailNamespace\", E' \\t\\n\\r') <> ''");
            table.HasCheckConstraint(
                "CK_AffiliatePayoutTransferEvidence_PaymentReference_NotBlank",
                "btrim(\"PaymentReference\", E' \\t\\n\\r') <> ''");
            table.HasCheckConstraint(
                "CK_AffiliatePayoutTransferEvidence_NormalizedPaymentReference_NotBlank",
                "btrim(\"NormalizedPaymentReference\", E' \\t\\n\\r') <> ''");
            table.HasCheckConstraint(
                "CK_AffiliatePayoutTransferEvidence_Currency_NotBlank",
                "btrim(\"Currency\", E' \\t\\n\\r') <> ''");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.RailNamespace)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(e => e.PaymentReference)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.NormalizedPaymentReference)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.ExpectedAmount)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(e => e.AllocatedAmount)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(e => e.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(e => e.VerifiedAtUtc)
            .IsRequired();

        builder.Property(e => e.VerificationSource)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(e => e.VerificationFingerprint)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(e => new { e.RailNamespace, e.NormalizedPaymentReference })
            .IsUnique()
            .HasDatabaseName("UQ_AffiliatePayoutTransferEvidence_Rail_Reference");

        builder.HasIndex(e => new { e.StoreTenantId, e.CreatedAt })
            .HasDatabaseName("IX_AffiliatePayoutTransferEvidence_Tenant_CreatedAt");
    }
}
