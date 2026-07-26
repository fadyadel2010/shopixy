using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class ProcessedEventConfiguration : IEntityTypeConfiguration<ProcessedEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedEvent> builder)
    {
        builder.ToTable("ProcessedEvents");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ConsumerName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.EventType)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.CorrelationId)
            .IsRequired()
            .HasMaxLength(200);

        builder.HasIndex(x => new { x.EventId, x.ConsumerName, x.EventType })
            .IsUnique()
            .HasDatabaseName("IX_ProcessedEvents_Idempotency")
            .HasFilter("\"IsDeleted\" = false");

        builder.HasIndex(x => x.ProcessedAtUtc)
            .HasDatabaseName("IX_ProcessedEvents_ProcessedAtUtc");

        builder.HasIndex(x => new { x.TenantId, x.EntityId })
            .HasDatabaseName("IX_ProcessedEvents_TenantId_EntityId");
    }
}
