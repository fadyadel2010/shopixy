using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class GlobalPhoneReputationConfiguration : IEntityTypeConfiguration<GlobalPhoneReputation>
{
    public void Configure(EntityTypeBuilder<GlobalPhoneReputation> builder)
    {
        builder.ToTable("GlobalPhoneReputation");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PhoneNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(x => x.SpamReports)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(x => x.FirstReportedAtUtc)
            .IsRequired();

        builder.Property(x => x.LastReportedAtUtc)
            .IsRequired();

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.UpdatedAtUtc)
            .IsRequired();

        builder.HasIndex(x => x.PhoneNumber)
            .IsUnique()
            .HasDatabaseName("IX_GlobalPhoneReputation_PhoneNumber");
    }
}
