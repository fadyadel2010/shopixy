using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ManualCodPermanentSettlementIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentSettlementOrders_Tenant_Order",
                table: "PaymentSettlementOrders");

            migrationBuilder.DropIndex(
                name: "IX_PaymentSettlementBatches_Tenant_Type_SettlementId",
                table: "PaymentSettlementBatches");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSettlementOrders_Tenant_Order",
                table: "PaymentSettlementOrders",
                columns: new[] { "TenantId", "OrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSettlementBatches_Tenant_Type_SettlementId",
                table: "PaymentSettlementBatches",
                columns: new[] { "TenantId", "SettlementType", "SettlementId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentSettlementOrders_Tenant_Order",
                table: "PaymentSettlementOrders");

            migrationBuilder.DropIndex(
                name: "IX_PaymentSettlementBatches_Tenant_Type_SettlementId",
                table: "PaymentSettlementBatches");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSettlementOrders_Tenant_Order",
                table: "PaymentSettlementOrders",
                columns: new[] { "TenantId", "OrderId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSettlementBatches_Tenant_Type_SettlementId",
                table: "PaymentSettlementBatches",
                columns: new[] { "TenantId", "SettlementType", "SettlementId" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }
    }
}
