-- Phase M2: Per-Product Pixel Override Table

CREATE TABLE IF NOT EXISTS "ProductPixelOverrides" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId" UUID NOT NULL,
    "ProductId" UUID NOT NULL,
    "IsEnabled" BOOLEAN NOT NULL DEFAULT FALSE,
    "Strategy" INT NOT NULL DEFAULT 1,
    "Value" DECIMAL(18,2) NOT NULL DEFAULT 100,
    "ApplyToMeta" BOOLEAN NOT NULL DEFAULT TRUE,
    "ApplyToGoogle" BOOLEAN NOT NULL DEFAULT TRUE,
    "ApplyToTikTok" BOOLEAN NOT NULL DEFAULT TRUE,
    "UpdatedAtUtc" TIMESTAMP NOT NULL DEFAULT NOW(),
    "UpdatedBy" UUID NOT NULL,
    CONSTRAINT "FK_ProductPixelOverride_Product" 
        FOREIGN KEY ("ProductId") REFERENCES "Products"("Id") ON DELETE CASCADE
);

-- Unique index to ensure one override per product per tenant
CREATE UNIQUE INDEX IF NOT EXISTS "IX_ProductPixelOverride_Tenant_Product" 
    ON "ProductPixelOverrides" ("TenantId", "ProductId");

-- Index for fast lookup by tenant (especially for bulk loading)
CREATE INDEX IF NOT EXISTS "IX_ProductPixelOverride_Tenant" 
    ON "ProductPixelOverrides" ("TenantId") WHERE "IsEnabled" = TRUE;
