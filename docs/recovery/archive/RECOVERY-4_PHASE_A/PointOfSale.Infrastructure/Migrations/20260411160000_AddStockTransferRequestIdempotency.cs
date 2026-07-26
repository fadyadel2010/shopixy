using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(PointOfSale.Infrastructure.Data.ApplicationDbContext))]
    [Migration("20260411160000_AddStockTransferRequestIdempotency")]
    public partial class AddStockTransferRequestIdempotency : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockTransferRequestIdempotency",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    TenantId = table.Column<Guid>(nullable: false),
                    Key = table.Column<string>(maxLength: 255, nullable: false),
                    Operation = table.Column<string>(maxLength: 64, nullable: false),
                    RequestFingerprint = table.Column<string>(maxLength: 64, nullable: false),
                    StockTransferId = table.Column<Guid>(nullable: true),
                    ResponseStatusCode = table.Column<int>(nullable: true),
                    ResponseJson = table.Column<string>(nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    CreatedBy = table.Column<Guid>(nullable: true),
                    UpdatedBy = table.Column<Guid>(nullable: true),
                    IsDeleted = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransferRequestIdempotency", record => record.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferRequestIdempotency_TenantId_StockTransferId",
                table: "StockTransferRequestIdempotency",
                columns: new[] { "TenantId", "StockTransferId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferRequestIdempotency_TenantId_Key_Operation",
                table: "StockTransferRequestIdempotency",
                columns: new[] { "TenantId", "Key", "Operation" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockTransferRequestIdempotency");
        }
    }
}
