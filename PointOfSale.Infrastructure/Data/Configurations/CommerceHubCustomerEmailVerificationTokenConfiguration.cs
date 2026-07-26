using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CommerceHubCustomerEmailVerificationTokenConfiguration
    : IEntityTypeConfiguration<CommerceHubCustomerEmailVerificationToken>
{
    public void Configure(EntityTypeBuilder<CommerceHubCustomerEmailVerificationToken> builder)
    {
        builder.ToTable("CommerceHubCustomerEmailVerificationTokens");

        builder.HasKey(token => token.Id);

        builder.Property(token => token.CustomerId)
            .IsRequired();

        builder.Property(token => token.TokenHash)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(token => token.ExpiresAtUtc)
            .IsRequired();

        builder.Property(token => token.IsUsed)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(token => token.UsedAtUtc);

        builder.Property(token => token.CreatedAtUtc)
            .IsRequired();

        // Hot lookup: find valid tokens by hash for consumption.
        builder.HasIndex(token => token.TokenHash)
            .IsUnique()
            .HasDatabaseName("IX_CHCustomerEmailVerificationTokens_TokenHash");

        // Cleanup/invalidation: find all tokens for a customer.
        builder.HasIndex(token => token.CustomerId)
            .HasDatabaseName("IX_CHCustomerEmailVerificationTokens_CustomerId");

        builder.HasOne<CommerceHubCustomer>()
            .WithMany()
            .HasForeignKey(token => token.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
