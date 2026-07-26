using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260706210000_Customs4AdministrationRuntimeWorkflow")]
    public partial class Customs4AdministrationRuntimeWorkflow : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "CustomsEvidence"
                    ADD COLUMN IF NOT EXISTS "CreatedByActor" varchar(128) NOT NULL DEFAULT 'system',
                    ADD COLUMN IF NOT EXISTS "LastEditedByActor" varchar(128) NOT NULL DEFAULT 'system',
                    ADD COLUMN IF NOT EXISTS "SubmittedByActor" varchar(128) NULL;

                ALTER TABLE "CustomsEvidence" DROP CONSTRAINT IF EXISTS "CK_CustomsEvidence_Status";
                ALTER TABLE "CustomsEvidence"
                    ADD CONSTRAINT "CK_CustomsEvidence_Status"
                    CHECK ("Status" IN ('Candidate','Admitted','Draft','UnderReview','Approved','Published','Rejected','Reopened','Retired','Superseded'));

                CREATE TABLE IF NOT EXISTS "CustomsEvidenceApprovals" (
                    "Id" uuid NOT NULL PRIMARY KEY,
                    "TenantId" uuid NOT NULL,
                    "CustomsEvidenceId" uuid NOT NULL,
                    "CustomsEvidenceVersionId" uuid NOT NULL,
                    "VersionNumber" integer NOT NULL,
                    "ValidationChecksumSha256" varchar(64) NOT NULL,
                    "ApprovedByActor" varchar(128) NOT NULL,
                    "Reason" varchar(512) NOT NULL,
                    "CorrelationId" varchar(128) NOT NULL,
                    "ApprovedAtUtc" timestamp with time zone NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NULL,
                    "CreatedBy" uuid NULL,
                    "UpdatedBy" uuid NULL,
                    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
                    CONSTRAINT "CK_CustomsEvidenceApproval_Checksum" CHECK (char_length("ValidationChecksumSha256") = 64)
                );

                CREATE TABLE IF NOT EXISTS "CustomsEvidencePublications" (
                    "Id" uuid NOT NULL PRIMARY KEY,
                    "TenantId" uuid NOT NULL,
                    "CustomsEvidenceId" uuid NOT NULL,
                    "CustomsEvidenceVersionId" uuid NOT NULL,
                    "SupersededCustomsEvidenceId" uuid NULL,
                    "SupersededCustomsEvidenceVersionId" uuid NULL,
                    "ProductId" uuid NULL,
                    "ProductVariantId" uuid NULL,
                    "VersionNumber" integer NOT NULL,
                    "Action" varchar(32) NOT NULL,
                    "ValidationChecksumSha256" varchar(64) NOT NULL,
                    "PublishedByActor" varchar(128) NOT NULL,
                    "Reason" varchar(512) NOT NULL,
                    "CorrelationId" varchar(128) NOT NULL,
                    "OccurredAtUtc" timestamp with time zone NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NULL,
                    "CreatedBy" uuid NULL,
                    "UpdatedBy" uuid NULL,
                    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
                    CONSTRAINT "CK_CustomsEvidencePublication_Action" CHECK ("Action" IN ('Published','Retired','Superseded')),
                    CONSTRAINT "CK_CustomsEvidencePublication_Checksum" CHECK (char_length("ValidationChecksumSha256") = 64)
                );

                CREATE TABLE IF NOT EXISTS "CustomsPublicationLocks" (
                    "Id" uuid NOT NULL PRIMARY KEY,
                    "TenantId" uuid NOT NULL,
                    "LockKey" varchar(256) NOT NULL,
                    "LockedAtUtc" timestamp with time zone NOT NULL,
                    "LockedUntilUtc" timestamp with time zone NOT NULL,
                    "LockedByActor" varchar(128) NOT NULL,
                    "CorrelationId" varchar(128) NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NULL,
                    "CreatedBy" uuid NULL,
                    "UpdatedBy" uuid NULL,
                    "IsDeleted" boolean NOT NULL DEFAULT FALSE
                );

                CREATE TABLE IF NOT EXISTS "CustomsAdministrationAudit" (
                    "Id" uuid NOT NULL PRIMARY KEY,
                    "TenantId" uuid NOT NULL,
                    "Action" varchar(64) NOT NULL,
                    "CustomsEvidenceId" uuid NULL,
                    "CustomsEvidenceVersionId" uuid NULL,
                    "ProductId" uuid NULL,
                    "ProductVariantId" uuid NULL,
                    "Actor" varchar(128) NOT NULL,
                    "Role" varchar(64) NOT NULL,
                    "Reason" varchar(512) NOT NULL,
                    "CorrelationId" varchar(128) NOT NULL,
                    "ValidationChecksumSha256" varchar(64) NULL,
                    "FailureCode" varchar(128) NULL,
                    "FromStatus" varchar(32) NULL,
                    "ToStatus" varchar(32) NULL,
                    "OccurredAtUtc" timestamp with time zone NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NULL,
                    "CreatedBy" uuid NULL,
                    "UpdatedBy" uuid NULL,
                    "IsDeleted" boolean NOT NULL DEFAULT FALSE
                );

                CREATE INDEX IF NOT EXISTS "IX_CustomsEvidenceApprovals_Evidence_Version"
                    ON "CustomsEvidenceApprovals" ("TenantId", "CustomsEvidenceId", "CustomsEvidenceVersionId");
                CREATE INDEX IF NOT EXISTS "IX_CustomsEvidencePublications_Product_Timeline"
                    ON "CustomsEvidencePublications" ("TenantId", "ProductId", "ProductVariantId", "Action", "OccurredAtUtc");
                CREATE INDEX IF NOT EXISTS "IX_CustomsEvidencePublications_Evidence_Version"
                    ON "CustomsEvidencePublications" ("TenantId", "CustomsEvidenceId", "CustomsEvidenceVersionId");
                CREATE UNIQUE INDEX IF NOT EXISTS "UQ_CustomsPublicationLock_Key"
                    ON "CustomsPublicationLocks" ("TenantId", "LockKey");
                CREATE INDEX IF NOT EXISTS "IX_CustomsAdministrationAudit_Tenant_Timeline"
                    ON "CustomsAdministrationAudit" ("TenantId", "OccurredAtUtc");
                CREATE INDEX IF NOT EXISTS "IX_CustomsAdministrationAudit_Evidence_Action"
                    ON "CustomsAdministrationAudit" ("CustomsEvidenceId", "Action", "OccurredAtUtc");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS "CustomsAdministrationAudit";
                DROP TABLE IF EXISTS "CustomsPublicationLocks";
                DROP TABLE IF EXISTS "CustomsEvidencePublications";
                DROP TABLE IF EXISTS "CustomsEvidenceApprovals";

                ALTER TABLE "CustomsEvidence" DROP CONSTRAINT IF EXISTS "CK_CustomsEvidence_Status";
                ALTER TABLE "CustomsEvidence"
                    ADD CONSTRAINT "CK_CustomsEvidence_Status"
                    CHECK ("Status" IN ('Candidate','Admitted','Superseded','Retired','Rejected'));
                ALTER TABLE "CustomsEvidence" DROP COLUMN IF EXISTS "SubmittedByActor";
                ALTER TABLE "CustomsEvidence" DROP COLUMN IF EXISTS "LastEditedByActor";
                ALTER TABLE "CustomsEvidence" DROP COLUMN IF EXISTS "CreatedByActor";
                """);
        }
    }
}
