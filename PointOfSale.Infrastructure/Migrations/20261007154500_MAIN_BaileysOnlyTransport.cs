using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261007154500_MAIN_BaileysOnlyTransport")]
public partial class MAIN_BaileysOnlyTransport : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "Tenants"
            SET "UseBaileys" = TRUE
            WHERE "UseBaileys" = FALSE;
            """);

        migrationBuilder.AlterColumn<bool>(
            name: "UseBaileys",
            table: "Tenants",
            type: "boolean",
            nullable: false,
            defaultValue: true,
            oldClrType: typeof(bool),
            oldType: "boolean");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<bool>(
            name: "UseBaileys",
            table: "Tenants",
            type: "boolean",
            nullable: false,
            oldClrType: typeof(bool),
            oldType: "boolean",
            oldDefaultValue: true);
    }
}
