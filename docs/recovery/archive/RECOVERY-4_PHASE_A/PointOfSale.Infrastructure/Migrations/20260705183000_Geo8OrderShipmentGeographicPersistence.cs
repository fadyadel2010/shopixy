using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260705183000_Geo8OrderShipmentGeographicPersistence")]
    public partial class Geo8OrderShipmentGeographicPersistence : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Orders"
                    ADD COLUMN IF NOT EXISTS "ShippingAddressAdmissionEvidenceId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "ShippingGeographicCatalogReleaseId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "ShippingCountryPlaceId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "ShippingAdministrativeAreaPlaceId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "ShippingLocalityPlaceId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "ShippingDistrictPlaceId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "ShippingZonePlaceId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "ShippingCountryDisplayName" varchar(200) NULL,
                    ADD COLUMN IF NOT EXISTS "ShippingAdministrativeAreaDisplayName" varchar(200) NULL,
                    ADD COLUMN IF NOT EXISTS "ShippingLocalityDisplayName" varchar(200) NULL,
                    ADD COLUMN IF NOT EXISTS "ShippingDistrictDisplayName" varchar(200) NULL,
                    ADD COLUMN IF NOT EXISTS "ShippingZoneDisplayName" varchar(200) NULL,
                    ADD COLUMN IF NOT EXISTS "ShippingStreet" varchar(500) NULL,
                    ADD COLUMN IF NOT EXISTS "ShippingStreet2" varchar(500) NULL,
                    ADD COLUMN IF NOT EXISTS "ShippingBuilding" varchar(120) NULL,
                    ADD COLUMN IF NOT EXISTS "ShippingFloor" varchar(64) NULL,
                    ADD COLUMN IF NOT EXISTS "ShippingApartment" varchar(64) NULL,
                    ADD COLUMN IF NOT EXISTS "ShippingLandmark" varchar(200) NULL,
                    ADD COLUMN IF NOT EXISTS "ShippingGeographicSnapshotCreatedAtUtc" timestamp with time zone NULL,
                    ADD COLUMN IF NOT EXISTS "ShippingGeographicSnapshotVersion" integer NULL,
                    ADD COLUMN IF NOT EXISTS "BillingAddressAdmissionEvidenceId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "BillingGeographicCatalogReleaseId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "BillingCountryPlaceId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "BillingAdministrativeAreaPlaceId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "BillingLocalityPlaceId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "BillingDistrictPlaceId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "BillingZonePlaceId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "BillingCountryDisplayName" varchar(200) NULL,
                    ADD COLUMN IF NOT EXISTS "BillingAdministrativeAreaDisplayName" varchar(200) NULL,
                    ADD COLUMN IF NOT EXISTS "BillingLocalityDisplayName" varchar(200) NULL,
                    ADD COLUMN IF NOT EXISTS "BillingDistrictDisplayName" varchar(200) NULL,
                    ADD COLUMN IF NOT EXISTS "BillingZoneDisplayName" varchar(200) NULL,
                    ADD COLUMN IF NOT EXISTS "BillingStreet" varchar(500) NULL,
                    ADD COLUMN IF NOT EXISTS "BillingStreet2" varchar(500) NULL,
                    ADD COLUMN IF NOT EXISTS "BillingBuilding" varchar(120) NULL,
                    ADD COLUMN IF NOT EXISTS "BillingFloor" varchar(64) NULL,
                    ADD COLUMN IF NOT EXISTS "BillingApartment" varchar(64) NULL,
                    ADD COLUMN IF NOT EXISTS "BillingLandmark" varchar(200) NULL,
                    ADD COLUMN IF NOT EXISTS "BillingGeographicSnapshotCreatedAtUtc" timestamp with time zone NULL,
                    ADD COLUMN IF NOT EXISTS "BillingGeographicSnapshotVersion" integer NULL;

                ALTER TABLE "Shipments"
                    ADD COLUMN IF NOT EXISTS "DestinationAddressAdmissionEvidenceId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "DestinationGeographicCatalogReleaseId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "DestinationCountryPlaceId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "DestinationAdministrativeAreaPlaceId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "DestinationLocalityPlaceId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "DestinationDistrictPlaceId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "DestinationZonePlaceId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "DestinationCountryDisplayName" varchar(200) NULL,
                    ADD COLUMN IF NOT EXISTS "DestinationAdministrativeAreaDisplayName" varchar(200) NULL,
                    ADD COLUMN IF NOT EXISTS "DestinationLocalityDisplayName" varchar(200) NULL,
                    ADD COLUMN IF NOT EXISTS "DestinationDistrictDisplayName" varchar(200) NULL,
                    ADD COLUMN IF NOT EXISTS "DestinationZoneDisplayName" varchar(200) NULL,
                    ADD COLUMN IF NOT EXISTS "DestinationStreet" varchar(500) NULL,
                    ADD COLUMN IF NOT EXISTS "DestinationStreet2" varchar(500) NULL,
                    ADD COLUMN IF NOT EXISTS "DestinationBuilding" varchar(120) NULL,
                    ADD COLUMN IF NOT EXISTS "DestinationFloor" varchar(64) NULL,
                    ADD COLUMN IF NOT EXISTS "DestinationApartment" varchar(64) NULL,
                    ADD COLUMN IF NOT EXISTS "DestinationLandmark" varchar(200) NULL,
                    ADD COLUMN IF NOT EXISTS "DestinationGeographicSnapshotCreatedAtUtc" timestamp with time zone NULL,
                    ADD COLUMN IF NOT EXISTS "DestinationGeographicSnapshotVersion" integer NULL;

                CREATE TABLE IF NOT EXISTS "CustomerAddresses" (
                    "Id" uuid PRIMARY KEY,
                    "TenantId" uuid NOT NULL,
                    "CustomerId" uuid NOT NULL,
                    "CurrentVersionId" uuid NULL,
                    "IsDefault" boolean NOT NULL DEFAULT false,
                    "IsArchived" boolean NOT NULL DEFAULT false,
                    "Version" integer NOT NULL DEFAULT 0,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NULL,
                    "IsDeleted" boolean NOT NULL DEFAULT false,
                    CONSTRAINT "FK_CustomerAddresses_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE RESTRICT
                );

                CREATE TABLE IF NOT EXISTS "CustomerAddressVersions" (
                    "Id" uuid PRIMARY KEY,
                    "TenantId" uuid NOT NULL,
                    "CustomerAddressId" uuid NOT NULL,
                    "Version" integer NOT NULL,
                    "AddressAdmissionEvidenceId" uuid NOT NULL,
                    "GeographicCatalogReleaseId" uuid NOT NULL,
                    "CountryPlaceId" uuid NOT NULL,
                    "AdministrativeAreaPlaceId" uuid NULL,
                    "LocalityPlaceId" uuid NULL,
                    "DistrictPlaceId" uuid NULL,
                    "ZonePlaceId" uuid NULL,
                    "CountryDisplayName" varchar(200) NULL,
                    "AdministrativeAreaDisplayName" varchar(200) NULL,
                    "LocalityDisplayName" varchar(200) NULL,
                    "DistrictDisplayName" varchar(200) NULL,
                    "ZoneDisplayName" varchar(200) NULL,
                    "Street" varchar(500) NOT NULL,
                    "Street2" varchar(500) NULL,
                    "Building" varchar(120) NULL,
                    "Floor" varchar(64) NULL,
                    "Apartment" varchar(64) NULL,
                    "Landmark" varchar(200) NULL,
                    "PostalCode" varchar(32) NULL,
                    "LegacyAddressText" varchar(1000) NULL,
                    "AdmittedAtUtc" timestamp with time zone NOT NULL,
                    "SnapshotCreatedAtUtc" timestamp with time zone NOT NULL,
                    "SnapshotVersion" integer NOT NULL DEFAULT 1,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NULL,
                    "IsDeleted" boolean NOT NULL DEFAULT false,
                    CONSTRAINT "FK_CustomerAddressVersions_CustomerAddresses_CustomerAddressId" FOREIGN KEY ("CustomerAddressId") REFERENCES "CustomerAddresses" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "CK_CustomerAddressVersions_Snapshot" CHECK ("AddressAdmissionEvidenceId" IS NOT NULL AND "GeographicCatalogReleaseId" IS NOT NULL AND "CountryPlaceId" IS NOT NULL AND "SnapshotVersion" = 1)
                );

                ALTER TABLE "CustomerAddresses"
                    ADD CONSTRAINT "FK_CustomerAddresses_CurrentVersion" FOREIGN KEY ("CurrentVersionId") REFERENCES "CustomerAddressVersions" ("Id") ON DELETE RESTRICT;

                ALTER TABLE "Orders"
                    ADD CONSTRAINT "CK_Orders_ShippingGeoSnapshot" CHECK (
                        "ShippingAddressAdmissionEvidenceId" IS NULL OR
                        ("ShippingGeographicCatalogReleaseId" IS NOT NULL AND "ShippingCountryPlaceId" IS NOT NULL AND "ShippingGeographicSnapshotVersion" = 1 AND "ShippingGeographicSnapshotCreatedAtUtc" IS NOT NULL)
                    ),
                    ADD CONSTRAINT "CK_Orders_BillingGeoSnapshot" CHECK (
                        "BillingAddressAdmissionEvidenceId" IS NULL OR
                        ("BillingGeographicCatalogReleaseId" IS NOT NULL AND "BillingCountryPlaceId" IS NOT NULL AND "BillingGeographicSnapshotVersion" = 1 AND "BillingGeographicSnapshotCreatedAtUtc" IS NOT NULL)
                    );

                ALTER TABLE "Shipments"
                    ADD CONSTRAINT "CK_Shipments_DestinationGeoSnapshot" CHECK (
                        "DestinationAddressAdmissionEvidenceId" IS NULL OR
                        ("DestinationGeographicCatalogReleaseId" IS NOT NULL AND "DestinationCountryPlaceId" IS NOT NULL AND "DestinationGeographicSnapshotVersion" = 1 AND "DestinationGeographicSnapshotCreatedAtUtc" IS NOT NULL)
                    );

                CREATE INDEX IF NOT EXISTS "IX_Orders_ShippingAddressAdmissionEvidenceId" ON "Orders" ("ShippingAddressAdmissionEvidenceId");
                CREATE INDEX IF NOT EXISTS "IX_Orders_BillingAddressAdmissionEvidenceId" ON "Orders" ("BillingAddressAdmissionEvidenceId");
                CREATE INDEX IF NOT EXISTS "IX_Orders_TenantId_ShippingGeoRelease_Country" ON "Orders" ("TenantId", "ShippingGeographicCatalogReleaseId", "ShippingCountryPlaceId");
                CREATE INDEX IF NOT EXISTS "IX_Orders_TenantId_BillingGeoRelease_Country" ON "Orders" ("TenantId", "BillingGeographicCatalogReleaseId", "BillingCountryPlaceId");
                CREATE INDEX IF NOT EXISTS "IX_Shipments_DestinationAddressAdmissionEvidenceId" ON "Shipments" ("DestinationAddressAdmissionEvidenceId");
                CREATE INDEX IF NOT EXISTS "IX_Shipments_TenantId_DestinationGeoRelease_Country" ON "Shipments" ("TenantId", "DestinationGeographicCatalogReleaseId", "DestinationCountryPlaceId");
                CREATE INDEX IF NOT EXISTS "IX_CustomerAddresses_TenantId_CustomerId_IsDefault" ON "CustomerAddresses" ("TenantId", "CustomerId", "IsDefault");
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_CustomerAddressVersions_TenantId_CustomerAddressId_Version" ON "CustomerAddressVersions" ("TenantId", "CustomerAddressId", "Version") WHERE "IsDeleted" = false;
                CREATE INDEX IF NOT EXISTS "IX_CustomerAddressVersions_AddressAdmissionEvidenceId" ON "CustomerAddressVersions" ("AddressAdmissionEvidenceId");
                CREATE INDEX IF NOT EXISTS "IX_CustomerAddressVersions_GeoLineage" ON "CustomerAddressVersions" ("GeographicCatalogReleaseId", "CountryPlaceId", "AdministrativeAreaPlaceId", "LocalityPlaceId", "DistrictPlaceId", "ZonePlaceId");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "CustomerAddresses" DROP CONSTRAINT IF EXISTS "FK_CustomerAddresses_CurrentVersion";
                DROP TABLE IF EXISTS "CustomerAddressVersions";
                DROP TABLE IF EXISTS "CustomerAddresses";

                ALTER TABLE "Shipments" DROP CONSTRAINT IF EXISTS "CK_Shipments_DestinationGeoSnapshot";
                ALTER TABLE "Orders" DROP CONSTRAINT IF EXISTS "CK_Orders_BillingGeoSnapshot";
                ALTER TABLE "Orders" DROP CONSTRAINT IF EXISTS "CK_Orders_ShippingGeoSnapshot";
                """);
        }
    }
}
