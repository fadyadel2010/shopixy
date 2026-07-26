using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260705200000_Geo9CarrierGeographicResolution")]
    public partial class Geo9CarrierGeographicResolution : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Shipments"
                    ADD COLUMN IF NOT EXISTS "CarrierGeographicMappingReleaseId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "CarrierGeographicMappingVersion" bigint NULL,
                    ADD COLUMN IF NOT EXISTS "CarrierGeographicMappingReleaseKey" varchar(128) NULL,
                    ADD COLUMN IF NOT EXISTS "CarrierGeographicEnvironment" varchar(24) NULL,
                    ADD COLUMN IF NOT EXISTS "CarrierGeographicAccountScope" varchar(128) NULL,
                    ADD COLUMN IF NOT EXISTS "CarrierGeographicAccountScopeId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "CarrierGeographicMappingSnapshotJson" jsonb NULL,
                    ADD COLUMN IF NOT EXISTS "CarrierGeographicResolvedAtUtc" timestamp with time zone NULL;

                ALTER TABLE "Shipments"
                    ADD CONSTRAINT "FK_Shipments_CarrierGeographicMappingRelease"
                        FOREIGN KEY ("CarrierGeographicMappingReleaseId")
                        REFERENCES "CarrierGeographicMappingReleases" ("Id")
                        ON DELETE RESTRICT,
                    ADD CONSTRAINT "CK_Shipments_CarrierGeographicSnapshot" CHECK (
                        "CarrierGeographicMappingReleaseId" IS NULL OR
                        (
                            "CarrierGeographicMappingVersion" IS NOT NULL AND
                            "CarrierGeographicMappingReleaseKey" IS NOT NULL AND
                            "CarrierGeographicEnvironment" IS NOT NULL AND
                            "CarrierGeographicAccountScope" IS NOT NULL AND
                            "CarrierGeographicMappingSnapshotJson" IS NOT NULL AND
                            "CarrierGeographicResolvedAtUtc" IS NOT NULL AND
                            "DestinationCountryPlaceId" IS NOT NULL
                        )
                    );

                CREATE INDEX IF NOT EXISTS "IX_Shipments_TenantId_CarrierGeoMappingRelease"
                    ON "Shipments" ("TenantId", "CarrierGeographicMappingReleaseId");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Shipments"
                    DROP CONSTRAINT IF EXISTS "CK_Shipments_CarrierGeographicSnapshot";
                ALTER TABLE "Shipments"
                    DROP CONSTRAINT IF EXISTS "FK_Shipments_CarrierGeographicMappingRelease";
                DROP INDEX IF EXISTS "IX_Shipments_TenantId_CarrierGeoMappingRelease";
                """);
        }
    }
}
