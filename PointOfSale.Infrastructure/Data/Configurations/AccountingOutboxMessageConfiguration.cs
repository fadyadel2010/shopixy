using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.Accounting;

namespace PointOfSale.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for AccountingOutboxMessage.
/// </summary>
public class AccountingOutboxMessageConfiguration : IEntityTypeConfiguration<AccountingOutboxMessage>
{
    public void Configure(EntityTypeBuilder<AccountingOutboxMessage> builder)
    {
        builder.ToTable("AccountingOutboxMessages", table =>
        {
            table.HasCheckConstraint(
                "CK_AccountingOutbox_OrderFinancialEvents_AggregateVersion",
                "\"EventType\" NOT IN ('OnlinePayment', 'CodDelivered', 'WalletApproved', 'EcommerceRefund') OR " +
                "(\"AggregateVersion\" IS NOT NULL AND \"AggregateVersion\" > 0)");
        });
        
        builder.HasKey(x => x.Id);
        
        builder.Property(x => x.EventType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.SourceType)
            .IsRequired()
            .HasMaxLength(50);
        
        builder.Property(x => x.EntityTypeName)
            .HasMaxLength(255);
        
        builder.Property(x => x.PayloadJson)
            .IsRequired();
        
        builder.Property(x => x.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(x => x.AggregateVersion);
        
        builder.Property(x => x.LastError)
            .HasMaxLength(2000);
        
        // Optimistic concurrency using PostgreSQL's xmin system column
        // This tells EF Core to use the xmin column for concurrency checks
        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
        
        // Unique constraint on idempotency key per tenant
        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("IX_AccountingOutbox_TenantId_IdempotencyKey");

        // Recovery/reconciliation services use source type to find the exact
        // accounting authority rows for a business mutation.
        builder.HasIndex(x => new { x.TenantId, x.SourceType, x.BusinessEntityId })
            .HasDatabaseName("IX_AccountingOutbox_Tenant_SourceType_Entity");
        
        // Index for processor query (pending messages)
        builder.HasIndex(x => new { x.ProcessedAtUtc, x.IsFailed, x.NextRetryAtUtc, x.ClaimedUntilUtc })
            .HasDatabaseName("IX_AccountingOutbox_ProcessingStatus")
            .HasFilter("\"ProcessedAtUtc\" IS NULL AND \"IsFailed\" = false");
        
        // Index for monitoring (failed messages)
        builder.HasIndex(x => new { x.TenantId, x.IsFailed })
            .HasDatabaseName("IX_AccountingOutbox_Failed")
            .HasFilter("\"IsFailed\" = true");
        
        // Index for deterministic per-aggregate ordering
        builder.HasIndex(x => new { x.TenantId, x.BusinessEntityId, x.AggregateVersion, x.OccurredAtUtc, x.CreatedAt, x.Id })
            .HasDatabaseName("IX_AccountingOutbox_AggregateVersionOrdering")
            .HasFilter("\"ProcessedAtUtc\" IS NULL AND \"IsFailed\" = false");

        // Index for ordering blockers. Failed critical financial-truth messages
        // still block later aggregate versions until explicitly retried/reconciled.
        builder.HasIndex(x => new
            {
                x.TenantId,
                x.BusinessEntityId,
                x.AggregateVersion,
                x.OccurredAtUtc,
                x.CreatedAt,
                x.Id,
                x.ProcessedAtUtc,
                x.IsFailed,
                x.EventType
            })
            .HasDatabaseName("IX_AccountingOutbox_AggregateVersionBlockers")
            .HasFilter("\"ProcessedAtUtc\" IS NULL");

        // Index for chronological fallback processing
        builder.HasIndex(x => x.OccurredAtUtc)
            .HasDatabaseName("IX_AccountingOutbox_OccurredAtUtc");
    }
}
