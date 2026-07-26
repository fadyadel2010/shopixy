CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE TABLE "Categories" (
        "Id" uuid NOT NULL,
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE TABLE "Customers" (
        "Id" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Phone" character varying(50),
        "Email" character varying(200),
        "Address" text,
        "TaxNumber" character varying(50),
        "Balance" numeric(18,2) NOT NULL,
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE TABLE "Suppliers" (
        "Id" uuid NOT NULL,
        "Name" text NOT NULL,
        "Phone" text,
        "Email" text,
        "Address" text,
        "TaxNumber" text,
        "Balance" numeric NOT NULL,
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE TABLE "SystemSettings" (
        "Id" uuid NOT NULL,
        "Key" text NOT NULL,
        "Value" text NOT NULL,
        "Description" text,
        "Category" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_SystemSettings" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE TABLE "Warehouses" (
        "Id" uuid NOT NULL,
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE TABLE "Products" (
        "Id" uuid NOT NULL,
        "Name" character varying(300) NOT NULL,
        "Barcode" character varying(100),
        "SKU" character varying(100),
        "Description" text,
        "CategoryId" uuid NOT NULL,
        "PurchasePrice" numeric(18,2) NOT NULL,
        "SalePrice" numeric(18,2) NOT NULL,
        "PriceIncludesTax" boolean NOT NULL,
        "TaxRate" numeric(5,2) NOT NULL,
        "Unit" character varying(50) NOT NULL,
        "MinimumStockLevel" numeric(18,2) NOT NULL,
        "Images" text,
        "IsActive" boolean NOT NULL,
        "TrackStock" boolean NOT NULL,
        "Notes" text,
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE TABLE "CustomerTransactions" (
        "Id" uuid NOT NULL,
        "CustomerId" uuid NOT NULL,
        "TransactionDate" timestamp with time zone NOT NULL,
        "TransactionType" integer NOT NULL,
        "DebitAmount" numeric NOT NULL,
        "CreditAmount" numeric NOT NULL,
        "BalanceAfter" numeric NOT NULL,
        "ReferenceId" uuid,
        "ReferenceNumber" text,
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE TABLE "SupplierTransactions" (
        "Id" uuid NOT NULL,
        "SupplierId" uuid NOT NULL,
        "TransactionDate" timestamp with time zone NOT NULL,
        "TransactionType" integer NOT NULL,
        "DebitAmount" numeric NOT NULL,
        "CreditAmount" numeric NOT NULL,
        "BalanceAfter" numeric NOT NULL,
        "ReferenceId" uuid,
        "ReferenceNumber" text,
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE TABLE "PurchaseInvoices" (
        "Id" uuid NOT NULL,
        "InvoiceNumber" text NOT NULL,
        "InvoiceDate" timestamp with time zone NOT NULL,
        "SupplierId" uuid NOT NULL,
        "WarehouseId" uuid NOT NULL,
        "Status" integer NOT NULL,
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
        CONSTRAINT "FK_PurchaseInvoices_Suppliers_SupplierId" FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_PurchaseInvoices_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE TABLE "Returns" (
        "Id" uuid NOT NULL,
        "ReturnNumber" text NOT NULL,
        "ReturnDate" timestamp with time zone NOT NULL,
        "ReturnType" integer NOT NULL,
        "OriginalInvoiceId" uuid,
        "OriginalInvoiceNumber" text,
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
        CONSTRAINT "FK_Returns_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id"),
        CONSTRAINT "FK_Returns_Suppliers_SupplierId" FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("Id"),
        CONSTRAINT "FK_Returns_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE TABLE "SalesInvoices" (
        "Id" uuid NOT NULL,
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
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_SalesInvoices" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SalesInvoices_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id"),
        CONSTRAINT "FK_SalesInvoices_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE TABLE "ProductVariants" (
        "Id" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "Name" text NOT NULL,
        "SKU" text,
        "Barcode" text,
        "AdditionalPrice" numeric NOT NULL,
        "IsActive" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_ProductVariants" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_ProductVariants_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE TABLE "PurchaseInvoiceItems" (
        "Id" uuid NOT NULL,
        "PurchaseInvoiceId" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "ProductVariantId" uuid,
        "Quantity" numeric NOT NULL,
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
        CONSTRAINT "FK_PurchaseInvoiceItems_ProductVariants_ProductVariantId" FOREIGN KEY ("ProductVariantId") REFERENCES "ProductVariants" ("Id"),
        CONSTRAINT "FK_PurchaseInvoiceItems_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_PurchaseInvoiceItems_PurchaseInvoices_PurchaseInvoiceId" FOREIGN KEY ("PurchaseInvoiceId") REFERENCES "PurchaseInvoices" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE TABLE "ReturnItems" (
        "Id" uuid NOT NULL,
        "ReturnId" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "ProductVariantId" uuid,
        "Quantity" numeric NOT NULL,
        "UnitPrice" numeric NOT NULL,
        "TaxRate" numeric NOT NULL,
        "TaxAmount" numeric NOT NULL,
        "TotalAmount" numeric NOT NULL,
        "Reason" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_ReturnItems" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_ReturnItems_ProductVariants_ProductVariantId" FOREIGN KEY ("ProductVariantId") REFERENCES "ProductVariants" ("Id"),
        CONSTRAINT "FK_ReturnItems_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_ReturnItems_Returns_ReturnId" FOREIGN KEY ("ReturnId") REFERENCES "Returns" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE TABLE "SalesInvoiceItems" (
        "Id" uuid NOT NULL,
        "SalesInvoiceId" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "ProductVariantId" uuid,
        "Quantity" numeric NOT NULL,
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
        CONSTRAINT "PK_SalesInvoiceItems" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SalesInvoiceItems_ProductVariants_ProductVariantId" FOREIGN KEY ("ProductVariantId") REFERENCES "ProductVariants" ("Id"),
        CONSTRAINT "FK_SalesInvoiceItems_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_SalesInvoiceItems_SalesInvoices_SalesInvoiceId" FOREIGN KEY ("SalesInvoiceId") REFERENCES "SalesInvoices" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE TABLE "StockMovements" (
        "Id" uuid NOT NULL,
        "WarehouseId" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "ProductVariantId" uuid,
        "MovementType" integer NOT NULL,
        "QuantityChange" numeric(18,2) NOT NULL,
        "QuantityAfter" numeric(18,2) NOT NULL,
        "ReferenceId" uuid,
        "ReferenceNumber" text,
        "Notes" text,
        "ToWarehouseId" uuid,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_StockMovements" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_StockMovements_ProductVariants_ProductVariantId" FOREIGN KEY ("ProductVariantId") REFERENCES "ProductVariants" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_StockMovements_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_StockMovements_Warehouses_ToWarehouseId" FOREIGN KEY ("ToWarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_StockMovements_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE TABLE "WarehouseStocks" (
        "Id" uuid NOT NULL,
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_Categories_ParentCategoryId" ON "Categories" ("ParentCategoryId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_Customers_Email" ON "Customers" ("Email");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_Customers_Phone" ON "Customers" ("Phone");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_Customers_TaxNumber" ON "Customers" ("TaxNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_CustomerTransactions_CustomerId" ON "CustomerTransactions" ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_Products_Barcode" ON "Products" ("Barcode");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_Products_CategoryId" ON "Products" ("CategoryId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_Products_Name" ON "Products" ("Name");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_Products_SKU" ON "Products" ("SKU");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_ProductVariants_ProductId" ON "ProductVariants" ("ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_PurchaseInvoiceItems_ProductId" ON "PurchaseInvoiceItems" ("ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_PurchaseInvoiceItems_ProductVariantId" ON "PurchaseInvoiceItems" ("ProductVariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_PurchaseInvoiceItems_PurchaseInvoiceId" ON "PurchaseInvoiceItems" ("PurchaseInvoiceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_PurchaseInvoicePayments_PurchaseInvoiceId" ON "PurchaseInvoicePayments" ("PurchaseInvoiceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_PurchaseInvoices_SupplierId" ON "PurchaseInvoices" ("SupplierId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_PurchaseInvoices_WarehouseId" ON "PurchaseInvoices" ("WarehouseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_ReturnItems_ProductId" ON "ReturnItems" ("ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_ReturnItems_ProductVariantId" ON "ReturnItems" ("ProductVariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_ReturnItems_ReturnId" ON "ReturnItems" ("ReturnId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_Returns_CustomerId" ON "Returns" ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_Returns_SupplierId" ON "Returns" ("SupplierId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_Returns_WarehouseId" ON "Returns" ("WarehouseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_SalesInvoiceItems_ProductId" ON "SalesInvoiceItems" ("ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_SalesInvoiceItems_ProductVariantId" ON "SalesInvoiceItems" ("ProductVariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_SalesInvoiceItems_SalesInvoiceId" ON "SalesInvoiceItems" ("SalesInvoiceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_SalesInvoicePayments_SalesInvoiceId" ON "SalesInvoicePayments" ("SalesInvoiceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_SalesInvoices_CustomerId" ON "SalesInvoices" ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_SalesInvoices_WarehouseId" ON "SalesInvoices" ("WarehouseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_StockMovements_CreatedAt" ON "StockMovements" ("CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_StockMovements_ProductId" ON "StockMovements" ("ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_StockMovements_ProductVariantId" ON "StockMovements" ("ProductVariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_StockMovements_ToWarehouseId" ON "StockMovements" ("ToWarehouseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_StockMovements_WarehouseId" ON "StockMovements" ("WarehouseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_SupplierTransactions_SupplierId" ON "SupplierTransactions" ("SupplierId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_Users_Email" ON "Users" ("Email");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_Users_Username" ON "Users" ("Username");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_WarehouseStocks_ProductId" ON "WarehouseStocks" ("ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE INDEX "IX_WarehouseStocks_ProductVariantId" ON "WarehouseStocks" ("ProductVariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_WarehouseStocks_WarehouseId_ProductId_ProductVariantId" ON "WarehouseStocks" ("WarehouseId", "ProductId", "ProductVariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251123190510_InitialCreate') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251123190510_InitialCreate', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126172001_AddCashboxEntities') THEN
    CREATE TABLE "Cashboxes" (
        "Id" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "WarehouseId" uuid NOT NULL,
        "CurrentBalance" numeric(18,2) NOT NULL,
        "IsActive" boolean NOT NULL,
        "OpenedAt" timestamp with time zone,
        "OpenedByUserId" uuid,
        "OpeningBalance" numeric(18,2) NOT NULL,
        "ExpectedBalance" numeric(18,2) NOT NULL,
        "Description" character varying(500),
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126172001_AddCashboxEntities') THEN
    CREATE TABLE "CashboxTransactions" (
        "Id" uuid NOT NULL,
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126172001_AddCashboxEntities') THEN
    CREATE INDEX "IX_Cashboxes_IsActive" ON "Cashboxes" ("IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126172001_AddCashboxEntities') THEN
    CREATE INDEX "IX_Cashboxes_OpenedByUserId" ON "Cashboxes" ("OpenedByUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126172001_AddCashboxEntities') THEN
    CREATE INDEX "IX_Cashboxes_WarehouseId" ON "Cashboxes" ("WarehouseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126172001_AddCashboxEntities') THEN
    CREATE INDEX "IX_Cashboxes_WarehouseId_IsActive" ON "Cashboxes" ("WarehouseId", "IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126172001_AddCashboxEntities') THEN
    CREATE INDEX "IX_CashboxTransactions_CashboxId" ON "CashboxTransactions" ("CashboxId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126172001_AddCashboxEntities') THEN
    CREATE INDEX "IX_CashboxTransactions_CashboxId_TransactionDate" ON "CashboxTransactions" ("CashboxId", "TransactionDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126172001_AddCashboxEntities') THEN
    CREATE INDEX "IX_CashboxTransactions_ReferenceId" ON "CashboxTransactions" ("ReferenceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126172001_AddCashboxEntities') THEN
    CREATE INDEX "IX_CashboxTransactions_TransactionDate" ON "CashboxTransactions" ("TransactionDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126172001_AddCashboxEntities') THEN
    CREATE INDEX "IX_CashboxTransactions_TransactionType" ON "CashboxTransactions" ("TransactionType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126172001_AddCashboxEntities') THEN
    CREATE INDEX "IX_CashboxTransactions_UserId" ON "CashboxTransactions" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126172001_AddCashboxEntities') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251126172001_AddCashboxEntities', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126175326_MakeCashboxWarehouseOptional') THEN
    ALTER TABLE "Cashboxes" ALTER COLUMN "WarehouseId" DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126175326_MakeCashboxWarehouseOptional') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251126175326_MakeCashboxWarehouseOptional', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126213231_AddAccountLedgerAndSOA') THEN
    CREATE TABLE "AccountLedgers" (
        "Id" uuid NOT NULL,
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126213231_AddAccountLedgerAndSOA') THEN
    CREATE INDEX "IX_AccountLedgers_UserId" ON "AccountLedgers" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126213231_AddAccountLedgerAndSOA') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251126213231_AddAccountLedgerAndSOA', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126221658_AddStockTransfer') THEN
    CREATE TABLE "StockTransfers" (
        "Id" uuid NOT NULL,
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126221658_AddStockTransfer') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126221658_AddStockTransfer') THEN
    CREATE INDEX "IX_StockTransferItems_ProductId" ON "StockTransferItems" ("ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126221658_AddStockTransfer') THEN
    CREATE INDEX "IX_StockTransferItems_ProductVariantId" ON "StockTransferItems" ("ProductVariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126221658_AddStockTransfer') THEN
    CREATE INDEX "IX_StockTransferItems_StockTransferId" ON "StockTransferItems" ("StockTransferId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126221658_AddStockTransfer') THEN
    CREATE INDEX "IX_StockTransfers_ApprovedById" ON "StockTransfers" ("ApprovedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126221658_AddStockTransfer') THEN
    CREATE INDEX "IX_StockTransfers_CompletedById" ON "StockTransfers" ("CompletedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126221658_AddStockTransfer') THEN
    CREATE INDEX "IX_StockTransfers_FromWarehouseId" ON "StockTransfers" ("FromWarehouseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126221658_AddStockTransfer') THEN
    CREATE INDEX "IX_StockTransfers_RequestedById" ON "StockTransfers" ("RequestedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126221658_AddStockTransfer') THEN
    CREATE INDEX "IX_StockTransfers_ToWarehouseId" ON "StockTransfers" ("ToWarehouseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251126221658_AddStockTransfer') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251126221658_AddStockTransfer', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251127185031_AddSalesRepresentatives') THEN
    ALTER TABLE "SalesInvoices" ADD "SalesRepresentativeId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251127185031_AddSalesRepresentatives') THEN
    CREATE TABLE "SalesRepresentatives" (
        "Id" uuid NOT NULL,
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251127185031_AddSalesRepresentatives') THEN
    CREATE INDEX "IX_SalesInvoices_SalesRepresentativeId" ON "SalesInvoices" ("SalesRepresentativeId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251127185031_AddSalesRepresentatives') THEN
    ALTER TABLE "SalesInvoices" ADD CONSTRAINT "FK_SalesInvoices_SalesRepresentatives_SalesRepresentativeId" FOREIGN KEY ("SalesRepresentativeId") REFERENCES "SalesRepresentatives" ("Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251127185031_AddSalesRepresentatives') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251127185031_AddSalesRepresentatives', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251128175638_AddFinancialAccounts') THEN
    CREATE TABLE "FinancialAccounts" (
        "Id" uuid NOT NULL,
        "Name" text NOT NULL,
        "AccountNumber" text,
        "IsSystem" boolean NOT NULL,
        "Balance" numeric NOT NULL,
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251128175638_AddFinancialAccounts') THEN
    CREATE TABLE "FinancialTransactions" (
        "Id" uuid NOT NULL,
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251128175638_AddFinancialAccounts') THEN
    INSERT INTO "FinancialAccounts" ("Id", "AccountNumber", "Balance", "CreatedAt", "CreatedBy", "IsDeleted", "IsSystem", "Name", "ParentAccountId", "UpdatedAt", "UpdatedBy")
    VALUES ('11111111-1111-1111-1111-111111111111', NULL, 0.0, TIMESTAMPTZ '2024-01-01T00:00:00Z', NULL, FALSE, TRUE, 'Visa Account', NULL, NULL, NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251128175638_AddFinancialAccounts') THEN
    CREATE INDEX "IX_FinancialAccounts_ParentAccountId" ON "FinancialAccounts" ("ParentAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251128175638_AddFinancialAccounts') THEN
    CREATE INDEX "IX_FinancialTransactions_AccountId" ON "FinancialTransactions" ("AccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251128175638_AddFinancialAccounts') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251128175638_AddFinancialAccounts', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251201201129_AddCashboxOpeningBalanceFields') THEN
    ALTER TABLE "CashboxTransactions" ADD "IsOpeningBalance" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251201201129_AddCashboxOpeningBalanceFields') THEN
    ALTER TABLE "Cashboxes" ADD "OpeningBalanceAmount" numeric;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251201201129_AddCashboxOpeningBalanceFields') THEN
    ALTER TABLE "Cashboxes" ADD "OpeningBalanceDate" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251201201129_AddCashboxOpeningBalanceFields') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251201201129_AddCashboxOpeningBalanceFields', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251202144522_AddSupplierOpeningBalanceFields') THEN
    ALTER TABLE "SupplierTransactions" ADD "IsOpeningBalance" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251202144522_AddSupplierOpeningBalanceFields') THEN
    ALTER TABLE "Suppliers" ADD "BalanceType" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251202144522_AddSupplierOpeningBalanceFields') THEN
    ALTER TABLE "Suppliers" ADD "OpeningBalanceAmount" numeric;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251202144522_AddSupplierOpeningBalanceFields') THEN
    ALTER TABLE "Suppliers" ADD "OpeningBalanceDate" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251202144522_AddSupplierOpeningBalanceFields') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251202144522_AddSupplierOpeningBalanceFields', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251202152548_AddOpeningBalanceFieldsToStockMovement') THEN
    ALTER TABLE "StockMovements" ADD "IsOpeningBalance" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251202152548_AddOpeningBalanceFieldsToStockMovement') THEN
    ALTER TABLE "StockMovements" ADD "Price" numeric;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251202152548_AddOpeningBalanceFieldsToStockMovement') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251202152548_AddOpeningBalanceFieldsToStockMovement', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251202190721_AddBankTransactions') THEN
    CREATE TABLE "BankTransactions" (
        "Id" uuid NOT NULL,
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251202190721_AddBankTransactions') THEN
    CREATE INDEX "IX_BankTransactions_BankId" ON "BankTransactions" ("BankId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251202190721_AddBankTransactions') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251202190721_AddBankTransactions', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251202203358_AddExpensesManagement') THEN
    CREATE TABLE "ExpenseCategories" (
        "Id" uuid NOT NULL,
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251202203358_AddExpensesManagement') THEN
    CREATE TABLE "Expenses" (
        "Id" uuid NOT NULL,
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251202203358_AddExpensesManagement') THEN
    CREATE INDEX "IX_Expenses_BankId" ON "Expenses" ("BankId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251202203358_AddExpensesManagement') THEN
    CREATE INDEX "IX_Expenses_CashboxId" ON "Expenses" ("CashboxId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251202203358_AddExpensesManagement') THEN
    CREATE INDEX "IX_Expenses_ExpenseCategoryId" ON "Expenses" ("ExpenseCategoryId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251202203358_AddExpensesManagement') THEN
    CREATE INDEX "IX_Expenses_SupplierId" ON "Expenses" ("SupplierId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251202203358_AddExpensesManagement') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251202203358_AddExpensesManagement', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251204192040_AddVisaTransactions') THEN
    CREATE TABLE "VisaTransactions" (
        "Id" uuid NOT NULL,
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251204192040_AddVisaTransactions') THEN
    CREATE INDEX "IX_VisaTransactions_CreatedByUserId" ON "VisaTransactions" ("CreatedByUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251204192040_AddVisaTransactions') THEN
    CREATE INDEX "IX_VisaTransactions_TransactionDate" ON "VisaTransactions" ("TransactionDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251204192040_AddVisaTransactions') THEN
    CREATE INDEX "IX_VisaTransactions_TransactionType" ON "VisaTransactions" ("TransactionType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251204192040_AddVisaTransactions') THEN
    CREATE INDEX "IX_VisaTransactions_SourceEntityId" ON "VisaTransactions" ("SourceEntityId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251204192040_AddVisaTransactions') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251204192040_AddVisaTransactions', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251205125248_AddMultiUnitSupport') THEN
    ALTER TABLE "StockMovements" ADD "ConversionFactor" numeric NOT NULL DEFAULT 1.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251205125248_AddMultiUnitSupport') THEN
    ALTER TABLE "StockMovements" ADD "UnitId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251205125248_AddMultiUnitSupport') THEN
    ALTER TABLE "SalesInvoiceItems" ADD "BaseQuantity" numeric NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251205125248_AddMultiUnitSupport') THEN
    ALTER TABLE "SalesInvoiceItems" ADD "ConversionFactor" numeric NOT NULL DEFAULT 1.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251205125248_AddMultiUnitSupport') THEN
    ALTER TABLE "SalesInvoiceItems" ADD "UnitId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251205125248_AddMultiUnitSupport') THEN
    ALTER TABLE "ReturnItems" ADD "BaseQuantity" numeric NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251205125248_AddMultiUnitSupport') THEN
    ALTER TABLE "ReturnItems" ADD "ConversionFactor" numeric NOT NULL DEFAULT 1.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251205125248_AddMultiUnitSupport') THEN
    ALTER TABLE "ReturnItems" ADD "UnitId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251205125248_AddMultiUnitSupport') THEN
    ALTER TABLE "PurchaseInvoiceItems" ADD "BaseQuantity" numeric NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251205125248_AddMultiUnitSupport') THEN
    ALTER TABLE "PurchaseInvoiceItems" ADD "ConversionFactor" numeric NOT NULL DEFAULT 1.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251205125248_AddMultiUnitSupport') THEN
    ALTER TABLE "PurchaseInvoiceItems" ADD "UnitId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251205125248_AddMultiUnitSupport') THEN
    CREATE TABLE "Units" (
        "Id" uuid NOT NULL,
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251205125248_AddMultiUnitSupport') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251205125248_AddMultiUnitSupport') THEN
    CREATE INDEX "IX_StockMovements_UnitId" ON "StockMovements" ("UnitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251205125248_AddMultiUnitSupport') THEN
    CREATE INDEX "IX_SalesInvoiceItems_UnitId" ON "SalesInvoiceItems" ("UnitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251205125248_AddMultiUnitSupport') THEN
    CREATE INDEX "IX_ReturnItems_UnitId" ON "ReturnItems" ("UnitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251205125248_AddMultiUnitSupport') THEN
    CREATE INDEX "IX_PurchaseInvoiceItems_UnitId" ON "PurchaseInvoiceItems" ("UnitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251205125248_AddMultiUnitSupport') THEN
    CREATE INDEX "IX_ProductUnits_ProductId" ON "ProductUnits" ("ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251205125248_AddMultiUnitSupport') THEN
    CREATE INDEX "IX_ProductUnits_UnitId" ON "ProductUnits" ("UnitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251205125248_AddMultiUnitSupport') THEN
    ALTER TABLE "PurchaseInvoiceItems" ADD CONSTRAINT "FK_PurchaseInvoiceItems_Units_UnitId" FOREIGN KEY ("UnitId") REFERENCES "Units" ("Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251205125248_AddMultiUnitSupport') THEN
    ALTER TABLE "ReturnItems" ADD CONSTRAINT "FK_ReturnItems_Units_UnitId" FOREIGN KEY ("UnitId") REFERENCES "Units" ("Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251205125248_AddMultiUnitSupport') THEN
    ALTER TABLE "SalesInvoiceItems" ADD CONSTRAINT "FK_SalesInvoiceItems_Units_UnitId" FOREIGN KEY ("UnitId") REFERENCES "Units" ("Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251205125248_AddMultiUnitSupport') THEN
    ALTER TABLE "StockMovements" ADD CONSTRAINT "FK_StockMovements_Units_UnitId" FOREIGN KEY ("UnitId") REFERENCES "Units" ("Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251205125248_AddMultiUnitSupport') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251205125248_AddMultiUnitSupport', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251205180157_AddDueDateToPurchaseInvoices') THEN
    ALTER TABLE "PurchaseInvoices" ADD "DueDate" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251205180157_AddDueDateToPurchaseInvoices') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251205180157_AddDueDateToPurchaseInvoices', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251209172937_UpdateVisaAccountName') THEN
    UPDATE "FinancialAccounts" SET "AccountNumber" = 'VISA-001', "Name" = 'حساب الفيزا'
    WHERE "Id" = '11111111-1111-1111-1111-111111111111';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251209172937_UpdateVisaAccountName') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251209172937_UpdateVisaAccountName', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251209173809_FixVisaAccountIsDeleted') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251209173809_FixVisaAccountIsDeleted', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251211181712_AddAuditLogs') THEN
    CREATE TABLE "AuditLogs" (
        "LogId" bigint GENERATED BY DEFAULT AS IDENTITY,
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251211181712_AddAuditLogs') THEN
    UPDATE "FinancialAccounts" SET "Name" = 'Visa Account'
    WHERE "Id" = '11111111-1111-1111-1111-111111111111';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251211181712_AddAuditLogs') THEN
    CREATE INDEX "IX_AuditLogs_ActionType" ON "AuditLogs" ("ActionType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251211181712_AddAuditLogs') THEN
    CREATE INDEX "IX_AuditLogs_EntityType" ON "AuditLogs" ("EntityType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251211181712_AddAuditLogs') THEN
    CREATE INDEX "IX_AuditLogs_TimestampUtc" ON "AuditLogs" ("TimestampUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251211181712_AddAuditLogs') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251211181712_AddAuditLogs', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251214184156_AddTrialBalanceColumnsToFinancialAccount') THEN
    ALTER TABLE "FinancialAccounts" ADD "AccountCode" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251214184156_AddTrialBalanceColumnsToFinancialAccount') THEN
    ALTER TABLE "FinancialAccounts" ADD "AccountType" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251214184156_AddTrialBalanceColumnsToFinancialAccount') THEN
    ALTER TABLE "FinancialAccounts" ADD "Level" integer NOT NULL DEFAULT 1;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251214184156_AddTrialBalanceColumnsToFinancialAccount') THEN

                    UPDATE "FinancialAccounts" 
                    SET "AccountCode" = '1200', "AccountType" = 1, "Level" = 1 
                    WHERE "Id" = '11111111-1111-1111-1111-111111111111'
                
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251214184156_AddTrialBalanceColumnsToFinancialAccount') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251214184156_AddTrialBalanceColumnsToFinancialAccount', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    DROP INDEX "IX_Products_SKU";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    ALTER TABLE "Products" ADD "AllowBackorder" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    ALTER TABLE "Products" ADD "AmazonASIN" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    ALTER TABLE "Products" ADD "Brand" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    ALTER TABLE "Products" ADD "DescriptionAr" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    ALTER TABLE "Products" ADD "DimensionUnit" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    ALTER TABLE "Products" ADD "Height" numeric NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    ALTER TABLE "Products" ADD "IsFeatured" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    ALTER TABLE "Products" ADD "IsOnlineVisible" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    ALTER TABLE "Products" ADD "IsSearchable" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    ALTER TABLE "Products" ADD "LastSyncedAt" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    ALTER TABLE "Products" ADD "Length" numeric NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    ALTER TABLE "Products" ADD "LowStockThreshold" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    ALTER TABLE "Products" ADD "Manufacturer" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    ALTER TABLE "Products" ADD "MetaDescription" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    ALTER TABLE "Products" ADD "MetaKeywords" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    ALTER TABLE "Products" ADD "MetaTitle" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    ALTER TABLE "Products" ADD "NameAr" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    ALTER TABLE "Products" ADD "PublishStatus" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    ALTER TABLE "Products" ADD "RelatedProductIds" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    ALTER TABLE "Products" ADD "Slug" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    ALTER TABLE "Products" ADD "Tags" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    ALTER TABLE "Products" ADD "VideoUrl" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    ALTER TABLE "Products" ADD "Weight" numeric NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    ALTER TABLE "Products" ADD "WeightUnit" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    ALTER TABLE "Products" ADD "Width" numeric NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    CREATE TABLE "Coupons" (
        "Id" uuid NOT NULL,
        "Code" text NOT NULL,
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
        CONSTRAINT "PK_Coupons" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    CREATE TABLE "ShippingMethods" (
        "Id" uuid NOT NULL,
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    CREATE TABLE "Orders" (
        "Id" uuid NOT NULL,
        "OrderNumber" text NOT NULL,
        "CustomerId" uuid,
        "GuestEmail" text,
        "GuestPhone" text,
        "GuestName" text,
        "Status" integer NOT NULL,
        "PaymentStatus" integer NOT NULL,
        "ShippingAddress" text NOT NULL,
        "ShippingCity" text,
        "ShippingState" text,
        "ShippingZipCode" text,
        "ShippingCountry" text,
        "BillingAddress" text,
        "BillingCity" text,
        "BillingState" text,
        "BillingZipCode" text,
        "BillingCountry" text,
        "ShippingMethodId" uuid,
        "ShippingCost" numeric NOT NULL,
        "TrackingNumber" text,
        "Carrier" text,
        "Subtotal" numeric NOT NULL,
        "DiscountAmount" numeric NOT NULL,
        "TaxAmount" numeric NOT NULL,
        "Total" numeric NOT NULL,
        "Currency" text NOT NULL,
        "CouponCode" text,
        "CouponId" uuid,
        "PaymentMethod" integer NOT NULL,
        "PaymentReference" text,
        "PaymentGateway" text,
        "PaidAt" timestamp with time zone,
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
        CONSTRAINT "FK_Orders_Coupons_CouponId" FOREIGN KEY ("CouponId") REFERENCES "Coupons" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_Orders_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_Orders_ShippingMethods_ShippingMethodId" FOREIGN KEY ("ShippingMethodId") REFERENCES "ShippingMethods" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_Orders_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    CREATE TABLE "OrderItems" (
        "Id" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "UnitId" uuid,
        "WarehouseId" uuid,
        "SKU" text NOT NULL,
        "ProductName" text NOT NULL,
        "VariantName" text,
        "ImageUrl" text,
        "Quantity" integer NOT NULL,
        "UnitPrice" numeric NOT NULL,
        "DiscountAmount" numeric NOT NULL,
        "TaxAmount" numeric NOT NULL,
        "Total" numeric NOT NULL,
        "CostPrice" numeric NOT NULL,
        "Notes" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_OrderItems" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_OrderItems_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_OrderItems_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_OrderItems_Units_UnitId" FOREIGN KEY ("UnitId") REFERENCES "Units" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_OrderItems_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    INSERT INTO "ShippingMethods" ("Id", "AvailableCountries", "BaseRate", "CarrierName", "CreatedAt", "CreatedBy", "Description", "EstimatedDeliveryDaysMax", "EstimatedDeliveryDaysMin", "FreeShippingThreshold", "IsActive", "IsDeleted", "Name", "NameAr", "PerKgRate", "SortOrder", "TrackingUrlTemplate", "UpdatedAt", "UpdatedBy")
    VALUES ('11111111-2222-3333-4444-555555555551', NULL, 10.0, NULL, TIMESTAMPTZ '2024-01-01T00:00:00Z', NULL, 'Standard delivery within 5-7 business days', 7, 5, NULL, TRUE, FALSE, 'Standard Shipping', 'شحن عادي', 0.0, 1, NULL, NULL, NULL);
    INSERT INTO "ShippingMethods" ("Id", "AvailableCountries", "BaseRate", "CarrierName", "CreatedAt", "CreatedBy", "Description", "EstimatedDeliveryDaysMax", "EstimatedDeliveryDaysMin", "FreeShippingThreshold", "IsActive", "IsDeleted", "Name", "NameAr", "PerKgRate", "SortOrder", "TrackingUrlTemplate", "UpdatedAt", "UpdatedBy")
    VALUES ('11111111-2222-3333-4444-555555555552', NULL, 25.0, NULL, TIMESTAMPTZ '2024-01-01T00:00:00Z', NULL, 'Express delivery within 2-3 business days', 3, 2, NULL, TRUE, FALSE, 'Express Shipping', 'شحن سريع', 2.0, 2, NULL, NULL, NULL);
    INSERT INTO "ShippingMethods" ("Id", "AvailableCountries", "BaseRate", "CarrierName", "CreatedAt", "CreatedBy", "Description", "EstimatedDeliveryDaysMax", "EstimatedDeliveryDaysMin", "FreeShippingThreshold", "IsActive", "IsDeleted", "Name", "NameAr", "PerKgRate", "SortOrder", "TrackingUrlTemplate", "UpdatedAt", "UpdatedBy")
    VALUES ('11111111-2222-3333-4444-555555555553', NULL, 0.0, NULL, TIMESTAMPTZ '2024-01-01T00:00:00Z', NULL, 'Free shipping for orders over $100', 14, 7, 100.0, TRUE, FALSE, 'Free Shipping', 'شحن مجاني', 0.0, 3, NULL, NULL, NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    CREATE INDEX "IX_Products_IsOnlineVisible" ON "Products" ("IsOnlineVisible");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    CREATE INDEX "IX_Products_PublishStatus" ON "Products" ("PublishStatus");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    CREATE UNIQUE INDEX "IX_Products_SKU" ON "Products" ("SKU") WHERE "IsDeleted" = false AND "SKU" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    CREATE UNIQUE INDEX "IX_Products_Slug" ON "Products" ("Slug") WHERE "IsDeleted" = false AND "Slug" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    CREATE UNIQUE INDEX "IX_Coupons_Code" ON "Coupons" ("Code") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    CREATE INDEX "IX_Coupons_IsActive" ON "Coupons" ("IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    CREATE INDEX "IX_OrderItems_OrderId" ON "OrderItems" ("OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    CREATE INDEX "IX_OrderItems_ProductId" ON "OrderItems" ("ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    CREATE INDEX "IX_OrderItems_UnitId" ON "OrderItems" ("UnitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    CREATE INDEX "IX_OrderItems_WarehouseId" ON "OrderItems" ("WarehouseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    CREATE INDEX "IX_Orders_CouponId" ON "Orders" ("CouponId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    CREATE INDEX "IX_Orders_CreatedAt" ON "Orders" ("CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    CREATE INDEX "IX_Orders_CustomerId" ON "Orders" ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    CREATE UNIQUE INDEX "IX_Orders_OrderNumber" ON "Orders" ("OrderNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    CREATE INDEX "IX_Orders_PaymentStatus" ON "Orders" ("PaymentStatus");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    CREATE INDEX "IX_Orders_ShippingMethodId" ON "Orders" ("ShippingMethodId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    CREATE INDEX "IX_Orders_Source" ON "Orders" ("Source");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    CREATE INDEX "IX_Orders_SourceOrderId" ON "Orders" ("SourceOrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    CREATE INDEX "IX_Orders_Status" ON "Orders" ("Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    CREATE INDEX "IX_Orders_WarehouseId" ON "Orders" ("WarehouseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221141054_AddEcommerceEntities') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251221141054_AddEcommerceEntities', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221180334_AddMassTransitOutbox') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221180334_AddMassTransitOutbox') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221180334_AddMassTransitOutbox') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221180334_AddMassTransitOutbox') THEN
    CREATE INDEX "IX_InboxState_Delivered" ON "InboxState" ("Delivered");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221180334_AddMassTransitOutbox') THEN
    CREATE INDEX "IX_OutboxMessage_EnqueueTime" ON "OutboxMessage" ("EnqueueTime");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221180334_AddMassTransitOutbox') THEN
    CREATE INDEX "IX_OutboxMessage_ExpirationTime" ON "OutboxMessage" ("ExpirationTime");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221180334_AddMassTransitOutbox') THEN
    CREATE UNIQUE INDEX "IX_OutboxMessage_InboxMessageId_InboxConsumerId_SequenceNumber" ON "OutboxMessage" ("InboxMessageId", "InboxConsumerId", "SequenceNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221180334_AddMassTransitOutbox') THEN
    CREATE UNIQUE INDEX "IX_OutboxMessage_OutboxId_SequenceNumber" ON "OutboxMessage" ("OutboxId", "SequenceNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221180334_AddMassTransitOutbox') THEN
    CREATE INDEX "IX_OutboxState_Created" ON "OutboxState" ("Created");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221180334_AddMassTransitOutbox') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251221180334_AddMassTransitOutbox', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221200148_AddMultiTenancyFoundation') THEN
    ALTER TABLE "Warehouses" ADD "TenantId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221200148_AddMultiTenancyFoundation') THEN
    ALTER TABLE "Products" ADD "TenantId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221200148_AddMultiTenancyFoundation') THEN
    ALTER TABLE "Customers" ADD "TenantId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221200148_AddMultiTenancyFoundation') THEN
    ALTER TABLE "Categories" ADD "TenantId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221200148_AddMultiTenancyFoundation') THEN
    CREATE TABLE "Tenants" (
        "Id" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "IsActive" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "Slug" character varying(100),
        "ContactEmail" character varying(255),
        CONSTRAINT "PK_Tenants" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221200148_AddMultiTenancyFoundation') THEN
    CREATE INDEX "IX_Tenants_IsActive" ON "Tenants" ("IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221200148_AddMultiTenancyFoundation') THEN
    CREATE UNIQUE INDEX "IX_Tenants_Slug" ON "Tenants" ("Slug") WHERE "Slug" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251221200148_AddMultiTenancyFoundation') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251221200148_AddMultiTenancyFoundation', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251224163945_AddPaymentsAndShipments') THEN
    CREATE TABLE "PaymentIntents" (
        "Id" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Amount" numeric NOT NULL,
        "Currency" text NOT NULL,
        "PaymentMethod" integer NOT NULL,
        "Status" integer NOT NULL,
        "Provider" text NOT NULL,
        "ProviderIntentId" text,
        "ProviderPaymentId" text,
        "PaymentReference" text NOT NULL,
        "RedirectUrl" text,
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251224163945_AddPaymentsAndShipments') THEN
    CREATE TABLE "Shipments" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "ShippingMethodId" uuid NOT NULL,
        "ShippingMethodName" text NOT NULL,
        "CarrierName" text,
        "ShippingCost" numeric NOT NULL,
        "Currency" text NOT NULL,
        "Status" integer NOT NULL,
        "TrackingNumber" text,
        "TrackingUrl" text,
        "ShipmentNumber" text NOT NULL,
        "RecipientName" text NOT NULL,
        "ShippingAddress" text NOT NULL,
        "ShippingCity" text,
        "ShippingState" text,
        "ShippingZipCode" text,
        "ShippingCountry" text,
        "RecipientPhone" text,
        "WeightKg" numeric,
        "EstimatedDeliveryDate" timestamp with time zone,
        "ActualDeliveryDate" timestamp with time zone,
        "PackedAtUtc" timestamp with time zone,
        "ShippedAtUtc" timestamp with time zone,
        "DeliveredAtUtc" timestamp with time zone,
        "CancelledAtUtc" timestamp with time zone,
        "CancellationReason" text,
        "Notes" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_Shipments" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Shipments_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_Shipments_ShippingMethods_ShippingMethodId" FOREIGN KEY ("ShippingMethodId") REFERENCES "ShippingMethods" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251224163945_AddPaymentsAndShipments') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251224163945_AddPaymentsAndShipments') THEN
    CREATE INDEX "IX_PaymentIntents_OrderId" ON "PaymentIntents" ("OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251224163945_AddPaymentsAndShipments') THEN
    CREATE INDEX "IX_PaymentWebhookLogs_PaymentIntentId" ON "PaymentWebhookLogs" ("PaymentIntentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251224163945_AddPaymentsAndShipments') THEN
    CREATE INDEX "IX_Shipments_OrderId" ON "Shipments" ("OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251224163945_AddPaymentsAndShipments') THEN
    CREATE INDEX "IX_Shipments_ShippingMethodId" ON "Shipments" ("ShippingMethodId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251224163945_AddPaymentsAndShipments') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251224163945_AddPaymentsAndShipments', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251224210903_AddStoreConfigs') THEN
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
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_StoreConfigs" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251224210903_AddStoreConfigs') THEN
    CREATE UNIQUE INDEX "IX_StoreConfigs_TenantId" ON "StoreConfigs" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251224210903_AddStoreConfigs') THEN
    CREATE INDEX "IX_StoreConfigs_ThemeId" ON "StoreConfigs" ("ThemeId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251224210903_AddStoreConfigs') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251224210903_AddStoreConfigs', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226140322_TenantRegistration_Phase8') THEN
    ALTER TABLE "Users" ADD "TenantId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226140322_TenantRegistration_Phase8') THEN
    ALTER TABLE "Tenants" ALTER COLUMN "Slug" TYPE character varying(63);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226140322_TenantRegistration_Phase8') THEN
    ALTER TABLE "Tenants" ALTER COLUMN "Name" TYPE character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226140322_TenantRegistration_Phase8') THEN
    ALTER TABLE "Tenants" ADD "BootstrapVersion" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226140322_TenantRegistration_Phase8') THEN
    ALTER TABLE "Tenants" ADD "Country" character varying(2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226140322_TenantRegistration_Phase8') THEN
    ALTER TABLE "Tenants" ADD "Currency" character varying(3);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226140322_TenantRegistration_Phase8') THEN
    ALTER TABLE "Tenants" ADD "DeletedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226140322_TenantRegistration_Phase8') THEN
    ALTER TABLE "Tenants" ADD "Status" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226140322_TenantRegistration_Phase8') THEN
    ALTER TABLE "Tenants" ADD "SuspendedReason" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226140322_TenantRegistration_Phase8') THEN
    ALTER TABLE "Tenants" ADD "UpdatedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226140322_TenantRegistration_Phase8') THEN
    ALTER TABLE "StoreConfigs" ADD "MaintenanceMessage" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226140322_TenantRegistration_Phase8') THEN
    ALTER TABLE "StoreConfigs" ADD "StorefrontStatus" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226140322_TenantRegistration_Phase8') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226140322_TenantRegistration_Phase8') THEN
    CREATE INDEX "IX_Users_TenantId" ON "Users" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226140322_TenantRegistration_Phase8') THEN
    CREATE INDEX "IX_Tenants_Status" ON "Tenants" ("Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226140322_TenantRegistration_Phase8') THEN
    CREATE UNIQUE INDEX "IX_OnboardingChecklists_TenantId" ON "OnboardingChecklists" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226140322_TenantRegistration_Phase8') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251226140322_TenantRegistration_Phase8', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226154845_EmailVerification_Phase84') THEN
    ALTER TABLE "Users" ADD "EmailVerificationStatus" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226154845_EmailVerification_Phase84') THEN
    ALTER TABLE "Users" ADD "EmailVerifiedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226154845_EmailVerification_Phase84') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226154845_EmailVerification_Phase84') THEN
    CREATE INDEX "IX_EmailVerificationTokens_ExpiresAtUtc" ON "EmailVerificationTokens" ("ExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226154845_EmailVerification_Phase84') THEN
    CREATE UNIQUE INDEX "IX_EmailVerificationTokens_TokenHash" ON "EmailVerificationTokens" ("TokenHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226154845_EmailVerification_Phase84') THEN
    CREATE INDEX "IX_EmailVerificationTokens_UserId" ON "EmailVerificationTokens" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226154845_EmailVerification_Phase84') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251226154845_EmailVerification_Phase84', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    DROP INDEX "IX_Banks_BankName";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    DELETE FROM "FinancialAccounts"
    WHERE "Id" = '11111111-1111-1111-1111-111111111111';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    DELETE FROM "ShippingMethods"
    WHERE "Id" = '11111111-2222-3333-4444-555555555551';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    DELETE FROM "ShippingMethods"
    WHERE "Id" = '11111111-2222-3333-4444-555555555552';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    DELETE FROM "ShippingMethods"
    WHERE "Id" = '11111111-2222-3333-4444-555555555553';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    ALTER TABLE "WarehouseStocks" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    UPDATE "Warehouses" SET "TenantId" = '00000000-0000-0000-0000-000000000000' WHERE "TenantId" IS NULL;
    ALTER TABLE "Warehouses" ALTER COLUMN "TenantId" SET NOT NULL;
    ALTER TABLE "Warehouses" ALTER COLUMN "TenantId" SET DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    ALTER TABLE "VisaTransactions" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    UPDATE "Users" SET "TenantId" = '00000000-0000-0000-0000-000000000000' WHERE "TenantId" IS NULL;
    ALTER TABLE "Users" ALTER COLUMN "TenantId" SET NOT NULL;
    ALTER TABLE "Users" ALTER COLUMN "TenantId" SET DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    ALTER TABLE "Units" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    ALTER TABLE "TreasuryTransactions" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    ALTER TABLE "SupplierTransactions" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    ALTER TABLE "Suppliers" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    ALTER TABLE "StockTransfers" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    ALTER TABLE "StockMovements" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    ALTER TABLE "ShippingMethods" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    ALTER TABLE "SalesRepresentatives" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    ALTER TABLE "SalesInvoices" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    ALTER TABLE "Returns" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    ALTER TABLE "PurchaseInvoices" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    UPDATE "Products" SET "TenantId" = '00000000-0000-0000-0000-000000000000' WHERE "TenantId" IS NULL;
    ALTER TABLE "Products" ALTER COLUMN "TenantId" SET NOT NULL;
    ALTER TABLE "Products" ALTER COLUMN "TenantId" SET DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    ALTER TABLE "Orders" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    ALTER TABLE "FinancialTransactions" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    ALTER TABLE "FinancialAccounts" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    ALTER TABLE "Expenses" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    ALTER TABLE "ExpenseCategories" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    ALTER TABLE "CustomerTransactions" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    UPDATE "Customers" SET "TenantId" = '00000000-0000-0000-0000-000000000000' WHERE "TenantId" IS NULL;
    ALTER TABLE "Customers" ALTER COLUMN "TenantId" SET NOT NULL;
    ALTER TABLE "Customers" ALTER COLUMN "TenantId" SET DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    ALTER TABLE "Coupons" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    UPDATE "Categories" SET "TenantId" = '00000000-0000-0000-0000-000000000000' WHERE "TenantId" IS NULL;
    ALTER TABLE "Categories" ALTER COLUMN "TenantId" SET NOT NULL;
    ALTER TABLE "Categories" ALTER COLUMN "TenantId" SET DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    ALTER TABLE "CashboxTransactions" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    ALTER TABLE "Cashboxes" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    ALTER TABLE "BankTransactions" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    ALTER TABLE "Banks" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    ALTER TABLE "AccountLedgers" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    CREATE UNIQUE INDEX "IX_Banks_TenantId_BankName" ON "Banks" ("TenantId", "BankName") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226210606_TenantIsolationHardening') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251226210606_TenantIsolationHardening', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226212911_AuditLogTenantId') THEN
    ALTER TABLE "AuditLogs" ADD "TenantId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226212911_AuditLogTenantId') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251226212911_AuditLogTenantId', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260102141736_AddOnlineCustomersTable') THEN
    ALTER TABLE "StoreConfigs" ADD "CustomizationJson" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260102141736_AddOnlineCustomersTable') THEN
    ALTER TABLE "StoreConfigs" ADD "LiveVersion" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260102141736_AddOnlineCustomersTable') THEN
    ALTER TABLE "StoreConfigs" ADD "ThemeDraftOverridesJson" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260102141736_AddOnlineCustomersTable') THEN
    ALTER TABLE "StoreConfigs" ADD "ThemeLiveOverridesJson" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260102141736_AddOnlineCustomersTable') THEN
    ALTER TABLE "StoreConfigs" ADD "ThemePresetKey" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260102141736_AddOnlineCustomersTable') THEN
    ALTER TABLE "StoreConfigs" ADD "VersionsJson" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260102141736_AddOnlineCustomersTable') THEN
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
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260102141736_AddOnlineCustomersTable') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260102141736_AddOnlineCustomersTable') THEN
    INSERT INTO "ThemePresets" ("Id", "BaseThemeJson", "CreatedAtUtc", "IsRtl", "Key", "Name", "PreviewImageUrl")
    VALUES ('11111111-1111-1111-1111-111111111111', '{"colors":{"primary":"#2563eb","secondary":"#0f172a","background":"#ffffff","text":"#020617","muted":"#64748b"},"typography":{"fontFamily":"Inter","baseSize":"16px"},"radius":{"base":"8px"}}', TIMESTAMPTZ '2024-01-01T00:00:00Z', FALSE, 'default', 'Default Modern', NULL);
    INSERT INTO "ThemePresets" ("Id", "BaseThemeJson", "CreatedAtUtc", "IsRtl", "Key", "Name", "PreviewImageUrl")
    VALUES ('22222222-2222-2222-2222-222222222222', '{"colors":{"primary":"#3b82f6","secondary":"#1e293b","background":"#0f172a","text":"#f8fafc","muted":"#94a3b8"},"typography":{"fontFamily":"Inter","baseSize":"16px"},"radius":{"base":"12px"}}', TIMESTAMPTZ '2024-01-01T00:00:00Z', FALSE, 'dark-minimal', 'Dark Minimal', NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260102141736_AddOnlineCustomersTable') THEN
    CREATE INDEX "IX_OnlineCustomers_CreatedAtUtc" ON "OnlineCustomers" ("CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260102141736_AddOnlineCustomersTable') THEN
    CREATE INDEX "IX_OnlineCustomers_EmailVerified" ON "OnlineCustomers" ("EmailVerified");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260102141736_AddOnlineCustomersTable') THEN
    CREATE INDEX "IX_OnlineCustomers_IsActive" ON "OnlineCustomers" ("IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260102141736_AddOnlineCustomersTable') THEN
    CREATE INDEX "IX_OnlineCustomers_Provider" ON "OnlineCustomers" ("Provider");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260102141736_AddOnlineCustomersTable') THEN
    CREATE INDEX "IX_OnlineCustomers_TenantId" ON "OnlineCustomers" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260102141736_AddOnlineCustomersTable') THEN
    CREATE UNIQUE INDEX "IX_OnlineCustomers_TenantId_Email" ON "OnlineCustomers" ("TenantId", "Email");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260102141736_AddOnlineCustomersTable') THEN
    CREATE INDEX "IX_OnlineCustomers_TenantId_Provider_ProviderUserId" ON "OnlineCustomers" ("TenantId", "Provider", "ProviderUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260102141736_AddOnlineCustomersTable') THEN
    CREATE UNIQUE INDEX "IX_ThemePresets_Key" ON "ThemePresets" ("Key");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260102141736_AddOnlineCustomersTable') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260102141736_AddOnlineCustomersTable', '9.0.1');
    END IF;
END $EF$;
COMMIT;

