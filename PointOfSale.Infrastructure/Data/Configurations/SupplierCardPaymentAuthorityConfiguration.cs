using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class PurchaseInvoicePaymentAuthorityConfiguration : IEntityTypeConfiguration<PurchaseInvoicePayment>
{
    public void Configure(EntityTypeBuilder<PurchaseInvoicePayment> builder)
    {
        builder.HasOne(payment => payment.FundingInstrument)
            .WithMany()
            .HasForeignKey(payment => payment.FundingInstrumentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(payment => payment.FundingLedgerAccount)
            .WithMany()
            .HasForeignKey(payment => payment.FundingLedgerAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(payment => payment.FundingInstrumentId);
    }
}

public sealed class SupplierPaymentAuthorityConfiguration : IEntityTypeConfiguration<SupplierPayment>
{
    public void Configure(EntityTypeBuilder<SupplierPayment> builder)
    {
        builder.HasOne(payment => payment.FundingInstrument)
            .WithMany()
            .HasForeignKey(payment => new { payment.TenantId, payment.FundingInstrumentId })
            .HasPrincipalKey(instrument => new { instrument.TenantId, instrument.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(payment => payment.FundingLedgerAccount)
            .WithMany()
            .HasForeignKey(payment => new { payment.TenantId, payment.FundingLedgerAccountId })
            .HasPrincipalKey(account => new { account.TenantId, account.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(payment => payment.FundingInstrumentId);
    }
}
