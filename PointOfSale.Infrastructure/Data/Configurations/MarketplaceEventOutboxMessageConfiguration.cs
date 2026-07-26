using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class MarketplaceEventOutboxMessageConfiguration : IEntityTypeConfiguration<MarketplaceEventOutboxMessage>
{
    public void Configure(EntityTypeBuilder<MarketplaceEventOutboxMessage> builder)
    {
        builder.ToTable("MarketplaceEventOutboxMessages", table =>
        {
            table.HasCheckConstraint(
                "CK_MarketplaceEventOutbox_TenantId_Required",
                "\"TenantId\" IS NOT NULL AND \"TenantId\" <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint(
                "CK_MarketplaceEventOutbox_AggregateRootId_NotEmpty",
                "\"AggregateRootId\" IS NOT NULL AND \"AggregateRootId\" <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint(
                "CK_MarketplaceEventOutbox_Version_Positive",
                "\"Version\" > 0");
        });

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

        builder.HasIndex(x => x.EventId)
            .IsUnique()
            .HasDatabaseName("IX_MarketplaceEventOutbox_EventId");

        // ClaimedUntilUtc is included in index columns for efficient claim checking
        builder.HasIndex(x => new { x.PublishedAtUtc, x.IsFailed, x.NextRetryAtUtc, x.ClaimedUntilUtc })
            .HasDatabaseName("IX_MarketplaceEventOutbox_ProcessingStatus")
            .HasFilter("\"PublishedAtUtc\" IS NULL AND \"IsFailed\" = false");

        builder.HasIndex(x => x.OccurredAtUtc)
            .HasDatabaseName("IX_MarketplaceEventOutbox_OccurredAtUtc");

        builder.HasIndex(x => new { x.TenantId, x.EntityId })
            .HasDatabaseName("IX_MarketplaceEventOutbox_TenantId_EntityId");

        builder.HasIndex(x => new { x.TenantId, x.AggregateRootId })
            .HasDatabaseName("IX_MarketplaceEventOutbox_TenantId_AggregateRootId");

        // S38C: Add filter to match SQL migration and prevent model drift
        builder.HasIndex(x => new { x.TenantId, x.AggregateRootId, x.PublishedAtUtc, x.Version, x.OccurredAtUtc, x.CreatedAt, x.Id })
            .HasDatabaseName("IX_MarketplaceEventOutbox_AggregateOrdering")
            .HasFilter("\"PublishedAtUtc\" IS NULL AND \"IsFailed\" = false");

        // Failed unpublished earlier events remain ordering blockers, so the
        // blocker lookup needs an index that deliberately includes failed rows.
        builder.HasIndex(x => new { x.TenantId, x.AggregateRootId, x.Version, x.OccurredAtUtc, x.CreatedAt, x.Id })
            .HasDatabaseName("IX_MarketplaceEventOutbox_AggregateOrderingBlockers")
            .HasFilter("\"PublishedAtUtc\" IS NULL");

        // Optimistic concurrency using PostgreSQL's xmin system column
        builder.Property(e => e.RowVersion)
            .IsRowVersion();
    }
}
