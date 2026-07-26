using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class PosCartOutboxMessageConfiguration : IEntityTypeConfiguration<PosCartOutboxMessage>
{
    public void Configure(EntityTypeBuilder<PosCartOutboxMessage> builder)
    {
        builder.ToTable("PosCartOutboxMessages");
        builder.HasKey(message => message.Id);
        builder.Property(message => message.EventType).HasMaxLength(100).IsRequired();
        builder.Property(message => message.PayloadJson).HasColumnType("text").IsRequired();
        builder.Property(message => message.IdempotencyKey).HasMaxLength(260).IsRequired();
        builder.Property(message => message.ProcessingError).HasMaxLength(1000);
        builder.Property(message => message.RowVersion)
            .IsConcurrencyToken();

        builder.HasIndex(message => new { message.TenantId, message.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_PosCartOutboxMessages_TenantId_IdempotencyKey");
        builder.HasIndex(message => new { message.TenantId, message.CartId, message.CartVersion })
            .HasDatabaseName("IX_PosCartOutboxMessages_TenantId_CartId_Version");
        builder.HasIndex(message => new { message.ProcessedAtUtc, message.OccurredAtUtc })
            .HasDatabaseName("IX_PosCartOutboxMessages_Processing");
    }
}
