using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for PaymentCancellationOutboxMessage.
/// </summary>
public class PaymentCancellationOutboxMessageConfiguration : IEntityTypeConfiguration<PaymentCancellationOutboxMessage>
{
    public void Configure(EntityTypeBuilder<PaymentCancellationOutboxMessage> builder)
    {
        builder.ToTable("PaymentCancellationOutboxMessages");
        
        builder.HasKey(x => x.Id);
        
        builder.Property(x => x.TenantId)
            .IsRequired();
        
        builder.Property(x => x.OrderId)
            .IsRequired();
        
        builder.Property(x => x.Reason)
            .IsRequired()
            .HasMaxLength(500);
        
        builder.Property(x => x.Actor)
            .IsRequired()
            .HasMaxLength(100);
        
        builder.Property(x => x.LastError)
            .HasMaxLength(2000);
        
        builder.Property(x => x.CorrelationId)
            .HasMaxLength(100);

        builder.Property(e => e.RowVersion)
            .IsRowVersion();
        
        // Index for efficient polling by background worker
        // Includes CreatedAt for ordering in queries that filter by ProcessedAtUtc/IsFailed/NextRetryAtUtc
        builder.HasIndex(x => new { x.ProcessedAtUtc, x.IsFailed, x.NextRetryAtUtc, x.ClaimedUntilUtc })
            .IncludeProperties(x => x.CreatedAt)
            .HasDatabaseName("IX_PaymentCancellationOutbox_Processing");
        
        // Index for tenant isolation
        builder.HasIndex(x => x.TenantId)
            .HasDatabaseName("IX_PaymentCancellationOutbox_TenantId");
        
        // Index for order lookup
        builder.HasIndex(x => x.OrderId)
            .HasDatabaseName("IX_PaymentCancellationOutbox_OrderId");
    }
}
