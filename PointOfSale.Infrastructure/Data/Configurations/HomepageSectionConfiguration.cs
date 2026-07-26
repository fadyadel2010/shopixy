using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class HomepageSectionConfiguration : IEntityTypeConfiguration<HomepageSection>
{
    public void Configure(EntityTypeBuilder<HomepageSection> builder)
    {
        builder.ToTable("HomepageSections");
        builder.HasKey(section => section.Id);

        builder.Property(section => section.SectionGroupId)
            .IsRequired();

        builder.Property(section => section.Type)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(section => section.Order)
            .HasColumnName("Order")
            .IsRequired();

        builder.Property(section => section.Data)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(section => section.Version)
            .IsRequired();

        builder.Property(section => section.SchemaVersion)
            .IsRequired();

        builder.Property(section => section.IsActive)
            .IsRequired();

        builder.Property(section => section.IsDeleted)
            .IsRequired();

        builder.Property(section => section.IsDraft)
            .IsRequired();

        builder.Property(section => section.IsPublished)
            .IsRequired();

        builder.Property(section => section.PublishedAt);

        builder.Property(section => section.DraftGroupId);

        builder.Property(section => section.CreatedAt)
            .IsRequired();

        builder.Property(section => section.UpdatedAt)
            .IsRequired();

        builder.HasIndex(section => new { section.SectionGroupId, section.Version })
            .IsUnique()
            .HasDatabaseName("UQ_HomepageSections_SectionGroupId_Version");

        builder.HasIndex(section => section.SectionGroupId)
            .HasDatabaseName("IX_HomepageSections_SectionGroupId");

        builder.HasIndex(section => new { section.TenantId, section.IsPublished, section.Order })
            .HasDatabaseName("IX_HomepageSections_TenantId_IsPublished_Order");

        builder.HasIndex(section => new { section.TenantId, section.IsDraft, section.Order })
            .HasDatabaseName("IX_HomepageSections_TenantId_IsDraft_Order");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_HomepageSections_Order",
                "\"Order\" >= 0");

            table.HasCheckConstraint(
                "CK_HomepageSections_Type",
                "\"Type\" IN ('hero', 'products', 'banner', 'categories')");

            table.HasCheckConstraint(
                "CK_HomepageSections_Version",
                "\"Version\" > 0");

            table.HasCheckConstraint(
                "CK_HomepageSections_SchemaVersion",
                "\"SchemaVersion\" > 0");

            table.HasCheckConstraint(
                "CK_HomepageSections_State",
                "NOT (\"IsDraft\" AND \"IsPublished\")");
        });
    }
}
