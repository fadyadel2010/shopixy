BEGIN;

CREATE TABLE IF NOT EXISTS "HomepageSections"
(
    "Id" uuid NOT NULL,
    "Type" character varying(40) NOT NULL,
    "Order" integer NOT NULL,
    "Data" jsonb NOT NULL DEFAULT '{}'::jsonb,
    "IsActive" boolean NOT NULL DEFAULT TRUE,
    "TenantId" uuid NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    CONSTRAINT "PK_HomepageSections" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_HomepageSections_Order" CHECK ("Order" >= 0),
    CONSTRAINT "CK_HomepageSections_Type" CHECK ("Type" IN ('hero', 'products', 'banner', 'categories')),
    CONSTRAINT "CK_HomepageSections_Data_Object" CHECK (jsonb_typeof("Data") = 'object')
);

CREATE INDEX IF NOT EXISTS "IX_HomepageSections_TenantId_Order"
    ON "HomepageSections" ("TenantId", "Order");

CREATE INDEX IF NOT EXISTS "IX_HomepageSections_TenantId_IsActive_Order"
    ON "HomepageSections" ("TenantId", "IsActive", "Order");

CREATE INDEX IF NOT EXISTS "IX_HomepageSections_IsActive_Order"
    ON "HomepageSections" ("IsActive", "Order");

COMMIT;
