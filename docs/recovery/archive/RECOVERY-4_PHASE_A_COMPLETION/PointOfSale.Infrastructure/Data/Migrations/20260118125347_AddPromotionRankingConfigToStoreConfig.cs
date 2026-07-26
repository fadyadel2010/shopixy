using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPromotionRankingConfigToStoreConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "PromotionRanking_BaseBoost",
                table: "StoreConfigs",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<bool>(
                name: "PromotionRanking_EnableTimeDecay",
                table: "StoreConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PromotionRanking_Enabled",
                table: "StoreConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "PromotionRanking_HighDiscountBoost",
                table: "StoreConfigs",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "PromotionRanking_HighDiscountThreshold",
                table: "StoreConfigs",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "PromotionRanking_MaxTotalBoost",
                table: "StoreConfigs",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "PromotionRanking_TimeDecayMinimum",
                table: "StoreConfigs",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "PromotionRanking_TimeDecayOffsetDays",
                table: "StoreConfigs",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "PromotionRanking_TimeDecayScaleDays",
                table: "StoreConfigs",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "SearchBoostOverride",
                table: "Promotions",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PromotionRanking_BaseBoost",
                table: "StoreConfigs");

            migrationBuilder.DropColumn(
                name: "PromotionRanking_EnableTimeDecay",
                table: "StoreConfigs");

            migrationBuilder.DropColumn(
                name: "PromotionRanking_Enabled",
                table: "StoreConfigs");

            migrationBuilder.DropColumn(
                name: "PromotionRanking_HighDiscountBoost",
                table: "StoreConfigs");

            migrationBuilder.DropColumn(
                name: "PromotionRanking_HighDiscountThreshold",
                table: "StoreConfigs");

            migrationBuilder.DropColumn(
                name: "PromotionRanking_MaxTotalBoost",
                table: "StoreConfigs");

            migrationBuilder.DropColumn(
                name: "PromotionRanking_TimeDecayMinimum",
                table: "StoreConfigs");

            migrationBuilder.DropColumn(
                name: "PromotionRanking_TimeDecayOffsetDays",
                table: "StoreConfigs");

            migrationBuilder.DropColumn(
                name: "PromotionRanking_TimeDecayScaleDays",
                table: "StoreConfigs");

            migrationBuilder.DropColumn(
                name: "SearchBoostOverride",
                table: "Promotions");
        }
    }
}
