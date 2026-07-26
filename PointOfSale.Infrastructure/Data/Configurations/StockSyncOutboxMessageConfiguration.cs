using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class StockSyncOutboxMessageConfiguration : IEntityTypeConfiguration<StockSyncOutboxMessage>
{
    public void Configure(EntityTypeBuilder<StockSyncOutboxMessage> builder)
    {
        builder.ToTable("StockSyncOutboxMessages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.EventType)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.ClrType)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(x => x.PayloadJson)
            .IsRequired();

        builder.Property(x => x.CorrelationId)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.LastError)
            .HasMaxLength(2000);

        builder.Property(x => x.EventVersion)
            .HasDefaultValue(1);

        builder.ToTable("StockSyncOutboxMessages", table =>
        {
            table.HasCheckConstraint(
                "CK_StockSyncOutbox_EventVersion_Positive",
                "\"EventVersion\" > 0");
            table.HasCheckConstraint(
                "CK_StockSyncOutbox_WarehouseId_NotEmpty",
                "\"WarehouseId\" <> '00000000-0000-0000-0000-000000000000'");
        });

        // Warehouse-scoped EventId uniqueness: same EventId can appear across different warehouses
        builder.HasIndex(x => new { x.TenantId, x.WarehouseId, x.EventId })
            .IsUnique()
            .HasDatabaseName("IX_StockSyncOutbox_Warehouse_EventId")
            .HasFilter("\"IsDeleted\" = false");

        // Filter is applied provider-specifically in ApplicationDbContext.OnModelCreating
        // ClaimedUntilUtc is included in index columns for efficient claim checking
        builder.HasIndex(x => new { x.PublishedAtUtc, x.IsFailed, x.NextRetryAtUtc, x.ClaimedUntilUtc })
            .HasDatabaseName("IX_StockSyncOutbox_ProcessingStatus");

        builder.HasIndex(x => x.OccurredAtUtc)
            .HasDatabaseName("IX_StockSyncOutbox_OccurredAtUtc");

        builder.HasIndex(x => new { x.TenantId, x.VariantId })
            .HasDatabaseName("IX_StockSyncOutbox_TenantId_VariantId");

        builder.HasIndex(x => new { x.TenantId, x.VariantId, x.WarehouseId, x.EventVersion })
            .IsUnique()
            .HasDatabaseName("UX_StockSyncOutboxMessages_Tenant_Variant_Warehouse_EventVersion")
            .HasFilter("\"IsDeleted\" = false");

        // Version-aware canonical ordering for warehouse-scoped stock truth.
        builder.HasIndex(x => new { x.TenantId, x.VariantId, x.WarehouseId, x.PublishedAtUtc, x.EventVersion, x.StockUpdatedAtUtc, x.OccurredAtUtc, x.CreatedAt, x.Id })
            .HasDatabaseName("IX_StockSyncOutbox_VariantOrdering")
            .HasFilter("\"PublishedAtUtc\" IS NULL AND \"IsFailed\" = false");

        builder.HasIndex(x => new { x.EventVersion, x.StockUpdatedAtUtc, x.OccurredAtUtc, x.CreatedAt, x.Id })
            .HasDatabaseName("IX_StockSyncOutbox_GlobalPendingOrdering")
            .HasFilter("\"PublishedAtUtc\" IS NULL AND \"IsFailed\" = false AND \"IsDeleted\" = false");

        // Failed unpublished earlier stock events remain ordering blockers, so
        // the blocker lookup needs an index that deliberately includes failed rows.
        builder.HasIndex(x => new { x.TenantId, x.VariantId, x.WarehouseId, x.EventVersion, x.StockUpdatedAtUtc, x.OccurredAtUtc, x.CreatedAt, x.Id })
            .HasDatabaseName("IX_StockSyncOutbox_VariantOrderingBlockers")
            .HasFilter("\"PublishedAtUtc\" IS NULL");

    }
}
