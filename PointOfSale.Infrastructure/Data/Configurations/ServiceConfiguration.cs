using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.ToTable("Services");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(s => s.Description)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(s => s.Category)
            .HasMaxLength(100);

        builder.Property(s => s.IsActive)
            .IsRequired();

        builder.HasOne(s => s.Profile)
            .WithMany()
            .HasForeignKey(s => s.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => s.TenantId);
        builder.HasIndex(s => s.ProfileId);
        builder.HasIndex(s => s.Category)
            .HasFilter("\"IsDeleted\" = false AND \"IsActive\" = true");
    }
}
