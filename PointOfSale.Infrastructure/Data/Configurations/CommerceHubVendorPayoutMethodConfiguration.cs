using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.CommerceHubFinance;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class CommerceHubVendorPayoutMethodConfiguration : IEntityTypeConfiguration<CommerceHubVendorPayoutMethod>
{
    public void Configure(EntityTypeBuilder<CommerceHubVendorPayoutMethod> builder)
    {
        builder.ToTable("CommerceHubVendorPayoutMethods");
        builder.HasKey(method => method.Id);

        builder.Property(method => method.DestinationValue)
            .HasMaxLength(200)
            .IsRequired();

        builder.HasIndex(method => new { method.VendorId, method.CreatedAt })
            .HasDatabaseName("IX_CommerceHubVendorPayoutMethods_VendorId_CreatedAt");

        builder.HasIndex(method => new { method.VendorId, method.MethodType, method.DestinationValue })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false")
            .HasDatabaseName("UX_CommerceHubVendorPayoutMethods_VendorId_MethodType_DestinationValue");

        builder.HasIndex(method => method.VendorId)
            .HasFilter("\"IsDeleted\" = false AND \"IsDefault\" = true")
            .IsUnique()
            .HasDatabaseName("UX_CommerceHubVendorPayoutMethods_VendorId_Default");
    }
}
