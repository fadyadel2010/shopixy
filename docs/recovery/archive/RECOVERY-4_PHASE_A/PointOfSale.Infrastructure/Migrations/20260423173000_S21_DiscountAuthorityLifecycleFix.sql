ALTER TABLE "CouponReservations"
    RENAME COLUMN "ConsumedAtUtc" TO "ConfirmedAtUtc";

DROP INDEX IF EXISTS "IX_CouponReservations_TenantId_CouponId_CustomerId";

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CouponReservations_TenantId_CouponId_CustomerId"
    ON "CouponReservations" ("TenantId", "CouponId", "CustomerId")
    WHERE "IsDeleted" = false AND "Status" = 0;
