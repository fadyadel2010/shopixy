using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CommerceHubSearchSynonymConfiguration : IEntityTypeConfiguration<CommerceHubSearchSynonym>
{
    public void Configure(EntityTypeBuilder<CommerceHubSearchSynonym> builder)
    {
        builder.ToTable("SearchSynonyms");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Term)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.SynonymsJson)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.Direction)
            .HasMaxLength(20)
            .HasDefaultValue(CommerceHubSearchSynonym.OneWayDirection)
            .IsRequired();

        builder.HasIndex(x => x.Term)
            .IsUnique()
            .HasDatabaseName("UQ_SearchSynonyms_Term");
    }
}
