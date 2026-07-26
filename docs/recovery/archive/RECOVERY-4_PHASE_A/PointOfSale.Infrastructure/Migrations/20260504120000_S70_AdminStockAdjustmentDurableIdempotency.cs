using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S70_AdminStockAdjustmentDurableIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM ""StockMovements""
        WHERE ""IsDeleted"" = false
          AND ""IdempotencyKey"" IS NOT NULL
        GROUP BY ""TenantId"", ""IdempotencyKey""
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'Cannot add UX_StockMovements_TenantId_IdempotencyKey: duplicate active stock movement idempotency keys exist and require repair.';
    END IF;
END $$;");

            migrationBuilder.CreateIndex(
                name: "UX_StockMovements_TenantId_IdempotencyKey",
                table: "StockMovements",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"IdempotencyKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_StockMovements_TenantId_IdempotencyKey",
                table: "StockMovements");
        }
    }
}
