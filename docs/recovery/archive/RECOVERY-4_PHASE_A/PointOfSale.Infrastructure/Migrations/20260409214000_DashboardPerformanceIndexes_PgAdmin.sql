-- Dashboard / reporting performance indexes
-- PgAdmin4-ready script
-- 
-- EXECUTION NOTES:
-- This script uses a safe drop-and-create pattern that checks index definitions.
-- If an index exists with a different definition, it will be dropped and recreated.
-- This ensures the migration enforces the intended definitions instead of silently
-- leaving stale indexes.
-- 
-- TRADE-OFF: Regular CREATE INDEX will briefly lock the table for writes.
-- If you need CONCURRENTLY (for production with active traffic):
--   1. Run this script using psql command line with autocommit
--   2. Or execute each block manually one at a time in PgAdmin
--
-- Notes:
-- 1. This script is intentionally scoped to performance indexes only.
-- 2. It does NOT include unrelated EF model drift.
-- 3. SalesInvoices partial index excludes Cancelled rows where Status = 2.
-- 4. Uses pg_get_indexdef to verify index definitions match expectations.

-- Helper function to safely recreate index if definition differs
DO $$
DECLARE
    v_current_def TEXT;
BEGIN
    -- Core tenant-scoped aggregates
    
    -- IX_Customers_TenantId
    SELECT pg_get_indexdef('"IX_Customers_TenantId"'::regclass) INTO v_current_def;
    IF v_current_def IS NULL OR v_current_def NOT LIKE '%"TenantId"%' THEN
        DROP INDEX IF EXISTS "IX_Customers_TenantId";
        CREATE INDEX "IX_Customers_TenantId" ON "Customers" ("TenantId");
    END IF;

    -- IX_Suppliers_TenantId
    SELECT pg_get_indexdef('"IX_Suppliers_TenantId"'::regclass) INTO v_current_def;
    IF v_current_def IS NULL OR v_current_def NOT LIKE '%"TenantId"%' THEN
        DROP INDEX IF EXISTS "IX_Suppliers_TenantId";
        CREATE INDEX "IX_Suppliers_TenantId" ON "Suppliers" ("TenantId");
    END IF;

    -- IX_Products_TenantId
    SELECT pg_get_indexdef('"IX_Products_TenantId"'::regclass) INTO v_current_def;
    IF v_current_def IS NULL OR v_current_def NOT LIKE '%"TenantId"%' THEN
        DROP INDEX IF EXISTS "IX_Products_TenantId";
        CREATE INDEX "IX_Products_TenantId" ON "Products" ("TenantId");
    END IF;

    -- IX_Products_TenantId_CategoryId
    SELECT pg_get_indexdef('"IX_Products_TenantId_CategoryId"'::regclass) INTO v_current_def;
    IF v_current_def IS NULL OR v_current_def NOT LIKE '%"TenantId", "CategoryId"%' THEN
        DROP INDEX IF EXISTS "IX_Products_TenantId_CategoryId";
        CREATE INDEX "IX_Products_TenantId_CategoryId" ON "Products" ("TenantId", "CategoryId");
    END IF;

    -- POS invoice dashboard / report filters
    
    -- IX_SalesInvoices_TenantId_InvoiceDate_Active
    SELECT pg_get_indexdef('"IX_SalesInvoices_TenantId_InvoiceDate_Active"'::regclass) INTO v_current_def;
    IF v_current_def IS NULL OR 
       v_current_def NOT LIKE '%"TenantId", "InvoiceDate"%' OR
       v_current_def NOT LIKE '%WHERE%"IsDeleted"%FALSE%"Status"%2%' THEN
        DROP INDEX IF EXISTS "IX_SalesInvoices_TenantId_InvoiceDate_Active";
        CREATE INDEX "IX_SalesInvoices_TenantId_InvoiceDate_Active"
            ON "SalesInvoices" ("TenantId", "InvoiceDate")
            WHERE "IsDeleted" = FALSE AND "Status" <> 2;
    END IF;

    -- IX_SalesInvoices_TenantId_Status_InvoiceDate
    SELECT pg_get_indexdef('"IX_SalesInvoices_TenantId_Status_InvoiceDate"'::regclass) INTO v_current_def;
    IF v_current_def IS NULL OR v_current_def NOT LIKE '%"TenantId", "Status", "InvoiceDate"%' THEN
        DROP INDEX IF EXISTS "IX_SalesInvoices_TenantId_Status_InvoiceDate";
        CREATE INDEX "IX_SalesInvoices_TenantId_Status_InvoiceDate"
            ON "SalesInvoices" ("TenantId", "Status", "InvoiceDate");
    END IF;

    -- IX_SalesInvoices_TenantId_CustomerId_InvoiceDate
    SELECT pg_get_indexdef('"IX_SalesInvoices_TenantId_CustomerId_InvoiceDate"'::regclass) INTO v_current_def;
    IF v_current_def IS NULL OR v_current_def NOT LIKE '%"TenantId", "CustomerId", "InvoiceDate"%' THEN
        DROP INDEX IF EXISTS "IX_SalesInvoices_TenantId_CustomerId_InvoiceDate";
        CREATE INDEX "IX_SalesInvoices_TenantId_CustomerId_InvoiceDate"
            ON "SalesInvoices" ("TenantId", "CustomerId", "InvoiceDate");
    END IF;

    -- IX_SalesInvoices_TenantId_WarehouseId_InvoiceDate
    SELECT pg_get_indexdef('"IX_SalesInvoices_TenantId_WarehouseId_InvoiceDate"'::regclass) INTO v_current_def;
    IF v_current_def IS NULL OR v_current_def NOT LIKE '%"TenantId", "WarehouseId", "InvoiceDate"%' THEN
        DROP INDEX IF EXISTS "IX_SalesInvoices_TenantId_WarehouseId_InvoiceDate";
        CREATE INDEX "IX_SalesInvoices_TenantId_WarehouseId_InvoiceDate"
            ON "SalesInvoices" ("TenantId", "WarehouseId", "InvoiceDate");
    END IF;

    -- IX_SalesInvoices_TenantId_CreatedAt
    SELECT pg_get_indexdef('"IX_SalesInvoices_TenantId_CreatedAt"'::regclass) INTO v_current_def;
    IF v_current_def IS NULL OR v_current_def NOT LIKE '%"TenantId", "CreatedAt"%' THEN
        DROP INDEX IF EXISTS "IX_SalesInvoices_TenantId_CreatedAt";
        CREATE INDEX "IX_SalesInvoices_TenantId_CreatedAt"
            ON "SalesInvoices" ("TenantId", "CreatedAt");
    END IF;

    -- Invoice line analytics / grouping
    
    -- IX_SalesInvoiceItems_SalesInvoiceId_ProductId
    SELECT pg_get_indexdef('"IX_SalesInvoiceItems_SalesInvoiceId_ProductId"'::regclass) INTO v_current_def;
    IF v_current_def IS NULL OR v_current_def NOT LIKE '%"SalesInvoiceId", "ProductId"%' THEN
        DROP INDEX IF EXISTS "IX_SalesInvoiceItems_SalesInvoiceId_ProductId";
        CREATE INDEX "IX_SalesInvoiceItems_SalesInvoiceId_ProductId"
            ON "SalesInvoiceItems" ("SalesInvoiceId", "ProductId");
    END IF;

    -- IX_SalesInvoiceItems_SalesInvoiceId_ProductVariantId
    SELECT pg_get_indexdef('"IX_SalesInvoiceItems_SalesInvoiceId_ProductVariantId"'::regclass) INTO v_current_def;
    IF v_current_def IS NULL OR v_current_def NOT LIKE '%"SalesInvoiceId", "ProductVariantId"%' THEN
        DROP INDEX IF EXISTS "IX_SalesInvoiceItems_SalesInvoiceId_ProductVariantId";
        CREATE INDEX "IX_SalesInvoiceItems_SalesInvoiceId_ProductVariantId"
            ON "SalesInvoiceItems" ("SalesInvoiceId", "ProductVariantId");
    END IF;

    -- Payment method breakdown
    
    -- IX_SalesInvoicePayments_SalesInvoiceId_PaymentDate
    SELECT pg_get_indexdef('"IX_SalesInvoicePayments_SalesInvoiceId_PaymentDate"'::regclass) INTO v_current_def;
    IF v_current_def IS NULL OR v_current_def NOT LIKE '%"SalesInvoiceId", "PaymentDate"%' THEN
        DROP INDEX IF EXISTS "IX_SalesInvoicePayments_SalesInvoiceId_PaymentDate";
        CREATE INDEX "IX_SalesInvoicePayments_SalesInvoiceId_PaymentDate"
            ON "SalesInvoicePayments" ("SalesInvoiceId", "PaymentDate");
    END IF;

    -- IX_SalesInvoicePayments_PaymentMethod_PaymentDate
    SELECT pg_get_indexdef('"IX_SalesInvoicePayments_PaymentMethod_PaymentDate"'::regclass) INTO v_current_def;
    IF v_current_def IS NULL OR v_current_def NOT LIKE '%"PaymentMethod", "PaymentDate"%' THEN
        DROP INDEX IF EXISTS "IX_SalesInvoicePayments_PaymentMethod_PaymentDate";
        CREATE INDEX "IX_SalesInvoicePayments_PaymentMethod_PaymentDate"
            ON "SalesInvoicePayments" ("PaymentMethod", "PaymentDate");
    END IF;

    -- Inventory dashboard
    
    -- IX_WarehouseStocks_TenantId_Quantity
    SELECT pg_get_indexdef('"IX_WarehouseStocks_TenantId_Quantity"'::regclass) INTO v_current_def;
    IF v_current_def IS NULL OR v_current_def NOT LIKE '%"TenantId", "Quantity"%' THEN
        DROP INDEX IF EXISTS "IX_WarehouseStocks_TenantId_Quantity";
        CREATE INDEX "IX_WarehouseStocks_TenantId_Quantity"
            ON "WarehouseStocks" ("TenantId", "Quantity");
    END IF;

    -- IX_WarehouseStocks_TenantId_WarehouseId_Quantity
    SELECT pg_get_indexdef('"IX_WarehouseStocks_TenantId_WarehouseId_Quantity"'::regclass) INTO v_current_def;
    IF v_current_def IS NULL OR v_current_def NOT LIKE '%"TenantId", "WarehouseId", "Quantity"%' THEN
        DROP INDEX IF EXISTS "IX_WarehouseStocks_TenantId_WarehouseId_Quantity";
        CREATE INDEX "IX_WarehouseStocks_TenantId_WarehouseId_Quantity"
            ON "WarehouseStocks" ("TenantId", "WarehouseId", "Quantity");
    END IF;

    -- Storefront analytics dashboard
    
    -- IX_Orders_TenantId_Source_CreatedAt
    SELECT pg_get_indexdef('"IX_Orders_TenantId_Source_CreatedAt"'::regclass) INTO v_current_def;
    IF v_current_def IS NULL OR v_current_def NOT LIKE '%"TenantId", "Source", "CreatedAt"%' THEN
        DROP INDEX IF EXISTS "IX_Orders_TenantId_Source_CreatedAt";
        CREATE INDEX "IX_Orders_TenantId_Source_CreatedAt"
            ON "Orders" ("TenantId", "Source", "CreatedAt");
    END IF;

    -- IX_Orders_TenantId_Status_CreatedAt
    SELECT pg_get_indexdef('"IX_Orders_TenantId_Status_CreatedAt"'::regclass) INTO v_current_def;
    IF v_current_def IS NULL OR v_current_def NOT LIKE '%"TenantId", "Status", "CreatedAt"%' THEN
        DROP INDEX IF EXISTS "IX_Orders_TenantId_Status_CreatedAt";
        CREATE INDEX "IX_Orders_TenantId_Status_CreatedAt"
            ON "Orders" ("TenantId", "Status", "CreatedAt");
    END IF;

    -- IX_Orders_TenantId_PaymentStatus_CreatedAt
    SELECT pg_get_indexdef('"IX_Orders_TenantId_PaymentStatus_CreatedAt"'::regclass) INTO v_current_def;
    IF v_current_def IS NULL OR v_current_def NOT LIKE '%"TenantId", "PaymentStatus", "CreatedAt"%' THEN
        DROP INDEX IF EXISTS "IX_Orders_TenantId_PaymentStatus_CreatedAt";
        CREATE INDEX "IX_Orders_TenantId_PaymentStatus_CreatedAt"
            ON "Orders" ("TenantId", "PaymentStatus", "CreatedAt");
    END IF;

    -- IX_CheckoutSessions_TenantId_Status_AbandonedAtUtc
    SELECT pg_get_indexdef('"IX_CheckoutSessions_TenantId_Status_AbandonedAtUtc"'::regclass) INTO v_current_def;
    IF v_current_def IS NULL OR v_current_def NOT LIKE '%"TenantId", "Status", "AbandonedAtUtc"%' THEN
        DROP INDEX IF EXISTS "IX_CheckoutSessions_TenantId_Status_AbandonedAtUtc";
        CREATE INDEX "IX_CheckoutSessions_TenantId_Status_AbandonedAtUtc"
            ON "CheckoutSessions" ("TenantId", "Status", "AbandonedAtUtc");
    END IF;

END $$;
