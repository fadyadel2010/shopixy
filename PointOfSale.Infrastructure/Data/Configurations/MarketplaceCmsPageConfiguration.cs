using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class MarketplaceCmsPageConfiguration : IEntityTypeConfiguration<MarketplaceCmsPage>
{
    public void Configure(EntityTypeBuilder<MarketplaceCmsPage> builder)
    {
        builder.ToTable("MarketplaceCmsPages");
        builder.HasKey(page => page.Id);

        builder.Property(page => page.PageGroupId)
            .IsRequired();

        builder.Property(page => page.Slug)
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(page => page.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(page => page.TitleJson)
            .HasColumnType("jsonb");

        builder.Property(page => page.Content)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(page => page.ContentJson)
            .HasColumnType("jsonb");

        builder.Property(page => page.ContentType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(page => page.Version)
            .IsRequired();

        builder.Property(page => page.IsDraft)
            .IsRequired();

        builder.Property(page => page.IsPublished)
            .IsRequired();

        builder.Property(page => page.PublishedAt);

        builder.Property(page => page.MetaTitle)
            .HasMaxLength(200);

        builder.Property(page => page.MetaTitleJson)
            .HasColumnType("jsonb");

        builder.Property(page => page.MetaDescription)
            .HasMaxLength(320);

        builder.Property(page => page.MetaDescriptionJson)
            .HasColumnType("jsonb");

        builder.Property(page => page.OpenGraphImageUrl)
            .HasMaxLength(1000);

        builder.Property(page => page.CreatedAt)
            .IsRequired();

        builder.Property(page => page.UpdatedAt)
            .IsRequired();

        builder.HasIndex(page => new { page.PageGroupId, page.Version })
            .IsUnique()
            .HasDatabaseName("UQ_MarketplaceCmsPages_PageGroupId_Version");

        builder.HasIndex(page => page.PageGroupId)
            .HasDatabaseName("IX_MarketplaceCmsPages_PageGroupId");

        builder.HasIndex(page => new { page.IsPublished, page.Slug })
            .HasDatabaseName("IX_MarketplaceCmsPages_IsPublished_Slug");

        builder.HasIndex(page => new { page.IsDraft, page.Slug })
            .HasDatabaseName("IX_MarketplaceCmsPages_IsDraft_Slug");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_MarketplaceCmsPages_Version",
                "\"Version\" > 0");

            table.HasCheckConstraint(
                "CK_MarketplaceCmsPages_State",
                "NOT (\"IsDraft\" AND \"IsPublished\")");

            table.HasCheckConstraint(
                "CK_MarketplaceCmsPages_PublishedAt",
                "NOT \"IsPublished\" OR \"PublishedAt\" IS NOT NULL");

            table.HasCheckConstraint(
                "CK_MarketplaceCmsPages_ContentType",
                "\"ContentType\" IN ('RichText', 'Blocks')");
        });
    }
}
