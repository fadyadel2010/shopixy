-- S50: Data authority violation closure
-- CommerceHub listing prices are not authoritative. POS ProductVariant.EffectiveOnlinePrice is the only marketplace price source.

UPDATE "CommerceHubListings"
SET "CommerceHubPrice" = NULL,
    "UpdatedAt" = COALESCE("UpdatedAt", NOW())
WHERE "CommerceHubPrice" IS NOT NULL;

ALTER TABLE "CommerceHubListings"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubListings_CommerceHubPrice_PosAuthorityNull";

ALTER TABLE "CommerceHubListings"
    ADD CONSTRAINT "CK_CommerceHubListings_CommerceHubPrice_PosAuthorityNull"
    CHECK ("CommerceHubPrice" IS NULL) NOT VALID;

ALTER TABLE "CommerceHubListings"
    VALIDATE CONSTRAINT "CK_CommerceHubListings_CommerceHubPrice_PosAuthorityNull";

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260429110000_S50_DataAuthorityViolationClosure', '9.0.1'
WHERE NOT EXISTS (
    SELECT 1
    FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260429110000_S50_DataAuthorityViolationClosure'
);
