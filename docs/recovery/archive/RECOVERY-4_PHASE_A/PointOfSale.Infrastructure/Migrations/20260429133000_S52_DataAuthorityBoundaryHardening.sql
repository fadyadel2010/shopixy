-- S52: Data authority boundary hardening
-- These constraints close authority gaps that EF SaveChanges guards cannot protect from raw SQL/bulk writes.

ALTER TABLE "MarketplaceEventOutboxMessages"
    DROP CONSTRAINT IF EXISTS "CK_MarketplaceEventOutbox_TenantId_Required";

ALTER TABLE "MarketplaceEventOutboxMessages"
    ADD CONSTRAINT "CK_MarketplaceEventOutbox_TenantId_Required"
    CHECK ("TenantId" IS NOT NULL AND "TenantId" <> '00000000-0000-0000-0000-000000000000'::uuid) NOT VALID;

ALTER TABLE "MarketplaceEventOutboxMessages"
    VALIDATE CONSTRAINT "CK_MarketplaceEventOutbox_TenantId_Required";

ALTER TABLE "MarketplaceEventOutboxMessages"
    ADD COLUMN IF NOT EXISTS "AggregateRootId" uuid;

UPDATE "MarketplaceEventOutboxMessages"
SET "AggregateRootId" = "EntityId"
WHERE "AggregateRootId" IS NULL;

ALTER TABLE "MarketplaceEventOutboxMessages"
    DROP CONSTRAINT IF EXISTS "CK_MarketplaceEventOutbox_AggregateRootId_NotEmpty";

ALTER TABLE "MarketplaceEventOutboxMessages"
    ADD CONSTRAINT "CK_MarketplaceEventOutbox_AggregateRootId_NotEmpty"
    CHECK ("AggregateRootId" <> '00000000-0000-0000-0000-000000000000'::uuid) NOT VALID;

ALTER TABLE "MarketplaceEventOutboxMessages"
    VALIDATE CONSTRAINT "CK_MarketplaceEventOutbox_AggregateRootId_NotEmpty";

ALTER TABLE "OutboxMessages"
    DROP CONSTRAINT IF EXISTS "CK_OutboxMessages_CriticalEvents_TenantId_Required";

ALTER TABLE "OutboxMessages"
    ADD CONSTRAINT "CK_OutboxMessages_CriticalEvents_TenantId_Required"
    CHECK (
        ("TenantId" IS NOT NULL AND "TenantId" <> '00000000-0000-0000-0000-000000000000'::uuid) OR NOT (
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
    VALIDATE CONSTRAINT "CK_OutboxMessages_CriticalEvents_TenantId_Required";

ALTER TABLE "CommerceHubVendorEarnings"
    ADD COLUMN IF NOT EXISTS "PayoutId" uuid NULL,
    ADD COLUMN IF NOT EXISTS "PaidOutAmount" numeric(18,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "PayoutReservedAmount" numeric(18,2) NOT NULL DEFAULT 0;

DO $$
BEGIN
    IF to_regclass('"CommerceHubPayoutItems"') IS NOT NULL
       AND to_regclass('"CommerceHubPayouts"') IS NOT NULL THEN
        WITH payout_allocations AS (
            SELECT
                item."EarningId",
                SUM(CASE WHEN payout."Status" = 2 THEN GREATEST(item."Amount", 0) ELSE 0 END) AS paid_out_amount,
                SUM(CASE WHEN payout."Status" IN (0, 1) THEN GREATEST(item."Amount", 0) ELSE 0 END) AS reserved_amount
            FROM "CommerceHubPayoutItems" item
            INNER JOIN "CommerceHubPayouts" payout ON payout."Id" = item."PayoutId"
            INNER JOIN "CommerceHubVendorEarnings" earning ON earning."Id" = item."EarningId"
            WHERE payout."IsDeleted" = false
              AND earning."IsDeleted" = false
              AND earning."Amount" > 0
            GROUP BY item."EarningId"
        )
        UPDATE "CommerceHubVendorEarnings" earning
        SET
            "PaidOutAmount" = ROUND(COALESCE(allocation.paid_out_amount, 0), 2),
            "PayoutReservedAmount" = ROUND(COALESCE(allocation.reserved_amount, 0), 2)
        FROM payout_allocations allocation
        WHERE earning."Id" = allocation."EarningId";
    END IF;
END $$;

ALTER TABLE "CommerceHubVendorEarnings"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubVendorEarnings_PayoutAmounts_NonNegative";

ALTER TABLE "CommerceHubVendorEarnings"
    ADD CONSTRAINT "CK_CommerceHubVendorEarnings_PayoutAmounts_NonNegative"
    CHECK ("PaidOutAmount" >= 0 AND "PayoutReservedAmount" >= 0) NOT VALID;

ALTER TABLE "CommerceHubVendorEarnings"
    VALIDATE CONSTRAINT "CK_CommerceHubVendorEarnings_PayoutAmounts_NonNegative";

ALTER TABLE "CommerceHubVendorEarnings"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubVendorEarnings_PayoutAmounts_Within_Positive_Earn";

ALTER TABLE "CommerceHubVendorEarnings"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubVendorEarnings_PayoutAmounts_Within_Positive_Ear~";

ALTER TABLE "CommerceHubVendorEarnings"
    ADD CONSTRAINT "CK_CommerceHubVendorEarnings_PayoutAmounts_Within_Positive_Ear~"
    CHECK ("Amount" < 0 OR ROUND("PaidOutAmount" + "PayoutReservedAmount", 2) <= ROUND("Amount", 2)) NOT VALID;

ALTER TABLE "CommerceHubVendorEarnings"
    VALIDATE CONSTRAINT "CK_CommerceHubVendorEarnings_PayoutAmounts_Within_Positive_Ear~";

ALTER TABLE "CommerceHubVendorEarnings"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubVendorEarnings_InPayout_HasPayout";

ALTER TABLE "CommerceHubVendorEarnings"
    ADD CONSTRAINT "CK_CommerceHubVendorEarnings_InPayout_HasPayout"
    CHECK ("Status" <> 3 OR "PayoutId" IS NOT NULL) NOT VALID;

ALTER TABLE "CommerceHubVendorEarnings"
    VALIDATE CONSTRAINT "CK_CommerceHubVendorEarnings_InPayout_HasPayout";

ALTER TABLE "CommerceHubVendorEarnings"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubVendorEarnings_PaidOut_HasPaidAmount";

ALTER TABLE "CommerceHubVendorEarnings"
    ADD CONSTRAINT "CK_CommerceHubVendorEarnings_PaidOut_HasPaidAmount"
    CHECK ("Status" <> 2 OR "PaidOutAmount" > 0 OR "Amount" < 0) NOT VALID;

ALTER TABLE "CommerceHubVendorEarnings"
    VALIDATE CONSTRAINT "CK_CommerceHubVendorEarnings_PaidOut_HasPaidAmount";

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260429133000_S52_DataAuthorityBoundaryHardening', '9.0.1'
WHERE NOT EXISTS (
    SELECT 1
    FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260429133000_S52_DataAuthorityBoundaryHardening'
);
