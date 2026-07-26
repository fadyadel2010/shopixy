using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

public partial class EvidenceDatabaseHardening : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE SCHEMA IF NOT EXISTS forensic;

            CREATE OR REPLACE FUNCTION forensic.reject_append_only_evidence_mutation()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $$
            BEGIN
                RAISE EXCEPTION USING
                    ERRCODE = '55000',
                    MESSAGE = format(
                        'FORENSIC_APPEND_ONLY_VIOLATION: %s is append-only; %s is prohibited',
                        TG_TABLE_NAME,
                        TG_OP);
            END;
            $$;

            REVOKE ALL ON SCHEMA forensic FROM PUBLIC;
            REVOKE ALL ON FUNCTION forensic.reject_append_only_evidence_mutation() FROM PUBLIC;

            DO $$
            DECLARE
                evidence_table text;
                trigger_name text;
                evidence_tables constant text[] := ARRAY[
                    'AuditLogs',
                    'UserGovernanceAuditLogs',
                    'CommerceHubFinancialTransactions',
                    'CommerceHubFinancialAuditLogs',
                    'CommerceHubFinancialOperationAudits',
                    'CommerceHubFinancialReconciliationReports',
                    'CommerceHubVendorSettlements',
                    'CommerceHubVendorPaymentAuditLogs',
                    'LedgerEntries',
                    'CommerceAccountingAudits',
                    'StoreConfigAuditEntries',
                    'MarketplaceEventOutboxDeadLetterArchives',
                    'CommerceHubMerchantLifecycleLedgerEntries',
                    'CommerceHubRecoveryOperationLedgerEntries'
                ];
            BEGIN
                FOREACH evidence_table IN ARRAY evidence_tables
                LOOP
                    IF to_regclass(format('public.%I', evidence_table)) IS NULL THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '55000',
                            MESSAGE = format(
                                'FORENSIC_APPEND_ONLY_INVENTORY_DRIFT: required evidence table %s is missing',
                                evidence_table);
                    END IF;

                    trigger_name := 'TRG_ForensicAppendOnly_' || evidence_table;

                    EXECUTE format(
                        'DROP TRIGGER IF EXISTS %I ON public.%I',
                        trigger_name,
                        evidence_table);

                    EXECUTE format(
                        'CREATE TRIGGER %I BEFORE UPDATE OR DELETE OR TRUNCATE ON public.%I FOR EACH STATEMENT EXECUTE FUNCTION forensic.reject_append_only_evidence_mutation()',
                        trigger_name,
                        evidence_table);

                    EXECUTE format(
                        'COMMENT ON TABLE public.%I IS %L',
                        evidence_table,
                        'FORENSIC_APPEND_ONLY: INSERT and SELECT are permitted for runtime evidence roles. UPDATE, DELETE, and TRUNCATE are rejected by database trigger.');
                END LOOP;
            END;
            $$;

            REVOKE UPDATE, DELETE, TRUNCATE ON TABLE
                public."AuditLogs",
                public."UserGovernanceAuditLogs",
                public."CommerceHubFinancialTransactions",
                public."CommerceHubFinancialAuditLogs",
                public."CommerceHubFinancialOperationAudits",
                public."CommerceHubFinancialReconciliationReports",
                public."CommerceHubVendorSettlements",
                public."CommerceHubVendorPaymentAuditLogs",
                public."LedgerEntries",
                public."CommerceAccountingAudits",
                public."StoreConfigAuditEntries",
                public."MarketplaceEventOutboxDeadLetterArchives",
                public."CommerceHubMerchantLifecycleLedgerEntries",
                public."CommerceHubRecoveryOperationLedgerEntries"
            FROM PUBLIC;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                RAISE EXCEPTION USING
                    ERRCODE = '55000',
                    MESSAGE = 'FORENSIC_APPEND_ONLY_ROLLBACK_PROHIBITED: EvidenceDatabaseHardening is irreversible. Apply a reviewed forward migration for any schema evolution.';
            END;
            $$;
            """);
    }
}
