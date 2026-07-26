using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCommerceHubGlobalCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PaidAt",
                table: "CommerceHubSubOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentReferenceId",
                table: "CommerceHubSubOrders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentStatus",
                table: "CommerceHubSubOrders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "CommerceHubMarketplaceOrders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidAt",
                table: "CommerceHubMarketplaceOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentReferenceId",
                table: "CommerceHubMarketplaceOrders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttributesJson",
                table: "CommerceHubListings",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommerceHubCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    IconEmoji = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommerceHubCategories_CommerceHubCategories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "CommerceHubCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubCategoryAttributes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    OptionsJson = table.Column<string>(type: "text", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubCategoryAttributes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommerceHubCategoryAttributes_CommerceHubCategories_Categor~",
                        column: x => x.CategoryId,
                        principalTable: "CommerceHubCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "CommerceHubCategories",
                columns: new[] { "Id", "CreatedAt", "IconEmoji", "IsActive", "Level", "Name", "NameAr", "ParentId", "Slug", "SortOrder", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-0000-0000-000000000001"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "📱", true, 0, "Electronics", "إلكترونيات", null, "electronics", 1, null },
                    { new Guid("10000000-0000-0000-0000-000000000002"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "👗", true, 0, "Fashion", "أزياء", null, "fashion", 2, null },
                    { new Guid("10000000-0000-0000-0000-000000000003"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "🏠", true, 0, "Home & Garden", "المنزل والحديقة", null, "home-garden", 3, null },
                    { new Guid("10000000-0000-0000-0000-000000000011"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "📺", true, 1, "TVs", "تلفزيونات", new Guid("10000000-0000-0000-0000-000000000001"), "tvs", 1, null },
                    { new Guid("10000000-0000-0000-0000-000000000012"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "📱", true, 1, "Mobile Phones", "هواتف محمولة", new Guid("10000000-0000-0000-0000-000000000001"), "mobile-phones", 2, null },
                    { new Guid("10000000-0000-0000-0000-000000000013"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "💻", true, 1, "Laptops", "لابتوب", new Guid("10000000-0000-0000-0000-000000000001"), "laptops", 3, null },
                    { new Guid("10000000-0000-0000-0000-000000000021"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "👔", true, 1, "Men", "رجالي", new Guid("10000000-0000-0000-0000-000000000002"), "mens-fashion", 1, null },
                    { new Guid("10000000-0000-0000-0000-000000000022"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "👗", true, 1, "Women", "نسائي", new Guid("10000000-0000-0000-0000-000000000002"), "womens-fashion", 2, null },
                    { new Guid("10000000-0000-0000-0000-000000000031"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "🛋️", true, 1, "Furniture", "أثاث", new Guid("10000000-0000-0000-0000-000000000003"), "furniture", 1, null },
                    { new Guid("10000000-0000-0000-0000-000000000032"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "🍳", true, 1, "Kitchen", "مطبخ", new Guid("10000000-0000-0000-0000-000000000003"), "kitchen", 2, null }
                });

            migrationBuilder.InsertData(
                table: "CommerceHubCategoryAttributes",
                columns: new[] { "Id", "CategoryId", "IsRequired", "Name", "NameAr", "OptionsJson", "SortOrder", "Type" },
                values: new object[,]
                {
                    { new Guid("20000000-0000-0000-0000-000000000001"), new Guid("10000000-0000-0000-0000-000000000011"), true, "Brand", "الماركة", "[\"Samsung\",\"LG\",\"Sony\",\"TCL\",\"Hisense\",\"Toshiba\",\"Panasonic\"]", 1, "select" },
                    { new Guid("20000000-0000-0000-0000-000000000002"), new Guid("10000000-0000-0000-0000-000000000011"), true, "Screen Size (inches)", "حجم الشاشة (بوصة)", null, 2, "number" },
                    { new Guid("20000000-0000-0000-0000-000000000003"), new Guid("10000000-0000-0000-0000-000000000011"), false, "Resolution", "الدقة", "[\"HD\",\"Full HD\",\"4K UHD\",\"8K\"]", 3, "select" },
                    { new Guid("20000000-0000-0000-0000-000000000011"), new Guid("10000000-0000-0000-0000-000000000012"), true, "Brand", "الماركة", "[\"Apple\",\"Samsung\",\"Xiaomi\",\"Huawei\",\"Oppo\",\"Vivo\",\"Realme\",\"OnePlus\"]", 1, "select" },
                    { new Guid("20000000-0000-0000-0000-000000000012"), new Guid("10000000-0000-0000-0000-000000000012"), true, "Storage", "السعة التخزينية", "[\"32GB\",\"64GB\",\"128GB\",\"256GB\",\"512GB\",\"1TB\"]", 2, "select" },
                    { new Guid("20000000-0000-0000-0000-000000000013"), new Guid("10000000-0000-0000-0000-000000000012"), false, "RAM", "الذاكرة العشوائية", "[\"2GB\",\"3GB\",\"4GB\",\"6GB\",\"8GB\",\"12GB\",\"16GB\"]", 3, "select" },
                    { new Guid("20000000-0000-0000-0000-000000000021"), new Guid("10000000-0000-0000-0000-000000000013"), true, "Brand", "الماركة", "[\"Apple\",\"Dell\",\"HP\",\"Lenovo\",\"ASUS\",\"Acer\",\"MSI\"]", 1, "select" },
                    { new Guid("20000000-0000-0000-0000-000000000022"), new Guid("10000000-0000-0000-0000-000000000013"), false, "Processor", "المعالج", "[\"Intel Core i3\",\"Intel Core i5\",\"Intel Core i7\",\"Intel Core i9\",\"AMD Ryzen 5\",\"AMD Ryzen 7\",\"Apple M1\",\"Apple M2\",\"Apple M3\"]", 2, "select" },
                    { new Guid("20000000-0000-0000-0000-000000000023"), new Guid("10000000-0000-0000-0000-000000000013"), false, "RAM", "الذاكرة العشوائية", "[\"4GB\",\"8GB\",\"16GB\",\"32GB\",\"64GB\"]", 3, "select" },
                    { new Guid("20000000-0000-0000-0000-000000000031"), new Guid("10000000-0000-0000-0000-000000000021"), true, "Size", "المقاس", "[\"XS\",\"S\",\"M\",\"L\",\"XL\",\"XXL\",\"3XL\"]", 1, "select" },
                    { new Guid("20000000-0000-0000-0000-000000000032"), new Guid("10000000-0000-0000-0000-000000000021"), false, "Color", "اللون", null, 2, "text" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubMarketplaceOrders_IdempotencyKey",
                table: "CommerceHubMarketplaceOrders",
                column: "IdempotencyKey",
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubCategories_ParentId_SortOrder",
                table: "CommerceHubCategories",
                columns: new[] { "ParentId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "UQ_CommerceHubCategories_Slug",
                table: "CommerceHubCategories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubCategoryAttributes_CategoryId_SortOrder",
                table: "CommerceHubCategoryAttributes",
                columns: new[] { "CategoryId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommerceHubCategoryAttributes");

            migrationBuilder.DropTable(
                name: "CommerceHubCategories");

            migrationBuilder.DropIndex(
                name: "IX_CommerceHubMarketplaceOrders_IdempotencyKey",
                table: "CommerceHubMarketplaceOrders");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "CommerceHubSubOrders");

            migrationBuilder.DropColumn(
                name: "PaymentReferenceId",
                table: "CommerceHubSubOrders");

            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                table: "CommerceHubSubOrders");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "CommerceHubMarketplaceOrders");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "CommerceHubMarketplaceOrders");

            migrationBuilder.DropColumn(
                name: "PaymentReferenceId",
                table: "CommerceHubMarketplaceOrders");

            migrationBuilder.DropColumn(
                name: "AttributesJson",
                table: "CommerceHubListings");
        }
    }
}
