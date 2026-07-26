-- S67: Track provider-confirmed CommerceHub refunds before accounting reconciliation.
-- Provider refund webhooks can arrive before the compensation/accounting worker
-- folds the refund into CommerceHubPayments.RefundedAmount. This provisional
-- bucket prevents later cumulative webhooks from being accepted as additional
-- refundable cash while preserving the canonical RefundedAmount lifecycle.

ALTER TABLE "CommerceHubPayments"
    ADD COLUMN IF NOT EXISTS "ProvisionalRefundedAmount" numeric(18,2) NOT NULL DEFAULT 0;

UPDATE "CommerceHubPayments"
SET "ProvisionalRefundedAmount" = 0
WHERE "ProvisionalRefundedAmount" IS NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_CommerceHubPayments_ProvisionalRefundedAmount_Range'
    ) THEN
        ALTER TABLE "CommerceHubPayments"
            ADD CONSTRAINT "CK_CommerceHubPayments_ProvisionalRefundedAmount_Range"
            CHECK (
                "ProvisionalRefundedAmount" >= 0
                AND ROUND("RefundedAmount" + "ProvisionalRefundedAmount", 2) <= ROUND("Amount", 2)
            )
            NOT VALID;
    END IF;
END $$;

ALTER TABLE "CommerceHubPayments"
    VALIDATE CONSTRAINT "CK_CommerceHubPayments_ProvisionalRefundedAmount_Range";
