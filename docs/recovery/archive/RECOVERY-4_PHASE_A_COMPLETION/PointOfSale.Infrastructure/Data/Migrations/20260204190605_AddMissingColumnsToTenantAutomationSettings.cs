using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingColumnsToTenantAutomationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "use_for_abandoned_cart_recovery",
                table: "whatsapp_accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "use_for_payment_approval",
                table: "whatsapp_accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "UseBaileys",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PhoneVerified",
                table: "OnlineCustomers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PhoneVerifiedAtUtc",
                table: "OnlineCustomers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AbandonedCarts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CartId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: true),
                    CartSnapshotJson = table.Column<string>(type: "text", nullable: false),
                    LastActivityAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AbandonedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RecoveryStatus = table.Column<string>(type: "text", nullable: false),
                    PendingAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TriggeredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RecoveredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RecoveredOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpiredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    LastErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbandonedCarts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbandonedCarts_OnlineCustomers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "OnlineCustomers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AbandonedCarts_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantAutomationSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnableAbandonedRecovery = table.Column<bool>(type: "boolean", nullable: false),
                    FirstMessageDelayMinutes = table.Column<int>(type: "integer", nullable: false),
                    SecondMessageDelayMinutes = table.Column<int>(type: "integer", nullable: false),
                    WorkingHoursFrom = table.Column<TimeSpan>(type: "interval", nullable: false),
                    WorkingHoursTo = table.Column<TimeSpan>(type: "interval", nullable: false),
                    N8nWebhookApiKey = table.Column<string>(type: "text", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantAutomationSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantAutomationSettings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AbandonedCartRecoveryLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AbandonedCartId = table.Column<Guid>(type: "uuid", nullable: false),
                    CartId = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneNumber = table.Column<string>(type: "text", nullable: false),
                    FirstMessageSent = table.Column<bool>(type: "boolean", nullable: false),
                    FirstMessageSentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FirstMessageError = table.Column<string>(type: "text", nullable: true),
                    SecondMessageSent = table.Column<bool>(type: "boolean", nullable: false),
                    SecondMessageSentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SecondMessageError = table.Column<string>(type: "text", nullable: true),
                    WorkflowStartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WorkflowCompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbandonedCartRecoveryLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbandonedCartRecoveryLog_AbandonedCarts_AbandonedCartId",
                        column: x => x.AbandonedCartId,
                        principalTable: "AbandonedCarts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbandonedCartRecoveryLog_AbandonedCartId",
                table: "AbandonedCartRecoveryLog",
                column: "AbandonedCartId");

            migrationBuilder.CreateIndex(
                name: "IX_AbandonedCarts_CartId",
                table: "AbandonedCarts",
                column: "CartId");

            migrationBuilder.CreateIndex(
                name: "IX_AbandonedCarts_CustomerId",
                table: "AbandonedCarts",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_AbandonedCarts_RecoveryStatus_LastActivityAtUtc",
                table: "AbandonedCarts",
                columns: new[] { "RecoveryStatus", "LastActivityAtUtc" },
                filter: "\"RecoveryStatus\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_AbandonedCarts_TenantId",
                table: "AbandonedCarts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AbandonedCarts_TenantId_CartId",
                table: "AbandonedCarts",
                columns: new[] { "TenantId", "CartId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantAutomationSettings_EnableAbandonedRecovery",
                table: "TenantAutomationSettings",
                column: "EnableAbandonedRecovery");

            migrationBuilder.CreateIndex(
                name: "IX_TenantAutomationSettings_TenantId",
                table: "TenantAutomationSettings",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbandonedCartRecoveryLog");

            migrationBuilder.DropTable(
                name: "TenantAutomationSettings");

            migrationBuilder.DropTable(
                name: "AbandonedCarts");

            migrationBuilder.DropColumn(
                name: "use_for_abandoned_cart_recovery",
                table: "whatsapp_accounts");

            migrationBuilder.DropColumn(
                name: "use_for_payment_approval",
                table: "whatsapp_accounts");

            migrationBuilder.DropColumn(
                name: "UseBaileys",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "PhoneVerified",
                table: "OnlineCustomers");

            migrationBuilder.DropColumn(
                name: "PhoneVerifiedAtUtc",
                table: "OnlineCustomers");
        }
    }
}
