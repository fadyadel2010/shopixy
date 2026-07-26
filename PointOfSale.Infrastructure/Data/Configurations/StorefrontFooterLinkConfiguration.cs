using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class StorefrontFooterLinkConfiguration : IEntityTypeConfiguration<StorefrontFooterLink>
{
    public void Configure(EntityTypeBuilder<StorefrontFooterLink> builder)
    {
        builder.ToTable("StorefrontFooterLinks");
        builder.HasKey(link => link.Id);

        builder.Property(link => link.Label)
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(link => link.LabelAr)
            .HasMaxLength(160);

        builder.Property(link => link.LinkType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(link => link.Url)
            .HasMaxLength(1000);

        builder.Property(link => link.CmsPageSlug)
            .HasMaxLength(160);

        builder.Property(link => link.SortOrder)
            .IsRequired();

        builder.Property(link => link.OpenInNewTab)
            .IsRequired();

        builder.Property(link => link.IsActive)
            .IsRequired();

        builder.Property(link => link.CreatedAt)
            .IsRequired();

        builder.Property(link => link.UpdatedAt)
            .IsRequired();

        builder.HasIndex(link => new { link.SectionId, link.SortOrder })
            .HasDatabaseName("IX_StorefrontFooterLinks_SectionId_SortOrder");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_StorefrontFooterLinks_LinkType",
                "\"LinkType\" IN ('Internal', 'External', 'Cms')");

            table.HasCheckConstraint(
                "CK_StorefrontFooterLinks_Target",
                "(\"LinkType\" = 'Cms' AND \"CmsPageSlug\" IS NOT NULL AND LENGTH(TRIM(\"CmsPageSlug\")) > 0) OR " +
                "(\"LinkType\" <> 'Cms' AND \"Url\" IS NOT NULL AND LENGTH(TRIM(\"Url\")) > 0)");
        });
    }
}
