CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "AccountingOutboxMessages" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "EventType" character varying(50) NOT NULL,
        "SourceType" character varying(50) NOT NULL,
        "BusinessEntityId" uuid NOT NULL,
        "EntityTypeName" character varying(255),
        "PayloadJson" text NOT NULL,
        "IdempotencyKey" character varying(255) NOT NULL,
        "OccurredAtUtc" timestamp with time zone NOT NULL,
        "AggregateVersion" integer,
        "ProcessedAtUtc" timestamp with time zone,
        "JournalId" uuid,
        "AttemptCount" integer NOT NULL,
        "ClaimedUntilUtc" timestamp with time zone,
        "LastError" character varying(2000),
        "NextRetryAtUtc" timestamp with time zone,
        "IsFailed" boolean NOT NULL,
        "CorrelationId" character varying(100),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_AccountingOutboxMessages" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AccountingOutbox_OrderFinancialEvents_AggregateVersion" CHECK ("EventType" NOT IN ('OnlinePayment', 'CodDelivered', 'WalletApproved', 'EcommerceRefund') OR ("AggregateVersion" IS NOT NULL AND "AggregateVersion" > 0))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
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
        "ClosedAtUtc" timestamp with time zone,
        "ClosureReason" character varying(500),
        "PayoutWalletNumber" text,
        "PayoutWalletType" text,
        "PayoutWalletUpdatedAtUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_AffiliateAccounts" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AffiliateAccounts_Status" CHECK ("Status" IN (0, 1, 2, 3))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
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
        "ApprovedBy" uuid,
        "RejectedBy" uuid,
        "PaidBy" uuid,
        "AdminNotes" character varying(1000),
        "PaymentReference" character varying(500),
        "Currency" character varying(3) NOT NULL,
        "PayoutWalletNumberSnapshot" character varying(128),
        "PayoutWalletTypeSnapshot" character varying(64),
        "PayoutWalletUpdatedAtUtcSnapshot" timestamp with time zone,
        "PayoutWalletSnapshotCapturedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AffiliatePayoutRequests" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "AffiliatePayoutTransferEvidence" (
        "Id" uuid NOT NULL,
        "StoreTenantId" uuid NOT NULL,
        "RailNamespace" character varying(80) NOT NULL,
        "PaymentReference" character varying(200) NOT NULL,
        "NormalizedPaymentReference" character varying(200) NOT NULL,
        "ExpectedAmount" numeric(18,4) NOT NULL,
        "AllocatedAmount" numeric(18,4) NOT NULL,
        "Currency" character varying(3) NOT NULL,
        "VerifiedAtUtc" timestamp with time zone NOT NULL,
        "VerificationSource" character varying(80) NOT NULL,
        "VerificationFingerprint" character varying(64) NOT NULL,
        "LastAllocationAtUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_AffiliatePayoutTransferEvidence" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AffiliatePayoutTransferEvidence_AllocatedAmount_Valid" CHECK ("AllocatedAmount" >= 0 AND "AllocatedAmount" <= "ExpectedAmount"),
        CONSTRAINT "CK_AffiliatePayoutTransferEvidence_Currency_NotBlank" CHECK (btrim("Currency", E' \t\n\r') <> ''),
        CONSTRAINT "CK_AffiliatePayoutTransferEvidence_ExpectedAmount_Positive" CHECK ("ExpectedAmount" > 0),
        CONSTRAINT "CK_AffiliatePayoutTransferEvidence_NormalizedPaymentReference_~" CHECK (btrim("NormalizedPaymentReference", E' \t\n\r') <> ''),
        CONSTRAINT "CK_AffiliatePayoutTransferEvidence_PaymentReference_NotBlank" CHECK (btrim("PaymentReference", E' \t\n\r') <> ''),
        CONSTRAINT "CK_AffiliatePayoutTransferEvidence_RailNamespace_NotBlank" CHECK (btrim("RailNamespace", E' \t\n\r') <> ''),
        CONSTRAINT "CK_AffiliatePayoutTransferEvidence_Tenant_Required" CHECK ("StoreTenantId" <> '00000000-0000-0000-0000-000000000000')
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "AnalyticsAlertRules" (
        "RuleId" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Description" character varying(500),
        "Metric" integer NOT NULL,
        "Operator" integer NOT NULL,
        "Threshold" numeric(18,4) NOT NULL,
        "WindowMinutes" integer NOT NULL,
        "Severity" integer NOT NULL,
        "IsEnabled" boolean NOT NULL,
        "CooldownMinutes" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_AnalyticsAlertRules" PRIMARY KEY ("RuleId")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "AuditLogs" (
        "LogId" bigint GENERATED BY DEFAULT AS IDENTITY,
        "TenantId" uuid,
        "TimestampUtc" timestamp with time zone NOT NULL,
        "UserId" uuid,
        "UserNameSnapshot" character varying(150),
        "ActionType" character varying(50) NOT NULL,
        "EntityType" character varying(50) NOT NULL,
        "EntityId" character varying(50) NOT NULL,
        "Description" text NOT NULL,
        "BeforeDataJson" text,
        "AfterDataJson" text,
        "IpAddress" character varying(50),
        "UserAgent" character varying(500),
        "ReferenceNumber" character varying(100),
        "Source" character varying(50),
        "SuccessFlag" boolean NOT NULL,
        CONSTRAINT "PK_AuditLogs" PRIMARY KEY ("LogId")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "Banks" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "BankName" text NOT NULL,
        "BranchName" text,
        "AccountNumber" text,
        "IBAN" text,
        "Notes" text,
        "IsActive" boolean NOT NULL,
        "OpeningBalanceAmount" numeric,
        "OpeningBalanceDate" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_Banks" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CampaignCosts" (
        "CampaignCostId" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Source" character varying(100) NOT NULL,
        "Medium" character varying(100) NOT NULL,
        "Campaign" character varying(200) NOT NULL,
        "PeriodStartUtc" timestamp with time zone NOT NULL,
        "PeriodEndUtc" timestamp with time zone NOT NULL,
        "CostAmount" numeric(18,2) NOT NULL,
        "Currency" character varying(3) NOT NULL,
        "Notes" character varying(500),
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CampaignCosts" PRIMARY KEY ("CampaignCostId")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CanonicalIdentityLinks" (
        "Id" uuid NOT NULL,
        "CanonicalIdentityId" uuid NOT NULL,
        "SubjectType" integer NOT NULL,
        "SubjectId" uuid NOT NULL,
        "TenantId" uuid,
        "SourceSystem" character varying(64) NOT NULL,
        "DisplayKey" character varying(255),
        "LinkedAtUtc" timestamp with time zone NOT NULL,
        "RevokedAtUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CanonicalIdentityLinks" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CarrierAccountPolicies" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CarrierAccountId" uuid NOT NULL,
        "Scope" character varying(40) NOT NULL,
        "Mode" character varying(40) NOT NULL,
        "CustomsReadinessPublished" boolean NOT NULL,
        "ReadinessEvidenceReference" character varying(300),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CarrierAccountPolicies" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CarrierAccounts" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CarrierCode" character varying(50) NOT NULL,
        "Environment" character varying(40),
        "ActiveCredentialReferenceId" uuid,
        "State" character varying(40) NOT NULL,
        "OperationalState" character varying(40) NOT NULL,
        "HealthState" character varying(40) NOT NULL,
        "HealthReason" character varying(1000),
        "LastHealthEvaluatedAtUtc" timestamp with time zone,
        "ActivatedAtUtc" timestamp with time zone,
        "SuspendedAtUtc" timestamp with time zone,
        "DisabledAtUtc" timestamp with time zone,
        "RetiredAtUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CarrierAccounts" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CarrierAdministrationAudit" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CarrierAccountId" uuid NOT NULL,
        "Action" character varying(80) NOT NULL,
        "ActorId" character varying(120) NOT NULL,
        "CorrelationId" character varying(120),
        "PreviousState" character varying(80),
        "NextState" character varying(80),
        "Reason" character varying(1000),
        "OccurredAtUtc" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CarrierAdministrationAudit" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CarrierConfigurations" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CarrierCode" character varying(50) NOT NULL,
        "DisplayName" character varying(100) NOT NULL,
        "IsEnabled" boolean NOT NULL,
        "IsDefault" boolean NOT NULL,
        "ApiKey" character varying(500),
        "ApiSecret" character varying(500),
        "AccountId" character varying(100),
        "ApiUrl" character varying(500),
        "UseSandbox" boolean NOT NULL,
        "SupportsCod" boolean NOT NULL,
        "SupportsTracking" boolean NOT NULL,
        "SupportsWebhooks" boolean NOT NULL,
        "SupportsLabels" boolean NOT NULL,
        "SupportsQuotes" boolean NOT NULL,
        "SupportsCancellation" boolean NOT NULL,
        "Configuration" text,
        "WebhookUrl" character varying(500),
        "WebhookSecret" character varying(256),
        "PickupAddress" character varying(500),
        "PickupCity" character varying(100),
        "PickupCountry" character varying(10),
        "PickupPhone" character varying(50),
        "PickupContactName" character varying(100),
        "AvailableServiceLevels" text,
        "DefaultServiceLevel" character varying(50) NOT NULL,
        "UseCarrierRates" boolean NOT NULL,
        "FixedShippingCost" numeric(18,2),
        "MarkupPercentage" numeric(5,2) NOT NULL,
        "FreeShippingThreshold" numeric(18,2),
        "SortOrder" integer NOT NULL,
        "Notes" character varying(1000),
        "LastValidatedAtUtc" timestamp with time zone,
        "LastValidationSucceeded" boolean,
        "LastValidationError" character varying(1000),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CarrierConfigurations" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CarrierConnectionTestRecords" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CarrierAccountId" uuid NOT NULL,
        "CredentialReferenceId" uuid,
        "Environment" character varying(40) NOT NULL,
        "Status" character varying(40) NOT NULL,
        "FailureCode" character varying(100),
        "FailureReason" character varying(1000),
        "TestedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CarrierConnectionTestRecords" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CarrierCredentialReferences" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CarrierAccountId" uuid NOT NULL,
        "SecretReference" character varying(300) NOT NULL,
        "MaskedFingerprint" character varying(120),
        "State" character varying(40) NOT NULL,
        "ExpiresAtUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CarrierCredentialReferences" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CarrierProviders" (
        "Id" uuid NOT NULL,
        "CarrierCode" character varying(64) NOT NULL,
        "DisplayName" character varying(128) NOT NULL,
        "SupportsGeographicMappings" boolean NOT NULL,
        "RequiresMappingForDelivery" boolean NOT NULL,
        "IsActive" boolean NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_CarrierProviders" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CarrierServiceLevelEnablements" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CarrierAccountId" uuid NOT NULL,
        "ServiceLevelCode" character varying(80) NOT NULL,
        "IsEnabled" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CarrierServiceLevelEnablements" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CarrierWarehouseAssignments" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CarrierAccountId" uuid NOT NULL,
        "WarehouseId" uuid NOT NULL,
        "IsEnabled" boolean NOT NULL,
        "OriginReadinessPublished" boolean NOT NULL,
        "ReadinessEvidenceReference" character varying(300),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CarrierWarehouseAssignments" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "Categories" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Name" text NOT NULL,
        "Description" text,
        "ParentCategoryId" uuid,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_Categories" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Categories_Categories_ParentCategoryId" FOREIGN KEY ("ParentCategoryId") REFERENCES "Categories" ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CmsPages" (
        "Id" uuid NOT NULL,
        "PageGroupId" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Slug" character varying(160) NOT NULL,
        "Title" character varying(200) NOT NULL,
        "Content" text NOT NULL,
        "ContentType" character varying(20) NOT NULL,
        "Version" integer NOT NULL,
        "SchemaVersion" integer NOT NULL,
        "IsDeleted" boolean NOT NULL,
        "IsDraft" boolean NOT NULL,
        "IsPublished" boolean NOT NULL,
        "PublishedAt" timestamp with time zone,
        "MetaTitle" character varying(200),
        "MetaDescription" character varying(320),
        "MetaKeywords" character varying(500),
        "OpenGraphImageUrl" character varying(1000),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_CmsPages" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CmsPages_ContentType" CHECK ("ContentType" IN ('RichText', 'Blocks')),
        CONSTRAINT "CK_CmsPages_PublishedAt" CHECK (NOT "IsPublished" OR "PublishedAt" IS NOT NULL),
        CONSTRAINT "CK_CmsPages_SchemaVersion" CHECK ("SchemaVersion" > 0),
        CONSTRAINT "CK_CmsPages_State" CHECK (NOT ("IsDraft" AND "IsPublished")),
        CONSTRAINT "CK_CmsPages_Version" CHECK ("Version" > 0)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubCarts" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CommerceHubCustomerId" uuid,
        "OnlineCustomerId" uuid NOT NULL,
        "ItemsJson" text NOT NULL,
        "Version" integer NOT NULL DEFAULT 1,
        "IsLocked" boolean NOT NULL,
        "LockedAtUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CommerceHubCarts" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CommerceHubCarts_CustomerAuthorityBoundary" CHECK ("CommerceHubCustomerId" IS NOT NULL AND "CommerceHubCustomerId" <> '00000000-0000-0000-0000-000000000000' AND "OnlineCustomerId" IS NOT NULL AND "OnlineCustomerId" <> '00000000-0000-0000-0000-000000000000' AND "OnlineCustomerId" = "CommerceHubCustomerId")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubCategories" (
        "Id" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "NameAr" character varying(200),
        "Slug" character varying(200) NOT NULL,
        "ParentId" uuid,
        "Level" integer NOT NULL,
        "IconEmoji" character varying(10),
        "IsActive" boolean NOT NULL DEFAULT TRUE,
        "SortOrder" integer NOT NULL DEFAULT 0,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_CommerceHubCategories" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CommerceHubCategories_CommerceHubCategories_ParentId" FOREIGN KEY ("ParentId") REFERENCES "CommerceHubCategories" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubCheckoutIdempotency" (
        "Id" uuid NOT NULL,
        "Key" character varying(255) NOT NULL,
        "RequestHash" character varying(64) NOT NULL,
        "TenantId" uuid,
        "CommerceHubCustomerId" uuid,
        "OnlineCustomerId" uuid,
        "ResponseJson" text,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_CommerceHubCheckoutIdempotency" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CommerceHubCheckoutIdempotency_CustomerAuthorityBoundary" CHECK ("CommerceHubCustomerId" IS NOT NULL AND "CommerceHubCustomerId" <> '00000000-0000-0000-0000-000000000000' AND "OnlineCustomerId" IS NULL)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubCustomers" (
        "Id" uuid NOT NULL,
        "Email" character varying(255) NOT NULL,
        "PasswordHash" character varying(500),
        "Name" character varying(200) NOT NULL,
        "PhoneNumber" character varying(50),
        "TokensValidAfterUtc" timestamp with time zone,
        "IsEmailVerified" boolean NOT NULL DEFAULT FALSE,
        "EmailVerifiedAtUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CommerceHubCustomers" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubDailyFinancialAggregates" (
        "DateUtc" date NOT NULL,
        "CurrencyCode" character varying(10) NOT NULL,
        "BaseCurrencyCode" character varying(10) NOT NULL DEFAULT 'EGP',
        "ExchangeRateToBase" numeric(18,8),
        "GrossSales" numeric(18,2) NOT NULL,
        "GrossCommission" numeric(18,2) NOT NULL,
        "PromoCost" numeric(18,2) NOT NULL,
        "PromoReversalAmount" numeric(18,2) NOT NULL,
        "RefundAmount" numeric(18,2) NOT NULL,
        "RefundImpactOnRevenue" numeric(18,2) NOT NULL,
        "NetRevenue" numeric(18,2) NOT NULL,
        "GrossSalesBase" numeric(18,2),
        "GrossCommissionBase" numeric(18,2),
        "PromoCostBase" numeric(18,2),
        "PromoReversalAmountBase" numeric(18,2),
        "RefundAmountBase" numeric(18,2),
        "RefundImpactOnRevenueBase" numeric(18,2),
        "NetRevenueBase" numeric(18,2),
        "OrdersCount" integer NOT NULL,
        "RefundCount" integer NOT NULL,
        "RefundedOrdersCount" integer NOT NULL,
        "IsValid" boolean NOT NULL DEFAULT TRUE,
        "LastValidatedAtUtc" timestamp with time zone,
        "ValidationError" text,
        "AggregatedThroughUtc" timestamp with time zone,
        "CalculatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_CommerceHubDailyFinancialAggregates" PRIMARY KEY ("DateUtc", "CurrencyCode"),
        CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_BaseCurrencyCode_EGP" CHECK ("BaseCurrencyCode" = 'EGP'),
        CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_CurrencyCode_EGP" CHECK ("CurrencyCode" = 'EGP'),
        CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_GrossCommission" CHECK ("GrossCommission" >= 0),
        CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_GrossSales" CHECK ("GrossSales" >= 0),
        CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_IsValid" CHECK ("IsValid" IN (TRUE, FALSE)),
        CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_OrdersCount" CHECK ("OrdersCount" >= 0),
        CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_PromoCost" CHECK ("PromoCost" >= 0),
        CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_PromoReversalAmount" CHECK ("PromoReversalAmount" >= 0),
        CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_RefundAmount" CHECK ("RefundAmount" >= 0),
        CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_RefundCount" CHECK ("RefundCount" >= 0),
        CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_RefundedOrdersCount" CHECK ("RefundedOrdersCount" >= 0),
        CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_RefundImpactOnRevenue" CHECK ("RefundImpactOnRevenue" >= 0)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubFinancialAuditLogs" (
        "Id" uuid NOT NULL,
        "TenantId" uuid,
        "MarketplaceOrderId" uuid,
        "SubOrderId" uuid,
        "PaymentId" uuid,
        "EarningId" uuid,
        "PayoutId" uuid,
        "RefundId" uuid,
        "Type" character varying(100) NOT NULL,
        "Debit" numeric(18,4) NOT NULL,
        "Credit" numeric(18,4) NOT NULL,
        "Currency" character varying(10) NOT NULL,
        "Description" text NOT NULL,
        "CorrelationId" character varying(200),
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_CommerceHubFinancialAuditLogs" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubFinancialGuardLocks" (
        "Id" uuid NOT NULL,
        "Scope" character varying(100) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_CommerceHubFinancialGuardLocks" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubFinancialOperationAudits" (
        "Id" uuid NOT NULL,
        "VendorId" uuid NOT NULL,
        "ActorId" uuid,
        "ActorType" character varying(50) NOT NULL,
        "OperationType" character varying(50) NOT NULL,
        "Amount" numeric(18,2) NOT NULL,
        "Currency" character varying(10) NOT NULL,
        "BeforePlatformCash" numeric(18,2) NOT NULL,
        "BeforeVendorPayable" numeric(18,2) NOT NULL,
        "BeforeVendorDebt" numeric(18,2) NOT NULL,
        "AfterPlatformCash" numeric(18,2) NOT NULL,
        "AfterVendorPayable" numeric(18,2) NOT NULL,
        "AfterVendorDebt" numeric(18,2) NOT NULL,
        "CorrelationId" character varying(200) NOT NULL,
        "IdempotencyKey" character varying(200),
        "ReferenceType" integer NOT NULL,
        "ReferenceId" uuid NOT NULL,
        "Outcome" character varying(50) NOT NULL,
        "Notes" text,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_CommerceHubFinancialOperationAudits" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubFinancialReconciliationReports" (
        "Id" uuid NOT NULL,
        "TimestampUtc" timestamp with time zone NOT NULL,
        "Status" character varying(20) NOT NULL,
        "MismatchCount" integer NOT NULL,
        "DetailsJson" text NOT NULL,
        CONSTRAINT "PK_CommerceHubFinancialReconciliationReports" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CommerceHubFinancialReconciliationReports_MismatchCount" CHECK ("MismatchCount" >= 0),
        CONSTRAINT "CK_CommerceHubFinancialReconciliationReports_Status" CHECK ("Status" IN ('Healthy', 'Warning', 'Critical'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubFinancialTransactions" (
        "Id" uuid NOT NULL,
        "EntityType" integer NOT NULL,
        "EntityId" uuid,
        "TransactionType" integer NOT NULL,
        "Direction" integer NOT NULL,
        "FinancialBucket" integer NOT NULL,
        "Amount" numeric(18,2) NOT NULL,
        "Currency" character varying(10) NOT NULL DEFAULT 'EGP',
        "ReferenceType" integer NOT NULL,
        "ReferenceId" uuid NOT NULL,
        "Description" text NOT NULL,
        "Reason" text,
        "IdempotencyKey" character varying(200) NOT NULL,
        "CorrelationId" character varying(200) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" uuid,
        CONSTRAINT "PK_CommerceHubFinancialTransactions" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CommerceHubFinancialTransactions_Amount" CHECK ("Amount" > 0),
        CONSTRAINT "CK_CommerceHubFinancialTransactions_Currency_EGP" CHECK ("Currency" = 'EGP'),
        CONSTRAINT "CK_CommerceHubFinancialTransactions_Direction" CHECK ("Direction" IN (0, 1)),
        CONSTRAINT "CK_CommerceHubFinancialTransactions_EntityScope" CHECK (("EntityType" = 0 AND "EntityId" IS NULL) OR ("EntityType" = 1 AND "EntityId" IS NOT NULL)),
        CONSTRAINT "CK_CommerceHubFinancialTransactions_FinancialBucket" CHECK ("FinancialBucket" IN (0, 1, 2)),
        CONSTRAINT "CK_CommerceHubFinancialTransactions_ReferenceType" CHECK ("ReferenceType" IN (0, 1, 2, 3, 4)),
        CONSTRAINT "CK_CommerceHubFinancialTransactions_TransactionType" CHECK ("TransactionType" IN (0, 1, 2, 3, 4, 5, 6))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubMerchantLifecycleLedgerEntries" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ListingId" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "VariantId" uuid NOT NULL,
        "EventType" character varying(100) NOT NULL,
        "ActorUserId" uuid,
        "ActorIdentity" character varying(200) NOT NULL,
        "OccurredAtUtc" timestamp with time zone NOT NULL,
        "CorrelationId" character varying(200) NOT NULL,
        "RequestId" character varying(200),
        "Reason" character varying(1024),
        "DetailsJson" text,
        CONSTRAINT "PK_CommerceHubMerchantLifecycleLedgerEntries" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubPaymentCompensationOutboxMessages" (
        "Id" uuid NOT NULL,
        "PaymentId" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "TenantId" uuid,
        "AffectedStoreIdsJson" text NOT NULL,
        "StoreAllocationSnapshotJson" text NOT NULL,
        "RefundAllocationSnapshotJson" text NOT NULL,
        "Provider" integer NOT NULL,
        "ProviderIntentId" character varying(200),
        "ProviderTransactionId" character varying(200),
        "Amount" numeric NOT NULL,
        "Currency" character varying(10) NOT NULL,
        "Reason" character varying(500) NOT NULL,
        "IdempotencyKey" character varying(200) NOT NULL,
        "CorrelationId" character varying(100),
        "ProcessedAtUtc" timestamp with time zone,
        "AttemptCount" integer NOT NULL,
        "LastError" character varying(2000),
        "NextRetryAtUtc" timestamp with time zone,
        "ProcessingLeaseId" uuid,
        "ProcessingLeaseExpiresAtUtc" timestamp with time zone,
        "IsFailed" boolean NOT NULL,
        "GatewayReference" character varying(200),
        "GatewayStatus" character varying(100),
        "GatewayRequestStartedAtUtc" timestamp with time zone,
        "GatewayOutcomeUncertainAtUtc" timestamp with time zone,
        "RequiresOperatorReconciliation" boolean NOT NULL DEFAULT FALSE,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CommerceHubPaymentCompensationOutboxMessages" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubRecoveryOperationLedgerEntries" (
        "Id" uuid NOT NULL,
        "OperationId" uuid NOT NULL,
        "OperationType" character varying(100) NOT NULL,
        "ActorIdentity" character varying(200) NOT NULL,
        "ActorUserId" uuid,
        "TenantId" uuid,
        "ListingId" uuid,
        "Scope" character varying(200) NOT NULL,
        "Outcome" character varying(100) NOT NULL,
        "StartedAtUtc" timestamp with time zone NOT NULL,
        "EndedAtUtc" timestamp with time zone NOT NULL,
        "CorrelationId" character varying(200) NOT NULL,
        "RequestId" character varying(200),
        "Reason" character varying(1024),
        "DetailsJson" text,
        CONSTRAINT "PK_CommerceHubRecoveryOperationLedgerEntries" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubUnresolvedPaymentWebhooks" (
        "Id" uuid NOT NULL,
        "Provider" integer NOT NULL,
        "ProviderEventId" character varying(200) NOT NULL,
        "ProviderReference" character varying(200),
        "LocalPaymentId" uuid,
        "ProviderIntentId" character varying(200),
        "ProviderTransactionId" character varying(200),
        "TenantId" uuid,
        "OrderId" uuid,
        "AffectedStoreIdsJson" text NOT NULL DEFAULT '[]',
        "SuspenseRoutedStoreIdsJson" text NOT NULL DEFAULT '[]',
        "Amount" numeric(18,2) NOT NULL,
        "Currency" character varying(10) NOT NULL,
        "EventType" character varying(100) NOT NULL,
        "PayloadHash" character varying(64) NOT NULL,
        "RawPayload" text NOT NULL,
        "Status" character varying(50) NOT NULL,
        "ResolutionAttemptCount" integer NOT NULL,
        "LastResolutionAttemptAtUtc" timestamp with time zone,
        "RoutedToSuspenseAtUtc" timestamp with time zone,
        "CorrelationId" character varying(100),
        "LastError" character varying(2000),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CommerceHubUnresolvedPaymentWebhooks" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CommerceHubUnresolvedPaymentWebhooks_Amount_NonNegative" CHECK ("Amount" >= 0),
        CONSTRAINT "CK_CommerceHubUnresolvedPaymentWebhooks_Currency_EGP" CHECK ("Currency" = 'EGP')
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubVendorPaymentMethods" (
        "Id" uuid NOT NULL,
        "Name" character varying(120) NOT NULL,
        "DestinationValue" character varying(200) NOT NULL,
        "Instructions" text NOT NULL,
        "IsActive" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedBy" uuid NOT NULL,
        CONSTRAINT "PK_CommerceHubVendorPaymentMethods" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubVendorPayoutMethods" (
        "Id" uuid NOT NULL,
        "VendorId" uuid NOT NULL,
        "MethodType" integer NOT NULL,
        "DestinationValue" character varying(200) NOT NULL,
        "IsDefault" boolean NOT NULL,
        "IsVerified" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CommerceHubVendorPayoutMethods" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubVendorSettlements" (
        "Id" uuid NOT NULL,
        "VendorId" uuid NOT NULL,
        "SettlementType" integer NOT NULL,
        "Direction" integer NOT NULL,
        "Amount" numeric(18,2) NOT NULL,
        "Currency" character varying(10) NOT NULL DEFAULT 'EGP',
        "IdempotencyKey" character varying(120) NOT NULL,
        "ExternalReference" character varying(160) NOT NULL,
        "Notes" text NOT NULL,
        "RecordedByUserId" uuid,
        "RecordedByUserEmail" character varying(320),
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_CommerceHubVendorSettlements" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CommerceHubVendorSettlements_Amount" CHECK ("Amount" > 0),
        CONSTRAINT "CK_CommerceHubVendorSettlements_Currency_EGP" CHECK ("Currency" = 'EGP'),
        CONSTRAINT "CK_CommerceHubVendorSettlements_Direction" CHECK ("Direction" IN (0, 1)),
        CONSTRAINT "CK_CommerceHubVendorSettlements_ExternalReference_NotBlank" CHECK (NULLIF(btrim("ExternalReference"), '') IS NOT NULL),
        CONSTRAINT "CK_CommerceHubVendorSettlements_IdempotencyKey_NotBlank" CHECK (NULLIF(btrim("IdempotencyKey"), '') IS NOT NULL),
        CONSTRAINT "CK_CommerceHubVendorSettlements_SettlementType" CHECK ("SettlementType" IN (0, 1, 2))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommissionRules" (
        "Id" uuid NOT NULL,
        "ScopeType" integer NOT NULL,
        "ScopeId" uuid,
        "Rate" numeric(8,6) NOT NULL,
        "IsActive" boolean NOT NULL DEFAULT TRUE,
        "Priority" integer NOT NULL DEFAULT 0,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CommissionRules" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "Coupons" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Code" text NOT NULL,
        "NormalizedCode" text NOT NULL,
        "Description" text,
        "DiscountType" integer NOT NULL,
        "DiscountValue" numeric NOT NULL,
        "MinOrderAmount" numeric,
        "MaxDiscountAmount" numeric,
        "MaxUsage" integer,
        "CurrentUsage" integer NOT NULL,
        "MaxUsagePerCustomer" integer,
        "StartDate" timestamp with time zone,
        "EndDate" timestamp with time zone,
        "IsActive" boolean NOT NULL,
        "FirstOrderOnly" boolean NOT NULL,
        "ApplicableProductIds" text,
        "ApplicableCategoryIds" text,
        "ExcludedProductIds" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_Coupons" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_Coupons_DiscountType" CHECK ("DiscountType" IN (0, 1)),
        CONSTRAINT "CK_Coupons_DiscountValue_FixedAmount" CHECK ("DiscountType" <> 1 OR "DiscountValue" <= 1000000.00),
        CONSTRAINT "CK_Coupons_DiscountValue_Percentage" CHECK ("DiscountType" <> 0 OR "DiscountValue" <= 100),
        CONSTRAINT "CK_Coupons_DiscountValue_Positive" CHECK ("DiscountValue" > 0)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "Currencies" (
        "Code" character varying(3) NOT NULL,
        "Name" character varying(100) NOT NULL,
        "Symbol" character varying(10) NOT NULL,
        "DecimalPlaces" integer NOT NULL DEFAULT 2,
        CONSTRAINT "PK_Currencies" PRIMARY KEY ("Code")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    INSERT INTO "Currencies" ("Code", "DecimalPlaces", "Name", "Symbol")
    VALUES ('EGP', 2, 'Egyptian Pound', 'EGP');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "Customers" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Phone" character varying(50),
        "Email" character varying(200),
        "Address" text,
        "TaxNumber" character varying(50),
        "Balance" numeric(18,2) NOT NULL,
        "OpeningBalanceAmount" numeric,
        "BalanceType" text,
        "OpeningBalanceDate" timestamp with time zone,
        "Notes" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_Customers" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CustomsAdministrationAudit" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Action" character varying(64) NOT NULL,
        "CustomsEvidenceId" uuid,
        "CustomsEvidenceVersionId" uuid,
        "ProductId" uuid,
        "ProductVariantId" uuid,
        "Actor" character varying(128) NOT NULL,
        "Role" character varying(64) NOT NULL,
        "Reason" character varying(512) NOT NULL,
        "CorrelationId" character varying(128) NOT NULL,
        "ValidationChecksumSha256" character(64),
        "FailureCode" character varying(128),
        "FromStatus" character varying(32),
        "ToStatus" character varying(32),
        "OccurredAtUtc" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CustomsAdministrationAudit" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CustomsAdmissionAudit" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Action" character varying(64) NOT NULL,
        "CustomsEvidenceId" uuid,
        "CustomsEvidenceVersionId" uuid,
        "ShipmentCustomsSnapshotId" uuid,
        "ShipmentId" uuid,
        "OrderId" uuid,
        "ProductId" uuid,
        "ProductVariantId" uuid,
        "Actor" character varying(128) NOT NULL,
        "Reason" character varying(512) NOT NULL,
        "CorrelationId" character varying(128) NOT NULL,
        "SourceEvidenceFingerprintSha256" character(64),
        "FailureCode" character varying(128),
        "OccurredAtUtc" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CustomsAdmissionAudit" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CustomsEvidence" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "SourceKind" character varying(32) NOT NULL,
        "SourceId" uuid NOT NULL,
        "ProductId" uuid,
        "ProductVariantId" uuid,
        "Status" character varying(32) NOT NULL,
        "CurrentVersionId" uuid NOT NULL,
        "CurrentVersionNumber" integer NOT NULL,
        "AdmittedAtUtc" timestamp with time zone NOT NULL,
        "AdmittedBy" character varying(128) NOT NULL,
        "CorrelationId" character varying(128) NOT NULL,
        "CreatedByActor" character varying(128) NOT NULL,
        "LastEditedByActor" character varying(128) NOT NULL,
        "SubmittedByActor" character varying(128),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CustomsEvidence" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CustomsEvidence_CurrentVersion" CHECK ("CurrentVersionNumber" > 0),
        CONSTRAINT "CK_CustomsEvidence_SourceKind" CHECK ("SourceKind" IN ('Product','ProductVariant','OrderLine','System')),
        CONSTRAINT "CK_CustomsEvidence_Status" CHECK ("Status" IN ('Candidate','Admitted','Draft','UnderReview','Approved','Published','Rejected','Reopened','Retired','Superseded'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CustomsEvidenceApprovals" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CustomsEvidenceId" uuid NOT NULL,
        "CustomsEvidenceVersionId" uuid NOT NULL,
        "VersionNumber" integer NOT NULL,
        "ValidationChecksumSha256" character(64) NOT NULL,
        "ApprovedByActor" character varying(128) NOT NULL,
        "Reason" character varying(512) NOT NULL,
        "CorrelationId" character varying(128) NOT NULL,
        "ApprovedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CustomsEvidenceApprovals" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CustomsEvidenceApproval_Checksum" CHECK (char_length("ValidationChecksumSha256") = 64)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CustomsEvidenceFingerprints" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CustomsEvidenceId" uuid NOT NULL,
        "CustomsEvidenceVersionId" uuid NOT NULL,
        "FingerprintSha256" character(64) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CustomsEvidenceFingerprints" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CustomsEvidenceFingerprint_Length" CHECK (char_length("FingerprintSha256") = 64)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CustomsEvidenceHistory" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CustomsEvidenceId" uuid NOT NULL,
        "CustomsEvidenceVersionId" uuid NOT NULL,
        "VersionNumber" integer NOT NULL,
        "Action" character varying(128) NOT NULL,
        "Reason" character varying(512) NOT NULL,
        "Actor" character varying(128) NOT NULL,
        "CorrelationId" character varying(128) NOT NULL,
        "OccurredAtUtc" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CustomsEvidenceHistory" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CustomsEvidencePublications" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CustomsEvidenceId" uuid NOT NULL,
        "CustomsEvidenceVersionId" uuid NOT NULL,
        "SupersededCustomsEvidenceId" uuid,
        "SupersededCustomsEvidenceVersionId" uuid,
        "ProductId" uuid,
        "ProductVariantId" uuid,
        "VersionNumber" integer NOT NULL,
        "Action" character varying(32) NOT NULL,
        "ValidationChecksumSha256" character(64) NOT NULL,
        "PublishedByActor" character varying(128) NOT NULL,
        "Reason" character varying(512) NOT NULL,
        "CorrelationId" character varying(128) NOT NULL,
        "OccurredAtUtc" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CustomsEvidencePublications" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CustomsEvidencePublication_Action" CHECK ("Action" IN ('Published','Retired','Superseded')),
        CONSTRAINT "CK_CustomsEvidencePublication_Checksum" CHECK (char_length("ValidationChecksumSha256") = 64)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CustomsPublicationLocks" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "LockKey" character varying(256) NOT NULL,
        "LockedAtUtc" timestamp with time zone NOT NULL,
        "LockedUntilUtc" timestamp with time zone NOT NULL,
        "LockedByActor" character varying(128) NOT NULL,
        "CorrelationId" character varying(128) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CustomsPublicationLocks" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "ExpenseCategories" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Name" character varying(100) NOT NULL,
        "Description" text,
        "IsActive" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_ExpenseCategories" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "FawryWebhookReplayRecords" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Provider" character varying(32) NOT NULL,
        "ProviderEventId" character varying(200) NOT NULL,
        "EventType" character varying(120) NOT NULL,
        "EvidenceId" uuid NOT NULL,
        "ActionId" uuid NOT NULL,
        "EvidenceHash" character varying(128) NOT NULL,
        "EvidenceReference" character varying(200) NOT NULL,
        "ProviderReference" character varying(240) NOT NULL,
        "CorrelationId" character varying(200) NOT NULL,
        "ProcessingTimestampUtc" timestamp with time zone NOT NULL,
        "PaymentIntentId" uuid,
        "OrderId" uuid,
        "AdmissionClassification" character varying(40) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_FawryWebhookReplayRecords" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "FinancialAccounts" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "AccountCode" text NOT NULL,
        "Name" text NOT NULL,
        "AccountNumber" text,
        "AccountType" integer NOT NULL,
        "IsSystem" boolean NOT NULL,
        "Balance" numeric NOT NULL,
        "Level" integer NOT NULL,
        "ParentAccountId" uuid,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_FinancialAccounts" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_FinancialAccounts_FinancialAccounts_ParentAccountId" FOREIGN KEY ("ParentAccountId") REFERENCES "FinancialAccounts" ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "GeographicCatalogReleases" (
        "Id" uuid NOT NULL,
        "ReleaseNumber" bigint NOT NULL,
        "ReleaseKey" character varying(64) NOT NULL,
        "Status" character varying(24) NOT NULL,
        "CountryScopeIso2" character(2),
        "SourceSummary" character varying(512) NOT NULL,
        "ChecksumSha256" character(64) NOT NULL,
        "CreatedByUserId" uuid NOT NULL,
        "PublishedAtUtc" timestamp with time zone,
        "PublishedByUserId" uuid,
        "SupersedesReleaseId" uuid,
        "RollbackOfReleaseId" uuid,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_GeographicCatalogReleases" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_GeoCatalogRelease_Checksum" CHECK (char_length("ChecksumSha256") = 64),
        CONSTRAINT "CK_GeoCatalogRelease_Status" CHECK ("Status" IN ('Draft','Validated','Published','Superseded','RolledBack','Rejected')),
        CONSTRAINT "FK_GeoCatalogRelease_RollbackOf" FOREIGN KEY ("RollbackOfReleaseId") REFERENCES "GeographicCatalogReleases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_GeoCatalogRelease_Supersedes" FOREIGN KEY ("SupersedesReleaseId") REFERENCES "GeographicCatalogReleases" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "GlobalPhoneReputation" (
        "Id" uuid NOT NULL,
        "PhoneNumber" character varying(32) NOT NULL,
        "SpamReports" integer NOT NULL DEFAULT 0,
        "FirstReportedAtUtc" timestamp with time zone NOT NULL,
        "LastReportedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_GlobalPhoneReputation" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "HomepageSections" (
        "Id" uuid NOT NULL,
        "SectionGroupId" uuid NOT NULL,
        "Type" character varying(40) NOT NULL,
        "Order" integer NOT NULL,
        "Data" jsonb NOT NULL,
        "Version" integer NOT NULL,
        "SchemaVersion" integer NOT NULL,
        "IsActive" boolean NOT NULL,
        "IsDeleted" boolean NOT NULL,
        "IsDraft" boolean NOT NULL,
        "IsPublished" boolean NOT NULL,
        "PublishedAt" timestamp with time zone,
        "DraftGroupId" uuid,
        "TenantId" uuid,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_HomepageSections" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_HomepageSections_Order" CHECK ("Order" >= 0),
        CONSTRAINT "CK_HomepageSections_SchemaVersion" CHECK ("SchemaVersion" > 0),
        CONSTRAINT "CK_HomepageSections_State" CHECK (NOT ("IsDraft" AND "IsPublished")),
        CONSTRAINT "CK_HomepageSections_Type" CHECK ("Type" IN ('hero', 'products', 'banner', 'categories')),
        CONSTRAINT "CK_HomepageSections_Version" CHECK ("Version" > 0)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "InboxState" (
        "Id" bigint GENERATED BY DEFAULT AS IDENTITY,
        "MessageId" uuid NOT NULL,
        "ConsumerId" uuid NOT NULL,
        "LockId" uuid NOT NULL,
        "RowVersion" bytea,
        "Received" timestamp with time zone NOT NULL,
        "ReceiveCount" integer NOT NULL,
        "ExpirationTime" timestamp with time zone,
        "Consumed" timestamp with time zone,
        "Delivered" timestamp with time zone,
        "LastSequenceNumber" bigint,
        CONSTRAINT "PK_InboxState" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_InboxState_MessageId_ConsumerId" UNIQUE ("MessageId", "ConsumerId")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "InventoryEventInbox" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "EventId" uuid NOT NULL,
        "EventType" integer NOT NULL,
        "ReferenceId" uuid,
        "ReferenceLineId" uuid,
        "PayloadHash" character varying(128),
        "FirstReceivedAtUtc" timestamp with time zone NOT NULL,
        "LastReceivedAtUtc" timestamp with time zone,
        "AppliedAtUtc" timestamp with time zone,
        "Status" integer NOT NULL,
        "ReplayCount" integer NOT NULL,
        "StockMovementId" uuid,
        "CorrelationId" character varying(100),
        "IdempotencyKey" character varying(200),
        "Source" character varying(100),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_InventoryEventInbox" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_InventoryEventInbox_ReplayCount_NonNegative" CHECK ("ReplayCount" >= 0)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "InventoryReservations" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "SubOrderId" uuid,
        "OrderItemId" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "VariantId" uuid NOT NULL,
        "WarehouseId" uuid NOT NULL,
        "ReservedQuantity" numeric(18,2) NOT NULL,
        "CommittedQuantity" numeric(18,2) NOT NULL,
        "ReleasedQuantity" numeric(18,2) NOT NULL,
        "ReturnedQuantity" numeric(18,2) NOT NULL,
        "DamagedReturnedQuantity" numeric(18,2) NOT NULL,
        "Status" integer NOT NULL,
        "ReservationEventId" uuid NOT NULL,
        "LastMutationEventId" uuid,
        "ExpiresAtUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_InventoryReservations" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_InventoryReservations_CommittedQuantity_NonNegative" CHECK ("CommittedQuantity" >= 0),
        CONSTRAINT "CK_InventoryReservations_DamagedReturnedQuantity_NonNegative" CHECK ("DamagedReturnedQuantity" >= 0),
        CONSTRAINT "CK_InventoryReservations_ReleasedQuantity_NonNegative" CHECK ("ReleasedQuantity" >= 0),
        CONSTRAINT "CK_InventoryReservations_ReservedFlow_Valid" CHECK ("CommittedQuantity" + "ReleasedQuantity" <= "ReservedQuantity"),
        CONSTRAINT "CK_InventoryReservations_ReservedQuantity_NonNegative" CHECK ("ReservedQuantity" >= 0),
        CONSTRAINT "CK_InventoryReservations_ReturnedQuantity_NonNegative" CHECK ("ReturnedQuantity" >= 0),
        CONSTRAINT "CK_InventoryReservations_Returns_DoNotExceedCommitted" CHECK ("ReturnedQuantity" + "DamagedReturnedQuantity" <= "CommittedQuantity")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "LedgerAccounts" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "AccountCode" character varying(20) NOT NULL,
        "Name" character varying(200) NOT NULL,
        "NameAr" character varying(200),
        "Description" character varying(500),
        "AccountType" integer NOT NULL,
        "ParentAccountId" uuid,
        "Level" integer NOT NULL DEFAULT 1,
        "CurrentBalance" numeric(18,4) NOT NULL,
        "IsSystem" boolean NOT NULL DEFAULT FALSE,
        "IsClearing" boolean NOT NULL DEFAULT FALSE,
        "PreventNegative" boolean NOT NULL DEFAULT FALSE,
        "IsActive" boolean NOT NULL DEFAULT TRUE,
        "ClearingSourceType" character varying(50),
        "ClearingSourceCode" character varying(50),
        "ExpectedSettlementDays" integer,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_LedgerAccounts" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_LedgerAccounts_TenantId_Id" UNIQUE ("TenantId", "Id"),
        CONSTRAINT "FK_LedgerAccounts_LedgerAccounts_ParentAccountId" FOREIGN KEY ("ParentAccountId") REFERENCES "LedgerAccounts" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "LedgerJournalEntries" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "JournalNumber" character varying(50) NOT NULL,
        "JournalDate" timestamp with time zone NOT NULL,
        "PostedAtUtc" timestamp with time zone NOT NULL,
        "SourceType" character varying(50) NOT NULL,
        "SourceId" uuid NOT NULL,
        "SourceReference" character varying(100),
        "Description" character varying(500) NOT NULL,
        "IdempotencyKey" character varying(255) NOT NULL,
        "Status" integer NOT NULL,
        "ReversedByJournalId" uuid,
        "ReversesJournalId" uuid,
        "ReversalReason" character varying(500),
        "CreatedByUserId" uuid,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_LedgerJournalEntries" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_LedgerJournalEntries_LedgerJournalEntries_ReversedByJournal~" FOREIGN KEY ("ReversedByJournalId") REFERENCES "LedgerJournalEntries" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "LedgerJournalNumberSequences" (
        "TenantId" uuid NOT NULL,
        "CurrentYear" integer NOT NULL,
        "NextNumber" integer NOT NULL DEFAULT 1,
        CONSTRAINT "PK_LedgerJournalNumberSequences" PRIMARY KEY ("TenantId")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "MarketplaceCmsPages" (
        "Id" uuid NOT NULL,
        "PageGroupId" uuid NOT NULL,
        "Slug" character varying(160) NOT NULL,
        "Title" character varying(200) NOT NULL,
        "TitleJson" jsonb,
        "Content" text NOT NULL,
        "ContentJson" jsonb,
        "ContentType" character varying(20) NOT NULL,
        "Version" integer NOT NULL,
        "IsDraft" boolean NOT NULL,
        "IsPublished" boolean NOT NULL,
        "PublishedAt" timestamp with time zone,
        "MetaTitle" character varying(200),
        "MetaTitleJson" jsonb,
        "MetaDescription" character varying(320),
        "MetaDescriptionJson" jsonb,
        "OpenGraphImageUrl" character varying(1000),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_MarketplaceCmsPages" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_MarketplaceCmsPages_ContentType" CHECK ("ContentType" IN ('RichText', 'Blocks')),
        CONSTRAINT "CK_MarketplaceCmsPages_PublishedAt" CHECK (NOT "IsPublished" OR "PublishedAt" IS NOT NULL),
        CONSTRAINT "CK_MarketplaceCmsPages_State" CHECK (NOT ("IsDraft" AND "IsPublished")),
        CONSTRAINT "CK_MarketplaceCmsPages_Version" CHECK ("Version" > 0)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "MarketplaceEventOutboxDeadLetterArchives" (
        "Id" uuid NOT NULL,
        "OriginalOutboxMessageId" uuid NOT NULL,
        "EventId" uuid NOT NULL,
        "EventType" character varying(200) NOT NULL,
        "ClrType" character varying(1000) NOT NULL,
        "PayloadJson" text NOT NULL,
        "EntityId" uuid NOT NULL,
        "AggregateRootId" uuid NOT NULL,
        "TenantId" uuid,
        "CorrelationId" character varying(200) NOT NULL,
        "Version" integer NOT NULL,
        "OccurredAtUtc" timestamp with time zone NOT NULL,
        "FailedAtUtc" timestamp with time zone,
        "AttemptCount" integer NOT NULL,
        "LastError" character varying(2000),
        "ArchivedAtUtc" timestamp with time zone NOT NULL,
        "ArchiveReason" character varying(200) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_MarketplaceEventOutboxDeadLetterArchives" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_MarketplaceEventOutboxDeadLetterArchive_OriginalId_Required" CHECK ("OriginalOutboxMessageId" <> '00000000-0000-0000-0000-000000000000')
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "MarketplaceEventOutboxMessages" (
        "Id" uuid NOT NULL,
        "EventId" uuid NOT NULL,
        "EventType" character varying(200) NOT NULL,
        "ClrType" character varying(1000) NOT NULL,
        "PayloadJson" text NOT NULL,
        "EntityId" uuid NOT NULL,
        "AggregateRootId" uuid NOT NULL,
        "TenantId" uuid,
        "CorrelationId" character varying(200) NOT NULL,
        "Version" integer NOT NULL,
        "OccurredAtUtc" timestamp with time zone NOT NULL,
        "PublishedAtUtc" timestamp with time zone,
        "ClaimedUntilUtc" timestamp with time zone,
        "AttemptCount" integer NOT NULL,
        "LastError" character varying(2000),
        "NextRetryAtUtc" timestamp with time zone,
        "IsFailed" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_MarketplaceEventOutboxMessages" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_MarketplaceEventOutbox_AggregateRootId_NotEmpty" CHECK ("AggregateRootId" IS NOT NULL AND "AggregateRootId" <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT "CK_MarketplaceEventOutbox_TenantId_Required" CHECK ("TenantId" IS NOT NULL AND "TenantId" <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT "CK_MarketplaceEventOutbox_Version_Positive" CHECK ("Version" > 0)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "MarketplaceFooterSections" (
        "Id" uuid NOT NULL,
        "Title" character varying(120) NOT NULL,
        "TitleJson" jsonb,
        "SortOrder" integer NOT NULL,
        "IsActive" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_MarketplaceFooterSections" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "MarketplaceProfiles" (
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "MarketplaceProjectionOutboxMessages" (
        "Id" uuid NOT NULL,
        "EventId" uuid NOT NULL,
        "ConsumerName" character varying(200) NOT NULL,
        "EventType" character varying(200) NOT NULL,
        "ClrType" character varying(1000) NOT NULL,
        "EntityId" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CorrelationId" character varying(200) NOT NULL,
        "EventVersion" integer NOT NULL,
        "OccurredAtUtc" timestamp with time zone NOT NULL,
        "PayloadJson" text NOT NULL,
        "ProcessedAtUtc" timestamp with time zone,
        "ClaimedUntilUtc" timestamp with time zone,
        "AttemptCount" integer NOT NULL,
        "LastError" character varying(2000),
        "NextRetryAtUtc" timestamp with time zone,
        "IsFailed" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_MarketplaceProjectionOutboxMessages" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "MarketplaceSocialLinks" (
        "Id" uuid NOT NULL,
        "Platform" character varying(40) NOT NULL,
        "Url" character varying(1000) NOT NULL,
        "Icon" character varying(80),
        "SortOrder" integer NOT NULL,
        "IsActive" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_MarketplaceSocialLinks" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "OnboardingChecklists" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ItemsJson" text NOT NULL,
        "CompletedAtUtc" timestamp with time zone,
        "DismissedAtUtc" timestamp with time zone,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_OnboardingChecklists" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "OnlineCustomers" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Email" character varying(255) NOT NULL,
        "NormalizedEmail" text NOT NULL,
        "PasswordHash" character varying(255),
        "Provider" character varying(20) NOT NULL,
        "ProviderUserId" character varying(255),
        "DisplayName" character varying(200),
        "Phone" character varying(50),
        "IsActive" boolean NOT NULL,
        "EmailVerified" boolean NOT NULL,
        "EmailVerifiedAtUtc" timestamp with time zone,
        "PhoneVerified" boolean NOT NULL,
        "PhoneVerifiedAtUtc" timestamp with time zone,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "LastLoginAtUtc" timestamp with time zone,
        "UpdatedAtUtc" timestamp with time zone,
        "TokensValidAfterUtc" timestamp with time zone,
        CONSTRAINT "PK_OnlineCustomers" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "OnlineCustomerWishlistItems" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "OnlineCustomerId" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "IsRemoved" boolean NOT NULL,
        "RemovedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_OnlineCustomerWishlistItems" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "OrderRequestIdempotency" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Key" character varying(200) NOT NULL,
        "RequestHash" character varying(64) NOT NULL,
        "SnapshotHash" character varying(64),
        "OrderId" uuid,
        "ResponseStatusCode" integer,
        "ResponsePayload" text,
        "CompletedAtUtc" timestamp with time zone,
        "ClaimToken" character varying(32),
        "ClaimTimestampUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_OrderRequestIdempotency" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "OutboxMessages" (
        "Id" uuid NOT NULL,
        "EventType" character varying(500) NOT NULL,
        "EventPayload" text NOT NULL,
        "TenantId" uuid,
        "AggregateId" uuid,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "EventVersion" integer NOT NULL DEFAULT 0,
        "ProcessedAtUtc" timestamp with time zone,
        "RetryCount" integer NOT NULL,
        "LastErrorMessage" character varying(2000),
        "RequiresOperatorReconciliation" boolean NOT NULL DEFAULT FALSE,
        "OperatorReconciliationRequiredAtUtc" timestamp with time zone,
        "OperatorReconciliationReason" character varying(2000),
        "LastErrorAtUtc" timestamp with time zone,
        CONSTRAINT "PK_OutboxMessages" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_OutboxMessages_CriticalEvents_AggregateId_Required" CHECK (("AggregateId" IS NOT NULL AND "AggregateId" <> '00000000-0000-0000-0000-000000000000') OR NOT ((lower("EventType") LIKE '%order%' OR lower("EventType") LIKE '%payment%' OR lower("EventType") LIKE '%inventory%' OR lower("EventType") LIKE '%stock%' OR lower("EventType") LIKE '%product%' OR lower("EventType") LIKE '%variant%' OR lower("EventType") LIKE '%commercehub%' OR lower("EventType") LIKE '%marketplace%' OR lower("EventType") LIKE '%affiliate%' OR lower("EventType") LIKE '%coupon%' OR lower("EventType") LIKE '%promotion%' OR lower("EventType") LIKE '%tenant%' OR lower("EventType") LIKE '%storeconfig%' OR lower("EventType") LIKE '%shippingmethod%' OR lower("EventType") LIKE '%shipment%' OR lower("EventType") LIKE '%customer%' OR lower("EventType") LIKE '%checkout%' OR lower("EventType") LIKE '%refund%' OR lower("EventType") LIKE '%payout%' OR lower("EventType") LIKE '%settlement%' OR lower("EventType") LIKE '%ledger%' OR lower("EventType") LIKE '%accounting%' OR lower("EventType") LIKE '%wallet%' OR lower("EventType") LIKE '%cod%' OR lower("EventType") LIKE '%currency%' OR lower("EventType") LIKE '%providertrust%') AND lower("EventType") NOT LIKE '%affiliatelinkclickedevent%' AND lower("EventType") NOT LIKE '%lowstockalertevent%' AND lower("EventType") NOT LIKE '%onlinecustomeremailverificationrequestedevent%' AND lower("EventType") NOT LIKE '%promotionseenevent%' AND lower("EventType") NOT LIKE '%promotionclickedevent%' AND lower("EventType") NOT LIKE '%promotionconvertedevent%' AND lower("EventType") NOT LIKE '%promotionboostappliedevent%' AND lower("EventType") NOT LIKE '%whatsapporderconfirmationrequestedevent%' AND lower("EventType") NOT LIKE '%whatsappotprequestedevent%' AND lower("EventType") NOT LIKE '%whatsapppaymentapprovalrequestedevent%' AND lower("EventType") NOT LIKE '%whatsapppaymentapprovednotificationevent%' AND lower("EventType") NOT LIKE '%whatsapppaymentrejectednotificationevent%')),
        CONSTRAINT "CK_OutboxMessages_CriticalEvents_EventVersion_Required" CHECK ("EventVersion" > 0 OR NOT ((lower("EventType") LIKE '%order%' OR lower("EventType") LIKE '%payment%' OR lower("EventType") LIKE '%inventory%' OR lower("EventType") LIKE '%stock%' OR lower("EventType") LIKE '%product%' OR lower("EventType") LIKE '%variant%' OR lower("EventType") LIKE '%commercehub%' OR lower("EventType") LIKE '%marketplace%' OR lower("EventType") LIKE '%affiliate%' OR lower("EventType") LIKE '%coupon%' OR lower("EventType") LIKE '%promotion%' OR lower("EventType") LIKE '%tenant%' OR lower("EventType") LIKE '%storeconfig%' OR lower("EventType") LIKE '%shippingmethod%' OR lower("EventType") LIKE '%shipment%' OR lower("EventType") LIKE '%customer%' OR lower("EventType") LIKE '%checkout%' OR lower("EventType") LIKE '%refund%' OR lower("EventType") LIKE '%payout%' OR lower("EventType") LIKE '%settlement%' OR lower("EventType") LIKE '%ledger%' OR lower("EventType") LIKE '%accounting%' OR lower("EventType") LIKE '%wallet%' OR lower("EventType") LIKE '%cod%' OR lower("EventType") LIKE '%currency%' OR lower("EventType") LIKE '%providertrust%') AND lower("EventType") NOT LIKE '%affiliatelinkclickedevent%' AND lower("EventType") NOT LIKE '%lowstockalertevent%' AND lower("EventType") NOT LIKE '%onlinecustomeremailverificationrequestedevent%' AND lower("EventType") NOT LIKE '%promotionseenevent%' AND lower("EventType") NOT LIKE '%promotionclickedevent%' AND lower("EventType") NOT LIKE '%promotionconvertedevent%' AND lower("EventType") NOT LIKE '%promotionboostappliedevent%' AND lower("EventType") NOT LIKE '%whatsapporderconfirmationrequestedevent%' AND lower("EventType") NOT LIKE '%whatsappotprequestedevent%' AND lower("EventType") NOT LIKE '%whatsapppaymentapprovalrequestedevent%' AND lower("EventType") NOT LIKE '%whatsapppaymentapprovednotificationevent%' AND lower("EventType") NOT LIKE '%whatsapppaymentrejectednotificationevent%')),
        CONSTRAINT "CK_OutboxMessages_CriticalEvents_TenantId_Required" CHECK (("TenantId" IS NOT NULL AND "TenantId" <> '00000000-0000-0000-0000-000000000000') OR NOT ((lower("EventType") LIKE '%order%' OR lower("EventType") LIKE '%payment%' OR lower("EventType") LIKE '%inventory%' OR lower("EventType") LIKE '%stock%' OR lower("EventType") LIKE '%product%' OR lower("EventType") LIKE '%variant%' OR lower("EventType") LIKE '%commercehub%' OR lower("EventType") LIKE '%marketplace%' OR lower("EventType") LIKE '%affiliate%' OR lower("EventType") LIKE '%coupon%' OR lower("EventType") LIKE '%promotion%' OR lower("EventType") LIKE '%tenant%' OR lower("EventType") LIKE '%storeconfig%' OR lower("EventType") LIKE '%shippingmethod%' OR lower("EventType") LIKE '%shipment%' OR lower("EventType") LIKE '%customer%' OR lower("EventType") LIKE '%checkout%' OR lower("EventType") LIKE '%refund%' OR lower("EventType") LIKE '%payout%' OR lower("EventType") LIKE '%settlement%' OR lower("EventType") LIKE '%ledger%' OR lower("EventType") LIKE '%accounting%' OR lower("EventType") LIKE '%wallet%' OR lower("EventType") LIKE '%cod%' OR lower("EventType") LIKE '%currency%' OR lower("EventType") LIKE '%providertrust%') AND lower("EventType") NOT LIKE '%affiliatelinkclickedevent%' AND lower("EventType") NOT LIKE '%lowstockalertevent%' AND lower("EventType") NOT LIKE '%onlinecustomeremailverificationrequestedevent%' AND lower("EventType") NOT LIKE '%promotionseenevent%' AND lower("EventType") NOT LIKE '%promotionclickedevent%' AND lower("EventType") NOT LIKE '%promotionconvertedevent%' AND lower("EventType") NOT LIKE '%promotionboostappliedevent%' AND lower("EventType") NOT LIKE '%whatsapporderconfirmationrequestedevent%' AND lower("EventType") NOT LIKE '%whatsappotprequestedevent%' AND lower("EventType") NOT LIKE '%whatsapppaymentapprovalrequestedevent%' AND lower("EventType") NOT LIKE '%whatsapppaymentapprovednotificationevent%' AND lower("EventType") NOT LIKE '%whatsapppaymentrejectednotificationevent%'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "OutboxState" (
        "OutboxId" uuid NOT NULL,
        "LockId" uuid NOT NULL,
        "RowVersion" bytea,
        "Created" timestamp with time zone NOT NULL,
        "Delivered" timestamp with time zone,
        "LastSequenceNumber" bigint,
        CONSTRAINT "PK_OutboxState" PRIMARY KEY ("OutboxId")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "PartyLedgerAdjustments" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "PartyType" integer NOT NULL,
        "PartyId" uuid NOT NULL,
        "TransactionType" integer NOT NULL,
        "AdjustmentDate" timestamp with time zone NOT NULL,
        "Amount" numeric(18,2) NOT NULL,
        "IdempotencyKey" character varying(100) NOT NULL,
        "ReferenceNumber" character varying(100) NOT NULL,
        "Notes" character varying(1000),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_PartyLedgerAdjustments" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "PaymentCancellationOutboxMessages" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "PaymentIntentId" uuid,
        "SkipPaymentIntentResolution" boolean NOT NULL,
        "Reason" character varying(500) NOT NULL,
        "Actor" character varying(100) NOT NULL,
        "ProcessedAtUtc" timestamp with time zone,
        "ClaimedUntilUtc" timestamp with time zone,
        "AttemptCount" integer NOT NULL,
        "LastError" character varying(2000),
        "NextRetryAtUtc" timestamp with time zone,
        "IsFailed" boolean NOT NULL,
        "CorrelationId" character varying(100),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_PaymentCancellationOutboxMessages" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "PaymentSettlementBatches" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "SettlementType" character varying(32) NOT NULL,
        "SettlementId" character varying(200) NOT NULL,
        "Provider" character varying(100),
        "PayoutBatchId" character varying(200),
        "GrossAmount" numeric(18,2) NOT NULL,
        "Fees" numeric(18,2) NOT NULL,
        "Currency" character varying(3) NOT NULL,
        "BankReference" character varying(200),
        "OrderCount" integer NOT NULL,
        "SettledAtUtc" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_PaymentSettlementBatches" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "PaymobWebhookReplayRecords" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Provider" character varying(32) NOT NULL,
        "ProviderEventId" character varying(200) NOT NULL,
        "EventType" character varying(120) NOT NULL,
        "EvidenceId" uuid NOT NULL,
        "ActionId" uuid NOT NULL,
        "EvidenceHash" character varying(128) NOT NULL,
        "EvidenceReference" character varying(200) NOT NULL,
        "ProviderReference" character varying(240) NOT NULL,
        "CorrelationId" character varying(200) NOT NULL,
        "ProcessingTimestampUtc" timestamp with time zone NOT NULL,
        "PaymentIntentId" uuid,
        "OrderId" uuid,
        "AdmissionClassification" character varying(40) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_PaymobWebhookReplayRecords" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "PhoneRiskProfiles" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "PhoneNumber" character varying(32) NOT NULL,
        "SpamReports" integer NOT NULL DEFAULT 0,
        "IsBlocked" boolean NOT NULL DEFAULT FALSE,
        "BlockedReason" character varying(500),
        "BlockedByUserId" uuid,
        "BlockedAtUtc" timestamp with time zone,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_PhoneRiskProfiles" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "PhoneSpamReports" (
        "Id" uuid NOT NULL,
        "PhoneNumber" character varying(32) NOT NULL,
        "TenantId" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "ReportedByUserId" uuid,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_PhoneSpamReports" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "PixelTrackingConfigs" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "IsEnabled" boolean NOT NULL,
        "Strategy" integer NOT NULL,
        "Value" numeric(18,4) NOT NULL,
        "AppliesToJson" text NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedBy" text,
        CONSTRAINT "PK_PixelTrackingConfigs" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "ProcessedEvents" (
        "Id" uuid NOT NULL,
        "EventId" uuid NOT NULL,
        "ConsumerName" character varying(200) NOT NULL,
        "EventType" character varying(200) NOT NULL,
        "EntityId" uuid NOT NULL,
        "TenantId" uuid,
        "CorrelationId" character varying(200) NOT NULL,
        "ProcessedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_ProcessedEvents" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "ProcessedTrustEvent" (
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "ProductRequestIdempotency" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Key" character varying(200) NOT NULL,
        "Operation" character varying(32) NOT NULL,
        "ProductId" uuid NOT NULL,
        "ProductVariantId" uuid,
        "RequestFingerprint" character varying(64) NOT NULL,
        "ResponseStatusCode" integer,
        "ResponseJson" text,
        "CompletedAtUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_ProductRequestIdempotency" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "PromoCodes" (
        "Id" uuid NOT NULL,
        "Code" character varying(64) NOT NULL,
        "DiscountType" integer NOT NULL,
        "DiscountValue" numeric(18,2) NOT NULL,
        "MaxDiscountAmount" numeric(18,2),
        "UsageLimit" integer,
        "MaxUsagePerCustomer" integer,
        "UsedCount" integer NOT NULL,
        "ExpiryDate" timestamp with time zone,
        "IsActive" boolean NOT NULL,
        "VendorId" uuid,
        "CategoryId" uuid,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_PromoCodes" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_PromoCodes_Code_Uppercase" CHECK ("Code" = UPPER("Code")),
        CONSTRAINT "CK_PromoCodes_DiscountType" CHECK ("DiscountType" IN (0, 1)),
        CONSTRAINT "CK_PromoCodes_DiscountValue" CHECK ("DiscountValue" > 0),
        CONSTRAINT "CK_PromoCodes_MaxDiscountAmount" CHECK ("MaxDiscountAmount" IS NULL OR "MaxDiscountAmount" > 0),
        CONSTRAINT "CK_PromoCodes_MaxUsagePerCustomer" CHECK ("MaxUsagePerCustomer" IS NULL OR "MaxUsagePerCustomer" > 0),
        CONSTRAINT "CK_PromoCodes_MaxUsagePerCustomerWithinUsageLimit" CHECK ("UsageLimit" IS NULL OR "MaxUsagePerCustomer" IS NULL OR "MaxUsagePerCustomer" <= "UsageLimit"),
        CONSTRAINT "CK_PromoCodes_Scope" CHECK ((CASE WHEN "VendorId" IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN "CategoryId" IS NOT NULL THEN 1 ELSE 0 END) <= 1),
        CONSTRAINT "CK_PromoCodes_UsageLimit" CHECK ("UsageLimit" IS NULL OR "UsageLimit" > 0),
        CONSTRAINT "CK_PromoCodes_UsedCount" CHECK ("UsedCount" >= 0)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "Promotions" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Description" character varying(500),
        "DiscountType" integer NOT NULL,
        "DiscountValue" numeric(18,2) NOT NULL,
        "MaxDiscountAmount" numeric(18,2),
        "Scope" integer NOT NULL,
        "AppliesToId" uuid,
        "StartDate" timestamp with time zone NOT NULL,
        "EndDate" timestamp with time zone NOT NULL,
        "Priority" integer NOT NULL,
        "IsActive" boolean NOT NULL,
        "IsExclusive" boolean NOT NULL,
        "MinOrderAmount" numeric(18,2),
        "MaxUsage" integer,
        "CurrentUsage" integer NOT NULL,
        "BannerStyle" text NOT NULL,
        "SearchBoostOverride" double precision,
        "ProjectionMutationVersion" bigint NOT NULL DEFAULT 0,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_Promotions" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_Promotions_DiscountType" CHECK ("DiscountType" IN (0, 1)),
        CONSTRAINT "CK_Promotions_DiscountValue_FixedAmount" CHECK ("DiscountType" <> 1 OR "DiscountValue" <= 1000000.00),
        CONSTRAINT "CK_Promotions_DiscountValue_Percentage" CHECK ("DiscountType" <> 0 OR "DiscountValue" <= 100),
        CONSTRAINT "CK_Promotions_DiscountValue_Positive" CHECK ("DiscountValue" > 0)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "ProviderDeliveryOutbox" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Provider" character varying(50) NOT NULL,
        "RequestType" character varying(100) NOT NULL,
        "DeliveryKey" character varying(255) NOT NULL,
        "PayloadHash" character varying(128) NOT NULL,
        "PayloadJson" text NOT NULL,
        "EndpointUrl" character varying(2048) NOT NULL,
        "HttpMethod" character varying(16) NOT NULL,
        "ContentType" character varying(100) NOT NULL,
        "HeadersJson" text,
        "TimeoutSeconds" integer,
        "CorrelationId" character varying(100),
        "OccurredAtUtc" timestamp with time zone NOT NULL,
        "DeliveredAtUtc" timestamp with time zone,
        "ClaimedUntilUtc" timestamp with time zone,
        "NextRetryAtUtc" timestamp with time zone,
        "AttemptCount" integer NOT NULL,
        "ReplayCount" integer NOT NULL,
        "IsFailed" boolean NOT NULL,
        "IsDeadLettered" boolean NOT NULL,
        "ReplayEligible" boolean NOT NULL,
        "DeadLetteredAtUtc" timestamp with time zone,
        "FailureClass" character varying(100),
        "LastError" character varying(2000),
        "LastHttpStatusCode" integer,
        "LastResponseBody" character varying(4000),
        "FailureMetadataJson" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_ProviderDeliveryOutbox" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "ProviderDeliveryReplayAudits" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ReplayId" uuid NOT NULL,
        "MessageId" uuid NOT NULL,
        "Provider" character varying(50) NOT NULL,
        "OperatorUserId" character varying(100),
        "ReplayedAtUtc" timestamp with time zone NOT NULL,
        "Reason" character varying(2000) NOT NULL,
        "ReplaySource" character varying(100) NOT NULL,
        "PreviousStateJson" text NOT NULL,
        "NewStateJson" text NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_ProviderDeliveryReplayAudits" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "PurchaseRequestIdempotency" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Key" character varying(255) NOT NULL,
        "Operation" character varying(32) NOT NULL,
        "PurchaseInvoiceId" uuid,
        "PurchaseAttemptId" character varying(200),
        "RequestFingerprint" character varying(64) NOT NULL,
        "Status" character varying(32) NOT NULL,
        "ResponseStatusCode" integer,
        "ResponseJson" text,
        "CompletedAtUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_PurchaseRequestIdempotency" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "PurchaseReturnRequestIdempotency" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Key" character varying(255) NOT NULL,
        "PurchaseAttemptId" character varying(200),
        "RequestFingerprint" character varying(64) NOT NULL,
        "ReturnId" uuid,
        "ResponseStatusCode" integer,
        "ResponseJson" text,
        "CompletedAtUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_PurchaseReturnRequestIdempotency" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "RevokedCustomerAccessTokens" (
        "Id" uuid NOT NULL,
        "Jti" character varying(128) NOT NULL,
        "CustomerId" uuid NOT NULL,
        "RevokedAt" timestamp with time zone NOT NULL,
        "ExpiresAt" timestamp with time zone NOT NULL,
        "Reason" character varying(256),
        CONSTRAINT "PK_RevokedCustomerAccessTokens" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "SalesRepresentatives" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Name" text NOT NULL,
        "Phone" text,
        "IsActive" boolean NOT NULL,
        "MonthlyTarget" numeric,
        "CommissionPercentage" numeric,
        "Notes" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_SalesRepresentatives" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "SalesRequestIdempotency" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Key" character varying(200) NOT NULL,
        "Operation" character varying(32) NOT NULL,
        "RequestHash" character varying(64) NOT NULL,
        "ResponseStatusCode" integer,
        "ResponseJson" text,
        "CompletedAtUtc" timestamp with time zone,
        "ClaimToken" character varying(32),
        "ClaimTimestampUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_SalesRequestIdempotency" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "SearchClicks" (
        "Id" uuid NOT NULL,
        "QueryText" character varying(500) NOT NULL,
        "ProductId" uuid NOT NULL,
        "VariantId" uuid,
        "Position" integer NOT NULL,
        "ClickedAt" timestamp with time zone NOT NULL,
        "SessionId" character varying(100),
        "TenantId" uuid,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_SearchClicks" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "SearchFilterEvents" (
        "Id" uuid NOT NULL,
        "FilterKey" character varying(200) NOT NULL,
        "FilterValue" character varying(500) NOT NULL,
        "QueryText" character varying(500) NOT NULL,
        "OccurredAt" timestamp with time zone NOT NULL,
        "SessionId" character varying(100),
        "TenantId" uuid,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_SearchFilterEvents" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "SearchImpressions" (
        "Id" uuid NOT NULL,
        "QueryText" character varying(500) NOT NULL,
        "ProductId" uuid NOT NULL,
        "Position" integer NOT NULL,
        "ImpressedAt" timestamp with time zone NOT NULL,
        "SessionId" character varying(100),
        "TenantId" uuid,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_SearchImpressions" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "SearchQueries" (
        "Id" uuid NOT NULL,
        "QueryText" character varying(500) NOT NULL,
        "ResultCount" bigint NOT NULL,
        "Intent" character varying(50) NOT NULL,
        "AppliedExpansionsJson" text,
        "SearchedAt" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_SearchQueries" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "SearchSynonyms" (
        "Id" uuid NOT NULL,
        "Term" character varying(200) NOT NULL,
        "SynonymsJson" text NOT NULL,
        "Direction" character varying(20) NOT NULL DEFAULT 'one_way',
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_SearchSynonyms" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "ShipmentCustomsSnapshots" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ShipmentId" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "SnapshotVersion" integer NOT NULL,
        "CarrierNeutralDtoVersion" integer NOT NULL,
        "DestinationCountryIso2" character(2) NOT NULL,
        "OriginCountryIso2" character(2) NOT NULL,
        "ShipmentGrossWeight" numeric(18,6) NOT NULL,
        "ShipmentGrossWeightUnit" character varying(16) NOT NULL,
        "SnapshotFingerprintSha256" character(64) NOT NULL,
        "SnapshotJson" jsonb NOT NULL,
        "IsRegistrationFrozen" boolean NOT NULL,
        "RegistrationBeganAtUtc" timestamp with time zone NOT NULL,
        "CorrelationId" character varying(128) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_ShipmentCustomsSnapshots" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_ShipmentCustomsSnapshot_Countries" CHECK (char_length("DestinationCountryIso2") = 2 AND char_length("OriginCountryIso2") = 2),
        CONSTRAINT "CK_ShipmentCustomsSnapshot_Fingerprint" CHECK (char_length("SnapshotFingerprintSha256") = 64),
        CONSTRAINT "CK_ShipmentCustomsSnapshot_Frozen" CHECK ("IsRegistrationFrozen" = TRUE),
        CONSTRAINT "CK_ShipmentCustomsSnapshot_GrossWeight" CHECK ("ShipmentGrossWeight" > 0),
        CONSTRAINT "CK_ShipmentCustomsSnapshot_Version" CHECK ("SnapshotVersion" = 1 AND "CarrierNeutralDtoVersion" = 1)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "ShippingMethods" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Name" text NOT NULL,
        "NameAr" text,
        "Description" text,
        "BaseRate" numeric NOT NULL,
        "PerKgRate" numeric NOT NULL,
        "FreeShippingThreshold" numeric,
        "EstimatedDeliveryDaysMin" integer NOT NULL,
        "EstimatedDeliveryDaysMax" integer NOT NULL,
        "IsActive" boolean NOT NULL,
        "SortOrder" integer NOT NULL,
        "AvailableCountries" text,
        "CarrierName" text,
        "CarrierCode" text,
        "ServiceLevel" text NOT NULL,
        "TrackingUrlTemplate" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_ShippingMethods" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "ShippingWebhookLogs" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CarrierCode" character varying(50) NOT NULL,
        "EventId" character varying(200) NOT NULL,
        "PayloadHash" character varying(128) NOT NULL,
        "EventType" character varying(200) NOT NULL,
        "TrackingNumber" character varying(100),
        "ShipmentId" uuid,
        "OrderId" uuid,
        "PayloadSummary" character varying(1000) NOT NULL,
        "Signature" character varying(1000),
        "SignatureVerified" boolean NOT NULL,
        "ValidationMethod" character varying(50) NOT NULL,
        "ProcessingStatus" integer NOT NULL,
        "ProcessedAt" timestamp with time zone,
        "ProcessingError" character varying(1000),
        "ProcessingDurationMs" bigint,
        "SourceIpPrefix" character varying(50),
        "UserAgentSummary" character varying(200),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_ShippingWebhookLogs" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "SmartOffers" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Status" character varying(32) NOT NULL,
        "Priority" integer NOT NULL,
        "TriggerType" character varying(32) NOT NULL,
        "PagesScope" character varying(32) NOT NULL,
        "DelaySeconds" integer,
        "ScrollPercent" integer,
        "ShowOncePerSession" boolean NOT NULL,
        "MaxDisplaysPerUser" integer,
        "PopupTitle" character varying(200) NOT NULL,
        "PopupDescription" character varying(1000),
        "ButtonText" character varying(100) NOT NULL,
        "CouponCode" character varying(100),
        "GeneratedCouponId" uuid,
        "CouponDiscountType" character varying(32),
        "CouponDiscountValue" numeric(18,2),
        "CouponMinOrderAmount" numeric(18,2),
        "CouponMaxDiscountAmount" numeric(18,2),
        "ImageUrl" character varying(1000),
        "BackgroundColor" character varying(32),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        CONSTRAINT "PK_SmartOffers" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_SmartOffers_CouponCode_Presence" CHECK (NULLIF(REGEXP_REPLACE("CouponCode", '\s', '', 'g'), '') IS NOT NULL OR ("CouponDiscountType" IS NULL AND "CouponDiscountValue" IS NULL AND "CouponMinOrderAmount" IS NULL AND "CouponMaxDiscountAmount" IS NULL)),
        CONSTRAINT "CK_SmartOffers_CouponDiscountType" CHECK ("CouponDiscountType" IS NULL OR "CouponDiscountType" IN ('Percentage', 'FixedAmount')),
        CONSTRAINT "CK_SmartOffers_CouponDiscountValue_FixedAmount" CHECK ("CouponDiscountValue" IS NULL OR COALESCE("CouponDiscountType", 'Percentage') <> 'FixedAmount' OR "CouponDiscountValue" <= 1000000.00),
        CONSTRAINT "CK_SmartOffers_CouponDiscountValue_Percentage" CHECK ("CouponDiscountValue" IS NULL OR COALESCE("CouponDiscountType", 'Percentage') <> 'Percentage' OR "CouponDiscountValue" <= 100),
        CONSTRAINT "CK_SmartOffers_CouponDiscountValue_Positive" CHECK ("CouponDiscountValue" IS NULL OR "CouponDiscountValue" > 0),
        CONSTRAINT "CK_SmartOffers_CouponDiscountValue_Presence" CHECK (NULLIF(REGEXP_REPLACE("CouponCode", '\s', '', 'g'), '') IS NULL OR "CouponDiscountValue" IS NOT NULL)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "StockSyncOutboxMessages" (
        "Id" uuid NOT NULL,
        "EventId" uuid NOT NULL,
        "EventType" character varying(200) NOT NULL,
        "ClrType" character varying(1000) NOT NULL,
        "PayloadJson" text NOT NULL,
        "TenantId" uuid NOT NULL,
        "VariantId" uuid NOT NULL,
        "WarehouseId" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "EventVersion" integer NOT NULL DEFAULT 1,
        "CorrelationId" character varying(200) NOT NULL,
        "OccurredAtUtc" timestamp with time zone NOT NULL,
        "StockUpdatedAtUtc" timestamp with time zone NOT NULL,
        "PublishedAtUtc" timestamp with time zone,
        "ClaimedUntilUtc" timestamp with time zone,
        "AttemptCount" integer NOT NULL,
        "LastError" character varying(2000),
        "NextRetryAtUtc" timestamp with time zone,
        "IsFailed" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_StockSyncOutboxMessages" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_StockSyncOutbox_EventVersion_Positive" CHECK ("EventVersion" > 0),
        CONSTRAINT "CK_StockSyncOutbox_WarehouseId_NotEmpty" CHECK ("WarehouseId" <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT "CK_StockSyncOutboxMessages_EventVersion_Positive" CHECK ("EventVersion" > 0),
        CONSTRAINT "CK_StockSyncOutboxMessages_OrderingTimestamps_Required" CHECK ("OccurredAtUtc" IS NOT NULL AND "StockUpdatedAtUtc" IS NOT NULL),
        CONSTRAINT "CK_StockSyncOutboxMessages_WarehouseId_NotEmpty" CHECK ("WarehouseId" <> '00000000-0000-0000-0000-000000000000')
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "StockTransferRequestIdempotency" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Key" character varying(255) NOT NULL,
        "Operation" character varying(64) NOT NULL,
        "RequestFingerprint" character varying(64) NOT NULL,
        "StockTransferId" uuid,
        "ResponseStatusCode" integer,
        "ResponseJson" text,
        "CompletedAtUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_StockTransferRequestIdempotency" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "StoreConfigAuditEntries" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "UserId" uuid,
        "UserName" character varying(256),
        "ActionType" character varying(50) NOT NULL,
        "Target" character varying(100) NOT NULL,
        "BeforeSnapshotJson" text,
        "AfterSnapshotJson" text,
        "Version" integer,
        "TraceId" character varying(128),
        "IpAddress" character varying(45),
        "UserAgent" text,
        "TimestampUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_StoreConfigAuditEntries" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "StoreConfigs" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "StoreName" character varying(100) NOT NULL,
        "StoreDescription" character varying(500),
        "ThemeId" character varying(50) NOT NULL,
        "Theme_Colors_Primary" text NOT NULL,
        "Theme_Colors_Secondary" text NOT NULL,
        "Theme_Colors_Background" text NOT NULL,
        "Theme_Colors_Text" text NOT NULL,
        "Theme_Colors_Muted" text NOT NULL,
        "Theme_Font_Family" text NOT NULL,
        "Theme_Font_BaseSize" text NOT NULL,
        "Theme_Ui_BorderRadius" text NOT NULL,
        "Theme_Ui_ButtonStyle" text NOT NULL,
        "Theme_Layout_ProductCardVariant" text NOT NULL,
        "Theme_Layout_HeaderVariant" text NOT NULL,
        "Behavior_EnableGuestCheckout" boolean NOT NULL,
        "Behavior_ShowOutOfStockProducts" boolean NOT NULL,
        "Behavior_EnableSearch" boolean NOT NULL,
        "Behavior_FailCheckoutOnPromotionError" boolean NOT NULL,
        "StorefrontStatus" text NOT NULL,
        "MaintenanceMessage" text,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "CustomizationJson" text NOT NULL,
        "ThemePresetKey" character varying(100) NOT NULL,
        "ThemeLivePresetKey" character varying(100) NOT NULL,
        "ThemeDraftOverridesJson" text NOT NULL,
        "ThemeLiveOverridesJson" text NOT NULL,
        "VersionsJson" text NOT NULL,
        "LiveVersion" integer NOT NULL,
        "PromotionRanking_Enabled" boolean NOT NULL,
        "PromotionRanking_BaseBoost" double precision NOT NULL,
        "PromotionRanking_HighDiscountBoost" double precision NOT NULL,
        "PromotionRanking_HighDiscountThreshold" double precision NOT NULL,
        "PromotionRanking_MaxTotalBoost" double precision NOT NULL,
        "PromotionRanking_EnableTimeDecay" boolean NOT NULL,
        "PromotionRanking_TimeDecayScaleDays" double precision NOT NULL,
        "PromotionRanking_TimeDecayOffsetDays" double precision NOT NULL,
        "PromotionRanking_TimeDecayMinimum" double precision NOT NULL,
        "CommerceAiEnabled" boolean NOT NULL,
        "CommerceAiEnabledAt" timestamp with time zone,
        "CommerceAiUpdatedBy" text,
        "CommerceAiWidgetStyle" text NOT NULL,
        "CommerceAiWidgetPosition" text NOT NULL,
        "CommerceAiAutoGreetingEnabled" boolean NOT NULL,
        "CommerceAiAutoGreetingMessage" text NOT NULL,
        "CommerceAiPersonality" text NOT NULL,
        CONSTRAINT "PK_StoreConfigs" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "StorefrontAuthProviderSettings" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Provider" character varying(50) NOT NULL,
        "IsEnabled" boolean NOT NULL DEFAULT FALSE,
        "ClientId" character varying(256),
        "ClientSecretEncrypted" character varying(1024),
        "AdditionalScopes" character varying(512),
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "UpdatedByUserId" uuid,
        CONSTRAINT "PK_StorefrontAuthProviderSettings" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "StorefrontCartCouponStates" (
        "Id" uuid NOT NULL,
        "CartId" uuid NOT NULL,
        "OwnerType" character varying(32) NOT NULL,
        "OwnerId" character varying(128) NOT NULL,
        "HasAppliedCoupon" boolean NOT NULL,
        "CouponId" uuid,
        "CouponCode" character varying(128),
        "DiscountType" character varying(32),
        "DiscountValue" numeric(18,4),
        "DiscountAmount" numeric(18,4),
        "CartSubtotal" numeric(18,4) NOT NULL,
        "CustomerId" uuid,
        "MutationVersion" integer NOT NULL,
        "LastRemovalReason" character varying(128),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        "TenantId" uuid NOT NULL,
        CONSTRAINT "PK_StorefrontCartCouponStates" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "StorefrontFooterSections" (
        "Id" uuid NOT NULL,
        "TenantId" uuid,
        "Title" character varying(120) NOT NULL,
        "TitleAr" character varying(120),
        "SortOrder" integer NOT NULL,
        "IsActive" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_StorefrontFooterSections" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "StorefrontSocialLinks" (
        "Id" uuid NOT NULL,
        "TenantId" uuid,
        "Platform" character varying(40) NOT NULL,
        "Url" character varying(1000) NOT NULL,
        "Icon" character varying(80),
        "SortOrder" integer NOT NULL,
        "IsActive" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_StorefrontSocialLinks" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "StripeWebhookReplayRecords" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Provider" character varying(32) NOT NULL,
        "ProviderEventId" character varying(200) NOT NULL,
        "EventType" character varying(120) NOT NULL,
        "EvidenceId" uuid NOT NULL,
        "ActionId" uuid NOT NULL,
        "EvidenceHash" character varying(128) NOT NULL,
        "EvidenceReference" character varying(200) NOT NULL,
        "ProviderReference" character varying(240) NOT NULL,
        "CorrelationId" character varying(200) NOT NULL,
        "ProcessingTimestampUtc" timestamp with time zone NOT NULL,
        "PaymentIntentId" uuid,
        "OrderId" uuid,
        "AdmissionClassification" character varying(40) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_StripeWebhookReplayRecords" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "SuperAdmins" (
        "Id" uuid NOT NULL,
        "Email" character varying(320) NOT NULL,
        "PasswordHash" character varying(512) NOT NULL,
        "IsSystem" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "LastLoginAt" timestamp with time zone,
        "RefreshTokenHash" character varying(256),
        "RefreshTokenExpiresAtUtc" timestamp with time zone,
        "TokenVersion" integer NOT NULL DEFAULT 0,
        CONSTRAINT "PK_SuperAdmins" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "Suppliers" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Phone" character varying(50),
        "Email" character varying(200),
        "Address" text,
        "TaxNumber" character varying(50),
        "Balance" numeric(18,2) NOT NULL,
        "OpeningBalanceAmount" numeric,
        "BalanceType" text,
        "OpeningBalanceDate" timestamp with time zone,
        "PaymentTermsDays" integer NOT NULL,
        "Notes" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_Suppliers" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "SystemSchemaVersions" (
        "Id" uuid NOT NULL,
        "Component" character varying(100) NOT NULL,
        "Version" character varying(200) NOT NULL,
        "AppliedAt" timestamp with time zone NOT NULL,
        "DetailsJson" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_SystemSchemaVersions" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "TenantAnalyticsConfigurations" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "SettingsJson" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_TenantAnalyticsConfigurations" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "TenantPaymentMethods" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Code" character varying(50) NOT NULL,
        "DisplayName" character varying(100) NOT NULL,
        "DisplayNameAr" character varying(100),
        "Description" character varying(500),
        "DescriptionAr" character varying(500),
        "IsEnabled" boolean NOT NULL,
        "SortOrder" integer NOT NULL,
        "SupportsOnline" boolean NOT NULL,
        "SupportsOffline" boolean NOT NULL,
        "IsSandbox" boolean NOT NULL,
        "ConfigEncrypted" text,
        "Icon" character varying(50),
        "MinOrderAmount" numeric(18,4),
        "MaxOrderAmount" numeric(18,4),
        "Fee" numeric(18,4) NOT NULL,
        "FeeType" character varying(20) NOT NULL DEFAULT 'fixed',
        "UpdatedAtUtc" timestamp with time zone,
        "Version" integer NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_TenantPaymentMethods" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "ThemePresets" (
        "Id" uuid NOT NULL,
        "Key" character varying(100) NOT NULL,
        "Name" character varying(100) NOT NULL,
        "IsRtl" boolean NOT NULL,
        "BaseThemeJson" text NOT NULL,
        "PreviewImageUrl" character varying(500),
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_ThemePresets" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "Units" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Name" text NOT NULL,
        "Symbol" text,
        "Description" text,
        "IsActive" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_Units" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "UserGovernanceAuditLogs" (
        "Id" uuid NOT NULL,
        "TargetUserId" uuid NOT NULL,
        "TargetRole" integer NOT NULL,
        "AdminId" uuid,
        "ActorType" character varying(50) NOT NULL,
        "ActionType" character varying(100) NOT NULL,
        "Reason" character varying(1000) NOT NULL,
        "BeforeState" jsonb NOT NULL,
        "AfterState" jsonb NOT NULL,
        "Metadata" jsonb,
        "TimestampUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_UserGovernanceAuditLogs" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "UserGovernanceBlacklistEntries" (
        "Id" uuid NOT NULL,
        "Type" integer NOT NULL,
        "ValueNormalized" character varying(320) NOT NULL,
        "ValueDisplay" character varying(320) NOT NULL,
        "Reason" character varying(1000) NOT NULL,
        "IsActive" boolean NOT NULL,
        "CreatedByAdminId" uuid,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "RevokedByAdminId" uuid,
        "RevokedAtUtc" timestamp with time zone,
        "RevokedReason" character varying(1000),
        CONSTRAINT "PK_UserGovernanceBlacklistEntries" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "UserGovernanceProfiles" (
        "Id" uuid NOT NULL,
        "SubjectId" uuid NOT NULL,
        "Role" integer NOT NULL,
        "Status" integer NOT NULL,
        "RiskLevel" integer NOT NULL,
        "IsKycVerified" boolean NOT NULL,
        "KycVerifiedAtUtc" timestamp with time zone,
        "KycVerifiedByAdminId" uuid,
        "KycVerificationReason" character varying(1000),
        "IsMarketplaceApproved" boolean NOT NULL,
        "MarketplaceApprovedAtUtc" timestamp with time zone,
        "MarketplaceApprovedByAdminId" uuid,
        "MarketplaceApprovalReason" character varying(1000),
        "SuspiciousFlags" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "LastLoginAtUtc" timestamp with time zone,
        "StatusChangedAtUtc" timestamp with time zone,
        "LastRiskEvaluatedAtUtc" timestamp with time zone,
        "LastModifiedByAdminId" uuid,
        "StatusReason" character varying(1000),
        "LastRiskReason" character varying(1000),
        "LastIpAddress" character varying(128),
        "DeviceFingerprint" character varying(200),
        CONSTRAINT "PK_UserGovernanceProfiles" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_UserGovernanceProfiles_MarketplaceApprovalRequiresKyc" CHECK (NOT "IsMarketplaceApproved" OR "IsKycVerified")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "Users" (
        "Id" uuid NOT NULL,
        "Username" character varying(100) NOT NULL,
        "Email" character varying(200) NOT NULL,
        "PasswordHash" text NOT NULL,
        "FirstName" character varying(100) NOT NULL,
        "LastName" character varying(100) NOT NULL,
        "Phone" character varying(50),
        "Role" integer NOT NULL,
        "IsActive" boolean NOT NULL,
        "LastLoginAt" timestamp with time zone,
        "RefreshToken" text,
        "RefreshTokenExpiryTime" timestamp with time zone,
        "TenantId" uuid NOT NULL,
        "EmailVerificationStatus" integer NOT NULL,
        "EmailVerifiedAtUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_Users" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "Warehouses" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Name" text NOT NULL,
        "Code" text,
        "Address" text,
        "Phone" text,
        "IsActive" boolean NOT NULL,
        "IsDefault" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_Warehouses" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE whatsapp_message_templates (
        "Id" uuid NOT NULL,
        "Type" character varying(50) NOT NULL,
        "Language" character varying(10) NOT NULL,
        "Content" text NOT NULL,
        "IsActive" boolean NOT NULL,
        "IsDefault" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT (NOW()),
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        "TenantId" uuid NOT NULL,
        CONSTRAINT "PK_whatsapp_message_templates" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "WhatsAppRateLimits" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "PhoneNumber" character varying(20) NOT NULL,
        "MessageCount" integer NOT NULL,
        "WindowStartUtc" timestamp with time zone NOT NULL,
        "LastMessageAtUtc" timestamp with time zone NOT NULL,
        "OptedOut" boolean NOT NULL DEFAULT FALSE,
        "OptedOutAtUtc" timestamp with time zone,
        CONSTRAINT "PK_WhatsAppRateLimits" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "AffiliateBalanceLedger" (
        "Id" uuid NOT NULL,
        "TenantId" uuid,
        "StoreTenantId" uuid NOT NULL,
        "AffiliateAccountId" uuid NOT NULL,
        "EntryType" integer NOT NULL,
        "Amount" numeric(18,4) NOT NULL,
        "ReferenceId" uuid,
        "Description" text,
        "IdempotencyKey" character varying(255) NOT NULL,
        "Currency" character varying(3) NOT NULL,
        "RecordedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" uuid,
        CONSTRAINT "PK_AffiliateBalanceLedger" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AffiliateBalanceLedger_Amount" CHECK ("Amount" != 0),
        CONSTRAINT "CK_AffiliateBalanceLedger_EntryType" CHECK ("EntryType" IN (1, 2, 3, 4)),
        CONSTRAINT "CK_AffiliateBalanceLedger_IdempotencyKey_NotBlank" CHECK (btrim("IdempotencyKey") <> ''),
        CONSTRAINT "CK_AffiliateBalanceLedger_WithdrawalNegative" CHECK (("EntryType" != 1 OR "Amount" < 0)),
        CONSTRAINT "FK_AffiliateBalanceLedger_AffiliateAccounts_AffiliateAccountId" FOREIGN KEY ("AffiliateAccountId") REFERENCES "AffiliateAccounts" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "AffiliateUsers" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Email" character varying(255) NOT NULL,
        "PasswordHash" character varying(500) NOT NULL,
        "AffiliateAccountId" uuid NOT NULL,
        "EmailVerified" boolean NOT NULL DEFAULT FALSE,
        "RefreshToken" character varying(500),
        "PreviousRefreshToken" character varying(500),
        "RefreshTokenExpiryTime" timestamp with time zone,
        "TokensValidAfterUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_AffiliateUsers" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AffiliateUsers_AffiliateAccounts_AffiliateAccountId" FOREIGN KEY ("AffiliateAccountId") REFERENCES "AffiliateAccounts" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "AffiliatePayoutTransferAllocations" (
        "Id" uuid NOT NULL,
        "EvidenceId" uuid NOT NULL,
        "StoreTenantId" uuid NOT NULL,
        "AffiliateAccountId" uuid NOT NULL,
        "ReferenceId" uuid NOT NULL,
        "SettlementSource" integer NOT NULL,
        "Amount" numeric(18,4) NOT NULL,
        "Currency" character varying(3) NOT NULL,
        "IdempotencyKey" character varying(255) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_AffiliatePayoutTransferAllocations" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AffiliatePayoutTransferAllocation_Affiliate_Required" CHECK ("AffiliateAccountId" <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT "CK_AffiliatePayoutTransferAllocation_Amount_Positive" CHECK ("Amount" > 0),
        CONSTRAINT "CK_AffiliatePayoutTransferAllocation_IdempotencyKey_NotBlank" CHECK (btrim("IdempotencyKey") <> ''),
        CONSTRAINT "CK_AffiliatePayoutTransferAllocation_Reference_Required" CHECK ("ReferenceId" <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT "CK_AffiliatePayoutTransferAllocation_Tenant_Required" CHECK ("StoreTenantId" <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT "FK_AffiliatePayoutTransferAllocations_AffiliatePayoutTransferE~" FOREIGN KEY ("EvidenceId") REFERENCES "AffiliatePayoutTransferEvidence" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "BankTransactions" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "BankId" uuid NOT NULL,
        "TransactionDate" timestamp with time zone NOT NULL,
        "TransactionType" integer NOT NULL,
        "Amount" numeric(18,2) NOT NULL,
        "Description" text,
        "ReferenceNumber" text,
        "Notes" text,
        "IsOpeningBalance" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_BankTransactions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_BankTransactions_Banks_BankId" FOREIGN KEY ("BankId") REFERENCES "Banks" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CarrierGeographicSyncRuns" (
        "Id" uuid NOT NULL,
        "CarrierProviderId" uuid NOT NULL,
        "Environment" character varying(24) NOT NULL,
        "AccountScopeKind" character varying(24) NOT NULL,
        "AccountScopeId" uuid,
        "TenantId" uuid,
        "SourceKind" character varying(24) NOT NULL,
        "SourceUri" text,
        "SourceVersion" character varying(128),
        "SourceHashSha256" character(64),
        "StartedAtUtc" timestamp with time zone NOT NULL,
        "CompletedAtUtc" timestamp with time zone,
        "Status" character varying(24) NOT NULL,
        "RawEvidenceObjectKey" text,
        "AddedCount" integer NOT NULL,
        "ChangedCount" integer NOT NULL,
        "RemovedCount" integer NOT NULL,
        "AmbiguousCount" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_CarrierGeographicSyncRuns" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CarrierGeographicSyncRuns_CarrierProviders_CarrierProviderId" FOREIGN KEY ("CarrierProviderId") REFERENCES "CarrierProviders" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "MarketplaceCategoryTranslations" (
        "Id" uuid NOT NULL,
        "CategoryId" uuid NOT NULL,
        "Language" character varying(5) NOT NULL,
        "Name" character varying(160) NOT NULL,
        "Slug" character varying(180),
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_MarketplaceCategoryTranslations" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_MarketplaceCategoryTranslations_Categories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES "Categories" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "Products" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Name" character varying(300) NOT NULL,
        "NameAr" text,
        "Barcode" character varying(100),
        "SKU" character varying(100),
        "Description" text,
        "DescriptionAr" text,
        "FeaturesJson" jsonb,
        "FaqJson" jsonb,
        "CategoryId" uuid NOT NULL,
        "PurchasePrice" numeric(18,2) NOT NULL,
        "SalePrice" numeric(18,2) NOT NULL,
        "OnlinePrice" numeric,
        "PriceIncludesTax" boolean NOT NULL,
        "TaxRate" numeric(5,2) NOT NULL,
        "Unit" character varying(50) NOT NULL,
        "MinimumStockLevel" numeric(18,2) NOT NULL,
        "Images" text,
        "IsActive" boolean NOT NULL,
        "TrackStock" boolean NOT NULL,
        "HasVariants" boolean NOT NULL,
        "Notes" text,
        "Slug" text,
        "MetaTitle" text,
        "MetaDescription" text,
        "MetaKeywords" text,
        "Weight" numeric NOT NULL,
        "Length" numeric NOT NULL,
        "Width" numeric NOT NULL,
        "Height" numeric NOT NULL,
        "WeightUnit" text NOT NULL,
        "DimensionUnit" text NOT NULL,
        "PublishStatus" integer NOT NULL,
        "IsOnlineVisible" boolean NOT NULL,
        "IsSearchable" boolean NOT NULL,
        "IsFeatured" boolean NOT NULL,
        "LowStockThreshold" integer NOT NULL,
        "AllowBackorder" boolean NOT NULL,
        "ProjectionMutationVersion" bigint NOT NULL,
        "Brand" text,
        "Manufacturer" text,
        "Tags" text,
        "TagsJson" jsonb,
        "AiGenerated" boolean NOT NULL,
        "AiMetadataJson" jsonb,
        "RelatedProductIds" text,
        "VideoUrl" text,
        "ProductTemplate" character varying(100),
        "AmazonASIN" text,
        "LastSyncedAt" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_Products" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Products_Categories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES "Categories" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubCategoryAttributes" (
        "Id" uuid NOT NULL,
        "CategoryId" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "NameAr" character varying(200),
        "Type" character varying(20) NOT NULL,
        "IsRequired" boolean NOT NULL,
        "IsFilterable" boolean NOT NULL DEFAULT TRUE,
        "IsSearchable" boolean NOT NULL DEFAULT TRUE,
        "OptionsJson" text,
        "SortOrder" integer NOT NULL DEFAULT 0,
        CONSTRAINT "PK_CommerceHubCategoryAttributes" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CommerceHubCategoryAttributes_CommerceHubCategories_Categor~" FOREIGN KEY ("CategoryId") REFERENCES "CommerceHubCategories" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubCustomerEmailVerificationTokens" (
        "Id" uuid NOT NULL,
        "CustomerId" uuid NOT NULL,
        "TokenHash" character varying(128) NOT NULL,
        "ExpiresAtUtc" timestamp with time zone NOT NULL,
        "IsUsed" boolean NOT NULL DEFAULT FALSE,
        "UsedAtUtc" timestamp with time zone,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_CommerceHubCustomerEmailVerificationTokens" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CommerceHubCustomerEmailVerificationTokens_CommerceHubCusto~" FOREIGN KEY ("CustomerId") REFERENCES "CommerceHubCustomers" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubCustomerIdentityEvents" (
        "Id" uuid NOT NULL,
        "CustomerId" uuid NOT NULL,
        "EventType" character varying(100) NOT NULL,
        "Description" character varying(500),
        "Metadata" character varying(2000),
        "IpAddress" character varying(45),
        "OccurredAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_CommerceHubCustomerIdentityEvents" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CommerceHubCustomerIdentityEvents_CommerceHubCustomers_Cust~" FOREIGN KEY ("CustomerId") REFERENCES "CommerceHubCustomers" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubCustomerPasswordResetTokens" (
        "Id" uuid NOT NULL,
        "CustomerId" uuid NOT NULL,
        "TokenHash" character varying(128) NOT NULL,
        "ExpiresAtUtc" timestamp with time zone NOT NULL,
        "IsUsed" boolean NOT NULL DEFAULT FALSE,
        "UsedAtUtc" timestamp with time zone,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedByIp" character varying(45),
        CONSTRAINT "PK_CommerceHubCustomerPasswordResetTokens" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CommerceHubCustomerPasswordResetTokens_CommerceHubCustomers~" FOREIGN KEY ("CustomerId") REFERENCES "CommerceHubCustomers" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubCustomerPayoutDetails" (
        "Id" uuid NOT NULL,
        "CustomerId" uuid NOT NULL,
        "Method" integer NOT NULL,
        "AccountIdentifier" character varying(500) NOT NULL,
        "AccountHolderName" character varying(200),
        "IsVerified" boolean NOT NULL DEFAULT FALSE,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CommerceHubCustomerPayoutDetails" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CommerceHubCustomerPayoutDetails_CommerceHubCustomers_Custo~" FOREIGN KEY ("CustomerId") REFERENCES "CommerceHubCustomers" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubMarketplaceOrders" (
        "Id" uuid NOT NULL,
        "CustomerId" uuid NOT NULL,
        "OrderNumber" text NOT NULL,
        "IdempotencyKey" character varying(100),
        "FullName" character varying(200) NOT NULL,
        "PhoneNumber" character varying(50) NOT NULL,
        "AddressLine" character varying(500) NOT NULL,
        "City" character varying(100) NOT NULL,
        "Area" character varying(100),
        "Notes" character varying(1000),
        "Currency" character varying(10) NOT NULL,
        "PaymentMethodCode" character varying(50) NOT NULL,
        "PaymentMethodName" character varying(100) NOT NULL,
        "PaymentReference" character varying(100),
        "PaymentReferenceId" character varying(100),
        "PaymentGateway" character varying(50),
        "Subtotal" numeric(18,2) NOT NULL,
        "ShippingTotal" numeric(18,2) NOT NULL,
        "TotalAmount" numeric(18,2) NOT NULL,
        "PromoCode" text,
        "PromoDiscountTotal" numeric NOT NULL,
        "Status" integer NOT NULL,
        "PaymentStatus" integer NOT NULL,
        "PaidAt" timestamp with time zone,
        "OnlineCustomerId" uuid,
        "OrderAccessToken" text,
        "IsProjected" boolean NOT NULL,
        "ProjectedAtUtc" timestamp with time zone,
        "ProjectionMutationVersion" bigint NOT NULL DEFAULT 1,
        "LastProjectionAttemptAt" timestamp with time zone,
        "ProjectionAttemptCount" integer NOT NULL,
        "IsProjectionTerminalFailure" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CommerceHubMarketplaceOrders" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CommerceHubMarketplaceOrders_Currency_EGP" CHECK ("Currency" = 'EGP'),
        CONSTRAINT "CK_CommerceHubMarketplaceOrders_CustomerAuthorityBoundary" CHECK ("CustomerId" <> '00000000-0000-0000-0000-000000000000' AND "OnlineCustomerId" IS NULL),
        CONSTRAINT "FK_CommerceHubMarketplaceOrders_CommerceHubCustomers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "CommerceHubCustomers" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubPayouts" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "PayoutMethodId" uuid,
        "Amount" numeric(18,2) NOT NULL,
        "Currency" character varying(10) NOT NULL,
        "Status" integer NOT NULL,
        "Method" integer NOT NULL,
        "DestinationValueSnapshot" character varying(200),
        "Reference" character varying(200),
        "ExternalReferenceId" character varying(200),
        "BankTransactionId" character varying(200),
        "ExternalTransferIdempotencyKey" character varying(240),
        "ExternalTransferHandoffToken" character varying(128),
        "ExternalTransferHandoffCommittedAtUtc" timestamp with time zone,
        "ProcessedBy" character varying(200),
        "FailureReason" character varying(1000),
        "IdempotencyKey" character varying(200) NOT NULL,
        "ProcessedAtUtc" timestamp with time zone,
        "CompletedAtUtc" timestamp with time zone,
        "CompletionEvidenceFingerprint" character varying(128),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CommerceHubPayouts" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CommerceHubPayouts_Amount_Positive" CHECK ("Amount" > 0),
        CONSTRAINT "CK_CommerceHubPayouts_Completed_Evidence" CHECK ("Status" <> 2 OR NULLIF(btrim("ExternalReferenceId"), '') IS NOT NULL OR NULLIF(btrim("BankTransactionId"), '') IS NOT NULL),
        CONSTRAINT "CK_CommerceHubPayouts_Completed_State_Complete" CHECK ("Status" <> 2 OR ("ProcessedAtUtc" IS NOT NULL AND "CompletedAtUtc" IS NOT NULL)),
        CONSTRAINT "CK_CommerceHubPayouts_Currency_EGP" CHECK ("Currency" = 'EGP'),
        CONSTRAINT "CK_CommerceHubPayouts_NoRefundFailureAfterHandoff" CHECK (NOT ("Status" = 3 AND "ExternalTransferHandoffCommittedAtUtc" IS NOT NULL AND "ProcessedBy" IN ('system:refund', 'system:refund-control'))),
        CONSTRAINT "CK_CommerceHubPayouts_Processing_ExternalIntent" CHECK (("Status" <> 1 AND "Status" <> 4) OR NULLIF(btrim("ExternalTransferIdempotencyKey"), '') IS NOT NULL),
        CONSTRAINT "CK_CommerceHubPayouts_Processing_HandoffCommitted" CHECK (("Status" <> 1 AND "Status" <> 4) OR (NULLIF(btrim("ExternalTransferHandoffToken"), '') IS NOT NULL AND "ExternalTransferHandoffCommittedAtUtc" IS NOT NULL)),
        CONSTRAINT "CK_CommerceHubPayouts_Terminal_HandoffCommitted" CHECK (("Status" <> 2 AND "Status" <> 4) OR (NULLIF(btrim("ExternalTransferHandoffToken"), '') IS NOT NULL AND "ExternalTransferHandoffCommittedAtUtc" IS NOT NULL)),
        CONSTRAINT "FK_CommerceHubPayouts_CommerceHubVendorPayoutMethods_PayoutMet~" FOREIGN KEY ("PayoutMethodId") REFERENCES "CommerceHubVendorPayoutMethods" ("Id") ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "Tenants" (
        "Id" uuid NOT NULL,
        "Name" character varying(100) NOT NULL,
        "IsActive" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "Slug" character varying(63),
        "ContactEmail" character varying(255),
        "Status" integer NOT NULL,
        "Country" character varying(2),
        "Currency" character varying(3) NOT NULL DEFAULT 'EGP',
        "SuspendedReason" text,
        "DeletedAtUtc" timestamp with time zone,
        "BootstrapVersion" integer NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "StorefrontBaseUrl" text,
        "UseBaileys" boolean NOT NULL,
        CONSTRAINT "PK_Tenants" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Tenants_Currencies_Currency" FOREIGN KEY ("Currency") REFERENCES "Currencies" ("Code") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CustomerTransactions" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CustomerId" uuid NOT NULL,
        "TransactionDate" timestamp with time zone NOT NULL,
        "TransactionType" integer NOT NULL,
        "DebitAmount" numeric NOT NULL,
        "CreditAmount" numeric NOT NULL,
        "BalanceAfter" numeric NOT NULL,
        "ReferenceId" uuid,
        "ReferenceNumber" text,
        "IsOpeningBalance" boolean NOT NULL,
        "Notes" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CustomerTransactions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CustomerTransactions_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CustomsEvidenceVersions" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CustomsEvidenceId" uuid NOT NULL,
        "VersionNumber" integer NOT NULL,
        "HsCode" character varying(32) NOT NULL,
        "CommodityDescription" character varying(512) NOT NULL,
        "CountryOfOriginIso2" character(2) NOT NULL,
        "CountryOfManufactureIso2" character(2),
        "DeclaredValue" numeric(18,4) NOT NULL,
        "Currency" character(3) NOT NULL,
        "NetWeight" numeric(18,6) NOT NULL,
        "WeightUnit" character varying(16) NOT NULL,
        "QuantityUnit" character varying(32) NOT NULL,
        "DangerousGoods" boolean,
        "ExportClassification" character varying(128),
        "ValueSource" character varying(128) NOT NULL,
        "SourceEvidenceJson" jsonb NOT NULL,
        "EvidenceFingerprintSha256" character(64) NOT NULL,
        "AdmittedAtUtc" timestamp with time zone NOT NULL,
        "AdmittedBy" character varying(128) NOT NULL,
        "CorrelationId" character varying(128) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CustomsEvidenceVersions" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CustomsEvidenceVersion_Commodity" CHECK (char_length(btrim("CommodityDescription")) > 0),
        CONSTRAINT "CK_CustomsEvidenceVersion_Country" CHECK (char_length("CountryOfOriginIso2") = 2),
        CONSTRAINT "CK_CustomsEvidenceVersion_Fingerprint" CHECK (char_length("EvidenceFingerprintSha256") = 64),
        CONSTRAINT "CK_CustomsEvidenceVersion_HsCode" CHECK (char_length(btrim("HsCode")) > 0),
        CONSTRAINT "CK_CustomsEvidenceVersion_NetWeight" CHECK ("NetWeight" > 0),
        CONSTRAINT "CK_CustomsEvidenceVersion_Value" CHECK ("DeclaredValue" > 0),
        CONSTRAINT "CK_CustomsEvidenceVersion_Version" CHECK ("VersionNumber" > 0),
        CONSTRAINT "FK_CustomsEvidenceVersions_CustomsEvidence_CustomsEvidenceId" FOREIGN KEY ("CustomsEvidenceId") REFERENCES "CustomsEvidence" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "FinancialTransactions" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "AccountId" uuid NOT NULL,
        "Amount" numeric NOT NULL,
        "TransactionType" integer NOT NULL,
        "Date" timestamp with time zone NOT NULL,
        "Description" text NOT NULL,
        "RelatedEntityId" uuid,
        "RelatedEntityType" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_FinancialTransactions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_FinancialTransactions_FinancialAccounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES "FinancialAccounts" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CarrierGeographicMappingReleases" (
        "Id" uuid NOT NULL,
        "CarrierProviderId" uuid NOT NULL,
        "ReleaseNumber" bigint NOT NULL,
        "ReleaseKey" character varying(96) NOT NULL,
        "Status" character varying(24) NOT NULL,
        "Environment" character varying(24) NOT NULL,
        "AccountScopeKind" character varying(24) NOT NULL,
        "AccountScopeId" uuid,
        "TenantId" uuid,
        "CatalogReleaseId" uuid NOT NULL,
        "SourceSummary" character varying(512) NOT NULL,
        "ChecksumSha256" character(64) NOT NULL,
        "EffectiveFromUtc" timestamp with time zone NOT NULL,
        "EffectiveToUtc" timestamp with time zone,
        "FreshnessExpiresAtUtc" timestamp with time zone,
        "CreatedByUserId" uuid NOT NULL,
        "PublishedAtUtc" timestamp with time zone,
        "PublishedByUserId" uuid,
        "SupersedesReleaseId" uuid,
        "RollbackOfReleaseId" uuid,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_CarrierGeographicMappingReleases" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CarrierGeoRelease_Env" CHECK ("Environment" IN ('Production','Sandbox','Test')),
        CONSTRAINT "CK_CarrierGeoRelease_Scope" CHECK ("AccountScopeKind" IN ('Global','ProviderAccount','TenantProviderAccount')),
        CONSTRAINT "CK_CarrierGeoRelease_Status" CHECK ("Status" IN ('Draft','Validated','Published','Superseded','RolledBack','Rejected')),
        CONSTRAINT "CK_CarrierGeoRelease_Window" CHECK ("EffectiveToUtc" IS NULL OR "EffectiveToUtc" > "EffectiveFromUtc"),
        CONSTRAINT "FK_CarrierGeoRelease_RollbackOf" FOREIGN KEY ("RollbackOfReleaseId") REFERENCES "CarrierGeographicMappingReleases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CarrierGeoRelease_Supersedes" FOREIGN KEY ("SupersedesReleaseId") REFERENCES "CarrierGeographicMappingReleases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CarrierGeographicMappingReleases_CarrierProviders_CarrierPr~" FOREIGN KEY ("CarrierProviderId") REFERENCES "CarrierProviders" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CarrierGeographicMappingReleases_GeographicCatalogReleases_~" FOREIGN KEY ("CatalogReleaseId") REFERENCES "GeographicCatalogReleases" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "GeographicCatalogPublications" (
        "Id" uuid NOT NULL,
        "CatalogReleaseId" uuid NOT NULL,
        "PublicationKind" character varying(24) NOT NULL,
        "PublishedAtUtc" timestamp with time zone NOT NULL,
        "PublishedByUserId" uuid NOT NULL,
        "ActivatedAtUtc" timestamp with time zone,
        "SupersededAtUtc" timestamp with time zone,
        "Reason" character varying(512) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_GeographicCatalogPublications" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_GeoCatalogPublication_Kind" CHECK ("PublicationKind" IN ('Initial','Publish','Rollback','Supersede')),
        CONSTRAINT "FK_GeographicCatalogPublications_GeographicCatalogReleases_Cat~" FOREIGN KEY ("CatalogReleaseId") REFERENCES "GeographicCatalogReleases" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "GeographicPlaces" (
        "Id" uuid NOT NULL,
        "StableKey" character varying(96) NOT NULL,
        "PlaceKind" character varying(24) NOT NULL,
        "CountryPlaceId" uuid,
        "LevelCode" character varying(64),
        "CanonicalCode" character varying(96) NOT NULL,
        "ExternalStandard" character varying(64),
        "ExternalStandardCode" character varying(96),
        "Status" character varying(24) NOT NULL,
        "SuccessorPlaceId" uuid,
        "ValidFromUtc" timestamp with time zone NOT NULL,
        "ValidToUtc" timestamp with time zone,
        "IntroducedCatalogReleaseId" uuid NOT NULL,
        "RetiredCatalogReleaseId" uuid,
        "CentroidLatitude" numeric(9,6),
        "CentroidLongitude" numeric(9,6),
        "TimeZoneId" character varying(96),
        "Source" character varying(128) NOT NULL,
        "CreatedByUserId" uuid NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedByUserId" uuid NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_GeographicPlaces" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_GeoPlace_CountryParent" CHECK (("PlaceKind" = 'Country' AND "CountryPlaceId" IS NULL) OR ("PlaceKind" <> 'Country' AND "CountryPlaceId" IS NOT NULL)),
        CONSTRAINT "CK_GeoPlace_Kind" CHECK ("PlaceKind" IN ('Country','AdministrativeArea','Locality','District','Zone')),
        CONSTRAINT "CK_GeoPlace_Latitude" CHECK ("CentroidLatitude" IS NULL OR ("CentroidLatitude" >= -90 AND "CentroidLatitude" <= 90)),
        CONSTRAINT "CK_GeoPlace_Longitude" CHECK ("CentroidLongitude" IS NULL OR ("CentroidLongitude" >= -180 AND "CentroidLongitude" <= 180)),
        CONSTRAINT "CK_GeoPlace_Status" CHECK ("Status" IN ('Draft','Active','Deprecated','Merged','Retired','Rejected')),
        CONSTRAINT "CK_GeoPlace_Successor" CHECK ("SuccessorPlaceId" IS NULL OR "SuccessorPlaceId" <> "Id"),
        CONSTRAINT "CK_GeoPlace_ValidWindow" CHECK ("ValidToUtc" IS NULL OR "ValidToUtc" > "ValidFromUtc"),
        CONSTRAINT "FK_GeographicPlaces_GeographicCatalogReleases_IntroducedCatalo~" FOREIGN KEY ("IntroducedCatalogReleaseId") REFERENCES "GeographicCatalogReleases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_GeographicPlaces_GeographicCatalogReleases_RetiredCatalogRe~" FOREIGN KEY ("RetiredCatalogReleaseId") REFERENCES "GeographicCatalogReleases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_GeographicPlaces_GeographicPlaces_CountryPlaceId" FOREIGN KEY ("CountryPlaceId") REFERENCES "GeographicPlaces" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_GeographicPlaces_GeographicPlaces_SuccessorPlaceId" FOREIGN KEY ("SuccessorPlaceId") REFERENCES "GeographicPlaces" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "PaymentMethods" (
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "TreasuryAccounts" (
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
        CONSTRAINT "FK_TreasuryAccounts_LedgerAccounts_TenantId_AccountId" FOREIGN KEY ("TenantId", "AccountId") REFERENCES "LedgerAccounts" ("TenantId", "Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceAccountingAudit" (
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "LedgerLines" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "JournalEntryId" uuid NOT NULL,
        "AccountId" uuid NOT NULL,
        "DebitAmount" numeric(18,4) NOT NULL,
        "CreditAmount" numeric(18,4) NOT NULL,
        "BalanceAfter" numeric(18,4) NOT NULL,
        "LineDescription" character varying(200),
        "LineNumber" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_LedgerLines" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_LedgerLines_PositiveAmounts" CHECK ("DebitAmount" >= 0 AND "CreditAmount" >= 0),
        CONSTRAINT "CK_LedgerLines_SingleSide" CHECK (("DebitAmount" > 0 AND "CreditAmount" = 0) OR ("DebitAmount" = 0 AND "CreditAmount" > 0)),
        CONSTRAINT "FK_LedgerLines_LedgerAccounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES "LedgerAccounts" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_LedgerLines_LedgerJournalEntries_JournalEntryId" FOREIGN KEY ("JournalEntryId") REFERENCES "LedgerJournalEntries" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "MarketplaceFooterLinks" (
        "Id" uuid NOT NULL,
        "SectionId" uuid NOT NULL,
        "Label" character varying(160) NOT NULL,
        "LabelJson" jsonb,
        "LinkType" character varying(20) NOT NULL,
        "Url" character varying(1000),
        "CmsPageSlug" character varying(160),
        "OpenInNewTab" boolean NOT NULL,
        "SortOrder" integer NOT NULL,
        "IsActive" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_MarketplaceFooterLinks" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_MarketplaceFooterLinks_LinkType" CHECK ("LinkType" IN ('Internal', 'External', 'Cms')),
        CONSTRAINT "CK_MarketplaceFooterLinks_Target" CHECK (("LinkType" = 'Cms' AND "CmsPageSlug" IS NOT NULL AND LENGTH(TRIM("CmsPageSlug")) > 0) OR ("LinkType" <> 'Cms' AND "Url" IS NOT NULL AND LENGTH(TRIM("Url")) > 0)),
        CONSTRAINT "FK_MarketplaceFooterLinks_MarketplaceFooterSections_SectionId" FOREIGN KEY ("SectionId") REFERENCES "MarketplaceFooterSections" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "ProviderTrustAggregates" (
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
        "MarketingReviewCount" integer NOT NULL,
        "PosVerifiedCount" integer NOT NULL,
        "TotalVerifiedRevenue" numeric NOT NULL,
        "TotalVerifiedOrders" integer NOT NULL,
        "MarketingWeightedScore" numeric NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        "TenantId" uuid NOT NULL,
        CONSTRAINT "PK_ProviderTrustAggregates" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_ProviderTrustAggregates_MarketplaceProfiles_ProviderProfile~" FOREIGN KEY ("ProviderProfileId") REFERENCES "MarketplaceProfiles" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "Services" (
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "OnlineCustomerAuthProviders" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "OnlineCustomerId" uuid NOT NULL,
        "Provider" character varying(50) NOT NULL,
        "ProviderUserId" character varying(256) NOT NULL,
        "ProviderEmail" character varying(256) NOT NULL,
        "EmailVerifiedByProvider" boolean NOT NULL,
        "DisplayName" character varying(256),
        "PictureUrl" character varying(2048),
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "LastUsedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_OnlineCustomerAuthProviders" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_OnlineCustomerAuthProviders_OnlineCustomers_OnlineCustomerId" FOREIGN KEY ("OnlineCustomerId") REFERENCES "OnlineCustomers" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "OnlineCustomerEmailVerifications" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "OnlineCustomerId" uuid NOT NULL,
        "TokenHash" character varying(128) NOT NULL,
        "ExpiresAtUtc" timestamp with time zone NOT NULL,
        "IsUsed" boolean NOT NULL DEFAULT FALSE,
        "UsedAtUtc" timestamp with time zone,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_OnlineCustomerEmailVerifications" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_OnlineCustomerEmailVerifications_OnlineCustomers_OnlineCust~" FOREIGN KEY ("OnlineCustomerId") REFERENCES "OnlineCustomers" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "OnlineCustomerPasswordResetTokens" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "OnlineCustomerId" uuid NOT NULL,
        "TokenHash" character varying(128) NOT NULL,
        "ExpiresAtUtc" timestamp with time zone NOT NULL,
        "IsUsed" boolean NOT NULL DEFAULT FALSE,
        "UsedAtUtc" timestamp with time zone,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedByIp" character varying(64) NOT NULL,
        CONSTRAINT "PK_OnlineCustomerPasswordResetTokens" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_OnlineCustomerPasswordResetTokens_OnlineCustomers_OnlineCus~" FOREIGN KEY ("OnlineCustomerId") REFERENCES "OnlineCustomers" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "OnlineCustomerRefreshTokens" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "OnlineCustomerId" uuid NOT NULL,
        "TokenHash" character varying(128) NOT NULL,
        "ExpiresAtUtc" timestamp with time zone NOT NULL,
        "CurrentAccessTokenJti" character varying(128),
        "CurrentAccessTokenExpiresAtUtc" timestamp with time zone,
        "IsRevoked" boolean NOT NULL DEFAULT FALSE,
        "IsAccessTokenBlacklisted" boolean NOT NULL DEFAULT FALSE,
        "RevokedAtUtc" timestamp with time zone,
        "RevokedReason" character varying(256),
        "ReplacedByTokenId" uuid,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedByIp" character varying(45) NOT NULL,
        "CreatedByUserAgent" character varying(512),
        CONSTRAINT "PK_OnlineCustomerRefreshTokens" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_OnlineCustomerRefreshTokens_OnlineCustomers_OnlineCustomerId" FOREIGN KEY ("OnlineCustomerId") REFERENCES "OnlineCustomers" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "RevokedStorefrontAccessTokens" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "OnlineCustomerId" uuid,
        "Jti" character varying(128) NOT NULL,
        "RevokedAtUtc" timestamp with time zone NOT NULL,
        "ExpiresAtUtc" timestamp with time zone NOT NULL,
        "Reason" character varying(256),
        CONSTRAINT "PK_RevokedStorefrontAccessTokens" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_RevokedStorefrontAccessTokens_OnlineCustomers_OnlineCustome~" FOREIGN KEY ("OnlineCustomerId") REFERENCES "OnlineCustomers" ("Id") ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "OutboxMessage" (
        "SequenceNumber" bigint GENERATED BY DEFAULT AS IDENTITY,
        "EnqueueTime" timestamp with time zone,
        "SentTime" timestamp with time zone NOT NULL,
        "Headers" text,
        "Properties" text,
        "InboxMessageId" uuid,
        "InboxConsumerId" uuid,
        "OutboxId" uuid,
        "MessageId" uuid NOT NULL,
        "ContentType" character varying(256) NOT NULL,
        "MessageType" text NOT NULL,
        "Body" text NOT NULL,
        "ConversationId" uuid,
        "CorrelationId" uuid,
        "InitiatorId" uuid,
        "RequestId" uuid,
        "SourceAddress" character varying(256),
        "DestinationAddress" character varying(256),
        "ResponseAddress" character varying(256),
        "FaultAddress" character varying(256),
        "ExpirationTime" timestamp with time zone,
        CONSTRAINT "PK_OutboxMessage" PRIMARY KEY ("SequenceNumber"),
        CONSTRAINT "FK_OutboxMessage_InboxState_InboxMessageId_InboxConsumerId" FOREIGN KEY ("InboxMessageId", "InboxConsumerId") REFERENCES "InboxState" ("MessageId", "ConsumerId"),
        CONSTRAINT "FK_OutboxMessage_OutboxState_OutboxId" FOREIGN KEY ("OutboxId") REFERENCES "OutboxState" ("OutboxId")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "ShipmentCustomsSnapshotLines" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ShipmentCustomsSnapshotId" uuid NOT NULL,
        "ShipmentId" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "OrderItemId" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "ProductVariantId" uuid,
        "CustomsEvidenceId" uuid NOT NULL,
        "CustomsEvidenceVersionId" uuid NOT NULL,
        "EvidenceVersionNumber" integer NOT NULL,
        "HsCode" character varying(32) NOT NULL,
        "CommodityDescription" character varying(512) NOT NULL,
        "CountryOfOriginIso2" character(2) NOT NULL,
        "CountryOfManufactureIso2" character(2),
        "DeclaredValue" numeric(18,4) NOT NULL,
        "Currency" character(3) NOT NULL,
        "Quantity" numeric(18,6) NOT NULL,
        "QuantityUnit" character varying(32) NOT NULL,
        "NetWeight" numeric(18,6) NOT NULL,
        "NetWeightUnit" character varying(16) NOT NULL,
        "DangerousGoods" boolean,
        "ExportClassification" character varying(128),
        "SourceEvidenceFingerprintSha256" character(64) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_ShipmentCustomsSnapshotLines" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_ShipmentCustomsSnapshotLine_Country" CHECK (char_length("CountryOfOriginIso2") = 2),
        CONSTRAINT "CK_ShipmentCustomsSnapshotLine_NetWeight" CHECK ("NetWeight" > 0),
        CONSTRAINT "CK_ShipmentCustomsSnapshotLine_Quantity" CHECK ("Quantity" > 0),
        CONSTRAINT "CK_ShipmentCustomsSnapshotLine_Value" CHECK ("DeclaredValue" > 0),
        CONSTRAINT "FK_ShipmentCustomsSnapshotLines_ShipmentCustomsSnapshots_Shipm~" FOREIGN KEY ("ShipmentCustomsSnapshotId") REFERENCES "ShipmentCustomsSnapshots" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "StorefrontFooterLinks" (
        "Id" uuid NOT NULL,
        "SectionId" uuid NOT NULL,
        "Label" character varying(160) NOT NULL,
        "LabelAr" character varying(160),
        "LinkType" character varying(20) NOT NULL,
        "Url" character varying(1000),
        "CmsPageSlug" character varying(160),
        "OpenInNewTab" boolean NOT NULL,
        "SortOrder" integer NOT NULL,
        "IsActive" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_StorefrontFooterLinks" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_StorefrontFooterLinks_LinkType" CHECK ("LinkType" IN ('Internal', 'External', 'Cms')),
        CONSTRAINT "CK_StorefrontFooterLinks_Target" CHECK (("LinkType" = 'Cms' AND "CmsPageSlug" IS NOT NULL AND LENGTH(TRIM("CmsPageSlug")) > 0) OR ("LinkType" <> 'Cms' AND "Url" IS NOT NULL AND LENGTH(TRIM("Url")) > 0)),
        CONSTRAINT "FK_StorefrontFooterLinks_StorefrontFooterSections_SectionId" FOREIGN KEY ("SectionId") REFERENCES "StorefrontFooterSections" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "SupplierTransactions" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "SupplierId" uuid NOT NULL,
        "TransactionDate" timestamp with time zone NOT NULL,
        "TransactionType" integer NOT NULL,
        "DebitAmount" numeric NOT NULL,
        "CreditAmount" numeric NOT NULL,
        "BalanceAfter" numeric NOT NULL,
        "ReferenceId" uuid,
        "ReferenceNumber" text,
        "IsOpeningBalance" boolean NOT NULL,
        "Notes" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_SupplierTransactions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SupplierTransactions_Suppliers_SupplierId" FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "AccountLedgers" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "PartyType" integer NOT NULL,
        "PartyId" uuid NOT NULL,
        "TransactionDate" timestamp with time zone NOT NULL,
        "TransactionType" integer NOT NULL,
        "DocumentNumber" text NOT NULL,
        "DebitAmount" numeric NOT NULL,
        "CreditAmount" numeric NOT NULL,
        "RunningBalance" numeric NOT NULL,
        "ReferenceId" uuid,
        "Notes" text,
        "UserId" uuid,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_AccountLedgers" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AccountLedgers_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "EmailVerificationTokens" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "TokenHash" character varying(128) NOT NULL,
        "ExpiresAtUtc" timestamp with time zone NOT NULL,
        "UsedAtUtc" timestamp with time zone,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_EmailVerificationTokens" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_EmailVerificationTokens_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "TreasuryTransactions" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "TransactionNumber" text NOT NULL,
        "TransactionDate" timestamp with time zone NOT NULL,
        "TransactionType" integer NOT NULL,
        "PaymentMethod" integer NOT NULL,
        "TargetAccountId" uuid,
        "TargetAccountName" text,
        "Amount" numeric(18,2) NOT NULL,
        "BalanceAfter" numeric(18,2) NOT NULL,
        "ReferenceNumber" text,
        "Notes" text,
        "CreatedByUserId" uuid,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_TreasuryTransactions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_TreasuryTransactions_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "VisaTransactions" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "TransactionDate" timestamp with time zone NOT NULL,
        "TransactionType" integer NOT NULL,
        "Amount" numeric(18,2) NOT NULL,
        "IsDebit" boolean NOT NULL,
        "BalanceAfter" numeric(18,2) NOT NULL,
        "ReferenceNumber" text,
        "Description" text,
        "Notes" text,
        "SourceEntityId" uuid,
        "SourceEntityType" text,
        "CreatedByUserId" uuid,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_VisaTransactions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_VisaTransactions_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "Cashboxes" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "WarehouseId" uuid,
        "CurrentBalance" numeric(18,2) NOT NULL,
        "IsActive" boolean NOT NULL,
        "OpenedAt" timestamp with time zone,
        "OpenedByUserId" uuid,
        "OpeningBalance" numeric(18,2) NOT NULL,
        "ExpectedBalance" numeric(18,2) NOT NULL,
        "Description" character varying(500),
        "OpeningBalanceAmount" numeric,
        "OpeningBalanceDate" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_Cashboxes" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Cashboxes_Users_OpenedByUserId" FOREIGN KEY ("OpenedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_Cashboxes_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "Orders" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "OrderNumber" text NOT NULL,
        "CustomerId" uuid,
        "OnlineCustomerId" uuid,
        "SessionId" character varying(100),
        "CartId" uuid,
        "GuestEmail" text,
        "GuestPhone" text,
        "GuestName" text,
        "IpAddress" character varying(45),
        "IsGuestCheckout" boolean NOT NULL,
        "AffiliateAccountId" uuid,
        "AffiliateReferralCode" text,
        "PromotionLinkId" uuid,
        "Status" integer NOT NULL,
        "CheckoutState" integer NOT NULL,
        "PaymentStatus" integer NOT NULL,
        "ProjectionMutationVersion" bigint NOT NULL,
        "FulfillmentStatus" integer NOT NULL,
        "FulfillmentMode" integer NOT NULL,
        "FulfillmentCarrierCode" text,
        "ShippingAddress" text NOT NULL,
        "ShippingCity" text,
        "ShippingState" text,
        "ShippingZipCode" text,
        "ShippingCountry" text,
        "ShippingAddressAdmissionEvidenceId" uuid,
        "ShippingGeographicCatalogReleaseId" uuid,
        "ShippingCountryPlaceId" uuid,
        "ShippingAdministrativeAreaPlaceId" uuid,
        "ShippingLocalityPlaceId" uuid,
        "ShippingDistrictPlaceId" uuid,
        "ShippingZonePlaceId" uuid,
        "ShippingCountryDisplayName" character varying(200),
        "ShippingAdministrativeAreaDisplayName" character varying(200),
        "ShippingLocalityDisplayName" character varying(200),
        "ShippingDistrictDisplayName" character varying(200),
        "ShippingZoneDisplayName" character varying(200),
        "ShippingStreet" character varying(500),
        "ShippingStreet2" character varying(500),
        "ShippingBuilding" character varying(120),
        "ShippingFloor" character varying(64),
        "ShippingApartment" character varying(64),
        "ShippingLandmark" character varying(200),
        "ShippingGeographicSnapshotCreatedAtUtc" timestamp with time zone,
        "ShippingGeographicSnapshotVersion" integer,
        "BillingAddress" text,
        "BillingCity" text,
        "BillingState" text,
        "BillingZipCode" text,
        "BillingCountry" text,
        "BillingAddressAdmissionEvidenceId" uuid,
        "BillingGeographicCatalogReleaseId" uuid,
        "BillingCountryPlaceId" uuid,
        "BillingAdministrativeAreaPlaceId" uuid,
        "BillingLocalityPlaceId" uuid,
        "BillingDistrictPlaceId" uuid,
        "BillingZonePlaceId" uuid,
        "BillingCountryDisplayName" character varying(200),
        "BillingAdministrativeAreaDisplayName" character varying(200),
        "BillingLocalityDisplayName" character varying(200),
        "BillingDistrictDisplayName" character varying(200),
        "BillingZoneDisplayName" character varying(200),
        "BillingStreet" character varying(500),
        "BillingStreet2" character varying(500),
        "BillingBuilding" character varying(120),
        "BillingFloor" character varying(64),
        "BillingApartment" character varying(64),
        "BillingLandmark" character varying(200),
        "BillingGeographicSnapshotCreatedAtUtc" timestamp with time zone,
        "BillingGeographicSnapshotVersion" integer,
        "ShippingMethodId" uuid,
        "ShippingMethodName" text,
        "ShippingCost" numeric(18,2) NOT NULL,
        "EstimatedDeliveryDays" text,
        "TrackingNumber" text,
        "Carrier" text,
        "Subtotal" numeric(18,2) NOT NULL,
        "PromotionDiscountAmount" numeric(18,2) NOT NULL,
        "DiscountAmount" numeric(18,2) NOT NULL,
        "ManualDiscountAmount" numeric(18,2) NOT NULL,
        "TotalDiscountAmount" numeric(18,2) NOT NULL,
        "TaxAmount" numeric(18,2) NOT NULL,
        "Total" numeric(18,2) NOT NULL,
        "Currency" text NOT NULL,
        "PricingSnapshotJson" text,
        "CouponCode" text,
        "CouponId" uuid,
        "AppliedPromotionId" uuid,
        "AppliedPromotionType" text,
        "AppliedPromotionName" text,
        "AppliedPromotionDescription" text,
        "PromotionCalculationFailed" boolean NOT NULL,
        "PromotionWarningMessage" text,
        "PaymentMethod" integer NOT NULL,
        "PaymentMethodCode" text NOT NULL,
        "PaymentMethodName" text,
        "PaymentFee" numeric(18,2) NOT NULL,
        "IsPaid" boolean NOT NULL,
        "PaymentReference" text,
        "PaymentGateway" text,
        "PaidAt" timestamp with time zone,
        "FinancialPostingStatus" integer NOT NULL,
        "FinancialPostingPendingAtUtc" timestamp with time zone,
        "FinancialPostedAtUtc" timestamp with time zone,
        "FinancialPostingJournalId" uuid,
        "FinancialPostingFailureReason" character varying(1000),
        "CustomerNotes" text,
        "AdminNotes" text,
        "Source" integer NOT NULL,
        "SourceOrderId" text,
        "ShippedAt" timestamp with time zone,
        "DeliveredAt" timestamp with time zone,
        "CancelledAt" timestamp with time zone,
        "CancellationReason" text,
        "WarehouseId" uuid,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_Orders" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_Orders_TenantId_Id" UNIQUE ("TenantId", "Id"),
        CONSTRAINT "CK_Orders_CustomerAuthorityBoundary" CHECK (("CustomerId" IS NULL OR "OnlineCustomerId" IS NULL) AND ("Source" <> 0 OR "CustomerId" IS NULL)),
        CONSTRAINT "CK_Orders_FinancialPostingStatus_Valid" CHECK ("FinancialPostingStatus" IN (0, 1, 2, 3) AND ("FinancialPostingStatus" <> 1 OR "FinancialPostingPendingAtUtc" IS NOT NULL) AND ("FinancialPostingStatus" <> 2 OR ("FinancialPostedAtUtc" IS NOT NULL AND "FinancialPostingJournalId" IS NOT NULL)) AND ("FinancialPostingStatus" <> 3 OR "FinancialPostingFailureReason" IS NOT NULL)),
        CONSTRAINT "FK_Orders_Coupons_CouponId" FOREIGN KEY ("CouponId") REFERENCES "Coupons" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_Orders_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_Orders_OnlineCustomers_OnlineCustomerId" FOREIGN KEY ("OnlineCustomerId") REFERENCES "OnlineCustomers" ("Id"),
        CONSTRAINT "FK_Orders_Promotions_AppliedPromotionId" FOREIGN KEY ("AppliedPromotionId") REFERENCES "Promotions" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_Orders_ShippingMethods_ShippingMethodId" FOREIGN KEY ("ShippingMethodId") REFERENCES "ShippingMethods" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_Orders_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "PurchaseInvoices" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "InvoiceNumber" text NOT NULL,
        "InvoiceDate" timestamp with time zone NOT NULL,
        "DueDate" timestamp with time zone,
        "SupplierId" uuid NOT NULL,
        "WarehouseId" uuid NOT NULL,
        "Status" integer NOT NULL,
        "AccountingMutationVersion" integer NOT NULL,
        "Subtotal" numeric NOT NULL,
        "DiscountAmount" numeric NOT NULL,
        "DiscountPercentage" numeric NOT NULL,
        "TaxAmount" numeric NOT NULL,
        "TotalAmount" numeric NOT NULL,
        "PaidAmount" numeric NOT NULL,
        "PaymentMethod" integer,
        "Notes" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_PurchaseInvoices" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_PurchaseInvoices_TenantId_Id" UNIQUE ("TenantId", "Id"),
        CONSTRAINT "FK_PurchaseInvoices_Suppliers_SupplierId" FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_PurchaseInvoices_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "SalesInvoices" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "InvoiceNumber" text NOT NULL,
        "InvoiceDate" timestamp with time zone NOT NULL,
        "CustomerId" uuid,
        "WarehouseId" uuid NOT NULL,
        "Status" integer NOT NULL,
        "Subtotal" numeric NOT NULL,
        "DiscountAmount" numeric NOT NULL,
        "DiscountPercentage" numeric NOT NULL,
        "TaxAmount" numeric NOT NULL,
        "TotalAmount" numeric NOT NULL,
        "PaidAmount" numeric NOT NULL,
        "PaymentMethod" integer,
        "CashAmount" numeric,
        "CardAmount" numeric,
        "Notes" text,
        "SalesRepresentativeId" uuid,
        "AccountingMutationVersion" integer NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_SalesInvoices" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SalesInvoices_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id"),
        CONSTRAINT "FK_SalesInvoices_SalesRepresentatives_SalesRepresentativeId" FOREIGN KEY ("SalesRepresentativeId") REFERENCES "SalesRepresentatives" ("Id"),
        CONSTRAINT "FK_SalesInvoices_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "StockTransfers" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "TransferNumber" text NOT NULL,
        "TransferDate" timestamp with time zone NOT NULL,
        "FromWarehouseId" uuid NOT NULL,
        "ToWarehouseId" uuid NOT NULL,
        "Status" integer NOT NULL,
        "Notes" text,
        "RequestedById" uuid,
        "ApprovedById" uuid,
        "ApprovedAt" timestamp with time zone,
        "ApprovalNotes" text,
        "CompletedById" uuid,
        "CompletedAt" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_StockTransfers" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_StockTransfers_Users_ApprovedById" FOREIGN KEY ("ApprovedById") REFERENCES "Users" ("Id"),
        CONSTRAINT "FK_StockTransfers_Users_CompletedById" FOREIGN KEY ("CompletedById") REFERENCES "Users" ("Id"),
        CONSTRAINT "FK_StockTransfers_Users_RequestedById" FOREIGN KEY ("RequestedById") REFERENCES "Users" ("Id"),
        CONSTRAINT "FK_StockTransfers_Warehouses_FromWarehouseId" FOREIGN KEY ("FromWarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_StockTransfers_Warehouses_ToWarehouseId" FOREIGN KEY ("ToWarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "AffiliateEmailVerificationTokens" (
        "Id" uuid NOT NULL,
        "AffiliateUserId" uuid NOT NULL,
        "TokenHash" character varying(128) NOT NULL,
        "ExpiresAtUtc" timestamp with time zone NOT NULL,
        "ConsumedAtUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_AffiliateEmailVerificationTokens" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AffiliateEmailVerificationTokens_AffiliateUsers_AffiliateUs~" FOREIGN KEY ("AffiliateUserId") REFERENCES "AffiliateUsers" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "AffiliatePasswordResetTokens" (
        "Id" uuid NOT NULL,
        "AffiliateUserId" uuid NOT NULL,
        "TokenHash" character varying(128) NOT NULL,
        "ExpiresAtUtc" timestamp with time zone NOT NULL,
        "ConsumedAtUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_AffiliatePasswordResetTokens" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AffiliatePasswordResetTokens_AffiliateUsers_AffiliateUserId" FOREIGN KEY ("AffiliateUserId") REFERENCES "AffiliateUsers" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "AffiliateRefreshTokenReplayEntries" (
        "Id" uuid NOT NULL,
        "AffiliateUserId" uuid NOT NULL,
        "TokenHash" character varying(128) NOT NULL,
        "RotatedAtUtc" timestamp with time zone NOT NULL,
        "ExpiresAtUtc" timestamp with time zone NOT NULL,
        "ReplayedAtUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_AffiliateRefreshTokenReplayEntries" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AffiliateRefreshTokenReplayEntries_AffiliateUsers_Affiliate~" FOREIGN KEY ("AffiliateUserId") REFERENCES "AffiliateUsers" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "AffiliateProductPolicies" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "CommissionRate" numeric(8,6) NOT NULL,
        "IsEnabled" boolean NOT NULL,
        "MaxCommissionPerItem" numeric(18,4),
        "TemplateType" text NOT NULL,
        "AffiliateCategoryId" uuid,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_AffiliateProductPolicies" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AffiliateProductPolicies_EnabledRequiresCategory" CHECK (NOT ("IsEnabled" = true AND "AffiliateCategoryId" IS NULL)),
        CONSTRAINT "CK_AffiliateProductPolicies_MaxCap" CHECK ("MaxCommissionPerItem" IS NULL OR "MaxCommissionPerItem" > 0),
        CONSTRAINT "CK_AffiliateProductPolicies_Rate" CHECK ("CommissionRate" >= 0.0001 AND "CommissionRate" <= 0.50),
        CONSTRAINT "FK_AffiliateProductPolicies_AffiliateCategories_AffiliateCateg~" FOREIGN KEY ("AffiliateCategoryId") REFERENCES "AffiliateCategories" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AffiliateProductPolicies_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "AffiliatePromotionLinks" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "AffiliateUserId" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "ReferralCode" character varying(16) NOT NULL,
        "AffiliateAccountId" uuid,
        "IsActive" boolean NOT NULL DEFAULT TRUE,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_AffiliatePromotionLinks" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AffiliatePromotionLinks_AffiliateUsers_AffiliateUserId" FOREIGN KEY ("AffiliateUserId") REFERENCES "AffiliateUsers" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AffiliatePromotionLinks_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "ProductMedia" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "MediaType" character varying(20) NOT NULL,
        "Url" text NOT NULL,
        "Provider" character varying(20),
        "Position" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_ProductMedia" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_ProductMedia_TenantId_ProductId_Id" UNIQUE ("TenantId", "ProductId", "Id"),
        CONSTRAINT "FK_ProductMedia_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "ProductPixelOverrides" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "IsEnabled" boolean NOT NULL,
        "Strategy" integer NOT NULL,
        "Value" numeric(18,4) NOT NULL,
        "ApplyToMeta" boolean NOT NULL,
        "ApplyToGoogle" boolean NOT NULL,
        "ApplyToTikTok" boolean NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedBy" uuid NOT NULL,
        CONSTRAINT "PK_ProductPixelOverrides" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_ProductPixelOverrides_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "ProductReviews" (
        "Id" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Rating" integer NOT NULL,
        "Title" character varying(200),
        "Comment" character varying(4000) NOT NULL,
        "IsVerifiedPurchase" boolean NOT NULL,
        "Status" integer NOT NULL DEFAULT 0,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_ProductReviews" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_ProductReviews_Rating_Range" CHECK ("Rating" >= 1 AND "Rating" <= 5),
        CONSTRAINT "FK_ProductReviews_CommerceHubCustomers_UserId" FOREIGN KEY ("UserId") REFERENCES "CommerceHubCustomers" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_ProductReviews_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "ProductUnits" (
        "Id" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "UnitId" uuid NOT NULL,
        "ConversionFactor" numeric NOT NULL,
        "PurchasePrice" numeric,
        "SellingPrice" numeric,
        "Barcode" text,
        "IsDefault" boolean NOT NULL,
        "IsBaseUnit" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_ProductUnits" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_ProductUnits_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_ProductUnits_Units_UnitId" FOREIGN KEY ("UnitId") REFERENCES "Units" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubPayments" (
        "Id" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "Amount" numeric(18,2) NOT NULL,
        "Currency" character varying(10) NOT NULL,
        "Status" integer NOT NULL,
        "Provider" integer NOT NULL,
        "ProviderReference" character varying(100) NOT NULL,
        "ProviderIntentId" character varying(200),
        "ProviderTransactionId" character varying(200),
        "CheckoutUrl" character varying(2000),
        "PaymentToken" text,
        "RefundedAmount" numeric(18,2) NOT NULL,
        "ProvisionalRefundedAmount" numeric(18,2) NOT NULL,
        "FailureReason" character varying(1000),
        "IsProcessed" boolean NOT NULL,
        "NeedsManualCurrencyReview" boolean NOT NULL DEFAULT FALSE,
        "ProcessedAtUtc" timestamp with time zone,
        "PaidAtUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CommerceHubPayments" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CommerceHubPayments_Amount_Positive" CHECK ("Amount" > 0),
        CONSTRAINT "CK_CommerceHubPayments_Currency_EGP" CHECK ("Currency" = 'EGP'),
        CONSTRAINT "CK_CommerceHubPayments_Paid_State_Complete" CHECK ("Status" <> 1 OR ("IsProcessed" = true AND "PaidAtUtc" IS NOT NULL AND "ProviderTransactionId" IS NOT NULL)),
        CONSTRAINT "CK_CommerceHubPayments_ProvisionalRefundedAmount_Range" CHECK ("ProvisionalRefundedAmount" >= 0 AND ROUND("RefundedAmount" + "ProvisionalRefundedAmount", 2) <= ROUND("Amount", 2)),
        CONSTRAINT "CK_CommerceHubPayments_RefundedAmount_Range" CHECK ("RefundedAmount" >= 0 AND "RefundedAmount" <= "Amount"),
        CONSTRAINT "FK_CommerceHubPayments_CommerceHubMarketplaceOrders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "CommerceHubMarketplaceOrders" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubSubOrders" (
        "Id" uuid NOT NULL,
        "MarketplaceOrderId" uuid NOT NULL,
        "OrderNumber" text NOT NULL,
        "StoreId" uuid NOT NULL,
        "StoreName" character varying(200) NOT NULL,
        "Currency" character varying(10) NOT NULL,
        "Subtotal" numeric(18,2) NOT NULL,
        "Shipping" numeric(18,2) NOT NULL,
        "ShippingMethodId" uuid,
        "ShippingMethodName" character varying(200),
        "ShippingSnapshotJson" text,
        "PaymentMethod" integer NOT NULL,
        "PaymentMethodCode" character varying(50) NOT NULL,
        "PaymentMethodName" character varying(100) NOT NULL,
        "PaymentSnapshotJson" text,
        "CommissionSnapshotJson" text,
        "IsEarningCreated" boolean NOT NULL,
        "EarningCreatedAtUtc" timestamp with time zone,
        "PlatformCommissionAmount" numeric(18,2) NOT NULL,
        "FinalPlatformCommissionAmount" numeric NOT NULL,
        "PromoDiscountAmount" numeric NOT NULL,
        "VendorEarningAmount" numeric(18,2) NOT NULL,
        "PlatformCommissionRate" numeric(8,6) NOT NULL,
        "Total" numeric(18,2) NOT NULL,
        "Status" integer NOT NULL,
        "PaymentStatus" integer NOT NULL,
        "PaymentReferenceId" character varying(100),
        "PaidAt" timestamp with time zone,
        "ProjectionMutationVersion" bigint NOT NULL DEFAULT 1,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CommerceHubSubOrders" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CommerceHubSubOrders_Currency_EGP" CHECK ("Currency" = 'EGP'),
        CONSTRAINT "FK_CommerceHubSubOrders_CommerceHubMarketplaceOrders_Marketpla~" FOREIGN KEY ("MarketplaceOrderId") REFERENCES "CommerceHubMarketplaceOrders" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "PromoCodeUsages" (
        "Id" uuid NOT NULL,
        "PromoCodeId" uuid NOT NULL,
        "MarketplaceOrderId" uuid NOT NULL,
        "CustomerId" uuid,
        "PromoCodeSnapshot" character varying(64) NOT NULL,
        "DiscountAmount" numeric(18,2) NOT NULL,
        "EligibleBaseCommissionAmount" numeric(18,2) NOT NULL,
        "CorrelationId" character varying(200),
        "AppliedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_PromoCodeUsages" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_PromoCodeUsages_DiscountAmount" CHECK ("DiscountAmount" >= 0),
        CONSTRAINT "CK_PromoCodeUsages_EligibleBaseCommissionAmount" CHECK ("EligibleBaseCommissionAmount" >= 0),
        CONSTRAINT "FK_PromoCodeUsages_CommerceHubMarketplaceOrders_MarketplaceOrd~" FOREIGN KEY ("MarketplaceOrderId") REFERENCES "CommerceHubMarketplaceOrders" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_PromoCodeUsages_PromoCodes_PromoCodeId" FOREIGN KEY ("PromoCodeId") REFERENCES "PromoCodes" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "AbandonedCarts" (
        "Id" uuid NOT NULL,
        "CartId" uuid NOT NULL,
        "SessionId" character varying(128),
        "CustomerId" uuid,
        "PhoneNumber" character varying(50) NOT NULL,
        "Email" character varying(255),
        "ContactChannel" character varying(32),
        "PhoneVerified" boolean NOT NULL,
        "EmailVerified" boolean NOT NULL,
        "CartSnapshotJson" text NOT NULL,
        "LastActivityAtUtc" timestamp with time zone NOT NULL,
        "AbandonedAtUtc" timestamp with time zone,
        "RecoveryStatus" text NOT NULL,
        "PendingAtUtc" timestamp with time zone,
        "TriggeredAtUtc" timestamp with time zone,
        "RecoveredAtUtc" timestamp with time zone,
        "RecoveredOrderId" uuid,
        "ExpiredAtUtc" timestamp with time zone,
        "RetryCount" integer NOT NULL,
        "LastErrorMessage" text,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "TenantId" uuid NOT NULL,
        CONSTRAINT "PK_AbandonedCarts" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AbandonedCarts_OnlineCustomers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "OnlineCustomers" ("Id"),
        CONSTRAINT "FK_AbandonedCarts_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CheckoutSessions" (
        "Id" uuid NOT NULL,
        "CartId" uuid NOT NULL,
        "RecoveryToken" character varying(64) NOT NULL,
        "SessionId" character varying(100),
        "OnlineCustomerId" uuid,
        "CustomerName" character varying(200),
        "Phone" character varying(20),
        "Email" character varying(255),
        "City" character varying(100),
        "Area" character varying(100),
        "Address" character varying(500),
        "Country" character varying(2),
        "Notes" character varying(500),
        "ItemsSnapshotJson" text NOT NULL,
        "CartTotal" numeric(18,4),
        "Currency" character varying(3),
        "ShippingMethodId" uuid,
        "PaymentMethodCode" character varying(50),
        "WalletType" text,
        "WalletPhoneNumber" text,
        "ProofImageUrl" text,
        "Status" integer NOT NULL,
        "DeviceType" character varying(20),
        "Source" character varying(50),
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "FirstActivityAtUtc" timestamp with time zone,
        "LastActivityAtUtc" timestamp with time zone NOT NULL,
        "AbandonedAtUtc" timestamp with time zone,
        "ConvertedAtUtc" timestamp with time zone,
        "ConvertedOrderId" uuid,
        "ConvertedOrderNumber" character varying(50),
        "InvalidatedAtUtc" timestamp with time zone,
        "TenantId" uuid NOT NULL,
        CONSTRAINT "PK_CheckoutSessions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CheckoutSessions_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubVendorPaymentRequests" (
        "Id" uuid NOT NULL,
        "VendorId" uuid NOT NULL,
        "PaymentMethodId" uuid NOT NULL,
        "Amount" numeric(18,2) NOT NULL,
        "Currency" character varying(10) NOT NULL DEFAULT 'EGP',
        "Status" integer NOT NULL,
        "TransferReference" character varying(200),
        "IdempotencyKey" character varying(200) NOT NULL,
        "MethodNameSnapshot" character varying(120) NOT NULL,
        "DestinationValueSnapshot" character varying(200) NOT NULL,
        "InstructionsSnapshot" text NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "ApprovedAtUtc" timestamp with time zone,
        "ApprovedBy" uuid,
        "RejectedAtUtc" timestamp with time zone,
        "RejectedBy" uuid,
        "ReviewReason" text,
        "LedgerCorrelationId" character varying(200),
        "LedgerIdempotencyKey" character varying(200),
        CONSTRAINT "PK_CommerceHubVendorPaymentRequests" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CommerceHubVendorPaymentRequests_Amount" CHECK ("Amount" > 0),
        CONSTRAINT "CK_CommerceHubVendorPaymentRequests_Approved_TransferReference" CHECK ("Status" <> 1 OR NULLIF(btrim("TransferReference"), '') IS NOT NULL),
        CONSTRAINT "CK_CommerceHubVendorPaymentRequests_Currency_EGP" CHECK ("Currency" = 'EGP'),
        CONSTRAINT "CK_CommerceHubVendorPaymentRequests_Status" CHECK ("Status" IN (0, 1, 2)),
        CONSTRAINT "FK_CommerceHubVendorPaymentRequests_CommerceHubVendorPaymentMe~" FOREIGN KEY ("PaymentMethodId") REFERENCES "CommerceHubVendorPaymentMethods" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CommerceHubVendorPaymentRequests_Tenants_VendorId" FOREIGN KEY ("VendorId") REFERENCES "Tenants" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "SystemSettings" (
        "Id" uuid NOT NULL,
        "TenantId" uuid,
        "Key" text NOT NULL,
        "Value" text NOT NULL,
        "Description" text,
        "Category" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_SystemSettings" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SystemSettings_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "TenantAutomationSettings" (
        "Id" uuid NOT NULL,
        "EnableAbandonedRecovery" boolean NOT NULL,
        "FirstMessageDelayMinutes" integer NOT NULL,
        "SecondMessageDelayMinutes" integer NOT NULL,
        "WorkingHoursFrom" interval NOT NULL,
        "WorkingHoursTo" interval NOT NULL,
        "N8nWebhookApiKey" text,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        "TenantId" uuid NOT NULL,
        CONSTRAINT "PK_TenantAutomationSettings" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_TenantAutomationSettings_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE whatsapp_accounts (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        phone_number character varying(20) NOT NULL,
        display_name character varying(100) NOT NULL,
        status integer NOT NULL,
        enabled boolean NOT NULL DEFAULT FALSE,
        use_for_order_confirmation boolean NOT NULL DEFAULT FALSE,
        use_for_otp_verification boolean NOT NULL DEFAULT FALSE,
        use_for_payment_approval boolean NOT NULL DEFAULT FALSE,
        use_for_abandoned_cart_recovery boolean NOT NULL DEFAULT FALSE,
        last_connected_at_utc timestamp with time zone,
        last_disconnected_at_utc timestamp with time zone,
        status_reason text,
        updated_at_utc timestamp with time zone,
        created_at_utc timestamp with time zone NOT NULL DEFAULT (NOW()),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        tenant_id uuid NOT NULL,
        CONSTRAINT "PK_whatsapp_accounts" PRIMARY KEY (id),
        CONSTRAINT "FK_whatsapp_accounts_Tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES "Tenants" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CarrierGeographicMappingGroups" (
        "Id" uuid NOT NULL,
        "MappingReleaseId" uuid NOT NULL,
        "GroupKey" character varying(128) NOT NULL,
        "CardinalityPolicy" character varying(40) NOT NULL,
        "CanonicalPlaceKind" character varying(24) NOT NULL,
        "ProviderPlaceType" character varying(64) NOT NULL,
        "Reason" character varying(512) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_CarrierGeographicMappingGroups" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CarrierGeoGroup_Cardinality" CHECK ("CardinalityPolicy" IN ('OneToOne','ManyCanonicalToOneProvider','OneCanonicalToManyProvider')),
        CONSTRAINT "FK_CarrierGeographicMappingGroups_CarrierGeographicMappingRele~" FOREIGN KEY ("MappingReleaseId") REFERENCES "CarrierGeographicMappingReleases" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CarrierGeographicMappingPublications" (
        "Id" uuid NOT NULL,
        "MappingReleaseId" uuid NOT NULL,
        "PublicationKind" character varying(24) NOT NULL,
        "PublishedAtUtc" timestamp with time zone NOT NULL,
        "PublishedByUserId" uuid NOT NULL,
        "ActivatedAtUtc" timestamp with time zone,
        "SupersededAtUtc" timestamp with time zone,
        "Reason" character varying(512) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_CarrierGeographicMappingPublications" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CarrierGeoPublication_Kind" CHECK ("PublicationKind" IN ('Initial','Publish','Rollback','Supersede')),
        CONSTRAINT "FK_CarrierGeographicMappingPublications_CarrierGeographicMappi~" FOREIGN KEY ("MappingReleaseId") REFERENCES "CarrierGeographicMappingReleases" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "GeographicAudit" (
        "Id" uuid NOT NULL,
        "TenantId" uuid,
        "ActorKind" character varying(32) NOT NULL,
        "ActorId" uuid,
        "ActorRole" character varying(128) NOT NULL,
        "Action" character varying(128) NOT NULL,
        "AggregateType" character varying(128) NOT NULL,
        "AggregateId" uuid NOT NULL,
        "CatalogReleaseId" uuid,
        "MappingReleaseId" uuid,
        "CountryScopeIso2" character(2),
        "ProviderCode" character varying(64),
        "Environment" character varying(24),
        "AccountScope" character varying(128),
        "Reason" character varying(512) NOT NULL,
        "CorrelationId" character varying(128) NOT NULL,
        "ValidationChecksumSha256" character(64),
        "EvidenceObjectKey" text,
        "OccurredAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_GeographicAudit" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_GeographicAudit_CarrierGeographicMappingReleases_MappingRel~" FOREIGN KEY ("MappingReleaseId") REFERENCES "CarrierGeographicMappingReleases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_GeographicAudit_GeographicCatalogReleases_CatalogReleaseId" FOREIGN KEY ("CatalogReleaseId") REFERENCES "GeographicCatalogReleases" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "AddressAdmissionEvidence" (
        "Id" uuid NOT NULL,
        "TenantId" uuid,
        "OwnerDomain" character varying(64) NOT NULL,
        "OwnerId" uuid,
        "AddressFingerprintSha256" character(64) NOT NULL,
        "Status" character varying(32) NOT NULL,
        "FailureCode" character varying(128),
        "CatalogReleaseId" uuid NOT NULL,
        "CountryPlaceId" uuid,
        "AdministrativeAreaPlaceId" uuid,
        "LocalityPlaceId" uuid,
        "DistrictPlaceId" uuid,
        "ZonePlaceId" uuid,
        "AdmittedAtUtc" timestamp with time zone NOT NULL,
        "NormalizationRuleVersion" character varying(128),
        "RawInputEvidenceObjectKey" text,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_AddressAdmissionEvidence" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AddressAdmissionEvidence_GeographicCatalogReleases_CatalogR~" FOREIGN KEY ("CatalogReleaseId") REFERENCES "GeographicCatalogReleases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AddressAdmissionEvidence_GeographicPlaces_AdministrativeAre~" FOREIGN KEY ("AdministrativeAreaPlaceId") REFERENCES "GeographicPlaces" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AddressAdmissionEvidence_GeographicPlaces_CountryPlaceId" FOREIGN KEY ("CountryPlaceId") REFERENCES "GeographicPlaces" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AddressAdmissionEvidence_GeographicPlaces_DistrictPlaceId" FOREIGN KEY ("DistrictPlaceId") REFERENCES "GeographicPlaces" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AddressAdmissionEvidence_GeographicPlaces_LocalityPlaceId" FOREIGN KEY ("LocalityPlaceId") REFERENCES "GeographicPlaces" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AddressAdmissionEvidence_GeographicPlaces_ZonePlaceId" FOREIGN KEY ("ZonePlaceId") REFERENCES "GeographicPlaces" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "AddressNormalizationRules" (
        "Id" uuid NOT NULL,
        "CountryPlaceId" uuid NOT NULL,
        "LanguageTag" character varying(35),
        "RuleKind" character varying(32) NOT NULL,
        "RuleVersion" bigint NOT NULL,
        "RuleDefinitionJson" text NOT NULL,
        "IsActive" boolean NOT NULL,
        "IntroducedCatalogReleaseId" uuid NOT NULL,
        "RetiredCatalogReleaseId" uuid,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_AddressNormalizationRules" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AddressNormalizationRules_GeographicCatalogReleases_Introdu~" FOREIGN KEY ("IntroducedCatalogReleaseId") REFERENCES "GeographicCatalogReleases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AddressNormalizationRules_GeographicCatalogReleases_Retired~" FOREIGN KEY ("RetiredCatalogReleaseId") REFERENCES "GeographicCatalogReleases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AddressNormalizationRules_GeographicPlaces_CountryPlaceId" FOREIGN KEY ("CountryPlaceId") REFERENCES "GeographicPlaces" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CarrierGeographicOverrides" (
        "Id" uuid NOT NULL,
        "CarrierProviderId" uuid NOT NULL,
        "Environment" character varying(24) NOT NULL,
        "AccountScopeKind" character varying(24) NOT NULL,
        "AccountScopeId" uuid,
        "TenantId" uuid,
        "CanonicalPlaceId" uuid NOT NULL,
        "ProviderPlaceType" character varying(64) NOT NULL,
        "ProviderPlaceId" character varying(256) NOT NULL,
        "ProviderPlaceCode" character varying(256),
        "ProviderDisplayName" text NOT NULL,
        "Status" character varying(24) NOT NULL,
        "EffectiveFromUtc" timestamp with time zone NOT NULL,
        "EffectiveToUtc" timestamp with time zone,
        "Reason" character varying(512) NOT NULL,
        "ApprovedByUserId" uuid,
        "ApprovedAtUtc" timestamp with time zone,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_CarrierGeographicOverrides" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CarrierGeoOverride_Window" CHECK ("EffectiveToUtc" IS NULL OR "EffectiveToUtc" > "EffectiveFromUtc"),
        CONSTRAINT "FK_CarrierGeographicOverrides_CarrierProviders_CarrierProvider~" FOREIGN KEY ("CarrierProviderId") REFERENCES "CarrierProviders" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CarrierGeographicOverrides_GeographicPlaces_CanonicalPlaceId" FOREIGN KEY ("CanonicalPlaceId") REFERENCES "GeographicPlaces" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CarrierGeographicSyncCandidates" (
        "Id" uuid NOT NULL,
        "SyncRunId" uuid NOT NULL,
        "CarrierProviderId" uuid NOT NULL,
        "ProviderPlaceType" character varying(64) NOT NULL,
        "ProviderPlaceId" character varying(256) NOT NULL,
        "ProviderPlaceCode" character varying(256),
        "ProviderDisplayName" text NOT NULL,
        "ProviderNormalizedName" text NOT NULL,
        "ProviderParentPlaceId" character varying(256),
        "CandidateCanonicalPlaceId" uuid,
        "Confidence" smallint NOT NULL,
        "CandidateStatus" character varying(24) NOT NULL,
        "ReviewReason" character varying(512),
        "ReviewedByUserId" uuid,
        "ReviewedAtUtc" timestamp with time zone,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_CarrierGeographicSyncCandidates" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CarrierGeoSyncCandidate_Confidence" CHECK ("Confidence" BETWEEN 0 AND 100),
        CONSTRAINT "FK_CarrierGeographicSyncCandidates_CarrierGeographicSyncRuns_S~" FOREIGN KEY ("SyncRunId") REFERENCES "CarrierGeographicSyncRuns" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CarrierGeographicSyncCandidates_CarrierProviders_CarrierPro~" FOREIGN KEY ("CarrierProviderId") REFERENCES "CarrierProviders" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CarrierGeographicSyncCandidates_GeographicPlaces_CandidateC~" FOREIGN KEY ("CandidateCanonicalPlaceId") REFERENCES "GeographicPlaces" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "GeographicCatalogReleasePlaces" (
        "CatalogReleaseId" uuid NOT NULL,
        "PlaceId" uuid NOT NULL,
        "PlaceStatus" character varying(24) NOT NULL,
        CONSTRAINT "PK_GeographicCatalogReleasePlaces" PRIMARY KEY ("CatalogReleaseId", "PlaceId"),
        CONSTRAINT "FK_GeographicCatalogReleasePlaces_GeographicCatalogReleases_Ca~" FOREIGN KEY ("CatalogReleaseId") REFERENCES "GeographicCatalogReleases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_GeographicCatalogReleasePlaces_GeographicPlaces_PlaceId" FOREIGN KEY ("PlaceId") REFERENCES "GeographicPlaces" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "GeographicCountryProfiles" (
        "Id" uuid NOT NULL,
        "CountryPlaceId" uuid NOT NULL,
        "Iso2" character(2) NOT NULL,
        "Iso3" character(3) NOT NULL,
        "DefaultLanguageTag" character varying(35) NOT NULL,
        "SupportedLanguageTags" text[] NOT NULL,
        "RequiredLevelCodesForDelivery" text[] NOT NULL,
        "RequiredLevelCodesForOperations" text[] NOT NULL,
        "PostalCodePolicy" character varying(24) NOT NULL,
        "PostalCodeRegex" text,
        "RequiresStreetLine1" boolean NOT NULL,
        "AllowsCoordinates" boolean NOT NULL,
        "RequiresCoordinateProvenance" boolean NOT NULL,
        "OfflinePackageEnabled" boolean NOT NULL,
        "IsActive" boolean NOT NULL,
        "IntroducedCatalogReleaseId" uuid NOT NULL,
        "RetiredCatalogReleaseId" uuid,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_GeographicCountryProfiles" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_GeoCountryProfile_Postal" CHECK ("PostalCodePolicy" IN ('NotUsed','Optional','Required','Forbidden')),
        CONSTRAINT "FK_GeographicCountryProfiles_GeographicCatalogReleases_Introdu~" FOREIGN KEY ("IntroducedCatalogReleaseId") REFERENCES "GeographicCatalogReleases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_GeographicCountryProfiles_GeographicCatalogReleases_Retired~" FOREIGN KEY ("RetiredCatalogReleaseId") REFERENCES "GeographicCatalogReleases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_GeographicCountryProfiles_GeographicPlaces_CountryPlaceId" FOREIGN KEY ("CountryPlaceId") REFERENCES "GeographicPlaces" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "GeographicPlaceAliases" (
        "Id" uuid NOT NULL,
        "PlaceId" uuid NOT NULL,
        "CountryPlaceId" uuid NOT NULL,
        "LanguageTag" character varying(35),
        "AliasKind" character varying(24) NOT NULL,
        "Alias" text NOT NULL,
        "NormalizedAlias" text NOT NULL,
        "SearchAlias" text NOT NULL,
        "Confidence" smallint NOT NULL,
        "Source" character varying(128) NOT NULL,
        "IsAmbiguous" boolean NOT NULL,
        "IntroducedCatalogReleaseId" uuid NOT NULL,
        "RetiredCatalogReleaseId" uuid,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_GeographicPlaceAliases" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_GeoPlaceAlias_Confidence" CHECK ("Confidence" BETWEEN 0 AND 100),
        CONSTRAINT "CK_GeoPlaceAlias_Kind" CHECK ("AliasKind" IN ('Spelling','Legacy','Transliteration','Abbreviation','Historic','ProviderLabel','MigrationEvidence')),
        CONSTRAINT "FK_GeographicPlaceAliases_GeographicCatalogReleases_Introduced~" FOREIGN KEY ("IntroducedCatalogReleaseId") REFERENCES "GeographicCatalogReleases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_GeographicPlaceAliases_GeographicCatalogReleases_RetiredCat~" FOREIGN KEY ("RetiredCatalogReleaseId") REFERENCES "GeographicCatalogReleases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_GeographicPlaceAliases_GeographicPlaces_CountryPlaceId" FOREIGN KEY ("CountryPlaceId") REFERENCES "GeographicPlaces" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_GeographicPlaceAliases_GeographicPlaces_PlaceId" FOREIGN KEY ("PlaceId") REFERENCES "GeographicPlaces" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "GeographicPlaceHierarchyEdges" (
        "Id" uuid NOT NULL,
        "ParentPlaceId" uuid NOT NULL,
        "ChildPlaceId" uuid NOT NULL,
        "CountryPlaceId" uuid NOT NULL,
        "Depth" integer NOT NULL,
        "IntroducedCatalogReleaseId" uuid NOT NULL,
        "RetiredCatalogReleaseId" uuid,
        "IsPrimary" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_GeographicPlaceHierarchyEdges" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_GeoHierarchy_Depth" CHECK ("Depth" >= 1),
        CONSTRAINT "CK_GeoHierarchy_NoSelf" CHECK ("ParentPlaceId" <> "ChildPlaceId"),
        CONSTRAINT "FK_GeographicPlaceHierarchyEdges_GeographicCatalogReleases_Int~" FOREIGN KEY ("IntroducedCatalogReleaseId") REFERENCES "GeographicCatalogReleases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_GeographicPlaceHierarchyEdges_GeographicCatalogReleases_Ret~" FOREIGN KEY ("RetiredCatalogReleaseId") REFERENCES "GeographicCatalogReleases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_GeographicPlaceHierarchyEdges_GeographicPlaces_ChildPlaceId" FOREIGN KEY ("ChildPlaceId") REFERENCES "GeographicPlaces" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_GeographicPlaceHierarchyEdges_GeographicPlaces_CountryPlace~" FOREIGN KEY ("CountryPlaceId") REFERENCES "GeographicPlaces" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_GeographicPlaceHierarchyEdges_GeographicPlaces_ParentPlaceId" FOREIGN KEY ("ParentPlaceId") REFERENCES "GeographicPlaces" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "GeographicPlaceLevels" (
        "Id" uuid NOT NULL,
        "CountryPlaceId" uuid NOT NULL,
        "LevelCode" character varying(64) NOT NULL,
        "ParentLevelCode" character varying(64),
        "SortOrder" integer NOT NULL,
        "PlaceKind" character varying(24) NOT NULL,
        "RequiredForDelivery" boolean NOT NULL,
        "RequiredForOperations" boolean NOT NULL,
        "RequiredForTax" boolean NOT NULL,
        "DisplayNameKey" character varying(96) NOT NULL,
        "AllowsChildren" boolean NOT NULL,
        "IsActive" boolean NOT NULL,
        "IntroducedCatalogReleaseId" uuid NOT NULL,
        "RetiredCatalogReleaseId" uuid,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_GeographicPlaceLevels" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_GeoPlaceLevel_Kind" CHECK ("PlaceKind" IN ('AdministrativeArea','Locality','District','Zone')),
        CONSTRAINT "CK_GeoPlaceLevel_Sort" CHECK ("SortOrder" > 0),
        CONSTRAINT "FK_GeographicPlaceLevels_GeographicCatalogReleases_IntroducedC~" FOREIGN KEY ("IntroducedCatalogReleaseId") REFERENCES "GeographicCatalogReleases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_GeographicPlaceLevels_GeographicCatalogReleases_RetiredCata~" FOREIGN KEY ("RetiredCatalogReleaseId") REFERENCES "GeographicCatalogReleases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_GeographicPlaceLevels_GeographicPlaces_CountryPlaceId" FOREIGN KEY ("CountryPlaceId") REFERENCES "GeographicPlaces" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "GeographicPlaceNames" (
        "Id" uuid NOT NULL,
        "PlaceId" uuid NOT NULL,
        "LanguageTag" character varying(35) NOT NULL,
        "ScriptCode" character(4),
        "NameKind" character varying(24) NOT NULL,
        "Name" text NOT NULL,
        "NormalizedName" text NOT NULL,
        "SearchName" text NOT NULL,
        "DisplayPriority" integer NOT NULL,
        "IsPreferred" boolean NOT NULL,
        "ValidFromUtc" timestamp with time zone NOT NULL,
        "ValidToUtc" timestamp with time zone,
        "IntroducedCatalogReleaseId" uuid NOT NULL,
        "RetiredCatalogReleaseId" uuid,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_GeographicPlaceNames" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_GeoPlaceName_Kind" CHECK ("NameKind" IN ('Official','Display','Short','Search','Historic','Transliteration','ProviderLabel')),
        CONSTRAINT "CK_GeoPlaceName_Window" CHECK ("ValidToUtc" IS NULL OR "ValidToUtc" > "ValidFromUtc"),
        CONSTRAINT "FK_GeographicPlaceNames_GeographicCatalogReleases_IntroducedCa~" FOREIGN KEY ("IntroducedCatalogReleaseId") REFERENCES "GeographicCatalogReleases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_GeographicPlaceNames_GeographicCatalogReleases_RetiredCatal~" FOREIGN KEY ("RetiredCatalogReleaseId") REFERENCES "GeographicCatalogReleases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_GeographicPlaceNames_GeographicPlaces_PlaceId" FOREIGN KEY ("PlaceId") REFERENCES "GeographicPlaces" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "PlaceDeprecations" (
        "Id" uuid NOT NULL,
        "PlaceId" uuid NOT NULL,
        "SuccessorPlaceId" uuid,
        "CatalogReleaseId" uuid NOT NULL,
        "DeprecationKind" character varying(32) NOT NULL,
        "Reason" character varying(512) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_PlaceDeprecations" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_PlaceDeprecation_NoSelf" CHECK ("SuccessorPlaceId" IS NULL OR "SuccessorPlaceId" <> "PlaceId"),
        CONSTRAINT "FK_PlaceDeprecations_GeographicCatalogReleases_CatalogReleaseId" FOREIGN KEY ("CatalogReleaseId") REFERENCES "GeographicCatalogReleases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_PlaceDeprecations_GeographicPlaces_PlaceId" FOREIGN KEY ("PlaceId") REFERENCES "GeographicPlaces" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_PlaceDeprecations_GeographicPlaces_SuccessorPlaceId" FOREIGN KEY ("SuccessorPlaceId") REFERENCES "GeographicPlaces" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "PlaceMergeHistory" (
        "Id" uuid NOT NULL,
        "SourcePlaceId" uuid NOT NULL,
        "TargetPlaceId" uuid NOT NULL,
        "CatalogReleaseId" uuid NOT NULL,
        "MergeKind" character varying(32) NOT NULL,
        "Reason" character varying(512) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_PlaceMergeHistory" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_PlaceMergeHistory_NoSelf" CHECK ("SourcePlaceId" <> "TargetPlaceId"),
        CONSTRAINT "FK_PlaceMergeHistory_GeographicCatalogReleases_CatalogReleaseId" FOREIGN KEY ("CatalogReleaseId") REFERENCES "GeographicCatalogReleases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_PlaceMergeHistory_GeographicPlaces_SourcePlaceId" FOREIGN KEY ("SourcePlaceId") REFERENCES "GeographicPlaces" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_PlaceMergeHistory_GeographicPlaces_TargetPlaceId" FOREIGN KEY ("TargetPlaceId") REFERENCES "GeographicPlaces" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "ServiceRequests" (
        "Id" uuid NOT NULL,
        "ServiceId" uuid NOT NULL,
        "BuyerUserId" uuid NOT NULL,
        "ProviderUserId" uuid NOT NULL,
        "ProviderProfileId" uuid NOT NULL,
        "ServiceTitleSnapshot" character varying(200) NOT NULL,
        "ServiceCategorySnapshot" character varying(100),
        "ServiceStartingPriceSnapshot" numeric,
        "ServicePricingTypeSnapshot" integer,
        "ServiceCurrencySnapshot" character varying(3),
        "ServicePackagesSnapshot" text,
        "Status" text NOT NULL,
        "RequestMessage" text,
        "AcceptedAt" timestamp with time zone,
        "RejectedAt" timestamp with time zone,
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CashboxTransactions" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CashboxId" uuid NOT NULL,
        "TransactionDate" timestamp with time zone NOT NULL,
        "TransactionType" integer NOT NULL,
        "Amount" numeric(18,2) NOT NULL,
        "BalanceAfter" numeric(18,2) NOT NULL,
        "PaymentMethod" integer,
        "ReferenceId" uuid,
        "ReferenceNumber" character varying(100),
        "Description" character varying(500) NOT NULL,
        "UserId" uuid,
        "Notes" character varying(1000),
        "IsOpeningBalance" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CashboxTransactions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CashboxTransactions_Cashboxes_CashboxId" FOREIGN KEY ("CashboxId") REFERENCES "Cashboxes" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CashboxTransactions_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "Expenses" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ExpenseCategoryId" uuid NOT NULL,
        "ExpenseDate" timestamp with time zone NOT NULL,
        "Amount" numeric(18,2) NOT NULL,
        "Description" character varying(200) NOT NULL,
        "Notes" text,
        "PaymentMethod" character varying(50) NOT NULL,
        "SupplierId" uuid,
        "CashboxId" uuid,
        "BankId" uuid,
        "ReceiptPath" character varying(500),
        "ReferenceNumber" character varying(100),
        "IsRecurring" boolean NOT NULL,
        "RecurringFrequency" character varying(50),
        "RequiresApproval" boolean NOT NULL,
        "IsApproved" boolean NOT NULL,
        "ApprovedBy" uuid,
        "ApprovedAt" timestamp with time zone,
        "AccountingMutationVersion" integer NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_Expenses" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Expenses_Banks_BankId" FOREIGN KEY ("BankId") REFERENCES "Banks" ("Id"),
        CONSTRAINT "FK_Expenses_Cashboxes_CashboxId" FOREIGN KEY ("CashboxId") REFERENCES "Cashboxes" ("Id"),
        CONSTRAINT "FK_Expenses_ExpenseCategories_ExpenseCategoryId" FOREIGN KEY ("ExpenseCategoryId") REFERENCES "ExpenseCategories" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_Expenses_Suppliers_SupplierId" FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "AffiliateAttributions" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "AffiliateAccountId" uuid NOT NULL,
        "ReferralCode" character varying(12) NOT NULL,
        "Source" integer NOT NULL,
        "AttributedAtUtc" timestamp with time zone NOT NULL,
        "PromotionLinkId" uuid,
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CouponReservations" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CouponId" uuid NOT NULL,
        "CustomerId" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "Status" integer NOT NULL,
        "CorrelationId" character varying(100),
        "ReservedAtUtc" timestamp with time zone NOT NULL,
        "ConfirmedAtUtc" timestamp with time zone,
        "ReleasedAtUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CouponReservations" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CouponReservation_Status" CHECK ("Status" IN (0, 1, 2)),
        CONSTRAINT "FK_CouponReservations_Coupons_CouponId" FOREIGN KEY ("CouponId") REFERENCES "Coupons" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CouponReservations_OnlineCustomers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "OnlineCustomers" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CouponReservations_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CouponUsages" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CouponId" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "CustomerId" uuid,
        "CouponCode" text NOT NULL,
        "DiscountAmount" numeric(18,2) NOT NULL,
        "OrderSubtotal" numeric(18,2) NOT NULL,
        "UsedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CouponUsages" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CouponUsages_Coupons_CouponId" FOREIGN KEY ("CouponId") REFERENCES "Coupons" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CouponUsages_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "OrderRefunds" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "RefundId" character varying(120) NOT NULL,
        "Amount" numeric(18,2) NOT NULL,
        "Currency" character varying(3) NOT NULL,
        "IsFullRefund" boolean NOT NULL,
        "Reason" character varying(1000) NOT NULL,
        "PaymentMethod" character varying(100) NOT NULL,
        "IsPostSettlementRefund" boolean NOT NULL,
        "SettlementBatchId" uuid,
        "SettlementType" character varying(32),
        "SettlementId" character varying(200),
        "PayoutBatchId" character varying(200),
        "SettledAtUtc" timestamp with time zone,
        "FinancialPostingStatus" integer NOT NULL DEFAULT 1,
        "FinancialPostingPendingAtUtc" timestamp with time zone DEFAULT (CURRENT_TIMESTAMP),
        "FinancialPostedAtUtc" timestamp with time zone,
        "FinancialPostingJournalId" uuid,
        "FinancialPostingFailureReason" character varying(1000),
        "RefundedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_OrderRefunds" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_OrderRefunds_TenantId_OrderId_Id" UNIQUE ("TenantId", "OrderId", "Id"),
        CONSTRAINT "CK_OrderRefunds_FinancialPostingStatus_Valid" CHECK ("FinancialPostingStatus" IN (1, 2, 3) AND ("FinancialPostingStatus" <> 1 OR "FinancialPostingPendingAtUtc" IS NOT NULL) AND ("FinancialPostingStatus" <> 2 OR ("FinancialPostedAtUtc" IS NOT NULL AND "FinancialPostingJournalId" IS NOT NULL)) AND ("FinancialPostingStatus" <> 3 OR "FinancialPostingFailureReason" IS NOT NULL)),
        CONSTRAINT "FK_OrderRefunds_Orders_TenantId_OrderId" FOREIGN KEY ("TenantId", "OrderId") REFERENCES "Orders" ("TenantId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_OrderRefunds_PaymentSettlementBatches_SettlementBatchId" FOREIGN KEY ("SettlementBatchId") REFERENCES "PaymentSettlementBatches" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "PaymentIntents" (
        "Id" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Amount" numeric NOT NULL,
        "Currency" text NOT NULL,
        "PaymentMethod" integer NOT NULL,
        "Status" integer NOT NULL,
        "Provider" text NOT NULL,
        "ReferenceCode" text,
        "ExpiresAtUtc" timestamp with time zone,
        "ProviderIntentId" text,
        "ProviderPaymentId" text,
        "PayoutBatchId" character varying(200),
        "PaymentReference" text NOT NULL,
        "RedirectUrl" text,
        "ClientSecretEncrypted" text,
        "WebhookUrl" text,
        "IdempotencyKey" text NOT NULL,
        "AuthorizedAt" timestamp with time zone,
        "CapturedAt" timestamp with time zone,
        "FailedAt" timestamp with time zone,
        "FailureReason" text,
        "FailureCode" text,
        "ProviderMetadata" text,
        "LastWebhookEventId" text,
        "WebhookEventCount" integer NOT NULL,
        "WalletType" text,
        "WalletPhoneNumber" text,
        "ProofImageUrl" text,
        "VerifiedAtUtc" timestamp with time zone,
        "VerifiedByAdmin" text,
        "RejectionReason" text,
        "ProofImageExpiresAtUtc" timestamp with time zone,
        "ProofImageDeleted" boolean NOT NULL,
        "InventoryCommitted" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_PaymentIntents" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_PaymentIntents_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "PromotionUsages" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "PromotionId" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "CustomerId" uuid,
        "PromotionName" character varying(200) NOT NULL,
        "DiscountAmount" numeric(18,2) NOT NULL,
        "OrderSubtotal" numeric(18,2) NOT NULL,
        "AppliedToItemsJson" text,
        "AppliedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_PromotionUsages" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_PromotionUsages_OnlineCustomers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "OnlineCustomers" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_PromotionUsages_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_PromotionUsages_Promotions_PromotionId" FOREIGN KEY ("PromotionId") REFERENCES "Promotions" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "Shipments" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "CarrierCode" character varying(50) NOT NULL,
        "ServiceLevel" character varying(50) NOT NULL,
        "Status" integer NOT NULL,
        "ShippingCost" numeric(18,2) NOT NULL,
        "Currency" character varying(10) NOT NULL,
        "IsCashOnDelivery" boolean NOT NULL,
        "CodAmount" numeric(18,2),
        "CodCollected" boolean NOT NULL,
        "CodCollectedAtUtc" timestamp with time zone,
        "TrackingNumber" character varying(100),
        "TrackingUrl" character varying(500),
        "AwbNumber" character varying(100),
        "ShipmentNumber" character varying(50) NOT NULL,
        "ShippingMethodId" uuid,
        "ShippingMethodName" character varying(200),
        "RecipientName" character varying(200) NOT NULL,
        "RecipientPhone" character varying(50),
        "RecipientEmail" character varying(255),
        "ShippingAddress" character varying(500) NOT NULL,
        "ShippingAddressLine2" character varying(500),
        "ShippingCity" character varying(100),
        "ShippingState" character varying(100),
        "ShippingZipCode" character varying(20),
        "ShippingCountry" character varying(10),
        "Latitude" numeric(9,6),
        "Longitude" numeric(9,6),
        "DestinationAddressAdmissionEvidenceId" uuid,
        "DestinationGeographicCatalogReleaseId" uuid,
        "DestinationCountryPlaceId" uuid,
        "DestinationAdministrativeAreaPlaceId" uuid,
        "DestinationLocalityPlaceId" uuid,
        "DestinationDistrictPlaceId" uuid,
        "DestinationZonePlaceId" uuid,
        "DestinationCountryDisplayName" character varying(200),
        "DestinationAdministrativeAreaDisplayName" character varying(200),
        "DestinationLocalityDisplayName" character varying(200),
        "DestinationDistrictDisplayName" character varying(200),
        "DestinationZoneDisplayName" character varying(200),
        "DestinationStreet" character varying(500),
        "DestinationStreet2" character varying(500),
        "DestinationBuilding" character varying(120),
        "DestinationFloor" character varying(64),
        "DestinationApartment" character varying(64),
        "DestinationLandmark" character varying(200),
        "DestinationGeographicSnapshotCreatedAtUtc" timestamp with time zone,
        "DestinationGeographicSnapshotVersion" integer,
        "OriginWarehouseId" uuid,
        "OriginAddressAdmissionEvidenceId" uuid,
        "OriginGeographicCatalogReleaseId" uuid,
        "OriginCountryPlaceId" uuid,
        "OriginAdministrativeAreaPlaceId" uuid,
        "OriginLocalityPlaceId" uuid,
        "OriginDistrictPlaceId" uuid,
        "OriginZonePlaceId" uuid,
        "OriginCountryIso2" character varying(2),
        "OriginCountryDisplayName" character varying(200),
        "OriginAdministrativeAreaDisplayName" character varying(200),
        "OriginLocalityDisplayName" character varying(200),
        "OriginDistrictDisplayName" character varying(200),
        "OriginZoneDisplayName" character varying(200),
        "OriginContactName" character varying(200),
        "OriginContactPhone" character varying(64),
        "OriginStreet" character varying(500),
        "OriginGeographicSnapshotCreatedAtUtc" timestamp with time zone,
        "OriginGeographicSnapshotVersion" integer,
        "OriginSnapshotJson" text,
        "CarrierGeographicMappingReleaseId" uuid,
        "CarrierGeographicMappingVersion" bigint,
        "CarrierGeographicMappingReleaseKey" character varying(128),
        "CarrierGeographicEnvironment" character varying(24),
        "CarrierGeographicAccountScope" character varying(128),
        "CarrierGeographicAccountScopeId" uuid,
        "CarrierGeographicMappingSnapshotJson" jsonb,
        "CarrierGeographicResolvedAtUtc" timestamp with time zone,
        "WeightKg" numeric(10,3),
        "PackageCount" integer NOT NULL,
        "PackageDescription" character varying(500),
        "EstimatedDeliveryDate" timestamp with time zone,
        "ActualDeliveryDate" timestamp with time zone,
        "ReadyForPickupAtUtc" timestamp with time zone,
        "PickedUpAtUtc" timestamp with time zone,
        "ShippedAtUtc" timestamp with time zone,
        "OutForDeliveryAtUtc" timestamp with time zone,
        "DeliveredAtUtc" timestamp with time zone,
        "CancelledAtUtc" timestamp with time zone,
        "ReturnedAtUtc" timestamp with time zone,
        "FailedAtUtc" timestamp with time zone,
        "CarrierMetadata" text,
        "CarrierCreateResponse" text,
        "ExternalReferenceId" character varying(200),
        "CarrierRequestId" character varying(200),
        "ExternalRegistrationState" character varying(50),
        "ExternalRegistrationAttemptCount" integer NOT NULL,
        "ExternalRegistrationRequestedAtUtc" timestamp with time zone,
        "ExternalRegistrationLastAttemptAtUtc" timestamp with time zone,
        "ExternalRegistrationCompletedAtUtc" timestamp with time zone,
        "ExternalRegistrationFailedAtUtc" timestamp with time zone,
        "ExternalRegistrationError" character varying(500),
        "CarrierTrackingResponse" text,
        "LastCarrierSyncAtUtc" timestamp with time zone,
        "CancellationReason" character varying(500),
        "FailureReason" character varying(500),
        "ReturnReason" character varying(500),
        "Notes" character varying(1000),
        "DeliveryAttempts" integer NOT NULL,
        "LabelUrl" character varying(1000),
        "LabelData" text,
        "LabelFormat" character varying(20),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_Shipments" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Shipments_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_Shipments_ShippingMethods_ShippingMethodId" FOREIGN KEY ("ShippingMethodId") REFERENCES "ShippingMethods" ("Id") ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "PurchaseInvoicePayments" (
        "Id" uuid NOT NULL,
        "PurchaseInvoiceId" uuid NOT NULL,
        "PaymentDate" timestamp with time zone NOT NULL,
        "Amount" numeric NOT NULL,
        "PaymentMethod" integer NOT NULL,
        "ReferenceNumber" text,
        "Notes" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_PurchaseInvoicePayments" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_PurchaseInvoicePayments_PurchaseInvoices_PurchaseInvoiceId" FOREIGN KEY ("PurchaseInvoiceId") REFERENCES "PurchaseInvoices" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "SupplierPayments" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "SupplierId" uuid NOT NULL,
        "PurchaseInvoiceId" uuid,
        "PaymentDate" timestamp with time zone NOT NULL,
        "Amount" numeric(18,2) NOT NULL,
        "PaymentMethod" integer NOT NULL,
        "IdempotencyKey" character varying(100) NOT NULL,
        "ReferenceNumber" character varying(100),
        "Notes" character varying(1000),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_SupplierPayments" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SupplierPayments_PurchaseInvoices_PurchaseInvoiceId" FOREIGN KEY ("PurchaseInvoiceId") REFERENCES "PurchaseInvoices" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_SupplierPayments_Suppliers_SupplierId" FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "Returns" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ReturnNumber" text NOT NULL,
        "ReturnDate" timestamp with time zone NOT NULL,
        "ReturnType" integer NOT NULL,
        "OriginalInvoiceId" uuid,
        "OriginalInvoiceNumber" text,
        "OriginalPurchaseInvoiceId" uuid,
        "CustomerId" uuid,
        "SupplierId" uuid,
        "WarehouseId" uuid NOT NULL,
        "Subtotal" numeric NOT NULL,
        "TaxAmount" numeric NOT NULL,
        "TotalAmount" numeric NOT NULL,
        "Reason" text,
        "Notes" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_Returns" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_Returns_PurchaseAuthorityLineage" CHECK (("ReturnType" <> 0) OR ("OriginalPurchaseInvoiceId" IS NOT NULL AND "OriginalInvoiceId" IS NULL AND "SupplierId" IS NOT NULL AND "CustomerId" IS NULL)),
        CONSTRAINT "FK_Returns_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id"),
        CONSTRAINT "FK_Returns_PurchaseInvoices_TenantId_OriginalPurchaseInvoiceId" FOREIGN KEY ("TenantId", "OriginalPurchaseInvoiceId") REFERENCES "PurchaseInvoices" ("TenantId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_Returns_SalesInvoices_OriginalInvoiceId" FOREIGN KEY ("OriginalInvoiceId") REFERENCES "SalesInvoices" ("Id"),
        CONSTRAINT "FK_Returns_Suppliers_SupplierId" FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("Id"),
        CONSTRAINT "FK_Returns_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "SalesInvoicePayments" (
        "Id" uuid NOT NULL,
        "SalesInvoiceId" uuid NOT NULL,
        "PaymentDate" timestamp with time zone NOT NULL,
        "Amount" numeric NOT NULL,
        "PaymentMethod" integer NOT NULL,
        "ReferenceNumber" text,
        "Notes" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_SalesInvoicePayments" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SalesInvoicePayments_SalesInvoices_SalesInvoiceId" FOREIGN KEY ("SalesInvoiceId") REFERENCES "SalesInvoices" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "ProductVariants" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "SKU" character varying(100),
        "Barcode" character varying(100),
        "AttributeCombinationKey" character varying(80),
        "Price" numeric(18,2) NOT NULL,
        "OnlinePrice" numeric(18,2),
        "CostPrice" numeric(18,2) NOT NULL,
        "IsDefault" boolean NOT NULL,
        "IsActive" boolean NOT NULL,
        "SemanticLifecycleState" integer NOT NULL DEFAULT 0,
        "SemanticReplacementVariantId" uuid,
        "SemanticLineageRootVariantId" uuid,
        "SemanticOperationId" uuid,
        "SemanticOperationKind" character varying(64),
        "SemanticSupersededAtUtc" timestamp with time zone,
        "MediaId" uuid,
        "ImageUrl" character varying(500),
        "SortOrder" integer NOT NULL,
        "Weight" numeric(18,3),
        "StockUpdatedAtUtc" timestamp with time zone,
        "ProjectionMutationVersion" bigint NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_ProductVariants" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_ProductVariants_ProductMedia_TenantId_ProductId_MediaId" FOREIGN KEY ("TenantId", "ProductId", "MediaId") REFERENCES "ProductMedia" ("TenantId", "ProductId", "Id"),
        CONSTRAINT "FK_ProductVariants_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubPaymentAttempts" (
        "Id" uuid NOT NULL,
        "PaymentId" uuid NOT NULL,
        "Provider" integer NOT NULL,
        "AttemptNumber" integer NOT NULL,
        "ProviderReference" character varying(100) NOT NULL,
        "ProviderIntentId" character varying(200),
        "ProviderTransactionId" character varying(200),
        "Status" integer NOT NULL,
        "FailureReason" character varying(1000),
        "CompletedAtUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CommerceHubPaymentAttempts" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CommerceHubPaymentAttempts_CommerceHubPayments_PaymentId" FOREIGN KEY ("PaymentId") REFERENCES "CommerceHubPayments" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubPaymentLogs" (
        "Id" uuid NOT NULL,
        "PaymentId" uuid NOT NULL,
        "EventType" character varying(100) NOT NULL,
        "ProviderEventId" character varying(100),
        "RawResponse" text NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CommerceHubPaymentLogs" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CommerceHubPaymentLogs_CommerceHubPayments_PaymentId" FOREIGN KEY ("PaymentId") REFERENCES "CommerceHubPayments" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubPaymentItems" (
        "Id" uuid NOT NULL,
        "PaymentId" uuid NOT NULL,
        "SubOrderId" uuid NOT NULL,
        "Amount" numeric(18,2) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CommerceHubPaymentItems" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CommerceHubPaymentItems_CommerceHubPayments_PaymentId" FOREIGN KEY ("PaymentId") REFERENCES "CommerceHubPayments" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_CommerceHubPaymentItems_CommerceHubSubOrders_SubOrderId" FOREIGN KEY ("SubOrderId") REFERENCES "CommerceHubSubOrders" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubRefunds" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "SubOrderId" uuid NOT NULL,
        "RefundRequestId" uuid,
        "OrderItemId" uuid,
        "Quantity" integer,
        "PaymentId" uuid,
        "Amount" numeric(18,2) NOT NULL,
        "CommissionReversalAmount" numeric(18,2) NOT NULL,
        "PromoDiscountReversalAmount" numeric NOT NULL,
        "VendorEarningReversalAmount" numeric(18,2) NOT NULL,
        "Currency" character varying(10) NOT NULL,
        "Status" integer NOT NULL,
        "Reason" character varying(1000) NOT NULL,
        "Reference" character varying(200),
        "GatewayStatus" character varying(100),
        "FailureReason" character varying(1000),
        "IdempotencyKey" character varying(200) NOT NULL,
        "IsFullRefund" boolean NOT NULL,
        "IsPostPayoutAdjustment" boolean NOT NULL,
        "ProcessingStartedAtUtc" timestamp with time zone,
        "ProcessedAtUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CommerceHubRefunds" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CommerceHubRefunds_Amount_Positive" CHECK ("Amount" > 0),
        CONSTRAINT "CK_CommerceHubRefunds_Completed_State_Complete" CHECK ("Status" <> 2 OR "ProcessedAtUtc" IS NOT NULL),
        CONSTRAINT "CK_CommerceHubRefunds_Currency_EGP" CHECK ("Currency" = 'EGP'),
        CONSTRAINT "CK_CommerceHubRefunds_Reversals_NonNegative" CHECK ("CommissionReversalAmount" >= 0 AND "PromoDiscountReversalAmount" >= 0 AND "VendorEarningReversalAmount" >= 0),
        CONSTRAINT "CK_CommerceHubRefunds_Reversals_Total_Amount" CHECK ("CommissionReversalAmount" + "PromoDiscountReversalAmount" + "VendorEarningReversalAmount" = "Amount"),
        CONSTRAINT "FK_CommerceHubRefunds_CommerceHubPayments_PaymentId" FOREIGN KEY ("PaymentId") REFERENCES "CommerceHubPayments" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_CommerceHubRefunds_CommerceHubSubOrders_SubOrderId" FOREIGN KEY ("SubOrderId") REFERENCES "CommerceHubSubOrders" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubSubOrderItems" (
        "Id" uuid NOT NULL,
        "SubOrderId" uuid NOT NULL,
        "ListingId" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "VariantId" uuid NOT NULL,
        "CategoryId" uuid NOT NULL,
        "ProductName" character varying(250) NOT NULL,
        "VariantName" character varying(250),
        "VariantAttributesJson" text,
        "ImageUrl" character varying(1000),
        "Quantity" integer NOT NULL,
        "UnitPrice" numeric(18,2) NOT NULL,
        "PlatformCommissionRate" numeric(8,6) NOT NULL,
        "PlatformCommissionAmount" numeric(18,2) NOT NULL,
        "FinalPlatformCommissionAmount" numeric NOT NULL,
        "PromoDiscountAmount" numeric NOT NULL,
        "Total" numeric(18,2) NOT NULL,
        "Status" integer NOT NULL DEFAULT 0,
        "DeliveredAt" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CommerceHubSubOrderItems" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CommerceHubSubOrderItems_CommerceHubSubOrders_SubOrderId" FOREIGN KEY ("SubOrderId") REFERENCES "CommerceHubSubOrders" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubSubOrderStatusHistory" (
        "Id" uuid NOT NULL,
        "SubOrderId" uuid NOT NULL,
        "PreviousStatus" integer NOT NULL,
        "NewStatus" integer NOT NULL,
        "Reason" character varying(1000),
        "CorrelationId" character varying(200),
        "ChangedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CommerceHubSubOrderStatusHistory" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CommerceHubSubOrderStatusHistory_CommerceHubSubOrders_SubOr~" FOREIGN KEY ("SubOrderId") REFERENCES "CommerceHubSubOrders" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "AbandonedCartRecoveryLogs" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "AbandonedCartId" uuid NOT NULL,
        "CartId" uuid NOT NULL,
        "PhoneNumber" text NOT NULL,
        "FirstMessageSent" boolean NOT NULL,
        "FirstMessageSentAtUtc" timestamp with time zone,
        "FirstMessageError" text,
        "SecondMessageSent" boolean NOT NULL,
        "SecondMessageSentAtUtc" timestamp with time zone,
        "SecondMessageError" text,
        "WorkflowStartedAtUtc" timestamp with time zone NOT NULL,
        "WorkflowCompletedAtUtc" timestamp with time zone,
        "IdempotencyKey" text,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_AbandonedCartRecoveryLogs" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AbandonedCartRecoveryLogs_AbandonedCarts_AbandonedCartId" FOREIGN KEY ("AbandonedCartId") REFERENCES "AbandonedCarts" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubVendorPaymentAuditLogs" (
        "Id" uuid NOT NULL,
        "PaymentRequestId" uuid NOT NULL,
        "AdminId" uuid NOT NULL,
        "Action" integer NOT NULL,
        "Amount" numeric(18,2) NOT NULL,
        "Currency" character varying(10) NOT NULL DEFAULT 'EGP',
        "Reason" text NOT NULL,
        "TimestampUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_CommerceHubVendorPaymentAuditLogs" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CommerceHubVendorPaymentAuditLogs_Action" CHECK ("Action" IN (0, 1)),
        CONSTRAINT "CK_CommerceHubVendorPaymentAuditLogs_Amount" CHECK ("Amount" > 0),
        CONSTRAINT "FK_CommerceHubVendorPaymentAuditLogs_CommerceHubVendorPaymentR~" FOREIGN KEY ("PaymentRequestId") REFERENCES "CommerceHubVendorPaymentRequests" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubVendorPaymentProofs" (
        "Id" uuid NOT NULL,
        "PaymentRequestId" uuid NOT NULL,
        "ObjectKey" character varying(500) NOT NULL,
        "OriginalFileName" character varying(255) NOT NULL,
        "ContentType" character varying(255) NOT NULL,
        "SizeBytes" bigint NOT NULL,
        "ContentHash" character varying(64) NOT NULL,
        "UploadedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_CommerceHubVendorPaymentProofs" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CommerceHubVendorPaymentProofs_CommerceHubVendorPaymentRequ~" FOREIGN KEY ("PaymentRequestId") REFERENCES "CommerceHubVendorPaymentRequests" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CarrierGeographicMappings" (
        "Id" uuid NOT NULL,
        "MappingReleaseId" uuid NOT NULL,
        "MappingGroupId" uuid,
        "CarrierProviderId" uuid NOT NULL,
        "CanonicalPlaceId" uuid NOT NULL,
        "CountryPlaceId" uuid NOT NULL,
        "CanonicalPlaceKind" character varying(24) NOT NULL,
        "ProviderPlaceType" character varying(64) NOT NULL,
        "ProviderPlaceId" character varying(256) NOT NULL,
        "ProviderPlaceCode" character varying(256),
        "ProviderDisplayName" text NOT NULL,
        "ProviderNormalizedName" text NOT NULL,
        "ProviderParentMappingId" uuid,
        "RequiredForOperation" character varying(64) NOT NULL,
        "MappingStatus" character varying(24) NOT NULL,
        "VerificationStatus" character varying(24) NOT NULL,
        "Source" character varying(128) NOT NULL,
        "EffectiveFromUtc" timestamp with time zone NOT NULL,
        "EffectiveToUtc" timestamp with time zone,
        "ReviewedAtUtc" timestamp with time zone,
        "ReviewedByUserId" uuid,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_CarrierGeographicMappings" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CarrierGeoMapping_Status" CHECK ("MappingStatus" IN ('Draft','Active','Deprecated','Retired','Rejected')),
        CONSTRAINT "CK_CarrierGeoMapping_Verification" CHECK ("VerificationStatus" IN ('Imported','AutoCarried','Reviewed','ManuallyApproved','Rejected')),
        CONSTRAINT "CK_CarrierGeoMapping_Window" CHECK ("EffectiveToUtc" IS NULL OR "EffectiveToUtc" > "EffectiveFromUtc"),
        CONSTRAINT "FK_CarrierGeographicMappings_CarrierGeographicMappingGroups_Ma~" FOREIGN KEY ("MappingGroupId") REFERENCES "CarrierGeographicMappingGroups" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CarrierGeographicMappings_CarrierGeographicMappingReleases_~" FOREIGN KEY ("MappingReleaseId") REFERENCES "CarrierGeographicMappingReleases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CarrierGeographicMappings_CarrierGeographicMappings_Provide~" FOREIGN KEY ("ProviderParentMappingId") REFERENCES "CarrierGeographicMappings" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CarrierGeographicMappings_CarrierProviders_CarrierProviderId" FOREIGN KEY ("CarrierProviderId") REFERENCES "CarrierProviders" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CarrierGeographicMappings_GeographicPlaces_CanonicalPlaceId" FOREIGN KEY ("CanonicalPlaceId") REFERENCES "GeographicPlaces" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CarrierGeographicMappings_GeographicPlaces_CountryPlaceId" FOREIGN KEY ("CountryPlaceId") REFERENCES "GeographicPlaces" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "GeographicCatalogReleaseEdges" (
        "CatalogReleaseId" uuid NOT NULL,
        "HierarchyEdgeId" uuid NOT NULL,
        CONSTRAINT "PK_GeographicCatalogReleaseEdges" PRIMARY KEY ("CatalogReleaseId", "HierarchyEdgeId"),
        CONSTRAINT "FK_GeographicCatalogReleaseEdges_GeographicCatalogReleases_Cat~" FOREIGN KEY ("CatalogReleaseId") REFERENCES "GeographicCatalogReleases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_GeographicCatalogReleaseEdges_GeographicPlaceHierarchyEdges~" FOREIGN KEY ("HierarchyEdgeId") REFERENCES "GeographicPlaceHierarchyEdges" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "ServiceRequestMessages" (
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "ServiceReviews" (
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
        CONSTRAINT "CK_ServiceReviews_Rating_Range" CHECK ("Rating" >= 1 AND "Rating" <= 5),
        CONSTRAINT "FK_ServiceReviews_ServiceRequests_ServiceRequestId" FOREIGN KEY ("ServiceRequestId") REFERENCES "ServiceRequests" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_ServiceReviews_Services_ServiceId" FOREIGN KEY ("ServiceId") REFERENCES "Services" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "PaymentSettlementOrders" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "SettlementBatchId" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "PaymentIntentId" uuid,
        "OrderTotal" numeric(18,2) NOT NULL,
        "Currency" character varying(3) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_PaymentSettlementOrders" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_PaymentSettlementOrders_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_PaymentSettlementOrders_PaymentIntents_PaymentIntentId" FOREIGN KEY ("PaymentIntentId") REFERENCES "PaymentIntents" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_PaymentSettlementOrders_PaymentSettlementBatches_Settlement~" FOREIGN KEY ("SettlementBatchId") REFERENCES "PaymentSettlementBatches" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "PaymentWebhookLogs" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Provider" text NOT NULL,
        "ProviderEventId" text NOT NULL,
        "EventType" text NOT NULL,
        "PaymentIntentId" uuid,
        "OrderId" uuid,
        "RawPayload" text NOT NULL,
        "Signature" text,
        "SignatureVerified" boolean NOT NULL,
        "ProcessingStatus" integer NOT NULL,
        "ProcessedAt" timestamp with time zone,
        "ProcessingError" text,
        "ProcessingDurationMs" bigint,
        "SourceIp" text,
        "UserAgent" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_PaymentWebhookLogs" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_PaymentWebhookLogs_PaymentIntents_PaymentIntentId" FOREIGN KEY ("PaymentIntentId") REFERENCES "PaymentIntents" ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubListings" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "VariantId" uuid NOT NULL,
        "IsPublished" boolean NOT NULL,
        "RestorePublicationOnActivate" boolean NOT NULL DEFAULT FALSE,
        "CommerceHubCategoryId" uuid NOT NULL,
        "CommerceHubPrice" numeric(18,2),
        "CommerceHubPriceOverrideUpdatedAtUtc" timestamp with time zone,
        "CommerceHubPriceOverrideUpdatedBy" uuid,
        "CommerceHubPriceOverrideReason" character varying(1024),
        "CommerceHubPriceOverrideMutationVersion" integer NOT NULL,
        "AttributesJson" text,
        "AttributeSchemaSnapshotJson" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CommerceHubListings" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CommerceHubListings_CommerceHubPrice_Positive" CHECK ("CommerceHubPrice" IS NULL OR "CommerceHubPrice" > 0),
        CONSTRAINT "FK_CommerceHubListings_ProductVariants_VariantId" FOREIGN KEY ("VariantId") REFERENCES "ProductVariants" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CommerceHubListings_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "OrderItems" (
        "Id" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "VariantId" uuid,
        "UnitId" uuid,
        "WarehouseId" uuid,
        "SKU" text NOT NULL,
        "ProductName" text NOT NULL,
        "VariantName" text,
        "VariantAttributesJson" text,
        "ImageUrl" text,
        "Quantity" integer NOT NULL,
        "UnitPrice" numeric NOT NULL,
        "DiscountAmount" numeric NOT NULL,
        "TaxAmount" numeric NOT NULL,
        "Total" numeric NOT NULL,
        "CostPrice" numeric NOT NULL,
        "Notes" text,
        "AffiliateCommissionEligible" boolean NOT NULL,
        "AffiliateCommissionPolicyIdSnapshot" uuid,
        "AffiliateCommissionRateSnapshot" numeric(18,6),
        "AffiliateMaxCommissionPerItemSnapshot" numeric(18,4),
        "AffiliateCommissionBaseAmountSnapshot" numeric(18,4),
        "AffiliateCommissionAmountSnapshot" numeric(18,4),
        "AffiliateCommissionSnapshotCapturedAtUtc" timestamp with time zone,
        "PaidAmount" numeric(18,2) NOT NULL,
        "RefundedAmount" numeric(18,2) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_OrderItems" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_OrderItems_OrderId_Id" UNIQUE ("OrderId", "Id"),
        CONSTRAINT "CK_OrderItems_AffiliateCommissionSnapshot_Valid" CHECK (("AffiliateCommissionEligible" = true AND "AffiliateCommissionSnapshotCapturedAtUtc" IS NOT NULL AND "AffiliateCommissionPolicyIdSnapshot" IS NOT NULL AND "AffiliateCommissionRateSnapshot" > 0 AND "AffiliateCommissionRateSnapshot" <= 0.500000 AND "AffiliateCommissionBaseAmountSnapshot" > 0 AND "AffiliateCommissionAmountSnapshot" > 0 AND ("AffiliateMaxCommissionPerItemSnapshot" IS NULL OR "AffiliateMaxCommissionPerItemSnapshot" > 0)) OR ("AffiliateCommissionEligible" = false AND "AffiliateCommissionSnapshotCapturedAtUtc" IS NULL AND "AffiliateCommissionPolicyIdSnapshot" IS NULL AND "AffiliateCommissionRateSnapshot" IS NULL AND "AffiliateCommissionBaseAmountSnapshot" IS NULL AND "AffiliateCommissionAmountSnapshot" IS NULL AND ("AffiliateMaxCommissionPerItemSnapshot" IS NULL OR "AffiliateMaxCommissionPerItemSnapshot" = 0))),
        CONSTRAINT "CK_OrderItems_PaymentAmounts_Valid" CHECK ("PaidAmount" >= 0 AND "RefundedAmount" >= 0 AND "RefundedAmount" <= "PaidAmount"),
        CONSTRAINT "FK_OrderItems_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_OrderItems_ProductVariants_VariantId" FOREIGN KEY ("VariantId") REFERENCES "ProductVariants" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_OrderItems_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_OrderItems_Units_UnitId" FOREIGN KEY ("UnitId") REFERENCES "Units" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_OrderItems_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "ProductVariantAttributes" (
        "Id" uuid NOT NULL,
        "VariantId" uuid NOT NULL,
        "Name" character varying(100) NOT NULL,
        "Value" character varying(255) NOT NULL,
        "SortOrder" integer NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_ProductVariantAttributes" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_ProductVariantAttributes_ProductVariants_VariantId" FOREIGN KEY ("VariantId") REFERENCES "ProductVariants" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "ProductVariantStocks" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "VariantId" uuid NOT NULL,
        "WarehouseId" uuid NOT NULL,
        "Quantity" numeric(18,2) NOT NULL,
        "ReservedQuantity" numeric(18,2) NOT NULL,
        "DamagedQuantity" numeric(18,2) NOT NULL,
        "ProjectionMutationVersion" bigint NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_ProductVariantStocks" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_ProductVariantStocks_DamagedQuantity_NonNegative" CHECK ("DamagedQuantity" >= 0),
        CONSTRAINT "CK_ProductVariantStocks_Quantity_NonNegative" CHECK ("Quantity" >= 0),
        CONSTRAINT "CK_ProductVariantStocks_ReservedQuantity_NonNegative" CHECK ("ReservedQuantity" >= 0),
        CONSTRAINT "CK_ProductVariantStocks_ReservedQuantity_Within_Quantity" CHECK ("Quantity" >= "ReservedQuantity"),
        CONSTRAINT "FK_ProductVariantStocks_ProductVariants_VariantId" FOREIGN KEY ("VariantId") REFERENCES "ProductVariants" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_ProductVariantStocks_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "PurchaseInvoiceItems" (
        "Id" uuid NOT NULL,
        "PurchaseInvoiceId" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "ProductVariantId" uuid,
        "UnitId" uuid,
        "Quantity" numeric NOT NULL,
        "ConversionFactor" numeric NOT NULL,
        "BaseQuantity" numeric NOT NULL,
        "UnitPrice" numeric NOT NULL,
        "DiscountAmount" numeric NOT NULL,
        "TaxRate" numeric NOT NULL,
        "TaxAmount" numeric NOT NULL,
        "TotalAmount" numeric NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_PurchaseInvoiceItems" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_PurchaseInvoiceItems_BaseQuantity_Positive" CHECK ("BaseQuantity" > 0),
        CONSTRAINT "CK_PurchaseInvoiceItems_ConversionFactor_Positive" CHECK ("ConversionFactor" > 0),
        CONSTRAINT "CK_PurchaseInvoiceItems_Quantity_Positive" CHECK ("Quantity" > 0),
        CONSTRAINT "CK_PurchaseInvoiceItems_TaxRate_Valid" CHECK ("TaxRate" >= 0 AND "TaxRate" <= 100),
        CONSTRAINT "CK_PurchaseInvoiceItems_UnitPrice_Positive" CHECK ("UnitPrice" > 0),
        CONSTRAINT "FK_PurchaseInvoiceItems_ProductVariants_ProductVariantId" FOREIGN KEY ("ProductVariantId") REFERENCES "ProductVariants" ("Id"),
        CONSTRAINT "FK_PurchaseInvoiceItems_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_PurchaseInvoiceItems_PurchaseInvoices_PurchaseInvoiceId" FOREIGN KEY ("PurchaseInvoiceId") REFERENCES "PurchaseInvoices" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_PurchaseInvoiceItems_Units_UnitId" FOREIGN KEY ("UnitId") REFERENCES "Units" ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "ReturnItems" (
        "Id" uuid NOT NULL,
        "ReturnId" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "ProductVariantId" uuid,
        "UnitId" uuid,
        "Quantity" numeric NOT NULL,
        "ConversionFactor" numeric NOT NULL,
        "BaseQuantity" numeric NOT NULL,
        "UnitPrice" numeric NOT NULL,
        "TaxRate" numeric NOT NULL,
        "TaxAmount" numeric NOT NULL,
        "TotalAmount" numeric NOT NULL,
        "CostPriceSnapshot" numeric(18,6),
        "CostAmountSnapshot" numeric(18,2),
        "Reason" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_ReturnItems" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_ReturnItems_Amounts_NonNegative" CHECK ("TaxAmount" >= 0 AND "TotalAmount" >= 0),
        CONSTRAINT "CK_ReturnItems_BaseQuantity_Positive" CHECK ("BaseQuantity" > 0),
        CONSTRAINT "CK_ReturnItems_ConversionFactor_Positive" CHECK ("ConversionFactor" > 0),
        CONSTRAINT "CK_ReturnItems_Quantity_Positive" CHECK ("Quantity" > 0),
        CONSTRAINT "CK_ReturnItems_TaxRate_Valid" CHECK ("TaxRate" >= 0 AND "TaxRate" <= 100),
        CONSTRAINT "CK_ReturnItems_UnitPrice_NonNegative" CHECK ("UnitPrice" >= 0),
        CONSTRAINT "FK_ReturnItems_ProductVariants_ProductVariantId" FOREIGN KEY ("ProductVariantId") REFERENCES "ProductVariants" ("Id"),
        CONSTRAINT "FK_ReturnItems_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_ReturnItems_Returns_ReturnId" FOREIGN KEY ("ReturnId") REFERENCES "Returns" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_ReturnItems_Units_UnitId" FOREIGN KEY ("UnitId") REFERENCES "Units" ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "SalesInvoiceItems" (
        "Id" uuid NOT NULL,
        "SalesInvoiceId" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "ProductVariantId" uuid,
        "UnitId" uuid,
        "Quantity" numeric NOT NULL,
        "ConversionFactor" numeric NOT NULL,
        "BaseQuantity" numeric NOT NULL,
        "UnitPrice" numeric NOT NULL,
        "DiscountAmount" numeric NOT NULL,
        "TaxRate" numeric NOT NULL,
        "TaxAmount" numeric NOT NULL,
        "TotalAmount" numeric NOT NULL,
        "CostPriceSnapshot" numeric(18,6),
        "CostAmountSnapshot" numeric(18,2),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_SalesInvoiceItems" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SalesInvoiceItems_ProductVariants_ProductVariantId" FOREIGN KEY ("ProductVariantId") REFERENCES "ProductVariants" ("Id"),
        CONSTRAINT "FK_SalesInvoiceItems_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_SalesInvoiceItems_SalesInvoices_SalesInvoiceId" FOREIGN KEY ("SalesInvoiceId") REFERENCES "SalesInvoices" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_SalesInvoiceItems_Units_UnitId" FOREIGN KEY ("UnitId") REFERENCES "Units" ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "StockMovements" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "WarehouseId" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "ProductVariantId" uuid,
        "MovementType" integer NOT NULL,
        "UnitId" uuid,
        "ConversionFactor" numeric NOT NULL,
        "QuantityChange" numeric(18,2) NOT NULL,
        "ReservedQuantityChange" numeric(18,2) NOT NULL,
        "DamagedQuantityChange" numeric(18,2) NOT NULL,
        "QuantityAfter" numeric(18,2) NOT NULL,
        "AvailableAfter" numeric(18,2) NOT NULL,
        "ReservedAfter" numeric(18,2) NOT NULL,
        "DamagedAfter" numeric(18,2) NOT NULL,
        "ReferenceId" uuid,
        "ReferenceLineId" uuid,
        "ReservationId" uuid,
        "ReferenceNumber" text,
        "Notes" text,
        "InventoryEventId" uuid,
        "InventoryEventType" integer,
        "ReferenceType" character varying(50),
        "CorrelationId" character varying(100),
        "IdempotencyKey" character varying(200),
        "Source" character varying(100),
        "Price" numeric,
        "IsOpeningBalance" boolean NOT NULL,
        "ToWarehouseId" uuid,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_StockMovements" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_StockMovements_ProductVariants_ProductVariantId" FOREIGN KEY ("ProductVariantId") REFERENCES "ProductVariants" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_StockMovements_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_StockMovements_Units_UnitId" FOREIGN KEY ("UnitId") REFERENCES "Units" ("Id"),
        CONSTRAINT "FK_StockMovements_Warehouses_ToWarehouseId" FOREIGN KEY ("ToWarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_StockMovements_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN

    CREATE OR REPLACE FUNCTION pos_reject_stock_movement_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
    AS $$
    BEGIN
        RAISE EXCEPTION 'StockMovements records are append-only and cannot be updated or deleted.';
    END;
    $$;

    DROP TRIGGER IF EXISTS trg_stock_movements_append_only ON "StockMovements";
    CREATE TRIGGER trg_stock_movements_append_only
    BEFORE UPDATE OR DELETE ON "StockMovements"
    FOR EACH ROW
    EXECUTE FUNCTION pos_reject_stock_movement_mutation();

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "StockTransferItems" (
        "Id" uuid NOT NULL,
        "StockTransferId" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "ProductVariantId" uuid,
        "Quantity" numeric NOT NULL,
        "Notes" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_StockTransferItems" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_StockTransferItems_ProductVariants_ProductVariantId" FOREIGN KEY ("ProductVariantId") REFERENCES "ProductVariants" ("Id"),
        CONSTRAINT "FK_StockTransferItems_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_StockTransferItems_StockTransfers_StockTransferId" FOREIGN KEY ("StockTransferId") REFERENCES "StockTransfers" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "WarehouseStocks" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "WarehouseId" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "ProductVariantId" uuid,
        "Quantity" numeric(18,2) NOT NULL,
        "ReservedQuantity" numeric(18,2) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_WarehouseStocks" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_WarehouseStocks_ProductVariants_ProductVariantId" FOREIGN KEY ("ProductVariantId") REFERENCES "ProductVariants" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_WarehouseStocks_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_WarehouseStocks_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubVendorEarnings" (
        "Id" uuid NOT NULL,
        "SubOrderId" uuid NOT NULL,
        "PaymentId" uuid,
        "PayoutId" uuid,
        "RefundId" uuid,
        "OriginalEarningId" uuid,
        "TenantId" uuid NOT NULL,
        "Amount" numeric(18,2) NOT NULL,
        "PaidOutAmount" numeric(18,2) NOT NULL,
        "PayoutReservedAmount" numeric(18,2) NOT NULL,
        "Status" integer NOT NULL,
        "AvailableAtUtc" timestamp with time zone,
        "PaidOutAtUtc" timestamp with time zone,
        "ReversedAtUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CommerceHubVendorEarnings" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CommerceHubVendorEarnings_InPayout_HasPayout" CHECK ("Status" <> 3 OR "PayoutId" IS NOT NULL),
        CONSTRAINT "CK_CommerceHubVendorEarnings_PaidOut_HasPaidAmount" CHECK ("Status" <> 2 OR "PaidOutAmount" > 0 OR "Amount" < 0),
        CONSTRAINT "CK_CommerceHubVendorEarnings_PayoutAmounts_NonNegative" CHECK ("PaidOutAmount" >= 0 AND "PayoutReservedAmount" >= 0),
        CONSTRAINT "CK_CommerceHubVendorEarnings_PayoutAmounts_Within_Positive_Ear~" CHECK ("Amount" < 0 OR ROUND("PaidOutAmount" + "PayoutReservedAmount", 2) <= ROUND("Amount", 2)),
        CONSTRAINT "FK_CommerceHubVendorEarnings_CommerceHubPayments_PaymentId" FOREIGN KEY ("PaymentId") REFERENCES "CommerceHubPayments" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_CommerceHubVendorEarnings_CommerceHubPayouts_PayoutId" FOREIGN KEY ("PayoutId") REFERENCES "CommerceHubPayouts" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_CommerceHubVendorEarnings_CommerceHubRefunds_RefundId" FOREIGN KEY ("RefundId") REFERENCES "CommerceHubRefunds" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_CommerceHubVendorEarnings_CommerceHubSubOrders_SubOrderId" FOREIGN KEY ("SubOrderId") REFERENCES "CommerceHubSubOrders" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_CommerceHubVendorEarnings_CommerceHubVendorEarnings_Origina~" FOREIGN KEY ("OriginalEarningId") REFERENCES "CommerceHubVendorEarnings" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubRefundRequests" (
        "Id" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "OrderItemId" uuid NOT NULL,
        "CustomerId" uuid NOT NULL,
        "VendorId" uuid NOT NULL,
        "Quantity" integer NOT NULL,
        "UnitPriceSnapshot" numeric(18,2) NOT NULL,
        "TotalAmount" numeric(18,2) NOT NULL,
        "ReasonCode" integer NOT NULL,
        "ReasonText" character varying(1000),
        "Reason" character varying(1000) NOT NULL,
        "PayoutMethodSnapshot" integer,
        "PayoutAccountSnapshot" character varying(500),
        "PayoutAccountMaskedSnapshot" character varying(32),
        "PayoutAccountHolderNameSnapshot" character varying(200),
        "ReturnRequestId" uuid,
        "RefundExecutionTrigger" integer NOT NULL,
        "IdempotencyKey" character varying(100),
        "ReviewedBy" uuid,
        "ReviewedAtUtc" timestamp with time zone,
        "ApprovedAtUtc" timestamp with time zone,
        "RejectedAtUtc" timestamp with time zone,
        "ProcessingStartedAtUtc" timestamp with time zone,
        "CompletedAtUtc" timestamp with time zone,
        "FailedAtUtc" timestamp with time zone,
        "AdminNote" character varying(1000),
        "FailureReason" character varying(2000),
        "ExecutionAttempts" integer NOT NULL,
        "Status" integer NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CommerceHubRefundRequests" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CommerceHubRefundRequests_CommerceHubCustomers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "CommerceHubCustomers" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CommerceHubRefundRequests_CommerceHubMarketplaceOrders_Orde~" FOREIGN KEY ("OrderId") REFERENCES "CommerceHubMarketplaceOrders" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CommerceHubRefundRequests_CommerceHubSubOrderItems_OrderIte~" FOREIGN KEY ("OrderItemId") REFERENCES "CommerceHubSubOrderItems" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "AffiliateCommissionLedger" (
        "Id" uuid NOT NULL,
        "TenantId" uuid,
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" uuid,
        "StoreTenantId" uuid NOT NULL,
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
        CONSTRAINT "CK_AffiliateCommLedger_IdempotencyKey_NotBlank" CHECK (btrim("IdempotencyKey") <> ''),
        CONSTRAINT "CK_AffiliateCommLedger_Rate" CHECK ("CommissionRate" > 0 AND "CommissionRate" <= 0.50),
        CONSTRAINT "CK_AffiliateCommLedger_ReversalRef" CHECK (("EntryType" IN (1, 2, 3) AND "OriginalEntryId" IS NOT NULL) OR ("EntryType" = 0 AND "OriginalEntryId" IS NULL)),
        CONSTRAINT "FK_AffiliateCommissionLedger_AffiliateAccounts_AffiliateAccoun~" FOREIGN KEY ("AffiliateAccountId") REFERENCES "AffiliateAccounts" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AffiliateCommissionLedger_AffiliateAttributions_Attribution~" FOREIGN KEY ("AttributionId") REFERENCES "AffiliateAttributions" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AffiliateCommissionLedger_AffiliateCommissionLedger_Origina~" FOREIGN KEY ("OriginalEntryId") REFERENCES "AffiliateCommissionLedger" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AffiliateCommissionLedger_OrderItems_OrderItemId" FOREIGN KEY ("OrderItemId") REFERENCES "OrderItems" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AffiliateCommissionLedger_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "OrderRefundLines" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "OrderRefundId" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "OrderItemId" uuid NOT NULL,
        "Amount" numeric(18,2) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_OrderRefundLines" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_OrderRefundLines_Amount_NonNegative" CHECK ("Amount" >= 0),
        CONSTRAINT "FK_OrderRefundLines_OrderItems_OrderId_OrderItemId" FOREIGN KEY ("OrderId", "OrderItemId") REFERENCES "OrderItems" ("OrderId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_OrderRefundLines_OrderRefunds_TenantId_OrderId_OrderRefundId" FOREIGN KEY ("TenantId", "OrderId", "OrderRefundId") REFERENCES "OrderRefunds" ("TenantId", "OrderId", "Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubPayoutItems" (
        "Id" uuid NOT NULL,
        "PayoutId" uuid NOT NULL,
        "EarningId" uuid NOT NULL,
        "Amount" numeric(18,2) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CommerceHubPayoutItems" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CommerceHubPayoutItems_CommerceHubPayouts_PayoutId" FOREIGN KEY ("PayoutId") REFERENCES "CommerceHubPayouts" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_CommerceHubPayoutItems_CommerceHubVendorEarnings_EarningId" FOREIGN KEY ("EarningId") REFERENCES "CommerceHubVendorEarnings" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubRefundEvidenceMedia" (
        "Id" uuid NOT NULL,
        "RefundRequestId" uuid,
        "CustomerId" uuid NOT NULL,
        "ObjectKey" character varying(500) NOT NULL,
        "OriginalFileName" character varying(255) NOT NULL,
        "ContentType" character varying(255) NOT NULL,
        "MediaType" character varying(32) NOT NULL,
        "SizeBytes" bigint NOT NULL,
        "ExpectedSizeBytes" bigint NOT NULL,
        "ContentHash" character varying(128),
        "MetadataJson" jsonb,
        "ThumbnailObjectKey" character varying(500),
        "IsTemporary" boolean NOT NULL,
        "IsFlagged" boolean NOT NULL,
        "ExpiresAtUtc" timestamp with time zone NOT NULL,
        "UploadedAtUtc" timestamp with time zone,
        "AttachedAtUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CommerceHubRefundEvidenceMedia" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CommerceHubRefundEvidenceMedia_CommerceHubCustomers_Custome~" FOREIGN KEY ("CustomerId") REFERENCES "CommerceHubCustomers" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CommerceHubRefundEvidenceMedia_CommerceHubRefundRequests_Re~" FOREIGN KEY ("RefundRequestId") REFERENCES "CommerceHubRefundRequests" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubRefundExecutions" (
        "Id" uuid NOT NULL,
        "RefundId" uuid NOT NULL,
        "Method" integer NOT NULL,
        "AccountSnapshot" character varying(500) NOT NULL,
        "TransactionReference" character varying(200) NOT NULL,
        "Amount" numeric(18,2) NOT NULL,
        "Currency" character varying(10) NOT NULL,
        "ExecutedBy" uuid NOT NULL,
        "ExecutedAt" timestamp with time zone NOT NULL,
        "Notes" character varying(1000),
        "IsSuccessful" boolean NOT NULL,
        "FailureReason" character varying(2000),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CommerceHubRefundExecutions" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CommerceHubRefundExecutions_Currency_EGP" CHECK ("Currency" = 'EGP'),
        CONSTRAINT "FK_CommerceHubRefundExecutions_CommerceHubRefundRequests_Refun~" FOREIGN KEY ("RefundId") REFERENCES "CommerceHubRefundRequests" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CommerceHubReturnRequests" (
        "Id" uuid NOT NULL,
        "RefundId" uuid NOT NULL,
        "OrderItemId" uuid NOT NULL,
        "CustomerId" uuid NOT NULL,
        "VendorId" uuid NOT NULL,
        "Status" integer NOT NULL,
        "PickupScheduledAt" timestamp with time zone,
        "PickedUpAt" timestamp with time zone,
        "InTransitAt" timestamp with time zone,
        "DeliveredAt" timestamp with time zone,
        "ConfirmedAt" timestamp with time zone,
        "TrackingNumber" character varying(200),
        "CourierName" character varying(200),
        "Notes" character varying(1000),
        "RequestedQuantity" integer NOT NULL,
        "ReceivedResellableQuantity" integer NOT NULL,
        "ReceivedDamagedQuantity" integer NOT NULL,
        "EscalatedAtUtc" timestamp with time zone,
        "EscalationLevel" integer NOT NULL,
        "EscalationReason" character varying(1000),
        "ExpectedCompletionAtUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CommerceHubReturnRequests" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CommerceHubReturnRequests_ReceivedDamagedQuantity_NonNegati~" CHECK ("ReceivedDamagedQuantity" >= 0),
        CONSTRAINT "CK_CommerceHubReturnRequests_ReceivedQuantity_Within_Requested" CHECK ("ReceivedResellableQuantity" + "ReceivedDamagedQuantity" <= "RequestedQuantity"),
        CONSTRAINT "CK_CommerceHubReturnRequests_ReceivedResellableQuantity_NonNeg~" CHECK ("ReceivedResellableQuantity" >= 0),
        CONSTRAINT "CK_CommerceHubReturnRequests_RequestedQuantity_Positive" CHECK ("RequestedQuantity" > 0),
        CONSTRAINT "FK_CommerceHubReturnRequests_CommerceHubCustomers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "CommerceHubCustomers" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CommerceHubReturnRequests_CommerceHubRefundRequests_RefundId" FOREIGN KEY ("RefundId") REFERENCES "CommerceHubRefundRequests" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CommerceHubReturnRequests_CommerceHubSubOrderItems_OrderIte~" FOREIGN KEY ("OrderItemId") REFERENCES "CommerceHubSubOrderItems" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CustomerAddresses" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CustomerId" uuid NOT NULL,
        "CurrentVersionId" uuid,
        "IsDefault" boolean NOT NULL,
        "IsArchived" boolean NOT NULL,
        "Version" integer NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CustomerAddresses" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CustomerAddresses_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE TABLE "CustomerAddressVersions" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CustomerAddressId" uuid NOT NULL,
        "Version" integer NOT NULL,
        "AddressAdmissionEvidenceId" uuid NOT NULL,
        "GeographicCatalogReleaseId" uuid NOT NULL,
        "CountryPlaceId" uuid NOT NULL,
        "AdministrativeAreaPlaceId" uuid,
        "LocalityPlaceId" uuid,
        "DistrictPlaceId" uuid,
        "ZonePlaceId" uuid,
        "CountryDisplayName" character varying(200),
        "AdministrativeAreaDisplayName" character varying(200),
        "LocalityDisplayName" character varying(200),
        "DistrictDisplayName" character varying(200),
        "ZoneDisplayName" character varying(200),
        "Street" character varying(500) NOT NULL,
        "Street2" character varying(500),
        "Building" character varying(120),
        "Floor" character varying(64),
        "Apartment" character varying(64),
        "Landmark" character varying(200),
        "PostalCode" character varying(32),
        "LegacyAddressText" character varying(1000),
        "AdmittedAtUtc" timestamp with time zone NOT NULL,
        "SnapshotCreatedAtUtc" timestamp with time zone NOT NULL,
        "SnapshotVersion" integer NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CustomerAddressVersions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CustomerAddressVersions_CustomerAddresses_CustomerAddressId" FOREIGN KEY ("CustomerAddressId") REFERENCES "CustomerAddresses" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    INSERT INTO "CommerceHubCategories" ("Id", "CreatedAtUtc", "IconEmoji", "IsActive", "Level", "Name", "NameAr", "ParentId", "Slug", "SortOrder", "UpdatedAtUtc")
    VALUES ('10000000-0000-0000-0000-000000000001', TIMESTAMPTZ '2026-01-01T00:00:00Z', '📱', TRUE, 0, 'Electronics', 'إلكترونيات', NULL, 'electronics', 1, NULL);
    INSERT INTO "CommerceHubCategories" ("Id", "CreatedAtUtc", "IconEmoji", "IsActive", "Level", "Name", "NameAr", "ParentId", "Slug", "SortOrder", "UpdatedAtUtc")
    VALUES ('10000000-0000-0000-0000-000000000002', TIMESTAMPTZ '2026-01-01T00:00:00Z', '👗', TRUE, 0, 'Fashion', 'أزياء', NULL, 'fashion', 2, NULL);
    INSERT INTO "CommerceHubCategories" ("Id", "CreatedAtUtc", "IconEmoji", "IsActive", "Level", "Name", "NameAr", "ParentId", "Slug", "SortOrder", "UpdatedAtUtc")
    VALUES ('10000000-0000-0000-0000-000000000003', TIMESTAMPTZ '2026-01-01T00:00:00Z', '🏠', TRUE, 0, 'Home & Garden', 'المنزل والحديقة', NULL, 'home-garden', 3, NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    INSERT INTO "ThemePresets" ("Id", "BaseThemeJson", "CreatedAtUtc", "IsRtl", "Key", "Name", "PreviewImageUrl")
    VALUES ('11111111-1111-1111-1111-111111111111', '{"colors":{"primary":"#2563eb","secondary":"#0f172a","background":"#ffffff","text":"#020617","muted":"#64748b"},"typography":{"fontFamily":"Inter","baseSize":"16px"},"radius":{"base":"8px"}}', TIMESTAMPTZ '2024-01-01T00:00:00Z', FALSE, 'default', 'Default Modern', NULL);
    INSERT INTO "ThemePresets" ("Id", "BaseThemeJson", "CreatedAtUtc", "IsRtl", "Key", "Name", "PreviewImageUrl")
    VALUES ('22222222-2222-2222-2222-222222222222', '{"colors":{"primary":"#3b82f6","secondary":"#1e293b","background":"#0f172a","text":"#f8fafc","muted":"#94a3b8"},"typography":{"fontFamily":"Inter","baseSize":"16px"},"radius":{"base":"12px"}}', TIMESTAMPTZ '2024-01-01T00:00:00Z', FALSE, 'dark-minimal', 'Dark Minimal', NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    INSERT INTO "CommerceHubCategories" ("Id", "CreatedAtUtc", "IconEmoji", "IsActive", "Level", "Name", "NameAr", "ParentId", "Slug", "SortOrder", "UpdatedAtUtc")
    VALUES ('10000000-0000-0000-0000-000000000011', TIMESTAMPTZ '2026-01-01T00:00:00Z', '📺', TRUE, 1, 'TVs', 'تلفزيونات', '10000000-0000-0000-0000-000000000001', 'tvs', 1, NULL);
    INSERT INTO "CommerceHubCategories" ("Id", "CreatedAtUtc", "IconEmoji", "IsActive", "Level", "Name", "NameAr", "ParentId", "Slug", "SortOrder", "UpdatedAtUtc")
    VALUES ('10000000-0000-0000-0000-000000000012', TIMESTAMPTZ '2026-01-01T00:00:00Z', '📱', TRUE, 1, 'Mobile Phones', 'هواتف محمولة', '10000000-0000-0000-0000-000000000001', 'mobile-phones', 2, NULL);
    INSERT INTO "CommerceHubCategories" ("Id", "CreatedAtUtc", "IconEmoji", "IsActive", "Level", "Name", "NameAr", "ParentId", "Slug", "SortOrder", "UpdatedAtUtc")
    VALUES ('10000000-0000-0000-0000-000000000013', TIMESTAMPTZ '2026-01-01T00:00:00Z', '💻', TRUE, 1, 'Laptops', 'لابتوب', '10000000-0000-0000-0000-000000000001', 'laptops', 3, NULL);
    INSERT INTO "CommerceHubCategories" ("Id", "CreatedAtUtc", "IconEmoji", "IsActive", "Level", "Name", "NameAr", "ParentId", "Slug", "SortOrder", "UpdatedAtUtc")
    VALUES ('10000000-0000-0000-0000-000000000021', TIMESTAMPTZ '2026-01-01T00:00:00Z', '👔', TRUE, 1, 'Men', 'رجالي', '10000000-0000-0000-0000-000000000002', 'mens-fashion', 1, NULL);
    INSERT INTO "CommerceHubCategories" ("Id", "CreatedAtUtc", "IconEmoji", "IsActive", "Level", "Name", "NameAr", "ParentId", "Slug", "SortOrder", "UpdatedAtUtc")
    VALUES ('10000000-0000-0000-0000-000000000022', TIMESTAMPTZ '2026-01-01T00:00:00Z', '👗', TRUE, 1, 'Women', 'نسائي', '10000000-0000-0000-0000-000000000002', 'womens-fashion', 2, NULL);
    INSERT INTO "CommerceHubCategories" ("Id", "CreatedAtUtc", "IconEmoji", "IsActive", "Level", "Name", "NameAr", "ParentId", "Slug", "SortOrder", "UpdatedAtUtc")
    VALUES ('10000000-0000-0000-0000-000000000031', TIMESTAMPTZ '2026-01-01T00:00:00Z', '🛋️', TRUE, 1, 'Furniture', 'أثاث', '10000000-0000-0000-0000-000000000003', 'furniture', 1, NULL);
    INSERT INTO "CommerceHubCategories" ("Id", "CreatedAtUtc", "IconEmoji", "IsActive", "Level", "Name", "NameAr", "ParentId", "Slug", "SortOrder", "UpdatedAtUtc")
    VALUES ('10000000-0000-0000-0000-000000000032', TIMESTAMPTZ '2026-01-01T00:00:00Z', '🍳', TRUE, 1, 'Kitchen', 'مطبخ', '10000000-0000-0000-0000-000000000003', 'kitchen', 2, NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    INSERT INTO "CommerceHubCategoryAttributes" ("Id", "CategoryId", "IsFilterable", "IsRequired", "IsSearchable", "Name", "NameAr", "OptionsJson", "SortOrder", "Type")
    VALUES ('20000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000011', TRUE, TRUE, TRUE, 'Brand', 'الماركة', '["Samsung","LG","Sony","TCL","Hisense","Toshiba","Panasonic"]', 1, 'select');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    INSERT INTO "CommerceHubCategoryAttributes" ("Id", "CategoryId", "IsFilterable", "IsRequired", "Name", "NameAr", "OptionsJson", "SortOrder", "Type")
    VALUES ('20000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000011', TRUE, TRUE, 'Screen Size (inches)', 'حجم الشاشة (بوصة)', NULL, 2, 'number');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    INSERT INTO "CommerceHubCategoryAttributes" ("Id", "CategoryId", "IsFilterable", "IsRequired", "IsSearchable", "Name", "NameAr", "OptionsJson", "SortOrder", "Type")
    VALUES ('20000000-0000-0000-0000-000000000003', '10000000-0000-0000-0000-000000000011', TRUE, FALSE, TRUE, 'Resolution', 'الدقة', '["HD","Full HD","4K UHD","8K"]', 3, 'select');
    INSERT INTO "CommerceHubCategoryAttributes" ("Id", "CategoryId", "IsFilterable", "IsRequired", "IsSearchable", "Name", "NameAr", "OptionsJson", "SortOrder", "Type")
    VALUES ('20000000-0000-0000-0000-000000000011', '10000000-0000-0000-0000-000000000012', TRUE, TRUE, TRUE, 'Brand', 'الماركة', '["Apple","Samsung","Xiaomi","Huawei","Oppo","Vivo","Realme","OnePlus"]', 1, 'select');
    INSERT INTO "CommerceHubCategoryAttributes" ("Id", "CategoryId", "IsFilterable", "IsRequired", "IsSearchable", "Name", "NameAr", "OptionsJson", "SortOrder", "Type")
    VALUES ('20000000-0000-0000-0000-000000000012', '10000000-0000-0000-0000-000000000012', TRUE, TRUE, TRUE, 'Storage', 'السعة التخزينية', '["32GB","64GB","128GB","256GB","512GB","1TB"]', 2, 'select');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    INSERT INTO "CommerceHubCategoryAttributes" ("Id", "CategoryId", "IsFilterable", "IsRequired", "Name", "NameAr", "OptionsJson", "SortOrder", "Type")
    VALUES ('20000000-0000-0000-0000-000000000013', '10000000-0000-0000-0000-000000000012', TRUE, FALSE, 'RAM', 'الذاكرة العشوائية', '["2GB","3GB","4GB","6GB","8GB","12GB","16GB"]', 3, 'select');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    INSERT INTO "CommerceHubCategoryAttributes" ("Id", "CategoryId", "IsFilterable", "IsRequired", "IsSearchable", "Name", "NameAr", "OptionsJson", "SortOrder", "Type")
    VALUES ('20000000-0000-0000-0000-000000000021', '10000000-0000-0000-0000-000000000013', TRUE, TRUE, TRUE, 'Brand', 'الماركة', '["Apple","Dell","HP","Lenovo","ASUS","Acer","MSI"]', 1, 'select');
    INSERT INTO "CommerceHubCategoryAttributes" ("Id", "CategoryId", "IsFilterable", "IsRequired", "IsSearchable", "Name", "NameAr", "OptionsJson", "SortOrder", "Type")
    VALUES ('20000000-0000-0000-0000-000000000022', '10000000-0000-0000-0000-000000000013', TRUE, FALSE, TRUE, 'Processor', 'المعالج', '["Intel Core i3","Intel Core i5","Intel Core i7","Intel Core i9","AMD Ryzen 5","AMD Ryzen 7","Apple M1","Apple M2","Apple M3"]', 2, 'select');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    INSERT INTO "CommerceHubCategoryAttributes" ("Id", "CategoryId", "IsFilterable", "IsRequired", "Name", "NameAr", "OptionsJson", "SortOrder", "Type")
    VALUES ('20000000-0000-0000-0000-000000000023', '10000000-0000-0000-0000-000000000013', TRUE, FALSE, 'RAM', 'الذاكرة العشوائية', '["4GB","8GB","16GB","32GB","64GB"]', 3, 'select');
    INSERT INTO "CommerceHubCategoryAttributes" ("Id", "CategoryId", "IsFilterable", "IsRequired", "Name", "NameAr", "OptionsJson", "SortOrder", "Type")
    VALUES ('20000000-0000-0000-0000-000000000031', '10000000-0000-0000-0000-000000000021', TRUE, TRUE, 'Size', 'المقاس', '["XS","S","M","L","XL","XXL","3XL"]', 1, 'select');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    INSERT INTO "CommerceHubCategoryAttributes" ("Id", "CategoryId", "IsFilterable", "IsRequired", "IsSearchable", "Name", "NameAr", "OptionsJson", "SortOrder", "Type")
    VALUES ('20000000-0000-0000-0000-000000000032', '10000000-0000-0000-0000-000000000021', TRUE, FALSE, TRUE, 'Color', 'اللون', NULL, 2, 'text');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AbandonedCartRecoveryLogs_AbandonedCartId" ON "AbandonedCartRecoveryLogs" ("AbandonedCartId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AbandonedCartRecoveryLogs_TenantId" ON "AbandonedCartRecoveryLogs" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_AbandonedCartRecoveryLogs_TenantId_IdempotencyKey_Unique" ON "AbandonedCartRecoveryLogs" ("TenantId", "IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AbandonedCarts_CartId" ON "AbandonedCarts" ("CartId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AbandonedCarts_CustomerId" ON "AbandonedCarts" ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AbandonedCarts_RecoveryStatus_LastActivityAtUtc" ON "AbandonedCarts" ("RecoveryStatus", "LastActivityAtUtc") WHERE "RecoveryStatus" = 'Active';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AbandonedCarts_TenantId" ON "AbandonedCarts" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_AbandonedCarts_TenantId_CartId" ON "AbandonedCarts" ("TenantId", "CartId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AbandonedCarts_TenantId_SessionId" ON "AbandonedCarts" ("TenantId", "SessionId") WHERE "SessionId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AccountingOutbox_AggregateVersionBlockers" ON "AccountingOutboxMessages" ("TenantId", "BusinessEntityId", "AggregateVersion", "OccurredAtUtc", "CreatedAt", "Id", "ProcessedAtUtc", "IsFailed", "EventType") WHERE "ProcessedAtUtc" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AccountingOutbox_AggregateVersionOrdering" ON "AccountingOutboxMessages" ("TenantId", "BusinessEntityId", "AggregateVersion", "OccurredAtUtc", "CreatedAt", "Id") WHERE "ProcessedAtUtc" IS NULL AND "IsFailed" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AccountingOutbox_Failed" ON "AccountingOutboxMessages" ("TenantId", "IsFailed") WHERE "IsFailed" = true;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AccountingOutbox_OccurredAtUtc" ON "AccountingOutboxMessages" ("OccurredAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AccountingOutbox_ProcessingStatus" ON "AccountingOutboxMessages" ("ProcessedAtUtc", "IsFailed", "NextRetryAtUtc", "ClaimedUntilUtc") WHERE "ProcessedAtUtc" IS NULL AND "IsFailed" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AccountingOutbox_Tenant_SourceType_Entity" ON "AccountingOutboxMessages" ("TenantId", "SourceType", "BusinessEntityId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_AccountingOutbox_TenantId_IdempotencyKey" ON "AccountingOutboxMessages" ("TenantId", "IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN

    DO $$
    BEGIN
        IF EXISTS (
            SELECT 1
            FROM "AccountLedgers"
            WHERE "IsDeleted" = false
              AND "PartyType" = 0
              AND "TransactionType" = 11
            GROUP BY "TenantId", "PartyType", "PartyId", "TransactionType"
            HAVING COUNT(*) > 1
        ) THEN
            RAISE EXCEPTION 'duplicate active customer opening-balance ledger rows exist';
        END IF;
    END
    $$;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_AccountLedgers_CustomerOpeningBalance_Once" ON "AccountLedgers" ("TenantId", "PartyType", "PartyId", "TransactionType") WHERE "IsDeleted" = false AND "PartyType" = 0 AND "TransactionType" = 11;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AccountLedgers_UserId" ON "AccountLedgers" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AddressAdmissionEvidence_AdministrativeAreaPlaceId" ON "AddressAdmissionEvidence" ("AdministrativeAreaPlaceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AddressAdmissionEvidence_CountryPlaceId" ON "AddressAdmissionEvidence" ("CountryPlaceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AddressAdmissionEvidence_DistrictPlaceId" ON "AddressAdmissionEvidence" ("DistrictPlaceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AddressAdmissionEvidence_Lineage" ON "AddressAdmissionEvidence" ("CatalogReleaseId", "CountryPlaceId", "AdministrativeAreaPlaceId", "LocalityPlaceId", "DistrictPlaceId", "ZonePlaceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AddressAdmissionEvidence_LocalityPlaceId" ON "AddressAdmissionEvidence" ("LocalityPlaceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AddressAdmissionEvidence_Owner_Fingerprint" ON "AddressAdmissionEvidence" ("TenantId", "OwnerDomain", "OwnerId", "AddressFingerprintSha256");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AddressAdmissionEvidence_ZonePlaceId" ON "AddressAdmissionEvidence" ("ZonePlaceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AddressNormalizationRules_Active" ON "AddressNormalizationRules" ("CountryPlaceId", "LanguageTag", "RuleKind", "IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AddressNormalizationRules_IntroducedCatalogReleaseId" ON "AddressNormalizationRules" ("IntroducedCatalogReleaseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AddressNormalizationRules_RetiredCatalogReleaseId" ON "AddressNormalizationRules" ("RetiredCatalogReleaseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_AddressNormalizationRule_Version" ON "AddressNormalizationRules" ("CountryPlaceId", "LanguageTag", "RuleKind", "RuleVersion");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliateAccounts_TenantId" ON "AffiliateAccounts" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_AffiliateAccounts_ReferralCode_Global" ON "AffiliateAccounts" ("ReferralCode");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_AffiliateAccounts_Tenant_ReferralCode" ON "AffiliateAccounts" ("TenantId", "ReferralCode");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_AffiliateAccounts_Tenant_User" ON "AffiliateAccounts" ("TenantId", "UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliateAttributions_AffiliateAccountId" ON "AffiliateAttributions" ("AffiliateAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliateAttributions_OrderId" ON "AffiliateAttributions" ("OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliateAttributions_PromotionLinkId" ON "AffiliateAttributions" ("PromotionLinkId") WHERE "PromotionLinkId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliateAttributions_Tenant_Order_Active" ON "AffiliateAttributions" ("TenantId", "OrderId", "IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliateAttributions_TenantId" ON "AffiliateAttributions" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_AffiliateAttributions_Tenant_Order" ON "AffiliateAttributions" ("TenantId", "OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliateBalanceLedger_AffiliateAccountId" ON "AffiliateBalanceLedger" ("AffiliateAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliateBalanceLedger_ReferenceId" ON "AffiliateBalanceLedger" ("ReferenceId") WHERE "ReferenceId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliateBalanceLedger_StoreTenantRecorded" ON "AffiliateBalanceLedger" ("StoreTenantId", "RecordedAtUtc" DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_AffiliateBalanceLedger_Idempotency" ON "AffiliateBalanceLedger" ("IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliateCategories_DisplayOrder" ON "AffiliateCategories" ("DisplayOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliateCategories_IsActive" ON "AffiliateCategories" ("IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliateCategories_ParentId" ON "AffiliateCategories" ("ParentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_AffiliateCategories_Parent_Name" ON "AffiliateCategories" ("ParentId", "Name");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_AffiliateCategories_Slug" ON "AffiliateCategories" ("Slug");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliateCommissionLedger_AttributionId" ON "AffiliateCommissionLedger" ("AttributionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliateCommissionLedger_OriginalEntryId" ON "AffiliateCommissionLedger" ("OriginalEntryId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliateCommLedger_AffiliateAccountId" ON "AffiliateCommissionLedger" ("AffiliateAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliateCommLedger_OrderId" ON "AffiliateCommissionLedger" ("OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliateCommLedger_OrderItemEntryType" ON "AffiliateCommissionLedger" ("OrderItemId", "EntryType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliateCommLedger_TenantId" ON "AffiliateCommissionLedger" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliateCommLedger_TenantRecorded" ON "AffiliateCommissionLedger" ("TenantId", "RecordedAtUtc" DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_AffiliateCommLedger_Idempotency" ON "AffiliateCommissionLedger" ("IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliateEmailVerificationTokens_ExpiresAtUtc" ON "AffiliateEmailVerificationTokens" ("ExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_AffiliateEmailVerificationTokens_TokenHash" ON "AffiliateEmailVerificationTokens" ("TokenHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliateEmailVerificationTokens_User_Consumed" ON "AffiliateEmailVerificationTokens" ("AffiliateUserId", "ConsumedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliatePasswordResetTokens_ExpiresAtUtc" ON "AffiliatePasswordResetTokens" ("ExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_AffiliatePasswordResetTokens_TokenHash" ON "AffiliatePasswordResetTokens" ("TokenHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliatePasswordResetTokens_User_Consumed" ON "AffiliatePasswordResetTokens" ("AffiliateUserId", "ConsumedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliatePayoutRequests_AffiliateId_Status" ON "AffiliatePayoutRequests" ("AffiliateAccountId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliatePayoutRequests_TenantId_AffiliateId_Status" ON "AffiliatePayoutRequests" ("StoreTenantId", "AffiliateAccountId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliatePayoutRequests_TenantId_Status" ON "AffiliatePayoutRequests" ("StoreTenantId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliatePayoutTransferAllocation_EvidenceId" ON "AffiliatePayoutTransferAllocations" ("EvidenceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliatePayoutTransferAllocation_Tenant_CreatedAt" ON "AffiliatePayoutTransferAllocations" ("StoreTenantId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_AffiliatePayoutTransferAllocation_Idempotency" ON "AffiliatePayoutTransferAllocations" ("IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliatePayoutTransferEvidence_Tenant_CreatedAt" ON "AffiliatePayoutTransferEvidence" ("StoreTenantId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_AffiliatePayoutTransferEvidence_Rail_Reference" ON "AffiliatePayoutTransferEvidence" ("RailNamespace", "NormalizedPaymentReference");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliateProductPolicies_AffiliateCategoryId" ON "AffiliateProductPolicies" ("AffiliateCategoryId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliateProductPolicies_ProductId" ON "AffiliateProductPolicies" ("ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliateProductPolicies_TenantId" ON "AffiliateProductPolicies" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_AffiliateProductPolicies_Tenant_Product" ON "AffiliateProductPolicies" ("TenantId", "ProductId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliatePromotionLinks_AffiliateUserId" ON "AffiliatePromotionLinks" ("AffiliateUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliatePromotionLinks_Code_Store" ON "AffiliatePromotionLinks" ("ReferralCode", "TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliatePromotionLinks_ProductId" ON "AffiliatePromotionLinks" ("ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliatePromotionLinks_TenantId" ON "AffiliatePromotionLinks" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_AffiliatePromotionLinks_ReferralCode" ON "AffiliatePromotionLinks" ("ReferralCode") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_AffiliatePromotionLinks_User_Store_Product" ON "AffiliatePromotionLinks" ("AffiliateUserId", "TenantId", "ProductId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliateRefreshTokenReplayEntries_ReplayedAtUtc" ON "AffiliateRefreshTokenReplayEntries" ("ReplayedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_AffiliateRefreshTokenReplayEntries_TokenHash" ON "AffiliateRefreshTokenReplayEntries" ("TokenHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliateRefreshTokenReplayEntries_User_Expires" ON "AffiliateRefreshTokenReplayEntries" ("AffiliateUserId", "ExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AffiliateUsers_TenantId" ON "AffiliateUsers" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_AffiliateUsers_AffiliateAccount" ON "AffiliateUsers" ("AffiliateAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_AffiliateUsers_Email_Tenant" ON "AffiliateUsers" ("TenantId", "Email");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AnalyticsAlertRules_TenantId" ON "AnalyticsAlertRules" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AnalyticsAlertRules_TenantId_IsEnabled_IsDeleted" ON "AnalyticsAlertRules" ("TenantId", "IsEnabled", "IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AuditLogs_ActionType" ON "AuditLogs" ("ActionType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AuditLogs_EntityType" ON "AuditLogs" ("EntityType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AuditLogs_TimestampUtc" ON "AuditLogs" ("TimestampUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_Banks_TenantId_BankName" ON "Banks" ("TenantId", "BankName") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_BankTransactions_BankId" ON "BankTransactions" ("BankId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CampaignCosts_TenantId" ON "CampaignCosts" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CampaignCosts_TenantId_PeriodStartUtc_PeriodEndUtc" ON "CampaignCosts" ("TenantId", "PeriodStartUtc", "PeriodEndUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CampaignCosts_TenantId_Source_Medium_Campaign_PeriodStartUtc" ON "CampaignCosts" ("TenantId", "Source", "Medium", "Campaign", "PeriodStartUtc") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CanonicalIdentityLinks_CanonicalIdentityId" ON "CanonicalIdentityLinks" ("CanonicalIdentityId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CanonicalIdentityLinks_Source_DisplayKey" ON "CanonicalIdentityLinks" ("SourceSystem", "DisplayKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CanonicalIdentityLinks_Tenant_Subject" ON "CanonicalIdentityLinks" ("TenantId", "SubjectType", "SubjectId") WHERE "TenantId" IS NOT NULL AND "IsDeleted" = false AND "RevokedAtUtc" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_CanonicalIdentityLinks_Global_Subject_Active" ON "CanonicalIdentityLinks" ("SubjectType", "SubjectId") WHERE "TenantId" IS NULL AND "IsDeleted" = false AND "RevokedAtUtc" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CarrierAccountPolicies_TenantId_CarrierAccountId_Scope" ON "CarrierAccountPolicies" ("TenantId", "CarrierAccountId", "Scope") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CarrierAccounts_TenantId_CarrierCode" ON "CarrierAccounts" ("TenantId", "CarrierCode") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CarrierAdministrationAudit_TenantId_CarrierAccountId_Occurr~" ON "CarrierAdministrationAudit" ("TenantId", "CarrierAccountId", "OccurredAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CarrierConfigurations_TenantId_CarrierCode" ON "CarrierConfigurations" ("TenantId", "CarrierCode") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CarrierConfigurations_TenantId_IsDefault" ON "CarrierConfigurations" ("TenantId", "IsDefault") WHERE "IsDefault" = true;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CarrierConfigurations_TenantId_IsEnabled_SortOrder" ON "CarrierConfigurations" ("TenantId", "IsEnabled", "SortOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CarrierConnectionTestRecords_TenantId_CarrierAccountId_Stat~" ON "CarrierConnectionTestRecords" ("TenantId", "CarrierAccountId", "Status", "TestedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CarrierCredentialReferences_TenantId_CarrierAccountId_State" ON "CarrierCredentialReferences" ("TenantId", "CarrierAccountId", "State");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_CarrierGeoGroup_Key" ON "CarrierGeographicMappingGroups" ("MappingReleaseId", "GroupKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CarrierGeoPublications_Release_Published" ON "CarrierGeographicMappingPublications" ("MappingReleaseId", "PublishedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CarrierGeographicMappingReleases_CatalogReleaseId" ON "CarrierGeographicMappingReleases" ("CatalogReleaseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CarrierGeographicMappingReleases_RollbackOfReleaseId" ON "CarrierGeographicMappingReleases" ("RollbackOfReleaseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CarrierGeographicMappingReleases_SupersedesReleaseId" ON "CarrierGeographicMappingReleases" ("SupersedesReleaseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CarrierGeoRelease_ActiveScope" ON "CarrierGeographicMappingReleases" ("CarrierProviderId", "Environment", "AccountScopeKind", "AccountScopeId", "Status", "EffectiveFromUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_CarrierGeoRelease_Key" ON "CarrierGeographicMappingReleases" ("ReleaseKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_CarrierGeoRelease_Number" ON "CarrierGeographicMappingReleases" ("CarrierProviderId", "Environment", "AccountScopeKind", "AccountScopeId", "ReleaseNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CarrierGeographicMappings_CanonicalPlaceId" ON "CarrierGeographicMappings" ("CanonicalPlaceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CarrierGeographicMappings_CarrierProviderId" ON "CarrierGeographicMappings" ("CarrierProviderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CarrierGeographicMappings_CountryPlaceId" ON "CarrierGeographicMappings" ("CountryPlaceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CarrierGeographicMappings_MappingGroupId" ON "CarrierGeographicMappings" ("MappingGroupId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CarrierGeographicMappings_ProviderParentMappingId" ON "CarrierGeographicMappings" ("ProviderParentMappingId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CarrierGeoMapping_Resolution" ON "CarrierGeographicMappings" ("MappingReleaseId", "CanonicalPlaceId", "ProviderPlaceType", "MappingStatus");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_CarrierGeoMapping_CanonicalProvider" ON "CarrierGeographicMappings" ("MappingReleaseId", "CanonicalPlaceId", "ProviderPlaceType", "ProviderPlaceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_CarrierGeoMapping_ProviderId" ON "CarrierGeographicMappings" ("MappingReleaseId", "ProviderPlaceType", "ProviderPlaceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CarrierGeographicOverrides_CanonicalPlaceId" ON "CarrierGeographicOverrides" ("CanonicalPlaceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CarrierGeoOverrides_Scope_Place_Status" ON "CarrierGeographicOverrides" ("CarrierProviderId", "Environment", "AccountScopeKind", "AccountScopeId", "TenantId", "CanonicalPlaceId", "ProviderPlaceType", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CarrierGeographicSyncCandidates_CandidateCanonicalPlaceId" ON "CarrierGeographicSyncCandidates" ("CandidateCanonicalPlaceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CarrierGeoSyncCandidates_Review" ON "CarrierGeographicSyncCandidates" ("CarrierProviderId", "CandidateStatus", "Confidence");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_CarrierGeoSyncCandidate_Node" ON "CarrierGeographicSyncCandidates" ("SyncRunId", "ProviderPlaceType", "ProviderPlaceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CarrierGeoSyncRuns_Scope_Status" ON "CarrierGeographicSyncRuns" ("CarrierProviderId", "Environment", "AccountScopeKind", "AccountScopeId", "StartedAtUtc", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_CarrierProvider_Code" ON "CarrierProviders" ("CarrierCode");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CarrierServiceLevelEnablements_TenantId_CarrierAccountId_Se~" ON "CarrierServiceLevelEnablements" ("TenantId", "CarrierAccountId", "ServiceLevelCode") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CarrierWarehouseAssignments_TenantId_CarrierAccountId_Wareh~" ON "CarrierWarehouseAssignments" ("TenantId", "CarrierAccountId", "WarehouseId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Cashboxes_IsActive" ON "Cashboxes" ("IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Cashboxes_OpenedByUserId" ON "Cashboxes" ("OpenedByUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Cashboxes_WarehouseId" ON "Cashboxes" ("WarehouseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Cashboxes_WarehouseId_IsActive" ON "Cashboxes" ("WarehouseId", "IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CashboxTransactions_CashboxId" ON "CashboxTransactions" ("CashboxId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CashboxTransactions_CashboxId_TransactionDate" ON "CashboxTransactions" ("CashboxId", "TransactionDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CashboxTransactions_ReferenceId" ON "CashboxTransactions" ("ReferenceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CashboxTransactions_TransactionDate" ON "CashboxTransactions" ("TransactionDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CashboxTransactions_TransactionType" ON "CashboxTransactions" ("TransactionType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CashboxTransactions_UserId" ON "CashboxTransactions" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Categories_ParentCategoryId" ON "Categories" ("ParentCategoryId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CheckoutSessions_CartId" ON "CheckoutSessions" ("CartId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CheckoutSessions_RecoveryToken" ON "CheckoutSessions" ("RecoveryToken");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CheckoutSessions_Status_LastActivity" ON "CheckoutSessions" ("Status", "LastActivityAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CheckoutSessions_TenantId" ON "CheckoutSessions" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CheckoutSessions_TenantId_CartId" ON "CheckoutSessions" ("TenantId", "CartId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CheckoutSessions_TenantId_CartId_Active_Unique" ON "CheckoutSessions" ("TenantId", "CartId", "Status") WHERE "Status" = 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CheckoutSessions_TenantId_Status_AbandonedAtUtc" ON "CheckoutSessions" ("TenantId", "Status", "AbandonedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CmsPages_PageGroupId" ON "CmsPages" ("PageGroupId") WHERE "IsPublished" AND NOT "IsDeleted";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CmsPages_TenantId_IsDraft_Slug" ON "CmsPages" ("TenantId", "IsDraft", "Slug");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CmsPages_TenantId_IsPublished_Slug" ON "CmsPages" ("TenantId", "IsPublished", "Slug");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_CmsPages_PageGroupId_Version" ON "CmsPages" ("PageGroupId", "Version");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_CmsPages_Tenant_Published_Slug" ON "CmsPages" ("TenantId", "Slug") WHERE "IsPublished" AND NOT "IsDeleted";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceAudit_Business" ON "CommerceAccountingAudit" ("TenantId", "BusinessType", "BusinessId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceAudit_JournalId" ON "CommerceAccountingAudit" ("JournalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceAudit_TenantId_PostedAtUtc" ON "CommerceAccountingAudit" ("TenantId", "PostedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_CommerceHubCarts_TenantId_CommerceHubCustomerId_Active" ON "CommerceHubCarts" ("TenantId", "CommerceHubCustomerId") WHERE "CommerceHubCustomerId" IS NOT NULL AND "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubCategories_ParentId_SortOrder" ON "CommerceHubCategories" ("ParentId", "SortOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_CommerceHubCategories_Slug" ON "CommerceHubCategories" ("Slug");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubCategoryAttributes_CategoryId_SortOrder" ON "CommerceHubCategoryAttributes" ("CategoryId", "SortOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubCheckoutIdempotency_CommerceHubCustomerId" ON "CommerceHubCheckoutIdempotency" ("CommerceHubCustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CommerceHubCheckoutIdempotency_Key" ON "CommerceHubCheckoutIdempotency" ("Key");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubCheckoutIdempotency_TenantId_CommerceHubCustomerId" ON "CommerceHubCheckoutIdempotency" ("TenantId", "CommerceHubCustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubCheckoutIdempotency_TenantId_RequestHash" ON "CommerceHubCheckoutIdempotency" ("TenantId", "RequestHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CHCustomerEmailVerificationTokens_CustomerId" ON "CommerceHubCustomerEmailVerificationTokens" ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CHCustomerEmailVerificationTokens_TokenHash" ON "CommerceHubCustomerEmailVerificationTokens" ("TokenHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CHCustomerIdentityEvents_Customer_Occurred" ON "CommerceHubCustomerIdentityEvents" ("CustomerId", "OccurredAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CHCustomerIdentityEvents_EventType_Occurred" ON "CommerceHubCustomerIdentityEvents" ("EventType", "OccurredAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CHCustomerPasswordResetTokens_CustomerId" ON "CommerceHubCustomerPasswordResetTokens" ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CHCustomerPasswordResetTokens_TokenHash" ON "CommerceHubCustomerPasswordResetTokens" ("TokenHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubCustomerPayoutDetails_CustomerId_CreatedAt" ON "CommerceHubCustomerPayoutDetails" ("CustomerId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubCustomerPayoutDetails_CustomerId_Method_CreatedAt" ON "CommerceHubCustomerPayoutDetails" ("CustomerId", "Method", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubCustomers_CreatedAt" ON "CommerceHubCustomers" ("CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CommerceHubCustomers_Email" ON "CommerceHubCustomers" ("Email");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubDailyFinancialAggregates_CalculatedAtUtc" ON "CommerceHubDailyFinancialAggregates" ("CalculatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubDailyFinancialAggregates_CurrencyCode_DateUtc" ON "CommerceHubDailyFinancialAggregates" ("CurrencyCode", "DateUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubFinancialAuditLogs_CreatedAtUtc" ON "CommerceHubFinancialAuditLogs" ("CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubFinancialAuditLogs_EarningId" ON "CommerceHubFinancialAuditLogs" ("EarningId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubFinancialAuditLogs_MarketplaceOrderId" ON "CommerceHubFinancialAuditLogs" ("MarketplaceOrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubFinancialAuditLogs_PaymentId" ON "CommerceHubFinancialAuditLogs" ("PaymentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubFinancialAuditLogs_PayoutId" ON "CommerceHubFinancialAuditLogs" ("PayoutId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubFinancialAuditLogs_RefundId" ON "CommerceHubFinancialAuditLogs" ("RefundId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubFinancialAuditLogs_SubOrderId" ON "CommerceHubFinancialAuditLogs" ("SubOrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubFinancialAuditLogs_TenantId" ON "CommerceHubFinancialAuditLogs" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubFinancialAuditLogs_Type" ON "CommerceHubFinancialAuditLogs" ("Type");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CommerceHubFinancialGuardLocks_Scope" ON "CommerceHubFinancialGuardLocks" ("Scope");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubFinancialOperationAudits_CorrelationId" ON "CommerceHubFinancialOperationAudits" ("CorrelationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubFinancialOperationAudits_OperationType" ON "CommerceHubFinancialOperationAudits" ("OperationType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubFinancialOperationAudits_ReferenceType_Reference~" ON "CommerceHubFinancialOperationAudits" ("ReferenceType", "ReferenceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubFinancialOperationAudits_VendorId" ON "CommerceHubFinancialOperationAudits" ("VendorId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubFinancialReconciliationReports_Status_TimestampUtc" ON "CommerceHubFinancialReconciliationReports" ("Status", "TimestampUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubFinancialReconciliationReports_TimestampUtc" ON "CommerceHubFinancialReconciliationReports" ("TimestampUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CHFinancialTx_EntityType_Bucket_CreatedAtUtc" ON "CommerceHubFinancialTransactions" ("EntityType", "FinancialBucket", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CHFinTx_EntityType_EntityId_CreatedAtUtc" ON "CommerceHubFinancialTransactions" ("EntityType", "EntityId", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CHFinTx_EntityType_EntityId_CreatedBy_CreatedAtUtc" ON "CommerceHubFinancialTransactions" ("EntityType", "EntityId", "CreatedBy", "CreatedAtUtc") WHERE "EntityType" = 1 AND "EntityId" IS NOT NULL AND "CreatedBy" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubFinancialTransactions_CorrelationId" ON "CommerceHubFinancialTransactions" ("CorrelationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubFinancialTransactions_CreatedAtUtc" ON "CommerceHubFinancialTransactions" ("CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubFinancialTransactions_EntityId_CreatedAtUtc_Vendor" ON "CommerceHubFinancialTransactions" ("EntityId", "CreatedAtUtc") WHERE "EntityType" = 1 AND "EntityId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubFinancialTransactions_EntityId_EntityType" ON "CommerceHubFinancialTransactions" ("EntityId", "EntityType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubFinancialTransactions_EntityType_EntityId_TransactionType_CreatedAtUtc" ON "CommerceHubFinancialTransactions" ("EntityType", "EntityId", "TransactionType", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubFinancialTransactions_ReferenceId" ON "CommerceHubFinancialTransactions" ("ReferenceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubFinancialTransactions_ReferenceType_ReferenceId" ON "CommerceHubFinancialTransactions" ("ReferenceType", "ReferenceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubFinancialTransactions_ReferenceType_TransactionType_Direction_CreatedAtUtc" ON "CommerceHubFinancialTransactions" ("ReferenceType", "TransactionType", "Direction", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubFinancialTransactions_TransactionType" ON "CommerceHubFinancialTransactions" ("TransactionType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_CHFinancialTx_Fingerprint_Platform" ON "CommerceHubFinancialTransactions" ("ReferenceType", "ReferenceId", "TransactionType", "Direction", "FinancialBucket", "Amount", "Currency") WHERE "EntityId" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_CHFinancialTx_Fingerprint_Vendor" ON "CommerceHubFinancialTransactions" ("ReferenceType", "ReferenceId", "TransactionType", "EntityId", "Direction", "FinancialBucket", "Amount", "Currency") WHERE "EntityId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_CommerceHubFinancialTransactions_IdempotencyKey" ON "CommerceHubFinancialTransactions" ("IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubListings_ProductId" ON "CommerceHubListings" ("ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubListings_TenantId_IsPublished_CategoryId" ON "CommerceHubListings" ("TenantId", "IsPublished", "CommerceHubCategoryId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubListings_VariantId" ON "CommerceHubListings" ("VariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_CommerceHubListings_TenantId_VariantId" ON "CommerceHubListings" ("TenantId", "VariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubMarketplaceOrders_CreatedAt" ON "CommerceHubMarketplaceOrders" ("CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubMarketplaceOrders_CustomerId" ON "CommerceHubMarketplaceOrders" ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CommerceHubMarketplaceOrders_IdempotencyKey" ON "CommerceHubMarketplaceOrders" ("IdempotencyKey") WHERE "IdempotencyKey" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CommerceHubMarketplaceOrders_OrderNumber" ON "CommerceHubMarketplaceOrders" ("OrderNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubMarketplaceOrders_PaymentStatus_Status_CreatedAt" ON "CommerceHubMarketplaceOrders" ("PaymentStatus", "Status", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CHMerchantLifecycle_Actor_Time" ON "CommerceHubMerchantLifecycleLedgerEntries" ("ActorUserId", "OccurredAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CHMerchantLifecycle_Correlation" ON "CommerceHubMerchantLifecycleLedgerEntries" ("CorrelationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CHMerchantLifecycle_Request" ON "CommerceHubMerchantLifecycleLedgerEntries" ("RequestId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CHMerchantLifecycle_Tenant_Listing_Time" ON "CommerceHubMerchantLifecycleLedgerEntries" ("TenantId", "ListingId", "OccurredAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubPaymentAttempts_PaymentId" ON "CommerceHubPaymentAttempts" ("PaymentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CommerceHubPaymentAttempts_PaymentId_AttemptNumber" ON "CommerceHubPaymentAttempts" ("PaymentId", "AttemptNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubPaymentAttempts_Provider_ProviderIntentId" ON "CommerceHubPaymentAttempts" ("Provider", "ProviderIntentId") WHERE "ProviderIntentId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubPaymentAttempts_ProviderTransactionId" ON "CommerceHubPaymentAttempts" ("ProviderTransactionId") WHERE "ProviderTransactionId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CHPaymentCompOutbox_OperatorReconciliation" ON "CommerceHubPaymentCompensationOutboxMessages" ("RequiresOperatorReconciliation", "ProcessedAtUtc", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CHPaymentCompOutbox_PaymentId" ON "CommerceHubPaymentCompensationOutboxMessages" ("PaymentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CHPaymentCompOutbox_PaymentOrdering" ON "CommerceHubPaymentCompensationOutboxMessages" ("PaymentId", "ProcessedAtUtc", "CreatedAt", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CHPaymentCompOutbox_Processing" ON "CommerceHubPaymentCompensationOutboxMessages" ("IsFailed", "ProcessedAtUtc", "NextRetryAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CHPaymentCompOutbox_ProcessingLease" ON "CommerceHubPaymentCompensationOutboxMessages" ("ProcessedAtUtc", "IsFailed", "NextRetryAtUtc", "ProcessingLeaseExpiresAtUtc", "CreatedAt", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_CHPaymentCompOutbox_IdempotencyKey" ON "CommerceHubPaymentCompensationOutboxMessages" ("IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubPaymentItems_PaymentId" ON "CommerceHubPaymentItems" ("PaymentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CommerceHubPaymentItems_PaymentId_SubOrderId" ON "CommerceHubPaymentItems" ("PaymentId", "SubOrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubPaymentItems_SubOrderId" ON "CommerceHubPaymentItems" ("SubOrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubPaymentLogs_PaymentId" ON "CommerceHubPaymentLogs" ("PaymentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubPaymentLogs_PaymentId_ProviderEventId" ON "CommerceHubPaymentLogs" ("PaymentId", "ProviderEventId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CommerceHubPayments_OrderId_SinglePaid" ON "CommerceHubPayments" ("OrderId") WHERE "Status" = 1 AND "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubPayments_OrderId_Status" ON "CommerceHubPayments" ("OrderId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CommerceHubPayments_ProviderReference" ON "CommerceHubPayments" ("ProviderReference");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CommerceHubPayments_ProviderTransactionId" ON "CommerceHubPayments" ("ProviderTransactionId") WHERE "ProviderTransactionId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_CommerceHubPayments_Provider_ProviderIntentId" ON "CommerceHubPayments" ("Provider", "ProviderIntentId") WHERE "ProviderIntentId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubPayoutItems_EarningId" ON "CommerceHubPayoutItems" ("EarningId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubPayoutItems_PayoutId" ON "CommerceHubPayoutItems" ("PayoutId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CommerceHubPayoutItems_PayoutId_EarningId" ON "CommerceHubPayoutItems" ("PayoutId", "EarningId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubPayouts_PayoutMethodId" ON "CommerceHubPayouts" ("PayoutMethodId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CommerceHubPayouts_TenantId_IdempotencyKey" ON "CommerceHubPayouts" ("TenantId", "IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubPayouts_TenantId_Status_CreatedAt" ON "CommerceHubPayouts" ("TenantId", "Status", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_CommerceHubPayouts_ExternalTransferHandoffToken" ON "CommerceHubPayouts" ("TenantId", "ExternalTransferHandoffToken") WHERE "ExternalTransferHandoffToken" IS NOT NULL AND "ExternalTransferHandoffToken" <> '' AND "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_CommerceHubPayouts_ExternalTransferIdempotencyKey" ON "CommerceHubPayouts" ("TenantId", "ExternalTransferIdempotencyKey") WHERE "ExternalTransferIdempotencyKey" IS NOT NULL AND "ExternalTransferIdempotencyKey" <> '' AND "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_CommerceHubPayouts_Live_BankTransactionId" ON "CommerceHubPayouts" ("BankTransactionId") WHERE "BankTransactionId" IS NOT NULL AND "BankTransactionId" <> '' AND "Status" <> 3 AND "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_CommerceHubPayouts_Live_ExternalReferenceId" ON "CommerceHubPayouts" ("ExternalReferenceId") WHERE "ExternalReferenceId" IS NOT NULL AND "ExternalReferenceId" <> '' AND "Status" <> 3 AND "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_CommerceHubPayouts_OneProcessingPerTenant" ON "CommerceHubPayouts" ("TenantId") WHERE ("Status" = 1 OR "Status" = 4) AND "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CHRecovery_Actor_Time" ON "CommerceHubRecoveryOperationLedgerEntries" ("ActorUserId", "StartedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CHRecovery_Correlation" ON "CommerceHubRecoveryOperationLedgerEntries" ("CorrelationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CHRecovery_Operation_Time" ON "CommerceHubRecoveryOperationLedgerEntries" ("OperationId", "StartedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CHRecovery_Request" ON "CommerceHubRecoveryOperationLedgerEntries" ("RequestId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CHRecovery_Tenant_Listing_Time" ON "CommerceHubRecoveryOperationLedgerEntries" ("TenantId", "ListingId", "StartedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubRefundEvidenceMedia_CustomerId_ContentHash" ON "CommerceHubRefundEvidenceMedia" ("CustomerId", "ContentHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubRefundEvidenceMedia_CustomerId_CreatedAt" ON "CommerceHubRefundEvidenceMedia" ("CustomerId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubRefundEvidenceMedia_IsTemporary_ExpiresAtUtc" ON "CommerceHubRefundEvidenceMedia" ("IsTemporary", "ExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CommerceHubRefundEvidenceMedia_ObjectKey" ON "CommerceHubRefundEvidenceMedia" ("ObjectKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubRefundEvidenceMedia_RefundRequestId" ON "CommerceHubRefundEvidenceMedia" ("RefundRequestId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubRefundExecutions_ExecutedAt" ON "CommerceHubRefundExecutions" ("ExecutedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CommerceHubRefundExecutions_RefundId" ON "CommerceHubRefundExecutions" ("RefundId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubRefundRequests_CustomerId_CreatedAt" ON "CommerceHubRefundRequests" ("CustomerId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubRefundRequests_CustomerId_Status_CreatedAt" ON "CommerceHubRefundRequests" ("CustomerId", "Status", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CommerceHubRefundRequests_IdempotencyKey" ON "CommerceHubRefundRequests" ("IdempotencyKey") WHERE "IdempotencyKey" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubRefundRequests_OrderId" ON "CommerceHubRefundRequests" ("OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubRefundRequests_OrderItemId_Status_CreatedAt" ON "CommerceHubRefundRequests" ("OrderItemId", "Status", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CommerceHubRefundRequests_ReturnRequestId" ON "CommerceHubRefundRequests" ("ReturnRequestId") WHERE "ReturnRequestId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubRefundRequests_Status_CreatedAt" ON "CommerceHubRefundRequests" ("Status", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubRefundRequests_VendorId_Status_CreatedAt" ON "CommerceHubRefundRequests" ("VendorId", "Status", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubRefunds_ExternalReconciliation" ON "CommerceHubRefunds" ("TenantId", "Status", "ProcessingStartedAtUtc", "CreatedAt") WHERE ("Status" = 1 OR "Status" = 4) AND "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubRefunds_OrderItemId" ON "CommerceHubRefunds" ("OrderItemId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubRefunds_PaymentId" ON "CommerceHubRefunds" ("PaymentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CommerceHubRefunds_RefundRequestId" ON "CommerceHubRefunds" ("RefundRequestId") WHERE "RefundRequestId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubRefunds_SubOrderId_Status_CreatedAt" ON "CommerceHubRefunds" ("SubOrderId", "Status", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubRefunds_TenantId_CreatedAt" ON "CommerceHubRefunds" ("TenantId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CommerceHubRefunds_TenantId_IdempotencyKey" ON "CommerceHubRefunds" ("TenantId", "IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubRefunds_TenantId_Status_CreatedAt" ON "CommerceHubRefunds" ("TenantId", "Status", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubReturnRequests_CustomerId_Status_CreatedAt" ON "CommerceHubReturnRequests" ("CustomerId", "Status", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubReturnRequests_EscalationLevel_Status" ON "CommerceHubReturnRequests" ("EscalationLevel", "Status") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubReturnRequests_OrderItemId_Status_CreatedAt" ON "CommerceHubReturnRequests" ("OrderItemId", "Status", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CommerceHubReturnRequests_RefundId" ON "CommerceHubReturnRequests" ("RefundId") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubReturnRequests_VendorId_Status_CreatedAt" ON "CommerceHubReturnRequests" ("VendorId", "Status", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubSubOrderItems_ListingId" ON "CommerceHubSubOrderItems" ("ListingId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubSubOrderItems_SubOrderId" ON "CommerceHubSubOrderItems" ("SubOrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CommerceHubSubOrderItems_SubOrderId_VariantId" ON "CommerceHubSubOrderItems" ("SubOrderId", "VariantId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubSubOrderItems_VariantId" ON "CommerceHubSubOrderItems" ("VariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubSubOrders_CreatedAt" ON "CommerceHubSubOrders" ("CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CommerceHubSubOrders_OrderNumber" ON "CommerceHubSubOrders" ("OrderNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubSubOrders_ShippingMethodId" ON "CommerceHubSubOrders" ("ShippingMethodId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubSubOrders_StoreId_CreatedAt" ON "CommerceHubSubOrders" ("StoreId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_CommerceHubSubOrders_MarketplaceOrderId_StoreId_Live" ON "CommerceHubSubOrders" ("MarketplaceOrderId", "StoreId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubSubOrderStatusHistory_SubOrderId" ON "CommerceHubSubOrderStatusHistory" ("SubOrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubSubOrderStatusHistory_SubOrderId_ChangedAtUtc" ON "CommerceHubSubOrderStatusHistory" ("SubOrderId", "ChangedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubUnresolvedPaymentWebhooks_LocalPaymentId" ON "CommerceHubUnresolvedPaymentWebhooks" ("LocalPaymentId") WHERE "LocalPaymentId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubUnresolvedPaymentWebhooks_OrderId" ON "CommerceHubUnresolvedPaymentWebhooks" ("OrderId") WHERE "OrderId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubUnresolvedPaymentWebhooks_Tenant_Status" ON "CommerceHubUnresolvedPaymentWebhooks" ("TenantId", "Status", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_CommerceHubUnresolvedPaymentWebhooks_Provider_Event" ON "CommerceHubUnresolvedPaymentWebhooks" ("Provider", "ProviderEventId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubVendorEarnings_OriginalEarningId" ON "CommerceHubVendorEarnings" ("OriginalEarningId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubVendorEarnings_PaymentId" ON "CommerceHubVendorEarnings" ("PaymentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubVendorEarnings_PayoutId" ON "CommerceHubVendorEarnings" ("PayoutId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubVendorEarnings_RefundId" ON "CommerceHubVendorEarnings" ("RefundId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CommerceHubVendorEarnings_SubOrderId" ON "CommerceHubVendorEarnings" ("SubOrderId") WHERE "OriginalEarningId" IS NULL AND "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubVendorEarnings_TenantId_Status_CreatedAt" ON "CommerceHubVendorEarnings" ("TenantId", "Status", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubVendorPaymentAuditLogs_AdminId_TimestampUtc" ON "CommerceHubVendorPaymentAuditLogs" ("AdminId", "TimestampUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubVendorPaymentAuditLogs_PaymentRequestId_TimestampUtc" ON "CommerceHubVendorPaymentAuditLogs" ("PaymentRequestId", "TimestampUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubVendorPaymentMethods_IsActive" ON "CommerceHubVendorPaymentMethods" ("IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_CommerceHubVendorPaymentMethods_Name_DestinationValue" ON "CommerceHubVendorPaymentMethods" ("Name", "DestinationValue");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_CommerceHubVendorPaymentProofs_ContentHash" ON "CommerceHubVendorPaymentProofs" ("ContentHash") WHERE "ContentHash" IS NOT NULL AND "ContentHash" <> '' AND "ContentHash" <> 'LEGACY_000000000000000000000000000000000000000000000000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_CommerceHubVendorPaymentProofs_ObjectKey" ON "CommerceHubVendorPaymentProofs" ("ObjectKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_CommerceHubVendorPaymentProofs_PaymentRequestId" ON "CommerceHubVendorPaymentProofs" ("PaymentRequestId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubVendorPaymentRequests_Status_CreatedAtUtc" ON "CommerceHubVendorPaymentRequests" ("Status", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubVendorPaymentRequests_VendorId_CreatedAtUtc" ON "CommerceHubVendorPaymentRequests" ("VendorId", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubVendorPaymentRequests_VendorId_Status_CreatedAtUtc" ON "CommerceHubVendorPaymentRequests" ("VendorId", "Status", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_CommerceHubVendorPaymentRequests_Live_Method_TransferReference" ON "CommerceHubVendorPaymentRequests" ("PaymentMethodId", "TransferReference") WHERE "TransferReference" IS NOT NULL AND "TransferReference" <> '' AND "Status" <> 2;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_CommerceHubVendorPaymentRequests_VendorId_IdempotencyKey" ON "CommerceHubVendorPaymentRequests" ("VendorId", "IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubVendorPayoutMethods_VendorId_CreatedAt" ON "CommerceHubVendorPayoutMethods" ("VendorId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_CommerceHubVendorPayoutMethods_VendorId_Default" ON "CommerceHubVendorPayoutMethods" ("VendorId") WHERE "IsDeleted" = false AND "IsDefault" = true;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_CommerceHubVendorPayoutMethods_VendorId_MethodType_DestinationValue" ON "CommerceHubVendorPayoutMethods" ("VendorId", "MethodType", "DestinationValue") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubVendorSettlements_CreatedAtUtc" ON "CommerceHubVendorSettlements" ("CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubVendorSettlements_SettlementType_CreatedAtUtc" ON "CommerceHubVendorSettlements" ("SettlementType", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommerceHubVendorSettlements_VendorId_CreatedAtUtc" ON "CommerceHubVendorSettlements" ("VendorId", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_CommerceHubVendorSettlements_ExternalReference" ON "CommerceHubVendorSettlements" ("ExternalReference") WHERE "ExternalReference" IS NOT NULL AND "ExternalReference" <> '' AND "ExternalReference" NOT LIKE 'legacy:%';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_CommerceHubVendorSettlements_Vendor_IdempotencyKey" ON "CommerceHubVendorSettlements" ("VendorId", "IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommissionRules_ScopeType_IsActive_Priority" ON "CommissionRules" ("ScopeType", "IsActive", "Priority");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CommissionRules_ScopeType_ScopeId_Priority" ON "CommissionRules" ("ScopeType", "ScopeId", "Priority");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CouponReservations_CouponId" ON "CouponReservations" ("CouponId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CouponReservations_CustomerId" ON "CouponReservations" ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CouponReservations_OrderId" ON "CouponReservations" ("OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CouponReservations_TenantId_CouponId_CustomerId" ON "CouponReservations" ("TenantId", "CouponId", "CustomerId") WHERE "IsDeleted" = false AND "Status" = 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CouponReservations_TenantId_OrderId" ON "CouponReservations" ("TenantId", "OrderId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Coupons_IsActive" ON "Coupons" ("IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_Coupons_TenantId_NormalizedCode" ON "Coupons" ("TenantId", "NormalizedCode") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CouponUsages_CouponId" ON "CouponUsages" ("CouponId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CouponUsages_OrderId" ON "CouponUsages" ("OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CouponUsages_TenantId_CouponId" ON "CouponUsages" ("TenantId", "CouponId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CouponUsages_TenantId_CouponId_CustomerId" ON "CouponUsages" ("TenantId", "CouponId", "CustomerId") WHERE "CustomerId" IS NOT NULL AND "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CouponUsages_TenantId_OrderId" ON "CouponUsages" ("TenantId", "OrderId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Currencies_Name" ON "Currencies" ("Name");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CustomerAddresses_CurrentVersionId" ON "CustomerAddresses" ("CurrentVersionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CustomerAddresses_CustomerId" ON "CustomerAddresses" ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CustomerAddresses_TenantId_CustomerId_IsDefault" ON "CustomerAddresses" ("TenantId", "CustomerId", "IsDefault");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CustomerAddressVersions_AddressAdmissionEvidenceId" ON "CustomerAddressVersions" ("AddressAdmissionEvidenceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CustomerAddressVersions_CustomerAddressId" ON "CustomerAddressVersions" ("CustomerAddressId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CustomerAddressVersions_GeographicCatalogReleaseId_CountryP~" ON "CustomerAddressVersions" ("GeographicCatalogReleaseId", "CountryPlaceId", "AdministrativeAreaPlaceId", "LocalityPlaceId", "DistrictPlaceId", "ZonePlaceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CustomerAddressVersions_TenantId_CustomerAddressId_Version" ON "CustomerAddressVersions" ("TenantId", "CustomerAddressId", "Version") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Customers_Email" ON "Customers" ("Email");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Customers_Phone" ON "Customers" ("Phone");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Customers_TaxNumber" ON "Customers" ("TaxNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Customers_TenantId" ON "Customers" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CustomerTransactions_CustomerId" ON "CustomerTransactions" ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN

    DO $$
    BEGIN
        IF EXISTS (
            SELECT 1
            FROM "CustomerTransactions"
            WHERE "IsDeleted" = false
              AND "TransactionType" = 11
            GROUP BY "TenantId", "CustomerId", "TransactionType"
            HAVING COUNT(*) > 1
        ) THEN
            RAISE EXCEPTION 'duplicate active customer opening-balance transaction rows exist';
        END IF;
    END
    $$;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_CustomerTransactions_OpeningBalance_Once" ON "CustomerTransactions" ("TenantId", "CustomerId", "TransactionType") WHERE "IsDeleted" = false AND "TransactionType" = 11;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CustomsAdministrationAudit_Evidence_Action" ON "CustomsAdministrationAudit" ("CustomsEvidenceId", "Action", "OccurredAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CustomsAdministrationAudit_Tenant_Timeline" ON "CustomsAdministrationAudit" ("TenantId", "OccurredAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CustomsAdmissionAudit_Shipment_Action" ON "CustomsAdmissionAudit" ("ShipmentId", "Action", "OccurredAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CustomsAdmissionAudit_Tenant_Timeline" ON "CustomsAdmissionAudit" ("TenantId", "OccurredAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CustomsEvidence_Product_Status" ON "CustomsEvidence" ("TenantId", "ProductId", "ProductVariantId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CustomsEvidence_Source_Status" ON "CustomsEvidence" ("TenantId", "SourceKind", "SourceId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CustomsEvidenceApprovals_Evidence_Version" ON "CustomsEvidenceApprovals" ("TenantId", "CustomsEvidenceId", "CustomsEvidenceVersionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CustomsEvidenceFingerprints_Fingerprint" ON "CustomsEvidenceFingerprints" ("TenantId", "FingerprintSha256");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_CustomsEvidenceFingerprints_Version" ON "CustomsEvidenceFingerprints" ("CustomsEvidenceVersionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CustomsEvidenceHistory_Evidence_Version" ON "CustomsEvidenceHistory" ("CustomsEvidenceId", "VersionNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CustomsEvidencePublications_Evidence_Version" ON "CustomsEvidencePublications" ("TenantId", "CustomsEvidenceId", "CustomsEvidenceVersionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CustomsEvidencePublications_Product_Timeline" ON "CustomsEvidencePublications" ("TenantId", "ProductId", "ProductVariantId", "Action", "OccurredAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_CustomsEvidenceVersion_Fingerprint" ON "CustomsEvidenceVersions" ("TenantId", "EvidenceFingerprintSha256");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_CustomsEvidenceVersion_Number" ON "CustomsEvidenceVersions" ("CustomsEvidenceId", "VersionNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_CustomsPublicationLock_Key" ON "CustomsPublicationLocks" ("TenantId", "LockKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_EmailVerificationTokens_ExpiresAtUtc" ON "EmailVerificationTokens" ("ExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_EmailVerificationTokens_TokenHash" ON "EmailVerificationTokens" ("TokenHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_EmailVerificationTokens_UserId" ON "EmailVerificationTokens" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Expenses_BankId" ON "Expenses" ("BankId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Expenses_CashboxId" ON "Expenses" ("CashboxId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Expenses_ExpenseCategoryId" ON "Expenses" ("ExpenseCategoryId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Expenses_SupplierId" ON "Expenses" ("SupplierId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_FawryWebhookReplayRecords_Tenant_EvidenceHash" ON "FawryWebhookReplayRecords" ("TenantId", "EvidenceHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_FawryWebhookReplayRecords_Tenant_Provider_Event" ON "FawryWebhookReplayRecords" ("TenantId", "Provider", "ProviderEventId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_FawryWebhookReplayRecords_Tenant_ProviderReference" ON "FawryWebhookReplayRecords" ("TenantId", "ProviderReference");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_FinancialAccounts_ParentAccountId" ON "FinancialAccounts" ("ParentAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_FinancialTransactions_AccountId" ON "FinancialTransactions" ("AccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeographicAudit_Aggregate_Timeline" ON "GeographicAudit" ("AggregateType", "AggregateId", "OccurredAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeographicAudit_MappingReleaseId" ON "GeographicAudit" ("MappingReleaseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeographicAudit_Release_Timeline" ON "GeographicAudit" ("CatalogReleaseId", "MappingReleaseId", "OccurredAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeographicAudit_Tenant_Timeline" ON "GeographicAudit" ("TenantId", "OccurredAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeoCatalogPublications_Release_Published" ON "GeographicCatalogPublications" ("CatalogReleaseId", "PublishedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeographicCatalogReleaseEdges_HierarchyEdgeId" ON "GeographicCatalogReleaseEdges" ("HierarchyEdgeId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeographicCatalogReleasePlaces_PlaceId" ON "GeographicCatalogReleasePlaces" ("PlaceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeoCatalogRelease_Status_Country_Published" ON "GeographicCatalogReleases" ("Status", "CountryScopeIso2", "PublishedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeographicCatalogReleases_RollbackOfReleaseId" ON "GeographicCatalogReleases" ("RollbackOfReleaseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeographicCatalogReleases_SupersedesReleaseId" ON "GeographicCatalogReleases" ("SupersedesReleaseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_GeoCatalogRelease_Key" ON "GeographicCatalogReleases" ("ReleaseKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_GeoCatalogRelease_Number" ON "GeographicCatalogReleases" ("ReleaseNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeographicCountryProfiles_IntroducedCatalogReleaseId" ON "GeographicCountryProfiles" ("IntroducedCatalogReleaseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeographicCountryProfiles_RetiredCatalogReleaseId" ON "GeographicCountryProfiles" ("RetiredCatalogReleaseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_GeoCountryProfile_Country" ON "GeographicCountryProfiles" ("CountryPlaceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_GeoCountryProfile_Iso2" ON "GeographicCountryProfiles" ("Iso2");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_GeoCountryProfile_Iso3" ON "GeographicCountryProfiles" ("Iso3");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeographicPlaceAliases_IntroducedCatalogReleaseId" ON "GeographicPlaceAliases" ("IntroducedCatalogReleaseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeographicPlaceAliases_PlaceId" ON "GeographicPlaceAliases" ("PlaceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeographicPlaceAliases_RetiredCatalogReleaseId" ON "GeographicPlaceAliases" ("RetiredCatalogReleaseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeoPlaceAliases_Search" ON "GeographicPlaceAliases" ("CountryPlaceId", "LanguageTag", "NormalizedAlias");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeographicPlaceHierarchyEdges_IntroducedCatalogReleaseId" ON "GeographicPlaceHierarchyEdges" ("IntroducedCatalogReleaseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeographicPlaceHierarchyEdges_RetiredCatalogReleaseId" ON "GeographicPlaceHierarchyEdges" ("RetiredCatalogReleaseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeoHierarchy_ChildPlaceId" ON "GeographicPlaceHierarchyEdges" ("ChildPlaceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeoHierarchy_Country_Parent_Primary" ON "GeographicPlaceHierarchyEdges" ("CountryPlaceId", "ParentPlaceId", "IsPrimary");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_GeoHierarchy_Active" ON "GeographicPlaceHierarchyEdges" ("ParentPlaceId", "ChildPlaceId", "IntroducedCatalogReleaseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeographicPlaceLevels_IntroducedCatalogReleaseId" ON "GeographicPlaceLevels" ("IntroducedCatalogReleaseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeographicPlaceLevels_RetiredCatalogReleaseId" ON "GeographicPlaceLevels" ("RetiredCatalogReleaseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_GeoPlaceLevel_Code" ON "GeographicPlaceLevels" ("CountryPlaceId", "LevelCode");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_GeoPlaceLevel_Order" ON "GeographicPlaceLevels" ("CountryPlaceId", "SortOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeographicPlaceNames_IntroducedCatalogReleaseId" ON "GeographicPlaceNames" ("IntroducedCatalogReleaseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeographicPlaceNames_RetiredCatalogReleaseId" ON "GeographicPlaceNames" ("RetiredCatalogReleaseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeoPlaceNames_Preferred" ON "GeographicPlaceNames" ("PlaceId", "LanguageTag", "IsPreferred");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeoPlaceNames_Search" ON "GeographicPlaceNames" ("LanguageTag", "SearchName");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeographicPlaces_IntroducedCatalogReleaseId" ON "GeographicPlaces" ("IntroducedCatalogReleaseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeographicPlaces_RetiredCatalogReleaseId" ON "GeographicPlaces" ("RetiredCatalogReleaseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeographicPlaces_SuccessorPlaceId" ON "GeographicPlaces" ("SuccessorPlaceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_GeoPlaces_Country_Kind_Code_Status" ON "GeographicPlaces" ("CountryPlaceId", "PlaceKind", "CanonicalCode", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_GeoPlace_ExternalStandardCode" ON "GeographicPlaces" ("ExternalStandard", "ExternalStandardCode");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_GeoPlace_StableKey" ON "GeographicPlaces" ("StableKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_GlobalPhoneReputation_PhoneNumber" ON "GlobalPhoneReputation" ("PhoneNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_HomepageSections_SectionGroupId" ON "HomepageSections" ("SectionGroupId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_HomepageSections_TenantId_IsDraft_Order" ON "HomepageSections" ("TenantId", "IsDraft", "Order");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_HomepageSections_TenantId_IsPublished_Order" ON "HomepageSections" ("TenantId", "IsPublished", "Order");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_HomepageSections_SectionGroupId_Version" ON "HomepageSections" ("SectionGroupId", "Version");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_InboxState_Delivered" ON "InboxState" ("Delivered");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_InventoryEventInbox_TenantId_ReferenceLineId_EventType" ON "InventoryEventInbox" ("TenantId", "ReferenceLineId", "EventType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_InventoryEventInbox_TenantId_EventId" ON "InventoryEventInbox" ("TenantId", "EventId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_InventoryReservations_TenantId_OrderItemId_VariantId_WarehouseId" ON "InventoryReservations" ("TenantId", "OrderItemId", "VariantId", "WarehouseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_InventoryReservations_TenantId_Status_ExpiresAtUtc" ON "InventoryReservations" ("TenantId", "Status", "ExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_LedgerAcct_Clearing" ON "LedgerAccounts" ("TenantId", "IsClearing") WHERE "IsClearing" = true;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_LedgerAcct_Parent" ON "LedgerAccounts" ("ParentAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_LedgerAcct_Tenant" ON "LedgerAccounts" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_LedgerAcct_TenantCode" ON "LedgerAccounts" ("TenantId", "AccountCode");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_LedgerAcct_TenantType" ON "LedgerAccounts" ("TenantId", "AccountType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_LedgerJournal_Date" ON "LedgerJournalEntries" ("TenantId", "JournalDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_LedgerJournal_Idempotency" ON "LedgerJournalEntries" ("TenantId", "IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_LedgerJournal_Number" ON "LedgerJournalEntries" ("TenantId", "JournalNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_LedgerJournal_Posted" ON "LedgerJournalEntries" ("PostedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_LedgerJournal_Source" ON "LedgerJournalEntries" ("TenantId", "SourceType", "SourceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_LedgerJournal_Status" ON "LedgerJournalEntries" ("TenantId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_LedgerJournal_Tenant" ON "LedgerJournalEntries" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_LedgerJournalEntries_ReversedByJournalId" ON "LedgerJournalEntries" ("ReversedByJournalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_LedgerLines_AcctDate" ON "LedgerLines" ("AccountId", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_LedgerLines_Journal" ON "LedgerLines" ("JournalEntryId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_LedgerLines_Tenant" ON "LedgerLines" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_LedgerLines_TenantAcct" ON "LedgerLines" ("TenantId", "AccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_MarketingReviewEvidences_EvidenceType" ON "MarketingReviewEvidences" ("EvidenceType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_MarketingReviewEvidences_Platform" ON "MarketingReviewEvidences" ("Platform");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_MarketingReviewEvidences_TenantId" ON "MarketingReviewEvidences" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_MarketingReviewEvidences_ServiceReview" ON "MarketingReviewEvidences" ("ServiceReviewId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_MarketplaceCategoryTranslations_Language" ON "MarketplaceCategoryTranslations" ("Language");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_MarketplaceCategoryTranslations_CategoryId_Language" ON "MarketplaceCategoryTranslations" ("CategoryId", "Language");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_MarketplaceCmsPages_IsDraft_Slug" ON "MarketplaceCmsPages" ("IsDraft", "Slug");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_MarketplaceCmsPages_IsPublished_Slug" ON "MarketplaceCmsPages" ("IsPublished", "Slug");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_MarketplaceCmsPages_PageGroupId" ON "MarketplaceCmsPages" ("PageGroupId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_MarketplaceCmsPages_PageGroupId_Version" ON "MarketplaceCmsPages" ("PageGroupId", "Version");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_MarketplaceEventOutboxDeadLetterArchives_ArchivedAtUtc" ON "MarketplaceEventOutboxDeadLetterArchives" ("ArchivedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_MarketplaceEventOutboxDeadLetterArchives_Tenant_Aggregate" ON "MarketplaceEventOutboxDeadLetterArchives" ("TenantId", "AggregateRootId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_MarketplaceEventOutboxDeadLetterArchives_OriginalId" ON "MarketplaceEventOutboxDeadLetterArchives" ("OriginalOutboxMessageId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_MarketplaceEventOutbox_AggregateOrdering" ON "MarketplaceEventOutboxMessages" ("TenantId", "AggregateRootId", "PublishedAtUtc", "Version", "OccurredAtUtc", "CreatedAt", "Id") WHERE "PublishedAtUtc" IS NULL AND "IsFailed" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_MarketplaceEventOutbox_AggregateOrderingBlockers" ON "MarketplaceEventOutboxMessages" ("TenantId", "AggregateRootId", "Version", "OccurredAtUtc", "CreatedAt", "Id") WHERE "PublishedAtUtc" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_MarketplaceEventOutbox_EventId" ON "MarketplaceEventOutboxMessages" ("EventId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_MarketplaceEventOutbox_OccurredAtUtc" ON "MarketplaceEventOutboxMessages" ("OccurredAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_MarketplaceEventOutbox_ProcessingStatus" ON "MarketplaceEventOutboxMessages" ("PublishedAtUtc", "IsFailed", "NextRetryAtUtc", "ClaimedUntilUtc") WHERE "PublishedAtUtc" IS NULL AND "IsFailed" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_MarketplaceEventOutbox_TenantId_AggregateRootId" ON "MarketplaceEventOutboxMessages" ("TenantId", "AggregateRootId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_MarketplaceEventOutbox_TenantId_EntityId" ON "MarketplaceEventOutboxMessages" ("TenantId", "EntityId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_MarketplaceFooterLinks_SectionId_SortOrder" ON "MarketplaceFooterLinks" ("SectionId", "SortOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_MarketplaceFooterSections_SortOrder" ON "MarketplaceFooterSections" ("SortOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_MarketplaceProfiles_IsActive" ON "MarketplaceProfiles" ("IsActive") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_MarketplaceProfiles_TenantId" ON "MarketplaceProfiles" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_MarketplaceProfiles_UserId" ON "MarketplaceProfiles" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_MarketplaceProfiles_TenantUser" ON "MarketplaceProfiles" ("TenantId", "UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_MarketplaceProjectionOutbox_Idempotency" ON "MarketplaceProjectionOutboxMessages" ("EventId", "ConsumerName", "EventType") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_MarketplaceProjectionOutbox_ProcessedAtUtc" ON "MarketplaceProjectionOutboxMessages" ("ProcessedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_MarketplaceProjectionOutbox_ProcessingStatus" ON "MarketplaceProjectionOutboxMessages" ("TenantId", "ConsumerName", "ProcessedAtUtc", "NextRetryAtUtc", "ClaimedUntilUtc") WHERE "ProcessedAtUtc" IS NULL AND "IsFailed" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_MarketplaceProjectionOutbox_TenantId_EntityId" ON "MarketplaceProjectionOutboxMessages" ("TenantId", "EntityId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_MarketplaceSocialLinks_SortOrder" ON "MarketplaceSocialLinks" ("SortOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_MarketplaceSocialLinks_Platform" ON "MarketplaceSocialLinks" ("Platform");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_OnboardingChecklists_TenantId" ON "OnboardingChecklists" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_AuthProvider_TenantId_CustomerId" ON "OnlineCustomerAuthProviders" ("TenantId", "OnlineCustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_AuthProvider_TenantId_CustomerId_Provider" ON "OnlineCustomerAuthProviders" ("TenantId", "OnlineCustomerId", "Provider");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_AuthProvider_TenantId_Provider_ProviderUserId" ON "OnlineCustomerAuthProviders" ("TenantId", "Provider", "ProviderUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OnlineCustomerAuthProviders_OnlineCustomerId" ON "OnlineCustomerAuthProviders" ("OnlineCustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_EmailVerification_ExpiresAtUtc" ON "OnlineCustomerEmailVerifications" ("ExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_EmailVerification_TenantId_CustomerId_IsUsed" ON "OnlineCustomerEmailVerifications" ("TenantId", "OnlineCustomerId", "IsUsed");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_EmailVerification_TenantId_TokenHash" ON "OnlineCustomerEmailVerifications" ("TenantId", "TokenHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OnlineCustomerEmailVerifications_OnlineCustomerId" ON "OnlineCustomerEmailVerifications" ("OnlineCustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OnlineCustomerPasswordResetTokens_OnlineCustomerId" ON "OnlineCustomerPasswordResetTokens" ("OnlineCustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PasswordResetToken_ExpiresAtUtc" ON "OnlineCustomerPasswordResetTokens" ("ExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PasswordResetToken_TenantId_CustomerId_IsUsed" ON "OnlineCustomerPasswordResetTokens" ("TenantId", "OnlineCustomerId", "IsUsed");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_PasswordResetToken_TenantId_TokenHash" ON "OnlineCustomerPasswordResetTokens" ("TenantId", "TokenHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OnlineCustomerRefreshTokens_OnlineCustomerId" ON "OnlineCustomerRefreshTokens" ("OnlineCustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_RefreshToken_ExpiresAtUtc" ON "OnlineCustomerRefreshTokens" ("ExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_RefreshToken_TenantId_CurrentAccessTokenJti" ON "OnlineCustomerRefreshTokens" ("TenantId", "CurrentAccessTokenJti");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_RefreshToken_TenantId_CustomerId" ON "OnlineCustomerRefreshTokens" ("TenantId", "OnlineCustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_RefreshToken_TenantId_CustomerId_IsRevoked" ON "OnlineCustomerRefreshTokens" ("TenantId", "OnlineCustomerId", "IsRevoked");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_RefreshToken_TenantId_TokenHash" ON "OnlineCustomerRefreshTokens" ("TenantId", "TokenHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OnlineCustomers_CreatedAtUtc" ON "OnlineCustomers" ("CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OnlineCustomers_EmailVerified" ON "OnlineCustomers" ("EmailVerified");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OnlineCustomers_IsActive" ON "OnlineCustomers" ("IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OnlineCustomers_Provider" ON "OnlineCustomers" ("Provider");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OnlineCustomers_TenantId" ON "OnlineCustomers" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_OnlineCustomers_TenantId_Email" ON "OnlineCustomers" ("TenantId", "Email");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OnlineCustomers_TenantId_Provider_ProviderUserId" ON "OnlineCustomers" ("TenantId", "Provider", "ProviderUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OnlineCustomerWishlistItems_CreatedAtUtc" ON "OnlineCustomerWishlistItems" ("CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OnlineCustomerWishlistItems_TenantId_CustomerId" ON "OnlineCustomerWishlistItems" ("TenantId", "OnlineCustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_OnlineCustomerWishlistItems_TenantId_CustomerId_ProductId" ON "OnlineCustomerWishlistItems" ("TenantId", "OnlineCustomerId", "ProductId") WHERE "IsRemoved" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OnlineCustomerWishlistItems_TenantId_IsRemoved" ON "OnlineCustomerWishlistItems" ("TenantId", "IsRemoved");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OnlineCustomerWishlistItems_TenantId_ProductId" ON "OnlineCustomerWishlistItems" ("TenantId", "ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OrderItems_OrderId" ON "OrderItems" ("OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_OrderItems_OrderId_Id" ON "OrderItems" ("OrderId", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OrderItems_ProductId" ON "OrderItems" ("ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OrderItems_UnitId" ON "OrderItems" ("UnitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OrderItems_VariantId" ON "OrderItems" ("VariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OrderItems_WarehouseId" ON "OrderItems" ("WarehouseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OrderRefundLines_OrderId_OrderItemId" ON "OrderRefundLines" ("OrderId", "OrderItemId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OrderRefundLines_OrderItemId" ON "OrderRefundLines" ("OrderItemId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OrderRefundLines_OrderRefundId" ON "OrderRefundLines" ("OrderRefundId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_OrderRefundLines_Refund_OrderItem" ON "OrderRefundLines" ("OrderRefundId", "OrderItemId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OrderRefundLines_TenantId_OrderId_OrderRefundId" ON "OrderRefundLines" ("TenantId", "OrderId", "OrderRefundId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OrderRefunds_SettlementBatchId" ON "OrderRefunds" ("SettlementBatchId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OrderRefunds_TenantId_FinancialPostingStatus_RefundedAtUtc" ON "OrderRefunds" ("TenantId", "FinancialPostingStatus", "RefundedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OrderRefunds_TenantId_OrderId" ON "OrderRefunds" ("TenantId", "OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_OrderRefunds_TenantId_OrderId_RefundId" ON "OrderRefunds" ("TenantId", "OrderId", "RefundId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OrderRefunds_TenantId_SettlementBatchId" ON "OrderRefunds" ("TenantId", "SettlementBatchId") WHERE "SettlementBatchId" IS NOT NULL AND "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OrderRequestIdempotency_CompletedAtUtc_IsDeleted" ON "OrderRequestIdempotency" ("CompletedAtUtc", "IsDeleted") WHERE "CompletedAtUtc" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_OrderRequestIdempotency_TenantId_Key" ON "OrderRequestIdempotency" ("TenantId", "Key");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OrderRequestIdempotency_TenantId_OrderId" ON "OrderRequestIdempotency" ("TenantId", "OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OrderRequestIdempotency_TenantId_RequestHash_IsDeleted" ON "OrderRequestIdempotency" ("TenantId", "RequestHash", "IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Orders_AppliedPromotionId" ON "Orders" ("AppliedPromotionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Orders_BillingAddressAdmissionEvidenceId" ON "Orders" ("BillingAddressAdmissionEvidenceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Orders_CouponId" ON "Orders" ("CouponId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Orders_CreatedAt" ON "Orders" ("CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Orders_CustomerId" ON "Orders" ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Orders_OnlineCustomerId" ON "Orders" ("OnlineCustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Orders_PaymentStatus" ON "Orders" ("PaymentStatus");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Orders_SessionId" ON "Orders" ("SessionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Orders_ShippingAddressAdmissionEvidenceId" ON "Orders" ("ShippingAddressAdmissionEvidenceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Orders_ShippingMethodId" ON "Orders" ("ShippingMethodId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Orders_Source" ON "Orders" ("Source");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Orders_Status" ON "Orders" ("Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Orders_TenantId_BillingGeoRelease_Country" ON "Orders" ("TenantId", "BillingGeographicCatalogReleaseId", "BillingCountryPlaceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_Orders_TenantId_CartId_ActiveStatuses" ON "Orders" ("TenantId", "CartId") WHERE "IsDeleted" = false AND "CartId" IS NOT NULL AND "Status" IN (0, 1, 2, 8);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Orders_TenantId_CheckoutState_CreatedAt" ON "Orders" ("TenantId", "CheckoutState", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Orders_TenantId_FinancialPostingStatus_CreatedAt" ON "Orders" ("TenantId", "FinancialPostingStatus", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Orders_TenantId_GuestPhone" ON "Orders" ("TenantId", "GuestPhone");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_Orders_TenantId_OrderNumber" ON "Orders" ("TenantId", "OrderNumber") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Orders_TenantId_PaymentStatus_CreatedAt" ON "Orders" ("TenantId", "PaymentStatus", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Orders_TenantId_ShippingGeoRelease_Country" ON "Orders" ("TenantId", "ShippingGeographicCatalogReleaseId", "ShippingCountryPlaceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Orders_TenantId_Source_CreatedAt" ON "Orders" ("TenantId", "Source", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_Orders_TenantId_Source_SourceOrderId" ON "Orders" ("TenantId", "Source", "SourceOrderId") WHERE "IsDeleted" = false AND "SourceOrderId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Orders_TenantId_Status_CreatedAt" ON "Orders" ("TenantId", "Status", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Orders_WarehouseId" ON "Orders" ("WarehouseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OutboxMessage_EnqueueTime" ON "OutboxMessage" ("EnqueueTime");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OutboxMessage_ExpirationTime" ON "OutboxMessage" ("ExpirationTime");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_OutboxMessage_InboxMessageId_InboxConsumerId_SequenceNumber" ON "OutboxMessage" ("InboxMessageId", "InboxConsumerId", "SequenceNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_OutboxMessage_OutboxId_SequenceNumber" ON "OutboxMessage" ("OutboxId", "SequenceNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OutboxMessages_CriticalAggregateOrdering" ON "OutboxMessages" ("TenantId", "AggregateId", "EventVersion", "CreatedAtUtc", "Id") WHERE "ProcessedAtUtc" IS NULL AND "TenantId" IS NOT NULL AND "AggregateId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OutboxMessages_OperatorReconciliation" ON "OutboxMessages" ("RequiresOperatorReconciliation", "ProcessedAtUtc", "CreatedAtUtc") WHERE "RequiresOperatorReconciliation" = true AND "ProcessedAtUtc" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OutboxMessages_ProcessedAtUtc" ON "OutboxMessages" ("ProcessedAtUtc") WHERE "ProcessedAtUtc" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OutboxMessages_ProcessingStatus" ON "OutboxMessages" ("CreatedAtUtc", "RetryCount") WHERE "ProcessedAtUtc" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OutboxMessages_TenantId_ProcessedAtUtc" ON "OutboxMessages" ("TenantId", "ProcessedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_OutboxState_Created" ON "OutboxState" ("Created");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_PartyLedgerAdjustments_TenantId_IdempotencyKey" ON "PartyLedgerAdjustments" ("TenantId", "IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PartyLedgerAdjustments_TenantId_PartyType_PartyId_Adjustmen~" ON "PartyLedgerAdjustments" ("TenantId", "PartyType", "PartyId", "AdjustmentDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PaymentCancellationOutbox_OrderId" ON "PaymentCancellationOutboxMessages" ("OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PaymentCancellationOutbox_Processing" ON "PaymentCancellationOutboxMessages" ("ProcessedAtUtc", "IsFailed", "NextRetryAtUtc", "ClaimedUntilUtc") INCLUDE ("CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PaymentCancellationOutbox_TenantId" ON "PaymentCancellationOutboxMessages" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_PaymentIntents_Active_OrderId_Provider_Unique" ON "PaymentIntents" ("TenantId", "OrderId", "Provider") WHERE "IsDeleted" = false AND "Status" IN (0, 1, 2, 7);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PaymentIntents_OrderId" ON "PaymentIntents" ("OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PaymentIntents_TenantId_PayoutBatchId" ON "PaymentIntents" ("TenantId", "PayoutBatchId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PaymentMethods_ClearingAccountId" ON "PaymentMethods" ("ClearingAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PaymentMethods_FeesAccountId" ON "PaymentMethods" ("FeesAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PaymentMethods_TargetAccountId" ON "PaymentMethods" ("TargetAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PaymentMethods_Tenant" ON "PaymentMethods" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_PaymentMethods_TenantCode" ON "PaymentMethods" ("TenantId", "Code");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PaymentSettlementBatches_Tenant_PayoutBatchId" ON "PaymentSettlementBatches" ("TenantId", "PayoutBatchId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_PaymentSettlementBatches_Tenant_Type_SettlementId" ON "PaymentSettlementBatches" ("TenantId", "SettlementType", "SettlementId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PaymentSettlementOrders_OrderId" ON "PaymentSettlementOrders" ("OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PaymentSettlementOrders_PaymentIntentId" ON "PaymentSettlementOrders" ("PaymentIntentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PaymentSettlementOrders_SettlementBatchId" ON "PaymentSettlementOrders" ("SettlementBatchId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_PaymentSettlementOrders_Tenant_Batch_Order" ON "PaymentSettlementOrders" ("TenantId", "SettlementBatchId", "OrderId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_PaymentSettlementOrders_Tenant_Order" ON "PaymentSettlementOrders" ("TenantId", "OrderId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PaymentSettlementOrders_Tenant_PaymentIntent" ON "PaymentSettlementOrders" ("TenantId", "PaymentIntentId") WHERE "IsDeleted" = false AND "PaymentIntentId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PaymentWebhookLogs_PaymentIntentId" ON "PaymentWebhookLogs" ("PaymentIntentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_PaymentWebhookLogs_TenantId_Provider_ProviderEventId" ON "PaymentWebhookLogs" ("TenantId", "Provider", "ProviderEventId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PaymobWebhookReplayRecords_Tenant_EvidenceHash" ON "PaymobWebhookReplayRecords" ("TenantId", "EvidenceHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_PaymobWebhookReplayRecords_Tenant_Provider_Event" ON "PaymobWebhookReplayRecords" ("TenantId", "Provider", "ProviderEventId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PaymobWebhookReplayRecords_Tenant_ProviderReference" ON "PaymobWebhookReplayRecords" ("TenantId", "ProviderReference");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PhoneRiskProfiles_TenantId_IsBlocked_BlockedAtUtc" ON "PhoneRiskProfiles" ("TenantId", "IsBlocked", "BlockedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_PhoneRiskProfiles_TenantId_PhoneNumber" ON "PhoneRiskProfiles" ("TenantId", "PhoneNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PhoneSpamReports_PhoneNumber" ON "PhoneSpamReports" ("PhoneNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_PhoneSpamReports_TenantId_OrderId" ON "PhoneSpamReports" ("TenantId", "OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_PixelTrackingConfigs_TenantId" ON "PixelTrackingConfigs" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PlaceDeprecations_CatalogReleaseId" ON "PlaceDeprecations" ("CatalogReleaseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PlaceDeprecations_SuccessorPlaceId" ON "PlaceDeprecations" ("SuccessorPlaceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_PlaceDeprecations_Place_Release" ON "PlaceDeprecations" ("PlaceId", "CatalogReleaseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PlaceMergeHistory_CatalogReleaseId" ON "PlaceMergeHistory" ("CatalogReleaseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PlaceMergeHistory_TargetPlaceId" ON "PlaceMergeHistory" ("TargetPlaceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_PlaceMergeHistory_Source_Target_Release" ON "PlaceMergeHistory" ("SourcePlaceId", "TargetPlaceId", "CatalogReleaseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_ProcessedEvents_Idempotency" ON "ProcessedEvents" ("EventId", "ConsumerName", "EventType") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ProcessedEvents_ProcessedAtUtc" ON "ProcessedEvents" ("ProcessedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ProcessedEvents_TenantId_EntityId" ON "ProcessedEvents" ("TenantId", "EntityId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ProductMedia_ProductId_Position" ON "ProductMedia" ("ProductId", "Position");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ProductPixelOverrides_ProductId" ON "ProductPixelOverrides" ("ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ProductPixelOverrides_TenantId" ON "ProductPixelOverrides" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_ProductPixelOverrides_TenantId_ProductId" ON "ProductPixelOverrides" ("TenantId", "ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_ProductRequestIdempotency_TenantId_Key_Operation" ON "ProductRequestIdempotency" ("TenantId", "Key", "Operation");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ProductRequestIdempotency_TenantId_ProductId" ON "ProductRequestIdempotency" ("TenantId", "ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ProductRequestIdempotency_TenantId_ProductVariantId" ON "ProductRequestIdempotency" ("TenantId", "ProductVariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ProductReviews_ProductId_Status_CreatedAtUtc" ON "ProductReviews" ("ProductId", "Status", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_ProductReviews_ProductId_UserId" ON "ProductReviews" ("ProductId", "UserId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ProductReviews_Status_Rating_ProductId" ON "ProductReviews" ("Status", "Rating", "ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ProductReviews_UserId_CreatedAtUtc" ON "ProductReviews" ("UserId", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Products_CategoryId" ON "Products" ("CategoryId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Products_IsOnlineVisible" ON "Products" ("IsOnlineVisible");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Products_Name" ON "Products" ("Name");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Products_PublishStatus" ON "Products" ("PublishStatus");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Products_TenantId" ON "Products" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_Products_TenantId_Barcode" ON "Products" ("TenantId", "Barcode") WHERE "IsDeleted" = false AND "Barcode" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Products_TenantId_CategoryId" ON "Products" ("TenantId", "CategoryId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_Products_TenantId_SKU" ON "Products" ("TenantId", "SKU") WHERE "IsDeleted" = false AND "SKU" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Products_TenantId_Slug" ON "Products" ("TenantId", "Slug") WHERE "IsDeleted" = false AND "Slug" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ProductUnits_Barcode_Active" ON "ProductUnits" ("Barcode") WHERE "IsDeleted" = false AND "Barcode" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ProductUnits_UnitId" ON "ProductUnits" ("UnitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_ProductUnits_ProductId_Base_Active" ON "ProductUnits" ("ProductId", "IsBaseUnit") WHERE "IsDeleted" = false AND "IsBaseUnit" = true;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_ProductUnits_ProductId_Default_Active" ON "ProductUnits" ("ProductId", "IsDefault") WHERE "IsDeleted" = false AND "IsDefault" = true;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_ProductUnits_ProductId_UnitId_Active" ON "ProductUnits" ("ProductId", "UnitId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ProductVariantAttributes_VariantId" ON "ProductVariantAttributes" ("VariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_ProductVariantAttributes_VariantId_Name" ON "ProductVariantAttributes" ("VariantId", "Name") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_ProductVariants_Barcode" ON "ProductVariants" ("TenantId", "Barcode") WHERE "IsDeleted" = false AND "SemanticLifecycleState" = 0 AND "Barcode" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ProductVariants_ProductId" ON "ProductVariants" ("ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_ProductVariants_ProductId_IsDefault" ON "ProductVariants" ("TenantId", "ProductId", "IsDefault") WHERE "IsDeleted" = false AND "SemanticLifecycleState" = 0 AND "IsDefault" = true;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_ProductVariants_SKU" ON "ProductVariants" ("TenantId", "SKU") WHERE "IsDeleted" = false AND "SemanticLifecycleState" = 0 AND "SKU" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ProductVariants_TenantId_ProductId" ON "ProductVariants" ("TenantId", "ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_ProductVariants_TenantId_ProductId_AttributeCombinationKey" ON "ProductVariants" ("TenantId", "ProductId", "AttributeCombinationKey") WHERE "IsDeleted" = false AND "SemanticLifecycleState" = 0 AND "AttributeCombinationKey" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ProductVariants_TenantId_ProductId_MediaId" ON "ProductVariants" ("TenantId", "ProductId", "MediaId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ProductVariants_TenantId_SemanticLifecycleState" ON "ProductVariants" ("TenantId", "SemanticLifecycleState");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ProductVariants_TenantId_SemanticOperationId" ON "ProductVariants" ("TenantId", "SemanticOperationId") WHERE "SemanticOperationId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_ProductVariantStocks_TenantId_VariantId_WarehouseId" ON "ProductVariantStocks" ("TenantId", "VariantId", "WarehouseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ProductVariantStocks_VariantId" ON "ProductVariantStocks" ("VariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ProductVariantStocks_WarehouseId" ON "ProductVariantStocks" ("WarehouseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PromoCodes_CategoryId_IsActive" ON "PromoCodes" ("CategoryId", "IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PromoCodes_IsActive_ExpiryDate" ON "PromoCodes" ("IsActive", "ExpiryDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PromoCodes_VendorId_IsActive" ON "PromoCodes" ("VendorId", "IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_PromoCodes_Code" ON "PromoCodes" ("Code") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PromoCodeUsages_MarketplaceOrderId_AppliedAtUtc" ON "PromoCodeUsages" ("MarketplaceOrderId", "AppliedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PromoCodeUsages_PromoCodeId" ON "PromoCodeUsages" ("PromoCodeId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PromoCodeUsages_PromoCodeId_CustomerId" ON "PromoCodeUsages" ("PromoCodeId", "CustomerId") WHERE "CustomerId" IS NOT NULL AND "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_PromoCodeUsages_MarketplaceOrderId" ON "PromoCodeUsages" ("MarketplaceOrderId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_PromoCodeUsages_PromoCodeId_MarketplaceOrderId" ON "PromoCodeUsages" ("PromoCodeId", "MarketplaceOrderId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Promotions_Priority" ON "Promotions" ("Priority");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Promotions_TenantId_IsActive_StartDate_EndDate" ON "Promotions" ("TenantId", "IsActive", "StartDate", "EndDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_Promotions_TenantId_Name" ON "Promotions" ("TenantId", "Name") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Promotions_TenantId_Scope_AppliesToId_IsActive" ON "Promotions" ("TenantId", "Scope", "AppliesToId", "IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PromotionUsages_CustomerId" ON "PromotionUsages" ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PromotionUsages_OrderId" ON "PromotionUsages" ("OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PromotionUsages_PromotionId" ON "PromotionUsages" ("PromotionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PromotionUsages_TenantId_PromotionId" ON "PromotionUsages" ("TenantId", "PromotionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_PromotionUsages_TenantId_PromotionId_OrderId" ON "PromotionUsages" ("TenantId", "PromotionId", "OrderId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ProviderDelivery_Claimable" ON "ProviderDeliveryOutbox" ("DeliveredAtUtc", "IsFailed", "IsDeadLettered", "NextRetryAtUtc", "ClaimedUntilUtc") WHERE "DeliveredAtUtc" IS NULL AND "IsFailed" = false AND "IsDeadLettered" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ProviderDelivery_DeadLetter_Replay" ON "ProviderDeliveryOutbox" ("TenantId", "Provider", "IsDeadLettered", "FailureClass");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ProviderDelivery_PayloadHash" ON "ProviderDeliveryOutbox" ("PayloadHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_ProviderDelivery_Tenant_Provider_DeliveryKey" ON "ProviderDeliveryOutbox" ("TenantId", "Provider", "DeliveryKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_ProviderDeliveryReplayAudits_ReplayId" ON "ProviderDeliveryReplayAudits" ("ReplayId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ProviderDeliveryReplayAudits_Tenant_Message_Time" ON "ProviderDeliveryReplayAudits" ("TenantId", "MessageId", "ReplayedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ProviderTrustAggregates_AverageRating" ON "ProviderTrustAggregates" ("AverageRating" DESC) WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ProviderTrustAggregates_TenantId" ON "ProviderTrustAggregates" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ProviderTrustAggregates_TotalReviews" ON "ProviderTrustAggregates" ("TotalReviews" DESC) WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_ProviderTrustAggregates_ProviderProfile" ON "ProviderTrustAggregates" ("ProviderProfileId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PurchaseInvoiceItems_ProductId" ON "PurchaseInvoiceItems" ("ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PurchaseInvoiceItems_ProductVariantId" ON "PurchaseInvoiceItems" ("ProductVariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PurchaseInvoiceItems_PurchaseInvoiceId" ON "PurchaseInvoiceItems" ("PurchaseInvoiceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PurchaseInvoiceItems_UnitId" ON "PurchaseInvoiceItems" ("UnitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PurchaseInvoicePayments_PurchaseInvoiceId" ON "PurchaseInvoicePayments" ("PurchaseInvoiceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PurchaseInvoices_SupplierId" ON "PurchaseInvoices" ("SupplierId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PurchaseInvoices_WarehouseId" ON "PurchaseInvoices" ("WarehouseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_PurchaseRequestIdempotency_TenantId_Key_Operation" ON "PurchaseRequestIdempotency" ("TenantId", "Key", "Operation");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PurchaseRequestIdempotency_TenantId_PurchaseInvoiceId" ON "PurchaseRequestIdempotency" ("TenantId", "PurchaseInvoiceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_PurchaseReturnRequestIdempotency_TenantId_Key" ON "PurchaseReturnRequestIdempotency" ("TenantId", "Key");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_PurchaseReturnRequestIdempotency_TenantId_ReturnId" ON "PurchaseReturnRequestIdempotency" ("TenantId", "ReturnId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ReturnItems_ProductId" ON "ReturnItems" ("ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ReturnItems_ProductVariantId" ON "ReturnItems" ("ProductVariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ReturnItems_ReturnId" ON "ReturnItems" ("ReturnId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ReturnItems_UnitId" ON "ReturnItems" ("UnitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Returns_CustomerId" ON "Returns" ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Returns_OriginalInvoiceId" ON "Returns" ("OriginalInvoiceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Returns_SupplierId" ON "Returns" ("SupplierId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Returns_TenantId_OriginalPurchaseInvoiceId" ON "Returns" ("TenantId", "OriginalPurchaseInvoiceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_Returns_TenantId_ReturnNumber" ON "Returns" ("TenantId", "ReturnNumber") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Returns_WarehouseId" ON "Returns" ("WarehouseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_RevokedCustomerAccessTokens_CustomerId" ON "RevokedCustomerAccessTokens" ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_RevokedCustomerAccessTokens_ExpiresAt" ON "RevokedCustomerAccessTokens" ("ExpiresAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_RevokedCustomerAccessTokens_Jti" ON "RevokedCustomerAccessTokens" ("Jti");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_RevokedStorefrontAccessTokens_ExpiresAtUtc" ON "RevokedStorefrontAccessTokens" ("ExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_RevokedStorefrontAccessTokens_OnlineCustomerId" ON "RevokedStorefrontAccessTokens" ("OnlineCustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_RevokedStorefrontAccessTokens_TenantId_Jti" ON "RevokedStorefrontAccessTokens" ("TenantId", "Jti");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_RevokedStorefrontAccessTokens_TenantId_OnlineCustomerId" ON "RevokedStorefrontAccessTokens" ("TenantId", "OnlineCustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SalesInvoiceItems_ProductId" ON "SalesInvoiceItems" ("ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SalesInvoiceItems_ProductVariantId" ON "SalesInvoiceItems" ("ProductVariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SalesInvoiceItems_SalesInvoiceId_ProductId" ON "SalesInvoiceItems" ("SalesInvoiceId", "ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SalesInvoiceItems_SalesInvoiceId_ProductVariantId" ON "SalesInvoiceItems" ("SalesInvoiceId", "ProductVariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SalesInvoiceItems_UnitId" ON "SalesInvoiceItems" ("UnitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SalesInvoicePayments_PaymentMethod_PaymentDate" ON "SalesInvoicePayments" ("PaymentMethod", "PaymentDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SalesInvoicePayments_SalesInvoiceId_PaymentDate" ON "SalesInvoicePayments" ("SalesInvoiceId", "PaymentDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SalesInvoices_CustomerId" ON "SalesInvoices" ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SalesInvoices_SalesRepresentativeId" ON "SalesInvoices" ("SalesRepresentativeId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SalesInvoices_TenantId_CreatedAt" ON "SalesInvoices" ("TenantId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SalesInvoices_TenantId_CustomerId_InvoiceDate" ON "SalesInvoices" ("TenantId", "CustomerId", "InvoiceDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SalesInvoices_TenantId_InvoiceDate_Active" ON "SalesInvoices" ("TenantId", "InvoiceDate") WHERE "IsDeleted" = false AND "Status" <> 2;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SalesInvoices_TenantId_Status_InvoiceDate" ON "SalesInvoices" ("TenantId", "Status", "InvoiceDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SalesInvoices_TenantId_WarehouseId_InvoiceDate" ON "SalesInvoices" ("TenantId", "WarehouseId", "InvoiceDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SalesInvoices_WarehouseId" ON "SalesInvoices" ("WarehouseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_SalesRequestIdempotency_TenantId_Key_Operation" ON "SalesRequestIdempotency" ("TenantId", "Key", "Operation");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SalesRequestIdempotency_TenantId_Operation_RequestHash_IsDeleted" ON "SalesRequestIdempotency" ("TenantId", "Operation", "RequestHash", "IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SearchClicks_ProductId_ClickedAt" ON "SearchClicks" ("ProductId", "ClickedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SearchClicks_QueryText_ClickedAt" ON "SearchClicks" ("QueryText", "ClickedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SearchClicks_TenantId_ClickedAt" ON "SearchClicks" ("TenantId", "ClickedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SearchFilterEvents_FilterKey_OccurredAt" ON "SearchFilterEvents" ("FilterKey", "OccurredAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SearchFilterEvents_QueryText_OccurredAt" ON "SearchFilterEvents" ("QueryText", "OccurredAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SearchFilterEvents_TenantId_OccurredAt" ON "SearchFilterEvents" ("TenantId", "OccurredAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SearchImpressions_ProductId_ImpressedAt" ON "SearchImpressions" ("ProductId", "ImpressedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SearchImpressions_QueryText_ImpressedAt" ON "SearchImpressions" ("QueryText", "ImpressedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SearchImpressions_TenantId_ImpressedAt" ON "SearchImpressions" ("TenantId", "ImpressedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SearchQueries_QueryText_SearchedAt" ON "SearchQueries" ("QueryText", "SearchedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SearchQueries_ResultCount_SearchedAt" ON "SearchQueries" ("ResultCount", "SearchedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_SearchSynonyms_Term" ON "SearchSynonyms" ("Term");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ServiceRequestMessages_SenderUserId" ON "ServiceRequestMessages" ("SenderUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ServiceRequestMessages_ServiceRequestId_CreatedAt" ON "ServiceRequestMessages" ("ServiceRequestId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ServiceRequestMessages_TenantId" ON "ServiceRequestMessages" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ServiceRequests_ServiceId" ON "ServiceRequests" ("ServiceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ServiceRequests_TenantId_BuyerUserId_Status" ON "ServiceRequests" ("TenantId", "BuyerUserId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ServiceRequests_TenantId_ProviderUserId_Status" ON "ServiceRequests" ("TenantId", "ProviderUserId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_ServiceRequests_Active_Service_Buyer" ON "ServiceRequests" ("TenantId", "ServiceId", "BuyerUserId") WHERE "IsDeleted" = FALSE AND "Status" IN ('Pending', 'Accepted', 'CompletedByProvider');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ServiceReviews_ProviderUserId" ON "ServiceReviews" ("ProviderUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ServiceReviews_Rating" ON "ServiceReviews" ("Rating") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ServiceReviews_ServiceId" ON "ServiceReviews" ("ServiceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ServiceReviews_TenantId" ON "ServiceReviews" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_ServiceReviews_ServiceRequest" ON "ServiceReviews" ("ServiceRequestId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Services_Category" ON "Services" ("Category") WHERE "IsDeleted" = false AND "IsActive" = true;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Services_ProfileId" ON "Services" ("ProfileId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Services_TenantId" ON "Services" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_ShipmentCustomsSnapshotLine_OrderItem" ON "ShipmentCustomsSnapshotLines" ("ShipmentCustomsSnapshotId", "OrderItemId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ShipmentCustomsSnapshot_Order" ON "ShipmentCustomsSnapshots" ("TenantId", "OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_ShipmentCustomsSnapshot_Shipment" ON "ShipmentCustomsSnapshots" ("TenantId", "ShipmentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Shipments_CarrierCode_TrackingNumber" ON "Shipments" ("CarrierCode", "TrackingNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Shipments_DestinationAddressAdmissionEvidenceId" ON "Shipments" ("DestinationAddressAdmissionEvidenceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Shipments_OrderId" ON "Shipments" ("OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Shipments_OriginAddressAdmissionEvidenceId" ON "Shipments" ("OriginAddressAdmissionEvidenceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Shipments_ShippingMethodId" ON "Shipments" ("ShippingMethodId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_Shipments_TenantId_CarrierCode_CarrierRequestId" ON "Shipments" ("TenantId", "CarrierCode", "CarrierRequestId") WHERE "IsDeleted" = false AND "CarrierRequestId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_Shipments_TenantId_CarrierCode_ExternalReferenceId" ON "Shipments" ("TenantId", "CarrierCode", "ExternalReferenceId") WHERE "IsDeleted" = false AND "ExternalReferenceId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Shipments_TenantId_CarrierCode_Status" ON "Shipments" ("TenantId", "CarrierCode", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Shipments_TenantId_CarrierGeoMappingRelease" ON "Shipments" ("TenantId", "CarrierGeographicMappingReleaseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Shipments_TenantId_DestinationGeoRelease_Country" ON "Shipments" ("TenantId", "DestinationGeographicCatalogReleaseId", "DestinationCountryPlaceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_Shipments_TenantId_OrderId" ON "Shipments" ("TenantId", "OrderId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Shipments_TenantId_OriginGeoRelease_Country" ON "Shipments" ("TenantId", "OriginGeographicCatalogReleaseId", "OriginCountryPlaceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Shipments_TenantId_Status_LastCarrierSyncAtUtc" ON "Shipments" ("TenantId", "Status", "LastCarrierSyncAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ShippingWebhookLogs_TenantId" ON "ShippingWebhookLogs" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_ShippingWebhookLogs_TenantId_CarrierCode_EventId" ON "ShippingWebhookLogs" ("TenantId", "CarrierCode", "EventId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_ShippingWebhookLogs_TenantId_CarrierCode_PayloadHash" ON "ShippingWebhookLogs" ("TenantId", "CarrierCode", "PayloadHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_ShippingWebhookLogs_TenantId_TrackingNumber_CreatedAt" ON "ShippingWebhookLogs" ("TenantId", "TrackingNumber", "CreatedAt") WHERE "TrackingNumber" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_SmartOffers_TenantId_Name" ON "SmartOffers" ("TenantId", "Name");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SmartOffers_TenantId_Status_Priority" ON "SmartOffers" ("TenantId", "Status", "Priority");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SmartOffers_TenantId_TriggerType_PagesScope" ON "SmartOffers" ("TenantId", "TriggerType", "PagesScope");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StockMovements_CreatedAt" ON "StockMovements" ("CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StockMovements_ProductId" ON "StockMovements" ("ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StockMovements_ProductVariantId" ON "StockMovements" ("ProductVariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StockMovements_ReferenceLineId_InventoryEventType" ON "StockMovements" ("ReferenceLineId", "InventoryEventType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StockMovements_TenantId_InventoryEventId" ON "StockMovements" ("TenantId", "InventoryEventId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StockMovements_ToWarehouseId" ON "StockMovements" ("ToWarehouseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StockMovements_UnitId" ON "StockMovements" ("UnitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StockMovements_WarehouseId" ON "StockMovements" ("WarehouseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_StockMovements_TenantId_IdempotencyKey" ON "StockMovements" ("TenantId", "IdempotencyKey") WHERE "IsDeleted" = false AND "IdempotencyKey" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StockSyncOutbox_GlobalPendingOrdering" ON "StockSyncOutboxMessages" ("EventVersion", "StockUpdatedAtUtc", "OccurredAtUtc", "CreatedAt", "Id") WHERE "PublishedAtUtc" IS NULL AND "IsFailed" = false AND "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StockSyncOutbox_OccurredAtUtc" ON "StockSyncOutboxMessages" ("OccurredAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StockSyncOutbox_ProcessingStatus" ON "StockSyncOutboxMessages" ("PublishedAtUtc", "IsFailed", "NextRetryAtUtc", "ClaimedUntilUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StockSyncOutbox_TenantId_VariantId" ON "StockSyncOutboxMessages" ("TenantId", "VariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StockSyncOutbox_VariantOrdering" ON "StockSyncOutboxMessages" ("TenantId", "VariantId", "WarehouseId", "PublishedAtUtc", "EventVersion", "StockUpdatedAtUtc", "OccurredAtUtc", "CreatedAt", "Id") WHERE "PublishedAtUtc" IS NULL AND "IsFailed" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_StockSyncOutbox_Warehouse_EventId" ON "StockSyncOutboxMessages" ("TenantId", "WarehouseId", "EventId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StockSyncOutboxMessages_Unpublished_EventOrdering" ON "StockSyncOutboxMessages" ("TenantId", "VariantId", "WarehouseId", "EventVersion", "StockUpdatedAtUtc", "OccurredAtUtc", "CreatedAt", "Id") WHERE "PublishedAtUtc" IS NULL AND "IsFailed" = false AND "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_StockSyncOutboxMessages_Tenant_Variant_Warehouse_EventVersion" ON "StockSyncOutboxMessages" ("TenantId", "VariantId", "WarehouseId", "EventVersion") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StockTransferItems_ProductId" ON "StockTransferItems" ("ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StockTransferItems_ProductVariantId" ON "StockTransferItems" ("ProductVariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StockTransferItems_StockTransferId" ON "StockTransferItems" ("StockTransferId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_StockTransferRequestIdempotency_TenantId_Key_Operation" ON "StockTransferRequestIdempotency" ("TenantId", "Key", "Operation");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StockTransferRequestIdempotency_TenantId_StockTransferId" ON "StockTransferRequestIdempotency" ("TenantId", "StockTransferId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StockTransfers_ApprovedById" ON "StockTransfers" ("ApprovedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StockTransfers_CompletedById" ON "StockTransfers" ("CompletedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StockTransfers_FromWarehouseId" ON "StockTransfers" ("FromWarehouseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StockTransfers_RequestedById" ON "StockTransfers" ("RequestedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_StockTransfers_TenantId_TransferNumber" ON "StockTransfers" ("TenantId", "TransferNumber") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StockTransfers_ToWarehouseId" ON "StockTransfers" ("ToWarehouseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StoreConfigAuditEntries_ActionType" ON "StoreConfigAuditEntries" ("ActionType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StoreConfigAuditEntries_TenantId" ON "StoreConfigAuditEntries" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StoreConfigAuditEntries_TenantId_TimestampUtc" ON "StoreConfigAuditEntries" ("TenantId", "TimestampUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StoreConfigAuditEntries_TimestampUtc" ON "StoreConfigAuditEntries" ("TimestampUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_StoreConfigs_TenantId" ON "StoreConfigs" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StoreConfigs_ThemeId" ON "StoreConfigs" ("ThemeId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_AuthProviderSettings_TenantId_Provider" ON "StorefrontAuthProviderSettings" ("TenantId", "Provider");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StorefrontCartCouponStates_TenantId_Owner" ON "StorefrontCartCouponStates" ("TenantId", "OwnerType", "OwnerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UX_StorefrontCartCouponStates_TenantId_CartId" ON "StorefrontCartCouponStates" ("TenantId", "CartId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StorefrontFooterLinks_SectionId_SortOrder" ON "StorefrontFooterLinks" ("SectionId", "SortOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StorefrontFooterSections_TenantId_SortOrder" ON "StorefrontFooterSections" ("TenantId", "SortOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StorefrontSocialLinks_TenantId_SortOrder" ON "StorefrontSocialLinks" ("TenantId", "SortOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_StorefrontSocialLinks_Global_Platform" ON "StorefrontSocialLinks" ("Platform") WHERE "TenantId" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_StorefrontSocialLinks_TenantId_Platform" ON "StorefrontSocialLinks" ("TenantId", "Platform") WHERE "TenantId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StripeWebhookReplayRecords_Tenant_EvidenceHash" ON "StripeWebhookReplayRecords" ("TenantId", "EvidenceHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_StripeWebhookReplayRecords_Tenant_Provider_Event" ON "StripeWebhookReplayRecords" ("TenantId", "Provider", "ProviderEventId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_StripeWebhookReplayRecords_Tenant_ProviderReference" ON "StripeWebhookReplayRecords" ("TenantId", "ProviderReference");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_SuperAdmins_Email" ON "SuperAdmins" ("Email");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_SuperAdmins_IsSystem" ON "SuperAdmins" ("IsSystem") WHERE "IsSystem" = TRUE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SupplierPayments_PurchaseInvoiceId" ON "SupplierPayments" ("PurchaseInvoiceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SupplierPayments_SupplierId" ON "SupplierPayments" ("SupplierId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_SupplierPayments_TenantId_IdempotencyKey" ON "SupplierPayments" ("TenantId", "IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SupplierPayments_TenantId_SupplierId_PaymentDate" ON "SupplierPayments" ("TenantId", "SupplierId", "PaymentDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Suppliers_Email" ON "Suppliers" ("Email");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Suppliers_Phone" ON "Suppliers" ("Phone");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Suppliers_TaxNumber" ON "Suppliers" ("TaxNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Suppliers_TenantId" ON "Suppliers" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_SupplierTransactions_OpeningBalance_Once" ON "SupplierTransactions" ("TenantId", "SupplierId", "TransactionType") WHERE "IsDeleted" = false AND "TransactionType" = 11;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_SupplierTransactions_SupplierId" ON "SupplierTransactions" ("SupplierId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "UQ_SystemSchemaVersions_Component" ON "SystemSchemaVersions" ("Component");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_SystemSettings_TenantId_Key" ON "SystemSettings" ("TenantId", "Key") WHERE "TenantId" IS NOT NULL AND NOT "IsDeleted";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_TenantAnalyticsConfigurations_TenantId" ON "TenantAnalyticsConfigurations" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_TenantAutomationSettings_EnableAbandonedRecovery" ON "TenantAutomationSettings" ("EnableAbandonedRecovery");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_TenantAutomationSettings_TenantId" ON "TenantAutomationSettings" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_TenantPaymentMethods_TenantId" ON "TenantPaymentMethods" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_TenantPaymentMethods_TenantId_Code" ON "TenantPaymentMethods" ("TenantId", "Code");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_TenantPaymentMethods_TenantId_IsEnabled" ON "TenantPaymentMethods" ("TenantId", "IsEnabled");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Tenants_Currency" ON "Tenants" ("Currency");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Tenants_IsActive" ON "Tenants" ("IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_Tenants_Slug" ON "Tenants" ("Slug") WHERE "Slug" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Tenants_Status" ON "Tenants" ("Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_ThemePresets_Key" ON "ThemePresets" ("Key");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_TreasuryAccounts_Tenant" ON "TreasuryAccounts" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_TreasuryAccounts_TenantAccount" ON "TreasuryAccounts" ("TenantId", "AccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_TreasuryAccounts_TenantType" ON "TreasuryAccounts" ("TenantId", "Type");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_TreasuryTransactions_CreatedByUserId" ON "TreasuryTransactions" ("CreatedByUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_UserGovernanceAuditLogs_AdminId" ON "UserGovernanceAuditLogs" ("AdminId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_UserGovernanceAuditLogs_TargetRole_TargetUserId_TimestampUtc" ON "UserGovernanceAuditLogs" ("TargetRole", "TargetUserId", "TimestampUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_UserGovernanceBlacklistEntries_CreatedAtUtc" ON "UserGovernanceBlacklistEntries" ("CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_UserGovernanceBlacklistEntries_IsActive" ON "UserGovernanceBlacklistEntries" ("IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_UserGovernanceBlacklistEntries_Type_ValueNormalized" ON "UserGovernanceBlacklistEntries" ("Type", "ValueNormalized");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_UserGovernanceProfiles_RiskLevel" ON "UserGovernanceProfiles" ("RiskLevel");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_UserGovernanceProfiles_Role_SubjectId" ON "UserGovernanceProfiles" ("Role", "SubjectId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_UserGovernanceProfiles_Status" ON "UserGovernanceProfiles" ("Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_Users_Email" ON "Users" ("Email");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_Users_TenantId" ON "Users" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_Users_Username" ON "Users" ("Username");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_VisaTransactions_CreatedByUserId" ON "VisaTransactions" ("CreatedByUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_WarehouseStocks_ProductId" ON "WarehouseStocks" ("ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_WarehouseStocks_ProductVariantId" ON "WarehouseStocks" ("ProductVariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_WarehouseStocks_TenantId_Quantity" ON "WarehouseStocks" ("TenantId", "Quantity");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_WarehouseStocks_TenantId_WarehouseId_Quantity" ON "WarehouseStocks" ("TenantId", "WarehouseId", "Quantity");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_WarehouseStocks_WarehouseId_ProductId_ProductVariantId" ON "WarehouseStocks" ("WarehouseId", "ProductId", "ProductVariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX ix_whatsapp_accounts_status ON whatsapp_accounts (status) WHERE enabled = TRUE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX ix_whatsapp_accounts_tenant_id ON whatsapp_accounts (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX uq_whatsapp_account_phone_tenant ON whatsapp_accounts (tenant_id, phone_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX uq_whatsapp_template_tenant_type_language ON whatsapp_message_templates ("TenantId", "Type", "Language");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE INDEX "IX_WhatsAppRateLimits_TenantId" ON "WhatsAppRateLimits" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    CREATE UNIQUE INDEX "IX_WhatsAppRateLimits_TenantId_PhoneNumber" ON "WhatsAppRateLimits" ("TenantId", "PhoneNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    ALTER TABLE "CustomerAddresses" ADD CONSTRAINT "FK_CustomerAddresses_CustomerAddressVersions_CurrentVersionId" FOREIGN KEY ("CurrentVersionId") REFERENCES "CustomerAddressVersions" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260723214642_Baseline_Recovery3') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260723214642_Baseline_Recovery3', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260726153652_PosCartRuntime') THEN
    CREATE TABLE "PosCartOutboxMessages" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CartId" uuid NOT NULL,
        "EventType" character varying(100) NOT NULL,
        "CartVersion" integer NOT NULL,
        "PayloadJson" text NOT NULL,
        "IdempotencyKey" character varying(260) NOT NULL,
        "OccurredAtUtc" timestamp with time zone NOT NULL,
        "ProcessedAtUtc" timestamp with time zone,
        "ProcessingError" character varying(1000),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_PosCartOutboxMessages" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260726153652_PosCartRuntime') THEN
    CREATE TABLE "PosCartRequestIdempotency" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Key" character varying(200) NOT NULL,
        "Operation" character varying(64) NOT NULL,
        "RequestHash" character varying(64) NOT NULL,
        "ResponseStatusCode" integer,
        "ResponseJson" text,
        "CompletedAtUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_PosCartRequestIdempotency" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260726153652_PosCartRuntime') THEN
    CREATE TABLE "PosCarts" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CashierId" uuid NOT NULL,
        "WarehouseId" uuid NOT NULL,
        "CustomerId" uuid,
        "Status" integer NOT NULL,
        "Version" integer NOT NULL,
        "Subtotal" numeric(18,2) NOT NULL,
        "DiscountAmount" numeric(18,2) NOT NULL,
        "TaxAmount" numeric(18,2) NOT NULL,
        "TotalAmount" numeric(18,2) NOT NULL,
        "LastActivityAtUtc" timestamp with time zone NOT NULL,
        "ExpiresAtUtc" timestamp with time zone NOT NULL,
        "HeldAtUtc" timestamp with time zone,
        "CancelledAtUtc" timestamp with time zone,
        "ExpiredAtUtc" timestamp with time zone,
        "RecoveredAtUtc" timestamp with time zone,
        "CompletedAtUtc" timestamp with time zone,
        "AbandonedAtUtc" timestamp with time zone,
        "CompletedSaleId" uuid,
        "CancellationReason" character varying(500),
        "RecoveryReason" character varying(500),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_PosCarts" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_PosCarts_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_PosCarts_Users_CashierId" FOREIGN KEY ("CashierId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_PosCarts_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260726153652_PosCartRuntime') THEN
    CREATE TABLE "PosCartItems" (
        "Id" uuid NOT NULL,
        "PosCartId" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "ProductVariantId" uuid NOT NULL,
        "UnitId" uuid,
        "Quantity" numeric(18,4) NOT NULL,
        "ConversionFactor" numeric(18,6) NOT NULL,
        "BaseQuantity" numeric(18,4) NOT NULL,
        "UnitPriceSnapshot" numeric(18,2) NOT NULL,
        "DiscountAmount" numeric(18,2) NOT NULL,
        "TaxRateSnapshot" numeric(9,4) NOT NULL,
        "TaxAmount" numeric(18,2) NOT NULL,
        "TotalAmount" numeric(18,2) NOT NULL,
        "CostPriceSnapshot" numeric(18,6) NOT NULL,
        "CostAmountSnapshot" numeric(18,2) NOT NULL,
        "ProductNameSnapshot" character varying(300) NOT NULL,
        "VariantSkuSnapshot" character varying(100),
        "LineNumber" integer NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_PosCartItems" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_PosCartItems_PosCarts_PosCartId" FOREIGN KEY ("PosCartId") REFERENCES "PosCarts" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_PosCartItems_ProductVariants_ProductVariantId" FOREIGN KEY ("ProductVariantId") REFERENCES "ProductVariants" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_PosCartItems_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_PosCartItems_Units_UnitId" FOREIGN KEY ("UnitId") REFERENCES "Units" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260726153652_PosCartRuntime') THEN
    CREATE INDEX "IX_PosCartItems_ProductId_ProductVariantId" ON "PosCartItems" ("ProductId", "ProductVariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260726153652_PosCartRuntime') THEN
    CREATE INDEX "IX_PosCartItems_ProductVariantId" ON "PosCartItems" ("ProductVariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260726153652_PosCartRuntime') THEN
    CREATE INDEX "IX_PosCartItems_UnitId" ON "PosCartItems" ("UnitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260726153652_PosCartRuntime') THEN
    CREATE UNIQUE INDEX "UX_PosCartItems_Cart_Variant_Unit" ON "PosCartItems" ("PosCartId", "ProductVariantId", "UnitId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260726153652_PosCartRuntime') THEN
    CREATE INDEX "IX_PosCartOutboxMessages_Processing" ON "PosCartOutboxMessages" ("ProcessedAtUtc", "OccurredAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260726153652_PosCartRuntime') THEN
    CREATE INDEX "IX_PosCartOutboxMessages_TenantId_CartId_Version" ON "PosCartOutboxMessages" ("TenantId", "CartId", "CartVersion");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260726153652_PosCartRuntime') THEN
    CREATE UNIQUE INDEX "UX_PosCartOutboxMessages_TenantId_IdempotencyKey" ON "PosCartOutboxMessages" ("TenantId", "IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260726153652_PosCartRuntime') THEN
    CREATE UNIQUE INDEX "IX_PosCartRequestIdempotency_TenantId_Key_Operation" ON "PosCartRequestIdempotency" ("TenantId", "Key", "Operation");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260726153652_PosCartRuntime') THEN
    CREATE INDEX "IX_PosCarts_CashierId" ON "PosCarts" ("CashierId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260726153652_PosCartRuntime') THEN
    CREATE INDEX "IX_PosCarts_CustomerId" ON "PosCarts" ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260726153652_PosCartRuntime') THEN
    CREATE INDEX "IX_PosCarts_TenantId_CashierId_Status_UpdatedAt" ON "PosCarts" ("TenantId", "CashierId", "Status", "UpdatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260726153652_PosCartRuntime') THEN
    CREATE INDEX "IX_PosCarts_TenantId_CompletedSaleId" ON "PosCarts" ("TenantId", "CompletedSaleId") WHERE "CompletedSaleId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260726153652_PosCartRuntime') THEN
    CREATE INDEX "IX_PosCarts_TenantId_ExpiresAtUtc" ON "PosCarts" ("TenantId", "ExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260726153652_PosCartRuntime') THEN
    CREATE INDEX "IX_PosCarts_TenantId_WarehouseId_Status" ON "PosCarts" ("TenantId", "WarehouseId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260726153652_PosCartRuntime') THEN
    CREATE INDEX "IX_PosCarts_WarehouseId" ON "PosCarts" ("WarehouseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260726153652_PosCartRuntime') THEN
    CREATE UNIQUE INDEX "UX_PosCarts_TenantId_CashierId_Open" ON "PosCarts" ("TenantId", "CashierId") WHERE "IsDeleted" = false AND "Status" IN (0, 1, 4);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260726153652_PosCartRuntime') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260726153652_PosCartRuntime', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260726172030_PlatformCurrencyReferenceSeed') THEN
    INSERT INTO "Currencies" ("Code", "Name", "Symbol", "DecimalPlaces")
    VALUES ('EGP', 'Egyptian Pound', 'EGP', 2)
    ON CONFLICT ("Code") DO UPDATE
    SET "Name" = EXCLUDED."Name",
        "Symbol" = EXCLUDED."Symbol",
        "DecimalPlaces" = EXCLUDED."DecimalPlaces";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260726172030_PlatformCurrencyReferenceSeed') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260726172030_PlatformCurrencyReferenceSeed', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729120000_StorefrontProductUnitDbEnforcement') THEN
    DO $$
    BEGIN
        IF EXISTS (
            SELECT 1
            FROM "ProductUnits"
            WHERE "IsDeleted" = false
              AND "ConversionFactor" <= 0
        ) THEN
            RAISE EXCEPTION 'ARCH-2 ProductUnit enforcement blocked: active ProductUnits must have positive ConversionFactor.';
        END IF;

        IF EXISTS (
            SELECT 1
            FROM "ProductUnits"
            WHERE "IsDeleted" = false
              AND "IsBaseUnit" = true
              AND "ConversionFactor" <> 1
        ) THEN
            RAISE EXCEPTION 'ARCH-2 ProductUnit enforcement blocked: active base ProductUnits must have ConversionFactor = 1.';
        END IF;
    END $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729120000_StorefrontProductUnitDbEnforcement') THEN
    ALTER TABLE "ProductUnits"
        ADD CONSTRAINT "CK_ProductUnits_ConversionFactor_Positive"
        CHECK ("ConversionFactor" > 0);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729120000_StorefrontProductUnitDbEnforcement') THEN
    ALTER TABLE "ProductUnits"
        ADD CONSTRAINT "CK_ProductUnits_BaseUnit_ConversionFactor_One"
        CHECK (NOT "IsBaseUnit" OR "ConversionFactor" = 1);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729120000_StorefrontProductUnitDbEnforcement') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260729120000_StorefrontProductUnitDbEnforcement', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729150000_ProductAuthoringXminConcurrency') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260729150000_ProductAuthoringXminConcurrency', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE TABLE "Domains" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "HostName" character varying(253) NOT NULL,
        "NormalizedHostName" character varying(253) NOT NULL,
        "DomainType" integer NOT NULL DEFAULT 0,
        "LifecycleState" integer NOT NULL DEFAULT 0,
        "SslCertificateId" uuid,
        "VerificationToken" character varying(256) NOT NULL DEFAULT '',
        "VerificationMethod" integer,
        "RedirectType" integer NOT NULL DEFAULT 0,
        "RedirectTargetDomainId" uuid,
        "CanonicalPolicy" integer NOT NULL DEFAULT 0,
        "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT (NOW()),
        "VerifiedAtUtc" timestamp with time zone,
        "SslActivatedAtUtc" timestamp with time zone,
        "UpdatedAtUtc" timestamp with time zone,
        "DeletedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL DEFAULT FALSE,
        "DomainHistoryJson" jsonb NOT NULL DEFAULT '[]',
        CONSTRAINT "PK_Domains" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_Domains_HostName_Length" CHECK ("HostName" IS NOT NULL AND LENGTH(TRIM("HostName")) >= 1 AND LENGTH("HostName") <= 253),
        CONSTRAINT "CK_Domains_Redirect_Cascade" CHECK (("DomainType" <> 2 OR ("RedirectTargetDomainId" IS NOT NULL AND "RedirectTargetDomainId" <> "Id" AND "RedirectType" <> 0))),
        CONSTRAINT "CK_Domains_Primary_Host_Ssl" CHECK (("LifecycleState" <> 5 AND "LifecycleState" <> 4) OR "SslCertificateId" IS NOT NULL),
        CONSTRAINT "CK_Domains_System_NoTenant" CHECK (("DomainType" NOT IN (5, 6)) OR "TenantId" IS NOT NULL),
        CONSTRAINT "FK_Domains_TenantId" FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_Domains_RedirectTargetDomainId" FOREIGN KEY ("RedirectTargetDomainId") REFERENCES "Domains" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE TABLE "DomainAuditEntries" (
        "Id" uuid NOT NULL,
        "DomainId" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Action" character varying(100) NOT NULL,
        "ActorType" integer NOT NULL,
        "ActorUserId" uuid,
        "BeforeSnapshotJson" jsonb,
        "AfterSnapshotJson" jsonb NOT NULL,
        "TraceId" character varying(128),
        "IpAddress" character varying(45),
        "UserAgent" character varying(500),
        "TimestampUtc" timestamp with time zone NOT NULL DEFAULT (NOW()),
        CONSTRAINT "PK_DomainAuditEntries" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_DomainAuditEntries_DomainId" FOREIGN KEY ("DomainId") REFERENCES "Domains" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE TABLE "DomainVerifications" (
        "Id" uuid NOT NULL,
        "DomainId" uuid NOT NULL,
        "Method" integer NOT NULL,
        "Token" character varying(256) NOT NULL,
        "Status" integer NOT NULL,
        "AttemptedAtUtc" timestamp with time zone NOT NULL DEFAULT (NOW()),
        "VerifiedAtUtc" timestamp with time zone,
        "FailureReason" character varying(500),
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT (NOW()),
        "UpdatedAt" timestamp with time zone,
        "IsDeleted" boolean NOT NULL DEFAULT FALSE,
        CONSTRAINT "PK_DomainVerifications" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_DomainVerifications_DomainId" FOREIGN KEY ("DomainId") REFERENCES "Domains" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE TABLE "SslCertificates" (
        "Id" uuid NOT NULL,
        "DomainId" uuid NOT NULL,
        "Provider" integer NOT NULL,
        "Status" integer NOT NULL,
        "CertificateArn" character varying(500),
        "Thumbprint" character varying(200),
        "NotBeforeUtc" timestamp with time zone,
        "NotAfterUtc" timestamp with time zone,
        "LastRenewalAttemptUtc" timestamp with time zone,
        "NextRenewalUtc" timestamp with time zone,
        "FailureReason" character varying(500),
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT (NOW()),
        "UpdatedAt" timestamp with time zone,
        "IsDeleted" boolean NOT NULL DEFAULT FALSE,
        "SslCertificateHistoryJson" jsonb NOT NULL DEFAULT '[]',
        CONSTRAINT "PK_SslCertificates" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SslCertificates_DomainId" FOREIGN KEY ("DomainId") REFERENCES "Domains" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE UNIQUE INDEX "UQ_Domains_NormalizedHostName" ON "Domains" ("NormalizedHostName");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE INDEX "IX_Domains_TenantId" ON "Domains" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE INDEX "IX_Domains_TenantId_LifecycleState" ON "Domains" ("TenantId", "LifecycleState");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE INDEX "IX_Domains_LifecycleState" ON "Domains" ("LifecycleState");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE INDEX "IX_Domains_DomainType" ON "Domains" ("DomainType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE INDEX "IX_Domains_SslCertificateId" ON "Domains" ("SslCertificateId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE INDEX "IX_Domains_RedirectTargetDomainId" ON "Domains" ("RedirectTargetDomainId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE INDEX "IX_Domains_IsDeleted" ON "Domains" ("IsDeleted") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE UNIQUE INDEX "UQ_Domains_TenantId_Primary" ON "Domains" ("TenantId") WHERE "DomainType" = 0 AND "LifecycleState" = 5 AND "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE INDEX "IX_SslCertificates_DomainId" ON "SslCertificates" ("DomainId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE UNIQUE INDEX "UQ_SslCertificates_DomainId_UniqueActive" ON "SslCertificates" ("DomainId") WHERE "IsDeleted" = false AND "Status" IN (0, 1, 2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE INDEX "IX_SslCertificates_Status" ON "SslCertificates" ("Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE INDEX "IX_SslCertificates_NotAfterUtc" ON "SslCertificates" ("NotAfterUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE INDEX "IX_SslCertificates_NextRenewalUtc" ON "SslCertificates" ("NextRenewalUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE INDEX "IX_SslCertificates_IsDeleted" ON "SslCertificates" ("IsDeleted") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE INDEX "IX_DomainVerifications_DomainId" ON "DomainVerifications" ("DomainId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE INDEX "IX_DomainVerifications_DomainId_Status" ON "DomainVerifications" ("DomainId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE INDEX "IX_DomainVerifications_AttemptedAtUtc" ON "DomainVerifications" ("AttemptedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE INDEX "IX_DomainVerifications_IsDeleted" ON "DomainVerifications" ("IsDeleted") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE UNIQUE INDEX "UQ_DomainVerifications_DomainId_Method" ON "DomainVerifications" ("DomainId", "Method") WHERE "Status" = 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE INDEX "IX_DomainAuditEntries_DomainId" ON "DomainAuditEntries" ("DomainId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE INDEX "IX_DomainAuditEntries_TenantId_TimestampUtc" ON "DomainAuditEntries" ("TenantId", "TimestampUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE INDEX "IX_DomainAuditEntries_TimestampUtc" ON "DomainAuditEntries" ("TimestampUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE INDEX "IX_DomainAuditEntries_TraceId" ON "DomainAuditEntries" ("TraceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE INDEX "IX_DomainAuditEntries_Action" ON "DomainAuditEntries" ("Action");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    CREATE OR REPLACE FUNCTION prevent_domain_audit_entries_mutation()
    RETURNS trigger AS $$
    BEGIN
        RAISE EXCEPTION 'DomainAuditEntries records are append-only and cannot be updated or deleted.';
    END;
    $$ LANGUAGE plpgsql;

    CREATE TRIGGER trg_domain_audit_entries_append_only
    BEFORE UPDATE OR DELETE ON "DomainAuditEntries"
    FOR EACH ROW EXECUTE FUNCTION prevent_domain_audit_entries_mutation();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215917_DOMAIN_2C_0_CreateDomainTables') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260805215917_DOMAIN_2C_0_CreateDomainTables', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260805215918_DOMAIN_2C_DomainPersistence') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260805215918_DOMAIN_2C_DomainPersistence', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809090000_DOMAIN_4_3_DomainVerificationEngineDiagnostics') THEN
    ALTER TABLE "DomainVerifications" ADD "CorrelationId" character varying(128);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809090000_DOMAIN_4_3_DomainVerificationEngineDiagnostics') THEN
    ALTER TABLE "DomainVerifications" ADD "ResolverEndpoint" character varying(128);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809090000_DOMAIN_4_3_DomainVerificationEngineDiagnostics') THEN
    ALTER TABLE "DomainVerifications" ADD "LatencyMilliseconds" integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809090000_DOMAIN_4_3_DomainVerificationEngineDiagnostics') THEN
    ALTER TABLE "DomainVerifications" ADD "Attempt" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809090000_DOMAIN_4_3_DomainVerificationEngineDiagnostics') THEN
    ALTER TABLE "DomainVerifications" ADD "Ttl" integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809090000_DOMAIN_4_3_DomainVerificationEngineDiagnostics') THEN
    ALTER TABLE "DomainVerifications" ADD "ResolvedValue" character varying(1024);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809090000_DOMAIN_4_3_DomainVerificationEngineDiagnostics') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260809090000_DOMAIN_4_3_DomainVerificationEngineDiagnostics', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" DROP CONSTRAINT "FK_SslCertificates_DomainId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    DROP INDEX "IX_SslCertificates_NextRenewalUtc";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    DROP INDEX "IX_SslCertificates_NotAfterUtc";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    DROP INDEX "UQ_SslCertificates_DomainId_UniqueActive";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "Domains" ADD CONSTRAINT "AK_Domains_TenantId_Id" UNIQUE ("TenantId", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "NormalizedHostName" character varying(253) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "Generation" integer NOT NULL DEFAULT 1;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "OperationPhase" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "OperationId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "IdempotencyKey" character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ProviderImplementation" character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ProviderAccountReference" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ProviderEnvironment" character varying(64);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ProviderRequestReference" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "OrderReference" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "AuthorizationReference" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ChallengeReference" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "EdgeCertificateReference" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "CaddyStorageReference" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "Issuer" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "Subject" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "SubjectAlternativeNamesJson" jsonb NOT NULL DEFAULT '[]';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "Fingerprint" character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "SerialNumber" character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "SignatureAlgorithm" character varying(120);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "IssuedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ActivatedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ExpiredAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "EdgeCluster" character varying(120);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "EdgeIdentifier" character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "DeployedGeneration" integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ObservedGeneration" integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "LastEdgeProbeAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "LastObservedThumbprint" character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ActivationStartedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ExpectedThumbprint" character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ActivationFailureCode" character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ActivationFailureReason" character varying(1000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ActivationFailedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "RenewalEligible" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "RenewalAttemptCount" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "RetryAfterUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "BackoffReason" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ReplacementCertificateId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "RevokedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "RevokedByActorType" integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "RevokedByActorId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "RevocationReason" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ProviderRevocationReference" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "EdgeDeactivationAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "FailureCode" character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "SafeFailureReason" character varying(1000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "ProviderErrorClass" character varying(120);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "Retryable" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "LastFailureAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "CorrelationId" character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "FencingToken" bigint NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "LockOwnerReference" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "LeaseExpiresAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "CreatedBy" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD "LastOperationAuditId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    UPDATE "SslCertificates" AS c
    SET "TenantId" = d."TenantId",
        "NormalizedHostName" = d."NormalizedHostName"
    FROM "Domains" AS d
    WHERE c."DomainId" = d."Id";

    UPDATE "SslCertificates"
    SET "SubjectAlternativeNamesJson" = "CertificateArn"::jsonb
    WHERE "CertificateArn" IS NOT NULL
      AND "CertificateArn" ~ '^[[:space:]]*\[';

    WITH ranked AS (
        SELECT "Id",
               ROW_NUMBER() OVER (
                   PARTITION BY "TenantId", "DomainId"
                   ORDER BY "CreatedAt", "Id") AS rn
        FROM "SslCertificates"
        WHERE "IsDeleted" = false
    )
    UPDATE "SslCertificates" AS c
    SET "Generation" = ranked.rn
    FROM ranked
    WHERE c."Id" = ranked."Id";

    UPDATE "SslCertificates"
    SET "RevokedAtUtc" = COALESCE("UpdatedAt", "CreatedAt", NOW())
    WHERE "Status" = 3 AND "RevokedAtUtc" IS NULL;

    UPDATE "SslCertificates"
    SET "ExpiredAtUtc" = COALESCE("NotAfterUtc", "UpdatedAt", "CreatedAt", NOW())
    WHERE "Status" = 2 AND "ExpiredAtUtc" IS NULL AND "NotAfterUtc" IS NULL;

    UPDATE "SslCertificates"
    SET "FailureCode" = 'legacy_failed'
    WHERE "Status" = 4
      AND "FailureCode" IS NULL
      AND "SafeFailureReason" IS NULL
      AND "FailureReason" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE TABLE "SslCertificateOperations" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "DomainId" uuid NOT NULL,
        "CertificateId" uuid,
        "NormalizedHostName" character varying(253) NOT NULL,
        "OperationType" integer NOT NULL,
        "Status" integer NOT NULL,
        "Phase" integer NOT NULL,
        "OperationId" uuid NOT NULL,
        "IdempotencyKey" character varying(200) NOT NULL,
        "RequestHash" character varying(128),
        "ProviderRequestReference" character varying(500),
        "EdgeCertificateReference" character varying(500),
        "FencingToken" bigint NOT NULL DEFAULT 0,
        "LockOwnerReference" uuid,
        "LeaseExpiresAtUtc" timestamp with time zone,
        "AttemptCount" integer NOT NULL DEFAULT 0,
        "RetryAfterUtc" timestamp with time zone,
        "FailureCode" character varying(100),
        "SafeFailureReason" character varying(1000),
        "Retryable" boolean NOT NULL DEFAULT FALSE,
        "CorrelationId" character varying(100),
        "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT (NOW()),
        "UpdatedAtUtc" timestamp with time zone,
        "CompletedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL DEFAULT FALSE,
        CONSTRAINT "PK_SslCertificateOperations" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_SslCertificateOperations_Fencing_NonNegative" CHECK ("FencingToken" >= 0),
        CONSTRAINT "CK_SslCertificateOperations_HostName_Length" CHECK ("NormalizedHostName" IS NOT NULL AND LENGTH(TRIM("NormalizedHostName")) >= 1 AND LENGTH("NormalizedHostName") <= 253),
        CONSTRAINT "CK_SslCertificateOperations_IdempotencyKey_NotBlank" CHECK ("IdempotencyKey" IS NOT NULL AND LENGTH(TRIM("IdempotencyKey")) >= 1),
        CONSTRAINT "FK_SslCertificateOperations_CertificateId" FOREIGN KEY ("CertificateId") REFERENCES "SslCertificates" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_SslCertificateOperations_DomainId" FOREIGN KEY ("TenantId", "DomainId") REFERENCES "Domains" ("TenantId", "Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE INDEX "IX_SslCertificates_NextRenewalUtc_Status" ON "SslCertificates" ("NextRenewalUtc", "Status") WHERE "NextRenewalUtc" IS NOT NULL AND "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE INDEX "IX_SslCertificates_NotAfterUtc_Status" ON "SslCertificates" ("NotAfterUtc", "Status") WHERE "NotAfterUtc" IS NOT NULL AND "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE INDEX "IX_SslCertificates_RetryAfterUtc_Status" ON "SslCertificates" ("RetryAfterUtc", "Status") WHERE "RetryAfterUtc" IS NOT NULL AND "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE INDEX "IX_SslCertificates_Status_OperationPhase" ON "SslCertificates" ("Status", "OperationPhase");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE INDEX "IX_SslCertificates_TenantId_DomainId_IsDeleted" ON "SslCertificates" ("TenantId", "DomainId", "IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE INDEX "IX_SslCertificates_TenantId_DomainId_OperationPhase" ON "SslCertificates" ("TenantId", "DomainId", "OperationPhase") WHERE "IsDeleted" = false AND "OperationPhase" <> 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE INDEX "IX_SslCertificates_TenantId_NormalizedHostName_Status" ON "SslCertificates" ("TenantId", "NormalizedHostName", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE INDEX "IX_SslCertificates_ReplacementCertificateId" ON "SslCertificates" ("ReplacementCertificateId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE UNIQUE INDEX "UQ_SslCertificates_TenantId_DomainId_Active" ON "SslCertificates" ("TenantId", "DomainId") WHERE "IsDeleted" = false AND "Status" = 1;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE UNIQUE INDEX "UQ_SslCertificates_TenantId_DomainId_ActiveActivatingGeneration" ON "SslCertificates" ("TenantId", "DomainId", "Generation") WHERE "IsDeleted" = false AND "Status" IN (1, 8);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE UNIQUE INDEX "UQ_SslCertificates_TenantId_DomainId_Generation" ON "SslCertificates" ("TenantId", "DomainId", "Generation") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE UNIQUE INDEX "UQ_SslCertificates_TenantId_DomainId_IdempotencyKey" ON "SslCertificates" ("TenantId", "DomainId", "IdempotencyKey") WHERE "IsDeleted" = false AND "IdempotencyKey" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD CONSTRAINT "CK_SslCertificates_Activation_ExpectedThumbprint" CHECK ("Status" <> 8 OR "ExpectedThumbprint" IS NOT NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD CONSTRAINT "CK_SslCertificates_Expired_Metadata" CHECK ("Status" <> 2 OR "ExpiredAtUtc" IS NOT NULL OR "NotAfterUtc" IS NOT NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD CONSTRAINT "CK_SslCertificates_Failed_Metadata" CHECK ("Status" <> 4 OR "FailureCode" IS NOT NULL OR "SafeFailureReason" IS NOT NULL OR "FailureReason" IS NOT NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD CONSTRAINT "CK_SslCertificates_Generation_Positive" CHECK ("Generation" >= 1);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD CONSTRAINT "CK_SslCertificates_HostName_Length" CHECK ("NormalizedHostName" IS NOT NULL AND LENGTH(TRIM("NormalizedHostName")) >= 1 AND LENGTH("NormalizedHostName") <= 253);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD CONSTRAINT "CK_SslCertificates_Revoked_Metadata" CHECK ("Status" <> 3 OR "RevokedAtUtc" IS NOT NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD CONSTRAINT "CK_SslCertificates_Status_Canonical" CHECK ("Status" IN (0, 1, 2, 3, 4, 5, 6, 7, 8, 9));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE INDEX "IX_SslCertificateOperations_CertificateId" ON "SslCertificateOperations" ("CertificateId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE INDEX "IX_SslCertificateOperations_DomainId" ON "SslCertificateOperations" ("DomainId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE INDEX "IX_SslCertificateOperations_LeaseExpiresAtUtc_Status" ON "SslCertificateOperations" ("LeaseExpiresAtUtc", "Status") WHERE "LeaseExpiresAtUtc" IS NOT NULL AND "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE INDEX "IX_SslCertificateOperations_RetryAfterUtc_Status" ON "SslCertificateOperations" ("RetryAfterUtc", "Status") WHERE "RetryAfterUtc" IS NOT NULL AND "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE INDEX "IX_SslCertificateOperations_Tenant_Domain_Certificate" ON "SslCertificateOperations" ("TenantId", "DomainId", "CertificateId") WHERE "IsDeleted" = false AND "CertificateId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE INDEX "IX_SslCertificateOperations_Tenant_Domain_FencingToken" ON "SslCertificateOperations" ("TenantId", "DomainId", "FencingToken") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE UNIQUE INDEX "UQ_SslCertificateOperations_Tenant_Domain_Type_IdempotencyKey" ON "SslCertificateOperations" ("TenantId", "DomainId", "OperationType", "IdempotencyKey") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    CREATE UNIQUE INDEX "UQ_SslCertificateOperations_Tenant_Domain_Type_InProgress" ON "SslCertificateOperations" ("TenantId", "DomainId", "OperationType") WHERE "IsDeleted" = false AND "Status" IN (0, 1);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD CONSTRAINT "FK_SslCertificates_DomainId" FOREIGN KEY ("TenantId", "DomainId") REFERENCES "Domains" ("TenantId", "Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    ALTER TABLE "SslCertificates" ADD CONSTRAINT "FK_SslCertificates_ReplacementCertificateId" FOREIGN KEY ("ReplacementCertificateId") REFERENCES "SslCertificates" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809185514_SHOPIXY_CheckoutFieldDefinitions') THEN
    CREATE TABLE "CheckoutFieldDefinitions" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "FieldKind" character varying(16) NOT NULL,
        "FieldType" character varying(32) NOT NULL,
        "SystemFieldKey" character varying(64),
        "Label" character varying(120) NOT NULL,
        "HelpText" character varying(500),
        "IsEnabled" boolean NOT NULL,
        "IsRequired" boolean NOT NULL,
        "DisplayOrder" integer NOT NULL,
        "ConfigurationSchemaVersion" integer NOT NULL DEFAULT 1,
        "ConfigurationJson" jsonb NOT NULL DEFAULT '{}',
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CheckoutFieldDefinitions" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CheckoutFieldDefinitions_RequiredEnabled" CHECK (NOT "IsRequired" OR "IsEnabled"),
        CONSTRAINT "CK_CheckoutFieldDefinitions_SystemFieldKey" CHECK (("FieldKind" = 'System' AND "SystemFieldKey" IS NOT NULL AND LENGTH(TRIM("SystemFieldKey")) > 0) OR ("FieldKind" = 'Custom' AND "SystemFieldKey" IS NULL)),
        CONSTRAINT "FK_CheckoutFieldDefinitions_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809185514_SHOPIXY_CheckoutFieldDefinitions') THEN
    CREATE INDEX "IX_CheckoutFieldDefinitions_TenantId" ON "CheckoutFieldDefinitions" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809185514_SHOPIXY_CheckoutFieldDefinitions') THEN
    CREATE INDEX "IX_CheckoutFieldDefinitions_TenantId_DisplayOrder" ON "CheckoutFieldDefinitions" ("TenantId", "DisplayOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809185514_SHOPIXY_CheckoutFieldDefinitions') THEN
    CREATE INDEX "IX_CheckoutFieldDefinitions_TenantId_IsDeleted" ON "CheckoutFieldDefinitions" ("TenantId", "IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809185514_SHOPIXY_CheckoutFieldDefinitions') THEN
    CREATE UNIQUE INDEX "UX_CheckoutFieldDefinitions_Tenant_SystemFieldKey_Active" ON "CheckoutFieldDefinitions" ("TenantId", "SystemFieldKey") WHERE "IsDeleted" = false AND "SystemFieldKey" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809185514_SHOPIXY_CheckoutFieldDefinitions') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260809185514_SHOPIXY_CheckoutFieldDefinitions', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809200555_SHOPIXY_OrderCheckoutFieldSnapshots') THEN
    CREATE TABLE "OrderCheckoutFieldSnapshots" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "CheckoutFieldDefinitionId" uuid NOT NULL,
        "FieldKind" character varying(16) NOT NULL,
        "FieldType" character varying(32) NOT NULL,
        "SystemFieldKey" character varying(64),
        "Label" character varying(120) NOT NULL,
        "HelpText" character varying(500),
        "IsRequired" boolean NOT NULL,
        "IsEnabled" boolean NOT NULL,
        "DisplayOrder" integer NOT NULL,
        "ConfigurationSchemaVersion" integer NOT NULL,
        "ConfigurationJson" jsonb NOT NULL DEFAULT '{}',
        "ValueJson" jsonb NOT NULL DEFAULT 'null',
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_OrderCheckoutFieldSnapshots" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_OrderCheckoutFieldSnapshots_ConfigurationSchemaVersion_Posi~" CHECK ("ConfigurationSchemaVersion" > 0),
        CONSTRAINT "CK_OrderCheckoutFieldSnapshots_SystemFieldKey" CHECK (("FieldKind" = 'System' AND "SystemFieldKey" IS NOT NULL AND LENGTH(TRIM("SystemFieldKey")) > 0) OR ("FieldKind" = 'Custom' AND "SystemFieldKey" IS NULL)),
        CONSTRAINT "FK_OrderCheckoutFieldSnapshots_Orders_TenantId_OrderId" FOREIGN KEY ("TenantId", "OrderId") REFERENCES "Orders" ("TenantId", "Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809200555_SHOPIXY_OrderCheckoutFieldSnapshots') THEN
    CREATE INDEX "IX_OrderCheckoutFieldSnapshots_OrderId" ON "OrderCheckoutFieldSnapshots" ("OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809200555_SHOPIXY_OrderCheckoutFieldSnapshots') THEN
    CREATE INDEX "IX_OrderCheckoutFieldSnapshots_TenantId_OrderId" ON "OrderCheckoutFieldSnapshots" ("TenantId", "OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809200555_SHOPIXY_OrderCheckoutFieldSnapshots') THEN
    CREATE INDEX "IX_OrderCheckoutFieldSnapshots_TenantId_OrderId_DisplayOrder" ON "OrderCheckoutFieldSnapshots" ("TenantId", "OrderId", "DisplayOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809200555_SHOPIXY_OrderCheckoutFieldSnapshots') THEN
    CREATE UNIQUE INDEX "UX_OrderCheckoutFieldSnapshots_Tenant_Order_FieldDefinition_Active" ON "OrderCheckoutFieldSnapshots" ("TenantId", "OrderId", "CheckoutFieldDefinitionId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809200555_SHOPIXY_OrderCheckoutFieldSnapshots') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260809200555_SHOPIXY_OrderCheckoutFieldSnapshots', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809203807_SHOPIXY_CheckoutAttachments') THEN
    CREATE TABLE "CheckoutAttachments" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CartId" uuid NOT NULL,
        "OwnerId" character varying(128) NOT NULL,
        "OwnerType" character varying(32) NOT NULL,
        "SessionId" character varying(128),
        "OnlineCustomerId" uuid,
        "CheckoutFieldDefinitionId" uuid NOT NULL,
        "OrderId" uuid,
        "ObjectKey" character varying(500) NOT NULL,
        "OriginalFileName" character varying(255) NOT NULL,
        "ContentType" character varying(255) NOT NULL,
        "MediaType" character varying(32) NOT NULL,
        "SizeBytes" bigint NOT NULL,
        "ExpectedSizeBytes" bigint NOT NULL,
        "Status" character varying(32) NOT NULL,
        "ExpiresAtUtc" timestamp with time zone NOT NULL,
        "UploadedAtUtc" timestamp with time zone,
        "AttachedAtUtc" timestamp with time zone,
        "MetadataJson" jsonb NOT NULL DEFAULT '{}',
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_CheckoutAttachments" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CheckoutAttachments_Orders_TenantId_OrderId" FOREIGN KEY ("TenantId", "OrderId") REFERENCES "Orders" ("TenantId", "Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809203807_SHOPIXY_CheckoutAttachments') THEN
    CREATE INDEX "IX_CheckoutAttachments_Status_ExpiresAtUtc" ON "CheckoutAttachments" ("Status", "ExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809203807_SHOPIXY_CheckoutAttachments') THEN
    CREATE INDEX "IX_CheckoutAttachments_Tenant_Cart_Owner_Field" ON "CheckoutAttachments" ("TenantId", "CartId", "OwnerId", "CheckoutFieldDefinitionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809203807_SHOPIXY_CheckoutAttachments') THEN
    CREATE INDEX "IX_CheckoutAttachments_Tenant_OrderId" ON "CheckoutAttachments" ("TenantId", "OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809203807_SHOPIXY_CheckoutAttachments') THEN
    CREATE UNIQUE INDEX "UX_CheckoutAttachments_ObjectKey" ON "CheckoutAttachments" ("ObjectKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809203807_SHOPIXY_CheckoutAttachments') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260809203807_SHOPIXY_CheckoutAttachments', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809223725_DOMAIN_Final_CertificateTenantIsolationAndRetirement') THEN
    ALTER TABLE "SslCertificateOperations" DROP CONSTRAINT "FK_SslCertificateOperations_DomainId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809223725_DOMAIN_Final_CertificateTenantIsolationAndRetirement') THEN
    ALTER TABLE "SslCertificates" DROP CONSTRAINT "FK_SslCertificates_DomainId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809223725_DOMAIN_Final_CertificateTenantIsolationAndRetirement') THEN
    ALTER TABLE "SslCertificates" DROP CONSTRAINT "CK_SslCertificates_Status_Canonical";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809223725_DOMAIN_Final_CertificateTenantIsolationAndRetirement') THEN
    DROP INDEX "IX_SslCertificateOperations_DomainId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809223725_DOMAIN_Final_CertificateTenantIsolationAndRetirement') THEN
    ALTER TABLE "SslCertificates" ADD CONSTRAINT "CK_SslCertificates_Status_Canonical" CHECK ("Status" IN (0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809223725_DOMAIN_Final_CertificateTenantIsolationAndRetirement') THEN
    ALTER TABLE "SslCertificateOperations" ADD CONSTRAINT "FK_SslCertificateOperations_DomainId" FOREIGN KEY ("TenantId", "DomainId") REFERENCES "Domains" ("TenantId", "Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809223725_DOMAIN_Final_CertificateTenantIsolationAndRetirement') THEN
    ALTER TABLE "SslCertificates" ADD CONSTRAINT "FK_SslCertificates_DomainId" FOREIGN KEY ("TenantId", "DomainId") REFERENCES "Domains" ("TenantId", "Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809223725_DOMAIN_Final_CertificateTenantIsolationAndRetirement') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260809223725_DOMAIN_Final_CertificateTenantIsolationAndRetirement', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810114400_SHOPIXY_CheckoutAttachmentFinalization') THEN
    ALTER TABLE "CheckoutAttachments" ADD "FinalChecksumSha256" character varying(128);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810114400_SHOPIXY_CheckoutAttachmentFinalization') THEN
    ALTER TABLE "CheckoutAttachments" ADD "FinalETag" character varying(128);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810114400_SHOPIXY_CheckoutAttachmentFinalization') THEN
    ALTER TABLE "CheckoutAttachments" ADD "FinalObjectKey" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810114400_SHOPIXY_CheckoutAttachmentFinalization') THEN
    ALTER TABLE "CheckoutAttachments" ADD "FinalizedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810114400_SHOPIXY_CheckoutAttachmentFinalization') THEN
    ALTER TABLE "CheckoutAttachments" ADD "StagedETag" character varying(128);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810114400_SHOPIXY_CheckoutAttachmentFinalization') THEN
    CREATE UNIQUE INDEX "UX_CheckoutAttachments_FinalObjectKey" ON "CheckoutAttachments" ("FinalObjectKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810114400_SHOPIXY_CheckoutAttachmentFinalization') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260810114400_SHOPIXY_CheckoutAttachmentFinalization', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810181420_DOMAIN_Final_ForensicHardening') THEN
    ALTER TABLE "Domains" DROP CONSTRAINT "FK_Domains_RedirectTargetDomainId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810181420_DOMAIN_Final_ForensicHardening') THEN
    ALTER TABLE "SslCertificateOperations" DROP CONSTRAINT "FK_SslCertificateOperations_CertificateId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810181420_DOMAIN_Final_ForensicHardening') THEN
    ALTER TABLE "SslCertificates" DROP CONSTRAINT "FK_SslCertificates_ReplacementCertificateId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810181420_DOMAIN_Final_ForensicHardening') THEN
    DROP INDEX "IX_SslCertificates_ReplacementCertificateId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810181420_DOMAIN_Final_ForensicHardening') THEN
    DROP INDEX "IX_SslCertificateOperations_CertificateId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810181420_DOMAIN_Final_ForensicHardening') THEN
    ALTER TABLE "Domains" DROP CONSTRAINT "CK_Domains_Redirect_Cascade";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810181420_DOMAIN_Final_ForensicHardening') THEN
    ALTER TABLE "Domains" ADD "VerificationTokenIssuedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810181420_DOMAIN_Final_ForensicHardening') THEN
    ALTER TABLE "Domains" ADD "CertificateOperationFencingToken" bigint NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810181420_DOMAIN_Final_ForensicHardening') THEN
    UPDATE "Domains" SET "VerificationTokenIssuedAtUtc" = COALESCE("UpdatedAtUtc", "CreatedAtUtc") WHERE "VerificationTokenIssuedAtUtc" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810181420_DOMAIN_Final_ForensicHardening') THEN
    ALTER TABLE "SslCertificates" ADD CONSTRAINT "AK_SslCertificates_TenantId_DomainId_Id" UNIQUE ("TenantId", "DomainId", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810181420_DOMAIN_Final_ForensicHardening') THEN
    CREATE INDEX "IX_SslCertificates_TenantId_DomainId_ReplacementCertificateId" ON "SslCertificates" ("TenantId", "DomainId", "ReplacementCertificateId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810181420_DOMAIN_Final_ForensicHardening') THEN
    CREATE UNIQUE INDEX "UQ_DomainVerifications_DomainId_Attempt" ON "DomainVerifications" ("DomainId", "Attempt") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810181420_DOMAIN_Final_ForensicHardening') THEN
    CREATE INDEX "IX_Domains_TenantId_RedirectTargetDomainId" ON "Domains" ("TenantId", "RedirectTargetDomainId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810181420_DOMAIN_Final_ForensicHardening') THEN
    ALTER TABLE "Domains" ADD CONSTRAINT "CK_Domains_Primary_Type_State_Equivalent" CHECK ((("DomainType" = 0 AND "LifecycleState" = 5) OR ("DomainType" <> 0 AND "LifecycleState" <> 5)));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810181420_DOMAIN_Final_ForensicHardening') THEN
    ALTER TABLE "Domains" ADD CONSTRAINT "CK_Domains_CertificateOperationFencingToken_NonNegative" CHECK ("CertificateOperationFencingToken" >= 0);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810181420_DOMAIN_Final_ForensicHardening') THEN
    ALTER TABLE "Domains" ADD CONSTRAINT "CK_Domains_Redirect_Cascade" CHECK ((("DomainType" = 2 AND "RedirectTargetDomainId" IS NOT NULL AND "RedirectTargetDomainId" <> "Id" AND "RedirectType" <> 0) OR ("DomainType" <> 2 AND "RedirectTargetDomainId" IS NULL AND "RedirectType" = 0)));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810181420_DOMAIN_Final_ForensicHardening') THEN
    ALTER TABLE "Domains" ADD CONSTRAINT "FK_Domains_RedirectTargetDomainId" FOREIGN KEY ("TenantId", "RedirectTargetDomainId") REFERENCES "Domains" ("TenantId", "Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810181420_DOMAIN_Final_ForensicHardening') THEN
    ALTER TABLE "SslCertificateOperations" ADD CONSTRAINT "FK_SslCertificateOperations_CertificateId" FOREIGN KEY ("TenantId", "DomainId", "CertificateId") REFERENCES "SslCertificates" ("TenantId", "DomainId", "Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810181420_DOMAIN_Final_ForensicHardening') THEN
    ALTER TABLE "SslCertificates" ADD CONSTRAINT "FK_SslCertificates_ReplacementCertificateId" FOREIGN KEY ("TenantId", "DomainId", "ReplacementCertificateId") REFERENCES "SslCertificates" ("TenantId", "DomainId", "Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810181420_DOMAIN_Final_ForensicHardening') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260810181420_DOMAIN_Final_ForensicHardening', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260811150644_SHOPIXY_WarehouseResolutionAuthority') THEN
    DO $$
    BEGIN
        IF EXISTS (
            SELECT 1
            FROM "CarrierWarehouseAssignments" assignment
            LEFT JOIN "CarrierAccounts" account
              ON account."TenantId" = assignment."TenantId"
             AND account."Id" = assignment."CarrierAccountId"
            WHERE account."Id" IS NULL
        ) THEN
            RAISE EXCEPTION 'SHOPIXY warehouse authority migration blocked: carrier warehouse assignments contain missing or cross-tenant carrier accounts.';
        END IF;

        IF EXISTS (
            SELECT 1
            FROM "CarrierWarehouseAssignments" assignment
            LEFT JOIN "Warehouses" warehouse
              ON warehouse."TenantId" = assignment."TenantId"
             AND warehouse."Id" = assignment."WarehouseId"
            WHERE warehouse."Id" IS NULL
        ) THEN
            RAISE EXCEPTION 'SHOPIXY warehouse authority migration blocked: carrier warehouse assignments contain missing or cross-tenant warehouses.';
        END IF;
    END $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260811150644_SHOPIXY_WarehouseResolutionAuthority') THEN
    ALTER TABLE "Warehouses" ADD CONSTRAINT "AK_Warehouses_TenantId_Id" UNIQUE ("TenantId", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260811150644_SHOPIXY_WarehouseResolutionAuthority') THEN
    ALTER TABLE "CheckoutSessions" ADD CONSTRAINT "AK_CheckoutSessions_TenantId_Id" UNIQUE ("TenantId", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260811150644_SHOPIXY_WarehouseResolutionAuthority') THEN
    ALTER TABLE "CarrierAccounts" ADD CONSTRAINT "AK_CarrierAccounts_TenantId_Id" UNIQUE ("TenantId", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260811150644_SHOPIXY_WarehouseResolutionAuthority') THEN
    CREATE TABLE "WarehouseResolutions" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CheckoutSessionId" uuid NOT NULL,
        "CartId" uuid NOT NULL,
        "WarehouseId" uuid,
        "Status" character varying(40) NOT NULL,
        "ResolutionSource" character varying(80) NOT NULL,
        "ResolutionVersion" integer NOT NULL,
        "AlgorithmCode" character varying(120) NOT NULL,
        "InputsSnapshotHash" character varying(128) NOT NULL,
        "InputsSnapshotJson" text,
        "FailureCode" character varying(120),
        "InvalidationReason" character varying(200),
        "ResolvedAtUtc" timestamp with time zone,
        "ExpiresAtUtc" timestamp with time zone,
        "LockedAtUtc" timestamp with time zone,
        "InvalidatedAtUtc" timestamp with time zone,
        "ConvertedOrderId" uuid,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_WarehouseResolutions" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_WarehouseResolutions_FailedRequiresCode" CHECK ("Status" <> 'Failed' OR "FailureCode" IS NOT NULL),
        CONSTRAINT "CK_WarehouseResolutions_OrderedRequiresOrder" CHECK ("Status" <> 'Ordered' OR "ConvertedOrderId" IS NOT NULL),
        CONSTRAINT "CK_WarehouseResolutions_ResolutionVersion_Positive" CHECK ("ResolutionVersion" > 0),
        CONSTRAINT "CK_WarehouseResolutions_ResolvedStatusesRequireWarehouse" CHECK ("Status" NOT IN ('Resolved', 'Locked', 'Ordered') OR "WarehouseId" IS NOT NULL),
        CONSTRAINT "FK_WarehouseResolutions_CheckoutSessions_TenantId_CheckoutSess~" FOREIGN KEY ("TenantId", "CheckoutSessionId") REFERENCES "CheckoutSessions" ("TenantId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_WarehouseResolutions_Orders_TenantId_ConvertedOrderId" FOREIGN KEY ("TenantId", "ConvertedOrderId") REFERENCES "Orders" ("TenantId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_WarehouseResolutions_Warehouses_TenantId_WarehouseId" FOREIGN KEY ("TenantId", "WarehouseId") REFERENCES "Warehouses" ("TenantId", "Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260811150644_SHOPIXY_WarehouseResolutionAuthority') THEN
    CREATE INDEX "IX_CarrierWarehouseAssignments_TenantId_WarehouseId" ON "CarrierWarehouseAssignments" ("TenantId", "WarehouseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260811150644_SHOPIXY_WarehouseResolutionAuthority') THEN
    CREATE INDEX "IX_WarehouseResolutions_InputsSnapshotHash" ON "WarehouseResolutions" ("InputsSnapshotHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260811150644_SHOPIXY_WarehouseResolutionAuthority') THEN
    CREATE INDEX "IX_WarehouseResolutions_TenantId" ON "WarehouseResolutions" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260811150644_SHOPIXY_WarehouseResolutionAuthority') THEN
    CREATE INDEX "IX_WarehouseResolutions_TenantId_Cart_Status" ON "WarehouseResolutions" ("TenantId", "CartId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260811150644_SHOPIXY_WarehouseResolutionAuthority') THEN
    CREATE INDEX "IX_WarehouseResolutions_TenantId_ConvertedOrderId" ON "WarehouseResolutions" ("TenantId", "ConvertedOrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260811150644_SHOPIXY_WarehouseResolutionAuthority') THEN
    CREATE INDEX "IX_WarehouseResolutions_TenantId_Expires_Status" ON "WarehouseResolutions" ("TenantId", "ExpiresAtUtc", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260811150644_SHOPIXY_WarehouseResolutionAuthority') THEN
    CREATE UNIQUE INDEX "IX_WarehouseResolutions_TenantId_Session_Active_Unique" ON "WarehouseResolutions" ("TenantId", "CheckoutSessionId") WHERE "Status" IN ('Resolved', 'Locked') AND "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260811150644_SHOPIXY_WarehouseResolutionAuthority') THEN
    CREATE INDEX "IX_WarehouseResolutions_TenantId_Session_Status" ON "WarehouseResolutions" ("TenantId", "CheckoutSessionId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260811150644_SHOPIXY_WarehouseResolutionAuthority') THEN
    CREATE INDEX "IX_WarehouseResolutions_TenantId_Warehouse_Status" ON "WarehouseResolutions" ("TenantId", "WarehouseId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260811150644_SHOPIXY_WarehouseResolutionAuthority') THEN
    CREATE UNIQUE INDEX "UX_WarehouseResolutions_TenantId_Session_Version" ON "WarehouseResolutions" ("TenantId", "CheckoutSessionId", "ResolutionVersion");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260811150644_SHOPIXY_WarehouseResolutionAuthority') THEN
    ALTER TABLE "CarrierWarehouseAssignments" ADD CONSTRAINT "FK_CarrierWarehouseAssignments_CarrierAccounts_TenantId_Carrie~" FOREIGN KEY ("TenantId", "CarrierAccountId") REFERENCES "CarrierAccounts" ("TenantId", "Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260811150644_SHOPIXY_WarehouseResolutionAuthority') THEN
    ALTER TABLE "CarrierWarehouseAssignments" ADD CONSTRAINT "FK_CarrierWarehouseAssignments_Warehouses_TenantId_WarehouseId" FOREIGN KEY ("TenantId", "WarehouseId") REFERENCES "Warehouses" ("TenantId", "Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260811150644_SHOPIXY_WarehouseResolutionAuthority') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260811150644_SHOPIXY_WarehouseResolutionAuthority', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260811160600_SHOPIXY_ShippingMethodFulfillmentWarehouseAuthority') THEN
    ALTER TABLE "ShippingMethods" ADD "FulfillmentWarehouseId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260811160600_SHOPIXY_ShippingMethodFulfillmentWarehouseAuthority') THEN
    CREATE INDEX "IX_ShippingMethods_TenantId_FulfillmentWarehouseId" ON "ShippingMethods" ("TenantId", "FulfillmentWarehouseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260811160600_SHOPIXY_ShippingMethodFulfillmentWarehouseAuthority') THEN
    ALTER TABLE "ShippingMethods" ADD CONSTRAINT "FK_ShippingMethods_Warehouses_TenantId_FulfillmentWarehouseId" FOREIGN KEY ("TenantId", "FulfillmentWarehouseId") REFERENCES "Warehouses" ("TenantId", "Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260811160600_SHOPIXY_ShippingMethodFulfillmentWarehouseAuthority') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260811160600_SHOPIXY_ShippingMethodFulfillmentWarehouseAuthority', '9.0.1');
    END IF;
END $EF$;
COMMIT;

