using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class ShipmentCancellationOperationConfiguration : IEntityTypeConfiguration<ShipmentCancellationOperation>
{
    public void Configure(EntityTypeBuilder<ShipmentCancellationOperation> entity)
    {
        entity.ToTable("ShipmentCancellationOperations");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.TenantId, x.ShipmentId }).IsUnique(); // permanent, no soft-delete filter
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(40);
        entity.Property(x => x.RequestFingerprint).HasMaxLength(64).IsRequired();
        entity.Property(x => x.ActorIdentity).HasMaxLength(120).IsRequired();
        entity.Property(x => x.Source).HasMaxLength(40).IsRequired();
        entity.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(200).IsRequired();
        entity.Property(x => x.CarrierCode).HasMaxLength(50).IsRequired();
        entity.Property(x => x.TrackingNumber).HasMaxLength(100);
        entity.Property(x => x.AwbNumber).HasMaxLength(100);
        entity.Property(x => x.ExternalReferenceId).HasMaxLength(200);
        entity.Property(x => x.CarrierRequestId).HasMaxLength(200);
        entity.Property(x => x.AccountId).HasMaxLength(200);
        entity.Property(x => x.Environment).HasMaxLength(24);
        entity.Property(x => x.ConfirmationBasis).HasMaxLength(50);
        entity.Property(x => x.EvidenceReference).HasMaxLength(200);
        entity.Property(x => x.EvidenceHash).HasMaxLength(64);
        entity.Property(x => x.Code).HasMaxLength(120).IsRequired();
        entity.HasOne<Shipment>().WithMany().HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
    }
}
