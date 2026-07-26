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

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
    ALTER TABLE "OnlineCustomers" ADD "EmailVerifiedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
    ALTER TABLE "OnlineCustomers" ADD "NormalizedEmail" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
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
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
    CREATE INDEX "IX_AuthProvider_TenantId_CustomerId" ON "OnlineCustomerAuthProviders" ("TenantId", "OnlineCustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
    CREATE UNIQUE INDEX "IX_AuthProvider_TenantId_CustomerId_Provider" ON "OnlineCustomerAuthProviders" ("TenantId", "OnlineCustomerId", "Provider");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
    CREATE UNIQUE INDEX "IX_AuthProvider_TenantId_Provider_ProviderUserId" ON "OnlineCustomerAuthProviders" ("TenantId", "Provider", "ProviderUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
    CREATE INDEX "IX_OnlineCustomerAuthProviders_OnlineCustomerId" ON "OnlineCustomerAuthProviders" ("OnlineCustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
    CREATE INDEX "IX_EmailVerification_ExpiresAtUtc" ON "OnlineCustomerEmailVerifications" ("ExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
    CREATE INDEX "IX_EmailVerification_TenantId_CustomerId_IsUsed" ON "OnlineCustomerEmailVerifications" ("TenantId", "OnlineCustomerId", "IsUsed");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
    CREATE UNIQUE INDEX "IX_EmailVerification_TenantId_TokenHash" ON "OnlineCustomerEmailVerifications" ("TenantId", "TokenHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
    CREATE INDEX "IX_OnlineCustomerEmailVerifications_OnlineCustomerId" ON "OnlineCustomerEmailVerifications" ("OnlineCustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
    CREATE INDEX "IX_OnlineCustomerPasswordResetTokens_OnlineCustomerId" ON "OnlineCustomerPasswordResetTokens" ("OnlineCustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
    CREATE INDEX "IX_PasswordResetToken_ExpiresAtUtc" ON "OnlineCustomerPasswordResetTokens" ("ExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
    CREATE INDEX "IX_PasswordResetToken_TenantId_CustomerId_IsUsed" ON "OnlineCustomerPasswordResetTokens" ("TenantId", "OnlineCustomerId", "IsUsed");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
    CREATE UNIQUE INDEX "IX_PasswordResetToken_TenantId_TokenHash" ON "OnlineCustomerPasswordResetTokens" ("TenantId", "TokenHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
    CREATE INDEX "IX_OnlineCustomerRefreshTokens_OnlineCustomerId" ON "OnlineCustomerRefreshTokens" ("OnlineCustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
    CREATE INDEX "IX_RefreshToken_ExpiresAtUtc" ON "OnlineCustomerRefreshTokens" ("ExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
    CREATE INDEX "IX_RefreshToken_TenantId_CustomerId" ON "OnlineCustomerRefreshTokens" ("TenantId", "OnlineCustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
    CREATE INDEX "IX_RefreshToken_TenantId_CustomerId_IsRevoked" ON "OnlineCustomerRefreshTokens" ("TenantId", "OnlineCustomerId", "IsRevoked");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
    CREATE UNIQUE INDEX "IX_RefreshToken_TenantId_TokenHash" ON "OnlineCustomerRefreshTokens" ("TenantId", "TokenHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
    CREATE INDEX "IX_OnlineCustomerWishlistItems_CreatedAtUtc" ON "OnlineCustomerWishlistItems" ("CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
    CREATE INDEX "IX_OnlineCustomerWishlistItems_TenantId_CustomerId" ON "OnlineCustomerWishlistItems" ("TenantId", "OnlineCustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
    CREATE UNIQUE INDEX "IX_OnlineCustomerWishlistItems_TenantId_CustomerId_ProductId" ON "OnlineCustomerWishlistItems" ("TenantId", "OnlineCustomerId", "ProductId") WHERE "IsRemoved" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
    CREATE INDEX "IX_OnlineCustomerWishlistItems_TenantId_IsRemoved" ON "OnlineCustomerWishlistItems" ("TenantId", "IsRemoved");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
    CREATE INDEX "IX_OnlineCustomerWishlistItems_TenantId_ProductId" ON "OnlineCustomerWishlistItems" ("TenantId", "ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
    CREATE UNIQUE INDEX "IX_AuthProviderSettings_TenantId_Provider" ON "StorefrontAuthProviderSettings" ("TenantId", "Provider");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104210031_AddOnlineCustomerWishlistItem') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260104210031_AddOnlineCustomerWishlistItem', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260105201157_AddOnlinePriceToProduct') THEN
    ALTER TABLE "Products" ADD "OnlinePrice" numeric;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260105201157_AddOnlinePriceToProduct') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260105201157_AddOnlinePriceToProduct', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260108143906_AddShippingSnapshotFields') THEN
    ALTER TABLE "Orders" ADD "EstimatedDeliveryDays" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260108143906_AddShippingSnapshotFields') THEN
    ALTER TABLE "Orders" ADD "ShippingMethodName" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260108143906_AddShippingSnapshotFields') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260108143906_AddShippingSnapshotFields', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260108155753_AddTenantPaymentMethods') THEN
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
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260108155753_AddTenantPaymentMethods') THEN
    CREATE INDEX "IX_TenantPaymentMethods_TenantId" ON "TenantPaymentMethods" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260108155753_AddTenantPaymentMethods') THEN
    CREATE UNIQUE INDEX "IX_TenantPaymentMethods_TenantId_Code" ON "TenantPaymentMethods" ("TenantId", "Code");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260108155753_AddTenantPaymentMethods') THEN
    CREATE INDEX "IX_TenantPaymentMethods_TenantId_IsEnabled" ON "TenantPaymentMethods" ("TenantId", "IsEnabled");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260108155753_AddTenantPaymentMethods') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260108155753_AddTenantPaymentMethods', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260109135134_AddEstimatedDeliveryDaysToOrder') THEN
    ALTER TABLE "Orders" ADD "EstimatedDeliveryDays" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260109135134_AddEstimatedDeliveryDaysToOrder') THEN
    ALTER TABLE "Orders" ADD "IsPaid" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260109135134_AddEstimatedDeliveryDaysToOrder') THEN
    ALTER TABLE "Orders" ADD "PaymentFee" numeric NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260109135134_AddEstimatedDeliveryDaysToOrder') THEN
    ALTER TABLE "Orders" ADD "PaymentMethodCode" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260109135134_AddEstimatedDeliveryDaysToOrder') THEN
    ALTER TABLE "Orders" ADD "PaymentMethodName" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260109135134_AddEstimatedDeliveryDaysToOrder') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260109135134_AddEstimatedDeliveryDaysToOrder', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260109175711_AddOnlineCustomerIdToOrders') THEN
    ALTER TABLE "Orders" ADD "OnlineCustomerId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260109175711_AddOnlineCustomerIdToOrders') THEN
    CREATE INDEX "IX_Orders_OnlineCustomerId" ON "Orders" ("OnlineCustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260109175711_AddOnlineCustomerIdToOrders') THEN
    ALTER TABLE "Orders" ADD CONSTRAINT "FK_Orders_OnlineCustomers_OnlineCustomerId" FOREIGN KEY ("OnlineCustomerId") REFERENCES "OnlineCustomers" ("Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260109175711_AddOnlineCustomerIdToOrders') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260109175711_AddOnlineCustomerIdToOrders', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260111160634_FixCouponsTenantIdAndUniqueIndex') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260111160634_FixCouponsTenantIdAndUniqueIndex', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260111160851_FixCouponUniqueIndexToIncludeTenantId') THEN
    DROP INDEX "IX_Coupons_Code";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260111160851_FixCouponUniqueIndexToIncludeTenantId') THEN
    CREATE UNIQUE INDEX "IX_Coupons_TenantId_Code" ON "Coupons" ("TenantId", "Code") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260111160851_FixCouponUniqueIndexToIncludeTenantId') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260111160851_FixCouponUniqueIndexToIncludeTenantId', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260112204103_AddProductVariantsPhase11_1') THEN
    DROP INDEX "IX_ProductVariants_ProductId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260112204103_AddProductVariantsPhase11_1') THEN
    ALTER TABLE "ProductVariants" DROP COLUMN "AdditionalPrice";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260112204103_AddProductVariantsPhase11_1') THEN
    ALTER TABLE "ProductVariants" DROP COLUMN "Name";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260112204103_AddProductVariantsPhase11_1') THEN
    ALTER TABLE "ProductVariants" ALTER COLUMN "SKU" TYPE character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260112204103_AddProductVariantsPhase11_1') THEN
    ALTER TABLE "ProductVariants" ALTER COLUMN "Barcode" TYPE character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260112204103_AddProductVariantsPhase11_1') THEN
    ALTER TABLE "ProductVariants" ADD "CostPrice" numeric(18,2) NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260112204103_AddProductVariantsPhase11_1') THEN
    ALTER TABLE "ProductVariants" ADD "ImageUrl" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260112204103_AddProductVariantsPhase11_1') THEN
    ALTER TABLE "ProductVariants" ADD "IsDefault" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260112204103_AddProductVariantsPhase11_1') THEN
    ALTER TABLE "ProductVariants" ADD "OnlinePrice" numeric(18,2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260112204103_AddProductVariantsPhase11_1') THEN
    ALTER TABLE "ProductVariants" ADD "Price" numeric(18,2) NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260112204103_AddProductVariantsPhase11_1') THEN
    ALTER TABLE "ProductVariants" ADD "SortOrder" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260112204103_AddProductVariantsPhase11_1') THEN
    ALTER TABLE "ProductVariants" ADD "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260112204103_AddProductVariantsPhase11_1') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260112204103_AddProductVariantsPhase11_1') THEN
    CREATE TABLE "ProductVariantStocks" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "VariantId" uuid NOT NULL,
        "WarehouseId" uuid NOT NULL,
        "Quantity" numeric(18,2) NOT NULL,
        "ReservedQuantity" numeric(18,2) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_ProductVariantStocks" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_ProductVariantStocks_ProductVariants_VariantId" FOREIGN KEY ("VariantId") REFERENCES "ProductVariants" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_ProductVariantStocks_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260112204103_AddProductVariantsPhase11_1') THEN
    CREATE INDEX "IX_ProductVariants_Barcode" ON "ProductVariants" ("Barcode") WHERE "IsDeleted" = false AND "Barcode" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260112204103_AddProductVariantsPhase11_1') THEN
    CREATE INDEX "IX_ProductVariants_ProductId_IsDefault" ON "ProductVariants" ("ProductId", "IsDefault") WHERE "IsDefault" = true;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260112204103_AddProductVariantsPhase11_1') THEN
    CREATE INDEX "IX_ProductVariants_SKU" ON "ProductVariants" ("SKU") WHERE "IsDeleted" = false AND "SKU" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260112204103_AddProductVariantsPhase11_1') THEN
    CREATE INDEX "IX_ProductVariants_TenantId_ProductId" ON "ProductVariants" ("TenantId", "ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260112204103_AddProductVariantsPhase11_1') THEN
    CREATE INDEX "IX_ProductVariantAttributes_VariantId" ON "ProductVariantAttributes" ("VariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260112204103_AddProductVariantsPhase11_1') THEN
    CREATE INDEX "IX_ProductVariantAttributes_VariantId_Name" ON "ProductVariantAttributes" ("VariantId", "Name");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260112204103_AddProductVariantsPhase11_1') THEN
    CREATE UNIQUE INDEX "IX_ProductVariantStocks_TenantId_VariantId_WarehouseId" ON "ProductVariantStocks" ("TenantId", "VariantId", "WarehouseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260112204103_AddProductVariantsPhase11_1') THEN
    CREATE INDEX "IX_ProductVariantStocks_VariantId" ON "ProductVariantStocks" ("VariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260112204103_AddProductVariantsPhase11_1') THEN
    CREATE INDEX "IX_ProductVariantStocks_WarehouseId" ON "ProductVariantStocks" ("WarehouseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260112204103_AddProductVariantsPhase11_1') THEN

                    -- Update existing ProductVariants with TenantId from parent Product
                    UPDATE "ProductVariants" pv
                    SET "TenantId" = p."TenantId",
                        "IsDefault" = true,
                        "Price" = p."SalePrice",
                        "OnlinePrice" = p."OnlinePrice",
                        "CostPrice" = p."PurchasePrice"
                    FROM "Products" p
                    WHERE pv."ProductId" = p."Id";
                
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260112204103_AddProductVariantsPhase11_1') THEN

                    INSERT INTO "ProductVariants" (
                        "Id",
                        "TenantId",
                        "ProductId",
                        "SKU",
                        "Barcode",
                        "Price",
                        "OnlinePrice",
                        "CostPrice",
                        "IsDefault",
                        "IsActive",
                        "SortOrder",
                        "CreatedAt",
                        "IsDeleted"
                    )
                    SELECT 
                        gen_random_uuid() AS "Id",
                        p."TenantId",
                        p."Id" AS "ProductId",
                        COALESCE(p."SKU", p."Barcode") AS "SKU",
                        p."Barcode",
                        p."SalePrice" AS "Price",
                        p."OnlinePrice",
                        p."PurchasePrice" AS "CostPrice",
                        true AS "IsDefault",
                        p."IsActive",
                        0 AS "SortOrder",
                        CURRENT_TIMESTAMP AS "CreatedAt",
                        p."IsDeleted"
                    FROM "Products" p
                    WHERE NOT EXISTS (
                        SELECT 1 FROM "ProductVariants" pv WHERE pv."ProductId" = p."Id"
                    );
                
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260112204103_AddProductVariantsPhase11_1') THEN

                    INSERT INTO "ProductVariantStocks" (
                        "Id",
                        "TenantId",
                        "VariantId",
                        "WarehouseId",
                        "Quantity",
                        "ReservedQuantity",
                        "CreatedAt",
                        "IsDeleted"
                    )
                    SELECT 
                        gen_random_uuid() AS "Id",
                        ws."TenantId",
                        pv."Id" AS "VariantId",
                        ws."WarehouseId",
                        ws."Quantity",
                        ws."ReservedQuantity",
                        CURRENT_TIMESTAMP AS "CreatedAt",
                        ws."IsDeleted"
                    FROM "WarehouseStocks" ws
                    INNER JOIN "ProductVariants" pv ON pv."ProductId" = ws."ProductId" AND pv."IsDefault" = true
                    WHERE NOT EXISTS (
                        SELECT 1 FROM "ProductVariantStocks" pvs 
                        WHERE pvs."VariantId" = pv."Id" AND pvs."WarehouseId" = ws."WarehouseId"
                    );
                
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260112204103_AddProductVariantsPhase11_1') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260112204103_AddProductVariantsPhase11_1', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260113153904_AddVariantIdToOrderItemsPhase11_4_1') THEN
    ALTER TABLE "OrderItems" ADD "VariantAttributesJson" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260113153904_AddVariantIdToOrderItemsPhase11_4_1') THEN
    ALTER TABLE "OrderItems" ADD "VariantId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260113153904_AddVariantIdToOrderItemsPhase11_4_1') THEN
    CREATE INDEX "IX_OrderItems_VariantId" ON "OrderItems" ("VariantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260113153904_AddVariantIdToOrderItemsPhase11_4_1') THEN
    ALTER TABLE "OrderItems" ADD CONSTRAINT "FK_OrderItems_ProductVariants_VariantId" FOREIGN KEY ("VariantId") REFERENCES "ProductVariants" ("Id") ON DELETE SET NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260113153904_AddVariantIdToOrderItemsPhase11_4_1') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260113153904_AddVariantIdToOrderItemsPhase11_4_1', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260113204319_AddHasVariantsToProduct') THEN
    ALTER TABLE "Products" ADD "HasVariants" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260113204319_AddHasVariantsToProduct') THEN
    ALTER TABLE "OrderItems" ADD "PaidAmount" numeric(18,2) NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260113204319_AddHasVariantsToProduct') THEN
    ALTER TABLE "OrderItems" ADD "RefundedAmount" numeric(18,2) NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260113204319_AddHasVariantsToProduct') THEN

                    UPDATE "Products" p
                    SET "HasVariants" = true
                    WHERE (
                        SELECT COUNT(*) 
                        FROM "ProductVariants" v 
                        WHERE v."ProductId" = p."Id"
                    ) > 1;
                
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260113204319_AddHasVariantsToProduct') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260113204319_AddHasVariantsToProduct', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115200647_AddStoreConfigOptimisticConcurrency') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260115200647_AddStoreConfigOptimisticConcurrency', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115214438_AddCouponUsagesTable') THEN
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
        CONSTRAINT "FK_CouponUsages_OnlineCustomers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "OnlineCustomers" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_CouponUsages_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115214438_AddCouponUsagesTable') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115214438_AddCouponUsagesTable') THEN
    CREATE INDEX "IX_CouponUsages_CouponId" ON "CouponUsages" ("CouponId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115214438_AddCouponUsagesTable') THEN
    CREATE INDEX "IX_CouponUsages_CustomerId" ON "CouponUsages" ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115214438_AddCouponUsagesTable') THEN
    CREATE INDEX "IX_CouponUsages_OrderId" ON "CouponUsages" ("OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115214438_AddCouponUsagesTable') THEN
    CREATE INDEX "IX_CouponUsages_TenantId_CouponId" ON "CouponUsages" ("TenantId", "CouponId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115214438_AddCouponUsagesTable') THEN
    CREATE INDEX "IX_CouponUsages_TenantId_CouponId_CustomerId" ON "CouponUsages" ("TenantId", "CouponId", "CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115214438_AddCouponUsagesTable') THEN
    CREATE UNIQUE INDEX "IX_CouponUsages_TenantId_OrderId" ON "CouponUsages" ("TenantId", "OrderId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115214438_AddCouponUsagesTable') THEN
    CREATE INDEX "IX_StoreConfigAuditEntries_ActionType" ON "StoreConfigAuditEntries" ("ActionType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115214438_AddCouponUsagesTable') THEN
    CREATE INDEX "IX_StoreConfigAuditEntries_TenantId" ON "StoreConfigAuditEntries" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115214438_AddCouponUsagesTable') THEN
    CREATE INDEX "IX_StoreConfigAuditEntries_TenantId_TimestampUtc" ON "StoreConfigAuditEntries" ("TenantId", "TimestampUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115214438_AddCouponUsagesTable') THEN
    CREATE INDEX "IX_StoreConfigAuditEntries_TimestampUtc" ON "StoreConfigAuditEntries" ("TimestampUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115214438_AddCouponUsagesTable') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260115214438_AddCouponUsagesTable', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260116130637_AddPromotionsEngine') THEN
    ALTER TABLE "Orders" ADD "AppliedPromotionDescription" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260116130637_AddPromotionsEngine') THEN
    ALTER TABLE "Orders" ADD "AppliedPromotionId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260116130637_AddPromotionsEngine') THEN
    ALTER TABLE "Orders" ADD "AppliedPromotionName" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260116130637_AddPromotionsEngine') THEN
    ALTER TABLE "Orders" ADD "AppliedPromotionType" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260116130637_AddPromotionsEngine') THEN
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
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_Promotions" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260116130637_AddPromotionsEngine') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260116130637_AddPromotionsEngine') THEN
    CREATE INDEX "IX_Orders_AppliedPromotionId" ON "Orders" ("AppliedPromotionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260116130637_AddPromotionsEngine') THEN
    CREATE INDEX "IX_Promotions_Priority" ON "Promotions" ("Priority");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260116130637_AddPromotionsEngine') THEN
    CREATE INDEX "IX_Promotions_TenantId_IsActive_StartDate_EndDate" ON "Promotions" ("TenantId", "IsActive", "StartDate", "EndDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260116130637_AddPromotionsEngine') THEN
    CREATE UNIQUE INDEX "IX_Promotions_TenantId_Name" ON "Promotions" ("TenantId", "Name") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260116130637_AddPromotionsEngine') THEN
    CREATE INDEX "IX_Promotions_TenantId_Scope_AppliesToId_IsActive" ON "Promotions" ("TenantId", "Scope", "AppliesToId", "IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260116130637_AddPromotionsEngine') THEN
    CREATE INDEX "IX_PromotionUsages_CustomerId" ON "PromotionUsages" ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260116130637_AddPromotionsEngine') THEN
    CREATE INDEX "IX_PromotionUsages_OrderId" ON "PromotionUsages" ("OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260116130637_AddPromotionsEngine') THEN
    CREATE INDEX "IX_PromotionUsages_PromotionId" ON "PromotionUsages" ("PromotionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260116130637_AddPromotionsEngine') THEN
    CREATE INDEX "IX_PromotionUsages_TenantId_PromotionId" ON "PromotionUsages" ("TenantId", "PromotionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260116130637_AddPromotionsEngine') THEN
    CREATE UNIQUE INDEX "IX_PromotionUsages_TenantId_PromotionId_OrderId" ON "PromotionUsages" ("TenantId", "PromotionId", "OrderId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260116130637_AddPromotionsEngine') THEN
    ALTER TABLE "Orders" ADD CONSTRAINT "FK_Orders_Promotions_AppliedPromotionId" FOREIGN KEY ("AppliedPromotionId") REFERENCES "Promotions" ("Id") ON DELETE SET NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260116130637_AddPromotionsEngine') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260116130637_AddPromotionsEngine', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260116155940_AddOrderPromotionDiscountAmount') THEN
    ALTER TABLE "Orders" ADD "PromotionDiscountAmount" numeric NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260116155940_AddOrderPromotionDiscountAmount') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260116155940_AddOrderPromotionDiscountAmount', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260116194740_AddPromotionBannerStyle') THEN
    ALTER TABLE "Promotions" ADD "BannerStyle" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260116194740_AddPromotionBannerStyle') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260116194740_AddPromotionBannerStyle', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260118125347_AddPromotionRankingConfigToStoreConfig') THEN
    ALTER TABLE "StoreConfigs" ADD "PromotionRanking_BaseBoost" double precision NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260118125347_AddPromotionRankingConfigToStoreConfig') THEN
    ALTER TABLE "StoreConfigs" ADD "PromotionRanking_EnableTimeDecay" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260118125347_AddPromotionRankingConfigToStoreConfig') THEN
    ALTER TABLE "StoreConfigs" ADD "PromotionRanking_Enabled" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260118125347_AddPromotionRankingConfigToStoreConfig') THEN
    ALTER TABLE "StoreConfigs" ADD "PromotionRanking_HighDiscountBoost" double precision NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260118125347_AddPromotionRankingConfigToStoreConfig') THEN
    ALTER TABLE "StoreConfigs" ADD "PromotionRanking_HighDiscountThreshold" double precision NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260118125347_AddPromotionRankingConfigToStoreConfig') THEN
    ALTER TABLE "StoreConfigs" ADD "PromotionRanking_MaxTotalBoost" double precision NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260118125347_AddPromotionRankingConfigToStoreConfig') THEN
    ALTER TABLE "StoreConfigs" ADD "PromotionRanking_TimeDecayMinimum" double precision NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260118125347_AddPromotionRankingConfigToStoreConfig') THEN
    ALTER TABLE "StoreConfigs" ADD "PromotionRanking_TimeDecayOffsetDays" double precision NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260118125347_AddPromotionRankingConfigToStoreConfig') THEN
    ALTER TABLE "StoreConfigs" ADD "PromotionRanking_TimeDecayScaleDays" double precision NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260118125347_AddPromotionRankingConfigToStoreConfig') THEN
    ALTER TABLE "Promotions" ADD "SearchBoostOverride" double precision;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260118125347_AddPromotionRankingConfigToStoreConfig') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260118125347_AddPromotionRankingConfigToStoreConfig', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260120154936_AddTenantAnalyticsConfiguration') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260120154936_AddTenantAnalyticsConfiguration') THEN
    CREATE UNIQUE INDEX "IX_TenantAnalyticsConfigurations_TenantId" ON "TenantAnalyticsConfigurations" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260120154936_AddTenantAnalyticsConfiguration') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260120154936_AddTenantAnalyticsConfiguration', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260121204129_AddCampaignCosts') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260121204129_AddCampaignCosts') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260121204129_AddCampaignCosts') THEN
    CREATE INDEX "IX_AnalyticsAlertRules_TenantId" ON "AnalyticsAlertRules" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260121204129_AddCampaignCosts') THEN
    CREATE INDEX "IX_AnalyticsAlertRules_TenantId_IsEnabled_IsDeleted" ON "AnalyticsAlertRules" ("TenantId", "IsEnabled", "IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260121204129_AddCampaignCosts') THEN
    CREATE INDEX "IX_CampaignCosts_TenantId" ON "CampaignCosts" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260121204129_AddCampaignCosts') THEN
    CREATE INDEX "IX_CampaignCosts_TenantId_PeriodStartUtc_PeriodEndUtc" ON "CampaignCosts" ("TenantId", "PeriodStartUtc", "PeriodEndUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260121204129_AddCampaignCosts') THEN
    CREATE UNIQUE INDEX "IX_CampaignCosts_TenantId_Source_Medium_Campaign_PeriodStartUtc" ON "CampaignCosts" ("TenantId", "Source", "Medium", "Campaign", "PeriodStartUtc") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260121204129_AddCampaignCosts') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260121204129_AddCampaignCosts', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" DROP CONSTRAINT "FK_Shipments_Orders_OrderId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" DROP CONSTRAINT "FK_Shipments_ShippingMethods_ShippingMethodId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" RENAME COLUMN "PackedAtUtc" TO "ReturnedAtUtc";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" RENAME COLUMN "CarrierName" TO "LabelData";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ALTER COLUMN "WeightKg" TYPE numeric(10,3);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ALTER COLUMN "TrackingUrl" TYPE character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ALTER COLUMN "TrackingNumber" TYPE character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ALTER COLUMN "ShippingZipCode" TYPE character varying(20);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ALTER COLUMN "ShippingState" TYPE character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ALTER COLUMN "ShippingMethodName" TYPE character varying(200);
    ALTER TABLE "Shipments" ALTER COLUMN "ShippingMethodName" DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ALTER COLUMN "ShippingMethodId" DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ALTER COLUMN "ShippingCountry" TYPE character varying(10);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ALTER COLUMN "ShippingCost" TYPE numeric(18,2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ALTER COLUMN "ShippingCity" TYPE character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ALTER COLUMN "ShippingAddress" TYPE character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ALTER COLUMN "ShipmentNumber" TYPE character varying(50);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ALTER COLUMN "RecipientPhone" TYPE character varying(50);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ALTER COLUMN "RecipientName" TYPE character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ALTER COLUMN "Notes" TYPE character varying(1000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ALTER COLUMN "Currency" TYPE character varying(10);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ALTER COLUMN "CancellationReason" TYPE character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ADD "AwbNumber" character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ADD "CarrierCode" character varying(50) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ADD "CarrierCreateResponse" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ADD "CarrierMetadata" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ADD "CarrierTrackingResponse" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ADD "CodAmount" numeric(18,2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ADD "CodCollected" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ADD "CodCollectedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ADD "DeliveryAttempts" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ADD "FailedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ADD "FailureReason" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ADD "IsCashOnDelivery" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ADD "LabelFormat" character varying(20);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ADD "LabelUrl" character varying(1000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ADD "LastCarrierSyncAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ADD "Latitude" numeric(9,6);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ADD "Longitude" numeric(9,6);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ADD "OutForDeliveryAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ADD "PackageCount" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ADD "PackageDescription" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ADD "PickedUpAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ADD "ReadyForPickupAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ADD "RecipientEmail" character varying(255);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ADD "ReturnReason" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ADD "ServiceLevel" character varying(50) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ADD "ShippingAddressLine2" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    CREATE INDEX "IX_Shipments_CarrierCode_TrackingNumber" ON "Shipments" ("CarrierCode", "TrackingNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    CREATE INDEX "IX_Shipments_TenantId_CarrierCode_Status" ON "Shipments" ("TenantId", "CarrierCode", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    CREATE UNIQUE INDEX "IX_Shipments_TenantId_OrderId" ON "Shipments" ("TenantId", "OrderId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    CREATE INDEX "IX_Shipments_TenantId_Status_LastCarrierSyncAtUtc" ON "Shipments" ("TenantId", "Status", "LastCarrierSyncAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    CREATE UNIQUE INDEX "IX_CarrierConfigurations_TenantId_CarrierCode" ON "CarrierConfigurations" ("TenantId", "CarrierCode") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    CREATE INDEX "IX_CarrierConfigurations_TenantId_IsDefault" ON "CarrierConfigurations" ("TenantId", "IsDefault") WHERE "IsDefault" = true;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    CREATE INDEX "IX_CarrierConfigurations_TenantId_IsEnabled_SortOrder" ON "CarrierConfigurations" ("TenantId", "IsEnabled", "SortOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ADD CONSTRAINT "FK_Shipments_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    ALTER TABLE "Shipments" ADD CONSTRAINT "FK_Shipments_ShippingMethods_ShippingMethodId" FOREIGN KEY ("ShippingMethodId") REFERENCES "ShippingMethods" ("Id") ON DELETE SET NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122123451_AddShipmentOutForDeliveryTimestamp') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260122123451_AddShipmentOutForDeliveryTimestamp', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260122165616_AddMissingColumnsToCarrierConfigurations') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260122165616_AddMissingColumnsToCarrierConfigurations', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260128151641_ManualWalletCleanup') THEN
    ALTER TABLE "StoreConfigs" ADD "CommerceAiAutoGreetingEnabled" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260128151641_ManualWalletCleanup') THEN
    ALTER TABLE "StoreConfigs" ADD "CommerceAiAutoGreetingMessage" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260128151641_ManualWalletCleanup') THEN
    ALTER TABLE "StoreConfigs" ADD "CommerceAiEnabled" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260128151641_ManualWalletCleanup') THEN
    ALTER TABLE "StoreConfigs" ADD "CommerceAiEnabledAt" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260128151641_ManualWalletCleanup') THEN
    ALTER TABLE "StoreConfigs" ADD "CommerceAiPersonality" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260128151641_ManualWalletCleanup') THEN
    ALTER TABLE "StoreConfigs" ADD "CommerceAiUpdatedBy" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260128151641_ManualWalletCleanup') THEN
    ALTER TABLE "StoreConfigs" ADD "CommerceAiWidgetPosition" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260128151641_ManualWalletCleanup') THEN
    ALTER TABLE "StoreConfigs" ADD "CommerceAiWidgetStyle" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260128151641_ManualWalletCleanup') THEN
    ALTER TABLE "ShippingMethods" ADD "CarrierCode" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260128151641_ManualWalletCleanup') THEN
    ALTER TABLE "PaymentIntents" ADD "ExpiresAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260128151641_ManualWalletCleanup') THEN
    ALTER TABLE "PaymentIntents" ADD "ProofImageDeleted" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260128151641_ManualWalletCleanup') THEN
    ALTER TABLE "PaymentIntents" ADD "ProofImageExpiresAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260128151641_ManualWalletCleanup') THEN
    ALTER TABLE "PaymentIntents" ADD "ProofImageUrl" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260128151641_ManualWalletCleanup') THEN
    ALTER TABLE "PaymentIntents" ADD "ReferenceCode" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260128151641_ManualWalletCleanup') THEN
    ALTER TABLE "PaymentIntents" ADD "RejectionReason" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260128151641_ManualWalletCleanup') THEN
    ALTER TABLE "PaymentIntents" ADD "VerifiedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260128151641_ManualWalletCleanup') THEN
    ALTER TABLE "PaymentIntents" ADD "VerifiedByAdmin" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260128151641_ManualWalletCleanup') THEN
    ALTER TABLE "PaymentIntents" ADD "WalletPhoneNumber" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260128151641_ManualWalletCleanup') THEN
    ALTER TABLE "PaymentIntents" ADD "WalletType" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260128151641_ManualWalletCleanup') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260128151641_ManualWalletCleanup', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260129143905_AddWhatsAppMessaging') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260129143905_AddWhatsAppMessaging') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260129143905_AddWhatsAppMessaging') THEN
    CREATE TABLE whatsapp_accounts (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        phone_number character varying(20) NOT NULL,
        display_name character varying(100) NOT NULL,
        status integer NOT NULL,
        enabled boolean NOT NULL DEFAULT FALSE,
        use_for_order_confirmation boolean NOT NULL DEFAULT FALSE,
        use_for_otp_verification boolean NOT NULL DEFAULT FALSE,
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260129143905_AddWhatsAppMessaging') THEN
    CREATE UNIQUE INDEX "IX_PixelTrackingConfigs_TenantId" ON "PixelTrackingConfigs" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260129143905_AddWhatsAppMessaging') THEN
    CREATE INDEX "IX_ProductPixelOverrides_ProductId" ON "ProductPixelOverrides" ("ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260129143905_AddWhatsAppMessaging') THEN
    CREATE INDEX "IX_ProductPixelOverrides_TenantId" ON "ProductPixelOverrides" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260129143905_AddWhatsAppMessaging') THEN
    CREATE UNIQUE INDEX "IX_ProductPixelOverrides_TenantId_ProductId" ON "ProductPixelOverrides" ("TenantId", "ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260129143905_AddWhatsAppMessaging') THEN
    CREATE INDEX ix_whatsapp_accounts_status ON whatsapp_accounts (status) WHERE enabled = TRUE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260129143905_AddWhatsAppMessaging') THEN
    CREATE INDEX ix_whatsapp_accounts_tenant_id ON whatsapp_accounts (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260129143905_AddWhatsAppMessaging') THEN
    CREATE UNIQUE INDEX uq_whatsapp_account_phone_tenant ON whatsapp_accounts (tenant_id, phone_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260129143905_AddWhatsAppMessaging') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260129143905_AddWhatsAppMessaging', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260129191429_WhatsAppTemplateHardening') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260129191429_WhatsAppTemplateHardening') THEN
    CREATE UNIQUE INDEX uq_whatsapp_template_tenant_type_language ON whatsapp_message_templates ("TenantId", "Type", "Language");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260129191429_WhatsAppTemplateHardening') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260129191429_WhatsAppTemplateHardening', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260204190605_AddMissingColumnsToTenantAutomationSettings') THEN
    ALTER TABLE whatsapp_accounts ADD use_for_abandoned_cart_recovery boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260204190605_AddMissingColumnsToTenantAutomationSettings') THEN
    ALTER TABLE whatsapp_accounts ADD use_for_payment_approval boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260204190605_AddMissingColumnsToTenantAutomationSettings') THEN
    ALTER TABLE "Tenants" ADD "UseBaileys" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260204190605_AddMissingColumnsToTenantAutomationSettings') THEN
    ALTER TABLE "OnlineCustomers" ADD "PhoneVerified" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260204190605_AddMissingColumnsToTenantAutomationSettings') THEN
    ALTER TABLE "OnlineCustomers" ADD "PhoneVerifiedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260204190605_AddMissingColumnsToTenantAutomationSettings') THEN
    CREATE TABLE "AbandonedCarts" (
        "Id" uuid NOT NULL,
        "CartId" uuid NOT NULL,
        "CustomerId" uuid,
        "PhoneNumber" text NOT NULL,
        "Email" text,
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
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        "TenantId" uuid NOT NULL,
        CONSTRAINT "PK_AbandonedCarts" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AbandonedCarts_OnlineCustomers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "OnlineCustomers" ("Id"),
        CONSTRAINT "FK_AbandonedCarts_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260204190605_AddMissingColumnsToTenantAutomationSettings') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260204190605_AddMissingColumnsToTenantAutomationSettings') THEN
    CREATE TABLE "AbandonedCartRecoveryLog" (
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
        CONSTRAINT "PK_AbandonedCartRecoveryLog" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AbandonedCartRecoveryLog_AbandonedCarts_AbandonedCartId" FOREIGN KEY ("AbandonedCartId") REFERENCES "AbandonedCarts" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260204190605_AddMissingColumnsToTenantAutomationSettings') THEN
    CREATE INDEX "IX_AbandonedCartRecoveryLog_AbandonedCartId" ON "AbandonedCartRecoveryLog" ("AbandonedCartId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260204190605_AddMissingColumnsToTenantAutomationSettings') THEN
    CREATE INDEX "IX_AbandonedCarts_CartId" ON "AbandonedCarts" ("CartId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260204190605_AddMissingColumnsToTenantAutomationSettings') THEN
    CREATE INDEX "IX_AbandonedCarts_CustomerId" ON "AbandonedCarts" ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260204190605_AddMissingColumnsToTenantAutomationSettings') THEN
    CREATE INDEX "IX_AbandonedCarts_RecoveryStatus_LastActivityAtUtc" ON "AbandonedCarts" ("RecoveryStatus", "LastActivityAtUtc") WHERE "RecoveryStatus" = 'Active';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260204190605_AddMissingColumnsToTenantAutomationSettings') THEN
    CREATE INDEX "IX_AbandonedCarts_TenantId" ON "AbandonedCarts" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260204190605_AddMissingColumnsToTenantAutomationSettings') THEN
    CREATE UNIQUE INDEX "IX_AbandonedCarts_TenantId_CartId" ON "AbandonedCarts" ("TenantId", "CartId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260204190605_AddMissingColumnsToTenantAutomationSettings') THEN
    CREATE INDEX "IX_TenantAutomationSettings_EnableAbandonedRecovery" ON "TenantAutomationSettings" ("EnableAbandonedRecovery");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260204190605_AddMissingColumnsToTenantAutomationSettings') THEN
    CREATE UNIQUE INDEX "IX_TenantAutomationSettings_TenantId" ON "TenantAutomationSettings" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260204190605_AddMissingColumnsToTenantAutomationSettings') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260204190605_AddMissingColumnsToTenantAutomationSettings', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260204203053_AddWhatsAppRateLimitsTable') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260204203053_AddWhatsAppRateLimitsTable') THEN
    CREATE INDEX "IX_WhatsAppRateLimits_TenantId" ON "WhatsAppRateLimits" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260204203053_AddWhatsAppRateLimitsTable') THEN
    CREATE UNIQUE INDEX "IX_WhatsAppRateLimits_TenantId_PhoneNumber" ON "WhatsAppRateLimits" ("TenantId", "PhoneNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260204203053_AddWhatsAppRateLimitsTable') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260204203053_AddWhatsAppRateLimitsTable', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN
    ALTER TABLE "AbandonedCarts" DROP COLUMN "CreatedAt";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN
    ALTER TABLE "AbandonedCarts" DROP COLUMN "CreatedBy";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN
    ALTER TABLE "AbandonedCarts" DROP COLUMN "IsDeleted";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN
    ALTER TABLE "AbandonedCarts" DROP COLUMN "UpdatedAt";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN
    ALTER TABLE "AbandonedCarts" DROP COLUMN "UpdatedBy";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN
    CREATE TABLE "Accounts" (
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
        CONSTRAINT "PK_Accounts" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Accounts_Accounts_ParentAccountId" FOREIGN KEY ("ParentAccountId") REFERENCES "Accounts" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN
    CREATE TABLE "JournalEntries" (
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
        CONSTRAINT "PK_JournalEntries" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_JournalEntries_JournalEntries_ReversedByJournalId" FOREIGN KEY ("ReversedByJournalId") REFERENCES "JournalEntries" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN
    CREATE TABLE "JournalNumberSequences" (
        "TenantId" uuid NOT NULL,
        "CurrentYear" integer NOT NULL,
        "NextNumber" integer NOT NULL DEFAULT 1,
        CONSTRAINT "PK_JournalNumberSequences" PRIMARY KEY ("TenantId")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN
    CREATE TABLE "LedgerEntries" (
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
        CONSTRAINT "PK_LedgerEntries" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_LedgerEntries_PositiveAmounts" CHECK ("DebitAmount" >= 0 AND "CreditAmount" >= 0),
        CONSTRAINT "CK_LedgerEntries_SingleSide" CHECK (("DebitAmount" > 0 AND "CreditAmount" = 0) OR ("DebitAmount" = 0 AND "CreditAmount" > 0)),
        CONSTRAINT "FK_LedgerEntries_Accounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES "Accounts" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_LedgerEntries_JournalEntries_JournalEntryId" FOREIGN KEY ("JournalEntryId") REFERENCES "JournalEntries" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN
    CREATE INDEX "IX_Accounts_ParentAccountId" ON "Accounts" ("ParentAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN
    CREATE INDEX "IX_Accounts_TenantId" ON "Accounts" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN
    CREATE UNIQUE INDEX "IX_Accounts_TenantId_AccountCode" ON "Accounts" ("TenantId", "AccountCode");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN
    CREATE INDEX "IX_Accounts_TenantId_AccountType" ON "Accounts" ("TenantId", "AccountType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN
    CREATE INDEX "IX_Accounts_TenantId_IsClearing" ON "Accounts" ("TenantId", "IsClearing") WHERE "IsClearing" = true;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN
    CREATE INDEX "IX_JournalEntries_PostedAtUtc" ON "JournalEntries" ("PostedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN
    CREATE UNIQUE INDEX "IX_JournalEntries_ReversedByJournalId" ON "JournalEntries" ("ReversedByJournalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN
    CREATE INDEX "IX_JournalEntries_TenantId" ON "JournalEntries" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN
    CREATE UNIQUE INDEX "IX_JournalEntries_TenantId_IdempotencyKey" ON "JournalEntries" ("TenantId", "IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN
    CREATE INDEX "IX_JournalEntries_TenantId_JournalDate" ON "JournalEntries" ("TenantId", "JournalDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN
    CREATE UNIQUE INDEX "IX_JournalEntries_TenantId_JournalNumber" ON "JournalEntries" ("TenantId", "JournalNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN
    CREATE INDEX "IX_JournalEntries_TenantId_Source" ON "JournalEntries" ("TenantId", "SourceType", "SourceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN
    CREATE INDEX "IX_JournalEntries_TenantId_Status" ON "JournalEntries" ("TenantId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN
    CREATE INDEX "IX_LedgerEntries_AccountId_CreatedAtUtc" ON "LedgerEntries" ("AccountId", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN
    CREATE INDEX "IX_LedgerEntries_JournalEntryId" ON "LedgerEntries" ("JournalEntryId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN
    CREATE INDEX "IX_LedgerEntries_TenantId" ON "LedgerEntries" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN
    CREATE INDEX "IX_LedgerEntries_TenantId_AccountId" ON "LedgerEntries" ("TenantId", "AccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN

                    CREATE OR REPLACE FUNCTION prevent_journal_modification()
                    RETURNS TRIGGER AS $$
                    BEGIN
                        IF TG_OP = 'UPDATE' THEN
                            IF OLD."JournalNumber" != NEW."JournalNumber" OR
                               OLD."JournalDate" != NEW."JournalDate" OR
                               OLD."SourceType" != NEW."SourceType" OR
                               OLD."SourceId" != NEW."SourceId" OR
                               OLD."Description" != NEW."Description" OR
                               OLD."IdempotencyKey" != NEW."IdempotencyKey" OR
                               OLD."PostedAtUtc" != NEW."PostedAtUtc" THEN
                                RAISE EXCEPTION 'Journal entries are immutable. Only status and reversal fields can be updated.';
                            END IF;
                            RETURN NEW;
                        END IF;
                        
                        IF TG_OP = 'DELETE' THEN
                            RAISE EXCEPTION 'Journal entries cannot be deleted. Use reversal instead.';
                        END IF;
                        
                        RETURN NULL;
                    END;
                    $$ LANGUAGE plpgsql;
                
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN

                    CREATE OR REPLACE FUNCTION prevent_ledger_modification()
                    RETURNS TRIGGER AS $$
                    BEGIN
                        IF TG_OP = 'UPDATE' THEN
                            RAISE EXCEPTION 'Ledger entries are immutable and cannot be updated.';
                        END IF;
                        
                        IF TG_OP = 'DELETE' THEN
                            RAISE EXCEPTION 'Ledger entries cannot be deleted. Use journal reversal instead.';
                        END IF;
                        
                        RETURN NULL;
                    END;
                    $$ LANGUAGE plpgsql;
                
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN

                    DROP TRIGGER IF EXISTS trg_prevent_journal_modification ON "JournalEntries";
                    CREATE TRIGGER trg_prevent_journal_modification
                        BEFORE UPDATE OR DELETE ON "JournalEntries"
                        FOR EACH ROW
                        EXECUTE FUNCTION prevent_journal_modification();
                
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN

                    DROP TRIGGER IF EXISTS trg_prevent_ledger_modification ON "LedgerEntries";
                    CREATE TRIGGER trg_prevent_ledger_modification
                        BEFORE UPDATE OR DELETE ON "LedgerEntries"
                        FOR EACH ROW
                        EXECUTE FUNCTION prevent_ledger_modification();
                
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206165348_AddCoreLedgerEngine') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260206165348_AddCoreLedgerEngine', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER TABLE "Accounts" DROP CONSTRAINT "FK_Accounts_Accounts_ParentAccountId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER TABLE "JournalEntries" DROP CONSTRAINT "FK_JournalEntries_JournalEntries_ReversedByJournalId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER TABLE "LedgerEntries" DROP CONSTRAINT "FK_LedgerEntries_Accounts_AccountId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER TABLE "LedgerEntries" DROP CONSTRAINT "FK_LedgerEntries_JournalEntries_JournalEntryId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER TABLE "LedgerEntries" DROP CONSTRAINT "PK_LedgerEntries";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER TABLE "LedgerEntries" DROP CONSTRAINT "CK_LedgerEntries_PositiveAmounts";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER TABLE "LedgerEntries" DROP CONSTRAINT "CK_LedgerEntries_SingleSide";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER TABLE "JournalNumberSequences" DROP CONSTRAINT "PK_JournalNumberSequences";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER TABLE "JournalEntries" DROP CONSTRAINT "PK_JournalEntries";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER TABLE "Accounts" DROP CONSTRAINT "PK_Accounts";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER TABLE "LedgerEntries" RENAME TO "LedgerLines";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER TABLE "JournalNumberSequences" RENAME TO "LedgerJournalNumberSequences";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER TABLE "JournalEntries" RENAME TO "LedgerJournalEntries";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER TABLE "Accounts" RENAME TO "LedgerAccounts";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER INDEX "IX_LedgerEntries_TenantId_AccountId" RENAME TO "IX_LedgerLines_TenantAcct";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER INDEX "IX_LedgerEntries_TenantId" RENAME TO "IX_LedgerLines_Tenant";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER INDEX "IX_LedgerEntries_JournalEntryId" RENAME TO "IX_LedgerLines_Journal";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER INDEX "IX_LedgerEntries_AccountId_CreatedAtUtc" RENAME TO "IX_LedgerLines_AcctDate";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER INDEX "IX_JournalEntries_TenantId_Status" RENAME TO "IX_LedgerJournal_Status";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER INDEX "IX_JournalEntries_TenantId_Source" RENAME TO "IX_LedgerJournal_Source";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER INDEX "IX_JournalEntries_TenantId_JournalNumber" RENAME TO "IX_LedgerJournal_Number";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER INDEX "IX_JournalEntries_TenantId_JournalDate" RENAME TO "IX_LedgerJournal_Date";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER INDEX "IX_JournalEntries_TenantId_IdempotencyKey" RENAME TO "IX_LedgerJournal_Idempotency";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER INDEX "IX_JournalEntries_TenantId" RENAME TO "IX_LedgerJournal_Tenant";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER INDEX "IX_JournalEntries_ReversedByJournalId" RENAME TO "IX_LedgerJournalEntries_ReversedByJournalId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER INDEX "IX_JournalEntries_PostedAtUtc" RENAME TO "IX_LedgerJournal_Posted";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER INDEX "IX_Accounts_TenantId_IsClearing" RENAME TO "IX_LedgerAcct_Clearing";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER INDEX "IX_Accounts_TenantId_AccountType" RENAME TO "IX_LedgerAcct_TenantType";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER INDEX "IX_Accounts_TenantId_AccountCode" RENAME TO "IX_LedgerAcct_TenantCode";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER INDEX "IX_Accounts_TenantId" RENAME TO "IX_LedgerAcct_Tenant";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER INDEX "IX_Accounts_ParentAccountId" RENAME TO "IX_LedgerAcct_Parent";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER TABLE "Orders" ADD "FulfillmentCarrierCode" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER TABLE "Orders" ADD "FulfillmentMode" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER TABLE "Orders" ADD "FulfillmentStatus" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER TABLE "LedgerLines" ADD CONSTRAINT "PK_LedgerLines" PRIMARY KEY ("Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER TABLE "LedgerJournalNumberSequences" ADD CONSTRAINT "PK_LedgerJournalNumberSequences" PRIMARY KEY ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER TABLE "LedgerJournalEntries" ADD CONSTRAINT "PK_LedgerJournalEntries" PRIMARY KEY ("Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER TABLE "LedgerAccounts" ADD CONSTRAINT "PK_LedgerAccounts" PRIMARY KEY ("Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE TABLE "AccountingOutboxMessages" (
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
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
        CONSTRAINT "FK_TreasuryAccounts_LedgerAccounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES "LedgerAccounts" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE TABLE "ServiceRequests" (
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
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
        CONSTRAINT "FK_ServiceReviews_ServiceRequests_ServiceRequestId" FOREIGN KEY ("ServiceRequestId") REFERENCES "ServiceRequests" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_ServiceReviews_Services_ServiceId" FOREIGN KEY ("ServiceId") REFERENCES "Services" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER TABLE "LedgerLines" ADD CONSTRAINT "CK_LedgerLines_PositiveAmounts" CHECK ("DebitAmount" >= 0 AND "CreditAmount" >= 0);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER TABLE "LedgerLines" ADD CONSTRAINT "CK_LedgerLines_SingleSide" CHECK (("DebitAmount" > 0 AND "CreditAmount" = 0) OR ("DebitAmount" = 0 AND "CreditAmount" > 0));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_AccountingOutbox_Failed" ON "AccountingOutboxMessages" ("TenantId", "IsFailed") WHERE "IsFailed" = true;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_AccountingOutbox_OccurredAtUtc" ON "AccountingOutboxMessages" ("OccurredAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_AccountingOutbox_ProcessingStatus" ON "AccountingOutboxMessages" ("ProcessedAtUtc", "IsFailed", "NextRetryAtUtc") WHERE "ProcessedAtUtc" IS NULL AND "IsFailed" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE UNIQUE INDEX "IX_AccountingOutbox_TenantId_IdempotencyKey" ON "AccountingOutboxMessages" ("TenantId", "IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_CommerceAudit_Business" ON "CommerceAccountingAudit" ("TenantId", "BusinessType", "BusinessId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_CommerceAudit_JournalId" ON "CommerceAccountingAudit" ("JournalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_CommerceAudit_TenantId_PostedAtUtc" ON "CommerceAccountingAudit" ("TenantId", "PostedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_MarketplaceProfiles_IsActive" ON "MarketplaceProfiles" ("IsActive") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_MarketplaceProfiles_TenantId" ON "MarketplaceProfiles" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_MarketplaceProfiles_UserId" ON "MarketplaceProfiles" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE UNIQUE INDEX "UQ_MarketplaceProfiles_TenantUser" ON "MarketplaceProfiles" ("TenantId", "UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_PaymentMethods_ClearingAccountId" ON "PaymentMethods" ("ClearingAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_PaymentMethods_FeesAccountId" ON "PaymentMethods" ("FeesAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_PaymentMethods_TargetAccountId" ON "PaymentMethods" ("TargetAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_PaymentMethods_Tenant" ON "PaymentMethods" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE UNIQUE INDEX "IX_PaymentMethods_TenantCode" ON "PaymentMethods" ("TenantId", "Code");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_ProviderTrustAggregates_AverageRating" ON "ProviderTrustAggregates" ("AverageRating" DESC) WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_ProviderTrustAggregates_TenantId" ON "ProviderTrustAggregates" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_ProviderTrustAggregates_TotalReviews" ON "ProviderTrustAggregates" ("TotalReviews" DESC) WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE UNIQUE INDEX "UQ_ProviderTrustAggregates_ProviderProfile" ON "ProviderTrustAggregates" ("ProviderProfileId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_ServiceRequestMessages_SenderUserId" ON "ServiceRequestMessages" ("SenderUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_ServiceRequestMessages_ServiceRequestId_CreatedAt" ON "ServiceRequestMessages" ("ServiceRequestId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_ServiceRequestMessages_TenantId" ON "ServiceRequestMessages" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_ServiceRequests_BuyerUserId" ON "ServiceRequests" ("BuyerUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_ServiceRequests_ProviderUserId" ON "ServiceRequests" ("ProviderUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_ServiceRequests_ServiceId" ON "ServiceRequests" ("ServiceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_ServiceRequests_Status" ON "ServiceRequests" ("Status") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_ServiceRequests_TenantId" ON "ServiceRequests" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_ServiceReviews_ProviderUserId" ON "ServiceReviews" ("ProviderUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_ServiceReviews_Rating" ON "ServiceReviews" ("Rating") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_ServiceReviews_ServiceId" ON "ServiceReviews" ("ServiceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_ServiceReviews_TenantId" ON "ServiceReviews" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE UNIQUE INDEX "UQ_ServiceReviews_ServiceRequest" ON "ServiceReviews" ("ServiceRequestId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_Services_Category" ON "Services" ("Category") WHERE "IsDeleted" = false AND "IsActive" = true;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_Services_ProfileId" ON "Services" ("ProfileId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_Services_TenantId" ON "Services" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_TreasuryAccounts_AccountId" ON "TreasuryAccounts" ("AccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_TreasuryAccounts_Tenant" ON "TreasuryAccounts" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE UNIQUE INDEX "IX_TreasuryAccounts_TenantAccount" ON "TreasuryAccounts" ("TenantId", "AccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_TreasuryAccounts_TenantType" ON "TreasuryAccounts" ("TenantId", "Type");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER TABLE "LedgerAccounts" ADD CONSTRAINT "FK_LedgerAccounts_LedgerAccounts_ParentAccountId" FOREIGN KEY ("ParentAccountId") REFERENCES "LedgerAccounts" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER TABLE "LedgerJournalEntries" ADD CONSTRAINT "FK_LedgerJournalEntries_LedgerJournalEntries_ReversedByJournal~" FOREIGN KEY ("ReversedByJournalId") REFERENCES "LedgerJournalEntries" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER TABLE "LedgerLines" ADD CONSTRAINT "FK_LedgerLines_LedgerAccounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES "LedgerAccounts" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_ServiceRequests_BuyerUserId_Status" ON "ServiceRequests" ("BuyerUserId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    CREATE INDEX "IX_ServiceRequests_ProviderUserId_Status" ON "ServiceRequests" ("ProviderUserId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    ALTER TABLE "LedgerLines" ADD CONSTRAINT "FK_LedgerLines_LedgerJournalEntries_JournalEntryId" FOREIGN KEY ("JournalEntryId") REFERENCES "LedgerJournalEntries" ("Id") ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260213225438_AddServiceRequestCompositeIndexes') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260213225438_AddServiceRequestCompositeIndexes', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    ALTER TABLE "ProviderTrustAggregates" ADD "MarketingReviewCount" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    ALTER TABLE "ProviderTrustAggregates" ADD "MarketingWeightedScore" numeric NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    ALTER TABLE "ProviderTrustAggregates" ADD "PosVerifiedCount" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    ALTER TABLE "ProviderTrustAggregates" ADD "TotalVerifiedOrders" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    ALTER TABLE "ProviderTrustAggregates" ADD "TotalVerifiedRevenue" numeric NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
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
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_AffiliateAccounts" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AffiliateAccounts_Status" CHECK ("Status" IN (0, 1, 2)),
        CONSTRAINT "FK_AffiliateAccounts_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE TABLE "AffiliateProductPolicies" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "CommissionRate" numeric(8,6) NOT NULL,
        "IsEnabled" boolean NOT NULL,
        "MaxCommissionPerItem" numeric(18,4),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "CreatedBy" uuid,
        "UpdatedBy" uuid,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_AffiliateProductPolicies" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AffiliateProductPolicies_MaxCap" CHECK ("MaxCommissionPerItem" IS NULL OR "MaxCommissionPerItem" > 0),
        CONSTRAINT "CK_AffiliateProductPolicies_Rate" CHECK ("CommissionRate" >= 0.0001 AND "CommissionRate" <= 0.50),
        CONSTRAINT "FK_AffiliateProductPolicies_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE TABLE "AffiliateAttributions" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "AffiliateAccountId" uuid NOT NULL,
        "ReferralCode" character varying(12) NOT NULL,
        "Source" integer NOT NULL,
        "AttributedAtUtc" timestamp with time zone NOT NULL,
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE TABLE "AffiliateUsers" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Email" character varying(255) NOT NULL,
        "PasswordHash" character varying(500) NOT NULL,
        "AffiliateAccountId" uuid NOT NULL,
        "EmailVerified" boolean NOT NULL DEFAULT FALSE,
        "RefreshToken" character varying(500),
        "RefreshTokenExpiryTime" timestamp with time zone,
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE TABLE "AffiliateCommissionLedger" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" uuid,
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE INDEX "IX_AffiliateAccounts_TenantId" ON "AffiliateAccounts" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE INDEX "IX_AffiliateAccounts_UserId" ON "AffiliateAccounts" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE UNIQUE INDEX "UQ_AffiliateAccounts_Tenant_ReferralCode" ON "AffiliateAccounts" ("TenantId", "ReferralCode");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE UNIQUE INDEX "UQ_AffiliateAccounts_Tenant_User" ON "AffiliateAccounts" ("TenantId", "UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE INDEX "IX_AffiliateAttributions_AffiliateAccountId" ON "AffiliateAttributions" ("AffiliateAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE INDEX "IX_AffiliateAttributions_OrderId" ON "AffiliateAttributions" ("OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE INDEX "IX_AffiliateAttributions_TenantId" ON "AffiliateAttributions" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE UNIQUE INDEX "UQ_AffiliateAttributions_Tenant_Order" ON "AffiliateAttributions" ("TenantId", "OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE INDEX "IX_AffiliateCommissionLedger_AttributionId" ON "AffiliateCommissionLedger" ("AttributionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE INDEX "IX_AffiliateCommissionLedger_OrderItemId" ON "AffiliateCommissionLedger" ("OrderItemId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE INDEX "IX_AffiliateCommissionLedger_OriginalEntryId" ON "AffiliateCommissionLedger" ("OriginalEntryId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE INDEX "IX_AffiliateCommLedger_AffiliateAccountId" ON "AffiliateCommissionLedger" ("AffiliateAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE INDEX "IX_AffiliateCommLedger_OrderId" ON "AffiliateCommissionLedger" ("OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE INDEX "IX_AffiliateCommLedger_TenantId" ON "AffiliateCommissionLedger" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE INDEX "IX_AffiliateCommLedger_TenantRecorded" ON "AffiliateCommissionLedger" ("TenantId", "RecordedAtUtc" DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE UNIQUE INDEX "UQ_AffiliateCommLedger_Idempotency" ON "AffiliateCommissionLedger" ("TenantId", "IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE INDEX "IX_AffiliateProductPolicies_ProductId" ON "AffiliateProductPolicies" ("ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE INDEX "IX_AffiliateProductPolicies_TenantId" ON "AffiliateProductPolicies" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE UNIQUE INDEX "UQ_AffiliateProductPolicies_Tenant_Product" ON "AffiliateProductPolicies" ("TenantId", "ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE INDEX "IX_AffiliateUsers_TenantId" ON "AffiliateUsers" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE UNIQUE INDEX "UQ_AffiliateUsers_AffiliateAccount" ON "AffiliateUsers" ("AffiliateAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE UNIQUE INDEX "UQ_AffiliateUsers_Email_Tenant" ON "AffiliateUsers" ("TenantId", "Email");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE INDEX "IX_MarketingReviewEvidences_EvidenceType" ON "MarketingReviewEvidences" ("EvidenceType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE INDEX "IX_MarketingReviewEvidences_Platform" ON "MarketingReviewEvidences" ("Platform");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE INDEX "IX_MarketingReviewEvidences_TenantId" ON "MarketingReviewEvidences" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    CREATE UNIQUE INDEX "UQ_MarketingReviewEvidences_ServiceReview" ON "MarketingReviewEvidences" ("ServiceReviewId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220154537_AddAffiliateUsers_Retry') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260220154537_AddAffiliateUsers_Retry', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220160244_RemoveLegacyAffiliateUserFK') THEN
    ALTER TABLE "AffiliateAccounts" DROP CONSTRAINT "FK_AffiliateAccounts_Users_UserId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220160244_RemoveLegacyAffiliateUserFK') THEN
    DROP INDEX "IX_AffiliateAccounts_UserId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260220160244_RemoveLegacyAffiliateUserFK') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260220160244_RemoveLegacyAffiliateUserFK', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260223214810_AF16D_AddStorefrontBaseUrlToTenant') THEN
    DROP INDEX "IX_AffiliateCommissionLedger_OrderItemId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260223214810_AF16D_AddStorefrontBaseUrlToTenant') THEN
    ALTER TABLE "Tenants" ADD "StorefrontBaseUrl" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260223214810_AF16D_AddStorefrontBaseUrlToTenant') THEN
    ALTER TABLE "Orders" ADD "AffiliateAccountId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260223214810_AF16D_AddStorefrontBaseUrlToTenant') THEN
    ALTER TABLE "Orders" ADD "AffiliateReferralCode" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260223214810_AF16D_AddStorefrontBaseUrlToTenant') THEN
    ALTER TABLE "Orders" ADD "IsGuestCheckout" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260223214810_AF16D_AddStorefrontBaseUrlToTenant') THEN
    ALTER TABLE "Orders" ADD "PromotionLinkId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260223214810_AF16D_AddStorefrontBaseUrlToTenant') THEN
    ALTER TABLE "AffiliateAttributions" ADD "PromotionLinkId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260223214810_AF16D_AddStorefrontBaseUrlToTenant') THEN
    ALTER TABLE "AffiliateAccounts" ADD "PayoutWalletNumber" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260223214810_AF16D_AddStorefrontBaseUrlToTenant') THEN
    ALTER TABLE "AffiliateAccounts" ADD "PayoutWalletType" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260223214810_AF16D_AddStorefrontBaseUrlToTenant') THEN
    ALTER TABLE "AffiliateAccounts" ADD "PayoutWalletUpdatedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260223214810_AF16D_AddStorefrontBaseUrlToTenant') THEN
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
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260223214810_AF16D_AddStorefrontBaseUrlToTenant') THEN
    CREATE INDEX "IX_AffiliateCommLedger_OrderItemEntryType" ON "AffiliateCommissionLedger" ("OrderItemId", "EntryType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260223214810_AF16D_AddStorefrontBaseUrlToTenant') THEN
    CREATE INDEX "IX_AffiliateAttributions_PromotionLinkId" ON "AffiliateAttributions" ("PromotionLinkId") WHERE "PromotionLinkId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260223214810_AF16D_AddStorefrontBaseUrlToTenant') THEN
    CREATE INDEX "IX_AffiliatePromotionLinks_AffiliateUserId" ON "AffiliatePromotionLinks" ("AffiliateUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260223214810_AF16D_AddStorefrontBaseUrlToTenant') THEN
    CREATE INDEX "IX_AffiliatePromotionLinks_Code_Store" ON "AffiliatePromotionLinks" ("ReferralCode", "TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260223214810_AF16D_AddStorefrontBaseUrlToTenant') THEN
    CREATE INDEX "IX_AffiliatePromotionLinks_ProductId" ON "AffiliatePromotionLinks" ("ProductId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260223214810_AF16D_AddStorefrontBaseUrlToTenant') THEN
    CREATE INDEX "IX_AffiliatePromotionLinks_TenantId" ON "AffiliatePromotionLinks" ("TenantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260223214810_AF16D_AddStorefrontBaseUrlToTenant') THEN
    CREATE UNIQUE INDEX "UQ_AffiliatePromotionLinks_ReferralCode" ON "AffiliatePromotionLinks" ("ReferralCode") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260223214810_AF16D_AddStorefrontBaseUrlToTenant') THEN
    CREATE UNIQUE INDEX "UQ_AffiliatePromotionLinks_User_Store_Product" ON "AffiliatePromotionLinks" ("AffiliateUserId", "TenantId", "ProductId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260223214810_AF16D_AddStorefrontBaseUrlToTenant') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260223214810_AF16D_AddStorefrontBaseUrlToTenant', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260224175620_AF19_GlobalMarketplaceAffiliate') THEN
    DROP INDEX "UQ_AffiliateAccounts_Tenant_ReferralCode";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260224175620_AF19_GlobalMarketplaceAffiliate') THEN
    DROP INDEX "UQ_AffiliateAccounts_Tenant_User";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260224175620_AF19_GlobalMarketplaceAffiliate') THEN
    ALTER TABLE "AffiliateCommissionLedger" ADD "StoreTenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260224175620_AF19_GlobalMarketplaceAffiliate') THEN
    CREATE UNIQUE INDEX "UQ_AffiliateAccounts_ReferralCode" ON "AffiliateAccounts" ("ReferralCode");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260224175620_AF19_GlobalMarketplaceAffiliate') THEN
    CREATE UNIQUE INDEX "UQ_AffiliateAccounts_User" ON "AffiliateAccounts" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260224175620_AF19_GlobalMarketplaceAffiliate') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260224175620_AF19_GlobalMarketplaceAffiliate', '9.0.1');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260225182859_AffiliateAuditIndexFixes') THEN
    DROP INDEX "UQ_AffiliateCommLedger_Idempotency";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260225182859_AffiliateAuditIndexFixes') THEN
    DROP INDEX "UQ_AffiliateAccounts_User";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260225182859_AffiliateAuditIndexFixes') THEN
    ALTER INDEX "UQ_AffiliateAccounts_ReferralCode" RENAME TO "UQ_AffiliateAccounts_ReferralCode_Global";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260225182859_AffiliateAuditIndexFixes') THEN
    ALTER TABLE "AffiliateCommissionLedger" ALTER COLUMN "TenantId" DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260225182859_AffiliateAuditIndexFixes') THEN
    CREATE UNIQUE INDEX "UQ_AffiliateCommLedger_Idempotency" ON "AffiliateCommissionLedger" ("IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260225182859_AffiliateAuditIndexFixes') THEN
    CREATE UNIQUE INDEX "UQ_AffiliateAccounts_Tenant_ReferralCode" ON "AffiliateAccounts" ("TenantId", "ReferralCode");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260225182859_AffiliateAuditIndexFixes') THEN
    CREATE UNIQUE INDEX "UQ_AffiliateAccounts_Tenant_User" ON "AffiliateAccounts" ("TenantId", "UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260225182859_AffiliateAuditIndexFixes') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260225182859_AffiliateAuditIndexFixes', '9.0.1');
    END IF;
END $EF$;
COMMIT;

