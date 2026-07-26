using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOnlineCustomerWishlistItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EmailVerifiedAtUtc",
                table: "OnlineCustomers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedEmail",
                table: "OnlineCustomers",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "OnlineCustomerAuthProviders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OnlineCustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProviderUserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProviderEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EmailVerifiedByProvider = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PictureUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnlineCustomerAuthProviders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OnlineCustomerAuthProviders_OnlineCustomers_OnlineCustomerId",
                        column: x => x.OnlineCustomerId,
                        principalTable: "OnlineCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OnlineCustomerEmailVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OnlineCustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsUsed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    UsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnlineCustomerEmailVerifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OnlineCustomerEmailVerifications_OnlineCustomers_OnlineCust~",
                        column: x => x.OnlineCustomerId,
                        principalTable: "OnlineCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OnlineCustomerPasswordResetTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OnlineCustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsUsed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    UsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnlineCustomerPasswordResetTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OnlineCustomerPasswordResetTokens_OnlineCustomers_OnlineCus~",
                        column: x => x.OnlineCustomerId,
                        principalTable: "OnlineCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OnlineCustomerRefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OnlineCustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsRevoked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ReplacedByTokenId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByIp = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    CreatedByUserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnlineCustomerRefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OnlineCustomerRefreshTokens_OnlineCustomers_OnlineCustomerId",
                        column: x => x.OnlineCustomerId,
                        principalTable: "OnlineCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OnlineCustomerWishlistItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OnlineCustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsRemoved = table.Column<bool>(type: "boolean", nullable: false),
                    RemovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnlineCustomerWishlistItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StorefrontAuthProviderSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ClientId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ClientSecretEncrypted = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    AdditionalScopes = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontAuthProviderSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthProvider_TenantId_CustomerId",
                table: "OnlineCustomerAuthProviders",
                columns: new[] { "TenantId", "OnlineCustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthProvider_TenantId_CustomerId_Provider",
                table: "OnlineCustomerAuthProviders",
                columns: new[] { "TenantId", "OnlineCustomerId", "Provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthProvider_TenantId_Provider_ProviderUserId",
                table: "OnlineCustomerAuthProviders",
                columns: new[] { "TenantId", "Provider", "ProviderUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCustomerAuthProviders_OnlineCustomerId",
                table: "OnlineCustomerAuthProviders",
                column: "OnlineCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailVerification_ExpiresAtUtc",
                table: "OnlineCustomerEmailVerifications",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_EmailVerification_TenantId_CustomerId_IsUsed",
                table: "OnlineCustomerEmailVerifications",
                columns: new[] { "TenantId", "OnlineCustomerId", "IsUsed" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailVerification_TenantId_TokenHash",
                table: "OnlineCustomerEmailVerifications",
                columns: new[] { "TenantId", "TokenHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCustomerEmailVerifications_OnlineCustomerId",
                table: "OnlineCustomerEmailVerifications",
                column: "OnlineCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCustomerPasswordResetTokens_OnlineCustomerId",
                table: "OnlineCustomerPasswordResetTokens",
                column: "OnlineCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetToken_ExpiresAtUtc",
                table: "OnlineCustomerPasswordResetTokens",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetToken_TenantId_CustomerId_IsUsed",
                table: "OnlineCustomerPasswordResetTokens",
                columns: new[] { "TenantId", "OnlineCustomerId", "IsUsed" });

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetToken_TenantId_TokenHash",
                table: "OnlineCustomerPasswordResetTokens",
                columns: new[] { "TenantId", "TokenHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCustomerRefreshTokens_OnlineCustomerId",
                table: "OnlineCustomerRefreshTokens",
                column: "OnlineCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshToken_ExpiresAtUtc",
                table: "OnlineCustomerRefreshTokens",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshToken_TenantId_CustomerId",
                table: "OnlineCustomerRefreshTokens",
                columns: new[] { "TenantId", "OnlineCustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshToken_TenantId_CustomerId_IsRevoked",
                table: "OnlineCustomerRefreshTokens",
                columns: new[] { "TenantId", "OnlineCustomerId", "IsRevoked" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshToken_TenantId_TokenHash",
                table: "OnlineCustomerRefreshTokens",
                columns: new[] { "TenantId", "TokenHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCustomerWishlistItems_CreatedAtUtc",
                table: "OnlineCustomerWishlistItems",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCustomerWishlistItems_TenantId_CustomerId",
                table: "OnlineCustomerWishlistItems",
                columns: new[] { "TenantId", "OnlineCustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCustomerWishlistItems_TenantId_CustomerId_ProductId",
                table: "OnlineCustomerWishlistItems",
                columns: new[] { "TenantId", "OnlineCustomerId", "ProductId" },
                unique: true,
                filter: "\"IsRemoved\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCustomerWishlistItems_TenantId_IsRemoved",
                table: "OnlineCustomerWishlistItems",
                columns: new[] { "TenantId", "IsRemoved" });

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCustomerWishlistItems_TenantId_ProductId",
                table: "OnlineCustomerWishlistItems",
                columns: new[] { "TenantId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthProviderSettings_TenantId_Provider",
                table: "StorefrontAuthProviderSettings",
                columns: new[] { "TenantId", "Provider" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OnlineCustomerAuthProviders");

            migrationBuilder.DropTable(
                name: "OnlineCustomerEmailVerifications");

            migrationBuilder.DropTable(
                name: "OnlineCustomerPasswordResetTokens");

            migrationBuilder.DropTable(
                name: "OnlineCustomerRefreshTokens");

            migrationBuilder.DropTable(
                name: "OnlineCustomerWishlistItems");

            migrationBuilder.DropTable(
                name: "StorefrontAuthProviderSettings");

            migrationBuilder.DropColumn(
                name: "EmailVerifiedAtUtc",
                table: "OnlineCustomers");

            migrationBuilder.DropColumn(
                name: "NormalizedEmail",
                table: "OnlineCustomers");
        }
    }
}
