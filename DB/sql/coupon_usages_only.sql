-- ============================================================================
-- COUPON USAGES TABLE - Shopify-Grade Coupon Engine
-- Run this in pgAdmin4
-- ============================================================================

-- Create CouponUsages table (if not exists)
CREATE TABLE IF NOT EXISTS "CouponUsages" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "CouponId" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "CustomerId" uuid,
    "CouponCode" text NOT NULL,
    "DiscountAmount" numeric(18,2) NOT NULL,
    "OrderSubtotal" numeric(18,2) NOT NULL,
    "UsedAtUtc" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_CouponUsages" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_CouponUsages_Coupons_CouponId" FOREIGN KEY ("CouponId") REFERENCES "Coupons" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_CouponUsages_OnlineCustomers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "OnlineCustomers" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_CouponUsages_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE RESTRICT
);

-- Create indexes (with IF NOT EXISTS)
CREATE INDEX IF NOT EXISTS "IX_CouponUsages_CouponId" ON "CouponUsages" ("CouponId");
CREATE INDEX IF NOT EXISTS "IX_CouponUsages_CustomerId" ON "CouponUsages" ("CustomerId");
CREATE INDEX IF NOT EXISTS "IX_CouponUsages_OrderId" ON "CouponUsages" ("OrderId");
CREATE INDEX IF NOT EXISTS "IX_CouponUsages_TenantId_CouponId" ON "CouponUsages" ("TenantId", "CouponId");
CREATE INDEX IF NOT EXISTS "IX_CouponUsages_TenantId_CouponId_CustomerId" ON "CouponUsages" ("TenantId", "CouponId", "CustomerId");

-- Create unique index (with IF NOT EXISTS) - one usage per order
CREATE UNIQUE INDEX IF NOT EXISTS "IX_CouponUsages_TenantId_OrderId" 
ON "CouponUsages" ("TenantId", "OrderId") 
WHERE "IsDeleted" = false;

-- Record migration in EF history (if not exists)
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260115214438_AddCouponUsagesTable', '9.0.1'
WHERE NOT EXISTS (
    SELECT 1 FROM "__EFMigrationsHistory" 
    WHERE "MigrationId" = '20260115214438_AddCouponUsagesTable'
);

-- Done!
SELECT 'CouponUsages table created successfully!' as result;
