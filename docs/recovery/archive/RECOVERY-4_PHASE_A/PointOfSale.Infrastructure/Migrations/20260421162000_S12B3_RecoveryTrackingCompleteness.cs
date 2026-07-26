using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260421162000_S12B3_RecoveryTrackingCompleteness")]
    public partial class S12B3_RecoveryTrackingCompleteness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "AbandonedCarts"
                    ADD COLUMN IF NOT EXISTS "SessionId" character varying(128),
                    ADD COLUMN IF NOT EXISTS "ContactChannel" character varying(32),
                    ADD COLUMN IF NOT EXISTS "PhoneVerified" boolean NOT NULL DEFAULT false,
                    ADD COLUMN IF NOT EXISTS "EmailVerified" boolean NOT NULL DEFAULT false;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "PhoneNumber",
                table: "AbandonedCarts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "AbandonedCarts",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS "IX_AbandonedCarts_TenantId_SessionId"
                ON "AbandonedCarts" ("TenantId", "SessionId");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """DROP INDEX IF EXISTS "IX_AbandonedCarts_TenantId_SessionId";""");

            migrationBuilder.AlterColumn<string>(
                name: "PhoneNumber",
                table: "AbandonedCarts",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "AbandonedCarts",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.Sql(
                """
                ALTER TABLE "AbandonedCarts"
                    DROP COLUMN IF EXISTS "SessionId",
                    DROP COLUMN IF EXISTS "ContactChannel",
                    DROP COLUMN IF EXISTS "PhoneVerified",
                    DROP COLUMN IF EXISTS "EmailVerified";
                """);
        }
    }
}
