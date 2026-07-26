using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;

namespace PointOfSale.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for OutboxMessage.
/// Phase: Abandoned Cart Hardening - Concurrency Control
/// </summary>
public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    private static readonly string CriticalEventTypePredicate =
        "(" + string.Join(" OR ", IntegrationEventAuthority.CriticalEventNameFragments
            .Select(fragment => $"lower(\"EventType\") LIKE '%{fragment.ToLowerInvariant()}%'")) + ")";

    private static readonly string NonAuthoritativeEventTypePredicate =
        string.Join(" AND ", IntegrationEventAuthority.NonAuthoritativeEventTypeNames
            .Select(eventTypeName => $"lower(\"EventType\") NOT LIKE '%{eventTypeName.ToLowerInvariant()}%'"));

    private static readonly string AuthoritativeCriticalEventPredicate =
        $"({CriticalEventTypePredicate} AND {NonAuthoritativeEventTypePredicate})";

    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages", table =>
        {
            table.HasCheckConstraint(
                "CK_OutboxMessages_CriticalEvents_TenantId_Required",
                $"(\"TenantId\" IS NOT NULL AND \"TenantId\" <> '00000000-0000-0000-0000-000000000000') OR NOT {AuthoritativeCriticalEventPredicate}");
            table.HasCheckConstraint(
                "CK_OutboxMessages_CriticalEvents_AggregateId_Required",
                $"(\"AggregateId\" IS NOT NULL AND \"AggregateId\" <> '00000000-0000-0000-0000-000000000000') OR NOT {AuthoritativeCriticalEventPredicate}");
            table.HasCheckConstraint(
                "CK_OutboxMessages_CriticalEvents_EventVersion_Required",
                $"\"EventVersion\" > 0 OR NOT {AuthoritativeCriticalEventPredicate}");
        });
        
        builder.HasKey(x => x.Id);
        
        builder.Property(x => x.EventType)
            .IsRequired()
            .HasMaxLength(500);
        
        builder.Property(x => x.EventPayload)
            .IsRequired();
        
        builder.Property(x => x.LastErrorMessage)
            .HasMaxLength(2000);

        builder.Property(x => x.OperatorReconciliationReason)
            .HasMaxLength(2000);

        builder.Property(x => x.RequiresOperatorReconciliation)
            .HasDefaultValue(false);

        builder.Property(x => x.EventVersion)
            .HasDefaultValue(0);
        
        // Optimistic concurrency using PostgreSQL's xmin system column
        // Prevents TOCTOU race conditions in concurrent job processing
        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
        
        // Index for processor query (pending messages ordered by creation time)
        builder.HasIndex(x => new { x.CreatedAtUtc, x.RetryCount })
            .HasDatabaseName("IX_OutboxMessages_ProcessingStatus")
            .HasFilter("\"ProcessedAtUtc\" IS NULL");
        
        // Index for cleanup query (old processed messages)
        builder.HasIndex(x => x.ProcessedAtUtc)
            .HasDatabaseName("IX_OutboxMessages_ProcessedAtUtc")
            .HasFilter("\"ProcessedAtUtc\" IS NOT NULL");
        
        // Index for tenant-scoped queries
        builder.HasIndex(x => new { x.TenantId, x.ProcessedAtUtc })
            .HasDatabaseName("IX_OutboxMessages_TenantId_ProcessedAtUtc");

        builder.HasIndex(x => new { x.TenantId, x.AggregateId, x.EventVersion, x.CreatedAtUtc, x.Id })
            .HasDatabaseName("IX_OutboxMessages_CriticalAggregateOrdering")
            .HasFilter("\"ProcessedAtUtc\" IS NULL AND \"TenantId\" IS NOT NULL AND \"AggregateId\" IS NOT NULL");

        builder.HasIndex(x => new { x.RequiresOperatorReconciliation, x.ProcessedAtUtc, x.CreatedAtUtc })
            .HasDatabaseName("IX_OutboxMessages_OperatorReconciliation")
            .HasFilter("\"RequiresOperatorReconciliation\" = true AND \"ProcessedAtUtc\" IS NULL");
    }
}
