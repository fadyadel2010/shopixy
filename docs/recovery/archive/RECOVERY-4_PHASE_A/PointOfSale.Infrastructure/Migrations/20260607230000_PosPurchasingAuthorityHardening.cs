using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

public partial class PosPurchasingAuthorityHardening : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "OriginalPurchaseInvoiceId",
            table: "Returns",
            type: "uuid",
            nullable: true);

        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM "Returns" r
                    WHERE r."ReturnType" = 0
                      AND (
                          r."OriginalInvoiceId" IS NULL OR
                          NOT EXISTS (
                              SELECT 1
                              FROM "PurchaseInvoices" p
                              WHERE p."TenantId" = r."TenantId"
                                AND p."Id" = r."OriginalInvoiceId"
                                AND p."IsDeleted" = FALSE
                          )
                      )
                ) THEN
                    RAISE EXCEPTION
                        'Cannot harden purchase return authority: a purchase return lacks same-tenant live PurchaseInvoice lineage';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM "PurchaseInvoiceItems"
                    WHERE "Quantity" <= 0
                       OR "ConversionFactor" <= 0
                       OR "BaseQuantity" <= 0
                       OR "UnitPrice" <= 0
                       OR "TaxRate" < 0
                       OR "TaxRate" > 100
                ) THEN
                    RAISE EXCEPTION
                        'Cannot harden purchasing authority: invalid historical PurchaseInvoiceItem quantity, conversion, cost, or tax data exists';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM "ReturnItems" ri
                    JOIN "Returns" r ON r."Id" = ri."ReturnId"
                    WHERE r."ReturnType" = 0
                      AND (
                          ri."Quantity" <= 0
                          OR ri."ConversionFactor" <= 0
                          OR ri."BaseQuantity" <= 0
                          OR ri."UnitPrice" < 0
                          OR ri."TaxRate" < 0
                          OR ri."TaxRate" > 100
                          OR ri."TaxAmount" < 0
                          OR ri."TotalAmount" < 0
                      )
                ) THEN
                    RAISE EXCEPTION
                        'Cannot harden purchasing authority: invalid historical purchase ReturnItem quantity, conversion, amount, or tax data exists';
                END IF;
            END
            $$;

            UPDATE "Returns"
            SET "OriginalPurchaseInvoiceId" = "OriginalInvoiceId",
                "OriginalInvoiceId" = NULL
            WHERE "ReturnType" = 0;
            """);

        migrationBuilder.AddUniqueConstraint(
            name: "AK_PurchaseInvoices_TenantId_Id",
            table: "PurchaseInvoices",
            columns: new[] { "TenantId", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_Returns_TenantId_OriginalPurchaseInvoiceId",
            table: "Returns",
            columns: new[] { "TenantId", "OriginalPurchaseInvoiceId" });

        migrationBuilder.AddForeignKey(
            name: "FK_Returns_PurchaseInvoices_TenantId_OriginalPurchaseInvoiceId",
            table: "Returns",
            columns: new[] { "TenantId", "OriginalPurchaseInvoiceId" },
            principalTable: "PurchaseInvoices",
            principalColumns: new[] { "TenantId", "Id" },
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddCheckConstraint(
            name: "CK_Returns_PurchaseAuthorityLineage",
            table: "Returns",
            sql: "(\"ReturnType\" <> 0) OR (\"OriginalPurchaseInvoiceId\" IS NOT NULL AND \"OriginalInvoiceId\" IS NULL AND \"SupplierId\" IS NOT NULL AND \"CustomerId\" IS NULL)");

        migrationBuilder.AddCheckConstraint(
            name: "CK_PurchaseInvoiceItems_Quantity_Positive",
            table: "PurchaseInvoiceItems",
            sql: "\"Quantity\" > 0");
        migrationBuilder.AddCheckConstraint(
            name: "CK_PurchaseInvoiceItems_ConversionFactor_Positive",
            table: "PurchaseInvoiceItems",
            sql: "\"ConversionFactor\" > 0");
        migrationBuilder.AddCheckConstraint(
            name: "CK_PurchaseInvoiceItems_BaseQuantity_Positive",
            table: "PurchaseInvoiceItems",
            sql: "\"BaseQuantity\" > 0");
        migrationBuilder.AddCheckConstraint(
            name: "CK_PurchaseInvoiceItems_UnitPrice_Positive",
            table: "PurchaseInvoiceItems",
            sql: "\"UnitPrice\" > 0");
        migrationBuilder.AddCheckConstraint(
            name: "CK_PurchaseInvoiceItems_TaxRate_Valid",
            table: "PurchaseInvoiceItems",
            sql: "\"TaxRate\" >= 0 AND \"TaxRate\" <= 100");

        migrationBuilder.AddCheckConstraint(
            name: "CK_ReturnItems_Quantity_Positive",
            table: "ReturnItems",
            sql: "\"Quantity\" > 0");
        migrationBuilder.AddCheckConstraint(
            name: "CK_ReturnItems_ConversionFactor_Positive",
            table: "ReturnItems",
            sql: "\"ConversionFactor\" > 0");
        migrationBuilder.AddCheckConstraint(
            name: "CK_ReturnItems_BaseQuantity_Positive",
            table: "ReturnItems",
            sql: "\"BaseQuantity\" > 0");
        migrationBuilder.AddCheckConstraint(
            name: "CK_ReturnItems_UnitPrice_NonNegative",
            table: "ReturnItems",
            sql: "\"UnitPrice\" >= 0");
        migrationBuilder.AddCheckConstraint(
            name: "CK_ReturnItems_TaxRate_Valid",
            table: "ReturnItems",
            sql: "\"TaxRate\" >= 0 AND \"TaxRate\" <= 100");
        migrationBuilder.AddCheckConstraint(
            name: "CK_ReturnItems_Amounts_NonNegative",
            table: "ReturnItems",
            sql: "\"TaxAmount\" >= 0 AND \"TotalAmount\" >= 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Returns_PurchaseInvoices_TenantId_OriginalPurchaseInvoiceId",
            table: "Returns");
        migrationBuilder.DropCheckConstraint(name: "CK_Returns_PurchaseAuthorityLineage", table: "Returns");
        migrationBuilder.DropIndex(name: "IX_Returns_TenantId_OriginalPurchaseInvoiceId", table: "Returns");
        migrationBuilder.DropUniqueConstraint(name: "AK_PurchaseInvoices_TenantId_Id", table: "PurchaseInvoices");

        migrationBuilder.DropCheckConstraint(name: "CK_PurchaseInvoiceItems_Quantity_Positive", table: "PurchaseInvoiceItems");
        migrationBuilder.DropCheckConstraint(name: "CK_PurchaseInvoiceItems_ConversionFactor_Positive", table: "PurchaseInvoiceItems");
        migrationBuilder.DropCheckConstraint(name: "CK_PurchaseInvoiceItems_BaseQuantity_Positive", table: "PurchaseInvoiceItems");
        migrationBuilder.DropCheckConstraint(name: "CK_PurchaseInvoiceItems_UnitPrice_Positive", table: "PurchaseInvoiceItems");
        migrationBuilder.DropCheckConstraint(name: "CK_PurchaseInvoiceItems_TaxRate_Valid", table: "PurchaseInvoiceItems");
        migrationBuilder.DropCheckConstraint(name: "CK_ReturnItems_Quantity_Positive", table: "ReturnItems");
        migrationBuilder.DropCheckConstraint(name: "CK_ReturnItems_ConversionFactor_Positive", table: "ReturnItems");
        migrationBuilder.DropCheckConstraint(name: "CK_ReturnItems_BaseQuantity_Positive", table: "ReturnItems");
        migrationBuilder.DropCheckConstraint(name: "CK_ReturnItems_UnitPrice_NonNegative", table: "ReturnItems");
        migrationBuilder.DropCheckConstraint(name: "CK_ReturnItems_TaxRate_Valid", table: "ReturnItems");
        migrationBuilder.DropCheckConstraint(name: "CK_ReturnItems_Amounts_NonNegative", table: "ReturnItems");

        migrationBuilder.DropColumn(
            name: "OriginalPurchaseInvoiceId",
            table: "Returns");
    }
}
