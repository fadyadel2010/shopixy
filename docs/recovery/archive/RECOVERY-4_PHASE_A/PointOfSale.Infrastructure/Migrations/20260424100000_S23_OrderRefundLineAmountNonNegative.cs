using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

/// <summary>
/// S23: OrderRefundLine Amount Non-Negative Constraint
/// 
/// WHAT: Adds a check constraint to enforce that OrderRefundLine.Amount cannot be negative.
/// 
/// WHY: Domain integrity - refund line amounts must be non-negative values.
/// 
/// SAFETY: Safe to apply - existing data should already be non-negative, but constraint
/// will prevent future invalid data.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260424100000_S23_OrderRefundLineAmountNonNegative")]
public partial class S23_OrderRefundLineAmountNonNegative : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddCheckConstraint(
            name: "CK_OrderRefundLines_Amount_NonNegative",
            table: "OrderRefundLines",
            sql: "\"Amount\" >= 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_OrderRefundLines_Amount_NonNegative",
            table: "OrderRefundLines");
    }
}
