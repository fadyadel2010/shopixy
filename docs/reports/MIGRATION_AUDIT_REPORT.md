# Migration Audit Report

## 1. Executive Summary

**Overall status: FAIL.** The PostgreSQL migration history is not synchronized with the application EF Core migration history.

Evidence summary: EF CLI reported 141 migrations for `ApplicationDbContext`, of which 40 are pending. Direct read-only query of `public.__EFMigrationsHistory` returned 128 rows. The current source tree contains 189 physical migration `.cs` files across `PointOfSale.Infrastructure\Migrations` and `PointOfSale.Infrastructure\Data\Migrations`.

Synchronization state: database and application history diverge. There are 40 EF CLI-recognized migrations not recorded in the database, 27 database history rows not recognized by the no-build EF CLI assembly, 22 database history rows with no physical migration `.cs` file, and 83 physical migration `.cs` files not recorded in the database history.

## 2. Environment

| Item | Evidence |
|---|---|
| Solution | `PointOfSale.sln` at repository root. |
| Audited production EF Core DbContext | `PointOfSale.Infrastructure.Data.ApplicationDbContext`; source `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:23`. |
| Startup project | `PointOfSale.API`; `PointOfSale.API/Program.cs:327` registers `AddDbContext<ApplicationDbContext>`; `PointOfSale.API/Program.cs:329` calls `UseNpgsql(defaultConnectionString)`. |
| Migration assembly | `PointOfSale.Infrastructure` by EF Core default; no `MigrationsAssembly` usage was found by `rg -n MigrationsAssembly PointOfSale.Infrastructure PointOfSale.API -g *.cs`. |
| Design-time factory | `PointOfSale.Infrastructure/Data/ApplicationDbContextFactory.cs:12`; `UseNpgsql(connectionString)` at line 43. |
| Target framework | `net9.0`; `PointOfSale.Infrastructure.csproj:38`, `PointOfSale.API.csproj:4`. |
| EF Core tool/design package | `Microsoft.EntityFrameworkCore.Design` and `Microsoft.EntityFrameworkCore.Tools` version `9.0.1`; `PointOfSale.Infrastructure.csproj:11` and `PointOfSale.Infrastructure.csproj:15`; `dotnet ef --version` returned `9.0.1`. |
| Provider | `Npgsql.EntityFrameworkCore.PostgreSQL` version `9.0.2`; `PointOfSale.Infrastructure.csproj:21`. |
| .NET SDK | `dotnet --version` returned `9.0.308`; `global.json` pins SDK `9.0.308`. |
| Database connection source | `PointOfSale.API/appsettings.Development.local.json` parsed with credentials redacted: Host `192.168.1.5`, Port `5432`, Database `PosEcommerceDb`, Username `postgres`; design-time factory also supports `ConnectionStrings__DefaultConnection`. |
| Database identity query | Read-only query returned database `PosEcommerceDb`, current user `postgres`, server `192.168.1.5/32`, port `5432`. |
| History table | `public.__EFMigrationsHistory` exists: `True`. |

Production DbContext discovery evidence: `rg -n 'class\s+.*DbContext|:\s*DbContext|IDesignTimeDbContextFactory'` in production projects found `ApplicationDbContext`, `ApplicationDbContextFactory`, `ApplicationDbContextModelSnapshot`, and `MongoDbContext`; `MongoDbContext` is not an EF Core `DbContext` subclass. Test-only nested EF contexts were found under `PointOfSale.Tests` and are not production PostgreSQL migration contexts.

Test-only EF DbContext types discovered: `CheckoutIdempotencyTestDbContext` (`PointOfSale.Tests/CheckoutRequestIdempotencyCoordinatorTests.cs:409`), `QueryFilterDisabledApplicationDbContext` (`PointOfSale.Tests/DashboardControllerTenantScopeTests.cs:235`), `TrackingApplicationDbContext` (`PointOfSale.Tests/Admin/ShippingMethodsSyncReliabilityTests.cs:132` and `PointOfSale.Tests/Admin/PaymentMethodsAdminSyncReliabilityTests.cs:126`), `SqliteLifecycleTestDbContext` (`PointOfSale.Tests/Integration/InventoryReservationLifecycleIntegrationTests.cs:1190`), `ManualWalletConcurrencyDbContext` (`PointOfSale.Tests/Integration/ManualWalletPaymentConcurrencyIntegrationTests.cs:405`), `ThrowingApplicationDbContext` (`PointOfSale.Tests/Security/PosAuthenticationLifecycleTests.cs:270`), `SessionBindingDbContext` (`PointOfSale.Tests/Security/PosSessionConcurrencyTests.cs:59`), `CouponAtomicityApplicationDbContext` (`PointOfSale.Tests/StorefrontCartCouponAtomicityTests.cs:647`), and `SpyApplicationDbContext` (`PointOfSale.Tests/SuperAdmin/SuperAdminHomepageSectionManagementServiceTests.cs:856`). No production PostgreSQL migration assembly/history was found for these test-only contexts.

## 3. Migration Inventory

Inventory basis: union of physical migration `.cs` files and database history rows, ordered lexicographically by migration ID. `Present in Code` means a physical migration `.cs` file exists in `PointOfSale.Infrastructure`. `Applied to Database` means a row exists in `public.__EFMigrationsHistory`.

| Order | Migration ID | Migration Name | Present in Code | Applied to Database |
|---:|---|---|---|---|
| 1 | `20251123190510_InitialCreate` | `InitialCreate` | Yes | Yes |
| 2 | `20251126172001_AddCashboxEntities` | `AddCashboxEntities` | Yes | Yes |
| 3 | `20251126175326_MakeCashboxWarehouseOptional` | `MakeCashboxWarehouseOptional` | Yes | Yes |
| 4 | `20251126213231_AddAccountLedgerAndSOA` | `AddAccountLedgerAndSOA` | Yes | Yes |
| 5 | `20251126221658_AddStockTransfer` | `AddStockTransfer` | Yes | Yes |
| 6 | `20251127185031_AddSalesRepresentatives` | `AddSalesRepresentatives` | Yes | Yes |
| 7 | `20251128175638_AddFinancialAccounts` | `AddFinancialAccounts` | Yes | Yes |
| 8 | `20251201201129_AddCashboxOpeningBalanceFields` | `AddCashboxOpeningBalanceFields` | Yes | Yes |
| 9 | `20251201203400_AddCustomerOpeningBalanceFields` | `AddCustomerOpeningBalanceFields` | Yes | Yes |
| 10 | `20251202144522_AddSupplierOpeningBalanceFields` | `AddSupplierOpeningBalanceFields` | Yes | Yes |
| 11 | `20251202152548_AddOpeningBalanceFieldsToStockMovement` | `AddOpeningBalanceFieldsToStockMovement` | Yes | Yes |
| 12 | `20251202174743_AddBanksTable` | `AddBanksTable` | Yes | Yes |
| 13 | `20251202190721_AddBankTransactions` | `AddBankTransactions` | Yes | Yes |
| 14 | `20251202203358_AddExpensesManagement` | `AddExpensesManagement` | Yes | Yes |
| 15 | `20251204131000_AddTreasuryTransactions` | `AddTreasuryTransactions` | Yes | Yes |
| 16 | `20251204192040_AddVisaTransactions` | `AddVisaTransactions` | Yes | Yes |
| 17 | `20251205125248_AddMultiUnitSupport` | `AddMultiUnitSupport` | Yes | Yes |
| 18 | `20251205180157_AddDueDateToPurchaseInvoices` | `AddDueDateToPurchaseInvoices` | Yes | Yes |
| 19 | `20251209172937_UpdateVisaAccountName` | `UpdateVisaAccountName` | Yes | Yes |
| 20 | `20251209173809_FixVisaAccountIsDeleted` | `FixVisaAccountIsDeleted` | Yes | Yes |
| 21 | `20251211181712_AddAuditLogs` | `AddAuditLogs` | Yes | Yes |
| 22 | `20251214184156_AddTrialBalanceColumnsToFinancialAccount` | `AddTrialBalanceColumnsToFinancialAccount` | Yes | Yes |
| 23 | `20251221141054_AddEcommerceEntities` | `AddEcommerceEntities` | Yes | Yes |
| 24 | `20251221180334_AddMassTransitOutbox` | `AddMassTransitOutbox` | Yes | Yes |
| 25 | `20251221200148_AddMultiTenancyFoundation` | `AddMultiTenancyFoundation` | Yes | Yes |
| 26 | `20251224163945_AddPaymentsAndShipments` | `AddPaymentsAndShipments` | Yes | Yes |
| 27 | `20251224210903_AddStoreConfigs` | `AddStoreConfigs` | Yes | Yes |
| 28 | `20251226140322_TenantRegistration_Phase8` | `TenantRegistration_Phase8` | Yes | Yes |
| 29 | `20251226154845_EmailVerification_Phase84` | `EmailVerification_Phase84` | Yes | Yes |
| 30 | `20251226210606_TenantIsolationHardening` | `TenantIsolationHardening` | Yes | Yes |
| 31 | `20251226212911_AuditLogTenantId` | `AuditLogTenantId` | Yes | Yes |
| 32 | `20260102141736_AddOnlineCustomersTable` | `AddOnlineCustomersTable` | Yes | Yes |
| 33 | `20260104210031_AddOnlineCustomerWishlistItem` | `AddOnlineCustomerWishlistItem` | Yes | Yes |
| 34 | `20260104230000_AddOnlineCustomerWishlistItem` | `AddOnlineCustomerWishlistItem` | No | Yes |
| 35 | `20260105201157_AddOnlinePriceToProduct` | `AddOnlinePriceToProduct` | Yes | Yes |
| 36 | `20260108143906_AddShippingSnapshotFields` | `AddShippingSnapshotFields` | Yes | Yes |
| 37 | `20260108155753_AddTenantPaymentMethods` | `AddTenantPaymentMethods` | Yes | Yes |
| 38 | `20260109135134_AddEstimatedDeliveryDaysToOrder` | `AddEstimatedDeliveryDaysToOrder` | Yes | Yes |
| 39 | `20260109175711_AddOnlineCustomerIdToOrders` | `AddOnlineCustomerIdToOrders` | Yes | Yes |
| 40 | `20260111160400_FixCouponUniqueIndexToIncludeTenantId` | `FixCouponUniqueIndexToIncludeTenantId` | Yes | Yes |
| 41 | `20260111160634_FixCouponsTenantIdAndUniqueIndex` | `FixCouponsTenantIdAndUniqueIndex` | Yes | Yes |
| 42 | `20260111160851_FixCouponUniqueIndexToIncludeTenantId` | `FixCouponUniqueIndexToIncludeTenantId` | Yes | Yes |
| 43 | `20260112204103_AddProductVariantsPhase11_1` | `AddProductVariantsPhase11_1` | Yes | Yes |
| 44 | `20260113153904_AddVariantIdToOrderItemsPhase11_4_1` | `AddVariantIdToOrderItemsPhase11_4_1` | Yes | Yes |
| 45 | `20260113174500_AddOrderItemFinancialsPhase11_4_2A` | `AddOrderItemFinancialsPhase11_4_2A` | No | Yes |
| 46 | `20260113204319_AddHasVariantsToProduct` | `AddHasVariantsToProduct` | Yes | Yes |
| 47 | `20260115200647_AddStoreConfigOptimisticConcurrency` | `AddStoreConfigOptimisticConcurrency` | Yes | Yes |
| 48 | `20260115214438_AddCouponUsagesTable` | `AddCouponUsagesTable` | Yes | Yes |
| 49 | `20260116130637_AddPromotionsEngine` | `AddPromotionsEngine` | Yes | Yes |
| 50 | `20260116155940_AddOrderPromotionDiscountAmount` | `AddOrderPromotionDiscountAmount` | Yes | Yes |
| 51 | `20260116194740_AddPromotionBannerStyle` | `AddPromotionBannerStyle` | Yes | Yes |
| 52 | `20260117000000_AddPromotionSearchBoostOverride` | `AddPromotionSearchBoostOverride` | Yes | No |
| 53 | `20260118125347_AddPromotionRankingConfigToStoreConfig` | `AddPromotionRankingConfigToStoreConfig` | Yes | Yes |
| 54 | `20260120154936_AddTenantAnalyticsConfiguration` | `AddTenantAnalyticsConfiguration` | Yes | Yes |
| 55 | `20260121204129_AddCampaignCosts` | `AddCampaignCosts` | Yes | Yes |
| 56 | `20260122123451_AddShipmentOutForDeliveryTimestamp` | `AddShipmentOutForDeliveryTimestamp` | Yes | Yes |
| 57 | `20260122165616_AddMissingColumnsToCarrierConfigurations` | `AddMissingColumnsToCarrierConfigurations` | Yes | Yes |
| 58 | `20260128151641_ManualWalletCleanup` | `ManualWalletCleanup` | Yes | Yes |
| 59 | `20260129143905_AddWhatsAppMessaging` | `AddWhatsAppMessaging` | Yes | Yes |
| 60 | `20260129191429_WhatsAppTemplateHardening` | `WhatsAppTemplateHardening` | Yes | Yes |
| 61 | `20260204190605_AddMissingColumnsToTenantAutomationSettings` | `AddMissingColumnsToTenantAutomationSettings` | Yes | Yes |
| 62 | `20260204203053_AddWhatsAppRateLimitsTable` | `AddWhatsAppRateLimitsTable` | Yes | Yes |
| 63 | `20260206_AddPhase2ChartOfAccountsTreasury` | `AddPhase2ChartOfAccountsTreasury` | No | Yes |
| 64 | `20260206165348_AddCoreLedgerEngine` | `AddCoreLedgerEngine` | Yes | Yes |
| 65 | `20260208180000_BlockLegacyCashboxVisaWrites` | `BlockLegacyCashboxVisaWrites` | Yes | No |
| 66 | `20260213225438_AddServiceRequestCompositeIndexes` | `AddServiceRequestCompositeIndexes` | Yes | Yes |
| 67 | `20260220154537_AddAffiliateUsers_Retry` | `AddAffiliateUsers_Retry` | Yes | Yes |
| 68 | `20260220160244_RemoveLegacyAffiliateUserFK` | `RemoveLegacyAffiliateUserFK` | Yes | Yes |
| 69 | `20260223214810_AF16D_AddStorefrontBaseUrlToTenant` | `AF16D_AddStorefrontBaseUrlToTenant` | Yes | Yes |
| 70 | `20260224175620_AF19_GlobalMarketplaceAffiliate` | `AF19_GlobalMarketplaceAffiliate` | Yes | Yes |
| 71 | `20260224214800_AF16D_AddStorefrontBaseUrlToTenant` | `AF16D_AddStorefrontBaseUrlToTenant` | No | Yes |
| 72 | `20260225182859_AffiliateAuditIndexFixes` | `AffiliateAuditIndexFixes` | Yes | Yes |
| 73 | `20260226192814_AddTemplateTypeToAffiliateProductPolicy` | `AddTemplateTypeToAffiliateProductPolicy` | Yes | Yes |
| 74 | `20260227125054_AF30A_FIX_D2_EnabledRequiresCategory` | `AF30A_FIX_D2_EnabledRequiresCategory` | Yes | Yes |
| 75 | `20260227220116_AF38_HOTFIX_Constraints_Update` | `AF38_HOTFIX_Constraints_Update` | Yes | Yes |
| 76 | `20260312135219_AddCheckoutSessions` | `AddCheckoutSessions` | Yes | Yes |
| 77 | `20260312165800_AddCheckoutSessionImprovements` | `AddCheckoutSessionImprovements` | No | Yes |
| 78 | `20260318205107_AddCommerceHubCheckoutTables` | `AddCommerceHubCheckoutTables` | Yes | Yes |
| 79 | `20260318223800_AddCommerceHubGlobalCategories` | `AddCommerceHubGlobalCategories` | Yes | Yes |
| 80 | `20260331132612_AddMarketplaceProductReviews` | `AddMarketplaceProductReviews` | Yes | Yes |
| 81 | `20260407134756_AddSalesRequestIdempotency` | `AddSalesRequestIdempotency` | Yes | Yes |
| 82 | `20260408103000_AddPurchaseInvoiceSequence` | `AddPurchaseInvoiceSequence` | Yes | Yes |
| 83 | `20260408120000_AddSalesIdempotencyClaimToken` | `AddSalesIdempotencyClaimToken` | Yes | Yes |
| 84 | `20260408153000_AddExpenseAccountingMutationVersion` | `AddExpenseAccountingMutationVersion` | Yes | Yes |
| 85 | `20260408193000_AddSupplierPaymentsAndPartyLedgerAdjustments` | `AddSupplierPaymentsAndPartyLedgerAdjustments` | Yes | Yes |
| 86 | `20260408212030_SyncSnapshot` | `SyncSnapshot` | Yes | Yes |
| 87 | `20260409164612_AddReliableDocumentNumberSequences` | `AddReliableDocumentNumberSequences` | Yes | Yes |
| 88 | `20260409190921_AddDashboardPerformanceIndexes` | `AddDashboardPerformanceIndexes` | Yes | Yes |
| 89 | `20260409210000_AddTenantScopedOrderNumberIndex` | `AddTenantScopedOrderNumberIndex` | No | Yes |
| 90 | `20260410120000_HardenSupplierOpeningBalanceIdempotency` | `HardenSupplierOpeningBalanceIdempotency` | Yes | Yes |
| 91 | `20260411125058_FixOutboxSchemaDrift` | `FixOutboxSchemaDrift` | Yes | Yes |
| 92 | `20260411160000_AddStockTransferRequestIdempotency` | `AddStockTransferRequestIdempotency` | Yes | Yes |
| 93 | `20260415103000_AddOrderRequestIdempotency` | `AddOrderRequestIdempotency` | Yes | Yes |
| 94 | `20260415194100_AddDiscountValueGuards` | `AddDiscountValueGuards` | Yes | Yes |
| 95 | `20260415202828_AddCouponNormalizedCodeColumn` | `AddCouponNormalizedCodeColumn` | Yes | Yes |
| 96 | `20260415221500_AddCouponCustomerUsageUniquenessGuard` | `AddCouponCustomerUsageUniquenessGuard` | Yes | Yes |
| 97 | `20260416003507_FixSmartOfferCouponCodeWhitespaceConstraints` | `FixSmartOfferCouponCodeWhitespaceConstraints` | Yes | No |
| 98 | `20260417155219_AddPaymentWebhookLogUniqueConstraint` | `AddPaymentWebhookLogUniqueConstraint` | Yes | No |
| 99 | `20260419113000_AddOrderRequestIdempotencySnapshotHash` | `AddOrderRequestIdempotencySnapshotHash` | Yes | No |
| 100 | `20260419175541_AddCountryToCheckoutSessions` | `AddCountryToCheckoutSessions` | Yes | No |
| 101 | `20260419190000_AddCheckoutSessionManualWalletRecovery` | `AddCheckoutSessionManualWalletRecovery` | Yes | No |
| 102 | `20260419222000_AddPaymentIntentActiveProviderIdempotency` | `AddPaymentIntentActiveProviderIdempotency` | No | Yes |
| 103 | `20260420103000_NormalizeManualWalletProofReferences` | `NormalizeManualWalletProofReferences` | Yes | No |
| 104 | `20260420114500_AddStockSyncOutboxReliability` | `AddStockSyncOutboxReliability` | Yes | No |
| 105 | `20260420223926_S12A_AuthorizationWebhookTrust` | `S12A_AuthorizationWebhookTrust` | Yes | No |
| 106 | `20260421000000_RedactShippingWebhookPII` | `RedactShippingWebhookPII` | Yes | No |
| 107 | `20260421000128_AddStockSyncOutboxMissingColumns` | `AddStockSyncOutboxMissingColumns` | Yes | No |
| 108 | `20260421125431_S12B1_CheckoutSessionConsistency` | `S12B1_CheckoutSessionConsistency` | Yes | No |
| 109 | `20260421125905_S12C_ShippingWebhookTrackingNumberNullable` | `S12C_ShippingWebhookTrackingNumberNullable` | Yes | No |
| 110 | `20260421162000_S12B3_RecoveryTrackingCompleteness` | `S12B3_RecoveryTrackingCompleteness` | Yes | Yes |
| 111 | `20260421165825_S13B_PaymentEventReliabilityCompletion` | `S13B_PaymentEventReliabilityCompletion` | Yes | Yes |
| 112 | `20260422133434_S14A_CqrsAttachmentSecurity` | `S14A_CqrsAttachmentSecurity` | Yes | No |
| 113 | `20260422190000_S15A_CheckoutIdempotencyCartConcurrency` | `S15A_CheckoutIdempotencyCartConcurrency` | Yes | No |
| 114 | `20260422203000_S15B_PromotionCouponCorrectness` | `S15B_PromotionCouponCorrectness` | Yes | No |
| 115 | `20260422224500_S16A_ShipmentCheckoutSafety` | `S16A_ShipmentCheckoutSafety` | Yes | Yes |
| 116 | `20260423093000_S17_SettlementIntegrity` | `S17_SettlementIntegrity` | Yes | Yes |
| 117 | `20260423110000_S19_CheckoutDomainSimplification` | `S19_CheckoutDomainSimplification` | Yes | No |
| 118 | `20260423173000_S21_DiscountAuthorityLifecycleFix` | `S21_DiscountAuthorityLifecycleFix` | Yes | No |
| 119 | `20260423193000_S19B_PromotionFallbackAndManualCurrencyReview` | `S19B_PromotionFallbackAndManualCurrencyReview` | Yes | Yes |
| 120 | `20260423200000_S19C_AddPromotionFallbackFields` | `S19C_AddPromotionFallbackFields` | Yes | Yes |
| 121 | `20260424090000_S22_OrderRefundLifecycleHardening` | `S22_OrderRefundLifecycleHardening` | Yes | No |
| 122 | `20260424100000_S23_OrderRefundLineAmountNonNegative` | `S23_OrderRefundLineAmountNonNegative` | Yes | No |
| 123 | `20260424150000_S28_PosSalesAccountingMutationVersion` | `S28_PosSalesAccountingMutationVersion` | Yes | Yes |
| 124 | `20260424153000_S29_SalesUpdateIdempotencyRequestHashRecovery` | `S29_SalesUpdateIdempotencyRequestHashRecovery` | No | Yes |
| 125 | `20260424160000_S30_MarketplaceProjectionMutationVersions` | `S30_MarketplaceProjectionMutationVersions` | Yes | Yes |
| 126 | `20260424163000_S31_CanonicalCrossSystemIdentityLinks` | `S31_CanonicalCrossSystemIdentityLinks` | No | Yes |
| 127 | `20260424170000_S32_AccountingOutboxProcessingLeases` | `S32_AccountingOutboxProcessingLeases` | No | Yes |
| 128 | `20260424173000_S33_PayoutCompletionEvidenceFingerprint` | `S33_PayoutCompletionEvidenceFingerprint` | No | Yes |
| 129 | `20260424183000_S35_EventOrderingBoundaryHardening` | `S35_EventOrderingBoundaryHardening` | No | Yes |
| 130 | `20260424190000_S36_FinancialIntegrityBoundaryHardening` | `S36_FinancialIntegrityBoundaryHardening` | No | Yes |
| 131 | `20260426120000_S36_TenantCurrencyRequired` | `S36_TenantCurrencyRequired` | Yes | Yes |
| 132 | `20260426212746_S40_AddCurrencyReferenceTable_EF` | `S40_AddCurrencyReferenceTable_EF` | Yes | Yes |
| 133 | `20260426212800_AddPaymentIntentClientSecretEncryptedColumn` | `AddPaymentIntentClientSecretEncryptedColumn` | Yes | Yes |
| 134 | `20260426212900_AddCanonicalIdentityLinksEfWrapper` | `AddCanonicalIdentityLinksEfWrapper` | Yes | No |
| 135 | `20260427123057_S41_ReconcilePendingModelChanges` | `S41_ReconcilePendingModelChanges` | Yes | No |
| 136 | `20260427170000_S42_CommerceHubLedgerFingerprintIdempotency` | `S42_CommerceHubLedgerFingerprintIdempotency` | Yes | Yes |
| 137 | `20260427183000_S43_CommerceHubPaymentProviderIdentityUniqueness` | `S43_CommerceHubPaymentProviderIdentityUniqueness` | No | Yes |
| 138 | `20260428143000_S46_CommerceHubPaymentCompensationOutbox` | `S46_CommerceHubPaymentCompensationOutbox` | Yes | No |
| 139 | `20260428160000_S47_AccountingOutboxSourceType` | `S47_AccountingOutboxSourceType` | Yes | Yes |
| 140 | `20260428160001_S47b_AccountingOutboxSourceTypeConstraints` | `S47b_AccountingOutboxSourceTypeConstraints` | Yes | No |
| 141 | `20260428173000_S48_ComHubEarningPayoutAllocation` | `S48_ComHubEarningPayoutAllocation` | Yes | No |
| 142 | `20260429100000_S49_CrossSystemCriticalRiskClosure` | `S49_CrossSystemCriticalRiskClosure` | Yes | Yes |
| 143 | `20260429110000_S50_DataAuthorityViolationClosure` | `S50_DataAuthorityViolationClosure` | Yes | Yes |
| 144 | `20260429120000_S51_FixAffectedStoreIdsJsonColumnType` | `S51_FixAffectedStoreIdsJsonColumnType` | Yes | No |
| 145 | `20260429123000_S51_CrossSystemCriticalRiskHardening` | `S51_CrossSystemCriticalRiskHardening` | No | Yes |
| 146 | `20260429130000_S52_CleanupOldCartOwnerIndex` | `S52_CleanupOldCartOwnerIndex` | Yes | No |
| 147 | `20260429133000_S52_DataAuthorityBoundaryHardening` | `S52_DataAuthorityBoundaryHardening` | No | Yes |
| 148 | `20260429143000_S54_EventOrderingHardening` | `S54_EventOrderingHardening` | No | Yes |
| 149 | `20260429150000_S55_CrossSystemCriticalRiskClosure` | `S55_CrossSystemCriticalRiskClosure` | No | Yes |
| 150 | `20260429153000_S56_DataAuthorityAndEventOrderingClosure` | `S56_DataAuthorityAndEventOrderingClosure` | No | Yes |
| 151 | `20260430100000_S60_CommerceHubOrderAggregateVersionHardening` | `S60_CommerceHubOrderAggregateVersionHardening` | No | Yes |
| 152 | `20260430110000_S61_DataAuthorityAndEventScopeClosure` | `S61_DataAuthorityAndEventScopeClosure` | No | Yes |
| 153 | `20260430120000_S62_AffiliateAuthorityAndEventOrderingClosure` | `S62_AffiliateAuthorityAndEventOrderingClosure` | No | Yes |
| 154 | `20260501200756_S66_CommerceHubUnresolvedPaymentWebhooksUnfilteredUniqueness` | `S66_CommerceHubUnresolvedPaymentWebhooksUnfilteredUniqueness` | Yes | No |
| 155 | `20260501210054_S67_ReconcileCrossSystemRiskModel` | `S67_ReconcileCrossSystemRiskModel` | Yes | No |
| 156 | `20260504120000_S70_AdminStockAdjustmentDurableIdempotency` | `S70_AdminStockAdjustmentDurableIdempotency` | Yes | No |
| 157 | `20260506110000_S72_CrossSystemCriticalRiskCompensationSnapshots` | `S72_CrossSystemCriticalRiskCompensationSnapshots` | Yes | No |
| 158 | `20260508143000_S77_CommerceHubProviderRefundAuthorityConvergence` | `S77_CommerceHubProviderRefundAuthorityConvergence` | Yes | No |
| 159 | `20260508170000_S78_PosPurchaseAccountingMutationVersion` | `S78_PosPurchaseAccountingMutationVersion` | Yes | Yes |
| 160 | `20260508215150_S75_ProductionAuthorityAuditPendingModelSync` | `S75_ProductionAuthorityAuditPendingModelSync` | Yes | No |
| 161 | `20260510130500_UpdateAffiliatePayoutTransferAllocationIdempotencyIndexToTenantScoped` | `UpdateAffiliatePayoutTransferAllocationIdempotencyIndexToTenantScoped` | Yes | No |
| 162 | `20260510190000_RestoreGlobalAffiliatePayoutEvidenceAuthority` | `RestoreGlobalAffiliatePayoutEvidenceAuthority` | Yes | No |
| 163 | `20260510210000_TenantScopedSystemSettingsAuthority` | `TenantScopedSystemSettingsAuthority` | Yes | Yes |
| 164 | `20260511143000_S79_CommerceHubCustomerSessionCutoff` | `S79_CommerceHubCustomerSessionCutoff` | Yes | No |
| 165 | `20260511150000_S80_AddTenantsCurrencyDefaultValue` | `S80_AddTenantsCurrencyDefaultValue` | Yes | Yes |
| 166 | `20260516090000_StorefrontPromotionScopeReliabilityHardening` | `StorefrontPromotionScopeReliabilityHardening` | Yes | No |
| 167 | `20260517120000_PosSaleImmutableCostBasisSnapshots` | `PosSaleImmutableCostBasisSnapshots` | Yes | No |
| 168 | `20260518120000_VariantAttributeCombinationAuthority` | `VariantAttributeCombinationAuthority` | Yes | No |
| 169 | `20260519153000_VariantMediaSqlOwnershipAuthority` | `VariantMediaSqlOwnershipAuthority` | Yes | No |
| 170 | `20260519183000_VariantDbInvariantAuthority` | `VariantDbInvariantAuthority` | Yes | No |
| 171 | `20260522143000_VariantMergeSplitSemanticAuthority` | `VariantMergeSplitSemanticAuthority` | Yes | No |
| 172 | `20260525120000_PaymentCancellationOutboxProcessingLease` | `PaymentCancellationOutboxProcessingLease` | Yes | No |
| 173 | `20260531134000_CommerceHubPriceOverridePersistenceAudit` | `CommerceHubPriceOverridePersistenceAudit` | Yes | No |
| 174 | `20260531170000_ActivateCommerceHubPriceOverrides` | `ActivateCommerceHubPriceOverrides` | Yes | No |
| 175 | `20260601153000_CommerceHubForensicEvidenceHardening` | `CommerceHubForensicEvidenceHardening` | Yes | No |
| 176 | `20260601190000_EvidenceDatabaseHardening` | `EvidenceDatabaseHardening` | Yes | No |
| 177 | `20260601213000_CommerceHubCustomerIdentityLifecycle` | `CommerceHubCustomerIdentityLifecycle` | Yes | No |
| 178 | `20260603120000_AffiliateIdentityAuthorityHardening` | `AffiliateIdentityAuthorityHardening` | Yes | No |
| 179 | `20260605120000_AffiliatePayoutWalletSnapshotAuthority` | `AffiliatePayoutWalletSnapshotAuthority` | Yes | No |
| 180 | `20260607190000_PosProductAuthorityHardening` | `PosProductAuthorityHardening` | Yes | No |
| 181 | `20260607210000_PosWarehouseInventoryAuthorityHardening` | `PosWarehouseInventoryAuthorityHardening` | Yes | No |
| 182 | `20260607230000_PosPurchasingAuthorityHardening` | `PosPurchasingAuthorityHardening` | Yes | No |
| 183 | `20260608120000_PosCustomerSupplierAuthorityHardening` | `PosCustomerSupplierAuthorityHardening` | Yes | No |
| 184 | `20260608150000_StorefrontOrderAuthorityHardening` | `StorefrontOrderAuthorityHardening` | Yes | No |
| 185 | `20260609180000_StorefrontThemeAuthorityHardening` | `StorefrontThemeAuthorityHardening` | Yes | No |
| 186 | `20260609210000_StorefrontWhatsAppAuthorityHardening` | `StorefrontWhatsAppAuthorityHardening` | Yes | No |
| 187 | `20260610140000_AffiliatePayoutVerifiedEvidenceAuthority` | `AffiliatePayoutVerifiedEvidenceAuthority` | Yes | No |
| 188 | `20260610170000_CommerceHubVendorAuthorityHardening` | `CommerceHubVendorAuthorityHardening` | Yes | No |
| 189 | `20260610193000_CommerceHubMarketplaceOrderAuthorityHardening` | `CommerceHubMarketplaceOrderAuthorityHardening` | Yes | No |
| 190 | `20260611120000_ServiceMarketplaceRequestAuthorityHardening` | `ServiceMarketplaceRequestAuthorityHardening` | Yes | No |
| 191 | `20260611203933_ServiceMarketplaceRatingAuthorityHardening` | `ServiceMarketplaceRatingAuthorityHardening` | Yes | No |
| 192 | `20260612120000_TreasuryCashAuthorityHardening` | `TreasuryCashAuthorityHardening` | Yes | No |
| 193 | `20260623120000_CommerceHubOrderCurrencyAuthorityHardening` | `CommerceHubOrderCurrencyAuthorityHardening` | Yes | No |
| 194 | `20260623130000_CommerceHubPaymentCurrencyAuthorityHardening` | `CommerceHubPaymentCurrencyAuthorityHardening` | Yes | No |
| 195 | `20260623140000_CommerceHubRefundReturnCurrencyAuthorityHardening` | `CommerceHubRefundReturnCurrencyAuthorityHardening` | Yes | No |
| 196 | `20260623150000_CommerceHubVendorEarningsCurrencyAuthorityHardening` | `CommerceHubVendorEarningsCurrencyAuthorityHardening` | Yes | No |
| 197 | `20260705170009_InitialGeographicRuntimeFoundation` | `InitialGeographicRuntimeFoundation` | Yes | No |
| 198 | `20260705183000_Geo8OrderShipmentGeographicPersistence` | `Geo8OrderShipmentGeographicPersistence` | Yes | No |
| 199 | `20260705200000_Geo9CarrierGeographicResolution` | `Geo9CarrierGeographicResolution` | Yes | No |
| 200 | `20260706120000_ShipOrigin1CanonicalShipmentOrigin` | `ShipOrigin1CanonicalShipmentOrigin` | Yes | No |
| 201 | `20260706143000_Customs2EvidenceRuntimeFoundation` | `Customs2EvidenceRuntimeFoundation` | Yes | No |
| 202 | `20260706210000_Customs4AdministrationRuntimeWorkflow` | `Customs4AdministrationRuntimeWorkflow` | Yes | No |
| 203 | `20260712160000_StripeContractClosure2WebhookReplay` | `StripeContractClosure2WebhookReplay` | Yes | No |
| 204 | `20260713120000_PaymobContractClosure2WebhookReplay` | `PaymobContractClosure2WebhookReplay` | Yes | No |
| 205 | `20260715120000_FawryContractClosure2WebhookReplay` | `FawryContractClosure2WebhookReplay` | Yes | No |
| 206 | `20260721120000_TenantStatusLifecycleAuthority` | `TenantStatusLifecycleAuthority` | Yes | No |
| 207 | `20260721121000_AffiliateRefreshReplaySentinel` | `AffiliateRefreshReplaySentinel` | Yes | No |
| 208 | `20260721122000_AffiliateRefreshTokenReplayLedger` | `AffiliateRefreshTokenReplayLedger` | Yes | No |
| 209 | `20260721130000_ProductIdentityTenantScopeHardening` | `ProductIdentityTenantScopeHardening` | Yes | No |
| 210 | `20260722120000_AddProviderDeliveryOutbox` | `AddProviderDeliveryOutbox` | Yes | No |
| 211 | `20260722130000_HardenProviderDeliveryOutbox` | `HardenProviderDeliveryOutbox` | Yes | No |

## 4. Database History

Read-only SQL executed by temporary reader outside the repository: `SELECT "MigrationId", "ProductVersion" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";`.

| Order | MigrationId | ProductVersion |
|---:|---|---|
| 1 | `20251123190510_InitialCreate` | `9.0.1` |
| 2 | `20251126172001_AddCashboxEntities` | `9.0.1` |
| 3 | `20251126175326_MakeCashboxWarehouseOptional` | `9.0.1` |
| 4 | `20251126213231_AddAccountLedgerAndSOA` | `9.0.1` |
| 5 | `20251126221658_AddStockTransfer` | `9.0.1` |
| 6 | `20251127185031_AddSalesRepresentatives` | `9.0.1` |
| 7 | `20251128175638_AddFinancialAccounts` | `9.0.1` |
| 8 | `20251201201129_AddCashboxOpeningBalanceFields` | `9.0.1` |
| 9 | `20251201203400_AddCustomerOpeningBalanceFields` | `9.0.1` |
| 10 | `20251202144522_AddSupplierOpeningBalanceFields` | `9.0.1` |
| 11 | `20251202152548_AddOpeningBalanceFieldsToStockMovement` | `9.0.1` |
| 12 | `20251202174743_AddBanksTable` | `9.0.1` |
| 13 | `20251202190721_AddBankTransactions` | `9.0.1` |
| 14 | `20251202203358_AddExpensesManagement` | `9.0.1` |
| 15 | `20251204131000_AddTreasuryTransactions` | `9.0.1` |
| 16 | `20251204192040_AddVisaTransactions` | `9.0.1` |
| 17 | `20251205125248_AddMultiUnitSupport` | `9.0.1` |
| 18 | `20251205180157_AddDueDateToPurchaseInvoices` | `9.0.1` |
| 19 | `20251209172937_UpdateVisaAccountName` | `9.0.1` |
| 20 | `20251209173809_FixVisaAccountIsDeleted` | `9.0.1` |
| 21 | `20251211181712_AddAuditLogs` | `9.0.1` |
| 22 | `20251214184156_AddTrialBalanceColumnsToFinancialAccount` | `9.0.1` |
| 23 | `20251221141054_AddEcommerceEntities` | `9.0.1` |
| 24 | `20251221180334_AddMassTransitOutbox` | `9.0.1` |
| 25 | `20251221200148_AddMultiTenancyFoundation` | `9.0.1` |
| 26 | `20251224163945_AddPaymentsAndShipments` | `9.0.1` |
| 27 | `20251224210903_AddStoreConfigs` | `9.0.1` |
| 28 | `20251226140322_TenantRegistration_Phase8` | `9.0.1` |
| 29 | `20251226154845_EmailVerification_Phase84` | `9.0.1` |
| 30 | `20251226210606_TenantIsolationHardening` | `9.0.1` |
| 31 | `20251226212911_AuditLogTenantId` | `9.0.1` |
| 32 | `20260102141736_AddOnlineCustomersTable` | `9.0.1` |
| 33 | `20260104210031_AddOnlineCustomerWishlistItem` | `9.0.1` |
| 34 | `20260104230000_AddOnlineCustomerWishlistItem` | `9.0.1` |
| 35 | `20260105201157_AddOnlinePriceToProduct` | `9.0.1` |
| 36 | `20260108143906_AddShippingSnapshotFields` | `9.0.1` |
| 37 | `20260108155753_AddTenantPaymentMethods` | `9.0.1` |
| 38 | `20260109135134_AddEstimatedDeliveryDaysToOrder` | `9.0.1` |
| 39 | `20260109175711_AddOnlineCustomerIdToOrders` | `9.0.1` |
| 40 | `20260111160400_FixCouponUniqueIndexToIncludeTenantId` | `9.0.1` |
| 41 | `20260111160634_FixCouponsTenantIdAndUniqueIndex` | `9.0.1` |
| 42 | `20260111160851_FixCouponUniqueIndexToIncludeTenantId` | `9.0.1` |
| 43 | `20260112204103_AddProductVariantsPhase11_1` | `9.0.1` |
| 44 | `20260113153904_AddVariantIdToOrderItemsPhase11_4_1` | `9.0.1` |
| 45 | `20260113174500_AddOrderItemFinancialsPhase11_4_2A` | `9.0.1` |
| 46 | `20260113204319_AddHasVariantsToProduct` | `9.0.1` |
| 47 | `20260115200647_AddStoreConfigOptimisticConcurrency` | `9.0.1` |
| 48 | `20260115214438_AddCouponUsagesTable` | `9.0.1` |
| 49 | `20260116130637_AddPromotionsEngine` | `9.0.1` |
| 50 | `20260116155940_AddOrderPromotionDiscountAmount` | `9.0.1` |
| 51 | `20260116194740_AddPromotionBannerStyle` | `9.0.1` |
| 52 | `20260118125347_AddPromotionRankingConfigToStoreConfig` | `9.0.1` |
| 53 | `20260120154936_AddTenantAnalyticsConfiguration` | `9.0.1` |
| 54 | `20260121204129_AddCampaignCosts` | `9.0.1` |
| 55 | `20260122123451_AddShipmentOutForDeliveryTimestamp` | `9.0.1` |
| 56 | `20260122165616_AddMissingColumnsToCarrierConfigurations` | `9.0.1` |
| 57 | `20260128151641_ManualWalletCleanup` | `9.0.1` |
| 58 | `20260129143905_AddWhatsAppMessaging` | `9.0.1` |
| 59 | `20260129191429_WhatsAppTemplateHardening` | `9.0.1` |
| 60 | `20260204190605_AddMissingColumnsToTenantAutomationSettings` | `9.0.1` |
| 61 | `20260204203053_AddWhatsAppRateLimitsTable` | `9.0.1` |
| 62 | `20260206_AddPhase2ChartOfAccountsTreasury` | `9.0.1` |
| 63 | `20260206165348_AddCoreLedgerEngine` | `9.0.1` |
| 64 | `20260213225438_AddServiceRequestCompositeIndexes` | `9.0.1` |
| 65 | `20260220154537_AddAffiliateUsers_Retry` | `9.0.1` |
| 66 | `20260220160244_RemoveLegacyAffiliateUserFK` | `9.0.1` |
| 67 | `20260223214810_AF16D_AddStorefrontBaseUrlToTenant` | `9.0.1` |
| 68 | `20260224175620_AF19_GlobalMarketplaceAffiliate` | `9.0.1` |
| 69 | `20260224214800_AF16D_AddStorefrontBaseUrlToTenant` | `9.0.1` |
| 70 | `20260225182859_AffiliateAuditIndexFixes` | `9.0.1` |
| 71 | `20260226192814_AddTemplateTypeToAffiliateProductPolicy` | `9.0.1` |
| 72 | `20260227125054_AF30A_FIX_D2_EnabledRequiresCategory` | `9.0.1` |
| 73 | `20260227220116_AF38_HOTFIX_Constraints_Update` | `9.0.1` |
| 74 | `20260312135219_AddCheckoutSessions` | `9.0.1` |
| 75 | `20260312165800_AddCheckoutSessionImprovements` | `9.0.1` |
| 76 | `20260318205107_AddCommerceHubCheckoutTables` | `9.0.1` |
| 77 | `20260318223800_AddCommerceHubGlobalCategories` | `9.0.1` |
| 78 | `20260331132612_AddMarketplaceProductReviews` | `9.0.1` |
| 79 | `20260407134756_AddSalesRequestIdempotency` | `9.0.1` |
| 80 | `20260408103000_AddPurchaseInvoiceSequence` | `9.0.1` |
| 81 | `20260408120000_AddSalesIdempotencyClaimToken` | `8.0.0` |
| 82 | `20260408153000_AddExpenseAccountingMutationVersion` | `9.0.1` |
| 83 | `20260408193000_AddSupplierPaymentsAndPartyLedgerAdjustments` | `9.0.1` |
| 84 | `20260408212030_SyncSnapshot` | `9.0.1` |
| 85 | `20260409164612_AddReliableDocumentNumberSequences` | `9.0.1` |
| 86 | `20260409190921_AddDashboardPerformanceIndexes` | `9.0.1` |
| 87 | `20260409210000_AddTenantScopedOrderNumberIndex` | `9.0.1` |
| 88 | `20260410120000_HardenSupplierOpeningBalanceIdempotency` | `8.0.0` |
| 89 | `20260411125058_FixOutboxSchemaDrift` | `9.0.1` |
| 90 | `20260411160000_AddStockTransferRequestIdempotency` | `9.0.1` |
| 91 | `20260415103000_AddOrderRequestIdempotency` | `9.0.1` |
| 92 | `20260415194100_AddDiscountValueGuards` | `9.0.1` |
| 93 | `20260415202828_AddCouponNormalizedCodeColumn` | `9.0.1` |
| 94 | `20260415221500_AddCouponCustomerUsageUniquenessGuard` | `9.0.1` |
| 95 | `20260419222000_AddPaymentIntentActiveProviderIdempotency` | `9.0.1` |
| 96 | `20260421162000_S12B3_RecoveryTrackingCompleteness` | `9.0.1` |
| 97 | `20260421165825_S13B_PaymentEventReliabilityCompletion` | `9.0.1` |
| 98 | `20260422224500_S16A_ShipmentCheckoutSafety` | `9.0.0` |
| 99 | `20260423093000_S17_SettlementIntegrity` | `9.0.1` |
| 100 | `20260423193000_S19B_PromotionFallbackAndManualCurrencyReview` | `9.0.1` |
| 101 | `20260423200000_S19C_AddPromotionFallbackFields` | `9.0.1` |
| 102 | `20260424150000_S28_PosSalesAccountingMutationVersion` | `9.0.1` |
| 103 | `20260424153000_S29_SalesUpdateIdempotencyRequestHashRecovery` | `9.0.1` |
| 104 | `20260424160000_S30_MarketplaceProjectionMutationVersions` | `9.0.1` |
| 105 | `20260424163000_S31_CanonicalCrossSystemIdentityLinks` | `9.0.1` |
| 106 | `20260424170000_S32_AccountingOutboxProcessingLeases` | `9.0.1` |
| 107 | `20260424173000_S33_PayoutCompletionEvidenceFingerprint` | `9.0.1` |
| 108 | `20260424183000_S35_EventOrderingBoundaryHardening` | `9.0.1` |
| 109 | `20260424190000_S36_FinancialIntegrityBoundaryHardening` | `9.0.1` |
| 110 | `20260426120000_S36_TenantCurrencyRequired` | `9.0.1` |
| 111 | `20260426212746_S40_AddCurrencyReferenceTable_EF` | `9.0.1` |
| 112 | `20260426212800_AddPaymentIntentClientSecretEncryptedColumn` | `9.0.1` |
| 113 | `20260427170000_S42_CommerceHubLedgerFingerprintIdempotency` | `9.0.1` |
| 114 | `20260427183000_S43_CommerceHubPaymentProviderIdentityUniqueness` | `9.0.1` |
| 115 | `20260428160000_S47_AccountingOutboxSourceType` | `9.0.1` |
| 116 | `20260429100000_S49_CrossSystemCriticalRiskClosure` | `9.0.1` |
| 117 | `20260429110000_S50_DataAuthorityViolationClosure` | `9.0.1` |
| 118 | `20260429123000_S51_CrossSystemCriticalRiskHardening` | `9.0.1` |
| 119 | `20260429133000_S52_DataAuthorityBoundaryHardening` | `9.0.1` |
| 120 | `20260429143000_S54_EventOrderingHardening` | `9.0.1` |
| 121 | `20260429150000_S55_CrossSystemCriticalRiskClosure` | `9.0.1` |
| 122 | `20260429153000_S56_DataAuthorityAndEventOrderingClosure` | `9.0.1` |
| 123 | `20260430100000_S60_CommerceHubOrderAggregateVersionHardening` | `9.0.1` |
| 124 | `20260430110000_S61_DataAuthorityAndEventScopeClosure` | `9.0.1` |
| 125 | `20260430120000_S62_AffiliateAuthorityAndEventOrderingClosure` | `9.0.1` |
| 126 | `20260508170000_S78_PosPurchaseAccountingMutationVersion` | `9.0.1` |
| 127 | `20260510210000_TenantScopedSystemSettingsAuthority` | `9.0.1` |
| 128 | `20260511150000_S80_AddTenantsCurrencyDefaultValue` | `9.0.1` |

No duplicate `MigrationId` rows were detected in `__EFMigrationsHistory`.

## 5. Pending Migrations

Pending migrations reported by exact command: `dotnet ef migrations list --no-build --context ApplicationDbContext --startup-project ..\PointOfSale.API --project .` from `PointOfSale.Infrastructure` with `ASPNETCORE_ENVIRONMENT=Development`.

- `20260419175541_AddCountryToCheckoutSessions`
- `20260419190000_AddCheckoutSessionManualWalletRecovery`
- `20260420103000_NormalizeManualWalletProofReferences`
- `20260420223926_S12A_AuthorizationWebhookTrust`
- `20260421000000_RedactShippingWebhookPII`
- `20260421125431_S12B1_CheckoutSessionConsistency`
- `20260421125905_S12C_ShippingWebhookTrackingNumberNullable`
- `20260422133434_S14A_CqrsAttachmentSecurity`
- `20260422190000_S15A_CheckoutIdempotencyCartConcurrency`
- `20260423110000_S19_CheckoutDomainSimplification`
- `20260424090000_S22_OrderRefundLifecycleHardening`
- `20260424100000_S23_OrderRefundLineAmountNonNegative`
- `20260426212900_AddCanonicalIdentityLinksEfWrapper`
- `20260427123057_S41_ReconcilePendingModelChanges`
- `20260428143000_S46_CommerceHubPaymentCompensationOutbox`
- `20260428160001_S47b_AccountingOutboxSourceTypeConstraints`
- `20260428173000_S48_ComHubEarningPayoutAllocation`
- `20260429130000_S52_CleanupOldCartOwnerIndex`
- `20260501200756_S66_CommerceHubUnresolvedPaymentWebhooksUnfilteredUniqueness`
- `20260501210054_S67_ReconcileCrossSystemRiskModel`
- `20260506110000_S72_CrossSystemCriticalRiskCompensationSnapshots`
- `20260508143000_S77_CommerceHubProviderRefundAuthorityConvergence`
- `20260510130500_UpdateAffiliatePayoutTransferAllocationIdempotencyIndexToTenantScoped`
- `20260516090000_StorefrontPromotionScopeReliabilityHardening`
- `20260517120000_PosSaleImmutableCostBasisSnapshots`
- `20260525120000_PaymentCancellationOutboxProcessingLease`
- `20260531134000_CommerceHubPriceOverridePersistenceAudit`
- `20260531170000_ActivateCommerceHubPriceOverrides`
- `20260705170009_InitialGeographicRuntimeFoundation`
- `20260705183000_Geo8OrderShipmentGeographicPersistence`
- `20260705200000_Geo9CarrierGeographicResolution`
- `20260706120000_ShipOrigin1CanonicalShipmentOrigin`
- `20260706143000_Customs2EvidenceRuntimeFoundation`
- `20260706210000_Customs4AdministrationRuntimeWorkflow`
- `20260712160000_StripeContractClosure2WebhookReplay`
- `20260713120000_PaymobContractClosure2WebhookReplay`
- `20260715120000_FawryContractClosure2WebhookReplay`
- `20260721120000_TenantStatusLifecycleAuthority`
- `20260721121000_AffiliateRefreshReplaySentinel`
- `20260721122000_AffiliateRefreshTokenReplayLedger`

## 6. Synchronization Analysis

**Database is not fully synchronized.** Evidence: EF CLI marked 40 migrations as pending, and the direct database history query did not contain those IDs.

**Code ahead of database.** Physical migration files not recorded in database history:
- `20260117000000_AddPromotionSearchBoostOverride` (`PointOfSale.Infrastructure\Data\Migrations\20260117000000_AddPromotionSearchBoostOverride.cs`)
- `20260208180000_BlockLegacyCashboxVisaWrites` (`PointOfSale.Infrastructure\Data\Migrations\20260208180000_BlockLegacyCashboxVisaWrites.cs`)
- `20260416003507_FixSmartOfferCouponCodeWhitespaceConstraints` (`PointOfSale.Infrastructure\Migrations\20260416003507_FixSmartOfferCouponCodeWhitespaceConstraints.cs`)
- `20260417155219_AddPaymentWebhookLogUniqueConstraint` (`PointOfSale.Infrastructure\Migrations\20260417155219_AddPaymentWebhookLogUniqueConstraint.cs`)
- `20260419113000_AddOrderRequestIdempotencySnapshotHash` (`PointOfSale.Infrastructure\Migrations\20260419113000_AddOrderRequestIdempotencySnapshotHash.cs`)
- `20260419175541_AddCountryToCheckoutSessions` (`PointOfSale.Infrastructure\Data\Migrations\20260419175541_AddCountryToCheckoutSessions.cs`)
- `20260419190000_AddCheckoutSessionManualWalletRecovery` (`PointOfSale.Infrastructure\Migrations\20260419190000_AddCheckoutSessionManualWalletRecovery.cs`)
- `20260420103000_NormalizeManualWalletProofReferences` (`PointOfSale.Infrastructure\Migrations\20260420103000_NormalizeManualWalletProofReferences.cs`)
- `20260420114500_AddStockSyncOutboxReliability` (`PointOfSale.Infrastructure\Migrations\20260420114500_AddStockSyncOutboxReliability.cs`)
- `20260420223926_S12A_AuthorizationWebhookTrust` (`PointOfSale.Infrastructure\Migrations\20260420223926_S12A_AuthorizationWebhookTrust.cs`)
- `20260421000000_RedactShippingWebhookPII` (`PointOfSale.Infrastructure\Migrations\20260421000000_RedactShippingWebhookPII.cs`)
- `20260421000128_AddStockSyncOutboxMissingColumns` (`PointOfSale.Infrastructure\Migrations\20260421000128_AddStockSyncOutboxMissingColumns.cs`)
- `20260421125431_S12B1_CheckoutSessionConsistency` (`PointOfSale.Infrastructure\Migrations\20260421125431_S12B1_CheckoutSessionConsistency.cs`)
- `20260421125905_S12C_ShippingWebhookTrackingNumberNullable` (`PointOfSale.Infrastructure\Migrations\20260421125905_S12C_ShippingWebhookTrackingNumberNullable.cs`)
- `20260422133434_S14A_CqrsAttachmentSecurity` (`PointOfSale.Infrastructure\Migrations\20260422133434_S14A_CqrsAttachmentSecurity.cs`)
- `20260422190000_S15A_CheckoutIdempotencyCartConcurrency` (`PointOfSale.Infrastructure\Migrations\20260422190000_S15A_CheckoutIdempotencyCartConcurrency.cs`)
- `20260422203000_S15B_PromotionCouponCorrectness` (`PointOfSale.Infrastructure\Migrations\20260422203000_S15B_PromotionCouponCorrectness.cs`)
- `20260423110000_S19_CheckoutDomainSimplification` (`PointOfSale.Infrastructure\Migrations\20260423110000_S19_CheckoutDomainSimplification.cs`)
- `20260423173000_S21_DiscountAuthorityLifecycleFix` (`PointOfSale.Infrastructure\Migrations\20260423173000_S21_DiscountAuthorityLifecycleFix.cs`)
- `20260424090000_S22_OrderRefundLifecycleHardening` (`PointOfSale.Infrastructure\Migrations\20260424090000_S22_OrderRefundLifecycleHardening.cs`)
- `20260424100000_S23_OrderRefundLineAmountNonNegative` (`PointOfSale.Infrastructure\Migrations\20260424100000_S23_OrderRefundLineAmountNonNegative.cs`)
- `20260426212900_AddCanonicalIdentityLinksEfWrapper` (`PointOfSale.Infrastructure\Migrations\20260426212900_AddCanonicalIdentityLinksEfWrapper.cs`)
- `20260427123057_S41_ReconcilePendingModelChanges` (`PointOfSale.Infrastructure\Migrations\20260427123057_S41_ReconcilePendingModelChanges.cs`)
- `20260428143000_S46_CommerceHubPaymentCompensationOutbox` (`PointOfSale.Infrastructure\Migrations\20260428143000_S46_CommerceHubPaymentCompensationOutbox.cs`)
- `20260428160001_S47b_AccountingOutboxSourceTypeConstraints` (`PointOfSale.Infrastructure\Migrations\20260428160001_S47b_AccountingOutboxSourceTypeConstraints.cs`)
- `20260428173000_S48_ComHubEarningPayoutAllocation` (`PointOfSale.Infrastructure\Migrations\20260428173000_S48_ComHubEarningPayoutAllocation.cs`)
- `20260429120000_S51_FixAffectedStoreIdsJsonColumnType` (`PointOfSale.Infrastructure\Migrations\20260429120000_S51_FixAffectedStoreIdsJsonColumnType.cs`)
- `20260429130000_S52_CleanupOldCartOwnerIndex` (`PointOfSale.Infrastructure\Migrations\20260429130000_S52_CleanupOldCartOwnerIndex.cs`)
- `20260501200756_S66_CommerceHubUnresolvedPaymentWebhooksUnfilteredUniqueness` (`PointOfSale.Infrastructure\Migrations\20260501200756_S66_CommerceHubUnresolvedPaymentWebhooksUnfilteredUniqueness.cs`)
- `20260501210054_S67_ReconcileCrossSystemRiskModel` (`PointOfSale.Infrastructure\Migrations\20260501210054_S67_ReconcileCrossSystemRiskModel.cs`)
- `20260504120000_S70_AdminStockAdjustmentDurableIdempotency` (`PointOfSale.Infrastructure\Migrations\20260504120000_S70_AdminStockAdjustmentDurableIdempotency.cs`)
- `20260506110000_S72_CrossSystemCriticalRiskCompensationSnapshots` (`PointOfSale.Infrastructure\Migrations\20260506110000_S72_CrossSystemCriticalRiskCompensationSnapshots.cs`)
- `20260508143000_S77_CommerceHubProviderRefundAuthorityConvergence` (`PointOfSale.Infrastructure\Migrations\20260508143000_S77_CommerceHubProviderRefundAuthorityConvergence.cs`)
- `20260508215150_S75_ProductionAuthorityAuditPendingModelSync` (`PointOfSale.Infrastructure\Migrations\20260508215150_S75_ProductionAuthorityAuditPendingModelSync.cs`)
- `20260510130500_UpdateAffiliatePayoutTransferAllocationIdempotencyIndexToTenantScoped` (`PointOfSale.Infrastructure\Migrations\20260510130500_UpdateAffiliatePayoutTransferAllocationIdempotencyIndexToTenantScoped.cs`)
- `20260510190000_RestoreGlobalAffiliatePayoutEvidenceAuthority` (`PointOfSale.Infrastructure\Migrations\20260510190000_RestoreGlobalAffiliatePayoutEvidenceAuthority.cs`)
- `20260511143000_S79_CommerceHubCustomerSessionCutoff` (`PointOfSale.Infrastructure\Migrations\20260511143000_S79_CommerceHubCustomerSessionCutoff.cs`)
- `20260516090000_StorefrontPromotionScopeReliabilityHardening` (`PointOfSale.Infrastructure\Migrations\20260516090000_StorefrontPromotionScopeReliabilityHardening.cs`)
- `20260517120000_PosSaleImmutableCostBasisSnapshots` (`PointOfSale.Infrastructure\Migrations\20260517120000_PosSaleImmutableCostBasisSnapshots.cs`)
- `20260518120000_VariantAttributeCombinationAuthority` (`PointOfSale.Infrastructure\Migrations\20260518120000_VariantAttributeCombinationAuthority.cs`)
- `20260519153000_VariantMediaSqlOwnershipAuthority` (`PointOfSale.Infrastructure\Migrations\20260519153000_VariantMediaSqlOwnershipAuthority.cs`)
- `20260519183000_VariantDbInvariantAuthority` (`PointOfSale.Infrastructure\Migrations\20260519183000_VariantDbInvariantAuthority.cs`)
- `20260522143000_VariantMergeSplitSemanticAuthority` (`PointOfSale.Infrastructure\Migrations\20260522143000_VariantMergeSplitSemanticAuthority.cs`)
- `20260525120000_PaymentCancellationOutboxProcessingLease` (`PointOfSale.Infrastructure\Migrations\20260525120000_PaymentCancellationOutboxProcessingLease.cs`)
- `20260531134000_CommerceHubPriceOverridePersistenceAudit` (`PointOfSale.Infrastructure\Migrations\20260531134000_CommerceHubPriceOverridePersistenceAudit.cs`)
- `20260531170000_ActivateCommerceHubPriceOverrides` (`PointOfSale.Infrastructure\Migrations\20260531170000_ActivateCommerceHubPriceOverrides.cs`)
- `20260601153000_CommerceHubForensicEvidenceHardening` (`PointOfSale.Infrastructure\Migrations\20260601153000_CommerceHubForensicEvidenceHardening.cs`)
- `20260601190000_EvidenceDatabaseHardening` (`PointOfSale.Infrastructure\Migrations\20260601190000_EvidenceDatabaseHardening.cs`)
- `20260601213000_CommerceHubCustomerIdentityLifecycle` (`PointOfSale.Infrastructure\Migrations\20260601213000_CommerceHubCustomerIdentityLifecycle.cs`)
- `20260603120000_AffiliateIdentityAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260603120000_AffiliateIdentityAuthorityHardening.cs`)
- `20260605120000_AffiliatePayoutWalletSnapshotAuthority` (`PointOfSale.Infrastructure\Migrations\20260605120000_AffiliatePayoutWalletSnapshotAuthority.cs`)
- `20260607190000_PosProductAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260607190000_PosProductAuthorityHardening.cs`)
- `20260607210000_PosWarehouseInventoryAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260607210000_PosWarehouseInventoryAuthorityHardening.cs`)
- `20260607230000_PosPurchasingAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260607230000_PosPurchasingAuthorityHardening.cs`)
- `20260608120000_PosCustomerSupplierAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260608120000_PosCustomerSupplierAuthorityHardening.cs`)
- `20260608150000_StorefrontOrderAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260608150000_StorefrontOrderAuthorityHardening.cs`)
- `20260609180000_StorefrontThemeAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260609180000_StorefrontThemeAuthorityHardening.cs`)
- `20260609210000_StorefrontWhatsAppAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260609210000_StorefrontWhatsAppAuthorityHardening.cs`)
- `20260610140000_AffiliatePayoutVerifiedEvidenceAuthority` (`PointOfSale.Infrastructure\Migrations\20260610140000_AffiliatePayoutVerifiedEvidenceAuthority.cs`)
- `20260610170000_CommerceHubVendorAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260610170000_CommerceHubVendorAuthorityHardening.cs`)
- `20260610193000_CommerceHubMarketplaceOrderAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260610193000_CommerceHubMarketplaceOrderAuthorityHardening.cs`)
- `20260611120000_ServiceMarketplaceRequestAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260611120000_ServiceMarketplaceRequestAuthorityHardening.cs`)
- `20260611203933_ServiceMarketplaceRatingAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260611203933_ServiceMarketplaceRatingAuthorityHardening.cs`)
- `20260612120000_TreasuryCashAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260612120000_TreasuryCashAuthorityHardening.cs`)
- `20260623120000_CommerceHubOrderCurrencyAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260623120000_CommerceHubOrderCurrencyAuthorityHardening.cs`)
- `20260623130000_CommerceHubPaymentCurrencyAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260623130000_CommerceHubPaymentCurrencyAuthorityHardening.cs`)
- `20260623140000_CommerceHubRefundReturnCurrencyAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260623140000_CommerceHubRefundReturnCurrencyAuthorityHardening.cs`)
- `20260623150000_CommerceHubVendorEarningsCurrencyAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260623150000_CommerceHubVendorEarningsCurrencyAuthorityHardening.cs`)
- `20260705170009_InitialGeographicRuntimeFoundation` (`PointOfSale.Infrastructure\Migrations\20260705170009_InitialGeographicRuntimeFoundation.cs`)
- `20260705183000_Geo8OrderShipmentGeographicPersistence` (`PointOfSale.Infrastructure\Migrations\20260705183000_Geo8OrderShipmentGeographicPersistence.cs`)
- `20260705200000_Geo9CarrierGeographicResolution` (`PointOfSale.Infrastructure\Migrations\20260705200000_Geo9CarrierGeographicResolution.cs`)
- `20260706120000_ShipOrigin1CanonicalShipmentOrigin` (`PointOfSale.Infrastructure\Migrations\20260706120000_ShipOrigin1CanonicalShipmentOrigin.cs`)
- `20260706143000_Customs2EvidenceRuntimeFoundation` (`PointOfSale.Infrastructure\Migrations\20260706143000_Customs2EvidenceRuntimeFoundation.cs`)
- `20260706210000_Customs4AdministrationRuntimeWorkflow` (`PointOfSale.Infrastructure\Migrations\20260706210000_Customs4AdministrationRuntimeWorkflow.cs`)
- `20260712160000_StripeContractClosure2WebhookReplay` (`PointOfSale.Infrastructure\Migrations\20260712160000_StripeContractClosure2WebhookReplay.cs`)
- `20260713120000_PaymobContractClosure2WebhookReplay` (`PointOfSale.Infrastructure\Migrations\20260713120000_PaymobContractClosure2WebhookReplay.cs`)
- `20260715120000_FawryContractClosure2WebhookReplay` (`PointOfSale.Infrastructure\Migrations\20260715120000_FawryContractClosure2WebhookReplay.cs`)
- `20260721120000_TenantStatusLifecycleAuthority` (`PointOfSale.Infrastructure\Migrations\20260721120000_TenantStatusLifecycleAuthority.cs`)
- `20260721121000_AffiliateRefreshReplaySentinel` (`PointOfSale.Infrastructure\Migrations\20260721121000_AffiliateRefreshReplaySentinel.cs`)
- `20260721122000_AffiliateRefreshTokenReplayLedger` (`PointOfSale.Infrastructure\Migrations\20260721122000_AffiliateRefreshTokenReplayLedger.cs`)
- `20260721130000_ProductIdentityTenantScopeHardening` (`PointOfSale.Infrastructure\Migrations\20260721130000_ProductIdentityTenantScopeHardening.cs`)
- `20260722120000_AddProviderDeliveryOutbox` (`PointOfSale.Infrastructure\Migrations\20260722120000_AddProviderDeliveryOutbox.cs`)
- `20260722130000_HardenProviderDeliveryOutbox` (`PointOfSale.Infrastructure\Migrations\20260722130000_HardenProviderDeliveryOutbox.cs`)

**Database ahead of current physical code.** Database history rows with no matching physical migration `.cs` file:
- `20260104230000_AddOnlineCustomerWishlistItem` ProductVersion `9.0.1`
- `20260113174500_AddOrderItemFinancialsPhase11_4_2A` ProductVersion `9.0.1`
- `20260206_AddPhase2ChartOfAccountsTreasury` ProductVersion `9.0.1`
- `20260224214800_AF16D_AddStorefrontBaseUrlToTenant` ProductVersion `9.0.1`
- `20260312165800_AddCheckoutSessionImprovements` ProductVersion `9.0.1`
- `20260409210000_AddTenantScopedOrderNumberIndex` ProductVersion `9.0.1`
- `20260419222000_AddPaymentIntentActiveProviderIdempotency` ProductVersion `9.0.1`
- `20260424153000_S29_SalesUpdateIdempotencyRequestHashRecovery` ProductVersion `9.0.1`
- `20260424163000_S31_CanonicalCrossSystemIdentityLinks` ProductVersion `9.0.1`
- `20260424170000_S32_AccountingOutboxProcessingLeases` ProductVersion `9.0.1`
- `20260424173000_S33_PayoutCompletionEvidenceFingerprint` ProductVersion `9.0.1`
- `20260424183000_S35_EventOrderingBoundaryHardening` ProductVersion `9.0.1`
- `20260424190000_S36_FinancialIntegrityBoundaryHardening` ProductVersion `9.0.1`
- `20260427183000_S43_CommerceHubPaymentProviderIdentityUniqueness` ProductVersion `9.0.1`
- `20260429123000_S51_CrossSystemCriticalRiskHardening` ProductVersion `9.0.1`
- `20260429133000_S52_DataAuthorityBoundaryHardening` ProductVersion `9.0.1`
- `20260429143000_S54_EventOrderingHardening` ProductVersion `9.0.1`
- `20260429150000_S55_CrossSystemCriticalRiskClosure` ProductVersion `9.0.1`
- `20260429153000_S56_DataAuthorityAndEventOrderingClosure` ProductVersion `9.0.1`
- `20260430100000_S60_CommerceHubOrderAggregateVersionHardening` ProductVersion `9.0.1`
- `20260430110000_S61_DataAuthorityAndEventScopeClosure` ProductVersion `9.0.1`
- `20260430120000_S62_AffiliateAuthorityAndEventOrderingClosure` ProductVersion `9.0.1`

**Database ahead of EF CLI-recognized assembly.** Database history rows not recognized by `dotnet ef migrations list --no-build`:
- `20260104230000_AddOnlineCustomerWishlistItem` ProductVersion `9.0.1`
- `20260113174500_AddOrderItemFinancialsPhase11_4_2A` ProductVersion `9.0.1`
- `20260206_AddPhase2ChartOfAccountsTreasury` ProductVersion `9.0.1`
- `20260224214800_AF16D_AddStorefrontBaseUrlToTenant` ProductVersion `9.0.1`
- `20260312165800_AddCheckoutSessionImprovements` ProductVersion `9.0.1`
- `20260409210000_AddTenantScopedOrderNumberIndex` ProductVersion `9.0.1`
- `20260415221500_AddCouponCustomerUsageUniquenessGuard` ProductVersion `9.0.1`
- `20260419222000_AddPaymentIntentActiveProviderIdempotency` ProductVersion `9.0.1`
- `20260422224500_S16A_ShipmentCheckoutSafety` ProductVersion `9.0.0`
- `20260424153000_S29_SalesUpdateIdempotencyRequestHashRecovery` ProductVersion `9.0.1`
- `20260424163000_S31_CanonicalCrossSystemIdentityLinks` ProductVersion `9.0.1`
- `20260424170000_S32_AccountingOutboxProcessingLeases` ProductVersion `9.0.1`
- `20260424173000_S33_PayoutCompletionEvidenceFingerprint` ProductVersion `9.0.1`
- `20260424183000_S35_EventOrderingBoundaryHardening` ProductVersion `9.0.1`
- `20260424190000_S36_FinancialIntegrityBoundaryHardening` ProductVersion `9.0.1`
- `20260426120000_S36_TenantCurrencyRequired` ProductVersion `9.0.1`
- `20260427170000_S42_CommerceHubLedgerFingerprintIdempotency` ProductVersion `9.0.1`
- `20260427183000_S43_CommerceHubPaymentProviderIdentityUniqueness` ProductVersion `9.0.1`
- `20260429123000_S51_CrossSystemCriticalRiskHardening` ProductVersion `9.0.1`
- `20260429133000_S52_DataAuthorityBoundaryHardening` ProductVersion `9.0.1`
- `20260429143000_S54_EventOrderingHardening` ProductVersion `9.0.1`
- `20260429150000_S55_CrossSystemCriticalRiskClosure` ProductVersion `9.0.1`
- `20260429153000_S56_DataAuthorityAndEventOrderingClosure` ProductVersion `9.0.1`
- `20260430100000_S60_CommerceHubOrderAggregateVersionHardening` ProductVersion `9.0.1`
- `20260430110000_S61_DataAuthorityAndEventScopeClosure` ProductVersion `9.0.1`
- `20260430120000_S62_AffiliateAuthorityAndEventOrderingClosure` ProductVersion `9.0.1`
- `20260511150000_S80_AddTenantsCurrencyDefaultValue` ProductVersion `9.0.1`

**EF CLI-recognized migrations missing from database.** This matches the pending list:
- `20260419175541_AddCountryToCheckoutSessions`
- `20260419190000_AddCheckoutSessionManualWalletRecovery`
- `20260420103000_NormalizeManualWalletProofReferences`
- `20260420223926_S12A_AuthorizationWebhookTrust`
- `20260421000000_RedactShippingWebhookPII`
- `20260421125431_S12B1_CheckoutSessionConsistency`
- `20260421125905_S12C_ShippingWebhookTrackingNumberNullable`
- `20260422133434_S14A_CqrsAttachmentSecurity`
- `20260422190000_S15A_CheckoutIdempotencyCartConcurrency`
- `20260423110000_S19_CheckoutDomainSimplification`
- `20260424090000_S22_OrderRefundLifecycleHardening`
- `20260424100000_S23_OrderRefundLineAmountNonNegative`
- `20260426212900_AddCanonicalIdentityLinksEfWrapper`
- `20260427123057_S41_ReconcilePendingModelChanges`
- `20260428143000_S46_CommerceHubPaymentCompensationOutbox`
- `20260428160001_S47b_AccountingOutboxSourceTypeConstraints`
- `20260428173000_S48_ComHubEarningPayoutAllocation`
- `20260429130000_S52_CleanupOldCartOwnerIndex`
- `20260501200756_S66_CommerceHubUnresolvedPaymentWebhooksUnfilteredUniqueness`
- `20260501210054_S67_ReconcileCrossSystemRiskModel`
- `20260506110000_S72_CrossSystemCriticalRiskCompensationSnapshots`
- `20260508143000_S77_CommerceHubProviderRefundAuthorityConvergence`
- `20260510130500_UpdateAffiliatePayoutTransferAllocationIdempotencyIndexToTenantScoped`
- `20260516090000_StorefrontPromotionScopeReliabilityHardening`
- `20260517120000_PosSaleImmutableCostBasisSnapshots`
- `20260525120000_PaymentCancellationOutboxProcessingLease`
- `20260531134000_CommerceHubPriceOverridePersistenceAudit`
- `20260531170000_ActivateCommerceHubPriceOverrides`
- `20260705170009_InitialGeographicRuntimeFoundation`
- `20260705183000_Geo8OrderShipmentGeographicPersistence`
- `20260705200000_Geo9CarrierGeographicResolution`
- `20260706120000_ShipOrigin1CanonicalShipmentOrigin`
- `20260706143000_Customs2EvidenceRuntimeFoundation`
- `20260706210000_Customs4AdministrationRuntimeWorkflow`
- `20260712160000_StripeContractClosure2WebhookReplay`
- `20260713120000_PaymobContractClosure2WebhookReplay`
- `20260715120000_FawryContractClosure2WebhookReplay`
- `20260721120000_TenantStatusLifecycleAuthority`
- `20260721121000_AffiliateRefreshReplaySentinel`
- `20260721122000_AffiliateRefreshTokenReplayLedger`

**Physical source not recognized by no-build EF CLI assembly.** This may indicate stale build artifacts or migrations not compiled into the current assembly used by `--no-build`; no corrective build was performed in-repo:
- `20260117000000_AddPromotionSearchBoostOverride` (`PointOfSale.Infrastructure\Data\Migrations\20260117000000_AddPromotionSearchBoostOverride.cs`)
- `20260208180000_BlockLegacyCashboxVisaWrites` (`PointOfSale.Infrastructure\Data\Migrations\20260208180000_BlockLegacyCashboxVisaWrites.cs`)
- `20260415221500_AddCouponCustomerUsageUniquenessGuard` (`PointOfSale.Infrastructure\Migrations\20260415221500_AddCouponCustomerUsageUniquenessGuard.cs`)
- `20260416003507_FixSmartOfferCouponCodeWhitespaceConstraints` (`PointOfSale.Infrastructure\Migrations\20260416003507_FixSmartOfferCouponCodeWhitespaceConstraints.cs`)
- `20260417155219_AddPaymentWebhookLogUniqueConstraint` (`PointOfSale.Infrastructure\Migrations\20260417155219_AddPaymentWebhookLogUniqueConstraint.cs`)
- `20260419113000_AddOrderRequestIdempotencySnapshotHash` (`PointOfSale.Infrastructure\Migrations\20260419113000_AddOrderRequestIdempotencySnapshotHash.cs`)
- `20260420114500_AddStockSyncOutboxReliability` (`PointOfSale.Infrastructure\Migrations\20260420114500_AddStockSyncOutboxReliability.cs`)
- `20260421000128_AddStockSyncOutboxMissingColumns` (`PointOfSale.Infrastructure\Migrations\20260421000128_AddStockSyncOutboxMissingColumns.cs`)
- `20260422203000_S15B_PromotionCouponCorrectness` (`PointOfSale.Infrastructure\Migrations\20260422203000_S15B_PromotionCouponCorrectness.cs`)
- `20260422224500_S16A_ShipmentCheckoutSafety` (`PointOfSale.Infrastructure\Migrations\20260422224500_S16A_ShipmentCheckoutSafety.cs`)
- `20260423173000_S21_DiscountAuthorityLifecycleFix` (`PointOfSale.Infrastructure\Migrations\20260423173000_S21_DiscountAuthorityLifecycleFix.cs`)
- `20260426120000_S36_TenantCurrencyRequired` (`PointOfSale.Infrastructure\Migrations\20260426120000_S36_TenantCurrencyRequired.cs`)
- `20260427170000_S42_CommerceHubLedgerFingerprintIdempotency` (`PointOfSale.Infrastructure\Migrations\20260427170000_S42_CommerceHubLedgerFingerprintIdempotency.cs`)
- `20260429120000_S51_FixAffectedStoreIdsJsonColumnType` (`PointOfSale.Infrastructure\Migrations\20260429120000_S51_FixAffectedStoreIdsJsonColumnType.cs`)
- `20260504120000_S70_AdminStockAdjustmentDurableIdempotency` (`PointOfSale.Infrastructure\Migrations\20260504120000_S70_AdminStockAdjustmentDurableIdempotency.cs`)
- `20260508215150_S75_ProductionAuthorityAuditPendingModelSync` (`PointOfSale.Infrastructure\Migrations\20260508215150_S75_ProductionAuthorityAuditPendingModelSync.cs`)
- `20260510190000_RestoreGlobalAffiliatePayoutEvidenceAuthority` (`PointOfSale.Infrastructure\Migrations\20260510190000_RestoreGlobalAffiliatePayoutEvidenceAuthority.cs`)
- `20260511143000_S79_CommerceHubCustomerSessionCutoff` (`PointOfSale.Infrastructure\Migrations\20260511143000_S79_CommerceHubCustomerSessionCutoff.cs`)
- `20260511150000_S80_AddTenantsCurrencyDefaultValue` (`PointOfSale.Infrastructure\Migrations\20260511150000_S80_AddTenantsCurrencyDefaultValue.cs`)
- `20260518120000_VariantAttributeCombinationAuthority` (`PointOfSale.Infrastructure\Migrations\20260518120000_VariantAttributeCombinationAuthority.cs`)
- `20260519153000_VariantMediaSqlOwnershipAuthority` (`PointOfSale.Infrastructure\Migrations\20260519153000_VariantMediaSqlOwnershipAuthority.cs`)
- `20260519183000_VariantDbInvariantAuthority` (`PointOfSale.Infrastructure\Migrations\20260519183000_VariantDbInvariantAuthority.cs`)
- `20260522143000_VariantMergeSplitSemanticAuthority` (`PointOfSale.Infrastructure\Migrations\20260522143000_VariantMergeSplitSemanticAuthority.cs`)
- `20260601153000_CommerceHubForensicEvidenceHardening` (`PointOfSale.Infrastructure\Migrations\20260601153000_CommerceHubForensicEvidenceHardening.cs`)
- `20260601190000_EvidenceDatabaseHardening` (`PointOfSale.Infrastructure\Migrations\20260601190000_EvidenceDatabaseHardening.cs`)
- `20260601213000_CommerceHubCustomerIdentityLifecycle` (`PointOfSale.Infrastructure\Migrations\20260601213000_CommerceHubCustomerIdentityLifecycle.cs`)
- `20260603120000_AffiliateIdentityAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260603120000_AffiliateIdentityAuthorityHardening.cs`)
- `20260605120000_AffiliatePayoutWalletSnapshotAuthority` (`PointOfSale.Infrastructure\Migrations\20260605120000_AffiliatePayoutWalletSnapshotAuthority.cs`)
- `20260607190000_PosProductAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260607190000_PosProductAuthorityHardening.cs`)
- `20260607210000_PosWarehouseInventoryAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260607210000_PosWarehouseInventoryAuthorityHardening.cs`)
- `20260607230000_PosPurchasingAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260607230000_PosPurchasingAuthorityHardening.cs`)
- `20260608120000_PosCustomerSupplierAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260608120000_PosCustomerSupplierAuthorityHardening.cs`)
- `20260608150000_StorefrontOrderAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260608150000_StorefrontOrderAuthorityHardening.cs`)
- `20260609180000_StorefrontThemeAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260609180000_StorefrontThemeAuthorityHardening.cs`)
- `20260609210000_StorefrontWhatsAppAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260609210000_StorefrontWhatsAppAuthorityHardening.cs`)
- `20260610140000_AffiliatePayoutVerifiedEvidenceAuthority` (`PointOfSale.Infrastructure\Migrations\20260610140000_AffiliatePayoutVerifiedEvidenceAuthority.cs`)
- `20260610170000_CommerceHubVendorAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260610170000_CommerceHubVendorAuthorityHardening.cs`)
- `20260610193000_CommerceHubMarketplaceOrderAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260610193000_CommerceHubMarketplaceOrderAuthorityHardening.cs`)
- `20260611120000_ServiceMarketplaceRequestAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260611120000_ServiceMarketplaceRequestAuthorityHardening.cs`)
- `20260611203933_ServiceMarketplaceRatingAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260611203933_ServiceMarketplaceRatingAuthorityHardening.cs`)
- `20260612120000_TreasuryCashAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260612120000_TreasuryCashAuthorityHardening.cs`)
- `20260623120000_CommerceHubOrderCurrencyAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260623120000_CommerceHubOrderCurrencyAuthorityHardening.cs`)
- `20260623130000_CommerceHubPaymentCurrencyAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260623130000_CommerceHubPaymentCurrencyAuthorityHardening.cs`)
- `20260623140000_CommerceHubRefundReturnCurrencyAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260623140000_CommerceHubRefundReturnCurrencyAuthorityHardening.cs`)
- `20260623150000_CommerceHubVendorEarningsCurrencyAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260623150000_CommerceHubVendorEarningsCurrencyAuthorityHardening.cs`)
- `20260721130000_ProductIdentityTenantScopeHardening` (`PointOfSale.Infrastructure\Migrations\20260721130000_ProductIdentityTenantScopeHardening.cs`)
- `20260722120000_AddProviderDeliveryOutbox` (`PointOfSale.Infrastructure\Migrations\20260722120000_AddProviderDeliveryOutbox.cs`)
- `20260722130000_HardenProviderDeliveryOutbox` (`PointOfSale.Infrastructure\Migrations\20260722130000_HardenProviderDeliveryOutbox.cs`)

## 7. Integrity Findings

- Missing history table: none. `HISTORY_TABLE_EXISTS` returned `True`.
- Duplicate database history rows: none detected by grouping 128 `MigrationId` rows.
- Duplicate physical migration IDs: none detected.
- Duplicate migration class name detected: `FixCouponUniqueIndexToIncludeTenantId` appears in `PointOfSale.Infrastructure/Migrations/20260111160400_FixCouponUniqueIndexToIncludeTenantId.cs` and `PointOfSale.Infrastructure/Data/Migrations/20260111160851_FixCouponUniqueIndexToIncludeTenantId.cs`.
- Timestamp ordering: 0 duplicate 14-digit timestamp prefixes detected among physical migration `.cs` IDs; lexical timestamp order is otherwise nondecreasing for the physical IDs inspected.
- Snapshot exists: `PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`; `[DbContext(typeof(ApplicationDbContext))]` at line 13 and `ProductVersion` annotation `9.0.1` at line 20.
- Snapshot/build verification: not certified. A redirected temp-output `dotnet build PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj` failed with duplicate assembly attribute errors from `PointOfSale.Core/obj/...` and temp `obj/...`; therefore this audit cannot certify that the current snapshot builds.
- Missing Designer files: 102 physical migration `.cs` files do not have a same-basename `.Designer.cs` file:
- `20251201203400_AddCustomerOpeningBalanceFields` (`PointOfSale.Infrastructure\Migrations\20251201203400_AddCustomerOpeningBalanceFields.cs`)
- `20251202174743_AddBanksTable` (`PointOfSale.Infrastructure\Migrations\20251202174743_AddBanksTable.cs`)
- `20251204131000_AddTreasuryTransactions` (`PointOfSale.Infrastructure\Migrations\20251204131000_AddTreasuryTransactions.cs`)
- `20260111160400_FixCouponUniqueIndexToIncludeTenantId` (`PointOfSale.Infrastructure\Migrations\20260111160400_FixCouponUniqueIndexToIncludeTenantId.cs`)
- `20260117000000_AddPromotionSearchBoostOverride` (`PointOfSale.Infrastructure\Data\Migrations\20260117000000_AddPromotionSearchBoostOverride.cs`)
- `20260208180000_BlockLegacyCashboxVisaWrites` (`PointOfSale.Infrastructure\Data\Migrations\20260208180000_BlockLegacyCashboxVisaWrites.cs`)
- `20260408103000_AddPurchaseInvoiceSequence` (`PointOfSale.Infrastructure\Migrations\20260408103000_AddPurchaseInvoiceSequence.cs`)
- `20260408120000_AddSalesIdempotencyClaimToken` (`PointOfSale.Infrastructure\Migrations\20260408120000_AddSalesIdempotencyClaimToken.cs`)
- `20260408153000_AddExpenseAccountingMutationVersion` (`PointOfSale.Infrastructure\Migrations\20260408153000_AddExpenseAccountingMutationVersion.cs`)
- `20260408193000_AddSupplierPaymentsAndPartyLedgerAdjustments` (`PointOfSale.Infrastructure\Migrations\20260408193000_AddSupplierPaymentsAndPartyLedgerAdjustments.cs`)
- `20260410120000_HardenSupplierOpeningBalanceIdempotency` (`PointOfSale.Infrastructure\Migrations\20260410120000_HardenSupplierOpeningBalanceIdempotency.cs`)
- `20260411160000_AddStockTransferRequestIdempotency` (`PointOfSale.Infrastructure\Migrations\20260411160000_AddStockTransferRequestIdempotency.cs`)
- `20260415103000_AddOrderRequestIdempotency` (`PointOfSale.Infrastructure\Migrations\20260415103000_AddOrderRequestIdempotency.cs`)
- `20260415221500_AddCouponCustomerUsageUniquenessGuard` (`PointOfSale.Infrastructure\Migrations\20260415221500_AddCouponCustomerUsageUniquenessGuard.cs`)
- `20260416003507_FixSmartOfferCouponCodeWhitespaceConstraints` (`PointOfSale.Infrastructure\Migrations\20260416003507_FixSmartOfferCouponCodeWhitespaceConstraints.cs`)
- `20260417155219_AddPaymentWebhookLogUniqueConstraint` (`PointOfSale.Infrastructure\Migrations\20260417155219_AddPaymentWebhookLogUniqueConstraint.cs`)
- `20260419113000_AddOrderRequestIdempotencySnapshotHash` (`PointOfSale.Infrastructure\Migrations\20260419113000_AddOrderRequestIdempotencySnapshotHash.cs`)
- `20260419190000_AddCheckoutSessionManualWalletRecovery` (`PointOfSale.Infrastructure\Migrations\20260419190000_AddCheckoutSessionManualWalletRecovery.cs`)
- `20260420103000_NormalizeManualWalletProofReferences` (`PointOfSale.Infrastructure\Migrations\20260420103000_NormalizeManualWalletProofReferences.cs`)
- `20260420114500_AddStockSyncOutboxReliability` (`PointOfSale.Infrastructure\Migrations\20260420114500_AddStockSyncOutboxReliability.cs`)
- `20260421000000_RedactShippingWebhookPII` (`PointOfSale.Infrastructure\Migrations\20260421000000_RedactShippingWebhookPII.cs`)
- `20260421000128_AddStockSyncOutboxMissingColumns` (`PointOfSale.Infrastructure\Migrations\20260421000128_AddStockSyncOutboxMissingColumns.cs`)
- `20260421162000_S12B3_RecoveryTrackingCompleteness` (`PointOfSale.Infrastructure\Migrations\20260421162000_S12B3_RecoveryTrackingCompleteness.cs`)
- `20260422190000_S15A_CheckoutIdempotencyCartConcurrency` (`PointOfSale.Infrastructure\Migrations\20260422190000_S15A_CheckoutIdempotencyCartConcurrency.cs`)
- `20260422203000_S15B_PromotionCouponCorrectness` (`PointOfSale.Infrastructure\Migrations\20260422203000_S15B_PromotionCouponCorrectness.cs`)
- `20260422224500_S16A_ShipmentCheckoutSafety` (`PointOfSale.Infrastructure\Migrations\20260422224500_S16A_ShipmentCheckoutSafety.cs`)
- `20260423093000_S17_SettlementIntegrity` (`PointOfSale.Infrastructure\Migrations\20260423093000_S17_SettlementIntegrity.cs`)
- `20260423110000_S19_CheckoutDomainSimplification` (`PointOfSale.Infrastructure\Migrations\20260423110000_S19_CheckoutDomainSimplification.cs`)
- `20260423173000_S21_DiscountAuthorityLifecycleFix` (`PointOfSale.Infrastructure\Migrations\20260423173000_S21_DiscountAuthorityLifecycleFix.cs`)
- `20260423193000_S19B_PromotionFallbackAndManualCurrencyReview` (`PointOfSale.Infrastructure\Migrations\20260423193000_S19B_PromotionFallbackAndManualCurrencyReview.cs`)
- `20260423200000_S19C_AddPromotionFallbackFields` (`PointOfSale.Infrastructure\Migrations\20260423200000_S19C_AddPromotionFallbackFields.cs`)
- `20260424090000_S22_OrderRefundLifecycleHardening` (`PointOfSale.Infrastructure\Migrations\20260424090000_S22_OrderRefundLifecycleHardening.cs`)
- `20260424100000_S23_OrderRefundLineAmountNonNegative` (`PointOfSale.Infrastructure\Migrations\20260424100000_S23_OrderRefundLineAmountNonNegative.cs`)
- `20260424150000_S28_PosSalesAccountingMutationVersion` (`PointOfSale.Infrastructure\Migrations\20260424150000_S28_PosSalesAccountingMutationVersion.cs`)
- `20260424160000_S30_MarketplaceProjectionMutationVersions` (`PointOfSale.Infrastructure\Migrations\20260424160000_S30_MarketplaceProjectionMutationVersions.cs`)
- `20260426120000_S36_TenantCurrencyRequired` (`PointOfSale.Infrastructure\Migrations\20260426120000_S36_TenantCurrencyRequired.cs`)
- `20260426212800_AddPaymentIntentClientSecretEncryptedColumn` (`PointOfSale.Infrastructure\Migrations\20260426212800_AddPaymentIntentClientSecretEncryptedColumn.cs`)
- `20260426212900_AddCanonicalIdentityLinksEfWrapper` (`PointOfSale.Infrastructure\Migrations\20260426212900_AddCanonicalIdentityLinksEfWrapper.cs`)
- `20260427170000_S42_CommerceHubLedgerFingerprintIdempotency` (`PointOfSale.Infrastructure\Migrations\20260427170000_S42_CommerceHubLedgerFingerprintIdempotency.cs`)
- `20260428143000_S46_CommerceHubPaymentCompensationOutbox` (`PointOfSale.Infrastructure\Migrations\20260428143000_S46_CommerceHubPaymentCompensationOutbox.cs`)
- `20260428160000_S47_AccountingOutboxSourceType` (`PointOfSale.Infrastructure\Migrations\20260428160000_S47_AccountingOutboxSourceType.cs`)
- `20260428160001_S47b_AccountingOutboxSourceTypeConstraints` (`PointOfSale.Infrastructure\Migrations\20260428160001_S47b_AccountingOutboxSourceTypeConstraints.cs`)
- `20260428173000_S48_ComHubEarningPayoutAllocation` (`PointOfSale.Infrastructure\Migrations\20260428173000_S48_ComHubEarningPayoutAllocation.cs`)
- `20260429100000_S49_CrossSystemCriticalRiskClosure` (`PointOfSale.Infrastructure\Migrations\20260429100000_S49_CrossSystemCriticalRiskClosure.cs`)
- `20260429110000_S50_DataAuthorityViolationClosure` (`PointOfSale.Infrastructure\Migrations\20260429110000_S50_DataAuthorityViolationClosure.cs`)
- `20260429120000_S51_FixAffectedStoreIdsJsonColumnType` (`PointOfSale.Infrastructure\Migrations\20260429120000_S51_FixAffectedStoreIdsJsonColumnType.cs`)
- `20260429130000_S52_CleanupOldCartOwnerIndex` (`PointOfSale.Infrastructure\Migrations\20260429130000_S52_CleanupOldCartOwnerIndex.cs`)
- `20260504120000_S70_AdminStockAdjustmentDurableIdempotency` (`PointOfSale.Infrastructure\Migrations\20260504120000_S70_AdminStockAdjustmentDurableIdempotency.cs`)
- `20260506110000_S72_CrossSystemCriticalRiskCompensationSnapshots` (`PointOfSale.Infrastructure\Migrations\20260506110000_S72_CrossSystemCriticalRiskCompensationSnapshots.cs`)
- `20260508143000_S77_CommerceHubProviderRefundAuthorityConvergence` (`PointOfSale.Infrastructure\Migrations\20260508143000_S77_CommerceHubProviderRefundAuthorityConvergence.cs`)
- `20260508170000_S78_PosPurchaseAccountingMutationVersion` (`PointOfSale.Infrastructure\Migrations\20260508170000_S78_PosPurchaseAccountingMutationVersion.cs`)
- `20260508215150_S75_ProductionAuthorityAuditPendingModelSync` (`PointOfSale.Infrastructure\Migrations\20260508215150_S75_ProductionAuthorityAuditPendingModelSync.cs`)
- `20260510190000_RestoreGlobalAffiliatePayoutEvidenceAuthority` (`PointOfSale.Infrastructure\Migrations\20260510190000_RestoreGlobalAffiliatePayoutEvidenceAuthority.cs`)
- `20260510210000_TenantScopedSystemSettingsAuthority` (`PointOfSale.Infrastructure\Migrations\20260510210000_TenantScopedSystemSettingsAuthority.cs`)
- `20260511143000_S79_CommerceHubCustomerSessionCutoff` (`PointOfSale.Infrastructure\Migrations\20260511143000_S79_CommerceHubCustomerSessionCutoff.cs`)
- `20260511150000_S80_AddTenantsCurrencyDefaultValue` (`PointOfSale.Infrastructure\Migrations\20260511150000_S80_AddTenantsCurrencyDefaultValue.cs`)
- `20260516090000_StorefrontPromotionScopeReliabilityHardening` (`PointOfSale.Infrastructure\Migrations\20260516090000_StorefrontPromotionScopeReliabilityHardening.cs`)
- `20260517120000_PosSaleImmutableCostBasisSnapshots` (`PointOfSale.Infrastructure\Migrations\20260517120000_PosSaleImmutableCostBasisSnapshots.cs`)
- `20260518120000_VariantAttributeCombinationAuthority` (`PointOfSale.Infrastructure\Migrations\20260518120000_VariantAttributeCombinationAuthority.cs`)
- `20260519153000_VariantMediaSqlOwnershipAuthority` (`PointOfSale.Infrastructure\Migrations\20260519153000_VariantMediaSqlOwnershipAuthority.cs`)
- `20260519183000_VariantDbInvariantAuthority` (`PointOfSale.Infrastructure\Migrations\20260519183000_VariantDbInvariantAuthority.cs`)
- `20260522143000_VariantMergeSplitSemanticAuthority` (`PointOfSale.Infrastructure\Migrations\20260522143000_VariantMergeSplitSemanticAuthority.cs`)
- `20260525120000_PaymentCancellationOutboxProcessingLease` (`PointOfSale.Infrastructure\Migrations\20260525120000_PaymentCancellationOutboxProcessingLease.cs`)
- `20260531134000_CommerceHubPriceOverridePersistenceAudit` (`PointOfSale.Infrastructure\Migrations\20260531134000_CommerceHubPriceOverridePersistenceAudit.cs`)
- `20260531170000_ActivateCommerceHubPriceOverrides` (`PointOfSale.Infrastructure\Migrations\20260531170000_ActivateCommerceHubPriceOverrides.cs`)
- `20260601153000_CommerceHubForensicEvidenceHardening` (`PointOfSale.Infrastructure\Migrations\20260601153000_CommerceHubForensicEvidenceHardening.cs`)
- `20260601190000_EvidenceDatabaseHardening` (`PointOfSale.Infrastructure\Migrations\20260601190000_EvidenceDatabaseHardening.cs`)
- `20260601213000_CommerceHubCustomerIdentityLifecycle` (`PointOfSale.Infrastructure\Migrations\20260601213000_CommerceHubCustomerIdentityLifecycle.cs`)
- `20260603120000_AffiliateIdentityAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260603120000_AffiliateIdentityAuthorityHardening.cs`)
- `20260605120000_AffiliatePayoutWalletSnapshotAuthority` (`PointOfSale.Infrastructure\Migrations\20260605120000_AffiliatePayoutWalletSnapshotAuthority.cs`)
- `20260607190000_PosProductAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260607190000_PosProductAuthorityHardening.cs`)
- `20260607210000_PosWarehouseInventoryAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260607210000_PosWarehouseInventoryAuthorityHardening.cs`)
- `20260607230000_PosPurchasingAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260607230000_PosPurchasingAuthorityHardening.cs`)
- `20260608120000_PosCustomerSupplierAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260608120000_PosCustomerSupplierAuthorityHardening.cs`)
- `20260608150000_StorefrontOrderAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260608150000_StorefrontOrderAuthorityHardening.cs`)
- `20260609180000_StorefrontThemeAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260609180000_StorefrontThemeAuthorityHardening.cs`)
- `20260609210000_StorefrontWhatsAppAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260609210000_StorefrontWhatsAppAuthorityHardening.cs`)
- `20260610140000_AffiliatePayoutVerifiedEvidenceAuthority` (`PointOfSale.Infrastructure\Migrations\20260610140000_AffiliatePayoutVerifiedEvidenceAuthority.cs`)
- `20260610170000_CommerceHubVendorAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260610170000_CommerceHubVendorAuthorityHardening.cs`)
- `20260610193000_CommerceHubMarketplaceOrderAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260610193000_CommerceHubMarketplaceOrderAuthorityHardening.cs`)
- `20260611120000_ServiceMarketplaceRequestAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260611120000_ServiceMarketplaceRequestAuthorityHardening.cs`)
- `20260611203933_ServiceMarketplaceRatingAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260611203933_ServiceMarketplaceRatingAuthorityHardening.cs`)
- `20260612120000_TreasuryCashAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260612120000_TreasuryCashAuthorityHardening.cs`)
- `20260623120000_CommerceHubOrderCurrencyAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260623120000_CommerceHubOrderCurrencyAuthorityHardening.cs`)
- `20260623130000_CommerceHubPaymentCurrencyAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260623130000_CommerceHubPaymentCurrencyAuthorityHardening.cs`)
- `20260623140000_CommerceHubRefundReturnCurrencyAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260623140000_CommerceHubRefundReturnCurrencyAuthorityHardening.cs`)
- `20260623150000_CommerceHubVendorEarningsCurrencyAuthorityHardening` (`PointOfSale.Infrastructure\Migrations\20260623150000_CommerceHubVendorEarningsCurrencyAuthorityHardening.cs`)
- `20260705170009_InitialGeographicRuntimeFoundation` (`PointOfSale.Infrastructure\Migrations\20260705170009_InitialGeographicRuntimeFoundation.cs`)
- `20260705183000_Geo8OrderShipmentGeographicPersistence` (`PointOfSale.Infrastructure\Migrations\20260705183000_Geo8OrderShipmentGeographicPersistence.cs`)
- `20260705200000_Geo9CarrierGeographicResolution` (`PointOfSale.Infrastructure\Migrations\20260705200000_Geo9CarrierGeographicResolution.cs`)
- `20260706120000_ShipOrigin1CanonicalShipmentOrigin` (`PointOfSale.Infrastructure\Migrations\20260706120000_ShipOrigin1CanonicalShipmentOrigin.cs`)
- `20260706143000_Customs2EvidenceRuntimeFoundation` (`PointOfSale.Infrastructure\Migrations\20260706143000_Customs2EvidenceRuntimeFoundation.cs`)
- `20260706210000_Customs4AdministrationRuntimeWorkflow` (`PointOfSale.Infrastructure\Migrations\20260706210000_Customs4AdministrationRuntimeWorkflow.cs`)
- `20260712160000_StripeContractClosure2WebhookReplay` (`PointOfSale.Infrastructure\Migrations\20260712160000_StripeContractClosure2WebhookReplay.cs`)
- `20260713120000_PaymobContractClosure2WebhookReplay` (`PointOfSale.Infrastructure\Migrations\20260713120000_PaymobContractClosure2WebhookReplay.cs`)
- `20260715120000_FawryContractClosure2WebhookReplay` (`PointOfSale.Infrastructure\Migrations\20260715120000_FawryContractClosure2WebhookReplay.cs`)
- `20260721120000_TenantStatusLifecycleAuthority` (`PointOfSale.Infrastructure\Migrations\20260721120000_TenantStatusLifecycleAuthority.cs`)
- `20260721121000_AffiliateRefreshReplaySentinel` (`PointOfSale.Infrastructure\Migrations\20260721121000_AffiliateRefreshReplaySentinel.cs`)
- `20260721122000_AffiliateRefreshTokenReplayLedger` (`PointOfSale.Infrastructure\Migrations\20260721122000_AffiliateRefreshTokenReplayLedger.cs`)
- `20260721130000_ProductIdentityTenantScopeHardening` (`PointOfSale.Infrastructure\Migrations\20260721130000_ProductIdentityTenantScopeHardening.cs`)
- `20260722120000_AddProviderDeliveryOutbox` (`PointOfSale.Infrastructure\Migrations\20260722120000_AddProviderDeliveryOutbox.cs`)
- `20260722130000_HardenProviderDeliveryOutbox` (`PointOfSale.Infrastructure\Migrations\20260722130000_HardenProviderDeliveryOutbox.cs`)

## 8. Risk Assessment

| Severity | Issue | Evidence | Engineering Risk |
|---|---|---|---|
| Critical | Database/application migration history divergence | 40 EF CLI pending migrations; 22 database rows with no physical `.cs`; 83 physical `.cs` files not in DB history. | Release cannot be certified as schema-history synchronized. EF migration operations may attempt unexpected deltas or fail. |
| High | Database contains unexpected migration history rows | Database-only rows include `20260104230000_AddOnlineCustomerWishlistItem`, `20260206_AddPhase2ChartOfAccountsTreasury`, and 20 others. | Indicates history was manually patched, migrations were deleted/renamed, or a different migration source was previously used. |
| High | Pending migrations detected by EF CLI | Exact pending list contains 40 migrations from `20260419175541_AddCountryToCheckoutSessions` through `20260721122000_AffiliateRefreshTokenReplayLedger`. | Application model may expect schema that the database has not recorded as applied. |
| Medium | Physical source and EF no-build assembly differ | 48 physical migration `.cs` files were not listed by `dotnet ef migrations list --no-build`. | Existing build artifacts may be stale or current source may not compile into the migration assembly used by EF tooling. |
| Medium | Missing Designer files | 102 migration `.cs` files have no matching `.Designer.cs`. | Violates the requested migration integrity criterion and reduces reliable snapshot attribution. |
| Medium | Duplicate migration class name | `FixCouponUniqueIndexToIncludeTenantId` class appears twice under different migration IDs/folders. | Class-name collision risk during compilation/tooling and human review ambiguity. |
| Medium | Snapshot build not certified | Redirected build failed with duplicate assembly attribute errors. | Current model snapshot compatibility could not be proven in this read-only audit. |

## 9. Final Certification

**FAIL.** The actual PostgreSQL database migration state is not fully synchronized with the application EF Core migration history.

Certification basis: direct database history (128 rows), EF CLI no-build output (141 migrations, 40 pending), physical source inventory (189 migration `.cs` files), and integrity checks show material divergence. No database updates, migration creation, migration deletion, migration reordering, or repair actions were performed.
