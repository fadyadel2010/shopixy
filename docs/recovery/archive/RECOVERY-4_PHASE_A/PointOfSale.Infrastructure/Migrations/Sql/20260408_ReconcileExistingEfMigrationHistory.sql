-- One-time repair script for already-populated environments that were upgraded
-- with manual SQL and now have a partial "__EFMigrationsHistory" baseline.
--
-- Run this only on a database where the historical schema changes below already exist.
-- It does not create those legacy objects; it only marks the corresponding EF migrations as applied
-- so future EF migration operations stop replaying "InitialCreate" and other early migrations.

BEGIN;

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory"
(
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES
    ('20251123190510_InitialCreate', '9.0.1'),
    ('20251126172001_AddCashboxEntities', '9.0.1'),
    ('20251126175326_MakeCashboxWarehouseOptional', '9.0.1'),
    ('20251126213231_AddAccountLedgerAndSOA', '9.0.1'),
    ('20251126221658_AddStockTransfer', '9.0.1'),
    ('20251127185031_AddSalesRepresentatives', '9.0.1'),
    ('20251128175638_AddFinancialAccounts', '9.0.1'),
    ('20251201201129_AddCashboxOpeningBalanceFields', '9.0.1'),
    ('20251201203400_AddCustomerOpeningBalanceFields', '9.0.1'),
    ('20251202144522_AddSupplierOpeningBalanceFields', '9.0.1'),
    ('20251202152548_AddOpeningBalanceFieldsToStockMovement', '9.0.1'),
    ('20251202174743_AddBanksTable', '9.0.1'),
    ('20251202190721_AddBankTransactions', '9.0.1'),
    ('20251202203358_AddExpensesManagement', '9.0.1'),
    ('20251204131000_AddTreasuryTransactions', '9.0.1'),
    ('20251204192040_AddVisaTransactions', '9.0.1'),
    ('20251205125248_AddMultiUnitSupport', '9.0.1'),
    ('20251205180157_AddDueDateToPurchaseInvoices', '9.0.1'),
    ('20251209172937_UpdateVisaAccountName', '9.0.1'),
    ('20251209173809_FixVisaAccountIsDeleted', '9.0.1'),
    ('20251211181712_AddAuditLogs', '9.0.1'),
    ('20251214184156_AddTrialBalanceColumnsToFinancialAccount', '9.0.1'),
    ('20251221141054_AddEcommerceEntities', '9.0.1'),
    ('20251221180334_AddMassTransitOutbox', '9.0.1'),
    ('20251221200148_AddMultiTenancyFoundation', '9.0.1'),
    ('20251224163945_AddPaymentsAndShipments', '9.0.1'),
    ('20251224210903_AddStoreConfigs', '9.0.1'),
    ('20251226140322_TenantRegistration_Phase8', '9.0.1'),
    ('20251226154845_EmailVerification_Phase84', '9.0.1'),
    ('20251226210606_TenantIsolationHardening', '9.0.1'),
    ('20251226212911_AuditLogTenantId', '9.0.1'),
    ('20260102141736_AddOnlineCustomersTable', '9.0.1'),
    ('20260104210031_AddOnlineCustomerWishlistItem', '9.0.1'),
    ('20260105201157_AddOnlinePriceToProduct', '9.0.1'),
    ('20260108143906_AddShippingSnapshotFields', '9.0.1'),
    ('20260108155753_AddTenantPaymentMethods', '9.0.1'),
    ('20260109135134_AddEstimatedDeliveryDaysToOrder', '9.0.1'),
    ('20260109175711_AddOnlineCustomerIdToOrders', '9.0.1'),
    ('20260111160400_FixCouponUniqueIndexToIncludeTenantId', '9.0.1'),
    ('20260111160634_FixCouponsTenantIdAndUniqueIndex', '9.0.1'),
    ('20260111160851_FixCouponUniqueIndexToIncludeTenantId', '9.0.1'),
    ('20260115200647_AddStoreConfigOptimisticConcurrency', '9.0.1'),
    ('20260116130637_AddPromotionsEngine', '9.0.1'),
    ('20260116155940_AddOrderPromotionDiscountAmount', '9.0.1'),
    ('20260116194740_AddPromotionBannerStyle', '9.0.1'),
    ('20260120154936_AddTenantAnalyticsConfiguration', '9.0.1'),
    ('20260121204129_AddCampaignCosts', '9.0.1'),
    ('20260122123451_AddShipmentOutForDeliveryTimestamp', '9.0.1'),
    ('20260122165616_AddMissingColumnsToCarrierConfigurations', '9.0.1'),
    ('20260129191429_WhatsAppTemplateHardening', '9.0.1'),
    ('20260204190605_AddMissingColumnsToTenantAutomationSettings', '9.0.1'),
    ('20260204203053_AddWhatsAppRateLimitsTable', '9.0.1'),
    ('20260213225438_AddServiceRequestCompositeIndexes', '9.0.1'),
    ('20260223214810_AF16D_AddStorefrontBaseUrlToTenant', '9.0.1'),
    ('20260226192814_AddTemplateTypeToAffiliateProductPolicy', '9.0.1'),
    ('20260227220116_AF38_HOTFIX_Constraints_Update', '9.0.1'),
    ('20260318205107_AddCommerceHubCheckoutTables', '9.0.1'),
    ('20260331132612_AddMarketplaceProductReviews', '9.0.1')
ON CONFLICT ("MigrationId") DO NOTHING;

COMMIT;
