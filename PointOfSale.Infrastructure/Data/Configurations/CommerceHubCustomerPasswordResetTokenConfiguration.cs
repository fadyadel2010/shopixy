using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CommerceHubCustomerPasswordResetTokenConfiguration
    : IEntityTypeConfiguration<CommerceHubCustomerPasswordResetToken>
{
    public void Configure(EntityTypeBuilder<CommerceHubCustomerPasswordResetToken> builder)
    {
        builder.ToTable("CommerceHubCustomerPasswordResetTokens");

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

        builder.Property(token => token.CreatedByIp)
            .HasMaxLength(45);

        // Hot lookup: find valid tokens by hash for consumption.
        builder.HasIndex(token => token.TokenHash)
            .IsUnique()
            .HasDatabaseName("IX_CHCustomerPasswordResetTokens_TokenHash");

        // Cleanup/invalidation: find all tokens for a customer.
        builder.HasIndex(token => token.CustomerId)
            .HasDatabaseName("IX_CHCustomerPasswordResetTokens_CustomerId");

        builder.HasOne<CommerceHubCustomer>()
            .WithMany()
            .HasForeignKey(token => token.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
