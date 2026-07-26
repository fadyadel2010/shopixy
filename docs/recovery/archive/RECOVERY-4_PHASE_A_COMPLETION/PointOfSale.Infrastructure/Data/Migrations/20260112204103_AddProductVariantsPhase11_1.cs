using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductVariantsPhase11_1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductVariants_ProductId",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "AdditionalPrice",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "ProductVariants");

            migrationBuilder.AlterColumn<string>(
                name: "SKU",
                table: "ProductVariants",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Barcode",
                table: "ProductVariants",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CostPrice",
                table: "ProductVariants",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "ProductVariants",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "ProductVariants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "OnlinePrice",
                table: "ProductVariants",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "ProductVariants",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "ProductVariants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ProductVariants",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "ProductVariantAttributes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariantAttributes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductVariantAttributes_ProductVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductVariantStocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReservedQuantity = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariantStocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductVariantStocks_ProductVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductVariantStocks_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_Barcode",
                table: "ProductVariants",
                column: "Barcode",
                filter: "\"IsDeleted\" = false AND \"Barcode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_ProductId_IsDefault",
                table: "ProductVariants",
                columns: new[] { "ProductId", "IsDefault" },
                filter: "\"IsDefault\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_SKU",
                table: "ProductVariants",
                column: "SKU",
                filter: "\"IsDeleted\" = false AND \"SKU\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_TenantId_ProductId",
                table: "ProductVariants",
                columns: new[] { "TenantId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantAttributes_VariantId",
                table: "ProductVariantAttributes",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantAttributes_VariantId_Name",
                table: "ProductVariantAttributes",
                columns: new[] { "VariantId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantStocks_TenantId_VariantId_WarehouseId",
                table: "ProductVariantStocks",
                columns: new[] { "TenantId", "VariantId", "WarehouseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantStocks_VariantId",
                table: "ProductVariantStocks",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantStocks_WarehouseId",
                table: "ProductVariantStocks",
                column: "WarehouseId");

            // =============================================================================
            // PHASE 11.1 DATA MIGRATION
            // 1. Update existing ProductVariants to have TenantId from parent Product
            // 2. Set IsDefault = true for existing variants (if any)
            // 3. Copy pricing from Product to Variant
            // =============================================================================
            migrationBuilder.Sql(@"
                -- Update existing ProductVariants with TenantId from parent Product
                UPDATE ""ProductVariants"" pv
                SET ""TenantId"" = p.""TenantId"",
                    ""IsDefault"" = true,
                    ""Price"" = p.""SalePrice"",
                    ""OnlinePrice"" = p.""OnlinePrice"",
                    ""CostPrice"" = p.""PurchasePrice""
                FROM ""Products"" p
                WHERE pv.""ProductId"" = p.""Id"";
            ");

            // =============================================================================
            // Create default variants for products that don't have any
            // =============================================================================
            migrationBuilder.Sql(@"
                INSERT INTO ""ProductVariants"" (
                    ""Id"",
                    ""TenantId"",
                    ""ProductId"",
                    ""SKU"",
                    ""Barcode"",
                    ""Price"",
                    ""OnlinePrice"",
                    ""CostPrice"",
                    ""IsDefault"",
                    ""IsActive"",
                    ""SortOrder"",
                    ""CreatedAt"",
                    ""IsDeleted""
                )
                SELECT 
                    gen_random_uuid() AS ""Id"",
                    p.""TenantId"",
                    p.""Id"" AS ""ProductId"",
                    COALESCE(p.""SKU"", p.""Barcode"") AS ""SKU"",
                    p.""Barcode"",
                    p.""SalePrice"" AS ""Price"",
                    p.""OnlinePrice"",
                    p.""PurchasePrice"" AS ""CostPrice"",
                    true AS ""IsDefault"",
                    p.""IsActive"",
                    0 AS ""SortOrder"",
                    CURRENT_TIMESTAMP AS ""CreatedAt"",
                    p.""IsDeleted""
                FROM ""Products"" p
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""ProductVariants"" pv WHERE pv.""ProductId"" = p.""Id""
                );
            ");

            // =============================================================================
            // Migrate WarehouseStocks to ProductVariantStocks
            // =============================================================================
            migrationBuilder.Sql(@"
                INSERT INTO ""ProductVariantStocks"" (
                    ""Id"",
                    ""TenantId"",
                    ""VariantId"",
                    ""WarehouseId"",
                    ""Quantity"",
                    ""ReservedQuantity"",
                    ""CreatedAt"",
                    ""IsDeleted""
                )
                SELECT 
                    gen_random_uuid() AS ""Id"",
                    ws.""TenantId"",
                    pv.""Id"" AS ""VariantId"",
                    ws.""WarehouseId"",
                    ws.""Quantity"",
                    ws.""ReservedQuantity"",
                    CURRENT_TIMESTAMP AS ""CreatedAt"",
                    ws.""IsDeleted""
                FROM ""WarehouseStocks"" ws
                INNER JOIN ""ProductVariants"" pv ON pv.""ProductId"" = ws.""ProductId"" AND pv.""IsDefault"" = true
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""ProductVariantStocks"" pvs 
                    WHERE pvs.""VariantId"" = pv.""Id"" AND pvs.""WarehouseId"" = ws.""WarehouseId""
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductVariantAttributes");

            migrationBuilder.DropTable(
                name: "ProductVariantStocks");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariants_Barcode",
                table: "ProductVariants");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariants_ProductId_IsDefault",
                table: "ProductVariants");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariants_SKU",
                table: "ProductVariants");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariants_TenantId_ProductId",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "CostPrice",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "OnlinePrice",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "Price",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ProductVariants");

            migrationBuilder.AlterColumn<string>(
                name: "SKU",
                table: "ProductVariants",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Barcode",
                table: "ProductVariants",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AdditionalPrice",
                table: "ProductVariants",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "ProductVariants",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_ProductId",
                table: "ProductVariants",
                column: "ProductId");
        }
    }
}
