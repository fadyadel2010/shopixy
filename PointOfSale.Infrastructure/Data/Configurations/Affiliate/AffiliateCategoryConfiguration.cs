using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.Affiliate;

namespace PointOfSale.Infrastructure.Data.Configurations.Affiliate;

/// <summary>
/// AF-30: EF Core configuration for AffiliateCategory.
///
/// CONSTRAINTS:
/// - Slug globally unique
/// - (ParentId + Name) unique
/// - Level CHECK: 1 or 2 only
/// - FK: ParentId → AffiliateCategories.Id (self-referencing)
/// </summary>
public class AffiliateCategoryConfiguration : IEntityTypeConfiguration<AffiliateCategory>
{
    public void Configure(EntityTypeBuilder<AffiliateCategory> builder)
    {
        builder.ToTable("AffiliateCategories");
        builder.HasKey(e => e.Id);

        // UNIQUE: Slug globally unique
        builder.HasIndex(e => e.Slug)
            .IsUnique()
            .HasDatabaseName("UQ_AffiliateCategories_Slug");

        // UNIQUE: (ParentId + Name) unique
        builder.HasIndex(e => new { e.ParentId, e.Name })
            .IsUnique()
            .HasDatabaseName("UQ_AffiliateCategories_Parent_Name");

        // INDEX: ParentId for hierarchy queries
        builder.HasIndex(e => e.ParentId)
            .HasDatabaseName("IX_AffiliateCategories_ParentId");

        // INDEX: IsActive for filtering
        builder.HasIndex(e => e.IsActive)
            .HasDatabaseName("IX_AffiliateCategories_IsActive");

        // INDEX: DisplayOrder for sorting
        builder.HasIndex(e => e.DisplayOrder)
            .HasDatabaseName("IX_AffiliateCategories_DisplayOrder");

        // Properties
        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Slug)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Level)
            .IsRequired();

        builder.Property(e => e.IsActive)
            .IsRequired();

        builder.Property(e => e.DisplayOrder)
            .IsRequired();

        // CHECK: Level must be 1 or 2
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_AffiliateCategories_Level",
            "\"Level\" IN (1, 2)"));

        // CHECK: Level 1 must have ParentId = null
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_AffiliateCategories_Level1_NoParent",
            "(\"Level\" = 1 AND \"ParentId\" IS NULL) OR (\"Level\" = 2 AND \"ParentId\" IS NOT NULL)"));

        // FK: Self-referencing parent category
        builder.HasOne<AffiliateCategory>()
            .WithMany()
            .HasForeignKey(e => e.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
