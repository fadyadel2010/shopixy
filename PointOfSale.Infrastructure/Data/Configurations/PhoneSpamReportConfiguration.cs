using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class PhoneSpamReportConfiguration : IEntityTypeConfiguration<PhoneSpamReport>
{
    public void Configure(EntityTypeBuilder<PhoneSpamReport> builder)
    {
        builder.ToTable("PhoneSpamReports");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PhoneNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(x => x.TenantId)
            .IsRequired();

        builder.Property(x => x.OrderId)
            .IsRequired();

        builder.Property(x => x.ReportedByUserId);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.OrderId })
            .IsUnique()
            .HasDatabaseName("IX_PhoneSpamReports_TenantId_OrderId");

        builder.HasIndex(x => x.PhoneNumber)
            .HasDatabaseName("IX_PhoneSpamReports_PhoneNumber");
    }
}
