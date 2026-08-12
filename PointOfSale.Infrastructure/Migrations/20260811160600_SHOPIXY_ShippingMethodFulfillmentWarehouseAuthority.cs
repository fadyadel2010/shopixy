using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SHOPIXY_ShippingMethodFulfillmentWarehouseAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FulfillmentWarehouseId",
                table: "ShippingMethods",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShippingMethods_TenantId_FulfillmentWarehouseId",
                table: "ShippingMethods",
                columns: new[] { "TenantId", "FulfillmentWarehouseId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ShippingMethods_Warehouses_TenantId_FulfillmentWarehouseId",
                table: "ShippingMethods",
                columns: new[] { "TenantId", "FulfillmentWarehouseId" },
                principalTable: "Warehouses",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShippingMethods_Warehouses_TenantId_FulfillmentWarehouseId",
                table: "ShippingMethods");

            migrationBuilder.DropIndex(
                name: "IX_ShippingMethods_TenantId_FulfillmentWarehouseId",
                table: "ShippingMethods");

            migrationBuilder.DropColumn(
                name: "FulfillmentWarehouseId",
                table: "ShippingMethods");
        }
    }
}
