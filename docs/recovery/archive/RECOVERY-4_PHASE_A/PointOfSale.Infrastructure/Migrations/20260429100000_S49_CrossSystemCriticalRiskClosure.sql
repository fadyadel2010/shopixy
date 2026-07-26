-- S49: Cross-system critical risk closure
-- Fails closed on historical payout evidence gaps and validates financial boundary constraints.

DO $$
DECLARE
    violating_count integer;
BEGIN
    SELECT COUNT(*)
    INTO violating_count
    FROM "CommerceHubPayouts"
    WHERE "Status" = 2
      AND NULLIF(btrim("ExternalReferenceId"), '') IS NULL
      AND NULLIF(btrim("BankTransactionId"), '') IS NULL;

    IF violating_count > 0 THEN
        RAISE EXCEPTION 'Completed CommerceHub payouts without immutable transfer evidence: %. Reconcile historical payouts before applying S49.', violating_count;
    END IF;
END $$;

ALTER TABLE "CommerceHubPayouts"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubPayouts_NonManual_Completed_Evidence";

ALTER TABLE "CommerceHubPayouts"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubPayouts_Completed_Evidence";

ALTER TABLE "CommerceHubPayouts"
    ADD CONSTRAINT "CK_CommerceHubPayouts_Completed_Evidence"
    CHECK (
        "Status" <> 2 OR
        NULLIF(btrim("ExternalReferenceId"), '') IS NOT NULL OR
        NULLIF(btrim("BankTransactionId"), '') IS NOT NULL
    ) NOT VALID;

DO $$
DECLARE
    constraint_row record;
BEGIN
    FOR constraint_row IN
        SELECT *
        FROM (VALUES
            ('CommerceHubPayments', 'CK_CommerceHubPayments_Amount_Positive'),
            ('CommerceHubPayments', 'CK_CommerceHubPayments_RefundedAmount_Range'),
            ('CommerceHubPayments', 'CK_CommerceHubPayments_Paid_State_Complete'),
            ('CommerceHubPayouts', 'CK_CommerceHubPayouts_Amount_Positive'),
            ('CommerceHubPayouts', 'CK_CommerceHubPayouts_Completed_State_Complete'),
            ('CommerceHubPayouts', 'CK_CommerceHubPayouts_Completed_Evidence'),
            ('CommerceHubRefunds', 'CK_CommerceHubRefunds_Amount_Positive'),
            ('CommerceHubRefunds', 'CK_CommerceHubRefunds_Reversals_NonNegative'),
            ('CommerceHubRefunds', 'CK_CommerceHubRefunds_Reversals_Total_Amount'),
            ('CommerceHubRefunds', 'CK_CommerceHubRefunds_Completed_State_Complete'),
            ('CommerceHubVendorEarnings', 'CK_CHVendorEarnings_PayoutAllocation_NonNegative'),
            ('CommerceHubVendorEarnings', 'CK_CHVendorEarnings_PayoutAllocation_NotOverAllocated')
        ) AS constraints_to_validate(table_name, constraint_name)
    LOOP
        IF EXISTS (
            SELECT 1
            FROM pg_constraint constraint_meta
            INNER JOIN pg_class table_meta ON table_meta.oid = constraint_meta.conrelid
            WHERE table_meta.relname = constraint_row.table_name
              AND constraint_meta.conname = constraint_row.constraint_name
        ) THEN
            EXECUTE format(
                'ALTER TABLE %I VALIDATE CONSTRAINT %I',
                constraint_row.table_name,
                constraint_row.constraint_name);
        END IF;
    END LOOP;
END $$;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260429100000_S49_CrossSystemCriticalRiskClosure', '9.0.1'
WHERE NOT EXISTS (
    SELECT 1
    FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260429100000_S49_CrossSystemCriticalRiskClosure'
);
