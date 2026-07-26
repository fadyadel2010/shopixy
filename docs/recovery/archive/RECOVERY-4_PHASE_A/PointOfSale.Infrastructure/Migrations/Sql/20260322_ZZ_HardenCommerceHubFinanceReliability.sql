BEGIN;

ALTER TABLE "CommerceHubSubOrders"
    ADD COLUMN IF NOT EXISTS "ShippingMethodId" uuid NULL,
    ADD COLUMN IF NOT EXISTS "ShippingMethodName" character varying(200) NULL,
    ADD COLUMN IF NOT EXISTS "ShippingSnapshotJson" text NULL,
    ADD COLUMN IF NOT EXISTS "PaymentMethod" integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "PaymentMethodCode" character varying(50) NOT NULL DEFAULT 'PAYMOB',
    ADD COLUMN IF NOT EXISTS "PaymentMethodName" character varying(100) NOT NULL DEFAULT 'Paymob',
    ADD COLUMN IF NOT EXISTS "PaymentSnapshotJson" text NULL,
    ADD COLUMN IF NOT EXISTS "CommissionSnapshotJson" text NULL,
    ADD COLUMN IF NOT EXISTS "IsEarningCreated" boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS "EarningCreatedAtUtc" timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS "PlatformCommissionAmount" numeric(18,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "VendorEarningAmount" numeric(18,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "PlatformCommissionRate" numeric(8,6) NOT NULL DEFAULT 0;

ALTER TABLE "CommerceHubVendorEarnings"
    ADD COLUMN IF NOT EXISTS "PaymentId" uuid NULL;

UPDATE "CommerceHubSubOrders" AS sub_order
SET
    "PaymentMethod" = CASE
        WHEN UPPER(COALESCE(parent_order."PaymentMethodCode", '')) = 'COD' THEN 1
        ELSE COALESCE(sub_order."PaymentMethod", 0)
    END,
    "PaymentMethodCode" = CASE
        WHEN UPPER(COALESCE(parent_order."PaymentMethodCode", '')) = 'COD' THEN 'COD'
        ELSE COALESCE(NULLIF(sub_order."PaymentMethodCode", ''), NULLIF(parent_order."PaymentMethodCode", ''), 'PAYMOB')
    END,
    "PaymentMethodName" = CASE
        WHEN UPPER(COALESCE(parent_order."PaymentMethodCode", '')) = 'COD' THEN 'Cash on Delivery'
        ELSE COALESCE(NULLIF(sub_order."PaymentMethodName", ''), NULLIF(parent_order."PaymentMethodName", ''), 'Paymob')
    END,
    "PaymentSnapshotJson" = COALESCE(
        NULLIF(BTRIM(sub_order."PaymentSnapshotJson"), ''),
        CASE
            WHEN parent_order."Id" IS NULL THEN NULL
            ELSE (
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
                    END
                )::text
            )
        END
    )
FROM "CommerceHubMarketplaceOrders" AS parent_order
WHERE sub_order."MarketplaceOrderId" = parent_order."Id"
  AND sub_order."IsDeleted" = false
  AND parent_order."IsDeleted" = false;

ALTER TABLE "CommerceHubPayouts"
    ADD COLUMN IF NOT EXISTS "ExternalReferenceId" character varying(200) NULL,
    ADD COLUMN IF NOT EXISTS "BankTransactionId" character varying(200) NULL,
    ADD COLUMN IF NOT EXISTS "ProcessedBy" character varying(200) NULL;

ALTER TABLE "CommerceHubRefunds"
    ADD COLUMN IF NOT EXISTS "GatewayStatus" character varying(100) NULL,
    ADD COLUMN IF NOT EXISTS "ProcessingStartedAtUtc" timestamp with time zone NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubVendorEarnings_CommerceHubPayments_PaymentId'
    ) THEN
        ALTER TABLE "CommerceHubVendorEarnings"
            ADD CONSTRAINT "FK_CommerceHubVendorEarnings_CommerceHubPayments_PaymentId"
            FOREIGN KEY ("PaymentId")
            REFERENCES "CommerceHubPayments" ("Id")
            ON DELETE SET NULL;
    END IF;
END $$;

UPDATE "CommerceHubRefunds"
SET
    "Status" = CASE
        WHEN "Status" = 2 THEN 3
        WHEN "Status" = 1 THEN 2
        ELSE "Status"
    END,
    "GatewayStatus" = CASE
        WHEN "GatewayStatus" IS NOT NULL THEN "GatewayStatus"
        WHEN "Status" = 2 THEN 'failed'
        WHEN "Status" = 1 THEN 'confirmed'
        ELSE 'pending'
    END,
    "ProcessingStartedAtUtc" = COALESCE("ProcessingStartedAtUtc", "ProcessedAtUtc", "CreatedAt")
WHERE
    "Status" IN (1, 2)
    OR "GatewayStatus" IS NULL
    OR "ProcessingStartedAtUtc" IS NULL;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubPayouts_ExternalReferenceId"
    ON "CommerceHubPayouts" ("ExternalReferenceId")
    WHERE "ExternalReferenceId" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubPayouts_BankTransactionId"
    ON "CommerceHubPayouts" ("BankTransactionId")
    WHERE "BankTransactionId" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubVendorEarnings_PaymentId"
    ON "CommerceHubVendorEarnings" ("PaymentId");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubSubOrders_ShippingMethodId"
    ON "CommerceHubSubOrders" ("ShippingMethodId");

COMMIT;
