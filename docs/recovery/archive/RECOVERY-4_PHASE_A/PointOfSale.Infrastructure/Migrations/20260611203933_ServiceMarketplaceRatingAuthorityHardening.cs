using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ServiceMarketplaceRatingAuthorityHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_ServiceReviews_Rating_Range",
                table: "ServiceReviews",
                sql: "\"Rating\" >= 1 AND \"Rating\" <= 5");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ServiceReviews_Rating_Range",
                table: "ServiceReviews");
        }
    }
}
