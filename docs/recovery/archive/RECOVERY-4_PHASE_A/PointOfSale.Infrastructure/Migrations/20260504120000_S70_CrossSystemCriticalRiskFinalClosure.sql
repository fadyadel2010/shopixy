-- S70: Final production closure for cross-system critical-risk findings.
-- Payout executors must not be able to reconcile external money movement that
-- started before the POS Processing transition was durably committed.
-- Provider-success webhooks also retain the local CommerceHubPayment.Id when
-- the provider sends it back, so reconciliation can link captured money back
-- to the exact payment row instead of relying only on provider references.

ALTER TABLE "CommerceHubUnresolvedPaymentWebhooks"
    ADD COLUMN IF NOT EXISTS "LocalPaymentId" uuid NULL;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubUnresolvedPaymentWebhooks_LocalPaymentId"
ON "CommerceHubUnresolvedPaymentWebhooks" ("LocalPaymentId")
WHERE "LocalPaymentId" IS NOT NULL;

ALTER TABLE "CommerceHubPayouts"
    ADD COLUMN IF NOT EXISTS "ExternalTransferHandoffToken" character varying(128) NULL;

ALTER TABLE "CommerceHubPayouts"
    ADD COLUMN IF NOT EXISTS "ExternalTransferHandoffCommittedAtUtc" timestamp with time zone NULL;

UPDATE "CommerceHubPayouts"
SET "ExternalTransferHandoffToken" = COALESCE(
        NULLIF(btrim("ExternalTransferHandoffToken"), ''),
        md5(COALESCE("ExternalTransferIdempotencyKey", '') || ':' || "Id"::text || ':' || clock_timestamp()::text)
    ),
    "ExternalTransferHandoffCommittedAtUtc" = COALESCE(
        "ExternalTransferHandoffCommittedAtUtc",
        "ProcessedAtUtc",
        "CreatedAt",
        NOW()
    )
WHERE "Status" IN (1, 2)
  AND (
        NULLIF(btrim(COALESCE("ExternalTransferHandoffToken", '')), '') IS NULL
        OR "ExternalTransferHandoffCommittedAtUtc" IS NULL
  );

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_CommerceHubPayouts_Processing_HandoffCommitted'
    ) THEN
        ALTER TABLE "CommerceHubPayouts"
            ADD CONSTRAINT "CK_CommerceHubPayouts_Processing_HandoffCommitted"
            CHECK (
                "Status" <> 1
                OR (
                    NULLIF(btrim("ExternalTransferHandoffToken"), '') IS NOT NULL
                    AND "ExternalTransferHandoffCommittedAtUtc" IS NOT NULL
                )
            )
            NOT VALID;
    END IF;

    ALTER TABLE "CommerceHubPayouts"
        DROP CONSTRAINT IF EXISTS "CK_CommerceHubPayouts_Terminal_HandoffCommitted";

    ALTER TABLE "CommerceHubPayouts"
        ADD CONSTRAINT "CK_CommerceHubPayouts_Terminal_HandoffCommitted"
        CHECK (
            "Status" <> 2
            OR (
                NULLIF(btrim("ExternalTransferHandoffToken"), '') IS NOT NULL
                AND "ExternalTransferHandoffCommittedAtUtc" IS NOT NULL
            )
        )
        NOT VALID;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_CommerceHubPayouts_NoRefundFailureAfterHandoff'
    ) THEN
        ALTER TABLE "CommerceHubPayouts"
            ADD CONSTRAINT "CK_CommerceHubPayouts_NoRefundFailureAfterHandoff"
            CHECK (
                NOT (
                    "Status" = 3
                    AND "ExternalTransferHandoffCommittedAtUtc" IS NOT NULL
                    AND "ProcessedBy" IN ('system:refund', 'system:refund-control')
                )
            )
            NOT VALID;
    END IF;
END $$;

ALTER TABLE "CommerceHubPayouts"
    VALIDATE CONSTRAINT "CK_CommerceHubPayouts_Processing_HandoffCommitted";

ALTER TABLE "CommerceHubPayouts"
    VALIDATE CONSTRAINT "CK_CommerceHubPayouts_Terminal_HandoffCommitted";

ALTER TABLE "CommerceHubPayouts"
    VALIDATE CONSTRAINT "CK_CommerceHubPayouts_NoRefundFailureAfterHandoff";

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubPayouts_ExternalTransferHandoffToken"
ON "CommerceHubPayouts" ("TenantId", "ExternalTransferHandoffToken")
WHERE "ExternalTransferHandoffToken" IS NOT NULL
  AND "ExternalTransferHandoffToken" <> ''
  AND "IsDeleted" = false;

-- Only one externally executable payout may be Processing per vendor. Application
-- code checks this, but the database must own the invariant under concurrency.
CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubPayouts_OneProcessingPerTenant"
ON "CommerceHubPayouts" ("TenantId")
WHERE "Status" = 1
  AND "IsDeleted" = false;

-- Payout creation locks earnings by tenant/id; keep that lookup hot under
-- concurrent vendor payout requests.
CREATE INDEX IF NOT EXISTS "IX_CommerceHubVendorEarnings_PayoutCreationLock"
ON "CommerceHubVendorEarnings" ("TenantId", "Id")
WHERE "IsDeleted" = false;
