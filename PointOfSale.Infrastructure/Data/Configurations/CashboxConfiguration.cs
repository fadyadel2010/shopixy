using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CashboxConfiguration : IEntityTypeConfiguration<Cashbox>
{
    public void Configure(EntityTypeBuilder<Cashbox> builder)
    {
        builder.HasKey(c => c.Id);
        
        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(200);
        
        builder.Property(c => c.CurrentBalance)
            .HasPrecision(18, 2);
        
        builder.Property(c => c.OpeningBalance)
            .HasPrecision(18, 2);
        
        builder.Property(c => c.ExpectedBalance)
            .HasPrecision(18, 2);
        
        builder.Property(c => c.Description)
            .HasMaxLength(500);
        
        builder.HasOne(c => c.Warehouse)
            .WithMany()
            .HasForeignKey(c => c.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
        
        builder.HasOne(c => c.OpenedByUser)
            .WithMany()
            .HasForeignKey(c => c.OpenedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        
        builder.HasIndex(c => c.WarehouseId);
        builder.HasIndex(c => c.IsActive);
        builder.HasIndex(c => new { c.WarehouseId, c.IsActive });
    }
}
