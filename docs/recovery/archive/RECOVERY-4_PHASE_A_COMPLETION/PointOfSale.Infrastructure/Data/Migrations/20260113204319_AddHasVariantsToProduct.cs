using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHasVariantsToProduct : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasVariants",
                table: "Products",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "PaidAmount",
                table: "OrderItems",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RefundedAmount",
                table: "OrderItems",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
            
            // PHASE 11.5: Set HasVariants = true for products that have more than 1 variant
            migrationBuilder.Sql(@"
                UPDATE ""Products"" p
                SET ""HasVariants"" = true
                WHERE (
                    SELECT COUNT(*) 
                    FROM ""ProductVariants"" v 
                    WHERE v.""ProductId"" = p.""Id""
                ) > 1;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasVariants",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "PaidAmount",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "RefundedAmount",
                table: "OrderItems");
        }
    }
}
