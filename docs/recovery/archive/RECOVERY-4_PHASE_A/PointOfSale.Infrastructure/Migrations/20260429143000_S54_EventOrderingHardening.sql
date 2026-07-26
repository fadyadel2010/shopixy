-- S54: Event ordering hardening
-- Critical integration events must carry an aggregate ordering key, and generic outbox replay
-- needs an index that can cheaply detect older unpublished messages for the same aggregate.

ALTER TABLE "OutboxMessages"
    DROP CONSTRAINT IF EXISTS "CK_OutboxMessages_CriticalEvents_AggregateId_Required";

ALTER TABLE "OutboxMessages"
    ADD CONSTRAINT "CK_OutboxMessages_CriticalEvents_AggregateId_Required"
    CHECK (
        ("AggregateId" IS NOT NULL AND "AggregateId" <> '00000000-0000-0000-0000-000000000000'::uuid) OR NOT (
            lower("EventType") LIKE '%order%' OR
            lower("EventType") LIKE '%payment%' OR
            lower("EventType") LIKE '%inventory%' OR
            lower("EventType") LIKE '%stock%' OR
            lower("EventType") LIKE '%product%' OR
            lower("EventType") LIKE '%variant%' OR
            lower("EventType") LIKE '%refund%' OR
            lower("EventType") LIKE '%payout%' OR
            lower("EventType") LIKE '%settlement%' OR
            lower("EventType") LIKE '%currency%' OR
            lower("EventType") LIKE '%commercehub%' OR
            lower("EventType") LIKE '%marketplace%' OR
            lower("EventType") LIKE '%affiliate%' OR
            lower("EventType") LIKE '%coupon%' OR
            lower("EventType") LIKE '%promotion%' OR
            lower("EventType") LIKE '%tenant%' OR
            lower("EventType") LIKE '%storeconfig%' OR
            lower("EventType") LIKE '%shippingmethod%' OR
            lower("EventType") LIKE '%shipment%' OR
            lower("EventType") LIKE '%customer%' OR
            lower("EventType") LIKE '%checkout%' OR
            lower("EventType") LIKE '%ledger%' OR
            lower("EventType") LIKE '%accounting%' OR
            lower("EventType") LIKE '%wallet%' OR
            lower("EventType") LIKE '%cod%' OR
            lower("EventType") LIKE '%providertrust%'
        )
    ) NOT VALID;

ALTER TABLE "OutboxMessages"
    VALIDATE CONSTRAINT "CK_OutboxMessages_CriticalEvents_AggregateId_Required";

CREATE INDEX IF NOT EXISTS "IX_OutboxMessages_CriticalAggregateOrdering"
    ON "OutboxMessages" ("TenantId", "AggregateId", "CreatedAtUtc", "Id")
    WHERE "ProcessedAtUtc" IS NULL
      AND "TenantId" IS NOT NULL
      AND "AggregateId" IS NOT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260429143000_S54_EventOrderingHardening', '9.0.1'
WHERE NOT EXISTS (
    SELECT 1
    FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260429143000_S54_EventOrderingHardening'
);
