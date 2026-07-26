using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.HasKey(c => c.Id);
        
        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(200);
        
        builder.Property(c => c.Phone)
            .HasMaxLength(50);
        
        builder.Property(c => c.Email)
            .HasMaxLength(200);
        
        builder.Property(c => c.TaxNumber)
            .HasMaxLength(50);
        
        builder.Property(c => c.Balance)
            .HasPrecision(18, 2);
        
        builder.HasIndex(c => c.TenantId);
        builder.HasIndex(c => c.Phone);
        builder.HasIndex(c => c.Email);
        builder.HasIndex(c => c.TaxNumber);
    }
}
