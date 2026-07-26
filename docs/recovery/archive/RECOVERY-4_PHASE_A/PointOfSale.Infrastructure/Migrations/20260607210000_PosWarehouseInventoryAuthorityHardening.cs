using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

public partial class PosWarehouseInventoryAuthorityHardening : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE OR REPLACE FUNCTION pos_reject_stock_movement_mutation()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $$
            BEGIN
                RAISE EXCEPTION
                    'StockMovements are append-only inventory authority and cannot be updated or deleted'
                    USING ERRCODE = '55000';
            END;
            $$;

            DROP TRIGGER IF EXISTS trg_stock_movements_append_only ON "StockMovements";

            CREATE TRIGGER trg_stock_movements_append_only
            BEFORE UPDATE OR DELETE ON "StockMovements"
            FOR EACH ROW
            EXECUTE FUNCTION pos_reject_stock_movement_mutation();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TRIGGER IF EXISTS trg_stock_movements_append_only ON "StockMovements";
            DROP FUNCTION IF EXISTS pos_reject_stock_movement_mutation();
            """);
    }
}
