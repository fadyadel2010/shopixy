using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CommerceHubCustomerIdentityEventConfiguration
    : IEntityTypeConfiguration<CommerceHubCustomerIdentityEvent>
{
    public void Configure(EntityTypeBuilder<CommerceHubCustomerIdentityEvent> builder)
    {
        builder.ToTable("CommerceHubCustomerIdentityEvents");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.CustomerId)
            .IsRequired();

        builder.Property(e => e.EventType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Description)
            .HasMaxLength(500);

        builder.Property(e => e.Metadata)
            .HasMaxLength(2000);

        builder.Property(e => e.IpAddress)
            .HasMaxLength(45);

        builder.Property(e => e.OccurredAtUtc)
            .IsRequired();

        // Per-customer event history for incident reconstruction.
        builder.HasIndex(e => new { e.CustomerId, e.OccurredAtUtc })
            .HasDatabaseName("IX_CHCustomerIdentityEvents_Customer_Occurred");

        // Event-type scans for monitoring dashboards.
        builder.HasIndex(e => new { e.EventType, e.OccurredAtUtc })
            .HasDatabaseName("IX_CHCustomerIdentityEvents_EventType_Occurred");

        builder.HasOne<CommerceHubCustomer>()
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
