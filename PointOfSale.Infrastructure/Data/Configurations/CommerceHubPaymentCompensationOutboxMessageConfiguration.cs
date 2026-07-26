using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CommerceHubPaymentCompensationOutboxMessageConfiguration
    : IEntityTypeConfiguration<CommerceHubPaymentCompensationOutboxMessage>
{
    public void Configure(EntityTypeBuilder<CommerceHubPaymentCompensationOutboxMessage> builder)
    {
        builder.ToTable("CommerceHubPaymentCompensationOutboxMessages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Currency)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(x => x.Reason)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.IdempotencyKey)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.ProviderIntentId).HasMaxLength(200);
        builder.Property(x => x.ProviderTransactionId).HasMaxLength(200);
        builder.Property(x => x.CorrelationId).HasMaxLength(100);
        builder.Property(x => x.AffectedStoreIdsJson).HasColumnType("text").IsRequired();
        builder.Property(x => x.StoreAllocationSnapshotJson).HasColumnType("text").IsRequired();
        builder.Property(x => x.RefundAllocationSnapshotJson).HasColumnType("text").IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(2000);
        builder.Property(x => x.GatewayReference).HasMaxLength(200);
        builder.Property(x => x.GatewayStatus).HasMaxLength(100);
        builder.Property(x => x.RequiresOperatorReconciliation).HasDefaultValue(false);

        builder.HasIndex(x => new { x.IsFailed, x.ProcessedAtUtc, x.NextRetryAtUtc })
            .HasDatabaseName("IX_CHPaymentCompOutbox_Processing");

        builder.HasIndex(x => new { x.RequiresOperatorReconciliation, x.ProcessedAtUtc, x.CreatedAt })
            .HasDatabaseName("IX_CHPaymentCompOutbox_OperatorReconciliation");

        builder.HasIndex(x => new
            {
                x.ProcessedAtUtc,
                x.IsFailed,
                x.NextRetryAtUtc,
                x.ProcessingLeaseExpiresAtUtc,
                x.CreatedAt,
                x.Id
            })
            .HasDatabaseName("IX_CHPaymentCompOutbox_ProcessingLease");

        builder.HasIndex(x => x.PaymentId)
            .HasDatabaseName("IX_CHPaymentCompOutbox_PaymentId");

        builder.HasIndex(x => new { x.PaymentId, x.ProcessedAtUtc, x.CreatedAt, x.Id })
            .HasDatabaseName("IX_CHPaymentCompOutbox_PaymentOrdering");

        builder.HasIndex(x => x.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("UX_CHPaymentCompOutbox_IdempotencyKey");
    }
}
