using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockTransfers_TenantId_TransferNumber",
                table: "StockTransfers");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoicePayments_SalesInvoiceId",
                table: "SalesInvoicePayments");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceItems_SalesInvoiceId",
                table: "SalesInvoiceItems");

            migrationBuilder.DropIndex(
                name: "IX_Returns_TenantId_ReturnNumber",
                table: "Returns");

            // Preflight validation: Check for Supplier data that exceeds new varchar limits
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    v_count INTEGER;
                    v_error_msg TEXT := '';
                BEGIN
                    -- Check TaxNumber length (max 50)
                    SELECT COUNT(*) INTO v_count
                    FROM ""Suppliers""
                    WHERE ""TaxNumber"" IS NOT NULL AND LENGTH(""TaxNumber"") > 50;
                    
                    IF v_count > 0 THEN
                        v_error_msg := v_error_msg || format('Found %s supplier(s) with TaxNumber exceeding 50 characters. ', v_count);
                    END IF;
                    
                    -- Check Phone length (max 50)
                    SELECT COUNT(*) INTO v_count
                    FROM ""Suppliers""
                    WHERE ""Phone"" IS NOT NULL AND LENGTH(""Phone"") > 50;
                    
                    IF v_count > 0 THEN
                        v_error_msg := v_error_msg || format('Found %s supplier(s) with Phone exceeding 50 characters. ', v_count);
                    END IF;
                    
                    -- Check Name length (max 200)
                    SELECT COUNT(*) INTO v_count
                    FROM ""Suppliers""
                    WHERE ""Name"" IS NOT NULL AND LENGTH(""Name"") > 200;
                    
                    IF v_count > 0 THEN
                        v_error_msg := v_error_msg || format('Found %s supplier(s) with Name exceeding 200 characters. ', v_count);
                    END IF;
                    
                    -- Check Email length (max 200)
                    SELECT COUNT(*) INTO v_count
                    FROM ""Suppliers""
                    WHERE ""Email"" IS NOT NULL AND LENGTH(""Email"") > 200;
                    
                    IF v_count > 0 THEN
                        v_error_msg := v_error_msg || format('Found %s supplier(s) with Email exceeding 200 characters. ', v_count);
                    END IF;
                    
                    -- If any violations found, raise exception with details
                    IF v_error_msg <> '' THEN
                        RAISE EXCEPTION 'Migration blocked: % Please truncate or fix these values before applying this migration.', v_error_msg;
                    END IF;
                END $$;
            ");

            migrationBuilder.AlterColumn<string>(
                name: "TaxNumber",
                table: "Suppliers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Phone",
                table: "Suppliers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Suppliers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Suppliers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Balance",
                table: "Suppliers",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            // Add ContentHash column with legacy sentinel as default for existing rows
            // Format: 'LEGACY_' (7 chars) + 57 zeros = 64 characters total (SHA-256 hash length)
            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "CommerceHubVendorPaymentProofs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "LEGACY_000000000000000000000000000000000000000000000000000000000");

            // Backfill existing rows with the legacy sentinel (redundant but explicit)
            migrationBuilder.Sql(@"
                UPDATE ""CommerceHubVendorPaymentProofs""
                SET ""ContentHash"" = 'LEGACY_' || repeat('0', 57)
                WHERE ""ContentHash"" = 'LEGACY_000000000000000000000000000000000000000000000000000000000'
                   OR ""ContentHash"" = '';
            ");

            // Remove the default so new rows must provide a valid hash
            migrationBuilder.AlterColumn<string>(
                name: "ContentHash",
                table: "CommerceHubVendorPaymentProofs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldDefaultValue: "LEGACY_000000000000000000000000000000000000000000000000000000000");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseStocks_TenantId_Quantity",
                table: "WarehouseStocks",
                columns: new[] { "TenantId", "Quantity" });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseStocks_TenantId_WarehouseId_Quantity",
                table: "WarehouseStocks",
                columns: new[] { "TenantId", "WarehouseId", "Quantity" });

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_Email",
                table: "Suppliers",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_Phone",
                table: "Suppliers",
                column: "Phone");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_TaxNumber",
                table: "Suppliers",
                column: "TaxNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_TenantId",
                table: "Suppliers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_TenantId_TransferNumber",
                table: "StockTransfers",
                columns: new[] { "TenantId", "TransferNumber" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_TenantId_CreatedAt",
                table: "SalesInvoices",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_TenantId_CustomerId_InvoiceDate",
                table: "SalesInvoices",
                columns: new[] { "TenantId", "CustomerId", "InvoiceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_TenantId_InvoiceDate_Active",
                table: "SalesInvoices",
                columns: new[] { "TenantId", "InvoiceDate" },
                filter: "\"IsDeleted\" = false AND \"Status\" <> 2");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_TenantId_Status_InvoiceDate",
                table: "SalesInvoices",
                columns: new[] { "TenantId", "Status", "InvoiceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_TenantId_WarehouseId_InvoiceDate",
                table: "SalesInvoices",
                columns: new[] { "TenantId", "WarehouseId", "InvoiceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoicePayments_PaymentMethod_PaymentDate",
                table: "SalesInvoicePayments",
                columns: new[] { "PaymentMethod", "PaymentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoicePayments_SalesInvoiceId_PaymentDate",
                table: "SalesInvoicePayments",
                columns: new[] { "SalesInvoiceId", "PaymentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceItems_SalesInvoiceId_ProductId",
                table: "SalesInvoiceItems",
                columns: new[] { "SalesInvoiceId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceItems_SalesInvoiceId_ProductVariantId",
                table: "SalesInvoiceItems",
                columns: new[] { "SalesInvoiceId", "ProductVariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_Returns_TenantId_ReturnNumber",
                table: "Returns",
                columns: new[] { "TenantId", "ReturnNumber" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId",
                table: "Products",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId_CategoryId",
                table: "Products",
                columns: new[] { "TenantId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_PaymentStatus_CreatedAt",
                table: "Orders",
                columns: new[] { "TenantId", "PaymentStatus", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_Source_CreatedAt",
                table: "Orders",
                columns: new[] { "TenantId", "Source", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_Status_CreatedAt",
                table: "Orders",
                columns: new[] { "TenantId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId",
                table: "Customers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_TenantId_Status_AbandonedAtUtc",
                table: "CheckoutSessions",
                columns: new[] { "TenantId", "Status", "AbandonedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WarehouseStocks_TenantId_Quantity",
                table: "WarehouseStocks");

            migrationBuilder.DropIndex(
                name: "IX_WarehouseStocks_TenantId_WarehouseId_Quantity",
                table: "WarehouseStocks");

            migrationBuilder.DropIndex(
                name: "IX_Suppliers_Email",
                table: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_Suppliers_Phone",
                table: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_Suppliers_TaxNumber",
                table: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_Suppliers_TenantId",
                table: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_StockTransfers_TenantId_TransferNumber",
                table: "StockTransfers");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoices_TenantId_CreatedAt",
                table: "SalesInvoices");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoices_TenantId_CustomerId_InvoiceDate",
                table: "SalesInvoices");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoices_TenantId_InvoiceDate_Active",
                table: "SalesInvoices");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoices_TenantId_Status_InvoiceDate",
                table: "SalesInvoices");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoices_TenantId_WarehouseId_InvoiceDate",
                table: "SalesInvoices");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoicePayments_PaymentMethod_PaymentDate",
                table: "SalesInvoicePayments");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoicePayments_SalesInvoiceId_PaymentDate",
                table: "SalesInvoicePayments");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceItems_SalesInvoiceId_ProductId",
                table: "SalesInvoiceItems");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceItems_SalesInvoiceId_ProductVariantId",
                table: "SalesInvoiceItems");

            migrationBuilder.DropIndex(
                name: "IX_Returns_TenantId_ReturnNumber",
                table: "Returns");

            migrationBuilder.DropIndex(
                name: "IX_Products_TenantId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_TenantId_CategoryId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Orders_TenantId_PaymentStatus_CreatedAt",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_TenantId_Source_CreatedAt",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_TenantId_Status_CreatedAt",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Customers_TenantId",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_CheckoutSessions_TenantId_Status_AbandonedAtUtc",
                table: "CheckoutSessions");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "CommerceHubVendorPaymentProofs");

            migrationBuilder.AlterColumn<string>(
                name: "TaxNumber",
                table: "Suppliers",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Phone",
                table: "Suppliers",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Suppliers",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Suppliers",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Balance",
                table: "Suppliers",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_TenantId_TransferNumber",
                table: "StockTransfers",
                columns: new[] { "TenantId", "TransferNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoicePayments_SalesInvoiceId",
                table: "SalesInvoicePayments",
                column: "SalesInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceItems_SalesInvoiceId",
                table: "SalesInvoiceItems",
                column: "SalesInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Returns_TenantId_ReturnNumber",
                table: "Returns",
                columns: new[] { "TenantId", "ReturnNumber" },
                unique: true);
        }
    }
}
