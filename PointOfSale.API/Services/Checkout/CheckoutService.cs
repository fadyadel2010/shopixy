using System.Diagnostics;
using System.Data;
using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Npgsql;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.DTOs.Checkout;
using PointOfSale.API.DTOs.Payments;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Events;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Helpers;
using PointOfSale.API.Infrastructure.Http;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.API.Infrastructure.DocumentNumbers;
using PointOfSale.API.Configuration;
using PointOfSale.API.Security;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Cart;
using PointOfSale.API.Services.Catalog;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Events;
using PointOfSale.Core.Events.AbandonedCart;
using PointOfSale.Core.Events.Affiliate;
using PointOfSale.Core.Events.WhatsApp;
using PointOfSale.Core.Financial;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Interfaces.Geographic;
using PointOfSale.Core.Entities.Geographic;
using PointOfSale.Core.Interfaces.WhatsApp;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Payments;
using PointOfSale.Core.Policies;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Infrastructure.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.API.Services.Payments.Paymob;
using PointOfSale.API.Services.Coupons;
using PointOfSale.API.Services.Promotions;
using PointOfSale.API.Services.PhoneRisk;
using PointOfSale.API.Services.Pricing;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Discounts;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Reliability;
using Microsoft.AspNetCore.DataProtection;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Services.Checkout;

// =============================================================================
// CHECKOUT AUTHORITY GUARANTEE
// =============================================================================
// Checkout must not let projection/read-model state decide financial or stock
// truth. SQL product/variant rows are the durable authority for price and stock
// policy. ProductVariantStocks are the write-side reservation authority.
//
// PostgreSQL is used for:
// ✅ Orders (write)
// ✅ Shipments (write)
// ✅ ProductVariantStocks (reservation write-side)
// 
// MongoDB projections may populate storefront selectors, but final checkout validation uses SQL authority:
// ✅ ShippingMethods (rates, thresholds)
// ✅ PaymentMethods (fees, limits)
// ✅ StoreConfig behavior flags that can change financial outcomes
// =============================================================================

// =============================================================================
// PHASE 11.4.1 — VARIANT-BASED CHECKOUT & INVENTORY RESERVATION
// =============================================================================
// Checkout MUST use ProductVariant (SKU) — NEVER Product
// 
// ABSOLUTE RULES:
// - A Product is NEVER sold. A ProductVariant (SKU) is ALWAYS sold.
// - Prices come from Variant.OnlinePrice ?? Variant.Price (NEVER Product.Price)
// - Stock comes from ProductVariantStocks (NEVER WarehouseStocks)
// - Orders store VariantId as source of truth
// =============================================================================

/// <summary>
/// Implementation of Checkout service.
/// 
/// PHASE 3.5: CHECKOUT API (Cart → Order)
/// 
/// ARCHITECTURAL BOUNDARY:
/// - Cart is READ-ONLY during checkout (from Redis)
/// - Prices and stock policy come from SQL product/variant authority
/// - Shipping and payment method configuration come from SQL authority
/// - PostgreSQL is Source of Truth for Orders, Shipments, and reservations
/// - Redis used for: Cart snapshot, Distributed lock, Idempotency
/// - Checkout is transactional
/// - Payment is NOT executed (intent only)
/// 
/// PHASE 11.4.1: VARIANT-BASED CHECKOUT
/// - All pricing comes from ProductVariant (SKU)
/// - Stock validation uses ProductVariantStocks
/// - Orders store immutable variant snapshots
/// 
/// Checkout Steps:
/// 1. Acquire distributed lock (lock:checkout:{tenantId}:{cartId})
/// 2. Check idempotency (tenant:{tenantId}:checkout:{requestId})
/// 3. Snapshot cart from Redis
/// 4. Validate prices from SQL ProductVariants (NOT projection documents)
/// 5. Validate shipping from SQL authority
/// 6. Validate payment from SQL authority
/// 7. Validate and reserve inventory (ProductVariantStocks)
/// 8. Create order in PostgreSQL (single transaction)
/// 9. Generate payment intent (NO payment gateway call)
/// 10. Store idempotency result
/// 
/// Error Handling:
/// - Lock fail → Abort (409)
/// - Cart invalid → Abort (400)
/// - Price changed → Abort (409)
/// - Stock fail → Abort (409)
/// - Order fail → Rollback + release inventory
/// - Payment intent fail → Order stays Pending
/// </summary>
public class CheckoutService : ICheckoutService
{
    private const NumberStyles CanonicalCheckoutNumberStyles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
    private sealed class CheckoutContext
    {
        public bool PromotionCalculationFailed { get; set; }
    }

    private sealed class ManualWalletProviderMetadata
    {
        public string? MerchantWalletNumber { get; set; }
        public string? WalletInstructions { get; set; }
    }

    private sealed record CheckoutPaymentSession
    {
        public Guid? PaymentIntentId { get; init; }
        public string? PaymentReference { get; init; }
        public string? RedirectUrl { get; init; }
        public string? ReferenceCode { get; init; }
        public DateTime? ExpiresAtUtc { get; init; }
        public bool IsManualPayment { get; init; }
        public IReadOnlyDictionary<string, string?> CompatibilityFields { get; init; } =
            new Dictionary<string, string?>(StringComparer.Ordinal);
    }

    private sealed record AuthoritativeCheckoutVariant(
        Guid VariantId,
        Guid ProductId,
        string ProductName,
        string? SKU,
        decimal EffectivePrice,
        decimal CostPrice,
        decimal Weight,
        Guid CategoryId,
        bool ProductIsActive,
        bool ProductIsOnlineVisible,
        bool ProductIsPublished,
        bool VariantIsActive,
        bool VariantIsCurrentSemanticOwner,
        bool ProductTrackStock,
        bool ProductAllowBackorder);

    private sealed record InventoryReservationResult(
        Guid ReservationId,
        List<InventoryReservationItem> ReservedItems,
        DateTime ExpiresAtUtc);

    private sealed record AffiliateAttributionAuthority(
        Guid AffiliateAccountId,
        Guid? PromotionLinkId,
        Guid? PromotionLinkProductId,
        string ReferralCode,
        Guid AuthorityTenantId,
        Guid? AffiliateUserId,
        string Flow);

    private sealed record AffiliateAccountAuthorityCandidate(
        Guid Id,
        string ReferralCode,
        Guid TenantId);

    private sealed record AffiliatePromotionLinkAuthorityCandidate(
        Guid Id,
        Guid AffiliateUserId,
        Guid? AffiliateAccountId,
        string ReferralCode,
        Guid TenantId,
        Guid ProductId);

    private sealed record AdmittedCheckoutGeographicAddress(
        Guid AddressAdmissionEvidenceId,
        Guid CatalogReleaseId,
        Guid CountryPlaceId,
        Guid? AdministrativeAreaPlaceId,
        Guid? LocalityPlaceId,
        Guid? DistrictPlaceId,
        Guid? ZonePlaceId);

    private sealed record CheckoutGeographicAdmissionSnapshot(
        AdmittedCheckoutGeographicAddress? Shipping,
        AdmittedCheckoutGeographicAddress? Billing);

    // Reusable JSON options for case-insensitive deserialization (performance optimization)
    private static readonly JsonSerializerOptions CaseInsensitiveJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    
    // Promotion warning constants for localization
    private const string PromotionCalculationFallbackWarningMessage = "Some discounts could not be applied. You have been charged the undiscounted price.";
    private const string PromotionCalculationFallbackWarningCode = "PROMOTION_CALCULATION_FAILED";

    // Write-side (PostgreSQL) - Orders, Shipments, Stock reservation
    private readonly ApplicationDbContext _dbContext;
    
    // Projection-backed read models for legacy cart enrichment and operational configuration.
    // Checkout price/stock policy is resolved from SQL authority before order creation.
    private readonly IProductVariantReadRepository _variantRead;
    private readonly IProductReadRepository _productRead; // Legacy, kept for backward compatibility
    private readonly IShippingMethodReadRepository _shippingRead;
    private readonly IPaymentMethodReadRepository _paymentRead;
    
    // PHASE 11.4.1: Variant-based stock reservation
    private readonly IProductVariantStockRepository _variantStockRepository;
    
    // Infrastructure
    private readonly ICartService _cartService;
    private readonly IEventPublisher _eventPublisher;
    private readonly ICouponValidationService _couponValidationService;
    private readonly IPromotionEngine _promotionEngine;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IStorefrontPaymentRuntimeReadinessService _paymentRuntimeReadinessService;
    private readonly IStorefrontPricingService _storefrontPricingService;
    private readonly ICheckoutExecutionLockManager _checkoutExecutionLockManager;
    private readonly ICheckoutRequestIdempotencyCoordinator _checkoutRequestIdempotencyCoordinator;
    private readonly ICheckoutSessionTokenService _checkoutSessionTokenService;
    private readonly ICheckoutAttachmentService _checkoutAttachmentService;
    private readonly IWarehouseResolutionService _warehouseResolutionService;
    private readonly ICarrierIntegrationAuthority _carrierIntegrationAuthority;
    private readonly IDataProtector _dataProtector;
    private readonly IPaymentIntentClientSecretProtector _clientSecretProtector;
    private readonly IPaymobSensitiveArtifactProtector _paymobArtifactProtector;
    private readonly R2Options _r2Options;
    private readonly ILogger<CheckoutService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    
    // PHASE 13: WhatsApp Messaging Integration
    private readonly IWhatsAppFeatureGateService? _whatsAppFeatureGate;
    
    // Phone OTP Verification for Checkout
    private readonly IStorefrontPhoneVerificationService? _phoneVerificationService;

    // Merchant-managed phone blocking for storefront orders
    private readonly IPhoneRiskProfileService _phoneRiskProfileService;
    private readonly IOrderVelocityService _orderVelocityService;
    private readonly IMarketplaceEventOutboxService _marketplaceEventOutbox;
    private readonly IAddressAdmissionService? _addressAdmissionService;
    
    // AF-12: Affiliate Mode Detection
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IProductReadRepository _productRepository;
    private readonly CommerceReliabilityTelemetry? _reliabilityTelemetry;
    
    // Row-level locking requirement for promotion capacity
    private readonly bool _requireRowLevelLocking;

    // =============================================================================
    // CHECKOUT CONFIGURATION CONSTANTS
    // =============================================================================

    /// <summary>
    /// Inventory reservation TTL in minutes
    /// </summary>
    private const int InventoryReservationTtlMinutes = 10;
    private const int CheckoutAuthorityCommitMaxRetries = 3;

    /// <summary>
    /// Price change threshold percentage for rejection
    /// Differences above this are considered "major" and abort checkout
    /// </summary>
    private const decimal PriceChangeThresholdPercent = 5.0m;
    private const string ActiveCartOrderUniqueIndexName = "IX_Orders_TenantId_CartId_ActiveStatuses";
    private const string OutboxMessageInboxUniqueIndexName = "IX_OutboxMessage_InboxMessageId_InboxConsumerId_SequenceNumber";
    private const string OutboxMessageOutboxUniqueIndexName = "IX_OutboxMessage_OutboxId_SequenceNumber";
    private const string OutboxMessagePrimaryKeyName = "PK_OutboxMessages";

    public CheckoutService(
        ApplicationDbContext dbContext,
        IProductVariantReadRepository variantRead,
        IProductReadRepository productRead,
        IShippingMethodReadRepository shippingRead,
        IPaymentMethodReadRepository paymentRead,
        IProductVariantStockRepository variantStockRepository,
        IMongoDatabase mongoDatabase,
        IEventPublisher eventPublisher,
        ICouponValidationService couponValidationService,
        IPromotionEngine promotionEngine,
        IPaymentGateway paymentGateway,
        IStorefrontPaymentRuntimeReadinessService paymentRuntimeReadinessService,
        IStorefrontPricingService storefrontPricingService,
        ICheckoutExecutionLockManager checkoutExecutionLockManager,
        ICheckoutRequestIdempotencyCoordinator checkoutRequestIdempotencyCoordinator,
        IMarketplaceEventOutboxService marketplaceEventOutbox,
        IPhoneRiskProfileService phoneRiskProfileService,
        IOrderVelocityService orderVelocityService,
        IDataProtectionProvider dataProtectionProvider,
        IPaymentIntentClientSecretProtector clientSecretProtector,
        IPaymobSensitiveArtifactProtector paymobArtifactProtector,
        ICheckoutSessionTokenService checkoutSessionTokenService,
        ICheckoutAttachmentService checkoutAttachmentService,
        IWarehouseResolutionService warehouseResolutionService,
        IOptions<R2Options> r2Options,
        IOptions<PaymentLifecycleSettings> paymentLifecycleSettings,
        ILogger<CheckoutService> logger,
        IHttpContextAccessor httpContextAccessor,
        ICartService cartService,
        IWhatsAppFeatureGateService? whatsAppFeatureGate = null,
        IStorefrontPhoneVerificationService? phoneVerificationService = null,
        CommerceReliabilityTelemetry? reliabilityTelemetry = null,
        IAddressAdmissionService? addressAdmissionService = null,
        ICarrierIntegrationAuthority? carrierIntegrationAuthority = null)
    {
        // CRITICAL: Purpose string MUST match PaymentMethodsAdminController.DataProtectionPurpose
        _dataProtector = dataProtectionProvider.CreateProtector("PaymentMethodSecrets");
        _clientSecretProtector = clientSecretProtector;
        _paymobArtifactProtector = paymobArtifactProtector;
        _dbContext = dbContext;
        _variantRead = variantRead;
        _productRead = productRead;
        _shippingRead = shippingRead;
        _paymentRead = paymentRead;
        _variantStockRepository = variantStockRepository;
        _cartService = cartService;
        _eventPublisher = eventPublisher;
        _couponValidationService = couponValidationService;
        _promotionEngine = promotionEngine;
        _paymentGateway = paymentGateway;
        _paymentRuntimeReadinessService = paymentRuntimeReadinessService;
        _storefrontPricingService = storefrontPricingService;
        _checkoutExecutionLockManager = checkoutExecutionLockManager;
        _checkoutRequestIdempotencyCoordinator = checkoutRequestIdempotencyCoordinator;
        _checkoutSessionTokenService = checkoutSessionTokenService;
        _checkoutAttachmentService = checkoutAttachmentService;
        _warehouseResolutionService = warehouseResolutionService;
        _carrierIntegrationAuthority = carrierIntegrationAuthority ?? NullCarrierIntegrationAuthority.Instance;
        _r2Options = r2Options.Value;
        _marketplaceEventOutbox = marketplaceEventOutbox;
        _phoneRiskProfileService = phoneRiskProfileService;
        _orderVelocityService = orderVelocityService;
        _logger = logger;
        _whatsAppFeatureGate = whatsAppFeatureGate;
        _phoneVerificationService = phoneVerificationService;
        _httpContextAccessor = httpContextAccessor;
        _productRepository = productRead;
        _reliabilityTelemetry = reliabilityTelemetry;
        _addressAdmissionService = addressAdmissionService;
        _requireRowLevelLocking = paymentLifecycleSettings.Value.RequireRowLevelLocking;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    private bool IsPostgresProvider() => _dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
    private bool IsSqlServerProvider() => _dbContext.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;

    private static bool PaymentMethodUsesProviderGateway(string? paymentMethodCode) =>
        paymentMethodCode?.Trim().ToUpperInvariant() is "STRIPE" or "PAYMOB" or "FAWRY" or "MANUALWALLET";

    private static string? GetCompatibilityField(CheckoutPaymentSession session, string fieldName) =>
        session.CompatibilityFields.TryGetValue(fieldName, out var value)
            ? value
            : null;

    /// <inheritdoc />
    public async Task<CheckoutResponseDto> ProcessCheckoutAsync(
        Guid tenantId,
        string ownerId,
        string ownerType,
        string? sessionId,
        Guid? customerId,
        string requestId,
        CheckoutRequestDto request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        DomainGuard.NonEmpty(
            tenantId,
            "CHECKOUT_TENANT_REQUIRED",
            "Checkout requires a valid tenant context.");
        ownerId = DomainGuard.Required(ownerId, "CHECKOUT_OWNER_REQUIRED", "Checkout requires a valid owner identifier.");
        ownerType = DomainGuard.Required(ownerType, "CHECKOUT_OWNER_TYPE_REQUIRED", "Checkout requires a valid owner type.");
        requestId = DomainGuard.Required(requestId, "CHECKOUT_REQUEST_ID_REQUIRED", "Checkout requires a request identifier.");
        correlationId = DomainGuard.Required(correlationId, "CHECKOUT_CORRELATION_REQUIRED", "Checkout requires a correlation identifier.");
        DomainGuard.Against(
            request == null,
            "CHECKOUT_REQUEST_REQUIRED",
            "Checkout requires a request payload.");

        // CRITICAL: Validate DTO members before they're accessed for idempotency/locking
        DomainGuard.NonEmpty(
            request.CartId,
            "CHECKOUT_CART_ID_REQUIRED",
            "Checkout requires a valid cart identifier.");
        DomainGuard.Against(
            string.IsNullOrWhiteSpace(request.PaymentMethod),
            "CHECKOUT_PAYMENT_METHOD_REQUIRED",
            "Checkout requires a payment method.");
        DomainGuard.Against(
            request.ShippingAddress == null,
            "CHECKOUT_SHIPPING_ADDRESS_REQUIRED",
            "Checkout requires a shipping address.");
        DomainGuard.Against(
            string.IsNullOrWhiteSpace(request.ShippingAddress.Country),
            "CHECKOUT_SHIPPING_COUNTRY_REQUIRED",
            "Shipping address requires a country.");
        
        // Validate billing address if provided (used in idempotency hashing)
        if (request.BillingAddress != null)
        {
            DomainGuard.Against(
                string.IsNullOrWhiteSpace(request.BillingAddress.Country),
                "CHECKOUT_BILLING_COUNTRY_REQUIRED",
                "Billing address requires a country.");
        }
        
        DomainGuard.NonEmpty(
            request.ShippingMethodId,
            "CHECKOUT_SHIPPING_METHOD_REQUIRED",
            "Checkout requires a shipping method.");

        var stopwatch = Stopwatch.StartNew();
        var steps = new List<CheckoutStepResult>();
        var context = new CheckoutContext();
        var clientSuppliedRequestId = requestId;
        var checkoutId = BuildPreSnapshotCheckoutLockId(tenantId, request.CartId, ownerId, ownerType, sessionId, customerId);
        ICheckoutExecutionLockLease? lockLease = null;
        OrderRequestIdempotency? idempotencyRecord = null;
        Guid? reservationId = null;
        var plannedOrderId = Guid.NewGuid();
        Guid? persistedOrderId = null;
        bool orderCommitted = false;
        string activeStep = "Initialization"; // Track the currently executing step
        PaymentMethodDocument? paymentMethod = null; // Declared here for telemetry in catch blocks
        string? canonicalPaymentMethodCode = null; // Canonical payment method code for consistent telemetry

        void EnsureLockOwnership()
        {
            lockLease?.ThrowIfOwnershipLost();
        }

        async Task<CheckoutResponseDto?> TryRecoverPaymentIntentPostCommitFailureAsync(Exception ex)
        {
            if (!orderCommitted)
            {
                return null;
            }

            var recoveryOrderId = persistedOrderId ?? plannedOrderId;

            if (!string.Equals(activeStep, "PaymentIntent", StringComparison.Ordinal))
            {
                return null;
            }

            try
            {
                var recoveryResponse = await BuildReplayResponseFromExistingOrderAsync(
                    tenantId,
                    recoveryOrderId,
                    sessionId,
                    customerId,
                    request,
                    checkoutId,
                    correlationId,
                    cancellationToken);

                if (!IsPostCommitPaymentRecoveryResponseSafe(recoveryResponse))
                {
                    _logger.LogWarning(
                        ex,
                        "Checkout post-commit payment intent recovery did not produce a safe provider handoff | OrderId: {OrderId} | TenantId: {TenantId} | PaymentMethod: {PaymentMethod} | PaymentIntentIdPresent: {PaymentIntentIdPresent} | PaymobHandoffPresent: {PaymobHandoffPresent} | StripeHandoffPresent: {StripeHandoffPresent} | CorrelationId: {CorrelationId}",
                        recoveryOrderId,
                        tenantId,
                        recoveryResponse.PaymentMethodCode,
                        recoveryResponse.PaymentIntentId.HasValue,
                        HasPaymobCheckoutHandoff(recoveryResponse),
                        HasStripeCheckoutHandoff(recoveryResponse),
                        correlationId);

                    return null;
                }

                _logger.LogWarning(
                    ex,
                    "Checkout recovered payment intent response after post-commit failure | OrderId: {OrderId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                    recoveryOrderId,
                    tenantId,
                    correlationId);

                return recoveryResponse;
            }
            catch (Exception recoveryException)
            {
                _logger.LogError(
                    recoveryException,
                    "Checkout post-commit payment intent recovery failed | OrderId: {OrderId} | TenantId: {TenantId} | Step: {Step} | CorrelationId: {CorrelationId}",
                    recoveryOrderId,
                    tenantId,
                    activeStep,
                    correlationId);
            }

            return null;
        }

        async Task<bool> TryCloseUnrecoverablePaymentIntentGapAsync(Exception ex)
        {
            if (!orderCommitted ||
                !string.Equals(activeStep, "PaymentIntent", StringComparison.Ordinal))
            {
                return false;
            }

            var recoveryOrderId = persistedOrderId ?? plannedOrderId;

            if (!IsLocalPaymentIntentFailure(ex))
            {
                _logger.LogError(
                    ex,
                    "Checkout payment intent outcome is unknown after order commit; leaving order for reconciliation recovery instead of cancelling | TenantId: {TenantId} | OrderId: {OrderId} | PaymentMethod: {PaymentMethod} | CorrelationId: {CorrelationId}",
                    tenantId,
                    recoveryOrderId,
                    paymentMethod?.Code ?? canonicalPaymentMethodCode ?? request.PaymentMethod,
                    correlationId);
                return false;
            }

            try
            {
                _dbContext.ChangeTracker.Clear();

                await using var transaction = await _dbContext.Database.BeginTransactionAsync(CancellationToken.None);

                // CRITICAL: Acquire row lock on Order before checking PaymentIntents to prevent
                // race condition where concurrent payment worker creates PaymentIntent between
                // the check and compensation.
                var trackedOrder = await LoadOrderForCompensationAsync(
                    tenantId,
                    recoveryOrderId,
                    CancellationToken.None);

                if (trackedOrder == null ||
                    trackedOrder.CheckoutState != CheckoutState.PendingPaymentIntent)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    return false;
                }

                // Re-check for PaymentIntents while holding the Order row lock
                var paymentIntentExists = await _dbContext.PaymentIntents
                    .AsNoTracking()
                    .AnyAsync(
                        item => item.TenantId == tenantId &&
                                item.OrderId == recoveryOrderId &&
                                !item.IsDeleted,
                        CancellationToken.None);

                if (paymentIntentExists)
                {
                    // Concurrent writer won - they created the PaymentIntent
                    await transaction.RollbackAsync(CancellationToken.None);
                    return false;
                }

                var nowUtc = DateTime.UtcNow;
                await _variantStockRepository.ReleaseOrderReservationsAsync(
                    tenantId,
                    recoveryOrderId,
                    CancellationToken.None);

                await _couponValidationService.ReleaseCouponReservationAsync(
                    tenantId,
                    recoveryOrderId,
                    correlationId,
                    "CheckoutPaymentIntentCreationFailed",
                    CancellationToken.None);

                trackedOrder.TransitionCheckoutState(CheckoutState.Failed);
                using var orderStatusScope = OrderStatusSaveChangesInterceptor.BeginAuthorizedContext();
                trackedOrder.Status = OrderStatus.Cancelled;
                trackedOrder.CancelledAt = nowUtc;
                trackedOrder.CancellationReason =
                    "Checkout payment intent could not be created after the order was committed.";
                trackedOrder.UpdatedAt = nowUtc;

                await _eventPublisher.PublishAsync(new OrderCancelledEvent
                {
                    TenantId = tenantId,
                    OrderId = trackedOrder.Id,
                    OrderNumber = trackedOrder.OrderNumber,
                    CancellationReason = trackedOrder.CancellationReason,
                    CancelledAt = nowUtc,
                    RestoreInventory = false,
                    CorrelationId = correlationId,
                    IdempotencyKey = $"checkout-payment-intent-gap:{tenantId:N}:{trackedOrder.Id:N}",
                    OccurredAtUtc = nowUtc,
                    EventVersion = ProductEventVersionHelper.ToProductEventVersion(
                        Math.Max(1L, trackedOrder.ProjectionMutationVersion + 1))
                }, CancellationToken.None);

                await EmitCheckoutFailedEventAsync(
                    tenantId,
                    trackedOrder.Id,
                    request.CartId,
                    "PaymentIntent",
                    "PAYMENT_INTENT_CREATION_UNRECOVERABLE",
                    "Checkout payment intent could not be created after the order was committed.",
                    inventoryReserved: false,
                    stopwatch.ElapsedMilliseconds,
                    requestId,
                    correlationId,
                    CancellationToken.None);

                await _dbContext.SaveChangesAsync(CancellationToken.None);
                await transaction.CommitAsync(CancellationToken.None);

                _logger.LogCritical(
                    ex,
                    "Checkout closed unrecoverable post-commit payment intent gap by failing checkout and releasing reservations | TenantId: {TenantId} | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                    tenantId,
                    trackedOrder.Id,
                    correlationId);

                return true;
            }
            catch (Exception closeException)
            {
                _logger.LogError(
                    closeException,
                    "Checkout failed to close unrecoverable post-commit payment intent gap | TenantId: {TenantId} | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                    tenantId,
                    recoveryOrderId,
                    correlationId);
                return false;
            }
        }

        /// <summary>
        /// Loads an order with a database row lock for compensation operations.
        /// Prevents race conditions with concurrent payment workers.
        /// </summary>
        async Task<Order?> LoadOrderForCompensationAsync(
            Guid tenantId,
            Guid orderId,
            CancellationToken cancellationToken)
        {
            if (IsPostgresProvider())
            {
                return await _dbContext.Orders
                    .FromSqlInterpolated($@"
                        SELECT *
                        FROM ""Orders""
                        WHERE ""Id"" = {orderId}
                          AND ""TenantId"" = {tenantId}
                          AND ""IsDeleted"" = FALSE
                        FOR UPDATE")
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (IsSqlServerProvider())
            {
                return await _dbContext.Orders
                    .FromSqlInterpolated($@"
                        SELECT *
                        FROM [Orders] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                        WHERE [Id] = {orderId}
                          AND [TenantId] = {tenantId}
                          AND [IsDeleted] = 0")
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(cancellationToken);
            }

            var providerName = _dbContext.Database.ProviderName ?? "Unknown";
            throw new InvalidOperationException(
                $"Database provider '{providerName}' is not supported for checkout compensation flows. " +
                "Row-level locking is required to prevent race conditions with concurrent payment workers.");
        }

        _logger.LogInformation(
            "Checkout starting | TenantId: {TenantId} | CartId: {CartId} | OwnerId: {OwnerId} | ServerLockId: {CheckoutId} | ClientRequestId: {ClientRequestId} | CorrelationId: {CorrelationId}",
            tenantId, request.CartId, ownerId, checkoutId, clientSuppliedRequestId, correlationId);

        try
        {
            activeStep = "CallerValidation";
            customerId = await ValidateCheckoutCallerOwnershipAsync(
                tenantId,
                ownerId,
                ownerType,
                sessionId,
                customerId,
                correlationId,
                cancellationToken);

            // =================================================================
            // STEP 0.5: Durable idempotency replay lookup before cart read
            // =================================================================
            // The client request id is compatibility-only. The durable checkout
            // key is normally derived from tenant/cart/canonical owner/snapshot,
            // but a retry after success may arrive after Redis cart expiry/clear.
            // Recover completed records by the deterministic request hash before
            // depending on volatile cart state.
            activeStep = "IdempotencyPreparation";

            string? normalizedProofKeyForHash = request.ProofFileKey;
            if (request.PaymentMethod.Equals("MANUALWALLET", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(request.ProofFileKey))
            {
                if (!ManualWalletProofStorage.IsExternalAbsoluteUrl(request.ProofFileKey) &&
                    ManualWalletProofStorage.TryNormalizeTenantScopedObjectKey(
                        request.ProofFileKey,
                        tenantId,
                        _r2Options,
                        out var normalizedKey))
                {
                    normalizedProofKeyForHash = normalizedKey;
                }
            }

            var requestHash = BuildCheckoutRequestHash(
                request, ownerId, ownerType, sessionId, customerId, normalizedProofKeyForHash);

            activeStep = "DurableIdempotencyReplay";
            var durableReplay = await _checkoutRequestIdempotencyCoordinator.TryClaimAsync(
                tenantId,
                checkoutId,
                requestHash,
                snapshotHash: null,
                cancellationToken);

            steps.Add(new CheckoutStepResult
            {
                StepName = "DurableIdempotencyReplay",
                Success = true,
                DurationMs = stopwatch.ElapsedMilliseconds
            });

            switch (durableReplay.Status)
            {
                case CheckoutIdempotencyClaimStatus.Replay:
                    _reliabilityTelemetry?.RecordCheckoutIdempotencyReuse("pre_cart_replay");
                    return await EnrichReplayResponseWithFreshTokensAsync(
                        tenantId,
                        durableReplay.ReplayResponse!,
                        sessionId,
                        customerId,
                        request,
                        durableReplay.CheckoutId ?? checkoutId,
                        correlationId,
                        cancellationToken);
                case CheckoutIdempotencyClaimStatus.ExistingOrder:
                    _reliabilityTelemetry?.RecordCheckoutIdempotencyReuse("pre_cart_existing_order");
                    return await BuildReplayResponseFromExistingOrderAsync(
                        tenantId,
                        durableReplay.ExistingOrderId!.Value,
                        sessionId,
                        customerId,
                        request,
                        durableReplay.CheckoutId ?? checkoutId,
                        correlationId,
                        cancellationToken);
                case CheckoutIdempotencyClaimStatus.PayloadConflict:
                    throw new CheckoutIdempotencyConflictException(
                        durableReplay.CheckoutId ?? checkoutId,
                        durableReplay.ConflictMessage);
                case CheckoutIdempotencyClaimStatus.InProgress:
                    throw new CheckoutIdempotencyInProgressException(durableReplay.CheckoutId ?? checkoutId);
            }

            // Completed historical requests above are read-only replays. New
            // Fawry checkout admission stops here before locks, cart mutation,
            // idempotency claims, events, orders, reservations, or provider work.
            if (PointOfSale.Core.Payments.FawryFinancialAuthorityPolicy.IsFawry(request.PaymentMethod))
            {
                throw new CheckoutFawryFinancialAuthorityException(request.PaymentMethod);
            }

            // Completed idempotent replays above do not create new authority.
            // Any new checkout execution must prove SQL StoreConfig is live.
            activeStep = "StorefrontAvailability";
            await EnsureStorefrontAcceptingOrdersAsync(
                tenantId,
                correlationId,
                customerId,
                !string.IsNullOrWhiteSpace(request.AffiliateReferralCode),
                lockAuthorityRow: false,
                cancellationToken);

            activeStep = "CheckoutDynamicFieldValidation";
            await ValidateCheckoutFieldDefinitionsAsync(
                tenantId,
                ownerId,
                ownerType,
                sessionId,
                customerId,
                request,
                orderId: null,
                cancellationToken);

            steps.Add(new CheckoutStepResult
            {
                StepName = "StorefrontAvailability",
                Success = true,
                DurationMs = stopwatch.ElapsedMilliseconds
            });

            // =================================================================
            // STEP 1: Acquire owned checkout lock lease
            // =================================================================
            activeStep = "AcquireLock";
            lockLease = await _checkoutExecutionLockManager.TryAcquireAsync(
                tenantId,
                request.CartId,
                checkoutId,
                correlationId,
                cancellationToken);

            steps.Add(new CheckoutStepResult
            {
                StepName = "AcquireLock",
                Success = lockLease != null,
                DurationMs = stopwatch.ElapsedMilliseconds
            });

            if (lockLease == null)
            {
                _logger.LogInformation(
                    "Checkout cart lock contention | TenantId: {TenantId} | CartId: {CartId} | CheckoutId: {CheckoutId} | CorrelationId: {CorrelationId}",
                    tenantId,
                    request.CartId,
                    checkoutId,
                    correlationId);

                await EmitCheckoutFailedEventAsync(
                    tenantId, Guid.Empty, request.CartId, "AcquireLock",
                    "CHECKOUT_IN_PROGRESS", "Another checkout is in progress",
                    false, stopwatch.ElapsedMilliseconds, checkoutId, correlationId, cancellationToken);

                throw new CheckoutLockException(request.CartId);
            }

            EnsureLockOwnership();

            // =================================================================
            // STEP 1.5: Server-side idempotency preparation
            // =================================================================
            EnsureLockOwnership();

            // =================================================================
            // STEP 2: Cart Snapshot
            // =================================================================
            activeStep = "CartSnapshot";
            var cartSnapshot = await GetCartSnapshotAsync(
                tenantId, ownerId, ownerType, request.CartId, correlationId, cancellationToken);
            steps.Add(new CheckoutStepResult
            {
                StepName = "CartSnapshot",
                Success = true,
                DurationMs = stopwatch.ElapsedMilliseconds
            });

            _logger.LogDebug(
                "Cart snapshot taken | CartId: {CartId} | Items: {ItemCount} | CorrelationId: {CorrelationId}",
                cartSnapshot.CartId, cartSnapshot.Items.Count, correlationId);

            var warehouseResolutionValidation = await _warehouseResolutionService.ValidateSubmittedResolutionAsync(
                tenantId,
                ownerId,
                ownerType,
                customerId,
                sessionId,
                cartSnapshot,
                request,
                correlationId,
                lockForCheckout: false,
                cancellationToken);

            // =================================================================
            // STEP 2.5: Claim persistent idempotency against the exact cart snapshot
            // =================================================================
            activeStep = "IdempotencyClaim";
            var snapshotHash = CheckoutCartIdempotencySnapshotBuilder.BuildHash(
                cartSnapshot,
                request,
                warehouseResolutionValidation.WarehouseId);

            checkoutId = BuildServerCheckoutIdempotencyKey(
                tenantId,
                request.CartId,
                ownerId,
                ownerType,
                sessionId,
                customerId,
                snapshotHash);
            requestId = checkoutId;

            await PersistCartCheckoutIdempotencyAsync(
                tenantId,
                ownerId,
                ownerType,
                request.CartId,
                checkoutId,
                snapshotHash,
                correlationId,
                cancellationToken);

            var idempotencyClaim = await _checkoutRequestIdempotencyCoordinator.TryClaimAsync(
                tenantId,
                checkoutId,
                requestHash,
                snapshotHash,
                cancellationToken);

            steps.Add(new CheckoutStepResult
            {
                StepName = "IdempotencyClaim",
                Success = idempotencyClaim.Status is CheckoutIdempotencyClaimStatus.Claimed or
                    CheckoutIdempotencyClaimStatus.Replay or
                    CheckoutIdempotencyClaimStatus.ExistingOrder,
                ErrorMessage = idempotencyClaim.ConflictMessage,
                DurationMs = stopwatch.ElapsedMilliseconds
            });

            switch (idempotencyClaim.Status)
            {
                case CheckoutIdempotencyClaimStatus.Replay:
                    _reliabilityTelemetry?.RecordCheckoutIdempotencyReuse("replay");
                    _logger.LogInformation(
                        "Checkout idempotent replay | TenantId: {TenantId} | CartId: {CartId} | CheckoutId: {CheckoutId} | OrderId: {OrderId} | SnapshotHash: {SnapshotHash} | CorrelationId: {CorrelationId}",
                        tenantId,
                        request.CartId,
                        checkoutId,
                        idempotencyClaim.ReplayResponse!.OrderId,
                        snapshotHash,
                        correlationId);
                    return await EnrichReplayResponseWithFreshTokensAsync(
                        tenantId, idempotencyClaim.ReplayResponse!, sessionId, customerId, request, checkoutId, correlationId, cancellationToken);
                case CheckoutIdempotencyClaimStatus.ExistingOrder:
                    _reliabilityTelemetry?.RecordCheckoutIdempotencyReuse("existing_order");
                    return await BuildReplayResponseFromExistingOrderAsync(
                        tenantId,
                        idempotencyClaim.ExistingOrderId!.Value,
                        sessionId,
                        customerId,
                        request,
                        checkoutId,
                        correlationId,
                        cancellationToken);
                case CheckoutIdempotencyClaimStatus.PayloadConflict:
                    throw new CheckoutIdempotencyConflictException(
                        checkoutId,
                        idempotencyClaim.ConflictMessage);
                case CheckoutIdempotencyClaimStatus.InProgress:
                    throw new CheckoutIdempotencyInProgressException(checkoutId);
                case CheckoutIdempotencyClaimStatus.Claimed:
                    idempotencyRecord = idempotencyClaim.Record!;
                    break;
            }

            // =================================================================
            // STEP 2.7: Existing active order guard
            // =================================================================
            activeStep = "DuplicateOrderGuard";
            var existingActiveOrder = await FindActiveOrderForCartAsync(
                tenantId,
                request.CartId,
                cancellationToken);
            if (existingActiveOrder != null)
            {
                _reliabilityTelemetry?.RecordCheckoutDuplicateOrderPrevented("pre_create_guard");

                var existingOrderReplayResponse = await BuildReplayResponseFromExistingOrderAsync(
                    tenantId,
                    existingActiveOrder.Id,
                    sessionId,
                    customerId,
                    request,
                    checkoutId,
                    correlationId,
                    cancellationToken);

                await _checkoutRequestIdempotencyCoordinator.MarkOrderCreatedAsync(
                    idempotencyRecord!,
                    existingActiveOrder.Id,
                    cancellationToken);

                await _checkoutRequestIdempotencyCoordinator.CompleteAsync(
                    idempotencyRecord!,
                    existingOrderReplayResponse,
                    cancellationToken);

                return existingOrderReplayResponse;
            }

            // =================================================================
            // STEP 2.9: Merchant Phone Blocking
            // =================================================================
            activeStep = "PhoneBlockValidation";
            var normalizedCheckoutPhone = await _phoneRiskProfileService.NormalizePhoneAsync(
                tenantId,
                request.ShippingAddress.Phone,
                cancellationToken);

            var isPhoneBlocked = await _phoneRiskProfileService.IsBlockedAsync(
                tenantId,
                normalizedCheckoutPhone,
                cancellationToken);

            steps.Add(new CheckoutStepResult
            {
                StepName = "PhoneBlockValidation",
                Success = !isPhoneBlocked,
                DurationMs = stopwatch.ElapsedMilliseconds
            });

            if (isPhoneBlocked)
            {
                _logger.LogWarning(
                    "Checkout blocked by phone policy | TenantId: {TenantId} | Phone: {Phone} | CartId: {CartId} | CorrelationId: {CorrelationId}",
                    tenantId,
                    MaskPhone(normalizedCheckoutPhone),
                    request.CartId,
                    correlationId);

                throw new CheckoutPhoneBlockedException(normalizedCheckoutPhone);
            }

            activeStep = "AddressGeographicAdmission";
            var admittedGeography = await AdmitCheckoutGeographyAsync(
                tenantId,
                request.CartId,
                request.ShippingAddress,
                request.BillingAddress,
                correlationId,
                cancellationToken,
                $"{ownerType}:{ownerId}");

            steps.Add(new CheckoutStepResult
            {
                StepName = "AddressGeographicAdmission",
                Success = true,
                DurationMs = stopwatch.ElapsedMilliseconds
            });

            // =================================================================
            // AF-12: CRITICAL SECURITY - Validate Affiliate Mode Eligibility
            // =================================================================
            // If checkout is happening in Affiliate Mode, re-validate that ALL
            // cart items are affiliate-enabled. This prevents commission leakage
            // if a non-affiliate product was injected into the cart.
            // =================================================================
            activeStep = "AffiliateValidation";
            var affiliateMode = _httpContextAccessor.HttpContext != null 
                ? AffiliateModeResolver.IsAffiliateMode(_httpContextAccessor.HttpContext)
                : false;

            if (affiliateMode)
            {
                var authoritativeAffiliateEligibleProductIds = await LoadAffiliateEligibleProductIdsAsync(
                    tenantId,
                    cartSnapshot.Items.Select(item => item.ProductId).ToList(),
                    cancellationToken);

                _logger.LogInformation(
                    "[AffiliateMode] Validating cart items for affiliate eligibility | TenantId: {TenantId} | CartId: {CartId} | ItemCount: {ItemCount} | CorrelationId: {CorrelationId}",
                    tenantId, cartSnapshot.CartId, cartSnapshot.Items.Count, correlationId);
                
                foreach (var item in cartSnapshot.Items)
                {
                    if (!authoritativeAffiliateEligibleProductIds.Contains(item.ProductId))
                    {
                        _logger.LogError(
                            "[AffiliateMode] Checkout blocked - product is not affiliate-eligible in SQL authority | TenantId: {TenantId} | ProductId: {ProductId} | ProductName: {ProductName} | CartId: {CartId} | CorrelationId: {CorrelationId}",
                            tenantId, item.ProductId, item.Name, cartSnapshot.CartId, correlationId);
                        
                        await EmitCheckoutFailedEventAsync(
                            tenantId, Guid.Empty, request.CartId, "AffiliateValidation",
                            "PRODUCT_NOT_ELIGIBLE", $"Product '{item.Name}' is not eligible for affiliate checkout",
                            false, stopwatch.ElapsedMilliseconds, requestId, correlationId, cancellationToken);
                        
                        throw new CheckoutCartInvalidException(
                            cartSnapshot.CartId, 
                            $"Product '{item.Name}' is not eligible for affiliate checkout");
                    }
                }
                
                _logger.LogInformation(
                    "[AffiliateMode] All cart items validated successfully | TenantId: {TenantId} | CartId: {CartId} | CorrelationId: {CorrelationId}",
                    tenantId, cartSnapshot.CartId, correlationId);
                
                steps.Add(new CheckoutStepResult
                {
                    StepName = "AffiliateValidation",
                    Success = true,
                    DurationMs = stopwatch.ElapsedMilliseconds
                });
            }

            // Emit CheckoutStartedEvent
            await EmitCheckoutStartedEventAsync(
                tenantId, request.CartId, request.ShippingMethodId, request.PaymentMethod,
                ownerType, ownerId, cartSnapshot.Items.Count, requestId, correlationId, cancellationToken);

            // =================================================================
            // SMART ABANDONED CART RECOVERY: Track Cart Activity
            // =================================================================
            // Track this checkout attempt for abandoned cart detection.
            // If customer doesn't complete checkout, scanner job will trigger recovery.
            await EmitCartActivityEventAsync(
                tenantId, request.CartId, sessionId, customerId,
                request.ShippingAddress?.Phone, request.ShippingAddress?.Email,
                cartSnapshot, correlationId, cancellationToken);

            EnsureLockOwnership();

            // =================================================================
            // STEP 4: Price Recalculation
            // =================================================================
            activeStep = "PriceRecalculation";
            var (recalculatedSnapshot, pricesRefreshed) = await RecalculatePricesAsync(
                cartSnapshot, tenantId, requestId, correlationId, cancellationToken);
            steps.Add(new CheckoutStepResult
            {
                StepName = "PriceRecalculation",
                Success = true,
                DurationMs = stopwatch.ElapsedMilliseconds
            });

            if (pricesRefreshed)
            {
                _logger.LogInformation(
                    "Checkout pricing refreshed from authoritative backend values | CartId: {CartId} | CorrelationId: {CorrelationId}",
                    request.CartId,
                    correlationId);
            }

            activeStep = "CouponValidation";
            context.PromotionCalculationFailed = await ReconcileCheckoutDiscountAuthorityAsync(
                tenantId,
                customerId,
                recalculatedSnapshot,
                request.CartId,
                correlationId,
                cancellationToken,
                mode: "checkout",
                steps,
                stopwatch,
                requestId,
                stepName => activeStep = stepName);

            EnsureLockOwnership();

            // =================================================================
            // STEP 5: Validate Shipping and Payment Methods
            // =================================================================
            string? manualWalletProofFileKey = null;
            activeStep = "ShippingMethodValidation";
            var shippingMethod = await ValidateShippingMethodAsync(
                request.ShippingMethodId, tenantId, correlationId, cancellationToken);
            if (CarrierCodes.Normalize(shippingMethod.CarrierCode) == CarrierCodes.Bosta)
            {
                var destinationFailure = BostaDestinationAdmission.ValidateCheckoutAddress(shippingMethod.ServiceLevel,
                    request.ShippingAddress.Country, request.ShippingAddress.Street);
                if (destinationFailure != null) throw new CheckoutException(destinationFailure, "Bosta requires a domestic Egypt delivery address; provider destination selection is required before booking.", "ShippingMethodValidation");
            }

            // Validate payment method from TenantPaymentMethod (Phase 10.4.D3)
            activeStep = "PaymentMethodValidation";
            paymentMethod = await ValidatePaymentMethodAsync(
                request.PaymentMethod, tenantId,
                correlationId, cancellationToken);
            
            // Compute canonical payment method code for consistent telemetry across success and failure paths
            canonicalPaymentMethodCode = paymentMethod.Code;
            
            steps.Add(new CheckoutStepResult
            {
                StepName = "PaymentMethodValidation",
                Success = true,
                DurationMs = stopwatch.ElapsedMilliseconds
            });

            if (request.PaymentMethod.Equals("MANUALWALLET", StringComparison.OrdinalIgnoreCase))
            {
                manualWalletProofFileKey = await ResolveValidatedManualWalletProofFileKeyAsync(
                    tenantId,
                    ownerId,
                    ownerType,
                    sessionId,
                    customerId,
                    request.CartId,
                    request.ProofFileKey,
                    orderId: null,
                    cancellationToken);
            }

            // =================================================================
            // STEP 5.5: Currency Validation (PHASE 10.4.X)
            // =================================================================
            // CURRENCY AUTHORITY LOCKDOWN: Cart snapshot must have valid currency
            // Currency comes from StorefrontConfig → Cart → Snapshot → Order
            // The final currency validation happens in payment gateway (Stripe/Paymob)
            // where we verify Order.Currency matches Tenant.Currency
            
            activeStep = "CurrencyValidation";
            recalculatedSnapshot.Currency = await EnsureCheckoutCurrencyAsync(
                tenantId,
                recalculatedSnapshot.Currency,
                "checkout.snapshot.currency",
                "CurrencyValidation",
                "Cart currency is not configured or does not match the tenant currency. Please refresh and try again.",
                orderId: null,
                source: "checkout",
                cancellationToken);

            // NOTE: Shipping and Payment methods do not have currency fields
            // The primary currency flow is: StorefrontConfig → Cart → Order
            // Final validation: Order.Currency === Tenant.Currency happens in payment gateway

            steps.Add(new CheckoutStepResult
            {
                StepName = "CurrencyValidation",
                Success = true,
                DurationMs = stopwatch.ElapsedMilliseconds
            });

            _logger.LogInformation(
                "Currency validated | Currency: {Currency} | CorrelationId: {CorrelationId}",
                recalculatedSnapshot.Currency, correlationId);

            activeStep = "PaymentAmountValidation";
            var preReservationPricing = await _storefrontPricingService.CalculateCheckoutAsync(
                tenantId,
                recalculatedSnapshot,
                shippingMethod,
                paymentMethod,
                request.ShippingAddress?.Country,
                cancellationToken);

            try
            {
                ValidatePaymentMethodOrderAmountLimits(paymentMethod, preReservationPricing.FinalTotal, correlationId);
                steps.Add(new CheckoutStepResult
                {
                    StepName = "PaymentAmountValidation",
                    Success = true,
                    DurationMs = stopwatch.ElapsedMilliseconds
                });
            }
            catch (CheckoutException ex)
            {
                steps.Add(new CheckoutStepResult
                {
                    StepName = "PaymentAmountValidation",
                    Success = false,
                    DurationMs = stopwatch.ElapsedMilliseconds
                });

                await EmitCheckoutFailedEventAsync(
                    tenantId,
                    Guid.Empty,
                    request.CartId,
                    "PaymentAmountValidation",
                    ex.ErrorCode,
                    ex.Message,
                    false,
                    stopwatch.ElapsedMilliseconds,
                    checkoutId,
                    correlationId,
                    cancellationToken);

                throw;
            }

            // =================================================================
            // STEP 5.6: Phone OTP Verification (Conditional)
            // =================================================================
            // ONLY blocks if:
            // - Tenant has WhatsApp OTP enabled
            // - Phone verification service is available
            // - Phone is NOT verified for this session
            // =================================================================
            activeStep = "PhoneOtpVerification";
            if (_phoneVerificationService != null)
            {
                var phoneToVerify = request.ShippingAddress?.Phone;
                if (!string.IsNullOrWhiteSpace(phoneToVerify))
                {
                    // Check if phone OTP is required and not verified
                    var isVerified = await _phoneVerificationService.IsPhoneVerifiedForSessionAsync(
                        tenantId, ownerId, phoneToVerify, cancellationToken);
                    
                    if (!isVerified)
                    {
                        // Check if OTP is enabled for this tenant (conditional skip)
                        var verificationStatus = await _phoneVerificationService.CheckVerificationRequiredAsync(
                            tenantId, ownerId, phoneToVerify, customerId, cancellationToken);
                        
                        if (verificationStatus.IsRequired && !verificationStatus.IsVerified)
                        {
                            _logger.LogWarning(
                                "[Checkout] Phone verification required but not verified | TenantId: {TenantId} | Phone: {Phone} | CorrelationId: {CorrelationId}",
                                tenantId, MaskPhone(phoneToVerify), correlationId);
                            
                            await EmitCheckoutFailedEventAsync(
                                tenantId, Guid.Empty, request.CartId, "PhoneVerification",
                                "PHONE_VERIFICATION_REQUIRED", "Please verify your phone number via WhatsApp OTP before placing the order",
                                false, stopwatch.ElapsedMilliseconds, requestId, correlationId, cancellationToken);
                            
                            throw new CheckoutException(
                                "PHONE_VERIFICATION_REQUIRED",
                                "Please verify your phone number via WhatsApp OTP before placing the order. Use the phone verification endpoints.",
                                "PhoneVerification");
                        }
                    }
                    
                    steps.Add(new CheckoutStepResult
                    {
                        StepName = "PhoneOtpVerification",
                        Success = true,
                        DurationMs = stopwatch.ElapsedMilliseconds
                    });
                    
                    _logger.LogDebug(
                        "[Checkout] Phone verification passed | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                        tenantId, correlationId);
                }
            }

            // =================================================================
            // STEP 6: Order Creation (Single PostgreSQL Transaction)
            // Inventory reservation is intentionally performed inside this transaction
            // so stock and order existence cannot diverge if the process dies mid-flow.
            // =================================================================
            activeStep = "OrderCreation";
            Order order;
            try
            {
                EnsureLockOwnership();

                var orderCreationResult = await CreateOrderAsync(
                    plannedOrderId, tenantId, customerId, ownerId, ownerType, sessionId,
                    recalculatedSnapshot, request, shippingMethod, paymentMethod,
                    admittedGeography,
                    manualWalletProofFileKey,
                    context.PromotionCalculationFailed, idempotencyRecord!,
                    requestId, correlationId, cancellationToken,
                    steps, stopwatch, stepName => activeStep = stepName);

                order = orderCreationResult.Order;
                persistedOrderId = order.Id;

                // CRITICAL: Order transaction is now committed. Set flag to prevent compensation.
                orderCommitted = true;

                steps.Add(new CheckoutStepResult
                {
                    StepName = "OrderCreation",
                    Success = true,
                    DurationMs = stopwatch.ElapsedMilliseconds
                });

                _logger.LogInformation(
                    "Order created | OrderId: {OrderId} | OrderNumber: {OrderNumber} | Total: {Total} | CorrelationId: {CorrelationId}",
                    order.Id, order.OrderNumber, order.Total, correlationId);

            }
            catch (CheckoutTotalMismatchException)
            {
                // CheckoutTotalMismatchException from EnsureExpectedPricingMatches must propagate
                // with its 409 semantics. Do NOT wrap in CheckoutOrderCreationException.
                // Release inventory reservation before rethrowing.
                if (!orderCommitted && reservationId.HasValue)
                {
                    try
                    {
                        await ReleaseInventoryReservationAsync(
                            reservationId.Value, plannedOrderId, tenantId, request.CartId, "TotalMismatch",
                            requestId, correlationId, cancellationToken, null);
                    }
                    catch (AggregateException aggEx)
                    {
                        // CRITICAL: AggregateException from ReleaseInventoryReservationAsync indicates
                        // partial compensation failure (stock released but event publish failed).
                        // This must be escalated to prevent cross-system divergence.
                        _logger.LogError(aggEx,
                            "Inventory reservation compensation partially failed during CheckoutTotalMismatchException cleanup | ReservationId: {ReservationId} | CorrelationId: {CorrelationId}",
                            reservationId.Value, correlationId);
                        throw;
                    }
                    catch (Exception cleanupEx)
                    {
                        // Log cleanup failure but do not let it replace the original CheckoutTotalMismatchException
                        _logger.LogWarning(cleanupEx,
                            "Failed to release inventory reservation during CheckoutTotalMismatchException cleanup | ReservationId: {ReservationId} | CorrelationId: {CorrelationId}",
                            reservationId.Value, correlationId);
                    }
                }
                throw;
            }
            catch (Exception ex)
            {
                if (ex is WarehouseResolutionException warehouseResolutionException)
                {
                    _logger.LogWarning(warehouseResolutionException,
                        "Checkout warehouse resolution rejected during order creation | CartId: {CartId} | CheckoutSessionId: {CheckoutSessionId} | ResolutionId: {ResolutionId} | FailurePhase: {FailurePhase} | StatusCode: {StatusCode} | ErrorCode: {ErrorCode} | CorrelationId: {CorrelationId}",
                        request.CartId,
                        request.CheckoutSessionId,
                        request.WarehouseResolutionId,
                        activeStep,
                        warehouseResolutionException.StatusCode,
                        warehouseResolutionException.ErrorCode,
                        correlationId);
                    throw;
                }

                _logger.LogError(ex,
                    "Order creation failed - rolling back | CartId: {CartId} | CorrelationId: {CorrelationId}",
                    request.CartId, correlationId);

                // Preserve DomainGuardException for middleware to handle with structured error response
                if (ex is DomainGuardException)
                {
                    throw;
                }

                if (ex is CheckoutException)
                {
                    throw;
                }

                // Only run pre-commit compensation if order was NOT committed
                if (!orderCommitted)
                {
                    // Release inventory reservation on failure
                    if (reservationId.HasValue)
                    {
                        try
                        {
                            await ReleaseInventoryReservationAsync(
                                reservationId.Value, plannedOrderId, tenantId, request.CartId, "OrderCreationFailed",
                                requestId, correlationId, cancellationToken, ex);
                        }
                        catch (AggregateException aggEx)
                        {
                            // CRITICAL: AggregateException from ReleaseInventoryReservationAsync indicates
                            // partial compensation failure (stock released but event publish failed).
                            // This must be escalated to prevent cross-system divergence.
                            _logger.LogError(aggEx,
                                "Inventory reservation compensation partially failed during order creation cleanup | ReservationId: {ReservationId} | CorrelationId: {CorrelationId}",
                                reservationId.Value, correlationId);
                            throw;
                        }
                        catch (Exception cleanupEx)
                        {
                            // Log cleanup failure but do not let it replace the original exception
                            _logger.LogWarning(cleanupEx,
                                "Failed to release inventory reservation during order creation cleanup | ReservationId: {ReservationId} | CorrelationId: {CorrelationId}",
                                reservationId.Value, correlationId);
                        }
                    }

                    try
                    {
                        await EmitCheckoutFailedEventAsync(
                            tenantId, Guid.Empty, request.CartId, "OrderCreation",
                            "ORDER_CREATION_FAILED", ex.Message,
                            true, stopwatch.ElapsedMilliseconds, requestId, correlationId, cancellationToken);
                    }
                    catch (Exception eventEx)
                    {
                        // Log event emission failure but do not let it replace the original exception
                        _logger.LogWarning(eventEx,
                            "Failed to emit checkout failed event during order creation cleanup | CorrelationId: {CorrelationId}",
                            correlationId);
                    }
                }

                throw new CheckoutOrderCreationException(ex.Message, ex);
            }

            // =================================================================
            // POST-COMMIT OPERATIONS
            // These operations run AFTER order is committed and must NOT trigger
            // pre-commit compensation logic if they fail.
            // =================================================================
            activeStep = "VelocityTracking";
            try
            {
                var velocityAssessment = await _orderVelocityService.TrackOrderAsync(
                    order.IpAddress,
                    normalizedCheckoutPhone,
                    cancellationToken);

                steps.Add(new CheckoutStepResult
                {
                    StepName = "VelocityTracking",
                    Success = true,
                    DurationMs = stopwatch.ElapsedMilliseconds
                });

                if (velocityAssessment.IpVelocityTriggered || velocityAssessment.PhoneVelocityTriggered)
                {
                    _logger.LogWarning(
                        "Checkout velocity signal detected | OrderId: {OrderId} | IpTriggered: {IpTriggered} | PhoneTriggered: {PhoneTriggered} | IpCount: {IpCount} | PhoneCount: {PhoneCount} | CorrelationId: {CorrelationId}",
                        order.Id,
                        velocityAssessment.IpVelocityTriggered,
                        velocityAssessment.PhoneVelocityTriggered,
                        velocityAssessment.IpOrderCount,
                        velocityAssessment.PhoneOrderCount,
                        correlationId);
                }
            }
            catch (Exception ex)
            {
                // Post-commit operations failed, but order already exists.
                // Log warning and continue - do NOT run compensation logic.
                _logger.LogWarning(
                    ex,
                    "Checkout post-commit failure | OrderId: {OrderId} | Step: {Step} | CorrelationId: {CorrelationId}",
                    order.Id,
                    "VelocityTracking",
                    correlationId);
            }

            // =================================================================
            // STEP 7: Payment Intent (Stripe or Generic)
            // =================================================================
            activeStep = "PaymentIntent";
            EnsureLockOwnership();

            string? paymentReference = null;
            string? redirectUrl = null;
            string? stripeClientSecret = null;
            string? stripePublishableKey = null;
            string? paymobIframeUrl = null;
            string? paymobPaymentKey = null;
            string? referenceCode = null;
            DateTime? expiresAtUtc = null;
            string? checkoutToken = null;
            Guid? paymentIntentId = null;
            
            // ManualWallet fields
            bool isManualWallet = false;
            string? merchantWalletNumber = null;
            string? walletType = null;
            string? walletInstructions = null;

            if (order.IsGuestCheckout ||
                paymentMethod.Code.Equals("STRIPE", StringComparison.OrdinalIgnoreCase) ||
                paymentMethod.Code.Equals("PAYMOB", StringComparison.OrdinalIgnoreCase))
            {
                checkoutToken = CreateCheckoutPaymentToken(tenantId, order.Id);
                if (string.IsNullOrWhiteSpace(checkoutToken))
                {
                    throw new CheckoutPaymentMethodException(
                        paymentMethod.Code,
                        "Checkout session token is required for recoverable payment flows.");
                }
            }

            if (PaymentMethodUsesProviderGateway(paymentMethod.Code))
            {
                if (paymentMethod.Code.Equals("MANUALWALLET", StringComparison.OrdinalIgnoreCase))
                {
                    isManualWallet = true;
                    walletType = request.WalletType;
                    var configuredWallet = await ResolveRequiredManualWalletDetailsAsync(
                        tenantId,
                        walletType,
                        order.Total,
                        order.Currency,
                        cancellationToken);
                    merchantWalletNumber = configuredWallet.MerchantWalletNumber;
                    walletInstructions = configuredWallet.WalletInstructions;
                }

                var session = await CreateCheckoutPaymentSessionAsync(
                    tenantId,
                    order,
                    paymentMethod.Code,
                    checkoutToken,
                    customerId,
                    context.PromotionCalculationFailed,
                    requestId,
                    correlationId,
                    request.WalletType,
                    request.WalletPhoneNumber,
                    manualWalletProofFileKey,
                    cancellationToken);

                paymentReference = session.PaymentReference;
                redirectUrl = session.RedirectUrl;
                referenceCode = session.ReferenceCode;
                paymentIntentId = session.PaymentIntentId;
                expiresAtUtc = session.ExpiresAtUtc;
                stripeClientSecret = GetCompatibilityField(session, nameof(CheckoutResponseDto.StripeClientSecret));
                stripePublishableKey = GetCompatibilityField(session, nameof(CheckoutResponseDto.StripePublishableKey));
                paymobIframeUrl = GetCompatibilityField(session, nameof(CheckoutResponseDto.PaymobIframeUrl));
                paymobPaymentKey = GetCompatibilityField(session, nameof(CheckoutResponseDto.PaymobPaymentKey));

                if (paymentIntentId.HasValue && !expiresAtUtc.HasValue)
                {
                    expiresAtUtc = await ResolvePaymentIntentExpirationAsync(tenantId, paymentIntentId.Value, cancellationToken);
                }

                if (string.IsNullOrWhiteSpace(paymentReference))
                {
                    throw new CheckoutPaymentMethodException(
                        paymentMethod.Code,
                        "Payment provider returned an incomplete canonical payment session.");
                }
            }
            else
            {
                // Generic handling for other methods (e.g. COD)
                paymentReference = GeneratePaymentReference(order.Id);

                // Update order with payment reference
                order.PaymentReference = paymentReference;
                order.TransitionCheckoutState(CheckoutState.PaymentPending);

                // Add outbox message BEFORE SaveChangesAsync to ensure atomic commit
                await EmitPaymentIntentCreatedEventAsync(
                    tenantId, order.Id, request.CartId, order.OrderNumber,
                    paymentReference, paymentMethod.Code, order.Total, order.Currency,
                    redirectUrl, context.PromotionCalculationFailed, requestId, correlationId,
                    ProductEventVersionHelper.ToProductEventVersion(order.ProjectionMutationVersion + 1),
                    cancellationToken);

                // Persist state change and outbox message atomically
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            if (order.CheckoutState == CheckoutState.PendingPaymentIntent)
            {
                order.TransitionCheckoutState(CheckoutState.PaymentPending);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            steps.Add(new CheckoutStepResult
            {
                StepName = "PaymentIntent",
                Success = true,
                DurationMs = stopwatch.ElapsedMilliseconds
            });

            _logger.LogDebug(
                "Payment intent created | OrderId: {OrderId} | PaymentRef: {PaymentRef} | Method: {Method} | CorrelationId: {CorrelationId}",
                order.Id, paymentReference, paymentMethod.Code, correlationId);

            // =================================================================
            // STEP 8: Persist Idempotency Result
            // =================================================================
            activeStep = "StoreIdempotency";
            var response = new CheckoutResponseDto
            {
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                Status = order.CheckoutState.ToString(),
                TotalAmount = order.Total,
                Currency = order.Currency,
                PaymentReference = paymentReference,
                PaymentIntentId = paymentIntentId,
                PaymentStatus = order.PaymentStatus.ToString(),
                PaymentMethodCode = paymentMethod.Code,
                RedirectUrl = redirectUrl,
                CheckoutToken = checkoutToken,
                TrackingToken = ShouldIssueGuestTrackingToken(order)
                    ? CreateGuestTrackingToken(tenantId, order.Id, order.SessionId)
                    : null,
                StripeClientSecret = stripeClientSecret,
                StripePublishableKey = stripePublishableKey,
                PaymobIframeUrl = paymobIframeUrl,
                PaymobPaymentKey = paymobPaymentKey,
                CartId = request.CartId,
                PromotionCalculationFailed = context.PromotionCalculationFailed,
                // Legacy field for backward compatibility
#pragma warning disable CS0618
                PromotionWarningMessage = context.PromotionCalculationFailed
                    ? PromotionCalculationFallbackWarningMessage
                    : null,
#pragma warning restore CS0618
                // New localization-friendly fields
                PromotionWarningCode = context.PromotionCalculationFailed
                    ? PromotionCalculationFallbackWarningCode
                    : null,
                PromotionWarningParams = null, // No parameters needed for this generic warning
                Totals = new OrderTotalsDto
                {
                    Subtotal = order.Subtotal,
                    DiscountAmount = order.TotalDiscountAmount > 0m
                        ? order.TotalDiscountAmount
                        : order.DiscountAmount + order.PromotionDiscountAmount + order.ManualDiscountAmount,
                    CouponDiscountAmount = order.DiscountAmount,
                    PromotionDiscountAmount = order.PromotionDiscountAmount,
                    ManualDiscountAmount = order.ManualDiscountAmount,
                    ShippingCost = order.ShippingCost,
                    TaxAmount = order.TaxAmount,
                    PaymentFee = order.PaymentFee,
                    GrandTotal = order.Total
                },
                ReferenceCode = GetStorefrontReferenceCode(paymentMethod.Code, referenceCode),
                ExpiresAtUtc = expiresAtUtc,
                // ManualWallet fields
                IsManualWallet = isManualWallet,
                MerchantWalletNumber = merchantWalletNumber,
                WalletType = walletType,
                WalletInstructions = walletInstructions
            };

            EnsureLockOwnership();
            
            // Create a replay-safe copy without time-limited tokens
            var replayResponse = new CheckoutResponseDto
            {
                OrderId = response.OrderId,
                OrderNumber = response.OrderNumber,
                Status = response.Status,
                TotalAmount = response.TotalAmount,
                Currency = response.Currency,
                PaymentReference = response.PaymentReference,
                PaymentIntentId = response.PaymentIntentId,
                PaymentStatus = response.PaymentStatus,
                PaymentMethodCode = response.PaymentMethodCode,
                RedirectUrl = paymentMethod.Code.Equals("PAYMOB", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : response.RedirectUrl,
                CheckoutToken = null, // Will be regenerated on replay
                TrackingToken = null, // Will be regenerated on replay
                StripeClientSecret = null, // Will be regenerated on replay
                StripePublishableKey = null, // Will be regenerated on replay
                PaymobIframeUrl = null, // Will be regenerated on replay
                PaymobPaymentKey = null, // Will be regenerated on replay
                CartId = response.CartId,
                PromotionCalculationFailed = response.PromotionCalculationFailed,
#pragma warning disable CS0618
                PromotionWarningMessage = response.PromotionWarningMessage, // Legacy field
#pragma warning restore CS0618
                PromotionWarningCode = response.PromotionWarningCode,
                PromotionWarningParams = response.PromotionWarningParams,
                Totals = response.Totals,
                ReferenceCode = response.ReferenceCode,
                ExpiresAtUtc = response.ExpiresAtUtc,
                IsManualWallet = response.IsManualWallet,
                MerchantWalletNumber = response.MerchantWalletNumber,
                WalletType = response.WalletType,
                WalletInstructions = response.WalletInstructions
            };
            
            await _checkoutRequestIdempotencyCoordinator.CompleteAsync(
                idempotencyRecord!,
                replayResponse,
                cancellationToken);

            if (paymentMethod.Code.Equals("MANUALWALLET", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await _cartService.ClearCartAsync(
                        tenantId,
                        new CartOwner { OwnerType = ownerType, OwnerId = ownerId },
                        correlationId,
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    // The durable replay record is already complete. The storefront
                    // repeats this cleanup, so a transient cart outage cannot turn a
                    // committed order into a false checkout failure.
                    _logger.LogWarning(
                        ex,
                        "Manual Wallet checkout committed but server cart cleanup failed | TenantId: {TenantId} | CartId: {CartId} | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                        tenantId,
                        request.CartId,
                        order.Id,
                        correlationId);
                }
            }

            steps.Add(new CheckoutStepResult
            {
                StepName = "StoreIdempotency",
                Success = true,
                DurationMs = stopwatch.ElapsedMilliseconds
            });

            // =============================================================================
            // PHASE 13: WhatsApp Order Confirmation (Optional, Opt-In Only)
            // =============================================================================
            activeStep = "OptionalSideEffects";
            try
            {
                await TryEmitWhatsAppOrderConfirmationAsync(
                    tenantId, order, recalculatedSnapshot.Currency,
                    correlationId, cancellationToken);

                // Persist any optional post-commit outbox messages emitted after order creation.
                // Core order/coupon/promotion projection events are committed inside CreateOrderAsync.
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Checkout post-commit failure | OrderId: {OrderId} | Step: {Step} | CorrelationId: {CorrelationId}",
                    order.Id,
                    "OptionalSideEffects",
                    correlationId);
            }
/*
                        // Promotion limit reached between cart and checkout — non-fatal
                        _logger.LogWarning(
                            "[Promotions] Promotion limit reached (atomic guard) — usage not incremented | OrderId: {OrderId} | PromotionId: {PromotionId}",
                            order.Id, promotionId);
                    }
                }

*/
            stopwatch.Stop();

            _logger.LogInformation(
                "Checkout completed | TenantId: {TenantId} | CartId: {CartId} | OrderId: {OrderId} | OrderNumber: {OrderNumber} | Total: {Total} | DurationMs: {DurationMs} | CorrelationId: {CorrelationId}",
                tenantId, request.CartId, order.Id, order.OrderNumber, order.Total, stopwatch.ElapsedMilliseconds, correlationId);

            // AF-13B: Observability — Affiliate Guest Checkout Metrics
            if (order.IsGuestCheckout)
            {
                _logger.LogInformation(
                    "[AF-13B] Guest checkout completed | TenantId: {TenantId} | OrderId: {OrderId} | AffiliateMode: {AffiliateMode} | CorrelationId: {CorrelationId}",
                    tenantId, order.Id, affiliateMode, correlationId);
            }

            _reliabilityTelemetry?.RecordCheckoutProcessed(tenantId, canonicalPaymentMethodCode, "success");
            return response;
        }
        catch (CheckoutPaymentMethodException ex)
        {
            _reliabilityTelemetry?.RecordCheckoutProcessed(tenantId, canonicalPaymentMethodCode ?? request.PaymentMethod, "failed");

            var recoveryResponse = await TryRecoverPaymentIntentPostCommitFailureAsync(ex);
            if (recoveryResponse != null)
            {
                return recoveryResponse;
            }

            await TryCloseUnrecoverablePaymentIntentGapAsync(ex);

            if (idempotencyRecord?.OrderId == null)
            {
                await _checkoutRequestIdempotencyCoordinator.DeleteClaimAsync(idempotencyRecord, CancellationToken.None);
            }

            // Re-throw checkout exceptions as-is
            throw;
        }
        catch (CheckoutException ex)
        {
            _reliabilityTelemetry?.RecordCheckoutProcessed(tenantId, canonicalPaymentMethodCode ?? request.PaymentMethod, "failed");

            await TryCloseUnrecoverablePaymentIntentGapAsync(ex);

            if (idempotencyRecord?.OrderId == null)
            {
                await _checkoutRequestIdempotencyCoordinator.DeleteClaimAsync(idempotencyRecord, CancellationToken.None);
            }

            // Re-throw checkout exceptions as-is
            throw;
        }
        catch (PaymentException ex)
        {
            _reliabilityTelemetry?.RecordCheckoutProcessed(tenantId, canonicalPaymentMethodCode ?? request.PaymentMethod, "failed");

            _logger.LogError(
                ex,
                "Checkout payment provider failed | TenantId: {TenantId} | CartId: {CartId} | Step: {Step} | ErrorCode: {ErrorCode} | CorrelationId: {CorrelationId}",
                tenantId,
                request.CartId,
                activeStep,
                ex.ErrorCode,
                correlationId);

            if (orderCommitted)
            {
                var recoveryResponse = await TryRecoverPaymentIntentPostCommitFailureAsync(ex);
                if (recoveryResponse != null)
                {
                    return recoveryResponse;
                }

                await TryCloseUnrecoverablePaymentIntentGapAsync(ex);
            }

            if (idempotencyRecord?.OrderId == null)
            {
                await _checkoutRequestIdempotencyCoordinator.DeleteClaimAsync(idempotencyRecord, CancellationToken.None);
            }

            throw;
        }
        catch (UnauthorizedAccessException ex) when (string.Equals(ex.Message, TenantErrorCodes.TenantMismatch, StringComparison.Ordinal))
        {
            _reliabilityTelemetry?.RecordCheckoutProcessed(tenantId, canonicalPaymentMethodCode ?? request.PaymentMethod, "failed");

            if (idempotencyRecord?.OrderId == null)
            {
                await _checkoutRequestIdempotencyCoordinator.DeleteClaimAsync(idempotencyRecord, CancellationToken.None);
            }

            throw;
        }
        catch (CheckoutPostCommitConsistencyException)
        {
            _reliabilityTelemetry?.RecordCheckoutProcessed(tenantId, canonicalPaymentMethodCode ?? request.PaymentMethod, "failed");

            if (idempotencyRecord?.OrderId == null)
            {
                await _checkoutRequestIdempotencyCoordinator.DeleteClaimAsync(idempotencyRecord, CancellationToken.None);
            }

            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Clean up in-progress idempotency claim to avoid blocking retries
            if (idempotencyRecord?.OrderId == null)
            {
                try
                {
                    await _checkoutRequestIdempotencyCoordinator.DeleteClaimAsync(idempotencyRecord, CancellationToken.None);
                }
                catch (Exception cleanupEx)
                {
                    // Log but don't convert to CheckoutException - preserve cancellation signal
                    _logger.LogWarning(cleanupEx,
                        "Failed to clean up idempotency claim after cancellation | TenantId: {TenantId} | CartId: {CartId} | CorrelationId: {CorrelationId}",
                        tenantId, request.CartId, correlationId);
                }
            }

            // Preserve cancellation signals - do not wrap into CheckoutException
            throw;
        }
        catch (Exception ex)
        {
            _reliabilityTelemetry?.RecordCheckoutProcessed(tenantId, canonicalPaymentMethodCode ?? request.PaymentMethod, "failed");

            if (ex is WarehouseResolutionException resolutionFailure)
                _logger.LogWarning(
                    "Checkout resolution rejected | TenantId: {TenantId} | CartId: {CartId} | Step: {Step} | Code: {Code} | CorrelationId: {CorrelationId}",
                    tenantId, request.CartId, activeStep, resolutionFailure.ErrorCode, correlationId);
            else
                _logger.LogError(ex,
                    "Checkout unexpected error | TenantId: {TenantId} | CartId: {CartId} | Step: {Step} | CorrelationId: {CorrelationId}",
                    tenantId, request.CartId, activeStep, correlationId);

            // Only run pre-commit compensation if order was NOT committed
            if (!orderCommitted)
            {
                // Release inventory if reserved
                if (reservationId.HasValue)
                {
                    try
                    {
                        await ReleaseInventoryReservationAsync(
                            reservationId.Value, plannedOrderId, tenantId, request.CartId, "UnexpectedError",
                            requestId, correlationId, cancellationToken, ex);
                    }
                    catch (AggregateException aggEx)
                    {
                        // CRITICAL: AggregateException from ReleaseInventoryReservationAsync indicates
                        // partial compensation failure (stock released but event publish failed).
                        // This must be escalated to prevent cross-system divergence.
                        _logger.LogError(aggEx,
                            "Inventory reservation compensation partially failed during unexpected error cleanup | ReservationId: {ReservationId} | CorrelationId: {CorrelationId}",
                            reservationId.Value, correlationId);
                        throw;
                    }
                    catch (Exception cleanupEx)
                    {
                        // Log cleanup failure but do not let it replace the original exception
                        _logger.LogWarning(cleanupEx,
                            "Failed to release inventory reservation during unexpected error cleanup | ReservationId: {ReservationId} | CorrelationId: {CorrelationId}",
                            reservationId.Value, correlationId);
                    }
                }

                try
                {
                    await EmitCheckoutFailedEventAsync(
                        tenantId, Guid.Empty, request.CartId, 
                        steps.LastOrDefault()?.StepName ?? "Unknown",
                        "CHECKOUT_FAILED", ex.Message,
                        reservationId.HasValue, stopwatch.ElapsedMilliseconds,
                        requestId, correlationId, cancellationToken);
                }
                catch (Exception eventEx)
                {
                    // Log event emission failure but do not let it replace the original exception
                    _logger.LogWarning(eventEx,
                        "Failed to emit checkout failed event during unexpected error cleanup | CorrelationId: {CorrelationId}",
                        correlationId);
                }
            }
            else
            {
                // Order was committed but post-commit operations failed
                var recoveryResponse = await TryRecoverPaymentIntentPostCommitFailureAsync(ex);
                if (recoveryResponse != null)
                {
                    return recoveryResponse;
                }

                await TryCloseUnrecoverablePaymentIntentGapAsync(ex);
            }

            if (idempotencyRecord?.OrderId == null)
            {
                await _checkoutRequestIdempotencyCoordinator.DeleteClaimAsync(idempotencyRecord, CancellationToken.None);
            }

            // Preserve domain admission failures after normal compensation so
            // the controller returns their structured 4xx, not CHECKOUT_FAILED.
            if (ex is DomainGuardException or WarehouseResolutionException)
            {
                throw;
            }

            throw new CheckoutException("CHECKOUT_FAILED", ex.Message, 
                activeStep, ex);
        }
        finally
        {
            if (lockLease != null)
            {
                await lockLease.DisposeAsync();
            }

            // Log checkout summary
            LogCheckoutSummary(tenantId, request.CartId, steps, correlationId);
        }
    }

    #region Step 1: Idempotency And Replay

    private static string BuildPreSnapshotCheckoutLockId(
        Guid tenantId,
        Guid cartId,
        string ownerId,
        string ownerType,
        string? sessionId,
        Guid? customerId)
    {
        var hash = DeterministicRequestHasher.ComputeHash(new
        {
            Operation = "STOREFRONT_CHECKOUT_LOCK",
            TenantId = tenantId,
            CartId = cartId,
            CanonicalOwner = BuildCanonicalCheckoutOwner(ownerId, ownerType, sessionId, customerId)
        });

        return $"chk_lock_{hash[..32]}";
    }

    private static string BuildServerCheckoutIdempotencyKey(
        Guid tenantId,
        Guid cartId,
        string ownerId,
        string ownerType,
        string? sessionId,
        Guid? customerId,
        string snapshotHash)
    {
        var hash = DeterministicRequestHasher.ComputeHash(new
        {
            Operation = "STOREFRONT_CHECKOUT_IDEMPOTENCY_V2",
            TenantId = tenantId,
            CartId = cartId,
            CanonicalOwner = BuildCanonicalCheckoutOwner(ownerId, ownerType, sessionId, customerId),
            SnapshotHash = snapshotHash
        });

        return $"chk_{hash}";
    }

    private static object BuildCanonicalCheckoutOwner(
        string ownerId,
        string ownerType,
        string? sessionId,
        Guid? customerId)
    {
        var normalizedOwnerType = ownerType.Trim().ToLowerInvariant();
        return new
        {
            OwnerType = normalizedOwnerType,
            OwnerId = ownerId.Trim(),
            SessionId = normalizedOwnerType == "session" ? sessionId?.Trim() : null,
            CustomerId = normalizedOwnerType == "customer" ? customerId : null
        };
    }

    private async Task<CheckoutGeographicAdmissionSnapshot> AdmitCheckoutGeographyAsync(
        Guid tenantId,
        Guid cartId,
        AddressDto shippingAddress,
        AddressDto? billingAddress,
        string correlationId,
        CancellationToken cancellationToken,
        string principalReference)
    {
        var shippingAdmission = await AdmitAddressIfCanonicalCandidateAsync(
            tenantId,
            cartId,
            shippingAddress,
            "shipping",
            correlationId,
            cancellationToken,
            principalReference);

        var effectiveBillingAddress = billingAddress ?? shippingAddress;
        var billingAdmission = await AdmitAddressIfCanonicalCandidateAsync(
                tenantId,
                cartId,
                effectiveBillingAddress,
                "billing",
                correlationId,
                cancellationToken,
                principalReference);

        return new CheckoutGeographicAdmissionSnapshot(shippingAdmission, billingAdmission);
    }

    private async Task<AdmittedCheckoutGeographicAddress?> AdmitAddressIfCanonicalCandidateAsync(
        Guid tenantId,
        Guid cartId,
        AddressDto address,
        string addressRole,
        string correlationId,
        CancellationToken cancellationToken,
        string principalReference)
    {
        var geography = address.Geography;
        if (geography?.HasAnyPlaceCandidate != true)
        {
            return null;
        }

        if (_addressAdmissionService == null)
        {
            throw new CheckoutException(
                "GEOGRAPHIC_ADMISSION_UNAVAILABLE",
                "Checkout cannot admit the submitted geographic identity. Please refresh and try again.",
                "AddressGeographicAdmission");
        }

        var validation = geography.Validate();
        if (validation.ErrorCode != null)
        {
            throw new CheckoutException(validation.ErrorCode, validation.ErrorMessage!, "AddressGeographicAdmission");
        }

        var canonicalInputs = new CanonicalAddressInputs($"checkout-{addressRole}", principalReference,
            "owned-cart:checkout-authority", address.Country, address.Street, address.Street2, address.City,
            address.State, address.Building, address.Floor, address.Apartment, address.Landmark, address.Name, address.Phone, address.Email);
        var lineage = new GeographicPlaceLineage(geography.CountryPlaceId!.Value, geography.AdministrativeAreaPlaceId,
            geography.LocalityPlaceId, geography.DistrictPlaceId, geography.ZonePlaceId);
        var candidate = new GeographicAddressCandidate(
            TenantId: tenantId,
            OwnerDomain: AddressAdmissionOwnerDomain.Checkout,
            OwnerId: cartId,
            Lineage: new GeographicPlaceLineage(
                geography.CountryPlaceId!.Value,
                geography.AdministrativeAreaPlaceId,
                geography.LocalityPlaceId,
                geography.DistrictPlaceId,
                geography.ZonePlaceId),
            AddressFingerprintSha256: PointOfSale.Infrastructure.Geographic.CanonicalAddressEvidence.ComputeInputFingerprint(
                tenantId, AddressAdmissionOwnerDomain.Checkout, cartId, lineage, canonicalInputs, address.ZipCode),
            PostalCode: address.ZipCode?.Trim(),
            Latitude: null,
            Longitude: null,
            CoordinateSource: null,
            CanonicalInputs: canonicalInputs);

        var result = await _addressAdmissionService.AdmitAsync(candidate, cancellationToken);
        if (!result.Succeeded || !result.AddressAdmissionEvidenceId.HasValue || !result.CatalogReleaseId.HasValue)
        {
            _logger.LogWarning(
                "Checkout geographic admission failed | TenantId: {TenantId} | CartId: {CartId} | AddressRole: {AddressRole} | FailureCode: {FailureCode} | CorrelationId: {CorrelationId}",
                tenantId,
                cartId,
                addressRole,
                result.FailureCode,
                correlationId);

            throw new CheckoutException(
                "GEOGRAPHIC_ADDRESS_ADMISSION_FAILED",
                "Checkout address could not be admitted against the canonical geographic catalog.",
                "AddressGeographicAdmission");
        }

        return new AdmittedCheckoutGeographicAddress(
            result.AddressAdmissionEvidenceId.Value,
            result.CatalogReleaseId.Value,
            geography.CountryPlaceId.Value,
            geography.AdministrativeAreaPlaceId,
            geography.LocalityPlaceId,
            geography.DistrictPlaceId,
            geography.ZonePlaceId);
    }

    private static string BuildGeographicAddressFingerprint(AddressDto address, string addressRole)
    {
        return DeterministicRequestHasher.ComputeHash(new
        {
            Operation = "STOREFRONT_CHECKOUT_GEOGRAPHIC_ADDRESS_ADMISSION",
            AddressRole = addressRole,
            Address = BuildAddressFingerprint(address)
        });
    }

    private static string BuildCheckoutRequestHash(
        CheckoutRequestDto request,
        string ownerId,
        string ownerType,
        string? sessionId,
        Guid? customerId,
        string? normalizedProofFileKey)
    {
        // Normalize payment method code to canonical form (same as used in payment processing)
        var canonicalPaymentMethod = request.PaymentMethod.Trim().ToUpperInvariant() switch
        {
            "COD" or "CASH" or "CASH_ON_DELIVERY" => "COD",
            "STRIPE" or "CARD" or "CREDIT_CARD" => "STRIPE",
            "PAYMOB" => "PAYMOB",
            _ => request.PaymentMethod.Trim().ToUpperInvariant()
        };

        // Use canonical owner representation (same as used in lock and idempotency keys)
        var canonicalOwner = BuildCanonicalCheckoutOwner(ownerId, ownerType, sessionId, customerId);

        return DeterministicRequestHasher.ComputeHash(new
        {
            Operation = "STOREFRONT_CHECKOUT",
            request.CartId,
            CanonicalOwner = canonicalOwner,
            request.CheckoutSessionId,
            request.WarehouseResolutionId,
            request.WarehouseResolutionVersion,
            request.ShippingMethodId,
            PaymentMethod = canonicalPaymentMethod,
            CouponCode = request.CouponCode?.Trim().ToUpperInvariant(),
            WalletType = request.WalletType?.Trim().ToUpperInvariant(),
            WalletPhoneNumber = request.WalletPhoneNumber?.Trim(),
            ProofFileKey = normalizedProofFileKey?.Trim(),
            CustomerNotes = request.CustomerNotes?.Trim(),
            AffiliateReferralCode = request.AffiliateReferralCode?.Trim().ToUpperInvariant(),
            CustomFields = BuildCustomFieldFingerprint(request.CustomFields),
            ShippingAddress = BuildAddressFingerprint(request.ShippingAddress),
            BillingAddress = request.BillingAddress == null ? null : BuildAddressFingerprint(request.BillingAddress)
        });
    }

    private async Task<CheckoutFieldSnapshotAdmission> ValidateCheckoutFieldDefinitionsAsync(
        Guid tenantId,
        string ownerId,
        string ownerType,
        string? sessionId,
        Guid? customerId,
        CheckoutRequestDto request,
        Guid? orderId,
        CancellationToken cancellationToken)
    {
        var definitions = await _dbContext.CheckoutFieldDefinitions
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(field => field.TenantId == tenantId && !field.IsDeleted)
            .OrderBy(field => field.DisplayOrder)
            .ThenBy(field => field.CreatedAt)
            .ThenBy(field => field.Id)
            .ToListAsync(cancellationToken);

        if (definitions.Count == 0)
        {
            _logger.LogInformation(
                "Checkout dynamic field configuration loaded | TenantId: {TenantId} | CartId: {CartId} | TotalCount: 0 | EnabledCount: 0 | Status: LEGACY_FALLBACK",
                tenantId,
                request.CartId);
            ValidateLegacyRequiredAddressContract(request);
            return CheckoutFieldSnapshotAdmission.Empty;
        }

        _logger.LogInformation(
            "Checkout dynamic field configuration loaded | TenantId: {TenantId} | CartId: {CartId} | TotalCount: {TotalCount} | EnabledCount: {EnabledCount} | CustomCount: {CustomCount} | SystemCount: {SystemCount} | Status: AVAILABLE",
            tenantId,
            request.CartId,
            definitions.Count,
            definitions.Count(field => field.IsEnabled),
            definitions.Count(field => field.FieldKind == CheckoutFieldKind.Custom),
            definitions.Count(field => field.FieldKind == CheckoutFieldKind.System));

        var customDefinitions = definitions
            .Where(field => field.FieldKind == CheckoutFieldKind.Custom)
            .ToDictionary(field => field.Id);
        var submitted = new Dictionary<Guid, CheckoutCustomFieldValueDto>(customDefinitions.Count);

        foreach (var item in request.CustomFields ?? new List<CheckoutCustomFieldValueDto>())
        {
            if (item.FieldId == Guid.Empty)
            {
                throw LogDynamicFieldRejection(
                    tenantId,
                    request.CartId,
                    null,
                    null,
                    "CUSTOM_FIELD_ID_INVALID",
                    "validation",
                    "Custom checkout field identifiers must be non-empty GUIDs.");
            }

            if (!submitted.TryAdd(item.FieldId, item))
            {
                throw LogDynamicFieldRejection(
                    tenantId,
                    request.CartId,
                    item.FieldId,
                    null,
                    "CUSTOM_FIELD_DUPLICATE",
                    "validation",
                    "Custom checkout fields must not be submitted more than once.");
            }
        }

        try
        {
            CheckoutFieldRuleEngine.ValidateRules(
                definitions,
                (code, message) => LogDynamicFieldRejection(
                    tenantId,
                    request.CartId,
                    null,
                    null,
                    code,
                    "rule_validation",
                    message));
        }
        catch (JsonException)
        {
            throw LogDynamicFieldRejection(
                tenantId,
                request.CartId,
                null,
                null,
                "CHECKOUT_FIELD_CONFIGURATION_INVALID",
                "rule_configuration",
                "Checkout field conditional rule configuration is invalid. Please contact the store.");
        }

        var submittedValues = BuildCheckoutFieldSubmittedValues(definitions, submitted, request);
        var effectiveStates = CheckoutFieldRuleEngine.Evaluate(definitions, submittedValues);

        foreach (var hiddenSystemField in definitions.Where(field =>
                     field.FieldKind == CheckoutFieldKind.System &&
                     !IsEffectivelyVisible(field, effectiveStates)))
        {
            if (!string.IsNullOrWhiteSpace(GetSystemFieldSubmittedValue(request, hiddenSystemField.SystemFieldKey)))
            {
                throw LogDynamicFieldRejection(
                    tenantId,
                    request.CartId,
                    hiddenSystemField.Id,
                    hiddenSystemField.FieldType,
                    "SYSTEM_FIELD_HIDDEN",
                    "validation",
                    "Submitted checkout data contains a system field that is not applicable to this checkout.");
            }
        }

        var visibleSystemFields = definitions
            .Where(field => field.FieldKind == CheckoutFieldKind.System && IsEffectivelyVisible(field, effectiveStates))
            .ToDictionary(field => field.SystemFieldKey ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        ValidateSystemField(visibleSystemFields, effectiveStates, "full_name", request.ShippingAddress.Name);
        ValidateSystemField(visibleSystemFields, effectiveStates, "phone", request.ShippingAddress.Phone);
        ValidateSystemField(visibleSystemFields, effectiveStates, "email", request.ShippingAddress.Email);
        ValidateSystemField(visibleSystemFields, effectiveStates, "city", request.ShippingAddress.City);
        ValidateSystemField(visibleSystemFields, effectiveStates, "area", request.ShippingAddress.State);
        ValidateSystemField(visibleSystemFields, effectiveStates, "street_address", request.ShippingAddress.Street);
        ValidateSystemField(visibleSystemFields, effectiveStates, "building", request.ShippingAddress.Building);
        ValidateSystemField(visibleSystemFields, effectiveStates, "apartment", request.ShippingAddress.Apartment);
        ValidateSystemField(visibleSystemFields, effectiveStates, "postal_code", request.ShippingAddress.ZipCode);
        ValidateSystemField(visibleSystemFields, effectiveStates, "order_notes", request.CustomerNotes);

        if (request.BillingAddress != null)
        {
            RequireSystemValue(request.BillingAddress.Name, "CHECKOUT_BILLING_NAME_REQUIRED", "Billing address requires a recipient name.");
            RequireSystemValue(request.BillingAddress.Phone, "CHECKOUT_BILLING_PHONE_REQUIRED", "Billing address requires a phone number.");
            RequireSystemValue(request.BillingAddress.Street, "CHECKOUT_BILLING_STREET_REQUIRED", "Billing address requires a street address.");
            RequireSystemValue(request.BillingAddress.City, "CHECKOUT_BILLING_CITY_REQUIRED", "Billing address requires a city.");
        }

        foreach (var item in request.CustomFields ?? new List<CheckoutCustomFieldValueDto>())
        {

            if (!customDefinitions.TryGetValue(item.FieldId, out var definition))
            {
                throw LogDynamicFieldRejection(
                    tenantId,
                    request.CartId,
                    item.FieldId,
                    null,
                    "CUSTOM_FIELD_UNKNOWN",
                    "authorization",
                    "Submitted custom checkout field is not valid for this store.");
            }

            if (!definition.IsEnabled)
            {
                throw LogDynamicFieldRejection(
                    tenantId,
                    request.CartId,
                    definition.Id,
                    definition.FieldType,
                    "CUSTOM_FIELD_DISABLED",
                    "invalid_state",
                    "Submitted custom checkout field is disabled for this store.");
            }

            if (!IsEffectivelyVisible(definition, effectiveStates))
            {
                throw LogDynamicFieldRejection(
                    tenantId,
                    request.CartId,
                    definition.Id,
                    definition.FieldType,
                    "CUSTOM_FIELD_HIDDEN",
                    "validation",
                    "Submitted custom checkout field is not applicable for this checkout.");
            }

            try
            {
                if (IsUploadField(definition.FieldType))
                {
                    ValidateUploadFieldReferenceCount(definition, item.AttachmentIds);
                }
                else
                {
                    ValidateCustomFieldValue(definition, item.Value);
                }
            }
            catch (CheckoutDynamicFieldValidationException ex)
            {
                LogDynamicFieldRejection(
                    tenantId,
                    request.CartId,
                    definition.Id,
                    definition.FieldType,
                    ex.ErrorCode,
                    IsUploadField(definition.FieldType) ? "attachment_validation" : "validation",
                    ex.Message);
                throw;
            }
        }

        foreach (var requiredField in customDefinitions.Values.Where(field => IsEffectivelyRequired(field, effectiveStates)))
        {
            if (!submitted.TryGetValue(requiredField.Id, out var submittedField) || IsEmptyCustomFieldValue(requiredField, submittedField))
            {
                throw LogDynamicFieldRejection(
                    tenantId,
                    request.CartId,
                    requiredField.Id,
                    requiredField.FieldType,
                    "CUSTOM_FIELD_REQUIRED",
                    "validation",
                    $"{requiredField.Label} is required.");
            }
        }

        var snapshots = new List<CheckoutFieldSnapshotDraft>();
        foreach (var definition in visibleSystemFields.Values.OrderBy(field => field.DisplayOrder).ThenBy(field => field.CreatedAt).ThenBy(field => field.Id))
        {
            snapshots.Add(BuildCheckoutFieldSnapshotDraft(
                definition,
                GetSystemFieldSubmittedValue(request, definition.SystemFieldKey),
                effectiveStates[definition.Id]));
        }

        foreach (var definition in customDefinitions.Values
                     .Where(field => IsEffectivelyVisible(field, effectiveStates))
                     .OrderBy(field => field.DisplayOrder)
                     .ThenBy(field => field.CreatedAt)
                     .ThenBy(field => field.Id))
        {
            submitted.TryGetValue(definition.Id, out var item);
            var effectiveState = effectiveStates[definition.Id];

            if (IsUploadField(definition.FieldType))
            {
                var attachmentIds = item?.AttachmentIds ?? new List<Guid>();
                if (attachmentIds.Count == 0)
                {
                    snapshots.Add(BuildCheckoutFieldSnapshotDraftFromJson(definition, "[]", effectiveState));
                    continue;
                }

                if (!orderId.HasValue)
                {
                    snapshots.Add(BuildCheckoutFieldSnapshotDraftFromJson(definition, BuildAttachmentIdsValueJson(attachmentIds), effectiveState));
                    continue;
                }

                var attachments = await _checkoutAttachmentService.ValidateAndAttachAsync(
                    tenantId,
                    ownerId,
                    ownerType,
                    sessionId,
                    customerId,
                    request.CartId,
                    orderId.Value,
                    definition,
                    attachmentIds,
                    cancellationToken);
                snapshots.Add(BuildCheckoutFieldSnapshotDraftFromJson(definition, BuildAttachmentSnapshotValueJson(attachments), effectiveState));
                continue;
            }

            snapshots.Add(BuildCheckoutFieldSnapshotDraft(definition, item?.Value, effectiveState));
        }

        return new CheckoutFieldSnapshotAdmission(
            snapshots
                .OrderBy(snapshot => snapshot.DisplayOrder)
                .ThenBy(snapshot => snapshot.CheckoutFieldDefinitionId)
                .ToArray());
    }

    private CheckoutDynamicFieldValidationException LogDynamicFieldRejection(
        Guid tenantId,
        Guid cartId,
        Guid? fieldId,
        CheckoutFieldType? fieldType,
        string errorCode,
        string reasonCategory,
        string message)
    {
        _logger.LogWarning(
            "Checkout dynamic field rejected | TenantId: {TenantId} | CartId: {CartId} | FieldId: {FieldId} | FieldType: {FieldType} | ErrorCode: {ErrorCode} | ReasonCategory: {ReasonCategory}",
            tenantId,
            cartId,
            fieldId,
            fieldType?.ToString(),
            errorCode,
            reasonCategory);

        return new CheckoutDynamicFieldValidationException(errorCode, message);
    }

    private static IReadOnlyDictionary<Guid, CheckoutFieldSubmittedValue> BuildCheckoutFieldSubmittedValues(
        IReadOnlyList<CheckoutFieldDefinition> definitions,
        IReadOnlyDictionary<Guid, CheckoutCustomFieldValueDto> submittedCustomFields,
        CheckoutRequestDto request)
    {
        var values = new Dictionary<Guid, CheckoutFieldSubmittedValue>(definitions.Count);
        foreach (var definition in definitions)
        {
            if (definition.FieldKind == CheckoutFieldKind.System)
            {
                values[definition.Id] = new CheckoutFieldSubmittedValue(GetSystemFieldSubmittedValue(request, definition.SystemFieldKey));
                continue;
            }

            if (submittedCustomFields.TryGetValue(definition.Id, out var submitted))
            {
                values[definition.Id] = IsUploadField(definition.FieldType)
                    ? new CheckoutFieldSubmittedValue(null, submitted.AttachmentIds)
                    : new CheckoutFieldSubmittedValue(submitted.Value);
                continue;
            }

            values[definition.Id] = new CheckoutFieldSubmittedValue(null);
        }

        return values;
    }

    private static bool IsEffectivelyVisible(
        CheckoutFieldDefinition definition,
        IReadOnlyDictionary<Guid, CheckoutFieldEffectiveState> effectiveStates) =>
        effectiveStates.TryGetValue(definition.Id, out var state) ? state.Visible : definition.IsEnabled;

    private static bool IsEffectivelyRequired(
        CheckoutFieldDefinition definition,
        IReadOnlyDictionary<Guid, CheckoutFieldEffectiveState> effectiveStates) =>
        effectiveStates.TryGetValue(definition.Id, out var state)
            ? state.Visible && state.Required
            : definition.IsEnabled && definition.IsRequired;

    private static CheckoutFieldSnapshotDraft BuildCheckoutFieldSnapshotDraft(
        CheckoutFieldDefinition definition,
        string? submittedValue,
        CheckoutFieldEffectiveState effectiveState)
    {
        return BuildCheckoutFieldSnapshotDraftFromJson(
            definition,
            BuildCheckoutFieldValueJson(definition.FieldType, submittedValue),
            effectiveState);
    }

    private static CheckoutFieldSnapshotDraft BuildCheckoutFieldSnapshotDraftFromJson(
        CheckoutFieldDefinition definition,
        string valueJson,
        CheckoutFieldEffectiveState effectiveState)
    {
        return new CheckoutFieldSnapshotDraft(
            definition.Id,
            definition.FieldKind,
            definition.FieldType,
            definition.SystemFieldKey,
            definition.Label,
            definition.HelpText,
            effectiveState.Required,
            effectiveState.Visible,
            definition.DisplayOrder,
            definition.ConfigurationSchemaVersion,
            string.IsNullOrWhiteSpace(definition.ConfigurationJson) ? "{}" : definition.ConfigurationJson,
            valueJson);
    }

    private static string? GetSystemFieldSubmittedValue(CheckoutRequestDto request, string? systemFieldKey)
    {
        return systemFieldKey?.Trim().ToLowerInvariant() switch
        {
            "full_name" => request.ShippingAddress.Name,
            "phone" => request.ShippingAddress.Phone,
            "email" => request.ShippingAddress.Email,
            "city" => request.ShippingAddress.City,
            "area" => request.ShippingAddress.State,
            "street_address" => request.ShippingAddress.Street,
            "building" => request.ShippingAddress.Building,
            "apartment" => request.ShippingAddress.Apartment,
            "postal_code" => request.ShippingAddress.ZipCode,
            "order_notes" => request.CustomerNotes,
            _ => null
        };
    }

    private static string BuildCheckoutFieldValueJson(CheckoutFieldType fieldType, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "null";
        }

        var trimmedValue = value.Trim();
        object? canonicalValue = fieldType switch
        {
            CheckoutFieldType.Number when decimal.TryParse(trimmedValue, CanonicalCheckoutNumberStyles, CultureInfo.InvariantCulture, out var number) => number,
            CheckoutFieldType.Checkbox when IsBooleanValue(trimmedValue) => IsTruthyCheckboxValue(trimmedValue),
            CheckoutFieldType.Date when DateOnly.TryParseExact(trimmedValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) =>
                date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => trimmedValue
        };

        return JsonSerializer.Serialize(canonicalValue);
    }

    private static void ValidateLegacyRequiredAddressContract(CheckoutRequestDto request)
    {
        RequireSystemValue(request.ShippingAddress.Name, "CHECKOUT_SHIPPING_NAME_REQUIRED", "Shipping address requires a recipient name.");
        RequireSystemValue(request.ShippingAddress.Phone, "CHECKOUT_SHIPPING_PHONE_REQUIRED", "Shipping address requires a phone number.");
        RequireSystemValue(request.ShippingAddress.Street, "CHECKOUT_SHIPPING_STREET_REQUIRED", "Shipping address requires a street address.");
        RequireSystemValue(request.ShippingAddress.City, "CHECKOUT_SHIPPING_CITY_REQUIRED", "Shipping address requires a city.");

        if (request.BillingAddress != null)
        {
            RequireSystemValue(request.BillingAddress.Name, "CHECKOUT_BILLING_NAME_REQUIRED", "Billing address requires a recipient name.");
            RequireSystemValue(request.BillingAddress.Phone, "CHECKOUT_BILLING_PHONE_REQUIRED", "Billing address requires a phone number.");
            RequireSystemValue(request.BillingAddress.Street, "CHECKOUT_BILLING_STREET_REQUIRED", "Billing address requires a street address.");
            RequireSystemValue(request.BillingAddress.City, "CHECKOUT_BILLING_CITY_REQUIRED", "Billing address requires a city.");
        }
    }

    private static void ValidateSystemField(
        IReadOnlyDictionary<string, CheckoutFieldDefinition> enabledSystemFields,
        IReadOnlyDictionary<Guid, CheckoutFieldEffectiveState> effectiveStates,
        string key,
        string? value)
    {
        if (!enabledSystemFields.TryGetValue(key, out var field) || !IsEffectivelyRequired(field, effectiveStates))
        {
            if (enabledSystemFields.TryGetValue(key, out field))
            {
                ValidateCustomFieldValue(field, value);
            }
            return;
        }

        RequireSystemValue(value, "SYSTEM_FIELD_REQUIRED", $"{field.Label} is required.");
        ValidateCustomFieldValue(field, value);
    }

    private static void RequireSystemValue(string? value, string errorCode, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CheckoutDynamicFieldValidationException(errorCode, message);
        }
    }

    private static void ValidateCustomFieldValue(CheckoutFieldDefinition definition, string? value)
    {
        if (IsEmptyFieldValue(definition.FieldType, value))
        {
            return;
        }

        if (value!.Contains('\0'))
        {
            throw new CheckoutDynamicFieldValidationException(
                "CUSTOM_FIELD_INVALID_CHARACTER",
                $"{definition.Label} contains an unsupported character.");
        }

        var configuration = DeserializeCheckoutFieldConfiguration(definition.ConfigurationJson);
        switch (definition.FieldType)
        {
            case CheckoutFieldType.Text:
            case CheckoutFieldType.Textarea:
            case CheckoutFieldType.Phone:
                ValidateLength(value!, configuration, definition.Label, definition.FieldType == CheckoutFieldType.Textarea ? 5000 : 1000);
                if (definition.FieldType == CheckoutFieldType.Phone && !IsValidPhone(value!))
                {
                    throw new CheckoutDynamicFieldValidationException("CUSTOM_FIELD_INVALID", $"{definition.Label} format is invalid.");
                }
                break;

            case CheckoutFieldType.Email:
                ValidateLength(value!, configuration, definition.Label, 1000);
                ValidateEmailIfPresent(value, "CUSTOM_FIELD_INVALID", $"{definition.Label} format is invalid.");
                break;

            case CheckoutFieldType.Number:
                if (!decimal.TryParse(value, CanonicalCheckoutNumberStyles, CultureInfo.InvariantCulture, out var numericValue))
                {
                    throw new CheckoutDynamicFieldValidationException("CUSTOM_FIELD_INVALID", $"{definition.Label} must be a valid number.");
                }
                if (configuration.Min.HasValue && numericValue < configuration.Min.Value)
                {
                    throw new CheckoutDynamicFieldValidationException("CUSTOM_FIELD_INVALID", $"{definition.Label} is below the allowed minimum.");
                }
                if (configuration.Max.HasValue && numericValue > configuration.Max.Value)
                {
                    throw new CheckoutDynamicFieldValidationException("CUSTOM_FIELD_INVALID", $"{definition.Label} is above the allowed maximum.");
                }
                break;

            case CheckoutFieldType.Select:
            case CheckoutFieldType.Radio:
                ValidateOptionValue(definition, configuration, value!);
                break;

            case CheckoutFieldType.Checkbox:
                if (!IsBooleanValue(value!))
                {
                    throw new CheckoutDynamicFieldValidationException("CUSTOM_FIELD_INVALID", $"{definition.Label} must be true or false.");
                }
                break;

            case CheckoutFieldType.Date:
                if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    throw new CheckoutDynamicFieldValidationException("CUSTOM_FIELD_INVALID", $"{definition.Label} must be a valid date.");
                }
                break;

            case CheckoutFieldType.Image:
            case CheckoutFieldType.File:
            case CheckoutFieldType.MultiImage:
                break;
        }
    }

    private static bool IsUploadField(CheckoutFieldType fieldType) =>
        fieldType is CheckoutFieldType.Image or CheckoutFieldType.File or CheckoutFieldType.MultiImage;

    private static bool IsEmptyCustomFieldValue(CheckoutFieldDefinition definition, CheckoutCustomFieldValueDto submitted) =>
        IsUploadField(definition.FieldType)
            ? submitted.AttachmentIds == null || submitted.AttachmentIds.Count == 0
            : IsEmptyFieldValue(definition.FieldType, submitted.Value);

    private static void ValidateUploadFieldReferenceCount(CheckoutFieldDefinition definition, IReadOnlyCollection<Guid>? attachmentIds)
    {
        if (attachmentIds == null || attachmentIds.Count == 0)
        {
            return;
        }

        if (attachmentIds.Any(id => id == Guid.Empty))
        {
            throw new CheckoutDynamicFieldValidationException("CHECKOUT_ATTACHMENT_ID_INVALID", "Uploaded file identifiers must be non-empty GUIDs.");
        }

        var configuration = DeserializeCheckoutFieldConfiguration(definition.ConfigurationJson);
        var maxFiles = definition.FieldType == CheckoutFieldType.MultiImage
            ? Math.Clamp(configuration.MaxFiles ?? 5, 1, 50)
            : 1;
        if (attachmentIds.Count > maxFiles)
        {
            throw new CheckoutDynamicFieldValidationException("CHECKOUT_ATTACHMENT_TOO_MANY", $"{definition.Label} allows at most {maxFiles} file(s).");
        }
    }

    private static string BuildAttachmentIdsValueJson(IReadOnlyCollection<Guid> attachmentIds) =>
        JsonSerializer.Serialize(attachmentIds.Select(id => new { attachmentId = id }).ToArray());

    private static string BuildAttachmentSnapshotValueJson(IReadOnlyList<CheckoutAttachment> attachments) =>
        JsonSerializer.Serialize(attachments.Select(attachment => new
        {
            attachmentId = attachment.Id,
            fileName = attachment.OriginalFileName,
            contentType = attachment.ContentType,
            sizeBytes = attachment.SizeBytes,
            mediaType = attachment.MediaType,
            uploadedAtUtc = attachment.UploadedAtUtc,
            finalizedAtUtc = attachment.FinalizedAtUtc,
            finalETag = attachment.FinalETag,
            finalChecksumSha256 = attachment.FinalChecksumSha256
        }).ToArray());

    private static void ValidateLength(
        string value,
        StorefrontCheckoutFieldConfigurationDto configuration,
        string label,
        int absoluteMaxLength)
    {
        var length = value.Trim().Length;
        if (configuration.MinLength.HasValue && length < configuration.MinLength.Value)
        {
            throw new CheckoutDynamicFieldValidationException("CUSTOM_FIELD_INVALID", $"{label} is shorter than the allowed minimum.");
        }
        var maxLength = Math.Min(configuration.MaxLength ?? absoluteMaxLength, absoluteMaxLength);
        if (length > maxLength)
        {
            throw new CheckoutDynamicFieldValidationException("CUSTOM_FIELD_INVALID", $"{label} is longer than the allowed maximum.");
        }
    }

    private static void ValidateOptionValue(
        CheckoutFieldDefinition definition,
        StorefrontCheckoutFieldConfigurationDto configuration,
        string value)
    {
        var options = configuration.Options ?? Array.Empty<StorefrontCheckoutFieldOptionDto>();
        if (!options.Any(option => string.Equals(option.Value, value, StringComparison.Ordinal)))
        {
            throw new CheckoutDynamicFieldValidationException(
                "CUSTOM_FIELD_INVALID_OPTION",
                $"{definition.Label} contains an option that is not valid for this store.");
        }
    }

    private static void ValidateEmailIfPresent(string? value, string errorCode, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        try
        {
            _ = new MailAddress(value.Trim());
        }
        catch (FormatException)
        {
            throw new CheckoutDynamicFieldValidationException(errorCode, message);
        }
    }

    private static bool IsValidPhone(string value)
    {
        var digitCount = value.Count(char.IsDigit);
        return digitCount is >= 7 and <= 20 && value.All(ch => char.IsDigit(ch) || char.IsWhiteSpace(ch) || ch is '+' or '-' or '(' or ')');
    }

    private static bool IsBooleanValue(string value) =>
        bool.TryParse(value, out _) || value == "1" || value == "0";

    private static bool IsTruthyCheckboxValue(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";

    private static bool IsEmptyFieldValue(CheckoutFieldType fieldType, string? value) =>
        fieldType == CheckoutFieldType.Checkbox
            ? !IsTruthyCheckboxValue(value)
            : string.IsNullOrWhiteSpace(value);

    private static StorefrontCheckoutFieldConfigurationDto DeserializeCheckoutFieldConfiguration(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return new StorefrontCheckoutFieldConfigurationDto();
        }

        try
        {
            return JsonSerializer.Deserialize<StorefrontCheckoutFieldConfigurationDto>(configurationJson, CaseInsensitiveJsonOptions)
                ?? new StorefrontCheckoutFieldConfigurationDto();
        }
        catch (JsonException)
        {
            throw new CheckoutDynamicFieldValidationException(
                "CHECKOUT_FIELD_CONFIGURATION_INVALID",
                "Checkout field configuration is invalid. Please contact the store.");
        }
    }

    private static object? BuildCustomFieldFingerprint(List<CheckoutCustomFieldValueDto>? customFields)
    {
        if (customFields == null || customFields.Count == 0)
        {
            return null;
        }

        return customFields
            .OrderBy(field => field.FieldId)
            .Select(field => new
            {
                field.FieldId,
                Value = field.Value?.Trim(),
                AttachmentIds = field.AttachmentIds == null ? null : field.AttachmentIds.OrderBy(id => id).ToArray()
            })
            .ToArray();
    }

    private static object BuildAddressFingerprint(AddressDto address) => new
    {
        Name = address.Name?.Trim(),
        Phone = address.Phone?.Trim(),
        Email = address.Email?.Trim().ToLowerInvariant(),
        Street = address.Street?.Trim(),
        Street2 = address.Street2?.Trim(),
        City = address.City?.Trim(),
        State = address.State?.Trim(),
        ZipCode = address.ZipCode?.Trim(),
        Country = address.Country?.Trim().ToUpperInvariant(),
        Building = address.Building?.Trim(),
        Floor = address.Floor?.Trim(),
        Apartment = address.Apartment?.Trim(),
        Landmark = address.Landmark?.Trim(),
        Geography = address.Geography == null ? null : new
        {
            address.Geography.CountryPlaceId,
            address.Geography.AdministrativeAreaPlaceId,
            address.Geography.LocalityPlaceId,
            address.Geography.DistrictPlaceId,
            address.Geography.ZonePlaceId
        }
    };

    /// <summary>
    /// Normalizes a checkout response for terminal orders by clearing stale payment artifacts.
    /// Terminal states (Paid, Failed, Cancelled, Expired) should not return payment tokens/links.
    /// </summary>
    private bool NormalizeTerminalOrderResponse(
        CheckoutResponseDto response,
        Order order,
        Guid tenantId,
        string correlationId)
    {
        var isTerminalState = order.CheckoutState == CheckoutState.Paid ||
                              order.CheckoutState == CheckoutState.Failed ||
                              order.CheckoutState == CheckoutState.Cancelled ||
                              order.CheckoutState == CheckoutState.Expired;

        if (!isTerminalState)
        {
            return false;
        }

        _logger.LogDebug(
            "Order in terminal state - normalizing response | OrderId: {OrderId} | State: {State} | CorrelationId: {CorrelationId}",
            response.OrderId,
            order.CheckoutState,
            correlationId);
        
        // Update status to reflect current order state (not stale snapshot)
        response.Status = order.CheckoutState == CheckoutState.Paid
            ? order.Status.ToString()
            : order.CheckoutState.ToString();
        
        // Clear all payment-specific artifacts (stale from snapshot)
        response.PaymentReference = null; // S40: Scrub payment provider identifier for terminal orders
        response.PaymentIntentId = null;
        response.PaymentStatus = order.PaymentStatus.ToString();
        response.PaymentMethodCode = order.PaymentMethodCode ?? response.PaymentMethodCode;
        response.RedirectUrl = null;
        response.ReferenceCode = null;
        response.PaymobIframeUrl = null;
        response.PaymobPaymentKey = null;
        response.StripeClientSecret = null;
        response.StripePublishableKey = null;
        response.CheckoutToken = null;
        response.ExpiresAtUtc = null;
        
        // Clear manual wallet fields
        response.IsManualWallet = false;
        response.MerchantWalletNumber = null;
        response.WalletType = null;
        response.WalletInstructions = null;
        response.ManualWalletConfigured = null;
        response.ManualWalletErrorMessage = null;
        
        // Preserve guest tracking token even for terminal orders (for order status tracking)
        response.TrackingToken = ShouldIssueGuestTrackingToken(order)
            ? CreateGuestTrackingToken(tenantId, order.Id, order.SessionId)
            : null;

        return true;
    }

    private async Task<CheckoutResponseDto> BuildReplayResponseFromExistingOrderAsync(
        Guid tenantId,
        Guid orderId,
        string? sessionId,
        Guid? customerId,
        CheckoutRequestDto request,
        string checkoutId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == orderId && item.TenantId == tenantId && !item.IsDeleted,
                cancellationToken);

        if (order == null)
        {
            throw new CheckoutOrderCreationException(
                $"Checkout replay could not find order {orderId} for tenant {tenantId}.");
        }

        var paymentMethodCode = order.PaymentMethodCode?.Trim().ToUpperInvariant();

        // S40: Check terminal state IMMEDIATELY after loading order, before any payment intent operations
        // Terminal orders (Paid/Failed/Cancelled/Expired) should be short-circuited without creating intents
        var isTerminalState = order.CheckoutState == CheckoutState.Paid ||
                              order.CheckoutState == CheckoutState.Failed ||
                              order.CheckoutState == CheckoutState.Cancelled ||
                              order.CheckoutState == CheckoutState.Expired;

        if (isTerminalState)
        {
            _logger.LogDebug(
                "Order in terminal state - short-circuiting replay without payment intent operations | OrderId: {OrderId} | State: {State} | CorrelationId: {CorrelationId}",
                order.Id,
                order.CheckoutState,
                correlationId);

            var terminalResponse = new CheckoutResponseDto
            {
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                Status = order.CheckoutState == CheckoutState.Paid
                    ? order.Status.ToString()
                    : order.CheckoutState.ToString(),
                TotalAmount = order.Total,
                Currency = order.Currency,
                PaymentReference = null, // S40: Scrub payment provider identifier for terminal orders
                PaymentIntentId = null,
                PaymentStatus = order.PaymentStatus.ToString(),
                PaymentMethodCode = paymentMethodCode ?? string.Empty,
                CartId = request.CartId,
                // S19C: Restore promotion fallback state from persisted order
                PromotionCalculationFailed = order.PromotionCalculationFailed,
#pragma warning disable CS0618
                PromotionWarningMessage = order.PromotionWarningMessage,
#pragma warning restore CS0618
                PromotionWarningCode = order.PromotionCalculationFailed
                    ? PromotionCalculationFallbackWarningCode
                    : null,
                PromotionWarningParams = null,
                Totals = new OrderTotalsDto
                {
                    Subtotal = order.Subtotal,
                    DiscountAmount = order.TotalDiscountAmount > 0m
                        ? order.TotalDiscountAmount
                        : order.DiscountAmount + order.PromotionDiscountAmount + order.ManualDiscountAmount,
                    CouponDiscountAmount = order.DiscountAmount,
                    PromotionDiscountAmount = order.PromotionDiscountAmount,
                    ManualDiscountAmount = order.ManualDiscountAmount,
                    ShippingCost = order.ShippingCost,
                    TaxAmount = order.TaxAmount,
                    PaymentFee = order.PaymentFee,
                    GrandTotal = order.Total
                },
                // Preserve guest tracking token for terminal orders (for order status tracking)
                TrackingToken = ShouldIssueGuestTrackingToken(order)
                    ? CreateGuestTrackingToken(tenantId, order.Id, order.SessionId)
                    : null,
                // All payment-specific artifacts cleared (no stale tokens/links for terminal orders)
                RedirectUrl = null,
                ReferenceCode = null,
                PaymobIframeUrl = null,
                PaymobPaymentKey = null,
                StripeClientSecret = null,
                StripePublishableKey = null,
                CheckoutToken = null,
                ExpiresAtUtc = null,
                IsManualWallet = false,
                MerchantWalletNumber = null,
                WalletType = null,
                WalletInstructions = null,
                ManualWalletConfigured = null,
                ManualWalletErrorMessage = null
            };

            return terminalResponse;
        }

        // Non-terminal orders: proceed with payment intent operations
        var paymentIntent = await LoadLatestPaymentIntentAsync(tenantId, orderId, cancellationToken);

        if (paymentIntent == null)
        {
            paymentIntent = await EnsureReplayPaymentIntentAsync(
                tenantId,
                order,
                sessionId,
                customerId,
                request,
                checkoutId,
                correlationId,
                cancellationToken);
        }

        var response = new CheckoutResponseDto
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            Status = order.CheckoutState == CheckoutState.Paid
                ? order.Status.ToString()
                : order.CheckoutState.ToString(),
            TotalAmount = order.Total,
            Currency = order.Currency,
            PaymentReference = paymentIntent?.PaymentReference ?? order.PaymentReference,
            PaymentIntentId = paymentIntent?.Id,
            PaymentStatus = order.PaymentStatus.ToString(),
            PaymentMethodCode = paymentMethodCode ?? string.Empty,
            RedirectUrl = paymentIntent?.RedirectUrl,
            CheckoutToken = ShouldIssueCheckoutToken(order)
                ? CreateCheckoutPaymentToken(tenantId, order.Id)
                : null,
            TrackingToken = ShouldIssueGuestTrackingToken(order)
                ? CreateGuestTrackingToken(tenantId, order.Id, order.SessionId)
                : null,
            CartId = request.CartId,
            // S19C: Restore promotion fallback state from persisted order
            PromotionCalculationFailed = order.PromotionCalculationFailed,
#pragma warning disable CS0618
            PromotionWarningMessage = order.PromotionWarningMessage,
#pragma warning restore CS0618
            PromotionWarningCode = order.PromotionCalculationFailed
                ? PromotionCalculationFallbackWarningCode
                : null,
            PromotionWarningParams = null, // No parameters needed for this generic warning
            Totals = new OrderTotalsDto
            {
                Subtotal = order.Subtotal,
                DiscountAmount = order.TotalDiscountAmount > 0m
                    ? order.TotalDiscountAmount
                    : order.DiscountAmount + order.PromotionDiscountAmount + order.ManualDiscountAmount,
                CouponDiscountAmount = order.DiscountAmount,
                PromotionDiscountAmount = order.PromotionDiscountAmount,
                ManualDiscountAmount = order.ManualDiscountAmount,
                ShippingCost = order.ShippingCost,
                TaxAmount = order.TaxAmount,
                PaymentFee = order.PaymentFee,
                GrandTotal = order.Total
            },
            ReferenceCode = GetStorefrontReferenceCode(paymentMethodCode, paymentIntent?.ReferenceCode),
            ExpiresAtUtc = paymentIntent?.ExpiresAtUtc
        };

        // Check if payment intent has expired - clear time-limited fields
        var isExpired = paymentIntent?.ExpiresAtUtc.HasValue == true && 
                        paymentIntent.ExpiresAtUtc.Value <= DateTime.UtcNow;

        if (isExpired)
        {
            _logger.LogWarning(
                "Payment intent expired in replay recovery - clearing time-limited fields | OrderId: {OrderId} | ExpiresAtUtc: {ExpiresAtUtc} | PaymentMethod: {PaymentMethod} | CorrelationId: {CorrelationId}",
                order.Id,
                paymentIntent!.ExpiresAtUtc,
                paymentMethodCode,
                correlationId);

            // Clear all time-limited gateway fields to prevent returning stale links
            response.RedirectUrl = null;
            response.PaymobPaymentKey = null;
            response.CheckoutToken = null;
            response.StripeClientSecret = null;
            response.StripePublishableKey = null;
            response.PaymobIframeUrl = null;
            response.ReferenceCode = null; // Clear stale payable codes (e.g., Fawry reference)

            // Preserve guest tracking token even when payment intent expires
            // (TrackingToken was already set correctly in response initialization above)

            paymentIntent = await TryRegenerateReplayOnlineProviderHandoffAsync(
                tenantId,
                order,
                sessionId,
                customerId,
                request,
                checkoutId,
                correlationId,
                response,
                paymentIntent,
                "existing_order_expired_intent",
                cancellationToken);

            EnsureReplayResponseContainsOnlineProviderHandoff(
                response,
                tenantId,
                order.Id,
                checkoutId,
                correlationId,
                "existing_order_expired_intent");

            return response;
        }

        switch (paymentMethodCode)
        {
            case "STRIPE":
                response.RedirectUrl = null;
                response.StripeClientSecret = GetStripeClientSecret(paymentIntent);
                response.StripePublishableKey = await TryGetStripePublishableKeyAsync(tenantId, cancellationToken);
                break;
            case "PAYMOB":
                var paymobSession = await GetPaymobPaymentSessionAsync(paymentIntent, cancellationToken);
                response.PaymobIframeUrl = paymobSession?.IframeUrl;
                response.PaymobPaymentKey = paymobSession?.PaymentKey;
                response.RedirectUrl = paymobSession?.IframeUrl;
                break;
            case "MANUALWALLET":
                response.IsManualWallet = true;
                response.RedirectUrl = null;
                response.WalletType = paymentIntent?.WalletType ?? request.WalletType;
                var manualWalletDetails = await ResolveManualWalletDetailsAsync(
                    tenantId,
                    paymentIntent,
                    response.WalletType,
                    order.Total,
                    order.Currency,
                    cancellationToken);
                response.MerchantWalletNumber = manualWalletDetails.MerchantWalletNumber;
                response.WalletInstructions = manualWalletDetails.WalletInstructions;
                response.ManualWalletConfigured = manualWalletDetails.IsConfigured;
                response.ManualWalletErrorMessage = manualWalletDetails.ErrorMessage;
                break;
            default:
                response.IsManualWallet = false;
                break;
        }

        paymentIntent = await TryRegenerateReplayOnlineProviderHandoffAsync(
            tenantId,
            order,
            sessionId,
            customerId,
            request,
            checkoutId,
            correlationId,
            response,
            paymentIntent,
            "existing_order",
            cancellationToken);

        _logger.LogInformation(
            "Checkout replay recovered from persisted order | TenantId: {TenantId} | OrderId: {OrderId} | CheckoutId: {CheckoutId} | CorrelationId: {CorrelationId}",
            tenantId,
            orderId,
            checkoutId,
            correlationId);

        EnsureReplayResponseContainsOnlineProviderHandoff(
            response,
            tenantId,
            order.Id,
            checkoutId,
            correlationId,
            "existing_order");

        return response;
    }

    private async Task<Order?> FindActiveOrderForCartAsync(
        Guid tenantId,
        Guid cartId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Orders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(order =>
                order.TenantId == tenantId &&
                order.CartId == cartId &&
                !order.IsDeleted &&
                (order.Status == OrderStatus.Pending ||
                 order.Status == OrderStatus.Confirmed ||
                 order.Status == OrderStatus.Processing ||
                 order.Status == OrderStatus.OnHold))
            .OrderByDescending(order => order.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Enriches a replay response with fresh time-limited tokens that may have expired since persistence.
    /// </summary>
    private async Task<CheckoutResponseDto> EnrichReplayResponseWithFreshTokensAsync(
        Guid tenantId,
        CheckoutResponseDto replayResponse,
        string? sessionId,
        Guid? customerId,
        CheckoutRequestDto request,
        string checkoutId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        // Load the order to get payment method details
        var order = await _dbContext.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == replayResponse.OrderId && item.TenantId == tenantId && !item.IsDeleted,
                cancellationToken);

        if (order == null)
        {
            _logger.LogWarning(
                "Replay response enrichment skipped - order not found | OrderId: {OrderId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                replayResponse.OrderId,
                tenantId,
                correlationId);
            return replayResponse;
        }

        // S40: Check terminal state IMMEDIATELY after loading order, before any payment intent operations
        // Terminal orders (Paid/Failed/Cancelled/Expired) should be short-circuited without payment intent I/O
        if (NormalizeTerminalOrderResponse(replayResponse, order, tenantId, correlationId))
        {
            return replayResponse;
        }

        // Non-terminal orders: proceed with payment intent operations
        var paymentIntent = await LoadLatestPaymentIntentAsync(tenantId, replayResponse.OrderId, cancellationToken);
        var paymentMethodCode = order.PaymentMethodCode?.Trim().ToUpperInvariant();

        // Ensure PaymentIntent exists if order is in PendingPaymentIntent state
        if (paymentIntent == null && order.CheckoutState == CheckoutState.PendingPaymentIntent)
        {
            paymentIntent = await EnsureReplayPaymentIntentAsync(
                tenantId,
                order,
                sessionId,
                customerId,
                request,
                checkoutId,
                correlationId,
                cancellationToken);
        }

        // Populate replay response from PaymentIntent if available
        if (paymentIntent != null)
        {
            // Only set status to PaymentPending if order is actually in a payment-pending state
            // Do not downgrade orders that are already Paid, Failed, Cancelled, or Expired
            if (order.CheckoutState == CheckoutState.PaymentPending || 
                order.CheckoutState == CheckoutState.PendingPaymentIntent)
            {
                replayResponse.Status = CheckoutState.PaymentPending.ToString();
            }
            // Otherwise preserve the order's existing status (already set from persisted response)
            
            // Always copy payment intent details regardless of status
            replayResponse.PaymentReference = paymentIntent.PaymentReference ?? order.PaymentReference;
            replayResponse.PaymentIntentId = paymentIntent.Id;
            replayResponse.PaymentStatus = order.PaymentStatus.ToString();
            replayResponse.ReferenceCode = GetStorefrontReferenceCode(paymentMethodCode, paymentIntent.ReferenceCode);
            replayResponse.ExpiresAtUtc = paymentIntent.ExpiresAtUtc;
        }

        // Check if payment intent has expired (for non-terminal orders)
        var isExpired = paymentIntent?.ExpiresAtUtc.HasValue == true &&
                        paymentIntent.ExpiresAtUtc.Value <= DateTime.UtcNow;

        if (isExpired)
        {
            _logger.LogWarning(
                "Payment intent expired - clearing time-limited fields | OrderId: {OrderId} | ExpiresAtUtc: {ExpiresAtUtc} | PaymentMethod: {PaymentMethod} | CorrelationId: {CorrelationId}",
                replayResponse.OrderId,
                paymentIntent!.ExpiresAtUtc,
                paymentMethodCode,
                correlationId);

            // Clear all time-limited gateway fields to prevent returning stale links
            replayResponse.RedirectUrl = null;
            replayResponse.PaymobPaymentKey = null;
            replayResponse.CheckoutToken = null;
            replayResponse.StripeClientSecret = null;
            replayResponse.StripePublishableKey = null;
            replayResponse.PaymobIframeUrl = null;
            replayResponse.ReferenceCode = null; // Clear stale payable codes (e.g., Fawry reference)

            // Preserve guest tracking token even when payment intent expires
            replayResponse.TrackingToken = ShouldIssueGuestTrackingToken(order)
                ? CreateGuestTrackingToken(tenantId, order.Id, order.SessionId)
                : null;

            paymentIntent = await TryRegenerateReplayOnlineProviderHandoffAsync(
                tenantId,
                order,
                sessionId,
                customerId,
                request,
                checkoutId,
                correlationId,
                replayResponse,
                paymentIntent,
                "stored_replay_expired_intent",
                cancellationToken);

            EnsureReplayResponseContainsOnlineProviderHandoff(
                replayResponse,
                tenantId,
                order.Id,
                checkoutId,
                correlationId,
                "stored_replay_expired_intent");

            return replayResponse;
        }

        // Regenerate time-limited tokens based on payment method
        replayResponse.CheckoutToken = ShouldIssueCheckoutToken(order)
            ? CreateCheckoutPaymentToken(tenantId, order.Id)
            : null;
        replayResponse.TrackingToken = ShouldIssueGuestTrackingToken(order)
            ? CreateGuestTrackingToken(tenantId, order.Id, order.SessionId)
            : null;

        switch (paymentMethodCode)
        {
            case "STRIPE":
                replayResponse.StripeClientSecret = GetStripeClientSecret(paymentIntent);
                replayResponse.StripePublishableKey = await TryGetStripePublishableKeyAsync(tenantId, cancellationToken);
                replayResponse.RedirectUrl = null;
                break;
            case "PAYMOB":
                var paymobSession = await GetPaymobPaymentSessionAsync(paymentIntent, cancellationToken);
                replayResponse.PaymobIframeUrl = paymobSession?.IframeUrl;
                replayResponse.PaymobPaymentKey = paymobSession?.PaymentKey;
                replayResponse.RedirectUrl = paymobSession?.IframeUrl;
                break;
            case "MANUALWALLET":
                replayResponse.IsManualWallet = true;
                replayResponse.RedirectUrl = null;
                replayResponse.WalletType = paymentIntent?.WalletType ?? request.WalletType;
                var manualWalletDetails = await ResolveManualWalletDetailsAsync(
                    tenantId,
                    paymentIntent,
                    replayResponse.WalletType,
                    order.Total,
                    order.Currency,
                    cancellationToken);
                replayResponse.MerchantWalletNumber = manualWalletDetails.MerchantWalletNumber;
                replayResponse.WalletInstructions = manualWalletDetails.WalletInstructions;
                replayResponse.ManualWalletConfigured = manualWalletDetails.IsConfigured;
                replayResponse.ManualWalletErrorMessage = manualWalletDetails.ErrorMessage;
                break;
        }

        paymentIntent = await TryRegenerateReplayOnlineProviderHandoffAsync(
            tenantId,
            order,
            sessionId,
            customerId,
            request,
            checkoutId,
            correlationId,
            replayResponse,
            paymentIntent,
            "stored_replay",
            cancellationToken);

        _logger.LogDebug(
            "Replay response enriched with fresh tokens | OrderId: {OrderId} | PaymentMethod: {PaymentMethod} | CorrelationId: {CorrelationId}",
            replayResponse.OrderId,
            paymentMethodCode,
            correlationId);

        EnsureReplayResponseContainsOnlineProviderHandoff(
            replayResponse,
            tenantId,
            order.Id,
            checkoutId,
            correlationId,
            "stored_replay");

        return replayResponse;
    }

    public async Task<CheckoutPricingPreviewResponseDto> PreviewPricingAsync(
        Guid tenantId,
        string ownerId,
        string ownerType,
        string? sessionId,
        Guid? customerId,
        CheckoutPricingPreviewRequestDto request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        customerId = await ValidateCheckoutCallerOwnershipAsync(
            tenantId,
            ownerId,
            ownerType,
            sessionId,
            customerId,
            correlationId,
            cancellationToken);

        var snapshot = await GetCartSnapshotAsync(
            tenantId,
            ownerId,
            ownerType,
            request.CartId,
            correlationId,
            cancellationToken);

        // =================================================================
        // SIDE-EFFECT FREE PRICE RECALCULATION (no event publishing)
        // =================================================================
        var (recalculatedSnapshot, pricesRefreshed) = await RecalculatePricesReadOnlyAsync(
            snapshot,
            tenantId,
            correlationId,
            cancellationToken);

        var promotionCalculationFailed = await ReconcileCheckoutDiscountAuthorityAsync(
            tenantId,
            customerId,
            recalculatedSnapshot,
            request.CartId,
            correlationId,
            cancellationToken,
            mode: "preview");

        ShippingMethodDocument? shippingMethod = null;
        if (request.ShippingMethodId.HasValue)
        {
            shippingMethod = await ValidateShippingMethodAsync(
                request.ShippingMethodId.Value,
                tenantId,
                correlationId,
                cancellationToken);
        }

        PaymentMethodDocument? paymentMethod = null;
        if (!string.IsNullOrWhiteSpace(request.PaymentMethod))
        {
            paymentMethod = await ValidatePaymentMethodAsync(
                request.PaymentMethod,
                tenantId,
                correlationId,
                cancellationToken);
        }

        // Price preview has no complete address/contact payload and is read-only.
        // A geographic candidate is not admission evidence. The actual submitted
        // address is admitted on checkout, never using invented preview inputs.
        if (request.ShippingGeography?.HasAnyPlaceCandidate == true)
        {
            var validation = request.ShippingGeography.Validate();
            if (validation.ErrorCode != null)
                throw new CheckoutException(validation.ErrorCode, validation.ErrorMessage!, "AddressGeographicAdmission");
        }

        var pricing = await _storefrontPricingService.CalculateCheckoutAsync(
            tenantId,
            recalculatedSnapshot,
            shippingMethod,
            paymentMethod,
            request.ShippingCountry,
            cancellationToken);

        if (paymentMethod != null)
        {
            ValidatePaymentMethodOrderAmountLimits(paymentMethod, pricing.FinalTotal, correlationId);
        }

        return new CheckoutPricingPreviewResponseDto
        {
            Subtotal = pricing.Subtotal,
            Shipping = pricing.Shipping,
            Tax = pricing.Tax,
            Discount = pricing.Discount,
            Total = pricing.Total,
            CouponDiscount = pricing.CouponDiscount,
            PromotionDiscount = pricing.PromotionDiscount,
            CartPromotionDiscount = pricing.CartPromotionDiscount,
            PaymentFee = pricing.PaymentFee,
            ManualDiscount = pricing.ManualDiscount,
            TaxRate = pricing.TaxRate,
            TaxSource = pricing.TaxSource,
            TaxCountryCode = pricing.TaxCountryCode,
            PricesRefreshed = pricesRefreshed,
            PromotionCalculationFailed = promotionCalculationFailed
        };
    }

    private async Task<PaymentIntent?> EnsureReplayPaymentIntentAsync(
        Guid tenantId,
        Order order,
        string? sessionId,
        Guid? customerId,
        CheckoutRequestDto request,
        string checkoutId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await CheckoutInventoryAuthorityGuard.EnsureStorefrontOrderReservationsActiveAsync(
            _dbContext,
            order,
            cancellationToken);

        var paymentMethodCode = order.PaymentMethodCode?.Trim().ToUpperInvariant();

        switch (paymentMethodCode)
        {
            case "STRIPE":
                var checkoutToken = CreateCheckoutPaymentToken(tenantId, order.Id);
                if (!string.IsNullOrWhiteSpace(checkoutToken))
                {
                    await CreateCheckoutPaymentSessionAsync(
                        tenantId,
                        order,
                        paymentMethodCode,
                        checkoutToken,
                        customerId,
                        order.PromotionCalculationFailed,
                        checkoutId,
                        correlationId,
                        walletType: null,
                        walletPhoneNumber: null,
                        proofFileKey: null,
                        cancellationToken: cancellationToken);
                }
                break;
            case "PAYMOB":
                await CreateCheckoutPaymentSessionAsync(
                    tenantId,
                    order,
                    paymentMethodCode,
                    CreateCheckoutPaymentToken(tenantId, order.Id),
                    customerId,
                    order.PromotionCalculationFailed,
                    checkoutId,
                    correlationId,
                    walletType: null,
                    walletPhoneNumber: null,
                    proofFileKey: null,
                    cancellationToken: cancellationToken);
                break;
            case "FAWRY":
                // T-09: Fawry has no certified financial settlement authority.
                // Replay must not initiate provider execution.
                throw new CheckoutFawryFinancialAuthorityException(paymentMethodCode);
            case "MANUALWALLET":
                // Resolve wallet details first to check configuration before creating intent
                var walletDetails = await ResolveManualWalletDetailsAsync(
                    tenantId,
                    paymentIntent: null, // No existing intent to read from
                    request.WalletType,
                    order.Total,
                    order.Currency,
                    cancellationToken);

                // Short-circuit if wallet is not configured - return null for degraded response
                // This prevents creating a payment intent when the wallet config is missing
                if (!walletDetails.IsConfigured)
                {
                    _logger.LogWarning(
                        "Manual wallet not configured, skipping payment intent creation | TenantId: {TenantId} | OrderId: {OrderId} | WalletType: {WalletType}",
                        tenantId,
                        order.Id,
                        request.WalletType);
                    return null;
                }

                await CreateCheckoutPaymentSessionAsync(
                    tenantId,
                    order,
                    paymentMethodCode,
                    checkoutToken: null,
                    customerId: customerId,
                    promotionCalculationFailed: order.PromotionCalculationFailed,
                    requestId: checkoutId,
                    correlationId: correlationId,
                    walletType: request.WalletType,
                    walletPhoneNumber: request.WalletPhoneNumber,
                    proofFileKey: await ResolveValidatedManualWalletProofFileKeyAsync(
                        tenantId,
                        order.IsGuestCheckout ? (sessionId ?? string.Empty) : (customerId?.ToString("D") ?? string.Empty),
                        order.IsGuestCheckout ? "session" : "customer",
                        sessionId,
                        customerId,
                        request.CartId,
                        request.ProofFileKey,
                        order.Id,
                        cancellationToken),
                    cancellationToken: cancellationToken);

                // Wallet details were resolved from config, update the newly created intent with metadata
                var newIntent = await LoadLatestPaymentIntentAsync(tenantId, order.Id, cancellationToken);
                if (newIntent != null && string.IsNullOrWhiteSpace(newIntent.ProviderMetadata))
                {
                    // Embed wallet details into ProviderMetadata for future replays
                    newIntent.ProviderMetadata = JsonSerializer.Serialize(
                        new ManualWalletProviderMetadata
                        {
                            MerchantWalletNumber = walletDetails.MerchantWalletNumber,
                            WalletInstructions = walletDetails.WalletInstructions
                        },
                        CaseInsensitiveJsonOptions);
                    
                    // LoadLatestPaymentIntentAsync uses AsNoTracking, so we must attach and mark modified
                    _dbContext.PaymentIntents.Attach(newIntent);
                    _dbContext.Entry(newIntent).Property(p => p.ProviderMetadata).IsModified = true;
                    await _dbContext.SaveChangesAsync(cancellationToken);

                    _logger.LogInformation(
                        "Embedded manual wallet metadata into replay payment intent | TenantId: {TenantId} | OrderId: {OrderId} | PaymentIntentId: {PaymentIntentId}",
                        tenantId,
                        order.Id,
                        newIntent.Id);
                }
                break;
            default:
                // Generic payment methods (COD, etc.) - check if order is stuck in PendingPaymentIntent
                // without corresponding outbox entry and recreate it for self-healing
                if (order.CheckoutState == CheckoutState.PendingPaymentIntent && 
                    !string.IsNullOrWhiteSpace(order.PaymentReference))
                {
                    // Check if outbox entry exists
                    var outboxMessageId = CreateDeterministicOutboxMessageId(
                        nameof(PaymentIntentCreatedEvent),
                        tenantId,
                        order.Id,
                        order.PaymentReference,
                        checkoutId);

                    var outboxExists = await _dbContext.OutboxMessages
                        .AsNoTracking()
                        .AnyAsync(message => message.Id == outboxMessageId, cancellationToken);

                    if (!outboxExists)
                    {
                        _logger.LogWarning(
                            "Order stuck in PendingPaymentIntent without outbox entry, recreating | TenantId: {TenantId} | OrderId: {OrderId} | PaymentReference: {PaymentReference}",
                            tenantId,
                            order.Id,
                            order.PaymentReference);

                        // Recreate the outbox entry idempotently using EmitPaymentIntentCreatedEventAsync
                        // Use CartId from order if available, otherwise use order.Id as fallback
                        var cartId = order.CartId ?? order.Id;
                        
                        await EmitPaymentIntentCreatedEventAsync(
                            tenantId,
                            order.Id,
                            cartId,
                            order.OrderNumber,
                            order.PaymentReference,
                            order.PaymentMethodCode ?? "UNKNOWN",
                            order.Total,
                            order.Currency,
                            redirectUrl: null, // Not available during replay
                            promotionCalculationFailed: order.PromotionCalculationFailed, // Use persisted value
                            checkoutId, // Use checkoutId as requestId for idempotency
                            correlationId,
                            ProductEventVersionHelper.ToProductEventVersion(Math.Max(1L, order.ProjectionMutationVersion)),
                            cancellationToken);

                        // Persist the outbox entry
                        try
                        {
                            await _dbContext.SaveChangesAsync(cancellationToken);
                        }
                        catch (DbUpdateException ex) when (IsDuplicateOutboxMessageUniqueViolation(ex))
                        {
                            // Race condition: another thread created the outbox entry
                            _logger.LogInformation(
                                "Outbox entry already created by concurrent request | TenantId: {TenantId} | OrderId: {OrderId} | OutboxMessageId: {OutboxMessageId}",
                                tenantId,
                                order.Id,
                                outboxMessageId);
                        }

                        // Verify outbox entry exists after save
                        var outboxCreated = await _dbContext.OutboxMessages
                            .AsNoTracking()
                            .AnyAsync(message => message.Id == outboxMessageId, cancellationToken);

                        if (outboxCreated)
                        {
                            _logger.LogInformation(
                                "Successfully recreated PaymentIntentCreatedEvent outbox entry | TenantId: {TenantId} | OrderId: {OrderId} | OutboxMessageId: {OutboxMessageId}",
                                tenantId,
                                order.Id,
                                outboxMessageId);
                        }
                        else
                        {
                            _logger.LogError(
                                "Failed to create outbox entry after save | TenantId: {TenantId} | OrderId: {OrderId} | OutboxMessageId: {OutboxMessageId}",
                                tenantId,
                                order.Id,
                                outboxMessageId);
                        }
                    }
                }
                return null;
        }

        var latestIntent = await LoadLatestPaymentIntentAsync(tenantId, order.Id, cancellationToken);
        if (latestIntent != null && order.CheckoutState == CheckoutState.PendingPaymentIntent)
        {
            // Re-query the order as a tracked entity since the incoming order is AsNoTracking
            var trackedOrder = await _dbContext.Orders.FindAsync(new object[] { order.Id }, cancellationToken);
            if (trackedOrder != null)
            {
                trackedOrder.TransitionCheckoutState(CheckoutState.PaymentPending);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        return latestIntent;
    }

    private async Task<PaymentIntent?> TryRegenerateReplayOnlineProviderHandoffAsync(
        Guid tenantId,
        Order order,
        string? sessionId,
        Guid? customerId,
        CheckoutRequestDto request,
        string checkoutId,
        string correlationId,
        CheckoutResponseDto response,
        PaymentIntent? paymentIntent,
        string replaySource,
        CancellationToken cancellationToken)
    {
        var paymentMethodCode = order.PaymentMethodCode?.Trim().ToUpperInvariant();
        if (!IsOnlineProviderRequiringStorefrontHandoff(paymentMethodCode) ||
            HasOnlineProviderHandoff(paymentMethodCode, response) ||
            !CanRegenerateOnlineProviderHandoff(paymentIntent))
        {
            return paymentIntent;
        }

        // T-09: Fawry handoff regeneration would initiate provider execution
        // without certified financial settlement authority.
        if (PointOfSale.Core.Payments.FawryFinancialAuthorityPolicy.IsFawry(paymentMethodCode))
        {
            throw new CheckoutFawryFinancialAuthorityException(paymentMethodCode ?? "FAWRY");
        }

        _logger.LogWarning(
            "Checkout replay regenerating online provider handoff after recoverable initialization failure | TenantId: {TenantId} | OrderId: {OrderId} | CheckoutId: {CheckoutId} | PaymentMethod: {PaymentMethod} | PreviousPaymentIntentId: {PaymentIntentId} | PreviousPaymentIntentStatus: {PaymentIntentStatus} | ReplaySource: {ReplaySource} | CorrelationId: {CorrelationId}",
            tenantId,
            order.Id,
            checkoutId,
            paymentMethodCode,
            paymentIntent?.Id,
            paymentIntent?.Status.ToString() ?? "None",
            replaySource,
            correlationId);

        var session = await CreateCheckoutPaymentSessionAsync(
            tenantId,
            order,
            paymentMethodCode!,
            CreateCheckoutPaymentToken(tenantId, order.Id),
            customerId,
            order.PromotionCalculationFailed,
            checkoutId,
            correlationId,
            walletType: null,
            walletPhoneNumber: null,
            proofFileKey: null,
            cancellationToken: cancellationToken);

        await ApplyReplayPaymentSessionAsync(
            tenantId,
            order,
            paymentMethodCode,
            response,
            session,
            cancellationToken);

        _logger.LogInformation(
            "Checkout replay regenerated online provider handoff | TenantId: {TenantId} | OrderId: {OrderId} | CheckoutId: {CheckoutId} | PaymentMethod: {PaymentMethod} | NewPaymentIntentIdPresent: {PaymentIntentIdPresent} | PaymobHandoffPresent: {PaymobHandoffPresent} | StripeHandoffPresent: {StripeHandoffPresent} | CorrelationId: {CorrelationId}",
            tenantId,
            order.Id,
            checkoutId,
            paymentMethodCode,
            response.PaymentIntentId.HasValue,
            HasPaymobCheckoutHandoff(response),
            HasStripeCheckoutHandoff(response),
            correlationId);

        return response.PaymentIntentId.HasValue
            ? await LoadLatestPaymentIntentAsync(tenantId, order.Id, cancellationToken)
            : paymentIntent;
    }

    private async Task ApplyReplayPaymentSessionAsync(
        Guid tenantId,
        Order order,
        string paymentMethodCode,
        CheckoutResponseDto response,
        CheckoutPaymentSession session,
        CancellationToken cancellationToken)
    {
        response.PaymentIntentId = session.PaymentIntentId;
        response.PaymentReference = !string.IsNullOrWhiteSpace(session.PaymentReference)
            ? session.PaymentReference
            : order.PaymentReference;
        response.RedirectUrl = session.RedirectUrl;
        response.ReferenceCode = GetStorefrontReferenceCode(paymentMethodCode, session.ReferenceCode);
        response.ExpiresAtUtc = session.ExpiresAtUtc;
        response.CheckoutToken = ShouldIssueCheckoutToken(order)
            ? CreateCheckoutPaymentToken(tenantId, order.Id)
            : null;

        if (response.PaymentIntentId.HasValue && !response.ExpiresAtUtc.HasValue)
        {
            response.ExpiresAtUtc = await ResolvePaymentIntentExpirationAsync(
                tenantId,
                response.PaymentIntentId.Value,
                cancellationToken);
        }

        switch (paymentMethodCode)
        {
            case "STRIPE":
                response.StripeClientSecret = GetCompatibilityField(session, nameof(CheckoutResponseDto.StripeClientSecret));
                response.StripePublishableKey = GetCompatibilityField(session, nameof(CheckoutResponseDto.StripePublishableKey));
                response.PaymobIframeUrl = null;
                response.PaymobPaymentKey = null;
                response.RedirectUrl = null;
                break;
            case "PAYMOB":
                response.PaymobIframeUrl = GetCompatibilityField(session, nameof(CheckoutResponseDto.PaymobIframeUrl))
                                          ?? session.RedirectUrl;
                response.PaymobPaymentKey = GetCompatibilityField(session, nameof(CheckoutResponseDto.PaymobPaymentKey));
                response.RedirectUrl = response.PaymobIframeUrl;
                response.StripeClientSecret = null;
                response.StripePublishableKey = null;
                break;
            case "FAWRY":
                response.PaymobIframeUrl = null;
                response.PaymobPaymentKey = null;
                response.StripeClientSecret = null;
                response.StripePublishableKey = null;
                break;
        }
    }

    private static bool IsOnlineProviderRequiringStorefrontHandoff(string? paymentMethodCode) =>
        paymentMethodCode is "STRIPE" or "PAYMOB" or "FAWRY";

    private static bool HasOnlineProviderHandoff(string? paymentMethodCode, CheckoutResponseDto response) =>
        paymentMethodCode switch
        {
            "STRIPE" => HasStripeCheckoutHandoff(response),
            "PAYMOB" => HasPaymobCheckoutHandoff(response),
            "FAWRY" => response.PaymentIntentId.HasValue &&
                       !string.IsNullOrWhiteSpace(response.CheckoutToken) &&
                       !string.IsNullOrWhiteSpace(response.ReferenceCode) &&
                       response.ExpiresAtUtc.HasValue,
            _ => true
        };

    private static bool CanRegenerateOnlineProviderHandoff(PaymentIntent? paymentIntent)
    {
        if (paymentIntent == null)
        {
            return true;
        }

        if (paymentIntent.Status is PaymentIntentStatus.Failed or PaymentIntentStatus.Cancelled or PaymentIntentStatus.Expired)
        {
            return true;
        }

        return paymentIntent.Status == PaymentIntentStatus.Pending &&
               paymentIntent.ExpiresAtUtc.HasValue &&
               paymentIntent.ExpiresAtUtc.Value <= DateTime.UtcNow;
    }

    private static bool IsLocalPaymentIntentFailure(Exception ex)
    {
        return ex is CheckoutManualWalletNotConfiguredException
            or PaymentManualWalletNotConfiguredException
            or PaymentProviderNotSupportedException;
    }

    private static bool IsPostCommitPaymentRecoveryResponseSafe(CheckoutResponseDto response)
    {
        var paymentMethodCode = response.PaymentMethodCode?.Trim().ToUpperInvariant();
        return paymentMethodCode switch
        {
            "STRIPE" => HasStripeCheckoutHandoff(response),
            "PAYMOB" => HasPaymobCheckoutHandoff(response),
            "FAWRY" => response.PaymentIntentId.HasValue &&
                       !string.IsNullOrWhiteSpace(response.CheckoutToken) &&
                       !string.IsNullOrWhiteSpace(response.ReferenceCode) &&
                       response.ExpiresAtUtc.HasValue,
            "MANUALWALLET" => response.PaymentIntentId.HasValue &&
                              response.IsManualWallet &&
                              !string.IsNullOrWhiteSpace(response.WalletType),
            _ => !string.IsNullOrWhiteSpace(response.PaymentReference)
        };
    }

    private void EnsureReplayResponseContainsOnlineProviderHandoff(
        CheckoutResponseDto response,
        Guid tenantId,
        Guid orderId,
        string checkoutId,
        string correlationId,
        string replaySource)
    {
        var paymentMethodCode = response.PaymentMethodCode?.Trim().ToUpperInvariant();
        var isSafe = paymentMethodCode switch
        {
            "STRIPE" => HasStripeCheckoutHandoff(response),
            "PAYMOB" => HasPaymobCheckoutHandoff(response),
            "FAWRY" => response.PaymentIntentId.HasValue &&
                       !string.IsNullOrWhiteSpace(response.CheckoutToken) &&
                       !string.IsNullOrWhiteSpace(response.ReferenceCode) &&
                       response.ExpiresAtUtc.HasValue,
            _ => true
        };

        if (isSafe)
        {
            return;
        }

        _logger.LogWarning(
            "Checkout replay blocked because online provider handoff is missing | TenantId: {TenantId} | OrderId: {OrderId} | CheckoutId: {CheckoutId} | PaymentMethod: {PaymentMethod} | PaymentIntentIdPresent: {PaymentIntentIdPresent} | PaymobHandoffPresent: {PaymobHandoffPresent} | StripeHandoffPresent: {StripeHandoffPresent} | ReplaySource: {ReplaySource} | CorrelationId: {CorrelationId}",
            tenantId,
            orderId,
            checkoutId,
            paymentMethodCode,
            response.PaymentIntentId.HasValue,
            HasPaymobCheckoutHandoff(response),
            HasStripeCheckoutHandoff(response),
            replaySource,
            correlationId);

        throw new PaymentException(
            "PAYMENT_HANDOFF_MISSING",
            "The previous online payment attempt did not produce a provider checkout handoff.");
    }

    private static bool HasStripeCheckoutHandoff(CheckoutResponseDto response)
    {
        return response.PaymentIntentId.HasValue &&
               !string.IsNullOrWhiteSpace(response.CheckoutToken) &&
               response.ExpiresAtUtc.HasValue &&
               !string.IsNullOrWhiteSpace(response.StripeClientSecret) &&
               !string.IsNullOrWhiteSpace(response.StripePublishableKey);
    }

    private static bool HasPaymobCheckoutHandoff(CheckoutResponseDto response)
    {
        if (!response.PaymentIntentId.HasValue ||
            string.IsNullOrWhiteSpace(response.CheckoutToken) ||
            !response.ExpiresAtUtc.HasValue ||
            response.ExpiresAtUtc.Value <= DateTime.UtcNow ||
            string.IsNullOrWhiteSpace(response.PaymobIframeUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(response.PaymobIframeUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "accept.paymob.com", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.AbsolutePath.TrimEnd('/'), "/unifiedcheckout", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var values = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        return values.TryGetValue("publicKey", out var publicKey) &&
               !string.IsNullOrWhiteSpace(publicKey.ToString()) &&
               values.TryGetValue("clientSecret", out var clientSecret) &&
               !string.IsNullOrWhiteSpace(clientSecret.ToString());
    }

    private async Task<PaymentIntent?> LoadLatestPaymentIntentAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.PaymentIntents
            .AsNoTracking()
            .Where(intent => intent.TenantId == tenantId && intent.OrderId == orderId && !intent.IsDeleted)
            .OrderByDescending(intent => intent.CreatedAt)
            .ThenByDescending(intent => intent.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<string?> TryGetStripePublishableKeyAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var paymentMethod = await _dbContext.TenantPaymentMethods
            .AsNoTracking()
            .FirstOrDefaultAsync(pm =>
                pm.TenantId == tenantId &&
                pm.Code == "STRIPE" &&
                pm.IsEnabled &&
                !string.IsNullOrEmpty(pm.ConfigEncrypted),
                cancellationToken);

        if (string.IsNullOrWhiteSpace(paymentMethod?.ConfigEncrypted))
        {
            return null;
        }

        var configuration = EvaluatePaymentMethodConfiguration(paymentMethod, "checkout replay");
        if (!configuration.IsConfigurationValid ||
            !configuration.NormalizedConfiguration.TryGetValue("publishableKey", out var publishableKey) ||
            string.IsNullOrWhiteSpace(publishableKey))
        {
            return null;
        }

        return publishableKey;
    }

    /// <summary>
    /// Retrieves the decrypted Stripe client secret from a payment intent.
    /// Handles both new encrypted field and legacy RedirectUrl field for backward compatibility.
    /// </summary>
    private string? GetStripeClientSecret(PaymentIntent? paymentIntent)
    {
        if (paymentIntent == null)
        {
            return null;
        }

        // New encrypted field (preferred)
        if (!string.IsNullOrWhiteSpace(paymentIntent.ClientSecretEncrypted))
        {
            try
            {
                return _clientSecretProtector.Unprotect(paymentIntent.ClientSecretEncrypted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Failed to decrypt Stripe client secret for PaymentIntent {PaymentIntentId}", 
                    paymentIntent.Id);
                return null;
            }
        }

        // Fallback to legacy RedirectUrl field for backward compatibility
        if (!string.IsNullOrWhiteSpace(paymentIntent.RedirectUrl) && 
            paymentIntent.RedirectUrl.StartsWith("pi_") && 
            paymentIntent.RedirectUrl.Contains("_secret_"))
        {
            _logger.LogWarning(
                "PaymentIntent {PaymentIntentId} still using RedirectUrl for client_secret - migration needed",
                paymentIntent.Id);
            return paymentIntent.RedirectUrl;
        }

        return null;
    }

    private async Task<PaymobProtectedPaymentSession?> GetPaymobPaymentSessionAsync(
        PaymentIntent? paymentIntent,
        CancellationToken cancellationToken)
    {
        if (paymentIntent == null)
        {
            return null;
        }

        var metadata = PaymentIntentRoutingMetadataSerializer.Deserialize(paymentIntent.ProviderMetadata);
        if (!string.IsNullOrWhiteSpace(metadata?.ProtectedPaymobSession))
        {
            try
            {
                return _paymobArtifactProtector.UnprotectPaymentSession(metadata.ProtectedPaymobSession);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to decrypt Paymob payment session for PaymentIntent {PaymentIntentId}",
                    paymentIntent.Id);
                return null;
            }
        }

        if (string.IsNullOrWhiteSpace(metadata?.PaymentKey) ||
            string.IsNullOrWhiteSpace(paymentIntent.RedirectUrl))
        {
            return null;
        }

        var legacySession = new PaymobProtectedPaymentSession(
            metadata.PaymentKey.Trim(),
            paymentIntent.RedirectUrl.Trim());
        var protectedSession = _paymobArtifactProtector.ProtectPaymentSession(
            legacySession.PaymentKey,
            legacySession.IframeUrl);

        paymentIntent.ProviderMetadata = PaymentIntentRoutingMetadataSerializer.Serialize(
            metadata.TenantId,
            metadata.OrderId,
            metadata.StorefrontBaseUrl,
            metadata.PaymobMerchantOrderId,
            metadata.PaymobCheckpointState,
            metadata.PaymobCheckpointedAtUtc,
            protectedSession);
        paymentIntent.RedirectUrl = null;

        var entry = _dbContext.Entry(paymentIntent);
        if (entry.State == EntityState.Detached)
        {
            _dbContext.PaymentIntents.Attach(paymentIntent);
        }

        entry.Property(item => item.ProviderMetadata).IsModified = true;
        entry.Property(item => item.RedirectUrl).IsModified = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return legacySession;
    }

    private async Task<(string? MerchantWalletNumber, string? WalletInstructions, bool IsConfigured, string? ErrorMessage)> ResolveManualWalletDetailsAsync(
        Guid tenantId,
        PaymentIntent? paymentIntent,
        string? walletType,
        decimal total,
        string currency,
        CancellationToken cancellationToken)
    {
        var persistedMetadata = TryReadManualWalletProviderMetadata(paymentIntent?.ProviderMetadata);
        if (!string.IsNullOrWhiteSpace(persistedMetadata?.MerchantWalletNumber) &&
            !string.IsNullOrWhiteSpace(persistedMetadata?.WalletInstructions))
        {
            return (
                persistedMetadata.MerchantWalletNumber.Trim(),
                persistedMetadata.WalletInstructions.Trim(),
                true,
                null);
        }

        try
        {
            var configuredWallet = await ResolveRequiredManualWalletDetailsAsync(
                tenantId,
                walletType,
                total,
                currency,
                cancellationToken);
            return (configuredWallet.MerchantWalletNumber, configuredWallet.WalletInstructions, true, null);
        }
        catch (CheckoutManualWalletNotConfiguredException)
        {
            _logger.LogCritical(
                "Manual wallet replay could not recover merchant instructions | TenantId: {TenantId} | PaymentIntentId: {PaymentIntentId}",
                tenantId,
                paymentIntent?.Id);
            return (null, null, false, "Manual wallet payment method is not configured for this merchant");
        }
    }

    private async Task<(string MerchantWalletNumber, string WalletInstructions)> ResolveRequiredManualWalletDetailsAsync(
        Guid tenantId,
        string? walletType,
        decimal total,
        string currency,
        CancellationToken cancellationToken)
    {
        var walletMethod = await _dbContext.TenantPaymentMethods
            .AsNoTracking()
            .FirstOrDefaultAsync(
                method => method.TenantId == tenantId && method.Code == "MANUALWALLET",
                cancellationToken);

        if (walletMethod == null)
        {
            LogManualWalletConfigurationFailure(tenantId, "PAYMENT_METHOD_MISSING", null);
            throw new CheckoutManualWalletNotConfiguredException(tenantId);
        }

        var configuration = EvaluatePaymentMethodConfiguration(walletMethod, "manual wallet checkout");
        if (!configuration.IsConfigurationValid)
        {
            LogManualWalletConfigurationFailure(tenantId, "CONFIG_INVALID", walletMethod.Id);
            throw new CheckoutManualWalletNotConfiguredException(tenantId);
        }

        var merchantWalletNumber = configuration.PublicConfiguration.GetValueOrDefault("walletNumber")?.Trim();

        if (string.IsNullOrWhiteSpace(merchantWalletNumber))
        {
            LogManualWalletConfigurationFailure(tenantId, "WALLET_NUMBER_MISSING", walletMethod.Id);
            throw new CheckoutManualWalletNotConfiguredException(tenantId);
        }

        var walletInstructions = BuildManualWalletInstructions(
            walletType,
            total,
            currency,
            merchantWalletNumber);

        return (merchantWalletNumber, walletInstructions);
    }

    private static string BuildManualWalletInstructions(
        string? walletType,
        decimal total,
        string currency,
        string? merchantWalletNumber) =>
        walletType?.ToUpperInvariant() switch
        {
            "VODAFONECASH" => $"Transfer {total} {currency} to Vodafone Cash number: {merchantWalletNumber} and upload proof.",
            "INSTAPAY" => $"Transfer {total} {currency} via InstaPay to: {merchantWalletNumber} and upload proof.",
            "ORANGECASH" => $"Transfer {total} {currency} to Orange Cash number: {merchantWalletNumber} and upload proof.",
            _ => $"Transfer {total} {currency} to wallet: {merchantWalletNumber} and upload proof."
        };

    private ManualWalletProviderMetadata? TryReadManualWalletProviderMetadata(string? providerMetadata)
    {
        if (string.IsNullOrWhiteSpace(providerMetadata))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ManualWalletProviderMetadata>(
                providerMetadata,
                CaseInsensitiveJsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Manual wallet provider metadata could not be parsed for replay");
            return null;
        }
    }

    private PaymentMethodConfigurationValidationResult EvaluatePaymentMethodConfiguration(
        TenantPaymentMethod method,
        string context)
    {
        if (string.IsNullOrWhiteSpace(method.ConfigEncrypted))
        {
            return PaymentMethodConfigurationValidator.Validate(method.Code, null);
        }

        try
        {
            var decrypted = _dataProtector.Unprotect(method.ConfigEncrypted);
            var configuration = JsonSerializer.Deserialize<Dictionary<string, string>>(decrypted);
            return PaymentMethodConfigurationValidator.Validate(method.Code, configuration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to decrypt payment method configuration for checkout | Context: {Context} | TenantId: {TenantId} | PaymentMethodId: {PaymentMethodId} | Code: {Code}",
                context,
                method.TenantId,
                method.Id,
                method.Code);

            var normalizedCode = method.Code?.Trim().ToUpperInvariant() ?? string.Empty;
            return new PaymentMethodConfigurationValidationResult(
                normalizedCode,
                PaymentMethodConfigurationValidator.RequiresConfiguration(normalizedCode),
                false,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                new[] { "Stored payment configuration could not be decrypted." },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private void LogManualWalletConfigurationFailure(
        Guid tenantId,
        string reason,
        Guid? paymentMethodId,
        Exception? exception = null)
    {
        _reliabilityTelemetry?.RecordPaymentConfigInvalid("manualwallet", "checkout", reason);

        if (exception == null)
        {
            _logger.LogCritical(
                "Admin action required: manual wallet checkout rejected because merchant configuration is invalid | TenantId: {TenantId} | PaymentMethodId: {PaymentMethodId} | Reason: {Reason}",
                tenantId,
                paymentMethodId,
                reason);
            return;
        }

        _logger.LogCritical(
            exception,
            "Admin action required: manual wallet checkout rejected because merchant configuration is invalid | TenantId: {TenantId} | PaymentMethodId: {PaymentMethodId} | Reason: {Reason}",
            tenantId,
            paymentMethodId,
            reason);
    }

    private async Task<string> ResolveValidatedManualWalletProofFileKeyAsync(
        Guid tenantId,
        string ownerId,
        string ownerType,
        string? sessionId,
        Guid? customerId,
        Guid cartId,
        string? proofFileKey,
        Guid? orderId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(proofFileKey))
        {
            throw new CheckoutManualWalletProofException(
                "Manual wallet proof is required. Re-upload the transfer proof and try again.");
        }

        if (ManualWalletProofStorage.IsExternalAbsoluteUrl(proofFileKey) ||
            !ManualWalletProofStorage.TryNormalizeTenantScopedObjectKey(
                proofFileKey,
                tenantId,
                _r2Options,
                out var normalizedProofFileKey))
        {
            throw new CheckoutManualWalletProofException(
                "Manual wallet proof must reference a trusted private upload from this checkout.");
        }

        var ownerIsCustomer = string.Equals(ownerType, "customer", StringComparison.OrdinalIgnoreCase);
        var effectiveSessionId = string.IsNullOrWhiteSpace(sessionId) ? ownerId : sessionId;

        CheckoutSession? proofSession = null;
        if (orderId.HasValue)
        {
            var convertedSessionCandidates = _dbContext.CheckoutSessions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(session => session.TenantId == tenantId &&
                                  session.CartId == cartId &&
                                  session.Status == CheckoutSessionStatus.ConvertedToOrder &&
                                  session.ConvertedOrderId == orderId.Value)
                .Where(session => ownerIsCustomer
                    ? session.OnlineCustomerId == customerId
                    : session.SessionId == effectiveSessionId);

            proofSession = await CheckoutSessionService.OrderCanonicalSessions(convertedSessionCandidates)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var activeSessionCandidates = _dbContext.CheckoutSessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(session => session.TenantId == tenantId &&
                              session.CartId == cartId &&
                              session.InvalidatedAtUtc == null &&
                              (session.Status == CheckoutSessionStatus.Active ||
                               session.Status == CheckoutSessionStatus.Abandoned))
            .Where(session => ownerIsCustomer
                ? session.OnlineCustomerId == customerId
                : session.SessionId == effectiveSessionId);

        proofSession ??= await CheckoutSessionService.OrderCanonicalSessions(activeSessionCandidates)
            .FirstOrDefaultAsync(cancellationToken);

        if (proofSession == null)
        {
            _logger.LogWarning(
                "Manual wallet proof validation rejected because no authoritative checkout session matched | TenantId: {TenantId} | CartId: {CartId} | OwnerType: {OwnerType} | SessionIdPresent: {SessionIdPresent} | CustomerIdPresent: {CustomerIdPresent} | OrderId: {OrderId}",
                tenantId,
                cartId,
                ownerType,
                !string.IsNullOrWhiteSpace(effectiveSessionId),
                customerId.HasValue,
                orderId);

            throw new CheckoutManualWalletProofException(
                "Manual wallet proof is no longer attached to this checkout. Re-upload the proof and try again.");
        }

        if (!ManualWalletProofStorage.TryNormalizeTenantScopedObjectKey(
                proofSession.ProofImageUrl,
                tenantId,
                _r2Options,
                out var storedProofFileKey) ||
            !string.Equals(storedProofFileKey, normalizedProofFileKey, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Manual wallet proof validation rejected because stored proof does not match submitted key | TenantId: {TenantId} | CartId: {CartId} | CheckoutSessionId: {CheckoutSessionId} | OwnerType: {OwnerType} | OrderId: {OrderId}",
                tenantId,
                cartId,
                proofSession.Id,
                ownerType,
                orderId);

            throw new CheckoutManualWalletProofException(
                "Manual wallet proof is missing or has been replaced. Re-upload the proof and try again.");
        }

        return normalizedProofFileKey;
    }

    internal static string? GetStorefrontReferenceCode(
        string? paymentMethodCode,
        string? referenceCode) =>
        string.Equals(paymentMethodCode?.Trim(), "FAWRY", StringComparison.OrdinalIgnoreCase)
            ? referenceCode
            : null;

    #endregion

    #region Step 3: Cart Snapshot

    private async Task<CartSnapshot> GetCartSnapshotAsync(
        Guid tenantId,
        string ownerId,
        string ownerType,
        Guid cartId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var owner = new CartOwner { OwnerType = ownerType, OwnerId = ownerId };
            var cart = await _cartService.GetCartAsync(
                tenantId,
                owner,
                correlationId,
                cancellationToken);

            // Validate cart
            if (cart.CartId != cartId)
            {
                throw new CheckoutCartOwnershipException(cartId);
            }

            if (cart.TenantId != tenantId)
            {
                throw new CheckoutCartOwnershipException(cartId);
            }

            if (!string.Equals(cart.OwnerType, ownerType, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(cart.OwnerId, ownerId, StringComparison.Ordinal))
            {
                throw new CheckoutCartOwnershipException(cartId);
            }

            var persistedCouponState = await _dbContext.StorefrontCartCouponStates
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.TenantId == tenantId && item.CartId == cartId,
                    cancellationToken);

            ApplyPersistedCouponState(cart, persistedCouponState);

            if (cart.Items == null || cart.Items.Count == 0)
            {
                throw new CheckoutCartInvalidException(cartId, "Cart is empty");
            }

            // Create immutable snapshot
            // ARCHITECTURAL RULE: Snapshot captures FULL cart state including coupon
            // Checkout revalidates any applied coupon before order creation
            // PHASE 11.4.1: Snapshot includes VariantId for SKU-level checkout
            var snapshot = new CartSnapshot
            {
                CartId = cart.CartId,
                TenantId = cart.TenantId,
                OwnerType = cart.OwnerType,
                OwnerId = cart.OwnerId,
                Currency = cart.Currency,
                SnapshotTakenAtUtc = DateTime.UtcNow,
                CartUpdatedAtUtc = cart.LastUpdatedUtc,
                // Coupon state from cart (READ-ONLY, no revalidation)
                // PHASE: Shopify-grade Coupon Engine - CouponId is CRITICAL for checkout validation
                AppliedCouponId = cart.AppliedCoupon?.CouponId,
                AppliedCouponCode = cart.AppliedCouponCode,
                DiscountTotal = cart.DiscountTotal,
                CouponDiscountTotal = cart.DiscountTotal,
                // PHASE: Promotions Engine - Item-level promotion discounts
                PromotionDiscountTotal = cart.PromotionDiscountTotal,
                CartPromotionDiscountTotal = cart.CartPromotionDiscountTotal,
                ManualDiscountTotal = 0m,
                // AF-27A: Anti-tampering affiliate attribution from cart metadata
                AffiliateAccountId = cart.AffiliateAccountId,
                AffiliateReferralCode = cart.AffiliateReferralCode,
                AffiliateSource = cart.AffiliateSource,
                Items = cart.Items.Select(item => new CartItemSnapshot
                {
                    ProductId = item.ProductId,
                    // PHASE 11.4.1: Include VariantId for SKU-level validation
                    VariantId = item.VariantId,
                    SKU = item.SKU,
                    Name = item.Name,
                    NameAr = item.NameAr,
                    // PHASE 11.4.1: Include variant attributes for display
                    VariantTitle = item.VariantAttributes?.Count > 0 
                        ? string.Join(" / ", item.VariantAttributes.Select(a => a.Value)) 
                        : null,
                    VariantAttributes = item.VariantAttributes?.Select(a => new VariantAttributeSnapshot
                    {
                        Name = a.Name,
                        Value = a.Value
                    }).ToList(),
                    Quantity = item.Quantity,
                    CartPrice = item.UnitPrice,
                    CurrentPrice = 0, // Will be filled in price recalculation
                    CostPrice = 0,    // Will be filled in price recalculation
                    Weight = Math.Max(0m, item.Weight),
                    ThumbnailImage = item.ThumbnailImage
                }).ToList()
            };

            return snapshot;
        }
        catch (CheckoutException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Cart snapshot error | CartId: {CartId} | CorrelationId: {CorrelationId}",
                cartId, correlationId);
            throw new CheckoutCartInvalidException(cartId, ex.Message);
        }
    }

    private async Task PersistCartCheckoutIdempotencyAsync(
        Guid tenantId,
        string ownerId,
        string ownerType,
        Guid cartId,
        string checkoutIdempotencyKey,
        string snapshotHash,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _cartService.SetActiveCheckoutIdempotencyAsync(
                tenantId,
                new CartOwner { OwnerType = ownerType, OwnerId = ownerId },
                cartId,
                checkoutIdempotencyKey,
                snapshotHash,
                correlationId,
                cancellationToken);

            _logger.LogDebug(
                "Persisted active checkout idempotency key on cart | TenantId: {TenantId} | CartId: {CartId} | CheckoutId: {CheckoutId} | CorrelationId: {CorrelationId}",
                tenantId,
                cartId,
                checkoutIdempotencyKey,
                correlationId);
        }
        catch (CheckoutException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to persist checkout idempotency key on cart | TenantId: {TenantId} | CartId: {CartId} | CorrelationId: {CorrelationId}",
                tenantId,
                cartId,
                correlationId);
            throw new CheckoutCartInvalidException(cartId, "Failed to persist checkout idempotency state.");
        }
    }

    private static bool IsActiveCartOrderUniqueViolation(Exception exception) =>
        exception is DbUpdateException dbUpdateException &&
        dbUpdateException.InnerException is PostgresException postgresException &&
        postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
        string.Equals(postgresException.ConstraintName, ActiveCartOrderUniqueIndexName, StringComparison.Ordinal);

    private static bool IsDuplicateOutboxMessageUniqueViolation(DbUpdateException exception)
    {
        if (exception.InnerException is PostgresException postgresException)
        {
            return postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
                   (string.Equals(postgresException.ConstraintName, OutboxMessageInboxUniqueIndexName, StringComparison.Ordinal) ||
                    string.Equals(postgresException.ConstraintName, OutboxMessageOutboxUniqueIndexName, StringComparison.Ordinal) ||
                    string.Equals(postgresException.ConstraintName, OutboxMessagePrimaryKeyName, StringComparison.Ordinal));
        }

        if (exception.InnerException?.GetType().FullName == "Microsoft.Data.SqlClient.SqlException" &&
            exception.InnerException.GetType().GetProperty("Number")?.GetValue(exception.InnerException) is int number)
        {
            // For SQL Server, we can't easily check constraint names via reflection,
            // so we accept any unique violation (2627 or 2601) as a potential OutboxMessage duplicate.
            // This is acceptable since SQL Server is not the primary database for this application.
            return number is 2627 or 2601;
        }

        return false;
    }

    private static bool IsCheckoutTransactionSerializationFailure(Exception exception)
    {
        var current = exception;
        while (current != null)
        {
            if (current is PostgresException postgresException)
            {
                return postgresException.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected;
            }

            if (current.GetType().FullName == "Microsoft.Data.SqlClient.SqlException" &&
                current.GetType().GetProperty("Number")?.GetValue(current) is int sqlNumber)
            {
                return sqlNumber == 1205;
            }

            if (current is DbUpdateException dbUpdateException && dbUpdateException.InnerException != null)
            {
                if (IsCheckoutTransactionSerializationFailure(dbUpdateException.InnerException))
                {
                    return true;
                }
            }

            current = current.InnerException;
        }

        var message = exception.InnerException?.Message ?? exception.Message;
        return message.Contains("40001", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("serialization failure", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("40P01", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("deadlock", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyPersistedCouponState(CartDto cart, StorefrontCartCouponState? persistedState)
    {
        if (persistedState == null)
        {
            return;
        }

        if (!persistedState.HasAppliedCoupon)
        {
            cart.ClearCoupon();
            return;
        }

        if (!persistedState.CouponId.HasValue ||
            string.IsNullOrWhiteSpace(persistedState.CouponCode) ||
            string.IsNullOrWhiteSpace(persistedState.DiscountType) ||
            !persistedState.DiscountValue.HasValue ||
            !persistedState.DiscountAmount.HasValue)
        {
            cart.ClearCoupon();
            return;
        }

        cart.ApplyCouponDiscount(new AppliedCouponInfo
        {
            CouponId = persistedState.CouponId.Value,
            Code = persistedState.CouponCode,
            Type = persistedState.DiscountType,
            Value = persistedState.DiscountValue.Value,
            Amount = persistedState.DiscountAmount.Value
        });
    }

    private async Task<Guid?> ValidateCheckoutCallerOwnershipAsync(
        Guid tenantId,
        string ownerId,
        string ownerType,
        string? sessionId,
        Guid? customerId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (customerId.HasValue)
        {
            var customer = await _dbContext.OnlineCustomers
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == customerId.Value, cancellationToken);

            if (customer == null || customer.TenantId != tenantId)
            {
                _logger.LogWarning(
                    "Checkout tenant mismatch detected for customer ownership | TenantId: {TenantId} | CustomerId: {CustomerId} | OwnerType: {OwnerType} | CorrelationId: {CorrelationId}",
                    tenantId,
                    customerId.Value,
                    ownerType,
                    correlationId);
                throw new UnauthorizedAccessException(TenantErrorCodes.TenantMismatch);
            }

            if (!customer.IsActive)
            {
                _logger.LogWarning(
                    "Checkout blocked because authenticated online customer is inactive | TenantId: {TenantId} | CustomerId: {CustomerId} | OwnerType: {OwnerType} | CorrelationId: {CorrelationId}",
                    tenantId,
                    customerId.Value,
                    ownerType,
                    correlationId);
                throw new CheckoutCustomerInactiveException(customerId.Value);
            }

            if (!string.Equals(ownerType, "customer", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(ownerId, customerId.Value.ToString("N"), StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Checkout owner mismatch detected for authenticated customer | TenantId: {TenantId} | CustomerId: {CustomerId} | OwnerType: {OwnerType} | OwnerId: {OwnerId} | CorrelationId: {CorrelationId}",
                    tenantId,
                    customerId.Value,
                    ownerType,
                    ownerId,
                    correlationId);
                throw new UnauthorizedAccessException(TenantErrorCodes.TenantMismatch);
            }

            return customer.Id;
        }

        if (!string.Equals(ownerType, "session", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(sessionId) ||
            !string.Equals(ownerId, sessionId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Checkout session ownership mismatch detected | TenantId: {TenantId} | OwnerType: {OwnerType} | OwnerId: {OwnerId} | SessionId: {SessionId} | CorrelationId: {CorrelationId}",
                tenantId,
                ownerType,
                ownerId,
                sessionId,
                correlationId);
            throw new UnauthorizedAccessException(TenantErrorCodes.TenantMismatch);
        }

        return null;
    }

    #endregion

    #region Step 4: Price Recalculation

    /// <summary>
    /// PHASE 11.4.1 — VARIANT-BASED PRICE RECALCULATION
    /// 
    /// CRITICAL RULES:
    /// - A Product is NEVER sold. A ProductVariant (SKU) is ALWAYS sold.
    /// - Prices come from Variant.OnlinePrice ?? Variant.Price (NEVER Product.Price)
    /// - Cart items with VariantId → load from IProductVariantReadRepository
    /// - Legacy items without VariantId → automatically map to default variant
    /// </summary>
    private async Task<(CartSnapshot Snapshot, bool PricesRefreshed)> RecalculatePricesAsync(
        CartSnapshot snapshot,
        Guid tenantId,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (recalculatedSnapshot, pricesRefreshed, priceChanges, originalSubtotal, newSubtotal, overallDiffPercent) =
            await RecalculatePricesInternalAsync(
                snapshot,
                tenantId,
                correlationId,
                cancellationToken,
                lockAuthorityRows: false);

        // Determine if price recalculation is accepted (checkout can proceed)
        bool accepted = !(overallDiffPercent > PriceChangeThresholdPercent || pricesRefreshed);

        // Emit price recalculation event with correct acceptance status
        await EmitPriceRecalculationEventAsync(
            tenantId, snapshot.CartId, originalSubtotal, newSubtotal, overallDiffPercent,
            accepted, priceChanges,
            requestId, correlationId, cancellationToken);

        if (overallDiffPercent > PriceChangeThresholdPercent)
        {
            _logger.LogWarning(
                "Price change exceeded threshold; checkout blocked until the cart is refreshed from authoritative catalog | CartId: {CartId} | Original: {Original} | New: {New} | Diff: {Diff}% | Threshold: {Threshold}% | CorrelationId: {CorrelationId}",
                snapshot.CartId, originalSubtotal, newSubtotal, overallDiffPercent, PriceChangeThresholdPercent, correlationId);

            throw new CheckoutPriceChangedException(originalSubtotal, newSubtotal, overallDiffPercent);
        }
        else if (pricesRefreshed)
        {
            _logger.LogWarning(
                "Price change detected; checkout blocked until the cart is refreshed from authoritative catalog | CartId: {CartId} | Original: {Original} | New: {New} | Diff: {Diff}% | CorrelationId: {CorrelationId}",
                snapshot.CartId, originalSubtotal, newSubtotal, overallDiffPercent, correlationId);

            throw new CheckoutPriceChangedException(originalSubtotal, newSubtotal, overallDiffPercent);
        }

        return (recalculatedSnapshot, pricesRefreshed);
    }

    /// <summary>
    /// Read-only price recalculation for preview requests - does NOT emit events
    /// </summary>
    private async Task<(CartSnapshot Snapshot, bool PricesRefreshed)> RecalculatePricesReadOnlyAsync(
        CartSnapshot snapshot,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (recalculatedSnapshot, pricesRefreshed, _, _, _, _) =
            await RecalculatePricesInternalAsync(
                snapshot,
                tenantId,
                correlationId,
                cancellationToken,
                lockAuthorityRows: false);

        return (recalculatedSnapshot, pricesRefreshed);
    }

    /// <summary>
    /// Core price recalculation logic - shared by both event-emitting and read-only paths
    /// </summary>
    private async Task<(CartSnapshot Snapshot, bool PricesRefreshed, List<PriceChangeItem> PriceChanges, decimal OriginalSubtotal, decimal NewSubtotal, decimal OverallDiffPercent)> RecalculatePricesInternalAsync(
        CartSnapshot snapshot,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken,
        bool lockAuthorityRows)
    {
        // =================================================================
        // PHASE 11.4.1: VARIANT-CENTRIC PRICE VALIDATION
        // =================================================================
        _logger.LogInformation(
            "Checkout recalculating prices from Variants | TenantId: {TenantId} | CartId: {CartId} | ItemCount: {ItemCount} | CorrelationId: {CorrelationId}",
            tenantId, snapshot.CartId, snapshot.Items.Count, correlationId);

        var legacyItems = snapshot.Items.Where(i => !i.VariantId.HasValue).ToList();

        // PHASE 11.4.1: For legacy items without VariantId, get default variants by ProductId
        foreach (var item in legacyItems)
        {
            var (product, variant) = await _variantRead.GetDefaultVariantAsync(
                tenantId, item.ProductId, cancellationToken);
            
            if (product != null && variant != null)
            {
                // Auto-map legacy item to default variant
                item.VariantId = variant.VariantId;
                item.SKU = variant.SKU ?? product.SKU ?? item.SKU;
                
                _logger.LogDebug(
                    "Legacy item auto-mapped to default variant | ProductId: {ProductId} | VariantId: {VariantId} | CorrelationId: {CorrelationId}",
                    item.ProductId, variant.VariantId, correlationId);
            }
        }

        var authoritativeVariants = await LoadAuthoritativeCheckoutVariantLookupAsync(
            tenantId,
            snapshot.Items
                .Where(item => item.VariantId.HasValue)
                .Select(item => item.VariantId!.Value)
                .ToList(),
            cancellationToken,
            lockAuthorityRows);
        var categoryLineage = await CategoryLineageAuthority.LoadTenantSnapshotAsync(
            _dbContext,
            tenantId,
            cancellationToken);

        var priceChanges = new List<PriceChangeItem>();
        decimal originalSubtotal = 0;
        decimal newSubtotal = 0;

        foreach (var item in snapshot.Items)
        {
            if (!item.VariantId.HasValue || !authoritativeVariants.TryGetValue(item.VariantId.Value, out var authoritativeVariant))
            {
                throw new CheckoutCartInvalidException(snapshot.CartId,
                    $"Variant {item.SKU} is no longer available");
            }

            if (authoritativeVariant.ProductId != item.ProductId)
            {
                throw new CheckoutCartInvalidException(snapshot.CartId,
                    $"Variant {item.SKU} does not belong to the selected product");
            }

            if (!authoritativeVariant.ProductIsActive ||
                !authoritativeVariant.ProductIsOnlineVisible ||
                !authoritativeVariant.ProductIsPublished ||
                !categoryLineage.IsLiveInLineage(authoritativeVariant.CategoryId))
            {
                throw new CheckoutCartInvalidException(snapshot.CartId,
                    $"Product {authoritativeVariant.ProductName} is no longer available");
            }

            if (!authoritativeVariant.VariantIsActive || !authoritativeVariant.VariantIsCurrentSemanticOwner)
            {
                throw new CheckoutCartInvalidException(snapshot.CartId,
                    $"Variant {item.SKU} is no longer available");
            }

            item.SKU = authoritativeVariant.SKU ?? item.SKU;
            item.Name = authoritativeVariant.ProductName;
            item.CurrentPrice = authoritativeVariant.EffectivePrice;
            item.CostPrice = authoritativeVariant.CostPrice;
            item.Weight = Math.Max(0m, authoritativeVariant.Weight);
            item.CategoryId = authoritativeVariant.CategoryId;

            _logger.LogDebug(
                "Variant price validated from SQL authority | VariantId: {VariantId} | SKU: {SKU} | Price: {Price} | CorrelationId: {CorrelationId}",
                authoritativeVariant.VariantId, item.SKU, item.CurrentPrice, correlationId);

            originalSubtotal += item.CartPrice * item.Quantity;
            newSubtotal += item.CurrentPrice * item.Quantity;

            // Track price changes
            if (item.CartPrice != item.CurrentPrice)
            {
                var diffPercent = item.CartPrice > 0 
                    ? (item.CurrentPrice - item.CartPrice) / item.CartPrice * 100 
                    : 0;

                priceChanges.Add(new PriceChangeItem
                {
                    ProductId = item.ProductId,
                    SKU = item.SKU,
                    OldPrice = item.CartPrice,
                    NewPrice = item.CurrentPrice,
                    DifferencePercent = diffPercent
                });
            }
        }

        // Calculate overall difference
        var overallDiffPercent = originalSubtotal > 0 
            ? Math.Abs((newSubtotal - originalSubtotal) / originalSubtotal * 100) 
            : 0;

        var pricesRefreshed = priceChanges.Count > 0;

        return (snapshot, pricesRefreshed, priceChanges, originalSubtotal, newSubtotal, overallDiffPercent);
    }

    private async Task<Dictionary<Guid, AuthoritativeCheckoutVariant>> LoadAuthoritativeCheckoutVariantLookupAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken,
        bool lockAuthorityRows = false)
    {
        var distinctVariantIds = variantIds
            .Where(variantId => variantId != Guid.Empty)
            .Distinct()
            .ToList();

        if (distinctVariantIds.Count == 0)
        {
            return new Dictionary<Guid, AuthoritativeCheckoutVariant>();
        }

        if (lockAuthorityRows)
        {
            await LockCheckoutCatalogAuthorityRowsAsync(
                tenantId,
                distinctVariantIds,
                cancellationToken);
        }

        return await _dbContext.ProductVariants
            .AsNoTracking()
            .Where(variant =>
                variant.TenantId == tenantId &&
                variant.Product.TenantId == tenantId &&
                distinctVariantIds.Contains(variant.Id) &&
                !variant.IsDeleted &&
                !variant.Product.IsDeleted)
            .Select(variant => new AuthoritativeCheckoutVariant(
                variant.Id,
                variant.ProductId,
                variant.Product.Name,
                variant.SKU,
                variant.OnlinePrice.HasValue && variant.OnlinePrice.Value > 0m
                    ? variant.OnlinePrice.Value
                    : variant.Price,
                variant.CostPrice,
                variant.Weight ?? variant.Product.Weight,
                variant.Product.CategoryId,
                variant.Product.IsActive,
                variant.Product.IsOnlineVisible,
                variant.Product.PublishStatus == PublishStatus.Published,
                variant.IsActive,
                variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
                    variant.SemanticSupersededAtUtc == null &&
                    variant.SemanticReplacementVariantId == null,
                variant.Product.TrackStock,
                variant.Product.AllowBackorder))
            .ToDictionaryAsync(variant => variant.VariantId, cancellationToken);
    }

    private async Task LockCheckoutCatalogAuthorityRowsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            return;
        }

        // Batch lock all variants in deterministic order
        var orderedVariantIds = variantIds.OrderBy(id => id).ToList();
        var lockedVariants = await LockCheckoutVariantAuthorityRowsBatchAsync(
            tenantId,
            orderedVariantIds,
            cancellationToken);

        // Extract distinct product IDs and batch lock them in deterministic order
        var productIds = lockedVariants
            .Select(v => v.ProductId)
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        if (productIds.Count > 0)
        {
            await LockCheckoutProductAuthorityRowsBatchAsync(
                tenantId,
                productIds,
                cancellationToken);
        }
    }

    private async Task<List<(Guid VariantId, Guid ProductId)>> LockCheckoutVariantAuthorityRowsBatchAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken)
    {
        if (variantIds.Count == 0)
        {
            return new List<(Guid, Guid)>();
        }

        if (IsPostgresProvider())
        {
            var variantIdsList = string.Join(", ", variantIds.Select(id => $"'{id}'"));
            var variants = await _dbContext.ProductVariants
                .FromSqlRaw($@"
                    SELECT *
                    FROM ""ProductVariants""
                    WHERE ""TenantId"" = '{tenantId}'
                      AND ""Id"" IN ({variantIdsList})
                      AND ""IsDeleted"" = false
                    ORDER BY ""Id""
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .Select(v => new { v.Id, v.ProductId })
                .ToListAsync(cancellationToken);

            return variants.Select(v => (v.Id, v.ProductId)).ToList();
        }

        if (IsSqlServerProvider())
        {
            var variantIdsList = string.Join(", ", variantIds.Select(id => $"'{id}'"));
            var variants = await _dbContext.ProductVariants
                .FromSqlRaw($@"
                    SELECT *
                    FROM [ProductVariants] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = '{tenantId}'
                      AND [Id] IN ({variantIdsList})
                      AND [IsDeleted] = CAST(0 AS bit)
                    ORDER BY [Id]")
                .IgnoreQueryFilters()
                .Select(v => new { v.Id, v.ProductId })
                .ToListAsync(cancellationToken);

            return variants.Select(v => (v.Id, v.ProductId)).ToList();
        }

        var providerName = _dbContext.Database.ProviderName ?? "Unknown";
        if (_requireRowLevelLocking)
        {
            throw new InvalidOperationException(
                $"Database provider '{providerName}' is not supported for production checkout catalog authority locking. " +
                "Row-level locking is required to keep final checkout pricing aligned with authoritative catalog mutations.");
        }

        var fallbackVariants = await _dbContext.ProductVariants
            .IgnoreQueryFilters()
            .Where(variant => variant.TenantId == tenantId && variantIds.Contains(variant.Id) && !variant.IsDeleted)
            .OrderBy(variant => variant.Id)
            .Select(variant => new { variant.Id, variant.ProductId })
            .ToListAsync(cancellationToken);

        return fallbackVariants.Select(v => (v.Id, v.ProductId)).ToList();
    }

    private async Task LockCheckoutProductAuthorityRowsBatchAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return;
        }

        if (IsPostgresProvider())
        {
            var productIdsList = string.Join(", ", productIds.Select(id => $"'{id}'"));
            await _dbContext.Products
                .FromSqlRaw($@"
                    SELECT xmin, *
                    FROM ""Products""
                    WHERE ""TenantId"" = '{tenantId}'
                      AND ""Id"" IN ({productIdsList})
                      AND ""IsDeleted"" = false
                    ORDER BY ""Id""
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .ToListAsync(cancellationToken);
            return;
        }

        if (IsSqlServerProvider())
        {
            var productIdsList = string.Join(", ", productIds.Select(id => $"'{id}'"));
            await _dbContext.Products
                .FromSqlRaw($@"
                    SELECT *
                    FROM [Products] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = '{tenantId}'
                      AND [Id] IN ({productIdsList})
                      AND [IsDeleted] = CAST(0 AS bit)
                    ORDER BY [Id]")
                .IgnoreQueryFilters()
                .ToListAsync(cancellationToken);
            return;
        }

        var providerName = _dbContext.Database.ProviderName ?? "Unknown";
        if (_requireRowLevelLocking)
        {
            throw new InvalidOperationException(
                $"Database provider '{providerName}' is not supported for production checkout product authority locking. " +
                "Row-level locking is required to keep final checkout pricing aligned with authoritative catalog mutations.");
        }

        await _dbContext.Products
            .IgnoreQueryFilters()
            .Where(product => product.TenantId == tenantId && productIds.Contains(product.Id) && !product.IsDeleted)
            .OrderBy(product => product.Id)
            .Select(product => product.Id)
            .ToListAsync(cancellationToken);
    }

    private CheckoutPromotionCalculationException CreatePromotionCalculationFailureException(
        string surface,
        string correlationId,
        Exception exception)
    {
        _reliabilityTelemetry?.RecordPromotionEngineFailure(surface, exception.GetType().Name);
        _logger.LogError(
            exception,
            "[Promotions] Promotion calculation failed - checkout blocked | Surface: {Surface} | CorrelationId: {CorrelationId}",
            surface,
            correlationId);

        return new CheckoutPromotionCalculationException(
            "Promotion calculation failed. Please refresh pricing and try again.",
            exception);
    }

    private async Task EnsureStorefrontAcceptingOrdersAsync(
        Guid tenantId,
        string correlationId,
        Guid? customerId,
        bool isAffiliateCheckout,
        bool lockAuthorityRow,
        CancellationToken cancellationToken)
    {
        string? storefrontStatus;
        bool? enableGuestCheckout;

        try
        {
            if (lockAuthorityRow)
            {
                storefrontStatus = await LoadStorefrontStatusForUpdateAsync(tenantId, cancellationToken);
                enableGuestCheckout = await _dbContext.StoreConfigs
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(config => config.TenantId == tenantId)
                    .Select(config => (bool?)config.Behavior.EnableGuestCheckout)
                    .FirstOrDefaultAsync(cancellationToken);
            }
            else
            {
                var authority = await _dbContext.StoreConfigs
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(config => config.TenantId == tenantId)
                    .Select(config => new
                    {
                        config.StorefrontStatus,
                        config.Behavior.EnableGuestCheckout,
                    })
                    .FirstOrDefaultAsync(cancellationToken);
                storefrontStatus = authority?.StorefrontStatus;
                enableGuestCheckout = authority?.EnableGuestCheckout;
            }
        }
        catch (CheckoutException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to load SQL storefront availability authority | TenantId: {TenantId} | LockAuthorityRow: {LockAuthorityRow} | CorrelationId: {CorrelationId}",
                tenantId,
                lockAuthorityRow,
                correlationId);

            throw new CheckoutException(
                "STOREFRONT_AVAILABILITY_UNCERTAIN",
                "Storefront availability could not be verified. Please try again.",
                "StorefrontAvailability",
                ex);
        }

        if (!string.Equals(storefrontStatus?.Trim(), "live", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Checkout rejected because SQL storefront authority is not live | TenantId: {TenantId} | StorefrontStatus: {StorefrontStatus} | LockAuthorityRow: {LockAuthorityRow} | CorrelationId: {CorrelationId}",
                tenantId,
                string.IsNullOrWhiteSpace(storefrontStatus) ? "(missing)" : storefrontStatus,
                lockAuthorityRow,
                correlationId);

            throw new CheckoutStorefrontUnavailableException(storefrontStatus);
        }

        if (!customerId.HasValue && !isAffiliateCheckout && enableGuestCheckout != true)
        {
            _logger.LogWarning(
                "Guest checkout rejected by SQL StoreConfig authority | TenantId: {TenantId} | LockAuthorityRow: {LockAuthorityRow} | CorrelationId: {CorrelationId}",
                tenantId,
                lockAuthorityRow,
                correlationId);
            throw new CheckoutGuestDisabledException();
        }
    }

    private async Task<string?> LoadStorefrontStatusForUpdateAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (IsPostgresProvider())
        {
            return await _dbContext.Database
                .SqlQuery<string>($@"
                    SELECT ""StorefrontStatus"" AS ""Value""
                    FROM ""StoreConfigs""
                    WHERE ""TenantId"" = {tenantId}
                    FOR UPDATE")
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (IsSqlServerProvider())
        {
            return await _dbContext.Database
                .SqlQuery<string>($@"
                    SELECT [StorefrontStatus] AS [Value]
                    FROM [StoreConfigs] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}")
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await _dbContext.StoreConfigs
            .IgnoreQueryFilters()
            .Where(config => config.TenantId == tenantId)
            .Select(config => config.StorefrontStatus)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<bool> ReconcileCheckoutDiscountAuthorityAsync(
        Guid tenantId,
        Guid? customerId,
        CartSnapshot snapshot,
        Guid cartId,
        string correlationId,
        CancellationToken cancellationToken,
        string mode,
        ICollection<CheckoutStepResult>? steps = null,
        Stopwatch? stopwatch = null,
        string? requestId = null,
        Action<string>? activeStepTracker = null)
    {
        activeStepTracker?.Invoke("CouponValidation");

        if (!string.IsNullOrWhiteSpace(snapshot.AppliedCouponCode) &&
            !snapshot.AppliedCouponId.HasValue)
        {
            if (mode == "checkout" && steps != null && stopwatch != null && requestId != null)
            {
                await EmitCheckoutFailedEventAsync(
                    tenantId,
                    Guid.Empty,
                    cartId,
                    "CouponValidation",
                    PointOfSale.Core.Discounts.CouponErrorCodes.Invalid,
                    "Applied coupon is missing its server-issued identity.",
                    false,
                    stopwatch.ElapsedMilliseconds,
                    requestId,
                    correlationId,
                    cancellationToken);
            }

            throw new CheckoutCartInvalidException(
                cartId,
                "Applied coupon is missing its server-issued identity.");
        }

        if (snapshot.AppliedCouponId.HasValue &&
            string.IsNullOrWhiteSpace(snapshot.AppliedCouponCode))
        {
            if (mode == "checkout" && steps != null && stopwatch != null && requestId != null)
            {
                await EmitCheckoutFailedEventAsync(
                    tenantId,
                    Guid.Empty,
                    cartId,
                    "CouponValidation",
                    PointOfSale.Core.Discounts.CouponErrorCodes.Invalid,
                    "Applied coupon is missing its code.",
                    false,
                    stopwatch.ElapsedMilliseconds,
                    requestId,
                    correlationId,
                    cancellationToken);
            }

            throw new CheckoutCartInvalidException(
                cartId,
                "Applied coupon is missing its code.");
        }

        decimal? authoritativeCouponDiscountAmount = null;
        string? authoritativeCouponDiscountType = null;
        decimal? authoritativeCouponDiscountValue = null;
        var recalculatedSubtotal = snapshot.Items.Sum(item => item.CurrentPrice * item.Quantity);

        if (snapshot.AppliedCouponId.HasValue &&
            !string.IsNullOrWhiteSpace(snapshot.AppliedCouponCode))
        {
            var couponValidation = await _couponValidationService.ValidateCouponByIdAsync(
                tenantId,
                snapshot.AppliedCouponId.Value,
                snapshot.AppliedCouponCode,
                customerId,
                BuildCouponValidationSnapshot(snapshot, recalculatedSubtotal),
                correlationId,
                cancellationToken: cancellationToken);

            steps?.Add(new CheckoutStepResult
            {
                StepName = "CouponValidation",
                Success = couponValidation.IsValid,
                ErrorMessage = couponValidation.ErrorMessage,
                DurationMs = stopwatch?.ElapsedMilliseconds ?? 0
            });

            if (!couponValidation.IsValid)
            {
                if (mode == "checkout" && stopwatch != null && requestId != null)
                {
                    await EmitCheckoutFailedEventAsync(
                        tenantId,
                        Guid.Empty,
                        cartId,
                        "CouponValidation",
                        couponValidation.ErrorCode ?? PointOfSale.Core.Discounts.CouponErrorCodes.Invalid,
                        couponValidation.ErrorMessage ?? "Applied coupon is invalid.",
                        false,
                        stopwatch.ElapsedMilliseconds,
                        requestId,
                        correlationId,
                        cancellationToken);
                }

                throw new CheckoutCartInvalidException(
                    cartId,
                    couponValidation.ErrorMessage ?? "Applied coupon is invalid.");
            }

            snapshot.DiscountTotal = couponValidation.CalculatedDiscountAmount ?? 0m;
            snapshot.CouponDiscountTotal = snapshot.DiscountTotal;
            authoritativeCouponDiscountAmount = couponValidation.CalculatedDiscountAmount;
            authoritativeCouponDiscountType = couponValidation.DiscountType;
            authoritativeCouponDiscountValue = couponValidation.DiscountValue;
        }
        else
        {
            steps?.Add(new CheckoutStepResult
            {
                StepName = "CouponValidation",
                Success = true,
                DurationMs = stopwatch?.ElapsedMilliseconds ?? 0
            });
        }

        activeStepTracker?.Invoke("PromotionCalculation");

        try
        {
            var promotionResult = await _promotionEngine.CalculateAsync(
                BuildPromotionAuthoritySnapshot(
                    tenantId,
                    customerId,
                    snapshot,
                    recalculatedSubtotal,
                    authoritativeCouponDiscountAmount,
                    authoritativeCouponDiscountType,
                    authoritativeCouponDiscountValue),
                DateTime.UtcNow,
                correlationId,
                cancellationToken);

            var promotionCalculationFailed = ApplyPromotionResultOrThrow(
                snapshot,
                promotionResult,
                exception: null,
                tenantId,
                mode,
                correlationId);

            steps?.Add(new CheckoutStepResult
            {
                StepName = "PromotionCalculation",
                Success = true,
                DurationMs = stopwatch?.ElapsedMilliseconds ?? 0
            });

            return promotionCalculationFailed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            steps?.Add(new CheckoutStepResult
            {
                StepName = "PromotionCalculation",
                Success = false,
                ErrorMessage = "Promotion calculation failed",
                DurationMs = stopwatch?.ElapsedMilliseconds ?? 0
            });

            // Preserve DomainGuardException for middleware to handle with structured error response
            if (ex is DomainGuardException)
            {
                throw;
            }

            return ApplyPromotionResultOrThrow(
                snapshot,
                promotionResult: null,
                ex,
                tenantId,
                mode,
                correlationId);
        }
    }

    private static CouponValidationSnapshot BuildCouponValidationSnapshot(
        CartSnapshot snapshot,
        decimal subtotal)
    {
        return new CouponValidationSnapshot
        {
            OrderSubtotal = subtotal,
            CartItems = snapshot.Items
                .Select(item => new CouponScopeValidationItem(
                    item.ProductId,
                    item.CategoryId ?? Guid.Empty))
                .ToList()
        };
    }

    private static CartSnapshotForPromotion BuildPromotionAuthoritySnapshot(
        Guid tenantId,
        Guid? customerId,
        CartSnapshot snapshot,
        decimal subtotal,
        decimal? authoritativeCouponDiscountAmount,
        string? authoritativeCouponDiscountType,
        decimal? authoritativeCouponDiscountValue)
    {
        return new CartSnapshotForPromotion
        {
            TenantId = tenantId,
            CustomerId = customerId,
            Subtotal = subtotal,
            Items = snapshot.Items.Select(item => new CartItemForPromotion
            {
                ProductId = item.ProductId,
                VariantId = item.VariantId,
                CategoryId = item.CategoryId,
                Quantity = item.Quantity,
                UnitPrice = item.CurrentPrice
            }).ToList(),
            AppliedCouponCode = snapshot.AppliedCouponCode,
            AppliedCouponId = snapshot.AppliedCouponId,
            PrevalidatedCouponDiscountAmount = authoritativeCouponDiscountAmount,
            PrevalidatedCouponDiscountType = authoritativeCouponDiscountType,
            PrevalidatedCouponDiscountValue = authoritativeCouponDiscountValue
        };
    }

    /// <summary>
    /// Applies the authoritative promotion result to the cart snapshot.
    /// Customer-visible checkout pricing fails closed when promotion calculation cannot be trusted.
    /// </summary>
    /// <param name="recalculatedSnapshot">The cart snapshot to update</param>
    /// <param name="promotionResult">The promotion result (null if calculation failed)</param>
    /// <param name="exception">The exception if promotion calculation failed (null if successful)</param>
    /// <param name="tenantId">The tenant identifier</param>
    /// <param name="mode">The operation mode ("preview" or "checkout")</param>
    /// <param name="correlationId">The correlation identifier for logging</param>
    /// <returns>False when promotion calculation succeeded.</returns>
    private bool ApplyPromotionResultOrThrow(
        CartSnapshot recalculatedSnapshot,
        PromotionResult? promotionResult,
        Exception? exception,
        Guid tenantId,
        string mode,
        string correlationId)
    {
        // Customer-visible pricing must not silently drop promotions after a calculation failure.
        if (exception != null)
        {
            // Preserve DomainGuardException for middleware to handle with structured error response
            if (exception is DomainGuardException)
            {
                throw exception;
            }

            _reliabilityTelemetry?.RecordPromotionFailClosed(tenantId, mode, exception.GetType().Name);
            throw CreatePromotionCalculationFailureException(mode, correlationId, exception);
        }

        // No exception - apply promotion result or clear fields if no promotion won
        if (promotionResult != null && promotionResult.HasDiscount)
        {
            recalculatedSnapshot.DiscountTotal = 0m;
            recalculatedSnapshot.CouponDiscountTotal = 0m;
            recalculatedSnapshot.PromotionDiscountTotal = 0m;
            recalculatedSnapshot.CartPromotionDiscountTotal = 0m;

            if (promotionResult.PromotionTypeString == "offer")
            {
                recalculatedSnapshot.PromotionDiscountTotal = promotionResult.DiscountAmount;
                recalculatedSnapshot.AppliedPromotionId = promotionResult.PromotionId;
                recalculatedSnapshot.AppliedPromotionType = promotionResult.PromotionTypeString;
                recalculatedSnapshot.AppliedPromotionName = promotionResult.PromotionName;
                recalculatedSnapshot.AppliedCouponId = null;
                recalculatedSnapshot.AppliedCouponCode = null;
            }
            else
            {
                recalculatedSnapshot.DiscountTotal = promotionResult.DiscountAmount;
                recalculatedSnapshot.CouponDiscountTotal = promotionResult.DiscountAmount;
                recalculatedSnapshot.AppliedPromotionId = null;
                recalculatedSnapshot.AppliedPromotionType = null;
                recalculatedSnapshot.AppliedPromotionName = null;
            }
            
            var logLevel = mode == "preview" ? LogLevel.Debug : LogLevel.Information;
            _logger.Log(logLevel,
                "[{Mode}] Promotion applied | Type: {Type} | Name: {Name} | Discount: {Discount} | CorrelationId: {CorrelationId}",
                mode == "preview" ? "Preview" : "Promotions",
                promotionResult.PromotionTypeString, 
                promotionResult.PromotionName, 
                promotionResult.DiscountAmount, 
                correlationId);
        }
        else
        {
            // No promotion won - clear all server-side discount state
            ClearAllPromotionFields(recalculatedSnapshot);
            
            if (mode == "checkout")
            {
                _logger.LogDebug(
                    "[Promotions] No applicable promotions | CorrelationId: {CorrelationId}",
                    correlationId);
            }
        }

        return false; // Promotion calculation succeeded
    }

    /// <summary>
    /// Clears all promotion and discount fields from the cart snapshot.
    /// </summary>
    private void ClearAllPromotionFields(CartSnapshot snapshot)
    {
        snapshot.DiscountTotal = 0m;
        snapshot.CouponDiscountTotal = 0m;
        snapshot.PromotionDiscountTotal = 0m;
        snapshot.CartPromotionDiscountTotal = 0m;
        snapshot.AppliedPromotionId = null;
        snapshot.AppliedPromotionType = null;
        snapshot.AppliedPromotionName = null;
        snapshot.AppliedCouponId = null;
        snapshot.AppliedCouponCode = null;
    }

    private void EnsureExpectedPricingMatches(
        Guid tenantId,
        CheckoutExpectedPricingDto? expectedPricing,
        StorefrontPricingBreakdown actualPricing,
        string correlationId)
    {
        if (expectedPricing == null)
        {
            _reliabilityTelemetry?.RecordCheckoutTotalMismatch(tenantId, "expected_pricing_missing");
            throw new CheckoutTotalMismatchException(new[] { "expected_pricing_missing" });
        }

        var mismatchedFields = new List<string>();
        var normalizedExpectedPromotionDiscount = RoundCurrency(
            expectedPricing.PromotionDiscount > 0m && expectedPricing.CartPromotionDiscount > 0m
                ? expectedPricing.PromotionDiscount + expectedPricing.CartPromotionDiscount
                : Math.Max(expectedPricing.PromotionDiscount, expectedPricing.CartPromotionDiscount));
        AddExpectedPricingMismatchIfNeeded(mismatchedFields, "subtotal", expectedPricing.Subtotal, actualPricing.Subtotal);
        AddExpectedPricingMismatchIfNeeded(mismatchedFields, "discount", expectedPricing.Discount, actualPricing.Discount);
        AddExpectedPricingMismatchIfNeeded(mismatchedFields, "coupon_discount", expectedPricing.CouponDiscount, actualPricing.CouponDiscount);
        AddExpectedPricingMismatchIfNeeded(mismatchedFields, "promotion_discount", normalizedExpectedPromotionDiscount, actualPricing.PromotionDiscount);
        AddExpectedPricingMismatchIfNeeded(mismatchedFields, "cart_promotion_discount", expectedPricing.CartPromotionDiscount, actualPricing.CartPromotionDiscount);

        AddExpectedPricingMismatchIfNeeded(mismatchedFields, "shipping", expectedPricing.Shipping, actualPricing.Shipping);
        AddExpectedPricingMismatchIfNeeded(mismatchedFields, "tax", expectedPricing.Tax, actualPricing.Tax);
        AddExpectedPricingMismatchIfNeeded(mismatchedFields, "payment_fee", expectedPricing.PaymentFee, actualPricing.PaymentFee);
        AddExpectedPricingMismatchIfNeeded(mismatchedFields, "manual_discount", expectedPricing.ManualDiscount, actualPricing.ManualDiscount);
        AddExpectedPricingMismatchIfNeeded(mismatchedFields, "total", expectedPricing.Total, actualPricing.Total);

        if (mismatchedFields.Count == 0)
        {
            return;
        }

        foreach (var mismatchedField in mismatchedFields)
        {
            _reliabilityTelemetry?.RecordCheckoutTotalMismatch(tenantId, mismatchedField);
        }

        _logger.LogWarning(
            "Checkout total mismatch detected | TenantId: {TenantId} | Fields: {Fields} | ExpectedSubtotal: {ExpectedSubtotal} | ActualSubtotal: {ActualSubtotal} | ExpectedDiscount: {ExpectedDiscount} | ActualDiscount: {ActualDiscount} | ExpectedTotal: {ExpectedTotal} | ActualTotal: {ActualTotal} | CorrelationId: {CorrelationId}",
            tenantId,
            string.Join(",", mismatchedFields),
            RoundCurrency(expectedPricing.Subtotal),
            RoundCurrency(actualPricing.Subtotal),
            RoundCurrency(expectedPricing.Discount),
            RoundCurrency(actualPricing.Discount),
            RoundCurrency(expectedPricing.Total),
            RoundCurrency(actualPricing.Total),
            correlationId);

        throw new CheckoutTotalMismatchException(mismatchedFields);
    }

    private static void AddExpectedPricingMismatchIfNeeded(
        ICollection<string> mismatchedFields,
        string field,
        decimal expected,
        decimal actual)
    {
        if (RoundCurrency(expected) != RoundCurrency(actual))
        {
            mismatchedFields.Add(field);
        }
    }

    #endregion

    #region Step 5: Inventory Validation & Reservation

    private async Task<ShippingMethodDocument> ValidateShippingMethodAsync(
        Guid shippingMethodId,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken,
        bool lockAuthorityRow = false)
    {
        var shippingMethod = await LoadShippingMethodAuthorityAsync(
            shippingMethodId,
            tenantId,
            lockAuthorityRow,
            cancellationToken);

        if (shippingMethod == null)
        {
            _logger.LogWarning(
                "Shipping method not found in SQL authority | ShippingMethodId: {ShippingMethodId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                shippingMethodId, tenantId, correlationId);
            throw new CheckoutShippingMethodException(shippingMethodId, "Not found");
        }

        if (!shippingMethod.IsActive)
        {
            throw new CheckoutShippingMethodException(shippingMethodId, "Not currently available");
        }

        var serviceLevelReadiness = await _carrierIntegrationAuthority.GetServiceLevelReadinessAsync(
            tenantId,
            shippingMethod.CarrierCode,
            shippingMethod.ServiceLevel,
            cancellationToken);
        if (!serviceLevelReadiness.CanExecute)
        {
            throw new CheckoutShippingMethodException(shippingMethodId, "Carrier service level is not currently available");
        }

        if (!CarrierCodes.IsManual(shippingMethod.CarrierCode))
        {
            var readiness = await _carrierIntegrationAuthority.GetIntegrationReadinessAsync(
                tenantId,
                shippingMethod.CarrierCode,
                shippingMethodId,
                shippingMethod.FulfillmentWarehouseId,
                CarrierIntegrationPurpose.CheckoutAdmission,
                cancellationToken);
            if (!readiness.CanExecute)
            {
                throw new CheckoutShippingMethodException(shippingMethodId, "Carrier is not currently available");
            }
        }

        _logger.LogDebug(
            "Shipping method validated from SQL authority | ShippingMethodId: {ShippingMethodId} | BaseRate: {BaseRate} | CorrelationId: {CorrelationId}",
            shippingMethodId, shippingMethod.BaseRate, correlationId);

        return MapAuthoritativeShippingMethodDocument(shippingMethod);
    }

    /// <summary>
    /// Validates the selected payment method from SQL authority.
    /// 
    /// PHASE 10.4.D3: COD Payment Integration (CQRS COMPLIANT)
    /// 
    /// ARCHITECTURAL RULES:
    /// - Payment methods read from PostgreSQL authority for checkout
    /// - MongoDB projections are storefront selectors only
    /// - Validates: enabled/configured storefront availability
    /// - Min/max limits are enforced after authoritative final checkout totals are calculated
    /// - COD requires NO gateway configuration
    /// - Returns snapshot to be stored immutably in Order
    /// </summary>
    private async Task<PaymentMethodDocument> ValidatePaymentMethodAsync(
        string paymentMethodCode,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken,
        bool lockAuthorityRow = false)
    {
        var normalizedCode = NormalizePaymentMethodCode(paymentMethodCode);

        // T-09: Fawry has no certified financial settlement authority. Reject at
        // the earliest provider-specific boundary, before any order, inventory
        // reservation, payment intent, provider call, or outbox mutation.
        if (PointOfSale.Core.Payments.FawryFinancialAuthorityPolicy.IsFawry(normalizedCode))
        {
            _logger.LogWarning(
                "Payment method rejected without Fawry financial authority | Code: {Code} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                normalizedCode, tenantId, correlationId);
            throw new CheckoutFawryFinancialAuthorityException(normalizedCode);
        }

        var paymentMethod = await LoadPaymentMethodAuthorityAsync(
            normalizedCode,
            tenantId,
            lockAuthorityRow,
            cancellationToken);

        if (paymentMethod == null)
        {
            _logger.LogWarning(
                "Payment method not found in SQL authority | Code: {Code} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                normalizedCode, tenantId, correlationId);
            throw new CheckoutPaymentMethodException(normalizedCode, "Payment method not found");
        }

        if (!paymentMethod.IsEnabled)
        {
            _logger.LogWarning(
                "Payment method disabled | Code: {Code} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                normalizedCode, tenantId, correlationId);
            throw new CheckoutPaymentMethodException(normalizedCode, "Payment method is not available");
        }

        var configurationValidation = EvaluatePaymentMethodConfiguration(
            paymentMethod,
            "checkout SQL authority validation");

        if (!configurationValidation.IsConfigurationValid)
        {
            _logger.LogWarning(
                "Payment method invalid configuration rejected at checkout | Code: {Code} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                normalizedCode,
                tenantId,
                correlationId);
            throw new CheckoutPaymentMethodException(normalizedCode, "Payment method is not fully configured");
        }

        var runtimeReadiness = await _paymentRuntimeReadinessService.EvaluateAsync(
            tenantId,
            normalizedCode,
            cancellationToken);
        if (!runtimeReadiness.IsReady)
        {
            var firstFailedGate = runtimeReadiness.FirstFailedGate ?? "RUNTIME_NOT_READY";
            _logger.LogWarning(
                "Payment method runtime readiness rejected at checkout | Code: {Code} | TenantId: {TenantId} | FirstFailedGate: {FirstFailedGate} | CorrelationId: {CorrelationId}",
                normalizedCode,
                tenantId,
                firstFailedGate,
                correlationId);
            throw new CheckoutPaymentRuntimeUnavailableException(normalizedCode, firstFailedGate);
        }

        _logger.LogDebug(
            "Payment method validated from SQL authority | Code: {Code} | SupportsOffline: {SupportsOffline} | Fee: {Fee} | CorrelationId: {CorrelationId}",
            paymentMethod.Code, paymentMethod.SupportsOffline, paymentMethod.Fee, correlationId);

        return MapAuthoritativePaymentMethodDocument(paymentMethod, configurationValidation);
    }

    private async Task<ShippingMethod?> LoadShippingMethodAuthorityAsync(
        Guid shippingMethodId,
        Guid tenantId,
        bool lockAuthorityRow,
        CancellationToken cancellationToken)
    {
        if (!lockAuthorityRow)
        {
            return await _dbContext.ShippingMethods
                .AsNoTracking()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    method =>
                        method.Id == shippingMethodId &&
                        method.TenantId == tenantId &&
                        !method.IsDeleted,
                    cancellationToken);
        }

        if (IsPostgresProvider())
        {
            return await _dbContext.ShippingMethods
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""ShippingMethods""
                    WHERE ""Id"" = {shippingMethodId}
                      AND ""TenantId"" = {tenantId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (IsSqlServerProvider())
        {
            return await _dbContext.ShippingMethods
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [ShippingMethods] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] = {shippingMethodId}
                      AND [TenantId] = {tenantId}
                      AND [IsDeleted] = CAST(0 AS bit)")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        // Provider does not support row-level locking
        if (_requireRowLevelLocking)
        {
            var providerName = _dbContext.Database.ProviderName ?? "Unknown";
            throw new NotSupportedException(
                $"Database provider '{providerName}' does not support row-level locking for shipping method authority. " +
                $"Row lock was requested but cannot be acquired. Set RequireRowLevelLocking=false to allow fallback.");
        }

        _logger.LogWarning(
            "Database provider does not support shipping method row locking - falling back to non-locking read | Provider: {Provider} | ShippingMethodId: {ShippingMethodId} | TenantId: {TenantId}",
            _dbContext.Database.ProviderName,
            shippingMethodId,
            tenantId);

        return await _dbContext.ShippingMethods
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                method =>
                    method.Id == shippingMethodId &&
                    method.TenantId == tenantId &&
                    !method.IsDeleted,
                cancellationToken);
    }

    private async Task<TenantPaymentMethod?> LoadPaymentMethodAuthorityAsync(
        string normalizedCode,
        Guid tenantId,
        bool lockAuthorityRow,
        CancellationToken cancellationToken)
    {
        if (!lockAuthorityRow)
        {
            return await _dbContext.TenantPaymentMethods
                .AsNoTracking()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    method =>
                        method.TenantId == tenantId &&
                        !method.IsDeleted &&
                        method.Code.ToUpper() == normalizedCode,
                    cancellationToken);
        }

        if (IsPostgresProvider())
        {
            return await _dbContext.TenantPaymentMethods
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""TenantPaymentMethods""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""IsDeleted"" = FALSE
                      AND UPPER(""Code"") = {normalizedCode}
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (IsSqlServerProvider())
        {
            return await _dbContext.TenantPaymentMethods
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [TenantPaymentMethods] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [IsDeleted] = CAST(0 AS bit)
                      AND UPPER([Code]) = {normalizedCode}")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        // Provider does not support row-level locking
        if (_requireRowLevelLocking)
        {
            var providerName = _dbContext.Database.ProviderName ?? "Unknown";
            throw new NotSupportedException(
                $"Database provider '{providerName}' does not support row-level locking for payment method authority. " +
                $"Row lock was requested but cannot be acquired. Set RequireRowLevelLocking=false to allow fallback.");
        }

        _logger.LogWarning(
            "Database provider does not support payment method row locking - falling back to non-locking read | Provider: {Provider} | Code: {Code} | TenantId: {TenantId}",
            _dbContext.Database.ProviderName,
            normalizedCode,
            tenantId);

        return await _dbContext.TenantPaymentMethods
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                method =>
                    method.TenantId == tenantId &&
                    !method.IsDeleted &&
                    method.Code.ToUpper() == normalizedCode,
                cancellationToken);
    }

    private static string NormalizePaymentMethodCode(string paymentMethodCode) =>
        paymentMethodCode.ToUpperInvariant() switch
        {
            "COD" or "CASH" or "CASH_ON_DELIVERY" => "COD",
            "STRIPE" or "CARD" or "CREDIT_CARD" => "STRIPE",
            "PAYMOB" => "PAYMOB",
            _ => paymentMethodCode.ToUpperInvariant()
        };

    private static ShippingMethodDocument MapAuthoritativeShippingMethodDocument(ShippingMethod method) =>
        new()
        {
            TenantId = method.TenantId,
            ShippingMethodId = method.Id,
            Name = method.Name,
            NameAr = method.NameAr,
            Description = method.Description,
            BaseRate = method.BaseRate,
            PerKgRate = method.PerKgRate,
            FreeShippingThreshold = method.FreeShippingThreshold,
            EstimatedDeliveryDaysMin = method.EstimatedDeliveryDaysMin,
            EstimatedDeliveryDaysMax = method.EstimatedDeliveryDaysMax,
            IsActive = method.IsActive,
            IsDeleted = method.IsDeleted,
            SortOrder = method.SortOrder,
            CarrierName = method.CarrierName,
            CarrierCode = method.CarrierCode,
            ServiceLevel = method.ServiceLevel,
            TrackingUrlTemplate = method.TrackingUrlTemplate,
            AvailableCountries = method.AvailableCountries,
            SyncedAtUtc = method.UpdatedAt ?? method.CreatedAt
        };

    private static PaymentMethodDocument MapAuthoritativePaymentMethodDocument(
        TenantPaymentMethod method,
        PaymentMethodConfigurationValidationResult configurationValidation)
    {
        var normalizedCode = method.Code?.Trim().ToUpperInvariant() ?? string.Empty;

        return new PaymentMethodDocument
        {
            TenantId = method.TenantId,
            PaymentMethodId = method.Id,
            Code = normalizedCode,
            DisplayName = method.DisplayName,
            DisplayNameAr = method.DisplayNameAr,
            Description = method.Description,
            DescriptionAr = method.DescriptionAr,
            IsEnabled = method.IsEnabled,
            IsDeleted = method.IsDeleted,
            SortOrder = method.SortOrder,
            SupportsOnline = method.SupportsOnline,
            SupportsOffline = method.SupportsOffline,
            IsSandbox = method.IsSandbox,
            Icon = method.Icon,
            MinOrderAmount = method.MinOrderAmount,
            MaxOrderAmount = method.MaxOrderAmount,
            Fee = method.Fee,
            FeeType = method.FeeType,
            IsConfigurationValid = configurationValidation.IsConfigurationValid,
            HasConfiguration = !string.IsNullOrWhiteSpace(method.ConfigEncrypted),
            PublicConfiguration = new Dictionary<string, string>(
                configurationValidation.PublicConfiguration,
                StringComparer.OrdinalIgnoreCase),
            SyncedAtUtc = method.UpdatedAtUtc ?? method.UpdatedAt ?? method.CreatedAt,
            LastEventVersion = method.Version
        };
    }

    private void ValidatePaymentMethodOrderAmountLimits(
        PaymentMethodDocument paymentMethod,
        decimal authoritativeOrderTotal,
        string correlationId)
    {
        var normalizedCode = paymentMethod.Code.Trim().ToUpperInvariant();
        var orderTotal = RoundCurrency(authoritativeOrderTotal);

        if (paymentMethod.MinOrderAmount.HasValue && orderTotal < RoundCurrency(paymentMethod.MinOrderAmount.Value))
        {
            _logger.LogWarning(
                "Order below payment method minimum | Code: {Code} | AuthoritativeOrderTotal: {OrderTotal} | MinAmount: {MinAmount} | CorrelationId: {CorrelationId}",
                normalizedCode, orderTotal, paymentMethod.MinOrderAmount.Value, correlationId);
            throw new CheckoutPaymentMethodException(normalizedCode,
                $"Minimum order amount for this payment method is {paymentMethod.MinOrderAmount.Value:C}");
        }

        if (paymentMethod.MaxOrderAmount.HasValue && orderTotal > RoundCurrency(paymentMethod.MaxOrderAmount.Value))
        {
            _logger.LogWarning(
                "Order exceeds payment method maximum | Code: {Code} | AuthoritativeOrderTotal: {OrderTotal} | MaxAmount: {MaxAmount} | CorrelationId: {CorrelationId}",
                normalizedCode, orderTotal, paymentMethod.MaxOrderAmount.Value, correlationId);
            throw new CheckoutPaymentMethodException(normalizedCode,
                $"Maximum order amount for this payment method is {paymentMethod.MaxOrderAmount.Value:C}");
        }
    }

    /// <summary>
    /// PHASE 11.4.1 — VARIANT-BASED INVENTORY VALIDATION & RESERVATION
    /// 
    /// CRITICAL RULES:
    /// - Stock is per-Variant, NOT per-Product
    /// - Uses ProductVariantStocks (NOT WarehouseStocks)
    /// - Reservation is per-VariantId
    /// - Legacy items fallback to default variant
    /// </summary>
    private async Task<InventoryReservationResult> ValidateAndReserveInventoryAsync(
        CartSnapshot snapshot,
        Guid tenantId,
        Guid orderId,
        Guid warehouseId,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken,
        bool emitExternalReservationSideEffects = true)
    {
        var reservationId = Guid.NewGuid();
        var reservedItems = new List<InventoryReservationItem>();
        var reservationMutationAttempted = false;

        // =================================================================
        // PHASE 11.4.1: VARIANT-CENTRIC INVENTORY VALIDATION
        // =================================================================
        _logger.LogInformation(
            "Checkout validating inventory from ProductVariantStocks | TenantId: {TenantId} | CartId: {CartId} | ItemCount: {ItemCount} | CorrelationId: {CorrelationId}",
            tenantId, snapshot.CartId, snapshot.Items.Count, correlationId);

        // Get variant IDs (should already be populated from RecalculatePricesAsync)
        var variantIds = snapshot.Items
            .Where(i => i.VariantId.HasValue)
            .Select(i => i.VariantId!.Value)
            .ToList();

        var reservationExpiresAtUtc = DateTime.UtcNow.AddMinutes(InventoryReservationTtlMinutes);

        var availableQuantities = variantIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await _dbContext.ProductVariantStocks
                .IgnoreQueryFilters()
                .AsNoTracking()
                .WhereCurrentInventoryStockOwner(tenantId)
                .Where(stock => stock.WarehouseId == warehouseId && variantIds.Contains(stock.VariantId))
                .GroupBy(stock => stock.VariantId)
                .Select(group => new { VariantId = group.Key, Available = group.Sum(stock => stock.Quantity - stock.ReservedQuantity) })
                .ToDictionaryAsync(item => item.VariantId, item => item.Available, cancellationToken);

        // Stock policy is write-side authority. Mongo product documents can lag Admin
        // mutations, so checkout must not use them to decide whether reservation is required.
        var authoritativeVariants = await LoadAuthoritativeCheckoutVariantLookupAsync(
            tenantId,
            variantIds,
            cancellationToken);

        try
        {
            var reservationItemsInAuthoritativeLockOrder = snapshot.Items
                .OrderBy(item => item.VariantId ?? Guid.Empty)
                .ThenBy(item => item.ProductId)
                .ThenBy(item => item.SKU, StringComparer.Ordinal)
                .ToList();

            foreach (var item in reservationItemsInAuthoritativeLockOrder)
            {
                if (!item.VariantId.HasValue ||
                    !authoritativeVariants.TryGetValue(item.VariantId.Value, out var authoritativeVariant) ||
                    authoritativeVariant.ProductId != item.ProductId ||
                    !authoritativeVariant.ProductIsActive ||
                    !authoritativeVariant.ProductIsOnlineVisible ||
                    !authoritativeVariant.ProductIsPublished ||
                    !authoritativeVariant.VariantIsActive)
                {
                    throw new CheckoutCartInvalidException(snapshot.CartId,
                        $"Variant {item.SKU} is no longer available");
                }

                var trackStock = authoritativeVariant.ProductTrackStock;
                var allowBackorder = authoritativeVariant.ProductAllowBackorder;

                if (!trackStock || allowBackorder)
                {
                    _logger.LogDebug(
                        "Skipping stock validation | VariantId: {VariantId} | TrackStock: {TrackStock} | AllowBackorder: {AllowBackorder} | CorrelationId: {CorrelationId}",
                        item.VariantId, trackStock, allowBackorder, correlationId);
                    continue; // Skip stock validation for non-tracked or backorderable items
                }

                // PHASE 11.4.1: Get available stock from ProductVariantStocks
                var availableQty = availableQuantities.TryGetValue(item.VariantId.Value, out var qty) ? qty : 0;

                if (availableQty < item.Quantity)
                {
                    _logger.LogWarning(
                        "Insufficient variant stock | VariantId: {VariantId} | SKU: {SKU} | Requested: {Requested} | Available: {Available} | CorrelationId: {CorrelationId}",
                        item.VariantId, item.SKU, item.Quantity, availableQty, correlationId);

                    throw new WarehouseResolutionException(
                        WarehouseResolutionErrorCodes.InsufficientStockAtResolvedWarehouse,
                        $"Resolved warehouse no longer has enough stock for {item.SKU}.",
                        409);
                }

                // PHASE 11.4.1: Reserve stock in ProductVariantStocks
                reservationMutationAttempted = true;
                var reserved = await _variantStockRepository.ReserveAsync(
                    tenantId, item.VariantId.Value, warehouseId, item.Quantity, orderId, reservationExpiresAtUtc, cancellationToken);

                if (!reserved)
                {
                    _logger.LogError(
                        "Failed to reserve variant stock | VariantId: {VariantId} | SKU: {SKU} | Quantity: {Quantity} | CorrelationId: {CorrelationId}",
                        item.VariantId, item.SKU, item.Quantity, correlationId);

                    throw new WarehouseResolutionException(
                        WarehouseResolutionErrorCodes.InsufficientStockAtResolvedWarehouse,
                        $"Resolved warehouse no longer has enough stock for {item.SKU}.",
                        409);
                }

                reservedItems.Add(new InventoryReservationItem
                {
                    ProductId = item.ProductId,
                    VariantId = item.VariantId,
                    SKU = item.SKU,
                    Quantity = item.Quantity
                });

                _logger.LogDebug(
                    "Variant stock reserved | VariantId: {VariantId} | SKU: {SKU} | Quantity: {Quantity} | CorrelationId: {CorrelationId}",
                    item.VariantId, item.SKU, item.Quantity, correlationId);
            }
        }
        catch (Exception ex) when (reservationMutationAttempted)
        {
            _logger.LogWarning(
                ex,
                "Inventory reservation failed after stock mutation attempt; releasing order reservations | ReservationId: {ReservationId} | OrderId: {OrderId} | ReservedItemCount: {ReservedItemCount} | CorrelationId: {CorrelationId}",
                reservationId,
                orderId,
                reservedItems.Count,
                correlationId);

            await ReleaseInventoryReservationAsync(
                reservationId,
                orderId,
                tenantId,
                snapshot.CartId,
                "InventoryReservationFailed",
                requestId,
                correlationId,
                CancellationToken.None,
                ex);

            throw;
        }

        if (emitExternalReservationSideEffects && HasPhysicalInventoryReservation(reservedItems))
        {
            await EmitInventoryReservedEventAsync(
                tenantId, orderId, snapshot.CartId, reservationId, reservedItems,
                reservationExpiresAtUtc,
                requestId, correlationId, cancellationToken);
        }

        _logger.LogInformation(
            "Inventory validated and reserved from ProductVariantStocks | ReservationId: {ReservationId} | Items: {ItemCount} | CorrelationId: {CorrelationId}",
            reservationId, reservedItems.Count, correlationId);

        return new InventoryReservationResult(reservationId, reservedItems, reservationExpiresAtUtc);
    }

    internal static bool HasPhysicalInventoryReservation(IReadOnlyCollection<InventoryReservationItem> items) =>
        items.Count > 0;

    private async Task ReleaseInventoryReservationAsync(
        Guid reservationId,
        Guid orderId,
        Guid tenantId,
        Guid cartId,
        string reason,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken,
        Exception? originalException = null)
    {
        // Track whether the stock release succeeded
        bool stockReleaseSucceeded = false;
        
        // Wrap compensation in try-catch to preserve original exception
        try
        {
            stockReleaseSucceeded = await _variantStockRepository.ReleaseOrderReservationsAsync(
                tenantId,
                orderId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to release order reservations during compensation | ReservationId: {ReservationId} | OrderId: {OrderId} | OriginalExceptionPresent: {HasOriginalException} | CorrelationId: {CorrelationId}",
                reservationId, orderId, originalException != null, correlationId);
            
            // Only rethrow if there's no original exception to preserve
            if (originalException == null)
            {
                throw;
            }
        }

        // Only proceed with cleanup and success signals if stock release succeeded
        if (stockReleaseSucceeded)
        {
            try
            {
                await EmitInventoryReleasedEventAsync(
                    tenantId, orderId, cartId, reservationId, reason,
                    requestId, correlationId, cancellationToken);
            }
            catch (Exception publishException)
            {
                _logger.LogError(publishException,
                    "Failed to emit inventory release event after stock release | ReservationId: {ReservationId} | OriginalExceptionPresent: {HasOriginalException} | CorrelationId: {CorrelationId}",
                    reservationId, originalException != null, correlationId);
                
                // CRITICAL: Event publishing failure must not be suppressed, even during compensation.
                // If we successfully released stock but failed to publish the event, downstream systems
                // will have stale reservation state, causing cross-system divergence.
                if (originalException != null)
                {
                    // Wrap both exceptions to surface the complete failure chain
                    throw new AggregateException(
                        $"Inventory reservation compensation partially failed: stock released but event publishing failed. ReservationId: {reservationId}",
                        originalException,
                        publishException);
                }
                
                throw;
            }

            _logger.LogInformation(
                "Inventory reservation released | ReservationId: {ReservationId} | Reason: {Reason} | OriginalExceptionPresent: {HasOriginalException} | CorrelationId: {CorrelationId}",
                reservationId, reason, originalException != null, correlationId);
        }
        else
        {
            // Stock release returned false - log warning and throw if no original exception
            _logger.LogWarning(
                "Stock release returned false during compensation | ReservationId: {ReservationId} | OrderId: {OrderId} | OriginalExceptionPresent: {HasOriginalException} | CorrelationId: {CorrelationId}",
                reservationId, orderId, originalException != null, correlationId);
            
            // Only throw if there's no original exception to preserve
            if (originalException == null)
            {
                throw new InvalidOperationException(
                    $"Failed to release order reservations during compensation. ReservationId: {reservationId}, OrderId: {orderId}, CorrelationId: {correlationId}");
            }
        }
    }

    #endregion

    #region Step 7: Order Creation

    private record OrderCreationResult(Order Order, InventoryReservationResult? InventoryReservation);
    private sealed record TransactionScopedCheckoutAuthoritySnapshot(
        CartSnapshot Snapshot,
        ShippingMethodDocument ShippingMethod,
        PaymentMethodDocument PaymentMethod,
        StorefrontPricingBreakdown Pricing,
        CheckoutFieldSnapshotAdmission CheckoutFieldSnapshots);

    private sealed record CheckoutFieldSnapshotAdmission(
        IReadOnlyList<CheckoutFieldSnapshotDraft> Snapshots)
    {
        public static CheckoutFieldSnapshotAdmission Empty { get; } = new(Array.Empty<CheckoutFieldSnapshotDraft>());
    }

    private sealed record CheckoutFieldSnapshotDraft(
        Guid CheckoutFieldDefinitionId,
        CheckoutFieldKind FieldKind,
        CheckoutFieldType FieldType,
        string? SystemFieldKey,
        string Label,
        string? HelpText,
        bool IsRequired,
        bool IsEnabled,
        int DisplayOrder,
        int ConfigurationSchemaVersion,
        string ConfigurationJson,
        string ValueJson);

    private async Task<OrderCreationResult> CreateOrderAsync(
        Guid orderId,
        Guid tenantId,
        Guid? customerId,
        string ownerId,
        string ownerType,
        string? sessionId,
        CartSnapshot snapshot,
        CheckoutRequestDto request,
        ShippingMethodDocument shippingMethod,
        PaymentMethodDocument paymentMethod,
        CheckoutGeographicAdmissionSnapshot admittedGeography,
        string? manualWalletProofFileKey,
        bool promotionCalculationFailed,
        OrderRequestIdempotency idempotencyRecord,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken,
        ICollection<CheckoutStepResult>? steps = null,
        Stopwatch? stopwatch = null,
        Action<string>? activeStepTracker = null)
    {
        var pricing = await _storefrontPricingService.CalculateCheckoutAsync(
            tenantId,
            snapshot,
            shippingMethod,
            paymentMethod,
            request.ShippingAddress.Country,
            cancellationToken);

        var subtotal = pricing.Subtotal;
        var shippingCost = pricing.Shipping;
        var couponDiscountAmount = pricing.CouponDiscount;
        var promotionDiscountAmount = pricing.PromotionDiscount;
        var manualDiscountAmount = pricing.ManualDiscount;
        var totalDiscountAmount = pricing.TotalDiscount;
        var taxAmount = pricing.Tax;
        var paymentFee = pricing.PaymentFee;
        var total = pricing.FinalTotal;
        CouponReservationDecision couponReservationDecision = CouponReservationDecision.None();
        var offerReservationCreated = false;

        ValidatePaymentMethodOrderAmountLimits(paymentMethod, pricing.FinalTotal, correlationId);

        EnsureExpectedPricingMatches(
            tenantId,
            request.ExpectedPricing,
            pricing,
            correlationId);

        _logger.LogInformation(
            "Order totals recalculated from authoritative pricing service | ShippingMethodId: {ShippingMethodId} | ShippingMethodName: {ShippingMethodName} | Subtotal: {Subtotal} | Shipping: {Shipping} | Tax: {Tax} | PromotionDiscount: {PromotionDiscount} | CouponDiscount: {CouponDiscount} | PaymentFee: {PaymentFee} | Total: {Total} | CorrelationId: {CorrelationId}",
            shippingMethod.ShippingMethodId,
            shippingMethod.Name,
            subtotal,
            shippingCost,
            taxAmount,
            promotionDiscountAmount,
            couponDiscountAmount,
            paymentFee,
            total,
            correlationId);
        
        // Determine if order is paid (COD = false, online payments = depends on gateway)
        // ARCHITECTURAL RULE: COD orders are NEVER auto-marked as paid
        var isCodPayment = paymentMethod.Code == "COD" || paymentMethod.SupportsOffline;
        var validatedOrderCurrency = await EnsureCheckoutCurrencyAsync(
            tenantId,
            snapshot.Currency,
            "checkout.order.currency",
            "OrderCreation",
            "Checkout currency is not configured or does not match the tenant currency.",
            orderId,
            "checkout",
            cancellationToken);

        var effectiveBillingAddress = request.BillingAddress ?? request.ShippingAddress;

        // Create order
        var order = new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = string.Empty,
            
            // PHASE 10.4 FIX: Storefront orders use OnlineCustomerId, NOT CustomerId
            // CustomerId references POS Customers table (wrong FK for storefront)
            // OnlineCustomerId references OnlineCustomers table (correct FK for storefront)
            CustomerId = null, // POS customer - always null for storefront orders
            OnlineCustomerId = customerId, // Storefront customer - set from authenticated user
            SessionId = sessionId,
            CartId = request.CartId,
            
            // Guest info from address
            GuestEmail = request.ShippingAddress.Email,
            GuestPhone = request.ShippingAddress.Phone,
            GuestName = request.ShippingAddress.Name,
            IpAddress = GetClientIpAddress(),
            
            // AF-13B: Guest Checkout Flag
            // Derived from auth state — NOT from request DTO (prevents spoofing)
            IsGuestCheckout = customerId == null,
            
            // Status
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            
            // ═══════════════════════════════════════════════════════════════════════════
            // FULFILLMENT OWNERSHIP SNAPSHOT (IMMUTABLE)
            // 
            // FulfillmentMode is decided at checkout based on ShippingMethod.CarrierCode.
            // This is the SINGLE SOURCE OF TRUTH for fulfillment ownership.
            // NEVER infer from ShippingMethod after order creation.
            // 
            // RULES:
            // - If ShippingMethod has CarrierCode (not "manual") → Carrier mode
            // - Otherwise → Manual mode
            // ═══════════════════════════════════════════════════════════════════════════
            FulfillmentMode = DetermineFulfillmentMode(shippingMethod),
            FulfillmentCarrierCode = ExtractCarrierCode(shippingMethod),
            
            // Shipping address
            ShippingAddress = FormatAddress(request.ShippingAddress),
            ShippingCity = request.ShippingAddress.City,
            ShippingState = request.ShippingAddress.State,
            ShippingZipCode = request.ShippingAddress.ZipCode,
            ShippingCountry = request.ShippingAddress.Country,
            ShippingAddressAdmissionEvidenceId = admittedGeography.Shipping?.AddressAdmissionEvidenceId,
            ShippingGeographicCatalogReleaseId = admittedGeography.Shipping?.CatalogReleaseId,
            ShippingCountryPlaceId = admittedGeography.Shipping?.CountryPlaceId,
            ShippingAdministrativeAreaPlaceId = admittedGeography.Shipping?.AdministrativeAreaPlaceId,
            ShippingLocalityPlaceId = admittedGeography.Shipping?.LocalityPlaceId,
            ShippingDistrictPlaceId = admittedGeography.Shipping?.DistrictPlaceId,
            ShippingZonePlaceId = admittedGeography.Shipping?.ZonePlaceId,
            ShippingCountryDisplayName = request.ShippingAddress.Country,
            ShippingAdministrativeAreaDisplayName = request.ShippingAddress.State,
            ShippingLocalityDisplayName = request.ShippingAddress.City,
            ShippingStreet = request.ShippingAddress.Street,
            ShippingStreet2 = request.ShippingAddress.Street2,
            ShippingBuilding = request.ShippingAddress.Building,
            ShippingFloor = request.ShippingAddress.Floor,
            ShippingApartment = request.ShippingAddress.Apartment,
            ShippingLandmark = request.ShippingAddress.Landmark,
            ShippingGeographicSnapshotCreatedAtUtc = admittedGeography.Shipping == null ? null : DateTime.UtcNow,
            ShippingGeographicSnapshotVersion = admittedGeography.Shipping == null ? null : 1,
             
            // Billing address
            BillingAddress = FormatAddress(effectiveBillingAddress),
            BillingCity = effectiveBillingAddress.City,
            BillingState = effectiveBillingAddress.State,
            BillingZipCode = effectiveBillingAddress.ZipCode,
            BillingCountry = effectiveBillingAddress.Country,
            BillingAddressAdmissionEvidenceId = admittedGeography.Billing?.AddressAdmissionEvidenceId,
            BillingGeographicCatalogReleaseId = admittedGeography.Billing?.CatalogReleaseId,
            BillingCountryPlaceId = admittedGeography.Billing?.CountryPlaceId,
            BillingAdministrativeAreaPlaceId = admittedGeography.Billing?.AdministrativeAreaPlaceId,
            BillingLocalityPlaceId = admittedGeography.Billing?.LocalityPlaceId,
            BillingDistrictPlaceId = admittedGeography.Billing?.DistrictPlaceId,
            BillingZonePlaceId = admittedGeography.Billing?.ZonePlaceId,
            BillingCountryDisplayName = effectiveBillingAddress.Country,
            BillingAdministrativeAreaDisplayName = effectiveBillingAddress.State,
            BillingLocalityDisplayName = effectiveBillingAddress.City,
            BillingStreet = effectiveBillingAddress.Street,
            BillingStreet2 = effectiveBillingAddress.Street2,
            BillingBuilding = effectiveBillingAddress.Building,
            BillingFloor = effectiveBillingAddress.Floor,
            BillingApartment = effectiveBillingAddress.Apartment,
            BillingLandmark = effectiveBillingAddress.Landmark,
            BillingGeographicSnapshotCreatedAtUtc = admittedGeography.Billing == null ? null : DateTime.UtcNow,
            BillingGeographicSnapshotVersion = admittedGeography.Billing == null ? null : 1,
            
            // Shipping method (snapshot - immutable after order creation)
            ShippingMethodId = shippingMethod.ShippingMethodId,
            ShippingMethodName = shippingMethod.Name,
            ShippingServiceLevel = shippingMethod.ServiceLevel,
            ShippingCost = shippingCost,
            EstimatedDeliveryDays = FormatEstimatedDelivery(shippingMethod),
            
            // Financials
            Subtotal = subtotal,
            PromotionDiscountAmount = promotionDiscountAmount,
            DiscountAmount = couponDiscountAmount,
            ManualDiscountAmount = manualDiscountAmount,
            TotalDiscountAmount = totalDiscountAmount,
            TaxAmount = taxAmount,
            Total = total,
            Currency = validatedOrderCurrency,
            PricingSnapshotJson = JsonSerializer.Serialize(new
            {
                subtotal,
                shippingCost,
                taxAmount,
                couponDiscountAmount,
                promotionDiscountAmount,
                manualDiscountAmount,
                totalDiscountAmount,
                paymentFee,
                total,
                pricing.TaxRate,
                pricing.TaxSource,
                pricing.TaxCountryCode,
                GeographicAddressSnapshot = admittedGeography
            }, _jsonOptions),
            
            // Coupon (from cart snapshot - NO revalidation)
            // PHASE: Shopify-grade Coupon Engine - CouponId is CRITICAL for usage tracking
            CouponId = snapshot.AppliedCouponId,
            CouponCode = snapshot.AppliedCouponCode,
            
            // PHASE: Promotions Engine - Promotion (offer) tracking
            // If an offer won, AppliedPromotionId is set and CouponId is null
            // If a coupon won, CouponId is set and AppliedPromotionId is null
            AppliedPromotionId = snapshot.AppliedPromotionId,
            AppliedPromotionType = snapshot.AppliedPromotionType,
            AppliedPromotionName = snapshot.AppliedPromotionName,
            
            // S19C: Promotion Fallback Tracking
            // Persist promotion calculation failure state for duplicate order recovery
            PromotionCalculationFailed = promotionCalculationFailed,
#pragma warning disable CS0618
            PromotionWarningMessage = promotionCalculationFailed
                ? PromotionCalculationFallbackWarningMessage
                : null,
#pragma warning restore CS0618
            
            // Payment method (SNAPSHOT - immutable after order creation)
            // PHASE 10.4.D3: Payment method snapshot from TenantPaymentMethod
            PaymentMethod = ParsePaymentMethod(request.PaymentMethod),
            PaymentMethodCode = paymentMethod.Code,
            PaymentMethodName = paymentMethod.DisplayName,
            PaymentFee = paymentFee,
            IsPaid = false, // ARCHITECTURAL RULE: Orders start unpaid. COD stays unpaid until delivery.
            
            // Notes
            CustomerNotes = request.CustomerNotes,
            
            // Source
            Source = OrderSource.Ecommerce,
            
            CreatedAt = DateTime.UtcNow
        };

        // ═══════════════════════════════════════════════════════════════════
        // AF-11/AF-18/AF-27A: AFFILIATE ATTRIBUTION STAMP
        //
        // PRIORITY 0: AF-27A Cart Metadata (trusted, server-stamped, anti-tampering)
        // PRIORITY 1: HttpContext.Items (set by AffiliateReferralMiddleware)
        // PRIORITY 2: AF-18 FIX - Request DTO AffiliateReferralCode (for SPA checkout)
        //
        // AF-27A ANTI-TAMPERING:
        // When checkout is funnel-originated (cart.AffiliateSource == "Funnel"),
        // the AffiliateAccountId was server-stamped into the Redis cart by the
        // funnel endpoint. This takes absolute priority over URL-based ?ref=
        // or middleware context, preventing ref-hijacking attacks.
        //
        // AF-18 ROOT CAUSE: SPAs make checkout API calls in a separate HTTP request
        // from the initial page load. The middleware sets affiliate mode in HttpContext,
        // but that context is lost when the SPA makes the checkout POST request.
        //
        // SOLUTION: Frontend captures ?ref= from URL and includes it in checkout request.
        // Backend resolves the affiliate account from the referral code.
        //
        // This stamp is the single source of truth for attribution.
        // Without it, no AffiliateAttribution → no commission.
        // ═══════════════════════════════════════════════════════════════════
        var checkoutProductIds = snapshot.Items
            .Select(item => item.ProductId)
            .Where(productId => productId != Guid.Empty)
            .Distinct()
            .ToList();

        AffiliateAttributionAuthority? resolvedAffiliateAuthority = null;
        string attributionFlow = "none";

        // Affiliate ownership must originate from middleware-validated, tenant-bound,
        // expiring server-protected evidence. Cart and request fields are derivative only.
        if (_httpContextAccessor.HttpContext != null)
        {
            var affiliateAccountId = AffiliateModeResolver.GetAffiliateAccountId(_httpContextAccessor.HttpContext);
            if (affiliateAccountId != null)
            {
                // AF-15: Stamp PromotionLinkId if v2 flow is active
                var middlewarePromotionLinkId = AffiliateModeResolver.GetPromotionLinkId(_httpContextAccessor.HttpContext);
                resolvedAffiliateAuthority = await ResolveMiddlewareAffiliateAuthorityAsync(
                    tenantId,
                    affiliateAccountId.Value,
                    middlewarePromotionLinkId,
                    checkoutProductIds,
                    cancellationToken);
                attributionFlow = resolvedAffiliateAuthority.Flow;
                
                // AF-19: Log cross-store affiliate activation
                if (resolvedAffiliateAuthority.AuthorityTenantId != tenantId)
                {
                    _logger.LogInformation(
                        "[AF-19:CrossStore] Affiliate from different store (middleware) | AffiliateAccountId: {AffiliateAccountId} | " +
                        "AffiliateHomeStore: {AffiliateHomeStore} | OrderStore: {OrderStore} | ReferralCode: {ReferralCode} | CorrelationId: {CorrelationId}",
                        resolvedAffiliateAuthority.AffiliateAccountId,
                        resolvedAffiliateAuthority.AuthorityTenantId,
                        tenantId,
                        resolvedAffiliateAuthority.ReferralCode,
                        correlationId);
                }
                
                _logger.LogInformation(
                    "[AF-11/15/19] Affiliate resolved from middleware | AffiliateAccountId: {AffiliateAccountId} | " +
                    "ReferralCode: {ReferralCode} | PromotionLinkId: {PromotionLinkId} | Flow: {Flow} | " +
                    "AffiliateHomeStore: {AffiliateHomeStore} | OrderStore: {OrderStore} | CrossStore: {CrossStore} | CorrelationId: {CorrelationId}",
                    resolvedAffiliateAuthority.AffiliateAccountId,
                    resolvedAffiliateAuthority.ReferralCode,
                    resolvedAffiliateAuthority.PromotionLinkId,
                    attributionFlow,
                    resolvedAffiliateAuthority.AuthorityTenantId,
                    tenantId,
                    resolvedAffiliateAuthority.AuthorityTenantId != tenantId,
                    correlationId);
            }
            else if (AffiliateModeResolver.IsAffiliateMode(_httpContextAccessor.HttpContext))
            {
                // DEFENSE-IN-DEPTH: AffiliateMode is active but no AffiliateAccountId
                _logger.LogError(
                    "[AF-11] CRITICAL: AffiliateMode=true but AffiliateAccountId is null | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                    order.Id, correlationId);
                throw new CheckoutException(
                    "AFFILIATE_IDENTITY_MISSING",
                    "Affiliate mode is active but affiliate identity could not be resolved. Please retry with a valid referral link.",
                    "AffiliateAttribution");
            }
        }

        // Stamp order with resolved affiliate identity
        if (resolvedAffiliateAuthority != null)
        {
            order.AffiliateAccountId = resolvedAffiliateAuthority.AffiliateAccountId;
            order.AffiliateReferralCode = resolvedAffiliateAuthority.ReferralCode;
            order.PromotionLinkId = resolvedAffiliateAuthority.PromotionLinkId;
            
            _logger.LogInformation(
                "[AF-11/15/18] Order stamped with affiliate | OrderId: {OrderId} | AffiliateAccountId: {AffiliateAccountId} | " +
                "ReferralCode: {ReferralCode} | PromotionLinkId: {PromotionLinkId} | Flow: {Flow} | CorrelationId: {CorrelationId}",
                order.Id,
                resolvedAffiliateAuthority.AffiliateAccountId,
                resolvedAffiliateAuthority.ReferralCode,
                resolvedAffiliateAuthority.PromotionLinkId,
                attributionFlow,
                correlationId);
        }

        var affiliateOrderLineAdmissions = resolvedAffiliateAuthority != null
            ? await LoadAffiliateCommissionPolicySnapshotsAsync(tenantId, checkoutProductIds, resolvedAffiliateAuthority, cancellationToken)
            : new Dictionary<Guid, AffiliateOrderLineAdmissionSnapshot>();
        // Create order items
        // PHASE 11.4.1: Order items are immutable SKU snapshots
        for (var index = 0; index < snapshot.Items.Count; index++)
        {
            var item = snapshot.Items[index];
            var orderItem = new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                ProductId = item.ProductId,
                // PHASE 11.4.1: VariantId is the source of truth
                VariantId = item.VariantId,
                SKU = item.SKU,
                ProductName = item.Name,
                // PHASE 11.4.1: Variant title and attributes snapshot
                VariantName = item.VariantTitle,
                VariantAttributesJson = item.VariantAttributes != null && item.VariantAttributes.Count > 0
                    ? JsonSerializer.Serialize(item.VariantAttributes, _jsonOptions)
                    : null,
                ImageUrl = item.ThumbnailImage,
                Quantity = item.Quantity,
                // PHASE 11.4.1: Price comes from Variant, NOT Product
                UnitPrice = item.CurrentPrice,
                CostPrice = item.CostPrice,
                DiscountAmount = 0m,
                TaxAmount = 0m,
                Total = RoundCurrency(item.CurrentPrice * item.Quantity),
                AffiliateCommissionEligible = false,
                AffiliateCommissionPolicyIdSnapshot = null,
                AffiliateCommissionRateSnapshot = null,
                AffiliateMaxCommissionPerItemSnapshot = null,
                AffiliateCommissionBaseAmountSnapshot = null,
                AffiliateCommissionAmountSnapshot = null,
                AffiliateCommissionSnapshotCapturedAtUtc = null,
                CreatedAt = DateTime.UtcNow
            };
            order.Items.Add(orderItem);
        }

        for (var attempt = 1; attempt <= CheckoutAuthorityCommitMaxRetries; attempt++)
        {
        couponReservationDecision = CouponReservationDecision.None();
        offerReservationCreated = false;

        // Save to database inside the checkout transaction
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        InventoryReservationResult? inventoryReservation = null;
        try
        {
            await EnsureStorefrontAcceptingOrdersAsync(
                tenantId,
                correlationId,
                customerId,
                !string.IsNullOrWhiteSpace(request.AffiliateReferralCode),
                lockAuthorityRow: true,
                cancellationToken);

            var transactionScopedAuthority = await RevalidateCheckoutAuthorityInsideTransactionAsync(
                tenantId,
                customerId,
                ownerId,
                ownerType,
                sessionId,
                order.Id,
                snapshot,
                request,
                correlationId,
                cancellationToken,
                steps,
                stopwatch,
                requestId,
                activeStepTracker);

            snapshot = transactionScopedAuthority.Snapshot;
            shippingMethod = transactionScopedAuthority.ShippingMethod;
            if (CarrierCodes.Normalize(shippingMethod.CarrierCode) == CarrierCodes.Bosta)
            {
                var destinationFailure = BostaDestinationAdmission.ValidateCheckoutAddress(shippingMethod.ServiceLevel,
                    request.ShippingAddress.Country, request.ShippingAddress.Street);
                if (destinationFailure != null) throw new CheckoutException(destinationFailure, "Bosta destination admission changed before checkout commit.", "ShippingMethodValidation");
            }
            paymentMethod = transactionScopedAuthority.PaymentMethod;
            var finalWarehouseResolution = await _warehouseResolutionService.ValidateSubmittedResolutionAsync(
                tenantId,
                ownerId,
                ownerType,
                customerId,
                sessionId,
                snapshot,
                request,
                correlationId,
                lockForCheckout: true,
                cancellationToken);
            pricing = transactionScopedAuthority.Pricing;
            resolvedAffiliateAuthority = await RevalidateResolvedAffiliateAuthorityInsideFinalTransactionAsync(
                tenantId,
                resolvedAffiliateAuthority,
                snapshot.Items
                    .Select(item => item.ProductId)
                    .Where(productId => productId != Guid.Empty)
                    .Distinct()
                    .ToList(),
                cancellationToken);
            if (resolvedAffiliateAuthority != null)
            {
                order.AffiliateAccountId = resolvedAffiliateAuthority.AffiliateAccountId;
                order.AffiliateReferralCode = resolvedAffiliateAuthority.ReferralCode;
                order.PromotionLinkId = resolvedAffiliateAuthority.PromotionLinkId;
            }
            affiliateOrderLineAdmissions = resolvedAffiliateAuthority != null
                ? await LoadAffiliateCommissionPolicySnapshotsAsync(
                    tenantId,
                    snapshot.Items
                        .Select(item => item.ProductId)
                        .Where(productId => productId != Guid.Empty)
                        .Distinct()
                        .ToList(),
                    resolvedAffiliateAuthority,
                    cancellationToken,
                    lockAuthorityRows: true)
                : new Dictionary<Guid, AffiliateOrderLineAdmissionSnapshot>();

            RefreshOrderAuthoritySnapshot(
                order,
                snapshot,
                shippingMethod,
                paymentMethod,
                pricing,
                validatedOrderCurrency,
                admittedGeography);
            order.WarehouseId = finalWarehouseResolution.WarehouseId;
            RefreshOrderItemAuthoritySnapshots(
                order,
                snapshot.Items,
                pricing.TotalDiscount,
                pricing.Tax,
                affiliateOrderLineAdmissions,
                resolvedAffiliateAuthority != null);
            foreach (var orderItem in order.Items)
            {
                orderItem.WarehouseId = finalWarehouseResolution.WarehouseId;
            }

            inventoryReservation = await ValidateAndReserveInventoryAsync(
                snapshot,
                tenantId,
                order.Id,
                finalWarehouseResolution.WarehouseId,
                requestId,
                correlationId,
                cancellationToken,
                emitExternalReservationSideEffects: false);

            // Retain the original physical/nonphysical decision before the first
            // order insert. Payment and binding must not infer it from today's catalog.
            order.PricingSnapshotJson = OrderInventoryReservationSnapshot.Capture(order,
                inventoryReservation.ReservedItems.GroupBy(i => (i.ProductId, i.VariantId))
                    .ToDictionary(g => g.Key, g => g.Sum(i => (decimal)i.Quantity)));

            _logger.LogInformation(
                "Inventory reserved transactionally with order creation | ReservationId: {ReservationId} | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                inventoryReservation.ReservationId,
                order.Id,
                correlationId);

            order.OrderNumber = await GenerateOrderNumberAsync(tenantId, cancellationToken);

            // Add order
            _dbContext.Orders.Add(order);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _warehouseResolutionService.MarkOrderedAsync(
                tenantId,
                finalWarehouseResolution.Resolution.Id,
                order.Id,
                cancellationToken);

            var checkoutFieldSnapshots = await ValidateCheckoutFieldDefinitionsAsync(
                tenantId,
                ownerId,
                ownerType,
                sessionId,
                customerId,
                request,
                order.Id,
                cancellationToken);
            RefreshOrderCheckoutFieldSnapshots(order, checkoutFieldSnapshots);

            await _variantStockRepository.BindOrderReservationsAsync(
                tenantId,
                order.Id,
                order.Items.ToList(),
                cancellationToken);

            await TryCreateTransactionalPaymentIntentAuthorityAsync(
                tenantId,
                order,
                paymentMethod,
                requestId,
                request.WalletType,
                request.WalletPhoneNumber,
                manualWalletProofFileKey,
                correlationId,
                cancellationToken);

            // ═══════════════════════════════════════════════════════════════════
            // AF-11: CREATE ATTRIBUTION INLINE (same transaction as order)
            //
            // CRITICAL: The AffiliateAttribution MUST exist before any payment
            // event fires (COD delivered, Stripe captured, etc.), because
            // RecordCommissionForOrderAsync looks up attribution to compute
            // commission. If attribution doesn't exist, commission is skipped.
            //
            // Creating it here in the same transaction guarantees atomicity:
            // Order + Attribution are committed together.
            // ═══════════════════════════════════════════════════════════════════
            if (order.AffiliateAccountId.HasValue && !string.IsNullOrWhiteSpace(order.AffiliateReferralCode))
            {
                // AF-15: Use v2 factory if PromotionLinkId is present, v1 otherwise
                AffiliateAttribution attribution;
                if (order.PromotionLinkId.HasValue)
                {
                    attribution = AffiliateAttribution.CreateV2(
                        tenantId,
                        order.Id,
                        order.AffiliateAccountId.Value,
                        order.AffiliateReferralCode.ToUpperInvariant(),
                        AttributionSource.ReferralLink,
                        order.PromotionLinkId.Value);
                }
                else
                {
                    attribution = AffiliateAttribution.Create(
                        tenantId,
                        order.Id,
                        order.AffiliateAccountId.Value,
                        order.AffiliateReferralCode.ToUpperInvariant(),
                        AttributionSource.ReferralLink);
                }

                _dbContext.AffiliateAttributions.Add(attribution);
                await _dbContext.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "[AF-11/15] AffiliateAttribution created inline | OrderId: {OrderId} | AttributionId: {AttributionId} | " +
                    "AffiliateAccountId: {AffiliateAccountId} | ReferralCode: {ReferralCode} | PromotionLinkId: {PromotionLinkId} | " +
                    "Flow: {Flow} | CorrelationId: {CorrelationId}",
                    order.Id, attribution.Id, order.AffiliateAccountId.Value, order.AffiliateReferralCode,
                    attribution.PromotionLinkId, attribution.PromotionLinkId.HasValue ? "v2" : "v1", correlationId);
            }

            if (order.CouponId.HasValue && !string.IsNullOrWhiteSpace(order.CouponCode))
            {
                couponReservationDecision = await _couponValidationService.ReserveCouponAsync(
                    tenantId,
                    order.CouponId.Value,
                    order.CouponCode,
                    order.Id,
                    customerId,
                    new CouponValidationSnapshot
                    {
                        OrderSubtotal = order.Subtotal,
                        AppliedDiscountAmount = order.DiscountAmount,
                        CartItems = snapshot.Items
                            .Select(item => new CouponScopeValidationItem(
                                item.ProductId,
                                item.CategoryId ?? Guid.Empty))
                            .ToList()
                    },
                    correlationId,
                    cancellationToken);

                _logger.LogInformation(
                    "[Coupon] Coupon reserved transactionally | OrderId: {OrderId} | CouponId: {CouponId} | DiscountAmount: {DiscountAmount}",
                    order.Id, order.CouponId, order.DiscountAmount);
            }

            if (order.AppliedPromotionId.HasValue && order.AppliedPromotionType == "offer")
            {
                offerReservationCreated = await ReserveAppliedOfferPromotionAsync(
                    tenantId,
                    order,
                    customerId,
                    correlationId,
                    cancellationToken);
            }

            await EmitOrderCreatedEventAsync(
                tenantId,
                order,
                snapshot,
                shippingMethod.Name,
                resolvedAffiliateAuthority,
                requestId,
                correlationId,
                cancellationToken);

            if (HasPhysicalInventoryReservation(inventoryReservation.ReservedItems))
            {
                await PersistInventoryReservedOutboxAsync(
                    tenantId,
                    order.Id,
                    snapshot.CartId,
                    inventoryReservation.ReservationId,
                    inventoryReservation.ReservedItems,
                    inventoryReservation.ExpiresAtUtc,
                    requestId,
                    correlationId,
                    eventVersion: 1,
                    cancellationToken);
            }

            // Durable exactly-once boundary: the order and its idempotency binding
            // are flushed together before the checkout transaction commits, even
            // when a serializable retry previously cleared EF tracking.
            idempotencyRecord = await _checkoutRequestIdempotencyCoordinator.BindOrderCreatedForCommitAsync(
                idempotencyRecord,
                order.Id,
                cancellationToken);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsDuplicateOutboxMessageUniqueViolation(ex))
            {
                // Race condition: another thread created the outbox entry
                _logger.LogInformation(
                    "InventoryReservedEvent outbox row already exists (concurrent write) | TenantId: {TenantId} | OrderId: {OrderId} | ReservationId: {ReservationId}",
                    tenantId,
                    order.Id,
                    inventoryReservation.ReservationId);

                // Detach the conflicting outbox entity to allow re-saving other changes
                foreach (var entry in ex.Entries)
                {
                    if (entry.Entity is OutboxMessage)
                    {
                        entry.State = EntityState.Detached;
                    }
                }

                // Re-run SaveChangesAsync to persist the idempotency record and other non-outbox changes
                // This ensures the order+idempotency binding is saved before the transaction completes
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception originalException)
        {
            // Clear in-memory binding BEFORE rollback so DeleteClaimAsync runs even if rollback throws
            idempotencyRecord.OrderId = null;
            idempotencyRecord.CompletedAtUtc = null;
            inventoryReservation = null; // Clear reservation on rollback

            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                // Preserve the original exception as the primary failure
                // Attach rollback failure as secondary context
                throw new AggregateException(
                    "Order creation failed and transaction rollback also failed",
                    originalException,
                    rollbackException);
            }

            if (IsActiveCartOrderUniqueViolation(originalException))
            {
                _dbContext.ChangeTracker.Clear();

                var existingOrder = await FindActiveOrderForCartAsync(
                    tenantId,
                    request.CartId,
                    CancellationToken.None);

                if (existingOrder != null)
                {
                    _reliabilityTelemetry?.RecordCheckoutDuplicateOrderPrevented("db_unique_guard");
                    await _checkoutRequestIdempotencyCoordinator.MarkOrderCreatedAsync(
                        idempotencyRecord,
                        existingOrder.Id,
                        CancellationToken.None);

                    _logger.LogWarning(
                        "Recovered existing active order after cart uniqueness violation | TenantId: {TenantId} | CartId: {CartId} | ExistingOrderId: {OrderId} | CorrelationId: {CorrelationId}",
                        tenantId,
                        request.CartId,
                        existingOrder.Id,
                        correlationId);

                    // Return existing order with no inventory reservation (already committed previously)
                    return new OrderCreationResult(existingOrder, null);
                }
            }

            if (IsCheckoutTransactionSerializationFailure(originalException))
            {
                _dbContext.ChangeTracker.Clear();

                if (attempt < CheckoutAuthorityCommitMaxRetries)
                {
                    ResetOrderNavigationStateForRetry(order);

                    _logger.LogWarning(
                        originalException,
                        "Retrying storefront checkout authority commit after serializable race | TenantId: {TenantId} | CartId: {CartId} | OrderId: {OrderId} | Attempt: {Attempt}/{MaxAttempts} | CorrelationId: {CorrelationId}",
                        tenantId,
                        request.CartId,
                        order.Id,
                        attempt,
                        CheckoutAuthorityCommitMaxRetries,
                        correlationId);

                    await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
                    continue;
                }

                throw new CheckoutConcurrencyConflictException(
                    request.CartId,
                    correlationId,
                    attempt,
                    originalException);
            }

            throw;
        }

        // ═══════════════════════════════════════════════════════════════════
        // POST-COMMIT OPERATIONS
        // These run after transaction.CommitAsync() and must not affect
        // idempotency state or trigger rollback logic if they fail.
        // ═══════════════════════════════════════════════════════════════════
        
        // External side effects stay post-commit. InventoryReservedEvent is already durably queued in the outbox.
        if (inventoryReservation != null && HasPhysicalInventoryReservation(inventoryReservation.ReservedItems))
        {
            try
            {
                await DispatchPersistedInventoryReservedOutboxAsync(
                    tenantId,
                    order.Id,
                    inventoryReservation.ReservationId,
                    requestId,
                    correlationId,
                    cancellationToken);
            }
            catch (CheckoutPostCommitConsistencyException)
            {
                // Rethrow consistency exceptions - these indicate critical issues like missing outbox rows
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to dispatch persisted InventoryReservedEvent post-commit (non-fatal) | OrderId: {OrderId} | ReservationId: {ReservationId} | CorrelationId: {CorrelationId}",
                    order.Id, inventoryReservation.ReservationId, correlationId);
                // Don't rethrow - order is already committed, and this is a best-effort operation
            }

        }

        _logger.LogInformation(
            "Order created; shipment orchestration deferred until authoritative confirmation/payment | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
            order.Id,
            correlationId);

        if (couponReservationDecision.HasCoupon && couponReservationDecision.WasCreated)
        {
            try
            {
                _reliabilityTelemetry?.RecordCouponReservationCreated(tenantId, "checkout");
            }
            catch (Exception telemetryEx)
            {
                _logger.LogWarning(telemetryEx,
                    "Failed to record coupon reservation telemetry after order commit | OrderId: {OrderId} | CouponId: {CouponId} | CorrelationId: {CorrelationId}",
                    order.Id, order.CouponId.Value, correlationId);
            }
        }

        if (offerReservationCreated)
        {
            _logger.LogInformation(
                "[Promotions] Offer reservation committed | OrderId: {OrderId} | PromotionId: {PromotionId} | CorrelationId: {CorrelationId}",
                order.Id,
                order.AppliedPromotionId,
                correlationId);
        }

            return new OrderCreationResult(order, inventoryReservation);
        }

        throw new CheckoutConcurrencyConflictException(
            request.CartId,
            correlationId,
            CheckoutAuthorityCommitMaxRetries);
    }

    private static void ResetOrderNavigationStateForRetry(Order order)
    {
        // Clear can detach EF's identity map while leaving relationship-fixup references
        // on this retained aggregate. A retry must reload those principals as new authority.
        order.Customer = null;
        order.OnlineCustomer = null;
        order.ShippingMethod = null;
        order.Coupon = null;
        order.AppliedPromotion = null;
        order.Warehouse = null;

        foreach (var orderItem in order.Items)
        {
            orderItem.Order = order;
            orderItem.Product = null!;
            orderItem.Variant = null;
            orderItem.Unit = null;
            orderItem.Warehouse = null;
        }
    }

    private static IReadOnlyList<decimal> AllocateAmountsByLineSubtotal(
        IReadOnlyList<CartItemSnapshot> items,
        decimal totalAmount,
        bool capToLineSubtotal)
    {
        if (items.Count == 0 || totalAmount <= 0m)
        {
            return Enumerable.Repeat(0m, items.Count).ToArray();
        }

        var lineSubtotals = items
            .Select(item => RoundCurrency(item.CurrentPrice * item.Quantity))
            .ToArray();
        var totalLineSubtotal = RoundCurrency(lineSubtotals.Sum());
        if (totalLineSubtotal <= 0m)
        {
            return Enumerable.Repeat(0m, items.Count).ToArray();
        }

        var allocations = new decimal[items.Count];
        var allocatedAmount = 0m;
        var remainders = new List<(int Index, decimal FractionalRemainder, decimal Capacity)>(items.Count);

        for (var index = 0; index < items.Count; index++)
        {
            var lineSubtotal = lineSubtotals[index];
            if (lineSubtotal <= 0m)
            {
                remainders.Add((index, 0m, 0m));
                continue;
            }

            var exactShare = totalAmount * (lineSubtotal / totalLineSubtotal);
            var roundedDownShare = Math.Floor(exactShare * 100m) / 100m;
            var capacity = capToLineSubtotal ? lineSubtotal : decimal.MaxValue;
            var allocatedShare = capToLineSubtotal
                ? Math.Min(capacity, roundedDownShare)
                : roundedDownShare;

            allocations[index] = RoundCurrency(allocatedShare);
            allocatedAmount = RoundCurrency(allocatedAmount + allocations[index]);
            remainders.Add((index, exactShare - roundedDownShare, capacity - allocations[index]));
        }

        var remainingAmount = RoundCurrency(totalAmount - allocatedAmount);
        foreach (var remainder in remainders
                     .Where(entry => entry.Capacity > 0m)
                     .OrderByDescending(entry => entry.FractionalRemainder)
                     .ThenBy(entry => entry.Index))
        {
            if (remainingAmount <= 0m)
            {
                break;
            }

            var nextAllocation = RoundCurrency(allocations[remainder.Index] + 0.01m);
            if (capToLineSubtotal && nextAllocation > lineSubtotals[remainder.Index])
            {
                continue;
            }

            allocations[remainder.Index] = nextAllocation;
            remainingAmount = RoundCurrency(remainingAmount - 0.01m);
        }

        if (remainingAmount > 0m)
        {
            var fallbackIndex = -1;
            for (var index = allocations.Length - 1; index >= 0; index--)
            {
                if (!capToLineSubtotal || allocations[index] < lineSubtotals[index])
                {
                    fallbackIndex = index;
                    break;
                }
            }

            if (fallbackIndex >= 0)
            {
                allocations[fallbackIndex] = RoundCurrency(allocations[fallbackIndex] + remainingAmount);
            }
        }

        return allocations;
    }

    private async Task<IReadOnlyDictionary<Guid, AffiliateOrderLineAdmissionSnapshot>> LoadAffiliateCommissionPolicySnapshotsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> productIds,
        AffiliateAttributionAuthority resolvedAffiliateAuthority,
        CancellationToken cancellationToken,
        bool lockAuthorityRows = false)
    {
        var distinctProductIds = productIds
            .Where(productId => productId != Guid.Empty)
            .Distinct()
            .OrderBy(productId => productId)
            .ToList();

        if (distinctProductIds.Count == 0)
        {
            return new Dictionary<Guid, AffiliateOrderLineAdmissionSnapshot>();
        }

        if (lockAuthorityRows)
        {
            await LockAffiliateCommissionPolicyAuthorityAsync(tenantId, distinctProductIds, cancellationToken);
        }

        if (resolvedAffiliateAuthority.PromotionLinkId.HasValue)
        {
            if (!resolvedAffiliateAuthority.PromotionLinkProductId.HasValue ||
                !distinctProductIds.Contains(resolvedAffiliateAuthority.PromotionLinkProductId.Value))
            {
                return new Dictionary<Guid, AffiliateOrderLineAdmissionSnapshot>();
            }

            distinctProductIds = distinctProductIds
                .Where(productId => productId == resolvedAffiliateAuthority.PromotionLinkProductId.Value)
                .ToList();
        }

        // Group by ProductId and fail closed if historical/data-corruption rows violate
        // the one-active-policy authority boundary.
        var policies = await (
                from policy in _dbContext.AffiliateProductPolicies.IgnoreQueryFilters()
                join product in _dbContext.Products.IgnoreQueryFilters()
                    on new { policy.TenantId, policy.ProductId }
                    equals new { product.TenantId, ProductId = product.Id }
                join category in _dbContext.AffiliateCategories.IgnoreQueryFilters()
                    on policy.AffiliateCategoryId equals category.Id
                where policy.TenantId == tenantId &&
                      distinctProductIds.Contains(policy.ProductId) &&
                      policy.IsEnabled &&
                      !policy.IsDeleted &&
                      product.TenantId == tenantId &&
                      !product.IsDeleted &&
                      product.IsActive &&
                      product.PublishStatus == PublishStatus.Published &&
                       product.IsOnlineVisible &&
                       category.IsActive &&
                       !category.IsDeleted &&
                       ((category.Level == 1 && category.ParentId == null) ||
                        (category.Level == 2 &&
                         category.ParentId != null &&
                         _dbContext.AffiliateCategories
                             .IgnoreQueryFilters()
                             .Any(parent =>
                                 parent.Id == category.ParentId.Value &&
                                 parent.Level == 1 &&
                                 parent.IsActive &&
                                 !parent.IsDeleted)))
                select new
                {
                    policy.ProductId,
                    policy.Id,
                    policy.CommissionRate,
                    policy.MaxCommissionPerItem,
                    policy.CreatedAt
                })
            .ToListAsync(cancellationToken);

        var selectedPolicies = new List<(Guid ProductId, Guid PolicyId, decimal CommissionRate, decimal? MaxCommissionPerItem)>();

        foreach (var group in policies.GroupBy(p => p.ProductId))
        {
            var groupPolicies = group.ToList();
            if (groupPolicies.Count > 1)
            {
                _logger.LogError(
                    "Multiple active affiliate product policies detected; checkout is blocked to prevent commission overpayment | TenantId: {TenantId} | ProductId: {ProductId} | ActivePolicyCount: {ActivePolicyCount}",
                    tenantId,
                    group.Key,
                    groupPolicies.Count);

                throw new CheckoutException(
                    "AFFILIATE_POLICY_AUTHORITY_CONFLICT",
                    "Checkout cannot continue because affiliate commission policy authority is ambiguous for one or more products.",
                    "AffiliateCommissionPolicy");
            }

            var selectedPolicy = groupPolicies[0];
            selectedPolicies.Add((
                selectedPolicy.ProductId,
                selectedPolicy.Id,
                selectedPolicy.CommissionRate,
                selectedPolicy.MaxCommissionPerItem));
        }

        return selectedPolicies.ToDictionary(
            policy => policy.ProductId,
            policy => new AffiliateOrderLineAdmissionSnapshot(
                policy.ProductId,
                policy.PolicyId,
                policy.CommissionRate,
                policy.MaxCommissionPerItem));
    }

    private async Task LockAffiliateCommissionPolicyAuthorityAsync(
        Guid tenantId,
        IReadOnlyList<Guid> productIds,
        CancellationToken cancellationToken)
    {
        if (productIds.Count == 0 || !_dbContext.Database.IsRelational())
        {
            return;
        }

        if (!IsPostgresProvider() && !IsSqlServerProvider())
        {
            var providerName = _dbContext.Database.ProviderName ?? "Unknown";

            if (_requireRowLevelLocking)
            {
                throw new InvalidOperationException(
                    $"Database provider '{providerName}' is not supported for production affiliate commission authority refresh. " +
                    "Row-level locking is required to keep affiliate commission snapshots consistent under concurrent checkout operations.");
            }

            _logger.LogWarning(
                "[Affiliate] Relational provider does not support affiliate policy row locking | Provider: {Provider} | TenantId: {TenantId}",
                providerName,
                tenantId);
            return;
        }

        foreach (var productId in productIds)
        {
            if (IsPostgresProvider())
            {
                await _dbContext.AffiliateProductPolicies
                    .FromSqlInterpolated($@"
                        SELECT *
                        FROM ""AffiliateProductPolicies""
                        WHERE ""TenantId"" = {tenantId}
                          AND ""ProductId"" = {productId}
                          AND ""IsDeleted"" = false
                        FOR UPDATE")
                    .IgnoreQueryFilters()
                    .ToListAsync(cancellationToken);
                continue;
            }

            await _dbContext.AffiliateProductPolicies
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [AffiliateProductPolicies] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [ProductId] = {productId}
                      AND [IsDeleted] = CAST(0 AS bit)")
                .IgnoreQueryFilters()
                .ToListAsync(cancellationToken);
        }
    }

    private async Task<HashSet<Guid>> LoadAffiliateEligibleProductIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        var distinctProductIds = productIds
            .Where(productId => productId != Guid.Empty)
            .Distinct()
            .ToList();

        if (distinctProductIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var eligibleProductIds = await (
                from policy in _dbContext.AffiliateProductPolicies.IgnoreQueryFilters()
                join category in _dbContext.AffiliateCategories.IgnoreQueryFilters()
                    on policy.AffiliateCategoryId equals category.Id
                where policy.TenantId == tenantId &&
                      policy.IsEnabled &&
                      !policy.IsDeleted &&
                      distinctProductIds.Contains(policy.ProductId) &&
                      category.IsActive &&
                      !category.IsDeleted &&
                      ((category.Level == 1 && category.ParentId == null) ||
                       (category.Level == 2 &&
                        category.ParentId != null &&
                        _dbContext.AffiliateCategories
                            .IgnoreQueryFilters()
                            .Any(parent =>
                                parent.Id == category.ParentId.Value &&
                                parent.Level == 1 &&
                                parent.IsActive &&
                                !parent.IsDeleted)))
                select policy.ProductId)
            .ToListAsync(cancellationToken);

        return eligibleProductIds.ToHashSet();
    }

    private static decimal RoundCurrency(decimal value)
    {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private async Task<bool> ReserveAppliedOfferPromotionAsync(
        Guid tenantId,
        Order order,
        Guid? customerId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.IsRelational() && _dbContext.Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException(
                "Offer promotion reservation must execute inside the checkout transaction.");
        }

        var promotionId = order.AppliedPromotionId!.Value;
        await LockPromotionForCapacityMutationAsync(tenantId, promotionId, cancellationToken);

        var promotion = await _dbContext.Promotions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                entity => entity.TenantId == tenantId &&
                          entity.Id == promotionId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Promotion {promotionId} could not be found while reserving checkout discount capacity.");

        promotion.Validate();

        var now = DateTime.UtcNow;
        if (!promotion.IsActive || promotion.StartDate > now || promotion.EndDate < now)
        {
            throw new InvalidOperationException(
                $"Promotion {promotionId} is no longer active for order {order.Id}.");
        }

        if (promotion.MinOrderAmount.HasValue && order.Subtotal < promotion.MinOrderAmount.Value)
        {
            throw new InvalidOperationException(
                $"Promotion {promotionId} no longer meets the minimum order amount for order {order.Id}.");
        }

        if (promotion.MaxUsage.HasValue)
        {
            var confirmedUsage = await _dbContext.PromotionUsages
                .CountAsync(
                    usage => usage.TenantId == tenantId &&
                             usage.PromotionId == promotionId &&
                             !usage.IsDeleted,
                    cancellationToken);

            var activeReservations = await _dbContext.Orders
                .CountAsync(
                    entity => entity.TenantId == tenantId &&
                              entity.AppliedPromotionId == promotionId &&
                              entity.AppliedPromotionType == "offer" &&
                              !entity.IsDeleted &&
                              entity.CheckoutState != CheckoutState.Cancelled &&
                              entity.CheckoutState != CheckoutState.Failed &&
                              entity.CheckoutState != CheckoutState.Expired &&
                              entity.PaymentStatus != PaymentStatus.Collected &&
                              entity.PaymentStatus != PaymentStatus.Settled &&
                              entity.PaymentStatus != PaymentStatus.Refunded &&
                              entity.PaymentStatus != PaymentStatus.PartiallyRefunded &&
                              entity.PaymentStatus != PaymentStatus.Failed &&
                              entity.PaymentStatus != PaymentStatus.Rejected &&
                              entity.PaymentStatus != PaymentStatus.Expired,
                    cancellationToken);

            if (confirmedUsage + activeReservations > promotion.MaxUsage.Value)
            {
                throw new InvalidOperationException(
                    $"Promotion reservation failed for order {order.Id}: usage limit changed before checkout commit.");
            }
        }

        _logger.LogInformation(
            "[Promotions] Offer reserved by pending order | OrderId: {OrderId} | PromotionId: {PromotionId} | DiscountAmount: {DiscountAmount} | CorrelationId: {CorrelationId}",
            order.Id,
            promotionId,
            order.PromotionDiscountAmount,
            correlationId);

        return true;
    }

    private async Task LockPromotionForCapacityMutationAsync(
        Guid tenantId,
        Guid promotionId,
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            return;
        }

        Promotion? promotion;

        if (IsPostgresProvider())
        {
            promotion = await _dbContext.Promotions
                .FromSqlInterpolated($@"
                    SELECT xmin, *
                    FROM ""Promotions""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""Id"" = {promotionId}
                      AND ""IsDeleted"" = false
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }
        else if (IsSqlServerProvider())
        {
            promotion = await _dbContext.Promotions
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [Promotions] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [Id] = {promotionId}
                      AND [IsDeleted] = CAST(0 AS bit)")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }
        else
        {
            var providerName = _dbContext.Database.ProviderName ?? "Unknown";
            
            if (_requireRowLevelLocking)
            {
                throw new InvalidOperationException(
                    $"Database provider '{providerName}' is not supported for production promotion capacity flows. " +
                    "Row-level locking is required to keep promotion capacity counts consistent under concurrent checkout operations.");
            }
            
            _logger.LogWarning(
                "[Promotions] Relational provider does not support promotion capacity row locking | Provider: {Provider} | TenantId: {TenantId} | PromotionId: {PromotionId}",
                providerName,
                tenantId,
                promotionId);

            promotion = await _dbContext.Promotions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    entity => entity.TenantId == tenantId &&
                              entity.Id == promotionId &&
                              !entity.IsDeleted,
                    cancellationToken);
        }

        if (promotion == null)
        {
            throw new InvalidOperationException(
                $"Promotion {promotionId} could not be locked while reserving checkout discount capacity.");
        }
    }

    private async Task<TransactionScopedCheckoutAuthoritySnapshot> RevalidateCheckoutAuthorityInsideTransactionAsync(
        Guid tenantId,
        Guid? customerId,
        string ownerId,
        string ownerType,
        string? sessionId,
        Guid orderId,
        CartSnapshot snapshot,
        CheckoutRequestDto request,
        string correlationId,
        CancellationToken cancellationToken,
        ICollection<CheckoutStepResult>? steps = null,
        Stopwatch? stopwatch = null,
        string? requestId = null,
        Action<string>? activeStepTracker = null)
    {
        var shippingMethod = await ValidateShippingMethodAsync(
            request.ShippingMethodId,
            tenantId,
            correlationId,
            cancellationToken,
            lockAuthorityRow: true);

        var paymentMethod = await ValidatePaymentMethodAsync(
            request.PaymentMethod,
            tenantId,
            correlationId,
            cancellationToken,
            lockAuthorityRow: true);

        var (authoritativeSnapshot, _, _, _, _, _) = await RecalculatePricesInternalAsync(
            snapshot,
            tenantId,
            correlationId,
            cancellationToken,
            lockAuthorityRows: true);

        await ReconcileCheckoutDiscountAuthorityAsync(
            tenantId,
            customerId,
            authoritativeSnapshot,
            request.CartId,
            correlationId,
            cancellationToken,
            mode: "checkout",
            steps,
            stopwatch,
            requestId,
            activeStepTracker);

        var pricing = await _storefrontPricingService.CalculateCheckoutAsync(
            tenantId,
            authoritativeSnapshot,
            shippingMethod,
            paymentMethod,
            request.ShippingAddress.Country,
            cancellationToken);

        ValidatePaymentMethodOrderAmountLimits(paymentMethod, pricing.FinalTotal, correlationId);

        EnsureExpectedPricingMatches(
            tenantId,
            request.ExpectedPricing,
            pricing,
            correlationId);

        _logger.LogInformation(
            "Checkout authority revalidated inside final order transaction | ShippingMethodId: {ShippingMethodId} | PaymentMethodCode: {PaymentMethodCode} | Total: {Total} | CorrelationId: {CorrelationId}",
            shippingMethod.ShippingMethodId,
                paymentMethod.Code,
                pricing.FinalTotal,
                correlationId);

        var checkoutFieldSnapshots = await ValidateCheckoutFieldDefinitionsAsync(
            tenantId,
            ownerId,
            ownerType,
            sessionId,
            customerId,
            request,
            orderId: null,
            cancellationToken);

        return new TransactionScopedCheckoutAuthoritySnapshot(
            authoritativeSnapshot,
            shippingMethod,
            paymentMethod,
            pricing,
            checkoutFieldSnapshots);
    }

    private void RefreshOrderAuthoritySnapshot(
        Order order,
        CartSnapshot snapshot,
        ShippingMethodDocument shippingMethod,
        PaymentMethodDocument paymentMethod,
        StorefrontPricingBreakdown pricing,
        string validatedOrderCurrency,
        CheckoutGeographicAdmissionSnapshot admittedGeography)
    {
        var subtotal = pricing.Subtotal;
        var shippingCost = pricing.Shipping;
        var couponDiscountAmount = pricing.CouponDiscount;
        var promotionDiscountAmount = pricing.PromotionDiscount;
        var manualDiscountAmount = pricing.ManualDiscount;
        var totalDiscountAmount = pricing.TotalDiscount;
        var taxAmount = pricing.Tax;
        var paymentFee = pricing.PaymentFee;
        var total = pricing.FinalTotal;

        order.FulfillmentMode = DetermineFulfillmentMode(shippingMethod);
        order.FulfillmentCarrierCode = ExtractCarrierCode(shippingMethod);
        order.ShippingMethodId = shippingMethod.ShippingMethodId;
        order.ShippingMethodName = shippingMethod.Name;
        order.ShippingServiceLevel = shippingMethod.ServiceLevel;
        order.ShippingCost = shippingCost;
        order.EstimatedDeliveryDays = FormatEstimatedDelivery(shippingMethod);
        order.Subtotal = subtotal;
        order.PromotionDiscountAmount = promotionDiscountAmount;
        order.DiscountAmount = couponDiscountAmount;
        order.ManualDiscountAmount = manualDiscountAmount;
        order.TotalDiscountAmount = totalDiscountAmount;
        order.TaxAmount = taxAmount;
        order.Total = total;
        order.Currency = validatedOrderCurrency;
        order.PricingSnapshotJson = JsonSerializer.Serialize(new
        {
            subtotal,
            shippingCost,
            taxAmount,
            couponDiscountAmount,
            promotionDiscountAmount,
            manualDiscountAmount,
            totalDiscountAmount,
            paymentFee,
            total,
            pricing.TaxRate,
            pricing.TaxSource,
            pricing.TaxCountryCode,
            GeographicAddressSnapshot = admittedGeography
        }, _jsonOptions);
        order.PaymentMethod = ParsePaymentMethod(paymentMethod.Code);
        order.PaymentMethodCode = paymentMethod.Code;
        order.PaymentMethodName = paymentMethod.DisplayName;
        order.PaymentFee = paymentFee;
        order.CouponId = snapshot.AppliedCouponId;
        order.CouponCode = snapshot.AppliedCouponCode;
        order.AppliedPromotionId = snapshot.AppliedPromotionId;
        order.AppliedPromotionType = snapshot.AppliedPromotionType;
        order.AppliedPromotionName = snapshot.AppliedPromotionName;
        order.PromotionCalculationFailed = false;
#pragma warning disable CS0618
        order.PromotionWarningMessage = null;
#pragma warning restore CS0618
        order.IsPaid = false;
    }

    private void RefreshOrderItemAuthoritySnapshots(
        Order order,
        IReadOnlyList<CartItemSnapshot> cartItems,
        decimal totalDiscountAmount,
        decimal taxAmount,
        IReadOnlyDictionary<Guid, AffiliateOrderLineAdmissionSnapshot> affiliateOrderLineAdmissions,
        bool hasResolvedAffiliateAuthority)
    {
        if (order.Items.Count != cartItems.Count)
        {
            throw new InvalidOperationException(
                $"Order item snapshot count mismatch. Order has {order.Items.Count} items but authoritative cart has {cartItems.Count} items.");
        }

        var orderItems = order.Items as IList<OrderItem> ?? order.Items.ToList();

        var itemDiscountAllocations = AllocateAmountsByLineSubtotal(
            cartItems,
            totalDiscountAmount,
            capToLineSubtotal: true);
        var itemTaxAllocations = AllocateAmountsByLineSubtotal(
            cartItems,
            taxAmount,
            capToLineSubtotal: false);
        var affiliateSnapshotCapturedAtUtc = hasResolvedAffiliateAuthority
            ? DateTime.UtcNow
            : (DateTime?)null;

        for (var index = 0; index < cartItems.Count; index++)
        {
            var cartItem = cartItems[index];
            var orderItem = orderItems[index];
            var lineSubtotal = RoundCurrency(cartItem.CurrentPrice * cartItem.Quantity);
            var lineDiscountAmount = itemDiscountAllocations[index];
            var lineTaxAmount = itemTaxAllocations[index];
            var lineTotal = RoundCurrency(lineSubtotal - lineDiscountAmount + lineTaxAmount);

            orderItem.SKU = cartItem.SKU;
            orderItem.UnitPrice = cartItem.CurrentPrice;
            orderItem.CostPrice = cartItem.CostPrice;
            orderItem.DiscountAmount = lineDiscountAmount;
            orderItem.TaxAmount = lineTaxAmount;
            orderItem.Total = lineTotal;

            affiliateOrderLineAdmissions.TryGetValue(cartItem.ProductId, out var affiliatePolicySnapshot);
            var hasCommissionableAffiliatePolicy =
                hasResolvedAffiliateAuthority &&
                affiliatePolicySnapshot != null &&
                lineTotal > 0m;
            var affiliateCommissionAmount = hasCommissionableAffiliatePolicy
                ? Math.Round(
                    affiliatePolicySnapshot!.MaxCommissionPerItem.HasValue
                        ? Math.Min(
                            lineTotal * affiliatePolicySnapshot.CommissionRate,
                            affiliatePolicySnapshot.MaxCommissionPerItem.Value * cartItem.Quantity)
                        : lineTotal * affiliatePolicySnapshot.CommissionRate,
                    4,
                    MidpointRounding.ToZero)
                : (decimal?)null;
            var hasPositiveAffiliateCommission = affiliateCommissionAmount.HasValue && affiliateCommissionAmount.Value > 0m;
            var shouldPersistAffiliateCommissionSnapshot = hasCommissionableAffiliatePolicy && hasPositiveAffiliateCommission;

            orderItem.AffiliateCommissionEligible = shouldPersistAffiliateCommissionSnapshot;
            orderItem.AffiliateCommissionPolicyIdSnapshot = shouldPersistAffiliateCommissionSnapshot ? affiliatePolicySnapshot!.PolicyId : null;
            orderItem.AffiliateCommissionRateSnapshot = shouldPersistAffiliateCommissionSnapshot ? affiliatePolicySnapshot!.CommissionRate : null;
            orderItem.AffiliateMaxCommissionPerItemSnapshot = shouldPersistAffiliateCommissionSnapshot ? affiliatePolicySnapshot!.MaxCommissionPerItem : null;
            orderItem.AffiliateCommissionBaseAmountSnapshot = shouldPersistAffiliateCommissionSnapshot ? lineTotal : null;
            orderItem.AffiliateCommissionAmountSnapshot = shouldPersistAffiliateCommissionSnapshot ? affiliateCommissionAmount : null;
            orderItem.AffiliateCommissionSnapshotCapturedAtUtc = shouldPersistAffiliateCommissionSnapshot ? affiliateSnapshotCapturedAtUtc : null;
        }
    }

    private void RefreshOrderCheckoutFieldSnapshots(
        Order order,
        CheckoutFieldSnapshotAdmission admission)
    {
        order.CheckoutFieldSnapshots.Clear();

        foreach (var snapshot in admission.Snapshots)
        {
            var entity = new OrderCheckoutFieldSnapshot
            {
                Id = Guid.NewGuid(),
                TenantId = order.TenantId,
                OrderId = order.Id,
                CheckoutFieldDefinitionId = snapshot.CheckoutFieldDefinitionId,
                FieldKind = snapshot.FieldKind,
                FieldType = snapshot.FieldType,
                SystemFieldKey = snapshot.SystemFieldKey,
                Label = snapshot.Label,
                HelpText = snapshot.HelpText,
                IsRequired = snapshot.IsRequired,
                IsEnabled = snapshot.IsEnabled,
                DisplayOrder = snapshot.DisplayOrder,
                ConfigurationSchemaVersion = snapshot.ConfigurationSchemaVersion,
                ConfigurationJson = snapshot.ConfigurationJson,
                ValueJson = snapshot.ValueJson,
                CreatedAt = DateTime.UtcNow
            };

            order.CheckoutFieldSnapshots.Add(entity);
            _dbContext.Entry(entity).State = EntityState.Added;
        }
    }

    private string? GetClientIpAddress()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return null;
        }

        var remoteIp = ClientIpAddressResolver.GetTrustedClientIp(httpContext, string.Empty);
        if (string.IsNullOrWhiteSpace(remoteIp))
        {
            return null;
        }

        return remoteIp[..Math.Min(remoteIp.Length, 45)];
    }

    /// <summary>
    /// Creates a shipment within the same transaction as order creation.
    /// CQRS COMPLIANT: Accepts MongoDB ShippingMethodDocument, not PostgreSQL entity.
    /// </summary>
    private async Task<AffiliateAttributionAuthority?> ResolveAffiliateAttributionAuthorityAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> checkoutProductIds,
        string referralCode,
        string correlationId,
        CancellationToken cancellationToken)
    {
        referralCode = NormalizeAffiliateReferralCode(referralCode);
        if (string.IsNullOrWhiteSpace(referralCode))
        {
            return null;
        }

        var promotionLinkMatches = await _dbContext.AffiliatePromotionLinks
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(link => link.ReferralCode == referralCode
                        && link.IsActive
                        && !link.IsDeleted)
            .Select(link => new AffiliatePromotionLinkAuthorityCandidate(
                link.Id,
                link.AffiliateUserId,
                link.AffiliateAccountId,
                link.ReferralCode,
                link.TenantId,
                link.ProductId))
            .Take(2)
            .ToListAsync(cancellationToken);

        if (promotionLinkMatches.Count > 1)
        {
            ThrowNonUniqueAffiliateReferralCode(referralCode, "AffiliatePromotionLinks");
        }

        if (promotionLinkMatches.Count == 1)
        {
            var promotionLink = promotionLinkMatches[0];
            if (promotionLink.TenantId != tenantId)
            {
                _logger.LogWarning(
                    "[AF-Authority] Promotion link referral rejected outside owning store | ReferralCode: {ReferralCode} | LinkTenant: {LinkTenant} | OrderTenant: {OrderTenant} | LinkProductId: {LinkProductId} | CorrelationId: {CorrelationId}",
                    referralCode,
                    promotionLink.TenantId,
                    tenantId,
                    promotionLink.ProductId,
                    correlationId);
                return null;
            }

            var affiliateAccountId = await ResolvePromotionLinkAffiliateAccountIdAsync(
                promotionLink.AffiliateUserId,
                promotionLink.AffiliateAccountId,
                cancellationToken);

            if (!affiliateAccountId.HasValue)
            {
                _logger.LogError(
                    "[AF-Authority] Promotion link has no resolvable affiliate account | PromotionLinkId: {PromotionLinkId} | ReferralCode: {ReferralCode} | CorrelationId: {CorrelationId}",
                    promotionLink.Id,
                    referralCode,
                    correlationId);
                return null;
            }

            var account = await LoadActiveAffiliateAccountAuthorityAsync(affiliateAccountId.Value, cancellationToken);
            if (account == null)
            {
                _logger.LogWarning(
                    "[AF-Authority] Promotion link affiliate account is inactive or missing | PromotionLinkId: {PromotionLinkId} | AffiliateAccountId: {AffiliateAccountId} | CorrelationId: {CorrelationId}",
                    promotionLink.Id,
                    affiliateAccountId.Value,
                    correlationId);
                return null;
            }

            return new AffiliateAttributionAuthority(
                account.Id,
                promotionLink.Id,
                promotionLink.ProductId,
                promotionLink.ReferralCode,
                promotionLink.TenantId,
                promotionLink.AffiliateUserId,
                "v2-request-dto");
        }

        var accountMatches = await _dbContext.AffiliateAccounts
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(account => account.ReferralCode == referralCode
                           && account.Status == AffiliateStatus.Active
                           && !account.IsDeleted)
            .Select(account => new AffiliateAccountAuthorityCandidate(
                account.Id,
                account.ReferralCode,
                account.TenantId))
            .Take(2)
            .ToListAsync(cancellationToken);

        if (accountMatches.Count > 1)
        {
            ThrowNonUniqueAffiliateReferralCode(referralCode, "AffiliateAccounts");
        }

        var affiliateAccount = accountMatches.SingleOrDefault();
        return affiliateAccount == null
            ? null
            : new AffiliateAttributionAuthority(
                affiliateAccount.Id,
                null,
                null,
                affiliateAccount.ReferralCode,
                affiliateAccount.TenantId,
                null,
                "v1-request-dto");
    }

    private async Task<AffiliateAttributionAuthority> ResolveMiddlewareAffiliateAuthorityAsync(
        Guid tenantId,
        Guid affiliateAccountId,
        Guid? promotionLinkId,
        IReadOnlyCollection<Guid> checkoutProductIds,
        CancellationToken cancellationToken)
    {
        var account = await LoadActiveAffiliateAccountAuthorityAsync(affiliateAccountId, cancellationToken);
        if (account == null)
        {
            throw new CheckoutException(
                "AFFILIATE_IDENTITY_INVALID",
                "Affiliate identity is inactive or could not be resolved. Please retry with a valid referral link.",
                "AffiliateAttribution");
        }

        if (!promotionLinkId.HasValue)
        {
            return new AffiliateAttributionAuthority(
                account.Id,
                null,
                null,
                account.ReferralCode,
                account.TenantId,
                null,
                "v1-middleware");
        }

        var promotionLink = await _dbContext.AffiliatePromotionLinks
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(link => link.Id == promotionLinkId.Value
                        && link.IsActive
                        && !link.IsDeleted)
            .Select(link => new AffiliatePromotionLinkAuthorityCandidate(
                link.Id,
                link.AffiliateUserId,
                link.AffiliateAccountId,
                link.ReferralCode,
                link.TenantId,
                link.ProductId))
            .SingleOrDefaultAsync(cancellationToken);

        if (promotionLink == null ||
            promotionLink.TenantId != tenantId)
        {
            throw new CheckoutException(
                "AFFILIATE_PROMOTION_LINK_INVALID",
                "Affiliate promotion link is not valid for this store checkout.",
                "AffiliateAttribution");
        }

        var promotionAccountId = await ResolvePromotionLinkAffiliateAccountIdAsync(
            promotionLink.AffiliateUserId,
            promotionLink.AffiliateAccountId,
            cancellationToken);

        if (promotionAccountId != account.Id)
        {
            throw new CheckoutException(
                "AFFILIATE_PROMOTION_LINK_MISMATCH",
                "Affiliate promotion link does not match the affiliate identity.",
                "AffiliateAttribution");
        }

        return new AffiliateAttributionAuthority(
            account.Id,
            promotionLink.Id,
            promotionLink.ProductId,
            promotionLink.ReferralCode,
            promotionLink.TenantId,
            promotionLink.AffiliateUserId,
            "v2-middleware");
    }

    private async Task<AffiliateAttributionAuthority?> ResolveActiveAffiliateAccountAuthorityAsync(
        Guid affiliateAccountId,
        string? referralCode,
        string flow,
        CancellationToken cancellationToken)
    {
        var account = await LoadActiveAffiliateAccountAuthorityAsync(affiliateAccountId, cancellationToken);
        return account == null
            ? null
            : new AffiliateAttributionAuthority(
                account.Id,
                null,
                null,
                account.ReferralCode,
                account.TenantId,
                null,
                flow);
    }

    private async Task<AffiliateAccountAuthorityCandidate?> LoadActiveAffiliateAccountAuthorityAsync(
        Guid affiliateAccountId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.AffiliateAccounts
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(account => account.Id == affiliateAccountId
                           && account.Status == AffiliateStatus.Active
                           && !account.IsDeleted)
            .Select(account => new AffiliateAccountAuthorityCandidate(
                account.Id,
                account.ReferralCode,
                account.TenantId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<DateTime?> ResolvePaymentIntentExpirationAsync(
        Guid tenantId,
        Guid paymentIntentId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.PaymentIntents
            .AsNoTracking()
            .Where(intent => intent.TenantId == tenantId && intent.Id == paymentIntentId && !intent.IsDeleted)
            .Select(intent => intent.ExpiresAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<AffiliateAttributionAuthority?> RevalidateResolvedAffiliateAuthorityInsideFinalTransactionAsync(
        Guid tenantId,
        AffiliateAttributionAuthority? resolvedAuthority,
        IReadOnlyCollection<Guid> checkoutProductIds,
        CancellationToken cancellationToken)
    {
        if (resolvedAuthority == null)
        {
            return null;
        }

        if (_dbContext.Database.IsRelational() && _dbContext.Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException(
                "Affiliate checkout authority must be revalidated inside the final checkout transaction.");
        }

        var account = await LoadActiveAffiliateAccountAuthorityForFinalCheckoutAsync(
            resolvedAuthority.AffiliateAccountId,
            cancellationToken);
        if (account == null)
        {
            throw new CheckoutException(
                "AFFILIATE_AUTHORITY_STALE",
                "Affiliate authority changed before checkout could be finalized. Please refresh the cart and try again.",
                "AffiliateAttribution");
        }

        if (!resolvedAuthority.PromotionLinkId.HasValue)
        {
            return new AffiliateAttributionAuthority(
                account.Id,
                null,
                null,
                account.ReferralCode,
                account.TenantId,
                null,
                $"{resolvedAuthority.Flow}-final-sql");
        }

        var promotionLink = await LoadActiveAffiliatePromotionLinkAuthorityForFinalCheckoutAsync(
            resolvedAuthority.PromotionLinkId.Value,
            cancellationToken);
        if (promotionLink == null ||
            promotionLink.TenantId != tenantId)
        {
            throw new CheckoutException(
                "AFFILIATE_PROMOTION_LINK_STALE",
                "Affiliate promotion link store authority changed before checkout could be finalized. Please refresh the cart and try again.",
                "AffiliateAttribution");
        }

        var promotionAccountId = promotionLink.AffiliateAccountId
            ?? await LoadAffiliateUserAccountIdForFinalCheckoutAsync(promotionLink.AffiliateUserId, cancellationToken);
        if (promotionAccountId != account.Id)
        {
            throw new CheckoutException(
                "AFFILIATE_PROMOTION_LINK_STALE",
                "Affiliate promotion link authority changed before checkout could be finalized. Please refresh the cart and try again.",
                "AffiliateAttribution");
        }

        return new AffiliateAttributionAuthority(
            account.Id,
            promotionLink.Id,
            promotionLink.ProductId,
            promotionLink.ReferralCode,
            promotionLink.TenantId,
            promotionLink.AffiliateUserId,
            $"{resolvedAuthority.Flow}-final-sql");
    }

    private async Task<AffiliateAccountAuthorityCandidate?> LoadActiveAffiliateAccountAuthorityForFinalCheckoutAsync(
        Guid affiliateAccountId,
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            return await LoadActiveAffiliateAccountAuthorityAsync(affiliateAccountId, cancellationToken);
        }

        if (!IsPostgresProvider() && !IsSqlServerProvider())
        {
            var providerName = _dbContext.Database.ProviderName ?? "Unknown";

            if (_requireRowLevelLocking)
            {
                throw new InvalidOperationException(
                    $"Database provider '{providerName}' is not supported for production affiliate account authority refresh. " +
                    "Row-level locking is required to keep affiliate checkout attribution consistent under concurrent checkout operations.");
            }

            _logger.LogWarning(
                "[Affiliate] Relational provider does not support affiliate account row locking | Provider: {Provider} | AffiliateAccountId: {AffiliateAccountId}",
                providerName,
                affiliateAccountId);
            return await LoadActiveAffiliateAccountAuthorityAsync(affiliateAccountId, cancellationToken);
        }

        var activeStatus = (int)AffiliateStatus.Active;
        var accounts = IsPostgresProvider()
            ? await _dbContext.AffiliateAccounts
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""AffiliateAccounts""
                    WHERE ""Id"" = {affiliateAccountId}
                      AND ""Status"" = {activeStatus}
                      AND ""IsDeleted"" = false
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .ToListAsync(cancellationToken)
            : await _dbContext.AffiliateAccounts
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [AffiliateAccounts] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] = {affiliateAccountId}
                      AND [Status] = {activeStatus}
                      AND [IsDeleted] = CAST(0 AS bit)")
                .IgnoreQueryFilters()
                .ToListAsync(cancellationToken);

        var account = accounts.SingleOrDefault();
        return account == null
            ? null
            : new AffiliateAccountAuthorityCandidate(account.Id, account.ReferralCode, account.TenantId);
    }

    private async Task<AffiliatePromotionLinkAuthorityCandidate?> LoadActiveAffiliatePromotionLinkAuthorityForFinalCheckoutAsync(
        Guid promotionLinkId,
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            return await _dbContext.AffiliatePromotionLinks
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(link => link.Id == promotionLinkId &&
                               link.IsActive &&
                               !link.IsDeleted)
                .Select(link => new AffiliatePromotionLinkAuthorityCandidate(
                    link.Id,
                    link.AffiliateUserId,
                    link.AffiliateAccountId,
                    link.ReferralCode,
                    link.TenantId,
                    link.ProductId))
                .SingleOrDefaultAsync(cancellationToken);
        }

        if (!IsPostgresProvider() && !IsSqlServerProvider())
        {
            var providerName = _dbContext.Database.ProviderName ?? "Unknown";

            if (_requireRowLevelLocking)
            {
                throw new InvalidOperationException(
                    $"Database provider '{providerName}' is not supported for production affiliate promotion-link authority refresh. " +
                    "Row-level locking is required to keep affiliate checkout attribution consistent under concurrent checkout operations.");
            }

            _logger.LogWarning(
                "[Affiliate] Relational provider does not support affiliate promotion-link row locking | Provider: {Provider} | PromotionLinkId: {PromotionLinkId}",
                providerName,
                promotionLinkId);
        }

        var links = IsPostgresProvider()
            ? await _dbContext.AffiliatePromotionLinks
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""AffiliatePromotionLinks""
                    WHERE ""Id"" = {promotionLinkId}
                      AND ""IsActive"" = true
                      AND ""IsDeleted"" = false
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .ToListAsync(cancellationToken)
            : await _dbContext.AffiliatePromotionLinks
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [AffiliatePromotionLinks] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] = {promotionLinkId}
                      AND [IsActive] = CAST(1 AS bit)
                      AND [IsDeleted] = CAST(0 AS bit)")
                .IgnoreQueryFilters()
                .ToListAsync(cancellationToken);

        var link = links.SingleOrDefault();
        return link == null
            ? null
            : new AffiliatePromotionLinkAuthorityCandidate(
                link.Id,
                link.AffiliateUserId,
                link.AffiliateAccountId,
                link.ReferralCode,
                link.TenantId,
                link.ProductId);
    }

    private async Task<Guid?> LoadAffiliateUserAccountIdForFinalCheckoutAsync(
        Guid affiliateUserId,
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            return await _dbContext.AffiliateUsers
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(user => user.Id == affiliateUserId && !user.IsDeleted)
                .Select(user => (Guid?)user.AffiliateAccountId)
                .SingleOrDefaultAsync(cancellationToken);
        }

        if (!IsPostgresProvider() && !IsSqlServerProvider())
        {
            var providerName = _dbContext.Database.ProviderName ?? "Unknown";

            if (_requireRowLevelLocking)
            {
                throw new InvalidOperationException(
                    $"Database provider '{providerName}' is not supported for production affiliate-user authority refresh. " +
                    "Row-level locking is required to keep affiliate checkout attribution consistent under concurrent checkout operations.");
            }

            _logger.LogWarning(
                "[Affiliate] Relational provider does not support affiliate-user row locking | Provider: {Provider} | AffiliateUserId: {AffiliateUserId}",
                providerName,
                affiliateUserId);
        }

        var users = IsPostgresProvider()
            ? await _dbContext.AffiliateUsers
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""AffiliateUsers""
                    WHERE ""Id"" = {affiliateUserId}
                      AND ""IsDeleted"" = false
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .ToListAsync(cancellationToken)
            : await _dbContext.AffiliateUsers
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [AffiliateUsers] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] = {affiliateUserId}
                      AND [IsDeleted] = CAST(0 AS bit)")
                .IgnoreQueryFilters()
                .ToListAsync(cancellationToken);

        return users.SingleOrDefault()?.AffiliateAccountId;
    }

    private async Task<Guid?> ResolvePromotionLinkAffiliateAccountIdAsync(
        Guid affiliateUserId,
        Guid? affiliateAccountId,
        CancellationToken cancellationToken)
    {
        if (affiliateAccountId.HasValue)
        {
            return affiliateAccountId.Value;
        }

        return await _dbContext.AffiliateUsers
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(user => user.Id == affiliateUserId && !user.IsDeleted)
            .Select(user => (Guid?)user.AffiliateAccountId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static string? NormalizeAffiliateReferralCode(string? referralCode)
    {
        var normalized = referralCode?.Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static Guid? NormalizeAffiliateAuthorityTenantId(Guid? authorityTenantId) =>
        authorityTenantId.HasValue && authorityTenantId.Value != Guid.Empty
            ? authorityTenantId.Value
            : null;

    private static void ThrowNonUniqueAffiliateReferralCode(string referralCode, string authorityTable)
    {
        throw new CheckoutException(
            "AFFILIATE_REFERRAL_CODE_NOT_UNIQUE",
            "Affiliate referral code governance is invalid. Please retry without the referral code.",
            $"AffiliateAttribution:{authorityTable}:{referralCode}");
    }

    private async Task<string> GenerateOrderNumberAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var timestampUtc = DateTime.UtcNow;

        if (IsPostgresProvider())
        {
            var sequenceValue = await DocumentNumberingSupport.GetNextSequenceValueAsync(
                _dbContext,
                DocumentNumberingSupport.OrderNumberSequenceName,
                cancellationToken);

            return DocumentNumberingSupport.FormatCheckoutOrderNumber(tenantId, timestampUtc, sequenceValue);
        }

        // Fallback for SQL Server and other providers: query-based numbering with tenant locking
        await LockTenantForOrderNumberAsync(tenantId, cancellationToken);

        var tenantPrefix = tenantId.ToString("N")[..4].ToUpperInvariant();
        var prefix = $"ORD-{tenantPrefix}-{timestampUtc:yyyy}";
        
        // Query all order numbers with the current year prefix
        var orderNumbers = await _dbContext.Orders
            .Where(order =>
                order.TenantId == tenantId &&
                order.OrderNumber.StartsWith(prefix))
            .Select(order => order.OrderNumber)
            .ToListAsync(cancellationToken);

        // Parse all numeric suffixes and find the maximum
        int? maxSuffix = null;
        foreach (var orderNumber in orderNumbers)
        {
            var parts = orderNumber.Split('-');
            if (parts.Length >= 4 && int.TryParse(parts[^1], out var suffix))
            {
                maxSuffix = maxSuffix.HasValue ? Math.Max(maxSuffix.Value, suffix) : suffix;
            }
        }

        return GenerateCheckoutOrderNumberFromMaxSuffix(maxSuffix, tenantId, timestampUtc);
    }

    private async Task LockTenantForOrderNumberAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        Tenant? tenant;

        if (IsPostgresProvider())
        {
            tenant = await _dbContext.Tenants
                .FromSqlInterpolated($@"SELECT * FROM ""Tenants"" WHERE ""Id"" = {tenantId} FOR UPDATE")
                .FirstOrDefaultAsync(cancellationToken);
        }
        else if (IsSqlServerProvider())
        {
            tenant = await _dbContext.Tenants
                .FromSqlInterpolated($@"SELECT * FROM [Tenants] WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE [Id] = {tenantId}")
                .FirstOrDefaultAsync(cancellationToken);
        }
        else
        {
            tenant = await _dbContext.Tenants.FirstOrDefaultAsync(entity => entity.Id == tenantId, cancellationToken);
        }

        if (tenant == null)
        {
            throw new InvalidOperationException("Tenant not found");
        }
    }

    private static string GenerateCheckoutOrderNumberFromMaxSuffix(int? lastSuffix, Guid tenantId, DateTime timestampUtc)
    {
        var tenantPrefix = tenantId.ToString("N")[..4].ToUpperInvariant();
        var prefix = $"ORD-{tenantPrefix}-{timestampUtc:yyyy}";

        if (!lastSuffix.HasValue)
        {
            return $"{prefix}-00001";
        }

        var nextNumber = DocumentNumberingSupport.IncrementLegacyNumber(lastSuffix.Value);
        return $"{prefix}-{nextNumber:D5}";
    }

    private string FormatAddress(AddressDto address)
    {
        var parts = new List<string> { address.Street };
        if (!string.IsNullOrWhiteSpace(address.Street2))
            parts.Add(address.Street2);
        if (!string.IsNullOrWhiteSpace(address.Building))
            parts.Add($"Building {address.Building.Trim()}");
        if (!string.IsNullOrWhiteSpace(address.Floor))
            parts.Add($"Floor {address.Floor.Trim()}");
        if (!string.IsNullOrWhiteSpace(address.Apartment))
            parts.Add($"Apartment {address.Apartment.Trim()}");
        if (!string.IsNullOrWhiteSpace(address.Landmark))
            parts.Add($"Landmark: {address.Landmark.Trim()}");
        
        return string.Join(", ", parts);
    }

    private PaymentMethod ParsePaymentMethod(string method)
    {
        return method.ToLowerInvariant() switch
        {
            "card" => PaymentMethod.Card,
            "cash" or "cash_on_delivery" or "cod" => PaymentMethod.Cash,
            "bank" or "bank_transfer" or "manualwallet" => PaymentMethod.BankTransfer,
            _ => PaymentMethod.Card
        };
    }

    /// <summary>
    /// Format estimated delivery from MongoDB ShippingMethodDocument.
    /// CQRS COMPLIANT: Uses MongoDB read model, not PostgreSQL entity.
    /// </summary>
    private string FormatEstimatedDelivery(ShippingMethodDocument method)
    {
        var min = method.EstimatedDeliveryDaysMin;
        var max = method.EstimatedDeliveryDaysMax;
        
        if (min == max)
        {
            return min == 1 ? "1 day" : $"{min} days";
        }
        return $"{min}-{max} days";
    }

    #endregion

    #region Step 7: Payment Intent

    private string GeneratePaymentReference(Guid orderId)
    {
        // Generate unique payment reference
        // Format: PAY-{timestamp}-{orderIdShort}
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var orderIdShort = orderId.ToString("N")[..8].ToUpperInvariant();
        return $"PAY-{timestamp}-{orderIdShort}";
    }

    private async Task<CheckoutPaymentSession> CreateCheckoutPaymentSessionAsync(
        Guid tenantId,
        Order order,
        string paymentMethodCode,
        string? checkoutToken,
        Guid? customerId,
        bool promotionCalculationFailed,
        string requestId,
        string correlationId,
        string? walletType,
        string? walletPhoneNumber,
        string? proofFileKey,
        CancellationToken cancellationToken)
    {
        var response = await _paymentGateway.CreateSessionAsync(
            new CanonicalPaymentRequest
            {
                TenantId = tenantId,
                OrderId = order.Id,
                ProviderCode = new ProviderCode(paymentMethodCode),
                CheckoutToken = checkoutToken,
                CustomerId = customerId,
                PromotionCalculationFailed = promotionCalculationFailed,
                RequestId = requestId,
                CorrelationId = correlationId,
                WalletType = walletType,
                WalletPhoneNumber = walletPhoneNumber,
                ProofFileKey = proofFileKey
            },
            cancellationToken);

        return new CheckoutPaymentSession
        {
            PaymentIntentId = response.PaymentIntentId,
            PaymentReference = response.PaymentReference,
            RedirectUrl = response.RedirectUrl,
            ReferenceCode = response.ReferenceCode,
            ExpiresAtUtc = response.ExpiresAtUtc,
            IsManualPayment = response.IsManualPayment,
            CompatibilityFields = response.CompatibilityFields
        };
    }

    private async Task<bool> TryCreateTransactionalPaymentIntentAuthorityAsync(
        Guid tenantId,
        Order order,
        PaymentMethodDocument paymentMethod,
        string requestId,
        string? walletType,
        string? walletPhoneNumber,
        string? proofFileKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var provider = ResolveTransactionalCheckoutPaymentProvider(paymentMethod.Code);
        if (!provider.HasValue)
        {
            return false;
        }

        var existingIntent = await _dbContext.PaymentIntents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                intent =>
                    intent.TenantId == tenantId &&
                    intent.OrderId == order.Id &&
                    !intent.IsDeleted &&
                    intent.Provider == provider.Value &&
                    (intent.Status == PaymentIntentStatus.Pending ||
                     intent.Status == PaymentIntentStatus.Authorized ||
                     intent.Status == PaymentIntentStatus.Paid ||
                     intent.Status == PaymentIntentStatus.AwaitingVerification),
                cancellationToken);

        if (existingIntent != null)
        {
            return true;
        }

        var paymentReference = GeneratePaymentReference(order.Id);
        var paymentIntent = new PaymentIntent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            Amount = order.Total,
            Currency = order.Currency,
            PaymentMethod = order.PaymentMethod,
            Provider = provider.Value,
            PaymentReference = paymentReference,
            IdempotencyKey = requestId,
            Status = PaymentIntentStatus.Pending,
            WalletType = walletType?.Trim().ToUpperInvariant(),
            WalletPhoneNumber = walletPhoneNumber?.Trim(),
            ProofImageUrl = proofFileKey?.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.PaymentIntents.Add(paymentIntent);
        order.PaymentReference = paymentReference;
        order.PaymentGateway = paymentMethod.Code.Trim().ToLowerInvariant();

        // NOTE: Do NOT transition from PendingPaymentIntent to PaymentPending here.
        // This is a seeded placeholder authority without provider tokens/contract.
        // The order should remain in PendingPaymentIntent until the provider materializes the intent.
        // This prevents replay/compensation logic from treating it as a real provider-created intent.

        _logger.LogInformation(
            "Seeded transactional payment intent authority before checkout commit | TenantId: {TenantId} | OrderId: {OrderId} | PaymentIntentId: {PaymentIntentId} | Provider: {Provider} | CorrelationId: {CorrelationId}",
            tenantId,
            order.Id,
            paymentIntent.Id,
            provider.Value,
            correlationId);

        return true;
    }

    private static PaymentProvider? ResolveTransactionalCheckoutPaymentProvider(string? paymentMethodCode)
    {
        return paymentMethodCode?.Trim().ToUpperInvariant() switch
        {
            "STRIPE" => PaymentProvider.Stripe,
            "PAYMOB" => PaymentProvider.Paymob,
            "FAWRY" => PaymentProvider.Fawry,
            "MANUALWALLET" => PaymentProvider.ManualWallet,
            _ => null
        };
    }

    #endregion

    #region Event Emission

    private async Task EmitCheckoutStartedEventAsync(
        Guid tenantId, Guid cartId, Guid shippingMethodId, string paymentMethod,
        string ownerType, string ownerId, int itemCount,
        string requestId, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            var evt = new CheckoutStartedEvent
            {
                TenantId = tenantId,
                CartId = cartId,
                ShippingMethodId = shippingMethodId,
                PaymentMethod = paymentMethod,
                OwnerType = ownerType,
                OwnerId = ownerId,
                ItemCount = itemCount,
                IdempotencyKey = requestId,
                CorrelationId = correlationId,
                OccurredAtUtc = DateTime.UtcNow
            };

            await _eventPublisher.PublishAsync(evt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit CheckoutStartedEvent");
        }
    }

    /// <summary>
    /// Emits CartActivityEvent for abandoned cart tracking.
    /// PHASE: Smart Abandoned Cart Recovery Engine
    /// 
    /// This is fired when customer starts checkout but BEFORE order is created.
    /// If checkout fails/abandons, the scanner job will detect it.
    /// </summary>
    private async Task EmitCartActivityEventAsync(
        Guid tenantId, Guid cartId, string? sessionId, Guid? customerId,
        string? phoneNumber, string? email,
        CartSnapshot cartSnapshot,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Track cart activity even without phone (phone can be added later)
            // WhatsApp recovery requires phone, but we still want to track abandonment
            if (string.IsNullOrWhiteSpace(phoneNumber))
            {
                _logger.LogDebug(
                    "[Abandoned Cart] Tracking activity without phone (recovery will wait for phone) | TenantId: {TenantId} | CartId: {CartId}",
                    tenantId, cartId);
            }

            var contact = await ResolveVerifiedContactAsync(
                tenantId,
                sessionId,
                customerId,
                phoneNumber,
                email,
                cancellationToken);

            var persistedCouponState = await _dbContext.StorefrontCartCouponStates
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.TenantId == tenantId && item.CartId == cartId,
                    cancellationToken);

            var recoverySnapshot = RecoveredCartSnapshotFactory.FromCheckoutSnapshot(
                cartSnapshot,
                persistedCouponState);
            var snapshotJson = JsonSerializer.Serialize(recoverySnapshot, _jsonOptions);

            var evt = new CartActivityEvent
            {
                TenantId = tenantId,
                CartId = cartId,
                SessionId = sessionId,
                CustomerId = customerId,
                PhoneNumber = contact.PhoneNumber,
                Email = contact.Email,
                ContactChannel = DetermineContactChannel(contact.PhoneNumber, contact.Email),
                PhoneVerified = contact.PhoneVerified,
                EmailVerified = contact.EmailVerified,
                CartSnapshotJson = snapshotJson,
                CorrelationId = correlationId,
                OccurredAtUtc = DateTime.UtcNow
            };

            await _eventPublisher.PublishAsync(evt, cancellationToken);

            _logger.LogDebug(
                "[Abandoned Cart] Cart activity tracked | TenantId: {TenantId} | CartId: {CartId} | SessionId: {SessionId} | CustomerId: {CustomerId} | ContactChannel: {ContactChannel} | PhoneVerified: {PhoneVerified} | EmailVerified: {EmailVerified}",
                tenantId,
                cartId,
                sessionId,
                customerId,
                evt.ContactChannel,
                evt.PhoneVerified,
                evt.EmailVerified);
        }
        catch (Exception ex)
        {
            _reliabilityTelemetry?.RecordAbandonedCartSkipped(tenantId, "checkout_activity", "publish_failed");

            // Fire-and-forget - don't fail checkout
            _logger.LogWarning(ex, 
                "[Abandoned Cart] Failed to emit CartActivityEvent | TenantId: {TenantId} | CartId: {CartId} | SessionId: {SessionId} | CustomerId: {CustomerId} | CorrelationId: {CorrelationId}",
                tenantId,
                cartId,
                sessionId,
                customerId,
                correlationId);
        }
    }

    private async Task<VerifiedContactSnapshot> ResolveVerifiedContactAsync(
        Guid tenantId,
        string? sessionId,
        Guid? customerId,
        string? phoneNumber,
        string? email,
        CancellationToken cancellationToken)
    {
        var resolvedPhone = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
        var resolvedEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        var phoneVerified = false;
        var emailVerified = false;

        if (customerId.HasValue && customerId.Value != Guid.Empty)
        {
            var customerContact = await _dbContext.OnlineCustomers
                .AsNoTracking()
                .Where(c => c.TenantId == tenantId && c.Id == customerId.Value)
                .Select(c => new
                {
                    c.Phone,
                    c.PhoneVerified,
                    c.Email,
                    c.EmailVerified
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (customerContact != null)
            {
                if (string.IsNullOrWhiteSpace(resolvedPhone) && customerContact.PhoneVerified)
                {
                    resolvedPhone = customerContact.Phone;
                }

                if (string.IsNullOrWhiteSpace(resolvedEmail) && customerContact.EmailVerified)
                {
                    resolvedEmail = customerContact.Email;
                }

                phoneVerified = customerContact.PhoneVerified &&
                    !string.IsNullOrWhiteSpace(resolvedPhone) &&
                    PhonesMatch(resolvedPhone, customerContact.Phone);

                emailVerified = customerContact.EmailVerified &&
                    !string.IsNullOrWhiteSpace(resolvedEmail) &&
                    EmailsMatch(resolvedEmail, customerContact.Email);
            }
        }
        else if (!string.IsNullOrWhiteSpace(sessionId) &&
                 !string.IsNullOrWhiteSpace(resolvedPhone) &&
                 _phoneVerificationService != null)
        {
            phoneVerified = await _phoneVerificationService.IsPhoneVerifiedForSessionAsync(
                tenantId,
                sessionId,
                resolvedPhone,
                cancellationToken);
        }

        return new VerifiedContactSnapshot(
            resolvedPhone,
            resolvedEmail,
            phoneVerified,
            emailVerified);
    }

    private static bool PhonesMatch(string? first, string? second)
    {
        var normalizedFirst = NormalizePhoneForComparison(first);
        var normalizedSecond = NormalizePhoneForComparison(second);
        return normalizedFirst.Length > 0 &&
            normalizedSecond.Length > 0 &&
            string.Equals(normalizedFirst, normalizedSecond, StringComparison.Ordinal);
    }

    private static string NormalizePhoneForComparison(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return string.Empty;
        }

        return new string(phone.Where(char.IsDigit).ToArray());
    }

    private static bool EmailsMatch(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) &&
        !string.IsNullOrWhiteSpace(second) &&
        string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? DetermineContactChannel(string? phone, string? email)
    {
        var hasPhone = !string.IsNullOrWhiteSpace(phone);
        var hasEmail = !string.IsNullOrWhiteSpace(email);

        return (hasPhone, hasEmail) switch
        {
            (true, true) => "phone,email",
            (true, false) => "phone",
            (false, true) => "email",
            _ => null
        };
    }

    /// <summary>
    /// Normalizes and validates service level values.
    /// 
    /// NORMALIZATION RULES:
    /// 1. Trim whitespace and convert to lowercase
    /// 2. Replace common delimiters (underscores, spaces, hyphens, slashes) with underscores
    /// 3. Map common aliases to canonical values:
    ///    - "same-day", "sameday", "same day" -> "same_day"
    ///    - "next-day", "nextday", "next day" -> "next_day"
    /// 4. Validate against whitelist: standard, express, same_day, next_day
    /// 5. If unrecognized, log a warning and return "standard" as safe fallback
    /// 
    /// CRITICAL: This method MUST log warnings for unrecognized values to prevent
    /// silent downgrades of customer-selected service levels.
    /// </summary>
    private string NormalizeServiceLevel(string? serviceLevel, Guid? shippingMethodId = null, string? shippingMethodName = null)
    {
        if (string.IsNullOrWhiteSpace(serviceLevel))
        {
            return "standard";
        }

        var original = serviceLevel;
        
        // Step 1: Trim and lowercase
        var normalized = serviceLevel.Trim().ToLowerInvariant();
        
        // Step 2: Replace common delimiters with underscores
        normalized = normalized
            .Replace(" ", "_")
            .Replace("-", "_")
            .Replace("/", "_")
            .Replace("\\", "_");
        
        // Step 3: Map common aliases
        normalized = normalized switch
        {
            "sameday" => "same_day",
            "nextday" => "next_day",
            _ => normalized
        };
        
        // Step 4: Validate against whitelist
        var isValid = normalized switch
        {
            "standard" => true,
            "express" => true,
            "same_day" => true,
            "next_day" => true,
            _ => false
        };
        
        if (isValid)
        {
            return normalized;
        }
        
        // Step 5: Log warning for unrecognized value and return safe fallback
        _logger.LogWarning(
            "Unrecognized service level value detected - falling back to 'standard' | " +
            "OriginalValue: {OriginalValue} | NormalizedValue: {NormalizedValue} | " +
            "ShippingMethodId: {ShippingMethodId} | ShippingMethodName: {ShippingMethodName}",
            original,
            normalized,
            shippingMethodId,
            shippingMethodName);
        
        return "standard"; // Safe fallback after logging
    }

    private sealed record VerifiedContactSnapshot(
        string? PhoneNumber,
        string? Email,
        bool PhoneVerified,
        bool EmailVerified);

    private async Task EmitCheckoutFailedEventAsync(
        Guid tenantId, Guid orderId, Guid cartId, string failedStep,
        string errorCode, string errorMessage, bool inventoryReserved,
        long durationMs, string requestId, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            var evt = new CheckoutFailedEvent
            {
                TenantId = tenantId,
                OrderId = orderId,
                CartId = cartId,
                FailedStep = failedStep,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                InventoryReserved = inventoryReserved,
                DurationMs = durationMs,
                IdempotencyKey = requestId,
                CorrelationId = correlationId,
                OccurredAtUtc = DateTime.UtcNow
            };

            await _eventPublisher.PublishAsync(evt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit CheckoutFailedEvent");
        }
    }

    private async Task EmitOrderCreatedEventAsync(
        Guid tenantId, Order order, CartSnapshot snapshot, string shippingMethodName,
        AffiliateAttributionAuthority? affiliateAuthority,
        string requestId, string correlationId, CancellationToken cancellationToken)
    {
            var orderCurrency = await EnsureCheckoutCurrencyAsync(
                tenantId,
                order.Currency,
                "checkout.order_created_event.currency",
                "OrderEventPublishing",
                "Order currency is missing or invalid.",
                order.Id,
                "checkout",
                cancellationToken,
                eventPublish: true);

            // =====================================================================
            // PHASE 10.5.A: Emit OrderCreatedEvent (from Core) for MongoDB projection
            // This event is consumed by OrderProjectionConsumer to create the
            // OrderDocument in MongoDB for Storefront CQRS reads.
            // =====================================================================
            var orderCreatedEventVersion = PointOfSale.Core.Helpers.ProductEventVersionHelper.ToProductEventVersion(
                Math.Max(1L, order.ProjectionMutationVersion));

            var coreEvent = new PointOfSale.Core.Events.OrderCreatedEvent
            {
                TenantId = tenantId,
                EventVersion = orderCreatedEventVersion,
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                CustomerId = order.CustomerId,
                OnlineCustomerId = order.OnlineCustomerId,
                CustomerEmail = order.GuestEmail,
                CustomerName = order.GuestName,
                Status = order.Status,
                PaymentStatus = order.PaymentStatus,
                Source = order.Source,
                SourceOrderId = order.SourceOrderId,
                Subtotal = order.Subtotal,
                PromotionDiscountAmount = order.PromotionDiscountAmount,
                DiscountAmount = order.DiscountAmount,
                TaxAmount = order.TaxAmount,
                ShippingCost = order.ShippingCost,
                Total = order.Total,
                Currency = orderCurrency,
                ShippingMethodName = shippingMethodName,
                PaymentMethodCode = order.PaymentMethodCode,
                PaymentMethodName = order.PaymentMethodName,
                SessionId = order.SessionId,
                IsPaid = order.IsPaid,
                CartId = snapshot.CartId,
                // FULFILLMENT OWNERSHIP SNAPSHOT
                FulfillmentMode = order.FulfillmentMode,
                FulfillmentCarrierCode = order.FulfillmentCarrierCode,
                // PHASE 10.5.A2: Shipping address for order tracking
                ShippingStreet = order.ShippingAddress,
                ShippingCity = order.ShippingCity,
                ShippingState = order.ShippingState,
                ShippingZipCode = order.ShippingZipCode,
                ShippingCountry = order.ShippingCountry,
                ShippingPhone = order.GuestPhone,
                RecipientName = order.GuestName,
                Items = order.Items.Select(i => new PointOfSale.Core.Events.OrderItemInfo
                {
                    ProductId = i.ProductId,
                    // PHASE 11.7: Include VariantId for SKU-level tracking
                    VariantId = i.VariantId,
                    SKU = i.SKU,
                    ProductName = i.ProductName,
                    VariantName = i.VariantName,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice,
                    Total = i.Total,
                    ImageUrl = i.ImageUrl
                }).ToList(),
                CorrelationId = correlationId,
                OccurredAtUtc = DateTime.UtcNow
            };

            await _marketplaceEventOutbox.EnqueueAsync(new PointOfSale.Core.Events.Marketplace.OrderCreatedEvent
            {
                CorrelationId = correlationId,
                EntityId = order.Id,
                AggregateRootId = order.Id,
                TenantId = order.TenantId,
                Version = orderCreatedEventVersion,
                OrderNumber = order.OrderNumber,
                CustomerId = order.CustomerId,
                OnlineCustomerId = order.OnlineCustomerId,
                TotalAmount = order.Total,
                SubtotalAmount = order.Subtotal,
                Currency = orderCurrency,
                PaymentMethodCode = order.PaymentMethodCode,
                PaymentMethodName = order.PaymentMethodName,
                IsPaid = order.IsPaid,
                Items = order.Items.Select(i => new PointOfSale.Core.Events.Marketplace.MarketplaceOrderItem
                {
                    ProductId = i.ProductId,
                    VariantId = i.VariantId,
                    Sku = i.SKU,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice
                }).ToList()
            }, cancellationToken);

            // ARCHITECTURE FIX: Use Publish for proper RabbitMQ exchange routing
            // The OrderProjectionConsumer will receive this via exchange bindings configured in OrderProjectionConsumerDefinition
            await _eventPublisher.PublishAsync(coreEvent, cancellationToken);
            
            _logger.LogInformation(
                "[Order] OrderCreatedEvent PUBLISHED via MassTransit | TenantId: {TenantId} | OrderId: {OrderId} | OrderNumber: {OrderNumber} | CustomerId: {CustomerId} | OnlineCustomerId: {OnlineCustomerId} | CorrelationId: {CorrelationId}",
                coreEvent.TenantId,
                coreEvent.OrderId,
                coreEvent.OrderNumber,
                coreEvent.CustomerId,
                coreEvent.OnlineCustomerId,
                correlationId);



            // Also emit CheckoutOrderCreatedEvent for other consumers (non-CQRS)
            var evt = new CheckoutOrderCreatedEvent
            {
                TenantId = tenantId,
                OrderId = order.Id,
                CartId = snapshot.CartId,
                OrderNumber = order.OrderNumber,
                CustomerId = order.CustomerId,
                OnlineCustomerId = order.OnlineCustomerId,
                GuestEmail = order.GuestEmail,
                // AF-13B: Guest checkout fields
                GuestPhone = order.GuestPhone,
                IsGuestCheckout = order.IsGuestCheckout,
                Subtotal = order.Subtotal,
                DiscountAmount = order.DiscountAmount,
                ShippingCost = order.ShippingCost,
                TaxAmount = order.TaxAmount,
                Total = order.Total,
                Currency = orderCurrency,
                ItemCount = order.Items.Count,
                ShippingMethodName = shippingMethodName,
                // PHASE 10.4.D3: Payment method snapshot
                PaymentMethodCode = order.PaymentMethodCode,
                PaymentMethodName = order.PaymentMethodName,
                PaymentFee = order.PaymentFee,
                IsPaid = order.IsPaid,
                // AF-11: Affiliate attribution — enables downstream attribution + commission
                AffiliateAccountId = order.AffiliateAccountId,
                AffiliateReferralCode = order.AffiliateReferralCode,
                Items = order.Items.Select(i => new CheckoutOrderItemInfo
                {
                    ProductId = i.ProductId,
                    SKU = i.SKU,
                    ProductName = i.ProductName,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice,
                    Total = i.Total
                }).ToList(),
                IdempotencyKey = requestId,
                CorrelationId = correlationId,
                OccurredAtUtc = DateTime.UtcNow
            };

            await _eventPublisher.PublishAsync(evt, cancellationToken);

            // ═══════════════════════════════════════════════════════════════════
            // AF-11: Publish AffiliateOrderPlacedEvent for attribution consumer
            // 
            // This event lives in Core (not API) so Infrastructure consumers
            // can subscribe without circular dependency.
            // Only emitted for affiliate-referred orders.
            // ═══════════════════════════════════════════════════════════════════
            if (order.AffiliateAccountId.HasValue && !string.IsNullOrWhiteSpace(order.AffiliateReferralCode))
            {
                var affiliateEvt = new AffiliateOrderPlacedEvent
                {
                    TenantId = tenantId,
                    StoreTenantId = tenantId,
                    AffiliateAuthorityTenantId = NormalizeAffiliateAuthorityTenantId(affiliateAuthority?.AuthorityTenantId),
                    OrderId = order.Id,
                    OrderNumber = order.OrderNumber,
                    AffiliateAccountId = order.AffiliateAccountId.Value,
                    ReferralCode = order.AffiliateReferralCode,
                    OrderTotal = order.Total,
                    Currency = orderCurrency,
                    PaymentMethodCode = order.PaymentMethodCode,
                    ItemCount = order.Items.Count,
                    IsGuestCheckout = order.IsGuestCheckout,
                    // AF-15: Include PromotionLinkId for v2 flow
                    PromotionLinkId = order.PromotionLinkId,
                    AffiliateUserId = affiliateAuthority?.AffiliateUserId,
                    CorrelationId = correlationId,
                    OccurredAtUtc = DateTime.UtcNow,
                    EventVersion = orderCreatedEventVersion
                };

                await _eventPublisher.PublishAsync(affiliateEvt, cancellationToken);

                _logger.LogInformation(
                    "[AF-11/15] AffiliateOrderPlacedEvent published | OrderId: {OrderId} | AffiliateAccountId: {AffiliateAccountId} | " +
                    "ReferralCode: {ReferralCode} | PromotionLinkId: {PromotionLinkId} | CorrelationId: {CorrelationId}",
                    order.Id, order.AffiliateAccountId.Value, order.AffiliateReferralCode, order.PromotionLinkId, correlationId);
            }
    }

    private async Task EmitPaymentIntentCreatedEventAsync(
        Guid tenantId, Guid orderId, Guid cartId, string orderNumber,
        string paymentReference, string paymentMethod, decimal amount, string currency,
        string? redirectUrl, bool promotionCalculationFailed, string requestId, string correlationId, int eventVersion, CancellationToken cancellationToken)
    {
        var validatedCurrency = await EnsureCheckoutCurrencyAsync(
            tenantId,
            currency,
            "checkout.payment_intent_event.currency",
            "PaymentIntentEventPublishing",
            "Payment intent currency is missing or invalid.",
            orderId,
            "checkout",
            cancellationToken,
            eventPublish: true);

        var evt = new PaymentIntentCreatedEvent
        {
            TenantId = tenantId,
            OrderId = orderId,
            CartId = cartId,
            OrderNumber = orderNumber,
            PaymentReference = paymentReference,
            PaymentMethod = paymentMethod,
            Amount = amount,
            Currency = validatedCurrency,
            RedirectUrl = redirectUrl,
            IsOfflineCollection = paymentMethod.Equals("COD", StringComparison.OrdinalIgnoreCase),
            PromotionCalculationFailed = promotionCalculationFailed,
            IdempotencyKey = requestId,
            CorrelationId = correlationId,
            OccurredAtUtc = DateTime.UtcNow,
            EventVersion = eventVersion
        };

        await PersistPaymentIntentCreatedOutboxAsync(evt, tenantId, orderId, paymentReference, requestId, cancellationToken);
    }

    private async Task PersistPaymentIntentCreatedOutboxAsync(
        PaymentIntentCreatedEvent evt,
        Guid tenantId,
        Guid orderId,
        string paymentReference,
        string requestId,
        CancellationToken cancellationToken)
    {
        var outboxMessageId = CreateDeterministicOutboxMessageId(
            nameof(PaymentIntentCreatedEvent),
            tenantId,
            orderId,
            paymentReference,
            requestId);

        var existing = await _dbContext.OutboxMessages
            .AsNoTracking()
            .AnyAsync(message => message.Id == outboxMessageId, cancellationToken);
        if (existing)
        {
            _logger.LogInformation(
                "PaymentIntentCreatedEvent outbox row already exists | TenantId: {TenantId} | OrderId: {OrderId} | OutboxMessageId: {OutboxMessageId}",
                tenantId,
                orderId,
                outboxMessageId);
            return;
        }

        var outboxMessage = new OutboxMessage
        {
            Id = outboxMessageId,
            EventType = typeof(PaymentIntentCreatedEvent).FullName ?? nameof(PaymentIntentCreatedEvent),
            EventPayload = JsonSerializer.Serialize(evt, _jsonOptions),
            TenantId = tenantId,
            AggregateId = orderId,
            EventVersion = evt.EventVersion,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.OutboxMessages.Add(outboxMessage);

        _logger.LogInformation(
            "PaymentIntentCreatedEvent added to outbox (will be persisted with order state) | TenantId: {TenantId} | OrderId: {OrderId} | OutboxMessageId: {OutboxMessageId}",
            tenantId,
            orderId,
            outboxMessage.Id);
    }

    private static Guid CreateDeterministicOutboxMessageId(
        string eventName,
        Guid tenantId,
        Guid aggregateId,
        string businessReference,
        string idempotencyKey)
    {
        var raw = $"{eventName}:{tenantId:N}:{aggregateId:N}:{businessReference}:{idempotencyKey}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return new Guid(hash[..16]);
    }

    private async Task PersistInventoryReservedOutboxAsync(
        Guid tenantId,
        Guid orderId,
        Guid cartId,
        Guid reservationId,
        List<InventoryReservationItem> items,
        DateTime expiresAtUtc,
        string requestId,
        string correlationId,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        var evt = new InventoryReservedEvent
        {
            TenantId = tenantId,
            OrderId = orderId,
            CartId = cartId,
            ReservationId = reservationId,
            ReservedItems = items,
            ExpiresAtUtc = expiresAtUtc,
            IdempotencyKey = requestId,
            CorrelationId = correlationId,
            OccurredAtUtc = DateTime.UtcNow,
            EventVersion = eventVersion
        };

        var outboxMessageId = CreateDeterministicOutboxMessageId(
            nameof(InventoryReservedEvent),
            tenantId,
            orderId,
            reservationId.ToString("N"),
            requestId);

        var existing = await _dbContext.OutboxMessages
            .AsNoTracking()
            .AnyAsync(message => message.Id == outboxMessageId, cancellationToken);
        if (existing)
        {
            _logger.LogInformation(
                "InventoryReservedEvent outbox row already exists | TenantId: {TenantId} | OrderId: {OrderId} | ReservationId: {ReservationId} | OutboxMessageId: {OutboxMessageId}",
                tenantId,
                orderId,
                reservationId,
                outboxMessageId);
            return;
        }

        var outboxMessage = new OutboxMessage
        {
            Id = outboxMessageId,
            EventType = typeof(InventoryReservedEvent).FullName ?? nameof(InventoryReservedEvent),
            EventPayload = JsonSerializer.Serialize(evt, _jsonOptions),
            TenantId = tenantId,
            AggregateId = orderId,
            EventVersion = eventVersion,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.OutboxMessages.Add(outboxMessage);

        _logger.LogInformation(
            "InventoryReservedEvent added to outbox (will be persisted with order state) | TenantId: {TenantId} | OrderId: {OrderId} | ReservationId: {ReservationId} | OutboxMessageId: {OutboxMessageId}",
            tenantId,
            orderId,
            reservationId,
            outboxMessage.Id);
    }

    private async Task DispatchPersistedInventoryReservedOutboxAsync(
        Guid tenantId,
        Guid orderId,
        Guid reservationId,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var outboxMessageId = CreateDeterministicOutboxMessageId(
            nameof(InventoryReservedEvent),
            tenantId,
            orderId,
            reservationId.ToString("N"),
            requestId);

        var outboxExists = await _dbContext.OutboxMessages
            .AsNoTracking()
            .AnyAsync(message => message.Id == outboxMessageId, cancellationToken);

        if (!outboxExists)
        {
            _logger.LogError(
                "InventoryReservedEvent outbox row was missing after order commit | TenantId: {TenantId} | OrderId: {OrderId} | ReservationId: {ReservationId} | OutboxMessageId: {OutboxMessageId} | CorrelationId: {CorrelationId}",
                tenantId,
                orderId,
                reservationId,
                outboxMessageId,
                correlationId);
            
            throw new CheckoutPostCommitConsistencyException(
                tenantId,
                orderId,
                correlationId,
                $"InventoryReservedEvent outbox row was missing after order commit. " +
                $"TenantId: {tenantId}, OrderId: {orderId}, ReservationId: {reservationId}, " +
                $"OutboxMessageId: {outboxMessageId}, CorrelationId: {correlationId}");
        }

        _logger.LogInformation(
            "InventoryReservedEvent queued durably for outbox dispatch post-commit | TenantId: {TenantId} | OrderId: {OrderId} | ReservationId: {ReservationId} | OutboxMessageId: {OutboxMessageId} | CorrelationId: {CorrelationId}",
            tenantId,
            orderId,
            reservationId,
            outboxMessageId,
            correlationId);
    }

    private async Task EmitPriceRecalculationEventAsync(
        Guid tenantId, Guid cartId, decimal originalSubtotal, decimal newSubtotal,
        decimal diffPercent, bool accepted, List<PriceChangeItem> changes,
        string requestId, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            var evt = new PriceRecalculationEvent
            {
                TenantId = tenantId,
                CartId = cartId,
                OriginalSubtotal = originalSubtotal,
                RecalculatedSubtotal = newSubtotal,
                DifferencePercent = diffPercent,
                Accepted = accepted,
                PriceChanges = changes,
                IdempotencyKey = requestId,
                CorrelationId = correlationId,
                OccurredAtUtc = DateTime.UtcNow
            };

            await _eventPublisher.PublishAsync(evt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit PriceRecalculationEvent");
        }
    }

    private async Task EmitInventoryReservedEventAsync(
        Guid tenantId, Guid orderId, Guid cartId, Guid reservationId,
        List<InventoryReservationItem> items, DateTime expiresAt,
        string requestId, string correlationId, CancellationToken cancellationToken)
    {
        var evt = new InventoryReservedEvent
        {
            TenantId = tenantId,
            OrderId = orderId,
            CartId = cartId,
            ReservationId = reservationId,
            ReservedItems = items,
            ExpiresAtUtc = expiresAt,
            IdempotencyKey = requestId,
            CorrelationId = correlationId,
            OccurredAtUtc = DateTime.UtcNow,
            EventVersion = 1
        };

        await _eventPublisher.PublishAsync(evt, cancellationToken);
    }

    private async Task EmitInventoryReleasedEventAsync(
        Guid tenantId, Guid orderId, Guid cartId, Guid reservationId, string reason,
        string requestId, string correlationId, CancellationToken cancellationToken)
    {
        var evt = new InventoryReservationReleasedEvent
        {
            TenantId = tenantId,
            OrderId = orderId,
            CartId = cartId,
            ReservationId = reservationId,
            ReleaseReason = reason,
            IdempotencyKey = requestId,
            CorrelationId = correlationId,
            OccurredAtUtc = DateTime.UtcNow
        };

        await _eventPublisher.PublishAsync(evt, cancellationToken);
    }

    #endregion

    #region Logging

    private void LogCheckoutSummary(Guid tenantId, Guid cartId, List<CheckoutStepResult> steps, string correlationId)
    {
        var summary = steps
            .Select(s => $"{s.StepName}:{(s.Success ? "OK" : "FAIL")}:{s.DurationMs}ms")
            .ToList();

        _logger.LogInformation(
            "Checkout summary | TenantId: {TenantId} | CartId: {CartId} | Steps: [{Steps}] | CorrelationId: {CorrelationId}",
            tenantId, cartId, string.Join(", ", summary), correlationId);
    }

    #endregion

    #region WhatsApp Integration (Phase 13)

    /// <summary>
    /// Attempts to emit WhatsApp order confirmation event if feature is enabled.
    /// This is NON-BLOCKING and NON-FATAL - failures are logged but don't affect order.
    /// </summary>
    private async Task TryEmitWhatsAppOrderConfirmationAsync(
        Guid tenantId,
        Order order,
        string currency,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Skip if WhatsApp feature gate is not injected
            if (_whatsAppFeatureGate == null)
            {
                _logger.LogDebug("[WhatsApp] Feature gate not available, skipping | OrderId: {OrderId}", order.Id);
                return;
            }

            // Check if WhatsApp order confirmation is enabled for this tenant
            var isEnabled = await _whatsAppFeatureGate.IsOrderConfirmationEnabledAsync(tenantId, cancellationToken);
            if (!isEnabled)
            {
                _logger.LogDebug(
                    "[WhatsApp] Order confirmation not enabled for tenant | TenantId: {TenantId} | OrderId: {OrderId}",
                    tenantId, order.Id);
                return;
            }

            // Verify customer has phone number
            var customerPhone = order.GuestPhone;
            if (string.IsNullOrWhiteSpace(customerPhone))
            {
                _logger.LogDebug(
                    "[WhatsApp] No phone number for order, skipping | OrderId: {OrderId}",
                    order.Id);
                return;
            }

            var validatedCurrency = await TryEnsureCheckoutNotificationCurrencyAsync(
                tenantId,
                currency,
                order,
                correlationId,
                cancellationToken);
            if (validatedCurrency == null)
            {
                return;
            }

            // Build and publish event
            var whatsAppEvent = new WhatsAppOrderConfirmationRequestedEvent
            {
                TenantId = tenantId,
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                CustomerPhone = customerPhone,
                CustomerName = order.GuestName ?? "Customer",
                OrderTotal = order.Total,
                Currency = validatedCurrency,
                Items = order.Items.Select(i => new OrderItemInfo
                {
                    ProductId = i.ProductId,
                    VariantId = i.VariantId,
                    SKU = i.SKU,
                    ProductName = i.ProductName,
                    VariantName = i.VariantName,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice,
                    Total = i.Total,
                    ImageUrl = i.ImageUrl
                }).ToList(),
                ShippingAddress = $"{order.ShippingAddress}, {order.ShippingCity}, {order.ShippingState} {order.ShippingZipCode}",
                CorrelationId = correlationId,
                OccurredAtUtc = DateTime.UtcNow,
                EventVersion = 1 // Required for OutboxMessages constraint CK_OutboxMessages_CriticalEvents_EventVersion_Required
            };

            await _eventPublisher.PublishAsync(whatsAppEvent, cancellationToken);

            _logger.LogInformation(
                "[WhatsApp] Order confirmation event published | TenantId: {TenantId} | OrderId: {OrderId} | Phone: {Phone} | CorrelationId: {CorrelationId}",
                tenantId, order.Id, MaskPhone(customerPhone), correlationId);
        }
        catch (Exception ex)
        {
            // NON-FATAL: WhatsApp is optional, don't fail the order
            _logger.LogWarning(ex,
                "[WhatsApp] Failed to emit order confirmation event (non-fatal) | OrderId: {OrderId}",
                order.Id);
        }
    }

    private async Task<string?> TryEnsureCheckoutNotificationCurrencyAsync(
        Guid tenantId,
        string? currency,
        Order order,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await EnsureCheckoutCurrencyAsync(
                tenantId,
                currency ?? order.Currency,
                "checkout.whatsapp_order_confirmation.currency",
                "WhatsAppOrderConfirmation",
                "Order confirmation currency is missing or invalid.",
                order.Id,
                "checkout",
                cancellationToken,
                eventPublish: true);
        }
        catch (CheckoutException ex)
        {
            _logger.LogError(
                ex,
                "[WhatsApp] Order confirmation event blocked due to currency validation | TenantId: {TenantId} | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                tenantId,
                order.Id,
                correlationId);
            return null;
        }
    }

    private async Task<string> EnsureCheckoutCurrencyAsync(
        Guid tenantId,
        string? currency,
        string component,
        string failureStep,
        string userMessage,
        Guid? orderId,
        string source,
        CancellationToken cancellationToken,
        bool eventPublish = false)
    {
        var context = $"tenantId:{tenantId:D};orderId:{orderId?.ToString("D") ?? "none"};component:{component}";

        try
        {
            var tenantCurrency = await ResolveRequiredTenantCurrencyAsync(tenantId, context, cancellationToken);
            return FinancialCurrencyGuard.RequireMatch(
                currency ?? string.Empty,
                component,
                tenantCurrency,
                "tenant.currency",
                context);
        }
        catch (FinancialCurrencyMismatchException ex)
        {
            _reliabilityTelemetry?.RecordCurrencyMismatch(tenantId, orderId, source);
            _logger.LogError(
                ex,
                "Checkout currency mismatch rejected | TenantId: {TenantId} | OrderId: {OrderId} | Component: {Component}",
                tenantId,
                orderId,
                component);
            throw new CheckoutException("CURRENCY_MISMATCH", userMessage, failureStep);
        }
        catch (FinancialCurrencyMissingException ex)
        {
            if (eventPublish)
            {
                _reliabilityTelemetry?.RecordCurrencyMissingEvent(tenantId, orderId, source);
            }
            else
            {
                _reliabilityTelemetry?.RecordCurrencyMissing(tenantId, orderId, source);
            }

            _logger.LogError(
                ex,
                "Checkout currency missing rejected | TenantId: {TenantId} | OrderId: {OrderId} | Component: {Component}",
                tenantId,
                orderId,
                component);
            throw new CheckoutException("CURRENCY_NOT_SET", userMessage, failureStep);
        }
        catch (FinancialCurrencyInvalidException ex)
        {
            if (eventPublish)
            {
                _reliabilityTelemetry?.RecordCurrencyInvalidEvent(tenantId, orderId, source);
            }
            else
            {
                _reliabilityTelemetry?.RecordCurrencyInvalid(tenantId, orderId, source);
            }

            _logger.LogError(
                ex,
                "Checkout currency invalid rejected | TenantId: {TenantId} | OrderId: {OrderId} | Component: {Component}",
                tenantId,
                orderId,
                component);
            throw new CheckoutException("CURRENCY_INVALID", userMessage, failureStep);
        }
    }

    private async Task<string> ResolveRequiredTenantCurrencyAsync(
        Guid tenantId,
        string context,
        CancellationToken cancellationToken)
    {
        var tenantCurrency = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => tenant.Currency)
            .FirstOrDefaultAsync(cancellationToken);

        return FinancialCurrencyGuard.RequireCurrency(
            tenantCurrency,
            "tenant.currency",
            context);
    }

    private static string MaskPhone(string? phone)
    {
        if (string.IsNullOrEmpty(phone) || phone.Length < 7)
            return "***";
        
        return phone.Substring(0, 4) + "****" + phone.Substring(phone.Length - 3);
    }

    private string? CreateCheckoutPaymentToken(Guid tenantId, Guid orderId)
    {
        // SECURITY: Bind payment token to order-specific identifier (order ID).
        // This prevents token reuse across multiple orders from the same browser session.
        // Each order gets its own unique token that cannot be used for other orders.
        return _checkoutSessionTokenService.CreatePaymentToken(tenantId, orderId);
    }

    private string? CreateGuestTrackingToken(Guid tenantId, Guid orderId, string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        return _checkoutSessionTokenService.CreateGuestTrackingToken(tenantId, orderId, sessionId.Trim());
    }

    private static bool ShouldIssueCheckoutToken(Order order) =>
        order.IsGuestCheckout ||
        string.Equals(order.PaymentMethodCode, "STRIPE", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(order.PaymentMethodCode, "PAYMOB", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(order.PaymentMethodCode, "FAWRY", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldIssueGuestTrackingToken(Order order) =>
        order.IsGuestCheckout && !string.IsNullOrWhiteSpace(order.SessionId);

    #endregion

    #region Fulfillment Ownership Helpers

    /// <summary>
    /// Determines fulfillment mode based on shipping method carrier code.
    /// 
    /// ARCHITECTURAL RULE:
    /// - If ShippingMethod has CarrierCode (not "manual") → Carrier mode
    /// - Otherwise → Manual mode
    /// 
    /// This is snapshotted at order creation and NEVER changes.
    /// </summary>
    private static FulfillmentMode DetermineFulfillmentMode(ShippingMethodDocument shippingMethod)
    {
        // Check if shipping method has a carrier code
        var carrierCode = shippingMethod.CarrierCode?.Trim().ToLowerInvariant();
        
        // If no carrier code or "manual", it's manual fulfillment
        if (string.IsNullOrEmpty(carrierCode) || carrierCode == "manual")
        {
            return FulfillmentMode.Manual;
        }
        
        // Otherwise, it's carrier-managed fulfillment
        return FulfillmentMode.Carrier;
    }

    /// <summary>
    /// Extracts carrier code from shipping method for carrier-managed orders.
    /// Returns null for manual fulfillment orders.
    /// 
    /// ARCHITECTURAL RULE:
    /// - Manual orders → NULL
    /// - Carrier orders → carrier code (e.g., "bosta", "aramex")
    /// </summary>
    private static string? ExtractCarrierCode(ShippingMethodDocument shippingMethod)
    {
        var carrierCode = shippingMethod.CarrierCode?.Trim().ToLowerInvariant();
        
        // Return null for manual or empty carrier codes
        if (string.IsNullOrEmpty(carrierCode) || carrierCode == "manual")
        {
            return null;
        }
        
        return carrierCode;
    }

    private sealed record AffiliateOrderLineAdmissionSnapshot(
        Guid ProductId,
        Guid PolicyId,
        decimal CommissionRate,
        decimal? MaxCommissionPerItem);

    #endregion
}
