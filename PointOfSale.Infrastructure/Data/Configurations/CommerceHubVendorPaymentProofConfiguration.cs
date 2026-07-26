using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.CommerceHubFinance;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CommerceHubVendorPaymentProofConfiguration : IEntityTypeConfiguration<CommerceHubVendorPaymentProof>
{
    public void Configure(EntityTypeBuilder<CommerceHubVendorPaymentProof> builder)
    {
        builder.ToTable("CommerceHubVendorPaymentProofs");
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.ObjectKey)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(entry => entry.OriginalFileName)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(entry => entry.ContentType)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(entry => entry.ContentHash)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(entry => entry.UploadedAtUtc)
            .IsRequired();

        builder.HasIndex(entry => entry.PaymentRequestId)
            .IsUnique()
            .HasDatabaseName("UX_CommerceHubVendorPaymentProofs_PaymentRequestId");

        builder.HasIndex(entry => entry.ObjectKey)
            .IsUnique()
            .HasDatabaseName("UX_CommerceHubVendorPaymentProofs_ObjectKey");

        builder.HasIndex(entry => entry.ContentHash)
            .IsUnique()
            .HasDatabaseName("UX_CommerceHubVendorPaymentProofs_ContentHash")
            .HasFilter($"\"ContentHash\" IS NOT NULL AND \"ContentHash\" <> '' AND \"ContentHash\" <> '{CommerceHubVendorPaymentProof.LegacyMigrationSentinel}'");
    }
}
