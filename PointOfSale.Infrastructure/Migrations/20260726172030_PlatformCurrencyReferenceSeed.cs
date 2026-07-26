using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PlatformCurrencyReferenceSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "Currencies" ("Code", "Name", "Symbol", "DecimalPlaces")
                VALUES ('EGP', 'Egyptian Pound', 'EGP', 2)
                ON CONFLICT ("Code") DO UPDATE
                SET "Name" = EXCLUDED."Name",
                    "Symbol" = EXCLUDED."Symbol",
                    "DecimalPlaces" = EXCLUDED."DecimalPlaces";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "EGP");
        }
    }
}
