CREATE TABLE IF NOT EXISTS "ProductMedia" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "MediaType" character varying(20) NOT NULL,
    "Url" text NOT NULL,
    "Provider" character varying(20) NULL,
    "Position" integer NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ProductMedia" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ProductMedia_Products_ProductId"
        FOREIGN KEY ("ProductId")
        REFERENCES "Products" ("Id")
        ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_ProductMedia_ProductId_Position"
    ON "ProductMedia" ("ProductId", "Position");
