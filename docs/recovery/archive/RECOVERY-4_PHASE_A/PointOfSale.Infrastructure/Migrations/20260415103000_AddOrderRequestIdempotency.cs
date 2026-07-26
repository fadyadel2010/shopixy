using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(PointOfSale.Infrastructure.Data.ApplicationDbContext))]
    [Migration("20260415103000_AddOrderRequestIdempotency")]
    public partial class AddOrderRequestIdempotency : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderRequestIdempotency",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    TenantId = table.Column<Guid>(nullable: false),
                    Key = table.Column<string>(maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(maxLength: 64, nullable: false),
                    OrderId = table.Column<Guid>(nullable: true),
                    ResponseStatusCode = table.Column<int>(nullable: true),
                    ResponsePayload = table.Column<string>(nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(nullable: true),
                    ClaimToken = table.Column<string>(maxLength: 32, nullable: true),
                    ClaimTimestampUtc = table.Column<DateTime>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    CreatedBy = table.Column<Guid>(nullable: true),
                    UpdatedBy = table.Column<Guid>(nullable: true),
                    IsDeleted = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderRequestIdempotency", record => record.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderRequestIdempotency_TenantId_Key",
                table: "OrderRequestIdempotency",
                columns: new[] { "TenantId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderRequestIdempotency_TenantId_OrderId",
                table: "OrderRequestIdempotency",
                columns: new[] { "TenantId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderRequestIdempotency_CompletedAtUtc_IsDeleted",
                table: "OrderRequestIdempotency",
                columns: new[] { "CompletedAtUtc", "IsDeleted" },
                filter: "\"CompletedAtUtc\" IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderRequestIdempotency");
        }
    }
}
