using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOnlineCustomerIdToOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OnlineCustomerId",
                table: "Orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_OnlineCustomerId",
                table: "Orders",
                column: "OnlineCustomerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_OnlineCustomers_OnlineCustomerId",
                table: "Orders",
                column: "OnlineCustomerId",
                principalTable: "OnlineCustomers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_OnlineCustomers_OnlineCustomerId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_OnlineCustomerId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "OnlineCustomerId",
                table: "Orders");
        }
    }
}
