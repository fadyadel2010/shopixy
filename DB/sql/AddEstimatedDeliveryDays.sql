-- =============================================================================
-- IDEMPOTENT MIGRATION SCRIPT
-- Generated: 2026-01-09
-- Purpose: Apply all pending EF Core migrations with safety checks
-- 
-- This script can be run multiple times safely - it checks if each change
-- already exists before applying it.
-- =============================================================================

-- Helper function to check if column exists
CREATE OR REPLACE FUNCTION column_exists(tbl_name text, col_name text) RETURNS boolean AS $$
BEGIN
    RETURN EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = tbl_name AND column_name = col_name
    );
END;
$$ LANGUAGE plpgsql;

-- Helper function to check if table exists
CREATE OR REPLACE FUNCTION table_exists(tbl_name text) RETURNS boolean AS $$
BEGIN
    RETURN EXISTS (
        SELECT 1 FROM information_schema.tables 
        WHERE table_name = tbl_name
    );
END;
$$ LANGUAGE plpgsql;

-- Helper function to check if index exists
CREATE OR REPLACE FUNCTION index_exists(idx_name text) RETURNS boolean AS $$
BEGIN
    RETURN EXISTS (
        SELECT 1 FROM pg_indexes WHERE indexname = idx_name
    );
END;
$$ LANGUAGE plpgsql;

-- Helper function to check if migration already applied
CREATE OR REPLACE FUNCTION migration_applied(migration_id text) RETURNS boolean AS $$
BEGIN
    RETURN EXISTS (
        SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = migration_id
    );
END;
$$ LANGUAGE plpgsql;

-- =============================================================================
-- START TRANSACTION
-- =============================================================================
START TRANSACTION;

-- =============================================================================
-- MIGRATION: 20251226210606_TenantIsolationHardening
-- =============================================================================
DO $$
BEGIN
    IF NOT migration_applied('20251226210606_TenantIsolationHardening') THEN
        RAISE NOTICE 'Applying migration: TenantIsolationHardening';
        
        -- Drop old index if exists
        IF index_exists('IX_Banks_BankName') THEN
            DROP INDEX "IX_Banks_BankName";
        END IF;
        
        -- Delete test data
        DELETE FROM "FinancialAccounts" WHERE "Id" = '11111111-1111-1111-1111-111111111111';
        DELETE FROM "ShippingMethods" WHERE "Id" IN (
            '11111111-2222-3333-4444-555555555551',
            '11111111-2222-3333-4444-555555555552',
            '11111111-2222-3333-4444-555555555553'
        );
        
        -- Add TenantId columns to tables
        IF NOT column_exists('WarehouseStocks', 'TenantId') THEN
            ALTER TABLE "WarehouseStocks" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF column_exists('Warehouses', 'TenantId') THEN
            UPDATE "Warehouses" SET "TenantId" = '00000000-0000-0000-0000-000000000000' WHERE "TenantId" IS NULL;
            ALTER TABLE "Warehouses" ALTER COLUMN "TenantId" SET NOT NULL;
            ALTER TABLE "Warehouses" ALTER COLUMN "TenantId" SET DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF NOT column_exists('VisaTransactions', 'TenantId') THEN
            ALTER TABLE "VisaTransactions" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF column_exists('Users', 'TenantId') THEN
            UPDATE "Users" SET "TenantId" = '00000000-0000-0000-0000-000000000000' WHERE "TenantId" IS NULL;
            ALTER TABLE "Users" ALTER COLUMN "TenantId" SET NOT NULL;
            ALTER TABLE "Users" ALTER COLUMN "TenantId" SET DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF NOT column_exists('Units', 'TenantId') THEN
            ALTER TABLE "Units" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF NOT column_exists('TreasuryTransactions', 'TenantId') THEN
            ALTER TABLE "TreasuryTransactions" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF NOT column_exists('SupplierTransactions', 'TenantId') THEN
            ALTER TABLE "SupplierTransactions" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF NOT column_exists('Suppliers', 'TenantId') THEN
            ALTER TABLE "Suppliers" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF NOT column_exists('StockTransfers', 'TenantId') THEN
            ALTER TABLE "StockTransfers" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF NOT column_exists('StockMovements', 'TenantId') THEN
            ALTER TABLE "StockMovements" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF NOT column_exists('ShippingMethods', 'TenantId') THEN
            ALTER TABLE "ShippingMethods" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF NOT column_exists('SalesRepresentatives', 'TenantId') THEN
            ALTER TABLE "SalesRepresentatives" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF NOT column_exists('SalesInvoices', 'TenantId') THEN
            ALTER TABLE "SalesInvoices" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF NOT column_exists('Returns', 'TenantId') THEN
            ALTER TABLE "Returns" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF NOT column_exists('PurchaseInvoices', 'TenantId') THEN
            ALTER TABLE "PurchaseInvoices" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF column_exists('Products', 'TenantId') THEN
            UPDATE "Products" SET "TenantId" = '00000000-0000-0000-0000-000000000000' WHERE "TenantId" IS NULL;
            ALTER TABLE "Products" ALTER COLUMN "TenantId" SET NOT NULL;
            ALTER TABLE "Products" ALTER COLUMN "TenantId" SET DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF NOT column_exists('Orders', 'TenantId') THEN
            ALTER TABLE "Orders" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF NOT column_exists('FinancialTransactions', 'TenantId') THEN
            ALTER TABLE "FinancialTransactions" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF NOT column_exists('FinancialAccounts', 'TenantId') THEN
            ALTER TABLE "FinancialAccounts" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF NOT column_exists('Expenses', 'TenantId') THEN
            ALTER TABLE "Expenses" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF NOT column_exists('ExpenseCategories', 'TenantId') THEN
            ALTER TABLE "ExpenseCategories" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF NOT column_exists('CustomerTransactions', 'TenantId') THEN
            ALTER TABLE "CustomerTransactions" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF column_exists('Customers', 'TenantId') THEN
            UPDATE "Customers" SET "TenantId" = '00000000-0000-0000-0000-000000000000' WHERE "TenantId" IS NULL;
            ALTER TABLE "Customers" ALTER COLUMN "TenantId" SET NOT NULL;
            ALTER TABLE "Customers" ALTER COLUMN "TenantId" SET DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF NOT column_exists('Coupons', 'TenantId') THEN
            ALTER TABLE "Coupons" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF column_exists('Categories', 'TenantId') THEN
            UPDATE "Categories" SET "TenantId" = '00000000-0000-0000-0000-000000000000' WHERE "TenantId" IS NULL;
            ALTER TABLE "Categories" ALTER COLUMN "TenantId" SET NOT NULL;
            ALTER TABLE "Categories" ALTER COLUMN "TenantId" SET DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF NOT column_exists('CashboxTransactions', 'TenantId') THEN
            ALTER TABLE "CashboxTransactions" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF NOT column_exists('Cashboxes', 'TenantId') THEN
            ALTER TABLE "Cashboxes" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF NOT column_exists('BankTransactions', 'TenantId') THEN
            ALTER TABLE "BankTransactions" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF NOT column_exists('Banks', 'TenantId') THEN
            ALTER TABLE "Banks" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        IF NOT column_exists('AccountLedgers', 'TenantId') THEN
            ALTER TABLE "AccountLedgers" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
        END IF;
        
        -- Create new index
        IF NOT index_exists('IX_Banks_TenantId_BankName') THEN
            CREATE UNIQUE INDEX "IX_Banks_TenantId_BankName" ON "Banks" ("TenantId", "BankName") WHERE "IsDeleted" = false;
        END IF;
        
        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
        VALUES ('20251226210606_TenantIsolationHardening', '9.0.1');
        
        RAISE NOTICE 'Migration TenantIsolationHardening applied successfully';
    ELSE
        RAISE NOTICE 'Migration TenantIsolationHardening already applied, skipping';
    END IF;
END $$;

-- =============================================================================
-- MIGRATION: 20251226212911_AuditLogTenantId
-- =============================================================================
DO $$
BEGIN
    IF NOT migration_applied('20251226212911_AuditLogTenantId') THEN
        RAISE NOTICE 'Applying migration: AuditLogTenantId';
        
        IF NOT column_exists('AuditLogs', 'TenantId') THEN
            ALTER TABLE "AuditLogs" ADD "TenantId" uuid;
        END IF;
        
        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
        VALUES ('20251226212911_AuditLogTenantId', '9.0.1');
        
        RAISE NOTICE 'Migration AuditLogTenantId applied successfully';
    ELSE
        RAISE NOTICE 'Migration AuditLogTenantId already applied, skipping';
    END IF;
END $$;

-- =============================================================================
-- MIGRATION: 20260102141736_AddOnlineCustomersTable
-- =============================================================================
DO $$
BEGIN
    IF NOT migration_applied('20260102141736_AddOnlineCustomersTable') THEN
        RAISE NOTICE 'Applying migration: AddOnlineCustomersTable';
        
        -- StoreConfigs columns
        IF NOT column_exists('StoreConfigs', 'CustomizationJson') THEN
            ALTER TABLE "StoreConfigs" ADD "CustomizationJson" text NOT NULL DEFAULT '';
        END IF;
        
        IF NOT column_exists('StoreConfigs', 'LiveVersion') THEN
            ALTER TABLE "StoreConfigs" ADD "LiveVersion" integer NOT NULL DEFAULT 0;
        END IF;
        
        IF NOT column_exists('StoreConfigs', 'ThemeDraftOverridesJson') THEN
            ALTER TABLE "StoreConfigs" ADD "ThemeDraftOverridesJson" text NOT NULL DEFAULT '';
        END IF;
        
        IF NOT column_exists('StoreConfigs', 'ThemeLiveOverridesJson') THEN
            ALTER TABLE "StoreConfigs" ADD "ThemeLiveOverridesJson" text NOT NULL DEFAULT '';
        END IF;
        
        IF NOT column_exists('StoreConfigs', 'ThemePresetKey') THEN
            ALTER TABLE "StoreConfigs" ADD "ThemePresetKey" text NOT NULL DEFAULT '';
        END IF;
        
        IF NOT column_exists('StoreConfigs', 'VersionsJson') THEN
            ALTER TABLE "StoreConfigs" ADD "VersionsJson" text NOT NULL DEFAULT '';
        END IF;
        
        -- OnlineCustomers table
        IF NOT table_exists('OnlineCustomers') THEN
            CREATE TABLE "OnlineCustomers" (
                "Id" uuid NOT NULL,
                "TenantId" uuid NOT NULL,
                "Email" character varying(255) NOT NULL,
                "PasswordHash" character varying(255),
                "Provider" character varying(20) NOT NULL,
                "ProviderUserId" character varying(255),
                "DisplayName" character varying(200),
                "Phone" character varying(50),
                "IsActive" boolean NOT NULL,
                "EmailVerified" boolean NOT NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "LastLoginAtUtc" timestamp with time zone,
                "UpdatedAtUtc" timestamp with time zone,
                CONSTRAINT "PK_OnlineCustomers" PRIMARY KEY ("Id")
            );
        END IF;
        
        -- ThemePresets table
        IF NOT table_exists('ThemePresets') THEN
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
            
            -- Insert default presets
            INSERT INTO "ThemePresets" ("Id", "BaseThemeJson", "CreatedAtUtc", "IsRtl", "Key", "Name", "PreviewImageUrl")
            VALUES ('11111111-1111-1111-1111-111111111111', '{"colors":{"primary":"#2563eb","secondary":"#0f172a","background":"#ffffff","text":"#020617","muted":"#64748b"},"typography":{"fontFamily":"Inter","baseSize":"16px"},"radius":{"base":"8px"}}', TIMESTAMPTZ '2024-01-01T00:00:00Z', FALSE, 'default', 'Default Modern', NULL)
            ON CONFLICT DO NOTHING;
            
            INSERT INTO "ThemePresets" ("Id", "BaseThemeJson", "CreatedAtUtc", "IsRtl", "Key", "Name", "PreviewImageUrl")
            VALUES ('22222222-2222-2222-2222-222222222222', '{"colors":{"primary":"#3b82f6","secondary":"#1e293b","background":"#0f172a","text":"#f8fafc","muted":"#94a3b8"},"typography":{"fontFamily":"Inter","baseSize":"16px"},"radius":{"base":"12px"}}', TIMESTAMPTZ '2024-01-01T00:00:00Z', FALSE, 'dark-minimal', 'Dark Minimal', NULL)
            ON CONFLICT DO NOTHING;
        END IF;
        
        -- Indexes for OnlineCustomers
        IF NOT index_exists('IX_OnlineCustomers_CreatedAtUtc') THEN
            CREATE INDEX "IX_OnlineCustomers_CreatedAtUtc" ON "OnlineCustomers" ("CreatedAtUtc");
        END IF;
        
        IF NOT index_exists('IX_OnlineCustomers_EmailVerified') THEN
            CREATE INDEX "IX_OnlineCustomers_EmailVerified" ON "OnlineCustomers" ("EmailVerified");
        END IF;
        
        IF NOT index_exists('IX_OnlineCustomers_IsActive') THEN
            CREATE INDEX "IX_OnlineCustomers_IsActive" ON "OnlineCustomers" ("IsActive");
        END IF;
        
        IF NOT index_exists('IX_OnlineCustomers_Provider') THEN
            CREATE INDEX "IX_OnlineCustomers_Provider" ON "OnlineCustomers" ("Provider");
        END IF;
        
        IF NOT index_exists('IX_OnlineCustomers_TenantId') THEN
            CREATE INDEX "IX_OnlineCustomers_TenantId" ON "OnlineCustomers" ("TenantId");
        END IF;
        
        IF NOT index_exists('IX_OnlineCustomers_TenantId_Email') THEN
            CREATE UNIQUE INDEX "IX_OnlineCustomers_TenantId_Email" ON "OnlineCustomers" ("TenantId", "Email");
        END IF;
        
        IF NOT index_exists('IX_OnlineCustomers_TenantId_Provider_ProviderUserId') THEN
            CREATE INDEX "IX_OnlineCustomers_TenantId_Provider_ProviderUserId" ON "OnlineCustomers" ("TenantId", "Provider", "ProviderUserId");
        END IF;
        
        IF NOT index_exists('IX_ThemePresets_Key') THEN
            CREATE UNIQUE INDEX "IX_ThemePresets_Key" ON "ThemePresets" ("Key");
        END IF;
        
        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
        VALUES ('20260102141736_AddOnlineCustomersTable', '9.0.1');
        
        RAISE NOTICE 'Migration AddOnlineCustomersTable applied successfully';
    ELSE
        RAISE NOTICE 'Migration AddOnlineCustomersTable already applied, skipping';
    END IF;
END $$;

-- =============================================================================
-- MIGRATION: 20260104210031_AddOnlineCustomerWishlistItem
-- =============================================================================
DO $$
BEGIN
    IF NOT migration_applied('20260104210031_AddOnlineCustomerWishlistItem') THEN
        RAISE NOTICE 'Applying migration: AddOnlineCustomerWishlistItem';
        
        -- OnlineCustomers additional columns
        IF NOT column_exists('OnlineCustomers', 'EmailVerifiedAtUtc') THEN
            ALTER TABLE "OnlineCustomers" ADD "EmailVerifiedAtUtc" timestamp with time zone;
        END IF;
        
        IF NOT column_exists('OnlineCustomers', 'NormalizedEmail') THEN
            ALTER TABLE "OnlineCustomers" ADD "NormalizedEmail" text NOT NULL DEFAULT '';
        END IF;
        
        -- OnlineCustomerAuthProviders table
        IF NOT table_exists('OnlineCustomerAuthProviders') THEN
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
        
        -- OnlineCustomerEmailVerifications table
        IF NOT table_exists('OnlineCustomerEmailVerifications') THEN
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
        
        -- OnlineCustomerPasswordResetTokens table
        IF NOT table_exists('OnlineCustomerPasswordResetTokens') THEN
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
        
        -- OnlineCustomerRefreshTokens table
        IF NOT table_exists('OnlineCustomerRefreshTokens') THEN
            CREATE TABLE "OnlineCustomerRefreshTokens" (
                "Id" uuid NOT NULL,
                "TenantId" uuid NOT NULL,
                "OnlineCustomerId" uuid NOT NULL,
                "TokenHash" character varying(128) NOT NULL,
                "ExpiresAtUtc" timestamp with time zone NOT NULL,
                "IsRevoked" boolean NOT NULL DEFAULT FALSE,
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
        
        -- OnlineCustomerWishlistItems table
        IF NOT table_exists('OnlineCustomerWishlistItems') THEN
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
        
        -- StorefrontAuthProviderSettings table
        IF NOT table_exists('StorefrontAuthProviderSettings') THEN
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
        
        -- Create all indexes if not exists
        IF NOT index_exists('IX_AuthProvider_TenantId_CustomerId') THEN
            CREATE INDEX "IX_AuthProvider_TenantId_CustomerId" ON "OnlineCustomerAuthProviders" ("TenantId", "OnlineCustomerId");
        END IF;
        
        IF NOT index_exists('IX_AuthProvider_TenantId_CustomerId_Provider') THEN
            CREATE UNIQUE INDEX "IX_AuthProvider_TenantId_CustomerId_Provider" ON "OnlineCustomerAuthProviders" ("TenantId", "OnlineCustomerId", "Provider");
        END IF;
        
        IF NOT index_exists('IX_AuthProvider_TenantId_Provider_ProviderUserId') THEN
            CREATE UNIQUE INDEX "IX_AuthProvider_TenantId_Provider_ProviderUserId" ON "OnlineCustomerAuthProviders" ("TenantId", "Provider", "ProviderUserId");
        END IF;
        
        IF NOT index_exists('IX_OnlineCustomerAuthProviders_OnlineCustomerId') THEN
            CREATE INDEX "IX_OnlineCustomerAuthProviders_OnlineCustomerId" ON "OnlineCustomerAuthProviders" ("OnlineCustomerId");
        END IF;
        
        IF NOT index_exists('IX_EmailVerification_ExpiresAtUtc') THEN
            CREATE INDEX "IX_EmailVerification_ExpiresAtUtc" ON "OnlineCustomerEmailVerifications" ("ExpiresAtUtc");
        END IF;
        
        IF NOT index_exists('IX_EmailVerification_TenantId_CustomerId_IsUsed') THEN
            CREATE INDEX "IX_EmailVerification_TenantId_CustomerId_IsUsed" ON "OnlineCustomerEmailVerifications" ("TenantId", "OnlineCustomerId", "IsUsed");
        END IF;
        
        IF NOT index_exists('IX_EmailVerification_TenantId_TokenHash') THEN
            CREATE UNIQUE INDEX "IX_EmailVerification_TenantId_TokenHash" ON "OnlineCustomerEmailVerifications" ("TenantId", "TokenHash");
        END IF;
        
        IF NOT index_exists('IX_OnlineCustomerEmailVerifications_OnlineCustomerId') THEN
            CREATE INDEX "IX_OnlineCustomerEmailVerifications_OnlineCustomerId" ON "OnlineCustomerEmailVerifications" ("OnlineCustomerId");
        END IF;
        
        IF NOT index_exists('IX_OnlineCustomerPasswordResetTokens_OnlineCustomerId') THEN
            CREATE INDEX "IX_OnlineCustomerPasswordResetTokens_OnlineCustomerId" ON "OnlineCustomerPasswordResetTokens" ("OnlineCustomerId");
        END IF;
        
        IF NOT index_exists('IX_PasswordResetToken_ExpiresAtUtc') THEN
            CREATE INDEX "IX_PasswordResetToken_ExpiresAtUtc" ON "OnlineCustomerPasswordResetTokens" ("ExpiresAtUtc");
        END IF;
        
        IF NOT index_exists('IX_PasswordResetToken_TenantId_CustomerId_IsUsed') THEN
            CREATE INDEX "IX_PasswordResetToken_TenantId_CustomerId_IsUsed" ON "OnlineCustomerPasswordResetTokens" ("TenantId", "OnlineCustomerId", "IsUsed");
        END IF;
        
        IF NOT index_exists('IX_PasswordResetToken_TenantId_TokenHash') THEN
            CREATE UNIQUE INDEX "IX_PasswordResetToken_TenantId_TokenHash" ON "OnlineCustomerPasswordResetTokens" ("TenantId", "TokenHash");
        END IF;
        
        IF NOT index_exists('IX_OnlineCustomerRefreshTokens_OnlineCustomerId') THEN
            CREATE INDEX "IX_OnlineCustomerRefreshTokens_OnlineCustomerId" ON "OnlineCustomerRefreshTokens" ("OnlineCustomerId");
        END IF;
        
        IF NOT index_exists('IX_RefreshToken_ExpiresAtUtc') THEN
            CREATE INDEX "IX_RefreshToken_ExpiresAtUtc" ON "OnlineCustomerRefreshTokens" ("ExpiresAtUtc");
        END IF;
        
        IF NOT index_exists('IX_RefreshToken_TenantId_CustomerId') THEN
            CREATE INDEX "IX_RefreshToken_TenantId_CustomerId" ON "OnlineCustomerRefreshTokens" ("TenantId", "OnlineCustomerId");
        END IF;
        
        IF NOT index_exists('IX_RefreshToken_TenantId_CustomerId_IsRevoked') THEN
            CREATE INDEX "IX_RefreshToken_TenantId_CustomerId_IsRevoked" ON "OnlineCustomerRefreshTokens" ("TenantId", "OnlineCustomerId", "IsRevoked");
        END IF;
        
        IF NOT index_exists('IX_RefreshToken_TenantId_TokenHash') THEN
            CREATE UNIQUE INDEX "IX_RefreshToken_TenantId_TokenHash" ON "OnlineCustomerRefreshTokens" ("TenantId", "TokenHash");
        END IF;
        
        IF NOT index_exists('IX_OnlineCustomerWishlistItems_CreatedAtUtc') THEN
            CREATE INDEX "IX_OnlineCustomerWishlistItems_CreatedAtUtc" ON "OnlineCustomerWishlistItems" ("CreatedAtUtc");
        END IF;
        
        IF NOT index_exists('IX_OnlineCustomerWishlistItems_TenantId_CustomerId') THEN
            CREATE INDEX "IX_OnlineCustomerWishlistItems_TenantId_CustomerId" ON "OnlineCustomerWishlistItems" ("TenantId", "OnlineCustomerId");
        END IF;
        
        IF NOT index_exists('IX_OnlineCustomerWishlistItems_TenantId_CustomerId_ProductId') THEN
            CREATE UNIQUE INDEX "IX_OnlineCustomerWishlistItems_TenantId_CustomerId_ProductId" ON "OnlineCustomerWishlistItems" ("TenantId", "OnlineCustomerId", "ProductId") WHERE "IsRemoved" = false;
        END IF;
        
        IF NOT index_exists('IX_OnlineCustomerWishlistItems_TenantId_IsRemoved') THEN
            CREATE INDEX "IX_OnlineCustomerWishlistItems_TenantId_IsRemoved" ON "OnlineCustomerWishlistItems" ("TenantId", "IsRemoved");
        END IF;
        
        IF NOT index_exists('IX_OnlineCustomerWishlistItems_TenantId_ProductId') THEN
            CREATE INDEX "IX_OnlineCustomerWishlistItems_TenantId_ProductId" ON "OnlineCustomerWishlistItems" ("TenantId", "ProductId");
        END IF;
        
        IF NOT index_exists('IX_AuthProviderSettings_TenantId_Provider') THEN
            CREATE UNIQUE INDEX "IX_AuthProviderSettings_TenantId_Provider" ON "StorefrontAuthProviderSettings" ("TenantId", "Provider");
        END IF;
        
        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
        VALUES ('20260104210031_AddOnlineCustomerWishlistItem', '9.0.1');
        
        RAISE NOTICE 'Migration AddOnlineCustomerWishlistItem applied successfully';
    ELSE
        RAISE NOTICE 'Migration AddOnlineCustomerWishlistItem already applied, skipping';
    END IF;
END $$;

-- =============================================================================
-- MIGRATION: 20260105201157_AddOnlinePriceToProduct
-- =============================================================================
DO $$
BEGIN
    IF NOT migration_applied('20260105201157_AddOnlinePriceToProduct') THEN
        RAISE NOTICE 'Applying migration: AddOnlinePriceToProduct';
        
        IF NOT column_exists('Products', 'OnlinePrice') THEN
            ALTER TABLE "Products" ADD "OnlinePrice" numeric;
        END IF;
        
        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
        VALUES ('20260105201157_AddOnlinePriceToProduct', '9.0.1');
        
        RAISE NOTICE 'Migration AddOnlinePriceToProduct applied successfully';
    ELSE
        RAISE NOTICE 'Migration AddOnlinePriceToProduct already applied, skipping';
    END IF;
END $$;

-- =============================================================================
-- MIGRATION: 20260108143906_AddShippingSnapshotFields
-- =============================================================================
DO $$
BEGIN
    IF NOT migration_applied('20260108143906_AddShippingSnapshotFields') THEN
        RAISE NOTICE 'Applying migration: AddShippingSnapshotFields';
        
        IF NOT column_exists('Orders', 'EstimatedDeliveryDays') THEN
            ALTER TABLE "Orders" ADD "EstimatedDeliveryDays" text;
        END IF;
        
        IF NOT column_exists('Orders', 'ShippingMethodName') THEN
            ALTER TABLE "Orders" ADD "ShippingMethodName" text;
        END IF;
        
        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
        VALUES ('20260108143906_AddShippingSnapshotFields', '9.0.1');
        
        RAISE NOTICE 'Migration AddShippingSnapshotFields applied successfully';
    ELSE
        RAISE NOTICE 'Migration AddShippingSnapshotFields already applied, skipping';
    END IF;
END $$;

-- =============================================================================
-- MIGRATION: 20260108155753_AddTenantPaymentMethods
-- =============================================================================
DO $$
BEGIN
    IF NOT migration_applied('20260108155753_AddTenantPaymentMethods') THEN
        RAISE NOTICE 'Applying migration: AddTenantPaymentMethods';
        
        IF NOT table_exists('TenantPaymentMethods') THEN
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
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone,
                "CreatedBy" uuid,
                "UpdatedBy" uuid,
                "IsDeleted" boolean NOT NULL,
                CONSTRAINT "PK_TenantPaymentMethods" PRIMARY KEY ("Id")
            );
        END IF;
        
        IF NOT index_exists('IX_TenantPaymentMethods_TenantId') THEN
            CREATE INDEX "IX_TenantPaymentMethods_TenantId" ON "TenantPaymentMethods" ("TenantId");
        END IF;
        
        IF NOT index_exists('IX_TenantPaymentMethods_TenantId_Code') THEN
            CREATE UNIQUE INDEX "IX_TenantPaymentMethods_TenantId_Code" ON "TenantPaymentMethods" ("TenantId", "Code");
        END IF;
        
        IF NOT index_exists('IX_TenantPaymentMethods_TenantId_IsEnabled') THEN
            CREATE INDEX "IX_TenantPaymentMethods_TenantId_IsEnabled" ON "TenantPaymentMethods" ("TenantId", "IsEnabled");
        END IF;
        
        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
        VALUES ('20260108155753_AddTenantPaymentMethods', '9.0.1');
        
        RAISE NOTICE 'Migration AddTenantPaymentMethods applied successfully';
    ELSE
        RAISE NOTICE 'Migration AddTenantPaymentMethods already applied, skipping';
    END IF;
END $$;

-- =============================================================================
-- MIGRATION: 20260109135134_AddEstimatedDeliveryDaysToOrder
-- =============================================================================
DO $$
BEGIN
    IF NOT migration_applied('20260109135134_AddEstimatedDeliveryDaysToOrder') THEN
        RAISE NOTICE 'Applying migration: AddEstimatedDeliveryDaysToOrder';
        
        -- Note: EstimatedDeliveryDays was already added in AddShippingSnapshotFields migration
        -- Only add payment-related columns
        
        IF NOT column_exists('Orders', 'IsPaid') THEN
            ALTER TABLE "Orders" ADD "IsPaid" boolean NOT NULL DEFAULT FALSE;
        END IF;
        
        IF NOT column_exists('Orders', 'PaymentFee') THEN
            ALTER TABLE "Orders" ADD "PaymentFee" numeric NOT NULL DEFAULT 0.0;
        END IF;
        
        IF NOT column_exists('Orders', 'PaymentMethodCode') THEN
            ALTER TABLE "Orders" ADD "PaymentMethodCode" text NOT NULL DEFAULT '';
        END IF;
        
        IF NOT column_exists('Orders', 'PaymentMethodName') THEN
            ALTER TABLE "Orders" ADD "PaymentMethodName" text;
        END IF;
        
        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
        VALUES ('20260109135134_AddEstimatedDeliveryDaysToOrder', '9.0.1');
        
        RAISE NOTICE 'Migration AddEstimatedDeliveryDaysToOrder applied successfully';
    ELSE
        RAISE NOTICE 'Migration AddEstimatedDeliveryDaysToOrder already applied, skipping';
    END IF;
END $$;

-- =============================================================================
-- COMMIT TRANSACTION
-- =============================================================================
COMMIT;

-- =============================================================================
-- CLEANUP: Drop helper functions
-- =============================================================================
DROP FUNCTION IF EXISTS column_exists(text, text);
DROP FUNCTION IF EXISTS table_exists(text);
DROP FUNCTION IF EXISTS index_exists(text);
DROP FUNCTION IF EXISTS migration_applied(text);

-- =============================================================================
-- DONE
-- =============================================================================
DO $$
BEGIN
    RAISE NOTICE '===========================================';
    RAISE NOTICE 'All migrations processed successfully!';
    RAISE NOTICE '===========================================';
END $$;
