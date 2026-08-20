using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

/// <summary>
/// FEED-1A: Persistence mapping for <see cref="ProductFeedToken"/>.
/// Only the SHA-256 token hash is stored; the raw token never reaches the database.
/// </summary>
public class ProductFeedTokenConfiguration : IEntityTypeConfiguration<ProductFeedToken>
{
    public void Configure(EntityTypeBuilder<ProductFeedToken> builder)
    {
        builder.ToTable("ProductFeedTokens");

        builder.HasKey(token => token.Id);

        builder.Property(token => token.TenantId)
            .IsRequired();

        builder.Property(token => token.Channel)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(token => token.TokenHash)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(token => token.CreatedAtUtc)
            .IsRequired();

        builder.Property(token => token.RevokedAtUtc);

        // Hot lookup: resolve the public feed request token to its tenant.
        builder.HasIndex(token => token.TokenHash)
            .IsUnique()
            .HasDatabaseName("IX_ProductFeedTokens_TokenHash");

        // Admin listing: all tokens for a tenant + channel.
        builder.HasIndex(token => new { token.TenantId, token.Channel })
            .HasDatabaseName("IX_ProductFeedTokens_TenantId_Channel");

        // Tenant ownership — Restrict, matching Domain -> Tenant convention.
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(token => token.TenantId)
            .HasConstraintName("FK_ProductFeedTokens_TenantId")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
