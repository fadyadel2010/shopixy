using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class ServiceReviewConfiguration : IEntityTypeConfiguration<ServiceReview>
{
    public void Configure(EntityTypeBuilder<ServiceReview> builder)
    {
        builder.ToTable("ServiceReviews", table =>
        {
            table.HasCheckConstraint(
                "CK_ServiceReviews_Rating_Range",
                "\"Rating\" >= 1 AND \"Rating\" <= 5");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Rating)
            .IsRequired();

        builder.Property(r => r.Comment)
            .HasColumnType("text");

        builder.HasOne(r => r.ServiceRequest)
            .WithMany()
            .HasForeignKey(r => r.ServiceRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.Service)
            .WithMany()
            .HasForeignKey(r => r.ServiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.TenantId);
        builder.HasIndex(r => r.ServiceId);
        builder.HasIndex(r => r.ProviderUserId);
        builder.HasIndex(r => r.Rating)
            .HasFilter("\"IsDeleted\" = false");

        builder.HasIndex(r => r.ServiceRequestId)
            .IsUnique()
            .HasDatabaseName("UQ_ServiceReviews_ServiceRequest");
    }
}
