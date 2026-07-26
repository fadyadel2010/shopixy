using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260721122000_AffiliateRefreshTokenReplayLedger")]
public sealed class AffiliateRefreshTokenReplayLedger : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AffiliateRefreshTokenReplayEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                AffiliateUserId = table.Column<Guid>(type: "uuid", nullable: false),
                TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                RotatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ReplayedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AffiliateRefreshTokenReplayEntries", x => x.Id);
                table.ForeignKey(
                    name: "FK_AffiliateRefreshTokenReplayEntries_AffiliateUsers_AffiliateUserId",
                    column: x => x.AffiliateUserId,
                    principalTable: "AffiliateUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AffiliateRefreshTokenReplayEntries_TokenHash",
            table: "AffiliateRefreshTokenReplayEntries",
            column: "TokenHash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AffiliateRefreshTokenReplayEntries_User_Expires",
            table: "AffiliateRefreshTokenReplayEntries",
            columns: new[] { "AffiliateUserId", "ExpiresAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_AffiliateRefreshTokenReplayEntries_ReplayedAtUtc",
            table: "AffiliateRefreshTokenReplayEntries",
            column: "ReplayedAtUtc");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AffiliateRefreshTokenReplayEntries");
    }
}
