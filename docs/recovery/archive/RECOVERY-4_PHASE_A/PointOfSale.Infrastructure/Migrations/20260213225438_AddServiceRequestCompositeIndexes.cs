using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceRequestCompositeIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Accounts_Accounts_ParentAccountId",
                table: "Accounts");

            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntries_JournalEntries_ReversedByJournalId",
                table: "JournalEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_LedgerEntries_Accounts_AccountId",
                table: "LedgerEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_LedgerEntries_JournalEntries_JournalEntryId",
                table: "LedgerEntries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_LedgerEntries",
                table: "LedgerEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LedgerEntries_PositiveAmounts",
                table: "LedgerEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LedgerEntries_SingleSide",
                table: "LedgerEntries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_JournalNumberSequences",
                table: "JournalNumberSequences");

            migrationBuilder.DropPrimaryKey(
                name: "PK_JournalEntries",
                table: "JournalEntries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Accounts",
                table: "Accounts");

            migrationBuilder.RenameTable(
                name: "LedgerEntries",
                newName: "LedgerLines");

            migrationBuilder.RenameTable(
                name: "JournalNumberSequences",
                newName: "LedgerJournalNumberSequences");

            migrationBuilder.RenameTable(
                name: "JournalEntries",
                newName: "LedgerJournalEntries");

            migrationBuilder.RenameTable(
                name: "Accounts",
                newName: "LedgerAccounts");

            migrationBuilder.RenameIndex(
                name: "IX_LedgerEntries_TenantId_AccountId",
                table: "LedgerLines",
                newName: "IX_LedgerLines_TenantAcct");

            migrationBuilder.RenameIndex(
                name: "IX_LedgerEntries_TenantId",
                table: "LedgerLines",
                newName: "IX_LedgerLines_Tenant");

            migrationBuilder.RenameIndex(
                name: "IX_LedgerEntries_JournalEntryId",
                table: "LedgerLines",
                newName: "IX_LedgerLines_Journal");

            migrationBuilder.RenameIndex(
                name: "IX_LedgerEntries_AccountId_CreatedAtUtc",
                table: "LedgerLines",
                newName: "IX_LedgerLines_AcctDate");

            migrationBuilder.RenameIndex(
                name: "IX_JournalEntries_TenantId_Status",
                table: "LedgerJournalEntries",
                newName: "IX_LedgerJournal_Status");

            migrationBuilder.RenameIndex(
                name: "IX_JournalEntries_TenantId_Source",
                table: "LedgerJournalEntries",
                newName: "IX_LedgerJournal_Source");

            migrationBuilder.RenameIndex(
                name: "IX_JournalEntries_TenantId_JournalNumber",
                table: "LedgerJournalEntries",
                newName: "IX_LedgerJournal_Number");

            migrationBuilder.RenameIndex(
                name: "IX_JournalEntries_TenantId_JournalDate",
                table: "LedgerJournalEntries",
                newName: "IX_LedgerJournal_Date");

            migrationBuilder.RenameIndex(
                name: "IX_JournalEntries_TenantId_IdempotencyKey",
                table: "LedgerJournalEntries",
                newName: "IX_LedgerJournal_Idempotency");

            migrationBuilder.RenameIndex(
                name: "IX_JournalEntries_TenantId",
                table: "LedgerJournalEntries",
                newName: "IX_LedgerJournal_Tenant");

            migrationBuilder.RenameIndex(
                name: "IX_JournalEntries_ReversedByJournalId",
                table: "LedgerJournalEntries",
                newName: "IX_LedgerJournalEntries_ReversedByJournalId");

            migrationBuilder.RenameIndex(
                name: "IX_JournalEntries_PostedAtUtc",
                table: "LedgerJournalEntries",
                newName: "IX_LedgerJournal_Posted");

            migrationBuilder.RenameIndex(
                name: "IX_Accounts_TenantId_IsClearing",
                table: "LedgerAccounts",
                newName: "IX_LedgerAcct_Clearing");

            migrationBuilder.RenameIndex(
                name: "IX_Accounts_TenantId_AccountType",
                table: "LedgerAccounts",
                newName: "IX_LedgerAcct_TenantType");

            migrationBuilder.RenameIndex(
                name: "IX_Accounts_TenantId_AccountCode",
                table: "LedgerAccounts",
                newName: "IX_LedgerAcct_TenantCode");

            migrationBuilder.RenameIndex(
                name: "IX_Accounts_TenantId",
                table: "LedgerAccounts",
                newName: "IX_LedgerAcct_Tenant");

            migrationBuilder.RenameIndex(
                name: "IX_Accounts_ParentAccountId",
                table: "LedgerAccounts",
                newName: "IX_LedgerAcct_Parent");

            migrationBuilder.AddColumn<string>(
                name: "FulfillmentCarrierCode",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FulfillmentMode",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FulfillmentStatus",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "LedgerAccounts",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddPrimaryKey(
                name: "PK_LedgerLines",
                table: "LedgerLines",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_LedgerJournalNumberSequences",
                table: "LedgerJournalNumberSequences",
                column: "TenantId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_LedgerJournalEntries",
                table: "LedgerJournalEntries",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_LedgerAccounts",
                table: "LedgerAccounts",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "AccountingOutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    BusinessEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityTypeName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    JournalId = table.Column<Guid>(type: "uuid", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NextRetryAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsFailed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingOutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CommerceAccountingAudit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    JournalId = table.Column<Guid>(type: "uuid", nullable: false),
                    JournalNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    OperationType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    PostedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    WasIdempotent = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceAccountingAudit", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommerceAccountingAudit_LedgerJournalEntries_JournalId",
                        column: x => x.JournalId,
                        principalTable: "LedgerJournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MarketplaceProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Bio = table.Column<string>(type: "text", nullable: true),
                    ProfileImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CoverImageUrl = table.Column<string>(type: "text", nullable: true),
                    Location = table.Column<string>(type: "text", nullable: true),
                    Skills = table.Column<string>(type: "text", nullable: true),
                    PortfolioLinks = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentMethods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TargetAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClearingAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    FeesAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentMethods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentMethods_LedgerAccounts_ClearingAccountId",
                        column: x => x.ClearingAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentMethods_LedgerAccounts_FeesAccountId",
                        column: x => x.FeesAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentMethods_LedgerAccounts_TargetAccountId",
                        column: x => x.TargetAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedTrustEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    ProviderProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedTrustEvent", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TreasuryAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReferenceCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Location = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreasuryAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TreasuryAccounts_LedgerAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProviderTrustAggregates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    TotalReviews = table.Column<int>(type: "integer", nullable: false),
                    SumRatings = table.Column<int>(type: "integer", nullable: false),
                    AverageRating = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: false),
                    FiveStarCount = table.Column<int>(type: "integer", nullable: false),
                    FourStarCount = table.Column<int>(type: "integer", nullable: false),
                    ThreeStarCount = table.Column<int>(type: "integer", nullable: false),
                    TwoStarCount = table.Column<int>(type: "integer", nullable: false),
                    OneStarCount = table.Column<int>(type: "integer", nullable: false),
                    LastReviewAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderTrustAggregates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProviderTrustAggregates_MarketplaceProfiles_ProviderProfile~",
                        column: x => x.ProviderProfileId,
                        principalTable: "MarketplaceProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Services",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    StartingPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    PricingType = table.Column<int>(type: "integer", nullable: true),
                    Currency = table.Column<string>(type: "text", nullable: true),
                    ServiceImages = table.Column<string>(type: "text", nullable: true),
                    Packages = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Services", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Services_MarketplaceProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "MarketplaceProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServiceRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuyerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    RequestMessage = table.Column<string>(type: "text", nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedByProviderAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedByBuyerAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceRequests_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServiceRequestMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderRole = table.Column<string>(type: "text", nullable: false),
                    MessageType = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    AttachmentUrl = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceRequestMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceRequestMessages_ServiceRequests_ServiceRequestId",
                        column: x => x.ServiceRequestId,
                        principalTable: "ServiceRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServiceReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceReviews_ServiceRequests_ServiceRequestId",
                        column: x => x.ServiceRequestId,
                        principalTable: "ServiceRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ServiceReviews_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_LedgerLines_PositiveAmounts",
                table: "LedgerLines",
                sql: "\"DebitAmount\" >= 0 AND \"CreditAmount\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LedgerLines_SingleSide",
                table: "LedgerLines",
                sql: "(\"DebitAmount\" > 0 AND \"CreditAmount\" = 0) OR (\"DebitAmount\" = 0 AND \"CreditAmount\" > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_AccountingOutbox_Failed",
                table: "AccountingOutboxMessages",
                columns: new[] { "TenantId", "IsFailed" },
                filter: "\"IsFailed\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_AccountingOutbox_OccurredAtUtc",
                table: "AccountingOutboxMessages",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AccountingOutbox_ProcessingStatus",
                table: "AccountingOutboxMessages",
                columns: new[] { "ProcessedAtUtc", "IsFailed", "NextRetryAtUtc" },
                filter: "\"ProcessedAtUtc\" IS NULL AND \"IsFailed\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_AccountingOutbox_TenantId_IdempotencyKey",
                table: "AccountingOutboxMessages",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommerceAudit_Business",
                table: "CommerceAccountingAudit",
                columns: new[] { "TenantId", "BusinessType", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceAudit_JournalId",
                table: "CommerceAccountingAudit",
                column: "JournalId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceAudit_TenantId_PostedAtUtc",
                table: "CommerceAccountingAudit",
                columns: new[] { "TenantId", "PostedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceProfiles_IsActive",
                table: "MarketplaceProfiles",
                column: "IsActive",
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceProfiles_TenantId",
                table: "MarketplaceProfiles",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceProfiles_UserId",
                table: "MarketplaceProfiles",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UQ_MarketplaceProfiles_TenantUser",
                table: "MarketplaceProfiles",
                columns: new[] { "TenantId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethods_ClearingAccountId",
                table: "PaymentMethods",
                column: "ClearingAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethods_FeesAccountId",
                table: "PaymentMethods",
                column: "FeesAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethods_TargetAccountId",
                table: "PaymentMethods",
                column: "TargetAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethods_Tenant",
                table: "PaymentMethods",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethods_TenantCode",
                table: "PaymentMethods",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderTrustAggregates_AverageRating",
                table: "ProviderTrustAggregates",
                column: "AverageRating",
                descending: new bool[0],
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderTrustAggregates_TenantId",
                table: "ProviderTrustAggregates",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderTrustAggregates_TotalReviews",
                table: "ProviderTrustAggregates",
                column: "TotalReviews",
                descending: new bool[0],
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UQ_ProviderTrustAggregates_ProviderProfile",
                table: "ProviderTrustAggregates",
                column: "ProviderProfileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequestMessages_SenderUserId",
                table: "ServiceRequestMessages",
                column: "SenderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequestMessages_ServiceRequestId_CreatedAt",
                table: "ServiceRequestMessages",
                columns: new[] { "ServiceRequestId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequestMessages_TenantId",
                table: "ServiceRequestMessages",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_BuyerUserId",
                table: "ServiceRequests",
                column: "BuyerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_ProviderUserId",
                table: "ServiceRequests",
                column: "ProviderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_ServiceId",
                table: "ServiceRequests",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_Status",
                table: "ServiceRequests",
                column: "Status",
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_TenantId",
                table: "ServiceRequests",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceReviews_ProviderUserId",
                table: "ServiceReviews",
                column: "ProviderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceReviews_Rating",
                table: "ServiceReviews",
                column: "Rating",
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceReviews_ServiceId",
                table: "ServiceReviews",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceReviews_TenantId",
                table: "ServiceReviews",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "UQ_ServiceReviews_ServiceRequest",
                table: "ServiceReviews",
                column: "ServiceRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Services_Category",
                table: "Services",
                column: "Category",
                filter: "\"IsDeleted\" = false AND \"IsActive\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_Services_ProfileId",
                table: "Services",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Services_TenantId",
                table: "Services",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TreasuryAccounts_AccountId",
                table: "TreasuryAccounts",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TreasuryAccounts_Tenant",
                table: "TreasuryAccounts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TreasuryAccounts_TenantAccount",
                table: "TreasuryAccounts",
                columns: new[] { "TenantId", "AccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TreasuryAccounts_TenantType",
                table: "TreasuryAccounts",
                columns: new[] { "TenantId", "Type" });

            migrationBuilder.AddForeignKey(
                name: "FK_LedgerAccounts_LedgerAccounts_ParentAccountId",
                table: "LedgerAccounts",
                column: "ParentAccountId",
                principalTable: "LedgerAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LedgerJournalEntries_LedgerJournalEntries_ReversedByJournal~",
                table: "LedgerJournalEntries",
                column: "ReversedByJournalId",
                principalTable: "LedgerJournalEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LedgerLines_LedgerAccounts_AccountId",
                table: "LedgerLines",
                column: "AccountId",
                principalTable: "LedgerAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_BuyerUserId_Status",
                table: "ServiceRequests",
                columns: new[] { "BuyerUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_ProviderUserId_Status",
                table: "ServiceRequests",
                columns: new[] { "ProviderUserId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_LedgerLines_LedgerJournalEntries_JournalEntryId",
                table: "LedgerLines",
                column: "JournalEntryId",
                principalTable: "LedgerJournalEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ServiceRequests_BuyerUserId_Status",
                table: "ServiceRequests");

            migrationBuilder.DropIndex(
                name: "IX_ServiceRequests_ProviderUserId_Status",
                table: "ServiceRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_LedgerAccounts_LedgerAccounts_ParentAccountId",
                table: "LedgerAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_LedgerJournalEntries_LedgerJournalEntries_ReversedByJournal~",
                table: "LedgerJournalEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_LedgerLines_LedgerAccounts_AccountId",
                table: "LedgerLines");

            migrationBuilder.DropForeignKey(
                name: "FK_LedgerLines_LedgerJournalEntries_JournalEntryId",
                table: "LedgerLines");

            migrationBuilder.DropTable(
                name: "AccountingOutboxMessages");

            migrationBuilder.DropTable(
                name: "CommerceAccountingAudit");

            migrationBuilder.DropTable(
                name: "PaymentMethods");

            migrationBuilder.DropTable(
                name: "ProcessedTrustEvent");

            migrationBuilder.DropTable(
                name: "ProviderTrustAggregates");

            migrationBuilder.DropTable(
                name: "ServiceRequestMessages");

            migrationBuilder.DropTable(
                name: "ServiceReviews");

            migrationBuilder.DropTable(
                name: "TreasuryAccounts");

            migrationBuilder.DropTable(
                name: "ServiceRequests");

            migrationBuilder.DropTable(
                name: "Services");

            migrationBuilder.DropTable(
                name: "MarketplaceProfiles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_LedgerLines",
                table: "LedgerLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LedgerLines_PositiveAmounts",
                table: "LedgerLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LedgerLines_SingleSide",
                table: "LedgerLines");

            migrationBuilder.DropPrimaryKey(
                name: "PK_LedgerJournalNumberSequences",
                table: "LedgerJournalNumberSequences");

            migrationBuilder.DropPrimaryKey(
                name: "PK_LedgerJournalEntries",
                table: "LedgerJournalEntries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_LedgerAccounts",
                table: "LedgerAccounts");

            migrationBuilder.DropColumn(
                name: "FulfillmentCarrierCode",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FulfillmentMode",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FulfillmentStatus",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "LedgerAccounts");

            migrationBuilder.RenameTable(
                name: "LedgerLines",
                newName: "LedgerEntries");

            migrationBuilder.RenameTable(
                name: "LedgerJournalNumberSequences",
                newName: "JournalNumberSequences");

            migrationBuilder.RenameTable(
                name: "LedgerJournalEntries",
                newName: "JournalEntries");

            migrationBuilder.RenameTable(
                name: "LedgerAccounts",
                newName: "Accounts");

            migrationBuilder.RenameIndex(
                name: "IX_LedgerLines_TenantAcct",
                table: "LedgerEntries",
                newName: "IX_LedgerEntries_TenantId_AccountId");

            migrationBuilder.RenameIndex(
                name: "IX_LedgerLines_Tenant",
                table: "LedgerEntries",
                newName: "IX_LedgerEntries_TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_LedgerLines_Journal",
                table: "LedgerEntries",
                newName: "IX_LedgerEntries_JournalEntryId");

            migrationBuilder.RenameIndex(
                name: "IX_LedgerLines_AcctDate",
                table: "LedgerEntries",
                newName: "IX_LedgerEntries_AccountId_CreatedAtUtc");

            migrationBuilder.RenameIndex(
                name: "IX_LedgerJournalEntries_ReversedByJournalId",
                table: "JournalEntries",
                newName: "IX_JournalEntries_ReversedByJournalId");

            migrationBuilder.RenameIndex(
                name: "IX_LedgerJournal_Tenant",
                table: "JournalEntries",
                newName: "IX_JournalEntries_TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_LedgerJournal_Status",
                table: "JournalEntries",
                newName: "IX_JournalEntries_TenantId_Status");

            migrationBuilder.RenameIndex(
                name: "IX_LedgerJournal_Source",
                table: "JournalEntries",
                newName: "IX_JournalEntries_TenantId_Source");

            migrationBuilder.RenameIndex(
                name: "IX_LedgerJournal_Posted",
                table: "JournalEntries",
                newName: "IX_JournalEntries_PostedAtUtc");

            migrationBuilder.RenameIndex(
                name: "IX_LedgerJournal_Number",
                table: "JournalEntries",
                newName: "IX_JournalEntries_TenantId_JournalNumber");

            migrationBuilder.RenameIndex(
                name: "IX_LedgerJournal_Idempotency",
                table: "JournalEntries",
                newName: "IX_JournalEntries_TenantId_IdempotencyKey");

            migrationBuilder.RenameIndex(
                name: "IX_LedgerJournal_Date",
                table: "JournalEntries",
                newName: "IX_JournalEntries_TenantId_JournalDate");

            migrationBuilder.RenameIndex(
                name: "IX_LedgerAcct_TenantType",
                table: "Accounts",
                newName: "IX_Accounts_TenantId_AccountType");

            migrationBuilder.RenameIndex(
                name: "IX_LedgerAcct_TenantCode",
                table: "Accounts",
                newName: "IX_Accounts_TenantId_AccountCode");

            migrationBuilder.RenameIndex(
                name: "IX_LedgerAcct_Tenant",
                table: "Accounts",
                newName: "IX_Accounts_TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_LedgerAcct_Parent",
                table: "Accounts",
                newName: "IX_Accounts_ParentAccountId");

            migrationBuilder.RenameIndex(
                name: "IX_LedgerAcct_Clearing",
                table: "Accounts",
                newName: "IX_Accounts_TenantId_IsClearing");

            migrationBuilder.AddPrimaryKey(
                name: "PK_LedgerEntries",
                table: "LedgerEntries",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_JournalNumberSequences",
                table: "JournalNumberSequences",
                column: "TenantId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_JournalEntries",
                table: "JournalEntries",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Accounts",
                table: "Accounts",
                column: "Id");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LedgerEntries_PositiveAmounts",
                table: "LedgerEntries",
                sql: "\"DebitAmount\" >= 0 AND \"CreditAmount\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LedgerEntries_SingleSide",
                table: "LedgerEntries",
                sql: "(\"DebitAmount\" > 0 AND \"CreditAmount\" = 0) OR (\"DebitAmount\" = 0 AND \"CreditAmount\" > 0)");

            migrationBuilder.AddForeignKey(
                name: "FK_Accounts_Accounts_ParentAccountId",
                table: "Accounts",
                column: "ParentAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntries_JournalEntries_ReversedByJournalId",
                table: "JournalEntries",
                column: "ReversedByJournalId",
                principalTable: "JournalEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LedgerEntries_Accounts_AccountId",
                table: "LedgerEntries",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LedgerEntries_JournalEntries_JournalEntryId",
                table: "LedgerEntries",
                column: "JournalEntryId",
                principalTable: "JournalEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
