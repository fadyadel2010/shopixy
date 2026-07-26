using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class PosCartItemConfiguration : IEntityTypeConfiguration<PosCartItem>
{
    public void Configure(EntityTypeBuilder<PosCartItem> builder)
    {
        builder.ToTable("PosCartItems");
        builder.HasKey(item => item.Id);

        builder.Property(item => item.Quantity).HasPrecision(18, 4);
        builder.Property(item => item.ConversionFactor).HasPrecision(18, 6);
        builder.Property(item => item.BaseQuantity).HasPrecision(18, 4);
        builder.Property(item => item.UnitPriceSnapshot).HasPrecision(18, 2);
        builder.Property(item => item.DiscountAmount).HasPrecision(18, 2);
        builder.Property(item => item.TaxRateSnapshot).HasPrecision(9, 4);
        builder.Property(item => item.TaxAmount).HasPrecision(18, 2);
        builder.Property(item => item.TotalAmount).HasPrecision(18, 2);
        builder.Property(item => item.CostPriceSnapshot).HasPrecision(18, 6);
        builder.Property(item => item.CostAmountSnapshot).HasPrecision(18, 2);
        builder.Property(item => item.ProductNameSnapshot).HasMaxLength(300).IsRequired();
        builder.Property(item => item.VariantSkuSnapshot).HasMaxLength(100);

        builder.HasIndex(item => new { item.PosCartId, item.ProductVariantId, item.UnitId })
            .IsUnique()
            .HasDatabaseName("UX_PosCartItems_Cart_Variant_Unit")
            .HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(item => new { item.ProductId, item.ProductVariantId });

        builder.HasOne(item => item.PosCart)
            .WithMany(cart => cart.Items)
            .HasForeignKey(item => item.PosCartId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.Product)
            .WithMany()
            .HasForeignKey(item => item.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.ProductVariant)
            .WithMany()
            .HasForeignKey(item => item.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.Unit)
            .WithMany()
            .HasForeignKey(item => item.UnitId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
