-- S56: Data authority and event ordering closure
-- Enforces source ownership on order identity, requires ordered financial postings,
-- and makes marketplace event dispatch version-first instead of timestamp-first.

ALTER TABLE "Orders"
    DROP CONSTRAINT IF EXISTS "CK_Orders_CustomerAuthorityBoundary";

ALTER TABLE "Orders"
    ADD CONSTRAINT "CK_Orders_CustomerAuthorityBoundary"
    CHECK (
        ("CustomerId" IS NULL OR "OnlineCustomerId" IS NULL) AND
        ("Source" <> 0 OR "CustomerId" IS NULL)
    ) NOT VALID;

ALTER TABLE "Orders"
    VALIDATE CONSTRAINT "CK_Orders_CustomerAuthorityBoundary";

ALTER TABLE "AccountingOutboxMessages"
    DROP CONSTRAINT IF EXISTS "CK_AccountingOutbox_OrderFinancialEvents_AggregateVersion";

ALTER TABLE "AccountingOutboxMessages"
    ADD CONSTRAINT "CK_AccountingOutbox_OrderFinancialEvents_AggregateVersion"
    CHECK (
        "EventType" NOT IN ('OnlinePayment', 'CodDelivered', 'WalletApproved', 'EcommerceRefund') OR
        ("AggregateVersion" IS NOT NULL AND "AggregateVersion" > 0)
    ) NOT VALID;

ALTER TABLE "AccountingOutboxMessages"
    VALIDATE CONSTRAINT "CK_AccountingOutbox_OrderFinancialEvents_AggregateVersion";

DROP INDEX IF EXISTS "IX_AccountingOutbox_AggregateVersionOrdering";

CREATE INDEX IF NOT EXISTS "IX_AccountingOutbox_AggregateVersionOrdering"
    ON "AccountingOutboxMessages" ("TenantId", "BusinessEntityId", "AggregateVersion", "OccurredAtUtc", "CreatedAt", "Id")
    WHERE "ProcessedAtUtc" IS NULL AND "IsFailed" = false;

DROP INDEX IF EXISTS "IX_AccountingOutbox_AggregateVersionBlockers";

CREATE INDEX IF NOT EXISTS "IX_AccountingOutbox_AggregateVersionBlockers"
    ON "AccountingOutboxMessages" ("TenantId", "BusinessEntityId", "AggregateVersion", "OccurredAtUtc", "CreatedAt", "Id", "ProcessedAtUtc", "IsFailed", "EventType")
    WHERE "ProcessedAtUtc" IS NULL;

ALTER TABLE "OutboxMessages"
    ADD COLUMN IF NOT EXISTS "EventVersion" integer NOT NULL DEFAULT 0;

ALTER TABLE "OutboxMessages"
    DROP CONSTRAINT IF EXISTS "CK_OutboxMessages_CriticalEvents_EventVersion_Required";

ALTER TABLE "OutboxMessages"
    ADD CONSTRAINT "CK_OutboxMessages_CriticalEvents_EventVersion_Required"
    CHECK (
        "EventVersion" > 0 OR NOT (
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
    VALIDATE CONSTRAINT "CK_OutboxMessages_CriticalEvents_EventVersion_Required";

DROP INDEX IF EXISTS "IX_OutboxMessages_CriticalAggregateOrdering";

CREATE INDEX IF NOT EXISTS "IX_OutboxMessages_CriticalAggregateOrdering"
    ON "OutboxMessages" ("TenantId", "AggregateId", "EventVersion", "CreatedAtUtc", "Id")
    WHERE "ProcessedAtUtc" IS NULL
      AND "TenantId" IS NOT NULL
      AND "AggregateId" IS NOT NULL;

ALTER TABLE "MarketplaceEventOutboxMessages"
    ADD COLUMN IF NOT EXISTS "AggregateRootId" uuid;

UPDATE "MarketplaceEventOutboxMessages"
SET "AggregateRootId" = "EntityId"
WHERE "AggregateRootId" IS NULL;

ALTER TABLE "MarketplaceEventOutboxMessages"
    ALTER COLUMN "AggregateRootId" SET NOT NULL;

ALTER TABLE "MarketplaceEventOutboxMessages"
    DROP CONSTRAINT IF EXISTS "CK_MarketplaceEventOutbox_AggregateRootId_NotEmpty";

ALTER TABLE "MarketplaceEventOutboxMessages"
    ADD CONSTRAINT "CK_MarketplaceEventOutbox_AggregateRootId_NotEmpty"
    CHECK (
        "AggregateRootId" IS NOT NULL AND
        "AggregateRootId" <> '00000000-0000-0000-0000-000000000000'::uuid
    ) NOT VALID;

ALTER TABLE "MarketplaceEventOutboxMessages"
    VALIDATE CONSTRAINT "CK_MarketplaceEventOutbox_AggregateRootId_NotEmpty";

ALTER TABLE "MarketplaceEventOutboxMessages"
    DROP CONSTRAINT IF EXISTS "CK_MarketplaceEventOutbox_Version_Positive";

ALTER TABLE "MarketplaceEventOutboxMessages"
    ADD CONSTRAINT "CK_MarketplaceEventOutbox_Version_Positive"
    CHECK ("Version" > 0) NOT VALID;

ALTER TABLE "MarketplaceEventOutboxMessages"
    VALIDATE CONSTRAINT "CK_MarketplaceEventOutbox_Version_Positive";

DROP INDEX IF EXISTS "IX_MarketplaceEventOutbox_AggregateOrdering";

CREATE INDEX IF NOT EXISTS "IX_MarketplaceEventOutbox_AggregateOrdering"
    ON "MarketplaceEventOutboxMessages" ("TenantId", "AggregateRootId", "PublishedAtUtc", "Version", "OccurredAtUtc", "CreatedAt", "Id")
    WHERE "PublishedAtUtc" IS NULL AND "IsFailed" = false;

DROP INDEX IF EXISTS "IX_MarketplaceEventOutbox_AggregateOrderingBlockers";

CREATE INDEX IF NOT EXISTS "IX_MarketplaceEventOutbox_AggregateOrderingBlockers"
    ON "MarketplaceEventOutboxMessages" ("TenantId", "AggregateRootId", "Version", "OccurredAtUtc", "CreatedAt", "Id")
    WHERE "PublishedAtUtc" IS NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260429153000_S56_DataAuthorityAndEventOrderingClosure', '9.0.1'
WHERE NOT EXISTS (
    SELECT 1
    FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260429153000_S56_DataAuthorityAndEventOrderingClosure'
);
