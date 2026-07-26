using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260706143000_Customs2EvidenceRuntimeFoundation")]
    public partial class Customs2EvidenceRuntimeFoundation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "CustomsEvidence" (
                    "Id" uuid NOT NULL PRIMARY KEY,
                    "TenantId" uuid NOT NULL,
                    "SourceKind" varchar(32) NOT NULL,
                    "SourceId" uuid NOT NULL,
                    "ProductId" uuid NULL,
                    "ProductVariantId" uuid NULL,
                    "Status" varchar(32) NOT NULL,
                    "CurrentVersionId" uuid NOT NULL,
                    "CurrentVersionNumber" integer NOT NULL,
                    "AdmittedAtUtc" timestamp with time zone NOT NULL,
                    "AdmittedBy" varchar(128) NOT NULL,
                    "CorrelationId" varchar(128) NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NULL,
                    "CreatedBy" uuid NULL,
                    "UpdatedBy" uuid NULL,
                    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
                    CONSTRAINT "CK_CustomsEvidence_Status" CHECK ("Status" IN ('Candidate','Admitted','Superseded','Retired','Rejected')),
                    CONSTRAINT "CK_CustomsEvidence_SourceKind" CHECK ("SourceKind" IN ('Product','ProductVariant','OrderLine','System')),
                    CONSTRAINT "CK_CustomsEvidence_CurrentVersion" CHECK ("CurrentVersionNumber" > 0)
                );

                CREATE TABLE IF NOT EXISTS "CustomsEvidenceVersions" (
                    "Id" uuid NOT NULL PRIMARY KEY,
                    "TenantId" uuid NOT NULL,
                    "CustomsEvidenceId" uuid NOT NULL REFERENCES "CustomsEvidence"("Id") ON DELETE RESTRICT,
                    "VersionNumber" integer NOT NULL,
                    "HsCode" varchar(32) NOT NULL,
                    "CommodityDescription" varchar(512) NOT NULL,
                    "CountryOfOriginIso2" varchar(2) NOT NULL,
                    "CountryOfManufactureIso2" varchar(2) NULL,
                    "DeclaredValue" numeric(18,4) NOT NULL,
                    "Currency" varchar(3) NOT NULL,
                    "NetWeight" numeric(18,6) NOT NULL,
                    "WeightUnit" varchar(16) NOT NULL,
                    "QuantityUnit" varchar(32) NOT NULL,
                    "DangerousGoods" boolean NULL,
                    "ExportClassification" varchar(128) NULL,
                    "ValueSource" varchar(128) NOT NULL,
                    "SourceEvidenceJson" jsonb NOT NULL,
                    "EvidenceFingerprintSha256" varchar(64) NOT NULL,
                    "AdmittedAtUtc" timestamp with time zone NOT NULL,
                    "AdmittedBy" varchar(128) NOT NULL,
                    "CorrelationId" varchar(128) NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NULL,
                    "CreatedBy" uuid NULL,
                    "UpdatedBy" uuid NULL,
                    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
                    CONSTRAINT "CK_CustomsEvidenceVersion_Version" CHECK ("VersionNumber" > 0),
                    CONSTRAINT "CK_CustomsEvidenceVersion_HsCode" CHECK (char_length(btrim("HsCode")) > 0),
                    CONSTRAINT "CK_CustomsEvidenceVersion_Commodity" CHECK (char_length(btrim("CommodityDescription")) > 0),
                    CONSTRAINT "CK_CustomsEvidenceVersion_Country" CHECK (char_length("CountryOfOriginIso2") = 2),
                    CONSTRAINT "CK_CustomsEvidenceVersion_Value" CHECK ("DeclaredValue" > 0),
                    CONSTRAINT "CK_CustomsEvidenceVersion_NetWeight" CHECK ("NetWeight" > 0),
                    CONSTRAINT "CK_CustomsEvidenceVersion_Fingerprint" CHECK (char_length("EvidenceFingerprintSha256") = 64)
                );

                CREATE TABLE IF NOT EXISTS "CustomsEvidenceHistory" (
                    "Id" uuid NOT NULL PRIMARY KEY,
                    "TenantId" uuid NOT NULL,
                    "CustomsEvidenceId" uuid NOT NULL,
                    "CustomsEvidenceVersionId" uuid NOT NULL,
                    "VersionNumber" integer NOT NULL,
                    "Action" varchar(128) NOT NULL,
                    "Reason" varchar(512) NOT NULL,
                    "Actor" varchar(128) NOT NULL,
                    "CorrelationId" varchar(128) NOT NULL,
                    "OccurredAtUtc" timestamp with time zone NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NULL,
                    "CreatedBy" uuid NULL,
                    "UpdatedBy" uuid NULL,
                    "IsDeleted" boolean NOT NULL DEFAULT FALSE
                );

                CREATE TABLE IF NOT EXISTS "CustomsEvidenceFingerprints" (
                    "Id" uuid NOT NULL PRIMARY KEY,
                    "TenantId" uuid NOT NULL,
                    "CustomsEvidenceId" uuid NOT NULL,
                    "CustomsEvidenceVersionId" uuid NOT NULL,
                    "FingerprintSha256" varchar(64) NOT NULL,
                    "CreatedAtUtc" timestamp with time zone NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NULL,
                    "CreatedBy" uuid NULL,
                    "UpdatedBy" uuid NULL,
                    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
                    CONSTRAINT "CK_CustomsEvidenceFingerprint_Length" CHECK (char_length("FingerprintSha256") = 64)
                );

                CREATE TABLE IF NOT EXISTS "ShipmentCustomsSnapshots" (
                    "Id" uuid NOT NULL PRIMARY KEY,
                    "TenantId" uuid NOT NULL,
                    "ShipmentId" uuid NOT NULL,
                    "OrderId" uuid NOT NULL,
                    "SnapshotVersion" integer NOT NULL,
                    "CarrierNeutralDtoVersion" integer NOT NULL,
                    "DestinationCountryIso2" varchar(2) NOT NULL,
                    "OriginCountryIso2" varchar(2) NOT NULL,
                    "ShipmentGrossWeight" numeric(18,6) NOT NULL,
                    "ShipmentGrossWeightUnit" varchar(16) NOT NULL,
                    "SnapshotFingerprintSha256" varchar(64) NOT NULL,
                    "SnapshotJson" jsonb NOT NULL,
                    "IsRegistrationFrozen" boolean NOT NULL,
                    "RegistrationBeganAtUtc" timestamp with time zone NOT NULL,
                    "CorrelationId" varchar(128) NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NULL,
                    "CreatedBy" uuid NULL,
                    "UpdatedBy" uuid NULL,
                    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
                    CONSTRAINT "CK_ShipmentCustomsSnapshot_Version" CHECK ("SnapshotVersion" = 1 AND "CarrierNeutralDtoVersion" = 1),
                    CONSTRAINT "CK_ShipmentCustomsSnapshot_Countries" CHECK (char_length("DestinationCountryIso2") = 2 AND char_length("OriginCountryIso2") = 2),
                    CONSTRAINT "CK_ShipmentCustomsSnapshot_GrossWeight" CHECK ("ShipmentGrossWeight" > 0),
                    CONSTRAINT "CK_ShipmentCustomsSnapshot_Fingerprint" CHECK (char_length("SnapshotFingerprintSha256") = 64),
                    CONSTRAINT "CK_ShipmentCustomsSnapshot_Frozen" CHECK ("IsRegistrationFrozen" = TRUE)
                );

                CREATE TABLE IF NOT EXISTS "ShipmentCustomsSnapshotLines" (
                    "Id" uuid NOT NULL PRIMARY KEY,
                    "TenantId" uuid NOT NULL,
                    "ShipmentCustomsSnapshotId" uuid NOT NULL REFERENCES "ShipmentCustomsSnapshots"("Id") ON DELETE RESTRICT,
                    "ShipmentId" uuid NOT NULL,
                    "OrderId" uuid NOT NULL,
                    "OrderItemId" uuid NOT NULL,
                    "ProductId" uuid NOT NULL,
                    "ProductVariantId" uuid NULL,
                    "CustomsEvidenceId" uuid NOT NULL,
                    "CustomsEvidenceVersionId" uuid NOT NULL,
                    "EvidenceVersionNumber" integer NOT NULL,
                    "HsCode" varchar(32) NOT NULL,
                    "CommodityDescription" varchar(512) NOT NULL,
                    "CountryOfOriginIso2" varchar(2) NOT NULL,
                    "CountryOfManufactureIso2" varchar(2) NULL,
                    "DeclaredValue" numeric(18,4) NOT NULL,
                    "Currency" varchar(3) NOT NULL,
                    "Quantity" numeric(18,6) NOT NULL,
                    "QuantityUnit" varchar(32) NOT NULL,
                    "NetWeight" numeric(18,6) NOT NULL,
                    "NetWeightUnit" varchar(16) NOT NULL,
                    "DangerousGoods" boolean NULL,
                    "ExportClassification" varchar(128) NULL,
                    "SourceEvidenceFingerprintSha256" varchar(64) NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NULL,
                    "CreatedBy" uuid NULL,
                    "UpdatedBy" uuid NULL,
                    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
                    CONSTRAINT "CK_ShipmentCustomsSnapshotLine_Value" CHECK ("DeclaredValue" > 0),
                    CONSTRAINT "CK_ShipmentCustomsSnapshotLine_Quantity" CHECK ("Quantity" > 0),
                    CONSTRAINT "CK_ShipmentCustomsSnapshotLine_NetWeight" CHECK ("NetWeight" > 0),
                    CONSTRAINT "CK_ShipmentCustomsSnapshotLine_Country" CHECK (char_length("CountryOfOriginIso2") = 2)
                );

                CREATE TABLE IF NOT EXISTS "CustomsAdmissionAudit" (
                    "Id" uuid NOT NULL PRIMARY KEY,
                    "TenantId" uuid NOT NULL,
                    "Action" varchar(64) NOT NULL,
                    "CustomsEvidenceId" uuid NULL,
                    "CustomsEvidenceVersionId" uuid NULL,
                    "ShipmentCustomsSnapshotId" uuid NULL,
                    "ShipmentId" uuid NULL,
                    "OrderId" uuid NULL,
                    "ProductId" uuid NULL,
                    "ProductVariantId" uuid NULL,
                    "Actor" varchar(128) NOT NULL,
                    "Reason" varchar(512) NOT NULL,
                    "CorrelationId" varchar(128) NOT NULL,
                    "SourceEvidenceFingerprintSha256" varchar(64) NULL,
                    "FailureCode" varchar(128) NULL,
                    "OccurredAtUtc" timestamp with time zone NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NULL,
                    "CreatedBy" uuid NULL,
                    "UpdatedBy" uuid NULL,
                    "IsDeleted" boolean NOT NULL DEFAULT FALSE
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "UQ_CustomsEvidenceVersion_Number" ON "CustomsEvidenceVersions" ("CustomsEvidenceId", "VersionNumber");
                CREATE INDEX IF NOT EXISTS "IX_CustomsEvidence_Source_Status" ON "CustomsEvidence" ("TenantId", "SourceKind", "SourceId", "Status");
                CREATE INDEX IF NOT EXISTS "IX_CustomsEvidence_Product_Status" ON "CustomsEvidence" ("TenantId", "ProductId", "ProductVariantId", "Status");
                CREATE INDEX IF NOT EXISTS "IX_CustomsEvidenceVersion_Fingerprint" ON "CustomsEvidenceVersions" ("TenantId", "EvidenceFingerprintSha256");
                CREATE INDEX IF NOT EXISTS "IX_CustomsEvidenceHistory_Evidence_Version" ON "CustomsEvidenceHistory" ("CustomsEvidenceId", "VersionNumber");
                CREATE INDEX IF NOT EXISTS "IX_CustomsEvidenceFingerprints_Fingerprint" ON "CustomsEvidenceFingerprints" ("TenantId", "FingerprintSha256");
                CREATE UNIQUE INDEX IF NOT EXISTS "UQ_CustomsEvidenceFingerprints_Version" ON "CustomsEvidenceFingerprints" ("CustomsEvidenceVersionId");
                CREATE UNIQUE INDEX IF NOT EXISTS "UQ_ShipmentCustomsSnapshot_Shipment" ON "ShipmentCustomsSnapshots" ("TenantId", "ShipmentId");
                CREATE INDEX IF NOT EXISTS "IX_ShipmentCustomsSnapshot_Order" ON "ShipmentCustomsSnapshots" ("TenantId", "OrderId");
                CREATE UNIQUE INDEX IF NOT EXISTS "UQ_ShipmentCustomsSnapshotLine_OrderItem" ON "ShipmentCustomsSnapshotLines" ("ShipmentCustomsSnapshotId", "OrderItemId");
                CREATE INDEX IF NOT EXISTS "IX_CustomsAdmissionAudit_Tenant_Timeline" ON "CustomsAdmissionAudit" ("TenantId", "OccurredAtUtc");
                CREATE INDEX IF NOT EXISTS "IX_CustomsAdmissionAudit_Shipment_Action" ON "CustomsAdmissionAudit" ("ShipmentId", "Action", "OccurredAtUtc");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS "CustomsAdmissionAudit";
                DROP TABLE IF EXISTS "ShipmentCustomsSnapshotLines";
                DROP TABLE IF EXISTS "ShipmentCustomsSnapshots";
                DROP TABLE IF EXISTS "CustomsEvidenceFingerprints";
                DROP TABLE IF EXISTS "CustomsEvidenceHistory";
                DROP TABLE IF EXISTS "CustomsEvidenceVersions";
                DROP TABLE IF EXISTS "CustomsEvidence";
                """);
        }
    }
}
