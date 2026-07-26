using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class InventoryEventInboxConfiguration : IEntityTypeConfiguration<InventoryEventInbox>
{
    public void Configure(EntityTypeBuilder<InventoryEventInbox> builder)
    {
        builder.ToTable("InventoryEventInbox", table =>
        {
            table.HasCheckConstraint(
                "CK_InventoryEventInbox_ReplayCount_NonNegative",
                "\"ReplayCount\" >= 0");
        });

        builder.HasKey(inbox => inbox.Id);

        builder.Property(inbox => inbox.PayloadHash)
            .HasMaxLength(128);

        builder.Property(inbox => inbox.CorrelationId)
            .HasMaxLength(100);

        builder.Property(inbox => inbox.IdempotencyKey)
            .HasMaxLength(200);

        builder.Property(inbox => inbox.Source)
            .HasMaxLength(100);

        builder.HasIndex(inbox => new { inbox.TenantId, inbox.EventId })
            .IsUnique()
            .HasDatabaseName("UX_InventoryEventInbox_TenantId_EventId");

        builder.HasIndex(inbox => new { inbox.TenantId, inbox.ReferenceLineId, inbox.EventType })
            .HasDatabaseName("IX_InventoryEventInbox_TenantId_ReferenceLineId_EventType");
    }
}
