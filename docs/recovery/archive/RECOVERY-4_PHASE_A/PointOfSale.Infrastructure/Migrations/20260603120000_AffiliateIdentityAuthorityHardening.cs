using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AffiliateIdentityAuthorityHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TokensValidAfterUtc",
                table: "AffiliateUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AffiliateEmailVerificationTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AffiliateUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliateEmailVerificationTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AffiliateEmailVerificationTokens_AffiliateUsers_AffiliateU~",
                        column: x => x.AffiliateUserId,
                        principalTable: "AffiliateUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AffiliatePasswordResetTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AffiliateUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliatePasswordResetTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AffiliatePasswordResetTokens_AffiliateUsers_AffiliateUser~",
                        column: x => x.AffiliateUserId,
                        principalTable: "AffiliateUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateEmailVerificationTokens_ExpiresAtUtc",
                table: "AffiliateEmailVerificationTokens",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateEmailVerificationTokens_TokenHash",
                table: "AffiliateEmailVerificationTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateEmailVerificationTokens_User_Consumed",
                table: "AffiliateEmailVerificationTokens",
                columns: new[] { "AffiliateUserId", "ConsumedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliatePasswordResetTokens_ExpiresAtUtc",
                table: "AffiliatePasswordResetTokens",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliatePasswordResetTokens_TokenHash",
                table: "AffiliatePasswordResetTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AffiliatePasswordResetTokens_User_Consumed",
                table: "AffiliatePasswordResetTokens",
                columns: new[] { "AffiliateUserId", "ConsumedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AffiliateEmailVerificationTokens");
            migrationBuilder.DropTable(name: "AffiliatePasswordResetTokens");

            migrationBuilder.DropColumn(
                name: "TokensValidAfterUtc",
                table: "AffiliateUsers");
        }
    }
}
