using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.HasKey(supplier => supplier.Id);

        builder.Property(supplier => supplier.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(supplier => supplier.Phone)
            .HasMaxLength(50);

        builder.Property(supplier => supplier.Email)
            .HasMaxLength(200);

        builder.Property(supplier => supplier.TaxNumber)
            .HasMaxLength(50);

        builder.Property(supplier => supplier.Balance)
            .HasPrecision(18, 2);

        builder.HasIndex(supplier => supplier.TenantId);
        builder.HasIndex(supplier => supplier.Phone);
        builder.HasIndex(supplier => supplier.Email);
        builder.HasIndex(supplier => supplier.TaxNumber);
    }
}
