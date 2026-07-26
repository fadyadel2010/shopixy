ALTER TABLE "Orders"
    ADD COLUMN IF NOT EXISTS "CheckoutState" integer NOT NULL DEFAULT 1;

ALTER TABLE "Orders"
    ADD COLUMN IF NOT EXISTS "ManualDiscountAmount" numeric(18,2) NOT NULL DEFAULT 0;

ALTER TABLE "Orders"
    ADD COLUMN IF NOT EXISTS "PricingSnapshotJson" text NULL;

ALTER TABLE "Orders"
    ADD COLUMN IF NOT EXISTS "TotalDiscountAmount" numeric(18,2) NOT NULL DEFAULT 0;

UPDATE "Orders"
SET "TotalDiscountAmount" = COALESCE("DiscountAmount", 0) + COALESCE("PromotionDiscountAmount", 0),
    "CheckoutState" = CASE
        -- Collected (2) and Settled (9) → Paid (3)
        WHEN "PaymentStatus" IN (2, 9) THEN 3
        -- PartiallyRefunded (6) → Paid (3) - order still paid, just partially refunded
        WHEN "PaymentStatus" = 6 THEN 3
        -- Order Status Cancelled (6) → Cancelled (5) - check BEFORE Failed/Rejected
        WHEN "Status" = 6 THEN 5
        -- Failed (4) and Rejected (8) → Failed (4)
        WHEN "PaymentStatus" IN (4, 8) THEN 4
        -- Refunded (5) → Cancelled (5)
        WHEN "PaymentStatus" = 5 THEN 5
        -- Expired (11) → Expired (6)
        WHEN "PaymentStatus" = 11 THEN 6
        -- Has payment reference → PaymentPending (2)
        WHEN "PaymentReference" IS NOT NULL THEN 2
        -- Default → PendingPaymentIntent (1)
        ELSE 1
    END;

CREATE TABLE IF NOT EXISTS "CouponReservations" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "CouponId" uuid NOT NULL,
    "CustomerId" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "Status" integer NOT NULL,
    "CorrelationId" character varying(100),
    "ReservedAtUtc" timestamp with time zone NOT NULL,
    "ConsumedAtUtc" timestamp with time zone NULL,
    "ReleasedAtUtc" timestamp with time zone NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL,
    CONSTRAINT "PK_CouponReservations" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_CouponReservations_Coupons_CouponId" FOREIGN KEY ("CouponId") REFERENCES "Coupons" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_CouponReservations_OnlineCustomers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "OnlineCustomers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_CouponReservations_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS "IX_CouponReservations_CouponId"
    ON "CouponReservations" ("CouponId");

CREATE INDEX IF NOT EXISTS "IX_CouponReservations_CustomerId"
    ON "CouponReservations" ("CustomerId");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CouponReservations_TenantId_CouponId_CustomerId"
    ON "CouponReservations" ("TenantId", "CouponId", "CustomerId")
    WHERE "IsDeleted" = false AND "Status" <> 2;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CouponReservations_TenantId_OrderId"
    ON "CouponReservations" ("TenantId", "OrderId")
    WHERE "IsDeleted" = false;

CREATE INDEX IF NOT EXISTS "IX_Orders_TenantId_CheckoutState_CreatedAt"
    ON "Orders" ("TenantId", "CheckoutState", "CreatedAt");
