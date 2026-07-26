using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiUnitSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ConversionFactor",
                table: "StockMovements",
                type: "numeric",
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<Guid>(
                name: "UnitId",
                table: "StockMovements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BaseQuantity",
                table: "SalesInvoiceItems",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ConversionFactor",
                table: "SalesInvoiceItems",
                type: "numeric",
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<Guid>(
                name: "UnitId",
                table: "SalesInvoiceItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BaseQuantity",
                table: "ReturnItems",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ConversionFactor",
                table: "ReturnItems",
                type: "numeric",
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<Guid>(
                name: "UnitId",
                table: "ReturnItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BaseQuantity",
                table: "PurchaseInvoiceItems",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ConversionFactor",
                table: "PurchaseInvoiceItems",
                type: "numeric",
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<Guid>(
                name: "UnitId",
                table: "PurchaseInvoiceItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Units",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Symbol = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Units", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductUnits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversionFactor = table.Column<decimal>(type: "numeric", nullable: false),
                    PurchasePrice = table.Column<decimal>(type: "numeric", nullable: true),
                    SellingPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    Barcode = table.Column<string>(type: "text", nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsBaseUnit = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductUnits_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductUnits_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_UnitId",
                table: "StockMovements",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceItems_UnitId",
                table: "SalesInvoiceItems",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnItems_UnitId",
                table: "ReturnItems",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceItems_UnitId",
                table: "PurchaseInvoiceItems",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductUnits_ProductId",
                table: "ProductUnits",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductUnits_UnitId",
                table: "ProductUnits",
                column: "UnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseInvoiceItems_Units_UnitId",
                table: "PurchaseInvoiceItems",
                column: "UnitId",
                principalTable: "Units",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ReturnItems_Units_UnitId",
                table: "ReturnItems",
                column: "UnitId",
                principalTable: "Units",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceItems_Units_UnitId",
                table: "SalesInvoiceItems",
                column: "UnitId",
                principalTable: "Units",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_Units_UnitId",
                table: "StockMovements",
                column: "UnitId",
                principalTable: "Units",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseInvoiceItems_Units_UnitId",
                table: "PurchaseInvoiceItems");

            migrationBuilder.DropForeignKey(
                name: "FK_ReturnItems_Units_UnitId",
                table: "ReturnItems");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceItems_Units_UnitId",
                table: "SalesInvoiceItems");

            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_Units_UnitId",
                table: "StockMovements");

            migrationBuilder.DropTable(
                name: "ProductUnits");

            migrationBuilder.DropTable(
                name: "Units");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_UnitId",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceItems_UnitId",
                table: "SalesInvoiceItems");

            migrationBuilder.DropIndex(
                name: "IX_ReturnItems_UnitId",
                table: "ReturnItems");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoiceItems_UnitId",
                table: "PurchaseInvoiceItems");

            migrationBuilder.DropColumn(
                name: "ConversionFactor",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "UnitId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "BaseQuantity",
                table: "SalesInvoiceItems");

            migrationBuilder.DropColumn(
                name: "ConversionFactor",
                table: "SalesInvoiceItems");

            migrationBuilder.DropColumn(
                name: "UnitId",
                table: "SalesInvoiceItems");

            migrationBuilder.DropColumn(
                name: "BaseQuantity",
                table: "ReturnItems");

            migrationBuilder.DropColumn(
                name: "ConversionFactor",
                table: "ReturnItems");

            migrationBuilder.DropColumn(
                name: "UnitId",
                table: "ReturnItems");

            migrationBuilder.DropColumn(
                name: "BaseQuantity",
                table: "PurchaseInvoiceItems");

            migrationBuilder.DropColumn(
                name: "ConversionFactor",
                table: "PurchaseInvoiceItems");

            migrationBuilder.DropColumn(
                name: "UnitId",
                table: "PurchaseInvoiceItems");
        }
    }
}
