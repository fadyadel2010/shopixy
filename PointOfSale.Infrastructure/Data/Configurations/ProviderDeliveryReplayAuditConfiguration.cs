using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class ProviderDeliveryReplayAuditConfiguration : IEntityTypeConfiguration<ProviderDeliveryReplayAudit>
{
    public void Configure(EntityTypeBuilder<ProviderDeliveryReplayAudit> builder)
    {
        builder.ToTable("ProviderDeliveryReplayAudits");

        builder.HasKey(audit => audit.Id);

        builder.Property(audit => audit.Provider)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(audit => audit.OperatorUserId)
            .HasMaxLength(100);

        builder.Property(audit => audit.Reason)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(audit => audit.ReplaySource)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(audit => audit.PreviousStateJson)
            .IsRequired();

        builder.Property(audit => audit.NewStateJson)
            .IsRequired();

        builder.HasIndex(audit => audit.ReplayId)
            .IsUnique()
            .HasDatabaseName("IX_ProviderDeliveryReplayAudits_ReplayId");

        builder.HasIndex(audit => new { audit.TenantId, audit.MessageId, audit.ReplayedAtUtc })
            .HasDatabaseName("IX_ProviderDeliveryReplayAudits_Tenant_Message_Time");
    }
}
