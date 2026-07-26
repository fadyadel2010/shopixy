using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    public partial class ServiceMarketplaceRequestAuthorityHardening : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProviderProfileId",
                table: "ServiceRequests",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.AddColumn<DateTime>(
                name: "RejectedAt",
                table: "ServiceRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceCategorySnapshot",
                table: "ServiceRequests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceCurrencySnapshot",
                table: "ServiceRequests",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServicePackagesSnapshot",
                table: "ServiceRequests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ServicePricingTypeSnapshot",
                table: "ServiceRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ServiceStartingPriceSnapshot",
                table: "ServiceRequests",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceTitleSnapshot",
                table: "ServiceRequests",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(@"
                UPDATE ""ServiceRequests"" sr
                SET
                    ""ProviderProfileId"" = COALESCE(s.""ProfileId"", '00000000-0000-0000-0000-000000000000'::uuid),
                    ""ServiceTitleSnapshot"" = COALESCE(NULLIF(s.""Title"", ''), 'Historical service'),
                    ""ServiceCategorySnapshot"" = s.""Category"",
                    ""ServiceStartingPriceSnapshot"" = s.""StartingPrice"",
                    ""ServicePricingTypeSnapshot"" = s.""PricingType"",
                    ""ServiceCurrencySnapshot"" = s.""Currency"",
                    ""ServicePackagesSnapshot"" = s.""Packages""
                FROM ""Services"" s
                WHERE sr.""ServiceId"" = s.""Id"";");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM ""ServiceRequests""
                        WHERE ""IsDeleted"" = FALSE
                          AND ""Status"" IN ('Pending', 'Accepted', 'CompletedByProvider')
                        GROUP BY ""TenantId"", ""ServiceId"", ""BuyerUserId""
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Cannot create UX_ServiceRequests_Active_Service_Buyer: duplicate active service requests exist';
                    END IF;
                END $$;");

            migrationBuilder.CreateIndex(
                name: "UX_ServiceRequests_Active_Service_Buyer",
                table: "ServiceRequests",
                columns: new[] { "TenantId", "ServiceId", "BuyerUserId" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE AND \"Status\" IN ('Pending', 'Accepted', 'CompletedByProvider')");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_TenantId_ProviderUserId_Status",
                table: "ServiceRequests",
                columns: new[] { "TenantId", "ProviderUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_TenantId_BuyerUserId_Status",
                table: "ServiceRequests",
                columns: new[] { "TenantId", "BuyerUserId", "Status" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_ServiceRequests_Active_Service_Buyer",
                table: "ServiceRequests");

            migrationBuilder.DropIndex(
                name: "IX_ServiceRequests_TenantId_ProviderUserId_Status",
                table: "ServiceRequests");

            migrationBuilder.DropIndex(
                name: "IX_ServiceRequests_TenantId_BuyerUserId_Status",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(name: "ProviderProfileId", table: "ServiceRequests");
            migrationBuilder.DropColumn(name: "RejectedAt", table: "ServiceRequests");
            migrationBuilder.DropColumn(name: "ServiceCategorySnapshot", table: "ServiceRequests");
            migrationBuilder.DropColumn(name: "ServiceCurrencySnapshot", table: "ServiceRequests");
            migrationBuilder.DropColumn(name: "ServicePackagesSnapshot", table: "ServiceRequests");
            migrationBuilder.DropColumn(name: "ServicePricingTypeSnapshot", table: "ServiceRequests");
            migrationBuilder.DropColumn(name: "ServiceStartingPriceSnapshot", table: "ServiceRequests");
            migrationBuilder.DropColumn(name: "ServiceTitleSnapshot", table: "ServiceRequests");
        }
    }
}
