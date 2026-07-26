START TRANSACTION;
ALTER TABLE IF EXISTS "Accounts" DROP CONSTRAINT IF EXISTS "FK_Accounts_Accounts_ParentAccountId";

ALTER TABLE IF EXISTS "JournalEntries" DROP CONSTRAINT IF EXISTS "FK_JournalEntries_JournalEntries_ReversedByJournalId";

ALTER TABLE IF EXISTS "LedgerEntries" DROP CONSTRAINT IF EXISTS "FK_LedgerEntries_Accounts_AccountId";

ALTER TABLE IF EXISTS "LedgerEntries" DROP CONSTRAINT IF EXISTS "FK_LedgerEntries_JournalEntries_JournalEntryId";

ALTER TABLE IF EXISTS "LedgerEntries" DROP CONSTRAINT IF EXISTS "PK_LedgerEntries" CASCADE;

ALTER TABLE IF EXISTS "LedgerEntries" DROP CONSTRAINT IF EXISTS "CK_LedgerEntries_PositiveAmounts";

ALTER TABLE IF EXISTS "LedgerEntries" DROP CONSTRAINT IF EXISTS "CK_LedgerEntries_SingleSide";

ALTER TABLE IF EXISTS "JournalNumberSequences" DROP CONSTRAINT IF EXISTS "PK_JournalNumberSequences" CASCADE;

ALTER TABLE IF EXISTS "JournalEntries" DROP CONSTRAINT IF EXISTS "PK_JournalEntries" CASCADE;

ALTER TABLE IF EXISTS "Accounts" DROP CONSTRAINT IF EXISTS "PK_Accounts" CASCADE;

-- Cleanup for potential partial migration state (if tables already renamed)
ALTER TABLE IF EXISTS "LedgerLines" DROP CONSTRAINT IF EXISTS "FK_LedgerEntries_Accounts_AccountId";
ALTER TABLE IF EXISTS "LedgerLines" DROP CONSTRAINT IF EXISTS "FK_LedgerEntries_JournalEntries_JournalEntryId";
ALTER TABLE IF EXISTS "LedgerLines" DROP CONSTRAINT IF EXISTS "PK_LedgerEntries" CASCADE;
ALTER TABLE IF EXISTS "LedgerLines" DROP CONSTRAINT IF EXISTS "CK_LedgerEntries_PositiveAmounts";
ALTER TABLE IF EXISTS "LedgerLines" DROP CONSTRAINT IF EXISTS "CK_LedgerEntries_SingleSide";

ALTER TABLE IF EXISTS "LedgerJournalEntries" DROP CONSTRAINT IF EXISTS "FK_JournalEntries_JournalEntries_ReversedByJournalId";
ALTER TABLE IF EXISTS "LedgerJournalEntries" DROP CONSTRAINT IF EXISTS "PK_JournalEntries" CASCADE;

ALTER TABLE IF EXISTS "LedgerAccounts" DROP CONSTRAINT IF EXISTS "FK_Accounts_Accounts_ParentAccountId";
ALTER TABLE IF EXISTS "LedgerAccounts" DROP CONSTRAINT IF EXISTS "PK_Accounts" CASCADE;

DO $$ 
BEGIN 
    IF to_regclass('"LedgerLines"') IS NULL AND to_regclass('"LedgerEntries"') IS NOT NULL THEN 
        ALTER TABLE "LedgerEntries" RENAME TO "LedgerLines"; 
    END IF; 
END $$;

DO $$ 
BEGIN 
    IF to_regclass('"LedgerJournalNumberSequences"') IS NULL AND to_regclass('"JournalNumberSequences"') IS NOT NULL THEN 
        ALTER TABLE "JournalNumberSequences" RENAME TO "LedgerJournalNumberSequences"; 
    END IF; 
END $$;

DO $$ 
BEGIN 
    IF to_regclass('"LedgerJournalEntries"') IS NULL AND to_regclass('"JournalEntries"') IS NOT NULL THEN 
        ALTER TABLE "JournalEntries" RENAME TO "LedgerJournalEntries"; 
    END IF; 
END $$;

DO $$ 
BEGIN 
    IF to_regclass('"LedgerAccounts"') IS NULL AND to_regclass('"Accounts"') IS NOT NULL THEN 
        ALTER TABLE "Accounts" RENAME TO "LedgerAccounts"; 
    END IF; 
END $$;

DO $$ 
BEGIN 
    -- IX_LedgerLines_TenantAcct
    IF to_regclass('"IX_LedgerLines_TenantAcct"') IS NULL AND to_regclass('"IX_LedgerEntries_TenantId_AccountId"') IS NOT NULL THEN 
        ALTER INDEX "IX_LedgerEntries_TenantId_AccountId" RENAME TO "IX_LedgerLines_TenantAcct"; 
    END IF;
    -- IX_LedgerLines_Tenant
    IF to_regclass('"IX_LedgerLines_Tenant"') IS NULL AND to_regclass('"IX_LedgerEntries_TenantId"') IS NOT NULL THEN 
        ALTER INDEX "IX_LedgerEntries_TenantId" RENAME TO "IX_LedgerLines_Tenant"; 
    END IF;
    -- IX_LedgerLines_Journal
    IF to_regclass('"IX_LedgerLines_Journal"') IS NULL AND to_regclass('"IX_LedgerEntries_JournalEntryId"') IS NOT NULL THEN 
        ALTER INDEX "IX_LedgerEntries_JournalEntryId" RENAME TO "IX_LedgerLines_Journal"; 
    END IF;
    -- IX_LedgerLines_AcctDate
    IF to_regclass('"IX_LedgerLines_AcctDate"') IS NULL AND to_regclass('"IX_LedgerEntries_AccountId_CreatedAtUtc"') IS NOT NULL THEN 
        ALTER INDEX "IX_LedgerEntries_AccountId_CreatedAtUtc" RENAME TO "IX_LedgerLines_AcctDate"; 
    END IF;
    -- IX_LedgerJournal_Status
    IF to_regclass('"IX_LedgerJournal_Status"') IS NULL AND to_regclass('"IX_JournalEntries_TenantId_Status"') IS NOT NULL THEN 
        ALTER INDEX "IX_JournalEntries_TenantId_Status" RENAME TO "IX_LedgerJournal_Status"; 
    END IF;
    -- IX_LedgerJournal_Source
    IF to_regclass('"IX_LedgerJournal_Source"') IS NULL AND to_regclass('"IX_JournalEntries_TenantId_Source"') IS NOT NULL THEN 
        ALTER INDEX "IX_JournalEntries_TenantId_Source" RENAME TO "IX_LedgerJournal_Source"; 
    END IF;
    -- IX_LedgerJournal_Number
    IF to_regclass('"IX_LedgerJournal_Number"') IS NULL AND to_regclass('"IX_JournalEntries_TenantId_JournalNumber"') IS NOT NULL THEN 
        ALTER INDEX "IX_JournalEntries_TenantId_JournalNumber" RENAME TO "IX_LedgerJournal_Number"; 
    END IF;
    -- IX_LedgerJournal_Date
    IF to_regclass('"IX_LedgerJournal_Date"') IS NULL AND to_regclass('"IX_JournalEntries_TenantId_JournalDate"') IS NOT NULL THEN 
        ALTER INDEX "IX_JournalEntries_TenantId_JournalDate" RENAME TO "IX_LedgerJournal_Date"; 
    END IF;
    -- IX_LedgerJournal_Idempotency
    IF to_regclass('"IX_LedgerJournal_Idempotency"') IS NULL AND to_regclass('"IX_JournalEntries_TenantId_IdempotencyKey"') IS NOT NULL THEN 
        ALTER INDEX "IX_JournalEntries_TenantId_IdempotencyKey" RENAME TO "IX_LedgerJournal_Idempotency"; 
    END IF;
    -- IX_LedgerJournal_Tenant
    IF to_regclass('"IX_LedgerJournal_Tenant"') IS NULL AND to_regclass('"IX_JournalEntries_TenantId"') IS NOT NULL THEN 
        ALTER INDEX "IX_JournalEntries_TenantId" RENAME TO "IX_LedgerJournal_Tenant"; 
    END IF;
    -- IX_LedgerJournalEntries_ReversedByJournalId
    IF to_regclass('"IX_LedgerJournalEntries_ReversedByJournalId"') IS NULL AND to_regclass('"IX_JournalEntries_ReversedByJournalId"') IS NOT NULL THEN 
        ALTER INDEX "IX_JournalEntries_ReversedByJournalId" RENAME TO "IX_LedgerJournalEntries_ReversedByJournalId"; 
    END IF;
    -- IX_LedgerJournal_Posted
    IF to_regclass('"IX_LedgerJournal_Posted"') IS NULL AND to_regclass('"IX_JournalEntries_PostedAtUtc"') IS NOT NULL THEN 
        ALTER INDEX "IX_JournalEntries_PostedAtUtc" RENAME TO "IX_LedgerJournal_Posted"; 
    END IF;
    -- IX_LedgerAcct_Clearing
    IF to_regclass('"IX_LedgerAcct_Clearing"') IS NULL AND to_regclass('"IX_Accounts_TenantId_IsClearing"') IS NOT NULL THEN 
        ALTER INDEX "IX_Accounts_TenantId_IsClearing" RENAME TO "IX_LedgerAcct_Clearing"; 
    END IF;
    -- IX_LedgerAcct_TenantType
    IF to_regclass('"IX_LedgerAcct_TenantType"') IS NULL AND to_regclass('"IX_Accounts_TenantId_AccountType"') IS NOT NULL THEN 
        ALTER INDEX "IX_Accounts_TenantId_AccountType" RENAME TO "IX_LedgerAcct_TenantType"; 
    END IF;
    -- IX_LedgerAcct_TenantCode
    IF to_regclass('"IX_LedgerAcct_TenantCode"') IS NULL AND to_regclass('"IX_Accounts_TenantId_AccountCode"') IS NOT NULL THEN 
        ALTER INDEX "IX_Accounts_TenantId_AccountCode" RENAME TO "IX_LedgerAcct_TenantCode"; 
    END IF;
    -- IX_LedgerAcct_Tenant
    IF to_regclass('"IX_LedgerAcct_Tenant"') IS NULL AND to_regclass('"IX_Accounts_TenantId"') IS NOT NULL THEN 
        ALTER INDEX "IX_Accounts_TenantId" RENAME TO "IX_LedgerAcct_Tenant"; 
    END IF;
    -- IX_LedgerAcct_Parent
    IF to_regclass('"IX_LedgerAcct_Parent"') IS NULL AND to_regclass('"IX_Accounts_ParentAccountId"') IS NOT NULL THEN 
        ALTER INDEX "IX_Accounts_ParentAccountId" RENAME TO "IX_LedgerAcct_Parent"; 
    END IF;
END $$;

ALTER TABLE IF EXISTS "Orders" ADD COLUMN IF NOT EXISTS "FulfillmentCarrierCode" text;

ALTER TABLE IF EXISTS "Orders" ADD COLUMN IF NOT EXISTS "FulfillmentMode" integer NOT NULL DEFAULT 0;

ALTER TABLE IF EXISTS "Orders" ADD COLUMN IF NOT EXISTS "FulfillmentStatus" integer NOT NULL DEFAULT 0;

DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_LedgerLines') THEN ALTER TABLE "LedgerLines" ADD CONSTRAINT "PK_LedgerLines" PRIMARY KEY ("Id"); END IF; END $$;

DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_LedgerJournalNumberSequences') THEN ALTER TABLE "LedgerJournalNumberSequences" ADD CONSTRAINT "PK_LedgerJournalNumberSequences" PRIMARY KEY ("TenantId"); END IF; END $$;

DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_LedgerJournalEntries') THEN ALTER TABLE "LedgerJournalEntries" ADD CONSTRAINT "PK_LedgerJournalEntries" PRIMARY KEY ("Id"); END IF; END $$;

DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_LedgerAccounts') THEN ALTER TABLE "LedgerAccounts" ADD CONSTRAINT "PK_LedgerAccounts" PRIMARY KEY ("Id"); END IF; END $$;

CREATE TABLE IF NOT EXISTS "AccountingOutboxMessages" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "EventType" character varying(50) NOT NULL,
    "BusinessEntityId" uuid NOT NULL,
    "EntityTypeName" character varying(255),
    "PayloadJson" text NOT NULL,
    "IdempotencyKey" character varying(255) NOT NULL,
    "OccurredAtUtc" timestamp with time zone NOT NULL,
    "ProcessedAtUtc" timestamp with time zone,
    "JournalId" uuid,
    "AttemptCount" integer NOT NULL,
    "LastError" character varying(2000),
    "NextRetryAtUtc" timestamp with time zone,
    "IsFailed" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    CONSTRAINT "PK_AccountingOutboxMessages" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "CommerceAccountingAudit" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "BusinessType" character varying(50) NOT NULL,
    "BusinessId" uuid NOT NULL,
    "BusinessReference" character varying(100),
    "JournalId" uuid NOT NULL,
    "JournalNumber" character varying(50),
    "OperationType" character varying(50) NOT NULL,
    "Amount" numeric(18,4) NOT NULL,
    "PostedAtUtc" timestamp with time zone NOT NULL,
    "IdempotencyKey" character varying(255),
    "WasIdempotent" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    CONSTRAINT "PK_CommerceAccountingAudit" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_CommerceAccountingAudit_LedgerJournalEntries_JournalId" FOREIGN KEY ("JournalId") REFERENCES "LedgerJournalEntries" ("Id") ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS "MarketplaceProfiles" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "DisplayName" character varying(100) NOT NULL,
    "Bio" text,
    "ProfileImageUrl" character varying(500),
    "CoverImageUrl" text,
    "Location" text,
    "Skills" text,
    "PortfolioLinks" text,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "TenantId" uuid NOT NULL,
    CONSTRAINT "PK_MarketplaceProfiles" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "PaymentMethods" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Code" character varying(20) NOT NULL,
    "Name" character varying(100) NOT NULL,
    "NameAr" character varying(100),
    "TargetAccountId" uuid,
    "ClearingAccountId" uuid,
    "FeesAccountId" uuid,
    "IsSystem" boolean NOT NULL DEFAULT FALSE,
    "IsActive" boolean NOT NULL DEFAULT TRUE,
    "DisplayOrder" integer NOT NULL DEFAULT 0,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    CONSTRAINT "PK_PaymentMethods" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_PaymentMethods_LedgerAccounts_ClearingAccountId" FOREIGN KEY ("ClearingAccountId") REFERENCES "LedgerAccounts" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_PaymentMethods_LedgerAccounts_FeesAccountId" FOREIGN KEY ("FeesAccountId") REFERENCES "LedgerAccounts" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_PaymentMethods_LedgerAccounts_TargetAccountId" FOREIGN KEY ("TargetAccountId") REFERENCES "LedgerAccounts" ("Id") ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS "ProcessedTrustEvent" (
    "Id" uuid NOT NULL,
    "EventId" uuid NOT NULL,
    "EventType" text NOT NULL,
    "ProviderProfileId" uuid NOT NULL,
    "ProcessedAt" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "TenantId" uuid NOT NULL,
    CONSTRAINT "PK_ProcessedTrustEvent" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "TreasuryAccounts" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "AccountId" uuid NOT NULL,
    "Type" integer NOT NULL,
    "Name" character varying(100) NOT NULL,
    "NameAr" character varying(100),
    "ReferenceCode" character varying(50),
    "Location" character varying(100),
    "IsDefault" boolean NOT NULL DEFAULT FALSE,
    "IsActive" boolean NOT NULL DEFAULT TRUE,
    "DisplayOrder" integer NOT NULL DEFAULT 0,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    CONSTRAINT "PK_TreasuryAccounts" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_TreasuryAccounts_LedgerAccounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES "LedgerAccounts" ("Id") ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS "ProviderTrustAggregates" (
    "Id" uuid NOT NULL,
    "ProviderProfileId" uuid NOT NULL,
    "TotalReviews" integer NOT NULL,
    "SumRatings" integer NOT NULL,
    "AverageRating" numeric(3,2) NOT NULL,
    "FiveStarCount" integer NOT NULL,
    "FourStarCount" integer NOT NULL,
    "ThreeStarCount" integer NOT NULL,
    "TwoStarCount" integer NOT NULL,
    "OneStarCount" integer NOT NULL,
    "LastReviewAt" timestamp with time zone,
    "Version" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "TenantId" uuid NOT NULL,
    CONSTRAINT "PK_ProviderTrustAggregates" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ProviderTrustAggregates_MarketplaceProfiles_ProviderProfile~" FOREIGN KEY ("ProviderProfileId") REFERENCES "MarketplaceProfiles" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "Services" (
    "Id" uuid NOT NULL,
    "ProfileId" uuid NOT NULL,
    "Title" character varying(200) NOT NULL,
    "Description" text NOT NULL,
    "Category" character varying(100),
    "IsActive" boolean NOT NULL,
    "StartingPrice" numeric,
    "PricingType" integer,
    "Currency" text,
    "ServiceImages" text,
    "Packages" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "TenantId" uuid NOT NULL,
    CONSTRAINT "PK_Services" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Services_MarketplaceProfiles_ProfileId" FOREIGN KEY ("ProfileId") REFERENCES "MarketplaceProfiles" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "ServiceRequests" (
    "Id" uuid NOT NULL,
    "ServiceId" uuid NOT NULL,
    "BuyerUserId" uuid NOT NULL,
    "ProviderUserId" uuid NOT NULL,
    "Status" text NOT NULL,
    "RequestMessage" text,
    "AcceptedAt" timestamp with time zone,
    "CompletedByProviderAt" timestamp with time zone,
    "CompletedByBuyerAt" timestamp with time zone,
    "CancelledAt" timestamp with time zone,
    "CancellationReason" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "TenantId" uuid NOT NULL,
    CONSTRAINT "PK_ServiceRequests" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ServiceRequests_Services_ServiceId" FOREIGN KEY ("ServiceId") REFERENCES "Services" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "ServiceRequestMessages" (
    "Id" uuid NOT NULL,
    "ServiceRequestId" uuid NOT NULL,
    "SenderUserId" uuid NOT NULL,
    "SenderRole" text NOT NULL,
    "MessageType" text NOT NULL,
    "Body" text NOT NULL,
    "AttachmentUrl" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "TenantId" uuid NOT NULL,
    CONSTRAINT "PK_ServiceRequestMessages" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ServiceRequestMessages_ServiceRequests_ServiceRequestId" FOREIGN KEY ("ServiceRequestId") REFERENCES "ServiceRequests" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "ServiceReviews" (
    "Id" uuid NOT NULL,
    "ServiceRequestId" uuid NOT NULL,
    "ServiceId" uuid NOT NULL,
    "ReviewerUserId" uuid NOT NULL,
    "ProviderUserId" uuid NOT NULL,
    "Rating" integer NOT NULL,
    "Comment" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL,
    "TenantId" uuid NOT NULL,
    CONSTRAINT "PK_ServiceReviews" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ServiceReviews_ServiceRequests_ServiceRequestId" FOREIGN KEY ("ServiceRequestId") REFERENCES "ServiceRequests" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ServiceReviews_Services_ServiceId" FOREIGN KEY ("ServiceId") REFERENCES "Services" ("Id") ON DELETE CASCADE
);

DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_LedgerLines_PositiveAmounts') THEN ALTER TABLE "LedgerLines" ADD CONSTRAINT "CK_LedgerLines_PositiveAmounts" CHECK ("DebitAmount" >= 0 AND "CreditAmount" >= 0); END IF; END $$;

DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_LedgerLines_SingleSide') THEN ALTER TABLE "LedgerLines" ADD CONSTRAINT "CK_LedgerLines_SingleSide" CHECK (("DebitAmount" > 0 AND "CreditAmount" = 0) OR ("DebitAmount" = 0 AND "CreditAmount" > 0)); END IF; END $$;

CREATE INDEX IF NOT EXISTS "IX_AccountingOutbox_Failed" ON "AccountingOutboxMessages" ("TenantId", "IsFailed") WHERE "IsFailed" = true;

CREATE INDEX IF NOT EXISTS "IX_AccountingOutbox_OccurredAtUtc" ON "AccountingOutboxMessages" ("OccurredAtUtc");

CREATE INDEX IF NOT EXISTS "IX_AccountingOutbox_ProcessingStatus" ON "AccountingOutboxMessages" ("ProcessedAtUtc", "IsFailed", "NextRetryAtUtc") WHERE "ProcessedAtUtc" IS NULL AND "IsFailed" = false;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AccountingOutbox_TenantId_IdempotencyKey" ON "AccountingOutboxMessages" ("TenantId", "IdempotencyKey");

CREATE INDEX IF NOT EXISTS "IX_CommerceAudit_Business" ON "CommerceAccountingAudit" ("TenantId", "BusinessType", "BusinessId");

CREATE INDEX IF NOT EXISTS "IX_CommerceAudit_JournalId" ON "CommerceAccountingAudit" ("JournalId");

CREATE INDEX IF NOT EXISTS "IX_CommerceAudit_TenantId_PostedAtUtc" ON "CommerceAccountingAudit" ("TenantId", "PostedAtUtc");

CREATE INDEX IF NOT EXISTS "IX_MarketplaceProfiles_IsActive" ON "MarketplaceProfiles" ("IsActive") WHERE "IsDeleted" = false;

CREATE INDEX IF NOT EXISTS "IX_MarketplaceProfiles_TenantId" ON "MarketplaceProfiles" ("TenantId");

CREATE INDEX IF NOT EXISTS "IX_MarketplaceProfiles_UserId" ON "MarketplaceProfiles" ("UserId");

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_MarketplaceProfiles_TenantUser" ON "MarketplaceProfiles" ("TenantId", "UserId");

CREATE INDEX IF NOT EXISTS "IX_PaymentMethods_ClearingAccountId" ON "PaymentMethods" ("ClearingAccountId");

CREATE INDEX IF NOT EXISTS "IX_PaymentMethods_FeesAccountId" ON "PaymentMethods" ("FeesAccountId");

CREATE INDEX IF NOT EXISTS "IX_PaymentMethods_TargetAccountId" ON "PaymentMethods" ("TargetAccountId");

CREATE INDEX IF NOT EXISTS "IX_PaymentMethods_Tenant" ON "PaymentMethods" ("TenantId");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_PaymentMethods_TenantCode" ON "PaymentMethods" ("TenantId", "Code");

CREATE INDEX IF NOT EXISTS "IX_ProviderTrustAggregates_AverageRating" ON "ProviderTrustAggregates" ("AverageRating" DESC) WHERE "IsDeleted" = false;

CREATE INDEX IF NOT EXISTS "IX_ProviderTrustAggregates_TenantId" ON "ProviderTrustAggregates" ("TenantId");

CREATE INDEX IF NOT EXISTS "IX_ProviderTrustAggregates_TotalReviews" ON "ProviderTrustAggregates" ("TotalReviews" DESC) WHERE "IsDeleted" = false;

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_ProviderTrustAggregates_ProviderProfile" ON "ProviderTrustAggregates" ("ProviderProfileId");

CREATE INDEX IF NOT EXISTS "IX_ServiceRequestMessages_SenderUserId" ON "ServiceRequestMessages" ("SenderUserId");

CREATE INDEX IF NOT EXISTS "IX_ServiceRequestMessages_ServiceRequestId_CreatedAt" ON "ServiceRequestMessages" ("ServiceRequestId", "CreatedAt");

CREATE INDEX IF NOT EXISTS "IX_ServiceRequestMessages_TenantId" ON "ServiceRequestMessages" ("TenantId");

CREATE INDEX IF NOT EXISTS "IX_ServiceRequests_BuyerUserId" ON "ServiceRequests" ("BuyerUserId");

CREATE INDEX IF NOT EXISTS "IX_ServiceRequests_ProviderUserId" ON "ServiceRequests" ("ProviderUserId");

CREATE INDEX IF NOT EXISTS "IX_ServiceRequests_ServiceId" ON "ServiceRequests" ("ServiceId");

CREATE INDEX IF NOT EXISTS "IX_ServiceRequests_Status" ON "ServiceRequests" ("Status") WHERE "IsDeleted" = false;

CREATE INDEX IF NOT EXISTS "IX_ServiceRequests_TenantId" ON "ServiceRequests" ("TenantId");

CREATE INDEX IF NOT EXISTS "IX_ServiceReviews_ProviderUserId" ON "ServiceReviews" ("ProviderUserId");

CREATE INDEX IF NOT EXISTS "IX_ServiceReviews_Rating" ON "ServiceReviews" ("Rating") WHERE "IsDeleted" = false;

CREATE INDEX IF NOT EXISTS "IX_ServiceReviews_ServiceId" ON "ServiceReviews" ("ServiceId");

CREATE INDEX IF NOT EXISTS "IX_ServiceReviews_TenantId" ON "ServiceReviews" ("TenantId");

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_ServiceReviews_ServiceRequest" ON "ServiceReviews" ("ServiceRequestId");

CREATE INDEX IF NOT EXISTS "IX_Services_Category" ON "Services" ("Category") WHERE "IsDeleted" = false AND "IsActive" = true;

CREATE INDEX IF NOT EXISTS "IX_Services_ProfileId" ON "Services" ("ProfileId");

CREATE INDEX IF NOT EXISTS "IX_Services_TenantId" ON "Services" ("TenantId");

CREATE INDEX IF NOT EXISTS "IX_TreasuryAccounts_AccountId" ON "TreasuryAccounts" ("AccountId");

CREATE INDEX IF NOT EXISTS "IX_TreasuryAccounts_Tenant" ON "TreasuryAccounts" ("TenantId");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_TreasuryAccounts_TenantAccount" ON "TreasuryAccounts" ("TenantId", "AccountId");

CREATE INDEX IF NOT EXISTS "IX_TreasuryAccounts_TenantType" ON "TreasuryAccounts" ("TenantId", "Type");

DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_LedgerAccounts_LedgerAccounts_ParentAccountId') THEN ALTER TABLE "LedgerAccounts" ADD CONSTRAINT "FK_LedgerAccounts_LedgerAccounts_ParentAccountId" FOREIGN KEY ("ParentAccountId") REFERENCES "LedgerAccounts" ("Id") ON DELETE RESTRICT; END IF; END $$;

DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_LedgerJournalEntries_LedgerJournalEntries_ReversedByJournalId') THEN ALTER TABLE "LedgerJournalEntries" ADD CONSTRAINT "FK_LedgerJournalEntries_LedgerJournalEntries_ReversedByJournalId" FOREIGN KEY ("ReversedByJournalId") REFERENCES "LedgerJournalEntries" ("Id") ON DELETE RESTRICT; END IF; END $$;

DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_LedgerLines_LedgerAccounts_AccountId') THEN ALTER TABLE "LedgerLines" ADD CONSTRAINT "FK_LedgerLines_LedgerAccounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES "LedgerAccounts" ("Id") ON DELETE RESTRICT; END IF; END $$;

CREATE INDEX IF NOT EXISTS "IX_ServiceRequests_BuyerUserId_Status" ON "ServiceRequests" ("BuyerUserId", "Status");

CREATE INDEX IF NOT EXISTS "IX_ServiceRequests_ProviderUserId_Status" ON "ServiceRequests" ("ProviderUserId", "Status");

DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_LedgerLines_LedgerJournalEntries_JournalEntryId') THEN ALTER TABLE "LedgerLines" ADD CONSTRAINT "FK_LedgerLines_LedgerJournalEntries_JournalEntryId" FOREIGN KEY ("JournalEntryId") REFERENCES "LedgerJournalEntries" ("Id") ON DELETE CASCADE; END IF; END $$;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260213225438_AddServiceRequestCompositeIndexes', '9.0.1');

COMMIT;

