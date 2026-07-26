-- ═══════════════════════════════════════════════════════════════════════════
-- AF-18: AFFILIATE ATTRIBUTION PIPELINE DIAGNOSTIC QUERIES
-- 
-- SCENARIO: Customer used ?ref=ZEZO906409, order completed, but affiliate
--           "ZEZO" does NOT appear in /admin/affiliates
--
-- PURPOSE: Trace each step of the pipeline to identify the broken layer
-- ═══════════════════════════════════════════════════════════════════════════

-- ─────────────────────────────────────────────────────────────────────────
-- STEP 1: Verify AffiliateAccount exists for ZEZO
-- ─────────────────────────────────────────────────────────────────────────
-- EXPECTED: 1 row with Status = 1 (Active)
-- FAILURE SIGNATURE: No rows → affiliate never registered
-- FAILURE SIGNATURE: Status != 1 → affiliate inactive/pending
-- ─────────────────────────────────────────────────────────────────────────
SELECT 
    "Id" AS AffiliateAccountId,
    "TenantId",
    "UserId",
    "ReferralCode",
    "DisplayName",
    "Status", -- 0=Pending, 1=Active, 2=Suspended, 3=Deactivated
    "ActivatedAtUtc",
    "CreatedAt",
    "IsDeleted",
    "PayoutWalletNumber",
    "PayoutWalletType"
FROM "AffiliateAccounts"
WHERE "ReferralCode" ILIKE '%ZEZO%'
  AND "IsDeleted" = false
ORDER BY "CreatedAt" DESC;

-- ─────────────────────────────────────────────────────────────────────────
-- STEP 2: Find orders with ZEZO referral code
-- ─────────────────────────────────────────────────────────────────────────
-- EXPECTED: 1+ rows with AffiliateAccountId populated
-- FAILURE SIGNATURE: AffiliateAccountId IS NULL → middleware didn't stamp
-- FAILURE SIGNATURE: No rows → order doesn't exist or wrong referral code
-- ─────────────────────────────────────────────────────────────────────────
SELECT 
    o."Id" AS OrderId,
    o."OrderNumber",
    o."TenantId",
    o."Status", -- 0=Pending, 1=Confirmed, 2=Processing, 3=Shipped, 4=Delivered, 5=Completed, 6=Cancelled
    o."PaymentStatus", -- 0=Pending, 1=Paid, 2=Failed, 3=Refunded, 4=PartiallyRefunded
    o."FulfillmentStatus", -- 0=Pending, 1=Processing, 2=Shipped, 3=Delivered, 4=Completed, 5=Cancelled
    o."AffiliateAccountId",
    o."AffiliateReferralCode",
    o."PromotionLinkId",
    o."IsGuestCheckout",
    o."GuestEmail",
    o."GuestName",
    o."Total",
    o."Currency",
    o."CreatedAt",
    o."IsDeleted"
FROM "Orders" o
WHERE o."AffiliateReferralCode" ILIKE '%ZEZO%'
  AND o."IsDeleted" = false
ORDER BY o."CreatedAt" DESC;

-- ─────────────────────────────────────────────────────────────────────────
-- STEP 3: Verify AffiliateAttribution was created
-- ─────────────────────────────────────────────────────────────────────────
-- EXPECTED: 1 row per order with matching OrderId and AffiliateAccountId
-- FAILURE SIGNATURE: No rows → attribution NOT created during checkout
-- ─────────────────────────────────────────────────────────────────────────
SELECT 
    aa."Id" AS AttributionId,
    aa."TenantId",
    aa."OrderId",
    aa."AffiliateAccountId",
    aa."ReferralCode",
    aa."Source", -- 0=ReferralLink, 1=Manual, 2=Imported
    aa."PromotionLinkId",
    aa."CreatedAt",
    aa."IsDeleted",
    o."OrderNumber",
    o."Status" AS OrderStatus,
    o."FulfillmentStatus",
    o."Total"
FROM "AffiliateAttributions" aa
INNER JOIN "Orders" o ON aa."OrderId" = o."Id"
WHERE aa."ReferralCode" ILIKE '%ZEZO%'
  AND aa."IsDeleted" = false
ORDER BY aa."CreatedAt" DESC;

-- ─────────────────────────────────────────────────────────────────────────
-- STEP 4: Check AffiliateCommissionLedger for Earned entries
-- ─────────────────────────────────────────────────────────────────────────
-- EXPECTED: 1+ rows with EntryType = 0 (Earned) after order delivered/completed
-- FAILURE SIGNATURE: No rows → commission NOT recorded
-- FAILURE SIGNATURE: CommissionAmount = 0 → policy issue or calculation bug
-- ─────────────────────────────────────────────────────────────────────────
SELECT 
    acl."Id" AS LedgerEntryId,
    acl."TenantId",
    acl."AffiliateAccountId",
    acl."OrderId",
    acl."OrderItemId",
    acl."AttributionId",
    acl."EntryType", -- 0=Earned, 1=Reversed, 2=Approved, 3=Paid
    acl."BaseAmount",
    acl."CommissionRate",
    acl."CommissionAmount",
    acl."Currency",
    acl."RecordedAtUtc",
    acl."IdempotencyKey",
    o."OrderNumber",
    o."Status" AS OrderStatus,
    o."FulfillmentStatus",
    oi."ProductId",
    oi."ProductName",
    oi."Quantity",
    oi."Total" AS ItemTotal
FROM "AffiliateCommissionLedger" acl
INNER JOIN "Orders" o ON acl."OrderId" = o."Id"
INNER JOIN "OrderItems" oi ON acl."OrderItemId" = oi."Id"
WHERE acl."AffiliateAccountId" IN (
    SELECT "Id" FROM "AffiliateAccounts" 
    WHERE "ReferralCode" ILIKE '%ZEZO%' AND "IsDeleted" = false
)
ORDER BY acl."RecordedAtUtc" DESC;

-- ─────────────────────────────────────────────────────────────────────────
-- STEP 5: Check AffiliateProductPolicies for products in the order
-- ─────────────────────────────────────────────────────────────────────────
-- EXPECTED: 1+ rows with IsEnabled = true for products in ZEZO's order
-- FAILURE SIGNATURE: No rows → product NOT enabled for affiliate program
-- FAILURE SIGNATURE: IsEnabled = false → policy disabled
-- ─────────────────────────────────────────────────────────────────────────
SELECT 
    app."Id" AS PolicyId,
    app."TenantId",
    app."ProductId",
    app."CommissionRate",
    app."MaxCommissionPerItem",
    app."IsEnabled",
    app."CreatedAt",
    app."IsDeleted",
    p."Name" AS ProductName,
    p."SKU"
FROM "AffiliateProductPolicies" app
INNER JOIN "Products" p ON app."ProductId" = p."Id"
WHERE app."ProductId" IN (
    SELECT DISTINCT oi."ProductId"
    FROM "OrderItems" oi
    INNER JOIN "Orders" o ON oi."OrderId" = o."Id"
    WHERE o."AffiliateReferralCode" ILIKE '%ZEZO%'
      AND o."IsDeleted" = false
)
  AND app."IsDeleted" = false
ORDER BY app."CreatedAt" DESC;

-- ─────────────────────────────────────────────────────────────────────────
-- STEP 6: Full pipeline trace for ZEZO orders
-- ─────────────────────────────────────────────────────────────────────────
-- EXPECTED: All columns populated with matching IDs
-- FAILURE SIGNATURE: NULL in any column → that step failed
-- ─────────────────────────────────────────────────────────────────────────
SELECT 
    o."OrderNumber",
    o."Status" AS OrderStatus,
    o."FulfillmentStatus",
    o."Total",
    o."Currency",
    o."AffiliateAccountId",
    o."AffiliateReferralCode",
    o."PromotionLinkId",
    aa."Id" AS AttributionId,
    aa."Source" AS AttributionSource,
    COUNT(DISTINCT acl."Id") AS CommissionEntriesCount,
    SUM(CASE WHEN acl."EntryType" = 0 THEN acl."CommissionAmount" ELSE 0 END) AS TotalEarned,
    SUM(CASE WHEN acl."EntryType" = 1 THEN acl."CommissionAmount" ELSE 0 END) AS TotalReversed,
    SUM(CASE WHEN acl."EntryType" = 2 THEN acl."CommissionAmount" ELSE 0 END) AS TotalApproved,
    SUM(CASE WHEN acl."EntryType" = 3 THEN acl."CommissionAmount" ELSE 0 END) AS TotalPaid,
    o."CreatedAt" AS OrderCreatedAt,
    aa."CreatedAt" AS AttributionCreatedAt,
    MAX(acl."RecordedAtUtc") AS LastCommissionRecordedAt
FROM "Orders" o
LEFT JOIN "AffiliateAttributions" aa ON o."Id" = aa."OrderId" AND aa."IsDeleted" = false
LEFT JOIN "AffiliateCommissionLedger" acl ON aa."Id" = acl."AttributionId"
WHERE o."AffiliateReferralCode" ILIKE '%ZEZO%'
  AND o."IsDeleted" = false
GROUP BY 
    o."OrderNumber", o."Status", o."FulfillmentStatus", o."Total", o."Currency",
    o."AffiliateAccountId", o."AffiliateReferralCode", o."PromotionLinkId",
    aa."Id", aa."Source", o."CreatedAt", aa."CreatedAt"
ORDER BY o."CreatedAt" DESC;

-- ─────────────────────────────────────────────────────────────────────────
-- STEP 7: Check if Admin query would filter out ZEZO
-- ─────────────────────────────────────────────────────────────────────────
-- This simulates the AdminAffiliateController.ListAffiliates query
-- EXPECTED: 1 row for ZEZO with financial data
-- FAILURE SIGNATURE: No rows → affiliate filtered out by Status or IsDeleted
-- ─────────────────────────────────────────────────────────────────────────
SELECT 
    a."Id" AS AffiliateAccountId,
    a."DisplayName",
    a."ReferralCode",
    a."Status",
    a."ActivatedAtUtc",
    a."CreatedAt",
    a."PayoutWalletNumber",
    a."PayoutWalletType",
    a."IsDeleted",
    -- MongoDB dashboard data would be joined here in the actual API
    -- We can't query MongoDB from SQL, but we can verify the affiliate exists
    COUNT(DISTINCT acl."OrderId") AS OrdersWithCommission,
    SUM(CASE WHEN acl."EntryType" = 0 THEN acl."CommissionAmount" ELSE 0 END) AS TotalEarned_SQL,
    SUM(CASE WHEN acl."EntryType" = 2 THEN acl."CommissionAmount" ELSE 0 END) AS TotalApproved_SQL,
    SUM(CASE WHEN acl."EntryType" = 3 THEN acl."CommissionAmount" ELSE 0 END) AS TotalPaid_SQL
FROM "AffiliateAccounts" a
LEFT JOIN "AffiliateCommissionLedger" acl ON a."Id" = acl."AffiliateAccountId"
WHERE a."ReferralCode" ILIKE '%ZEZO%'
  AND a."IsDeleted" = false
  -- AdminAffiliateController filters by Status if provided, but lists all by default
GROUP BY 
    a."Id", a."DisplayName", a."ReferralCode", a."Status", 
    a."ActivatedAtUtc", a."CreatedAt", a."PayoutWalletNumber", 
    a."PayoutWalletType", a."IsDeleted"
ORDER BY a."CreatedAt" DESC;

-- ═══════════════════════════════════════════════════════════════════════════
-- DIAGNOSTIC INTERPRETATION GUIDE
-- ═══════════════════════════════════════════════════════════════════════════
--
-- IF STEP 1 FAILS (No AffiliateAccount):
--   → Affiliate never registered or was soft-deleted
--   → FIX: Check affiliate registration flow
--
-- IF STEP 2 FAILS (No Order or AffiliateAccountId NULL):
--   → Middleware didn't stamp affiliate identity
--   → FIX: Check AffiliateReferralMiddleware, AffiliateModeResolver
--   → Verify ?ref=ZEZO906409 was captured in cookie/header
--
-- IF STEP 3 FAILS (No AffiliateAttribution):
--   → Attribution NOT created during checkout
--   → FIX: Check CheckoutService line ~2000-2030
--   → Verify transaction committed successfully
--
-- IF STEP 4 FAILS (No CommissionLedger entries):
--   → Commission NOT recorded after order delivered/completed
--   → FIX: Check AffiliatePaymentCapturedConsumers
--   → Verify RecordCommissionForOrderAsync was called
--   → Check if AffiliateProductPolicy exists and IsEnabled = true
--
-- IF STEP 5 FAILS (No AffiliateProductPolicy):
--   → Product NOT enabled for affiliate program
--   → FIX: Admin must enable product in affiliate settings
--
-- IF STEP 6 SHOWS NULL AttributionId:
--   → Checkout transaction failed or rolled back
--   → FIX: Check application logs for checkout errors
--
-- IF STEP 7 SHOWS ZERO OrdersWithCommission:
--   → MongoDB projection NOT updated
--   → FIX: Check AffiliateCommissionEarnedProjectionConsumer
--   → Verify RabbitMQ messages are being consumed
--   → Check MongoDB connection and collection name
--
-- IF ALL STEPS PASS BUT AFFILIATE NOT IN /admin/affiliates:
--   → MongoDB dashboard document missing or not queried correctly
--   → FIX: Check AdminAffiliateController.ListAffiliates line 67-152
--   → Verify MongoDB collection "affiliate_dashboards" exists
--   → Check if dashboard document was created for this AffiliateAccountId
--
-- ═══════════════════════════════════════════════════════════════════════════
