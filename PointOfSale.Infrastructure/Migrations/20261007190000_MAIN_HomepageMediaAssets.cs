using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261007190000_MAIN_HomepageMediaAssets")]
public sealed class MAIN_HomepageMediaAssets : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("HomepageMediaAssets", columns: table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            TenantId = table.Column<Guid>(type: "uuid", nullable: false),
            ObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
            ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
            Size = table.Column<long>(type: "bigint", nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_HomepageMediaAssets", asset => asset.Id);
            table.ForeignKey("FK_HomepageMediaAssets_Tenants_TenantId", asset => asset.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_HomepageMediaAssets_TenantId_Id", "HomepageMediaAssets", new[] { "TenantId", "Id" });
        migrationBuilder.CreateIndex("IX_HomepageMediaAssets_ObjectKey", "HomepageMediaAssets", "ObjectKey", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("HomepageMediaAssets");
}
