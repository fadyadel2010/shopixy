Build started...
Build succeeded.
The Entity Framework tools version '9.0.0' is older than that of the runtime '9.0.1'. Update the tools for the latest features and bug fixes. See https://aka.ms/AAc1fbw for more information.
START TRANSACTION;
ALTER TABLE "AffiliateProductPolicies" ADD "AffiliateCategoryId" uuid;

CREATE TABLE "AffiliateCategories" (
    "Id" uuid NOT NULL,
    "Name" character varying(100) NOT NULL,
    "Slug" character varying(100) NOT NULL,
    "ParentId" uuid,
    "Level" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    CONSTRAINT "PK_AffiliateCategories" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_AffiliateCategories_Level" CHECK ("Level" IN (1, 2)),
    CONSTRAINT "CK_AffiliateCategories_Level1_NoParent" CHECK (("Level" = 1 AND "ParentId" IS NULL) OR ("Level" = 2 AND "ParentId" IS NOT NULL)),
    CONSTRAINT "FK_AffiliateCategories_AffiliateCategories_ParentId" FOREIGN KEY ("ParentId") REFERENCES "AffiliateCategories" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_AffiliateProductPolicies_AffiliateCategoryId" ON "AffiliateProductPolicies" ("AffiliateCategoryId");

ALTER TABLE "AffiliateProductPolicies" ADD CONSTRAINT "CK_AffiliateProductPolicies_EnabledRequiresCategory" CHECK (NOT ("IsEnabled" = true AND "AffiliateCategoryId" IS NULL));

CREATE INDEX "IX_AffiliateCategories_DisplayOrder" ON "AffiliateCategories" ("DisplayOrder");

CREATE INDEX "IX_AffiliateCategories_IsActive" ON "AffiliateCategories" ("IsActive");

CREATE INDEX "IX_AffiliateCategories_ParentId" ON "AffiliateCategories" ("ParentId");

CREATE UNIQUE INDEX "UQ_AffiliateCategories_Parent_Name" ON "AffiliateCategories" ("ParentId", "Name");

CREATE UNIQUE INDEX "UQ_AffiliateCategories_Slug" ON "AffiliateCategories" ("Slug");

ALTER TABLE "AffiliateProductPolicies" ADD CONSTRAINT "FK_AffiliateProductPolicies_AffiliateCategories_AffiliateCateg~" FOREIGN KEY ("AffiliateCategoryId") REFERENCES "AffiliateCategories" ("Id") ON DELETE RESTRICT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260227125054_AF30A_FIX_D2_EnabledRequiresCategory', '9.0.1');

ALTER TABLE "AffiliateCommissionLedger" DROP CONSTRAINT "CK_AffiliateCommLedger_EntryType";

ALTER TABLE "AffiliateCommissionLedger" DROP CONSTRAINT "CK_AffiliateCommLedger_Rate";

ALTER TABLE "AffiliateCommissionLedger" DROP CONSTRAINT "CK_AffiliateCommLedger_ReversalRef";

ALTER TABLE "AffiliateCommissionLedger" ADD "PayoutRequestId" uuid;

CREATE TABLE "AffiliatePayoutRequests" (
    "Id" uuid NOT NULL,
    "StoreTenantId" uuid NOT NULL,
    "AffiliateAccountId" uuid NOT NULL,
    "RequestedAmount" numeric(18,4) NOT NULL,
    "ApprovedAmount" numeric(18,4),
    "RemainingAmount" numeric(18,4) NOT NULL,
    "Status" integer NOT NULL,
    "RequestedAtUtc" timestamp with time zone NOT NULL,
    "ApprovedAtUtc" timestamp with time zone,
    "RejectedAtUtc" timestamp with time zone,
    "PaidAtUtc" timestamp with time zone,
    "AdminNotes" character varying(1000),
    "PaymentReference" character varying(500),
    "Currency" character varying(3) NOT NULL,
    CONSTRAINT "PK_AffiliatePayoutRequests" PRIMARY KEY ("Id")
);

ALTER TABLE "AffiliateCommissionLedger" ADD CONSTRAINT "CK_AffiliateCommLedger_EntryType" CHECK ("EntryType" IN (0, 1, 2, 3, 4));

ALTER TABLE "AffiliateCommissionLedger" ADD CONSTRAINT "CK_AffiliateCommLedger_Rate" CHECK (("CommissionRate" > 0 AND "CommissionRate" <= 0.50) OR ("EntryType" = 4 AND "CommissionRate" = 0));

ALTER TABLE "AffiliateCommissionLedger" ADD CONSTRAINT "CK_AffiliateCommLedger_ReversalRef" CHECK (("EntryType" IN (1, 2, 3) AND "OriginalEntryId" IS NOT NULL) OR ("EntryType" IN (0, 4) AND "OriginalEntryId" IS NULL));

CREATE INDEX "IX_AffiliatePayoutRequests_AffiliateId_Status" ON "AffiliatePayoutRequests" ("AffiliateAccountId", "Status");

CREATE INDEX "IX_AffiliatePayoutRequests_TenantId_AffiliateId_Status" ON "AffiliatePayoutRequests" ("StoreTenantId", "AffiliateAccountId", "Status");

CREATE INDEX "IX_AffiliatePayoutRequests_TenantId_Status" ON "AffiliatePayoutRequests" ("StoreTenantId", "Status");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260227220116_AF38_HOTFIX_Constraints_Update', '9.0.1');

COMMIT;


