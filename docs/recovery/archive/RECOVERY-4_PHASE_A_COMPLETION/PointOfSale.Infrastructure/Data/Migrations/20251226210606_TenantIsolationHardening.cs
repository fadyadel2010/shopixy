using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PointOfSale.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class TenantIsolationHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Banks_BankName",
                table: "Banks");

            migrationBuilder.DeleteData(
                table: "FinancialAccounts",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.DeleteData(
                table: "ShippingMethods",
                keyColumn: "Id",
                keyValue: new Guid("11111111-2222-3333-4444-555555555551"));

            migrationBuilder.DeleteData(
                table: "ShippingMethods",
                keyColumn: "Id",
                keyValue: new Guid("11111111-2222-3333-4444-555555555552"));

            migrationBuilder.DeleteData(
                table: "ShippingMethods",
                keyColumn: "Id",
                keyValue: new Guid("11111111-2222-3333-4444-555555555553"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "WarehouseStocks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "Warehouses",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "VisaTransactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "Users",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Units",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "TreasuryTransactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "SupplierTransactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Suppliers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "StockTransfers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "StockMovements",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ShippingMethods",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "SalesRepresentatives",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "SalesInvoices",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Returns",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "PurchaseInvoices",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "Products",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "FinancialTransactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "FinancialAccounts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Expenses",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ExpenseCategories",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "CustomerTransactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "Customers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Coupons",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "Categories",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "CashboxTransactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Cashboxes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "BankTransactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Banks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "AccountLedgers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Banks_TenantId_BankName",
                table: "Banks",
                columns: new[] { "TenantId", "BankName" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Banks_TenantId_BankName",
                table: "Banks");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "WarehouseStocks");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "VisaTransactions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "TreasuryTransactions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SupplierTransactions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ShippingMethods");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SalesRepresentatives");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Returns");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PurchaseInvoices");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "FinancialTransactions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "FinancialAccounts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ExpenseCategories");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "CustomerTransactions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Coupons");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "CashboxTransactions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Cashboxes");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "BankTransactions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Banks");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AccountLedgers");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "Warehouses",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "Users",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "Products",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "Customers",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "Categories",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.InsertData(
                table: "FinancialAccounts",
                columns: new[] { "Id", "AccountCode", "AccountNumber", "AccountType", "Balance", "CreatedAt", "CreatedBy", "IsDeleted", "IsSystem", "Level", "Name", "ParentAccountId", "UpdatedAt", "UpdatedBy" },
                values: new object[] { new Guid("11111111-1111-1111-1111-111111111111"), "1200", "VISA-001", 1, 0m, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, true, 1, "Visa Account", null, null, null });

            migrationBuilder.InsertData(
                table: "ShippingMethods",
                columns: new[] { "Id", "AvailableCountries", "BaseRate", "CarrierName", "CreatedAt", "CreatedBy", "Description", "EstimatedDeliveryDaysMax", "EstimatedDeliveryDaysMin", "FreeShippingThreshold", "IsActive", "IsDeleted", "Name", "NameAr", "PerKgRate", "SortOrder", "TrackingUrlTemplate", "UpdatedAt", "UpdatedBy" },
                values: new object[,]
                {
                    { new Guid("11111111-2222-3333-4444-555555555551"), null, 10.00m, null, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Standard delivery within 5-7 business days", 7, 5, null, true, false, "Standard Shipping", "شحن عادي", 0m, 1, null, null, null },
                    { new Guid("11111111-2222-3333-4444-555555555552"), null, 25.00m, null, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Express delivery within 2-3 business days", 3, 2, null, true, false, "Express Shipping", "شحن سريع", 2.00m, 2, null, null, null },
                    { new Guid("11111111-2222-3333-4444-555555555553"), null, 0m, null, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Free shipping for orders over $100", 14, 7, 100.00m, true, false, "Free Shipping", "شحن مجاني", 0m, 3, null, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Banks_BankName",
                table: "Banks",
                column: "BankName",
                unique: true,
                filter: "[IsDeleted] = 0");
        }
    }
}
