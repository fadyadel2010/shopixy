using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PosUserManagementAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            // IX_Users_TenantId intentionally remains (blueprint: existing
            // non-unique TenantId index). TenantList is added alongside it.

            migrationBuilder.DropIndex(
                name: "IX_Users_Username",
                table: "Users");

            migrationBuilder.AddColumn<string>(
                name: "DisableReason",
                table: "Users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DisabledAtUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DisabledByUserId",
                table: "Users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InvitationAcceptedAtUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InvitedAtUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InvitedByUserId",
                table: "Users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedEmail",
                table: "Users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedUsername",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // Blueprint §12.2 expand sequence: backfill normalized identity
            // with the exact server normalizer equivalent (UPPER(BTRIM())).
            // Printable ASCII is enforced below because Unicode/whitespace
            // case-folding differs between .NET and PostgreSQL collations.
            migrationBuilder.Sql(
                """
                UPDATE "Users"
                SET "NormalizedUsername" = UPPER(BTRIM("Username")),
                    "NormalizedEmail" = UPPER(BTRIM("Email"));
                """);

            // Mandatory fail-closed preflight (§12.1). Any violation aborts
            // the migration with an explicit blocker. Production data is
            // never repaired, renamed, or deleted here.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM "Users"
                        WHERE "Username" !~ '^[ -~]+$'
                           OR "Email" !~ '^[ -~]+$'
                           OR BTRIM("Username") = ''
                           OR BTRIM("Email") = '') THEN
                        RAISE EXCEPTION 'USER_MGMT_PREFLIGHT_BLOCKER: POS identities must be non-empty printable ASCII so application and PostgreSQL normalization are identical.';
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM "Users"
                        GROUP BY UPPER(BTRIM("Username"))
                        HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION 'USER_MGMT_PREFLIGHT_BLOCKER: case-insensitive username collisions exist. Resolve duplicates before migrating.';
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM "Users"
                        GROUP BY UPPER(BTRIM("Email"))
                        HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION 'USER_MGMT_PREFLIGHT_BLOCKER: case-insensitive email collisions exist. Resolve duplicates before migrating.';
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM "Users" u
                        LEFT JOIN "Tenants" t ON t."Id" = u."TenantId"
                        WHERE t."Id" IS NULL) THEN
                        RAISE EXCEPTION 'USER_MGMT_PREFLIGHT_BLOCKER: orphan Users.TenantId values exist. Resolve orphans before migrating.';
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM "Users"
                        WHERE NOT "IsDeleted" AND "Role" = 0
                        GROUP BY "TenantId"
                        HAVING COUNT(*) <> 1) THEN
                        RAISE EXCEPTION 'USER_MGMT_PREFLIGHT_BLOCKER: Owner cardinality violated (tenants with zero or multiple non-deleted Owners). Resolve ownership before migrating.';
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM "Tenants" t
                        WHERE NOT EXISTS (
                            SELECT 1 FROM "Users" u
                            WHERE u."TenantId" = t."Id"
                              AND NOT u."IsDeleted"
                              AND u."Role" = 0)) THEN
                        RAISE EXCEPTION 'USER_MGMT_PREFLIGHT_BLOCKER: tenants with zero non-deleted Owners exist. Resolve ownership before migrating.';
                    END IF;
                END
                $$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedEmail",
                table: "Users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedUsername",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "PosUserSecurityTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosUserSecurityTokens", x => x.Id);
                    table.CheckConstraint("CK_PosUserSecurityTokens_ConsumedNotBeforeCreation", "\"ConsumedAtUtc\" IS NULL OR \"ConsumedAtUtc\" >= \"CreatedAtUtc\"");
                    table.CheckConstraint("CK_PosUserSecurityTokens_ExpiryAfterCreation", "\"ExpiresAtUtc\" > \"CreatedAtUtc\"");
                    table.CheckConstraint("CK_PosUserSecurityTokens_RevokedNotBeforeCreation", "\"RevokedAtUtc\" IS NULL OR \"RevokedAtUtc\" >= \"CreatedAtUtc\"");
                    table.ForeignKey(
                        name: "FK_PosUserSecurityTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedEmail",
                table: "Users",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedUsername",
                table: "Users",
                column: "NormalizedUsername",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_SoleOwner",
                table: "Users",
                column: "TenantId",
                unique: true,
                filter: "\"IsDeleted\" = FALSE AND \"Role\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantList",
                table: "Users",
                columns: new[] { "TenantId", "IsDeleted", "IsActive", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_PosUserSecurityTokens_ExpiresAtUtc",
                table: "PosUserSecurityTokens",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PosUserSecurityTokens_TenantUserPurposeExpiry",
                table: "PosUserSecurityTokens",
                columns: new[] { "TenantId", "UserId", "Purpose", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PosUserSecurityTokens_TokenHash",
                table: "PosUserSecurityTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PosUserSecurityTokens_UserId",
                table: "PosUserSecurityTokens",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Tenants_TenantId",
                table: "Users",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Tenants_TenantId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "PosUserSecurityTokens");

            migrationBuilder.DropIndex(
                name: "IX_Users_NormalizedEmail",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_NormalizedUsername",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_TenantId_SoleOwner",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_TenantList",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DisableReason",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DisabledAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DisabledByUserId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "InvitationAcceptedAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "InvitedAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "InvitedByUserId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NormalizedEmail",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NormalizedUsername",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }
    }
}
