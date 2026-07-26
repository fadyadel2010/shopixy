using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260721121000_AffiliateRefreshReplaySentinel")]
public sealed class AffiliateRefreshReplaySentinel : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PreviousRefreshToken",
            table: "AffiliateUsers",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_AffiliateUsers_PreviousRefreshToken",
            table: "AffiliateUsers",
            column: "PreviousRefreshToken",
            filter: "\"PreviousRefreshToken\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_AffiliateUsers_PreviousRefreshToken",
            table: "AffiliateUsers");

        migrationBuilder.DropColumn(
            name: "PreviousRefreshToken",
            table: "AffiliateUsers");
    }
}
