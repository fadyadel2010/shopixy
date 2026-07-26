using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class MarketplaceProjectionOutboxMessageConfiguration : IEntityTypeConfiguration<MarketplaceProjectionOutboxMessage>
{
    public void Configure(EntityTypeBuilder<MarketplaceProjectionOutboxMessage> builder)
    {
        builder.ToTable("MarketplaceProjectionOutboxMessages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ConsumerName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.EventType)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.ClrType)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(x => x.CorrelationId)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.PayloadJson)
            .IsRequired();

        builder.Property(x => x.LastError)
            .HasMaxLength(2000);

        builder.Property(x => x.RowVersion)
            .IsRowVersion();

        builder.HasIndex(x => new { x.EventId, x.ConsumerName, x.EventType })
            .IsUnique()
            .HasDatabaseName("IX_MarketplaceProjectionOutbox_Idempotency")
            .HasFilter("\"IsDeleted\" = false");

        builder.HasIndex(x => x.ProcessedAtUtc)
            .HasDatabaseName("IX_MarketplaceProjectionOutbox_ProcessedAtUtc");

        builder.HasIndex(x => new { x.TenantId, x.EntityId })
            .HasDatabaseName("IX_MarketplaceProjectionOutbox_TenantId_EntityId");

        builder.HasIndex(x => new { x.TenantId, x.ConsumerName, x.ProcessedAtUtc, x.NextRetryAtUtc, x.ClaimedUntilUtc })
            .HasDatabaseName("IX_MarketplaceProjectionOutbox_ProcessingStatus")
            .HasFilter("\"ProcessedAtUtc\" IS NULL AND \"IsFailed\" = false");
    }
}
