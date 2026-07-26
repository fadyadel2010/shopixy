BEGIN;

CREATE TABLE IF NOT EXISTS "CommerceHubFinancialReconciliationReports"
(
    "Id" uuid NOT NULL,
    "TimestampUtc" timestamp with time zone NOT NULL DEFAULT NOW(),
    "Status" character varying(20) NOT NULL,
    "MismatchCount" integer NOT NULL DEFAULT 0,
    "DetailsJson" text NOT NULL DEFAULT '{}',
    CONSTRAINT "PK_CommerceHubFinancialReconciliationReports" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_CommerceHubFinancialReconciliationReports_MismatchCount"
        CHECK ("MismatchCount" >= 0),
    CONSTRAINT "CK_CommerceHubFinancialReconciliationReports_Status"
        CHECK ("Status" IN ('Healthy', 'Warning', 'Critical'))
);

CREATE INDEX IF NOT EXISTS "IX_CommerceHubFinancialReconciliationReports_TimestampUtc"
    ON "CommerceHubFinancialReconciliationReports" ("TimestampUtc" DESC);

CREATE INDEX IF NOT EXISTS "IX_CommerceHubFinancialReconciliationReports_Status_TimestampUtc"
    ON "CommerceHubFinancialReconciliationReports" ("Status", "TimestampUtc" DESC);

CREATE OR REPLACE FUNCTION fn_commercehub_financial_reconciliation_reports_immutable()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'CommerceHubFinancialReconciliationReports is append-only. UPDATE and DELETE are forbidden.';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_commercehub_financial_reconciliation_reports_immutable
    ON "CommerceHubFinancialReconciliationReports";

CREATE TRIGGER trg_commercehub_financial_reconciliation_reports_immutable
    BEFORE UPDATE OR DELETE ON "CommerceHubFinancialReconciliationReports"
    FOR EACH ROW
    EXECUTE FUNCTION fn_commercehub_financial_reconciliation_reports_immutable();

COMMIT;
