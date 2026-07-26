using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.CommerceHubFinance;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CommerceHubVendorPaymentMethodConfiguration : IEntityTypeConfiguration<CommerceHubVendorPaymentMethod>
{
    public void Configure(EntityTypeBuilder<CommerceHubVendorPaymentMethod> builder)
    {
        builder.ToTable("CommerceHubVendorPaymentMethods");
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Name)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(entry => entry.DestinationValue)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(entry => entry.Instructions)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(entry => entry.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(entry => entry.IsActive)
            .HasDatabaseName("IX_CommerceHubVendorPaymentMethods_IsActive");

        builder.HasIndex(entry => new { entry.Name, entry.DestinationValue })
            .IsUnique()
            .HasDatabaseName("UX_CommerceHubVendorPaymentMethods_Name_DestinationValue");
    }
}
