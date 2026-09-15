using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class T06SupplierCardFundingAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FundingInstrumentId",
                table: "SupplierPayments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FundingInstrumentVersion",
                table: "SupplierPayments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FundingLedgerAccountId",
                table: "SupplierPayments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FundingInstrumentId",
                table: "PurchaseInvoicePayments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FundingInstrumentVersion",
                table: "PurchaseInvoicePayments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FundingLedgerAccountId",
                table: "PurchaseInvoicePayments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSupplierCardFunding",
                table: "LedgerAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_TreasuryAccounts_TenantId_Id",
                table: "TreasuryAccounts",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "SupplierCardFundingInstruments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    InstrumentType = table.Column<int>(type: "integer", nullable: false),
                    LedgerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    FundingSourceTreasuryAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Issuer = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    AuthorityVersion = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierCardFundingInstruments", x => x.Id);
                    table.UniqueConstraint("AK_SupplierCardFundingInstruments_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_SupplierCardFundingInstrument_AuthorityVersion", "\"AuthorityVersion\" > 0");
                    table.CheckConstraint("CK_SupplierCardFundingInstrument_Last4", "\"Last4\" IS NULL OR \"Last4\" ~ '^[0-9]{4}$'");
                    table.ForeignKey(
                        name: "FK_SupplierCardFundingInstruments_LedgerAccounts_TenantId_Ledg~",
                        columns: x => new { x.TenantId, x.LedgerAccountId },
                        principalTable: "LedgerAccounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierCardFundingInstruments_TreasuryAccounts_TenantId_Fu~",
                        columns: x => new { x.TenantId, x.FundingSourceTreasuryAccountId },
                        principalTable: "TreasuryAccounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierCardFundings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FundingInstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    FundingLedgerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    FundingSourceTreasuryAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    FundingSourceLedgerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    FundingInstrumentVersion = table.Column<int>(type: "integer", nullable: false),
                    FundingDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReferenceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierCardFundings", x => x.Id);
                    table.CheckConstraint("CK_SupplierCardFunding_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_SupplierCardFundings_LedgerAccounts_TenantId_FundingLedgerA~",
                        columns: x => new { x.TenantId, x.FundingLedgerAccountId },
                        principalTable: "LedgerAccounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierCardFundings_LedgerAccounts_TenantId_FundingSourceL~",
                        columns: x => new { x.TenantId, x.FundingSourceLedgerAccountId },
                        principalTable: "LedgerAccounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierCardFundings_SupplierCardFundingInstruments_TenantI~",
                        columns: x => new { x.TenantId, x.FundingInstrumentId },
                        principalTable: "SupplierCardFundingInstruments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierCardFundings_TreasuryAccounts_TenantId_FundingSourc~",
                        columns: x => new { x.TenantId, x.FundingSourceTreasuryAccountId },
                        principalTable: "TreasuryAccounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_FundingInstrumentId",
                table: "SupplierPayments",
                column: "FundingInstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_TenantId_FundingInstrumentId",
                table: "SupplierPayments",
                columns: new[] { "TenantId", "FundingInstrumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_TenantId_FundingLedgerAccountId",
                table: "SupplierPayments",
                columns: new[] { "TenantId", "FundingLedgerAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoicePayments_FundingInstrumentId",
                table: "PurchaseInvoicePayments",
                column: "FundingInstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoicePayments_FundingLedgerAccountId",
                table: "PurchaseInvoicePayments",
                column: "FundingLedgerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCardFundingInstruments_TenantId_FundingSourceTreasu~",
                table: "SupplierCardFundingInstruments",
                columns: new[] { "TenantId", "FundingSourceTreasuryAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCardFundingInstruments_TenantId_IsActive",
                table: "SupplierCardFundingInstruments",
                columns: new[] { "TenantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCardFundingInstruments_TenantId_LedgerAccountId",
                table: "SupplierCardFundingInstruments",
                columns: new[] { "TenantId", "LedgerAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCardFundings_TenantId_FundingInstrumentId_FundingDa~",
                table: "SupplierCardFundings",
                columns: new[] { "TenantId", "FundingInstrumentId", "FundingDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCardFundings_TenantId_FundingLedgerAccountId",
                table: "SupplierCardFundings",
                columns: new[] { "TenantId", "FundingLedgerAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCardFundings_TenantId_FundingSourceLedgerAccountId",
                table: "SupplierCardFundings",
                columns: new[] { "TenantId", "FundingSourceLedgerAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCardFundings_TenantId_FundingSourceTreasuryAccountId",
                table: "SupplierCardFundings",
                columns: new[] { "TenantId", "FundingSourceTreasuryAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCardFundings_TenantId_IdempotencyKey",
                table: "SupplierCardFundings",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseInvoicePayments_LedgerAccounts_FundingLedgerAccount~",
                table: "PurchaseInvoicePayments",
                column: "FundingLedgerAccountId",
                principalTable: "LedgerAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseInvoicePayments_SupplierCardFundingInstruments_Fund~",
                table: "PurchaseInvoicePayments",
                column: "FundingInstrumentId",
                principalTable: "SupplierCardFundingInstruments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SupplierPayments_LedgerAccounts_TenantId_FundingLedgerAccou~",
                table: "SupplierPayments",
                columns: new[] { "TenantId", "FundingLedgerAccountId" },
                principalTable: "LedgerAccounts",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SupplierPayments_SupplierCardFundingInstruments_TenantId_Fu~",
                table: "SupplierPayments",
                columns: new[] { "TenantId", "FundingInstrumentId" },
                principalTable: "SupplierCardFundingInstruments",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseInvoicePayments_LedgerAccounts_FundingLedgerAccount~",
                table: "PurchaseInvoicePayments");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseInvoicePayments_SupplierCardFundingInstruments_Fund~",
                table: "PurchaseInvoicePayments");

            migrationBuilder.DropForeignKey(
                name: "FK_SupplierPayments_LedgerAccounts_TenantId_FundingLedgerAccou~",
                table: "SupplierPayments");

            migrationBuilder.DropForeignKey(
                name: "FK_SupplierPayments_SupplierCardFundingInstruments_TenantId_Fu~",
                table: "SupplierPayments");

            migrationBuilder.DropTable(
                name: "SupplierCardFundings");

            migrationBuilder.DropTable(
                name: "SupplierCardFundingInstruments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_TreasuryAccounts_TenantId_Id",
                table: "TreasuryAccounts");

            migrationBuilder.DropIndex(
                name: "IX_SupplierPayments_FundingInstrumentId",
                table: "SupplierPayments");

            migrationBuilder.DropIndex(
                name: "IX_SupplierPayments_TenantId_FundingInstrumentId",
                table: "SupplierPayments");

            migrationBuilder.DropIndex(
                name: "IX_SupplierPayments_TenantId_FundingLedgerAccountId",
                table: "SupplierPayments");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoicePayments_FundingInstrumentId",
                table: "PurchaseInvoicePayments");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoicePayments_FundingLedgerAccountId",
                table: "PurchaseInvoicePayments");

            migrationBuilder.DropColumn(
                name: "FundingInstrumentId",
                table: "SupplierPayments");

            migrationBuilder.DropColumn(
                name: "FundingInstrumentVersion",
                table: "SupplierPayments");

            migrationBuilder.DropColumn(
                name: "FundingLedgerAccountId",
                table: "SupplierPayments");

            migrationBuilder.DropColumn(
                name: "FundingInstrumentId",
                table: "PurchaseInvoicePayments");

            migrationBuilder.DropColumn(
                name: "FundingInstrumentVersion",
                table: "PurchaseInvoicePayments");

            migrationBuilder.DropColumn(
                name: "FundingLedgerAccountId",
                table: "PurchaseInvoicePayments");

            migrationBuilder.DropColumn(
                name: "IsSupplierCardFunding",
                table: "LedgerAccounts");
        }
    }
}
