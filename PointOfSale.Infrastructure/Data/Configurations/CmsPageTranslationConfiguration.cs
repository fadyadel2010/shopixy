using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class CmsPageTranslationConfiguration : IEntityTypeConfiguration<CmsPageTranslation>
{
    public void Configure(EntityTypeBuilder<CmsPageTranslation> builder)
    {
        builder.ToTable("CmsPageTranslations");
        builder.HasKey(translation => new { translation.CmsPageVersionId, translation.Locale });

        builder.Property(translation => translation.Locale)
            .HasMaxLength(10)
            .IsRequired();
        builder.Property(translation => translation.Title)
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(translation => translation.Content)
            .HasColumnType("text")
            .IsRequired();
        builder.Property(translation => translation.ContentType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(translation => translation.SchemaVersion)
            .IsRequired();
        builder.Property(translation => translation.MetaTitle)
            .HasMaxLength(200);
        builder.Property(translation => translation.MetaDescription)
            .HasMaxLength(320);
        builder.Property(translation => translation.MetaKeywords)
            .HasMaxLength(500);
        builder.Property(translation => translation.OpenGraphImageUrl)
            .HasMaxLength(1000);

        builder.HasOne(translation => translation.PageVersion)
            .WithMany(page => page.Translations)
            .HasForeignKey(translation => new { translation.CmsPageVersionId, translation.TenantId })
            .HasPrincipalKey(page => new { page.Id, page.TenantId })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(translation => new
            {
                translation.TenantId,
                translation.Locale,
                translation.CmsPageVersionId
            })
            .HasDatabaseName("IX_CmsPageTranslations_TenantId_Locale_CmsPageVersionId");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_CmsPageTranslations_Locale",
                "\"Locale\" IN ('en', 'ar')");
            table.HasCheckConstraint(
                "CK_CmsPageTranslations_ContentType",
                "\"ContentType\" IN ('RichText', 'Blocks')");
            table.HasCheckConstraint(
                "CK_CmsPageTranslations_SchemaVersion",
                "\"SchemaVersion\" > 0");
        });
    }
}
