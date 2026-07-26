-- ═══════════════════════════════════════════════════════════════════════════
-- AF-18: ZEZO Order Investigation
-- Check if the test order has AffiliateAccountId stamped
-- ═══════════════════════════════════════════════════════════════════════════

-- Find the most recent order with ZEZO referral code
SELECT 
    o."Id" AS OrderId,
    o."OrderNumber",
    o."TenantId",
    o."Status",
    o."FulfillmentStatus",
    o."AffiliateAccountId",  -- ← CRITICAL: Should NOT be NULL
    o."AffiliateReferralCode",  -- ← Should be "ZEZO906409"
    o."PromotionLinkId",
    o."Total",
    o."Currency",
    o."CreatedAt",
    o."UpdatedAt"
FROM "Orders" o
WHERE o."AffiliateReferralCode" ILIKE '%ZEZO%'
  AND o."IsDeleted" = false
ORDER BY o."CreatedAt" DESC
LIMIT 5;

-- Check if AffiliateAttribution was created
SELECT 
    aa."Id" AS AttributionId,
    aa."OrderId",
    aa."AffiliateAccountId",
    aa."ReferralCode",
    aa."Source",
    aa."PromotionLinkId",
    aa."CreatedAt"
FROM "AffiliateAttributions" aa
WHERE aa."ReferralCode" ILIKE '%ZEZO%'
  AND aa."IsDeleted" = false
ORDER BY aa."CreatedAt" DESC
LIMIT 5;

-- Check commission ledger entries
SELECT 
    acl."Id" AS LedgerEntryId,
    acl."OrderId",
    acl."AffiliateAccountId",
    acl."EntryType",
    acl."CommissionAmount",
    acl."RecordedAtUtc"
FROM "AffiliateCommissionLedger" acl
WHERE acl."AffiliateAccountId" = '019c8fb7-0756-7ab3-9187-086089e86e06'
ORDER BY acl."RecordedAtUtc" DESC
LIMIT 5;
