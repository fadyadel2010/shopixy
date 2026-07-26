BEGIN;

CREATE TABLE IF NOT EXISTS "CommerceHubDailyFinancialAggregates"
(
    "DateUtc" date NOT NULL,
    "CurrencyCode" character varying(10) NOT NULL,
    "BaseCurrencyCode" character varying(10) NOT NULL DEFAULT 'EGP',
    "ExchangeRateToBase" numeric(18,8) NULL,
    "GrossSales" numeric(18,2) NOT NULL DEFAULT 0,
    "GrossCommission" numeric(18,2) NOT NULL DEFAULT 0,
    "PromoCost" numeric(18,2) NOT NULL DEFAULT 0,
    "PromoReversalAmount" numeric(18,2) NOT NULL DEFAULT 0,
    "RefundAmount" numeric(18,2) NOT NULL DEFAULT 0,
    "RefundImpactOnRevenue" numeric(18,2) NOT NULL DEFAULT 0,
    "NetRevenue" numeric(18,2) NOT NULL DEFAULT 0,
    "GrossSalesBase" numeric(18,2) NULL,
    "GrossCommissionBase" numeric(18,2) NULL,
    "PromoCostBase" numeric(18,2) NULL,
    "PromoReversalAmountBase" numeric(18,2) NULL,
    "RefundAmountBase" numeric(18,2) NULL,
    "RefundImpactOnRevenueBase" numeric(18,2) NULL,
    "NetRevenueBase" numeric(18,2) NULL,
    "OrdersCount" integer NOT NULL DEFAULT 0,
    "RefundCount" integer NOT NULL DEFAULT 0,
    "RefundedOrdersCount" integer NOT NULL DEFAULT 0,
    "IsValid" boolean NOT NULL DEFAULT TRUE,
    "LastValidatedAtUtc" timestamp with time zone NULL,
    "ValidationError" text NULL,
    "AggregatedThroughUtc" timestamp with time zone NULL,
    "CalculatedAtUtc" timestamp with time zone NOT NULL DEFAULT NOW(),
    CONSTRAINT "PK_CommerceHubDailyFinancialAggregates" PRIMARY KEY ("DateUtc", "CurrencyCode"),
    CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_GrossSales" CHECK ("GrossSales" >= 0),
    CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_GrossCommission" CHECK ("GrossCommission" >= 0),
    CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_PromoCost" CHECK ("PromoCost" >= 0),
    CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_PromoReversalAmount" CHECK ("PromoReversalAmount" >= 0),
    CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_RefundAmount" CHECK ("RefundAmount" >= 0),
    CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_RefundImpactOnRevenue" CHECK ("RefundImpactOnRevenue" >= 0),
    CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_OrdersCount" CHECK ("OrdersCount" >= 0),
    CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_RefundCount" CHECK ("RefundCount" >= 0),
    CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_RefundedOrdersCount" CHECK ("RefundedOrdersCount" >= 0),
    CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_IsValid" CHECK ("IsValid" IN (TRUE, FALSE))
);

ALTER TABLE "CommerceHubDailyFinancialAggregates"
    ADD COLUMN IF NOT EXISTS "IsValid" boolean NOT NULL DEFAULT TRUE,
    ADD COLUMN IF NOT EXISTS "LastValidatedAtUtc" timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS "ValidationError" text NULL;

ALTER TABLE "CommerceHubDailyFinancialAggregates"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubDailyFinancialAggregates_IsValid";

ALTER TABLE "CommerceHubDailyFinancialAggregates"
    ADD CONSTRAINT "CK_CommerceHubDailyFinancialAggregates_IsValid"
        CHECK ("IsValid" IN (TRUE, FALSE));

CREATE INDEX IF NOT EXISTS "IX_CommerceHubDailyFinancialAggregates_CalculatedAtUtc"
    ON "CommerceHubDailyFinancialAggregates" ("CalculatedAtUtc" DESC);

CREATE INDEX IF NOT EXISTS "IX_CommerceHubDailyFinancialAggregates_CurrencyCode_DateUtc"
    ON "CommerceHubDailyFinancialAggregates" ("CurrencyCode", "DateUtc" DESC);

CREATE INDEX IF NOT EXISTS "IX_CommerceHubDailyFinancialAggregates_IsValid_DateUtc"
    ON "CommerceHubDailyFinancialAggregates" ("IsValid", "DateUtc" DESC);

CREATE INDEX IF NOT EXISTS "IX_CommerceHubFinancialTransactions_ReferenceType_ReferenceId"
    ON "CommerceHubFinancialTransactions" ("ReferenceType", "ReferenceId");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubFinancialTransactions_ReferenceType_TransactionType_Direction_CreatedAtUtc"
    ON "CommerceHubFinancialTransactions" ("ReferenceType", "TransactionType", "Direction", "CreatedAtUtc" DESC);

CREATE INDEX IF NOT EXISTS "IX_CommerceHubMarketplaceOrders_CreatedAt"
    ON "CommerceHubMarketplaceOrders" ("CreatedAt" DESC);

CREATE INDEX IF NOT EXISTS "IX_CommerceHubMarketplaceOrders_PaymentStatus_Status_CreatedAt"
    ON "CommerceHubMarketplaceOrders" ("PaymentStatus", "Status", "CreatedAt" DESC);

CREATE INDEX IF NOT EXISTS "IX_CommerceHubSubOrders_StoreId_CreatedAt"
    ON "CommerceHubSubOrders" ("StoreId", "CreatedAt" DESC);

CREATE INDEX IF NOT EXISTS "IX_CommerceHubSubOrders_CreatedAt"
    ON "CommerceHubSubOrders" ("CreatedAt" DESC);

CREATE INDEX IF NOT EXISTS "IX_PromoCodeUsages_MarketplaceOrderId_AppliedAtUtc"
    ON "PromoCodeUsages" ("MarketplaceOrderId", "AppliedAtUtc" DESC)
    WHERE "IsDeleted" = FALSE;

COMMIT;
