\set ON_ERROR_STOP on

WITH evidence_tables(table_name) AS (
    VALUES
        ('AuditLogs'),
        ('UserGovernanceAuditLogs'),
        ('CommerceHubFinancialTransactions'),
        ('CommerceHubFinancialAuditLogs'),
        ('CommerceHubFinancialOperationAudits'),
        ('CommerceHubFinancialReconciliationReports'),
        ('CommerceHubVendorSettlements'),
        ('CommerceHubVendorPaymentAuditLogs'),
        ('LedgerEntries'),
        ('CommerceAccountingAudits'),
        ('StoreConfigAuditEntries'),
        ('MarketplaceEventOutboxDeadLetterArchives'),
        ('CommerceHubMerchantLifecycleLedgerEntries'),
        ('CommerceHubRecoveryOperationLedgerEntries')
)
SELECT
    e.table_name,
    c.relowner::regrole AS owner_role,
    has_table_privilege('pointofsale_evidence_writer', c.oid, 'INSERT') AS writer_can_insert,
    has_table_privilege('pointofsale_evidence_writer', c.oid, 'UPDATE') AS writer_can_update,
    has_table_privilege('pointofsale_evidence_writer', c.oid, 'DELETE') AS writer_can_delete,
    has_table_privilege('pointofsale_evidence_writer', c.oid, 'TRUNCATE') AS writer_can_truncate,
    EXISTS (
        SELECT 1
        FROM pg_trigger t
        WHERE t.tgrelid = c.oid
          AND NOT t.tgisinternal
          AND t.tgname = 'TRG_ForensicAppendOnly_' || e.table_name
          AND pg_get_triggerdef(t.oid) LIKE '%BEFORE UPDATE OR DELETE OR TRUNCATE%'
          AND pg_get_triggerdef(t.oid) LIKE '%forensic.reject_append_only_evidence_mutation()%'
    ) AS append_only_trigger_installed
FROM evidence_tables e
LEFT JOIN pg_class c
    ON c.oid = to_regclass(format('public.%I', e.table_name))
ORDER BY e.table_name;

DO $$
DECLARE
    evidence_table text;
    unsafe_tables text[];
BEGIN
    SELECT array_agg(e.table_name ORDER BY e.table_name)
    INTO unsafe_tables
    FROM (
        VALUES
            ('AuditLogs'),
            ('UserGovernanceAuditLogs'),
            ('CommerceHubFinancialTransactions'),
            ('CommerceHubFinancialAuditLogs'),
            ('CommerceHubFinancialOperationAudits'),
            ('CommerceHubFinancialReconciliationReports'),
            ('CommerceHubVendorSettlements'),
            ('CommerceHubVendorPaymentAuditLogs'),
            ('LedgerEntries'),
            ('CommerceAccountingAudits'),
            ('StoreConfigAuditEntries'),
            ('MarketplaceEventOutboxDeadLetterArchives'),
            ('CommerceHubMerchantLifecycleLedgerEntries'),
            ('CommerceHubRecoveryOperationLedgerEntries')
    ) AS e(table_name)
    WHERE to_regclass(format('public.%I', e.table_name)) IS NULL
       OR (SELECT c.relowner FROM pg_class c WHERE c.oid = to_regclass(format('public.%I', e.table_name)))
            <> 'pointofsale_evidence_owner'::regrole
       OR has_table_privilege('pointofsale_evidence_writer', to_regclass(format('public.%I', e.table_name)), 'UPDATE')
       OR has_table_privilege('pointofsale_evidence_writer', to_regclass(format('public.%I', e.table_name)), 'DELETE')
       OR has_table_privilege('pointofsale_evidence_writer', to_regclass(format('public.%I', e.table_name)), 'TRUNCATE')
       OR EXISTS (
            SELECT 1
            FROM pg_class acl_class
            CROSS JOIN LATERAL aclexplode(COALESCE(acl_class.relacl, acldefault('r', acl_class.relowner))) acl
            WHERE acl_class.oid = to_regclass(format('public.%I', e.table_name))
              AND acl.privilege_type IN ('UPDATE', 'DELETE', 'TRUNCATE')
              AND acl.grantee <> 'pointofsale_evidence_owner'::regrole
       )
       OR NOT EXISTS (
            SELECT 1
            FROM pg_trigger t
            WHERE t.tgrelid = to_regclass(format('public.%I', e.table_name))
              AND NOT t.tgisinternal
              AND t.tgname = 'TRG_ForensicAppendOnly_' || e.table_name
       );

    IF unsafe_tables IS NOT NULL THEN
        RAISE EXCEPTION USING
            ERRCODE = '55000',
            MESSAGE = format(
                'FORENSIC_APPEND_ONLY_VERIFICATION_FAILED: unsafe evidence tables: %s',
                array_to_string(unsafe_tables, ', '));
    END IF;

    IF (
        SELECT n.nspowner
        FROM pg_namespace n
        WHERE n.nspname = 'forensic'
    ) <> 'pointofsale_evidence_owner'::regrole THEN
        RAISE EXCEPTION USING
            ERRCODE = '55000',
            MESSAGE = 'FORENSIC_APPEND_ONLY_VERIFICATION_FAILED: forensic schema owner drift';
    END IF;

    IF (
        SELECT p.proowner
        FROM pg_proc p
        INNER JOIN pg_namespace n ON n.oid = p.pronamespace
        WHERE n.nspname = 'forensic'
          AND p.proname = 'reject_append_only_evidence_mutation'
    ) <> 'pointofsale_evidence_owner'::regrole THEN
        RAISE EXCEPTION USING
            ERRCODE = '55000',
            MESSAGE = 'FORENSIC_APPEND_ONLY_VERIFICATION_FAILED: rejection function owner drift';
    END IF;

    FOREACH evidence_table IN ARRAY ARRAY['AuditLogs', 'LedgerEntries', 'CommerceHubMerchantLifecycleLedgerEntries']
    LOOP
        BEGIN
            EXECUTE format('UPDATE public.%I SET "Id" = "Id" WHERE FALSE', evidence_table);
            RAISE EXCEPTION USING
                ERRCODE = '55000',
                MESSAGE = format('FORENSIC_APPEND_ONLY_UPDATE_PROBE_FAILED: %s accepted UPDATE', evidence_table);
        EXCEPTION
            WHEN SQLSTATE '55000' THEN
                IF SQLERRM NOT LIKE 'FORENSIC_APPEND_ONLY_VIOLATION:%' THEN
                    RAISE;
                END IF;
        END;

        BEGIN
            EXECUTE format('DELETE FROM public.%I WHERE FALSE', evidence_table);
            RAISE EXCEPTION USING
                ERRCODE = '55000',
                MESSAGE = format('FORENSIC_APPEND_ONLY_DELETE_PROBE_FAILED: %s accepted DELETE', evidence_table);
        EXCEPTION
            WHEN SQLSTATE '55000' THEN
                IF SQLERRM NOT LIKE 'FORENSIC_APPEND_ONLY_VIOLATION:%' THEN
                    RAISE;
                END IF;
        END;
    END LOOP;
END
$$;
