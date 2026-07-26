using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S12B1_CheckoutSessionConsistency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE EXTENSION IF NOT EXISTS pgcrypto;

                WITH ranked_sessions AS (
                    SELECT
                        "Id",
                        ROW_NUMBER() OVER (
                            PARTITION BY "TenantId", "CartId"
                            ORDER BY
                                CASE WHEN "Status" = 0 THEN 0 ELSE 1 END,
                                "LastActivityAtUtc" DESC,
                                "CreatedAtUtc" DESC,
                                "Id" DESC
                        ) AS row_num
                    FROM "CheckoutSessions"
                    WHERE "Status" IN (0, 1)
                ),
                duplicate_sessions AS (
                    SELECT "Id"
                    FROM ranked_sessions
                    WHERE row_num > 1
                ),
                token_conflicts AS (
                    SELECT s1."Id"
                    FROM "CheckoutSessions" s1
                    INNER JOIN duplicate_sessions ds ON s1."Id" = ds."Id"
                    WHERE s1."RecoveryToken" IS NOT NULL
                      AND EXISTS (
                        SELECT 1 
                        FROM "CheckoutSessions" s2 
                        WHERE s2."RecoveryToken" = s1."RecoveryToken" 
                          AND s2."Id" != s1."Id"
                      )
                )
                UPDATE "CheckoutSessions" AS session
                SET
                    "Status" = 1,
                    "AbandonedAtUtc" = COALESCE(session."AbandonedAtUtc", NOW()),
                    "LastActivityAtUtc" = session."LastActivityAtUtc",
                    "RecoveryToken" = 
                        CASE 
                            WHEN session."RecoveryToken" IS NULL OR session."Id" IN (SELECT "Id" FROM token_conflicts) THEN
                                ENCODE(gen_random_bytes(32), 'hex')
                            ELSE 
                                session."RecoveryToken"
                        END
                WHERE session."Id" IN (SELECT "Id" FROM duplicate_sessions);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_TenantId_CartId_Active_Unique",
                table: "CheckoutSessions",
                columns: new[] { "TenantId", "CartId", "Status" },
                unique: true,
                filter: "\"Status\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CheckoutSessions_TenantId_CartId_Active_Unique",
                table: "CheckoutSessions");
        }
    }
}
