using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for OnlineCustomer entity.
/// 
/// ARCHITECTURAL RULES:
/// - Unique index on (TenantId, Email) for tenant-scoped email uniqueness
/// - Email is case-insensitive (normalized to lowercase before save)
/// - PasswordHash can be NULL for social login providers
/// - TenantId is MANDATORY - no cross-tenant access
/// 
/// Phase: 10.3.1 - Online Customer Identity (Core)
/// </summary>
public class OnlineCustomerConfiguration : IEntityTypeConfiguration<OnlineCustomer>
{
    public void Configure(EntityTypeBuilder<OnlineCustomer> builder)
    {
        // Primary key
        builder.HasKey(c => c.Id);

        // TenantId is required and indexed
        builder.Property(c => c.TenantId)
            .IsRequired();
        builder.HasIndex(c => c.TenantId);

        // Unique index on (TenantId, Email) for tenant-scoped email uniqueness
        builder.HasIndex(c => new { c.TenantId, c.Email })
            .IsUnique()
            .HasDatabaseName("IX_OnlineCustomers_TenantId_Email");

        // Email configuration
        builder.Property(c => c.Email)
            .IsRequired()
            .HasMaxLength(255);

        // PasswordHash - can be NULL for social login
        builder.Property(c => c.PasswordHash)
            .HasMaxLength(255);

        // Provider configuration with enum conversion
        builder.Property(c => c.Provider)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        // ProviderUserId for social login
        builder.Property(c => c.ProviderUserId)
            .HasMaxLength(255);

        // Display name
        builder.Property(c => c.DisplayName)
            .HasMaxLength(200);

        // Phone number
        builder.Property(c => c.Phone)
            .HasMaxLength(50);

        // Indexes for common queries
        builder.HasIndex(c => c.IsActive);
        builder.HasIndex(c => c.EmailVerified);
        builder.HasIndex(c => c.Provider);
        builder.HasIndex(c => c.CreatedAtUtc);

        // Composite index for provider lookups
        builder.HasIndex(c => new { c.TenantId, c.Provider, c.ProviderUserId })
            .HasDatabaseName("IX_OnlineCustomers_TenantId_Provider_ProviderUserId");
    }
}
