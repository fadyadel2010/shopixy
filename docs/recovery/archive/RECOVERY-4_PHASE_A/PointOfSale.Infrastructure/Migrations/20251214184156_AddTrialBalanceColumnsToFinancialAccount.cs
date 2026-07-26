using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTrialBalanceColumnsToFinancialAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountCode",
                table: "FinancialAccounts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "AccountType",
                table: "FinancialAccounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Level",
                table: "FinancialAccounts",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // Update existing Visa account with proper values if it exists
            migrationBuilder.Sql(@"
                UPDATE ""FinancialAccounts"" 
                SET ""AccountCode"" = '1200', ""AccountType"" = 1, ""Level"" = 1 
                WHERE ""Id"" = '11111111-1111-1111-1111-111111111111'
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccountCode",
                table: "FinancialAccounts");

            migrationBuilder.DropColumn(
                name: "AccountType",
                table: "FinancialAccounts");

            migrationBuilder.DropColumn(
                name: "Level",
                table: "FinancialAccounts");
        }
    }
}
