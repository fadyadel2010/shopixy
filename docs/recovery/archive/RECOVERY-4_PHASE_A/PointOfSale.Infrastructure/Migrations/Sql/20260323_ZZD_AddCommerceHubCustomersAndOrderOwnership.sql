BEGIN;

CREATE TABLE IF NOT EXISTS "CommerceHubCustomers" (
    "Id" uuid NOT NULL,
    "Email" character varying(255) NOT NULL,
    "PasswordHash" character varying(500) NULL,
    "Name" character varying(200) NOT NULL,
    "PhoneNumber" character varying(50) NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_CommerceHubCustomers" PRIMARY KEY ("Id")
);

ALTER TABLE "CommerceHubMarketplaceOrders"
    ADD COLUMN IF NOT EXISTS "CustomerId" uuid NULL;

WITH source_customers AS (
    SELECT DISTINCT
        candidate."Email",
        candidate."Name",
        candidate."PhoneNumber",
        candidate."CreatedAt",
        (
            substr(md5(candidate."Email"), 1, 8) || '-' ||
            substr(md5(candidate."Email"), 9, 4) || '-' ||
            substr(md5(candidate."Email"), 13, 4) || '-' ||
            substr(md5(candidate."Email"), 17, 4) || '-' ||
            substr(md5(candidate."Email"), 21, 12)
        )::uuid AS "Id"
    FROM (
        SELECT
            lower(trim(oc."Email")) AS "Email",
            COALESCE(NULLIF(trim(oc."DisplayName"), ''), NULLIF(trim(mo."FullName"), ''), 'CommerceHub Customer') AS "Name",
            COALESCE(NULLIF(trim(oc."Phone"), ''), NULLIF(trim(mo."PhoneNumber"), '')) AS "PhoneNumber",
            COALESCE(oc."CreatedAtUtc", mo."CreatedAt") AS "CreatedAt"
        FROM "CommerceHubMarketplaceOrders" AS mo
        INNER JOIN "OnlineCustomers" AS oc
            ON oc."Id" = mo."OnlineCustomerId"
        WHERE mo."CustomerId" IS NULL
          AND oc."Email" IS NOT NULL
          AND trim(oc."Email") <> ''

        UNION ALL

        SELECT
            lower('guest-' || mo."Id"::text || '@commercehub.local') AS "Email",
            COALESCE(NULLIF(trim(mo."FullName"), ''), 'CommerceHub Guest') AS "Name",
            NULLIF(trim(mo."PhoneNumber"), '') AS "PhoneNumber",
            mo."CreatedAt" AS "CreatedAt"
        FROM "CommerceHubMarketplaceOrders" AS mo
        LEFT JOIN "OnlineCustomers" AS oc
            ON oc."Id" = mo."OnlineCustomerId"
        WHERE mo."CustomerId" IS NULL
          AND (
              mo."OnlineCustomerId" IS NULL OR
              oc."Id" IS NULL OR
              oc."Email" IS NULL OR
              trim(oc."Email") = ''
          )
    ) AS candidate
)
INSERT INTO "CommerceHubCustomers" (
    "Id",
    "Email",
    "PasswordHash",
    "Name",
    "PhoneNumber",
    "CreatedAt",
    "UpdatedAt",
    "CreatedBy",
    "UpdatedBy",
    "IsDeleted"
)
SELECT
    source."Id",
    source."Email",
    NULL,
    source."Name",
    source."PhoneNumber",
    source."CreatedAt",
    NULL,
    NULL,
    NULL,
    false
FROM source_customers AS source
ON CONFLICT DO NOTHING;

WITH ownership AS (
    SELECT
        mo."Id" AS "OrderId",
        COALESCE(
            CASE
                WHEN oc."Email" IS NOT NULL AND trim(oc."Email") <> '' THEN
                    (
                        substr(md5(lower(trim(oc."Email"))), 1, 8) || '-' ||
                        substr(md5(lower(trim(oc."Email"))), 9, 4) || '-' ||
                        substr(md5(lower(trim(oc."Email"))), 13, 4) || '-' ||
                        substr(md5(lower(trim(oc."Email"))), 17, 4) || '-' ||
                        substr(md5(lower(trim(oc."Email"))), 21, 12)
                    )::uuid
            END,
            (
                substr(md5(lower('guest-' || mo."Id"::text || '@commercehub.local')), 1, 8) || '-' ||
                substr(md5(lower('guest-' || mo."Id"::text || '@commercehub.local')), 9, 4) || '-' ||
                substr(md5(lower('guest-' || mo."Id"::text || '@commercehub.local')), 13, 4) || '-' ||
                substr(md5(lower('guest-' || mo."Id"::text || '@commercehub.local')), 17, 4) || '-' ||
                substr(md5(lower('guest-' || mo."Id"::text || '@commercehub.local')), 21, 12)
            )::uuid
        ) AS "CustomerId"
    FROM "CommerceHubMarketplaceOrders" AS mo
    LEFT JOIN "OnlineCustomers" AS oc
        ON oc."Id" = mo."OnlineCustomerId"
    WHERE mo."CustomerId" IS NULL
)
UPDATE "CommerceHubMarketplaceOrders" AS target
SET "CustomerId" = ownership."CustomerId"
FROM ownership
WHERE target."Id" = ownership."OrderId";

ALTER TABLE "CommerceHubMarketplaceOrders"
    ALTER COLUMN "CustomerId" SET NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_CommerceHubMarketplaceOrders_CommerceHubCustomers_CustomerId'
    ) THEN
        ALTER TABLE "CommerceHubMarketplaceOrders"
            ADD CONSTRAINT "FK_CommerceHubMarketplaceOrders_CommerceHubCustomers_CustomerId"
            FOREIGN KEY ("CustomerId")
            REFERENCES "CommerceHubCustomers" ("Id")
            ON DELETE RESTRICT;
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommerceHubCustomers_Email"
    ON "CommerceHubCustomers" ("Email")
    WHERE "IsDeleted" = false;

CREATE INDEX IF NOT EXISTS "IX_CommerceHubCustomers_CreatedAt"
    ON "CommerceHubCustomers" ("CreatedAt");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubMarketplaceOrders_CustomerId"
    ON "CommerceHubMarketplaceOrders" ("CustomerId");

COMMIT;
