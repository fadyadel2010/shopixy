using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class CarrierCollectionEvidenceConfiguration : IEntityTypeConfiguration<CarrierCollectionEvidence>
{
    public void Configure(EntityTypeBuilder<CarrierCollectionEvidence> b)
    {
        b.ToTable("CarrierCollectionEvidence");
        b.HasKey(x => x.Id);
        b.Property(x => x.CarrierCode).HasMaxLength(30).IsRequired();
        b.Property(x => x.AccountId).HasMaxLength(200);
        b.Property(x => x.Environment).HasMaxLength(20).IsRequired();
        b.Property(x => x.TrackingNumber).HasMaxLength(100).IsRequired();
        b.Property(x => x.DeliveryReference).HasMaxLength(200).IsRequired();
        b.Property(x => x.ObservedAmount).HasPrecision(18, 6);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.SourceContract).HasMaxLength(100).IsRequired();
        b.Property(x => x.TrustMethod).HasMaxLength(40).IsRequired();
        b.Property(x => x.PayloadHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ObservationFingerprint).HasMaxLength(64).IsRequired();
        b.Property(x => x.AdmissionOutcome).HasMaxLength(30).IsRequired();
        b.Property(x => x.AdmissionCode).HasMaxLength(100).IsRequired();
        b.Property(x => x.AuditIdentity).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.CarrierCode, x.ObservationFingerprint }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.OrderId }).IsUnique().HasFilter("\"AdmissionOutcome\" = 'Qualified'");
        b.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Shipment>().WithMany().HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CarrierConfiguration>().WithMany().HasForeignKey(x => x.ConfigurationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CarrierCodShipmentBinding>().WithMany().HasForeignKey(x => x.ShipmentBindingId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CarrierCollectionEvidence>().WithMany().HasForeignKey(x => x.ReviewedEvidenceId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CarrierCodShipmentBindingConfiguration : IEntityTypeConfiguration<CarrierCodShipmentBinding>
{
    public void Configure(EntityTypeBuilder<CarrierCodShipmentBinding> b)
    {
        b.ToTable("CarrierCodShipmentBindings");
        b.HasKey(x => x.Id);
        b.Property(x => x.CarrierCode).HasMaxLength(30).IsRequired();
        b.Property(x => x.AccountId).HasMaxLength(200).IsRequired();
        b.Property(x => x.Environment).HasMaxLength(20).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.TrackingNumber).HasMaxLength(100).IsRequired();
        b.Property(x => x.DeliveryReference).HasMaxLength(200).IsRequired();
        b.Property(x => x.ExternalReferenceId).HasMaxLength(200);
        b.Property(x => x.Fingerprint).HasMaxLength(64).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.ShipmentId }).IsUnique();
        b.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Shipment>().WithMany().HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CarrierConfiguration>().WithMany().HasForeignKey(x => x.ConfigurationId).OnDelete(DeleteBehavior.Restrict);
    }
}
