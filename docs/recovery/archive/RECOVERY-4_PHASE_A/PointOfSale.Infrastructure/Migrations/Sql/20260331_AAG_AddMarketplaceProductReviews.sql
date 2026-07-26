BEGIN;

-- ============================================================================
-- Marketplace Product Reviews
-- Adds the PostgreSQL write-model table for marketplace-only product reviews.
--
-- Status values:
--   0 = Pending
--   1 = Approved
--   2 = Rejected
--
-- Safe to run from pgAdmin 4.
-- ============================================================================

DO $$
BEGIN
    IF to_regclass('public."Products"') IS NULL THEN
        RAISE EXCEPTION 'Required table public."Products" was not found.';
    END IF;

    IF to_regclass('public."CommerceHubCustomers"') IS NULL THEN
        RAISE EXCEPTION 'Required table public."CommerceHubCustomers" was not found.';
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS "ProductReviews"
(
    "Id" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Rating" integer NOT NULL,
    "Title" character varying(200) NULL,
    "Comment" character varying(4000) NOT NULL,
    "IsVerifiedPurchase" boolean NOT NULL,
    "Status" integer NOT NULL DEFAULT 0,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL,
    CONSTRAINT "PK_ProductReviews" PRIMARY KEY ("Id")
);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_ProductReviews_Rating_Range')
    THEN
        ALTER TABLE "ProductReviews"
            ADD CONSTRAINT "CK_ProductReviews_Rating_Range"
            CHECK ("Rating" >= 1 AND "Rating" <= 5);
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_ProductReviews_CommerceHubCustomers_UserId')
    THEN
        ALTER TABLE "ProductReviews"
            ADD CONSTRAINT "FK_ProductReviews_CommerceHubCustomers_UserId"
            FOREIGN KEY ("UserId") REFERENCES "CommerceHubCustomers" ("Id") ON DELETE RESTRICT;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_ProductReviews_Products_ProductId')
    THEN
        ALTER TABLE "ProductReviews"
            ADD CONSTRAINT "FK_ProductReviews_Products_ProductId"
            FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE RESTRICT;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_ProductReviews_ProductId_Status_CreatedAtUtc"
    ON "ProductReviews" ("ProductId", "Status", "CreatedAtUtc");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_ProductReviews_ProductId_UserId"
    ON "ProductReviews" ("ProductId", "UserId")
    WHERE "IsDeleted" = FALSE;

CREATE INDEX IF NOT EXISTS "IX_ProductReviews_Status_Rating_ProductId"
    ON "ProductReviews" ("Status", "Rating", "ProductId");

CREATE INDEX IF NOT EXISTS "IX_ProductReviews_UserId_CreatedAtUtc"
    ON "ProductReviews" ("UserId", "CreatedAtUtc");

COMMIT;
