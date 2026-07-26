using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements");

        builder.HasKey(sm => sm.Id);
        
        builder.Property(sm => sm.QuantityChange)
            .HasPrecision(18, 2);

        builder.Property(sm => sm.ReservedQuantityChange)
            .HasPrecision(18, 2);

        builder.Property(sm => sm.DamagedQuantityChange)
            .HasPrecision(18, 2);
        
        builder.Property(sm => sm.QuantityAfter)
            .HasPrecision(18, 2);

        builder.Property(sm => sm.AvailableAfter)
            .HasPrecision(18, 2);

        builder.Property(sm => sm.ReservedAfter)
            .HasPrecision(18, 2);

        builder.Property(sm => sm.DamagedAfter)
            .HasPrecision(18, 2);

        builder.Property(sm => sm.ReferenceType)
            .HasMaxLength(50);

        builder.Property(sm => sm.CorrelationId)
            .HasMaxLength(100);

        builder.Property(sm => sm.IdempotencyKey)
            .HasMaxLength(200);

        builder.Property(sm => sm.Source)
            .HasMaxLength(100);
        
        // Configure the main warehouse relationship
        builder.HasOne(sm => sm.Warehouse)
            .WithMany(w => w.StockMovements)
            .HasForeignKey(sm => sm.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        
        // Configure the ToWarehouse relationship (for transfers)
        builder.HasOne(sm => sm.ToWarehouse)
            .WithMany()
            .HasForeignKey(sm => sm.ToWarehouseId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
        
        builder.HasOne(sm => sm.Product)
            .WithMany()
            .HasForeignKey(sm => sm.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        
        builder.HasOne(sm => sm.ProductVariant)
            .WithMany()
            .HasForeignKey(sm => sm.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
        
        builder.HasIndex(sm => sm.WarehouseId);
        builder.HasIndex(sm => sm.ProductId);
        builder.HasIndex(sm => sm.CreatedAt);
        builder.HasIndex(sm => new { sm.TenantId, sm.InventoryEventId })
            .HasDatabaseName("IX_StockMovements_TenantId_InventoryEventId");
        builder.HasIndex(sm => new { sm.ReferenceLineId, sm.InventoryEventType })
            .HasDatabaseName("IX_StockMovements_ReferenceLineId_InventoryEventType");
        builder.HasIndex(sm => new { sm.TenantId, sm.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_StockMovements_TenantId_IdempotencyKey")
            .HasFilter("\"IsDeleted\" = false AND \"IdempotencyKey\" IS NOT NULL");
    }
}
