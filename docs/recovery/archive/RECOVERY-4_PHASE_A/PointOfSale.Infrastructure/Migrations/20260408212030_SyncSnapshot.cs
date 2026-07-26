using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
ALTER TABLE "StorefrontFooterSections"
    ADD COLUMN IF NOT EXISTS "TitleAr" character varying(120) NULL;

ALTER TABLE "SalesRequestIdempotency"
    ADD COLUMN IF NOT EXISTS "ClaimTimestampUtc" timestamp with time zone NULL;

ALTER TABLE "SalesRequestIdempotency"
    ADD COLUMN IF NOT EXISTS "ClaimToken" character varying(32) NULL;

ALTER TABLE "Products"
    ADD COLUMN IF NOT EXISTS "AiGenerated" boolean NOT NULL DEFAULT FALSE;

ALTER TABLE "Products"
    ADD COLUMN IF NOT EXISTS "AiMetadataJson" jsonb NULL;

ALTER TABLE "Products"
    ADD COLUMN IF NOT EXISTS "FaqJson" jsonb NULL;

ALTER TABLE "Products"
    ADD COLUMN IF NOT EXISTS "FeaturesJson" jsonb NULL;

ALTER TABLE "Products"
    ADD COLUMN IF NOT EXISTS "TagsJson" jsonb NULL;

ALTER TABLE "MarketplaceFooterSections"
    ADD COLUMN IF NOT EXISTS "TitleJson" jsonb NULL;

ALTER TABLE "MarketplaceFooterLinks"
    ADD COLUMN IF NOT EXISTS "LabelJson" jsonb NULL;

ALTER TABLE "MarketplaceCmsPages"
    ADD COLUMN IF NOT EXISTS "ContentJson" jsonb NULL;

ALTER TABLE "MarketplaceCmsPages"
    ADD COLUMN IF NOT EXISTS "MetaDescriptionJson" jsonb NULL;

ALTER TABLE "MarketplaceCmsPages"
    ADD COLUMN IF NOT EXISTS "MetaTitleJson" jsonb NULL;

ALTER TABLE "MarketplaceCmsPages"
    ADD COLUMN IF NOT EXISTS "TitleJson" jsonb NULL;
""");

            migrationBuilder.Sql("""
CREATE TABLE IF NOT EXISTS "MarketplaceCategoryTranslations"
(
    "Id" uuid NOT NULL,
    "CategoryId" uuid NOT NULL,
    "Language" character varying(5) NOT NULL,
    "Name" character varying(160) NOT NULL,
    "Slug" character varying(180) NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_MarketplaceCategoryTranslations" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MarketplaceCategoryTranslations_Categories_CategoryId"
        FOREIGN KEY ("CategoryId") REFERENCES "Categories" ("Id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_MarketplaceCategoryTranslations_CategoryId_Language"
    ON "MarketplaceCategoryTranslations" ("CategoryId", "Language");

CREATE INDEX IF NOT EXISTS "IX_MarketplaceCategoryTranslations_Language"
    ON "MarketplaceCategoryTranslations" ("Language");
""");

            migrationBuilder.Sql("""
CREATE TABLE IF NOT EXISTS "PurchaseRequestIdempotency"
(
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Key" character varying(200) NOT NULL,
    "Operation" character varying(32) NOT NULL,
    "PurchaseInvoiceId" uuid NULL,
    "PurchaseAttemptId" character varying(200) NULL,
    "RequestFingerprint" character varying(64) NOT NULL,
    "Status" character varying(32) NOT NULL,
    "ResponseStatusCode" integer NULL,
    "ResponseJson" text NULL,
    "CompletedAtUtc" timestamp with time zone NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT "PK_PurchaseRequestIdempotency" PRIMARY KEY ("Id")
);

ALTER TABLE "PurchaseRequestIdempotency"
    ADD COLUMN IF NOT EXISTS "CreatedBy" uuid NULL;

ALTER TABLE "PurchaseRequestIdempotency"
    ADD COLUMN IF NOT EXISTS "UpdatedBy" uuid NULL;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_PurchaseRequestIdempotency_TenantId_Key_Operation"
    ON "PurchaseRequestIdempotency" ("TenantId", "Key", "Operation");

CREATE INDEX IF NOT EXISTS "IX_PurchaseRequestIdempotency_TenantId_PurchaseInvoiceId"
    ON "PurchaseRequestIdempotency" ("TenantId", "PurchaseInvoiceId");
""");

            migrationBuilder.Sql("""
CREATE TABLE IF NOT EXISTS "PurchaseReturnRequestIdempotency"
(
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Key" character varying(200) NOT NULL,
    "PurchaseAttemptId" character varying(200) NULL,
    "RequestFingerprint" character varying(64) NOT NULL,
    "ReturnId" uuid NULL,
    "ResponseStatusCode" integer NULL,
    "ResponseJson" text NULL,
    "CompletedAtUtc" timestamp with time zone NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT "PK_PurchaseReturnRequestIdempotency" PRIMARY KEY ("Id")
);

ALTER TABLE "PurchaseReturnRequestIdempotency"
    ADD COLUMN IF NOT EXISTS "CreatedBy" uuid NULL;

ALTER TABLE "PurchaseReturnRequestIdempotency"
    ADD COLUMN IF NOT EXISTS "UpdatedBy" uuid NULL;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_PurchaseReturnRequestIdempotency_TenantId_Key"
    ON "PurchaseReturnRequestIdempotency" ("TenantId", "Key");

CREATE INDEX IF NOT EXISTS "IX_PurchaseReturnRequestIdempotency_TenantId_ReturnId"
    ON "PurchaseReturnRequestIdempotency" ("TenantId", "ReturnId");
""");

            migrationBuilder.Sql("""
CREATE TABLE IF NOT EXISTS "PartyLedgerAdjustments"
(
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "PartyType" integer NOT NULL,
    "PartyId" uuid NOT NULL,
    "TransactionType" integer NOT NULL,
    "AdjustmentDate" timestamp with time zone NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "IdempotencyKey" character varying(100) NOT NULL,
    "ReferenceNumber" character varying(100) NOT NULL,
    "Notes" character varying(1000) NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT "PK_PartyLedgerAdjustments" PRIMARY KEY ("Id")
);

ALTER TABLE "PartyLedgerAdjustments"
    ADD COLUMN IF NOT EXISTS "CreatedBy" uuid NULL;

ALTER TABLE "PartyLedgerAdjustments"
    ADD COLUMN IF NOT EXISTS "UpdatedBy" uuid NULL;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_PartyLedgerAdjustments_TenantId_IdempotencyKey"
    ON "PartyLedgerAdjustments" ("TenantId", "IdempotencyKey");

CREATE INDEX IF NOT EXISTS "IX_PartyLedgerAdjustments_TenantId_PartyType_PartyId_AdjustmentDate"
    ON "PartyLedgerAdjustments" ("TenantId", "PartyType", "PartyId", "AdjustmentDate");
""");

            migrationBuilder.Sql("""
CREATE TABLE IF NOT EXISTS "SupplierPayments"
(
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "SupplierId" uuid NOT NULL,
    "PurchaseInvoiceId" uuid NULL,
    "PaymentDate" timestamp with time zone NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "PaymentMethod" integer NOT NULL,
    "IdempotencyKey" character varying(100) NOT NULL,
    "ReferenceNumber" character varying(100) NULL,
    "Notes" character varying(1000) NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT "PK_SupplierPayments" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_SupplierPayments_PurchaseInvoices_PurchaseInvoiceId"
        FOREIGN KEY ("PurchaseInvoiceId") REFERENCES "PurchaseInvoices" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_SupplierPayments_Suppliers_SupplierId"
        FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("Id") ON DELETE RESTRICT
);

ALTER TABLE "SupplierPayments"
    ADD COLUMN IF NOT EXISTS "CreatedBy" uuid NULL;

ALTER TABLE "SupplierPayments"
    ADD COLUMN IF NOT EXISTS "UpdatedBy" uuid NULL;

CREATE INDEX IF NOT EXISTS "IX_SupplierPayments_PurchaseInvoiceId"
    ON "SupplierPayments" ("PurchaseInvoiceId");

CREATE INDEX IF NOT EXISTS "IX_SupplierPayments_SupplierId"
    ON "SupplierPayments" ("SupplierId");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_SupplierPayments_TenantId_IdempotencyKey"
    ON "SupplierPayments" ("TenantId", "IdempotencyKey");

CREATE INDEX IF NOT EXISTS "IX_SupplierPayments_TenantId_SupplierId_PaymentDate"
    ON "SupplierPayments" ("TenantId", "SupplierId", "PaymentDate");
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Snapshot reconciliation migration.
            // Existing environments may already contain this schema from prior SQL-first deployments,
            // so rollback is intentionally a no-op to avoid destructive drift.
        }
    }
}
