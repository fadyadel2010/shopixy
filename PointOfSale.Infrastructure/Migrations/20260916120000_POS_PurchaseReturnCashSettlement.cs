using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260916120000_POS_PurchaseReturnCashSettlement")]
public partial class POS_PurchaseReturnCashSettlement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_Returns_SettlementSnapshot_Consistent",
            table: "Returns");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Returns_SettlementSnapshot_Consistent",
            table: "Returns",
            sql: "(\"RefundMethod\" IS NULL AND \"CashRefundAmount\" = 0 AND \"CardRefundAmount\" = 0 AND \"CustomerCreditAmount\" = 0) OR " +
                 "(\"ReturnType\" = 1 AND ((\"RefundMethod\" = 0 AND \"CashRefundAmount\" = \"TotalAmount\" AND \"CardRefundAmount\" = 0 AND \"CustomerCreditAmount\" = 0) OR " +
                 "(\"RefundMethod\" = 1 AND \"CashRefundAmount\" = 0 AND \"CardRefundAmount\" = \"TotalAmount\" AND \"CustomerCreditAmount\" = 0) OR " +
                 "(\"RefundMethod\" = 2 AND \"CashRefundAmount\" > 0 AND \"CardRefundAmount\" > 0 AND \"CashRefundAmount\" + \"CardRefundAmount\" = \"TotalAmount\" AND \"CustomerCreditAmount\" = 0) OR " +
                 "(\"RefundMethod\" = 3 AND \"CashRefundAmount\" = 0 AND \"CardRefundAmount\" = 0 AND \"CustomerCreditAmount\" = \"TotalAmount\"))) OR " +
                 "(\"ReturnType\" = 0 AND ((\"RefundMethod\" = 0 AND \"CashRefundAmount\" = \"TotalAmount\" AND \"CardRefundAmount\" = 0 AND \"CustomerCreditAmount\" = 0) OR " +
                 "(\"RefundMethod\" = 3 AND \"CashRefundAmount\" = 0 AND \"CardRefundAmount\" = 0 AND \"CustomerCreditAmount\" = 0)))");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_Returns_SettlementSnapshot_Consistent",
            table: "Returns");

        migrationBuilder.Sql(
            "UPDATE \"Returns\" SET \"RefundMethod\" = NULL, \"CashRefundAmount\" = 0 " +
            "WHERE \"ReturnType\" = 0 AND \"RefundMethod\" IS NOT NULL;");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Returns_SettlementSnapshot_Consistent",
            table: "Returns",
            sql: "(\"RefundMethod\" IS NULL AND \"CashRefundAmount\" = 0 AND \"CardRefundAmount\" = 0 AND \"CustomerCreditAmount\" = 0) OR " +
                 "(\"ReturnType\" = 1 AND ((\"RefundMethod\" = 0 AND \"CashRefundAmount\" = \"TotalAmount\" AND \"CardRefundAmount\" = 0 AND \"CustomerCreditAmount\" = 0) OR " +
                 "(\"RefundMethod\" = 1 AND \"CashRefundAmount\" = 0 AND \"CardRefundAmount\" = \"TotalAmount\" AND \"CustomerCreditAmount\" = 0) OR " +
                 "(\"RefundMethod\" = 2 AND \"CashRefundAmount\" > 0 AND \"CardRefundAmount\" > 0 AND \"CashRefundAmount\" + \"CardRefundAmount\" = \"TotalAmount\" AND \"CustomerCreditAmount\" = 0) OR " +
                 "(\"RefundMethod\" = 3 AND \"CashRefundAmount\" = 0 AND \"CardRefundAmount\" = 0 AND \"CustomerCreditAmount\" = \"TotalAmount\")))");
    }
}
