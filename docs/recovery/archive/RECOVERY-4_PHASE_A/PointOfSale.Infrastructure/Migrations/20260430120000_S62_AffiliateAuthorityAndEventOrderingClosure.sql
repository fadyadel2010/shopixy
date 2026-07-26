-- S62: Data authority and event ordering closure.
-- Align the legacy OutboxMessages constraints with the runtime authority classifier:
-- high-volume analytics events are not authoritative even when their names contain
-- Affiliate or Promotion fragments.

ALTER TABLE "OutboxMessages"
    DROP CONSTRAINT IF EXISTS "CK_OutboxMessages_CriticalEvents_TenantId_Required";

ALTER TABLE "OutboxMessages"
    ADD CONSTRAINT "CK_OutboxMessages_CriticalEvents_TenantId_Required"
    CHECK (
        ("TenantId" IS NOT NULL AND "TenantId" <> '00000000-0000-0000-0000-000000000000')
        OR NOT (
            (
                lower("EventType") LIKE '%order%' OR
                lower("EventType") LIKE '%payment%' OR
                lower("EventType") LIKE '%inventory%' OR
                lower("EventType") LIKE '%stock%' OR
                lower("EventType") LIKE '%product%' OR
                lower("EventType") LIKE '%variant%' OR
                lower("EventType") LIKE '%refund%' OR
                lower("EventType") LIKE '%payout%' OR
                lower("EventType") LIKE '%settlement%' OR
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
                lower("EventType") LIKE '%currency%' OR
                lower("EventType") LIKE '%providertrust%'
            )
            AND lower("EventType") NOT LIKE '%affiliatelinkclickedevent%'
            AND lower("EventType") NOT LIKE '%lowstockalertevent%'
            AND lower("EventType") NOT LIKE '%onlinecustomeremailverificationrequestedevent%'
            AND lower("EventType") NOT LIKE '%promotionseenevent%'
            AND lower("EventType") NOT LIKE '%promotionclickedevent%'
            AND lower("EventType") NOT LIKE '%promotionconvertedevent%'
            AND lower("EventType") NOT LIKE '%promotionboostappliedevent%'
            AND lower("EventType") NOT LIKE '%whatsapporderconfirmationrequestedevent%'
            AND lower("EventType") NOT LIKE '%whatsapppaymentapprovalrequestedevent%'
            AND lower("EventType") NOT LIKE '%whatsapppaymentapprovednotificationevent%'
            AND lower("EventType") NOT LIKE '%whatsapppaymentrejectednotificationevent%'
        )
    ) NOT VALID;

ALTER TABLE "OutboxMessages"
    DROP CONSTRAINT IF EXISTS "CK_OutboxMessages_CriticalEvents_AggregateId_Required";

ALTER TABLE "OutboxMessages"
    ADD CONSTRAINT "CK_OutboxMessages_CriticalEvents_AggregateId_Required"
    CHECK (
        ("AggregateId" IS NOT NULL AND "AggregateId" <> '00000000-0000-0000-0000-000000000000')
        OR NOT (
            (
                lower("EventType") LIKE '%order%' OR
                lower("EventType") LIKE '%payment%' OR
                lower("EventType") LIKE '%inventory%' OR
                lower("EventType") LIKE '%stock%' OR
                lower("EventType") LIKE '%product%' OR
                lower("EventType") LIKE '%variant%' OR
                lower("EventType") LIKE '%refund%' OR
                lower("EventType") LIKE '%payout%' OR
                lower("EventType") LIKE '%settlement%' OR
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
                lower("EventType") LIKE '%currency%' OR
                lower("EventType") LIKE '%providertrust%'
            )
            AND lower("EventType") NOT LIKE '%affiliatelinkclickedevent%'
            AND lower("EventType") NOT LIKE '%lowstockalertevent%'
            AND lower("EventType") NOT LIKE '%onlinecustomeremailverificationrequestedevent%'
            AND lower("EventType") NOT LIKE '%promotionseenevent%'
            AND lower("EventType") NOT LIKE '%promotionclickedevent%'
            AND lower("EventType") NOT LIKE '%promotionconvertedevent%'
            AND lower("EventType") NOT LIKE '%promotionboostappliedevent%'
            AND lower("EventType") NOT LIKE '%whatsapporderconfirmationrequestedevent%'
            AND lower("EventType") NOT LIKE '%whatsapppaymentapprovalrequestedevent%'
            AND lower("EventType") NOT LIKE '%whatsapppaymentapprovednotificationevent%'
            AND lower("EventType") NOT LIKE '%whatsapppaymentrejectednotificationevent%'
        )
    ) NOT VALID;

ALTER TABLE "OutboxMessages"
    DROP CONSTRAINT IF EXISTS "CK_OutboxMessages_CriticalEvents_EventVersion_Required";

ALTER TABLE "OutboxMessages"
    ADD CONSTRAINT "CK_OutboxMessages_CriticalEvents_EventVersion_Required"
    CHECK (
        "EventVersion" > 0
        OR NOT (
            (
                lower("EventType") LIKE '%order%' OR
                lower("EventType") LIKE '%payment%' OR
                lower("EventType") LIKE '%inventory%' OR
                lower("EventType") LIKE '%stock%' OR
                lower("EventType") LIKE '%product%' OR
                lower("EventType") LIKE '%variant%' OR
                lower("EventType") LIKE '%refund%' OR
                lower("EventType") LIKE '%payout%' OR
                lower("EventType") LIKE '%settlement%' OR
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
                lower("EventType") LIKE '%currency%' OR
                lower("EventType") LIKE '%providertrust%'
            )
            AND lower("EventType") NOT LIKE '%affiliatelinkclickedevent%'
            AND lower("EventType") NOT LIKE '%lowstockalertevent%'
            AND lower("EventType") NOT LIKE '%onlinecustomeremailverificationrequestedevent%'
            AND lower("EventType") NOT LIKE '%promotionseenevent%'
            AND lower("EventType") NOT LIKE '%promotionclickedevent%'
            AND lower("EventType") NOT LIKE '%promotionconvertedevent%'
            AND lower("EventType") NOT LIKE '%promotionboostappliedevent%'
            AND lower("EventType") NOT LIKE '%whatsapporderconfirmationrequestedevent%'
            AND lower("EventType") NOT LIKE '%whatsapppaymentapprovalrequestedevent%'
            AND lower("EventType") NOT LIKE '%whatsapppaymentapprovednotificationevent%'
            AND lower("EventType") NOT LIKE '%whatsapppaymentrejectednotificationevent%'
        )
    ) NOT VALID;

ALTER TABLE "OutboxMessages"
    VALIDATE CONSTRAINT "CK_OutboxMessages_CriticalEvents_TenantId_Required";

ALTER TABLE "OutboxMessages"
    VALIDATE CONSTRAINT "CK_OutboxMessages_CriticalEvents_AggregateId_Required";

ALTER TABLE "OutboxMessages"
    VALIDATE CONSTRAINT "CK_OutboxMessages_CriticalEvents_EventVersion_Required";

-- Dedicated stock sync outbox must carry the same monotonic source version as
-- product/variant projections. Timestamp ordering remains a tie-breaker only.
ALTER TABLE "StockSyncOutboxMessages"
    ADD COLUMN IF NOT EXISTS "EventVersion" integer NOT NULL DEFAULT 1;

ALTER TABLE "StockSyncOutboxMessages"
    DROP CONSTRAINT IF EXISTS "CK_StockSyncOutbox_EventVersion_Positive";

ALTER TABLE "StockSyncOutboxMessages"
    ADD CONSTRAINT "CK_StockSyncOutbox_EventVersion_Positive"
    CHECK ("EventVersion" > 0) NOT VALID;

DROP INDEX IF EXISTS "IX_StockSyncOutbox_VariantOrdering";
DROP INDEX IF EXISTS "IX_StockSyncOutbox_VariantOrderingBlockers";

CREATE INDEX IF NOT EXISTS "IX_StockSyncOutbox_VariantOrdering"
    ON "StockSyncOutboxMessages"
        ("TenantId", "VariantId", "PublishedAtUtc", "EventVersion", "StockUpdatedAtUtc", "OccurredAtUtc", "CreatedAt", "Id")
    WHERE "PublishedAtUtc" IS NULL AND "IsFailed" = false;

CREATE INDEX IF NOT EXISTS "IX_StockSyncOutbox_VariantOrderingBlockers"
    ON "StockSyncOutboxMessages"
        ("TenantId", "VariantId", "EventVersion", "StockUpdatedAtUtc", "OccurredAtUtc", "CreatedAt", "Id")
    WHERE "PublishedAtUtc" IS NULL;

ALTER TABLE "StockSyncOutboxMessages"
    VALIDATE CONSTRAINT "CK_StockSyncOutbox_EventVersion_Positive";

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260430120000_S62_AffiliateAuthorityAndEventOrderingClosure', '9.0.1'
WHERE NOT EXISTS (
    SELECT 1
    FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260430120000_S62_AffiliateAuthorityAndEventOrderingClosure'
);
