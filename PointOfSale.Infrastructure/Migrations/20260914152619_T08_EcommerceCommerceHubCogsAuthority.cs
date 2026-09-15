using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class T08_EcommerceCommerceHubCogsAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CostAmountSnapshot",
                table: "CommerceHubSubOrderItems",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "CostSnapshotCapturedAtUtc",
                table: "CommerceHubSubOrderItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCostSnapshot",
                table: "CommerceHubSubOrderItems",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CostAmountSnapshot",
                table: "CommerceHubSubOrderItems");

            migrationBuilder.DropColumn(
                name: "CostSnapshotCapturedAtUtc",
                table: "CommerceHubSubOrderItems");

            migrationBuilder.DropColumn(
                name: "UnitCostSnapshot",
                table: "CommerceHubSubOrderItems");
        }
    }
}
