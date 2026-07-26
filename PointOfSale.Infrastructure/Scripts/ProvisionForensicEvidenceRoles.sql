\set ON_ERROR_STOP on

-- Run with a PostgreSQL administration identity after EF migrations.
-- Required psql variables:
--   application_login, background_worker_login, migration_login,
--   maintenance_login, reporting_login
--
-- Example:
-- psql "$ADMIN_CONNECTION" \
--   --set=application_login=pointofsale_app \
--   --set=background_worker_login=pointofsale_worker \
--   --set=migration_login=pointofsale_migration \
--   --set=maintenance_login=pointofsale_maintenance \
--   --set=reporting_login=pointofsale_reporting \
--   --file PointOfSale.Infrastructure/Scripts/ProvisionForensicEvidenceRoles.sql

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'pointofsale_evidence_owner') THEN
        CREATE ROLE pointofsale_evidence_owner NOLOGIN NOINHERIT;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'pointofsale_evidence_writer') THEN
        CREATE ROLE pointofsale_evidence_writer NOLOGIN INHERIT;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'pointofsale_evidence_reader') THEN
        CREATE ROLE pointofsale_evidence_reader NOLOGIN INHERIT;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'pointofsale_evidence_migration') THEN
        CREATE ROLE pointofsale_evidence_migration NOLOGIN NOINHERIT;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'pointofsale_evidence_maintenance') THEN
        CREATE ROLE pointofsale_evidence_maintenance NOLOGIN INHERIT;
    END IF;
END
$$;

GRANT USAGE ON SCHEMA public TO pointofsale_evidence_writer;
GRANT USAGE ON SCHEMA public TO pointofsale_evidence_reader;
GRANT USAGE ON SCHEMA public TO pointofsale_evidence_maintenance;
GRANT USAGE ON SCHEMA forensic TO pointofsale_evidence_owner;

ALTER SCHEMA forensic OWNER TO pointofsale_evidence_owner;
ALTER FUNCTION forensic.reject_append_only_evidence_mutation() OWNER TO pointofsale_evidence_owner;

SELECT set_config('pointofsale.application_login', :'application_login', false);
SELECT set_config('pointofsale.background_worker_login', :'background_worker_login', false);
SELECT set_config('pointofsale.reporting_login', :'reporting_login', false);
SELECT set_config('pointofsale.maintenance_login', :'maintenance_login', false);
SELECT set_config('pointofsale.migration_login', :'migration_login', false);

DO $$
DECLARE
    evidence_table text;
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

        EXECUTE format('ALTER TABLE public.%I OWNER TO pointofsale_evidence_owner', evidence_table);
        EXECUTE format('REVOKE ALL PRIVILEGES ON TABLE public.%I FROM PUBLIC', evidence_table);
        EXECUTE format('REVOKE ALL PRIVILEGES ON TABLE public.%I FROM pointofsale_evidence_writer', evidence_table);
        EXECUTE format('REVOKE ALL PRIVILEGES ON TABLE public.%I FROM pointofsale_evidence_reader', evidence_table);
        EXECUTE format('REVOKE ALL PRIVILEGES ON TABLE public.%I FROM pointofsale_evidence_maintenance', evidence_table);
        EXECUTE format(
            'REVOKE ALL PRIVILEGES ON TABLE public.%I FROM %I',
            evidence_table,
            current_setting('pointofsale.application_login'));
        EXECUTE format(
            'REVOKE ALL PRIVILEGES ON TABLE public.%I FROM %I',
            evidence_table,
            current_setting('pointofsale.background_worker_login'));
        EXECUTE format(
            'REVOKE ALL PRIVILEGES ON TABLE public.%I FROM %I',
            evidence_table,
            current_setting('pointofsale.reporting_login'));
        EXECUTE format(
            'REVOKE ALL PRIVILEGES ON TABLE public.%I FROM %I',
            evidence_table,
            current_setting('pointofsale.maintenance_login'));
        EXECUTE format('GRANT SELECT, INSERT ON TABLE public.%I TO pointofsale_evidence_writer', evidence_table);
        EXECUTE format('GRANT SELECT ON TABLE public.%I TO pointofsale_evidence_reader', evidence_table);
        EXECUTE format('GRANT SELECT ON TABLE public.%I TO pointofsale_evidence_maintenance', evidence_table);
    END LOOP;
END
$$;

GRANT pointofsale_evidence_writer TO :"application_login";
GRANT pointofsale_evidence_writer TO :"background_worker_login";
GRANT pointofsale_evidence_reader TO :"reporting_login";
GRANT pointofsale_evidence_maintenance TO :"maintenance_login";
GRANT pointofsale_evidence_owner TO pointofsale_evidence_migration;
GRANT pointofsale_evidence_migration TO :"migration_login";

-- Runtime identities must never own evidence objects or inherit migration authority.
DO $$
DECLARE
    unprivileged_role text;
BEGIN
    FOREACH unprivileged_role IN ARRAY ARRAY[
        current_setting('pointofsale.application_login'),
        current_setting('pointofsale.background_worker_login'),
        current_setting('pointofsale.reporting_login'),
        current_setting('pointofsale.maintenance_login')]
    LOOP
        IF pg_has_role(unprivileged_role, 'pointofsale_evidence_owner', 'MEMBER')
            OR pg_has_role(unprivileged_role, 'pointofsale_evidence_migration', 'MEMBER') THEN
            RAISE EXCEPTION USING
                ERRCODE = '55000',
                MESSAGE = format(
                    'FORENSIC_ROLE_SEPARATION_VIOLATION: unprivileged role %s inherits evidence owner or migration authority',
                    unprivileged_role);
        END IF;
    END LOOP;

    IF current_setting('pointofsale.migration_login') = ANY (ARRAY[
        current_setting('pointofsale.application_login'),
        current_setting('pointofsale.background_worker_login'),
        current_setting('pointofsale.reporting_login'),
        current_setting('pointofsale.maintenance_login')]) THEN
        RAISE EXCEPTION USING
            ERRCODE = '55000',
            MESSAGE = 'FORENSIC_ROLE_SEPARATION_VIOLATION: migration login must not be reused as an application, worker, reporting, or maintenance login';
    END IF;
END
$$;
