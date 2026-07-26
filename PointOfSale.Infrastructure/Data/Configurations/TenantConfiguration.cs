using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for the Tenant entity
/// </summary>
public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");
        
        builder.HasKey(t => t.Id);
        
        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(200);
            
        builder.Property(t => t.Slug)
            .HasMaxLength(100);
            
        builder.Property(t => t.ContactEmail)
            .HasMaxLength(255);
            
        builder.HasIndex(t => t.Slug)
            .IsUnique()
            .HasFilter("\"Slug\" IS NOT NULL");
            
        builder.HasIndex(t => t.IsActive);
    }
}
