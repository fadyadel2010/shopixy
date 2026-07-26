using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class ServiceRequestMessageConfiguration : IEntityTypeConfiguration<ServiceRequestMessage>
{
    public void Configure(EntityTypeBuilder<ServiceRequestMessage> builder)
    {
        builder.ToTable("ServiceRequestMessages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.SenderRole)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(m => m.MessageType)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(m => m.Body)
            .IsRequired()
            .HasColumnType("text");

        builder.HasOne(m => m.ServiceRequest)
            .WithMany()
            .HasForeignKey(m => m.ServiceRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => m.TenantId);
        builder.HasIndex(m => new { m.ServiceRequestId, m.CreatedAt });
        builder.HasIndex(m => m.SenderUserId);
    }
}
