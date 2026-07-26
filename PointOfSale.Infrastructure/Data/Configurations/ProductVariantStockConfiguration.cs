using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for ProductVariantStock entity.
/// 
/// PHASE 11.1 — VARIANT-LEVEL INVENTORY
/// </summary>
public class ProductVariantStockConfiguration : IEntityTypeConfiguration<ProductVariantStock>
{
    public void Configure(EntityTypeBuilder<ProductVariantStock> builder)
    {
        builder.ToTable("ProductVariantStocks", table =>
        {
            table.HasCheckConstraint(
                "CK_ProductVariantStocks_Quantity_NonNegative",
                "\"Quantity\" >= 0");
            table.HasCheckConstraint(
                "CK_ProductVariantStocks_ReservedQuantity_NonNegative",
                "\"ReservedQuantity\" >= 0");
            table.HasCheckConstraint(
                "CK_ProductVariantStocks_DamagedQuantity_NonNegative",
                "\"DamagedQuantity\" >= 0");
            table.HasCheckConstraint(
                "CK_ProductVariantStocks_ReservedQuantity_Within_Quantity",
                "\"Quantity\" >= \"ReservedQuantity\"");
        });
        
        builder.HasKey(s => s.Id);
        
        // TenantId is required
        builder.Property(s => s.TenantId)
            .IsRequired();
        
        builder.Property(s => s.Quantity)
            .HasPrecision(18, 2);
        
        builder.Property(s => s.ReservedQuantity)
            .HasPrecision(18, 2);

        builder.Property(s => s.DamagedQuantity)
            .HasPrecision(18, 2);
        
        // Relationship to Variant
        builder.HasOne(s => s.Variant)
            .WithMany(v => v.Stocks)
            .HasForeignKey(s => s.VariantId)
            .OnDelete(DeleteBehavior.Cascade);
        
        // Relationship to Warehouse
        builder.HasOne(s => s.Warehouse)
            .WithMany()
            .HasForeignKey(s => s.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        
        // Indexes for query performance
        builder.HasIndex(s => new { s.TenantId, s.VariantId, s.WarehouseId })
            .HasDatabaseName("IX_ProductVariantStocks_TenantId_VariantId_WarehouseId")
            .IsUnique();
        
        builder.HasIndex(s => s.VariantId)
            .HasDatabaseName("IX_ProductVariantStocks_VariantId");
        
        builder.HasIndex(s => s.WarehouseId)
            .HasDatabaseName("IX_ProductVariantStocks_WarehouseId");
    }
}
