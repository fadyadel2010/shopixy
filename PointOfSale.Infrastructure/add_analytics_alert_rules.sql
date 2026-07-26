-- ===========================================================================
-- Phase 4: Realtime Business Alerts - AnalyticsAlertRules Table
-- 
-- This table stores per-tenant alert rule configurations.
-- Rules are evaluated every 60 seconds by the AnalyticsAlertEvaluatorService.
--
-- Run this in PgAdmin4 to add the new table.
-- ===========================================================================

-- Create AnalyticsAlertRules table
CREATE TABLE IF NOT EXISTS "AnalyticsAlertRules" (
    "RuleId" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Name" varchar(200) NOT NULL,
    "Description" varchar(500) NULL,
    "Metric" integer NOT NULL,
    "Operator" integer NOT NULL,
    "Threshold" numeric(18,4) NOT NULL,
    "WindowMinutes" integer NOT NULL,
    "Severity" integer NOT NULL DEFAULT 2,
    "IsEnabled" boolean NOT NULL DEFAULT true,
    "CooldownMinutes" integer NOT NULL DEFAULT 15,
    "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAtUtc" timestamp with time zone NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_AnalyticsAlertRules" PRIMARY KEY ("RuleId")
);

-- Create index on TenantId
CREATE INDEX IF NOT EXISTS "IX_AnalyticsAlertRules_TenantId" 
    ON "AnalyticsAlertRules" ("TenantId");

-- Create composite index for evaluator queries
CREATE INDEX IF NOT EXISTS "IX_AnalyticsAlertRules_TenantId_IsEnabled_IsDeleted" 
    ON "AnalyticsAlertRules" ("TenantId", "IsEnabled", "IsDeleted");

-- ===========================================================================
-- SEED: Sample Alert Rules (Optional)
-- Uncomment and modify TenantId to seed default rules for a tenant
-- ===========================================================================

-- Example: Zero Sales Alert (30 minutes)
-- INSERT INTO "AnalyticsAlertRules" (
--     "RuleId", "TenantId", "Name", "Description", 
--     "Metric", "Operator", "Threshold", "WindowMinutes", 
--     "Severity", "IsEnabled", "CooldownMinutes", "CreatedAtUtc"
-- ) VALUES (
--     gen_random_uuid(), 
--     'YOUR-TENANT-ID-HERE',
--     'Zero Sales Alert',
--     'Alert when no purchases in 30 minutes',
--     4, -- PurchaseCompleted
--     3, -- Equal
--     0, -- Threshold
--     30, -- WindowMinutes
--     3, -- Critical
--     true,
--     30,
--     CURRENT_TIMESTAMP
-- );

-- Example: High Cart Abandonment Alert
-- INSERT INTO "AnalyticsAlertRules" (
--     "RuleId", "TenantId", "Name", "Description", 
--     "Metric", "Operator", "Threshold", "WindowMinutes", 
--     "Severity", "IsEnabled", "CooldownMinutes", "CreatedAtUtc"
-- ) VALUES (
--     gen_random_uuid(), 
--     'YOUR-TENANT-ID-HERE',
--     'High Cart Abandonment',
--     'Alert when cart abandonment exceeds 80%',
--     6, -- CartAbandonmentRate
--     1, -- GreaterThan
--     80, -- Threshold (80%)
--     60, -- WindowMinutes
--     2, -- Warning
--     true,
--     60,
--     CURRENT_TIMESTAMP
-- );

-- ===========================================================================
-- VERIFICATION QUERY
-- Run this after creating the table to verify it was created correctly:
-- ===========================================================================
-- SELECT table_name, column_name, data_type 
-- FROM information_schema.columns 
-- WHERE table_name = 'AnalyticsAlertRules';

-- ===========================================================================
-- METRIC VALUES REFERENCE
-- ===========================================================================
-- Metric enum values:
-- 1 = PageViews
-- 2 = AddToCart
-- 3 = BeginCheckout
-- 4 = PurchaseCompleted
-- 5 = PaymentFailed
-- 6 = CartAbandonmentRate
-- 7 = ConversionRate
-- 8 = ProductToCartRate
-- 9 = ProductViews

-- Operator enum values:
-- 1 = GreaterThan
-- 2 = LessThan
-- 3 = Equal
-- 4 = GreaterThanOrEqual
-- 5 = LessThanOrEqual

-- Severity enum values:
-- 1 = Info
-- 2 = Warning
-- 3 = Critical
