using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class SyntheticQaRunConfiguration : IEntityTypeConfiguration<SyntheticQaRun>
{
    public void Configure(EntityTypeBuilder<SyntheticQaRun> b)
    {
        b.ToTable("SyntheticQaRuns"); b.HasKey(x => x.Id);
        b.Property(x => x.Provenance).HasMaxLength(100).IsRequired();
        b.HasOne<PointOfSale.Core.Entities.Affiliate.AffiliatePromotionLink>().WithMany()
            .HasForeignKey(x => x.PromotionLinkId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<OnlineCustomer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SyntheticQaOrderConfiguration : IEntityTypeConfiguration<SyntheticQaOrder>
{
    public void Configure(EntityTypeBuilder<SyntheticQaOrder> b)
    {
        b.ToTable("SyntheticQaOrders"); b.HasKey(x => x.OrderId);
        b.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SyntheticQaRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SyntheticQaEvidenceConfiguration : IEntityTypeConfiguration<SyntheticQaEvidence>
{
    public void Configure(EntityTypeBuilder<SyntheticQaEvidence> b)
    {
        b.ToTable("SyntheticQaEvidence"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.OrderId, x.Kind }).IsUnique();
        b.HasIndex(x => x.Nonce).IsUnique();
        b.Property(x => x.Kind).HasMaxLength(30).IsRequired();
        b.Property(x => x.Nonce).HasMaxLength(64).IsRequired();
        b.Property(x => x.Signature).HasMaxLength(64).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.Amount).HasPrecision(18, 6);
        b.Property(x => x.Debit).HasMaxLength(80).IsRequired();
        b.Property(x => x.Credit).HasMaxLength(80).IsRequired();
        b.Property(x => x.Provenance).HasMaxLength(100).IsRequired();
        b.Ignore(x => x.DeliveryReference); b.Ignore(x => x.ProofUrl);
        b.HasOne<SyntheticQaOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SyntheticQaRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Shipment>().WithMany().HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SyntheticQaCommissionPostingConfiguration : IEntityTypeConfiguration<SyntheticQaCommissionPosting>
{
    public void Configure(EntityTypeBuilder<SyntheticQaCommissionPosting> b)
    {
        b.ToTable("SyntheticQaCommissionPostings"); b.HasKey(x => x.Id);
        b.HasIndex(x => x.OrderId).IsUnique();
        b.Property(x => x.Amount).HasPrecision(18, 6);
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.Debit).HasMaxLength(80).IsRequired();
        b.Property(x => x.Credit).HasMaxLength(80).IsRequired();
        b.Property(x => x.Provenance).HasMaxLength(100).IsRequired();
        b.HasOne<SyntheticQaOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
    }
}
