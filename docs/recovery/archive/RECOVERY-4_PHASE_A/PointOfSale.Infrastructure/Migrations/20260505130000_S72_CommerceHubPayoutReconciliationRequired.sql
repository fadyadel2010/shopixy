-- S72: Quarantine stale CommerceHub payout handoffs into an explicit
-- ReconciliationRequired state instead of leaving Processing as an indefinite limbo.
-- Enum mapping:
--   Pending = 0
--   Processing = 1
--   Completed = 2
--   Failed = 3
--   ReconciliationRequired = 4

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_CommerceHubPayouts_Processing_ExternalIntent'
    ) THEN
        ALTER TABLE "CommerceHubPayouts"
            DROP CONSTRAINT "CK_CommerceHubPayouts_Processing_ExternalIntent";
    END IF;

    ALTER TABLE "CommerceHubPayouts"
        ADD CONSTRAINT "CK_CommerceHubPayouts_Processing_ExternalIntent"
        CHECK (
            ("Status" <> 1 AND "Status" <> 4) OR
            NULLIF(btrim("ExternalTransferIdempotencyKey"), '') IS NOT NULL
        );
END $$;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_CommerceHubPayouts_Processing_HandoffCommitted'
    ) THEN
        ALTER TABLE "CommerceHubPayouts"
            DROP CONSTRAINT "CK_CommerceHubPayouts_Processing_HandoffCommitted";
    END IF;

    ALTER TABLE "CommerceHubPayouts"
        ADD CONSTRAINT "CK_CommerceHubPayouts_Processing_HandoffCommitted"
        CHECK (
            ("Status" <> 1 AND "Status" <> 4) OR
            (
                NULLIF(btrim("ExternalTransferHandoffToken"), '') IS NOT NULL AND
                "ExternalTransferHandoffCommittedAtUtc" IS NOT NULL
            )
        );
END $$;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_CommerceHubPayouts_Terminal_HandoffCommitted'
    ) THEN
        ALTER TABLE "CommerceHubPayouts"
            DROP CONSTRAINT "CK_CommerceHubPayouts_Terminal_HandoffCommitted";
    END IF;

    ALTER TABLE "CommerceHubPayouts"
        ADD CONSTRAINT "CK_CommerceHubPayouts_Terminal_HandoffCommitted"
        CHECK (
            ("Status" <> 2 AND "Status" <> 4) OR
            (
                NULLIF(btrim("ExternalTransferHandoffToken"), '') IS NOT NULL AND
                "ExternalTransferHandoffCommittedAtUtc" IS NOT NULL
            )
        );
END $$;

DROP INDEX IF EXISTS "UX_CommerceHubPayouts_OneProcessingPerTenant";

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubPayouts_OneProcessingPerTenant"
    ON "CommerceHubPayouts" ("TenantId")
    WHERE ("Status" = 1 OR "Status" = 4) AND "IsDeleted" = false;
