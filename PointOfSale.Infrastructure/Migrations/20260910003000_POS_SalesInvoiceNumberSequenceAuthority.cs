using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260910003000_POS_SalesInvoiceNumberSequenceAuthority")]
public partial class POS_SalesInvoiceNumberSequenceAuthority : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $$
            DECLARE
                max_invoice_suffix bigint;
                current_last_value bigint;
                current_is_called boolean;
                current_next_value bigint;
            BEGIN
                CREATE SEQUENCE IF NOT EXISTS public."PosSalesInvoiceNumberSeq"
                    START WITH 1
                    INCREMENT BY 1
                    MINVALUE 1
                    NO MAXVALUE
                    CACHE 1
                    NO CYCLE;

                SELECT COALESCE(MAX((regexp_match("InvoiceNumber", '^INV-\d{8}-(\d+)$'))[1]::bigint), 0)
                INTO max_invoice_suffix
                FROM "SalesInvoices"
                WHERE "InvoiceNumber" ~ '^INV-\d{8}-\d+$';

                SELECT last_value, is_called
                INTO current_last_value, current_is_called
                FROM public."PosSalesInvoiceNumberSeq";

                current_next_value := CASE
                    WHEN current_is_called THEN current_last_value + 1
                    ELSE current_last_value
                END;

                IF max_invoice_suffix = 0 THEN
                    IF current_next_value < 1 THEN
                        PERFORM setval('public."PosSalesInvoiceNumberSeq"'::regclass, 1, false);
                    END IF;
                ELSIF current_next_value <= max_invoice_suffix THEN
                    PERFORM setval(
                        'public."PosSalesInvoiceNumberSeq"'::regclass,
                        max_invoice_suffix,
                        true);
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Invoice numbers are durable business identifiers. A rollback must not remove
        // sequence authority that may already have issued numbers.
    }
}
