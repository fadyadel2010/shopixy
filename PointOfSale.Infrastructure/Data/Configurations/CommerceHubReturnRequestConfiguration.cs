using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CommerceHubReturnRequestConfiguration : IEntityTypeConfiguration<CommerceHubReturnRequest>
{
    public void Configure(EntityTypeBuilder<CommerceHubReturnRequest> builder)
    {
        builder.ToTable("CommerceHubReturnRequests", table =>
        {
            table.HasCheckConstraint(
                "CK_CommerceHubReturnRequests_RequestedQuantity_Positive",
                "\"RequestedQuantity\" > 0");
            table.HasCheckConstraint(
                "CK_CommerceHubReturnRequests_ReceivedResellableQuantity_NonNegative",
                "\"ReceivedResellableQuantity\" >= 0");
            table.HasCheckConstraint(
                "CK_CommerceHubReturnRequests_ReceivedDamagedQuantity_NonNegative",
                "\"ReceivedDamagedQuantity\" >= 0");
            table.HasCheckConstraint(
                "CK_CommerceHubReturnRequests_ReceivedQuantity_Within_Requested",
                "\"ReceivedResellableQuantity\" + \"ReceivedDamagedQuantity\" <= \"RequestedQuantity\"");
        });

        builder.HasKey(returnRequest => returnRequest.Id);

        builder.Property(returnRequest => returnRequest.Status)
            .IsRequired();

        builder.Property(returnRequest => returnRequest.TrackingNumber)
            .HasMaxLength(200);

        builder.Property(returnRequest => returnRequest.CourierName)
            .HasMaxLength(200);

        builder.Property(returnRequest => returnRequest.Notes)
            .HasMaxLength(1000);

        builder.HasIndex(returnRequest => returnRequest.RefundId)
            .IsUnique()
            .HasFilter("\"IsDeleted\" = FALSE")
            .HasDatabaseName("IX_CommerceHubReturnRequests_RefundId");

        builder.HasIndex(returnRequest => new { returnRequest.CustomerId, returnRequest.Status, returnRequest.CreatedAt })
            .HasDatabaseName("IX_CommerceHubReturnRequests_CustomerId_Status_CreatedAt");

        builder.HasIndex(returnRequest => new { returnRequest.VendorId, returnRequest.Status, returnRequest.CreatedAt })
            .HasDatabaseName("IX_CommerceHubReturnRequests_VendorId_Status_CreatedAt");

        builder.HasIndex(returnRequest => new { returnRequest.OrderItemId, returnRequest.Status, returnRequest.CreatedAt })
            .HasDatabaseName("IX_CommerceHubReturnRequests_OrderItemId_Status_CreatedAt");

        builder.HasOne(returnRequest => returnRequest.Refund)
            .WithOne(refundRequest => refundRequest.ReturnRequest)
            .HasForeignKey<CommerceHubReturnRequest>(returnRequest => returnRequest.RefundId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(returnRequest => returnRequest.OrderItem)
            .WithMany()
            .HasForeignKey(returnRequest => returnRequest.OrderItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(returnRequest => returnRequest.Customer)
            .WithMany()
            .HasForeignKey(returnRequest => returnRequest.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // SLA Tracking (Audit Improvement)
        builder.Property(returnRequest => returnRequest.EscalationReason)
            .HasMaxLength(1000);

        builder.HasIndex(returnRequest => new { returnRequest.EscalationLevel, returnRequest.Status })
            .HasFilter("\"IsDeleted\" = FALSE")
            .HasDatabaseName("IX_CommerceHubReturnRequests_EscalationLevel_Status");
    }
}
