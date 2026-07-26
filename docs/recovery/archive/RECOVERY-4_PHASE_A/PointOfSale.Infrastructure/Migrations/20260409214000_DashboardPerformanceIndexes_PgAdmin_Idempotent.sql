-- Dashboard / reporting performance indexes (IDEMPOTENT VERSION)
-- PgAdmin4-ready script
-- Notes:
-- 1. This script is intentionally scoped to performance indexes only.
-- 2. It does NOT include unrelated EF model drift.
-- 3. SalesInvoices partial index excludes Cancelled rows where Status = 2.
-- 4. Uses regular (non-concurrent) index creation to allow running in transaction blocks.
-- 5. Each index is dropped then created to ensure idempotency.
-- 6. This script can be run multiple times safely.
-- 7. WARNING: Regular index creation will lock tables during creation.
-- 8. For production, consider running each CREATE INDEX CONCURRENTLY statement separately outside a transaction.

-- Customers indexes
DROP INDEX IF EXISTS "IX_Customers_TenantId";
CREATE INDEX "IX_Customers_TenantId" ON "Customers" ("TenantId");

-- Suppliers indexes
DROP INDEX IF EXISTS "IX_Suppliers_TenantId";
CREATE INDEX "IX_Suppliers_TenantId" ON "Suppliers" ("TenantId");

-- Products indexes
DROP INDEX IF EXISTS "IX_Products_TenantId";
CREATE INDEX "IX_Products_TenantId" ON "Products" ("TenantId");

DROP INDEX IF EXISTS "IX_Products_TenantId_CategoryId";
CREATE INDEX "IX_Products_TenantId_CategoryId" ON "Products" ("TenantId", "CategoryId");

-- SalesInvoices indexes
DROP INDEX IF EXISTS "IX_SalesInvoices_TenantId_InvoiceDate_Active";
CREATE INDEX "IX_SalesInvoices_TenantId_InvoiceDate_Active" ON "SalesInvoices" ("TenantId", "InvoiceDate") WHERE "IsDeleted" = FALSE AND "Status" <> 2;

DROP INDEX IF EXISTS "IX_SalesInvoices_TenantId_Status_InvoiceDate";
CREATE INDEX "IX_SalesInvoices_TenantId_Status_InvoiceDate" ON "SalesInvoices" ("TenantId", "Status", "InvoiceDate");

DROP INDEX IF EXISTS "IX_SalesInvoices_TenantId_CustomerId_InvoiceDate";
CREATE INDEX "IX_SalesInvoices_TenantId_CustomerId_InvoiceDate" ON "SalesInvoices" ("TenantId", "CustomerId", "InvoiceDate");

DROP INDEX IF EXISTS "IX_SalesInvoices_TenantId_WarehouseId_InvoiceDate";
CREATE INDEX "IX_SalesInvoices_TenantId_WarehouseId_InvoiceDate" ON "SalesInvoices" ("TenantId", "WarehouseId", "InvoiceDate");

DROP INDEX IF EXISTS "IX_SalesInvoices_TenantId_CreatedAt";
CREATE INDEX "IX_SalesInvoices_TenantId_CreatedAt" ON "SalesInvoices" ("TenantId", "CreatedAt");

-- SalesInvoiceItems indexes
DROP INDEX IF EXISTS "IX_SalesInvoiceItems_SalesInvoiceId_ProductId";
CREATE INDEX "IX_SalesInvoiceItems_SalesInvoiceId_ProductId" ON "SalesInvoiceItems" ("SalesInvoiceId", "ProductId");

DROP INDEX IF EXISTS "IX_SalesInvoiceItems_SalesInvoiceId_ProductVariantId";
CREATE INDEX "IX_SalesInvoiceItems_SalesInvoiceId_ProductVariantId" ON "SalesInvoiceItems" ("SalesInvoiceId", "ProductVariantId");

-- SalesInvoicePayments indexes
DROP INDEX IF EXISTS "IX_SalesInvoicePayments_SalesInvoiceId_PaymentDate";
CREATE INDEX "IX_SalesInvoicePayments_SalesInvoiceId_PaymentDate" ON "SalesInvoicePayments" ("SalesInvoiceId", "PaymentDate");

DROP INDEX IF EXISTS "IX_SalesInvoicePayments_PaymentMethod_PaymentDate";
CREATE INDEX "IX_SalesInvoicePayments_PaymentMethod_PaymentDate" ON "SalesInvoicePayments" ("PaymentMethod", "PaymentDate");

-- WarehouseStocks indexes
DROP INDEX IF EXISTS "IX_WarehouseStocks_TenantId_Quantity";
CREATE INDEX "IX_WarehouseStocks_TenantId_Quantity" ON "WarehouseStocks" ("TenantId", "Quantity");

DROP INDEX IF EXISTS "IX_WarehouseStocks_TenantId_WarehouseId_Quantity";
CREATE INDEX "IX_WarehouseStocks_TenantId_WarehouseId_Quantity" ON "WarehouseStocks" ("TenantId", "WarehouseId", "Quantity");

-- Orders indexes
DROP INDEX IF EXISTS "IX_Orders_TenantId_Source_CreatedAt";
CREATE INDEX "IX_Orders_TenantId_Source_CreatedAt" ON "Orders" ("TenantId", "Source", "CreatedAt");

DROP INDEX IF EXISTS "IX_Orders_TenantId_Status_CreatedAt";
CREATE INDEX "IX_Orders_TenantId_Status_CreatedAt" ON "Orders" ("TenantId", "Status", "CreatedAt");

DROP INDEX IF EXISTS "IX_Orders_TenantId_PaymentStatus_CreatedAt";
CREATE INDEX "IX_Orders_TenantId_PaymentStatus_CreatedAt" ON "Orders" ("TenantId", "PaymentStatus", "CreatedAt");

-- CheckoutSessions indexes
DROP INDEX IF EXISTS "IX_CheckoutSessions_TenantId_Status_AbandonedAtUtc";
CREATE INDEX "IX_CheckoutSessions_TenantId_Status_AbandonedAtUtc" ON "CheckoutSessions" ("TenantId", "Status", "AbandonedAtUtc");
