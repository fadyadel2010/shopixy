using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

public partial class StorefrontThemeAuthorityHardening : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ThemeLivePresetKey",
            table: "StoreConfigs",
            maxLength: 100,
            nullable: false,
            defaultValue: "default");

        if (migrationBuilder.ActiveProvider.Contains("SqlServer"))
        {
            migrationBuilder.Sql("""
                UPDATE [StoreConfigs]
                SET [ThemeLivePresetKey] = COALESCE(NULLIF([ThemePresetKey], ''), 'default');
                """);
        }
        else
        {
            migrationBuilder.Sql("""
                UPDATE "StoreConfigs"
                SET "ThemeLivePresetKey" = COALESCE(NULLIF("ThemePresetKey", ''), 'default');
                """);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ThemeLivePresetKey",
            table: "StoreConfigs");
    }
}
