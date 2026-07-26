using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260419190000_AddCheckoutSessionManualWalletRecovery")]
    public partial class AddCheckoutSessionManualWalletRecovery : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProofImageUrl",
                table: "CheckoutSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WalletPhoneNumber",
                table: "CheckoutSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WalletType",
                table: "CheckoutSessions",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProofImageUrl",
                table: "CheckoutSessions");

            migrationBuilder.DropColumn(
                name: "WalletPhoneNumber",
                table: "CheckoutSessions");

            migrationBuilder.DropColumn(
                name: "WalletType",
                table: "CheckoutSessions");
        }
    }
}
