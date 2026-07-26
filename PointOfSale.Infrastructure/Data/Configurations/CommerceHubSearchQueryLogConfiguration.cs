using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CommerceHubSearchQueryLogConfiguration : IEntityTypeConfiguration<CommerceHubSearchQueryLog>
{
    public void Configure(EntityTypeBuilder<CommerceHubSearchQueryLog> builder)
    {
        builder.ToTable("SearchQueries");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.QueryText)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.Intent)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.SearchedAt)
            .IsRequired();

        builder.HasIndex(x => new { x.QueryText, x.SearchedAt })
            .HasDatabaseName("IX_SearchQueries_QueryText_SearchedAt");

        builder.HasIndex(x => new { x.ResultCount, x.SearchedAt })
            .HasDatabaseName("IX_SearchQueries_ResultCount_SearchedAt");
    }
}
