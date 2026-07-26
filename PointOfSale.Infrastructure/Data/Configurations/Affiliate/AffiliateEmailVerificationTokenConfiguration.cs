using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.Affiliate;

namespace PointOfSale.Infrastructure.Data.Configurations.Affiliate;

public class AffiliateEmailVerificationTokenConfiguration : IEntityTypeConfiguration<AffiliateEmailVerificationToken>
{
    public void Configure(EntityTypeBuilder<AffiliateEmailVerificationToken> builder)
    {
        builder.ToTable("AffiliateEmailVerificationTokens");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.TokenHash)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(e => e.TokenHash)
            .IsUnique()
            .HasDatabaseName("IX_AffiliateEmailVerificationTokens_TokenHash");

        builder.HasIndex(e => new { e.AffiliateUserId, e.ConsumedAtUtc })
            .HasDatabaseName("IX_AffiliateEmailVerificationTokens_User_Consumed");

        builder.HasIndex(e => e.ExpiresAtUtc)
            .HasDatabaseName("IX_AffiliateEmailVerificationTokens_ExpiresAtUtc");

        builder.HasOne(e => e.AffiliateUser)
            .WithMany()
            .HasForeignKey(e => e.AffiliateUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
