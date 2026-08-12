using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SHOPIXY_ShippingClosureRemediationStep2N : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShippingServiceLevel",
                table: "Orders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "WebhookSecret",
                table: "CarrierConfigurations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CredentialConfigurationFingerprint",
                table: "CarrierConfigurations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastValidatedConfigurationFingerprint",
                table: "CarrierConfigurations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // A warehouse is deterministic only when the tenant has exactly one
            // active, non-deleted default warehouse. Never guess among candidates.
            migrationBuilder.Sql(
                """
                WITH default_candidates AS (
                    SELECT "TenantId", "Id",
                           COUNT(*) OVER (PARTITION BY "TenantId") AS candidate_count
                    FROM "Warehouses"
                    WHERE "IsActive" = TRUE
                      AND "IsDeleted" = FALSE
                      AND "IsDefault" = TRUE
                )
                UPDATE "ShippingMethods" AS method
                SET "FulfillmentWarehouseId" = candidate."Id",
                    "UpdatedAt" = NOW()
                FROM default_candidates AS candidate
                WHERE method."TenantId" = candidate."TenantId"
                  AND candidate.candidate_count = 1
                  AND method."IsActive" = TRUE
                  AND method."IsDeleted" = FALSE
                  AND method."FulfillmentWarehouseId" IS NULL;
                """);

            // Any active row that still lacks a valid active tenant-owned warehouse
            // is made explicitly unavailable and annotated for operator remediation.
            migrationBuilder.Sql(
                """
                UPDATE "ShippingMethods" AS method
                SET "IsActive" = FALSE,
                    "Description" = CONCAT_WS(' ', NULLIF(method."Description", ''),
                        '[Step 2N] Deactivated: fulfillment warehouse requires operator remediation.'),
                    "UpdatedAt" = NOW()
                WHERE method."IsActive" = TRUE
                  AND method."IsDeleted" = FALSE
                  AND (
                      method."FulfillmentWarehouseId" IS NULL
                      OR NOT EXISTS (
                          SELECT 1
                          FROM "Warehouses" AS warehouse
                          WHERE warehouse."Id" = method."FulfillmentWarehouseId"
                            AND warehouse."TenantId" = method."TenantId"
                            AND warehouse."IsActive" = TRUE
                            AND warehouse."IsDeleted" = FALSE
                      )
                  );
                """);

            // Existing orders receive the strongest safely derivable execution
            // evidence. New orders persist this snapshot during checkout.
            migrationBuilder.Sql(
                """
                UPDATE "Orders" AS orders
                SET "ShippingServiceLevel" = method."ServiceLevel"
                FROM "ShippingMethods" AS method
                WHERE orders."ShippingServiceLevel" IS NULL
                  AND orders."ShippingMethodId" = method."Id"
                  AND orders."TenantId" = method."TenantId";
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ShippingMethods_ActiveRequiresFulfillmentWarehouse",
                table: "ShippingMethods",
                sql: "NOT \"IsActive\" OR \"FulfillmentWarehouseId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ShippingMethods_ActiveRequiresFulfillmentWarehouse",
                table: "ShippingMethods");

            migrationBuilder.DropColumn(
                name: "ShippingServiceLevel",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CredentialConfigurationFingerprint",
                table: "CarrierConfigurations");

            migrationBuilder.DropColumn(
                name: "LastValidatedConfigurationFingerprint",
                table: "CarrierConfigurations");

            migrationBuilder.AlterColumn<string>(
                name: "WebhookSecret",
                table: "CarrierConfigurations",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);
        }
    }
}
