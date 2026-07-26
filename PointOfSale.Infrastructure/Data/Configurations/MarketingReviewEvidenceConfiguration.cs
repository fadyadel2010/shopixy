using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class MarketingReviewEvidenceConfiguration : IEntityTypeConfiguration<MarketingReviewEvidence>
{
    public void Configure(EntityTypeBuilder<MarketingReviewEvidence> builder)
    {
        builder.ToTable("MarketingReviewEvidences");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.CampaignName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Platform)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(e => e.StartDate)
            .IsRequired()
            .HasColumnType("date");

        builder.Property(e => e.EndDate)
            .IsRequired()
            .HasColumnType("date");

        builder.Property(e => e.OrdersCount)
            .IsRequired(false);

        builder.Property(e => e.RevenueAmount)
            .IsRequired(false)
            .HasColumnType("decimal(18,2)");

        builder.Property(e => e.EvidenceType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        // 1-to-1 relationship: ServiceReview → MarketingReviewEvidence
        // FK with UNIQUE constraint ensures exactly one evidence per review
        builder.HasOne(e => e.ServiceReview)
            .WithOne(r => r.MarketingEvidence)
            .HasForeignKey<MarketingReviewEvidence>(e => e.ServiceReviewId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.ServiceReviewId)
            .IsUnique()
            .HasDatabaseName("UQ_MarketingReviewEvidences_ServiceReview");

        // Query indexes
        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => e.Platform);
        builder.HasIndex(e => e.EvidenceType);
    }
}
