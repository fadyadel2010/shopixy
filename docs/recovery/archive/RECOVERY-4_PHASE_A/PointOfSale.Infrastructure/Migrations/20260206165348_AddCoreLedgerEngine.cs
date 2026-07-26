using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCoreLedgerEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "AbandonedCarts");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "AbandonedCarts");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "AbandonedCarts");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "AbandonedCarts");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "AbandonedCarts");

            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AccountType = table.Column<int>(type: "integer", nullable: false),
                    ParentAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Level = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    CurrentBalance = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsClearing = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    PreventNegative = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    ClearingSourceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ClearingSourceCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ExpectedSettlementDays = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Accounts_Accounts_ParentAccountId",
                        column: x => x.ParentAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    JournalNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    JournalDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PostedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReversedByJournalId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversesJournalId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversalReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalEntries_JournalEntries_ReversedByJournalId",
                        column: x => x.ReversedByJournalId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JournalNumberSequences",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentYear = table.Column<int>(type: "integer", nullable: false),
                    NextNumber = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalNumberSequences", x => x.TenantId);
                });

            migrationBuilder.CreateTable(
                name: "LedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    DebitAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CreditAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LineDescription = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerEntries", x => x.Id);
                    table.CheckConstraint("CK_LedgerEntries_PositiveAmounts", "\"DebitAmount\" >= 0 AND \"CreditAmount\" >= 0");
                    table.CheckConstraint("CK_LedgerEntries_SingleSide", "(\"DebitAmount\" > 0 AND \"CreditAmount\" = 0) OR (\"DebitAmount\" = 0 AND \"CreditAmount\" > 0)");
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_ParentAccountId",
                table: "Accounts",
                column: "ParentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_TenantId",
                table: "Accounts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_TenantId_AccountCode",
                table: "Accounts",
                columns: new[] { "TenantId", "AccountCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_TenantId_AccountType",
                table: "Accounts",
                columns: new[] { "TenantId", "AccountType" });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_TenantId_IsClearing",
                table: "Accounts",
                columns: new[] { "TenantId", "IsClearing" },
                filter: "\"IsClearing\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_PostedAtUtc",
                table: "JournalEntries",
                column: "PostedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_ReversedByJournalId",
                table: "JournalEntries",
                column: "ReversedByJournalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_TenantId",
                table: "JournalEntries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_TenantId_IdempotencyKey",
                table: "JournalEntries",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_TenantId_JournalDate",
                table: "JournalEntries",
                columns: new[] { "TenantId", "JournalDate" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_TenantId_JournalNumber",
                table: "JournalEntries",
                columns: new[] { "TenantId", "JournalNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_TenantId_Source",
                table: "JournalEntries",
                columns: new[] { "TenantId", "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_TenantId_Status",
                table: "JournalEntries",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_AccountId_CreatedAtUtc",
                table: "LedgerEntries",
                columns: new[] { "AccountId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_JournalEntryId",
                table: "LedgerEntries",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_TenantId",
                table: "LedgerEntries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_TenantId_AccountId",
                table: "LedgerEntries",
                columns: new[] { "TenantId", "AccountId" });
            
            // ═══════════════════════════════════════════════════════════════════
            // IMMUTABILITY TRIGGERS: Prevent UPDATE/DELETE on journal tables
            // ═══════════════════════════════════════════════════════════════════
            
            // Function to prevent unauthorized updates on journal entries
            // Only allows Status, ReversedByJournalId, ReversalReason updates (for reversal marking)
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION prevent_journal_modification()
                RETURNS TRIGGER AS $$
                BEGIN
                    IF TG_OP = 'UPDATE' THEN
                        IF OLD.""JournalNumber"" != NEW.""JournalNumber"" OR
                           OLD.""JournalDate"" != NEW.""JournalDate"" OR
                           OLD.""SourceType"" != NEW.""SourceType"" OR
                           OLD.""SourceId"" != NEW.""SourceId"" OR
                           OLD.""Description"" != NEW.""Description"" OR
                           OLD.""IdempotencyKey"" != NEW.""IdempotencyKey"" OR
                           OLD.""PostedAtUtc"" != NEW.""PostedAtUtc"" THEN
                            RAISE EXCEPTION 'Journal entries are immutable. Only status and reversal fields can be updated.';
                        END IF;
                        RETURN NEW;
                    END IF;
                    
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Journal entries cannot be deleted. Use reversal instead.';
                    END IF;
                    
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;
            ");
            
            // Function to prevent any modification on ledger entries
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION prevent_ledger_modification()
                RETURNS TRIGGER AS $$
                BEGIN
                    IF TG_OP = 'UPDATE' THEN
                        RAISE EXCEPTION 'Ledger entries are immutable and cannot be updated.';
                    END IF;
                    
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Ledger entries cannot be deleted. Use journal reversal instead.';
                    END IF;
                    
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;
            ");
            
            // Apply immutability triggers
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS trg_prevent_journal_modification ON ""JournalEntries"";
                CREATE TRIGGER trg_prevent_journal_modification
                    BEFORE UPDATE OR DELETE ON ""JournalEntries""
                    FOR EACH ROW
                    EXECUTE FUNCTION prevent_journal_modification();
            ");
            
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS trg_prevent_ledger_modification ON ""LedgerEntries"";
                CREATE TRIGGER trg_prevent_ledger_modification
                    BEFORE UPDATE OR DELETE ON ""LedgerEntries""
                    FOR EACH ROW
                    EXECUTE FUNCTION prevent_ledger_modification();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop immutability triggers first
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS trg_prevent_ledger_modification ON ""LedgerEntries"";");
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS trg_prevent_journal_modification ON ""JournalEntries"";");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS prevent_ledger_modification();");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS prevent_journal_modification();");
            
            migrationBuilder.DropTable(
                name: "JournalNumberSequences");

            migrationBuilder.DropTable(
                name: "LedgerEntries");

            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropTable(
                name: "JournalEntries");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "AbandonedCarts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedBy",
                table: "AbandonedCarts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "AbandonedCarts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "AbandonedCarts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedBy",
                table: "AbandonedCarts",
                type: "uuid",
                nullable: true);
        }
    }
}
