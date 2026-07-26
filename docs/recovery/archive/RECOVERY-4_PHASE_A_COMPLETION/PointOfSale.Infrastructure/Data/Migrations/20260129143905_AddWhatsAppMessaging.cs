using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppMessaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PixelTrackingConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Strategy = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    AppliesToJson = table.Column<string>(type: "text", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PixelTrackingConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductPixelOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Strategy = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ApplyToMeta = table.Column<bool>(type: "boolean", nullable: false),
                    ApplyToGoogle = table.Column<bool>(type: "boolean", nullable: false),
                    ApplyToTikTok = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductPixelOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductPixelOverrides_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    phone_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    use_for_order_confirmation = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    use_for_otp_verification = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    last_connected_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_disconnected_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status_reason = table.Column<string>(type: "text", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_whatsapp_accounts", x => x.id);
                    table.ForeignKey(
                        name: "FK_whatsapp_accounts_Tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PixelTrackingConfigs_TenantId",
                table: "PixelTrackingConfigs",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductPixelOverrides_ProductId",
                table: "ProductPixelOverrides",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductPixelOverrides_TenantId",
                table: "ProductPixelOverrides",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductPixelOverrides_TenantId_ProductId",
                table: "ProductPixelOverrides",
                columns: new[] { "TenantId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_accounts_status",
                table: "whatsapp_accounts",
                column: "status",
                filter: "enabled = TRUE");

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_accounts_tenant_id",
                table: "whatsapp_accounts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "uq_whatsapp_account_phone_tenant",
                table: "whatsapp_accounts",
                columns: new[] { "tenant_id", "phone_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PixelTrackingConfigs");

            migrationBuilder.DropTable(
                name: "ProductPixelOverrides");

            migrationBuilder.DropTable(
                name: "whatsapp_accounts");
        }
    }
}
