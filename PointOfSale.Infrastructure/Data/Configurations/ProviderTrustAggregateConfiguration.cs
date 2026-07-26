using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class ProviderTrustAggregateConfiguration : IEntityTypeConfiguration<ProviderTrustAggregate>
{
    public void Configure(EntityTypeBuilder<ProviderTrustAggregate> builder)
    {
        builder.ToTable("ProviderTrustAggregates");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.AverageRating)
            .HasPrecision(3, 2);

        builder.Property(t => t.Version)
            .IsConcurrencyToken();

        builder.HasOne(t => t.ProviderProfile)
            .WithMany()
            .HasForeignKey(t => t.ProviderProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.TenantId);
        builder.HasIndex(t => t.ProviderProfileId)
            .IsUnique()
            .HasDatabaseName("UQ_ProviderTrustAggregates_ProviderProfile");
        builder.HasIndex(t => t.AverageRating)
            .IsDescending()
            .HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(t => t.TotalReviews)
            .IsDescending()
            .HasFilter("\"IsDeleted\" = false");
    }
}
