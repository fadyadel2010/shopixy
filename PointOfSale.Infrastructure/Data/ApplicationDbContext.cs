using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Customs;
using PointOfSale.Core.Entities.Geographic;
using PointOfSale.Core.Entities.ShippingAdministration;
using PointOfSale.Core.Financial;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.SuperAdmin.Models;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Validation;
using PointOfSale.Infrastructure.Accounting;
using MassTransit;

namespace PointOfSale.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    private readonly ITenantContext? _tenantContext;
    private readonly ILogger<ApplicationDbContext>? _logger;
    
    // Property for EF Core query filter expression (cannot use method call directly)
    private Guid? CurrentTenantId => _tenantContext?.TenantId;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }
    
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ITenantContext tenantContext) : base(options)
    {
        _tenantContext = tenantContext;
    }
    
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ITenantContext tenantContext, ILogger<ApplicationDbContext> logger) : base(options)
    {
        _tenantContext = tenantContext;
        _logger = logger;
    }

    // Master Data
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    public DbSet<CustomerAddressVersion> CustomerAddressVersions => Set<CustomerAddressVersion>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductMedia> ProductMedia => Set<ProductMedia>();
    public DbSet<HomepageMediaAsset> HomepageMediaAssets => Set<HomepageMediaAsset>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductVariantStock> ProductVariantStocks => Set<ProductVariantStock>();
    public DbSet<ProductVariantAttribute> ProductVariantAttributes => Set<ProductVariantAttribute>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<ProductUnit> ProductUnits => Set<ProductUnit>();
    public DbSet<CommerceHubListing> CommerceHubListings => Set<CommerceHubListing>();
    public DbSet<CommerceHubMerchantLifecycleLedgerEntry> CommerceHubMerchantLifecycleLedgerEntries => Set<CommerceHubMerchantLifecycleLedgerEntry>();
    public DbSet<CommerceHubRecoveryOperationLedgerEntry> CommerceHubRecoveryOperationLedgerEntries => Set<CommerceHubRecoveryOperationLedgerEntry>();
    public DbSet<CommissionRule> CommissionRules => Set<CommissionRule>();
    public DbSet<PromoCode> PromoCodes => Set<PromoCode>();
    public DbSet<PromoCodeUsage> PromoCodeUsages => Set<PromoCodeUsage>();
    public DbSet<CommerceHubCustomer> CommerceHubCustomers => Set<CommerceHubCustomer>();
    public DbSet<RevokedCustomerAccessToken> RevokedCustomerAccessTokens => Set<RevokedCustomerAccessToken>();
    public DbSet<CommerceHubCustomerPasswordResetToken> CommerceHubCustomerPasswordResetTokens => Set<CommerceHubCustomerPasswordResetToken>();
    public DbSet<CommerceHubCustomerEmailVerificationToken> CommerceHubCustomerEmailVerificationTokens => Set<CommerceHubCustomerEmailVerificationToken>();
    public DbSet<CommerceHubCustomerIdentityEvent> CommerceHubCustomerIdentityEvents => Set<CommerceHubCustomerIdentityEvent>();
    public DbSet<RevokedStorefrontAccessToken> RevokedStorefrontAccessTokens => Set<RevokedStorefrontAccessToken>();
    public DbSet<CustomerPayoutDetails> CustomerPayoutDetails => Set<CustomerPayoutDetails>();

    // Global CommerceHub Categories (NOT tenant-scoped — platform-controlled)
    public DbSet<CommerceHubCategory> CommerceHubCategories => Set<CommerceHubCategory>();
    public DbSet<CommerceHubCategoryAttribute> CommerceHubCategoryAttributes => Set<CommerceHubCategoryAttribute>();
    public DbSet<CommerceHubSearchClick> CommerceHubSearchClicks => Set<CommerceHubSearchClick>();
    public DbSet<CommerceHubSearchImpression> CommerceHubSearchImpressions => Set<CommerceHubSearchImpression>();
    public DbSet<CommerceHubSearchFilterEvent> CommerceHubSearchFilterEvents => Set<CommerceHubSearchFilterEvent>();
    public DbSet<CommerceHubSearchQueryLog> CommerceHubSearchQueries => Set<CommerceHubSearchQueryLog>();
    public DbSet<CommerceHubSearchSynonym> CommerceHubSearchSynonyms => Set<CommerceHubSearchSynonym>();
    public DbSet<ProductReview> ProductReviews => Set<ProductReview>();
    public DbSet<HomepageSection> HomepageSections => Set<HomepageSection>();
    public DbSet<CmsPage> CmsPages => Set<CmsPage>();
    public DbSet<CmsPageTranslation> CmsPageTranslations => Set<CmsPageTranslation>();
    public DbSet<MarketplaceCmsPage> MarketplaceCmsPages => Set<MarketplaceCmsPage>();
    public DbSet<MarketplaceCategoryTranslation> MarketplaceCategoryTranslations => Set<MarketplaceCategoryTranslation>();
    public DbSet<StorefrontFooterSection> StorefrontFooterSections => Set<StorefrontFooterSection>();
    public DbSet<StorefrontFooterLink> StorefrontFooterLinks => Set<StorefrontFooterLink>();
    public DbSet<StorefrontSocialLink> StorefrontSocialLinks => Set<StorefrontSocialLink>();
    public DbSet<MarketplaceFooterSection> MarketplaceFooterSections => Set<MarketplaceFooterSection>();
    public DbSet<MarketplaceFooterLink> MarketplaceFooterLinks => Set<MarketplaceFooterLink>();
    public DbSet<MarketplaceSocialLink> MarketplaceSocialLinks => Set<MarketplaceSocialLink>();
    public DbSet<SystemSchemaVersion> SystemSchemaVersions => Set<SystemSchemaVersion>();
    
    // Inventory & Warehouse
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<WarehouseStock> WarehouseStocks => Set<WarehouseStock>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<InventoryReservation> InventoryReservations => Set<InventoryReservation>();
    public DbSet<InventoryEventInbox> InventoryEventInbox => Set<InventoryEventInbox>();
    
    // Purchasing
    public DbSet<PurchaseInvoice> PurchaseInvoices => Set<PurchaseInvoice>();
    public DbSet<PurchaseInvoiceItem> PurchaseInvoiceItems => Set<PurchaseInvoiceItem>();
    public DbSet<PurchaseInvoicePayment> PurchaseInvoicePayments => Set<PurchaseInvoicePayment>();
    public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();
    public DbSet<SupplierCardFundingInstrument> SupplierCardFundingInstruments => Set<SupplierCardFundingInstrument>();
    public DbSet<SupplierCardFunding> SupplierCardFundings => Set<SupplierCardFunding>();
    public DbSet<PurchaseRequestIdempotency> PurchaseRequestIdempotencyRecords => Set<PurchaseRequestIdempotency>();
    public DbSet<PurchaseReturnRequestIdempotency> PurchaseReturnRequestIdempotencyRecords => Set<PurchaseReturnRequestIdempotency>();
    public DbSet<ProductRequestIdempotency> ProductRequestIdempotencyRecords => Set<ProductRequestIdempotency>();
    
    // Sales
    public DbSet<SalesInvoice> SalesInvoices => Set<SalesInvoice>();
    public DbSet<SalesInvoiceItem> SalesInvoiceItems => Set<SalesInvoiceItem>();
    public DbSet<SalesInvoicePayment> SalesInvoicePayments => Set<SalesInvoicePayment>();
    public DbSet<CustomerPayment> CustomerPayments => Set<CustomerPayment>();
    public DbSet<SalesRequestIdempotency> SalesRequestIdempotencyRecords => Set<SalesRequestIdempotency>();
    public DbSet<PosCart> PosCarts => Set<PosCart>();
    public DbSet<PosCartItem> PosCartItems => Set<PosCartItem>();
    public DbSet<PosCartRequestIdempotency> PosCartRequestIdempotencyRecords => Set<PosCartRequestIdempotency>();
    public DbSet<PosCartOutboxMessage> PosCartOutboxMessages => Set<PosCartOutboxMessage>();
    
    // Returns
    public DbSet<Return> Returns => Set<Return>();
    public DbSet<ReturnItem> ReturnItems => Set<ReturnItem>();
    
    // Transactions
    public DbSet<CustomerTransaction> CustomerTransactions => Set<CustomerTransaction>();
    public DbSet<SupplierTransaction> SupplierTransactions => Set<SupplierTransaction>();
    public DbSet<PartyLedgerAdjustment> PartyLedgerAdjustments => Set<PartyLedgerAdjustment>();
    public DbSet<AccountLedger> AccountLedgers => Set<AccountLedger>();
    
    // Cashbox
    public DbSet<Cashbox> Cashboxes => Set<Cashbox>();
    
    // Stock Transfers
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferItem> StockTransferItems => Set<StockTransferItem>();
    public DbSet<StockTransferRequestIdempotency> StockTransferRequestIdempotencyRecords => Set<StockTransferRequestIdempotency>();
    
    // Sales Representatives
    public DbSet<SalesRepresentative> SalesRepresentatives => Set<SalesRepresentative>();
    
    // Users & Settings
    public DbSet<User> Users => Set<User>();
    // Users Management (Wave 1): purpose-bound invitation/password-reset tokens.
    public DbSet<PosUserSecurityToken> PosUserSecurityTokens => Set<PosUserSecurityToken>();
    public DbSet<CanonicalIdentityLink> CanonicalIdentityLinks => Set<CanonicalIdentityLink>();
    public DbSet<SuperAdminUser> SuperAdmins => Set<SuperAdminUser>();
    public DbSet<UserGovernanceProfile> UserGovernanceProfiles => Set<UserGovernanceProfile>();
    public DbSet<UserGovernanceAuditLog> UserGovernanceAuditLogs => Set<UserGovernanceAuditLog>();
    public DbSet<UserGovernanceBlacklistEntry> UserGovernanceBlacklistEntries => Set<UserGovernanceBlacklistEntry>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    // Financial Accounts
    public DbSet<FinancialAccount> FinancialAccounts => Set<FinancialAccount>();
    public DbSet<FinancialTransaction> FinancialTransactions => Set<FinancialTransaction>();

    // Banks
    public DbSet<Bank> Banks => Set<Bank>();
    public DbSet<BankTransaction> BankTransactions => Set<BankTransaction>();

    // Expenses
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<Expense> Expenses => Set<Expense>();

    // Audit Logs
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // E-commerce Orders
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<WarehouseResolution> WarehouseResolutions => Set<WarehouseResolution>();
    public DbSet<CheckoutAttachment> CheckoutAttachments => Set<CheckoutAttachment>();
    public DbSet<OrderCheckoutFieldSnapshot> OrderCheckoutFieldSnapshots => Set<OrderCheckoutFieldSnapshot>();
    public DbSet<OrderRefund> OrderRefunds => Set<OrderRefund>();
    public DbSet<OrderRefundLine> OrderRefundLines => Set<OrderRefundLine>();
    public DbSet<OrderRequestIdempotency> OrderRequestIdempotencyRecords => Set<OrderRequestIdempotency>();
    public DbSet<CommerceHubCart> CommerceHubCarts => Set<CommerceHubCart>();
    public DbSet<CommerceHubCheckoutIdempotency> CommerceHubCheckoutIdempotency => Set<CommerceHubCheckoutIdempotency>();
    public DbSet<CommerceHubMarketplaceOrder> CommerceHubMarketplaceOrders => Set<CommerceHubMarketplaceOrder>();
    public DbSet<CommerceHubSubOrder> CommerceHubSubOrders => Set<CommerceHubSubOrder>();
    public DbSet<CommerceHubSubOrderItem> CommerceHubSubOrderItems => Set<CommerceHubSubOrderItem>();
    public DbSet<CommerceHubSubOrderStatusHistory> CommerceHubSubOrderStatusHistory => Set<CommerceHubSubOrderStatusHistory>();
    public DbSet<CommerceHubPayment> CommerceHubPayments => Set<CommerceHubPayment>();
    public DbSet<CommerceHubPaymentCompensationOutboxMessage> CommerceHubPaymentCompensationOutboxMessages => Set<CommerceHubPaymentCompensationOutboxMessage>();
    public DbSet<CommerceHubPaymentAttempt> CommerceHubPaymentAttempts => Set<CommerceHubPaymentAttempt>();
    public DbSet<CommerceHubPaymentItem> CommerceHubPaymentItems => Set<CommerceHubPaymentItem>();
    public DbSet<CommerceHubPaymentLog> CommerceHubPaymentLogs => Set<CommerceHubPaymentLog>();
    public DbSet<CommerceHubUnresolvedPaymentWebhook> CommerceHubUnresolvedPaymentWebhooks => Set<CommerceHubUnresolvedPaymentWebhook>();
    public DbSet<CommerceHubVendorEarning> CommerceHubVendorEarnings => Set<CommerceHubVendorEarning>();
    public DbSet<CommerceHubPayout> CommerceHubPayouts => Set<CommerceHubPayout>();
    public DbSet<CommerceHubPayoutItem> CommerceHubPayoutItems => Set<CommerceHubPayoutItem>();
    public DbSet<CommerceHubRefund> CommerceHubRefunds => Set<CommerceHubRefund>();
    public DbSet<CommerceHubRefundExecution> CommerceHubRefundExecutions => Set<CommerceHubRefundExecution>();
    public DbSet<CommerceHubRefundRequest> CommerceHubRefundRequests => Set<CommerceHubRefundRequest>();
    public DbSet<CommerceHubReturnRequest> CommerceHubReturnRequests => Set<CommerceHubReturnRequest>();
    public DbSet<CommerceHubRefundEvidenceMedia> CommerceHubRefundEvidenceMedia => Set<CommerceHubRefundEvidenceMedia>();
    public DbSet<CommerceHubFinancialAuditLog> CommerceHubFinancialAuditLogs => Set<CommerceHubFinancialAuditLog>();
    public DbSet<CommerceHubFinancialGuardLock> CommerceHubFinancialGuardLocks => Set<CommerceHubFinancialGuardLock>();
    public DbSet<CommerceHubFinancialOperationAudit> CommerceHubFinancialOperationAudits => Set<CommerceHubFinancialOperationAudit>();
    public DbSet<CommerceHubFinancialTransaction> CommerceHubFinancialTransactions => Set<CommerceHubFinancialTransaction>();
    public DbSet<CommerceHubDailyFinancialAggregate> CommerceHubDailyFinancialAggregates => Set<CommerceHubDailyFinancialAggregate>();
    public DbSet<CommerceHubFinancialReconciliationReport> CommerceHubFinancialReconciliationReports => Set<CommerceHubFinancialReconciliationReport>();
    public DbSet<CommerceHubVendorSettlement> CommerceHubVendorSettlements => Set<CommerceHubVendorSettlement>();
    public DbSet<CommerceHubVendorPaymentMethod> CommerceHubVendorPaymentMethods => Set<CommerceHubVendorPaymentMethod>();
    public DbSet<CommerceHubVendorPaymentRequest> CommerceHubVendorPaymentRequests => Set<CommerceHubVendorPaymentRequest>();
    public DbSet<CommerceHubVendorPaymentProof> CommerceHubVendorPaymentProofs => Set<CommerceHubVendorPaymentProof>();
    public DbSet<CommerceHubVendorPaymentAuditLog> CommerceHubVendorPaymentAuditLogs => Set<CommerceHubVendorPaymentAuditLog>();
    public DbSet<CommerceHubVendorPayoutMethod> CommerceHubVendorPayoutMethods => Set<CommerceHubVendorPayoutMethod>();
    
    // Payments
    public DbSet<PaymentIntent> PaymentIntents => Set<PaymentIntent>();
    public DbSet<PaymentWebhookLog> PaymentWebhookLogs => Set<PaymentWebhookLog>();
    public DbSet<StripeWebhookReplayRecord> StripeWebhookReplayRecords => Set<StripeWebhookReplayRecord>();
    public DbSet<PaymobWebhookReplayRecord> PaymobWebhookReplayRecords => Set<PaymobWebhookReplayRecord>();
    public DbSet<FawryWebhookReplayRecord> FawryWebhookReplayRecords => Set<FawryWebhookReplayRecord>();
    public DbSet<PaymentSettlementBatch> PaymentSettlementBatches => Set<PaymentSettlementBatch>();
    public DbSet<PaymentSettlementOrder> PaymentSettlementOrders => Set<PaymentSettlementOrder>();
    public DbSet<ShippingWebhookLog> ShippingWebhookLogs => Set<ShippingWebhookLog>();

    // Shipping Methods
    public DbSet<ShippingMethod> ShippingMethods => Set<ShippingMethod>();
    
    // Shipments (E-commerce)
    public DbSet<Shipment> Shipments => Set<Shipment>();
    
    // Carrier Configurations (Carrier-Agnostic Shipping Module)
    // Per-tenant configuration for shipping carriers (Bosta, Aramex, DHL, etc.)
    public DbSet<CarrierConfiguration> CarrierConfigurations => Set<CarrierConfiguration>();
    public DbSet<CarrierAccount> CarrierAccounts => Set<CarrierAccount>();
    public DbSet<CarrierCredentialReference> CarrierCredentialReferences => Set<CarrierCredentialReference>();
    public DbSet<CarrierWarehouseAssignment> CarrierWarehouseAssignments => Set<CarrierWarehouseAssignment>();
    public DbSet<CarrierAccountPolicy> CarrierAccountPolicies => Set<CarrierAccountPolicy>();
    public DbSet<CarrierServiceLevelEnablement> CarrierServiceLevelEnablements => Set<CarrierServiceLevelEnablement>();
    public DbSet<CarrierConnectionTestRecord> CarrierConnectionTestRecords => Set<CarrierConnectionTestRecord>();
    public DbSet<CarrierAdministrationAudit> CarrierAdministrationAudit => Set<CarrierAdministrationAudit>();

    // Carrier-neutral Customs runtime foundation (CUSTOMS-2)
    public DbSet<CustomsEvidence> CustomsEvidence => Set<CustomsEvidence>();
    public DbSet<CustomsEvidenceVersion> CustomsEvidenceVersions => Set<CustomsEvidenceVersion>();
    public DbSet<CustomsEvidenceHistory> CustomsEvidenceHistory => Set<CustomsEvidenceHistory>();
    public DbSet<CustomsEvidenceFingerprint> CustomsEvidenceFingerprints => Set<CustomsEvidenceFingerprint>();
    public DbSet<ShipmentCustomsSnapshot> ShipmentCustomsSnapshots => Set<ShipmentCustomsSnapshot>();
    public DbSet<ShipmentCustomsSnapshotLine> ShipmentCustomsSnapshotLines => Set<ShipmentCustomsSnapshotLine>();
    public DbSet<CustomsAdmissionAudit> CustomsAdmissionAudit => Set<CustomsAdmissionAudit>();
    public DbSet<CustomsEvidenceApproval> CustomsEvidenceApprovals => Set<CustomsEvidenceApproval>();
    public DbSet<CustomsEvidencePublication> CustomsEvidencePublications => Set<CustomsEvidencePublication>();
    public DbSet<CustomsPublicationLock> CustomsPublicationLocks => Set<CustomsPublicationLock>();
    public DbSet<CustomsAdministrationAudit> CustomsAdministrationAudit => Set<CustomsAdministrationAudit>();

    // Geographic Identity platform foundation (GEO-5)
    public DbSet<GeographicPlace> GeographicPlaces => Set<GeographicPlace>();
    public DbSet<GeographicPlaceLevel> GeographicPlaceLevels => Set<GeographicPlaceLevel>();
    public DbSet<GeographicPlaceHierarchyEdge> GeographicPlaceHierarchyEdges => Set<GeographicPlaceHierarchyEdge>();
    public DbSet<GeographicPlaceName> GeographicPlaceNames => Set<GeographicPlaceName>();
    public DbSet<GeographicPlaceAlias> GeographicPlaceAliases => Set<GeographicPlaceAlias>();
    public DbSet<GeographicCountryProfile> GeographicCountryProfiles => Set<GeographicCountryProfile>();
    public DbSet<GeographicCatalogRelease> GeographicCatalogReleases => Set<GeographicCatalogRelease>();
    public DbSet<GeographicCatalogReleasePlace> GeographicCatalogReleasePlaces => Set<GeographicCatalogReleasePlace>();
    public DbSet<GeographicCatalogReleaseEdge> GeographicCatalogReleaseEdges => Set<GeographicCatalogReleaseEdge>();
    public DbSet<GeographicCatalogPublication> GeographicCatalogPublications => Set<GeographicCatalogPublication>();
    public DbSet<CarrierProvider> CarrierProviders => Set<CarrierProvider>();
    public DbSet<CarrierGeographicMappingRelease> CarrierGeographicMappingReleases => Set<CarrierGeographicMappingRelease>();
    public DbSet<CarrierGeographicMapping> CarrierGeographicMappings => Set<CarrierGeographicMapping>();
    public DbSet<CarrierGeographicMappingGroup> CarrierGeographicMappingGroups => Set<CarrierGeographicMappingGroup>();
    public DbSet<CarrierGeographicMappingPublication> CarrierGeographicMappingPublications => Set<CarrierGeographicMappingPublication>();
    public DbSet<CarrierGeographicSyncRun> CarrierGeographicSyncRuns => Set<CarrierGeographicSyncRun>();
    public DbSet<CarrierGeographicSyncCandidate> CarrierGeographicSyncCandidates => Set<CarrierGeographicSyncCandidate>();
    public DbSet<CarrierGeographicOverride> CarrierGeographicOverrides => Set<CarrierGeographicOverride>();
    public DbSet<PlaceMergeHistory> PlaceMergeHistory => Set<PlaceMergeHistory>();
    public DbSet<PlaceDeprecation> PlaceDeprecations => Set<PlaceDeprecation>();
    public DbSet<AddressAdmissionEvidence> AddressAdmissionEvidence => Set<AddressAdmissionEvidence>();
    public DbSet<AddressNormalizationRule> AddressNormalizationRules => Set<AddressNormalizationRule>();
    public DbSet<GeographicAudit> GeographicAudit => Set<GeographicAudit>();


    // Coupons
    public DbSet<Coupon> Coupons => Set<Coupon>();
    
    // Coupon Usages (PHASE: Shopify-grade Coupon Engine)
    // Tracks individual coupon redemptions per order for MaxUsage/MaxPerCustomer enforcement
    public DbSet<CouponUsage> CouponUsages => Set<CouponUsage>();

    // Durable coupon checkout reservations for customer-bound eligibility locking.
    public DbSet<CouponReservation> CouponReservations => Set<CouponReservation>();
    
    // Promotions (PHASE: Promotions Engine - Offers + Coupons unified)
    // Auto-applied promotions without codes (Product/Category/Cart scope)
    public DbSet<Promotion> Promotions => Set<Promotion>();
    
    // Promotion Usages (PHASE: Promotions Engine)
    // Tracks individual offer applications per order for MaxUsage enforcement
    public DbSet<PromotionUsage> PromotionUsages => Set<PromotionUsage>();

    // Smart Offers (independent popup marketing engine)
    public DbSet<SmartOffer> SmartOffers => Set<SmartOffer>();
    
    // Multi-tenancy
    public DbSet<Tenant> Tenants => Set<Tenant>();

    // Currency Reference (S40 - ISO 4217 Validation)
    public DbSet<Currency> Currencies => Set<Currency>();

    // Store Configuration (Phase 7.1)
    public DbSet<StoreConfig> StoreConfigs => Set<StoreConfig>();
    
    // Store Config Audit Trail (Phase P3 - Hardening)
    public DbSet<StoreConfigAuditEntry> StoreConfigAuditEntries => Set<StoreConfigAuditEntry>();
    
    // Custom Domains Persistence (DOMAIN-2C)
    public DbSet<Domain> Domains => Set<Domain>();
    public DbSet<DomainVerification> DomainVerifications => Set<DomainVerification>();
    public DbSet<SslCertificate> SslCertificates => Set<SslCertificate>();
    public DbSet<SslCertificateOperation> SslCertificateOperations => Set<SslCertificateOperation>();
    public DbSet<DomainAuditEntry> DomainAuditEntries => Set<DomainAuditEntry>();

    // Theme Presets (Phase 9.3)
    public DbSet<ThemePreset> ThemePresets => Set<ThemePreset>();

    // Onboarding Checklists (Phase 8.0)
    public DbSet<OnboardingChecklist> OnboardingChecklists => Set<OnboardingChecklist>();

    // Email Verification Tokens (Phase 8.4)
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();

    // Online Customers (Phase 10.3.1 - Storefront Identity)
    // IMPORTANT: Completely separate from POS Customers
    public DbSet<OnlineCustomer> OnlineCustomers => Set<OnlineCustomer>();

    // Online Customer Email Verifications (Phase 10.3.2.5)
    public DbSet<OnlineCustomerEmailVerification> OnlineCustomerEmailVerifications => Set<OnlineCustomerEmailVerification>();

    // Online Customer Refresh Tokens (Phase 10.3.4)
    public DbSet<OnlineCustomerRefreshToken> OnlineCustomerRefreshTokens => Set<OnlineCustomerRefreshToken>();

    // Online Customer Auth Providers (Phase 10.3.3 - Social Login)
    public DbSet<OnlineCustomerAuthProvider> OnlineCustomerAuthProviders => Set<OnlineCustomerAuthProvider>();

    // Storefront Auth Provider Settings (Phase 10.3.3 - Social Login POS Config)
    public DbSet<StorefrontAuthProviderSettings> StorefrontAuthProviderSettings => Set<StorefrontAuthProviderSettings>();

    // Online Customer Password Reset Tokens (Phase 10.3.5 - Password Reset)
    public DbSet<OnlineCustomerPasswordResetToken> OnlineCustomerPasswordResetTokens => Set<OnlineCustomerPasswordResetToken>();

    // Online Customer Wishlist Items (Phase 10.3.6 - Wishlist)
    // IMPORTANT: Belongs to OnlineCustomer, NOT POS Customer
    public DbSet<OnlineCustomerWishlistItem> OnlineCustomerWishlistItems => Set<OnlineCustomerWishlistItem>();

    // Tenant Payment Methods (Phase 10.4.D1 - Payment Methods Management)
    // POS-managed payment method configurations for storefront
    public DbSet<TenantPaymentMethod> TenantPaymentMethods => Set<TenantPaymentMethod>();

    // Tenant Analytics Configuration (Phase 3 - Analytics Dispatchers)
    // Stores per-tenant settings for GA4, Meta CAPI, etc.
    // Secrets are encrypted at rest using Data Protection API
    public DbSet<PointOfSale.Infrastructure.Analytics.Dispatchers.TenantAnalyticsConfiguration> TenantAnalyticsConfigurations => Set<PointOfSale.Infrastructure.Analytics.Dispatchers.TenantAnalyticsConfiguration>();
    
    // WhatsApp Accounts (Phase 13 - WhatsApp Messaging)
    public DbSet<WhatsAppAccount> WhatsAppAccounts => Set<WhatsAppAccount>();
    
    // WhatsApp Message Templates (Phase 13 - Final Hardening)
    public DbSet<WhatsAppMessageTemplate> WhatsAppMessageTemplates => Set<WhatsAppMessageTemplate>();
    
    // Tenant Automation Settings (Phase: Smart Abandoned Cart Recovery Engine - Foundation)
    // Per-tenant configuration for e-commerce marketing automation
    public DbSet<TenantAutomationSettings> TenantAutomationSettings => Set<TenantAutomationSettings>();
    
    // Abandoned Carts (Phase: Smart Abandoned Cart Recovery Engine - Phase 2)
    // Tracks cart lifecycle from active → abandoned → triggered → recovered
    public DbSet<AbandonedCart> AbandonedCarts => Set<AbandonedCart>();
    public DbSet<AbandonedCartRecoveryLog> AbandonedCartRecoveryLogs => Set<AbandonedCartRecoveryLog>();
    
    // WhatsApp Rate Limits (Phase: Smart Abandoned Cart Recovery Engine - Hardening)
    // Tracks message rate limits and opt-out status per phone number
    public DbSet<WhatsAppRateLimit> WhatsAppRateLimits => Set<WhatsAppRateLimit>();
    
    // Checkout Sessions (Phase: Abandoned Checkout / Lost Orders)
    // Captures partially completed checkout attempts for recovery
    // NOT an Order — never touches inventory or payments
    public DbSet<CheckoutSession> CheckoutSessions => Set<CheckoutSession>();

    // POS-managed dynamic checkout field configuration.
    public DbSet<CheckoutFieldDefinition> CheckoutFieldDefinitions => Set<CheckoutFieldDefinition>();

    // Tenant-scoped storefront phone blocking profiles (Phase 1)
    public DbSet<PhoneRiskProfile> PhoneRiskProfiles => Set<PhoneRiskProfile>();

    // Global storefront spam reputation shared across all tenants (Phase 2)
    public DbSet<GlobalPhoneReputation> GlobalPhoneReputations => Set<GlobalPhoneReputation>();

    // Immutable spam report records for per-order idempotency (Phase 2.5)
    public DbSet<PhoneSpamReport> PhoneSpamReports => Set<PhoneSpamReport>();
    
    // Pixel Tracking Config (Phase M1 - Pixel Override)
    // Configuration for overriding pixel conversion values (privacy/strategy)
    public DbSet<PixelTrackingConfig> PixelTrackingConfigs => Set<PixelTrackingConfig>();

    // Product Pixel Overrides (Phase M2)
    // Per-product configuration for overriding pixel conversion values
    public DbSet<ProductPixelOverride> ProductPixelOverrides => Set<ProductPixelOverride>();

    // Analytics Alert Rules (Phase 4 - Realtime Business Alerts)
    // Threshold-based alert rules evaluated by background job
    public DbSet<PointOfSale.Core.Analytics.AnalyticsAlertRule> AnalyticsAlertRules => Set<PointOfSale.Core.Analytics.AnalyticsAlertRule>();

    // Campaign Costs (Phase 8B - Campaign Cost & ROI)
    // Admin-declared marketing spend per campaign for ROI calculations
    public DbSet<PointOfSale.Core.Analytics.Attribution.CampaignCost> CampaignCosts => Set<PointOfSale.Core.Analytics.Attribution.CampaignCost>();

    // ═══════════════════════════════════════════════════════════════════════════
    // UNIFIED FINANCIAL ENGINE (Phase 1 - Core Ledger Engine)
    // Double-entry accounting kernel - the financial heart of the system
    // ═══════════════════════════════════════════════════════════════════════════
    
    // Chart of Accounts
    public DbSet<Account> LedgerAccounts => Set<Account>();
    
    // Journal Entries (immutable financial transaction records)
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    
    // Ledger Entries (debit/credit lines within journals)
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    
    // Journal Number Sequences (per-tenant sequential numbering)
    public DbSet<JournalNumberSequence> JournalNumberSequences => Set<JournalNumberSequence>();
    
    // ═══════════════════════════════════════════════════════════════════════════
    // UNIFIED FINANCIAL ENGINE (Phase 2 - Chart of Accounts & Treasury)
    // Payment methods, treasury accounts, and system account management
    // ═══════════════════════════════════════════════════════════════════════════
    
    // Payment Methods (maps payment types to ledger accounts)
    public DbSet<PointOfSale.Core.Entities.Accounting.PaymentMethod> PaymentMethods => Set<PointOfSale.Core.Entities.Accounting.PaymentMethod>();
    
    // Treasury Accounts (cashboxes, banks, clearing accounts)
    public DbSet<TreasuryAccount> TreasuryAccounts => Set<TreasuryAccount>();
    public DbSet<TreasuryInitialCapitalization> TreasuryInitialCapitalizations => Set<TreasuryInitialCapitalization>();
    public DbSet<TreasuryInitialCapitalizationAllocation> TreasuryInitialCapitalizationAllocations => Set<TreasuryInitialCapitalizationAllocation>();
    
    // ═══════════════════════════════════════════════════════════════════════════
    // UNIFIED FINANCIAL ENGINE (Phase 3.5 - Production Hardening)
    // Outbox pattern and audit trail for guaranteed delivery
    // ═══════════════════════════════════════════════════════════════════════════
    
    // Accounting Outbox (transactional outbox for guaranteed delivery)
    public DbSet<AccountingOutboxMessage> AccountingOutboxMessages => Set<AccountingOutboxMessage>();

    // Payment Cancellation Outbox (durable payment cancellation operations)
    public DbSet<PaymentCancellationOutboxMessage> PaymentCancellationOutboxMessages => Set<PaymentCancellationOutboxMessage>();

    // General Outbox (for cart events and other non-specialized events)
    // Note: Fully-qualified to distinguish from MassTransit.OutboxMessage added by AddOutboxMessageEntity()
    public DbSet<PointOfSale.Core.Entities.OutboxMessage> OutboxMessages => Set<PointOfSale.Core.Entities.OutboxMessage>();
    public DbSet<ProviderDeliveryOutboxMessage> ProviderDeliveryOutbox => Set<ProviderDeliveryOutboxMessage>();
    public DbSet<ProviderDeliveryReplayAudit> ProviderDeliveryReplayAudits => Set<ProviderDeliveryReplayAudit>();
    public DbSet<StorefrontCartCouponState> StorefrontCartCouponStates => Set<StorefrontCartCouponState>();

    // Marketplace event backbone (transactional outbox + consumer idempotency)
    public DbSet<MarketplaceEventOutboxMessage> MarketplaceEventOutboxMessages => Set<MarketplaceEventOutboxMessage>();
    public DbSet<MarketplaceProjectionOutboxMessage> MarketplaceProjectionOutboxMessages => Set<MarketplaceProjectionOutboxMessage>();
    public DbSet<MarketplaceEventOutboxDeadLetterArchive> MarketplaceEventOutboxDeadLetterArchives => Set<MarketplaceEventOutboxDeadLetterArchive>();
    public DbSet<StockSyncOutboxMessage> StockSyncOutboxMessages => Set<StockSyncOutboxMessage>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();
    
    // Commerce-to-Ledger Audit Trail (full traceability)
    public DbSet<CommerceAccountingAudit> CommerceAccountingAudits => Set<CommerceAccountingAudit>();

    // ═══════════════════════════════════════════════════════════════════════════
    // AFFILIATE SYSTEM (Phase AF-1 - Write-Side Domain Model)
    // Commission attribution and tracking for affiliate marketing
    // ═══════════════════════════════════════════════════════════════════════════
    
    public DbSet<AffiliateAccount> AffiliateAccounts => Set<AffiliateAccount>();
    public DbSet<AffiliateProductPolicy> AffiliateProductPolicies => Set<AffiliateProductPolicy>();
    public DbSet<AffiliateAttribution> AffiliateAttributions => Set<AffiliateAttribution>();
    public DbSet<AffiliateCommissionLedger> AffiliateCommissionLedger => Set<AffiliateCommissionLedger>();
    
    // AF-38C: Dual-Ledger Architecture - Account-level balance movements
    public DbSet<AffiliateBalanceLedger> AffiliateBalanceLedger => Set<AffiliateBalanceLedger>();

    // AF-10: Independent Affiliate Identity (decoupled from POS User)
    public DbSet<AffiliateUser> AffiliateUsers => Set<AffiliateUser>();
    public DbSet<AffiliateEmailVerificationToken> AffiliateEmailVerificationTokens => Set<AffiliateEmailVerificationToken>();
    public DbSet<AffiliatePasswordResetToken> AffiliatePasswordResetTokens => Set<AffiliatePasswordResetToken>();
    public DbSet<AffiliateRefreshTokenReplayEntry> AffiliateRefreshTokenReplayEntries => Set<AffiliateRefreshTokenReplayEntry>();

    // FEED-1A: Product feed tokens — hashed opaque tokens for public feed URLs
    public DbSet<ProductFeedToken> ProductFeedTokens => Set<ProductFeedToken>();

    // AF-15: Promotion Links — standard tenant + soft-delete
    public DbSet<AffiliatePromotionLink> AffiliatePromotionLinks => Set<AffiliatePromotionLink>();

    // AF-30: Global Marketplace Category System
    public DbSet<AffiliateCategory> AffiliateCategories => Set<AffiliateCategory>();

    // AF-38: Affiliate Payout Requests (Hybrid Withdrawal Engine)
    public DbSet<AffiliatePayoutRequest> AffiliatePayoutRequests => Set<AffiliatePayoutRequest>();
    public DbSet<AffiliatePayoutTransferEvidence> AffiliatePayoutTransferEvidence => Set<AffiliatePayoutTransferEvidence>();
    public DbSet<AffiliatePayoutTransferAllocation> AffiliatePayoutTransferAllocations => Set<AffiliatePayoutTransferAllocation>();


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all entity configurations from assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        if (Database.IsNpgsql())
        {
            modelBuilder.Entity<Tenant>().ToTable("Tenants", table => table.HasCheckConstraint(
                "CK_Tenants_Slug_Canonical",
                "\"Slug\" IS NULL OR (\"Slug\" = LOWER(BTRIM(\"Slug\")) AND LENGTH(\"Slug\") BETWEEN 3 AND 63 AND \"Slug\" ~ '^[a-z0-9]([a-z0-9-]*[a-z0-9])?$' AND POSITION('--' IN \"Slug\") = 0)"));
            modelBuilder.Entity<SupplierCardFundingInstrument>().ToTable("SupplierCardFundingInstruments", table => table.HasCheckConstraint(
                "CK_SupplierCardFundingInstrument_Last4",
                "\"Last4\" IS NULL OR \"Last4\" ~ '^[0-9]{4}$'"));
        }
        ConfigurePosUserRowVersion(modelBuilder);
        modelBuilder.Entity<ProviderDeliveryOutboxMessage>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        modelBuilder.Entity<ProviderDeliveryReplayAudit>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        ConfigureAccountingOutboxConcurrency(modelBuilder);
        ConfigurePosCartConcurrency(modelBuilder);
        var maxFixedDiscountValueSql = DiscountValidation.MaxFixedDiscountValue.ToString("0.00", CultureInfo.InvariantCulture);
        
        // =============================================================================
        // PRODUCTION-GRADE TENANT ISOLATION (Phase: Tenant Isolation Hardening)
        // 
        // CRITICAL SECURITY RULES:
        // 1. ALL tenant entities MUST be filtered by CurrentTenantId
        // 2. NO `TenantId == null` fallbacks - this caused data leakage!
        // 3. NO `CurrentTenantId == null` bypasses - tenant context is REQUIRED
        // 4. Entities WITHOUT a tenant context are INVISIBLE (security by default)
        // 
        // Cross-tenant access is reserved for platform/system maintenance only.
        // Tenant admin flows MUST stay inside the global query filters.
        // =============================================================================
        
        // Helper property for strict tenant filtering
        // Entities are ONLY visible if they belong to the current tenant
        
        // -------------------------------------------------------------------------
        // TENANT-OWNED ENTITIES: Strict tenant isolation + soft-delete
        // -------------------------------------------------------------------------
        
        modelBuilder.Entity<Customer>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);

        modelBuilder.Entity<CustomerAddress>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);

        modelBuilder.Entity<CustomerAddressVersion>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);

        modelBuilder.Entity<CustomerAddress>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TenantId, e.CustomerId, e.IsDefault });
            entity.HasOne(e => e.Customer)
                .WithMany(e => e.Addresses)
                .HasForeignKey(e => e.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CurrentVersion)
                .WithMany()
                .HasForeignKey(e => e.CurrentVersionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CustomerAddressVersion>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TenantId, e.CustomerAddressId, e.Version })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");
            entity.HasIndex(e => e.AddressAdmissionEvidenceId);
            entity.HasIndex(e => new { e.GeographicCatalogReleaseId, e.CountryPlaceId, e.AdministrativeAreaPlaceId, e.LocalityPlaceId, e.DistrictPlaceId, e.ZonePlaceId });
            entity.Property(e => e.Street).HasMaxLength(500);
            entity.Property(e => e.Street2).HasMaxLength(500);
            entity.Property(e => e.Building).HasMaxLength(120);
            entity.Property(e => e.Floor).HasMaxLength(64);
            entity.Property(e => e.Apartment).HasMaxLength(64);
            entity.Property(e => e.Landmark).HasMaxLength(200);
            entity.Property(e => e.PostalCode).HasMaxLength(32);
            entity.Property(e => e.CountryDisplayName).HasMaxLength(200);
            entity.Property(e => e.AdministrativeAreaDisplayName).HasMaxLength(200);
            entity.Property(e => e.LocalityDisplayName).HasMaxLength(200);
            entity.Property(e => e.DistrictDisplayName).HasMaxLength(200);
            entity.Property(e => e.ZoneDisplayName).HasMaxLength(200);
            entity.Property(e => e.LegacyAddressText).HasMaxLength(1000);
            entity.HasOne(e => e.CustomerAddress)
                .WithMany(e => e.Versions)
                .HasForeignKey(e => e.CustomerAddressId)
                .OnDelete(DeleteBehavior.Restrict);
        });
            
        modelBuilder.Entity<Category>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);
            
        modelBuilder.Entity<Product>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);

        modelBuilder.Entity<ProductMedia>().HasQueryFilter(e =>
            e.TenantId == CurrentTenantId);
        modelBuilder.Entity<HomepageMediaAsset>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
            
        modelBuilder.Entity<Warehouse>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        modelBuilder.Entity<Warehouse>()
            .HasAlternateKey(e => new { e.TenantId, e.Id })
            .HasName("AK_Warehouses_TenantId_Id");
            
        modelBuilder.Entity<Supplier>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);
            
        modelBuilder.Entity<User>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);

        // Users Management (Wave 1): strict tenant filter for token authority.
        // Anonymous ceremonies bypass via IgnoreQueryFilters with hash lookup.
        modelBuilder.Entity<PosUserSecurityToken>().HasQueryFilter(e =>
            e.TenantId == CurrentTenantId);
            
        modelBuilder.Entity<FinancialAccount>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);
            
        modelBuilder.Entity<Bank>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);
            
        modelBuilder.Entity<Cashbox>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);
            
        modelBuilder.Entity<ExpenseCategory>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);
            
        modelBuilder.Entity<Expense>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);
            
        modelBuilder.Entity<Unit>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);
            
        modelBuilder.Entity<SalesRepresentative>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);
            
        modelBuilder.Entity<SalesInvoice>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);
            
        modelBuilder.Entity<PurchaseInvoice>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);

        modelBuilder.Entity<PurchaseInvoice>()
            .HasAlternateKey(invoice => new { invoice.TenantId, invoice.Id })
            .HasName("AK_PurchaseInvoices_TenantId_Id");

        modelBuilder.Entity<PurchaseRequestIdempotency>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);

        modelBuilder.Entity<PurchaseReturnRequestIdempotency>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);

        modelBuilder.Entity<ProductRequestIdempotency>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);

        modelBuilder.Entity<SalesRequestIdempotency>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);

        modelBuilder.Entity<PosCart>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);

        modelBuilder.Entity<PosCartRequestIdempotency>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);

        modelBuilder.Entity<PosCartOutboxMessage>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);

        modelBuilder.Entity<OrderRequestIdempotency>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);
            
        modelBuilder.Entity<Return>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);
            
        modelBuilder.Entity<StockTransfer>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);
            
        modelBuilder.Entity<Order>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        modelBuilder.Entity<OrderRefund>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        modelBuilder.Entity<OrderRefundLine>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);
            
        modelBuilder.Entity<ShippingMethod>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        modelBuilder.Entity<ShippingMethod>(entity =>
        {
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_ShippingMethods_ActiveRequiresFulfillmentWarehouse",
                "NOT \"IsActive\" OR \"FulfillmentWarehouseId\" IS NOT NULL"));

            entity.HasIndex(e => new { e.TenantId, e.FulfillmentWarehouseId })
                .HasDatabaseName("IX_ShippingMethods_TenantId_FulfillmentWarehouseId");

            entity.HasOne(e => e.FulfillmentWarehouse)
                .WithMany()
                .HasForeignKey(e => new { e.TenantId, e.FulfillmentWarehouseId })
                .HasPrincipalKey(e => new { e.TenantId, e.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
            
        modelBuilder.Entity<Coupon>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        
        // CouponUsage (PHASE: Shopify-grade Coupon Engine)
        modelBuilder.Entity<CouponUsage>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);

        modelBuilder.Entity<CouponReservation>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);

        modelBuilder.Entity<PromoCodeUsage>().HasQueryFilter(e =>
            !e.IsDeleted);

        // Promotion (PHASE: Promotions Engine - Offers)
        modelBuilder.Entity<Promotion>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        
        // PromotionUsage (PHASE: Promotions Engine)
        modelBuilder.Entity<PromotionUsage>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);

        modelBuilder.Entity<SmartOffer>().HasQueryFilter(e =>
            e.TenantId == CurrentTenantId);

        modelBuilder.Entity<CmsPage>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        modelBuilder.Entity<CmsPageTranslation>().HasQueryFilter(e =>
            e.TenantId == CurrentTenantId);
             
        modelBuilder.Entity<Shipment>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        
        // Transaction entities (also tenant-owned)
        modelBuilder.Entity<AccountLedger>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);
            
        modelBuilder.Entity<BankTransaction>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);
            
        modelBuilder.Entity<CashboxTransaction>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);
            
        modelBuilder.Entity<CustomerTransaction>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);

        modelBuilder.Entity<CustomerPayment>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);
            
        modelBuilder.Entity<SupplierTransaction>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);

        modelBuilder.Entity<SupplierPayment>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        modelBuilder.Entity<SupplierCardFundingInstrument>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        modelBuilder.Entity<SupplierCardFunding>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);

        modelBuilder.Entity<PartyLedgerAdjustment>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);
            
        modelBuilder.Entity<FinancialTransaction>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);
            
        modelBuilder.Entity<TreasuryTransaction>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        modelBuilder.Entity<TreasuryTransaction>().ToTable("TreasuryTransactions");
            
        modelBuilder.Entity<VisaTransaction>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        modelBuilder.Entity<VisaTransaction>().ToTable("VisaTransactions");
            
        // Stock entities (also tenant-owned)
        modelBuilder.Entity<WarehouseStock>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);
            
        modelBuilder.Entity<StockMovement>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);
            
        // Payment entities (also tenant-owned)
        modelBuilder.Entity<PaymentIntent>(entity => 
        {
            entity.HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);
            entity.Property(e => e.Provider).HasConversion<string>();
            entity.Property(e => e.PayoutBatchId).HasMaxLength(200);
            entity.Property(e => e.RowVersion)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .IsRowVersion();
            entity.HasIndex(e => new { e.TenantId, e.OrderId, e.Provider })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false AND \"Status\" IN (0, 1, 2, 7)")
                .HasDatabaseName("IX_PaymentIntents_Active_OrderId_Provider_Unique");
            entity.HasIndex(e => new { e.TenantId, e.PayoutBatchId })
                .HasDatabaseName("IX_PaymentIntents_TenantId_PayoutBatchId");
        });
        var paymentCancellationOutboxRowVersion = modelBuilder.Entity<PaymentCancellationOutboxMessage>()
            .Property(e => e.RowVersion);
        if (Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            paymentCancellationOutboxRowVersion
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .IsRowVersion();
        }
        else
        {
            paymentCancellationOutboxRowVersion
                .HasColumnName(nameof(PaymentCancellationOutboxMessage.RowVersion))
                .IsConcurrencyToken()
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValue(0u);
        }

            
        modelBuilder.Entity<PaymentWebhookLog>(entity =>
        {
            entity.HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);
            entity.HasIndex(e => new { e.TenantId, e.Provider, e.ProviderEventId })
                .IsUnique()
                .HasDatabaseName("IX_PaymentWebhookLogs_TenantId_Provider_ProviderEventId");
        });

        modelBuilder.Entity<StripeWebhookReplayRecord>(entity =>
        {
            entity.HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);
            entity.Property(e => e.Provider).HasMaxLength(32).IsRequired();
            entity.Property(e => e.ProviderEventId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.EventType).HasMaxLength(120).IsRequired();
            entity.Property(e => e.EvidenceHash).HasMaxLength(128).IsRequired();
            entity.Property(e => e.EvidenceReference).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ProviderReference).HasMaxLength(240).IsRequired();
            entity.Property(e => e.CorrelationId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.AdmissionClassification).HasMaxLength(40).IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.Provider, e.ProviderEventId })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false")
                .HasDatabaseName("IX_StripeWebhookReplayRecords_Tenant_Provider_Event");
            entity.HasIndex(e => new { e.TenantId, e.ProviderReference })
                .HasDatabaseName("IX_StripeWebhookReplayRecords_Tenant_ProviderReference");
            entity.HasIndex(e => new { e.TenantId, e.EvidenceHash })
                .HasDatabaseName("IX_StripeWebhookReplayRecords_Tenant_EvidenceHash");
        });

        modelBuilder.Entity<PaymobWebhookReplayRecord>(entity =>
        {
            entity.HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);
            entity.Property(e => e.Provider).HasMaxLength(32).IsRequired();
            entity.Property(e => e.ProviderEventId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.EventType).HasMaxLength(120).IsRequired();
            entity.Property(e => e.EvidenceHash).HasMaxLength(128).IsRequired();
            entity.Property(e => e.EvidenceReference).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ProviderReference).HasMaxLength(240).IsRequired();
            entity.Property(e => e.CorrelationId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.AdmissionClassification).HasMaxLength(40).IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.Provider, e.ProviderEventId })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false")
                .HasDatabaseName("IX_PaymobWebhookReplayRecords_Tenant_Provider_Event");
            entity.HasIndex(e => new { e.TenantId, e.ProviderReference })
                .HasDatabaseName("IX_PaymobWebhookReplayRecords_Tenant_ProviderReference");
            entity.HasIndex(e => new { e.TenantId, e.EvidenceHash })
                .HasDatabaseName("IX_PaymobWebhookReplayRecords_Tenant_EvidenceHash");
        });

        modelBuilder.Entity<FawryWebhookReplayRecord>(entity =>
        {
            entity.HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);
            entity.Property(e => e.Provider).HasMaxLength(32).IsRequired();
            entity.Property(e => e.ProviderEventId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.EventType).HasMaxLength(120).IsRequired();
            entity.Property(e => e.EvidenceHash).HasMaxLength(128).IsRequired();
            entity.Property(e => e.EvidenceReference).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ProviderReference).HasMaxLength(240).IsRequired();
            entity.Property(e => e.CorrelationId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.AdmissionClassification).HasMaxLength(40).IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.Provider, e.ProviderEventId })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false")
                .HasDatabaseName("IX_FawryWebhookReplayRecords_Tenant_Provider_Event");
            entity.HasIndex(e => new { e.TenantId, e.ProviderReference })
                .HasDatabaseName("IX_FawryWebhookReplayRecords_Tenant_ProviderReference");
            entity.HasIndex(e => new { e.TenantId, e.EvidenceHash })
                .HasDatabaseName("IX_FawryWebhookReplayRecords_Tenant_EvidenceHash");
        });

        modelBuilder.Entity<PaymentSettlementBatch>(entity =>
        {
            entity.HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);
            entity.Property(e => e.SettlementType).HasMaxLength(32).IsRequired();
            entity.Property(e => e.SettlementId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Provider).HasMaxLength(100);
            entity.Property(e => e.PayoutBatchId).HasMaxLength(200);
            entity.Property(e => e.Currency).HasMaxLength(3).IsRequired();
            entity.Property(e => e.BankReference).HasMaxLength(200);
            entity.Property(e => e.GrossAmount).HasPrecision(18, 2);
            entity.Property(e => e.Fees).HasPrecision(18, 2);
            entity.HasIndex(e => new { e.TenantId, e.SettlementType, e.SettlementId })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false")
                .HasDatabaseName("IX_PaymentSettlementBatches_Tenant_Type_SettlementId");
            entity.HasIndex(e => new { e.TenantId, e.PayoutBatchId })
                .HasDatabaseName("IX_PaymentSettlementBatches_Tenant_PayoutBatchId");
            entity.HasMany(e => e.Orders)
                .WithOne(e => e.SettlementBatch)
                .HasForeignKey(e => e.SettlementBatchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PaymentSettlementOrder>(entity =>
        {
            entity.HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);
            entity.Property(e => e.Currency).HasMaxLength(3).IsRequired();
            entity.Property(e => e.OrderTotal).HasPrecision(18, 2);
            entity.HasIndex(e => new { e.TenantId, e.SettlementBatchId, e.OrderId })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false")
                .HasDatabaseName("IX_PaymentSettlementOrders_Tenant_Batch_Order");
            entity.HasIndex(e => new { e.TenantId, e.OrderId })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false")
                .HasDatabaseName("IX_PaymentSettlementOrders_Tenant_Order");
            entity.HasIndex(e => new { e.TenantId, e.PaymentIntentId })
                .HasFilter("\"IsDeleted\" = false AND \"PaymentIntentId\" IS NOT NULL")
                .HasDatabaseName("IX_PaymentSettlementOrders_Tenant_PaymentIntent");
            entity.HasOne(e => e.Order)
                .WithMany()
                .HasForeignKey(e => e.OrderId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.PaymentIntent)
                .WithMany()
                .HasForeignKey(e => e.PaymentIntentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ShippingWebhookLog>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        
        // -------------------------------------------------------------------------
        // UNIFIED FINANCIAL ENGINE ENTITIES (Phase 1 - Core Ledger Engine)
        // Strict tenant isolation, no soft-delete (immutable journals)
        // -------------------------------------------------------------------------
        
        // Chart of Accounts
        modelBuilder.Entity<Account>().HasQueryFilter(e =>
            e.TenantId == CurrentTenantId);
        
        // Journal Entries (immutable - no soft-delete needed)
        modelBuilder.Entity<JournalEntry>().HasQueryFilter(e =>
            e.TenantId == CurrentTenantId);
        
        // Ledger Entries (immutable - no soft-delete needed)
        modelBuilder.Entity<LedgerEntry>().HasQueryFilter(e =>
            e.TenantId == CurrentTenantId);
        
        // -------------------------------------------------------------------------
        // UNIFIED FINANCIAL ENGINE ENTITIES (Phase 2 - Chart of Accounts & Treasury)
        // Payment methods, treasury accounts - tenant isolation
        // -------------------------------------------------------------------------
        
        // Payment Methods (maps payment types to ledger accounts)
        modelBuilder.Entity<PointOfSale.Core.Entities.Accounting.PaymentMethod>().HasQueryFilter(e =>
            e.TenantId == CurrentTenantId);
        
        // Treasury Accounts (cashboxes, banks, clearing accounts)
        modelBuilder.Entity<TreasuryAccount>().HasQueryFilter(e =>
            e.TenantId == CurrentTenantId);
        modelBuilder.Entity<TreasuryInitialCapitalization>().HasQueryFilter(e =>
            e.TenantId == CurrentTenantId);
        modelBuilder.Entity<TreasuryInitialCapitalizationAllocation>().HasQueryFilter(e =>
            e.TenantId == CurrentTenantId);
        
        // -------------------------------------------------------------------------
        // CONFIGURATION ENTITIES: Tenant isolation without soft-delete
        // -------------------------------------------------------------------------
        
        modelBuilder.Entity<StoreConfig>().HasQueryFilter(e => 
            e.TenantId == CurrentTenantId);
        
        // Store Config Audit Trail (Phase P3)
        // Tenant isolation for audit entries
        modelBuilder.Entity<StoreConfigAuditEntry>().HasQueryFilter(e =>
            e.TenantId == CurrentTenantId);
            
        modelBuilder.Entity<OnboardingChecklist>().HasQueryFilter(e => 
            e.TenantId == CurrentTenantId);
        
        // Online Customers (Phase 10.3.1 - Storefront Identity)
        // IMPORTANT: Separate from POS Customers - different authentication system
        modelBuilder.Entity<OnlineCustomer>().HasQueryFilter(e =>
            e.TenantId == CurrentTenantId);
        
        // Online Customer Wishlist Items (Phase 10.3.6 - Wishlist)
        // Tenant isolation + soft-delete filter
        modelBuilder.Entity<OnlineCustomerWishlistItem>().HasQueryFilter(e =>
            !e.IsRemoved && e.TenantId == CurrentTenantId);
        
        // Tenant Payment Methods (Phase 10.4.D1 - Payment Methods Management)
        // Tenant isolation only (no soft-delete for configuration entities)
        modelBuilder.Entity<TenantPaymentMethod>().HasQueryFilter(e =>
            e.TenantId == CurrentTenantId);

        modelBuilder.Entity<CheckoutFieldDefinition>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        modelBuilder.Entity<CheckoutAttachment>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);

        // -------------------------------------------------------------------------
        // Custom Domains (DOMAIN-2C)
        // CPD-5: Domain implements ITenantOwned with System/Development exemption.
        // System and Development domain types are platform-global — exempt from
        // tenant filtering. All other types are filtered by TenantId AND IsDeleted.
        // -------------------------------------------------------------------------
        modelBuilder.Entity<Domain>().HasQueryFilter(e =>
            !e.IsDeleted &&
            (e.DomainType == DomainType.System ||
             e.DomainType == DomainType.Development ||
             e.TenantId == CurrentTenantId));

        // DomainVerification: cascade soft-delete through parent Domain
        modelBuilder.Entity<DomainVerification>().HasQueryFilter(e =>
            !e.IsDeleted &&
            !e.Domain.IsDeleted &&
            e.Domain.TenantId == CurrentTenantId);

        // SslCertificate: explicit tenant isolation plus soft-delete.
        modelBuilder.Entity<SslCertificate>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);

        // SslCertificateOperation: durable idempotency/lease records are tenant-scoped.
        modelBuilder.Entity<SslCertificateOperation>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);

        // DomainAuditEntry: CPD-4 tenant isolation (ITenantOwned)
        modelBuilder.Entity<DomainAuditEntry>().HasQueryFilter(e =>
            e.TenantId == CurrentTenantId);

        // Storefront phone blocking profiles (Phase 1)
        modelBuilder.Entity<PhoneRiskProfile>().HasQueryFilter(e =>
            e.TenantId == CurrentTenantId);
        
        // -------------------------------------------------------------------------
        // MARKETPLACE ENTITIES (Phase: Marketplace Audit Hardening)
        // CRITICAL: These were missing tenant isolation query filters.
        // All marketplace entities inherit from TenantEntity and MUST be filtered.
        // -------------------------------------------------------------------------
        
        modelBuilder.Entity<MarketplaceProfile>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        
        modelBuilder.Entity<Service>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        
        modelBuilder.Entity<ServiceRequest>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        
        modelBuilder.Entity<ServiceReview>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        
        modelBuilder.Entity<ProviderTrustAggregate>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        
        modelBuilder.Entity<ServiceRequestMessage>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        
        modelBuilder.Entity<ProcessedTrustEvent>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        
        modelBuilder.Entity<MarketingReviewEvidence>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        
        // -------------------------------------------------------------------------
        // AFFILIATE SYSTEM ENTITIES (Phase AF-1)
        // AffiliateAccount, AffiliateProductPolicy, AffiliateAttribution: standard tenant + soft-delete
        // AffiliateCommissionLedger: tenant-only (no IsDeleted — append-only, no BaseEntity)
        // -------------------------------------------------------------------------
        
        modelBuilder.Entity<AffiliateAccount>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        
        modelBuilder.Entity<AffiliateProductPolicy>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        modelBuilder.Entity<AffiliateProductPolicy>(entity =>
        {
            entity.HasIndex(e => e.TenantId)
                .HasDatabaseName("IX_AffiliateProductPolicies_TenantId");
            entity.HasIndex(e => new { e.TenantId, e.ProductId })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false")
                .HasDatabaseName("UQ_AffiliateProductPolicies_Tenant_Product");

            entity.ToTable("AffiliateProductPolicies", table =>
            {
                table.HasCheckConstraint(
                    "CK_AffiliateProductPolicies_Rate",
                    "\"CommissionRate\" >= 0.0001 AND \"CommissionRate\" <= 0.50");
                table.HasCheckConstraint(
                    "CK_AffiliateProductPolicies_MaxCap",
                    "\"MaxCommissionPerItem\" IS NULL OR \"MaxCommissionPerItem\" > 0");
                table.HasCheckConstraint(
                    "CK_AffiliateProductPolicies_EnabledRequiresCategory",
                    "NOT (\"IsEnabled\" = true AND \"AffiliateCategoryId\" IS NULL)");
            });
        });
        
        modelBuilder.Entity<AffiliateAttribution>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        
        // Commission ledger: NO IsDeleted (genuinely append-only entity)
        modelBuilder.Entity<AffiliateCommissionLedger>().HasQueryFilter(e =>
            e.TenantId == CurrentTenantId);
        
        // AF-15: Promotion Links — standard tenant + soft-delete
        modelBuilder.Entity<AffiliatePromotionLink>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        modelBuilder.Entity<CommerceHubListing>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);

        // AF-38: Payout Requests — scoped to StoreTenantId (global, not tenant-bound like commissions)
        // No soft-delete (withdrawal records are immutable audit entries)
        modelBuilder.Entity<AffiliatePayoutRequest>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RequestedAmount).HasPrecision(18, 4);
            entity.Property(e => e.ApprovedAmount).HasPrecision(18, 4);
            entity.Property(e => e.RemainingAmount).HasPrecision(18, 4);
            entity.Property(e => e.Currency).HasMaxLength(3);
            entity.Property(e => e.AdminNotes).HasMaxLength(1000);
            entity.Property(e => e.PaymentReference).HasMaxLength(500);
            entity.Property(e => e.PayoutWalletNumberSnapshot).HasMaxLength(128);
            entity.Property(e => e.PayoutWalletTypeSnapshot).HasMaxLength(64);
            entity.Property(e => e.Status).HasConversion<int>();

            // Indexes — AF-38 spec
            entity.HasIndex(e => new { e.StoreTenantId, e.Status })
                .HasDatabaseName("IX_AffiliatePayoutRequests_TenantId_Status");
            entity.HasIndex(e => new { e.AffiliateAccountId, e.Status })
                .HasDatabaseName("IX_AffiliatePayoutRequests_AffiliateId_Status");
            entity.HasIndex(e => new { e.StoreTenantId, e.AffiliateAccountId, e.Status })
                .HasDatabaseName("IX_AffiliatePayoutRequests_TenantId_AffiliateId_Status");
        });
        
        // -------------------------------------------------------------------------
        // CHILD ENTITIES: Soft-delete only (isolation via parent FK)
        // -------------------------------------------------------------------------
        
        // ProductVariant now has TenantId (Phase 11.1 - Shopify-grade variants)
        modelBuilder.Entity<ProductVariant>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        modelBuilder.Entity<ProductVariantStock>().HasQueryFilter(e => 
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        modelBuilder.Entity<MarketplaceProjectionOutboxMessage>().HasQueryFilter(e =>
            !e.IsDeleted);
        var marketplaceProjectionOutboxRowVersion = modelBuilder.Entity<MarketplaceProjectionOutboxMessage>()
            .Property(e => e.RowVersion);
        if (Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            marketplaceProjectionOutboxRowVersion
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .IsRowVersion();
        }
        else
        {
            marketplaceProjectionOutboxRowVersion
                .HasColumnName(nameof(MarketplaceProjectionOutboxMessage.RowVersion))
                .IsConcurrencyToken()
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValue(0u);
        }

        modelBuilder.Entity<StockSyncOutboxMessage>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        var stockSyncOutboxRowVersion = modelBuilder.Entity<StockSyncOutboxMessage>()
            .Property(e => e.RowVersion);
        if (Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            stockSyncOutboxRowVersion
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .IsRowVersion();
        }
        else
        {
            // Non-PostgreSQL fallback: ensure ValueGeneratedOnAddOrUpdate() is set
            // to enable automatic version generation and proper concurrency detection
            stockSyncOutboxRowVersion
                .HasColumnName(nameof(StockSyncOutboxMessage.RowVersion))
                .IsConcurrencyToken()
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValue(0u);
        }

        var stockSyncOutboxEntity = modelBuilder.Entity<StockSyncOutboxMessage>();
        stockSyncOutboxEntity.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_StockSyncOutboxMessages_EventVersion_Positive",
                "\"EventVersion\" > 0");
            table.HasCheckConstraint(
                "CK_StockSyncOutboxMessages_OrderingTimestamps_Required",
                "\"OccurredAtUtc\" IS NOT NULL AND \"StockUpdatedAtUtc\" IS NOT NULL");
            table.HasCheckConstraint(
                "CK_StockSyncOutboxMessages_WarehouseId_NotEmpty",
                "\"WarehouseId\" <> '00000000-0000-0000-0000-000000000000'");
        });
        stockSyncOutboxEntity
            .HasIndex(e => new { e.TenantId, e.VariantId, e.WarehouseId, e.EventVersion })
            .IsUnique()
            .HasDatabaseName("UX_StockSyncOutboxMessages_Tenant_Variant_Warehouse_EventVersion")
            .HasFilter("\"IsDeleted\" = false");
        stockSyncOutboxEntity
            .HasIndex(e => new
            {
                e.TenantId,
                e.VariantId,
                e.WarehouseId,
                e.EventVersion,
                e.StockUpdatedAtUtc,
                e.OccurredAtUtc,
                e.CreatedAt,
                e.Id
            })
            .HasDatabaseName("IX_StockSyncOutboxMessages_Unpublished_EventOrdering")
            .HasFilter("\"PublishedAtUtc\" IS NULL AND \"IsFailed\" = false AND \"IsDeleted\" = false");

        // Apply provider-specific filtered index for StockSyncOutboxMessage processing status
        // Note: Cannot include time-based comparisons (NOW(), GETUTCDATE()) in index predicates
        // as they require IMMUTABLE functions. Time filtering happens in queries instead.
        var processingStatusIndex = stockSyncOutboxEntity.Metadata
            .GetIndexes()
            .FirstOrDefault(i => i.Name == "IX_StockSyncOutbox_ProcessingStatus");
        
        if (processingStatusIndex != null)
        {
            if (Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
            {
                // PostgreSQL: Filter on static conditions only
                processingStatusIndex.SetFilter("\"PublishedAtUtc\" IS NULL AND \"IsFailed\" = false");
            }
            else if (Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true)
            {
                // SQL Server: Filter on static conditions only
                processingStatusIndex.SetFilter("[PublishedAtUtc] IS NULL AND [IsFailed] = 0");
            }
            // For other providers, no filter is applied
        }

        // Apply provider-specific filters for CanonicalIdentityLink unique indexes
        var canonicalIdentityLinkEntity = modelBuilder.Entity<CanonicalIdentityLink>();
        
        // Define the unique indexes with filters in EF Core model
        canonicalIdentityLinkEntity
            .HasIndex(e => new { e.SubjectType, e.SubjectId })
            .IsUnique()
            .HasDatabaseName("UX_CanonicalIdentityLinks_Global_Subject_Active")
            .HasFilter("\"TenantId\" IS NULL AND \"IsDeleted\" = false AND \"RevokedAtUtc\" IS NULL");
        
        canonicalIdentityLinkEntity
            .HasIndex(e => new { e.TenantId, e.SubjectType, e.SubjectId })
            .IsUnique()
            .HasDatabaseName("UX_CanonicalIdentityLinks_Tenant_Subject_Active")
            .HasFilter("\"TenantId\" IS NOT NULL AND \"IsDeleted\" = false AND \"RevokedAtUtc\" IS NULL");
        
        // Also define the non-unique index for lookups
        canonicalIdentityLinkEntity
            .HasIndex(e => new { e.TenantId, e.SubjectType, e.SubjectId })
            .HasDatabaseName("IX_CanonicalIdentityLinks_Tenant_Subject");
        
        // Apply provider-specific filter syntax
        var tenantSubjectActiveIndex = canonicalIdentityLinkEntity.Metadata
            .GetIndexes()
            .FirstOrDefault(i => i.Name == "UX_CanonicalIdentityLinks_Tenant_Subject_Active");
        
        if (tenantSubjectActiveIndex != null)
        {
            if (Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
            {
                tenantSubjectActiveIndex.SetFilter("\"TenantId\" IS NOT NULL AND \"IsDeleted\" = false AND \"RevokedAtUtc\" IS NULL");
            }
            else if (Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true)
            {
                tenantSubjectActiveIndex.SetFilter("[TenantId] IS NOT NULL AND [IsDeleted] = 0 AND [RevokedAtUtc] IS NULL");
            }
            else if (Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
            {
                tenantSubjectActiveIndex.SetFilter("\"TenantId\" IS NOT NULL AND \"IsDeleted\" = 0 AND \"RevokedAtUtc\" IS NULL");
            }
        }
        
        var globalSubjectActiveIndex = canonicalIdentityLinkEntity.Metadata
            .GetIndexes()
            .FirstOrDefault(i => i.Name == "UX_CanonicalIdentityLinks_Global_Subject_Active");
        
        if (globalSubjectActiveIndex != null)
        {
            if (Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
            {
                globalSubjectActiveIndex.SetFilter("\"TenantId\" IS NULL AND \"IsDeleted\" = false AND \"RevokedAtUtc\" IS NULL");
            }
            else if (Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true)
            {
                globalSubjectActiveIndex.SetFilter("[TenantId] IS NULL AND [IsDeleted] = 0 AND [RevokedAtUtc] IS NULL");
            }
            else if (Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
            {
                globalSubjectActiveIndex.SetFilter("\"TenantId\" IS NULL AND \"IsDeleted\" = 0 AND \"RevokedAtUtc\" IS NULL");
            }
        }

        modelBuilder.Entity<ProductVariantAttribute>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<ProductUnit>(entity =>
        {
            entity.HasQueryFilter(e =>
                !e.IsDeleted &&
                e.Product.TenantId == CurrentTenantId &&
                e.Unit.TenantId == CurrentTenantId);

            entity.ToTable("ProductUnits", table =>
            {
                table.HasCheckConstraint(
                    "CK_ProductUnits_ConversionFactor_Positive",
                    "\"ConversionFactor\" > 0");
                table.HasCheckConstraint(
                    "CK_ProductUnits_BaseUnit_ConversionFactor_One",
                    "NOT \"IsBaseUnit\" OR \"ConversionFactor\" = 1");
            });

            entity.HasIndex(e => new { e.ProductId, e.UnitId })
                .IsUnique()
                .HasDatabaseName("UX_ProductUnits_ProductId_UnitId_Active")
                .HasFilter("\"IsDeleted\" = false");

            entity.HasIndex(e => new { e.ProductId, e.IsBaseUnit })
                .IsUnique()
                .HasDatabaseName("UX_ProductUnits_ProductId_Base_Active")
                .HasFilter("\"IsDeleted\" = false AND \"IsBaseUnit\" = true");

            entity.HasIndex(e => new { e.ProductId, e.IsDefault })
                .IsUnique()
                .HasDatabaseName("UX_ProductUnits_ProductId_Default_Active")
                .HasFilter("\"IsDeleted\" = false AND \"IsDefault\" = true");

            entity.HasIndex(e => e.Barcode)
                .HasDatabaseName("IX_ProductUnits_Barcode_Active")
                .HasFilter("\"IsDeleted\" = false AND \"Barcode\" IS NOT NULL");
        });
        modelBuilder.Entity<PosCartItem>().HasQueryFilter(e =>
            !e.IsDeleted &&
            !e.PosCart.IsDeleted &&
            e.PosCart.TenantId == CurrentTenantId);
        modelBuilder.Entity<OrderItem>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<OrderCheckoutFieldSnapshot>().HasQueryFilter(e =>
            !e.IsDeleted && e.TenantId == CurrentTenantId);
        
        // -------------------------------------------------------------------------
        // Bank unique index (needs to be per-tenant now)
        // -------------------------------------------------------------------------
        modelBuilder.Entity<Bank>().HasIndex(b => new { b.TenantId, b.BankName })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        // MassTransit Outbox
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();

        modelBuilder.Entity<SystemSetting>(entity =>
        {
            entity.HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);

            entity.HasIndex(e => new { e.TenantId, e.Key })
                .IsUnique()
                .HasFilter("\"TenantId\" IS NOT NULL AND NOT \"IsDeleted\"");

            entity.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });


        // StoreConfig Configuration (Phase 7.1 - Theme & Store Config)
        // Uses EF Core Owned Entities pattern for complex nested types.
        // This maps to individual columns and works consistently across all database providers.
        modelBuilder.Entity<StoreConfig>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TenantId).IsUnique(); // One config per tenant
            entity.HasIndex(e => e.ThemeId); // Non-unique index for theme filtering
            entity.Property(e => e.StoreName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.StoreDescription).HasMaxLength(500);
            entity.Property(e => e.ThemeId).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ThemePresetKey).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ThemeLivePresetKey).HasMaxLength(100).IsRequired();
            
            // PHASE: Storefront Admin Hardening - P0
            // Configure RowVersion as PostgreSQL xmin system column for optimistic concurrency
            // This prevents silent overwrites when multiple admins edit simultaneously
            entity.Property(e => e.RowVersion)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .IsRowVersion();
            
            // Owned entities: Theme contains Colors, Font, Ui, Layout
            entity.OwnsOne(e => e.Theme, theme =>
            {
                theme.OwnsOne(t => t.Colors);
                theme.OwnsOne(t => t.Font);
                theme.OwnsOne(t => t.Ui);
                theme.OwnsOne(t => t.Layout);
            });
            // Owned entity: Behavior flags
            entity.OwnsOne(e => e.Behavior);
            
            // Owned entity: Promotion ranking configuration (Phase: Promotion-Aware Search)
            // Persisted as JSON columns in StoreConfigs table
            entity.OwnsOne(e => e.PromotionRanking, pr =>
            {
                // Note: CategoryOverrides is manually marked as [NotMapped] 
                // because EF Core cannot directly map Dictionary types.
                // It will be stored in a separate JSON column if needed in the future.
                pr.Property(p => p.Enabled).HasColumnName("PromotionRanking_Enabled");
                pr.Property(p => p.BaseBoost).HasColumnName("PromotionRanking_BaseBoost");
                pr.Property(p => p.HighDiscountBoost).HasColumnName("PromotionRanking_HighDiscountBoost");
                pr.Property(p => p.HighDiscountThreshold).HasColumnName("PromotionRanking_HighDiscountThreshold");
                pr.Property(p => p.MaxTotalBoost).HasColumnName("PromotionRanking_MaxTotalBoost");
                pr.Property(p => p.EnableTimeDecay).HasColumnName("PromotionRanking_EnableTimeDecay");
                pr.Property(p => p.TimeDecayScaleDays).HasColumnName("PromotionRanking_TimeDecayScaleDays");
                pr.Property(p => p.TimeDecayOffsetDays).HasColumnName("PromotionRanking_TimeDecayOffsetDays");
                pr.Property(p => p.TimeDecayMinimum).HasColumnName("PromotionRanking_TimeDecayMinimum");
            });
        });

        // Theme Preset Configuration (Phase 9.3)
        modelBuilder.Entity<ThemePreset>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Key).IsUnique();
            entity.Property(e => e.BaseThemeJson).IsRequired();
            
            // Seed system presets
            entity.HasData(
                new ThemePreset 
                { 
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), 
                    Key = "default", 
                    Name = "Default Modern", 
                    IsRtl = false,
                    BaseThemeJson = "{\"colors\":{\"primary\":\"#2563eb\",\"secondary\":\"#0f172a\",\"background\":\"#ffffff\",\"text\":\"#020617\",\"muted\":\"#64748b\"},\"typography\":{\"fontFamily\":\"Inter\",\"baseSize\":\"16px\"},\"radius\":{\"base\":\"8px\"}}",
                    CreatedAtUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new ThemePreset 
                { 
                    Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), 
                    Key = "dark-minimal", 
                    Name = "Dark Minimal", 
                    IsRtl = false,
                    BaseThemeJson = "{\"colors\":{\"primary\":\"#3b82f6\",\"secondary\":\"#1e293b\",\"background\":\"#0f172a\",\"text\":\"#f8fafc\",\"muted\":\"#94a3b8\"},\"typography\":{\"fontFamily\":\"Inter\",\"baseSize\":\"16px\"},\"radius\":{\"base\":\"12px\"}}",
                    CreatedAtUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            );
        });

        // OnboardingChecklist Configuration (Phase 8.0)
        modelBuilder.Entity<OnboardingChecklist>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TenantId).IsUnique(); // One checklist per tenant
            entity.Property(e => e.ItemsJson).IsRequired();
        });

        // Tenant Configuration (Phase 8.0 - Extended fields)
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Slug).IsUnique();
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.Currency); // S40: Index for FK lookups
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Slug).HasMaxLength(63);
            entity.Property(e => e.ContactEmail).HasMaxLength(255);
            entity.Property(e => e.Country).HasMaxLength(2);
            entity.Property(e => e.UseBaileys).HasDefaultValue(true);
            entity.Property(e => e.Currency)
                .HasMaxLength(3)
                .IsRequired()
                .HasDefaultValue("EGP");
            
            // S40: Foreign key to Currency reference table (replaces regex check constraint)
            entity.HasOne<Currency>()
                .WithMany()
                .HasForeignKey(e => e.Currency)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Currency Configuration (S40 - ISO 4217 Reference Table)
        modelBuilder.Entity<Currency>(entity =>
        {
            entity.HasKey(e => e.Code);
            entity.HasIndex(e => e.Name);
            entity.Property(e => e.Code).HasMaxLength(3).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Symbol).HasMaxLength(10).IsRequired();
            entity.Property(e => e.DecimalPlaces).IsRequired().HasDefaultValue(2);
            entity.ToTable("Currencies");

            entity.HasData(new Currency
            {
                Code = FinancialCurrencyGuard.PlatformBaseCurrencyCode,
                Name = "Egyptian Pound",
                Symbol = "EGP",
                DecimalPlaces = 2
            });
        });

        // User Configuration is owned by UserConfiguration
        // (normalized identity, xmin concurrency, tenant FK, indexes).
        // Applied via ApplyConfigurationsFromAssembly above; no inline
        // mapping here to avoid duplicate index definitions.

        // EmailVerificationToken Configuration (Phase 8.4)
        modelBuilder.Entity<EmailVerificationToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.ExpiresAtUtc);
            entity.Property(e => e.TokenHash).HasMaxLength(128).IsRequired();
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Audit Log Configuration
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasIndex(e => e.TimestampUtc);
            entity.HasIndex(e => e.ActionType);
            entity.HasIndex(e => e.EntityType);
        });

        // StoreConfigAuditEntry Configuration (Phase P3 - Hardening)
        modelBuilder.Entity<StoreConfigAuditEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.TimestampUtc);
            entity.HasIndex(e => e.ActionType);
            entity.HasIndex(e => new { e.TenantId, e.TimestampUtc });
            entity.Property(e => e.ActionType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Target).HasMaxLength(100).IsRequired();
            entity.Property(e => e.TraceId).HasMaxLength(128);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.UserName).HasMaxLength(256);
        });

        // TenantAnalyticsConfiguration (Phase 3 - Analytics Dispatchers)
        // Stores per-tenant analytics provider settings (GA4, Meta CAPI)
        // Secrets are encrypted using Data Protection API
        modelBuilder.Entity<PointOfSale.Infrastructure.Analytics.Dispatchers.TenantAnalyticsConfiguration>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TenantId).IsUnique(); // One config per tenant
            entity.Property(e => e.SettingsJson).IsRequired();
        });

        // WhatsAppAccount (Phase 13 - WhatsApp Messaging)
        // Strict tenant isolation + status index
        modelBuilder.Entity<WhatsAppAccount>(entity =>
        {
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
        });

        // TenantAutomationSettings (Phase: Smart Abandoned Cart Recovery Engine - Foundation)
        // Per-tenant automation configuration for e-commerce marketing
        modelBuilder.Entity<TenantAutomationSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TenantId).IsUnique(); // One settings record per tenant
            entity.HasIndex(e => e.EnableAbandonedRecovery); // For finding enabled tenants
            entity.Property(e => e.TimeZoneId).HasMaxLength(96).IsRequired();
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
        });

        // AbandonedCarts (Phase: Smart Abandoned Cart Recovery Engine - Phase 2)
        // Tracks cart lifecycle from active → abandoned → triggered → recovered
        modelBuilder.Entity<AbandonedCart>(entity =>
        {
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_AbandonedCarts_RecoveryStatus",
                "\"RecoveryStatus\" IN ('Active', 'Abandoned', 'Pending', 'Triggered', 'Recovered', 'Expired', 'Failed', 'Canceled')"));
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.CartId);
            entity.HasIndex(e => new { e.TenantId, e.CartId }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.SessionId })
                .HasDatabaseName("IX_AbandonedCarts_TenantId_SessionId")
                .HasFilter("\"SessionId\" IS NOT NULL");
            entity.HasIndex(e => new { e.RecoveryStatus, e.LastActivityAtUtc })
                .HasFilter("\"RecoveryStatus\" = 'Active'");
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            
            entity.Property(e => e.RecoveryStatus).HasConversion<string>();
            entity.Property(e => e.SessionId).HasMaxLength(128);
            entity.Property(e => e.ContactChannel).HasMaxLength(32);
            entity.Property(e => e.PhoneNumber).HasMaxLength(50);
            entity.Property(e => e.Email).HasMaxLength(255);
            
            // AbandonedCart uses its own timestamp columns (CreatedAtUtc, UpdatedAtUtc)
            // instead of BaseEntity's CreatedAt/UpdatedAt. Ignore the base properties.
            entity.Ignore(e => e.CreatedAt);
            entity.Ignore(e => e.UpdatedAt);
            entity.Ignore(e => e.IsDeleted);
            entity.Ignore(e => e.CreatedBy);
            entity.Ignore(e => e.UpdatedBy);
        });

        // AbandonedCartRecoveryLog (Phase: Abandoned Cart Hardening)
        // Tracks recovery workflow executions for analytics and debugging
        modelBuilder.Entity<AbandonedCartRecoveryLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.AbandonedCartId);
            // Composite unique constraint for idempotency under concurrency
            entity.HasIndex(e => new { e.TenantId, e.IdempotencyKey })
                .IsUnique()
                .HasDatabaseName("IX_AbandonedCartRecoveryLogs_TenantId_IdempotencyKey_Unique");
            entity.HasIndex(e => new { e.FirstMessageStatus, e.FirstMessageDueAtUtc });
            entity.HasIndex(e => new { e.SecondMessageStatus, e.SecondMessageDueAtUtc });
            entity.Property(e => e.FirstMessageStatus).HasMaxLength(32).IsRequired();
            entity.Property(e => e.SecondMessageStatus).HasMaxLength(32).IsRequired();
            entity.Property(e => e.FirstMessageLeaseId).HasMaxLength(64);
            entity.Property(e => e.SecondMessageLeaseId).HasMaxLength(64);
            entity.Property(e => e.FirstMessageDispatchLogId).HasMaxLength(64);
            entity.Property(e => e.SecondMessageDispatchLogId).HasMaxLength(64);
            entity.Property(e => e.TimeZoneId).HasMaxLength(96).IsRequired();
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
        });

        // CheckoutSessions (Phase: Abandoned Checkout / Lost Orders)
        // Captures partially completed checkout attempts for merchant recovery.
        // NOT an Order — never touches inventory, payments, or financial records.
        modelBuilder.Entity<CheckoutSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasAlternateKey(e => new { e.TenantId, e.Id })
                .HasName("AK_CheckoutSessions_TenantId_Id");
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.CartId);
            entity.HasIndex(e => new { e.TenantId, e.CartId });
            entity.HasIndex(e => new { e.TenantId, e.CartId, e.Status })
                .IsUnique()
                .HasFilter("\"Status\" = 0")
                .HasDatabaseName("IX_CheckoutSessions_TenantId_CartId_Active_Unique");
            // Scanner query: find Active sessions where LastActivityAtUtc < threshold
            entity.HasIndex(e => new { e.Status, e.LastActivityAtUtc })
                .HasDatabaseName("IX_CheckoutSessions_Status_LastActivity");
            entity.HasIndex(e => new { e.TenantId, e.Status, e.AbandonedAtUtc })
                .HasDatabaseName("IX_CheckoutSessions_TenantId_Status_AbandonedAtUtc");
            // Recovery token lookup — unique, unguessable, indexed
            entity.HasIndex(e => e.RecoveryToken)
                .IsUnique()
                .HasDatabaseName("IX_CheckoutSessions_RecoveryToken");
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.CartTotal).HasPrecision(18, 4);
            entity.Property(e => e.CustomerName).HasMaxLength(200);
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.City).HasMaxLength(100);
            entity.Property(e => e.Area).HasMaxLength(100);
            entity.Property(e => e.Address).HasMaxLength(500);
            entity.Property(e => e.Country).HasMaxLength(2); // ISO 3166-1 alpha-2 code
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.Currency).HasMaxLength(3);
            entity.Property(e => e.PaymentMethodCode).HasMaxLength(50);
            entity.Property(e => e.SessionId).HasMaxLength(100);
            entity.Property(e => e.ConvertedOrderNumber).HasMaxLength(50);
            entity.Property(e => e.RecoveryToken).HasMaxLength(64).IsRequired();
            entity.Property(e => e.DeviceType).HasMaxLength(20);
            entity.Property(e => e.Source).HasMaxLength(50);
            
            // CheckoutSession uses its own timestamp columns
            entity.Ignore(e => e.CreatedAt);
            entity.Ignore(e => e.UpdatedAt);
            entity.Ignore(e => e.IsDeleted);
            entity.Ignore(e => e.CreatedBy);
            entity.Ignore(e => e.UpdatedBy);
        });

        modelBuilder.Entity<WarehouseResolution>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);

            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(40);
            entity.Property(e => e.ResolutionSource).HasConversion<string>().HasMaxLength(80);
            entity.Property(e => e.AlgorithmCode).HasMaxLength(120).IsRequired();
            entity.Property(e => e.InputsSnapshotHash).HasMaxLength(128).IsRequired();
            entity.Property(e => e.FailureCode).HasMaxLength(120);
            entity.Property(e => e.InvalidationReason).HasMaxLength(200);

            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.TenantId, e.CheckoutSessionId, e.Status })
                .HasDatabaseName("IX_WarehouseResolutions_TenantId_Session_Status");
            entity.HasIndex(e => new { e.TenantId, e.CheckoutSessionId, e.ResolutionVersion })
                .IsUnique()
                .HasDatabaseName("UX_WarehouseResolutions_TenantId_Session_Version");
            entity.HasIndex(e => new { e.TenantId, e.CheckoutSessionId })
                .IsUnique()
                .HasFilter("\"Status\" IN ('Resolved', 'Locked') AND \"IsDeleted\" = false")
                .HasDatabaseName("IX_WarehouseResolutions_TenantId_Session_Active_Unique");
            entity.HasIndex(e => new { e.TenantId, e.CartId, e.Status })
                .HasDatabaseName("IX_WarehouseResolutions_TenantId_Cart_Status");
            entity.HasIndex(e => new { e.TenantId, e.WarehouseId, e.Status })
                .HasDatabaseName("IX_WarehouseResolutions_TenantId_Warehouse_Status");
            entity.HasIndex(e => new { e.TenantId, e.ExpiresAtUtc, e.Status })
                .HasDatabaseName("IX_WarehouseResolutions_TenantId_Expires_Status");
            entity.HasIndex(e => e.InputsSnapshotHash)
                .HasDatabaseName("IX_WarehouseResolutions_InputsSnapshotHash");

            entity.HasOne(e => e.CheckoutSession)
                .WithMany()
                .HasForeignKey(e => new { e.TenantId, e.CheckoutSessionId })
                .HasPrincipalKey(e => new { e.TenantId, e.Id })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Warehouse)
                .WithMany()
                .HasForeignKey(e => new { e.TenantId, e.WarehouseId })
                .HasPrincipalKey(e => new { e.TenantId, e.Id })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ConvertedOrder)
                .WithMany()
                .HasForeignKey(e => new { e.TenantId, e.ConvertedOrderId })
                .HasPrincipalKey(e => new { e.TenantId, e.Id })
                .OnDelete(DeleteBehavior.Restrict);

            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_WarehouseResolutions_ResolvedStatusesRequireWarehouse",
                    "\"Status\" NOT IN ('Resolved', 'Locked', 'Ordered') OR \"WarehouseId\" IS NOT NULL");
                table.HasCheckConstraint(
                    "CK_WarehouseResolutions_ResolutionVersion_Positive",
                    "\"ResolutionVersion\" > 0");
                table.HasCheckConstraint(
                    "CK_WarehouseResolutions_OrderedRequiresOrder",
                    "\"Status\" <> 'Ordered' OR \"ConvertedOrderId\" IS NOT NULL");
                table.HasCheckConstraint(
                    "CK_WarehouseResolutions_FailedRequiresCode",
                    "\"Status\" <> 'Failed' OR \"FailureCode\" IS NOT NULL");
            });
        });

        modelBuilder.Entity<StorefrontCartCouponState>(entity =>
        {
            entity.HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<ShippingWebhookLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.TenantId, e.CarrierCode, e.EventId })
                .IsUnique()
                .HasDatabaseName("IX_ShippingWebhookLogs_TenantId_CarrierCode_EventId");
            entity.HasIndex(e => new { e.TenantId, e.CarrierCode, e.PayloadHash })
                .IsUnique()
                .HasDatabaseName("IX_ShippingWebhookLogs_TenantId_CarrierCode_PayloadHash");
            // Filtered index for tracking number lookups - only indexes rows with tracking numbers
            entity.HasIndex(e => new { e.TenantId, e.TrackingNumber, e.CreatedAt })
                .HasDatabaseName("IX_ShippingWebhookLogs_TenantId_TrackingNumber_CreatedAt")
                .HasFilter("\"TrackingNumber\" IS NOT NULL");

            entity.Property(e => e.CarrierCode).HasMaxLength(50).IsRequired();
            entity.Property(e => e.EventId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.PayloadHash).HasMaxLength(128).IsRequired();
            entity.Property(e => e.EventType).HasMaxLength(200).IsRequired();
            entity.Property(e => e.TrackingNumber).HasMaxLength(100); // Nullable - webhooks may arrive without tracking numbers
            entity.Property(e => e.PayloadSummary).HasMaxLength(1000).IsRequired();
            entity.Property(e => e.Signature).HasMaxLength(1000);
            entity.Property(e => e.ValidationMethod).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ProcessingError).HasMaxLength(1000);
            entity.Property(e => e.SourceIpPrefix).HasMaxLength(50);
            entity.Property(e => e.UserAgentSummary).HasMaxLength(200);
        });

        // PixelTrackingConfig (Phase M1 - Pixel Override)
        // Configuration for overriding pixel conversion values
        modelBuilder.Entity<PixelTrackingConfig>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TenantId).IsUnique(); // One config per tenant
            entity.Property(e => e.Value).HasPrecision(18, 4);
            entity.Property(e => e.AppliesToJson).IsRequired();
            
            // Tenant isolation (Config entity, no soft-delete usually needed but adhering to interface if any)
            // It implements ITenantOwned, so it has TenantId.
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
        });

        // ProductPixelOverride (Phase M2 - Per-Product Pixel Override)
        // Configuration for overriding pixel conversion values at the product level
        modelBuilder.Entity<ProductPixelOverride>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TenantId, e.ProductId }).IsUnique(); // One override per product per tenant
            entity.HasIndex(e => e.TenantId); // For tenant-level lookups
            entity.Property(e => e.Value).HasPrecision(18, 4);
            
            // Relationships
            entity.HasOne(e => e.Product)
                .WithMany()
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            // Tenant constraint
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
        });

        // AnalyticsAlertRule (Phase 4 - Realtime Business Alerts)
        // Threshold-based alert rules evaluated by background job
        modelBuilder.Entity<PointOfSale.Core.Analytics.AnalyticsAlertRule>(entity =>
        {
            entity.HasKey(e => e.RuleId);
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.TenantId, e.IsEnabled, e.IsDeleted }); // For evaluator queries
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Threshold).HasPrecision(18, 4);
            entity.HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);
        });

        // CampaignCost (Phase 8B - Campaign Cost & ROI)
        // Admin-declared marketing spend per campaign for ROI calculations
        modelBuilder.Entity<PointOfSale.Core.Analytics.Attribution.CampaignCost>(entity =>
        {
            entity.HasKey(e => e.CampaignCostId);
            entity.HasIndex(e => e.TenantId);
            
            // Unique index: (TenantId, Source, Medium, Campaign, PeriodStartUtc)
            // One cost entry per campaign per period start
            entity.HasIndex(e => new { e.TenantId, e.Source, e.Medium, e.Campaign, e.PeriodStartUtc })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");
            
            // Index for period range queries
            entity.HasIndex(e => new { e.TenantId, e.PeriodStartUtc, e.PeriodEndUtc });
            
            // Field constraints
            entity.Property(e => e.Source).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Medium).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Campaign).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Currency).HasMaxLength(3).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.CostAmount).HasPrecision(18, 2);
            
            // Tenant isolation filter
            entity.HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);
        });

        // -------------------------------------------------------------------------
        // REMOVED: Global seed data for FinancialAccount
        // 
        // Phase: Tenant Isolation Hardening
        // 
        // Global seed data violates tenant isolation! Financial accounts (including
        // the Visa Account) MUST be created per-tenant during tenant bootstrap.
        // See TenantRegistrationService.cs for the bootstrap logic.
        // -------------------------------------------------------------------------

        // Product lookup and tenant-scoped identity indexes
        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasIndex(p => new { p.TenantId, p.Barcode })
                .IsUnique()
                .HasDatabaseName("IX_Products_TenantId_Barcode")
                .HasFilter("\"IsDeleted\" = false AND \"Barcode\" IS NOT NULL");
            entity.HasIndex(p => new { p.TenantId, p.SKU })
                .IsUnique()
                .HasDatabaseName("IX_Products_TenantId_SKU")
                .HasFilter("\"IsDeleted\" = false AND \"SKU\" IS NOT NULL");
            entity.HasIndex(p => new { p.TenantId, p.Slug })
                .HasDatabaseName("IX_Products_TenantId_Slug")
                .HasFilter("\"IsDeleted\" = false AND \"Slug\" IS NOT NULL");
            entity.HasIndex(p => p.PublishStatus);
            entity.HasIndex(p => p.IsOnlineVisible);
            entity.Property(p => p.RowVersion)
                .HasColumnName("xmin")
                .IsRowVersion();
        });

        modelBuilder.Entity<Return>(entity =>
        {
            entity.Property(returnEntity => returnEntity.CashRefundAmount).HasPrecision(18, 2);
            entity.Property(returnEntity => returnEntity.CardRefundAmount).HasPrecision(18, 2);
            entity.Property(returnEntity => returnEntity.CustomerCreditAmount).HasPrecision(18, 2);

            entity.HasIndex(r => new { r.TenantId, r.ReturnNumber })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");

            entity.HasIndex(r => new { r.TenantId, r.OriginalPurchaseInvoiceId });

            entity.HasOne(r => r.OriginalPurchaseInvoice)
                .WithMany()
                .HasForeignKey(r => new { r.TenantId, r.OriginalPurchaseInvoiceId })
                .HasPrincipalKey(invoice => new { invoice.TenantId, invoice.Id })
                .OnDelete(DeleteBehavior.Restrict);

            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_Returns_PurchaseAuthorityLineage",
                    $"(\"ReturnType\" <> {(int)ReturnType.PurchaseReturn}) OR " +
                    "(\"OriginalPurchaseInvoiceId\" IS NOT NULL AND \"OriginalInvoiceId\" IS NULL AND \"SupplierId\" IS NOT NULL AND \"CustomerId\" IS NULL)");
                table.HasCheckConstraint(
                    "CK_Returns_RefundAmounts_NonNegative",
                    "\"CashRefundAmount\" >= 0 AND \"CardRefundAmount\" >= 0 AND \"CustomerCreditAmount\" >= 0");
                table.HasCheckConstraint(
                    "CK_Returns_SettlementSnapshot_Consistent",
                    "(\"RefundMethod\" IS NULL AND \"CashRefundAmount\" = 0 AND \"CardRefundAmount\" = 0 AND \"CustomerCreditAmount\" = 0) OR " +
                    "(\"ReturnType\" = 1 AND ((\"RefundMethod\" = 0 AND \"CashRefundAmount\" = \"TotalAmount\" AND \"CardRefundAmount\" = 0 AND \"CustomerCreditAmount\" = 0) OR " +
                    "(\"RefundMethod\" = 1 AND \"CashRefundAmount\" = 0 AND \"CardRefundAmount\" = \"TotalAmount\" AND \"CustomerCreditAmount\" = 0) OR " +
                    "(\"RefundMethod\" = 2 AND \"CashRefundAmount\" > 0 AND \"CardRefundAmount\" > 0 AND \"CashRefundAmount\" + \"CardRefundAmount\" = \"TotalAmount\" AND \"CustomerCreditAmount\" = 0) OR " +
                    "(\"RefundMethod\" = 3 AND \"CashRefundAmount\" = 0 AND \"CardRefundAmount\" = 0 AND \"CustomerCreditAmount\" = \"TotalAmount\"))) OR " +
                    "(\"ReturnType\" = 0 AND ((\"RefundMethod\" = 0 AND \"CashRefundAmount\" = \"TotalAmount\" AND \"CardRefundAmount\" = 0 AND \"CustomerCreditAmount\" = 0) OR " +
                    "(\"RefundMethod\" = 3 AND \"CashRefundAmount\" = 0 AND \"CardRefundAmount\" = 0 AND \"CustomerCreditAmount\" = 0)))");
            });
        });

        modelBuilder.Entity<PurchaseInvoiceItem>().ToTable(table =>
        {
            table.HasCheckConstraint("CK_PurchaseInvoiceItems_Quantity_Positive", "\"Quantity\" > 0");
            table.HasCheckConstraint("CK_PurchaseInvoiceItems_ConversionFactor_Positive", "\"ConversionFactor\" > 0");
            table.HasCheckConstraint("CK_PurchaseInvoiceItems_BaseQuantity_Positive", "\"BaseQuantity\" > 0");
            table.HasCheckConstraint("CK_PurchaseInvoiceItems_UnitPrice_Positive", "\"UnitPrice\" > 0");
            table.HasCheckConstraint("CK_PurchaseInvoiceItems_TaxRate_Valid", "\"TaxRate\" >= 0 AND \"TaxRate\" <= 100");
        });

        modelBuilder.Entity<ReturnItem>().ToTable(table =>
        {
            table.HasCheckConstraint("CK_ReturnItems_Quantity_Positive", "\"Quantity\" > 0");
            table.HasCheckConstraint("CK_ReturnItems_ConversionFactor_Positive", "\"ConversionFactor\" > 0");
            table.HasCheckConstraint("CK_ReturnItems_BaseQuantity_Positive", "\"BaseQuantity\" > 0");
            table.HasCheckConstraint("CK_ReturnItems_UnitPrice_NonNegative", "\"UnitPrice\" >= 0");
            table.HasCheckConstraint("CK_ReturnItems_TaxRate_Valid", "\"TaxRate\" >= 0 AND \"TaxRate\" <= 100");
            table.HasCheckConstraint("CK_ReturnItems_Amounts_NonNegative", "\"TaxAmount\" >= 0 AND \"TotalAmount\" >= 0");
        });

        // Order configuration
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_Orders_FinancialPostingStatus_Valid",
                    "\"FinancialPostingStatus\" IN (0, 1, 2, 3) AND " +
                    "(\"FinancialPostingStatus\" <> 1 OR \"FinancialPostingPendingAtUtc\" IS NOT NULL) AND " +
                    "(\"FinancialPostingStatus\" <> 2 OR (\"FinancialPostedAtUtc\" IS NOT NULL AND \"FinancialPostingJournalId\" IS NOT NULL)) AND " +
                    "(\"FinancialPostingStatus\" <> 3 OR \"FinancialPostingFailureReason\" IS NOT NULL)");

                table.HasCheckConstraint(
                    "CK_Orders_CustomerAuthorityBoundary",
                    "(\"CustomerId\" IS NULL OR \"OnlineCustomerId\" IS NULL) AND " +
                    $"(\"Source\" <> {(int)OrderSource.Ecommerce} OR \"CustomerId\" IS NULL)");
            });

            var activeCartOrderFilter =
                $"\"IsDeleted\" = false AND \"CartId\" IS NOT NULL AND \"Status\" IN ({(int)OrderStatus.Pending}, {(int)OrderStatus.Confirmed}, {(int)OrderStatus.Processing}, {(int)OrderStatus.OnHold})";

            entity.HasAlternateKey(o => new { o.TenantId, o.Id })
                .HasName("AK_Orders_TenantId_Id");
            entity.HasIndex(o => new { o.TenantId, o.OrderNumber })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false")
                .HasDatabaseName("IX_Orders_TenantId_OrderNumber");
            entity.HasIndex(o => new { o.TenantId, o.CartId })
                .IsUnique()
                .HasFilter(activeCartOrderFilter)
                .HasDatabaseName("IX_Orders_TenantId_CartId_ActiveStatuses");
            entity.HasIndex(o => new { o.TenantId, o.GuestPhone })
                .HasDatabaseName("IX_Orders_TenantId_GuestPhone");
            entity.HasIndex(o => new { o.TenantId, o.Source, o.CreatedAt })
                .HasDatabaseName("IX_Orders_TenantId_Source_CreatedAt");
            entity.HasIndex(o => new { o.TenantId, o.Status, o.CreatedAt })
                .HasDatabaseName("IX_Orders_TenantId_Status_CreatedAt");
            entity.HasIndex(o => new { o.TenantId, o.PaymentStatus, o.CreatedAt })
                .HasDatabaseName("IX_Orders_TenantId_PaymentStatus_CreatedAt");
            entity.HasIndex(o => new { o.TenantId, o.FinancialPostingStatus, o.CreatedAt })
                .HasDatabaseName("IX_Orders_TenantId_FinancialPostingStatus_CreatedAt");
            entity.HasIndex(o => o.Status);
            entity.HasIndex(o => o.PaymentStatus);
            entity.HasIndex(o => o.Source);
            entity.HasIndex(o => o.CreatedAt);
            entity.HasIndex(o => new { o.TenantId, o.Source, o.SourceOrderId })
                .IsUnique()
                .HasDatabaseName("IX_Orders_TenantId_Source_SourceOrderId")
                .HasFilter("\"IsDeleted\" = false AND \"SourceOrderId\" IS NOT NULL");
            entity.HasIndex(o => o.SessionId);
            entity.HasIndex(o => new { o.TenantId, o.CheckoutState, o.CreatedAt })
                .HasDatabaseName("IX_Orders_TenantId_CheckoutState_CreatedAt");
            entity.Property(o => o.IpAddress).HasMaxLength(45);
            entity.Property(o => o.SessionId).HasMaxLength(100);
            entity.Property(o => o.CheckoutState).HasConversion<int>();
            entity.Property<uint>("xmin")
                .HasColumnName("xmin")
                .IsRowVersion();
            entity.Property(o => o.Subtotal).HasPrecision(18, 2);
            entity.Property(o => o.PromotionDiscountAmount).HasPrecision(18, 2);
            entity.Property(o => o.DiscountAmount).HasPrecision(18, 2);
            entity.Property(o => o.ManualDiscountAmount).HasPrecision(18, 2);
            entity.Property(o => o.TotalDiscountAmount).HasPrecision(18, 2);
            entity.Property(o => o.ShippingCost).HasPrecision(18, 2);
            entity.Property(o => o.TaxAmount).HasPrecision(18, 2);
            entity.Property(o => o.PaymentFee).HasPrecision(18, 2);
            entity.Property(o => o.Total).HasPrecision(18, 2);
            entity.Property(o => o.PricingSnapshotJson).HasColumnType("text");
            entity.Property(o => o.ShippingServiceLevel).HasMaxLength(50);
            entity.Property(o => o.DeliveryReference).HasMaxLength(200);
            entity.Property(o => o.ProofOfDeliveryUrl).HasMaxLength(2048);
            entity.Property(o => o.FinancialPostingStatus).HasConversion<int>();
            entity.Property(o => o.FinancialPostingFailureReason).HasMaxLength(1000);
            ConfigureOrderGeographicSnapshotProperties(entity);

            entity.HasOne(o => o.Customer)
                .WithMany()
                .HasForeignKey(o => o.CustomerId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(o => o.ShippingMethod)
                .WithMany()
                .HasForeignKey(o => o.ShippingMethodId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(o => o.Coupon)
                .WithMany()
                .HasForeignKey(o => o.CouponId)
                .OnDelete(DeleteBehavior.SetNull);

            // Promotion (PHASE: Promotions Engine) - auto-applied offers
            entity.HasOne(o => o.AppliedPromotion)
                .WithMany()
                .HasForeignKey(o => o.AppliedPromotionId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(o => o.Warehouse)
                .WithMany()
                .HasForeignKey(o => o.WarehouseId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<StockTransfer>(entity =>
        {
            entity.HasIndex(t => new { t.TenantId, t.TransferNumber })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");
        });

        // OrderItem configuration
        // PHASE 11.4.1: Added VariantId for variant-based checkout
        // PHASE 11.4.2.A: Added financial ledger fields
        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_OrderItems_AffiliateCommissionSnapshot_Valid",
                    // Mutually exclusive states: either eligible WITH snapshots OR ineligible WITHOUT snapshots
                    "(" +
                    // State 1: Eligible = true requires all snapshot fields populated and valid
                    "\"AffiliateCommissionEligible\" = true AND " +
                    "\"AffiliateCommissionSnapshotCapturedAtUtc\" IS NOT NULL AND " +
                    "\"AffiliateCommissionPolicyIdSnapshot\" IS NOT NULL AND " +
                    "\"AffiliateCommissionRateSnapshot\" > 0 AND " +
                    "\"AffiliateCommissionRateSnapshot\" <= 0.500000 AND " +
                    "\"AffiliateCommissionBaseAmountSnapshot\" > 0 AND " +
                    "\"AffiliateCommissionAmountSnapshot\" > 0 AND " +
                    "(\"AffiliateMaxCommissionPerItemSnapshot\" IS NULL OR \"AffiliateMaxCommissionPerItemSnapshot\" > 0)" +
                    ") OR (" +
                    // State 2: Eligible = false requires all snapshot fields cleared/null
                    "\"AffiliateCommissionEligible\" = false AND " +
                    "\"AffiliateCommissionSnapshotCapturedAtUtc\" IS NULL AND " +
                    "\"AffiliateCommissionPolicyIdSnapshot\" IS NULL AND " +
                    "\"AffiliateCommissionRateSnapshot\" IS NULL AND " +
                    "\"AffiliateCommissionBaseAmountSnapshot\" IS NULL AND " +
                    "\"AffiliateCommissionAmountSnapshot\" IS NULL AND " +
                    "(\"AffiliateMaxCommissionPerItemSnapshot\" IS NULL OR \"AffiliateMaxCommissionPerItemSnapshot\" = 0)" +
                    ")");
                table.HasCheckConstraint(
                    "CK_OrderItems_PaymentAmounts_Valid",
                    "\"PaidAmount\" >= 0 AND \"RefundedAmount\" >= 0 AND \"RefundedAmount\" <= \"PaidAmount\"");
            });

            entity.HasIndex(oi => oi.OrderId);
            entity.HasIndex(oi => oi.ProductId);
            // PHASE 11.4.1: Index on VariantId for SKU-level queries
            entity.HasIndex(oi => oi.VariantId);
            
            // Composite unique index for order-scoped FK from OrderRefundLine
            entity.HasIndex(oi => new { oi.OrderId, oi.Id })
                .IsUnique()
                .HasDatabaseName("IX_OrderItems_OrderId_Id");

            entity.HasOne(oi => oi.Order)
                .WithMany(o => o.Items)
                .HasForeignKey(oi => oi.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(oi => oi.Product)
                .WithMany()
                .HasForeignKey(oi => oi.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            // PHASE 11.4.1: Variant relationship (optional - legacy orders may not have VariantId)
            entity.HasOne(oi => oi.Variant)
                .WithMany()
                .HasForeignKey(oi => oi.VariantId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(oi => oi.Unit)
                .WithMany()
                .HasForeignKey(oi => oi.UnitId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(oi => oi.Warehouse)
                .WithMany()
                .HasForeignKey(oi => oi.WarehouseId)
                .OnDelete(DeleteBehavior.SetNull);
            
            // PHASE 11.4.2.A: Financial ledger field precision
            entity.Property(oi => oi.PaidAmount).HasPrecision(18, 2);
            entity.Property(oi => oi.RefundedAmount).HasPrecision(18, 2);
            entity.Property(oi => oi.AffiliateCommissionRateSnapshot).HasPrecision(18, 6);
            entity.Property(oi => oi.AffiliateMaxCommissionPerItemSnapshot).HasPrecision(18, 4);
            entity.Property(oi => oi.AffiliateCommissionBaseAmountSnapshot).HasPrecision(18, 4);
            entity.Property(oi => oi.AffiliateCommissionAmountSnapshot).HasPrecision(18, 4);
            
            // PHASE 11.4.2.A: Ignore computed properties (they are not stored in DB)
            entity.Ignore(oi => oi.Subtotal);
            entity.Ignore(oi => oi.IsFullyPaid);
            entity.Ignore(oi => oi.IsFullyRefunded);
            entity.Ignore(oi => oi.NetPaidAmount);
            entity.Ignore(oi => oi.RemainingAmount);
        });

        modelBuilder.Entity<OrderRefund>(entity =>
        {
            entity.HasAlternateKey(refund => new { refund.TenantId, refund.OrderId, refund.Id })
                .HasName("AK_OrderRefunds_TenantId_OrderId_Id");
            entity.HasIndex(refund => new { refund.TenantId, refund.OrderId });
            entity.HasIndex(refund => new { refund.TenantId, refund.OrderId, refund.RefundId })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false")
                .HasDatabaseName("IX_OrderRefunds_TenantId_OrderId_RefundId");
            entity.HasIndex(refund => new { refund.TenantId, refund.SettlementBatchId })
                .HasFilter("\"SettlementBatchId\" IS NOT NULL AND \"IsDeleted\" = false")
                .HasDatabaseName("IX_OrderRefunds_TenantId_SettlementBatchId");

            entity.Property(refund => refund.RefundId)
                .IsRequired()
                .HasMaxLength(120);
            entity.Property(refund => refund.Amount).HasPrecision(18, 2);
            entity.Property(refund => refund.Currency).HasMaxLength(3);
            entity.Property(refund => refund.Reason).HasMaxLength(1000);
            entity.Property(refund => refund.PaymentMethod).HasMaxLength(100);
            entity.Property(refund => refund.SettlementType).HasMaxLength(32);
            entity.Property(refund => refund.SettlementId).HasMaxLength(200);
            entity.Property(refund => refund.PayoutBatchId).HasMaxLength(200);
            entity.Property(refund => refund.FinancialPostingStatus)
                .HasConversion<int>()
                .HasDefaultValue(FinancialPostingStatus.Pending);
            entity.Property(refund => refund.FinancialPostingPendingAtUtc)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(refund => refund.FinancialPostingFailureReason).HasMaxLength(1000);
            entity.HasIndex(refund => new { refund.TenantId, refund.FinancialPostingStatus, refund.RefundedAtUtc })
                .HasDatabaseName("IX_OrderRefunds_TenantId_FinancialPostingStatus_RefundedAtUtc");
            entity.HasCheckConstraint(
                "CK_OrderRefunds_FinancialPostingStatus_Valid",
                "\"FinancialPostingStatus\" IN (1, 2, 3) AND " +
                "(\"FinancialPostingStatus\" <> 1 OR \"FinancialPostingPendingAtUtc\" IS NOT NULL) AND " +
                "(\"FinancialPostingStatus\" <> 2 OR (\"FinancialPostedAtUtc\" IS NOT NULL AND \"FinancialPostingJournalId\" IS NOT NULL)) AND " +
                "(\"FinancialPostingStatus\" <> 3 OR \"FinancialPostingFailureReason\" IS NOT NULL)");

            entity.HasOne(refund => refund.Order)
                .WithMany(order => order.Refunds)
                .HasForeignKey(refund => new { refund.TenantId, refund.OrderId })
                .HasPrincipalKey(order => new { order.TenantId, order.Id })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<PaymentSettlementBatch>()
                .WithMany(batch => batch.OrderRefunds)
                .HasForeignKey(refund => refund.SettlementBatchId)
                .HasPrincipalKey(batch => batch.Id)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrderRefundLine>(entity =>
        {
            entity.HasIndex(line => line.OrderRefundId);
            entity.HasIndex(line => line.OrderItemId);
            entity.HasIndex(line => new { line.OrderRefundId, line.OrderItemId })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false")
                .HasDatabaseName("IX_OrderRefundLines_Refund_OrderItem");

            entity.Property(line => line.Amount).HasPrecision(18, 2);

            // Enforce non-negative amount at database level
            entity.HasCheckConstraint("CK_OrderRefundLines_Amount_NonNegative", "\"Amount\" >= 0");

            entity.HasOne(line => line.OrderRefund)
                .WithMany(refund => refund.Lines)
                .HasForeignKey(line => new { line.TenantId, line.OrderId, line.OrderRefundId })
                .HasPrincipalKey(refund => new { refund.TenantId, refund.OrderId, refund.Id })
                .OnDelete(DeleteBehavior.Cascade);

            // Composite FK: Ensures OrderItemId references an item from the correct order
            entity.HasOne(line => line.OrderItem)
                .WithMany()
                .HasForeignKey(line => new { line.OrderId, line.OrderItemId })
                .HasPrincipalKey(item => new { item.OrderId, item.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CommerceHubMarketplaceOrder>(entity =>
        {
            entity.HasIndex(order => order.OrderNumber).IsUnique();
            entity.HasIndex(order => order.CustomerId);
            entity.HasIndex(order => order.IdempotencyKey)
                .IsUnique()
                .HasFilter("\"IdempotencyKey\" IS NOT NULL");
            entity.Property(order => order.Subtotal).HasPrecision(18, 2);
            entity.Property(order => order.ShippingTotal).HasPrecision(18, 2);
            entity.Property(order => order.TotalAmount).HasPrecision(18, 2);
            entity.Property(order => order.Currency).HasMaxLength(10);
            entity.Property(order => order.IdempotencyKey).HasMaxLength(100);
            entity.Property(order => order.FullName).HasMaxLength(200);
            entity.Property(order => order.PhoneNumber).HasMaxLength(50);
            entity.Property(order => order.AddressLine).HasMaxLength(500);
            entity.Property(order => order.City).HasMaxLength(100);
            entity.Property(order => order.Area).HasMaxLength(100);
            entity.Property(order => order.PaymentMethodCode).HasMaxLength(50);
            entity.Property(order => order.PaymentMethodName).HasMaxLength(100);
            entity.Property(order => order.PaymentReference).HasMaxLength(100);
            entity.Property(order => order.PaymentReferenceId).HasMaxLength(100);
            entity.Property(order => order.PaymentGateway).HasMaxLength(50);
            entity.Property(order => order.Notes).HasMaxLength(1000);

            entity.HasOne(order => order.Customer)
                .WithMany(customer => customer.Orders)
                .HasForeignKey(order => order.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CommerceHubCart>(entity =>
        {
            entity.ToTable("CommerceHubCarts", table =>
            {
                table.HasCheckConstraint(
                    "CK_CommerceHubCarts_CustomerAuthorityBoundary",
                    "\"CommerceHubCustomerId\" IS NOT NULL AND \"CommerceHubCustomerId\" <> '00000000-0000-0000-0000-000000000000' AND \"OnlineCustomerId\" IS NOT NULL AND \"OnlineCustomerId\" <> '00000000-0000-0000-0000-000000000000' AND \"OnlineCustomerId\" = \"CommerceHubCustomerId\"");
            });

            entity.HasIndex(cart => new { cart.TenantId, cart.CommerceHubCustomerId })
                .IsUnique()
                .HasDatabaseName("UX_CommerceHubCarts_TenantId_CommerceHubCustomerId_Active")
                .HasFilter("\"CommerceHubCustomerId\" IS NOT NULL AND \"IsDeleted\" = false");
            entity.Property(cart => cart.ItemsJson).HasColumnType("text");
            entity.Property(cart => cart.Version)
                .HasDefaultValue(1)
                .IsConcurrencyToken();
        });

        // CommerceHubCheckoutIdempotency configuration is applied via CommerceHubCheckoutIdempotencyConfiguration
        // (loaded by ApplyConfigurationsFromAssembly) - no inline mapping needed here

        modelBuilder.Entity<PurchaseRequestIdempotency>(entity =>
        {
            entity.ToTable("PurchaseRequestIdempotency");
            entity.HasKey(record => record.Id);
            entity.HasIndex(record => new { record.TenantId, record.Key, record.Operation })
                .IsUnique()
                .HasDatabaseName("IX_PurchaseRequestIdempotency_TenantId_Key_Operation");
            entity.HasIndex(record => new { record.TenantId, record.PurchaseInvoiceId });
            entity.Property(record => record.Key).HasMaxLength(255);
            entity.Property(record => record.Operation).HasMaxLength(32);
            entity.Property(record => record.PurchaseAttemptId).HasMaxLength(200);
            entity.Property(record => record.RequestFingerprint).HasMaxLength(64);
            entity.Property(record => record.Status).HasMaxLength(32);
            entity.Property(record => record.ResponseJson).HasColumnType("text");
        });

        modelBuilder.Entity<ProductRequestIdempotency>(entity =>
        {
            entity.ToTable("ProductRequestIdempotency");
            entity.HasKey(record => record.Id);
            entity.HasIndex(record => new { record.TenantId, record.Key, record.Operation })
                .IsUnique()
                .HasDatabaseName("IX_ProductRequestIdempotency_TenantId_Key_Operation");
            entity.HasIndex(record => new { record.TenantId, record.ProductId });
            entity.HasIndex(record => new { record.TenantId, record.ProductVariantId });
            entity.Property(record => record.Key).HasMaxLength(200);
            entity.Property(record => record.Operation).HasMaxLength(32);
            entity.Property(record => record.RequestFingerprint).HasMaxLength(64);
            entity.Property(record => record.ResponseJson).HasColumnType("text");
        });

        modelBuilder.Entity<AccountLedger>(entity =>
        {
            entity.ToTable("AccountLedgers");
            entity.HasKey(entry => entry.Id);
            entity.HasIndex(entry => entry.UserId);
            
            var supplierOpeningFilterExpression = $"\"IsDeleted\" = false AND \"PartyType\" = {(int)PartyType.Supplier} AND \"TransactionType\" = {(int)TransactionType.OpeningBalance}";
            var customerOpeningFilterExpression = $"\"IsDeleted\" = false AND \"PartyType\" = {(int)PartyType.Customer} AND \"TransactionType\" = {(int)TransactionType.OpeningBalance}";
            
            entity.HasIndex(entry => new
                {
                    entry.TenantId,
                    entry.PartyType,
                    entry.PartyId,
                    entry.TransactionType
                })
                .IsUnique()
                .HasFilter(supplierOpeningFilterExpression)
                .HasDatabaseName("IX_AccountLedgers_SupplierOpeningBalance_Once");

            entity.HasIndex(entry => new
                {
                    entry.TenantId,
                    entry.PartyType,
                    entry.PartyId,
                    entry.TransactionType
                })
                .IsUnique()
                .HasFilter(customerOpeningFilterExpression)
                .HasDatabaseName("IX_AccountLedgers_CustomerOpeningBalance_Once");
        });

        modelBuilder.Entity<CustomerTransaction>(entity =>
        {
            entity.ToTable("CustomerTransactions");
            entity.HasKey(transaction => transaction.Id);

            var filterExpression = $"\"IsDeleted\" = false AND \"TransactionType\" = {(int)TransactionType.OpeningBalance}";

            entity.HasIndex(transaction => new
                {
                    transaction.TenantId,
                    transaction.CustomerId,
                    transaction.TransactionType
                })
                .IsUnique()
                .HasFilter(filterExpression)
                .HasDatabaseName("IX_CustomerTransactions_OpeningBalance_Once");
        });

        modelBuilder.Entity<SupplierTransaction>(entity =>
        {
            entity.ToTable("SupplierTransactions");
            entity.HasKey(transaction => transaction.Id);
            
            var filterExpression = $"\"IsDeleted\" = false AND \"TransactionType\" = {(int)TransactionType.OpeningBalance}";
            
            entity.HasIndex(transaction => new
                {
                    transaction.TenantId,
                    transaction.SupplierId,
                    transaction.TransactionType
                })
                .IsUnique()
                .HasFilter(filterExpression)
                .HasDatabaseName("IX_SupplierTransactions_OpeningBalance_Once");
        });

        modelBuilder.Entity<SupplierPayment>(entity =>
        {
            entity.ToTable("SupplierPayments");
            entity.HasKey(payment => payment.Id);
            entity.HasIndex(payment => new { payment.TenantId, payment.IdempotencyKey })
                .IsUnique()
                .HasDatabaseName("IX_SupplierPayments_TenantId_IdempotencyKey");
            entity.HasIndex(payment => new { payment.TenantId, payment.SupplierId, payment.PaymentDate });
            entity.Property(payment => payment.Amount).HasPrecision(18, 2);
            entity.Property(payment => payment.IdempotencyKey).HasMaxLength(100);
            entity.Property(payment => payment.ReferenceNumber).HasMaxLength(100);
            entity.Property(payment => payment.Notes).HasMaxLength(1000);

            entity.HasOne(payment => payment.Supplier)
                .WithMany()
                .HasForeignKey(payment => payment.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(payment => payment.PurchaseInvoice)
                .WithMany()
                .HasForeignKey(payment => payment.PurchaseInvoiceId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PartyLedgerAdjustment>(entity =>
        {
            entity.ToTable("PartyLedgerAdjustments");
            entity.HasKey(adjustment => adjustment.Id);
            entity.HasIndex(adjustment => new { adjustment.TenantId, adjustment.IdempotencyKey })
                .IsUnique()
                .HasDatabaseName("IX_PartyLedgerAdjustments_TenantId_IdempotencyKey");
            entity.HasIndex(adjustment => new { adjustment.TenantId, adjustment.PartyType, adjustment.PartyId, adjustment.AdjustmentDate });
            entity.Property(adjustment => adjustment.Amount).HasPrecision(18, 2);
            entity.Property(adjustment => adjustment.IdempotencyKey).HasMaxLength(100);
            entity.Property(adjustment => adjustment.ReferenceNumber).HasMaxLength(100);
            entity.Property(adjustment => adjustment.Notes).HasMaxLength(1000);
        });

        modelBuilder.Entity<PurchaseReturnRequestIdempotency>(entity =>
        {
            entity.ToTable("PurchaseReturnRequestIdempotency");
            entity.HasKey(record => record.Id);
            entity.HasIndex(record => new { record.TenantId, record.Key })
                .IsUnique()
                .HasDatabaseName("IX_PurchaseReturnRequestIdempotency_TenantId_Key");
            entity.HasIndex(record => new { record.TenantId, record.ReturnId });
            entity.Property(record => record.Key).HasMaxLength(255);
            entity.Property(record => record.PurchaseAttemptId).HasMaxLength(200);
            entity.Property(record => record.RequestFingerprint).HasMaxLength(64);
            entity.Property(record => record.ResponseJson).HasColumnType("text");
        });

        modelBuilder.Entity<SalesRequestIdempotency>(entity =>
        {
            entity.ToTable("SalesRequestIdempotency");
            entity.HasKey(record => record.Id);
            entity.HasIndex(record => new { record.TenantId, record.Key, record.Operation })
                .IsUnique()
                .HasDatabaseName("IX_SalesRequestIdempotency_TenantId_Key_Operation");
            entity.HasIndex(record => new { record.TenantId, record.Operation, record.RequestHash, record.IsDeleted })
                .HasDatabaseName("IX_SalesRequestIdempotency_TenantId_Operation_RequestHash_IsDeleted");
            entity.Property(record => record.Key).HasMaxLength(200);
            entity.Property(record => record.Operation).HasMaxLength(32);
            entity.Property(record => record.RequestHash).HasMaxLength(64);
            entity.Property(record => record.ResponseJson).HasColumnType("text");
            entity.Property(record => record.ClaimToken).HasMaxLength(32);
        });

        modelBuilder.Entity<OrderRequestIdempotency>(entity =>
        {
            entity.ToTable("OrderRequestIdempotency");
            entity.HasKey(record => record.Id);
            entity.HasIndex(record => new { record.TenantId, record.Key })
                .IsUnique()
                .HasDatabaseName("IX_OrderRequestIdempotency_TenantId_Key");
            entity.HasIndex(record => new { record.TenantId, record.RequestHash, record.IsDeleted })
                .HasDatabaseName("IX_OrderRequestIdempotency_TenantId_RequestHash_IsDeleted");
            entity.HasIndex(record => new { record.TenantId, record.OrderId });
            entity.HasIndex(record => new { record.CompletedAtUtc, record.IsDeleted })
                .HasFilter("\"CompletedAtUtc\" IS NOT NULL")
                .HasDatabaseName("IX_OrderRequestIdempotency_CompletedAtUtc_IsDeleted");
            entity.Property(record => record.Key).HasMaxLength(200);
            entity.Property(record => record.RequestHash).HasMaxLength(64);
            entity.Property(record => record.SnapshotHash).HasMaxLength(64);
            entity.Property(record => record.ResponsePayload).HasColumnType("text");
            entity.Property(record => record.ClaimToken).HasMaxLength(32);
        });

        modelBuilder.Entity<StockTransferRequestIdempotency>(entity =>
        {
            entity.ToTable("StockTransferRequestIdempotency");
            entity.HasKey(record => record.Id);
            entity.HasIndex(record => new { record.TenantId, record.Key, record.Operation })
                .IsUnique()
                .HasDatabaseName("IX_StockTransferRequestIdempotency_TenantId_Key_Operation");
            entity.HasIndex(record => new { record.TenantId, record.StockTransferId });
            entity.Property(record => record.Key).HasMaxLength(255);
            entity.Property(record => record.Operation).HasMaxLength(64);
            entity.Property(record => record.RequestFingerprint).HasMaxLength(64);
            entity.Property(record => record.ResponseJson).HasColumnType("text");
            entity.HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<ProductReview>(entity =>
        {
            entity.ToTable("ProductReviews");
            entity.HasKey(review => review.Id);

            entity.HasIndex(review => new { review.ProductId, review.UserId })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");
            entity.HasIndex(review => new { review.ProductId, review.Status, review.CreatedAt });
            entity.HasIndex(review => new { review.Status, review.Rating, review.ProductId });
            entity.HasIndex(review => new { review.UserId, review.CreatedAt });

            entity.Property(review => review.Rating).IsRequired();
            entity.Property(review => review.Title).HasMaxLength(200);
            entity.Property(review => review.Comment).HasMaxLength(4000).IsRequired();
            entity.Property(review => review.Status)
                .HasConversion<int>()
                .HasDefaultValue(ProductReviewStatus.Pending);
            entity.Property(review => review.CreatedAt)
                .HasColumnName("CreatedAtUtc");
            entity.Property(review => review.UpdatedAt)
                .HasColumnName("UpdatedAtUtc");

            entity.ToTable(table =>
                table.HasCheckConstraint(
                    "CK_ProductReviews_Rating_Range",
                    "\"Rating\" >= 1 AND \"Rating\" <= 5"));

            entity.HasOne(review => review.Product)
                .WithMany()
                .HasForeignKey(review => review.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(review => review.User)
                .WithMany()
                .HasForeignKey(review => review.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CommissionRule>(entity =>
        {
            entity.HasIndex(rule => new { rule.ScopeType, rule.ScopeId, rule.Priority });
            entity.HasIndex(rule => new { rule.ScopeType, rule.IsActive, rule.Priority });
            entity.Property(rule => rule.Rate).HasPrecision(8, 6);
            entity.Property(rule => rule.IsActive).HasDefaultValue(true);
            entity.Property(rule => rule.Priority).HasDefaultValue(0);
        });

        modelBuilder.Entity<CommerceHubSubOrder>(entity =>
        {
            entity.HasIndex(order => order.OrderNumber).IsUnique();
            entity.HasIndex(order => new { order.MarketplaceOrderId, order.StoreId })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false")
                .HasDatabaseName("UX_CommerceHubSubOrders_MarketplaceOrderId_StoreId_Live");
            entity.HasIndex(order => order.ShippingMethodId);
            entity.Property(order => order.StoreName).HasMaxLength(200);
            entity.Property(order => order.Currency).HasMaxLength(10);
            entity.Property(order => order.Subtotal).HasPrecision(18, 2);
            entity.Property(order => order.Shipping).HasPrecision(18, 2);
            entity.Property(order => order.ShippingMethodName).HasMaxLength(200);
            entity.Property(order => order.ShippingSnapshotJson).HasColumnType("text");
            entity.Property(order => order.PaymentMethodCode).HasMaxLength(50);
            entity.Property(order => order.PaymentMethodName).HasMaxLength(100);
            entity.Property(order => order.PaymentSnapshotJson).HasColumnType("text");
            entity.Property(order => order.CommissionSnapshotJson).HasColumnType("text");
            entity.Property(order => order.PlatformCommissionAmount).HasPrecision(18, 2);
            entity.Property(order => order.VendorEarningAmount).HasPrecision(18, 2);
            entity.Property(order => order.PlatformCommissionRate).HasPrecision(8, 6);
            entity.Property(order => order.Total).HasPrecision(18, 2);
            entity.Property(order => order.PaymentReferenceId).HasMaxLength(100);

            entity.HasOne(order => order.MarketplaceOrder)
                .WithMany(order => order.SubOrders)
                .HasForeignKey(order => order.MarketplaceOrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CommerceHubSubOrderItem>(entity =>
        {
            entity.HasIndex(item => item.SubOrderId);
            entity.HasIndex(item => item.ListingId);
            entity.HasIndex(item => item.VariantId);
            entity.HasIndex(item => new { item.SubOrderId, item.VariantId })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");
            entity.Property(item => item.ProductName).HasMaxLength(250);
            entity.Property(item => item.VariantName).HasMaxLength(250);
            entity.Property(item => item.ImageUrl).HasMaxLength(1000);
            entity.Property(item => item.UnitPrice).HasPrecision(18, 2);
            // T-08: immutable merchandise-cost authority. Unit precision matches the
            // POS CostPriceSnapshot convention (18,6); amount matches CostAmountSnapshot (18,2).
            entity.Property(item => item.UnitCostSnapshot).HasPrecision(18, 6);
            entity.Property(item => item.CostAmountSnapshot).HasPrecision(18, 2);
            entity.Property(item => item.PlatformCommissionRate).HasPrecision(8, 6);
            entity.Property(item => item.PlatformCommissionAmount).HasPrecision(18, 2);
            entity.Property(item => item.Total).HasPrecision(18, 2);
            entity.Property(item => item.Status)
                .HasDefaultValue(PointOfSale.Core.Enums.CommerceHubOrderItemStatus.Pending);

            entity.HasOne(item => item.SubOrder)
                .WithMany(order => order.Items)
                .HasForeignKey(item => item.SubOrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CommerceHubSubOrderStatusHistory>(entity =>
        {
            entity.HasIndex(history => history.SubOrderId);
            entity.HasIndex(history => new { history.SubOrderId, history.ChangedAtUtc });
            entity.Property(history => history.Reason).HasMaxLength(1000);
            entity.Property(history => history.CorrelationId).HasMaxLength(200);

            entity.HasOne(history => history.SubOrder)
                .WithMany()
                .HasForeignKey(history => history.SubOrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CommerceHubPayment>(entity =>
        {
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_CommerceHubPayments_Amount_Positive",
                    "\"Amount\" > 0");
                table.HasCheckConstraint(
                    "CK_CommerceHubPayments_RefundedAmount_Range",
                    "\"RefundedAmount\" >= 0 AND \"RefundedAmount\" <= \"Amount\"");
                table.HasCheckConstraint(
                    "CK_CommerceHubPayments_ProvisionalRefundedAmount_Range",
                    "\"ProvisionalRefundedAmount\" >= 0 AND ROUND(\"RefundedAmount\" + \"ProvisionalRefundedAmount\", 2) <= ROUND(\"Amount\", 2)");
                table.HasCheckConstraint(
                    "CK_CommerceHubPayments_Paid_State_Complete",
                    "\"Status\" <> 1 OR (\"IsProcessed\" = true AND \"PaidAtUtc\" IS NOT NULL AND \"ProviderTransactionId\" IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_CommerceHubPayments_Currency_EGP",
                    "\"Currency\" = 'EGP'");
            });

            entity.HasIndex(payment => payment.OrderId);
            entity.HasIndex(payment => payment.OrderId)
                .HasDatabaseName("IX_CommerceHubPayments_OrderId_SinglePaid")
                .IsUnique()
                .HasFilter("\"Status\" = 1 AND \"IsDeleted\" = false");
            entity.HasIndex(payment => new { payment.OrderId, payment.Status });
            entity.HasIndex(payment => new { payment.Provider, payment.ProviderIntentId })
                .IsUnique()
                .HasDatabaseName("UX_CommerceHubPayments_Provider_ProviderIntentId")
                .HasFilter("\"ProviderIntentId\" IS NOT NULL");
            entity.HasIndex(payment => payment.ProviderReference).IsUnique();
            entity.HasIndex(payment => payment.ProviderTransactionId)
                .IsUnique()
                .HasFilter("\"ProviderTransactionId\" IS NOT NULL");
            entity.Property(payment => payment.Amount).HasPrecision(18, 2);
            entity.Property(payment => payment.Currency).HasMaxLength(10);
            entity.Property(payment => payment.ProviderReference).HasMaxLength(100);
            entity.Property(payment => payment.ProviderIntentId).HasMaxLength(200);
            entity.Property(payment => payment.ProviderTransactionId).HasMaxLength(200);
            entity.Property(payment => payment.CheckoutUrl).HasMaxLength(2000);
            entity.Property(payment => payment.PaymentToken).HasColumnType("text");
            entity.Property(payment => payment.RefundedAmount).HasPrecision(18, 2);
            entity.Property(payment => payment.ProvisionalRefundedAmount).HasPrecision(18, 2);
            entity.Property(payment => payment.FailureReason).HasMaxLength(1000);
            entity.Property(payment => payment.NeedsManualCurrencyReview).HasDefaultValue(false);

            entity.HasOne(payment => payment.Order)
                .WithMany(order => order.Payments)
                .HasForeignKey(payment => payment.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CommerceHubPaymentAttempt>(entity =>
        {
            entity.HasIndex(attempt => attempt.PaymentId);
            entity.HasIndex(attempt => new { attempt.PaymentId, attempt.AttemptNumber }).IsUnique();
            entity.HasIndex(attempt => new { attempt.Provider, attempt.ProviderIntentId })
                .HasFilter("\"ProviderIntentId\" IS NOT NULL");
            entity.HasIndex(attempt => attempt.ProviderTransactionId)
                .HasFilter("\"ProviderTransactionId\" IS NOT NULL");
            entity.Property(attempt => attempt.ProviderReference).HasMaxLength(100);
            entity.Property(attempt => attempt.ProviderIntentId).HasMaxLength(200);
            entity.Property(attempt => attempt.ProviderTransactionId).HasMaxLength(200);
            entity.Property(attempt => attempt.FailureReason).HasMaxLength(1000);

            entity.HasOne(attempt => attempt.Payment)
                .WithMany(payment => payment.Attempts)
                .HasForeignKey(attempt => attempt.PaymentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CommerceHubPaymentItem>(entity =>
        {
            entity.HasIndex(item => item.PaymentId);
            entity.HasIndex(item => item.SubOrderId);
            entity.HasIndex(item => new { item.PaymentId, item.SubOrderId }).IsUnique();
            entity.Property(item => item.Amount).HasPrecision(18, 2);

            entity.HasOne(item => item.Payment)
                .WithMany(payment => payment.Items)
                .HasForeignKey(item => item.PaymentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(item => item.SubOrder)
                .WithMany()
                .HasForeignKey(item => item.SubOrderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CommerceHubPaymentLog>(entity =>
        {
            entity.HasIndex(log => log.PaymentId);
            entity.HasIndex(log => new { log.PaymentId, log.ProviderEventId });
            entity.Property(log => log.EventType).HasMaxLength(100);
            entity.Property(log => log.ProviderEventId).HasMaxLength(100);
            entity.Property(log => log.RawResponse).HasColumnType("text");

            entity.HasOne(log => log.Payment)
                .WithMany(payment => payment.Logs)
                .HasForeignKey(log => log.PaymentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CommerceHubUnresolvedPaymentWebhook>(entity =>
        {
            entity.HasQueryFilter(item => !item.IsDeleted);

            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_CommerceHubUnresolvedPaymentWebhooks_Amount_NonNegative",
                    "\"Amount\" >= 0");
                table.HasCheckConstraint(
                    "CK_CommerceHubUnresolvedPaymentWebhooks_Currency_EGP",
                    "\"Currency\" = 'EGP'");
            });

            entity.HasIndex(item => new { item.Provider, item.ProviderEventId })
                .IsUnique()
                .HasDatabaseName("UX_CommerceHubUnresolvedPaymentWebhooks_Provider_Event");
            entity.HasIndex(item => new { item.TenantId, item.Status, item.CreatedAt })
                .HasDatabaseName("IX_CommerceHubUnresolvedPaymentWebhooks_Tenant_Status");
            entity.HasIndex(item => item.OrderId)
                .HasFilter("\"OrderId\" IS NOT NULL");
            entity.HasIndex(item => item.LocalPaymentId)
                .HasFilter("\"LocalPaymentId\" IS NOT NULL");
            entity.Property(item => item.ProviderEventId).HasMaxLength(200);
            entity.Property(item => item.ProviderReference).HasMaxLength(200);
            entity.Property(item => item.ProviderIntentId).HasMaxLength(200);
            entity.Property(item => item.ProviderTransactionId).HasMaxLength(200);
            entity.Property(item => item.AffectedStoreIdsJson).HasColumnType("text").IsRequired().HasDefaultValue("[]");
            entity.Property(item => item.SuspenseRoutedStoreIdsJson).HasColumnType("text").IsRequired().HasDefaultValue("[]");
            entity.Property(item => item.Amount).HasPrecision(18, 2);
            entity.Property(item => item.Currency).HasMaxLength(10);
            entity.Property(item => item.EventType).HasMaxLength(100);
            entity.Property(item => item.PayloadHash).HasMaxLength(64);
            entity.Property(item => item.RawPayload).HasColumnType("text");
            entity.Property(item => item.Status).HasMaxLength(50);
            entity.Property(item => item.CorrelationId).HasMaxLength(100);
            entity.Property(item => item.LastError).HasMaxLength(2000);
        });

        modelBuilder.Entity<CommerceHubVendorEarning>(entity =>
        {
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_CommerceHubVendorEarnings_PayoutAmounts_NonNegative",
                    "\"PaidOutAmount\" >= 0 AND \"PayoutReservedAmount\" >= 0");
                table.HasCheckConstraint(
                    "CK_CommerceHubVendorEarnings_PayoutAmounts_Within_Positive_Earning",
                    "\"Amount\" < 0 OR ROUND(\"PaidOutAmount\" + \"PayoutReservedAmount\", 2) <= ROUND(\"Amount\", 2)");
                table.HasCheckConstraint(
                    "CK_CommerceHubVendorEarnings_InPayout_HasPayout",
                    "\"Status\" <> 3 OR \"PayoutId\" IS NOT NULL");
                table.HasCheckConstraint(
                    "CK_CommerceHubVendorEarnings_PaidOut_HasPaidAmount",
                    "\"Status\" <> 2 OR \"PaidOutAmount\" > 0 OR \"Amount\" < 0");
            });

            entity.HasIndex(earning => earning.SubOrderId)
                .IsUnique()
                .HasFilter("\"OriginalEarningId\" IS NULL AND \"IsDeleted\" = false");
            entity.HasIndex(earning => earning.PaymentId);
            entity.HasIndex(earning => earning.PayoutId);
            entity.HasIndex(earning => earning.RefundId);
            entity.HasIndex(earning => earning.OriginalEarningId);
            entity.HasIndex(earning => new { earning.TenantId, earning.Status, earning.CreatedAt });
            entity.Property(earning => earning.Amount).HasPrecision(18, 2);
            entity.Property(earning => earning.PaidOutAmount).HasPrecision(18, 2);
            entity.Property(earning => earning.PayoutReservedAmount).HasPrecision(18, 2);

            entity.HasOne(earning => earning.SubOrder)
                .WithMany()
                .HasForeignKey(earning => earning.SubOrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(earning => earning.Payment)
                .WithMany()
                .HasForeignKey(earning => earning.PaymentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(earning => earning.Payout)
                .WithMany()
                .HasForeignKey(earning => earning.PayoutId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(earning => earning.Refund)
                .WithMany()
                .HasForeignKey(earning => earning.RefundId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(earning => earning.OriginalEarning)
                .WithMany()
                .HasForeignKey(earning => earning.OriginalEarningId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CommerceHubPayout>(entity =>
        {
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_CommerceHubPayouts_Amount_Positive",
                    "\"Amount\" > 0");
                table.HasCheckConstraint(
                    "CK_CommerceHubPayouts_Currency_EGP",
                    "\"Currency\" = 'EGP'");
                table.HasCheckConstraint(
                    "CK_CommerceHubPayouts_Completed_State_Complete",
                    "\"Status\" <> 2 OR (\"ProcessedAtUtc\" IS NOT NULL AND \"CompletedAtUtc\" IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_CommerceHubPayouts_Completed_Evidence",
                    "\"Status\" <> 2 OR NULLIF(btrim(\"ExternalReferenceId\"), '') IS NOT NULL OR NULLIF(btrim(\"BankTransactionId\"), '') IS NOT NULL");
                table.HasCheckConstraint(
                    "CK_CommerceHubPayouts_Processing_ExternalIntent",
                    "(\"Status\" <> 1 AND \"Status\" <> 4) OR NULLIF(btrim(\"ExternalTransferIdempotencyKey\"), '') IS NOT NULL");
                table.HasCheckConstraint(
                    "CK_CommerceHubPayouts_Processing_HandoffCommitted",
                    "(\"Status\" <> 1 AND \"Status\" <> 4) OR (NULLIF(btrim(\"ExternalTransferHandoffToken\"), '') IS NOT NULL AND \"ExternalTransferHandoffCommittedAtUtc\" IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_CommerceHubPayouts_Terminal_HandoffCommitted",
                    "(\"Status\" <> 2 AND \"Status\" <> 4) OR (NULLIF(btrim(\"ExternalTransferHandoffToken\"), '') IS NOT NULL AND \"ExternalTransferHandoffCommittedAtUtc\" IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_CommerceHubPayouts_NoRefundFailureAfterHandoff",
                    "NOT (\"Status\" = 3 AND \"ExternalTransferHandoffCommittedAtUtc\" IS NOT NULL AND \"ProcessedBy\" IN ('system:refund', 'system:refund-control'))");
            });

            entity.HasIndex(payout => new { payout.TenantId, payout.Status, payout.CreatedAt });
            entity.HasIndex(payout => payout.TenantId)
                .IsUnique()
                .HasDatabaseName("UX_CommerceHubPayouts_OneProcessingPerTenant")
                .HasFilter("(\"Status\" = 1 OR \"Status\" = 4) AND \"IsDeleted\" = false");
            entity.HasIndex(payout => new { payout.TenantId, payout.IdempotencyKey }).IsUnique();
            entity.HasIndex(payout => payout.PayoutMethodId);
            entity.HasIndex(payout => payout.ExternalReferenceId)
                .IsUnique()
                .HasDatabaseName("UX_CommerceHubPayouts_Live_ExternalReferenceId")
                .HasFilter("\"ExternalReferenceId\" IS NOT NULL AND \"ExternalReferenceId\" <> '' AND \"Status\" <> 3 AND \"IsDeleted\" = false");
            entity.HasIndex(payout => payout.BankTransactionId)
                .IsUnique()
                .HasDatabaseName("UX_CommerceHubPayouts_Live_BankTransactionId")
                .HasFilter("\"BankTransactionId\" IS NOT NULL AND \"BankTransactionId\" <> '' AND \"Status\" <> 3 AND \"IsDeleted\" = false");
            entity.HasIndex(payout => new { payout.TenantId, payout.ExternalTransferIdempotencyKey })
                .IsUnique()
                .HasDatabaseName("UX_CommerceHubPayouts_ExternalTransferIdempotencyKey")
                .HasFilter("\"ExternalTransferIdempotencyKey\" IS NOT NULL AND \"ExternalTransferIdempotencyKey\" <> '' AND \"IsDeleted\" = false");
            entity.HasIndex(payout => new { payout.TenantId, payout.ExternalTransferHandoffToken })
                .IsUnique()
                .HasDatabaseName("UX_CommerceHubPayouts_ExternalTransferHandoffToken")
                .HasFilter("\"ExternalTransferHandoffToken\" IS NOT NULL AND \"ExternalTransferHandoffToken\" <> '' AND \"IsDeleted\" = false");
            entity.Property(payout => payout.Amount).HasPrecision(18, 2);
            entity.Property(payout => payout.Currency).HasMaxLength(10);
            entity.Property(payout => payout.DestinationValueSnapshot).HasMaxLength(200);
            entity.Property(payout => payout.Reference).HasMaxLength(200);
            entity.Property(payout => payout.ExternalReferenceId).HasMaxLength(200);
            entity.Property(payout => payout.BankTransactionId).HasMaxLength(200);
            entity.Property(payout => payout.ExternalTransferIdempotencyKey).HasMaxLength(240);
            entity.Property(payout => payout.ExternalTransferHandoffToken).HasMaxLength(128);
            entity.Property(payout => payout.ProcessedBy).HasMaxLength(200);
            entity.Property(payout => payout.FailureReason).HasMaxLength(1000);
            entity.Property(payout => payout.IdempotencyKey).HasMaxLength(200);
            entity.Property(payout => payout.CompletionEvidenceFingerprint).HasMaxLength(128);

            entity.HasOne<CommerceHubVendorPayoutMethod>()
                .WithMany()
                .HasForeignKey(payout => payout.PayoutMethodId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<CommerceHubPayoutItem>(entity =>
        {
            entity.HasIndex(item => item.PayoutId);
            entity.HasIndex(item => item.EarningId);
            entity.HasIndex(item => new { item.PayoutId, item.EarningId }).IsUnique();
            entity.Property(item => item.Amount).HasPrecision(18, 2);

            entity.HasOne(item => item.Payout)
                .WithMany(payout => payout.Items)
                .HasForeignKey(item => item.PayoutId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(item => item.Earning)
                .WithMany()
                .HasForeignKey(item => item.EarningId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CommerceHubRefund>(entity =>
        {
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_CommerceHubRefunds_Amount_Positive",
                    "\"Amount\" > 0");
                table.HasCheckConstraint(
                    "CK_CommerceHubRefunds_Reversals_NonNegative",
                    "\"CommissionReversalAmount\" >= 0 AND \"PromoDiscountReversalAmount\" >= 0 AND \"VendorEarningReversalAmount\" >= 0");
                table.HasCheckConstraint(
                    "CK_CommerceHubRefunds_Reversals_Total_Amount",
                    "\"CommissionReversalAmount\" + \"PromoDiscountReversalAmount\" + \"VendorEarningReversalAmount\" = \"Amount\"");
                table.HasCheckConstraint(
                    "CK_CommerceHubRefunds_Completed_State_Complete",
                    "\"Status\" <> 2 OR \"ProcessedAtUtc\" IS NOT NULL");
                table.HasCheckConstraint(
                    "CK_CommerceHubRefunds_Currency_EGP",
                    "\"Currency\" = 'EGP'");
            });

            entity.HasIndex(refund => new { refund.TenantId, refund.CreatedAt });
            entity.HasIndex(refund => new { refund.TenantId, refund.Status, refund.CreatedAt });
            entity.HasIndex(refund => new { refund.TenantId, refund.Status, refund.ProcessingStartedAtUtc, refund.CreatedAt })
                .HasDatabaseName("IX_CommerceHubRefunds_ExternalReconciliation")
                .HasFilter("(\"Status\" = 1 OR \"Status\" = 4) AND \"IsDeleted\" = false");
            entity.HasIndex(refund => new { refund.SubOrderId, refund.Status, refund.CreatedAt });
            entity.HasIndex(refund => refund.PaymentId);
            entity.HasIndex(refund => new { refund.TenantId, refund.IdempotencyKey }).IsUnique();
            entity.Property(refund => refund.Amount).HasPrecision(18, 2);
            entity.Property(refund => refund.CommissionReversalAmount).HasPrecision(18, 2);
            entity.Property(refund => refund.VendorEarningReversalAmount).HasPrecision(18, 2);
            entity.Property(refund => refund.Currency).HasMaxLength(10);
            entity.Property(refund => refund.Reason).HasMaxLength(1000);
            entity.Property(refund => refund.Reference).HasMaxLength(200);
            entity.Property(refund => refund.GatewayStatus).HasMaxLength(100);
            entity.Property(refund => refund.FailureReason).HasMaxLength(1000);
            entity.Property(refund => refund.IdempotencyKey).HasMaxLength(200);

            entity.HasOne(refund => refund.SubOrder)
                .WithMany()
                .HasForeignKey(refund => refund.SubOrderId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(refund => refund.Payment)
                .WithMany(payment => payment.Refunds)
                .HasForeignKey(refund => refund.PaymentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<CommerceHubFinancialAuditLog>(entity =>
        {
            entity.HasKey(entry => entry.Id);
            entity.HasIndex(entry => entry.CreatedAtUtc);
            entity.HasIndex(entry => entry.TenantId);
            entity.HasIndex(entry => entry.MarketplaceOrderId);
            entity.HasIndex(entry => entry.SubOrderId);
            entity.HasIndex(entry => entry.PaymentId);
            entity.HasIndex(entry => entry.EarningId);
            entity.HasIndex(entry => entry.PayoutId);
            entity.HasIndex(entry => entry.RefundId);
            entity.HasIndex(entry => entry.Type);
            entity.Property(entry => entry.Type).HasMaxLength(100);
            entity.Property(entry => entry.Debit).HasPrecision(18, 4);
            entity.Property(entry => entry.Credit).HasPrecision(18, 4);
            entity.Property(entry => entry.Currency).HasMaxLength(10);
            entity.Property(entry => entry.Description).HasColumnType("text");
            entity.Property(entry => entry.CorrelationId).HasMaxLength(200);
        });

        modelBuilder.Entity<CommerceHubFinancialGuardLock>(entity =>
        {
            entity.HasKey(entry => entry.Id);
            entity.HasIndex(entry => entry.Scope).IsUnique();
            entity.Property(entry => entry.Scope).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<CommerceHubFinancialOperationAudit>(entity =>
        {
            entity.HasKey(entry => entry.Id);
            entity.HasIndex(entry => entry.VendorId);
            entity.HasIndex(entry => entry.CorrelationId);
            entity.HasIndex(entry => entry.OperationType);
            entity.HasIndex(entry => new { entry.ReferenceType, entry.ReferenceId });
            entity.Property(entry => entry.ActorType).HasMaxLength(50);
            entity.Property(entry => entry.OperationType).HasMaxLength(50);
            entity.Property(entry => entry.Amount).HasPrecision(18, 2);
            entity.Property(entry => entry.Currency).HasMaxLength(10);
            entity.Property(entry => entry.BeforePlatformCash).HasPrecision(18, 2);
            entity.Property(entry => entry.BeforeVendorPayable).HasPrecision(18, 2);
            entity.Property(entry => entry.BeforeVendorDebt).HasPrecision(18, 2);
            entity.Property(entry => entry.AfterPlatformCash).HasPrecision(18, 2);
            entity.Property(entry => entry.AfterVendorPayable).HasPrecision(18, 2);
            entity.Property(entry => entry.AfterVendorDebt).HasPrecision(18, 2);
            entity.Property(entry => entry.CorrelationId).HasMaxLength(200).IsRequired();
            entity.Property(entry => entry.IdempotencyKey).HasMaxLength(200);
            entity.Property(entry => entry.Outcome).HasMaxLength(50).IsRequired();
            entity.Property(entry => entry.Notes).HasColumnType("text");
        });

        modelBuilder.Entity<UserGovernanceProfile>(entity =>
        {
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_UserGovernanceProfiles_MarketplaceApprovalRequiresKyc",
                "NOT \"IsMarketplaceApproved\" OR \"IsKycVerified\""));
            entity.HasKey(profile => profile.Id);
            entity.HasIndex(profile => new { profile.Role, profile.SubjectId })
                .IsUnique();
            entity.HasIndex(profile => profile.Status);
            entity.HasIndex(profile => profile.RiskLevel);
            entity.Property(profile => profile.StatusReason).HasMaxLength(1000);
            entity.Property(profile => profile.KycVerificationReason).HasMaxLength(1000);
            entity.Property(profile => profile.MarketplaceApprovalReason).HasMaxLength(1000);
            entity.Property(profile => profile.LastRiskReason).HasMaxLength(1000);
            entity.Property(profile => profile.LastIpAddress).HasMaxLength(128);
            entity.Property(profile => profile.DeviceFingerprint).HasMaxLength(200);
        });

        modelBuilder.Entity<UserGovernanceAuditLog>(entity =>
        {
            entity.HasKey(entry => entry.Id);
            entity.HasIndex(entry => new { entry.TargetRole, entry.TargetUserId, entry.TimestampUtc });
            entity.HasIndex(entry => entry.AdminId);
            entity.Property(entry => entry.ActorType).HasMaxLength(50).IsRequired();
            entity.Property(entry => entry.ActionType).HasMaxLength(100).IsRequired();
            entity.Property(entry => entry.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(entry => entry.BeforeState).HasColumnType("jsonb").IsRequired();
            entity.Property(entry => entry.AfterState).HasColumnType("jsonb").IsRequired();
            entity.Property(entry => entry.Metadata).HasColumnType("jsonb");
        });

        modelBuilder.Entity<UserGovernanceBlacklistEntry>(entity =>
        {
            entity.HasKey(entry => entry.Id);
            entity.HasIndex(entry => new { entry.Type, entry.ValueNormalized })
                .IsUnique();
            entity.HasIndex(entry => entry.IsActive);
            entity.HasIndex(entry => entry.CreatedAtUtc);
            entity.Property(entry => entry.ValueNormalized).HasMaxLength(320).IsRequired();
            entity.Property(entry => entry.ValueDisplay).HasMaxLength(320).IsRequired();
            entity.Property(entry => entry.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(entry => entry.RevokedReason).HasMaxLength(1000);
        });

        // Coupon configuration
        // MULTI-TENANT: Unique index on (TenantId, NormalizedCode) - enforces case-insensitive uniqueness
        // Same code can exist for different tenants, but not with different casing within a tenant
        modelBuilder.Entity<Coupon>(entity =>
        {
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_Coupons_DiscountType",
                    "\"DiscountType\" IN (0, 1)");
                table.HasCheckConstraint(
                    "CK_Coupons_DiscountValue_Positive",
                    "\"DiscountValue\" > 0");
                table.HasCheckConstraint(
                    "CK_Coupons_DiscountValue_Percentage",
                    "\"DiscountType\" <> 0 OR \"DiscountValue\" <= 100");
                table.HasCheckConstraint(
                    "CK_Coupons_DiscountValue_FixedAmount",
                    $"\"DiscountType\" <> 1 OR \"DiscountValue\" <= {maxFixedDiscountValueSql}");
            });

            // Case-insensitive unique index using NormalizedCode
            entity.HasIndex(c => new { c.TenantId, c.NormalizedCode })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");
            entity.HasIndex(c => c.IsActive);
            var couponRowVersion = entity.Property(c => c.RowVersion);
            if (Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
            {
                couponRowVersion
                    .HasColumnName("xmin")
                    .HasColumnType("xid")
                    .IsRowVersion();
            }
            else
            {
                couponRowVersion
                    .HasColumnName(nameof(Coupon.RowVersion))
                    .IsConcurrencyToken()
                    .ValueGeneratedOnAddOrUpdate()
                    .HasDefaultValue(0u);
            }
        });

        // CouponUsage Configuration (PHASE: Shopify-grade Coupon Engine)
        // Source of truth for coupon consumption, MaxUsage, and MaxPerCustomer enforcement
        modelBuilder.Entity<CouponUsage>(entity =>
        {
            // Unique constraint: one usage per order
            entity.HasIndex(cu => new { cu.TenantId, cu.OrderId })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");
            
            // Index for MaxUsage queries (count by coupon)
            entity.HasIndex(cu => new { cu.TenantId, cu.CouponId });
            
            // Index for MaxPerCustomer queries (count by coupon + customer)
            // Non-unique to support MaxUsagePerCustomer > 1
            // Usage counts are confirmed-only; active checkout capacity is guarded by CouponReservation.
            entity.HasIndex(cu => new { cu.TenantId, cu.CouponId, cu.CustomerId })
                .HasFilter("\"CustomerId\" IS NOT NULL AND \"IsDeleted\" = false");
            
            // Relationships
            entity.HasOne(cu => cu.Coupon)
                .WithMany()
                .HasForeignKey(cu => cu.CouponId)
                .OnDelete(DeleteBehavior.Restrict);
            
            entity.HasOne(cu => cu.Order)
                .WithMany()
                .HasForeignKey(cu => cu.OrderId)
                .OnDelete(DeleteBehavior.Restrict);
            
            // Precision for monetary fields
            entity.Property(cu => cu.DiscountAmount).HasPrecision(18, 2);
            entity.Property(cu => cu.OrderSubtotal).HasPrecision(18, 2);
        });

        modelBuilder.Entity<CouponReservation>(entity =>
        {
            entity.HasIndex(cr => new { cr.TenantId, cr.OrderId })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false")
                .HasDatabaseName("IX_CouponReservations_TenantId_OrderId");

            entity.HasIndex(cr => new { cr.TenantId, cr.CouponId, cr.CustomerId })
                .IsUnique()
                .HasFilter($"\"IsDeleted\" = false AND \"Status\" = {(int)CouponReservationStatus.Reserved}")
                .HasDatabaseName("IX_CouponReservations_TenantId_CouponId_CustomerId");

            entity.Property(cr => cr.Status).HasConversion<int>();
            entity.Property(cr => cr.CorrelationId).HasMaxLength(100);
            
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_CouponReservation_Status",
                    "\"Status\" IN (0, 1, 2)");
            });

            entity.HasOne(cr => cr.Coupon)
                .WithMany()
                .HasForeignKey(cr => cr.CouponId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(cr => cr.Customer)
                .WithMany()
                .HasForeignKey(cr => cr.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Changed from WithOne to WithMany to allow multiple reservations per order
            // (only one active at a time, enforced by filtered unique index)
            entity.HasOne(cr => cr.Order)
                .WithMany()
                .HasForeignKey(cr => cr.OrderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Promotion Configuration (PHASE: Promotions Engine - Offers)
        // Auto-applied discounts for products/categories without codes
        modelBuilder.Entity<Promotion>(entity =>
        {
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_Promotions_DiscountType",
                    "\"DiscountType\" IN (0, 1)");
                table.HasCheckConstraint(
                    "CK_Promotions_DiscountValue_Positive",
                    "\"DiscountValue\" > 0");
                table.HasCheckConstraint(
                    "CK_Promotions_DiscountValue_Percentage",
                    "\"DiscountType\" <> 0 OR \"DiscountValue\" <= 100");
                table.HasCheckConstraint(
                    "CK_Promotions_DiscountValue_FixedAmount",
                    $"\"DiscountType\" <> 1 OR \"DiscountValue\" <= {maxFixedDiscountValueSql}");
            });

            // Multi-tenant unique name constraint
            entity.HasIndex(p => new { p.TenantId, p.Name })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");

            entity.Property(p => p.RowVersion)
                .HasColumnName("xmin")
                .IsRowVersion();
            
            // Index for active promotions query (most common storefront query)
            entity.HasIndex(p => new { p.TenantId, p.IsActive, p.StartDate, p.EndDate });
            
            // Index for product/category scope lookups
            entity.HasIndex(p => new { p.TenantId, p.Scope, p.AppliesToId, p.IsActive });
            
            // Priority index for conflict resolution
            entity.HasIndex(p => p.Priority);
            
            // Field constraints
            entity.Property(p => p.Name).HasMaxLength(200).IsRequired();
            entity.Property(p => p.Description).HasMaxLength(500);
            entity.Property(p => p.DiscountValue).HasPrecision(18, 2);
            entity.Property(p => p.MaxDiscountAmount).HasPrecision(18, 2);
            entity.Property(p => p.MinOrderAmount).HasPrecision(18, 2);
            entity.Property(p => p.ProjectionMutationVersion).HasDefaultValue(0L);
        });

        // PromotionUsage Configuration (PHASE: Promotions Engine)
        // Tracks offer applications per order for MaxUsage enforcement
        modelBuilder.Entity<PromotionUsage>(entity =>
        {
            // Unique constraint: one usage per promotion per order
            entity.HasIndex(pu => new { pu.TenantId, pu.PromotionId, pu.OrderId })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");
            
            // Index for MaxUsage queries (count by promotion)
            entity.HasIndex(pu => new { pu.TenantId, pu.PromotionId });
            
            // Relationships
            entity.HasOne(pu => pu.Promotion)
                .WithMany()
                .HasForeignKey(pu => pu.PromotionId)
                .OnDelete(DeleteBehavior.Restrict);
            
            entity.HasOne(pu => pu.Order)
                .WithMany()
                .HasForeignKey(pu => pu.OrderId)
                .OnDelete(DeleteBehavior.Restrict);
            
            entity.HasOne(pu => pu.Customer)
                .WithMany()
                .HasForeignKey(pu => pu.CustomerId)
                .OnDelete(DeleteBehavior.SetNull);
            
            // Field constraints
            entity.Property(pu => pu.PromotionName).HasMaxLength(200);
            entity.Property(pu => pu.DiscountAmount).HasPrecision(18, 2);
            entity.Property(pu => pu.OrderSubtotal).HasPrecision(18, 2);
        });

        // -------------------------------------------------------------------------
        // REMOVED: Global seed data for ShippingMethod
        // 
        // Phase: Tenant Isolation Hardening
        // 
        // Global seed data violates tenant isolation! Shipping methods MUST be
        // created per-tenant during tenant bootstrap.
        // See TenantRegistrationService.cs for the bootstrap logic.
        // -------------------------------------------------------------------------
        
        // -------------------------------------------------------------------------
        // CARRIER CONFIGURATION (Carrier-Agnostic Shipping Module)
        // Per-tenant configuration for shipping carriers (Bosta, Aramex, DHL)
        // -------------------------------------------------------------------------
        modelBuilder.Entity<CarrierConfiguration>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            // One configuration per carrier per tenant
            entity.HasIndex(e => new { e.TenantId, e.CarrierCode })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");
            
            // Index for enabled carriers query
            entity.HasIndex(e => new { e.TenantId, e.IsEnabled, e.SortOrder });
            
            // Index for default carrier lookup
            entity.HasIndex(e => new { e.TenantId, e.IsDefault })
                .HasFilter("\"IsDefault\" = true");
            
            // Field constraints
            entity.Property(e => e.CarrierCode).HasMaxLength(50).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ApiKey).HasMaxLength(500);
            entity.Property(e => e.ApiSecret).HasMaxLength(500);
            entity.Property(e => e.AccountId).HasMaxLength(100);
            entity.Property(e => e.ApiUrl).HasMaxLength(500);
            entity.Property(e => e.WebhookUrl).HasMaxLength(500);
            entity.Property(e => e.WebhookSecret).HasMaxLength(500);
            entity.Property(e => e.CredentialConfigurationFingerprint).HasMaxLength(64);
            entity.Property(e => e.LastValidatedConfigurationFingerprint).HasMaxLength(64);
            entity.Property(e => e.PickupAddress).HasMaxLength(500);
            entity.Property(e => e.PickupCity).HasMaxLength(100);
            entity.Property(e => e.PickupCountry).HasMaxLength(10);
            entity.Property(e => e.PickupPhone).HasMaxLength(50);
            entity.Property(e => e.PickupContactName).HasMaxLength(100);
            entity.Property(e => e.DefaultServiceLevel).HasMaxLength(50);
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.Property(e => e.LastValidationError).HasMaxLength(1000);
            
            // Pricing precision
            entity.Property(e => e.FixedShippingCost).HasPrecision(18, 2);
            entity.Property(e => e.MarkupPercentage).HasPrecision(5, 2);
            entity.Property(e => e.FreeShippingThreshold).HasPrecision(18, 2);
            
            // Query filter for tenant isolation
            entity.HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<CarrierAccount>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasAlternateKey(e => new { e.TenantId, e.Id })
                .HasName("AK_CarrierAccounts_TenantId_Id");
            entity.HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);
            entity.HasIndex(e => new { e.TenantId, e.CarrierCode })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");
            entity.Property(e => e.CarrierCode).HasMaxLength(50).IsRequired();
            entity.Property(e => e.HealthReason).HasMaxLength(1000);
            entity.Property(e => e.State).HasConversion<string>().HasMaxLength(40);
            entity.Property(e => e.Environment).HasConversion<string>().HasMaxLength(40);
            entity.Property(e => e.OperationalState).HasConversion<string>().HasMaxLength(40);
            entity.Property(e => e.HealthState).HasConversion<string>().HasMaxLength(40);
        });

        modelBuilder.Entity<CarrierCredentialReference>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);
            entity.HasIndex(e => new { e.TenantId, e.CarrierAccountId, e.State });
            entity.Property(e => e.SecretReference).HasMaxLength(300).IsRequired();
            entity.Property(e => e.MaskedFingerprint).HasMaxLength(120);
            entity.Property(e => e.State).HasConversion<string>().HasMaxLength(40);
        });

        modelBuilder.Entity<CarrierWarehouseAssignment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);
            entity.HasIndex(e => new { e.TenantId, e.CarrierAccountId, e.WarehouseId })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");
            entity.Property(e => e.ReadinessEvidenceReference).HasMaxLength(300);
            entity.HasOne<CarrierAccount>()
                .WithMany()
                .HasForeignKey(e => new { e.TenantId, e.CarrierAccountId })
                .HasPrincipalKey(e => new { e.TenantId, e.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Warehouse>()
                .WithMany()
                .HasForeignKey(e => new { e.TenantId, e.WarehouseId })
                .HasPrincipalKey(e => new { e.TenantId, e.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CustomerPayment>(entity =>
        {
            entity.ToTable("CustomerPayments", table =>
            {
                table.HasCheckConstraint(
                    "CK_CustomerPayments_Amount_Positive",
                    "\"Amount\" > 0");
                table.HasCheckConstraint(
                    "CK_CustomerPayments_Allocation_Consistent",
                    "\"CashAmount\" >= 0 AND \"CardAmount\" >= 0 AND \"BankTransferAmount\" >= 0 AND " +
                    "\"CashAmount\" + \"CardAmount\" + \"BankTransferAmount\" = \"Amount\"");
            });
            entity.HasKey(payment => payment.Id);
            entity.HasIndex(payment => new { payment.TenantId, payment.IdempotencyKey })
                .IsUnique()
                .HasDatabaseName("IX_CustomerPayments_TenantId_IdempotencyKey");
            entity.HasIndex(payment => new { payment.TenantId, payment.CustomerId, payment.PaymentDate });
            entity.Property(payment => payment.Amount).HasPrecision(18, 2);
            entity.Property(payment => payment.CashAmount).HasPrecision(18, 2);
            entity.Property(payment => payment.CardAmount).HasPrecision(18, 2);
            entity.Property(payment => payment.BankTransferAmount).HasPrecision(18, 2);
            entity.Property(payment => payment.IdempotencyKey).HasMaxLength(100);
            entity.Property(payment => payment.ReferenceNumber).HasMaxLength(100);
            entity.Property(payment => payment.Notes).HasMaxLength(1000);

            entity.HasOne(payment => payment.Customer)
                .WithMany()
                .HasForeignKey(payment => payment.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CarrierAccountPolicy>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);
            entity.HasIndex(e => new { e.TenantId, e.CarrierAccountId, e.Scope })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");
            entity.Property(e => e.Scope).HasConversion<string>().HasMaxLength(40);
            entity.Property(e => e.Mode).HasConversion<string>().HasMaxLength(40);
            entity.Property(e => e.ReadinessEvidenceReference).HasMaxLength(300);
        });

        modelBuilder.Entity<CarrierServiceLevelEnablement>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);
            entity.HasIndex(e => new { e.TenantId, e.CarrierAccountId, e.ServiceLevelCode })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");
            entity.Property(e => e.ServiceLevelCode).HasMaxLength(80).IsRequired();
        });

        modelBuilder.Entity<CarrierConnectionTestRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);
            entity.HasIndex(e => new { e.TenantId, e.CarrierAccountId, e.Status, e.TestedAtUtc });
            entity.Property(e => e.Environment).HasConversion<string>().HasMaxLength(40);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(40);
            entity.Property(e => e.FailureCode).HasMaxLength(100);
            entity.Property(e => e.FailureReason).HasMaxLength(1000);
        });

        modelBuilder.Entity<CarrierAdministrationAudit>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);
            entity.HasIndex(e => new { e.TenantId, e.CarrierAccountId, e.OccurredAtUtc });
            entity.Property(e => e.Action).HasConversion<string>().HasMaxLength(80);
            entity.Property(e => e.ActorId).HasMaxLength(120).IsRequired();
            entity.Property(e => e.CorrelationId).HasMaxLength(120);
            entity.Property(e => e.PreviousState).HasMaxLength(80);
            entity.Property(e => e.NextState).HasMaxLength(80);
            entity.Property(e => e.Reason).HasMaxLength(1000);
        });
        
        // -------------------------------------------------------------------------
        // SHIPMENT CONFIGURATION (Carrier-Agnostic Shipping Module)
        // Enhanced shipment entity with carrier fields
        // -------------------------------------------------------------------------
        modelBuilder.Entity<Shipment>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            // Index for order lookup (one shipment per order)
            entity.HasIndex(e => new { e.TenantId, e.OrderId })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");
            
            // Index for tracking number lookup
            entity.HasIndex(e => new { e.CarrierCode, e.TrackingNumber });
            
            // Index for status-based queries
            entity.HasIndex(e => new { e.TenantId, e.Status, e.LastCarrierSyncAtUtc });
            
            // Index for carrier-specific queries
            entity.HasIndex(e => new { e.TenantId, e.CarrierCode, e.Status });

            // Index for external carrier reconciliation and duplicate suppression
            entity.HasIndex(e => new { e.TenantId, e.CarrierCode, e.ExternalReferenceId })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false AND \"ExternalReferenceId\" IS NOT NULL");
            entity.HasIndex(e => new { e.TenantId, e.CarrierCode, e.CarrierRequestId })
                .IsUnique()
                .HasDatabaseName("IX_Shipments_TenantId_CarrierCode_CarrierRequestId")
                .HasFilter("\"IsDeleted\" = false AND \"CarrierRequestId\" IS NOT NULL");
            
            // Field constraints
            entity.Property(e => e.CarrierCode).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ServiceLevel).HasMaxLength(50);
            entity.Property(e => e.Currency).HasMaxLength(10);
            entity.Property(e => e.TrackingNumber).HasMaxLength(100);
            entity.Property(e => e.TrackingUrl).HasMaxLength(500);
            entity.Property(e => e.AwbNumber).HasMaxLength(100);
            entity.Property(e => e.ShipmentNumber).HasMaxLength(50);
            entity.Property(e => e.RecipientName).HasMaxLength(200);
            entity.Property(e => e.RecipientPhone).HasMaxLength(50);
            entity.Property(e => e.RecipientEmail).HasMaxLength(255);
            entity.Property(e => e.ShippingAddress).HasMaxLength(500);
            entity.Property(e => e.ShippingAddressLine2).HasMaxLength(500);
            entity.Property(e => e.ShippingCity).HasMaxLength(100);
            entity.Property(e => e.ShippingState).HasMaxLength(100);
            entity.Property(e => e.ShippingZipCode).HasMaxLength(20);
            entity.Property(e => e.ShippingCountry).HasMaxLength(10);
            entity.Property(e => e.PackageDescription).HasMaxLength(500);
            entity.Property(e => e.CancellationReason).HasMaxLength(500);
            entity.Property(e => e.FailureReason).HasMaxLength(500);
            entity.Property(e => e.ReturnReason).HasMaxLength(500);
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.Property(e => e.LabelUrl).HasMaxLength(1000);
            entity.Property(e => e.LabelFormat).HasMaxLength(20);
            entity.Property(e => e.ShippingMethodName).HasMaxLength(200);
            entity.Property(e => e.ExternalReferenceId).HasMaxLength(200);
            entity.Property(e => e.CarrierRequestId).HasMaxLength(200);
            entity.Property(e => e.ExternalRegistrationState).HasMaxLength(50);
            entity.Property(e => e.ExternalRegistrationError).HasMaxLength(500);
            entity.Property(e => e.CarrierGeographicMappingReleaseKey).HasMaxLength(128);
            entity.Property(e => e.CarrierGeographicEnvironment).HasMaxLength(24);
            entity.Property(e => e.CarrierGeographicAccountScope).HasMaxLength(128);
            entity.Property(e => e.CarrierGeographicMappingSnapshotJson).HasColumnType("jsonb");
            entity.HasIndex(e => new { e.TenantId, e.CarrierGeographicMappingReleaseId })
                .HasDatabaseName("IX_Shipments_TenantId_CarrierGeoMappingRelease");
            ConfigureShipmentGeographicSnapshotProperties(entity);
            
            // Precision for decimal fields
            entity.Property(e => e.ShippingCost).HasPrecision(18, 2);
            entity.Property(e => e.CodAmount).HasPrecision(18, 2);
            entity.Property(e => e.WeightKg).HasPrecision(10, 3);
            entity.Property(e => e.Latitude).HasPrecision(9, 6);
            entity.Property(e => e.Longitude).HasPrecision(9, 6);
            
            // Relationships
            entity.HasOne(e => e.Order)
                .WithMany()
                .HasForeignKey(e => e.OrderId)
                .OnDelete(DeleteBehavior.Restrict);
            
            entity.HasOne(e => e.ShippingMethod)
                .WithMany()
                .HasForeignKey(e => e.ShippingMethodId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureOrderGeographicSnapshotProperties(EntityTypeBuilder<Order> entity)
    {
        entity.HasIndex(o => new { o.TenantId, o.ShippingGeographicCatalogReleaseId, o.ShippingCountryPlaceId })
            .HasDatabaseName("IX_Orders_TenantId_ShippingGeoRelease_Country");
        entity.HasIndex(o => o.ShippingAddressAdmissionEvidenceId)
            .HasDatabaseName("IX_Orders_ShippingAddressAdmissionEvidenceId");

        entity.Property(o => o.ShippingCountryDisplayName).HasMaxLength(200);
        entity.Property(o => o.ShippingAdministrativeAreaDisplayName).HasMaxLength(200);
        entity.Property(o => o.ShippingLocalityDisplayName).HasMaxLength(200);
        entity.Property(o => o.ShippingDistrictDisplayName).HasMaxLength(200);
        entity.Property(o => o.ShippingZoneDisplayName).HasMaxLength(200);
        entity.Property(o => o.ShippingStreet).HasMaxLength(500);
        entity.Property(o => o.ShippingStreet2).HasMaxLength(500);
        entity.Property(o => o.ShippingBuilding).HasMaxLength(120);
        entity.Property(o => o.ShippingFloor).HasMaxLength(64);
        entity.Property(o => o.ShippingApartment).HasMaxLength(64);
        entity.Property(o => o.ShippingLandmark).HasMaxLength(200);

        entity.HasIndex(o => new { o.TenantId, o.BillingGeographicCatalogReleaseId, o.BillingCountryPlaceId })
            .HasDatabaseName("IX_Orders_TenantId_BillingGeoRelease_Country");
        entity.HasIndex(o => o.BillingAddressAdmissionEvidenceId)
            .HasDatabaseName("IX_Orders_BillingAddressAdmissionEvidenceId");

        entity.Property(o => o.BillingCountryDisplayName).HasMaxLength(200);
        entity.Property(o => o.BillingAdministrativeAreaDisplayName).HasMaxLength(200);
        entity.Property(o => o.BillingLocalityDisplayName).HasMaxLength(200);
        entity.Property(o => o.BillingDistrictDisplayName).HasMaxLength(200);
        entity.Property(o => o.BillingZoneDisplayName).HasMaxLength(200);
        entity.Property(o => o.BillingStreet).HasMaxLength(500);
        entity.Property(o => o.BillingStreet2).HasMaxLength(500);
        entity.Property(o => o.BillingBuilding).HasMaxLength(120);
        entity.Property(o => o.BillingFloor).HasMaxLength(64);
        entity.Property(o => o.BillingApartment).HasMaxLength(64);
        entity.Property(o => o.BillingLandmark).HasMaxLength(200);
    }

    private static void ConfigureShipmentGeographicSnapshotProperties(EntityTypeBuilder<Shipment> entity)
    {
        entity.HasIndex(e => new { e.TenantId, e.DestinationGeographicCatalogReleaseId, e.DestinationCountryPlaceId })
            .HasDatabaseName("IX_Shipments_TenantId_DestinationGeoRelease_Country");
        entity.HasIndex(e => e.DestinationAddressAdmissionEvidenceId)
            .HasDatabaseName("IX_Shipments_DestinationAddressAdmissionEvidenceId");
        entity.HasIndex(e => new { e.TenantId, e.OriginGeographicCatalogReleaseId, e.OriginCountryPlaceId })
            .HasDatabaseName("IX_Shipments_TenantId_OriginGeoRelease_Country");
        entity.HasIndex(e => e.OriginAddressAdmissionEvidenceId)
            .HasDatabaseName("IX_Shipments_OriginAddressAdmissionEvidenceId");

        entity.Property(e => e.DestinationCountryDisplayName).HasMaxLength(200);
        entity.Property(e => e.DestinationAdministrativeAreaDisplayName).HasMaxLength(200);
        entity.Property(e => e.DestinationLocalityDisplayName).HasMaxLength(200);
        entity.Property(e => e.DestinationDistrictDisplayName).HasMaxLength(200);
        entity.Property(e => e.DestinationZoneDisplayName).HasMaxLength(200);
        entity.Property(e => e.DestinationStreet).HasMaxLength(500);
        entity.Property(e => e.DestinationStreet2).HasMaxLength(500);
        entity.Property(e => e.DestinationBuilding).HasMaxLength(120);
        entity.Property(e => e.DestinationFloor).HasMaxLength(64);
        entity.Property(e => e.DestinationApartment).HasMaxLength(64);
        entity.Property(e => e.DestinationLandmark).HasMaxLength(200);
        entity.Property(e => e.OriginCountryIso2).HasMaxLength(2);
        entity.Property(e => e.OriginCountryDisplayName).HasMaxLength(200);
        entity.Property(e => e.OriginAdministrativeAreaDisplayName).HasMaxLength(200);
        entity.Property(e => e.OriginLocalityDisplayName).HasMaxLength(200);
        entity.Property(e => e.OriginDistrictDisplayName).HasMaxLength(200);
        entity.Property(e => e.OriginZoneDisplayName).HasMaxLength(200);
        entity.Property(e => e.OriginContactName).HasMaxLength(200);
        entity.Property(e => e.OriginContactPhone).HasMaxLength(64);
        entity.Property(e => e.OriginStreet).HasMaxLength(500);
    }

    private void ConfigureAccountingOutboxConcurrency(ModelBuilder modelBuilder)
    {
        var versionProperty = modelBuilder.Entity<AccountingOutboxMessage>().Property<uint>("xmin");

        if (Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            versionProperty
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
            return;
        }

        // Non-PostgreSQL: manually incremented in IncrementRowVersionForNonPostgreSQL()
        versionProperty
            .HasColumnName("xmin")
            .HasDefaultValue(1u)
            .IsConcurrencyToken();
    }

    private void ConfigurePosCartConcurrency(ModelBuilder modelBuilder)
    {
        var cartRowVersion = modelBuilder.Entity<PosCart>().Property(cart => cart.RowVersion);
        var outboxRowVersion = modelBuilder.Entity<PosCartOutboxMessage>().Property(message => message.RowVersion);

        if (Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            cartRowVersion
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .IsRowVersion();
            outboxRowVersion
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .IsRowVersion();
            return;
        }

        // Non-PostgreSQL providers keep regular uint values; runtime concurrency is enforced
        // by the explicit cart Version contract outside PostgreSQL integration tests.
        cartRowVersion
            .HasColumnName(nameof(PosCart.RowVersion))
            .ValueGeneratedNever()
            .IsConcurrencyToken(false);
        outboxRowVersion
            .HasColumnName(nameof(PosCartOutboxMessage.RowVersion))
            .ValueGeneratedNever()
            .IsConcurrencyToken(false);
    }
    
    public override int SaveChanges()
    {
        ApplyAuditAndCommerceHubImmutabilityGuards();
        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await ApplyAuditAndCommerceHubImmutabilityGuardsAsync(cancellationToken);
        return await base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyAuditAndCommerceHubImmutabilityGuards()
    {
        NormalizeCouponCodes();
        NormalizePosUserIdentities();
        BumpPosUserRowVersion();
        EnsureCanonicalIdentityLinks();
        GuardPosTenantForeignKeyCoOwnership();
        GuardShippingMethodWarehouseAuthority();
        ApplyAuditAndCommerceHubImmutabilityGuardsCore();
    }

    private async Task ApplyAuditAndCommerceHubImmutabilityGuardsAsync(CancellationToken cancellationToken)
    {
        NormalizeCouponCodes();
        NormalizePosUserIdentities();
        BumpPosUserRowVersion();
        await EnsureCanonicalIdentityLinksAsync(cancellationToken);
        await GuardPosTenantForeignKeyCoOwnershipAsync(cancellationToken);
        await GuardShippingMethodWarehouseAuthorityAsync(cancellationToken);
        ApplyAuditAndCommerceHubImmutabilityGuardsCore();
    }

    private void GuardShippingMethodWarehouseAuthority()
    {
        foreach (var method in PendingActiveShippingMethods())
        {
            if (!method.FulfillmentWarehouseId.HasValue || method.FulfillmentWarehouseId == Guid.Empty)
            {
                throw new InvalidOperationException("Active shipping methods require a fulfillment warehouse.");
            }

            var trackedWarehouse = ChangeTracker.Entries<Warehouse>().FirstOrDefault(entry =>
                entry.Entity.Id == method.FulfillmentWarehouseId.Value &&
                entry.Entity.TenantId == method.TenantId);
            var warehouseIsValid = trackedWarehouse != null
                ? trackedWarehouse.State != EntityState.Deleted &&
                  trackedWarehouse.Entity.IsActive &&
                  !trackedWarehouse.Entity.IsDeleted
                : Warehouses.IgnoreQueryFilters().Any(warehouse =>
                    warehouse.Id == method.FulfillmentWarehouseId.Value &&
                    warehouse.TenantId == method.TenantId &&
                    warehouse.IsActive &&
                    !warehouse.IsDeleted);
            if (!warehouseIsValid)
            {
                throw new InvalidOperationException("Active shipping methods require an active tenant-owned fulfillment warehouse.");
            }
        }

        GuardWarehousesCannotInvalidateActiveShippingMethods(useAsync: false).GetAwaiter().GetResult();
    }

    private async Task GuardShippingMethodWarehouseAuthorityAsync(CancellationToken cancellationToken)
    {
        foreach (var method in PendingActiveShippingMethods())
        {
            if (!method.FulfillmentWarehouseId.HasValue || method.FulfillmentWarehouseId == Guid.Empty)
            {
                throw new InvalidOperationException("Active shipping methods require a fulfillment warehouse.");
            }

            var trackedWarehouse = ChangeTracker.Entries<Warehouse>().FirstOrDefault(entry =>
                entry.Entity.Id == method.FulfillmentWarehouseId.Value &&
                entry.Entity.TenantId == method.TenantId);
            var persistedWarehouseIsValid = trackedWarehouse != null
                ? trackedWarehouse.State != EntityState.Deleted &&
                  trackedWarehouse.Entity.IsActive &&
                  !trackedWarehouse.Entity.IsDeleted
                : await Warehouses.IgnoreQueryFilters().AnyAsync(warehouse =>
                    warehouse.Id == method.FulfillmentWarehouseId.Value &&
                    warehouse.TenantId == method.TenantId &&
                    warehouse.IsActive &&
                    !warehouse.IsDeleted,
                    cancellationToken);
            if (!persistedWarehouseIsValid)
            {
                throw new InvalidOperationException("Active shipping methods require an active tenant-owned fulfillment warehouse.");
            }
        }

        await GuardWarehousesCannotInvalidateActiveShippingMethods(useAsync: true, cancellationToken);
    }

    private async Task GuardWarehousesCannotInvalidateActiveShippingMethods(
        bool useAsync,
        CancellationToken cancellationToken = default)
    {
        var invalidatedWarehouses = ChangeTracker.Entries<Warehouse>()
            .Where(entry => entry.State is EntityState.Modified or EntityState.Deleted)
            .Where(entry => entry.State == EntityState.Deleted || !entry.Entity.IsActive || entry.Entity.IsDeleted)
            .Select(entry => new { entry.Entity.Id, entry.Entity.TenantId })
            .ToList();
        var deactivatedMethodIds = ChangeTracker.Entries<ShippingMethod>()
            .Where(entry => entry.State is EntityState.Modified or EntityState.Deleted)
            .Where(entry => entry.State == EntityState.Deleted || !entry.Entity.IsActive || entry.Entity.IsDeleted)
            .Select(entry => entry.Entity.Id)
            .ToList();

        foreach (var warehouse in invalidatedWarehouses)
        {
            var query = ShippingMethods.IgnoreQueryFilters().Where(method =>
                method.TenantId == warehouse.TenantId &&
                method.FulfillmentWarehouseId == warehouse.Id &&
                method.IsActive &&
                !method.IsDeleted &&
                !deactivatedMethodIds.Contains(method.Id));
            var hasDependentActiveMethod = useAsync
                ? await query.AnyAsync(cancellationToken)
                : query.Any();
            if (hasDependentActiveMethod)
            {
                throw new InvalidOperationException(
                    "A warehouse used by an active shipping method cannot be deactivated or deleted.");
            }
        }
    }

    private IEnumerable<ShippingMethod> PendingActiveShippingMethods() =>
        ChangeTracker.Entries<ShippingMethod>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .Select(entry => entry.Entity)
            .Where(method => method.IsActive && !method.IsDeleted);

    /// <summary>
    /// Shared core logic for audit and immutability guards.
    /// Extracted to prevent divergence between sync and async paths.
    /// </summary>
    private void ApplyAuditAndCommerceHubImmutabilityGuardsCore()
    {
        GuardPurchaseInvoiceFinancialIntegrity();
        GuardDiscountConfigurationIntegrity();
        GuardCommerceHubCurrencySovereignty();
        GuardCommerceHubBoundaryOwnership();
        GuardCommerceHubOrderImmutability();
        GuardCommerceHubFinancialLifecycleRecordImmutability();
        GuardCommerceHubFinancialLedgerImmutability();
        GuardTreasuryCashAuthority();
        GuardSupplierCardAuthority();
        GuardCommerceHubFinancialSafetyGuardImmutability();
        GuardCommerceHubVendorSettlementImmutability();
        GuardCommerceHubPriceAuthority();
        GuardPosHistoricalCostSnapshotImmutability();
        GuardForensicEvidenceImmutability();
        GuardAffiliateAttributionAuthority();
        GuardAffiliatePayoutAuthority();
        GuardStockMovementImmutability();
        GuardUserGovernanceProfileAuthority();
        GuardUserGovernanceAuditImmutability();
        GuardGeographicReleaseAndAuditAuthority();
        GuardCustomsEvidenceRuntimeAuthority();
        GuardCarrierAdministrationAuditAuthority();
        GuardGeographicBusinessSnapshotPersistence();
        GuardStorefrontOrderHistoricalSnapshotImmutability();
        GuardOrderCheckoutFieldSnapshotImmutability();
        GuardWarehouseResolutionAuthority();
        MarkProductsChangedByDependentSearchMutations();
        IncrementMarketplaceProjectionMutationVersions();
        ResetCommerceHubProjectionMarkersForBusinessMutations();
        IncrementRowVersionForNonPostgreSQL();

        var entries = ChangeTracker.Entries<BaseEntity>();
        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = DateTime.UtcNow;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
        }

        foreach (var entry in ChangeTracker.Entries<GeographicEntity>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAtUtc == default)
            {
                entry.Entity.CreatedAtUtc = DateTime.UtcNow;
            }
        }
    }

    private void GuardCarrierAdministrationAuditAuthority()
    {
        foreach (var entry in ChangeTracker.Entries<CarrierAdministrationAudit>())
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{nameof(CarrierAdministrationAudit)} records are append-only and cannot be deleted.");
            }

            if (entry.State == EntityState.Modified)
            {
                throw new InvalidOperationException(
                    $"{nameof(CarrierAdministrationAudit)} records are append-only and cannot be updated.");
            }
        }
    }

    private void GuardGeographicReleaseAndAuditAuthority()
    {
        GuardAppendOnlyEvidence<GeographicCatalogPublication>();
        GuardAppendOnlyEvidence<CarrierGeographicMappingPublication>();
        GuardAppendOnlyEvidence<PlaceMergeHistory>();
        GuardAppendOnlyEvidence<PlaceDeprecation>();
        GuardAppendOnlyEvidence<AddressAdmissionEvidence>();
        GuardAppendOnlyEvidence<GeographicAudit>();

        foreach (var entry in ChangeTracker.Entries<GeographicCatalogRelease>())
        {
            GuardPublishedReleaseMutation(entry, nameof(GeographicCatalogRelease.Status));
        }

        foreach (var entry in ChangeTracker.Entries<CarrierGeographicMappingRelease>())
        {
            GuardPublishedReleaseMutation(entry, nameof(CarrierGeographicMappingRelease.Status));
        }
    }

    private void GuardCustomsEvidenceRuntimeAuthority()
    {
        GuardAppendOnlyEvidence<CustomsEvidenceVersion>();
        GuardAppendOnlyEvidence<CustomsEvidenceHistory>();
        GuardAppendOnlyEvidence<CustomsEvidenceFingerprint>();
        GuardAppendOnlyEvidence<ShipmentCustomsSnapshot>();
        GuardAppendOnlyEvidence<ShipmentCustomsSnapshotLine>();
        GuardAppendOnlyEvidence<CustomsAdmissionAudit>();
        GuardAppendOnlyEvidence<CustomsEvidenceApproval>();
        GuardAppendOnlyEvidence<CustomsEvidencePublication>();
        GuardAppendOnlyEvidence<CustomsAdministrationAudit>();

        foreach (var entry in ChangeTracker.Entries<ShipmentCustomsSnapshot>())
        {
            if (entry.State == EntityState.Added)
            {
                ValidateShipmentCustomsSnapshot(entry.Entity);
            }
        }

        foreach (var entry in ChangeTracker.Entries<ShipmentCustomsSnapshotLine>())
        {
            if (entry.State == EntityState.Added)
            {
                ValidateShipmentCustomsSnapshotLine(entry.Entity);
            }
        }
    }

    private static void ValidateShipmentCustomsSnapshot(ShipmentCustomsSnapshot snapshot)
    {
        if (snapshot.TenantId == Guid.Empty ||
            snapshot.ShipmentId == Guid.Empty ||
            snapshot.OrderId == Guid.Empty ||
            snapshot.SnapshotVersion != 1 ||
            snapshot.CarrierNeutralDtoVersion != 1 ||
            !snapshot.IsRegistrationFrozen ||
            snapshot.RegistrationBeganAtUtc == default ||
            snapshot.ShipmentGrossWeight <= 0 ||
            string.IsNullOrWhiteSpace(snapshot.ShipmentGrossWeightUnit) ||
            string.IsNullOrWhiteSpace(snapshot.OriginCountryIso2) ||
            string.IsNullOrWhiteSpace(snapshot.DestinationCountryIso2) ||
            string.IsNullOrWhiteSpace(snapshot.SnapshotJson) ||
            snapshot.SnapshotFingerprintSha256.Length != 64)
        {
            throw new InvalidOperationException("Shipment customs snapshot is incomplete and cannot be persisted.");
        }
    }

    private static void ValidateShipmentCustomsSnapshotLine(ShipmentCustomsSnapshotLine line)
    {
        if (line.TenantId == Guid.Empty ||
            line.ShipmentCustomsSnapshotId == Guid.Empty ||
            line.ShipmentId == Guid.Empty ||
            line.OrderId == Guid.Empty ||
            line.OrderItemId == Guid.Empty ||
            line.ProductId == Guid.Empty ||
            line.CustomsEvidenceId == Guid.Empty ||
            line.CustomsEvidenceVersionId == Guid.Empty ||
            line.EvidenceVersionNumber <= 0 ||
            string.IsNullOrWhiteSpace(line.HsCode) ||
            string.IsNullOrWhiteSpace(line.CommodityDescription) ||
            string.IsNullOrWhiteSpace(line.CountryOfOriginIso2) ||
            line.DeclaredValue <= 0 ||
            string.IsNullOrWhiteSpace(line.Currency) ||
            line.Quantity <= 0 ||
            string.IsNullOrWhiteSpace(line.QuantityUnit) ||
            line.NetWeight <= 0 ||
            string.IsNullOrWhiteSpace(line.NetWeightUnit) ||
            line.SourceEvidenceFingerprintSha256.Length != 64)
        {
            throw new InvalidOperationException("Shipment customs snapshot line is incomplete and cannot be persisted.");
        }
    }

    private void GuardGeographicBusinessSnapshotPersistence()
    {
        foreach (var entry in ChangeTracker.Entries<CustomerAddressVersion>())
        {
            if (entry.State == EntityState.Modified || entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException("Customer address versions are immutable geographic snapshots; edit by creating a new admitted version.");
            }

            if (entry.State == EntityState.Added)
            {
                ValidateSavedAddressVersionSnapshot(entry.Entity);
                AddGeographicSnapshotAudit(
                    entry.Entity.TenantId,
                    "AddressAdmissionLinked",
                    nameof(CustomerAddressVersion),
                    entry.Entity.Id,
                    entry.Entity.GeographicCatalogReleaseId,
                    "Saved customer address version linked to admitted geographic evidence.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<Order>())
        {
            if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
            {
                ValidateOrderGeographicSnapshot(entry.Entity);
            }

            if (entry.State == EntityState.Modified)
            {
                GuardSnapshotPropertyMutation(entry, "Shipping", "Order shipping");
                GuardSnapshotPropertyMutation(entry, "Billing", "Order billing");
            }

            if (entry.State == EntityState.Added && entry.Entity.ShippingAddressAdmissionEvidenceId.HasValue)
            {
                AddGeographicSnapshotAudit(
                    entry.Entity.TenantId,
                    "OrderGeographicSnapshotCreated",
                    nameof(Order),
                    entry.Entity.Id,
                    entry.Entity.ShippingGeographicCatalogReleaseId,
                    "Order persisted immutable admitted shipping geographic snapshot.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<Shipment>().ToList())
        {
            if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
            {
                ValidateShipmentGeographicSnapshot(entry.Entity);
            }

            if (entry.State == EntityState.Modified)
            {
                GuardSnapshotPropertyMutation(entry, "Destination", "Shipment destination");
                var originalOriginSnapshot = entry.Property(nameof(Shipment.OriginSnapshotJson)).OriginalValue as string;
                if (!string.IsNullOrWhiteSpace(originalOriginSnapshot))
                {
                    GuardSnapshotPropertyMutation(entry, "Origin", "Shipment origin");
                }
                var originalMappingSnapshot = entry.Property(nameof(Shipment.CarrierGeographicMappingSnapshotJson)).OriginalValue as string;
                if (!string.IsNullOrWhiteSpace(originalMappingSnapshot))
                {
                    GuardSnapshotPropertyMutation(entry, "CarrierGeographic", "Shipment carrier mapping");
                }
            }

            if (entry.State == EntityState.Added && entry.Entity.DestinationAddressAdmissionEvidenceId.HasValue)
            {
                AddGeographicSnapshotAudit(
                    entry.Entity.TenantId,
                    "ShipmentGeographicSnapshotCreated",
                    nameof(Shipment),
                    entry.Entity.Id,
                    entry.Entity.DestinationGeographicCatalogReleaseId,
                    "Shipment persisted immutable destination geographic snapshot copied from order snapshot.");
            }
        }
    }

    private void AddGeographicSnapshotAudit(
        Guid? tenantId,
        string action,
        string aggregateType,
        Guid aggregateId,
        Guid? catalogReleaseId,
        string reason)
    {
        if (aggregateId == Guid.Empty || ChangeTracker.Entries<GeographicAudit>().Any(e => e.Entity.Action == action && e.Entity.AggregateId == aggregateId))
        {
            return;
        }

        GeographicAudit.Add(new GeographicAudit
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorKind = GeographicAuditActorKind.System,
            ActorRole = "BackendPersistenceAuthority",
            Action = action,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            CatalogReleaseId = catalogReleaseId,
            Reason = reason,
            CorrelationId = Guid.NewGuid().ToString("N"),
            OccurredAtUtc = DateTime.UtcNow
        });
    }

    private static void ValidateSavedAddressVersionSnapshot(CustomerAddressVersion version)
    {
        if (version.AddressAdmissionEvidenceId == Guid.Empty ||
            version.GeographicCatalogReleaseId == Guid.Empty ||
            version.CountryPlaceId == Guid.Empty ||
            version.SnapshotVersion != 1)
        {
            throw new InvalidOperationException("Saved address geographic snapshot is broken and cannot be persisted.");
        }

        if (version.AdmittedAtUtc == default)
        {
            version.AdmittedAtUtc = DateTime.UtcNow;
        }

        if (version.SnapshotCreatedAtUtc == default)
        {
            version.SnapshotCreatedAtUtc = DateTime.UtcNow;
        }
    }

    private static void GuardSnapshotPropertyMutation<TEntity>(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity> entry,
        string propertyPrefix,
        string owner)
        where TEntity : class
    {
        if (entry.Properties.Any(property => property.IsModified && IsGeographicSnapshotProperty(property.Metadata.Name, propertyPrefix)))
        {
            throw new InvalidOperationException($"{owner} geographic snapshot is immutable and cannot be updated after persistence.");
        }
    }

    private static bool IsGeographicSnapshotProperty(string propertyName, string prefix)
    {
        if (!propertyName.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        return propertyName.Contains("Geographic", StringComparison.Ordinal) ||
               propertyName.Contains("PlaceId", StringComparison.Ordinal) ||
               propertyName.Contains("DisplayName", StringComparison.Ordinal) ||
               propertyName.Contains("AddressAdmissionEvidenceId", StringComparison.Ordinal) ||
               propertyName.EndsWith("Street", StringComparison.Ordinal) ||
               propertyName.EndsWith("Street2", StringComparison.Ordinal) ||
               propertyName.EndsWith("Building", StringComparison.Ordinal) ||
               propertyName.EndsWith("Floor", StringComparison.Ordinal) ||
               propertyName.EndsWith("Apartment", StringComparison.Ordinal) ||
               propertyName.EndsWith("Landmark", StringComparison.Ordinal);
    }

    private static void ValidateOrderGeographicSnapshot(Order order)
    {
        ValidateBusinessSnapshot(
            hasSnapshot: order.ShippingAddressAdmissionEvidenceId.HasValue || order.ShippingCountryPlaceId.HasValue,
            evidenceId: order.ShippingAddressAdmissionEvidenceId,
            catalogReleaseId: order.ShippingGeographicCatalogReleaseId,
            countryPlaceId: order.ShippingCountryPlaceId,
            snapshotVersion: order.ShippingGeographicSnapshotVersion,
            snapshotCreatedAtUtc: order.ShippingGeographicSnapshotCreatedAtUtc,
            owner: "Order shipping");

        ValidateBusinessSnapshot(
            hasSnapshot: order.BillingAddressAdmissionEvidenceId.HasValue || order.BillingCountryPlaceId.HasValue,
            evidenceId: order.BillingAddressAdmissionEvidenceId,
            catalogReleaseId: order.BillingGeographicCatalogReleaseId,
            countryPlaceId: order.BillingCountryPlaceId,
            snapshotVersion: order.BillingGeographicSnapshotVersion,
            snapshotCreatedAtUtc: order.BillingGeographicSnapshotCreatedAtUtc,
            owner: "Order billing");
    }

    private void GuardStorefrontOrderHistoricalSnapshotImmutability()
    {
        GuardImmutableProperties<Order>(
            "Order historical, identity, pricing, currency, shipping, and payment-method snapshots are immutable after creation",
            nameof(Order.TenantId),
            nameof(Order.OrderNumber),
            nameof(Order.CustomerId),
            nameof(Order.OnlineCustomerId),
            nameof(Order.SessionId),
            nameof(Order.CartId),
            nameof(Order.GuestEmail),
            nameof(Order.GuestPhone),
            nameof(Order.GuestName),
            nameof(Order.IpAddress),
            nameof(Order.IsGuestCheckout),
            nameof(Order.AffiliateAccountId),
            nameof(Order.AffiliateReferralCode),
            nameof(Order.PromotionLinkId),
            nameof(Order.FulfillmentMode),
            nameof(Order.FulfillmentCarrierCode),
            nameof(Order.ShippingAddress),
            nameof(Order.ShippingCity),
            nameof(Order.ShippingState),
            nameof(Order.ShippingZipCode),
            nameof(Order.ShippingCountry),
            nameof(Order.BillingAddress),
            nameof(Order.BillingCity),
            nameof(Order.BillingState),
            nameof(Order.BillingZipCode),
            nameof(Order.BillingCountry),
            nameof(Order.ShippingMethodId),
            nameof(Order.ShippingMethodName),
            nameof(Order.ShippingServiceLevel),
            nameof(Order.ShippingCost),
            nameof(Order.EstimatedDeliveryDays),
            nameof(Order.Subtotal),
            nameof(Order.PromotionDiscountAmount),
            nameof(Order.DiscountAmount),
            nameof(Order.ManualDiscountAmount),
            nameof(Order.TotalDiscountAmount),
            nameof(Order.TaxAmount),
            nameof(Order.Total),
            nameof(Order.Currency),
            nameof(Order.PricingSnapshotJson),
            nameof(Order.CouponCode),
            nameof(Order.CouponId),
            nameof(Order.AppliedPromotionId),
            nameof(Order.AppliedPromotionType),
            nameof(Order.AppliedPromotionName),
            nameof(Order.AppliedPromotionDescription),
            nameof(Order.PromotionCalculationFailed),
            nameof(Order.PromotionWarningMessage),
            nameof(Order.PaymentMethod),
            nameof(Order.PaymentMethodCode),
            nameof(Order.PaymentMethodName),
            nameof(Order.PaymentFee),
            nameof(Order.Source),
            nameof(Order.SourceOrderId),
            nameof(Order.WarehouseId));

        GuardImmutableProperties<OrderItem>(
            "Order item SKU, pricing, variant, tax, discount, and affiliate snapshots are immutable after creation",
            nameof(OrderItem.OrderId),
            nameof(OrderItem.ProductId),
            nameof(OrderItem.VariantId),
            nameof(OrderItem.UnitId),
            nameof(OrderItem.WarehouseId),
            nameof(OrderItem.SKU),
            nameof(OrderItem.ProductName),
            nameof(OrderItem.VariantName),
            nameof(OrderItem.VariantAttributesJson),
            nameof(OrderItem.ImageUrl),
            nameof(OrderItem.Quantity),
            nameof(OrderItem.UnitPrice),
            nameof(OrderItem.DiscountAmount),
            nameof(OrderItem.TaxAmount),
            nameof(OrderItem.Total),
            nameof(OrderItem.CostPrice),
            nameof(OrderItem.AffiliateCommissionEligible),
            nameof(OrderItem.AffiliateCommissionPolicyIdSnapshot),
            nameof(OrderItem.AffiliateCommissionRateSnapshot),
            nameof(OrderItem.AffiliateMaxCommissionPerItemSnapshot),
            nameof(OrderItem.AffiliateCommissionBaseAmountSnapshot),
            nameof(OrderItem.AffiliateCommissionAmountSnapshot),
            nameof(OrderItem.AffiliateCommissionSnapshotCapturedAtUtc));

        GuardImmutableProperties<OrderRefund>(
            "Order refund financial facts are immutable after creation",
            nameof(OrderRefund.TenantId),
            nameof(OrderRefund.OrderId),
            nameof(OrderRefund.RefundId),
            nameof(OrderRefund.Amount),
            nameof(OrderRefund.Currency),
            nameof(OrderRefund.IsFullRefund),
            nameof(OrderRefund.Reason),
            nameof(OrderRefund.PaymentMethod),
            nameof(OrderRefund.IsPostSettlementRefund),
            nameof(OrderRefund.SettlementBatchId),
            nameof(OrderRefund.SettlementType),
            nameof(OrderRefund.SettlementId),
            nameof(OrderRefund.PayoutBatchId),
            nameof(OrderRefund.SettledAtUtc),
            nameof(OrderRefund.RefundedAtUtc));

        GuardImmutableProperties<OrderRefundLine>(
            "Order refund line financial facts are immutable after creation",
            nameof(OrderRefundLine.TenantId),
            nameof(OrderRefundLine.OrderRefundId),
            nameof(OrderRefundLine.OrderId),
            nameof(OrderRefundLine.OrderItemId),
            nameof(OrderRefundLine.Amount));
    }

    private void GuardOrderCheckoutFieldSnapshotImmutability()
    {
        foreach (var entry in ChangeTracker.Entries<OrderCheckoutFieldSnapshot>())
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{nameof(OrderCheckoutFieldSnapshot)} records are immutable order-history evidence and cannot be deleted after creation.");
            }

            if (entry.State == EntityState.Modified)
            {
                throw new InvalidOperationException(
                    $"{nameof(OrderCheckoutFieldSnapshot)} records are immutable order-history evidence and cannot be updated after creation.");
            }
        }
    }

    private void GuardWarehouseResolutionAuthority()
    {
        GuardImmutableProperties<WarehouseResolution>(
            "Warehouse resolution decision inputs are immutable after creation",
            nameof(WarehouseResolution.TenantId),
            nameof(WarehouseResolution.CheckoutSessionId),
            nameof(WarehouseResolution.CartId),
            nameof(WarehouseResolution.WarehouseId),
            nameof(WarehouseResolution.ResolutionSource),
            nameof(WarehouseResolution.ResolutionVersion),
            nameof(WarehouseResolution.AlgorithmCode),
            nameof(WarehouseResolution.InputsSnapshotHash),
            nameof(WarehouseResolution.InputsSnapshotJson),
            nameof(WarehouseResolution.CreatedAtUtc));

        foreach (var entry in ChangeTracker.Entries<WarehouseResolution>())
        {
            if (entry.State == EntityState.Deleted ||
                (entry.State == EntityState.Modified && entry.Property(item => item.IsDeleted).IsModified))
            {
                throw new InvalidOperationException(
                    $"{nameof(WarehouseResolution)} records are retained checkout authority and cannot be deleted.");
            }
        }
    }

    private static void ValidateShipmentGeographicSnapshot(Shipment shipment)
    {
        ValidateBusinessSnapshot(
            hasSnapshot: shipment.DestinationAddressAdmissionEvidenceId.HasValue || shipment.DestinationCountryPlaceId.HasValue,
            evidenceId: shipment.DestinationAddressAdmissionEvidenceId,
            catalogReleaseId: shipment.DestinationGeographicCatalogReleaseId,
            countryPlaceId: shipment.DestinationCountryPlaceId,
            snapshotVersion: shipment.DestinationGeographicSnapshotVersion,
            snapshotCreatedAtUtc: shipment.DestinationGeographicSnapshotCreatedAtUtc,
            owner: "Shipment destination");

        ValidateBusinessSnapshot(
            hasSnapshot: shipment.OriginAddressAdmissionEvidenceId.HasValue ||
                         shipment.OriginCountryPlaceId.HasValue ||
                         !string.IsNullOrWhiteSpace(shipment.OriginSnapshotJson),
            evidenceId: shipment.OriginAddressAdmissionEvidenceId,
            catalogReleaseId: shipment.OriginGeographicCatalogReleaseId,
            countryPlaceId: shipment.OriginCountryPlaceId,
            snapshotVersion: shipment.OriginGeographicSnapshotVersion,
            snapshotCreatedAtUtc: shipment.OriginGeographicSnapshotCreatedAtUtc,
            owner: "Shipment origin");
        if (!string.IsNullOrWhiteSpace(shipment.OriginSnapshotJson) &&
            (!shipment.OriginWarehouseId.HasValue ||
             shipment.OriginWarehouseId.Value == Guid.Empty ||
             string.IsNullOrWhiteSpace(shipment.OriginCountryIso2) ||
             string.IsNullOrWhiteSpace(shipment.OriginCountryDisplayName) ||
             string.IsNullOrWhiteSpace(shipment.OriginContactName) ||
             string.IsNullOrWhiteSpace(shipment.OriginContactPhone) ||
             string.IsNullOrWhiteSpace(shipment.OriginStreet)))
        {
            throw new InvalidOperationException(
                "Shipment origin snapshot is incomplete and cannot be persisted.");
        }

        var hasCarrierSnapshot =
            shipment.CarrierGeographicMappingReleaseId.HasValue ||
            !string.IsNullOrWhiteSpace(shipment.CarrierGeographicMappingSnapshotJson);
        if (hasCarrierSnapshot &&
            (!shipment.CarrierGeographicMappingReleaseId.HasValue ||
             shipment.CarrierGeographicMappingReleaseId == Guid.Empty ||
             !shipment.CarrierGeographicMappingVersion.HasValue ||
             string.IsNullOrWhiteSpace(shipment.CarrierGeographicMappingReleaseKey) ||
             string.IsNullOrWhiteSpace(shipment.CarrierGeographicEnvironment) ||
             string.IsNullOrWhiteSpace(shipment.CarrierGeographicAccountScope) ||
             string.IsNullOrWhiteSpace(shipment.CarrierGeographicMappingSnapshotJson) ||
             !shipment.CarrierGeographicResolvedAtUtc.HasValue ||
             !shipment.DestinationCountryPlaceId.HasValue))
        {
            throw new InvalidOperationException(
                "Shipment carrier geographic mapping snapshot is incomplete and cannot be persisted.");
        }
    }

    private static void ValidateBusinessSnapshot(
        bool hasSnapshot,
        Guid? evidenceId,
        Guid? catalogReleaseId,
        Guid? countryPlaceId,
        int? snapshotVersion,
        DateTime? snapshotCreatedAtUtc,
        string owner)
    {
        if (!hasSnapshot)
        {
            return;
        }

        if (!evidenceId.HasValue || evidenceId.Value == Guid.Empty ||
            !catalogReleaseId.HasValue || catalogReleaseId.Value == Guid.Empty ||
            !countryPlaceId.HasValue || countryPlaceId.Value == Guid.Empty ||
            snapshotVersion != 1 ||
            !snapshotCreatedAtUtc.HasValue)
        {
            throw new InvalidOperationException($"{owner} geographic snapshot is broken and cannot be persisted.");
        }
    }

    private static void GuardPublishedReleaseMutation<TEntity>(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity> entry,
        string statusPropertyName)
        where TEntity : class
    {
        if (entry.State == EntityState.Deleted)
        {
            throw new InvalidOperationException($"{typeof(TEntity).Name} records cannot be deleted.");
        }

        if (entry.State != EntityState.Modified)
        {
            return;
        }

        var originalStatus = entry.Property(statusPropertyName).OriginalValue?.ToString();
        if (!string.Equals(originalStatus, nameof(GeographicReleaseStatus.Published), StringComparison.Ordinal))
        {
            return;
        }

        var allowedProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            statusPropertyName,
            "RowVersion"
        };

        if (entry.Properties.Any(property =>
                property.IsModified && !allowedProperties.Contains(property.Metadata.Name)))
        {
            throw new InvalidOperationException(
                $"{typeof(TEntity).Name} is immutable after publication; publish a new release instead.");
        }
    }

    private void GuardCommerceHubPriceAuthority()
    {
        foreach (var entry in ChangeTracker.Entries<CommerceHubListing>().ToList())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            var priceProperty = entry.Property(nameof(CommerceHubListing.CommerceHubPrice));
            if (priceProperty.CurrentValue is decimal price && price <= 0m)
            {
                throw new InvalidOperationException("CommerceHub price overrides must be positive when present.");
            }

            if (priceProperty.CurrentValue != null &&
                (entry.Entity.CommerceHubPriceOverrideUpdatedBy is null ||
                 entry.Entity.CommerceHubPriceOverrideUpdatedAtUtc is null))
            {
                throw new InvalidOperationException("CommerceHub price overrides require merchant actor and timestamp audit metadata.");
            }
        }
    }

    /// <summary>
    /// CommerceHub is a single-currency marketplace. Payment, refund, payout, recovery,
    /// audit, and projection-shaped records must never persist tenant or gateway currency.
    /// This is deliberately a persistence boundary guard so replay, repair, and recovery
    /// code cannot bypass the service-level payment currency checks.
    /// </summary>
    private void GuardCommerceHubCurrencySovereignty()
    {
        foreach (var entry in ChangeTracker.Entries()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            var entityType = entry.Metadata.ClrType;
            if (!entityType.Name.StartsWith("CommerceHub", StringComparison.Ordinal) ||
                entry.Metadata.FindProperty("Currency") == null)
            {
                continue;
            }

            var currency = entry.Property("Currency").CurrentValue as string;
            if (!string.Equals(currency?.Trim(), FinancialCurrencyGuard.PlatformBaseCurrencyCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"CommerceHub currency sovereignty violation blocked for {entityType.Name}. " +
                    $"Currency must be {FinancialCurrencyGuard.PlatformBaseCurrencyCode}, not '{currency ?? "(missing)"}'.");
            }

            entry.Property("Currency").CurrentValue = FinancialCurrencyGuard.PlatformBaseCurrencyCode;
        }
    }

    private void GuardCommerceHubBoundaryOwnership()
    {
        GuardBoundaryOwnedEntityCreation<CommerceHubMarketplaceOrder>(
            CommerceHubBoundaryMutationScope.IsOrderLifecycleAuthorized,
            "CommerceHub marketplace orders cannot be created outside the CommerceHub order lifecycle boundary.");

        GuardBoundaryOwnedCreationState<CommerceHubMarketplaceOrder>(
            CommerceHubBoundaryMutationScope.IsOrderLifecycleAuthorized,
            "CommerceHub marketplace orders cannot be created with lifecycle state outside the CommerceHub order lifecycle boundary.",
            nameof(CommerceHubMarketplaceOrder.Status));

        GuardBoundaryOwnedProperties<CommerceHubMarketplaceOrder>(
            CommerceHubBoundaryMutationScope.IsOrderLifecycleAuthorized,
            "CommerceHub marketplace order lifecycle fields can only be changed by the CommerceHub order lifecycle boundary.",
            nameof(CommerceHubMarketplaceOrder.Status));

        GuardBoundaryOwnedCreationState<CommerceHubMarketplaceOrder>(
            CommerceHubBoundaryMutationScope.IsPaymentLifecycleAuthorized,
            "CommerceHub marketplace orders cannot be created with payment state outside the CommerceHub payment lifecycle authority.",
            nameof(CommerceHubMarketplaceOrder.PaymentStatus),
            nameof(CommerceHubMarketplaceOrder.PaymentReferenceId),
            nameof(CommerceHubMarketplaceOrder.PaidAt));

        GuardBoundaryOwnedProperties<CommerceHubMarketplaceOrder>(
            CommerceHubBoundaryMutationScope.IsPaymentLifecycleAuthorized,
            "CommerceHub marketplace order payment fields can only be changed by the CommerceHub payment lifecycle authority.",
            nameof(CommerceHubMarketplaceOrder.PaymentStatus),
            nameof(CommerceHubMarketplaceOrder.PaymentReferenceId),
            nameof(CommerceHubMarketplaceOrder.PaidAt));

        GuardBoundaryOwnedCreationState<CommerceHubMarketplaceOrder>(
            CommerceHubBoundaryMutationScope.IsProjectionAuthorized,
            "CommerceHub marketplace orders cannot be created with projection markers outside the CommerceHub projection boundary.",
            nameof(CommerceHubMarketplaceOrder.IsProjected),
            nameof(CommerceHubMarketplaceOrder.ProjectedAtUtc),
            nameof(CommerceHubMarketplaceOrder.LastProjectionAttemptAt),
            nameof(CommerceHubMarketplaceOrder.ProjectionAttemptCount),
            nameof(CommerceHubMarketplaceOrder.IsProjectionTerminalFailure));

        GuardBoundaryOwnedProperties<CommerceHubMarketplaceOrder>(
            CommerceHubBoundaryMutationScope.IsProjectionAuthorized,
            "CommerceHub projection markers can only be changed by the CommerceHub projection boundary.",
            nameof(CommerceHubMarketplaceOrder.IsProjected),
            nameof(CommerceHubMarketplaceOrder.ProjectedAtUtc),
            nameof(CommerceHubMarketplaceOrder.LastProjectionAttemptAt),
            nameof(CommerceHubMarketplaceOrder.ProjectionAttemptCount),
            nameof(CommerceHubMarketplaceOrder.IsProjectionTerminalFailure));

        GuardBoundaryOwnedCreationState<CommerceHubSubOrder>(
            CommerceHubBoundaryMutationScope.IsOrderLifecycleAuthorized,
            "CommerceHub sub-orders cannot be created with lifecycle state outside the CommerceHub order lifecycle boundary.",
            nameof(CommerceHubSubOrder.Status));

        GuardBoundaryOwnedProperties<CommerceHubSubOrder>(
            CommerceHubBoundaryMutationScope.IsOrderLifecycleAuthorized,
            "CommerceHub sub-order lifecycle fields can only be changed by the CommerceHub order lifecycle boundary.",
            nameof(CommerceHubSubOrder.Status));

        GuardBoundaryOwnedEntityCreation<CommerceHubSubOrder>(
            CommerceHubBoundaryMutationScope.IsOrderLifecycleAuthorized,
            "CommerceHub sub-orders cannot be created outside the CommerceHub order lifecycle boundary.");

        GuardBoundaryOwnedProperties<CommerceHubSubOrder>(
            CommerceHubBoundaryMutationScope.IsShippingAssignmentAuthorized,
            "CommerceHub sub-order shipping assignment fields can only be changed by the CommerceHub shipping assignment boundary.",
            nameof(CommerceHubSubOrder.StoreId),
            nameof(CommerceHubSubOrder.Shipping),
            nameof(CommerceHubSubOrder.ShippingMethodId),
            nameof(CommerceHubSubOrder.ShippingMethodName),
            nameof(CommerceHubSubOrder.ShippingSnapshotJson));

        GuardBoundaryOwnedCreationState<CommerceHubSubOrder>(
            CommerceHubBoundaryMutationScope.IsPaymentLifecycleAuthorized,
            "CommerceHub sub-orders cannot be created with payment state outside the CommerceHub payment lifecycle authority.",
            nameof(CommerceHubSubOrder.PaymentStatus),
            nameof(CommerceHubSubOrder.PaymentReferenceId),
            nameof(CommerceHubSubOrder.PaidAt));

        GuardBoundaryOwnedProperties<CommerceHubSubOrder>(
            CommerceHubBoundaryMutationScope.IsPaymentLifecycleAuthorized,
            "CommerceHub sub-order payment fields can only be changed by the CommerceHub payment lifecycle authority.",
            nameof(CommerceHubSubOrder.PaymentStatus),
            nameof(CommerceHubSubOrder.PaymentReferenceId),
            nameof(CommerceHubSubOrder.PaidAt));

        GuardBoundaryOwnedProperties<CommerceHubSubOrder>(
            CommerceHubBoundaryMutationScope.IsFinancialWorkflowAuthorized,
            "CommerceHub sub-order financial snapshot fields can only be changed by the CommerceHub financial workflow boundary.",
            nameof(CommerceHubSubOrder.CommissionSnapshotJson),
            nameof(CommerceHubSubOrder.FinalPlatformCommissionAmount),
            nameof(CommerceHubSubOrder.PromoDiscountAmount),
            nameof(CommerceHubSubOrder.IsEarningCreated),
            nameof(CommerceHubSubOrder.EarningCreatedAtUtc),
            nameof(CommerceHubSubOrder.PlatformCommissionAmount),
            nameof(CommerceHubSubOrder.VendorEarningAmount),
            nameof(CommerceHubSubOrder.PlatformCommissionRate));

        GuardBoundaryOwnedProperties<CommerceHubSubOrderItem>(
            CommerceHubBoundaryMutationScope.IsOrderLifecycleAuthorized,
            "CommerceHub sub-order item lifecycle fields can only be changed by the CommerceHub order lifecycle boundary.",
            nameof(CommerceHubSubOrderItem.Status),
            nameof(CommerceHubSubOrderItem.DeliveredAt));

        GuardBoundaryOwnedEntityCreation<CommerceHubSubOrderItem>(
            CommerceHubBoundaryMutationScope.IsOrderLifecycleAuthorized,
            "CommerceHub sub-order items cannot be created outside the CommerceHub order lifecycle boundary.");

        // S38C: Split payment and refund scope guards to prevent refund code from mutating payment-initiation fields
        GuardBoundaryOwnedEntityCreation<CommerceHubPayment>(
            CommerceHubBoundaryMutationScope.IsPaymentAuthorized,
            "CommerceHub payments cannot be created outside the CommerceHub payment boundary.");

        GuardBoundaryOwnedCreationState<CommerceHubPayment>(
            CommerceHubBoundaryMutationScope.IsPaymentAuthorized,
            "CommerceHub payments cannot be created with payment-initiation state outside the CommerceHub payment boundary.",
            nameof(CommerceHubPayment.OrderId),
            nameof(CommerceHubPayment.Amount),
            nameof(CommerceHubPayment.Currency),
            nameof(CommerceHubPayment.Status),
            nameof(CommerceHubPayment.Provider),
            nameof(CommerceHubPayment.ProviderReference),
            nameof(CommerceHubPayment.ProviderIntentId),
            nameof(CommerceHubPayment.ProviderTransactionId),
            nameof(CommerceHubPayment.CheckoutUrl),
            nameof(CommerceHubPayment.PaymentToken),
            nameof(CommerceHubPayment.PaidAtUtc),
            nameof(CommerceHubPayment.FailureReason),
            nameof(CommerceHubPayment.IsProcessed),
            nameof(CommerceHubPayment.NeedsManualCurrencyReview),
            nameof(CommerceHubPayment.ProcessedAtUtc));

        GuardBoundaryOwnedProperties<CommerceHubPayment>(
            CommerceHubBoundaryMutationScope.IsPaymentAuthorized,
            "CommerceHub payment-initiation fields can only be changed by the CommerceHub payment boundary.",
            nameof(CommerceHubPayment.Status),
            nameof(CommerceHubPayment.ProviderIntentId),
            nameof(CommerceHubPayment.ProviderTransactionId),
            nameof(CommerceHubPayment.CheckoutUrl),
            nameof(CommerceHubPayment.PaymentToken),
            nameof(CommerceHubPayment.PaidAtUtc),
            nameof(CommerceHubPayment.FailureReason),
            nameof(CommerceHubPayment.IsProcessed),
            nameof(CommerceHubPayment.NeedsManualCurrencyReview),
            nameof(CommerceHubPayment.ProcessedAtUtc));

        GuardBoundaryOwnedEntityCreation<CommerceHubPaymentAttempt>(
            CommerceHubBoundaryMutationScope.IsPaymentAuthorized,
            "CommerceHub payment attempts cannot be created outside the CommerceHub payment boundary.");

        GuardBoundaryOwnedCreationState<CommerceHubPaymentAttempt>(
            CommerceHubBoundaryMutationScope.IsPaymentAuthorized,
            "CommerceHub payment attempts cannot be created outside the CommerceHub payment boundary.",
            nameof(CommerceHubPaymentAttempt.PaymentId),
            nameof(CommerceHubPaymentAttempt.Provider),
            nameof(CommerceHubPaymentAttempt.AttemptNumber),
            nameof(CommerceHubPaymentAttempt.ProviderReference),
            nameof(CommerceHubPaymentAttempt.ProviderIntentId),
            nameof(CommerceHubPaymentAttempt.ProviderTransactionId),
            nameof(CommerceHubPaymentAttempt.Status),
            nameof(CommerceHubPaymentAttempt.FailureReason),
            nameof(CommerceHubPaymentAttempt.CompletedAtUtc));

        GuardBoundaryOwnedProperties<CommerceHubPaymentAttempt>(
            CommerceHubBoundaryMutationScope.IsPaymentAuthorized,
            "CommerceHub payment attempt records can only be changed by the CommerceHub payment boundary.",
            nameof(CommerceHubPaymentAttempt.ProviderIntentId),
            nameof(CommerceHubPaymentAttempt.ProviderTransactionId),
            nameof(CommerceHubPaymentAttempt.Status),
            nameof(CommerceHubPaymentAttempt.FailureReason),
            nameof(CommerceHubPaymentAttempt.CompletedAtUtc));

        GuardBoundaryOwnedEntityCreation<CommerceHubPaymentItem>(
            CommerceHubBoundaryMutationScope.IsPaymentAuthorized,
            "CommerceHub payment items cannot be created outside the CommerceHub payment boundary.");

        GuardBoundaryOwnedEntityCreation<CommerceHubPaymentLog>(
            CommerceHubBoundaryMutationScope.IsPaymentAuthorized,
            "CommerceHub payment provider logs cannot be created outside the CommerceHub payment boundary.");

        GuardBoundaryOwnedCreationState<CommerceHubPaymentLog>(
            CommerceHubBoundaryMutationScope.IsPaymentAuthorized,
            "CommerceHub payment provider logs cannot be created outside the CommerceHub payment boundary.",
            nameof(CommerceHubPaymentLog.PaymentId),
            nameof(CommerceHubPaymentLog.EventType),
            nameof(CommerceHubPaymentLog.ProviderEventId),
            nameof(CommerceHubPaymentLog.RawResponse));

        GuardBoundaryOwnedCreationState<CommerceHubPayment>(
            CommerceHubBoundaryMutationScope.IsRefundAuthorized,
            "CommerceHub payments cannot be created with refund state outside the CommerceHub refund boundary.",
            nameof(CommerceHubPayment.RefundedAmount),
            nameof(CommerceHubPayment.ProvisionalRefundedAmount));

        GuardBoundaryOwnedProperties<CommerceHubPayment>(
            CommerceHubBoundaryMutationScope.IsRefundAuthorized,
            "CommerceHub payment refund fields can only be changed by the CommerceHub refund boundary.",
            nameof(CommerceHubPayment.RefundedAmount),
            nameof(CommerceHubPayment.ProvisionalRefundedAmount));

        GuardBoundaryOwnedEntityCreation<CommerceHubRefund>(
            CommerceHubBoundaryMutationScope.IsRefundAuthorized,
            "CommerceHub refunds cannot be created outside the CommerceHub refund boundary.");

        GuardBoundaryOwnedCreationState<CommerceHubRefund>(
            CommerceHubBoundaryMutationScope.IsRefundAuthorized,
            "CommerceHub refunds cannot be created with refund lifecycle state outside the CommerceHub refund boundary.",
            nameof(CommerceHubRefund.PaymentId),
            nameof(CommerceHubRefund.Amount),
            nameof(CommerceHubRefund.CommissionReversalAmount),
            nameof(CommerceHubRefund.PromoDiscountReversalAmount),
            nameof(CommerceHubRefund.VendorEarningReversalAmount),
            nameof(CommerceHubRefund.Status),
            nameof(CommerceHubRefund.Reference),
            nameof(CommerceHubRefund.GatewayStatus),
            nameof(CommerceHubRefund.FailureReason),
            nameof(CommerceHubRefund.IsFullRefund),
            nameof(CommerceHubRefund.IsPostPayoutAdjustment),
            nameof(CommerceHubRefund.ProcessingStartedAtUtc),
            nameof(CommerceHubRefund.ProcessedAtUtc));

        GuardBoundaryOwnedProperties<CommerceHubRefund>(
            CommerceHubBoundaryMutationScope.IsRefundAuthorized,
            "CommerceHub refund records can only be changed by the CommerceHub refund boundary.",
            nameof(CommerceHubRefund.Status),
            nameof(CommerceHubRefund.PaymentId),
            nameof(CommerceHubRefund.Amount),
            nameof(CommerceHubRefund.CommissionReversalAmount),
            nameof(CommerceHubRefund.PromoDiscountReversalAmount),
            nameof(CommerceHubRefund.VendorEarningReversalAmount),
            nameof(CommerceHubRefund.Reference),
            nameof(CommerceHubRefund.GatewayStatus),
            nameof(CommerceHubRefund.FailureReason),
            nameof(CommerceHubRefund.IsFullRefund),
            nameof(CommerceHubRefund.IsPostPayoutAdjustment),
            nameof(CommerceHubRefund.ProcessingStartedAtUtc),
            nameof(CommerceHubRefund.ProcessedAtUtc));

        GuardBoundaryOwnedEntityCreation<CommerceHubPayout>(
            CommerceHubBoundaryMutationScope.IsPayoutAuthorized,
            "CommerceHub payouts cannot be created outside the CommerceHub payout boundary.");

        GuardBoundaryOwnedCreationState<CommerceHubPayout>(
            CommerceHubBoundaryMutationScope.IsPayoutAuthorized,
            "CommerceHub payouts cannot be created with payout lifecycle state outside the CommerceHub payout boundary.",
            nameof(CommerceHubPayout.TenantId),
            nameof(CommerceHubPayout.PayoutMethodId),
            nameof(CommerceHubPayout.Amount),
            nameof(CommerceHubPayout.Currency),
            nameof(CommerceHubPayout.Status),
            nameof(CommerceHubPayout.Method),
            nameof(CommerceHubPayout.DestinationValueSnapshot),
            nameof(CommerceHubPayout.Reference),
            nameof(CommerceHubPayout.ExternalReferenceId),
            nameof(CommerceHubPayout.BankTransactionId),
            nameof(CommerceHubPayout.ExternalTransferIdempotencyKey),
            nameof(CommerceHubPayout.ProcessedBy),
            nameof(CommerceHubPayout.FailureReason),
            nameof(CommerceHubPayout.ProcessedAtUtc),
            nameof(CommerceHubPayout.CompletedAtUtc),
            nameof(CommerceHubPayout.CompletionEvidenceFingerprint));

        GuardBoundaryOwnedProperties<CommerceHubPayout>(
            CommerceHubBoundaryMutationScope.IsPayoutAuthorized,
            "CommerceHub payout records can only be changed by the CommerceHub payout boundary.",
            nameof(CommerceHubPayout.Status),
            nameof(CommerceHubPayout.Reference),
            nameof(CommerceHubPayout.ExternalReferenceId),
            nameof(CommerceHubPayout.BankTransactionId),
            nameof(CommerceHubPayout.ExternalTransferIdempotencyKey),
            nameof(CommerceHubPayout.ExternalTransferHandoffToken),
            nameof(CommerceHubPayout.ExternalTransferHandoffCommittedAtUtc),
            nameof(CommerceHubPayout.ProcessedBy),
            nameof(CommerceHubPayout.FailureReason),
            nameof(CommerceHubPayout.ProcessedAtUtc),
            nameof(CommerceHubPayout.CompletedAtUtc),
            nameof(CommerceHubPayout.CompletionEvidenceFingerprint));

        GuardBoundaryOwnedEntityCreation<CommerceHubPayoutItem>(
            CommerceHubBoundaryMutationScope.IsPayoutAuthorized,
            "CommerceHub payout items cannot be created outside the CommerceHub payout boundary.");

        GuardBoundaryOwnedCreationState<CommerceHubPayoutItem>(
            CommerceHubBoundaryMutationScope.IsPayoutAuthorized,
            "CommerceHub payout items cannot be created outside the CommerceHub payout boundary.",
            nameof(CommerceHubPayoutItem.PayoutId),
            nameof(CommerceHubPayoutItem.EarningId),
            nameof(CommerceHubPayoutItem.Amount));

        GuardBoundaryOwnedEntityCreation<CommerceHubVendorEarning>(
            CommerceHubBoundaryMutationScope.IsFinancialWorkflowAuthorized ||
            CommerceHubBoundaryMutationScope.IsPayoutAuthorized ||
            CommerceHubBoundaryMutationScope.IsRefundAuthorized,
            "CommerceHub vendor earnings cannot be created outside the CommerceHub financial workflow, payout, or refund boundary.");

        GuardBoundaryOwnedCreationState<CommerceHubVendorEarning>(
            CommerceHubBoundaryMutationScope.IsFinancialWorkflowAuthorized ||
            CommerceHubBoundaryMutationScope.IsPayoutAuthorized ||
            CommerceHubBoundaryMutationScope.IsRefundAuthorized,
            "CommerceHub vendor earnings cannot be created with lifecycle state outside the CommerceHub financial workflow, payout, or refund boundary.",
            nameof(CommerceHubVendorEarning.SubOrderId),
            nameof(CommerceHubVendorEarning.PaymentId),
            nameof(CommerceHubVendorEarning.PayoutId),
            nameof(CommerceHubVendorEarning.RefundId),
            nameof(CommerceHubVendorEarning.OriginalEarningId),
            nameof(CommerceHubVendorEarning.TenantId),
            nameof(CommerceHubVendorEarning.Amount),
            nameof(CommerceHubVendorEarning.PaidOutAmount),
            nameof(CommerceHubVendorEarning.PayoutReservedAmount),
            nameof(CommerceHubVendorEarning.Status),
            nameof(CommerceHubVendorEarning.AvailableAtUtc),
            nameof(CommerceHubVendorEarning.PaidOutAtUtc),
            nameof(CommerceHubVendorEarning.ReversedAtUtc));

        GuardBoundaryOwnedProperties<CommerceHubVendorEarning>(
            CommerceHubBoundaryMutationScope.IsFinancialWorkflowAuthorized ||
            CommerceHubBoundaryMutationScope.IsPayoutAuthorized ||
            CommerceHubBoundaryMutationScope.IsRefundAuthorized,
            "CommerceHub vendor earning lifecycle fields can only be changed by the CommerceHub financial workflow, payout, or refund boundary.",
            nameof(CommerceHubVendorEarning.PayoutId),
            nameof(CommerceHubVendorEarning.RefundId),
            nameof(CommerceHubVendorEarning.PaidOutAmount),
            nameof(CommerceHubVendorEarning.Status),
            nameof(CommerceHubVendorEarning.AvailableAtUtc),
            nameof(CommerceHubVendorEarning.PaidOutAtUtc),
            nameof(CommerceHubVendorEarning.ReversedAtUtc));

        GuardBoundaryOwnedProperties<CommerceHubVendorEarning>(
            CommerceHubBoundaryMutationScope.IsPayoutAuthorized,
            "CommerceHub vendor earning payout reservation amount can only be changed by the CommerceHub payout boundary.",
            nameof(CommerceHubVendorEarning.PayoutReservedAmount));

    }

    private void GuardBoundaryOwnedEntityCreation<TEntity>(
        bool isAuthorized,
        string message)
        where TEntity : class
    {
        if (isAuthorized)
        {
            return;
        }

        if (ChangeTracker.Entries<TEntity>().Any(entry => entry.State == EntityState.Added))
        {
            throw new InvalidOperationException(message);
        }
    }

    private void GuardBoundaryOwnedCreationState<TEntity>(
        bool isAuthorized,
        string message,
        params string[] boundaryProperties)
        where TEntity : class
    {
        if (isAuthorized)
        {
            return;
        }

        var ownedProperties = new HashSet<string>(boundaryProperties, StringComparer.Ordinal);
        foreach (var entry in ChangeTracker.Entries<TEntity>())
        {
            if (entry.State != EntityState.Added)
            {
                continue;
            }

            var nonDefaultOwnedProperties = entry.Properties
                .Where(property => ownedProperties.Contains(property.Metadata.Name) &&
                                   !Equals(property.CurrentValue, GetClrDefaultValue(property.Metadata.ClrType)))
                .Select(property => property.Metadata.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            if (nonDefaultOwnedProperties.Count == 0)
            {
                continue;
            }

            throw new InvalidOperationException(
                $"{message} Blocked fields: {string.Join(", ", nonDefaultOwnedProperties)}.");
        }
    }

    private void GuardBoundaryOwnedProperties<TEntity>(
        bool isAuthorized,
        string message,
        params string[] boundaryProperties)
        where TEntity : class
    {
        if (isAuthorized)
        {
            return;
        }

        var ownedProperties = new HashSet<string>(boundaryProperties, StringComparer.Ordinal);
        foreach (var entry in ChangeTracker.Entries<TEntity>())
        {
            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            var modifiedOwnedProperties = entry.Properties
                .Where(property => property.IsModified && ownedProperties.Contains(property.Metadata.Name))
                .Select(property => property.Metadata.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            if (modifiedOwnedProperties.Count == 0)
            {
                continue;
            }

            throw new InvalidOperationException(
                $"{message} Blocked fields: {string.Join(", ", modifiedOwnedProperties)}.");
        }
    }

    private void GuardImmutableProperties<TEntity>(
        string message,
        params string[] immutableProperties)
        where TEntity : class
    {
        var immutablePropertySet = new HashSet<string>(immutableProperties, StringComparer.Ordinal);
        foreach (var entry in ChangeTracker.Entries<TEntity>())
        {
            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            var modifiedImmutableProperties = entry.Properties
                .Where(property =>
                    property.IsModified &&
                    immutablePropertySet.Contains(property.Metadata.Name) &&
                    !Equals(property.OriginalValue, property.CurrentValue))
                .Select(property => property.Metadata.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            if (modifiedImmutableProperties.Count == 0)
            {
                continue;
            }

            throw new InvalidOperationException(
                $"{message}. Blocked fields: {string.Join(", ", modifiedImmutableProperties)}.");
        }
    }

    private static object? GetClrDefaultValue(Type clrType)
    {
        var underlyingType = Nullable.GetUnderlyingType(clrType);
        if (underlyingType != null)
        {
            return null;
        }

        return clrType.IsValueType
            ? Activator.CreateInstance(clrType)
            : null;
    }

    private void IncrementRowVersionForNonPostgreSQL()
    {
        // PostgreSQL uses xmin system column for automatic row versioning
        // For other providers, manually increment RowVersion on updates
        if (Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            return;
        }

        foreach (var entry in ChangeTracker.Entries<StockSyncOutboxMessage>())
        {
            if (entry.State == EntityState.Modified)
            {
                // Increment RowVersion to enable optimistic concurrency detection
                entry.Entity.RowVersion++;
            }
        }

        foreach (var entry in ChangeTracker.Entries<MarketplaceProjectionOutboxMessage>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.RowVersion++;
            }
        }

        foreach (var entry in ChangeTracker.Entries<PaymentCancellationOutboxMessage>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.RowVersion++;
            }
        }

        foreach (var entry in ChangeTracker.Entries<AccountingOutboxMessage>())
        {
            if (entry.State == EntityState.Modified)
            {
                // Increment shadow xmin property to enable optimistic concurrency detection
                var currentValue = entry.Property("xmin").CurrentValue as uint? ?? 0u;
                entry.Property("xmin").CurrentValue = currentValue + 1;
            }
        }

        foreach (var entry in ChangeTracker.Entries<Order>())
        {
            if (entry.State == EntityState.Modified)
            {
                var currentValue = entry.Property("xmin").CurrentValue as uint? ?? 0u;
                entry.Property("xmin").CurrentValue = currentValue + 1;
            }
        }

        foreach (var entry in ChangeTracker.Entries<Coupon>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.RowVersion++;
            }
        }

        foreach (var entry in ChangeTracker.Entries<Promotion>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.RowVersion++;
            }
        }

        foreach (var entry in ChangeTracker.Entries<Product>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.RowVersion++;
            }
        }

        foreach (var entry in ChangeTracker.Entries<SmartOffer>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.RowVersion++;
            }
        }

        foreach (var entry in ChangeTracker.Entries<PosCart>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.RowVersion++;
            }
        }

        foreach (var entry in ChangeTracker.Entries<PosCartOutboxMessage>())
        {
            if (entry.State is EntityState.Modified)
            {
                entry.Entity.RowVersion++;
            }
        }
    }

    private void EnsureCanonicalIdentityLinks()
    {
        var plannedCanonicalIds = ChangeTracker.Entries<CanonicalIdentityLink>()
            .Where(entry => entry.State != EntityState.Deleted && entry.Entity.IsActive)
            .ToDictionary(
                entry => (entry.Entity.SubjectType, entry.Entity.SubjectId, entry.Entity.TenantId),
                entry => entry.Entity.CanonicalIdentityId);

        foreach (var entry in ChangeTracker.Entries<Customer>().ToList())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                EnsureCanonicalIdentityLink(
                    plannedCanonicalIds,
                    CanonicalIdentitySubjectType.PosCustomer,
                    entry.Entity.Id,
                    entry.Entity.TenantId,
                    "POS",
                    entry.Entity.Email);
            }
        }

        foreach (var entry in ChangeTracker.Entries<Tenant>().ToList())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                EnsureCanonicalIdentityLink(
                    plannedCanonicalIds,
                    CanonicalIdentitySubjectType.StoreTenant,
                    entry.Entity.Id,
                    entry.Entity.Id,
                    "POS",
                    entry.Entity.Slug ?? entry.Entity.ContactEmail ?? entry.Entity.Name);
            }
        }

        foreach (var entry in ChangeTracker.Entries<AffiliateAccount>().ToList())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                Guid? preferredCanonicalId = null;
                if (entry.Entity.UserId != Guid.Empty)
                {
                    preferredCanonicalId = EnsureCanonicalIdentityLink(
                        plannedCanonicalIds,
                        CanonicalIdentitySubjectType.PosUser,
                        entry.Entity.UserId,
                        NormalizeIdentityScope(entry.Entity.TenantId),
                        "POS",
                        entry.Entity.DisplayName);
                }

                EnsureCanonicalIdentityLink(
                    plannedCanonicalIds,
                    CanonicalIdentitySubjectType.AffiliateAccount,
                    entry.Entity.Id,
                    NormalizeIdentityScope(entry.Entity.TenantId),
                    "Affiliate",
                    entry.Entity.ReferralCode,
                    preferredCanonicalId);
            }
        }

        foreach (var entry in ChangeTracker.Entries<AffiliateUser>().ToList())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                var affiliateCanonicalId = EnsureCanonicalIdentityLink(
                    plannedCanonicalIds,
                    CanonicalIdentitySubjectType.AffiliateAccount,
                    entry.Entity.AffiliateAccountId,
                    NormalizeIdentityScope(entry.Entity.TenantId),
                    "Affiliate",
                    entry.Entity.Email);

                EnsureCanonicalIdentityLink(
                    plannedCanonicalIds,
                    CanonicalIdentitySubjectType.AffiliateUser,
                    entry.Entity.Id,
                    NormalizeIdentityScope(entry.Entity.TenantId),
                    "Affiliate",
                    entry.Entity.Email,
                    affiliateCanonicalId);
            }
        }

        foreach (var entry in ChangeTracker.Entries<Order>().ToList())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                EnsureOrderIdentityLinks(entry.Entity, plannedCanonicalIds);
            }
        }

        foreach (var entry in ChangeTracker.Entries<AffiliateAttribution>().ToList())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                EnsureCanonicalIdentityLink(
                    plannedCanonicalIds,
                    CanonicalIdentitySubjectType.AffiliateAccount,
                    entry.Entity.AffiliateAccountId,
                    tenantId: null,
                    "Affiliate",
                    entry.Entity.ReferralCode);
            }
        }

        // Batch load all OnlineCustomer tenant IDs needed for CommerceHubMarketplaceOrder entries
        var onlineCustomerIds = ChangeTracker.Entries<CommerceHubMarketplaceOrder>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .Where(entry => entry.Entity.OnlineCustomerId.HasValue && entry.Entity.OnlineCustomerId.Value != Guid.Empty)
            .Select(entry => entry.Entity.OnlineCustomerId!.Value)
            .Distinct()
            .ToList();

        var onlineCustomerTenantCache = new Dictionary<Guid, Guid?>();

        // First, seed cache from tracked OnlineCustomer entries (takes precedence over DB)
        // This ensures newly created/modified OnlineCustomers in the same UnitOfWork are included
        foreach (var entry in ChangeTracker.Entries<OnlineCustomer>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified && entry.Entity.Id != Guid.Empty)
            {
                onlineCustomerTenantCache[entry.Entity.Id] = entry.Entity.TenantId;
            }
        }

        // Then, load remaining OnlineCustomer tenant IDs from database (for those not in ChangeTracker)
        var onlineCustomerIdsToLoad = onlineCustomerIds
            .Where(id => !onlineCustomerTenantCache.ContainsKey(id))
            .ToList();

        if (onlineCustomerIdsToLoad.Any())
        {
            var tenantLookups = OnlineCustomers
                .IgnoreQueryFilters()
                .Where(oc => onlineCustomerIdsToLoad.Contains(oc.Id))
                .Select(oc => new { oc.Id, TenantId = (Guid?)oc.TenantId })
                .ToList();

            foreach (var lookup in tenantLookups)
            {
                onlineCustomerTenantCache[lookup.Id] = lookup.TenantId;
            }
        }

        foreach (var entry in ChangeTracker.Entries<CommerceHubMarketplaceOrder>().ToList())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                // Resolve OnlineCustomer tenant from cache
                Guid? onlineCustomerTenantId = null;
                if (entry.Entity.OnlineCustomerId.HasValue && entry.Entity.OnlineCustomerId.Value != Guid.Empty)
                {
                    if (!onlineCustomerTenantCache.TryGetValue(entry.Entity.OnlineCustomerId.Value, out onlineCustomerTenantId) ||
                        !onlineCustomerTenantId.HasValue ||
                        onlineCustomerTenantId.Value == Guid.Empty)
                    {
                        throw new InvalidOperationException(
                            $"Cannot resolve tenant for OnlineCustomer {entry.Entity.OnlineCustomerId.Value} " +
                            $"in CommerceHubMarketplaceOrder {entry.Entity.Id}. " +
                            "OnlineCustomer must have a valid tenant to prevent global-scoped canonical identity links.");
                    }
                }

                var buyerPreferredCanonicalId = entry.Entity.OnlineCustomerId.HasValue &&
                                                entry.Entity.OnlineCustomerId.Value != Guid.Empty
                    ? TryResolveCanonicalIdentity(
                        plannedCanonicalIds,
                        CanonicalIdentitySubjectType.OnlineCustomer,
                        entry.Entity.OnlineCustomerId.Value,
                        onlineCustomerTenantId)
                    : null;

                Guid? buyerCanonicalId = null;
                if (entry.Entity.CustomerId != Guid.Empty)
                {
                    buyerCanonicalId = EnsureCanonicalIdentityLink(
                        plannedCanonicalIds,
                        CanonicalIdentitySubjectType.CommerceHubCustomer,
                        entry.Entity.CustomerId,
                        tenantId: null,
                        "CommerceHub",
                        entry.Entity.FullName,
                        buyerPreferredCanonicalId);
                }

                if (entry.Entity.OnlineCustomerId.HasValue && entry.Entity.OnlineCustomerId.Value != Guid.Empty)
                {
                    // HARDENING: onlineCustomerTenantId is guaranteed non-null here due to validation above
                    EnsureCanonicalIdentityLink(
                        plannedCanonicalIds,
                        CanonicalIdentitySubjectType.OnlineCustomer,
                        entry.Entity.OnlineCustomerId.Value,
                        onlineCustomerTenantId,
                        "Storefront",
                        entry.Entity.FullName,
                        buyerCanonicalId);
                }
            }
        }

        foreach (var entry in ChangeTracker.Entries<CommerceHubCustomer>().ToList())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                EnsureCanonicalIdentityLink(
                    plannedCanonicalIds,
                    CanonicalIdentitySubjectType.CommerceHubCustomer,
                    entry.Entity.Id,
                    tenantId: null,
                    "CommerceHub",
                    entry.Entity.Email);
            }
        }

        foreach (var entry in ChangeTracker.Entries<OnlineCustomer>().ToList())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                EnsureCanonicalIdentityLink(
                    plannedCanonicalIds,
                    CanonicalIdentitySubjectType.OnlineCustomer,
                    entry.Entity.Id,
                    entry.Entity.TenantId,
                    "Storefront",
                    entry.Entity.Email);
            }
        }

        foreach (var entry in ChangeTracker.Entries<CommerceHubSubOrder>().ToList())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                EnsureCanonicalIdentityLink(
                    plannedCanonicalIds,
                    CanonicalIdentitySubjectType.StoreTenant,
                    entry.Entity.StoreId,
                    entry.Entity.StoreId,
                    "CommerceHub",
                    entry.Entity.StoreName);
            }
        }

        foreach (var entry in ChangeTracker.Entries<CommerceHubPayout>().ToList())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                EnsureCanonicalIdentityLink(
                    plannedCanonicalIds,
                    CanonicalIdentitySubjectType.StoreTenant,
                    entry.Entity.TenantId,
                    entry.Entity.TenantId,
                    "CommerceHub",
                    entry.Entity.Reference);
            }
        }
    }

    private async Task EnsureCanonicalIdentityLinksAsync(CancellationToken cancellationToken)
    {
        var plannedCanonicalIds = ChangeTracker.Entries<CanonicalIdentityLink>()
            .Where(entry => entry.State != EntityState.Deleted && entry.Entity.IsActive)
            .ToDictionary(
                entry => (entry.Entity.SubjectType, entry.Entity.SubjectId, entry.Entity.TenantId),
                entry => entry.Entity.CanonicalIdentityId);

        foreach (var entry in ChangeTracker.Entries<Customer>().ToList())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                EnsureCanonicalIdentityLink(
                    plannedCanonicalIds,
                    CanonicalIdentitySubjectType.PosCustomer,
                    entry.Entity.Id,
                    entry.Entity.TenantId,
                    "POS",
                    entry.Entity.Email);
            }
        }

        foreach (var entry in ChangeTracker.Entries<Tenant>().ToList())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                EnsureCanonicalIdentityLink(
                    plannedCanonicalIds,
                    CanonicalIdentitySubjectType.StoreTenant,
                    entry.Entity.Id,
                    entry.Entity.Id,
                    "POS",
                    entry.Entity.Slug ?? entry.Entity.ContactEmail ?? entry.Entity.Name);
            }
        }

        foreach (var entry in ChangeTracker.Entries<AffiliateAccount>().ToList())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                Guid? preferredCanonicalId = null;
                if (entry.Entity.UserId != Guid.Empty)
                {
                    preferredCanonicalId = EnsureCanonicalIdentityLink(
                        plannedCanonicalIds,
                        CanonicalIdentitySubjectType.PosUser,
                        entry.Entity.UserId,
                        NormalizeIdentityScope(entry.Entity.TenantId),
                        "POS",
                        entry.Entity.DisplayName);
                }

                EnsureCanonicalIdentityLink(
                    plannedCanonicalIds,
                    CanonicalIdentitySubjectType.AffiliateAccount,
                    entry.Entity.Id,
                    NormalizeIdentityScope(entry.Entity.TenantId),
                    "Affiliate",
                    entry.Entity.ReferralCode,
                    preferredCanonicalId);
            }
        }

        foreach (var entry in ChangeTracker.Entries<AffiliateUser>().ToList())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                var affiliateCanonicalId = EnsureCanonicalIdentityLink(
                    plannedCanonicalIds,
                    CanonicalIdentitySubjectType.AffiliateAccount,
                    entry.Entity.AffiliateAccountId,
                    NormalizeIdentityScope(entry.Entity.TenantId),
                    "Affiliate",
                    entry.Entity.Email);

                EnsureCanonicalIdentityLink(
                    plannedCanonicalIds,
                    CanonicalIdentitySubjectType.AffiliateUser,
                    entry.Entity.Id,
                    NormalizeIdentityScope(entry.Entity.TenantId),
                    "Affiliate",
                    entry.Entity.Email,
                    affiliateCanonicalId);
            }
        }

        foreach (var entry in ChangeTracker.Entries<Order>().ToList())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                EnsureOrderIdentityLinks(entry.Entity, plannedCanonicalIds);
            }
        }

        foreach (var entry in ChangeTracker.Entries<AffiliateAttribution>().ToList())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                EnsureCanonicalIdentityLink(
                    plannedCanonicalIds,
                    CanonicalIdentitySubjectType.AffiliateAccount,
                    entry.Entity.AffiliateAccountId,
                    tenantId: null,
                    "Affiliate",
                    entry.Entity.ReferralCode);
            }
        }

        // Batch load all OnlineCustomer tenant IDs needed for CommerceHubMarketplaceOrder entries
        var onlineCustomerIds = ChangeTracker.Entries<CommerceHubMarketplaceOrder>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .Where(entry => entry.Entity.OnlineCustomerId.HasValue && entry.Entity.OnlineCustomerId.Value != Guid.Empty)
            .Select(entry => entry.Entity.OnlineCustomerId!.Value)
            .Distinct()
            .ToList();

        var onlineCustomerTenantCache = new Dictionary<Guid, Guid?>();

        // First, seed cache from tracked OnlineCustomer entries (takes precedence over DB)
        // This ensures newly created/modified OnlineCustomers in the same UnitOfWork are included
        foreach (var entry in ChangeTracker.Entries<OnlineCustomer>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified && entry.Entity.Id != Guid.Empty)
            {
                onlineCustomerTenantCache[entry.Entity.Id] = entry.Entity.TenantId;
            }
        }

        // Then, load remaining OnlineCustomer tenant IDs from database (for those not in ChangeTracker)
        var onlineCustomerIdsToLoad = onlineCustomerIds
            .Where(id => !onlineCustomerTenantCache.ContainsKey(id))
            .ToList();

        if (onlineCustomerIdsToLoad.Any())
        {
            var tenantLookups = await OnlineCustomers
                .IgnoreQueryFilters()
                .Where(oc => onlineCustomerIdsToLoad.Contains(oc.Id))
                .Select(oc => new { oc.Id, TenantId = (Guid?)oc.TenantId })
                .ToListAsync(cancellationToken);

            foreach (var lookup in tenantLookups)
            {
                onlineCustomerTenantCache[lookup.Id] = lookup.TenantId;
            }
        }

        foreach (var entry in ChangeTracker.Entries<CommerceHubMarketplaceOrder>().ToList())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                // Resolve OnlineCustomer tenant from cache
                Guid? onlineCustomerTenantId = null;
                if (entry.Entity.OnlineCustomerId.HasValue && entry.Entity.OnlineCustomerId.Value != Guid.Empty)
                {
                    if (!onlineCustomerTenantCache.TryGetValue(entry.Entity.OnlineCustomerId.Value, out onlineCustomerTenantId) ||
                        !onlineCustomerTenantId.HasValue ||
                        onlineCustomerTenantId.Value == Guid.Empty)
                    {
                        throw new InvalidOperationException(
                            $"Cannot resolve tenant for OnlineCustomer {entry.Entity.OnlineCustomerId.Value} " +
                            $"in CommerceHubMarketplaceOrder {entry.Entity.Id}. " +
                            "OnlineCustomer must have a valid tenant to prevent global-scoped canonical identity links.");
                    }
                }

                var buyerPreferredCanonicalId = entry.Entity.OnlineCustomerId.HasValue &&
                                                entry.Entity.OnlineCustomerId.Value != Guid.Empty
                    ? TryResolveCanonicalIdentity(
                        plannedCanonicalIds,
                        CanonicalIdentitySubjectType.OnlineCustomer,
                        entry.Entity.OnlineCustomerId.Value,
                        onlineCustomerTenantId)
                    : null;

                Guid? buyerCanonicalId = null;
                if (entry.Entity.CustomerId != Guid.Empty)
                {
                    buyerCanonicalId = EnsureCanonicalIdentityLink(
                        plannedCanonicalIds,
                        CanonicalIdentitySubjectType.CommerceHubCustomer,
                        entry.Entity.CustomerId,
                        tenantId: null,
                        "CommerceHub",
                        entry.Entity.FullName,
                        buyerPreferredCanonicalId);
                }

                if (entry.Entity.OnlineCustomerId.HasValue && entry.Entity.OnlineCustomerId.Value != Guid.Empty)
                {
                    // HARDENING: onlineCustomerTenantId is guaranteed non-null here due to validation above
                    EnsureCanonicalIdentityLink(
                        plannedCanonicalIds,
                        CanonicalIdentitySubjectType.OnlineCustomer,
                        entry.Entity.OnlineCustomerId.Value,
                        onlineCustomerTenantId,
                        "Storefront",
                        entry.Entity.FullName,
                        buyerCanonicalId);
                }
            }
        }

        foreach (var entry in ChangeTracker.Entries<CommerceHubCustomer>().ToList())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                EnsureCanonicalIdentityLink(
                    plannedCanonicalIds,
                    CanonicalIdentitySubjectType.CommerceHubCustomer,
                    entry.Entity.Id,
                    tenantId: null,
                    "CommerceHub",
                    entry.Entity.Email);
            }
        }

        foreach (var entry in ChangeTracker.Entries<OnlineCustomer>().ToList())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                EnsureCanonicalIdentityLink(
                    plannedCanonicalIds,
                    CanonicalIdentitySubjectType.OnlineCustomer,
                    entry.Entity.Id,
                    entry.Entity.TenantId,
                    "Storefront",
                    entry.Entity.Email);
            }
        }

        foreach (var entry in ChangeTracker.Entries<CommerceHubSubOrder>().ToList())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                EnsureCanonicalIdentityLink(
                    plannedCanonicalIds,
                    CanonicalIdentitySubjectType.StoreTenant,
                    entry.Entity.StoreId,
                    entry.Entity.StoreId,
                    "CommerceHub",
                    entry.Entity.StoreName);
            }
        }

        foreach (var entry in ChangeTracker.Entries<CommerceHubPayout>().ToList())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                EnsureCanonicalIdentityLink(
                    plannedCanonicalIds,
                    CanonicalIdentitySubjectType.StoreTenant,
                    entry.Entity.TenantId,
                    entry.Entity.TenantId,
                    "CommerceHub",
                    entry.Entity.Reference);
            }
        }
    }

    private Guid? TryResolveCanonicalIdentity(
        Dictionary<(CanonicalIdentitySubjectType SubjectType, Guid SubjectId, Guid? TenantId), Guid> plannedCanonicalIds,
        CanonicalIdentitySubjectType subjectType,
        Guid subjectId,
        Guid? tenantId = null)
    {
        if (subjectId == Guid.Empty)
        {
            return null;
        }

        var normalizedTenantId = NormalizeIdentityScope(tenantId);
        var key = (subjectType, subjectId, normalizedTenantId);
        
        if (plannedCanonicalIds.TryGetValue(key, out var plannedCanonicalId))
        {
            return plannedCanonicalId;
        }

        var query = CanonicalIdentityLinks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(link =>
                link.SubjectType == subjectType &&
                link.SubjectId == subjectId &&
                !link.IsDeleted &&
                link.RevokedAtUtc == null);

        // Apply tenant filter: match exact tenant or global (null)
        if (normalizedTenantId.HasValue)
        {
            query = query.Where(link => link.TenantId == normalizedTenantId.Value);
        }
        else
        {
            query = query.Where(link => link.TenantId == null);
        }

        return query
            .Select(link => (Guid?)link.CanonicalIdentityId)
            .FirstOrDefault();
    }

    private void EnsureOrderIdentityLinks(
        Order order,
        Dictionary<(CanonicalIdentitySubjectType SubjectType, Guid SubjectId, Guid? TenantId), Guid> plannedCanonicalIds)
    {
        Guid? currentCanonicalId = null;

        if (order.CustomerId.HasValue)
        {
            currentCanonicalId = EnsureCanonicalIdentityLink(
                plannedCanonicalIds,
                CanonicalIdentitySubjectType.PosCustomer,
                order.CustomerId.Value,
                order.TenantId,
                "POS",
                order.GuestEmail ?? order.GuestPhone);
        }

        if (order.OnlineCustomerId.HasValue)
        {
            currentCanonicalId = EnsureCanonicalIdentityLink(
                plannedCanonicalIds,
                CanonicalIdentitySubjectType.OnlineCustomer,
                order.OnlineCustomerId.Value,
                order.TenantId,
                "Storefront",
                order.GuestEmail ?? order.GuestPhone,
                currentCanonicalId);
        }

        if (order.AffiliateAccountId.HasValue)
        {
            EnsureCanonicalIdentityLink(
                plannedCanonicalIds,
                CanonicalIdentitySubjectType.AffiliateAccount,
                order.AffiliateAccountId.Value,
                tenantId: null,
                "Affiliate",
                order.AffiliateReferralCode,
                currentCanonicalId);
        }
    }

    private Guid EnsureCanonicalIdentityLink(
        Dictionary<(CanonicalIdentitySubjectType SubjectType, Guid SubjectId, Guid? TenantId), Guid> plannedCanonicalIds,
        CanonicalIdentitySubjectType subjectType,
        Guid subjectId,
        Guid? tenantId,
        string sourceSystem,
        string? displayKey,
        Guid? preferredCanonicalIdentityId = null)
    {
        if (subjectId == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"Cannot create canonical identity link for empty {subjectType} subject id.");
        }

        var normalizedTenantId = NormalizeIdentityScope(tenantId);
        var key = (subjectType, subjectId, normalizedTenantId);
        
        if (plannedCanonicalIds.TryGetValue(key, out var plannedCanonicalId))
        {
            EnforcePreferredCanonicalIdentity(subjectType, subjectId, plannedCanonicalId, preferredCanonicalIdentityId);
            return plannedCanonicalId;
        }

        var query = CanonicalIdentityLinks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(link =>
                link.SubjectType == subjectType &&
                link.SubjectId == subjectId &&
                !link.IsDeleted &&
                link.RevokedAtUtc == null);

        // Apply tenant filter: match exact tenant or global (null)
        if (normalizedTenantId.HasValue)
        {
            query = query.Where(link => link.TenantId == normalizedTenantId.Value);
        }
        else
        {
            query = query.Where(link => link.TenantId == null);
        }

        var existingLink = query.FirstOrDefault();

        if (existingLink != null)
        {
            EnforcePreferredCanonicalIdentity(subjectType, subjectId, existingLink.CanonicalIdentityId, preferredCanonicalIdentityId);
            plannedCanonicalIds[key] = existingLink.CanonicalIdentityId;
            return existingLink.CanonicalIdentityId;
        }

        var canonicalIdentityId = preferredCanonicalIdentityId ?? Guid.NewGuid();
        CanonicalIdentityLinks.Add(CanonicalIdentityLink.Create(
            canonicalIdentityId,
            subjectType,
            subjectId,
            normalizedTenantId,
            sourceSystem,
            displayKey));

        plannedCanonicalIds[key] = canonicalIdentityId;
        return canonicalIdentityId;
    }

    private static Guid? NormalizeIdentityScope(Guid? tenantId) =>
        tenantId.HasValue && tenantId.Value != Guid.Empty ? tenantId : null;

    private static void EnforcePreferredCanonicalIdentity(
        CanonicalIdentitySubjectType subjectType,
        Guid subjectId,
        Guid actualCanonicalIdentityId,
        Guid? preferredCanonicalIdentityId)
    {
        if (!preferredCanonicalIdentityId.HasValue ||
            preferredCanonicalIdentityId.Value == actualCanonicalIdentityId)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Canonical identity conflict for {subjectType} subject {subjectId}. " +
            $"Existing canonical identity {actualCanonicalIdentityId} does not match required canonical identity {preferredCanonicalIdentityId.Value}.");
    }

    private void IncrementMarketplaceProjectionMutationVersions()
    {
        IncrementProjectionMutationVersion(ChangeTracker.Entries<Product>());
        IncrementProjectionMutationVersion(ChangeTracker.Entries<ProductVariant>());
        IncrementProjectionMutationVersion(ChangeTracker.Entries<ProductVariantStock>());
        IncrementProjectionMutationVersion(ChangeTracker.Entries<Order>());
        IncrementProjectionMutationVersion(ChangeTracker.Entries<Promotion>());
        IncrementCommerceHubMarketplaceOrderProjectionMutationVersions();
        IncrementProjectionMutationVersion(ChangeTracker.Entries<CommerceHubSubOrder>());
    }

    private void ResetCommerceHubProjectionMarkersForBusinessMutations()
    {
        var trackedOrdersById = ChangeTracker
            .Entries<CommerceHubMarketplaceOrder>()
            .ToDictionary(entry => entry.Entity.Id, entry => entry.Entity);

        foreach (var entry in ChangeTracker.Entries<CommerceHubMarketplaceOrder>())
        {
            if (entry.State != EntityState.Modified || IsCommerceHubMarketplaceProjectionMarkerOnly(entry))
            {
                continue;
            }

            MarkCommerceHubMarketplaceOrderProjectionDirty(entry.Entity);
        }

        foreach (var entry in ChangeTracker.Entries<CommerceHubSubOrder>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            if (entry.Entity.MarketplaceOrder != null)
            {
                MarkCommerceHubMarketplaceOrderProjectionDirty(entry.Entity.MarketplaceOrder);
                continue;
            }

            if (trackedOrdersById.TryGetValue(entry.Entity.MarketplaceOrderId, out var marketplaceOrder))
            {
                MarkCommerceHubMarketplaceOrderProjectionDirty(marketplaceOrder);
                continue;
            }

            // Parent not tracked; load or attach it
            var parentOrder = CommerceHubMarketplaceOrders.Local.FirstOrDefault(o => o.Id == entry.Entity.MarketplaceOrderId)
                ?? Find<CommerceHubMarketplaceOrder>(entry.Entity.MarketplaceOrderId);

            if (parentOrder == null)
            {
                // Attach a key-only entity to enable projection marker update
                _logger?.LogWarning(
                    "CommerceHubMarketplaceOrder with Id {MarketplaceOrderId} not found for CommerceHubSubOrder {SubOrderId}. " +
                    "Attaching key-only entity to mark projection dirty. This indicates a potential FK integrity issue.",
                    entry.Entity.MarketplaceOrderId,
                    entry.Entity.Id);
                
                parentOrder = new CommerceHubMarketplaceOrder { Id = entry.Entity.MarketplaceOrderId };
                Attach(parentOrder);
            }

            MarkCommerceHubMarketplaceOrderProjectionDirty(parentOrder);
        }

        foreach (var entry in ChangeTracker.Entries<CommerceHubSubOrderItem>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            var marketplaceOrder = entry.Entity.SubOrder?.MarketplaceOrder;
            if (marketplaceOrder != null)
            {
                MarkCommerceHubMarketplaceOrderProjectionDirty(marketplaceOrder);
                continue;
            }

            // Resolve MarketplaceOrderId via SubOrder
            var subOrderId = entry.Entity.SubOrderId;
            var subOrderEntry = ChangeTracker.Entries<CommerceHubSubOrder>()
                .FirstOrDefault(e => e.Entity.Id == subOrderId);

            Guid marketplaceOrderId;
            if (subOrderEntry != null)
            {
                marketplaceOrderId = subOrderEntry.Entity.MarketplaceOrderId;
            }
            else
            {
                // SubOrder not tracked; load it to get MarketplaceOrderId
                var subOrder = Find<CommerceHubSubOrder>(subOrderId);
                if (subOrder == null)
                {
                    _logger?.LogWarning(
                        "CommerceHubSubOrder not found for SubOrderId={SubOrderId} during CommerceHubSubOrderItem processing. " +
                        "SubOrderItemId={SubOrderItemId}, EntityState={EntityState}",
                        subOrderId,
                        entry.Entity.Id,
                        entry.State);
                    continue; // Cannot resolve parent
                }
                marketplaceOrderId = subOrder.MarketplaceOrderId;
            }

            // Load or attach the parent MarketplaceOrder
            if (trackedOrdersById.TryGetValue(marketplaceOrderId, out var trackedOrder))
            {
                MarkCommerceHubMarketplaceOrderProjectionDirty(trackedOrder);
                continue;
            }

            var parentOrder = CommerceHubMarketplaceOrders.Local.FirstOrDefault(o => o.Id == marketplaceOrderId)
                ?? Find<CommerceHubMarketplaceOrder>(marketplaceOrderId);

            if (parentOrder == null)
            {
                // Attach a key-only entity to enable projection marker update
                parentOrder = new CommerceHubMarketplaceOrder { Id = marketplaceOrderId };
                Attach(parentOrder);
            }

            MarkCommerceHubMarketplaceOrderProjectionDirty(parentOrder);
        }
    }

    private static bool IsCommerceHubMarketplaceProjectionMarkerOnly(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<CommerceHubMarketplaceOrder> entry)
    {
        var modifiedProperties = entry.Properties
            .Where(property => property.IsModified)
            .Select(property => property.Metadata.Name)
            .ToList();

        return modifiedProperties.Count > 0 &&
            modifiedProperties.All(propertyName =>
                propertyName is nameof(CommerceHubMarketplaceOrder.IsProjected)
                    or nameof(CommerceHubMarketplaceOrder.ProjectedAtUtc)
                    or nameof(CommerceHubMarketplaceOrder.LastProjectionAttemptAt)
                    or nameof(CommerceHubMarketplaceOrder.ProjectionAttemptCount)
                    or nameof(CommerceHubMarketplaceOrder.IsProjectionTerminalFailure)
                    or nameof(BaseEntity.UpdatedAt));
    }

    private static void MarkCommerceHubMarketplaceOrderProjectionDirty(CommerceHubMarketplaceOrder order)
    {
        order.IsProjected = false;
        order.ProjectedAtUtc = null;
    }

    private void IncrementCommerceHubMarketplaceOrderProjectionMutationVersions()
    {
        foreach (var entry in ChangeTracker.Entries<CommerceHubMarketplaceOrder>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.ProjectionMutationVersion <= 0)
                {
                    entry.Entity.AdvanceProjectionMutationVersion(1L);
                }

                continue;
            }

            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            var projectionMarkerOnly = IsCommerceHubMarketplaceProjectionMarkerOnly(entry);
            if (projectionMarkerOnly)
            {
                continue;
            }

            var versionProperty = entry.Property(nameof(IProjectionVersioned.ProjectionMutationVersion));
            if (versionProperty.IsModified)
            {
                var originalVersion = versionProperty.OriginalValue is long original ? original : 0L;
                if (entry.Entity.ProjectionMutationVersion <= originalVersion)
                {
                    throw new InvalidOperationException(
                        "ProjectionMutationVersion must increase for CommerceHubMarketplaceOrder. " +
                        $"Original={originalVersion}; Current={entry.Entity.ProjectionMutationVersion}.");
                }

                continue;
            }

            var current = entry.Entity.ProjectionMutationVersion;
            entry.Entity.AdvanceProjectionMutationVersion(current + 1L);
        }
    }

    private static void IncrementProjectionMutationVersion<TEntity>(
        IEnumerable<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>> entries)
        where TEntity : class, IProjectionVersioned
    {
        foreach (var entry in entries)
        {
            var entity = entry.Entity;
            
            if (entry.State == EntityState.Added)
            {
                if (entity.ProjectionMutationVersion <= 0)
                {
                    entity.AdvanceProjectionMutationVersion(1L);
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                var versionProperty = entry.Property(nameof(IProjectionVersioned.ProjectionMutationVersion));
                if (versionProperty.IsModified)
                {
                    var originalVersion = versionProperty.OriginalValue is long original ? original : 0L;
                    if (entity.ProjectionMutationVersion <= originalVersion)
                    {
                        throw new InvalidOperationException(
                            $"ProjectionMutationVersion must increase for {typeof(TEntity).Name}. " +
                            $"Original={originalVersion}; Current={entity.ProjectionMutationVersion}.");
                    }

                    continue;
                }

                var current = entity.ProjectionMutationVersion;
                entity.AdvanceProjectionMutationVersion(current + 1L);
            }
        }
    }

    private void NormalizeCouponCodes()
    {
        foreach (var entry in ChangeTracker.Entries<Coupon>())
        {
            if (entry.State is not EntityState.Added and not EntityState.Modified)
            {
                continue;
            }

            entry.Entity.SetCode(entry.Entity.Code);
        }
    }

    /// <summary>
    /// Provider-conditional optimistic-concurrency mapping for User.RowVersion.
    /// Production (Npgsql) uses the xmin system column following the
    /// StoreConfig precedent. Other providers cannot generate xmin, so they
    /// use a regular client-bumped concurrency column with identical
    /// check semantics (see BumpPosUserRowVersion). Migrations and the model
    /// snapshot are Npgsql-only and always record the xmin mapping.
    /// </summary>
    private void ConfigurePosUserRowVersion(ModelBuilder modelBuilder)
    {
        var rowVersion = modelBuilder.Entity<User>().Property(u => u.RowVersion);
        if (Database.IsNpgsql())
        {
            rowVersion.HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        }
        else
        {
            rowVersion.HasColumnName("RowVersion").IsConcurrencyToken().ValueGeneratedNever();
        }
    }

    /// <summary>
    /// Client-side optimistic-concurrency bump for non-PostgreSQL providers.
    /// On Npgsql the database generates xmin and this is a no-op.
    /// </summary>
    private void BumpPosUserRowVersion()
    {
        if (Database.IsNpgsql())
        {
            return;
        }

        foreach (var entry in ChangeTracker.Entries<User>())
        {
            if (entry.State is not EntityState.Added and not EntityState.Modified)
            {
                continue;
            }

            entry.Entity.RowVersion++;
        }
    }

    /// <summary>
    /// Server-authoritative safety net for POS identity normalization.
    /// Every User insert or Username/Email change gets deterministic
    /// normalized keys from the single PosIdentityNormalizer implementation,
    /// so login lookups and global uniqueness hold on every write path
    /// (registration, invitation, seeds). Service layers normalize
    /// explicitly before queries; this guarantees persistence.
    /// </summary>
    private void NormalizePosUserIdentities()
    {
        foreach (var entry in ChangeTracker.Entries<User>())
        {
            if (entry.State is not EntityState.Added and not EntityState.Modified)
            {
                continue;
            }

            var user = entry.Entity;
            var usernameChanged = entry.State == EntityState.Added ||
                entry.Property(nameof(User.Username)).IsModified;
            var emailChanged = entry.State == EntityState.Added ||
                entry.Property(nameof(User.Email)).IsModified;

            if ((usernameChanged || string.IsNullOrWhiteSpace(user.NormalizedUsername)) &&
                !string.IsNullOrWhiteSpace(user.Username))
            {
                user.NormalizedUsername = PointOfSale.Core.Security.PosIdentityNormalizer.NormalizeUsername(user.Username);
            }

            if ((emailChanged || string.IsNullOrWhiteSpace(user.NormalizedEmail)) &&
                !string.IsNullOrWhiteSpace(user.Email))
            {
                user.NormalizedEmail = PointOfSale.Core.Security.PosIdentityNormalizer.NormalizeEmail(user.Email);
            }
        }
    }

    private void GuardPurchaseInvoiceFinancialIntegrity()
    {
        foreach (var entry in ChangeTracker.Entries<PurchaseInvoice>())
        {
            if (entry.State is not EntityState.Added and not EntityState.Modified)
            {
                continue;
            }

            // Validate raw value before rounding to catch tiny negative values
            if (entry.Entity.PaidAmount < 0m)
            {
                throw new InvalidOperationException("Paid amount cannot be negative.");
            }

            // Validate raw value before rounding to catch tiny negative values
            if (entry.Entity.TotalAmount < 0m)
            {
                throw new InvalidOperationException("Total amount cannot be negative.");
            }

            var roundedPaidAmount = RoundCurrency(entry.Entity.PaidAmount);
            var roundedTotalAmount = RoundCurrency(entry.Entity.TotalAmount);

            if (roundedPaidAmount > roundedTotalAmount)
            {
                throw new InvalidOperationException("Paid amount cannot exceed total amount.");
            }
        }
    }

    private void GuardPosTenantForeignKeyCoOwnership()
    {
        var guard = new PosTenantForeignKeyCoOwnershipGuard(this);
        guard.Collect();
        guard.Load();
        guard.Validate();
    }

    private async Task GuardPosTenantForeignKeyCoOwnershipAsync(CancellationToken cancellationToken)
    {
        var guard = new PosTenantForeignKeyCoOwnershipGuard(this);
        guard.Collect();
        await guard.LoadAsync(cancellationToken);
        guard.Validate();
    }

    private sealed class PosTenantForeignKeyCoOwnershipGuard
    {
        private readonly ApplicationDbContext _context;
        private readonly List<TenantEdge> _tenantEdges = new();
        private readonly List<VariantEdge> _variantEdges = new();
        private readonly List<SalesInvoiceItemEdge> _salesInvoiceItemEdges = new();
        private readonly List<PurchaseInvoiceItemEdge> _purchaseInvoiceItemEdges = new();
        private readonly HashSet<Guid> _customerIds = new();
        private readonly HashSet<Guid> _supplierIds = new();
        private readonly HashSet<Guid> _warehouseIds = new();
        private readonly HashSet<Guid> _productIds = new();
        private readonly HashSet<Guid> _variantIds = new();
        private readonly HashSet<Guid> _unitIds = new();
        private readonly HashSet<Guid> _salesRepresentativeIds = new();
        private readonly HashSet<Guid> _financialAccountIds = new();
        private readonly HashSet<Guid> _salesInvoiceIds = new();
        private readonly HashSet<Guid> _purchaseInvoiceIds = new();

        private Dictionary<Guid, Guid> _customers = new();
        private Dictionary<Guid, Guid> _suppliers = new();
        private Dictionary<Guid, Guid> _warehouses = new();
        private Dictionary<Guid, Guid> _products = new();
        private Dictionary<Guid, Guid> _units = new();
        private Dictionary<Guid, Guid> _salesRepresentatives = new();
        private Dictionary<Guid, Guid> _financialAccounts = new();
        private Dictionary<Guid, Guid> _salesInvoices = new();
        private Dictionary<Guid, Guid> _purchaseInvoices = new();
        private Dictionary<Guid, VariantOwnership> _variants = new();

        public PosTenantForeignKeyCoOwnershipGuard(ApplicationDbContext context)
        {
            _context = context;
        }

        public void Collect()
        {
            foreach (var entry in _context.ChangeTracker.Entries<SalesInvoice>().Where(IsPersistedMutation))
            {
                var invoice = entry.Entity;
                RememberSalesInvoice(invoice.Id);
                RememberWarehouse(invoice.WarehouseId);
                _tenantEdges.Add(new TenantEdge(invoice.TenantId, Describe(invoice), RelatedKind.Warehouse, invoice.WarehouseId, "WarehouseId"));

                if (invoice.CustomerId.HasValue)
                {
                    RememberCustomer(invoice.CustomerId.Value);
                    _tenantEdges.Add(new TenantEdge(invoice.TenantId, Describe(invoice), RelatedKind.Customer, invoice.CustomerId.Value, "CustomerId"));
                }

                if (invoice.SalesRepresentativeId.HasValue)
                {
                    RememberSalesRepresentative(invoice.SalesRepresentativeId.Value);
                    _tenantEdges.Add(new TenantEdge(invoice.TenantId, Describe(invoice), RelatedKind.SalesRepresentative, invoice.SalesRepresentativeId.Value, "SalesRepresentativeId"));
                }
            }

            foreach (var entry in _context.ChangeTracker.Entries<PurchaseInvoice>().Where(IsPersistedMutation))
            {
                var invoice = entry.Entity;
                RememberPurchaseInvoice(invoice.Id);
                RememberSupplier(invoice.SupplierId);
                RememberWarehouse(invoice.WarehouseId);
                _tenantEdges.Add(new TenantEdge(invoice.TenantId, Describe(invoice), RelatedKind.Supplier, invoice.SupplierId, "SupplierId"));
                _tenantEdges.Add(new TenantEdge(invoice.TenantId, Describe(invoice), RelatedKind.Warehouse, invoice.WarehouseId, "WarehouseId"));
            }

            foreach (var entry in _context.ChangeTracker.Entries<Return>().Where(IsPersistedMutation))
            {
                var returnObj = entry.Entity;
                RememberWarehouse(returnObj.WarehouseId);
                _tenantEdges.Add(new TenantEdge(returnObj.TenantId, Describe(returnObj), RelatedKind.Warehouse, returnObj.WarehouseId, "WarehouseId"));

                if (returnObj.ReturnType == ReturnType.PurchaseReturn)
                {
                    if (!returnObj.SupplierId.HasValue || !returnObj.OriginalPurchaseInvoiceId.HasValue)
                    {
                        throw new InvalidOperationException(
                            $"Purchase return {returnObj.Id} requires supplier and original purchase invoice authority.");
                    }

                    RememberSupplier(returnObj.SupplierId.Value);
                    RememberPurchaseInvoice(returnObj.OriginalPurchaseInvoiceId.Value);
                    _tenantEdges.Add(new TenantEdge(returnObj.TenantId, Describe(returnObj), RelatedKind.Supplier, returnObj.SupplierId.Value, "SupplierId"));
                    _tenantEdges.Add(new TenantEdge(returnObj.TenantId, Describe(returnObj), RelatedKind.PurchaseInvoice, returnObj.OriginalPurchaseInvoiceId.Value, "OriginalPurchaseInvoiceId"));
                }
            }

            foreach (var entry in _context.ChangeTracker.Entries<SalesInvoiceItem>().Where(IsPersistedMutation))
            {
                var item = entry.Entity;
                RememberSalesInvoice(item.SalesInvoiceId);
                RememberProduct(item.ProductId);
                RememberVariant(item.ProductVariantId);
                RememberUnit(item.UnitId);
                _salesInvoiceItemEdges.Add(new SalesInvoiceItemEdge(item.Id, item.SalesInvoiceId, item.ProductId, item.ProductVariantId, item.UnitId));
            }

            foreach (var entry in _context.ChangeTracker.Entries<PurchaseInvoiceItem>().Where(IsPersistedMutation))
            {
                var item = entry.Entity;
                RememberPurchaseInvoice(item.PurchaseInvoiceId);
                RememberProduct(item.ProductId);
                RememberVariant(item.ProductVariantId);
                RememberUnit(item.UnitId);
                _purchaseInvoiceItemEdges.Add(new PurchaseInvoiceItemEdge(item.Id, item.PurchaseInvoiceId, item.ProductId, item.ProductVariantId, item.UnitId));
            }

            foreach (var entry in _context.ChangeTracker.Entries<StockMovement>().Where(IsPersistedMutation))
            {
                var movement = entry.Entity;
                RememberWarehouse(movement.WarehouseId);
                RememberWarehouse(movement.ToWarehouseId);
                RememberProduct(movement.ProductId);
                RememberVariant(movement.ProductVariantId);
                RememberUnit(movement.UnitId);
                _tenantEdges.Add(new TenantEdge(movement.TenantId, Describe(movement), RelatedKind.Warehouse, movement.WarehouseId, "WarehouseId"));
                AddOptionalTenantEdge(movement.TenantId, Describe(movement), RelatedKind.Warehouse, movement.ToWarehouseId, "ToWarehouseId");
                _tenantEdges.Add(new TenantEdge(movement.TenantId, Describe(movement), RelatedKind.Product, movement.ProductId, "ProductId"));
                AddOptionalTenantEdge(movement.TenantId, Describe(movement), RelatedKind.Unit, movement.UnitId, "UnitId");
                AddOptionalVariantEdge(movement.TenantId, Describe(movement), movement.ProductId, movement.ProductVariantId, "ProductVariantId");
            }

            foreach (var entry in _context.ChangeTracker.Entries<ProductVariantStock>().Where(IsPersistedMutation))
            {
                var stock = entry.Entity;
                RememberWarehouse(stock.WarehouseId);
                RememberVariant(stock.VariantId);
                _tenantEdges.Add(new TenantEdge(stock.TenantId, Describe(stock), RelatedKind.Warehouse, stock.WarehouseId, "WarehouseId"));
                _variantEdges.Add(new VariantEdge(stock.TenantId, Describe(stock), null, stock.VariantId, "VariantId"));
            }

            foreach (var entry in _context.ChangeTracker.Entries<WarehouseStock>().Where(IsPersistedMutation))
            {
                var stock = entry.Entity;
                RememberWarehouse(stock.WarehouseId);
                RememberProduct(stock.ProductId);
                RememberVariant(stock.ProductVariantId);
                _tenantEdges.Add(new TenantEdge(stock.TenantId, Describe(stock), RelatedKind.Warehouse, stock.WarehouseId, "WarehouseId"));
                _tenantEdges.Add(new TenantEdge(stock.TenantId, Describe(stock), RelatedKind.Product, stock.ProductId, "ProductId"));
                AddOptionalVariantEdge(stock.TenantId, Describe(stock), stock.ProductId, stock.ProductVariantId, "ProductVariantId");
            }

            foreach (var entry in _context.ChangeTracker.Entries<AccountLedger>().Where(IsPersistedMutation))
            {
                var ledger = entry.Entity;
                if (ledger.PartyType == PartyType.Customer)
                {
                    RememberCustomer(ledger.PartyId);
                    _tenantEdges.Add(new TenantEdge(ledger.TenantId, Describe(ledger), RelatedKind.Customer, ledger.PartyId, "PartyId"));
                }
                else if (ledger.PartyType == PartyType.Supplier)
                {
                    RememberSupplier(ledger.PartyId);
                    _tenantEdges.Add(new TenantEdge(ledger.TenantId, Describe(ledger), RelatedKind.Supplier, ledger.PartyId, "PartyId"));
                }
            }

            foreach (var entry in _context.ChangeTracker.Entries<CustomerTransaction>().Where(IsPersistedMutation))
            {
                var transaction = entry.Entity;
                RememberCustomer(transaction.CustomerId);
                _tenantEdges.Add(new TenantEdge(transaction.TenantId, Describe(transaction), RelatedKind.Customer, transaction.CustomerId, "CustomerId"));
            }

            foreach (var entry in _context.ChangeTracker.Entries<CustomerPayment>().Where(IsPersistedMutation))
            {
                var payment = entry.Entity;
                RememberCustomer(payment.CustomerId);
                _tenantEdges.Add(new TenantEdge(payment.TenantId, Describe(payment), RelatedKind.Customer, payment.CustomerId, "CustomerId"));
            }

            foreach (var entry in _context.ChangeTracker.Entries<SupplierTransaction>().Where(IsPersistedMutation))
            {
                var transaction = entry.Entity;
                RememberSupplier(transaction.SupplierId);
                _tenantEdges.Add(new TenantEdge(transaction.TenantId, Describe(transaction), RelatedKind.Supplier, transaction.SupplierId, "SupplierId"));
            }

            foreach (var entry in _context.ChangeTracker.Entries<SupplierPayment>().Where(IsPersistedMutation))
            {
                var payment = entry.Entity;
                RememberSupplier(payment.SupplierId);
                RememberPurchaseInvoice(payment.PurchaseInvoiceId);
                _tenantEdges.Add(new TenantEdge(payment.TenantId, Describe(payment), RelatedKind.Supplier, payment.SupplierId, "SupplierId"));
                AddOptionalTenantEdge(payment.TenantId, Describe(payment), RelatedKind.PurchaseInvoice, payment.PurchaseInvoiceId, "PurchaseInvoiceId");
            }

            foreach (var entry in _context.ChangeTracker.Entries<FinancialTransaction>().Where(IsPersistedMutation))
            {
                var transaction = entry.Entity;
                RememberFinancialAccount(transaction.AccountId);
                _tenantEdges.Add(new TenantEdge(transaction.TenantId, Describe(transaction), RelatedKind.FinancialAccount, transaction.AccountId, "AccountId"));
            }
        }

        public void Load()
        {
            _customers = LoadTenantMap<Customer>(_customerIds);
            _suppliers = LoadTenantMap<Supplier>(_supplierIds);
            _warehouses = LoadTenantMap<Warehouse>(_warehouseIds);
            _products = LoadTenantMap<Product>(_productIds);
            _units = LoadTenantMap<Unit>(_unitIds);
            _salesRepresentatives = LoadTenantMap<SalesRepresentative>(_salesRepresentativeIds);
            _financialAccounts = LoadTenantMap<FinancialAccount>(_financialAccountIds);
            _salesInvoices = LoadTenantMap<SalesInvoice>(_salesInvoiceIds);
            _purchaseInvoices = LoadTenantMap<PurchaseInvoice>(_purchaseInvoiceIds);
            _variants = LoadVariantMap(_variantIds);
        }

        public async Task LoadAsync(CancellationToken cancellationToken)
        {
            _customers = await LoadTenantMapAsync<Customer>(_customerIds, cancellationToken);
            _suppliers = await LoadTenantMapAsync<Supplier>(_supplierIds, cancellationToken);
            _warehouses = await LoadTenantMapAsync<Warehouse>(_warehouseIds, cancellationToken);
            _products = await LoadTenantMapAsync<Product>(_productIds, cancellationToken);
            _units = await LoadTenantMapAsync<Unit>(_unitIds, cancellationToken);
            _salesRepresentatives = await LoadTenantMapAsync<SalesRepresentative>(_salesRepresentativeIds, cancellationToken);
            _financialAccounts = await LoadTenantMapAsync<FinancialAccount>(_financialAccountIds, cancellationToken);
            _salesInvoices = await LoadTenantMapAsync<SalesInvoice>(_salesInvoiceIds, cancellationToken);
            _purchaseInvoices = await LoadTenantMapAsync<PurchaseInvoice>(_purchaseInvoiceIds, cancellationToken);
            _variants = await LoadVariantMapAsync(_variantIds, cancellationToken);
        }

        public void Validate()
        {
            foreach (var edge in _tenantEdges)
            {
                ValidateTenantEdge(edge);
            }

            foreach (var edge in _variantEdges)
            {
                ValidateVariantEdge(edge);
            }

            foreach (var edge in _salesInvoiceItemEdges)
            {
                var ownerTenantId = ResolveTenant(_salesInvoices, edge.SalesInvoiceId);
                if (!ownerTenantId.HasValue)
                {
                    // SECURITY FIX: Fail-closed when parent invoice is not found
                    throw new InvalidOperationException(
                        $"Tenant co-ownership violation blocked. SalesInvoiceItem({edge.ItemId}) " +
                        $"references SalesInvoice({edge.SalesInvoiceId}) which does not exist or is deleted.");
                }

                ValidateTenantEdge(new TenantEdge(ownerTenantId.Value, $"SalesInvoiceItem({edge.ItemId})", RelatedKind.Product, edge.ProductId, "ProductId"));
                ValidateOptionalTenantEdge(ownerTenantId.Value, $"SalesInvoiceItem({edge.ItemId})", RelatedKind.Unit, edge.UnitId, "UnitId");
                ValidateOptionalVariantEdge(ownerTenantId.Value, $"SalesInvoiceItem({edge.ItemId})", edge.ProductId, edge.ProductVariantId, "ProductVariantId");
            }

            foreach (var edge in _purchaseInvoiceItemEdges)
            {
                var ownerTenantId = ResolveTenant(_purchaseInvoices, edge.PurchaseInvoiceId);
                if (!ownerTenantId.HasValue)
                {
                    // SECURITY FIX: Fail-closed when parent invoice is not found
                    throw new InvalidOperationException(
                        $"Tenant co-ownership violation blocked. PurchaseInvoiceItem({edge.ItemId}) " +
                        $"references PurchaseInvoice({edge.PurchaseInvoiceId}) which does not exist or is deleted.");
                }

                ValidateTenantEdge(new TenantEdge(ownerTenantId.Value, $"PurchaseInvoiceItem({edge.ItemId})", RelatedKind.Product, edge.ProductId, "ProductId"));
                ValidateOptionalTenantEdge(ownerTenantId.Value, $"PurchaseInvoiceItem({edge.ItemId})", RelatedKind.Unit, edge.UnitId, "UnitId");
                ValidateOptionalVariantEdge(ownerTenantId.Value, $"PurchaseInvoiceItem({edge.ItemId})", edge.ProductId, edge.ProductVariantId, "ProductVariantId");
            }
        }

        private void AddOptionalTenantEdge(Guid ownerTenantId, string owner, RelatedKind relatedKind, Guid? relatedId, string propertyName)
        {
            if (!relatedId.HasValue)
            {
                return;
            }

            _tenantEdges.Add(new TenantEdge(ownerTenantId, owner, relatedKind, relatedId.Value, propertyName));
        }

        private void AddOptionalVariantEdge(Guid ownerTenantId, string owner, Guid? expectedProductId, Guid? variantId, string propertyName)
        {
            if (!variantId.HasValue)
            {
                return;
            }

            RememberVariant(variantId.Value);
            _variantEdges.Add(new VariantEdge(ownerTenantId, owner, expectedProductId, variantId.Value, propertyName));
        }

        private void ValidateOptionalTenantEdge(Guid ownerTenantId, string owner, RelatedKind relatedKind, Guid? relatedId, string propertyName)
        {
            if (relatedId.HasValue)
            {
                ValidateTenantEdge(new TenantEdge(ownerTenantId, owner, relatedKind, relatedId.Value, propertyName));
            }
        }

        private void ValidateOptionalVariantEdge(Guid ownerTenantId, string owner, Guid productId, Guid? variantId, string propertyName)
        {
            if (variantId.HasValue)
            {
                ValidateVariantEdge(new VariantEdge(ownerTenantId, owner, productId, variantId.Value, propertyName));
            }
        }

        private void ValidateTenantEdge(TenantEdge edge)
        {
            var relatedTenantId = ResolveTenant(GetTenantMap(edge.RelatedKind), edge.RelatedId);
            if (!relatedTenantId.HasValue)
            {
                // SECURITY FIX: Fail-closed when related entity is not found
                // Missing entities (soft-deleted or non-existent) should block the operation
                throw new InvalidOperationException(
                    "Tenant co-ownership violation blocked. " +
                    $"{edge.Owner}.{edge.PropertyName} references {edge.RelatedKind}({edge.RelatedId}) " +
                    $"which does not exist or is deleted.");
            }

            if (relatedTenantId != edge.OwnerTenantId)
            {
                throw new InvalidOperationException(
                    "Tenant co-ownership violation blocked. " +
                    $"{edge.Owner}.{edge.PropertyName} references {edge.RelatedKind}({edge.RelatedId}) " +
                    $"owned by tenant {relatedTenantId}, but owner tenant is {edge.OwnerTenantId}.");
            }
        }

        private void ValidateVariantEdge(VariantEdge edge)
        {
            if (!_variants.TryGetValue(edge.VariantId, out var variant))
            {
                // SECURITY FIX: Fail-closed when variant is not found
                // Missing variants (soft-deleted or non-existent) should block the operation
                throw new InvalidOperationException(
                    "Tenant co-ownership violation blocked. " +
                    $"{edge.Owner}.{edge.PropertyName} references ProductVariant({edge.VariantId}) " +
                    $"which does not exist or is deleted.");
            }

            if (variant.TenantId != edge.OwnerTenantId)
            {
                throw new InvalidOperationException(
                    "Tenant co-ownership violation blocked. " +
                    $"{edge.Owner}.{edge.PropertyName} references ProductVariant({edge.VariantId}) " +
                    $"owned by tenant {variant.TenantId}, but owner tenant is {edge.OwnerTenantId}.");
            }

            if (edge.ExpectedProductId.HasValue && variant.ProductId != edge.ExpectedProductId.Value)
            {
                throw new InvalidOperationException(
                    "Product variant ownership violation blocked. " +
                    $"{edge.Owner}.{edge.PropertyName} references ProductVariant({edge.VariantId}) " +
                    $"for product {variant.ProductId}, but the line references product {edge.ExpectedProductId.Value}.");
            }
        }

        private static Guid? ResolveTenant(Dictionary<Guid, Guid> map, Guid id) =>
            map.TryGetValue(id, out var tenantId) ? tenantId : null;

        private Dictionary<Guid, Guid> GetTenantMap(RelatedKind relatedKind) =>
            relatedKind switch
            {
                RelatedKind.Customer => _customers,
                RelatedKind.Supplier => _suppliers,
                RelatedKind.Warehouse => _warehouses,
                RelatedKind.Product => _products,
                RelatedKind.Unit => _units,
                RelatedKind.SalesRepresentative => _salesRepresentatives,
                RelatedKind.FinancialAccount => _financialAccounts,
                RelatedKind.PurchaseInvoice => _purchaseInvoices,
                _ => throw new InvalidOperationException($"Unsupported related kind {relatedKind}.")
            };

        private Dictionary<Guid, Guid> LoadTenantMap<TEntity>(HashSet<Guid> ids)
            where TEntity : BaseEntity, ITenantOwned
        {
            var idList = ids.Where(id => id != Guid.Empty).Distinct().ToList();
            if (idList.Count == 0)
            {
                return new Dictionary<Guid, Guid>();
            }

            var result = LoadTrackedTenantMap<TEntity>(idList);
            var missingIds = idList.Where(id => !result.ContainsKey(id)).ToList();
            if (missingIds.Count == 0)
            {
                return result;
            }

            foreach (var item in _context.Set<TEntity>()
                         .IgnoreQueryFilters()
                         .Where(entity => missingIds.Contains(entity.Id) && !entity.IsDeleted)
                         .Select(entity => new { entity.Id, entity.TenantId })
                         .ToList())
            {
                result[item.Id] = item.TenantId;
            }

            return result;
        }

        private async Task<Dictionary<Guid, Guid>> LoadTenantMapAsync<TEntity>(
            HashSet<Guid> ids,
            CancellationToken cancellationToken)
            where TEntity : BaseEntity, ITenantOwned
        {
            var idList = ids.Where(id => id != Guid.Empty).Distinct().ToList();
            if (idList.Count == 0)
            {
                return new Dictionary<Guid, Guid>();
            }

            var result = LoadTrackedTenantMap<TEntity>(idList);
            var missingIds = idList.Where(id => !result.ContainsKey(id)).ToList();
            if (missingIds.Count == 0)
            {
                return result;
            }

            foreach (var item in await _context.Set<TEntity>()
                         .IgnoreQueryFilters()
                         .Where(entity => missingIds.Contains(entity.Id) && !entity.IsDeleted)
                         .Select(entity => new { entity.Id, entity.TenantId })
                         .ToListAsync(cancellationToken))
            {
                result[item.Id] = item.TenantId;
            }

            return result;
        }

        private Dictionary<Guid, Guid> LoadTrackedTenantMap<TEntity>(List<Guid> ids)
            where TEntity : BaseEntity, ITenantOwned
        {
            return _context.ChangeTracker.Entries<TEntity>()
                .Where(entry =>
                    entry.State is not EntityState.Detached and not EntityState.Deleted &&
                    !entry.Entity.IsDeleted &&
                    ids.Contains(entry.Entity.Id))
                .GroupBy(entry => entry.Entity.Id)
                .ToDictionary(group => group.Key, group => group.First().Entity.TenantId);
        }

        private Dictionary<Guid, VariantOwnership> LoadVariantMap(HashSet<Guid> ids)
        {
            var idList = ids.Where(id => id != Guid.Empty).Distinct().ToList();
            if (idList.Count == 0)
            {
                return new Dictionary<Guid, VariantOwnership>();
            }

            var result = LoadTrackedVariantMap(idList);
            var missingIds = idList.Where(id => !result.ContainsKey(id)).ToList();
            if (missingIds.Count == 0)
            {
                return result;
            }

            foreach (var item in _context.ProductVariants
                         .IgnoreQueryFilters()
                         .Where(variant => missingIds.Contains(variant.Id) && !variant.IsDeleted)
                         .Select(variant => new { variant.Id, variant.TenantId, variant.ProductId })
                         .ToList())
            {
                result[item.Id] = new VariantOwnership(item.TenantId, item.ProductId);
            }

            return result;
        }

        private async Task<Dictionary<Guid, VariantOwnership>> LoadVariantMapAsync(
            HashSet<Guid> ids,
            CancellationToken cancellationToken)
        {
            var idList = ids.Where(id => id != Guid.Empty).Distinct().ToList();
            if (idList.Count == 0)
            {
                return new Dictionary<Guid, VariantOwnership>();
            }

            var result = LoadTrackedVariantMap(idList);
            var missingIds = idList.Where(id => !result.ContainsKey(id)).ToList();
            if (missingIds.Count == 0)
            {
                return result;
            }

            foreach (var item in await _context.ProductVariants
                         .IgnoreQueryFilters()
                         .Where(variant => missingIds.Contains(variant.Id) && !variant.IsDeleted)
                         .Select(variant => new { variant.Id, variant.TenantId, variant.ProductId })
                         .ToListAsync(cancellationToken))
            {
                result[item.Id] = new VariantOwnership(item.TenantId, item.ProductId);
            }

            return result;
        }

        private Dictionary<Guid, VariantOwnership> LoadTrackedVariantMap(List<Guid> ids)
        {
            return _context.ChangeTracker.Entries<ProductVariant>()
                .Where(entry =>
                    entry.State is not EntityState.Detached and not EntityState.Deleted &&
                    !entry.Entity.IsDeleted &&
                    ids.Contains(entry.Entity.Id))
                .GroupBy(entry => entry.Entity.Id)
                .ToDictionary(
                    group => group.Key,
                    group => new VariantOwnership(group.First().Entity.TenantId, group.First().Entity.ProductId));
        }

        private void RememberCustomer(Guid id) => _customerIds.Add(id);
        private void RememberSupplier(Guid id) => _supplierIds.Add(id);
        private void RememberWarehouse(Guid id) => _warehouseIds.Add(id);
        private void RememberWarehouse(Guid? id)
        {
            if (id.HasValue)
            {
                _warehouseIds.Add(id.Value);
            }
        }

        private void RememberProduct(Guid id) => _productIds.Add(id);
        private void RememberVariant(Guid id) => _variantIds.Add(id);
        private void RememberVariant(Guid? id)
        {
            if (id.HasValue)
            {
                _variantIds.Add(id.Value);
            }
        }

        private void RememberUnit(Guid? id)
        {
            if (id.HasValue)
            {
                _unitIds.Add(id.Value);
            }
        }

        private void RememberSalesRepresentative(Guid id) => _salesRepresentativeIds.Add(id);
        private void RememberFinancialAccount(Guid id) => _financialAccountIds.Add(id);
        private void RememberSalesInvoice(Guid id) => _salesInvoiceIds.Add(id);
        private void RememberPurchaseInvoice(Guid id) => _purchaseInvoiceIds.Add(id);
        private void RememberPurchaseInvoice(Guid? id)
        {
            if (id.HasValue)
            {
                _purchaseInvoiceIds.Add(id.Value);
            }
        }

        private static bool IsPersistedMutation(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry) =>
            entry.State is EntityState.Added or EntityState.Modified;

        private static string Describe(BaseEntity entity) => $"{entity.GetType().Name}({entity.Id})";

        private sealed record TenantEdge(Guid OwnerTenantId, string Owner, RelatedKind RelatedKind, Guid RelatedId, string PropertyName);
        private sealed record VariantEdge(Guid OwnerTenantId, string Owner, Guid? ExpectedProductId, Guid VariantId, string PropertyName);
        private sealed record SalesInvoiceItemEdge(Guid ItemId, Guid SalesInvoiceId, Guid ProductId, Guid? ProductVariantId, Guid? UnitId);
        private sealed record PurchaseInvoiceItemEdge(Guid ItemId, Guid PurchaseInvoiceId, Guid ProductId, Guid? ProductVariantId, Guid? UnitId);
        private sealed record VariantOwnership(Guid TenantId, Guid ProductId);

        private enum RelatedKind
        {
            Customer,
            Supplier,
            Warehouse,
            Product,
            Unit,
            SalesRepresentative,
            FinancialAccount,
            PurchaseInvoice
        }
    }

    private static decimal RoundCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private void GuardDiscountConfigurationIntegrity()
    {
        foreach (var entry in ChangeTracker.Entries<Coupon>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.Validate();
            }
        }

        foreach (var entry in ChangeTracker.Entries<Promotion>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.Validate();
            }
        }

        foreach (var entry in ChangeTracker.Entries<SmartOffer>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.Validate();
            }
        }
    }

    private void GuardCommerceHubOrderImmutability()
    {
        GuardImmutableCommerceHubEntity<CommerceHubMarketplaceOrder>(
            nameof(CommerceHubMarketplaceOrder.Status),
            nameof(CommerceHubMarketplaceOrder.PaymentStatus),
            nameof(CommerceHubMarketplaceOrder.PaymentReferenceId),
            nameof(CommerceHubMarketplaceOrder.PaidAt),
            nameof(CommerceHubMarketplaceOrder.IsProjected),
            nameof(CommerceHubMarketplaceOrder.ProjectedAtUtc),
            nameof(CommerceHubMarketplaceOrder.LastProjectionAttemptAt),
            nameof(CommerceHubMarketplaceOrder.ProjectionAttemptCount),
            nameof(CommerceHubMarketplaceOrder.IsProjectionTerminalFailure),
            nameof(CommerceHubMarketplaceOrder.ProjectionMutationVersion),
            nameof(BaseEntity.UpdatedAt),
            nameof(BaseEntity.UpdatedBy));

        GuardImmutableCommerceHubEntity<CommerceHubSubOrder>(
            nameof(CommerceHubSubOrder.Status),
            nameof(CommerceHubSubOrder.PaymentStatus),
            nameof(CommerceHubSubOrder.PaymentReferenceId),
            nameof(CommerceHubSubOrder.PaidAt),
            nameof(CommerceHubSubOrder.CommissionSnapshotJson),
            nameof(CommerceHubSubOrder.FinalPlatformCommissionAmount),
            nameof(CommerceHubSubOrder.PromoDiscountAmount),
            nameof(CommerceHubSubOrder.IsEarningCreated),
            nameof(CommerceHubSubOrder.EarningCreatedAtUtc),
            nameof(CommerceHubSubOrder.PlatformCommissionAmount),
            nameof(CommerceHubSubOrder.VendorEarningAmount),
            nameof(CommerceHubSubOrder.PlatformCommissionRate),
            nameof(CommerceHubSubOrder.ProjectionMutationVersion),
            nameof(BaseEntity.UpdatedAt),
            nameof(BaseEntity.UpdatedBy));

        GuardImmutableCommerceHubEntity<CommerceHubSubOrderItem>(
            nameof(CommerceHubSubOrderItem.Status),
            nameof(CommerceHubSubOrderItem.DeliveredAt),
            nameof(BaseEntity.UpdatedAt),
            nameof(BaseEntity.UpdatedBy));
    }

    private void GuardCommerceHubFinancialLifecycleRecordImmutability()
    {
        GuardImmutableCommerceHubEntity<CommerceHubPayment>(
            nameof(CommerceHubPayment.Status),
            nameof(CommerceHubPayment.ProviderIntentId),
            nameof(CommerceHubPayment.ProviderTransactionId),
            nameof(CommerceHubPayment.CheckoutUrl),
            nameof(CommerceHubPayment.PaymentToken),
            nameof(CommerceHubPayment.RefundedAmount),
            nameof(CommerceHubPayment.ProvisionalRefundedAmount),
            nameof(CommerceHubPayment.FailureReason),
            nameof(CommerceHubPayment.IsProcessed),
            nameof(CommerceHubPayment.NeedsManualCurrencyReview),
            nameof(CommerceHubPayment.ProcessedAtUtc),
            nameof(CommerceHubPayment.PaidAtUtc),
            nameof(BaseEntity.UpdatedAt),
            nameof(BaseEntity.UpdatedBy));

        GuardImmutableCommerceHubEntity<CommerceHubPaymentItem>(
            nameof(BaseEntity.UpdatedAt),
            nameof(BaseEntity.UpdatedBy));

        GuardImmutableCommerceHubEntity<CommerceHubPaymentAttempt>(
            nameof(CommerceHubPaymentAttempt.ProviderIntentId),
            nameof(CommerceHubPaymentAttempt.ProviderTransactionId),
            nameof(CommerceHubPaymentAttempt.Status),
            nameof(CommerceHubPaymentAttempt.FailureReason),
            nameof(CommerceHubPaymentAttempt.CompletedAtUtc),
            nameof(BaseEntity.UpdatedAt),
            nameof(BaseEntity.UpdatedBy));

        if (CommerceHubBoundaryMutationScope.IsSensitiveArtifactBackfillAuthorized)
        {
            GuardImmutableCommerceHubEntity<CommerceHubPaymentLog>(
                nameof(CommerceHubPaymentLog.RawResponse),
                nameof(BaseEntity.UpdatedAt),
                nameof(BaseEntity.UpdatedBy));
        }
        else
        {
            GuardImmutableCommerceHubEntity<CommerceHubPaymentLog>(
                nameof(BaseEntity.UpdatedAt),
                nameof(BaseEntity.UpdatedBy));
        }

        GuardImmutableCommerceHubEntity<CommerceHubRefund>(
            nameof(CommerceHubRefund.Status),
            nameof(CommerceHubRefund.Reference),
            nameof(CommerceHubRefund.GatewayStatus),
            nameof(CommerceHubRefund.FailureReason),
            nameof(CommerceHubRefund.IsPostPayoutAdjustment),
            nameof(CommerceHubRefund.ProcessingStartedAtUtc),
            nameof(CommerceHubRefund.ProcessedAtUtc),
            nameof(BaseEntity.UpdatedAt),
            nameof(BaseEntity.UpdatedBy));

        GuardImmutableCommerceHubEntity<CommerceHubPayout>(
            nameof(CommerceHubPayout.Status),
            nameof(CommerceHubPayout.Reference),
            nameof(CommerceHubPayout.ExternalReferenceId),
            nameof(CommerceHubPayout.BankTransactionId),
            nameof(CommerceHubPayout.ExternalTransferIdempotencyKey),
            nameof(CommerceHubPayout.ExternalTransferHandoffToken),
            nameof(CommerceHubPayout.ExternalTransferHandoffCommittedAtUtc),
            nameof(CommerceHubPayout.ProcessedBy),
            nameof(CommerceHubPayout.FailureReason),
            nameof(CommerceHubPayout.ProcessedAtUtc),
            nameof(CommerceHubPayout.CompletedAtUtc),
            nameof(CommerceHubPayout.CompletionEvidenceFingerprint),
            nameof(BaseEntity.UpdatedAt),
            nameof(BaseEntity.UpdatedBy));

        GuardImmutableCommerceHubEntity<CommerceHubPayoutItem>(
            nameof(BaseEntity.UpdatedAt),
            nameof(BaseEntity.UpdatedBy));

        GuardImmutableCommerceHubEntity<CommerceHubVendorEarning>(
            nameof(CommerceHubVendorEarning.PayoutId),
            nameof(CommerceHubVendorEarning.RefundId),
            nameof(CommerceHubVendorEarning.PaidOutAmount),
            nameof(CommerceHubVendorEarning.PayoutReservedAmount),
            nameof(CommerceHubVendorEarning.Status),
            nameof(CommerceHubVendorEarning.AvailableAtUtc),
            nameof(CommerceHubVendorEarning.PaidOutAtUtc),
            nameof(CommerceHubVendorEarning.ReversedAtUtc),
            nameof(BaseEntity.UpdatedAt),
            nameof(BaseEntity.UpdatedBy));
    }

    private void GuardCommerceHubFinancialLedgerImmutability()
    {
        foreach (var entry in ChangeTracker.Entries<CommerceHubFinancialTransaction>())
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{nameof(CommerceHubFinancialTransaction)} records are append-only and cannot be deleted.");
            }

            if (entry.State == EntityState.Modified)
            {
                throw new InvalidOperationException(
                    $"{nameof(CommerceHubFinancialTransaction)} records are append-only and cannot be updated.");
            }
        }
    }

    private void GuardCommerceHubFinancialSafetyGuardImmutability()
    {
        foreach (var entry in ChangeTracker.Entries<CommerceHubFinancialOperationAudit>())
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{nameof(CommerceHubFinancialOperationAudit)} records are append-only and cannot be deleted.");
            }

            if (entry.State == EntityState.Modified)
            {
                throw new InvalidOperationException(
                    $"{nameof(CommerceHubFinancialOperationAudit)} records are append-only and cannot be updated.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<CommerceHubFinancialReconciliationReport>())
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{nameof(CommerceHubFinancialReconciliationReport)} records are append-only and cannot be deleted.");
            }

            if (entry.State == EntityState.Modified)
            {
                throw new InvalidOperationException(
                    $"{nameof(CommerceHubFinancialReconciliationReport)} records are append-only and cannot be updated.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<CommerceHubFinancialGuardLock>())
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{nameof(CommerceHubFinancialGuardLock)} records are immutable and cannot be deleted.");
            }

            if (entry.State == EntityState.Modified)
            {
                throw new InvalidOperationException(
                    $"{nameof(CommerceHubFinancialGuardLock)} records are immutable and cannot be updated.");
            }
        }
    }

    private void GuardCommerceHubVendorSettlementImmutability()
    {
        foreach (var entry in ChangeTracker.Entries<CommerceHubVendorSettlement>())
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{nameof(CommerceHubVendorSettlement)} records are append-only and cannot be deleted.");
            }

            if (entry.State == EntityState.Modified)
            {
                throw new InvalidOperationException(
                    $"{nameof(CommerceHubVendorSettlement)} records are append-only and cannot be updated.");
            }
        }
    }

    private void GuardUserGovernanceAuditImmutability()
    {
        foreach (var entry in ChangeTracker.Entries<UserGovernanceAuditLog>())
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{nameof(UserGovernanceAuditLog)} records are append-only and cannot be deleted.");
            }

            if (entry.State == EntityState.Modified)
            {
                throw new InvalidOperationException(
                    $"{nameof(UserGovernanceAuditLog)} records are append-only and cannot be updated.");
            }
        }
    }

    private void GuardUserGovernanceProfileAuthority()
    {
        foreach (var entry in ChangeTracker.Entries<UserGovernanceProfile>())
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{nameof(UserGovernanceProfile)} records cannot be deleted.");
            }

            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            if (entry.Property(nameof(UserGovernanceProfile.SubjectId)).IsModified ||
                entry.Property(nameof(UserGovernanceProfile.Role)).IsModified)
            {
                throw new InvalidOperationException(
                    $"{nameof(UserGovernanceProfile)} ownership cannot be reassigned.");
            }

            var certificationChanged =
                entry.Property(nameof(UserGovernanceProfile.IsKycVerified)).IsModified ||
                entry.Property(nameof(UserGovernanceProfile.KycVerifiedAtUtc)).IsModified ||
                entry.Property(nameof(UserGovernanceProfile.KycVerifiedByAdminId)).IsModified ||
                entry.Property(nameof(UserGovernanceProfile.KycVerificationReason)).IsModified ||
                entry.Property(nameof(UserGovernanceProfile.IsMarketplaceApproved)).IsModified ||
                entry.Property(nameof(UserGovernanceProfile.MarketplaceApprovedAtUtc)).IsModified ||
                entry.Property(nameof(UserGovernanceProfile.MarketplaceApprovedByAdminId)).IsModified ||
                entry.Property(nameof(UserGovernanceProfile.MarketplaceApprovalReason)).IsModified;

            if (certificationChanged &&
                !UserGovernanceMutationScope.IsCertificationMutationAuthorized)
            {
                throw new InvalidOperationException(
                    "Vendor verification and marketplace approval may only be changed through the governance authority service.");
            }
        }
    }

    private void GuardForensicEvidenceImmutability()
    {
        GuardAppendOnlyEvidence<AuditLog>();
        GuardAppendOnlyEvidence<CommerceHubFinancialAuditLog>();
        GuardAppendOnlyEvidence<CommerceHubVendorPaymentAuditLog>();
        GuardAppendOnlyEvidence<CommerceAccountingAudit>();
        GuardAppendOnlyEvidence<StoreConfigAuditEntry>();
        GuardAppendOnlyEvidence<LedgerEntry>();
        GuardAppendOnlyEvidence<MarketplaceEventOutboxDeadLetterArchive>();
        GuardAppendOnlyEvidence<CommerceHubMerchantLifecycleLedgerEntry>();
        GuardAppendOnlyEvidence<CommerceHubRecoveryOperationLedgerEntry>();
        GuardAppendOnlyEvidence<CommerceHubCustomerIdentityEvent>();
        GuardAppendOnlyEvidence<DomainAuditEntry>();
    }

    private void MarkProductsChangedByDependentSearchMutations()
    {
        var affectedProducts = new HashSet<(Guid TenantId, Guid ProductId)>();
        var affectedProductIdsWithoutTenant = new HashSet<Guid>();
        var affectedVariantIds = new HashSet<Guid>();
        var tenantWideProductPromotionTenantIds = new HashSet<Guid>();

        foreach (var entry in ChangeTracker.Entries<ProductVariant>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            affectedProducts.Add((entry.Entity.TenantId, entry.Entity.ProductId));
        }

        foreach (var entry in ChangeTracker.Entries<ProductVariantStock>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            affectedVariantIds.Add(entry.Entity.VariantId);
        }

        foreach (var entry in ChangeTracker.Entries<ProductVariantAttribute>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            affectedVariantIds.Add(entry.Entity.VariantId);
        }

        if (affectedVariantIds.Count > 0 && Model.FindEntityType(typeof(ProductVariant)) != null)
        {
            foreach (var variant in ProductVariants.Local.Where(variant => affectedVariantIds.Contains(variant.Id)))
            {
                affectedProducts.Add((variant.TenantId, variant.ProductId));
                affectedVariantIds.Remove(variant.Id);
            }

            foreach (var variant in ProductVariants
                         .IgnoreQueryFilters()
                         .Where(variant => affectedVariantIds.Contains(variant.Id))
                         .Select(variant => new { variant.TenantId, variant.ProductId }))
            {
                affectedProducts.Add((variant.TenantId, variant.ProductId));
            }
        }

        AddDirectProductDependents(ChangeTracker.Entries<ProductMedia>(), affectedProducts, media => (media.TenantId, media.ProductId));
        foreach (var entry in ChangeTracker.Entries<ProductUnit>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (entry.Entity.Product != null)
            {
                affectedProducts.Add((entry.Entity.Product.TenantId, entry.Entity.ProductId));
            }
            else
            {
                affectedProductIdsWithoutTenant.Add(entry.Entity.ProductId);
            }
        }

        // Affiliate policy has its own authority and event pipeline. Treating it as a
        // Product search dependent changes Product.UpdatedAt/xmin for an Affiliate-only
        // mutation and violates Product/Affiliate lifecycle separation.

        foreach (var entry in ChangeTracker.Entries<Promotion>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            AddPromotionProducts(
                entry.Entity.TenantId,
                entry.Entity.Scope,
                entry.Entity.AppliesToId,
                affectedProducts,
                tenantWideProductPromotionTenantIds);

            if (entry.State == EntityState.Modified)
            {
                AddPromotionProducts(
                    entry.Entity.TenantId,
                    entry.Property(nameof(Promotion.Scope)).OriginalValue is PromotionScope originalScope
                        ? originalScope
                        : entry.Entity.Scope,
                    entry.Property(nameof(Promotion.AppliesToId)).OriginalValue as Guid?,
                    affectedProducts,
                    tenantWideProductPromotionTenantIds);
            }
        }

        if (affectedProducts.Count == 0 &&
            affectedProductIdsWithoutTenant.Count == 0 &&
            tenantWideProductPromotionTenantIds.Count == 0)
        {
            return;
        }

        var productIds = affectedProducts
            .Select(item => item.ProductId)
            .Concat(affectedProductIdsWithoutTenant)
            .Distinct()
            .ToList();
        var products = Products.Local
            .Where(product => productIds.Contains(product.Id))
            .ToDictionary(product => product.Id);

        if (tenantWideProductPromotionTenantIds.Count > 0)
        {
            foreach (var product in Products
                         .IgnoreQueryFilters()
                         .Where(product =>
                             tenantWideProductPromotionTenantIds.Contains(product.TenantId) &&
                             !product.IsDeleted &&
                             product.IsActive &&
                             product.IsOnlineVisible &&
                             product.IsSearchable))
            {
                affectedProducts.Add((product.TenantId, product.Id));
                products[product.Id] = product;
            }
        }

        foreach (var product in Products
                     .IgnoreQueryFilters()
                     .Where(product => productIds.Contains(product.Id)))
        {
            products[product.Id] = product;
        }

        foreach (var productId in affectedProductIdsWithoutTenant)
        {
            if (products.TryGetValue(productId, out var product))
            {
                affectedProducts.Add((product.TenantId, productId));
            }
        }

        foreach (var key in affectedProducts)
        {
            if (!products.TryGetValue(key.ProductId, out var product) ||
                product.TenantId != key.TenantId)
            {
                continue;
            }

            var productEntry = Entry(product);
            if (productEntry.State == EntityState.Unchanged)
            {
                product.UpdatedAt = DateTime.UtcNow;
            }
        }
    }

    private static void AddDirectProductDependents<TEntity>(
        IEnumerable<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>> entries,
        ISet<(Guid TenantId, Guid ProductId)> affectedProducts,
        Func<TEntity, (Guid TenantId, Guid ProductId)> keySelector)
        where TEntity : class
    {
        foreach (var entry in entries.Where(entry =>
                     entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            affectedProducts.Add(keySelector(entry.Entity));
        }
    }

    private static void AddPromotionProducts(
        Guid tenantId,
        PromotionScope scope,
        Guid? appliesToId,
        ISet<(Guid TenantId, Guid ProductId)> affectedProducts,
        ISet<Guid> tenantWideProductPromotionTenantIds)
    {
        if (scope != PromotionScope.Product)
        {
            return;
        }

        if (appliesToId.HasValue)
        {
            affectedProducts.Add((tenantId, appliesToId.Value));
            return;
        }

        tenantWideProductPromotionTenantIds.Add(tenantId);
    }

    private void GuardTreasuryCashAuthority()
    {
        foreach (var entry in ChangeTracker.Entries<TreasuryInitialCapitalizationAllocation>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "Treasury initial capitalization allocations are immutable.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<TreasuryInitialCapitalization>())
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "Treasury initial capitalization documents cannot be deleted.");
            }

            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            var allowedProperties = new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(TreasuryInitialCapitalization.Status),
                nameof(TreasuryInitialCapitalization.PostedAtUtc),
                nameof(TreasuryInitialCapitalization.PostedJournalId),
                nameof(TreasuryInitialCapitalization.ReversalJournalId),
                nameof(TreasuryInitialCapitalization.ReversalOperationId),
                nameof(TreasuryInitialCapitalization.ReversalRequestHash),
                nameof(TreasuryInitialCapitalization.ReversalReason),
                nameof(TreasuryInitialCapitalization.ReversedAtUtc),
                nameof(TreasuryInitialCapitalization.MutationVersion),
                nameof(BaseEntity.UpdatedAt),
                nameof(BaseEntity.UpdatedBy)
            };

            if (entry.Properties.Any(property =>
                    property.IsModified && !allowedProperties.Contains(property.Metadata.Name)))
            {
                throw new InvalidOperationException(
                    "Treasury initial capitalization source authority is immutable after creation.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<LedgerEntry>())
        {
            if (entry.State == EntityState.Added &&
                !LedgerMutationScope.IsAuthorized)
            {
                throw new InvalidOperationException(
                    "Ledger entries may only be created by the financial posting authority.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<Account>())
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{nameof(Account)} records cannot be deleted; deactivate them instead.");
            }

            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            if (entry.Property(nameof(Account.TenantId)).IsModified ||
                entry.Property(nameof(Account.AccountCode)).IsModified ||
                entry.Property(nameof(Account.AccountType)).IsModified ||
                entry.Property(nameof(Account.IsSupplierCardFunding)).IsModified)
            {
                throw new InvalidOperationException(
                    $"{nameof(Account)} tenant, code, and classification are immutable.");
            }

            if (entry.Property(nameof(Account.CurrentBalance)).IsModified &&
                !LedgerMutationScope.IsAuthorized)
            {
                throw new InvalidOperationException(
                    "Ledger account balances may only be changed by the financial posting authority.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<JournalEntry>())
        {
            if (entry.State == EntityState.Added)
            {
                if (!LedgerMutationScope.IsAuthorized)
                {
                    throw new InvalidOperationException(
                        "Journal entries may only be created by the financial posting authority.");
                }

                continue;
            }

            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{nameof(JournalEntry)} records cannot be deleted.");
            }

            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            if (!LedgerMutationScope.IsAuthorized)
            {
                throw new InvalidOperationException(
                    "Journal reversal state may only be changed by the financial posting authority.");
            }

            var allowedProperties = new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(JournalEntry.Status),
                nameof(JournalEntry.ReversedByJournalId),
                nameof(JournalEntry.ReversalReason),
                nameof(BaseEntity.UpdatedAt)
            };

            if (entry.Properties.Any(property =>
                    property.IsModified && !allowedProperties.Contains(property.Metadata.Name)))
            {
                throw new InvalidOperationException(
                    $"{nameof(JournalEntry)} identity and financial lineage are immutable after posting.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<TreasuryAccount>())
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{nameof(TreasuryAccount)} records cannot be deleted; deactivate them instead.");
            }

            if (entry.State == EntityState.Modified &&
                (entry.Property(nameof(TreasuryAccount.TenantId)).IsModified ||
                 entry.Property(nameof(TreasuryAccount.AccountId)).IsModified ||
                 entry.Property(nameof(TreasuryAccount.Type)).IsModified))
            {
                throw new InvalidOperationException(
                    $"{nameof(TreasuryAccount)} tenant, ledger account, and type are immutable.");
            }
        }
    }

    private void GuardSupplierCardAuthority()
    {
        GuardImmutableCommerceHubEntity<SupplierCardFunding>();

        foreach (var entry in ChangeTracker.Entries<SupplierCardFundingInstrument>())
        {
            if (entry.State == EntityState.Deleted)
                throw new InvalidOperationException("Supplier card funding instruments cannot be deleted; deactivate them instead.");
            if (entry.State != EntityState.Modified)
                continue;

            var immutableProperties = new[]
            {
                nameof(SupplierCardFundingInstrument.TenantId),
                nameof(SupplierCardFundingInstrument.InstrumentType),
                nameof(SupplierCardFundingInstrument.LedgerAccountId),
                nameof(SupplierCardFundingInstrument.FundingSourceTreasuryAccountId),
                nameof(SupplierCardFundingInstrument.AuthorityVersion)
            };
            if (immutableProperties.Any(name => entry.Property(name).IsModified))
                throw new InvalidOperationException("Supplier card instrument accounting authority is immutable.");
        }

        foreach (var entry in ChangeTracker.Entries<PurchaseInvoicePayment>())
        {
            if (entry.State == EntityState.Added)
            {
                GuardNewSupplierCardSnapshot(
                    entry.Entity.PaymentMethod,
                    entry.Entity.FundingInstrumentId,
                    entry.Entity.FundingLedgerAccountId,
                    entry.Entity.FundingInstrumentVersion,
                    nameof(PurchaseInvoicePayment));
            }
            else if (entry.State == EntityState.Modified &&
                (entry.Property(nameof(PurchaseInvoicePayment.PaymentMethod)).IsModified ||
                 entry.Property(nameof(PurchaseInvoicePayment.FundingInstrumentId)).IsModified ||
                 entry.Property(nameof(PurchaseInvoicePayment.FundingLedgerAccountId)).IsModified ||
                 entry.Property(nameof(PurchaseInvoicePayment.FundingInstrumentVersion)).IsModified))
            {
                throw new InvalidOperationException("Purchase payment method and supplier card authority snapshots are immutable.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<SupplierPayment>())
        {
            if (entry.State == EntityState.Added)
            {
                GuardNewSupplierCardSnapshot(
                    entry.Entity.PaymentMethod,
                    entry.Entity.FundingInstrumentId,
                    entry.Entity.FundingLedgerAccountId,
                    entry.Entity.FundingInstrumentVersion,
                    nameof(SupplierPayment));
            }
            else if (entry.State == EntityState.Modified &&
                (entry.Property(nameof(SupplierPayment.PaymentMethod)).IsModified ||
                 entry.Property(nameof(SupplierPayment.FundingInstrumentId)).IsModified ||
                 entry.Property(nameof(SupplierPayment.FundingLedgerAccountId)).IsModified ||
                 entry.Property(nameof(SupplierPayment.FundingInstrumentVersion)).IsModified))
            {
                throw new InvalidOperationException("Supplier payment method and card authority snapshots are immutable.");
            }
        }
    }

    private static void GuardNewSupplierCardSnapshot(
        PointOfSale.Core.Enums.PaymentMethod paymentMethod,
        Guid? fundingInstrumentId,
        Guid? fundingLedgerAccountId,
        int? fundingInstrumentVersion,
        string entityName)
    {
        var hasCompleteAuthority = fundingInstrumentId.HasValue && fundingInstrumentId != Guid.Empty &&
            fundingLedgerAccountId.HasValue && fundingLedgerAccountId != Guid.Empty &&
            fundingInstrumentVersion > 0;
        var hasAnyAuthority = fundingInstrumentId.HasValue || fundingLedgerAccountId.HasValue || fundingInstrumentVersion.HasValue;

        if ((paymentMethod == PointOfSale.Core.Enums.PaymentMethod.Card && !hasCompleteAuthority) ||
            (paymentMethod != PointOfSale.Core.Enums.PaymentMethod.Card && hasAnyAuthority))
        {
            throw new InvalidOperationException(
                $"{entityName} supplier card authority must be complete for Card payments and absent otherwise.");
        }
    }

    private void GuardStockMovementImmutability()
    {
        foreach (var entry in ChangeTracker.Entries<StockMovement>())
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{nameof(StockMovement)} records are append-only and cannot be deleted.");
            }

            if (entry.State == EntityState.Modified)
            {
                throw new InvalidOperationException(
                    $"{nameof(StockMovement)} records are append-only and cannot be updated.");
            }
        }
    }

    private void GuardAffiliatePayoutAuthority()
    {
        foreach (var entry in ChangeTracker.Entries<AffiliateCommissionLedger>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{nameof(AffiliateCommissionLedger)} records are append-only and cannot be changed.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<AffiliateBalanceLedger>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{nameof(AffiliateBalanceLedger)} records are append-only and cannot be changed.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<AffiliatePayoutTransferAllocation>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{nameof(AffiliatePayoutTransferAllocation)} records are append-only and cannot be changed.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<AffiliatePayoutTransferEvidence>())
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{nameof(AffiliatePayoutTransferEvidence)} records cannot be deleted.");
            }

            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            var mutableProperties = new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(PointOfSale.Core.Entities.Affiliate.AffiliatePayoutTransferEvidence.AllocatedAmount),
                nameof(PointOfSale.Core.Entities.Affiliate.AffiliatePayoutTransferEvidence.LastAllocationAtUtc),
                nameof(BaseEntity.UpdatedAt)
            };

            if (entry.Properties.Any(property =>
                    property.IsModified && !mutableProperties.Contains(property.Metadata.Name)))
            {
                throw new InvalidOperationException(
                    $"{nameof(AffiliatePayoutTransferEvidence)} identity, tenant, amount, reference, and currency are immutable.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<AffiliatePayoutRequest>())
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{nameof(AffiliatePayoutRequest)} records cannot be deleted.");
            }

            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            var immutableProperties = new[]
            {
                nameof(AffiliatePayoutRequest.Id),
                nameof(AffiliatePayoutRequest.StoreTenantId),
                nameof(AffiliatePayoutRequest.AffiliateAccountId),
                nameof(AffiliatePayoutRequest.RequestedAmount),
                nameof(AffiliatePayoutRequest.RequestedAtUtc),
                nameof(AffiliatePayoutRequest.Currency),
                nameof(AffiliatePayoutRequest.PayoutWalletNumberSnapshot),
                nameof(AffiliatePayoutRequest.PayoutWalletTypeSnapshot),
                nameof(AffiliatePayoutRequest.PayoutWalletUpdatedAtUtcSnapshot),
                nameof(AffiliatePayoutRequest.PayoutWalletSnapshotCapturedAtUtc)
            };

            if (immutableProperties.Any(propertyName => entry.Property(propertyName).IsModified))
            {
                throw new InvalidOperationException(
                    $"{nameof(AffiliatePayoutRequest)} identity, amount, currency, tenant, affiliate, and payout destination snapshot are immutable.");
            }
        }
    }

    private void GuardAffiliateAttributionAuthority()
    {
        foreach (var entry in ChangeTracker.Entries<AffiliateAttribution>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{nameof(AffiliateAttribution)} records are append-only and cannot be changed after conversion.");
            }

            if (entry.State != EntityState.Added)
            {
                continue;
            }

            var attribution = entry.Entity;
            var trackedOrder = ChangeTracker.Entries<Order>()
                .Where(orderEntry => orderEntry.State != EntityState.Deleted)
                .Select(orderEntry => orderEntry.Entity)
                .SingleOrDefault(order => order.Id == attribution.OrderId);

            var orderMatches = trackedOrder != null
                ? trackedOrder.TenantId == attribution.TenantId &&
                  trackedOrder.AffiliateAccountId == attribution.AffiliateAccountId &&
                  string.Equals(
                      trackedOrder.AffiliateReferralCode?.Trim(),
                      attribution.ReferralCode,
                      StringComparison.OrdinalIgnoreCase) &&
                  trackedOrder.PromotionLinkId == attribution.PromotionLinkId
                : Orders
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Any(order =>
                        order.Id == attribution.OrderId &&
                        order.TenantId == attribution.TenantId &&
                        !order.IsDeleted &&
                        order.AffiliateAccountId == attribution.AffiliateAccountId &&
                        order.AffiliateReferralCode == attribution.ReferralCode &&
                        order.PromotionLinkId == attribution.PromotionLinkId);

            if (!orderMatches)
            {
                throw new InvalidOperationException(
                    $"Affiliate attribution {attribution.Id} does not match its tenant-owned order authority snapshot.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<Order>())
        {
            if (entry.State == EntityState.Added)
            {
                var hasAffiliateId = entry.Entity.AffiliateAccountId.HasValue;
                var hasReferralCode = !string.IsNullOrWhiteSpace(entry.Entity.AffiliateReferralCode);
                if (hasAffiliateId != hasReferralCode)
                {
                    throw new InvalidOperationException(
                        $"Order {entry.Entity.Id} has an incomplete affiliate attribution snapshot.");
                }

                continue;
            }

            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            var affiliateIdProperty = entry.Property(order => order.AffiliateAccountId);
            var referralCodeProperty = entry.Property(order => order.AffiliateReferralCode);
            var promotionLinkProperty = entry.Property(order => order.PromotionLinkId);
            var affiliateChanged =
                affiliateIdProperty.CurrentValue != affiliateIdProperty.OriginalValue ||
                !string.Equals(
                    referralCodeProperty.CurrentValue,
                    referralCodeProperty.OriginalValue,
                    StringComparison.Ordinal) ||
                promotionLinkProperty.CurrentValue != promotionLinkProperty.OriginalValue;
            if (affiliateChanged)
            {
                throw new InvalidOperationException(
                    $"Order {entry.Entity.Id} affiliate attribution cannot be reassigned after creation.");
            }
        }
    }

    private void GuardAppendOnlyEvidence<TEntity>()
        where TEntity : class
    {
        foreach (var entry in ChangeTracker.Entries<TEntity>())
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{typeof(TEntity).Name} forensic evidence records are append-only and cannot be deleted.");
            }

            if (entry.State == EntityState.Modified)
            {
                throw new InvalidOperationException(
                    $"{typeof(TEntity).Name} forensic evidence records are append-only and cannot be updated.");
            }
        }
    }

    private void GuardPosHistoricalCostSnapshotImmutability()
    {
        GuardImmutableCostSnapshot<SalesInvoiceItem>(
            nameof(SalesInvoiceItem.CostPriceSnapshot),
            nameof(SalesInvoiceItem.CostAmountSnapshot));
        GuardImmutableCostSnapshot<ReturnItem>(
            nameof(ReturnItem.CostPriceSnapshot),
            nameof(ReturnItem.CostAmountSnapshot));
    }

    private void GuardImmutableCostSnapshot<TEntity>(
        string costPricePropertyName,
        string costAmountPropertyName)
        where TEntity : class
    {
        foreach (var entry in ChangeTracker.Entries<TEntity>())
        {
            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            GuardImmutableCostSnapshotProperty(entry.Property(costPricePropertyName), typeof(TEntity).Name);
            GuardImmutableCostSnapshotProperty(entry.Property(costAmountPropertyName), typeof(TEntity).Name);
        }
    }

    private static void GuardImmutableCostSnapshotProperty(
        Microsoft.EntityFrameworkCore.ChangeTracking.PropertyEntry property,
        string entityName)
    {
        if (!property.IsModified)
        {
            return;
        }

        // Legacy rows may be backfilled from null once. Once materialized, the
        // historical cost snapshot is financial truth and must not be rewritten.
        if (property.OriginalValue is null)
        {
            return;
        }

        if (!Equals(property.OriginalValue, property.CurrentValue))
        {
            throw new InvalidOperationException(
                $"{entityName}.{property.Metadata.Name} is an immutable historical POS cost snapshot and cannot be changed after it is materialized.");
        }
    }

    private void GuardImmutableCommerceHubEntity<TEntity>(params string[] mutableProperties)
        where TEntity : BaseEntity
    {
        var allowedProperties = new HashSet<string>(mutableProperties, StringComparer.Ordinal);

        foreach (var entry in ChangeTracker.Entries<TEntity>())
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{typeof(TEntity).Name} records are immutable and cannot be deleted once created.");
            }

            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            var blockedProperties = entry.Properties
                .Where(property => property.IsModified && !allowedProperties.Contains(property.Metadata.Name))
                .Select(property => property.Metadata.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            if (blockedProperties.Count == 0)
            {
                continue;
            }

            throw new InvalidOperationException(
                $"{typeof(TEntity).Name} records are immutable. Blocked fields: {string.Join(", ", blockedProperties)}.");
        }
    }
}
