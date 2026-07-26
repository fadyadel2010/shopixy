START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    DROP INDEX IF EXISTS "IX_StockTransfers_TenantId_TransferNumber";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    DROP INDEX IF EXISTS "IX_SalesInvoicePayments_SalesInvoiceId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    DROP INDEX IF EXISTS "IX_SalesInvoiceItems_SalesInvoiceId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    DROP INDEX IF EXISTS "IX_Returns_TenantId_ReturnNumber";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    ALTER TABLE "Suppliers" ALTER COLUMN "TaxNumber" TYPE character varying(50);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    ALTER TABLE "Suppliers" ALTER COLUMN "Phone" TYPE character varying(50);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    ALTER TABLE "Suppliers" ALTER COLUMN "Name" TYPE character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    ALTER TABLE "Suppliers" ALTER COLUMN "Email" TYPE character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    ALTER TABLE "Suppliers" ALTER COLUMN "Balance" TYPE numeric(18,2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    -- Add ContentHash column with legacy sentinel as default for existing rows
    -- Format: 'LEGACY_' + 57 zeros to maintain 64-char SHA-256 format (7 + 57 = 64)
    ALTER TABLE "CommerceHubVendorPaymentProofs" ADD COLUMN IF NOT EXISTS "ContentHash" character varying(64) NOT NULL DEFAULT ('LEGACY_' || repeat('0', 57));
    
    -- Remove the default constraint immediately after populating existing rows
    -- This ensures the legacy sentinel is NOT used for future inserts
    ALTER TABLE "CommerceHubVendorPaymentProofs" ALTER COLUMN "ContentHash" DROP DEFAULT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    CREATE INDEX IF NOT EXISTS "IX_WarehouseStocks_TenantId_Quantity" ON "WarehouseStocks" ("TenantId", "Quantity");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    CREATE INDEX IF NOT EXISTS "IX_WarehouseStocks_TenantId_WarehouseId_Quantity" ON "WarehouseStocks" ("TenantId", "WarehouseId", "Quantity");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    CREATE INDEX IF NOT EXISTS "IX_Suppliers_Email" ON "Suppliers" ("Email");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    CREATE INDEX IF NOT EXISTS "IX_Suppliers_Phone" ON "Suppliers" ("Phone");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    CREATE INDEX IF NOT EXISTS "IX_Suppliers_TaxNumber" ON "Suppliers" ("TaxNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    CREATE INDEX IF NOT EXISTS "IX_Suppliers_TenantId" ON "Suppliers" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    CREATE UNIQUE INDEX IF NOT EXISTS "IX_StockTransfers_TenantId_TransferNumber" ON "StockTransfers" ("TenantId", "TransferNumber") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    CREATE INDEX IF NOT EXISTS "IX_SalesInvoices_TenantId_CreatedAt" ON "SalesInvoices" ("TenantId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    CREATE INDEX IF NOT EXISTS "IX_SalesInvoices_TenantId_CustomerId_InvoiceDate" ON "SalesInvoices" ("TenantId", "CustomerId", "InvoiceDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    CREATE INDEX IF NOT EXISTS "IX_SalesInvoices_TenantId_InvoiceDate_Active" ON "SalesInvoices" ("TenantId", "InvoiceDate") WHERE "IsDeleted" = false AND "Status" <> 2;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    CREATE INDEX IF NOT EXISTS "IX_SalesInvoices_TenantId_Status_InvoiceDate" ON "SalesInvoices" ("TenantId", "Status", "InvoiceDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    CREATE INDEX IF NOT EXISTS "IX_SalesInvoices_TenantId_WarehouseId_InvoiceDate" ON "SalesInvoices" ("TenantId", "WarehouseId", "InvoiceDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    CREATE INDEX IF NOT EXISTS "IX_SalesInvoicePayments_PaymentMethod_PaymentDate" ON "SalesInvoicePayments" ("PaymentMethod", "PaymentDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    CREATE INDEX IF NOT EXISTS "IX_SalesInvoicePayments_SalesInvoiceId_PaymentDate" ON "SalesInvoicePayments" ("SalesInvoiceId", "PaymentDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    CREATE INDEX IF NOT EXISTS "IX_SalesInvoiceItems_SalesInvoiceId_ProductId" ON "SalesInvoiceItems" ("SalesInvoiceId", "ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    CREATE INDEX IF NOT EXISTS "IX_SalesInvoiceItems_SalesInvoiceId_ProductVariantId" ON "SalesInvoiceItems" ("SalesInvoiceId", "ProductVariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    CREATE UNIQUE INDEX IF NOT EXISTS "IX_Returns_TenantId_ReturnNumber" ON "Returns" ("TenantId", "ReturnNumber") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    CREATE INDEX IF NOT EXISTS "IX_Products_TenantId" ON "Products" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    CREATE INDEX IF NOT EXISTS "IX_Products_TenantId_CategoryId" ON "Products" ("TenantId", "CategoryId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    CREATE INDEX IF NOT EXISTS "IX_Orders_TenantId_PaymentStatus_CreatedAt" ON "Orders" ("TenantId", "PaymentStatus", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    CREATE INDEX IF NOT EXISTS "IX_Orders_TenantId_Source_CreatedAt" ON "Orders" ("TenantId", "Source", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    CREATE INDEX IF NOT EXISTS "IX_Orders_TenantId_Status_CreatedAt" ON "Orders" ("TenantId", "Status", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    CREATE INDEX IF NOT EXISTS "IX_Customers_TenantId" ON "Customers" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    CREATE INDEX IF NOT EXISTS "IX_CheckoutSessions_TenantId_Status_AbandonedAtUtc" ON "CheckoutSessions" ("TenantId", "Status", "AbandonedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260409190921_AddDashboardPerformanceIndexes') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260409190921_AddDashboardPerformanceIndexes', '9.0.1');
    END IF;
END $EF$;
COMMIT;

