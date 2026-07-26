using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCashboxOpeningBalanceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsOpeningBalance",
                table: "CashboxTransactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningBalanceAmount",
                table: "Cashboxes",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OpeningBalanceDate",
                table: "Cashboxes",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsOpeningBalance",
                table: "CashboxTransactions");

            migrationBuilder.DropColumn(
                name: "OpeningBalanceAmount",
                table: "Cashboxes");

            migrationBuilder.DropColumn(
                name: "OpeningBalanceDate",
                table: "Cashboxes");
        }
    }
}
