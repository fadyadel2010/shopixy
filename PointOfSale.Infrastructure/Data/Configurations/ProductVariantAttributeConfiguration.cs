using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for ProductVariantAttribute entity.
/// 
/// PHASE 11.1 — VARIANT ATTRIBUTES
/// </summary>
public class ProductVariantAttributeConfiguration : IEntityTypeConfiguration<ProductVariantAttribute>
{
    public void Configure(EntityTypeBuilder<ProductVariantAttribute> builder)
    {
        builder.ToTable("ProductVariantAttributes");
        
        builder.HasKey(a => a.Id);
        
        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(100);
        
        builder.Property(a => a.Value)
            .IsRequired()
            .HasMaxLength(255);
        
        // Relationship to Variant
        builder.HasOne(a => a.Variant)
            .WithMany(v => v.Attributes)
            .HasForeignKey(a => a.VariantId)
            .OnDelete(DeleteBehavior.Cascade);
        
        // Index for finding attributes by variant
        builder.HasIndex(a => a.VariantId)
            .HasDatabaseName("IX_ProductVariantAttributes_VariantId");
        
        // A variant cannot have two live values for the same attribute name.
        builder.HasIndex(a => new { a.VariantId, a.Name })
            .IsUnique()
            .HasDatabaseName("IX_ProductVariantAttributes_VariantId_Name")
            .HasFilter("\"IsDeleted\" = false");
    }
}
