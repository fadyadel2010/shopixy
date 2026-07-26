using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S66_CommerceHubUnresolvedPaymentWebhooksUnfilteredUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_StockSyncOutbox_VariantOrdering"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_StockSyncOutbox_VariantOrderingBlockers"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_MarketplaceEventOutbox_EntityOrdering"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_MarketplaceEventOutbox_EntityOrderingBlockers"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_CommerceHubVendorPaymentRequests_PaymentMethodId"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""UX_CommerceHubPayouts_Completed_BankTransactionId"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""UX_CommerceHubPayouts_Completed_ExternalReferenceId"";");
            migrationBuilder.Sql(@"ALTER TABLE ""CommerceHubPayouts"" DROP CONSTRAINT IF EXISTS ""CK_CommerceHubPayouts_NonManual_Completed_Evidence"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_CommerceHubCheckoutIdempotency_TenantId_OnlineCustomerId"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_CommerceHubCarts_TenantId_OnlineCustomerId"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""UQ_AffiliateProductPolicies_Tenant_Product"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_AccountingOutbox_AggregateVersionBlockers"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_AccountingOutbox_AggregateVersionOrdering"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_OutboxMessages_CriticalAggregateOrdering"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Orders_TenantId_FinancialPostingStatus_CreatedAt"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_MarketplaceEventOutbox_AggregateOrdering"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_MarketplaceEventOutbox_AggregateOrderingBlockers"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_MarketplaceEventOutbox_TenantId_AggregateRootId"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""UX_CommerceHubVendorSettlements_ExternalReference"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""UX_CommerceHubVendorSettlements_Vendor_IdempotencyKey"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""UX_CommerceHubVendorPaymentRequests_Live_Method_TransferReference"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""UX_CommerceHubVendorPaymentProofs_ContentHash"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""UX_CommerceHubPayouts_ExternalTransferIdempotencyKey"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""UX_CommerceHubPayouts_Live_BankTransactionId"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""UX_CommerceHubPayouts_Live_ExternalReferenceId"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_CommerceHubCheckoutIdempotency_TenantId_CommerceHubCustomerId"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_CommerceHubUnresolvedPaymentWebhooks_OrderId"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_CommerceHubUnresolvedPaymentWebhooks_Tenant_Status"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""UX_CommerceHubUnresolvedPaymentWebhooks_Provider_Event"";");
            migrationBuilder.Sql(@"ALTER TABLE ""StockSyncOutboxMessages"" DROP CONSTRAINT IF EXISTS ""CK_StockSyncOutbox_EventVersion_Positive"";");
            migrationBuilder.Sql(@"ALTER TABLE ""OutboxMessages"" DROP CONSTRAINT IF EXISTS ""CK_OutboxMessages_CriticalEvents_AggregateId_Required"";");
            migrationBuilder.Sql(@"ALTER TABLE ""OutboxMessages"" DROP CONSTRAINT IF EXISTS ""CK_OutboxMessages_CriticalEvents_EventVersion_Required"";");
            migrationBuilder.Sql(@"ALTER TABLE ""OutboxMessages"" DROP CONSTRAINT IF EXISTS ""CK_OutboxMessages_CriticalEvents_TenantId_Required"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Orders"" DROP CONSTRAINT IF EXISTS ""CK_Orders_CustomerAuthorityBoundary"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Orders"" DROP CONSTRAINT IF EXISTS ""CK_Orders_FinancialPostingStatus_Valid"";");
            migrationBuilder.Sql(@"ALTER TABLE ""OrderItems"" DROP CONSTRAINT IF EXISTS ""CK_OrderItems_AffiliateCommissionSnapshot_Valid"";");
            migrationBuilder.Sql(@"ALTER TABLE ""MarketplaceEventOutboxMessages"" DROP CONSTRAINT IF EXISTS ""CK_MarketplaceEventOutbox_AggregateRootId_NotEmpty"";");
            migrationBuilder.Sql(@"ALTER TABLE ""MarketplaceEventOutboxMessages"" DROP CONSTRAINT IF EXISTS ""CK_MarketplaceEventOutbox_TenantId_Required"";");
            migrationBuilder.Sql(@"ALTER TABLE ""MarketplaceEventOutboxMessages"" DROP CONSTRAINT IF EXISTS ""CK_MarketplaceEventOutbox_Version_Positive"";");
            migrationBuilder.Sql(@"ALTER TABLE ""CommerceHubVendorSettlements"" DROP CONSTRAINT IF EXISTS ""CK_CommerceHubVendorSettlements_ExternalReference_NotBlank"";");
            migrationBuilder.Sql(@"ALTER TABLE ""CommerceHubVendorSettlements"" DROP CONSTRAINT IF EXISTS ""CK_CommerceHubVendorSettlements_IdempotencyKey_NotBlank"";");
            migrationBuilder.Sql(@"ALTER TABLE ""CommerceHubVendorPaymentRequests"" DROP CONSTRAINT IF EXISTS ""CK_CommerceHubVendorPaymentRequests_Approved_TransferReference"";");
            migrationBuilder.Sql(@"ALTER TABLE ""CommerceHubVendorEarnings"" DROP CONSTRAINT IF EXISTS ""CK_CommerceHubVendorEarnings_InPayout_HasPayout"";");
            migrationBuilder.Sql(@"ALTER TABLE ""CommerceHubVendorEarnings"" DROP CONSTRAINT IF EXISTS ""CK_CommerceHubVendorEarnings_PaidOut_HasPaidAmount"";");
            migrationBuilder.Sql(@"ALTER TABLE ""CommerceHubVendorEarnings"" DROP CONSTRAINT IF EXISTS ""CK_CommerceHubVendorEarnings_PayoutAmounts_NonNegative"";");
            migrationBuilder.Sql(@"ALTER TABLE ""CommerceHubVendorEarnings"" DROP CONSTRAINT IF EXISTS ""CK_CommerceHubVendorEarnings_PayoutAmounts_Within_Positive_Ear~"";");
            migrationBuilder.Sql(@"ALTER TABLE ""CommerceHubPayouts"" DROP CONSTRAINT IF EXISTS ""CK_CommerceHubPayouts_Completed_Evidence"";");
            migrationBuilder.Sql(@"ALTER TABLE ""CommerceHubPayouts"" DROP CONSTRAINT IF EXISTS ""CK_CommerceHubPayouts_Processing_ExternalIntent"";");
            migrationBuilder.Sql(@"ALTER TABLE ""CommerceHubMarketplaceOrders"" DROP CONSTRAINT IF EXISTS ""CK_CommerceHubMarketplaceOrders_CustomerAuthorityBoundary"";");
            migrationBuilder.Sql(@"ALTER TABLE ""CommerceHubCheckoutIdempotency"" DROP CONSTRAINT IF EXISTS ""CK_CommerceHubCheckoutIdempotency_CustomerAuthorityBoundary"";");
            migrationBuilder.Sql(@"ALTER TABLE ""CommerceHubCarts"" DROP CONSTRAINT IF EXISTS ""CK_CommerceHubCarts_CustomerAuthorityBoundary"";");
            migrationBuilder.Sql(@"ALTER TABLE ""AccountingOutboxMessages"" DROP CONSTRAINT IF EXISTS ""CK_AccountingOutbox_OrderFinancialEvents_AggregateVersion"";");

            migrationBuilder.Sql(@"ALTER TABLE ""StockSyncOutboxMessages"" ADD COLUMN IF NOT EXISTS ""EventVersion"" integer NOT NULL DEFAULT 1;");
            migrationBuilder.Sql(@"ALTER TABLE ""OutboxMessages"" ADD COLUMN IF NOT EXISTS ""EventVersion"" integer NOT NULL DEFAULT 0;");
            migrationBuilder.Sql(@"ALTER TABLE ""Orders"" ADD COLUMN IF NOT EXISTS ""FinancialPostedAtUtc"" timestamp with time zone NULL;");
            migrationBuilder.Sql(@"ALTER TABLE ""Orders"" ADD COLUMN IF NOT EXISTS ""FinancialPostingFailureReason"" character varying(1000) NULL;");
            migrationBuilder.Sql(@"ALTER TABLE ""Orders"" ADD COLUMN IF NOT EXISTS ""FinancialPostingJournalId"" uuid NULL;");
            migrationBuilder.Sql(@"ALTER TABLE ""Orders"" ADD COLUMN IF NOT EXISTS ""FinancialPostingPendingAtUtc"" timestamp with time zone NULL;");
            migrationBuilder.Sql(@"ALTER TABLE ""Orders"" ADD COLUMN IF NOT EXISTS ""FinancialPostingStatus"" integer NOT NULL DEFAULT 0;");
            migrationBuilder.Sql(@"ALTER TABLE ""OrderItems"" ADD COLUMN IF NOT EXISTS ""AffiliateCommissionAmountSnapshot"" numeric(18,4) NULL;");
            migrationBuilder.Sql(@"ALTER TABLE ""OrderItems"" ADD COLUMN IF NOT EXISTS ""AffiliateCommissionBaseAmountSnapshot"" numeric(18,4) NULL;");
            migrationBuilder.Sql(@"ALTER TABLE ""OrderItems"" ADD COLUMN IF NOT EXISTS ""AffiliateCommissionEligible"" boolean NOT NULL DEFAULT false;");
            migrationBuilder.Sql(@"ALTER TABLE ""OrderItems"" ADD COLUMN IF NOT EXISTS ""AffiliateCommissionPolicyIdSnapshot"" uuid NULL;");
            migrationBuilder.Sql(@"ALTER TABLE ""OrderItems"" ADD COLUMN IF NOT EXISTS ""AffiliateCommissionRateSnapshot"" numeric(18,6) NULL;");
            migrationBuilder.Sql(@"ALTER TABLE ""OrderItems"" ADD COLUMN IF NOT EXISTS ""AffiliateCommissionSnapshotCapturedAtUtc"" timestamp with time zone NULL;");
            migrationBuilder.Sql(@"ALTER TABLE ""OrderItems"" ADD COLUMN IF NOT EXISTS ""AffiliateMaxCommissionPerItemSnapshot"" numeric(18,4) NULL;");
            migrationBuilder.Sql(@"ALTER TABLE ""MarketplaceEventOutboxMessages"" ADD COLUMN IF NOT EXISTS ""AggregateRootId"" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';");
            migrationBuilder.Sql(@"ALTER TABLE ""CommerceHubVendorSettlements"" ADD COLUMN IF NOT EXISTS ""ExternalReference"" character varying(160) NOT NULL DEFAULT '';");
            migrationBuilder.Sql(@"ALTER TABLE ""CommerceHubVendorSettlements"" ADD COLUMN IF NOT EXISTS ""IdempotencyKey"" character varying(120) NOT NULL DEFAULT '';");
            migrationBuilder.Sql(@"ALTER TABLE ""CommerceHubVendorEarnings"" ADD COLUMN IF NOT EXISTS ""PaidOutAmount"" numeric(18,2) NOT NULL DEFAULT 0;");
            migrationBuilder.Sql(@"ALTER TABLE ""CommerceHubVendorEarnings"" ADD COLUMN IF NOT EXISTS ""PayoutReservedAmount"" numeric(18,2) NOT NULL DEFAULT 0;");
            migrationBuilder.Sql(@"ALTER TABLE ""CommerceHubSubOrders"" ADD COLUMN IF NOT EXISTS ""ProjectionMutationVersion"" bigint NOT NULL DEFAULT 1;");
            migrationBuilder.Sql(@"ALTER TABLE ""CommerceHubPayouts"" ADD COLUMN IF NOT EXISTS ""ExternalTransferIdempotencyKey"" character varying(240) NULL;");
            migrationBuilder.Sql(@"ALTER TABLE ""CommerceHubMarketplaceOrders"" ADD COLUMN IF NOT EXISTS ""IsProjectionTerminalFailure"" boolean NOT NULL DEFAULT false;");
            migrationBuilder.Sql(@"ALTER TABLE ""CommerceHubMarketplaceOrders"" ADD COLUMN IF NOT EXISTS ""LastProjectionAttemptAt"" timestamp with time zone NULL;");
            migrationBuilder.Sql(@"ALTER TABLE ""CommerceHubMarketplaceOrders"" ADD COLUMN IF NOT EXISTS ""ProjectionAttemptCount"" integer NOT NULL DEFAULT 0;");
            migrationBuilder.Sql(@"ALTER TABLE ""CommerceHubMarketplaceOrders"" ADD COLUMN IF NOT EXISTS ""ProjectionMutationVersion"" bigint NOT NULL DEFAULT 1;");
            migrationBuilder.Sql(@"ALTER TABLE ""AccountingOutboxMessages"" ADD COLUMN IF NOT EXISTS ""AggregateVersion"" integer NULL;");
            migrationBuilder.Sql(@"ALTER TABLE ""TenantPaymentMethods"" ADD COLUMN IF NOT EXISTS ""Version"" integer NOT NULL DEFAULT 1;");

            migrationBuilder.CreateTable(
                name: "CommerceHubUnresolvedPaymentWebhooks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    ProviderEventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProviderIntentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProviderTransactionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
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
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockSyncOutbox_VariantOrdering",
                table: "StockSyncOutboxMessages",
                columns: new[] { "TenantId", "VariantId", "PublishedAtUtc", "EventVersion", "StockUpdatedAtUtc", "OccurredAtUtc", "CreatedAt", "Id" },
                filter: "\"PublishedAtUtc\" IS NULL AND \"IsFailed\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_StockSyncOutbox_VariantOrderingBlockers",
                table: "StockSyncOutboxMessages",
                columns: new[] { "TenantId", "VariantId", "EventVersion", "StockUpdatedAtUtc", "OccurredAtUtc", "CreatedAt", "Id" },
                filter: "\"PublishedAtUtc\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockSyncOutbox_EventVersion_Positive",
                table: "StockSyncOutboxMessages",
                sql: "\"EventVersion\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_CriticalAggregateOrdering",
                table: "OutboxMessages",
                columns: new[] { "TenantId", "AggregateId", "EventVersion", "CreatedAtUtc", "Id" },
                filter: "\"ProcessedAtUtc\" IS NULL AND \"TenantId\" IS NOT NULL AND \"AggregateId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutboxMessages_CriticalEvents_AggregateId_Required",
                table: "OutboxMessages",
                sql: "(\"AggregateId\" IS NOT NULL AND \"AggregateId\" <> '00000000-0000-0000-0000-000000000000') OR NOT ((lower(\"EventType\") LIKE '%order%' OR lower(\"EventType\") LIKE '%payment%' OR lower(\"EventType\") LIKE '%inventory%' OR lower(\"EventType\") LIKE '%stock%' OR lower(\"EventType\") LIKE '%product%' OR lower(\"EventType\") LIKE '%variant%' OR lower(\"EventType\") LIKE '%commercehub%' OR lower(\"EventType\") LIKE '%marketplace%' OR lower(\"EventType\") LIKE '%affiliate%' OR lower(\"EventType\") LIKE '%coupon%' OR lower(\"EventType\") LIKE '%promotion%' OR lower(\"EventType\") LIKE '%tenant%' OR lower(\"EventType\") LIKE '%storeconfig%' OR lower(\"EventType\") LIKE '%shippingmethod%' OR lower(\"EventType\") LIKE '%shipment%' OR lower(\"EventType\") LIKE '%customer%' OR lower(\"EventType\") LIKE '%checkout%' OR lower(\"EventType\") LIKE '%refund%' OR lower(\"EventType\") LIKE '%payout%' OR lower(\"EventType\") LIKE '%settlement%' OR lower(\"EventType\") LIKE '%ledger%' OR lower(\"EventType\") LIKE '%accounting%' OR lower(\"EventType\") LIKE '%wallet%' OR lower(\"EventType\") LIKE '%cod%' OR lower(\"EventType\") LIKE '%currency%' OR lower(\"EventType\") LIKE '%providertrust%') AND lower(\"EventType\") NOT LIKE '%affiliatelinkclickedevent%' AND lower(\"EventType\") NOT LIKE '%lowstockalertevent%' AND lower(\"EventType\") NOT LIKE '%onlinecustomeremailverificationrequestedevent%' AND lower(\"EventType\") NOT LIKE '%promotionseenevent%' AND lower(\"EventType\") NOT LIKE '%promotionclickedevent%' AND lower(\"EventType\") NOT LIKE '%promotionconvertedevent%' AND lower(\"EventType\") NOT LIKE '%promotionboostappliedevent%' AND lower(\"EventType\") NOT LIKE '%whatsapporderconfirmationrequestedevent%' AND lower(\"EventType\") NOT LIKE '%whatsapppaymentapprovalrequestedevent%' AND lower(\"EventType\") NOT LIKE '%whatsapppaymentapprovednotificationevent%' AND lower(\"EventType\") NOT LIKE '%whatsapppaymentrejectednotificationevent%')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutboxMessages_CriticalEvents_EventVersion_Required",
                table: "OutboxMessages",
                sql: "\"EventVersion\" > 0 OR NOT ((lower(\"EventType\") LIKE '%order%' OR lower(\"EventType\") LIKE '%payment%' OR lower(\"EventType\") LIKE '%inventory%' OR lower(\"EventType\") LIKE '%stock%' OR lower(\"EventType\") LIKE '%product%' OR lower(\"EventType\") LIKE '%variant%' OR lower(\"EventType\") LIKE '%commercehub%' OR lower(\"EventType\") LIKE '%marketplace%' OR lower(\"EventType\") LIKE '%affiliate%' OR lower(\"EventType\") LIKE '%coupon%' OR lower(\"EventType\") LIKE '%promotion%' OR lower(\"EventType\") LIKE '%tenant%' OR lower(\"EventType\") LIKE '%storeconfig%' OR lower(\"EventType\") LIKE '%shippingmethod%' OR lower(\"EventType\") LIKE '%shipment%' OR lower(\"EventType\") LIKE '%customer%' OR lower(\"EventType\") LIKE '%checkout%' OR lower(\"EventType\") LIKE '%refund%' OR lower(\"EventType\") LIKE '%payout%' OR lower(\"EventType\") LIKE '%settlement%' OR lower(\"EventType\") LIKE '%ledger%' OR lower(\"EventType\") LIKE '%accounting%' OR lower(\"EventType\") LIKE '%wallet%' OR lower(\"EventType\") LIKE '%cod%' OR lower(\"EventType\") LIKE '%currency%' OR lower(\"EventType\") LIKE '%providertrust%') AND lower(\"EventType\") NOT LIKE '%affiliatelinkclickedevent%' AND lower(\"EventType\") NOT LIKE '%lowstockalertevent%' AND lower(\"EventType\") NOT LIKE '%onlinecustomeremailverificationrequestedevent%' AND lower(\"EventType\") NOT LIKE '%promotionseenevent%' AND lower(\"EventType\") NOT LIKE '%promotionclickedevent%' AND lower(\"EventType\") NOT LIKE '%promotionconvertedevent%' AND lower(\"EventType\") NOT LIKE '%promotionboostappliedevent%' AND lower(\"EventType\") NOT LIKE '%whatsapporderconfirmationrequestedevent%' AND lower(\"EventType\") NOT LIKE '%whatsapppaymentapprovalrequestedevent%' AND lower(\"EventType\") NOT LIKE '%whatsapppaymentapprovednotificationevent%' AND lower(\"EventType\") NOT LIKE '%whatsapppaymentrejectednotificationevent%')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutboxMessages_CriticalEvents_TenantId_Required",
                table: "OutboxMessages",
                sql: "(\"TenantId\" IS NOT NULL AND \"TenantId\" <> '00000000-0000-0000-0000-000000000000') OR NOT ((lower(\"EventType\") LIKE '%order%' OR lower(\"EventType\") LIKE '%payment%' OR lower(\"EventType\") LIKE '%inventory%' OR lower(\"EventType\") LIKE '%stock%' OR lower(\"EventType\") LIKE '%product%' OR lower(\"EventType\") LIKE '%variant%' OR lower(\"EventType\") LIKE '%commercehub%' OR lower(\"EventType\") LIKE '%marketplace%' OR lower(\"EventType\") LIKE '%affiliate%' OR lower(\"EventType\") LIKE '%coupon%' OR lower(\"EventType\") LIKE '%promotion%' OR lower(\"EventType\") LIKE '%tenant%' OR lower(\"EventType\") LIKE '%storeconfig%' OR lower(\"EventType\") LIKE '%shippingmethod%' OR lower(\"EventType\") LIKE '%shipment%' OR lower(\"EventType\") LIKE '%customer%' OR lower(\"EventType\") LIKE '%checkout%' OR lower(\"EventType\") LIKE '%refund%' OR lower(\"EventType\") LIKE '%payout%' OR lower(\"EventType\") LIKE '%settlement%' OR lower(\"EventType\") LIKE '%ledger%' OR lower(\"EventType\") LIKE '%accounting%' OR lower(\"EventType\") LIKE '%wallet%' OR lower(\"EventType\") LIKE '%cod%' OR lower(\"EventType\") LIKE '%currency%' OR lower(\"EventType\") LIKE '%providertrust%') AND lower(\"EventType\") NOT LIKE '%affiliatelinkclickedevent%' AND lower(\"EventType\") NOT LIKE '%lowstockalertevent%' AND lower(\"EventType\") NOT LIKE '%onlinecustomeremailverificationrequestedevent%' AND lower(\"EventType\") NOT LIKE '%promotionseenevent%' AND lower(\"EventType\") NOT LIKE '%promotionclickedevent%' AND lower(\"EventType\") NOT LIKE '%promotionconvertedevent%' AND lower(\"EventType\") NOT LIKE '%promotionboostappliedevent%' AND lower(\"EventType\") NOT LIKE '%whatsapporderconfirmationrequestedevent%' AND lower(\"EventType\") NOT LIKE '%whatsapppaymentapprovalrequestedevent%' AND lower(\"EventType\") NOT LIKE '%whatsapppaymentapprovednotificationevent%' AND lower(\"EventType\") NOT LIKE '%whatsapppaymentrejectednotificationevent%')");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_FinancialPostingStatus_CreatedAt",
                table: "Orders",
                columns: new[] { "TenantId", "FinancialPostingStatus", "CreatedAt" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_CustomerAuthorityBoundary",
                table: "Orders",
                sql: "(\"CustomerId\" IS NULL OR \"OnlineCustomerId\" IS NULL) AND (\"Source\" <> 0 OR \"CustomerId\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_FinancialPostingStatus_Valid",
                table: "Orders",
                sql: "\"FinancialPostingStatus\" IN (0, 1, 2, 3) AND (\"FinancialPostingStatus\" <> 1 OR \"FinancialPostingPendingAtUtc\" IS NOT NULL) AND (\"FinancialPostingStatus\" <> 2 OR (\"FinancialPostedAtUtc\" IS NOT NULL AND \"FinancialPostingJournalId\" IS NOT NULL)) AND (\"FinancialPostingStatus\" <> 3 OR \"FinancialPostingFailureReason\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderItems_AffiliateCommissionSnapshot_Valid",
                table: "OrderItems",
                sql: "(\"AffiliateCommissionEligible\" = true AND \"AffiliateCommissionSnapshotCapturedAtUtc\" IS NOT NULL AND \"AffiliateCommissionPolicyIdSnapshot\" IS NOT NULL AND \"AffiliateCommissionRateSnapshot\" > 0 AND \"AffiliateCommissionRateSnapshot\" <= 0.500000 AND \"AffiliateCommissionBaseAmountSnapshot\" > 0 AND \"AffiliateCommissionAmountSnapshot\" > 0 AND (\"AffiliateMaxCommissionPerItemSnapshot\" IS NULL OR \"AffiliateMaxCommissionPerItemSnapshot\" > 0)) OR (\"AffiliateCommissionEligible\" = false AND \"AffiliateCommissionSnapshotCapturedAtUtc\" IS NULL AND \"AffiliateCommissionPolicyIdSnapshot\" IS NULL AND \"AffiliateCommissionRateSnapshot\" IS NULL AND \"AffiliateCommissionBaseAmountSnapshot\" IS NULL AND \"AffiliateCommissionAmountSnapshot\" IS NULL AND (\"AffiliateMaxCommissionPerItemSnapshot\" IS NULL OR \"AffiliateMaxCommissionPerItemSnapshot\" = 0))");

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
                name: "IX_MarketplaceEventOutbox_TenantId_AggregateRootId",
                table: "MarketplaceEventOutboxMessages",
                columns: new[] { "TenantId", "AggregateRootId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_MarketplaceEventOutbox_AggregateRootId_NotEmpty",
                table: "MarketplaceEventOutboxMessages",
                sql: "\"AggregateRootId\" IS NOT NULL AND \"AggregateRootId\" <> '00000000-0000-0000-0000-000000000000'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MarketplaceEventOutbox_TenantId_Required",
                table: "MarketplaceEventOutboxMessages",
                sql: "\"TenantId\" IS NOT NULL AND \"TenantId\" <> '00000000-0000-0000-0000-000000000000'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MarketplaceEventOutbox_Version_Positive",
                table: "MarketplaceEventOutboxMessages",
                sql: "\"Version\" > 0");

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

            migrationBuilder.AddCheckConstraint(
                name: "CK_CommerceHubVendorSettlements_ExternalReference_NotBlank",
                table: "CommerceHubVendorSettlements",
                sql: "NULLIF(btrim(\"ExternalReference\"), '') IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CommerceHubVendorSettlements_IdempotencyKey_NotBlank",
                table: "CommerceHubVendorSettlements",
                sql: "NULLIF(btrim(\"IdempotencyKey\"), '') IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_CommerceHubVendorPaymentRequests_Live_Method_TransferReference",
                table: "CommerceHubVendorPaymentRequests",
                columns: new[] { "PaymentMethodId", "TransferReference" },
                unique: true,
                filter: "\"TransferReference\" IS NOT NULL AND \"TransferReference\" <> '' AND \"Status\" <> 2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CommerceHubVendorPaymentRequests_Approved_TransferReference",
                table: "CommerceHubVendorPaymentRequests",
                sql: "\"Status\" <> 1 OR NULLIF(btrim(\"TransferReference\"), '') IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_CommerceHubVendorPaymentProofs_ContentHash",
                table: "CommerceHubVendorPaymentProofs",
                column: "ContentHash",
                unique: true,
                filter: "\"ContentHash\" IS NOT NULL AND \"ContentHash\" <> '' AND \"ContentHash\" <> 'LEGACY_000000000000000000000000000000000000000000000000000000000'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CommerceHubVendorEarnings_InPayout_HasPayout",
                table: "CommerceHubVendorEarnings",
                sql: "\"Status\" <> 3 OR \"PayoutId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CommerceHubVendorEarnings_PaidOut_HasPaidAmount",
                table: "CommerceHubVendorEarnings",
                sql: "\"Status\" <> 2 OR \"PaidOutAmount\" > 0 OR \"Amount\" < 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CommerceHubVendorEarnings_PayoutAmounts_NonNegative",
                table: "CommerceHubVendorEarnings",
                sql: "\"PaidOutAmount\" >= 0 AND \"PayoutReservedAmount\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CommerceHubVendorEarnings_PayoutAmounts_Within_Positive_Ear~",
                table: "CommerceHubVendorEarnings",
                sql: "\"Amount\" < 0 OR ROUND(\"PaidOutAmount\" + \"PayoutReservedAmount\", 2) <= ROUND(\"Amount\", 2)");

            migrationBuilder.CreateIndex(
                name: "UX_CommerceHubPayouts_ExternalTransferIdempotencyKey",
                table: "CommerceHubPayouts",
                columns: new[] { "TenantId", "ExternalTransferIdempotencyKey" },
                unique: true,
                filter: "\"ExternalTransferIdempotencyKey\" IS NOT NULL AND \"ExternalTransferIdempotencyKey\" <> '' AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UX_CommerceHubPayouts_Live_BankTransactionId",
                table: "CommerceHubPayouts",
                columns: new[] { "TenantId", "BankTransactionId" },
                unique: true,
                filter: "\"BankTransactionId\" IS NOT NULL AND \"BankTransactionId\" <> '' AND \"Status\" <> 3 AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UX_CommerceHubPayouts_Live_ExternalReferenceId",
                table: "CommerceHubPayouts",
                columns: new[] { "TenantId", "ExternalReferenceId" },
                unique: true,
                filter: "\"ExternalReferenceId\" IS NOT NULL AND \"ExternalReferenceId\" <> '' AND \"Status\" <> 3 AND \"IsDeleted\" = false");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CommerceHubPayouts_Completed_Evidence",
                table: "CommerceHubPayouts",
                sql: "\"Status\" <> 2 OR NULLIF(btrim(\"ExternalReferenceId\"), '') IS NOT NULL OR NULLIF(btrim(\"BankTransactionId\"), '') IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CommerceHubPayouts_Processing_ExternalIntent",
                table: "CommerceHubPayouts",
                sql: "\"Status\" <> 1 OR NULLIF(btrim(\"ExternalTransferIdempotencyKey\"), '') IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CommerceHubMarketplaceOrders_CustomerAuthorityBoundary",
                table: "CommerceHubMarketplaceOrders",
                sql: "\"CustomerId\" <> '00000000-0000-0000-0000-000000000000' AND \"OnlineCustomerId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubCheckoutIdempotency_TenantId_CommerceHubCustomerId",
                table: "CommerceHubCheckoutIdempotency",
                columns: new[] { "TenantId", "CommerceHubCustomerId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_CommerceHubCheckoutIdempotency_CustomerAuthorityBoundary",
                table: "CommerceHubCheckoutIdempotency",
                sql: "\"CommerceHubCustomerId\" IS NOT NULL AND \"CommerceHubCustomerId\" <> '00000000-0000-0000-0000-000000000000' AND \"OnlineCustomerId\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CommerceHubCarts_CustomerAuthorityBoundary",
                table: "CommerceHubCarts",
                sql: "\"CommerceHubCustomerId\" IS NOT NULL AND \"CommerceHubCustomerId\" <> '00000000-0000-0000-0000-000000000000' AND \"OnlineCustomerId\" IS NOT NULL AND \"OnlineCustomerId\" <> '00000000-0000-0000-0000-000000000000' AND \"OnlineCustomerId\" = \"CommerceHubCustomerId\"");

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateProductPolicies_Tenant_Product",
                table: "AffiliateProductPolicies",
                columns: new[] { "TenantId", "ProductId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

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

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccountingOutbox_OrderFinancialEvents_AggregateVersion",
                table: "AccountingOutboxMessages",
                sql: "\"EventType\" NOT IN ('OnlinePayment', 'CodDelivered', 'WalletApproved', 'EcommerceRefund') OR (\"AggregateVersion\" IS NOT NULL AND \"AggregateVersion\" > 0)");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommerceHubUnresolvedPaymentWebhooks");

            migrationBuilder.DropIndex(
                name: "IX_StockSyncOutbox_VariantOrdering",
                table: "StockSyncOutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_StockSyncOutbox_VariantOrderingBlockers",
                table: "StockSyncOutboxMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StockSyncOutbox_EventVersion_Positive",
                table: "StockSyncOutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_CriticalAggregateOrdering",
                table: "OutboxMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutboxMessages_CriticalEvents_AggregateId_Required",
                table: "OutboxMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutboxMessages_CriticalEvents_EventVersion_Required",
                table: "OutboxMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutboxMessages_CriticalEvents_TenantId_Required",
                table: "OutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_Orders_TenantId_FinancialPostingStatus_CreatedAt",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_CustomerAuthorityBoundary",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_FinancialPostingStatus_Valid",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderItems_AffiliateCommissionSnapshot_Valid",
                table: "OrderItems");

            migrationBuilder.DropIndex(
                name: "IX_MarketplaceEventOutbox_AggregateOrdering",
                table: "MarketplaceEventOutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_MarketplaceEventOutbox_AggregateOrderingBlockers",
                table: "MarketplaceEventOutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_MarketplaceEventOutbox_TenantId_AggregateRootId",
                table: "MarketplaceEventOutboxMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MarketplaceEventOutbox_AggregateRootId_NotEmpty",
                table: "MarketplaceEventOutboxMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MarketplaceEventOutbox_TenantId_Required",
                table: "MarketplaceEventOutboxMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MarketplaceEventOutbox_Version_Positive",
                table: "MarketplaceEventOutboxMessages");

            migrationBuilder.DropIndex(
                name: "UX_CommerceHubVendorSettlements_ExternalReference",
                table: "CommerceHubVendorSettlements");

            migrationBuilder.DropIndex(
                name: "UX_CommerceHubVendorSettlements_Vendor_IdempotencyKey",
                table: "CommerceHubVendorSettlements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CommerceHubVendorSettlements_ExternalReference_NotBlank",
                table: "CommerceHubVendorSettlements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CommerceHubVendorSettlements_IdempotencyKey_NotBlank",
                table: "CommerceHubVendorSettlements");

            migrationBuilder.DropIndex(
                name: "UX_CommerceHubVendorPaymentRequests_Live_Method_TransferReference",
                table: "CommerceHubVendorPaymentRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CommerceHubVendorPaymentRequests_Approved_TransferReference",
                table: "CommerceHubVendorPaymentRequests");

            migrationBuilder.DropIndex(
                name: "UX_CommerceHubVendorPaymentProofs_ContentHash",
                table: "CommerceHubVendorPaymentProofs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CommerceHubVendorEarnings_InPayout_HasPayout",
                table: "CommerceHubVendorEarnings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CommerceHubVendorEarnings_PaidOut_HasPaidAmount",
                table: "CommerceHubVendorEarnings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CommerceHubVendorEarnings_PayoutAmounts_NonNegative",
                table: "CommerceHubVendorEarnings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CommerceHubVendorEarnings_PayoutAmounts_Within_Positive_Ear~",
                table: "CommerceHubVendorEarnings");

            migrationBuilder.DropIndex(
                name: "UX_CommerceHubPayouts_ExternalTransferIdempotencyKey",
                table: "CommerceHubPayouts");

            migrationBuilder.DropIndex(
                name: "UX_CommerceHubPayouts_Live_BankTransactionId",
                table: "CommerceHubPayouts");

            migrationBuilder.DropIndex(
                name: "UX_CommerceHubPayouts_Live_ExternalReferenceId",
                table: "CommerceHubPayouts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CommerceHubPayouts_Completed_Evidence",
                table: "CommerceHubPayouts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CommerceHubPayouts_Processing_ExternalIntent",
                table: "CommerceHubPayouts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CommerceHubMarketplaceOrders_CustomerAuthorityBoundary",
                table: "CommerceHubMarketplaceOrders");

            migrationBuilder.DropIndex(
                name: "IX_CommerceHubCheckoutIdempotency_TenantId_CommerceHubCustomerId",
                table: "CommerceHubCheckoutIdempotency");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CommerceHubCheckoutIdempotency_CustomerAuthorityBoundary",
                table: "CommerceHubCheckoutIdempotency");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CommerceHubCarts_CustomerAuthorityBoundary",
                table: "CommerceHubCarts");

            migrationBuilder.DropIndex(
                name: "UQ_AffiliateProductPolicies_Tenant_Product",
                table: "AffiliateProductPolicies");

            migrationBuilder.DropIndex(
                name: "IX_AccountingOutbox_AggregateVersionBlockers",
                table: "AccountingOutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_AccountingOutbox_AggregateVersionOrdering",
                table: "AccountingOutboxMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AccountingOutbox_OrderFinancialEvents_AggregateVersion",
                table: "AccountingOutboxMessages");

            migrationBuilder.DropColumn(
                name: "EventVersion",
                table: "StockSyncOutboxMessages");

            migrationBuilder.DropColumn(
                name: "EventVersion",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "FinancialPostedAtUtc",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FinancialPostingFailureReason",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FinancialPostingJournalId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FinancialPostingPendingAtUtc",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FinancialPostingStatus",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "AffiliateCommissionAmountSnapshot",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "AffiliateCommissionBaseAmountSnapshot",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "AffiliateCommissionEligible",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "AffiliateCommissionPolicyIdSnapshot",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "AffiliateCommissionRateSnapshot",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "AffiliateCommissionSnapshotCapturedAtUtc",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "AffiliateMaxCommissionPerItemSnapshot",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "AggregateRootId",
                table: "MarketplaceEventOutboxMessages");

            migrationBuilder.DropColumn(
                name: "ExternalReference",
                table: "CommerceHubVendorSettlements");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "CommerceHubVendorSettlements");

            migrationBuilder.DropColumn(
                name: "PaidOutAmount",
                table: "CommerceHubVendorEarnings");

            migrationBuilder.DropColumn(
                name: "PayoutReservedAmount",
                table: "CommerceHubVendorEarnings");

            migrationBuilder.DropColumn(
                name: "ProjectionMutationVersion",
                table: "CommerceHubSubOrders");

            migrationBuilder.DropColumn(
                name: "ExternalTransferIdempotencyKey",
                table: "CommerceHubPayouts");

            migrationBuilder.DropColumn(
                name: "IsProjectionTerminalFailure",
                table: "CommerceHubMarketplaceOrders");

            migrationBuilder.DropColumn(
                name: "LastProjectionAttemptAt",
                table: "CommerceHubMarketplaceOrders");

            migrationBuilder.DropColumn(
                name: "ProjectionAttemptCount",
                table: "CommerceHubMarketplaceOrders");

            migrationBuilder.DropColumn(
                name: "ProjectionMutationVersion",
                table: "CommerceHubMarketplaceOrders");

            migrationBuilder.CreateIndex(
                name: "IX_StockSyncOutbox_VariantOrdering",
                table: "StockSyncOutboxMessages",
                columns: new[] { "TenantId", "VariantId", "PublishedAtUtc", "StockUpdatedAtUtc", "OccurredAtUtc", "CreatedAt", "Id" },
                filter: "\"PublishedAtUtc\" IS NULL AND \"IsFailed\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_StockSyncOutbox_VariantOrderingBlockers",
                table: "StockSyncOutboxMessages",
                columns: new[] { "TenantId", "VariantId", "StockUpdatedAtUtc", "OccurredAtUtc", "CreatedAt", "Id" },
                filter: "\"PublishedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceEventOutbox_EntityOrdering",
                table: "MarketplaceEventOutboxMessages",
                columns: new[] { "TenantId", "EntityId", "PublishedAtUtc", "OccurredAtUtc", "Version", "CreatedAt", "Id" },
                filter: "\"PublishedAtUtc\" IS NULL AND \"IsFailed\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceEventOutbox_EntityOrderingBlockers",
                table: "MarketplaceEventOutboxMessages",
                columns: new[] { "TenantId", "EntityId", "OccurredAtUtc", "Version", "CreatedAt", "Id" },
                filter: "\"PublishedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubVendorPaymentRequests_PaymentMethodId",
                table: "CommerceHubVendorPaymentRequests",
                column: "PaymentMethodId");

            migrationBuilder.CreateIndex(
                name: "UX_CommerceHubPayouts_Completed_BankTransactionId",
                table: "CommerceHubPayouts",
                columns: new[] { "TenantId", "BankTransactionId" },
                unique: true,
                filter: "\"BankTransactionId\" IS NOT NULL AND \"BankTransactionId\" <> '' AND \"Status\" = 2 AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UX_CommerceHubPayouts_Completed_ExternalReferenceId",
                table: "CommerceHubPayouts",
                columns: new[] { "TenantId", "ExternalReferenceId" },
                unique: true,
                filter: "\"ExternalReferenceId\" IS NOT NULL AND \"ExternalReferenceId\" <> '' AND \"Status\" = 2 AND \"IsDeleted\" = false");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CommerceHubPayouts_NonManual_Completed_Evidence",
                table: "CommerceHubPayouts",
                sql: "\"Status\" <> 2 OR \"Method\" = 2 OR \"ExternalReferenceId\" IS NOT NULL OR \"BankTransactionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubCheckoutIdempotency_TenantId_OnlineCustomerId",
                table: "CommerceHubCheckoutIdempotency",
                columns: new[] { "TenantId", "OnlineCustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubCarts_TenantId_OnlineCustomerId",
                table: "CommerceHubCarts",
                columns: new[] { "TenantId", "OnlineCustomerId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateProductPolicies_Tenant_Product",
                table: "AffiliateProductPolicies",
                columns: new[] { "TenantId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountingOutbox_AggregateVersionBlockers",
                table: "AccountingOutboxMessages",
                columns: new[] { "TenantId", "BusinessEntityId", "AggregateVersion", "ProcessedAtUtc", "IsFailed", "EventType" },
                filter: "\"ProcessedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AccountingOutbox_AggregateVersionOrdering",
                table: "AccountingOutboxMessages",
                columns: new[] { "TenantId", "BusinessEntityId", "AggregateVersion", "OccurredAtUtc" },
                filter: "\"ProcessedAtUtc\" IS NULL AND \"IsFailed\" = false");
        }
    }
}
