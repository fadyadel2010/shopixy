using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.Accounting;

namespace PointOfSale.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for PaymentMethod entity.
/// </summary>
public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.ToTable("PaymentMethods");
        
        builder.HasKey(e => e.Id);
        
        // Unique code per tenant
        builder.HasIndex(e => new { e.TenantId, e.Code })
            .IsUnique()
            .HasDatabaseName("IX_PaymentMethods_TenantCode");
        
        builder.HasIndex(e => e.TenantId)
            .HasDatabaseName("IX_PaymentMethods_Tenant");
        
        // Properties
        builder.Property(e => e.Code)
            .IsRequired()
            .HasMaxLength(20);
        
        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);
        
        builder.Property(e => e.NameAr)
            .HasMaxLength(100);
        
        builder.Property(e => e.IsSystem)
            .IsRequired()
            .HasDefaultValue(false);
        
        builder.Property(e => e.IsActive)
            .IsRequired()
            .HasDefaultValue(true);
        
        builder.Property(e => e.DisplayOrder)
            .IsRequired()
            .HasDefaultValue(0);
        
        // Relationships
        builder.HasOne(e => e.TargetAccount)
            .WithMany()
            .HasForeignKey(e => e.TargetAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        
        builder.HasOne(e => e.ClearingAccount)
            .WithMany()
            .HasForeignKey(e => e.ClearingAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        
        builder.HasOne(e => e.FeesAccount)
            .WithMany()
            .HasForeignKey(e => e.FeesAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
