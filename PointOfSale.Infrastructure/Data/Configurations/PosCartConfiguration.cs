using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class PosCartConfiguration : IEntityTypeConfiguration<PosCart>
{
    public void Configure(EntityTypeBuilder<PosCart> builder)
    {
        builder.ToTable("PosCarts");
        builder.HasKey(cart => cart.Id);

        builder.Property(cart => cart.Status).HasConversion<int>();
        builder.Property(cart => cart.Subtotal).HasPrecision(18, 2);
        builder.Property(cart => cart.DiscountAmount).HasPrecision(18, 2);
        builder.Property(cart => cart.TaxAmount).HasPrecision(18, 2);
        builder.Property(cart => cart.TotalAmount).HasPrecision(18, 2);
        builder.Property(cart => cart.CancellationReason).HasMaxLength(500);
        builder.Property(cart => cart.RecoveryReason).HasMaxLength(500);
        builder.Property(cart => cart.RowVersion)
            .IsConcurrencyToken();

        builder.HasIndex(cart => new { cart.TenantId, cart.CashierId, cart.Status, cart.UpdatedAt })
            .HasDatabaseName("IX_PosCarts_TenantId_CashierId_Status_UpdatedAt");
        builder.HasIndex(cart => new { cart.TenantId, cart.WarehouseId, cart.Status })
            .HasDatabaseName("IX_PosCarts_TenantId_WarehouseId_Status");
        builder.HasIndex(cart => new { cart.TenantId, cart.ExpiresAtUtc })
            .HasDatabaseName("IX_PosCarts_TenantId_ExpiresAtUtc");
        builder.HasIndex(cart => new { cart.TenantId, cart.CompletedSaleId })
            .HasDatabaseName("IX_PosCarts_TenantId_CompletedSaleId")
            .HasFilter("\"CompletedSaleId\" IS NOT NULL");
        builder.HasIndex(cart => new { cart.TenantId, cart.CashierId })
            .IsUnique()
            .HasDatabaseName("UX_PosCarts_TenantId_CashierId_Open")
            .HasFilter($"\"IsDeleted\" = false AND \"Status\" IN ({(int)PosCartStatus.Active}, {(int)PosCartStatus.Held}, {(int)PosCartStatus.Recovered})");

        builder.HasOne(cart => cart.Warehouse)
            .WithMany()
            .HasForeignKey(cart => cart.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(cart => cart.Customer)
            .WithMany()
            .HasForeignKey(cart => cart.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(cart => cart.Cashier)
            .WithMany()
            .HasForeignKey(cart => cart.CashierId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
