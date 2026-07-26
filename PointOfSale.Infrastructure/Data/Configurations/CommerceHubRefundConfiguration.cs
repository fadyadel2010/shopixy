using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CommerceHubRefundConfiguration : IEntityTypeConfiguration<CommerceHubRefund>
{
    public void Configure(EntityTypeBuilder<CommerceHubRefund> builder)
    {
        builder.HasKey(refund => refund.Id);

        builder.Property(refund => refund.Amount)
            .HasPrecision(18, 2);

        builder.Property(refund => refund.CommissionReversalAmount)
            .HasPrecision(18, 2);

        builder.Property(refund => refund.VendorEarningReversalAmount)
            .HasPrecision(18, 2);

        builder.Property(refund => refund.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(refund => refund.Reference)
            .HasMaxLength(200);

        builder.Property(refund => refund.GatewayStatus)
            .HasMaxLength(100);

        builder.Property(refund => refund.FailureReason)
            .HasMaxLength(2000);

        builder.HasIndex(refund => refund.RefundRequestId)
            .IsUnique()
            .HasFilter("\"RefundRequestId\" IS NOT NULL")
            .HasDatabaseName("IX_CommerceHubRefunds_RefundRequestId");

        builder.HasIndex(refund => refund.OrderItemId)
            .HasDatabaseName("IX_CommerceHubRefunds_OrderItemId");
    }
}
