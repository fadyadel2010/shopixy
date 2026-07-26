using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <summary>
    /// S22: Add authoritative e-commerce refund records for idempotency,
    /// over-refund protection, and SKU-level refund allocation.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260424090000_S22_OrderRefundLifecycleHardening")]
    public partial class S22OrderRefundLifecycleHardening : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderRefunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    RefundId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IsFullRefund = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsPostSettlementRefund = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    SettlementBatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    SettlementType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SettlementId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PayoutBatchId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SettledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RefundedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderRefunds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderRefunds_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderRefundLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderRefundId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderRefundLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderRefundLines_OrderItems_OrderItemId",
                        column: x => x.OrderItemId,
                        principalTable: "OrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderRefundLines_OrderRefunds_OrderRefundId",
                        column: x => x.OrderRefundId,
                        principalTable: "OrderRefunds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefunds_TenantId_OrderId",
                table: "OrderRefunds",
                columns: new[] { "TenantId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefunds_TenantId_OrderId_RefundId",
                table: "OrderRefunds",
                columns: new[] { "TenantId", "OrderId", "RefundId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefunds_TenantId_SettlementBatchId",
                table: "OrderRefunds",
                columns: new[] { "TenantId", "SettlementBatchId" },
                filter: "\"SettlementBatchId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefundLines_OrderItemId",
                table: "OrderRefundLines",
                column: "OrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefundLines_OrderRefundId",
                table: "OrderRefundLines",
                column: "OrderRefundId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefundLines_TenantId_IsDeleted",
                table: "OrderRefundLines",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefundLines_Refund_OrderItem",
                table: "OrderRefundLines",
                columns: new[] { "OrderRefundId", "OrderItemId" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "OrderRefundLines");
            migrationBuilder.DropTable(name: "OrderRefunds");
        }
    }
}
