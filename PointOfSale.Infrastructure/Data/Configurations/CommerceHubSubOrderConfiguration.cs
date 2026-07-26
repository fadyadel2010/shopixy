using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class CommerceHubSubOrderConfiguration : IEntityTypeConfiguration<CommerceHubSubOrder>
{
    public void Configure(EntityTypeBuilder<CommerceHubSubOrder> builder)
    {
        builder.ToTable("CommerceHubSubOrders", table =>
        {
            table.HasCheckConstraint(
                "CK_CommerceHubSubOrders_Currency_EGP",
                "\"Currency\" = 'EGP'");
        });

        builder.HasIndex(subOrder => new { subOrder.StoreId, subOrder.CreatedAt })
            .HasDatabaseName("IX_CommerceHubSubOrders_StoreId_CreatedAt");

        builder.HasIndex(subOrder => new { subOrder.MarketplaceOrderId, subOrder.StoreId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false")
            .HasDatabaseName("UX_CommerceHubSubOrders_MarketplaceOrderId_StoreId_Live");

        builder.HasIndex(subOrder => subOrder.CreatedAt)
            .HasDatabaseName("IX_CommerceHubSubOrders_CreatedAt");

        builder.Property(subOrder => subOrder.ProjectionMutationVersion)
            .HasDefaultValue(1L)
            .IsRequired();
    }
}
