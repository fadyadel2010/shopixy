using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for OnlineCustomerWishlistItem entity.
/// 
/// INDEXES:
/// - Unique: (TenantId, OnlineCustomerId, ProductId) – one item per product per customer
/// - Index: (TenantId, OnlineCustomerId) – for fetching customer's wishlist
/// - Index: (TenantId, ProductId) – for analytics (who wishlisted this product)
/// - Index: (TenantId, IsRemoved) – for filtering active items
/// 
/// Phase: 10.3.6 - Wishlist (Production-grade)
/// </summary>
public class OnlineCustomerWishlistItemConfiguration : IEntityTypeConfiguration<OnlineCustomerWishlistItem>
{
    public void Configure(EntityTypeBuilder<OnlineCustomerWishlistItem> builder)
    {
        // Table name
        builder.ToTable("OnlineCustomerWishlistItems");

        // Primary key
        builder.HasKey(w => w.Id);

        // TenantId is required and indexed
        builder.Property(w => w.TenantId)
            .IsRequired();

        // OnlineCustomerId is required
        builder.Property(w => w.OnlineCustomerId)
            .IsRequired();

        // ProductId is required
        builder.Property(w => w.ProductId)
            .IsRequired();

        // CreatedAtUtc is required
        builder.Property(w => w.CreatedAtUtc)
            .IsRequired();

        // Unique composite index on (TenantId, OnlineCustomerId, ProductId)
        // Ensures one item per product per customer per tenant
        // Filter excludes soft-deleted items so the same product can be re-added
        builder.HasIndex(w => new { w.TenantId, w.OnlineCustomerId, w.ProductId })
            .IsUnique()
            .HasFilter("\"IsRemoved\" = false")
            .HasDatabaseName("IX_OnlineCustomerWishlistItems_TenantId_CustomerId_ProductId");

        // Index for fetching customer's wishlist
        builder.HasIndex(w => new { w.TenantId, w.OnlineCustomerId })
            .HasDatabaseName("IX_OnlineCustomerWishlistItems_TenantId_CustomerId");

        // Index for analytics (who wishlisted this product)
        builder.HasIndex(w => new { w.TenantId, w.ProductId })
            .HasDatabaseName("IX_OnlineCustomerWishlistItems_TenantId_ProductId");

        // Index for filtering active items
        builder.HasIndex(w => new { w.TenantId, w.IsRemoved })
            .HasDatabaseName("IX_OnlineCustomerWishlistItems_TenantId_IsRemoved");

        // Index on CreatedAtUtc for ordering
        builder.HasIndex(w => w.CreatedAtUtc)
            .HasDatabaseName("IX_OnlineCustomerWishlistItems_CreatedAtUtc");
    }
}
