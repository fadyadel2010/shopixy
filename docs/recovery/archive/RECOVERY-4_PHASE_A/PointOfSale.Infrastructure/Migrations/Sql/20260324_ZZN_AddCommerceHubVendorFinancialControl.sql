BEGIN;

ALTER TABLE "CommerceHubFinancialTransactions"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubFinancialTransactions_ReferenceType";

ALTER TABLE "CommerceHubFinancialTransactions"
    ADD CONSTRAINT "CK_CommerceHubFinancialTransactions_ReferenceType"
        CHECK ("ReferenceType" IN (0, 1, 2, 3, 4));

CREATE TABLE IF NOT EXISTS "CommerceHubVendorSettlements"
(
    "Id" uuid NOT NULL,
    "VendorId" uuid NOT NULL,
    "SettlementType" integer NOT NULL,
    "Direction" integer NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Currency" character varying(10) NOT NULL DEFAULT 'EGP',
    "Notes" text NOT NULL,
    "RecordedByUserId" uuid NULL,
    "RecordedByUserEmail" character varying(320) NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_CommerceHubVendorSettlements" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_CommerceHubVendorSettlements_Amount"
        CHECK ("Amount" > 0),
    CONSTRAINT "CK_CommerceHubVendorSettlements_SettlementType"
        CHECK ("SettlementType" IN (0, 1, 2)),
    CONSTRAINT "CK_CommerceHubVendorSettlements_Direction"
        CHECK ("Direction" IN (0, 1))
);

CREATE INDEX IF NOT EXISTS "IX_CommerceHubVendorSettlements_VendorId_CreatedAtUtc"
    ON "CommerceHubVendorSettlements" ("VendorId", "CreatedAtUtc" DESC);

CREATE INDEX IF NOT EXISTS "IX_CommerceHubVendorSettlements_SettlementType_CreatedAtUtc"
    ON "CommerceHubVendorSettlements" ("SettlementType", "CreatedAtUtc" DESC);

CREATE INDEX IF NOT EXISTS "IX_CommerceHubVendorSettlements_CreatedAtUtc"
    ON "CommerceHubVendorSettlements" ("CreatedAtUtc" DESC);

CREATE OR REPLACE FUNCTION fn_commercehub_vendor_settlements_immutable()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'CommerceHubVendorSettlements is append-only. UPDATE and DELETE are forbidden.';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_commercehub_vendor_settlements_immutable
    ON "CommerceHubVendorSettlements";

CREATE TRIGGER trg_commercehub_vendor_settlements_immutable
    BEFORE UPDATE OR DELETE ON "CommerceHubVendorSettlements"
    FOR EACH ROW
    EXECUTE FUNCTION fn_commercehub_vendor_settlements_immutable();

COMMIT;
