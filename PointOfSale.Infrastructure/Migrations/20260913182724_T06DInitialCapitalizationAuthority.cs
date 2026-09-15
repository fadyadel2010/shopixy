using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class T06DInitialCapitalizationAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TreasuryInitialCapitalizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CutoverDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DeclarationVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EvidenceReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PostedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PostedJournalId = table.Column<Guid>(type: "uuid", nullable: true),
                    OpeningEquityLedgerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpeningEquityAccountCodeSnapshot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OpeningEquityAccountNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PredecessorCapitalizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversalJournalId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversalOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversalRequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReversalReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReversedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MutationVersion = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreasuryInitialCapitalizations", x => x.Id);
                    table.UniqueConstraint("AK_TreasuryInitialCapitalizations_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_TreasuryInitialCapitalization_MutationVersion", "\"MutationVersion\" > 0");
                    table.CheckConstraint("CK_TreasuryInitialCapitalization_OpeningEquity", "\"OpeningEquityAccountCodeSnapshot\" = '3010'");
                    table.CheckConstraint("CK_TreasuryInitialCapitalization_Total", "\"TotalAmount\" > 0");
                    table.ForeignKey(
                        name: "FK_TreasuryInitialCapitalizations_LedgerAccounts_TenantId_Open~",
                        columns: x => new { x.TenantId, x.OpeningEquityLedgerAccountId },
                        principalTable: "LedgerAccounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TreasuryInitialCapitalizations_TreasuryInitialCapitalizatio~",
                        columns: x => new { x.TenantId, x.PredecessorCapitalizationId },
                        principalTable: "TreasuryInitialCapitalizations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TreasuryInitialCapitalizationAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CapitalizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TreasuryAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    LedgerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TreasuryAccountTypeSnapshot = table.Column<int>(type: "integer", nullable: false),
                    TreasuryAccountNameSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LedgerAccountCodeSnapshot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LedgerAccountNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreasuryInitialCapitalizationAllocations", x => x.Id);
                    table.CheckConstraint("CK_TreasuryInitialCapitalizationAllocation_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_TreasuryInitialCapitalizationAllocations_LedgerAccounts_Ten~",
                        columns: x => new { x.TenantId, x.LedgerAccountId },
                        principalTable: "LedgerAccounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TreasuryInitialCapitalizationAllocations_TreasuryAccounts_T~",
                        columns: x => new { x.TenantId, x.TreasuryAccountId },
                        principalTable: "TreasuryAccounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TreasuryInitialCapitalizationAllocations_TreasuryInitialCap~",
                        columns: x => new { x.TenantId, x.CapitalizationId },
                        principalTable: "TreasuryInitialCapitalizations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TreasuryInitialCapitalizationAllocation_Destination",
                table: "TreasuryInitialCapitalizationAllocations",
                columns: new[] { "TenantId", "CapitalizationId", "TreasuryAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TreasuryInitialCapitalizationAllocations_TenantId_LedgerAcc~",
                table: "TreasuryInitialCapitalizationAllocations",
                columns: new[] { "TenantId", "LedgerAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_TreasuryInitialCapitalizationAllocations_TenantId_TreasuryA~",
                table: "TreasuryInitialCapitalizationAllocations",
                columns: new[] { "TenantId", "TreasuryAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_TreasuryInitialCapitalization_OneActivePerTenant",
                table: "TreasuryInitialCapitalizations",
                column: "TenantId",
                unique: true,
                filter: "\"Status\" IN (0, 1) AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_TreasuryInitialCapitalization_Predecessor",
                table: "TreasuryInitialCapitalizations",
                columns: new[] { "TenantId", "PredecessorCapitalizationId" },
                unique: true,
                filter: "\"PredecessorCapitalizationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TreasuryInitialCapitalization_Tenant_Number",
                table: "TreasuryInitialCapitalizations",
                columns: new[] { "TenantId", "DocumentNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TreasuryInitialCapitalization_Tenant_Operation",
                table: "TreasuryInitialCapitalizations",
                columns: new[] { "TenantId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TreasuryInitialCapitalizations_TenantId_OpeningEquityLedger~",
                table: "TreasuryInitialCapitalizations",
                columns: new[] { "TenantId", "OpeningEquityLedgerAccountId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TreasuryInitialCapitalizationAllocations");

            migrationBuilder.DropTable(
                name: "TreasuryInitialCapitalizations");
        }
    }
}
