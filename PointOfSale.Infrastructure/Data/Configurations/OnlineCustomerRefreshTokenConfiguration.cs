using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for OnlineCustomerRefreshToken entity.
/// 
/// INDEXES:
/// - Unique on (TenantId, TokenHash) for token lookup
/// - (TenantId, OnlineCustomerId) for finding customer tokens
/// - ExpiresAtUtc for cleanup queries
/// 
/// Phase: 10.3.4 - Storefront Refresh Tokens & Token Revocation
/// </summary>
public class OnlineCustomerRefreshTokenConfiguration : IEntityTypeConfiguration<OnlineCustomerRefreshToken>
{
    public void Configure(EntityTypeBuilder<OnlineCustomerRefreshToken> builder)
    {
        builder.ToTable("OnlineCustomerRefreshTokens");

        builder.HasKey(e => e.Id);

        // TenantId is MANDATORY
        builder.Property(e => e.TenantId)
            .IsRequired();

        builder.Property(e => e.OnlineCustomerId)
            .IsRequired();

        // Token hash - SHA256 produces 64 hex characters
        builder.Property(e => e.TokenHash)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(e => e.ExpiresAtUtc)
            .IsRequired();

        builder.Property(e => e.CurrentAccessTokenJti)
            .HasMaxLength(128);

        builder.Property(e => e.IsRevoked)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.IsAccessTokenBlacklisted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.RevokedReason)
            .HasMaxLength(256);

        builder.Property(e => e.CreatedAtUtc)
            .IsRequired();

        builder.Property(e => e.CreatedByIp)
            .IsRequired()
            .HasMaxLength(45); // IPv6 max length

        builder.Property(e => e.CreatedByUserAgent)
            .HasMaxLength(512);

        // Unique index for token lookup (most common query)
        builder.HasIndex(e => new { e.TenantId, e.TokenHash })
            .IsUnique()
            .HasDatabaseName("IX_RefreshToken_TenantId_TokenHash");

        // Index for finding all tokens for a customer
        builder.HasIndex(e => new { e.TenantId, e.OnlineCustomerId })
            .HasDatabaseName("IX_RefreshToken_TenantId_CustomerId");

        // Index for cleanup of expired tokens
        builder.HasIndex(e => e.ExpiresAtUtc)
            .HasDatabaseName("IX_RefreshToken_ExpiresAtUtc");

        builder.HasIndex(e => new { e.TenantId, e.CurrentAccessTokenJti })
            .HasDatabaseName("IX_RefreshToken_TenantId_CurrentAccessTokenJti");

        // Index for finding active tokens
        builder.HasIndex(e => new { e.TenantId, e.OnlineCustomerId, e.IsRevoked })
            .HasDatabaseName("IX_RefreshToken_TenantId_CustomerId_IsRevoked");

        // Foreign key to OnlineCustomer
        builder.HasOne(e => e.OnlineCustomer)
            .WithMany()
            .HasForeignKey(e => e.OnlineCustomerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
