using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CommerceHubSearchImpressionConfiguration : IEntityTypeConfiguration<CommerceHubSearchImpression>
{
    public void Configure(EntityTypeBuilder<CommerceHubSearchImpression> builder)
    {
        builder.ToTable("SearchImpressions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.QueryText)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.SessionId)
            .HasMaxLength(100);

        builder.Property(x => x.Position)
            .IsRequired();

        builder.Property(x => x.ImpressedAt)
            .IsRequired();

        builder.HasIndex(x => new { x.QueryText, x.ImpressedAt })
            .HasDatabaseName("IX_SearchImpressions_QueryText_ImpressedAt");

        builder.HasIndex(x => new { x.ProductId, x.ImpressedAt })
            .HasDatabaseName("IX_SearchImpressions_ProductId_ImpressedAt");

        builder.HasIndex(x => new { x.TenantId, x.ImpressedAt })
            .HasDatabaseName("IX_SearchImpressions_TenantId_ImpressedAt");
    }
}
