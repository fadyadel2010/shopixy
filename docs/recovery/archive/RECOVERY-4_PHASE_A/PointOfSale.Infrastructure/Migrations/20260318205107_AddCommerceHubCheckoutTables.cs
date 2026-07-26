using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCommerceHubCheckoutTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MediaId",
                table: "ProductVariants",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductTemplate",
                table: "Products",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "Orders",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SessionId",
                table: "Orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeviceType",
                table: "CheckoutSessions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstActivityAtUtc",
                table: "CheckoutSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryToken",
                table: "CheckoutSessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "CheckoutSessions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommerceHubListings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    CommerceHubCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommerceHubPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubListings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommerceHubListings_ProductVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommerceHubListings_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubMarketplaceOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "text", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AddressLine = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Area = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    PaymentMethodCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PaymentMethodName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PaymentReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PaymentGateway = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ShippingTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PaymentStatus = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubMarketplaceOrders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GlobalPhoneReputation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SpamReports = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    FirstReportedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastReportedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalPhoneReputation", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PhoneRiskProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SpamReports = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    IsBlocked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    BlockedReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BlockedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    BlockedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhoneRiskProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PhoneSpamReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhoneSpamReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductMedia",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductMedia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductMedia_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SmartOffers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    TriggerType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PagesScope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DelaySeconds = table.Column<int>(type: "integer", nullable: true),
                    ScrollPercent = table.Column<int>(type: "integer", nullable: true),
                    ShowOncePerSession = table.Column<bool>(type: "boolean", nullable: false),
                    MaxDisplaysPerUser = table.Column<int>(type: "integer", nullable: true),
                    PopupTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PopupDescription = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ButtonText = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CouponCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    GeneratedCouponId = table.Column<Guid>(type: "uuid", nullable: true),
                    CouponDiscountType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CouponDiscountValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CouponMinOrderAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CouponMaxDiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ImageUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    BackgroundColor = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartOffers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubSubOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketplaceOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "text", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Shipping = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubSubOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommerceHubSubOrders_CommerceHubMarketplaceOrders_Marketpla~",
                        column: x => x.MarketplaceOrderId,
                        principalTable: "CommerceHubMarketplaceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubSubOrderItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductName = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    VariantName = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    VariantAttributesJson = table.Column<string>(type: "text", nullable: true),
                    ImageUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubSubOrderItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommerceHubSubOrderItems_CommerceHubSubOrders_SubOrderId",
                        column: x => x.SubOrderId,
                        principalTable: "CommerceHubSubOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_MediaId",
                table: "ProductVariants",
                column: "MediaId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_SessionId",
                table: "Orders",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_GuestPhone",
                table: "Orders",
                columns: new[] { "TenantId", "GuestPhone" });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_RecoveryToken",
                table: "CheckoutSessions",
                column: "RecoveryToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubListings_ProductId",
                table: "CommerceHubListings",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubListings_TenantId_IsPublished_CategoryId",
                table: "CommerceHubListings",
                columns: new[] { "TenantId", "IsPublished", "CommerceHubCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubListings_VariantId",
                table: "CommerceHubListings",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "UQ_CommerceHubListings_TenantId_VariantId",
                table: "CommerceHubListings",
                columns: new[] { "TenantId", "VariantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubMarketplaceOrders_OrderNumber",
                table: "CommerceHubMarketplaceOrders",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubSubOrderItems_ListingId",
                table: "CommerceHubSubOrderItems",
                column: "ListingId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubSubOrderItems_SubOrderId",
                table: "CommerceHubSubOrderItems",
                column: "SubOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubSubOrderItems_VariantId",
                table: "CommerceHubSubOrderItems",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubSubOrders_MarketplaceOrderId_StoreId",
                table: "CommerceHubSubOrders",
                columns: new[] { "MarketplaceOrderId", "StoreId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubSubOrders_OrderNumber",
                table: "CommerceHubSubOrders",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GlobalPhoneReputation_PhoneNumber",
                table: "GlobalPhoneReputation",
                column: "PhoneNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhoneRiskProfiles_TenantId_IsBlocked_BlockedAtUtc",
                table: "PhoneRiskProfiles",
                columns: new[] { "TenantId", "IsBlocked", "BlockedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PhoneRiskProfiles_TenantId_PhoneNumber",
                table: "PhoneRiskProfiles",
                columns: new[] { "TenantId", "PhoneNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhoneSpamReports_PhoneNumber",
                table: "PhoneSpamReports",
                column: "PhoneNumber");

            migrationBuilder.CreateIndex(
                name: "IX_PhoneSpamReports_TenantId_OrderId",
                table: "PhoneSpamReports",
                columns: new[] { "TenantId", "OrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductMedia_ProductId_Position",
                table: "ProductMedia",
                columns: new[] { "ProductId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_SmartOffers_TenantId_Name",
                table: "SmartOffers",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SmartOffers_TenantId_Status_Priority",
                table: "SmartOffers",
                columns: new[] { "TenantId", "Status", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_SmartOffers_TenantId_TriggerType_PagesScope",
                table: "SmartOffers",
                columns: new[] { "TenantId", "TriggerType", "PagesScope" });

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariants_ProductMedia_MediaId",
                table: "ProductVariants",
                column: "MediaId",
                principalTable: "ProductMedia",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariants_ProductMedia_MediaId",
                table: "ProductVariants");

            migrationBuilder.DropTable(
                name: "CommerceHubListings");

            migrationBuilder.DropTable(
                name: "CommerceHubSubOrderItems");

            migrationBuilder.DropTable(
                name: "GlobalPhoneReputation");

            migrationBuilder.DropTable(
                name: "PhoneRiskProfiles");

            migrationBuilder.DropTable(
                name: "PhoneSpamReports");

            migrationBuilder.DropTable(
                name: "ProductMedia");

            migrationBuilder.DropTable(
                name: "SmartOffers");

            migrationBuilder.DropTable(
                name: "CommerceHubSubOrders");

            migrationBuilder.DropTable(
                name: "CommerceHubMarketplaceOrders");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariants_MediaId",
                table: "ProductVariants");

            migrationBuilder.DropIndex(
                name: "IX_Orders_SessionId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_TenantId_GuestPhone",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_CheckoutSessions_RecoveryToken",
                table: "CheckoutSessions");

            migrationBuilder.DropColumn(
                name: "MediaId",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "ProductTemplate",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DeviceType",
                table: "CheckoutSessions");

            migrationBuilder.DropColumn(
                name: "FirstActivityAtUtc",
                table: "CheckoutSessions");

            migrationBuilder.DropColumn(
                name: "RecoveryToken",
                table: "CheckoutSessions");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "CheckoutSessions");
        }
    }
}
