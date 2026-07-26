using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260423193000_S19B_PromotionFallbackAndManualCurrencyReview")]
    public partial class S19BPromotionFallbackAndManualCurrencyReview : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "StoreConfigs"
                    ADD COLUMN IF NOT EXISTS "Behavior_FailCheckoutOnPromotionError" boolean NOT NULL DEFAULT false;

                ALTER TABLE "CommerceHubPayments"
                    ADD COLUMN IF NOT EXISTS "NeedsManualCurrencyReview" boolean NOT NULL DEFAULT false;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "StoreConfigs"
                    DROP COLUMN IF EXISTS "Behavior_FailCheckoutOnPromotionError";

                ALTER TABLE "CommerceHubPayments"
                    DROP COLUMN IF EXISTS "NeedsManualCurrencyReview";
                """);
        }
    }
}
