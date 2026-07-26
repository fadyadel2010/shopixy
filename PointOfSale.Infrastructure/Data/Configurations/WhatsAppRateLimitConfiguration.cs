using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for WhatsAppRateLimit entity.
/// Phase: Smart Abandoned Cart Recovery Engine
/// </summary>
public class WhatsAppRateLimitConfiguration : IEntityTypeConfiguration<WhatsAppRateLimit>
{
    public void Configure(EntityTypeBuilder<WhatsAppRateLimit> builder)
    {
        builder.ToTable("WhatsAppRateLimits");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.TenantId)
            .IsRequired();

        builder.Property(r => r.PhoneNumber)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(r => r.MessageCount)
            .IsRequired();

        builder.Property(r => r.WindowStartUtc)
            .IsRequired();

        builder.Property(r => r.LastMessageAtUtc)
            .IsRequired();

        builder.Property(r => r.OptedOut)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(r => r.OptedOutAtUtc);

        // Unique constraint for upsert operations
        builder.HasIndex(r => new { r.TenantId, r.PhoneNumber })
            .IsUnique()
            .HasDatabaseName("IX_WhatsAppRateLimits_TenantId_PhoneNumber");

        // Index for tenant queries
        builder.HasIndex(r => r.TenantId)
            .HasDatabaseName("IX_WhatsAppRateLimits_TenantId");
    }
}
