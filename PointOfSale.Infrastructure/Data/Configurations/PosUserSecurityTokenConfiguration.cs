using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for the purpose-bound POS user security token
/// authority (invitation and password-reset ceremonies).
/// </summary>
public class PosUserSecurityTokenConfiguration : IEntityTypeConfiguration<PosUserSecurityToken>
{
    public void Configure(EntityTypeBuilder<PosUserSecurityToken> builder)
    {
        builder.ToTable("PosUserSecurityTokens", table =>
        {
            table.HasCheckConstraint(
                "CK_PosUserSecurityTokens_ExpiryAfterCreation",
                "\"ExpiresAtUtc\" > \"CreatedAtUtc\"");
            table.HasCheckConstraint(
                "CK_PosUserSecurityTokens_ConsumedNotBeforeCreation",
                "\"ConsumedAtUtc\" IS NULL OR \"ConsumedAtUtc\" >= \"CreatedAtUtc\"");
            table.HasCheckConstraint(
                "CK_PosUserSecurityTokens_RevokedNotBeforeCreation",
                "\"RevokedAtUtc\" IS NULL OR \"RevokedAtUtc\" >= \"CreatedAtUtc\"");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.TenantId)
            .IsRequired();

        builder.Property(e => e.UserId)
            .IsRequired();

        builder.Property(e => e.Purpose)
            .IsRequired();

        builder.Property(e => e.TokenHash)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(e => e.ExpiresAtUtc)
            .IsRequired();

        builder.Property(e => e.CreatedAtUtc)
            .IsRequired();

        // Unique token hash: raw tokens are high-entropy, hash-only persisted.
        builder.HasIndex(e => e.TokenHash)
            .IsUnique()
            .HasDatabaseName("IX_PosUserSecurityTokens_TokenHash");

        builder.HasIndex(e => new { e.TenantId, e.UserId, e.Purpose, e.ExpiresAtUtc })
            .HasDatabaseName("IX_PosUserSecurityTokens_TenantUserPurposeExpiry");

        builder.HasIndex(e => e.ExpiresAtUtc)
            .HasDatabaseName("IX_PosUserSecurityTokens_ExpiresAtUtc");

        // Restrictive user FK: token rows are security evidence and must not
        // cascade-delete when a user row changes.
        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
