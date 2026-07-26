BEGIN;

ALTER TABLE "CommerceHubSubOrderItems"
    ADD COLUMN IF NOT EXISTS "Status" integer NOT NULL DEFAULT 0;

ALTER TABLE "CommerceHubSubOrderItems"
    ADD COLUMN IF NOT EXISTS "DeliveredAt" timestamp with time zone NULL;

UPDATE "CommerceHubSubOrderItems" AS items
SET "Status" = CASE
    WHEN subOrders."Status" IN (4, 5) THEN 2
    WHEN subOrders."Status" IN (6, 7) THEN 3
    WHEN subOrders."Status" = 3 THEN 1
    ELSE 0
END
FROM "CommerceHubSubOrders" AS subOrders
WHERE items."SubOrderId" = subOrders."Id";

WITH delivered_history AS (
    SELECT
        "SubOrderId",
        MAX("ChangedAtUtc") AS "DeliveredAt"
    FROM "CommerceHubSubOrderStatusHistory"
    WHERE NOT "IsDeleted"
      AND "NewStatus" IN (4, 5)
    GROUP BY "SubOrderId"
)
UPDATE "CommerceHubSubOrderItems" AS items
SET "DeliveredAt" = COALESCE(delivered_history."DeliveredAt", subOrders."UpdatedAt", subOrders."CreatedAt")
FROM "CommerceHubSubOrders" AS subOrders
LEFT JOIN delivered_history
    ON delivered_history."SubOrderId" = subOrders."Id"
WHERE items."SubOrderId" = subOrders."Id"
  AND items."Status" = 2
  AND items."DeliveredAt" IS NULL;

ALTER TABLE "CommerceHubRefundRequests"
    ADD COLUMN IF NOT EXISTS "IdempotencyKey" character varying(100) NULL;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommerceHubRefundRequests_IdempotencyKey"
    ON "CommerceHubRefundRequests" ("IdempotencyKey")
    WHERE "IdempotencyKey" IS NOT NULL;

COMMIT;
