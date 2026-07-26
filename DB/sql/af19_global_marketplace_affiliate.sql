START TRANSACTION;
ALTER TABLE "AffiliateAccounts" DROP CONSTRAINT "UQ_AffiliateAccounts_Tenant_ReferralCode";

ALTER TABLE "AffiliateAccounts" DROP CONSTRAINT "UQ_AffiliateAccounts_Tenant_User";

ALTER TABLE "AffiliateAccounts" DROP CONSTRAINT "FK_AffiliateAccounts_Tenants";

ALTER TABLE "AffiliateCommissionLedger" ADD "StoreTenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';

CREATE UNIQUE INDEX "UQ_AffiliateAccounts_ReferralCode" ON "AffiliateAccounts" ("ReferralCode");

CREATE UNIQUE INDEX "UQ_AffiliateAccounts_User" ON "AffiliateAccounts" ("UserId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260224175620_AF19_GlobalMarketplaceAffiliate', '9.0.1');

COMMIT;

