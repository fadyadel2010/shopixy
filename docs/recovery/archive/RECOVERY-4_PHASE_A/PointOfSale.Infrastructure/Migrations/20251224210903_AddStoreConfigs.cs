using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreConfigs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoreConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StoreDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ThemeId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Theme_Colors_Primary = table.Column<string>(type: "text", nullable: false),
                    Theme_Colors_Secondary = table.Column<string>(type: "text", nullable: false),
                    Theme_Colors_Background = table.Column<string>(type: "text", nullable: false),
                    Theme_Colors_Text = table.Column<string>(type: "text", nullable: false),
                    Theme_Colors_Muted = table.Column<string>(type: "text", nullable: false),
                    Theme_Font_Family = table.Column<string>(type: "text", nullable: false),
                    Theme_Font_BaseSize = table.Column<string>(type: "text", nullable: false),
                    Theme_Ui_BorderRadius = table.Column<string>(type: "text", nullable: false),
                    Theme_Ui_ButtonStyle = table.Column<string>(type: "text", nullable: false),
                    Theme_Layout_ProductCardVariant = table.Column<string>(type: "text", nullable: false),
                    Theme_Layout_HeaderVariant = table.Column<string>(type: "text", nullable: false),
                    Behavior_EnableGuestCheckout = table.Column<bool>(type: "boolean", nullable: false),
                    Behavior_ShowOutOfStockProducts = table.Column<bool>(type: "boolean", nullable: false),
                    Behavior_EnableSearch = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreConfigs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreConfigs_TenantId",
                table: "StoreConfigs",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreConfigs_ThemeId",
                table: "StoreConfigs",
                column: "ThemeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoreConfigs");
        }
    }
}
