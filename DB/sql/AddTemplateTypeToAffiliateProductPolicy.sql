START TRANSACTION;
ALTER TABLE "AffiliateProductPolicies" ADD "TemplateType" text NOT NULL DEFAULT '';

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260226192814_AddTemplateTypeToAffiliateProductPolicy', '9.0.1');

COMMIT;

