--
-- PostgreSQL database dump
--

\restrict isfYeecELcvWLkrYfEgL62I3cWFEAEbAw22UY1ozEMOhTBBFIdRlovhMEkpvpAS

-- Dumped from database version 18.4 (Ubuntu 18.4-1.pgdg22.04+1)
-- Dumped by pg_dump version 18.0

-- Started on 2026-07-23 21:14:56

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- TOC entry 6 (class 2615 OID 20512)
-- Name: hangfire; Type: SCHEMA; Schema: -; Owner: postgres
--

CREATE SCHEMA hangfire;


ALTER SCHEMA hangfire OWNER TO postgres;

--
-- TOC entry 5 (class 2615 OID 2200)
-- Name: public; Type: SCHEMA; Schema: -; Owner: postgres
--

-- *not* creating schema, since initdb creates it


ALTER SCHEMA public OWNER TO postgres;

--
-- TOC entry 524 (class 1255 OID 55419)
-- Name: analyze_legacy_cash_balances(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.analyze_legacy_cash_balances() RETURNS TABLE("TenantId" uuid, "TenantName" character varying, "SharedCashBalance" numeric, "CashboxCount" bigint, "DedicatedTreasuryCount" bigint, "MigrationCategory" text, "CashboxIds" uuid[], "CashboxNames" text[], "HasInvalidTenantId" boolean)
    LANGUAGE plpgsql
    AS $$
BEGIN
    RETURN QUERY
    WITH generic_cash AS (
        -- Find all treasury accounts linked to the shared cash account (1010)
        -- with no ReferenceCode (indicating legacy/generic, not dedicated)
        SELECT
            t."TenantId",
            t."Id" AS "TreasuryAccountId",
            t."Name" AS "TreasuryAccountName",
            a."Id" AS "LedgerAccountId",
            a."AccountCode",
            a."CurrentBalance"
        FROM "TreasuryAccounts" t
        INNER JOIN "LedgerAccounts" a
            ON a."Id" = t."AccountId"
        WHERE NOT t."IsDeleted"
          AND NOT a."IsDeleted"
          AND t."Type" = 1  -- Cashbox type
          AND COALESCE(t."ReferenceCode", '') = ''  -- No reference code = legacy
          AND a."AccountCode" = '1010'  -- Shared cash account
          AND ABS(a."CurrentBalance") > 0.0001  -- Has non-zero balance
    ),
    tenant_balances AS (
        -- Aggregate balances per tenant
        SELECT
            gc."TenantId",
            SUM(gc."CurrentBalance") AS "TotalBalance"
        FROM generic_cash gc
        GROUP BY gc."TenantId"
    ),
    cashbox_info AS (
        -- Count cashboxes per tenant
        SELECT
            c."TenantId",
            COUNT(*) AS "CashboxCount",
            ARRAY_AGG(c."Id" ORDER BY c."Name") AS "CashboxIds",
            ARRAY_AGG(c."Name" ORDER BY c."Name") AS "CashboxNames"
        FROM "Cashboxes" c
        WHERE NOT c."IsDeleted"
        GROUP BY c."TenantId"
    ),
    dedicated_treasury AS (
        -- Count dedicated treasury accounts (with ReferenceCode)
        SELECT
            t."TenantId",
            COUNT(*) AS "DedicatedCount"
        FROM "TreasuryAccounts" t
        WHERE NOT t."IsDeleted"
          AND t."Type" = 1
          AND COALESCE(t."ReferenceCode", '') <> ''
        GROUP BY t."TenantId"
    ),
    tenant_names AS (
        -- Get tenant names (assuming StoreTenants table or similar)
        SELECT 
            st."Id" AS "TenantId",
            COALESCE(st."Name", st."StoreName", 'Unknown') AS "Name"
        FROM "StoreTenants" st
        WHERE NOT st."IsDeleted"
    )
    SELECT
        tb."TenantId",
        tn."Name" AS "TenantName",
        tb."TotalBalance" AS "SharedCashBalance",
        COALESCE(ci."CashboxCount", 0)::bigint AS "CashboxCount",
        COALESCE(dt."DedicatedCount", 0)::bigint AS "DedicatedTreasuryCount",
        CASE
            -- Case D: Invalid tenant ID (all zeros or null-like)
            WHEN tb."TenantId" = '00000000-0000-0000-0000-000000000000'::uuid 
                 OR tb."TenantId" IS NULL
            THEN 'D_INVALID_TENANT'
            
            -- Case C: No cashboxes - requires remediation
            WHEN COALESCE(ci."CashboxCount", 0) = 0
            THEN 'C_NO_CASHBOX'
            
            -- Case A: Exactly 1 cashbox - auto-migrate
            WHEN ci."CashboxCount" = 1
            THEN 'A_AUTO_MIGRATE'
            
            -- Case B: Multiple cashboxes - requires manual allocation
            WHEN ci."CashboxCount" > 1
            THEN 'B_MULTIPLE_CASHBOXES'
            
            ELSE 'UNKNOWN'
        END AS "MigrationCategory",
        ci."CashboxIds",
        ci."CashboxNames",
        (tb."TenantId" = '00000000-0000-0000-0000-000000000000'::uuid) AS "HasInvalidTenantId"
    FROM tenant_balances tb
    LEFT JOIN cashbox_info ci ON ci."TenantId" = tb."TenantId"
    LEFT JOIN dedicated_treasury dt ON dt."TenantId" = tb."TenantId"
    LEFT JOIN tenant_names tn ON tn."TenantId" = tb."TenantId"
    ORDER BY 
        CASE
            WHEN tb."TenantId" = '00000000-0000-0000-0000-000000000000'::uuid THEN 0
            WHEN COALESCE(ci."CashboxCount", 0) = 0 THEN 1
            WHEN ci."CashboxCount" = 1 THEN 2
            ELSE 3
        END,
        ABS(tb."TotalBalance") DESC;
END;
$$;


ALTER FUNCTION public.analyze_legacy_cash_balances() OWNER TO postgres;

--
-- TOC entry 504 (class 1255 OID 35797)
-- Name: audit_message_insert(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.audit_message_insert() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    -- Audit only - NO business logic, NO authorization checks
    INSERT INTO "ChatAuditLog" (
        "MessageId", "ServiceRequestId", "SenderUserId", "TenantId",
        "Action", "Timestamp", "Metadata"
    ) VALUES (
        NEW."Id", NEW."ServiceRequestId", NEW."SenderUserId", NEW."TenantId",
        'MESSAGE_SENT', NOW(),
        jsonb_build_object(
            'senderRole', NEW."SenderRole",
            'messageType', NEW."MessageType"
        )
    );
    RETURN NEW;
END;
$$;


ALTER FUNCTION public.audit_message_insert() OWNER TO postgres;

--
-- TOC entry 494 (class 1255 OID 35345)
-- Name: block_legacy_cashbox_insert(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.block_legacy_cashbox_insert() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
    enforcement_enabled boolean;
BEGIN
    -- Check if enforcement is enabled
    SELECT COALESCE("Value"::boolean, false) INTO enforcement_enabled
    FROM "SystemSettings"
    WHERE "Key" = 'LCR_ENFORCEMENT_ENABLED';
    
    IF enforcement_enabled THEN
        -- Allow only specific transaction types (Treasury operations):
        -- 1 = OpenDrawer
        -- 2 = CloseDrawer  
        -- 7 = CashIn (deposit)
        -- 8 = CashOut (withdrawal)
        IF NEW."TransactionType" NOT IN (1, 2, 7, 8) THEN
            RAISE EXCEPTION 'PHASE_LCR_VIOLATION: Direct insert to CashboxTransactions is blocked. Use Treasury/Ledger instead. TransactionType=%', NEW."TransactionType"
            USING ERRCODE = 'P0001';
        END IF;
    END IF;
    
    RETURN NEW;
END;
$$;


ALTER FUNCTION public.block_legacy_cashbox_insert() OWNER TO postgres;

--
-- TOC entry 495 (class 1255 OID 35346)
-- Name: block_legacy_visa_insert(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.block_legacy_visa_insert() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
    enforcement_enabled boolean;
BEGIN
    -- Check if enforcement is enabled
    SELECT COALESCE("Value"::boolean, false) INTO enforcement_enabled
    FROM "SystemSettings"
    WHERE "Key" = 'LCR_ENFORCEMENT_ENABLED';
    
    IF enforcement_enabled THEN
        -- Block ALL VisaTransactions inserts - all should go through Treasury/Ledger
        RAISE EXCEPTION 'PHASE_LCR_VIOLATION: Direct insert to VisaTransactions is blocked. Use Treasury/Ledger instead.'
        USING ERRCODE = 'P0001';
    END IF;
    
    RETURN NEW;
END;
$$;


ALTER FUNCTION public.block_legacy_visa_insert() OWNER TO postgres;

--
-- TOC entry 502 (class 1255 OID 35607)
-- Name: enforce_review_requires_completed_request(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.enforce_review_requires_completed_request() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM "ServiceRequests"
        WHERE "Id" = NEW."ServiceRequestId"
        AND "Status" = 'CompletedByBuyer'
        AND "IsDeleted" = FALSE
    ) THEN
        RAISE EXCEPTION 'Cannot create review: ServiceRequest must be CompletedByBuyer';
    END IF;
    
    RETURN NEW;
END;
$$;


ALTER FUNCTION public.enforce_review_requires_completed_request() OWNER TO postgres;

--
-- TOC entry 520 (class 1255 OID 37414)
-- Name: enforce_super_admin_presence(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.enforce_super_admin_presence() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF (SELECT COUNT(1) FROM "SuperAdmins") <= 1 THEN
        RAISE EXCEPTION 'At least one Super Admin must always exist.';
    END IF;

    RETURN OLD;
END;
$$;


ALTER FUNCTION public.enforce_super_admin_presence() OWNER TO postgres;

--
-- TOC entry 506 (class 1255 OID 36091)
-- Name: fn_affiliate_commission_ledger_immutable(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.fn_affiliate_commission_ledger_immutable() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    RAISE EXCEPTION 'AffiliateCommissionLedger is append-only. UPDATE and DELETE are forbidden.';
    RETURN NULL;
END;
$$;


ALTER FUNCTION public.fn_affiliate_commission_ledger_immutable() OWNER TO postgres;

--
-- TOC entry 486 (class 1255 OID 38026)
-- Name: fn_commercehub_financial_operation_audits_immutable(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.fn_commercehub_financial_operation_audits_immutable() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    RAISE EXCEPTION 'CommerceHubFinancialOperationAudits is append-only. UPDATE and DELETE are forbidden.';
END;
$$;


ALTER FUNCTION public.fn_commercehub_financial_operation_audits_immutable() OWNER TO postgres;

--
-- TOC entry 490 (class 1255 OID 38264)
-- Name: fn_commercehub_financial_reconciliation_reports_immutable(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.fn_commercehub_financial_reconciliation_reports_immutable() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    RAISE EXCEPTION 'CommerceHubFinancialReconciliationReports is append-only. UPDATE and DELETE are forbidden.';
END;
$$;


ALTER FUNCTION public.fn_commercehub_financial_reconciliation_reports_immutable() OWNER TO postgres;

--
-- TOC entry 484 (class 1255 OID 37707)
-- Name: fn_commercehub_financial_transactions_immutable(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.fn_commercehub_financial_transactions_immutable() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    RAISE EXCEPTION 'CommerceHubFinancialTransactions is append-only. UPDATE and DELETE are forbidden.';
END;
$$;


ALTER FUNCTION public.fn_commercehub_financial_transactions_immutable() OWNER TO postgres;

--
-- TOC entry 523 (class 1255 OID 37720)
-- Name: fn_commercehub_ledger_prevent_delete(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.fn_commercehub_ledger_prevent_delete() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF current_setting('app.ledger_maintenance_mode', true) = 'true' THEN
        RETURN OLD;
    END IF;

    RAISE EXCEPTION 'LEDGER_IMMUTABILITY_VIOLATION: DELETE operations are not allowed on CommerceHubFinancialTransactions. Ledger entries are append-only. Set session variable app.ledger_maintenance_mode=true for emergency corrections.'
        USING ERRCODE = 'P0001';

    RETURN NULL;
END;
$$;


ALTER FUNCTION public.fn_commercehub_ledger_prevent_delete() OWNER TO postgres;

--
-- TOC entry 491 (class 1255 OID 37719)
-- Name: fn_commercehub_ledger_prevent_update(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.fn_commercehub_ledger_prevent_update() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF current_setting('app.ledger_maintenance_mode', true) = 'true' THEN
        RETURN NEW;
    END IF;

    RAISE EXCEPTION 'LEDGER_IMMUTABILITY_VIOLATION: UPDATE operations are not allowed on CommerceHubFinancialTransactions. Ledger entries are append-only. Set session variable app.ledger_maintenance_mode=true for emergency corrections.'
        USING ERRCODE = 'P0001';

    RETURN NULL;
END;
$$;


ALTER FUNCTION public.fn_commercehub_ledger_prevent_update() OWNER TO postgres;

--
-- TOC entry 485 (class 1255 OID 37753)
-- Name: fn_commercehub_vendor_settlements_immutable(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.fn_commercehub_vendor_settlements_immutable() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    RAISE EXCEPTION 'CommerceHubVendorSettlements is append-only. UPDATE and DELETE are forbidden.';
END;
$$;


ALTER FUNCTION public.fn_commercehub_vendor_settlements_immutable() OWNER TO postgres;

--
-- TOC entry 488 (class 1255 OID 38071)
-- Name: fn_user_governance_audit_logs_immutable(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.fn_user_governance_audit_logs_immutable() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    RAISE EXCEPTION 'UserGovernanceAuditLogs is append-only. UPDATE and DELETE are forbidden.';
END;
$$;


ALTER FUNCTION public.fn_user_governance_audit_logs_immutable() OWNER TO postgres;

--
-- TOC entry 525 (class 1255 OID 55420)
-- Name: migrate_legacy_cash_to_dedicated_accounts(boolean, uuid); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.migrate_legacy_cash_to_dedicated_accounts(p_dry_run boolean DEFAULT true, p_operator_id uuid DEFAULT NULL::uuid) RETURNS TABLE("TenantId" uuid, "MigrationCategory" text, "Status" text, "SourceBalance" numeric, "MigrationAmount" numeric, "TargetAccountCode" character varying, "JournalNumber" character varying, "Message" text)
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_tenant_record RECORD;
    v_source_account_id uuid;
    v_source_balance decimal(18,4);
    v_target_account_id uuid;
    v_target_account_code varchar(20);
    v_cashbox_id uuid;
    v_cashbox_name varchar(200);
    v_journal_id uuid;
    v_journal_number varchar(50);
    v_migration_id uuid;
    v_idempotency_key varchar(255);
    v_migration_amount decimal(18,4);
    v_treasury_account_id uuid;
BEGIN
    -- Create temp table for analysis results
    CREATE TEMP TABLE IF NOT EXISTS _migration_analysis AS
    SELECT * FROM analyze_legacy_cash_balances();
    
    -- Process each tenant
    FOR v_tenant_record IN 
        SELECT * FROM _migration_analysis 
        WHERE "MigrationCategory" NOT IN ('D_INVALID_TENANT')
        ORDER BY "MigrationCategory", ABS("SharedCashBalance") DESC
    LOOP
        -- Initialize variables
        v_migration_amount := 0;
        v_target_account_id := null;
        v_target_account_code := null;
        v_journal_id := null;
        v_journal_number := null;
        
        -- Get the source treasury account and ledger account
        SELECT t."Id", a."Id", a."CurrentBalance"
        INTO v_treasury_account_id, v_source_account_id, v_source_balance
        FROM "TreasuryAccounts" t
        INNER JOIN "LedgerAccounts" a ON a."Id" = t."AccountId"
        WHERE t."TenantId" = v_tenant_record."TenantId"
          AND NOT t."IsDeleted"
          AND t."Type" = 1
          AND COALESCE(t."ReferenceCode", '') = ''
          AND a."AccountCode" = '1010'
        LIMIT 1;
        
        -- Handle based on category
        CASE v_tenant_record."MigrationCategory"
            -- ═════════════════════════════════════════════════════════════════
            -- CASE A: Auto-migrate (exactly 1 cashbox)
            -- ═════════════════════════════════════════════════════════════════
            WHEN 'A_AUTO_MIGRATE' THEN
                BEGIN
                    -- Get the single cashbox
                    v_cashbox_id := v_tenant_record."CashboxIds"[1];
                    v_cashbox_name := v_tenant_record."CashboxNames"[1];
                    
                    -- Find or create dedicated treasury account for this cashbox
                    -- First, check if it already exists
                    SELECT t."AccountId", a."AccountCode"
                    INTO v_target_account_id, v_target_account_code
                    FROM "TreasuryAccounts" t
                    INNER JOIN "LedgerAccounts" a ON a."Id" = t."AccountId"
                    WHERE t."TenantId" = v_tenant_record."TenantId"
                      AND t."Type" = 1
                      AND t."ReferenceCode" = REPLACE(v_cashbox_id::text, '-', '')
                      AND NOT t."IsDeleted";
                    
                    -- If no dedicated account exists, we need to create one
                    IF v_target_account_id IS NULL THEN
                        -- Check if the ledger account exists (created by CashboxLedgerAccountService)
                        v_target_account_code := '1010-' || UPPER(SUBSTRING(v_cashbox_id::text, 25, 8));
                        
                        SELECT "Id" INTO v_target_account_id
                        FROM "LedgerAccounts"
                        WHERE "TenantId" = v_tenant_record."TenantId"
                          AND "AccountCode" = v_target_account_code
                          AND NOT "IsDeleted";
                        
                        -- If still no account, create it
                        IF v_target_account_id IS NULL THEN
                            IF NOT p_dry_run THEN
                                -- Create the dedicated ledger account
                                INSERT INTO "LedgerAccounts" (
                                    "Id", "TenantId", "AccountCode", "Name", "NameAr",
                                    "AccountType", "Level", "CurrentBalance",
                                    "IsSystem", "PreventNegative", "IsActive",
                                    "CreatedAtUtc", "ParentAccountId"
                                )
                                SELECT
                                    gen_random_uuid(),
                                    v_tenant_record."TenantId",
                                    v_target_account_code,
                                    'Cashbox - ' || v_cashbox_name,
                                    'صندوق - ' || COALESCE((
                                        SELECT "NameAr" FROM "Cashboxes" WHERE "Id" = v_cashbox_id
                                    ), v_cashbox_name),
                                    1, -- Asset
                                    2,
                                    0,
                                    false,
                                    true,
                                    true,
                                    now(),
                                    (SELECT "Id" FROM "LedgerAccounts" 
                                     WHERE "TenantId" = v_tenant_record."TenantId" 
                                       AND "AccountCode" = '1010' 
                                       AND NOT "IsDeleted" 
                                     LIMIT 1)
                                RETURNING "Id" INTO v_target_account_id;
                            END IF;
                        END IF;
                        
                        -- Create the treasury account mapping
                        IF NOT p_dry_run AND v_target_account_id IS NOT NULL THEN
                            INSERT INTO "TreasuryAccounts" (
                                "Id", "TenantId", "AccountId", "Type", "Name",
                                "ReferenceCode", "IsDefault", "IsActive", "DisplayOrder"
                            )
                            VALUES (
                                gen_random_uuid(),
                                v_tenant_record."TenantId",
                                v_target_account_id,
                                1, -- Cashbox
                                v_cashbox_name,
                                REPLACE(v_cashbox_id::text, '-', ''),
                                false,
                                true,
                                10
                            );
                        END IF;
                    END IF;
                    
                    -- Now perform the migration
                    IF v_target_account_id IS NOT NULL AND v_source_balance <> 0 THEN
                        v_migration_amount := v_source_balance;
                        
                        IF NOT p_dry_run THEN
                            -- Generate idempotency key
                            v_idempotency_key := 'LEGACY_CASH_MIGRATION:' || v_tenant_record."TenantId"::text || ':' || v_cashbox_id::text;
                            
                            -- Check if already migrated
                            IF NOT EXISTS (
                                SELECT 1 FROM "JournalEntries" 
                                WHERE "TenantId" = v_tenant_record."TenantId"
                                  AND "IdempotencyKey" = v_idempotency_key
                            ) THEN
                                -- Create the journal entry
                                BEGIN
                                    -- Generate journal number
                                    v_journal_number := 'JE-' || to_char(now(), 'YYYY') || '-' || 
                                        LPAD(COALESCE(
                                            (SELECT COUNT(*) + 1 
                                             FROM "JournalEntries" 
                                             WHERE "TenantId" = v_tenant_record."TenantId"
                                               AND EXTRACT(YEAR FROM "PostedAtUtc") = EXTRACT(YEAR FROM now())),
                                            1
                                        )::text, 6, '0');
                                    
                                    -- Insert journal entry
                                    INSERT INTO "JournalEntries" (
                                        "Id", "TenantId", "JournalNumber", "JournalDate", "PostedAtUtc",
                                        "SourceType", "SourceId", "Description", "IdempotencyKey",
                                        "Status", "CreatedByUserId"
                                    )
                                    VALUES (
                                        gen_random_uuid(),
                                        v_tenant_record."TenantId",
                                        v_journal_number,
                                        CURRENT_DATE,
                                        now(),
                                        'LegacyCashMigration',
                                        v_cashbox_id,
                                        'Migration of legacy shared cash balance to dedicated cashbox account',
                                        v_idempotency_key,
                                        'Posted',
                                        p_operator_id
                                    )
                                    RETURNING "Id" INTO v_journal_id;
                                    
                                    -- Create ledger entries
                                    -- Debit: Target (dedicated cashbox account)
                                    INSERT INTO "LedgerEntries" (
                                        "Id", "AccountId", "JournalEntryId", "TenantId",
                                        "DebitAmount", "CreditAmount", "BalanceAfter",
                                        "CreatedAtUtc"
                                    )
                                    VALUES (
                                        gen_random_uuid(),
                                        v_target_account_id,
                                        v_journal_id,
                                        v_tenant_record."TenantId",
                                        ABS(v_migration_amount),
                                        0,
                                        ABS(v_migration_amount),
                                        now()
                                    );
                                    
                                    -- Credit: Source (shared cash account 1010)
                                    INSERT INTO "LedgerEntries" (
                                        "Id", "AccountId", "JournalEntryId", "TenantId",
                                        "DebitAmount", "CreditAmount", "BalanceAfter",
                                        "CreatedAtUtc"
                                    )
                                    VALUES (
                                        gen_random_uuid(),
                                        v_source_account_id,
                                        v_journal_id,
                                        v_tenant_record."TenantId",
                                        0,
                                        ABS(v_migration_amount),
                                        0,
                                        now()
                                    );
                                    
                                    -- Update account balances
                                    UPDATE "LedgerAccounts"
                                    SET "CurrentBalance" = "CurrentBalance" + ABS(v_migration_amount),
                                        "UpdatedAtUtc" = now()
                                    WHERE "Id" = v_target_account_id;
                                    
                                    UPDATE "LedgerAccounts"
                                    SET "CurrentBalance" = "CurrentBalance" - ABS(v_migration_amount),
                                        "UpdatedAtUtc" = now()
                                    WHERE "Id" = v_source_account_id;
                                    
                                    -- Log the migration
                                    INSERT INTO "_LegacyCashMigrationLog" (
                                        "TenantId", "SourceType", "SourceTreasuryAccountId",
                                        "SourceAccountCode", "SourceBalanceBefore",
                                        "TargetAccountId", "TargetAccountCode",
                                        "TargetCashboxId", "TargetCashboxName",
                                        "MigrationAmount", "JournalEntryId", "JournalNumber",
                                        "Status", "OperatorId"
                                    )
                                    VALUES (
                                        v_tenant_record."TenantId",
                                        'AUTO_MIGRATE',
                                        v_treasury_account_id,
                                        '1010',
                                        v_source_balance,
                                        v_target_account_id,
                                        v_target_account_code,
                                        v_cashbox_id,
                                        v_cashbox_name,
                                        v_migration_amount,
                                        v_journal_id,
                                        v_journal_number,
                                        'COMPLETED',
                                        p_operator_id
                                    );
                                    
                                EXCEPTION WHEN OTHERS THEN
                                    -- Log failure
                                    INSERT INTO "_LegacyCashMigrationLog" (
                                        "TenantId", "SourceType", "SourceTreasuryAccountId",
                                        "SourceAccountCode", "SourceBalanceBefore",
                                        "MigrationAmount", "Status", "FailureReason", "OperatorId"
                                    )
                                    VALUES (
                                        v_tenant_record."TenantId",
                                        'AUTO_MIGRATE',
                                        v_treasury_account_id,
                                        '1010',
                                        v_source_balance,
                                        v_migration_amount,
                                        'FAILED',
                                        SQLERRM,
                                        p_operator_id
                                    );
                                    
                                    RAISE;
                                END;
                            ELSE
                                -- Already migrated
                                SELECT "Id", "JournalNumber" INTO v_journal_id, v_journal_number
                                FROM "JournalEntries"
                                WHERE "TenantId" = v_tenant_record."TenantId"
                                  AND "IdempotencyKey" = v_idempotency_key;
                                  
                                v_target_account_code := 'ALREADY_MIGRATED';
                            END IF;
                        END IF;
                    END IF;
                    
                    RETURN QUERY SELECT
                        v_tenant_record."TenantId",
                        v_tenant_record."MigrationCategory",
                        CASE WHEN p_dry_run THEN 'DRY_RUN' ELSE 'COMPLETED' END,
                        v_source_balance,
                        v_migration_amount,
                        v_target_account_code,
                        v_journal_number,
                        'Auto-migrated to cashbox: ' || v_cashbox_name;
                END;
                
            -- ═════════════════════════════════════════════════════════════════
            -- CASE B: Multiple cashboxes - requires manual allocation
            -- ═════════════════════════════════════════════════════════════════
            WHEN 'B_MULTIPLE_CASHBOXES' THEN
                BEGIN
                    -- Log for manual review
                    IF NOT p_dry_run THEN
                        INSERT INTO "_LegacyCashMigrationLog" (
                            "TenantId", "SourceType", "SourceTreasuryAccountId",
                            "SourceAccountCode", "SourceBalanceBefore",
                            "Status", "FailureReason", "OperatorId"
                        )
                        VALUES (
                            v_tenant_record."TenantId",
                            'MANUAL_REQUIRED',
                            v_treasury_account_id,
                            '1010',
                            v_source_balance,
                            'SKIPPED',
                            'Multiple cashboxes (' || v_tenant_record."CashboxCount" || ') require manual allocation decision',
                            p_operator_id
                        );
                    END IF;
                    
                    RETURN QUERY SELECT
                        v_tenant_record."TenantId",
                        v_tenant_record."MigrationCategory",
                        'REQUIRES_MANUAL_ACTION',
                        v_source_balance,
                        0,
                        NULL::varchar(20),
                        NULL::varchar(50),
                        'Multiple cashboxes (' || v_tenant_record."CashboxCount" || '): ' || 
                        array_to_string(v_tenant_record."CashboxNames", ', ') ||
                        '. Manual allocation required.';
                END;
                
            -- ═════════════════════════════════════════════════════════════════
            -- CASE C: No cashboxes - requires remediation
            -- ═════════════════════════════════════════════════════════════════
            WHEN 'C_NO_CASHBOX' THEN
                BEGIN
                    -- Log for remediation
                    IF NOT p_dry_run THEN
                        INSERT INTO "_LegacyCashMigrationLog" (
                            "TenantId", "SourceType", "SourceTreasuryAccountId",
                            "SourceAccountCode", "SourceBalanceBefore",
                            "Status", "FailureReason", "OperatorId"
                        )
                        VALUES (
                            v_tenant_record."TenantId",
                            'REMEDIATION_REQUIRED',
                            v_treasury_account_id,
                            '1010',
                            v_source_balance,
                            'SKIPPED',
                            'No cashboxes exist for this tenant. Create a default cashbox or move to suspense account 2030.',
                            p_operator_id
                        );
                    END IF;
                    
                    RETURN QUERY SELECT
                        v_tenant_record."TenantId",
                        v_tenant_record."MigrationCategory",
                        'REQUIRES_REMEDIATION',
                        v_source_balance,
                        0,
                        NULL::varchar(20),
                        NULL::varchar(50),
                        'No cashboxes. Create default cashbox or move to suspense account 2030.';
                END;
        END CASE;
    END LOOP;
    
    -- Handle invalid tenants (Case D)
    FOR v_tenant_record IN 
        SELECT * FROM _migration_analysis 
        WHERE "MigrationCategory" = 'D_INVALID_TENANT'
    LOOP
        IF NOT p_dry_run THEN
            INSERT INTO "_LegacyCashMigrationLog" (
                "TenantId", "SourceType", "SourceAccountCode", "SourceBalanceBefore",
                "Status", "FailureReason", "OperatorId"
            )
            VALUES (
                v_tenant_record."TenantId",
                'CRITICAL_DATA_FIX',
                '1010',
                v_tenant_record."SharedCashBalance",
                'SKIPPED',
                'CRITICAL: Invalid tenant ID (all zeros). Data must be reassigned or removed before migration can proceed.',
                p_operator_id
            );
        END IF;
        
        RETURN QUERY SELECT
            v_tenant_record."TenantId",
            v_tenant_record."MigrationCategory",
            'CRITICAL_DATA_FIX_REQUIRED',
            v_tenant_record."SharedCashBalance",
            0,
            NULL::varchar(20),
            NULL::varchar(50),
            'CRITICAL: Invalid tenant ID. Data must be reassigned or removed.';
    END LOOP;
    
    -- Cleanup
    DROP TABLE IF EXISTS _migration_analysis;
    
    RETURN;
END;
$$;


ALTER FUNCTION public.migrate_legacy_cash_to_dedicated_accounts(p_dry_run boolean, p_operator_id uuid) OWNER TO postgres;

--
-- TOC entry 511 (class 1255 OID 36367)
-- Name: prevent_balance_ledger_mutation(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.prevent_balance_ledger_mutation() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF TG_OP = 'UPDATE' THEN
        RAISE EXCEPTION 'AffiliateBalanceLedger is append-only. UPDATE forbidden.';
    ELSIF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'AffiliateBalanceLedger is append-only. DELETE forbidden.';
    END IF;
    RETURN NULL;
END;
$$;


ALTER FUNCTION public.prevent_balance_ledger_mutation() OWNER TO postgres;

--
-- TOC entry 505 (class 1255 OID 35423)
-- Name: prevent_fulfillment_mode_update(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.prevent_fulfillment_mode_update() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF OLD."FulfillmentMode" IS DISTINCT FROM NEW."FulfillmentMode" THEN
        RAISE EXCEPTION 'FulfillmentMode is immutable and cannot be changed after order creation. OrderId: %, Old: %, New: %',
            OLD."Id", OLD."FulfillmentMode", NEW."FulfillmentMode"
            USING ERRCODE = 'integrity_constraint_violation';
    END IF;
    
    IF OLD."FulfillmentCarrierCode" IS DISTINCT FROM NEW."FulfillmentCarrierCode" THEN
        RAISE EXCEPTION 'FulfillmentCarrierCode is immutable and cannot be changed after order creation. OrderId: %, Old: %, New: %',
            OLD."Id", OLD."FulfillmentCarrierCode", NEW."FulfillmentCarrierCode"
            USING ERRCODE = 'integrity_constraint_violation';
    END IF;
    
    RETURN NEW;
END;
$$;


ALTER FUNCTION public.prevent_fulfillment_mode_update() OWNER TO postgres;

--
-- TOC entry 483 (class 1255 OID 35130)
-- Name: prevent_ledger_journal_modification(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.prevent_ledger_journal_modification() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF TG_OP = 'UPDATE' THEN
        IF OLD."JournalNumber" != NEW."JournalNumber" OR
           OLD."JournalDate" != NEW."JournalDate" OR
           OLD."SourceType" != NEW."SourceType" OR
           OLD."SourceId" != NEW."SourceId" OR
           OLD."Description" != NEW."Description" OR
           OLD."IdempotencyKey" != NEW."IdempotencyKey" OR
           OLD."PostedAtUtc" != NEW."PostedAtUtc" THEN
            RAISE EXCEPTION 'Journal entries are immutable. Only status and reversal fields can be updated.';
        END IF;
        RETURN NEW;
    END IF;
    
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'Journal entries cannot be deleted. Use reversal instead.';
    END IF;
    
    RETURN NULL;
END;
$$;


ALTER FUNCTION public.prevent_ledger_journal_modification() OWNER TO postgres;

--
-- TOC entry 487 (class 1255 OID 35131)
-- Name: prevent_ledger_line_modification(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.prevent_ledger_line_modification() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF TG_OP = 'UPDATE' THEN
        RAISE EXCEPTION 'Ledger lines are immutable and cannot be updated.';
    END IF;
    
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'Ledger lines cannot be deleted. Use journal reversal instead.';
    END IF;
    
    RETURN NULL;
END;
$$;


ALTER FUNCTION public.prevent_ledger_line_modification() OWNER TO postgres;

--
-- TOC entry 503 (class 1255 OID 35718)
-- Name: prevent_manual_trust_modification(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.prevent_manual_trust_modification() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    -- This trigger ensures trust aggregates are only modified through domain events
    -- In practice, this is enforced by application layer, but we add DB-level protection
    
    -- Allow INSERT (initial creation)
    IF TG_OP = 'INSERT' THEN
        RETURN NEW;
    END IF;
    
    -- Allow UPDATE only if Version is incremented
    IF TG_OP = 'UPDATE' THEN
        IF NEW."Version" <= OLD."Version" THEN
            RAISE EXCEPTION 'ProviderTrustAggregate Version must be incremented on update';
        END IF;
        RETURN NEW;
    END IF;
    
    RETURN NEW;
END;
$$;


ALTER FUNCTION public.prevent_manual_trust_modification() OWNER TO postgres;

--
-- TOC entry 521 (class 1255 OID 35664)
-- Name: prevent_message_modification(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.prevent_message_modification() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF OLD."Body" IS DISTINCT FROM NEW."Body" OR
       OLD."SenderUserId" IS DISTINCT FROM NEW."SenderUserId" OR
       OLD."SenderRole" IS DISTINCT FROM NEW."SenderRole" OR
       OLD."MessageType" IS DISTINCT FROM NEW."MessageType" THEN
        RAISE EXCEPTION 'Messages are immutable - cannot modify Body, SenderUserId, SenderRole, or MessageType';
    END IF;
    RETURN NEW;
END;
$$;


ALTER FUNCTION public.prevent_message_modification() OWNER TO postgres;

--
-- TOC entry 489 (class 1255 OID 35200)
-- Name: prevent_system_account_deletion(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.prevent_system_account_deletion() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF OLD."IsSystem" = TRUE THEN
        RAISE EXCEPTION 'System accounts cannot be deleted. Account: % (%)', OLD."Name", OLD."AccountCode";
    END IF;
    RETURN OLD;
END;
$$;


ALTER FUNCTION public.prevent_system_account_deletion() OWNER TO postgres;

--
-- TOC entry 492 (class 1255 OID 35202)
-- Name: prevent_system_account_modification(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.prevent_system_account_modification() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF OLD."IsSystem" = TRUE THEN
        IF OLD."AccountCode" != NEW."AccountCode" THEN
            RAISE EXCEPTION 'System account code cannot be changed. Account: %', OLD."AccountCode";
        END IF;
        IF OLD."AccountType" != NEW."AccountType" THEN
            RAISE EXCEPTION 'System account type cannot be changed. Account: %', OLD."AccountCode";
        END IF;
        IF OLD."IsSystem" != NEW."IsSystem" THEN
            RAISE EXCEPTION 'System account flag cannot be removed. Account: %', OLD."AccountCode";
        END IF;
    END IF;
    RETURN NEW;
END;
$$;


ALTER FUNCTION public.prevent_system_account_modification() OWNER TO postgres;

--
-- TOC entry 493 (class 1255 OID 35204)
-- Name: prevent_system_payment_method_deletion(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.prevent_system_payment_method_deletion() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF OLD."IsSystem" = TRUE THEN
        RAISE EXCEPTION 'System payment methods cannot be deleted. Code: %', OLD."Code";
    END IF;
    RETURN OLD;
END;
$$;


ALTER FUNCTION public.prevent_system_payment_method_deletion() OWNER TO postgres;

--
-- TOC entry 522 (class 1255 OID 37413)
-- Name: protect_system_super_admin_mutation(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.protect_system_super_admin_mutation() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF OLD."IsSystem" = true THEN
        IF TG_OP = 'DELETE' THEN
            RAISE EXCEPTION 'System root Super Admin cannot be deleted.';
        END IF;

        -- ONLY protect immutable identity fields
        -- Allow: PasswordHash, LastLoginAt, RefreshTokenHash,
        --        RefreshTokenExpiresAtUtc, TokenVersion
        IF NEW."Id" IS DISTINCT FROM OLD."Id" THEN
            RAISE EXCEPTION 'System root Super Admin Id cannot be changed.';
        END IF;

        IF NEW."Email" IS DISTINCT FROM OLD."Email" THEN
            RAISE EXCEPTION 'System root Super Admin Email cannot be changed.';
        END IF;

        IF NEW."IsSystem" IS DISTINCT FROM OLD."IsSystem" THEN
            RAISE EXCEPTION 'System root Super Admin IsSystem flag cannot be changed.';
        END IF;

        IF NEW."CreatedAt" IS DISTINCT FROM OLD."CreatedAt" THEN
            RAISE EXCEPTION 'System root Super Admin CreatedAt cannot be changed.';
        END IF;
    END IF;

    RETURN COALESCE(NEW, OLD);
END;
$$;


ALTER FUNCTION public.protect_system_super_admin_mutation() OWNER TO postgres;

--
-- TOC entry 527 (class 1255 OID 2287587)
-- Name: redact_pii(text); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.redact_pii(input_text text) RETURNS text
    LANGUAGE plpgsql IMMUTABLE
    AS $$
BEGIN
    IF input_text IS NULL OR input_text = '' THEN
        RETURN input_text;
    END IF;
    
    -- Redact email addresses
    input_text := REGEXP_REPLACE(input_text, '\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b', '[EMAIL_REDACTED]', 'g');
    
    -- Redact phone numbers (various formats: +201234567890, 01234567890, (123) 456-7890, etc.)
    input_text := REGEXP_REPLACE(input_text, '\+?[\d\s\-\(\)]{10,}', '[PHONE_REDACTED]', 'g');
    
    -- Redact potential addresses (street numbers + street names)
    input_text := REGEXP_REPLACE(input_text, '\b\d+\s+[A-Za-z\s]+(?:Street|St|Avenue|Ave|Road|Rd|Boulevard|Blvd|Lane|Ln|Drive|Dr)\b', '[ADDRESS_REDACTED]', 'gi');
    
    -- Redact common PII field values in JSON (name, address, phone, email fields)
    input_text := REGEXP_REPLACE(input_text, '("(?:name|firstName|lastName|fullName|customerName|recipient)"\s*:\s*")[^"]*(")', '\1[NAME_REDACTED]\2', 'gi');
    input_text := REGEXP_REPLACE(input_text, '("(?:phone|mobile|telephone|phoneNumber)"\s*:\s*")[^"]*(")', '\1[PHONE_REDACTED]\2', 'gi');
    input_text := REGEXP_REPLACE(input_text, '("(?:email|emailAddress)"\s*:\s*")[^"]*(")', '\1[EMAIL_REDACTED]\2', 'gi');
    input_text := REGEXP_REPLACE(input_text, '("(?:address|street|city|state|zip|postal|postcode)"\s*:\s*")[^"]*(")', '\1[ADDRESS_REDACTED]\2', 'gi');
    
    RETURN input_text;
END;
$$;


ALTER FUNCTION public.redact_pii(input_text text) OWNER TO postgres;

--
-- TOC entry 496 (class 1255 OID 35349)
-- Name: tr_block_cashbox_transactions_insert(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.tr_block_cashbox_transactions_insert() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    RAISE EXCEPTION 'PHASE_TOJ_BLOCKED: Direct INSERT to CashboxTransactions is blocked. Use ITreasuryOperationService to post journal entries.';
END;
$$;


ALTER FUNCTION public.tr_block_cashbox_transactions_insert() OWNER TO postgres;

--
-- TOC entry 497 (class 1255 OID 35351)
-- Name: tr_block_cashbox_transactions_update(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.tr_block_cashbox_transactions_update() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    RAISE EXCEPTION 'PHASE_TOJ_BLOCKED: Direct UPDATE to CashboxTransactions is blocked. Use ITreasuryOperationService for corrections.';
END;
$$;


ALTER FUNCTION public.tr_block_cashbox_transactions_update() OWNER TO postgres;

--
-- TOC entry 500 (class 1255 OID 35357)
-- Name: tr_block_treasury_transactions_insert(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.tr_block_treasury_transactions_insert() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    RAISE EXCEPTION 'PHASE_TOJ_BLOCKED: Direct INSERT to TreasuryTransactions is blocked. Use ITreasuryOperationService to post journal entries.';
END;
$$;


ALTER FUNCTION public.tr_block_treasury_transactions_insert() OWNER TO postgres;

--
-- TOC entry 501 (class 1255 OID 35359)
-- Name: tr_block_treasury_transactions_update(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.tr_block_treasury_transactions_update() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    RAISE EXCEPTION 'PHASE_TOJ_BLOCKED: Direct UPDATE to TreasuryTransactions is blocked. Use ITreasuryOperationService for corrections.';
END;
$$;


ALTER FUNCTION public.tr_block_treasury_transactions_update() OWNER TO postgres;

--
-- TOC entry 498 (class 1255 OID 35353)
-- Name: tr_block_visa_transactions_insert(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.tr_block_visa_transactions_insert() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    RAISE EXCEPTION 'PHASE_TOJ_BLOCKED: Direct INSERT to VisaTransactions is blocked. Use ITreasuryOperationService to post journal entries.';
END;
$$;


ALTER FUNCTION public.tr_block_visa_transactions_insert() OWNER TO postgres;

--
-- TOC entry 499 (class 1255 OID 35355)
-- Name: tr_block_visa_transactions_update(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.tr_block_visa_transactions_update() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    RAISE EXCEPTION 'PHASE_TOJ_BLOCKED: Direct UPDATE to VisaTransactions is blocked. Use ITreasuryOperationService for corrections.';
END;
$$;


ALTER FUNCTION public.tr_block_visa_transactions_update() OWNER TO postgres;

--
-- TOC entry 526 (class 1255 OID 55422)
-- Name: validate_legacy_cash_migration(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.validate_legacy_cash_migration() RETURNS TABLE("CheckName" text, "Status" text, "Details" text, "Value" numeric)
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_shared_balance decimal(18,4);
    v_migrated_count int;
    v_failed_count int;
    v_pending_count int;
BEGIN
    -- Check 1: Shared account (1010) balance should be zero
    SELECT COALESCE(SUM(a."CurrentBalance"), 0)
    INTO v_shared_balance
    FROM "TreasuryAccounts" t
    INNER JOIN "LedgerAccounts" a ON a."Id" = t."AccountId"
    WHERE NOT t."IsDeleted"
      AND NOT a."IsDeleted"
      AND t."Type" = 1
      AND COALESCE(t."ReferenceCode", '') = ''
      AND a."AccountCode" = '1010';
    
    RETURN QUERY SELECT
        'SharedCashAccount1010Balance'::text,
        CASE WHEN ABS(v_shared_balance) < 0.01 THEN 'PASS' ELSE 'FAIL' END,
        'Balance in shared cash account (1010) should be zero after migration',
        v_shared_balance;
    
    -- Check 2: Count of completed migrations
    SELECT COUNT(*)
    INTO v_migrated_count
    FROM "_LegacyCashMigrationLog"
    WHERE "Status" = 'COMPLETED';
    
    RETURN QUERY SELECT
        'CompletedMigrations'::text,
        CASE WHEN v_migrated_count > 0 THEN 'INFO' ELSE 'WARNING' END,
        'Number of successfully completed migrations',
        v_migrated_count::decimal(18,4);
    
    -- Check 3: Count of failed migrations
    SELECT COUNT(*)
    INTO v_failed_count
    FROM "_LegacyCashMigrationLog"
    WHERE "Status" = 'FAILED';
    
    RETURN QUERY SELECT
        'FailedMigrations'::text,
        CASE WHEN v_failed_count = 0 THEN 'PASS' ELSE 'FAIL' END,
        'Number of failed migrations (should be zero)',
        v_failed_count::decimal(18,4);
    
    -- Check 4: Count of pending/manual migrations
    SELECT COUNT(*)
    INTO v_pending_count
    FROM "_LegacyCashMigrationLog"
    WHERE "Status" = 'SKIPPED';
    
    RETURN QUERY SELECT
        'PendingManualAction'::text,
        CASE WHEN v_pending_count = 0 THEN 'PASS' ELSE 'WARNING' END,
        'Number of tenants requiring manual action',
        v_pending_count::decimal(18,4);
    
    -- Check 5: Total migrated amount
    RETURN QUERY SELECT
        'TotalMigratedAmount'::text,
        'INFO'::text,
        'Total amount migrated from shared to dedicated accounts',
        COALESCE((
            SELECT SUM("MigrationAmount") 
            FROM "_LegacyCashMigrationLog" 
            WHERE "Status" = 'COMPLETED'
        ), 0);
    
    RETURN;
END;
$$;


ALTER FUNCTION public.validate_legacy_cash_migration() OWNER TO postgres;

--
-- TOC entry 519 (class 1255 OID 35609)
-- Name: validate_service_request_status_transition(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.validate_service_request_status_transition() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
    old_status "ServiceRequestStatus";
BEGIN
    IF TG_OP = 'UPDATE' THEN
        old_status := OLD."Status";
        
        -- Valid transitions only
        IF old_status = 'Pending' AND NEW."Status" NOT IN ('Accepted', 'Cancelled') THEN
            RAISE EXCEPTION 'Invalid transition from Pending to %', NEW."Status";
        END IF;
        
        IF old_status = 'Accepted' AND NEW."Status" NOT IN ('CompletedByProvider', 'Cancelled') THEN
            RAISE EXCEPTION 'Invalid transition from Accepted to %', NEW."Status";
        END IF;
        
        IF old_status = 'CompletedByProvider' AND NEW."Status" != 'CompletedByBuyer' THEN
            RAISE EXCEPTION 'Invalid transition from CompletedByProvider to %', NEW."Status";
        END IF;
        
        -- Terminal states cannot transition
        IF old_status IN ('CompletedByBuyer', 'Cancelled') THEN
            RAISE EXCEPTION 'Cannot transition from terminal state %', old_status;
        END IF;
    END IF;
    
    RETURN NEW;
END;
$$;


ALTER FUNCTION public.validate_service_request_status_transition() OWNER TO postgres;

SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- TOC entry 269 (class 1259 OID 20867)
-- Name: aggregatedcounter; Type: TABLE; Schema: hangfire; Owner: postgres
--

CREATE TABLE hangfire.aggregatedcounter (
    id bigint NOT NULL,
    key text NOT NULL,
    value bigint NOT NULL,
    expireat timestamp with time zone
);


ALTER TABLE hangfire.aggregatedcounter OWNER TO postgres;

--
-- TOC entry 268 (class 1259 OID 20866)
-- Name: aggregatedcounter_id_seq; Type: SEQUENCE; Schema: hangfire; Owner: postgres
--

CREATE SEQUENCE hangfire.aggregatedcounter_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE hangfire.aggregatedcounter_id_seq OWNER TO postgres;

--
-- TOC entry 6784 (class 0 OID 0)
-- Dependencies: 268
-- Name: aggregatedcounter_id_seq; Type: SEQUENCE OWNED BY; Schema: hangfire; Owner: postgres
--

ALTER SEQUENCE hangfire.aggregatedcounter_id_seq OWNED BY hangfire.aggregatedcounter.id;


--
-- TOC entry 251 (class 1259 OID 20520)
-- Name: counter; Type: TABLE; Schema: hangfire; Owner: postgres
--

CREATE TABLE hangfire.counter (
    id bigint NOT NULL,
    key text NOT NULL,
    value bigint NOT NULL,
    expireat timestamp with time zone
);


ALTER TABLE hangfire.counter OWNER TO postgres;

--
-- TOC entry 250 (class 1259 OID 20519)
-- Name: counter_id_seq; Type: SEQUENCE; Schema: hangfire; Owner: postgres
--

CREATE SEQUENCE hangfire.counter_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE hangfire.counter_id_seq OWNER TO postgres;

--
-- TOC entry 6785 (class 0 OID 0)
-- Dependencies: 250
-- Name: counter_id_seq; Type: SEQUENCE OWNED BY; Schema: hangfire; Owner: postgres
--

ALTER SEQUENCE hangfire.counter_id_seq OWNED BY hangfire.counter.id;


--
-- TOC entry 253 (class 1259 OID 20531)
-- Name: hash; Type: TABLE; Schema: hangfire; Owner: postgres
--

CREATE TABLE hangfire.hash (
    id bigint NOT NULL,
    key text NOT NULL,
    field text NOT NULL,
    value text,
    expireat timestamp with time zone,
    updatecount integer DEFAULT 0 NOT NULL
);


ALTER TABLE hangfire.hash OWNER TO postgres;

--
-- TOC entry 252 (class 1259 OID 20530)
-- Name: hash_id_seq; Type: SEQUENCE; Schema: hangfire; Owner: postgres
--

CREATE SEQUENCE hangfire.hash_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE hangfire.hash_id_seq OWNER TO postgres;

--
-- TOC entry 6786 (class 0 OID 0)
-- Dependencies: 252
-- Name: hash_id_seq; Type: SEQUENCE OWNED BY; Schema: hangfire; Owner: postgres
--

ALTER SEQUENCE hangfire.hash_id_seq OWNED BY hangfire.hash.id;


--
-- TOC entry 255 (class 1259 OID 20545)
-- Name: job; Type: TABLE; Schema: hangfire; Owner: postgres
--

CREATE TABLE hangfire.job (
    id bigint NOT NULL,
    stateid bigint,
    statename text,
    invocationdata jsonb NOT NULL,
    arguments jsonb NOT NULL,
    createdat timestamp with time zone NOT NULL,
    expireat timestamp with time zone,
    updatecount integer DEFAULT 0 NOT NULL
);


ALTER TABLE hangfire.job OWNER TO postgres;

--
-- TOC entry 254 (class 1259 OID 20544)
-- Name: job_id_seq; Type: SEQUENCE; Schema: hangfire; Owner: postgres
--

CREATE SEQUENCE hangfire.job_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE hangfire.job_id_seq OWNER TO postgres;

--
-- TOC entry 6787 (class 0 OID 0)
-- Dependencies: 254
-- Name: job_id_seq; Type: SEQUENCE OWNED BY; Schema: hangfire; Owner: postgres
--

ALTER SEQUENCE hangfire.job_id_seq OWNED BY hangfire.job.id;


--
-- TOC entry 266 (class 1259 OID 20624)
-- Name: jobparameter; Type: TABLE; Schema: hangfire; Owner: postgres
--

CREATE TABLE hangfire.jobparameter (
    id bigint NOT NULL,
    jobid bigint NOT NULL,
    name text NOT NULL,
    value text,
    updatecount integer DEFAULT 0 NOT NULL
);


ALTER TABLE hangfire.jobparameter OWNER TO postgres;

--
-- TOC entry 265 (class 1259 OID 20623)
-- Name: jobparameter_id_seq; Type: SEQUENCE; Schema: hangfire; Owner: postgres
--

CREATE SEQUENCE hangfire.jobparameter_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE hangfire.jobparameter_id_seq OWNER TO postgres;

--
-- TOC entry 6788 (class 0 OID 0)
-- Dependencies: 265
-- Name: jobparameter_id_seq; Type: SEQUENCE OWNED BY; Schema: hangfire; Owner: postgres
--

ALTER SEQUENCE hangfire.jobparameter_id_seq OWNED BY hangfire.jobparameter.id;


--
-- TOC entry 259 (class 1259 OID 20578)
-- Name: jobqueue; Type: TABLE; Schema: hangfire; Owner: postgres
--

CREATE TABLE hangfire.jobqueue (
    id bigint NOT NULL,
    jobid bigint NOT NULL,
    queue text NOT NULL,
    fetchedat timestamp with time zone,
    updatecount integer DEFAULT 0 NOT NULL
);


ALTER TABLE hangfire.jobqueue OWNER TO postgres;

--
-- TOC entry 258 (class 1259 OID 20577)
-- Name: jobqueue_id_seq; Type: SEQUENCE; Schema: hangfire; Owner: postgres
--

CREATE SEQUENCE hangfire.jobqueue_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE hangfire.jobqueue_id_seq OWNER TO postgres;

--
-- TOC entry 6789 (class 0 OID 0)
-- Dependencies: 258
-- Name: jobqueue_id_seq; Type: SEQUENCE OWNED BY; Schema: hangfire; Owner: postgres
--

ALTER SEQUENCE hangfire.jobqueue_id_seq OWNED BY hangfire.jobqueue.id;


--
-- TOC entry 261 (class 1259 OID 20589)
-- Name: list; Type: TABLE; Schema: hangfire; Owner: postgres
--

CREATE TABLE hangfire.list (
    id bigint NOT NULL,
    key text NOT NULL,
    value text,
    expireat timestamp with time zone,
    updatecount integer DEFAULT 0 NOT NULL
);


ALTER TABLE hangfire.list OWNER TO postgres;

--
-- TOC entry 260 (class 1259 OID 20588)
-- Name: list_id_seq; Type: SEQUENCE; Schema: hangfire; Owner: postgres
--

CREATE SEQUENCE hangfire.list_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE hangfire.list_id_seq OWNER TO postgres;

--
-- TOC entry 6790 (class 0 OID 0)
-- Dependencies: 260
-- Name: list_id_seq; Type: SEQUENCE OWNED BY; Schema: hangfire; Owner: postgres
--

ALTER SEQUENCE hangfire.list_id_seq OWNED BY hangfire.list.id;


--
-- TOC entry 267 (class 1259 OID 20641)
-- Name: lock; Type: TABLE; Schema: hangfire; Owner: postgres
--

CREATE TABLE hangfire.lock (
    resource text NOT NULL,
    updatecount integer DEFAULT 0 NOT NULL,
    acquired timestamp with time zone
);


ALTER TABLE hangfire.lock OWNER TO postgres;

--
-- TOC entry 249 (class 1259 OID 20513)
-- Name: schema; Type: TABLE; Schema: hangfire; Owner: postgres
--

CREATE TABLE hangfire.schema (
    version integer NOT NULL
);


ALTER TABLE hangfire.schema OWNER TO postgres;

--
-- TOC entry 262 (class 1259 OID 20599)
-- Name: server; Type: TABLE; Schema: hangfire; Owner: postgres
--

CREATE TABLE hangfire.server (
    id text NOT NULL,
    data jsonb,
    lastheartbeat timestamp with time zone NOT NULL,
    updatecount integer DEFAULT 0 NOT NULL
);


ALTER TABLE hangfire.server OWNER TO postgres;

--
-- TOC entry 264 (class 1259 OID 20609)
-- Name: set; Type: TABLE; Schema: hangfire; Owner: postgres
--

CREATE TABLE hangfire.set (
    id bigint NOT NULL,
    key text NOT NULL,
    score double precision NOT NULL,
    value text NOT NULL,
    expireat timestamp with time zone,
    updatecount integer DEFAULT 0 NOT NULL
);


ALTER TABLE hangfire.set OWNER TO postgres;

--
-- TOC entry 263 (class 1259 OID 20608)
-- Name: set_id_seq; Type: SEQUENCE; Schema: hangfire; Owner: postgres
--

CREATE SEQUENCE hangfire.set_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE hangfire.set_id_seq OWNER TO postgres;

--
-- TOC entry 6791 (class 0 OID 0)
-- Dependencies: 263
-- Name: set_id_seq; Type: SEQUENCE OWNED BY; Schema: hangfire; Owner: postgres
--

ALTER SEQUENCE hangfire.set_id_seq OWNED BY hangfire.set.id;


--
-- TOC entry 257 (class 1259 OID 20559)
-- Name: state; Type: TABLE; Schema: hangfire; Owner: postgres
--

CREATE TABLE hangfire.state (
    id bigint NOT NULL,
    jobid bigint NOT NULL,
    name text NOT NULL,
    reason text,
    createdat timestamp with time zone NOT NULL,
    data jsonb,
    updatecount integer DEFAULT 0 NOT NULL
);


ALTER TABLE hangfire.state OWNER TO postgres;

--
-- TOC entry 256 (class 1259 OID 20558)
-- Name: state_id_seq; Type: SEQUENCE; Schema: hangfire; Owner: postgres
--

CREATE SEQUENCE hangfire.state_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE hangfire.state_id_seq OWNER TO postgres;

--
-- TOC entry 6792 (class 0 OID 0)
-- Dependencies: 256
-- Name: state_id_seq; Type: SEQUENCE OWNED BY; Schema: hangfire; Owner: postgres
--

ALTER SEQUENCE hangfire.state_id_seq OWNED BY hangfire.state.id;


--
-- TOC entry 343 (class 1259 OID 34915)
-- Name: AbandonedCartRecoveryLogs; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."AbandonedCartRecoveryLogs" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid NOT NULL,
    "AbandonedCartId" uuid NOT NULL,
    "CartId" uuid NOT NULL,
    "PhoneNumber" character varying(20) NOT NULL,
    "FirstMessageSent" boolean DEFAULT false NOT NULL,
    "FirstMessageSentAtUtc" timestamp without time zone,
    "FirstMessageError" character varying(2000),
    "SecondMessageSent" boolean DEFAULT false NOT NULL,
    "SecondMessageSentAtUtc" timestamp without time zone,
    "SecondMessageError" character varying(2000),
    "WorkflowStartedAtUtc" timestamp without time zone DEFAULT now() NOT NULL,
    "WorkflowCompletedAtUtc" timestamp without time zone,
    "IdempotencyKey" character varying(100),
    "CreatedAtUtc" timestamp without time zone DEFAULT now() NOT NULL
);


ALTER TABLE public."AbandonedCartRecoveryLogs" OWNER TO postgres;

--
-- TOC entry 6793 (class 0 OID 0)
-- Dependencies: 343
-- Name: TABLE "AbandonedCartRecoveryLogs"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON TABLE public."AbandonedCartRecoveryLogs" IS 'Tracks all recovery workflow executions for analytics and debugging. One record per n8n workflow run.';


--
-- TOC entry 341 (class 1259 OID 34848)
-- Name: AbandonedCarts; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."AbandonedCarts" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid NOT NULL,
    "CartId" uuid NOT NULL,
    "CustomerId" uuid,
    "PhoneNumber" character varying(20) NOT NULL,
    "Email" character varying(255),
    "CartSnapshotJson" jsonb DEFAULT '{}'::jsonb NOT NULL,
    "LastActivityAtUtc" timestamp without time zone DEFAULT now() NOT NULL,
    "AbandonedAtUtc" timestamp without time zone,
    "RecoveryStatus" character varying(20) DEFAULT 'Active'::character varying NOT NULL,
    "TriggeredAtUtc" timestamp without time zone,
    "RecoveredAtUtc" timestamp without time zone,
    "RecoveredOrderId" uuid,
    "CreatedAtUtc" timestamp without time zone DEFAULT now() NOT NULL,
    "UpdatedAtUtc" timestamp without time zone DEFAULT now() NOT NULL,
    "ExpiredAtUtc" timestamp without time zone,
    "PendingAtUtc" timestamp without time zone,
    "RetryCount" integer DEFAULT 0 NOT NULL,
    "LastErrorMessage" character varying(2000),
    "SessionId" character varying(128),
    "ContactChannel" character varying(32),
    "PhoneVerified" boolean DEFAULT false NOT NULL,
    "EmailVerified" boolean DEFAULT false NOT NULL,
    CONSTRAINT "CK_AbandonedCarts_RecoveryStatus" CHECK ((("RecoveryStatus")::text = ANY ((ARRAY['Active'::character varying, 'Abandoned'::character varying, 'Pending'::character varying, 'Triggered'::character varying, 'Recovered'::character varying, 'Expired'::character varying, 'Failed'::character varying])::text[])))
);


ALTER TABLE public."AbandonedCarts" OWNER TO postgres;

--
-- TOC entry 6794 (class 0 OID 0)
-- Dependencies: 341
-- Name: TABLE "AbandonedCarts"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON TABLE public."AbandonedCarts" IS 'Tracks cart lifecycle from active → abandoned → triggered → recovered/expired. Phase: Smart Abandoned Cart Recovery Engine - Phase 2';


--
-- TOC entry 6795 (class 0 OID 0)
-- Dependencies: 341
-- Name: COLUMN "AbandonedCarts"."CartId"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."AbandonedCarts"."CartId" IS 'Cart identifier from storefront session. Unique per tenant.';


--
-- TOC entry 6796 (class 0 OID 0)
-- Dependencies: 341
-- Name: COLUMN "AbandonedCarts"."PhoneNumber"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."AbandonedCarts"."PhoneNumber" IS 'Customer phone number in E.164 format (required for WhatsApp recovery).';


--
-- TOC entry 6797 (class 0 OID 0)
-- Dependencies: 341
-- Name: COLUMN "AbandonedCarts"."CartSnapshotJson"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."AbandonedCarts"."CartSnapshotJson" IS 'Snapshot of cart contents at last activity. JSON structure: { items: [...], total: 0, currency: "USD" }';


--
-- TOC entry 6798 (class 0 OID 0)
-- Dependencies: 341
-- Name: COLUMN "AbandonedCarts"."LastActivityAtUtc"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."AbandonedCarts"."LastActivityAtUtc" IS 'Last time customer interacted with cart. Used to calculate abandonment threshold.';


--
-- TOC entry 6799 (class 0 OID 0)
-- Dependencies: 341
-- Name: COLUMN "AbandonedCarts"."AbandonedAtUtc"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."AbandonedCarts"."AbandonedAtUtc" IS 'When cart was marked as abandoned. Null while cart is Active.';


--
-- TOC entry 6800 (class 0 OID 0)
-- Dependencies: 341
-- Name: COLUMN "AbandonedCarts"."RecoveryStatus"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."AbandonedCarts"."RecoveryStatus" IS 'Current recovery status: Active, Abandoned, Triggered, Recovered, Expired';


--
-- TOC entry 6801 (class 0 OID 0)
-- Dependencies: 341
-- Name: COLUMN "AbandonedCarts"."TriggeredAtUtc"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."AbandonedCarts"."TriggeredAtUtc" IS 'When recovery workflow was triggered (n8n webhook called). Used for idempotency.';


--
-- TOC entry 6802 (class 0 OID 0)
-- Dependencies: 341
-- Name: COLUMN "AbandonedCarts"."RecoveredAtUtc"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."AbandonedCarts"."RecoveredAtUtc" IS 'When cart was recovered (order completed). Used for success metrics.';


--
-- TOC entry 6803 (class 0 OID 0)
-- Dependencies: 341
-- Name: COLUMN "AbandonedCarts"."RecoveredOrderId"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."AbandonedCarts"."RecoveredOrderId" IS 'Order ID if cart was recovered. Links abandoned cart to successful conversion.';


--
-- TOC entry 297 (class 1259 OID 23232)
-- Name: AccountBalances; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."AccountBalances" (
    "BalanceId" bigint NOT NULL,
    "AccountId" bigint NOT NULL,
    "PeriodKey" character varying(7) NOT NULL,
    "OpeningDebit" numeric(18,4) NOT NULL,
    "OpeningCredit" numeric(18,4) NOT NULL,
    "PeriodDebit" numeric(18,4) NOT NULL,
    "PeriodCredit" numeric(18,4) NOT NULL,
    "ClosingDebit" numeric(18,4) NOT NULL,
    "ClosingCredit" numeric(18,4) NOT NULL,
    "LastUpdatedAt" timestamp with time zone NOT NULL
);


ALTER TABLE public."AccountBalances" OWNER TO postgres;

--
-- TOC entry 296 (class 1259 OID 23231)
-- Name: AccountBalances_BalanceId_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public."AccountBalances" ALTER COLUMN "BalanceId" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."AccountBalances_BalanceId_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 276 (class 1259 OID 21190)
-- Name: AccountLedgers; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."AccountLedgers" (
    "Id" uuid NOT NULL,
    "PartyType" integer NOT NULL,
    "PartyId" uuid NOT NULL,
    "TransactionDate" timestamp with time zone NOT NULL,
    "TransactionType" integer NOT NULL,
    "DocumentNumber" text NOT NULL,
    "DebitAmount" numeric NOT NULL,
    "CreditAmount" numeric NOT NULL,
    "RunningBalance" numeric NOT NULL,
    "ReferenceId" uuid,
    "Notes" text,
    "UserId" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL
);


ALTER TABLE public."AccountLedgers" OWNER TO postgres;

--
-- TOC entry 299 (class 1259 OID 23253)
-- Name: AccountMappings; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."AccountMappings" (
    "MappingId" bigint NOT NULL,
    "MappingKey" character varying(100) NOT NULL,
    "AccountId" bigint NOT NULL,
    "Description" character varying(500),
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone
);


ALTER TABLE public."AccountMappings" OWNER TO postgres;

--
-- TOC entry 298 (class 1259 OID 23252)
-- Name: AccountMappings_MappingId_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public."AccountMappings" ALTER COLUMN "MappingId" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."AccountMappings_MappingId_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 351 (class 1259 OID 35259)
-- Name: AccountingOutboxMessages; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."AccountingOutboxMessages" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid NOT NULL,
    "EventType" character varying(50) NOT NULL,
    "BusinessEntityId" uuid NOT NULL,
    "EntityTypeName" character varying(255),
    "PayloadJson" text NOT NULL,
    "IdempotencyKey" character varying(255) NOT NULL,
    "OccurredAtUtc" timestamp without time zone NOT NULL,
    "ProcessedAtUtc" timestamp without time zone,
    "JournalId" uuid,
    "AttemptCount" integer DEFAULT 0 NOT NULL,
    "LastError" character varying(2000),
    "NextRetryAtUtc" timestamp without time zone,
    "IsFailed" boolean DEFAULT false NOT NULL,
    "CreatedAt" timestamp without time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp without time zone DEFAULT now(),
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "CorrelationId" character varying(100),
    "AggregateVersion" integer,
    "ClaimedUntilUtc" timestamp with time zone,
    "SourceType" character varying(50) NOT NULL,
    CONSTRAINT "CK_AccountingOutbox_OrderFinancialEvents_AggregateVersion" CHECK (((("EventType")::text <> ALL ((ARRAY['OnlinePayment'::character varying, 'CodDelivered'::character varying, 'WalletApproved'::character varying, 'EcommerceRefund'::character varying])::text[])) OR (("AggregateVersion" IS NOT NULL) AND ("AggregateVersion" > 0))))
);


ALTER TABLE public."AccountingOutboxMessages" OWNER TO postgres;

--
-- TOC entry 293 (class 1259 OID 23186)
-- Name: Accounts; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."Accounts" (
    "AccountId" bigint NOT NULL,
    "Code" character varying(50) NOT NULL,
    "Name" character varying(200) NOT NULL,
    "NameAr" character varying(200),
    "ParentAccountId" bigint,
    "Level" integer NOT NULL,
    "Type" character varying(50) NOT NULL,
    "IsActive" boolean NOT NULL,
    "IsSystem" boolean NOT NULL,
    "NormalBalance" character varying(10) NOT NULL,
    "Note" character varying(500),
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" character varying(100),
    "UpdatedAt" timestamp with time zone,
    "IsDeleted" boolean NOT NULL
);


ALTER TABLE public."Accounts" OWNER TO postgres;

--
-- TOC entry 292 (class 1259 OID 23185)
-- Name: Accounts_AccountId_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public."Accounts" ALTER COLUMN "AccountId" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."Accounts_AccountId_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 368 (class 1259 OID 35921)
-- Name: AffiliateAccounts; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."AffiliateAccounts" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid,
    "UserId" uuid NOT NULL,
    "ReferralCode" character varying(12) NOT NULL,
    "Status" integer DEFAULT 0 NOT NULL,
    "DisplayName" character varying(200) DEFAULT ''::character varying NOT NULL,
    "ActivatedAtUtc" timestamp with time zone,
    "SuspendedAtUtc" timestamp with time zone,
    "SuspensionReason" character varying(500),
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "PayoutWalletNumber" text,
    "PayoutWalletType" text,
    "PayoutWalletUpdatedAtUtc" timestamp with time zone,
    CONSTRAINT "CK_AffiliateAccounts_Status" CHECK (("Status" = ANY (ARRAY[0, 1, 2])))
);


ALTER TABLE public."AffiliateAccounts" OWNER TO postgres;

--
-- TOC entry 6804 (class 0 OID 0)
-- Dependencies: 368
-- Name: TABLE "AffiliateAccounts"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON TABLE public."AffiliateAccounts" IS 'AF-16B: Payout wallet numbers normalized on [DATE]';


--
-- TOC entry 6805 (class 0 OID 0)
-- Dependencies: 368
-- Name: COLUMN "AffiliateAccounts"."TenantId"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."AffiliateAccounts"."TenantId" IS 'AF-19B: DEPRECATED - Always NULL. Affiliates are global entities. Column kept for rollback safety only.';


--
-- TOC entry 6806 (class 0 OID 0)
-- Dependencies: 368
-- Name: COLUMN "AffiliateAccounts"."PayoutWalletNumber"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."AffiliateAccounts"."PayoutWalletNumber" IS 'Affiliate payout wallet number (Vodafone Cash, Bank Account, etc.)';


--
-- TOC entry 6807 (class 0 OID 0)
-- Dependencies: 368
-- Name: COLUMN "AffiliateAccounts"."PayoutWalletType"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."AffiliateAccounts"."PayoutWalletType" IS 'Wallet type: VodafoneCash, OrangeMoney, EtisalatCash, Bank, etc.';


--
-- TOC entry 6808 (class 0 OID 0)
-- Dependencies: 368
-- Name: COLUMN "AffiliateAccounts"."PayoutWalletUpdatedAtUtc"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."AffiliateAccounts"."PayoutWalletUpdatedAtUtc" IS 'Last time wallet was updated by affiliate';


--
-- TOC entry 370 (class 1259 OID 35988)
-- Name: AffiliateAttributions; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."AffiliateAttributions" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "AffiliateAccountId" uuid NOT NULL,
    "ReferralCode" character varying(12) NOT NULL,
    "Source" integer DEFAULT 0 NOT NULL,
    "AttributedAtUtc" timestamp with time zone DEFAULT now() NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "PromotionLinkId" uuid,
    CONSTRAINT "CK_AffiliateAttributions_Source" CHECK (("Source" = ANY (ARRAY[0, 1, 2])))
);


ALTER TABLE public."AffiliateAttributions" OWNER TO postgres;

--
-- TOC entry 376 (class 1259 OID 36336)
-- Name: AffiliateBalanceLedger; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."AffiliateBalanceLedger" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "StoreTenantId" uuid NOT NULL,
    "AffiliateAccountId" uuid NOT NULL,
    "EntryType" integer NOT NULL,
    "Amount" numeric(18,4) NOT NULL,
    "ReferenceId" uuid,
    "Description" text,
    "IdempotencyKey" character varying(255) NOT NULL,
    "Currency" character varying(3) DEFAULT 'EGP'::character varying NOT NULL,
    "RecordedAtUtc" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    CONSTRAINT "CK_AffiliateBalanceLedger_Amount" CHECK (("Amount" <> (0)::numeric)),
    CONSTRAINT "CK_AffiliateBalanceLedger_EntryType" CHECK (("EntryType" = ANY (ARRAY[1, 2, 3, 4]))),
    CONSTRAINT "CK_AffiliateBalanceLedger_WithdrawalNegative" CHECK ((("EntryType" <> 1) OR ("Amount" < (0)::numeric)))
);


ALTER TABLE public."AffiliateBalanceLedger" OWNER TO postgres;

--
-- TOC entry 374 (class 1259 OID 36259)
-- Name: AffiliateCategories; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."AffiliateCategories" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "Name" character varying(100) NOT NULL,
    "Slug" character varying(100) NOT NULL,
    "ParentId" uuid,
    "Level" integer NOT NULL,
    "IsActive" boolean DEFAULT true NOT NULL,
    "DisplayOrder" integer DEFAULT 0 NOT NULL,
    "CreatedAt" timestamp without time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp without time zone,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    CONSTRAINT "CK_AffiliateCategories_Level" CHECK (("Level" = ANY (ARRAY[1, 2]))),
    CONSTRAINT "CK_AffiliateCategories_Level1_NoParent" CHECK (((("Level" = 1) AND ("ParentId" IS NULL)) OR (("Level" = 2) AND ("ParentId" IS NOT NULL))))
);


ALTER TABLE public."AffiliateCategories" OWNER TO postgres;

--
-- TOC entry 371 (class 1259 OID 36027)
-- Name: AffiliateCommissionLedger; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."AffiliateCommissionLedger" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid,
    "AttributionId" uuid NOT NULL,
    "AffiliateAccountId" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "OrderItemId" uuid NOT NULL,
    "BaseAmount" numeric(18,4) NOT NULL,
    "CommissionRate" numeric(8,6) NOT NULL,
    "CommissionAmount" numeric(18,4) NOT NULL,
    "Currency" character varying(3) DEFAULT 'USD'::character varying NOT NULL,
    "EntryType" integer NOT NULL,
    "OriginalEntryId" uuid,
    "IdempotencyKey" character varying(255) NOT NULL,
    "RecordedAtUtc" timestamp with time zone DEFAULT now() NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "CreatedBy" uuid,
    "StoreTenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL,
    CONSTRAINT "CK_AffiliateCommLedger_Amount" CHECK (("CommissionAmount" > (0)::numeric)),
    CONSTRAINT "CK_AffiliateCommLedger_BaseAmount" CHECK (("BaseAmount" > (0)::numeric)),
    CONSTRAINT "CK_AffiliateCommLedger_EntryType" CHECK (("EntryType" = ANY (ARRAY[0, 1, 2, 3]))),
    CONSTRAINT "CK_AffiliateCommLedger_Rate" CHECK ((("CommissionRate" > (0)::numeric) AND ("CommissionRate" <= 0.50))),
    CONSTRAINT "CK_AffiliateCommLedger_ReversalRef" CHECK (((("EntryType" = ANY (ARRAY[1, 2, 3])) AND ("OriginalEntryId" IS NOT NULL)) OR (("EntryType" = ANY (ARRAY[0, 4])) AND ("OriginalEntryId" IS NULL))))
);


ALTER TABLE public."AffiliateCommissionLedger" OWNER TO postgres;

--
-- TOC entry 6809 (class 0 OID 0)
-- Dependencies: 371
-- Name: COLUMN "AffiliateCommissionLedger"."TenantId"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."AffiliateCommissionLedger"."TenantId" IS 'AF-19B: DEPRECATED - Always NULL. Use StoreTenantId instead. Column kept for rollback safety only.';


--
-- TOC entry 375 (class 1259 OID 36308)
-- Name: AffiliatePayoutRequests; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."AffiliatePayoutRequests" (
    "Id" uuid NOT NULL,
    "StoreTenantId" uuid NOT NULL,
    "AffiliateAccountId" uuid NOT NULL,
    "RequestedAmount" numeric(18,4) NOT NULL,
    "ApprovedAmount" numeric(18,4),
    "Status" integer DEFAULT 1 NOT NULL,
    "RequestedAtUtc" timestamp with time zone NOT NULL,
    "ApprovedAtUtc" timestamp with time zone,
    "RejectedAtUtc" timestamp with time zone,
    "PaidAtUtc" timestamp with time zone,
    "AdminNotes" character varying(1000),
    "PaymentReference" character varying(500),
    "Currency" character varying(3) DEFAULT 'EGP'::character varying NOT NULL,
    "RemainingAmount" numeric(18,4) DEFAULT 0 NOT NULL
);


ALTER TABLE public."AffiliatePayoutRequests" OWNER TO postgres;

--
-- TOC entry 369 (class 1259 OID 35957)
-- Name: AffiliateProductPolicies; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."AffiliateProductPolicies" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "CommissionRate" numeric(8,6) NOT NULL,
    "IsEnabled" boolean DEFAULT true NOT NULL,
    "MaxCommissionPerItem" numeric(18,4),
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "TemplateType" text DEFAULT ''::text NOT NULL,
    "AffiliateCategoryId" uuid,
    CONSTRAINT "CK_AffiliateProductPolicies_EnabledRequiresCategory" CHECK ((NOT (("IsEnabled" = true) AND ("AffiliateCategoryId" IS NULL)))),
    CONSTRAINT "CK_AffiliateProductPolicies_MaxCap" CHECK ((("MaxCommissionPerItem" IS NULL) OR ("MaxCommissionPerItem" > (0)::numeric))),
    CONSTRAINT "CK_AffiliateProductPolicies_Rate" CHECK ((("CommissionRate" >= 0.0001) AND ("CommissionRate" <= 0.50)))
);


ALTER TABLE public."AffiliateProductPolicies" OWNER TO postgres;

--
-- TOC entry 373 (class 1259 OID 36167)
-- Name: AffiliatePromotionLinks; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."AffiliatePromotionLinks" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid NOT NULL,
    "AffiliateUserId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "ReferralCode" character varying(16) NOT NULL,
    "AffiliateAccountId" uuid,
    "IsActive" boolean DEFAULT true NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."AffiliatePromotionLinks" OWNER TO postgres;

--
-- TOC entry 372 (class 1259 OID 36104)
-- Name: AffiliateUsers; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."AffiliateUsers" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Email" character varying(255) NOT NULL,
    "PasswordHash" character varying(500) NOT NULL,
    "AffiliateAccountId" uuid NOT NULL,
    "EmailVerified" boolean DEFAULT false NOT NULL,
    "RefreshToken" character varying(500),
    "RefreshTokenExpiryTime" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL
);


ALTER TABLE public."AffiliateUsers" OWNER TO postgres;

--
-- TOC entry 333 (class 1259 OID 34506)
-- Name: AnalyticsAlertRules; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."AnalyticsAlertRules" (
    "RuleId" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Description" character varying(500),
    "Metric" integer NOT NULL,
    "Operator" integer NOT NULL,
    "Threshold" numeric(18,4) NOT NULL,
    "WindowMinutes" integer NOT NULL,
    "Severity" integer DEFAULT 2 NOT NULL,
    "IsEnabled" boolean DEFAULT true NOT NULL,
    "CooldownMinutes" integer DEFAULT 15 NOT NULL,
    "CreatedAtUtc" timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."AnalyticsAlertRules" OWNER TO postgres;

--
-- TOC entry 291 (class 1259 OID 23054)
-- Name: AuditLogs; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."AuditLogs" (
    "LogId" bigint NOT NULL,
    "TimestampUtc" timestamp with time zone NOT NULL,
    "UserId" uuid,
    "UserNameSnapshot" character varying(150),
    "ActionType" character varying(50) NOT NULL,
    "EntityType" character varying(50) NOT NULL,
    "EntityId" character varying(50) NOT NULL,
    "Description" text NOT NULL,
    "BeforeDataJson" text,
    "AfterDataJson" text,
    "IpAddress" character varying(50),
    "UserAgent" character varying(500),
    "ReferenceNumber" character varying(100),
    "Source" character varying(50),
    "SuccessFlag" boolean NOT NULL,
    "TenantId" uuid
);


ALTER TABLE public."AuditLogs" OWNER TO postgres;

--
-- TOC entry 290 (class 1259 OID 23053)
-- Name: AuditLogs_LogId_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public."AuditLogs" ALTER COLUMN "LogId" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."AuditLogs_LogId_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 283 (class 1259 OID 21370)
-- Name: BankTransactions; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."BankTransactions" (
    "Id" uuid NOT NULL,
    "BankId" uuid NOT NULL,
    "TransactionDate" timestamp with time zone NOT NULL,
    "TransactionType" integer NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Description" text,
    "ReferenceNumber" text,
    "Notes" text,
    "IsOpeningBalance" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL
);


ALTER TABLE public."BankTransactions" OWNER TO postgres;

--
-- TOC entry 282 (class 1259 OID 21357)
-- Name: Banks; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."Banks" (
    "Id" uuid NOT NULL,
    "BankName" text NOT NULL,
    "BranchName" text,
    "AccountNumber" text,
    "IBAN" text,
    "Notes" text,
    "IsActive" boolean NOT NULL,
    "OpeningBalanceAmount" numeric(18,2),
    "OpeningBalanceDate" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL
);


ALTER TABLE public."Banks" OWNER TO postgres;

--
-- TOC entry 334 (class 1259 OID 34532)
-- Name: CampaignCosts; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CampaignCosts" (
    "CampaignCostId" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Source" character varying(100) NOT NULL,
    "Medium" character varying(100) NOT NULL,
    "Campaign" character varying(200) NOT NULL,
    "PeriodStartUtc" timestamp with time zone NOT NULL,
    "PeriodEndUtc" timestamp with time zone NOT NULL,
    "CostAmount" numeric(18,2) NOT NULL,
    "Currency" character varying(3) NOT NULL,
    "Notes" character varying(500),
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."CampaignCosts" OWNER TO postgres;

--
-- TOC entry 470 (class 1259 OID 2287698)
-- Name: CanonicalIdentityLinks; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CanonicalIdentityLinks" (
    "Id" uuid NOT NULL,
    "CanonicalIdentityId" uuid NOT NULL,
    "SubjectType" integer NOT NULL,
    "SubjectId" uuid NOT NULL,
    "TenantId" uuid,
    "SourceSystem" character varying(64) NOT NULL,
    "DisplayKey" character varying(255),
    "LinkedAtUtc" timestamp with time zone NOT NULL,
    "RevokedAtUtc" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."CanonicalIdentityLinks" OWNER TO postgres;

--
-- TOC entry 335 (class 1259 OID 34555)
-- Name: CarrierConfigurations; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CarrierConfigurations" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "CarrierCode" character varying(50) NOT NULL,
    "DisplayName" character varying(100) NOT NULL,
    "IsEnabled" boolean DEFAULT false NOT NULL,
    "IsDefault" boolean DEFAULT false NOT NULL,
    "ApiKey" character varying(500),
    "ApiSecret" character varying(500),
    "AccountId" character varying(100),
    "ApiUrl" character varying(500),
    "UseSandbox" boolean DEFAULT false NOT NULL,
    "SupportsCod" boolean DEFAULT false NOT NULL,
    "SupportsTracking" boolean DEFAULT true NOT NULL,
    "SupportsWebhooks" boolean DEFAULT false NOT NULL,
    "SupportsLabels" boolean DEFAULT false NOT NULL,
    "SupportsQuotes" boolean DEFAULT true NOT NULL,
    "SupportsCancellation" boolean DEFAULT true NOT NULL,
    "Configuration" text,
    "WebhookUrl" character varying(500),
    "WebhookSecret" character varying(256),
    "PickupAddress" character varying(500),
    "PickupCity" character varying(100),
    "PickupCountry" character varying(10),
    "PickupPhone" character varying(50),
    "PickupContactName" character varying(100),
    "AvailableServiceLevels" text,
    "DefaultServiceLevel" character varying(50) DEFAULT 'standard'::character varying NOT NULL,
    "UseCarrierRates" boolean DEFAULT true NOT NULL,
    "FixedShippingCost" numeric(18,2),
    "MarkupPercentage" numeric(5,2) DEFAULT 0 NOT NULL,
    "FreeShippingThreshold" numeric(18,2),
    "SortOrder" integer DEFAULT 0 NOT NULL,
    "Notes" character varying(1000),
    "LastValidatedAtUtc" timestamp with time zone,
    "LastValidationSucceeded" boolean,
    "LastValidationError" character varying(1000),
    "CreatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "CreatedBy" uuid,
    "UpdatedBy" uuid
);


ALTER TABLE public."CarrierConfigurations" OWNER TO postgres;

--
-- TOC entry 272 (class 1259 OID 21047)
-- Name: CashboxTransactions; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CashboxTransactions" (
    "Id" uuid NOT NULL,
    "CashboxId" uuid NOT NULL,
    "TransactionDate" timestamp with time zone NOT NULL,
    "TransactionType" integer NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "BalanceAfter" numeric(18,2) NOT NULL,
    "PaymentMethod" integer,
    "ReferenceId" uuid,
    "ReferenceNumber" character varying(100),
    "Description" character varying(500) NOT NULL,
    "UserId" uuid,
    "Notes" character varying(1000),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "IsOpeningBalance" boolean DEFAULT false NOT NULL,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL
);


ALTER TABLE public."CashboxTransactions" OWNER TO postgres;

--
-- TOC entry 271 (class 1259 OID 21021)
-- Name: Cashboxes; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."Cashboxes" (
    "Id" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "WarehouseId" uuid,
    "CurrentBalance" numeric(18,2) NOT NULL,
    "IsActive" boolean NOT NULL,
    "OpenedAt" timestamp with time zone,
    "OpenedByUserId" uuid,
    "OpeningBalance" numeric(18,2) NOT NULL,
    "ExpectedBalance" numeric(18,2) NOT NULL,
    "Description" character varying(500),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "OpeningBalanceAmount" numeric,
    "OpeningBalanceDate" timestamp with time zone,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL
);


ALTER TABLE public."Cashboxes" OWNER TO postgres;

--
-- TOC entry 229 (class 1259 OID 20012)
-- Name: Categories; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."Categories" (
    "Id" uuid NOT NULL,
    "Name" text NOT NULL,
    "Description" text,
    "ParentCategoryId" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL
);


ALTER TABLE public."Categories" OWNER TO postgres;

--
-- TOC entry 365 (class 1259 OID 35773)
-- Name: ChatAuditLog; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."ChatAuditLog" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "MessageId" uuid NOT NULL,
    "ServiceRequestId" uuid NOT NULL,
    "SenderUserId" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Action" text NOT NULL,
    "Timestamp" timestamp without time zone DEFAULT now() NOT NULL,
    "Metadata" jsonb
);


ALTER TABLE public."ChatAuditLog" OWNER TO postgres;

--
-- TOC entry 377 (class 1259 OID 36389)
-- Name: CheckoutSessions; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CheckoutSessions" (
    "Id" uuid NOT NULL,
    "CartId" uuid NOT NULL,
    "SessionId" character varying(100),
    "OnlineCustomerId" uuid,
    "CustomerName" character varying(200),
    "Phone" character varying(20),
    "Email" character varying(255),
    "City" character varying(100),
    "Area" character varying(100),
    "Address" character varying(500),
    "Notes" character varying(500),
    "ItemsSnapshotJson" text NOT NULL,
    "CartTotal" numeric(18,4),
    "Currency" character varying(3),
    "ShippingMethodId" uuid,
    "PaymentMethodCode" character varying(50),
    "Status" integer NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "LastActivityAtUtc" timestamp with time zone NOT NULL,
    "AbandonedAtUtc" timestamp with time zone,
    "ConvertedAtUtc" timestamp with time zone,
    "ConvertedOrderId" uuid,
    "ConvertedOrderNumber" character varying(50),
    "TenantId" uuid NOT NULL,
    "RecoveryToken" character varying(64) DEFAULT ''::character varying NOT NULL,
    "DeviceType" character varying(20),
    "Source" character varying(50),
    "FirstActivityAtUtc" timestamp with time zone,
    "Country" text,
    "WalletType" text,
    "WalletPhoneNumber" text,
    "ProofImageUrl" text,
    "InvalidatedAtUtc" timestamp without time zone
);


ALTER TABLE public."CheckoutSessions" OWNER TO postgres;

--
-- TOC entry 6810 (class 0 OID 0)
-- Dependencies: 377
-- Name: COLUMN "CheckoutSessions"."Country"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."CheckoutSessions"."Country" IS 'Country from shipping address (ISO 3166-1 alpha-2 code or full name). Used for shipping/tax calculations and recovery.';


--
-- TOC entry 6811 (class 0 OID 0)
-- Dependencies: 377
-- Name: COLUMN "CheckoutSessions"."InvalidatedAtUtc"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."CheckoutSessions"."InvalidatedAtUtc" IS 'Timestamp when this session was invalidated during reconciliation (duplicate resolution). NULL if never invalidated. Used to exclude already-invalidated duplicates from recoverable session queries.';


--
-- TOC entry 441 (class 1259 OID 38350)
-- Name: CmsPages; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CmsPages" (
    "Id" uuid NOT NULL,
    "PageGroupId" uuid NOT NULL,
    "TenantId" uuid,
    "Slug" character varying(160) NOT NULL,
    "Title" character varying(200) NOT NULL,
    "Content" text NOT NULL,
    "ContentType" character varying(20) NOT NULL,
    "Version" integer DEFAULT 1 NOT NULL,
    "SchemaVersion" integer DEFAULT 1 NOT NULL,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "IsDraft" boolean DEFAULT false NOT NULL,
    "IsPublished" boolean DEFAULT false NOT NULL,
    "PublishedAt" timestamp with time zone,
    "MetaTitle" character varying(200),
    "MetaDescription" character varying(320),
    "MetaKeywords" character varying(500),
    "OpenGraphImageUrl" character varying(1000),
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT "CK_CmsPages_ContentType" CHECK ((("ContentType")::text = ANY ((ARRAY['RichText'::character varying, 'Blocks'::character varying])::text[]))),
    CONSTRAINT "CK_CmsPages_PublishedAt" CHECK (((NOT "IsPublished") OR ("PublishedAt" IS NOT NULL))),
    CONSTRAINT "CK_CmsPages_SchemaVersion" CHECK (("SchemaVersion" > 0)),
    CONSTRAINT "CK_CmsPages_State" CHECK ((NOT ("IsDraft" AND "IsPublished"))),
    CONSTRAINT "CK_CmsPages_Version" CHECK (("Version" > 0))
);


ALTER TABLE public."CmsPages" OWNER TO postgres;

--
-- TOC entry 352 (class 1259 OID 35288)
-- Name: CommerceAccountingAudit; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceAccountingAudit" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid NOT NULL,
    "BusinessType" character varying(50) NOT NULL,
    "BusinessId" uuid NOT NULL,
    "BusinessReference" character varying(100),
    "JournalId" uuid NOT NULL,
    "JournalNumber" character varying(50),
    "OperationType" character varying(50) NOT NULL,
    "Amount" numeric(18,4) NOT NULL,
    "PostedAtUtc" timestamp without time zone NOT NULL,
    "IdempotencyKey" character varying(255),
    "WasIdempotent" boolean DEFAULT false NOT NULL,
    "CreatedAt" timestamp without time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp without time zone DEFAULT now(),
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "CreatedBy" uuid,
    "UpdatedBy" uuid
);


ALTER TABLE public."CommerceAccountingAudit" OWNER TO postgres;

--
-- TOC entry 401 (class 1259 OID 37023)
-- Name: CommerceHubCarts; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubCarts" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid NOT NULL,
    "OnlineCustomerId" uuid NOT NULL,
    "ItemsJson" text DEFAULT '[]'::text NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "Version" integer DEFAULT 1 NOT NULL,
    "IsLocked" boolean DEFAULT false NOT NULL,
    "LockedAtUtc" timestamp with time zone,
    "CommerceHubCustomerId" uuid,
    CONSTRAINT "CK_CommerceHubCarts_CustomerAuthorityBoundary" CHECK ((("CommerceHubCustomerId" IS NOT NULL) AND ("CommerceHubCustomerId" <> '00000000-0000-0000-0000-000000000000'::uuid) AND ("OnlineCustomerId" IS NOT NULL) AND ("OnlineCustomerId" = "CommerceHubCustomerId")))
);


ALTER TABLE public."CommerceHubCarts" OWNER TO postgres;

--
-- TOC entry 386 (class 1259 OID 36708)
-- Name: CommerceHubCategories; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubCategories" (
    "Id" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "NameAr" character varying(200),
    "Slug" character varying(200) NOT NULL,
    "ParentId" uuid,
    "Level" integer NOT NULL,
    "IconEmoji" character varying(10),
    "IsActive" boolean DEFAULT true NOT NULL,
    "SortOrder" integer DEFAULT 0 NOT NULL,
    "CreatedAtUtc" timestamp with time zone CONSTRAINT "CommerceHubCategories_CreatedAt_not_null" NOT NULL,
    "UpdatedAtUtc" timestamp with time zone
);


ALTER TABLE public."CommerceHubCategories" OWNER TO postgres;

--
-- TOC entry 387 (class 1259 OID 36729)
-- Name: CommerceHubCategoryAttributes; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubCategoryAttributes" (
    "Id" uuid NOT NULL,
    "CategoryId" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "NameAr" character varying(200),
    "Type" character varying(20) NOT NULL,
    "IsRequired" boolean NOT NULL,
    "OptionsJson" text,
    "SortOrder" integer DEFAULT 0 NOT NULL,
    "IsFilterable" boolean DEFAULT true NOT NULL,
    "IsSearchable" boolean DEFAULT true NOT NULL
);


ALTER TABLE public."CommerceHubCategoryAttributes" OWNER TO postgres;

--
-- TOC entry 402 (class 1259 OID 37045)
-- Name: CommerceHubCheckoutIdempotency; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubCheckoutIdempotency" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "Key" character varying(100) NOT NULL,
    "TenantId" uuid,
    "OnlineCustomerId" uuid,
    "ResponseJson" text,
    "CreatedAtUtc" timestamp with time zone DEFAULT now() NOT NULL,
    "RequestHash" character varying(64) NOT NULL,
    "CommerceHubCustomerId" uuid,
    CONSTRAINT "CHK_CommerceHubCheckoutIdempotency_RequestHash_NotEmpty" CHECK ((length(TRIM(BOTH FROM "RequestHash")) > 0))
);


ALTER TABLE public."CommerceHubCheckoutIdempotency" OWNER TO postgres;

--
-- TOC entry 419 (class 1259 OID 37583)
-- Name: CommerceHubCustomerPayoutDetails; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubCustomerPayoutDetails" (
    "Id" uuid NOT NULL,
    "CustomerId" uuid NOT NULL,
    "Method" integer NOT NULL,
    "AccountIdentifier" character varying(500) NOT NULL,
    "AccountHolderName" character varying(200),
    "IsVerified" boolean DEFAULT false NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."CommerceHubCustomerPayoutDetails" OWNER TO postgres;

--
-- TOC entry 416 (class 1259 OID 37453)
-- Name: CommerceHubCustomers; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubCustomers" (
    "Id" uuid NOT NULL,
    "Email" character varying(255) NOT NULL,
    "PasswordHash" character varying(500),
    "Name" character varying(200) NOT NULL,
    "PhoneNumber" character varying(50),
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."CommerceHubCustomers" OWNER TO postgres;

--
-- TOC entry 438 (class 1259 OID 38197)
-- Name: CommerceHubDailyFinancialAggregates; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubDailyFinancialAggregates" (
    "DateUtc" date NOT NULL,
    "CurrencyCode" character varying(10) NOT NULL,
    "BaseCurrencyCode" character varying(10) DEFAULT 'EGP'::character varying NOT NULL,
    "ExchangeRateToBase" numeric(18,8),
    "GrossSales" numeric(18,2) DEFAULT 0 NOT NULL,
    "GrossCommission" numeric(18,2) DEFAULT 0 NOT NULL,
    "PromoCost" numeric(18,2) DEFAULT 0 NOT NULL,
    "PromoReversalAmount" numeric(18,2) DEFAULT 0 CONSTRAINT "CommerceHubDailyFinancialAggregate_PromoReversalAmount_not_null" NOT NULL,
    "RefundAmount" numeric(18,2) DEFAULT 0 NOT NULL,
    "RefundImpactOnRevenue" numeric(18,2) DEFAULT 0 CONSTRAINT "CommerceHubDailyFinancialAggrega_RefundImpactOnRevenue_not_null" NOT NULL,
    "NetRevenue" numeric(18,2) DEFAULT 0 NOT NULL,
    "GrossSalesBase" numeric(18,2),
    "GrossCommissionBase" numeric(18,2),
    "PromoCostBase" numeric(18,2),
    "PromoReversalAmountBase" numeric(18,2),
    "RefundAmountBase" numeric(18,2),
    "RefundImpactOnRevenueBase" numeric(18,2),
    "NetRevenueBase" numeric(18,2),
    "OrdersCount" integer DEFAULT 0 NOT NULL,
    "RefundCount" integer DEFAULT 0 NOT NULL,
    "RefundedOrdersCount" integer DEFAULT 0 CONSTRAINT "CommerceHubDailyFinancialAggregate_RefundedOrdersCount_not_null" NOT NULL,
    "AggregatedThroughUtc" timestamp with time zone,
    "CalculatedAtUtc" timestamp with time zone DEFAULT now() NOT NULL,
    "IsValid" boolean DEFAULT true NOT NULL,
    "LastValidatedAtUtc" timestamp with time zone,
    "ValidationError" text,
    CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_GrossCommission" CHECK (("GrossCommission" >= (0)::numeric)),
    CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_GrossSales" CHECK (("GrossSales" >= (0)::numeric)),
    CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_IsValid" CHECK (("IsValid" = ANY (ARRAY[true, false]))),
    CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_OrdersCount" CHECK (("OrdersCount" >= 0)),
    CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_PromoCost" CHECK (("PromoCost" >= (0)::numeric)),
    CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_PromoReversalAmount" CHECK (("PromoReversalAmount" >= (0)::numeric)),
    CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_RefundAmount" CHECK (("RefundAmount" >= (0)::numeric)),
    CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_RefundCount" CHECK (("RefundCount" >= 0)),
    CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_RefundImpactOnRevenue" CHECK (("RefundImpactOnRevenue" >= (0)::numeric)),
    CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_RefundedOrdersCount" CHECK (("RefundedOrdersCount" >= 0))
);


ALTER TABLE public."CommerceHubDailyFinancialAggregates" OWNER TO postgres;

--
-- TOC entry 410 (class 1259 OID 37234)
-- Name: CommerceHubFinancialAuditLogs; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubFinancialAuditLogs" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "MarketplaceOrderId" uuid,
    "SubOrderId" uuid,
    "PaymentId" uuid,
    "EarningId" uuid,
    "Type" text DEFAULT ''::text NOT NULL,
    "Debit" numeric(18,4) DEFAULT 0 NOT NULL,
    "Credit" numeric(18,4) DEFAULT 0 NOT NULL,
    "Currency" character varying(10) DEFAULT 'EGP'::character varying NOT NULL,
    "Description" text DEFAULT ''::text NOT NULL,
    "CorrelationId" text,
    "CreatedAtUtc" timestamp with time zone DEFAULT now() NOT NULL,
    "TenantId" uuid,
    "PayoutId" uuid,
    "RefundId" uuid,
    "IsSystemGenerated" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."CommerceHubFinancialAuditLogs" OWNER TO postgres;

--
-- TOC entry 431 (class 1259 OID 37986)
-- Name: CommerceHubFinancialGuardLocks; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubFinancialGuardLocks" (
    "Id" uuid NOT NULL,
    "Scope" character varying(100) NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL
);


ALTER TABLE public."CommerceHubFinancialGuardLocks" OWNER TO postgres;

--
-- TOC entry 432 (class 1259 OID 37995)
-- Name: CommerceHubFinancialOperationAudits; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubFinancialOperationAudits" (
    "Id" uuid NOT NULL,
    "VendorId" uuid NOT NULL,
    "ActorId" uuid,
    "ActorType" character varying(50) NOT NULL,
    "OperationType" character varying(50) NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Currency" character varying(10) DEFAULT 'EGP'::character varying NOT NULL,
    "BeforePlatformCash" numeric(18,2) NOT NULL,
    "BeforeVendorPayable" numeric(18,2) CONSTRAINT "CommerceHubFinancialOperationAudit_BeforeVendorPayable_not_null" NOT NULL,
    "BeforeVendorDebt" numeric(18,2) NOT NULL,
    "AfterPlatformCash" numeric(18,2) NOT NULL,
    "AfterVendorPayable" numeric(18,2) NOT NULL,
    "AfterVendorDebt" numeric(18,2) NOT NULL,
    "CorrelationId" character varying(200) NOT NULL,
    "IdempotencyKey" character varying(200),
    "ReferenceType" integer NOT NULL,
    "ReferenceId" uuid NOT NULL,
    "Outcome" character varying(50) NOT NULL,
    "Notes" text,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "CK_CommerceHubFinancialOperationAudits_Amount" CHECK (("Amount" > (0)::numeric)),
    CONSTRAINT "CK_CommerceHubFinancialOperationAudits_ReferenceType" CHECK (("ReferenceType" = ANY (ARRAY[0, 1, 2, 3, 4])))
);


ALTER TABLE public."CommerceHubFinancialOperationAudits" OWNER TO postgres;

--
-- TOC entry 439 (class 1259 OID 38245)
-- Name: CommerceHubFinancialReconciliationReports; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubFinancialReconciliationReports" (
    "Id" uuid NOT NULL,
    "TimestampUtc" timestamp with time zone DEFAULT now() NOT NULL,
    "Status" character varying(20) NOT NULL,
    "MismatchCount" integer DEFAULT 0 CONSTRAINT "CommerceHubFinancialReconciliationReport_MismatchCount_not_null" NOT NULL,
    "DetailsJson" text DEFAULT '{}'::text NOT NULL,
    CONSTRAINT "CK_CommerceHubFinancialReconciliationReports_MismatchCount" CHECK (("MismatchCount" >= 0)),
    CONSTRAINT "CK_CommerceHubFinancialReconciliationReports_Status" CHECK ((("Status")::text = ANY ((ARRAY['Healthy'::character varying, 'Warning'::character varying, 'Critical'::character varying])::text[])))
);


ALTER TABLE public."CommerceHubFinancialReconciliationReports" OWNER TO postgres;

--
-- TOC entry 422 (class 1259 OID 37681)
-- Name: CommerceHubFinancialTransactions; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubFinancialTransactions" (
    "Id" uuid NOT NULL,
    "EntityType" integer NOT NULL,
    "EntityId" uuid,
    "TransactionType" integer NOT NULL,
    "Direction" integer NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Currency" character varying(10) DEFAULT 'EGP'::character varying NOT NULL,
    "ReferenceType" integer NOT NULL,
    "ReferenceId" uuid NOT NULL,
    "Description" text NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "IdempotencyKey" character varying(200) NOT NULL,
    "CorrelationId" character varying(200) NOT NULL,
    "CreatedBy" uuid,
    "Reason" text,
    "FinancialBucket" integer NOT NULL,
    CONSTRAINT "CK_CommerceHubFinancialTransactions_Amount" CHECK (("Amount" > (0)::numeric)),
    CONSTRAINT "CK_CommerceHubFinancialTransactions_Direction" CHECK (("Direction" = ANY (ARRAY[0, 1]))),
    CONSTRAINT "CK_CommerceHubFinancialTransactions_EntityScope" CHECK (((("EntityType" = 0) AND ("EntityId" IS NULL)) OR (("EntityType" = 1) AND ("EntityId" IS NOT NULL)))),
    CONSTRAINT "CK_CommerceHubFinancialTransactions_FinancialBucket" CHECK (("FinancialBucket" = ANY (ARRAY[0, 1, 2]))),
    CONSTRAINT "CK_CommerceHubFinancialTransactions_ReferenceType" CHECK (("ReferenceType" = ANY (ARRAY[0, 1, 2, 3, 4]))),
    CONSTRAINT "CK_CommerceHubFinancialTransactions_TransactionType" CHECK (("TransactionType" = ANY (ARRAY[0, 1, 2, 3, 4, 5, 6])))
);


ALTER TABLE public."CommerceHubFinancialTransactions" OWNER TO postgres;

--
-- TOC entry 391 (class 1259 OID 36813)
-- Name: CommerceHubListings; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubListings" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "VariantId" uuid NOT NULL,
    "IsPublished" boolean DEFAULT false NOT NULL,
    "CommerceHubCategoryId" uuid NOT NULL,
    "CommerceHubPrice" numeric(18,2),
    "AttributesJson" text,
    "AttributeSchemaSnapshotJson" text,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "RestorePublicationOnActivate" boolean DEFAULT false NOT NULL,
    CONSTRAINT "CK_CommerceHubListings_CommerceHubPrice_PosAuthorityNull" CHECK (("CommerceHubPrice" IS NULL))
);


ALTER TABLE public."CommerceHubListings" OWNER TO postgres;

--
-- TOC entry 403 (class 1259 OID 37060)
-- Name: CommerceHubMarketplaceOrderNumberSeq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public."CommerceHubMarketplaceOrderNumberSeq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public."CommerceHubMarketplaceOrderNumberSeq" OWNER TO postgres;

--
-- TOC entry 383 (class 1259 OID 36622)
-- Name: CommerceHubMarketplaceOrders; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubMarketplaceOrders" (
    "Id" uuid NOT NULL,
    "OrderNumber" text NOT NULL,
    "FullName" character varying(200) NOT NULL,
    "PhoneNumber" character varying(50) NOT NULL,
    "AddressLine" character varying(500) NOT NULL,
    "City" character varying(100) NOT NULL,
    "Area" character varying(100),
    "Notes" character varying(1000),
    "Currency" character varying(10) NOT NULL,
    "PaymentMethodCode" character varying(50) NOT NULL,
    "PaymentMethodName" character varying(100) NOT NULL,
    "PaymentReference" character varying(100),
    "PaymentGateway" character varying(50),
    "Subtotal" numeric(18,2) NOT NULL,
    "ShippingTotal" numeric(18,2) NOT NULL,
    "TotalAmount" numeric(18,2) NOT NULL,
    "Status" integer NOT NULL,
    "PaymentStatus" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "IdempotencyKey" character varying(100),
    "PaidAt" timestamp with time zone,
    "PaymentReferenceId" character varying(100),
    "OnlineCustomerId" uuid,
    "OrderAccessToken" text,
    "IsProjected" boolean DEFAULT false NOT NULL,
    "ProjectedAtUtc" timestamp with time zone,
    "CustomerId" uuid NOT NULL,
    "PromoCode" character varying(64),
    "PromoDiscountTotal" numeric(18,2) DEFAULT 0 NOT NULL,
    "ProjectionMutationVersion" bigint DEFAULT 1 NOT NULL,
    "LastProjectionAttemptAt" timestamp with time zone,
    "ProjectionAttemptCount" integer DEFAULT 0 NOT NULL,
    "IsProjectionTerminalFailure" boolean DEFAULT false CONSTRAINT "CommerceHubMarketplaceOrder_IsProjectionTerminalFailur_not_null" NOT NULL,
    CONSTRAINT "CK_CommerceHubMarketplaceOrders_CustomerAuthorityBoundary" CHECK ((("CustomerId" <> '00000000-0000-0000-0000-000000000000'::uuid) AND ("OnlineCustomerId" IS NULL))),
    CONSTRAINT "CK_CommerceHubMarketplaceOrders_ProjectionMutationVersion_Posit" CHECK (("ProjectionMutationVersion" > 0)),
    CONSTRAINT "CK_CommerceHubMarketplaceOrders_PromoDiscountTotal" CHECK (("PromoDiscountTotal" >= (0)::numeric))
);


ALTER TABLE public."CommerceHubMarketplaceOrders" OWNER TO postgres;

--
-- TOC entry 6812 (class 0 OID 0)
-- Dependencies: 383
-- Name: COLUMN "CommerceHubMarketplaceOrders"."LastProjectionAttemptAt"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."CommerceHubMarketplaceOrders"."LastProjectionAttemptAt" IS 'Timestamp of the last projection attempt. Used to prioritize newer failures over old stuck ones.';


--
-- TOC entry 6813 (class 0 OID 0)
-- Dependencies: 383
-- Name: COLUMN "CommerceHubMarketplaceOrders"."ProjectionAttemptCount"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."CommerceHubMarketplaceOrders"."ProjectionAttemptCount" IS 'Number of projection attempts. Used to identify terminal failures after threshold is exceeded.';


--
-- TOC entry 6814 (class 0 OID 0)
-- Dependencies: 383
-- Name: COLUMN "CommerceHubMarketplaceOrders"."IsProjectionTerminalFailure"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."CommerceHubMarketplaceOrders"."IsProjectionTerminalFailure" IS 'True if projection has failed repeatedly and should be skipped by the reconciliation worker.';


--
-- TOC entry 409 (class 1259 OID 37199)
-- Name: CommerceHubPaymentAttempts; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubPaymentAttempts" (
    "Id" uuid NOT NULL,
    "PaymentId" uuid NOT NULL,
    "Provider" integer NOT NULL,
    "AttemptNumber" integer NOT NULL,
    "ProviderReference" character varying(100) NOT NULL,
    "ProviderIntentId" character varying(200),
    "ProviderTransactionId" character varying(200),
    "Status" integer NOT NULL,
    "FailureReason" character varying(1000),
    "CompletedAtUtc" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."CommerceHubPaymentAttempts" OWNER TO postgres;

--
-- TOC entry 475 (class 1259 OID 3022175)
-- Name: CommerceHubPaymentCompensationOutboxMessages; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubPaymentCompensationOutboxMessages" (
    "Id" uuid NOT NULL,
    "PaymentId" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "TenantId" uuid,
    "AffectedStoreIdsJson" text DEFAULT '[]'::text CONSTRAINT "CommerceHubPaymentCompensationOut_AffectedStoreIdsJson_not_null" NOT NULL,
    "Provider" integer NOT NULL,
    "ProviderIntentId" character varying(200),
    "ProviderTransactionId" character varying(200),
    "Amount" numeric(18,2) NOT NULL,
    "Currency" character varying(10) NOT NULL,
    "Reason" character varying(500) NOT NULL,
    "IdempotencyKey" character varying(200) CONSTRAINT "CommerceHubPaymentCompensationOutboxMes_IdempotencyKey_not_null" NOT NULL,
    "CorrelationId" character varying(100),
    "ProcessedAtUtc" timestamp with time zone,
    "AttemptCount" integer DEFAULT 0 CONSTRAINT "CommerceHubPaymentCompensationOutboxMessa_AttemptCount_not_null" NOT NULL,
    "LastError" character varying(2000),
    "NextRetryAtUtc" timestamp with time zone,
    "ProcessingLeaseId" uuid,
    "ProcessingLeaseExpiresAtUtc" timestamp with time zone,
    "IsFailed" boolean DEFAULT false NOT NULL,
    "GatewayReference" character varying(200),
    "GatewayStatus" character varying(100),
    "GatewayRequestStartedAtUtc" timestamp with time zone,
    "GatewayOutcomeUncertainAtUtc" timestamp with time zone,
    "RequiresOperatorReconciliation" boolean DEFAULT false CONSTRAINT "CommerceHubPaymentCompensat_RequiresOperatorReconcilia_not_null" NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    CONSTRAINT "CK_CHPaymentCompOutbox_Amount" CHECK (("Amount" > (0)::numeric)),
    CONSTRAINT "CK_CHPaymentCompOutbox_UncertainOutcomeRequiresOperator" CHECK ((("GatewayOutcomeUncertainAtUtc" IS NULL) OR (("RequiresOperatorReconciliation" = true) AND ("ProcessedAtUtc" IS NULL) AND ("IsFailed" = true))))
);


ALTER TABLE public."CommerceHubPaymentCompensationOutboxMessages" OWNER TO postgres;

--
-- TOC entry 406 (class 1259 OID 37120)
-- Name: CommerceHubPaymentItems; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubPaymentItems" (
    "Id" uuid NOT NULL,
    "PaymentId" uuid NOT NULL,
    "SubOrderId" uuid NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."CommerceHubPaymentItems" OWNER TO postgres;

--
-- TOC entry 407 (class 1259 OID 37133)
-- Name: CommerceHubPaymentLogs; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubPaymentLogs" (
    "Id" uuid NOT NULL,
    "PaymentId" uuid NOT NULL,
    "EventType" character varying(100) NOT NULL,
    "ProviderEventId" character varying(100),
    "RawResponse" text NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."CommerceHubPaymentLogs" OWNER TO postgres;

--
-- TOC entry 405 (class 1259 OID 37100)
-- Name: CommerceHubPayments; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubPayments" (
    "Id" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Currency" character varying(10) NOT NULL,
    "Status" integer NOT NULL,
    "Provider" integer NOT NULL,
    "ProviderReference" character varying(100) NOT NULL,
    "ProviderIntentId" character varying(200),
    "ProviderTransactionId" character varying(200),
    "CheckoutUrl" character varying(2000),
    "PaymentToken" character varying(500),
    "RefundedAmount" numeric(18,2) DEFAULT 0 NOT NULL,
    "FailureReason" character varying(1000),
    "PaidAtUtc" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "IsProcessed" boolean DEFAULT false NOT NULL,
    "ProcessedAtUtc" timestamp with time zone,
    "ProvisionalRefundedAmount" numeric(18,2) DEFAULT 0 NOT NULL,
    "NeedsManualCurrencyReview" boolean DEFAULT false NOT NULL,
    CONSTRAINT "CK_CommerceHubPayments_Amount_Positive" CHECK (("Amount" > (0)::numeric)),
    CONSTRAINT "CK_CommerceHubPayments_Paid_State_Complete" CHECK ((("Status" <> 1) OR (("IsProcessed" = true) AND ("PaidAtUtc" IS NOT NULL) AND ("ProviderTransactionId" IS NOT NULL)))),
    CONSTRAINT "CK_CommerceHubPayments_ProvisionalRefundedAmount_Range" CHECK ((("ProvisionalRefundedAmount" >= (0)::numeric) AND (round(("RefundedAmount" + "ProvisionalRefundedAmount"), 2) <= round("Amount", 2)))),
    CONSTRAINT "CK_CommerceHubPayments_RefundedAmount_Range" CHECK ((("RefundedAmount" >= (0)::numeric) AND ("RefundedAmount" <= "Amount")))
);


ALTER TABLE public."CommerceHubPayments" OWNER TO postgres;

--
-- TOC entry 412 (class 1259 OID 37277)
-- Name: CommerceHubPayoutItems; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubPayoutItems" (
    "Id" uuid NOT NULL,
    "PayoutId" uuid NOT NULL,
    "EarningId" uuid NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."CommerceHubPayoutItems" OWNER TO postgres;

--
-- TOC entry 411 (class 1259 OID 37259)
-- Name: CommerceHubPayouts; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubPayouts" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Currency" character varying(10) NOT NULL,
    "Status" integer NOT NULL,
    "Method" integer NOT NULL,
    "Reference" character varying(200),
    "FailureReason" character varying(1000),
    "IdempotencyKey" character varying(200) NOT NULL,
    "ProcessedAtUtc" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "ExternalReferenceId" character varying(200),
    "BankTransactionId" character varying(200),
    "ProcessedBy" character varying(200),
    "PayoutMethodId" uuid,
    "DestinationValueSnapshot" character varying(200),
    "CompletedAtUtc" timestamp with time zone,
    "CompletionEvidenceFingerprint" character varying(128),
    "ExternalTransferIdempotencyKey" character varying(240),
    "ExternalTransferHandoffToken" character varying(128),
    "ExternalTransferHandoffCommittedAtUtc" timestamp with time zone,
    CONSTRAINT "CK_CommerceHubPayouts_Amount_Positive" CHECK (("Amount" > (0)::numeric)),
    CONSTRAINT "CK_CommerceHubPayouts_Completed_Evidence" CHECK ((("Status" <> 2) OR (NULLIF(btrim(("ExternalReferenceId")::text), ''::text) IS NOT NULL) OR (NULLIF(btrim(("BankTransactionId")::text), ''::text) IS NOT NULL))),
    CONSTRAINT "CK_CommerceHubPayouts_Completed_State_Complete" CHECK ((("Status" <> 2) OR (("ProcessedAtUtc" IS NOT NULL) AND ("CompletedAtUtc" IS NOT NULL)))),
    CONSTRAINT "CK_CommerceHubPayouts_NoRefundFailureAfterHandoff" CHECK ((NOT (("Status" = 3) AND ("ExternalTransferHandoffCommittedAtUtc" IS NOT NULL) AND (("ProcessedBy")::text = ANY ((ARRAY['system:refund'::character varying, 'system:refund-control'::character varying])::text[]))))),
    CONSTRAINT "CK_CommerceHubPayouts_Processing_ExternalIntent" CHECK (((("Status" <> 1) AND ("Status" <> 4)) OR (NULLIF(btrim(("ExternalTransferIdempotencyKey")::text), ''::text) IS NOT NULL))),
    CONSTRAINT "CK_CommerceHubPayouts_Processing_HandoffCommitted" CHECK (((("Status" <> 1) AND ("Status" <> 4)) OR ((NULLIF(btrim(("ExternalTransferHandoffToken")::text), ''::text) IS NOT NULL) AND ("ExternalTransferHandoffCommittedAtUtc" IS NOT NULL)))),
    CONSTRAINT "CK_CommerceHubPayouts_Terminal_HandoffCommitted" CHECK (((("Status" <> 2) AND ("Status" <> 4)) OR ((NULLIF(btrim(("ExternalTransferHandoffToken")::text), ''::text) IS NOT NULL) AND ("ExternalTransferHandoffCommittedAtUtc" IS NOT NULL))))
);


ALTER TABLE public."CommerceHubPayouts" OWNER TO postgres;

--
-- TOC entry 418 (class 1259 OID 37543)
-- Name: CommerceHubRefundEvidenceMedia; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubRefundEvidenceMedia" (
    "Id" uuid NOT NULL,
    "RefundRequestId" uuid,
    "CustomerId" uuid NOT NULL,
    "ObjectKey" character varying(500) NOT NULL,
    "OriginalFileName" character varying(255) NOT NULL,
    "ContentType" character varying(255) NOT NULL,
    "MediaType" character varying(32) NOT NULL,
    "SizeBytes" bigint DEFAULT 0 NOT NULL,
    "MetadataJson" jsonb,
    "ExpiresAtUtc" timestamp with time zone NOT NULL,
    "AttachedAtUtc" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "ExpectedSizeBytes" bigint DEFAULT 0 NOT NULL,
    "ContentHash" character varying(128),
    "ThumbnailObjectKey" character varying(500),
    "IsTemporary" boolean DEFAULT true NOT NULL,
    "IsFlagged" boolean DEFAULT false NOT NULL,
    "UploadedAtUtc" timestamp with time zone
);


ALTER TABLE public."CommerceHubRefundEvidenceMedia" OWNER TO postgres;

--
-- TOC entry 420 (class 1259 OID 37606)
-- Name: CommerceHubRefundExecutions; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubRefundExecutions" (
    "Id" uuid NOT NULL,
    "RefundId" uuid NOT NULL,
    "Method" integer NOT NULL,
    "AccountSnapshot" character varying(500) NOT NULL,
    "TransactionReference" character varying(200) NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Currency" character varying(10) NOT NULL,
    "ExecutedBy" uuid NOT NULL,
    "ExecutedAt" timestamp with time zone NOT NULL,
    "Notes" character varying(1000),
    "IsSuccessful" boolean DEFAULT false NOT NULL,
    "FailureReason" character varying(2000),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."CommerceHubRefundExecutions" OWNER TO postgres;

--
-- TOC entry 417 (class 1259 OID 37478)
-- Name: CommerceHubRefundRequests; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubRefundRequests" (
    "Id" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "OrderItemId" uuid NOT NULL,
    "CustomerId" uuid NOT NULL,
    "VendorId" uuid NOT NULL,
    "Quantity" integer NOT NULL,
    "UnitPriceSnapshot" numeric(18,2) NOT NULL,
    "TotalAmount" numeric(18,2) NOT NULL,
    "Reason" character varying(1000) NOT NULL,
    "Status" integer NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "IdempotencyKey" character varying(100),
    "ReviewedBy" uuid,
    "ReviewedAtUtc" timestamp with time zone,
    "ApprovedAtUtc" timestamp with time zone,
    "RejectedAtUtc" timestamp with time zone,
    "ProcessingStartedAtUtc" timestamp with time zone,
    "CompletedAtUtc" timestamp with time zone,
    "FailedAtUtc" timestamp with time zone,
    "AdminNote" character varying(1000),
    "FailureReason" character varying(2000),
    "ExecutionAttempts" integer DEFAULT 0 NOT NULL,
    "ReasonCode" integer DEFAULT 5 NOT NULL,
    "ReasonText" character varying(1000),
    "PayoutMethodSnapshot" integer,
    "PayoutAccountSnapshot" character varying(500),
    "PayoutAccountMaskedSnapshot" character varying(32),
    "PayoutAccountHolderNameSnapshot" character varying(200),
    "ReturnRequestId" uuid,
    "RefundExecutionTrigger" integer DEFAULT 0 NOT NULL
);


ALTER TABLE public."CommerceHubRefundRequests" OWNER TO postgres;

--
-- TOC entry 413 (class 1259 OID 37290)
-- Name: CommerceHubRefunds; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubRefunds" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "SubOrderId" uuid NOT NULL,
    "PaymentId" uuid,
    "Amount" numeric(18,2) NOT NULL,
    "CommissionReversalAmount" numeric(18,2) NOT NULL,
    "VendorEarningReversalAmount" numeric(18,2) NOT NULL,
    "Currency" character varying(10) NOT NULL,
    "Status" integer NOT NULL,
    "Reason" character varying(1000) NOT NULL,
    "Reference" character varying(200),
    "FailureReason" character varying(1000),
    "IdempotencyKey" character varying(200) NOT NULL,
    "IsFullRefund" boolean DEFAULT false NOT NULL,
    "ProcessedAtUtc" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "GatewayStatus" character varying(100),
    "ProcessingStartedAtUtc" timestamp with time zone,
    "RefundRequestId" uuid,
    "OrderItemId" uuid,
    "Quantity" integer,
    "IsPostPayoutAdjustment" boolean DEFAULT false NOT NULL,
    "PromoDiscountReversalAmount" numeric(18,2) DEFAULT 0 NOT NULL,
    CONSTRAINT "CK_CommerceHubRefunds_Amount_Positive" CHECK (("Amount" > (0)::numeric)),
    CONSTRAINT "CK_CommerceHubRefunds_Completed_State_Complete" CHECK ((("Status" <> 2) OR ("ProcessedAtUtc" IS NOT NULL))),
    CONSTRAINT "CK_CommerceHubRefunds_PromoDiscountReversalAmount" CHECK (("PromoDiscountReversalAmount" >= (0)::numeric)),
    CONSTRAINT "CK_CommerceHubRefunds_Reversals_NonNegative" CHECK ((("CommissionReversalAmount" >= (0)::numeric) AND ("PromoDiscountReversalAmount" >= (0)::numeric) AND ("VendorEarningReversalAmount" >= (0)::numeric))),
    CONSTRAINT "CK_CommerceHubRefunds_Reversals_Total_Amount" CHECK (((("CommissionReversalAmount" + "PromoDiscountReversalAmount") + "VendorEarningReversalAmount") = "Amount"))
);


ALTER TABLE public."CommerceHubRefunds" OWNER TO postgres;

--
-- TOC entry 421 (class 1259 OID 37636)
-- Name: CommerceHubReturnRequests; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubReturnRequests" (
    "Id" uuid NOT NULL,
    "RefundId" uuid NOT NULL,
    "OrderItemId" uuid NOT NULL,
    "CustomerId" uuid NOT NULL,
    "VendorId" uuid NOT NULL,
    "Status" integer NOT NULL,
    "PickupScheduledAt" timestamp with time zone,
    "PickedUpAt" timestamp with time zone,
    "InTransitAt" timestamp with time zone,
    "DeliveredAt" timestamp with time zone,
    "ConfirmedAt" timestamp with time zone,
    "TrackingNumber" character varying(200),
    "CourierName" character varying(200),
    "Notes" character varying(1000),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "EscalatedAtUtc" timestamp without time zone,
    "EscalationLevel" integer DEFAULT 0 NOT NULL,
    "EscalationReason" text,
    "ExpectedCompletionAtUtc" timestamp without time zone,
    "RequestedQuantity" integer DEFAULT 1 NOT NULL,
    "ReceivedResellableQuantity" integer DEFAULT 0 NOT NULL,
    "ReceivedDamagedQuantity" integer DEFAULT 0 NOT NULL,
    CONSTRAINT "CK_CommerceHubReturnRequests_ReceivedDamagedQuantity_NonNegativ" CHECK (("ReceivedDamagedQuantity" >= 0)),
    CONSTRAINT "CK_CommerceHubReturnRequests_ReceivedQuantity_Within_Requested" CHECK ((("ReceivedResellableQuantity" + "ReceivedDamagedQuantity") <= "RequestedQuantity")),
    CONSTRAINT "CK_CommerceHubReturnRequests_ReceivedResellableQuantity_NonNega" CHECK (("ReceivedResellableQuantity" >= 0)),
    CONSTRAINT "CK_CommerceHubReturnRequests_RequestedQuantity_Positive" CHECK (("RequestedQuantity" > 0))
);


ALTER TABLE public."CommerceHubReturnRequests" OWNER TO postgres;

--
-- TOC entry 385 (class 1259 OID 36666)
-- Name: CommerceHubSubOrderItems; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubSubOrderItems" (
    "Id" uuid NOT NULL,
    "SubOrderId" uuid NOT NULL,
    "ListingId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "VariantId" uuid NOT NULL,
    "ProductName" character varying(250) NOT NULL,
    "VariantName" character varying(250),
    "VariantAttributesJson" text,
    "ImageUrl" character varying(1000),
    "Quantity" integer NOT NULL,
    "UnitPrice" numeric(18,2) NOT NULL,
    "Total" numeric(18,2) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "PlatformCommissionRate" numeric(8,6) DEFAULT 0 NOT NULL,
    "PlatformCommissionAmount" numeric(18,2) DEFAULT 0 NOT NULL,
    "Status" integer DEFAULT 0 NOT NULL,
    "DeliveredAt" timestamp with time zone,
    "CategoryId" uuid NOT NULL,
    "FinalPlatformCommissionAmount" numeric(18,2) DEFAULT 0 NOT NULL,
    "PromoDiscountAmount" numeric(18,2) DEFAULT 0 NOT NULL,
    CONSTRAINT "CK_CommerceHubSubOrderItems_FinalPlatformCommissionAmount" CHECK (("FinalPlatformCommissionAmount" >= (0)::numeric)),
    CONSTRAINT "CK_CommerceHubSubOrderItems_PromoDiscountAmount" CHECK (("PromoDiscountAmount" >= (0)::numeric))
);


ALTER TABLE public."CommerceHubSubOrderItems" OWNER TO postgres;

--
-- TOC entry 404 (class 1259 OID 37062)
-- Name: CommerceHubSubOrderStatusHistory; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubSubOrderStatusHistory" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "SubOrderId" uuid NOT NULL,
    "PreviousStatus" integer NOT NULL,
    "NewStatus" integer NOT NULL,
    "Reason" character varying(1000),
    "CorrelationId" character varying(200),
    "ChangedAtUtc" timestamp with time zone DEFAULT now() NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."CommerceHubSubOrderStatusHistory" OWNER TO postgres;

--
-- TOC entry 384 (class 1259 OID 36646)
-- Name: CommerceHubSubOrders; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubSubOrders" (
    "Id" uuid NOT NULL,
    "MarketplaceOrderId" uuid NOT NULL,
    "OrderNumber" text NOT NULL,
    "StoreId" uuid NOT NULL,
    "StoreName" character varying(200) NOT NULL,
    "Currency" character varying(10) NOT NULL,
    "Subtotal" numeric(18,2) NOT NULL,
    "Shipping" numeric(18,2) NOT NULL,
    "Total" numeric(18,2) NOT NULL,
    "Status" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "PaidAt" timestamp with time zone,
    "PaymentReferenceId" character varying(100),
    "PaymentStatus" integer DEFAULT 0 NOT NULL,
    "ShippingMethodId" uuid,
    "ShippingMethodName" character varying(200),
    "PaymentMethod" integer DEFAULT 0 NOT NULL,
    "PaymentMethodCode" character varying(50) DEFAULT 'PAYMOB'::character varying NOT NULL,
    "PaymentMethodName" character varying(100) DEFAULT 'Paymob'::character varying NOT NULL,
    "PaymentSnapshotJson" text,
    "PlatformCommissionAmount" numeric(18,2) DEFAULT 0 NOT NULL,
    "VendorEarningAmount" numeric(18,2) DEFAULT 0 NOT NULL,
    "PlatformCommissionRate" numeric(8,6) DEFAULT 0 NOT NULL,
    "CommissionSnapshotJson" text,
    "IsEarningCreated" boolean DEFAULT false NOT NULL,
    "EarningCreatedAtUtc" timestamp with time zone,
    "ShippingSnapshotJson" text,
    "FinalPlatformCommissionAmount" numeric(18,2) DEFAULT 0 NOT NULL,
    "PromoDiscountAmount" numeric(18,2) DEFAULT 0 NOT NULL,
    "ProjectionMutationVersion" bigint DEFAULT 1 NOT NULL,
    CONSTRAINT "CK_CommerceHubSubOrders_FinalPlatformCommissionAmount" CHECK (("FinalPlatformCommissionAmount" >= (0)::numeric)),
    CONSTRAINT "CK_CommerceHubSubOrders_ProjectionMutationVersion_Positive" CHECK (("ProjectionMutationVersion" > 0)),
    CONSTRAINT "CK_CommerceHubSubOrders_PromoDiscountAmount" CHECK (("PromoDiscountAmount" >= (0)::numeric))
);


ALTER TABLE public."CommerceHubSubOrders" OWNER TO postgres;

--
-- TOC entry 474 (class 1259 OID 3022139)
-- Name: CommerceHubUnresolvedPaymentWebhooks; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubUnresolvedPaymentWebhooks" (
    "Id" uuid NOT NULL,
    "Provider" integer NOT NULL,
    "ProviderEventId" character varying(200) NOT NULL,
    "ProviderReference" character varying(200),
    "LocalPaymentId" uuid,
    "ProviderIntentId" character varying(200),
    "ProviderTransactionId" character varying(200),
    "TenantId" uuid,
    "OrderId" uuid,
    "AffectedStoreIdsJson" text DEFAULT '[]'::text CONSTRAINT "CommerceHubUnresolvedPaymentWebho_AffectedStoreIdsJson_not_null" NOT NULL,
    "SuspenseRoutedStoreIdsJson" text DEFAULT '[]'::text CONSTRAINT "CommerceHubUnresolvedPaymen_SuspenseRoutedStoreIdsJson_not_null" NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Currency" character varying(10) NOT NULL,
    "EventType" character varying(100) NOT NULL,
    "PayloadHash" character varying(64) NOT NULL,
    "RawPayload" text NOT NULL,
    "Status" character varying(50) DEFAULT 'PendingResolution'::character varying NOT NULL,
    "ResolutionAttemptCount" integer DEFAULT 0 CONSTRAINT "CommerceHubUnresolvedPaymentWeb_ResolutionAttemptCount_not_null" NOT NULL,
    "LastResolutionAttemptAtUtc" timestamp with time zone,
    "RoutedToSuspenseAtUtc" timestamp with time zone,
    "CorrelationId" character varying(100),
    "LastError" character varying(2000),
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    CONSTRAINT "CK_CommerceHubUnresolvedPaymentWebhooks_AffectedStoreIdsJson_Ar" CHECK ((jsonb_typeof(("AffectedStoreIdsJson")::jsonb) = 'array'::text)),
    CONSTRAINT "CK_CommerceHubUnresolvedPaymentWebhooks_Amount_NonNegative" CHECK (("Amount" >= (0)::numeric)),
    CONSTRAINT "CK_CommerceHubUnresolvedPaymentWebhooks_PendingTenantResolution" CHECK (((("Status")::text <> 'PendingTenantResolution'::text) OR (("RoutedToSuspenseAtUtc" IS NULL) AND (("SuspenseRoutedStoreIdsJson")::jsonb = '[]'::jsonb)))),
    CONSTRAINT "CK_CommerceHubUnresolvedPaymentWebhooks_RoutedSuspenseStatusSco" CHECK (((("Status")::text <> 'RoutedToSuspense'::text) OR (("RoutedToSuspenseAtUtc" IS NOT NULL) AND (jsonb_array_length(("AffectedStoreIdsJson")::jsonb) > 0) AND (("SuspenseRoutedStoreIdsJson")::jsonb = ("AffectedStoreIdsJson")::jsonb)))),
    CONSTRAINT "CK_CommerceHubUnresolvedPaymentWebhooks_SuspenseRoutedStoreIdsJ" CHECK ((jsonb_typeof(("SuspenseRoutedStoreIdsJson")::jsonb) = 'array'::text))
);


ALTER TABLE public."CommerceHubUnresolvedPaymentWebhooks" OWNER TO postgres;

--
-- TOC entry 408 (class 1259 OID 37148)
-- Name: CommerceHubVendorEarnings; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubVendorEarnings" (
    "Id" uuid NOT NULL,
    "SubOrderId" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Status" integer NOT NULL,
    "AvailableAtUtc" timestamp with time zone,
    "PaidOutAtUtc" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "PayoutId" uuid,
    "RefundId" uuid,
    "OriginalEarningId" uuid,
    "ReversedAtUtc" timestamp with time zone,
    "PaymentId" uuid,
    "PaidOutAmount" numeric(18,2) DEFAULT 0 NOT NULL,
    "PayoutReservedAmount" numeric(18,2) DEFAULT 0 NOT NULL,
    CONSTRAINT "CK_CHVendorEarnings_PayoutAllocation_NonNegative" CHECK ((("PaidOutAmount" >= (0)::numeric) AND ("PayoutReservedAmount" >= (0)::numeric))),
    CONSTRAINT "CK_CHVendorEarnings_PayoutAllocation_NotOverAllocated" CHECK ((("Amount" < (0)::numeric) OR (round(("PaidOutAmount" + "PayoutReservedAmount"), 2) <= round("Amount", 2)))),
    CONSTRAINT "CK_CommerceHubVendorEarnings_InPayout_HasPayout" CHECK ((("Status" <> 3) OR ("PayoutId" IS NOT NULL))),
    CONSTRAINT "CK_CommerceHubVendorEarnings_PaidOut_HasPaidAmount" CHECK ((("Status" <> 2) OR ("PaidOutAmount" > (0)::numeric) OR ("Amount" < (0)::numeric))),
    CONSTRAINT "CK_CommerceHubVendorEarnings_PayoutAmounts_NonNegative" CHECK ((("PaidOutAmount" >= (0)::numeric) AND ("PayoutReservedAmount" >= (0)::numeric))),
    CONSTRAINT "CK_CommerceHubVendorEarnings_PayoutAmounts_Within_Positive_Ear~" CHECK ((("Amount" < (0)::numeric) OR (round(("PaidOutAmount" + "PayoutReservedAmount"), 2) <= round("Amount", 2))))
);


ALTER TABLE public."CommerceHubVendorEarnings" OWNER TO postgres;

--
-- TOC entry 427 (class 1259 OID 37837)
-- Name: CommerceHubVendorPaymentAuditLogs; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubVendorPaymentAuditLogs" (
    "Id" uuid NOT NULL,
    "PaymentRequestId" uuid NOT NULL,
    "AdminId" uuid NOT NULL,
    "Action" integer NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Currency" character varying(10) DEFAULT 'EGP'::character varying NOT NULL,
    "Reason" text NOT NULL,
    "TimestampUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "CK_CommerceHubVendorPaymentAuditLogs_Action" CHECK (("Action" = ANY (ARRAY[0, 1]))),
    CONSTRAINT "CK_CommerceHubVendorPaymentAuditLogs_Amount" CHECK (("Amount" > (0)::numeric))
);


ALTER TABLE public."CommerceHubVendorPaymentAuditLogs" OWNER TO postgres;

--
-- TOC entry 424 (class 1259 OID 37764)
-- Name: CommerceHubVendorPaymentMethods; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubVendorPaymentMethods" (
    "Id" uuid NOT NULL,
    "Name" character varying(120) NOT NULL,
    "DestinationValue" character varying(200) NOT NULL,
    "Instructions" text NOT NULL,
    "IsActive" boolean DEFAULT true NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "CreatedBy" uuid NOT NULL
);


ALTER TABLE public."CommerceHubVendorPaymentMethods" OWNER TO postgres;

--
-- TOC entry 426 (class 1259 OID 37816)
-- Name: CommerceHubVendorPaymentProofs; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubVendorPaymentProofs" (
    "Id" uuid NOT NULL,
    "PaymentRequestId" uuid NOT NULL,
    "ObjectKey" character varying(500) NOT NULL,
    "OriginalFileName" character varying(255) NOT NULL,
    "ContentType" character varying(255) NOT NULL,
    "SizeBytes" bigint NOT NULL,
    "UploadedAtUtc" timestamp with time zone NOT NULL,
    "ContentHash" character varying(64) NOT NULL
);


ALTER TABLE public."CommerceHubVendorPaymentProofs" OWNER TO postgres;

--
-- TOC entry 425 (class 1259 OID 37781)
-- Name: CommerceHubVendorPaymentRequests; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubVendorPaymentRequests" (
    "Id" uuid NOT NULL,
    "VendorId" uuid NOT NULL,
    "PaymentMethodId" uuid NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Currency" character varying(10) DEFAULT 'EGP'::character varying NOT NULL,
    "Status" integer NOT NULL,
    "TransferReference" character varying(200),
    "IdempotencyKey" character varying(200) NOT NULL,
    "MethodNameSnapshot" character varying(120) NOT NULL,
    "DestinationValueSnapshot" character varying(200) CONSTRAINT "CommerceHubVendorPaymentReque_DestinationValueSnapshot_not_null" NOT NULL,
    "InstructionsSnapshot" text NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "ApprovedAtUtc" timestamp with time zone,
    "ApprovedBy" uuid,
    "RejectedAtUtc" timestamp with time zone,
    "RejectedBy" uuid,
    "ReviewReason" text,
    "LedgerCorrelationId" character varying(200),
    "LedgerIdempotencyKey" character varying(200),
    CONSTRAINT "CK_CommerceHubVendorPaymentRequests_Amount" CHECK (("Amount" > (0)::numeric)),
    CONSTRAINT "CK_CommerceHubVendorPaymentRequests_Approved_TransferReference" CHECK ((("Status" <> 1) OR (NULLIF(btrim(("TransferReference")::text), ''::text) IS NOT NULL))),
    CONSTRAINT "CK_CommerceHubVendorPaymentRequests_Status" CHECK (("Status" = ANY (ARRAY[0, 1, 2])))
);


ALTER TABLE public."CommerceHubVendorPaymentRequests" OWNER TO postgres;

--
-- TOC entry 428 (class 1259 OID 37872)
-- Name: CommerceHubVendorPayoutMethods; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubVendorPayoutMethods" (
    "Id" uuid NOT NULL,
    "VendorId" uuid NOT NULL,
    "MethodType" integer NOT NULL,
    "DestinationValue" character varying(200) NOT NULL,
    "IsDefault" boolean DEFAULT false NOT NULL,
    "IsVerified" boolean DEFAULT false NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."CommerceHubVendorPayoutMethods" OWNER TO postgres;

--
-- TOC entry 423 (class 1259 OID 37731)
-- Name: CommerceHubVendorSettlements; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommerceHubVendorSettlements" (
    "Id" uuid NOT NULL,
    "VendorId" uuid NOT NULL,
    "SettlementType" integer NOT NULL,
    "Direction" integer NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Currency" character varying(10) DEFAULT 'EGP'::character varying NOT NULL,
    "Notes" text NOT NULL,
    "RecordedByUserId" uuid,
    "RecordedByUserEmail" character varying(320),
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "IdempotencyKey" character varying(120) NOT NULL,
    "ExternalReference" character varying(160) NOT NULL,
    CONSTRAINT "CK_CommerceHubVendorSettlements_Amount" CHECK (("Amount" > (0)::numeric)),
    CONSTRAINT "CK_CommerceHubVendorSettlements_Direction" CHECK (("Direction" = ANY (ARRAY[0, 1]))),
    CONSTRAINT "CK_CommerceHubVendorSettlements_ExternalReference_NotBlank" CHECK ((NULLIF(btrim(("ExternalReference")::text), ''::text) IS NOT NULL)),
    CONSTRAINT "CK_CommerceHubVendorSettlements_IdempotencyKey_NotBlank" CHECK ((NULLIF(btrim(("IdempotencyKey")::text), ''::text) IS NOT NULL)),
    CONSTRAINT "CK_CommerceHubVendorSettlements_SettlementType" CHECK (("SettlementType" = ANY (ARRAY[0, 1, 2])))
);


ALTER TABLE public."CommerceHubVendorSettlements" OWNER TO postgres;

--
-- TOC entry 415 (class 1259 OID 37426)
-- Name: CommissionRules; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CommissionRules" (
    "Id" uuid NOT NULL,
    "ScopeType" integer NOT NULL,
    "ScopeId" uuid,
    "Rate" numeric(8,6) NOT NULL,
    "IsActive" boolean DEFAULT true NOT NULL,
    "Priority" integer DEFAULT 0 NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    CONSTRAINT "CK_CommissionRules_Rate" CHECK ((("Rate" >= (0)::numeric) AND ("Rate" <= (1)::numeric))),
    CONSTRAINT "CK_CommissionRules_Scope" CHECK (((("ScopeType" = 0) AND ("ScopeId" IS NULL)) OR (("ScopeType" = ANY (ARRAY[1, 2])) AND ("ScopeId" IS NOT NULL))))
);


ALTER TABLE public."CommissionRules" OWNER TO postgres;

--
-- TOC entry 471 (class 1259 OID 2287776)
-- Name: CouponReservations; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CouponReservations" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "CouponId" uuid NOT NULL,
    "CustomerId" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "Status" integer NOT NULL,
    "CorrelationId" character varying(100),
    "ReservedAtUtc" timestamp with time zone NOT NULL,
    "ConfirmedAtUtc" timestamp with time zone,
    "ReleasedAtUtc" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL
);


ALTER TABLE public."CouponReservations" OWNER TO postgres;

--
-- TOC entry 329 (class 1259 OID 26095)
-- Name: CouponUsages; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CouponUsages" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "CouponId" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "CustomerId" uuid,
    "CouponCode" text NOT NULL,
    "DiscountAmount" numeric(18,2) NOT NULL,
    "OrderSubtotal" numeric(18,2) NOT NULL,
    "UsedAtUtc" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."CouponUsages" OWNER TO postgres;

--
-- TOC entry 302 (class 1259 OID 23394)
-- Name: Coupons; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."Coupons" (
    "Id" uuid NOT NULL,
    "Code" text NOT NULL,
    "Description" text,
    "DiscountType" integer NOT NULL,
    "DiscountValue" numeric NOT NULL,
    "MinOrderAmount" numeric,
    "MaxDiscountAmount" numeric,
    "MaxUsage" integer,
    "CurrentUsage" integer NOT NULL,
    "MaxUsagePerCustomer" integer,
    "StartDate" timestamp with time zone,
    "EndDate" timestamp with time zone,
    "IsActive" boolean NOT NULL,
    "FirstOrderOnly" boolean NOT NULL,
    "ApplicableProductIds" text,
    "ApplicableCategoryIds" text,
    "ExcludedProductIds" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL,
    "NormalizedCode" text NOT NULL,
    CONSTRAINT "CK_Coupons_DiscountType" CHECK (("DiscountType" = ANY (ARRAY[0, 1]))),
    CONSTRAINT "CK_Coupons_DiscountValue_FixedAmount" CHECK ((("DiscountType" <> 1) OR ("DiscountValue" <= 1000000.00))),
    CONSTRAINT "CK_Coupons_DiscountValue_Percentage" CHECK ((("DiscountType" <> 0) OR ("DiscountValue" <= (100)::numeric))),
    CONSTRAINT "CK_Coupons_DiscountValue_Positive" CHECK (("DiscountValue" > (0)::numeric))
);


ALTER TABLE public."Coupons" OWNER TO postgres;

--
-- TOC entry 472 (class 1259 OID 2287814)
-- Name: Currencies; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."Currencies" (
    "Code" character varying(3) NOT NULL,
    "Name" character varying(100) NOT NULL,
    "Symbol" character varying(10) NOT NULL,
    "DecimalPlaces" integer DEFAULT 2 NOT NULL
);


ALTER TABLE public."Currencies" OWNER TO postgres;

--
-- TOC entry 236 (class 1259 OID 20120)
-- Name: CustomerTransactions; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."CustomerTransactions" (
    "Id" uuid NOT NULL,
    "CustomerId" uuid NOT NULL,
    "TransactionDate" timestamp with time zone NOT NULL,
    "TransactionType" integer NOT NULL,
    "DebitAmount" numeric NOT NULL,
    "CreditAmount" numeric NOT NULL,
    "BalanceAfter" numeric NOT NULL,
    "ReferenceId" uuid,
    "ReferenceNumber" text,
    "Notes" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "IsOpeningBalance" boolean DEFAULT false NOT NULL,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL
);


ALTER TABLE public."CustomerTransactions" OWNER TO postgres;

--
-- TOC entry 230 (class 1259 OID 20028)
-- Name: Customers; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."Customers" (
    "Id" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Phone" character varying(50),
    "Email" character varying(200),
    "Address" text,
    "TaxNumber" character varying(50),
    "Balance" numeric(18,2) NOT NULL,
    "Notes" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "OpeningBalanceAmount" numeric,
    "BalanceType" text,
    "OpeningBalanceDate" timestamp with time zone,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL
);


ALTER TABLE public."Customers" OWNER TO postgres;

--
-- TOC entry 317 (class 1259 OID 23750)
-- Name: EmailVerificationTokens; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."EmailVerificationTokens" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "TokenHash" character varying(128) NOT NULL,
    "ExpiresAtUtc" timestamp with time zone NOT NULL,
    "UsedAtUtc" timestamp with time zone,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL
);


ALTER TABLE public."EmailVerificationTokens" OWNER TO postgres;

--
-- TOC entry 284 (class 1259 OID 21391)
-- Name: ExpenseCategories; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."ExpenseCategories" (
    "Id" uuid NOT NULL,
    "Name" character varying(100) NOT NULL,
    "Description" text,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL
);


ALTER TABLE public."ExpenseCategories" OWNER TO postgres;

--
-- TOC entry 285 (class 1259 OID 21403)
-- Name: Expenses; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."Expenses" (
    "Id" uuid NOT NULL,
    "ExpenseCategoryId" uuid NOT NULL,
    "ExpenseDate" timestamp with time zone NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Description" character varying(200) NOT NULL,
    "Notes" text,
    "PaymentMethod" character varying(50) NOT NULL,
    "SupplierId" uuid,
    "CashboxId" uuid,
    "BankId" uuid,
    "ReceiptPath" character varying(500),
    "ReferenceNumber" character varying(100),
    "IsRecurring" boolean NOT NULL,
    "RecurringFrequency" character varying(50),
    "RequiresApproval" boolean NOT NULL,
    "IsApproved" boolean NOT NULL,
    "ApprovedBy" uuid,
    "ApprovedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL,
    "AccountingMutationVersion" integer DEFAULT 0 NOT NULL
);


ALTER TABLE public."Expenses" OWNER TO postgres;

--
-- TOC entry 280 (class 1259 OID 21308)
-- Name: FinancialAccounts; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."FinancialAccounts" (
    "Id" uuid NOT NULL,
    "Name" text NOT NULL,
    "AccountNumber" text,
    "IsSystem" boolean NOT NULL,
    "Balance" numeric NOT NULL,
    "ParentAccountId" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "AccountCode" text DEFAULT ''::text NOT NULL,
    "AccountType" integer DEFAULT 0 NOT NULL,
    "Level" integer DEFAULT 1 NOT NULL,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL
);


ALTER TABLE public."FinancialAccounts" OWNER TO postgres;

--
-- TOC entry 281 (class 1259 OID 21326)
-- Name: FinancialTransactions; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."FinancialTransactions" (
    "Id" uuid NOT NULL,
    "AccountId" uuid NOT NULL,
    "Amount" numeric NOT NULL,
    "TransactionType" integer NOT NULL,
    "Date" timestamp with time zone NOT NULL,
    "Description" text NOT NULL,
    "RelatedEntityId" uuid,
    "RelatedEntityType" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL
);


ALTER TABLE public."FinancialTransactions" OWNER TO postgres;

--
-- TOC entry 379 (class 1259 OID 36442)
-- Name: GlobalPhoneReputation; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."GlobalPhoneReputation" (
    "Id" uuid NOT NULL,
    "PhoneNumber" character varying(32) NOT NULL,
    "SpamReports" integer DEFAULT 0 NOT NULL,
    "FirstReportedAtUtc" timestamp with time zone NOT NULL,
    "LastReportedAtUtc" timestamp with time zone NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL
);


ALTER TABLE public."GlobalPhoneReputation" OWNER TO postgres;

--
-- TOC entry 440 (class 1259 OID 38287)
-- Name: HomepageSections; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."HomepageSections" (
    "Id" uuid NOT NULL,
    "Type" character varying(40) NOT NULL,
    "Order" integer NOT NULL,
    "Data" jsonb DEFAULT '{}'::jsonb NOT NULL,
    "IsActive" boolean DEFAULT true NOT NULL,
    "TenantId" uuid,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "SectionGroupId" uuid NOT NULL,
    "Version" integer DEFAULT 1 NOT NULL,
    "SchemaVersion" integer DEFAULT 1 NOT NULL,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "IsDraft" boolean DEFAULT false NOT NULL,
    "IsPublished" boolean DEFAULT false NOT NULL,
    "PublishedAt" timestamp with time zone,
    "DraftGroupId" uuid,
    CONSTRAINT "CK_HomepageSections_Data_Object" CHECK ((jsonb_typeof("Data") = 'object'::text)),
    CONSTRAINT "CK_HomepageSections_DraftGroupId" CHECK (((NOT "IsDraft") OR ("DraftGroupId" IS NOT NULL))),
    CONSTRAINT "CK_HomepageSections_Order" CHECK (("Order" >= 0)),
    CONSTRAINT "CK_HomepageSections_PublishedAt" CHECK (((NOT "IsPublished") OR ("PublishedAt" IS NOT NULL))),
    CONSTRAINT "CK_HomepageSections_SchemaVersion" CHECK (("SchemaVersion" > 0)),
    CONSTRAINT "CK_HomepageSections_State" CHECK ((NOT ("IsDraft" AND "IsPublished"))),
    CONSTRAINT "CK_HomepageSections_Type" CHECK ((("Type")::text = ANY ((ARRAY['hero'::character varying, 'products'::character varying, 'banner'::character varying, 'categories'::character varying])::text[]))),
    CONSTRAINT "CK_HomepageSections_Version" CHECK (("Version" > 0))
);


ALTER TABLE public."HomepageSections" OWNER TO postgres;

--
-- TOC entry 307 (class 1259 OID 23530)
-- Name: InboxState; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."InboxState" (
    "Id" bigint NOT NULL,
    "MessageId" uuid NOT NULL,
    "ConsumerId" uuid NOT NULL,
    "LockId" uuid NOT NULL,
    "RowVersion" bytea,
    "Received" timestamp with time zone NOT NULL,
    "ReceiveCount" integer NOT NULL,
    "ExpirationTime" timestamp with time zone,
    "Consumed" timestamp with time zone,
    "Delivered" timestamp with time zone,
    "LastSequenceNumber" bigint
);


ALTER TABLE public."InboxState" OWNER TO postgres;

--
-- TOC entry 306 (class 1259 OID 23529)
-- Name: InboxState_Id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public."InboxState" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."InboxState_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 430 (class 1259 OID 37946)
-- Name: InventoryEventInbox; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."InventoryEventInbox" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "EventId" uuid NOT NULL,
    "EventType" integer NOT NULL,
    "ReferenceId" uuid,
    "ReferenceLineId" uuid,
    "PayloadHash" character varying(128),
    "FirstReceivedAtUtc" timestamp with time zone NOT NULL,
    "LastReceivedAtUtc" timestamp with time zone,
    "AppliedAtUtc" timestamp with time zone,
    "Status" integer NOT NULL,
    "ReplayCount" integer DEFAULT 0 NOT NULL,
    "StockMovementId" uuid,
    "CorrelationId" character varying(100),
    "IdempotencyKey" character varying(200),
    "Source" character varying(100),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    CONSTRAINT "CK_InventoryEventInbox_ReplayCount_NonNegative" CHECK (("ReplayCount" >= 0))
);


ALTER TABLE public."InventoryEventInbox" OWNER TO postgres;

--
-- TOC entry 429 (class 1259 OID 37919)
-- Name: InventoryReservations; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."InventoryReservations" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "SubOrderId" uuid,
    "OrderItemId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "VariantId" uuid NOT NULL,
    "WarehouseId" uuid NOT NULL,
    "ReservedQuantity" numeric(18,2) DEFAULT 0 NOT NULL,
    "CommittedQuantity" numeric(18,2) DEFAULT 0 NOT NULL,
    "ReleasedQuantity" numeric(18,2) DEFAULT 0 NOT NULL,
    "ReturnedQuantity" numeric(18,2) DEFAULT 0 NOT NULL,
    "DamagedReturnedQuantity" numeric(18,2) DEFAULT 0 NOT NULL,
    "Status" integer NOT NULL,
    "ReservationEventId" uuid NOT NULL,
    "LastMutationEventId" uuid,
    "ExpiresAtUtc" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    CONSTRAINT "CK_InventoryReservations_CommittedQuantity_NonNegative" CHECK (("CommittedQuantity" >= (0)::numeric)),
    CONSTRAINT "CK_InventoryReservations_DamagedReturnedQuantity_NonNegative" CHECK (("DamagedReturnedQuantity" >= (0)::numeric)),
    CONSTRAINT "CK_InventoryReservations_ReleasedQuantity_NonNegative" CHECK (("ReleasedQuantity" >= (0)::numeric)),
    CONSTRAINT "CK_InventoryReservations_ReservedFlow_Valid" CHECK ((("CommittedQuantity" + "ReleasedQuantity") <= "ReservedQuantity")),
    CONSTRAINT "CK_InventoryReservations_ReservedQuantity_NonNegative" CHECK (("ReservedQuantity" >= (0)::numeric)),
    CONSTRAINT "CK_InventoryReservations_ReturnedQuantity_NonNegative" CHECK (("ReturnedQuantity" >= (0)::numeric)),
    CONSTRAINT "CK_InventoryReservations_Returns_DoNotExceedCommitted" CHECK ((("ReturnedQuantity" + "DamagedReturnedQuantity") <= "CommittedQuantity"))
);


ALTER TABLE public."InventoryReservations" OWNER TO postgres;

--
-- TOC entry 295 (class 1259 OID 23209)
-- Name: JournalEntries; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."JournalEntries" (
    "JournalId" bigint NOT NULL,
    "JournalNumber" character varying(50) NOT NULL,
    "Date" timestamp with time zone NOT NULL,
    "Description" character varying(1000) NOT NULL,
    "SourceModule" character varying(100),
    "ReferenceType" character varying(100),
    "ReferenceId" uuid,
    "ReferenceNumber" character varying(100),
    "TotalDebit" numeric(18,4) NOT NULL,
    "TotalCredit" numeric(18,4) NOT NULL,
    "Posted" boolean NOT NULL,
    "PostedAt" timestamp with time zone,
    "PostedBy" character varying(100),
    "IsReversed" boolean NOT NULL,
    "ReversedJournalId" bigint,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" character varying(100),
    "UpdatedAt" timestamp with time zone,
    "IsDeleted" boolean NOT NULL
);


ALTER TABLE public."JournalEntries" OWNER TO postgres;

--
-- TOC entry 294 (class 1259 OID 23208)
-- Name: JournalEntries_JournalId_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public."JournalEntries" ALTER COLUMN "JournalId" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."JournalEntries_JournalId_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 301 (class 1259 OID 23271)
-- Name: JournalLines; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."JournalLines" (
    "LineId" bigint NOT NULL,
    "JournalId" bigint NOT NULL,
    "AccountId" bigint NOT NULL,
    "Debit" numeric(18,4) NOT NULL,
    "Credit" numeric(18,4) NOT NULL,
    "Description" character varying(1000),
    "CostCenterId" bigint,
    "ProductId" uuid,
    "WarehouseId" uuid,
    "CustomerId" uuid,
    "SupplierId" uuid,
    "CreatedAt" timestamp with time zone NOT NULL
);


ALTER TABLE public."JournalLines" OWNER TO postgres;

--
-- TOC entry 300 (class 1259 OID 23270)
-- Name: JournalLines_LineId_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public."JournalLines" ALTER COLUMN "LineId" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."JournalLines_LineId_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 345 (class 1259 OID 35005)
-- Name: LedgerAccounts; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."LedgerAccounts" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid NOT NULL,
    "AccountCode" character varying(20) NOT NULL,
    "Name" character varying(200) NOT NULL,
    "NameAr" character varying(200),
    "Description" character varying(500),
    "AccountType" integer NOT NULL,
    "ParentAccountId" uuid,
    "Level" integer DEFAULT 1 NOT NULL,
    "CurrentBalance" numeric(18,4) DEFAULT 0 NOT NULL,
    "IsSystem" boolean DEFAULT false NOT NULL,
    "IsClearing" boolean DEFAULT false NOT NULL,
    "PreventNegative" boolean DEFAULT false NOT NULL,
    "IsActive" boolean DEFAULT true NOT NULL,
    "ClearingSourceType" character varying(50),
    "ClearingSourceCode" character varying(50),
    "ExpectedSettlementDays" integer,
    "CreatedAtUtc" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."LedgerAccounts" OWNER TO postgres;

--
-- TOC entry 6815 (class 0 OID 0)
-- Dependencies: 345
-- Name: TABLE "LedgerAccounts"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON TABLE public."LedgerAccounts" IS 'Unified Ledger - Chart of Accounts';


--
-- TOC entry 346 (class 1259 OID 35041)
-- Name: LedgerJournalEntries; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."LedgerJournalEntries" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid NOT NULL,
    "JournalNumber" character varying(50) NOT NULL,
    "JournalDate" timestamp with time zone NOT NULL,
    "PostedAtUtc" timestamp with time zone DEFAULT now() NOT NULL,
    "SourceType" character varying(50) NOT NULL,
    "SourceId" uuid NOT NULL,
    "SourceReference" character varying(100),
    "Description" character varying(500) NOT NULL,
    "IdempotencyKey" character varying(255) NOT NULL,
    "Status" integer DEFAULT 1 NOT NULL,
    "ReversedByJournalId" uuid,
    "ReversesJournalId" uuid,
    "ReversalReason" character varying(500),
    "CreatedByUserId" uuid,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."LedgerJournalEntries" OWNER TO postgres;

--
-- TOC entry 6816 (class 0 OID 0)
-- Dependencies: 346
-- Name: TABLE "LedgerJournalEntries"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON TABLE public."LedgerJournalEntries" IS 'Unified Ledger - Immutable journal entries';


--
-- TOC entry 347 (class 1259 OID 35070)
-- Name: LedgerJournalNumberSequences; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."LedgerJournalNumberSequences" (
    "TenantId" uuid NOT NULL,
    "CurrentYear" integer NOT NULL,
    "NextNumber" integer DEFAULT 1 NOT NULL
);


ALTER TABLE public."LedgerJournalNumberSequences" OWNER TO postgres;

--
-- TOC entry 6817 (class 0 OID 0)
-- Dependencies: 347
-- Name: TABLE "LedgerJournalNumberSequences"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON TABLE public."LedgerJournalNumberSequences" IS 'Unified Ledger - Journal number sequences';


--
-- TOC entry 348 (class 1259 OID 35079)
-- Name: LedgerLines; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."LedgerLines" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid NOT NULL,
    "JournalEntryId" uuid NOT NULL,
    "AccountId" uuid NOT NULL,
    "DebitAmount" numeric(18,4) DEFAULT 0 NOT NULL,
    "CreditAmount" numeric(18,4) DEFAULT 0 NOT NULL,
    "BalanceAfter" numeric(18,4) NOT NULL,
    "LineDescription" character varying(200),
    "LineNumber" integer NOT NULL,
    "CreatedAtUtc" timestamp with time zone DEFAULT now() NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    CONSTRAINT "CK_LedgerLines_Positive" CHECK ((("DebitAmount" >= (0)::numeric) AND ("CreditAmount" >= (0)::numeric))),
    CONSTRAINT "CK_LedgerLines_SingleSide" CHECK (((("DebitAmount" > (0)::numeric) AND ("CreditAmount" = (0)::numeric)) OR (("DebitAmount" = (0)::numeric) AND ("CreditAmount" > (0)::numeric))))
);


ALTER TABLE public."LedgerLines" OWNER TO postgres;

--
-- TOC entry 6818 (class 0 OID 0)
-- Dependencies: 348
-- Name: TABLE "LedgerLines"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON TABLE public."LedgerLines" IS 'Unified Ledger - Debit/Credit lines';


--
-- TOC entry 366 (class 1259 OID 35841)
-- Name: MarketingReviewEvidences; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."MarketingReviewEvidences" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "ServiceReviewId" uuid NOT NULL,
    "CampaignName" character varying(200) NOT NULL,
    "Platform" character varying(50) NOT NULL,
    "StartDate" date NOT NULL,
    "EndDate" date NOT NULL,
    "OrdersCount" integer,
    "RevenueAmount" numeric(18,2),
    "EvidenceType" character varying(20) NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    CONSTRAINT "CK_MarketingReviewEvidences_CampaignNameNotEmpty" CHECK ((length(("CampaignName")::text) > 0)),
    CONSTRAINT "CK_MarketingReviewEvidences_DateRange" CHECK (("EndDate" >= "StartDate")),
    CONSTRAINT "CK_MarketingReviewEvidences_EvidenceType_Metrics" CHECK ((((("EvidenceType")::text = 'PosVerified'::text) AND ("OrdersCount" IS NOT NULL) AND ("RevenueAmount" IS NOT NULL)) OR ((("EvidenceType")::text = 'SelfReported'::text) AND ("OrdersCount" IS NULL) AND ("RevenueAmount" IS NULL)))),
    CONSTRAINT "CK_MarketingReviewEvidences_NonNegativeMetrics" CHECK (((("OrdersCount" IS NULL) OR ("OrdersCount" >= 0)) AND (("RevenueAmount" IS NULL) OR ("RevenueAmount" >= (0)::numeric)))),
    CONSTRAINT "CK_MarketingReviewEvidences_Platform" CHECK ((("Platform")::text = ANY ((ARRAY['Facebook'::character varying, 'Instagram'::character varying, 'Google'::character varying, 'TikTok'::character varying, 'Twitter'::character varying, 'LinkedIn'::character varying, 'YouTube'::character varying, 'Snapchat'::character varying, 'Email'::character varying, 'WhatsApp'::character varying, 'Other'::character varying])::text[])))
);


ALTER TABLE public."MarketingReviewEvidences" OWNER TO postgres;

--
-- TOC entry 452 (class 1259 OID 46806)
-- Name: MarketplaceCategoryTranslations; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."MarketplaceCategoryTranslations" (
    "Id" uuid NOT NULL,
    "CategoryId" uuid NOT NULL,
    "Language" character varying(5) NOT NULL,
    "Name" character varying(160) NOT NULL,
    "Slug" character varying(180),
    "CreatedAtUtc" timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE public."MarketplaceCategoryTranslations" OWNER TO postgres;

--
-- TOC entry 442 (class 1259 OID 46530)
-- Name: MarketplaceCmsPages; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."MarketplaceCmsPages" (
    "Id" uuid NOT NULL,
    "PageGroupId" uuid NOT NULL,
    "Slug" character varying(160) NOT NULL,
    "Title" character varying(200) NOT NULL,
    "Content" text NOT NULL,
    "ContentType" character varying(20) NOT NULL,
    "Version" integer DEFAULT 1 NOT NULL,
    "IsDraft" boolean DEFAULT false NOT NULL,
    "IsPublished" boolean DEFAULT false NOT NULL,
    "PublishedAt" timestamp with time zone,
    "MetaTitle" character varying(200),
    "MetaDescription" character varying(320),
    "OpenGraphImageUrl" character varying(1000),
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "TitleJson" jsonb,
    "ContentJson" jsonb,
    "MetaTitleJson" jsonb,
    "MetaDescriptionJson" jsonb,
    CONSTRAINT "CK_MarketplaceCmsPages_ContentType" CHECK ((("ContentType")::text = ANY ((ARRAY['RichText'::character varying, 'Blocks'::character varying])::text[]))),
    CONSTRAINT "CK_MarketplaceCmsPages_PublishedAt" CHECK (((NOT "IsPublished") OR ("PublishedAt" IS NOT NULL))),
    CONSTRAINT "CK_MarketplaceCmsPages_State" CHECK ((NOT ("IsDraft" AND "IsPublished"))),
    CONSTRAINT "CK_MarketplaceCmsPages_Version" CHECK (("Version" > 0))
);


ALTER TABLE public."MarketplaceCmsPages" OWNER TO postgres;

--
-- TOC entry 479 (class 1259 OID 3022551)
-- Name: MarketplaceEventOutboxDeadLetterArchives; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."MarketplaceEventOutboxDeadLetterArchives" (
    "Id" uuid NOT NULL,
    "OriginalOutboxMessageId" uuid CONSTRAINT "MarketplaceEventOutboxDeadLett_OriginalOutboxMessageId_not_null" NOT NULL,
    "EventId" uuid NOT NULL,
    "EventType" character varying(200) NOT NULL,
    "ClrType" character varying(1000) NOT NULL,
    "PayloadJson" text NOT NULL,
    "EntityId" uuid NOT NULL,
    "AggregateRootId" uuid CONSTRAINT "MarketplaceEventOutboxDeadLetterArchiv_AggregateRootId_not_null" NOT NULL,
    "TenantId" uuid,
    "CorrelationId" character varying(200) NOT NULL,
    "Version" integer NOT NULL,
    "OccurredAtUtc" timestamp with time zone NOT NULL,
    "FailedAtUtc" timestamp with time zone,
    "AttemptCount" integer NOT NULL,
    "LastError" character varying(2000),
    "ArchivedAtUtc" timestamp with time zone NOT NULL,
    "ArchiveReason" character varying(200) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    CONSTRAINT "CK_MarketplaceEventOutboxDeadLetterArchive_OriginalId_Required" CHECK (("OriginalOutboxMessageId" <> '00000000-0000-0000-0000-000000000000'::uuid))
);


ALTER TABLE public."MarketplaceEventOutboxDeadLetterArchives" OWNER TO postgres;

--
-- TOC entry 449 (class 1259 OID 46698)
-- Name: MarketplaceEventOutboxMessages; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."MarketplaceEventOutboxMessages" (
    "Id" uuid NOT NULL,
    "EventId" uuid NOT NULL,
    "EventType" character varying(200) NOT NULL,
    "ClrType" character varying(1000) NOT NULL,
    "PayloadJson" text NOT NULL,
    "EntityId" uuid NOT NULL,
    "TenantId" uuid,
    "CorrelationId" character varying(200) NOT NULL,
    "Version" integer DEFAULT 1 NOT NULL,
    "OccurredAtUtc" timestamp with time zone NOT NULL,
    "PublishedAtUtc" timestamp with time zone,
    "AttemptCount" integer DEFAULT 0 NOT NULL,
    "LastError" character varying(2000),
    "NextRetryAtUtc" timestamp with time zone,
    "IsFailed" boolean DEFAULT false NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "AggregateRootId" uuid NOT NULL,
    "ClaimedUntilUtc" timestamp with time zone,
    CONSTRAINT "CK_MarketplaceEventOutbox_AggregateRootId_NotEmpty" CHECK (("AggregateRootId" <> '00000000-0000-0000-0000-000000000000'::uuid)),
    CONSTRAINT "CK_MarketplaceEventOutbox_TenantId_Required" CHECK ((("TenantId" IS NOT NULL) AND ("TenantId" <> '00000000-0000-0000-0000-000000000000'::uuid))),
    CONSTRAINT "CK_MarketplaceEventOutbox_Version_Positive" CHECK (("Version" > 0))
);


ALTER TABLE public."MarketplaceEventOutboxMessages" OWNER TO postgres;

--
-- TOC entry 444 (class 1259 OID 46581)
-- Name: MarketplaceFooterLinks; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."MarketplaceFooterLinks" (
    "Id" uuid NOT NULL,
    "SectionId" uuid NOT NULL,
    "Label" character varying(160) NOT NULL,
    "LinkType" character varying(20) NOT NULL,
    "Url" character varying(1000),
    "CmsPageSlug" character varying(160),
    "OpenInNewTab" boolean DEFAULT false NOT NULL,
    "SortOrder" integer DEFAULT 0 NOT NULL,
    "IsActive" boolean DEFAULT true NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "LabelJson" jsonb,
    CONSTRAINT "CK_MarketplaceFooterLinks_LinkType" CHECK ((("LinkType")::text = ANY ((ARRAY['Internal'::character varying, 'External'::character varying, 'Cms'::character varying])::text[]))),
    CONSTRAINT "CK_MarketplaceFooterLinks_Target" CHECK ((((("LinkType")::text = 'Cms'::text) AND ("CmsPageSlug" IS NOT NULL) AND (length(TRIM(BOTH FROM "CmsPageSlug")) > 0)) OR ((("LinkType")::text <> 'Cms'::text) AND ("Url" IS NOT NULL) AND (length(TRIM(BOTH FROM "Url")) > 0))))
);


ALTER TABLE public."MarketplaceFooterLinks" OWNER TO postgres;

--
-- TOC entry 443 (class 1259 OID 46566)
-- Name: MarketplaceFooterSections; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."MarketplaceFooterSections" (
    "Id" uuid NOT NULL,
    "Title" character varying(120) NOT NULL,
    "SortOrder" integer DEFAULT 0 NOT NULL,
    "IsActive" boolean DEFAULT true NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "TitleJson" jsonb
);


ALTER TABLE public."MarketplaceFooterSections" OWNER TO postgres;

--
-- TOC entry 358 (class 1259 OID 35430)
-- Name: MarketplaceProfiles; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."MarketplaceProfiles" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "DisplayName" character varying(100) NOT NULL,
    "Bio" text,
    "ProfileImageUrl" text,
    "IsActive" boolean DEFAULT true NOT NULL,
    "CreatedAt" timestamp without time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp without time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "CoverImageUrl" text,
    "Location" text,
    "Skills" text,
    "PortfolioLinks" text
);


ALTER TABLE public."MarketplaceProfiles" OWNER TO postgres;

--
-- TOC entry 482 (class 1259 OID 3866318)
-- Name: MarketplaceProjectionOutboxMessages; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."MarketplaceProjectionOutboxMessages" (
    "Id" uuid NOT NULL,
    "EventId" uuid NOT NULL,
    "ConsumerName" character varying(200) NOT NULL,
    "EventType" character varying(200) NOT NULL,
    "ClrType" character varying(1000) NOT NULL,
    "EntityId" uuid NOT NULL,
    "TenantId" uuid,
    "CorrelationId" character varying(200) NOT NULL,
    "EventVersion" integer NOT NULL,
    "OccurredAtUtc" timestamp with time zone NOT NULL,
    "PayloadJson" text NOT NULL,
    "ProcessedAtUtc" timestamp with time zone,
    "ClaimedUntilUtc" timestamp with time zone,
    "AttemptCount" integer DEFAULT 0 NOT NULL,
    "LastError" character varying(2000),
    "NextRetryAtUtc" timestamp with time zone,
    "IsFailed" boolean DEFAULT false NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."MarketplaceProjectionOutboxMessages" OWNER TO postgres;

--
-- TOC entry 445 (class 1259 OID 46607)
-- Name: MarketplaceSocialLinks; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."MarketplaceSocialLinks" (
    "Id" uuid NOT NULL,
    "Platform" character varying(40) NOT NULL,
    "Url" character varying(1000) NOT NULL,
    "Icon" character varying(80),
    "SortOrder" integer DEFAULT 0 NOT NULL,
    "IsActive" boolean DEFAULT true NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE public."MarketplaceSocialLinks" OWNER TO postgres;

--
-- TOC entry 316 (class 1259 OID 23733)
-- Name: OnboardingChecklists; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."OnboardingChecklists" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "ItemsJson" text NOT NULL,
    "CompletedAtUtc" timestamp with time zone,
    "DismissedAtUtc" timestamp with time zone,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL
);


ALTER TABLE public."OnboardingChecklists" OWNER TO postgres;

--
-- TOC entry 322 (class 1259 OID 25784)
-- Name: OnlineCustomerAuthProviders; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."OnlineCustomerAuthProviders" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "OnlineCustomerId" uuid NOT NULL,
    "Provider" character varying(50) NOT NULL,
    "ProviderUserId" character varying(256) NOT NULL,
    "ProviderEmail" character varying(256) NOT NULL,
    "EmailVerifiedByProvider" boolean DEFAULT false NOT NULL,
    "DisplayName" character varying(256),
    "PictureUrl" character varying(2048),
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "LastUsedAtUtc" timestamp with time zone
);


ALTER TABLE public."OnlineCustomerAuthProviders" OWNER TO postgres;

--
-- TOC entry 320 (class 1259 OID 25738)
-- Name: OnlineCustomerEmailVerifications; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."OnlineCustomerEmailVerifications" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "OnlineCustomerId" uuid NOT NULL,
    "TokenHash" character varying(128) NOT NULL,
    "ExpiresAtUtc" timestamp with time zone NOT NULL,
    "IsUsed" boolean DEFAULT false NOT NULL,
    "UsedAtUtc" timestamp with time zone,
    "CreatedAtUtc" timestamp with time zone NOT NULL
);


ALTER TABLE public."OnlineCustomerEmailVerifications" OWNER TO postgres;

--
-- TOC entry 321 (class 1259 OID 25759)
-- Name: OnlineCustomerRefreshTokens; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."OnlineCustomerRefreshTokens" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "OnlineCustomerId" uuid NOT NULL,
    "TokenHash" character varying(128) NOT NULL,
    "ExpiresAtUtc" timestamp with time zone NOT NULL,
    "IsRevoked" boolean DEFAULT false NOT NULL,
    "RevokedAtUtc" timestamp with time zone,
    "RevokedReason" character varying(256),
    "ReplacedByTokenId" uuid,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "CreatedByIp" character varying(45) NOT NULL,
    "CreatedByUserAgent" character varying(512),
    "CurrentAccessTokenJti" character varying(128),
    "CurrentAccessTokenExpiresAtUtc" timestamp without time zone,
    "IsAccessTokenBlacklisted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."OnlineCustomerRefreshTokens" OWNER TO postgres;

--
-- TOC entry 6819 (class 0 OID 0)
-- Dependencies: 321
-- Name: COLUMN "OnlineCustomerRefreshTokens"."IsAccessTokenBlacklisted"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."OnlineCustomerRefreshTokens"."IsAccessTokenBlacklisted" IS 'Tracks whether the CurrentAccessTokenJti has been successfully blacklisted in Redis. Used to enable retry logic for Redis blacklisting failures.';


--
-- TOC entry 324 (class 1259 OID 25824)
-- Name: OnlineCustomerWishlistItems; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."OnlineCustomerWishlistItems" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "OnlineCustomerId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "IsRemoved" boolean DEFAULT false NOT NULL,
    "RemovedAtUtc" timestamp with time zone
);


ALTER TABLE public."OnlineCustomerWishlistItems" OWNER TO postgres;

--
-- TOC entry 319 (class 1259 OID 25713)
-- Name: OnlineCustomers; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."OnlineCustomers" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Email" character varying(255) NOT NULL,
    "PasswordHash" character varying(255),
    "Provider" character varying(20) DEFAULT 'Local'::character varying NOT NULL,
    "ProviderUserId" character varying(255),
    "DisplayName" character varying(200),
    "Phone" character varying(50),
    "IsActive" boolean DEFAULT true NOT NULL,
    "EmailVerified" boolean DEFAULT false NOT NULL,
    "CreatedAtUtc" timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "LastLoginAtUtc" timestamp with time zone,
    "UpdatedAtUtc" timestamp with time zone,
    "EmailVerifiedAtUtc" timestamp with time zone,
    "NormalizedEmail" text DEFAULT ''::text NOT NULL,
    "PhoneVerified" boolean DEFAULT false NOT NULL,
    "PhoneVerifiedAtUtc" timestamp with time zone,
    "TokensValidAfterUtc" timestamp without time zone
);


ALTER TABLE public."OnlineCustomers" OWNER TO postgres;

--
-- TOC entry 6820 (class 0 OID 0)
-- Dependencies: 319
-- Name: TABLE "OnlineCustomers"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON TABLE public."OnlineCustomers" IS 'Phase 10.3.1: Storefront customer identity - separate from POS Customers. Write model for CQRS architecture.';


--
-- TOC entry 6821 (class 0 OID 0)
-- Dependencies: 319
-- Name: COLUMN "OnlineCustomers"."PhoneVerified"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."OnlineCustomers"."PhoneVerified" IS 'Whether the customer phone has been verified via WhatsApp OTP';


--
-- TOC entry 6822 (class 0 OID 0)
-- Dependencies: 319
-- Name: COLUMN "OnlineCustomers"."PhoneVerifiedAtUtc"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."OnlineCustomers"."PhoneVerifiedAtUtc" IS 'When the phone was verified via WhatsApp OTP';


--
-- TOC entry 6823 (class 0 OID 0)
-- Dependencies: 319
-- Name: COLUMN "OnlineCustomers"."TokensValidAfterUtc"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."OnlineCustomers"."TokensValidAfterUtc" IS 'Durable cutoff timestamp for token validation. Any access token issued before this timestamp MUST be rejected. Set when password is reset or changed. NULL means no cutoff is enforced.';


--
-- TOC entry 305 (class 1259 OID 23469)
-- Name: OrderItems; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."OrderItems" (
    "Id" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "UnitId" uuid,
    "WarehouseId" uuid,
    "SKU" text NOT NULL,
    "ProductName" text NOT NULL,
    "VariantName" text,
    "ImageUrl" text,
    "Quantity" integer NOT NULL,
    "UnitPrice" numeric NOT NULL,
    "DiscountAmount" numeric NOT NULL,
    "TaxAmount" numeric NOT NULL,
    "Total" numeric NOT NULL,
    "CostPrice" numeric NOT NULL,
    "Notes" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "VariantId" uuid,
    "VariantAttributesJson" text,
    "PaidAmount" numeric(18,2) DEFAULT 0 NOT NULL,
    "RefundedAmount" numeric(18,2) DEFAULT 0 NOT NULL,
    "AffiliateCommissionEligible" boolean DEFAULT false NOT NULL,
    "AffiliateCommissionPolicyIdSnapshot" uuid,
    "AffiliateCommissionRateSnapshot" numeric(18,6),
    "AffiliateMaxCommissionPerItemSnapshot" numeric(18,4),
    "AffiliateCommissionBaseAmountSnapshot" numeric(18,4),
    "AffiliateCommissionAmountSnapshot" numeric(18,4),
    "AffiliateCommissionSnapshotCapturedAtUtc" timestamp with time zone,
    CONSTRAINT "CK_OrderItems_AffiliateCommissionSnapshot_Valid" CHECK (((("AffiliateCommissionEligible" = true) AND ("AffiliateCommissionSnapshotCapturedAtUtc" IS NOT NULL) AND ("AffiliateCommissionPolicyIdSnapshot" IS NOT NULL) AND ("AffiliateCommissionRateSnapshot" > (0)::numeric) AND ("AffiliateCommissionRateSnapshot" <= 0.500000) AND ("AffiliateCommissionBaseAmountSnapshot" > (0)::numeric) AND ("AffiliateCommissionAmountSnapshot" > (0)::numeric) AND (("AffiliateMaxCommissionPerItemSnapshot" IS NULL) OR ("AffiliateMaxCommissionPerItemSnapshot" > (0)::numeric))) OR (("AffiliateCommissionEligible" = false) AND ("AffiliateCommissionSnapshotCapturedAtUtc" IS NULL) AND ("AffiliateCommissionPolicyIdSnapshot" IS NULL) AND ("AffiliateCommissionRateSnapshot" IS NULL) AND ("AffiliateCommissionBaseAmountSnapshot" IS NULL) AND ("AffiliateCommissionAmountSnapshot" IS NULL) AND (("AffiliateMaxCommissionPerItemSnapshot" IS NULL) OR ("AffiliateMaxCommissionPerItemSnapshot" = (0)::numeric)))))
);


ALTER TABLE public."OrderItems" OWNER TO postgres;

--
-- TOC entry 469 (class 1259 OID 2287658)
-- Name: OrderRefundLines; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."OrderRefundLines" (
    "Id" uuid NOT NULL,
    "OrderRefundId" uuid NOT NULL,
    "OrderItemId" uuid NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "OrderId" uuid NOT NULL,
    "TenantId" uuid NOT NULL
);


ALTER TABLE public."OrderRefundLines" OWNER TO postgres;

--
-- TOC entry 468 (class 1259 OID 2287634)
-- Name: OrderRefunds; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."OrderRefunds" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "RefundId" character varying(120) NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Currency" character varying(3) NOT NULL,
    "IsFullRefund" boolean DEFAULT false NOT NULL,
    "Reason" character varying(1000) NOT NULL,
    "PaymentMethod" character varying(100) NOT NULL,
    "IsPostSettlementRefund" boolean DEFAULT false NOT NULL,
    "SettlementBatchId" uuid,
    "SettlementType" character varying(32),
    "SettlementId" character varying(200),
    "PayoutBatchId" character varying(200),
    "SettledAtUtc" timestamp with time zone,
    "RefundedAtUtc" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."OrderRefunds" OWNER TO postgres;

--
-- TOC entry 464 (class 1259 OID 2277977)
-- Name: OrderRequestIdempotency; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."OrderRequestIdempotency" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Key" character varying(200) NOT NULL,
    "RequestHash" character varying(64) NOT NULL,
    "OrderId" uuid,
    "ResponseStatusCode" integer,
    "ResponsePayload" text,
    "CompletedAtUtc" timestamp with time zone,
    "ClaimToken" character varying(32),
    "ClaimTimestampUtc" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "SnapshotHash" character varying(64)
);


ALTER TABLE public."OrderRequestIdempotency" OWNER TO postgres;

--
-- TOC entry 304 (class 1259 OID 23427)
-- Name: Orders; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."Orders" (
    "Id" uuid NOT NULL,
    "OrderNumber" text NOT NULL,
    "CustomerId" uuid,
    "GuestEmail" text,
    "GuestPhone" text,
    "GuestName" text,
    "Status" integer NOT NULL,
    "PaymentStatus" integer NOT NULL,
    "ShippingAddress" text NOT NULL,
    "ShippingCity" text,
    "ShippingState" text,
    "ShippingZipCode" text,
    "ShippingCountry" text,
    "BillingAddress" text,
    "BillingCity" text,
    "BillingState" text,
    "BillingZipCode" text,
    "BillingCountry" text,
    "ShippingMethodId" uuid,
    "ShippingCost" numeric NOT NULL,
    "TrackingNumber" text,
    "Carrier" text,
    "Subtotal" numeric NOT NULL,
    "DiscountAmount" numeric NOT NULL,
    "TaxAmount" numeric NOT NULL,
    "Total" numeric NOT NULL,
    "Currency" text NOT NULL,
    "CouponCode" text,
    "CouponId" uuid,
    "PaymentMethod" integer NOT NULL,
    "PaymentReference" text,
    "PaymentGateway" text,
    "PaidAt" timestamp with time zone,
    "CustomerNotes" text,
    "AdminNotes" text,
    "Source" integer NOT NULL,
    "SourceOrderId" text,
    "ShippedAt" timestamp with time zone,
    "DeliveredAt" timestamp with time zone,
    "CancelledAt" timestamp with time zone,
    "CancellationReason" text,
    "WarehouseId" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL,
    "PaymentMethodCode" character varying(50) DEFAULT 'COD'::character varying NOT NULL,
    "PaymentMethodName" character varying(255),
    "PaymentFee" numeric(18,2) DEFAULT 0 NOT NULL,
    "IsPaid" boolean DEFAULT false NOT NULL,
    "EstimatedDeliveryDays" text,
    "ShippingMethodName" text,
    "OnlineCustomerId" uuid,
    "AppliedPromotionId" uuid,
    "AppliedPromotionType" character varying(20),
    "AppliedPromotionName" character varying(200),
    "AppliedPromotionDescription" character varying(500),
    "PromotionDiscountAmount" numeric(18,2) DEFAULT 0 NOT NULL,
    "FulfillmentStatus" integer DEFAULT 1 NOT NULL,
    "FulfillmentMode" integer DEFAULT 0 NOT NULL,
    "FulfillmentCarrierCode" text,
    "ShippingMethodIdSnapshot" uuid,
    "ShippingMethodNameSnapshot" text,
    "IsGuestCheckout" boolean DEFAULT false NOT NULL,
    "AffiliateAccountId" uuid,
    "AffiliateReferralCode" text,
    "PromotionLinkId" uuid,
    "SessionId" character varying(100),
    "IpAddress" character varying(45),
    "ProjectionMutationVersion" bigint DEFAULT 0 NOT NULL,
    "CartId" uuid,
    "CheckoutState" integer DEFAULT 1 NOT NULL,
    "ManualDiscountAmount" numeric(18,2) DEFAULT 0 NOT NULL,
    "PricingSnapshotJson" text,
    "TotalDiscountAmount" numeric(18,2) DEFAULT 0 NOT NULL,
    "FinancialPostingStatus" integer DEFAULT 0 NOT NULL,
    "FinancialPostingPendingAtUtc" timestamp with time zone,
    "FinancialPostedAtUtc" timestamp with time zone,
    "FinancialPostingJournalId" uuid,
    "FinancialPostingFailureReason" character varying(1000),
    "PromotionCalculationFailed" boolean DEFAULT false NOT NULL,
    "PromotionWarningMessage" text,
    CONSTRAINT "CK_Orders_CustomerAuthorityBoundary" CHECK (((("CustomerId" IS NULL) OR ("OnlineCustomerId" IS NULL)) AND (("Source" <> 0) OR ("CustomerId" IS NULL)))),
    CONSTRAINT "CK_Orders_FinancialPostingStatus_Valid" CHECK ((("FinancialPostingStatus" = ANY (ARRAY[0, 1, 2, 3])) AND (("FinancialPostingStatus" <> 1) OR ("FinancialPostingPendingAtUtc" IS NOT NULL)) AND (("FinancialPostingStatus" <> 2) OR (("FinancialPostedAtUtc" IS NOT NULL) AND ("FinancialPostingJournalId" IS NOT NULL))) AND (("FinancialPostingStatus" <> 3) OR ("FinancialPostingFailureReason" IS NOT NULL)))),
    CONSTRAINT "CK_Orders_FulfillmentOwnership" CHECK (((("FulfillmentMode" = 0) AND ("FulfillmentCarrierCode" IS NULL)) OR (("FulfillmentMode" = 1) AND ("FulfillmentCarrierCode" IS NOT NULL))))
);


ALTER TABLE public."Orders" OWNER TO postgres;

--
-- TOC entry 6824 (class 0 OID 0)
-- Dependencies: 304
-- Name: COLUMN "Orders"."PromotionDiscountAmount"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."Orders"."PromotionDiscountAmount" IS 'PHASE: Promotions Engine - Discount from product-level promotions (separate from coupon DiscountAmount)';


--
-- TOC entry 6825 (class 0 OID 0)
-- Dependencies: 304
-- Name: CONSTRAINT "CK_Orders_FulfillmentOwnership" ON "Orders"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON CONSTRAINT "CK_Orders_FulfillmentOwnership" ON public."Orders" IS 'Enforces fulfillment ownership invariant: Manual orders have no carrier code, Carrier orders must have carrier code.';


--
-- TOC entry 310 (class 1259 OID 23556)
-- Name: OutboxMessage; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."OutboxMessage" (
    "SequenceNumber" bigint NOT NULL,
    "EnqueueTime" timestamp with time zone,
    "SentTime" timestamp with time zone NOT NULL,
    "Headers" text,
    "Properties" text,
    "InboxMessageId" uuid,
    "InboxConsumerId" uuid,
    "OutboxId" uuid,
    "MessageId" uuid NOT NULL,
    "ContentType" character varying(256) NOT NULL,
    "MessageType" text NOT NULL,
    "Body" text NOT NULL,
    "ConversationId" uuid,
    "CorrelationId" uuid,
    "InitiatorId" uuid,
    "RequestId" uuid,
    "SourceAddress" character varying(256),
    "DestinationAddress" character varying(256),
    "ResponseAddress" character varying(256),
    "FaultAddress" character varying(256),
    "ExpirationTime" timestamp with time zone
);


ALTER TABLE public."OutboxMessage" OWNER TO postgres;

--
-- TOC entry 309 (class 1259 OID 23555)
-- Name: OutboxMessage_SequenceNumber_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public."OutboxMessage" ALTER COLUMN "SequenceNumber" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."OutboxMessage_SequenceNumber_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 342 (class 1259 OID 34897)
-- Name: OutboxMessages; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."OutboxMessages" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "EventType" character varying(500) NOT NULL,
    "EventPayload" jsonb NOT NULL,
    "TenantId" uuid,
    "AggregateId" uuid,
    "CreatedAtUtc" timestamp without time zone DEFAULT now() NOT NULL,
    "ProcessedAtUtc" timestamp without time zone,
    "RetryCount" integer DEFAULT 0 NOT NULL,
    "LastErrorMessage" character varying(2000),
    "LastErrorAtUtc" timestamp without time zone,
    "EventVersion" integer DEFAULT 0 NOT NULL,
    "RequiresOperatorReconciliation" boolean DEFAULT false NOT NULL,
    "OperatorReconciliationRequiredAtUtc" timestamp with time zone,
    "OperatorReconciliationReason" character varying(2000),
    CONSTRAINT "CK_OutboxMessages_CriticalEvents_AggregateId_Required" CHECK (((("AggregateId" IS NOT NULL) AND ("AggregateId" <> '00000000-0000-0000-0000-000000000000'::uuid)) OR (NOT (((lower(("EventType")::text) ~~ '%order%'::text) OR (lower(("EventType")::text) ~~ '%payment%'::text) OR (lower(("EventType")::text) ~~ '%inventory%'::text) OR (lower(("EventType")::text) ~~ '%stock%'::text) OR (lower(("EventType")::text) ~~ '%product%'::text) OR (lower(("EventType")::text) ~~ '%variant%'::text) OR (lower(("EventType")::text) ~~ '%refund%'::text) OR (lower(("EventType")::text) ~~ '%payout%'::text) OR (lower(("EventType")::text) ~~ '%settlement%'::text) OR (lower(("EventType")::text) ~~ '%commercehub%'::text) OR (lower(("EventType")::text) ~~ '%marketplace%'::text) OR (lower(("EventType")::text) ~~ '%affiliate%'::text) OR (lower(("EventType")::text) ~~ '%coupon%'::text) OR (lower(("EventType")::text) ~~ '%promotion%'::text) OR (lower(("EventType")::text) ~~ '%tenant%'::text) OR (lower(("EventType")::text) ~~ '%storeconfig%'::text) OR (lower(("EventType")::text) ~~ '%shippingmethod%'::text) OR (lower(("EventType")::text) ~~ '%shipment%'::text) OR (lower(("EventType")::text) ~~ '%customer%'::text) OR (lower(("EventType")::text) ~~ '%checkout%'::text) OR (lower(("EventType")::text) ~~ '%ledger%'::text) OR (lower(("EventType")::text) ~~ '%accounting%'::text) OR (lower(("EventType")::text) ~~ '%wallet%'::text) OR (lower(("EventType")::text) ~~ '%cod%'::text) OR (lower(("EventType")::text) ~~ '%currency%'::text) OR (lower(("EventType")::text) ~~ '%providertrust%'::text)) AND (lower(("EventType")::text) !~~ '%affiliatelinkclickedevent%'::text) AND (lower(("EventType")::text) !~~ '%lowstockalertevent%'::text) AND (lower(("EventType")::text) !~~ '%onlinecustomeremailverificationrequestedevent%'::text) AND (lower(("EventType")::text) !~~ '%promotionseenevent%'::text) AND (lower(("EventType")::text) !~~ '%promotionclickedevent%'::text) AND (lower(("EventType")::text) !~~ '%promotionconvertedevent%'::text) AND (lower(("EventType")::text) !~~ '%promotionboostappliedevent%'::text) AND (lower(("EventType")::text) !~~ '%whatsapporderconfirmationrequestedevent%'::text) AND (lower(("EventType")::text) !~~ '%whatsapppaymentapprovalrequestedevent%'::text) AND (lower(("EventType")::text) !~~ '%whatsapppaymentapprovednotificationevent%'::text) AND (lower(("EventType")::text) !~~ '%whatsapppaymentrejectednotificationevent%'::text))))),
    CONSTRAINT "CK_OutboxMessages_CriticalEvents_EventVersion_Required" CHECK ((("EventVersion" > 0) OR (NOT (((lower(("EventType")::text) ~~ '%order%'::text) OR (lower(("EventType")::text) ~~ '%payment%'::text) OR (lower(("EventType")::text) ~~ '%inventory%'::text) OR (lower(("EventType")::text) ~~ '%stock%'::text) OR (lower(("EventType")::text) ~~ '%product%'::text) OR (lower(("EventType")::text) ~~ '%variant%'::text) OR (lower(("EventType")::text) ~~ '%refund%'::text) OR (lower(("EventType")::text) ~~ '%payout%'::text) OR (lower(("EventType")::text) ~~ '%settlement%'::text) OR (lower(("EventType")::text) ~~ '%commercehub%'::text) OR (lower(("EventType")::text) ~~ '%marketplace%'::text) OR (lower(("EventType")::text) ~~ '%affiliate%'::text) OR (lower(("EventType")::text) ~~ '%coupon%'::text) OR (lower(("EventType")::text) ~~ '%promotion%'::text) OR (lower(("EventType")::text) ~~ '%tenant%'::text) OR (lower(("EventType")::text) ~~ '%storeconfig%'::text) OR (lower(("EventType")::text) ~~ '%shippingmethod%'::text) OR (lower(("EventType")::text) ~~ '%shipment%'::text) OR (lower(("EventType")::text) ~~ '%customer%'::text) OR (lower(("EventType")::text) ~~ '%checkout%'::text) OR (lower(("EventType")::text) ~~ '%ledger%'::text) OR (lower(("EventType")::text) ~~ '%accounting%'::text) OR (lower(("EventType")::text) ~~ '%wallet%'::text) OR (lower(("EventType")::text) ~~ '%cod%'::text) OR (lower(("EventType")::text) ~~ '%currency%'::text) OR (lower(("EventType")::text) ~~ '%providertrust%'::text)) AND (lower(("EventType")::text) !~~ '%affiliatelinkclickedevent%'::text) AND (lower(("EventType")::text) !~~ '%lowstockalertevent%'::text) AND (lower(("EventType")::text) !~~ '%onlinecustomeremailverificationrequestedevent%'::text) AND (lower(("EventType")::text) !~~ '%promotionseenevent%'::text) AND (lower(("EventType")::text) !~~ '%promotionclickedevent%'::text) AND (lower(("EventType")::text) !~~ '%promotionconvertedevent%'::text) AND (lower(("EventType")::text) !~~ '%promotionboostappliedevent%'::text) AND (lower(("EventType")::text) !~~ '%whatsapporderconfirmationrequestedevent%'::text) AND (lower(("EventType")::text) !~~ '%whatsapppaymentapprovalrequestedevent%'::text) AND (lower(("EventType")::text) !~~ '%whatsapppaymentapprovednotificationevent%'::text) AND (lower(("EventType")::text) !~~ '%whatsapppaymentrejectednotificationevent%'::text))))),
    CONSTRAINT "CK_OutboxMessages_CriticalEvents_TenantId_Required" CHECK (((("TenantId" IS NOT NULL) AND ("TenantId" <> '00000000-0000-0000-0000-000000000000'::uuid)) OR (NOT (((lower(("EventType")::text) ~~ '%order%'::text) OR (lower(("EventType")::text) ~~ '%payment%'::text) OR (lower(("EventType")::text) ~~ '%inventory%'::text) OR (lower(("EventType")::text) ~~ '%stock%'::text) OR (lower(("EventType")::text) ~~ '%product%'::text) OR (lower(("EventType")::text) ~~ '%variant%'::text) OR (lower(("EventType")::text) ~~ '%refund%'::text) OR (lower(("EventType")::text) ~~ '%payout%'::text) OR (lower(("EventType")::text) ~~ '%settlement%'::text) OR (lower(("EventType")::text) ~~ '%commercehub%'::text) OR (lower(("EventType")::text) ~~ '%marketplace%'::text) OR (lower(("EventType")::text) ~~ '%affiliate%'::text) OR (lower(("EventType")::text) ~~ '%coupon%'::text) OR (lower(("EventType")::text) ~~ '%promotion%'::text) OR (lower(("EventType")::text) ~~ '%tenant%'::text) OR (lower(("EventType")::text) ~~ '%storeconfig%'::text) OR (lower(("EventType")::text) ~~ '%shippingmethod%'::text) OR (lower(("EventType")::text) ~~ '%shipment%'::text) OR (lower(("EventType")::text) ~~ '%customer%'::text) OR (lower(("EventType")::text) ~~ '%checkout%'::text) OR (lower(("EventType")::text) ~~ '%ledger%'::text) OR (lower(("EventType")::text) ~~ '%accounting%'::text) OR (lower(("EventType")::text) ~~ '%wallet%'::text) OR (lower(("EventType")::text) ~~ '%cod%'::text) OR (lower(("EventType")::text) ~~ '%currency%'::text) OR (lower(("EventType")::text) ~~ '%providertrust%'::text)) AND (lower(("EventType")::text) !~~ '%affiliatelinkclickedevent%'::text) AND (lower(("EventType")::text) !~~ '%lowstockalertevent%'::text) AND (lower(("EventType")::text) !~~ '%onlinecustomeremailverificationrequestedevent%'::text) AND (lower(("EventType")::text) !~~ '%promotionseenevent%'::text) AND (lower(("EventType")::text) !~~ '%promotionclickedevent%'::text) AND (lower(("EventType")::text) !~~ '%promotionconvertedevent%'::text) AND (lower(("EventType")::text) !~~ '%promotionboostappliedevent%'::text) AND (lower(("EventType")::text) !~~ '%whatsapporderconfirmationrequestedevent%'::text) AND (lower(("EventType")::text) !~~ '%whatsapppaymentapprovalrequestedevent%'::text) AND (lower(("EventType")::text) !~~ '%whatsapppaymentapprovednotificationevent%'::text) AND (lower(("EventType")::text) !~~ '%whatsapppaymentrejectednotificationevent%'::text)))))
);


ALTER TABLE public."OutboxMessages" OWNER TO postgres;

--
-- TOC entry 6826 (class 0 OID 0)
-- Dependencies: 342
-- Name: TABLE "OutboxMessages"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON TABLE public."OutboxMessages" IS 'Transactional outbox for guaranteed event delivery. Events written in same transaction as domain changes. Phase: Abandoned Cart Hardening.';


--
-- TOC entry 308 (class 1259 OID 23545)
-- Name: OutboxState; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."OutboxState" (
    "OutboxId" uuid NOT NULL,
    "LockId" uuid NOT NULL,
    "RowVersion" bytea,
    "Created" timestamp with time zone NOT NULL,
    "Delivered" timestamp with time zone,
    "LastSequenceNumber" bigint
);


ALTER TABLE public."OutboxState" OWNER TO postgres;

--
-- TOC entry 458 (class 1259 OID 46923)
-- Name: PartyLedgerAdjustments; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."PartyLedgerAdjustments" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "PartyType" integer NOT NULL,
    "PartyId" uuid NOT NULL,
    "TransactionType" integer NOT NULL,
    "AdjustmentDate" timestamp with time zone NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "IdempotencyKey" character varying(100) NOT NULL,
    "ReferenceNumber" character varying(100) NOT NULL,
    "Notes" character varying(1000),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "CreatedBy" uuid,
    "UpdatedBy" uuid
);


ALTER TABLE public."PartyLedgerAdjustments" OWNER TO postgres;

--
-- TOC entry 478 (class 1259 OID 3022527)
-- Name: PaymentCancellationOutboxMessages; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."PaymentCancellationOutboxMessages" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "PaymentIntentId" uuid,
    "SkipPaymentIntentResolution" boolean DEFAULT false CONSTRAINT "PaymentCancellationOutboxMe_SkipPaymentIntentResolutio_not_null" NOT NULL,
    "Reason" character varying(500) NOT NULL,
    "Actor" character varying(100) NOT NULL,
    "ProcessedAtUtc" timestamp with time zone,
    "AttemptCount" integer DEFAULT 0 NOT NULL,
    "LastError" character varying(2000),
    "NextRetryAtUtc" timestamp with time zone,
    "IsFailed" boolean DEFAULT false NOT NULL,
    "CorrelationId" character varying(100),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "ClaimedUntilUtc" timestamp with time zone
);


ALTER TABLE public."PaymentCancellationOutboxMessages" OWNER TO postgres;

--
-- TOC entry 312 (class 1259 OID 23598)
-- Name: PaymentIntents; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."PaymentIntents" (
    "Id" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Amount" numeric NOT NULL,
    "Currency" text NOT NULL,
    "PaymentMethod" integer NOT NULL,
    "Status" integer NOT NULL,
    "Provider" text NOT NULL,
    "ProviderIntentId" text,
    "ProviderPaymentId" text,
    "PaymentReference" text NOT NULL,
    "RedirectUrl" text,
    "WebhookUrl" text,
    "IdempotencyKey" text NOT NULL,
    "AuthorizedAt" timestamp with time zone,
    "CapturedAt" timestamp with time zone,
    "FailedAt" timestamp with time zone,
    "FailureReason" text,
    "FailureCode" text,
    "ProviderMetadata" text,
    "LastWebhookEventId" text,
    "WebhookEventCount" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "WalletType" text,
    "WalletPhoneNumber" text,
    "ProofImageUrl" text,
    "VerifiedAtUtc" timestamp with time zone,
    "VerifiedByAdmin" text,
    "RejectionReason" text,
    "ExpiresAtUtc" timestamp with time zone,
    "ReferenceCode" text,
    "ProofImageExpiresAtUtc" timestamp with time zone,
    "ProofImageDeleted" boolean DEFAULT false NOT NULL,
    "InventoryCommitted" boolean DEFAULT false NOT NULL,
    "ClientSecretEncrypted" text,
    "PayoutBatchId" character varying(200)
);


ALTER TABLE public."PaymentIntents" OWNER TO postgres;

--
-- TOC entry 6827 (class 0 OID 0)
-- Dependencies: 312
-- Name: COLUMN "PaymentIntents"."RedirectUrl"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."PaymentIntents"."RedirectUrl" IS 'Redirect URL for hosted payment pages (e.g., Paymob iframe URL). Should NOT contain sensitive credentials.';


--
-- TOC entry 6828 (class 0 OID 0)
-- Dependencies: 312
-- Name: COLUMN "PaymentIntents"."InventoryCommitted"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."PaymentIntents"."InventoryCommitted" IS 'Whether inventory has been committed for this payment. Used to ensure inventory commitment is recoverable on webhook retry.';


--
-- TOC entry 6829 (class 0 OID 0)
-- Dependencies: 312
-- Name: COLUMN "PaymentIntents"."ClientSecretEncrypted"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."PaymentIntents"."ClientSecretEncrypted" IS 'Encrypted client secret for payment providers (e.g., Stripe client_secret). Encrypted using ASP.NET Core Data Protection API.';


--
-- TOC entry 349 (class 1259 OID 35134)
-- Name: PaymentMethods; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."PaymentMethods" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid NOT NULL,
    "Code" character varying(20) NOT NULL,
    "Name" character varying(100) NOT NULL,
    "NameAr" character varying(100),
    "TargetAccountId" uuid,
    "ClearingAccountId" uuid,
    "FeesAccountId" uuid,
    "IsSystem" boolean DEFAULT false NOT NULL,
    "IsActive" boolean DEFAULT true NOT NULL,
    "DisplayOrder" integer DEFAULT 0 NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."PaymentMethods" OWNER TO postgres;

--
-- TOC entry 6830 (class 0 OID 0)
-- Dependencies: 349
-- Name: TABLE "PaymentMethods"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON TABLE public."PaymentMethods" IS 'Phase 2 - Maps payment types to ledger accounts';


--
-- TOC entry 6831 (class 0 OID 0)
-- Dependencies: 349
-- Name: COLUMN "PaymentMethods"."TargetAccountId"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."PaymentMethods"."TargetAccountId" IS 'Account for immediate settlement (CASH)';


--
-- TOC entry 6832 (class 0 OID 0)
-- Dependencies: 349
-- Name: COLUMN "PaymentMethods"."ClearingAccountId"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."PaymentMethods"."ClearingAccountId" IS 'Account for deferred settlement (CARD, STRIPE)';


--
-- TOC entry 6833 (class 0 OID 0)
-- Dependencies: 349
-- Name: COLUMN "PaymentMethods"."FeesAccountId"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."PaymentMethods"."FeesAccountId" IS 'Expense account for payment fees';


--
-- TOC entry 476 (class 1259 OID 3022478)
-- Name: PaymentSettlementBatches; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."PaymentSettlementBatches" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "SettlementType" character varying(32) NOT NULL,
    "SettlementId" character varying(200) NOT NULL,
    "Provider" character varying(100),
    "PayoutBatchId" character varying(200),
    "GrossAmount" numeric(18,2) NOT NULL,
    "Fees" numeric(18,2) NOT NULL,
    "Currency" character varying(3) NOT NULL,
    "BankReference" character varying(200),
    "OrderCount" integer NOT NULL,
    "SettledAtUtc" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL
);


ALTER TABLE public."PaymentSettlementBatches" OWNER TO postgres;

--
-- TOC entry 477 (class 1259 OID 3022496)
-- Name: PaymentSettlementOrders; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."PaymentSettlementOrders" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "SettlementBatchId" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "PaymentIntentId" uuid,
    "OrderTotal" numeric(18,2) NOT NULL,
    "Currency" character varying(3) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL
);


ALTER TABLE public."PaymentSettlementOrders" OWNER TO postgres;

--
-- TOC entry 314 (class 1259 OID 23653)
-- Name: PaymentWebhookLogs; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."PaymentWebhookLogs" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Provider" text NOT NULL,
    "ProviderEventId" text NOT NULL,
    "EventType" text NOT NULL,
    "PaymentIntentId" uuid,
    "OrderId" uuid,
    "RawPayload" text NOT NULL,
    "Signature" text,
    "SignatureVerified" boolean NOT NULL,
    "ProcessingStatus" integer NOT NULL,
    "ProcessedAt" timestamp with time zone,
    "ProcessingError" text,
    "ProcessingDurationMs" bigint,
    "SourceIp" text,
    "UserAgent" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL
);


ALTER TABLE public."PaymentWebhookLogs" OWNER TO postgres;

--
-- TOC entry 378 (class 1259 OID 36421)
-- Name: PhoneRiskProfiles; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."PhoneRiskProfiles" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "PhoneNumber" character varying(32) NOT NULL,
    "IsBlocked" boolean DEFAULT false NOT NULL,
    "BlockedReason" character varying(500),
    "BlockedByUserId" uuid,
    "BlockedAtUtc" timestamp with time zone,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    "SpamReports" integer DEFAULT 0 NOT NULL
);


ALTER TABLE public."PhoneRiskProfiles" OWNER TO postgres;

--
-- TOC entry 380 (class 1259 OID 36456)
-- Name: PhoneSpamReports; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."PhoneSpamReports" (
    "Id" uuid NOT NULL,
    "PhoneNumber" character varying(32) NOT NULL,
    "TenantId" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "ReportedByUserId" uuid,
    "CreatedAtUtc" timestamp with time zone NOT NULL
);


ALTER TABLE public."PhoneSpamReports" OWNER TO postgres;

--
-- TOC entry 336 (class 1259 OID 34634)
-- Name: PixelTrackingConfigs; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."PixelTrackingConfigs" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "IsEnabled" boolean NOT NULL,
    "Strategy" integer NOT NULL,
    "Value" numeric(18,4) NOT NULL,
    "AppliesToJson" text NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedBy" text
);


ALTER TABLE public."PixelTrackingConfigs" OWNER TO postgres;

--
-- TOC entry 6834 (class 0 OID 0)
-- Dependencies: 336
-- Name: TABLE "PixelTrackingConfigs"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON TABLE public."PixelTrackingConfigs" IS 'Configuration for Pixel Conversion Value Override (Phase M1).';


--
-- TOC entry 6835 (class 0 OID 0)
-- Dependencies: 336
-- Name: COLUMN "PixelTrackingConfigs"."IsEnabled"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."PixelTrackingConfigs"."IsEnabled" IS 'Whether value override is enabled.';


--
-- TOC entry 6836 (class 0 OID 0)
-- Dependencies: 336
-- Name: COLUMN "PixelTrackingConfigs"."Strategy"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."PixelTrackingConfigs"."Strategy" IS 'Strategy for calculating the override value (1=Percentage, 2=Fixed).';


--
-- TOC entry 6837 (class 0 OID 0)
-- Dependencies: 336
-- Name: COLUMN "PixelTrackingConfigs"."Value"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."PixelTrackingConfigs"."Value" IS 'The value parameter for the strategy.';


--
-- TOC entry 6838 (class 0 OID 0)
-- Dependencies: 336
-- Name: COLUMN "PixelTrackingConfigs"."AppliesToJson"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."PixelTrackingConfigs"."AppliesToJson" IS 'List of providers this override applies to (JSON).';


--
-- TOC entry 453 (class 1259 OID 46860)
-- Name: PosSalesInvoiceNumberSeq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public."PosSalesInvoiceNumberSeq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public."PosSalesInvoiceNumberSeq" OWNER TO postgres;

--
-- TOC entry 450 (class 1259 OID 46723)
-- Name: ProcessedEvents; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."ProcessedEvents" (
    "Id" uuid NOT NULL,
    "EventId" uuid NOT NULL,
    "ConsumerName" character varying(200) NOT NULL,
    "EventType" character varying(200) NOT NULL,
    "EntityId" uuid NOT NULL,
    "TenantId" uuid,
    "CorrelationId" character varying(200) NOT NULL,
    "ProcessedAtUtc" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."ProcessedEvents" OWNER TO postgres;

--
-- TOC entry 367 (class 1259 OID 35896)
-- Name: ProcessedTrustEvent; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."ProcessedTrustEvent" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "EventId" uuid NOT NULL,
    "EventType" character varying(100) NOT NULL,
    "ProviderProfileId" uuid NOT NULL,
    "ProcessedAt" timestamp without time zone DEFAULT (now() AT TIME ZONE 'utc'::text) NOT NULL,
    "CreatedAt" timestamp without time zone DEFAULT (now() AT TIME ZONE 'utc'::text) NOT NULL,
    "UpdatedAt" timestamp without time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."ProcessedTrustEvent" OWNER TO postgres;

--
-- TOC entry 381 (class 1259 OID 36525)
-- Name: ProductMedia; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."ProductMedia" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "MediaType" character varying(20) NOT NULL,
    "Url" text NOT NULL,
    "Provider" character varying(20),
    "Position" integer NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL
);


ALTER TABLE public."ProductMedia" OWNER TO postgres;

--
-- TOC entry 337 (class 1259 OID 34649)
-- Name: ProductPixelOverrides; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."ProductPixelOverrides" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "IsEnabled" boolean DEFAULT false NOT NULL,
    "Strategy" integer DEFAULT 1 NOT NULL,
    "Value" numeric(18,2) DEFAULT 100 NOT NULL,
    "ApplyToMeta" boolean DEFAULT true NOT NULL,
    "ApplyToGoogle" boolean DEFAULT true NOT NULL,
    "ApplyToTikTok" boolean DEFAULT true NOT NULL,
    "UpdatedAtUtc" timestamp without time zone DEFAULT now() NOT NULL,
    "UpdatedBy" uuid NOT NULL
);


ALTER TABLE public."ProductPixelOverrides" OWNER TO postgres;

--
-- TOC entry 481 (class 1259 OID 3795739)
-- Name: ProductRequestIdempotency; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."ProductRequestIdempotency" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Key" character varying(200) NOT NULL,
    "Operation" character varying(32) NOT NULL,
    "ProductId" uuid NOT NULL,
    "ProductVariantId" uuid,
    "RequestFingerprint" character varying(64) NOT NULL,
    "ResponseStatusCode" integer,
    "ResponseJson" text,
    "CompletedAtUtc" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."ProductRequestIdempotency" OWNER TO postgres;

--
-- TOC entry 451 (class 1259 OID 46750)
-- Name: ProductReviews; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."ProductReviews" (
    "Id" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Rating" integer NOT NULL,
    "Title" character varying(200),
    "Comment" character varying(4000) NOT NULL,
    "IsVerifiedPurchase" boolean NOT NULL,
    "Status" integer DEFAULT 0 NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    CONSTRAINT "CK_ProductReviews_Rating_Range" CHECK ((("Rating" >= 1) AND ("Rating" <= 5)))
);


ALTER TABLE public."ProductReviews" OWNER TO postgres;

--
-- TOC entry 289 (class 1259 OID 21519)
-- Name: ProductUnits; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."ProductUnits" (
    "Id" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "UnitId" uuid NOT NULL,
    "ConversionFactor" numeric NOT NULL,
    "PurchasePrice" numeric,
    "SellingPrice" numeric,
    "Barcode" text,
    "IsDefault" boolean NOT NULL,
    "IsBaseUnit" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL
);


ALTER TABLE public."ProductUnits" OWNER TO postgres;

--
-- TOC entry 326 (class 1259 OID 25931)
-- Name: ProductVariantAttributes; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."ProductVariantAttributes" (
    "Id" uuid NOT NULL,
    "VariantId" uuid NOT NULL,
    "Name" character varying(100) NOT NULL,
    "Value" character varying(255) NOT NULL,
    "SortOrder" integer DEFAULT 0 NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."ProductVariantAttributes" OWNER TO postgres;

--
-- TOC entry 327 (class 1259 OID 25951)
-- Name: ProductVariantStocks; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."ProductVariantStocks" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "VariantId" uuid NOT NULL,
    "WarehouseId" uuid NOT NULL,
    "Quantity" numeric(18,2) DEFAULT 0 NOT NULL,
    "ReservedQuantity" numeric(18,2) DEFAULT 0 NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "DamagedQuantity" numeric(18,2) DEFAULT 0 NOT NULL,
    "ProjectionMutationVersion" bigint DEFAULT 0 NOT NULL,
    CONSTRAINT "CK_ProductVariantStocks_DamagedQuantity_NonNegative" CHECK (("DamagedQuantity" >= (0)::numeric)),
    CONSTRAINT "CK_ProductVariantStocks_Quantity_NonNegative" CHECK (("Quantity" >= (0)::numeric)),
    CONSTRAINT "CK_ProductVariantStocks_ReservedQuantity_NonNegative" CHECK (("ReservedQuantity" >= (0)::numeric)),
    CONSTRAINT "CK_ProductVariantStocks_ReservedQuantity_Within_Quantity" CHECK (("Quantity" >= "ReservedQuantity"))
);


ALTER TABLE public."ProductVariantStocks" OWNER TO postgres;

--
-- TOC entry 241 (class 1259 OID 20255)
-- Name: ProductVariants; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."ProductVariants" (
    "Id" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "SKU" character varying(100),
    "Barcode" character varying(100),
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL,
    "Price" numeric(18,2) DEFAULT 0 NOT NULL,
    "OnlinePrice" numeric(18,2),
    "CostPrice" numeric(18,2) DEFAULT 0 NOT NULL,
    "IsDefault" boolean DEFAULT false NOT NULL,
    "ImageUrl" character varying(500),
    "SortOrder" integer DEFAULT 0 NOT NULL,
    "MediaId" uuid,
    "Weight" numeric(18,3),
    "StockUpdatedAtUtc" timestamp with time zone,
    "ProjectionMutationVersion" bigint DEFAULT 0 NOT NULL
);


ALTER TABLE public."ProductVariants" OWNER TO postgres;

--
-- TOC entry 235 (class 1259 OID 20095)
-- Name: Products; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."Products" (
    "Id" uuid NOT NULL,
    "Name" character varying(300) NOT NULL,
    "Barcode" character varying(100),
    "SKU" character varying(100),
    "Description" text,
    "CategoryId" uuid NOT NULL,
    "PurchasePrice" numeric(18,2) NOT NULL,
    "SalePrice" numeric(18,2) NOT NULL,
    "PriceIncludesTax" boolean NOT NULL,
    "TaxRate" numeric(5,2) NOT NULL,
    "Unit" character varying(50) NOT NULL,
    "MinimumStockLevel" numeric(18,2) NOT NULL,
    "Images" text,
    "IsActive" boolean NOT NULL,
    "TrackStock" boolean NOT NULL,
    "Notes" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "ReorderPoint" numeric DEFAULT 0.0 NOT NULL,
    "ReorderQuantity" numeric DEFAULT 0.0 NOT NULL,
    "TrackBatchNumbers" boolean DEFAULT false NOT NULL,
    "TrackExpiry" boolean DEFAULT false NOT NULL,
    "AllowBackorder" boolean DEFAULT false NOT NULL,
    "AmazonASIN" text,
    "Brand" text,
    "DescriptionAr" text,
    "DimensionUnit" text DEFAULT ''::text NOT NULL,
    "Height" numeric DEFAULT 0.0 NOT NULL,
    "IsFeatured" boolean DEFAULT false NOT NULL,
    "IsOnlineVisible" boolean DEFAULT false NOT NULL,
    "IsSearchable" boolean DEFAULT false NOT NULL,
    "LastSyncedAt" timestamp with time zone,
    "Length" numeric DEFAULT 0.0 NOT NULL,
    "LowStockThreshold" integer DEFAULT 0 NOT NULL,
    "Manufacturer" text,
    "MetaDescription" text,
    "MetaKeywords" text,
    "MetaTitle" text,
    "NameAr" text,
    "PublishStatus" integer DEFAULT 0 NOT NULL,
    "RelatedProductIds" text,
    "Slug" text,
    "Tags" text,
    "VideoUrl" text,
    "Weight" numeric DEFAULT 0.0 NOT NULL,
    "WeightUnit" text DEFAULT ''::text NOT NULL,
    "Width" numeric DEFAULT 0.0 NOT NULL,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL,
    "OnlinePrice" numeric,
    "HasVariants" boolean DEFAULT false NOT NULL,
    "ProductTemplate" character varying(100),
    "FeaturesJson" jsonb,
    "FaqJson" jsonb,
    "TagsJson" jsonb,
    "AiGenerated" boolean DEFAULT false NOT NULL,
    "AiMetadataJson" jsonb,
    "ProjectionMutationVersion" bigint DEFAULT 0 NOT NULL
);


ALTER TABLE public."Products" OWNER TO postgres;

--
-- TOC entry 6839 (class 0 OID 0)
-- Dependencies: 235
-- Name: COLUMN "Products"."OnlinePrice"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."Products"."OnlinePrice" IS 'Online/Storefront price for e-commerce display. If NULL or 0, falls back to SalePrice. This is the source of truth for storefront pricing.';


--
-- TOC entry 437 (class 1259 OID 38147)
-- Name: PromoCodeUsages; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."PromoCodeUsages" (
    "Id" uuid NOT NULL,
    "PromoCodeId" uuid NOT NULL,
    "MarketplaceOrderId" uuid NOT NULL,
    "CustomerId" uuid,
    "PromoCodeSnapshot" character varying(64) NOT NULL,
    "DiscountAmount" numeric(18,2) NOT NULL,
    "EligibleBaseCommissionAmount" numeric(18,2) NOT NULL,
    "CorrelationId" character varying(200),
    "AppliedAtUtc" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    CONSTRAINT "CK_PromoCodeUsages_DiscountAmount" CHECK (("DiscountAmount" >= (0)::numeric)),
    CONSTRAINT "CK_PromoCodeUsages_EligibleBaseCommissionAmount" CHECK (("EligibleBaseCommissionAmount" >= (0)::numeric))
);


ALTER TABLE public."PromoCodeUsages" OWNER TO postgres;

--
-- TOC entry 436 (class 1259 OID 38097)
-- Name: PromoCodes; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."PromoCodes" (
    "Id" uuid NOT NULL,
    "Code" character varying(64) NOT NULL,
    "DiscountType" integer NOT NULL,
    "DiscountValue" numeric(18,2) NOT NULL,
    "MaxDiscountAmount" numeric(18,2),
    "UsageLimit" integer,
    "UsedCount" integer DEFAULT 0 NOT NULL,
    "ExpiryDate" timestamp with time zone,
    "IsActive" boolean DEFAULT true NOT NULL,
    "VendorId" uuid,
    "CategoryId" uuid,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "MaxUsagePerCustomer" integer,
    CONSTRAINT "CK_PromoCodes_Code_Uppercase" CHECK ((("Code")::text = upper(("Code")::text))),
    CONSTRAINT "CK_PromoCodes_DiscountType" CHECK (("DiscountType" = ANY (ARRAY[0, 1]))),
    CONSTRAINT "CK_PromoCodes_DiscountValue" CHECK (("DiscountValue" > (0)::numeric)),
    CONSTRAINT "CK_PromoCodes_MaxDiscountAmount" CHECK ((("MaxDiscountAmount" IS NULL) OR ("MaxDiscountAmount" > (0)::numeric))),
    CONSTRAINT "CK_PromoCodes_MaxUsagePerCustomer" CHECK ((("MaxUsagePerCustomer" IS NULL) OR ("MaxUsagePerCustomer" > 0))),
    CONSTRAINT "CK_PromoCodes_MaxUsagePerCustomerWithinUsageLimit" CHECK ((("UsageLimit" IS NULL) OR ("MaxUsagePerCustomer" IS NULL) OR ("MaxUsagePerCustomer" <= "UsageLimit"))),
    CONSTRAINT "CK_PromoCodes_Scope" CHECK (((
CASE
    WHEN ("VendorId" IS NOT NULL) THEN 1
    ELSE 0
END +
CASE
    WHEN ("CategoryId" IS NOT NULL) THEN 1
    ELSE 0
END) <= 1)),
    CONSTRAINT "CK_PromoCodes_UsageLimit" CHECK ((("UsageLimit" IS NULL) OR ("UsageLimit" > 0))),
    CONSTRAINT "CK_PromoCodes_UsedCount" CHECK (("UsedCount" >= 0))
);


ALTER TABLE public."PromoCodes" OWNER TO postgres;

--
-- TOC entry 331 (class 1259 OID 26167)
-- Name: PromotionUsages; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."PromotionUsages" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "PromotionId" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "CustomerId" uuid,
    "PromotionName" character varying(200) NOT NULL,
    "DiscountAmount" numeric(18,2) NOT NULL,
    "OrderSubtotal" numeric(18,2) NOT NULL,
    "AppliedToItemsJson" text,
    "AppliedAtUtc" timestamp with time zone DEFAULT now() NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "CreatedBy" uuid,
    "UpdatedBy" uuid
);


ALTER TABLE public."PromotionUsages" OWNER TO postgres;

--
-- TOC entry 330 (class 1259 OID 26134)
-- Name: Promotions; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."Promotions" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Description" character varying(500),
    "DiscountType" integer DEFAULT 0 NOT NULL,
    "DiscountValue" numeric(18,2) NOT NULL,
    "MaxDiscountAmount" numeric(18,2),
    "Scope" integer DEFAULT 0 NOT NULL,
    "AppliesToId" uuid,
    "StartDate" timestamp with time zone NOT NULL,
    "EndDate" timestamp with time zone NOT NULL,
    "Priority" integer DEFAULT 0 NOT NULL,
    "IsActive" boolean DEFAULT true NOT NULL,
    "IsExclusive" boolean DEFAULT false NOT NULL,
    "MinOrderAmount" numeric(18,2),
    "MaxUsage" integer,
    "CurrentUsage" integer DEFAULT 0 NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "BannerStyle" character varying(50) DEFAULT 'gradient'::character varying NOT NULL,
    "SearchBoostOverride" double precision,
    CONSTRAINT "CK_Promotions_DiscountType" CHECK (("DiscountType" = ANY (ARRAY[0, 1]))),
    CONSTRAINT "CK_Promotions_DiscountValue_FixedAmount" CHECK ((("DiscountType" <> 1) OR ("DiscountValue" <= 1000000.00))),
    CONSTRAINT "CK_Promotions_DiscountValue_Percentage" CHECK ((("DiscountType" <> 0) OR ("DiscountValue" <= (100)::numeric))),
    CONSTRAINT "CK_Promotions_DiscountValue_Positive" CHECK (("DiscountValue" > (0)::numeric))
);


ALTER TABLE public."Promotions" OWNER TO postgres;

--
-- TOC entry 6840 (class 0 OID 0)
-- Dependencies: 330
-- Name: COLUMN "Promotions"."SearchBoostOverride"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."Promotions"."SearchBoostOverride" IS 'Manual boost override for merchandising control. When set (non-null), adds to calculated promotion boost in Elasticsearch search ranking. Recommended range: 1-50. Capped at tenant MaxTotalBoost.';


--
-- TOC entry 363 (class 1259 OID 35666)
-- Name: ProviderTrustAggregates; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."ProviderTrustAggregates" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid NOT NULL,
    "ProviderProfileId" uuid NOT NULL,
    "TotalReviews" integer DEFAULT 0 NOT NULL,
    "SumRatings" integer DEFAULT 0 NOT NULL,
    "AverageRating" numeric(3,2) DEFAULT 0.00 NOT NULL,
    "FiveStarCount" integer DEFAULT 0 NOT NULL,
    "FourStarCount" integer DEFAULT 0 NOT NULL,
    "ThreeStarCount" integer DEFAULT 0 NOT NULL,
    "TwoStarCount" integer DEFAULT 0 NOT NULL,
    "OneStarCount" integer DEFAULT 0 NOT NULL,
    "LastReviewAt" timestamp without time zone,
    "Version" integer DEFAULT 1 NOT NULL,
    "CreatedAt" timestamp without time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp without time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "MarketingReviewCount" integer DEFAULT 0 NOT NULL,
    "PosVerifiedCount" integer DEFAULT 0 NOT NULL,
    "TotalVerifiedRevenue" numeric(18,2) DEFAULT 0 NOT NULL,
    "TotalVerifiedOrders" integer DEFAULT 0 NOT NULL,
    "MarketingWeightedScore" numeric(18,2) DEFAULT 0 NOT NULL,
    CONSTRAINT "CHK_ProviderTrustAggregates_AverageRating" CHECK ((("AverageRating" >= 0.00) AND ("AverageRating" <= 5.00))),
    CONSTRAINT "CHK_ProviderTrustAggregates_StarCounts" CHECK ((("FiveStarCount" >= 0) AND ("FourStarCount" >= 0) AND ("ThreeStarCount" >= 0) AND ("TwoStarCount" >= 0) AND ("OneStarCount" >= 0))),
    CONSTRAINT "CHK_ProviderTrustAggregates_StarCountsSum" CHECK (("TotalReviews" = (((("FiveStarCount" + "FourStarCount") + "ThreeStarCount") + "TwoStarCount") + "OneStarCount"))),
    CONSTRAINT "CHK_ProviderTrustAggregates_SumRatings" CHECK (("SumRatings" >= 0)),
    CONSTRAINT "CHK_ProviderTrustAggregates_TotalReviews" CHECK (("TotalReviews" >= 0)),
    CONSTRAINT "CHK_ProviderTrustAggregates_Version" CHECK (("Version" > 0))
);


ALTER TABLE public."ProviderTrustAggregates" OWNER TO postgres;

--
-- TOC entry 364 (class 1259 OID 35720)
-- Name: ProviderTrustSummary; Type: VIEW; Schema: public; Owner: postgres
--

CREATE VIEW public."ProviderTrustSummary" AS
 SELECT pta."ProviderProfileId",
    mp."DisplayName" AS "ProviderName",
    pta."TotalReviews",
    pta."AverageRating",
    pta."FiveStarCount",
    pta."FourStarCount",
    pta."ThreeStarCount",
    pta."TwoStarCount",
    pta."OneStarCount",
    pta."LastReviewAt",
        CASE
            WHEN (pta."TotalReviews" < 5) THEN 'New'::text
            WHEN ((pta."AverageRating" >= 4.6) AND (pta."TotalReviews" >= 50)) THEN 'TopRated'::text
            WHEN ((pta."AverageRating" >= 4.3) AND (pta."TotalReviews" >= 20)) THEN 'Trusted'::text
            WHEN ((pta."AverageRating" >= 4.0) AND (pta."TotalReviews" >= 5)) THEN 'Rising'::text
            ELSE 'Standard'::text
        END AS "TrustBadge"
   FROM (public."ProviderTrustAggregates" pta
     JOIN public."MarketplaceProfiles" mp ON ((pta."ProviderProfileId" = mp."Id")))
  WHERE ((pta."IsDeleted" = false) AND (mp."IsDeleted" = false));


ALTER VIEW public."ProviderTrustSummary" OWNER TO postgres;

--
-- TOC entry 244 (class 1259 OID 20312)
-- Name: PurchaseInvoiceItems; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."PurchaseInvoiceItems" (
    "Id" uuid NOT NULL,
    "PurchaseInvoiceId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "ProductVariantId" uuid,
    "Quantity" numeric NOT NULL,
    "UnitPrice" numeric NOT NULL,
    "DiscountAmount" numeric NOT NULL,
    "TaxRate" numeric NOT NULL,
    "TaxAmount" numeric NOT NULL,
    "TotalAmount" numeric NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "BaseQuantity" numeric DEFAULT 0.0 NOT NULL,
    "ConversionFactor" numeric DEFAULT 1.0 NOT NULL,
    "UnitId" uuid
);


ALTER TABLE public."PurchaseInvoiceItems" OWNER TO postgres;

--
-- TOC entry 242 (class 1259 OID 20274)
-- Name: PurchaseInvoicePayments; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."PurchaseInvoicePayments" (
    "Id" uuid NOT NULL,
    "PurchaseInvoiceId" uuid NOT NULL,
    "PaymentDate" timestamp with time zone NOT NULL,
    "Amount" numeric NOT NULL,
    "PaymentMethod" integer NOT NULL,
    "ReferenceNumber" text,
    "Notes" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL
);


ALTER TABLE public."PurchaseInvoicePayments" OWNER TO postgres;

--
-- TOC entry 238 (class 1259 OID 20162)
-- Name: PurchaseInvoices; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."PurchaseInvoices" (
    "Id" uuid NOT NULL,
    "InvoiceNumber" text NOT NULL,
    "InvoiceDate" timestamp with time zone NOT NULL,
    "SupplierId" uuid NOT NULL,
    "WarehouseId" uuid NOT NULL,
    "Status" integer NOT NULL,
    "Subtotal" numeric NOT NULL,
    "DiscountAmount" numeric NOT NULL,
    "DiscountPercentage" numeric NOT NULL,
    "TaxAmount" numeric NOT NULL,
    "TotalAmount" numeric NOT NULL,
    "PaidAmount" numeric NOT NULL,
    "PaymentMethod" integer,
    "Notes" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "DueDate" timestamp with time zone,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL,
    "AccountingMutationVersion" integer DEFAULT 0 NOT NULL
);


ALTER TABLE public."PurchaseInvoices" OWNER TO postgres;

--
-- TOC entry 454 (class 1259 OID 46864)
-- Name: PurchaseRequestIdempotency; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."PurchaseRequestIdempotency" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid NOT NULL,
    "Key" character varying(255) NOT NULL,
    "Operation" character varying(32) NOT NULL,
    "PurchaseInvoiceId" uuid,
    "PurchaseAttemptId" character varying(200),
    "RequestFingerprint" character varying(64) NOT NULL,
    "Status" character varying(32) NOT NULL,
    "ResponseStatusCode" integer,
    "ResponseJson" text,
    "CompletedAtUtc" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."PurchaseRequestIdempotency" OWNER TO postgres;

--
-- TOC entry 455 (class 1259 OID 46884)
-- Name: PurchaseReturnRequestIdempotency; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."PurchaseReturnRequestIdempotency" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid NOT NULL,
    "Key" character varying(255) NOT NULL,
    "PurchaseAttemptId" character varying(200),
    "RequestFingerprint" character varying(64) NOT NULL,
    "ReturnId" uuid,
    "ResponseStatusCode" integer,
    "ResponseJson" text,
    "CompletedAtUtc" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."PurchaseReturnRequestIdempotency" OWNER TO postgres;

--
-- TOC entry 270 (class 1259 OID 21007)
-- Name: ReceiptSettings; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."ReceiptSettings" (
    "Id" uuid NOT NULL,
    "BusinessName" text NOT NULL,
    "LogoUrl" text,
    "TaxId" text,
    "Phone" text,
    "Email" text,
    "Address" text,
    "FooterText" text,
    "ShowLogo" boolean NOT NULL,
    "ShowTaxId" boolean NOT NULL,
    "ReceiptWidth" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL
);


ALTER TABLE public."ReceiptSettings" OWNER TO postgres;

--
-- TOC entry 357 (class 1259 OID 35393)
-- Name: RejectedCarrierWebhookEvents; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."RejectedCarrierWebhookEvents" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "OrderNumber" text NOT NULL,
    "CarrierCode" text NOT NULL,
    "Payload" text NOT NULL,
    "RejectionReason" text NOT NULL,
    "RejectedAt" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL
);


ALTER TABLE public."RejectedCarrierWebhookEvents" OWNER TO postgres;

--
-- TOC entry 245 (class 1259 OID 20345)
-- Name: ReturnItems; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."ReturnItems" (
    "Id" uuid NOT NULL,
    "ReturnId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "ProductVariantId" uuid,
    "Quantity" numeric NOT NULL,
    "UnitPrice" numeric NOT NULL,
    "TaxRate" numeric NOT NULL,
    "TaxAmount" numeric NOT NULL,
    "TotalAmount" numeric NOT NULL,
    "Reason" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "BaseQuantity" numeric DEFAULT 0.0 NOT NULL,
    "ConversionFactor" numeric DEFAULT 1.0 NOT NULL,
    "UnitId" uuid
);


ALTER TABLE public."ReturnItems" OWNER TO postgres;

--
-- TOC entry 239 (class 1259 OID 20193)
-- Name: Returns; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."Returns" (
    "Id" uuid NOT NULL,
    "ReturnNumber" text NOT NULL,
    "ReturnDate" timestamp with time zone NOT NULL,
    "ReturnType" integer NOT NULL,
    "OriginalInvoiceId" uuid,
    "OriginalInvoiceNumber" text,
    "CustomerId" uuid,
    "SupplierId" uuid,
    "WarehouseId" uuid NOT NULL,
    "Subtotal" numeric NOT NULL,
    "TaxAmount" numeric NOT NULL,
    "TotalAmount" numeric NOT NULL,
    "Reason" text,
    "Notes" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL
);


ALTER TABLE public."Returns" OWNER TO postgres;

--
-- TOC entry 465 (class 1259 OID 2287504)
-- Name: RevokedCustomerAccessTokens; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."RevokedCustomerAccessTokens" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "Jti" character varying(450) NOT NULL,
    "CustomerId" uuid NOT NULL,
    "RevokedAt" timestamp without time zone DEFAULT (now() AT TIME ZONE 'UTC'::text) NOT NULL,
    "ExpiresAt" timestamp without time zone NOT NULL,
    "Reason" character varying(100)
);


ALTER TABLE public."RevokedCustomerAccessTokens" OWNER TO postgres;

--
-- TOC entry 466 (class 1259 OID 2287528)
-- Name: RevokedStorefrontAccessTokens; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."RevokedStorefrontAccessTokens" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "OnlineCustomerId" uuid,
    "Jti" character varying(128) NOT NULL,
    "RevokedAtUtc" timestamp without time zone NOT NULL,
    "ExpiresAtUtc" timestamp without time zone NOT NULL,
    "Reason" character varying(256)
);


ALTER TABLE public."RevokedStorefrontAccessTokens" OWNER TO postgres;

--
-- TOC entry 246 (class 1259 OID 20377)
-- Name: SalesInvoiceItems; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."SalesInvoiceItems" (
    "Id" uuid NOT NULL,
    "SalesInvoiceId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "ProductVariantId" uuid,
    "Quantity" numeric NOT NULL,
    "UnitPrice" numeric NOT NULL,
    "DiscountAmount" numeric NOT NULL,
    "TaxRate" numeric NOT NULL,
    "TaxAmount" numeric NOT NULL,
    "TotalAmount" numeric NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "BaseQuantity" numeric DEFAULT 0.0 NOT NULL,
    "ConversionFactor" numeric DEFAULT 1.0 NOT NULL,
    "UnitId" uuid
);


ALTER TABLE public."SalesInvoiceItems" OWNER TO postgres;

--
-- TOC entry 243 (class 1259 OID 20293)
-- Name: SalesInvoicePayments; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."SalesInvoicePayments" (
    "Id" uuid NOT NULL,
    "SalesInvoiceId" uuid NOT NULL,
    "PaymentDate" timestamp with time zone NOT NULL,
    "Amount" numeric NOT NULL,
    "PaymentMethod" integer NOT NULL,
    "ReferenceNumber" text,
    "Notes" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL
);


ALTER TABLE public."SalesInvoicePayments" OWNER TO postgres;

--
-- TOC entry 240 (class 1259 OID 20225)
-- Name: SalesInvoices; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."SalesInvoices" (
    "Id" uuid NOT NULL,
    "InvoiceNumber" text NOT NULL,
    "InvoiceDate" timestamp with time zone NOT NULL,
    "CustomerId" uuid,
    "WarehouseId" uuid NOT NULL,
    "Status" integer NOT NULL,
    "Subtotal" numeric NOT NULL,
    "DiscountAmount" numeric NOT NULL,
    "DiscountPercentage" numeric NOT NULL,
    "TaxAmount" numeric NOT NULL,
    "TotalAmount" numeric NOT NULL,
    "PaidAmount" numeric NOT NULL,
    "PaymentMethod" integer,
    "CashAmount" numeric,
    "CardAmount" numeric,
    "Notes" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "SalesRepresentativeId" uuid,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL,
    "AccountingMutationVersion" integer DEFAULT 0 NOT NULL
);


ALTER TABLE public."SalesInvoices" OWNER TO postgres;

--
-- TOC entry 279 (class 1259 OID 21290)
-- Name: SalesRepresentatives; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."SalesRepresentatives" (
    "Id" uuid NOT NULL,
    "Name" text NOT NULL,
    "Phone" text,
    "IsActive" boolean NOT NULL,
    "MonthlyTarget" numeric,
    "CommissionPercentage" numeric,
    "Notes" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL
);


ALTER TABLE public."SalesRepresentatives" OWNER TO postgres;

--
-- TOC entry 456 (class 1259 OID 46903)
-- Name: SalesRequestIdempotency; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."SalesRequestIdempotency" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Key" character varying(200) NOT NULL,
    "Operation" character varying(32) NOT NULL,
    "RequestHash" character varying(64) NOT NULL,
    "ResponseStatusCode" integer,
    "ResponseJson" text,
    "CompletedAtUtc" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "ClaimToken" character varying(32),
    "ClaimTimestampUtc" timestamp with time zone
);


ALTER TABLE public."SalesRequestIdempotency" OWNER TO postgres;

--
-- TOC entry 388 (class 1259 OID 36756)
-- Name: SearchClicks; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."SearchClicks" (
    "Id" uuid NOT NULL,
    "QueryText" character varying(500) NOT NULL,
    "ProductId" uuid NOT NULL,
    "VariantId" uuid,
    "Position" integer NOT NULL,
    "ClickedAt" timestamp with time zone NOT NULL,
    "SessionId" character varying(100),
    "TenantId" uuid,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."SearchClicks" OWNER TO postgres;

--
-- TOC entry 393 (class 1259 OID 36872)
-- Name: SearchFilterEvents; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."SearchFilterEvents" (
    "Id" uuid NOT NULL,
    "FilterKey" character varying(200) NOT NULL,
    "FilterValue" character varying(500) NOT NULL,
    "QueryText" character varying(500) NOT NULL,
    "OccurredAt" timestamp with time zone NOT NULL,
    "SessionId" character varying(100),
    "TenantId" uuid,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."SearchFilterEvents" OWNER TO postgres;

--
-- TOC entry 389 (class 1259 OID 36772)
-- Name: SearchQueries; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."SearchQueries" (
    "Id" uuid NOT NULL,
    "QueryText" character varying(500) NOT NULL,
    "ResultCount" bigint NOT NULL,
    "SearchedAt" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."SearchQueries" OWNER TO postgres;

--
-- TOC entry 390 (class 1259 OID 36787)
-- Name: SearchSynonyms; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."SearchSynonyms" (
    "Id" uuid NOT NULL,
    "Term" character varying(200) NOT NULL,
    "SynonymsJson" text NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "Direction" character varying(20) DEFAULT 'one_way'::character varying NOT NULL
);


ALTER TABLE public."SearchSynonyms" OWNER TO postgres;

--
-- TOC entry 362 (class 1259 OID 35623)
-- Name: ServiceRequestMessages; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."ServiceRequestMessages" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid NOT NULL,
    "ServiceRequestId" uuid NOT NULL,
    "SenderUserId" uuid NOT NULL,
    "SenderRole" text,
    "MessageType" text,
    "Body" text NOT NULL,
    "CreatedAt" timestamp without time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp without time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "AttachmentUrl" text,
    CONSTRAINT "CHK_MessageType_Valid" CHECK (("MessageType" = ANY (ARRAY['Text'::text, 'System'::text]))),
    CONSTRAINT "CHK_SenderRole_Valid" CHECK (("SenderRole" = ANY (ARRAY['Buyer'::text, 'Provider'::text]))),
    CONSTRAINT "CHK_ServiceRequestMessages_Body" CHECK (((length(TRIM(BOTH FROM "Body")) > 0) OR (("AttachmentUrl" IS NOT NULL) AND (length(TRIM(BOTH FROM "AttachmentUrl")) > 0))))
);


ALTER TABLE public."ServiceRequestMessages" OWNER TO postgres;

--
-- TOC entry 360 (class 1259 OID 35507)
-- Name: ServiceRequests; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."ServiceRequests" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid NOT NULL,
    "ServiceId" uuid NOT NULL,
    "BuyerUserId" uuid NOT NULL,
    "ProviderUserId" uuid NOT NULL,
    "Status" text,
    "RequestMessage" text,
    "AcceptedAt" timestamp without time zone,
    "CompletedByProviderAt" timestamp without time zone,
    "CompletedByBuyerAt" timestamp without time zone,
    "CancelledAt" timestamp without time zone,
    "CancellationReason" text,
    "CreatedAt" timestamp without time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp without time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    CONSTRAINT "CHK_ServiceRequests_DifferentUsers" CHECK (("BuyerUserId" <> "ProviderUserId"))
);


ALTER TABLE public."ServiceRequests" OWNER TO postgres;

--
-- TOC entry 361 (class 1259 OID 35556)
-- Name: ServiceReviews; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."ServiceReviews" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid NOT NULL,
    "ServiceRequestId" uuid NOT NULL,
    "ServiceId" uuid NOT NULL,
    "ReviewerUserId" uuid NOT NULL,
    "ProviderUserId" uuid NOT NULL,
    "Rating" integer NOT NULL,
    "Comment" text,
    "CreatedAt" timestamp without time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp without time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    CONSTRAINT "CHK_ServiceReviews_Rating" CHECK ((("Rating" >= 1) AND ("Rating" <= 5))),
    CONSTRAINT "CK_ServiceReviews_CommentLength" CHECK ((("Comment" IS NULL) OR (length("Comment") <= 5000))),
    CONSTRAINT "CK_ServiceReviews_RatingRange" CHECK ((("Rating" >= 1) AND ("Rating" <= 5)))
);


ALTER TABLE public."ServiceReviews" OWNER TO postgres;

--
-- TOC entry 359 (class 1259 OID 35463)
-- Name: Services; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."Services" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid NOT NULL,
    "ProfileId" uuid NOT NULL,
    "Title" character varying(200) NOT NULL,
    "Description" text NOT NULL,
    "Category" character varying(100),
    "IsActive" boolean DEFAULT true NOT NULL,
    "CreatedAt" timestamp without time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp without time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "StartingPrice" numeric(18,2),
    "PricingType" integer,
    "Currency" character varying(3),
    "ServiceImages" text,
    "Packages" text,
    CONSTRAINT "CK_Services_Currency" CHECK ((("Currency" IS NULL) OR (length(("Currency")::text) = 3))),
    CONSTRAINT "CK_Services_PricingType" CHECK ((("PricingType" IS NULL) OR (("PricingType" >= 0) AND ("PricingType" <= 2))))
);


ALTER TABLE public."Services" OWNER TO postgres;

--
-- TOC entry 313 (class 1259 OID 23623)
-- Name: Shipments; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."Shipments" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "ShippingMethodId" uuid,
    "ShippingMethodName" text NOT NULL,
    "CarrierName" text,
    "ShippingCost" numeric NOT NULL,
    "Currency" text NOT NULL,
    "Status" integer NOT NULL,
    "TrackingNumber" text,
    "TrackingUrl" text,
    "ShipmentNumber" text NOT NULL,
    "RecipientName" text NOT NULL,
    "ShippingAddress" text NOT NULL,
    "ShippingCity" text,
    "ShippingState" text,
    "ShippingZipCode" text,
    "ShippingCountry" text,
    "RecipientPhone" text,
    "WeightKg" numeric,
    "EstimatedDeliveryDate" timestamp with time zone,
    "ActualDeliveryDate" timestamp with time zone,
    "PackedAtUtc" timestamp with time zone,
    "ShippedAtUtc" timestamp with time zone,
    "DeliveredAtUtc" timestamp with time zone,
    "CancelledAtUtc" timestamp with time zone,
    "CancellationReason" text,
    "Notes" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "CarrierCode" character varying(50) DEFAULT 'manual'::character varying NOT NULL,
    "ServiceLevel" character varying(50) DEFAULT 'standard'::character varying,
    "IsCashOnDelivery" boolean DEFAULT false NOT NULL,
    "CodAmount" numeric(18,2),
    "CodCollected" boolean DEFAULT false NOT NULL,
    "CodCollectedAtUtc" timestamp with time zone,
    "AwbNumber" character varying(100),
    "RecipientEmail" character varying(255),
    "ShippingAddressLine2" character varying(500),
    "Latitude" numeric(9,6),
    "Longitude" numeric(9,6),
    "PackageCount" integer DEFAULT 1 NOT NULL,
    "PackageDescription" character varying(500),
    "ReadyForPickupAtUtc" timestamp with time zone,
    "PickedUpAtUtc" timestamp with time zone,
    "ReturnedAtUtc" timestamp with time zone,
    "FailedAtUtc" timestamp with time zone,
    "CarrierMetadata" text,
    "CarrierCreateResponse" text,
    "CarrierTrackingResponse" text,
    "LastCarrierSyncAtUtc" timestamp with time zone,
    "FailureReason" character varying(500),
    "ReturnReason" character varying(500),
    "DeliveryAttempts" integer DEFAULT 0 NOT NULL,
    "LabelUrl" character varying(1000),
    "LabelData" text,
    "LabelFormat" character varying(20),
    "OutForDeliveryAtUtc" timestamp with time zone,
    "ExternalReferenceId" character varying(200),
    "CarrierRequestId" character varying(200),
    "ExternalRegistrationState" character varying(50),
    "ExternalRegistrationAttemptCount" integer DEFAULT 0 NOT NULL,
    "ExternalRegistrationRequestedAtUtc" timestamp with time zone,
    "ExternalRegistrationLastAttemptAtUtc" timestamp with time zone,
    "ExternalRegistrationCompletedAtUtc" timestamp with time zone,
    "ExternalRegistrationFailedAtUtc" timestamp with time zone,
    "ExternalRegistrationError" character varying(500)
);


ALTER TABLE public."Shipments" OWNER TO postgres;

--
-- TOC entry 6841 (class 0 OID 0)
-- Dependencies: 313
-- Name: COLUMN "Shipments"."OutForDeliveryAtUtc"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."Shipments"."OutForDeliveryAtUtc" IS 'Timestamp when shipment status changed to OutForDelivery';


--
-- TOC entry 303 (class 1259 OID 23410)
-- Name: ShippingMethods; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."ShippingMethods" (
    "Id" uuid NOT NULL,
    "Name" text NOT NULL,
    "NameAr" text,
    "Description" text,
    "BaseRate" numeric NOT NULL,
    "PerKgRate" numeric NOT NULL,
    "FreeShippingThreshold" numeric,
    "EstimatedDeliveryDaysMin" integer NOT NULL,
    "EstimatedDeliveryDaysMax" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "SortOrder" integer NOT NULL,
    "AvailableCountries" text,
    "CarrierName" text,
    "TrackingUrlTemplate" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL,
    "CarrierCode" character varying(50),
    "FulfillmentMode" integer DEFAULT 0 NOT NULL,
    "FulfillmentCarrierCode" text,
    CONSTRAINT "CK_ShippingMethods_FulfillmentOwnership" CHECK (((("FulfillmentMode" = 0) AND ("FulfillmentCarrierCode" IS NULL)) OR (("FulfillmentMode" = 1) AND ("FulfillmentCarrierCode" IS NOT NULL))))
);


ALTER TABLE public."ShippingMethods" OWNER TO postgres;

--
-- TOC entry 6842 (class 0 OID 0)
-- Dependencies: 303
-- Name: COLUMN "ShippingMethods"."CarrierCode"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."ShippingMethods"."CarrierCode" IS 'Carrier code for carrier-agnostic shipping (bosta, aramex, mylerz, dhl). NULL means manual.';


--
-- TOC entry 382 (class 1259 OID 36582)
-- Name: SmartOffers; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."SmartOffers" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Status" character varying(32) NOT NULL,
    "Priority" integer DEFAULT 0 NOT NULL,
    "TriggerType" character varying(32) NOT NULL,
    "PagesScope" character varying(32) NOT NULL,
    "DelaySeconds" integer,
    "ScrollPercent" integer,
    "ShowOncePerSession" boolean DEFAULT false NOT NULL,
    "MaxDisplaysPerUser" integer,
    "PopupTitle" character varying(200) NOT NULL,
    "PopupDescription" character varying(1000),
    "ButtonText" character varying(100) NOT NULL,
    "CouponCode" character varying(100),
    "ImageUrl" character varying(1000),
    "BackgroundColor" character varying(32),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "GeneratedCouponId" uuid,
    "CouponDiscountType" character varying(32),
    "CouponDiscountValue" numeric(18,2),
    "CouponMinOrderAmount" numeric(18,2),
    "CouponMaxDiscountAmount" numeric(18,2),
    CONSTRAINT "CK_SmartOffers_CouponDiscountType" CHECK ((("CouponDiscountType" IS NULL) OR (("CouponDiscountType")::text = ANY ((ARRAY['Percentage'::character varying, 'FixedAmount'::character varying])::text[])))),
    CONSTRAINT "CK_SmartOffers_CouponDiscountValue_FixedAmount" CHECK ((("CouponDiscountValue" IS NULL) OR ((COALESCE("CouponDiscountType", 'Percentage'::character varying))::text <> 'FixedAmount'::text) OR ("CouponDiscountValue" <= 1000000.00))),
    CONSTRAINT "CK_SmartOffers_CouponDiscountValue_Percentage" CHECK ((("CouponDiscountValue" IS NULL) OR ((COALESCE("CouponDiscountType", 'Percentage'::character varying))::text <> 'Percentage'::text) OR ("CouponDiscountValue" <= (100)::numeric))),
    CONSTRAINT "CK_SmartOffers_CouponDiscountValue_Positive" CHECK ((("CouponDiscountValue" IS NULL) OR ("CouponDiscountValue" > (0)::numeric)))
);


ALTER TABLE public."SmartOffers" OWNER TO postgres;

--
-- TOC entry 273 (class 1259 OID 21093)
-- Name: StockBatches; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."StockBatches" (
    "Id" uuid NOT NULL,
    "WarehouseId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "ProductVariantId" uuid,
    "BatchNumber" character varying(100) NOT NULL,
    "ExpiryDate" timestamp with time zone,
    "ManufactureDate" timestamp with time zone,
    "Quantity" numeric(18,2) NOT NULL,
    "PurchasePrice" numeric(18,2) NOT NULL,
    "PurchaseInvoiceId" uuid,
    "Notes" character varying(500),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL
);


ALTER TABLE public."StockBatches" OWNER TO postgres;

--
-- TOC entry 247 (class 1259 OID 20410)
-- Name: StockMovements; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."StockMovements" (
    "Id" uuid NOT NULL,
    "WarehouseId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "ProductVariantId" uuid,
    "MovementType" integer NOT NULL,
    "QuantityChange" numeric(18,2) NOT NULL,
    "QuantityAfter" numeric(18,2) NOT NULL,
    "ReferenceId" uuid,
    "ReferenceNumber" text,
    "Notes" text,
    "ToWarehouseId" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "IsOpeningBalance" boolean DEFAULT false NOT NULL,
    "Price" numeric,
    "ConversionFactor" numeric DEFAULT 1.0 NOT NULL,
    "UnitId" uuid,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL,
    "ReservedQuantityChange" numeric(18,2) DEFAULT 0 NOT NULL,
    "DamagedQuantityChange" numeric(18,2) DEFAULT 0 NOT NULL,
    "AvailableAfter" numeric(18,2) DEFAULT 0 NOT NULL,
    "ReservedAfter" numeric(18,2) DEFAULT 0 NOT NULL,
    "DamagedAfter" numeric(18,2) DEFAULT 0 NOT NULL,
    "ReferenceLineId" uuid,
    "ReservationId" uuid,
    "InventoryEventId" uuid,
    "InventoryEventType" integer,
    "ReferenceType" character varying(50),
    "CorrelationId" character varying(100),
    "IdempotencyKey" character varying(200),
    "Source" character varying(100)
);


ALTER TABLE public."StockMovements" OWNER TO postgres;

--
-- TOC entry 467 (class 1259 OID 2287551)
-- Name: StockSyncOutboxMessages; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."StockSyncOutboxMessages" (
    "Id" uuid NOT NULL,
    "EventId" uuid NOT NULL,
    "EventType" character varying(200) NOT NULL,
    "ClrType" character varying(1000) NOT NULL,
    "PayloadJson" text NOT NULL,
    "TenantId" uuid NOT NULL,
    "VariantId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "CorrelationId" character varying(200) NOT NULL,
    "OccurredAtUtc" timestamp with time zone NOT NULL,
    "StockUpdatedAtUtc" timestamp with time zone NOT NULL,
    "PublishedAtUtc" timestamp with time zone,
    "AttemptCount" integer DEFAULT 0 NOT NULL,
    "LastError" character varying(2000),
    "NextRetryAtUtc" timestamp with time zone,
    "IsFailed" boolean DEFAULT false NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "ClaimedUntilUtc" timestamp without time zone,
    "EventVersion" integer DEFAULT 1 NOT NULL,
    "WarehouseId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL,
    CONSTRAINT "CK_StockSyncOutboxMessages_EventVersion_Positive" CHECK (("EventVersion" > 0)),
    CONSTRAINT "CK_StockSyncOutboxMessages_OrderingTimestamps_Required" CHECK ((("OccurredAtUtc" IS NOT NULL) AND ("StockUpdatedAtUtc" IS NOT NULL))),
    CONSTRAINT "CK_StockSyncOutboxMessages_WarehouseId_NotEmpty" CHECK (("WarehouseId" <> '00000000-0000-0000-0000-000000000000'::uuid)),
    CONSTRAINT "CK_StockSyncOutbox_EventVersion_Positive" CHECK (("EventVersion" > 0))
);


ALTER TABLE public."StockSyncOutboxMessages" OWNER TO postgres;

--
-- TOC entry 6843 (class 0 OID 0)
-- Dependencies: 467
-- Name: COLUMN "StockSyncOutboxMessages"."ClaimedUntilUtc"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."StockSyncOutboxMessages"."ClaimedUntilUtc" IS 'Timestamp until which this message is claimed by a worker. Prevents duplicate publishing by multiple workers.';


--
-- TOC entry 275 (class 1259 OID 21147)
-- Name: StockTakeItems; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."StockTakeItems" (
    "Id" uuid NOT NULL,
    "StockTakeId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "ProductVariantId" uuid,
    "SystemQuantity" numeric(18,2) NOT NULL,
    "CountedQuantity" numeric(18,2) NOT NULL,
    "Notes" character varying(500),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL
);


ALTER TABLE public."StockTakeItems" OWNER TO postgres;

--
-- TOC entry 274 (class 1259 OID 21128)
-- Name: StockTakes; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."StockTakes" (
    "Id" uuid NOT NULL,
    "ReferenceNumber" character varying(50) NOT NULL,
    "WarehouseId" uuid NOT NULL,
    "ScheduledDate" timestamp with time zone NOT NULL,
    "CompletedDate" timestamp with time zone,
    "Status" integer NOT NULL,
    "Notes" character varying(500),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL
);


ALTER TABLE public."StockTakes" OWNER TO postgres;

--
-- TOC entry 278 (class 1259 OID 21254)
-- Name: StockTransferItems; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."StockTransferItems" (
    "Id" uuid NOT NULL,
    "StockTransferId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "ProductVariantId" uuid,
    "Quantity" numeric NOT NULL,
    "Notes" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL
);


ALTER TABLE public."StockTransferItems" OWNER TO postgres;

--
-- TOC entry 480 (class 1259 OID 3022644)
-- Name: StockTransferRequestIdempotency; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."StockTransferRequestIdempotency" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Key" character varying(255) NOT NULL,
    "Operation" character varying(64) NOT NULL,
    "RequestFingerprint" character varying(64),
    "ResponseStatusCode" integer,
    "ResponseJson" text,
    "CompletedAtUtc" timestamp with time zone,
    "ClaimToken" character varying(32),
    "ClaimTimestampUtc" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."StockTransferRequestIdempotency" OWNER TO postgres;

--
-- TOC entry 277 (class 1259 OID 21214)
-- Name: StockTransfers; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."StockTransfers" (
    "Id" uuid NOT NULL,
    "TransferNumber" text NOT NULL,
    "TransferDate" timestamp with time zone NOT NULL,
    "FromWarehouseId" uuid NOT NULL,
    "ToWarehouseId" uuid NOT NULL,
    "Status" integer NOT NULL,
    "Notes" text,
    "RequestedById" uuid,
    "ApprovedById" uuid,
    "ApprovedAt" timestamp with time zone,
    "ApprovalNotes" text,
    "CompletedById" uuid,
    "CompletedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL
);


ALTER TABLE public."StockTransfers" OWNER TO postgres;

--
-- TOC entry 328 (class 1259 OID 26022)
-- Name: StoreConfigAuditEntries; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."StoreConfigAuditEntries" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid NOT NULL,
    "UserId" uuid,
    "UserName" character varying(256),
    "ActionType" character varying(50) NOT NULL,
    "Target" character varying(100) NOT NULL,
    "BeforeSnapshotJson" text,
    "AfterSnapshotJson" text,
    "Version" integer,
    "TraceId" character varying(128),
    "IpAddress" character varying(45),
    "UserAgent" text,
    "TimestampUtc" timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE public."StoreConfigAuditEntries" OWNER TO postgres;

--
-- TOC entry 315 (class 1259 OID 23679)
-- Name: StoreConfigs; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."StoreConfigs" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "StoreName" character varying(100) NOT NULL,
    "StoreDescription" character varying(500),
    "ThemeId" character varying(50) NOT NULL,
    "Theme_Colors_Primary" text NOT NULL,
    "Theme_Colors_Secondary" text NOT NULL,
    "Theme_Colors_Background" text NOT NULL,
    "Theme_Colors_Text" text NOT NULL,
    "Theme_Colors_Muted" text NOT NULL,
    "Theme_Font_Family" text NOT NULL,
    "Theme_Font_BaseSize" text NOT NULL,
    "Theme_Ui_BorderRadius" text NOT NULL,
    "Theme_Ui_ButtonStyle" text NOT NULL,
    "Theme_Layout_ProductCardVariant" text NOT NULL,
    "Theme_Layout_HeaderVariant" text NOT NULL,
    "Behavior_EnableGuestCheckout" boolean NOT NULL,
    "Behavior_ShowOutOfStockProducts" boolean NOT NULL,
    "Behavior_EnableSearch" boolean NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "MaintenanceMessage" text,
    "StorefrontStatus" text DEFAULT ''::text NOT NULL,
    "CustomizationJson" text DEFAULT '{}'::text NOT NULL,
    "ThemePresetKey" character varying(100) DEFAULT 'default'::character varying NOT NULL,
    "ThemeDraftOverridesJson" text DEFAULT '{}'::text NOT NULL,
    "ThemeLiveOverridesJson" text DEFAULT '{}'::text NOT NULL,
    "VersionsJson" text DEFAULT '[]'::text NOT NULL,
    "LiveVersion" integer DEFAULT 0 NOT NULL,
    "PromotionRanking_Enabled" boolean DEFAULT true NOT NULL,
    "PromotionRanking_BaseBoost" double precision DEFAULT 5.0 NOT NULL,
    "PromotionRanking_HighDiscountBoost" double precision DEFAULT 10.0 NOT NULL,
    "PromotionRanking_HighDiscountThreshold" double precision DEFAULT 20.0 NOT NULL,
    "PromotionRanking_MaxTotalBoost" double precision DEFAULT 25.0 NOT NULL,
    "PromotionRanking_EnableTimeDecay" boolean DEFAULT true NOT NULL,
    "PromotionRanking_TimeDecayScaleDays" double precision DEFAULT 7.0 NOT NULL,
    "PromotionRanking_TimeDecayOffsetDays" double precision DEFAULT 1.0 NOT NULL,
    "PromotionRanking_TimeDecayMinimum" double precision DEFAULT 0.3 NOT NULL,
    "CommerceAiEnabled" boolean DEFAULT false NOT NULL,
    "CommerceAiEnabledAt" timestamp without time zone,
    "CommerceAiUpdatedBy" character varying(255),
    "CommerceAiWidgetStyle" text DEFAULT 'minimal'::text NOT NULL,
    "CommerceAiWidgetPosition" text DEFAULT 'bottom-right'::text NOT NULL,
    "CommerceAiAutoGreetingEnabled" boolean DEFAULT false NOT NULL,
    "CommerceAiAutoGreetingMessage" text DEFAULT ''::text NOT NULL,
    "CommerceAiPersonality" text DEFAULT 'friendly'::text NOT NULL,
    "Behavior_FailCheckoutOnPromotionError" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."StoreConfigs" OWNER TO postgres;

--
-- TOC entry 323 (class 1259 OID 25808)
-- Name: StorefrontAuthProviderSettings; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."StorefrontAuthProviderSettings" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Provider" character varying(50) NOT NULL,
    "IsEnabled" boolean DEFAULT false NOT NULL,
    "ClientId" character varying(256),
    "ClientSecretEncrypted" character varying(1024),
    "AdditionalScopes" character varying(512),
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "UpdatedByUserId" uuid
);


ALTER TABLE public."StorefrontAuthProviderSettings" OWNER TO postgres;

--
-- TOC entry 447 (class 1259 OID 46646)
-- Name: StorefrontFooterLinks; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."StorefrontFooterLinks" (
    "Id" uuid NOT NULL,
    "SectionId" uuid NOT NULL,
    "Label" character varying(160) NOT NULL,
    "LabelAr" character varying(160),
    "LinkType" character varying(20) NOT NULL,
    "Url" character varying(1000),
    "CmsPageSlug" character varying(160),
    "OpenInNewTab" boolean DEFAULT false NOT NULL,
    "SortOrder" integer DEFAULT 0 NOT NULL,
    "IsActive" boolean DEFAULT true NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT "CK_StorefrontFooterLinks_LinkType" CHECK ((("LinkType")::text = ANY ((ARRAY['Internal'::character varying, 'External'::character varying, 'Cms'::character varying])::text[]))),
    CONSTRAINT "CK_StorefrontFooterLinks_Target" CHECK ((((("LinkType")::text = 'Cms'::text) AND ("CmsPageSlug" IS NOT NULL) AND (length(TRIM(BOTH FROM "CmsPageSlug")) > 0)) OR ((("LinkType")::text <> 'Cms'::text) AND ("Url" IS NOT NULL) AND (length(TRIM(BOTH FROM "Url")) > 0))))
);


ALTER TABLE public."StorefrontFooterLinks" OWNER TO postgres;

--
-- TOC entry 446 (class 1259 OID 46631)
-- Name: StorefrontFooterSections; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."StorefrontFooterSections" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "Title" character varying(120) NOT NULL,
    "SortOrder" integer DEFAULT 0 NOT NULL,
    "IsActive" boolean DEFAULT true NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "TitleAr" character varying(120)
);


ALTER TABLE public."StorefrontFooterSections" OWNER TO postgres;

--
-- TOC entry 448 (class 1259 OID 46672)
-- Name: StorefrontSocialLinks; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."StorefrontSocialLinks" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "Platform" character varying(40) NOT NULL,
    "Url" character varying(1000) NOT NULL,
    "Icon" character varying(80),
    "SortOrder" integer DEFAULT 0 NOT NULL,
    "IsActive" boolean DEFAULT true NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE public."StorefrontSocialLinks" OWNER TO postgres;

--
-- TOC entry 414 (class 1259 OID 37398)
-- Name: SuperAdmins; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."SuperAdmins" (
    "Id" uuid NOT NULL,
    "Email" character varying(320) NOT NULL,
    "PasswordHash" character varying(512) NOT NULL,
    "IsSystem" boolean DEFAULT false NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "LastLoginAt" timestamp with time zone,
    "RefreshTokenHash" character varying(256),
    "RefreshTokenExpiresAtUtc" timestamp with time zone,
    "TokenVersion" integer DEFAULT 0 NOT NULL
);


ALTER TABLE public."SuperAdmins" OWNER TO postgres;

--
-- TOC entry 459 (class 1259 OID 46942)
-- Name: SupplierPayments; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."SupplierPayments" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "SupplierId" uuid NOT NULL,
    "PurchaseInvoiceId" uuid,
    "PaymentDate" timestamp with time zone NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "PaymentMethod" integer NOT NULL,
    "IdempotencyKey" character varying(100) NOT NULL,
    "ReferenceNumber" character varying(100),
    "Notes" character varying(1000),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "CreatedBy" uuid,
    "UpdatedBy" uuid
);


ALTER TABLE public."SupplierPayments" OWNER TO postgres;

--
-- TOC entry 237 (class 1259 OID 20141)
-- Name: SupplierTransactions; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."SupplierTransactions" (
    "Id" uuid NOT NULL,
    "SupplierId" uuid NOT NULL,
    "TransactionDate" timestamp with time zone NOT NULL,
    "TransactionType" integer NOT NULL,
    "DebitAmount" numeric NOT NULL,
    "CreditAmount" numeric NOT NULL,
    "BalanceAfter" numeric NOT NULL,
    "ReferenceId" uuid,
    "ReferenceNumber" text,
    "Notes" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "IsOpeningBalance" boolean DEFAULT false NOT NULL,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL
);


ALTER TABLE public."SupplierTransactions" OWNER TO postgres;

--
-- TOC entry 231 (class 1259 OID 20040)
-- Name: Suppliers; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."Suppliers" (
    "Id" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Phone" character varying(50),
    "Email" character varying(200),
    "Address" text,
    "TaxNumber" character varying(50),
    "Balance" numeric(18,2) NOT NULL,
    "PaymentTermsDays" integer NOT NULL,
    "Notes" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "BalanceType" text,
    "OpeningBalanceAmount" numeric,
    "OpeningBalanceDate" timestamp with time zone,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL
);


ALTER TABLE public."Suppliers" OWNER TO postgres;

--
-- TOC entry 392 (class 1259 OID 36846)
-- Name: SystemSchemaVersions; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."SystemSchemaVersions" (
    "Id" uuid NOT NULL,
    "Component" character varying(100) NOT NULL,
    "Version" character varying(200) NOT NULL,
    "AppliedAt" timestamp with time zone NOT NULL,
    "DetailsJson" text,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."SystemSchemaVersions" OWNER TO postgres;

--
-- TOC entry 232 (class 1259 OID 20053)
-- Name: SystemSettings; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."SystemSettings" (
    "Id" uuid NOT NULL,
    "Key" text NOT NULL,
    "Value" text NOT NULL,
    "Description" text,
    "Category" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "TenantId" uuid
);


ALTER TABLE public."SystemSettings" OWNER TO postgres;

--
-- TOC entry 332 (class 1259 OID 34490)
-- Name: TenantAnalyticsConfigurations; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."TenantAnalyticsConfigurations" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "SettingsJson" text DEFAULT '{}'::text NOT NULL,
    "CreatedAtUtc" timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "UpdatedAtUtc" timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


ALTER TABLE public."TenantAnalyticsConfigurations" OWNER TO postgres;

--
-- TOC entry 340 (class 1259 OID 34814)
-- Name: TenantAutomationSettings; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."TenantAutomationSettings" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid NOT NULL,
    "EnableAbandonedRecovery" boolean DEFAULT false NOT NULL,
    "FirstMessageDelayMinutes" integer DEFAULT 60 NOT NULL,
    "SecondMessageDelayMinutes" integer DEFAULT 1440 NOT NULL,
    "WorkingHoursFrom" time without time zone DEFAULT '09:00:00'::time without time zone NOT NULL,
    "WorkingHoursTo" time without time zone DEFAULT '21:00:00'::time without time zone NOT NULL,
    "UpdatedAtUtc" timestamp without time zone DEFAULT now() NOT NULL,
    "CreatedAtUtc" timestamp without time zone DEFAULT now() NOT NULL,
    "N8nWebhookApiKey" character varying(100),
    "CreatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."TenantAutomationSettings" OWNER TO postgres;

--
-- TOC entry 6844 (class 0 OID 0)
-- Dependencies: 340
-- Name: TABLE "TenantAutomationSettings"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON TABLE public."TenantAutomationSettings" IS 'Tenant-level automation configuration for e-commerce marketing features. Phase: Smart Abandoned Cart Recovery Engine - Foundation';


--
-- TOC entry 6845 (class 0 OID 0)
-- Dependencies: 340
-- Name: COLUMN "TenantAutomationSettings"."EnableAbandonedRecovery"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."TenantAutomationSettings"."EnableAbandonedRecovery" IS 'Whether abandoned cart recovery automation is enabled. Default FALSE - must be explicitly enabled by admin.';


--
-- TOC entry 6846 (class 0 OID 0)
-- Dependencies: 340
-- Name: COLUMN "TenantAutomationSettings"."FirstMessageDelayMinutes"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."TenantAutomationSettings"."FirstMessageDelayMinutes" IS 'Delay in minutes before sending first abandoned cart message. Default 60 minutes (1 hour). Range: 0-10080 (7 days).';


--
-- TOC entry 6847 (class 0 OID 0)
-- Dependencies: 340
-- Name: COLUMN "TenantAutomationSettings"."SecondMessageDelayMinutes"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."TenantAutomationSettings"."SecondMessageDelayMinutes" IS 'Delay in minutes before sending second follow-up message. Default 1440 minutes (24 hours). Range: 0-20160 (14 days). Set to 0 to disable.';


--
-- TOC entry 6848 (class 0 OID 0)
-- Dependencies: 340
-- Name: COLUMN "TenantAutomationSettings"."WorkingHoursFrom"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."TenantAutomationSettings"."WorkingHoursFrom" IS 'Start of working hours (tenant timezone). Messages only sent during working hours. Default 09:00.';


--
-- TOC entry 6849 (class 0 OID 0)
-- Dependencies: 340
-- Name: COLUMN "TenantAutomationSettings"."WorkingHoursTo"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."TenantAutomationSettings"."WorkingHoursTo" IS 'End of working hours (tenant timezone). Messages only sent during working hours. Default 21:00.';


--
-- TOC entry 6850 (class 0 OID 0)
-- Dependencies: 340
-- Name: COLUMN "TenantAutomationSettings"."N8nWebhookApiKey"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."TenantAutomationSettings"."N8nWebhookApiKey" IS 'API key for authenticating n8n webhook callbacks. Auto-generated on first use.';


--
-- TOC entry 473 (class 1259 OID 2287825)
-- Name: TenantCurrencyNormalizationAudit; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."TenantCurrencyNormalizationAudit" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid NOT NULL,
    "OriginalCurrency" character varying(50) NOT NULL,
    "NormalizedCurrency" character varying(3) NOT NULL,
    "NormalizedAt" timestamp without time zone DEFAULT now() NOT NULL,
    "MigrationVersion" character varying(50) DEFAULT 'S40_AddCurrencyReferenceTable'::character varying NOT NULL
);


ALTER TABLE public."TenantCurrencyNormalizationAudit" OWNER TO postgres;

--
-- TOC entry 325 (class 1259 OID 25844)
-- Name: TenantPaymentMethods; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."TenantPaymentMethods" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Code" character varying(50) NOT NULL,
    "DisplayName" character varying(100) NOT NULL,
    "DisplayNameAr" character varying(100),
    "Description" character varying(500),
    "DescriptionAr" character varying(500),
    "IsEnabled" boolean DEFAULT false NOT NULL,
    "SortOrder" integer DEFAULT 0 NOT NULL,
    "SupportsOnline" boolean DEFAULT false NOT NULL,
    "SupportsOffline" boolean DEFAULT false NOT NULL,
    "IsSandbox" boolean DEFAULT true NOT NULL,
    "ConfigEncrypted" text,
    "Icon" character varying(50),
    "MinOrderAmount" numeric(18,4),
    "MaxOrderAmount" numeric(18,4),
    "Fee" numeric(18,4) DEFAULT 0 NOT NULL,
    "FeeType" character varying(20) DEFAULT 'fixed'::character varying NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "Version" integer DEFAULT 1 NOT NULL
);


ALTER TABLE public."TenantPaymentMethods" OWNER TO postgres;

--
-- TOC entry 6851 (class 0 OID 0)
-- Dependencies: 325
-- Name: COLUMN "TenantPaymentMethods"."Version"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."TenantPaymentMethods"."Version" IS 'Event version counter for projection ordering. Incremented on every mutation that publishes an event.';


--
-- TOC entry 311 (class 1259 OID 23585)
-- Name: Tenants; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."Tenants" (
    "Id" uuid NOT NULL,
    "Name" character varying(100) NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "Slug" character varying(63),
    "ContactEmail" character varying(255),
    "BootstrapVersion" integer DEFAULT 0 NOT NULL,
    "Country" character varying(2),
    "Currency" character varying(3) DEFAULT 'USD'::character varying NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    "Status" integer DEFAULT 0 NOT NULL,
    "SuspendedReason" text,
    "UpdatedAtUtc" timestamp with time zone,
    "UseBaileys" boolean DEFAULT false NOT NULL,
    "StorefrontBaseUrl" text
);


ALTER TABLE public."Tenants" OWNER TO postgres;

--
-- TOC entry 6852 (class 0 OID 0)
-- Dependencies: 311
-- Name: COLUMN "Tenants"."UseBaileys"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."Tenants"."UseBaileys" IS 'Feature gate for WhatsApp Baileys migration. When true, uses Baileys transport (20-40 MB RAM). When false, uses Playwright transport (300-600 MB RAM). Default: false for backward compatibility.';


--
-- TOC entry 318 (class 1259 OID 25525)
-- Name: ThemePresets; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."ThemePresets" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "Key" character varying(100) NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Description" text,
    "IsRtl" boolean DEFAULT false NOT NULL,
    "BaseThemeJson" text DEFAULT '{}'::text NOT NULL,
    "ThumbnailUrl" character varying(500),
    "IsActive" boolean DEFAULT true NOT NULL,
    "CreatedAtUtc" timestamp without time zone DEFAULT now() NOT NULL,
    "UpdatedAtUtc" timestamp without time zone DEFAULT now() NOT NULL,
    "PreviewImageUrl" character varying(500)
);


ALTER TABLE public."ThemePresets" OWNER TO postgres;

--
-- TOC entry 350 (class 1259 OID 35169)
-- Name: TreasuryAccounts; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."TreasuryAccounts" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid NOT NULL,
    "AccountId" uuid NOT NULL,
    "Type" integer NOT NULL,
    "Name" character varying(100) NOT NULL,
    "NameAr" character varying(100),
    "ReferenceCode" character varying(50),
    "Location" character varying(100),
    "IsDefault" boolean DEFAULT false NOT NULL,
    "IsActive" boolean DEFAULT true NOT NULL,
    "DisplayOrder" integer DEFAULT 0 NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


ALTER TABLE public."TreasuryAccounts" OWNER TO postgres;

--
-- TOC entry 6853 (class 0 OID 0)
-- Dependencies: 350
-- Name: TABLE "TreasuryAccounts"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON TABLE public."TreasuryAccounts" IS 'Phase 2 - Cashbox, bank, and clearing account tracking';


--
-- TOC entry 6854 (class 0 OID 0)
-- Dependencies: 350
-- Name: COLUMN "TreasuryAccounts"."Type"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public."TreasuryAccounts"."Type" IS '1=Cashbox, 2=Bank, 3=Clearing';


--
-- TOC entry 286 (class 1259 OID 21445)
-- Name: TreasuryTransactions; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."TreasuryTransactions" (
    "Id" uuid NOT NULL,
    "TransactionNumber" text NOT NULL,
    "TransactionDate" timestamp with time zone NOT NULL,
    "TransactionType" integer NOT NULL,
    "PaymentMethod" integer NOT NULL,
    "TargetAccountId" uuid,
    "TargetAccountName" text,
    "Amount" numeric(18,2) NOT NULL,
    "BalanceAfter" numeric(18,2) NOT NULL,
    "ReferenceNumber" text,
    "Notes" text,
    "CreatedByUserId" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL
);


ALTER TABLE public."TreasuryTransactions" OWNER TO postgres;

--
-- TOC entry 288 (class 1259 OID 21507)
-- Name: Units; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."Units" (
    "Id" uuid NOT NULL,
    "Name" text NOT NULL,
    "Symbol" text,
    "Description" text,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL
);


ALTER TABLE public."Units" OWNER TO postgres;

--
-- TOC entry 434 (class 1259 OID 38053)
-- Name: UserGovernanceAuditLogs; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."UserGovernanceAuditLogs" (
    "Id" uuid NOT NULL,
    "TargetUserId" uuid NOT NULL,
    "TargetRole" integer NOT NULL,
    "AdminId" uuid,
    "ActorType" character varying(50) NOT NULL,
    "ActionType" character varying(100) NOT NULL,
    "Reason" character varying(1000) NOT NULL,
    "BeforeState" jsonb NOT NULL,
    "AfterState" jsonb NOT NULL,
    "Metadata" jsonb,
    "TimestampUtc" timestamp with time zone NOT NULL
);


ALTER TABLE public."UserGovernanceAuditLogs" OWNER TO postgres;

--
-- TOC entry 435 (class 1259 OID 38073)
-- Name: UserGovernanceBlacklistEntries; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."UserGovernanceBlacklistEntries" (
    "Id" uuid NOT NULL,
    "Type" integer NOT NULL,
    "ValueNormalized" character varying(320) NOT NULL,
    "ValueDisplay" character varying(320) NOT NULL,
    "Reason" character varying(1000) NOT NULL,
    "IsActive" boolean DEFAULT true NOT NULL,
    "CreatedByAdminId" uuid,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "RevokedByAdminId" uuid,
    "RevokedAtUtc" timestamp with time zone,
    "RevokedReason" character varying(1000)
);


ALTER TABLE public."UserGovernanceBlacklistEntries" OWNER TO postgres;

--
-- TOC entry 433 (class 1259 OID 38031)
-- Name: UserGovernanceProfiles; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."UserGovernanceProfiles" (
    "Id" uuid NOT NULL,
    "SubjectId" uuid NOT NULL,
    "Role" integer NOT NULL,
    "Status" integer DEFAULT 0 NOT NULL,
    "RiskLevel" integer DEFAULT 0 NOT NULL,
    "IsKycVerified" boolean DEFAULT false NOT NULL,
    "SuspiciousFlags" integer DEFAULT 0 NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "LastLoginAtUtc" timestamp with time zone,
    "StatusChangedAtUtc" timestamp with time zone,
    "LastRiskEvaluatedAtUtc" timestamp with time zone,
    "LastModifiedByAdminId" uuid,
    "StatusReason" character varying(1000),
    "LastRiskReason" character varying(1000),
    "LastIpAddress" character varying(128),
    "DeviceFingerprint" character varying(200)
);


ALTER TABLE public."UserGovernanceProfiles" OWNER TO postgres;

--
-- TOC entry 233 (class 1259 OID 20065)
-- Name: Users; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."Users" (
    "Id" uuid NOT NULL,
    "Username" character varying(100) NOT NULL,
    "Email" character varying(200) NOT NULL,
    "PasswordHash" text NOT NULL,
    "FirstName" character varying(100) NOT NULL,
    "LastName" character varying(100) NOT NULL,
    "Phone" character varying(50),
    "Role" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "LastLoginAt" timestamp with time zone,
    "RefreshToken" text,
    "RefreshTokenExpiryTime" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL,
    "EmailVerificationStatus" integer DEFAULT 0 NOT NULL,
    "EmailVerifiedAtUtc" timestamp with time zone
);


ALTER TABLE public."Users" OWNER TO postgres;

--
-- TOC entry 287 (class 1259 OID 21469)
-- Name: VisaTransactions; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."VisaTransactions" (
    "Id" uuid NOT NULL,
    "TransactionDate" timestamp with time zone NOT NULL,
    "TransactionType" integer NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "IsDebit" boolean NOT NULL,
    "BalanceAfter" numeric(18,2) NOT NULL,
    "ReferenceNumber" text,
    "Description" text,
    "Notes" text,
    "SourceEntityId" uuid,
    "SourceEntityType" text,
    "CreatedByUserId" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL
);


ALTER TABLE public."VisaTransactions" OWNER TO postgres;

--
-- TOC entry 248 (class 1259 OID 20445)
-- Name: WarehouseStocks; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."WarehouseStocks" (
    "Id" uuid NOT NULL,
    "WarehouseId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "ProductVariantId" uuid,
    "Quantity" numeric(18,2) NOT NULL,
    "ReservedQuantity" numeric(18,2) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "LastCountedAt" timestamp with time zone,
    "LastCountedBy" text,
    "LastCountedQuantity" numeric,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL
);


ALTER TABLE public."WarehouseStocks" OWNER TO postgres;

--
-- TOC entry 234 (class 1259 OID 20082)
-- Name: Warehouses; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."Warehouses" (
    "Id" uuid NOT NULL,
    "Name" text NOT NULL,
    "Code" text,
    "Address" text,
    "Phone" text,
    "IsActive" boolean NOT NULL,
    "IsDefault" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "TenantId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL
);


ALTER TABLE public."Warehouses" OWNER TO postgres;

--
-- TOC entry 344 (class 1259 OID 34950)
-- Name: WhatsAppRateLimits; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."WhatsAppRateLimits" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid NOT NULL,
    "PhoneNumber" character varying(20) NOT NULL,
    "MessageCount" integer DEFAULT 0 NOT NULL,
    "WindowStartUtc" timestamp without time zone DEFAULT now() NOT NULL,
    "LastMessageAtUtc" timestamp without time zone DEFAULT now() NOT NULL,
    "OptedOut" boolean DEFAULT false NOT NULL,
    "OptedOutAtUtc" timestamp without time zone
);


ALTER TABLE public."WhatsAppRateLimits" OWNER TO postgres;

--
-- TOC entry 6855 (class 0 OID 0)
-- Dependencies: 344
-- Name: TABLE "WhatsAppRateLimits"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON TABLE public."WhatsAppRateLimits" IS 'Tracks WhatsApp message rate limits and opt-out status per phone number per tenant. Phase: Abandoned Cart Hardening.';


--
-- TOC entry 463 (class 1259 OID 55397)
-- Name: _LegacyCashMigrationLog; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."_LegacyCashMigrationLog" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TenantId" uuid NOT NULL,
    "MigrationDate" timestamp with time zone DEFAULT now() NOT NULL,
    "SourceType" character varying(50) NOT NULL,
    "SourceTreasuryAccountId" uuid,
    "SourceAccountCode" character varying(20) NOT NULL,
    "SourceBalanceBefore" numeric(18,4) NOT NULL,
    "TargetAccountId" uuid,
    "TargetAccountCode" character varying(20),
    "TargetCashboxId" uuid,
    "TargetCashboxName" character varying(200),
    "MigrationAmount" numeric(18,4) NOT NULL,
    "JournalEntryId" uuid,
    "JournalNumber" character varying(50),
    "Status" character varying(20) NOT NULL,
    "FailureReason" text,
    "OperatorId" uuid,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE public."_LegacyCashMigrationLog" OWNER TO postgres;

--
-- TOC entry 394 (class 1259 OID 36894)
-- Name: __CommerceHubCategorySeparation_Backup_Categories; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."__CommerceHubCategorySeparation_Backup_Categories" (
    "Id" uuid,
    "Name" text,
    "Description" text,
    "ParentCategoryId" uuid,
    "CreatedAt" timestamp with time zone,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean,
    "TenantId" uuid
);


ALTER TABLE public."__CommerceHubCategorySeparation_Backup_Categories" OWNER TO postgres;

--
-- TOC entry 395 (class 1259 OID 36899)
-- Name: __CommerceHubCategorySeparation_Backup_Products; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."__CommerceHubCategorySeparation_Backup_Products" (
    "Id" uuid,
    "Name" character varying(300),
    "Barcode" character varying(100),
    "SKU" character varying(100),
    "Description" text,
    "CategoryId" uuid,
    "PurchasePrice" numeric(18,2),
    "SalePrice" numeric(18,2),
    "PriceIncludesTax" boolean,
    "TaxRate" numeric(5,2),
    "Unit" character varying(50),
    "MinimumStockLevel" numeric(18,2),
    "Images" text,
    "IsActive" boolean,
    "TrackStock" boolean,
    "Notes" text,
    "CreatedAt" timestamp with time zone,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean,
    "ReorderPoint" numeric,
    "ReorderQuantity" numeric,
    "TrackBatchNumbers" boolean,
    "TrackExpiry" boolean,
    "AllowBackorder" boolean,
    "AmazonASIN" text,
    "Brand" text,
    "DescriptionAr" text,
    "DimensionUnit" text,
    "Height" numeric,
    "IsFeatured" boolean,
    "IsOnlineVisible" boolean,
    "IsSearchable" boolean,
    "LastSyncedAt" timestamp with time zone,
    "Length" numeric,
    "LowStockThreshold" integer,
    "Manufacturer" text,
    "MetaDescription" text,
    "MetaKeywords" text,
    "MetaTitle" text,
    "NameAr" text,
    "PublishStatus" integer,
    "RelatedProductIds" text,
    "Slug" text,
    "Tags" text,
    "VideoUrl" text,
    "Weight" numeric,
    "WeightUnit" text,
    "Width" numeric,
    "TenantId" uuid,
    "OnlinePrice" numeric,
    "HasVariants" boolean,
    "ProductTemplate" character varying(100)
);


ALTER TABLE public."__CommerceHubCategorySeparation_Backup_Products" OWNER TO postgres;

--
-- TOC entry 396 (class 1259 OID 36904)
-- Name: __CommerceHubCategorySeparation_CloneMap; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."__CommerceHubCategorySeparation_CloneMap" (
    "LegacyCategoryId" uuid CONSTRAINT "__CommerceHubCategorySeparation_Clone_LegacyCategoryId_not_null" NOT NULL,
    "TenantId" uuid NOT NULL,
    "ClonedCategoryId" uuid CONSTRAINT "__CommerceHubCategorySeparation_Clone_ClonedCategoryId_not_null" NOT NULL,
    "MappedAtUtc" timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE public."__CommerceHubCategorySeparation_CloneMap" OWNER TO postgres;

--
-- TOC entry 397 (class 1259 OID 36914)
-- Name: __CommerceHubCategorySeparation_MigratedCategories; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."__CommerceHubCategorySeparation_MigratedCategories" (
    "LegacyCategoryId" uuid CONSTRAINT "__CommerceHubCategorySeparation_Migra_LegacyCategoryId_not_null" NOT NULL,
    "DetectedReason" text CONSTRAINT "__CommerceHubCategorySeparation_Migrate_DetectedReason_not_null" NOT NULL,
    "MigratedAtUtc" timestamp with time zone DEFAULT now() CONSTRAINT "__CommerceHubCategorySeparation_Migrated_MigratedAtUtc_not_null" NOT NULL
);


ALTER TABLE public."__CommerceHubCategorySeparation_MigratedCategories" OWNER TO postgres;

--
-- TOC entry 398 (class 1259 OID 36981)
-- Name: __DefaultVariantBackfill_Backup_Products; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."__DefaultVariantBackfill_Backup_Products" (
    "Id" uuid,
    "Name" character varying(300),
    "Barcode" character varying(100),
    "SKU" character varying(100),
    "Description" text,
    "CategoryId" uuid,
    "PurchasePrice" numeric(18,2),
    "SalePrice" numeric(18,2),
    "PriceIncludesTax" boolean,
    "TaxRate" numeric(5,2),
    "Unit" character varying(50),
    "MinimumStockLevel" numeric(18,2),
    "Images" text,
    "IsActive" boolean,
    "TrackStock" boolean,
    "Notes" text,
    "CreatedAt" timestamp with time zone,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean,
    "ReorderPoint" numeric,
    "ReorderQuantity" numeric,
    "TrackBatchNumbers" boolean,
    "TrackExpiry" boolean,
    "AllowBackorder" boolean,
    "AmazonASIN" text,
    "Brand" text,
    "DescriptionAr" text,
    "DimensionUnit" text,
    "Height" numeric,
    "IsFeatured" boolean,
    "IsOnlineVisible" boolean,
    "IsSearchable" boolean,
    "LastSyncedAt" timestamp with time zone,
    "Length" numeric,
    "LowStockThreshold" integer,
    "Manufacturer" text,
    "MetaDescription" text,
    "MetaKeywords" text,
    "MetaTitle" text,
    "NameAr" text,
    "PublishStatus" integer,
    "RelatedProductIds" text,
    "Slug" text,
    "Tags" text,
    "VideoUrl" text,
    "Weight" numeric,
    "WeightUnit" text,
    "Width" numeric,
    "TenantId" uuid,
    "OnlinePrice" numeric,
    "HasVariants" boolean,
    "ProductTemplate" character varying(100)
);


ALTER TABLE public."__DefaultVariantBackfill_Backup_Products" OWNER TO postgres;

--
-- TOC entry 399 (class 1259 OID 36986)
-- Name: __DefaultVariantBackfill_Backup_WarehouseStocks; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."__DefaultVariantBackfill_Backup_WarehouseStocks" (
    "Id" uuid,
    "WarehouseId" uuid,
    "ProductId" uuid,
    "ProductVariantId" uuid,
    "Quantity" numeric(18,2),
    "ReservedQuantity" numeric(18,2),
    "CreatedAt" timestamp with time zone,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean,
    "LastCountedAt" timestamp with time zone,
    "LastCountedBy" text,
    "LastCountedQuantity" numeric,
    "TenantId" uuid
);


ALTER TABLE public."__DefaultVariantBackfill_Backup_WarehouseStocks" OWNER TO postgres;

--
-- TOC entry 400 (class 1259 OID 36991)
-- Name: __DefaultVariantBackfill_CreatedVariants; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."__DefaultVariantBackfill_CreatedVariants" (
    "ProductId" uuid NOT NULL,
    "VariantId" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE public."__DefaultVariantBackfill_CreatedVariants" OWNER TO postgres;

--
-- TOC entry 228 (class 1259 OID 20005)
-- Name: __EFMigrationsHistory; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL
);


ALTER TABLE public."__EFMigrationsHistory" OWNER TO postgres;

--
-- TOC entry 356 (class 1259 OID 35333)
-- Name: __MigrationHistory; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."__MigrationHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "AppliedOn" timestamp without time zone DEFAULT now() NOT NULL
);


ALTER TABLE public."__MigrationHistory" OWNER TO postgres;

--
-- TOC entry 461 (class 1259 OID 55113)
-- Name: order_number_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.order_number_seq
    START WITH 1000
    INCREMENT BY 1
    MINVALUE 1000
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.order_number_seq OWNER TO postgres;

--
-- TOC entry 457 (class 1259 OID 46920)
-- Name: purchase_invoice_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.purchase_invoice_seq
    START WITH 1000
    INCREMENT BY 1
    MINVALUE 1000
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.purchase_invoice_seq OWNER TO postgres;

--
-- TOC entry 460 (class 1259 OID 55112)
-- Name: return_number_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.return_number_seq
    START WITH 1000
    INCREMENT BY 1
    MINVALUE 1000
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.return_number_seq OWNER TO postgres;

--
-- TOC entry 462 (class 1259 OID 55114)
-- Name: stock_transfer_number_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.stock_transfer_number_seq
    START WITH 1000
    INCREMENT BY 1
    MINVALUE 1000
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.stock_transfer_number_seq OWNER TO postgres;

--
-- TOC entry 355 (class 1259 OID 35329)
-- Name: v_AccountingAuditRecent; Type: VIEW; Schema: public; Owner: postgres
--

CREATE VIEW public."v_AccountingAuditRecent" AS
 SELECT "TenantId",
    "BusinessType",
    "BusinessReference",
    "OperationType",
    "Amount",
    "JournalNumber",
    "PostedAtUtc",
    "WasIdempotent"
   FROM public."CommerceAccountingAudit" a
  WHERE ("PostedAtUtc" > (now() - '24:00:00'::interval))
  ORDER BY "PostedAtUtc" DESC;


ALTER VIEW public."v_AccountingAuditRecent" OWNER TO postgres;

--
-- TOC entry 354 (class 1259 OID 35325)
-- Name: v_AccountingOutboxFailed; Type: VIEW; Schema: public; Owner: postgres
--

CREATE VIEW public."v_AccountingOutboxFailed" AS
 SELECT "Id",
    "TenantId",
    "EventType",
    "BusinessEntityId",
    "IdempotencyKey",
    "OccurredAtUtc",
    "AttemptCount",
    "LastError"
   FROM public."AccountingOutboxMessages"
  WHERE ("IsFailed" = true)
  ORDER BY "OccurredAtUtc";


ALTER VIEW public."v_AccountingOutboxFailed" OWNER TO postgres;

--
-- TOC entry 353 (class 1259 OID 35320)
-- Name: v_AccountingOutboxPending; Type: VIEW; Schema: public; Owner: postgres
--

CREATE VIEW public."v_AccountingOutboxPending" AS
 SELECT "TenantId",
    "EventType",
    count(*) AS "PendingCount",
    min("OccurredAtUtc") AS "OldestPending",
    max("AttemptCount") AS "MaxAttempts"
   FROM public."AccountingOutboxMessages"
  WHERE (("ProcessedAtUtc" IS NULL) AND ("IsFailed" = false))
  GROUP BY "TenantId", "EventType";


ALTER VIEW public."v_AccountingOutboxPending" OWNER TO postgres;

--
-- TOC entry 338 (class 1259 OID 34680)
-- Name: whatsapp_accounts; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.whatsapp_accounts (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    phone_number character varying(20) NOT NULL,
    display_name character varying(100) NOT NULL,
    status integer DEFAULT 0 NOT NULL,
    enabled boolean DEFAULT false NOT NULL,
    use_for_order_confirmation boolean DEFAULT false NOT NULL,
    use_for_otp_verification boolean DEFAULT false NOT NULL,
    last_connected_at_utc timestamp with time zone,
    last_disconnected_at_utc timestamp with time zone,
    status_reason text,
    updated_at_utc timestamp with time zone,
    created_at_utc timestamp with time zone DEFAULT now() NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    tenant_id uuid NOT NULL,
    use_for_payment_approval boolean DEFAULT false NOT NULL,
    use_for_abandoned_cart_recovery boolean DEFAULT false NOT NULL
);


ALTER TABLE public.whatsapp_accounts OWNER TO postgres;

--
-- TOC entry 6856 (class 0 OID 0)
-- Dependencies: 338
-- Name: COLUMN whatsapp_accounts.use_for_payment_approval; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.whatsapp_accounts.use_for_payment_approval IS 'Whether this WhatsApp account is used for manual payment approval notifications';


--
-- TOC entry 6857 (class 0 OID 0)
-- Dependencies: 338
-- Name: COLUMN whatsapp_accounts.use_for_abandoned_cart_recovery; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON COLUMN public.whatsapp_accounts.use_for_abandoned_cart_recovery IS 'Use for abandoned cart recovery messages. Phase: Smart Abandoned Cart Recovery Engine';


--
-- TOC entry 339 (class 1259 OID 34717)
-- Name: whatsapp_message_templates; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.whatsapp_message_templates (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Type" character varying(50) NOT NULL,
    "Language" character varying(10) NOT NULL,
    "Content" text NOT NULL,
    "IsActive" boolean NOT NULL,
    "IsDefault" boolean NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "CreatedAtUtc" timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE public.whatsapp_message_templates OWNER TO postgres;

--
-- TOC entry 4304 (class 2604 OID 20870)
-- Name: aggregatedcounter id; Type: DEFAULT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.aggregatedcounter ALTER COLUMN id SET DEFAULT nextval('hangfire.aggregatedcounter_id_seq'::regclass);


--
-- TOC entry 4287 (class 2604 OID 20689)
-- Name: counter id; Type: DEFAULT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.counter ALTER COLUMN id SET DEFAULT nextval('hangfire.counter_id_seq'::regclass);


--
-- TOC entry 4288 (class 2604 OID 20699)
-- Name: hash id; Type: DEFAULT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.hash ALTER COLUMN id SET DEFAULT nextval('hangfire.hash_id_seq'::regclass);


--
-- TOC entry 4290 (class 2604 OID 20710)
-- Name: job id; Type: DEFAULT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.job ALTER COLUMN id SET DEFAULT nextval('hangfire.job_id_seq'::regclass);


--
-- TOC entry 4301 (class 2604 OID 20763)
-- Name: jobparameter id; Type: DEFAULT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.jobparameter ALTER COLUMN id SET DEFAULT nextval('hangfire.jobparameter_id_seq'::regclass);


--
-- TOC entry 4294 (class 2604 OID 20788)
-- Name: jobqueue id; Type: DEFAULT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.jobqueue ALTER COLUMN id SET DEFAULT nextval('hangfire.jobqueue_id_seq'::regclass);


--
-- TOC entry 4296 (class 2604 OID 20810)
-- Name: list id; Type: DEFAULT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.list ALTER COLUMN id SET DEFAULT nextval('hangfire.list_id_seq'::regclass);


--
-- TOC entry 4299 (class 2604 OID 20820)
-- Name: set id; Type: DEFAULT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.set ALTER COLUMN id SET DEFAULT nextval('hangfire.set_id_seq'::regclass);


--
-- TOC entry 4292 (class 2604 OID 20738)
-- Name: state id; Type: DEFAULT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.state ALTER COLUMN id SET DEFAULT nextval('hangfire.state_id_seq'::regclass);


--
-- TOC entry 5254 (class 2606 OID 20879)
-- Name: aggregatedcounter aggregatedcounter_key_key; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.aggregatedcounter
    ADD CONSTRAINT aggregatedcounter_key_key UNIQUE (key);


--
-- TOC entry 5256 (class 2606 OID 20877)
-- Name: aggregatedcounter aggregatedcounter_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.aggregatedcounter
    ADD CONSTRAINT aggregatedcounter_pkey PRIMARY KEY (id);


--
-- TOC entry 5216 (class 2606 OID 20691)
-- Name: counter counter_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.counter
    ADD CONSTRAINT counter_pkey PRIMARY KEY (id);


--
-- TOC entry 5220 (class 2606 OID 20839)
-- Name: hash hash_key_field_key; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.hash
    ADD CONSTRAINT hash_key_field_key UNIQUE (key, field);


--
-- TOC entry 5222 (class 2606 OID 20701)
-- Name: hash hash_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.hash
    ADD CONSTRAINT hash_pkey PRIMARY KEY (id);


--
-- TOC entry 5228 (class 2606 OID 20712)
-- Name: job job_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.job
    ADD CONSTRAINT job_pkey PRIMARY KEY (id);


--
-- TOC entry 5250 (class 2606 OID 20765)
-- Name: jobparameter jobparameter_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.jobparameter
    ADD CONSTRAINT jobparameter_pkey PRIMARY KEY (id);


--
-- TOC entry 5236 (class 2606 OID 20790)
-- Name: jobqueue jobqueue_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.jobqueue
    ADD CONSTRAINT jobqueue_pkey PRIMARY KEY (id);


--
-- TOC entry 5239 (class 2606 OID 20812)
-- Name: list list_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.list
    ADD CONSTRAINT list_pkey PRIMARY KEY (id);


--
-- TOC entry 5252 (class 2606 OID 20680)
-- Name: lock lock_resource_key; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.lock
    ADD CONSTRAINT lock_resource_key UNIQUE (resource);

ALTER TABLE ONLY hangfire.lock REPLICA IDENTITY USING INDEX lock_resource_key;


--
-- TOC entry 5214 (class 2606 OID 20518)
-- Name: schema schema_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.schema
    ADD CONSTRAINT schema_pkey PRIMARY KEY (version);


--
-- TOC entry 5241 (class 2606 OID 20844)
-- Name: server server_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.server
    ADD CONSTRAINT server_pkey PRIMARY KEY (id);


--
-- TOC entry 5245 (class 2606 OID 20847)
-- Name: set set_key_value_key; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.set
    ADD CONSTRAINT set_key_value_key UNIQUE (key, value);


--
-- TOC entry 5247 (class 2606 OID 20822)
-- Name: set set_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.set
    ADD CONSTRAINT set_pkey PRIMARY KEY (id);


--
-- TOC entry 5231 (class 2606 OID 20740)
-- Name: state state_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.state
    ADD CONSTRAINT state_pkey PRIMARY KEY (id);


--
-- TOC entry 5433 (class 2606 OID 23544)
-- Name: InboxState AK_InboxState_MessageId_ConsumerId; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."InboxState"
    ADD CONSTRAINT "AK_InboxState_MessageId_ConsumerId" UNIQUE ("MessageId", "ConsumerId");


--
-- TOC entry 5631 (class 2606 OID 34935)
-- Name: AbandonedCartRecoveryLogs AbandonedCartRecoveryLogs_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AbandonedCartRecoveryLogs"
    ADD CONSTRAINT "AbandonedCartRecoveryLogs_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 5610 (class 2606 OID 34870)
-- Name: AbandonedCarts AbandonedCarts_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AbandonedCarts"
    ADD CONSTRAINT "AbandonedCarts_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 5678 (class 2606 OID 35283)
-- Name: AccountingOutboxMessages AccountingOutboxMessages_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AccountingOutboxMessages"
    ADD CONSTRAINT "AccountingOutboxMessages_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 5759 (class 2606 OID 35941)
-- Name: AffiliateAccounts AffiliateAccounts_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliateAccounts"
    ADD CONSTRAINT "AffiliateAccounts_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 5773 (class 2606 OID 36007)
-- Name: AffiliateAttributions AffiliateAttributions_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliateAttributions"
    ADD CONSTRAINT "AffiliateAttributions_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 5807 (class 2606 OID 36278)
-- Name: AffiliateCategories AffiliateCategories_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliateCategories"
    ADD CONSTRAINT "AffiliateCategories_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 5782 (class 2606 OID 36054)
-- Name: AffiliateCommissionLedger AffiliateCommissionLedger_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliateCommissionLedger"
    ADD CONSTRAINT "AffiliateCommissionLedger_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 5767 (class 2606 OID 35974)
-- Name: AffiliateProductPolicies AffiliateProductPolicies_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliateProductPolicies"
    ADD CONSTRAINT "AffiliateProductPolicies_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 5743 (class 2606 OID 35788)
-- Name: ChatAuditLog ChatAuditLog_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ChatAuditLog"
    ADD CONSTRAINT "ChatAuditLog_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 5687 (class 2606 OID 35311)
-- Name: CommerceAccountingAudit CommerceAccountingAudit_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceAccountingAudit"
    ADD CONSTRAINT "CommerceAccountingAudit_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 6324 (class 2606 OID 2287823)
-- Name: Currencies Currencies_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Currencies"
    ADD CONSTRAINT "Currencies_pkey" PRIMARY KEY ("Code");


--
-- TOC entry 6277 (class 2606 OID 2287519)
-- Name: RevokedCustomerAccessTokens IX_RevokedCustomerAccessTokens_Jti; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."RevokedCustomerAccessTokens"
    ADD CONSTRAINT "IX_RevokedCustomerAccessTokens_Jti" UNIQUE ("Jti");


--
-- TOC entry 6356 (class 2606 OID 3022575)
-- Name: MarketplaceEventOutboxDeadLetterArchives MarketplaceEventOutboxDeadLetterArchives_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."MarketplaceEventOutboxDeadLetterArchives"
    ADD CONSTRAINT "MarketplaceEventOutboxDeadLetterArchives_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 5702 (class 2606 OID 35447)
-- Name: MarketplaceProfiles MarketplaceProfiles_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."MarketplaceProfiles"
    ADD CONSTRAINT "MarketplaceProfiles_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 5515 (class 2606 OID 25799)
-- Name: OnlineCustomerAuthProviders OnlineCustomerAuthProviders_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."OnlineCustomerAuthProviders"
    ADD CONSTRAINT "OnlineCustomerAuthProviders_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 5502 (class 2606 OID 25750)
-- Name: OnlineCustomerEmailVerifications OnlineCustomerEmailVerifications_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."OnlineCustomerEmailVerifications"
    ADD CONSTRAINT "OnlineCustomerEmailVerifications_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 5510 (class 2606 OID 25774)
-- Name: OnlineCustomerRefreshTokens OnlineCustomerRefreshTokens_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."OnlineCustomerRefreshTokens"
    ADD CONSTRAINT "OnlineCustomerRefreshTokens_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 5629 (class 2606 OID 34911)
-- Name: OutboxMessages OutboxMessages_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."OutboxMessages"
    ADD CONSTRAINT "OutboxMessages_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 5372 (class 2606 OID 23246)
-- Name: AccountBalances PK_AccountBalances; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AccountBalances"
    ADD CONSTRAINT "PK_AccountBalances" PRIMARY KEY ("BalanceId");


--
-- TOC entry 5296 (class 2606 OID 21207)
-- Name: AccountLedgers PK_AccountLedgers; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AccountLedgers"
    ADD CONSTRAINT "PK_AccountLedgers" PRIMARY KEY ("Id");


--
-- TOC entry 5376 (class 2606 OID 23264)
-- Name: AccountMappings PK_AccountMappings; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AccountMappings"
    ADD CONSTRAINT "PK_AccountMappings" PRIMARY KEY ("MappingId");


--
-- TOC entry 5358 (class 2606 OID 23202)
-- Name: Accounts PK_Accounts; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Accounts"
    ADD CONSTRAINT "PK_Accounts" PRIMARY KEY ("AccountId");


--
-- TOC entry 5825 (class 2606 OID 36355)
-- Name: AffiliateBalanceLedger PK_AffiliateBalanceLedger; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliateBalanceLedger"
    ADD CONSTRAINT "PK_AffiliateBalanceLedger" PRIMARY KEY ("Id");


--
-- TOC entry 5820 (class 2606 OID 36323)
-- Name: AffiliatePayoutRequests PK_AffiliatePayoutRequests; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliatePayoutRequests"
    ADD CONSTRAINT "PK_AffiliatePayoutRequests" PRIMARY KEY ("Id");


--
-- TOC entry 5802 (class 2606 OID 36183)
-- Name: AffiliatePromotionLinks PK_AffiliatePromotionLinks; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliatePromotionLinks"
    ADD CONSTRAINT "PK_AffiliatePromotionLinks" PRIMARY KEY ("Id");


--
-- TOC entry 5794 (class 2606 OID 36119)
-- Name: AffiliateUsers PK_AffiliateUsers; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliateUsers"
    ADD CONSTRAINT "PK_AffiliateUsers" PRIMARY KEY ("Id");


--
-- TOC entry 5571 (class 2606 OID 34529)
-- Name: AnalyticsAlertRules PK_AnalyticsAlertRules; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AnalyticsAlertRules"
    ADD CONSTRAINT "PK_AnalyticsAlertRules" PRIMARY KEY ("RuleId");


--
-- TOC entry 5352 (class 2606 OID 23067)
-- Name: AuditLogs PK_AuditLogs; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AuditLogs"
    ADD CONSTRAINT "PK_AuditLogs" PRIMARY KEY ("LogId");


--
-- TOC entry 5323 (class 2606 OID 21384)
-- Name: BankTransactions PK_BankTransactions; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."BankTransactions"
    ADD CONSTRAINT "PK_BankTransactions" PRIMARY KEY ("Id");


--
-- TOC entry 5320 (class 2606 OID 21368)
-- Name: Banks PK_Banks; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Banks"
    ADD CONSTRAINT "PK_Banks" PRIMARY KEY ("Id");


--
-- TOC entry 5576 (class 2606 OID 34551)
-- Name: CampaignCosts PK_CampaignCosts; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CampaignCosts"
    ADD CONSTRAINT "PK_CampaignCosts" PRIMARY KEY ("CampaignCostId");


--
-- TOC entry 6314 (class 2606 OID 2287711)
-- Name: CanonicalIdentityLinks PK_CanonicalIdentityLinks; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CanonicalIdentityLinks"
    ADD CONSTRAINT "PK_CanonicalIdentityLinks" PRIMARY KEY ("Id");


--
-- TOC entry 5581 (class 2606 OID 34595)
-- Name: CarrierConfigurations PK_CarrierConfigurations; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CarrierConfigurations"
    ADD CONSTRAINT "PK_CarrierConfigurations" PRIMARY KEY ("Id");


--
-- TOC entry 5272 (class 2606 OID 21062)
-- Name: CashboxTransactions PK_CashboxTransactions; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CashboxTransactions"
    ADD CONSTRAINT "PK_CashboxTransactions" PRIMARY KEY ("Id");


--
-- TOC entry 5264 (class 2606 OID 21036)
-- Name: Cashboxes PK_Cashboxes; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Cashboxes"
    ADD CONSTRAINT "PK_Cashboxes" PRIMARY KEY ("Id");


--
-- TOC entry 5102 (class 2606 OID 20022)
-- Name: Categories PK_Categories; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Categories"
    ADD CONSTRAINT "PK_Categories" PRIMARY KEY ("Id");


--
-- TOC entry 5836 (class 2606 OID 36402)
-- Name: CheckoutSessions PK_CheckoutSessions; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CheckoutSessions"
    ADD CONSTRAINT "PK_CheckoutSessions" PRIMARY KEY ("Id");


--
-- TOC entry 6175 (class 2606 OID 38376)
-- Name: CmsPages PK_CmsPages; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CmsPages"
    ADD CONSTRAINT "PK_CmsPages" PRIMARY KEY ("Id");


--
-- TOC entry 5924 (class 2606 OID 37039)
-- Name: CommerceHubCarts PK_CommerceHubCarts; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubCarts"
    ADD CONSTRAINT "PK_CommerceHubCarts" PRIMARY KEY ("Id");


--
-- TOC entry 5884 (class 2606 OID 36723)
-- Name: CommerceHubCategories PK_CommerceHubCategories; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubCategories"
    ADD CONSTRAINT "PK_CommerceHubCategories" PRIMARY KEY ("Id");


--
-- TOC entry 5888 (class 2606 OID 36742)
-- Name: CommerceHubCategoryAttributes PK_CommerceHubCategoryAttributes; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubCategoryAttributes"
    ADD CONSTRAINT "PK_CommerceHubCategoryAttributes" PRIMARY KEY ("Id");


--
-- TOC entry 5932 (class 2606 OID 37056)
-- Name: CommerceHubCheckoutIdempotency PK_CommerceHubCheckoutIdempotency; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubCheckoutIdempotency"
    ADD CONSTRAINT "PK_CommerceHubCheckoutIdempotency" PRIMARY KEY ("Id");


--
-- TOC entry 6044 (class 2606 OID 37598)
-- Name: CommerceHubCustomerPayoutDetails PK_CommerceHubCustomerPayoutDetails; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubCustomerPayoutDetails"
    ADD CONSTRAINT "PK_CommerceHubCustomerPayoutDetails" PRIMARY KEY ("Id");


--
-- TOC entry 6023 (class 2606 OID 37466)
-- Name: CommerceHubCustomers PK_CommerceHubCustomers; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubCustomers"
    ADD CONSTRAINT "PK_CommerceHubCustomers" PRIMARY KEY ("Id");


--
-- TOC entry 6154 (class 2606 OID 38236)
-- Name: CommerceHubDailyFinancialAggregates PK_CommerceHubDailyFinancialAggregates; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubDailyFinancialAggregates"
    ADD CONSTRAINT "PK_CommerceHubDailyFinancialAggregates" PRIMARY KEY ("DateUtc", "CurrencyCode");


--
-- TOC entry 5980 (class 2606 OID 37254)
-- Name: CommerceHubFinancialAuditLogs PK_CommerceHubFinancialAuditLogs; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubFinancialAuditLogs"
    ADD CONSTRAINT "PK_CommerceHubFinancialAuditLogs" PRIMARY KEY ("Id");


--
-- TOC entry 6115 (class 2606 OID 37993)
-- Name: CommerceHubFinancialGuardLocks PK_CommerceHubFinancialGuardLocks; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubFinancialGuardLocks"
    ADD CONSTRAINT "PK_CommerceHubFinancialGuardLocks" PRIMARY KEY ("Id");


--
-- TOC entry 6122 (class 2606 OID 38021)
-- Name: CommerceHubFinancialOperationAudits PK_CommerceHubFinancialOperationAudits; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubFinancialOperationAudits"
    ADD CONSTRAINT "PK_CommerceHubFinancialOperationAudits" PRIMARY KEY ("Id");


--
-- TOC entry 6158 (class 2606 OID 38261)
-- Name: CommerceHubFinancialReconciliationReports PK_CommerceHubFinancialReconciliationReports; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubFinancialReconciliationReports"
    ADD CONSTRAINT "PK_CommerceHubFinancialReconciliationReports" PRIMARY KEY ("Id");


--
-- TOC entry 6070 (class 2606 OID 37703)
-- Name: CommerceHubFinancialTransactions PK_CommerceHubFinancialTransactions; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubFinancialTransactions"
    ADD CONSTRAINT "PK_CommerceHubFinancialTransactions" PRIMARY KEY ("Id");


--
-- TOC entry 5906 (class 2606 OID 36830)
-- Name: CommerceHubListings PK_CommerceHubListings; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubListings"
    ADD CONSTRAINT "PK_CommerceHubListings" PRIMARY KEY ("Id");


--
-- TOC entry 5867 (class 2606 OID 36645)
-- Name: CommerceHubMarketplaceOrders PK_CommerceHubMarketplaceOrders; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubMarketplaceOrders"
    ADD CONSTRAINT "PK_CommerceHubMarketplaceOrders" PRIMARY KEY ("Id");


--
-- TOC entry 5969 (class 2606 OID 37215)
-- Name: CommerceHubPaymentAttempts PK_CommerceHubPaymentAttempts; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubPaymentAttempts"
    ADD CONSTRAINT "PK_CommerceHubPaymentAttempts" PRIMARY KEY ("Id");


--
-- TOC entry 6338 (class 2606 OID 3022202)
-- Name: CommerceHubPaymentCompensationOutboxMessages PK_CommerceHubPaymentCompensationOutboxMessages; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubPaymentCompensationOutboxMessages"
    ADD CONSTRAINT "PK_CommerceHubPaymentCompensationOutboxMessages" PRIMARY KEY ("Id");


--
-- TOC entry 5949 (class 2606 OID 37132)
-- Name: CommerceHubPaymentItems PK_CommerceHubPaymentItems; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubPaymentItems"
    ADD CONSTRAINT "PK_CommerceHubPaymentItems" PRIMARY KEY ("Id");


--
-- TOC entry 5953 (class 2606 OID 37147)
-- Name: CommerceHubPaymentLogs PK_CommerceHubPaymentLogs; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubPaymentLogs"
    ADD CONSTRAINT "PK_CommerceHubPaymentLogs" PRIMARY KEY ("Id");


--
-- TOC entry 5943 (class 2606 OID 37119)
-- Name: CommerceHubPayments PK_CommerceHubPayments; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubPayments"
    ADD CONSTRAINT "PK_CommerceHubPayments" PRIMARY KEY ("Id");


--
-- TOC entry 5999 (class 2606 OID 37289)
-- Name: CommerceHubPayoutItems PK_CommerceHubPayoutItems; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubPayoutItems"
    ADD CONSTRAINT "PK_CommerceHubPayoutItems" PRIMARY KEY ("Id");


--
-- TOC entry 5987 (class 2606 OID 37276)
-- Name: CommerceHubPayouts PK_CommerceHubPayouts; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubPayouts"
    ADD CONSTRAINT "PK_CommerceHubPayouts" PRIMARY KEY ("Id");


--
-- TOC entry 6040 (class 2606 OID 37561)
-- Name: CommerceHubRefundEvidenceMedia PK_CommerceHubRefundEvidenceMedia; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubRefundEvidenceMedia"
    ADD CONSTRAINT "PK_CommerceHubRefundEvidenceMedia" PRIMARY KEY ("Id");


--
-- TOC entry 6048 (class 2606 OID 37626)
-- Name: CommerceHubRefundExecutions PK_CommerceHubRefundExecutions; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubRefundExecutions"
    ADD CONSTRAINT "PK_CommerceHubRefundExecutions" PRIMARY KEY ("Id");


--
-- TOC entry 6033 (class 2606 OID 37498)
-- Name: CommerceHubRefundRequests PK_CommerceHubRefundRequests; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubRefundRequests"
    ADD CONSTRAINT "PK_CommerceHubRefundRequests" PRIMARY KEY ("Id");


--
-- TOC entry 6009 (class 2606 OID 37312)
-- Name: CommerceHubRefunds PK_CommerceHubRefunds; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubRefunds"
    ADD CONSTRAINT "PK_CommerceHubRefunds" PRIMARY KEY ("Id");


--
-- TOC entry 6056 (class 2606 OID 37651)
-- Name: CommerceHubReturnRequests PK_CommerceHubReturnRequests; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubReturnRequests"
    ADD CONSTRAINT "PK_CommerceHubReturnRequests" PRIMARY KEY ("Id");


--
-- TOC entry 5881 (class 2606 OID 36684)
-- Name: CommerceHubSubOrderItems PK_CommerceHubSubOrderItems; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubSubOrderItems"
    ADD CONSTRAINT "PK_CommerceHubSubOrderItems" PRIMARY KEY ("Id");


--
-- TOC entry 5936 (class 2606 OID 37079)
-- Name: CommerceHubSubOrderStatusHistory PK_CommerceHubSubOrderStatusHistory; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubSubOrderStatusHistory"
    ADD CONSTRAINT "PK_CommerceHubSubOrderStatusHistory" PRIMARY KEY ("Id");


--
-- TOC entry 5875 (class 2606 OID 36665)
-- Name: CommerceHubSubOrders PK_CommerceHubSubOrders; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubSubOrders"
    ADD CONSTRAINT "PK_CommerceHubSubOrders" PRIMARY KEY ("Id");


--
-- TOC entry 6334 (class 2606 OID 3022166)
-- Name: CommerceHubUnresolvedPaymentWebhooks PK_CommerceHubUnresolvedPaymentWebhooks; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubUnresolvedPaymentWebhooks"
    ADD CONSTRAINT "PK_CommerceHubUnresolvedPaymentWebhooks" PRIMARY KEY ("Id");


--
-- TOC entry 5963 (class 2606 OID 37161)
-- Name: CommerceHubVendorEarnings PK_CommerceHubVendorEarnings; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubVendorEarnings"
    ADD CONSTRAINT "PK_CommerceHubVendorEarnings" PRIMARY KEY ("Id");


--
-- TOC entry 6100 (class 2606 OID 37854)
-- Name: CommerceHubVendorPaymentAuditLogs PK_CommerceHubVendorPaymentAuditLogs; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubVendorPaymentAuditLogs"
    ADD CONSTRAINT "PK_CommerceHubVendorPaymentAuditLogs" PRIMARY KEY ("Id");


--
-- TOC entry 6083 (class 2606 OID 37778)
-- Name: CommerceHubVendorPaymentMethods PK_CommerceHubVendorPaymentMethods; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubVendorPaymentMethods"
    ADD CONSTRAINT "PK_CommerceHubVendorPaymentMethods" PRIMARY KEY ("Id");


--
-- TOC entry 6093 (class 2606 OID 37829)
-- Name: CommerceHubVendorPaymentProofs PK_CommerceHubVendorPaymentProofs; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubVendorPaymentProofs"
    ADD CONSTRAINT "PK_CommerceHubVendorPaymentProofs" PRIMARY KEY ("Id");


--
-- TOC entry 6089 (class 2606 OID 37801)
-- Name: CommerceHubVendorPaymentRequests PK_CommerceHubVendorPaymentRequests; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubVendorPaymentRequests"
    ADD CONSTRAINT "PK_CommerceHubVendorPaymentRequests" PRIMARY KEY ("Id");


--
-- TOC entry 6103 (class 2606 OID 37887)
-- Name: CommerceHubVendorPayoutMethods PK_CommerceHubVendorPayoutMethods; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubVendorPayoutMethods"
    ADD CONSTRAINT "PK_CommerceHubVendorPayoutMethods" PRIMARY KEY ("Id");


--
-- TOC entry 6078 (class 2606 OID 37749)
-- Name: CommerceHubVendorSettlements PK_CommerceHubVendorSettlements; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubVendorSettlements"
    ADD CONSTRAINT "PK_CommerceHubVendorSettlements" PRIMARY KEY ("Id");


--
-- TOC entry 6019 (class 2606 OID 37441)
-- Name: CommissionRules PK_CommissionRules; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommissionRules"
    ADD CONSTRAINT "PK_CommissionRules" PRIMARY KEY ("Id");


--
-- TOC entry 6322 (class 2606 OID 2287789)
-- Name: CouponReservations PK_CouponReservations; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CouponReservations"
    ADD CONSTRAINT "PK_CouponReservations" PRIMARY KEY ("Id");


--
-- TOC entry 5554 (class 2606 OID 26112)
-- Name: CouponUsages PK_CouponUsages; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CouponUsages"
    ADD CONSTRAINT "PK_CouponUsages" PRIMARY KEY ("Id");


--
-- TOC entry 5389 (class 2606 OID 23409)
-- Name: Coupons PK_Coupons; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Coupons"
    ADD CONSTRAINT "PK_Coupons" PRIMARY KEY ("Id");


--
-- TOC entry 5138 (class 2606 OID 20135)
-- Name: CustomerTransactions PK_CustomerTransactions; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CustomerTransactions"
    ADD CONSTRAINT "PK_CustomerTransactions" PRIMARY KEY ("Id");


--
-- TOC entry 5108 (class 2606 OID 20039)
-- Name: Customers PK_Customers; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Customers"
    ADD CONSTRAINT "PK_Customers" PRIMARY KEY ("Id");


--
-- TOC entry 5483 (class 2606 OID 23761)
-- Name: EmailVerificationTokens PK_EmailVerificationTokens; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."EmailVerificationTokens"
    ADD CONSTRAINT "PK_EmailVerificationTokens" PRIMARY KEY ("Id");


--
-- TOC entry 5325 (class 2606 OID 21402)
-- Name: ExpenseCategories PK_ExpenseCategories; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ExpenseCategories"
    ADD CONSTRAINT "PK_ExpenseCategories" PRIMARY KEY ("Id");


--
-- TOC entry 5331 (class 2606 OID 21420)
-- Name: Expenses PK_Expenses; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Expenses"
    ADD CONSTRAINT "PK_Expenses" PRIMARY KEY ("Id");


--
-- TOC entry 5314 (class 2606 OID 21320)
-- Name: FinancialAccounts PK_FinancialAccounts; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."FinancialAccounts"
    ADD CONSTRAINT "PK_FinancialAccounts" PRIMARY KEY ("Id");


--
-- TOC entry 5317 (class 2606 OID 21340)
-- Name: FinancialTransactions PK_FinancialTransactions; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."FinancialTransactions"
    ADD CONSTRAINT "PK_FinancialTransactions" PRIMARY KEY ("Id");


--
-- TOC entry 5843 (class 2606 OID 36454)
-- Name: GlobalPhoneReputation PK_GlobalPhoneReputation; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."GlobalPhoneReputation"
    ADD CONSTRAINT "PK_GlobalPhoneReputation" PRIMARY KEY ("Id");


--
-- TOC entry 6163 (class 2606 OID 38307)
-- Name: HomepageSections PK_HomepageSections; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."HomepageSections"
    ADD CONSTRAINT "PK_HomepageSections" PRIMARY KEY ("Id");


--
-- TOC entry 5436 (class 2606 OID 23542)
-- Name: InboxState PK_InboxState; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."InboxState"
    ADD CONSTRAINT "PK_InboxState" PRIMARY KEY ("Id");


--
-- TOC entry 6112 (class 2606 OID 37963)
-- Name: InventoryEventInbox PK_InventoryEventInbox; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."InventoryEventInbox"
    ADD CONSTRAINT "PK_InventoryEventInbox" PRIMARY KEY ("Id");


--
-- TOC entry 6109 (class 2606 OID 37945)
-- Name: InventoryReservations PK_InventoryReservations; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."InventoryReservations"
    ADD CONSTRAINT "PK_InventoryReservations" PRIMARY KEY ("Id");


--
-- TOC entry 5367 (class 2606 OID 23225)
-- Name: JournalEntries PK_JournalEntries; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."JournalEntries"
    ADD CONSTRAINT "PK_JournalEntries" PRIMARY KEY ("JournalId");


--
-- TOC entry 5383 (class 2606 OID 23283)
-- Name: JournalLines PK_JournalLines; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."JournalLines"
    ADD CONSTRAINT "PK_JournalLines" PRIMARY KEY ("LineId");


--
-- TOC entry 5649 (class 2606 OID 35035)
-- Name: LedgerAccounts PK_LedgerAccounts; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."LedgerAccounts"
    ADD CONSTRAINT "PK_LedgerAccounts" PRIMARY KEY ("Id");


--
-- TOC entry 5659 (class 2606 OID 35064)
-- Name: LedgerJournalEntries PK_LedgerJournalEntries; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."LedgerJournalEntries"
    ADD CONSTRAINT "PK_LedgerJournalEntries" PRIMARY KEY ("Id");


--
-- TOC entry 5661 (class 2606 OID 35078)
-- Name: LedgerJournalNumberSequences PK_LedgerJournalSeq; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."LedgerJournalNumberSequences"
    ADD CONSTRAINT "PK_LedgerJournalSeq" PRIMARY KEY ("TenantId");


--
-- TOC entry 5667 (class 2606 OID 35102)
-- Name: LedgerLines PK_LedgerLines; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."LedgerLines"
    ADD CONSTRAINT "PK_LedgerLines" PRIMARY KEY ("Id");


--
-- TOC entry 5751 (class 2606 OID 35857)
-- Name: MarketingReviewEvidences PK_MarketingReviewEvidences; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."MarketingReviewEvidences"
    ADD CONSTRAINT "PK_MarketingReviewEvidences" PRIMARY KEY ("Id");


--
-- TOC entry 6239 (class 2606 OID 46816)
-- Name: MarketplaceCategoryTranslations PK_MarketplaceCategoryTranslations; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."MarketplaceCategoryTranslations"
    ADD CONSTRAINT "PK_MarketplaceCategoryTranslations" PRIMARY KEY ("Id");


--
-- TOC entry 6187 (class 2606 OID 46552)
-- Name: MarketplaceCmsPages PK_MarketplaceCmsPages; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."MarketplaceCmsPages"
    ADD CONSTRAINT "PK_MarketplaceCmsPages" PRIMARY KEY ("Id");


--
-- TOC entry 6224 (class 2606 OID 46722)
-- Name: MarketplaceEventOutboxMessages PK_MarketplaceEventOutboxMessages; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."MarketplaceEventOutboxMessages"
    ADD CONSTRAINT "PK_MarketplaceEventOutboxMessages" PRIMARY KEY ("Id");


--
-- TOC entry 6198 (class 2606 OID 46601)
-- Name: MarketplaceFooterLinks PK_MarketplaceFooterLinks; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."MarketplaceFooterLinks"
    ADD CONSTRAINT "PK_MarketplaceFooterLinks" PRIMARY KEY ("Id");


--
-- TOC entry 6195 (class 2606 OID 46580)
-- Name: MarketplaceFooterSections PK_MarketplaceFooterSections; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."MarketplaceFooterSections"
    ADD CONSTRAINT "PK_MarketplaceFooterSections" PRIMARY KEY ("Id");


--
-- TOC entry 6371 (class 2606 OID 3866341)
-- Name: MarketplaceProjectionOutboxMessages PK_MarketplaceProjectionOutboxMessages; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."MarketplaceProjectionOutboxMessages"
    ADD CONSTRAINT "PK_MarketplaceProjectionOutboxMessages" PRIMARY KEY ("Id");


--
-- TOC entry 6201 (class 2606 OID 46624)
-- Name: MarketplaceSocialLinks PK_MarketplaceSocialLinks; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."MarketplaceSocialLinks"
    ADD CONSTRAINT "PK_MarketplaceSocialLinks" PRIMARY KEY ("Id");


--
-- TOC entry 5478 (class 2606 OID 23744)
-- Name: OnboardingChecklists PK_OnboardingChecklists; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."OnboardingChecklists"
    ADD CONSTRAINT "PK_OnboardingChecklists" PRIMARY KEY ("Id");


--
-- TOC entry 5525 (class 2606 OID 25835)
-- Name: OnlineCustomerWishlistItems PK_OnlineCustomerWishlistItems; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."OnlineCustomerWishlistItems"
    ADD CONSTRAINT "PK_OnlineCustomerWishlistItems" PRIMARY KEY ("Id");


--
-- TOC entry 5497 (class 2606 OID 25730)
-- Name: OnlineCustomers PK_OnlineCustomers; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."OnlineCustomers"
    ADD CONSTRAINT "PK_OnlineCustomers" PRIMARY KEY ("Id");


--
-- TOC entry 5431 (class 2606 OID 23488)
-- Name: OrderItems PK_OrderItems; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."OrderItems"
    ADD CONSTRAINT "PK_OrderItems" PRIMARY KEY ("Id");


--
-- TOC entry 6309 (class 2606 OID 2287670)
-- Name: OrderRefundLines PK_OrderRefundLines; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."OrderRefundLines"
    ADD CONSTRAINT "PK_OrderRefundLines" PRIMARY KEY ("Id");


--
-- TOC entry 6302 (class 2606 OID 2287657)
-- Name: OrderRefunds PK_OrderRefunds; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."OrderRefunds"
    ADD CONSTRAINT "PK_OrderRefunds" PRIMARY KEY ("Id");


--
-- TOC entry 6273 (class 2606 OID 2277989)
-- Name: OrderRequestIdempotency PK_OrderRequestIdempotency; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."OrderRequestIdempotency"
    ADD CONSTRAINT "PK_OrderRequestIdempotency" PRIMARY KEY ("Id");


--
-- TOC entry 5422 (class 2606 OID 23448)
-- Name: Orders PK_Orders; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Orders"
    ADD CONSTRAINT "PK_Orders" PRIMARY KEY ("Id");


--
-- TOC entry 5445 (class 2606 OID 23568)
-- Name: OutboxMessage PK_OutboxMessage; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."OutboxMessage"
    ADD CONSTRAINT "PK_OutboxMessage" PRIMARY KEY ("SequenceNumber");


--
-- TOC entry 5439 (class 2606 OID 23554)
-- Name: OutboxState PK_OutboxState; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."OutboxState"
    ADD CONSTRAINT "PK_OutboxState" PRIMARY KEY ("OutboxId");


--
-- TOC entry 6256 (class 2606 OID 46941)
-- Name: PartyLedgerAdjustments PK_PartyLedgerAdjustments; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PartyLedgerAdjustments"
    ADD CONSTRAINT "PK_PartyLedgerAdjustments" PRIMARY KEY ("Id");


--
-- TOC entry 5458 (class 2606 OID 23617)
-- Name: PaymentIntents PK_PaymentIntents; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PaymentIntents"
    ADD CONSTRAINT "PK_PaymentIntents" PRIMARY KEY ("Id");


--
-- TOC entry 5671 (class 2606 OID 35153)
-- Name: PaymentMethods PK_PaymentMethods; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PaymentMethods"
    ADD CONSTRAINT "PK_PaymentMethods" PRIMARY KEY ("Id");


--
-- TOC entry 6342 (class 2606 OID 3022495)
-- Name: PaymentSettlementBatches PK_PaymentSettlementBatches; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PaymentSettlementBatches"
    ADD CONSTRAINT "PK_PaymentSettlementBatches" PRIMARY KEY ("Id");


--
-- TOC entry 6347 (class 2606 OID 3022508)
-- Name: PaymentSettlementOrders PK_PaymentSettlementOrders; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PaymentSettlementOrders"
    ADD CONSTRAINT "PK_PaymentSettlementOrders" PRIMARY KEY ("Id");


--
-- TOC entry 5471 (class 2606 OID 23669)
-- Name: PaymentWebhookLogs PK_PaymentWebhookLogs; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PaymentWebhookLogs"
    ADD CONSTRAINT "PK_PaymentWebhookLogs" PRIMARY KEY ("Id");


--
-- TOC entry 5840 (class 2606 OID 36434)
-- Name: PhoneRiskProfiles PK_PhoneRiskProfiles; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PhoneRiskProfiles"
    ADD CONSTRAINT "PK_PhoneRiskProfiles" PRIMARY KEY ("Id");


--
-- TOC entry 5847 (class 2606 OID 36465)
-- Name: PhoneSpamReports PK_PhoneSpamReports; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PhoneSpamReports"
    ADD CONSTRAINT "PK_PhoneSpamReports" PRIMARY KEY ("Id");


--
-- TOC entry 5584 (class 2606 OID 34647)
-- Name: PixelTrackingConfigs PK_PixelTrackingConfigs; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PixelTrackingConfigs"
    ADD CONSTRAINT "PK_PixelTrackingConfigs" PRIMARY KEY ("Id");


--
-- TOC entry 6229 (class 2606 OID 46740)
-- Name: ProcessedEvents PK_ProcessedEvents; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ProcessedEvents"
    ADD CONSTRAINT "PK_ProcessedEvents" PRIMARY KEY ("Id");


--
-- TOC entry 5757 (class 2606 OID 35911)
-- Name: ProcessedTrustEvent PK_ProcessedTrustEvent; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ProcessedTrustEvent"
    ADD CONSTRAINT "PK_ProcessedTrustEvent" PRIMARY KEY ("Id");


--
-- TOC entry 5850 (class 2606 OID 36538)
-- Name: ProductMedia PK_ProductMedia; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ProductMedia"
    ADD CONSTRAINT "PK_ProductMedia" PRIMARY KEY ("Id");


--
-- TOC entry 6365 (class 2606 OID 3795754)
-- Name: ProductRequestIdempotency PK_ProductRequestIdempotency; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ProductRequestIdempotency"
    ADD CONSTRAINT "PK_ProductRequestIdempotency" PRIMARY KEY ("Id");


--
-- TOC entry 6236 (class 2606 OID 46766)
-- Name: ProductReviews PK_ProductReviews; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ProductReviews"
    ADD CONSTRAINT "PK_ProductReviews" PRIMARY KEY ("Id");


--
-- TOC entry 5347 (class 2606 OID 21533)
-- Name: ProductUnits PK_ProductUnits; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ProductUnits"
    ADD CONSTRAINT "PK_ProductUnits" PRIMARY KEY ("Id");


--
-- TOC entry 5535 (class 2606 OID 25945)
-- Name: ProductVariantAttributes PK_ProductVariantAttributes; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ProductVariantAttributes"
    ADD CONSTRAINT "PK_ProductVariantAttributes" PRIMARY KEY ("Id");


--
-- TOC entry 5540 (class 2606 OID 25967)
-- Name: ProductVariantStocks PK_ProductVariantStocks; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ProductVariantStocks"
    ADD CONSTRAINT "PK_ProductVariantStocks" PRIMARY KEY ("Id");


--
-- TOC entry 5169 (class 2606 OID 20268)
-- Name: ProductVariants PK_ProductVariants; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ProductVariants"
    ADD CONSTRAINT "PK_ProductVariants" PRIMARY KEY ("Id");


--
-- TOC entry 5135 (class 2606 OID 20114)
-- Name: Products PK_Products; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Products"
    ADD CONSTRAINT "PK_Products" PRIMARY KEY ("Id");


--
-- TOC entry 6147 (class 2606 OID 38162)
-- Name: PromoCodeUsages PK_PromoCodeUsages; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PromoCodeUsages"
    ADD CONSTRAINT "PK_PromoCodeUsages" PRIMARY KEY ("Id");


--
-- TOC entry 6141 (class 2606 OID 38113)
-- Name: PromoCodes PK_PromoCodes; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PromoCodes"
    ADD CONSTRAINT "PK_PromoCodes" PRIMARY KEY ("Id");


--
-- TOC entry 5564 (class 2606 OID 26186)
-- Name: PromotionUsages PK_PromotionUsages; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PromotionUsages"
    ADD CONSTRAINT "PK_PromotionUsages" PRIMARY KEY ("Id");


--
-- TOC entry 5560 (class 2606 OID 26162)
-- Name: Promotions PK_Promotions; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Promotions"
    ADD CONSTRAINT "PK_Promotions" PRIMARY KEY ("Id");


--
-- TOC entry 5182 (class 2606 OID 20329)
-- Name: PurchaseInvoiceItems PK_PurchaseInvoiceItems; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PurchaseInvoiceItems"
    ADD CONSTRAINT "PK_PurchaseInvoiceItems" PRIMARY KEY ("Id");


--
-- TOC entry 5172 (class 2606 OID 20287)
-- Name: PurchaseInvoicePayments PK_PurchaseInvoicePayments; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PurchaseInvoicePayments"
    ADD CONSTRAINT "PK_PurchaseInvoicePayments" PRIMARY KEY ("Id");


--
-- TOC entry 5146 (class 2606 OID 20182)
-- Name: PurchaseInvoices PK_PurchaseInvoices; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PurchaseInvoices"
    ADD CONSTRAINT "PK_PurchaseInvoices" PRIMARY KEY ("Id");


--
-- TOC entry 6244 (class 2606 OID 46881)
-- Name: PurchaseRequestIdempotency PK_PurchaseRequestIdempotency; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PurchaseRequestIdempotency"
    ADD CONSTRAINT "PK_PurchaseRequestIdempotency" PRIMARY KEY ("Id");


--
-- TOC entry 6248 (class 2606 OID 46899)
-- Name: PurchaseReturnRequestIdempotency PK_PurchaseReturnRequestIdempotency; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PurchaseReturnRequestIdempotency"
    ADD CONSTRAINT "PK_PurchaseReturnRequestIdempotency" PRIMARY KEY ("Id");


--
-- TOC entry 5258 (class 2606 OID 21020)
-- Name: ReceiptSettings PK_ReceiptSettings; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ReceiptSettings"
    ADD CONSTRAINT "PK_ReceiptSettings" PRIMARY KEY ("Id");


--
-- TOC entry 5696 (class 2606 OID 35409)
-- Name: RejectedCarrierWebhookEvents PK_RejectedCarrierWebhookEvents; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."RejectedCarrierWebhookEvents"
    ADD CONSTRAINT "PK_RejectedCarrierWebhookEvents" PRIMARY KEY ("Id");


--
-- TOC entry 5188 (class 2606 OID 20361)
-- Name: ReturnItems PK_ReturnItems; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ReturnItems"
    ADD CONSTRAINT "PK_ReturnItems" PRIMARY KEY ("Id");


--
-- TOC entry 5152 (class 2606 OID 20209)
-- Name: Returns PK_Returns; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Returns"
    ADD CONSTRAINT "PK_Returns" PRIMARY KEY ("Id");


--
-- TOC entry 5195 (class 2606 OID 20394)
-- Name: SalesInvoiceItems PK_SalesInvoiceItems; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."SalesInvoiceItems"
    ADD CONSTRAINT "PK_SalesInvoiceItems" PRIMARY KEY ("Id");


--
-- TOC entry 5176 (class 2606 OID 20306)
-- Name: SalesInvoicePayments PK_SalesInvoicePayments; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."SalesInvoicePayments"
    ADD CONSTRAINT "PK_SalesInvoicePayments" PRIMARY KEY ("Id");


--
-- TOC entry 5163 (class 2606 OID 20244)
-- Name: SalesInvoices PK_SalesInvoices; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."SalesInvoices"
    ADD CONSTRAINT "PK_SalesInvoices" PRIMARY KEY ("Id");


--
-- TOC entry 5311 (class 2606 OID 21301)
-- Name: SalesRepresentatives PK_SalesRepresentatives; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."SalesRepresentatives"
    ADD CONSTRAINT "PK_SalesRepresentatives" PRIMARY KEY ("Id");


--
-- TOC entry 6252 (class 2606 OID 46917)
-- Name: SalesRequestIdempotency PK_SalesRequestIdempotency; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."SalesRequestIdempotency"
    ADD CONSTRAINT "PK_SalesRequestIdempotency" PRIMARY KEY ("Id");


--
-- TOC entry 5893 (class 2606 OID 36771)
-- Name: SearchClicks PK_SearchClicks; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."SearchClicks"
    ADD CONSTRAINT "PK_SearchClicks" PRIMARY KEY ("Id");


--
-- TOC entry 5915 (class 2606 OID 36887)
-- Name: SearchFilterEvents PK_SearchFilterEvents; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."SearchFilterEvents"
    ADD CONSTRAINT "PK_SearchFilterEvents" PRIMARY KEY ("Id");


--
-- TOC entry 5897 (class 2606 OID 36786)
-- Name: SearchQueries PK_SearchQueries; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."SearchQueries"
    ADD CONSTRAINT "PK_SearchQueries" PRIMARY KEY ("Id");


--
-- TOC entry 5899 (class 2606 OID 36800)
-- Name: SearchSynonyms PK_SearchSynonyms; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."SearchSynonyms"
    ADD CONSTRAINT "PK_SearchSynonyms" PRIMARY KEY ("Id");


--
-- TOC entry 5467 (class 2606 OID 23642)
-- Name: Shipments PK_Shipments; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Shipments"
    ADD CONSTRAINT "PK_Shipments" PRIMARY KEY ("Id");


--
-- TOC entry 5392 (class 2606 OID 23426)
-- Name: ShippingMethods PK_ShippingMethods; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ShippingMethods"
    ADD CONSTRAINT "PK_ShippingMethods" PRIMARY KEY ("Id");


--
-- TOC entry 5855 (class 2606 OID 36601)
-- Name: SmartOffers PK_SmartOffers; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."SmartOffers"
    ADD CONSTRAINT "PK_SmartOffers" PRIMARY KEY ("Id");


--
-- TOC entry 5280 (class 2606 OID 21107)
-- Name: StockBatches PK_StockBatches; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockBatches"
    ADD CONSTRAINT "PK_StockBatches" PRIMARY KEY ("Id");


--
-- TOC entry 5205 (class 2606 OID 20424)
-- Name: StockMovements PK_StockMovements; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockMovements"
    ADD CONSTRAINT "PK_StockMovements" PRIMARY KEY ("Id");


--
-- TOC entry 6296 (class 2606 OID 2287575)
-- Name: StockSyncOutboxMessages PK_StockSyncOutboxMessages; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockSyncOutboxMessages"
    ADD CONSTRAINT "PK_StockSyncOutboxMessages" PRIMARY KEY ("Id");


--
-- TOC entry 5292 (class 2606 OID 21160)
-- Name: StockTakeItems PK_StockTakeItems; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockTakeItems"
    ADD CONSTRAINT "PK_StockTakeItems" PRIMARY KEY ("Id");


--
-- TOC entry 5286 (class 2606 OID 21141)
-- Name: StockTakes PK_StockTakes; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockTakes"
    ADD CONSTRAINT "PK_StockTakes" PRIMARY KEY ("Id");


--
-- TOC entry 5309 (class 2606 OID 21266)
-- Name: StockTransferItems PK_StockTransferItems; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockTransferItems"
    ADD CONSTRAINT "PK_StockTransferItems" PRIMARY KEY ("Id");


--
-- TOC entry 6360 (class 2606 OID 3022657)
-- Name: StockTransferRequestIdempotency PK_StockTransferRequestIdempotency; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockTransferRequestIdempotency"
    ADD CONSTRAINT "PK_StockTransferRequestIdempotency" PRIMARY KEY ("Id");


--
-- TOC entry 5304 (class 2606 OID 21228)
-- Name: StockTransfers PK_StockTransfers; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockTransfers"
    ADD CONSTRAINT "PK_StockTransfers" PRIMARY KEY ("Id");


--
-- TOC entry 5475 (class 2606 OID 23705)
-- Name: StoreConfigs PK_StoreConfigs; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StoreConfigs"
    ADD CONSTRAINT "PK_StoreConfigs" PRIMARY KEY ("Id");


--
-- TOC entry 6208 (class 2606 OID 46666)
-- Name: StorefrontFooterLinks PK_StorefrontFooterLinks; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StorefrontFooterLinks"
    ADD CONSTRAINT "PK_StorefrontFooterLinks" PRIMARY KEY ("Id");


--
-- TOC entry 6205 (class 2606 OID 46645)
-- Name: StorefrontFooterSections PK_StorefrontFooterSections; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StorefrontFooterSections"
    ADD CONSTRAINT "PK_StorefrontFooterSections" PRIMARY KEY ("Id");


--
-- TOC entry 6211 (class 2606 OID 46689)
-- Name: StorefrontSocialLinks PK_StorefrontSocialLinks; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StorefrontSocialLinks"
    ADD CONSTRAINT "PK_StorefrontSocialLinks" PRIMARY KEY ("Id");


--
-- TOC entry 6014 (class 2606 OID 37410)
-- Name: SuperAdmins PK_SuperAdmins; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."SuperAdmins"
    ADD CONSTRAINT "PK_SuperAdmins" PRIMARY KEY ("Id");


--
-- TOC entry 6262 (class 2606 OID 46958)
-- Name: SupplierPayments PK_SupplierPayments; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."SupplierPayments"
    ADD CONSTRAINT "PK_SupplierPayments" PRIMARY KEY ("Id");


--
-- TOC entry 5142 (class 2606 OID 20156)
-- Name: SupplierTransactions PK_SupplierTransactions; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."SupplierTransactions"
    ADD CONSTRAINT "PK_SupplierTransactions" PRIMARY KEY ("Id");


--
-- TOC entry 5114 (class 2606 OID 20052)
-- Name: Suppliers PK_Suppliers; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Suppliers"
    ADD CONSTRAINT "PK_Suppliers" PRIMARY KEY ("Id");


--
-- TOC entry 5909 (class 2606 OID 36860)
-- Name: SystemSchemaVersions PK_SystemSchemaVersions; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."SystemSchemaVersions"
    ADD CONSTRAINT "PK_SystemSchemaVersions" PRIMARY KEY ("Id");


--
-- TOC entry 5117 (class 2606 OID 20064)
-- Name: SystemSettings PK_SystemSettings; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."SystemSettings"
    ADD CONSTRAINT "PK_SystemSettings" PRIMARY KEY ("Id");


--
-- TOC entry 5567 (class 2606 OID 34504)
-- Name: TenantAnalyticsConfigurations PK_TenantAnalyticsConfigurations; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."TenantAnalyticsConfigurations"
    ADD CONSTRAINT "PK_TenantAnalyticsConfigurations" PRIMARY KEY ("Id");


--
-- TOC entry 5531 (class 2606 OID 25872)
-- Name: TenantPaymentMethods PK_TenantPaymentMethods; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."TenantPaymentMethods"
    ADD CONSTRAINT "PK_TenantPaymentMethods" PRIMARY KEY ("Id");


--
-- TOC entry 5452 (class 2606 OID 23595)
-- Name: Tenants PK_Tenants; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Tenants"
    ADD CONSTRAINT "PK_Tenants" PRIMARY KEY ("Id");


--
-- TOC entry 5676 (class 2606 OID 35189)
-- Name: TreasuryAccounts PK_TreasuryAccounts; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."TreasuryAccounts"
    ADD CONSTRAINT "PK_TreasuryAccounts" PRIMARY KEY ("Id");


--
-- TOC entry 5335 (class 2606 OID 21461)
-- Name: TreasuryTransactions PK_TreasuryTransactions; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."TreasuryTransactions"
    ADD CONSTRAINT "PK_TreasuryTransactions" PRIMARY KEY ("Id");


--
-- TOC entry 5343 (class 2606 OID 21518)
-- Name: Units PK_Units; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Units"
    ADD CONSTRAINT "PK_Units" PRIMARY KEY ("Id");


--
-- TOC entry 6131 (class 2606 OID 38068)
-- Name: UserGovernanceAuditLogs PK_UserGovernanceAuditLogs; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."UserGovernanceAuditLogs"
    ADD CONSTRAINT "PK_UserGovernanceAuditLogs" PRIMARY KEY ("Id");


--
-- TOC entry 6135 (class 2606 OID 38087)
-- Name: UserGovernanceBlacklistEntries PK_UserGovernanceBlacklistEntries; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."UserGovernanceBlacklistEntries"
    ADD CONSTRAINT "PK_UserGovernanceBlacklistEntries" PRIMARY KEY ("Id");


--
-- TOC entry 6126 (class 2606 OID 38049)
-- Name: UserGovernanceProfiles PK_UserGovernanceProfiles; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."UserGovernanceProfiles"
    ADD CONSTRAINT "PK_UserGovernanceProfiles" PRIMARY KEY ("Id");


--
-- TOC entry 5122 (class 2606 OID 20081)
-- Name: Users PK_Users; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Users"
    ADD CONSTRAINT "PK_Users" PRIMARY KEY ("Id");


--
-- TOC entry 5341 (class 2606 OID 21483)
-- Name: VisaTransactions PK_VisaTransactions; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."VisaTransactions"
    ADD CONSTRAINT "PK_VisaTransactions" PRIMARY KEY ("Id");


--
-- TOC entry 5212 (class 2606 OID 20456)
-- Name: WarehouseStocks PK_WarehouseStocks; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."WarehouseStocks"
    ADD CONSTRAINT "PK_WarehouseStocks" PRIMARY KEY ("Id");


--
-- TOC entry 5124 (class 2606 OID 20094)
-- Name: Warehouses PK_Warehouses; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Warehouses"
    ADD CONSTRAINT "PK_Warehouses" PRIMARY KEY ("Id");


--
-- TOC entry 5917 (class 2606 OID 36913)
-- Name: __CommerceHubCategorySeparation_CloneMap PK___CommerceHubCategorySeparation_CloneMap; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."__CommerceHubCategorySeparation_CloneMap"
    ADD CONSTRAINT "PK___CommerceHubCategorySeparation_CloneMap" PRIMARY KEY ("LegacyCategoryId", "TenantId");


--
-- TOC entry 5919 (class 2606 OID 36924)
-- Name: __CommerceHubCategorySeparation_MigratedCategories PK___CommerceHubCategorySeparation_MigratedCategories; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."__CommerceHubCategorySeparation_MigratedCategories"
    ADD CONSTRAINT "PK___CommerceHubCategorySeparation_MigratedCategories" PRIMARY KEY ("LegacyCategoryId");


--
-- TOC entry 5921 (class 2606 OID 36999)
-- Name: __DefaultVariantBackfill_CreatedVariants PK___DefaultVariantBackfill_CreatedVariants; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."__DefaultVariantBackfill_CreatedVariants"
    ADD CONSTRAINT "PK___DefaultVariantBackfill_CreatedVariants" PRIMARY KEY ("ProductId");


--
-- TOC entry 5099 (class 2606 OID 20011)
-- Name: __EFMigrationsHistory PK___EFMigrationsHistory; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."__EFMigrationsHistory"
    ADD CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId");


--
-- TOC entry 5593 (class 2606 OID 34705)
-- Name: whatsapp_accounts PK_whatsapp_accounts; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.whatsapp_accounts
    ADD CONSTRAINT "PK_whatsapp_accounts" PRIMARY KEY (id);


--
-- TOC entry 5601 (class 2606 OID 34735)
-- Name: whatsapp_message_templates PK_whatsapp_message_templates; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.whatsapp_message_templates
    ADD CONSTRAINT "PK_whatsapp_message_templates" PRIMARY KEY ("Id");


--
-- TOC entry 6352 (class 2606 OID 3022547)
-- Name: PaymentCancellationOutboxMessages PaymentCancellationOutboxMessages_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PaymentCancellationOutboxMessages"
    ADD CONSTRAINT "PaymentCancellationOutboxMessages_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 5591 (class 2606 OID 34672)
-- Name: ProductPixelOverrides ProductPixelOverrides_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ProductPixelOverrides"
    ADD CONSTRAINT "ProductPixelOverrides_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 5739 (class 2606 OID 35702)
-- Name: ProviderTrustAggregates ProviderTrustAggregates_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ProviderTrustAggregates"
    ADD CONSTRAINT "ProviderTrustAggregates_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 6279 (class 2606 OID 2287517)
-- Name: RevokedCustomerAccessTokens RevokedCustomerAccessTokens_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."RevokedCustomerAccessTokens"
    ADD CONSTRAINT "RevokedCustomerAccessTokens_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 6284 (class 2606 OID 2287537)
-- Name: RevokedStorefrontAccessTokens RevokedStorefrontAccessTokens_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."RevokedStorefrontAccessTokens"
    ADD CONSTRAINT "RevokedStorefrontAccessTokens_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 5734 (class 2606 OID 35643)
-- Name: ServiceRequestMessages ServiceRequestMessages_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ServiceRequestMessages"
    ADD CONSTRAINT "ServiceRequestMessages_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 5719 (class 2606 OID 35530)
-- Name: ServiceRequests ServiceRequests_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ServiceRequests"
    ADD CONSTRAINT "ServiceRequests_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 5725 (class 2606 OID 35575)
-- Name: ServiceReviews ServiceReviews_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ServiceReviews"
    ADD CONSTRAINT "ServiceReviews_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 5710 (class 2606 OID 35481)
-- Name: Services Services_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Services"
    ADD CONSTRAINT "Services_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 5546 (class 2606 OID 26035)
-- Name: StoreConfigAuditEntries StoreConfigAuditEntries_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StoreConfigAuditEntries"
    ADD CONSTRAINT "StoreConfigAuditEntries_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 5518 (class 2606 OID 25820)
-- Name: StorefrontAuthProviderSettings StorefrontAuthProviderSettings_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StorefrontAuthProviderSettings"
    ADD CONSTRAINT "StorefrontAuthProviderSettings_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 5606 (class 2606 OID 34835)
-- Name: TenantAutomationSettings TenantAutomationSettings_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."TenantAutomationSettings"
    ADD CONSTRAINT "TenantAutomationSettings_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 6328 (class 2606 OID 2287838)
-- Name: TenantCurrencyNormalizationAudit TenantCurrencyNormalizationAudit_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."TenantCurrencyNormalizationAudit"
    ADD CONSTRAINT "TenantCurrencyNormalizationAudit_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 5485 (class 2606 OID 25547)
-- Name: ThemePresets ThemePresets_Key_key; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ThemePresets"
    ADD CONSTRAINT "ThemePresets_Key_key" UNIQUE ("Key");


--
-- TOC entry 5487 (class 2606 OID 25545)
-- Name: ThemePresets ThemePresets_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ThemePresets"
    ADD CONSTRAINT "ThemePresets_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 5622 (class 2606 OID 34872)
-- Name: AbandonedCarts UQ_AbandonedCarts_TenantId_CartId; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AbandonedCarts"
    ADD CONSTRAINT "UQ_AbandonedCarts_TenantId_CartId" UNIQUE ("TenantId", "CartId");


--
-- TOC entry 5780 (class 2606 OID 36009)
-- Name: AffiliateAttributions UQ_AffiliateAttributions_Tenant_Order; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliateAttributions"
    ADD CONSTRAINT "UQ_AffiliateAttributions_Tenant_Order" UNIQUE ("TenantId", "OrderId");


--
-- TOC entry 5812 (class 2606 OID 36282)
-- Name: AffiliateCategories UQ_AffiliateCategories_Parent_Name; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliateCategories"
    ADD CONSTRAINT "UQ_AffiliateCategories_Parent_Name" UNIQUE ("ParentId", "Name");


--
-- TOC entry 5814 (class 2606 OID 36280)
-- Name: AffiliateCategories UQ_AffiliateCategories_Slug; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliateCategories"
    ADD CONSTRAINT "UQ_AffiliateCategories_Slug" UNIQUE ("Slug");


--
-- TOC entry 5704 (class 2606 OID 35449)
-- Name: MarketplaceProfiles UQ_MarketplaceProfiles_TenantUser; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."MarketplaceProfiles"
    ADD CONSTRAINT "UQ_MarketplaceProfiles_TenantUser" UNIQUE ("TenantId", "UserId");


--
-- TOC entry 5741 (class 2606 OID 35704)
-- Name: ProviderTrustAggregates UQ_ProviderTrustAggregates_ProviderProfile; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ProviderTrustAggregates"
    ADD CONSTRAINT "UQ_ProviderTrustAggregates_ProviderProfile" UNIQUE ("ProviderProfileId");


--
-- TOC entry 5727 (class 2606 OID 35577)
-- Name: ServiceReviews UQ_ServiceReviews_ServiceRequest; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ServiceReviews"
    ADD CONSTRAINT "UQ_ServiceReviews_ServiceRequest" UNIQUE ("ServiceRequestId");


--
-- TOC entry 5608 (class 2606 OID 34837)
-- Name: TenantAutomationSettings UQ_TenantAutomationSettings_TenantId; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."TenantAutomationSettings"
    ADD CONSTRAINT "UQ_TenantAutomationSettings_TenantId" UNIQUE ("TenantId");


--
-- TOC entry 5640 (class 2606 OID 34968)
-- Name: WhatsAppRateLimits UQ_WhatsAppRateLimits_TenantId_PhoneNumber; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."WhatsAppRateLimits"
    ADD CONSTRAINT "UQ_WhatsAppRateLimits_TenantId_PhoneNumber" UNIQUE ("TenantId", "PhoneNumber");


--
-- TOC entry 5642 (class 2606 OID 34966)
-- Name: WhatsAppRateLimits WhatsAppRateLimits_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."WhatsAppRateLimits"
    ADD CONSTRAINT "WhatsAppRateLimits_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 6267 (class 2606 OID 55415)
-- Name: _LegacyCashMigrationLog _LegacyCashMigrationLog_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."_LegacyCashMigrationLog"
    ADD CONSTRAINT "_LegacyCashMigrationLog_pkey" PRIMARY KEY ("Id");


--
-- TOC entry 5692 (class 2606 OID 35340)
-- Name: __MigrationHistory __MigrationHistory_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."__MigrationHistory"
    ADD CONSTRAINT "__MigrationHistory_pkey" PRIMARY KEY ("MigrationId");


--
-- TOC entry 5217 (class 1259 OID 20887)
-- Name: ix_hangfire_counter_expireat; Type: INDEX; Schema: hangfire; Owner: postgres
--

CREATE INDEX ix_hangfire_counter_expireat ON hangfire.counter USING btree (expireat);


--
-- TOC entry 5218 (class 1259 OID 20831)
-- Name: ix_hangfire_counter_key; Type: INDEX; Schema: hangfire; Owner: postgres
--

CREATE INDEX ix_hangfire_counter_key ON hangfire.counter USING btree (key);


--
-- TOC entry 5223 (class 1259 OID 20896)
-- Name: ix_hangfire_hash_expireat; Type: INDEX; Schema: hangfire; Owner: postgres
--

CREATE INDEX ix_hangfire_hash_expireat ON hangfire.hash USING btree (expireat);


--
-- TOC entry 5224 (class 1259 OID 20914)
-- Name: ix_hangfire_job_expireat; Type: INDEX; Schema: hangfire; Owner: postgres
--

CREATE INDEX ix_hangfire_job_expireat ON hangfire.job USING btree (expireat);


--
-- TOC entry 5225 (class 1259 OID 20841)
-- Name: ix_hangfire_job_statename; Type: INDEX; Schema: hangfire; Owner: postgres
--

CREATE INDEX ix_hangfire_job_statename ON hangfire.job USING btree (statename);


--
-- TOC entry 5226 (class 1259 OID 21005)
-- Name: ix_hangfire_job_statename_is_not_null; Type: INDEX; Schema: hangfire; Owner: postgres
--

CREATE INDEX ix_hangfire_job_statename_is_not_null ON hangfire.job USING btree (statename) INCLUDE (id) WHERE (statename IS NOT NULL);


--
-- TOC entry 5248 (class 1259 OID 20849)
-- Name: ix_hangfire_jobparameter_jobidandname; Type: INDEX; Schema: hangfire; Owner: postgres
--

CREATE INDEX ix_hangfire_jobparameter_jobidandname ON hangfire.jobparameter USING btree (jobid, name);


--
-- TOC entry 5232 (class 1259 OID 21004)
-- Name: ix_hangfire_jobqueue_fetchedat_queue_jobid; Type: INDEX; Schema: hangfire; Owner: postgres
--

CREATE INDEX ix_hangfire_jobqueue_fetchedat_queue_jobid ON hangfire.jobqueue USING btree (fetchedat NULLS FIRST, queue, jobid);


--
-- TOC entry 5233 (class 1259 OID 20800)
-- Name: ix_hangfire_jobqueue_jobidandqueue; Type: INDEX; Schema: hangfire; Owner: postgres
--

CREATE INDEX ix_hangfire_jobqueue_jobidandqueue ON hangfire.jobqueue USING btree (jobid, queue);


--
-- TOC entry 5234 (class 1259 OID 20923)
-- Name: ix_hangfire_jobqueue_queueandfetchedat; Type: INDEX; Schema: hangfire; Owner: postgres
--

CREATE INDEX ix_hangfire_jobqueue_queueandfetchedat ON hangfire.jobqueue USING btree (queue, fetchedat);


--
-- TOC entry 5237 (class 1259 OID 20934)
-- Name: ix_hangfire_list_expireat; Type: INDEX; Schema: hangfire; Owner: postgres
--

CREATE INDEX ix_hangfire_list_expireat ON hangfire.list USING btree (expireat);


--
-- TOC entry 5242 (class 1259 OID 20955)
-- Name: ix_hangfire_set_expireat; Type: INDEX; Schema: hangfire; Owner: postgres
--

CREATE INDEX ix_hangfire_set_expireat ON hangfire.set USING btree (expireat);


--
-- TOC entry 5243 (class 1259 OID 20865)
-- Name: ix_hangfire_set_key_score; Type: INDEX; Schema: hangfire; Owner: postgres
--

CREATE INDEX ix_hangfire_set_key_score ON hangfire.set USING btree (key, score);


--
-- TOC entry 5229 (class 1259 OID 20749)
-- Name: ix_hangfire_state_jobid; Type: INDEX; Schema: hangfire; Owner: postgres
--

CREATE INDEX ix_hangfire_state_jobid ON hangfire.state USING btree (jobid);


--
-- TOC entry 5632 (class 1259 OID 34947)
-- Name: IX_AbandonedCartRecoveryLogs_AbandonedCartId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AbandonedCartRecoveryLogs_AbandonedCartId" ON public."AbandonedCartRecoveryLogs" USING btree ("AbandonedCartId");


--
-- TOC entry 5633 (class 1259 OID 34949)
-- Name: IX_AbandonedCartRecoveryLogs_CreatedAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AbandonedCartRecoveryLogs_CreatedAtUtc" ON public."AbandonedCartRecoveryLogs" USING btree ("TenantId", "CreatedAtUtc");


--
-- TOC entry 5634 (class 1259 OID 34946)
-- Name: IX_AbandonedCartRecoveryLogs_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AbandonedCartRecoveryLogs_TenantId" ON public."AbandonedCartRecoveryLogs" USING btree ("TenantId");


--
-- TOC entry 5635 (class 1259 OID 2287547)
-- Name: IX_AbandonedCartRecoveryLogs_TenantId_IdempotencyKey_Unique; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_AbandonedCartRecoveryLogs_TenantId_IdempotencyKey_Unique" ON public."AbandonedCartRecoveryLogs" USING btree ("TenantId", "IdempotencyKey") WHERE ("IdempotencyKey" IS NOT NULL);


--
-- TOC entry 6858 (class 0 OID 0)
-- Dependencies: 5635
-- Name: INDEX "IX_AbandonedCartRecoveryLogs_TenantId_IdempotencyKey_Unique"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON INDEX public."IX_AbandonedCartRecoveryLogs_TenantId_IdempotencyKey_Unique" IS 'Enforces idempotency guarantee under concurrent workflow executions. Prevents duplicate telemetry logs for the same workflow run.';


--
-- TOC entry 5611 (class 1259 OID 34889)
-- Name: IX_AbandonedCarts_CartId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AbandonedCarts_CartId" ON public."AbandonedCarts" USING btree ("CartId");


--
-- TOC entry 5612 (class 1259 OID 34893)
-- Name: IX_AbandonedCarts_CustomerId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AbandonedCarts_CustomerId" ON public."AbandonedCarts" USING btree ("CustomerId") WHERE ("CustomerId" IS NOT NULL);


--
-- TOC entry 5613 (class 1259 OID 34977)
-- Name: IX_AbandonedCarts_Expiration; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AbandonedCarts_Expiration" ON public."AbandonedCarts" USING btree ("TriggeredAtUtc") WHERE (("RecoveryStatus")::text = 'Triggered'::text);


--
-- TOC entry 5614 (class 1259 OID 34978)
-- Name: IX_AbandonedCarts_Failed; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AbandonedCarts_Failed" ON public."AbandonedCarts" USING btree ("RetryCount", "UpdatedAtUtc") WHERE ((("RecoveryStatus")::text = 'Failed'::text) AND ("RetryCount" < 3));


--
-- TOC entry 5615 (class 1259 OID 34976)
-- Name: IX_AbandonedCarts_Pending; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AbandonedCarts_Pending" ON public."AbandonedCarts" USING btree ("PendingAtUtc") WHERE (("RecoveryStatus")::text = 'Pending'::text);


--
-- TOC entry 5616 (class 1259 OID 34890)
-- Name: IX_AbandonedCarts_ReadyForAbandonment; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AbandonedCarts_ReadyForAbandonment" ON public."AbandonedCarts" USING btree ("RecoveryStatus", "LastActivityAtUtc") WHERE (("RecoveryStatus")::text = 'Active'::text);


--
-- TOC entry 5617 (class 1259 OID 34892)
-- Name: IX_AbandonedCarts_Recovered; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AbandonedCarts_Recovered" ON public."AbandonedCarts" USING btree ("TenantId", "RecoveredAtUtc") WHERE (("RecoveryStatus")::text = 'Recovered'::text);


--
-- TOC entry 5618 (class 1259 OID 34888)
-- Name: IX_AbandonedCarts_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AbandonedCarts_TenantId" ON public."AbandonedCarts" USING btree ("TenantId");


--
-- TOC entry 5619 (class 1259 OID 3022477)
-- Name: IX_AbandonedCarts_TenantId_SessionId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AbandonedCarts_TenantId_SessionId" ON public."AbandonedCarts" USING btree ("TenantId", "SessionId") WHERE ("SessionId" IS NOT NULL);


--
-- TOC entry 5620 (class 1259 OID 34891)
-- Name: IX_AbandonedCarts_Triggered; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AbandonedCarts_Triggered" ON public."AbandonedCarts" USING btree ("TenantId", "TriggeredAtUtc") WHERE (("RecoveryStatus")::text = 'Triggered'::text);


--
-- TOC entry 5368 (class 1259 OID 23294)
-- Name: IX_AccountBalances_AccountId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AccountBalances_AccountId" ON public."AccountBalances" USING btree ("AccountId");


--
-- TOC entry 5369 (class 1259 OID 23295)
-- Name: IX_AccountBalances_AccountId_PeriodKey; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_AccountBalances_AccountId_PeriodKey" ON public."AccountBalances" USING btree ("AccountId", "PeriodKey");


--
-- TOC entry 5370 (class 1259 OID 23296)
-- Name: IX_AccountBalances_PeriodKey; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AccountBalances_PeriodKey" ON public."AccountBalances" USING btree ("PeriodKey");


--
-- TOC entry 5293 (class 1259 OID 55339)
-- Name: IX_AccountLedgers_SupplierOpeningBalance_Once; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_AccountLedgers_SupplierOpeningBalance_Once" ON public."AccountLedgers" USING btree ("TenantId", "PartyType", "PartyId", "TransactionType") WHERE (("IsDeleted" = false) AND ("PartyType" = 1) AND ("TransactionType" = 11));


--
-- TOC entry 5294 (class 1259 OID 21213)
-- Name: IX_AccountLedgers_UserId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AccountLedgers_UserId" ON public."AccountLedgers" USING btree ("UserId");


--
-- TOC entry 5373 (class 1259 OID 23297)
-- Name: IX_AccountMappings_AccountId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AccountMappings_AccountId" ON public."AccountMappings" USING btree ("AccountId");


--
-- TOC entry 5374 (class 1259 OID 23298)
-- Name: IX_AccountMappings_MappingKey; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_AccountMappings_MappingKey" ON public."AccountMappings" USING btree ("MappingKey");


--
-- TOC entry 5679 (class 1259 OID 3022075)
-- Name: IX_AccountingOutbox_AggregateVersionBlockers; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AccountingOutbox_AggregateVersionBlockers" ON public."AccountingOutboxMessages" USING btree ("TenantId", "BusinessEntityId", "AggregateVersion", "OccurredAtUtc", "CreatedAt", "Id", "ProcessedAtUtc", "IsFailed", "EventType") WHERE ("ProcessedAtUtc" IS NULL);


--
-- TOC entry 5680 (class 1259 OID 3022074)
-- Name: IX_AccountingOutbox_AggregateVersionOrdering; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AccountingOutbox_AggregateVersionOrdering" ON public."AccountingOutboxMessages" USING btree ("TenantId", "BusinessEntityId", "AggregateVersion", "OccurredAtUtc", "CreatedAt", "Id") WHERE (("ProcessedAtUtc" IS NULL) AND ("IsFailed" = false));


--
-- TOC entry 5681 (class 1259 OID 35286)
-- Name: IX_AccountingOutbox_Failed; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AccountingOutbox_Failed" ON public."AccountingOutboxMessages" USING btree ("TenantId", "IsFailed") WHERE ("IsFailed" = true);


--
-- TOC entry 5682 (class 1259 OID 35287)
-- Name: IX_AccountingOutbox_OccurredAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AccountingOutbox_OccurredAtUtc" ON public."AccountingOutboxMessages" USING btree ("OccurredAtUtc");


--
-- TOC entry 5683 (class 1259 OID 2287697)
-- Name: IX_AccountingOutbox_ProcessingStatus; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AccountingOutbox_ProcessingStatus" ON public."AccountingOutboxMessages" USING btree ("ProcessedAtUtc", "IsFailed", "NextRetryAtUtc", "ClaimedUntilUtc") WHERE (("ProcessedAtUtc" IS NULL) AND ("IsFailed" = false));


--
-- TOC entry 5684 (class 1259 OID 35284)
-- Name: IX_AccountingOutbox_TenantId_IdempotencyKey; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_AccountingOutbox_TenantId_IdempotencyKey" ON public."AccountingOutboxMessages" USING btree ("TenantId", "IdempotencyKey");


--
-- TOC entry 5685 (class 1259 OID 3022309)
-- Name: IX_AccountingOutbox_Tenant_SourceType_Entity; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AccountingOutbox_Tenant_SourceType_Entity" ON public."AccountingOutboxMessages" USING btree ("TenantId", "SourceType", "BusinessEntityId");


--
-- TOC entry 5353 (class 1259 OID 23299)
-- Name: IX_Accounts_Code; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_Accounts_Code" ON public."Accounts" USING btree ("Code") WHERE ("IsDeleted" = false);


--
-- TOC entry 5354 (class 1259 OID 23300)
-- Name: IX_Accounts_IsActive; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Accounts_IsActive" ON public."Accounts" USING btree ("IsActive");


--
-- TOC entry 5355 (class 1259 OID 23301)
-- Name: IX_Accounts_ParentAccountId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Accounts_ParentAccountId" ON public."Accounts" USING btree ("ParentAccountId");


--
-- TOC entry 5356 (class 1259 OID 23302)
-- Name: IX_Accounts_Type; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Accounts_Type" ON public."Accounts" USING btree ("Type");


--
-- TOC entry 5760 (class 1259 OID 36206)
-- Name: IX_AffiliateAccounts_PayoutWallet; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliateAccounts_PayoutWallet" ON public."AffiliateAccounts" USING btree ("TenantId", "PayoutWalletNumber");


--
-- TOC entry 5761 (class 1259 OID 35956)
-- Name: IX_AffiliateAccounts_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliateAccounts_TenantId" ON public."AffiliateAccounts" USING btree ("TenantId");


--
-- TOC entry 5774 (class 1259 OID 36025)
-- Name: IX_AffiliateAttributions_AffiliateAccountId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliateAttributions_AffiliateAccountId" ON public."AffiliateAttributions" USING btree ("AffiliateAccountId");


--
-- TOC entry 5775 (class 1259 OID 36126)
-- Name: IX_AffiliateAttributions_OrderId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliateAttributions_OrderId" ON public."AffiliateAttributions" USING btree ("OrderId");


--
-- TOC entry 5776 (class 1259 OID 36201)
-- Name: IX_AffiliateAttributions_PromotionLinkId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliateAttributions_PromotionLinkId" ON public."AffiliateAttributions" USING btree ("PromotionLinkId") WHERE ("PromotionLinkId" IS NOT NULL);


--
-- TOC entry 5777 (class 1259 OID 36026)
-- Name: IX_AffiliateAttributions_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliateAttributions_TenantId" ON public."AffiliateAttributions" USING btree ("TenantId");


--
-- TOC entry 5778 (class 1259 OID 3022526)
-- Name: IX_AffiliateAttributions_Tenant_Order_Active; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliateAttributions_Tenant_Order_Active" ON public."AffiliateAttributions" USING btree ("TenantId", "OrderId", "IsDeleted");


--
-- TOC entry 5821 (class 1259 OID 36362)
-- Name: IX_AffiliateBalanceLedger_AffiliateAccountId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliateBalanceLedger_AffiliateAccountId" ON public."AffiliateBalanceLedger" USING btree ("AffiliateAccountId");


--
-- TOC entry 5822 (class 1259 OID 36364)
-- Name: IX_AffiliateBalanceLedger_ReferenceId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliateBalanceLedger_ReferenceId" ON public."AffiliateBalanceLedger" USING btree ("ReferenceId") WHERE ("ReferenceId" IS NOT NULL);


--
-- TOC entry 5823 (class 1259 OID 36363)
-- Name: IX_AffiliateBalanceLedger_StoreTenantRecorded; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliateBalanceLedger_StoreTenantRecorded" ON public."AffiliateBalanceLedger" USING btree ("StoreTenantId", "RecordedAtUtc" DESC);


--
-- TOC entry 5808 (class 1259 OID 36290)
-- Name: IX_AffiliateCategories_DisplayOrder; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliateCategories_DisplayOrder" ON public."AffiliateCategories" USING btree ("DisplayOrder");


--
-- TOC entry 5809 (class 1259 OID 36289)
-- Name: IX_AffiliateCategories_IsActive; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliateCategories_IsActive" ON public."AffiliateCategories" USING btree ("IsActive");


--
-- TOC entry 5810 (class 1259 OID 36288)
-- Name: IX_AffiliateCategories_ParentId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliateCategories_ParentId" ON public."AffiliateCategories" USING btree ("ParentId");


--
-- TOC entry 5783 (class 1259 OID 36087)
-- Name: IX_AffiliateCommLedger_AffiliateAccountId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliateCommLedger_AffiliateAccountId" ON public."AffiliateCommissionLedger" USING btree ("AffiliateAccountId");


--
-- TOC entry 5784 (class 1259 OID 36088)
-- Name: IX_AffiliateCommLedger_OrderId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliateCommLedger_OrderId" ON public."AffiliateCommissionLedger" USING btree ("OrderId");


--
-- TOC entry 5785 (class 1259 OID 36090)
-- Name: IX_AffiliateCommLedger_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliateCommLedger_TenantId" ON public."AffiliateCommissionLedger" USING btree ("TenantId");


--
-- TOC entry 5786 (class 1259 OID 36089)
-- Name: IX_AffiliateCommLedger_TenantRecorded; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliateCommLedger_TenantRecorded" ON public."AffiliateCommissionLedger" USING btree ("TenantId", "RecordedAtUtc" DESC);


--
-- TOC entry 5787 (class 1259 OID 36127)
-- Name: IX_AffiliateCommissionLedger_AttributionId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliateCommissionLedger_AttributionId" ON public."AffiliateCommissionLedger" USING btree ("AttributionId");


--
-- TOC entry 5788 (class 1259 OID 36128)
-- Name: IX_AffiliateCommissionLedger_OrderItemId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliateCommissionLedger_OrderItemId" ON public."AffiliateCommissionLedger" USING btree ("OrderItemId");


--
-- TOC entry 5789 (class 1259 OID 36129)
-- Name: IX_AffiliateCommissionLedger_OriginalEntryId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliateCommissionLedger_OriginalEntryId" ON public."AffiliateCommissionLedger" USING btree ("OriginalEntryId");


--
-- TOC entry 5815 (class 1259 OID 36325)
-- Name: IX_AffiliatePayoutRequests_AffiliateId_Status; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliatePayoutRequests_AffiliateId_Status" ON public."AffiliatePayoutRequests" USING btree ("AffiliateAccountId", "Status");


--
-- TOC entry 5816 (class 1259 OID 3022307)
-- Name: IX_AffiliatePayoutRequests_PaymentReference_Paid_Unique; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_AffiliatePayoutRequests_PaymentReference_Paid_Unique" ON public."AffiliatePayoutRequests" USING btree ("StoreTenantId", "PaymentReference") WHERE (("Status" = 4) AND ("PaymentReference" IS NOT NULL));


--
-- TOC entry 5817 (class 1259 OID 36326)
-- Name: IX_AffiliatePayoutRequests_TenantId_AffiliateId_Status; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliatePayoutRequests_TenantId_AffiliateId_Status" ON public."AffiliatePayoutRequests" USING btree ("StoreTenantId", "AffiliateAccountId", "Status");


--
-- TOC entry 5818 (class 1259 OID 36324)
-- Name: IX_AffiliatePayoutRequests_TenantId_Status; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliatePayoutRequests_TenantId_Status" ON public."AffiliatePayoutRequests" USING btree ("StoreTenantId", "Status");


--
-- TOC entry 5768 (class 1259 OID 36296)
-- Name: IX_AffiliateProductPolicies_CategoryId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliateProductPolicies_CategoryId" ON public."AffiliateProductPolicies" USING btree ("AffiliateCategoryId");


--
-- TOC entry 5769 (class 1259 OID 36130)
-- Name: IX_AffiliateProductPolicies_ProductId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliateProductPolicies_ProductId" ON public."AffiliateProductPolicies" USING btree ("ProductId");


--
-- TOC entry 5770 (class 1259 OID 35987)
-- Name: IX_AffiliateProductPolicies_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliateProductPolicies_TenantId" ON public."AffiliateProductPolicies" USING btree ("TenantId");


--
-- TOC entry 5797 (class 1259 OID 36187)
-- Name: IX_AffiliatePromotionLinks_AffiliateUserId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliatePromotionLinks_AffiliateUserId" ON public."AffiliatePromotionLinks" USING btree ("AffiliateUserId");


--
-- TOC entry 5798 (class 1259 OID 36189)
-- Name: IX_AffiliatePromotionLinks_Code_Store; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliatePromotionLinks_Code_Store" ON public."AffiliatePromotionLinks" USING btree ("ReferralCode", "TenantId");


--
-- TOC entry 5799 (class 1259 OID 36188)
-- Name: IX_AffiliatePromotionLinks_ProductId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliatePromotionLinks_ProductId" ON public."AffiliatePromotionLinks" USING btree ("ProductId");


--
-- TOC entry 5800 (class 1259 OID 36186)
-- Name: IX_AffiliatePromotionLinks_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliatePromotionLinks_TenantId" ON public."AffiliatePromotionLinks" USING btree ("TenantId");


--
-- TOC entry 5792 (class 1259 OID 36131)
-- Name: IX_AffiliateUsers_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AffiliateUsers_TenantId" ON public."AffiliateUsers" USING btree ("TenantId");


--
-- TOC entry 5568 (class 1259 OID 34530)
-- Name: IX_AnalyticsAlertRules_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AnalyticsAlertRules_TenantId" ON public."AnalyticsAlertRules" USING btree ("TenantId");


--
-- TOC entry 5569 (class 1259 OID 34531)
-- Name: IX_AnalyticsAlertRules_TenantId_IsEnabled_IsDeleted; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AnalyticsAlertRules_TenantId_IsEnabled_IsDeleted" ON public."AnalyticsAlertRules" USING btree ("TenantId", "IsEnabled", "IsDeleted");


--
-- TOC entry 5348 (class 1259 OID 23068)
-- Name: IX_AuditLogs_ActionType; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AuditLogs_ActionType" ON public."AuditLogs" USING btree ("ActionType");


--
-- TOC entry 5349 (class 1259 OID 23069)
-- Name: IX_AuditLogs_EntityType; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AuditLogs_EntityType" ON public."AuditLogs" USING btree ("EntityType");


--
-- TOC entry 5350 (class 1259 OID 23070)
-- Name: IX_AuditLogs_TimestampUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AuditLogs_TimestampUtc" ON public."AuditLogs" USING btree ("TimestampUtc");


--
-- TOC entry 5516 (class 1259 OID 25821)
-- Name: IX_AuthProviderSettings_TenantId_Provider; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_AuthProviderSettings_TenantId_Provider" ON public."StorefrontAuthProviderSettings" USING btree ("TenantId", "Provider");


--
-- TOC entry 5511 (class 1259 OID 25807)
-- Name: IX_AuthProvider_TenantId_CustomerId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_AuthProvider_TenantId_CustomerId" ON public."OnlineCustomerAuthProviders" USING btree ("TenantId", "OnlineCustomerId");


--
-- TOC entry 5512 (class 1259 OID 25806)
-- Name: IX_AuthProvider_TenantId_CustomerId_Provider; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_AuthProvider_TenantId_CustomerId_Provider" ON public."OnlineCustomerAuthProviders" USING btree ("TenantId", "OnlineCustomerId", "Provider");


--
-- TOC entry 5513 (class 1259 OID 25805)
-- Name: IX_AuthProvider_TenantId_Provider_ProviderUserId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_AuthProvider_TenantId_Provider_ProviderUserId" ON public."OnlineCustomerAuthProviders" USING btree ("TenantId", "Provider", "ProviderUserId");


--
-- TOC entry 5321 (class 1259 OID 21390)
-- Name: IX_BankTransactions_BankId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_BankTransactions_BankId" ON public."BankTransactions" USING btree ("BankId");


--
-- TOC entry 5318 (class 1259 OID 24694)
-- Name: IX_Banks_TenantId_BankName; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_Banks_TenantId_BankName" ON public."Banks" USING btree ("TenantId", "BankName") WHERE ("IsDeleted" = false);


--
-- TOC entry 6057 (class 1259 OID 37868)
-- Name: IX_CHFinancialTx_EntityType_Bucket_CreatedAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CHFinancialTx_EntityType_Bucket_CreatedAtUtc" ON public."CommerceHubFinancialTransactions" USING btree ("EntityType", "FinancialBucket", "CreatedAtUtc" DESC);


--
-- TOC entry 6058 (class 1259 OID 38028)
-- Name: IX_CHFinancialTx_EntityType_EntityId_Bucket_Currency_CreatedAtU; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CHFinancialTx_EntityType_EntityId_Bucket_Currency_CreatedAtU" ON public."CommerceHubFinancialTransactions" USING btree ("EntityType", "EntityId", "FinancialBucket", "Currency", "CreatedAtUtc" DESC);


--
-- TOC entry 6336 (class 1259 OID 3022204)
-- Name: IX_CHPaymentCompOutbox_OperatorReconciliation; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CHPaymentCompOutbox_OperatorReconciliation" ON public."CommerceHubPaymentCompensationOutboxMessages" USING btree ("RequiresOperatorReconciliation", "ProcessedAtUtc", "CreatedAt");


--
-- TOC entry 5954 (class 1259 OID 3022282)
-- Name: IX_CHVendorEarnings_Tenant_Status_AvailablePayout; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CHVendorEarnings_Tenant_Status_AvailablePayout" ON public."CommerceHubVendorEarnings" USING btree ("TenantId", "Status", "AvailableAtUtc", "CreatedAt") WHERE (("IsDeleted" = false) AND ("Amount" > (0)::numeric));


--
-- TOC entry 5572 (class 1259 OID 34552)
-- Name: IX_CampaignCosts_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CampaignCosts_TenantId" ON public."CampaignCosts" USING btree ("TenantId");


--
-- TOC entry 5573 (class 1259 OID 34554)
-- Name: IX_CampaignCosts_TenantId_PeriodStartUtc_PeriodEndUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CampaignCosts_TenantId_PeriodStartUtc_PeriodEndUtc" ON public."CampaignCosts" USING btree ("TenantId", "PeriodStartUtc", "PeriodEndUtc");


--
-- TOC entry 5574 (class 1259 OID 34553)
-- Name: IX_CampaignCosts_TenantId_Source_Medium_Campaign_PeriodStartUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CampaignCosts_TenantId_Source_Medium_Campaign_PeriodStartUtc" ON public."CampaignCosts" USING btree ("TenantId", "Source", "Medium", "Campaign", "PeriodStartUtc") WHERE ("IsDeleted" = false);


--
-- TOC entry 6310 (class 1259 OID 2287712)
-- Name: IX_CanonicalIdentityLinks_CanonicalIdentityId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CanonicalIdentityLinks_CanonicalIdentityId" ON public."CanonicalIdentityLinks" USING btree ("CanonicalIdentityId");


--
-- TOC entry 6311 (class 1259 OID 2287714)
-- Name: IX_CanonicalIdentityLinks_Source_DisplayKey; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CanonicalIdentityLinks_Source_DisplayKey" ON public."CanonicalIdentityLinks" USING btree ("SourceSystem", "DisplayKey") WHERE (("DisplayKey" IS NOT NULL) AND ("IsDeleted" = false) AND ("RevokedAtUtc" IS NULL));


--
-- TOC entry 6312 (class 1259 OID 2287713)
-- Name: IX_CanonicalIdentityLinks_Tenant_Subject; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CanonicalIdentityLinks_Tenant_Subject" ON public."CanonicalIdentityLinks" USING btree ("TenantId", "SubjectType", "SubjectId");


--
-- TOC entry 5577 (class 1259 OID 34596)
-- Name: IX_CarrierConfigurations_TenantId_CarrierCode; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CarrierConfigurations_TenantId_CarrierCode" ON public."CarrierConfigurations" USING btree ("TenantId", "CarrierCode") WHERE ("IsDeleted" = false);


--
-- TOC entry 5578 (class 1259 OID 34598)
-- Name: IX_CarrierConfigurations_TenantId_IsDefault; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CarrierConfigurations_TenantId_IsDefault" ON public."CarrierConfigurations" USING btree ("TenantId", "IsDefault") WHERE ("IsDefault" = true);


--
-- TOC entry 5579 (class 1259 OID 34597)
-- Name: IX_CarrierConfigurations_TenantId_IsEnabled_SortOrder; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CarrierConfigurations_TenantId_IsEnabled_SortOrder" ON public."CarrierConfigurations" USING btree ("TenantId", "IsEnabled", "SortOrder");


--
-- TOC entry 5265 (class 1259 OID 21077)
-- Name: IX_CashboxTransactions_CashboxId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CashboxTransactions_CashboxId" ON public."CashboxTransactions" USING btree ("CashboxId");


--
-- TOC entry 5266 (class 1259 OID 21078)
-- Name: IX_CashboxTransactions_CashboxId_TransactionDate; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CashboxTransactions_CashboxId_TransactionDate" ON public."CashboxTransactions" USING btree ("CashboxId", "TransactionDate");


--
-- TOC entry 5267 (class 1259 OID 21079)
-- Name: IX_CashboxTransactions_ReferenceId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CashboxTransactions_ReferenceId" ON public."CashboxTransactions" USING btree ("ReferenceId");


--
-- TOC entry 5268 (class 1259 OID 21080)
-- Name: IX_CashboxTransactions_TransactionDate; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CashboxTransactions_TransactionDate" ON public."CashboxTransactions" USING btree ("TransactionDate");


--
-- TOC entry 5269 (class 1259 OID 21081)
-- Name: IX_CashboxTransactions_TransactionType; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CashboxTransactions_TransactionType" ON public."CashboxTransactions" USING btree ("TransactionType");


--
-- TOC entry 5270 (class 1259 OID 21082)
-- Name: IX_CashboxTransactions_UserId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CashboxTransactions_UserId" ON public."CashboxTransactions" USING btree ("UserId");


--
-- TOC entry 5259 (class 1259 OID 21073)
-- Name: IX_Cashboxes_IsActive; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Cashboxes_IsActive" ON public."Cashboxes" USING btree ("IsActive");


--
-- TOC entry 5260 (class 1259 OID 21074)
-- Name: IX_Cashboxes_OpenedByUserId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Cashboxes_OpenedByUserId" ON public."Cashboxes" USING btree ("OpenedByUserId");


--
-- TOC entry 5261 (class 1259 OID 21075)
-- Name: IX_Cashboxes_WarehouseId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Cashboxes_WarehouseId" ON public."Cashboxes" USING btree ("WarehouseId");


--
-- TOC entry 5262 (class 1259 OID 21076)
-- Name: IX_Cashboxes_WarehouseId_IsActive; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Cashboxes_WarehouseId_IsActive" ON public."Cashboxes" USING btree ("WarehouseId", "IsActive");


--
-- TOC entry 5100 (class 1259 OID 20472)
-- Name: IX_Categories_ParentCategoryId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Categories_ParentCategoryId" ON public."Categories" USING btree ("ParentCategoryId");


--
-- TOC entry 5744 (class 1259 OID 35794)
-- Name: IX_ChatAuditLog_ServiceRequestId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ChatAuditLog_ServiceRequestId" ON public."ChatAuditLog" USING btree ("ServiceRequestId");


--
-- TOC entry 5745 (class 1259 OID 35796)
-- Name: IX_ChatAuditLog_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ChatAuditLog_TenantId" ON public."ChatAuditLog" USING btree ("TenantId");


--
-- TOC entry 5746 (class 1259 OID 35795)
-- Name: IX_ChatAuditLog_Timestamp; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ChatAuditLog_Timestamp" ON public."ChatAuditLog" USING btree ("Timestamp" DESC);


--
-- TOC entry 5827 (class 1259 OID 36408)
-- Name: IX_CheckoutSessions_CartId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CheckoutSessions_CartId" ON public."CheckoutSessions" USING btree ("CartId");


--
-- TOC entry 5828 (class 1259 OID 2287548)
-- Name: IX_CheckoutSessions_Country; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CheckoutSessions_Country" ON public."CheckoutSessions" USING btree ("Country") WHERE ("Country" IS NOT NULL);


--
-- TOC entry 6859 (class 0 OID 0)
-- Dependencies: 5828
-- Name: INDEX "IX_CheckoutSessions_Country"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON INDEX public."IX_CheckoutSessions_Country" IS 'Supports country-based filtering for abandoned checkout recovery and analytics.';


--
-- TOC entry 5829 (class 1259 OID 2287586)
-- Name: IX_CheckoutSessions_InvalidatedAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CheckoutSessions_InvalidatedAtUtc" ON public."CheckoutSessions" USING btree ("InvalidatedAtUtc") WHERE ("InvalidatedAtUtc" IS NULL);


--
-- TOC entry 5830 (class 1259 OID 36415)
-- Name: IX_CheckoutSessions_RecoveryToken; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CheckoutSessions_RecoveryToken" ON public."CheckoutSessions" USING btree ("RecoveryToken");


--
-- TOC entry 5831 (class 1259 OID 36409)
-- Name: IX_CheckoutSessions_Status_LastActivity; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CheckoutSessions_Status_LastActivity" ON public."CheckoutSessions" USING btree ("Status", "LastActivityAtUtc");


--
-- TOC entry 5832 (class 1259 OID 36410)
-- Name: IX_CheckoutSessions_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CheckoutSessions_TenantId" ON public."CheckoutSessions" USING btree ("TenantId");


--
-- TOC entry 5833 (class 1259 OID 36411)
-- Name: IX_CheckoutSessions_TenantId_CartId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CheckoutSessions_TenantId_CartId" ON public."CheckoutSessions" USING btree ("TenantId", "CartId");


--
-- TOC entry 5834 (class 1259 OID 55336)
-- Name: IX_CheckoutSessions_TenantId_Status_AbandonedAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CheckoutSessions_TenantId_Status_AbandonedAtUtc" ON public."CheckoutSessions" USING btree ("TenantId", "Status", "AbandonedAtUtc");


--
-- TOC entry 6171 (class 1259 OID 38380)
-- Name: IX_CmsPages_PageGroupId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CmsPages_PageGroupId" ON public."CmsPages" USING btree ("PageGroupId");


--
-- TOC entry 6172 (class 1259 OID 38382)
-- Name: IX_CmsPages_TenantId_IsDraft_Slug; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CmsPages_TenantId_IsDraft_Slug" ON public."CmsPages" USING btree ("TenantId", "IsDraft", "Slug");


--
-- TOC entry 6173 (class 1259 OID 38381)
-- Name: IX_CmsPages_TenantId_IsPublished_Slug; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CmsPages_TenantId_IsPublished_Slug" ON public."CmsPages" USING btree ("TenantId", "IsPublished", "Slug");


--
-- TOC entry 5688 (class 1259 OID 35317)
-- Name: IX_CommerceAudit_Business; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceAudit_Business" ON public."CommerceAccountingAudit" USING btree ("TenantId", "BusinessType", "BusinessId");


--
-- TOC entry 5689 (class 1259 OID 35318)
-- Name: IX_CommerceAudit_JournalId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceAudit_JournalId" ON public."CommerceAccountingAudit" USING btree ("JournalId");


--
-- TOC entry 5690 (class 1259 OID 35319)
-- Name: IX_CommerceAudit_TenantId_PostedAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceAudit_TenantId_PostedAtUtc" ON public."CommerceAccountingAudit" USING btree ("TenantId", "PostedAtUtc");


--
-- TOC entry 5922 (class 1259 OID 37040)
-- Name: IX_CommerceHubCarts_TenantId_OnlineCustomerId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CommerceHubCarts_TenantId_OnlineCustomerId" ON public."CommerceHubCarts" USING btree ("TenantId", "OnlineCustomerId") WHERE ("IsDeleted" = false);


--
-- TOC entry 5882 (class 1259 OID 36749)
-- Name: IX_CommerceHubCategories_ParentId_SortOrder; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubCategories_ParentId_SortOrder" ON public."CommerceHubCategories" USING btree ("ParentId", "SortOrder");


--
-- TOC entry 5886 (class 1259 OID 36751)
-- Name: IX_CommerceHubCategoryAttributes_CategoryId_SortOrder; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubCategoryAttributes_CategoryId_SortOrder" ON public."CommerceHubCategoryAttributes" USING btree ("CategoryId", "SortOrder");


--
-- TOC entry 5926 (class 1259 OID 3022284)
-- Name: IX_CommerceHubCheckoutIdempotency_CommerceHubCustomerId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubCheckoutIdempotency_CommerceHubCustomerId" ON public."CommerceHubCheckoutIdempotency" USING btree ("CommerceHubCustomerId") WHERE ("CommerceHubCustomerId" IS NOT NULL);


--
-- TOC entry 5927 (class 1259 OID 37057)
-- Name: IX_CommerceHubCheckoutIdempotency_Key; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CommerceHubCheckoutIdempotency_Key" ON public."CommerceHubCheckoutIdempotency" USING btree ("Key");


--
-- TOC entry 5928 (class 1259 OID 2287694)
-- Name: IX_CommerceHubCheckoutIdempotency_RequestHash; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubCheckoutIdempotency_RequestHash" ON public."CommerceHubCheckoutIdempotency" USING btree ("RequestHash");


--
-- TOC entry 5929 (class 1259 OID 37058)
-- Name: IX_CommerceHubCheckoutIdempotency_TenantId_OnlineCustomerId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubCheckoutIdempotency_TenantId_OnlineCustomerId" ON public."CommerceHubCheckoutIdempotency" USING btree ("TenantId", "OnlineCustomerId");


--
-- TOC entry 5930 (class 1259 OID 2287849)
-- Name: IX_CommerceHubCheckoutIdempotency_TenantId_RequestHash; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubCheckoutIdempotency_TenantId_RequestHash" ON public."CommerceHubCheckoutIdempotency" USING btree ("TenantId", "RequestHash");


--
-- TOC entry 6041 (class 1259 OID 37604)
-- Name: IX_CommerceHubCustomerPayoutDetails_CustomerId_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubCustomerPayoutDetails_CustomerId_CreatedAt" ON public."CommerceHubCustomerPayoutDetails" USING btree ("CustomerId", "CreatedAt" DESC);


--
-- TOC entry 6042 (class 1259 OID 37605)
-- Name: IX_CommerceHubCustomerPayoutDetails_CustomerId_Method_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubCustomerPayoutDetails_CustomerId_Method_CreatedAt" ON public."CommerceHubCustomerPayoutDetails" USING btree ("CustomerId", "Method", "CreatedAt" DESC);


--
-- TOC entry 6020 (class 1259 OID 37474)
-- Name: IX_CommerceHubCustomers_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubCustomers_CreatedAt" ON public."CommerceHubCustomers" USING btree ("CreatedAt");


--
-- TOC entry 6021 (class 1259 OID 37473)
-- Name: IX_CommerceHubCustomers_Email; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CommerceHubCustomers_Email" ON public."CommerceHubCustomers" USING btree ("Email") WHERE ("IsDeleted" = false);


--
-- TOC entry 6150 (class 1259 OID 38237)
-- Name: IX_CommerceHubDailyFinancialAggregates_CalculatedAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubDailyFinancialAggregates_CalculatedAtUtc" ON public."CommerceHubDailyFinancialAggregates" USING btree ("CalculatedAtUtc" DESC);


--
-- TOC entry 6151 (class 1259 OID 38238)
-- Name: IX_CommerceHubDailyFinancialAggregates_CurrencyCode_DateUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubDailyFinancialAggregates_CurrencyCode_DateUtc" ON public."CommerceHubDailyFinancialAggregates" USING btree ("CurrencyCode", "DateUtc" DESC);


--
-- TOC entry 6152 (class 1259 OID 38271)
-- Name: IX_CommerceHubDailyFinancialAggregates_IsValid_DateUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubDailyFinancialAggregates_IsValid_DateUtc" ON public."CommerceHubDailyFinancialAggregates" USING btree ("IsValid", "DateUtc" DESC);


--
-- TOC entry 5970 (class 1259 OID 37255)
-- Name: IX_CommerceHubFinancialAuditLogs_CreatedAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubFinancialAuditLogs_CreatedAtUtc" ON public."CommerceHubFinancialAuditLogs" USING btree ("CreatedAtUtc");


--
-- TOC entry 5971 (class 1259 OID 37366)
-- Name: IX_CommerceHubFinancialAuditLogs_EarningId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubFinancialAuditLogs_EarningId" ON public."CommerceHubFinancialAuditLogs" USING btree ("EarningId") WHERE ("EarningId" IS NOT NULL);


--
-- TOC entry 5972 (class 1259 OID 37256)
-- Name: IX_CommerceHubFinancialAuditLogs_MarketplaceOrderId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubFinancialAuditLogs_MarketplaceOrderId" ON public."CommerceHubFinancialAuditLogs" USING btree ("MarketplaceOrderId") WHERE ("MarketplaceOrderId" IS NOT NULL);


--
-- TOC entry 5973 (class 1259 OID 37365)
-- Name: IX_CommerceHubFinancialAuditLogs_PaymentId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubFinancialAuditLogs_PaymentId" ON public."CommerceHubFinancialAuditLogs" USING btree ("PaymentId") WHERE ("PaymentId" IS NOT NULL);


--
-- TOC entry 5974 (class 1259 OID 37367)
-- Name: IX_CommerceHubFinancialAuditLogs_PayoutId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubFinancialAuditLogs_PayoutId" ON public."CommerceHubFinancialAuditLogs" USING btree ("PayoutId") WHERE ("PayoutId" IS NOT NULL);


--
-- TOC entry 5975 (class 1259 OID 37368)
-- Name: IX_CommerceHubFinancialAuditLogs_RefundId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubFinancialAuditLogs_RefundId" ON public."CommerceHubFinancialAuditLogs" USING btree ("RefundId") WHERE ("RefundId" IS NOT NULL);


--
-- TOC entry 5976 (class 1259 OID 37257)
-- Name: IX_CommerceHubFinancialAuditLogs_SubOrderId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubFinancialAuditLogs_SubOrderId" ON public."CommerceHubFinancialAuditLogs" USING btree ("SubOrderId") WHERE ("SubOrderId" IS NOT NULL);


--
-- TOC entry 5977 (class 1259 OID 37364)
-- Name: IX_CommerceHubFinancialAuditLogs_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubFinancialAuditLogs_TenantId" ON public."CommerceHubFinancialAuditLogs" USING btree ("TenantId") WHERE ("TenantId" IS NOT NULL);


--
-- TOC entry 5978 (class 1259 OID 37258)
-- Name: IX_CommerceHubFinancialAuditLogs_Type; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubFinancialAuditLogs_Type" ON public."CommerceHubFinancialAuditLogs" USING btree ("Type");


--
-- TOC entry 6117 (class 1259 OID 38023)
-- Name: IX_CommerceHubFinancialOperationAudits_CorrelationId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubFinancialOperationAudits_CorrelationId" ON public."CommerceHubFinancialOperationAudits" USING btree ("CorrelationId");


--
-- TOC entry 6118 (class 1259 OID 38024)
-- Name: IX_CommerceHubFinancialOperationAudits_OperationType; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubFinancialOperationAudits_OperationType" ON public."CommerceHubFinancialOperationAudits" USING btree ("OperationType");


--
-- TOC entry 6119 (class 1259 OID 38025)
-- Name: IX_CommerceHubFinancialOperationAudits_ReferenceType_ReferenceI; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubFinancialOperationAudits_ReferenceType_ReferenceI" ON public."CommerceHubFinancialOperationAudits" USING btree ("ReferenceType", "ReferenceId");


--
-- TOC entry 6120 (class 1259 OID 38022)
-- Name: IX_CommerceHubFinancialOperationAudits_VendorId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubFinancialOperationAudits_VendorId" ON public."CommerceHubFinancialOperationAudits" USING btree ("VendorId");


--
-- TOC entry 6155 (class 1259 OID 38263)
-- Name: IX_CommerceHubFinancialReconciliationReports_Status_TimestampUt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubFinancialReconciliationReports_Status_TimestampUt" ON public."CommerceHubFinancialReconciliationReports" USING btree ("Status", "TimestampUtc" DESC);


--
-- TOC entry 6156 (class 1259 OID 38262)
-- Name: IX_CommerceHubFinancialReconciliationReports_TimestampUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubFinancialReconciliationReports_TimestampUtc" ON public."CommerceHubFinancialReconciliationReports" USING btree ("TimestampUtc" DESC);


--
-- TOC entry 6059 (class 1259 OID 37716)
-- Name: IX_CommerceHubFinancialTransactions_CorrelationId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubFinancialTransactions_CorrelationId" ON public."CommerceHubFinancialTransactions" USING btree ("CorrelationId");


--
-- TOC entry 6060 (class 1259 OID 37706)
-- Name: IX_CommerceHubFinancialTransactions_CreatedAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubFinancialTransactions_CreatedAtUtc" ON public."CommerceHubFinancialTransactions" USING btree ("CreatedAtUtc" DESC);


--
-- TOC entry 6061 (class 1259 OID 37759)
-- Name: IX_CommerceHubFinancialTransactions_EntityId_CreatedAtUtc_Vendo; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubFinancialTransactions_EntityId_CreatedAtUtc_Vendo" ON public."CommerceHubFinancialTransactions" USING btree ("EntityId", "CreatedAtUtc" DESC) WHERE (("EntityType" = 1) AND ("EntityId" IS NOT NULL));


--
-- TOC entry 6062 (class 1259 OID 37704)
-- Name: IX_CommerceHubFinancialTransactions_EntityId_EntityType; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubFinancialTransactions_EntityId_EntityType" ON public."CommerceHubFinancialTransactions" USING btree ("EntityId", "EntityType");


--
-- TOC entry 6063 (class 1259 OID 37711)
-- Name: IX_CommerceHubFinancialTransactions_EntityType_EntityId_Created; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubFinancialTransactions_EntityType_EntityId_Created" ON public."CommerceHubFinancialTransactions" USING btree ("EntityType", "EntityId", "CreatedAtUtc" DESC);


--
-- TOC entry 6064 (class 1259 OID 37757)
-- Name: IX_CommerceHubFinancialTransactions_EntityType_EntityId_Transac; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubFinancialTransactions_EntityType_EntityId_Transac" ON public."CommerceHubFinancialTransactions" USING btree ("EntityType", "EntityId", "TransactionType", "CreatedAtUtc" DESC);


--
-- TOC entry 6065 (class 1259 OID 37705)
-- Name: IX_CommerceHubFinancialTransactions_ReferenceId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubFinancialTransactions_ReferenceId" ON public."CommerceHubFinancialTransactions" USING btree ("ReferenceId");


--
-- TOC entry 6066 (class 1259 OID 38239)
-- Name: IX_CommerceHubFinancialTransactions_ReferenceType_ReferenceId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubFinancialTransactions_ReferenceType_ReferenceId" ON public."CommerceHubFinancialTransactions" USING btree ("ReferenceType", "ReferenceId");


--
-- TOC entry 6067 (class 1259 OID 38240)
-- Name: IX_CommerceHubFinancialTransactions_ReferenceType_TransactionTy; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubFinancialTransactions_ReferenceType_TransactionTy" ON public."CommerceHubFinancialTransactions" USING btree ("ReferenceType", "TransactionType", "Direction", "CreatedAtUtc" DESC);


--
-- TOC entry 6068 (class 1259 OID 37758)
-- Name: IX_CommerceHubFinancialTransactions_TransactionType; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubFinancialTransactions_TransactionType" ON public."CommerceHubFinancialTransactions" USING btree ("TransactionType");


--
-- TOC entry 5901 (class 1259 OID 36831)
-- Name: IX_CommerceHubListings_ProductId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubListings_ProductId" ON public."CommerceHubListings" USING btree ("ProductId");


--
-- TOC entry 5902 (class 1259 OID 36833)
-- Name: IX_CommerceHubListings_TenantId_IsPublished_CategoryId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubListings_TenantId_IsPublished_CategoryId" ON public."CommerceHubListings" USING btree ("TenantId", "IsPublished", "CommerceHubCategoryId");


--
-- TOC entry 5903 (class 1259 OID 38095)
-- Name: IX_CommerceHubListings_TenantId_RestorePublicationOnActivate; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubListings_TenantId_RestorePublicationOnActivate" ON public."CommerceHubListings" USING btree ("TenantId", "RestorePublicationOnActivate") WHERE ("RestorePublicationOnActivate" = true);


--
-- TOC entry 5904 (class 1259 OID 36832)
-- Name: IX_CommerceHubListings_VariantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubListings_VariantId" ON public."CommerceHubListings" USING btree ("VariantId");


--
-- TOC entry 5856 (class 1259 OID 38241)
-- Name: IX_CommerceHubMarketplaceOrders_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubMarketplaceOrders_CreatedAt" ON public."CommerceHubMarketplaceOrders" USING btree ("CreatedAt" DESC);


--
-- TOC entry 5857 (class 1259 OID 37475)
-- Name: IX_CommerceHubMarketplaceOrders_CustomerId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubMarketplaceOrders_CustomerId" ON public."CommerceHubMarketplaceOrders" USING btree ("CustomerId");


--
-- TOC entry 5858 (class 1259 OID 36748)
-- Name: IX_CommerceHubMarketplaceOrders_IdempotencyKey; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CommerceHubMarketplaceOrders_IdempotencyKey" ON public."CommerceHubMarketplaceOrders" USING btree ("IdempotencyKey") WHERE ("IdempotencyKey" IS NOT NULL);


--
-- TOC entry 5859 (class 1259 OID 37232)
-- Name: IX_CommerceHubMarketplaceOrders_IsProjected_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubMarketplaceOrders_IsProjected_CreatedAt" ON public."CommerceHubMarketplaceOrders" USING btree ("IsProjected", "CreatedAt") WHERE ("IsProjected" = false);


--
-- TOC entry 5860 (class 1259 OID 37233)
-- Name: IX_CommerceHubMarketplaceOrders_OnlineCustomerId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubMarketplaceOrders_OnlineCustomerId" ON public."CommerceHubMarketplaceOrders" USING btree ("OnlineCustomerId") WHERE ("OnlineCustomerId" IS NOT NULL);


--
-- TOC entry 5861 (class 1259 OID 36695)
-- Name: IX_CommerceHubMarketplaceOrders_OrderNumber; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CommerceHubMarketplaceOrders_OrderNumber" ON public."CommerceHubMarketplaceOrders" USING btree ("OrderNumber");


--
-- TOC entry 5862 (class 1259 OID 38242)
-- Name: IX_CommerceHubMarketplaceOrders_PaymentStatus_Status_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubMarketplaceOrders_PaymentStatus_Status_CreatedAt" ON public."CommerceHubMarketplaceOrders" USING btree ("PaymentStatus", "Status", "CreatedAt" DESC);


--
-- TOC entry 5863 (class 1259 OID 3022272)
-- Name: IX_CommerceHubMarketplaceOrders_ProjectionMutationVersion; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubMarketplaceOrders_ProjectionMutationVersion" ON public."CommerceHubMarketplaceOrders" USING btree ("Id", "ProjectionMutationVersion");


--
-- TOC entry 5864 (class 1259 OID 3022293)
-- Name: IX_CommerceHubMarketplaceOrders_ProjectionReconciliation; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubMarketplaceOrders_ProjectionReconciliation" ON public."CommerceHubMarketplaceOrders" USING btree ("IsProjectionTerminalFailure", "LastProjectionAttemptAt" NULLS FIRST, "CreatedAt") WHERE (("IsDeleted" = false) AND ("IsProjected" = false));


--
-- TOC entry 6860 (class 0 OID 0)
-- Dependencies: 5864
-- Name: INDEX "IX_CommerceHubMarketplaceOrders_ProjectionReconciliation"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON INDEX public."IX_CommerceHubMarketplaceOrders_ProjectionReconciliation" IS 'Supports projection reconciliation queries. Orders unprojected orders by: non-terminal failures first, never-attempted first, then oldest attempt, then oldest creation. Prevents old stuck failures from blocking new orders.';


--
-- TOC entry 5865 (class 1259 OID 38145)
-- Name: IX_CommerceHubMarketplaceOrders_PromoCode; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubMarketplaceOrders_PromoCode" ON public."CommerceHubMarketplaceOrders" USING btree ("PromoCode") WHERE ("PromoCode" IS NOT NULL);


--
-- TOC entry 5964 (class 1259 OID 37222)
-- Name: IX_CommerceHubPaymentAttempts_PaymentId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubPaymentAttempts_PaymentId" ON public."CommerceHubPaymentAttempts" USING btree ("PaymentId");


--
-- TOC entry 5965 (class 1259 OID 37223)
-- Name: IX_CommerceHubPaymentAttempts_PaymentId_AttemptNumber; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CommerceHubPaymentAttempts_PaymentId_AttemptNumber" ON public."CommerceHubPaymentAttempts" USING btree ("PaymentId", "AttemptNumber");


--
-- TOC entry 5966 (class 1259 OID 37225)
-- Name: IX_CommerceHubPaymentAttempts_ProviderTransactionId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubPaymentAttempts_ProviderTransactionId" ON public."CommerceHubPaymentAttempts" USING btree ("ProviderTransactionId") WHERE ("ProviderTransactionId" IS NOT NULL);


--
-- TOC entry 5967 (class 1259 OID 37224)
-- Name: IX_CommerceHubPaymentAttempts_Provider_ProviderIntentId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubPaymentAttempts_Provider_ProviderIntentId" ON public."CommerceHubPaymentAttempts" USING btree ("Provider", "ProviderIntentId") WHERE ("ProviderIntentId" IS NOT NULL);


--
-- TOC entry 5945 (class 1259 OID 37192)
-- Name: IX_CommerceHubPaymentItems_PaymentId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubPaymentItems_PaymentId" ON public."CommerceHubPaymentItems" USING btree ("PaymentId");


--
-- TOC entry 5946 (class 1259 OID 37193)
-- Name: IX_CommerceHubPaymentItems_PaymentId_SubOrderId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CommerceHubPaymentItems_PaymentId_SubOrderId" ON public."CommerceHubPaymentItems" USING btree ("PaymentId", "SubOrderId");


--
-- TOC entry 5947 (class 1259 OID 37194)
-- Name: IX_CommerceHubPaymentItems_SubOrderId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubPaymentItems_SubOrderId" ON public."CommerceHubPaymentItems" USING btree ("SubOrderId");


--
-- TOC entry 5950 (class 1259 OID 37195)
-- Name: IX_CommerceHubPaymentLogs_PaymentId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubPaymentLogs_PaymentId" ON public."CommerceHubPaymentLogs" USING btree ("PaymentId");


--
-- TOC entry 5951 (class 1259 OID 37196)
-- Name: IX_CommerceHubPaymentLogs_PaymentId_ProviderEventId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubPaymentLogs_PaymentId_ProviderEventId" ON public."CommerceHubPaymentLogs" USING btree ("PaymentId", "ProviderEventId");


--
-- TOC entry 5937 (class 1259 OID 37187)
-- Name: IX_CommerceHubPayments_OrderId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubPayments_OrderId" ON public."CommerceHubPayments" USING btree ("OrderId");


--
-- TOC entry 5938 (class 1259 OID 37221)
-- Name: IX_CommerceHubPayments_OrderId_SinglePaid; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CommerceHubPayments_OrderId_SinglePaid" ON public."CommerceHubPayments" USING btree ("OrderId") WHERE (("Status" = 1) AND ("IsDeleted" = false));


--
-- TOC entry 5939 (class 1259 OID 37188)
-- Name: IX_CommerceHubPayments_OrderId_Status; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubPayments_OrderId_Status" ON public."CommerceHubPayments" USING btree ("OrderId", "Status");


--
-- TOC entry 5940 (class 1259 OID 37189)
-- Name: IX_CommerceHubPayments_ProviderReference; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CommerceHubPayments_ProviderReference" ON public."CommerceHubPayments" USING btree ("ProviderReference");


--
-- TOC entry 5941 (class 1259 OID 37190)
-- Name: IX_CommerceHubPayments_ProviderTransactionId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CommerceHubPayments_ProviderTransactionId" ON public."CommerceHubPayments" USING btree ("ProviderTransactionId") WHERE ("ProviderTransactionId" IS NOT NULL);


--
-- TOC entry 5995 (class 1259 OID 37357)
-- Name: IX_CommerceHubPayoutItems_EarningId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubPayoutItems_EarningId" ON public."CommerceHubPayoutItems" USING btree ("EarningId");


--
-- TOC entry 5996 (class 1259 OID 37356)
-- Name: IX_CommerceHubPayoutItems_PayoutId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubPayoutItems_PayoutId" ON public."CommerceHubPayoutItems" USING btree ("PayoutId");


--
-- TOC entry 5997 (class 1259 OID 37358)
-- Name: IX_CommerceHubPayoutItems_PayoutId_EarningId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CommerceHubPayoutItems_PayoutId_EarningId" ON public."CommerceHubPayoutItems" USING btree ("PayoutId", "EarningId");


--
-- TOC entry 5981 (class 1259 OID 37374)
-- Name: IX_CommerceHubPayouts_BankTransactionId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubPayouts_BankTransactionId" ON public."CommerceHubPayouts" USING btree ("BankTransactionId") WHERE ("BankTransactionId" IS NOT NULL);


--
-- TOC entry 5982 (class 1259 OID 37373)
-- Name: IX_CommerceHubPayouts_ExternalReferenceId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubPayouts_ExternalReferenceId" ON public."CommerceHubPayouts" USING btree ("ExternalReferenceId") WHERE ("ExternalReferenceId" IS NOT NULL);


--
-- TOC entry 5983 (class 1259 OID 37891)
-- Name: IX_CommerceHubPayouts_PayoutMethodId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubPayouts_PayoutMethodId" ON public."CommerceHubPayouts" USING btree ("PayoutMethodId");


--
-- TOC entry 5984 (class 1259 OID 37355)
-- Name: IX_CommerceHubPayouts_TenantId_IdempotencyKey; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CommerceHubPayouts_TenantId_IdempotencyKey" ON public."CommerceHubPayouts" USING btree ("TenantId", "IdempotencyKey");


--
-- TOC entry 5985 (class 1259 OID 37354)
-- Name: IX_CommerceHubPayouts_TenantId_Status_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubPayouts_TenantId_Status_CreatedAt" ON public."CommerceHubPayouts" USING btree ("TenantId", "Status", "CreatedAt");


--
-- TOC entry 6034 (class 1259 OID 37581)
-- Name: IX_CommerceHubRefundEvidenceMedia_CustomerId_ContentHash; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubRefundEvidenceMedia_CustomerId_ContentHash" ON public."CommerceHubRefundEvidenceMedia" USING btree ("CustomerId", "ContentHash");


--
-- TOC entry 6035 (class 1259 OID 37572)
-- Name: IX_CommerceHubRefundEvidenceMedia_CustomerId_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubRefundEvidenceMedia_CustomerId_CreatedAt" ON public."CommerceHubRefundEvidenceMedia" USING btree ("CustomerId", "CreatedAt" DESC);


--
-- TOC entry 6036 (class 1259 OID 37582)
-- Name: IX_CommerceHubRefundEvidenceMedia_IsTemporary_ExpiresAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubRefundEvidenceMedia_IsTemporary_ExpiresAtUtc" ON public."CommerceHubRefundEvidenceMedia" USING btree ("IsTemporary", "ExpiresAtUtc");


--
-- TOC entry 6037 (class 1259 OID 37574)
-- Name: IX_CommerceHubRefundEvidenceMedia_ObjectKey; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CommerceHubRefundEvidenceMedia_ObjectKey" ON public."CommerceHubRefundEvidenceMedia" USING btree ("ObjectKey");


--
-- TOC entry 6038 (class 1259 OID 37573)
-- Name: IX_CommerceHubRefundEvidenceMedia_RefundRequestId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubRefundEvidenceMedia_RefundRequestId" ON public."CommerceHubRefundEvidenceMedia" USING btree ("RefundRequestId");


--
-- TOC entry 6045 (class 1259 OID 37633)
-- Name: IX_CommerceHubRefundExecutions_ExecutedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubRefundExecutions_ExecutedAt" ON public."CommerceHubRefundExecutions" USING btree ("ExecutedAt" DESC);


--
-- TOC entry 6046 (class 1259 OID 37632)
-- Name: IX_CommerceHubRefundExecutions_RefundId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CommerceHubRefundExecutions_RefundId" ON public."CommerceHubRefundExecutions" USING btree ("RefundId") WHERE ("IsDeleted" = false);


--
-- TOC entry 6024 (class 1259 OID 37514)
-- Name: IX_CommerceHubRefundRequests_CustomerId_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubRefundRequests_CustomerId_CreatedAt" ON public."CommerceHubRefundRequests" USING btree ("CustomerId", "CreatedAt" DESC);


--
-- TOC entry 6025 (class 1259 OID 37515)
-- Name: IX_CommerceHubRefundRequests_CustomerId_Status_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubRefundRequests_CustomerId_Status_CreatedAt" ON public."CommerceHubRefundRequests" USING btree ("CustomerId", "Status", "CreatedAt" DESC);


--
-- TOC entry 6026 (class 1259 OID 37521)
-- Name: IX_CommerceHubRefundRequests_IdempotencyKey; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CommerceHubRefundRequests_IdempotencyKey" ON public."CommerceHubRefundRequests" USING btree ("IdempotencyKey") WHERE ("IdempotencyKey" IS NOT NULL);


--
-- TOC entry 6027 (class 1259 OID 37518)
-- Name: IX_CommerceHubRefundRequests_OrderId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubRefundRequests_OrderId" ON public."CommerceHubRefundRequests" USING btree ("OrderId");


--
-- TOC entry 6028 (class 1259 OID 37516)
-- Name: IX_CommerceHubRefundRequests_OrderItemId_Status_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubRefundRequests_OrderItemId_Status_CreatedAt" ON public."CommerceHubRefundRequests" USING btree ("OrderItemId", "Status", "CreatedAt" DESC);


--
-- TOC entry 6029 (class 1259 OID 37671)
-- Name: IX_CommerceHubRefundRequests_ReturnRequestId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CommerceHubRefundRequests_ReturnRequestId" ON public."CommerceHubRefundRequests" USING btree ("ReturnRequestId") WHERE ("ReturnRequestId" IS NOT NULL);


--
-- TOC entry 6030 (class 1259 OID 37525)
-- Name: IX_CommerceHubRefundRequests_Status_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubRefundRequests_Status_CreatedAt" ON public."CommerceHubRefundRequests" USING btree ("Status", "CreatedAt" DESC);


--
-- TOC entry 6031 (class 1259 OID 37517)
-- Name: IX_CommerceHubRefundRequests_VendorId_Status_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubRefundRequests_VendorId_Status_CreatedAt" ON public."CommerceHubRefundRequests" USING btree ("VendorId", "Status", "CreatedAt" DESC);


--
-- TOC entry 6000 (class 1259 OID 3022205)
-- Name: IX_CommerceHubRefunds_ExternalReconciliation; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubRefunds_ExternalReconciliation" ON public."CommerceHubRefunds" USING btree ("TenantId", "Status", "ProcessingStartedAtUtc", "CreatedAt") WHERE (("IsDeleted" = false) AND ("Status" = 1));


--
-- TOC entry 6001 (class 1259 OID 37539)
-- Name: IX_CommerceHubRefunds_OrderItemId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubRefunds_OrderItemId" ON public."CommerceHubRefunds" USING btree ("OrderItemId") WHERE ("OrderItemId" IS NOT NULL);


--
-- TOC entry 6002 (class 1259 OID 37362)
-- Name: IX_CommerceHubRefunds_PaymentId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubRefunds_PaymentId" ON public."CommerceHubRefunds" USING btree ("PaymentId");


--
-- TOC entry 6003 (class 1259 OID 37538)
-- Name: IX_CommerceHubRefunds_RefundRequestId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CommerceHubRefunds_RefundRequestId" ON public."CommerceHubRefunds" USING btree ("RefundRequestId") WHERE ("RefundRequestId" IS NOT NULL);


--
-- TOC entry 6004 (class 1259 OID 37361)
-- Name: IX_CommerceHubRefunds_SubOrderId_Status_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubRefunds_SubOrderId_Status_CreatedAt" ON public."CommerceHubRefunds" USING btree ("SubOrderId", "Status", "CreatedAt");


--
-- TOC entry 6005 (class 1259 OID 37359)
-- Name: IX_CommerceHubRefunds_TenantId_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubRefunds_TenantId_CreatedAt" ON public."CommerceHubRefunds" USING btree ("TenantId", "CreatedAt");


--
-- TOC entry 6006 (class 1259 OID 37363)
-- Name: IX_CommerceHubRefunds_TenantId_IdempotencyKey; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CommerceHubRefunds_TenantId_IdempotencyKey" ON public."CommerceHubRefunds" USING btree ("TenantId", "IdempotencyKey");


--
-- TOC entry 6007 (class 1259 OID 37360)
-- Name: IX_CommerceHubRefunds_TenantId_Status_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubRefunds_TenantId_Status_CreatedAt" ON public."CommerceHubRefunds" USING btree ("TenantId", "Status", "CreatedAt");


--
-- TOC entry 6049 (class 1259 OID 37668)
-- Name: IX_CommerceHubReturnRequests_CustomerId_Status_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubReturnRequests_CustomerId_Status_CreatedAt" ON public."CommerceHubReturnRequests" USING btree ("CustomerId", "Status", "CreatedAt" DESC);


--
-- TOC entry 6050 (class 1259 OID 37725)
-- Name: IX_CommerceHubReturnRequests_EscalationLevel_Status; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubReturnRequests_EscalationLevel_Status" ON public."CommerceHubReturnRequests" USING btree ("EscalationLevel", "Status") WHERE ("IsDeleted" = false);


--
-- TOC entry 6051 (class 1259 OID 37726)
-- Name: IX_CommerceHubReturnRequests_ExpectedCompletionAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubReturnRequests_ExpectedCompletionAtUtc" ON public."CommerceHubReturnRequests" USING btree ("ExpectedCompletionAtUtc") WHERE (("IsDeleted" = false) AND ("ExpectedCompletionAtUtc" IS NOT NULL));


--
-- TOC entry 6052 (class 1259 OID 37670)
-- Name: IX_CommerceHubReturnRequests_OrderItemId_Status_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubReturnRequests_OrderItemId_Status_CreatedAt" ON public."CommerceHubReturnRequests" USING btree ("OrderItemId", "Status", "CreatedAt" DESC);


--
-- TOC entry 6053 (class 1259 OID 37667)
-- Name: IX_CommerceHubReturnRequests_RefundId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CommerceHubReturnRequests_RefundId" ON public."CommerceHubReturnRequests" USING btree ("RefundId") WHERE ("IsDeleted" = false);


--
-- TOC entry 6054 (class 1259 OID 37669)
-- Name: IX_CommerceHubReturnRequests_VendorId_Status_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubReturnRequests_VendorId_Status_CreatedAt" ON public."CommerceHubReturnRequests" USING btree ("VendorId", "Status", "CreatedAt" DESC);


--
-- TOC entry 5876 (class 1259 OID 36699)
-- Name: IX_CommerceHubSubOrderItems_ListingId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubSubOrderItems_ListingId" ON public."CommerceHubSubOrderItems" USING btree ("ListingId");


--
-- TOC entry 5877 (class 1259 OID 36698)
-- Name: IX_CommerceHubSubOrderItems_SubOrderId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubSubOrderItems_SubOrderId" ON public."CommerceHubSubOrderItems" USING btree ("SubOrderId");


--
-- TOC entry 5878 (class 1259 OID 37085)
-- Name: IX_CommerceHubSubOrderItems_SubOrderId_VariantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CommerceHubSubOrderItems_SubOrderId_VariantId" ON public."CommerceHubSubOrderItems" USING btree ("SubOrderId", "VariantId") WHERE ("IsDeleted" = false);


--
-- TOC entry 5879 (class 1259 OID 36700)
-- Name: IX_CommerceHubSubOrderItems_VariantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubSubOrderItems_VariantId" ON public."CommerceHubSubOrderItems" USING btree ("VariantId");


--
-- TOC entry 5933 (class 1259 OID 37086)
-- Name: IX_CommerceHubSubOrderStatusHistory_SubOrderId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubSubOrderStatusHistory_SubOrderId" ON public."CommerceHubSubOrderStatusHistory" USING btree ("SubOrderId");


--
-- TOC entry 5934 (class 1259 OID 37087)
-- Name: IX_CommerceHubSubOrderStatusHistory_SubOrderId_ChangedAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubSubOrderStatusHistory_SubOrderId_ChangedAtUtc" ON public."CommerceHubSubOrderStatusHistory" USING btree ("SubOrderId", "ChangedAtUtc" DESC);


--
-- TOC entry 5868 (class 1259 OID 38243)
-- Name: IX_CommerceHubSubOrders_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubSubOrders_CreatedAt" ON public."CommerceHubSubOrders" USING btree ("CreatedAt" DESC);


--
-- TOC entry 5869 (class 1259 OID 3022273)
-- Name: IX_CommerceHubSubOrders_MarketplaceOrderId_ProjectionMutationVe; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubSubOrders_MarketplaceOrderId_ProjectionMutationVe" ON public."CommerceHubSubOrders" USING btree ("MarketplaceOrderId", "ProjectionMutationVersion");


--
-- TOC entry 5870 (class 1259 OID 36697)
-- Name: IX_CommerceHubSubOrders_MarketplaceOrderId_StoreId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubSubOrders_MarketplaceOrderId_StoreId" ON public."CommerceHubSubOrders" USING btree ("MarketplaceOrderId", "StoreId");


--
-- TOC entry 5871 (class 1259 OID 36696)
-- Name: IX_CommerceHubSubOrders_OrderNumber; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CommerceHubSubOrders_OrderNumber" ON public."CommerceHubSubOrders" USING btree ("OrderNumber");


--
-- TOC entry 5872 (class 1259 OID 37021)
-- Name: IX_CommerceHubSubOrders_ShippingMethodId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubSubOrders_ShippingMethodId" ON public."CommerceHubSubOrders" USING btree ("ShippingMethodId");


--
-- TOC entry 5873 (class 1259 OID 37061)
-- Name: IX_CommerceHubSubOrders_StoreId_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubSubOrders_StoreId_CreatedAt" ON public."CommerceHubSubOrders" USING btree ("StoreId", "CreatedAt" DESC);


--
-- TOC entry 6329 (class 1259 OID 3022209)
-- Name: IX_CommerceHubUnresolvedPaymentWebhooks_AffectedStoreScope; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubUnresolvedPaymentWebhooks_AffectedStoreScope" ON public."CommerceHubUnresolvedPaymentWebhooks" USING gin ((("AffectedStoreIdsJson")::jsonb));


--
-- TOC entry 6330 (class 1259 OID 3022212)
-- Name: IX_CommerceHubUnresolvedPaymentWebhooks_LocalPaymentId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubUnresolvedPaymentWebhooks_LocalPaymentId" ON public."CommerceHubUnresolvedPaymentWebhooks" USING btree ("LocalPaymentId") WHERE ("LocalPaymentId" IS NOT NULL);


--
-- TOC entry 6331 (class 1259 OID 3022169)
-- Name: IX_CommerceHubUnresolvedPaymentWebhooks_OrderId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubUnresolvedPaymentWebhooks_OrderId" ON public."CommerceHubUnresolvedPaymentWebhooks" USING btree ("OrderId") WHERE ("OrderId" IS NOT NULL);


--
-- TOC entry 6332 (class 1259 OID 3022168)
-- Name: IX_CommerceHubUnresolvedPaymentWebhooks_Tenant_Status; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubUnresolvedPaymentWebhooks_Tenant_Status" ON public."CommerceHubUnresolvedPaymentWebhooks" USING btree ("TenantId", "Status", "CreatedAt");


--
-- TOC entry 5955 (class 1259 OID 37353)
-- Name: IX_CommerceHubVendorEarnings_OriginalEarningId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubVendorEarnings_OriginalEarningId" ON public."CommerceHubVendorEarnings" USING btree ("OriginalEarningId");


--
-- TOC entry 5956 (class 1259 OID 37395)
-- Name: IX_CommerceHubVendorEarnings_PaymentId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubVendorEarnings_PaymentId" ON public."CommerceHubVendorEarnings" USING btree ("PaymentId");


--
-- TOC entry 5957 (class 1259 OID 3022218)
-- Name: IX_CommerceHubVendorEarnings_PayoutCreationLock; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubVendorEarnings_PayoutCreationLock" ON public."CommerceHubVendorEarnings" USING btree ("TenantId", "Id") WHERE ("IsDeleted" = false);


--
-- TOC entry 5958 (class 1259 OID 37351)
-- Name: IX_CommerceHubVendorEarnings_PayoutId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubVendorEarnings_PayoutId" ON public."CommerceHubVendorEarnings" USING btree ("PayoutId");


--
-- TOC entry 5959 (class 1259 OID 37352)
-- Name: IX_CommerceHubVendorEarnings_RefundId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubVendorEarnings_RefundId" ON public."CommerceHubVendorEarnings" USING btree ("RefundId");


--
-- TOC entry 5960 (class 1259 OID 37394)
-- Name: IX_CommerceHubVendorEarnings_SubOrderId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CommerceHubVendorEarnings_SubOrderId" ON public."CommerceHubVendorEarnings" USING btree ("SubOrderId") WHERE (("OriginalEarningId" IS NULL) AND ("IsDeleted" = false));


--
-- TOC entry 5961 (class 1259 OID 37198)
-- Name: IX_CommerceHubVendorEarnings_TenantId_Status_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubVendorEarnings_TenantId_Status_CreatedAt" ON public."CommerceHubVendorEarnings" USING btree ("TenantId", "Status", "CreatedAt");


--
-- TOC entry 6097 (class 1259 OID 37861)
-- Name: IX_CommerceHubVendorPaymentAuditLogs_AdminId_TimestampUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubVendorPaymentAuditLogs_AdminId_TimestampUtc" ON public."CommerceHubVendorPaymentAuditLogs" USING btree ("AdminId", "TimestampUtc" DESC);


--
-- TOC entry 6098 (class 1259 OID 37860)
-- Name: IX_CommerceHubVendorPaymentAuditLogs_PaymentRequestId_Timestamp; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubVendorPaymentAuditLogs_PaymentRequestId_Timestamp" ON public."CommerceHubVendorPaymentAuditLogs" USING btree ("PaymentRequestId", "TimestampUtc" DESC);


--
-- TOC entry 6081 (class 1259 OID 37780)
-- Name: IX_CommerceHubVendorPaymentMethods_IsActive; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubVendorPaymentMethods_IsActive" ON public."CommerceHubVendorPaymentMethods" USING btree ("IsActive");


--
-- TOC entry 6085 (class 1259 OID 37813)
-- Name: IX_CommerceHubVendorPaymentRequests_Status_CreatedAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubVendorPaymentRequests_Status_CreatedAtUtc" ON public."CommerceHubVendorPaymentRequests" USING btree ("Status", "CreatedAtUtc" DESC);


--
-- TOC entry 6086 (class 1259 OID 37812)
-- Name: IX_CommerceHubVendorPaymentRequests_VendorId_CreatedAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubVendorPaymentRequests_VendorId_CreatedAtUtc" ON public."CommerceHubVendorPaymentRequests" USING btree ("VendorId", "CreatedAtUtc" DESC);


--
-- TOC entry 6087 (class 1259 OID 37814)
-- Name: IX_CommerceHubVendorPaymentRequests_VendorId_Status_CreatedAtUt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubVendorPaymentRequests_VendorId_Status_CreatedAtUt" ON public."CommerceHubVendorPaymentRequests" USING btree ("VendorId", "Status", "CreatedAtUtc" DESC);


--
-- TOC entry 6101 (class 1259 OID 37888)
-- Name: IX_CommerceHubVendorPayoutMethods_VendorId_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubVendorPayoutMethods_VendorId_CreatedAt" ON public."CommerceHubVendorPayoutMethods" USING btree ("VendorId", "CreatedAt" DESC);


--
-- TOC entry 6074 (class 1259 OID 37752)
-- Name: IX_CommerceHubVendorSettlements_CreatedAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubVendorSettlements_CreatedAtUtc" ON public."CommerceHubVendorSettlements" USING btree ("CreatedAtUtc" DESC);


--
-- TOC entry 6075 (class 1259 OID 37751)
-- Name: IX_CommerceHubVendorSettlements_SettlementType_CreatedAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubVendorSettlements_SettlementType_CreatedAtUtc" ON public."CommerceHubVendorSettlements" USING btree ("SettlementType", "CreatedAtUtc" DESC);


--
-- TOC entry 6076 (class 1259 OID 37750)
-- Name: IX_CommerceHubVendorSettlements_VendorId_CreatedAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommerceHubVendorSettlements_VendorId_CreatedAtUtc" ON public."CommerceHubVendorSettlements" USING btree ("VendorId", "CreatedAtUtc" DESC);


--
-- TOC entry 6015 (class 1259 OID 37448)
-- Name: IX_CommissionRules_ActiveGlobal; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CommissionRules_ActiveGlobal" ON public."CommissionRules" USING btree ("ScopeType") WHERE (("ScopeType" = 0) AND ("IsActive" = true) AND ("IsDeleted" = false));


--
-- TOC entry 6016 (class 1259 OID 37449)
-- Name: IX_CommissionRules_ActiveScopePriority; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CommissionRules_ActiveScopePriority" ON public."CommissionRules" USING btree ("ScopeType", "ScopeId", "Priority") WHERE (("ScopeType" <> 0) AND ("IsActive" = true) AND ("IsDeleted" = false));


--
-- TOC entry 6017 (class 1259 OID 37450)
-- Name: IX_CommissionRules_ScopeLookup; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CommissionRules_ScopeLookup" ON public."CommissionRules" USING btree ("ScopeType", "ScopeId", "IsActive", "Priority" DESC, "CreatedAt" DESC) WHERE ("IsDeleted" = false);


--
-- TOC entry 6317 (class 1259 OID 2287805)
-- Name: IX_CouponReservations_CouponId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CouponReservations_CouponId" ON public."CouponReservations" USING btree ("CouponId");


--
-- TOC entry 6318 (class 1259 OID 2287806)
-- Name: IX_CouponReservations_CustomerId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CouponReservations_CustomerId" ON public."CouponReservations" USING btree ("CustomerId");


--
-- TOC entry 6319 (class 1259 OID 2287810)
-- Name: IX_CouponReservations_TenantId_CouponId_CustomerId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CouponReservations_TenantId_CouponId_CustomerId" ON public."CouponReservations" USING btree ("TenantId", "CouponId", "CustomerId") WHERE (("IsDeleted" = false) AND ("Status" = 0));


--
-- TOC entry 6320 (class 1259 OID 2287808)
-- Name: IX_CouponReservations_TenantId_OrderId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CouponReservations_TenantId_OrderId" ON public."CouponReservations" USING btree ("TenantId", "OrderId") WHERE ("IsDeleted" = false);


--
-- TOC entry 5547 (class 1259 OID 26128)
-- Name: IX_CouponUsages_CouponId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CouponUsages_CouponId" ON public."CouponUsages" USING btree ("CouponId");


--
-- TOC entry 5548 (class 1259 OID 26129)
-- Name: IX_CouponUsages_CustomerId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CouponUsages_CustomerId" ON public."CouponUsages" USING btree ("CustomerId");


--
-- TOC entry 5549 (class 1259 OID 26130)
-- Name: IX_CouponUsages_OrderId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CouponUsages_OrderId" ON public."CouponUsages" USING btree ("OrderId");


--
-- TOC entry 5550 (class 1259 OID 26131)
-- Name: IX_CouponUsages_TenantId_CouponId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CouponUsages_TenantId_CouponId" ON public."CouponUsages" USING btree ("TenantId", "CouponId");


--
-- TOC entry 5551 (class 1259 OID 2287768)
-- Name: IX_CouponUsages_TenantId_CouponId_CustomerId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CouponUsages_TenantId_CouponId_CustomerId" ON public."CouponUsages" USING btree ("TenantId", "CouponId", "CustomerId") WHERE (("CustomerId" IS NOT NULL) AND ("IsDeleted" = false));


--
-- TOC entry 5552 (class 1259 OID 26133)
-- Name: IX_CouponUsages_TenantId_OrderId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_CouponUsages_TenantId_OrderId" ON public."CouponUsages" USING btree ("TenantId", "OrderId") WHERE ("IsDeleted" = false);


--
-- TOC entry 5384 (class 1259 OID 23514)
-- Name: IX_Coupons_IsActive; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Coupons_IsActive" ON public."Coupons" USING btree ("IsActive");


--
-- TOC entry 5385 (class 1259 OID 2277995)
-- Name: IX_Coupons_NormalizedCode; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Coupons_NormalizedCode" ON public."Coupons" USING btree ("NormalizedCode") WHERE ("IsDeleted" = false);


--
-- TOC entry 5386 (class 1259 OID 25900)
-- Name: IX_Coupons_TenantId_Code; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_Coupons_TenantId_Code" ON public."Coupons" USING btree ("TenantId", "Code") WHERE ("IsDeleted" = false);


--
-- TOC entry 5387 (class 1259 OID 2277994)
-- Name: IX_Coupons_TenantId_NormalizedCode; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_Coupons_TenantId_NormalizedCode" ON public."Coupons" USING btree ("TenantId", "NormalizedCode") WHERE ("IsDeleted" = false);


--
-- TOC entry 6325 (class 1259 OID 2287824)
-- Name: IX_Currencies_Name; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Currencies_Name" ON public."Currencies" USING btree ("Name");


--
-- TOC entry 5136 (class 1259 OID 20476)
-- Name: IX_CustomerTransactions_CustomerId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_CustomerTransactions_CustomerId" ON public."CustomerTransactions" USING btree ("CustomerId");


--
-- TOC entry 5103 (class 1259 OID 20473)
-- Name: IX_Customers_Email; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Customers_Email" ON public."Customers" USING btree ("Email");


--
-- TOC entry 5104 (class 1259 OID 20474)
-- Name: IX_Customers_Phone; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Customers_Phone" ON public."Customers" USING btree ("Phone");


--
-- TOC entry 5105 (class 1259 OID 20475)
-- Name: IX_Customers_TaxNumber; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Customers_TaxNumber" ON public."Customers" USING btree ("TaxNumber");


--
-- TOC entry 5106 (class 1259 OID 55318)
-- Name: IX_Customers_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Customers_TenantId" ON public."Customers" USING btree ("TenantId");


--
-- TOC entry 5479 (class 1259 OID 23767)
-- Name: IX_EmailVerificationTokens_ExpiresAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_EmailVerificationTokens_ExpiresAtUtc" ON public."EmailVerificationTokens" USING btree ("ExpiresAtUtc");


--
-- TOC entry 5480 (class 1259 OID 23768)
-- Name: IX_EmailVerificationTokens_TokenHash; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_EmailVerificationTokens_TokenHash" ON public."EmailVerificationTokens" USING btree ("TokenHash");


--
-- TOC entry 5481 (class 1259 OID 23769)
-- Name: IX_EmailVerificationTokens_UserId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_EmailVerificationTokens_UserId" ON public."EmailVerificationTokens" USING btree ("UserId");


--
-- TOC entry 5498 (class 1259 OID 25758)
-- Name: IX_EmailVerification_ExpiresAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_EmailVerification_ExpiresAtUtc" ON public."OnlineCustomerEmailVerifications" USING btree ("ExpiresAtUtc");


--
-- TOC entry 5499 (class 1259 OID 25757)
-- Name: IX_EmailVerification_TenantId_CustomerId_IsUsed; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_EmailVerification_TenantId_CustomerId_IsUsed" ON public."OnlineCustomerEmailVerifications" USING btree ("TenantId", "OnlineCustomerId", "IsUsed");


--
-- TOC entry 5500 (class 1259 OID 25756)
-- Name: IX_EmailVerification_TenantId_TokenHash; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_EmailVerification_TenantId_TokenHash" ON public."OnlineCustomerEmailVerifications" USING btree ("TenantId", "TokenHash");


--
-- TOC entry 5326 (class 1259 OID 21441)
-- Name: IX_Expenses_BankId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Expenses_BankId" ON public."Expenses" USING btree ("BankId");


--
-- TOC entry 5327 (class 1259 OID 21442)
-- Name: IX_Expenses_CashboxId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Expenses_CashboxId" ON public."Expenses" USING btree ("CashboxId");


--
-- TOC entry 5328 (class 1259 OID 21443)
-- Name: IX_Expenses_ExpenseCategoryId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Expenses_ExpenseCategoryId" ON public."Expenses" USING btree ("ExpenseCategoryId");


--
-- TOC entry 5329 (class 1259 OID 21444)
-- Name: IX_Expenses_SupplierId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Expenses_SupplierId" ON public."Expenses" USING btree ("SupplierId");


--
-- TOC entry 5312 (class 1259 OID 21346)
-- Name: IX_FinancialAccounts_ParentAccountId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_FinancialAccounts_ParentAccountId" ON public."FinancialAccounts" USING btree ("ParentAccountId");


--
-- TOC entry 5315 (class 1259 OID 21347)
-- Name: IX_FinancialTransactions_AccountId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_FinancialTransactions_AccountId" ON public."FinancialTransactions" USING btree ("AccountId");


--
-- TOC entry 5841 (class 1259 OID 36455)
-- Name: IX_GlobalPhoneReputation_PhoneNumber; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_GlobalPhoneReputation_PhoneNumber" ON public."GlobalPhoneReputation" USING btree ("PhoneNumber");


--
-- TOC entry 6159 (class 1259 OID 38325)
-- Name: IX_HomepageSections_SectionGroupId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_HomepageSections_SectionGroupId" ON public."HomepageSections" USING btree ("SectionGroupId");


--
-- TOC entry 6160 (class 1259 OID 38327)
-- Name: IX_HomepageSections_TenantId_IsDraft_Order; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_HomepageSections_TenantId_IsDraft_Order" ON public."HomepageSections" USING btree ("TenantId", "IsDraft", "Order");


--
-- TOC entry 6161 (class 1259 OID 38326)
-- Name: IX_HomepageSections_TenantId_IsPublished_Order; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_HomepageSections_TenantId_IsPublished_Order" ON public."HomepageSections" USING btree ("TenantId", "IsPublished", "Order");


--
-- TOC entry 5434 (class 1259 OID 23579)
-- Name: IX_InboxState_Delivered; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_InboxState_Delivered" ON public."InboxState" USING btree ("Delivered");


--
-- TOC entry 6110 (class 1259 OID 37965)
-- Name: IX_InventoryEventInbox_TenantId_ReferenceLineId_EventType; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_InventoryEventInbox_TenantId_ReferenceLineId_EventType" ON public."InventoryEventInbox" USING btree ("TenantId", "ReferenceLineId", "EventType");


--
-- TOC entry 6106 (class 1259 OID 37966)
-- Name: IX_InventoryReservations_TenantId_OrderItemId_VariantId_Warehou; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_InventoryReservations_TenantId_OrderItemId_VariantId_Warehou" ON public."InventoryReservations" USING btree ("TenantId", "OrderItemId", "VariantId", "WarehouseId");


--
-- TOC entry 6107 (class 1259 OID 37967)
-- Name: IX_InventoryReservations_TenantId_Status_ExpiresAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_InventoryReservations_TenantId_Status_ExpiresAtUtc" ON public."InventoryReservations" USING btree ("TenantId", "Status", "ExpiresAtUtc");


--
-- TOC entry 5359 (class 1259 OID 23303)
-- Name: IX_JournalEntries_Date; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_JournalEntries_Date" ON public."JournalEntries" USING btree ("Date");


--
-- TOC entry 5360 (class 1259 OID 23304)
-- Name: IX_JournalEntries_IsReversed; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_JournalEntries_IsReversed" ON public."JournalEntries" USING btree ("IsReversed");


--
-- TOC entry 5361 (class 1259 OID 23305)
-- Name: IX_JournalEntries_JournalNumber; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_JournalEntries_JournalNumber" ON public."JournalEntries" USING btree ("JournalNumber") WHERE ("IsDeleted" = false);


--
-- TOC entry 5362 (class 1259 OID 23306)
-- Name: IX_JournalEntries_Posted; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_JournalEntries_Posted" ON public."JournalEntries" USING btree ("Posted");


--
-- TOC entry 5363 (class 1259 OID 23307)
-- Name: IX_JournalEntries_ReferenceId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_JournalEntries_ReferenceId" ON public."JournalEntries" USING btree ("ReferenceId");


--
-- TOC entry 5364 (class 1259 OID 23308)
-- Name: IX_JournalEntries_ReversedJournalId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_JournalEntries_ReversedJournalId" ON public."JournalEntries" USING btree ("ReversedJournalId");


--
-- TOC entry 5365 (class 1259 OID 23309)
-- Name: IX_JournalEntries_SourceModule; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_JournalEntries_SourceModule" ON public."JournalEntries" USING btree ("SourceModule");


--
-- TOC entry 5377 (class 1259 OID 23310)
-- Name: IX_JournalLines_AccountId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_JournalLines_AccountId" ON public."JournalLines" USING btree ("AccountId");


--
-- TOC entry 5378 (class 1259 OID 23311)
-- Name: IX_JournalLines_CustomerId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_JournalLines_CustomerId" ON public."JournalLines" USING btree ("CustomerId");


--
-- TOC entry 5379 (class 1259 OID 23312)
-- Name: IX_JournalLines_JournalId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_JournalLines_JournalId" ON public."JournalLines" USING btree ("JournalId");


--
-- TOC entry 5380 (class 1259 OID 23313)
-- Name: IX_JournalLines_ProductId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_JournalLines_ProductId" ON public."JournalLines" USING btree ("ProductId");


--
-- TOC entry 5381 (class 1259 OID 23314)
-- Name: IX_JournalLines_SupplierId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_JournalLines_SupplierId" ON public."JournalLines" USING btree ("SupplierId");


--
-- TOC entry 5643 (class 1259 OID 35117)
-- Name: IX_LedgerAcct_Clearing; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_LedgerAcct_Clearing" ON public."LedgerAccounts" USING btree ("TenantId", "IsClearing") WHERE ("IsClearing" = true);


--
-- TOC entry 5644 (class 1259 OID 35113)
-- Name: IX_LedgerAcct_Parent; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_LedgerAcct_Parent" ON public."LedgerAccounts" USING btree ("ParentAccountId");


--
-- TOC entry 5645 (class 1259 OID 35114)
-- Name: IX_LedgerAcct_Tenant; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_LedgerAcct_Tenant" ON public."LedgerAccounts" USING btree ("TenantId");


--
-- TOC entry 5646 (class 1259 OID 35115)
-- Name: IX_LedgerAcct_TenantCode; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_LedgerAcct_TenantCode" ON public."LedgerAccounts" USING btree ("TenantId", "AccountCode");


--
-- TOC entry 5647 (class 1259 OID 35116)
-- Name: IX_LedgerAcct_TenantType; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_LedgerAcct_TenantType" ON public."LedgerAccounts" USING btree ("TenantId", "AccountType");


--
-- TOC entry 5650 (class 1259 OID 35122)
-- Name: IX_LedgerJournal_Date; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_LedgerJournal_Date" ON public."LedgerJournalEntries" USING btree ("TenantId", "JournalDate");


--
-- TOC entry 5651 (class 1259 OID 35121)
-- Name: IX_LedgerJournal_Idempotency; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_LedgerJournal_Idempotency" ON public."LedgerJournalEntries" USING btree ("TenantId", "IdempotencyKey");


--
-- TOC entry 5652 (class 1259 OID 35123)
-- Name: IX_LedgerJournal_Number; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_LedgerJournal_Number" ON public."LedgerJournalEntries" USING btree ("TenantId", "JournalNumber");


--
-- TOC entry 5653 (class 1259 OID 35118)
-- Name: IX_LedgerJournal_Posted; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_LedgerJournal_Posted" ON public."LedgerJournalEntries" USING btree ("PostedAtUtc");


--
-- TOC entry 5654 (class 1259 OID 35119)
-- Name: IX_LedgerJournal_Reversed; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_LedgerJournal_Reversed" ON public."LedgerJournalEntries" USING btree ("ReversedByJournalId");


--
-- TOC entry 5655 (class 1259 OID 35124)
-- Name: IX_LedgerJournal_Source; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_LedgerJournal_Source" ON public."LedgerJournalEntries" USING btree ("TenantId", "SourceType", "SourceId");


--
-- TOC entry 5656 (class 1259 OID 35125)
-- Name: IX_LedgerJournal_Status; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_LedgerJournal_Status" ON public."LedgerJournalEntries" USING btree ("TenantId", "Status");


--
-- TOC entry 5657 (class 1259 OID 35120)
-- Name: IX_LedgerJournal_Tenant; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_LedgerJournal_Tenant" ON public."LedgerJournalEntries" USING btree ("TenantId");


--
-- TOC entry 5662 (class 1259 OID 35126)
-- Name: IX_LedgerLines_AcctDate; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_LedgerLines_AcctDate" ON public."LedgerLines" USING btree ("AccountId", "CreatedAtUtc");


--
-- TOC entry 5663 (class 1259 OID 35127)
-- Name: IX_LedgerLines_Journal; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_LedgerLines_Journal" ON public."LedgerLines" USING btree ("JournalEntryId");


--
-- TOC entry 5664 (class 1259 OID 35128)
-- Name: IX_LedgerLines_Tenant; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_LedgerLines_Tenant" ON public."LedgerLines" USING btree ("TenantId");


--
-- TOC entry 5665 (class 1259 OID 35129)
-- Name: IX_LedgerLines_TenantAcct; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_LedgerLines_TenantAcct" ON public."LedgerLines" USING btree ("TenantId", "AccountId");


--
-- TOC entry 6263 (class 1259 OID 55418)
-- Name: IX_LegacyCashMigrationLog_MigrationDate; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_LegacyCashMigrationLog_MigrationDate" ON public."_LegacyCashMigrationLog" USING btree ("MigrationDate");


--
-- TOC entry 6264 (class 1259 OID 55417)
-- Name: IX_LegacyCashMigrationLog_Status; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_LegacyCashMigrationLog_Status" ON public."_LegacyCashMigrationLog" USING btree ("Status");


--
-- TOC entry 6265 (class 1259 OID 55416)
-- Name: IX_LegacyCashMigrationLog_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_LegacyCashMigrationLog_TenantId" ON public."_LegacyCashMigrationLog" USING btree ("TenantId");


--
-- TOC entry 5747 (class 1259 OID 35866)
-- Name: IX_MarketingReviewEvidences_EvidenceType; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_MarketingReviewEvidences_EvidenceType" ON public."MarketingReviewEvidences" USING btree ("EvidenceType");


--
-- TOC entry 5748 (class 1259 OID 35865)
-- Name: IX_MarketingReviewEvidences_Platform; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_MarketingReviewEvidences_Platform" ON public."MarketingReviewEvidences" USING btree ("Platform");


--
-- TOC entry 5749 (class 1259 OID 35864)
-- Name: IX_MarketingReviewEvidences_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_MarketingReviewEvidences_TenantId" ON public."MarketingReviewEvidences" USING btree ("TenantId");


--
-- TOC entry 6237 (class 1259 OID 46823)
-- Name: IX_MarketplaceCategoryTranslations_Language; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_MarketplaceCategoryTranslations_Language" ON public."MarketplaceCategoryTranslations" USING btree ("Language");


--
-- TOC entry 6183 (class 1259 OID 46558)
-- Name: IX_MarketplaceCmsPages_IsDraft_Slug; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_MarketplaceCmsPages_IsDraft_Slug" ON public."MarketplaceCmsPages" USING btree ("IsDraft", "Slug");


--
-- TOC entry 6184 (class 1259 OID 46557)
-- Name: IX_MarketplaceCmsPages_IsPublished_Slug; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_MarketplaceCmsPages_IsPublished_Slug" ON public."MarketplaceCmsPages" USING btree ("IsPublished", "Slug");


--
-- TOC entry 6185 (class 1259 OID 46556)
-- Name: IX_MarketplaceCmsPages_PageGroupId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_MarketplaceCmsPages_PageGroupId" ON public."MarketplaceCmsPages" USING btree ("PageGroupId");


--
-- TOC entry 6353 (class 1259 OID 3022578)
-- Name: IX_MarketplaceEventOutboxDeadLetterArchives_ArchivedAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_MarketplaceEventOutboxDeadLetterArchives_ArchivedAtUtc" ON public."MarketplaceEventOutboxDeadLetterArchives" USING btree ("ArchivedAtUtc");


--
-- TOC entry 6354 (class 1259 OID 3022577)
-- Name: IX_MarketplaceEventOutboxDeadLetterArchives_Tenant_Aggregate; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_MarketplaceEventOutboxDeadLetterArchives_Tenant_Aggregate" ON public."MarketplaceEventOutboxDeadLetterArchives" USING btree ("TenantId", "AggregateRootId");


--
-- TOC entry 6214 (class 1259 OID 3022276)
-- Name: IX_MarketplaceEventOutbox_AggregateOrdering; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_MarketplaceEventOutbox_AggregateOrdering" ON public."MarketplaceEventOutboxMessages" USING btree ("TenantId", "AggregateRootId", "PublishedAtUtc", "OccurredAtUtc", "Version", "CreatedAt", "Id") WHERE (("PublishedAtUtc" IS NULL) AND ("IsFailed" = false));


--
-- TOC entry 6215 (class 1259 OID 3022277)
-- Name: IX_MarketplaceEventOutbox_AggregateOrderingBlockers; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_MarketplaceEventOutbox_AggregateOrderingBlockers" ON public."MarketplaceEventOutboxMessages" USING btree ("TenantId", "AggregateRootId", "OccurredAtUtc", "Version", "CreatedAt", "Id") WHERE ("PublishedAtUtc" IS NULL);


--
-- TOC entry 6216 (class 1259 OID 2287599)
-- Name: IX_MarketplaceEventOutbox_EntityOrdering; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_MarketplaceEventOutbox_EntityOrdering" ON public."MarketplaceEventOutboxMessages" USING btree ("TenantId", "EntityId", "PublishedAtUtc", "OccurredAtUtc", "Version", "CreatedAt", "Id") WHERE (("PublishedAtUtc" IS NULL) AND ("IsFailed" = false));


--
-- TOC entry 6217 (class 1259 OID 2287600)
-- Name: IX_MarketplaceEventOutbox_EntityOrderingBlockers; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_MarketplaceEventOutbox_EntityOrderingBlockers" ON public."MarketplaceEventOutboxMessages" USING btree ("TenantId", "EntityId", "OccurredAtUtc", "Version", "CreatedAt", "Id") WHERE ("PublishedAtUtc" IS NULL);


--
-- TOC entry 6218 (class 1259 OID 46741)
-- Name: IX_MarketplaceEventOutbox_EventId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_MarketplaceEventOutbox_EventId" ON public."MarketplaceEventOutboxMessages" USING btree ("EventId");


--
-- TOC entry 6219 (class 1259 OID 46743)
-- Name: IX_MarketplaceEventOutbox_OccurredAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_MarketplaceEventOutbox_OccurredAtUtc" ON public."MarketplaceEventOutboxMessages" USING btree ("OccurredAtUtc");


--
-- TOC entry 6220 (class 1259 OID 3022475)
-- Name: IX_MarketplaceEventOutbox_ProcessingStatus; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_MarketplaceEventOutbox_ProcessingStatus" ON public."MarketplaceEventOutboxMessages" USING btree ("PublishedAtUtc", "IsFailed", "NextRetryAtUtc", "ClaimedUntilUtc") WHERE (("PublishedAtUtc" IS NULL) AND ("IsFailed" = false));


--
-- TOC entry 6221 (class 1259 OID 3022275)
-- Name: IX_MarketplaceEventOutbox_TenantId_AggregateRootId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_MarketplaceEventOutbox_TenantId_AggregateRootId" ON public."MarketplaceEventOutboxMessages" USING btree ("TenantId", "AggregateRootId");


--
-- TOC entry 6222 (class 1259 OID 46744)
-- Name: IX_MarketplaceEventOutbox_TenantId_EntityId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_MarketplaceEventOutbox_TenantId_EntityId" ON public."MarketplaceEventOutboxMessages" USING btree ("TenantId", "EntityId");


--
-- TOC entry 6196 (class 1259 OID 46626)
-- Name: IX_MarketplaceFooterLinks_SectionId_SortOrder; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_MarketplaceFooterLinks_SectionId_SortOrder" ON public."MarketplaceFooterLinks" USING btree ("SectionId", "SortOrder");


--
-- TOC entry 6193 (class 1259 OID 46625)
-- Name: IX_MarketplaceFooterSections_SortOrder; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_MarketplaceFooterSections_SortOrder" ON public."MarketplaceFooterSections" USING btree ("SortOrder");


--
-- TOC entry 5697 (class 1259 OID 35462)
-- Name: IX_MarketplaceProfiles_IsActive; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_MarketplaceProfiles_IsActive" ON public."MarketplaceProfiles" USING btree ("IsActive") WHERE ("IsDeleted" = false);


--
-- TOC entry 5698 (class 1259 OID 35807)
-- Name: IX_MarketplaceProfiles_Location; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_MarketplaceProfiles_Location" ON public."MarketplaceProfiles" USING btree ("Location") WHERE (("IsDeleted" = false) AND ("IsActive" = true));


--
-- TOC entry 5699 (class 1259 OID 35460)
-- Name: IX_MarketplaceProfiles_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_MarketplaceProfiles_TenantId" ON public."MarketplaceProfiles" USING btree ("TenantId");


--
-- TOC entry 5700 (class 1259 OID 35461)
-- Name: IX_MarketplaceProfiles_UserId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_MarketplaceProfiles_UserId" ON public."MarketplaceProfiles" USING btree ("UserId");


--
-- TOC entry 6366 (class 1259 OID 3866342)
-- Name: IX_MarketplaceProjectionOutbox_Idempotency; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_MarketplaceProjectionOutbox_Idempotency" ON public."MarketplaceProjectionOutboxMessages" USING btree ("EventId", "ConsumerName", "EventType") WHERE ("IsDeleted" = false);


--
-- TOC entry 6367 (class 1259 OID 3866343)
-- Name: IX_MarketplaceProjectionOutbox_ProcessedAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_MarketplaceProjectionOutbox_ProcessedAtUtc" ON public."MarketplaceProjectionOutboxMessages" USING btree ("ProcessedAtUtc");


--
-- TOC entry 6368 (class 1259 OID 3866345)
-- Name: IX_MarketplaceProjectionOutbox_ProcessingStatus; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_MarketplaceProjectionOutbox_ProcessingStatus" ON public."MarketplaceProjectionOutboxMessages" USING btree ("TenantId", "ConsumerName", "ProcessedAtUtc", "NextRetryAtUtc", "ClaimedUntilUtc") WHERE (("ProcessedAtUtc" IS NULL) AND ("IsFailed" = false));


--
-- TOC entry 6369 (class 1259 OID 3866344)
-- Name: IX_MarketplaceProjectionOutbox_TenantId_EntityId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_MarketplaceProjectionOutbox_TenantId_EntityId" ON public."MarketplaceProjectionOutboxMessages" USING btree ("TenantId", "EntityId");


--
-- TOC entry 6199 (class 1259 OID 46627)
-- Name: IX_MarketplaceSocialLinks_SortOrder; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_MarketplaceSocialLinks_SortOrder" ON public."MarketplaceSocialLinks" USING btree ("SortOrder");


--
-- TOC entry 5476 (class 1259 OID 23747)
-- Name: IX_OnboardingChecklists_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_OnboardingChecklists_TenantId" ON public."OnboardingChecklists" USING btree ("TenantId");


--
-- TOC entry 5503 (class 1259 OID 2287585)
-- Name: IX_OnlineCustomerRefreshTokens_Revoked_Blacklisted; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OnlineCustomerRefreshTokens_Revoked_Blacklisted" ON public."OnlineCustomerRefreshTokens" USING btree ("TenantId", "IsRevoked", "IsAccessTokenBlacklisted") WHERE (("IsRevoked" = true) AND ("IsAccessTokenBlacklisted" = false));


--
-- TOC entry 5519 (class 1259 OID 25840)
-- Name: IX_OnlineCustomerWishlistItems_CreatedAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OnlineCustomerWishlistItems_CreatedAtUtc" ON public."OnlineCustomerWishlistItems" USING btree ("CreatedAtUtc");


--
-- TOC entry 5520 (class 1259 OID 25837)
-- Name: IX_OnlineCustomerWishlistItems_TenantId_CustomerId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OnlineCustomerWishlistItems_TenantId_CustomerId" ON public."OnlineCustomerWishlistItems" USING btree ("TenantId", "OnlineCustomerId");


--
-- TOC entry 5521 (class 1259 OID 25836)
-- Name: IX_OnlineCustomerWishlistItems_TenantId_CustomerId_ProductId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_OnlineCustomerWishlistItems_TenantId_CustomerId_ProductId" ON public."OnlineCustomerWishlistItems" USING btree ("TenantId", "OnlineCustomerId", "ProductId") WHERE ("IsRemoved" = false);


--
-- TOC entry 5522 (class 1259 OID 25839)
-- Name: IX_OnlineCustomerWishlistItems_TenantId_IsRemoved; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OnlineCustomerWishlistItems_TenantId_IsRemoved" ON public."OnlineCustomerWishlistItems" USING btree ("TenantId", "IsRemoved");


--
-- TOC entry 5523 (class 1259 OID 25838)
-- Name: IX_OnlineCustomerWishlistItems_TenantId_ProductId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OnlineCustomerWishlistItems_TenantId_ProductId" ON public."OnlineCustomerWishlistItems" USING btree ("TenantId", "ProductId");


--
-- TOC entry 5488 (class 1259 OID 25736)
-- Name: IX_OnlineCustomers_CreatedAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OnlineCustomers_CreatedAtUtc" ON public."OnlineCustomers" USING btree ("CreatedAtUtc");


--
-- TOC entry 5489 (class 1259 OID 25734)
-- Name: IX_OnlineCustomers_EmailVerified; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OnlineCustomers_EmailVerified" ON public."OnlineCustomers" USING btree ("EmailVerified");


--
-- TOC entry 5490 (class 1259 OID 2287546)
-- Name: IX_OnlineCustomers_Id_TenantId_TokensValidAfterUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OnlineCustomers_Id_TenantId_TokensValidAfterUtc" ON public."OnlineCustomers" USING btree ("Id", "TenantId") INCLUDE ("TokensValidAfterUtc", "IsActive");


--
-- TOC entry 5491 (class 1259 OID 25733)
-- Name: IX_OnlineCustomers_IsActive; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OnlineCustomers_IsActive" ON public."OnlineCustomers" USING btree ("IsActive");


--
-- TOC entry 5492 (class 1259 OID 25735)
-- Name: IX_OnlineCustomers_Provider; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OnlineCustomers_Provider" ON public."OnlineCustomers" USING btree ("Provider");


--
-- TOC entry 5493 (class 1259 OID 25732)
-- Name: IX_OnlineCustomers_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OnlineCustomers_TenantId" ON public."OnlineCustomers" USING btree ("TenantId");


--
-- TOC entry 5494 (class 1259 OID 25731)
-- Name: IX_OnlineCustomers_TenantId_Email; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_OnlineCustomers_TenantId_Email" ON public."OnlineCustomers" USING btree ("TenantId", "Email");


--
-- TOC entry 5495 (class 1259 OID 25737)
-- Name: IX_OnlineCustomers_TenantId_Provider_ProviderUserId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OnlineCustomers_TenantId_Provider_ProviderUserId" ON public."OnlineCustomers" USING btree ("TenantId", "Provider", "ProviderUserId");


--
-- TOC entry 5423 (class 1259 OID 3022030)
-- Name: IX_OrderItems_AffiliateCommissionSnapshot; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OrderItems_AffiliateCommissionSnapshot" ON public."OrderItems" USING btree ("OrderId", "AffiliateCommissionEligible", "AffiliateCommissionSnapshotCapturedAtUtc");


--
-- TOC entry 5424 (class 1259 OID 23515)
-- Name: IX_OrderItems_OrderId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OrderItems_OrderId" ON public."OrderItems" USING btree ("OrderId");


--
-- TOC entry 5425 (class 1259 OID 2287748)
-- Name: IX_OrderItems_OrderId_Id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_OrderItems_OrderId_Id" ON public."OrderItems" USING btree ("OrderId", "Id");


--
-- TOC entry 5426 (class 1259 OID 23516)
-- Name: IX_OrderItems_ProductId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OrderItems_ProductId" ON public."OrderItems" USING btree ("ProductId");


--
-- TOC entry 5427 (class 1259 OID 23517)
-- Name: IX_OrderItems_UnitId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OrderItems_UnitId" ON public."OrderItems" USING btree ("UnitId");


--
-- TOC entry 5428 (class 1259 OID 25987)
-- Name: IX_OrderItems_VariantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OrderItems_VariantId" ON public."OrderItems" USING btree ("VariantId");


--
-- TOC entry 5429 (class 1259 OID 23518)
-- Name: IX_OrderItems_WarehouseId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OrderItems_WarehouseId" ON public."OrderItems" USING btree ("WarehouseId");


--
-- TOC entry 6304 (class 1259 OID 2287690)
-- Name: IX_OrderRefundLines_OrderItemId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OrderRefundLines_OrderItemId" ON public."OrderRefundLines" USING btree ("OrderItemId");


--
-- TOC entry 6305 (class 1259 OID 2287689)
-- Name: IX_OrderRefundLines_OrderRefundId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OrderRefundLines_OrderRefundId" ON public."OrderRefundLines" USING btree ("OrderRefundId");


--
-- TOC entry 6306 (class 1259 OID 2287691)
-- Name: IX_OrderRefundLines_Refund_OrderItem; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_OrderRefundLines_Refund_OrderItem" ON public."OrderRefundLines" USING btree ("OrderRefundId", "OrderItemId") WHERE ("IsDeleted" = false);


--
-- TOC entry 6307 (class 1259 OID 2287745)
-- Name: IX_OrderRefundLines_TenantId_IsDeleted; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OrderRefundLines_TenantId_IsDeleted" ON public."OrderRefundLines" USING btree ("TenantId", "IsDeleted");


--
-- TOC entry 6298 (class 1259 OID 2287686)
-- Name: IX_OrderRefunds_TenantId_OrderId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OrderRefunds_TenantId_OrderId" ON public."OrderRefunds" USING btree ("TenantId", "OrderId");


--
-- TOC entry 6299 (class 1259 OID 2287687)
-- Name: IX_OrderRefunds_TenantId_OrderId_RefundId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_OrderRefunds_TenantId_OrderId_RefundId" ON public."OrderRefunds" USING btree ("TenantId", "OrderId", "RefundId") WHERE ("IsDeleted" = false);


--
-- TOC entry 6300 (class 1259 OID 2287688)
-- Name: IX_OrderRefunds_TenantId_SettlementBatchId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OrderRefunds_TenantId_SettlementBatchId" ON public."OrderRefunds" USING btree ("TenantId", "SettlementBatchId") WHERE (("SettlementBatchId" IS NOT NULL) AND ("IsDeleted" = false));


--
-- TOC entry 6268 (class 1259 OID 2277992)
-- Name: IX_OrderRequestIdempotency_CompletedAtUtc_IsDeleted; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OrderRequestIdempotency_CompletedAtUtc_IsDeleted" ON public."OrderRequestIdempotency" USING btree ("CompletedAtUtc", "IsDeleted") WHERE ("CompletedAtUtc" IS NOT NULL);


--
-- TOC entry 6269 (class 1259 OID 2277990)
-- Name: IX_OrderRequestIdempotency_TenantId_Key; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_OrderRequestIdempotency_TenantId_Key" ON public."OrderRequestIdempotency" USING btree ("TenantId", "Key");


--
-- TOC entry 6270 (class 1259 OID 2277991)
-- Name: IX_OrderRequestIdempotency_TenantId_OrderId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OrderRequestIdempotency_TenantId_OrderId" ON public."OrderRequestIdempotency" USING btree ("TenantId", "OrderId");


--
-- TOC entry 6271 (class 1259 OID 2287728)
-- Name: IX_OrderRequestIdempotency_TenantId_RequestHash_IsDeleted; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OrderRequestIdempotency_TenantId_RequestHash_IsDeleted" ON public."OrderRequestIdempotency" USING btree ("TenantId", "RequestHash", "IsDeleted");


--
-- TOC entry 5393 (class 1259 OID 36158)
-- Name: IX_Orders_AffiliateAccountId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Orders_AffiliateAccountId" ON public."Orders" USING btree ("AffiliateAccountId") WHERE ("AffiliateAccountId" IS NOT NULL);


--
-- TOC entry 5394 (class 1259 OID 26209)
-- Name: IX_Orders_AppliedPromotionId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Orders_AppliedPromotionId" ON public."Orders" USING btree ("AppliedPromotionId");


--
-- TOC entry 5395 (class 1259 OID 35392)
-- Name: IX_Orders_CarrierCode; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Orders_CarrierCode" ON public."Orders" USING btree ("FulfillmentCarrierCode") WHERE ("FulfillmentCarrierCode" IS NOT NULL);


--
-- TOC entry 5396 (class 1259 OID 23519)
-- Name: IX_Orders_CouponId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Orders_CouponId" ON public."Orders" USING btree ("CouponId");


--
-- TOC entry 5397 (class 1259 OID 23520)
-- Name: IX_Orders_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Orders_CreatedAt" ON public."Orders" USING btree ("CreatedAt");


--
-- TOC entry 5398 (class 1259 OID 23521)
-- Name: IX_Orders_CustomerId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Orders_CustomerId" ON public."Orders" USING btree ("CustomerId");


--
-- TOC entry 5399 (class 1259 OID 35391)
-- Name: IX_Orders_FulfillmentMode; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Orders_FulfillmentMode" ON public."Orders" USING btree ("FulfillmentMode", "FulfillmentStatus");


--
-- TOC entry 5400 (class 1259 OID 35369)
-- Name: IX_Orders_FulfillmentStatus; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Orders_FulfillmentStatus" ON public."Orders" USING btree ("FulfillmentStatus");


--
-- TOC entry 5401 (class 1259 OID 36155)
-- Name: IX_Orders_IsGuestCheckout; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Orders_IsGuestCheckout" ON public."Orders" USING btree ("IsGuestCheckout") WHERE ("IsGuestCheckout" = true);


--
-- TOC entry 5402 (class 1259 OID 25892)
-- Name: IX_Orders_OnlineCustomerId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Orders_OnlineCustomerId" ON public."Orders" USING btree ("OnlineCustomerId");


--
-- TOC entry 5403 (class 1259 OID 23523)
-- Name: IX_Orders_PaymentStatus; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Orders_PaymentStatus" ON public."Orders" USING btree ("PaymentStatus");


--
-- TOC entry 5404 (class 1259 OID 36200)
-- Name: IX_Orders_PromotionLinkId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Orders_PromotionLinkId" ON public."Orders" USING btree ("PromotionLinkId") WHERE ("PromotionLinkId" IS NOT NULL);


--
-- TOC entry 5405 (class 1259 OID 36419)
-- Name: IX_Orders_SessionId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Orders_SessionId" ON public."Orders" USING btree ("SessionId");


--
-- TOC entry 5406 (class 1259 OID 23524)
-- Name: IX_Orders_ShippingMethodId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Orders_ShippingMethodId" ON public."Orders" USING btree ("ShippingMethodId");


--
-- TOC entry 5407 (class 1259 OID 23525)
-- Name: IX_Orders_Source; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Orders_Source" ON public."Orders" USING btree ("Source");


--
-- TOC entry 5408 (class 1259 OID 23526)
-- Name: IX_Orders_SourceOrderId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Orders_SourceOrderId" ON public."Orders" USING btree ("SourceOrderId");


--
-- TOC entry 5409 (class 1259 OID 23527)
-- Name: IX_Orders_Status; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Orders_Status" ON public."Orders" USING btree ("Status");


--
-- TOC entry 5410 (class 1259 OID 2287769)
-- Name: IX_Orders_TenantId_CartId_ActiveStatuses; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_Orders_TenantId_CartId_ActiveStatuses" ON public."Orders" USING btree ("TenantId", "CartId") WHERE (("IsDeleted" = false) AND ("CartId" IS NOT NULL) AND ("Status" = ANY (ARRAY[0, 1, 2, 8])));


--
-- TOC entry 5411 (class 1259 OID 2287809)
-- Name: IX_Orders_TenantId_CheckoutState_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Orders_TenantId_CheckoutState_CreatedAt" ON public."Orders" USING btree ("TenantId", "CheckoutState", "CreatedAt");


--
-- TOC entry 5412 (class 1259 OID 3022034)
-- Name: IX_Orders_TenantId_FinancialPostingStatus_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Orders_TenantId_FinancialPostingStatus_CreatedAt" ON public."Orders" USING btree ("TenantId", "FinancialPostingStatus", "CreatedAt");


--
-- TOC entry 5413 (class 1259 OID 35425)
-- Name: IX_Orders_TenantId_FulfillmentMode; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Orders_TenantId_FulfillmentMode" ON public."Orders" USING btree ("TenantId", "FulfillmentMode");


--
-- TOC entry 6861 (class 0 OID 0)
-- Dependencies: 5413
-- Name: INDEX "IX_Orders_TenantId_FulfillmentMode"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON INDEX public."IX_Orders_TenantId_FulfillmentMode" IS 'Optimizes queries filtering orders by fulfillment mode (manual vs carrier).';


--
-- TOC entry 5414 (class 1259 OID 35370)
-- Name: IX_Orders_TenantId_FulfillmentStatus; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Orders_TenantId_FulfillmentStatus" ON public."Orders" USING btree ("TenantId", "FulfillmentStatus");


--
-- TOC entry 5415 (class 1259 OID 36438)
-- Name: IX_Orders_TenantId_GuestPhone; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Orders_TenantId_GuestPhone" ON public."Orders" USING btree ("TenantId", "GuestPhone");


--
-- TOC entry 5416 (class 1259 OID 55182)
-- Name: IX_Orders_TenantId_OrderNumber; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_Orders_TenantId_OrderNumber" ON public."Orders" USING btree ("TenantId", "OrderNumber") WHERE ("IsDeleted" = false);


--
-- TOC entry 5417 (class 1259 OID 55335)
-- Name: IX_Orders_TenantId_PaymentStatus_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Orders_TenantId_PaymentStatus_CreatedAt" ON public."Orders" USING btree ("TenantId", "PaymentStatus", "CreatedAt");


--
-- TOC entry 5418 (class 1259 OID 55333)
-- Name: IX_Orders_TenantId_Source_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Orders_TenantId_Source_CreatedAt" ON public."Orders" USING btree ("TenantId", "Source", "CreatedAt");


--
-- TOC entry 5419 (class 1259 OID 55334)
-- Name: IX_Orders_TenantId_Status_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Orders_TenantId_Status_CreatedAt" ON public."Orders" USING btree ("TenantId", "Status", "CreatedAt");


--
-- TOC entry 5420 (class 1259 OID 23528)
-- Name: IX_Orders_WarehouseId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Orders_WarehouseId" ON public."Orders" USING btree ("WarehouseId");


--
-- TOC entry 5440 (class 1259 OID 23580)
-- Name: IX_OutboxMessage_EnqueueTime; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OutboxMessage_EnqueueTime" ON public."OutboxMessage" USING btree ("EnqueueTime");


--
-- TOC entry 5441 (class 1259 OID 23581)
-- Name: IX_OutboxMessage_ExpirationTime; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OutboxMessage_ExpirationTime" ON public."OutboxMessage" USING btree ("ExpirationTime");


--
-- TOC entry 5442 (class 1259 OID 23582)
-- Name: IX_OutboxMessage_InboxMessageId_InboxConsumerId_SequenceNumber; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_OutboxMessage_InboxMessageId_InboxConsumerId_SequenceNumber" ON public."OutboxMessage" USING btree ("InboxMessageId", "InboxConsumerId", "SequenceNumber");


--
-- TOC entry 5443 (class 1259 OID 23583)
-- Name: IX_OutboxMessage_OutboxId_SequenceNumber; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_OutboxMessage_OutboxId_SequenceNumber" ON public."OutboxMessage" USING btree ("OutboxId", "SequenceNumber");


--
-- TOC entry 5623 (class 1259 OID 3022077)
-- Name: IX_OutboxMessages_CriticalAggregateOrdering; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OutboxMessages_CriticalAggregateOrdering" ON public."OutboxMessages" USING btree ("TenantId", "AggregateId", "EventVersion", "CreatedAtUtc", "Id") WHERE (("ProcessedAtUtc" IS NULL) AND ("TenantId" IS NOT NULL) AND ("AggregateId" IS NOT NULL));


--
-- TOC entry 5624 (class 1259 OID 3022208)
-- Name: IX_OutboxMessages_OperatorReconciliation; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OutboxMessages_OperatorReconciliation" ON public."OutboxMessages" USING btree ("RequiresOperatorReconciliation", "ProcessedAtUtc", "CreatedAtUtc") WHERE (("RequiresOperatorReconciliation" = true) AND ("ProcessedAtUtc" IS NULL));


--
-- TOC entry 5625 (class 1259 OID 34913)
-- Name: IX_OutboxMessages_Processed; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OutboxMessages_Processed" ON public."OutboxMessages" USING btree ("ProcessedAtUtc") WHERE ("ProcessedAtUtc" IS NOT NULL);


--
-- TOC entry 5626 (class 1259 OID 34914)
-- Name: IX_OutboxMessages_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OutboxMessages_TenantId" ON public."OutboxMessages" USING btree ("TenantId") WHERE ("TenantId" IS NOT NULL);


--
-- TOC entry 5627 (class 1259 OID 34912)
-- Name: IX_OutboxMessages_Unprocessed; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OutboxMessages_Unprocessed" ON public."OutboxMessages" USING btree ("CreatedAtUtc") WHERE ("ProcessedAtUtc" IS NULL);


--
-- TOC entry 5437 (class 1259 OID 23584)
-- Name: IX_OutboxState_Created; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_OutboxState_Created" ON public."OutboxState" USING btree ("Created");


--
-- TOC entry 6253 (class 1259 OID 46969)
-- Name: IX_PartyLedgerAdjustments_TenantId_IdempotencyKey; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_PartyLedgerAdjustments_TenantId_IdempotencyKey" ON public."PartyLedgerAdjustments" USING btree ("TenantId", "IdempotencyKey");


--
-- TOC entry 6254 (class 1259 OID 46970)
-- Name: IX_PartyLedgerAdjustments_TenantId_PartyType_PartyId_Adjustment; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PartyLedgerAdjustments_TenantId_PartyType_PartyId_Adjustment" ON public."PartyLedgerAdjustments" USING btree ("TenantId", "PartyType", "PartyId", "AdjustmentDate");


--
-- TOC entry 6348 (class 1259 OID 3022550)
-- Name: IX_PaymentCancellationOutbox_OrderId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PaymentCancellationOutbox_OrderId" ON public."PaymentCancellationOutboxMessages" USING btree ("OrderId");


--
-- TOC entry 6349 (class 1259 OID 3022548)
-- Name: IX_PaymentCancellationOutbox_Processing; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PaymentCancellationOutbox_Processing" ON public."PaymentCancellationOutboxMessages" USING btree ("ProcessedAtUtc", "IsFailed", "NextRetryAtUtc") INCLUDE ("CreatedAt");


--
-- TOC entry 6350 (class 1259 OID 3022549)
-- Name: IX_PaymentCancellationOutbox_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PaymentCancellationOutbox_TenantId" ON public."PaymentCancellationOutboxMessages" USING btree ("TenantId");


--
-- TOC entry 5453 (class 1259 OID 2287550)
-- Name: IX_PaymentIntents_Active_OrderId_Provider_Unique; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_PaymentIntents_Active_OrderId_Provider_Unique" ON public."PaymentIntents" USING btree ("TenantId", "OrderId", "Provider") WHERE (("IsDeleted" = false) AND ("Status" = ANY (ARRAY[0, 1, 2, 7])));


--
-- TOC entry 5454 (class 1259 OID 23675)
-- Name: IX_PaymentIntents_OrderId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PaymentIntents_OrderId" ON public."PaymentIntents" USING btree ("OrderId");


--
-- TOC entry 5455 (class 1259 OID 2287503)
-- Name: IX_PaymentIntents_Status_InventoryCommitted; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PaymentIntents_Status_InventoryCommitted" ON public."PaymentIntents" USING btree ("Status", "InventoryCommitted") WHERE (("Status" = 3) AND ("InventoryCommitted" = false));


--
-- TOC entry 5456 (class 1259 OID 3022509)
-- Name: IX_PaymentIntents_TenantId_PayoutBatchId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PaymentIntents_TenantId_PayoutBatchId" ON public."PaymentIntents" USING btree ("TenantId", "PayoutBatchId");


--
-- TOC entry 5668 (class 1259 OID 35196)
-- Name: IX_PaymentMethods_Tenant; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PaymentMethods_Tenant" ON public."PaymentMethods" USING btree ("TenantId");


--
-- TOC entry 5669 (class 1259 OID 35195)
-- Name: IX_PaymentMethods_TenantCode; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_PaymentMethods_TenantCode" ON public."PaymentMethods" USING btree ("TenantId", "Code");


--
-- TOC entry 6339 (class 1259 OID 3022510)
-- Name: IX_PaymentSettlementBatches_Tenant_PayoutBatchId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PaymentSettlementBatches_Tenant_PayoutBatchId" ON public."PaymentSettlementBatches" USING btree ("TenantId", "PayoutBatchId");


--
-- TOC entry 6340 (class 1259 OID 3022511)
-- Name: IX_PaymentSettlementBatches_Tenant_Type_SettlementId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_PaymentSettlementBatches_Tenant_Type_SettlementId" ON public."PaymentSettlementBatches" USING btree ("TenantId", "SettlementType", "SettlementId") WHERE ("IsDeleted" = false);


--
-- TOC entry 6343 (class 1259 OID 3022512)
-- Name: IX_PaymentSettlementOrders_OrderId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PaymentSettlementOrders_OrderId" ON public."PaymentSettlementOrders" USING btree ("OrderId");


--
-- TOC entry 6344 (class 1259 OID 3022513)
-- Name: IX_PaymentSettlementOrders_Tenant_Batch_Order; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_PaymentSettlementOrders_Tenant_Batch_Order" ON public."PaymentSettlementOrders" USING btree ("TenantId", "SettlementBatchId", "OrderId") WHERE ("IsDeleted" = false);


--
-- TOC entry 6345 (class 1259 OID 3022514)
-- Name: IX_PaymentSettlementOrders_Tenant_Order; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_PaymentSettlementOrders_Tenant_Order" ON public."PaymentSettlementOrders" USING btree ("TenantId", "OrderId") WHERE ("IsDeleted" = false);


--
-- TOC entry 5468 (class 1259 OID 23676)
-- Name: IX_PaymentWebhookLogs_PaymentIntentId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PaymentWebhookLogs_PaymentIntentId" ON public."PaymentWebhookLogs" USING btree ("PaymentIntentId");


--
-- TOC entry 5469 (class 1259 OID 3022474)
-- Name: IX_PaymentWebhookLogs_TenantId_Provider_ProviderEventId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_PaymentWebhookLogs_TenantId_Provider_ProviderEventId" ON public."PaymentWebhookLogs" USING btree ("TenantId", "Provider", "ProviderEventId");


--
-- TOC entry 5837 (class 1259 OID 36436)
-- Name: IX_PhoneRiskProfiles_TenantId_IsBlocked_BlockedAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PhoneRiskProfiles_TenantId_IsBlocked_BlockedAtUtc" ON public."PhoneRiskProfiles" USING btree ("TenantId", "IsBlocked", "BlockedAtUtc");


--
-- TOC entry 5838 (class 1259 OID 36435)
-- Name: IX_PhoneRiskProfiles_TenantId_PhoneNumber; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_PhoneRiskProfiles_TenantId_PhoneNumber" ON public."PhoneRiskProfiles" USING btree ("TenantId", "PhoneNumber");


--
-- TOC entry 5844 (class 1259 OID 36467)
-- Name: IX_PhoneSpamReports_PhoneNumber; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PhoneSpamReports_PhoneNumber" ON public."PhoneSpamReports" USING btree ("PhoneNumber");


--
-- TOC entry 5845 (class 1259 OID 36466)
-- Name: IX_PhoneSpamReports_TenantId_OrderId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_PhoneSpamReports_TenantId_OrderId" ON public."PhoneSpamReports" USING btree ("TenantId", "OrderId");


--
-- TOC entry 5582 (class 1259 OID 34648)
-- Name: IX_PixelTrackingConfigs_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_PixelTrackingConfigs_TenantId" ON public."PixelTrackingConfigs" USING btree ("TenantId");


--
-- TOC entry 6225 (class 1259 OID 46745)
-- Name: IX_ProcessedEvents_EventId_ConsumerName; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_ProcessedEvents_EventId_ConsumerName" ON public."ProcessedEvents" USING btree ("EventId", "ConsumerName");


--
-- TOC entry 6226 (class 1259 OID 46746)
-- Name: IX_ProcessedEvents_ProcessedAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ProcessedEvents_ProcessedAtUtc" ON public."ProcessedEvents" USING btree ("ProcessedAtUtc");


--
-- TOC entry 6227 (class 1259 OID 46747)
-- Name: IX_ProcessedEvents_TenantId_EntityId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ProcessedEvents_TenantId_EntityId" ON public."ProcessedEvents" USING btree ("TenantId", "EntityId");


--
-- TOC entry 5753 (class 1259 OID 35912)
-- Name: IX_ProcessedTrustEvent_EventId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_ProcessedTrustEvent_EventId" ON public."ProcessedTrustEvent" USING btree ("EventId");


--
-- TOC entry 5754 (class 1259 OID 35913)
-- Name: IX_ProcessedTrustEvent_ProviderProfileId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ProcessedTrustEvent_ProviderProfileId" ON public."ProcessedTrustEvent" USING btree ("ProviderProfileId");


--
-- TOC entry 5755 (class 1259 OID 35914)
-- Name: IX_ProcessedTrustEvent_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ProcessedTrustEvent_TenantId" ON public."ProcessedTrustEvent" USING btree ("TenantId");


--
-- TOC entry 5848 (class 1259 OID 36544)
-- Name: IX_ProductMedia_ProductId_Position; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ProductMedia_ProductId_Position" ON public."ProductMedia" USING btree ("ProductId", "Position");


--
-- TOC entry 5585 (class 1259 OID 34679)
-- Name: IX_ProductPixelOverride_Tenant; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ProductPixelOverride_Tenant" ON public."ProductPixelOverrides" USING btree ("TenantId") WHERE ("IsEnabled" = true);


--
-- TOC entry 5586 (class 1259 OID 34678)
-- Name: IX_ProductPixelOverride_Tenant_Product; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_ProductPixelOverride_Tenant_Product" ON public."ProductPixelOverrides" USING btree ("TenantId", "ProductId");


--
-- TOC entry 5587 (class 1259 OID 34711)
-- Name: IX_ProductPixelOverrides_ProductId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ProductPixelOverrides_ProductId" ON public."ProductPixelOverrides" USING btree ("ProductId");


--
-- TOC entry 5588 (class 1259 OID 34712)
-- Name: IX_ProductPixelOverrides_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ProductPixelOverrides_TenantId" ON public."ProductPixelOverrides" USING btree ("TenantId");


--
-- TOC entry 5589 (class 1259 OID 34713)
-- Name: IX_ProductPixelOverrides_TenantId_ProductId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_ProductPixelOverrides_TenantId_ProductId" ON public."ProductPixelOverrides" USING btree ("TenantId", "ProductId");


--
-- TOC entry 6361 (class 1259 OID 3795755)
-- Name: IX_ProductRequestIdempotency_TenantId_Key_Operation; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_ProductRequestIdempotency_TenantId_Key_Operation" ON public."ProductRequestIdempotency" USING btree ("TenantId", "Key", "Operation");


--
-- TOC entry 6362 (class 1259 OID 3795756)
-- Name: IX_ProductRequestIdempotency_TenantId_ProductId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ProductRequestIdempotency_TenantId_ProductId" ON public."ProductRequestIdempotency" USING btree ("TenantId", "ProductId");


--
-- TOC entry 6363 (class 1259 OID 3795757)
-- Name: IX_ProductRequestIdempotency_TenantId_ProductVariantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ProductRequestIdempotency_TenantId_ProductVariantId" ON public."ProductRequestIdempotency" USING btree ("TenantId", "ProductVariantId");


--
-- TOC entry 6231 (class 1259 OID 46778)
-- Name: IX_ProductReviews_ProductId_Status_CreatedAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ProductReviews_ProductId_Status_CreatedAtUtc" ON public."ProductReviews" USING btree ("ProductId", "Status", "CreatedAtUtc");


--
-- TOC entry 6232 (class 1259 OID 46779)
-- Name: IX_ProductReviews_ProductId_UserId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_ProductReviews_ProductId_UserId" ON public."ProductReviews" USING btree ("ProductId", "UserId") WHERE ("IsDeleted" = false);


--
-- TOC entry 6233 (class 1259 OID 46780)
-- Name: IX_ProductReviews_Status_Rating_ProductId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ProductReviews_Status_Rating_ProductId" ON public."ProductReviews" USING btree ("Status", "Rating", "ProductId");


--
-- TOC entry 6234 (class 1259 OID 46781)
-- Name: IX_ProductReviews_UserId_CreatedAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ProductReviews_UserId_CreatedAtUtc" ON public."ProductReviews" USING btree ("UserId", "CreatedAtUtc");


--
-- TOC entry 5344 (class 1259 OID 21548)
-- Name: IX_ProductUnits_ProductId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ProductUnits_ProductId" ON public."ProductUnits" USING btree ("ProductId");


--
-- TOC entry 5345 (class 1259 OID 21549)
-- Name: IX_ProductUnits_UnitId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ProductUnits_UnitId" ON public."ProductUnits" USING btree ("UnitId");


--
-- TOC entry 5532 (class 1259 OID 25982)
-- Name: IX_ProductVariantAttributes_VariantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ProductVariantAttributes_VariantId" ON public."ProductVariantAttributes" USING btree ("VariantId");


--
-- TOC entry 5533 (class 1259 OID 25983)
-- Name: IX_ProductVariantAttributes_VariantId_Name; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ProductVariantAttributes_VariantId_Name" ON public."ProductVariantAttributes" USING btree ("VariantId", "Name");


--
-- TOC entry 5536 (class 1259 OID 25984)
-- Name: IX_ProductVariantStocks_TenantId_VariantId_WarehouseId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_ProductVariantStocks_TenantId_VariantId_WarehouseId" ON public."ProductVariantStocks" USING btree ("TenantId", "VariantId", "WarehouseId");


--
-- TOC entry 5537 (class 1259 OID 25985)
-- Name: IX_ProductVariantStocks_VariantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ProductVariantStocks_VariantId" ON public."ProductVariantStocks" USING btree ("VariantId");


--
-- TOC entry 5538 (class 1259 OID 25986)
-- Name: IX_ProductVariantStocks_WarehouseId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ProductVariantStocks_WarehouseId" ON public."ProductVariantStocks" USING btree ("WarehouseId");


--
-- TOC entry 5164 (class 1259 OID 25978)
-- Name: IX_ProductVariants_Barcode; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ProductVariants_Barcode" ON public."ProductVariants" USING btree ("Barcode") WHERE (("IsDeleted" = false) AND ("Barcode" IS NOT NULL));


--
-- TOC entry 5165 (class 1259 OID 25981)
-- Name: IX_ProductVariants_ProductId_IsDefault; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ProductVariants_ProductId_IsDefault" ON public."ProductVariants" USING btree ("ProductId", "IsDefault") WHERE ("IsDefault" = true);


--
-- TOC entry 5166 (class 1259 OID 25979)
-- Name: IX_ProductVariants_SKU; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ProductVariants_SKU" ON public."ProductVariants" USING btree ("SKU") WHERE (("IsDeleted" = false) AND ("SKU" IS NOT NULL));


--
-- TOC entry 5167 (class 1259 OID 25980)
-- Name: IX_ProductVariants_TenantId_ProductId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ProductVariants_TenantId_ProductId" ON public."ProductVariants" USING btree ("TenantId", "ProductId");


--
-- TOC entry 5125 (class 1259 OID 20477)
-- Name: IX_Products_Barcode; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Products_Barcode" ON public."Products" USING btree ("Barcode");


--
-- TOC entry 5126 (class 1259 OID 20478)
-- Name: IX_Products_CategoryId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Products_CategoryId" ON public."Products" USING btree ("CategoryId");


--
-- TOC entry 5127 (class 1259 OID 23509)
-- Name: IX_Products_IsOnlineVisible; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Products_IsOnlineVisible" ON public."Products" USING btree ("IsOnlineVisible");


--
-- TOC entry 5128 (class 1259 OID 20479)
-- Name: IX_Products_Name; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Products_Name" ON public."Products" USING btree ("Name");


--
-- TOC entry 5129 (class 1259 OID 23510)
-- Name: IX_Products_PublishStatus; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Products_PublishStatus" ON public."Products" USING btree ("PublishStatus");


--
-- TOC entry 5130 (class 1259 OID 23511)
-- Name: IX_Products_SKU; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_Products_SKU" ON public."Products" USING btree ("SKU") WHERE (("IsDeleted" = false) AND ("SKU" IS NOT NULL));


--
-- TOC entry 5131 (class 1259 OID 23512)
-- Name: IX_Products_Slug; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_Products_Slug" ON public."Products" USING btree ("Slug") WHERE (("IsDeleted" = false) AND ("Slug" IS NOT NULL));


--
-- TOC entry 5132 (class 1259 OID 55320)
-- Name: IX_Products_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Products_TenantId" ON public."Products" USING btree ("TenantId");


--
-- TOC entry 5133 (class 1259 OID 55321)
-- Name: IX_Products_TenantId_CategoryId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Products_TenantId_CategoryId" ON public."Products" USING btree ("TenantId", "CategoryId");


--
-- TOC entry 6143 (class 1259 OID 38244)
-- Name: IX_PromoCodeUsages_MarketplaceOrderId_AppliedAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PromoCodeUsages_MarketplaceOrderId_AppliedAtUtc" ON public."PromoCodeUsages" USING btree ("MarketplaceOrderId", "AppliedAtUtc" DESC) WHERE ("IsDeleted" = false);


--
-- TOC entry 6144 (class 1259 OID 38192)
-- Name: IX_PromoCodeUsages_PromoCodeId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PromoCodeUsages_PromoCodeId" ON public."PromoCodeUsages" USING btree ("PromoCodeId");


--
-- TOC entry 6145 (class 1259 OID 38193)
-- Name: IX_PromoCodeUsages_PromoCodeId_CustomerId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PromoCodeUsages_PromoCodeId_CustomerId" ON public."PromoCodeUsages" USING btree ("PromoCodeId", "CustomerId") WHERE (("CustomerId" IS NOT NULL) AND ("IsDeleted" = false));


--
-- TOC entry 6137 (class 1259 OID 38144)
-- Name: IX_PromoCodes_CategoryId_IsActive; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PromoCodes_CategoryId_IsActive" ON public."PromoCodes" USING btree ("CategoryId", "IsActive");


--
-- TOC entry 6138 (class 1259 OID 38142)
-- Name: IX_PromoCodes_IsActive_ExpiryDate; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PromoCodes_IsActive_ExpiryDate" ON public."PromoCodes" USING btree ("IsActive", "ExpiryDate");


--
-- TOC entry 6139 (class 1259 OID 38143)
-- Name: IX_PromoCodes_VendorId_IsActive; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PromoCodes_VendorId_IsActive" ON public."PromoCodes" USING btree ("VendorId", "IsActive");


--
-- TOC entry 5561 (class 1259 OID 26203)
-- Name: IX_PromotionUsages_TenantId_PromotionId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PromotionUsages_TenantId_PromotionId" ON public."PromotionUsages" USING btree ("TenantId", "PromotionId");


--
-- TOC entry 5562 (class 1259 OID 26202)
-- Name: IX_PromotionUsages_TenantId_PromotionId_OrderId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_PromotionUsages_TenantId_PromotionId_OrderId" ON public."PromotionUsages" USING btree ("TenantId", "PromotionId", "OrderId") WHERE ("IsDeleted" = false);


--
-- TOC entry 5555 (class 1259 OID 26166)
-- Name: IX_Promotions_Priority; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Promotions_Priority" ON public."Promotions" USING btree ("Priority");


--
-- TOC entry 5556 (class 1259 OID 26164)
-- Name: IX_Promotions_TenantId_IsActive_StartDate_EndDate; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Promotions_TenantId_IsActive_StartDate_EndDate" ON public."Promotions" USING btree ("TenantId", "IsActive", "StartDate", "EndDate");


--
-- TOC entry 5557 (class 1259 OID 26163)
-- Name: IX_Promotions_TenantId_Name; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_Promotions_TenantId_Name" ON public."Promotions" USING btree ("TenantId", "Name") WHERE ("IsDeleted" = false);


--
-- TOC entry 5558 (class 1259 OID 26165)
-- Name: IX_Promotions_TenantId_Scope_AppliesToId_IsActive; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Promotions_TenantId_Scope_AppliesToId_IsActive" ON public."Promotions" USING btree ("TenantId", "Scope", "AppliesToId", "IsActive");


--
-- TOC entry 5735 (class 1259 OID 35716)
-- Name: IX_ProviderTrustAggregates_AverageRating; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ProviderTrustAggregates_AverageRating" ON public."ProviderTrustAggregates" USING btree ("AverageRating" DESC) WHERE ("IsDeleted" = false);


--
-- TOC entry 5736 (class 1259 OID 35715)
-- Name: IX_ProviderTrustAggregates_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ProviderTrustAggregates_TenantId" ON public."ProviderTrustAggregates" USING btree ("TenantId");


--
-- TOC entry 5737 (class 1259 OID 35717)
-- Name: IX_ProviderTrustAggregates_TotalReviews; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ProviderTrustAggregates_TotalReviews" ON public."ProviderTrustAggregates" USING btree ("TotalReviews" DESC) WHERE ("IsDeleted" = false);


--
-- TOC entry 5177 (class 1259 OID 20482)
-- Name: IX_PurchaseInvoiceItems_ProductId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PurchaseInvoiceItems_ProductId" ON public."PurchaseInvoiceItems" USING btree ("ProductId");


--
-- TOC entry 5178 (class 1259 OID 20483)
-- Name: IX_PurchaseInvoiceItems_ProductVariantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PurchaseInvoiceItems_ProductVariantId" ON public."PurchaseInvoiceItems" USING btree ("ProductVariantId");


--
-- TOC entry 5179 (class 1259 OID 20484)
-- Name: IX_PurchaseInvoiceItems_PurchaseInvoiceId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PurchaseInvoiceItems_PurchaseInvoiceId" ON public."PurchaseInvoiceItems" USING btree ("PurchaseInvoiceId");


--
-- TOC entry 5180 (class 1259 OID 21547)
-- Name: IX_PurchaseInvoiceItems_UnitId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PurchaseInvoiceItems_UnitId" ON public."PurchaseInvoiceItems" USING btree ("UnitId");


--
-- TOC entry 5170 (class 1259 OID 20485)
-- Name: IX_PurchaseInvoicePayments_PurchaseInvoiceId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PurchaseInvoicePayments_PurchaseInvoiceId" ON public."PurchaseInvoicePayments" USING btree ("PurchaseInvoiceId");


--
-- TOC entry 5143 (class 1259 OID 20486)
-- Name: IX_PurchaseInvoices_SupplierId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PurchaseInvoices_SupplierId" ON public."PurchaseInvoices" USING btree ("SupplierId");


--
-- TOC entry 5144 (class 1259 OID 20487)
-- Name: IX_PurchaseInvoices_WarehouseId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PurchaseInvoices_WarehouseId" ON public."PurchaseInvoices" USING btree ("WarehouseId");


--
-- TOC entry 6241 (class 1259 OID 733746)
-- Name: IX_PurchaseRequestIdempotency_TenantId_Key_Operation; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_PurchaseRequestIdempotency_TenantId_Key_Operation" ON public."PurchaseRequestIdempotency" USING btree ("TenantId", "Key", "Operation");


--
-- TOC entry 6242 (class 1259 OID 46883)
-- Name: IX_PurchaseRequestIdempotency_TenantId_PurchaseInvoiceId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PurchaseRequestIdempotency_TenantId_PurchaseInvoiceId" ON public."PurchaseRequestIdempotency" USING btree ("TenantId", "PurchaseInvoiceId");


--
-- TOC entry 6245 (class 1259 OID 733748)
-- Name: IX_PurchaseReturnRequestIdempotency_TenantId_Key; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_PurchaseReturnRequestIdempotency_TenantId_Key" ON public."PurchaseReturnRequestIdempotency" USING btree ("TenantId", "Key");


--
-- TOC entry 6246 (class 1259 OID 46901)
-- Name: IX_PurchaseReturnRequestIdempotency_TenantId_ReturnId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_PurchaseReturnRequestIdempotency_TenantId_ReturnId" ON public."PurchaseReturnRequestIdempotency" USING btree ("TenantId", "ReturnId");


--
-- TOC entry 5504 (class 1259 OID 25782)
-- Name: IX_RefreshToken_ExpiresAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_RefreshToken_ExpiresAtUtc" ON public."OnlineCustomerRefreshTokens" USING btree ("ExpiresAtUtc");


--
-- TOC entry 5505 (class 1259 OID 2287527)
-- Name: IX_RefreshToken_TenantId_CurrentAccessTokenJti; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_RefreshToken_TenantId_CurrentAccessTokenJti" ON public."OnlineCustomerRefreshTokens" USING btree ("TenantId", "CurrentAccessTokenJti");


--
-- TOC entry 5506 (class 1259 OID 25781)
-- Name: IX_RefreshToken_TenantId_CustomerId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_RefreshToken_TenantId_CustomerId" ON public."OnlineCustomerRefreshTokens" USING btree ("TenantId", "OnlineCustomerId");


--
-- TOC entry 5507 (class 1259 OID 25783)
-- Name: IX_RefreshToken_TenantId_CustomerId_IsRevoked; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_RefreshToken_TenantId_CustomerId_IsRevoked" ON public."OnlineCustomerRefreshTokens" USING btree ("TenantId", "OnlineCustomerId", "IsRevoked");


--
-- TOC entry 5508 (class 1259 OID 25780)
-- Name: IX_RefreshToken_TenantId_TokenHash; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_RefreshToken_TenantId_TokenHash" ON public."OnlineCustomerRefreshTokens" USING btree ("TenantId", "TokenHash");


--
-- TOC entry 5693 (class 1259 OID 35415)
-- Name: IX_RejectedCarrierWebhookEvents_OrderId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_RejectedCarrierWebhookEvents_OrderId" ON public."RejectedCarrierWebhookEvents" USING btree ("OrderId");


--
-- TOC entry 5694 (class 1259 OID 35416)
-- Name: IX_RejectedCarrierWebhookEvents_RejectedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_RejectedCarrierWebhookEvents_RejectedAt" ON public."RejectedCarrierWebhookEvents" USING btree ("RejectedAt");


--
-- TOC entry 5183 (class 1259 OID 20488)
-- Name: IX_ReturnItems_ProductId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ReturnItems_ProductId" ON public."ReturnItems" USING btree ("ProductId");


--
-- TOC entry 5184 (class 1259 OID 20489)
-- Name: IX_ReturnItems_ProductVariantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ReturnItems_ProductVariantId" ON public."ReturnItems" USING btree ("ProductVariantId");


--
-- TOC entry 5185 (class 1259 OID 20490)
-- Name: IX_ReturnItems_ReturnId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ReturnItems_ReturnId" ON public."ReturnItems" USING btree ("ReturnId");


--
-- TOC entry 5186 (class 1259 OID 21546)
-- Name: IX_ReturnItems_UnitId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ReturnItems_UnitId" ON public."ReturnItems" USING btree ("UnitId");


--
-- TOC entry 5147 (class 1259 OID 20491)
-- Name: IX_Returns_CustomerId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Returns_CustomerId" ON public."Returns" USING btree ("CustomerId");


--
-- TOC entry 5148 (class 1259 OID 20492)
-- Name: IX_Returns_SupplierId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Returns_SupplierId" ON public."Returns" USING btree ("SupplierId");


--
-- TOC entry 5149 (class 1259 OID 55317)
-- Name: IX_Returns_TenantId_ReturnNumber; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_Returns_TenantId_ReturnNumber" ON public."Returns" USING btree ("TenantId", "ReturnNumber") WHERE ("IsDeleted" = false);


--
-- TOC entry 5150 (class 1259 OID 20493)
-- Name: IX_Returns_WarehouseId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Returns_WarehouseId" ON public."Returns" USING btree ("WarehouseId");


--
-- TOC entry 6274 (class 1259 OID 2287520)
-- Name: IX_RevokedCustomerAccessTokens_CustomerId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_RevokedCustomerAccessTokens_CustomerId" ON public."RevokedCustomerAccessTokens" USING btree ("CustomerId");


--
-- TOC entry 6275 (class 1259 OID 2287521)
-- Name: IX_RevokedCustomerAccessTokens_ExpiresAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_RevokedCustomerAccessTokens_ExpiresAt" ON public."RevokedCustomerAccessTokens" USING btree ("ExpiresAt");


--
-- TOC entry 6280 (class 1259 OID 2287539)
-- Name: IX_RevokedStorefrontAccessTokens_ExpiresAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_RevokedStorefrontAccessTokens_ExpiresAtUtc" ON public."RevokedStorefrontAccessTokens" USING btree ("ExpiresAtUtc");


--
-- TOC entry 6281 (class 1259 OID 2287538)
-- Name: IX_RevokedStorefrontAccessTokens_TenantId_Jti; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_RevokedStorefrontAccessTokens_TenantId_Jti" ON public."RevokedStorefrontAccessTokens" USING btree ("TenantId", "Jti");


--
-- TOC entry 6282 (class 1259 OID 2287540)
-- Name: IX_RevokedStorefrontAccessTokens_TenantId_OnlineCustomerId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_RevokedStorefrontAccessTokens_TenantId_OnlineCustomerId" ON public."RevokedStorefrontAccessTokens" USING btree ("TenantId", "OnlineCustomerId");


--
-- TOC entry 5189 (class 1259 OID 20494)
-- Name: IX_SalesInvoiceItems_ProductId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SalesInvoiceItems_ProductId" ON public."SalesInvoiceItems" USING btree ("ProductId");


--
-- TOC entry 5190 (class 1259 OID 20495)
-- Name: IX_SalesInvoiceItems_ProductVariantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SalesInvoiceItems_ProductVariantId" ON public."SalesInvoiceItems" USING btree ("ProductVariantId");


--
-- TOC entry 5191 (class 1259 OID 55327)
-- Name: IX_SalesInvoiceItems_SalesInvoiceId_ProductId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SalesInvoiceItems_SalesInvoiceId_ProductId" ON public."SalesInvoiceItems" USING btree ("SalesInvoiceId", "ProductId");


--
-- TOC entry 5192 (class 1259 OID 55328)
-- Name: IX_SalesInvoiceItems_SalesInvoiceId_ProductVariantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SalesInvoiceItems_SalesInvoiceId_ProductVariantId" ON public."SalesInvoiceItems" USING btree ("SalesInvoiceId", "ProductVariantId");


--
-- TOC entry 5193 (class 1259 OID 21545)
-- Name: IX_SalesInvoiceItems_UnitId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SalesInvoiceItems_UnitId" ON public."SalesInvoiceItems" USING btree ("UnitId");


--
-- TOC entry 5173 (class 1259 OID 55330)
-- Name: IX_SalesInvoicePayments_PaymentMethod_PaymentDate; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SalesInvoicePayments_PaymentMethod_PaymentDate" ON public."SalesInvoicePayments" USING btree ("PaymentMethod", "PaymentDate");


--
-- TOC entry 5174 (class 1259 OID 55329)
-- Name: IX_SalesInvoicePayments_SalesInvoiceId_PaymentDate; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SalesInvoicePayments_SalesInvoiceId_PaymentDate" ON public."SalesInvoicePayments" USING btree ("SalesInvoiceId", "PaymentDate");


--
-- TOC entry 5153 (class 1259 OID 20498)
-- Name: IX_SalesInvoices_CustomerId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SalesInvoices_CustomerId" ON public."SalesInvoices" USING btree ("CustomerId");


--
-- TOC entry 5154 (class 1259 OID 21302)
-- Name: IX_SalesInvoices_SalesRepresentativeId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SalesInvoices_SalesRepresentativeId" ON public."SalesInvoices" USING btree ("SalesRepresentativeId");


--
-- TOC entry 5155 (class 1259 OID 55326)
-- Name: IX_SalesInvoices_TenantId_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SalesInvoices_TenantId_CreatedAt" ON public."SalesInvoices" USING btree ("TenantId", "CreatedAt");


--
-- TOC entry 5156 (class 1259 OID 55324)
-- Name: IX_SalesInvoices_TenantId_CustomerId_InvoiceDate; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SalesInvoices_TenantId_CustomerId_InvoiceDate" ON public."SalesInvoices" USING btree ("TenantId", "CustomerId", "InvoiceDate");


--
-- TOC entry 5157 (class 1259 OID 55322)
-- Name: IX_SalesInvoices_TenantId_InvoiceDate_Active; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SalesInvoices_TenantId_InvoiceDate_Active" ON public."SalesInvoices" USING btree ("TenantId", "InvoiceDate") WHERE (("IsDeleted" = false) AND ("Status" <> 2));


--
-- TOC entry 5158 (class 1259 OID 46861)
-- Name: IX_SalesInvoices_TenantId_InvoiceNumber_Active; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_SalesInvoices_TenantId_InvoiceNumber_Active" ON public."SalesInvoices" USING btree ("TenantId", "InvoiceNumber") WHERE ("IsDeleted" = false);


--
-- TOC entry 5159 (class 1259 OID 55323)
-- Name: IX_SalesInvoices_TenantId_Status_InvoiceDate; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SalesInvoices_TenantId_Status_InvoiceDate" ON public."SalesInvoices" USING btree ("TenantId", "Status", "InvoiceDate");


--
-- TOC entry 5160 (class 1259 OID 55325)
-- Name: IX_SalesInvoices_TenantId_WarehouseId_InvoiceDate; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SalesInvoices_TenantId_WarehouseId_InvoiceDate" ON public."SalesInvoices" USING btree ("TenantId", "WarehouseId", "InvoiceDate");


--
-- TOC entry 5161 (class 1259 OID 20499)
-- Name: IX_SalesInvoices_WarehouseId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SalesInvoices_WarehouseId" ON public."SalesInvoices" USING btree ("WarehouseId");


--
-- TOC entry 6249 (class 1259 OID 3022678)
-- Name: IX_SalesRequestIdempotency_TenantId_Key_Operation; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_SalesRequestIdempotency_TenantId_Key_Operation" ON public."SalesRequestIdempotency" USING btree ("TenantId", "Key", "Operation");


--
-- TOC entry 6250 (class 1259 OID 3022679)
-- Name: IX_SalesRequestIdempotency_TenantId_Operation_RequestHash_IsDel; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SalesRequestIdempotency_TenantId_Operation_RequestHash_IsDel" ON public."SalesRequestIdempotency" USING btree ("TenantId", "Operation", "RequestHash", "IsDeleted");


--
-- TOC entry 5889 (class 1259 OID 36802)
-- Name: IX_SearchClicks_ProductId_ClickedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SearchClicks_ProductId_ClickedAt" ON public."SearchClicks" USING btree ("ProductId", "ClickedAt");


--
-- TOC entry 5890 (class 1259 OID 36801)
-- Name: IX_SearchClicks_QueryText_ClickedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SearchClicks_QueryText_ClickedAt" ON public."SearchClicks" USING btree ("QueryText", "ClickedAt");


--
-- TOC entry 5891 (class 1259 OID 36803)
-- Name: IX_SearchClicks_TenantId_ClickedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SearchClicks_TenantId_ClickedAt" ON public."SearchClicks" USING btree ("TenantId", "ClickedAt");


--
-- TOC entry 5911 (class 1259 OID 36888)
-- Name: IX_SearchFilterEvents_FilterKey_OccurredAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SearchFilterEvents_FilterKey_OccurredAt" ON public."SearchFilterEvents" USING btree ("FilterKey", "OccurredAt");


--
-- TOC entry 5912 (class 1259 OID 36889)
-- Name: IX_SearchFilterEvents_QueryText_OccurredAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SearchFilterEvents_QueryText_OccurredAt" ON public."SearchFilterEvents" USING btree ("QueryText", "OccurredAt");


--
-- TOC entry 5913 (class 1259 OID 36890)
-- Name: IX_SearchFilterEvents_TenantId_OccurredAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SearchFilterEvents_TenantId_OccurredAt" ON public."SearchFilterEvents" USING btree ("TenantId", "OccurredAt");


--
-- TOC entry 5894 (class 1259 OID 36804)
-- Name: IX_SearchQueries_QueryText_SearchedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SearchQueries_QueryText_SearchedAt" ON public."SearchQueries" USING btree ("QueryText", "SearchedAt");


--
-- TOC entry 5895 (class 1259 OID 36805)
-- Name: IX_SearchQueries_ResultCount_SearchedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SearchQueries_ResultCount_SearchedAt" ON public."SearchQueries" USING btree ("ResultCount", "SearchedAt");


--
-- TOC entry 5728 (class 1259 OID 35800)
-- Name: IX_ServiceRequestMessages_MessageType; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ServiceRequestMessages_MessageType" ON public."ServiceRequestMessages" USING btree ("MessageType") WHERE ("IsDeleted" = false);


--
-- TOC entry 5729 (class 1259 OID 35799)
-- Name: IX_ServiceRequestMessages_SenderRole; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ServiceRequestMessages_SenderRole" ON public."ServiceRequestMessages" USING btree ("SenderRole") WHERE ("IsDeleted" = false);


--
-- TOC entry 5730 (class 1259 OID 35661)
-- Name: IX_ServiceRequestMessages_SenderUserId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ServiceRequestMessages_SenderUserId" ON public."ServiceRequestMessages" USING btree ("SenderUserId");


--
-- TOC entry 5731 (class 1259 OID 35660)
-- Name: IX_ServiceRequestMessages_ServiceRequestId_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ServiceRequestMessages_ServiceRequestId_CreatedAt" ON public."ServiceRequestMessages" USING btree ("ServiceRequestId", "CreatedAt");


--
-- TOC entry 5732 (class 1259 OID 35659)
-- Name: IX_ServiceRequestMessages_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ServiceRequestMessages_TenantId" ON public."ServiceRequestMessages" USING btree ("TenantId");


--
-- TOC entry 5711 (class 1259 OID 35553)
-- Name: IX_ServiceRequests_BuyerUserId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ServiceRequests_BuyerUserId" ON public."ServiceRequests" USING btree ("BuyerUserId");


--
-- TOC entry 5712 (class 1259 OID 35831)
-- Name: IX_ServiceRequests_BuyerUserId_Status; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ServiceRequests_BuyerUserId_Status" ON public."ServiceRequests" USING btree ("BuyerUserId", "Status");


--
-- TOC entry 5713 (class 1259 OID 35554)
-- Name: IX_ServiceRequests_ProviderUserId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ServiceRequests_ProviderUserId" ON public."ServiceRequests" USING btree ("ProviderUserId");


--
-- TOC entry 5714 (class 1259 OID 35832)
-- Name: IX_ServiceRequests_ProviderUserId_Status; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ServiceRequests_ProviderUserId_Status" ON public."ServiceRequests" USING btree ("ProviderUserId", "Status");


--
-- TOC entry 5715 (class 1259 OID 35552)
-- Name: IX_ServiceRequests_ServiceId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ServiceRequests_ServiceId" ON public."ServiceRequests" USING btree ("ServiceId");


--
-- TOC entry 5716 (class 1259 OID 35551)
-- Name: IX_ServiceRequests_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ServiceRequests_TenantId" ON public."ServiceRequests" USING btree ("TenantId");


--
-- TOC entry 5717 (class 1259 OID 35727)
-- Name: IX_ServiceRequests_TenantId_ServiceId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ServiceRequests_TenantId_ServiceId" ON public."ServiceRequests" USING btree ("TenantId", "ServiceId") WHERE ("IsDeleted" = false);


--
-- TOC entry 5720 (class 1259 OID 35605)
-- Name: IX_ServiceReviews_ProviderUserId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ServiceReviews_ProviderUserId" ON public."ServiceReviews" USING btree ("ProviderUserId");


--
-- TOC entry 5721 (class 1259 OID 35606)
-- Name: IX_ServiceReviews_Rating; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ServiceReviews_Rating" ON public."ServiceReviews" USING btree ("Rating") WHERE ("IsDeleted" = false);


--
-- TOC entry 5722 (class 1259 OID 35604)
-- Name: IX_ServiceReviews_ServiceId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ServiceReviews_ServiceId" ON public."ServiceReviews" USING btree ("ServiceId");


--
-- TOC entry 5723 (class 1259 OID 35603)
-- Name: IX_ServiceReviews_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ServiceReviews_TenantId" ON public."ServiceReviews" USING btree ("TenantId");


--
-- TOC entry 5705 (class 1259 OID 35494)
-- Name: IX_Services_Category; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Services_Category" ON public."Services" USING btree ("Category") WHERE (("IsDeleted" = false) AND ("IsActive" = true));


--
-- TOC entry 5706 (class 1259 OID 35493)
-- Name: IX_Services_ProfileId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Services_ProfileId" ON public."Services" USING btree ("ProfileId");


--
-- TOC entry 5707 (class 1259 OID 35806)
-- Name: IX_Services_StartingPrice; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Services_StartingPrice" ON public."Services" USING btree ("StartingPrice") WHERE (("IsDeleted" = false) AND ("IsActive" = true));


--
-- TOC entry 5708 (class 1259 OID 35492)
-- Name: IX_Services_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Services_TenantId" ON public."Services" USING btree ("TenantId");


--
-- TOC entry 5459 (class 1259 OID 34610)
-- Name: IX_Shipments_CarrierCode_TrackingNumber; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Shipments_CarrierCode_TrackingNumber" ON public."Shipments" USING btree ("CarrierCode", "TrackingNumber");


--
-- TOC entry 5460 (class 1259 OID 23677)
-- Name: IX_Shipments_OrderId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Shipments_OrderId" ON public."Shipments" USING btree ("OrderId");


--
-- TOC entry 5461 (class 1259 OID 23678)
-- Name: IX_Shipments_ShippingMethodId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Shipments_ShippingMethodId" ON public."Shipments" USING btree ("ShippingMethodId");


--
-- TOC entry 5462 (class 1259 OID 2287767)
-- Name: IX_Shipments_TenantId_CarrierCode_CarrierRequestId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_Shipments_TenantId_CarrierCode_CarrierRequestId" ON public."Shipments" USING btree ("TenantId", "CarrierCode", "CarrierRequestId") WHERE (("IsDeleted" = false) AND ("CarrierRequestId" IS NOT NULL));


--
-- TOC entry 5463 (class 1259 OID 2287766)
-- Name: IX_Shipments_TenantId_CarrierCode_ExternalReferenceId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_Shipments_TenantId_CarrierCode_ExternalReferenceId" ON public."Shipments" USING btree ("TenantId", "CarrierCode", "ExternalReferenceId") WHERE (("IsDeleted" = false) AND ("ExternalReferenceId" IS NOT NULL));


--
-- TOC entry 5464 (class 1259 OID 34612)
-- Name: IX_Shipments_TenantId_CarrierCode_Status; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Shipments_TenantId_CarrierCode_Status" ON public."Shipments" USING btree ("TenantId", "CarrierCode", "Status");


--
-- TOC entry 5465 (class 1259 OID 34611)
-- Name: IX_Shipments_TenantId_Status_LastCarrierSyncAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Shipments_TenantId_Status_LastCarrierSyncAtUtc" ON public."Shipments" USING btree ("TenantId", "Status", "LastCarrierSyncAtUtc");


--
-- TOC entry 5390 (class 1259 OID 35390)
-- Name: IX_ShippingMethods_FulfillmentMode_CarrierCode; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_ShippingMethods_FulfillmentMode_CarrierCode" ON public."ShippingMethods" USING btree ("FulfillmentMode", "FulfillmentCarrierCode");


--
-- TOC entry 5851 (class 1259 OID 36607)
-- Name: IX_SmartOffers_TenantId_Name; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_SmartOffers_TenantId_Name" ON public."SmartOffers" USING btree ("TenantId", "Name");


--
-- TOC entry 5852 (class 1259 OID 36608)
-- Name: IX_SmartOffers_TenantId_Status_Priority; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SmartOffers_TenantId_Status_Priority" ON public."SmartOffers" USING btree ("TenantId", "Status", "Priority" DESC);


--
-- TOC entry 5853 (class 1259 OID 36609)
-- Name: IX_SmartOffers_TenantId_TriggerType_PagesScope; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SmartOffers_TenantId_TriggerType_PagesScope" ON public."SmartOffers" USING btree ("TenantId", "TriggerType", "PagesScope");


--
-- TOC entry 5273 (class 1259 OID 21176)
-- Name: IX_StockBatches_BatchNumber; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockBatches_BatchNumber" ON public."StockBatches" USING btree ("BatchNumber");


--
-- TOC entry 5274 (class 1259 OID 21177)
-- Name: IX_StockBatches_ExpiryDate; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockBatches_ExpiryDate" ON public."StockBatches" USING btree ("ExpiryDate");


--
-- TOC entry 5275 (class 1259 OID 21178)
-- Name: IX_StockBatches_ProductId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockBatches_ProductId" ON public."StockBatches" USING btree ("ProductId");


--
-- TOC entry 5276 (class 1259 OID 21179)
-- Name: IX_StockBatches_ProductVariantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockBatches_ProductVariantId" ON public."StockBatches" USING btree ("ProductVariantId");


--
-- TOC entry 5277 (class 1259 OID 21180)
-- Name: IX_StockBatches_PurchaseInvoiceId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockBatches_PurchaseInvoiceId" ON public."StockBatches" USING btree ("PurchaseInvoiceId");


--
-- TOC entry 5278 (class 1259 OID 21181)
-- Name: IX_StockBatches_WarehouseId_ProductId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockBatches_WarehouseId_ProductId" ON public."StockBatches" USING btree ("WarehouseId", "ProductId");


--
-- TOC entry 5196 (class 1259 OID 20500)
-- Name: IX_StockMovements_CreatedAt; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockMovements_CreatedAt" ON public."StockMovements" USING btree ("CreatedAt");


--
-- TOC entry 5197 (class 1259 OID 20501)
-- Name: IX_StockMovements_ProductId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockMovements_ProductId" ON public."StockMovements" USING btree ("ProductId");


--
-- TOC entry 5198 (class 1259 OID 20502)
-- Name: IX_StockMovements_ProductVariantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockMovements_ProductVariantId" ON public."StockMovements" USING btree ("ProductVariantId");


--
-- TOC entry 5199 (class 1259 OID 37969)
-- Name: IX_StockMovements_ReferenceLineId_InventoryEventType; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockMovements_ReferenceLineId_InventoryEventType" ON public."StockMovements" USING btree ("ReferenceLineId", "InventoryEventType");


--
-- TOC entry 5200 (class 1259 OID 37968)
-- Name: IX_StockMovements_TenantId_InventoryEventId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockMovements_TenantId_InventoryEventId" ON public."StockMovements" USING btree ("TenantId", "InventoryEventId");


--
-- TOC entry 5201 (class 1259 OID 20503)
-- Name: IX_StockMovements_ToWarehouseId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockMovements_ToWarehouseId" ON public."StockMovements" USING btree ("ToWarehouseId");


--
-- TOC entry 5202 (class 1259 OID 21544)
-- Name: IX_StockMovements_UnitId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockMovements_UnitId" ON public."StockMovements" USING btree ("UnitId");


--
-- TOC entry 5203 (class 1259 OID 20504)
-- Name: IX_StockMovements_WarehouseId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockMovements_WarehouseId" ON public."StockMovements" USING btree ("WarehouseId");


--
-- TOC entry 6285 (class 1259 OID 3022257)
-- Name: IX_StockSyncOutboxMessages_Unpublished_EventOrdering; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockSyncOutboxMessages_Unpublished_EventOrdering" ON public."StockSyncOutboxMessages" USING btree ("TenantId", "VariantId", "WarehouseId", "EventVersion", "StockUpdatedAtUtc", "OccurredAtUtc", "CreatedAt", "Id") WHERE (("PublishedAtUtc" IS NULL) AND ("IsFailed" = false) AND ("IsDeleted" = false));


--
-- TOC entry 6286 (class 1259 OID 2287582)
-- Name: IX_StockSyncOutbox_ClaimStatus; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockSyncOutbox_ClaimStatus" ON public."StockSyncOutboxMessages" USING btree ("ClaimedUntilUtc", "PublishedAtUtc", "IsFailed") WHERE (("PublishedAtUtc" IS NULL) AND ("IsFailed" = false));


--
-- TOC entry 6287 (class 1259 OID 2287581)
-- Name: IX_StockSyncOutbox_Claimable; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockSyncOutbox_Claimable" ON public."StockSyncOutboxMessages" USING btree ("OccurredAtUtc", "ClaimedUntilUtc", "NextRetryAtUtc") WHERE (("PublishedAtUtc" IS NULL) AND ("IsFailed" = false));


--
-- TOC entry 6288 (class 1259 OID 3022525)
-- Name: IX_StockSyncOutbox_GlobalPendingOrdering; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockSyncOutbox_GlobalPendingOrdering" ON public."StockSyncOutboxMessages" USING btree ("EventVersion", "StockUpdatedAtUtc", "OccurredAtUtc", "CreatedAt", "Id") WHERE (("PublishedAtUtc" IS NULL) AND ("IsFailed" = false) AND ("IsDeleted" = false));


--
-- TOC entry 6289 (class 1259 OID 2287577)
-- Name: IX_StockSyncOutbox_OccurredAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockSyncOutbox_OccurredAtUtc" ON public."StockSyncOutboxMessages" USING btree ("OccurredAtUtc");


--
-- TOC entry 6290 (class 1259 OID 3022476)
-- Name: IX_StockSyncOutbox_ProcessingStatus; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockSyncOutbox_ProcessingStatus" ON public."StockSyncOutboxMessages" USING btree ("PublishedAtUtc", "IsFailed", "NextRetryAtUtc", "ClaimedUntilUtc") WHERE (("PublishedAtUtc" IS NULL) AND ("IsFailed" = false));


--
-- TOC entry 6291 (class 1259 OID 2287579)
-- Name: IX_StockSyncOutbox_TenantId_VariantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockSyncOutbox_TenantId_VariantId" ON public."StockSyncOutboxMessages" USING btree ("TenantId", "VariantId");


--
-- TOC entry 6292 (class 1259 OID 3022298)
-- Name: IX_StockSyncOutbox_VariantOrdering; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockSyncOutbox_VariantOrdering" ON public."StockSyncOutboxMessages" USING btree ("TenantId", "VariantId", "PublishedAtUtc", "EventVersion", "StockUpdatedAtUtc", "OccurredAtUtc", "CreatedAt", "Id") WHERE (("PublishedAtUtc" IS NULL) AND ("IsFailed" = false));


--
-- TOC entry 6293 (class 1259 OID 3022299)
-- Name: IX_StockSyncOutbox_VariantOrderingBlockers; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockSyncOutbox_VariantOrderingBlockers" ON public."StockSyncOutboxMessages" USING btree ("TenantId", "VariantId", "EventVersion", "StockUpdatedAtUtc", "OccurredAtUtc", "CreatedAt", "Id") WHERE ("PublishedAtUtc" IS NULL);


--
-- TOC entry 6294 (class 1259 OID 3022256)
-- Name: IX_StockSyncOutbox_Warehouse_EventId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_StockSyncOutbox_Warehouse_EventId" ON public."StockSyncOutboxMessages" USING btree ("TenantId", "WarehouseId", "EventId") WHERE ("IsDeleted" = false);


--
-- TOC entry 5287 (class 1259 OID 21182)
-- Name: IX_StockTakeItems_ProductId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockTakeItems_ProductId" ON public."StockTakeItems" USING btree ("ProductId");


--
-- TOC entry 5288 (class 1259 OID 21183)
-- Name: IX_StockTakeItems_ProductVariantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockTakeItems_ProductVariantId" ON public."StockTakeItems" USING btree ("ProductVariantId");


--
-- TOC entry 5289 (class 1259 OID 21184)
-- Name: IX_StockTakeItems_StockTakeId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockTakeItems_StockTakeId" ON public."StockTakeItems" USING btree ("StockTakeId");


--
-- TOC entry 5290 (class 1259 OID 21185)
-- Name: IX_StockTakeItems_StockTakeId_ProductId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockTakeItems_StockTakeId_ProductId" ON public."StockTakeItems" USING btree ("StockTakeId", "ProductId");


--
-- TOC entry 5281 (class 1259 OID 21186)
-- Name: IX_StockTakes_ReferenceNumber; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_StockTakes_ReferenceNumber" ON public."StockTakes" USING btree ("ReferenceNumber");


--
-- TOC entry 5282 (class 1259 OID 21187)
-- Name: IX_StockTakes_ScheduledDate; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockTakes_ScheduledDate" ON public."StockTakes" USING btree ("ScheduledDate");


--
-- TOC entry 5283 (class 1259 OID 21188)
-- Name: IX_StockTakes_Status; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockTakes_Status" ON public."StockTakes" USING btree ("Status");


--
-- TOC entry 5284 (class 1259 OID 21189)
-- Name: IX_StockTakes_WarehouseId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockTakes_WarehouseId" ON public."StockTakes" USING btree ("WarehouseId");


--
-- TOC entry 5305 (class 1259 OID 21282)
-- Name: IX_StockTransferItems_ProductId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockTransferItems_ProductId" ON public."StockTransferItems" USING btree ("ProductId");


--
-- TOC entry 5306 (class 1259 OID 21283)
-- Name: IX_StockTransferItems_ProductVariantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockTransferItems_ProductVariantId" ON public."StockTransferItems" USING btree ("ProductVariantId");


--
-- TOC entry 5307 (class 1259 OID 21284)
-- Name: IX_StockTransferItems_StockTransferId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockTransferItems_StockTransferId" ON public."StockTransferItems" USING btree ("StockTransferId");


--
-- TOC entry 6358 (class 1259 OID 3022658)
-- Name: IX_StockTransferRequestIdempotency_TenantId_Key_Operation; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_StockTransferRequestIdempotency_TenantId_Key_Operation" ON public."StockTransferRequestIdempotency" USING btree ("TenantId", "Key", "Operation");


--
-- TOC entry 5297 (class 1259 OID 21285)
-- Name: IX_StockTransfers_ApprovedById; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockTransfers_ApprovedById" ON public."StockTransfers" USING btree ("ApprovedById");


--
-- TOC entry 5298 (class 1259 OID 21286)
-- Name: IX_StockTransfers_CompletedById; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockTransfers_CompletedById" ON public."StockTransfers" USING btree ("CompletedById");


--
-- TOC entry 5299 (class 1259 OID 21287)
-- Name: IX_StockTransfers_FromWarehouseId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockTransfers_FromWarehouseId" ON public."StockTransfers" USING btree ("FromWarehouseId");


--
-- TOC entry 5300 (class 1259 OID 21288)
-- Name: IX_StockTransfers_RequestedById; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockTransfers_RequestedById" ON public."StockTransfers" USING btree ("RequestedById");


--
-- TOC entry 5301 (class 1259 OID 55316)
-- Name: IX_StockTransfers_TenantId_TransferNumber; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_StockTransfers_TenantId_TransferNumber" ON public."StockTransfers" USING btree ("TenantId", "TransferNumber") WHERE ("IsDeleted" = false);


--
-- TOC entry 5302 (class 1259 OID 21289)
-- Name: IX_StockTransfers_ToWarehouseId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StockTransfers_ToWarehouseId" ON public."StockTransfers" USING btree ("ToWarehouseId");


--
-- TOC entry 5541 (class 1259 OID 26038)
-- Name: IX_StoreConfigAuditEntries_ActionType; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StoreConfigAuditEntries_ActionType" ON public."StoreConfigAuditEntries" USING btree ("ActionType");


--
-- TOC entry 5542 (class 1259 OID 26036)
-- Name: IX_StoreConfigAuditEntries_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StoreConfigAuditEntries_TenantId" ON public."StoreConfigAuditEntries" USING btree ("TenantId");


--
-- TOC entry 5543 (class 1259 OID 26039)
-- Name: IX_StoreConfigAuditEntries_TenantId_TimestampUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StoreConfigAuditEntries_TenantId_TimestampUtc" ON public."StoreConfigAuditEntries" USING btree ("TenantId", "TimestampUtc" DESC);


--
-- TOC entry 5544 (class 1259 OID 26037)
-- Name: IX_StoreConfigAuditEntries_TimestampUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StoreConfigAuditEntries_TimestampUtc" ON public."StoreConfigAuditEntries" USING btree ("TimestampUtc" DESC);


--
-- TOC entry 5472 (class 1259 OID 23706)
-- Name: IX_StoreConfigs_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_StoreConfigs_TenantId" ON public."StoreConfigs" USING btree ("TenantId");


--
-- TOC entry 5473 (class 1259 OID 23707)
-- Name: IX_StoreConfigs_ThemeId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StoreConfigs_ThemeId" ON public."StoreConfigs" USING btree ("ThemeId");


--
-- TOC entry 6206 (class 1259 OID 46691)
-- Name: IX_StorefrontFooterLinks_SectionId_SortOrder; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StorefrontFooterLinks_SectionId_SortOrder" ON public."StorefrontFooterLinks" USING btree ("SectionId", "SortOrder");


--
-- TOC entry 6203 (class 1259 OID 46690)
-- Name: IX_StorefrontFooterSections_TenantId_SortOrder; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StorefrontFooterSections_TenantId_SortOrder" ON public."StorefrontFooterSections" USING btree ("TenantId", "SortOrder");


--
-- TOC entry 6209 (class 1259 OID 46692)
-- Name: IX_StorefrontSocialLinks_TenantId_SortOrder; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_StorefrontSocialLinks_TenantId_SortOrder" ON public."StorefrontSocialLinks" USING btree ("TenantId", "SortOrder");


--
-- TOC entry 6010 (class 1259 OID 37411)
-- Name: IX_SuperAdmins_Email; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_SuperAdmins_Email" ON public."SuperAdmins" USING btree ("Email");


--
-- TOC entry 6011 (class 1259 OID 37418)
-- Name: IX_SuperAdmins_RefreshTokenHash; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SuperAdmins_RefreshTokenHash" ON public."SuperAdmins" USING btree ("RefreshTokenHash") WHERE ("RefreshTokenHash" IS NOT NULL);


--
-- TOC entry 6012 (class 1259 OID 37412)
-- Name: IX_SuperAdmins_SingleSystemRoot; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_SuperAdmins_SingleSystemRoot" ON public."SuperAdmins" USING btree ("IsSystem") WHERE ("IsSystem" = true);


--
-- TOC entry 6257 (class 1259 OID 46971)
-- Name: IX_SupplierPayments_PurchaseInvoiceId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SupplierPayments_PurchaseInvoiceId" ON public."SupplierPayments" USING btree ("PurchaseInvoiceId");


--
-- TOC entry 6258 (class 1259 OID 46972)
-- Name: IX_SupplierPayments_SupplierId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SupplierPayments_SupplierId" ON public."SupplierPayments" USING btree ("SupplierId");


--
-- TOC entry 6259 (class 1259 OID 46973)
-- Name: IX_SupplierPayments_TenantId_IdempotencyKey; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_SupplierPayments_TenantId_IdempotencyKey" ON public."SupplierPayments" USING btree ("TenantId", "IdempotencyKey");


--
-- TOC entry 6260 (class 1259 OID 46974)
-- Name: IX_SupplierPayments_TenantId_SupplierId_PaymentDate; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SupplierPayments_TenantId_SupplierId_PaymentDate" ON public."SupplierPayments" USING btree ("TenantId", "SupplierId", "PaymentDate");


--
-- TOC entry 5139 (class 1259 OID 55340)
-- Name: IX_SupplierTransactions_OpeningBalance_Once; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_SupplierTransactions_OpeningBalance_Once" ON public."SupplierTransactions" USING btree ("TenantId", "SupplierId", "TransactionType") WHERE (("IsDeleted" = false) AND ("TransactionType" = 11));


--
-- TOC entry 5140 (class 1259 OID 20505)
-- Name: IX_SupplierTransactions_SupplierId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_SupplierTransactions_SupplierId" ON public."SupplierTransactions" USING btree ("SupplierId");


--
-- TOC entry 5109 (class 1259 OID 55313)
-- Name: IX_Suppliers_Email; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Suppliers_Email" ON public."Suppliers" USING btree ("Email");


--
-- TOC entry 5110 (class 1259 OID 55314)
-- Name: IX_Suppliers_Phone; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Suppliers_Phone" ON public."Suppliers" USING btree ("Phone");


--
-- TOC entry 5111 (class 1259 OID 55315)
-- Name: IX_Suppliers_TaxNumber; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Suppliers_TaxNumber" ON public."Suppliers" USING btree ("TaxNumber");


--
-- TOC entry 5112 (class 1259 OID 55319)
-- Name: IX_Suppliers_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Suppliers_TenantId" ON public."Suppliers" USING btree ("TenantId");


--
-- TOC entry 5115 (class 1259 OID 3022600)
-- Name: IX_SystemSettings_TenantId_Key; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_SystemSettings_TenantId_Key" ON public."SystemSettings" USING btree ("TenantId", "Key") WHERE (("TenantId" IS NOT NULL) AND (NOT "IsDeleted"));


--
-- TOC entry 5565 (class 1259 OID 34505)
-- Name: IX_TenantAnalyticsConfigurations_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_TenantAnalyticsConfigurations_TenantId" ON public."TenantAnalyticsConfigurations" USING btree ("TenantId");


--
-- TOC entry 5603 (class 1259 OID 34844)
-- Name: IX_TenantAutomationSettings_EnableAbandonedRecovery; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_TenantAutomationSettings_EnableAbandonedRecovery" ON public."TenantAutomationSettings" USING btree ("EnableAbandonedRecovery") WHERE ("EnableAbandonedRecovery" = true);


--
-- TOC entry 5604 (class 1259 OID 34843)
-- Name: IX_TenantAutomationSettings_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_TenantAutomationSettings_TenantId" ON public."TenantAutomationSettings" USING btree ("TenantId");


--
-- TOC entry 6326 (class 1259 OID 2287839)
-- Name: IX_TenantCurrencyNormalizationAudit_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_TenantCurrencyNormalizationAudit_TenantId" ON public."TenantCurrencyNormalizationAudit" USING btree ("TenantId");


--
-- TOC entry 5526 (class 1259 OID 25873)
-- Name: IX_TenantPaymentMethods_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_TenantPaymentMethods_TenantId" ON public."TenantPaymentMethods" USING btree ("TenantId");


--
-- TOC entry 5527 (class 1259 OID 25874)
-- Name: IX_TenantPaymentMethods_TenantId_Code; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_TenantPaymentMethods_TenantId_Code" ON public."TenantPaymentMethods" USING btree ("TenantId", "Code");


--
-- TOC entry 5528 (class 1259 OID 25875)
-- Name: IX_TenantPaymentMethods_TenantId_IsEnabled; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_TenantPaymentMethods_TenantId_IsEnabled" ON public."TenantPaymentMethods" USING btree ("TenantId", "IsEnabled");


--
-- TOC entry 5529 (class 1259 OID 2287813)
-- Name: IX_TenantPaymentMethods_Version; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_TenantPaymentMethods_Version" ON public."TenantPaymentMethods" USING btree ("Version");


--
-- TOC entry 5446 (class 1259 OID 2287845)
-- Name: IX_Tenants_Currency; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Tenants_Currency" ON public."Tenants" USING btree ("Currency");


--
-- TOC entry 5447 (class 1259 OID 23596)
-- Name: IX_Tenants_IsActive; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Tenants_IsActive" ON public."Tenants" USING btree ("IsActive");


--
-- TOC entry 5448 (class 1259 OID 23709)
-- Name: IX_Tenants_Slug; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_Tenants_Slug" ON public."Tenants" USING btree ("Slug") WHERE ("Slug" IS NOT NULL);


--
-- TOC entry 5449 (class 1259 OID 23746)
-- Name: IX_Tenants_Status; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Tenants_Status" ON public."Tenants" USING btree ("Status");


--
-- TOC entry 5450 (class 1259 OID 34769)
-- Name: IX_Tenants_UseBaileys; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Tenants_UseBaileys" ON public."Tenants" USING btree ("UseBaileys");


--
-- TOC entry 5672 (class 1259 OID 35198)
-- Name: IX_TreasuryAccounts_Tenant; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_TreasuryAccounts_Tenant" ON public."TreasuryAccounts" USING btree ("TenantId");


--
-- TOC entry 5673 (class 1259 OID 35197)
-- Name: IX_TreasuryAccounts_TenantAccount; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_TreasuryAccounts_TenantAccount" ON public."TreasuryAccounts" USING btree ("TenantId", "AccountId");


--
-- TOC entry 5674 (class 1259 OID 35199)
-- Name: IX_TreasuryAccounts_TenantType; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_TreasuryAccounts_TenantType" ON public."TreasuryAccounts" USING btree ("TenantId", "Type");


--
-- TOC entry 5332 (class 1259 OID 21467)
-- Name: IX_TreasuryTransactions_CreatedByUserId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_TreasuryTransactions_CreatedByUserId" ON public."TreasuryTransactions" USING btree ("CreatedByUserId");


--
-- TOC entry 5333 (class 1259 OID 21468)
-- Name: IX_TreasuryTransactions_TransactionDate; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_TreasuryTransactions_TransactionDate" ON public."TreasuryTransactions" USING btree ("TransactionDate");


--
-- TOC entry 6128 (class 1259 OID 38070)
-- Name: IX_UserGovernanceAuditLogs_AdminId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_UserGovernanceAuditLogs_AdminId" ON public."UserGovernanceAuditLogs" USING btree ("AdminId");


--
-- TOC entry 6129 (class 1259 OID 38069)
-- Name: IX_UserGovernanceAuditLogs_TargetRole_TargetUserId_TimestampUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_UserGovernanceAuditLogs_TargetRole_TargetUserId_TimestampUtc" ON public."UserGovernanceAuditLogs" USING btree ("TargetRole", "TargetUserId", "TimestampUtc" DESC);


--
-- TOC entry 6132 (class 1259 OID 38090)
-- Name: IX_UserGovernanceBlacklistEntries_CreatedAtUtc; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_UserGovernanceBlacklistEntries_CreatedAtUtc" ON public."UserGovernanceBlacklistEntries" USING btree ("CreatedAtUtc" DESC);


--
-- TOC entry 6133 (class 1259 OID 38089)
-- Name: IX_UserGovernanceBlacklistEntries_IsActive; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_UserGovernanceBlacklistEntries_IsActive" ON public."UserGovernanceBlacklistEntries" USING btree ("IsActive");


--
-- TOC entry 6123 (class 1259 OID 38052)
-- Name: IX_UserGovernanceProfiles_RiskLevel; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_UserGovernanceProfiles_RiskLevel" ON public."UserGovernanceProfiles" USING btree ("RiskLevel");


--
-- TOC entry 6124 (class 1259 OID 38051)
-- Name: IX_UserGovernanceProfiles_Status; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_UserGovernanceProfiles_Status" ON public."UserGovernanceProfiles" USING btree ("Status");


--
-- TOC entry 5118 (class 1259 OID 20506)
-- Name: IX_Users_Email; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_Users_Email" ON public."Users" USING btree ("Email");


--
-- TOC entry 5119 (class 1259 OID 23745)
-- Name: IX_Users_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_Users_TenantId" ON public."Users" USING btree ("TenantId");


--
-- TOC entry 5120 (class 1259 OID 20507)
-- Name: IX_Users_Username; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_Users_Username" ON public."Users" USING btree ("Username");


--
-- TOC entry 5336 (class 1259 OID 21489)
-- Name: IX_VisaTransactions_CreatedByUserId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_VisaTransactions_CreatedByUserId" ON public."VisaTransactions" USING btree ("CreatedByUserId");


--
-- TOC entry 5337 (class 1259 OID 21492)
-- Name: IX_VisaTransactions_SourceEntityId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_VisaTransactions_SourceEntityId" ON public."VisaTransactions" USING btree ("SourceEntityId");


--
-- TOC entry 5338 (class 1259 OID 21490)
-- Name: IX_VisaTransactions_TransactionDate; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_VisaTransactions_TransactionDate" ON public."VisaTransactions" USING btree ("TransactionDate");


--
-- TOC entry 5339 (class 1259 OID 21491)
-- Name: IX_VisaTransactions_TransactionType; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_VisaTransactions_TransactionType" ON public."VisaTransactions" USING btree ("TransactionType");


--
-- TOC entry 5206 (class 1259 OID 20508)
-- Name: IX_WarehouseStocks_ProductId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_WarehouseStocks_ProductId" ON public."WarehouseStocks" USING btree ("ProductId");


--
-- TOC entry 5207 (class 1259 OID 20509)
-- Name: IX_WarehouseStocks_ProductVariantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_WarehouseStocks_ProductVariantId" ON public."WarehouseStocks" USING btree ("ProductVariantId");


--
-- TOC entry 5208 (class 1259 OID 55331)
-- Name: IX_WarehouseStocks_TenantId_Quantity; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_WarehouseStocks_TenantId_Quantity" ON public."WarehouseStocks" USING btree ("TenantId", "Quantity");


--
-- TOC entry 5209 (class 1259 OID 55332)
-- Name: IX_WarehouseStocks_TenantId_WarehouseId_Quantity; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_WarehouseStocks_TenantId_WarehouseId_Quantity" ON public."WarehouseStocks" USING btree ("TenantId", "WarehouseId", "Quantity");


--
-- TOC entry 5210 (class 1259 OID 20510)
-- Name: IX_WarehouseStocks_WarehouseId_ProductId_ProductVariantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "IX_WarehouseStocks_WarehouseId_ProductId_ProductVariantId" ON public."WarehouseStocks" USING btree ("WarehouseId", "ProductId", "ProductVariantId");


--
-- TOC entry 5636 (class 1259 OID 34975)
-- Name: IX_WhatsAppRateLimits_OptedOut; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_WhatsAppRateLimits_OptedOut" ON public."WhatsAppRateLimits" USING btree ("TenantId") WHERE ("OptedOut" = true);


--
-- TOC entry 5637 (class 1259 OID 34986)
-- Name: IX_WhatsAppRateLimits_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_WhatsAppRateLimits_TenantId" ON public."WhatsAppRateLimits" USING btree ("TenantId");


--
-- TOC entry 5638 (class 1259 OID 34974)
-- Name: IX_WhatsAppRateLimits_TenantId_PhoneNumber; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_WhatsAppRateLimits_TenantId_PhoneNumber" ON public."WhatsAppRateLimits" USING btree ("TenantId", "PhoneNumber");


--
-- TOC entry 5599 (class 1259 OID 34742)
-- Name: IX_whatsapp_message_templates_TenantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX "IX_whatsapp_message_templates_TenantId" ON public.whatsapp_message_templates USING btree ("TenantId");


--
-- TOC entry 5762 (class 1259 OID 36238)
-- Name: UQ_AffiliateAccounts_ReferralCode_Global; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_AffiliateAccounts_ReferralCode_Global" ON public."AffiliateAccounts" USING btree ("ReferralCode");


--
-- TOC entry 5763 (class 1259 OID 36239)
-- Name: UQ_AffiliateAccounts_Tenant_ReferralCode; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_AffiliateAccounts_Tenant_ReferralCode" ON public."AffiliateAccounts" USING btree ("TenantId", "ReferralCode");


--
-- TOC entry 5764 (class 1259 OID 36240)
-- Name: UQ_AffiliateAccounts_Tenant_User; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_AffiliateAccounts_Tenant_User" ON public."AffiliateAccounts" USING btree ("TenantId", "UserId");


--
-- TOC entry 5826 (class 1259 OID 36361)
-- Name: UQ_AffiliateBalanceLedger_Idempotency; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_AffiliateBalanceLedger_Idempotency" ON public."AffiliateBalanceLedger" USING btree ("IdempotencyKey");


--
-- TOC entry 5790 (class 1259 OID 36237)
-- Name: UQ_AffiliateCommLedger_Idempotency; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_AffiliateCommLedger_Idempotency" ON public."AffiliateCommissionLedger" USING btree ("IdempotencyKey");


--
-- TOC entry 5791 (class 1259 OID 36225)
-- Name: UQ_AffiliateCommLedger_Idempotency_Global; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_AffiliateCommLedger_Idempotency_Global" ON public."AffiliateCommissionLedger" USING btree ("IdempotencyKey") WHERE ("IdempotencyKey" IS NOT NULL);


--
-- TOC entry 5771 (class 1259 OID 3022303)
-- Name: UQ_AffiliateProductPolicies_Tenant_Product; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_AffiliateProductPolicies_Tenant_Product" ON public."AffiliateProductPolicies" USING btree ("TenantId", "ProductId") WHERE ("IsDeleted" = false);


--
-- TOC entry 5803 (class 1259 OID 36185)
-- Name: UQ_AffiliatePromotionLinks_ReferralCode; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_AffiliatePromotionLinks_ReferralCode" ON public."AffiliatePromotionLinks" USING btree ("ReferralCode") WHERE ("IsDeleted" = false);


--
-- TOC entry 5804 (class 1259 OID 36184)
-- Name: UQ_AffiliatePromotionLinks_User_Store_Product; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_AffiliatePromotionLinks_User_Store_Product" ON public."AffiliatePromotionLinks" USING btree ("AffiliateUserId", "TenantId", "ProductId") WHERE ("IsDeleted" = false);


--
-- TOC entry 5795 (class 1259 OID 36132)
-- Name: UQ_AffiliateUsers_AffiliateAccount; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_AffiliateUsers_AffiliateAccount" ON public."AffiliateUsers" USING btree ("AffiliateAccountId");


--
-- TOC entry 5796 (class 1259 OID 36133)
-- Name: UQ_AffiliateUsers_Email_Tenant; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_AffiliateUsers_Email_Tenant" ON public."AffiliateUsers" USING btree ("TenantId", "Email");


--
-- TOC entry 6176 (class 1259 OID 38383)
-- Name: UQ_CmsPages_Global_Draft_Slug; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_CmsPages_Global_Draft_Slug" ON public."CmsPages" USING btree ("Slug") WHERE (("TenantId" IS NULL) AND ("IsDraft" = true) AND ("IsDeleted" = false));


--
-- TOC entry 6177 (class 1259 OID 38385)
-- Name: UQ_CmsPages_Global_Published_Slug; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_CmsPages_Global_Published_Slug" ON public."CmsPages" USING btree ("Slug") WHERE (("TenantId" IS NULL) AND ("IsPublished" = true) AND ("IsDeleted" = false));


--
-- TOC entry 6178 (class 1259 OID 38378)
-- Name: UQ_CmsPages_PageGroupId_Draft; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_CmsPages_PageGroupId_Draft" ON public."CmsPages" USING btree ("PageGroupId") WHERE ("IsDraft" = true);


--
-- TOC entry 6179 (class 1259 OID 38379)
-- Name: UQ_CmsPages_PageGroupId_Published; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_CmsPages_PageGroupId_Published" ON public."CmsPages" USING btree ("PageGroupId") WHERE ("IsPublished" = true);


--
-- TOC entry 6180 (class 1259 OID 38377)
-- Name: UQ_CmsPages_PageGroupId_Version; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_CmsPages_PageGroupId_Version" ON public."CmsPages" USING btree ("PageGroupId", "Version");


--
-- TOC entry 6181 (class 1259 OID 38384)
-- Name: UQ_CmsPages_Tenant_Draft_Slug; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_CmsPages_Tenant_Draft_Slug" ON public."CmsPages" USING btree ("TenantId", "Slug") WHERE (("TenantId" IS NOT NULL) AND ("IsDraft" = true) AND ("IsDeleted" = false));


--
-- TOC entry 6182 (class 1259 OID 38386)
-- Name: UQ_CmsPages_Tenant_Published_Slug; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_CmsPages_Tenant_Published_Slug" ON public."CmsPages" USING btree ("TenantId", "Slug") WHERE (("TenantId" IS NOT NULL) AND ("IsPublished" = true) AND ("IsDeleted" = false));


--
-- TOC entry 5885 (class 1259 OID 36750)
-- Name: UQ_CommerceHubCategories_Slug; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_CommerceHubCategories_Slug" ON public."CommerceHubCategories" USING btree ("Slug");


--
-- TOC entry 5907 (class 1259 OID 36834)
-- Name: UQ_CommerceHubListings_TenantId_VariantId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_CommerceHubListings_TenantId_VariantId" ON public."CommerceHubListings" USING btree ("TenantId", "VariantId");


--
-- TOC entry 6164 (class 1259 OID 38328)
-- Name: UQ_HomepageSections_Global_Draft_Order; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_HomepageSections_Global_Draft_Order" ON public."HomepageSections" USING btree ("Order") WHERE (("TenantId" IS NULL) AND ("IsDraft" = true) AND ("IsDeleted" = false));


--
-- TOC entry 6165 (class 1259 OID 38330)
-- Name: UQ_HomepageSections_Global_Published_Order; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_HomepageSections_Global_Published_Order" ON public."HomepageSections" USING btree ("Order") WHERE (("TenantId" IS NULL) AND ("IsPublished" = true) AND ("IsDeleted" = false));


--
-- TOC entry 6166 (class 1259 OID 38323)
-- Name: UQ_HomepageSections_SectionGroupId_Draft; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_HomepageSections_SectionGroupId_Draft" ON public."HomepageSections" USING btree ("SectionGroupId") WHERE ("IsDraft" = true);


--
-- TOC entry 6167 (class 1259 OID 38324)
-- Name: UQ_HomepageSections_SectionGroupId_Published; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_HomepageSections_SectionGroupId_Published" ON public."HomepageSections" USING btree ("SectionGroupId") WHERE ("IsPublished" = true);


--
-- TOC entry 6168 (class 1259 OID 38322)
-- Name: UQ_HomepageSections_SectionGroupId_Version; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_HomepageSections_SectionGroupId_Version" ON public."HomepageSections" USING btree ("SectionGroupId", "Version");


--
-- TOC entry 6169 (class 1259 OID 38329)
-- Name: UQ_HomepageSections_Tenant_Draft_Order; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_HomepageSections_Tenant_Draft_Order" ON public."HomepageSections" USING btree ("TenantId", "Order") WHERE (("TenantId" IS NOT NULL) AND ("IsDraft" = true) AND ("IsDeleted" = false));


--
-- TOC entry 6170 (class 1259 OID 38331)
-- Name: UQ_HomepageSections_Tenant_Published_Order; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_HomepageSections_Tenant_Published_Order" ON public."HomepageSections" USING btree ("TenantId", "Order") WHERE (("TenantId" IS NOT NULL) AND ("IsPublished" = true) AND ("IsDeleted" = false));


--
-- TOC entry 5752 (class 1259 OID 35863)
-- Name: UQ_MarketingReviewEvidences_ServiceReview; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_MarketingReviewEvidences_ServiceReview" ON public."MarketingReviewEvidences" USING btree ("ServiceReviewId");


--
-- TOC entry 6240 (class 1259 OID 46822)
-- Name: UQ_MarketplaceCategoryTranslations_CategoryId_Language; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_MarketplaceCategoryTranslations_CategoryId_Language" ON public."MarketplaceCategoryTranslations" USING btree ("CategoryId", "Language");


--
-- TOC entry 6188 (class 1259 OID 46559)
-- Name: UQ_MarketplaceCmsPages_Draft_Slug; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_MarketplaceCmsPages_Draft_Slug" ON public."MarketplaceCmsPages" USING btree ("Slug") WHERE ("IsDraft" = true);


--
-- TOC entry 6189 (class 1259 OID 46554)
-- Name: UQ_MarketplaceCmsPages_PageGroupId_Draft; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_MarketplaceCmsPages_PageGroupId_Draft" ON public."MarketplaceCmsPages" USING btree ("PageGroupId") WHERE ("IsDraft" = true);


--
-- TOC entry 6190 (class 1259 OID 46555)
-- Name: UQ_MarketplaceCmsPages_PageGroupId_Published; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_MarketplaceCmsPages_PageGroupId_Published" ON public."MarketplaceCmsPages" USING btree ("PageGroupId") WHERE ("IsPublished" = true);


--
-- TOC entry 6191 (class 1259 OID 46553)
-- Name: UQ_MarketplaceCmsPages_PageGroupId_Version; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_MarketplaceCmsPages_PageGroupId_Version" ON public."MarketplaceCmsPages" USING btree ("PageGroupId", "Version");


--
-- TOC entry 6192 (class 1259 OID 46560)
-- Name: UQ_MarketplaceCmsPages_Published_Slug; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_MarketplaceCmsPages_Published_Slug" ON public."MarketplaceCmsPages" USING btree ("Slug") WHERE ("IsPublished" = true);


--
-- TOC entry 6357 (class 1259 OID 3022576)
-- Name: UQ_MarketplaceEventOutboxDeadLetterArchives_OriginalId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_MarketplaceEventOutboxDeadLetterArchives_OriginalId" ON public."MarketplaceEventOutboxDeadLetterArchives" USING btree ("OriginalOutboxMessageId");


--
-- TOC entry 6202 (class 1259 OID 46628)
-- Name: UQ_MarketplaceSocialLinks_Platform; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_MarketplaceSocialLinks_Platform" ON public."MarketplaceSocialLinks" USING btree ("Platform");


--
-- TOC entry 5900 (class 1259 OID 36806)
-- Name: UQ_SearchSynonyms_Term; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_SearchSynonyms_Term" ON public."SearchSynonyms" USING btree ("Term");


--
-- TOC entry 6212 (class 1259 OID 46694)
-- Name: UQ_StorefrontSocialLinks_Global_Platform; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_StorefrontSocialLinks_Global_Platform" ON public."StorefrontSocialLinks" USING btree ("Platform") WHERE ("TenantId" IS NULL);


--
-- TOC entry 6213 (class 1259 OID 46693)
-- Name: UQ_StorefrontSocialLinks_TenantId_Platform; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_StorefrontSocialLinks_TenantId_Platform" ON public."StorefrontSocialLinks" USING btree ("TenantId", "Platform") WHERE ("TenantId" IS NOT NULL);


--
-- TOC entry 5910 (class 1259 OID 36861)
-- Name: UQ_SystemSchemaVersions_Component; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UQ_SystemSchemaVersions_Component" ON public."SystemSchemaVersions" USING btree ("Component");


--
-- TOC entry 5765 (class 1259 OID 2287695)
-- Name: UX_AffiliateAccounts_ReferralCode_Global_CI_Active; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_AffiliateAccounts_ReferralCode_Global_CI_Active" ON public."AffiliateAccounts" USING btree (upper(("ReferralCode")::text)) WHERE ("IsDeleted" = false);


--
-- TOC entry 5805 (class 1259 OID 2287696)
-- Name: UX_AffiliatePromotionLinks_ReferralCode_Global_CI_Active; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_AffiliatePromotionLinks_ReferralCode_Global_CI_Active" ON public."AffiliatePromotionLinks" USING btree (upper(("ReferralCode")::text)) WHERE ("IsDeleted" = false);


--
-- TOC entry 6071 (class 1259 OID 3022287)
-- Name: UX_CHFinancialTx_Fingerprint_Platform; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_CHFinancialTx_Fingerprint_Platform" ON public."CommerceHubFinancialTransactions" USING btree ("ReferenceType", "ReferenceId", "TransactionType", "Direction", "FinancialBucket", "Amount", "Currency") WHERE ("EntityId" IS NULL);


--
-- TOC entry 6072 (class 1259 OID 3022286)
-- Name: UX_CHFinancialTx_Fingerprint_Vendor; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_CHFinancialTx_Fingerprint_Vendor" ON public."CommerceHubFinancialTransactions" USING btree ("ReferenceType", "ReferenceId", "TransactionType", "EntityId", "Direction", "FinancialBucket", "Amount", "Currency") WHERE ("EntityId" IS NOT NULL);


--
-- TOC entry 6315 (class 1259 OID 2287716)
-- Name: UX_CanonicalIdentityLinks_Global_Subject_Active; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_CanonicalIdentityLinks_Global_Subject_Active" ON public."CanonicalIdentityLinks" USING btree ("SubjectType", "SubjectId") WHERE (("TenantId" IS NULL) AND ("IsDeleted" = false) AND ("RevokedAtUtc" IS NULL));


--
-- TOC entry 6316 (class 1259 OID 2287715)
-- Name: UX_CanonicalIdentityLinks_Tenant_Subject_Active; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_CanonicalIdentityLinks_Tenant_Subject_Active" ON public."CanonicalIdentityLinks" USING btree ("TenantId", "SubjectType", "SubjectId") WHERE (("TenantId" IS NOT NULL) AND ("IsDeleted" = false) AND ("RevokedAtUtc" IS NULL));


--
-- TOC entry 5925 (class 1259 OID 3022283)
-- Name: UX_CommerceHubCarts_CommerceHubCustomerId_Active; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_CommerceHubCarts_CommerceHubCustomerId_Active" ON public."CommerceHubCarts" USING btree ("CommerceHubCustomerId") WHERE (("CommerceHubCustomerId" IS NOT NULL) AND ("IsDeleted" = false));


--
-- TOC entry 6116 (class 1259 OID 37994)
-- Name: UX_CommerceHubFinancialGuardLocks_Scope; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_CommerceHubFinancialGuardLocks_Scope" ON public."CommerceHubFinancialGuardLocks" USING btree ("Scope");


--
-- TOC entry 6073 (class 1259 OID 37717)
-- Name: UX_CommerceHubFinancialTransactions_IdempotencyKey; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_CommerceHubFinancialTransactions_IdempotencyKey" ON public."CommerceHubFinancialTransactions" USING btree ("IdempotencyKey");


--
-- TOC entry 5944 (class 1259 OID 3022285)
-- Name: UX_CommerceHubPayments_Provider_ProviderIntentId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_CommerceHubPayments_Provider_ProviderIntentId" ON public."CommerceHubPayments" USING btree ("Provider", "ProviderIntentId") WHERE ("ProviderIntentId" IS NOT NULL);


--
-- TOC entry 5988 (class 1259 OID 2287618)
-- Name: UX_CommerceHubPayouts_Completed_BankTransactionId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_CommerceHubPayouts_Completed_BankTransactionId" ON public."CommerceHubPayouts" USING btree ("TenantId", "BankTransactionId") WHERE (("BankTransactionId" IS NOT NULL) AND (("BankTransactionId")::text <> ''::text) AND ("Status" = 2) AND ("IsDeleted" = false));


--
-- TOC entry 5989 (class 1259 OID 2287617)
-- Name: UX_CommerceHubPayouts_Completed_ExternalReferenceId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_CommerceHubPayouts_Completed_ExternalReferenceId" ON public."CommerceHubPayouts" USING btree ("TenantId", "ExternalReferenceId") WHERE (("ExternalReferenceId" IS NOT NULL) AND (("ExternalReferenceId")::text <> ''::text) AND ("Status" = 2) AND ("IsDeleted" = false));


--
-- TOC entry 5990 (class 1259 OID 3022216)
-- Name: UX_CommerceHubPayouts_ExternalTransferHandoffToken; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_CommerceHubPayouts_ExternalTransferHandoffToken" ON public."CommerceHubPayouts" USING btree ("TenantId", "ExternalTransferHandoffToken") WHERE (("ExternalTransferHandoffToken" IS NOT NULL) AND (("ExternalTransferHandoffToken")::text <> ''::text) AND ("IsDeleted" = false));


--
-- TOC entry 5991 (class 1259 OID 3022035)
-- Name: UX_CommerceHubPayouts_ExternalTransferIdempotencyKey; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_CommerceHubPayouts_ExternalTransferIdempotencyKey" ON public."CommerceHubPayouts" USING btree ("TenantId", "ExternalTransferIdempotencyKey") WHERE (("ExternalTransferIdempotencyKey" IS NOT NULL) AND (("ExternalTransferIdempotencyKey")::text <> ''::text) AND ("IsDeleted" = false));


--
-- TOC entry 5992 (class 1259 OID 3022240)
-- Name: UX_CommerceHubPayouts_Live_BankTransactionId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_CommerceHubPayouts_Live_BankTransactionId" ON public."CommerceHubPayouts" USING btree ("TenantId", "BankTransactionId") WHERE (("BankTransactionId" IS NOT NULL) AND (("BankTransactionId")::text <> ''::text) AND ("Status" <> 3) AND ("IsDeleted" = false));


--
-- TOC entry 5993 (class 1259 OID 3022239)
-- Name: UX_CommerceHubPayouts_Live_ExternalReferenceId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_CommerceHubPayouts_Live_ExternalReferenceId" ON public."CommerceHubPayouts" USING btree ("TenantId", "ExternalReferenceId") WHERE (("ExternalReferenceId" IS NOT NULL) AND (("ExternalReferenceId")::text <> ''::text) AND ("Status" <> 3) AND ("IsDeleted" = false));


--
-- TOC entry 5994 (class 1259 OID 3022383)
-- Name: UX_CommerceHubPayouts_OneProcessingPerTenant; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_CommerceHubPayouts_OneProcessingPerTenant" ON public."CommerceHubPayouts" USING btree ("TenantId") WHERE ((("Status" = 1) OR ("Status" = 4)) AND ("IsDeleted" = false));


--
-- TOC entry 6335 (class 1259 OID 3022167)
-- Name: UX_CommerceHubUnresolvedPaymentWebhooks_Provider_Event; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_CommerceHubUnresolvedPaymentWebhooks_Provider_Event" ON public."CommerceHubUnresolvedPaymentWebhooks" USING btree ("Provider", "ProviderEventId");


--
-- TOC entry 6084 (class 1259 OID 37779)
-- Name: UX_CommerceHubVendorPaymentMethods_Name_DestinationValue; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_CommerceHubVendorPaymentMethods_Name_DestinationValue" ON public."CommerceHubVendorPaymentMethods" USING btree ("Name", "DestinationValue");


--
-- TOC entry 6094 (class 1259 OID 3022231)
-- Name: UX_CommerceHubVendorPaymentProofs_ContentHash; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_CommerceHubVendorPaymentProofs_ContentHash" ON public."CommerceHubVendorPaymentProofs" USING btree ("ContentHash") WHERE (("ContentHash" IS NOT NULL) AND (("ContentHash")::text <> ''::text) AND (("ContentHash")::text <> 'LEGACY_000000000000000000000000000000000000000000000000000000000'::text));


--
-- TOC entry 6095 (class 1259 OID 37836)
-- Name: UX_CommerceHubVendorPaymentProofs_ObjectKey; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_CommerceHubVendorPaymentProofs_ObjectKey" ON public."CommerceHubVendorPaymentProofs" USING btree ("ObjectKey");


--
-- TOC entry 6096 (class 1259 OID 37835)
-- Name: UX_CommerceHubVendorPaymentProofs_PaymentRequestId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_CommerceHubVendorPaymentProofs_PaymentRequestId" ON public."CommerceHubVendorPaymentProofs" USING btree ("PaymentRequestId");


--
-- TOC entry 6090 (class 1259 OID 3022230)
-- Name: UX_CommerceHubVendorPaymentRequests_Live_Method_TransferReferen; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_CommerceHubVendorPaymentRequests_Live_Method_TransferReferen" ON public."CommerceHubVendorPaymentRequests" USING btree ("PaymentMethodId", "TransferReference") WHERE (("TransferReference" IS NOT NULL) AND (("TransferReference")::text <> ''::text) AND ("Status" <> 2));


--
-- TOC entry 6091 (class 1259 OID 37815)
-- Name: UX_CommerceHubVendorPaymentRequests_VendorId_IdempotencyKey; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_CommerceHubVendorPaymentRequests_VendorId_IdempotencyKey" ON public."CommerceHubVendorPaymentRequests" USING btree ("VendorId", "IdempotencyKey");


--
-- TOC entry 6104 (class 1259 OID 37890)
-- Name: UX_CommerceHubVendorPayoutMethods_VendorId_Default; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_CommerceHubVendorPayoutMethods_VendorId_Default" ON public."CommerceHubVendorPayoutMethods" USING btree ("VendorId") WHERE (("IsDeleted" = false) AND ("IsDefault" = true));


--
-- TOC entry 6105 (class 1259 OID 37889)
-- Name: UX_CommerceHubVendorPayoutMethods_VendorId_MethodType_Destinati; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_CommerceHubVendorPayoutMethods_VendorId_MethodType_Destinati" ON public."CommerceHubVendorPayoutMethods" USING btree ("VendorId", "MethodType", "DestinationValue") WHERE ("IsDeleted" = false);


--
-- TOC entry 6079 (class 1259 OID 3022238)
-- Name: UX_CommerceHubVendorSettlements_ExternalReference; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_CommerceHubVendorSettlements_ExternalReference" ON public."CommerceHubVendorSettlements" USING btree ("ExternalReference") WHERE (("ExternalReference" IS NOT NULL) AND (("ExternalReference")::text <> ''::text) AND (("ExternalReference")::text !~~ 'legacy:%'::text));


--
-- TOC entry 6080 (class 1259 OID 3022237)
-- Name: UX_CommerceHubVendorSettlements_Vendor_IdempotencyKey; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_CommerceHubVendorSettlements_Vendor_IdempotencyKey" ON public."CommerceHubVendorSettlements" USING btree ("VendorId", "IdempotencyKey");


--
-- TOC entry 6113 (class 1259 OID 37964)
-- Name: UX_InventoryEventInbox_TenantId_EventId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_InventoryEventInbox_TenantId_EventId" ON public."InventoryEventInbox" USING btree ("TenantId", "EventId");


--
-- TOC entry 6303 (class 1259 OID 2287749)
-- Name: UX_OrderRefunds_TenantId_OrderId_Id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_OrderRefunds_TenantId_OrderId_Id" ON public."OrderRefunds" USING btree ("TenantId", "OrderId", "Id");


--
-- TOC entry 6148 (class 1259 OID 38194)
-- Name: UX_PromoCodeUsages_MarketplaceOrderId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_PromoCodeUsages_MarketplaceOrderId" ON public."PromoCodeUsages" USING btree ("MarketplaceOrderId") WHERE ("IsDeleted" = false);


--
-- TOC entry 6149 (class 1259 OID 38191)
-- Name: UX_PromoCodeUsages_PromoCodeId_MarketplaceOrderId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_PromoCodeUsages_PromoCodeId_MarketplaceOrderId" ON public."PromoCodeUsages" USING btree ("PromoCodeId", "MarketplaceOrderId") WHERE ("IsDeleted" = false);


--
-- TOC entry 6142 (class 1259 OID 38141)
-- Name: UX_PromoCodes_Code; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_PromoCodes_Code" ON public."PromoCodes" USING btree ("Code") WHERE ("IsDeleted" = false);


--
-- TOC entry 6297 (class 1259 OID 3022255)
-- Name: UX_StockSyncOutboxMessages_Tenant_Variant_Warehouse_EventVersio; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_StockSyncOutboxMessages_Tenant_Variant_Warehouse_EventVersio" ON public."StockSyncOutboxMessages" USING btree ("TenantId", "VariantId", "WarehouseId", "EventVersion") WHERE ("IsDeleted" = false);


--
-- TOC entry 6136 (class 1259 OID 38088)
-- Name: UX_UserGovernanceBlacklistEntries_Type_ValueNormalized; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_UserGovernanceBlacklistEntries_Type_ValueNormalized" ON public."UserGovernanceBlacklistEntries" USING btree ("Type", "ValueNormalized");


--
-- TOC entry 6127 (class 1259 OID 38050)
-- Name: UX_UserGovernanceProfiles_Role_SubjectId; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX "UX_UserGovernanceProfiles_Role_SubjectId" ON public."UserGovernanceProfiles" USING btree ("Role", "SubjectId");


--
-- TOC entry 6230 (class 1259 OID 2287549)
-- Name: ix_processedevents_idempotency; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX ix_processedevents_idempotency ON public."ProcessedEvents" USING btree ("TenantId", "EventId", "ConsumerName", "EventType") WHERE ("IsDeleted" = false);


--
-- TOC entry 5594 (class 1259 OID 34847)
-- Name: ix_whatsapp_accounts_abandoned_cart_recovery; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX ix_whatsapp_accounts_abandoned_cart_recovery ON public.whatsapp_accounts USING btree (tenant_id, use_for_abandoned_cart_recovery, enabled, status) WHERE ((use_for_abandoned_cart_recovery = true) AND (enabled = true));


--
-- TOC entry 5595 (class 1259 OID 34779)
-- Name: ix_whatsapp_accounts_payment_approval; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX ix_whatsapp_accounts_payment_approval ON public.whatsapp_accounts USING btree (tenant_id, use_for_payment_approval, enabled) WHERE ((use_for_payment_approval = true) AND (enabled = true));


--
-- TOC entry 5596 (class 1259 OID 34714)
-- Name: ix_whatsapp_accounts_status; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX ix_whatsapp_accounts_status ON public.whatsapp_accounts USING btree (status) WHERE (enabled = true);


--
-- TOC entry 5597 (class 1259 OID 34715)
-- Name: ix_whatsapp_accounts_tenant_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX ix_whatsapp_accounts_tenant_id ON public.whatsapp_accounts USING btree (tenant_id);


--
-- TOC entry 5598 (class 1259 OID 34716)
-- Name: uq_whatsapp_account_phone_tenant; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX uq_whatsapp_account_phone_tenant ON public.whatsapp_accounts USING btree (tenant_id, phone_number);


--
-- TOC entry 5602 (class 1259 OID 34752)
-- Name: uq_whatsapp_template_tenant_type_language; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX uq_whatsapp_template_tenant_type_language ON public.whatsapp_message_templates USING btree ("TenantId", "Type", "Language");


--
-- TOC entry 6618 (class 2620 OID 37417)
-- Name: SuperAdmins TRG_SuperAdmins_EnforcePresence_Delete; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER "TRG_SuperAdmins_EnforcePresence_Delete" BEFORE DELETE ON public."SuperAdmins" FOR EACH ROW EXECUTE FUNCTION public.enforce_super_admin_presence();


--
-- TOC entry 6619 (class 2620 OID 37423)
-- Name: SuperAdmins TRG_SuperAdmins_ProtectSystemRoot_Delete; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER "TRG_SuperAdmins_ProtectSystemRoot_Delete" BEFORE DELETE ON public."SuperAdmins" FOR EACH ROW EXECUTE FUNCTION public.protect_system_super_admin_mutation();


--
-- TOC entry 6620 (class 2620 OID 37422)
-- Name: SuperAdmins TRG_SuperAdmins_ProtectSystemRoot_Update; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER "TRG_SuperAdmins_ProtectSystemRoot_Update" BEFORE UPDATE ON public."SuperAdmins" FOR EACH ROW EXECUTE FUNCTION public.protect_system_super_admin_mutation();


--
-- TOC entry 6612 (class 2620 OID 35801)
-- Name: ServiceRequestMessages audit_message_trigger; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER audit_message_trigger AFTER INSERT ON public."ServiceRequestMessages" FOR EACH ROW EXECUTE FUNCTION public.audit_message_insert();


--
-- TOC entry 6613 (class 2620 OID 35802)
-- Name: ServiceRequestMessages prevent_message_modification_trigger; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER prevent_message_modification_trigger BEFORE UPDATE ON public."ServiceRequestMessages" FOR EACH ROW EXECUTE FUNCTION public.prevent_message_modification();


--
-- TOC entry 6597 (class 2620 OID 35347)
-- Name: CashboxTransactions tr_block_legacy_cashbox_insert; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER tr_block_legacy_cashbox_insert BEFORE INSERT ON public."CashboxTransactions" FOR EACH ROW EXECUTE FUNCTION public.block_legacy_cashbox_insert();


--
-- TOC entry 6602 (class 2620 OID 35348)
-- Name: VisaTransactions tr_block_legacy_visa_insert; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER tr_block_legacy_visa_insert BEFORE INSERT ON public."VisaTransactions" FOR EACH ROW EXECUTE FUNCTION public.block_legacy_visa_insert();


--
-- TOC entry 6616 (class 2620 OID 36226)
-- Name: AffiliateCommissionLedger trg_affiliate_commission_ledger_immutable; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER trg_affiliate_commission_ledger_immutable BEFORE DELETE OR UPDATE ON public."AffiliateCommissionLedger" FOR EACH ROW EXECUTE FUNCTION public.fn_affiliate_commission_ledger_immutable();


--
-- TOC entry 6598 (class 2620 OID 35350)
-- Name: CashboxTransactions trg_block_cashbox_insert; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER trg_block_cashbox_insert BEFORE INSERT ON public."CashboxTransactions" FOR EACH ROW EXECUTE FUNCTION public.tr_block_cashbox_transactions_insert();


--
-- TOC entry 6599 (class 2620 OID 35352)
-- Name: CashboxTransactions trg_block_cashbox_update; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER trg_block_cashbox_update BEFORE UPDATE ON public."CashboxTransactions" FOR EACH ROW EXECUTE FUNCTION public.tr_block_cashbox_transactions_update();


--
-- TOC entry 6600 (class 2620 OID 35358)
-- Name: TreasuryTransactions trg_block_treasury_insert; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER trg_block_treasury_insert BEFORE INSERT ON public."TreasuryTransactions" FOR EACH ROW EXECUTE FUNCTION public.tr_block_treasury_transactions_insert();


--
-- TOC entry 6601 (class 2620 OID 35360)
-- Name: TreasuryTransactions trg_block_treasury_update; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER trg_block_treasury_update BEFORE UPDATE ON public."TreasuryTransactions" FOR EACH ROW EXECUTE FUNCTION public.tr_block_treasury_transactions_update();


--
-- TOC entry 6603 (class 2620 OID 35354)
-- Name: VisaTransactions trg_block_visa_insert; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER trg_block_visa_insert BEFORE INSERT ON public."VisaTransactions" FOR EACH ROW EXECUTE FUNCTION public.tr_block_visa_transactions_insert();


--
-- TOC entry 6604 (class 2620 OID 35356)
-- Name: VisaTransactions trg_block_visa_update; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER trg_block_visa_update BEFORE UPDATE ON public."VisaTransactions" FOR EACH ROW EXECUTE FUNCTION public.tr_block_visa_transactions_update();


--
-- TOC entry 6624 (class 2620 OID 38029)
-- Name: CommerceHubFinancialOperationAudits trg_commercehub_financial_operation_audits_immutable; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER trg_commercehub_financial_operation_audits_immutable BEFORE DELETE OR UPDATE ON public."CommerceHubFinancialOperationAudits" FOR EACH ROW EXECUTE FUNCTION public.fn_commercehub_financial_operation_audits_immutable();


--
-- TOC entry 6626 (class 2620 OID 38272)
-- Name: CommerceHubFinancialReconciliationReports trg_commercehub_financial_reconciliation_reports_immutable; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER trg_commercehub_financial_reconciliation_reports_immutable BEFORE DELETE OR UPDATE ON public."CommerceHubFinancialReconciliationReports" FOR EACH ROW EXECUTE FUNCTION public.fn_commercehub_financial_reconciliation_reports_immutable();


--
-- TOC entry 6621 (class 2620 OID 37870)
-- Name: CommerceHubFinancialTransactions trg_commercehub_ledger_prevent_delete; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER trg_commercehub_ledger_prevent_delete BEFORE DELETE ON public."CommerceHubFinancialTransactions" FOR EACH ROW EXECUTE FUNCTION public.fn_commercehub_ledger_prevent_delete();


--
-- TOC entry 6622 (class 2620 OID 37869)
-- Name: CommerceHubFinancialTransactions trg_commercehub_ledger_prevent_update; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER trg_commercehub_ledger_prevent_update BEFORE UPDATE ON public."CommerceHubFinancialTransactions" FOR EACH ROW EXECUTE FUNCTION public.fn_commercehub_ledger_prevent_update();


--
-- TOC entry 6623 (class 2620 OID 37754)
-- Name: CommerceHubVendorSettlements trg_commercehub_vendor_settlements_immutable; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER trg_commercehub_vendor_settlements_immutable BEFORE DELETE OR UPDATE ON public."CommerceHubVendorSettlements" FOR EACH ROW EXECUTE FUNCTION public.fn_commercehub_vendor_settlements_immutable();


--
-- TOC entry 6611 (class 2620 OID 35608)
-- Name: ServiceReviews trg_enforce_review_invariant; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER trg_enforce_review_invariant BEFORE INSERT ON public."ServiceReviews" FOR EACH ROW EXECUTE FUNCTION public.enforce_review_requires_completed_request();


--
-- TOC entry 6608 (class 2620 OID 35132)
-- Name: LedgerJournalEntries trg_journal_immutable; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER trg_journal_immutable BEFORE DELETE OR UPDATE ON public."LedgerJournalEntries" FOR EACH ROW EXECUTE FUNCTION public.prevent_ledger_journal_modification();


--
-- TOC entry 6609 (class 2620 OID 35133)
-- Name: LedgerLines trg_line_immutable; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER trg_line_immutable BEFORE DELETE OR UPDATE ON public."LedgerLines" FOR EACH ROW EXECUTE FUNCTION public.prevent_ledger_line_modification();


--
-- TOC entry 6617 (class 2620 OID 36373)
-- Name: AffiliateBalanceLedger trg_prevent_balance_ledger_mutation; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER trg_prevent_balance_ledger_mutation BEFORE DELETE OR UPDATE ON public."AffiliateBalanceLedger" FOR EACH ROW EXECUTE FUNCTION public.prevent_balance_ledger_mutation();


--
-- TOC entry 6605 (class 2620 OID 35424)
-- Name: Orders trg_prevent_fulfillment_mode_update; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER trg_prevent_fulfillment_mode_update BEFORE UPDATE ON public."Orders" FOR EACH ROW EXECUTE FUNCTION public.prevent_fulfillment_mode_update();


--
-- TOC entry 6862 (class 0 OID 0)
-- Dependencies: 6605
-- Name: TRIGGER trg_prevent_fulfillment_mode_update ON "Orders"; Type: COMMENT; Schema: public; Owner: postgres
--

COMMENT ON TRIGGER trg_prevent_fulfillment_mode_update ON public."Orders" IS 'Prevents any updates to FulfillmentMode or FulfillmentCarrierCode after order creation.';


--
-- TOC entry 6615 (class 2620 OID 35719)
-- Name: ProviderTrustAggregates trg_prevent_manual_trust_modification; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER trg_prevent_manual_trust_modification BEFORE UPDATE ON public."ProviderTrustAggregates" FOR EACH ROW EXECUTE FUNCTION public.prevent_manual_trust_modification();


--
-- TOC entry 6614 (class 2620 OID 35665)
-- Name: ServiceRequestMessages trg_prevent_message_modification; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER trg_prevent_message_modification BEFORE UPDATE ON public."ServiceRequestMessages" FOR EACH ROW EXECUTE FUNCTION public.prevent_message_modification();


--
-- TOC entry 6606 (class 2620 OID 35201)
-- Name: LedgerAccounts trg_prevent_system_account_delete; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER trg_prevent_system_account_delete BEFORE DELETE ON public."LedgerAccounts" FOR EACH ROW EXECUTE FUNCTION public.prevent_system_account_deletion();


--
-- TOC entry 6607 (class 2620 OID 35203)
-- Name: LedgerAccounts trg_prevent_system_account_modify; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER trg_prevent_system_account_modify BEFORE UPDATE ON public."LedgerAccounts" FOR EACH ROW EXECUTE FUNCTION public.prevent_system_account_modification();


--
-- TOC entry 6610 (class 2620 OID 35205)
-- Name: PaymentMethods trg_prevent_system_pm_delete; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER trg_prevent_system_pm_delete BEFORE DELETE ON public."PaymentMethods" FOR EACH ROW EXECUTE FUNCTION public.prevent_system_payment_method_deletion();


--
-- TOC entry 6625 (class 2620 OID 38091)
-- Name: UserGovernanceAuditLogs trg_user_governance_audit_logs_immutable; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER trg_user_governance_audit_logs_immutable BEFORE DELETE OR UPDATE ON public."UserGovernanceAuditLogs" FOR EACH ROW EXECUTE FUNCTION public.fn_user_governance_audit_logs_immutable();


--
-- TOC entry 6410 (class 2606 OID 20776)
-- Name: jobparameter jobparameter_jobid_fkey; Type: FK CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.jobparameter
    ADD CONSTRAINT jobparameter_jobid_fkey FOREIGN KEY (jobid) REFERENCES hangfire.job(id) ON UPDATE CASCADE ON DELETE CASCADE;


--
-- TOC entry 6409 (class 2606 OID 20751)
-- Name: state state_jobid_fkey; Type: FK CONSTRAINT; Schema: hangfire; Owner: postgres
--

ALTER TABLE ONLY hangfire.state
    ADD CONSTRAINT state_jobid_fkey FOREIGN KEY (jobid) REFERENCES hangfire.job(id) ON UPDATE CASCADE ON DELETE CASCADE;


--
-- TOC entry 6487 (class 2606 OID 34936)
-- Name: AbandonedCartRecoveryLogs FK_AbandonedCartRecoveryLogs_AbandonedCarts; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AbandonedCartRecoveryLogs"
    ADD CONSTRAINT "FK_AbandonedCartRecoveryLogs_AbandonedCarts" FOREIGN KEY ("AbandonedCartId") REFERENCES public."AbandonedCarts"("Id") ON DELETE CASCADE;


--
-- TOC entry 6488 (class 2606 OID 34941)
-- Name: AbandonedCartRecoveryLogs FK_AbandonedCartRecoveryLogs_Tenants; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AbandonedCartRecoveryLogs"
    ADD CONSTRAINT "FK_AbandonedCartRecoveryLogs_Tenants" FOREIGN KEY ("TenantId") REFERENCES public."Tenants"("Id") ON DELETE CASCADE;


--
-- TOC entry 6484 (class 2606 OID 34878)
-- Name: AbandonedCarts FK_AbandonedCarts_OnlineCustomers_CustomerId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AbandonedCarts"
    ADD CONSTRAINT "FK_AbandonedCarts_OnlineCustomers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES public."OnlineCustomers"("Id") ON DELETE SET NULL;


--
-- TOC entry 6485 (class 2606 OID 34883)
-- Name: AbandonedCarts FK_AbandonedCarts_Orders_RecoveredOrderId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AbandonedCarts"
    ADD CONSTRAINT "FK_AbandonedCarts_Orders_RecoveredOrderId" FOREIGN KEY ("RecoveredOrderId") REFERENCES public."Orders"("Id") ON DELETE SET NULL;


--
-- TOC entry 6486 (class 2606 OID 34873)
-- Name: AbandonedCarts FK_AbandonedCarts_Tenants_TenantId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AbandonedCarts"
    ADD CONSTRAINT "FK_AbandonedCarts_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES public."Tenants"("Id") ON DELETE CASCADE;


--
-- TOC entry 6445 (class 2606 OID 23247)
-- Name: AccountBalances FK_AccountBalances_Accounts_AccountId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AccountBalances"
    ADD CONSTRAINT "FK_AccountBalances_Accounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES public."Accounts"("AccountId") ON DELETE CASCADE;


--
-- TOC entry 6423 (class 2606 OID 21208)
-- Name: AccountLedgers FK_AccountLedgers_Users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AccountLedgers"
    ADD CONSTRAINT "FK_AccountLedgers_Users_UserId" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id");


--
-- TOC entry 6446 (class 2606 OID 23265)
-- Name: AccountMappings FK_AccountMappings_Accounts_AccountId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AccountMappings"
    ADD CONSTRAINT "FK_AccountMappings_Accounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES public."Accounts"("AccountId") ON DELETE RESTRICT;


--
-- TOC entry 6443 (class 2606 OID 23203)
-- Name: Accounts FK_Accounts_Accounts_ParentAccountId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Accounts"
    ADD CONSTRAINT "FK_Accounts_Accounts_ParentAccountId" FOREIGN KEY ("ParentAccountId") REFERENCES public."Accounts"("AccountId") ON DELETE RESTRICT;


--
-- TOC entry 6523 (class 2606 OID 36020)
-- Name: AffiliateAttributions FK_AffiliateAttributions_AffiliateAccounts; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliateAttributions"
    ADD CONSTRAINT "FK_AffiliateAttributions_AffiliateAccounts" FOREIGN KEY ("AffiliateAccountId") REFERENCES public."AffiliateAccounts"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6524 (class 2606 OID 36015)
-- Name: AffiliateAttributions FK_AffiliateAttributions_Orders; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliateAttributions"
    ADD CONSTRAINT "FK_AffiliateAttributions_Orders" FOREIGN KEY ("OrderId") REFERENCES public."Orders"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6525 (class 2606 OID 36010)
-- Name: AffiliateAttributions FK_AffiliateAttributions_Tenants; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliateAttributions"
    ADD CONSTRAINT "FK_AffiliateAttributions_Tenants" FOREIGN KEY ("TenantId") REFERENCES public."Tenants"("Id");


--
-- TOC entry 6536 (class 2606 OID 36356)
-- Name: AffiliateBalanceLedger FK_AffiliateBalanceLedger_AffiliateAccounts; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliateBalanceLedger"
    ADD CONSTRAINT "FK_AffiliateBalanceLedger_AffiliateAccounts" FOREIGN KEY ("AffiliateAccountId") REFERENCES public."AffiliateAccounts"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6535 (class 2606 OID 36283)
-- Name: AffiliateCategories FK_AffiliateCategories_Parent; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliateCategories"
    ADD CONSTRAINT "FK_AffiliateCategories_Parent" FOREIGN KEY ("ParentId") REFERENCES public."AffiliateCategories"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6526 (class 2606 OID 36067)
-- Name: AffiliateCommissionLedger FK_AffiliateCommLedger_AffiliateAccounts; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliateCommissionLedger"
    ADD CONSTRAINT "FK_AffiliateCommLedger_AffiliateAccounts" FOREIGN KEY ("AffiliateAccountId") REFERENCES public."AffiliateAccounts"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6527 (class 2606 OID 36062)
-- Name: AffiliateCommissionLedger FK_AffiliateCommLedger_Attributions; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliateCommissionLedger"
    ADD CONSTRAINT "FK_AffiliateCommLedger_Attributions" FOREIGN KEY ("AttributionId") REFERENCES public."AffiliateAttributions"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6528 (class 2606 OID 36077)
-- Name: AffiliateCommissionLedger FK_AffiliateCommLedger_OrderItems; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliateCommissionLedger"
    ADD CONSTRAINT "FK_AffiliateCommLedger_OrderItems" FOREIGN KEY ("OrderItemId") REFERENCES public."OrderItems"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6529 (class 2606 OID 36072)
-- Name: AffiliateCommissionLedger FK_AffiliateCommLedger_Orders; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliateCommissionLedger"
    ADD CONSTRAINT "FK_AffiliateCommLedger_Orders" FOREIGN KEY ("OrderId") REFERENCES public."Orders"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6530 (class 2606 OID 36082)
-- Name: AffiliateCommissionLedger FK_AffiliateCommLedger_OriginalEntry; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliateCommissionLedger"
    ADD CONSTRAINT "FK_AffiliateCommLedger_OriginalEntry" FOREIGN KEY ("OriginalEntryId") REFERENCES public."AffiliateCommissionLedger"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6531 (class 2606 OID 36057)
-- Name: AffiliateCommissionLedger FK_AffiliateCommLedger_Tenants; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliateCommissionLedger"
    ADD CONSTRAINT "FK_AffiliateCommLedger_Tenants" FOREIGN KEY ("TenantId") REFERENCES public."Tenants"("Id");


--
-- TOC entry 6520 (class 2606 OID 36291)
-- Name: AffiliateProductPolicies FK_AffiliateProductPolicies_Category; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliateProductPolicies"
    ADD CONSTRAINT "FK_AffiliateProductPolicies_Category" FOREIGN KEY ("AffiliateCategoryId") REFERENCES public."AffiliateCategories"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6521 (class 2606 OID 35982)
-- Name: AffiliateProductPolicies FK_AffiliateProductPolicies_Products; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliateProductPolicies"
    ADD CONSTRAINT "FK_AffiliateProductPolicies_Products" FOREIGN KEY ("ProductId") REFERENCES public."Products"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6522 (class 2606 OID 35977)
-- Name: AffiliateProductPolicies FK_AffiliateProductPolicies_Tenants; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliateProductPolicies"
    ADD CONSTRAINT "FK_AffiliateProductPolicies_Tenants" FOREIGN KEY ("TenantId") REFERENCES public."Tenants"("Id");


--
-- TOC entry 6533 (class 2606 OID 36190)
-- Name: AffiliatePromotionLinks FK_AffiliatePromotionLinks_AffiliateUsers; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliatePromotionLinks"
    ADD CONSTRAINT "FK_AffiliatePromotionLinks_AffiliateUsers" FOREIGN KEY ("AffiliateUserId") REFERENCES public."AffiliateUsers"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6534 (class 2606 OID 36195)
-- Name: AffiliatePromotionLinks FK_AffiliatePromotionLinks_Products; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliatePromotionLinks"
    ADD CONSTRAINT "FK_AffiliatePromotionLinks_Products" FOREIGN KEY ("ProductId") REFERENCES public."Products"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6532 (class 2606 OID 36120)
-- Name: AffiliateUsers FK_AffiliateUsers_AffiliateAccounts_AffiliateAccountId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."AffiliateUsers"
    ADD CONSTRAINT "FK_AffiliateUsers_AffiliateAccounts_AffiliateAccountId" FOREIGN KEY ("AffiliateAccountId") REFERENCES public."AffiliateAccounts"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6471 (class 2606 OID 25800)
-- Name: OnlineCustomerAuthProviders FK_AuthProviders_OnlineCustomer; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."OnlineCustomerAuthProviders"
    ADD CONSTRAINT "FK_AuthProviders_OnlineCustomer" FOREIGN KEY ("OnlineCustomerId") REFERENCES public."OnlineCustomers"("Id") ON DELETE CASCADE;


--
-- TOC entry 6434 (class 2606 OID 21385)
-- Name: BankTransactions FK_BankTransactions_Banks_BankId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."BankTransactions"
    ADD CONSTRAINT "FK_BankTransactions_Banks_BankId" FOREIGN KEY ("BankId") REFERENCES public."Banks"("Id") ON DELETE CASCADE;


--
-- TOC entry 6413 (class 2606 OID 21063)
-- Name: CashboxTransactions FK_CashboxTransactions_Cashboxes_CashboxId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CashboxTransactions"
    ADD CONSTRAINT "FK_CashboxTransactions_Cashboxes_CashboxId" FOREIGN KEY ("CashboxId") REFERENCES public."Cashboxes"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6414 (class 2606 OID 21068)
-- Name: CashboxTransactions FK_CashboxTransactions_Users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CashboxTransactions"
    ADD CONSTRAINT "FK_CashboxTransactions_Users_UserId" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6411 (class 2606 OID 21037)
-- Name: Cashboxes FK_Cashboxes_Users_OpenedByUserId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Cashboxes"
    ADD CONSTRAINT "FK_Cashboxes_Users_OpenedByUserId" FOREIGN KEY ("OpenedByUserId") REFERENCES public."Users"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6412 (class 2606 OID 21042)
-- Name: Cashboxes FK_Cashboxes_Warehouses_WarehouseId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Cashboxes"
    ADD CONSTRAINT "FK_Cashboxes_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES public."Warehouses"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6372 (class 2606 OID 20023)
-- Name: Categories FK_Categories_Categories_ParentCategoryId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Categories"
    ADD CONSTRAINT "FK_Categories_Categories_ParentCategoryId" FOREIGN KEY ("ParentCategoryId") REFERENCES public."Categories"("Id");


--
-- TOC entry 6518 (class 2606 OID 35789)
-- Name: ChatAuditLog FK_ChatAuditLog_Messages; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ChatAuditLog"
    ADD CONSTRAINT "FK_ChatAuditLog_Messages" FOREIGN KEY ("MessageId") REFERENCES public."ServiceRequestMessages"("Id") ON DELETE CASCADE;


--
-- TOC entry 6537 (class 2606 OID 36403)
-- Name: CheckoutSessions FK_CheckoutSessions_Tenants_TenantId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CheckoutSessions"
    ADD CONSTRAINT "FK_CheckoutSessions_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES public."Tenants"("Id") ON DELETE CASCADE;


--
-- TOC entry 6498 (class 2606 OID 35312)
-- Name: CommerceAccountingAudit FK_CommerceAudit_Journal; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceAccountingAudit"
    ADD CONSTRAINT "FK_CommerceAudit_Journal" FOREIGN KEY ("JournalId") REFERENCES public."LedgerJournalEntries"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6543 (class 2606 OID 36724)
-- Name: CommerceHubCategories FK_CommerceHubCategories_CommerceHubCategories_ParentId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubCategories"
    ADD CONSTRAINT "FK_CommerceHubCategories_CommerceHubCategories_ParentId" FOREIGN KEY ("ParentId") REFERENCES public."CommerceHubCategories"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6544 (class 2606 OID 36743)
-- Name: CommerceHubCategoryAttributes FK_CommerceHubCategoryAttr_CommerceHubCat_CategoryId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubCategoryAttributes"
    ADD CONSTRAINT "FK_CommerceHubCategoryAttr_CommerceHubCat_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES public."CommerceHubCategories"("Id") ON DELETE CASCADE;


--
-- TOC entry 6545 (class 2606 OID 36864)
-- Name: CommerceHubCategoryAttributes FK_CommerceHubCategoryAttributes_CommerceHubCategories_Category; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubCategoryAttributes"
    ADD CONSTRAINT "FK_CommerceHubCategoryAttributes_CommerceHubCategories_Category" FOREIGN KEY ("CategoryId") REFERENCES public."CommerceHubCategories"("Id") ON DELETE CASCADE;


--
-- TOC entry 6571 (class 2606 OID 37599)
-- Name: CommerceHubCustomerPayoutDetails FK_CommerceHubCustomerPayoutDetails_CommerceHubCustomers_Custom; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubCustomerPayoutDetails"
    ADD CONSTRAINT "FK_CommerceHubCustomerPayoutDetails_CommerceHubCustomers_Custom" FOREIGN KEY ("CustomerId") REFERENCES public."CommerceHubCustomers"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6546 (class 2606 OID 36840)
-- Name: CommerceHubListings FK_CommerceHubListings_ProductVariants_VariantId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubListings"
    ADD CONSTRAINT "FK_CommerceHubListings_ProductVariants_VariantId" FOREIGN KEY ("VariantId") REFERENCES public."ProductVariants"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6547 (class 2606 OID 36835)
-- Name: CommerceHubListings FK_CommerceHubListings_Products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubListings"
    ADD CONSTRAINT "FK_CommerceHubListings_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public."Products"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6540 (class 2606 OID 37468)
-- Name: CommerceHubMarketplaceOrders FK_CommerceHubMarketplaceOrders_CommerceHubCustomers_CustomerId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubMarketplaceOrders"
    ADD CONSTRAINT "FK_CommerceHubMarketplaceOrders_CommerceHubCustomers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES public."CommerceHubCustomers"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6558 (class 2606 OID 37216)
-- Name: CommerceHubPaymentAttempts FK_CommerceHubPaymentAttempts_CommerceHubPayments_PaymentId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubPaymentAttempts"
    ADD CONSTRAINT "FK_CommerceHubPaymentAttempts_CommerceHubPayments_PaymentId" FOREIGN KEY ("PaymentId") REFERENCES public."CommerceHubPayments"("Id") ON DELETE CASCADE;


--
-- TOC entry 6550 (class 2606 OID 37167)
-- Name: CommerceHubPaymentItems FK_CommerceHubPaymentItems_CommerceHubPayments_PaymentId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubPaymentItems"
    ADD CONSTRAINT "FK_CommerceHubPaymentItems_CommerceHubPayments_PaymentId" FOREIGN KEY ("PaymentId") REFERENCES public."CommerceHubPayments"("Id") ON DELETE CASCADE;


--
-- TOC entry 6551 (class 2606 OID 37172)
-- Name: CommerceHubPaymentItems FK_CommerceHubPaymentItems_CommerceHubSubOrders_SubOrderId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubPaymentItems"
    ADD CONSTRAINT "FK_CommerceHubPaymentItems_CommerceHubSubOrders_SubOrderId" FOREIGN KEY ("SubOrderId") REFERENCES public."CommerceHubSubOrders"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6552 (class 2606 OID 37177)
-- Name: CommerceHubPaymentLogs FK_CommerceHubPaymentLogs_CommerceHubPayments_PaymentId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubPaymentLogs"
    ADD CONSTRAINT "FK_CommerceHubPaymentLogs_CommerceHubPayments_PaymentId" FOREIGN KEY ("PaymentId") REFERENCES public."CommerceHubPayments"("Id") ON DELETE CASCADE;


--
-- TOC entry 6549 (class 2606 OID 37162)
-- Name: CommerceHubPayments FK_CommerceHubPayments_CommerceHubMarketplaceOrders_OrderId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubPayments"
    ADD CONSTRAINT "FK_CommerceHubPayments_CommerceHubMarketplaceOrders_OrderId" FOREIGN KEY ("OrderId") REFERENCES public."CommerceHubMarketplaceOrders"("Id") ON DELETE CASCADE;


--
-- TOC entry 6560 (class 2606 OID 37330)
-- Name: CommerceHubPayoutItems FK_CommerceHubPayoutItems_CommerceHubPayouts_PayoutId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubPayoutItems"
    ADD CONSTRAINT "FK_CommerceHubPayoutItems_CommerceHubPayouts_PayoutId" FOREIGN KEY ("PayoutId") REFERENCES public."CommerceHubPayouts"("Id") ON DELETE CASCADE;


--
-- TOC entry 6561 (class 2606 OID 37335)
-- Name: CommerceHubPayoutItems FK_CommerceHubPayoutItems_CommerceHubVendorEarnings_EarningId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubPayoutItems"
    ADD CONSTRAINT "FK_CommerceHubPayoutItems_CommerceHubVendorEarnings_EarningId" FOREIGN KEY ("EarningId") REFERENCES public."CommerceHubVendorEarnings"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6559 (class 2606 OID 37892)
-- Name: CommerceHubPayouts FK_CommerceHubPayouts_CommerceHubVendorPayoutMethods_PayoutMeth; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubPayouts"
    ADD CONSTRAINT "FK_CommerceHubPayouts_CommerceHubVendorPayoutMethods_PayoutMeth" FOREIGN KEY ("PayoutMethodId") REFERENCES public."CommerceHubVendorPayoutMethods"("Id") ON DELETE SET NULL;


--
-- TOC entry 6569 (class 2606 OID 37562)
-- Name: CommerceHubRefundEvidenceMedia FK_CommerceHubRefundEvidenceMedia_CommerceHubCustomers_Customer; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubRefundEvidenceMedia"
    ADD CONSTRAINT "FK_CommerceHubRefundEvidenceMedia_CommerceHubCustomers_Customer" FOREIGN KEY ("CustomerId") REFERENCES public."CommerceHubCustomers"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6570 (class 2606 OID 37567)
-- Name: CommerceHubRefundEvidenceMedia FK_CommerceHubRefundEvidenceMedia_CommerceHubRefundRequests_Ref; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubRefundEvidenceMedia"
    ADD CONSTRAINT "FK_CommerceHubRefundEvidenceMedia_CommerceHubRefundRequests_Ref" FOREIGN KEY ("RefundRequestId") REFERENCES public."CommerceHubRefundRequests"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6572 (class 2606 OID 37627)
-- Name: CommerceHubRefundExecutions FK_CommerceHubRefundExecutions_CommerceHubRefundRequests_Refund; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubRefundExecutions"
    ADD CONSTRAINT "FK_CommerceHubRefundExecutions_CommerceHubRefundRequests_Refund" FOREIGN KEY ("RefundId") REFERENCES public."CommerceHubRefundRequests"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6566 (class 2606 OID 37509)
-- Name: CommerceHubRefundRequests FK_CommerceHubRefundRequests_CommerceHubCustomers_CustomerId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubRefundRequests"
    ADD CONSTRAINT "FK_CommerceHubRefundRequests_CommerceHubCustomers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES public."CommerceHubCustomers"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6567 (class 2606 OID 37499)
-- Name: CommerceHubRefundRequests FK_CommerceHubRefundRequests_CommerceHubMarketplaceOrders_Order; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubRefundRequests"
    ADD CONSTRAINT "FK_CommerceHubRefundRequests_CommerceHubMarketplaceOrders_Order" FOREIGN KEY ("OrderId") REFERENCES public."CommerceHubMarketplaceOrders"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6568 (class 2606 OID 37504)
-- Name: CommerceHubRefundRequests FK_CommerceHubRefundRequests_CommerceHubSubOrderItems_OrderItem; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubRefundRequests"
    ADD CONSTRAINT "FK_CommerceHubRefundRequests_CommerceHubSubOrderItems_OrderItem" FOREIGN KEY ("OrderItemId") REFERENCES public."CommerceHubSubOrderItems"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6562 (class 2606 OID 37345)
-- Name: CommerceHubRefunds FK_CommerceHubRefunds_CommerceHubPayments_PaymentId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubRefunds"
    ADD CONSTRAINT "FK_CommerceHubRefunds_CommerceHubPayments_PaymentId" FOREIGN KEY ("PaymentId") REFERENCES public."CommerceHubPayments"("Id") ON DELETE SET NULL;


--
-- TOC entry 6563 (class 2606 OID 37528)
-- Name: CommerceHubRefunds FK_CommerceHubRefunds_CommerceHubRefundRequests_RefundRequestId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubRefunds"
    ADD CONSTRAINT "FK_CommerceHubRefunds_CommerceHubRefundRequests_RefundRequestId" FOREIGN KEY ("RefundRequestId") REFERENCES public."CommerceHubRefundRequests"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6564 (class 2606 OID 37533)
-- Name: CommerceHubRefunds FK_CommerceHubRefunds_CommerceHubSubOrderItems_OrderItemId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubRefunds"
    ADD CONSTRAINT "FK_CommerceHubRefunds_CommerceHubSubOrderItems_OrderItemId" FOREIGN KEY ("OrderItemId") REFERENCES public."CommerceHubSubOrderItems"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6565 (class 2606 OID 37340)
-- Name: CommerceHubRefunds FK_CommerceHubRefunds_CommerceHubSubOrders_SubOrderId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubRefunds"
    ADD CONSTRAINT "FK_CommerceHubRefunds_CommerceHubSubOrders_SubOrderId" FOREIGN KEY ("SubOrderId") REFERENCES public."CommerceHubSubOrders"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6573 (class 2606 OID 37662)
-- Name: CommerceHubReturnRequests FK_CommerceHubReturnRequests_CommerceHubCustomers_CustomerId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubReturnRequests"
    ADD CONSTRAINT "FK_CommerceHubReturnRequests_CommerceHubCustomers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES public."CommerceHubCustomers"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6574 (class 2606 OID 37652)
-- Name: CommerceHubReturnRequests FK_CommerceHubReturnRequests_CommerceHubRefundRequests_RefundId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubReturnRequests"
    ADD CONSTRAINT "FK_CommerceHubReturnRequests_CommerceHubRefundRequests_RefundId" FOREIGN KEY ("RefundId") REFERENCES public."CommerceHubRefundRequests"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6575 (class 2606 OID 37657)
-- Name: CommerceHubReturnRequests FK_CommerceHubReturnRequests_CommerceHubSubOrderItems_OrderItem; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubReturnRequests"
    ADD CONSTRAINT "FK_CommerceHubReturnRequests_CommerceHubSubOrderItems_OrderItem" FOREIGN KEY ("OrderItemId") REFERENCES public."CommerceHubSubOrderItems"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6542 (class 2606 OID 36690)
-- Name: CommerceHubSubOrderItems FK_CommerceHubSubOrderItems_CommerceHubSubOrders_SubOrderId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubSubOrderItems"
    ADD CONSTRAINT "FK_CommerceHubSubOrderItems_CommerceHubSubOrders_SubOrderId" FOREIGN KEY ("SubOrderId") REFERENCES public."CommerceHubSubOrders"("Id") ON DELETE CASCADE;


--
-- TOC entry 6548 (class 2606 OID 37080)
-- Name: CommerceHubSubOrderStatusHistory FK_CommerceHubSubOrderStatusHistory_CommerceHubSubOrders_SubOrd; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubSubOrderStatusHistory"
    ADD CONSTRAINT "FK_CommerceHubSubOrderStatusHistory_CommerceHubSubOrders_SubOrd" FOREIGN KEY ("SubOrderId") REFERENCES public."CommerceHubSubOrders"("Id") ON DELETE CASCADE;


--
-- TOC entry 6541 (class 2606 OID 36685)
-- Name: CommerceHubSubOrders FK_CommerceHubSubOrders_CommerceHubMarketplaceOrders_Marketplac; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubSubOrders"
    ADD CONSTRAINT "FK_CommerceHubSubOrders_CommerceHubMarketplaceOrders_Marketplac" FOREIGN KEY ("MarketplaceOrderId") REFERENCES public."CommerceHubMarketplaceOrders"("Id") ON DELETE CASCADE;


--
-- TOC entry 6553 (class 2606 OID 37389)
-- Name: CommerceHubVendorEarnings FK_CommerceHubVendorEarnings_CommerceHubPayments_PaymentId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubVendorEarnings"
    ADD CONSTRAINT "FK_CommerceHubVendorEarnings_CommerceHubPayments_PaymentId" FOREIGN KEY ("PaymentId") REFERENCES public."CommerceHubPayments"("Id") ON DELETE SET NULL;


--
-- TOC entry 6554 (class 2606 OID 37315)
-- Name: CommerceHubVendorEarnings FK_CommerceHubVendorEarnings_CommerceHubPayouts_PayoutId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubVendorEarnings"
    ADD CONSTRAINT "FK_CommerceHubVendorEarnings_CommerceHubPayouts_PayoutId" FOREIGN KEY ("PayoutId") REFERENCES public."CommerceHubPayouts"("Id") ON DELETE SET NULL;


--
-- TOC entry 6555 (class 2606 OID 37320)
-- Name: CommerceHubVendorEarnings FK_CommerceHubVendorEarnings_CommerceHubRefunds_RefundId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubVendorEarnings"
    ADD CONSTRAINT "FK_CommerceHubVendorEarnings_CommerceHubRefunds_RefundId" FOREIGN KEY ("RefundId") REFERENCES public."CommerceHubRefunds"("Id") ON DELETE SET NULL;


--
-- TOC entry 6556 (class 2606 OID 37182)
-- Name: CommerceHubVendorEarnings FK_CommerceHubVendorEarnings_CommerceHubSubOrders_SubOrderId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubVendorEarnings"
    ADD CONSTRAINT "FK_CommerceHubVendorEarnings_CommerceHubSubOrders_SubOrderId" FOREIGN KEY ("SubOrderId") REFERENCES public."CommerceHubSubOrders"("Id") ON DELETE CASCADE;


--
-- TOC entry 6557 (class 2606 OID 37325)
-- Name: CommerceHubVendorEarnings FK_CommerceHubVendorEarnings_CommerceHubVendorEarnings_Original; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubVendorEarnings"
    ADD CONSTRAINT "FK_CommerceHubVendorEarnings_CommerceHubVendorEarnings_Original" FOREIGN KEY ("OriginalEarningId") REFERENCES public."CommerceHubVendorEarnings"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6579 (class 2606 OID 37855)
-- Name: CommerceHubVendorPaymentAuditLogs FK_CommerceHubVendorPaymentAuditLogs_CommerceHubVendorPaymentRe; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubVendorPaymentAuditLogs"
    ADD CONSTRAINT "FK_CommerceHubVendorPaymentAuditLogs_CommerceHubVendorPaymentRe" FOREIGN KEY ("PaymentRequestId") REFERENCES public."CommerceHubVendorPaymentRequests"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6578 (class 2606 OID 37830)
-- Name: CommerceHubVendorPaymentProofs FK_CommerceHubVendorPaymentProofs_CommerceHubVendorPaymentReque; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubVendorPaymentProofs"
    ADD CONSTRAINT "FK_CommerceHubVendorPaymentProofs_CommerceHubVendorPaymentReque" FOREIGN KEY ("PaymentRequestId") REFERENCES public."CommerceHubVendorPaymentRequests"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6576 (class 2606 OID 37807)
-- Name: CommerceHubVendorPaymentRequests FK_CommerceHubVendorPaymentRequests_CommerceHubVendorPaymentMet; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubVendorPaymentRequests"
    ADD CONSTRAINT "FK_CommerceHubVendorPaymentRequests_CommerceHubVendorPaymentMet" FOREIGN KEY ("PaymentMethodId") REFERENCES public."CommerceHubVendorPaymentMethods"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6577 (class 2606 OID 37802)
-- Name: CommerceHubVendorPaymentRequests FK_CommerceHubVendorPaymentRequests_Tenants_VendorId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CommerceHubVendorPaymentRequests"
    ADD CONSTRAINT "FK_CommerceHubVendorPaymentRequests_Tenants_VendorId" FOREIGN KEY ("VendorId") REFERENCES public."Tenants"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6594 (class 2606 OID 2287790)
-- Name: CouponReservations FK_CouponReservations_Coupons_CouponId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CouponReservations"
    ADD CONSTRAINT "FK_CouponReservations_Coupons_CouponId" FOREIGN KEY ("CouponId") REFERENCES public."Coupons"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6595 (class 2606 OID 2287795)
-- Name: CouponReservations FK_CouponReservations_OnlineCustomers_CustomerId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CouponReservations"
    ADD CONSTRAINT "FK_CouponReservations_OnlineCustomers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES public."OnlineCustomers"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6596 (class 2606 OID 2287800)
-- Name: CouponReservations FK_CouponReservations_Orders_OrderId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CouponReservations"
    ADD CONSTRAINT "FK_CouponReservations_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES public."Orders"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6475 (class 2606 OID 26113)
-- Name: CouponUsages FK_CouponUsages_Coupons_CouponId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CouponUsages"
    ADD CONSTRAINT "FK_CouponUsages_Coupons_CouponId" FOREIGN KEY ("CouponId") REFERENCES public."Coupons"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6476 (class 2606 OID 26123)
-- Name: CouponUsages FK_CouponUsages_Orders_OrderId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CouponUsages"
    ADD CONSTRAINT "FK_CouponUsages_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES public."Orders"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6375 (class 2606 OID 20136)
-- Name: CustomerTransactions FK_CustomerTransactions_Customers_CustomerId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."CustomerTransactions"
    ADD CONSTRAINT "FK_CustomerTransactions_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES public."Customers"("Id") ON DELETE CASCADE;


--
-- TOC entry 6468 (class 2606 OID 23762)
-- Name: EmailVerificationTokens FK_EmailVerificationTokens_Users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."EmailVerificationTokens"
    ADD CONSTRAINT "FK_EmailVerificationTokens_Users_UserId" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- TOC entry 6469 (class 2606 OID 25751)
-- Name: OnlineCustomerEmailVerifications FK_EmailVerification_OnlineCustomer; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."OnlineCustomerEmailVerifications"
    ADD CONSTRAINT "FK_EmailVerification_OnlineCustomer" FOREIGN KEY ("OnlineCustomerId") REFERENCES public."OnlineCustomers"("Id") ON DELETE CASCADE;


--
-- TOC entry 6435 (class 2606 OID 21421)
-- Name: Expenses FK_Expenses_Banks_BankId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Expenses"
    ADD CONSTRAINT "FK_Expenses_Banks_BankId" FOREIGN KEY ("BankId") REFERENCES public."Banks"("Id");


--
-- TOC entry 6436 (class 2606 OID 21426)
-- Name: Expenses FK_Expenses_Cashboxes_CashboxId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Expenses"
    ADD CONSTRAINT "FK_Expenses_Cashboxes_CashboxId" FOREIGN KEY ("CashboxId") REFERENCES public."Cashboxes"("Id");


--
-- TOC entry 6437 (class 2606 OID 21431)
-- Name: Expenses FK_Expenses_ExpenseCategories_ExpenseCategoryId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Expenses"
    ADD CONSTRAINT "FK_Expenses_ExpenseCategories_ExpenseCategoryId" FOREIGN KEY ("ExpenseCategoryId") REFERENCES public."ExpenseCategories"("Id") ON DELETE CASCADE;


--
-- TOC entry 6438 (class 2606 OID 21436)
-- Name: Expenses FK_Expenses_Suppliers_SupplierId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Expenses"
    ADD CONSTRAINT "FK_Expenses_Suppliers_SupplierId" FOREIGN KEY ("SupplierId") REFERENCES public."Suppliers"("Id");


--
-- TOC entry 6432 (class 2606 OID 21321)
-- Name: FinancialAccounts FK_FinancialAccounts_FinancialAccounts_ParentAccountId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."FinancialAccounts"
    ADD CONSTRAINT "FK_FinancialAccounts_FinancialAccounts_ParentAccountId" FOREIGN KEY ("ParentAccountId") REFERENCES public."FinancialAccounts"("Id");


--
-- TOC entry 6433 (class 2606 OID 21341)
-- Name: FinancialTransactions FK_FinancialTransactions_FinancialAccounts_AccountId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."FinancialTransactions"
    ADD CONSTRAINT "FK_FinancialTransactions_FinancialAccounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES public."FinancialAccounts"("Id") ON DELETE CASCADE;


--
-- TOC entry 6444 (class 2606 OID 23226)
-- Name: JournalEntries FK_JournalEntries_JournalEntries_ReversedJournalId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."JournalEntries"
    ADD CONSTRAINT "FK_JournalEntries_JournalEntries_ReversedJournalId" FOREIGN KEY ("ReversedJournalId") REFERENCES public."JournalEntries"("JournalId");


--
-- TOC entry 6447 (class 2606 OID 23284)
-- Name: JournalLines FK_JournalLines_Accounts_AccountId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."JournalLines"
    ADD CONSTRAINT "FK_JournalLines_Accounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES public."Accounts"("AccountId") ON DELETE RESTRICT;


--
-- TOC entry 6448 (class 2606 OID 23289)
-- Name: JournalLines FK_JournalLines_JournalEntries_JournalId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."JournalLines"
    ADD CONSTRAINT "FK_JournalLines_JournalEntries_JournalId" FOREIGN KEY ("JournalId") REFERENCES public."JournalEntries"("JournalId") ON DELETE CASCADE;


--
-- TOC entry 6490 (class 2606 OID 35036)
-- Name: LedgerAccounts FK_LedgerAccounts_Parent; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."LedgerAccounts"
    ADD CONSTRAINT "FK_LedgerAccounts_Parent" FOREIGN KEY ("ParentAccountId") REFERENCES public."LedgerAccounts"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6491 (class 2606 OID 35065)
-- Name: LedgerJournalEntries FK_LedgerJournal_ReversedBy; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."LedgerJournalEntries"
    ADD CONSTRAINT "FK_LedgerJournal_ReversedBy" FOREIGN KEY ("ReversedByJournalId") REFERENCES public."LedgerJournalEntries"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6492 (class 2606 OID 35103)
-- Name: LedgerLines FK_LedgerLines_Account; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."LedgerLines"
    ADD CONSTRAINT "FK_LedgerLines_Account" FOREIGN KEY ("AccountId") REFERENCES public."LedgerAccounts"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6493 (class 2606 OID 35108)
-- Name: LedgerLines FK_LedgerLines_Journal; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."LedgerLines"
    ADD CONSTRAINT "FK_LedgerLines_Journal" FOREIGN KEY ("JournalEntryId") REFERENCES public."LedgerJournalEntries"("Id") ON DELETE CASCADE;


--
-- TOC entry 6519 (class 2606 OID 35858)
-- Name: MarketingReviewEvidences FK_MarketingReviewEvidences_ServiceReviews_ServiceReviewId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."MarketingReviewEvidences"
    ADD CONSTRAINT "FK_MarketingReviewEvidences_ServiceReviews_ServiceReviewId" FOREIGN KEY ("ServiceReviewId") REFERENCES public."ServiceReviews"("Id") ON DELETE CASCADE;


--
-- TOC entry 6586 (class 2606 OID 46817)
-- Name: MarketplaceCategoryTranslations FK_MarketplaceCategoryTranslations_Categories_CategoryId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."MarketplaceCategoryTranslations"
    ADD CONSTRAINT "FK_MarketplaceCategoryTranslations_Categories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES public."Categories"("Id") ON DELETE CASCADE;


--
-- TOC entry 6582 (class 2606 OID 46602)
-- Name: MarketplaceFooterLinks FK_MarketplaceFooterLinks_MarketplaceFooterSections_SectionId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."MarketplaceFooterLinks"
    ADD CONSTRAINT "FK_MarketplaceFooterLinks_MarketplaceFooterSections_SectionId" FOREIGN KEY ("SectionId") REFERENCES public."MarketplaceFooterSections"("Id") ON DELETE CASCADE;


--
-- TOC entry 6500 (class 2606 OID 35450)
-- Name: MarketplaceProfiles FK_MarketplaceProfiles_Tenants; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."MarketplaceProfiles"
    ADD CONSTRAINT "FK_MarketplaceProfiles_Tenants" FOREIGN KEY ("TenantId") REFERENCES public."Tenants"("Id") ON DELETE CASCADE;


--
-- TOC entry 6501 (class 2606 OID 35455)
-- Name: MarketplaceProfiles FK_MarketplaceProfiles_Users; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."MarketplaceProfiles"
    ADD CONSTRAINT "FK_MarketplaceProfiles_Users" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- TOC entry 6456 (class 2606 OID 23489)
-- Name: OrderItems FK_OrderItems_Orders_OrderId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."OrderItems"
    ADD CONSTRAINT "FK_OrderItems_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES public."Orders"("Id") ON DELETE CASCADE;


--
-- TOC entry 6457 (class 2606 OID 25988)
-- Name: OrderItems FK_OrderItems_ProductVariants_VariantId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."OrderItems"
    ADD CONSTRAINT "FK_OrderItems_ProductVariants_VariantId" FOREIGN KEY ("VariantId") REFERENCES public."ProductVariants"("Id") ON DELETE SET NULL;


--
-- TOC entry 6458 (class 2606 OID 23494)
-- Name: OrderItems FK_OrderItems_Products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."OrderItems"
    ADD CONSTRAINT "FK_OrderItems_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public."Products"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6459 (class 2606 OID 23499)
-- Name: OrderItems FK_OrderItems_Units_UnitId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."OrderItems"
    ADD CONSTRAINT "FK_OrderItems_Units_UnitId" FOREIGN KEY ("UnitId") REFERENCES public."Units"("Id") ON DELETE SET NULL;


--
-- TOC entry 6460 (class 2606 OID 23504)
-- Name: OrderItems FK_OrderItems_Warehouses_WarehouseId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."OrderItems"
    ADD CONSTRAINT "FK_OrderItems_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES public."Warehouses"("Id") ON DELETE SET NULL;


--
-- TOC entry 6592 (class 2606 OID 2287750)
-- Name: OrderRefundLines FK_OrderRefundLines_OrderItems_OrderId_OrderItemId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."OrderRefundLines"
    ADD CONSTRAINT "FK_OrderRefundLines_OrderItems_OrderId_OrderItemId" FOREIGN KEY ("OrderId", "OrderItemId") REFERENCES public."OrderItems"("OrderId", "Id") ON DELETE RESTRICT;


--
-- TOC entry 6593 (class 2606 OID 2287755)
-- Name: OrderRefundLines FK_OrderRefundLines_OrderRefunds_TenantId_OrderId_OrderRefundId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."OrderRefundLines"
    ADD CONSTRAINT "FK_OrderRefundLines_OrderRefunds_TenantId_OrderId_OrderRefundId" FOREIGN KEY ("TenantId", "OrderId", "OrderRefundId") REFERENCES public."OrderRefunds"("TenantId", "OrderId", "Id") ON DELETE CASCADE;


--
-- TOC entry 6591 (class 2606 OID 2287671)
-- Name: OrderRefunds FK_OrderRefunds_Orders_OrderId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."OrderRefunds"
    ADD CONSTRAINT "FK_OrderRefunds_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES public."Orders"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6449 (class 2606 OID 36159)
-- Name: Orders FK_Orders_AffiliateAccounts_AffiliateAccountId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Orders"
    ADD CONSTRAINT "FK_Orders_AffiliateAccounts_AffiliateAccountId" FOREIGN KEY ("AffiliateAccountId") REFERENCES public."AffiliateAccounts"("Id") ON DELETE SET NULL;


--
-- TOC entry 6450 (class 2606 OID 23449)
-- Name: Orders FK_Orders_Coupons_CouponId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Orders"
    ADD CONSTRAINT "FK_Orders_Coupons_CouponId" FOREIGN KEY ("CouponId") REFERENCES public."Coupons"("Id") ON DELETE SET NULL;


--
-- TOC entry 6451 (class 2606 OID 23454)
-- Name: Orders FK_Orders_Customers_CustomerId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Orders"
    ADD CONSTRAINT "FK_Orders_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES public."Customers"("Id") ON DELETE SET NULL;


--
-- TOC entry 6452 (class 2606 OID 25887)
-- Name: Orders FK_Orders_OnlineCustomers_OnlineCustomerId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Orders"
    ADD CONSTRAINT "FK_Orders_OnlineCustomers_OnlineCustomerId" FOREIGN KEY ("OnlineCustomerId") REFERENCES public."OnlineCustomers"("Id") ON DELETE SET NULL;


--
-- TOC entry 6453 (class 2606 OID 26204)
-- Name: Orders FK_Orders_Promotions_AppliedPromotionId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Orders"
    ADD CONSTRAINT "FK_Orders_Promotions_AppliedPromotionId" FOREIGN KEY ("AppliedPromotionId") REFERENCES public."Promotions"("Id") ON DELETE SET NULL;


--
-- TOC entry 6454 (class 2606 OID 23459)
-- Name: Orders FK_Orders_ShippingMethods_ShippingMethodId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Orders"
    ADD CONSTRAINT "FK_Orders_ShippingMethods_ShippingMethodId" FOREIGN KEY ("ShippingMethodId") REFERENCES public."ShippingMethods"("Id") ON DELETE SET NULL;


--
-- TOC entry 6455 (class 2606 OID 23464)
-- Name: Orders FK_Orders_Warehouses_WarehouseId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Orders"
    ADD CONSTRAINT "FK_Orders_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES public."Warehouses"("Id") ON DELETE SET NULL;


--
-- TOC entry 6461 (class 2606 OID 23569)
-- Name: OutboxMessage FK_OutboxMessage_InboxState_InboxMessageId_InboxConsumerId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."OutboxMessage"
    ADD CONSTRAINT "FK_OutboxMessage_InboxState_InboxMessageId_InboxConsumerId" FOREIGN KEY ("InboxMessageId", "InboxConsumerId") REFERENCES public."InboxState"("MessageId", "ConsumerId");


--
-- TOC entry 6462 (class 2606 OID 23574)
-- Name: OutboxMessage FK_OutboxMessage_OutboxState_OutboxId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."OutboxMessage"
    ADD CONSTRAINT "FK_OutboxMessage_OutboxState_OutboxId" FOREIGN KEY ("OutboxId") REFERENCES public."OutboxState"("OutboxId");


--
-- TOC entry 6464 (class 2606 OID 23618)
-- Name: PaymentIntents FK_PaymentIntents_Orders_OrderId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PaymentIntents"
    ADD CONSTRAINT "FK_PaymentIntents_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES public."Orders"("Id") ON DELETE CASCADE;


--
-- TOC entry 6494 (class 2606 OID 35159)
-- Name: PaymentMethods FK_PaymentMethods_Clearing; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PaymentMethods"
    ADD CONSTRAINT "FK_PaymentMethods_Clearing" FOREIGN KEY ("ClearingAccountId") REFERENCES public."LedgerAccounts"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6495 (class 2606 OID 35164)
-- Name: PaymentMethods FK_PaymentMethods_Fees; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PaymentMethods"
    ADD CONSTRAINT "FK_PaymentMethods_Fees" FOREIGN KEY ("FeesAccountId") REFERENCES public."LedgerAccounts"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6496 (class 2606 OID 35154)
-- Name: PaymentMethods FK_PaymentMethods_Target; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PaymentMethods"
    ADD CONSTRAINT "FK_PaymentMethods_Target" FOREIGN KEY ("TargetAccountId") REFERENCES public."LedgerAccounts"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6467 (class 2606 OID 23670)
-- Name: PaymentWebhookLogs FK_PaymentWebhookLogs_PaymentIntents_PaymentIntentId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PaymentWebhookLogs"
    ADD CONSTRAINT "FK_PaymentWebhookLogs_PaymentIntents_PaymentIntentId" FOREIGN KEY ("PaymentIntentId") REFERENCES public."PaymentIntents"("Id");


--
-- TOC entry 6538 (class 2606 OID 36539)
-- Name: ProductMedia FK_ProductMedia_Products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ProductMedia"
    ADD CONSTRAINT "FK_ProductMedia_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public."Products"("Id") ON DELETE CASCADE;


--
-- TOC entry 6480 (class 2606 OID 34673)
-- Name: ProductPixelOverrides FK_ProductPixelOverride_Product; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ProductPixelOverrides"
    ADD CONSTRAINT "FK_ProductPixelOverride_Product" FOREIGN KEY ("ProductId") REFERENCES public."Products"("Id") ON DELETE CASCADE;


--
-- TOC entry 6584 (class 2606 OID 46768)
-- Name: ProductReviews FK_ProductReviews_CommerceHubCustomers_UserId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ProductReviews"
    ADD CONSTRAINT "FK_ProductReviews_CommerceHubCustomers_UserId" FOREIGN KEY ("UserId") REFERENCES public."CommerceHubCustomers"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6585 (class 2606 OID 46773)
-- Name: ProductReviews FK_ProductReviews_Products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ProductReviews"
    ADD CONSTRAINT "FK_ProductReviews_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public."Products"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6441 (class 2606 OID 21534)
-- Name: ProductUnits FK_ProductUnits_Products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ProductUnits"
    ADD CONSTRAINT "FK_ProductUnits_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public."Products"("Id") ON DELETE CASCADE;


--
-- TOC entry 6442 (class 2606 OID 21539)
-- Name: ProductUnits FK_ProductUnits_Units_UnitId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ProductUnits"
    ADD CONSTRAINT "FK_ProductUnits_Units_UnitId" FOREIGN KEY ("UnitId") REFERENCES public."Units"("Id") ON DELETE CASCADE;


--
-- TOC entry 6472 (class 2606 OID 25946)
-- Name: ProductVariantAttributes FK_ProductVariantAttributes_ProductVariants_VariantId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ProductVariantAttributes"
    ADD CONSTRAINT "FK_ProductVariantAttributes_ProductVariants_VariantId" FOREIGN KEY ("VariantId") REFERENCES public."ProductVariants"("Id") ON DELETE CASCADE;


--
-- TOC entry 6473 (class 2606 OID 25968)
-- Name: ProductVariantStocks FK_ProductVariantStocks_ProductVariants_VariantId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ProductVariantStocks"
    ADD CONSTRAINT "FK_ProductVariantStocks_ProductVariants_VariantId" FOREIGN KEY ("VariantId") REFERENCES public."ProductVariants"("Id") ON DELETE CASCADE;


--
-- TOC entry 6474 (class 2606 OID 25973)
-- Name: ProductVariantStocks FK_ProductVariantStocks_Warehouses_WarehouseId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ProductVariantStocks"
    ADD CONSTRAINT "FK_ProductVariantStocks_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES public."Warehouses"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6385 (class 2606 OID 36545)
-- Name: ProductVariants FK_ProductVariant_ProductMedia; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ProductVariants"
    ADD CONSTRAINT "FK_ProductVariant_ProductMedia" FOREIGN KEY ("MediaId") REFERENCES public."ProductMedia"("Id") ON DELETE SET NULL;


--
-- TOC entry 6386 (class 2606 OID 20269)
-- Name: ProductVariants FK_ProductVariants_Products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ProductVariants"
    ADD CONSTRAINT "FK_ProductVariants_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public."Products"("Id") ON DELETE CASCADE;


--
-- TOC entry 6374 (class 2606 OID 20115)
-- Name: Products FK_Products_Categories_CategoryId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Products"
    ADD CONSTRAINT "FK_Products_Categories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES public."Categories"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6580 (class 2606 OID 38185)
-- Name: PromoCodeUsages FK_PromoCodeUsages_CommerceHubMarketplaceOrders; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PromoCodeUsages"
    ADD CONSTRAINT "FK_PromoCodeUsages_CommerceHubMarketplaceOrders" FOREIGN KEY ("MarketplaceOrderId") REFERENCES public."CommerceHubMarketplaceOrders"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6581 (class 2606 OID 38180)
-- Name: PromoCodeUsages FK_PromoCodeUsages_PromoCodes; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PromoCodeUsages"
    ADD CONSTRAINT "FK_PromoCodeUsages_PromoCodes" FOREIGN KEY ("PromoCodeId") REFERENCES public."PromoCodes"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6477 (class 2606 OID 26197)
-- Name: PromotionUsages FK_PromotionUsages_OnlineCustomers_CustomerId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PromotionUsages"
    ADD CONSTRAINT "FK_PromotionUsages_OnlineCustomers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES public."OnlineCustomers"("Id") ON DELETE SET NULL;


--
-- TOC entry 6478 (class 2606 OID 26192)
-- Name: PromotionUsages FK_PromotionUsages_Orders_OrderId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PromotionUsages"
    ADD CONSTRAINT "FK_PromotionUsages_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES public."Orders"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6479 (class 2606 OID 26187)
-- Name: PromotionUsages FK_PromotionUsages_Promotions_PromotionId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PromotionUsages"
    ADD CONSTRAINT "FK_PromotionUsages_Promotions_PromotionId" FOREIGN KEY ("PromotionId") REFERENCES public."Promotions"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6516 (class 2606 OID 35710)
-- Name: ProviderTrustAggregates FK_ProviderTrustAggregates_MarketplaceProfiles; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ProviderTrustAggregates"
    ADD CONSTRAINT "FK_ProviderTrustAggregates_MarketplaceProfiles" FOREIGN KEY ("ProviderProfileId") REFERENCES public."MarketplaceProfiles"("Id") ON DELETE CASCADE;


--
-- TOC entry 6517 (class 2606 OID 35705)
-- Name: ProviderTrustAggregates FK_ProviderTrustAggregates_Tenants; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ProviderTrustAggregates"
    ADD CONSTRAINT "FK_ProviderTrustAggregates_Tenants" FOREIGN KEY ("TenantId") REFERENCES public."Tenants"("Id") ON DELETE CASCADE;


--
-- TOC entry 6389 (class 2606 OID 20330)
-- Name: PurchaseInvoiceItems FK_PurchaseInvoiceItems_ProductVariants_ProductVariantId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PurchaseInvoiceItems"
    ADD CONSTRAINT "FK_PurchaseInvoiceItems_ProductVariants_ProductVariantId" FOREIGN KEY ("ProductVariantId") REFERENCES public."ProductVariants"("Id");


--
-- TOC entry 6390 (class 2606 OID 20335)
-- Name: PurchaseInvoiceItems FK_PurchaseInvoiceItems_Products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PurchaseInvoiceItems"
    ADD CONSTRAINT "FK_PurchaseInvoiceItems_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public."Products"("Id") ON DELETE CASCADE;


--
-- TOC entry 6391 (class 2606 OID 20340)
-- Name: PurchaseInvoiceItems FK_PurchaseInvoiceItems_PurchaseInvoices_PurchaseInvoiceId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PurchaseInvoiceItems"
    ADD CONSTRAINT "FK_PurchaseInvoiceItems_PurchaseInvoices_PurchaseInvoiceId" FOREIGN KEY ("PurchaseInvoiceId") REFERENCES public."PurchaseInvoices"("Id") ON DELETE CASCADE;


--
-- TOC entry 6392 (class 2606 OID 21550)
-- Name: PurchaseInvoiceItems FK_PurchaseInvoiceItems_Units_UnitId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PurchaseInvoiceItems"
    ADD CONSTRAINT "FK_PurchaseInvoiceItems_Units_UnitId" FOREIGN KEY ("UnitId") REFERENCES public."Units"("Id");


--
-- TOC entry 6387 (class 2606 OID 20288)
-- Name: PurchaseInvoicePayments FK_PurchaseInvoicePayments_PurchaseInvoices_PurchaseInvoiceId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PurchaseInvoicePayments"
    ADD CONSTRAINT "FK_PurchaseInvoicePayments_PurchaseInvoices_PurchaseInvoiceId" FOREIGN KEY ("PurchaseInvoiceId") REFERENCES public."PurchaseInvoices"("Id") ON DELETE CASCADE;


--
-- TOC entry 6377 (class 2606 OID 20183)
-- Name: PurchaseInvoices FK_PurchaseInvoices_Suppliers_SupplierId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PurchaseInvoices"
    ADD CONSTRAINT "FK_PurchaseInvoices_Suppliers_SupplierId" FOREIGN KEY ("SupplierId") REFERENCES public."Suppliers"("Id") ON DELETE CASCADE;


--
-- TOC entry 6378 (class 2606 OID 20188)
-- Name: PurchaseInvoices FK_PurchaseInvoices_Warehouses_WarehouseId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."PurchaseInvoices"
    ADD CONSTRAINT "FK_PurchaseInvoices_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES public."Warehouses"("Id") ON DELETE CASCADE;


--
-- TOC entry 6470 (class 2606 OID 25775)
-- Name: OnlineCustomerRefreshTokens FK_RefreshToken_OnlineCustomer; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."OnlineCustomerRefreshTokens"
    ADD CONSTRAINT "FK_RefreshToken_OnlineCustomer" FOREIGN KEY ("OnlineCustomerId") REFERENCES public."OnlineCustomers"("Id") ON DELETE CASCADE;


--
-- TOC entry 6499 (class 2606 OID 35410)
-- Name: RejectedCarrierWebhookEvents FK_RejectedCarrierWebhookEvents_Orders_OrderId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."RejectedCarrierWebhookEvents"
    ADD CONSTRAINT "FK_RejectedCarrierWebhookEvents_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES public."Orders"("Id") ON DELETE CASCADE;


--
-- TOC entry 6393 (class 2606 OID 20362)
-- Name: ReturnItems FK_ReturnItems_ProductVariants_ProductVariantId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ReturnItems"
    ADD CONSTRAINT "FK_ReturnItems_ProductVariants_ProductVariantId" FOREIGN KEY ("ProductVariantId") REFERENCES public."ProductVariants"("Id");


--
-- TOC entry 6394 (class 2606 OID 20367)
-- Name: ReturnItems FK_ReturnItems_Products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ReturnItems"
    ADD CONSTRAINT "FK_ReturnItems_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public."Products"("Id") ON DELETE CASCADE;


--
-- TOC entry 6395 (class 2606 OID 20372)
-- Name: ReturnItems FK_ReturnItems_Returns_ReturnId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ReturnItems"
    ADD CONSTRAINT "FK_ReturnItems_Returns_ReturnId" FOREIGN KEY ("ReturnId") REFERENCES public."Returns"("Id") ON DELETE CASCADE;


--
-- TOC entry 6396 (class 2606 OID 21555)
-- Name: ReturnItems FK_ReturnItems_Units_UnitId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ReturnItems"
    ADD CONSTRAINT "FK_ReturnItems_Units_UnitId" FOREIGN KEY ("UnitId") REFERENCES public."Units"("Id");


--
-- TOC entry 6379 (class 2606 OID 20210)
-- Name: Returns FK_Returns_Customers_CustomerId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Returns"
    ADD CONSTRAINT "FK_Returns_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES public."Customers"("Id");


--
-- TOC entry 6380 (class 2606 OID 20215)
-- Name: Returns FK_Returns_Suppliers_SupplierId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Returns"
    ADD CONSTRAINT "FK_Returns_Suppliers_SupplierId" FOREIGN KEY ("SupplierId") REFERENCES public."Suppliers"("Id");


--
-- TOC entry 6381 (class 2606 OID 20220)
-- Name: Returns FK_Returns_Warehouses_WarehouseId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Returns"
    ADD CONSTRAINT "FK_Returns_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES public."Warehouses"("Id") ON DELETE CASCADE;


--
-- TOC entry 6589 (class 2606 OID 2287522)
-- Name: RevokedCustomerAccessTokens FK_RevokedCustomerAccessTokens_CommerceHubCustomers_CustomerId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."RevokedCustomerAccessTokens"
    ADD CONSTRAINT "FK_RevokedCustomerAccessTokens_CommerceHubCustomers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES public."CommerceHubCustomers"("Id") ON DELETE CASCADE;


--
-- TOC entry 6590 (class 2606 OID 2287541)
-- Name: RevokedStorefrontAccessTokens FK_RevokedStorefrontAccessTokens_OnlineCustomers_OnlineCustomer; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."RevokedStorefrontAccessTokens"
    ADD CONSTRAINT "FK_RevokedStorefrontAccessTokens_OnlineCustomers_OnlineCustomer" FOREIGN KEY ("OnlineCustomerId") REFERENCES public."OnlineCustomers"("Id") ON DELETE SET NULL;


--
-- TOC entry 6397 (class 2606 OID 20395)
-- Name: SalesInvoiceItems FK_SalesInvoiceItems_ProductVariants_ProductVariantId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."SalesInvoiceItems"
    ADD CONSTRAINT "FK_SalesInvoiceItems_ProductVariants_ProductVariantId" FOREIGN KEY ("ProductVariantId") REFERENCES public."ProductVariants"("Id");


--
-- TOC entry 6398 (class 2606 OID 20400)
-- Name: SalesInvoiceItems FK_SalesInvoiceItems_Products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."SalesInvoiceItems"
    ADD CONSTRAINT "FK_SalesInvoiceItems_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public."Products"("Id") ON DELETE CASCADE;


--
-- TOC entry 6399 (class 2606 OID 20405)
-- Name: SalesInvoiceItems FK_SalesInvoiceItems_SalesInvoices_SalesInvoiceId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."SalesInvoiceItems"
    ADD CONSTRAINT "FK_SalesInvoiceItems_SalesInvoices_SalesInvoiceId" FOREIGN KEY ("SalesInvoiceId") REFERENCES public."SalesInvoices"("Id") ON DELETE CASCADE;


--
-- TOC entry 6400 (class 2606 OID 21560)
-- Name: SalesInvoiceItems FK_SalesInvoiceItems_Units_UnitId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."SalesInvoiceItems"
    ADD CONSTRAINT "FK_SalesInvoiceItems_Units_UnitId" FOREIGN KEY ("UnitId") REFERENCES public."Units"("Id");


--
-- TOC entry 6388 (class 2606 OID 20307)
-- Name: SalesInvoicePayments FK_SalesInvoicePayments_SalesInvoices_SalesInvoiceId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."SalesInvoicePayments"
    ADD CONSTRAINT "FK_SalesInvoicePayments_SalesInvoices_SalesInvoiceId" FOREIGN KEY ("SalesInvoiceId") REFERENCES public."SalesInvoices"("Id") ON DELETE CASCADE;


--
-- TOC entry 6382 (class 2606 OID 20245)
-- Name: SalesInvoices FK_SalesInvoices_Customers_CustomerId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."SalesInvoices"
    ADD CONSTRAINT "FK_SalesInvoices_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES public."Customers"("Id");


--
-- TOC entry 6383 (class 2606 OID 21303)
-- Name: SalesInvoices FK_SalesInvoices_SalesRepresentatives_SalesRepresentativeId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."SalesInvoices"
    ADD CONSTRAINT "FK_SalesInvoices_SalesRepresentatives_SalesRepresentativeId" FOREIGN KEY ("SalesRepresentativeId") REFERENCES public."SalesRepresentatives"("Id");


--
-- TOC entry 6384 (class 2606 OID 20250)
-- Name: SalesInvoices FK_SalesInvoices_Warehouses_WarehouseId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."SalesInvoices"
    ADD CONSTRAINT "FK_SalesInvoices_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES public."Warehouses"("Id") ON DELETE CASCADE;


--
-- TOC entry 6513 (class 2606 OID 35649)
-- Name: ServiceRequestMessages FK_ServiceRequestMessages_ServiceRequests; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ServiceRequestMessages"
    ADD CONSTRAINT "FK_ServiceRequestMessages_ServiceRequests" FOREIGN KEY ("ServiceRequestId") REFERENCES public."ServiceRequests"("Id") ON DELETE CASCADE;


--
-- TOC entry 6514 (class 2606 OID 35644)
-- Name: ServiceRequestMessages FK_ServiceRequestMessages_Tenants; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ServiceRequestMessages"
    ADD CONSTRAINT "FK_ServiceRequestMessages_Tenants" FOREIGN KEY ("TenantId") REFERENCES public."Tenants"("Id") ON DELETE CASCADE;


--
-- TOC entry 6515 (class 2606 OID 35654)
-- Name: ServiceRequestMessages FK_ServiceRequestMessages_Users; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ServiceRequestMessages"
    ADD CONSTRAINT "FK_ServiceRequestMessages_Users" FOREIGN KEY ("SenderUserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- TOC entry 6504 (class 2606 OID 35541)
-- Name: ServiceRequests FK_ServiceRequests_BuyerUsers; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ServiceRequests"
    ADD CONSTRAINT "FK_ServiceRequests_BuyerUsers" FOREIGN KEY ("BuyerUserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- TOC entry 6505 (class 2606 OID 35546)
-- Name: ServiceRequests FK_ServiceRequests_ProviderUsers; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ServiceRequests"
    ADD CONSTRAINT "FK_ServiceRequests_ProviderUsers" FOREIGN KEY ("ProviderUserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- TOC entry 6506 (class 2606 OID 35536)
-- Name: ServiceRequests FK_ServiceRequests_Services; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ServiceRequests"
    ADD CONSTRAINT "FK_ServiceRequests_Services" FOREIGN KEY ("ServiceId") REFERENCES public."Services"("Id") ON DELETE CASCADE;


--
-- TOC entry 6507 (class 2606 OID 35531)
-- Name: ServiceRequests FK_ServiceRequests_Tenants; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ServiceRequests"
    ADD CONSTRAINT "FK_ServiceRequests_Tenants" FOREIGN KEY ("TenantId") REFERENCES public."Tenants"("Id") ON DELETE CASCADE;


--
-- TOC entry 6508 (class 2606 OID 35598)
-- Name: ServiceReviews FK_ServiceReviews_ProviderUsers; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ServiceReviews"
    ADD CONSTRAINT "FK_ServiceReviews_ProviderUsers" FOREIGN KEY ("ProviderUserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- TOC entry 6509 (class 2606 OID 35593)
-- Name: ServiceReviews FK_ServiceReviews_ReviewerUsers; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ServiceReviews"
    ADD CONSTRAINT "FK_ServiceReviews_ReviewerUsers" FOREIGN KEY ("ReviewerUserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- TOC entry 6510 (class 2606 OID 35583)
-- Name: ServiceReviews FK_ServiceReviews_ServiceRequests; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ServiceReviews"
    ADD CONSTRAINT "FK_ServiceReviews_ServiceRequests" FOREIGN KEY ("ServiceRequestId") REFERENCES public."ServiceRequests"("Id") ON DELETE CASCADE;


--
-- TOC entry 6511 (class 2606 OID 35588)
-- Name: ServiceReviews FK_ServiceReviews_Services; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ServiceReviews"
    ADD CONSTRAINT "FK_ServiceReviews_Services" FOREIGN KEY ("ServiceId") REFERENCES public."Services"("Id") ON DELETE CASCADE;


--
-- TOC entry 6512 (class 2606 OID 35578)
-- Name: ServiceReviews FK_ServiceReviews_Tenants; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."ServiceReviews"
    ADD CONSTRAINT "FK_ServiceReviews_Tenants" FOREIGN KEY ("TenantId") REFERENCES public."Tenants"("Id") ON DELETE CASCADE;


--
-- TOC entry 6502 (class 2606 OID 35487)
-- Name: Services FK_Services_MarketplaceProfiles; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Services"
    ADD CONSTRAINT "FK_Services_MarketplaceProfiles" FOREIGN KEY ("ProfileId") REFERENCES public."MarketplaceProfiles"("Id") ON DELETE CASCADE;


--
-- TOC entry 6503 (class 2606 OID 35482)
-- Name: Services FK_Services_Tenants; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Services"
    ADD CONSTRAINT "FK_Services_Tenants" FOREIGN KEY ("TenantId") REFERENCES public."Tenants"("Id") ON DELETE CASCADE;


--
-- TOC entry 6465 (class 2606 OID 23643)
-- Name: Shipments FK_Shipments_Orders_OrderId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Shipments"
    ADD CONSTRAINT "FK_Shipments_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES public."Orders"("Id") ON DELETE CASCADE;


--
-- TOC entry 6466 (class 2606 OID 23648)
-- Name: Shipments FK_Shipments_ShippingMethods_ShippingMethodId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Shipments"
    ADD CONSTRAINT "FK_Shipments_ShippingMethods_ShippingMethodId" FOREIGN KEY ("ShippingMethodId") REFERENCES public."ShippingMethods"("Id") ON DELETE CASCADE;


--
-- TOC entry 6539 (class 2606 OID 36602)
-- Name: SmartOffers FK_SmartOffers_Tenants_TenantId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."SmartOffers"
    ADD CONSTRAINT "FK_SmartOffers_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES public."Tenants"("Id") ON DELETE CASCADE;


--
-- TOC entry 6415 (class 2606 OID 21108)
-- Name: StockBatches FK_StockBatches_ProductVariants_ProductVariantId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockBatches"
    ADD CONSTRAINT "FK_StockBatches_ProductVariants_ProductVariantId" FOREIGN KEY ("ProductVariantId") REFERENCES public."ProductVariants"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6416 (class 2606 OID 21113)
-- Name: StockBatches FK_StockBatches_Products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockBatches"
    ADD CONSTRAINT "FK_StockBatches_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public."Products"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6417 (class 2606 OID 21118)
-- Name: StockBatches FK_StockBatches_PurchaseInvoices_PurchaseInvoiceId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockBatches"
    ADD CONSTRAINT "FK_StockBatches_PurchaseInvoices_PurchaseInvoiceId" FOREIGN KEY ("PurchaseInvoiceId") REFERENCES public."PurchaseInvoices"("Id") ON DELETE SET NULL;


--
-- TOC entry 6418 (class 2606 OID 21123)
-- Name: StockBatches FK_StockBatches_Warehouses_WarehouseId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockBatches"
    ADD CONSTRAINT "FK_StockBatches_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES public."Warehouses"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6401 (class 2606 OID 20425)
-- Name: StockMovements FK_StockMovements_ProductVariants_ProductVariantId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockMovements"
    ADD CONSTRAINT "FK_StockMovements_ProductVariants_ProductVariantId" FOREIGN KEY ("ProductVariantId") REFERENCES public."ProductVariants"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6402 (class 2606 OID 20430)
-- Name: StockMovements FK_StockMovements_Products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockMovements"
    ADD CONSTRAINT "FK_StockMovements_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public."Products"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6403 (class 2606 OID 21565)
-- Name: StockMovements FK_StockMovements_Units_UnitId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockMovements"
    ADD CONSTRAINT "FK_StockMovements_Units_UnitId" FOREIGN KEY ("UnitId") REFERENCES public."Units"("Id");


--
-- TOC entry 6404 (class 2606 OID 20435)
-- Name: StockMovements FK_StockMovements_Warehouses_ToWarehouseId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockMovements"
    ADD CONSTRAINT "FK_StockMovements_Warehouses_ToWarehouseId" FOREIGN KEY ("ToWarehouseId") REFERENCES public."Warehouses"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6405 (class 2606 OID 20440)
-- Name: StockMovements FK_StockMovements_Warehouses_WarehouseId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockMovements"
    ADD CONSTRAINT "FK_StockMovements_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES public."Warehouses"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6420 (class 2606 OID 21161)
-- Name: StockTakeItems FK_StockTakeItems_ProductVariants_ProductVariantId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockTakeItems"
    ADD CONSTRAINT "FK_StockTakeItems_ProductVariants_ProductVariantId" FOREIGN KEY ("ProductVariantId") REFERENCES public."ProductVariants"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6421 (class 2606 OID 21166)
-- Name: StockTakeItems FK_StockTakeItems_Products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockTakeItems"
    ADD CONSTRAINT "FK_StockTakeItems_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public."Products"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6422 (class 2606 OID 21171)
-- Name: StockTakeItems FK_StockTakeItems_StockTakes_StockTakeId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockTakeItems"
    ADD CONSTRAINT "FK_StockTakeItems_StockTakes_StockTakeId" FOREIGN KEY ("StockTakeId") REFERENCES public."StockTakes"("Id") ON DELETE CASCADE;


--
-- TOC entry 6419 (class 2606 OID 21142)
-- Name: StockTakes FK_StockTakes_Warehouses_WarehouseId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockTakes"
    ADD CONSTRAINT "FK_StockTakes_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES public."Warehouses"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6429 (class 2606 OID 21267)
-- Name: StockTransferItems FK_StockTransferItems_ProductVariants_ProductVariantId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockTransferItems"
    ADD CONSTRAINT "FK_StockTransferItems_ProductVariants_ProductVariantId" FOREIGN KEY ("ProductVariantId") REFERENCES public."ProductVariants"("Id");


--
-- TOC entry 6430 (class 2606 OID 21272)
-- Name: StockTransferItems FK_StockTransferItems_Products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockTransferItems"
    ADD CONSTRAINT "FK_StockTransferItems_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public."Products"("Id") ON DELETE CASCADE;


--
-- TOC entry 6431 (class 2606 OID 21277)
-- Name: StockTransferItems FK_StockTransferItems_StockTransfers_StockTransferId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockTransferItems"
    ADD CONSTRAINT "FK_StockTransferItems_StockTransfers_StockTransferId" FOREIGN KEY ("StockTransferId") REFERENCES public."StockTransfers"("Id") ON DELETE CASCADE;


--
-- TOC entry 6424 (class 2606 OID 21229)
-- Name: StockTransfers FK_StockTransfers_Users_ApprovedById; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockTransfers"
    ADD CONSTRAINT "FK_StockTransfers_Users_ApprovedById" FOREIGN KEY ("ApprovedById") REFERENCES public."Users"("Id");


--
-- TOC entry 6425 (class 2606 OID 21234)
-- Name: StockTransfers FK_StockTransfers_Users_CompletedById; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockTransfers"
    ADD CONSTRAINT "FK_StockTransfers_Users_CompletedById" FOREIGN KEY ("CompletedById") REFERENCES public."Users"("Id");


--
-- TOC entry 6426 (class 2606 OID 21239)
-- Name: StockTransfers FK_StockTransfers_Users_RequestedById; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockTransfers"
    ADD CONSTRAINT "FK_StockTransfers_Users_RequestedById" FOREIGN KEY ("RequestedById") REFERENCES public."Users"("Id");


--
-- TOC entry 6427 (class 2606 OID 21244)
-- Name: StockTransfers FK_StockTransfers_Warehouses_FromWarehouseId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockTransfers"
    ADD CONSTRAINT "FK_StockTransfers_Warehouses_FromWarehouseId" FOREIGN KEY ("FromWarehouseId") REFERENCES public."Warehouses"("Id") ON DELETE CASCADE;


--
-- TOC entry 6428 (class 2606 OID 21249)
-- Name: StockTransfers FK_StockTransfers_Warehouses_ToWarehouseId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StockTransfers"
    ADD CONSTRAINT "FK_StockTransfers_Warehouses_ToWarehouseId" FOREIGN KEY ("ToWarehouseId") REFERENCES public."Warehouses"("Id") ON DELETE CASCADE;


--
-- TOC entry 6583 (class 2606 OID 46667)
-- Name: StorefrontFooterLinks FK_StorefrontFooterLinks_StorefrontFooterSections_SectionId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."StorefrontFooterLinks"
    ADD CONSTRAINT "FK_StorefrontFooterLinks_StorefrontFooterSections_SectionId" FOREIGN KEY ("SectionId") REFERENCES public."StorefrontFooterSections"("Id") ON DELETE CASCADE;


--
-- TOC entry 6587 (class 2606 OID 46959)
-- Name: SupplierPayments FK_SupplierPayments_PurchaseInvoices_PurchaseInvoiceId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."SupplierPayments"
    ADD CONSTRAINT "FK_SupplierPayments_PurchaseInvoices_PurchaseInvoiceId" FOREIGN KEY ("PurchaseInvoiceId") REFERENCES public."PurchaseInvoices"("Id") ON DELETE SET NULL;


--
-- TOC entry 6588 (class 2606 OID 46964)
-- Name: SupplierPayments FK_SupplierPayments_Suppliers_SupplierId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."SupplierPayments"
    ADD CONSTRAINT "FK_SupplierPayments_Suppliers_SupplierId" FOREIGN KEY ("SupplierId") REFERENCES public."Suppliers"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6376 (class 2606 OID 20157)
-- Name: SupplierTransactions FK_SupplierTransactions_Suppliers_SupplierId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."SupplierTransactions"
    ADD CONSTRAINT "FK_SupplierTransactions_Suppliers_SupplierId" FOREIGN KEY ("SupplierId") REFERENCES public."Suppliers"("Id") ON DELETE CASCADE;


--
-- TOC entry 6373 (class 2606 OID 3022601)
-- Name: SystemSettings FK_SystemSettings_Tenants_TenantId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."SystemSettings"
    ADD CONSTRAINT "FK_SystemSettings_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES public."Tenants"("Id") ON DELETE CASCADE;


--
-- TOC entry 6483 (class 2606 OID 34838)
-- Name: TenantAutomationSettings FK_TenantAutomationSettings_Tenants_TenantId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."TenantAutomationSettings"
    ADD CONSTRAINT "FK_TenantAutomationSettings_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES public."Tenants"("Id") ON DELETE CASCADE;


--
-- TOC entry 6463 (class 2606 OID 2287840)
-- Name: Tenants FK_Tenants_Currencies_Currency; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Tenants"
    ADD CONSTRAINT "FK_Tenants_Currencies_Currency" FOREIGN KEY ("Currency") REFERENCES public."Currencies"("Code") ON DELETE RESTRICT;


--
-- TOC entry 6497 (class 2606 OID 35190)
-- Name: TreasuryAccounts FK_TreasuryAccounts_Account; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."TreasuryAccounts"
    ADD CONSTRAINT "FK_TreasuryAccounts_Account" FOREIGN KEY ("AccountId") REFERENCES public."LedgerAccounts"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6439 (class 2606 OID 21462)
-- Name: TreasuryTransactions FK_TreasuryTransactions_Users_CreatedByUserId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."TreasuryTransactions"
    ADD CONSTRAINT "FK_TreasuryTransactions_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES public."Users"("Id");


--
-- TOC entry 6440 (class 2606 OID 21484)
-- Name: VisaTransactions FK_VisaTransactions_Users_CreatedByUserId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."VisaTransactions"
    ADD CONSTRAINT "FK_VisaTransactions_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES public."Users"("Id");


--
-- TOC entry 6406 (class 2606 OID 20457)
-- Name: WarehouseStocks FK_WarehouseStocks_ProductVariants_ProductVariantId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."WarehouseStocks"
    ADD CONSTRAINT "FK_WarehouseStocks_ProductVariants_ProductVariantId" FOREIGN KEY ("ProductVariantId") REFERENCES public."ProductVariants"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6407 (class 2606 OID 20462)
-- Name: WarehouseStocks FK_WarehouseStocks_Products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."WarehouseStocks"
    ADD CONSTRAINT "FK_WarehouseStocks_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public."Products"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6408 (class 2606 OID 20467)
-- Name: WarehouseStocks FK_WarehouseStocks_Warehouses_WarehouseId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."WarehouseStocks"
    ADD CONSTRAINT "FK_WarehouseStocks_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES public."Warehouses"("Id") ON DELETE RESTRICT;


--
-- TOC entry 6489 (class 2606 OID 34969)
-- Name: WhatsAppRateLimits FK_WhatsAppRateLimits_Tenants; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."WhatsAppRateLimits"
    ADD CONSTRAINT "FK_WhatsAppRateLimits_Tenants" FOREIGN KEY ("TenantId") REFERENCES public."Tenants"("Id") ON DELETE CASCADE;


--
-- TOC entry 6481 (class 2606 OID 34706)
-- Name: whatsapp_accounts FK_whatsapp_accounts_Tenants_tenant_id; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.whatsapp_accounts
    ADD CONSTRAINT "FK_whatsapp_accounts_Tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES public."Tenants"("Id") ON DELETE CASCADE;


--
-- TOC entry 6482 (class 2606 OID 34736)
-- Name: whatsapp_message_templates FK_whatsapp_message_templates_Tenants_TenantId; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.whatsapp_message_templates
    ADD CONSTRAINT "FK_whatsapp_message_templates_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES public."Tenants"("Id") ON DELETE CASCADE;


--
-- TOC entry 6783 (class 0 OID 0)
-- Dependencies: 5
-- Name: SCHEMA public; Type: ACL; Schema: -; Owner: postgres
--

REVOKE USAGE ON SCHEMA public FROM PUBLIC;
GRANT ALL ON SCHEMA public TO PUBLIC;


-- Completed on 2026-07-23 21:14:58

--
-- PostgreSQL database dump complete
--

\unrestrict isfYeecELcvWLkrYfEgL62I3cWFEAEbAw22UY1ozEMOhTBBFIdRlovhMEkpvpAS

