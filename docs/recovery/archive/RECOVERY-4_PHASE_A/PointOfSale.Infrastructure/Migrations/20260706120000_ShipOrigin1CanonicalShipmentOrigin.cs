using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260706120000_ShipOrigin1CanonicalShipmentOrigin")]
    public partial class ShipOrigin1CanonicalShipmentOrigin : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Shipments"
                    ADD COLUMN IF NOT EXISTS "OriginWarehouseId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "OriginAddressAdmissionEvidenceId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "OriginGeographicCatalogReleaseId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "OriginCountryPlaceId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "OriginAdministrativeAreaPlaceId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "OriginLocalityPlaceId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "OriginDistrictPlaceId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "OriginZonePlaceId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "OriginCountryIso2" varchar(2) NULL,
                    ADD COLUMN IF NOT EXISTS "OriginCountryDisplayName" varchar(200) NULL,
                    ADD COLUMN IF NOT EXISTS "OriginAdministrativeAreaDisplayName" varchar(200) NULL,
                    ADD COLUMN IF NOT EXISTS "OriginLocalityDisplayName" varchar(200) NULL,
                    ADD COLUMN IF NOT EXISTS "OriginDistrictDisplayName" varchar(200) NULL,
                    ADD COLUMN IF NOT EXISTS "OriginZoneDisplayName" varchar(200) NULL,
                    ADD COLUMN IF NOT EXISTS "OriginContactName" varchar(200) NULL,
                    ADD COLUMN IF NOT EXISTS "OriginContactPhone" varchar(64) NULL,
                    ADD COLUMN IF NOT EXISTS "OriginStreet" varchar(500) NULL,
                    ADD COLUMN IF NOT EXISTS "OriginGeographicSnapshotCreatedAtUtc" timestamp with time zone NULL,
                    ADD COLUMN IF NOT EXISTS "OriginGeographicSnapshotVersion" integer NULL,
                    ADD COLUMN IF NOT EXISTS "OriginSnapshotJson" jsonb NULL;

                ALTER TABLE "Shipments"
                    ADD CONSTRAINT "CK_Shipments_OriginSnapshotComplete" CHECK (
                        "OriginSnapshotJson" IS NULL OR
                        (
                            "OriginWarehouseId" IS NOT NULL AND
                            "OriginAddressAdmissionEvidenceId" IS NOT NULL AND
                            "OriginGeographicCatalogReleaseId" IS NOT NULL AND
                            "OriginCountryPlaceId" IS NOT NULL AND
                            "OriginCountryIso2" IS NOT NULL AND
                            "OriginCountryDisplayName" IS NOT NULL AND
                            "OriginContactName" IS NOT NULL AND
                            "OriginContactPhone" IS NOT NULL AND
                            "OriginStreet" IS NOT NULL AND
                            "OriginGeographicSnapshotCreatedAtUtc" IS NOT NULL AND
                            "OriginGeographicSnapshotVersion" = 1
                        )
                    );

                CREATE INDEX IF NOT EXISTS "IX_Shipments_OriginAddressAdmissionEvidenceId"
                    ON "Shipments" ("OriginAddressAdmissionEvidenceId");
                CREATE INDEX IF NOT EXISTS "IX_Shipments_TenantId_OriginGeoRelease_Country"
                    ON "Shipments" ("TenantId", "OriginGeographicCatalogReleaseId", "OriginCountryPlaceId");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Shipments"
                    DROP CONSTRAINT IF EXISTS "CK_Shipments_OriginSnapshotComplete";
                DROP INDEX IF EXISTS "IX_Shipments_OriginAddressAdmissionEvidenceId";
                DROP INDEX IF EXISTS "IX_Shipments_TenantId_OriginGeoRelease_Country";
                """);
        }
    }
}
