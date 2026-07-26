using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AF38_HOTFIX_Constraints_Update : Migration
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

            migrationBuilder.AddColumn<Guid>(
                name: "PayoutRequestId",
                table: "AffiliateCommissionLedger",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AffiliatePayoutRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AffiliateAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ApprovedAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    RemainingAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AdminNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PaymentReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliatePayoutRequests", x => x.Id);
                });

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

            migrationBuilder.CreateIndex(
                name: "IX_AffiliatePayoutRequests_AffiliateId_Status",
                table: "AffiliatePayoutRequests",
                columns: new[] { "AffiliateAccountId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliatePayoutRequests_TenantId_AffiliateId_Status",
                table: "AffiliatePayoutRequests",
                columns: new[] { "StoreTenantId", "AffiliateAccountId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliatePayoutRequests_TenantId_Status",
                table: "AffiliatePayoutRequests",
                columns: new[] { "StoreTenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AffiliatePayoutRequests");

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
        }
    }
}
