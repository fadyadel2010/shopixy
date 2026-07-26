using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CommerceHubSearchFilterEventConfiguration : IEntityTypeConfiguration<CommerceHubSearchFilterEvent>
{
    public void Configure(EntityTypeBuilder<CommerceHubSearchFilterEvent> builder)
    {
        builder.ToTable("SearchFilterEvents");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.FilterKey)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.FilterValue)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.QueryText)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.SessionId)
            .HasMaxLength(100);

        builder.Property(x => x.OccurredAt)
            .IsRequired();

        builder.HasIndex(x => new { x.FilterKey, x.OccurredAt })
            .HasDatabaseName("IX_SearchFilterEvents_FilterKey_OccurredAt");

        builder.HasIndex(x => new { x.QueryText, x.OccurredAt })
            .HasDatabaseName("IX_SearchFilterEvents_QueryText_OccurredAt");

        builder.HasIndex(x => new { x.TenantId, x.OccurredAt })
            .HasDatabaseName("IX_SearchFilterEvents_TenantId_OccurredAt");
    }
}
