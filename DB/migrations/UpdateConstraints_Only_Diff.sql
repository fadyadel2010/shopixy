
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


