using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class StorefrontFooterSectionConfiguration : IEntityTypeConfiguration<StorefrontFooterSection>
{
    public void Configure(EntityTypeBuilder<StorefrontFooterSection> builder)
    {
        builder.ToTable("StorefrontFooterSections");
        builder.HasKey(section => section.Id);

        builder.Property(section => section.Title)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(section => section.TitleAr)
            .HasMaxLength(120)
            .IsRequired(false);

        builder.Property(section => section.SortOrder)
            .IsRequired();

        builder.Property(section => section.IsActive)
            .IsRequired();

        builder.Property(section => section.CreatedAt)
            .IsRequired();

        builder.Property(section => section.UpdatedAt)
            .IsRequired();

        builder.HasIndex(section => new { section.TenantId, section.SortOrder })
            .HasDatabaseName("IX_StorefrontFooterSections_TenantId_SortOrder");

        builder.HasMany(section => section.Links)
            .WithOne(link => link.Section)
            .HasForeignKey(link => link.SectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
