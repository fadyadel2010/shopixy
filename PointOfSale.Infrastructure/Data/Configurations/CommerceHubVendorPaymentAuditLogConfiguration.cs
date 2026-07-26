using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.CommerceHubFinance;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CommerceHubVendorPaymentAuditLogConfiguration : IEntityTypeConfiguration<CommerceHubVendorPaymentAuditLog>
{
    public void Configure(EntityTypeBuilder<CommerceHubVendorPaymentAuditLog> builder)
    {
        builder.ToTable("CommerceHubVendorPaymentAuditLogs");
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Amount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(entry => entry.Currency)
            .HasMaxLength(10)
            .HasDefaultValue("EGP")
            .IsRequired();

        builder.Property(entry => entry.Reason)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(entry => entry.TimestampUtc)
            .IsRequired();

        builder.HasIndex(entry => new { entry.PaymentRequestId, entry.TimestampUtc })
            .HasDatabaseName("IX_CommerceHubVendorPaymentAuditLogs_PaymentRequestId_TimestampUtc");

        builder.HasIndex(entry => new { entry.AdminId, entry.TimestampUtc })
            .HasDatabaseName("IX_CommerceHubVendorPaymentAuditLogs_AdminId_TimestampUtc");

        builder.HasOne(entry => entry.PaymentRequest)
            .WithMany()
            .HasForeignKey(entry => entry.PaymentRequestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_CommerceHubVendorPaymentAuditLogs_Amount",
                "\"Amount\" > 0");

            table.HasCheckConstraint(
                "CK_CommerceHubVendorPaymentAuditLogs_Action",
                "\"Action\" IN (0, 1)");
        });
    }
}
