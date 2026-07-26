using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CommerceHubCheckoutIdempotencyConfiguration : IEntityTypeConfiguration<CommerceHubCheckoutIdempotency>
{
    public void Configure(EntityTypeBuilder<CommerceHubCheckoutIdempotency> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Key)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.RequestHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(e => e.ResponseJson)
            .HasColumnType("text");

        builder.HasIndex(e => e.Key)
            .IsUnique();

        builder.HasIndex(e => e.CommerceHubCustomerId)
            .HasDatabaseName("IX_CommerceHubCheckoutIdempotency_CommerceHubCustomerId");

        builder.ToTable("CommerceHubCheckoutIdempotency", table =>
        {
            table.HasCheckConstraint(
                "CK_CommerceHubCheckoutIdempotency_CustomerAuthorityBoundary",
                "\"CommerceHubCustomerId\" IS NOT NULL AND \"CommerceHubCustomerId\" <> '00000000-0000-0000-0000-000000000000' AND \"OnlineCustomerId\" IS NULL");
        });

        builder.HasIndex(e => new { e.TenantId, e.CommerceHubCustomerId })
            .HasDatabaseName("IX_CommerceHubCheckoutIdempotency_TenantId_CommerceHubCustomerId");

        // Index for RequestHash-based recovery (Phase S27)
        builder.HasIndex(e => new { e.TenantId, e.RequestHash })
            .HasDatabaseName("IX_CommerceHubCheckoutIdempotency_TenantId_RequestHash");
    }
}
