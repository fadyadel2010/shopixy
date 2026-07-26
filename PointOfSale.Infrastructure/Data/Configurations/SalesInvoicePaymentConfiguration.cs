using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class SalesInvoicePaymentConfiguration : IEntityTypeConfiguration<SalesInvoicePayment>
{
    public void Configure(EntityTypeBuilder<SalesInvoicePayment> builder)
    {
        builder.HasKey(payment => payment.Id);

        builder.HasIndex(payment => new { payment.SalesInvoiceId, payment.PaymentDate })
            .HasDatabaseName("IX_SalesInvoicePayments_SalesInvoiceId_PaymentDate");

        builder.HasIndex(payment => new { payment.PaymentMethod, payment.PaymentDate })
            .HasDatabaseName("IX_SalesInvoicePayments_PaymentMethod_PaymentDate");
    }
}
