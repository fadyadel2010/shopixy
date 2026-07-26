DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM "CommerceHubPayouts"
        WHERE "ExternalReferenceId" IS NOT NULL
          AND btrim("ExternalReferenceId") <> ''
          AND "Status" <> 3
          AND "IsDeleted" = false
        GROUP BY "ExternalReferenceId"
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'CommerceHubPayouts contains duplicate live ExternalReferenceId values across vendors. Reconcile duplicate payout evidence before applying S73.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM "CommerceHubPayouts"
        WHERE "BankTransactionId" IS NOT NULL
          AND btrim("BankTransactionId") <> ''
          AND "Status" <> 3
          AND "IsDeleted" = false
        GROUP BY "BankTransactionId"
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'CommerceHubPayouts contains duplicate live BankTransactionId values across vendors. Reconcile duplicate payout evidence before applying S73.';
    END IF;
END $$;

DROP INDEX IF EXISTS "UX_CommerceHubPayouts_Completed_ExternalReferenceId";
DROP INDEX IF EXISTS "UX_CommerceHubPayouts_Completed_BankTransactionId";
DROP INDEX IF EXISTS "UX_CommerceHubPayouts_Live_ExternalReferenceId";
DROP INDEX IF EXISTS "UX_CommerceHubPayouts_Live_BankTransactionId";

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubPayouts_Live_ExternalReferenceId"
    ON "CommerceHubPayouts" ("ExternalReferenceId")
    WHERE "ExternalReferenceId" IS NOT NULL
      AND "ExternalReferenceId" <> ''
      AND "Status" <> 3
      AND "IsDeleted" = false;

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubPayouts_Live_BankTransactionId"
    ON "CommerceHubPayouts" ("BankTransactionId")
    WHERE "BankTransactionId" IS NOT NULL
      AND "BankTransactionId" <> ''
      AND "Status" <> 3
      AND "IsDeleted" = false;
