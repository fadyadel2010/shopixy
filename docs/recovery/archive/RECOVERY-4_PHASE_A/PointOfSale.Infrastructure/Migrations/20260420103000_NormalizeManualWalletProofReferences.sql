BEGIN;

UPDATE "PaymentIntents"
SET "ProofImageUrl" = substring("ProofImageUrl" from 'tenants/[^?#/]+/manual-wallet-proofs/[^?#]+')
WHERE "ProofImageUrl" ILIKE 'http%'
  AND substring("ProofImageUrl" from 'tenants/([^?#/]+)/manual-wallet-proofs/') = "TenantId"::text;

UPDATE "PaymentIntents"
SET "ProofImageUrl" = NULL,
    "ProofImageDeleted" = TRUE
WHERE "ProofImageUrl" ILIKE 'http%'
  AND (substring("ProofImageUrl" from 'tenants/([^?#/]+)/manual-wallet-proofs/') IS NULL
       OR substring("ProofImageUrl" from 'tenants/([^?#/]+)/manual-wallet-proofs/') != "TenantId"::text);

UPDATE "CheckoutSessions"
SET "ProofImageUrl" = substring("ProofImageUrl" from 'tenants/[^?#/]+/manual-wallet-proofs/[^?#]+')
WHERE "ProofImageUrl" ILIKE 'http%'
  AND substring("ProofImageUrl" from 'tenants/([^?#/]+)/manual-wallet-proofs/') = "TenantId"::text;

UPDATE "CheckoutSessions"
SET "ProofImageUrl" = NULL,
    "WalletPhoneNumber" = NULL,
    "WalletType" = NULL
WHERE "ProofImageUrl" ILIKE 'http%'
  AND (substring("ProofImageUrl" from 'tenants/([^?#/]+)/manual-wallet-proofs/') IS NULL
       OR substring("ProofImageUrl" from 'tenants/([^?#/]+)/manual-wallet-proofs/') != "TenantId"::text);

COMMIT;

-- Rollback is intentionally omitted because external URLs are normalized or purged for security.
