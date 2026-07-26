using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCountryToCheckoutSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AbandonedCartRecoveryLog_AbandonedCarts_AbandonedCartId",
                table: "AbandonedCartRecoveryLog");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AbandonedCartRecoveryLog",
                table: "AbandonedCartRecoveryLog");

            migrationBuilder.RenameTable(
                name: "AbandonedCartRecoveryLog",
                newName: "AbandonedCartRecoveryLogs");

            migrationBuilder.RenameIndex(
                name: "IX_AbandonedCartRecoveryLog_AbandonedCartId",
                table: "AbandonedCartRecoveryLogs",
                newName: "IX_AbandonedCartRecoveryLogs_AbandonedCartId");

            migrationBuilder.AddColumn<decimal>(
                name: "Weight",
                table: "ProductVariants",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "InventoryCommitted",
                table: "PaymentIntents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "TokensValidAfterUtc",
                table: "OnlineCustomers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CurrentAccessTokenExpiresAtUtc",
                table: "OnlineCustomerRefreshTokens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrentAccessTokenJti",
                table: "OnlineCustomerRefreshTokens",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "CheckoutSessions",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_AbandonedCartRecoveryLogs",
                table: "AbandonedCartRecoveryLogs",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EventPayload = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    LastErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LastErrorAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RevokedCustomerAccessTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Jti = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevokedCustomerAccessTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RevokedStorefrontAccessTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OnlineCustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Jti = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevokedStorefrontAccessTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RevokedStorefrontAccessTokens_OnlineCustomers_OnlineCustome~",
                        column: x => x.OnlineCustomerId,
                        principalTable: "OnlineCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // Data cleanup: Sanitize SmartOffers before applying check constraints
            // Fix rows with whitespace-only CouponCode but non-null coupon fields
            migrationBuilder.Sql(@"
                UPDATE ""SmartOffers""
                SET ""CouponDiscountType"" = NULL,
                    ""CouponDiscountValue"" = NULL,
                    ""CouponMinOrderAmount"" = NULL,
                    ""CouponMaxDiscountAmount"" = NULL
                WHERE NULLIF(REGEXP_REPLACE(""CouponCode"", '\s', '', 'g'), '') IS NULL
                  AND (""CouponDiscountType"" IS NOT NULL 
                       OR ""CouponDiscountValue"" IS NOT NULL 
                       OR ""CouponMinOrderAmount"" IS NOT NULL 
                       OR ""CouponMaxDiscountAmount"" IS NOT NULL);
            ");

            // Fix rows with non-empty CouponCode but missing CouponDiscountValue
            migrationBuilder.Sql(@"
                UPDATE ""SmartOffers""
                SET ""CouponDiscountValue"" = 0
                WHERE NULLIF(REGEXP_REPLACE(""CouponCode"", '\s', '', 'g'), '') IS NOT NULL
                  AND ""CouponDiscountValue"" IS NULL;
            ");

            // Data cleanup: Deduplicate AbandonedCartRecoveryLogs before creating unique index
            // Keep the earliest record for each (TenantId, IdempotencyKey) pair
            migrationBuilder.Sql(@"
                DELETE FROM ""AbandonedCartRecoveryLogs""
                WHERE ""Id"" IN (
                    SELECT ""Id""
                    FROM (
                        SELECT ""Id"",
                               ROW_NUMBER() OVER (
                                   PARTITION BY ""TenantId"", ""IdempotencyKey""
                                   ORDER BY ""CreatedAtUtc"" ASC, ""Id"" ASC
                               ) AS rn
                        FROM ""AbandonedCartRecoveryLogs""
                        WHERE ""IdempotencyKey"" IS NOT NULL
                    ) sub
                    WHERE rn > 1
                );
            ");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SmartOffers_CouponCode_Presence",
                table: "SmartOffers",
                sql: "NULLIF(REGEXP_REPLACE(\"CouponCode\", '\\s', '', 'g'), '') IS NOT NULL OR (\"CouponDiscountType\" IS NULL AND \"CouponDiscountValue\" IS NULL AND \"CouponMinOrderAmount\" IS NULL AND \"CouponMaxDiscountAmount\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SmartOffers_CouponDiscountValue_Presence",
                table: "SmartOffers",
                sql: "NULLIF(REGEXP_REPLACE(\"CouponCode\", '\\s', '', 'g'), '') IS NULL OR \"CouponDiscountValue\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshToken_TenantId_CurrentAccessTokenJti",
                table: "OnlineCustomerRefreshTokens",
                columns: new[] { "TenantId", "CurrentAccessTokenJti" });

            migrationBuilder.CreateIndex(
                name: "IX_AbandonedCartRecoveryLogs_TenantId",
                table: "AbandonedCartRecoveryLogs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AbandonedCartRecoveryLogs_TenantId_IdempotencyKey_Unique",
                table: "AbandonedCartRecoveryLogs",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessedAtUtc",
                table: "OutboxMessages",
                column: "ProcessedAtUtc",
                filter: "\"ProcessedAtUtc\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessingStatus",
                table: "OutboxMessages",
                columns: new[] { "CreatedAtUtc", "RetryCount" },
                filter: "\"ProcessedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_TenantId_ProcessedAtUtc",
                table: "OutboxMessages",
                columns: new[] { "TenantId", "ProcessedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RevokedCustomerAccessTokens_CustomerId",
                table: "RevokedCustomerAccessTokens",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_RevokedCustomerAccessTokens_ExpiresAt",
                table: "RevokedCustomerAccessTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_RevokedCustomerAccessTokens_Jti",
                table: "RevokedCustomerAccessTokens",
                column: "Jti",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RevokedStorefrontAccessTokens_ExpiresAtUtc",
                table: "RevokedStorefrontAccessTokens",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RevokedStorefrontAccessTokens_OnlineCustomerId",
                table: "RevokedStorefrontAccessTokens",
                column: "OnlineCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_RevokedStorefrontAccessTokens_TenantId_Jti",
                table: "RevokedStorefrontAccessTokens",
                columns: new[] { "TenantId", "Jti" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RevokedStorefrontAccessTokens_TenantId_OnlineCustomerId",
                table: "RevokedStorefrontAccessTokens",
                columns: new[] { "TenantId", "OnlineCustomerId" });

            migrationBuilder.AddForeignKey(
                name: "FK_AbandonedCartRecoveryLogs_AbandonedCarts_AbandonedCartId",
                table: "AbandonedCartRecoveryLogs",
                column: "AbandonedCartId",
                principalTable: "AbandonedCarts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AbandonedCartRecoveryLogs_AbandonedCarts_AbandonedCartId",
                table: "AbandonedCartRecoveryLogs");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "RevokedCustomerAccessTokens");

            migrationBuilder.DropTable(
                name: "RevokedStorefrontAccessTokens");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SmartOffers_CouponCode_Presence",
                table: "SmartOffers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SmartOffers_CouponDiscountValue_Presence",
                table: "SmartOffers");

            migrationBuilder.DropIndex(
                name: "IX_RefreshToken_TenantId_CurrentAccessTokenJti",
                table: "OnlineCustomerRefreshTokens");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AbandonedCartRecoveryLogs",
                table: "AbandonedCartRecoveryLogs");

            migrationBuilder.DropIndex(
                name: "IX_AbandonedCartRecoveryLogs_TenantId",
                table: "AbandonedCartRecoveryLogs");

            migrationBuilder.DropIndex(
                name: "IX_AbandonedCartRecoveryLogs_TenantId_IdempotencyKey_Unique",
                table: "AbandonedCartRecoveryLogs");

            migrationBuilder.DropColumn(
                name: "Weight",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "InventoryCommitted",
                table: "PaymentIntents");

            migrationBuilder.DropColumn(
                name: "TokensValidAfterUtc",
                table: "OnlineCustomers");

            migrationBuilder.DropColumn(
                name: "CurrentAccessTokenExpiresAtUtc",
                table: "OnlineCustomerRefreshTokens");

            migrationBuilder.DropColumn(
                name: "CurrentAccessTokenJti",
                table: "OnlineCustomerRefreshTokens");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "CheckoutSessions");

            migrationBuilder.RenameTable(
                name: "AbandonedCartRecoveryLogs",
                newName: "AbandonedCartRecoveryLog");

            migrationBuilder.RenameIndex(
                name: "IX_AbandonedCartRecoveryLogs_AbandonedCartId",
                table: "AbandonedCartRecoveryLog",
                newName: "IX_AbandonedCartRecoveryLog_AbandonedCartId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AbandonedCartRecoveryLog",
                table: "AbandonedCartRecoveryLog",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AbandonedCartRecoveryLog_AbandonedCarts_AbandonedCartId",
                table: "AbandonedCartRecoveryLog",
                column: "AbandonedCartId",
                principalTable: "AbandonedCarts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
