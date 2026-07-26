using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class CmsPageConfiguration : IEntityTypeConfiguration<CmsPage>
{
    public void Configure(EntityTypeBuilder<CmsPage> builder)
    {
        builder.ToTable("CmsPages");
        builder.HasKey(page => page.Id);

        builder.Property(page => page.PageGroupId)
            .IsRequired();

        builder.Property(page => page.TenantId)
            .IsRequired();

        builder.Property(page => page.Slug)
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(page => page.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(page => page.Content)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(page => page.ContentType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(page => page.Version)
            .IsRequired();

        builder.Property(page => page.SchemaVersion)
            .IsRequired();

        builder.Property(page => page.IsDeleted)
            .IsRequired();

        builder.Property(page => page.IsDraft)
            .IsRequired();

        builder.Property(page => page.IsPublished)
            .IsRequired();

        builder.Property(page => page.PublishedAt);

        builder.Property(page => page.MetaTitle)
            .HasMaxLength(200);

        builder.Property(page => page.MetaDescription)
            .HasMaxLength(320);

        builder.Property(page => page.MetaKeywords)
            .HasMaxLength(500);

        builder.Property(page => page.OpenGraphImageUrl)
            .HasMaxLength(1000);

        builder.Property(page => page.CreatedAt)
            .IsRequired();

        builder.Property(page => page.UpdatedAt)
            .IsRequired();

        builder.HasIndex(page => new { page.PageGroupId, page.Version })
            .IsUnique()
            .HasDatabaseName("UQ_CmsPages_PageGroupId_Version");

        builder.HasIndex(page => page.PageGroupId)
            .IsUnique()
            .HasFilter("\"IsDraft\" AND NOT \"IsDeleted\"")
            .HasDatabaseName("UQ_CmsPages_PageGroupId_Draft");

        builder.HasIndex(page => page.PageGroupId)
            .IsUnique()
            .HasFilter("\"IsPublished\" AND NOT \"IsDeleted\"")
            .HasDatabaseName("UQ_CmsPages_PageGroupId_Published");

        builder.HasIndex(page => page.PageGroupId)
            .HasDatabaseName("IX_CmsPages_PageGroupId");

        builder.HasIndex(page => new { page.TenantId, page.Slug })
            .IsUnique()
            .HasFilter("\"IsDraft\" AND NOT \"IsDeleted\"")
            .HasDatabaseName("UQ_CmsPages_Tenant_Draft_Slug");

        builder.HasIndex(page => new { page.TenantId, page.Slug })
            .IsUnique()
            .HasFilter("\"IsPublished\" AND NOT \"IsDeleted\"")
            .HasDatabaseName("UQ_CmsPages_Tenant_Published_Slug");

        builder.HasIndex(page => new { page.TenantId, page.IsPublished, page.Slug })
            .HasDatabaseName("IX_CmsPages_TenantId_IsPublished_Slug");

        builder.HasIndex(page => new { page.TenantId, page.IsDraft, page.Slug })
            .HasDatabaseName("IX_CmsPages_TenantId_IsDraft_Slug");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_CmsPages_Version",
                "\"Version\" > 0");

            table.HasCheckConstraint(
                "CK_CmsPages_SchemaVersion",
                "\"SchemaVersion\" > 0");

            table.HasCheckConstraint(
                "CK_CmsPages_State",
                "NOT (\"IsDraft\" AND \"IsPublished\")");

            table.HasCheckConstraint(
                "CK_CmsPages_PublishedAt",
                "NOT \"IsPublished\" OR \"PublishedAt\" IS NOT NULL");

            table.HasCheckConstraint(
                "CK_CmsPages_ContentType",
                "\"ContentType\" IN ('RichText', 'Blocks')");
        });
    }
}
