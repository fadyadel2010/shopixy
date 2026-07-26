using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierOpeningBalanceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsOpeningBalance",
                table: "SupplierTransactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BalanceType",
                table: "Suppliers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningBalanceAmount",
                table: "Suppliers",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OpeningBalanceDate",
                table: "Suppliers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsOpeningBalance",
                table: "SupplierTransactions");

            migrationBuilder.DropColumn(
                name: "BalanceType",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "OpeningBalanceAmount",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "OpeningBalanceDate",
                table: "Suppliers");
        }
    }
}
