using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260517120000_PosSaleImmutableCostBasisSnapshots")]
    public partial class PosSaleImmutableCostBasisSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "SalesInvoiceItems"
                    ADD COLUMN IF NOT EXISTS "CostPriceSnapshot" numeric(18,6) NULL;

                ALTER TABLE "SalesInvoiceItems"
                    ADD COLUMN IF NOT EXISTS "CostAmountSnapshot" numeric(18,2) NULL;

                ALTER TABLE "ReturnItems"
                    ADD COLUMN IF NOT EXISTS "CostPriceSnapshot" numeric(18,6) NULL;

                ALTER TABLE "ReturnItems"
                    ADD COLUMN IF NOT EXISTS "CostAmountSnapshot" numeric(18,2) NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "ReturnItems"
                    DROP COLUMN IF EXISTS "CostAmountSnapshot";

                ALTER TABLE "ReturnItems"
                    DROP COLUMN IF EXISTS "CostPriceSnapshot";

                ALTER TABLE "SalesInvoiceItems"
                    DROP COLUMN IF EXISTS "CostAmountSnapshot";

                ALTER TABLE "SalesInvoiceItems"
                    DROP COLUMN IF EXISTS "CostPriceSnapshot";
                """);
        }
    }
}
