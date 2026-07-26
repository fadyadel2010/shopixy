using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for OnlineCustomerPasswordResetToken entity.
/// 
/// INDEXES:
/// - Unique on (TenantId, TokenHash) - token lookup
/// - (TenantId, OnlineCustomerId, IsUsed) - invalidate previous tokens
/// - (ExpiresAtUtc) - cleanup expired tokens
/// 
/// Phase: 10.3.5 - Password Reset
/// </summary>
public class OnlineCustomerPasswordResetTokenConfiguration : IEntityTypeConfiguration<OnlineCustomerPasswordResetToken>
{
    public void Configure(EntityTypeBuilder<OnlineCustomerPasswordResetToken> builder)
    {
        builder.ToTable("OnlineCustomerPasswordResetTokens");

        builder.HasKey(e => e.Id);

        // TenantId is MANDATORY
        builder.Property(e => e.TenantId)
            .IsRequired();

        builder.Property(e => e.OnlineCustomerId)
            .IsRequired();

        // SHA256 hash of the token - 64 chars hex
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

        builder.Property(e => e.CreatedByIp)
            .IsRequired()
            .HasMaxLength(64);

        // CRITICAL: Unique token hash per tenant
        // Prevents token collision attacks
        builder.HasIndex(e => new { e.TenantId, e.TokenHash })
            .IsUnique()
            .HasDatabaseName("IX_PasswordResetToken_TenantId_TokenHash");

        // For invalidating previous unused tokens
        builder.HasIndex(e => new { e.TenantId, e.OnlineCustomerId, e.IsUsed })
            .HasDatabaseName("IX_PasswordResetToken_TenantId_CustomerId_IsUsed");

        // For cleanup of expired tokens
        builder.HasIndex(e => e.ExpiresAtUtc)
            .HasDatabaseName("IX_PasswordResetToken_ExpiresAtUtc");

        // Foreign key to OnlineCustomer
        builder.HasOne(e => e.OnlineCustomer)
            .WithMany()
            .HasForeignKey(e => e.OnlineCustomerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
