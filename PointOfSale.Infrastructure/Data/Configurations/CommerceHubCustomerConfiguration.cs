using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CommerceHubCustomerConfiguration : IEntityTypeConfiguration<CommerceHubCustomer>
{
    public void Configure(EntityTypeBuilder<CommerceHubCustomer> builder)
    {
        builder.HasKey(customer => customer.Id);

        builder.Property(customer => customer.Email)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(customer => customer.PasswordHash)
            .HasMaxLength(500);

        builder.Property(customer => customer.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(customer => customer.PhoneNumber)
            .HasMaxLength(50);

        builder.Property(customer => customer.TokensValidAfterUtc);

        builder.Property(customer => customer.IsEmailVerified)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(customer => customer.EmailVerifiedAtUtc);

        builder.HasIndex(customer => customer.Email)
            .IsUnique()
            .HasDatabaseName("IX_CommerceHubCustomers_Email");

        builder.HasIndex(customer => customer.CreatedAt)
            .HasDatabaseName("IX_CommerceHubCustomers_CreatedAt");
    }
}
