using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalyticsAlertRules",
                columns: table => new
                {
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Metric = table.Column<int>(type: "integer", nullable: false),
                    Operator = table.Column<int>(type: "integer", nullable: false),
                    Threshold = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    WindowMinutes = table.Column<int>(type: "integer", nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CooldownMinutes = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalyticsAlertRules", x => x.RuleId);
                });

            migrationBuilder.CreateTable(
                name: "CampaignCosts",
                columns: table => new
                {
                    CampaignCostId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Medium = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Campaign = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PeriodStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CostAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignCosts", x => x.CampaignCostId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsAlertRules_TenantId",
                table: "AnalyticsAlertRules",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsAlertRules_TenantId_IsEnabled_IsDeleted",
                table: "AnalyticsAlertRules",
                columns: new[] { "TenantId", "IsEnabled", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignCosts_TenantId",
                table: "CampaignCosts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignCosts_TenantId_PeriodStartUtc_PeriodEndUtc",
                table: "CampaignCosts",
                columns: new[] { "TenantId", "PeriodStartUtc", "PeriodEndUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignCosts_TenantId_Source_Medium_Campaign_PeriodStartUtc",
                table: "CampaignCosts",
                columns: new[] { "TenantId", "Source", "Medium", "Campaign", "PeriodStartUtc" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalyticsAlertRules");

            migrationBuilder.DropTable(
                name: "CampaignCosts");
        }
    }
}
