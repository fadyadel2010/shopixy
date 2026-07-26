BEGIN;

ALTER TABLE "CommerceHubSubOrders"
    ADD COLUMN IF NOT EXISTS "PaymentMethod" integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "PaymentMethodCode" character varying(50) NOT NULL DEFAULT 'PAYMOB',
    ADD COLUMN IF NOT EXISTS "PaymentMethodName" character varying(100) NOT NULL DEFAULT 'Paymob',
    ADD COLUMN IF NOT EXISTS "PaymentSnapshotJson" text NULL;

UPDATE "CommerceHubMarketplaceOrders"
SET
    "PaymentStatus" = 10,
    "UpdatedAt" = NOW()
WHERE "IsDeleted" = false
  AND UPPER(COALESCE("PaymentMethodCode", '')) = 'COD'
  AND "PaymentStatus" = 0;

UPDATE "CommerceHubSubOrders" AS sub_order
SET
    "PaymentMethod" = CASE
        WHEN UPPER(COALESCE(parent_order."PaymentMethodCode", '')) = 'COD' THEN 1
        ELSE 0
    END,
    "PaymentMethodCode" = CASE
        WHEN UPPER(COALESCE(parent_order."PaymentMethodCode", '')) = 'COD' THEN 'COD'
        ELSE COALESCE(NULLIF(parent_order."PaymentMethodCode", ''), 'PAYMOB')
    END,
    "PaymentMethodName" = CASE
        WHEN UPPER(COALESCE(parent_order."PaymentMethodCode", '')) = 'COD' THEN 'Cash on Delivery'
        ELSE COALESCE(NULLIF(parent_order."PaymentMethodName", ''), 'Paymob')
    END,
    "PaymentStatus" = CASE
        WHEN UPPER(COALESCE(parent_order."PaymentMethodCode", '')) = 'COD'
         AND sub_order."PaymentStatus" = 0 THEN 10
        ELSE sub_order."PaymentStatus"
    END,
    "PaymentSnapshotJson" = COALESCE(
        NULLIF(BTRIM(sub_order."PaymentSnapshotJson"), ''),
        (
            json_build_object(
                'method', CASE
                    WHEN UPPER(COALESCE(parent_order."PaymentMethodCode", '')) = 'COD' THEN 'CashOnDelivery'
                    ELSE 'Online'
                END,
                'paymentMethodCode', CASE
                    WHEN UPPER(COALESCE(parent_order."PaymentMethodCode", '')) = 'COD' THEN 'COD'
                    ELSE COALESCE(NULLIF(parent_order."PaymentMethodCode", ''), 'PAYMOB')
                END,
                'paymentMethodName', CASE
                    WHEN UPPER(COALESCE(parent_order."PaymentMethodCode", '')) = 'COD' THEN 'Cash on Delivery'
                    ELSE COALESCE(NULLIF(parent_order."PaymentMethodName", ''), 'Paymob')
                END,
                'status', CASE
                    WHEN UPPER(COALESCE(parent_order."PaymentMethodCode", '')) = 'COD'
                     AND sub_order."PaymentStatus" = 0 THEN 'CashOnDelivery'
                    WHEN sub_order."PaymentStatus" = 0 THEN 'Pending'
                    WHEN sub_order."PaymentStatus" = 1 THEN 'Authorized'
                    WHEN sub_order."PaymentStatus" = 2 THEN 'Collected'
                    WHEN sub_order."PaymentStatus" = 3 THEN 'PartiallyPaid'
                    WHEN sub_order."PaymentStatus" = 4 THEN 'Failed'
                    WHEN sub_order."PaymentStatus" = 5 THEN 'Refunded'
                    WHEN sub_order."PaymentStatus" = 6 THEN 'PartiallyRefunded'
                    WHEN sub_order."PaymentStatus" = 7 THEN 'AwaitingVerification'
                    WHEN sub_order."PaymentStatus" = 8 THEN 'Rejected'
                    WHEN sub_order."PaymentStatus" = 9 THEN 'Settled'
                    WHEN sub_order."PaymentStatus" = 10 THEN 'CashOnDelivery'
                    ELSE 'Pending'
                END
            )::text
        )
    ),
    "UpdatedAt" = NOW()
FROM "CommerceHubMarketplaceOrders" AS parent_order
WHERE sub_order."MarketplaceOrderId" = parent_order."Id"
  AND sub_order."IsDeleted" = false
  AND parent_order."IsDeleted" = false;

COMMIT;
