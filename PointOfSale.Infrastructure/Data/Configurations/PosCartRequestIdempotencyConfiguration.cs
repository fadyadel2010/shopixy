using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class PosCartRequestIdempotencyConfiguration : IEntityTypeConfiguration<PosCartRequestIdempotency>
{
    public void Configure(EntityTypeBuilder<PosCartRequestIdempotency> builder)
    {
        builder.ToTable("PosCartRequestIdempotency");
        builder.HasKey(record => record.Id);
        builder.HasIndex(record => new { record.TenantId, record.Key, record.Operation })
            .IsUnique()
            .HasDatabaseName("IX_PosCartRequestIdempotency_TenantId_Key_Operation");
        builder.Property(record => record.Key).HasMaxLength(200).IsRequired();
        builder.Property(record => record.Operation).HasMaxLength(64).IsRequired();
        builder.Property(record => record.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(record => record.ResponseJson).HasColumnType("text");
    }
}
