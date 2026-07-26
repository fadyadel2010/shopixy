BEGIN;

ALTER TABLE "CommerceHubRefundRequests"
    ADD COLUMN IF NOT EXISTS "ReasonCode" integer NOT NULL DEFAULT 5;

ALTER TABLE "CommerceHubRefundRequests"
    ADD COLUMN IF NOT EXISTS "ReasonText" character varying(1000) NULL;

UPDATE "CommerceHubRefundRequests"
SET "ReasonText" = NULLIF(BTRIM("Reason"), '')
WHERE "ReasonText" IS NULL
  AND "Reason" IS NOT NULL;

CREATE TABLE IF NOT EXISTS "CommerceHubRefundEvidenceMedia"
(
    "Id" uuid NOT NULL,
    "RefundRequestId" uuid NULL,
    "CustomerId" uuid NOT NULL,
    "ObjectKey" character varying(500) NOT NULL,
    "OriginalFileName" character varying(255) NOT NULL,
    "ContentType" character varying(255) NOT NULL,
    "MediaType" character varying(32) NOT NULL,
    "SizeBytes" bigint NOT NULL DEFAULT 0,
    "ExpectedSizeBytes" bigint NOT NULL DEFAULT 0,
    "ContentHash" character varying(128) NULL,
    "MetadataJson" jsonb NULL,
    "ThumbnailObjectKey" character varying(500) NULL,
    "IsTemporary" boolean NOT NULL DEFAULT TRUE,
    "IsFlagged" boolean NOT NULL DEFAULT FALSE,
    "ExpiresAtUtc" timestamp with time zone NOT NULL,
    "UploadedAtUtc" timestamp with time zone NULL,
    "AttachedAtUtc" timestamp with time zone NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT "PK_CommerceHubRefundEvidenceMedia" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_CommerceHubRefundEvidenceMedia_CommerceHubCustomers_CustomerId"
        FOREIGN KEY ("CustomerId")
        REFERENCES "CommerceHubCustomers" ("Id")
        ON DELETE RESTRICT,
    CONSTRAINT "FK_CommerceHubRefundEvidenceMedia_CommerceHubRefundRequests_RefundRequestId"
        FOREIGN KEY ("RefundRequestId")
        REFERENCES "CommerceHubRefundRequests" ("Id")
        ON DELETE RESTRICT
);

ALTER TABLE "CommerceHubRefundEvidenceMedia"
    ADD COLUMN IF NOT EXISTS "ExpectedSizeBytes" bigint NOT NULL DEFAULT 0;

ALTER TABLE "CommerceHubRefundEvidenceMedia"
    ADD COLUMN IF NOT EXISTS "ContentHash" character varying(128) NULL;

ALTER TABLE "CommerceHubRefundEvidenceMedia"
    ADD COLUMN IF NOT EXISTS "ThumbnailObjectKey" character varying(500) NULL;

ALTER TABLE "CommerceHubRefundEvidenceMedia"
    ADD COLUMN IF NOT EXISTS "IsTemporary" boolean NOT NULL DEFAULT TRUE;

ALTER TABLE "CommerceHubRefundEvidenceMedia"
    ADD COLUMN IF NOT EXISTS "IsFlagged" boolean NOT NULL DEFAULT FALSE;

ALTER TABLE "CommerceHubRefundEvidenceMedia"
    ADD COLUMN IF NOT EXISTS "UploadedAtUtc" timestamp with time zone NULL;

UPDATE "CommerceHubRefundEvidenceMedia"
SET "ExpectedSizeBytes" = CASE
        WHEN COALESCE("ExpectedSizeBytes", 0) > 0 THEN "ExpectedSizeBytes"
        ELSE COALESCE("SizeBytes", 0)
    END,
    "UploadedAtUtc" = COALESCE("UploadedAtUtc", "AttachedAtUtc", "CreatedAt"),
    "IsTemporary" = CASE
        WHEN "RefundRequestId" IS NULL THEN TRUE
        ELSE FALSE
    END;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubRefundEvidenceMedia_CustomerId_CreatedAt"
    ON "CommerceHubRefundEvidenceMedia" ("CustomerId", "CreatedAt" DESC);

CREATE INDEX IF NOT EXISTS "IX_CommerceHubRefundEvidenceMedia_CustomerId_ContentHash"
    ON "CommerceHubRefundEvidenceMedia" ("CustomerId", "ContentHash");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubRefundEvidenceMedia_RefundRequestId"
    ON "CommerceHubRefundEvidenceMedia" ("RefundRequestId");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubRefundEvidenceMedia_IsTemporary_ExpiresAtUtc"
    ON "CommerceHubRefundEvidenceMedia" ("IsTemporary", "ExpiresAtUtc");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommerceHubRefundEvidenceMedia_ObjectKey"
    ON "CommerceHubRefundEvidenceMedia" ("ObjectKey");

COMMIT;
