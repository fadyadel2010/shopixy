BEGIN;

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory"
(
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

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
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT "PK_PartyLedgerAdjustments" PRIMARY KEY ("Id")
);

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
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT "PK_SupplierPayments" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_SupplierPayments_PurchaseInvoices_PurchaseInvoiceId"
        FOREIGN KEY ("PurchaseInvoiceId") REFERENCES "PurchaseInvoices" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_SupplierPayments_Suppliers_SupplierId"
        FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_PartyLedgerAdjustments_TenantId_IdempotencyKey"
    ON "PartyLedgerAdjustments" ("TenantId", "IdempotencyKey");

CREATE INDEX IF NOT EXISTS "IX_PartyLedgerAdjustments_TenantId_PartyType_PartyId_AdjustmentDate"
    ON "PartyLedgerAdjustments" ("TenantId", "PartyType", "PartyId", "AdjustmentDate");

CREATE INDEX IF NOT EXISTS "IX_SupplierPayments_PurchaseInvoiceId"
    ON "SupplierPayments" ("PurchaseInvoiceId");

CREATE INDEX IF NOT EXISTS "IX_SupplierPayments_SupplierId"
    ON "SupplierPayments" ("SupplierId");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_SupplierPayments_TenantId_IdempotencyKey"
    ON "SupplierPayments" ("TenantId", "IdempotencyKey");

CREATE INDEX IF NOT EXISTS "IX_SupplierPayments_TenantId_SupplierId_PaymentDate"
    ON "SupplierPayments" ("TenantId", "SupplierId", "PaymentDate");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260408193000_AddSupplierPaymentsAndPartyLedgerAdjustments', '9.0.1'
WHERE NOT EXISTS
(
    SELECT 1
    FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260408193000_AddSupplierPaymentsAndPartyLedgerAdjustments'
);

COMMIT;
