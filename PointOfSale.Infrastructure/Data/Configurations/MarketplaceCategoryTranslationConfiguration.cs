using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class MarketplaceCategoryTranslationConfiguration : IEntityTypeConfiguration<MarketplaceCategoryTranslation>
{
    public void Configure(EntityTypeBuilder<MarketplaceCategoryTranslation> builder)
    {
        builder.ToTable("MarketplaceCategoryTranslations");
        builder.HasKey(translation => translation.Id);

        builder.Property(translation => translation.CategoryId)
            .IsRequired();

        builder.Property(translation => translation.Language)
            .HasMaxLength(5)
            .IsRequired();

        builder.Property(translation => translation.Name)
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(translation => translation.Slug)
            .HasMaxLength(180);

        builder.Property(translation => translation.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(translation => new { translation.CategoryId, translation.Language })
            .IsUnique()
            .HasDatabaseName("UQ_MarketplaceCategoryTranslations_CategoryId_Language");

        builder.HasIndex(translation => translation.Language)
            .HasDatabaseName("IX_MarketplaceCategoryTranslations_Language");

        builder.HasOne(translation => translation.Category)
            .WithMany()
            .HasForeignKey(translation => translation.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
