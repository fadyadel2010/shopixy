using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class ServiceRequestConfiguration : IEntityTypeConfiguration<ServiceRequest>
{
    public void Configure(EntityTypeBuilder<ServiceRequest> builder)
    {
        builder.ToTable("ServiceRequests");

        builder.Property(r => r.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(r => r.ServiceTitleSnapshot)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.ServiceCategorySnapshot)
            .HasMaxLength(100);

        builder.Property(r => r.ServiceCurrencySnapshot)
            .HasMaxLength(3);

        builder.Property(r => r.ServicePackagesSnapshot)
            .HasColumnType("text");

        builder.HasIndex(r => new { r.TenantId, r.ServiceId, r.BuyerUserId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = FALSE AND \"Status\" IN ('Pending', 'Accepted', 'CompletedByProvider')")
            .HasDatabaseName("UX_ServiceRequests_Active_Service_Buyer");

        builder.HasIndex(r => new { r.TenantId, r.ProviderUserId, r.Status });
        builder.HasIndex(r => new { r.TenantId, r.BuyerUserId, r.Status });
    }
}
