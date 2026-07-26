BEGIN;

ALTER TABLE "CommerceHubListings"
    ADD COLUMN IF NOT EXISTS "RestorePublicationOnActivate" boolean NOT NULL DEFAULT FALSE;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubListings_TenantId_RestorePublicationOnActivate"
    ON "CommerceHubListings" ("TenantId", "RestorePublicationOnActivate")
    WHERE "RestorePublicationOnActivate" = TRUE;

COMMIT;
