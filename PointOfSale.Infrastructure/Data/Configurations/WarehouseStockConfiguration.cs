using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class WarehouseStockConfiguration : IEntityTypeConfiguration<WarehouseStock>
{
    public void Configure(EntityTypeBuilder<WarehouseStock> builder)
    {
        builder.HasKey(ws => ws.Id);
        
        builder.Property(ws => ws.Quantity)
            .HasPrecision(18, 2);
        
        builder.Property(ws => ws.ReservedQuantity)
            .HasPrecision(18, 2);
        
        builder.HasOne(ws => ws.Warehouse)
            .WithMany(w => w.Stock)
            .HasForeignKey(ws => ws.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        
        builder.HasOne(ws => ws.Product)
            .WithMany(p => p.WarehouseStocks)
            .HasForeignKey(ws => ws.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        
        builder.HasOne(ws => ws.ProductVariant)
            .WithMany()
            .HasForeignKey(ws => ws.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);
        
        // Unique constraint: one stock record per warehouse-product-variant combination
        builder.HasIndex(ws => new { ws.WarehouseId, ws.ProductId, ws.ProductVariantId })
            .IsUnique();

        builder.HasIndex(ws => new { ws.TenantId, ws.Quantity })
            .HasDatabaseName("IX_WarehouseStocks_TenantId_Quantity");

        builder.HasIndex(ws => new { ws.TenantId, ws.WarehouseId, ws.Quantity })
            .HasDatabaseName("IX_WarehouseStocks_TenantId_WarehouseId_Quantity");
    }
}
