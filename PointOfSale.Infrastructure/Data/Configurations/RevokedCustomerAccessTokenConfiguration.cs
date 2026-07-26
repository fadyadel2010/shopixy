using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class RevokedCustomerAccessTokenConfiguration : IEntityTypeConfiguration<RevokedCustomerAccessToken>
{
    public void Configure(EntityTypeBuilder<RevokedCustomerAccessToken> builder)
    {
        builder.ToTable("RevokedCustomerAccessTokens");

        builder.HasKey(token => token.Id);

        // Jti is the primary lookup key on every authenticated request —
        // must be unique and indexed for O(1) revocation checks.
        // JWT IDs are UUIDs (36 chars); 128 gives headroom for any format variation.
        builder.Property(token => token.Jti)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(token => token.CustomerId)
            .IsRequired();

        builder.Property(token => token.RevokedAt)
            .IsRequired();

        // ExpiresAt is the scan predicate for cleanup; must be indexed so
        // CleanupExpiredTokensAsync does not table-scan as the blacklist grows.
        builder.Property(token => token.ExpiresAt)
            .IsRequired();

        builder.Property(token => token.Reason)
            .HasMaxLength(256);

        // Unique index on Jti — a JTI must never appear twice in the blacklist.
        // This is also the hot read path: IsTokenRevokedAsync filters by Jti.
        builder.HasIndex(token => token.Jti)
            .IsUnique()
            .HasDatabaseName("IX_RevokedCustomerAccessTokens_Jti");

        // Covering index for expiry-based cleanup scans.
        builder.HasIndex(token => token.ExpiresAt)
            .HasDatabaseName("IX_RevokedCustomerAccessTokens_ExpiresAt");

        // Supporting index for per-customer revocation queries (e.g. ban/logout-all).
        builder.HasIndex(token => token.CustomerId)
            .HasDatabaseName("IX_RevokedCustomerAccessTokens_CustomerId");
    }
}
