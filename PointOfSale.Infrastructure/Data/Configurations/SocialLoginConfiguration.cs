using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for OnlineCustomerAuthProvider entity.
/// 
/// INDEXES:
/// - Unique on (TenantId, Provider, ProviderUserId) - one provider account per customer
/// - Unique on (TenantId, OnlineCustomerId, Provider) - one link per provider per customer
/// - (TenantId, OnlineCustomerId) for finding all providers for a customer
/// 
/// Phase: 10.3.3.1 - Social Identity Model
/// </summary>
public class OnlineCustomerAuthProviderConfiguration : IEntityTypeConfiguration<OnlineCustomerAuthProvider>
{
    public void Configure(EntityTypeBuilder<OnlineCustomerAuthProvider> builder)
    {
        builder.ToTable("OnlineCustomerAuthProviders");

        builder.HasKey(e => e.Id);

        // TenantId is MANDATORY
        builder.Property(e => e.TenantId)
            .IsRequired();

        builder.Property(e => e.OnlineCustomerId)
            .IsRequired();

        // Provider enum stored as string for readability
        builder.Property(e => e.Provider)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        // Provider user ID - the unique ID from the provider
        builder.Property(e => e.ProviderUserId)
            .IsRequired()
            .HasMaxLength(256);

        // Email from provider (normalized)
        builder.Property(e => e.ProviderEmail)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.DisplayName)
            .HasMaxLength(256);

        builder.Property(e => e.PictureUrl)
            .HasMaxLength(2048);

        builder.Property(e => e.CreatedAtUtc)
            .IsRequired();

        // CRITICAL: One provider account can only link to one customer per tenant
        // This prevents provider account takeover attacks
        builder.HasIndex(e => new { e.TenantId, e.Provider, e.ProviderUserId })
            .IsUnique()
            .HasDatabaseName("IX_AuthProvider_TenantId_Provider_ProviderUserId");

        // One customer can only have one link per provider
        builder.HasIndex(e => new { e.TenantId, e.OnlineCustomerId, e.Provider })
            .IsUnique()
            .HasDatabaseName("IX_AuthProvider_TenantId_CustomerId_Provider");

        // For finding all providers for a customer
        builder.HasIndex(e => new { e.TenantId, e.OnlineCustomerId })
            .HasDatabaseName("IX_AuthProvider_TenantId_CustomerId");

        // Foreign key to OnlineCustomer
        builder.HasOne(e => e.OnlineCustomer)
            .WithMany()
            .HasForeignKey(e => e.OnlineCustomerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// EF Core configuration for StorefrontAuthProviderSettings entity.
/// 
/// INDEXES:
/// - Unique on (TenantId, Provider) - one settings per provider per tenant
/// 
/// Phase: 10.3.3.2 - Provider Configuration (POS Managed)
/// </summary>
public class StorefrontAuthProviderSettingsConfiguration : IEntityTypeConfiguration<StorefrontAuthProviderSettings>
{
    public void Configure(EntityTypeBuilder<StorefrontAuthProviderSettings> builder)
    {
        builder.ToTable("StorefrontAuthProviderSettings");
        builder.Property(e => e.UpdatedAtUtc).IsConcurrencyToken();

        builder.HasKey(e => e.Id);

        builder.Property(e => e.TenantId)
            .IsRequired();

        builder.Property(e => e.Provider)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(e => e.IsEnabled)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.ClientId)
            .HasMaxLength(256);

        // Encrypted - may be longer than actual secret
        builder.Property(e => e.ClientSecretEncrypted)
            .HasMaxLength(1024);

        builder.Property(e => e.AdditionalScopes)
            .HasMaxLength(512);

        builder.Property(e => e.CreatedAtUtc)
            .IsRequired();

        // Unique: one settings per provider per tenant
        builder.HasIndex(e => new { e.TenantId, e.Provider })
            .IsUnique()
            .HasDatabaseName("IX_AuthProviderSettings_TenantId_Provider");
    }
}
