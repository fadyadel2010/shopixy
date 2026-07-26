-- S75: Preserve externally confirmed CommerceHub refund money movement as
-- reconciliation-required instead of terminal failed local state.
-- Enum mapping:
--   Pending = 0
--   Processing = 1
--   Completed = 2
--   Failed = 3
--   ReconciliationRequired = 4

DROP INDEX IF EXISTS "IX_CommerceHubRefunds_ExternalReconciliation";

CREATE INDEX IF NOT EXISTS "IX_CommerceHubRefunds_ExternalReconciliation"
    ON "CommerceHubRefunds" (
        "TenantId",
        "Status",
        "ProcessingStartedAtUtc",
        "CreatedAt"
    )
    WHERE ("Status" = 1 OR "Status" = 4)
      AND "IsDeleted" = false;
