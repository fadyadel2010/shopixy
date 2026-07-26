using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CommerceHubSearchClickConfiguration : IEntityTypeConfiguration<CommerceHubSearchClick>
{
    public void Configure(EntityTypeBuilder<CommerceHubSearchClick> builder)
    {
        builder.ToTable("SearchClicks");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.QueryText)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.SessionId)
            .HasMaxLength(100);

        builder.Property(x => x.Position)
            .IsRequired();

        builder.Property(x => x.ClickedAt)
            .IsRequired();

        builder.HasIndex(x => new { x.QueryText, x.ClickedAt })
            .HasDatabaseName("IX_SearchClicks_QueryText_ClickedAt");

        builder.HasIndex(x => new { x.ProductId, x.ClickedAt })
            .HasDatabaseName("IX_SearchClicks_ProductId_ClickedAt");

        builder.HasIndex(x => new { x.TenantId, x.ClickedAt })
            .HasDatabaseName("IX_SearchClicks_TenantId_ClickedAt");
    }
}
