BEGIN;

CREATE TABLE IF NOT EXISTS "CommerceHubFinancialGuardLocks"
(
    "Id" uuid NOT NULL,
    "Scope" character varying(100) NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_CommerceHubFinancialGuardLocks" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubFinancialGuardLocks_Scope"
    ON "CommerceHubFinancialGuardLocks" ("Scope");

INSERT INTO "CommerceHubFinancialGuardLocks" ("Id", "Scope", "CreatedAtUtc")
VALUES ('7b5b9720-2e31-4d55-808c-6b0dd4d833a1', 'PlatformCash', NOW())
ON CONFLICT ("Scope") DO NOTHING;

CREATE TABLE IF NOT EXISTS "CommerceHubFinancialOperationAudits"
(
    "Id" uuid NOT NULL,
    "VendorId" uuid NOT NULL,
    "ActorId" uuid NULL,
    "ActorType" character varying(50) NOT NULL,
    "OperationType" character varying(50) NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Currency" character varying(10) NOT NULL DEFAULT 'EGP',
    "BeforePlatformCash" numeric(18,2) NOT NULL,
    "BeforeVendorPayable" numeric(18,2) NOT NULL,
    "BeforeVendorDebt" numeric(18,2) NOT NULL,
    "AfterPlatformCash" numeric(18,2) NOT NULL,
    "AfterVendorPayable" numeric(18,2) NOT NULL,
    "AfterVendorDebt" numeric(18,2) NOT NULL,
    "CorrelationId" character varying(200) NOT NULL,
    "IdempotencyKey" character varying(200) NULL,
    "ReferenceType" integer NOT NULL,
    "ReferenceId" uuid NOT NULL,
    "Outcome" character varying(50) NOT NULL,
    "Notes" text NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_CommerceHubFinancialOperationAudits" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_CommerceHubFinancialOperationAudits_Amount"
        CHECK ("Amount" > 0),
    CONSTRAINT "CK_CommerceHubFinancialOperationAudits_ReferenceType"
        CHECK ("ReferenceType" IN (0, 1, 2, 3, 4))
);

CREATE INDEX IF NOT EXISTS "IX_CommerceHubFinancialOperationAudits_VendorId"
    ON "CommerceHubFinancialOperationAudits" ("VendorId");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubFinancialOperationAudits_CorrelationId"
    ON "CommerceHubFinancialOperationAudits" ("CorrelationId");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubFinancialOperationAudits_OperationType"
    ON "CommerceHubFinancialOperationAudits" ("OperationType");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubFinancialOperationAudits_ReferenceType_ReferenceId"
    ON "CommerceHubFinancialOperationAudits" ("ReferenceType", "ReferenceId");

CREATE OR REPLACE FUNCTION fn_commercehub_financial_operation_audits_immutable()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'CommerceHubFinancialOperationAudits is append-only. UPDATE and DELETE are forbidden.';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_commercehub_financial_operation_audits_immutable
    ON "CommerceHubFinancialOperationAudits";

CREATE TRIGGER trg_commercehub_financial_operation_audits_immutable
    BEFORE UPDATE OR DELETE ON "CommerceHubFinancialOperationAudits"
    FOR EACH ROW
    EXECUTE FUNCTION fn_commercehub_financial_operation_audits_immutable();

SELECT set_config('app.ledger_maintenance_mode', 'true', true);

UPDATE "CommerceHubFinancialTransactions" tx
SET "FinancialBucket" = 0,
    "Description" = CONCAT('Vendor cash collected from customer for COD Order #', sub."OrderNumber")
FROM "CommerceHubSubOrders" sub
WHERE tx."ReferenceType" = 0
  AND tx."ReferenceId" = sub."Id"
  AND tx."EntityType" = 1
  AND tx."TransactionType" = 0
  AND tx."Direction" = 0
  AND sub."PaymentMethod" = 1;

UPDATE "CommerceHubFinancialTransactions" tx
SET "TransactionType" = 3,
    "Direction" = 1,
    "FinancialBucket" = 2,
    "Description" = 'Vendor payable reduced by payout execution',
    "IdempotencyKey" = CONCAT(
        'ledger:',
        REPLACE(COALESCE(tx."EntityId"::text, 'missing'), '-', ''),
        ':3:1:2:',
        REPLACE(tx."ReferenceId"::text, '-', ''))
WHERE tx."ReferenceType" = 2
  AND tx."EntityType" = 1
  AND tx."TransactionType" = 5;

UPDATE "CommerceHubFinancialTransactions" tx
SET "FinancialBucket" = 1,
    "Description" = 'Vendor debt clearance recorded.'
FROM "CommerceHubVendorSettlements" settlement
WHERE settlement."Id" = tx."ReferenceId"
  AND settlement."SettlementType" = 2
  AND tx."ReferenceType" = 4
  AND tx."EntityType" = 1
  AND tx."TransactionType" = 4;

UPDATE "CommerceHubFinancialTransactions" tx
SET "Direction" = 0,
    "FinancialBucket" = 1,
    "Description" = 'Platform receivable reduced by vendor debt clearance.',
    "IdempotencyKey" = CONCAT(
        'ledger:platform:4:0:4:',
        REPLACE(tx."ReferenceId"::text, '-', ''))
FROM "CommerceHubVendorSettlements" settlement
WHERE settlement."Id" = tx."ReferenceId"
  AND settlement."SettlementType" = 2
  AND tx."ReferenceType" = 4
  AND tx."EntityType" = 0
  AND tx."TransactionType" = 4;

UPDATE "CommerceHubFinancialTransactions" tx
SET "Direction" = CASE
        WHEN settlement."Direction" = 1 THEN 1
        ELSE 0
    END,
    "FinancialBucket" = CASE
        WHEN settlement."Direction" = 1 THEN 1
        ELSE 2
    END,
    "Description" = CASE
        WHEN settlement."Direction" = 1 THEN 'Platform receivable increased by vendor balance adjustment.'
        ELSE 'Platform payable increased by vendor balance adjustment.'
    END,
    "IdempotencyKey" = CASE
        WHEN settlement."Direction" = 1 THEN CONCAT(
            'ledger:platform:4:1:4:',
            REPLACE(tx."ReferenceId"::text, '-', ''))
        ELSE CONCAT(
            'ledger:platform:4:0:4:',
            REPLACE(tx."ReferenceId"::text, '-', ''))
    END
FROM "CommerceHubVendorSettlements" settlement
WHERE settlement."Id" = tx."ReferenceId"
  AND settlement."SettlementType" = 1
  AND tx."ReferenceType" = 4
  AND tx."EntityType" = 0
  AND tx."TransactionType" = 4;

CREATE INDEX IF NOT EXISTS "IX_CHFinancialTx_EntityType_EntityId_Bucket_Currency_CreatedAtUtc"
    ON "CommerceHubFinancialTransactions" ("EntityType", "EntityId", "FinancialBucket", "Currency", "CreatedAtUtc" DESC);

COMMIT;
