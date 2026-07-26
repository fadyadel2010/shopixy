-- Phase FE-3: Marketplace Experience Expansion
-- Add profile expansion, pricing, and media fields

-- MarketplaceProfile extensions
ALTER TABLE "MarketplaceProfiles"
ADD COLUMN IF NOT EXISTS "CoverImageUrl" TEXT NULL,
ADD COLUMN IF NOT EXISTS "Location" TEXT NULL,
ADD COLUMN IF NOT EXISTS "Skills" TEXT NULL, -- JSON array
ADD COLUMN IF NOT EXISTS "PortfolioLinks" TEXT NULL; -- JSON array

-- Service extensions
ALTER TABLE "Services"
ADD COLUMN IF NOT EXISTS "StartingPrice" DECIMAL(18,2) NULL,
ADD COLUMN IF NOT EXISTS "PricingType" INTEGER NULL, -- 0=Fixed, 1=Hourly, 2=Package
ADD COLUMN IF NOT EXISTS "Currency" VARCHAR(3) NULL, -- ISO 4217
ADD COLUMN IF NOT EXISTS "ServiceImages" TEXT NULL, -- JSON array
ADD COLUMN IF NOT EXISTS "Packages" TEXT NULL; -- JSON array

-- ServiceRequestMessage extensions
ALTER TABLE "ServiceRequestMessages"
ADD COLUMN IF NOT EXISTS "AttachmentUrl" TEXT NULL;

-- Add CHECK constraint for PricingType enum
ALTER TABLE "Services"
ADD CONSTRAINT "CK_Services_PricingType" CHECK ("PricingType" IS NULL OR "PricingType" BETWEEN 0 AND 2);

-- Add CHECK constraint for Currency (ISO 4217 format)
ALTER TABLE "Services"
ADD CONSTRAINT "CK_Services_Currency" CHECK ("Currency" IS NULL OR LENGTH("Currency") = 3);

-- Add index for pricing queries
CREATE INDEX IF NOT EXISTS "IX_Services_StartingPrice" ON "Services" ("StartingPrice") WHERE "IsDeleted" = FALSE AND "IsActive" = TRUE;

-- Add index for location-based profile queries
CREATE INDEX IF NOT EXISTS "IX_MarketplaceProfiles_Location" ON "MarketplaceProfiles" ("Location") WHERE "IsDeleted" = FALSE AND "IsActive" = TRUE;
