using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.CommerceHubFinance;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CommerceHubVendorPaymentRequestConfiguration : IEntityTypeConfiguration<CommerceHubVendorPaymentRequest>
{
    public void Configure(EntityTypeBuilder<CommerceHubVendorPaymentRequest> builder)
    {
        builder.ToTable("CommerceHubVendorPaymentRequests");
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Amount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(entry => entry.Currency)
            .HasMaxLength(10)
            .HasDefaultValue("EGP")
            .IsRequired();

        builder.Property(entry => entry.TransferReference)
            .HasMaxLength(200);

        builder.Property(entry => entry.IdempotencyKey)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(entry => entry.MethodNameSnapshot)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(entry => entry.DestinationValueSnapshot)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(entry => entry.InstructionsSnapshot)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(entry => entry.ReviewReason)
            .HasColumnType("text");

        builder.Property(entry => entry.LedgerCorrelationId)
            .HasMaxLength(200);

        builder.Property(entry => entry.LedgerIdempotencyKey)
            .HasMaxLength(200);

        builder.Property(entry => entry.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(entry => new { entry.VendorId, entry.CreatedAtUtc })
            .HasDatabaseName("IX_CommerceHubVendorPaymentRequests_VendorId_CreatedAtUtc");

        builder.HasIndex(entry => new { entry.Status, entry.CreatedAtUtc })
            .HasDatabaseName("IX_CommerceHubVendorPaymentRequests_Status_CreatedAtUtc");

        builder.HasIndex(entry => new { entry.VendorId, entry.Status, entry.CreatedAtUtc })
            .HasDatabaseName("IX_CommerceHubVendorPaymentRequests_VendorId_Status_CreatedAtUtc");

        builder.HasIndex(entry => new { entry.VendorId, entry.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_CommerceHubVendorPaymentRequests_VendorId_IdempotencyKey");

        builder.HasIndex(entry => new { entry.PaymentMethodId, entry.TransferReference })
            .IsUnique()
            .HasDatabaseName("UX_CommerceHubVendorPaymentRequests_Live_Method_TransferReference")
            .HasFilter("\"TransferReference\" IS NOT NULL AND \"TransferReference\" <> '' AND \"Status\" <> 2");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(entry => entry.VendorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(entry => entry.PaymentMethod)
            .WithMany()
            .HasForeignKey(entry => entry.PaymentMethodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(entry => entry.Proof)
            .WithOne(proof => proof.PaymentRequest)
            .HasForeignKey<CommerceHubVendorPaymentProof>(proof => proof.PaymentRequestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_CommerceHubVendorPaymentRequests_Amount",
                "\"Amount\" > 0");

            table.HasCheckConstraint(
                "CK_CommerceHubVendorPaymentRequests_Currency_EGP",
                "\"Currency\" = 'EGP'");

            table.HasCheckConstraint(
                "CK_CommerceHubVendorPaymentRequests_Status",
                "\"Status\" IN (0, 1, 2)");

            table.HasCheckConstraint(
                "CK_CommerceHubVendorPaymentRequests_Approved_TransferReference",
                "\"Status\" <> 1 OR NULLIF(btrim(\"TransferReference\"), '') IS NOT NULL");
        });
    }
}
