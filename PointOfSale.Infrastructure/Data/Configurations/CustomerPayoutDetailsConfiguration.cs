using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CustomerPayoutDetailsConfiguration : IEntityTypeConfiguration<CustomerPayoutDetails>
{
    public void Configure(EntityTypeBuilder<CustomerPayoutDetails> builder)
    {
        builder.ToTable("CommerceHubCustomerPayoutDetails");

        builder.HasKey(payoutDetails => payoutDetails.Id);

        builder.Property(payoutDetails => payoutDetails.Method)
            .IsRequired();

        builder.Property(payoutDetails => payoutDetails.AccountIdentifier)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(payoutDetails => payoutDetails.AccountHolderName)
            .HasMaxLength(200);

        builder.Property(payoutDetails => payoutDetails.IsVerified)
            .HasDefaultValue(false);

        builder.HasIndex(payoutDetails => new { payoutDetails.CustomerId, payoutDetails.CreatedAt })
            .HasDatabaseName("IX_CommerceHubCustomerPayoutDetails_CustomerId_CreatedAt");

        builder.HasIndex(payoutDetails => new { payoutDetails.CustomerId, payoutDetails.Method, payoutDetails.CreatedAt })
            .HasDatabaseName("IX_CommerceHubCustomerPayoutDetails_CustomerId_Method_CreatedAt");

        builder.HasOne(payoutDetails => payoutDetails.Customer)
            .WithMany(customer => customer.PayoutDetails)
            .HasForeignKey(payoutDetails => payoutDetails.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
