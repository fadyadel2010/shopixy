using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class SalesInvoiceConfiguration : IEntityTypeConfiguration<SalesInvoice>
{
    public void Configure(EntityTypeBuilder<SalesInvoice> builder)
    {
        builder.HasKey(invoice => invoice.Id);

        builder.HasIndex(invoice => new { invoice.TenantId, invoice.InvoiceDate })
            .HasDatabaseName("IX_SalesInvoices_TenantId_InvoiceDate_Active")
            .HasFilter($"\"IsDeleted\" = false AND \"Status\" <> {(int)InvoiceStatus.Cancelled}");

        builder.HasIndex(invoice => new { invoice.TenantId, invoice.Status, invoice.InvoiceDate })
            .HasDatabaseName("IX_SalesInvoices_TenantId_Status_InvoiceDate");

        builder.HasIndex(invoice => new { invoice.TenantId, invoice.CustomerId, invoice.InvoiceDate })
            .HasDatabaseName("IX_SalesInvoices_TenantId_CustomerId_InvoiceDate");

        builder.HasIndex(invoice => new { invoice.TenantId, invoice.WarehouseId, invoice.InvoiceDate })
            .HasDatabaseName("IX_SalesInvoices_TenantId_WarehouseId_InvoiceDate");

        builder.HasIndex(invoice => new { invoice.TenantId, invoice.CreatedAt })
            .HasDatabaseName("IX_SalesInvoices_TenantId_CreatedAt");
    }
}
