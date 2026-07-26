using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class SalesInvoiceItemConfiguration : IEntityTypeConfiguration<SalesInvoiceItem>
{
    public void Configure(EntityTypeBuilder<SalesInvoiceItem> builder)
    {
        builder.HasKey(item => item.Id);

        builder.HasIndex(item => new { item.SalesInvoiceId, item.ProductId })
            .HasDatabaseName("IX_SalesInvoiceItems_SalesInvoiceId_ProductId");

        builder.HasIndex(item => new { item.SalesInvoiceId, item.ProductVariantId })
            .HasDatabaseName("IX_SalesInvoiceItems_SalesInvoiceId_ProductVariantId");

        builder.Property(item => item.CostPriceSnapshot)
            .HasPrecision(18, 6);

        builder.Property(item => item.CostAmountSnapshot)
            .HasPrecision(18, 2);
    }
}
