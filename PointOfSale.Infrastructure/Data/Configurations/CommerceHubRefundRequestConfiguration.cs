using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CommerceHubRefundRequestConfiguration : IEntityTypeConfiguration<CommerceHubRefundRequest>
{
    public void Configure(EntityTypeBuilder<CommerceHubRefundRequest> builder)
    {
        builder.HasKey(refundRequest => refundRequest.Id);

        builder.Property(refundRequest => refundRequest.Quantity)
            .IsRequired();

        builder.Property(refundRequest => refundRequest.UnitPriceSnapshot)
            .HasPrecision(18, 2);

        builder.Property(refundRequest => refundRequest.TotalAmount)
            .HasPrecision(18, 2);

        builder.Property(refundRequest => refundRequest.ReasonCode)
            .IsRequired();

        builder.Property(refundRequest => refundRequest.ReasonText)
            .HasMaxLength(1000);

        builder.Property(refundRequest => refundRequest.Reason)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(refundRequest => refundRequest.PayoutAccountSnapshot)
            .HasMaxLength(500);

        builder.Property(refundRequest => refundRequest.PayoutAccountMaskedSnapshot)
            .HasMaxLength(32);

        builder.Property(refundRequest => refundRequest.PayoutAccountHolderNameSnapshot)
            .HasMaxLength(200);

        builder.Property(refundRequest => refundRequest.RefundExecutionTrigger)
            .IsRequired();

        builder.Property(refundRequest => refundRequest.IdempotencyKey)
            .HasMaxLength(100);

        builder.Property(refundRequest => refundRequest.AdminNote)
            .HasMaxLength(1000);

        builder.Property(refundRequest => refundRequest.FailureReason)
            .HasMaxLength(2000);

        builder.HasIndex(refundRequest => new { refundRequest.CustomerId, refundRequest.CreatedAt })
            .HasDatabaseName("IX_CommerceHubRefundRequests_CustomerId_CreatedAt");

        builder.HasIndex(refundRequest => new { refundRequest.CustomerId, refundRequest.Status, refundRequest.CreatedAt })
            .HasDatabaseName("IX_CommerceHubRefundRequests_CustomerId_Status_CreatedAt");

        builder.HasIndex(refundRequest => new { refundRequest.OrderItemId, refundRequest.Status, refundRequest.CreatedAt })
            .HasDatabaseName("IX_CommerceHubRefundRequests_OrderItemId_Status_CreatedAt");

        builder.HasIndex(refundRequest => new { refundRequest.VendorId, refundRequest.Status, refundRequest.CreatedAt })
            .HasDatabaseName("IX_CommerceHubRefundRequests_VendorId_Status_CreatedAt");

        builder.HasIndex(refundRequest => refundRequest.OrderId)
            .HasDatabaseName("IX_CommerceHubRefundRequests_OrderId");

        builder.HasIndex(refundRequest => new { refundRequest.Status, refundRequest.CreatedAt })
            .HasDatabaseName("IX_CommerceHubRefundRequests_Status_CreatedAt");

        builder.HasIndex(refundRequest => refundRequest.ReturnRequestId)
            .IsUnique()
            .HasFilter("\"ReturnRequestId\" IS NOT NULL")
            .HasDatabaseName("IX_CommerceHubRefundRequests_ReturnRequestId");

        builder.HasIndex(refundRequest => refundRequest.IdempotencyKey)
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL")
            .HasDatabaseName("IX_CommerceHubRefundRequests_IdempotencyKey");

        builder.HasOne(refundRequest => refundRequest.Order)
            .WithMany()
            .HasForeignKey(refundRequest => refundRequest.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(refundRequest => refundRequest.OrderItem)
            .WithMany()
            .HasForeignKey(refundRequest => refundRequest.OrderItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(refundRequest => refundRequest.Customer)
            .WithMany()
            .HasForeignKey(refundRequest => refundRequest.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(refundRequest => refundRequest.EvidenceMedia)
            .WithOne(media => media.RefundRequest)
            .HasForeignKey(media => media.RefundRequestId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
