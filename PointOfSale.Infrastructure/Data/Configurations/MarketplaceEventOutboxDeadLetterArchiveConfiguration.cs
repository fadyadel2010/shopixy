using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class MarketplaceEventOutboxDeadLetterArchiveConfiguration : IEntityTypeConfiguration<MarketplaceEventOutboxDeadLetterArchive>
{
    public void Configure(EntityTypeBuilder<MarketplaceEventOutboxDeadLetterArchive> builder)
    {
        builder.ToTable("MarketplaceEventOutboxDeadLetterArchives", table =>
        {
            table.HasCheckConstraint(
                "CK_MarketplaceEventOutboxDeadLetterArchive_OriginalId_Required",
                "\"OriginalOutboxMessageId\" <> '00000000-0000-0000-0000-000000000000'");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.EventType)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.ClrType)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(e => e.PayloadJson)
            .IsRequired();

        builder.Property(e => e.CorrelationId)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.LastError)
            .HasMaxLength(2000);

        builder.Property(e => e.ArchiveReason)
            .IsRequired()
            .HasMaxLength(200);

        builder.HasIndex(e => e.OriginalOutboxMessageId)
            .IsUnique()
            .HasDatabaseName("UQ_MarketplaceEventOutboxDeadLetterArchives_OriginalId");

        builder.HasIndex(e => new { e.TenantId, e.AggregateRootId })
            .HasDatabaseName("IX_MarketplaceEventOutboxDeadLetterArchives_Tenant_Aggregate");

        builder.HasIndex(e => e.ArchivedAtUtc)
            .HasDatabaseName("IX_MarketplaceEventOutboxDeadLetterArchives_ArchivedAtUtc");
    }
}
