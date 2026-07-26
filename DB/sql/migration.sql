CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
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

CREATE INDEX "IX_Categories_ParentCategoryId" ON "Categories" ("ParentCategoryId");

CREATE INDEX "IX_Customers_Email" ON "Customers" ("Email");

CREATE INDEX "IX_Customers_Phone" ON "Customers" ("Phone");

CREATE INDEX "IX_Customers_TaxNumber" ON "Customers" ("TaxNumber");

CREATE INDEX "IX_CustomerTransactions_CustomerId" ON "CustomerTransactions" ("CustomerId");

CREATE INDEX "IX_Products_Barcode" ON "Products" ("Barcode");

CREATE INDEX "IX_Products_CategoryId" ON "Products" ("CategoryId");

CREATE INDEX "IX_Products_Name" ON "Products" ("Name");

CREATE INDEX "IX_Products_SKU" ON "Products" ("SKU");

CREATE INDEX "IX_ProductVariants_ProductId" ON "ProductVariants" ("ProductId");

CREATE INDEX "IX_PurchaseInvoiceItems_ProductId" ON "PurchaseInvoiceItems" ("ProductId");

CREATE INDEX "IX_PurchaseInvoiceItems_ProductVariantId" ON "PurchaseInvoiceItems" ("ProductVariantId");

CREATE INDEX "IX_PurchaseInvoiceItems_PurchaseInvoiceId" ON "PurchaseInvoiceItems" ("PurchaseInvoiceId");

CREATE INDEX "IX_PurchaseInvoicePayments_PurchaseInvoiceId" ON "PurchaseInvoicePayments" ("PurchaseInvoiceId");

CREATE INDEX "IX_PurchaseInvoices_SupplierId" ON "PurchaseInvoices" ("SupplierId");

CREATE INDEX "IX_PurchaseInvoices_WarehouseId" ON "PurchaseInvoices" ("WarehouseId");

CREATE INDEX "IX_ReturnItems_ProductId" ON "ReturnItems" ("ProductId");

CREATE INDEX "IX_ReturnItems_ProductVariantId" ON "ReturnItems" ("ProductVariantId");

CREATE INDEX "IX_ReturnItems_ReturnId" ON "ReturnItems" ("ReturnId");

CREATE INDEX "IX_Returns_CustomerId" ON "Returns" ("CustomerId");

CREATE INDEX "IX_Returns_SupplierId" ON "Returns" ("SupplierId");

CREATE INDEX "IX_Returns_WarehouseId" ON "Returns" ("WarehouseId");

CREATE INDEX "IX_SalesInvoiceItems_ProductId" ON "SalesInvoiceItems" ("ProductId");

CREATE INDEX "IX_SalesInvoiceItems_ProductVariantId" ON "SalesInvoiceItems" ("ProductVariantId");

CREATE INDEX "IX_SalesInvoiceItems_SalesInvoiceId" ON "SalesInvoiceItems" ("SalesInvoiceId");

CREATE INDEX "IX_SalesInvoicePayments_SalesInvoiceId" ON "SalesInvoicePayments" ("SalesInvoiceId");

CREATE INDEX "IX_SalesInvoices_CustomerId" ON "SalesInvoices" ("CustomerId");

CREATE INDEX "IX_SalesInvoices_WarehouseId" ON "SalesInvoices" ("WarehouseId");

CREATE INDEX "IX_StockMovements_CreatedAt" ON "StockMovements" ("CreatedAt");

CREATE INDEX "IX_StockMovements_ProductId" ON "StockMovements" ("ProductId");

CREATE INDEX "IX_StockMovements_ProductVariantId" ON "StockMovements" ("ProductVariantId");

CREATE INDEX "IX_StockMovements_ToWarehouseId" ON "StockMovements" ("ToWarehouseId");

CREATE INDEX "IX_StockMovements_WarehouseId" ON "StockMovements" ("WarehouseId");

CREATE INDEX "IX_SupplierTransactions_SupplierId" ON "SupplierTransactions" ("SupplierId");

CREATE UNIQUE INDEX "IX_Users_Email" ON "Users" ("Email");

CREATE UNIQUE INDEX "IX_Users_Username" ON "Users" ("Username");

CREATE INDEX "IX_WarehouseStocks_ProductId" ON "WarehouseStocks" ("ProductId");

CREATE INDEX "IX_WarehouseStocks_ProductVariantId" ON "WarehouseStocks" ("ProductVariantId");

CREATE UNIQUE INDEX "IX_WarehouseStocks_WarehouseId_ProductId_ProductVariantId" ON "WarehouseStocks" ("WarehouseId", "ProductId", "ProductVariantId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251123190510_InitialCreate', '9.0.0');

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

CREATE INDEX "IX_Cashboxes_IsActive" ON "Cashboxes" ("IsActive");

CREATE INDEX "IX_Cashboxes_OpenedByUserId" ON "Cashboxes" ("OpenedByUserId");

CREATE INDEX "IX_Cashboxes_WarehouseId" ON "Cashboxes" ("WarehouseId");

CREATE INDEX "IX_Cashboxes_WarehouseId_IsActive" ON "Cashboxes" ("WarehouseId", "IsActive");

CREATE INDEX "IX_CashboxTransactions_CashboxId" ON "CashboxTransactions" ("CashboxId");

CREATE INDEX "IX_CashboxTransactions_CashboxId_TransactionDate" ON "CashboxTransactions" ("CashboxId", "TransactionDate");

CREATE INDEX "IX_CashboxTransactions_ReferenceId" ON "CashboxTransactions" ("ReferenceId");

CREATE INDEX "IX_CashboxTransactions_TransactionDate" ON "CashboxTransactions" ("TransactionDate");

CREATE INDEX "IX_CashboxTransactions_TransactionType" ON "CashboxTransactions" ("TransactionType");

CREATE INDEX "IX_CashboxTransactions_UserId" ON "CashboxTransactions" ("UserId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251126172001_AddCashboxEntities', '9.0.0');

ALTER TABLE "Cashboxes" ALTER COLUMN "WarehouseId" DROP NOT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251126175326_MakeCashboxWarehouseOptional', '9.0.0');

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

CREATE INDEX "IX_AccountLedgers_UserId" ON "AccountLedgers" ("UserId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251126213231_AddAccountLedgerAndSOA', '9.0.0');

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

CREATE INDEX "IX_StockTransferItems_ProductId" ON "StockTransferItems" ("ProductId");

CREATE INDEX "IX_StockTransferItems_ProductVariantId" ON "StockTransferItems" ("ProductVariantId");

CREATE INDEX "IX_StockTransferItems_StockTransferId" ON "StockTransferItems" ("StockTransferId");

CREATE INDEX "IX_StockTransfers_ApprovedById" ON "StockTransfers" ("ApprovedById");

CREATE INDEX "IX_StockTransfers_CompletedById" ON "StockTransfers" ("CompletedById");

CREATE INDEX "IX_StockTransfers_FromWarehouseId" ON "StockTransfers" ("FromWarehouseId");

CREATE INDEX "IX_StockTransfers_RequestedById" ON "StockTransfers" ("RequestedById");

CREATE INDEX "IX_StockTransfers_ToWarehouseId" ON "StockTransfers" ("ToWarehouseId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251126221658_AddStockTransfer', '9.0.0');

ALTER TABLE "SalesInvoices" ADD "SalesRepresentativeId" uuid;

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

CREATE INDEX "IX_SalesInvoices_SalesRepresentativeId" ON "SalesInvoices" ("SalesRepresentativeId");

ALTER TABLE "SalesInvoices" ADD CONSTRAINT "FK_SalesInvoices_SalesRepresentatives_SalesRepresentativeId" FOREIGN KEY ("SalesRepresentativeId") REFERENCES "SalesRepresentatives" ("Id");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251127185031_AddSalesRepresentatives', '9.0.0');

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

INSERT INTO "FinancialAccounts" ("Id", "AccountNumber", "Balance", "CreatedAt", "CreatedBy", "IsDeleted", "IsSystem", "Name", "ParentAccountId", "UpdatedAt", "UpdatedBy")
VALUES ('11111111-1111-1111-1111-111111111111', NULL, 0.0, TIMESTAMPTZ '2024-01-01T00:00:00Z', NULL, FALSE, TRUE, 'Visa Account', NULL, NULL, NULL);

CREATE INDEX "IX_FinancialAccounts_ParentAccountId" ON "FinancialAccounts" ("ParentAccountId");

CREATE INDEX "IX_FinancialTransactions_AccountId" ON "FinancialTransactions" ("AccountId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251128175638_AddFinancialAccounts', '9.0.0');

ALTER TABLE "CashboxTransactions" ADD "IsOpeningBalance" boolean NOT NULL DEFAULT FALSE;

ALTER TABLE "Cashboxes" ADD "OpeningBalanceAmount" numeric;

ALTER TABLE "Cashboxes" ADD "OpeningBalanceDate" timestamp with time zone;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251201201129_AddCashboxOpeningBalanceFields', '9.0.0');

ALTER TABLE "SupplierTransactions" ADD "IsOpeningBalance" boolean NOT NULL DEFAULT FALSE;

ALTER TABLE "Suppliers" ADD "BalanceType" text;

ALTER TABLE "Suppliers" ADD "OpeningBalanceAmount" numeric;

ALTER TABLE "Suppliers" ADD "OpeningBalanceDate" timestamp with time zone;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251202144522_AddSupplierOpeningBalanceFields', '9.0.0');

ALTER TABLE "StockMovements" ADD "IsOpeningBalance" boolean NOT NULL DEFAULT FALSE;

ALTER TABLE "StockMovements" ADD "Price" numeric;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251202152548_AddOpeningBalanceFieldsToStockMovement', '9.0.0');

COMMIT;

