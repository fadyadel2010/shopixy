using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AffiliateCommLedger_EntryType",
                table: "AffiliateCommissionLedger");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AffiliateCommLedger_Rate",
                table: "AffiliateCommissionLedger");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AffiliateCommLedger_ReversalRef",
                table: "AffiliateCommissionLedger");

            migrationBuilder.DropColumn(
                name: "PayoutRequestId",
                table: "AffiliateCommissionLedger");

            migrationBuilder.CreateTable(
                name: "AffiliateBalanceLedger",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    StoreTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AffiliateAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryType = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliateBalanceLedger", x => x.Id);
                    table.CheckConstraint("CK_AffiliateBalanceLedger_Amount", "\"Amount\" != 0");
                    table.CheckConstraint("CK_AffiliateBalanceLedger_EntryType", "\"EntryType\" IN (1, 2, 3, 4)");
                    table.CheckConstraint("CK_AffiliateBalanceLedger_WithdrawalNegative", "(\"EntryType\" != 1 OR \"Amount\" < 0)");
                    table.ForeignKey(
                        name: "FK_AffiliateBalanceLedger_AffiliateAccounts_AffiliateAccountId",
                        column: x => x.AffiliateAccountId,
                        principalTable: "AffiliateAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CheckoutSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CartId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OnlineCustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Area = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ItemsSnapshotJson = table.Column<string>(type: "text", nullable: false),
                    CartTotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    ShippingMethodId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentMethodCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastActivityAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AbandonedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConvertedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConvertedOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConvertedOrderNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckoutSessions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_AffiliateCommLedger_EntryType",
                table: "AffiliateCommissionLedger",
                sql: "\"EntryType\" IN (0, 1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AffiliateCommLedger_Rate",
                table: "AffiliateCommissionLedger",
                sql: "\"CommissionRate\" > 0 AND \"CommissionRate\" <= 0.50");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AffiliateCommLedger_ReversalRef",
                table: "AffiliateCommissionLedger",
                sql: "(\"EntryType\" IN (1, 2, 3) AND \"OriginalEntryId\" IS NOT NULL) OR (\"EntryType\" = 0 AND \"OriginalEntryId\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateBalanceLedger_AffiliateAccountId",
                table: "AffiliateBalanceLedger",
                column: "AffiliateAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateBalanceLedger_ReferenceId",
                table: "AffiliateBalanceLedger",
                column: "ReferenceId",
                filter: "\"ReferenceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateBalanceLedger_StoreTenantRecorded",
                table: "AffiliateBalanceLedger",
                columns: new[] { "StoreTenantId", "RecordedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateBalanceLedger_Idempotency",
                table: "AffiliateBalanceLedger",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_CartId",
                table: "CheckoutSessions",
                column: "CartId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_Status_LastActivity",
                table: "CheckoutSessions",
                columns: new[] { "Status", "LastActivityAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_TenantId",
                table: "CheckoutSessions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_TenantId_CartId",
                table: "CheckoutSessions",
                columns: new[] { "TenantId", "CartId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AffiliateBalanceLedger");

            migrationBuilder.DropTable(
                name: "CheckoutSessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AffiliateCommLedger_EntryType",
                table: "AffiliateCommissionLedger");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AffiliateCommLedger_Rate",
                table: "AffiliateCommissionLedger");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AffiliateCommLedger_ReversalRef",
                table: "AffiliateCommissionLedger");

            migrationBuilder.AddColumn<Guid>(
                name: "PayoutRequestId",
                table: "AffiliateCommissionLedger",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AffiliateCommLedger_EntryType",
                table: "AffiliateCommissionLedger",
                sql: "\"EntryType\" IN (0, 1, 2, 3, 4)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AffiliateCommLedger_Rate",
                table: "AffiliateCommissionLedger",
                sql: "(\"CommissionRate\" > 0 AND \"CommissionRate\" <= 0.50) OR (\"EntryType\" = 4 AND \"CommissionRate\" = 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AffiliateCommLedger_ReversalRef",
                table: "AffiliateCommissionLedger",
                sql: "(\"EntryType\" IN (1, 2, 3) AND \"OriginalEntryId\" IS NOT NULL) OR (\"EntryType\" IN (0, 4) AND \"OriginalEntryId\" IS NULL)");
        }
    }
}
