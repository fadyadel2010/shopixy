using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(PointOfSale.Infrastructure.Data.ApplicationDbContext))]
    [Migration("20260408103000_AddPurchaseInvoiceSequence")]
    public partial class AddPurchaseInvoiceSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                migrationBuilder.Sql(
                    """
                    DO $$
                    DECLARE max_purchase_number bigint;
                    BEGIN
                        CREATE SEQUENCE IF NOT EXISTS purchase_invoice_seq
                            START WITH 1000
                            INCREMENT BY 1
                            MINVALUE 1000;

                        SELECT COALESCE(MAX((regexp_match("InvoiceNumber", '^PUR-\d{8}-(\d+)$'))[1]::bigint), 0)
                        INTO max_purchase_number
                        FROM "PurchaseInvoices"
                        WHERE "InvoiceNumber" ~ '^PUR-\d{8}-\d+$';

                        IF max_purchase_number < 1000 THEN
                            PERFORM setval('purchase_invoice_seq', 1000, false);
                        ELSE
                            PERFORM setval('purchase_invoice_seq', max_purchase_number, true);
                        END IF;
                    END $$;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                migrationBuilder.Sql("""DROP SEQUENCE IF EXISTS purchase_invoice_seq;""");
            }
        }
    }
}
