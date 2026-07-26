using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.Accounting;

namespace PointOfSale.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for TreasuryAccount entity.
/// </summary>
public class TreasuryAccountConfiguration : IEntityTypeConfiguration<TreasuryAccount>
{
    public void Configure(EntityTypeBuilder<TreasuryAccount> builder)
    {
        builder.ToTable("TreasuryAccounts");
        
        builder.HasKey(e => e.Id);
        
        // Unique account per type per tenant
        builder.HasIndex(e => new { e.TenantId, e.AccountId })
            .IsUnique()
            .HasDatabaseName("IX_TreasuryAccounts_TenantAccount");
        
        builder.HasIndex(e => e.TenantId)
            .HasDatabaseName("IX_TreasuryAccounts_Tenant");
        
        builder.HasIndex(e => new { e.TenantId, e.Type })
            .HasDatabaseName("IX_TreasuryAccounts_TenantType");
        
        // Properties
        builder.Property(e => e.Type)
            .IsRequired();
        
        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);
        
        builder.Property(e => e.NameAr)
            .HasMaxLength(100);
        
        builder.Property(e => e.ReferenceCode)
            .HasMaxLength(50);
        
        builder.Property(e => e.Location)
            .HasMaxLength(100);
        
        builder.Property(e => e.IsDefault)
            .IsRequired()
            .HasDefaultValue(false);
        
        builder.Property(e => e.IsActive)
            .IsRequired()
            .HasDefaultValue(true);
        
        builder.Property(e => e.DisplayOrder)
            .IsRequired()
            .HasDefaultValue(0);
        
        // Relationships
        builder.HasOne(e => e.Account)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.AccountId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
