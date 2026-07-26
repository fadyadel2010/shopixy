using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Baseline_Recovery3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountingOutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    BusinessEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityTypeName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AggregateVersion = table.Column<int>(type: "integer", nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    JournalId = table.Column<Guid>(type: "uuid", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ClaimedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NextRetryAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsFailed = table.Column<bool>(type: "boolean", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingOutboxMessages", x => x.Id);
                    table.CheckConstraint("CK_AccountingOutbox_OrderFinancialEvents_AggregateVersion", "\"EventType\" NOT IN ('OnlinePayment', 'CodDelivered', 'WalletApproved', 'EcommerceRefund') OR (\"AggregateVersion\" IS NOT NULL AND \"AggregateVersion\" > 0)");
                });

            migrationBuilder.CreateTable(
                name: "AffiliateAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferralCode = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SuspendedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SuspensionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PayoutWalletNumber = table.Column<string>(type: "text", nullable: true),
                    PayoutWalletType = table.Column<string>(type: "text", nullable: true),
                    PayoutWalletUpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliateAccounts", x => x.Id);
                    table.CheckConstraint("CK_AffiliateAccounts_Status", "\"Status\" IN (0, 1, 2, 3)");
                });

            migrationBuilder.CreateTable(
                name: "AffiliateCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliateCategories", x => x.Id);
                    table.CheckConstraint("CK_AffiliateCategories_Level", "\"Level\" IN (1, 2)");
                    table.CheckConstraint("CK_AffiliateCategories_Level1_NoParent", "(\"Level\" = 1 AND \"ParentId\" IS NULL) OR (\"Level\" = 2 AND \"ParentId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_AffiliateCategories_AffiliateCategories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "AffiliateCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

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
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RejectedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    PaidBy = table.Column<Guid>(type: "uuid", nullable: true),
                    AdminNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PaymentReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    PayoutWalletNumberSnapshot = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PayoutWalletTypeSnapshot = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PayoutWalletUpdatedAtUtcSnapshot = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PayoutWalletSnapshotCapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliatePayoutRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AffiliatePayoutTransferEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RailNamespace = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PaymentReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedPaymentReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExpectedAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    AllocatedAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VerificationSource = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    VerificationFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastAllocationAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliatePayoutTransferEvidence", x => x.Id);
                    table.CheckConstraint("CK_AffiliatePayoutTransferEvidence_AllocatedAmount_Valid", "\"AllocatedAmount\" >= 0 AND \"AllocatedAmount\" <= \"ExpectedAmount\"");
                    table.CheckConstraint("CK_AffiliatePayoutTransferEvidence_Currency_NotBlank", "btrim(\"Currency\", E' \\t\\n\\r') <> ''");
                    table.CheckConstraint("CK_AffiliatePayoutTransferEvidence_ExpectedAmount_Positive", "\"ExpectedAmount\" > 0");
                    table.CheckConstraint("CK_AffiliatePayoutTransferEvidence_NormalizedPaymentReference_~", "btrim(\"NormalizedPaymentReference\", E' \\t\\n\\r') <> ''");
                    table.CheckConstraint("CK_AffiliatePayoutTransferEvidence_PaymentReference_NotBlank", "btrim(\"PaymentReference\", E' \\t\\n\\r') <> ''");
                    table.CheckConstraint("CK_AffiliatePayoutTransferEvidence_RailNamespace_NotBlank", "btrim(\"RailNamespace\", E' \\t\\n\\r') <> ''");
                    table.CheckConstraint("CK_AffiliatePayoutTransferEvidence_Tenant_Required", "\"StoreTenantId\" <> '00000000-0000-0000-0000-000000000000'");
                });

            migrationBuilder.CreateTable(
                name: "AnalyticsAlertRules",
                columns: table => new
                {
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Metric = table.Column<int>(type: "integer", nullable: false),
                    Operator = table.Column<int>(type: "integer", nullable: false),
                    Threshold = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    WindowMinutes = table.Column<int>(type: "integer", nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CooldownMinutes = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalyticsAlertRules", x => x.RuleId);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    LogId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserNameSnapshot = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ActionType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    BeforeDataJson = table.Column<string>(type: "text", nullable: true),
                    AfterDataJson = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReferenceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    SuccessFlag = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.LogId);
                });

            migrationBuilder.CreateTable(
                name: "Banks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    BankName = table.Column<string>(type: "text", nullable: false),
                    BranchName = table.Column<string>(type: "text", nullable: true),
                    AccountNumber = table.Column<string>(type: "text", nullable: true),
                    IBAN = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    OpeningBalanceAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    OpeningBalanceDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Banks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CampaignCosts",
                columns: table => new
                {
                    CampaignCostId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Medium = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Campaign = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PeriodStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CostAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignCosts", x => x.CampaignCostId);
                });

            migrationBuilder.CreateTable(
                name: "CanonicalIdentityLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalIdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectType = table.Column<int>(type: "integer", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceSystem = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    LinkedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CanonicalIdentityLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CarrierAccountPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Mode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CustomsReadinessPublished = table.Column<bool>(type: "boolean", nullable: false),
                    ReadinessEvidenceReference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierAccountPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CarrierAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Environment = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ActiveCredentialReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    OperationalState = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    HealthState = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    HealthReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LastHealthEvaluatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SuspendedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DisabledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetiredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CarrierAdministrationAudit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    PreviousState = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    NextState = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierAdministrationAudit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CarrierConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    ApiKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ApiSecret = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AccountId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ApiUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UseSandbox = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsCod = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsTracking = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsWebhooks = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsLabels = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsQuotes = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsCancellation = table.Column<bool>(type: "boolean", nullable: false),
                    Configuration = table.Column<string>(type: "text", nullable: true),
                    WebhookUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    WebhookSecret = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PickupAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PickupCity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PickupCountry = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    PickupPhone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PickupContactName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AvailableServiceLevels = table.Column<string>(type: "text", nullable: true),
                    DefaultServiceLevel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UseCarrierRates = table.Column<bool>(type: "boolean", nullable: false),
                    FixedShippingCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    MarkupPercentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    FreeShippingThreshold = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LastValidatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastValidationSucceeded = table.Column<bool>(type: "boolean", nullable: true),
                    LastValidationError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CarrierConnectionTestRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CredentialReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Environment = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierConnectionTestRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CarrierCredentialReferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecretReference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    MaskedFingerprint = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    State = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierCredentialReferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CarrierProviders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SupportsGeographicMappings = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresMappingForDelivery = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierProviders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CarrierServiceLevelEnablements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceLevelCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierServiceLevelEnablements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CarrierWarehouseAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    OriginReadinessPublished = table.Column<bool>(type: "boolean", nullable: false),
                    ReadinessEvidenceReference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierWarehouseAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ParentCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Categories_Categories_ParentCategoryId",
                        column: x => x.ParentCategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CmsPages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PageGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    IsDraft = table.Column<bool>(type: "boolean", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MetaTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    MetaDescription = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    MetaKeywords = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OpenGraphImageUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CmsPages", x => x.Id);
                    table.CheckConstraint("CK_CmsPages_ContentType", "\"ContentType\" IN ('RichText', 'Blocks')");
                    table.CheckConstraint("CK_CmsPages_PublishedAt", "NOT \"IsPublished\" OR \"PublishedAt\" IS NOT NULL");
                    table.CheckConstraint("CK_CmsPages_SchemaVersion", "\"SchemaVersion\" > 0");
                    table.CheckConstraint("CK_CmsPages_State", "NOT (\"IsDraft\" AND \"IsPublished\")");
                    table.CheckConstraint("CK_CmsPages_Version", "\"Version\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubCarts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommerceHubCustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    OnlineCustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemsJson = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    LockedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubCarts", x => x.Id);
                    table.CheckConstraint("CK_CommerceHubCarts_CustomerAuthorityBoundary", "\"CommerceHubCustomerId\" IS NOT NULL AND \"CommerceHubCustomerId\" <> '00000000-0000-0000-0000-000000000000' AND \"OnlineCustomerId\" IS NOT NULL AND \"OnlineCustomerId\" <> '00000000-0000-0000-0000-000000000000' AND \"OnlineCustomerId\" = \"CommerceHubCustomerId\"");
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    IconEmoji = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommerceHubCategories_CommerceHubCategories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "CommerceHubCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubCheckoutIdempotency",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CommerceHubCustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    OnlineCustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResponseJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubCheckoutIdempotency", x => x.Id);
                    table.CheckConstraint("CK_CommerceHubCheckoutIdempotency_CustomerAuthorityBoundary", "\"CommerceHubCustomerId\" IS NOT NULL AND \"CommerceHubCustomerId\" <> '00000000-0000-0000-0000-000000000000' AND \"OnlineCustomerId\" IS NULL");
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubCustomers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    TokensValidAfterUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsEmailVerified = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    EmailVerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubCustomers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubDailyFinancialAggregates",
                columns: table => new
                {
                    DateUtc = table.Column<DateTime>(type: "date", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    BaseCurrencyCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "EGP"),
                    ExchangeRateToBase = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    GrossSales = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    GrossCommission = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PromoCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PromoReversalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RefundAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RefundImpactOnRevenue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NetRevenue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    GrossSalesBase = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    GrossCommissionBase = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    PromoCostBase = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    PromoReversalAmountBase = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    RefundAmountBase = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    RefundImpactOnRevenueBase = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    NetRevenueBase = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    OrdersCount = table.Column<int>(type: "integer", nullable: false),
                    RefundCount = table.Column<int>(type: "integer", nullable: false),
                    RefundedOrdersCount = table.Column<int>(type: "integer", nullable: false),
                    IsValid = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    LastValidatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ValidationError = table.Column<string>(type: "text", nullable: true),
                    AggregatedThroughUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CalculatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubDailyFinancialAggregates", x => new { x.DateUtc, x.CurrencyCode });
                    table.CheckConstraint("CK_CommerceHubDailyFinancialAggregates_BaseCurrencyCode_EGP", "\"BaseCurrencyCode\" = 'EGP'");
                    table.CheckConstraint("CK_CommerceHubDailyFinancialAggregates_CurrencyCode_EGP", "\"CurrencyCode\" = 'EGP'");
                    table.CheckConstraint("CK_CommerceHubDailyFinancialAggregates_GrossCommission", "\"GrossCommission\" >= 0");
                    table.CheckConstraint("CK_CommerceHubDailyFinancialAggregates_GrossSales", "\"GrossSales\" >= 0");
                    table.CheckConstraint("CK_CommerceHubDailyFinancialAggregates_IsValid", "\"IsValid\" IN (TRUE, FALSE)");
                    table.CheckConstraint("CK_CommerceHubDailyFinancialAggregates_OrdersCount", "\"OrdersCount\" >= 0");
                    table.CheckConstraint("CK_CommerceHubDailyFinancialAggregates_PromoCost", "\"PromoCost\" >= 0");
                    table.CheckConstraint("CK_CommerceHubDailyFinancialAggregates_PromoReversalAmount", "\"PromoReversalAmount\" >= 0");
                    table.CheckConstraint("CK_CommerceHubDailyFinancialAggregates_RefundAmount", "\"RefundAmount\" >= 0");
                    table.CheckConstraint("CK_CommerceHubDailyFinancialAggregates_RefundCount", "\"RefundCount\" >= 0");
                    table.CheckConstraint("CK_CommerceHubDailyFinancialAggregates_RefundedOrdersCount", "\"RefundedOrdersCount\" >= 0");
                    table.CheckConstraint("CK_CommerceHubDailyFinancialAggregates_RefundImpactOnRevenue", "\"RefundImpactOnRevenue\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubFinancialAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    MarketplaceOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    EarningId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayoutId = table.Column<Guid>(type: "uuid", nullable: true),
                    RefundId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Debit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Credit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubFinancialAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubFinancialGuardLocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubFinancialGuardLocks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubFinancialOperationAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OperationType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    BeforePlatformCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BeforeVendorPayable = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BeforeVendorDebt = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AfterPlatformCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AfterVendorPayable = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AfterVendorDebt = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReferenceType = table.Column<int>(type: "integer", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubFinancialOperationAudits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubFinancialReconciliationReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MismatchCount = table.Column<int>(type: "integer", nullable: false),
                    DetailsJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubFinancialReconciliationReports", x => x.Id);
                    table.CheckConstraint("CK_CommerceHubFinancialReconciliationReports_MismatchCount", "\"MismatchCount\" >= 0");
                    table.CheckConstraint("CK_CommerceHubFinancialReconciliationReports_Status", "\"Status\" IN ('Healthy', 'Warning', 'Critical')");
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubFinancialTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<int>(type: "integer", nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransactionType = table.Column<int>(type: "integer", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    FinancialBucket = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "EGP"),
                    ReferenceType = table.Column<int>(type: "integer", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubFinancialTransactions", x => x.Id);
                    table.CheckConstraint("CK_CommerceHubFinancialTransactions_Amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_CommerceHubFinancialTransactions_Currency_EGP", "\"Currency\" = 'EGP'");
                    table.CheckConstraint("CK_CommerceHubFinancialTransactions_Direction", "\"Direction\" IN (0, 1)");
                    table.CheckConstraint("CK_CommerceHubFinancialTransactions_EntityScope", "(\"EntityType\" = 0 AND \"EntityId\" IS NULL) OR (\"EntityType\" = 1 AND \"EntityId\" IS NOT NULL)");
                    table.CheckConstraint("CK_CommerceHubFinancialTransactions_FinancialBucket", "\"FinancialBucket\" IN (0, 1, 2)");
                    table.CheckConstraint("CK_CommerceHubFinancialTransactions_ReferenceType", "\"ReferenceType\" IN (0, 1, 2, 3, 4)");
                    table.CheckConstraint("CK_CommerceHubFinancialTransactions_TransactionType", "\"TransactionType\" IN (0, 1, 2, 3, 4, 5, 6)");
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubMerchantLifecycleLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorIdentity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    DetailsJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubMerchantLifecycleLedgerEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubPaymentCompensationOutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    AffectedStoreIdsJson = table.Column<string>(type: "text", nullable: false),
                    StoreAllocationSnapshotJson = table.Column<string>(type: "text", nullable: false),
                    RefundAllocationSnapshotJson = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    ProviderIntentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProviderTransactionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NextRetryAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessingLeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcessingLeaseExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsFailed = table.Column<bool>(type: "boolean", nullable: false),
                    GatewayReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    GatewayStatus = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    GatewayRequestStartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GatewayOutcomeUncertainAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RequiresOperatorReconciliation = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubPaymentCompensationOutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubRecoveryOperationLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ActorIdentity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ListingId = table.Column<Guid>(type: "uuid", nullable: true),
                    Scope = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    DetailsJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubRecoveryOperationLedgerEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubUnresolvedPaymentWebhooks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    ProviderEventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LocalPaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderIntentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProviderTransactionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    AffectedStoreIdsJson = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    SuspenseRoutedStoreIdsJson = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RawPayload = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ResolutionAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastResolutionAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RoutedToSuspenseAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubUnresolvedPaymentWebhooks", x => x.Id);
                    table.CheckConstraint("CK_CommerceHubUnresolvedPaymentWebhooks_Amount_NonNegative", "\"Amount\" >= 0");
                    table.CheckConstraint("CK_CommerceHubUnresolvedPaymentWebhooks_Currency_EGP", "\"Currency\" = 'EGP'");
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubVendorPaymentMethods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DestinationValue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Instructions = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubVendorPaymentMethods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubVendorPayoutMethods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: false),
                    MethodType = table.Column<int>(type: "integer", nullable: false),
                    DestinationValue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsVerified = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubVendorPayoutMethods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubVendorSettlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: false),
                    SettlementType = table.Column<int>(type: "integer", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "EGP"),
                    IdempotencyKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    RecordedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordedByUserEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubVendorSettlements", x => x.Id);
                    table.CheckConstraint("CK_CommerceHubVendorSettlements_Amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_CommerceHubVendorSettlements_Currency_EGP", "\"Currency\" = 'EGP'");
                    table.CheckConstraint("CK_CommerceHubVendorSettlements_Direction", "\"Direction\" IN (0, 1)");
                    table.CheckConstraint("CK_CommerceHubVendorSettlements_ExternalReference_NotBlank", "NULLIF(btrim(\"ExternalReference\"), '') IS NOT NULL");
                    table.CheckConstraint("CK_CommerceHubVendorSettlements_IdempotencyKey_NotBlank", "NULLIF(btrim(\"IdempotencyKey\"), '') IS NOT NULL");
                    table.CheckConstraint("CK_CommerceHubVendorSettlements_SettlementType", "\"SettlementType\" IN (0, 1, 2)");
                });

            migrationBuilder.CreateTable(
                name: "CommissionRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeType = table.Column<int>(type: "integer", nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Rate = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Priority = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommissionRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Coupons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    NormalizedCode = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    DiscountType = table.Column<int>(type: "integer", nullable: false),
                    DiscountValue = table.Column<decimal>(type: "numeric", nullable: false),
                    MinOrderAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    MaxDiscountAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    MaxUsage = table.Column<int>(type: "integer", nullable: true),
                    CurrentUsage = table.Column<int>(type: "integer", nullable: false),
                    MaxUsagePerCustomer = table.Column<int>(type: "integer", nullable: true),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    FirstOrderOnly = table.Column<bool>(type: "boolean", nullable: false),
                    ApplicableProductIds = table.Column<string>(type: "text", nullable: true),
                    ApplicableCategoryIds = table.Column<string>(type: "text", nullable: true),
                    ExcludedProductIds = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Coupons", x => x.Id);
                    table.CheckConstraint("CK_Coupons_DiscountType", "\"DiscountType\" IN (0, 1)");
                    table.CheckConstraint("CK_Coupons_DiscountValue_FixedAmount", "\"DiscountType\" <> 1 OR \"DiscountValue\" <= 1000000.00");
                    table.CheckConstraint("CK_Coupons_DiscountValue_Percentage", "\"DiscountType\" <> 0 OR \"DiscountValue\" <= 100");
                    table.CheckConstraint("CK_Coupons_DiscountValue_Positive", "\"DiscountValue\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "Currencies",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Symbol = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    DecimalPlaces = table.Column<int>(type: "integer", nullable: false, defaultValue: 2)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Currencies", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Address = table.Column<string>(type: "text", nullable: true),
                    TaxNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpeningBalanceAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    BalanceType = table.Column<string>(type: "text", nullable: true),
                    OpeningBalanceDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomsAdministrationAudit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CustomsEvidenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomsEvidenceVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Actor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ValidationChecksumSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    FromStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomsAdministrationAudit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomsAdmissionAudit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CustomsEvidenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomsEvidenceVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShipmentCustomsSnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Actor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceEvidenceFingerprintSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomsAdmissionAudit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomsEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CurrentVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentVersionNumber = table.Column<int>(type: "integer", nullable: false),
                    AdmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AdmittedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedByActor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LastEditedByActor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SubmittedByActor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomsEvidence", x => x.Id);
                    table.CheckConstraint("CK_CustomsEvidence_CurrentVersion", "\"CurrentVersionNumber\" > 0");
                    table.CheckConstraint("CK_CustomsEvidence_SourceKind", "\"SourceKind\" IN ('Product','ProductVariant','OrderLine','System')");
                    table.CheckConstraint("CK_CustomsEvidence_Status", "\"Status\" IN ('Candidate','Admitted','Draft','UnderReview','Approved','Published','Rejected','Reopened','Retired','Superseded')");
                });

            migrationBuilder.CreateTable(
                name: "CustomsEvidenceApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomsEvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomsEvidenceVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    ValidationChecksumSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ApprovedByActor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomsEvidenceApprovals", x => x.Id);
                    table.CheckConstraint("CK_CustomsEvidenceApproval_Checksum", "char_length(\"ValidationChecksumSha256\") = 64");
                });

            migrationBuilder.CreateTable(
                name: "CustomsEvidenceFingerprints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomsEvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomsEvidenceVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FingerprintSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomsEvidenceFingerprints", x => x.Id);
                    table.CheckConstraint("CK_CustomsEvidenceFingerprint_Length", "char_length(\"FingerprintSha256\") = 64");
                });

            migrationBuilder.CreateTable(
                name: "CustomsEvidenceHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomsEvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomsEvidenceVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Actor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomsEvidenceHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomsEvidencePublications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomsEvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomsEvidenceVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupersededCustomsEvidenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupersededCustomsEvidenceVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ValidationChecksumSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    PublishedByActor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomsEvidencePublications", x => x.Id);
                    table.CheckConstraint("CK_CustomsEvidencePublication_Action", "\"Action\" IN ('Published','Retired','Superseded')");
                    table.CheckConstraint("CK_CustomsEvidencePublication_Checksum", "char_length(\"ValidationChecksumSha256\") = 64");
                });

            migrationBuilder.CreateTable(
                name: "CustomsPublicationLocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LockKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    LockedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LockedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LockedByActor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomsPublicationLocks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpenseCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FawryWebhookReplayRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderEventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EventType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EvidenceReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProcessingTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaymentIntentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    AdmissionClassification = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FawryWebhookReplayRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinancialAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountCode = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    AccountNumber = table.Column<string>(type: "text", nullable: true),
                    AccountType = table.Column<int>(type: "integer", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    Balance = table.Column<decimal>(type: "numeric", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    ParentAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialAccounts_FinancialAccounts_ParentAccountId",
                        column: x => x.ParentAccountId,
                        principalTable: "FinancialAccounts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "GeographicCatalogReleases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReleaseNumber = table.Column<long>(type: "bigint", nullable: false),
                    ReleaseKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CountryScopeIso2 = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: true),
                    SourceSummary = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ChecksumSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublishedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupersedesReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    RollbackOfReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeographicCatalogReleases", x => x.Id);
                    table.CheckConstraint("CK_GeoCatalogRelease_Checksum", "char_length(\"ChecksumSha256\") = 64");
                    table.CheckConstraint("CK_GeoCatalogRelease_Status", "\"Status\" IN ('Draft','Validated','Published','Superseded','RolledBack','Rejected')");
                    table.ForeignKey(
                        name: "FK_GeoCatalogRelease_RollbackOf",
                        column: x => x.RollbackOfReleaseId,
                        principalTable: "GeographicCatalogReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeoCatalogRelease_Supersedes",
                        column: x => x.SupersedesReleaseId,
                        principalTable: "GeographicCatalogReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GlobalPhoneReputation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SpamReports = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    FirstReportedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastReportedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalPhoneReputation", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HomepageSections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Data = table.Column<string>(type: "jsonb", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    IsDraft = table.Column<bool>(type: "boolean", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DraftGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HomepageSections", x => x.Id);
                    table.CheckConstraint("CK_HomepageSections_Order", "\"Order\" >= 0");
                    table.CheckConstraint("CK_HomepageSections_SchemaVersion", "\"SchemaVersion\" > 0");
                    table.CheckConstraint("CK_HomepageSections_State", "NOT (\"IsDraft\" AND \"IsPublished\")");
                    table.CheckConstraint("CK_HomepageSections_Type", "\"Type\" IN ('hero', 'products', 'banner', 'categories')");
                    table.CheckConstraint("CK_HomepageSections_Version", "\"Version\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "InboxState",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uuid", nullable: false),
                    LockId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true),
                    Received = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceiveCount = table.Column<int>(type: "integer", nullable: false),
                    ExpirationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Consumed = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Delivered = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSequenceNumber = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxState", x => x.Id);
                    table.UniqueConstraint("AK_InboxState_MessageId_ConsumerId", x => new { x.MessageId, x.ConsumerId });
                });

            migrationBuilder.CreateTable(
                name: "InventoryEventInbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReferenceLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayloadHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    FirstReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AppliedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReplayCount = table.Column<int>(type: "integer", nullable: false),
                    StockMovementId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryEventInbox", x => x.Id);
                    table.CheckConstraint("CK_InventoryEventInbox_ReplayCount_NonNegative", "\"ReplayCount\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "InventoryReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservedQuantity = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CommittedQuantity = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReleasedQuantity = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReturnedQuantity = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DamagedReturnedQuantity = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReservationEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastMutationEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryReservations", x => x.Id);
                    table.CheckConstraint("CK_InventoryReservations_CommittedQuantity_NonNegative", "\"CommittedQuantity\" >= 0");
                    table.CheckConstraint("CK_InventoryReservations_DamagedReturnedQuantity_NonNegative", "\"DamagedReturnedQuantity\" >= 0");
                    table.CheckConstraint("CK_InventoryReservations_ReleasedQuantity_NonNegative", "\"ReleasedQuantity\" >= 0");
                    table.CheckConstraint("CK_InventoryReservations_ReservedFlow_Valid", "\"CommittedQuantity\" + \"ReleasedQuantity\" <= \"ReservedQuantity\"");
                    table.CheckConstraint("CK_InventoryReservations_ReservedQuantity_NonNegative", "\"ReservedQuantity\" >= 0");
                    table.CheckConstraint("CK_InventoryReservations_ReturnedQuantity_NonNegative", "\"ReturnedQuantity\" >= 0");
                    table.CheckConstraint("CK_InventoryReservations_Returns_DoNotExceedCommitted", "\"ReturnedQuantity\" + \"DamagedReturnedQuantity\" <= \"CommittedQuantity\"");
                });

            migrationBuilder.CreateTable(
                name: "LedgerAccounts",
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
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerAccounts", x => x.Id);
                    table.UniqueConstraint("AK_LedgerAccounts_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_LedgerAccounts_LedgerAccounts_ParentAccountId",
                        column: x => x.ParentAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LedgerJournalEntries",
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
                    table.PrimaryKey("PK_LedgerJournalEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LedgerJournalEntries_LedgerJournalEntries_ReversedByJournal~",
                        column: x => x.ReversedByJournalId,
                        principalTable: "LedgerJournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LedgerJournalNumberSequences",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentYear = table.Column<int>(type: "integer", nullable: false),
                    NextNumber = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerJournalNumberSequences", x => x.TenantId);
                });

            migrationBuilder.CreateTable(
                name: "MarketplaceCmsPages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PageGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TitleJson = table.Column<string>(type: "jsonb", nullable: true),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ContentJson = table.Column<string>(type: "jsonb", nullable: true),
                    ContentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsDraft = table.Column<bool>(type: "boolean", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MetaTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    MetaTitleJson = table.Column<string>(type: "jsonb", nullable: true),
                    MetaDescription = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    MetaDescriptionJson = table.Column<string>(type: "jsonb", nullable: true),
                    OpenGraphImageUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceCmsPages", x => x.Id);
                    table.CheckConstraint("CK_MarketplaceCmsPages_ContentType", "\"ContentType\" IN ('RichText', 'Blocks')");
                    table.CheckConstraint("CK_MarketplaceCmsPages_PublishedAt", "NOT \"IsPublished\" OR \"PublishedAt\" IS NOT NULL");
                    table.CheckConstraint("CK_MarketplaceCmsPages_State", "NOT (\"IsDraft\" AND \"IsPublished\")");
                    table.CheckConstraint("CK_MarketplaceCmsPages_Version", "\"Version\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "MarketplaceEventOutboxDeadLetterArchives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalOutboxMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClrType = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    AggregateRootId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FailedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ArchivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ArchiveReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceEventOutboxDeadLetterArchives", x => x.Id);
                    table.CheckConstraint("CK_MarketplaceEventOutboxDeadLetterArchive_OriginalId_Required", "\"OriginalOutboxMessageId\" <> '00000000-0000-0000-0000-000000000000'");
                });

            migrationBuilder.CreateTable(
                name: "MarketplaceEventOutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClrType = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    AggregateRootId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClaimedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_MarketplaceEventOutboxMessages", x => x.Id);
                    table.CheckConstraint("CK_MarketplaceEventOutbox_AggregateRootId_NotEmpty", "\"AggregateRootId\" IS NOT NULL AND \"AggregateRootId\" <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_MarketplaceEventOutbox_TenantId_Required", "\"TenantId\" IS NOT NULL AND \"TenantId\" <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_MarketplaceEventOutbox_Version_Positive", "\"Version\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "MarketplaceFooterSections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    TitleJson = table.Column<string>(type: "jsonb", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceFooterSections", x => x.Id);
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
                name: "MarketplaceProjectionOutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EventType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClrType = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EventVersion = table.Column<int>(type: "integer", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClaimedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
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
                    table.PrimaryKey("PK_MarketplaceProjectionOutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MarketplaceSocialLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Icon = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceSocialLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OnboardingChecklists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemsJson = table.Column<string>(type: "text", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DismissedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnboardingChecklists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OnlineCustomers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProviderUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    EmailVerified = table.Column<bool>(type: "boolean", nullable: false),
                    EmailVerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PhoneVerified = table.Column<bool>(type: "boolean", nullable: false),
                    PhoneVerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastLoginAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TokensValidAfterUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnlineCustomers", x => x.Id);
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
                name: "OrderRequestIdempotency",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SnapshotHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResponseStatusCode = table.Column<int>(type: "integer", nullable: true),
                    ResponsePayload = table.Column<string>(type: "text", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClaimToken = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ClaimTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderRequestIdempotency", x => x.Id);
                });

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
                    EventVersion = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    LastErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RequiresOperatorReconciliation = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    OperatorReconciliationRequiredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OperatorReconciliationReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LastErrorAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                    table.CheckConstraint("CK_OutboxMessages_CriticalEvents_AggregateId_Required", "(\"AggregateId\" IS NOT NULL AND \"AggregateId\" <> '00000000-0000-0000-0000-000000000000') OR NOT ((lower(\"EventType\") LIKE '%order%' OR lower(\"EventType\") LIKE '%payment%' OR lower(\"EventType\") LIKE '%inventory%' OR lower(\"EventType\") LIKE '%stock%' OR lower(\"EventType\") LIKE '%product%' OR lower(\"EventType\") LIKE '%variant%' OR lower(\"EventType\") LIKE '%commercehub%' OR lower(\"EventType\") LIKE '%marketplace%' OR lower(\"EventType\") LIKE '%affiliate%' OR lower(\"EventType\") LIKE '%coupon%' OR lower(\"EventType\") LIKE '%promotion%' OR lower(\"EventType\") LIKE '%tenant%' OR lower(\"EventType\") LIKE '%storeconfig%' OR lower(\"EventType\") LIKE '%shippingmethod%' OR lower(\"EventType\") LIKE '%shipment%' OR lower(\"EventType\") LIKE '%customer%' OR lower(\"EventType\") LIKE '%checkout%' OR lower(\"EventType\") LIKE '%refund%' OR lower(\"EventType\") LIKE '%payout%' OR lower(\"EventType\") LIKE '%settlement%' OR lower(\"EventType\") LIKE '%ledger%' OR lower(\"EventType\") LIKE '%accounting%' OR lower(\"EventType\") LIKE '%wallet%' OR lower(\"EventType\") LIKE '%cod%' OR lower(\"EventType\") LIKE '%currency%' OR lower(\"EventType\") LIKE '%providertrust%') AND lower(\"EventType\") NOT LIKE '%affiliatelinkclickedevent%' AND lower(\"EventType\") NOT LIKE '%lowstockalertevent%' AND lower(\"EventType\") NOT LIKE '%onlinecustomeremailverificationrequestedevent%' AND lower(\"EventType\") NOT LIKE '%promotionseenevent%' AND lower(\"EventType\") NOT LIKE '%promotionclickedevent%' AND lower(\"EventType\") NOT LIKE '%promotionconvertedevent%' AND lower(\"EventType\") NOT LIKE '%promotionboostappliedevent%' AND lower(\"EventType\") NOT LIKE '%whatsapporderconfirmationrequestedevent%' AND lower(\"EventType\") NOT LIKE '%whatsappotprequestedevent%' AND lower(\"EventType\") NOT LIKE '%whatsapppaymentapprovalrequestedevent%' AND lower(\"EventType\") NOT LIKE '%whatsapppaymentapprovednotificationevent%' AND lower(\"EventType\") NOT LIKE '%whatsapppaymentrejectednotificationevent%')");
                    table.CheckConstraint("CK_OutboxMessages_CriticalEvents_EventVersion_Required", "\"EventVersion\" > 0 OR NOT ((lower(\"EventType\") LIKE '%order%' OR lower(\"EventType\") LIKE '%payment%' OR lower(\"EventType\") LIKE '%inventory%' OR lower(\"EventType\") LIKE '%stock%' OR lower(\"EventType\") LIKE '%product%' OR lower(\"EventType\") LIKE '%variant%' OR lower(\"EventType\") LIKE '%commercehub%' OR lower(\"EventType\") LIKE '%marketplace%' OR lower(\"EventType\") LIKE '%affiliate%' OR lower(\"EventType\") LIKE '%coupon%' OR lower(\"EventType\") LIKE '%promotion%' OR lower(\"EventType\") LIKE '%tenant%' OR lower(\"EventType\") LIKE '%storeconfig%' OR lower(\"EventType\") LIKE '%shippingmethod%' OR lower(\"EventType\") LIKE '%shipment%' OR lower(\"EventType\") LIKE '%customer%' OR lower(\"EventType\") LIKE '%checkout%' OR lower(\"EventType\") LIKE '%refund%' OR lower(\"EventType\") LIKE '%payout%' OR lower(\"EventType\") LIKE '%settlement%' OR lower(\"EventType\") LIKE '%ledger%' OR lower(\"EventType\") LIKE '%accounting%' OR lower(\"EventType\") LIKE '%wallet%' OR lower(\"EventType\") LIKE '%cod%' OR lower(\"EventType\") LIKE '%currency%' OR lower(\"EventType\") LIKE '%providertrust%') AND lower(\"EventType\") NOT LIKE '%affiliatelinkclickedevent%' AND lower(\"EventType\") NOT LIKE '%lowstockalertevent%' AND lower(\"EventType\") NOT LIKE '%onlinecustomeremailverificationrequestedevent%' AND lower(\"EventType\") NOT LIKE '%promotionseenevent%' AND lower(\"EventType\") NOT LIKE '%promotionclickedevent%' AND lower(\"EventType\") NOT LIKE '%promotionconvertedevent%' AND lower(\"EventType\") NOT LIKE '%promotionboostappliedevent%' AND lower(\"EventType\") NOT LIKE '%whatsapporderconfirmationrequestedevent%' AND lower(\"EventType\") NOT LIKE '%whatsappotprequestedevent%' AND lower(\"EventType\") NOT LIKE '%whatsapppaymentapprovalrequestedevent%' AND lower(\"EventType\") NOT LIKE '%whatsapppaymentapprovednotificationevent%' AND lower(\"EventType\") NOT LIKE '%whatsapppaymentrejectednotificationevent%')");
                    table.CheckConstraint("CK_OutboxMessages_CriticalEvents_TenantId_Required", "(\"TenantId\" IS NOT NULL AND \"TenantId\" <> '00000000-0000-0000-0000-000000000000') OR NOT ((lower(\"EventType\") LIKE '%order%' OR lower(\"EventType\") LIKE '%payment%' OR lower(\"EventType\") LIKE '%inventory%' OR lower(\"EventType\") LIKE '%stock%' OR lower(\"EventType\") LIKE '%product%' OR lower(\"EventType\") LIKE '%variant%' OR lower(\"EventType\") LIKE '%commercehub%' OR lower(\"EventType\") LIKE '%marketplace%' OR lower(\"EventType\") LIKE '%affiliate%' OR lower(\"EventType\") LIKE '%coupon%' OR lower(\"EventType\") LIKE '%promotion%' OR lower(\"EventType\") LIKE '%tenant%' OR lower(\"EventType\") LIKE '%storeconfig%' OR lower(\"EventType\") LIKE '%shippingmethod%' OR lower(\"EventType\") LIKE '%shipment%' OR lower(\"EventType\") LIKE '%customer%' OR lower(\"EventType\") LIKE '%checkout%' OR lower(\"EventType\") LIKE '%refund%' OR lower(\"EventType\") LIKE '%payout%' OR lower(\"EventType\") LIKE '%settlement%' OR lower(\"EventType\") LIKE '%ledger%' OR lower(\"EventType\") LIKE '%accounting%' OR lower(\"EventType\") LIKE '%wallet%' OR lower(\"EventType\") LIKE '%cod%' OR lower(\"EventType\") LIKE '%currency%' OR lower(\"EventType\") LIKE '%providertrust%') AND lower(\"EventType\") NOT LIKE '%affiliatelinkclickedevent%' AND lower(\"EventType\") NOT LIKE '%lowstockalertevent%' AND lower(\"EventType\") NOT LIKE '%onlinecustomeremailverificationrequestedevent%' AND lower(\"EventType\") NOT LIKE '%promotionseenevent%' AND lower(\"EventType\") NOT LIKE '%promotionclickedevent%' AND lower(\"EventType\") NOT LIKE '%promotionconvertedevent%' AND lower(\"EventType\") NOT LIKE '%promotionboostappliedevent%' AND lower(\"EventType\") NOT LIKE '%whatsapporderconfirmationrequestedevent%' AND lower(\"EventType\") NOT LIKE '%whatsappotprequestedevent%' AND lower(\"EventType\") NOT LIKE '%whatsapppaymentapprovalrequestedevent%' AND lower(\"EventType\") NOT LIKE '%whatsapppaymentapprovednotificationevent%' AND lower(\"EventType\") NOT LIKE '%whatsapppaymentrejectednotificationevent%')");
                });

            migrationBuilder.CreateTable(
                name: "OutboxState",
                columns: table => new
                {
                    OutboxId = table.Column<Guid>(type: "uuid", nullable: false),
                    LockId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Delivered = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSequenceNumber = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxState", x => x.OutboxId);
                });

            migrationBuilder.CreateTable(
                name: "PartyLedgerAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartyType = table.Column<int>(type: "integer", nullable: false),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionType = table.Column<int>(type: "integer", nullable: false),
                    AdjustmentDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ReferenceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartyLedgerAdjustments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentCancellationOutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentIntentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SkipPaymentIntentResolution = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClaimedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NextRetryAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsFailed = table.Column<bool>(type: "boolean", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentCancellationOutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentSettlementBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SettlementType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SettlementId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PayoutBatchId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    GrossAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Fees = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    BankReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OrderCount = table.Column<int>(type: "integer", nullable: false),
                    SettledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentSettlementBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymobWebhookReplayRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderEventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EventType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EvidenceReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProcessingTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaymentIntentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    AdmissionClassification = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymobWebhookReplayRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PhoneRiskProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SpamReports = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    IsBlocked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    BlockedReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BlockedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    BlockedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhoneRiskProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PhoneSpamReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhoneSpamReports", x => x.Id);
                });

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
                name: "ProcessedEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EventType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedEvents", x => x.Id);
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
                name: "ProductRequestIdempotency",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Operation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResponseStatusCode = table.Column<int>(type: "integer", nullable: true),
                    ResponseJson = table.Column<string>(type: "text", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductRequestIdempotency", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PromoCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DiscountType = table.Column<int>(type: "integer", nullable: false),
                    DiscountValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MaxDiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    UsageLimit = table.Column<int>(type: "integer", nullable: true),
                    MaxUsagePerCustomer = table.Column<int>(type: "integer", nullable: true),
                    UsedCount = table.Column<int>(type: "integer", nullable: false),
                    ExpiryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: true),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromoCodes", x => x.Id);
                    table.CheckConstraint("CK_PromoCodes_Code_Uppercase", "\"Code\" = UPPER(\"Code\")");
                    table.CheckConstraint("CK_PromoCodes_DiscountType", "\"DiscountType\" IN (0, 1)");
                    table.CheckConstraint("CK_PromoCodes_DiscountValue", "\"DiscountValue\" > 0");
                    table.CheckConstraint("CK_PromoCodes_MaxDiscountAmount", "\"MaxDiscountAmount\" IS NULL OR \"MaxDiscountAmount\" > 0");
                    table.CheckConstraint("CK_PromoCodes_MaxUsagePerCustomer", "\"MaxUsagePerCustomer\" IS NULL OR \"MaxUsagePerCustomer\" > 0");
                    table.CheckConstraint("CK_PromoCodes_MaxUsagePerCustomerWithinUsageLimit", "\"UsageLimit\" IS NULL OR \"MaxUsagePerCustomer\" IS NULL OR \"MaxUsagePerCustomer\" <= \"UsageLimit\"");
                    table.CheckConstraint("CK_PromoCodes_Scope", "(CASE WHEN \"VendorId\" IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN \"CategoryId\" IS NOT NULL THEN 1 ELSE 0 END) <= 1");
                    table.CheckConstraint("CK_PromoCodes_UsageLimit", "\"UsageLimit\" IS NULL OR \"UsageLimit\" > 0");
                    table.CheckConstraint("CK_PromoCodes_UsedCount", "\"UsedCount\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "Promotions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DiscountType = table.Column<int>(type: "integer", nullable: false),
                    DiscountValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MaxDiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    AppliesToId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsExclusive = table.Column<bool>(type: "boolean", nullable: false),
                    MinOrderAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    MaxUsage = table.Column<int>(type: "integer", nullable: true),
                    CurrentUsage = table.Column<int>(type: "integer", nullable: false),
                    BannerStyle = table.Column<string>(type: "text", nullable: false),
                    SearchBoostOverride = table.Column<double>(type: "double precision", nullable: true),
                    ProjectionMutationVersion = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promotions", x => x.Id);
                    table.CheckConstraint("CK_Promotions_DiscountType", "\"DiscountType\" IN (0, 1)");
                    table.CheckConstraint("CK_Promotions_DiscountValue_FixedAmount", "\"DiscountType\" <> 1 OR \"DiscountValue\" <= 1000000.00");
                    table.CheckConstraint("CK_Promotions_DiscountValue_Percentage", "\"DiscountType\" <> 0 OR \"DiscountValue\" <= 100");
                    table.CheckConstraint("CK_Promotions_DiscountValue_Positive", "\"DiscountValue\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "ProviderDeliveryOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RequestType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DeliveryKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    EndpointUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    HttpMethod = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    HeadersJson = table.Column<string>(type: "text", nullable: true),
                    TimeoutSeconds = table.Column<int>(type: "integer", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeliveredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClaimedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextRetryAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ReplayCount = table.Column<int>(type: "integer", nullable: false),
                    IsFailed = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeadLettered = table.Column<bool>(type: "boolean", nullable: false),
                    ReplayEligible = table.Column<bool>(type: "boolean", nullable: false),
                    DeadLetteredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureClass = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LastHttpStatusCode = table.Column<int>(type: "integer", nullable: true),
                    LastResponseBody = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    FailureMetadataJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderDeliveryOutbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderDeliveryReplayAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReplayId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OperatorUserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReplayedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ReplaySource = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PreviousStateJson = table.Column<string>(type: "text", nullable: false),
                    NewStateJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderDeliveryReplayAudits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseRequestIdempotency",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Operation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PurchaseInvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    PurchaseAttemptId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResponseStatusCode = table.Column<int>(type: "integer", nullable: true),
                    ResponseJson = table.Column<string>(type: "text", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseRequestIdempotency", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseReturnRequestIdempotency",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PurchaseAttemptId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReturnId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResponseStatusCode = table.Column<int>(type: "integer", nullable: true),
                    ResponseJson = table.Column<string>(type: "text", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReturnRequestIdempotency", x => x.Id);
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
                name: "SalesRepresentatives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    MonthlyTarget = table.Column<decimal>(type: "numeric", nullable: true),
                    CommissionPercentage = table.Column<decimal>(type: "numeric", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesRepresentatives", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SalesRequestIdempotency",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Operation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResponseStatusCode = table.Column<int>(type: "integer", nullable: true),
                    ResponseJson = table.Column<string>(type: "text", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClaimToken = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ClaimTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesRequestIdempotency", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SearchClicks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QueryText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    ClickedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchClicks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SearchFilterEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FilterKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FilterValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    QueryText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchFilterEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SearchImpressions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QueryText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    ImpressedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchImpressions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SearchQueries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QueryText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ResultCount = table.Column<long>(type: "bigint", nullable: false),
                    Intent = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AppliedExpansionsJson = table.Column<string>(type: "text", nullable: true),
                    SearchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchQueries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SearchSynonyms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Term = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SynonymsJson = table.Column<string>(type: "text", nullable: false),
                    Direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "one_way"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchSynonyms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShipmentCustomsSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotVersion = table.Column<int>(type: "integer", nullable: false),
                    CarrierNeutralDtoVersion = table.Column<int>(type: "integer", nullable: false),
                    DestinationCountryIso2 = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    OriginCountryIso2 = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    ShipmentGrossWeight = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    ShipmentGrossWeightUnit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SnapshotFingerprintSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    IsRegistrationFrozen = table.Column<bool>(type: "boolean", nullable: false),
                    RegistrationBeganAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentCustomsSnapshots", x => x.Id);
                    table.CheckConstraint("CK_ShipmentCustomsSnapshot_Countries", "char_length(\"DestinationCountryIso2\") = 2 AND char_length(\"OriginCountryIso2\") = 2");
                    table.CheckConstraint("CK_ShipmentCustomsSnapshot_Fingerprint", "char_length(\"SnapshotFingerprintSha256\") = 64");
                    table.CheckConstraint("CK_ShipmentCustomsSnapshot_Frozen", "\"IsRegistrationFrozen\" = TRUE");
                    table.CheckConstraint("CK_ShipmentCustomsSnapshot_GrossWeight", "\"ShipmentGrossWeight\" > 0");
                    table.CheckConstraint("CK_ShipmentCustomsSnapshot_Version", "\"SnapshotVersion\" = 1 AND \"CarrierNeutralDtoVersion\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "ShippingMethods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    NameAr = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    BaseRate = table.Column<decimal>(type: "numeric", nullable: false),
                    PerKgRate = table.Column<decimal>(type: "numeric", nullable: false),
                    FreeShippingThreshold = table.Column<decimal>(type: "numeric", nullable: true),
                    EstimatedDeliveryDaysMin = table.Column<int>(type: "integer", nullable: false),
                    EstimatedDeliveryDaysMax = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    AvailableCountries = table.Column<string>(type: "text", nullable: true),
                    CarrierName = table.Column<string>(type: "text", nullable: true),
                    CarrierCode = table.Column<string>(type: "text", nullable: true),
                    ServiceLevel = table.Column<string>(type: "text", nullable: false),
                    TrackingUrlTemplate = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingMethods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShippingWebhookLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TrackingNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayloadSummary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Signature = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SignatureVerified = table.Column<bool>(type: "boolean", nullable: false),
                    ValidationMethod = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProcessingStatus = table.Column<int>(type: "integer", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessingError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ProcessingDurationMs = table.Column<long>(type: "bigint", nullable: true),
                    SourceIpPrefix = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    UserAgentSummary = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingWebhookLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SmartOffers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    TriggerType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PagesScope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DelaySeconds = table.Column<int>(type: "integer", nullable: true),
                    ScrollPercent = table.Column<int>(type: "integer", nullable: true),
                    ShowOncePerSession = table.Column<bool>(type: "boolean", nullable: false),
                    MaxDisplaysPerUser = table.Column<int>(type: "integer", nullable: true),
                    PopupTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PopupDescription = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ButtonText = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CouponCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    GeneratedCouponId = table.Column<Guid>(type: "uuid", nullable: true),
                    CouponDiscountType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CouponDiscountValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CouponMinOrderAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CouponMaxDiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ImageUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    BackgroundColor = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartOffers", x => x.Id);
                    table.CheckConstraint("CK_SmartOffers_CouponCode_Presence", "NULLIF(REGEXP_REPLACE(\"CouponCode\", '\\s', '', 'g'), '') IS NOT NULL OR (\"CouponDiscountType\" IS NULL AND \"CouponDiscountValue\" IS NULL AND \"CouponMinOrderAmount\" IS NULL AND \"CouponMaxDiscountAmount\" IS NULL)");
                    table.CheckConstraint("CK_SmartOffers_CouponDiscountType", "\"CouponDiscountType\" IS NULL OR \"CouponDiscountType\" IN ('Percentage', 'FixedAmount')");
                    table.CheckConstraint("CK_SmartOffers_CouponDiscountValue_FixedAmount", "\"CouponDiscountValue\" IS NULL OR COALESCE(\"CouponDiscountType\", 'Percentage') <> 'FixedAmount' OR \"CouponDiscountValue\" <= 1000000.00");
                    table.CheckConstraint("CK_SmartOffers_CouponDiscountValue_Percentage", "\"CouponDiscountValue\" IS NULL OR COALESCE(\"CouponDiscountType\", 'Percentage') <> 'Percentage' OR \"CouponDiscountValue\" <= 100");
                    table.CheckConstraint("CK_SmartOffers_CouponDiscountValue_Positive", "\"CouponDiscountValue\" IS NULL OR \"CouponDiscountValue\" > 0");
                    table.CheckConstraint("CK_SmartOffers_CouponDiscountValue_Presence", "NULLIF(REGEXP_REPLACE(\"CouponCode\", '\\s', '', 'g'), '') IS NULL OR \"CouponDiscountValue\" IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "StockSyncOutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClrType = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventVersion = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StockUpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClaimedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NextRetryAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsFailed = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockSyncOutboxMessages", x => x.Id);
                    table.CheckConstraint("CK_StockSyncOutbox_EventVersion_Positive", "\"EventVersion\" > 0");
                    table.CheckConstraint("CK_StockSyncOutbox_WarehouseId_NotEmpty", "\"WarehouseId\" <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_StockSyncOutboxMessages_EventVersion_Positive", "\"EventVersion\" > 0");
                    table.CheckConstraint("CK_StockSyncOutboxMessages_OrderingTimestamps_Required", "\"OccurredAtUtc\" IS NOT NULL AND \"StockUpdatedAtUtc\" IS NOT NULL");
                    table.CheckConstraint("CK_StockSyncOutboxMessages_WarehouseId_NotEmpty", "\"WarehouseId\" <> '00000000-0000-0000-0000-000000000000'");
                });

            migrationBuilder.CreateTable(
                name: "StockTransferRequestIdempotency",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Operation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StockTransferId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResponseStatusCode = table.Column<int>(type: "integer", nullable: true),
                    ResponseJson = table.Column<string>(type: "text", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransferRequestIdempotency", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StoreConfigAuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ActionType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Target = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BeforeSnapshotJson = table.Column<string>(type: "text", nullable: true),
                    AfterSnapshotJson = table.Column<string>(type: "text", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: true),
                    TraceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true),
                    TimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreConfigAuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StoreConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StoreDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ThemeId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Theme_Colors_Primary = table.Column<string>(type: "text", nullable: false),
                    Theme_Colors_Secondary = table.Column<string>(type: "text", nullable: false),
                    Theme_Colors_Background = table.Column<string>(type: "text", nullable: false),
                    Theme_Colors_Text = table.Column<string>(type: "text", nullable: false),
                    Theme_Colors_Muted = table.Column<string>(type: "text", nullable: false),
                    Theme_Font_Family = table.Column<string>(type: "text", nullable: false),
                    Theme_Font_BaseSize = table.Column<string>(type: "text", nullable: false),
                    Theme_Ui_BorderRadius = table.Column<string>(type: "text", nullable: false),
                    Theme_Ui_ButtonStyle = table.Column<string>(type: "text", nullable: false),
                    Theme_Layout_ProductCardVariant = table.Column<string>(type: "text", nullable: false),
                    Theme_Layout_HeaderVariant = table.Column<string>(type: "text", nullable: false),
                    Behavior_EnableGuestCheckout = table.Column<bool>(type: "boolean", nullable: false),
                    Behavior_ShowOutOfStockProducts = table.Column<bool>(type: "boolean", nullable: false),
                    Behavior_EnableSearch = table.Column<bool>(type: "boolean", nullable: false),
                    Behavior_FailCheckoutOnPromotionError = table.Column<bool>(type: "boolean", nullable: false),
                    StorefrontStatus = table.Column<string>(type: "text", nullable: false),
                    MaintenanceMessage = table.Column<string>(type: "text", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CustomizationJson = table.Column<string>(type: "text", nullable: false),
                    ThemePresetKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ThemeLivePresetKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ThemeDraftOverridesJson = table.Column<string>(type: "text", nullable: false),
                    ThemeLiveOverridesJson = table.Column<string>(type: "text", nullable: false),
                    VersionsJson = table.Column<string>(type: "text", nullable: false),
                    LiveVersion = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    PromotionRanking_Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    PromotionRanking_BaseBoost = table.Column<double>(type: "double precision", nullable: false),
                    PromotionRanking_HighDiscountBoost = table.Column<double>(type: "double precision", nullable: false),
                    PromotionRanking_HighDiscountThreshold = table.Column<double>(type: "double precision", nullable: false),
                    PromotionRanking_MaxTotalBoost = table.Column<double>(type: "double precision", nullable: false),
                    PromotionRanking_EnableTimeDecay = table.Column<bool>(type: "boolean", nullable: false),
                    PromotionRanking_TimeDecayScaleDays = table.Column<double>(type: "double precision", nullable: false),
                    PromotionRanking_TimeDecayOffsetDays = table.Column<double>(type: "double precision", nullable: false),
                    PromotionRanking_TimeDecayMinimum = table.Column<double>(type: "double precision", nullable: false),
                    CommerceAiEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CommerceAiEnabledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CommerceAiUpdatedBy = table.Column<string>(type: "text", nullable: true),
                    CommerceAiWidgetStyle = table.Column<string>(type: "text", nullable: false),
                    CommerceAiWidgetPosition = table.Column<string>(type: "text", nullable: false),
                    CommerceAiAutoGreetingEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CommerceAiAutoGreetingMessage = table.Column<string>(type: "text", nullable: false),
                    CommerceAiPersonality = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreConfigs", x => x.Id);
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

            migrationBuilder.CreateTable(
                name: "StorefrontCartCouponStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CartId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OwnerId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    HasAppliedCoupon = table.Column<bool>(type: "boolean", nullable: false),
                    CouponId = table.Column<Guid>(type: "uuid", nullable: true),
                    CouponCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DiscountType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    DiscountValue = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    CartSubtotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    MutationVersion = table.Column<int>(type: "integer", nullable: false),
                    LastRemovalReason = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontCartCouponStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StorefrontFooterSections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    TitleAr = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontFooterSections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StorefrontSocialLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Platform = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Icon = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontSocialLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StripeWebhookReplayRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderEventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EventType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EvidenceReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProcessingTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaymentIntentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    AdmissionClassification = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StripeWebhookReplayRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SuperAdmins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RefreshTokenHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    RefreshTokenExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TokenVersion = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuperAdmins", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Address = table.Column<string>(type: "text", nullable: true),
                    TaxNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpeningBalanceAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    BalanceType = table.Column<string>(type: "text", nullable: true),
                    OpeningBalanceDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaymentTermsDays = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemSchemaVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Component = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Version = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DetailsJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSchemaVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantAnalyticsConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SettingsJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantAnalyticsConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantPaymentMethods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DisplayNameAr = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DescriptionAr = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    SupportsOnline = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsOffline = table.Column<bool>(type: "boolean", nullable: false),
                    IsSandbox = table.Column<bool>(type: "boolean", nullable: false),
                    ConfigEncrypted = table.Column<string>(type: "text", nullable: true),
                    Icon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    MinOrderAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    MaxOrderAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    Fee = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    FeeType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "fixed"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantPaymentMethods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ThemePresets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsRtl = table.Column<bool>(type: "boolean", nullable: false),
                    BaseThemeJson = table.Column<string>(type: "text", nullable: false),
                    PreviewImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThemePresets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Units",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Symbol = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Units", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserGovernanceAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetRole = table.Column<int>(type: "integer", nullable: false),
                    AdminId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ActionType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    BeforeState = table.Column<string>(type: "jsonb", nullable: false),
                    AfterState = table.Column<string>(type: "jsonb", nullable: false),
                    Metadata = table.Column<string>(type: "jsonb", nullable: true),
                    TimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserGovernanceAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserGovernanceBlacklistEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    ValueNormalized = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    ValueDisplay = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedByAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserGovernanceBlacklistEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserGovernanceProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RiskLevel = table.Column<int>(type: "integer", nullable: false),
                    IsKycVerified = table.Column<bool>(type: "boolean", nullable: false),
                    KycVerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    KycVerifiedByAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                    KycVerificationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsMarketplaceApproved = table.Column<bool>(type: "boolean", nullable: false),
                    MarketplaceApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MarketplaceApprovedByAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                    MarketplaceApprovalReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SuspiciousFlags = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastLoginAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StatusChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastRiskEvaluatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifiedByAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                    StatusReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LastRiskReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LastIpAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DeviceFingerprint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserGovernanceProfiles", x => x.Id);
                    table.CheckConstraint("CK_UserGovernanceProfiles_MarketplaceApprovalRequiresKyc", "NOT \"IsMarketplaceApproved\" OR \"IsKycVerified\"");
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RefreshToken = table.Column<string>(type: "text", nullable: true),
                    RefreshTokenExpiryTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmailVerificationStatus = table.Column<int>(type: "integer", nullable: false),
                    EmailVerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Warehouses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: true),
                    Address = table.Column<string>(type: "text", nullable: true),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Warehouses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_message_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
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
                    table.PrimaryKey("PK_whatsapp_message_templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppRateLimits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MessageCount = table.Column<int>(type: "integer", nullable: false),
                    WindowStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastMessageAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OptedOut = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    OptedOutAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppRateLimits", x => x.Id);
                });

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
                    table.CheckConstraint("CK_AffiliateBalanceLedger_IdempotencyKey_NotBlank", "btrim(\"IdempotencyKey\") <> ''");
                    table.CheckConstraint("CK_AffiliateBalanceLedger_WithdrawalNegative", "(\"EntryType\" != 1 OR \"Amount\" < 0)");
                    table.ForeignKey(
                        name: "FK_AffiliateBalanceLedger_AffiliateAccounts_AffiliateAccountId",
                        column: x => x.AffiliateAccountId,
                        principalTable: "AffiliateAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AffiliateUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AffiliateAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmailVerified = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    RefreshToken = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PreviousRefreshToken = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RefreshTokenExpiryTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TokensValidAfterUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliateUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AffiliateUsers_AffiliateAccounts_AffiliateAccountId",
                        column: x => x.AffiliateAccountId,
                        principalTable: "AffiliateAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AffiliatePayoutTransferAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AffiliateAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SettlementSource = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliatePayoutTransferAllocations", x => x.Id);
                    table.CheckConstraint("CK_AffiliatePayoutTransferAllocation_Affiliate_Required", "\"AffiliateAccountId\" <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_AffiliatePayoutTransferAllocation_Amount_Positive", "\"Amount\" > 0");
                    table.CheckConstraint("CK_AffiliatePayoutTransferAllocation_IdempotencyKey_NotBlank", "btrim(\"IdempotencyKey\") <> ''");
                    table.CheckConstraint("CK_AffiliatePayoutTransferAllocation_Reference_Required", "\"ReferenceId\" <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_AffiliatePayoutTransferAllocation_Tenant_Required", "\"StoreTenantId\" <> '00000000-0000-0000-0000-000000000000'");
                    table.ForeignKey(
                        name: "FK_AffiliatePayoutTransferAllocations_AffiliatePayoutTransferE~",
                        column: x => x.EvidenceId,
                        principalTable: "AffiliatePayoutTransferEvidence",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    BankId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TransactionType = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    IsOpeningBalance = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankTransactions_Banks_BankId",
                        column: x => x.BankId,
                        principalTable: "Banks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CarrierGeographicSyncRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Environment = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    AccountScopeKind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    AccountScopeId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceKind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    SourceUri = table.Column<string>(type: "text", nullable: true),
                    SourceVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SourceHashSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    RawEvidenceObjectKey = table.Column<string>(type: "text", nullable: true),
                    AddedCount = table.Column<int>(type: "integer", nullable: false),
                    ChangedCount = table.Column<int>(type: "integer", nullable: false),
                    RemovedCount = table.Column<int>(type: "integer", nullable: false),
                    AmbiguousCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierGeographicSyncRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CarrierGeographicSyncRuns_CarrierProviders_CarrierProviderId",
                        column: x => x.CarrierProviderId,
                        principalTable: "CarrierProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MarketplaceCategoryTranslations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Language = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Slug = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceCategoryTranslations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketplaceCategoryTranslations_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    NameAr = table.Column<string>(type: "text", nullable: true),
                    Barcode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SKU = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    DescriptionAr = table.Column<string>(type: "text", nullable: true),
                    FeaturesJson = table.Column<string>(type: "jsonb", nullable: true),
                    FaqJson = table.Column<string>(type: "jsonb", nullable: true),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchasePrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SalePrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OnlinePrice = table.Column<decimal>(type: "numeric", nullable: true),
                    PriceIncludesTax = table.Column<bool>(type: "boolean", nullable: false),
                    TaxRate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    Unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MinimumStockLevel = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Images = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TrackStock = table.Column<bool>(type: "boolean", nullable: false),
                    HasVariants = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Slug = table.Column<string>(type: "text", nullable: true),
                    MetaTitle = table.Column<string>(type: "text", nullable: true),
                    MetaDescription = table.Column<string>(type: "text", nullable: true),
                    MetaKeywords = table.Column<string>(type: "text", nullable: true),
                    Weight = table.Column<decimal>(type: "numeric", nullable: false),
                    Length = table.Column<decimal>(type: "numeric", nullable: false),
                    Width = table.Column<decimal>(type: "numeric", nullable: false),
                    Height = table.Column<decimal>(type: "numeric", nullable: false),
                    WeightUnit = table.Column<string>(type: "text", nullable: false),
                    DimensionUnit = table.Column<string>(type: "text", nullable: false),
                    PublishStatus = table.Column<int>(type: "integer", nullable: false),
                    IsOnlineVisible = table.Column<bool>(type: "boolean", nullable: false),
                    IsSearchable = table.Column<bool>(type: "boolean", nullable: false),
                    IsFeatured = table.Column<bool>(type: "boolean", nullable: false),
                    LowStockThreshold = table.Column<int>(type: "integer", nullable: false),
                    AllowBackorder = table.Column<bool>(type: "boolean", nullable: false),
                    ProjectionMutationVersion = table.Column<long>(type: "bigint", nullable: false),
                    Brand = table.Column<string>(type: "text", nullable: true),
                    Manufacturer = table.Column<string>(type: "text", nullable: true),
                    Tags = table.Column<string>(type: "text", nullable: true),
                    TagsJson = table.Column<string>(type: "jsonb", nullable: true),
                    AiGenerated = table.Column<bool>(type: "boolean", nullable: false),
                    AiMetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    RelatedProductIds = table.Column<string>(type: "text", nullable: true),
                    VideoUrl = table.Column<string>(type: "text", nullable: true),
                    ProductTemplate = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AmazonASIN = table.Column<string>(type: "text", nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Products_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubCategoryAttributes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    IsFilterable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    IsSearchable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    OptionsJson = table.Column<string>(type: "text", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubCategoryAttributes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommerceHubCategoryAttributes_CommerceHubCategories_Categor~",
                        column: x => x.CategoryId,
                        principalTable: "CommerceHubCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubCustomerEmailVerificationTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsUsed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    UsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubCustomerEmailVerificationTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommerceHubCustomerEmailVerificationTokens_CommerceHubCusto~",
                        column: x => x.CustomerId,
                        principalTable: "CommerceHubCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubCustomerIdentityEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Metadata = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubCustomerIdentityEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommerceHubCustomerIdentityEvents_CommerceHubCustomers_Cust~",
                        column: x => x.CustomerId,
                        principalTable: "CommerceHubCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubCustomerPasswordResetTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsUsed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    UsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByIp = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubCustomerPasswordResetTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommerceHubCustomerPasswordResetTokens_CommerceHubCustomers~",
                        column: x => x.CustomerId,
                        principalTable: "CommerceHubCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubCustomerPayoutDetails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    AccountIdentifier = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AccountHolderName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsVerified = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubCustomerPayoutDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommerceHubCustomerPayoutDetails_CommerceHubCustomers_Custo~",
                        column: x => x.CustomerId,
                        principalTable: "CommerceHubCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubMarketplaceOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AddressLine = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Area = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    PaymentMethodCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PaymentMethodName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PaymentReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PaymentReferenceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PaymentGateway = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ShippingTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PromoCode = table.Column<string>(type: "text", nullable: true),
                    PromoDiscountTotal = table.Column<decimal>(type: "numeric", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PaymentStatus = table.Column<int>(type: "integer", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OnlineCustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderAccessToken = table.Column<string>(type: "text", nullable: true),
                    IsProjected = table.Column<bool>(type: "boolean", nullable: false),
                    ProjectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProjectionMutationVersion = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    LastProjectionAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProjectionAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    IsProjectionTerminalFailure = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubMarketplaceOrders", x => x.Id);
                    table.CheckConstraint("CK_CommerceHubMarketplaceOrders_Currency_EGP", "\"Currency\" = 'EGP'");
                    table.CheckConstraint("CK_CommerceHubMarketplaceOrders_CustomerAuthorityBoundary", "\"CustomerId\" <> '00000000-0000-0000-0000-000000000000' AND \"OnlineCustomerId\" IS NULL");
                    table.ForeignKey(
                        name: "FK_CommerceHubMarketplaceOrders_CommerceHubCustomers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "CommerceHubCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubPayouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayoutMethodId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    DestinationValueSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ExternalReferenceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BankTransactionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ExternalTransferIdempotencyKey = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    ExternalTransferHandoffToken = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ExternalTransferHandoffCommittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletionEvidenceFingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubPayouts", x => x.Id);
                    table.CheckConstraint("CK_CommerceHubPayouts_Amount_Positive", "\"Amount\" > 0");
                    table.CheckConstraint("CK_CommerceHubPayouts_Completed_Evidence", "\"Status\" <> 2 OR NULLIF(btrim(\"ExternalReferenceId\"), '') IS NOT NULL OR NULLIF(btrim(\"BankTransactionId\"), '') IS NOT NULL");
                    table.CheckConstraint("CK_CommerceHubPayouts_Completed_State_Complete", "\"Status\" <> 2 OR (\"ProcessedAtUtc\" IS NOT NULL AND \"CompletedAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_CommerceHubPayouts_Currency_EGP", "\"Currency\" = 'EGP'");
                    table.CheckConstraint("CK_CommerceHubPayouts_NoRefundFailureAfterHandoff", "NOT (\"Status\" = 3 AND \"ExternalTransferHandoffCommittedAtUtc\" IS NOT NULL AND \"ProcessedBy\" IN ('system:refund', 'system:refund-control'))");
                    table.CheckConstraint("CK_CommerceHubPayouts_Processing_ExternalIntent", "(\"Status\" <> 1 AND \"Status\" <> 4) OR NULLIF(btrim(\"ExternalTransferIdempotencyKey\"), '') IS NOT NULL");
                    table.CheckConstraint("CK_CommerceHubPayouts_Processing_HandoffCommitted", "(\"Status\" <> 1 AND \"Status\" <> 4) OR (NULLIF(btrim(\"ExternalTransferHandoffToken\"), '') IS NOT NULL AND \"ExternalTransferHandoffCommittedAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_CommerceHubPayouts_Terminal_HandoffCommitted", "(\"Status\" <> 2 AND \"Status\" <> 4) OR (NULLIF(btrim(\"ExternalTransferHandoffToken\"), '') IS NOT NULL AND \"ExternalTransferHandoffCommittedAtUtc\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_CommerceHubPayouts_CommerceHubVendorPayoutMethods_PayoutMet~",
                        column: x => x.PayoutMethodId,
                        principalTable: "CommerceHubVendorPayoutMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Slug = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: true),
                    ContactEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "EGP"),
                    SuspendedReason = table.Column<string>(type: "text", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BootstrapVersion = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StorefrontBaseUrl = table.Column<string>(type: "text", nullable: true),
                    UseBaileys = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tenants_Currencies_Currency",
                        column: x => x.Currency,
                        principalTable: "Currencies",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TransactionType = table.Column<int>(type: "integer", nullable: false),
                    DebitAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    CreditAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "text", nullable: true),
                    IsOpeningBalance = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerTransactions_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomsEvidenceVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomsEvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    HsCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CommodityDescription = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CountryOfOriginIso2 = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    CountryOfManufactureIso2 = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: true),
                    DeclaredValue = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    NetWeight = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    WeightUnit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    QuantityUnit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DangerousGoods = table.Column<bool>(type: "boolean", nullable: true),
                    ExportClassification = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ValueSource = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceEvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                    EvidenceFingerprintSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    AdmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AdmittedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomsEvidenceVersions", x => x.Id);
                    table.CheckConstraint("CK_CustomsEvidenceVersion_Commodity", "char_length(btrim(\"CommodityDescription\")) > 0");
                    table.CheckConstraint("CK_CustomsEvidenceVersion_Country", "char_length(\"CountryOfOriginIso2\") = 2");
                    table.CheckConstraint("CK_CustomsEvidenceVersion_Fingerprint", "char_length(\"EvidenceFingerprintSha256\") = 64");
                    table.CheckConstraint("CK_CustomsEvidenceVersion_HsCode", "char_length(btrim(\"HsCode\")) > 0");
                    table.CheckConstraint("CK_CustomsEvidenceVersion_NetWeight", "\"NetWeight\" > 0");
                    table.CheckConstraint("CK_CustomsEvidenceVersion_Value", "\"DeclaredValue\" > 0");
                    table.CheckConstraint("CK_CustomsEvidenceVersion_Version", "\"VersionNumber\" > 0");
                    table.ForeignKey(
                        name: "FK_CustomsEvidenceVersions_CustomsEvidence_CustomsEvidenceId",
                        column: x => x.CustomsEvidenceId,
                        principalTable: "CustomsEvidence",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancialTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    TransactionType = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    RelatedEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    RelatedEntityType = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialTransactions_FinancialAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "FinancialAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CarrierGeographicMappingReleases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReleaseNumber = table.Column<long>(type: "bigint", nullable: false),
                    ReleaseKey = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Environment = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    AccountScopeKind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    AccountScopeId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CatalogReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSummary = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ChecksumSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FreshnessExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublishedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupersedesReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    RollbackOfReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierGeographicMappingReleases", x => x.Id);
                    table.CheckConstraint("CK_CarrierGeoRelease_Env", "\"Environment\" IN ('Production','Sandbox','Test')");
                    table.CheckConstraint("CK_CarrierGeoRelease_Scope", "\"AccountScopeKind\" IN ('Global','ProviderAccount','TenantProviderAccount')");
                    table.CheckConstraint("CK_CarrierGeoRelease_Status", "\"Status\" IN ('Draft','Validated','Published','Superseded','RolledBack','Rejected')");
                    table.CheckConstraint("CK_CarrierGeoRelease_Window", "\"EffectiveToUtc\" IS NULL OR \"EffectiveToUtc\" > \"EffectiveFromUtc\"");
                    table.ForeignKey(
                        name: "FK_CarrierGeoRelease_RollbackOf",
                        column: x => x.RollbackOfReleaseId,
                        principalTable: "CarrierGeographicMappingReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierGeoRelease_Supersedes",
                        column: x => x.SupersedesReleaseId,
                        principalTable: "CarrierGeographicMappingReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierGeographicMappingReleases_CarrierProviders_CarrierPr~",
                        column: x => x.CarrierProviderId,
                        principalTable: "CarrierProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierGeographicMappingReleases_GeographicCatalogReleases_~",
                        column: x => x.CatalogReleaseId,
                        principalTable: "GeographicCatalogReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GeographicCatalogPublications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CatalogReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicationKind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublishedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SupersededAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeographicCatalogPublications", x => x.Id);
                    table.CheckConstraint("CK_GeoCatalogPublication_Kind", "\"PublicationKind\" IN ('Initial','Publish','Rollback','Supersede')");
                    table.ForeignKey(
                        name: "FK_GeographicCatalogPublications_GeographicCatalogReleases_Cat~",
                        column: x => x.CatalogReleaseId,
                        principalTable: "GeographicCatalogReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GeographicPlaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StableKey = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    PlaceKind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CountryPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    LevelCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CanonicalCode = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    ExternalStandard = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExternalStandardCode = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    SuccessorPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ValidFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IntroducedCatalogReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    RetiredCatalogReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    CentroidLatitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    CentroidLongitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    TimeZoneId = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    Source = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeographicPlaces", x => x.Id);
                    table.CheckConstraint("CK_GeoPlace_CountryParent", "(\"PlaceKind\" = 'Country' AND \"CountryPlaceId\" IS NULL) OR (\"PlaceKind\" <> 'Country' AND \"CountryPlaceId\" IS NOT NULL)");
                    table.CheckConstraint("CK_GeoPlace_Kind", "\"PlaceKind\" IN ('Country','AdministrativeArea','Locality','District','Zone')");
                    table.CheckConstraint("CK_GeoPlace_Latitude", "\"CentroidLatitude\" IS NULL OR (\"CentroidLatitude\" >= -90 AND \"CentroidLatitude\" <= 90)");
                    table.CheckConstraint("CK_GeoPlace_Longitude", "\"CentroidLongitude\" IS NULL OR (\"CentroidLongitude\" >= -180 AND \"CentroidLongitude\" <= 180)");
                    table.CheckConstraint("CK_GeoPlace_Status", "\"Status\" IN ('Draft','Active','Deprecated','Merged','Retired','Rejected')");
                    table.CheckConstraint("CK_GeoPlace_Successor", "\"SuccessorPlaceId\" IS NULL OR \"SuccessorPlaceId\" <> \"Id\"");
                    table.CheckConstraint("CK_GeoPlace_ValidWindow", "\"ValidToUtc\" IS NULL OR \"ValidToUtc\" > \"ValidFromUtc\"");
                    table.ForeignKey(
                        name: "FK_GeographicPlaces_GeographicCatalogReleases_IntroducedCatalo~",
                        column: x => x.IntroducedCatalogReleaseId,
                        principalTable: "GeographicCatalogReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeographicPlaces_GeographicCatalogReleases_RetiredCatalogRe~",
                        column: x => x.RetiredCatalogReleaseId,
                        principalTable: "GeographicCatalogReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeographicPlaces_GeographicPlaces_CountryPlaceId",
                        column: x => x.CountryPlaceId,
                        principalTable: "GeographicPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeographicPlaces_GeographicPlaces_SuccessorPlaceId",
                        column: x => x.SuccessorPlaceId,
                        principalTable: "GeographicPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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
                        name: "FK_TreasuryAccounts_LedgerAccounts_TenantId_AccountId",
                        columns: x => new { x.TenantId, x.AccountId },
                        principalTable: "LedgerAccounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
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
                name: "LedgerLines",
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
                    table.PrimaryKey("PK_LedgerLines", x => x.Id);
                    table.CheckConstraint("CK_LedgerLines_PositiveAmounts", "\"DebitAmount\" >= 0 AND \"CreditAmount\" >= 0");
                    table.CheckConstraint("CK_LedgerLines_SingleSide", "(\"DebitAmount\" > 0 AND \"CreditAmount\" = 0) OR (\"DebitAmount\" = 0 AND \"CreditAmount\" > 0)");
                    table.ForeignKey(
                        name: "FK_LedgerLines_LedgerAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerLines_LedgerJournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "LedgerJournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MarketplaceFooterLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    LabelJson = table.Column<string>(type: "jsonb", nullable: true),
                    LinkType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CmsPageSlug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    OpenInNewTab = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceFooterLinks", x => x.Id);
                    table.CheckConstraint("CK_MarketplaceFooterLinks_LinkType", "\"LinkType\" IN ('Internal', 'External', 'Cms')");
                    table.CheckConstraint("CK_MarketplaceFooterLinks_Target", "(\"LinkType\" = 'Cms' AND \"CmsPageSlug\" IS NOT NULL AND LENGTH(TRIM(\"CmsPageSlug\")) > 0) OR (\"LinkType\" <> 'Cms' AND \"Url\" IS NOT NULL AND LENGTH(TRIM(\"Url\")) > 0)");
                    table.ForeignKey(
                        name: "FK_MarketplaceFooterLinks_MarketplaceFooterSections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "MarketplaceFooterSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                    MarketingReviewCount = table.Column<int>(type: "integer", nullable: false),
                    PosVerifiedCount = table.Column<int>(type: "integer", nullable: false),
                    TotalVerifiedRevenue = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalVerifiedOrders = table.Column<int>(type: "integer", nullable: false),
                    MarketingWeightedScore = table.Column<decimal>(type: "numeric", nullable: false),
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
                    CurrentAccessTokenJti = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CurrentAccessTokenExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsRevoked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsAccessTokenBlacklisted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
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

            migrationBuilder.CreateTable(
                name: "OutboxMessage",
                columns: table => new
                {
                    SequenceNumber = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EnqueueTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SentTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Headers = table.Column<string>(type: "text", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    InboxMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    InboxConsumerId = table.Column<Guid>(type: "uuid", nullable: true),
                    OutboxId = table.Column<Guid>(type: "uuid", nullable: true),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    MessageType = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    InitiatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DestinationAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ResponseAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FaultAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ExpirationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessage", x => x.SequenceNumber);
                    table.ForeignKey(
                        name: "FK_OutboxMessage_InboxState_InboxMessageId_InboxConsumerId",
                        columns: x => new { x.InboxMessageId, x.InboxConsumerId },
                        principalTable: "InboxState",
                        principalColumns: new[] { "MessageId", "ConsumerId" });
                    table.ForeignKey(
                        name: "FK_OutboxMessage_OutboxState_OutboxId",
                        column: x => x.OutboxId,
                        principalTable: "OutboxState",
                        principalColumn: "OutboxId");
                });

            migrationBuilder.CreateTable(
                name: "ShipmentCustomsSnapshotLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentCustomsSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomsEvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomsEvidenceVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceVersionNumber = table.Column<int>(type: "integer", nullable: false),
                    HsCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CommodityDescription = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CountryOfOriginIso2 = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    CountryOfManufactureIso2 = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: true),
                    DeclaredValue = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    QuantityUnit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NetWeight = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    NetWeightUnit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DangerousGoods = table.Column<bool>(type: "boolean", nullable: true),
                    ExportClassification = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SourceEvidenceFingerprintSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentCustomsSnapshotLines", x => x.Id);
                    table.CheckConstraint("CK_ShipmentCustomsSnapshotLine_Country", "char_length(\"CountryOfOriginIso2\") = 2");
                    table.CheckConstraint("CK_ShipmentCustomsSnapshotLine_NetWeight", "\"NetWeight\" > 0");
                    table.CheckConstraint("CK_ShipmentCustomsSnapshotLine_Quantity", "\"Quantity\" > 0");
                    table.CheckConstraint("CK_ShipmentCustomsSnapshotLine_Value", "\"DeclaredValue\" > 0");
                    table.ForeignKey(
                        name: "FK_ShipmentCustomsSnapshotLines_ShipmentCustomsSnapshots_Shipm~",
                        column: x => x.ShipmentCustomsSnapshotId,
                        principalTable: "ShipmentCustomsSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StorefrontFooterLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    LabelAr = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    LinkType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CmsPageSlug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    OpenInNewTab = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontFooterLinks", x => x.Id);
                    table.CheckConstraint("CK_StorefrontFooterLinks_LinkType", "\"LinkType\" IN ('Internal', 'External', 'Cms')");
                    table.CheckConstraint("CK_StorefrontFooterLinks_Target", "(\"LinkType\" = 'Cms' AND \"CmsPageSlug\" IS NOT NULL AND LENGTH(TRIM(\"CmsPageSlug\")) > 0) OR (\"LinkType\" <> 'Cms' AND \"Url\" IS NOT NULL AND LENGTH(TRIM(\"Url\")) > 0)");
                    table.ForeignKey(
                        name: "FK_StorefrontFooterLinks_StorefrontFooterSections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "StorefrontFooterSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupplierTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TransactionType = table.Column<int>(type: "integer", nullable: false),
                    DebitAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    CreditAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "text", nullable: true),
                    IsOpeningBalance = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierTransactions_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccountLedgers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartyType = table.Column<int>(type: "integer", nullable: false),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TransactionType = table.Column<int>(type: "integer", nullable: false),
                    DocumentNumber = table.Column<string>(type: "text", nullable: false),
                    DebitAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    CreditAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    RunningBalance = table.Column<decimal>(type: "numeric", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountLedgers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountLedgers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EmailVerificationTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailVerificationTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailVerificationTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TreasuryTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionNumber = table.Column<string>(type: "text", nullable: false),
                    TransactionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TransactionType = table.Column<int>(type: "integer", nullable: false),
                    PaymentMethod = table.Column<int>(type: "integer", nullable: false),
                    TargetAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetAccountName = table.Column<string>(type: "text", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreasuryTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TreasuryTransactions_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "VisaTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TransactionType = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    IsDebit = table.Column<bool>(type: "boolean", nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    SourceEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceEntityType = table.Column<string>(type: "text", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisaTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VisaTransactions_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Cashboxes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OpenedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OpeningBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ExpectedBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OpeningBalanceAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    OpeningBalanceDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cashboxes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cashboxes_Users_OpenedByUserId",
                        column: x => x.OpenedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Cashboxes_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "text", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    OnlineCustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    SessionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CartId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuestEmail = table.Column<string>(type: "text", nullable: true),
                    GuestPhone = table.Column<string>(type: "text", nullable: true),
                    GuestName = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    IsGuestCheckout = table.Column<bool>(type: "boolean", nullable: false),
                    AffiliateAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    AffiliateReferralCode = table.Column<string>(type: "text", nullable: true),
                    PromotionLinkId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CheckoutState = table.Column<int>(type: "integer", nullable: false),
                    PaymentStatus = table.Column<int>(type: "integer", nullable: false),
                    ProjectionMutationVersion = table.Column<long>(type: "bigint", nullable: false),
                    FulfillmentStatus = table.Column<int>(type: "integer", nullable: false),
                    FulfillmentMode = table.Column<int>(type: "integer", nullable: false),
                    FulfillmentCarrierCode = table.Column<string>(type: "text", nullable: true),
                    ShippingAddress = table.Column<string>(type: "text", nullable: false),
                    ShippingCity = table.Column<string>(type: "text", nullable: true),
                    ShippingState = table.Column<string>(type: "text", nullable: true),
                    ShippingZipCode = table.Column<string>(type: "text", nullable: true),
                    ShippingCountry = table.Column<string>(type: "text", nullable: true),
                    ShippingAddressAdmissionEvidenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShippingGeographicCatalogReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShippingCountryPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShippingAdministrativeAreaPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShippingLocalityPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShippingDistrictPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShippingZonePlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShippingCountryDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ShippingAdministrativeAreaDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ShippingLocalityDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ShippingDistrictDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ShippingZoneDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ShippingStreet = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ShippingStreet2 = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ShippingBuilding = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ShippingFloor = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ShippingApartment = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ShippingLandmark = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ShippingGeographicSnapshotCreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ShippingGeographicSnapshotVersion = table.Column<int>(type: "integer", nullable: true),
                    BillingAddress = table.Column<string>(type: "text", nullable: true),
                    BillingCity = table.Column<string>(type: "text", nullable: true),
                    BillingState = table.Column<string>(type: "text", nullable: true),
                    BillingZipCode = table.Column<string>(type: "text", nullable: true),
                    BillingCountry = table.Column<string>(type: "text", nullable: true),
                    BillingAddressAdmissionEvidenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    BillingGeographicCatalogReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    BillingCountryPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    BillingAdministrativeAreaPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    BillingLocalityPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    BillingDistrictPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    BillingZonePlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    BillingCountryDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BillingAdministrativeAreaDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BillingLocalityDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BillingDistrictDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BillingZoneDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BillingStreet = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BillingStreet2 = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BillingBuilding = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    BillingFloor = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    BillingApartment = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    BillingLandmark = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BillingGeographicSnapshotCreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BillingGeographicSnapshotVersion = table.Column<int>(type: "integer", nullable: true),
                    ShippingMethodId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShippingMethodName = table.Column<string>(type: "text", nullable: true),
                    ShippingCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    EstimatedDeliveryDays = table.Column<string>(type: "text", nullable: true),
                    TrackingNumber = table.Column<string>(type: "text", nullable: true),
                    Carrier = table.Column<string>(type: "text", nullable: true),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PromotionDiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ManualDiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalDiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    PricingSnapshotJson = table.Column<string>(type: "text", nullable: true),
                    CouponCode = table.Column<string>(type: "text", nullable: true),
                    CouponId = table.Column<Guid>(type: "uuid", nullable: true),
                    AppliedPromotionId = table.Column<Guid>(type: "uuid", nullable: true),
                    AppliedPromotionType = table.Column<string>(type: "text", nullable: true),
                    AppliedPromotionName = table.Column<string>(type: "text", nullable: true),
                    AppliedPromotionDescription = table.Column<string>(type: "text", nullable: true),
                    PromotionCalculationFailed = table.Column<bool>(type: "boolean", nullable: false),
                    PromotionWarningMessage = table.Column<string>(type: "text", nullable: true),
                    PaymentMethod = table.Column<int>(type: "integer", nullable: false),
                    PaymentMethodCode = table.Column<string>(type: "text", nullable: false),
                    PaymentMethodName = table.Column<string>(type: "text", nullable: true),
                    PaymentFee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsPaid = table.Column<bool>(type: "boolean", nullable: false),
                    PaymentReference = table.Column<string>(type: "text", nullable: true),
                    PaymentGateway = table.Column<string>(type: "text", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinancialPostingStatus = table.Column<int>(type: "integer", nullable: false),
                    FinancialPostingPendingAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinancialPostedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinancialPostingJournalId = table.Column<Guid>(type: "uuid", nullable: true),
                    FinancialPostingFailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CustomerNotes = table.Column<string>(type: "text", nullable: true),
                    AdminNotes = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    SourceOrderId = table.Column<string>(type: "text", nullable: true),
                    ShippedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "text", nullable: true),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.UniqueConstraint("AK_Orders_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Orders_CustomerAuthorityBoundary", "(\"CustomerId\" IS NULL OR \"OnlineCustomerId\" IS NULL) AND (\"Source\" <> 0 OR \"CustomerId\" IS NULL)");
                    table.CheckConstraint("CK_Orders_FinancialPostingStatus_Valid", "\"FinancialPostingStatus\" IN (0, 1, 2, 3) AND (\"FinancialPostingStatus\" <> 1 OR \"FinancialPostingPendingAtUtc\" IS NOT NULL) AND (\"FinancialPostingStatus\" <> 2 OR (\"FinancialPostedAtUtc\" IS NOT NULL AND \"FinancialPostingJournalId\" IS NOT NULL)) AND (\"FinancialPostingStatus\" <> 3 OR \"FinancialPostingFailureReason\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Orders_Coupons_CouponId",
                        column: x => x.CouponId,
                        principalTable: "Coupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Orders_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Orders_OnlineCustomers_OnlineCustomerId",
                        column: x => x.OnlineCustomerId,
                        principalTable: "OnlineCustomers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Orders_Promotions_AppliedPromotionId",
                        column: x => x.AppliedPromotionId,
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Orders_ShippingMethods_ShippingMethodId",
                        column: x => x.ShippingMethodId,
                        principalTable: "ShippingMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Orders_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "text", nullable: false),
                    InvoiceDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AccountingMutationVersion = table.Column<int>(type: "integer", nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    DiscountPercentage = table.Column<decimal>(type: "numeric", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    PaidAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    PaymentMethod = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoices", x => x.Id);
                    table.UniqueConstraint("AK_PurchaseInvoices_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_PurchaseInvoices_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoices_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "text", nullable: false),
                    InvoiceDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    DiscountPercentage = table.Column<decimal>(type: "numeric", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    PaidAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    PaymentMethod = table.Column<int>(type: "integer", nullable: true),
                    CashAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    CardAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    SalesRepresentativeId = table.Column<Guid>(type: "uuid", nullable: true),
                    AccountingMutationVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesInvoices_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SalesInvoices_SalesRepresentatives_SalesRepresentativeId",
                        column: x => x.SalesRepresentativeId,
                        principalTable: "SalesRepresentatives",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SalesInvoices_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StockTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransferNumber = table.Column<string>(type: "text", nullable: false),
                    TransferDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FromWarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToWarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    RequestedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovalNotes = table.Column<string>(type: "text", nullable: true),
                    CompletedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockTransfers_Users_ApprovedById",
                        column: x => x.ApprovedById,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StockTransfers_Users_CompletedById",
                        column: x => x.CompletedById,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StockTransfers_Users_RequestedById",
                        column: x => x.RequestedById,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StockTransfers_Warehouses_FromWarehouseId",
                        column: x => x.FromWarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StockTransfers_Warehouses_ToWarehouseId",
                        column: x => x.ToWarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AffiliateEmailVerificationTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AffiliateUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliateEmailVerificationTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AffiliateEmailVerificationTokens_AffiliateUsers_AffiliateUs~",
                        column: x => x.AffiliateUserId,
                        principalTable: "AffiliateUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AffiliatePasswordResetTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AffiliateUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliatePasswordResetTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AffiliatePasswordResetTokens_AffiliateUsers_AffiliateUserId",
                        column: x => x.AffiliateUserId,
                        principalTable: "AffiliateUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AffiliateRefreshTokenReplayEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AffiliateUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RotatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReplayedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliateRefreshTokenReplayEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AffiliateRefreshTokenReplayEntries_AffiliateUsers_Affiliate~",
                        column: x => x.AffiliateUserId,
                        principalTable: "AffiliateUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AffiliateProductPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommissionRate = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    MaxCommissionPerItem = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    TemplateType = table.Column<string>(type: "text", nullable: false),
                    AffiliateCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliateProductPolicies", x => x.Id);
                    table.CheckConstraint("CK_AffiliateProductPolicies_EnabledRequiresCategory", "NOT (\"IsEnabled\" = true AND \"AffiliateCategoryId\" IS NULL)");
                    table.CheckConstraint("CK_AffiliateProductPolicies_MaxCap", "\"MaxCommissionPerItem\" IS NULL OR \"MaxCommissionPerItem\" > 0");
                    table.CheckConstraint("CK_AffiliateProductPolicies_Rate", "\"CommissionRate\" >= 0.0001 AND \"CommissionRate\" <= 0.50");
                    table.ForeignKey(
                        name: "FK_AffiliateProductPolicies_AffiliateCategories_AffiliateCateg~",
                        column: x => x.AffiliateCategoryId,
                        principalTable: "AffiliateCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AffiliateProductPolicies_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AffiliatePromotionLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AffiliateUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferralCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AffiliateAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliatePromotionLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AffiliatePromotionLinks_AffiliateUsers_AffiliateUserId",
                        column: x => x.AffiliateUserId,
                        principalTable: "AffiliateUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AffiliatePromotionLinks_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductMedia",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductMedia", x => x.Id);
                    table.UniqueConstraint("AK_ProductMedia_TenantId_ProductId_Id", x => new { x.TenantId, x.ProductId, x.Id });
                    table.ForeignKey(
                        name: "FK_ProductMedia_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                name: "ProductReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    IsVerifiedPurchase = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductReviews", x => x.Id);
                    table.CheckConstraint("CK_ProductReviews_Rating_Range", "\"Rating\" >= 1 AND \"Rating\" <= 5");
                    table.ForeignKey(
                        name: "FK_ProductReviews_CommerceHubCustomers_UserId",
                        column: x => x.UserId,
                        principalTable: "CommerceHubCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductReviews_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductUnits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversionFactor = table.Column<decimal>(type: "numeric", nullable: false),
                    PurchasePrice = table.Column<decimal>(type: "numeric", nullable: true),
                    SellingPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    Barcode = table.Column<string>(type: "text", nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsBaseUnit = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductUnits_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductUnits_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProviderIntentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProviderTransactionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CheckoutUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PaymentToken = table.Column<string>(type: "text", nullable: true),
                    RefundedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ProvisionalRefundedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsProcessed = table.Column<bool>(type: "boolean", nullable: false),
                    NeedsManualCurrencyReview = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubPayments", x => x.Id);
                    table.CheckConstraint("CK_CommerceHubPayments_Amount_Positive", "\"Amount\" > 0");
                    table.CheckConstraint("CK_CommerceHubPayments_Currency_EGP", "\"Currency\" = 'EGP'");
                    table.CheckConstraint("CK_CommerceHubPayments_Paid_State_Complete", "\"Status\" <> 1 OR (\"IsProcessed\" = true AND \"PaidAtUtc\" IS NOT NULL AND \"ProviderTransactionId\" IS NOT NULL)");
                    table.CheckConstraint("CK_CommerceHubPayments_ProvisionalRefundedAmount_Range", "\"ProvisionalRefundedAmount\" >= 0 AND ROUND(\"RefundedAmount\" + \"ProvisionalRefundedAmount\", 2) <= ROUND(\"Amount\", 2)");
                    table.CheckConstraint("CK_CommerceHubPayments_RefundedAmount_Range", "\"RefundedAmount\" >= 0 AND \"RefundedAmount\" <= \"Amount\"");
                    table.ForeignKey(
                        name: "FK_CommerceHubPayments_CommerceHubMarketplaceOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "CommerceHubMarketplaceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubSubOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketplaceOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "text", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Shipping = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ShippingMethodId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShippingMethodName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ShippingSnapshotJson = table.Column<string>(type: "text", nullable: true),
                    PaymentMethod = table.Column<int>(type: "integer", nullable: false),
                    PaymentMethodCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PaymentMethodName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PaymentSnapshotJson = table.Column<string>(type: "text", nullable: true),
                    CommissionSnapshotJson = table.Column<string>(type: "text", nullable: true),
                    IsEarningCreated = table.Column<bool>(type: "boolean", nullable: false),
                    EarningCreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PlatformCommissionAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FinalPlatformCommissionAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    PromoDiscountAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    VendorEarningAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PlatformCommissionRate = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PaymentStatus = table.Column<int>(type: "integer", nullable: false),
                    PaymentReferenceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProjectionMutationVersion = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubSubOrders", x => x.Id);
                    table.CheckConstraint("CK_CommerceHubSubOrders_Currency_EGP", "\"Currency\" = 'EGP'");
                    table.ForeignKey(
                        name: "FK_CommerceHubSubOrders_CommerceHubMarketplaceOrders_Marketpla~",
                        column: x => x.MarketplaceOrderId,
                        principalTable: "CommerceHubMarketplaceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PromoCodeUsages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromoCodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketplaceOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    PromoCodeSnapshot = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    EligibleBaseCommissionAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AppliedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromoCodeUsages", x => x.Id);
                    table.CheckConstraint("CK_PromoCodeUsages_DiscountAmount", "\"DiscountAmount\" >= 0");
                    table.CheckConstraint("CK_PromoCodeUsages_EligibleBaseCommissionAmount", "\"EligibleBaseCommissionAmount\" >= 0");
                    table.ForeignKey(
                        name: "FK_PromoCodeUsages_CommerceHubMarketplaceOrders_MarketplaceOrd~",
                        column: x => x.MarketplaceOrderId,
                        principalTable: "CommerceHubMarketplaceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PromoCodeUsages_PromoCodes_PromoCodeId",
                        column: x => x.PromoCodeId,
                        principalTable: "PromoCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AbandonedCarts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CartId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    PhoneNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ContactChannel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PhoneVerified = table.Column<bool>(type: "boolean", nullable: false),
                    EmailVerified = table.Column<bool>(type: "boolean", nullable: false),
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
                name: "CheckoutSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CartId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecoveryToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OnlineCustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Area = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ItemsSnapshotJson = table.Column<string>(type: "text", nullable: false),
                    CartTotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    ShippingMethodId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentMethodCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    WalletType = table.Column<string>(type: "text", nullable: true),
                    WalletPhoneNumber = table.Column<string>(type: "text", nullable: true),
                    ProofImageUrl = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DeviceType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FirstActivityAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastActivityAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AbandonedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConvertedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConvertedOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConvertedOrderNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    InvalidatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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

            migrationBuilder.CreateTable(
                name: "CommerceHubVendorPaymentRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentMethodId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "EGP"),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TransferReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MethodNameSnapshot = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DestinationValueSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    InstructionsSnapshot = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RejectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewReason = table.Column<string>(type: "text", nullable: true),
                    LedgerCorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LedgerIdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubVendorPaymentRequests", x => x.Id);
                    table.CheckConstraint("CK_CommerceHubVendorPaymentRequests_Amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_CommerceHubVendorPaymentRequests_Approved_TransferReference", "\"Status\" <> 1 OR NULLIF(btrim(\"TransferReference\"), '') IS NOT NULL");
                    table.CheckConstraint("CK_CommerceHubVendorPaymentRequests_Currency_EGP", "\"Currency\" = 'EGP'");
                    table.CheckConstraint("CK_CommerceHubVendorPaymentRequests_Status", "\"Status\" IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_CommerceHubVendorPaymentRequests_CommerceHubVendorPaymentMe~",
                        column: x => x.PaymentMethodId,
                        principalTable: "CommerceHubVendorPaymentMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommerceHubVendorPaymentRequests_Tenants_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Key = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Category = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SystemSettings_Tenants_TenantId",
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
                    use_for_payment_approval = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    use_for_abandoned_cart_recovery = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
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

            migrationBuilder.CreateTable(
                name: "CarrierGeographicMappingGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MappingReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CardinalityPolicy = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CanonicalPlaceKind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ProviderPlaceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierGeographicMappingGroups", x => x.Id);
                    table.CheckConstraint("CK_CarrierGeoGroup_Cardinality", "\"CardinalityPolicy\" IN ('OneToOne','ManyCanonicalToOneProvider','OneCanonicalToManyProvider')");
                    table.ForeignKey(
                        name: "FK_CarrierGeographicMappingGroups_CarrierGeographicMappingRele~",
                        column: x => x.MappingReleaseId,
                        principalTable: "CarrierGeographicMappingReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CarrierGeographicMappingPublications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MappingReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicationKind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublishedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SupersededAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierGeographicMappingPublications", x => x.Id);
                    table.CheckConstraint("CK_CarrierGeoPublication_Kind", "\"PublicationKind\" IN ('Initial','Publish','Rollback','Supersede')");
                    table.ForeignKey(
                        name: "FK_CarrierGeographicMappingPublications_CarrierGeographicMappi~",
                        column: x => x.MappingReleaseId,
                        principalTable: "CarrierGeographicMappingReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GeographicAudit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorRole = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AggregateType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    CatalogReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    MappingReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    CountryScopeIso2 = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: true),
                    ProviderCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Environment = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    AccountScope = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ValidationChecksumSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    EvidenceObjectKey = table.Column<string>(type: "text", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeographicAudit", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GeographicAudit_CarrierGeographicMappingReleases_MappingRel~",
                        column: x => x.MappingReleaseId,
                        principalTable: "CarrierGeographicMappingReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeographicAudit_GeographicCatalogReleases_CatalogReleaseId",
                        column: x => x.CatalogReleaseId,
                        principalTable: "GeographicCatalogReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AddressAdmissionEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerDomain = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    AddressFingerprintSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CatalogReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CountryPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    AdministrativeAreaPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocalityPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    DistrictPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ZonePlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    AdmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NormalizationRuleVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RawInputEvidenceObjectKey = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AddressAdmissionEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AddressAdmissionEvidence_GeographicCatalogReleases_CatalogR~",
                        column: x => x.CatalogReleaseId,
                        principalTable: "GeographicCatalogReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AddressAdmissionEvidence_GeographicPlaces_AdministrativeAre~",
                        column: x => x.AdministrativeAreaPlaceId,
                        principalTable: "GeographicPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AddressAdmissionEvidence_GeographicPlaces_CountryPlaceId",
                        column: x => x.CountryPlaceId,
                        principalTable: "GeographicPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AddressAdmissionEvidence_GeographicPlaces_DistrictPlaceId",
                        column: x => x.DistrictPlaceId,
                        principalTable: "GeographicPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AddressAdmissionEvidence_GeographicPlaces_LocalityPlaceId",
                        column: x => x.LocalityPlaceId,
                        principalTable: "GeographicPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AddressAdmissionEvidence_GeographicPlaces_ZonePlaceId",
                        column: x => x.ZonePlaceId,
                        principalTable: "GeographicPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AddressNormalizationRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CountryPlaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    LanguageTag = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: true),
                    RuleKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RuleVersion = table.Column<long>(type: "bigint", nullable: false),
                    RuleDefinitionJson = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IntroducedCatalogReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    RetiredCatalogReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AddressNormalizationRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AddressNormalizationRules_GeographicCatalogReleases_Introdu~",
                        column: x => x.IntroducedCatalogReleaseId,
                        principalTable: "GeographicCatalogReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AddressNormalizationRules_GeographicCatalogReleases_Retired~",
                        column: x => x.RetiredCatalogReleaseId,
                        principalTable: "GeographicCatalogReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AddressNormalizationRules_GeographicPlaces_CountryPlaceId",
                        column: x => x.CountryPlaceId,
                        principalTable: "GeographicPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CarrierGeographicOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Environment = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    AccountScopeKind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    AccountScopeId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CanonicalPlaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderPlaceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderPlaceId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProviderPlaceCode = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierGeographicOverrides", x => x.Id);
                    table.CheckConstraint("CK_CarrierGeoOverride_Window", "\"EffectiveToUtc\" IS NULL OR \"EffectiveToUtc\" > \"EffectiveFromUtc\"");
                    table.ForeignKey(
                        name: "FK_CarrierGeographicOverrides_CarrierProviders_CarrierProvider~",
                        column: x => x.CarrierProviderId,
                        principalTable: "CarrierProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierGeographicOverrides_GeographicPlaces_CanonicalPlaceId",
                        column: x => x.CanonicalPlaceId,
                        principalTable: "GeographicPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CarrierGeographicSyncCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SyncRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderPlaceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderPlaceId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProviderPlaceCode = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: false),
                    ProviderNormalizedName = table.Column<string>(type: "text", nullable: false),
                    ProviderParentPlaceId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CandidateCanonicalPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Confidence = table.Column<short>(type: "smallint", nullable: false),
                    CandidateStatus = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ReviewReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierGeographicSyncCandidates", x => x.Id);
                    table.CheckConstraint("CK_CarrierGeoSyncCandidate_Confidence", "\"Confidence\" BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_CarrierGeographicSyncCandidates_CarrierGeographicSyncRuns_S~",
                        column: x => x.SyncRunId,
                        principalTable: "CarrierGeographicSyncRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierGeographicSyncCandidates_CarrierProviders_CarrierPro~",
                        column: x => x.CarrierProviderId,
                        principalTable: "CarrierProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierGeographicSyncCandidates_GeographicPlaces_CandidateC~",
                        column: x => x.CandidateCanonicalPlaceId,
                        principalTable: "GeographicPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GeographicCatalogReleasePlaces",
                columns: table => new
                {
                    CatalogReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlaceStatus = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeographicCatalogReleasePlaces", x => new { x.CatalogReleaseId, x.PlaceId });
                    table.ForeignKey(
                        name: "FK_GeographicCatalogReleasePlaces_GeographicCatalogReleases_Ca~",
                        column: x => x.CatalogReleaseId,
                        principalTable: "GeographicCatalogReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeographicCatalogReleasePlaces_GeographicPlaces_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "GeographicPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GeographicCountryProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CountryPlaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Iso2 = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    Iso3 = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    DefaultLanguageTag = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    SupportedLanguageTags = table.Column<string[]>(type: "text[]", nullable: false),
                    RequiredLevelCodesForDelivery = table.Column<string[]>(type: "text[]", nullable: false),
                    RequiredLevelCodesForOperations = table.Column<string[]>(type: "text[]", nullable: false),
                    PostalCodePolicy = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    PostalCodeRegex = table.Column<string>(type: "text", nullable: true),
                    RequiresStreetLine1 = table.Column<bool>(type: "boolean", nullable: false),
                    AllowsCoordinates = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresCoordinateProvenance = table.Column<bool>(type: "boolean", nullable: false),
                    OfflinePackageEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IntroducedCatalogReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    RetiredCatalogReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeographicCountryProfiles", x => x.Id);
                    table.CheckConstraint("CK_GeoCountryProfile_Postal", "\"PostalCodePolicy\" IN ('NotUsed','Optional','Required','Forbidden')");
                    table.ForeignKey(
                        name: "FK_GeographicCountryProfiles_GeographicCatalogReleases_Introdu~",
                        column: x => x.IntroducedCatalogReleaseId,
                        principalTable: "GeographicCatalogReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeographicCountryProfiles_GeographicCatalogReleases_Retired~",
                        column: x => x.RetiredCatalogReleaseId,
                        principalTable: "GeographicCatalogReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeographicCountryProfiles_GeographicPlaces_CountryPlaceId",
                        column: x => x.CountryPlaceId,
                        principalTable: "GeographicPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GeographicPlaceAliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CountryPlaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    LanguageTag = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: true),
                    AliasKind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Alias = table.Column<string>(type: "text", nullable: false),
                    NormalizedAlias = table.Column<string>(type: "text", nullable: false),
                    SearchAlias = table.Column<string>(type: "text", nullable: false),
                    Confidence = table.Column<short>(type: "smallint", nullable: false),
                    Source = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsAmbiguous = table.Column<bool>(type: "boolean", nullable: false),
                    IntroducedCatalogReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    RetiredCatalogReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeographicPlaceAliases", x => x.Id);
                    table.CheckConstraint("CK_GeoPlaceAlias_Confidence", "\"Confidence\" BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_GeoPlaceAlias_Kind", "\"AliasKind\" IN ('Spelling','Legacy','Transliteration','Abbreviation','Historic','ProviderLabel','MigrationEvidence')");
                    table.ForeignKey(
                        name: "FK_GeographicPlaceAliases_GeographicCatalogReleases_Introduced~",
                        column: x => x.IntroducedCatalogReleaseId,
                        principalTable: "GeographicCatalogReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeographicPlaceAliases_GeographicCatalogReleases_RetiredCat~",
                        column: x => x.RetiredCatalogReleaseId,
                        principalTable: "GeographicCatalogReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeographicPlaceAliases_GeographicPlaces_CountryPlaceId",
                        column: x => x.CountryPlaceId,
                        principalTable: "GeographicPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeographicPlaceAliases_GeographicPlaces_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "GeographicPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GeographicPlaceHierarchyEdges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentPlaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChildPlaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CountryPlaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Depth = table.Column<int>(type: "integer", nullable: false),
                    IntroducedCatalogReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    RetiredCatalogReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeographicPlaceHierarchyEdges", x => x.Id);
                    table.CheckConstraint("CK_GeoHierarchy_Depth", "\"Depth\" >= 1");
                    table.CheckConstraint("CK_GeoHierarchy_NoSelf", "\"ParentPlaceId\" <> \"ChildPlaceId\"");
                    table.ForeignKey(
                        name: "FK_GeographicPlaceHierarchyEdges_GeographicCatalogReleases_Int~",
                        column: x => x.IntroducedCatalogReleaseId,
                        principalTable: "GeographicCatalogReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeographicPlaceHierarchyEdges_GeographicCatalogReleases_Ret~",
                        column: x => x.RetiredCatalogReleaseId,
                        principalTable: "GeographicCatalogReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeographicPlaceHierarchyEdges_GeographicPlaces_ChildPlaceId",
                        column: x => x.ChildPlaceId,
                        principalTable: "GeographicPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeographicPlaceHierarchyEdges_GeographicPlaces_CountryPlace~",
                        column: x => x.CountryPlaceId,
                        principalTable: "GeographicPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeographicPlaceHierarchyEdges_GeographicPlaces_ParentPlaceId",
                        column: x => x.ParentPlaceId,
                        principalTable: "GeographicPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GeographicPlaceLevels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CountryPlaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    LevelCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ParentLevelCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    PlaceKind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    RequiredForDelivery = table.Column<bool>(type: "boolean", nullable: false),
                    RequiredForOperations = table.Column<bool>(type: "boolean", nullable: false),
                    RequiredForTax = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayNameKey = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    AllowsChildren = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IntroducedCatalogReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    RetiredCatalogReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeographicPlaceLevels", x => x.Id);
                    table.CheckConstraint("CK_GeoPlaceLevel_Kind", "\"PlaceKind\" IN ('AdministrativeArea','Locality','District','Zone')");
                    table.CheckConstraint("CK_GeoPlaceLevel_Sort", "\"SortOrder\" > 0");
                    table.ForeignKey(
                        name: "FK_GeographicPlaceLevels_GeographicCatalogReleases_IntroducedC~",
                        column: x => x.IntroducedCatalogReleaseId,
                        principalTable: "GeographicCatalogReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeographicPlaceLevels_GeographicCatalogReleases_RetiredCata~",
                        column: x => x.RetiredCatalogReleaseId,
                        principalTable: "GeographicCatalogReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeographicPlaceLevels_GeographicPlaces_CountryPlaceId",
                        column: x => x.CountryPlaceId,
                        principalTable: "GeographicPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GeographicPlaceNames",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    LanguageTag = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    ScriptCode = table.Column<string>(type: "character(4)", fixedLength: true, maxLength: 4, nullable: true),
                    NameKind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    NormalizedName = table.Column<string>(type: "text", nullable: false),
                    SearchName = table.Column<string>(type: "text", nullable: false),
                    DisplayPriority = table.Column<int>(type: "integer", nullable: false),
                    IsPreferred = table.Column<bool>(type: "boolean", nullable: false),
                    ValidFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IntroducedCatalogReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    RetiredCatalogReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeographicPlaceNames", x => x.Id);
                    table.CheckConstraint("CK_GeoPlaceName_Kind", "\"NameKind\" IN ('Official','Display','Short','Search','Historic','Transliteration','ProviderLabel')");
                    table.CheckConstraint("CK_GeoPlaceName_Window", "\"ValidToUtc\" IS NULL OR \"ValidToUtc\" > \"ValidFromUtc\"");
                    table.ForeignKey(
                        name: "FK_GeographicPlaceNames_GeographicCatalogReleases_IntroducedCa~",
                        column: x => x.IntroducedCatalogReleaseId,
                        principalTable: "GeographicCatalogReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeographicPlaceNames_GeographicCatalogReleases_RetiredCatal~",
                        column: x => x.RetiredCatalogReleaseId,
                        principalTable: "GeographicCatalogReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeographicPlaceNames_GeographicPlaces_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "GeographicPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlaceDeprecations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SuccessorPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CatalogReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeprecationKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaceDeprecations", x => x.Id);
                    table.CheckConstraint("CK_PlaceDeprecation_NoSelf", "\"SuccessorPlaceId\" IS NULL OR \"SuccessorPlaceId\" <> \"PlaceId\"");
                    table.ForeignKey(
                        name: "FK_PlaceDeprecations_GeographicCatalogReleases_CatalogReleaseId",
                        column: x => x.CatalogReleaseId,
                        principalTable: "GeographicCatalogReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlaceDeprecations_GeographicPlaces_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "GeographicPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlaceDeprecations_GeographicPlaces_SuccessorPlaceId",
                        column: x => x.SuccessorPlaceId,
                        principalTable: "GeographicPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlaceMergeHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcePlaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetPlaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CatalogReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    MergeKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaceMergeHistory", x => x.Id);
                    table.CheckConstraint("CK_PlaceMergeHistory_NoSelf", "\"SourcePlaceId\" <> \"TargetPlaceId\"");
                    table.ForeignKey(
                        name: "FK_PlaceMergeHistory_GeographicCatalogReleases_CatalogReleaseId",
                        column: x => x.CatalogReleaseId,
                        principalTable: "GeographicCatalogReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlaceMergeHistory_GeographicPlaces_SourcePlaceId",
                        column: x => x.SourcePlaceId,
                        principalTable: "GeographicPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlaceMergeHistory_GeographicPlaces_TargetPlaceId",
                        column: x => x.TargetPlaceId,
                        principalTable: "GeographicPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServiceRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuyerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceTitleSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ServiceCategorySnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ServiceStartingPriceSnapshot = table.Column<decimal>(type: "numeric", nullable: true),
                    ServicePricingTypeSnapshot = table.Column<int>(type: "integer", nullable: true),
                    ServiceCurrencySnapshot = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    ServicePackagesSnapshot = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    RequestMessage = table.Column<string>(type: "text", nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                name: "CashboxTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashboxId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TransactionType = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaymentMethod = table.Column<int>(type: "integer", nullable: true),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsOpeningBalance = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashboxTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashboxTransactions_Cashboxes_CashboxId",
                        column: x => x.CashboxId,
                        principalTable: "Cashboxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashboxTransactions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpenseCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpenseDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    PaymentMethod = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    CashboxId = table.Column<Guid>(type: "uuid", nullable: true),
                    BankId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceiptPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReferenceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsRecurring = table.Column<bool>(type: "boolean", nullable: false),
                    RecurringFrequency = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RequiresApproval = table.Column<bool>(type: "boolean", nullable: false),
                    IsApproved = table.Column<bool>(type: "boolean", nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AccountingMutationVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Expenses_Banks_BankId",
                        column: x => x.BankId,
                        principalTable: "Banks",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Expenses_Cashboxes_CashboxId",
                        column: x => x.CashboxId,
                        principalTable: "Cashboxes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Expenses_ExpenseCategories_ExpenseCategoryId",
                        column: x => x.ExpenseCategoryId,
                        principalTable: "ExpenseCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Expenses_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AffiliateAttributions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    AffiliateAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferralCode = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    AttributedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PromotionLinkId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliateAttributions", x => x.Id);
                    table.CheckConstraint("CK_AffiliateAttributions_Source", "\"Source\" IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_AffiliateAttributions_AffiliateAccounts_AffiliateAccountId",
                        column: x => x.AffiliateAccountId,
                        principalTable: "AffiliateAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AffiliateAttributions_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CouponReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CouponId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReservedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReleasedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CouponReservations", x => x.Id);
                    table.CheckConstraint("CK_CouponReservation_Status", "\"Status\" IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_CouponReservations_Coupons_CouponId",
                        column: x => x.CouponId,
                        principalTable: "Coupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CouponReservations_OnlineCustomers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "OnlineCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CouponReservations_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CouponUsages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CouponId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CouponCode = table.Column<string>(type: "text", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OrderSubtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CouponUsages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CouponUsages_Coupons_CouponId",
                        column: x => x.CouponId,
                        principalTable: "Coupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CouponUsages_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderRefunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    RefundId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IsFullRefund = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsPostSettlementRefund = table.Column<bool>(type: "boolean", nullable: false),
                    SettlementBatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    SettlementType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SettlementId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PayoutBatchId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SettledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinancialPostingStatus = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    FinancialPostingPendingAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    FinancialPostedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinancialPostingJournalId = table.Column<Guid>(type: "uuid", nullable: true),
                    FinancialPostingFailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RefundedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderRefunds", x => x.Id);
                    table.UniqueConstraint("AK_OrderRefunds_TenantId_OrderId_Id", x => new { x.TenantId, x.OrderId, x.Id });
                    table.CheckConstraint("CK_OrderRefunds_FinancialPostingStatus_Valid", "\"FinancialPostingStatus\" IN (1, 2, 3) AND (\"FinancialPostingStatus\" <> 1 OR \"FinancialPostingPendingAtUtc\" IS NOT NULL) AND (\"FinancialPostingStatus\" <> 2 OR (\"FinancialPostedAtUtc\" IS NOT NULL AND \"FinancialPostingJournalId\" IS NOT NULL)) AND (\"FinancialPostingStatus\" <> 3 OR \"FinancialPostingFailureReason\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_OrderRefunds_Orders_TenantId_OrderId",
                        columns: x => new { x.TenantId, x.OrderId },
                        principalTable: "Orders",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderRefunds_PaymentSettlementBatches_SettlementBatchId",
                        column: x => x.SettlementBatchId,
                        principalTable: "PaymentSettlementBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentIntents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    PaymentMethod = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    ReferenceCode = table.Column<string>(type: "text", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProviderIntentId = table.Column<string>(type: "text", nullable: true),
                    ProviderPaymentId = table.Column<string>(type: "text", nullable: true),
                    PayoutBatchId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PaymentReference = table.Column<string>(type: "text", nullable: false),
                    RedirectUrl = table.Column<string>(type: "text", nullable: true),
                    ClientSecretEncrypted = table.Column<string>(type: "text", nullable: true),
                    WebhookUrl = table.Column<string>(type: "text", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    AuthorizedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    FailureCode = table.Column<string>(type: "text", nullable: true),
                    ProviderMetadata = table.Column<string>(type: "text", nullable: true),
                    LastWebhookEventId = table.Column<string>(type: "text", nullable: true),
                    WebhookEventCount = table.Column<int>(type: "integer", nullable: false),
                    WalletType = table.Column<string>(type: "text", nullable: true),
                    WalletPhoneNumber = table.Column<string>(type: "text", nullable: true),
                    ProofImageUrl = table.Column<string>(type: "text", nullable: true),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerifiedByAdmin = table.Column<string>(type: "text", nullable: true),
                    RejectionReason = table.Column<string>(type: "text", nullable: true),
                    ProofImageExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProofImageDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    InventoryCommitted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentIntents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentIntents_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PromotionUsages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    PromotionName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OrderSubtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AppliedToItemsJson = table.Column<string>(type: "text", nullable: true),
                    AppliedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionUsages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromotionUsages_OnlineCustomers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "OnlineCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PromotionUsages_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PromotionUsages_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Shipments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ServiceLevel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ShippingCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    IsCashOnDelivery = table.Column<bool>(type: "boolean", nullable: false),
                    CodAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CodCollected = table.Column<bool>(type: "boolean", nullable: false),
                    CodCollectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TrackingNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TrackingUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AwbNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ShipmentNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ShippingMethodId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShippingMethodName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RecipientName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecipientPhone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RecipientEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ShippingAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ShippingAddressLine2 = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ShippingCity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ShippingState = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ShippingZipCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ShippingCountry = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    Longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    DestinationAddressAdmissionEvidenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    DestinationGeographicCatalogReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    DestinationCountryPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    DestinationAdministrativeAreaPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    DestinationLocalityPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    DestinationDistrictPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    DestinationZonePlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    DestinationCountryDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DestinationAdministrativeAreaDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DestinationLocalityDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DestinationDistrictDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DestinationZoneDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DestinationStreet = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DestinationStreet2 = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DestinationBuilding = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    DestinationFloor = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DestinationApartment = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DestinationLandmark = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DestinationGeographicSnapshotCreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DestinationGeographicSnapshotVersion = table.Column<int>(type: "integer", nullable: true),
                    OriginWarehouseId = table.Column<Guid>(type: "uuid", nullable: true),
                    OriginAddressAdmissionEvidenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    OriginGeographicCatalogReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    OriginCountryPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    OriginAdministrativeAreaPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    OriginLocalityPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    OriginDistrictPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    OriginZonePlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    OriginCountryIso2 = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    OriginCountryDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OriginAdministrativeAreaDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OriginLocalityDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OriginDistrictDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OriginZoneDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OriginContactName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OriginContactPhone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OriginStreet = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OriginGeographicSnapshotCreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OriginGeographicSnapshotVersion = table.Column<int>(type: "integer", nullable: true),
                    OriginSnapshotJson = table.Column<string>(type: "text", nullable: true),
                    CarrierGeographicMappingReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    CarrierGeographicMappingVersion = table.Column<long>(type: "bigint", nullable: true),
                    CarrierGeographicMappingReleaseKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CarrierGeographicEnvironment = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    CarrierGeographicAccountScope = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CarrierGeographicAccountScopeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CarrierGeographicMappingSnapshotJson = table.Column<string>(type: "jsonb", nullable: true),
                    CarrierGeographicResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WeightKg = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: true),
                    PackageCount = table.Column<int>(type: "integer", nullable: false),
                    PackageDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    EstimatedDeliveryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActualDeliveryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReadyForPickupAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PickedUpAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ShippedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OutForDeliveryAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReturnedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CarrierMetadata = table.Column<string>(type: "text", nullable: true),
                    CarrierCreateResponse = table.Column<string>(type: "text", nullable: true),
                    ExternalReferenceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CarrierRequestId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ExternalRegistrationState = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ExternalRegistrationAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ExternalRegistrationRequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExternalRegistrationLastAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExternalRegistrationCompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExternalRegistrationFailedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExternalRegistrationError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CarrierTrackingResponse = table.Column<string>(type: "text", nullable: true),
                    LastCarrierSyncAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReturnReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DeliveryAttempts = table.Column<int>(type: "integer", nullable: false),
                    LabelUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LabelData = table.Column<string>(type: "text", nullable: true),
                    LabelFormat = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Shipments_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipments_ShippingMethods_ShippingMethodId",
                        column: x => x.ShippingMethodId,
                        principalTable: "ShippingMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseInvoicePayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseInvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    PaymentMethod = table.Column<int>(type: "integer", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoicePayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoicePayments_PurchaseInvoices_PurchaseInvoiceId",
                        column: x => x.PurchaseInvoiceId,
                        principalTable: "PurchaseInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupplierPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseInvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaymentMethod = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
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
                    table.PrimaryKey("PK_SupplierPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierPayments_PurchaseInvoices_PurchaseInvoiceId",
                        column: x => x.PurchaseInvoiceId,
                        principalTable: "PurchaseInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SupplierPayments_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Returns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnNumber = table.Column<string>(type: "text", nullable: false),
                    ReturnDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReturnType = table.Column<int>(type: "integer", nullable: false),
                    OriginalInvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    OriginalInvoiceNumber = table.Column<string>(type: "text", nullable: true),
                    OriginalPurchaseInvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Returns", x => x.Id);
                    table.CheckConstraint("CK_Returns_PurchaseAuthorityLineage", "(\"ReturnType\" <> 0) OR (\"OriginalPurchaseInvoiceId\" IS NOT NULL AND \"OriginalInvoiceId\" IS NULL AND \"SupplierId\" IS NOT NULL AND \"CustomerId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_Returns_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Returns_PurchaseInvoices_TenantId_OriginalPurchaseInvoiceId",
                        columns: x => new { x.TenantId, x.OriginalPurchaseInvoiceId },
                        principalTable: "PurchaseInvoices",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Returns_SalesInvoices_OriginalInvoiceId",
                        column: x => x.OriginalInvoiceId,
                        principalTable: "SalesInvoices",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Returns_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Returns_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoicePayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesInvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    PaymentMethod = table.Column<int>(type: "integer", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoicePayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesInvoicePayments_SalesInvoices_SalesInvoiceId",
                        column: x => x.SalesInvoiceId,
                        principalTable: "SalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductVariants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    SKU = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Barcode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AttributeCombinationKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OnlinePrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CostPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SemanticLifecycleState = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    SemanticReplacementVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    SemanticLineageRootVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    SemanticOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    SemanticOperationKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SemanticSupersededAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MediaId = table.Column<Guid>(type: "uuid", nullable: true),
                    ImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Weight = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    StockUpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProjectionMutationVersion = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductVariants_ProductMedia_TenantId_ProductId_MediaId",
                        columns: x => new { x.TenantId, x.ProductId, x.MediaId },
                        principalTable: "ProductMedia",
                        principalColumns: new[] { "TenantId", "ProductId", "Id" });
                    table.ForeignKey(
                        name: "FK_ProductVariants_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubPaymentAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProviderIntentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProviderTransactionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubPaymentAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommerceHubPaymentAttempts_CommerceHubPayments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "CommerceHubPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubPaymentLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProviderEventId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RawResponse = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubPaymentLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommerceHubPaymentLogs_CommerceHubPayments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "CommerceHubPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubPaymentItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubPaymentItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommerceHubPaymentItems_CommerceHubPayments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "CommerceHubPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CommerceHubPaymentItems_CommerceHubSubOrders_SubOrderId",
                        column: x => x.SubOrderId,
                        principalTable: "CommerceHubSubOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubRefunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    RefundRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: true),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CommissionReversalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PromoDiscountReversalAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    VendorEarningReversalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    GatewayStatus = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsFullRefund = table.Column<bool>(type: "boolean", nullable: false),
                    IsPostPayoutAdjustment = table.Column<bool>(type: "boolean", nullable: false),
                    ProcessingStartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubRefunds", x => x.Id);
                    table.CheckConstraint("CK_CommerceHubRefunds_Amount_Positive", "\"Amount\" > 0");
                    table.CheckConstraint("CK_CommerceHubRefunds_Completed_State_Complete", "\"Status\" <> 2 OR \"ProcessedAtUtc\" IS NOT NULL");
                    table.CheckConstraint("CK_CommerceHubRefunds_Currency_EGP", "\"Currency\" = 'EGP'");
                    table.CheckConstraint("CK_CommerceHubRefunds_Reversals_NonNegative", "\"CommissionReversalAmount\" >= 0 AND \"PromoDiscountReversalAmount\" >= 0 AND \"VendorEarningReversalAmount\" >= 0");
                    table.CheckConstraint("CK_CommerceHubRefunds_Reversals_Total_Amount", "\"CommissionReversalAmount\" + \"PromoDiscountReversalAmount\" + \"VendorEarningReversalAmount\" = \"Amount\"");
                    table.ForeignKey(
                        name: "FK_CommerceHubRefunds_CommerceHubPayments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "CommerceHubPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CommerceHubRefunds_CommerceHubSubOrders_SubOrderId",
                        column: x => x.SubOrderId,
                        principalTable: "CommerceHubSubOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubSubOrderItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductName = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    VariantName = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    VariantAttributesJson = table.Column<string>(type: "text", nullable: true),
                    ImageUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PlatformCommissionRate = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false),
                    PlatformCommissionAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FinalPlatformCommissionAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    PromoDiscountAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    DeliveredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubSubOrderItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommerceHubSubOrderItems_CommerceHubSubOrders_SubOrderId",
                        column: x => x.SubOrderId,
                        principalTable: "CommerceHubSubOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubSubOrderStatusHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousStatus = table.Column<int>(type: "integer", nullable: false),
                    NewStatus = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubSubOrderStatusHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommerceHubSubOrderStatusHistory_CommerceHubSubOrders_SubOr~",
                        column: x => x.SubOrderId,
                        principalTable: "CommerceHubSubOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AbandonedCartRecoveryLogs",
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
                    table.PrimaryKey("PK_AbandonedCartRecoveryLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbandonedCartRecoveryLogs_AbandonedCarts_AbandonedCartId",
                        column: x => x.AbandonedCartId,
                        principalTable: "AbandonedCarts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubVendorPaymentAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdminId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "EGP"),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubVendorPaymentAuditLogs", x => x.Id);
                    table.CheckConstraint("CK_CommerceHubVendorPaymentAuditLogs_Action", "\"Action\" IN (0, 1)");
                    table.CheckConstraint("CK_CommerceHubVendorPaymentAuditLogs_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_CommerceHubVendorPaymentAuditLogs_CommerceHubVendorPaymentR~",
                        column: x => x.PaymentRequestId,
                        principalTable: "CommerceHubVendorPaymentRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubVendorPaymentProofs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubVendorPaymentProofs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommerceHubVendorPaymentProofs_CommerceHubVendorPaymentRequ~",
                        column: x => x.PaymentRequestId,
                        principalTable: "CommerceHubVendorPaymentRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CarrierGeographicMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MappingReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    MappingGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    CarrierProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalPlaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CountryPlaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalPlaceKind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ProviderPlaceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderPlaceId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProviderPlaceCode = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: false),
                    ProviderNormalizedName = table.Column<string>(type: "text", nullable: false),
                    ProviderParentMappingId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequiredForOperation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MappingStatus = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    VerificationStatus = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Source = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierGeographicMappings", x => x.Id);
                    table.CheckConstraint("CK_CarrierGeoMapping_Status", "\"MappingStatus\" IN ('Draft','Active','Deprecated','Retired','Rejected')");
                    table.CheckConstraint("CK_CarrierGeoMapping_Verification", "\"VerificationStatus\" IN ('Imported','AutoCarried','Reviewed','ManuallyApproved','Rejected')");
                    table.CheckConstraint("CK_CarrierGeoMapping_Window", "\"EffectiveToUtc\" IS NULL OR \"EffectiveToUtc\" > \"EffectiveFromUtc\"");
                    table.ForeignKey(
                        name: "FK_CarrierGeographicMappings_CarrierGeographicMappingGroups_Ma~",
                        column: x => x.MappingGroupId,
                        principalTable: "CarrierGeographicMappingGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierGeographicMappings_CarrierGeographicMappingReleases_~",
                        column: x => x.MappingReleaseId,
                        principalTable: "CarrierGeographicMappingReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierGeographicMappings_CarrierGeographicMappings_Provide~",
                        column: x => x.ProviderParentMappingId,
                        principalTable: "CarrierGeographicMappings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierGeographicMappings_CarrierProviders_CarrierProviderId",
                        column: x => x.CarrierProviderId,
                        principalTable: "CarrierProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierGeographicMappings_GeographicPlaces_CanonicalPlaceId",
                        column: x => x.CanonicalPlaceId,
                        principalTable: "GeographicPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierGeographicMappings_GeographicPlaces_CountryPlaceId",
                        column: x => x.CountryPlaceId,
                        principalTable: "GeographicPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GeographicCatalogReleaseEdges",
                columns: table => new
                {
                    CatalogReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    HierarchyEdgeId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeographicCatalogReleaseEdges", x => new { x.CatalogReleaseId, x.HierarchyEdgeId });
                    table.ForeignKey(
                        name: "FK_GeographicCatalogReleaseEdges_GeographicCatalogReleases_Cat~",
                        column: x => x.CatalogReleaseId,
                        principalTable: "GeographicCatalogReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeographicCatalogReleaseEdges_GeographicPlaceHierarchyEdges~",
                        column: x => x.HierarchyEdgeId,
                        principalTable: "GeographicPlaceHierarchyEdges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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
                    table.CheckConstraint("CK_ServiceReviews_Rating_Range", "\"Rating\" >= 1 AND \"Rating\" <= 5");
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

            migrationBuilder.CreateTable(
                name: "PaymentSettlementOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SettlementBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentIntentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentSettlementOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentSettlementOrders_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentSettlementOrders_PaymentIntents_PaymentIntentId",
                        column: x => x.PaymentIntentId,
                        principalTable: "PaymentIntents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentSettlementOrders_PaymentSettlementBatches_Settlement~",
                        column: x => x.SettlementBatchId,
                        principalTable: "PaymentSettlementBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentWebhookLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    ProviderEventId = table.Column<string>(type: "text", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    PaymentIntentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    RawPayload = table.Column<string>(type: "text", nullable: false),
                    Signature = table.Column<string>(type: "text", nullable: true),
                    SignatureVerified = table.Column<bool>(type: "boolean", nullable: false),
                    ProcessingStatus = table.Column<int>(type: "integer", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessingError = table.Column<string>(type: "text", nullable: true),
                    ProcessingDurationMs = table.Column<long>(type: "bigint", nullable: true),
                    SourceIp = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentWebhookLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentWebhookLogs_PaymentIntents_PaymentIntentId",
                        column: x => x.PaymentIntentId,
                        principalTable: "PaymentIntents",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubListings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    RestorePublicationOnActivate = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CommerceHubCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommerceHubPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CommerceHubPriceOverrideUpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CommerceHubPriceOverrideUpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CommerceHubPriceOverrideReason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CommerceHubPriceOverrideMutationVersion = table.Column<int>(type: "integer", nullable: false),
                    AttributesJson = table.Column<string>(type: "text", nullable: true),
                    AttributeSchemaSnapshotJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubListings", x => x.Id);
                    table.CheckConstraint("CK_CommerceHubListings_CommerceHubPrice_Positive", "\"CommerceHubPrice\" IS NULL OR \"CommerceHubPrice\" > 0");
                    table.ForeignKey(
                        name: "FK_CommerceHubListings_ProductVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommerceHubListings_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: true),
                    SKU = table.Column<string>(type: "text", nullable: false),
                    ProductName = table.Column<string>(type: "text", nullable: false),
                    VariantName = table.Column<string>(type: "text", nullable: true),
                    VariantAttributesJson = table.Column<string>(type: "text", nullable: true),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    Total = table.Column<decimal>(type: "numeric", nullable: false),
                    CostPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    AffiliateCommissionEligible = table.Column<bool>(type: "boolean", nullable: false),
                    AffiliateCommissionPolicyIdSnapshot = table.Column<Guid>(type: "uuid", nullable: true),
                    AffiliateCommissionRateSnapshot = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    AffiliateMaxCommissionPerItemSnapshot = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    AffiliateCommissionBaseAmountSnapshot = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    AffiliateCommissionAmountSnapshot = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    AffiliateCommissionSnapshotCapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RefundedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItems", x => x.Id);
                    table.UniqueConstraint("AK_OrderItems_OrderId_Id", x => new { x.OrderId, x.Id });
                    table.CheckConstraint("CK_OrderItems_AffiliateCommissionSnapshot_Valid", "(\"AffiliateCommissionEligible\" = true AND \"AffiliateCommissionSnapshotCapturedAtUtc\" IS NOT NULL AND \"AffiliateCommissionPolicyIdSnapshot\" IS NOT NULL AND \"AffiliateCommissionRateSnapshot\" > 0 AND \"AffiliateCommissionRateSnapshot\" <= 0.500000 AND \"AffiliateCommissionBaseAmountSnapshot\" > 0 AND \"AffiliateCommissionAmountSnapshot\" > 0 AND (\"AffiliateMaxCommissionPerItemSnapshot\" IS NULL OR \"AffiliateMaxCommissionPerItemSnapshot\" > 0)) OR (\"AffiliateCommissionEligible\" = false AND \"AffiliateCommissionSnapshotCapturedAtUtc\" IS NULL AND \"AffiliateCommissionPolicyIdSnapshot\" IS NULL AND \"AffiliateCommissionRateSnapshot\" IS NULL AND \"AffiliateCommissionBaseAmountSnapshot\" IS NULL AND \"AffiliateCommissionAmountSnapshot\" IS NULL AND (\"AffiliateMaxCommissionPerItemSnapshot\" IS NULL OR \"AffiliateMaxCommissionPerItemSnapshot\" = 0))");
                    table.CheckConstraint("CK_OrderItems_PaymentAmounts_Valid", "\"PaidAmount\" >= 0 AND \"RefundedAmount\" >= 0 AND \"RefundedAmount\" <= \"PaidAmount\"");
                    table.ForeignKey(
                        name: "FK_OrderItems_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderItems_ProductVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrderItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderItems_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrderItems_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ProductVariantAttributes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariantAttributes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductVariantAttributes_ProductVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductVariantStocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReservedQuantity = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DamagedQuantity = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ProjectionMutationVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariantStocks", x => x.Id);
                    table.CheckConstraint("CK_ProductVariantStocks_DamagedQuantity_NonNegative", "\"DamagedQuantity\" >= 0");
                    table.CheckConstraint("CK_ProductVariantStocks_Quantity_NonNegative", "\"Quantity\" >= 0");
                    table.CheckConstraint("CK_ProductVariantStocks_ReservedQuantity_NonNegative", "\"ReservedQuantity\" >= 0");
                    table.CheckConstraint("CK_ProductVariantStocks_ReservedQuantity_Within_Quantity", "\"Quantity\" >= \"ReservedQuantity\"");
                    table.ForeignKey(
                        name: "FK_ProductVariantStocks_ProductVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductVariantStocks_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseInvoiceItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseInvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    ConversionFactor = table.Column<decimal>(type: "numeric", nullable: false),
                    BaseQuantity = table.Column<decimal>(type: "numeric", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    TaxRate = table.Column<decimal>(type: "numeric", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoiceItems", x => x.Id);
                    table.CheckConstraint("CK_PurchaseInvoiceItems_BaseQuantity_Positive", "\"BaseQuantity\" > 0");
                    table.CheckConstraint("CK_PurchaseInvoiceItems_ConversionFactor_Positive", "\"ConversionFactor\" > 0");
                    table.CheckConstraint("CK_PurchaseInvoiceItems_Quantity_Positive", "\"Quantity\" > 0");
                    table.CheckConstraint("CK_PurchaseInvoiceItems_TaxRate_Valid", "\"TaxRate\" >= 0 AND \"TaxRate\" <= 100");
                    table.CheckConstraint("CK_PurchaseInvoiceItems_UnitPrice_Positive", "\"UnitPrice\" > 0");
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceItems_ProductVariants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceItems_PurchaseInvoices_PurchaseInvoiceId",
                        column: x => x.PurchaseInvoiceId,
                        principalTable: "PurchaseInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceItems_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ReturnItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    ConversionFactor = table.Column<decimal>(type: "numeric", nullable: false),
                    BaseQuantity = table.Column<decimal>(type: "numeric", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    TaxRate = table.Column<decimal>(type: "numeric", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    CostPriceSnapshot = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    CostAmountSnapshot = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnItems", x => x.Id);
                    table.CheckConstraint("CK_ReturnItems_Amounts_NonNegative", "\"TaxAmount\" >= 0 AND \"TotalAmount\" >= 0");
                    table.CheckConstraint("CK_ReturnItems_BaseQuantity_Positive", "\"BaseQuantity\" > 0");
                    table.CheckConstraint("CK_ReturnItems_ConversionFactor_Positive", "\"ConversionFactor\" > 0");
                    table.CheckConstraint("CK_ReturnItems_Quantity_Positive", "\"Quantity\" > 0");
                    table.CheckConstraint("CK_ReturnItems_TaxRate_Valid", "\"TaxRate\" >= 0 AND \"TaxRate\" <= 100");
                    table.CheckConstraint("CK_ReturnItems_UnitPrice_NonNegative", "\"UnitPrice\" >= 0");
                    table.ForeignKey(
                        name: "FK_ReturnItems_ProductVariants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReturnItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReturnItems_Returns_ReturnId",
                        column: x => x.ReturnId,
                        principalTable: "Returns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReturnItems_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoiceItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesInvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    ConversionFactor = table.Column<decimal>(type: "numeric", nullable: false),
                    BaseQuantity = table.Column<decimal>(type: "numeric", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    TaxRate = table.Column<decimal>(type: "numeric", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    CostPriceSnapshot = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    CostAmountSnapshot = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoiceItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceItems_ProductVariants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SalesInvoiceItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceItems_SalesInvoices_SalesInvoiceId",
                        column: x => x.SalesInvoiceId,
                        principalTable: "SalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceItems_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StockMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    MovementType = table.Column<int>(type: "integer", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConversionFactor = table.Column<decimal>(type: "numeric", nullable: false),
                    QuantityChange = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReservedQuantityChange = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DamagedQuantityChange = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    QuantityAfter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AvailableAfter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReservedAfter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DamagedAfter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReferenceLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    InventoryEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    InventoryEventType = table.Column<int>(type: "integer", nullable: true),
                    ReferenceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Price = table.Column<decimal>(type: "numeric", nullable: true),
                    IsOpeningBalance = table.Column<bool>(type: "boolean", nullable: false),
                    ToWarehouseId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockMovements_ProductVariants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockMovements_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockMovements_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StockMovements_Warehouses_ToWarehouseId",
                        column: x => x.ToWarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockMovements_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION pos_reject_stock_movement_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'StockMovements records are append-only and cannot be updated or deleted.';
END;
$$;

DROP TRIGGER IF EXISTS trg_stock_movements_append_only ON ""StockMovements"";
CREATE TRIGGER trg_stock_movements_append_only
BEFORE UPDATE OR DELETE ON ""StockMovements""
FOR EACH ROW
EXECUTE FUNCTION pos_reject_stock_movement_mutation();
");

            migrationBuilder.CreateTable(
                name: "StockTransferItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StockTransferId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransferItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockTransferItems_ProductVariants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StockTransferItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StockTransferItems_StockTransfers_StockTransferId",
                        column: x => x.StockTransferId,
                        principalTable: "StockTransfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WarehouseStocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReservedQuantity = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarehouseStocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WarehouseStocks_ProductVariants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WarehouseStocks_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WarehouseStocks_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubVendorEarnings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayoutId = table.Column<Guid>(type: "uuid", nullable: true),
                    RefundId = table.Column<Guid>(type: "uuid", nullable: true),
                    OriginalEarningId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidOutAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PayoutReservedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AvailableAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidOutAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReversedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubVendorEarnings", x => x.Id);
                    table.CheckConstraint("CK_CommerceHubVendorEarnings_InPayout_HasPayout", "\"Status\" <> 3 OR \"PayoutId\" IS NOT NULL");
                    table.CheckConstraint("CK_CommerceHubVendorEarnings_PaidOut_HasPaidAmount", "\"Status\" <> 2 OR \"PaidOutAmount\" > 0 OR \"Amount\" < 0");
                    table.CheckConstraint("CK_CommerceHubVendorEarnings_PayoutAmounts_NonNegative", "\"PaidOutAmount\" >= 0 AND \"PayoutReservedAmount\" >= 0");
                    table.CheckConstraint("CK_CommerceHubVendorEarnings_PayoutAmounts_Within_Positive_Ear~", "\"Amount\" < 0 OR ROUND(\"PaidOutAmount\" + \"PayoutReservedAmount\", 2) <= ROUND(\"Amount\", 2)");
                    table.ForeignKey(
                        name: "FK_CommerceHubVendorEarnings_CommerceHubPayments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "CommerceHubPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CommerceHubVendorEarnings_CommerceHubPayouts_PayoutId",
                        column: x => x.PayoutId,
                        principalTable: "CommerceHubPayouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CommerceHubVendorEarnings_CommerceHubRefunds_RefundId",
                        column: x => x.RefundId,
                        principalTable: "CommerceHubRefunds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CommerceHubVendorEarnings_CommerceHubSubOrders_SubOrderId",
                        column: x => x.SubOrderId,
                        principalTable: "CommerceHubSubOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CommerceHubVendorEarnings_CommerceHubVendorEarnings_Origina~",
                        column: x => x.OriginalEarningId,
                        principalTable: "CommerceHubVendorEarnings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubRefundRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPriceSnapshot = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReasonCode = table.Column<int>(type: "integer", nullable: false),
                    ReasonText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PayoutMethodSnapshot = table.Column<int>(type: "integer", nullable: true),
                    PayoutAccountSnapshot = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PayoutAccountMaskedSnapshot = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PayoutAccountHolderNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReturnRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    RefundExecutionTrigger = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReviewedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessingStartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AdminNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ExecutionAttempts = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubRefundRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommerceHubRefundRequests_CommerceHubCustomers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "CommerceHubCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommerceHubRefundRequests_CommerceHubMarketplaceOrders_Orde~",
                        column: x => x.OrderId,
                        principalTable: "CommerceHubMarketplaceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommerceHubRefundRequests_CommerceHubSubOrderItems_OrderIte~",
                        column: x => x.OrderItemId,
                        principalTable: "CommerceHubSubOrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MarketingReviewEvidences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Platform = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StartDate = table.Column<DateTime>(type: "date", nullable: false),
                    EndDate = table.Column<DateTime>(type: "date", nullable: false),
                    OrdersCount = table.Column<int>(type: "integer", nullable: true),
                    RevenueAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    EvidenceType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketingReviewEvidences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketingReviewEvidences_ServiceReviews_ServiceReviewId",
                        column: x => x.ServiceReviewId,
                        principalTable: "ServiceReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AffiliateCommissionLedger",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    StoreTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttributionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AffiliateAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CommissionRate = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false),
                    CommissionAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    EntryType = table.Column<int>(type: "integer", nullable: false),
                    OriginalEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliateCommissionLedger", x => x.Id);
                    table.CheckConstraint("CK_AffiliateCommLedger_Amount", "\"CommissionAmount\" > 0");
                    table.CheckConstraint("CK_AffiliateCommLedger_BaseAmount", "\"BaseAmount\" > 0");
                    table.CheckConstraint("CK_AffiliateCommLedger_EntryType", "\"EntryType\" IN (0, 1, 2, 3)");
                    table.CheckConstraint("CK_AffiliateCommLedger_IdempotencyKey_NotBlank", "btrim(\"IdempotencyKey\") <> ''");
                    table.CheckConstraint("CK_AffiliateCommLedger_Rate", "\"CommissionRate\" > 0 AND \"CommissionRate\" <= 0.50");
                    table.CheckConstraint("CK_AffiliateCommLedger_ReversalRef", "(\"EntryType\" IN (1, 2, 3) AND \"OriginalEntryId\" IS NOT NULL) OR (\"EntryType\" = 0 AND \"OriginalEntryId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_AffiliateCommissionLedger_AffiliateAccounts_AffiliateAccoun~",
                        column: x => x.AffiliateAccountId,
                        principalTable: "AffiliateAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AffiliateCommissionLedger_AffiliateAttributions_Attribution~",
                        column: x => x.AttributionId,
                        principalTable: "AffiliateAttributions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AffiliateCommissionLedger_AffiliateCommissionLedger_Origina~",
                        column: x => x.OriginalEntryId,
                        principalTable: "AffiliateCommissionLedger",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AffiliateCommissionLedger_OrderItems_OrderItemId",
                        column: x => x.OrderItemId,
                        principalTable: "OrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AffiliateCommissionLedger_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderRefundLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderRefundId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderRefundLines", x => x.Id);
                    table.CheckConstraint("CK_OrderRefundLines_Amount_NonNegative", "\"Amount\" >= 0");
                    table.ForeignKey(
                        name: "FK_OrderRefundLines_OrderItems_OrderId_OrderItemId",
                        columns: x => new { x.OrderId, x.OrderItemId },
                        principalTable: "OrderItems",
                        principalColumns: new[] { "OrderId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderRefundLines_OrderRefunds_TenantId_OrderId_OrderRefundId",
                        columns: x => new { x.TenantId, x.OrderId, x.OrderRefundId },
                        principalTable: "OrderRefunds",
                        principalColumns: new[] { "TenantId", "OrderId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubPayoutItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PayoutId = table.Column<Guid>(type: "uuid", nullable: false),
                    EarningId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubPayoutItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommerceHubPayoutItems_CommerceHubPayouts_PayoutId",
                        column: x => x.PayoutId,
                        principalTable: "CommerceHubPayouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CommerceHubPayoutItems_CommerceHubVendorEarnings_EarningId",
                        column: x => x.EarningId,
                        principalTable: "CommerceHubVendorEarnings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubRefundEvidenceMedia",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RefundRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ExpectedSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    ThumbnailObjectKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsTemporary = table.Column<bool>(type: "boolean", nullable: false),
                    IsFlagged = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttachedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubRefundEvidenceMedia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommerceHubRefundEvidenceMedia_CommerceHubCustomers_Custome~",
                        column: x => x.CustomerId,
                        principalTable: "CommerceHubCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommerceHubRefundEvidenceMedia_CommerceHubRefundRequests_Re~",
                        column: x => x.RefundRequestId,
                        principalTable: "CommerceHubRefundRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubRefundExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RefundId = table.Column<Guid>(type: "uuid", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    AccountSnapshot = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    TransactionReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ExecutedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsSuccessful = table.Column<bool>(type: "boolean", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubRefundExecutions", x => x.Id);
                    table.CheckConstraint("CK_CommerceHubRefundExecutions_Currency_EGP", "\"Currency\" = 'EGP'");
                    table.ForeignKey(
                        name: "FK_CommerceHubRefundExecutions_CommerceHubRefundRequests_Refun~",
                        column: x => x.RefundId,
                        principalTable: "CommerceHubRefundRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommerceHubReturnRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RefundId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PickupScheduledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PickedUpAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InTransitAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TrackingNumber = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CourierName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RequestedQuantity = table.Column<int>(type: "integer", nullable: false),
                    ReceivedResellableQuantity = table.Column<int>(type: "integer", nullable: false),
                    ReceivedDamagedQuantity = table.Column<int>(type: "integer", nullable: false),
                    EscalatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EscalationLevel = table.Column<int>(type: "integer", nullable: false),
                    EscalationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ExpectedCompletionAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommerceHubReturnRequests", x => x.Id);
                    table.CheckConstraint("CK_CommerceHubReturnRequests_ReceivedDamagedQuantity_NonNegati~", "\"ReceivedDamagedQuantity\" >= 0");
                    table.CheckConstraint("CK_CommerceHubReturnRequests_ReceivedQuantity_Within_Requested", "\"ReceivedResellableQuantity\" + \"ReceivedDamagedQuantity\" <= \"RequestedQuantity\"");
                    table.CheckConstraint("CK_CommerceHubReturnRequests_ReceivedResellableQuantity_NonNeg~", "\"ReceivedResellableQuantity\" >= 0");
                    table.CheckConstraint("CK_CommerceHubReturnRequests_RequestedQuantity_Positive", "\"RequestedQuantity\" > 0");
                    table.ForeignKey(
                        name: "FK_CommerceHubReturnRequests_CommerceHubCustomers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "CommerceHubCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommerceHubReturnRequests_CommerceHubRefundRequests_RefundId",
                        column: x => x.RefundId,
                        principalTable: "CommerceHubRefundRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommerceHubReturnRequests_CommerceHubSubOrderItems_OrderIte~",
                        column: x => x.OrderItemId,
                        principalTable: "CommerceHubSubOrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerAddresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerAddresses_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerAddressVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerAddressId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    AddressAdmissionEvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    GeographicCatalogReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CountryPlaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdministrativeAreaPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocalityPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    DistrictPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ZonePlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CountryDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AdministrativeAreaDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LocalityDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DistrictDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ZoneDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Street = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Street2 = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Building = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Floor = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Apartment = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Landmark = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PostalCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    LegacyAddressText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AdmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SnapshotCreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SnapshotVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAddressVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerAddressVersions_CustomerAddresses_CustomerAddressId",
                        column: x => x.CustomerAddressId,
                        principalTable: "CustomerAddresses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "CommerceHubCategories",
                columns: new[] { "Id", "CreatedAtUtc", "IconEmoji", "IsActive", "Level", "Name", "NameAr", "ParentId", "Slug", "SortOrder", "UpdatedAtUtc" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-0000-0000-000000000001"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "📱", true, 0, "Electronics", "إلكترونيات", null, "electronics", 1, null },
                    { new Guid("10000000-0000-0000-0000-000000000002"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "👗", true, 0, "Fashion", "أزياء", null, "fashion", 2, null },
                    { new Guid("10000000-0000-0000-0000-000000000003"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "🏠", true, 0, "Home & Garden", "المنزل والحديقة", null, "home-garden", 3, null }
                });

            migrationBuilder.InsertData(
                table: "ThemePresets",
                columns: new[] { "Id", "BaseThemeJson", "CreatedAtUtc", "IsRtl", "Key", "Name", "PreviewImageUrl" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111111"), "{\"colors\":{\"primary\":\"#2563eb\",\"secondary\":\"#0f172a\",\"background\":\"#ffffff\",\"text\":\"#020617\",\"muted\":\"#64748b\"},\"typography\":{\"fontFamily\":\"Inter\",\"baseSize\":\"16px\"},\"radius\":{\"base\":\"8px\"}}", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), false, "default", "Default Modern", null },
                    { new Guid("22222222-2222-2222-2222-222222222222"), "{\"colors\":{\"primary\":\"#3b82f6\",\"secondary\":\"#1e293b\",\"background\":\"#0f172a\",\"text\":\"#f8fafc\",\"muted\":\"#94a3b8\"},\"typography\":{\"fontFamily\":\"Inter\",\"baseSize\":\"16px\"},\"radius\":{\"base\":\"12px\"}}", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), false, "dark-minimal", "Dark Minimal", null }
                });

            migrationBuilder.InsertData(
                table: "CommerceHubCategories",
                columns: new[] { "Id", "CreatedAtUtc", "IconEmoji", "IsActive", "Level", "Name", "NameAr", "ParentId", "Slug", "SortOrder", "UpdatedAtUtc" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-0000-0000-000000000011"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "📺", true, 1, "TVs", "تلفزيونات", new Guid("10000000-0000-0000-0000-000000000001"), "tvs", 1, null },
                    { new Guid("10000000-0000-0000-0000-000000000012"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "📱", true, 1, "Mobile Phones", "هواتف محمولة", new Guid("10000000-0000-0000-0000-000000000001"), "mobile-phones", 2, null },
                    { new Guid("10000000-0000-0000-0000-000000000013"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "💻", true, 1, "Laptops", "لابتوب", new Guid("10000000-0000-0000-0000-000000000001"), "laptops", 3, null },
                    { new Guid("10000000-0000-0000-0000-000000000021"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "👔", true, 1, "Men", "رجالي", new Guid("10000000-0000-0000-0000-000000000002"), "mens-fashion", 1, null },
                    { new Guid("10000000-0000-0000-0000-000000000022"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "👗", true, 1, "Women", "نسائي", new Guid("10000000-0000-0000-0000-000000000002"), "womens-fashion", 2, null },
                    { new Guid("10000000-0000-0000-0000-000000000031"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "🛋️", true, 1, "Furniture", "أثاث", new Guid("10000000-0000-0000-0000-000000000003"), "furniture", 1, null },
                    { new Guid("10000000-0000-0000-0000-000000000032"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "🍳", true, 1, "Kitchen", "مطبخ", new Guid("10000000-0000-0000-0000-000000000003"), "kitchen", 2, null }
                });

            migrationBuilder.InsertData(
                table: "CommerceHubCategoryAttributes",
                columns: new[] { "Id", "CategoryId", "IsFilterable", "IsRequired", "IsSearchable", "Name", "NameAr", "OptionsJson", "SortOrder", "Type" },
                values: new object[] { new Guid("20000000-0000-0000-0000-000000000001"), new Guid("10000000-0000-0000-0000-000000000011"), true, true, true, "Brand", "الماركة", "[\"Samsung\",\"LG\",\"Sony\",\"TCL\",\"Hisense\",\"Toshiba\",\"Panasonic\"]", 1, "select" });

            migrationBuilder.InsertData(
                table: "CommerceHubCategoryAttributes",
                columns: new[] { "Id", "CategoryId", "IsFilterable", "IsRequired", "Name", "NameAr", "OptionsJson", "SortOrder", "Type" },
                values: new object[] { new Guid("20000000-0000-0000-0000-000000000002"), new Guid("10000000-0000-0000-0000-000000000011"), true, true, "Screen Size (inches)", "حجم الشاشة (بوصة)", null, 2, "number" });

            migrationBuilder.InsertData(
                table: "CommerceHubCategoryAttributes",
                columns: new[] { "Id", "CategoryId", "IsFilterable", "IsRequired", "IsSearchable", "Name", "NameAr", "OptionsJson", "SortOrder", "Type" },
                values: new object[,]
                {
                    { new Guid("20000000-0000-0000-0000-000000000003"), new Guid("10000000-0000-0000-0000-000000000011"), true, false, true, "Resolution", "الدقة", "[\"HD\",\"Full HD\",\"4K UHD\",\"8K\"]", 3, "select" },
                    { new Guid("20000000-0000-0000-0000-000000000011"), new Guid("10000000-0000-0000-0000-000000000012"), true, true, true, "Brand", "الماركة", "[\"Apple\",\"Samsung\",\"Xiaomi\",\"Huawei\",\"Oppo\",\"Vivo\",\"Realme\",\"OnePlus\"]", 1, "select" },
                    { new Guid("20000000-0000-0000-0000-000000000012"), new Guid("10000000-0000-0000-0000-000000000012"), true, true, true, "Storage", "السعة التخزينية", "[\"32GB\",\"64GB\",\"128GB\",\"256GB\",\"512GB\",\"1TB\"]", 2, "select" }
                });

            migrationBuilder.InsertData(
                table: "CommerceHubCategoryAttributes",
                columns: new[] { "Id", "CategoryId", "IsFilterable", "IsRequired", "Name", "NameAr", "OptionsJson", "SortOrder", "Type" },
                values: new object[] { new Guid("20000000-0000-0000-0000-000000000013"), new Guid("10000000-0000-0000-0000-000000000012"), true, false, "RAM", "الذاكرة العشوائية", "[\"2GB\",\"3GB\",\"4GB\",\"6GB\",\"8GB\",\"12GB\",\"16GB\"]", 3, "select" });

            migrationBuilder.InsertData(
                table: "CommerceHubCategoryAttributes",
                columns: new[] { "Id", "CategoryId", "IsFilterable", "IsRequired", "IsSearchable", "Name", "NameAr", "OptionsJson", "SortOrder", "Type" },
                values: new object[,]
                {
                    { new Guid("20000000-0000-0000-0000-000000000021"), new Guid("10000000-0000-0000-0000-000000000013"), true, true, true, "Brand", "الماركة", "[\"Apple\",\"Dell\",\"HP\",\"Lenovo\",\"ASUS\",\"Acer\",\"MSI\"]", 1, "select" },
                    { new Guid("20000000-0000-0000-0000-000000000022"), new Guid("10000000-0000-0000-0000-000000000013"), true, false, true, "Processor", "المعالج", "[\"Intel Core i3\",\"Intel Core i5\",\"Intel Core i7\",\"Intel Core i9\",\"AMD Ryzen 5\",\"AMD Ryzen 7\",\"Apple M1\",\"Apple M2\",\"Apple M3\"]", 2, "select" }
                });

            migrationBuilder.InsertData(
                table: "CommerceHubCategoryAttributes",
                columns: new[] { "Id", "CategoryId", "IsFilterable", "IsRequired", "Name", "NameAr", "OptionsJson", "SortOrder", "Type" },
                values: new object[,]
                {
                    { new Guid("20000000-0000-0000-0000-000000000023"), new Guid("10000000-0000-0000-0000-000000000013"), true, false, "RAM", "الذاكرة العشوائية", "[\"4GB\",\"8GB\",\"16GB\",\"32GB\",\"64GB\"]", 3, "select" },
                    { new Guid("20000000-0000-0000-0000-000000000031"), new Guid("10000000-0000-0000-0000-000000000021"), true, true, "Size", "المقاس", "[\"XS\",\"S\",\"M\",\"L\",\"XL\",\"XXL\",\"3XL\"]", 1, "select" }
                });

            migrationBuilder.InsertData(
                table: "CommerceHubCategoryAttributes",
                columns: new[] { "Id", "CategoryId", "IsFilterable", "IsRequired", "IsSearchable", "Name", "NameAr", "OptionsJson", "SortOrder", "Type" },
                values: new object[] { new Guid("20000000-0000-0000-0000-000000000032"), new Guid("10000000-0000-0000-0000-000000000021"), true, false, true, "Color", "اللون", null, 2, "text" });

            migrationBuilder.CreateIndex(
                name: "IX_AbandonedCartRecoveryLogs_AbandonedCartId",
                table: "AbandonedCartRecoveryLogs",
                column: "AbandonedCartId");

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
                name: "IX_AbandonedCarts_TenantId_SessionId",
                table: "AbandonedCarts",
                columns: new[] { "TenantId", "SessionId" },
                filter: "\"SessionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AccountingOutbox_AggregateVersionBlockers",
                table: "AccountingOutboxMessages",
                columns: new[] { "TenantId", "BusinessEntityId", "AggregateVersion", "OccurredAtUtc", "CreatedAt", "Id", "ProcessedAtUtc", "IsFailed", "EventType" },
                filter: "\"ProcessedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AccountingOutbox_AggregateVersionOrdering",
                table: "AccountingOutboxMessages",
                columns: new[] { "TenantId", "BusinessEntityId", "AggregateVersion", "OccurredAtUtc", "CreatedAt", "Id" },
                filter: "\"ProcessedAtUtc\" IS NULL AND \"IsFailed\" = false");

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
                columns: new[] { "ProcessedAtUtc", "IsFailed", "NextRetryAtUtc", "ClaimedUntilUtc" },
                filter: "\"ProcessedAtUtc\" IS NULL AND \"IsFailed\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_AccountingOutbox_Tenant_SourceType_Entity",
                table: "AccountingOutboxMessages",
                columns: new[] { "TenantId", "SourceType", "BusinessEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountingOutbox_TenantId_IdempotencyKey",
                table: "AccountingOutboxMessages",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM ""AccountLedgers""
        WHERE ""IsDeleted"" = false
          AND ""PartyType"" = 0
          AND ""TransactionType"" = 11
        GROUP BY ""TenantId"", ""PartyType"", ""PartyId"", ""TransactionType""
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'duplicate active customer opening-balance ledger rows exist';
    END IF;
END
$$;
");

            migrationBuilder.CreateIndex(
                name: "IX_AccountLedgers_CustomerOpeningBalance_Once",
                table: "AccountLedgers",
                columns: new[] { "TenantId", "PartyType", "PartyId", "TransactionType" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"PartyType\" = 0 AND \"TransactionType\" = 11");

            migrationBuilder.CreateIndex(
                name: "IX_AccountLedgers_UserId",
                table: "AccountLedgers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AddressAdmissionEvidence_AdministrativeAreaPlaceId",
                table: "AddressAdmissionEvidence",
                column: "AdministrativeAreaPlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_AddressAdmissionEvidence_CountryPlaceId",
                table: "AddressAdmissionEvidence",
                column: "CountryPlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_AddressAdmissionEvidence_DistrictPlaceId",
                table: "AddressAdmissionEvidence",
                column: "DistrictPlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_AddressAdmissionEvidence_Lineage",
                table: "AddressAdmissionEvidence",
                columns: new[] { "CatalogReleaseId", "CountryPlaceId", "AdministrativeAreaPlaceId", "LocalityPlaceId", "DistrictPlaceId", "ZonePlaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_AddressAdmissionEvidence_LocalityPlaceId",
                table: "AddressAdmissionEvidence",
                column: "LocalityPlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_AddressAdmissionEvidence_Owner_Fingerprint",
                table: "AddressAdmissionEvidence",
                columns: new[] { "TenantId", "OwnerDomain", "OwnerId", "AddressFingerprintSha256" });

            migrationBuilder.CreateIndex(
                name: "IX_AddressAdmissionEvidence_ZonePlaceId",
                table: "AddressAdmissionEvidence",
                column: "ZonePlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_AddressNormalizationRules_Active",
                table: "AddressNormalizationRules",
                columns: new[] { "CountryPlaceId", "LanguageTag", "RuleKind", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AddressNormalizationRules_IntroducedCatalogReleaseId",
                table: "AddressNormalizationRules",
                column: "IntroducedCatalogReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_AddressNormalizationRules_RetiredCatalogReleaseId",
                table: "AddressNormalizationRules",
                column: "RetiredCatalogReleaseId");

            migrationBuilder.CreateIndex(
                name: "UQ_AddressNormalizationRule_Version",
                table: "AddressNormalizationRules",
                columns: new[] { "CountryPlaceId", "LanguageTag", "RuleKind", "RuleVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateAccounts_TenantId",
                table: "AffiliateAccounts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateAccounts_ReferralCode_Global",
                table: "AffiliateAccounts",
                column: "ReferralCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateAccounts_Tenant_ReferralCode",
                table: "AffiliateAccounts",
                columns: new[] { "TenantId", "ReferralCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateAccounts_Tenant_User",
                table: "AffiliateAccounts",
                columns: new[] { "TenantId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateAttributions_AffiliateAccountId",
                table: "AffiliateAttributions",
                column: "AffiliateAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateAttributions_OrderId",
                table: "AffiliateAttributions",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateAttributions_PromotionLinkId",
                table: "AffiliateAttributions",
                column: "PromotionLinkId",
                filter: "\"PromotionLinkId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateAttributions_Tenant_Order_Active",
                table: "AffiliateAttributions",
                columns: new[] { "TenantId", "OrderId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateAttributions_TenantId",
                table: "AffiliateAttributions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateAttributions_Tenant_Order",
                table: "AffiliateAttributions",
                columns: new[] { "TenantId", "OrderId" },
                unique: true);

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
                name: "IX_AffiliateCategories_DisplayOrder",
                table: "AffiliateCategories",
                column: "DisplayOrder");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateCategories_IsActive",
                table: "AffiliateCategories",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateCategories_ParentId",
                table: "AffiliateCategories",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateCategories_Parent_Name",
                table: "AffiliateCategories",
                columns: new[] { "ParentId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateCategories_Slug",
                table: "AffiliateCategories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateCommissionLedger_AttributionId",
                table: "AffiliateCommissionLedger",
                column: "AttributionId");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateCommissionLedger_OriginalEntryId",
                table: "AffiliateCommissionLedger",
                column: "OriginalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateCommLedger_AffiliateAccountId",
                table: "AffiliateCommissionLedger",
                column: "AffiliateAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateCommLedger_OrderId",
                table: "AffiliateCommissionLedger",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateCommLedger_OrderItemEntryType",
                table: "AffiliateCommissionLedger",
                columns: new[] { "OrderItemId", "EntryType" });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateCommLedger_TenantId",
                table: "AffiliateCommissionLedger",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateCommLedger_TenantRecorded",
                table: "AffiliateCommissionLedger",
                columns: new[] { "TenantId", "RecordedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateCommLedger_Idempotency",
                table: "AffiliateCommissionLedger",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateEmailVerificationTokens_ExpiresAtUtc",
                table: "AffiliateEmailVerificationTokens",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateEmailVerificationTokens_TokenHash",
                table: "AffiliateEmailVerificationTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateEmailVerificationTokens_User_Consumed",
                table: "AffiliateEmailVerificationTokens",
                columns: new[] { "AffiliateUserId", "ConsumedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliatePasswordResetTokens_ExpiresAtUtc",
                table: "AffiliatePasswordResetTokens",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliatePasswordResetTokens_TokenHash",
                table: "AffiliatePasswordResetTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AffiliatePasswordResetTokens_User_Consumed",
                table: "AffiliatePasswordResetTokens",
                columns: new[] { "AffiliateUserId", "ConsumedAtUtc" });

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

            migrationBuilder.CreateIndex(
                name: "IX_AffiliatePayoutTransferAllocation_EvidenceId",
                table: "AffiliatePayoutTransferAllocations",
                column: "EvidenceId");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliatePayoutTransferAllocation_Tenant_CreatedAt",
                table: "AffiliatePayoutTransferAllocations",
                columns: new[] { "StoreTenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliatePayoutTransferAllocation_Idempotency",
                table: "AffiliatePayoutTransferAllocations",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AffiliatePayoutTransferEvidence_Tenant_CreatedAt",
                table: "AffiliatePayoutTransferEvidence",
                columns: new[] { "StoreTenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliatePayoutTransferEvidence_Rail_Reference",
                table: "AffiliatePayoutTransferEvidence",
                columns: new[] { "RailNamespace", "NormalizedPaymentReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateProductPolicies_AffiliateCategoryId",
                table: "AffiliateProductPolicies",
                column: "AffiliateCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateProductPolicies_ProductId",
                table: "AffiliateProductPolicies",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateProductPolicies_TenantId",
                table: "AffiliateProductPolicies",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateProductPolicies_Tenant_Product",
                table: "AffiliateProductPolicies",
                columns: new[] { "TenantId", "ProductId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliatePromotionLinks_AffiliateUserId",
                table: "AffiliatePromotionLinks",
                column: "AffiliateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliatePromotionLinks_Code_Store",
                table: "AffiliatePromotionLinks",
                columns: new[] { "ReferralCode", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliatePromotionLinks_ProductId",
                table: "AffiliatePromotionLinks",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliatePromotionLinks_TenantId",
                table: "AffiliatePromotionLinks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliatePromotionLinks_ReferralCode",
                table: "AffiliatePromotionLinks",
                column: "ReferralCode",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliatePromotionLinks_User_Store_Product",
                table: "AffiliatePromotionLinks",
                columns: new[] { "AffiliateUserId", "TenantId", "ProductId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateRefreshTokenReplayEntries_ReplayedAtUtc",
                table: "AffiliateRefreshTokenReplayEntries",
                column: "ReplayedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateRefreshTokenReplayEntries_TokenHash",
                table: "AffiliateRefreshTokenReplayEntries",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateRefreshTokenReplayEntries_User_Expires",
                table: "AffiliateRefreshTokenReplayEntries",
                columns: new[] { "AffiliateUserId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateUsers_TenantId",
                table: "AffiliateUsers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateUsers_AffiliateAccount",
                table: "AffiliateUsers",
                column: "AffiliateAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateUsers_Email_Tenant",
                table: "AffiliateUsers",
                columns: new[] { "TenantId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsAlertRules_TenantId",
                table: "AnalyticsAlertRules",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsAlertRules_TenantId_IsEnabled_IsDeleted",
                table: "AnalyticsAlertRules",
                columns: new[] { "TenantId", "IsEnabled", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ActionType",
                table: "AuditLogs",
                column: "ActionType");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType",
                table: "AuditLogs",
                column: "EntityType");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TimestampUtc",
                table: "AuditLogs",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Banks_TenantId_BankName",
                table: "Banks",
                columns: new[] { "TenantId", "BankName" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_BankId",
                table: "BankTransactions",
                column: "BankId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignCosts_TenantId",
                table: "CampaignCosts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignCosts_TenantId_PeriodStartUtc_PeriodEndUtc",
                table: "CampaignCosts",
                columns: new[] { "TenantId", "PeriodStartUtc", "PeriodEndUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignCosts_TenantId_Source_Medium_Campaign_PeriodStartUtc",
                table: "CampaignCosts",
                columns: new[] { "TenantId", "Source", "Medium", "Campaign", "PeriodStartUtc" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalIdentityLinks_CanonicalIdentityId",
                table: "CanonicalIdentityLinks",
                column: "CanonicalIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalIdentityLinks_Source_DisplayKey",
                table: "CanonicalIdentityLinks",
                columns: new[] { "SourceSystem", "DisplayKey" });

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalIdentityLinks_Tenant_Subject",
                table: "CanonicalIdentityLinks",
                columns: new[] { "TenantId", "SubjectType", "SubjectId" },
                unique: true,
                filter: "\"TenantId\" IS NOT NULL AND \"IsDeleted\" = false AND \"RevokedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_CanonicalIdentityLinks_Global_Subject_Active",
                table: "CanonicalIdentityLinks",
                columns: new[] { "SubjectType", "SubjectId" },
                unique: true,
                filter: "\"TenantId\" IS NULL AND \"IsDeleted\" = false AND \"RevokedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierAccountPolicies_TenantId_CarrierAccountId_Scope",
                table: "CarrierAccountPolicies",
                columns: new[] { "TenantId", "CarrierAccountId", "Scope" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierAccounts_TenantId_CarrierCode",
                table: "CarrierAccounts",
                columns: new[] { "TenantId", "CarrierCode" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierAdministrationAudit_TenantId_CarrierAccountId_Occurr~",
                table: "CarrierAdministrationAudit",
                columns: new[] { "TenantId", "CarrierAccountId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CarrierConfigurations_TenantId_CarrierCode",
                table: "CarrierConfigurations",
                columns: new[] { "TenantId", "CarrierCode" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierConfigurations_TenantId_IsDefault",
                table: "CarrierConfigurations",
                columns: new[] { "TenantId", "IsDefault" },
                filter: "\"IsDefault\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierConfigurations_TenantId_IsEnabled_SortOrder",
                table: "CarrierConfigurations",
                columns: new[] { "TenantId", "IsEnabled", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CarrierConnectionTestRecords_TenantId_CarrierAccountId_Stat~",
                table: "CarrierConnectionTestRecords",
                columns: new[] { "TenantId", "CarrierAccountId", "Status", "TestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CarrierCredentialReferences_TenantId_CarrierAccountId_State",
                table: "CarrierCredentialReferences",
                columns: new[] { "TenantId", "CarrierAccountId", "State" });

            migrationBuilder.CreateIndex(
                name: "UQ_CarrierGeoGroup_Key",
                table: "CarrierGeographicMappingGroups",
                columns: new[] { "MappingReleaseId", "GroupKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CarrierGeoPublications_Release_Published",
                table: "CarrierGeographicMappingPublications",
                columns: new[] { "MappingReleaseId", "PublishedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CarrierGeographicMappingReleases_CatalogReleaseId",
                table: "CarrierGeographicMappingReleases",
                column: "CatalogReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierGeographicMappingReleases_RollbackOfReleaseId",
                table: "CarrierGeographicMappingReleases",
                column: "RollbackOfReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierGeographicMappingReleases_SupersedesReleaseId",
                table: "CarrierGeographicMappingReleases",
                column: "SupersedesReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierGeoRelease_ActiveScope",
                table: "CarrierGeographicMappingReleases",
                columns: new[] { "CarrierProviderId", "Environment", "AccountScopeKind", "AccountScopeId", "Status", "EffectiveFromUtc" });

            migrationBuilder.CreateIndex(
                name: "UQ_CarrierGeoRelease_Key",
                table: "CarrierGeographicMappingReleases",
                column: "ReleaseKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_CarrierGeoRelease_Number",
                table: "CarrierGeographicMappingReleases",
                columns: new[] { "CarrierProviderId", "Environment", "AccountScopeKind", "AccountScopeId", "ReleaseNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CarrierGeographicMappings_CanonicalPlaceId",
                table: "CarrierGeographicMappings",
                column: "CanonicalPlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierGeographicMappings_CarrierProviderId",
                table: "CarrierGeographicMappings",
                column: "CarrierProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierGeographicMappings_CountryPlaceId",
                table: "CarrierGeographicMappings",
                column: "CountryPlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierGeographicMappings_MappingGroupId",
                table: "CarrierGeographicMappings",
                column: "MappingGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierGeographicMappings_ProviderParentMappingId",
                table: "CarrierGeographicMappings",
                column: "ProviderParentMappingId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierGeoMapping_Resolution",
                table: "CarrierGeographicMappings",
                columns: new[] { "MappingReleaseId", "CanonicalPlaceId", "ProviderPlaceType", "MappingStatus" });

            migrationBuilder.CreateIndex(
                name: "UQ_CarrierGeoMapping_CanonicalProvider",
                table: "CarrierGeographicMappings",
                columns: new[] { "MappingReleaseId", "CanonicalPlaceId", "ProviderPlaceType", "ProviderPlaceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_CarrierGeoMapping_ProviderId",
                table: "CarrierGeographicMappings",
                columns: new[] { "MappingReleaseId", "ProviderPlaceType", "ProviderPlaceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CarrierGeographicOverrides_CanonicalPlaceId",
                table: "CarrierGeographicOverrides",
                column: "CanonicalPlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierGeoOverrides_Scope_Place_Status",
                table: "CarrierGeographicOverrides",
                columns: new[] { "CarrierProviderId", "Environment", "AccountScopeKind", "AccountScopeId", "TenantId", "CanonicalPlaceId", "ProviderPlaceType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CarrierGeographicSyncCandidates_CandidateCanonicalPlaceId",
                table: "CarrierGeographicSyncCandidates",
                column: "CandidateCanonicalPlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierGeoSyncCandidates_Review",
                table: "CarrierGeographicSyncCandidates",
                columns: new[] { "CarrierProviderId", "CandidateStatus", "Confidence" });

            migrationBuilder.CreateIndex(
                name: "UQ_CarrierGeoSyncCandidate_Node",
                table: "CarrierGeographicSyncCandidates",
                columns: new[] { "SyncRunId", "ProviderPlaceType", "ProviderPlaceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CarrierGeoSyncRuns_Scope_Status",
                table: "CarrierGeographicSyncRuns",
                columns: new[] { "CarrierProviderId", "Environment", "AccountScopeKind", "AccountScopeId", "StartedAtUtc", "Status" });

            migrationBuilder.CreateIndex(
                name: "UQ_CarrierProvider_Code",
                table: "CarrierProviders",
                column: "CarrierCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CarrierServiceLevelEnablements_TenantId_CarrierAccountId_Se~",
                table: "CarrierServiceLevelEnablements",
                columns: new[] { "TenantId", "CarrierAccountId", "ServiceLevelCode" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierWarehouseAssignments_TenantId_CarrierAccountId_Wareh~",
                table: "CarrierWarehouseAssignments",
                columns: new[] { "TenantId", "CarrierAccountId", "WarehouseId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Cashboxes_IsActive",
                table: "Cashboxes",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Cashboxes_OpenedByUserId",
                table: "Cashboxes",
                column: "OpenedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Cashboxes_WarehouseId",
                table: "Cashboxes",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_Cashboxes_WarehouseId_IsActive",
                table: "Cashboxes",
                columns: new[] { "WarehouseId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_CashboxTransactions_CashboxId",
                table: "CashboxTransactions",
                column: "CashboxId");

            migrationBuilder.CreateIndex(
                name: "IX_CashboxTransactions_CashboxId_TransactionDate",
                table: "CashboxTransactions",
                columns: new[] { "CashboxId", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CashboxTransactions_ReferenceId",
                table: "CashboxTransactions",
                column: "ReferenceId");

            migrationBuilder.CreateIndex(
                name: "IX_CashboxTransactions_TransactionDate",
                table: "CashboxTransactions",
                column: "TransactionDate");

            migrationBuilder.CreateIndex(
                name: "IX_CashboxTransactions_TransactionType",
                table: "CashboxTransactions",
                column: "TransactionType");

            migrationBuilder.CreateIndex(
                name: "IX_CashboxTransactions_UserId",
                table: "CashboxTransactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_ParentCategoryId",
                table: "Categories",
                column: "ParentCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_CartId",
                table: "CheckoutSessions",
                column: "CartId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_RecoveryToken",
                table: "CheckoutSessions",
                column: "RecoveryToken",
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_TenantId_CartId_Active_Unique",
                table: "CheckoutSessions",
                columns: new[] { "TenantId", "CartId", "Status" },
                unique: true,
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_TenantId_Status_AbandonedAtUtc",
                table: "CheckoutSessions",
                columns: new[] { "TenantId", "Status", "AbandonedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CmsPages_PageGroupId",
                table: "CmsPages",
                column: "PageGroupId",
                unique: true,
                filter: "\"IsPublished\" AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_CmsPages_TenantId_IsDraft_Slug",
                table: "CmsPages",
                columns: new[] { "TenantId", "IsDraft", "Slug" });

            migrationBuilder.CreateIndex(
                name: "IX_CmsPages_TenantId_IsPublished_Slug",
                table: "CmsPages",
                columns: new[] { "TenantId", "IsPublished", "Slug" });

            migrationBuilder.CreateIndex(
                name: "UQ_CmsPages_PageGroupId_Version",
                table: "CmsPages",
                columns: new[] { "PageGroupId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_CmsPages_Tenant_Published_Slug",
                table: "CmsPages",
                columns: new[] { "TenantId", "Slug" },
                unique: true,
                filter: "\"IsPublished\" AND NOT \"IsDeleted\"");

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
                name: "UX_CommerceHubCarts_TenantId_CommerceHubCustomerId_Active",
                table: "CommerceHubCarts",
                columns: new[] { "TenantId", "CommerceHubCustomerId" },
                unique: true,
                filter: "\"CommerceHubCustomerId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubCategories_ParentId_SortOrder",
                table: "CommerceHubCategories",
                columns: new[] { "ParentId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "UQ_CommerceHubCategories_Slug",
                table: "CommerceHubCategories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubCategoryAttributes_CategoryId_SortOrder",
                table: "CommerceHubCategoryAttributes",
                columns: new[] { "CategoryId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubCheckoutIdempotency_CommerceHubCustomerId",
                table: "CommerceHubCheckoutIdempotency",
                column: "CommerceHubCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubCheckoutIdempotency_Key",
                table: "CommerceHubCheckoutIdempotency",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubCheckoutIdempotency_TenantId_CommerceHubCustomerId",
                table: "CommerceHubCheckoutIdempotency",
                columns: new[] { "TenantId", "CommerceHubCustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubCheckoutIdempotency_TenantId_RequestHash",
                table: "CommerceHubCheckoutIdempotency",
                columns: new[] { "TenantId", "RequestHash" });

            migrationBuilder.CreateIndex(
                name: "IX_CHCustomerEmailVerificationTokens_CustomerId",
                table: "CommerceHubCustomerEmailVerificationTokens",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CHCustomerEmailVerificationTokens_TokenHash",
                table: "CommerceHubCustomerEmailVerificationTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CHCustomerIdentityEvents_Customer_Occurred",
                table: "CommerceHubCustomerIdentityEvents",
                columns: new[] { "CustomerId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CHCustomerIdentityEvents_EventType_Occurred",
                table: "CommerceHubCustomerIdentityEvents",
                columns: new[] { "EventType", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CHCustomerPasswordResetTokens_CustomerId",
                table: "CommerceHubCustomerPasswordResetTokens",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CHCustomerPasswordResetTokens_TokenHash",
                table: "CommerceHubCustomerPasswordResetTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubCustomerPayoutDetails_CustomerId_CreatedAt",
                table: "CommerceHubCustomerPayoutDetails",
                columns: new[] { "CustomerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubCustomerPayoutDetails_CustomerId_Method_CreatedAt",
                table: "CommerceHubCustomerPayoutDetails",
                columns: new[] { "CustomerId", "Method", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubCustomers_CreatedAt",
                table: "CommerceHubCustomers",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubCustomers_Email",
                table: "CommerceHubCustomers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubDailyFinancialAggregates_CalculatedAtUtc",
                table: "CommerceHubDailyFinancialAggregates",
                column: "CalculatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubDailyFinancialAggregates_CurrencyCode_DateUtc",
                table: "CommerceHubDailyFinancialAggregates",
                columns: new[] { "CurrencyCode", "DateUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubFinancialAuditLogs_CreatedAtUtc",
                table: "CommerceHubFinancialAuditLogs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubFinancialAuditLogs_EarningId",
                table: "CommerceHubFinancialAuditLogs",
                column: "EarningId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubFinancialAuditLogs_MarketplaceOrderId",
                table: "CommerceHubFinancialAuditLogs",
                column: "MarketplaceOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubFinancialAuditLogs_PaymentId",
                table: "CommerceHubFinancialAuditLogs",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubFinancialAuditLogs_PayoutId",
                table: "CommerceHubFinancialAuditLogs",
                column: "PayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubFinancialAuditLogs_RefundId",
                table: "CommerceHubFinancialAuditLogs",
                column: "RefundId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubFinancialAuditLogs_SubOrderId",
                table: "CommerceHubFinancialAuditLogs",
                column: "SubOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubFinancialAuditLogs_TenantId",
                table: "CommerceHubFinancialAuditLogs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubFinancialAuditLogs_Type",
                table: "CommerceHubFinancialAuditLogs",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubFinancialGuardLocks_Scope",
                table: "CommerceHubFinancialGuardLocks",
                column: "Scope",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubFinancialOperationAudits_CorrelationId",
                table: "CommerceHubFinancialOperationAudits",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubFinancialOperationAudits_OperationType",
                table: "CommerceHubFinancialOperationAudits",
                column: "OperationType");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubFinancialOperationAudits_ReferenceType_Reference~",
                table: "CommerceHubFinancialOperationAudits",
                columns: new[] { "ReferenceType", "ReferenceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubFinancialOperationAudits_VendorId",
                table: "CommerceHubFinancialOperationAudits",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubFinancialReconciliationReports_Status_TimestampUtc",
                table: "CommerceHubFinancialReconciliationReports",
                columns: new[] { "Status", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubFinancialReconciliationReports_TimestampUtc",
                table: "CommerceHubFinancialReconciliationReports",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CHFinancialTx_EntityType_Bucket_CreatedAtUtc",
                table: "CommerceHubFinancialTransactions",
                columns: new[] { "EntityType", "FinancialBucket", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CHFinTx_EntityType_EntityId_CreatedAtUtc",
                table: "CommerceHubFinancialTransactions",
                columns: new[] { "EntityType", "EntityId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CHFinTx_EntityType_EntityId_CreatedBy_CreatedAtUtc",
                table: "CommerceHubFinancialTransactions",
                columns: new[] { "EntityType", "EntityId", "CreatedBy", "CreatedAtUtc" },
                filter: "\"EntityType\" = 1 AND \"EntityId\" IS NOT NULL AND \"CreatedBy\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubFinancialTransactions_CorrelationId",
                table: "CommerceHubFinancialTransactions",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubFinancialTransactions_CreatedAtUtc",
                table: "CommerceHubFinancialTransactions",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubFinancialTransactions_EntityId_CreatedAtUtc_Vendor",
                table: "CommerceHubFinancialTransactions",
                columns: new[] { "EntityId", "CreatedAtUtc" },
                filter: "\"EntityType\" = 1 AND \"EntityId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubFinancialTransactions_EntityId_EntityType",
                table: "CommerceHubFinancialTransactions",
                columns: new[] { "EntityId", "EntityType" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubFinancialTransactions_EntityType_EntityId_TransactionType_CreatedAtUtc",
                table: "CommerceHubFinancialTransactions",
                columns: new[] { "EntityType", "EntityId", "TransactionType", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubFinancialTransactions_ReferenceId",
                table: "CommerceHubFinancialTransactions",
                column: "ReferenceId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubFinancialTransactions_ReferenceType_ReferenceId",
                table: "CommerceHubFinancialTransactions",
                columns: new[] { "ReferenceType", "ReferenceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubFinancialTransactions_ReferenceType_TransactionType_Direction_CreatedAtUtc",
                table: "CommerceHubFinancialTransactions",
                columns: new[] { "ReferenceType", "TransactionType", "Direction", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubFinancialTransactions_TransactionType",
                table: "CommerceHubFinancialTransactions",
                column: "TransactionType");

            migrationBuilder.CreateIndex(
                name: "UX_CHFinancialTx_Fingerprint_Platform",
                table: "CommerceHubFinancialTransactions",
                columns: new[] { "ReferenceType", "ReferenceId", "TransactionType", "Direction", "FinancialBucket", "Amount", "Currency" },
                unique: true,
                filter: "\"EntityId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_CHFinancialTx_Fingerprint_Vendor",
                table: "CommerceHubFinancialTransactions",
                columns: new[] { "ReferenceType", "ReferenceId", "TransactionType", "EntityId", "Direction", "FinancialBucket", "Amount", "Currency" },
                unique: true,
                filter: "\"EntityId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_CommerceHubFinancialTransactions_IdempotencyKey",
                table: "CommerceHubFinancialTransactions",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubListings_ProductId",
                table: "CommerceHubListings",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubListings_TenantId_IsPublished_CategoryId",
                table: "CommerceHubListings",
                columns: new[] { "TenantId", "IsPublished", "CommerceHubCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubListings_VariantId",
                table: "CommerceHubListings",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "UQ_CommerceHubListings_TenantId_VariantId",
                table: "CommerceHubListings",
                columns: new[] { "TenantId", "VariantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubMarketplaceOrders_CreatedAt",
                table: "CommerceHubMarketplaceOrders",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubMarketplaceOrders_CustomerId",
                table: "CommerceHubMarketplaceOrders",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubMarketplaceOrders_IdempotencyKey",
                table: "CommerceHubMarketplaceOrders",
                column: "IdempotencyKey",
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubMarketplaceOrders_OrderNumber",
                table: "CommerceHubMarketplaceOrders",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubMarketplaceOrders_PaymentStatus_Status_CreatedAt",
                table: "CommerceHubMarketplaceOrders",
                columns: new[] { "PaymentStatus", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CHMerchantLifecycle_Actor_Time",
                table: "CommerceHubMerchantLifecycleLedgerEntries",
                columns: new[] { "ActorUserId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CHMerchantLifecycle_Correlation",
                table: "CommerceHubMerchantLifecycleLedgerEntries",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_CHMerchantLifecycle_Request",
                table: "CommerceHubMerchantLifecycleLedgerEntries",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_CHMerchantLifecycle_Tenant_Listing_Time",
                table: "CommerceHubMerchantLifecycleLedgerEntries",
                columns: new[] { "TenantId", "ListingId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubPaymentAttempts_PaymentId",
                table: "CommerceHubPaymentAttempts",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubPaymentAttempts_PaymentId_AttemptNumber",
                table: "CommerceHubPaymentAttempts",
                columns: new[] { "PaymentId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubPaymentAttempts_Provider_ProviderIntentId",
                table: "CommerceHubPaymentAttempts",
                columns: new[] { "Provider", "ProviderIntentId" },
                filter: "\"ProviderIntentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubPaymentAttempts_ProviderTransactionId",
                table: "CommerceHubPaymentAttempts",
                column: "ProviderTransactionId",
                filter: "\"ProviderTransactionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CHPaymentCompOutbox_OperatorReconciliation",
                table: "CommerceHubPaymentCompensationOutboxMessages",
                columns: new[] { "RequiresOperatorReconciliation", "ProcessedAtUtc", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CHPaymentCompOutbox_PaymentId",
                table: "CommerceHubPaymentCompensationOutboxMessages",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_CHPaymentCompOutbox_PaymentOrdering",
                table: "CommerceHubPaymentCompensationOutboxMessages",
                columns: new[] { "PaymentId", "ProcessedAtUtc", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CHPaymentCompOutbox_Processing",
                table: "CommerceHubPaymentCompensationOutboxMessages",
                columns: new[] { "IsFailed", "ProcessedAtUtc", "NextRetryAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CHPaymentCompOutbox_ProcessingLease",
                table: "CommerceHubPaymentCompensationOutboxMessages",
                columns: new[] { "ProcessedAtUtc", "IsFailed", "NextRetryAtUtc", "ProcessingLeaseExpiresAtUtc", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_CHPaymentCompOutbox_IdempotencyKey",
                table: "CommerceHubPaymentCompensationOutboxMessages",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubPaymentItems_PaymentId",
                table: "CommerceHubPaymentItems",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubPaymentItems_PaymentId_SubOrderId",
                table: "CommerceHubPaymentItems",
                columns: new[] { "PaymentId", "SubOrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubPaymentItems_SubOrderId",
                table: "CommerceHubPaymentItems",
                column: "SubOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubPaymentLogs_PaymentId",
                table: "CommerceHubPaymentLogs",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubPaymentLogs_PaymentId_ProviderEventId",
                table: "CommerceHubPaymentLogs",
                columns: new[] { "PaymentId", "ProviderEventId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubPayments_OrderId_SinglePaid",
                table: "CommerceHubPayments",
                column: "OrderId",
                unique: true,
                filter: "\"Status\" = 1 AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubPayments_OrderId_Status",
                table: "CommerceHubPayments",
                columns: new[] { "OrderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubPayments_ProviderReference",
                table: "CommerceHubPayments",
                column: "ProviderReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubPayments_ProviderTransactionId",
                table: "CommerceHubPayments",
                column: "ProviderTransactionId",
                unique: true,
                filter: "\"ProviderTransactionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_CommerceHubPayments_Provider_ProviderIntentId",
                table: "CommerceHubPayments",
                columns: new[] { "Provider", "ProviderIntentId" },
                unique: true,
                filter: "\"ProviderIntentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubPayoutItems_EarningId",
                table: "CommerceHubPayoutItems",
                column: "EarningId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubPayoutItems_PayoutId",
                table: "CommerceHubPayoutItems",
                column: "PayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubPayoutItems_PayoutId_EarningId",
                table: "CommerceHubPayoutItems",
                columns: new[] { "PayoutId", "EarningId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubPayouts_PayoutMethodId",
                table: "CommerceHubPayouts",
                column: "PayoutMethodId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubPayouts_TenantId_IdempotencyKey",
                table: "CommerceHubPayouts",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubPayouts_TenantId_Status_CreatedAt",
                table: "CommerceHubPayouts",
                columns: new[] { "TenantId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_CommerceHubPayouts_ExternalTransferHandoffToken",
                table: "CommerceHubPayouts",
                columns: new[] { "TenantId", "ExternalTransferHandoffToken" },
                unique: true,
                filter: "\"ExternalTransferHandoffToken\" IS NOT NULL AND \"ExternalTransferHandoffToken\" <> '' AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UX_CommerceHubPayouts_ExternalTransferIdempotencyKey",
                table: "CommerceHubPayouts",
                columns: new[] { "TenantId", "ExternalTransferIdempotencyKey" },
                unique: true,
                filter: "\"ExternalTransferIdempotencyKey\" IS NOT NULL AND \"ExternalTransferIdempotencyKey\" <> '' AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UX_CommerceHubPayouts_Live_BankTransactionId",
                table: "CommerceHubPayouts",
                column: "BankTransactionId",
                unique: true,
                filter: "\"BankTransactionId\" IS NOT NULL AND \"BankTransactionId\" <> '' AND \"Status\" <> 3 AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UX_CommerceHubPayouts_Live_ExternalReferenceId",
                table: "CommerceHubPayouts",
                column: "ExternalReferenceId",
                unique: true,
                filter: "\"ExternalReferenceId\" IS NOT NULL AND \"ExternalReferenceId\" <> '' AND \"Status\" <> 3 AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UX_CommerceHubPayouts_OneProcessingPerTenant",
                table: "CommerceHubPayouts",
                column: "TenantId",
                unique: true,
                filter: "(\"Status\" = 1 OR \"Status\" = 4) AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_CHRecovery_Actor_Time",
                table: "CommerceHubRecoveryOperationLedgerEntries",
                columns: new[] { "ActorUserId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CHRecovery_Correlation",
                table: "CommerceHubRecoveryOperationLedgerEntries",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_CHRecovery_Operation_Time",
                table: "CommerceHubRecoveryOperationLedgerEntries",
                columns: new[] { "OperationId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CHRecovery_Request",
                table: "CommerceHubRecoveryOperationLedgerEntries",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_CHRecovery_Tenant_Listing_Time",
                table: "CommerceHubRecoveryOperationLedgerEntries",
                columns: new[] { "TenantId", "ListingId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubRefundEvidenceMedia_CustomerId_ContentHash",
                table: "CommerceHubRefundEvidenceMedia",
                columns: new[] { "CustomerId", "ContentHash" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubRefundEvidenceMedia_CustomerId_CreatedAt",
                table: "CommerceHubRefundEvidenceMedia",
                columns: new[] { "CustomerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubRefundEvidenceMedia_IsTemporary_ExpiresAtUtc",
                table: "CommerceHubRefundEvidenceMedia",
                columns: new[] { "IsTemporary", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubRefundEvidenceMedia_ObjectKey",
                table: "CommerceHubRefundEvidenceMedia",
                column: "ObjectKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubRefundEvidenceMedia_RefundRequestId",
                table: "CommerceHubRefundEvidenceMedia",
                column: "RefundRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubRefundExecutions_ExecutedAt",
                table: "CommerceHubRefundExecutions",
                column: "ExecutedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubRefundExecutions_RefundId",
                table: "CommerceHubRefundExecutions",
                column: "RefundId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubRefundRequests_CustomerId_CreatedAt",
                table: "CommerceHubRefundRequests",
                columns: new[] { "CustomerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubRefundRequests_CustomerId_Status_CreatedAt",
                table: "CommerceHubRefundRequests",
                columns: new[] { "CustomerId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubRefundRequests_IdempotencyKey",
                table: "CommerceHubRefundRequests",
                column: "IdempotencyKey",
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubRefundRequests_OrderId",
                table: "CommerceHubRefundRequests",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubRefundRequests_OrderItemId_Status_CreatedAt",
                table: "CommerceHubRefundRequests",
                columns: new[] { "OrderItemId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubRefundRequests_ReturnRequestId",
                table: "CommerceHubRefundRequests",
                column: "ReturnRequestId",
                unique: true,
                filter: "\"ReturnRequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubRefundRequests_Status_CreatedAt",
                table: "CommerceHubRefundRequests",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubRefundRequests_VendorId_Status_CreatedAt",
                table: "CommerceHubRefundRequests",
                columns: new[] { "VendorId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubRefunds_ExternalReconciliation",
                table: "CommerceHubRefunds",
                columns: new[] { "TenantId", "Status", "ProcessingStartedAtUtc", "CreatedAt" },
                filter: "(\"Status\" = 1 OR \"Status\" = 4) AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubRefunds_OrderItemId",
                table: "CommerceHubRefunds",
                column: "OrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubRefunds_PaymentId",
                table: "CommerceHubRefunds",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubRefunds_RefundRequestId",
                table: "CommerceHubRefunds",
                column: "RefundRequestId",
                unique: true,
                filter: "\"RefundRequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubRefunds_SubOrderId_Status_CreatedAt",
                table: "CommerceHubRefunds",
                columns: new[] { "SubOrderId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubRefunds_TenantId_CreatedAt",
                table: "CommerceHubRefunds",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubRefunds_TenantId_IdempotencyKey",
                table: "CommerceHubRefunds",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubRefunds_TenantId_Status_CreatedAt",
                table: "CommerceHubRefunds",
                columns: new[] { "TenantId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubReturnRequests_CustomerId_Status_CreatedAt",
                table: "CommerceHubReturnRequests",
                columns: new[] { "CustomerId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubReturnRequests_EscalationLevel_Status",
                table: "CommerceHubReturnRequests",
                columns: new[] { "EscalationLevel", "Status" },
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubReturnRequests_OrderItemId_Status_CreatedAt",
                table: "CommerceHubReturnRequests",
                columns: new[] { "OrderItemId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubReturnRequests_RefundId",
                table: "CommerceHubReturnRequests",
                column: "RefundId",
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubReturnRequests_VendorId_Status_CreatedAt",
                table: "CommerceHubReturnRequests",
                columns: new[] { "VendorId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubSubOrderItems_ListingId",
                table: "CommerceHubSubOrderItems",
                column: "ListingId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubSubOrderItems_SubOrderId",
                table: "CommerceHubSubOrderItems",
                column: "SubOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubSubOrderItems_SubOrderId_VariantId",
                table: "CommerceHubSubOrderItems",
                columns: new[] { "SubOrderId", "VariantId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubSubOrderItems_VariantId",
                table: "CommerceHubSubOrderItems",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubSubOrders_CreatedAt",
                table: "CommerceHubSubOrders",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubSubOrders_OrderNumber",
                table: "CommerceHubSubOrders",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubSubOrders_ShippingMethodId",
                table: "CommerceHubSubOrders",
                column: "ShippingMethodId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubSubOrders_StoreId_CreatedAt",
                table: "CommerceHubSubOrders",
                columns: new[] { "StoreId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_CommerceHubSubOrders_MarketplaceOrderId_StoreId_Live",
                table: "CommerceHubSubOrders",
                columns: new[] { "MarketplaceOrderId", "StoreId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubSubOrderStatusHistory_SubOrderId",
                table: "CommerceHubSubOrderStatusHistory",
                column: "SubOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubSubOrderStatusHistory_SubOrderId_ChangedAtUtc",
                table: "CommerceHubSubOrderStatusHistory",
                columns: new[] { "SubOrderId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubUnresolvedPaymentWebhooks_LocalPaymentId",
                table: "CommerceHubUnresolvedPaymentWebhooks",
                column: "LocalPaymentId",
                filter: "\"LocalPaymentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubUnresolvedPaymentWebhooks_OrderId",
                table: "CommerceHubUnresolvedPaymentWebhooks",
                column: "OrderId",
                filter: "\"OrderId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubUnresolvedPaymentWebhooks_Tenant_Status",
                table: "CommerceHubUnresolvedPaymentWebhooks",
                columns: new[] { "TenantId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_CommerceHubUnresolvedPaymentWebhooks_Provider_Event",
                table: "CommerceHubUnresolvedPaymentWebhooks",
                columns: new[] { "Provider", "ProviderEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubVendorEarnings_OriginalEarningId",
                table: "CommerceHubVendorEarnings",
                column: "OriginalEarningId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubVendorEarnings_PaymentId",
                table: "CommerceHubVendorEarnings",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubVendorEarnings_PayoutId",
                table: "CommerceHubVendorEarnings",
                column: "PayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubVendorEarnings_RefundId",
                table: "CommerceHubVendorEarnings",
                column: "RefundId");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubVendorEarnings_SubOrderId",
                table: "CommerceHubVendorEarnings",
                column: "SubOrderId",
                unique: true,
                filter: "\"OriginalEarningId\" IS NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubVendorEarnings_TenantId_Status_CreatedAt",
                table: "CommerceHubVendorEarnings",
                columns: new[] { "TenantId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubVendorPaymentAuditLogs_AdminId_TimestampUtc",
                table: "CommerceHubVendorPaymentAuditLogs",
                columns: new[] { "AdminId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubVendorPaymentAuditLogs_PaymentRequestId_TimestampUtc",
                table: "CommerceHubVendorPaymentAuditLogs",
                columns: new[] { "PaymentRequestId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubVendorPaymentMethods_IsActive",
                table: "CommerceHubVendorPaymentMethods",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "UX_CommerceHubVendorPaymentMethods_Name_DestinationValue",
                table: "CommerceHubVendorPaymentMethods",
                columns: new[] { "Name", "DestinationValue" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CommerceHubVendorPaymentProofs_ContentHash",
                table: "CommerceHubVendorPaymentProofs",
                column: "ContentHash",
                unique: true,
                filter: "\"ContentHash\" IS NOT NULL AND \"ContentHash\" <> '' AND \"ContentHash\" <> 'LEGACY_000000000000000000000000000000000000000000000000000000000'");

            migrationBuilder.CreateIndex(
                name: "UX_CommerceHubVendorPaymentProofs_ObjectKey",
                table: "CommerceHubVendorPaymentProofs",
                column: "ObjectKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CommerceHubVendorPaymentProofs_PaymentRequestId",
                table: "CommerceHubVendorPaymentProofs",
                column: "PaymentRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubVendorPaymentRequests_Status_CreatedAtUtc",
                table: "CommerceHubVendorPaymentRequests",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubVendorPaymentRequests_VendorId_CreatedAtUtc",
                table: "CommerceHubVendorPaymentRequests",
                columns: new[] { "VendorId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubVendorPaymentRequests_VendorId_Status_CreatedAtUtc",
                table: "CommerceHubVendorPaymentRequests",
                columns: new[] { "VendorId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_CommerceHubVendorPaymentRequests_Live_Method_TransferReference",
                table: "CommerceHubVendorPaymentRequests",
                columns: new[] { "PaymentMethodId", "TransferReference" },
                unique: true,
                filter: "\"TransferReference\" IS NOT NULL AND \"TransferReference\" <> '' AND \"Status\" <> 2");

            migrationBuilder.CreateIndex(
                name: "UX_CommerceHubVendorPaymentRequests_VendorId_IdempotencyKey",
                table: "CommerceHubVendorPaymentRequests",
                columns: new[] { "VendorId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubVendorPayoutMethods_VendorId_CreatedAt",
                table: "CommerceHubVendorPayoutMethods",
                columns: new[] { "VendorId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_CommerceHubVendorPayoutMethods_VendorId_Default",
                table: "CommerceHubVendorPayoutMethods",
                column: "VendorId",
                unique: true,
                filter: "\"IsDeleted\" = false AND \"IsDefault\" = true");

            migrationBuilder.CreateIndex(
                name: "UX_CommerceHubVendorPayoutMethods_VendorId_MethodType_DestinationValue",
                table: "CommerceHubVendorPayoutMethods",
                columns: new[] { "VendorId", "MethodType", "DestinationValue" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubVendorSettlements_CreatedAtUtc",
                table: "CommerceHubVendorSettlements",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubVendorSettlements_SettlementType_CreatedAtUtc",
                table: "CommerceHubVendorSettlements",
                columns: new[] { "SettlementType", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubVendorSettlements_VendorId_CreatedAtUtc",
                table: "CommerceHubVendorSettlements",
                columns: new[] { "VendorId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_CommerceHubVendorSettlements_ExternalReference",
                table: "CommerceHubVendorSettlements",
                column: "ExternalReference",
                unique: true,
                filter: "\"ExternalReference\" IS NOT NULL AND \"ExternalReference\" <> '' AND \"ExternalReference\" NOT LIKE 'legacy:%'");

            migrationBuilder.CreateIndex(
                name: "UX_CommerceHubVendorSettlements_Vendor_IdempotencyKey",
                table: "CommerceHubVendorSettlements",
                columns: new[] { "VendorId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommissionRules_ScopeType_IsActive_Priority",
                table: "CommissionRules",
                columns: new[] { "ScopeType", "IsActive", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_CommissionRules_ScopeType_ScopeId_Priority",
                table: "CommissionRules",
                columns: new[] { "ScopeType", "ScopeId", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_CouponReservations_CouponId",
                table: "CouponReservations",
                column: "CouponId");

            migrationBuilder.CreateIndex(
                name: "IX_CouponReservations_CustomerId",
                table: "CouponReservations",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CouponReservations_OrderId",
                table: "CouponReservations",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CouponReservations_TenantId_CouponId_CustomerId",
                table: "CouponReservations",
                columns: new[] { "TenantId", "CouponId", "CustomerId" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CouponReservations_TenantId_OrderId",
                table: "CouponReservations",
                columns: new[] { "TenantId", "OrderId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_IsActive",
                table: "Coupons",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_TenantId_NormalizedCode",
                table: "Coupons",
                columns: new[] { "TenantId", "NormalizedCode" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_CouponUsages_CouponId",
                table: "CouponUsages",
                column: "CouponId");

            migrationBuilder.CreateIndex(
                name: "IX_CouponUsages_OrderId",
                table: "CouponUsages",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CouponUsages_TenantId_CouponId",
                table: "CouponUsages",
                columns: new[] { "TenantId", "CouponId" });

            migrationBuilder.CreateIndex(
                name: "IX_CouponUsages_TenantId_CouponId_CustomerId",
                table: "CouponUsages",
                columns: new[] { "TenantId", "CouponId", "CustomerId" },
                filter: "\"CustomerId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_CouponUsages_TenantId_OrderId",
                table: "CouponUsages",
                columns: new[] { "TenantId", "OrderId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Currencies_Name",
                table: "Currencies",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAddresses_CurrentVersionId",
                table: "CustomerAddresses",
                column: "CurrentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAddresses_CustomerId",
                table: "CustomerAddresses",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAddresses_TenantId_CustomerId_IsDefault",
                table: "CustomerAddresses",
                columns: new[] { "TenantId", "CustomerId", "IsDefault" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAddressVersions_AddressAdmissionEvidenceId",
                table: "CustomerAddressVersions",
                column: "AddressAdmissionEvidenceId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAddressVersions_CustomerAddressId",
                table: "CustomerAddressVersions",
                column: "CustomerAddressId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAddressVersions_GeographicCatalogReleaseId_CountryP~",
                table: "CustomerAddressVersions",
                columns: new[] { "GeographicCatalogReleaseId", "CountryPlaceId", "AdministrativeAreaPlaceId", "LocalityPlaceId", "DistrictPlaceId", "ZonePlaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAddressVersions_TenantId_CustomerAddressId_Version",
                table: "CustomerAddressVersions",
                columns: new[] { "TenantId", "CustomerAddressId", "Version" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Email",
                table: "Customers",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Phone",
                table: "Customers",
                column: "Phone");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TaxNumber",
                table: "Customers",
                column: "TaxNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId",
                table: "Customers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerTransactions_CustomerId",
                table: "CustomerTransactions",
                column: "CustomerId");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM ""CustomerTransactions""
        WHERE ""IsDeleted"" = false
          AND ""TransactionType"" = 11
        GROUP BY ""TenantId"", ""CustomerId"", ""TransactionType""
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'duplicate active customer opening-balance transaction rows exist';
    END IF;
END
$$;
");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerTransactions_OpeningBalance_Once",
                table: "CustomerTransactions",
                columns: new[] { "TenantId", "CustomerId", "TransactionType" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"TransactionType\" = 11");

            migrationBuilder.CreateIndex(
                name: "IX_CustomsAdministrationAudit_Evidence_Action",
                table: "CustomsAdministrationAudit",
                columns: new[] { "CustomsEvidenceId", "Action", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomsAdministrationAudit_Tenant_Timeline",
                table: "CustomsAdministrationAudit",
                columns: new[] { "TenantId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomsAdmissionAudit_Shipment_Action",
                table: "CustomsAdmissionAudit",
                columns: new[] { "ShipmentId", "Action", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomsAdmissionAudit_Tenant_Timeline",
                table: "CustomsAdmissionAudit",
                columns: new[] { "TenantId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomsEvidence_Product_Status",
                table: "CustomsEvidence",
                columns: new[] { "TenantId", "ProductId", "ProductVariantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomsEvidence_Source_Status",
                table: "CustomsEvidence",
                columns: new[] { "TenantId", "SourceKind", "SourceId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomsEvidenceApprovals_Evidence_Version",
                table: "CustomsEvidenceApprovals",
                columns: new[] { "TenantId", "CustomsEvidenceId", "CustomsEvidenceVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomsEvidenceFingerprints_Fingerprint",
                table: "CustomsEvidenceFingerprints",
                columns: new[] { "TenantId", "FingerprintSha256" });

            migrationBuilder.CreateIndex(
                name: "UQ_CustomsEvidenceFingerprints_Version",
                table: "CustomsEvidenceFingerprints",
                column: "CustomsEvidenceVersionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomsEvidenceHistory_Evidence_Version",
                table: "CustomsEvidenceHistory",
                columns: new[] { "CustomsEvidenceId", "VersionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomsEvidencePublications_Evidence_Version",
                table: "CustomsEvidencePublications",
                columns: new[] { "TenantId", "CustomsEvidenceId", "CustomsEvidenceVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomsEvidencePublications_Product_Timeline",
                table: "CustomsEvidencePublications",
                columns: new[] { "TenantId", "ProductId", "ProductVariantId", "Action", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomsEvidenceVersion_Fingerprint",
                table: "CustomsEvidenceVersions",
                columns: new[] { "TenantId", "EvidenceFingerprintSha256" });

            migrationBuilder.CreateIndex(
                name: "UQ_CustomsEvidenceVersion_Number",
                table: "CustomsEvidenceVersions",
                columns: new[] { "CustomsEvidenceId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_CustomsPublicationLock_Key",
                table: "CustomsPublicationLocks",
                columns: new[] { "TenantId", "LockKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailVerificationTokens_ExpiresAtUtc",
                table: "EmailVerificationTokens",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_EmailVerificationTokens_TokenHash",
                table: "EmailVerificationTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailVerificationTokens_UserId",
                table: "EmailVerificationTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_BankId",
                table: "Expenses",
                column: "BankId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CashboxId",
                table: "Expenses",
                column: "CashboxId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ExpenseCategoryId",
                table: "Expenses",
                column: "ExpenseCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_SupplierId",
                table: "Expenses",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_FawryWebhookReplayRecords_Tenant_EvidenceHash",
                table: "FawryWebhookReplayRecords",
                columns: new[] { "TenantId", "EvidenceHash" });

            migrationBuilder.CreateIndex(
                name: "IX_FawryWebhookReplayRecords_Tenant_Provider_Event",
                table: "FawryWebhookReplayRecords",
                columns: new[] { "TenantId", "Provider", "ProviderEventId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_FawryWebhookReplayRecords_Tenant_ProviderReference",
                table: "FawryWebhookReplayRecords",
                columns: new[] { "TenantId", "ProviderReference" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialAccounts_ParentAccountId",
                table: "FinancialAccounts",
                column: "ParentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransactions_AccountId",
                table: "FinancialTransactions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_GeographicAudit_Aggregate_Timeline",
                table: "GeographicAudit",
                columns: new[] { "AggregateType", "AggregateId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GeographicAudit_MappingReleaseId",
                table: "GeographicAudit",
                column: "MappingReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_GeographicAudit_Release_Timeline",
                table: "GeographicAudit",
                columns: new[] { "CatalogReleaseId", "MappingReleaseId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GeographicAudit_Tenant_Timeline",
                table: "GeographicAudit",
                columns: new[] { "TenantId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GeoCatalogPublications_Release_Published",
                table: "GeographicCatalogPublications",
                columns: new[] { "CatalogReleaseId", "PublishedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GeographicCatalogReleaseEdges_HierarchyEdgeId",
                table: "GeographicCatalogReleaseEdges",
                column: "HierarchyEdgeId");

            migrationBuilder.CreateIndex(
                name: "IX_GeographicCatalogReleasePlaces_PlaceId",
                table: "GeographicCatalogReleasePlaces",
                column: "PlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_GeoCatalogRelease_Status_Country_Published",
                table: "GeographicCatalogReleases",
                columns: new[] { "Status", "CountryScopeIso2", "PublishedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GeographicCatalogReleases_RollbackOfReleaseId",
                table: "GeographicCatalogReleases",
                column: "RollbackOfReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_GeographicCatalogReleases_SupersedesReleaseId",
                table: "GeographicCatalogReleases",
                column: "SupersedesReleaseId");

            migrationBuilder.CreateIndex(
                name: "UQ_GeoCatalogRelease_Key",
                table: "GeographicCatalogReleases",
                column: "ReleaseKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_GeoCatalogRelease_Number",
                table: "GeographicCatalogReleases",
                column: "ReleaseNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GeographicCountryProfiles_IntroducedCatalogReleaseId",
                table: "GeographicCountryProfiles",
                column: "IntroducedCatalogReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_GeographicCountryProfiles_RetiredCatalogReleaseId",
                table: "GeographicCountryProfiles",
                column: "RetiredCatalogReleaseId");

            migrationBuilder.CreateIndex(
                name: "UQ_GeoCountryProfile_Country",
                table: "GeographicCountryProfiles",
                column: "CountryPlaceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_GeoCountryProfile_Iso2",
                table: "GeographicCountryProfiles",
                column: "Iso2",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_GeoCountryProfile_Iso3",
                table: "GeographicCountryProfiles",
                column: "Iso3",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GeographicPlaceAliases_IntroducedCatalogReleaseId",
                table: "GeographicPlaceAliases",
                column: "IntroducedCatalogReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_GeographicPlaceAliases_PlaceId",
                table: "GeographicPlaceAliases",
                column: "PlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_GeographicPlaceAliases_RetiredCatalogReleaseId",
                table: "GeographicPlaceAliases",
                column: "RetiredCatalogReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_GeoPlaceAliases_Search",
                table: "GeographicPlaceAliases",
                columns: new[] { "CountryPlaceId", "LanguageTag", "NormalizedAlias" });

            migrationBuilder.CreateIndex(
                name: "IX_GeographicPlaceHierarchyEdges_IntroducedCatalogReleaseId",
                table: "GeographicPlaceHierarchyEdges",
                column: "IntroducedCatalogReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_GeographicPlaceHierarchyEdges_RetiredCatalogReleaseId",
                table: "GeographicPlaceHierarchyEdges",
                column: "RetiredCatalogReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_GeoHierarchy_ChildPlaceId",
                table: "GeographicPlaceHierarchyEdges",
                column: "ChildPlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_GeoHierarchy_Country_Parent_Primary",
                table: "GeographicPlaceHierarchyEdges",
                columns: new[] { "CountryPlaceId", "ParentPlaceId", "IsPrimary" });

            migrationBuilder.CreateIndex(
                name: "UQ_GeoHierarchy_Active",
                table: "GeographicPlaceHierarchyEdges",
                columns: new[] { "ParentPlaceId", "ChildPlaceId", "IntroducedCatalogReleaseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GeographicPlaceLevels_IntroducedCatalogReleaseId",
                table: "GeographicPlaceLevels",
                column: "IntroducedCatalogReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_GeographicPlaceLevels_RetiredCatalogReleaseId",
                table: "GeographicPlaceLevels",
                column: "RetiredCatalogReleaseId");

            migrationBuilder.CreateIndex(
                name: "UQ_GeoPlaceLevel_Code",
                table: "GeographicPlaceLevels",
                columns: new[] { "CountryPlaceId", "LevelCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_GeoPlaceLevel_Order",
                table: "GeographicPlaceLevels",
                columns: new[] { "CountryPlaceId", "SortOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GeographicPlaceNames_IntroducedCatalogReleaseId",
                table: "GeographicPlaceNames",
                column: "IntroducedCatalogReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_GeographicPlaceNames_RetiredCatalogReleaseId",
                table: "GeographicPlaceNames",
                column: "RetiredCatalogReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_GeoPlaceNames_Preferred",
                table: "GeographicPlaceNames",
                columns: new[] { "PlaceId", "LanguageTag", "IsPreferred" });

            migrationBuilder.CreateIndex(
                name: "IX_GeoPlaceNames_Search",
                table: "GeographicPlaceNames",
                columns: new[] { "LanguageTag", "SearchName" });

            migrationBuilder.CreateIndex(
                name: "IX_GeographicPlaces_IntroducedCatalogReleaseId",
                table: "GeographicPlaces",
                column: "IntroducedCatalogReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_GeographicPlaces_RetiredCatalogReleaseId",
                table: "GeographicPlaces",
                column: "RetiredCatalogReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_GeographicPlaces_SuccessorPlaceId",
                table: "GeographicPlaces",
                column: "SuccessorPlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_GeoPlaces_Country_Kind_Code_Status",
                table: "GeographicPlaces",
                columns: new[] { "CountryPlaceId", "PlaceKind", "CanonicalCode", "Status" });

            migrationBuilder.CreateIndex(
                name: "UQ_GeoPlace_ExternalStandardCode",
                table: "GeographicPlaces",
                columns: new[] { "ExternalStandard", "ExternalStandardCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_GeoPlace_StableKey",
                table: "GeographicPlaces",
                column: "StableKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GlobalPhoneReputation_PhoneNumber",
                table: "GlobalPhoneReputation",
                column: "PhoneNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HomepageSections_SectionGroupId",
                table: "HomepageSections",
                column: "SectionGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_HomepageSections_TenantId_IsDraft_Order",
                table: "HomepageSections",
                columns: new[] { "TenantId", "IsDraft", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_HomepageSections_TenantId_IsPublished_Order",
                table: "HomepageSections",
                columns: new[] { "TenantId", "IsPublished", "Order" });

            migrationBuilder.CreateIndex(
                name: "UQ_HomepageSections_SectionGroupId_Version",
                table: "HomepageSections",
                columns: new[] { "SectionGroupId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboxState_Delivered",
                table: "InboxState",
                column: "Delivered");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryEventInbox_TenantId_ReferenceLineId_EventType",
                table: "InventoryEventInbox",
                columns: new[] { "TenantId", "ReferenceLineId", "EventType" });

            migrationBuilder.CreateIndex(
                name: "UX_InventoryEventInbox_TenantId_EventId",
                table: "InventoryEventInbox",
                columns: new[] { "TenantId", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_TenantId_OrderItemId_VariantId_WarehouseId",
                table: "InventoryReservations",
                columns: new[] { "TenantId", "OrderItemId", "VariantId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_TenantId_Status_ExpiresAtUtc",
                table: "InventoryReservations",
                columns: new[] { "TenantId", "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAcct_Clearing",
                table: "LedgerAccounts",
                columns: new[] { "TenantId", "IsClearing" },
                filter: "\"IsClearing\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAcct_Parent",
                table: "LedgerAccounts",
                column: "ParentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAcct_Tenant",
                table: "LedgerAccounts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAcct_TenantCode",
                table: "LedgerAccounts",
                columns: new[] { "TenantId", "AccountCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAcct_TenantType",
                table: "LedgerAccounts",
                columns: new[] { "TenantId", "AccountType" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerJournal_Date",
                table: "LedgerJournalEntries",
                columns: new[] { "TenantId", "JournalDate" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerJournal_Idempotency",
                table: "LedgerJournalEntries",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerJournal_Number",
                table: "LedgerJournalEntries",
                columns: new[] { "TenantId", "JournalNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerJournal_Posted",
                table: "LedgerJournalEntries",
                column: "PostedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerJournal_Source",
                table: "LedgerJournalEntries",
                columns: new[] { "TenantId", "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerJournal_Status",
                table: "LedgerJournalEntries",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerJournal_Tenant",
                table: "LedgerJournalEntries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerJournalEntries_ReversedByJournalId",
                table: "LedgerJournalEntries",
                column: "ReversedByJournalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerLines_AcctDate",
                table: "LedgerLines",
                columns: new[] { "AccountId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerLines_Journal",
                table: "LedgerLines",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerLines_Tenant",
                table: "LedgerLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerLines_TenantAcct",
                table: "LedgerLines",
                columns: new[] { "TenantId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketingReviewEvidences_EvidenceType",
                table: "MarketingReviewEvidences",
                column: "EvidenceType");

            migrationBuilder.CreateIndex(
                name: "IX_MarketingReviewEvidences_Platform",
                table: "MarketingReviewEvidences",
                column: "Platform");

            migrationBuilder.CreateIndex(
                name: "IX_MarketingReviewEvidences_TenantId",
                table: "MarketingReviewEvidences",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "UQ_MarketingReviewEvidences_ServiceReview",
                table: "MarketingReviewEvidences",
                column: "ServiceReviewId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceCategoryTranslations_Language",
                table: "MarketplaceCategoryTranslations",
                column: "Language");

            migrationBuilder.CreateIndex(
                name: "UQ_MarketplaceCategoryTranslations_CategoryId_Language",
                table: "MarketplaceCategoryTranslations",
                columns: new[] { "CategoryId", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceCmsPages_IsDraft_Slug",
                table: "MarketplaceCmsPages",
                columns: new[] { "IsDraft", "Slug" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceCmsPages_IsPublished_Slug",
                table: "MarketplaceCmsPages",
                columns: new[] { "IsPublished", "Slug" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceCmsPages_PageGroupId",
                table: "MarketplaceCmsPages",
                column: "PageGroupId");

            migrationBuilder.CreateIndex(
                name: "UQ_MarketplaceCmsPages_PageGroupId_Version",
                table: "MarketplaceCmsPages",
                columns: new[] { "PageGroupId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceEventOutboxDeadLetterArchives_ArchivedAtUtc",
                table: "MarketplaceEventOutboxDeadLetterArchives",
                column: "ArchivedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceEventOutboxDeadLetterArchives_Tenant_Aggregate",
                table: "MarketplaceEventOutboxDeadLetterArchives",
                columns: new[] { "TenantId", "AggregateRootId" });

            migrationBuilder.CreateIndex(
                name: "UQ_MarketplaceEventOutboxDeadLetterArchives_OriginalId",
                table: "MarketplaceEventOutboxDeadLetterArchives",
                column: "OriginalOutboxMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceEventOutbox_AggregateOrdering",
                table: "MarketplaceEventOutboxMessages",
                columns: new[] { "TenantId", "AggregateRootId", "PublishedAtUtc", "Version", "OccurredAtUtc", "CreatedAt", "Id" },
                filter: "\"PublishedAtUtc\" IS NULL AND \"IsFailed\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceEventOutbox_AggregateOrderingBlockers",
                table: "MarketplaceEventOutboxMessages",
                columns: new[] { "TenantId", "AggregateRootId", "Version", "OccurredAtUtc", "CreatedAt", "Id" },
                filter: "\"PublishedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceEventOutbox_EventId",
                table: "MarketplaceEventOutboxMessages",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceEventOutbox_OccurredAtUtc",
                table: "MarketplaceEventOutboxMessages",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceEventOutbox_ProcessingStatus",
                table: "MarketplaceEventOutboxMessages",
                columns: new[] { "PublishedAtUtc", "IsFailed", "NextRetryAtUtc", "ClaimedUntilUtc" },
                filter: "\"PublishedAtUtc\" IS NULL AND \"IsFailed\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceEventOutbox_TenantId_AggregateRootId",
                table: "MarketplaceEventOutboxMessages",
                columns: new[] { "TenantId", "AggregateRootId" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceEventOutbox_TenantId_EntityId",
                table: "MarketplaceEventOutboxMessages",
                columns: new[] { "TenantId", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceFooterLinks_SectionId_SortOrder",
                table: "MarketplaceFooterLinks",
                columns: new[] { "SectionId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceFooterSections_SortOrder",
                table: "MarketplaceFooterSections",
                column: "SortOrder");

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
                name: "IX_MarketplaceProjectionOutbox_Idempotency",
                table: "MarketplaceProjectionOutboxMessages",
                columns: new[] { "EventId", "ConsumerName", "EventType" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceProjectionOutbox_ProcessedAtUtc",
                table: "MarketplaceProjectionOutboxMessages",
                column: "ProcessedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceProjectionOutbox_ProcessingStatus",
                table: "MarketplaceProjectionOutboxMessages",
                columns: new[] { "TenantId", "ConsumerName", "ProcessedAtUtc", "NextRetryAtUtc", "ClaimedUntilUtc" },
                filter: "\"ProcessedAtUtc\" IS NULL AND \"IsFailed\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceProjectionOutbox_TenantId_EntityId",
                table: "MarketplaceProjectionOutboxMessages",
                columns: new[] { "TenantId", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceSocialLinks_SortOrder",
                table: "MarketplaceSocialLinks",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "UQ_MarketplaceSocialLinks_Platform",
                table: "MarketplaceSocialLinks",
                column: "Platform",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingChecklists_TenantId",
                table: "OnboardingChecklists",
                column: "TenantId",
                unique: true);

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
                name: "IX_RefreshToken_TenantId_CurrentAccessTokenJti",
                table: "OnlineCustomerRefreshTokens",
                columns: new[] { "TenantId", "CurrentAccessTokenJti" });

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
                name: "IX_OnlineCustomers_CreatedAtUtc",
                table: "OnlineCustomers",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCustomers_EmailVerified",
                table: "OnlineCustomers",
                column: "EmailVerified");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCustomers_IsActive",
                table: "OnlineCustomers",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCustomers_Provider",
                table: "OnlineCustomers",
                column: "Provider");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCustomers_TenantId",
                table: "OnlineCustomers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCustomers_TenantId_Email",
                table: "OnlineCustomers",
                columns: new[] { "TenantId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCustomers_TenantId_Provider_ProviderUserId",
                table: "OnlineCustomers",
                columns: new[] { "TenantId", "Provider", "ProviderUserId" });

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
                name: "IX_OrderItems_OrderId",
                table: "OrderItems",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_OrderId_Id",
                table: "OrderItems",
                columns: new[] { "OrderId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_ProductId",
                table: "OrderItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_UnitId",
                table: "OrderItems",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_VariantId",
                table: "OrderItems",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_WarehouseId",
                table: "OrderItems",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefundLines_OrderId_OrderItemId",
                table: "OrderRefundLines",
                columns: new[] { "OrderId", "OrderItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefundLines_OrderItemId",
                table: "OrderRefundLines",
                column: "OrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefundLines_OrderRefundId",
                table: "OrderRefundLines",
                column: "OrderRefundId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefundLines_Refund_OrderItem",
                table: "OrderRefundLines",
                columns: new[] { "OrderRefundId", "OrderItemId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefundLines_TenantId_OrderId_OrderRefundId",
                table: "OrderRefundLines",
                columns: new[] { "TenantId", "OrderId", "OrderRefundId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefunds_SettlementBatchId",
                table: "OrderRefunds",
                column: "SettlementBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefunds_TenantId_FinancialPostingStatus_RefundedAtUtc",
                table: "OrderRefunds",
                columns: new[] { "TenantId", "FinancialPostingStatus", "RefundedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefunds_TenantId_OrderId",
                table: "OrderRefunds",
                columns: new[] { "TenantId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefunds_TenantId_OrderId_RefundId",
                table: "OrderRefunds",
                columns: new[] { "TenantId", "OrderId", "RefundId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefunds_TenantId_SettlementBatchId",
                table: "OrderRefunds",
                columns: new[] { "TenantId", "SettlementBatchId" },
                filter: "\"SettlementBatchId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_OrderRequestIdempotency_CompletedAtUtc_IsDeleted",
                table: "OrderRequestIdempotency",
                columns: new[] { "CompletedAtUtc", "IsDeleted" },
                filter: "\"CompletedAtUtc\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrderRequestIdempotency_TenantId_Key",
                table: "OrderRequestIdempotency",
                columns: new[] { "TenantId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderRequestIdempotency_TenantId_OrderId",
                table: "OrderRequestIdempotency",
                columns: new[] { "TenantId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderRequestIdempotency_TenantId_RequestHash_IsDeleted",
                table: "OrderRequestIdempotency",
                columns: new[] { "TenantId", "RequestHash", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_AppliedPromotionId",
                table: "Orders",
                column: "AppliedPromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_BillingAddressAdmissionEvidenceId",
                table: "Orders",
                column: "BillingAddressAdmissionEvidenceId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CouponId",
                table: "Orders",
                column: "CouponId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CreatedAt",
                table: "Orders",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CustomerId",
                table: "Orders",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_OnlineCustomerId",
                table: "Orders",
                column: "OnlineCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PaymentStatus",
                table: "Orders",
                column: "PaymentStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_SessionId",
                table: "Orders",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ShippingAddressAdmissionEvidenceId",
                table: "Orders",
                column: "ShippingAddressAdmissionEvidenceId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ShippingMethodId",
                table: "Orders",
                column: "ShippingMethodId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Source",
                table: "Orders",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Status",
                table: "Orders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_BillingGeoRelease_Country",
                table: "Orders",
                columns: new[] { "TenantId", "BillingGeographicCatalogReleaseId", "BillingCountryPlaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_CartId_ActiveStatuses",
                table: "Orders",
                columns: new[] { "TenantId", "CartId" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"CartId\" IS NOT NULL AND \"Status\" IN (0, 1, 2, 8)");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_CheckoutState_CreatedAt",
                table: "Orders",
                columns: new[] { "TenantId", "CheckoutState", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_FinancialPostingStatus_CreatedAt",
                table: "Orders",
                columns: new[] { "TenantId", "FinancialPostingStatus", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_GuestPhone",
                table: "Orders",
                columns: new[] { "TenantId", "GuestPhone" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_OrderNumber",
                table: "Orders",
                columns: new[] { "TenantId", "OrderNumber" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_PaymentStatus_CreatedAt",
                table: "Orders",
                columns: new[] { "TenantId", "PaymentStatus", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_ShippingGeoRelease_Country",
                table: "Orders",
                columns: new[] { "TenantId", "ShippingGeographicCatalogReleaseId", "ShippingCountryPlaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_Source_CreatedAt",
                table: "Orders",
                columns: new[] { "TenantId", "Source", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_Source_SourceOrderId",
                table: "Orders",
                columns: new[] { "TenantId", "Source", "SourceOrderId" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"SourceOrderId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_Status_CreatedAt",
                table: "Orders",
                columns: new[] { "TenantId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_WarehouseId",
                table: "Orders",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_EnqueueTime",
                table: "OutboxMessage",
                column: "EnqueueTime");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_ExpirationTime",
                table: "OutboxMessage",
                column: "ExpirationTime");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_InboxMessageId_InboxConsumerId_SequenceNumber",
                table: "OutboxMessage",
                columns: new[] { "InboxMessageId", "InboxConsumerId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_OutboxId_SequenceNumber",
                table: "OutboxMessage",
                columns: new[] { "OutboxId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_CriticalAggregateOrdering",
                table: "OutboxMessages",
                columns: new[] { "TenantId", "AggregateId", "EventVersion", "CreatedAtUtc", "Id" },
                filter: "\"ProcessedAtUtc\" IS NULL AND \"TenantId\" IS NOT NULL AND \"AggregateId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_OperatorReconciliation",
                table: "OutboxMessages",
                columns: new[] { "RequiresOperatorReconciliation", "ProcessedAtUtc", "CreatedAtUtc" },
                filter: "\"RequiresOperatorReconciliation\" = true AND \"ProcessedAtUtc\" IS NULL");

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
                name: "IX_OutboxState_Created",
                table: "OutboxState",
                column: "Created");

            migrationBuilder.CreateIndex(
                name: "IX_PartyLedgerAdjustments_TenantId_IdempotencyKey",
                table: "PartyLedgerAdjustments",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartyLedgerAdjustments_TenantId_PartyType_PartyId_Adjustmen~",
                table: "PartyLedgerAdjustments",
                columns: new[] { "TenantId", "PartyType", "PartyId", "AdjustmentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCancellationOutbox_OrderId",
                table: "PaymentCancellationOutboxMessages",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCancellationOutbox_Processing",
                table: "PaymentCancellationOutboxMessages",
                columns: new[] { "ProcessedAtUtc", "IsFailed", "NextRetryAtUtc", "ClaimedUntilUtc" })
                .Annotation("Npgsql:IndexInclude", new[] { "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCancellationOutbox_TenantId",
                table: "PaymentCancellationOutboxMessages",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_Active_OrderId_Provider_Unique",
                table: "PaymentIntents",
                columns: new[] { "TenantId", "OrderId", "Provider" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"Status\" IN (0, 1, 2, 7)");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_OrderId",
                table: "PaymentIntents",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_TenantId_PayoutBatchId",
                table: "PaymentIntents",
                columns: new[] { "TenantId", "PayoutBatchId" });

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
                name: "IX_PaymentSettlementBatches_Tenant_PayoutBatchId",
                table: "PaymentSettlementBatches",
                columns: new[] { "TenantId", "PayoutBatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSettlementBatches_Tenant_Type_SettlementId",
                table: "PaymentSettlementBatches",
                columns: new[] { "TenantId", "SettlementType", "SettlementId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSettlementOrders_OrderId",
                table: "PaymentSettlementOrders",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSettlementOrders_PaymentIntentId",
                table: "PaymentSettlementOrders",
                column: "PaymentIntentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSettlementOrders_SettlementBatchId",
                table: "PaymentSettlementOrders",
                column: "SettlementBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSettlementOrders_Tenant_Batch_Order",
                table: "PaymentSettlementOrders",
                columns: new[] { "TenantId", "SettlementBatchId", "OrderId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSettlementOrders_Tenant_Order",
                table: "PaymentSettlementOrders",
                columns: new[] { "TenantId", "OrderId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSettlementOrders_Tenant_PaymentIntent",
                table: "PaymentSettlementOrders",
                columns: new[] { "TenantId", "PaymentIntentId" },
                filter: "\"IsDeleted\" = false AND \"PaymentIntentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookLogs_PaymentIntentId",
                table: "PaymentWebhookLogs",
                column: "PaymentIntentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookLogs_TenantId_Provider_ProviderEventId",
                table: "PaymentWebhookLogs",
                columns: new[] { "TenantId", "Provider", "ProviderEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymobWebhookReplayRecords_Tenant_EvidenceHash",
                table: "PaymobWebhookReplayRecords",
                columns: new[] { "TenantId", "EvidenceHash" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymobWebhookReplayRecords_Tenant_Provider_Event",
                table: "PaymobWebhookReplayRecords",
                columns: new[] { "TenantId", "Provider", "ProviderEventId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_PaymobWebhookReplayRecords_Tenant_ProviderReference",
                table: "PaymobWebhookReplayRecords",
                columns: new[] { "TenantId", "ProviderReference" });

            migrationBuilder.CreateIndex(
                name: "IX_PhoneRiskProfiles_TenantId_IsBlocked_BlockedAtUtc",
                table: "PhoneRiskProfiles",
                columns: new[] { "TenantId", "IsBlocked", "BlockedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PhoneRiskProfiles_TenantId_PhoneNumber",
                table: "PhoneRiskProfiles",
                columns: new[] { "TenantId", "PhoneNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhoneSpamReports_PhoneNumber",
                table: "PhoneSpamReports",
                column: "PhoneNumber");

            migrationBuilder.CreateIndex(
                name: "IX_PhoneSpamReports_TenantId_OrderId",
                table: "PhoneSpamReports",
                columns: new[] { "TenantId", "OrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PixelTrackingConfigs_TenantId",
                table: "PixelTrackingConfigs",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaceDeprecations_CatalogReleaseId",
                table: "PlaceDeprecations",
                column: "CatalogReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceDeprecations_SuccessorPlaceId",
                table: "PlaceDeprecations",
                column: "SuccessorPlaceId");

            migrationBuilder.CreateIndex(
                name: "UQ_PlaceDeprecations_Place_Release",
                table: "PlaceDeprecations",
                columns: new[] { "PlaceId", "CatalogReleaseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaceMergeHistory_CatalogReleaseId",
                table: "PlaceMergeHistory",
                column: "CatalogReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceMergeHistory_TargetPlaceId",
                table: "PlaceMergeHistory",
                column: "TargetPlaceId");

            migrationBuilder.CreateIndex(
                name: "UQ_PlaceMergeHistory_Source_Target_Release",
                table: "PlaceMergeHistory",
                columns: new[] { "SourcePlaceId", "TargetPlaceId", "CatalogReleaseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedEvents_Idempotency",
                table: "ProcessedEvents",
                columns: new[] { "EventId", "ConsumerName", "EventType" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedEvents_ProcessedAtUtc",
                table: "ProcessedEvents",
                column: "ProcessedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedEvents_TenantId_EntityId",
                table: "ProcessedEvents",
                columns: new[] { "TenantId", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductMedia_ProductId_Position",
                table: "ProductMedia",
                columns: new[] { "ProductId", "Position" });

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
                name: "IX_ProductRequestIdempotency_TenantId_Key_Operation",
                table: "ProductRequestIdempotency",
                columns: new[] { "TenantId", "Key", "Operation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductRequestIdempotency_TenantId_ProductId",
                table: "ProductRequestIdempotency",
                columns: new[] { "TenantId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductRequestIdempotency_TenantId_ProductVariantId",
                table: "ProductRequestIdempotency",
                columns: new[] { "TenantId", "ProductVariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviews_ProductId_Status_CreatedAtUtc",
                table: "ProductReviews",
                columns: new[] { "ProductId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviews_ProductId_UserId",
                table: "ProductReviews",
                columns: new[] { "ProductId", "UserId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviews_Status_Rating_ProductId",
                table: "ProductReviews",
                columns: new[] { "Status", "Rating", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviews_UserId_CreatedAtUtc",
                table: "ProductReviews",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_CategoryId",
                table: "Products",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_IsOnlineVisible",
                table: "Products",
                column: "IsOnlineVisible");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Name",
                table: "Products",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Products_PublishStatus",
                table: "Products",
                column: "PublishStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId",
                table: "Products",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId_Barcode",
                table: "Products",
                columns: new[] { "TenantId", "Barcode" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"Barcode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId_CategoryId",
                table: "Products",
                columns: new[] { "TenantId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId_SKU",
                table: "Products",
                columns: new[] { "TenantId", "SKU" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"SKU\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId_Slug",
                table: "Products",
                columns: new[] { "TenantId", "Slug" },
                filter: "\"IsDeleted\" = false AND \"Slug\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductUnits_Barcode_Active",
                table: "ProductUnits",
                column: "Barcode",
                filter: "\"IsDeleted\" = false AND \"Barcode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductUnits_UnitId",
                table: "ProductUnits",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "UX_ProductUnits_ProductId_Base_Active",
                table: "ProductUnits",
                columns: new[] { "ProductId", "IsBaseUnit" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"IsBaseUnit\" = true");

            migrationBuilder.CreateIndex(
                name: "UX_ProductUnits_ProductId_Default_Active",
                table: "ProductUnits",
                columns: new[] { "ProductId", "IsDefault" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"IsDefault\" = true");

            migrationBuilder.CreateIndex(
                name: "UX_ProductUnits_ProductId_UnitId_Active",
                table: "ProductUnits",
                columns: new[] { "ProductId", "UnitId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantAttributes_VariantId",
                table: "ProductVariantAttributes",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantAttributes_VariantId_Name",
                table: "ProductVariantAttributes",
                columns: new[] { "VariantId", "Name" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_Barcode",
                table: "ProductVariants",
                columns: new[] { "TenantId", "Barcode" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"SemanticLifecycleState\" = 0 AND \"Barcode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_ProductId",
                table: "ProductVariants",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_ProductId_IsDefault",
                table: "ProductVariants",
                columns: new[] { "TenantId", "ProductId", "IsDefault" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"SemanticLifecycleState\" = 0 AND \"IsDefault\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_SKU",
                table: "ProductVariants",
                columns: new[] { "TenantId", "SKU" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"SemanticLifecycleState\" = 0 AND \"SKU\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_TenantId_ProductId",
                table: "ProductVariants",
                columns: new[] { "TenantId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_TenantId_ProductId_AttributeCombinationKey",
                table: "ProductVariants",
                columns: new[] { "TenantId", "ProductId", "AttributeCombinationKey" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"SemanticLifecycleState\" = 0 AND \"AttributeCombinationKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_TenantId_ProductId_MediaId",
                table: "ProductVariants",
                columns: new[] { "TenantId", "ProductId", "MediaId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_TenantId_SemanticLifecycleState",
                table: "ProductVariants",
                columns: new[] { "TenantId", "SemanticLifecycleState" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_TenantId_SemanticOperationId",
                table: "ProductVariants",
                columns: new[] { "TenantId", "SemanticOperationId" },
                filter: "\"SemanticOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantStocks_TenantId_VariantId_WarehouseId",
                table: "ProductVariantStocks",
                columns: new[] { "TenantId", "VariantId", "WarehouseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantStocks_VariantId",
                table: "ProductVariantStocks",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantStocks_WarehouseId",
                table: "ProductVariantStocks",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodes_CategoryId_IsActive",
                table: "PromoCodes",
                columns: new[] { "CategoryId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodes_IsActive_ExpiryDate",
                table: "PromoCodes",
                columns: new[] { "IsActive", "ExpiryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodes_VendorId_IsActive",
                table: "PromoCodes",
                columns: new[] { "VendorId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "UX_PromoCodes_Code",
                table: "PromoCodes",
                column: "Code",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodeUsages_MarketplaceOrderId_AppliedAtUtc",
                table: "PromoCodeUsages",
                columns: new[] { "MarketplaceOrderId", "AppliedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodeUsages_PromoCodeId",
                table: "PromoCodeUsages",
                column: "PromoCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodeUsages_PromoCodeId_CustomerId",
                table: "PromoCodeUsages",
                columns: new[] { "PromoCodeId", "CustomerId" },
                filter: "\"CustomerId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UX_PromoCodeUsages_MarketplaceOrderId",
                table: "PromoCodeUsages",
                column: "MarketplaceOrderId",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UX_PromoCodeUsages_PromoCodeId_MarketplaceOrderId",
                table: "PromoCodeUsages",
                columns: new[] { "PromoCodeId", "MarketplaceOrderId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_Priority",
                table: "Promotions",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_TenantId_IsActive_StartDate_EndDate",
                table: "Promotions",
                columns: new[] { "TenantId", "IsActive", "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_TenantId_Name",
                table: "Promotions",
                columns: new[] { "TenantId", "Name" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_TenantId_Scope_AppliesToId_IsActive",
                table: "Promotions",
                columns: new[] { "TenantId", "Scope", "AppliesToId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PromotionUsages_CustomerId",
                table: "PromotionUsages",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionUsages_OrderId",
                table: "PromotionUsages",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionUsages_PromotionId",
                table: "PromotionUsages",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionUsages_TenantId_PromotionId",
                table: "PromotionUsages",
                columns: new[] { "TenantId", "PromotionId" });

            migrationBuilder.CreateIndex(
                name: "IX_PromotionUsages_TenantId_PromotionId_OrderId",
                table: "PromotionUsages",
                columns: new[] { "TenantId", "PromotionId", "OrderId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderDelivery_Claimable",
                table: "ProviderDeliveryOutbox",
                columns: new[] { "DeliveredAtUtc", "IsFailed", "IsDeadLettered", "NextRetryAtUtc", "ClaimedUntilUtc" },
                filter: "\"DeliveredAtUtc\" IS NULL AND \"IsFailed\" = false AND \"IsDeadLettered\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderDelivery_DeadLetter_Replay",
                table: "ProviderDeliveryOutbox",
                columns: new[] { "TenantId", "Provider", "IsDeadLettered", "FailureClass" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderDelivery_PayloadHash",
                table: "ProviderDeliveryOutbox",
                column: "PayloadHash");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderDelivery_Tenant_Provider_DeliveryKey",
                table: "ProviderDeliveryOutbox",
                columns: new[] { "TenantId", "Provider", "DeliveryKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderDeliveryReplayAudits_ReplayId",
                table: "ProviderDeliveryReplayAudits",
                column: "ReplayId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderDeliveryReplayAudits_Tenant_Message_Time",
                table: "ProviderDeliveryReplayAudits",
                columns: new[] { "TenantId", "MessageId", "ReplayedAtUtc" });

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
                name: "IX_PurchaseInvoiceItems_ProductId",
                table: "PurchaseInvoiceItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceItems_ProductVariantId",
                table: "PurchaseInvoiceItems",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceItems_PurchaseInvoiceId",
                table: "PurchaseInvoiceItems",
                column: "PurchaseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceItems_UnitId",
                table: "PurchaseInvoiceItems",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoicePayments_PurchaseInvoiceId",
                table: "PurchaseInvoicePayments",
                column: "PurchaseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_SupplierId",
                table: "PurchaseInvoices",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_WarehouseId",
                table: "PurchaseInvoices",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequestIdempotency_TenantId_Key_Operation",
                table: "PurchaseRequestIdempotency",
                columns: new[] { "TenantId", "Key", "Operation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequestIdempotency_TenantId_PurchaseInvoiceId",
                table: "PurchaseRequestIdempotency",
                columns: new[] { "TenantId", "PurchaseInvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnRequestIdempotency_TenantId_Key",
                table: "PurchaseReturnRequestIdempotency",
                columns: new[] { "TenantId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnRequestIdempotency_TenantId_ReturnId",
                table: "PurchaseReturnRequestIdempotency",
                columns: new[] { "TenantId", "ReturnId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnItems_ProductId",
                table: "ReturnItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnItems_ProductVariantId",
                table: "ReturnItems",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnItems_ReturnId",
                table: "ReturnItems",
                column: "ReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnItems_UnitId",
                table: "ReturnItems",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Returns_CustomerId",
                table: "Returns",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Returns_OriginalInvoiceId",
                table: "Returns",
                column: "OriginalInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Returns_SupplierId",
                table: "Returns",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_Returns_TenantId_OriginalPurchaseInvoiceId",
                table: "Returns",
                columns: new[] { "TenantId", "OriginalPurchaseInvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Returns_TenantId_ReturnNumber",
                table: "Returns",
                columns: new[] { "TenantId", "ReturnNumber" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Returns_WarehouseId",
                table: "Returns",
                column: "WarehouseId");

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

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceItems_ProductId",
                table: "SalesInvoiceItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceItems_ProductVariantId",
                table: "SalesInvoiceItems",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceItems_SalesInvoiceId_ProductId",
                table: "SalesInvoiceItems",
                columns: new[] { "SalesInvoiceId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceItems_SalesInvoiceId_ProductVariantId",
                table: "SalesInvoiceItems",
                columns: new[] { "SalesInvoiceId", "ProductVariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceItems_UnitId",
                table: "SalesInvoiceItems",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoicePayments_PaymentMethod_PaymentDate",
                table: "SalesInvoicePayments",
                columns: new[] { "PaymentMethod", "PaymentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoicePayments_SalesInvoiceId_PaymentDate",
                table: "SalesInvoicePayments",
                columns: new[] { "SalesInvoiceId", "PaymentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_CustomerId",
                table: "SalesInvoices",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_SalesRepresentativeId",
                table: "SalesInvoices",
                column: "SalesRepresentativeId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_TenantId_CreatedAt",
                table: "SalesInvoices",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_TenantId_CustomerId_InvoiceDate",
                table: "SalesInvoices",
                columns: new[] { "TenantId", "CustomerId", "InvoiceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_TenantId_InvoiceDate_Active",
                table: "SalesInvoices",
                columns: new[] { "TenantId", "InvoiceDate" },
                filter: "\"IsDeleted\" = false AND \"Status\" <> 2");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_TenantId_Status_InvoiceDate",
                table: "SalesInvoices",
                columns: new[] { "TenantId", "Status", "InvoiceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_TenantId_WarehouseId_InvoiceDate",
                table: "SalesInvoices",
                columns: new[] { "TenantId", "WarehouseId", "InvoiceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_WarehouseId",
                table: "SalesInvoices",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesRequestIdempotency_TenantId_Key_Operation",
                table: "SalesRequestIdempotency",
                columns: new[] { "TenantId", "Key", "Operation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesRequestIdempotency_TenantId_Operation_RequestHash_IsDeleted",
                table: "SalesRequestIdempotency",
                columns: new[] { "TenantId", "Operation", "RequestHash", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_SearchClicks_ProductId_ClickedAt",
                table: "SearchClicks",
                columns: new[] { "ProductId", "ClickedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SearchClicks_QueryText_ClickedAt",
                table: "SearchClicks",
                columns: new[] { "QueryText", "ClickedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SearchClicks_TenantId_ClickedAt",
                table: "SearchClicks",
                columns: new[] { "TenantId", "ClickedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SearchFilterEvents_FilterKey_OccurredAt",
                table: "SearchFilterEvents",
                columns: new[] { "FilterKey", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SearchFilterEvents_QueryText_OccurredAt",
                table: "SearchFilterEvents",
                columns: new[] { "QueryText", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SearchFilterEvents_TenantId_OccurredAt",
                table: "SearchFilterEvents",
                columns: new[] { "TenantId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SearchImpressions_ProductId_ImpressedAt",
                table: "SearchImpressions",
                columns: new[] { "ProductId", "ImpressedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SearchImpressions_QueryText_ImpressedAt",
                table: "SearchImpressions",
                columns: new[] { "QueryText", "ImpressedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SearchImpressions_TenantId_ImpressedAt",
                table: "SearchImpressions",
                columns: new[] { "TenantId", "ImpressedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SearchQueries_QueryText_SearchedAt",
                table: "SearchQueries",
                columns: new[] { "QueryText", "SearchedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SearchQueries_ResultCount_SearchedAt",
                table: "SearchQueries",
                columns: new[] { "ResultCount", "SearchedAt" });

            migrationBuilder.CreateIndex(
                name: "UQ_SearchSynonyms_Term",
                table: "SearchSynonyms",
                column: "Term",
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
                name: "IX_ServiceRequests_ServiceId",
                table: "ServiceRequests",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_TenantId_BuyerUserId_Status",
                table: "ServiceRequests",
                columns: new[] { "TenantId", "BuyerUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_TenantId_ProviderUserId_Status",
                table: "ServiceRequests",
                columns: new[] { "TenantId", "ProviderUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_ServiceRequests_Active_Service_Buyer",
                table: "ServiceRequests",
                columns: new[] { "TenantId", "ServiceId", "BuyerUserId" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE AND \"Status\" IN ('Pending', 'Accepted', 'CompletedByProvider')");

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
                name: "UQ_ShipmentCustomsSnapshotLine_OrderItem",
                table: "ShipmentCustomsSnapshotLines",
                columns: new[] { "ShipmentCustomsSnapshotId", "OrderItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentCustomsSnapshot_Order",
                table: "ShipmentCustomsSnapshots",
                columns: new[] { "TenantId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "UQ_ShipmentCustomsSnapshot_Shipment",
                table: "ShipmentCustomsSnapshots",
                columns: new[] { "TenantId", "ShipmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_CarrierCode_TrackingNumber",
                table: "Shipments",
                columns: new[] { "CarrierCode", "TrackingNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_DestinationAddressAdmissionEvidenceId",
                table: "Shipments",
                column: "DestinationAddressAdmissionEvidenceId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_OrderId",
                table: "Shipments",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_OriginAddressAdmissionEvidenceId",
                table: "Shipments",
                column: "OriginAddressAdmissionEvidenceId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_ShippingMethodId",
                table: "Shipments",
                column: "ShippingMethodId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_TenantId_CarrierCode_CarrierRequestId",
                table: "Shipments",
                columns: new[] { "TenantId", "CarrierCode", "CarrierRequestId" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"CarrierRequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_TenantId_CarrierCode_ExternalReferenceId",
                table: "Shipments",
                columns: new[] { "TenantId", "CarrierCode", "ExternalReferenceId" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"ExternalReferenceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_TenantId_CarrierCode_Status",
                table: "Shipments",
                columns: new[] { "TenantId", "CarrierCode", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_TenantId_CarrierGeoMappingRelease",
                table: "Shipments",
                columns: new[] { "TenantId", "CarrierGeographicMappingReleaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_TenantId_DestinationGeoRelease_Country",
                table: "Shipments",
                columns: new[] { "TenantId", "DestinationGeographicCatalogReleaseId", "DestinationCountryPlaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_TenantId_OrderId",
                table: "Shipments",
                columns: new[] { "TenantId", "OrderId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_TenantId_OriginGeoRelease_Country",
                table: "Shipments",
                columns: new[] { "TenantId", "OriginGeographicCatalogReleaseId", "OriginCountryPlaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_TenantId_Status_LastCarrierSyncAtUtc",
                table: "Shipments",
                columns: new[] { "TenantId", "Status", "LastCarrierSyncAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ShippingWebhookLogs_TenantId",
                table: "ShippingWebhookLogs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingWebhookLogs_TenantId_CarrierCode_EventId",
                table: "ShippingWebhookLogs",
                columns: new[] { "TenantId", "CarrierCode", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShippingWebhookLogs_TenantId_CarrierCode_PayloadHash",
                table: "ShippingWebhookLogs",
                columns: new[] { "TenantId", "CarrierCode", "PayloadHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShippingWebhookLogs_TenantId_TrackingNumber_CreatedAt",
                table: "ShippingWebhookLogs",
                columns: new[] { "TenantId", "TrackingNumber", "CreatedAt" },
                filter: "\"TrackingNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SmartOffers_TenantId_Name",
                table: "SmartOffers",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SmartOffers_TenantId_Status_Priority",
                table: "SmartOffers",
                columns: new[] { "TenantId", "Status", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_SmartOffers_TenantId_TriggerType_PagesScope",
                table: "SmartOffers",
                columns: new[] { "TenantId", "TriggerType", "PagesScope" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_CreatedAt",
                table: "StockMovements",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ProductId",
                table: "StockMovements",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ProductVariantId",
                table: "StockMovements",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ReferenceLineId_InventoryEventType",
                table: "StockMovements",
                columns: new[] { "ReferenceLineId", "InventoryEventType" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_TenantId_InventoryEventId",
                table: "StockMovements",
                columns: new[] { "TenantId", "InventoryEventId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ToWarehouseId",
                table: "StockMovements",
                column: "ToWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_UnitId",
                table: "StockMovements",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_WarehouseId",
                table: "StockMovements",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "UX_StockMovements_TenantId_IdempotencyKey",
                table: "StockMovements",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StockSyncOutbox_GlobalPendingOrdering",
                table: "StockSyncOutboxMessages",
                columns: new[] { "EventVersion", "StockUpdatedAtUtc", "OccurredAtUtc", "CreatedAt", "Id" },
                filter: "\"PublishedAtUtc\" IS NULL AND \"IsFailed\" = false AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_StockSyncOutbox_OccurredAtUtc",
                table: "StockSyncOutboxMessages",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StockSyncOutbox_ProcessingStatus",
                table: "StockSyncOutboxMessages",
                columns: new[] { "PublishedAtUtc", "IsFailed", "NextRetryAtUtc", "ClaimedUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StockSyncOutbox_TenantId_VariantId",
                table: "StockSyncOutboxMessages",
                columns: new[] { "TenantId", "VariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockSyncOutbox_VariantOrdering",
                table: "StockSyncOutboxMessages",
                columns: new[] { "TenantId", "VariantId", "WarehouseId", "PublishedAtUtc", "EventVersion", "StockUpdatedAtUtc", "OccurredAtUtc", "CreatedAt", "Id" },
                filter: "\"PublishedAtUtc\" IS NULL AND \"IsFailed\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_StockSyncOutbox_Warehouse_EventId",
                table: "StockSyncOutboxMessages",
                columns: new[] { "TenantId", "WarehouseId", "EventId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_StockSyncOutboxMessages_Unpublished_EventOrdering",
                table: "StockSyncOutboxMessages",
                columns: new[] { "TenantId", "VariantId", "WarehouseId", "EventVersion", "StockUpdatedAtUtc", "OccurredAtUtc", "CreatedAt", "Id" },
                filter: "\"PublishedAtUtc\" IS NULL AND \"IsFailed\" = false AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UX_StockSyncOutboxMessages_Tenant_Variant_Warehouse_EventVersion",
                table: "StockSyncOutboxMessages",
                columns: new[] { "TenantId", "VariantId", "WarehouseId", "EventVersion" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferItems_ProductId",
                table: "StockTransferItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferItems_ProductVariantId",
                table: "StockTransferItems",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferItems_StockTransferId",
                table: "StockTransferItems",
                column: "StockTransferId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferRequestIdempotency_TenantId_Key_Operation",
                table: "StockTransferRequestIdempotency",
                columns: new[] { "TenantId", "Key", "Operation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferRequestIdempotency_TenantId_StockTransferId",
                table: "StockTransferRequestIdempotency",
                columns: new[] { "TenantId", "StockTransferId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_ApprovedById",
                table: "StockTransfers",
                column: "ApprovedById");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_CompletedById",
                table: "StockTransfers",
                column: "CompletedById");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_FromWarehouseId",
                table: "StockTransfers",
                column: "FromWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_RequestedById",
                table: "StockTransfers",
                column: "RequestedById");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_TenantId_TransferNumber",
                table: "StockTransfers",
                columns: new[] { "TenantId", "TransferNumber" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_ToWarehouseId",
                table: "StockTransfers",
                column: "ToWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreConfigAuditEntries_ActionType",
                table: "StoreConfigAuditEntries",
                column: "ActionType");

            migrationBuilder.CreateIndex(
                name: "IX_StoreConfigAuditEntries_TenantId",
                table: "StoreConfigAuditEntries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreConfigAuditEntries_TenantId_TimestampUtc",
                table: "StoreConfigAuditEntries",
                columns: new[] { "TenantId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreConfigAuditEntries_TimestampUtc",
                table: "StoreConfigAuditEntries",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StoreConfigs_TenantId",
                table: "StoreConfigs",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreConfigs_ThemeId",
                table: "StoreConfigs",
                column: "ThemeId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthProviderSettings_TenantId_Provider",
                table: "StorefrontAuthProviderSettings",
                columns: new[] { "TenantId", "Provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCartCouponStates_TenantId_Owner",
                table: "StorefrontCartCouponStates",
                columns: new[] { "TenantId", "OwnerType", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "UX_StorefrontCartCouponStates_TenantId_CartId",
                table: "StorefrontCartCouponStates",
                columns: new[] { "TenantId", "CartId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontFooterLinks_SectionId_SortOrder",
                table: "StorefrontFooterLinks",
                columns: new[] { "SectionId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontFooterSections_TenantId_SortOrder",
                table: "StorefrontFooterSections",
                columns: new[] { "TenantId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontSocialLinks_TenantId_SortOrder",
                table: "StorefrontSocialLinks",
                columns: new[] { "TenantId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "UQ_StorefrontSocialLinks_Global_Platform",
                table: "StorefrontSocialLinks",
                column: "Platform",
                unique: true,
                filter: "\"TenantId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "UQ_StorefrontSocialLinks_TenantId_Platform",
                table: "StorefrontSocialLinks",
                columns: new[] { "TenantId", "Platform" },
                unique: true,
                filter: "\"TenantId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StripeWebhookReplayRecords_Tenant_EvidenceHash",
                table: "StripeWebhookReplayRecords",
                columns: new[] { "TenantId", "EvidenceHash" });

            migrationBuilder.CreateIndex(
                name: "IX_StripeWebhookReplayRecords_Tenant_Provider_Event",
                table: "StripeWebhookReplayRecords",
                columns: new[] { "TenantId", "Provider", "ProviderEventId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_StripeWebhookReplayRecords_Tenant_ProviderReference",
                table: "StripeWebhookReplayRecords",
                columns: new[] { "TenantId", "ProviderReference" });

            migrationBuilder.CreateIndex(
                name: "IX_SuperAdmins_Email",
                table: "SuperAdmins",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SuperAdmins_IsSystem",
                table: "SuperAdmins",
                column: "IsSystem",
                unique: true,
                filter: "\"IsSystem\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_PurchaseInvoiceId",
                table: "SupplierPayments",
                column: "PurchaseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_SupplierId",
                table: "SupplierPayments",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_TenantId_IdempotencyKey",
                table: "SupplierPayments",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_TenantId_SupplierId_PaymentDate",
                table: "SupplierPayments",
                columns: new[] { "TenantId", "SupplierId", "PaymentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_Email",
                table: "Suppliers",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_Phone",
                table: "Suppliers",
                column: "Phone");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_TaxNumber",
                table: "Suppliers",
                column: "TaxNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_TenantId",
                table: "Suppliers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierTransactions_OpeningBalance_Once",
                table: "SupplierTransactions",
                columns: new[] { "TenantId", "SupplierId", "TransactionType" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"TransactionType\" = 11");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierTransactions_SupplierId",
                table: "SupplierTransactions",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "UQ_SystemSchemaVersions_Component",
                table: "SystemSchemaVersions",
                column: "Component",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_TenantId_Key",
                table: "SystemSettings",
                columns: new[] { "TenantId", "Key" },
                unique: true,
                filter: "\"TenantId\" IS NOT NULL AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_TenantAnalyticsConfigurations_TenantId",
                table: "TenantAnalyticsConfigurations",
                column: "TenantId",
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

            migrationBuilder.CreateIndex(
                name: "IX_TenantPaymentMethods_TenantId",
                table: "TenantPaymentMethods",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantPaymentMethods_TenantId_Code",
                table: "TenantPaymentMethods",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantPaymentMethods_TenantId_IsEnabled",
                table: "TenantPaymentMethods",
                columns: new[] { "TenantId", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Currency",
                table: "Tenants",
                column: "Currency");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_IsActive",
                table: "Tenants",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Slug",
                table: "Tenants",
                column: "Slug",
                unique: true,
                filter: "\"Slug\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Status",
                table: "Tenants",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ThemePresets_Key",
                table: "ThemePresets",
                column: "Key",
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_TreasuryTransactions_CreatedByUserId",
                table: "TreasuryTransactions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserGovernanceAuditLogs_AdminId",
                table: "UserGovernanceAuditLogs",
                column: "AdminId");

            migrationBuilder.CreateIndex(
                name: "IX_UserGovernanceAuditLogs_TargetRole_TargetUserId_TimestampUtc",
                table: "UserGovernanceAuditLogs",
                columns: new[] { "TargetRole", "TargetUserId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserGovernanceBlacklistEntries_CreatedAtUtc",
                table: "UserGovernanceBlacklistEntries",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UserGovernanceBlacklistEntries_IsActive",
                table: "UserGovernanceBlacklistEntries",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_UserGovernanceBlacklistEntries_Type_ValueNormalized",
                table: "UserGovernanceBlacklistEntries",
                columns: new[] { "Type", "ValueNormalized" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserGovernanceProfiles_RiskLevel",
                table: "UserGovernanceProfiles",
                column: "RiskLevel");

            migrationBuilder.CreateIndex(
                name: "IX_UserGovernanceProfiles_Role_SubjectId",
                table: "UserGovernanceProfiles",
                columns: new[] { "Role", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserGovernanceProfiles_Status",
                table: "UserGovernanceProfiles",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId",
                table: "Users",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VisaTransactions_CreatedByUserId",
                table: "VisaTransactions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseStocks_ProductId",
                table: "WarehouseStocks",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseStocks_ProductVariantId",
                table: "WarehouseStocks",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseStocks_TenantId_Quantity",
                table: "WarehouseStocks",
                columns: new[] { "TenantId", "Quantity" });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseStocks_TenantId_WarehouseId_Quantity",
                table: "WarehouseStocks",
                columns: new[] { "TenantId", "WarehouseId", "Quantity" });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseStocks_WarehouseId_ProductId_ProductVariantId",
                table: "WarehouseStocks",
                columns: new[] { "WarehouseId", "ProductId", "ProductVariantId" },
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

            migrationBuilder.CreateIndex(
                name: "uq_whatsapp_template_tenant_type_language",
                table: "whatsapp_message_templates",
                columns: new[] { "TenantId", "Type", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppRateLimits_TenantId",
                table: "WhatsAppRateLimits",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppRateLimits_TenantId_PhoneNumber",
                table: "WhatsAppRateLimits",
                columns: new[] { "TenantId", "PhoneNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerAddresses_CustomerAddressVersions_CurrentVersionId",
                table: "CustomerAddresses",
                column: "CurrentVersionId",
                principalTable: "CustomerAddressVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerAddresses_CustomerAddressVersions_CurrentVersionId",
                table: "CustomerAddresses");

            migrationBuilder.DropTable(
                name: "AbandonedCartRecoveryLogs");

            migrationBuilder.DropTable(
                name: "AccountingOutboxMessages");

            migrationBuilder.DropTable(
                name: "AccountLedgers");

            migrationBuilder.DropTable(
                name: "AddressAdmissionEvidence");

            migrationBuilder.DropTable(
                name: "AddressNormalizationRules");

            migrationBuilder.DropTable(
                name: "AffiliateBalanceLedger");

            migrationBuilder.DropTable(
                name: "AffiliateCommissionLedger");

            migrationBuilder.DropTable(
                name: "AffiliateEmailVerificationTokens");

            migrationBuilder.DropTable(
                name: "AffiliatePasswordResetTokens");

            migrationBuilder.DropTable(
                name: "AffiliatePayoutRequests");

            migrationBuilder.DropTable(
                name: "AffiliatePayoutTransferAllocations");

            migrationBuilder.DropTable(
                name: "AffiliateProductPolicies");

            migrationBuilder.DropTable(
                name: "AffiliatePromotionLinks");

            migrationBuilder.DropTable(
                name: "AffiliateRefreshTokenReplayEntries");

            migrationBuilder.DropTable(
                name: "AnalyticsAlertRules");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "BankTransactions");

            migrationBuilder.DropTable(
                name: "CampaignCosts");

            migrationBuilder.DropTable(
                name: "CanonicalIdentityLinks");

            migrationBuilder.DropTable(
                name: "CarrierAccountPolicies");

            migrationBuilder.DropTable(
                name: "CarrierAccounts");

            migrationBuilder.DropTable(
                name: "CarrierAdministrationAudit");

            migrationBuilder.DropTable(
                name: "CarrierConfigurations");

            migrationBuilder.DropTable(
                name: "CarrierConnectionTestRecords");

            migrationBuilder.DropTable(
                name: "CarrierCredentialReferences");

            migrationBuilder.DropTable(
                name: "CarrierGeographicMappingPublications");

            migrationBuilder.DropTable(
                name: "CarrierGeographicMappings");

            migrationBuilder.DropTable(
                name: "CarrierGeographicOverrides");

            migrationBuilder.DropTable(
                name: "CarrierGeographicSyncCandidates");

            migrationBuilder.DropTable(
                name: "CarrierServiceLevelEnablements");

            migrationBuilder.DropTable(
                name: "CarrierWarehouseAssignments");

            migrationBuilder.DropTable(
                name: "CashboxTransactions");

            migrationBuilder.DropTable(
                name: "CheckoutSessions");

            migrationBuilder.DropTable(
                name: "CmsPages");

            migrationBuilder.DropTable(
                name: "CommerceAccountingAudit");

            migrationBuilder.DropTable(
                name: "CommerceHubCarts");

            migrationBuilder.DropTable(
                name: "CommerceHubCategoryAttributes");

            migrationBuilder.DropTable(
                name: "CommerceHubCheckoutIdempotency");

            migrationBuilder.DropTable(
                name: "CommerceHubCustomerEmailVerificationTokens");

            migrationBuilder.DropTable(
                name: "CommerceHubCustomerIdentityEvents");

            migrationBuilder.DropTable(
                name: "CommerceHubCustomerPasswordResetTokens");

            migrationBuilder.DropTable(
                name: "CommerceHubCustomerPayoutDetails");

            migrationBuilder.DropTable(
                name: "CommerceHubDailyFinancialAggregates");

            migrationBuilder.DropTable(
                name: "CommerceHubFinancialAuditLogs");

            migrationBuilder.DropTable(
                name: "CommerceHubFinancialGuardLocks");

            migrationBuilder.DropTable(
                name: "CommerceHubFinancialOperationAudits");

            migrationBuilder.DropTable(
                name: "CommerceHubFinancialReconciliationReports");

            migrationBuilder.DropTable(
                name: "CommerceHubFinancialTransactions");

            migrationBuilder.DropTable(
                name: "CommerceHubListings");

            migrationBuilder.DropTable(
                name: "CommerceHubMerchantLifecycleLedgerEntries");

            migrationBuilder.DropTable(
                name: "CommerceHubPaymentAttempts");

            migrationBuilder.DropTable(
                name: "CommerceHubPaymentCompensationOutboxMessages");

            migrationBuilder.DropTable(
                name: "CommerceHubPaymentItems");

            migrationBuilder.DropTable(
                name: "CommerceHubPaymentLogs");

            migrationBuilder.DropTable(
                name: "CommerceHubPayoutItems");

            migrationBuilder.DropTable(
                name: "CommerceHubRecoveryOperationLedgerEntries");

            migrationBuilder.DropTable(
                name: "CommerceHubRefundEvidenceMedia");

            migrationBuilder.DropTable(
                name: "CommerceHubRefundExecutions");

            migrationBuilder.DropTable(
                name: "CommerceHubReturnRequests");

            migrationBuilder.DropTable(
                name: "CommerceHubSubOrderStatusHistory");

            migrationBuilder.DropTable(
                name: "CommerceHubUnresolvedPaymentWebhooks");

            migrationBuilder.DropTable(
                name: "CommerceHubVendorPaymentAuditLogs");

            migrationBuilder.DropTable(
                name: "CommerceHubVendorPaymentProofs");

            migrationBuilder.DropTable(
                name: "CommerceHubVendorSettlements");

            migrationBuilder.DropTable(
                name: "CommissionRules");

            migrationBuilder.DropTable(
                name: "CouponReservations");

            migrationBuilder.DropTable(
                name: "CouponUsages");

            migrationBuilder.DropTable(
                name: "CustomerTransactions");

            migrationBuilder.DropTable(
                name: "CustomsAdministrationAudit");

            migrationBuilder.DropTable(
                name: "CustomsAdmissionAudit");

            migrationBuilder.DropTable(
                name: "CustomsEvidenceApprovals");

            migrationBuilder.DropTable(
                name: "CustomsEvidenceFingerprints");

            migrationBuilder.DropTable(
                name: "CustomsEvidenceHistory");

            migrationBuilder.DropTable(
                name: "CustomsEvidencePublications");

            migrationBuilder.DropTable(
                name: "CustomsEvidenceVersions");

            migrationBuilder.DropTable(
                name: "CustomsPublicationLocks");

            migrationBuilder.DropTable(
                name: "EmailVerificationTokens");

            migrationBuilder.DropTable(
                name: "Expenses");

            migrationBuilder.DropTable(
                name: "FawryWebhookReplayRecords");

            migrationBuilder.DropTable(
                name: "FinancialTransactions");

            migrationBuilder.DropTable(
                name: "GeographicAudit");

            migrationBuilder.DropTable(
                name: "GeographicCatalogPublications");

            migrationBuilder.DropTable(
                name: "GeographicCatalogReleaseEdges");

            migrationBuilder.DropTable(
                name: "GeographicCatalogReleasePlaces");

            migrationBuilder.DropTable(
                name: "GeographicCountryProfiles");

            migrationBuilder.DropTable(
                name: "GeographicPlaceAliases");

            migrationBuilder.DropTable(
                name: "GeographicPlaceLevels");

            migrationBuilder.DropTable(
                name: "GeographicPlaceNames");

            migrationBuilder.DropTable(
                name: "GlobalPhoneReputation");

            migrationBuilder.DropTable(
                name: "HomepageSections");

            migrationBuilder.DropTable(
                name: "InventoryEventInbox");

            migrationBuilder.DropTable(
                name: "InventoryReservations");

            migrationBuilder.DropTable(
                name: "LedgerJournalNumberSequences");

            migrationBuilder.DropTable(
                name: "LedgerLines");

            migrationBuilder.DropTable(
                name: "MarketingReviewEvidences");

            migrationBuilder.DropTable(
                name: "MarketplaceCategoryTranslations");

            migrationBuilder.DropTable(
                name: "MarketplaceCmsPages");

            migrationBuilder.DropTable(
                name: "MarketplaceEventOutboxDeadLetterArchives");

            migrationBuilder.DropTable(
                name: "MarketplaceEventOutboxMessages");

            migrationBuilder.DropTable(
                name: "MarketplaceFooterLinks");

            migrationBuilder.DropTable(
                name: "MarketplaceProjectionOutboxMessages");

            migrationBuilder.DropTable(
                name: "MarketplaceSocialLinks");

            migrationBuilder.DropTable(
                name: "OnboardingChecklists");

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
                name: "OrderRefundLines");

            migrationBuilder.DropTable(
                name: "OrderRequestIdempotency");

            migrationBuilder.DropTable(
                name: "OutboxMessage");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "PartyLedgerAdjustments");

            migrationBuilder.DropTable(
                name: "PaymentCancellationOutboxMessages");

            migrationBuilder.DropTable(
                name: "PaymentMethods");

            migrationBuilder.DropTable(
                name: "PaymentSettlementOrders");

            migrationBuilder.DropTable(
                name: "PaymentWebhookLogs");

            migrationBuilder.DropTable(
                name: "PaymobWebhookReplayRecords");

            migrationBuilder.DropTable(
                name: "PhoneRiskProfiles");

            migrationBuilder.DropTable(
                name: "PhoneSpamReports");

            migrationBuilder.DropTable(
                name: "PixelTrackingConfigs");

            migrationBuilder.DropTable(
                name: "PlaceDeprecations");

            migrationBuilder.DropTable(
                name: "PlaceMergeHistory");

            migrationBuilder.DropTable(
                name: "ProcessedEvents");

            migrationBuilder.DropTable(
                name: "ProcessedTrustEvent");

            migrationBuilder.DropTable(
                name: "ProductPixelOverrides");

            migrationBuilder.DropTable(
                name: "ProductRequestIdempotency");

            migrationBuilder.DropTable(
                name: "ProductReviews");

            migrationBuilder.DropTable(
                name: "ProductUnits");

            migrationBuilder.DropTable(
                name: "ProductVariantAttributes");

            migrationBuilder.DropTable(
                name: "ProductVariantStocks");

            migrationBuilder.DropTable(
                name: "PromoCodeUsages");

            migrationBuilder.DropTable(
                name: "PromotionUsages");

            migrationBuilder.DropTable(
                name: "ProviderDeliveryOutbox");

            migrationBuilder.DropTable(
                name: "ProviderDeliveryReplayAudits");

            migrationBuilder.DropTable(
                name: "ProviderTrustAggregates");

            migrationBuilder.DropTable(
                name: "PurchaseInvoiceItems");

            migrationBuilder.DropTable(
                name: "PurchaseInvoicePayments");

            migrationBuilder.DropTable(
                name: "PurchaseRequestIdempotency");

            migrationBuilder.DropTable(
                name: "PurchaseReturnRequestIdempotency");

            migrationBuilder.DropTable(
                name: "ReturnItems");

            migrationBuilder.DropTable(
                name: "RevokedCustomerAccessTokens");

            migrationBuilder.DropTable(
                name: "RevokedStorefrontAccessTokens");

            migrationBuilder.DropTable(
                name: "SalesInvoiceItems");

            migrationBuilder.DropTable(
                name: "SalesInvoicePayments");

            migrationBuilder.DropTable(
                name: "SalesRequestIdempotency");

            migrationBuilder.DropTable(
                name: "SearchClicks");

            migrationBuilder.DropTable(
                name: "SearchFilterEvents");

            migrationBuilder.DropTable(
                name: "SearchImpressions");

            migrationBuilder.DropTable(
                name: "SearchQueries");

            migrationBuilder.DropTable(
                name: "SearchSynonyms");

            migrationBuilder.DropTable(
                name: "ServiceRequestMessages");

            migrationBuilder.DropTable(
                name: "ShipmentCustomsSnapshotLines");

            migrationBuilder.DropTable(
                name: "Shipments");

            migrationBuilder.DropTable(
                name: "ShippingWebhookLogs");

            migrationBuilder.DropTable(
                name: "SmartOffers");

            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS trg_stock_movements_append_only ON ""StockMovements"";");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS pos_reject_stock_movement_mutation();");

            migrationBuilder.DropTable(
                name: "StockMovements");

            migrationBuilder.DropTable(
                name: "StockSyncOutboxMessages");

            migrationBuilder.DropTable(
                name: "StockTransferItems");

            migrationBuilder.DropTable(
                name: "StockTransferRequestIdempotency");

            migrationBuilder.DropTable(
                name: "StoreConfigAuditEntries");

            migrationBuilder.DropTable(
                name: "StoreConfigs");

            migrationBuilder.DropTable(
                name: "StorefrontAuthProviderSettings");

            migrationBuilder.DropTable(
                name: "StorefrontCartCouponStates");

            migrationBuilder.DropTable(
                name: "StorefrontFooterLinks");

            migrationBuilder.DropTable(
                name: "StorefrontSocialLinks");

            migrationBuilder.DropTable(
                name: "StripeWebhookReplayRecords");

            migrationBuilder.DropTable(
                name: "SuperAdmins");

            migrationBuilder.DropTable(
                name: "SupplierPayments");

            migrationBuilder.DropTable(
                name: "SupplierTransactions");

            migrationBuilder.DropTable(
                name: "SystemSchemaVersions");

            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.DropTable(
                name: "TenantAnalyticsConfigurations");

            migrationBuilder.DropTable(
                name: "TenantAutomationSettings");

            migrationBuilder.DropTable(
                name: "TenantPaymentMethods");

            migrationBuilder.DropTable(
                name: "ThemePresets");

            migrationBuilder.DropTable(
                name: "TreasuryAccounts");

            migrationBuilder.DropTable(
                name: "TreasuryTransactions");

            migrationBuilder.DropTable(
                name: "UserGovernanceAuditLogs");

            migrationBuilder.DropTable(
                name: "UserGovernanceBlacklistEntries");

            migrationBuilder.DropTable(
                name: "UserGovernanceProfiles");

            migrationBuilder.DropTable(
                name: "VisaTransactions");

            migrationBuilder.DropTable(
                name: "WarehouseStocks");

            migrationBuilder.DropTable(
                name: "whatsapp_accounts");

            migrationBuilder.DropTable(
                name: "whatsapp_message_templates");

            migrationBuilder.DropTable(
                name: "WhatsAppRateLimits");

            migrationBuilder.DropTable(
                name: "AbandonedCarts");

            migrationBuilder.DropTable(
                name: "AffiliateAttributions");

            migrationBuilder.DropTable(
                name: "AffiliatePayoutTransferEvidence");

            migrationBuilder.DropTable(
                name: "AffiliateCategories");

            migrationBuilder.DropTable(
                name: "AffiliateUsers");

            migrationBuilder.DropTable(
                name: "CarrierGeographicMappingGroups");

            migrationBuilder.DropTable(
                name: "CarrierGeographicSyncRuns");

            migrationBuilder.DropTable(
                name: "CommerceHubCategories");

            migrationBuilder.DropTable(
                name: "CommerceHubVendorEarnings");

            migrationBuilder.DropTable(
                name: "CommerceHubRefundRequests");

            migrationBuilder.DropTable(
                name: "CommerceHubVendorPaymentRequests");

            migrationBuilder.DropTable(
                name: "CustomsEvidence");

            migrationBuilder.DropTable(
                name: "Banks");

            migrationBuilder.DropTable(
                name: "Cashboxes");

            migrationBuilder.DropTable(
                name: "ExpenseCategories");

            migrationBuilder.DropTable(
                name: "FinancialAccounts");

            migrationBuilder.DropTable(
                name: "GeographicPlaceHierarchyEdges");

            migrationBuilder.DropTable(
                name: "LedgerJournalEntries");

            migrationBuilder.DropTable(
                name: "ServiceReviews");

            migrationBuilder.DropTable(
                name: "MarketplaceFooterSections");

            migrationBuilder.DropTable(
                name: "OrderItems");

            migrationBuilder.DropTable(
                name: "OrderRefunds");

            migrationBuilder.DropTable(
                name: "InboxState");

            migrationBuilder.DropTable(
                name: "OutboxState");

            migrationBuilder.DropTable(
                name: "PaymentIntents");

            migrationBuilder.DropTable(
                name: "PromoCodes");

            migrationBuilder.DropTable(
                name: "Returns");

            migrationBuilder.DropTable(
                name: "ShipmentCustomsSnapshots");

            migrationBuilder.DropTable(
                name: "StockTransfers");

            migrationBuilder.DropTable(
                name: "StorefrontFooterSections");

            migrationBuilder.DropTable(
                name: "LedgerAccounts");

            migrationBuilder.DropTable(
                name: "AffiliateAccounts");

            migrationBuilder.DropTable(
                name: "CarrierGeographicMappingReleases");

            migrationBuilder.DropTable(
                name: "CommerceHubPayouts");

            migrationBuilder.DropTable(
                name: "CommerceHubRefunds");

            migrationBuilder.DropTable(
                name: "CommerceHubSubOrderItems");

            migrationBuilder.DropTable(
                name: "CommerceHubVendorPaymentMethods");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropTable(
                name: "GeographicPlaces");

            migrationBuilder.DropTable(
                name: "ServiceRequests");

            migrationBuilder.DropTable(
                name: "ProductVariants");

            migrationBuilder.DropTable(
                name: "Units");

            migrationBuilder.DropTable(
                name: "PaymentSettlementBatches");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "PurchaseInvoices");

            migrationBuilder.DropTable(
                name: "SalesInvoices");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "CarrierProviders");

            migrationBuilder.DropTable(
                name: "CommerceHubVendorPayoutMethods");

            migrationBuilder.DropTable(
                name: "CommerceHubPayments");

            migrationBuilder.DropTable(
                name: "CommerceHubSubOrders");

            migrationBuilder.DropTable(
                name: "Currencies");

            migrationBuilder.DropTable(
                name: "GeographicCatalogReleases");

            migrationBuilder.DropTable(
                name: "Services");

            migrationBuilder.DropTable(
                name: "ProductMedia");

            migrationBuilder.DropTable(
                name: "Coupons");

            migrationBuilder.DropTable(
                name: "OnlineCustomers");

            migrationBuilder.DropTable(
                name: "Promotions");

            migrationBuilder.DropTable(
                name: "ShippingMethods");

            migrationBuilder.DropTable(
                name: "Suppliers");

            migrationBuilder.DropTable(
                name: "SalesRepresentatives");

            migrationBuilder.DropTable(
                name: "Warehouses");

            migrationBuilder.DropTable(
                name: "CommerceHubMarketplaceOrders");

            migrationBuilder.DropTable(
                name: "MarketplaceProfiles");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "CommerceHubCustomers");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "CustomerAddressVersions");

            migrationBuilder.DropTable(
                name: "CustomerAddresses");

            migrationBuilder.DropTable(
                name: "Customers");
        }
    }
}
