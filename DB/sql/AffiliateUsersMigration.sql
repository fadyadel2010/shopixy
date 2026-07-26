START TRANSACTION;
ALTER TABLE "ProviderTrustAggregates" ADD "MarketingReviewCount" integer NOT NULL DEFAULT 0;

ALTER TABLE "ProviderTrustAggregates" ADD "MarketingWeightedScore" numeric NOT NULL DEFAULT 0.0;

ALTER TABLE "ProviderTrustAggregates" ADD "PosVerifiedCount" integer NOT NULL DEFAULT 0;

ALTER TABLE "ProviderTrustAggregates" ADD "TotalVerifiedOrders" integer NOT NULL DEFAULT 0;

ALTER TABLE "ProviderTrustAggregates" ADD "TotalVerifiedRevenue" numeric NOT NULL DEFAULT 0.0;

CREATE TABLE "AffiliateAccounts" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "ReferralCode" character varying(12) NOT NULL,
    "Status" integer NOT NULL,
    "DisplayName" character varying(200) NOT NULL,
    "ActivatedAtUtc" timestamp with time zone,
    "SuspendedAtUtc" timestamp with time zone,
    "SuspensionReason" character varying(500),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    CONSTRAINT "PK_AffiliateAccounts" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_AffiliateAccounts_Status" CHECK ("Status" IN (0, 1, 2)),
    CONSTRAINT "FK_AffiliateAccounts_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "AffiliateProductPolicies" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "CommissionRate" numeric(8,6) NOT NULL,
    "IsEnabled" boolean NOT NULL,
    "MaxCommissionPerItem" numeric(18,4),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    CONSTRAINT "PK_AffiliateProductPolicies" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_AffiliateProductPolicies_MaxCap" CHECK ("MaxCommissionPerItem" IS NULL OR "MaxCommissionPerItem" > 0),
    CONSTRAINT "CK_AffiliateProductPolicies_Rate" CHECK ("CommissionRate" >= 0.0001 AND "CommissionRate" <= 0.50),
    CONSTRAINT "FK_AffiliateProductPolicies_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "MarketingReviewEvidences" (
    "Id" uuid NOT NULL,
    "ServiceReviewId" uuid NOT NULL,
    "CampaignName" character varying(200) NOT NULL,
    "Platform" character varying(50) NOT NULL,
    "StartDate" date NOT NULL,
    "EndDate" date NOT NULL,
    "OrdersCount" integer,
    "RevenueAmount" numeric(18,2),
    "EvidenceType" character varying(20) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "TenantId" uuid NOT NULL,
    CONSTRAINT "PK_MarketingReviewEvidences" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MarketingReviewEvidences_ServiceReviews_ServiceReviewId" FOREIGN KEY ("ServiceReviewId") REFERENCES "ServiceReviews" ("Id") ON DELETE CASCADE
);

CREATE TABLE "AffiliateAttributions" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "AffiliateAccountId" uuid NOT NULL,
    "ReferralCode" character varying(12) NOT NULL,
    "Source" integer NOT NULL,
    "AttributedAtUtc" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    CONSTRAINT "PK_AffiliateAttributions" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_AffiliateAttributions_Source" CHECK ("Source" IN (0, 1, 2)),
    CONSTRAINT "FK_AffiliateAttributions_AffiliateAccounts_AffiliateAccountId" FOREIGN KEY ("AffiliateAccountId") REFERENCES "AffiliateAccounts" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AffiliateAttributions_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "AffiliateUsers" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Email" character varying(255) NOT NULL,
    "PasswordHash" character varying(500) NOT NULL,
    "AffiliateAccountId" uuid NOT NULL,
    "EmailVerified" boolean NOT NULL DEFAULT FALSE,
    "RefreshToken" character varying(500),
    "RefreshTokenExpiryTime" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    CONSTRAINT "PK_AffiliateUsers" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AffiliateUsers_AffiliateAccounts_AffiliateAccountId" FOREIGN KEY ("AffiliateAccountId") REFERENCES "AffiliateAccounts" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "AffiliateCommissionLedger" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid,
    "AttributionId" uuid NOT NULL,
    "AffiliateAccountId" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "OrderItemId" uuid NOT NULL,
    "BaseAmount" numeric(18,4) NOT NULL,
    "CommissionRate" numeric(8,6) NOT NULL,
    "CommissionAmount" numeric(18,4) NOT NULL,
    "Currency" character varying(3) NOT NULL,
    "EntryType" integer NOT NULL,
    "OriginalEntryId" uuid,
    "IdempotencyKey" character varying(255) NOT NULL,
    "RecordedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_AffiliateCommissionLedger" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_AffiliateCommLedger_Amount" CHECK ("CommissionAmount" > 0),
    CONSTRAINT "CK_AffiliateCommLedger_BaseAmount" CHECK ("BaseAmount" > 0),
    CONSTRAINT "CK_AffiliateCommLedger_EntryType" CHECK ("EntryType" IN (0, 1, 2, 3)),
    CONSTRAINT "CK_AffiliateCommLedger_Rate" CHECK ("CommissionRate" > 0 AND "CommissionRate" <= 0.50),
    CONSTRAINT "CK_AffiliateCommLedger_ReversalRef" CHECK (("EntryType" IN (1, 2, 3) AND "OriginalEntryId" IS NOT NULL) OR ("EntryType" = 0 AND "OriginalEntryId" IS NULL)),
    CONSTRAINT "FK_AffiliateCommissionLedger_AffiliateAccounts_AffiliateAccoun~" FOREIGN KEY ("AffiliateAccountId") REFERENCES "AffiliateAccounts" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AffiliateCommissionLedger_AffiliateAttributions_Attribution~" FOREIGN KEY ("AttributionId") REFERENCES "AffiliateAttributions" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AffiliateCommissionLedger_AffiliateCommissionLedger_Origina~" FOREIGN KEY ("OriginalEntryId") REFERENCES "AffiliateCommissionLedger" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AffiliateCommissionLedger_OrderItems_OrderItemId" FOREIGN KEY ("OrderItemId") REFERENCES "OrderItems" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AffiliateCommissionLedger_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_AffiliateAccounts_TenantId" ON "AffiliateAccounts" ("TenantId");

CREATE INDEX "IX_AffiliateAccounts_UserId" ON "AffiliateAccounts" ("UserId");

CREATE UNIQUE INDEX "UQ_AffiliateAccounts_Tenant_ReferralCode" ON "AffiliateAccounts" ("TenantId", "ReferralCode");

CREATE UNIQUE INDEX "UQ_AffiliateAccounts_Tenant_User" ON "AffiliateAccounts" ("TenantId", "UserId");

CREATE INDEX "IX_AffiliateAttributions_AffiliateAccountId" ON "AffiliateAttributions" ("AffiliateAccountId");

CREATE INDEX "IX_AffiliateAttributions_OrderId" ON "AffiliateAttributions" ("OrderId");

CREATE INDEX "IX_AffiliateAttributions_TenantId" ON "AffiliateAttributions" ("TenantId");

CREATE UNIQUE INDEX "UQ_AffiliateAttributions_Tenant_Order" ON "AffiliateAttributions" ("TenantId", "OrderId");

CREATE INDEX "IX_AffiliateCommissionLedger_AttributionId" ON "AffiliateCommissionLedger" ("AttributionId");

CREATE INDEX "IX_AffiliateCommissionLedger_OrderItemId" ON "AffiliateCommissionLedger" ("OrderItemId");

CREATE INDEX "IX_AffiliateCommissionLedger_OriginalEntryId" ON "AffiliateCommissionLedger" ("OriginalEntryId");

CREATE INDEX "IX_AffiliateCommLedger_AffiliateAccountId" ON "AffiliateCommissionLedger" ("AffiliateAccountId");

CREATE INDEX "IX_AffiliateCommLedger_OrderId" ON "AffiliateCommissionLedger" ("OrderId");

CREATE INDEX "IX_AffiliateCommLedger_TenantId" ON "AffiliateCommissionLedger" ("TenantId");

CREATE INDEX "IX_AffiliateCommLedger_TenantRecorded" ON "AffiliateCommissionLedger" ("TenantId", "RecordedAtUtc" DESC);

CREATE UNIQUE INDEX "UQ_AffiliateCommLedger_Idempotency" ON "AffiliateCommissionLedger" ("TenantId", "IdempotencyKey");

CREATE INDEX "IX_AffiliateProductPolicies_ProductId" ON "AffiliateProductPolicies" ("ProductId");

CREATE INDEX "IX_AffiliateProductPolicies_TenantId" ON "AffiliateProductPolicies" ("TenantId");

CREATE UNIQUE INDEX "UQ_AffiliateProductPolicies_Tenant_Product" ON "AffiliateProductPolicies" ("TenantId", "ProductId");

CREATE INDEX "IX_AffiliateUsers_TenantId" ON "AffiliateUsers" ("TenantId");

CREATE UNIQUE INDEX "UQ_AffiliateUsers_AffiliateAccount" ON "AffiliateUsers" ("AffiliateAccountId");

CREATE UNIQUE INDEX "UQ_AffiliateUsers_Email_Tenant" ON "AffiliateUsers" ("TenantId", "Email");

CREATE INDEX "IX_MarketingReviewEvidences_EvidenceType" ON "MarketingReviewEvidences" ("EvidenceType");

CREATE INDEX "IX_MarketingReviewEvidences_Platform" ON "MarketingReviewEvidences" ("Platform");

CREATE INDEX "IX_MarketingReviewEvidences_TenantId" ON "MarketingReviewEvidences" ("TenantId");

CREATE UNIQUE INDEX "UQ_MarketingReviewEvidences_ServiceReview" ON "MarketingReviewEvidences" ("ServiceReviewId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260220154537_AddAffiliateUsers_Retry', '9.0.1');

COMMIT;

