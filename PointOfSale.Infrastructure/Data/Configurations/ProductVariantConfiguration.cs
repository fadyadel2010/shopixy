using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for ProductVariant entity.
/// 
/// PHASE 11.1 — PRODUCT VARIANTS
/// 
/// Key constraints:
/// - TenantId required for multi-tenant isolation
/// - Only ONE variant per product can have IsDefault = true
/// - Unique constraint on (TenantId, SKU) with filter for non-deleted
/// </summary>
public class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.ToTable("ProductVariants");
        
        builder.HasKey(v => v.Id);
        
        // TenantId is required
        builder.Property(v => v.TenantId)
            .IsRequired();
        
        builder.Property(v => v.SKU)
            .HasMaxLength(100);
        
        builder.Property(v => v.Barcode)
            .HasMaxLength(100);

        builder.Property(v => v.AttributeCombinationKey)
            .HasMaxLength(PointOfSale.Core.Helpers.ProductVariantAttributeCombinationKey.MaxLength);
        
        builder.Property(v => v.Price)
            .HasPrecision(18, 2)
            .IsRequired();
        
        builder.Property(v => v.OnlinePrice)
            .HasPrecision(18, 2);
        
        builder.Property(v => v.CostPrice)
            .HasPrecision(18, 2);
        
        builder.Property(v => v.Weight)
            .HasPrecision(18, 3);

        builder.Property(v => v.StockUpdatedAtUtc);

        builder.Property(v => v.RowVersion)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .IsRowVersion();

        builder.Property(v => v.ImageUrl)
            .HasMaxLength(500);

        builder.Property(v => v.MediaId);

        builder.Property(v => v.SemanticLifecycleState)
            .HasConversion<int>()
            .HasDefaultValue(PointOfSale.Core.Enums.ProductVariantSemanticLifecycleState.Current)
            .IsRequired();

        builder.Property(v => v.SemanticOperationKind)
            .HasMaxLength(64);
        
        // Relationship to Product
        builder.HasOne(v => v.Product)
            .WithMany(p => p.Variants)
            .HasForeignKey(v => v.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(v => v.Media)
            .WithMany()
            .HasForeignKey(v => new { v.TenantId, v.ProductId, v.MediaId })
            .HasPrincipalKey(m => new { m.TenantId, m.ProductId, m.Id })
            .OnDelete(DeleteBehavior.NoAction);
        
        // Indexes for query performance
        builder.HasIndex(v => new { v.TenantId, v.ProductId })
            .HasDatabaseName("IX_ProductVariants_TenantId_ProductId");

        builder.HasIndex(v => new { v.TenantId, v.SemanticLifecycleState })
            .HasDatabaseName("IX_ProductVariants_TenantId_SemanticLifecycleState");

        builder.HasIndex(v => new { v.TenantId, v.SemanticOperationId })
            .HasDatabaseName("IX_ProductVariants_TenantId_SemanticOperationId")
            .HasFilter("\"SemanticOperationId\" IS NOT NULL");
        
        builder.HasIndex(v => new { v.TenantId, v.SKU })
            .IsUnique()
            .HasDatabaseName("IX_ProductVariants_SKU")
            .HasFilter("\"IsDeleted\" = false AND \"SemanticLifecycleState\" = 0 AND \"SKU\" IS NOT NULL");
        
        builder.HasIndex(v => new { v.TenantId, v.Barcode })
            .IsUnique()
            .HasDatabaseName("IX_ProductVariants_Barcode")
            .HasFilter("\"IsDeleted\" = false AND \"SemanticLifecycleState\" = 0 AND \"Barcode\" IS NOT NULL");

        builder.HasIndex(v => new { v.TenantId, v.ProductId, v.AttributeCombinationKey })
            .IsUnique()
            .HasDatabaseName("IX_ProductVariants_TenantId_ProductId_AttributeCombinationKey")
            .HasFilter("\"IsDeleted\" = false AND \"SemanticLifecycleState\" = 0 AND \"AttributeCombinationKey\" IS NOT NULL");
        
        // Index for finding default variant quickly
        builder.HasIndex(v => new { v.TenantId, v.ProductId, v.IsDefault })
            .IsUnique()
            .HasDatabaseName("IX_ProductVariants_ProductId_IsDefault")
            .HasFilter("\"IsDeleted\" = false AND \"SemanticLifecycleState\" = 0 AND \"IsDefault\" = true");
    }
}
