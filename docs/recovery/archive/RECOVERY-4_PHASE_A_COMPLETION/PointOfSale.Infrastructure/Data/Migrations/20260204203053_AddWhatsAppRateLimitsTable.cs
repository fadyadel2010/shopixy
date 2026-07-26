using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppRateLimitsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WhatsAppRateLimits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MessageCount = table.Column<int>(type: "integer", nullable: false),
                    WindowStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastMessageAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OptedOut = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    OptedOutAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppRateLimits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppRateLimits_TenantId",
                table: "WhatsAppRateLimits",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppRateLimits_TenantId_PhoneNumber",
                table: "WhatsAppRateLimits",
                columns: new[] { "TenantId", "PhoneNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppRateLimits");
        }
    }
}
