using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class ProductMediaConfiguration : IEntityTypeConfiguration<ProductMedia>
{
    public void Configure(EntityTypeBuilder<ProductMedia> builder)
    {
        builder.ToTable("ProductMedia");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.TenantId)
            .IsRequired();

        builder.Property(m => m.MediaType)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(m => m.Url)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(m => m.Provider)
            .HasMaxLength(20);

        builder.Property(m => m.Position)
            .IsRequired();

        builder.Property(m => m.CreatedAtUtc)
            .IsRequired();

        builder.HasOne(m => m.Product)
            .WithMany(p => p.Media)
            .HasForeignKey(m => m.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.ProductId, m.Position })
            .HasDatabaseName("IX_ProductMedia_ProductId_Position");
    }
}
