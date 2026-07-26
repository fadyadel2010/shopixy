CREATE TABLE IF NOT EXISTS "AffiliatePayoutTransferEvidence" (
    "Id" uuid PRIMARY KEY,
    "StoreTenantId" uuid NOT NULL,
    "RailNamespace" character varying(80) NOT NULL,
    "PaymentReference" character varying(200) NOT NULL,
    "NormalizedPaymentReference" character varying(200) NOT NULL,
    "ExpectedAmount" numeric(18,4) NOT NULL,
    "AllocatedAmount" numeric(18,4) NOT NULL DEFAULT 0,
    "Currency" character varying(3) NOT NULL,
    "LastAllocationAtUtc" timestamp with time zone NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "CK_AffiliatePayoutTransferEvidence_ExpectedAmount_Positive" CHECK ("ExpectedAmount" > 0),
    CONSTRAINT "CK_AffiliatePayoutTransferEvidence_AllocatedAmount_Valid" CHECK ("AllocatedAmount" >= 0 AND "AllocatedAmount" <= "ExpectedAmount"),
    CONSTRAINT "CK_AffiliatePayoutTransferEvidence_Tenant_Required" CHECK ("StoreTenantId" <> '00000000-0000-0000-0000-000000000000')
);

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_AffiliatePayoutTransferEvidence_Rail_Reference"
    ON "AffiliatePayoutTransferEvidence" ("RailNamespace", "NormalizedPaymentReference");

CREATE INDEX IF NOT EXISTS "IX_AffiliatePayoutTransferEvidence_Tenant_CreatedAt"
    ON "AffiliatePayoutTransferEvidence" ("StoreTenantId", "CreatedAt");

CREATE TABLE IF NOT EXISTS "AffiliatePayoutTransferAllocations" (
    "Id" uuid PRIMARY KEY,
    "EvidenceId" uuid NOT NULL,
    "StoreTenantId" uuid NOT NULL,
    "AffiliateAccountId" uuid NOT NULL,
    "ReferenceId" uuid NOT NULL,
    "SettlementSource" integer NOT NULL,
    "Amount" numeric(18,4) NOT NULL,
    "Currency" character varying(3) NOT NULL,
    "IdempotencyKey" character varying(255) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "FK_AffiliatePayoutTransferAllocations_Evidence"
        FOREIGN KEY ("EvidenceId") REFERENCES "AffiliatePayoutTransferEvidence" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "CK_AffiliatePayoutTransferAllocation_Amount_Positive" CHECK ("Amount" > 0),
    CONSTRAINT "CK_AffiliatePayoutTransferAllocation_Tenant_Required" CHECK ("StoreTenantId" <> '00000000-0000-0000-0000-000000000000'),
    CONSTRAINT "CK_AffiliatePayoutTransferAllocation_Affiliate_Required" CHECK ("AffiliateAccountId" <> '00000000-0000-0000-0000-000000000000'),
    CONSTRAINT "CK_AffiliatePayoutTransferAllocation_Reference_Required" CHECK ("ReferenceId" <> '00000000-0000-0000-0000-000000000000')
);

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_AffiliatePayoutTransferAllocation_Idempotency"
    ON "AffiliatePayoutTransferAllocations" ("IdempotencyKey");

CREATE INDEX IF NOT EXISTS "IX_AffiliatePayoutTransferAllocation_EvidenceId"
    ON "AffiliatePayoutTransferAllocations" ("EvidenceId");

CREATE INDEX IF NOT EXISTS "IX_AffiliatePayoutTransferAllocation_Tenant_CreatedAt"
    ON "AffiliatePayoutTransferAllocations" ("StoreTenantId", "CreatedAt");

CREATE TABLE IF NOT EXISTS "MarketplaceEventOutboxDeadLetterArchives" (
    "Id" uuid PRIMARY KEY,
    "OriginalOutboxMessageId" uuid NOT NULL,
    "EventId" uuid NOT NULL,
    "EventType" character varying(200) NOT NULL,
    "ClrType" character varying(1000) NOT NULL,
    "PayloadJson" text NOT NULL,
    "EntityId" uuid NOT NULL,
    "AggregateRootId" uuid NOT NULL,
    "TenantId" uuid NULL,
    "CorrelationId" character varying(200) NOT NULL,
    "Version" integer NOT NULL,
    "OccurredAtUtc" timestamp with time zone NOT NULL,
    "FailedAtUtc" timestamp with time zone NULL,
    "AttemptCount" integer NOT NULL,
    "LastError" character varying(2000) NULL,
    "ArchivedAtUtc" timestamp with time zone NOT NULL,
    "ArchiveReason" character varying(200) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "CK_MarketplaceEventOutboxDeadLetterArchive_OriginalId_Required"
        CHECK ("OriginalOutboxMessageId" <> '00000000-0000-0000-0000-000000000000')
);

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_MarketplaceEventOutboxDeadLetterArchives_OriginalId"
    ON "MarketplaceEventOutboxDeadLetterArchives" ("OriginalOutboxMessageId");

CREATE INDEX IF NOT EXISTS "IX_MarketplaceEventOutboxDeadLetterArchives_Tenant_Aggregate"
    ON "MarketplaceEventOutboxDeadLetterArchives" ("TenantId", "AggregateRootId");

CREATE INDEX IF NOT EXISTS "IX_MarketplaceEventOutboxDeadLetterArchives_ArchivedAtUtc"
    ON "MarketplaceEventOutboxDeadLetterArchives" ("ArchivedAtUtc");
