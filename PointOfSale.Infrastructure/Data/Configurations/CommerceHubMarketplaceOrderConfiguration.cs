using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class CommerceHubMarketplaceOrderConfiguration : IEntityTypeConfiguration<CommerceHubMarketplaceOrder>
{
    public void Configure(EntityTypeBuilder<CommerceHubMarketplaceOrder> builder)
    {
        builder.ToTable("CommerceHubMarketplaceOrders", table =>
        {
            table.HasCheckConstraint(
                "CK_CommerceHubMarketplaceOrders_CustomerAuthorityBoundary",
                "\"CustomerId\" <> '00000000-0000-0000-0000-000000000000' AND \"OnlineCustomerId\" IS NULL");
            table.HasCheckConstraint(
                "CK_CommerceHubMarketplaceOrders_Currency_EGP",
                "\"Currency\" = 'EGP'");
        });

        builder.HasIndex(order => order.CreatedAt)
            .HasDatabaseName("IX_CommerceHubMarketplaceOrders_CreatedAt");

        builder.HasIndex(order => new { order.PaymentStatus, order.Status, order.CreatedAt })
            .HasDatabaseName("IX_CommerceHubMarketplaceOrders_PaymentStatus_Status_CreatedAt");

        builder.Property(order => order.ProjectionMutationVersion)
            .HasDefaultValue(1L)
            .IsRequired();
    }
}
