-- Fix ProfileImageUrl and CoverImageUrl column types to support data URLs
-- Data URLs can be very long (base64 encoded images)

ALTER TABLE "MarketplaceProfiles"
ALTER COLUMN "ProfileImageUrl" TYPE TEXT;

ALTER TABLE "MarketplaceProfiles"
ALTER COLUMN "CoverImageUrl" TYPE TEXT;

-- Also ensure Skills and PortfolioLinks are TEXT (should already be from previous migration)
ALTER TABLE "MarketplaceProfiles"
ALTER COLUMN "Skills" TYPE TEXT;

ALTER TABLE "MarketplaceProfiles"
ALTER COLUMN "PortfolioLinks" TYPE TEXT;
