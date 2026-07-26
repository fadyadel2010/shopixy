using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.Affiliate;

namespace PointOfSale.Infrastructure.Data.Configurations.Affiliate;

public class AffiliateRefreshTokenReplayEntryConfiguration : IEntityTypeConfiguration<AffiliateRefreshTokenReplayEntry>
{
    public void Configure(EntityTypeBuilder<AffiliateRefreshTokenReplayEntry> builder)
    {
        builder.ToTable("AffiliateRefreshTokenReplayEntries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.TokenHash)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(e => e.TokenHash)
            .IsUnique()
            .HasDatabaseName("IX_AffiliateRefreshTokenReplayEntries_TokenHash");

        builder.HasIndex(e => new { e.AffiliateUserId, e.ExpiresAtUtc })
            .HasDatabaseName("IX_AffiliateRefreshTokenReplayEntries_User_Expires");

        builder.HasIndex(e => e.ReplayedAtUtc)
            .HasDatabaseName("IX_AffiliateRefreshTokenReplayEntries_ReplayedAtUtc");

        builder.HasOne(e => e.AffiliateUser)
            .WithMany()
            .HasForeignKey(e => e.AffiliateUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
