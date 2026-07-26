using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class RevokedStorefrontAccessTokenConfiguration : IEntityTypeConfiguration<RevokedStorefrontAccessToken>
{
    public void Configure(EntityTypeBuilder<RevokedStorefrontAccessToken> builder)
    {
        builder.ToTable("RevokedStorefrontAccessTokens");

        builder.HasKey(token => token.Id);

        builder.Property(token => token.TenantId)
            .IsRequired();

        builder.Property(token => token.Jti)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(token => token.RevokedAtUtc)
            .IsRequired();

        builder.Property(token => token.ExpiresAtUtc)
            .IsRequired();

        builder.Property(token => token.Reason)
            .HasMaxLength(256);

        builder.HasIndex(token => new { token.TenantId, token.Jti })
            .IsUnique()
            .HasDatabaseName("IX_RevokedStorefrontAccessTokens_TenantId_Jti");

        builder.HasIndex(token => token.ExpiresAtUtc)
            .HasDatabaseName("IX_RevokedStorefrontAccessTokens_ExpiresAtUtc");

        builder.HasIndex(token => new { token.TenantId, token.OnlineCustomerId })
            .HasDatabaseName("IX_RevokedStorefrontAccessTokens_TenantId_OnlineCustomerId");

        builder.HasOne(token => token.OnlineCustomer)
            .WithMany()
            .HasForeignKey(token => token.OnlineCustomerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
