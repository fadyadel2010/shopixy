using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CommerceHubListingConfiguration : IEntityTypeConfiguration<CommerceHubListing>
{
    public void Configure(EntityTypeBuilder<CommerceHubListing> builder)
    {
        builder.ToTable("CommerceHubListings", table =>
        {
            table.HasCheckConstraint(
                "CK_CommerceHubListings_CommerceHubPrice_Positive",
                "\"CommerceHubPrice\" IS NULL OR \"CommerceHubPrice\" > 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId)
            .IsRequired();

        builder.Property(x => x.ProductId)
            .IsRequired();

        builder.Property(x => x.VariantId)
            .IsRequired();

        builder.Property(x => x.CommerceHubCategoryId)
            .IsRequired();

        builder.Property(x => x.CommerceHubPrice)
            .HasPrecision(18, 2);

        builder.Property(x => x.CommerceHubPriceOverrideUpdatedAtUtc);

        builder.Property(x => x.CommerceHubPriceOverrideUpdatedBy);

        builder.Property(x => x.CommerceHubPriceOverrideReason)
            .HasMaxLength(1024);

        builder.Property(x => x.CommerceHubPriceOverrideMutationVersion)
            .IsRequired();

        builder.Property(x => x.IsPublished)
            .IsRequired();

        builder.Property(x => x.RestorePublicationOnActivate)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.AttributesJson)
            .HasColumnType("text");

        builder.Property(x => x.AttributeSchemaSnapshotJson)
            .HasColumnType("text");

        builder.HasIndex(x => new { x.TenantId, x.VariantId })
            .IsUnique()
            .HasDatabaseName("UQ_CommerceHubListings_TenantId_VariantId");

        builder.HasIndex(x => new { x.TenantId, x.IsPublished, x.CommerceHubCategoryId })
            .HasDatabaseName("IX_CommerceHubListings_TenantId_IsPublished_CategoryId");

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(x => x.VariantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
