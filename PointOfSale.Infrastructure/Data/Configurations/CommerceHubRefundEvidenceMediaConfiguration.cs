using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CommerceHubRefundEvidenceMediaConfiguration : IEntityTypeConfiguration<CommerceHubRefundEvidenceMedia>
{
    public void Configure(EntityTypeBuilder<CommerceHubRefundEvidenceMedia> builder)
    {
        builder.ToTable("CommerceHubRefundEvidenceMedia");

        builder.HasKey(media => media.Id);

        builder.Property(media => media.ObjectKey)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(media => media.OriginalFileName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(media => media.ContentType)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(media => media.MediaType)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(media => media.ContentHash)
            .HasMaxLength(128);

        builder.Property(media => media.MetadataJson)
            .HasColumnType("jsonb");

        builder.Property(media => media.ThumbnailObjectKey)
            .HasMaxLength(500);

        builder.HasIndex(media => new { media.CustomerId, media.CreatedAt })
            .HasDatabaseName("IX_CommerceHubRefundEvidenceMedia_CustomerId_CreatedAt");

        builder.HasIndex(media => new { media.CustomerId, media.ContentHash })
            .HasDatabaseName("IX_CommerceHubRefundEvidenceMedia_CustomerId_ContentHash");

        builder.HasIndex(media => media.RefundRequestId)
            .HasDatabaseName("IX_CommerceHubRefundEvidenceMedia_RefundRequestId");

        builder.HasIndex(media => new { media.IsTemporary, media.ExpiresAtUtc })
            .HasDatabaseName("IX_CommerceHubRefundEvidenceMedia_IsTemporary_ExpiresAtUtc");

        builder.HasIndex(media => media.ObjectKey)
            .IsUnique()
            .HasDatabaseName("IX_CommerceHubRefundEvidenceMedia_ObjectKey");

        builder.HasOne(media => media.Customer)
            .WithMany()
            .HasForeignKey(media => media.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(media => media.RefundRequest)
            .WithMany(refundRequest => refundRequest.EvidenceMedia)
            .HasForeignKey(media => media.RefundRequestId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
