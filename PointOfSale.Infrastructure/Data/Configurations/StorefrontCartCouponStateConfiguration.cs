using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class StorefrontCartCouponStateConfiguration : IEntityTypeConfiguration<StorefrontCartCouponState>
{
    public void Configure(EntityTypeBuilder<StorefrontCartCouponState> builder)
    {
        builder.ToTable("StorefrontCartCouponStates");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OwnerType)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.OwnerId)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.CouponCode)
            .HasMaxLength(128);

        builder.Property(x => x.DiscountType)
            .HasMaxLength(32);

        builder.Property(x => x.CartSubtotal)
            .HasPrecision(18, 4);

        builder.Property(x => x.DiscountValue)
            .HasPrecision(18, 4);

        builder.Property(x => x.DiscountAmount)
            .HasPrecision(18, 4);

        builder.Property(x => x.LastRemovalReason)
            .HasMaxLength(128);

        builder.Property(x => x.MutationVersion)
            .IsConcurrencyToken()
            .ValueGeneratedNever();

        builder.HasIndex(x => new { x.TenantId, x.CartId })
            .IsUnique()
            .HasDatabaseName("UX_StorefrontCartCouponStates_TenantId_CartId");

        builder.HasIndex(x => new { x.TenantId, x.OwnerType, x.OwnerId })
            .HasDatabaseName("IX_StorefrontCartCouponStates_TenantId_Owner");
    }
}
