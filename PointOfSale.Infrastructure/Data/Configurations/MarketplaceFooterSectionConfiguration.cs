using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class MarketplaceFooterSectionConfiguration : IEntityTypeConfiguration<MarketplaceFooterSection>
{
    public void Configure(EntityTypeBuilder<MarketplaceFooterSection> builder)
    {
        builder.ToTable("MarketplaceFooterSections");
        builder.HasKey(section => section.Id);

        builder.Property(section => section.Title)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(section => section.TitleJson)
            .HasColumnType("jsonb");

        builder.Property(section => section.SortOrder)
            .IsRequired();

        builder.Property(section => section.IsActive)
            .IsRequired();

        builder.Property(section => section.CreatedAt)
            .IsRequired();

        builder.Property(section => section.UpdatedAt)
            .IsRequired();

        builder.HasIndex(section => section.SortOrder)
            .HasDatabaseName("IX_MarketplaceFooterSections_SortOrder");

        builder.HasMany(section => section.Links)
            .WithOne(link => link.Section)
            .HasForeignKey(link => link.SectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
