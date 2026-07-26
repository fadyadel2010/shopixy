using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S67_ReconcileCrossSystemRiskModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_StockSyncOutbox_VariantOrderingBlockers"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""UX_StockSyncOutboxMessages_Tenant_Variant_EventVersion"";");

            // Step 1: Add WarehouseId column with temporary Guid.Empty default for backfill
            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "StockSyncOutboxMessages",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            // Step 2: Backfill existing rows from PayloadJson with UUID validation
            // First, check for any rows with invalid WarehouseId values and abort if found
            migrationBuilder.Sql(@"
DO $$
DECLARE
    invalid_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO invalid_count
    FROM ""StockSyncOutboxMessages""
    WHERE ""WarehouseId"" = '00000000-0000-0000-0000-000000000000'::uuid
      AND ""PayloadJson"" IS NOT NULL
      AND ""PayloadJson"" <> ''
      AND ""PayloadJson""::jsonb ? 'WarehouseId'
      AND (""PayloadJson""::jsonb ->> 'WarehouseId' IS NULL 
           OR ""PayloadJson""::jsonb ->> 'WarehouseId' = ''
           OR NOT (""PayloadJson""::jsonb ->> 'WarehouseId' ~ '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$'));
    
    IF invalid_count > 0 THEN
        RAISE EXCEPTION 'Cannot add WarehouseId constraint: Found % StockSyncOutboxMessages rows with invalid or missing WarehouseId in PayloadJson. Manual cleanup required before migration can proceed.', invalid_count;
    END IF;
END $$;");

            // Update only rows with valid UUID format in PayloadJson
            migrationBuilder.Sql(@"
UPDATE ""StockSyncOutboxMessages""
SET ""WarehouseId"" = (""PayloadJson""::jsonb ->> 'WarehouseId')::uuid
WHERE ""WarehouseId"" = '00000000-0000-0000-0000-000000000000'::uuid
  AND ""PayloadJson"" IS NOT NULL
  AND ""PayloadJson"" <> ''
  AND ""PayloadJson""::jsonb ? 'WarehouseId'
  AND ""PayloadJson""::jsonb ->> 'WarehouseId' IS NOT NULL
  AND ""PayloadJson""::jsonb ->> 'WarehouseId' <> ''
  AND ""PayloadJson""::jsonb ->> 'WarehouseId' ~ '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$';");

            // Verify no Guid.Empty rows remain before adding constraint
            migrationBuilder.Sql(@"
DO $$
DECLARE
    empty_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO empty_count
    FROM ""StockSyncOutboxMessages""
    WHERE ""WarehouseId"" = '00000000-0000-0000-0000-000000000000'::uuid
      AND ""IsDeleted"" = false;
    
    IF empty_count > 0 THEN
        RAISE EXCEPTION 'Cannot add WarehouseId constraint: Found % active StockSyncOutboxMessages rows with Guid.Empty WarehouseId after backfill. Manual data repair required.', empty_count;
    END IF;
END $$;");

            // Step 3: Drop the default value after backfill
            migrationBuilder.AlterColumn<Guid>(
                name: "WarehouseId",
                table: "StockSyncOutboxMessages",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldDefaultValue: Guid.Empty);

            // Step 4: Add CHECK constraint to prevent future inserts with Guid.Empty
            migrationBuilder.AddCheckConstraint(
                name: "CK_StockSyncOutboxMessages_WarehouseId_NotEmpty",
                table: "StockSyncOutboxMessages",
                sql: "\"WarehouseId\" <> '00000000-0000-0000-0000-000000000000'::uuid");

            migrationBuilder.CreateIndex(
                name: "IX_StockSyncOutboxMessages_Unpublished_EventOrdering",
                table: "StockSyncOutboxMessages",
                columns: new[] { "TenantId", "VariantId", "WarehouseId", "EventVersion", "StockUpdatedAtUtc", "OccurredAtUtc", "CreatedAt", "Id" },
                filter: "\"PublishedAtUtc\" IS NULL AND \"IsFailed\" = false AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UX_StockSyncOutboxMessages_Tenant_Variant_Warehouse_EventVersion",
                table: "StockSyncOutboxMessages",
                columns: new[] { "TenantId", "VariantId", "WarehouseId", "EventVersion" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockSyncOutboxMessages_EventVersion_Positive",
                table: "StockSyncOutboxMessages",
                sql: "\"EventVersion\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockSyncOutboxMessages_OrderingTimestamps_Required",
                table: "StockSyncOutboxMessages",
                sql: "\"OccurredAtUtc\" IS NOT NULL AND \"StockUpdatedAtUtc\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockSyncOutboxMessages_Unpublished_EventOrdering",
                table: "StockSyncOutboxMessages");

            migrationBuilder.DropIndex(
                name: "UX_StockSyncOutboxMessages_Tenant_Variant_Warehouse_EventVersion",
                table: "StockSyncOutboxMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StockSyncOutboxMessages_WarehouseId_NotEmpty",
                table: "StockSyncOutboxMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StockSyncOutboxMessages_EventVersion_Positive",
                table: "StockSyncOutboxMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StockSyncOutboxMessages_OrderingTimestamps_Required",
                table: "StockSyncOutboxMessages");

            migrationBuilder.CreateIndex(
                name: "IX_StockSyncOutbox_VariantOrderingBlockers",
                table: "StockSyncOutboxMessages",
                columns: new[] { "TenantId", "VariantId", "EventVersion", "StockUpdatedAtUtc", "OccurredAtUtc", "CreatedAt", "Id" },
                filter: "\"PublishedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_StockSyncOutboxMessages_Tenant_Variant_EventVersion",
                table: "StockSyncOutboxMessages",
                columns: new[] { "TenantId", "VariantId", "EventVersion" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "StockSyncOutboxMessages");
        }
    }
}
