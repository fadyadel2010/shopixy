using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260911120000_POS_SalesReturnSettlementSnapshots")]
public partial class POS_SalesReturnSettlementSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "CardRefundAmount",
            table: "Returns",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "CashRefundAmount",
            table: "Returns",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "CustomerCreditAmount",
            table: "Returns",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<int>(
            name: "RefundMethod",
            table: "Returns",
            type: "integer",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_Returns_RefundAmounts_NonNegative",
            table: "Returns",
            sql: "\"CashRefundAmount\" >= 0 AND \"CardRefundAmount\" >= 0 AND \"CustomerCreditAmount\" >= 0");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Returns_SettlementSnapshot_Consistent",
            table: "Returns",
            sql: "(\"RefundMethod\" IS NULL AND \"CashRefundAmount\" = 0 AND \"CardRefundAmount\" = 0 AND \"CustomerCreditAmount\" = 0) OR " +
                 "(\"ReturnType\" = 1 AND ((\"RefundMethod\" = 0 AND \"CashRefundAmount\" = \"TotalAmount\" AND \"CardRefundAmount\" = 0 AND \"CustomerCreditAmount\" = 0) OR " +
                 "(\"RefundMethod\" = 1 AND \"CashRefundAmount\" = 0 AND \"CardRefundAmount\" = \"TotalAmount\" AND \"CustomerCreditAmount\" = 0) OR " +
                 "(\"RefundMethod\" = 2 AND \"CashRefundAmount\" > 0 AND \"CardRefundAmount\" > 0 AND \"CashRefundAmount\" + \"CardRefundAmount\" = \"TotalAmount\" AND \"CustomerCreditAmount\" = 0) OR " +
                 "(\"RefundMethod\" = 3 AND \"CashRefundAmount\" = 0 AND \"CardRefundAmount\" = 0 AND \"CustomerCreditAmount\" = \"TotalAmount\")))");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_Returns_RefundAmounts_NonNegative",
            table: "Returns");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Returns_SettlementSnapshot_Consistent",
            table: "Returns");

        migrationBuilder.DropColumn(name: "CardRefundAmount", table: "Returns");
        migrationBuilder.DropColumn(name: "CashRefundAmount", table: "Returns");
        migrationBuilder.DropColumn(name: "CustomerCreditAmount", table: "Returns");
        migrationBuilder.DropColumn(name: "RefundMethod", table: "Returns");
    }
}
