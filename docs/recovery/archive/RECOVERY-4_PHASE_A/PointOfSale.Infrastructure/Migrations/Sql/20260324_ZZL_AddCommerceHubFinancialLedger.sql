BEGIN;

CREATE TABLE IF NOT EXISTS "CommerceHubFinancialTransactions"
(
    "Id" uuid NOT NULL,
    "EntityType" integer NOT NULL,
    "EntityId" uuid NULL,
    "TransactionType" integer NOT NULL,
    "Direction" integer NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Currency" character varying(10) NOT NULL DEFAULT 'EGP',
    "ReferenceType" integer NOT NULL,
    "ReferenceId" uuid NOT NULL,
    "Description" text NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_CommerceHubFinancialTransactions" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_CommerceHubFinancialTransactions_Amount"
        CHECK ("Amount" > 0),
    CONSTRAINT "CK_CommerceHubFinancialTransactions_EntityScope"
        CHECK (("EntityType" = 0 AND "EntityId" IS NULL) OR ("EntityType" = 1 AND "EntityId" IS NOT NULL)),
    CONSTRAINT "CK_CommerceHubFinancialTransactions_TransactionType"
        CHECK ("TransactionType" IN (0, 1, 2, 3, 4)),
    CONSTRAINT "CK_CommerceHubFinancialTransactions_Direction"
        CHECK ("Direction" IN (0, 1)),
    CONSTRAINT "CK_CommerceHubFinancialTransactions_ReferenceType"
        CHECK ("ReferenceType" IN (0, 1, 2))
);

CREATE INDEX IF NOT EXISTS "IX_CommerceHubFinancialTransactions_EntityId_EntityType"
    ON "CommerceHubFinancialTransactions" ("EntityId", "EntityType");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubFinancialTransactions_ReferenceId"
    ON "CommerceHubFinancialTransactions" ("ReferenceId");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubFinancialTransactions_CreatedAtUtc"
    ON "CommerceHubFinancialTransactions" ("CreatedAtUtc" DESC);

CREATE OR REPLACE FUNCTION fn_commercehub_financial_transactions_immutable()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'CommerceHubFinancialTransactions is append-only. UPDATE and DELETE are forbidden.';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_commercehub_financial_transactions_immutable
    ON "CommerceHubFinancialTransactions";

CREATE TRIGGER trg_commercehub_financial_transactions_immutable
    BEFORE UPDATE OR DELETE ON "CommerceHubFinancialTransactions"
    FOR EACH ROW
    EXECUTE FUNCTION fn_commercehub_financial_transactions_immutable();

COMMIT;
