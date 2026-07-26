-- Add Banks table migration
START TRANSACTION;

CREATE TABLE "Banks" (
    "Id" uuid NOT NULL,
    "BankName" text NOT NULL,
    "BranchName" text,
    "AccountNumber" text,
    "IBAN" text,
    "Notes" text,
    "IsActive" boolean NOT NULL,
    "OpeningBalanceAmount" numeric(18,2),
    "OpeningBalanceDate" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    CONSTRAINT "PK_Banks" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX "IX_Banks_BankName" ON "Banks" ("BankName") WHERE "IsDeleted" = FALSE;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251202174743_AddBanksTable', '9.0.0');

COMMIT;
