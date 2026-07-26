START TRANSACTION;
ALTER TABLE "AffiliateAccounts" DROP CONSTRAINT "FK_AffiliateAccounts_Users_UserId";

DROP INDEX "IX_AffiliateAccounts_UserId";

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260220160244_RemoveLegacyAffiliateUserFK', '9.0.1');

COMMIT;

