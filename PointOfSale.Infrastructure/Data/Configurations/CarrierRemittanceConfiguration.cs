using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class CarrierRemittanceConfiguration : IEntityTypeConfiguration<CarrierRemittance>
{
    public void Configure(EntityTypeBuilder<CarrierRemittance> b)
    {
        b.ToTable("CarrierRemittances", t => t.HasCheckConstraint("CK_CarrierRemittance_Receipt",
            "\"GrossAmount\" > 0 AND \"ReceivedAmount\" = \"GrossAmount\" AND \"Fees\" = 0 AND \"ActualReceiptAttested\" AND \"ReceiptChannel\" = 'PhysicalMerchantCash' AND \"ConfirmationBasis\" = 'OperatorAttestedPhysicalReceipt' AND \"ReconciliationOutcome\" = 'WholeOrderZeroFeeMatched' AND length(btrim(\"ReceiptReferenceKey\")) > 0 AND \"ReceivedAtUtc\" <= \"RecordedAtUtc\" AND (\"Outcome\" = 'Preparing' OR (\"Outcome\" = 'Posted' AND \"PostedJournalId\" IS NOT NULL AND \"PostedAtUtc\" IS NOT NULL))"));
        b.HasKey(x => x.Id);
        b.Property(x => x.RequestFingerprint).HasMaxLength(64).IsRequired();
        b.Property(x => x.CarrierCode).HasMaxLength(30).IsRequired();
        b.Property(x => x.AccountId).HasMaxLength(200).IsRequired();
        b.Property(x => x.Environment).HasMaxLength(20).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.GrossAmount).HasPrecision(18, 2);
        b.Property(x => x.ReceivedAmount).HasPrecision(18, 2);
        b.Property(x => x.Fees).HasPrecision(18, 2);
        b.Property(x => x.ReceiptChannel).HasMaxLength(30).IsRequired();
        b.Property(x => x.ReceiptReference).HasMaxLength(100).IsRequired();
        b.Property(x => x.ReceiptReferenceKey).HasMaxLength(100).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.EvidenceReference).HasMaxLength(500);
        b.Property(x => x.ConfirmationBasis).HasMaxLength(50).IsRequired();
        b.Property(x => x.ReconciliationOutcome).HasMaxLength(40).IsRequired();
        b.Property(x => x.Outcome).HasMaxLength(20).IsRequired();
        // A merchant reference is permanent within the tenant, even across accounts/carriers and deletion.
        b.HasIndex(x => new { x.TenantId, x.ReceiptReferenceKey }).IsUnique();
        b.HasOne<PaymentSettlementBatch>().WithOne().HasForeignKey<CarrierRemittance>(x => x.Id).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.OperatorId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CarrierConfiguration>().WithMany().HasForeignKey(x => x.ConfigurationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.ReceivingAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.ClearingAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.PostedJournalId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CarrierRemittanceOrderConfiguration : IEntityTypeConfiguration<CarrierRemittanceOrder>
{
    public void Configure(EntityTypeBuilder<CarrierRemittanceOrder> b)
    {
        b.ToTable("CarrierRemittanceOrders", t => t.HasCheckConstraint("CK_CarrierRemittanceOrder_Amount", "\"GrossAmount\" > 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.GrossAmount).HasPrecision(18, 2);
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.OrderId }).IsUnique();
        b.HasIndex(x => x.AllocationId).IsUnique();
        b.HasOne(x => x.Remittance).WithMany(x => x.Orders).HasForeignKey(x => x.RemittanceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PaymentSettlementOrder>().WithOne().HasForeignKey<CarrierRemittanceOrder>(x => x.AllocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Shipment>().WithMany().HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CarrierCodShipmentBinding>().WithMany().HasForeignKey(x => x.ShipmentBindingId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CarrierCollectionEvidence>().WithMany().HasForeignKey(x => x.CollectionEvidenceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.CollectionJournalId).OnDelete(DeleteBehavior.Restrict);
    }
}
