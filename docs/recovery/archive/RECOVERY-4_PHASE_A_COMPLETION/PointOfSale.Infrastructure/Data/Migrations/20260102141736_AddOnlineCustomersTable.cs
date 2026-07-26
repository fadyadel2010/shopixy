using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PointOfSale.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOnlineCustomersTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomizationJson",
                table: "StoreConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "LiveVersion",
                table: "StoreConfigs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ThemeDraftOverridesJson",
                table: "StoreConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ThemeLiveOverridesJson",
                table: "StoreConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ThemePresetKey",
                table: "StoreConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VersionsJson",
                table: "StoreConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "OnlineCustomers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProviderUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    EmailVerified = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastLoginAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnlineCustomers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ThemePresets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsRtl = table.Column<bool>(type: "boolean", nullable: false),
                    BaseThemeJson = table.Column<string>(type: "text", nullable: false),
                    PreviewImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThemePresets", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "ThemePresets",
                columns: new[] { "Id", "BaseThemeJson", "CreatedAtUtc", "IsRtl", "Key", "Name", "PreviewImageUrl" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111111"), "{\"colors\":{\"primary\":\"#2563eb\",\"secondary\":\"#0f172a\",\"background\":\"#ffffff\",\"text\":\"#020617\",\"muted\":\"#64748b\"},\"typography\":{\"fontFamily\":\"Inter\",\"baseSize\":\"16px\"},\"radius\":{\"base\":\"8px\"}}", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), false, "default", "Default Modern", null },
                    { new Guid("22222222-2222-2222-2222-222222222222"), "{\"colors\":{\"primary\":\"#3b82f6\",\"secondary\":\"#1e293b\",\"background\":\"#0f172a\",\"text\":\"#f8fafc\",\"muted\":\"#94a3b8\"},\"typography\":{\"fontFamily\":\"Inter\",\"baseSize\":\"16px\"},\"radius\":{\"base\":\"12px\"}}", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), false, "dark-minimal", "Dark Minimal", null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCustomers_CreatedAtUtc",
                table: "OnlineCustomers",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCustomers_EmailVerified",
                table: "OnlineCustomers",
                column: "EmailVerified");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCustomers_IsActive",
                table: "OnlineCustomers",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCustomers_Provider",
                table: "OnlineCustomers",
                column: "Provider");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCustomers_TenantId",
                table: "OnlineCustomers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCustomers_TenantId_Email",
                table: "OnlineCustomers",
                columns: new[] { "TenantId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCustomers_TenantId_Provider_ProviderUserId",
                table: "OnlineCustomers",
                columns: new[] { "TenantId", "Provider", "ProviderUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ThemePresets_Key",
                table: "ThemePresets",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OnlineCustomers");

            migrationBuilder.DropTable(
                name: "ThemePresets");

            migrationBuilder.DropColumn(
                name: "CustomizationJson",
                table: "StoreConfigs");

            migrationBuilder.DropColumn(
                name: "LiveVersion",
                table: "StoreConfigs");

            migrationBuilder.DropColumn(
                name: "ThemeDraftOverridesJson",
                table: "StoreConfigs");

            migrationBuilder.DropColumn(
                name: "ThemeLiveOverridesJson",
                table: "StoreConfigs");

            migrationBuilder.DropColumn(
                name: "ThemePresetKey",
                table: "StoreConfigs");

            migrationBuilder.DropColumn(
                name: "VersionsJson",
                table: "StoreConfigs");
        }
    }
}
