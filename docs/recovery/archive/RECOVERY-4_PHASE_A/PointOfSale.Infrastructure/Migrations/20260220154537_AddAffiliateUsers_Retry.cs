using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAffiliateUsers_Retry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MarketingReviewCount",
                table: "ProviderTrustAggregates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "MarketingWeightedScore",
                table: "ProviderTrustAggregates",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "PosVerifiedCount",
                table: "ProviderTrustAggregates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalVerifiedOrders",
                table: "ProviderTrustAggregates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalVerifiedRevenue",
                table: "ProviderTrustAggregates",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "AffiliateAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferralCode = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SuspendedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SuspensionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliateAccounts", x => x.Id);
                    table.CheckConstraint("CK_AffiliateAccounts_Status", "\"Status\" IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_AffiliateAccounts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AffiliateProductPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommissionRate = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    MaxCommissionPerItem = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliateProductPolicies", x => x.Id);
                    table.CheckConstraint("CK_AffiliateProductPolicies_MaxCap", "\"MaxCommissionPerItem\" IS NULL OR \"MaxCommissionPerItem\" > 0");
                    table.CheckConstraint("CK_AffiliateProductPolicies_Rate", "\"CommissionRate\" >= 0.0001 AND \"CommissionRate\" <= 0.50");
                    table.ForeignKey(
                        name: "FK_AffiliateProductPolicies_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MarketingReviewEvidences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Platform = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StartDate = table.Column<DateTime>(type: "date", nullable: false),
                    EndDate = table.Column<DateTime>(type: "date", nullable: false),
                    OrdersCount = table.Column<int>(type: "integer", nullable: true),
                    RevenueAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    EvidenceType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketingReviewEvidences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketingReviewEvidences_ServiceReviews_ServiceReviewId",
                        column: x => x.ServiceReviewId,
                        principalTable: "ServiceReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AffiliateAttributions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    AffiliateAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferralCode = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    AttributedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliateAttributions", x => x.Id);
                    table.CheckConstraint("CK_AffiliateAttributions_Source", "\"Source\" IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_AffiliateAttributions_AffiliateAccounts_AffiliateAccountId",
                        column: x => x.AffiliateAccountId,
                        principalTable: "AffiliateAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AffiliateAttributions_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AffiliateUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AffiliateAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmailVerified = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    RefreshToken = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RefreshTokenExpiryTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliateUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AffiliateUsers_AffiliateAccounts_AffiliateAccountId",
                        column: x => x.AffiliateAccountId,
                        principalTable: "AffiliateAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AffiliateCommissionLedger",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    AttributionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AffiliateAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CommissionRate = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false),
                    CommissionAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    EntryType = table.Column<int>(type: "integer", nullable: false),
                    OriginalEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliateCommissionLedger", x => x.Id);
                    table.CheckConstraint("CK_AffiliateCommLedger_Amount", "\"CommissionAmount\" > 0");
                    table.CheckConstraint("CK_AffiliateCommLedger_BaseAmount", "\"BaseAmount\" > 0");
                    table.CheckConstraint("CK_AffiliateCommLedger_EntryType", "\"EntryType\" IN (0, 1, 2, 3)");
                    table.CheckConstraint("CK_AffiliateCommLedger_Rate", "\"CommissionRate\" > 0 AND \"CommissionRate\" <= 0.50");
                    table.CheckConstraint("CK_AffiliateCommLedger_ReversalRef", "(\"EntryType\" IN (1, 2, 3) AND \"OriginalEntryId\" IS NOT NULL) OR (\"EntryType\" = 0 AND \"OriginalEntryId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_AffiliateCommissionLedger_AffiliateAccounts_AffiliateAccoun~",
                        column: x => x.AffiliateAccountId,
                        principalTable: "AffiliateAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AffiliateCommissionLedger_AffiliateAttributions_Attribution~",
                        column: x => x.AttributionId,
                        principalTable: "AffiliateAttributions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AffiliateCommissionLedger_AffiliateCommissionLedger_Origina~",
                        column: x => x.OriginalEntryId,
                        principalTable: "AffiliateCommissionLedger",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AffiliateCommissionLedger_OrderItems_OrderItemId",
                        column: x => x.OrderItemId,
                        principalTable: "OrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AffiliateCommissionLedger_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateAccounts_TenantId",
                table: "AffiliateAccounts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateAccounts_UserId",
                table: "AffiliateAccounts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateAccounts_Tenant_ReferralCode",
                table: "AffiliateAccounts",
                columns: new[] { "TenantId", "ReferralCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateAccounts_Tenant_User",
                table: "AffiliateAccounts",
                columns: new[] { "TenantId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateAttributions_AffiliateAccountId",
                table: "AffiliateAttributions",
                column: "AffiliateAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateAttributions_OrderId",
                table: "AffiliateAttributions",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateAttributions_TenantId",
                table: "AffiliateAttributions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateAttributions_Tenant_Order",
                table: "AffiliateAttributions",
                columns: new[] { "TenantId", "OrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateCommissionLedger_AttributionId",
                table: "AffiliateCommissionLedger",
                column: "AttributionId");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateCommissionLedger_OrderItemId",
                table: "AffiliateCommissionLedger",
                column: "OrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateCommissionLedger_OriginalEntryId",
                table: "AffiliateCommissionLedger",
                column: "OriginalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateCommLedger_AffiliateAccountId",
                table: "AffiliateCommissionLedger",
                column: "AffiliateAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateCommLedger_OrderId",
                table: "AffiliateCommissionLedger",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateCommLedger_TenantId",
                table: "AffiliateCommissionLedger",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateCommLedger_TenantRecorded",
                table: "AffiliateCommissionLedger",
                columns: new[] { "TenantId", "RecordedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateCommLedger_Idempotency",
                table: "AffiliateCommissionLedger",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateProductPolicies_ProductId",
                table: "AffiliateProductPolicies",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateProductPolicies_TenantId",
                table: "AffiliateProductPolicies",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateProductPolicies_Tenant_Product",
                table: "AffiliateProductPolicies",
                columns: new[] { "TenantId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateUsers_TenantId",
                table: "AffiliateUsers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateUsers_AffiliateAccount",
                table: "AffiliateUsers",
                column: "AffiliateAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateUsers_Email_Tenant",
                table: "AffiliateUsers",
                columns: new[] { "TenantId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketingReviewEvidences_EvidenceType",
                table: "MarketingReviewEvidences",
                column: "EvidenceType");

            migrationBuilder.CreateIndex(
                name: "IX_MarketingReviewEvidences_Platform",
                table: "MarketingReviewEvidences",
                column: "Platform");

            migrationBuilder.CreateIndex(
                name: "IX_MarketingReviewEvidences_TenantId",
                table: "MarketingReviewEvidences",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "UQ_MarketingReviewEvidences_ServiceReview",
                table: "MarketingReviewEvidences",
                column: "ServiceReviewId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AffiliateCommissionLedger");

            migrationBuilder.DropTable(
                name: "AffiliateProductPolicies");

            migrationBuilder.DropTable(
                name: "AffiliateUsers");

            migrationBuilder.DropTable(
                name: "MarketingReviewEvidences");

            migrationBuilder.DropTable(
                name: "AffiliateAttributions");

            migrationBuilder.DropTable(
                name: "AffiliateAccounts");

            migrationBuilder.DropColumn(
                name: "MarketingReviewCount",
                table: "ProviderTrustAggregates");

            migrationBuilder.DropColumn(
                name: "MarketingWeightedScore",
                table: "ProviderTrustAggregates");

            migrationBuilder.DropColumn(
                name: "PosVerifiedCount",
                table: "ProviderTrustAggregates");

            migrationBuilder.DropColumn(
                name: "TotalVerifiedOrders",
                table: "ProviderTrustAggregates");

            migrationBuilder.DropColumn(
                name: "TotalVerifiedRevenue",
                table: "ProviderTrustAggregates");
        }
    }
}
