using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class PhoneRiskProfileConfiguration : IEntityTypeConfiguration<PhoneRiskProfile>
{
    public void Configure(EntityTypeBuilder<PhoneRiskProfile> builder)
    {
        builder.ToTable("PhoneRiskProfiles");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId)
            .IsRequired();

        builder.Property(x => x.PhoneNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(x => x.SpamReports)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(x => x.IsBlocked)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.BlockedReason)
            .HasMaxLength(500);

        builder.Property(x => x.BlockedByUserId);

        builder.Property(x => x.BlockedAtUtc);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.UpdatedAtUtc)
            .IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.PhoneNumber })
            .IsUnique()
            .HasDatabaseName("IX_PhoneRiskProfiles_TenantId_PhoneNumber");

        builder.HasIndex(x => new { x.TenantId, x.IsBlocked, x.BlockedAtUtc })
            .HasDatabaseName("IX_PhoneRiskProfiles_TenantId_IsBlocked_BlockedAtUtc");
    }
}
