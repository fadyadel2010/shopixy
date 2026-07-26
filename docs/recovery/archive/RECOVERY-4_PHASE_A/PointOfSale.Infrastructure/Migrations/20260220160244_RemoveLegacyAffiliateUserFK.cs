using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyAffiliateUserFK : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AffiliateAccounts_Users_UserId",
                table: "AffiliateAccounts");

            migrationBuilder.DropIndex(
                name: "IX_AffiliateAccounts_UserId",
                table: "AffiliateAccounts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AffiliateAccounts_UserId",
                table: "AffiliateAccounts",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AffiliateAccounts_Users_UserId",
                table: "AffiliateAccounts",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
