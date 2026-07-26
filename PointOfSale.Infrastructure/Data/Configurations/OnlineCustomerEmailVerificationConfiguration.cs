using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for OnlineCustomerEmailVerification entity.
/// 
/// INDEXES:
/// - Unique on (TenantId, TokenHash) for token lookup
/// - (TenantId, OnlineCustomerId, IsUsed) for finding active tokens
/// - ExpiresAtUtc for cleanup queries
/// 
/// Phase: 10.3.2.5 - Storefront Email Verification Flow
/// </summary>
public class OnlineCustomerEmailVerificationConfiguration : IEntityTypeConfiguration<OnlineCustomerEmailVerification>
{
    public void Configure(EntityTypeBuilder<OnlineCustomerEmailVerification> builder)
    {
        builder.ToTable("OnlineCustomerEmailVerifications");

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

        builder.Property(e => e.IsUsed)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedAtUtc)
            .IsRequired();

        // Index for token lookup (most common query)
        builder.HasIndex(e => new { e.TenantId, e.TokenHash })
            .IsUnique()
            .HasDatabaseName("IX_EmailVerification_TenantId_TokenHash");

        // Index for finding active tokens for a customer
        builder.HasIndex(e => new { e.TenantId, e.OnlineCustomerId, e.IsUsed })
            .HasDatabaseName("IX_EmailVerification_TenantId_CustomerId_IsUsed");

        // Index for cleanup of expired tokens
        builder.HasIndex(e => e.ExpiresAtUtc)
            .HasDatabaseName("IX_EmailVerification_ExpiresAtUtc");

        // Foreign key to OnlineCustomer
        builder.HasOne(e => e.OnlineCustomer)
            .WithMany()
            .HasForeignKey(e => e.OnlineCustomerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
