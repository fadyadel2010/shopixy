using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for TenantPaymentMethod entity.
/// 
/// PHASE 10.4.D1: Payment Methods Management
/// </summary>
public class TenantPaymentMethodConfiguration : IEntityTypeConfiguration<TenantPaymentMethod>
{
    public void Configure(EntityTypeBuilder<TenantPaymentMethod> builder)
    {
        builder.ToTable("TenantPaymentMethods");
        
        builder.HasKey(x => x.Id);
        
        // Unique constraint: One payment method code per tenant
        builder.HasIndex(x => new { x.TenantId, x.Code })
            .IsUnique()
            .HasDatabaseName("IX_TenantPaymentMethods_TenantId_Code");
        
        // Index for tenant queries
        builder.HasIndex(x => x.TenantId)
            .HasDatabaseName("IX_TenantPaymentMethods_TenantId");
        
        // Index for enabled methods query
        builder.HasIndex(x => new { x.TenantId, x.IsEnabled })
            .HasDatabaseName("IX_TenantPaymentMethods_TenantId_IsEnabled");
        
        // Properties
        builder.Property(x => x.Code)
            .IsRequired()
            .HasMaxLength(50);
        
        builder.Property(x => x.DisplayName)
            .IsRequired()
            .HasMaxLength(100);
        
        builder.Property(x => x.DisplayNameAr)
            .HasMaxLength(100);
        
        builder.Property(x => x.Description)
            .HasMaxLength(500);
        
        builder.Property(x => x.DescriptionAr)
            .HasMaxLength(500);
        
        builder.Property(x => x.Icon)
            .HasMaxLength(50);
        
        builder.Property(x => x.FeeType)
            .HasMaxLength(20)
            .HasDefaultValue("fixed");
        
        // Encrypted config - no max length (can be large)
        builder.Property(x => x.ConfigEncrypted)
            .HasColumnType("text");
        
        // Decimal precision
        builder.Property(x => x.MinOrderAmount)
            .HasPrecision(18, 4);
        
        builder.Property(x => x.MaxOrderAmount)
            .HasPrecision(18, 4);
        
        builder.Property(x => x.Fee)
            .HasPrecision(18, 4);
    }
}
