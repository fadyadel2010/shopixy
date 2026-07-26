using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(PointOfSale.Infrastructure.Data.ApplicationDbContext))]
    [Migration("20251201203400_AddCustomerOpeningBalanceFields")]
    public partial class AddCustomerOpeningBalanceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsOpeningBalance",
                table: "CustomerTransactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerTransactions_IsOpeningBalance",
                table: "CustomerTransactions",
                column: "IsOpeningBalance");

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningBalanceAmount",
                table: "Customers",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BalanceType",
                table: "Customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_BalanceType",
                table: "Customers",
                sql: "\"BalanceType\" IS NULL OR \"BalanceType\" IN ('Debit', 'Credit')");

            migrationBuilder.AddColumn<DateTime>(
                name: "OpeningBalanceDate",
                table: "Customers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CustomerTransactions_IsOpeningBalance",
                table: "CustomerTransactions");

            migrationBuilder.DropColumn(
                name: "IsOpeningBalance",
                table: "CustomerTransactions");

            migrationBuilder.DropColumn(
                name: "OpeningBalanceAmount",
                table: "Customers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_BalanceType",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "BalanceType",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "OpeningBalanceDate",
                table: "Customers");
        }
    }
}
