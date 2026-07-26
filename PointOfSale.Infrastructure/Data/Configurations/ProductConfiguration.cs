using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(p => p.Id);
        
        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(300);
        
        builder.Property(p => p.Barcode)
            .HasMaxLength(100);
        
        builder.Property(p => p.SKU)
            .HasMaxLength(100);
        
        builder.Property(p => p.PurchasePrice)
            .HasPrecision(18, 2);
        
        builder.Property(p => p.SalePrice)
            .HasPrecision(18, 2);
        
        builder.Property(p => p.TaxRate)
            .HasPrecision(5, 2);
        
        builder.Property(p => p.MinimumStockLevel)
            .HasPrecision(18, 2);
        
        builder.Property(p => p.Unit)
            .HasMaxLength(50);

        builder.Property(p => p.ProductTemplate)
            .HasMaxLength(100);

        builder.Property(p => p.FeaturesJson)
            .HasColumnType("jsonb");

        builder.Property(p => p.FaqJson)
            .HasColumnType("jsonb");

        builder.Property(p => p.TagsJson)
            .HasColumnType("jsonb");

        builder.Property(p => p.AiMetadataJson)
            .HasColumnType("jsonb");
        
        // SECURITY: Prevent Guid.Empty CategoryId to avoid category-scoped coupon bypass
        builder.Property(p => p.CategoryId)
            .IsRequired();
        
        builder.HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
        
        builder.HasIndex(p => p.TenantId);
        builder.HasIndex(p => new { p.TenantId, p.CategoryId });
        builder.HasIndex(p => new { p.TenantId, p.Barcode });
        builder.HasIndex(p => new { p.TenantId, p.SKU });
        builder.HasIndex(p => p.Name);
    }
}
