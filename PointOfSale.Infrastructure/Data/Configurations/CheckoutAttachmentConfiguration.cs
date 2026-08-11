using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class CheckoutAttachmentConfiguration : IEntityTypeConfiguration<CheckoutAttachment>
{
    public void Configure(EntityTypeBuilder<CheckoutAttachment> builder)
    {
        builder.ToTable("CheckoutAttachments");
        builder.HasKey(attachment => attachment.Id);

        builder.Property(attachment => attachment.OwnerId).HasMaxLength(128).IsRequired();
        builder.Property(attachment => attachment.OwnerType).HasMaxLength(32).IsRequired();
        builder.Property(attachment => attachment.SessionId).HasMaxLength(128);
        builder.Property(attachment => attachment.ObjectKey).HasMaxLength(500).IsRequired();
        builder.Property(attachment => attachment.FinalObjectKey).HasMaxLength(500);
        builder.Property(attachment => attachment.OriginalFileName).HasMaxLength(255).IsRequired();
        builder.Property(attachment => attachment.ContentType).HasMaxLength(255).IsRequired();
        builder.Property(attachment => attachment.MediaType).HasMaxLength(32).IsRequired();
        builder.Property(attachment => attachment.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(attachment => attachment.StagedETag).HasMaxLength(128);
        builder.Property(attachment => attachment.FinalETag).HasMaxLength(128);
        builder.Property(attachment => attachment.FinalChecksumSha256).HasMaxLength(128);
        builder.Property(attachment => attachment.MetadataJson).HasColumnType("jsonb").IsRequired().HasDefaultValue("{}");

        builder.HasIndex(attachment => attachment.ObjectKey)
            .IsUnique()
            .HasDatabaseName("UX_CheckoutAttachments_ObjectKey");
        builder.HasIndex(attachment => attachment.FinalObjectKey)
            .IsUnique()
            .HasDatabaseName("UX_CheckoutAttachments_FinalObjectKey");
        builder.HasIndex(attachment => new { attachment.TenantId, attachment.CartId, attachment.OwnerId, attachment.CheckoutFieldDefinitionId })
            .HasDatabaseName("IX_CheckoutAttachments_Tenant_Cart_Owner_Field");
        builder.HasIndex(attachment => new { attachment.TenantId, attachment.OrderId })
            .HasDatabaseName("IX_CheckoutAttachments_Tenant_OrderId");
        builder.HasIndex(attachment => new { attachment.Status, attachment.ExpiresAtUtc })
            .HasDatabaseName("IX_CheckoutAttachments_Status_ExpiresAtUtc");

        builder.HasOne(attachment => attachment.Order)
            .WithMany()
            .HasForeignKey(attachment => new { attachment.TenantId, attachment.OrderId })
            .HasPrincipalKey(order => new { order.TenantId, order.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
