using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260426212900_AddCanonicalIdentityLinksEfWrapper")]
    public partial class AddCanonicalIdentityLinksEfWrapper : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS "CanonicalIdentityLinks"
                (
                    "Id" uuid NOT NULL,
                    "CanonicalIdentityId" uuid NOT NULL,
                    "SubjectType" integer NOT NULL,
                    "SubjectId" uuid NOT NULL,
                    "TenantId" uuid NULL,
                    "SourceSystem" character varying(64) NOT NULL,
                    "DisplayKey" character varying(255) NULL,
                    "LinkedAtUtc" timestamp with time zone NOT NULL,
                    "RevokedAtUtc" timestamp with time zone NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NULL,
                    "CreatedBy" uuid NULL,
                    "UpdatedBy" uuid NULL,
                    "IsDeleted" boolean NOT NULL DEFAULT false,
                    CONSTRAINT "PK_CanonicalIdentityLinks" PRIMARY KEY ("Id")
                );

                CREATE INDEX IF NOT EXISTS "IX_CanonicalIdentityLinks_CanonicalIdentityId"
                    ON "CanonicalIdentityLinks" ("CanonicalIdentityId");

                CREATE INDEX IF NOT EXISTS "IX_CanonicalIdentityLinks_Tenant_Subject"
                    ON "CanonicalIdentityLinks" ("TenantId", "SubjectType", "SubjectId");

                CREATE INDEX IF NOT EXISTS "IX_CanonicalIdentityLinks_Source_DisplayKey"
                    ON "CanonicalIdentityLinks" ("SourceSystem", "DisplayKey")
                    WHERE "DisplayKey" IS NOT NULL AND "IsDeleted" = false AND "RevokedAtUtc" IS NULL;

                CREATE UNIQUE INDEX IF NOT EXISTS "UX_CanonicalIdentityLinks_Tenant_Subject_Active"
                    ON "CanonicalIdentityLinks" ("TenantId", "SubjectType", "SubjectId")
                    WHERE "TenantId" IS NOT NULL AND "IsDeleted" = false AND "RevokedAtUtc" IS NULL;

                CREATE UNIQUE INDEX IF NOT EXISTS "UX_CanonicalIdentityLinks_Global_Subject_Active"
                    ON "CanonicalIdentityLinks" ("SubjectType", "SubjectId")
                    WHERE "TenantId" IS NULL AND "IsDeleted" = false AND "RevokedAtUtc" IS NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "UX_CanonicalIdentityLinks_Global_Subject_Active";
                DROP INDEX IF EXISTS "UX_CanonicalIdentityLinks_Tenant_Subject_Active";
                DROP INDEX IF EXISTS "IX_CanonicalIdentityLinks_Source_DisplayKey";
                DROP INDEX IF EXISTS "IX_CanonicalIdentityLinks_Tenant_Subject";
                DROP INDEX IF EXISTS "IX_CanonicalIdentityLinks_CanonicalIdentityId";
                DROP TABLE IF EXISTS "CanonicalIdentityLinks";
                """);
        }
    }
}
