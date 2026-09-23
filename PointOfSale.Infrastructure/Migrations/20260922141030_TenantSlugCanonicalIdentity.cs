using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TenantSlugCanonicalIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                return;
            }

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "Tenants"
                        WHERE "Slug" IS NOT NULL
                          AND NOT (
                              "Slug" = LOWER(BTRIM("Slug"))
                              AND LENGTH("Slug") BETWEEN 3 AND 63
                              AND "Slug" ~ '^[a-z0-9]([a-z0-9-]*[a-z0-9])?$'
                              AND POSITION('--' IN "Slug") = 0)) THEN
                        RAISE EXCEPTION 'TENANT_SLUG_PREFLIGHT_BLOCKER: non-canonical tenant slugs exist. Resolve them before applying this migration.';
                    END IF;
                END
                $$;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tenants_Slug_Canonical",
                table: "Tenants",
                sql: "\"Slug\" IS NULL OR (\"Slug\" = LOWER(BTRIM(\"Slug\")) AND LENGTH(\"Slug\") BETWEEN 3 AND 63 AND \"Slug\" ~ '^[a-z0-9]([a-z0-9-]*[a-z0-9])?$' AND POSITION('--' IN \"Slug\") = 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                return;
            }

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tenants_Slug_Canonical",
                table: "Tenants");
        }
    }
}
