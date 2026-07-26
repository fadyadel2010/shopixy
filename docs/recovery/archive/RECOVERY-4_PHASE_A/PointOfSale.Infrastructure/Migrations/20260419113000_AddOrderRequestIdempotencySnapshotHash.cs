using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [Migration("20260419113000_AddOrderRequestIdempotencySnapshotHash")]
    public partial class AddOrderRequestIdempotencySnapshotHash : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SnapshotHash",
                table: "OrderRequestIdempotency",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SnapshotHash",
                table: "OrderRequestIdempotency");
        }
    }
}
