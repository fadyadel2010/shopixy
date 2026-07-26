-- ═══════════════════════════════════════════════════════════════════════════
-- AF-18: BACKFILL MISSING COMMISSIONS
-- 
-- PURPOSE: Identify and backfill commission for orders that have:
-- 1. AffiliateAttribution (order was attributed to an affiliate)
-- 2. Status = Completed or Delivered
-- 3. NO commission ledger entries (commission was never recorded)
--
-- ROOT CAUSE: Orders that went directly to Completed status without triggering
-- OrderCodDeliveredEvent never recorded commission, making affiliates invisible.
--
-- USAGE:
-- 1. Run STEP 1 to identify affected orders
-- 2. Review the results
-- 3. For each affected order, trigger commission recording via API or manually
-- ═══════════════════════════════════════════════════════════════════════════

-- ─────────────────────────────────────────────────────────────────────────
-- STEP 1: IDENTIFY AFFECTED ORDERS
-- ─────────────────────────────────────────────────────────────────────────
-- Orders with attribution but NO commission entries
-- ─────────────────────────────────────────────────────────────────────────
SELECT 
    o."Id" AS OrderId,
    o."OrderNumber",
    o."TenantId",
    o."Status",
    o."FulfillmentStatus",
    o."PaymentMethod",
    o."Total",
    o."Currency",
    o."AffiliateAccountId",
    o."AffiliateReferralCode",
    aa."Id" AS AttributionId,
    aa."ReferralCode" AS AttributionReferralCode,
    a."DisplayName" AS AffiliateName,
    a."Status" AS AffiliateStatus,
    o."CreatedAt" AS OrderCreatedAt,
    aa."CreatedAt" AS AttributionCreatedAt,
    COUNT(acl."Id") AS CommissionEntriesCount
FROM "Orders" o
INNER JOIN "AffiliateAttributions" aa 
    ON o."Id" = aa."OrderId" 
    AND aa."IsDeleted" = false
INNER JOIN "AffiliateAccounts" a 
    ON aa."AffiliateAccountId" = a."Id" 
    AND a."IsDeleted" = false
LEFT JOIN "AffiliateCommissionLedger" acl 
    ON aa."Id" = acl."AttributionId"
WHERE o."IsDeleted" = false
  AND o."Status" IN (4, 5) -- 4=Delivered, 5=Completed
  AND o."Total" > 0
GROUP BY 
    o."Id", o."OrderNumber", o."TenantId", o."Status", o."FulfillmentStatus",
    o."PaymentMethod", o."Total", o."Currency", o."AffiliateAccountId",
    o."AffiliateReferralCode", aa."Id", aa."ReferralCode", a."DisplayName",
    a."Status", o."CreatedAt", aa."CreatedAt"
HAVING COUNT(acl."Id") = 0
ORDER BY o."CreatedAt" DESC;

-- ─────────────────────────────────────────────────────────────────────────
-- STEP 2: CHECK PRODUCT POLICIES FOR AFFECTED ORDERS
-- ─────────────────────────────────────────────────────────────────────────
-- Verify that products in affected orders have affiliate policies enabled
-- ─────────────────────────────────────────────────────────────────────────
WITH AffectedOrders AS (
    SELECT DISTINCT o."Id" AS OrderId
    FROM "Orders" o
    INNER JOIN "AffiliateAttributions" aa 
        ON o."Id" = aa."OrderId" AND aa."IsDeleted" = false
    LEFT JOIN "AffiliateCommissionLedger" acl 
        ON aa."Id" = acl."AttributionId"
    WHERE o."IsDeleted" = false
      AND o."Status" IN (4, 5)
      AND o."Total" > 0
    GROUP BY o."Id"
    HAVING COUNT(acl."Id") = 0
)
SELECT 
    ao.OrderId,
    o."OrderNumber",
    oi."ProductId",
    p."Name" AS ProductName,
    p."SKU",
    app."Id" AS PolicyId,
    app."CommissionRate",
    app."MaxCommissionPerItem",
    app."IsEnabled" AS PolicyEnabled,
    CASE 
        WHEN app."Id" IS NULL THEN 'NO_POLICY'
        WHEN app."IsEnabled" = false THEN 'POLICY_DISABLED'
        ELSE 'OK'
    END AS PolicyStatus
FROM AffectedOrders ao
INNER JOIN "Orders" o ON ao.OrderId = o."Id"
INNER JOIN "OrderItems" oi ON o."Id" = oi."OrderId"
INNER JOIN "Products" p ON oi."ProductId" = p."Id"
LEFT JOIN "AffiliateProductPolicies" app 
    ON oi."ProductId" = app."ProductId" 
    AND app."TenantId" = o."TenantId"
    AND app."IsDeleted" = false
ORDER BY ao.OrderId, oi."ProductId";

-- ─────────────────────────────────────────────────────────────────────────
-- STEP 3: SUMMARY BY AFFILIATE
-- ─────────────────────────────────────────────────────────────────────────
-- Shows how many orders per affiliate are missing commission
-- ─────────────────────────────────────────────────────────────────────────
SELECT 
    a."Id" AS AffiliateAccountId,
    a."DisplayName",
    a."ReferralCode",
    a."Status" AS AffiliateStatus,
    COUNT(DISTINCT o."Id") AS AffectedOrdersCount,
    SUM(o."Total") AS TotalAffectedAmount,
    o."Currency"
FROM "Orders" o
INNER JOIN "AffiliateAttributions" aa 
    ON o."Id" = aa."OrderId" AND aa."IsDeleted" = false
INNER JOIN "AffiliateAccounts" a 
    ON aa."AffiliateAccountId" = a."Id" AND a."IsDeleted" = false
LEFT JOIN "AffiliateCommissionLedger" acl 
    ON aa."Id" = acl."AttributionId"
WHERE o."IsDeleted" = false
  AND o."Status" IN (4, 5)
  AND o."Total" > 0
GROUP BY 
    a."Id", a."DisplayName", a."ReferralCode", a."Status", o."Currency"
HAVING COUNT(acl."Id") = 0
ORDER BY AffectedOrdersCount DESC;

-- ─────────────────────────────────────────────────────────────────────────
-- STEP 4: ZEZO SPECIFIC CHECK
-- ─────────────────────────────────────────────────────────────────────────
-- Detailed check for the ZEZO affiliate mentioned in the bug report
-- ─────────────────────────────────────────────────────────────────────────
SELECT 
    o."Id" AS OrderId,
    o."OrderNumber",
    o."Status",
    o."FulfillmentStatus",
    o."PaymentMethod",
    o."Total",
    o."Currency",
    o."CreatedAt",
    aa."Id" AS AttributionId,
    aa."CreatedAt" AS AttributionCreatedAt,
    COUNT(acl."Id") AS CommissionEntriesCount,
    -- Order items breakdown
    (SELECT COUNT(*) FROM "OrderItems" WHERE "OrderId" = o."Id") AS ItemCount,
    (SELECT STRING_AGG(p."Name", ', ')
     FROM "OrderItems" oi
     INNER JOIN "Products" p ON oi."ProductId" = p."Id"
     WHERE oi."OrderId" = o."Id") AS ProductNames
FROM "Orders" o
INNER JOIN "AffiliateAttributions" aa 
    ON o."Id" = aa."OrderId" AND aa."IsDeleted" = false
LEFT JOIN "AffiliateCommissionLedger" acl 
    ON aa."Id" = acl."AttributionId"
WHERE o."AffiliateReferralCode" ILIKE '%ZEZO%'
  AND o."IsDeleted" = false
GROUP BY 
    o."Id", o."OrderNumber", o."Status", o."FulfillmentStatus",
    o."PaymentMethod", o."Total", o."Currency", o."CreatedAt",
    aa."Id", aa."CreatedAt"
ORDER BY o."CreatedAt" DESC;

-- ═══════════════════════════════════════════════════════════════════════════
-- BACKFILL STRATEGY
-- ═══════════════════════════════════════════════════════════════════════════
--
-- OPTION A: TRIGGER VIA API (RECOMMENDED)
-- ─────────────────────────────────────────────────────────────────────────
-- For each affected OrderId from STEP 1, call the commission recording API:
--
-- POST /api/admin/affiliates/backfill-commission
-- {
--   "orderId": "<order-id-from-step-1>"
-- }
--
-- This will:
-- 1. Call AffiliateService.RecordCommissionForOrderAsync()
-- 2. Create commission ledger entries
-- 3. Publish AffiliateCommissionEarnedEvent
-- 4. Update MongoDB dashboard
-- 5. Make affiliate visible in /admin/affiliates
--
-- OPTION B: PUBLISH EVENT MANUALLY
-- ─────────────────────────────────────────────────────────────────────────
-- If API endpoint doesn't exist, publish OrderCompletedEvent manually:
--
-- 1. Deploy the fix (OrderCompletedAffiliateConsumer)
-- 2. For each affected order, publish OrderCompletedEvent to RabbitMQ
-- 3. Consumer will pick it up and record commission
--
-- OPTION C: DIRECT SQL INSERT (NOT RECOMMENDED)
-- ─────────────────────────────────────────────────────────────────────────
-- Manually inserting into AffiliateCommissionLedger is NOT recommended because:
-- 1. Requires calculating commission manually (complex logic)
-- 2. Must respect idempotency keys
-- 3. Must update MongoDB dashboard separately
-- 4. Risk of data inconsistency
--
-- ═══════════════════════════════════════════════════════════════════════════

-- ─────────────────────────────────────────────────────────────────────────
-- VERIFICATION AFTER BACKFILL
-- ─────────────────────────────────────────────────────────────────────────
-- Run this query after backfilling to verify commission was recorded
-- ─────────────────────────────────────────────────────────────────────────
SELECT 
    o."OrderNumber",
    o."AffiliateReferralCode",
    aa."Id" AS AttributionId,
    COUNT(acl."Id") AS CommissionEntriesCount,
    SUM(CASE WHEN acl."EntryType" = 0 THEN acl."CommissionAmount" ELSE 0 END) AS TotalEarned,
    MAX(acl."RecordedAtUtc") AS LastCommissionRecordedAt
FROM "Orders" o
INNER JOIN "AffiliateAttributions" aa 
    ON o."Id" = aa."OrderId" AND aa."IsDeleted" = false
LEFT JOIN "AffiliateCommissionLedger" acl 
    ON aa."Id" = acl."AttributionId"
WHERE o."AffiliateReferralCode" ILIKE '%ZEZO%'
  AND o."IsDeleted" = false
GROUP BY o."OrderNumber", o."AffiliateReferralCode", aa."Id"
ORDER BY o."CreatedAt" DESC;

-- EXPECTED: CommissionEntriesCount > 0 for all orders
-- EXPECTED: TotalEarned > 0 for all orders with enabled product policies

-- ─────────────────────────────────────────────────────────────────────────
-- MONGODB VERIFICATION
-- ─────────────────────────────────────────────────────────────────────────
-- After backfill, verify MongoDB dashboard was updated:
--
-- db.affiliate_dashboards.find({
--   "referralCode": /ZEZO/i
-- })
--
-- EXPECTED:
-- - TotalEarned > 0
-- - TotalOrders >= 1
-- - NetEarnings > 0
-- - LastCommissionAtUtc is recent
-- ─────────────────────────────────────────────────────────────────────────
