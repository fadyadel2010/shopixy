using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using PointOfSale.Core.Discounts;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events.Affiliate;
using PointOfSale.Core.Events;
using PointOfSale.Core.Financial;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Core.Interfaces.Affiliate;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Core.Payments;
using PointOfSale.Core.Policies;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Observability;
using PointOfSale.Infrastructure.Reliability;
using PointOfSale.Infrastructure.Services.Affiliate;

namespace PointOfSale.Infrastructure.Payments;

/// <summary>
/// Canonical payment lifecycle coordinator.
///
/// CRITICAL RULES:
/// 1. All payment outcomes must flow through HandlePaymentResultAsync
/// 2. Inventory finalization happens in the same transaction as payment state mutation
/// 3. Domain events are staged into the outbox before database commit
/// 4. Replayed payment outcomes are idempotent
/// </summary>
public partial class PaymentLifecycleService : IPaymentLifecycleService
{
    private readonly ApplicationDbContext _db;
    private readonly IAccountingOutboxService _accountingOutbox;
    private readonly ICommerceAccountingService _commerceAccounting;
    private readonly IEventPublisher _eventPublisher;
    private readonly IMarketplaceEventOutboxService _marketplaceEventOutbox;
    private readonly ICorrelationIdAccessor _correlationIdAccessor;
    private readonly IProductVariantStockRepository _stockRepository;
    private readonly IInventoryReservationMetricsService _inventoryReservationMetrics;
    private readonly ICouponValidationService _couponValidationService;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<PaymentLifecycleService> _logger;
    private readonly CommerceReliabilityTelemetry? _reliabilityTelemetry;
    private readonly IsolationLevel _transactionIsolationLevel;
    private readonly bool _enableTransactionTelemetry;
    private readonly bool _requireRowLevelLocking;

    private const string SettlementTypeOfflineCollection = "OFFLINE_COLLECTION";
    private const string SettlementTypeGateway = "GATEWAY";
    private const string SettlementMismatchFailureCode = "SETTLEMENT_MISMATCH";
    private const string OfflineSettlementRejectedFailureCode = "OFFLINE_SETTLEMENT_REJECTED";

    public PaymentLifecycleService(
        ApplicationDbContext db,
        IAccountingOutboxService accountingOutbox,
        ICommerceAccountingService commerceAccounting,
        IEventPublisher eventPublisher,
        IMarketplaceEventOutboxService marketplaceEventOutbox,
        ICorrelationIdAccessor correlationIdAccessor,
        IProductVariantStockRepository stockRepository,
        IInventoryReservationMetricsService inventoryReservationMetrics,
        ICouponValidationService couponValidationService,
        ILogger<PaymentLifecycleService> logger,
        IOptions<PaymentLifecycleSettings> settings,
        CommerceReliabilityTelemetry? reliabilityTelemetry = null,
        IServiceScopeFactory? serviceScopeFactory = null)
    {
        _db = db;
        _accountingOutbox = accountingOutbox;
        _commerceAccounting = commerceAccounting;
        _eventPublisher = eventPublisher;
        _marketplaceEventOutbox = marketplaceEventOutbox;
        _correlationIdAccessor = correlationIdAccessor;
        _stockRepository = stockRepository;
        _inventoryReservationMetrics = inventoryReservationMetrics;
        _couponValidationService = couponValidationService;
        _serviceScopeFactory = serviceScopeFactory ?? NullServiceScopeFactory.Instance;
        _logger = logger;
        _reliabilityTelemetry = reliabilityTelemetry;
        _transactionIsolationLevel = settings.Value.TransactionIsolationLevel;
        _enableTransactionTelemetry = settings.Value.EnableTransactionTelemetry;
        _requireRowLevelLocking = settings.Value.RequireRowLevelLocking;
    }

    public Task<PaymentTransitionResult> HandlePaymentResultAsync(
        HandlePaymentResultRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.IgnoreQueryFilters &&
            (!request.TenantId.HasValue || request.TenantId.Value == Guid.Empty))
        {
            _logger.LogCritical(
                "[PAYMENT-LIFECYCLE] Rejected system mutation without explicit tenant authority | OrderId: {OrderId} | PaymentIntentId: {PaymentIntentId} | Source: {Source}",
                request.OrderId,
                request.PaymentIntentId,
                request.Source);

            return Task.FromResult(PaymentTransitionResult.Failed(
                "Tenant authority is required for system payment lifecycle mutations.",
                "PAYMENT_TENANT_AUTHORITY_REQUIRED"));
        }

        if (request.Source == PaymentLifecycleSource.GatewayWebhook &&
            !TryResolveGatewayPaymentProvider(request.Gateway, out _))
        {
            return Task.FromResult(PaymentTransitionResult.Failed(
                "Gateway webhook provider is not supported for payment lifecycle mutation.",
                "PAYMENT_GATEWAY_UNSUPPORTED"));
        }

        return request.Outcome switch
        {
            PaymentLifecycleOutcome.Authorized => HandleAuthorizedAsync(request, ct),
            PaymentLifecycleOutcome.Succeeded => HandleSuccessAsync(request, ct),
            PaymentLifecycleOutcome.Failed or PaymentLifecycleOutcome.Cancelled or PaymentLifecycleOutcome.Rejected or PaymentLifecycleOutcome.Expired
                => HandleFailureAsync(request, ct),
            PaymentLifecycleOutcome.AmountMismatch => Task.FromResult(PaymentTransitionResult.Failed(
                "AmountMismatch is a system-only outcome and cannot be passed by external callers. " +
                "Amount mismatches are automatically detected and quarantined by the payment lifecycle service.")),
            _ => Task.FromResult(PaymentTransitionResult.Failed(
                $"Unsupported payment outcome '{request.Outcome}'."))
        };
    }

    public Task<PaymentIntent> CreateReviewablePaymentIntentAsync(
        ReviewablePaymentIntentCreateRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Id == Guid.Empty ||
            request.TenantId == Guid.Empty ||
            request.OrderId == Guid.Empty ||
            request.Amount <= 0m ||
            string.IsNullOrWhiteSpace(request.Currency) ||
            string.IsNullOrWhiteSpace(request.PaymentReference) ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            string.IsNullOrWhiteSpace(request.ProviderCode))
        {
            throw new InvalidOperationException("Reviewable payment intent creation request is invalid.");
        }

        var provider = ResolvePaymentProviderCode(request.ProviderCode);

        var createdAtUtc = request.CreatedAtUtc == default ? DateTime.UtcNow : request.CreatedAtUtc;
        if (!string.IsNullOrWhiteSpace(request.ProofImageUrl) &&
            !IsValidManualWalletProofRetention(createdAtUtc, request.ProofImageRetentionExpiresAtUtc))
        {
            throw new InvalidOperationException("Payment proof retention policy is missing or invalid.");
        }

        var paymentIntent = new PaymentIntent
        {
            Id = request.Id,
            OrderId = request.OrderId,
            TenantId = request.TenantId,
            Amount = request.Amount,
            Currency = request.Currency,
            PaymentMethod = request.PaymentMethod,
            Provider = provider,
            PaymentReference = request.PaymentReference,
            IdempotencyKey = request.IdempotencyKey,
            Status = PaymentIntentStatus.Pending,
            CreatedAt = createdAtUtc,
            WalletType = request.WalletType,
            WalletPhoneNumber = request.WalletPhoneNumber,
            ProofImageUrl = request.ProofImageUrl,
            ProofImageExpiresAtUtc = request.ProofImageRetentionExpiresAtUtc,
            ProviderMetadata = request.ProviderMetadata
        };

        _db.PaymentIntents.Add(paymentIntent);
        return Task.FromResult(paymentIntent);
    }

    private static PaymentProvider ResolvePaymentProviderCode(string providerCode)
    {
        if (!Enum.TryParse<PaymentProvider>(providerCode.Trim(), ignoreCase: true, out var provider))
        {
            throw new InvalidOperationException("Payment provider code is invalid.");
        }

        return provider;
    }

    public async Task<PaymentTransitionResult> AdmitPaymentReviewAsync(
        PaymentReviewAdmissionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.TenantId == Guid.Empty ||
            request.OrderId == Guid.Empty ||
            request.PaymentIntentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.ReferenceCode) ||
            request.ExpiresAtUtc == default ||
            request.ProofImageRetentionExpiresAtUtc == default)
        {
            return PaymentTransitionResult.Failed(
                "Payment review admission request is invalid.",
                "PAYMENT_REVIEW_ADMISSION_INVALID");
        }

        var order = await _db.Orders
            .FirstOrDefaultAsync(entity =>
                entity.Id == request.OrderId &&
                entity.TenantId == request.TenantId,
                ct);

        if (order is null)
        {
            return PaymentTransitionResult.Failed("Order not found", "ORDER_NOT_FOUND");
        }

        var paymentIntent = await _db.PaymentIntents
            .FirstOrDefaultAsync(entity =>
                entity.Id == request.PaymentIntentId &&
                entity.OrderId == request.OrderId &&
                entity.TenantId == request.TenantId,
                ct);

        if (paymentIntent is null)
        {
            return PaymentTransitionResult.Failed("Reviewable payment intent not found", "PAYMENT_INTENT_NOT_FOUND");
        }

        var observedAtUtc = DateTime.UtcNow;
        if (request.ExpiresAtUtc <= observedAtUtc ||
            (paymentIntent.CreatedAt != default && request.ExpiresAtUtc <= paymentIntent.CreatedAt))
        {
            return PaymentTransitionResult.Failed(
                "Payment review expiration authority is invalid.",
                "PAYMENT_REVIEW_EXPIRATION_INVALID");
        }

        if (!string.IsNullOrWhiteSpace(paymentIntent.ProofImageUrl) &&
            !IsValidManualWalletProofRetention(paymentIntent.CreatedAt, request.ProofImageRetentionExpiresAtUtc))
        {
            return PaymentTransitionResult.Failed(
                "Payment proof retention authority is invalid.",
                "PAYMENT_REVIEW_RETENTION_INVALID");
        }

        if (paymentIntent.Status == PaymentIntentStatus.AwaitingVerification &&
            order.PaymentStatus == PaymentStatus.AwaitingVerification)
        {
            return PaymentTransitionResult.Succeeded(
                PaymentStatus.AwaitingVerification,
                wasIdempotent: true,
                stateChanged: false);
        }

        if (paymentIntent.Status != PaymentIntentStatus.Pending ||
            order.PaymentStatus != PaymentStatus.Pending)
        {
            return PaymentTransitionResult.Failed(
                $"Cannot admit Manual Wallet review from Order:{order.PaymentStatus}/Intent:{paymentIntent.Status}",
                "PAYMENT_REVIEW_INVALID_STATE");
        }

        paymentIntent.Status = PaymentIntentStatus.AwaitingVerification;
        paymentIntent.ReferenceCode = request.ReferenceCode;
        paymentIntent.ExpiresAtUtc = request.ExpiresAtUtc;
        paymentIntent.ProviderIntentId = request.ProviderIntentId;
        if (!string.IsNullOrWhiteSpace(paymentIntent.ProofImageUrl))
        {
            paymentIntent.ProofImageExpiresAtUtc = request.ProofImageRetentionExpiresAtUtc;
            paymentIntent.ProofImageDeleted = false;
        }

        PaymentStatusSaveChangesInterceptor.EnableAuthorizedContext();
        try
        {
            order.PaymentStatus = PaymentStatus.AwaitingVerification;
            order.AcceptPaymentStatusChange();
            order.PaymentGateway = PaymentProvider.ManualWallet.ToString();
            order.PaymentReference = paymentIntent.PaymentReference;

            await _db.SaveChangesAsync(ct);
        }
        finally
        {
            PaymentStatusSaveChangesInterceptor.DisableAuthorizedContext();
        }

        return PaymentTransitionResult.Succeeded(PaymentStatus.AwaitingVerification);
    }

    public Task<PaymentTransitionResult> MarkOfflineCollectionDeliveredAsync(
        Guid tenantId,
        Guid orderId,
        string? deliveryReference = null,
        string? confirmedBy = null,
        CancellationToken ct = default)
    {
        return HandlePaymentResultAsync(new HandlePaymentResultRequest
        {
            TenantId = tenantId,
            OrderId = orderId,
            Outcome = PaymentLifecycleOutcome.Succeeded,
            Source = PaymentLifecycleSource.OfflineCollection,
            PaymentReference = deliveryReference,
            Actor = confirmedBy,
            IgnoreQueryFilters = true
        }, ct);
    }

    public Task<PaymentTransitionResult> RecordCarrierCodCollectionAsync(
        Guid tenantId,
        Guid orderId,
        Guid shipmentId,
        CancellationToken ct = default)
        => Task.FromResult(PaymentTransitionResult.Failed("Immutable observed-money evidence locator is required.", "COD_CARRIER_EVIDENCE_REQUIRED"));

    public Task<PaymentTransitionResult> RecordCarrierCodCollectionAsync(
        Guid tenantId, Guid orderId, Guid shipmentId, Guid evidenceId, CancellationToken ct = default)
    {
        if (shipmentId == Guid.Empty || evidenceId == Guid.Empty || tenantId == Guid.Empty)
            return Task.FromResult(PaymentTransitionResult.Failed(
                "Carrier COD collection requires a shipment authority locator.", "COD_CARRIER_EVIDENCE_REQUIRED"));

        return HandlePaymentResultAsync(new HandlePaymentResultRequest
        {
            TenantId = tenantId,
            OrderId = orderId,
            CarrierCollectionShipmentId = shipmentId,
            CarrierCollectionEvidenceId = evidenceId,
            Outcome = PaymentLifecycleOutcome.Succeeded,
            Source = PaymentLifecycleSource.OfflineCollection,
            IgnoreQueryFilters = true
        }, ct);
    }

    private async Task<PaymentTransitionResult?> ValidateCodCollectionAdmissionAsync(
        Order order, HandlePaymentResultRequest request, CancellationToken ct)
    {
        if (request.CarrierCollectionEvidenceId.HasValue && !request.CarrierCollectionShipmentId.HasValue)
            return PaymentTransitionResult.Failed("Carrier evidence requires its exact shipment locator.", "COD_CARRIER_EVIDENCE_REQUIRED");
        if (!request.CarrierCollectionShipmentId.HasValue && (order.IsDeleted || !IsCodOrder(order)))
            return PaymentTransitionResult.Failed("Order is not a COD order", "COD_ORDER_REQUIRED");

        if (!request.CarrierCollectionShipmentId.HasValue)
        {
            if (order.FulfillmentMode != FulfillmentMode.Manual ||
                !string.IsNullOrWhiteSpace(order.FulfillmentCarrierCode))
                return PaymentTransitionResult.Failed(
                    "Operator COD collection requires Manual Fulfillment.", "COD_MANUAL_FULFILLMENT_REQUIRED");
        }
        else
        {
            if (!request.CarrierCollectionEvidenceId.HasValue || request.CarrierCollectionEvidenceId == Guid.Empty)
                return PaymentTransitionResult.Failed(
                    "Immutable observed-money evidence is required.", "COD_CARRIER_EVIDENCE_REQUIRED");
            var (evidence, code) = await PointOfSale.Infrastructure.Shipping.CarrierCollectionMoneyAuthority.RevalidateAsync(
                _db, order, request.CarrierCollectionEvidenceId, request.CarrierCollectionShipmentId, ct);
            if (code != null)
            {
                if (evidence == null) return PaymentTransitionResult.Failed("Carrier money evidence was not found.", code);
                using var moneyAuthority = PointOfSale.Infrastructure.Shipping.CarrierMoneyPersistenceAuthority.Begin();
                await PointOfSale.Infrastructure.Shipping.CarrierCollectionMoneyAuthority.StageReviewAsync(_db, evidence, code, ct);
                await _db.SaveChangesAsync(ct);
                return PaymentTransitionResult.ManualReviewRequired(order.PaymentStatus, "Carrier collection evidence requires reconciliation.", code);
            }
            request.PaymentReference = evidence!.TrackingNumber;
            request.Actor = evidence.CarrierCode;
            request.ProcessedAtUtc = evidence.ProviderEventAtUtc;
            request.Amount = evidence.ObservedAmount;
            request.Currency = evidence.Currency;
        }

        if (!IsOrderSuccessfulTerminal(order) && order.FulfillmentStatus != FulfillmentStatus.Delivered)
            return PaymentTransitionResult.Failed(
                $"Cannot collect COD: order not delivered (current fulfillment status: {order.FulfillmentStatus})",
                "COD_DELIVERY_REQUIRED");

        // Neither manual request fields nor carrier event fields may choose a financial amount.
        request.Amount = order.Total;
        request.Currency = order.Currency;
        request.ProviderAmount = null;
        request.ProviderCurrency = null;
        return null;
    }

    public async Task<PaymentTransitionResult> RecordOfflineSettlementAsync(
        OfflineSettlementRequest request,
        CancellationToken ct = default)
    {
        await using var transaction = await BeginLifecycleTransactionAsync(ct);
        Guid tenantId = Guid.Empty;
        decimal actualTotal = 0m;
        Guid settlementBatchId = Guid.Empty;
        string? settlementId = null; // Capture before try for safe catch access

        try
        {
            tenantId = request.TenantId;
            settlementId = request.SettlementId; // Capture early for exception handling
            var normalizedCurrency = NormalizeCurrency(request.Currency);
            var requestedOrderIds = request.OrderIds
                .Where(orderId => orderId != Guid.Empty)
                .Distinct()
                .ToList();

            if (tenantId == Guid.Empty)
            {
                return RejectOfflineSettlement(tenantId, "Tenant context is required for offline settlement.", "missing_tenant");
            }

            if (string.IsNullOrWhiteSpace(request.SettlementId))
            {
                return RejectOfflineSettlement(tenantId, "Settlement ID is required.", "missing_settlement_id");
            }

            if (requestedOrderIds.Count == 0 || requestedOrderIds.Count != request.OrderIds.Count)
            {
                return RejectOfflineSettlement(tenantId, "Offline settlement requires a non-empty, duplicate-free order set.", "invalid_order_set");
            }

            if (string.IsNullOrWhiteSpace(normalizedCurrency))
            {
                return RejectOfflineSettlement(tenantId, "Currency is required for offline settlement.", "missing_currency");
            }

            var codAmountValidationFailure = ValidateSettlementAmountBounds("Offline", request.GrossAmount, request.Fees);
            if (codAmountValidationFailure is not null)
            {
                return RejectOfflineSettlement(
                    tenantId,
                    codAmountValidationFailure.Value.Message,
                    codAmountValidationFailure.Value.ErrorCode);
            }

            var orders = await LoadOrdersForSettlementMutationAsync(
                tenantId,
                requestedOrderIds,
                ct);

            if (orders.Count != requestedOrderIds.Count)
            {
                return RejectOfflineSettlement(tenantId, "Offline settlement rejected: one or more orders were not found.", "order_not_found");
            }

            if (orders.Any(order => order.TenantId != tenantId))
            {
                return RejectOfflineSettlement(tenantId, "Offline settlement rejected: order tenant mismatch.", "tenant_mismatch");
            }

            if (orders.Any(order => !CurrencyEquals(order.Currency, normalizedCurrency)))
            {
                return RejectOfflineSettlement(tenantId, "Offline settlement rejected: currency mismatch.", "currency_mismatch");
            }

            actualTotal = orders.Sum(order => order.Total);
            
            // Validate gross amount matches actual order total (mirrors RecordGatewaySettlementAsync pattern)
            if (actualTotal != request.GrossAmount)
            {
                _logger.LogWarning(
                    "COD settlement rejected: gross amount mismatch | " +
                    "SettlementId: {SettlementId} | RequestedGrossAmount: {RequestedGrossAmount} | " +
                    "ActualOrderTotal: {ActualOrderTotal} | TenantId: {TenantId}",
                    request.SettlementId,
                    request.GrossAmount,
                    actualTotal,
                    tenantId);
                
                return RejectOfflineSettlement(tenantId, "Offline settlement rejected: gross amount does not match resolved order total.", "gross_amount_mismatch");
            }
            
            var occurredAtUtc = DateTime.UtcNow;

            var existingBatch = await LoadSettlementBatchAsync(
                tenantId,
                SettlementTypeOfflineCollection,
                request.SettlementId,
                ct);

            if (existingBatch != null)
            {
                if (SettlementBatchMatches(
                    existingBatch,
                    SettlementTypeOfflineCollection,
                    request.SettlementId,
                    request.ShippingCompany,
                    payoutBatchId: null,
                    actualTotal,
                    request.Fees,
                    normalizedCurrency,
                    requestedOrderIds))
                {
                    return PaymentTransitionResult.Succeeded(
                        PaymentStatus.Settled,
                        wasIdempotent: true,
                        stateChanged: false);
                }

                return RejectOfflineSettlement(tenantId, "Offline settlement rejected: settlement ID already exists with different contents.", "duplicate_settlement_mismatch");
            }

            if (orders.Any(order => order.PaymentStatus != PaymentStatus.Collected))
            {
                return RejectOfflineSettlement(tenantId, "Offline settlement rejected: all orders must be in Collected state.", "non_collected_order");
            }

            var accountingReadinessFailure = await EnsureCollectionAccountingPostedForCodSettlementAsync(
                tenantId,
                orders,
                ct);

            if (accountingReadinessFailure != null)
            {
                return accountingReadinessFailure;
            }

            PaymentSettlementBatch settlementBatch;
            
            PaymentStatusSaveChangesInterceptor.EnableAuthorizedContext();
            try
            {
                settlementBatch = CreateSettlementBatch(
                    tenantId,
                    SettlementTypeOfflineCollection,
                    request.SettlementId,
                    request.ShippingCompany,
                    payoutBatchId: null,
                    actualTotal,
                    request.Fees,
                    normalizedCurrency,
                    request.BankReference,
                    orders,
                    paymentIntentIdsByOrder: null,
                    occurredAtUtc);

                _db.PaymentSettlementBatches.Add(settlementBatch);
                settlementBatchId = settlementBatch.Id; // Capture for post-commit use

                foreach (var order in orders)
                {
                    var oldPaymentStatus = order.PaymentStatus;
                    order.PaymentStatus = PaymentStatus.Settled;
                    order.AcceptPaymentStatusChange();

                    await PublishSettlementPaymentStatusChangedEventAsync(
                        order,
                        oldPaymentStatus,
                        request.SettlementId,
                        occurredAtUtc,
                        ct);
                }

                await _accountingOutbox.EnqueueCodSettlementAsync(
                    tenantId, settlementBatch.Id, actualTotal, request.Fees, request.SettlementId, ct);

                await _eventPublisher.PublishAsync(new OfflineSettlementCompletedEvent
                {
                    TenantId = tenantId,
                    SettlementBatchId = settlementBatch.Id,
                    SettlementId = request.SettlementId,
                    GrossAmount = actualTotal,
                    Fees = request.Fees,
                    Currency = normalizedCurrency,
                    ShippingCompany = request.ShippingCompany,
                    BankAccount = request.BankReference,
                    OrderIds = requestedOrderIds,
                    SettlementDate = occurredAtUtc,
                    CorrelationId = _correlationIdAccessor.GetCorrelationId(),
                    IdempotencyKey = BuildCodSettlementIdempotencyKey(tenantId, request.SettlementId),
                    OccurredAtUtc = occurredAtUtc
                }, ct);

                await _db.SaveChangesAsync(ct);
            }
            finally
            {
                PaymentStatusSaveChangesInterceptor.DisableAuthorizedContext();
            }

            await transaction.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            // Log the original exception first
            _logger.LogError(ex, "Failed to record COD settlement {SettlementId}", settlementId ?? "unknown");
            
            // Attempt rollback, but don't let rollback exceptions mask the original error
            try
            {
                await transaction.RollbackAsync(ct);
            }
            catch (Exception rollbackEx)
            {
                _logger.LogError(rollbackEx, "Rollback failed for COD settlement {SettlementId}", settlementId ?? "unknown");
            }
            
            return PaymentTransitionResult.Failed($"Internal error: {ex.Message}");
        }

        // Post to external accounting system AFTER local state is durable and committed.
        // This is best-effort: failures here are logged but do not affect the settled result.
        // The outbox row remains unmodified for retry if posting fails.
        try
        {
            await PostCommittedCodSettlementAccountingFromSqlTruthAsync(
                tenantId,
                settlementBatchId,
                BuildCodSettlementIdempotencyKey(tenantId, request.SettlementId),
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Post-commit accounting operation failed for COD settlement {SettlementId}. " +
                "Settlement is committed; outbox will retry.", 
                request.SettlementId);
        }

        return PaymentTransitionResult.Succeeded(PaymentStatus.Settled);
    }

    public Task<PaymentTransitionResult> CaptureOnlinePaymentAsync(
        Guid orderId,
        string gateway,
        string paymentIntentId,
        decimal providerAmount,
        string providerCurrency,
        CancellationToken ct = default)
    {
        return HandlePaymentResultAsync(new HandlePaymentResultRequest
        {
            OrderId = orderId,
            Outcome = PaymentLifecycleOutcome.Succeeded,
            Source = PaymentLifecycleSource.GatewayWebhook,
            Gateway = gateway,
            PaymentReference = paymentIntentId,
            ProviderAmount = providerAmount,
            ProviderCurrency = providerCurrency
        }, ct);
    }

    public async Task<PaymentTransitionResult> RecordGatewaySettlementAsync(
        GatewaySettlementRequest request,
        CancellationToken ct = default)
    {
        await using var transaction = await BeginLifecycleTransactionAsync(ct);
        Guid tenantId = Guid.Empty;
        Guid settlementBatchId = Guid.Empty;
        string? settlementId = null; // Capture before try for safe catch access

        try
        {
            tenantId = request.TenantId;
            settlementId = request.SettlementId; // Capture early for exception handling
            var normalizedCurrency = NormalizeCurrency(request.Currency);

            if (tenantId == Guid.Empty)
            {
                return RejectGatewaySettlement(tenantId, "Tenant context is required for gateway settlement.", "missing_tenant");
            }

            if (string.IsNullOrWhiteSpace(request.SettlementId))
            {
                return RejectGatewaySettlement(tenantId, "Settlement ID is required.", "missing_settlement_id");
            }

            if (string.IsNullOrWhiteSpace(request.Gateway))
            {
                return RejectGatewaySettlement(tenantId, "Gateway is required for settlement.", "missing_gateway");
            }

            // T-09: Fawry has no certified clearing or settlement authority.
            // Fail before selector resolution or any durable mutation.
            if (FawryFinancialAuthorityPolicy.IsFawry(request.Gateway))
            {
                _logger.LogWarning(
                    "Gateway settlement rejected: Fawry financial authority unavailable | SettlementId: {SettlementId} | TenantId: {TenantId}",
                    request.SettlementId,
                    tenantId);
                return PaymentTransitionResult.Failed(
                    FawryFinancialAuthorityPolicy.Message,
                    FawryFinancialAuthorityPolicy.Code);
            }

            if (!IsSupportedGatewaySettlementProvider(request.Gateway))
            {
                return RejectGatewaySettlement(
                    tenantId,
                    $"Gateway settlement is not supported for provider '{request.Gateway.Trim()}'.",
                    "unsupported_gateway");
            }

            request.Gateway = request.Gateway.Trim();

            if (string.IsNullOrWhiteSpace(normalizedCurrency))
            {
                return RejectGatewaySettlement(tenantId, "Currency is required for gateway settlement.", "missing_currency");
            }

            var gatewayAmountValidationFailure = ValidateSettlementAmountBounds("Gateway", request.GrossAmount, request.Fees);
            if (gatewayAmountValidationFailure is not null)
            {
                return RejectGatewaySettlement(
                    tenantId,
                    gatewayAmountValidationFailure.Value.Message,
                    gatewayAmountValidationFailure.Value.ErrorCode);
            }

            var resolution = await ResolveGatewaySettlementOrdersAsync(request, ct);
            if (resolution.Failure != null)
            {
                return resolution.Failure;
            }

            var orders = resolution.Orders;
            var requestedOrderIds = orders.Select(order => order.Id).Distinct().ToList();

            orders = await LoadOrdersForSettlementMutationAsync(
                tenantId,
                requestedOrderIds,
                ct);

            if (orders.Count == 0)
            {
                return RejectGatewaySettlement(tenantId, "Gateway settlement rejected: no matching collected orders were resolved.", "no_orders_resolved");
            }

            if (resolution.RequestedOrderCount.HasValue &&
                requestedOrderIds.Count != resolution.RequestedOrderCount.Value)
            {
                return RejectGatewaySettlement(tenantId, "Gateway settlement rejected: resolved order count does not match requested identifiers.", "resolved_count_mismatch");
            }

            if (orders.Any(order => order.TenantId != tenantId))
            {
                return RejectGatewaySettlement(tenantId, "Gateway settlement rejected: order tenant mismatch.", "tenant_mismatch");
            }

            if (orders.Any(IsFawryOrder))
            {
                return PaymentTransitionResult.Failed(
                    FawryFinancialAuthorityPolicy.Message,
                    FawryFinancialAuthorityPolicy.Code);
            }

            var paymentIntentAuthorityFailure = await ValidateGatewaySettlementPaymentIntentAuthorityAsync(
                tenantId,
                orders,
                resolution.PaymentIntentIdsByOrder,
                request.Gateway,
                ct);
            if (paymentIntentAuthorityFailure != null)
            {
                return paymentIntentAuthorityFailure;
            }

            if (orders.Any(order => !CurrencyEquals(order.Currency, normalizedCurrency)))
            {
                return RejectGatewaySettlement(tenantId, "Gateway settlement rejected: currency mismatch.", "currency_mismatch");
            }

            if (orders.Any(order => !GatewayMatches(order, request.Gateway)))
            {
                return RejectGatewaySettlement(tenantId, "Gateway settlement rejected: gateway mismatch.", "gateway_mismatch");
            }

            var actualTotal = orders.Sum(order => order.Total);
            if (actualTotal != request.GrossAmount)
            {
                return RejectGatewaySettlement(tenantId, "Gateway settlement rejected: gross amount does not match resolved order total.", "gross_amount_mismatch");
            }

            var existingBatch = await LoadSettlementBatchAsync(
                tenantId,
                SettlementTypeGateway,
                request.SettlementId,
                ct);

            if (existingBatch != null)
            {
                if (SettlementBatchMatches(
                    existingBatch,
                    SettlementTypeGateway,
                    request.SettlementId,
                    request.Gateway,
                    request.PayoutBatchId,
                    request.GrossAmount,
                    request.Fees,
                    normalizedCurrency,
                    requestedOrderIds))
                {
                    return PaymentTransitionResult.Succeeded(
                        PaymentStatus.Settled,
                        wasIdempotent: true,
                        stateChanged: false);
                }

                return RejectGatewaySettlement(tenantId, "Gateway settlement rejected: settlement ID already exists with different contents.", "duplicate_settlement_mismatch");
            }

            if (orders.Any(order => order.PaymentStatus != PaymentStatus.Collected))
            {
                return RejectGatewaySettlement(tenantId, "Gateway settlement rejected: all orders must be in Collected state.", "non_collected_order");
            }

            var accountingReadinessFailure = await EnsureCollectionAccountingPostedForGatewaySettlementAsync(
                tenantId,
                orders,
                resolution.PaymentIntentIdsByOrder,
                ct);

            if (accountingReadinessFailure != null)
            {
                return accountingReadinessFailure;
            }

            PaymentSettlementBatch settlementBatch;
            
            PaymentStatusSaveChangesInterceptor.EnableAuthorizedContext();
            try
            {
                var occurredAtUtc = DateTime.UtcNow;
                settlementBatch = CreateSettlementBatch(
                    tenantId,
                    SettlementTypeGateway,
                    request.SettlementId,
                    request.Gateway,
                    request.PayoutBatchId,
                    request.GrossAmount,
                    request.Fees,
                    normalizedCurrency,
                    request.BankReference,
                    orders,
                    resolution.PaymentIntentIdsByOrder,
                    occurredAtUtc);

                _db.PaymentSettlementBatches.Add(settlementBatch);
                settlementBatchId = settlementBatch.Id; // Capture for post-commit use

                foreach (var order in orders)
                {
                    var oldPaymentStatus = order.PaymentStatus;
                    order.PaymentStatus = PaymentStatus.Settled;
                    order.AcceptPaymentStatusChange();

                    await PublishSettlementPaymentStatusChangedEventAsync(
                        order,
                        oldPaymentStatus,
                        request.SettlementId,
                        occurredAtUtc,
                        ct);
                }

                await _accountingOutbox.EnqueueGatewaySettlementAsync(
                    tenantId,
                    settlementBatch.Id,
                    request.Gateway,
                    request.GrossAmount,
                    request.Fees,
                    request.SettlementId,
                    ct);

                await _eventPublisher.PublishAsync(new GatewaySettlementCompletedEvent
                {
                    TenantId = tenantId,
                    SettlementBatchId = settlementBatch.Id,
                    SettlementId = request.SettlementId,
                    Gateway = request.Gateway,
                    PayoutBatchId = request.PayoutBatchId,
                    OrderIds = requestedOrderIds,
                    PaymentIntentIds = resolution.PaymentIntentIdsByOrder.Values
                        .Where(paymentIntentId => paymentIntentId.HasValue)
                        .Select(paymentIntentId => paymentIntentId!.Value)
                        .Distinct()
                        .ToList(),
                    GrossAmount = request.GrossAmount,
                    Fees = request.Fees,
                    Currency = normalizedCurrency,
                    BankReference = request.BankReference,
                    SettlementDate = occurredAtUtc,
                    CorrelationId = _correlationIdAccessor.GetCorrelationId(),
                    IdempotencyKey = BuildGatewaySettlementIdempotencyKey(tenantId, request.SettlementId),
                    OccurredAtUtc = occurredAtUtc
                }, ct);

                await _db.SaveChangesAsync(ct);
            }
            finally
            {
                PaymentStatusSaveChangesInterceptor.DisableAuthorizedContext();
            }

            await transaction.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            // Log the original exception first
            _logger.LogError(ex, "Failed to record gateway settlement {SettlementId}", settlementId ?? "unknown");
            
            // Attempt rollback, but don't let rollback exceptions mask the original error
            try
            {
                await transaction.RollbackAsync(ct);
            }
            catch (Exception rollbackEx)
            {
                _logger.LogError(rollbackEx, "Rollback failed for gateway settlement {SettlementId}", settlementId ?? "unknown");
            }
            
            return PaymentTransitionResult.Failed($"Internal error: {ex.Message}");
        }

        // Post to external accounting system AFTER local state is durable and committed.
        // This is best-effort: failures here are logged but do not affect the settled result.
        // The outbox row remains unmodified for retry if posting fails.
        try
        {
            await PostCommittedGatewaySettlementAccountingFromSqlTruthAsync(
                tenantId,
                settlementBatchId,
                BuildGatewaySettlementIdempotencyKey(tenantId, request.SettlementId),
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Post-commit accounting operation failed for gateway settlement {SettlementId}. " +
                "Settlement is committed; outbox will retry.", 
                request.SettlementId);
        }

        return PaymentTransitionResult.Succeeded(PaymentStatus.Settled);
    }

    public Task<PaymentTransitionResult> ApproveReviewedPaymentAsync(
        Guid orderId,
        string approvedBy,
        string? transactionReference = null,
        CancellationToken ct = default)
    {
        return HandlePaymentResultAsync(new HandlePaymentResultRequest
        {
            OrderId = orderId,
            Outcome = PaymentLifecycleOutcome.Succeeded,
            Source = PaymentLifecycleSource.PaymentReviewApproval,
            PaymentReference = transactionReference,
            ProviderPaymentId = transactionReference,
            Actor = approvedBy
        }, ct);
    }

    public Task<PaymentTransitionResult> RejectReviewedPaymentAsync(
        Guid orderId,
        string rejectedBy,
        string reason,
        CancellationToken ct = default)
    {
        return HandlePaymentResultAsync(new HandlePaymentResultRequest
        {
            OrderId = orderId,
            Outcome = PaymentLifecycleOutcome.Rejected,
            Source = PaymentLifecycleSource.PaymentReviewRejection,
            Reason = reason,
            Actor = rejectedBy
        }, ct);
    }

    public async Task<PaymentTransitionResult> RecordRefundAsync(
        Guid orderId,
        decimal refundAmount,
        string refundId,
        string reason,
        CancellationToken ct = default)
    {
        await using var transaction = await BeginLifecycleTransactionAsync(ct);

        // Declare variables outside try block for post-commit use
        Order? order = null;
        string? normalizedRefundId = null;
        decimal normalizedRefundAmount = 0m;
        EcommerceRefundAccountingContext? refundAccountingContext = null;
        OrderRefund? refund = null;
        PaymentStatus newStatus = PaymentStatus.PartiallyRefunded;

        try
        {
            normalizedRefundId = NormalizeRefundId(refundId);
            if (normalizedRefundId == null)
            {
                return PaymentTransitionResult.Failed("Refund ID is required", "REFUND_ID_REQUIRED");
            }

            order = await LoadOrderForRefundMutationAsync(orderId, ct);

            if (order == null)
            {
                return PaymentTransitionResult.Failed("Order not found", "ORDER_NOT_FOUND");
            }

            // Normalize amount and currency for validation
            normalizedRefundAmount = NormalizeMoney(refundAmount, order.Currency);
            var normalizedCurrency = NormalizeCurrency(order.Currency) ?? order.Currency;

            // Check for existing refund using both new hash-based and legacy truncated forms
            // Pass amount and currency to validate stable invariants for legacy matches
            var existingRefund = FindExistingRefund(order, refundId, normalizedRefundAmount, normalizedCurrency);

            if (existingRefund != null)
            {
                await transaction.CommitAsync(ct);
                return PaymentTransitionResult.Succeeded(
                    order.PaymentStatus,
                    wasIdempotent: true,
                    stateChanged: false);
            }

            // T-09: no Fawry clearing or post-settlement lineage is certified, so an
            // application refund for a Fawry order fails before any refund mutation.
            if (IsFawryOrder(order) || await HasLatestPaidFawryPaymentIntentAsync(order, ct))
            {
                _logger.LogWarning(
                    "Refund rejected: Fawry financial authority unavailable | OrderId: {OrderId} | TenantId: {TenantId}",
                    order.Id,
                    order.TenantId);
                return PaymentTransitionResult.Failed(
                    FawryFinancialAuthorityPolicy.Message,
                    FawryFinancialAuthorityPolicy.Code);
            }

            if (order.PaymentStatus != PaymentStatus.Collected &&
                order.PaymentStatus != PaymentStatus.Settled &&
                order.PaymentStatus != PaymentStatus.PartiallyRefunded)
            {
                return PaymentTransitionResult.Failed(
                    $"Cannot refund: current status is {order.PaymentStatus}",
                    "REFUND_INVALID_PAYMENT_STATE");
            }

            var collectionAccountingFailure = await EnsureCollectionAccountingPostedForRefundAsync(order, ct);
            if (collectionAccountingFailure != null)
            {
                return collectionAccountingFailure;
            }

            var refundAccountingContextResolution = await ResolveRefundAccountingContextAsync(order, ct);
            if (refundAccountingContextResolution.ErrorMessage != null)
            {
                return PaymentTransitionResult.Failed(refundAccountingContextResolution.ErrorMessage);
            }

            refundAccountingContext = refundAccountingContextResolution.Context
                ?? EcommerceRefundAccountingContext.PreSettlement;

            // Amount already normalized above for validation
            if (normalizedRefundAmount <= 0m)
            {
                return PaymentTransitionResult.Failed("Refund amount must be positive", "REFUND_AMOUNT_INVALID");
            }

            var tolerance = CurrencyHelper.GetMonetaryTolerance(order.Currency);
            var refundableAmount = ResolveRefundableAmount(order);
            var alreadyRefundedAmount = ResolveAlreadyRefundedAmount(order);
            var remainingRefundableAmount = NormalizeMoney(refundableAmount - alreadyRefundedAmount, order.Currency);

            if (remainingRefundableAmount <= tolerance)
            {
                return PaymentTransitionResult.Failed(
                    "Order has no remaining refundable amount",
                    "REFUND_AMOUNT_EXHAUSTED");
            }

            if (normalizedRefundAmount - remainingRefundableAmount > tolerance)
            {
                return PaymentTransitionResult.Failed(
                    $"Refund amount {normalizedRefundAmount} exceeds remaining refundable amount {remainingRefundableAmount}",
                    "REFUND_AMOUNT_EXCEEDS_REMAINING");
            }

            var totalRefundedAfterThisRefund = NormalizeMoney(alreadyRefundedAmount + normalizedRefundAmount, order.Currency);
            var isFullRefund = totalRefundedAfterThisRefund + tolerance >= refundableAmount;
            newStatus = isFullRefund ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
            var refundedAtUtc = DateTime.UtcNow;
            var paymentMethod = order.PaymentGateway ?? order.PaymentMethodCode ?? string.Empty;

            refund = new OrderRefund
            {
                Id = Guid.NewGuid(),
                TenantId = order.TenantId,
                OrderId = order.Id,
                RefundId = normalizedRefundId,
                Amount = normalizedRefundAmount,
                Currency = NormalizeCurrency(order.Currency) ?? order.Currency,
                IsFullRefund = isFullRefund,
                Reason = reason,
                PaymentMethod = paymentMethod,
                IsPostSettlementRefund = refundAccountingContext.IsPostSettlementRefund,
                SettlementBatchId = refundAccountingContext.SettlementBatchId,
                SettlementType = refundAccountingContext.SettlementType,
                SettlementId = refundAccountingContext.SettlementId,
                PayoutBatchId = refundAccountingContext.PayoutBatchId,
                SettledAtUtc = refundAccountingContext.SettledAtUtc,
                FinancialPostingStatus = FinancialPostingStatus.Pending,
                FinancialPostingPendingAtUtc = refundedAtUtc,
                RefundedAtUtc = refundedAtUtc
            };

            AllocateRefundAcrossItems(order, refund, normalizedRefundAmount);

            PaymentStatusSaveChangesInterceptor.EnableAuthorizedContext();
            try
            {
                var oldPaymentStatus = order.PaymentStatus;
                order.PaymentStatus = newStatus;
                order.AcceptPaymentStatusChange();
                _db.OrderRefunds.Add(refund);

                if (refundAccountingContext.IsPostSettlementRefund)
                {
                    await _accountingOutbox.EnqueueSettledEcommerceRefundAsync(
                        order.TenantId,
                        orderId,
                        normalizedRefundAmount,
                        normalizedRefundId,
                        refundAccountingContext,
                        ct);
                }
                else
                {
                    await _accountingOutbox.EnqueueEcommerceRefundAsync(
                        order.TenantId, orderId, normalizedRefundAmount, normalizedRefundId, ct);
                }

                // T-08: link the financial refund to physical stock and COGS reversal
                // atomically in this same transaction:
                // - pre-commit refund: release open reservations (no COGS exists yet),
                //   no COGS reversal is due;
                // - post-commit refund: restore returnable stock (SalesReturn movement
                //   + ReturnedQuantity) and stage one COGS reversal at the original
                //   snapshot cost, capped so cumulative reversals never exceed posted COGS.
                await LinkReturnStockAndCogsReversalForRefundAsync(
                    order,
                    refund,
                    normalizedRefundId,
                    ct);

                await _eventPublisher.PublishAsync(new OrderRefundedEvent
                {
                    TenantId = order.TenantId,
                    OrderId = orderId,
                    OrderNumber = order.OrderNumber,
                    RefundId = normalizedRefundId,
                    RefundAmount = normalizedRefundAmount,
                    IsFullRefund = isFullRefund,
                    Reason = reason,
                    PaymentMethod = paymentMethod,
                    RefundedAt = refundedAtUtc,
                    CorrelationId = _correlationIdAccessor.GetCorrelationId(),
                    IdempotencyKey = BuildRefundIdempotencyKey(order.TenantId, orderId, normalizedRefundId),
                    OccurredAtUtc = refundedAtUtc
                }, ct);

                await PublishRefundPaymentStatusChangedEventAsync(
                    order,
                    oldPaymentStatus,
                    normalizedRefundId,
                    refundedAtUtc,
                    ct);

                await PublishItemLevelRefundEventsAsync(
                    order,
                    refund,
                    normalizedRefundId,
                    reason,
                    refundedAtUtc,
                    ct);

                await ReconcileAffiliateCommissionReversalsForRefundAsync(
                    order,
                    refund,
                    refundedAtUtc,
                    ct);

                await _db.SaveChangesAsync(ct);
            }
            finally
            {
                PaymentStatusSaveChangesInterceptor.DisableAuthorizedContext();
            }

            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (IsDuplicateOrderRefundException(ex))
        {
            await transaction.RollbackAsync(ct);
            _db.ChangeTracker.Clear();

            var orderForDuplicateCheck = await _db.Orders
                .FirstOrDefaultAsync(o => o.Id == orderId, ct);

            _logger.LogInformation(
                ex,
                "Duplicate refund replay detected for order {OrderId} and refund id {RefundId}",
                orderId,
                refundId);

            if (orderForDuplicateCheck == null)
            {
                _logger.LogWarning(
                    ex,
                    "Order {OrderId} not found after duplicate refund exception for refund {RefundId}. Using fallback PaymentStatus.PartiallyRefunded",
                    orderId,
                    refundId);
            }

            return PaymentTransitionResult.Succeeded(
                orderForDuplicateCheck?.PaymentStatus ?? PaymentStatus.PartiallyRefunded,
                wasIdempotent: true,
                stateChanged: false);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            _logger.LogError(ex, "Failed to record refund for order {OrderId}", orderId);
            return PaymentTransitionResult.Failed(
                "Refund could not be processed.",
                "REFUND_PROCESSING_FAILED");
        }

        // Post to external accounting system AFTER local state is durable and committed.
        // This is best-effort: failures here are logged but do not affect the refund result.
        // The outbox row remains unmodified for retry if posting fails.
        try
        {
            var refundAccountingMessage = await LoadRequiredAccountingOutboxMessageAsync(
                order.TenantId,
                BuildEcommerceRefundAccountingIdempotencyKey(order.TenantId, orderId, normalizedRefundId),
                ct);

            await PostCommittedRefundAccountingFromSqlTruthAsync(
                order.TenantId,
                orderId,
                refund.Id,
                refundAccountingMessage.Id,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Post-commit accounting operation failed for refund {RefundId} on order {OrderId}. " +
                "Refund is committed; outbox will retry.",
                normalizedRefundId,
                orderId);
        }

        return PaymentTransitionResult.Succeeded(newStatus);
    }

    public async Task<PaymentTransitionResult> MarkPaymentFailedAsync(
        Guid orderId,
        string reason,
        CancellationToken ct = default)
    {
        var gateway = await _db.Orders
            .AsNoTracking()
            .Where(order => order.Id == orderId)
            .Select(order => order.PaymentMethodCode)
            .FirstOrDefaultAsync(ct);

        return await HandlePaymentResultAsync(new HandlePaymentResultRequest
        {
            OrderId = orderId,
            Outcome = PaymentLifecycleOutcome.Failed,
            Source = PaymentLifecycleSource.GatewayWebhook,
            Gateway = gateway,
            Reason = reason
        }, ct);
    }

    public async Task<PaymentTransitionResult> MarkPaymentExpiredAsync(
        Guid tenantId,
        Guid orderId,
        string reason,
        CancellationToken ct = default)
    {
        var activePaymentIntentIds = await _db.PaymentIntents
            .IgnoreQueryFilters()
            .Where(intent =>
                intent.TenantId == tenantId &&
                intent.OrderId == orderId &&
                !intent.IsDeleted &&
                (intent.Status == PaymentIntentStatus.Pending ||
                 intent.Status == PaymentIntentStatus.Authorized ||
                 intent.Status == PaymentIntentStatus.AwaitingVerification))
            .OrderByDescending(intent => intent.CreatedAt)
            .Select(intent => intent.Id)
            .ToListAsync(ct);

        // Check if there's a terminal Paid intent before processing any active intents
        // This prevents bypassing the paid-intent safety check even when mixed paid+active intents exist
        var hasPaidIntent = await _db.PaymentIntents
            .IgnoreQueryFilters()
            .AnyAsync(intent =>
                intent.TenantId == tenantId &&
                intent.OrderId == orderId &&
                !intent.IsDeleted &&
                intent.Status == PaymentIntentStatus.Paid,
                ct);

        if (hasPaidIntent)
        {
            // Load the paid intent to trigger the manual review path in HandleFailureAsync
            var paidIntentId = await _db.PaymentIntents
                .IgnoreQueryFilters()
                .Where(intent =>
                    intent.TenantId == tenantId &&
                    intent.OrderId == orderId &&
                    !intent.IsDeleted &&
                    intent.Status == PaymentIntentStatus.Paid)
                .OrderByDescending(intent => intent.CreatedAt)
                .Select(intent => (Guid?)intent.Id)
                .FirstOrDefaultAsync(ct);

            return await HandlePaymentResultAsync(
                CreateReservationExpiredRequest(tenantId, orderId, paidIntentId, reason, skipPaymentIntentResolution: false),
                ct);
        }

        if (activePaymentIntentIds.Count == 0)
        {
            // No active or paid intents - safe to skip intent resolution
            return await HandlePaymentResultAsync(
                CreateReservationExpiredRequest(tenantId, orderId, paymentIntentId: null, reason, skipPaymentIntentResolution: true),
                ct);
        }

        PaymentTransitionResult? lastResult = null;
        foreach (var paymentIntentId in activePaymentIntentIds)
        {
            lastResult = await HandlePaymentResultAsync(
                CreateReservationExpiredRequest(tenantId, orderId, paymentIntentId, reason, skipPaymentIntentResolution: false),
                ct);

            if (!lastResult.IsSuccess)
            {
                return lastResult;
            }
        }

        return lastResult ?? PaymentTransitionResult.Succeeded(PaymentStatus.Expired, wasIdempotent: true, stateChanged: false);
    }

    public async Task<PaymentTransitionResult> ResetFailedPaymentForRetryAsync(
        Guid orderId,
        Guid failedPaymentIntentId,
        CancellationToken ct = default)
    {
        if (orderId == Guid.Empty || failedPaymentIntentId == Guid.Empty)
        {
            return PaymentTransitionResult.Failed(
                "Payment retry authority requires an order and failed payment intent.",
                "PAYMENT_RETRY_INVALID_REQUEST");
        }

        await using var transaction = await BeginLifecycleTransactionAsync(ct);

        try
        {
            var order = await LoadOrderForPaymentLifecycleMutationAsync(
                orderId,
                ignoreQueryFilters: false,
                ct);
            if (order == null)
            {
                return PaymentTransitionResult.Failed("Order not found", "ORDER_NOT_FOUND");
            }

            var failedPaymentIntent = await _db.PaymentIntents
                .FirstOrDefaultAsync(intent =>
                    intent.Id == failedPaymentIntentId &&
                    intent.OrderId == order.Id &&
                    intent.TenantId == order.TenantId &&
                    !intent.IsDeleted,
                    ct);
            if (failedPaymentIntent == null)
            {
                return PaymentTransitionResult.Failed(
                    "Failed payment intent not found.",
                    "PAYMENT_RETRY_INTENT_NOT_FOUND");
            }

            if (order.Status != OrderStatus.Pending)
            {
                return PaymentTransitionResult.Failed(
                    $"Cannot retry payment for order status {order.Status}.",
                    "PAYMENT_RETRY_ORDER_NOT_PENDING");
            }

            if (order.PaymentStatus == PaymentStatus.Pending &&
                order.CheckoutState == CheckoutState.PaymentPending)
            {
                await transaction.CommitAsync(ct);
                return PaymentTransitionResult.Succeeded(
                    PaymentStatus.Pending,
                    wasIdempotent: true,
                    stateChanged: false);
            }

            if (order.PaymentStatus != PaymentStatus.Failed ||
                order.CheckoutState is not (CheckoutState.Failed or CheckoutState.Expired))
            {
                return PaymentTransitionResult.Failed(
                    $"Cannot retry payment from Payment:{order.PaymentStatus}/Checkout:{order.CheckoutState}.",
                    "PAYMENT_RETRY_INVALID_STATE");
            }

            if (failedPaymentIntent.Status is not (
                    PaymentIntentStatus.Failed or
                    PaymentIntentStatus.Cancelled or
                    PaymentIntentStatus.Rejected or
                    PaymentIntentStatus.Expired))
            {
                return PaymentTransitionResult.Failed(
                    $"Cannot retry from payment intent status {failedPaymentIntent.Status}.",
                    "PAYMENT_RETRY_INTENT_NOT_TERMINAL");
            }

            var hasActiveSiblingAttempt = await _db.PaymentIntents.AnyAsync(intent =>
                intent.OrderId == order.Id &&
                intent.TenantId == order.TenantId &&
                intent.Id != failedPaymentIntent.Id &&
                !intent.IsDeleted &&
                (intent.Status == PaymentIntentStatus.Pending ||
                 intent.Status == PaymentIntentStatus.Authorized ||
                 intent.Status == PaymentIntentStatus.AwaitingVerification),
                ct);
            if (hasActiveSiblingAttempt)
            {
                return PaymentTransitionResult.Failed(
                    "Cannot reset payment while another attempt is active.",
                    "PAYMENT_RETRY_ACTIVE_ATTEMPT_EXISTS");
            }

            var now = DateTime.UtcNow;
            var oldPaymentStatus = order.PaymentStatus;
            var retryRequest = new HandlePaymentResultRequest
            {
                OrderId = order.Id,
                PaymentIntentId = failedPaymentIntent.Id,
                Outcome = PaymentLifecycleOutcome.Authorized,
                Source = PaymentLifecycleSource.PaymentRetry,
                PaymentReference = failedPaymentIntent.PaymentReference,
                ProcessedAtUtc = now
            };

            PaymentStatusSaveChangesInterceptor.EnableAuthorizedContext();
            try
            {
                order.TransitionCheckoutState(CheckoutState.PendingPaymentIntent);
                order.TransitionCheckoutState(CheckoutState.PaymentPending);
                order.PaymentStatus = PaymentStatus.Pending;
                order.AcceptPaymentStatusChange();
                order.IsPaid = false;
                order.PaidAt = null;

                await PublishCompatibilityPaymentStatusChangedEventAsync(
                    order,
                    failedPaymentIntent,
                    retryRequest,
                    oldPaymentStatus,
                    amountPaid: null,
                    now,
                    ct);
                await _db.SaveChangesAsync(ct);
            }
            finally
            {
                PaymentStatusSaveChangesInterceptor.DisableAuthorizedContext();
            }

            await transaction.CommitAsync(ct);

            return PaymentTransitionResult.Succeeded(PaymentStatus.Pending);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);

            if (IsPaymentConcurrencyException(ex))
            {
                return PaymentTransitionResult.Failed(
                    "Payment retry authority changed concurrently. Refresh and try again.",
                    "PAYMENT_CONCURRENCY_CONFLICT");
            }

            _logger.LogError(ex, "Failed to reset payment for retry on order {OrderId}", orderId);
            return PaymentTransitionResult.Failed(
                $"Internal error: {ex.Message}",
                "PAYMENT_RETRY_FAILED");
        }
    }

    private HandlePaymentResultRequest CreateReservationExpiredRequest(Guid tenantId, Guid orderId, Guid? paymentIntentId, string reason, bool skipPaymentIntentResolution)
    {
        return new HandlePaymentResultRequest
        {
            TenantId = tenantId,
            OrderId = orderId,
            PaymentIntentId = paymentIntentId,
            Outcome = PaymentLifecycleOutcome.Expired,
            Source = PaymentLifecycleSource.ReservationCleanup,
            Reason = reason,
            FailureCode = "RESERVATION_EXPIRED",
            IgnoreQueryFilters = true,
            SkipPaymentIntentResolution = skipPaymentIntentResolution
        };
    }

    public async Task<PaymentAllowedActions> GetAllowedActionsAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order == null)
        {
            return new PaymentAllowedActions
            {
                OrderId = orderId,
                StatusMessage = "Order not found"
            };
        }

        var latestPaymentIntent = await _db.PaymentIntents
            .AsNoTracking()
            .Where(pi => pi.OrderId == orderId && !pi.IsDeleted)
            .OrderByDescending(pi => pi.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var isCod = IsCodOrder(order);
        var isWallet = IsWalletOrder(order);
        var isOnline = !isCod && !isWallet;
        var awaitingWalletVerification =
            latestPaymentIntent?.Status == PaymentIntentStatus.AwaitingVerification ||
            order.PaymentStatus == PaymentStatus.AwaitingVerification;

        return new PaymentAllowedActions
        {
            OrderId = orderId,
            CurrentStatus = order.PaymentStatus,
            PaymentMethodCode = order.PaymentMethodCode,
            CanMarkOfflineCollectionDelivered = isCod &&
                order.FulfillmentMode == FulfillmentMode.Manual &&
                string.IsNullOrWhiteSpace(order.FulfillmentCarrierCode) &&
                order.PaymentStatus == PaymentStatus.Pending &&
                order.FulfillmentStatus == FulfillmentStatus.Delivered,
            CanApproveReviewedPayment = isWallet && awaitingWalletVerification,
            CanRejectReviewedPayment = isWallet && awaitingWalletVerification,
            CanRefund = !IsFawryOrder(order) &&
                        latestPaymentIntent?.Provider != PaymentProvider.Fawry &&
                        (order.PaymentStatus == PaymentStatus.Collected ||
                         order.PaymentStatus == PaymentStatus.Settled ||
                         order.PaymentStatus == PaymentStatus.PartiallyRefunded),
            StatusMessage = GetStatusMessage(order, latestPaymentIntent, isCod, isWallet, isOnline)
        };
    }

    private async Task<GatewaySettlementResolution> ResolveGatewaySettlementOrdersAsync(
        GatewaySettlementRequest request,
        CancellationToken ct)
    {
        var hasBatchId = !string.IsNullOrWhiteSpace(request.PayoutBatchId);
        var explicitOrderIds = request.OrderIds
            .Where(orderId => orderId != Guid.Empty)
            .Distinct()
            .ToList();
        var explicitPaymentIntentIds = request.PaymentIntentIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var selectorCount = (hasBatchId ? 1 : 0)
            + (explicitOrderIds.Count > 0 ? 1 : 0)
            + (explicitPaymentIntentIds.Count > 0 ? 1 : 0);

        if (selectorCount != 1)
        {
            return GatewaySettlementResolution.Failed(
                RejectGatewaySettlement(
                    request.TenantId,
                    "Gateway settlement requires exactly one selector: payoutBatchId, orderIds, or paymentIntentIds.",
                    "ambiguous_order_selector"));
        }

        if (explicitOrderIds.Count > 0 && explicitOrderIds.Count != request.OrderIds.Count)
        {
            return GatewaySettlementResolution.Failed(
                RejectGatewaySettlement(
                    request.TenantId,
                    "Gateway settlement rejected: orderIds must be non-empty and duplicate-free.",
                    "invalid_order_ids"));
        }

        if (explicitPaymentIntentIds.Count > 0 && explicitPaymentIntentIds.Count != request.PaymentIntentIds.Count)
        {
            return GatewaySettlementResolution.Failed(
                RejectGatewaySettlement(
                    request.TenantId,
                    "Gateway settlement rejected: paymentIntentIds must be non-empty and duplicate-free.",
                    "invalid_payment_intent_ids"));
        }

        if (hasBatchId)
        {
            var payoutBatchId = request.PayoutBatchId!.Trim();
            var paymentIntents = await _db.PaymentIntents
                .IgnoreQueryFilters()
                .Include(paymentIntent => paymentIntent.Order)
                .Where(paymentIntent =>
                    !paymentIntent.IsDeleted &&
                    paymentIntent.TenantId == request.TenantId &&
                    paymentIntent.PayoutBatchId == payoutBatchId)
                .ToListAsync(ct);

            return BuildGatewaySettlementResolution(
                paymentIntents,
                requestedOrderCount: null);
        }

        if (explicitOrderIds.Count > 0)
        {
            var orders = await _db.Orders
                .IgnoreQueryFilters()
                .Where(order => 
                    explicitOrderIds.Contains(order.Id) && 
                    !order.IsDeleted &&
                    order.TenantId == request.TenantId)
                .ToListAsync(ct);

            return new GatewaySettlementResolution(
                orders,
                new Dictionary<Guid, Guid?>(),
                explicitOrderIds.Count,
                Failure: null);
        }

        var parsedIntentIds = explicitPaymentIntentIds
            .Select(id => Guid.TryParse(id, out var parsed) ? parsed : (Guid?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        var externalIntentIds = explicitPaymentIntentIds
            .Where(id => !Guid.TryParse(id, out _))
            .ToList();

        var resolvedPaymentIntents = await _db.PaymentIntents
            .IgnoreQueryFilters()
            .Include(paymentIntent => paymentIntent.Order)
            .Where(paymentIntent =>
                !paymentIntent.IsDeleted &&
                paymentIntent.TenantId == request.TenantId &&
                (parsedIntentIds.Contains(paymentIntent.Id) ||
                 (paymentIntent.ProviderIntentId != null && externalIntentIds.Contains(paymentIntent.ProviderIntentId)) ||
                 (paymentIntent.ProviderPaymentId != null && externalIntentIds.Contains(paymentIntent.ProviderPaymentId)) ||
                 externalIntentIds.Contains(paymentIntent.PaymentReference)))
            .ToListAsync(ct);

        return BuildGatewaySettlementResolution(
            resolvedPaymentIntents,
            explicitPaymentIntentIds.Count);
    }

    private static GatewaySettlementResolution BuildGatewaySettlementResolution(
        List<PaymentIntent> paymentIntents,
        int? requestedOrderCount)
    {
        var orders = paymentIntents
            .Where(paymentIntent => paymentIntent.Order != null)
            .Select(paymentIntent => paymentIntent.Order)
            .GroupBy(order => order.Id)
            .Select(group => group.First())
            .ToList();

        var paymentIntentIdsByOrder = paymentIntents
            .Where(paymentIntent => paymentIntent.OrderId != Guid.Empty)
            .GroupBy(paymentIntent => paymentIntent.OrderId)
            .ToDictionary(
                group => group.Key,
                group => (Guid?)group.OrderByDescending(paymentIntent => paymentIntent.CreatedAt).First().Id);

        return new GatewaySettlementResolution(
            orders,
            paymentIntentIdsByOrder,
            requestedOrderCount,
            Failure: null);
    }

    private async Task<PaymentSettlementBatch?> LoadSettlementBatchAsync(
        Guid tenantId,
        string settlementType,
        string settlementId,
        CancellationToken ct)
    {
        return await _db.PaymentSettlementBatches
            .IgnoreQueryFilters()
            .Include(batch => batch.Orders)
            .FirstOrDefaultAsync(batch =>
                !batch.IsDeleted &&
                batch.TenantId == tenantId &&
                batch.SettlementType == settlementType &&
                batch.SettlementId == settlementId,
                ct);
    }

    private static PaymentSettlementBatch CreateSettlementBatch(
        Guid tenantId,
        string settlementType,
        string settlementId,
        string? provider,
        string? payoutBatchId,
        decimal grossAmount,
        decimal fees,
        string currency,
        string? bankReference,
        List<Order> orders,
        IReadOnlyDictionary<Guid, Guid?>? paymentIntentIdsByOrder,
        DateTime occurredAtUtc)
    {
        var batch = new PaymentSettlementBatch
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SettlementType = settlementType,
            SettlementId = settlementId,
            Provider = provider,
            PayoutBatchId = string.IsNullOrWhiteSpace(payoutBatchId) ? null : payoutBatchId.Trim(),
            GrossAmount = grossAmount,
            Fees = fees,
            Currency = currency,
            BankReference = bankReference,
            OrderCount = orders.Count,
            SettledAtUtc = occurredAtUtc,
            CreatedAt = occurredAtUtc
        };

        batch.Orders = orders.Select(order =>
        {
            Guid? paymentIntentId = null;
            paymentIntentIdsByOrder?.TryGetValue(order.Id, out paymentIntentId);
            return new PaymentSettlementOrder
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                SettlementBatchId = batch.Id,
                OrderId = order.Id,
                PaymentIntentId = paymentIntentId,
                OrderTotal = order.Total,
                Currency = order.Currency,
                CreatedAt = occurredAtUtc
            };
        }).ToList();

        return batch;
    }

    private static bool SettlementBatchMatches(
        PaymentSettlementBatch batch,
        string settlementType,
        string settlementId,
        string? provider,
        string? payoutBatchId,
        decimal grossAmount,
        decimal fees,
        string currency,
        IReadOnlyCollection<Guid> orderIds)
    {
        var existingOrderIds = batch.Orders
            .Where(order => !order.IsDeleted)
            .Select(order => order.OrderId)
            .ToHashSet();

        return batch.SettlementType == settlementType &&
               batch.SettlementId == settlementId &&
               string.Equals(batch.Provider ?? string.Empty, provider ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(batch.PayoutBatchId ?? string.Empty, payoutBatchId ?? string.Empty, StringComparison.Ordinal) &&
               batch.GrossAmount == grossAmount &&
               batch.Fees == fees &&
               CurrencyEquals(batch.Currency, currency) &&
               existingOrderIds.SetEquals(orderIds);
    }

    private async Task<PaymentTransitionResult?> EnsureCollectionAccountingPostedForCodSettlementAsync(
        Guid tenantId,
        IReadOnlyCollection<Order> orders,
        CancellationToken ct)
    {
        var requiredKeysByOrder = orders.ToDictionary(
            order => order.Id,
            order => BuildCodDeliveredIdempotencyKey(tenantId, order.Id));

        var missingOrderIds = await ResolveMissingPostedAccountingKeysAsync(
            tenantId,
            requiredKeysByOrder,
            ct);

        if (missingOrderIds.Count == 0)
        {
            return null;
        }

        _logger.LogWarning(
            "COD settlement rejected because collection accounting is not posted | TenantId: {TenantId} | MissingOrderCount: {MissingOrderCount} | MissingOrderIds: {MissingOrderIds}",
            tenantId,
            missingOrderIds.Count,
            string.Join(",", missingOrderIds));

        return RejectOfflineSettlement(
            tenantId,
            "COD settlement rejected: collection accounting journal is not posted for all orders.",
            "collection_accounting_not_posted");
    }

    private async Task<PaymentTransitionResult?> EnsureCollectionAccountingPostedForGatewaySettlementAsync(
        Guid tenantId,
        IReadOnlyCollection<Order> orders,
        IReadOnlyDictionary<Guid, Guid?> resolvedPaymentIntentIdsByOrder,
        CancellationToken ct)
    {
        var requiredKeysByOrder = await ResolveGatewayCollectionAccountingKeysByOrderAsync(
            tenantId,
            orders,
            resolvedPaymentIntentIdsByOrder,
            ct);

        var unresolvedOrderIds = orders
            .Select(order => order.Id)
            .Where(orderId => !requiredKeysByOrder.ContainsKey(orderId))
            .ToList();

        if (unresolvedOrderIds.Count > 0)
        {
            _logger.LogWarning(
                "Gateway settlement rejected because paid payment intent could not be resolved for all orders | TenantId: {TenantId} | MissingOrderCount: {MissingOrderCount} | MissingOrderIds: {MissingOrderIds}",
                tenantId,
                unresolvedOrderIds.Count,
                string.Join(",", unresolvedOrderIds));

            return RejectGatewaySettlement(
                tenantId,
                "Gateway settlement rejected: paid payment intent could not be resolved for all orders.",
                "collection_payment_intent_not_resolved");
        }

        var missingOrderIds = await ResolveMissingPostedAccountingKeysAsync(
            tenantId,
            requiredKeysByOrder,
            ct);

        if (missingOrderIds.Count == 0)
        {
            return null;
        }

        _logger.LogWarning(
            "Gateway settlement rejected because collection accounting is not posted | TenantId: {TenantId} | MissingOrderCount: {MissingOrderCount} | MissingOrderIds: {MissingOrderIds}",
            tenantId,
            missingOrderIds.Count,
            string.Join(",", missingOrderIds));

        return RejectGatewaySettlement(
            tenantId,
            "Gateway settlement rejected: collection accounting journal is not posted for all orders.",
            "collection_accounting_not_posted");
    }

    private async Task<Dictionary<Guid, string>> ResolveGatewayCollectionAccountingKeysByOrderAsync(
        Guid tenantId,
        IReadOnlyCollection<Order> orders,
        IReadOnlyDictionary<Guid, Guid?> resolvedPaymentIntentIdsByOrder,
        CancellationToken ct)
    {
        var orderIds = orders.Select(order => order.Id).Distinct().ToList();
        var intentIdsByOrder = resolvedPaymentIntentIdsByOrder
            .Where(item => item.Value.HasValue)
            .ToDictionary(item => item.Key, item => item.Value!.Value);

        var unresolvedOrderIds = orderIds
            .Where(orderId => !intentIdsByOrder.ContainsKey(orderId))
            .ToList();

        if (unresolvedOrderIds.Count > 0)
        {
            var latestPaidIntentIds = await _db.PaymentIntents
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(paymentIntent =>
                    paymentIntent.TenantId == tenantId &&
                    unresolvedOrderIds.Contains(paymentIntent.OrderId) &&
                    paymentIntent.Status == PaymentIntentStatus.Paid &&
                    !paymentIntent.IsDeleted)
                .GroupBy(paymentIntent => paymentIntent.OrderId)
                .Select(group => new
                {
                    OrderId = group.Key,
                    PaymentIntentId = group
                        .OrderByDescending(paymentIntent => paymentIntent.CapturedAt ?? paymentIntent.UpdatedAt ?? paymentIntent.CreatedAt)
                        .Select(paymentIntent => paymentIntent.Id)
                        .First()
                })
                .ToListAsync(ct);

            foreach (var item in latestPaidIntentIds)
            {
                intentIdsByOrder[item.OrderId] = item.PaymentIntentId;
            }
        }

        return intentIdsByOrder.ToDictionary(
            item => item.Key,
            item => BuildOnlinePaymentIdempotencyKey(tenantId, item.Key, item.Value));
    }

    private async Task<List<Guid>> ResolveMissingPostedAccountingKeysAsync(
        Guid tenantId,
        IReadOnlyDictionary<Guid, string> requiredKeysByOrder,
        CancellationToken ct)
    {
        if (requiredKeysByOrder.Count == 0)
        {
            return new List<Guid>();
        }

        var requiredKeys = requiredKeysByOrder.Values.Distinct(StringComparer.Ordinal).ToList();
        var postedKeys = await _db.JournalEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(journal =>
                journal.TenantId == tenantId &&
                requiredKeys.Contains(journal.IdempotencyKey) &&
                journal.Status == JournalStatus.Posted)
            .Select(journal => journal.IdempotencyKey)
            .ToListAsync(ct);

        var postedKeySet = postedKeys.ToHashSet(StringComparer.Ordinal);
        return requiredKeysByOrder
            .Where(item => !postedKeySet.Contains(item.Value))
            .Select(item => item.Key)
            .ToList();
    }

    private static string BuildOnlinePaymentIdempotencyKey(Guid tenantId, Guid orderId, Guid paymentIntentId) =>
        $"ONLINE_PAYMENT:{tenantId}:{orderId}:{paymentIntentId}";

    private async Task<PaymentTransitionResult?> EnsureCollectionAccountingPostedForRefundAsync(
        Order order,
        CancellationToken ct)
    {
        if (order.FinancialPostingStatus == FinancialPostingStatus.NotRequired)
        {
            return null;
        }

        Dictionary<Guid, string> requiredKeysByOrder;
        if (IsCodOrder(order))
        {
            requiredKeysByOrder = new Dictionary<Guid, string>
            {
                [order.Id] = BuildCodDeliveredIdempotencyKey(order.TenantId, order.Id)
            };
        }
        else if (IsWalletOrder(order))
        {
            requiredKeysByOrder = new Dictionary<Guid, string>
            {
                [order.Id] = BuildWalletApprovedIdempotencyKey(order.TenantId, order.Id)
            };
        }
        else
        {
            requiredKeysByOrder = await ResolveGatewayCollectionAccountingKeysByOrderAsync(
                order.TenantId,
                new[] { order },
                new Dictionary<Guid, Guid?>(),
                ct);

            if (!requiredKeysByOrder.ContainsKey(order.Id))
            {
                _logger.LogWarning(
                    "Refund rejected because collection payment intent could not be resolved | TenantId: {TenantId} | OrderId: {OrderId} | PaymentMethodCode: {PaymentMethodCode} | PaymentGateway: {PaymentGateway}",
                    order.TenantId,
                    order.Id,
                    order.PaymentMethodCode,
                    order.PaymentGateway);

                return PaymentTransitionResult.Failed(
                    "Cannot refund until the original collection payment intent and accounting journal are resolved.");
            }
        }

        var missingOrderIds = await ResolveMissingPostedAccountingKeysAsync(
            order.TenantId,
            requiredKeysByOrder,
            ct);

        if (missingOrderIds.Count == 0)
        {
            return null;
        }

        _logger.LogWarning(
            "Refund rejected because collection accounting is not posted | TenantId: {TenantId} | OrderId: {OrderId} | FinancialPostingStatus: {FinancialPostingStatus}",
            order.TenantId,
            order.Id,
            order.FinancialPostingStatus);

        return PaymentTransitionResult.Failed(
            "Cannot refund until the original collection accounting journal is posted.");
    }

    private PaymentTransitionResult RejectGatewaySettlement(Guid tenantId, string message, string reason)
    {
        _reliabilityTelemetry?.RecordSettlementBatchMismatch(tenantId, SettlementTypeGateway, reason);
        return PaymentTransitionResult.Failed(message, SettlementMismatchFailureCode);
    }

    private PaymentTransitionResult RejectOfflineSettlement(Guid tenantId, string message, string reason)
    {
        _reliabilityTelemetry?.RecordCodSettlementRejected(tenantId, reason);
        return PaymentTransitionResult.Failed(message, OfflineSettlementRejectedFailureCode);
    }

    private static bool CurrencyEquals(string? left, string? right) =>
        string.Equals(NormalizeCurrency(left), NormalizeCurrency(right), StringComparison.Ordinal);

    private static bool GatewayMatches(Order order, string gateway) =>
        GatewayAuthorityMatches(order.PaymentGateway, gateway) ||
        GatewayAuthorityMatches(order.PaymentMethodCode, gateway);

    private static bool GatewayAuthorityMatches(string? left, string? right)
    {
        var leftProvider = NormalizeGatewayAuthority(left);
        var rightProvider = NormalizeGatewayAuthority(right);
        return leftProvider != null &&
               string.Equals(leftProvider, rightProvider, StringComparison.Ordinal);
    }

    private static string? NormalizeGatewayAuthority(string? gateway) =>
        gateway?.Trim().ToUpperInvariant() switch
        {
            "STRIPE" => "STRIPE",
            "PAYMOB" or "CARD" or "VISA" => "PAYMOB",
            "FAWRY" => "FAWRY",
            _ => null
        };

    private static bool IsSupportedGatewaySettlementProvider(string gateway) =>
        gateway.Trim().ToUpperInvariant() is "STRIPE" or "PAYMOB" or "CARD" or "VISA";

    private async Task<PaymentTransitionResult?> ValidateGatewaySettlementPaymentIntentAuthorityAsync(
        Guid tenantId,
        IReadOnlyCollection<Order> orders,
        IReadOnlyDictionary<Guid, Guid?> resolvedPaymentIntentIdsByOrder,
        string gateway,
        CancellationToken ct)
    {
        var orderIds = orders.Select(order => order.Id).Distinct().ToList();
        var selectedIntentIds = resolvedPaymentIntentIdsByOrder.Values
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var paymentIntents = await _db.PaymentIntents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(intent =>
                intent.TenantId == tenantId &&
                orderIds.Contains(intent.OrderId) &&
                !intent.IsDeleted &&
                (selectedIntentIds.Contains(intent.Id) || intent.Status == PaymentIntentStatus.Paid))
            .ToListAsync(ct);

        foreach (var order in orders)
        {
            PaymentIntent? paymentIntent = null;
            if (resolvedPaymentIntentIdsByOrder.TryGetValue(order.Id, out var selectedIntentId) &&
                selectedIntentId.HasValue)
            {
                paymentIntent = paymentIntents.FirstOrDefault(intent => intent.Id == selectedIntentId.Value);
            }
            else
            {
                paymentIntent = paymentIntents
                    .Where(intent => intent.OrderId == order.Id && intent.Status == PaymentIntentStatus.Paid)
                    .OrderByDescending(intent => intent.CapturedAt ?? intent.UpdatedAt ?? intent.CreatedAt)
                    .ThenByDescending(intent => intent.Id)
                    .FirstOrDefault();
            }

            if (paymentIntent?.Provider == PaymentProvider.Fawry)
            {
                return PaymentTransitionResult.Failed(
                    FawryFinancialAuthorityPolicy.Message,
                    FawryFinancialAuthorityPolicy.Code);
            }

            if (paymentIntent != null && !GatewayMatches(paymentIntent.Provider, gateway))
            {
                return RejectGatewaySettlement(
                    tenantId,
                    "Gateway settlement rejected: payment intent provider mismatch.",
                    "payment_intent_provider_mismatch");
            }
        }

        return null;
    }

    private static bool GatewayMatches(PaymentProvider provider, string gateway) =>
        provider switch
        {
            PaymentProvider.Stripe => string.Equals(gateway.Trim(), "STRIPE", StringComparison.OrdinalIgnoreCase),
            PaymentProvider.Paymob => gateway.Trim().ToUpperInvariant() is "PAYMOB" or "CARD" or "VISA",
            _ => false
        };

    private static bool IsFawryOrder(Order order) =>
        FawryFinancialAuthorityPolicy.IsFawry(order.PaymentGateway) ||
        FawryFinancialAuthorityPolicy.IsFawry(order.PaymentMethodCode);

    private async Task<bool> HasLatestPaidFawryPaymentIntentAsync(Order order, CancellationToken ct) =>
        await _db.PaymentIntents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(intent =>
                intent.TenantId == order.TenantId &&
                intent.OrderId == order.Id &&
                !intent.IsDeleted &&
                intent.Status == PaymentIntentStatus.Paid)
            .OrderByDescending(intent => intent.CapturedAt ?? intent.UpdatedAt ?? intent.CreatedAt)
            .ThenByDescending(intent => intent.Id)
            .Select(intent => (PaymentProvider?)intent.Provider)
            .FirstOrDefaultAsync(ct) == PaymentProvider.Fawry;

    private static PaymentTransitionResult? ValidateGatewayWebhookProviderAuthority(
        HandlePaymentResultRequest request)
    {
        if (request.Source != PaymentLifecycleSource.GatewayWebhook)
        {
            return null;
        }

        if (FawryFinancialAuthorityPolicy.IsFawry(request.Gateway))
        {
            return PaymentTransitionResult.Failed(
                FawryFinancialAuthorityPolicy.Message,
                FawryFinancialAuthorityPolicy.Code);
        }

        return TryResolveGatewayPaymentProvider(request.Gateway, out var provider) &&
               provider is PaymentProvider.Stripe or PaymentProvider.Paymob
            ? null
            : PaymentTransitionResult.Failed(
                "Gateway webhook provider is not supported for payment collection.",
                "PAYMENT_GATEWAY_UNSUPPORTED");
    }

    private async Task<PaymentTransitionResult> HandleAuthorizedAsync(
        HandlePaymentResultRequest request,
        CancellationToken ct)
    {
        await using var transaction = await BeginLifecycleTransactionAsync(ct);

        try
        {
            var providerAuthorityFailure = ValidateGatewayWebhookProviderAuthority(request);
            if (providerAuthorityFailure != null)
            {
                return providerAuthorityFailure;
            }

            var aggregate = await LoadAggregateAsync(request, ct);
            if (aggregate.Order == null)
            {
                return PaymentTransitionResult.Failed("Order not found");
            }

            var order = aggregate.Order;
            var paymentIntent = aggregate.PaymentIntent;

            if (IsFawryOrder(order) ||
                paymentIntent?.Provider == PaymentProvider.Fawry)
            {
                _logger.LogWarning(
                    "[PAYMENT-LIFECYCLE] Rejected Fawry order authorization without financial authority | OrderId: {OrderId} | Source: {Source}",
                    request.OrderId,
                    request.Source);
                return PaymentTransitionResult.Failed(
                    FawryFinancialAuthorityPolicy.Message,
                    FawryFinancialAuthorityPolicy.Code);
            }

            var supersededAttemptResult = await TryRejectSupersededTerminalAttemptAsync(
                order,
                paymentIntent,
                request,
                ct);
            if (supersededAttemptResult != null)
            {
                await transaction.CommitAsync(ct);
                return supersededAttemptResult;
            }

            // Check idempotent/replay terminal states BEFORE currency enforcement
            if (order.PaymentStatus == PaymentStatus.Collected ||
                order.PaymentStatus == PaymentStatus.Settled ||
                order.PaymentStatus == PaymentStatus.Paid)
            {
                return PaymentTransitionResult.Succeeded(
                    order.PaymentStatus,
                    wasIdempotent: true,
                    stateChanged: false);
            }

            if (order.PaymentStatus == PaymentStatus.Authorized &&
                paymentIntent?.Status == PaymentIntentStatus.Authorized)
            {
                if (order.CheckoutState == CheckoutState.PendingPaymentIntent)
                {
                    TransitionCheckoutState(order, CheckoutState.PaymentPending);
                    await _db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                }

                return PaymentTransitionResult.Succeeded(
                    PaymentStatus.Authorized,
                    wasIdempotent: true,
                    stateChanged: false);
            }

            // Currency resolution happens AFTER replay checks to prevent currency changes from breaking idempotency
            var currencyValidation = await TryResolveLifecycleCurrencyAsync(order, paymentIntent, request, ct);
            if (currencyValidation.Failure != null)
            {
                return currencyValidation.Failure;
            }

            request.Currency = currencyValidation.Currency;

            if (order.PaymentStatus != PaymentStatus.Pending &&
                order.PaymentStatus != PaymentStatus.Authorized)
            {
                return PaymentTransitionResult.Failed(
                    $"Cannot authorize payment: current status is {order.PaymentStatus}");
            }

            var now = request.ProcessedAtUtc ?? DateTime.UtcNow;

            UpdatePaymentIntentAuthorized(paymentIntent, request, now);

            PaymentStatusSaveChangesInterceptor.EnableAuthorizedContext();
            try
            {
                TransitionCheckoutState(order, CheckoutState.PaymentPending);
                order.PaymentStatus = PaymentStatus.Authorized;
                order.AcceptPaymentStatusChange();
                order.PaymentGateway = request.Gateway ?? order.PaymentGateway;
                order.PaymentReference = request.PaymentReference ?? order.PaymentReference;

                await PublishCanonicalAuthorizedOutcomeEventAsync(order, paymentIntent, request, now, ct);
                await _db.SaveChangesAsync(ct);
            }
            finally
            {
                PaymentStatusSaveChangesInterceptor.DisableAuthorizedContext();
            }

            await transaction.CommitAsync(ct);

            return PaymentTransitionResult.Succeeded(
                PaymentStatus.Authorized,
                stateChanged: true);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            _logger.LogError(ex, "Failed to authorize payment for order {OrderId}", request.OrderId);
            return PaymentTransitionResult.Failed($"Internal error: {ex.Message}");
        }
    }

    private async Task<PaymentTransitionResult> HandleSuccessAsync(
        HandlePaymentResultRequest request,
        CancellationToken ct)
    {
        await using var transaction = await BeginLifecycleTransactionAsync(ct);

        try
        {
            var providerAuthorityFailure = ValidateGatewayWebhookProviderAuthority(request);
            if (providerAuthorityFailure != null)
            {
                return providerAuthorityFailure;
            }

            var aggregate = await LoadAggregateAsync(request, ct);
            if (aggregate.Order == null)
            {
                return PaymentTransitionResult.Failed("Order not found");
            }

            var order = aggregate.Order;
            var paymentIntent = aggregate.PaymentIntent;
            var now = request.ProcessedAtUtc ?? DateTime.UtcNow;
            var correlationId = _correlationIdAccessor.GetCorrelationId();

            if (request.CarrierCollectionShipmentId.HasValue)
                await _db.Entry(order).ReloadAsync(ct); // locked SQL truth, including when a scoped context already tracked this order

            // T-09: authenticated Fawry transport evidence is not clearing or
            // settlement authority. Reject before any money mutation.
            if (IsFawryOrder(order) ||
                paymentIntent?.Provider == PaymentProvider.Fawry)
            {
                _logger.LogWarning(
                    "[PAYMENT-LIFECYCLE] Rejected Fawry success without financial authority | OrderId: {OrderId} | Source: {Source}",
                    request.OrderId,
                    request.Source);
                return PaymentTransitionResult.Failed(
                    FawryFinancialAuthorityPolicy.Message,
                    FawryFinancialAuthorityPolicy.Code);
            }

            if (request.Source == PaymentLifecycleSource.OfflineCollection)
            {
                var collectionAdmissionFailure = await ValidateCodCollectionAdmissionAsync(order, request, ct);
                if (collectionAdmissionFailure != null)
                {
                    if (collectionAdmissionFailure.RequiresManualReview) await transaction.CommitAsync(ct);
                    return collectionAdmissionFailure;
                }
                now = request.ProcessedAtUtc ?? now;
            }

            var supersededAttemptSuccessResult = await TryRejectSupersededTerminalAttemptAsync(
                order,
                paymentIntent,
                request,
                ct);
            if (supersededAttemptSuccessResult != null)
            {
                await transaction.CommitAsync(ct);
                return supersededAttemptSuccessResult;
            }

            // Check idempotent/replay terminal states BEFORE currency enforcement
            if (IsOrderSuccessfulTerminal(order))
            {
                bool checkoutStateRepaired = false;
                if (order.CheckoutState != CheckoutState.Paid)
                {
                    TransitionCheckoutState(order, CheckoutState.Paid);
                    checkoutStateRepaired = true;
                }

                // T-08: snapshot committed balances so a repair commit can stage its
                // own per-commit COGS event atomically below.
                var replayCogsCommittedBeforeByItem = await SnapshotCommittedQuantitiesByItemAsync(
                    order.TenantId, order.Id, ct);

                var (replayInventoryAction, replayQuantity) = await FinalizeReplayInventoryAsync(
                    order,
                    paymentIntent,
                    PaymentLifecycleOutcome.Succeeded,
                    ct);

                _logger.LogInformation(
                    "[PAYMENT-LIFECYCLE] Idempotent success replay ignored | OrderId: {OrderId} | Source: {Source} | PaymentStatus: {PaymentStatus}",
                    order.Id, request.Source, order.PaymentStatus);

                // Commit transaction once for both checkout state repair and inventory replay
                if (checkoutStateRepaired || replayInventoryAction != PaymentInventoryAction.None)
                {
                    if (replayInventoryAction == PaymentInventoryAction.Commit && replayQuantity > 0m)
                    {
                        await TryEnqueueEcommerceCogsForCommittedDeltaAsync(
                            order,
                            replayCogsCommittedBeforeByItem,
                            $"payment-replay-commit:{request.Source}",
                            DateTime.UtcNow,
                            ct);
                    }

                    await _db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    
                    // Record metrics only after transaction is committed
                    if (replayInventoryAction == PaymentInventoryAction.Commit)
                    {
                        _inventoryReservationMetrics.RecordCommit(replayQuantity, "payment_lifecycle_replay");
                    }
                    else if (replayInventoryAction == PaymentInventoryAction.Release)
                    {
                        _inventoryReservationMetrics.RecordRelease(replayQuantity, "payment_lifecycle_replay");
                    }
                }

                return PaymentTransitionResult.Succeeded(
                    PaymentStatus.Collected,
                    wasIdempotent: true,
                    stateChanged: checkoutStateRepaired || (replayInventoryAction != PaymentInventoryAction.None && replayQuantity > 0m),
                    inventoryAction: replayInventoryAction);
            }

            if (IsLateSuccessfulPaymentQuarantineTerminal(order, paymentIntent))
            {
                var failureCode = paymentIntent!.FailureCode!;
                return PaymentTransitionResult.ManualReviewRequired(
                    PaymentStatus.Expired,
                    ResolveLateSuccessfulPaymentQuarantineReason(paymentIntent),
                    failureCode);
            }

            if (IsAdminCancellationPendingQuarantineTerminal(order, paymentIntent))
            {
                return PaymentTransitionResult.ManualReviewRequired(
                    PaymentStatus.Expired,
                    "Provider reported a successful payment after an admin cancellation request was queued. Refund or manual reconciliation is required.",
                    "ADMIN_CANCELLATION_PENDING_RECONCILIATION_REQUIRED");
            }

            // Check if payment is already quarantined for amount mismatch BEFORE validating success state
            // This allows already-quarantined payments to return ManualReviewRequired instead of hard failure
            if (IsAmountMismatchTerminal(order, paymentIntent))
            {
                return PaymentTransitionResult.ManualReviewRequired(
                    PaymentStatus.AmountMismatch,
                    "Provider payment amount or currency mismatch is already quarantined for manual review.",
                    "PAYMENT_AMOUNT_MISMATCH");
            }

            var staleSuccessResult = TryRejectStaleProviderSuccessAgainstTerminalSqlTruth(
                order,
                paymentIntent,
                request);
            if (staleSuccessResult != null)
            {
                await transaction.CommitAsync(ct);
                return staleSuccessResult;
            }

            // Validate success state AFTER quarantine check to prevent overwriting terminal states
            var validationError = ValidateSuccessState(order, paymentIntent, request);
            if (validationError != null)
            {
                return PaymentTransitionResult.Failed(validationError);
            }

            var authorityFailure = await TryQuarantineProviderAuthorityMismatchAsync(
                order,
                paymentIntent,
                request,
                now,
                ct);

            if (authorityFailure != null)
            {
                await transaction.CommitAsync(ct);
                return authorityFailure;
            }

            // Currency resolution happens AFTER replay checks to prevent currency changes from breaking idempotency
            var currencyValidation = await TryResolveLifecycleCurrencyAsync(order, paymentIntent, request, ct);
            if (currencyValidation.Failure != null)
            {
                return currencyValidation.Failure;
            }

            request.Currency = currencyValidation.Currency;

            if (request.Source == PaymentLifecycleSource.OfflineCollection)
            {
                if (!IsCodOrder(order))
                {
                    return PaymentTransitionResult.Failed("Order is not a COD order");
                }

                if (order.FulfillmentStatus != FulfillmentStatus.Delivered)
                {
                    return PaymentTransitionResult.Failed(
                        $"Cannot collect COD: order not delivered (current fulfillment status: {order.FulfillmentStatus})");
                }
            }

            if (request.Source == PaymentLifecycleSource.PaymentReviewApproval && !IsWalletOrder(order))
            {
                return PaymentTransitionResult.Failed("Order is not a wallet payment order");
            }

            if (request.Source == PaymentLifecycleSource.PaymentReviewApproval)
            {
                var expirationFailure = ValidateManualWalletExpirationAuthority(
                    paymentIntent,
                    now,
                    allowElapsed: false);
                if (expirationFailure is not null)
                {
                    return expirationFailure;
                }

                var retentionFailure = ValidateManualWalletProofRetentionAuthority(paymentIntent, request);
                if (retentionFailure is not null)
                {
                    return retentionFailure;
                }
            }

            var reservationValidation = await ValidateReservationCanAcceptPaymentAsync(
                order,
                request,
                now,
                ct);

            if (IsAdminCancellationPending(order) &&
                RequiresActiveReservationForSuccessfulPayment(request.Source))
            {
                var quarantineResult = await QuarantineLateSuccessfulPaymentAsync(
                    order,
                    paymentIntent,
                    request,
                    now,
                    "Provider reported a successful payment after an admin cancellation request was queued.",
                    "ADMIN_CANCELLATION_PENDING_RECONCILIATION_REQUIRED",
                    ct);

                await transaction.CommitAsync(ct);

                if (quarantineResult.CouponReleaseDecision.WasReleased)
                {
                    _reliabilityTelemetry?.RecordCouponReservationReleased(
                        order.TenantId,
                        "payment_admin_cancellation_quarantine",
                        "ADMIN_CANCELLATION_PENDING_RECONCILIATION_REQUIRED");
                }

                return quarantineResult.Result;
            }

            if (!reservationValidation.IsValid)
            {
                var quarantineResult = await QuarantineLateSuccessfulPaymentAsync(
                    order,
                    paymentIntent,
                    request,
                    now,
                    reservationValidation.Reason,
                    reservationValidation.FailureCode,
                    ct);

                await transaction.CommitAsync(ct);

                if (quarantineResult.CouponReleaseDecision.WasReleased)
                {
                    _reliabilityTelemetry?.RecordCouponReservationReleased(
                        order.TenantId,
                        "payment_late_quarantine",
                        reservationValidation.FailureCode);
                }

                return quarantineResult.Result;
            }

            var amount = ResolvePaymentAmount(order, paymentIntent, request);

            if (request.Source == PaymentLifecycleSource.PaymentReviewApproval)
            {
                var settlementFailure = await ValidateManualWalletSettlementAsync(order, paymentIntent, amount, ct);
                if (settlementFailure is not null) return settlementFailure;
            }

            ValidateSuccessfulAmount(order, amount);
            UpdatePaymentIntentSucceeded(paymentIntent, request, now);

            OutboxEnqueueResult? outboxResult = null;
            CouponConfirmationDecision couponConfirmationDecision = CouponConfirmationDecision.None();
            var offerUsageConfirmed = false;
            
            // Declare variables outside try block for post-commit use
            bool routedToSuspense = false;
            string? resolvedPaymentReference = null;
            AccountingStageResult? accountingStage = null;

            PaymentStatusSaveChangesInterceptor.EnableAuthorizedContext();
            try
            {
                using var orderStatusScope = OrderStatusSaveChangesInterceptor.BeginAuthorizedContext();
                var oldPaymentStatus = order.PaymentStatus;
                var oldOrderStatus = order.Status;
                ApplySuccessfulPaymentAllocation(order, amount);
                var reservedQuantityToCommit = await GetOpenReservationQuantityAsync(order.TenantId, order.Id, ct);

                TransitionCheckoutState(order, CheckoutState.Paid);
                order.PaymentStatus = PaymentStatus.Collected;
                order.AcceptPaymentStatusChange();
                order.IsPaid = order.Items.Count == 0 || order.Items.All(item => item.IsFullyPaid);
                order.PaidAt = now;
                order.PaymentGateway = ResolveGateway(order, request);
                order.PaymentReference = request.PaymentReference ?? order.PaymentReference;

                if (request.Source == PaymentLifecycleSource.OfflineCollection)
                {
                    order.DeliveredAt = now;
                    if (order.Status == OrderStatus.Shipped || order.Status == OrderStatus.Pending)
                    {
                        order.Status = OrderStatus.Delivered;
                    }
                }
                else if (order.Status == OrderStatus.Pending)
                {
                    order.Status = OrderStatus.Confirmed;
                }

                // T-08: snapshot committed balances BEFORE the commit so the COGS event
                // can prove exactly which quantities this transaction committed.
                var cogsCommittedBeforeByItem = await SnapshotCommittedQuantitiesByItemAsync(
                    order.TenantId, order.Id, ct);

                await _stockRepository.CommitOrderReservationsAsync(order.TenantId, order.Id, ct);
                if (paymentIntent != null)
                {
                    paymentIntent.InventoryCommitted = true;
                }

                if (reservedQuantityToCommit > 0m)
                {
                    _inventoryReservationMetrics.RecordCommit(reservedQuantityToCommit, "payment_lifecycle");
                }

                couponConfirmationDecision = await _couponValidationService.ConfirmCouponUsageAsync(
                    order.TenantId,
                    order.Id,
                    correlationId,
                    ct);

                offerUsageConfirmed = await ConfirmOfferPromotionUsageAsync(
                    order,
                    correlationId,
                    ct);

                // Compute suspense-routing decision once for consistent behavior across outbox and posting
                routedToSuspense = await WasPreviouslyRoutedToSuspenseAsync(order, request, ct);

                // Resolve payment reference once with full fallback chain for consistent behavior
                resolvedPaymentReference = request.PaymentReference ??
                                               paymentIntent?.ProviderIntentId ??
                                               request.ProviderPaymentId ??
                                               paymentIntent?.ProviderPaymentId ??
                                               paymentIntent?.PaymentReference ??
                                               order.PaymentReference;

                accountingStage = await EnqueueSuccessOutboxAsync(order, paymentIntent, request, amount, routedToSuspense, resolvedPaymentReference, ct);
                outboxResult = accountingStage.OutboxResult;

                // T-08: stage the merchandise-cost event atomically with the stock
                // commit above. Enqueue is a no-op when this transaction committed
                // no new quantities (replay/second capture), so duplicates cannot
                // double-post: one commit event → one COGS journal.
                await TryEnqueueEcommerceCogsForCommittedDeltaAsync(
                    order,
                    cogsCommittedBeforeByItem,
                    BuildEcommerceCogsCommitEventId(order, request, resolvedPaymentReference, routedToSuspense),
                    now,
                    ct);

                await PublishSuccessDomainEventAsync(order, request, amount, now, ct);
                await PublishCanonicalSuccessOutcomeEventsAsync(order, paymentIntent, request, amount, now, ct);
                await PublishCompatibilityPaymentStatusChangedEventAsync(
                    order,
                    paymentIntent,
                    request,
                    oldPaymentStatus,
                    amount,
                    now,
                    ct);
                await PublishCompatibilityOrderStatusChangedEventAsync(
                    order,
                    request,
                    oldOrderStatus,
                    now,
                    ct);

                await _db.SaveChangesAsync(ct);
                if (request.CarrierCollectionEvidenceId.HasValue)
                    await PostCarrierCollectionAtomicallyAsync(order, request, accountingStage!.Message, amount, now, ct);
            }
            finally
            {
                PaymentStatusSaveChangesInterceptor.DisableAuthorizedContext();
            }

            await transaction.CommitAsync(ct);

            // Post to external accounting system AFTER local state is durable and committed.
            // This is best-effort: failures here are logged but do not affect the payment result.
            // The outbox row remains unmodified for retry if posting fails.
            try
            {
                await PostCommittedCollectionAccountingFromSqlTruthAsync(
                    order.TenantId,
                    order.Id,
                    accountingStage.Message.Id,
                    now,
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Post-commit accounting operation failed for payment collection on order {OrderId}. " +
                    "Payment is committed; outbox will retry.",
                    order.Id);
            }

            // Post-commit work: logging and telemetry only - exceptions here should not affect payment status
            try
            {
                _logger.LogInformation(
                    "[PAYMENT-LIFECYCLE] Payment succeeded | OrderId: {OrderId} | Source: {Source} | InventoryAction: Commit | PaymentReference: {PaymentReference}",
                    order.Id, request.Source, request.PaymentReference ?? order.PaymentReference);

                if (couponConfirmationDecision.WasConfirmed)
                {
                    _reliabilityTelemetry?.RecordCouponUsageConfirmed(order.TenantId, "payment_success");
                }

                if (offerUsageConfirmed)
                {
                    _logger.LogInformation(
                        "[PAYMENT-LIFECYCLE] Offer usage confirmed after payment success | TenantId: {TenantId} | OrderId: {OrderId} | PromotionId: {PromotionId}",
                        order.TenantId,
                        order.Id,
                        order.AppliedPromotionId);
                }
            }
            catch (Exception postCommitEx)
            {
                // Transaction already committed successfully - log but don't fail the operation
                _logger.LogWarning(
                    postCommitEx,
                    "Post-commit logging/telemetry failed for successful payment | OrderId: {OrderId} | Payment was committed successfully",
                    request.OrderId);
            }

            return PaymentTransitionResult.Succeeded(
                PaymentStatus.Collected,
                outboxStatus: outboxResult?.Status,
                outboxMessageId: outboxResult?.MessageId ?? outboxResult?.ExistingMessageId,
                stateChanged: true,
                inventoryAction: PaymentInventoryAction.Commit);
        }
        catch (Exception ex)
        {
            // Pre-commit exception - safe to rollback
            try
            {
                await transaction.RollbackAsync(ct);
            }
            catch (Exception rollbackEx)
            {
                _logger.LogWarning(
                    rollbackEx,
                    "Failed to roll back successful payment lifecycle transaction after error | OrderId: {OrderId}",
                    request.OrderId);
            }

            if (IsPaymentConcurrencyException(ex))
            {
                _logger.LogWarning(
                    ex,
                    "Concurrent payment outcome rejected for order {OrderId}",
                    request.OrderId);
                return PaymentTransitionResult.Failed(
                    "This payment was updated by another administrator. Refresh and try again.",
                    "PAYMENT_CONCURRENCY_CONFLICT");
            }

            _logger.LogError(ex, "Failed to finalize successful payment for order {OrderId}", request.OrderId);
            return PaymentTransitionResult.Failed($"Internal error: {ex.Message}");
        }
    }

    private static bool IsPaymentConcurrencyException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateConcurrencyException)
            {
                return true;
            }

            if (current is PostgresException postgresException &&
                postgresException.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<PaymentTransitionResult> HandleFailureAsync(
        HandlePaymentResultRequest request,
        CancellationToken ct)
    {
        await using var transaction = await BeginLifecycleTransactionAsync(ct);

        try
        {
            var aggregate = await LoadAggregateAsync(request, ct);
            if (aggregate.Order == null)
            {
                return PaymentTransitionResult.Failed("Order not found");
            }

            var order = aggregate.Order;
            var paymentIntent = aggregate.PaymentIntent;
            var targetStatus = request.Outcome switch
            {
                PaymentLifecycleOutcome.Rejected => PaymentStatus.Rejected,
                PaymentLifecycleOutcome.Expired => PaymentStatus.Expired,
                _ => PaymentStatus.Failed
            };

            if (request.Source == PaymentLifecycleSource.ReservationCleanup)
                await _db.Entry(order).ReloadAsync(ct);

            // Recheck under the lifecycle's order lock. A cleanup candidate may
            // race a confirmation or a payment-method change; TTL cannot expire
            // a live COD order or release its purchased inventory/coupon.
            if (request.Source == PaymentLifecycleSource.ReservationCleanup &&
                PointOfSale.Core.Policies.CodReservationExpiryPolicy.ProtectsLiveOrder(
                    order.PaymentMethodCode, order.PaymentStatus, order.Status))
            {
                return PaymentTransitionResult.Succeeded(order.PaymentStatus,
                    wasIdempotent: true, stateChanged: false);
            }

            var supersededAttemptResult = await TryRejectSupersededTerminalAttemptAsync(
                order,
                paymentIntent,
                request,
                ct);
            if (supersededAttemptResult != null)
            {
                await transaction.CommitAsync(ct);
                return supersededAttemptResult;
            }

            var staleFailureResult = TryCollapseStaleProviderFailureAgainstTerminalSqlTruth(
                order,
                paymentIntent,
                request,
                targetStatus);
            if (staleFailureResult != null)
            {
                await transaction.CommitAsync(ct);
                return staleFailureResult;
            }

            // Check idempotent/replay terminal states BEFORE currency enforcement
            if (order.PaymentStatus == targetStatus &&
                IsMatchingIntentTerminal(paymentIntent, request.Outcome))
            {
                // T-08: snapshot committed balances so a repair commit can stage its
                // own per-commit COGS event atomically below.
                var failureReplayCogsBeforeByItem = await SnapshotCommittedQuantitiesByItemAsync(
                    order.TenantId, order.Id, ct);

                var (replayInventoryAction, replayQuantity) = await FinalizeReplayInventoryAsync(
                    order,
                    paymentIntent,
                    request.Outcome,
                    ct);

                if (replayInventoryAction != PaymentInventoryAction.None)
                {
                    if (replayInventoryAction == PaymentInventoryAction.Commit && replayQuantity > 0m)
                    {
                        await TryEnqueueEcommerceCogsForCommittedDeltaAsync(
                            order,
                            failureReplayCogsBeforeByItem,
                            $"payment-replay-commit:{request.Source}",
                            DateTime.UtcNow,
                            ct);
                    }

                    await transaction.CommitAsync(ct);
                    
                    // Record metrics only after transaction is committed
                    if (replayInventoryAction == PaymentInventoryAction.Commit)
                    {
                        _inventoryReservationMetrics.RecordCommit(replayQuantity, "payment_lifecycle_replay");
                    }
                    else if (replayInventoryAction == PaymentInventoryAction.Release)
                    {
                        _inventoryReservationMetrics.RecordRelease(replayQuantity, "payment_lifecycle_replay");
                    }
                }

                return PaymentTransitionResult.Succeeded(
                    targetStatus,
                    wasIdempotent: true,
                    stateChanged: replayInventoryAction != PaymentInventoryAction.None,
                    inventoryAction: replayInventoryAction);
            }

            if (paymentIntent?.Status == PaymentIntentStatus.Paid && !IsOrderSuccessfulTerminal(order))
            {
                _logger.LogWarning(
                    "[PAYMENT-LIFECYCLE] Failure ignored because payment intent is already paid but order is not finalized | OrderId: {OrderId} | Source: {Source} | OrderPaymentStatus: {PaymentStatus}",
                    order.Id, request.Source, order.PaymentStatus);

                await transaction.CommitAsync(ct);
                return PaymentTransitionResult.ManualReviewRequired(
                    order.PaymentStatus,
                    "Payment intent is paid but order is not finalized; retry the success lifecycle before applying a failure.",
                    "PAID_INTENT_NOT_FINALIZED");
            }

            if (IsOrderSuccessfulTerminal(order))
            {
                // T-08: snapshot committed balances so a repair commit can stage its
                // own per-commit COGS event atomically below.
                var staleFailureCogsBeforeByItem = await SnapshotCommittedQuantitiesByItemAsync(
                    order.TenantId, order.Id, ct);

                var (replayInventoryAction, replayQuantity) = await FinalizeReplayInventoryAsync(
                    order,
                    paymentIntent,
                    PaymentLifecycleOutcome.Succeeded,
                    ct);

                _logger.LogWarning(
                    "[PAYMENT-LIFECYCLE] Ignoring stale failure after success | OrderId: {OrderId} | Source: {Source} | PaymentStatus: {PaymentStatus}",
                    order.Id, request.Source, order.PaymentStatus);

                if (replayInventoryAction != PaymentInventoryAction.None)
                {
                    if (replayInventoryAction == PaymentInventoryAction.Commit && replayQuantity > 0m)
                    {
                        await TryEnqueueEcommerceCogsForCommittedDeltaAsync(
                            order,
                            staleFailureCogsBeforeByItem,
                            $"payment-replay-commit:{request.Source}",
                            DateTime.UtcNow,
                            ct);
                    }

                    await transaction.CommitAsync(ct);
                    
                    // Record metrics only after transaction is committed
                    if (replayInventoryAction == PaymentInventoryAction.Commit)
                    {
                        _inventoryReservationMetrics.RecordCommit(replayQuantity, "payment_lifecycle_replay");
                    }
                    else if (replayInventoryAction == PaymentInventoryAction.Release)
                    {
                        _inventoryReservationMetrics.RecordRelease(replayQuantity, "payment_lifecycle_replay");
                    }
                }

                return PaymentTransitionResult.Succeeded(
                    NormalizeSuccessfulStatus(order.PaymentStatus),
                    wasIdempotent: true,
                    stateChanged: replayInventoryAction != PaymentInventoryAction.None,
                    inventoryAction: replayInventoryAction);
            }

            // Currency resolution happens AFTER replay checks to prevent currency changes from breaking idempotency
            var currencyValidation = await TryResolveLifecycleCurrencyAsync(order, paymentIntent, request, ct);
            if (currencyValidation.Failure != null)
            {
                return currencyValidation.Failure;
            }

            request.Currency = currencyValidation.Currency;

            if (request.Source == PaymentLifecycleSource.PaymentReviewRejection && !IsWalletOrder(order))
            {
                return PaymentTransitionResult.Failed("Order is not a wallet payment order");
            }

            if (request.Source == PaymentLifecycleSource.PaymentReviewRejection)
            {
                var observedAtUtc = request.ProcessedAtUtc ?? DateTime.UtcNow;
                var expirationFailure = ValidateManualWalletExpirationAuthority(
                    paymentIntent,
                    observedAtUtc,
                    allowElapsed: false);
                if (expirationFailure is not null)
                {
                    return expirationFailure;
                }

                var retentionFailure = ValidateManualWalletProofRetentionAuthority(paymentIntent, request);
                if (retentionFailure is not null)
                {
                    return retentionFailure;
                }
            }

            var validationError = ValidateFailureState(order, paymentIntent, request, targetStatus);
            if (validationError != null)
            {
                return PaymentTransitionResult.Failed(validationError);
            }

            var now = request.ProcessedAtUtc ?? DateTime.UtcNow;
            var correlationId = _correlationIdAccessor.GetCorrelationId();
            CouponReleaseDecision couponReleaseDecision = CouponReleaseDecision.None();

            UpdatePaymentIntentFailed(paymentIntent, request, now);

            PaymentStatusSaveChangesInterceptor.EnableAuthorizedContext();
            try
            {
                using var orderStatusScope = OrderStatusSaveChangesInterceptor.BeginAuthorizedContext();
                var oldPaymentStatus = order.PaymentStatus;
                var oldOrderStatus = order.Status;
                var reservedQuantityToRelease = await GetOpenReservationQuantityAsync(order.TenantId, order.Id, ct);

                TransitionCheckoutState(
                    order,
                    request.Outcome == PaymentLifecycleOutcome.Expired
                        ? CheckoutState.Expired
                        : request.Outcome is PaymentLifecycleOutcome.Rejected or PaymentLifecycleOutcome.Cancelled
                        ? CheckoutState.Cancelled
                        : CheckoutState.Failed);
                order.PaymentStatus = targetStatus;
                order.AcceptPaymentStatusChange();
                order.IsPaid = false;
                order.PaidAt = null;
                AppendAdminNote(order, request.Reason, request.Actor, now, request.Outcome);

                if (request.Outcome is PaymentLifecycleOutcome.Rejected or PaymentLifecycleOutcome.Cancelled or PaymentLifecycleOutcome.Expired)
                {
                    order.Status = OrderStatus.Cancelled;
                    order.CancelledAt = now;
                    order.CancellationReason = request.Reason ?? (request.Outcome == PaymentLifecycleOutcome.Expired
                        ? "Payment expired"
                        : "Payment cancelled");
                }

                var inventoryReleased = await _stockRepository.ReleaseOrderReservationsAsync(order.TenantId, order.Id, ct);
                if (inventoryReleased)
                {
                    if (reservedQuantityToRelease > 0m)
                    {
                        _inventoryReservationMetrics.RecordRelease(reservedQuantityToRelease, "payment_lifecycle");
                    }
                }

                couponReleaseDecision = await _couponValidationService.ReleaseCouponReservationAsync(
                    order.TenantId,
                    order.Id,
                    correlationId,
                    request.Reason ?? request.Outcome.ToString(),
                    ct);

                await PublishFailureDomainEventAsync(order, request, now, ct);
                await PublishCanonicalFailureOutcomeEventsAsync(order, paymentIntent, request, now, ct);
                await PublishCompatibilityPaymentStatusChangedEventAsync(
                    order,
                    paymentIntent,
                    request,
                    oldPaymentStatus,
                    amountPaid: null,
                    now,
                    ct);
                await PublishCompatibilityOrderStatusChangedEventAsync(
                    order,
                    request,
                    oldOrderStatus,
                    now,
                    ct);
                await _db.SaveChangesAsync(ct);
            }
            finally
            {
                PaymentStatusSaveChangesInterceptor.DisableAuthorizedContext();
            }

            await transaction.CommitAsync(ct);

            // Post-commit work: logging and telemetry only - exceptions here should not affect payment status
            try
            {
                _logger.LogInformation(
                    "[PAYMENT-LIFECYCLE] Payment failed | OrderId: {OrderId} | Source: {Source} | InventoryAction: Release | Reason: {Reason}",
                    order.Id, request.Source, request.Reason);

                if (couponReleaseDecision.WasReleased)
                {
                    _reliabilityTelemetry?.RecordCouponReservationReleased(
                        order.TenantId,
                        "payment_failure",
                        request.Reason ?? request.Outcome.ToString());
                }
            }
            catch (Exception postCommitEx)
            {
                // Transaction already committed successfully - log but don't fail the operation
                _logger.LogWarning(
                    postCommitEx,
                    "Post-commit logging/telemetry failed for failed payment | OrderId: {OrderId} | Payment failure was committed successfully",
                    request.OrderId);
            }

            return PaymentTransitionResult.Succeeded(
                targetStatus,
                stateChanged: true,
                inventoryAction: PaymentInventoryAction.Release);
        }
        catch (Exception ex)
        {
            // Pre-commit exception - safe to rollback
            try
            {
                await transaction.RollbackAsync(ct);
            }
            catch (Exception rollbackEx)
            {
                _logger.LogWarning(
                    rollbackEx,
                    "Failed to roll back failed payment lifecycle transaction after error | OrderId: {OrderId}",
                    request.OrderId);
            }

            _logger.LogError(ex, "Failed to finalize failed payment for order {OrderId}", request.OrderId);
            return PaymentTransitionResult.Failed($"Internal error: {ex.Message}");
        }
    }

    private async Task<PaymentLifecycleAggregate> LoadAggregateAsync(
        HandlePaymentResultRequest request,
        CancellationToken ct)
    {
        var paymentIntent = await ResolvePaymentIntentForLifecycleMutationAsync(request, ct);
        Order? order = null;

        if (paymentIntent != null)
        {
            order = await LoadOrderForPaymentLifecycleMutationAsync(
                paymentIntent.OrderId,
                request.IgnoreQueryFilters,
                ct);

            if (order != null)
            {
                paymentIntent.Order = order;
            }
        }

        if (order == null && request.OrderId != Guid.Empty)
        {
            order = await LoadOrderForPaymentLifecycleMutationAsync(
                request.OrderId,
                request.IgnoreQueryFilters,
                ct);
        }

        if (request.TenantId.HasValue &&
            ((order != null && order.TenantId != request.TenantId.Value) ||
             (paymentIntent != null && paymentIntent.TenantId != request.TenantId.Value)))
        {
            _logger.LogCritical(
                "[PAYMENT-LIFECYCLE] Rejected cross-tenant payment mutation | ExpectedTenantId: {ExpectedTenantId} | OrderId: {OrderId} | OrderTenantId: {OrderTenantId} | PaymentIntentId: {PaymentIntentId} | PaymentIntentTenantId: {PaymentIntentTenantId} | Source: {Source}",
                request.TenantId.Value,
                request.OrderId,
                order?.TenantId,
                paymentIntent?.Id,
                paymentIntent?.TenantId,
                request.Source);

            return new PaymentLifecycleAggregate(null, null);
        }

        return new PaymentLifecycleAggregate(order, paymentIntent);
    }

    private async Task<Order?> LoadOrderForPaymentLifecycleMutationAsync(
        Guid orderId,
        bool ignoreQueryFilters,
        CancellationToken ct)
    {
        IQueryable<Order> query;

        if (IsPostgresProvider())
        {
            if (ignoreQueryFilters)
            {
                query = _db.Orders.FromSqlInterpolated($@"
                    SELECT xmin, * FROM ""Orders""
                    WHERE ""Id"" = {orderId}
                    FOR UPDATE");
            }
            else
            {
                query = _db.Orders.FromSqlInterpolated($@"
                    SELECT xmin, * FROM ""Orders""
                    WHERE ""Id"" = {orderId}
                      AND ""IsDeleted"" = false
                    FOR UPDATE");
            }
        }
        else if (IsSqlServerProvider())
        {
            if (ignoreQueryFilters)
            {
                query = _db.Orders.FromSqlInterpolated($@"
                    SELECT * FROM [Orders] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] = {orderId}");
            }
            else
            {
                query = _db.Orders.FromSqlInterpolated($@"
                    SELECT * FROM [Orders] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] = {orderId}
                      AND [IsDeleted] = 0");
            }
        }
        else
        {
            var providerName = _db.Database.ProviderName ?? "Unknown";
            if (_requireRowLevelLocking)
            {
                throw new InvalidOperationException(
                    $"Database provider '{providerName}' is not supported for production payment lifecycle flows. " +
                    "Row-level locking is required to prevent concurrent payment outcome, cleanup, and settlement races.");
            }

            _logger.LogWarning(
                "Database provider '{ProviderName}' does not support payment lifecycle row locking. " +
                "Payment outcome flows may be vulnerable to races outside test environments.",
                providerName);

            query = _db.Orders.Where(order => order.Id == orderId);
            
            if (!ignoreQueryFilters)
            {
                query = query.Where(order => !order.IsDeleted);
            }
        }

        if (ignoreQueryFilters)
        {
            query = query.IgnoreQueryFilters();
        }

        return await query
            .Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.Id == orderId, ct);
    }

    private async Task<List<Order>> LoadOrdersForSettlementMutationAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> orderIds,
        CancellationToken ct)
    {
        var orders = new List<Order>(orderIds.Count);

        foreach (var orderId in orderIds.Where(id => id != Guid.Empty).Distinct().OrderBy(id => id))
        {
            var order = await LoadOrderForPaymentLifecycleMutationAsync(
                orderId,
                ignoreQueryFilters: true,
                ct);

            if (order != null && order.TenantId == tenantId)
            {
                orders.Add(order);
            }
        }

        return orders;
    }

    private async Task<LifecycleTransactionScope> BeginLifecycleTransactionAsync(CancellationToken ct)
    {
        if (!_db.Database.IsRelational())
        {
            return LifecycleTransactionScope.JoinExisting();
        }

        var currentTransaction = _db.Database.CurrentTransaction;
        if (currentTransaction != null)
        {
            // S40: Join existing transaction with savepoint for isolation (if supported)
            return await LifecycleTransactionScope.JoinExistingWithSavepointAsync(
                currentTransaction,
                _db.Database,
                ct);
        }

        var transaction = await _db.Database.BeginTransactionAsync(_transactionIsolationLevel, ct);
        return LifecycleTransactionScope.Own(transaction);
    }

    private sealed class LifecycleTransactionScope : IAsyncDisposable
    {
        private readonly IDbContextTransaction? _transaction;
        private readonly string? _savepointName;
        private readonly bool _ownTransaction;
        private bool _savepointReleased;

        private LifecycleTransactionScope(
            IDbContextTransaction? transaction,
            string? savepointName,
            bool ownTransaction)
        {
            _transaction = transaction;
            _savepointName = savepointName;
            _ownTransaction = ownTransaction;
            _savepointReleased = false;
        }

        public static LifecycleTransactionScope JoinExisting() => new(null, null, false);

        public static async Task<LifecycleTransactionScope> JoinExistingWithSavepointAsync(
            IDbContextTransaction transaction,
            DatabaseFacade database,
            CancellationToken ct)
        {
            // S40: Check if the database provider supports savepoints before creating one
            var relationalConnection = database.GetService<IRelationalConnection>();
            if (relationalConnection == null)
            {
                // Non-relational or unsupported provider - join without savepoint
                return JoinExisting();
            }

            // Check for an active underlying transaction
            var activeTransaction = relationalConnection.CurrentTransaction;
            if (activeTransaction == null)
            {
                // No active transaction context - join without savepoint
                return JoinExisting();
            }

            // Verify savepoint support (most relational databases support this, but check defensively)
            // Note: EF Core's IDbContextTransaction doesn't expose SupportsSavepoints directly,
            // but CreateSavepointAsync will throw if not supported. We wrap this defensively.
            try
            {
                var savepointName = $"lifecycle_sp_{Guid.NewGuid():N}";
                await transaction.CreateSavepointAsync(savepointName, ct);
                return new(transaction, savepointName, false);
            }
            catch (NotSupportedException)
            {
                // Provider doesn't support savepoints - join without savepoint isolation
                return JoinExisting();
            }
        }

        public static LifecycleTransactionScope Own(IDbContextTransaction transaction) => 
            new(transaction, null, true);

        public async Task CommitAsync(CancellationToken ct)
        {
            if (_transaction == null)
            {
                return;
            }

            if (_savepointName != null && !_savepointReleased)
            {
                // S40: Release savepoint on commit (nested transaction)
                await _transaction.ReleaseSavepointAsync(_savepointName, ct);
                _savepointReleased = true;
            }
            else if (_ownTransaction)
            {
                // Commit owned transaction
                await _transaction.CommitAsync(ct);
            }
        }

        public async Task RollbackAsync(CancellationToken ct)
        {
            if (_transaction == null)
            {
                return;
            }

            if (_savepointName != null && !_savepointReleased)
            {
                // S40: Rollback to savepoint (nested transaction)
                await _transaction.RollbackToSavepointAsync(_savepointName, ct);
                _savepointReleased = true;
            }
            else if (_ownTransaction)
            {
                // Rollback owned transaction
                await _transaction.RollbackAsync(ct);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_transaction == null)
            {
                return;
            }

            // S40: Clean up unreleased savepoint before disposal
            if (_savepointName != null && !_savepointReleased)
            {
                try
                {
                    // Rollback unreleased savepoint to prevent partial commits
                    await _transaction.RollbackToSavepointAsync(_savepointName, CancellationToken.None);
                }
                catch
                {
                    // Ignore errors during cleanup - transaction may already be disposed
                }
            }

            // Only dispose if we own the transaction
            if (_ownTransaction)
            {
                await _transaction.DisposeAsync();
            }
        }
    }

    private async Task<PaymentIntent?> ResolvePaymentIntentAsync(
        HandlePaymentResultRequest request,
        CancellationToken ct)
    {
        // Skip resolution entirely when flag is set (order-only operations)
        if (request.SkipPaymentIntentResolution ||
            request.Source == PaymentLifecycleSource.OfflineCollection)
        {
            return null;
        }

        var query = BuildPaymentIntentQuery(request.IgnoreQueryFilters);

        if (request.PaymentIntentId.HasValue)
        {
            return await query.FirstOrDefaultAsync(
                pi => pi.Id == request.PaymentIntentId.Value && !pi.IsDeleted,
                ct);
        }

        if (request.OrderId == Guid.Empty)
        {
            return null;
        }

        query = query.Where(pi => pi.OrderId == request.OrderId && !pi.IsDeleted);

        if (RequiresReviewablePaymentIntentFilter(request))
        {
            query = ApplyReviewablePaymentIntentFilter(query);
        }
        else
        {
            var provider = ResolvePaymentIntentProviderFilter(request);
            if (provider.HasValue)
            {
                query = query.Where(pi => pi.Provider == provider.Value);
            }
        }

        return await query
            .OrderByDescending(pi => pi.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<PaymentIntent?> ResolvePaymentIntentForLifecycleMutationAsync(
        HandlePaymentResultRequest request,
        CancellationToken ct)
    {
        if (request.SkipPaymentIntentResolution)
        {
            return null;
        }

        if (!IsPostgresProvider() && !IsSqlServerProvider())
        {
            var providerName = _db.Database.ProviderName ?? "Unknown";
            if (_requireRowLevelLocking)
            {
                throw new InvalidOperationException(
                    $"Database provider '{providerName}' is not supported for production payment lifecycle flows. " +
                    "Payment intent row-level locking is required before applying provider outcomes.");
            }

            _logger.LogWarning(
                "Database provider '{ProviderName}' does not support payment intent row locking. " +
                "Falling back to unlocked reads for non-production/test lifecycle flow.",
                providerName);

            return await ResolvePaymentIntentAsync(request, ct);
        }

        if (request.PaymentIntentId.HasValue)
        {
            return await LoadPaymentIntentForLifecycleMutationAsync(
                request.PaymentIntentId.Value,
                request.IgnoreQueryFilters,
                ct);
        }

        if (request.OrderId == Guid.Empty)
        {
            return null;
        }

        var provider = ResolvePaymentIntentProviderFilter(request);
        var requireReviewableIntent = RequiresReviewablePaymentIntentFilter(request);

        return await LoadLatestPaymentIntentForLifecycleMutationAsync(
            request.OrderId,
            provider,
            requireReviewableIntent,
            request.IgnoreQueryFilters,
            ct);
    }

    private async Task<PaymentIntent?> LoadPaymentIntentForLifecycleMutationAsync(
        Guid paymentIntentId,
        bool ignoreQueryFilters,
        CancellationToken ct)
    {
        IQueryable<PaymentIntent> query;

        if (IsPostgresProvider())
        {
            if (ignoreQueryFilters)
            {
                query = _db.PaymentIntents.FromSqlInterpolated($@"
                    SELECT xmin, * FROM ""PaymentIntents""
                    WHERE ""Id"" = {paymentIntentId}
                    FOR UPDATE");
            }
            else
            {
                query = _db.PaymentIntents.FromSqlInterpolated($@"
                    SELECT xmin, * FROM ""PaymentIntents""
                    WHERE ""Id"" = {paymentIntentId}
                      AND ""IsDeleted"" = false
                    FOR UPDATE");
            }
        }
        else if (IsSqlServerProvider())
        {
            if (ignoreQueryFilters)
            {
                query = _db.PaymentIntents.FromSqlInterpolated($@"
                    SELECT * FROM [PaymentIntents] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] = {paymentIntentId}");
            }
            else
            {
                query = _db.PaymentIntents.FromSqlInterpolated($@"
                    SELECT * FROM [PaymentIntents] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] = {paymentIntentId}
                      AND [IsDeleted] = 0");
            }
        }
        else
        {
            query = _db.PaymentIntents.Where(intent => intent.Id == paymentIntentId);
            
            if (!ignoreQueryFilters)
            {
                query = query.Where(intent => !intent.IsDeleted);
            }
        }

        if (ignoreQueryFilters)
        {
            query = query.IgnoreQueryFilters();
        }

        return await query.FirstOrDefaultAsync(intent => intent.Id == paymentIntentId, ct);
    }

    private async Task<PaymentIntent?> LoadLatestPaymentIntentForLifecycleMutationAsync(
        Guid orderId,
        PaymentProvider? provider,
        bool requireReviewableIntent,
        bool ignoreQueryFilters,
        CancellationToken ct)
    {
        IQueryable<PaymentIntent> query;

        if (IsPostgresProvider())
        {
            if (ignoreQueryFilters)
            {
                query = requireReviewableIntent
                    ? _db.PaymentIntents.FromSqlInterpolated($@"
                        SELECT xmin, * FROM ""PaymentIntents""
                        WHERE ""OrderId"" = {orderId}
                          AND (
                            NULLIF(BTRIM(""WalletType""), '') IS NOT NULL OR
                            NULLIF(BTRIM(""WalletPhoneNumber""), '') IS NOT NULL OR
                            NULLIF(BTRIM(""ProofImageUrl""), '') IS NOT NULL
                          )
                        ORDER BY ""CreatedAt"" DESC, ""Id"" DESC
                        LIMIT 1
                        FOR UPDATE")
                    : provider.HasValue
                    ? _db.PaymentIntents.FromSqlInterpolated($@"
                        SELECT xmin, * FROM ""PaymentIntents""
                        WHERE ""OrderId"" = {orderId}
                          AND ""Provider"" = {provider.Value}
                        ORDER BY ""CreatedAt"" DESC, ""Id"" DESC
                        LIMIT 1
                        FOR UPDATE")
                    : _db.PaymentIntents.FromSqlInterpolated($@"
                        SELECT xmin, * FROM ""PaymentIntents""
                        WHERE ""OrderId"" = {orderId}
                        ORDER BY ""CreatedAt"" DESC, ""Id"" DESC
                        LIMIT 1
                        FOR UPDATE");
            }
            else
            {
                query = requireReviewableIntent
                    ? _db.PaymentIntents.FromSqlInterpolated($@"
                        SELECT xmin, * FROM ""PaymentIntents""
                        WHERE ""OrderId"" = {orderId}
                          AND ""IsDeleted"" = false
                          AND (
                            NULLIF(BTRIM(""WalletType""), '') IS NOT NULL OR
                            NULLIF(BTRIM(""WalletPhoneNumber""), '') IS NOT NULL OR
                            NULLIF(BTRIM(""ProofImageUrl""), '') IS NOT NULL
                          )
                        ORDER BY ""CreatedAt"" DESC, ""Id"" DESC
                        LIMIT 1
                        FOR UPDATE")
                    : provider.HasValue
                    ? _db.PaymentIntents.FromSqlInterpolated($@"
                        SELECT xmin, * FROM ""PaymentIntents""
                        WHERE ""OrderId"" = {orderId}
                          AND ""Provider"" = {provider.Value}
                          AND ""IsDeleted"" = false
                        ORDER BY ""CreatedAt"" DESC, ""Id"" DESC
                        LIMIT 1
                        FOR UPDATE")
                    : _db.PaymentIntents.FromSqlInterpolated($@"
                        SELECT xmin, * FROM ""PaymentIntents""
                        WHERE ""OrderId"" = {orderId}
                          AND ""IsDeleted"" = false
                        ORDER BY ""CreatedAt"" DESC, ""Id"" DESC
                        LIMIT 1
                        FOR UPDATE");
            }
        }
        else if (IsSqlServerProvider())
        {
            if (ignoreQueryFilters)
            {
                query = requireReviewableIntent
                    ? _db.PaymentIntents.FromSqlInterpolated($@"
                        SELECT TOP(1) * FROM [PaymentIntents] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                        WHERE [OrderId] = {orderId}
                          AND (
                            NULLIF(LTRIM(RTRIM([WalletType])), '') IS NOT NULL OR
                            NULLIF(LTRIM(RTRIM([WalletPhoneNumber])), '') IS NOT NULL OR
                            NULLIF(LTRIM(RTRIM([ProofImageUrl])), '') IS NOT NULL
                          )
                        ORDER BY [CreatedAt] DESC, [Id] DESC")
                    : provider.HasValue
                    ? _db.PaymentIntents.FromSqlInterpolated($@"
                        SELECT TOP(1) * FROM [PaymentIntents] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                        WHERE [OrderId] = {orderId}
                          AND [Provider] = {provider.Value}
                        ORDER BY [CreatedAt] DESC, [Id] DESC")
                    : _db.PaymentIntents.FromSqlInterpolated($@"
                        SELECT TOP(1) * FROM [PaymentIntents] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                        WHERE [OrderId] = {orderId}
                        ORDER BY [CreatedAt] DESC, [Id] DESC");
            }
            else
            {
                query = requireReviewableIntent
                    ? _db.PaymentIntents.FromSqlInterpolated($@"
                        SELECT TOP(1) * FROM [PaymentIntents] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                        WHERE [OrderId] = {orderId}
                          AND [IsDeleted] = 0
                          AND (
                            NULLIF(LTRIM(RTRIM([WalletType])), '') IS NOT NULL OR
                            NULLIF(LTRIM(RTRIM([WalletPhoneNumber])), '') IS NOT NULL OR
                            NULLIF(LTRIM(RTRIM([ProofImageUrl])), '') IS NOT NULL
                          )
                        ORDER BY [CreatedAt] DESC, [Id] DESC")
                    : provider.HasValue
                    ? _db.PaymentIntents.FromSqlInterpolated($@"
                        SELECT TOP(1) * FROM [PaymentIntents] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                        WHERE [OrderId] = {orderId}
                          AND [Provider] = {provider.Value}
                          AND [IsDeleted] = 0
                        ORDER BY [CreatedAt] DESC, [Id] DESC")
                    : _db.PaymentIntents.FromSqlInterpolated($@"
                        SELECT TOP(1) * FROM [PaymentIntents] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                        WHERE [OrderId] = {orderId}
                          AND [IsDeleted] = 0
                        ORDER BY [CreatedAt] DESC, [Id] DESC");
            }
        }
        else
        {
            query = _db.PaymentIntents.Where(intent => intent.OrderId == orderId);
            
            if (!ignoreQueryFilters)
            {
                query = query.Where(intent => !intent.IsDeleted);
            }

            if (requireReviewableIntent)
            {
                query = ApplyReviewablePaymentIntentFilter(query);
            }
            else if (provider.HasValue)
            {
                query = query.Where(intent => intent.Provider == provider.Value);
            }

            query = query
                .OrderByDescending(intent => intent.CreatedAt)
                .ThenByDescending(intent => intent.Id);
        }

        if (ignoreQueryFilters)
        {
            query = query.IgnoreQueryFilters();
        }

        return await query.FirstOrDefaultAsync(ct);
    }

    private IQueryable<Order> BuildOrderQuery(bool ignoreQueryFilters)
    {
        var query = _db.Orders
            .Include(order => order.Items)
            .AsQueryable();

        return ignoreQueryFilters ? query.IgnoreQueryFilters() : query;
    }

    private IQueryable<PaymentIntent> BuildPaymentIntentQuery(bool ignoreQueryFilters)
    {
        var query = _db.PaymentIntents
            .Include(pi => pi.Order)
            .ThenInclude(order => order.Items)
            .AsQueryable();

        return ignoreQueryFilters ? query.IgnoreQueryFilters() : query;
    }

    private static string? ValidateSuccessState(
        Order order,
        PaymentIntent? paymentIntent,
        HandlePaymentResultRequest request)
    {
        return request.Source switch
        {
            PaymentLifecycleSource.OfflineCollection when order.PaymentStatus != PaymentStatus.Pending
                => $"Cannot collect COD: current status is {order.PaymentStatus}",
            PaymentLifecycleSource.PaymentReviewApproval
                => ValidateWalletApprovalState(order, paymentIntent),
            _ when order.PaymentStatus != PaymentStatus.Pending &&
                     order.PaymentStatus != PaymentStatus.Authorized
                => $"Cannot capture payment: current status is {order.PaymentStatus}",
            _ => null
        };
    }

    private static string? ValidateFailureState(
        Order order,
        PaymentIntent? paymentIntent,
        HandlePaymentResultRequest request,
        PaymentStatus targetStatus)
    {
        if (request.Source == PaymentLifecycleSource.PaymentReviewRejection)
        {
            return ValidateWalletRejectionState(order, paymentIntent);
        }

        if (order.PaymentStatus == targetStatus)
        {
            return null;
        }

        if (order.PaymentStatus != PaymentStatus.Pending &&
            order.PaymentStatus != PaymentStatus.Authorized &&
            order.PaymentStatus != PaymentStatus.AwaitingVerification)
        {
            return $"Cannot mark as failed: current status is {order.PaymentStatus}";
        }

        return null;
    }

    private static string? ValidateWalletApprovalState(Order order, PaymentIntent? paymentIntent)
    {
        if (!IsReviewablePaymentIntent(paymentIntent))
        {
            return "Cannot approve: manual wallet payment intent not found";
        }

        if (!IsWalletPending(order, paymentIntent))
        {
            if (paymentIntent.Status != PaymentIntentStatus.Paid ||
                order.PaymentStatus is not (PaymentStatus.Pending or PaymentStatus.AwaitingVerification))
            {
                return $"Cannot approve: current status is {order.PaymentStatus}";
            }
        }

        if (!HasTrustedManualWalletProof(paymentIntent.ProofImageUrl, order.TenantId))
        {
            return "Cannot approve: trusted manual wallet proof is required";
        }

        return null;
    }

    private static PaymentTransitionResult? ValidateManualWalletExpirationAuthority(
        PaymentIntent? paymentIntent,
        DateTime observedAtUtc,
        bool allowElapsed)
    {
        if (!IsReviewablePaymentIntent(paymentIntent))
        {
            return null;
        }

        if (!paymentIntent.ExpiresAtUtc.HasValue)
        {
            return PaymentTransitionResult.Failed(
                "Manual Wallet expiration is required.",
                "MANUAL_WALLET_EXPIRATION_MISSING");
        }

        var expiresAtUtc = paymentIntent.ExpiresAtUtc.Value;
        if (expiresAtUtc == default)
        {
            return PaymentTransitionResult.Failed(
                "Manual Wallet expiration is invalid.",
                "MANUAL_WALLET_EXPIRATION_INVALID");
        }

        if (paymentIntent.CreatedAt != default && expiresAtUtc <= paymentIntent.CreatedAt)
        {
            return PaymentTransitionResult.Failed(
                "Manual Wallet expiration clock authority is inconsistent.",
                "MANUAL_WALLET_CLOCK_INCONSISTENT");
        }

        if (observedAtUtc < paymentIntent.CreatedAt)
        {
            return PaymentTransitionResult.Failed(
                "Manual Wallet expiration clock authority is inconsistent.",
                "MANUAL_WALLET_CLOCK_INCONSISTENT");
        }

        if (!allowElapsed && expiresAtUtc <= observedAtUtc)
        {
            return PaymentTransitionResult.Failed(
                "Manual Wallet payment is expired.",
                "MANUAL_WALLET_EXPIRED");
        }

        return null;
    }

    private static string? ValidateWalletRejectionState(Order order, PaymentIntent? paymentIntent)
    {
        if (!IsReviewablePaymentIntent(paymentIntent))
        {
            return "Cannot reject: manual wallet payment intent not found";
        }

        if (paymentIntent.Status == PaymentIntentStatus.Paid ||
            order.PaymentStatus == PaymentStatus.Collected)
        {
            return "Cannot reject: payment already collected";
        }

        if (!IsWalletPending(order, paymentIntent))
        {
            return $"Cannot reject: current status is {order.PaymentStatus}";
        }

        return null;
    }

    private static bool HasTrustedManualWalletProof(string? proofImageUrl, Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(proofImageUrl))
        {
            return false;
        }

        var normalized = proofImageUrl.Trim().Replace('\\', '/').TrimStart('/');
        
        // URL-decode to detect encoded traversal sequences like %2e%2e
        try
        {
            normalized = Uri.UnescapeDataString(normalized);
        }
        catch
        {
            // Treat decode failures as invalid input
            return false;
        }
        
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }
        
        // Re-normalize after decoding
        normalized = normalized.Trim().Replace('\\', '/').TrimStart('/');
        
        if (Uri.TryCreate(normalized, UriKind.Absolute, out _))
        {
            return false;
        }

        if (normalized.Split('/').Any(segment => string.Equals(segment, "..", StringComparison.Ordinal)))
        {
            return false;
        }

        var tenantScopedPrefix = $"tenants/{tenantId:D}/manual-wallet-proofs/";
        return normalized.StartsWith(tenantScopedPrefix, StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("manual-payments/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOrderSuccessfulTerminal(Order order)
    {
        return order.PaymentStatus is PaymentStatus.Collected or PaymentStatus.Paid or PaymentStatus.Settled;
    }

    private static bool IsProviderCallbackSource(PaymentLifecycleSource source)
    {
        return source is PaymentLifecycleSource.GatewayWebhook;
    }

    private static bool IsTerminalSqlPaymentTruth(PaymentStatus status)
    {
        return status is PaymentStatus.Collected
            or PaymentStatus.Paid
            or PaymentStatus.Settled
            or PaymentStatus.Refunded
            or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Failed
            or PaymentStatus.Rejected
            or PaymentStatus.Expired
            or PaymentStatus.AmountMismatch;
    }

    private static bool IsTerminalNegativePaymentIntent(PaymentIntentStatus status)
    {
        return status is PaymentIntentStatus.Failed
            or PaymentIntentStatus.Cancelled
            or PaymentIntentStatus.Rejected
            or PaymentIntentStatus.Expired
            or PaymentIntentStatus.AmountMismatch
            or PaymentIntentStatus.Suspicious;
    }

    private async Task<PaymentTransitionResult?> TryRejectSupersededTerminalAttemptAsync(
        Order order,
        PaymentIntent? paymentIntent,
        HandlePaymentResultRequest request,
        CancellationToken ct)
    {
        if (!IsProviderCallbackSource(request.Source) ||
            paymentIntent == null ||
            !IsTerminalNegativePaymentIntent(paymentIntent.Status))
        {
            return null;
        }

        var provider = ResolvePaymentIntentProviderFilter(request);
        if (!provider.HasValue)
        {
            return null;
        }

        var siblingAttempts = await _db.PaymentIntents
            .IgnoreQueryFilters()
            .Where(intent =>
                intent.TenantId == paymentIntent.TenantId &&
                intent.OrderId == paymentIntent.OrderId &&
                intent.Provider == provider.Value &&
                !intent.IsDeleted &&
                intent.Id != paymentIntent.Id)
            .Select(intent => new { intent.Id, intent.CreatedAt })
            .ToListAsync(ct);
        var hasNewerAttemptLineage = siblingAttempts.Any(intent =>
            intent.CreatedAt > paymentIntent.CreatedAt ||
            (intent.CreatedAt == paymentIntent.CreatedAt && intent.Id.CompareTo(paymentIntent.Id) > 0));

        if (!hasNewerAttemptLineage)
        {
            return null;
        }

        _logger.LogWarning(
            "[PAYMENT-LIFECYCLE] Superseded provider outcome rejected | OrderId: {OrderId} | PaymentIntentId: {PaymentIntentId} | PaymentIntentStatus: {PaymentIntentStatus} | Outcome: {Outcome} | Gateway: {Gateway} | WebhookEventId: {WebhookEventId}",
            order.Id,
            paymentIntent.Id,
            paymentIntent.Status,
            request.Outcome,
            request.Gateway,
            request.WebhookEventId);

        if (request.Outcome == PaymentLifecycleOutcome.Succeeded)
        {
            await EnqueueQuarantinedCapturedPaymentSuspenseAsync(
                order,
                request,
                businessEntityId: order.Id,
                ct);

            return PaymentTransitionResult.ManualReviewRequired(
                order.PaymentStatus,
                "Provider reported success for a superseded terminal payment attempt. The newer active/successful attempt remains authoritative.",
                "SUPERSEDED_PAYMENT_ATTEMPT_SUCCESS_REJECTED");
        }

        return PaymentTransitionResult.Succeeded(
            order.PaymentStatus,
            wasIdempotent: true,
            stateChanged: false,
            inventoryAction: PaymentInventoryAction.None);
    }
    private PaymentTransitionResult? TryRejectStaleProviderSuccessAgainstTerminalSqlTruth(
        Order order,
        PaymentIntent? paymentIntent,
        HandlePaymentResultRequest request)
    {
        if (request.Outcome != PaymentLifecycleOutcome.Succeeded ||
            !IsProviderCallbackSource(request.Source) ||
            IsOrderSuccessfulTerminal(order) ||
            order.PaymentStatus == PaymentStatus.Pending ||
            order.PaymentStatus == PaymentStatus.Authorized ||
            order.PaymentStatus == PaymentStatus.AwaitingVerification)
        {
            return null;
        }

        if (!IsTerminalSqlPaymentTruth(order.PaymentStatus))
        {
            return null;
        }

        _logger.LogWarning(
            "[PAYMENT-LIFECYCLE] Stale provider success rejected against terminal SQL truth | OrderId: {OrderId} | Source: {Source} | PaymentStatus: {PaymentStatus} | PaymentIntentStatus: {PaymentIntentStatus} | WebhookEventId: {WebhookEventId}",
            order.Id,
            request.Source,
            order.PaymentStatus,
            paymentIntent?.Status,
            request.WebhookEventId);

        return PaymentTransitionResult.ManualReviewRequired(
            order.PaymentStatus,
            $"Provider reported success after SQL payment truth was already terminal ({order.PaymentStatus}). The callback was recorded as external evidence and must not mutate payment, fulfillment, accounting, payout, or order authority.",
            "STALE_PROVIDER_SUCCESS_REJECTED");
    }

    private PaymentTransitionResult? TryCollapseStaleProviderFailureAgainstTerminalSqlTruth(
        Order order,
        PaymentIntent? paymentIntent,
        HandlePaymentResultRequest request,
        PaymentStatus targetStatus)
    {
        if (!IsProviderCallbackSource(request.Source) ||
            request.Outcome == PaymentLifecycleOutcome.Succeeded ||
            order.PaymentStatus == targetStatus ||
            order.PaymentStatus == PaymentStatus.Pending ||
            order.PaymentStatus == PaymentStatus.Authorized ||
            order.PaymentStatus == PaymentStatus.AwaitingVerification)
        {
            return null;
        }

        if (!IsTerminalSqlPaymentTruth(order.PaymentStatus))
        {
            return null;
        }

        _logger.LogWarning(
            "[PAYMENT-LIFECYCLE] Stale provider failure collapsed against terminal SQL truth | OrderId: {OrderId} | Source: {Source} | PaymentStatus: {PaymentStatus} | PaymentIntentStatus: {PaymentIntentStatus} | RequestedOutcome: {Outcome} | WebhookEventId: {WebhookEventId}",
            order.Id,
            request.Source,
            order.PaymentStatus,
            paymentIntent?.Status,
            request.Outcome,
            request.WebhookEventId);

        return PaymentTransitionResult.Succeeded(
            NormalizeSuccessfulStatus(order.PaymentStatus),
            wasIdempotent: true,
            stateChanged: false,
            inventoryAction: PaymentInventoryAction.None);
    }

    private static bool IsMatchingIntentTerminal(
        PaymentIntent? paymentIntent,
        PaymentLifecycleOutcome outcome)
    {
        if (paymentIntent == null)
        {
            return true;
        }

        return outcome switch
        {
            PaymentLifecycleOutcome.Rejected => paymentIntent.Status == PaymentIntentStatus.Rejected,
            PaymentLifecycleOutcome.Cancelled => paymentIntent.Status == PaymentIntentStatus.Cancelled,
            PaymentLifecycleOutcome.Expired => paymentIntent.Status == PaymentIntentStatus.Expired,
            PaymentLifecycleOutcome.Failed => paymentIntent.Status == PaymentIntentStatus.Failed,
            _ => false
        };
    }

    private static bool IsWalletPending(Order order, PaymentIntent? paymentIntent)
    {
        return order.PaymentStatus is PaymentStatus.Pending or PaymentStatus.AwaitingVerification &&
               paymentIntent?.Status is null or PaymentIntentStatus.Pending or PaymentIntentStatus.AwaitingVerification;
    }

    private static bool IsReviewablePaymentIntent(PaymentIntent? paymentIntent)
    {
        return paymentIntent is not null &&
               (!string.IsNullOrWhiteSpace(paymentIntent.WalletType) ||
                !string.IsNullOrWhiteSpace(paymentIntent.WalletPhoneNumber) ||
                !string.IsNullOrWhiteSpace(paymentIntent.ProofImageUrl));
    }

    private static PaymentStatus NormalizeSuccessfulStatus(PaymentStatus status)
    {
        return status == PaymentStatus.Paid ? PaymentStatus.Collected : status;
    }

    private async Task<(PaymentInventoryAction Action, decimal Quantity)> FinalizeReplayInventoryAsync(
        Order order,
        PaymentIntent? paymentIntent,
        PaymentLifecycleOutcome outcome,
        CancellationToken ct)
    {
        var remainingReservedQuantity = await GetOpenReservationQuantityAsync(order.TenantId, order.Id, ct);
        
        // ----------------------------------------------------------------------
        // CRITICAL: Always return Commit/Release based on outcome, even if inventory
        // was already finalized (remainingReservedQuantity <= 0).
        // ----------------------------------------------------------------------
        // Scenario: legacy state may already have finalized inventory before a replay.
        // Return the semantic action so callers can log the repaired lifecycle consistently;
        // authoritative outcome events are now staged in the source transaction.
        
        if (outcome == PaymentLifecycleOutcome.Succeeded)
        {
            if (remainingReservedQuantity > 0m)
            {
                await _stockRepository.CommitOrderReservationsAsync(order.TenantId, order.Id, ct);
                if (paymentIntent != null)
                {
                    paymentIntent.InventoryCommitted = true;
                    await _db.SaveChangesAsync(ct);
                }
            }

            return (PaymentInventoryAction.Commit, remainingReservedQuantity);
        }

        if (remainingReservedQuantity > 0m)
        {
            var inventoryReleased = await _stockRepository.ReleaseOrderReservationsAsync(order.TenantId, order.Id, ct);
            if (inventoryReleased && paymentIntent != null)
            {
                paymentIntent.InventoryCommitted = false;
                await _db.SaveChangesAsync(ct);
            }
        }

        return (PaymentInventoryAction.Release, remainingReservedQuantity);
    }

    private async Task<decimal> GetOpenReservationQuantityAsync(Guid tenantId, Guid orderId, CancellationToken ct)
    {
        return await _db.InventoryReservations
            .Where(reservation =>
                reservation.TenantId == tenantId &&
                reservation.OrderId == orderId &&
                !reservation.IsDeleted)
            .Select(reservation => (decimal?)(reservation.ReservedQuantity - reservation.CommittedQuantity - reservation.ReleasedQuantity))
            .SumAsync(ct) ?? 0m;
    }

    private async Task<ReservationPaymentValidationResult> ValidateReservationCanAcceptPaymentAsync(
        Order order,
        HandlePaymentResultRequest request,
        DateTime now,
        CancellationToken ct)
    {
        if (!RequiresActiveReservationForSuccessfulPayment(request.Source))
        {
            return ReservationPaymentValidationResult.Valid();
        }

        var reservations = await LoadOrderReservationsForPaymentMutationAsync(order.TenantId, order.Id, ct);
        var openReservations = reservations
            .Where(reservation => reservation.RemainingReservedQuantity > 0m)
            .ToList();

        IReadOnlyList<OrderItem> physicalItems;
        try { physicalItems = OrderInventoryReservationSnapshot.PhysicalItems(order); }
        catch (InvalidOperationException)
        {
            return ReservationPaymentValidationResult.Invalid("INVENTORY_RESERVATION_SNAPSHOT_INVALID",
                "Original physical inventory requirements need reconciliation.");
        }
        var expectedQuantity = physicalItems
            .Sum(item => (decimal)item.Quantity);

        if (expectedQuantity <= 0m)
        {
            // Only a complete original snapshot can grant this exemption; legacy
            // orders still take the previous strict all-line requirement.
            if (order.Items.Any(item => !item.IsDeleted && item.Quantity > 0) && openReservations.Count == 0)
                return ReservationPaymentValidationResult.Valid();
            return ReservationPaymentValidationResult.Invalid(
                "NO_STOCK_BACKED_ITEMS",
                "Order has no stock-backed items to validate before accepting payment.");
        }

        if (openReservations.Count == 0)
        {
            return ReservationPaymentValidationResult.Invalid(
                "RESERVATION_MISSING",
                "Payment succeeded after the inventory reservation was already released or missing.");
        }

        var openQuantity = openReservations.Sum(reservation => reservation.RemainingReservedQuantity);
        if (openQuantity < expectedQuantity)
        {
            return ReservationPaymentValidationResult.Invalid(
                "RESERVATION_QUANTITY_SHORT",
                $"Open reserved quantity {openQuantity} is less than order quantity {expectedQuantity}.");
        }

        if (physicalItems.Any(item => openReservations.Where(r => r.OrderItemId == item.Id &&
                    r.ProductId == item.ProductId && r.VariantId == item.VariantId &&
                    (!item.WarehouseId.HasValue || r.WarehouseId == item.WarehouseId.Value))
                .Sum(r => r.RemainingReservedQuantity) != item.Quantity) ||
            openReservations.Any(r => !physicalItems.Any(item => item.Id == r.OrderItemId &&
                item.ProductId == r.ProductId && item.VariantId == r.VariantId &&
                (!item.WarehouseId.HasValue || item.WarehouseId == r.WarehouseId))))
            return ReservationPaymentValidationResult.Invalid("RESERVATION_LINEAGE_MISMATCH",
                "Open reservations do not exactly cover the original physical order lines.");

        var expiredReservation = openReservations.FirstOrDefault(reservation =>
            reservation.ExpiresAtUtc.HasValue &&
            reservation.ExpiresAtUtc.Value <= now);

        if (expiredReservation != null)
        {
            return ReservationPaymentValidationResult.Invalid(
                "RESERVATION_EXPIRED",
                $"Inventory reservation expired at {expiredReservation.ExpiresAtUtc:O}.");
        }

        var stockAllocated = await ValidateReservedStockStillAllocatedAsync(order.TenantId, openReservations, ct);
        if (!stockAllocated)
        {
            return ReservationPaymentValidationResult.Invalid(
                "RESERVED_STOCK_NOT_ALLOCATED",
                "Reserved stock quantity is no longer allocated on the stock rows.");
        }

        return ReservationPaymentValidationResult.Valid();
    }

    private async Task<(PaymentTransitionResult Result, CouponReleaseDecision CouponReleaseDecision)> QuarantineLateSuccessfulPaymentAsync(
        Order order,
        PaymentIntent? paymentIntent,
        HandlePaymentResultRequest request,
        DateTime now,
        string reason,
        string failureCode,
        CancellationToken ct)
    {
        var provider = ResolveGateway(order, request);
        var correlationId = _correlationIdAccessor.GetCorrelationId();
        CouponReleaseDecision couponReleaseDecision = CouponReleaseDecision.None();

        _logger.LogCritical(
            "[PAYMENT-LIFECYCLE] Late successful payment quarantined | OrderId: {OrderId} | OrderNumber: {OrderNumber} | Provider: {Provider} | FailureCode: {FailureCode} | Reason: {Reason} | PaymentReference: {PaymentReference}",
            order.Id,
            order.OrderNumber,
            provider,
            failureCode,
            reason,
            request.PaymentReference ?? order.PaymentReference);

        // Capture old statuses before mutation for compatibility events
        var oldPaymentStatus = order.PaymentStatus;
        var oldOrderStatus = order.Status;

        PaymentStatusSaveChangesInterceptor.EnableAuthorizedContext();
        try
        {
            using var orderStatusScope = OrderStatusSaveChangesInterceptor.BeginAuthorizedContext();
            TransitionCheckoutState(order, CheckoutState.Expired);
            order.PaymentStatus = PaymentStatus.Expired;
            order.AcceptPaymentStatusChange();
            order.IsPaid = false;
            order.PaidAt = null;
            order.Status = OrderStatus.OnHold;
            order.CancellationReason = "Provider reported successful payment after inventory reservation expiry; refund or manual reconciliation required.";
            order.PaymentGateway = provider;
            order.PaymentReference = request.PaymentReference ?? order.PaymentReference;
            AppendAdminNote(order, reason, "payment-lifecycle", now, PaymentLifecycleOutcome.Rejected);

            if (paymentIntent != null)
            {
                paymentIntent.Status = PaymentIntentStatus.Suspicious;
                paymentIntent.ProviderPaymentId = request.ProviderPaymentId ?? paymentIntent.ProviderPaymentId;
                paymentIntent.CapturedAt ??= now;
                paymentIntent.FailedAt = now;
                paymentIntent.FailureCode = failureCode;
                paymentIntent.FailureReason = $"Provider success quarantined after POS reservation expiry: {reason}";
                paymentIntent.LastWebhookEventId = request.WebhookEventId ?? paymentIntent.LastWebhookEventId;
                paymentIntent.WebhookEventCount++;
                paymentIntent.InventoryCommitted = false;
            }

            if (!string.Equals(failureCode, "RESERVED_STOCK_NOT_ALLOCATED", StringComparison.Ordinal))
            {
                var reservedQuantityToRelease = await GetOpenReservationQuantityAsync(order.TenantId, order.Id, ct);
                if (reservedQuantityToRelease > 0m)
                {
                    var inventoryReleased = await _stockRepository.ReleaseOrderReservationsAsync(order.TenantId, order.Id, ct);
                    if (inventoryReleased && reservedQuantityToRelease > 0m)
                    {
                        _inventoryReservationMetrics.RecordRelease(reservedQuantityToRelease, "payment_late_quarantine");
                    }
                }
            }

            couponReleaseDecision = await _couponValidationService.ReleaseCouponReservationAsync(
                order.TenantId,
                order.Id,
                correlationId,
                failureCode,
                ct);

            await EnqueueQuarantinedCapturedPaymentSuspenseAsync(
                order,
                request,
                businessEntityId: order.Id,
                ct);

            // Publish compatibility events for status changes before SaveChanges
            // so the outbox entries are persisted atomically with the order mutations
            await PublishCompatibilityPaymentStatusChangedEventAsync(
                order,
                paymentIntent,
                request,
                oldPaymentStatus,
                amountPaid: null,
                now,
                ct);
            await PublishCompatibilityOrderStatusChangedEventAsync(
                order,
                request,
                oldOrderStatus,
                now,
                ct);

            await _db.SaveChangesAsync(ct);
        }
        finally
        {
            PaymentStatusSaveChangesInterceptor.DisableAuthorizedContext();
        }

        _reliabilityTelemetry?.RecordPaymentAfterExpiryDetected(provider, failureCode);
        _reliabilityTelemetry?.RecordWebhookLatePayment(provider, failureCode);

        return (
            PaymentTransitionResult.ManualReviewRequired(
                PaymentStatus.Expired,
                reason,
                failureCode),
            couponReleaseDecision);
    }

    private async Task<List<InventoryReservation>> LoadOrderReservationsForPaymentMutationAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken ct)
    {
        if (IsPostgresProvider())
        {
            return await _db.InventoryReservations.FromSqlInterpolated($@"
                SELECT * FROM ""InventoryReservations""
                WHERE ""TenantId"" = {tenantId}
                  AND ""OrderId"" = {orderId}
                  AND ""IsDeleted"" = false
                ORDER BY ""CreatedAt"", ""Id""
                FOR UPDATE")
                .IgnoreQueryFilters()
                .ToListAsync(ct);
        }

        if (IsSqlServerProvider())
        {
            return await _db.InventoryReservations.FromSqlInterpolated($@"
                SELECT * FROM [InventoryReservations] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [TenantId] = {tenantId}
                  AND [OrderId] = {orderId}
                  AND [IsDeleted] = 0
                ORDER BY [CreatedAt], [Id]")
                .IgnoreQueryFilters()
                .ToListAsync(ct);
        }

        // Unsupported database provider - cannot guarantee row-level locking for payment safety
        var providerName = _db.Database.ProviderName ?? "Unknown";
        
        if (_requireRowLevelLocking)
        {
            throw new InvalidOperationException(
                $"Database provider '{providerName}' is not supported for production payment flows. " +
                "Row-level locking is required to prevent race conditions during concurrent payment attempts. " +
                "Supported providers: PostgreSQL (Npgsql), SQL Server (Microsoft.EntityFrameworkCore.SqlServer).");
        }
        
        _logger.LogWarning(
            "Database provider '{ProviderName}' does not support row-level locking. " +
            "Payment flows may be vulnerable to race conditions in concurrent scenarios. " +
            "This configuration should only be used in test environments.",
            providerName);
        
        // Perform unlocked read for unsupported providers (soft-fallback for test environments)
        return await _db.InventoryReservations
            .Where(r => r.TenantId == tenantId && r.OrderId == orderId && !r.IsDeleted)
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .IgnoreQueryFilters()
            .ToListAsync(ct);
    }

    private async Task<bool> ValidateReservedStockStillAllocatedAsync(
        Guid tenantId,
        IReadOnlyCollection<InventoryReservation> openReservations,
        CancellationToken ct)
    {
        foreach (var group in openReservations
                     .GroupBy(reservation => new { reservation.VariantId, reservation.WarehouseId }))
        {
            var requiredReserved = group.Sum(reservation => reservation.RemainingReservedQuantity);
            var stock = await LoadStockForPaymentMutationAsync(tenantId, group.Key.VariantId, group.Key.WarehouseId, ct);

            if (stock == null || stock.ReservedQuantity < requiredReserved)
            {
                return false;
            }
        }

        return true;
    }

    private async Task<ProductVariantStock?> LoadStockForPaymentMutationAsync(
        Guid tenantId,
        Guid variantId,
        Guid warehouseId,
        CancellationToken ct)
    {
        if (IsPostgresProvider())
        {
            return await _db.ProductVariantStocks.FromSqlInterpolated($@"
                SELECT * FROM ""ProductVariantStocks""
                WHERE ""TenantId"" = {tenantId}
                  AND ""VariantId"" = {variantId}
                  AND ""WarehouseId"" = {warehouseId}
                  AND ""IsDeleted"" = false
                FOR UPDATE")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(ct);
        }

        if (IsSqlServerProvider())
        {
            return await _db.ProductVariantStocks.FromSqlInterpolated($@"
                SELECT * FROM [ProductVariantStocks] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [TenantId] = {tenantId}
                  AND [VariantId] = {variantId}
                  AND [WarehouseId] = {warehouseId}
                  AND [IsDeleted] = 0")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(ct);
        }

        return await _db.ProductVariantStocks
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(stock =>
                stock.TenantId == tenantId &&
                stock.VariantId == variantId &&
                stock.WarehouseId == warehouseId &&
                !stock.IsDeleted,
                ct);
    }

    private static bool RequiresActiveReservationForSuccessfulPayment(PaymentLifecycleSource source)
    {
        return source is PaymentLifecycleSource.GatewayWebhook;
    }

    private bool IsPostgresProvider() =>
        _db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private bool IsSqlServerProvider() =>
        _db.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;

    private static decimal ResolvePaymentAmount(
        Order order,
        PaymentIntent? paymentIntent,
        HandlePaymentResultRequest request)
    {
        if (RequiresProviderAuthority(request))
        {
            if (request.ProviderAmount.HasValue && request.ProviderAmount.Value > 0m)
            {
                return request.ProviderAmount.Value;
            }

            throw new InvalidOperationException(
                "Provider-authoritative payment amount is required for online payment success.");
        }

        if (request.Amount.HasValue && request.Amount.Value > 0m)
        {
            return request.Amount.Value;
        }

        if (paymentIntent?.Amount > 0m)
        {
            return paymentIntent.Amount;
        }

        return order.Total;
    }

    private async Task<PaymentTransitionResult?> TryQuarantineProviderAuthorityMismatchAsync(
        Order order,
        PaymentIntent? paymentIntent,
        HandlePaymentResultRequest request,
        DateTime now,
        CancellationToken ct)
    {
        if (!RequiresProviderAuthority(request))
        {
            return null;
        }

        var provider = ResolveGateway(order, request);
        var storedAmount = NormalizeMoney(paymentIntent?.Amount > 0m ? paymentIntent.Amount : order.Total, order.Currency);
        var providerAmount = request.ProviderAmount.HasValue
            ? NormalizeMoney(request.ProviderAmount.Value, order.Currency)
            : (decimal?)null;
        var storedCurrency = NormalizeCurrency(paymentIntent?.Currency ?? order.Currency);
        var providerCurrency = NormalizeCurrency(request.ProviderCurrency);

        string? mismatchType = null;
        if (!providerAmount.HasValue || providerAmount.Value <= 0m)
        {
            mismatchType = "provider_amount_missing";
        }
        else if (providerAmount.Value != storedAmount)
        {
            mismatchType = "amount";
        }
        else if (string.IsNullOrWhiteSpace(providerCurrency))
        {
            mismatchType = "provider_currency_missing";
        }
        else if (!string.Equals(providerCurrency, storedCurrency, StringComparison.OrdinalIgnoreCase))
        {
            mismatchType = "currency";
        }

        if (mismatchType == null)
        {
            request.Amount = providerAmount;
            request.Currency = providerCurrency;
            return null;
        }

        var reason =
            $"Provider payment authority mismatch ({mismatchType}). Expected {storedAmount} {storedCurrency}; received {(providerAmount.HasValue ? providerAmount.Value.ToString("0.####") : "missing")} {providerCurrency ?? "missing"}.";

        _logger.LogCritical(
            "[PAYMENT-LIFECYCLE] Provider payment authority mismatch quarantined | TenantId: {TenantId} | OrderId: {OrderId} | OrderNumber: {OrderNumber} | Provider: {Provider} | MismatchType: {MismatchType} | ExpectedAmount: {ExpectedAmount} | ProviderAmount: {ProviderAmount} | ExpectedCurrency: {ExpectedCurrency} | ProviderCurrency: {ProviderCurrency} | PaymentIntentId: {PaymentIntentId} | WebhookEventId: {WebhookEventId}",
            order.TenantId,
            order.Id,
            order.OrderNumber,
            provider,
            mismatchType,
            storedAmount,
            providerAmount,
            storedCurrency,
            providerCurrency,
            paymentIntent?.Id,
            request.WebhookEventId);

        // Capture old statuses before mutation for compatibility events
        var oldPaymentStatus = order.PaymentStatus;
        var oldOrderStatus = order.Status;

        PaymentStatusSaveChangesInterceptor.EnableAuthorizedContext();
        try
        {
            using var orderStatusScope = OrderStatusSaveChangesInterceptor.BeginAuthorizedContext();
            TransitionCheckoutState(order, CheckoutState.Failed);
            order.PaymentStatus = PaymentStatus.AmountMismatch;
            order.AcceptPaymentStatusChange();
            order.IsPaid = false;
            order.PaidAt = null;
            order.Status = OrderStatus.OnHold;
            order.PaymentGateway = provider;
            order.PaymentReference = request.PaymentReference ?? order.PaymentReference;
            AppendAdminNote(order, reason, "payment-lifecycle", now, PaymentLifecycleOutcome.AmountMismatch);

            if (paymentIntent != null)
            {
                paymentIntent.Status = PaymentIntentStatus.AmountMismatch;
                paymentIntent.ProviderPaymentId = request.ProviderPaymentId ?? paymentIntent.ProviderPaymentId;
                paymentIntent.FailedAt = now;
                paymentIntent.FailureCode = "PAYMENT_AMOUNT_MISMATCH";
                paymentIntent.FailureReason = reason;
                paymentIntent.LastWebhookEventId = request.WebhookEventId ?? paymentIntent.LastWebhookEventId;
                paymentIntent.WebhookEventCount++;
                paymentIntent.InventoryCommitted = false;
            }

            await EnqueueQuarantinedCapturedPaymentSuspenseAsync(
                order,
                request,
                businessEntityId: order.Id,
                ct);

            // Publish compatibility events for status changes before SaveChanges
            // so the outbox entries are persisted atomically with the order mutations
            await PublishCompatibilityPaymentStatusChangedEventAsync(
                order,
                paymentIntent,
                request,
                oldPaymentStatus,
                amountPaid: null,
                now,
                ct);
            await PublishCompatibilityOrderStatusChangedEventAsync(
                order,
                request,
                oldOrderStatus,
                now,
                ct);

            await _db.SaveChangesAsync(ct);
        }
        finally
        {
            PaymentStatusSaveChangesInterceptor.DisableAuthorizedContext();
        }

        _reliabilityTelemetry?.RecordPaymentAmountMismatch(order.TenantId, provider, mismatchType);

        return PaymentTransitionResult.ManualReviewRequired(
            PaymentStatus.AmountMismatch,
            reason,
            "PAYMENT_AMOUNT_MISMATCH");
    }

    private async Task EnqueueQuarantinedCapturedPaymentSuspenseAsync(
        Order order,
        HandlePaymentResultRequest request,
        Guid businessEntityId,
        CancellationToken ct)
    {
        var providerAmount = request.ProviderAmount ?? request.Amount;
        if (!providerAmount.HasValue || providerAmount.Value <= 0m)
        {
            return;
        }

        var providerCurrency = request.ProviderCurrency ??
                               request.Currency ??
                               order.Currency;

        if (string.IsNullOrWhiteSpace(providerCurrency) ||
            string.IsNullOrWhiteSpace(request.WebhookEventId))
        {
            return;
        }

        await _accountingOutbox.EnqueueUnresolvedOnlinePaymentAsync(
            order.TenantId,
            providerAmount.Value,
            providerCurrency,
            ResolveGateway(order, request),
            request.WebhookEventId,
            businessEntityId,
            request.PaymentReference ?? order.PaymentReference,
            ct);
    }

    private static bool RequiresProviderAuthority(HandlePaymentResultRequest request)
    {
        if (request.Outcome != PaymentLifecycleOutcome.Succeeded)
        {
            return false;
        }

        return request.Source is PaymentLifecycleSource.GatewayWebhook;
    }

    private static bool RequiresReviewablePaymentIntentFilter(HandlePaymentResultRequest request)
    {
        return request.Source is PaymentLifecycleSource.PaymentReviewApproval or PaymentLifecycleSource.PaymentReviewRejection;
    }

    private static IQueryable<PaymentIntent> ApplyReviewablePaymentIntentFilter(IQueryable<PaymentIntent> query)
    {
        return query.Where(paymentIntent =>
            paymentIntent.WalletType != null && paymentIntent.WalletType != string.Empty ||
            paymentIntent.WalletPhoneNumber != null && paymentIntent.WalletPhoneNumber != string.Empty ||
            paymentIntent.ProofImageUrl != null && paymentIntent.ProofImageUrl != string.Empty);
    }

    private static PaymentProvider? ResolvePaymentIntentProviderFilter(HandlePaymentResultRequest request)
    {
        if (request.Source == PaymentLifecycleSource.GatewayWebhook &&
            TryResolveGatewayPaymentProvider(request.Gateway, out var provider))
        {
            return provider;
        }

        return null;
    }

    private static bool TryResolveGatewayPaymentProvider(string? gateway, out PaymentProvider provider)
    {
        switch (gateway?.Trim().ToUpperInvariant())
        {
            case "STRIPE":
                provider = PaymentProvider.Stripe;
                return true;
            case "PAYMOB":
            case "CARD":
            case "VISA":
                provider = PaymentProvider.Paymob;
                return true;
            case "FAWRY":
                provider = PaymentProvider.Fawry;
                return true;
            default:
                provider = default;
                return false;
        }
    }

    private static bool IsAmountMismatchTerminal(Order order, PaymentIntent? paymentIntent) =>
        order.PaymentStatus == PaymentStatus.AmountMismatch ||
        paymentIntent?.Status == PaymentIntentStatus.AmountMismatch;

    private static bool IsLateSuccessfulPaymentQuarantineTerminal(Order order, PaymentIntent? paymentIntent) =>
        order.PaymentStatus == PaymentStatus.Expired &&
        order.Status == OrderStatus.OnHold &&
        paymentIntent?.Status == PaymentIntentStatus.Suspicious &&
        paymentIntent.FailureCode is
            "NO_STOCK_BACKED_ITEMS" or
            "RESERVATION_MISSING" or
            "RESERVATION_QUANTITY_SHORT" or
            "RESERVATION_EXPIRED" or
            "RESERVED_STOCK_NOT_ALLOCATED" or
            "LATE_PROVIDER_SUCCESS_RECONCILIATION_REQUIRED";

    private static string ResolveLateSuccessfulPaymentQuarantineReason(PaymentIntent paymentIntent)
    {
        const string reasonPrefix = "Provider success quarantined after POS reservation expiry: ";
        return paymentIntent.FailureReason?.StartsWith(reasonPrefix, StringComparison.Ordinal) == true
            ? paymentIntent.FailureReason[reasonPrefix.Length..]
            : "Provider reported a successful payment after POS released the inventory reservation. Refund or manual reconciliation is required.";
    }

    private static bool IsAdminCancellationPending(Order order) =>
        order.Status == OrderStatus.OnHold &&
        !string.IsNullOrWhiteSpace(order.AdminNotes) &&
        order.AdminNotes.Contains("[admin-cancellation-pending]", StringComparison.Ordinal);

    private static bool IsAdminCancellationPendingQuarantineTerminal(Order order, PaymentIntent? paymentIntent) =>
        order.PaymentStatus == PaymentStatus.Expired &&
        order.Status == OrderStatus.OnHold &&
        paymentIntent?.Status == PaymentIntentStatus.Suspicious &&
        string.Equals(
            paymentIntent.FailureCode,
            "ADMIN_CANCELLATION_PENDING_RECONCILIATION_REQUIRED",
            StringComparison.Ordinal);

    private static decimal NormalizeMoney(decimal amount, string? currency)
    {
        var decimalPlaces = CurrencyHelper.GetDecimalPlaces(currency);
        return Math.Round(amount, decimalPlaces, MidpointRounding.AwayFromZero);
    }

    private static string? NormalizeCurrency(string? currency) =>
        string.IsNullOrWhiteSpace(currency)
            ? null
            : currency.Trim().ToUpperInvariant();

    private async Task<(PaymentTransitionResult? Failure, string? Currency)> TryResolveLifecycleCurrencyAsync(
        Order order,
        PaymentIntent? paymentIntent,
        HandlePaymentResultRequest request,
        CancellationToken ct)
    {
        var context = $"tenantId:{order.TenantId:D};orderId:{order.Id:D};source:{request.Source}";
        var paymentCurrency = request.Currency;

        if (string.IsNullOrWhiteSpace(paymentCurrency) && paymentIntent != null)
        {
            paymentCurrency = paymentIntent.Currency;
        }

        try
        {
            var orderCurrency = FinancialCurrencyGuard.RequireCurrency(
                order.Currency,
                "order.currency",
                context);

            if (paymentIntent != null || !string.IsNullOrWhiteSpace(paymentCurrency))
            {
                var resolvedPaymentCurrency = FinancialCurrencyGuard.RequireMatch(
                    paymentCurrency ?? string.Empty,
                    "payment.currency",
                    orderCurrency,
                    "order.currency",
                    context);

                return (null, resolvedPaymentCurrency);
            }

            return (null, orderCurrency);
        }
        catch (FinancialCurrencyMismatchException ex)
        {
            _reliabilityTelemetry?.RecordCurrencyMismatch(order.TenantId, order.Id, "payment");
            _logger.LogError(
                ex,
                "Payment lifecycle rejected due to currency mismatch | TenantId: {TenantId} | OrderId: {OrderId} | Source: {Source}",
                order.TenantId,
                order.Id,
                request.Source);
            return (PaymentTransitionResult.Failed(
                "Payment currency does not match the authoritative tenant/order currency.",
                "CURRENCY_MISMATCH"), null);
        }
        catch (FinancialCurrencyMissingException ex)
        {
            _reliabilityTelemetry?.RecordCurrencyMissing(order.TenantId, order.Id, "payment");
            _logger.LogError(
                ex,
                "Payment lifecycle rejected due to missing currency | TenantId: {TenantId} | OrderId: {OrderId} | Source: {Source}",
                order.TenantId,
                order.Id,
                request.Source);
            return (PaymentTransitionResult.Failed(
                "Payment currency is missing.",
                "CURRENCY_NOT_SET"), null);
        }
        catch (FinancialCurrencyInvalidException ex)
        {
            _reliabilityTelemetry?.RecordCurrencyInvalid(order.TenantId, order.Id, "payment");
            _logger.LogError(
                ex,
                "Payment lifecycle rejected due to invalid currency | TenantId: {TenantId} | OrderId: {OrderId} | Source: {Source}",
                order.TenantId,
                order.Id,
                request.Source);
            return (PaymentTransitionResult.Failed(
                "Payment currency is invalid.",
                "CURRENCY_INVALID"), null);
        }
    }

    private static string ResolveGateway(Order order, HandlePaymentResultRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Gateway))
        {
            return request.Gateway;
        }

        if (!string.IsNullOrWhiteSpace(order.PaymentGateway))
        {
            return order.PaymentGateway;
        }

        return order.PaymentMethodCode;
    }

    private static void ValidateSuccessfulAmount(Order order, decimal amount)
    {
        if (amount <= 0m)
        {
            throw new InvalidOperationException("Captured amount must be positive.");
        }

        var remainingBalance = order.Items.Sum(item => item.RemainingAmount);
        var tolerance = CurrencyHelper.GetMonetaryTolerance(order.Currency);
        
        if (remainingBalance > 0m && amount + tolerance < remainingBalance)
        {
            throw new InvalidOperationException(
                $"Captured amount {amount} does not cover remaining item balance {remainingBalance} for order {order.OrderNumber}.");
        }
    }

    private async Task<PaymentTransitionResult?> ValidateManualWalletSettlementAsync(
        Order order, PaymentIntent? paymentIntent, decimal amount, CancellationToken ct)
    {
        // Full settlement uses the accepted order total, including non-item charges.
        // Item balances use the persisted net discount/tax allocation. Taking their
        // maximum also fails closed on inconsistent legacy snapshots rather than
        // forgiving an unrepresented discount or manufacturing a captured amount.
        var accepted = await _db.PaymentIntents.AsNoTracking()
            .Where(pi => pi.TenantId == order.TenantId && pi.OrderId == order.Id &&
                pi.Id != (paymentIntent != null ? paymentIntent.Id : Guid.Empty) &&
                pi.Status == PaymentIntentStatus.Paid && pi.Currency == order.Currency)
            .SumAsync(pi => (decimal?)pi.Amount, ct) ?? 0m;
        var recognized = Math.Max(accepted, order.TotalPaidAmount);
        var remaining = Math.Max(order.Items.Sum(item => item.RemainingAmount), Math.Max(0m, order.Total - recognized));
        var tolerance = CurrencyHelper.GetMonetaryTolerance(order.Currency);
        if (amount <= 0m || amount + tolerance < remaining)
        {
            return PaymentTransitionResult.Failed(
                $"Submitted amount {amount:F2} {order.Currency} does not cover remaining payable balance {remaining:F2} {order.Currency} for order {order.OrderNumber} (shortfall {Math.Max(0m, remaining - amount):F2}). Full settlement is required for Manual Wallet approval.",
                "PAYMENT_INSUFFICIENT_AMOUNT");
        }

        return amount - tolerance > remaining
            ? PaymentTransitionResult.Failed(
                $"Submitted amount {amount:F2} {order.Currency} exceeds remaining payable balance {remaining:F2} {order.Currency} for order {order.OrderNumber}. Reconcile the excess before Manual Wallet approval.",
                "PAYMENT_EXCESS_AMOUNT")
            : null;
    }

    private static void ApplySuccessfulPaymentAllocation(Order order, decimal amount)
    {
        if (order.Items.Count == 0)
        {
            return;
        }

        var remainingToApply = amount;

        foreach (var item in order.Items.OrderBy(item => item.Id))
        {
            if (remainingToApply <= 0m)
            {
                break;
            }

            var remainingItemBalance = item.RemainingAmount;
            if (remainingItemBalance <= 0m)
            {
                continue;
            }

            var applied = Math.Min(remainingItemBalance, remainingToApply);
            item.PaidAmount += applied;
            remainingToApply -= applied;
        }
    }

    private async Task<Order?> LoadOrderForRefundMutationAsync(Guid orderId, CancellationToken ct)
    {
        IQueryable<Order> query;

        if (IsPostgresProvider())
        {
            query = _db.Orders.FromSqlInterpolated($@"
                SELECT xmin, * FROM ""Orders""
                WHERE ""Id"" = {orderId}
                  AND ""IsDeleted"" = false
                FOR UPDATE");
        }
        else if (IsSqlServerProvider())
        {
            query = _db.Orders.FromSqlInterpolated($@"
                SELECT * FROM [Orders] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [Id] = {orderId}
                  AND [IsDeleted] = 0");
        }
        else
        {
            var providerName = _db.Database.ProviderName ?? "Unknown";
            if (_requireRowLevelLocking)
            {
                throw new InvalidOperationException(
                    $"Database provider '{providerName}' is not supported for production refund flows. " +
                    "Row-level locking is required to prevent concurrent over-refunds.");
            }

            _logger.LogWarning(
                "Database provider '{ProviderName}' does not support row-level locking. " +
                "Refund flows may be vulnerable to races outside test environments.",
                providerName);

            query = _db.Orders;
        }

        return await query
            .Include(order => order.Items)
            .Include(order => order.Refunds)
            .FirstOrDefaultAsync(order => order.Id == orderId, ct);
    }

    private async Task<RefundAccountingContextResolution> ResolveRefundAccountingContextAsync(
        Order order,
        CancellationToken ct)
    {
        var hasCurrentPostSettlementRefundHistory = order.Refunds.Any(refund =>
            !refund.IsDeleted &&
            refund.IsPostSettlementRefund);

        var settlementOrder = await _db.PaymentSettlementOrders
            .IgnoreQueryFilters()
            .Include(item => item.SettlementBatch)
            .Where(item =>
                !item.IsDeleted &&
                item.TenantId == order.TenantId &&
                item.OrderId == order.Id)
            .SingleOrDefaultAsync(ct);

        if (settlementOrder == null)
        {
            if (order.PaymentStatus == PaymentStatus.Settled || hasCurrentPostSettlementRefundHistory)
            {
                return RefundAccountingContextResolution.Failed(
                    "Cannot refund settled order because its settlement snapshot is missing.");
            }

            return RefundAccountingContextResolution.Succeeded(
                EcommerceRefundAccountingContext.PreSettlement);
        }

        var batch = settlementOrder.SettlementBatch;
        if (batch == null || batch.IsDeleted)
        {
            return RefundAccountingContextResolution.Failed(
                "Cannot refund settled order because its settlement batch snapshot is missing.");
        }

        try
        {
            await EnsureSettlementBatchRetainsCurrentOrderLineageAsync(batch, order.Id, ct);
        }
        catch (InvalidOperationException ex)
        {
            return RefundAccountingContextResolution.Failed(ex.Message);
        }

        var context = new EcommerceRefundAccountingContext(
            IsPostSettlementRefund: true,
            SettlementBatchId: batch.Id,
            SettlementType: batch.SettlementType,
            SettlementId: batch.SettlementId,
            PayoutBatchId: batch.PayoutBatchId,
            SettledAtUtc: batch.SettledAtUtc);

        try
        {
            context.EnsureValidForPostSettlementRefund();
            await SettlementRefundLineage.ResolveDestinationAccountAsync(_db, order.TenantId, order.Id, context, ct);
        }
        catch (InvalidOperationException ex)
        {
            return RefundAccountingContextResolution.Failed(ex.Message);
        }

        return RefundAccountingContextResolution.Succeeded(context);
    }

    private sealed record RefundAccountingContextResolution(
        EcommerceRefundAccountingContext? Context,
        string? ErrorMessage)
    {
        public static RefundAccountingContextResolution Succeeded(EcommerceRefundAccountingContext context) =>
            new(context, null);

        public static RefundAccountingContextResolution Failed(string errorMessage) =>
            new(null, errorMessage);
    }

    private static decimal ResolveRefundableAmount(Order order)
    {
        var orderTotal = NormalizeMoney(order.Total, order.Currency);
        if (orderTotal > 0m)
        {
            return orderTotal;
        }

        return NormalizeMoney(order.TotalPaidAmount, order.Currency);
    }

    private static decimal ResolvePaymentStatusVisibilityAmount(Order order)
    {
        var paidAmount = NormalizeMoney(order.TotalPaidAmount, order.Currency);
        return paidAmount > 0m
            ? paidAmount
            : NormalizeMoney(order.Total, order.Currency);
    }

    private static decimal ResolveAlreadyRefundedAmount(Order order)
    {
        var recordedRefundAmount = NormalizeMoney(
            order.Refunds.Where(refund => !refund.IsDeleted).Sum(refund => refund.Amount),
            order.Currency);
        var itemRefundAmount = NormalizeMoney(order.TotalRefundedAmount, order.Currency);

        return Math.Max(recordedRefundAmount, itemRefundAmount);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // T-08 STOREFRONT MERCHANDISE-COST (COGS) LIFECYCLE
    //
    // Policy: ProductVariant.CostPrice (moving weighted average) frozen at checkout
    // into immutable OrderItem.CostPrice. COGS follows the actual inventory commit:
    // each transaction that commits new reservation quantities stages exactly one
    // COGS event for the committed delta (per-commit semantics, cumulative caps
    // enforced by the outbox processor). Replays that commit nothing stage nothing.
    //
    // Partial-fulfillment accounting: the current architecture commits the whole
    // open reservation set at once (CommitOrderReservationsAsync), so a normal
    // order produces a single full-quantity COGS event. Genuine partial commits
    // (distinct commit events) each produce their own event; the processor caps
    // cumulative committed quantities per line at the ordered quantity.
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task<Dictionary<Guid, decimal>> SnapshotCommittedQuantitiesByItemAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken ct)
    {
        return await _db.InventoryReservations
            .IgnoreQueryFilters()
            .Where(reservation =>
                reservation.TenantId == tenantId &&
                reservation.OrderId == orderId &&
                !reservation.IsDeleted)
            .GroupBy(reservation => reservation.OrderItemId)
            .Select(group => new
            {
                OrderItemId = group.Key,
                Committed = group.Sum(reservation => reservation.CommittedQuantity)
            })
            .ToDictionaryAsync(row => row.OrderItemId, row => row.Committed, ct);
    }

    private static string BuildEcommerceCogsCommitEventId(
        Order order,
        HandlePaymentResultRequest request,
        string? resolvedPaymentReference,
        bool routedToSuspense)
    {
        return request.Source switch
        {
            // Single-commit flows: one deterministic commit identity per order.
            PaymentLifecycleSource.OfflineCollection => "cod-delivery-commit",
            PaymentLifecycleSource.PaymentReviewApproval => "wallet-approval-commit",
            _ when routedToSuspense && !string.IsNullOrWhiteSpace(request.WebhookEventId)
                => $"suspense-resolution-commit:{request.WebhookEventId!.Trim()}",
            _ when !string.IsNullOrWhiteSpace(resolvedPaymentReference)
                => resolvedPaymentReference.Trim(),
            _ => throw new InvalidOperationException(
                $"Storefront COGS commit for order {order.Id} requires a payment commit event identity. Failing closed.")
        };
    }

    /// <summary>
    /// T-08: stages one COGS event for the quantities THIS transaction committed
    /// (after-minus-before committed balances per order line), atomically with the
    /// commit. Returns null when nothing new was committed. Fails closed when the
    /// committed delta cannot be costed from immutable snapshots.
    /// </summary>
    private async Task<OutboxEnqueueResult?> TryEnqueueEcommerceCogsForCommittedDeltaAsync(
        Order order,
        Dictionary<Guid, decimal> committedBeforeByItem,
        string commitEventId,
        DateTime occurredAtUtc,
        CancellationToken ct)
    {
        var committedAfterByItem = await SnapshotCommittedQuantitiesByItemAsync(
            order.TenantId, order.Id, ct);

        var items = await _db.OrderItems
            .IgnoreQueryFilters()
            .Where(item => item.OrderId == order.Id && !item.IsDeleted)
            .ToListAsync(ct);

        var lines = new List<EcommerceCogsOutboxLine>();
        foreach (var item in items.OrderBy(candidate => candidate.Id))
        {
            if (item.Quantity <= 0 || item.CostPrice < 0m)
            {
                continue;
            }

            var delta = committedAfterByItem.GetValueOrDefault(item.Id)
                - committedBeforeByItem.GetValueOrDefault(item.Id);
            if (delta <= 0.0000001m)
            {
                continue;
            }

            if (delta - item.Quantity > 0.0001m)
            {
                throw new InvalidOperationException(
                    $"Storefront COGS commit delta {delta} exceeds ordered quantity {item.Quantity} for line {item.Id}. Failing closed.");
            }

            var committedQty = Math.Min(delta, (decimal)item.Quantity);
            var costAmount = Math.Round(item.CostPrice * committedQty, 2, MidpointRounding.AwayFromZero);
            lines.Add(new EcommerceCogsOutboxLine(
                item.Id, item.VariantId, committedQty, item.CostPrice, costAmount));
        }

        if (lines.Count == 0)
        {
            return null;
        }

        var currency = NormalizeCurrency(order.Currency) ?? order.Currency;
        var payload = new EcommerceCogsOutboxPayload(
            order.Id,
            commitEventId,
            currency,
            occurredAtUtc,
            lines,
            lines.Sum(line => line.CostAmount));

        return await _accountingOutbox.EnqueueEcommerceCogsAsync(
            order.TenantId, order.Id, commitEventId, payload, ct);
    }

    /// <summary>
    /// T-08: links a financial refund to physical stock and COGS reversal inside
    /// the same refund transaction (no half-refunded/half-returned state):
    /// - No COGS event ever staged for the order (pre-commit refund): release any
    ///   open reservations so available stock is not stranded; no reversal is due.
    /// - COGS staged (post-commit refund): restore the refunded share of the
    ///   remaining returnable stock and stage one COGS reversal at the ORIGINAL
    ///   snapshot cost. Cumulative restores/reversals are capped; over-return
    ///   fails closed and rolls the refund back for operator review.
    /// Pre-remediation orders committed before T-08 have no COGS event by design:
    /// their refunds keep today's behavior (money only) under the historical boundary.
    /// </summary>
    private async Task LinkReturnStockAndCogsReversalForRefundAsync(
        Order order,
        OrderRefund refund,
        string normalizedRefundId,
        CancellationToken ct)
    {
        var cogsEverStaged = await _db.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .AnyAsync(message =>
                message.TenantId == order.TenantId &&
                message.BusinessEntityId == order.Id &&
                message.EventType == EcommerceCogsPolicy.StorefrontCogsEventType,
                ct);

        if (!cogsEverStaged)
        {
            await _stockRepository.ReleaseOrderReservationsAsync(order.TenantId, order.Id, ct);
            return;
        }

        var items = await _db.OrderItems
            .IgnoreQueryFilters()
            .Where(item => item.OrderId == order.Id && !item.IsDeleted)
            .ToListAsync(ct);
        var itemsById = items.ToDictionary(item => item.Id);

        var priorReversalRows = await _db.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == order.TenantId &&
                message.BusinessEntityId == order.Id &&
                message.EventType == EcommerceCogsPolicy.StorefrontCogsReversalEventType)
            .Select(message => message.PayloadJson)
            .ToListAsync(ct);

        var priorReversedAmt = new Dictionary<Guid, decimal>();
        foreach (var priorJson in priorReversalRows)
        {
            var prior = JsonSerializer.Deserialize<EcommerceCogsReversalOutboxPayload>(priorJson);
            if (prior == null)
            {
                continue;
            }

            foreach (var priorLine in prior.Lines)
            {
                priorReversedAmt[priorLine.OrderItemId] =
                    priorReversedAmt.GetValueOrDefault(priorLine.OrderItemId) + priorLine.ReversalAmount;
            }
        }

        var reservationRows = await _db.InventoryReservations
            .IgnoreQueryFilters()
            .Where(reservation =>
                reservation.TenantId == order.TenantId &&
                reservation.OrderId == order.Id &&
                !reservation.IsDeleted)
            .Select(reservation => new
            {
                reservation.OrderItemId,
                reservation.CommittedQuantity,
                reservation.ReturnedQuantity,
                reservation.DamagedReturnedQuantity
            })
            .ToListAsync(ct);

        var committedByItem = reservationRows
            .GroupBy(row => row.OrderItemId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(row => row.CommittedQuantity));
        var returnableByItem = reservationRows
            .GroupBy(row => row.OrderItemId)
            .ToDictionary(
                group => group.Key,
                group => Math.Max(0m, group.Sum(row =>
                    row.CommittedQuantity - row.ReturnedQuantity - row.DamagedReturnedQuantity)));

        var restoreByItem = new Dictionary<Guid, decimal>();
        var reversalLines = new List<EcommerceCogsReversalOutboxLine>();

        foreach (var refundLine in refund.Lines.OrderBy(line => line.OrderItemId))
        {
            if (refundLine.Amount <= 0m)
            {
                continue;
            }

            if (!itemsById.TryGetValue(refundLine.OrderItemId, out var item))
            {
                throw new InvalidOperationException(
                    $"Refund line references unknown order item {refundLine.OrderItemId}. Failing closed.");
            }

            if (item.PaidAmount <= 0m)
            {
                throw new InvalidOperationException(
                    $"Refund line {refundLine.OrderItemId} has no paid amount to relieve. Failing closed.");
            }

            var share = refundLine.Amount / item.PaidAmount;
            if (share <= 0m)
            {
                continue;
            }

            if (share - 1m > 0.0001m)
            {
                throw new InvalidOperationException(
                    $"Refund share {share} exceeds the paid amount for line {refundLine.OrderItemId}. Failing closed.");
            }

            share = Math.Min(share, 1m);

            var returnable = returnableByItem.GetValueOrDefault(item.Id);
            var committed = committedByItem.GetValueOrDefault(item.Id);
            var returnQty = Math.Min(share * committed, returnable);
            if (returnQty > 0m)
            {
                restoreByItem[item.Id] = returnQty;
            }

            var postedItemCogs = Math.Round(item.CostPrice * item.Quantity, 2, MidpointRounding.AwayFromZero);
            if (postedItemCogs <= 0m || item.CostPrice <= 0m)
            {
                continue;
            }

            var reversal = Math.Round(postedItemCogs * share, 2, MidpointRounding.AwayFromZero);
            var alreadyReversed = priorReversedAmt.GetValueOrDefault(item.Id);
            var remaining = postedItemCogs - alreadyReversed;
            if (reversal - remaining > 0.01m)
            {
                throw new InvalidOperationException(
                    $"COGS reversal {reversal} exceeds remaining posted COGS {remaining} for line {item.Id}. Failing closed.");
            }

            reversal = Math.Min(reversal, Math.Max(0m, remaining));
            if (reversal > 0m)
            {
                reversalLines.Add(new EcommerceCogsReversalOutboxLine(item.Id, returnQty, reversal));
            }
        }

        if (restoreByItem.Count > 0)
        {
            await _stockRepository.RestoreCommittedReservationsForReturnAsync(
                order.TenantId, order.Id, restoreByItem, refund.Id, ct);
        }

        if (reversalLines.Count == 0)
        {
            return;
        }

        var currency = NormalizeCurrency(order.Currency) ?? order.Currency;
        var payload = new EcommerceCogsReversalOutboxPayload(
            order.Id,
            normalizedRefundId,
            currency,
            DateTime.UtcNow,
            reversalLines,
            reversalLines.Sum(line => line.ReversalAmount));

        await _accountingOutbox.EnqueueEcommerceCogsReversalAsync(
            order.TenantId, order.Id, normalizedRefundId, payload, ct);
    }

    private static void AllocateRefundAcrossItems(Order order, OrderRefund refund, decimal refundAmount)
    {
        if (order.Items.Count == 0)
        {
            return;
        }

        var remainingToAllocate = refundAmount;

        foreach (var item in order.Items.OrderBy(item => item.Id))
        {
            if (remainingToAllocate <= 0m)
            {
                break;
            }

            var availableItemRefund = NormalizeMoney(
                Math.Max(0m, item.PaidAmount - item.RefundedAmount),
                order.Currency);
            if (availableItemRefund <= 0m)
            {
                continue;
            }

            var applied = NormalizeMoney(Math.Min(availableItemRefund, remainingToAllocate), order.Currency);
            if (applied <= 0m)
            {
                continue;
            }

            item.RefundedAmount = NormalizeMoney(item.RefundedAmount + applied, order.Currency);
            remainingToAllocate = NormalizeMoney(remainingToAllocate - applied, order.Currency);

            refund.Lines.Add(new OrderRefundLine
            {
                Id = Guid.NewGuid(),
                TenantId = refund.TenantId,
                OrderRefundId = refund.Id,
                OrderId = item.OrderId,
                OrderItemId = item.Id,
                Amount = applied
            });
        }
    }

    private async Task PublishItemLevelRefundEventsAsync(
        Order order,
        OrderRefund refund,
        string refundId,
        string reason,
        DateTime refundedAtUtc,
        CancellationToken ct)
    {
        var orderItemsById = order.Items.ToDictionary(item => item.Id);
        var correlationId = _correlationIdAccessor.GetCorrelationId();

        foreach (var line in refund.Lines.OrderBy(line => line.OrderItemId))
        {
            if (!orderItemsById.TryGetValue(line.OrderItemId, out var item))
            {
                continue;
            }

            // PRODUCTION HARDENING: Always publish OrderItemRefundedEvent for any refunded line.
            // The item.RefundedAmount has already been updated by AllocateRefundAcrossItems before this method is called.
            // RefundedAmount is transaction-scoped (line.Amount) - the amount being refunded in THIS transaction.
            // CumulativeRefundedAmount is the total refunded amount including all previous refunds (item.RefundedAmount).

            await _eventPublisher.PublishAsync(new OrderItemRefundedEvent
            {
                TenantId = order.TenantId,
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                RefundId = refundId,
                OrderItemId = line.OrderItemId,
                ProductId = item.ProductId,
                ProductName = item.ProductName,
                RefundedAmount = line.Amount, // Transaction-scoped: amount for THIS refund
                CumulativeRefundedAmount = item.RefundedAmount, // Cumulative: total refunded including this transaction
                Reason = reason,
                RefundedAt = refundedAtUtc,
                CorrelationId = correlationId,
                IdempotencyKey = BuildRefundItemIdempotencyKey(order.TenantId, order.Id, refundId, line.OrderItemId),
                OccurredAtUtc = refundedAtUtc
            }, ct);
        }
    }

    private async Task ReconcileAffiliateCommissionReversalsForRefundAsync(
        Order order,
        OrderRefund refund,
        DateTime refundedAtUtc,
        CancellationToken ct)
    {
        var refundedItemIds = refund.Lines
            .Where(line => line.Amount > 0m)
            .Select(line => line.OrderItemId)
            .Where(orderItemId => orderItemId != Guid.Empty)
            .Distinct()
            .ToList();

        if (refundedItemIds.Count == 0)
        {
            return;
        }

        var earnedEntries = await _db.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .Where(entry =>
                entry.OrderId == order.Id &&
                refundedItemIds.Contains(entry.OrderItemId) &&
                entry.EntryType == CommissionEntryType.Earned)
            .ToListAsync(ct);

        if (earnedEntries.Count == 0)
        {
            return;
        }

        var earnedEntryIds = earnedEntries
            .Select(entry => entry.Id)
            .ToList();

        // Query all existing reversals (not just presence, but actual amounts)
        var existingReversals = await _db.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .Where(entry =>
                entry.EntryType == CommissionEntryType.Reversed &&
                entry.OriginalEntryId.HasValue &&
                earnedEntryIds.Contains(entry.OriginalEntryId.Value))
            .Select(entry => new
            {
                entry.OriginalEntryId,
                entry.BaseAmount,
                entry.CommissionAmount
            })
            .ToListAsync(ct);

        // Group reversals by original entry ID and sum amounts
        var previouslyReversedByEntryId = existingReversals
            .GroupBy(r => r.OriginalEntryId!.Value)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    BaseAmount = g.Sum(r => r.BaseAmount),
                    CommissionAmount = g.Sum(r => r.CommissionAmount)
                });

        // Build lookup: OrderItemId -> total refunded amount in THIS refund
        var refundedAmountByItemId = refund.Lines
            .Where(line => line.Amount > 0m && line.OrderItemId != Guid.Empty)
            .GroupBy(line => line.OrderItemId)
            .ToDictionary(g => g.Key, g => g.Sum(line => line.Amount));

        foreach (var earned in earnedEntries.OrderBy(entry => entry.RecordedAtUtc))
        {
            // Calculate previously reversed amounts for this earned entry
            var previouslyReversedBase = previouslyReversedByEntryId.TryGetValue(earned.Id, out var prev)
                ? prev.BaseAmount
                : 0m;

            var previouslyReversedCommission = previouslyReversedByEntryId.TryGetValue(earned.Id, out var prevComm)
                ? prevComm.CommissionAmount
                : 0m;

            // Get the refunded amount for this order item in THIS refund
            var refundedForItem = refundedAmountByItemId.TryGetValue(earned.OrderItemId, out var amt)
                ? amt
                : 0m;

            // Calculate the amount to reverse:
            // Min of (remaining earned base, refunded amount for this item)
            var remainingEarnedBase = earned.BaseAmount - previouslyReversedBase;
            var amountToReverseBase = Math.Min(remainingEarnedBase, refundedForItem);

            // Skip if nothing left to reverse
            if (amountToReverseBase <= 0m)
            {
                continue;
            }

            // Calculate proportional commission reversal
            var proportion = earned.BaseAmount > 0m
                ? amountToReverseBase / earned.BaseAmount
                : 0m;

            var amountToReverseCommission = Math.Round(
                earned.CommissionAmount * proportion,
                4,
                MidpointRounding.ToZero);

            // Ensure we don't reverse more commission than remains
            var remainingEarnedCommission = earned.CommissionAmount - previouslyReversedCommission;
            amountToReverseCommission = Math.Min(amountToReverseCommission, remainingEarnedCommission);

            // PRODUCTION HARDENING: Detect when this is the final reversal for this earned entry
            // and clear the full remaining commission to avoid leaving tiny remainders from rounding.
            // This happens when either:
            // 1. The remaining commission after this reversal would be less than minimum precision (0.0001)
            // 2. This reversal covers the full remaining base amount (final slice)
            var remainderAfterReversal = remainingEarnedCommission - amountToReverseCommission;
            var isFinalReversal = amountToReverseBase >= remainingEarnedBase;
            
            if (remainderAfterReversal < 0.0001m || isFinalReversal)
            {
                amountToReverseCommission = remainingEarnedCommission;
            }

            // Skip if commission rounds to zero or negative
            if (amountToReverseCommission <= 0m)
            {
                continue;
            }

            // Generate idempotency key that includes refund ID and amount to support multiple partial reversals
            // CULTURE-INVARIANT: Use InvariantCulture to ensure consistent formatting across all server locales
            var reversalIdempotencyKey = $"AFFILIATE_COMM:REVERSED:{refund.RefundId}:{earned.OrderItemId:N}:{amountToReverseBase.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}";

            var reversal = AffiliateCommissionLedger.CreateReversalWithCustomKey(
                earned.StoreTenantId,
                earned.Id,
                earned.AttributionId,
                earned.AffiliateAccountId,
                earned.OrderId,
                earned.OrderItemId,
                amountToReverseBase,
                amountToReverseCommission,
                earned.CommissionRate,
                earned.Currency,
                reversalIdempotencyKey);

            _db.AffiliateCommissionLedger.Add(reversal);

            var affiliateAuthorityTenantId = await ResolveAffiliateAuthorityTenantIdForRefundAsync(
                earned.AffiliateAccountId,
                ct);

            // Include the reversal amount in idempotency key to allow multiple partial reversals
            // CULTURE-INVARIANT: Use InvariantCulture to ensure consistent formatting across all server locales
            var reversalAmountKey = amountToReverseBase.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
            
            await _eventPublisher.PublishAsync(new AffiliateCommissionReversedEvent
            {
                TenantId = earned.StoreTenantId,
                StoreTenantId = earned.StoreTenantId,
                AffiliateAuthorityTenantId = affiliateAuthorityTenantId,
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                OrderItemId = earned.OrderItemId,
                AffiliateAccountId = earned.AffiliateAccountId,
                OriginalLedgerEntryId = earned.Id,
                ReversalLedgerEntryId = reversal.Id,
                ReversedAmount = reversal.CommissionAmount,
                Currency = reversal.Currency,
                CorrelationId = _correlationIdAccessor.GetCorrelationId() ?? order.Id.ToString(),
                IdempotencyKey = $"AFFILIATE_COMM:REFUND_REVERSAL:{refund.RefundId}:{earned.OrderItemId:N}:{reversalAmountKey}",
                OccurredAtUtc = refundedAtUtc
            }, ct);
        }
    }

    private async Task<Guid?> ResolveAffiliateAuthorityTenantIdForRefundAsync(
        Guid affiliateAccountId,
        CancellationToken ct)
    {
        var tenantId = await _db.AffiliateAccounts
            .IgnoreQueryFilters()
            .Where(account => account.Id == affiliateAccountId)
            .Select(account => (Guid?)account.TenantId)
            .FirstOrDefaultAsync(ct);

        return tenantId == Guid.Empty ? null : tenantId;
    }

    private async Task<bool> ConfirmOfferPromotionUsageAsync(
        Order order,
        string correlationId,
        CancellationToken ct)
    {
        if (!order.AppliedPromotionId.HasValue ||
            !string.Equals(order.AppliedPromotionType, "offer", StringComparison.OrdinalIgnoreCase) ||
            order.PromotionDiscountAmount <= 0m)
        {
            return false;
        }

        var promotionId = order.AppliedPromotionId.Value;
        var promotion = await LockPromotionForUsageMutationAsync(order.TenantId, promotionId, order.Id, ct);

        var existingUsage = await _db.PromotionUsages
            .FirstOrDefaultAsync(
                usage => usage.TenantId == order.TenantId &&
                         usage.PromotionId == promotionId &&
                         usage.OrderId == order.Id &&
                         !usage.IsDeleted,
                ct);

        if (existingUsage != null)
        {
            return false;
        }

        await EnsurePromotionUsageAuthorityStillValidAsync(
            promotion,
            order,
            ct);

        var appliedAtUtc = DateTime.UtcNow;
        var appliedItems = BuildPromotionAppliedItems(order);
        var usage = new PromotionUsage
        {
            Id = Guid.NewGuid(),
            TenantId = order.TenantId,
            PromotionId = promotionId,
            OrderId = order.Id,
            CustomerId = ResolvePromotionCustomerId(order),
            PromotionName = string.IsNullOrWhiteSpace(promotion.Name) ? "Offer" : promotion.Name,
            DiscountAmount = order.PromotionDiscountAmount,
            OrderSubtotal = order.Subtotal,
            AppliedToItemsJson = appliedItems.Count == 0 ? null : JsonSerializer.Serialize(appliedItems),
            AppliedAtUtc = appliedAtUtc,
            CreatedAt = appliedAtUtc
        };

        _db.PromotionUsages.Add(usage);

        // DESIGN NOTE: Save immediately to detect duplicate key violations early (fail-fast pattern).
        // This enables idempotent handling before executing expensive operations below:
        // - CountAsync for usage calculation
        // - tracked promotion CurrentUsage mutation
        // - event publishing and staging
        // Deferring this save would waste resources on operations that would be rolled back on duplicate.
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            DetachEntity(usage);

            if (IsDuplicatePromotionUsageOrderException(ex))
            {
                return false;
            }

            throw;
        }

        var confirmedUsageCount = await _db.PromotionUsages
            .CountAsync(
                entity => entity.TenantId == order.TenantId &&
                          entity.PromotionId == promotionId &&
                          !entity.IsDeleted,
                ct);

        promotion.CurrentUsage = confirmedUsageCount;

        await _eventPublisher.PublishAsync(new PromotionAppliedEvent
        {
            TenantId = order.TenantId,
            PromotionId = promotionId,
            PromotionName = usage.PromotionName,
            PromotionType = "offer",
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            CustomerId = usage.CustomerId,
            DiscountAmount = usage.DiscountAmount,
            OrderSubtotal = order.Subtotal,
            NewTotalUsageCount = confirmedUsageCount,
            AppliedToItems = appliedItems,
            CorrelationId = correlationId,
            OccurredAtUtc = appliedAtUtc
        }, ct);

        // Save the tracked promotion usage count and staged outbox event together.
        // Keeping CurrentUsage on the locked tracked entity lets ApplicationDbContext
        // advance ProjectionMutationVersion before the authoritative event is stored.
        await _db.SaveChangesAsync(ct);

        return true;
    }

    private async Task EnsurePromotionUsageAuthorityStillValidAsync(
        Promotion promotion,
        Order order,
        CancellationToken ct)
    {
        promotion.Validate();

        var nowUtc = DateTime.UtcNow;
        if (!promotion.IsActive || promotion.StartDate > nowUtc || promotion.EndDate < nowUtc)
        {
            throw new InvalidOperationException(
                $"Promotion {promotion.Id} is no longer active while confirming usage for order {order.Id}.");
        }

        if (promotion.MinOrderAmount.HasValue && order.Subtotal < promotion.MinOrderAmount.Value)
        {
            throw new InvalidOperationException(
                $"Promotion {promotion.Id} no longer meets the minimum order amount for order {order.Id}.");
        }

        var confirmedUsageCount = await _db.PromotionUsages
            .CountAsync(
                usage => usage.TenantId == order.TenantId &&
                         usage.PromotionId == promotion.Id &&
                         !usage.IsDeleted,
                ct);

        if (promotion.MaxUsage.HasValue && confirmedUsageCount >= promotion.MaxUsage.Value)
        {
            throw new InvalidOperationException(
                $"Promotion {promotion.Id} usage limit is exhausted while confirming usage for order {order.Id}.");
        }

        var currentDiscount = await CalculateCurrentPromotionDiscountForOrderAsync(
            promotion,
            order,
            ct);

        if (NormalizeMoney(currentDiscount, order.Currency) != NormalizeMoney(order.PromotionDiscountAmount, order.Currency))
        {
            throw new InvalidOperationException(
                $"Promotion {promotion.Id} no longer matches the authoritative order discount for order {order.Id}.");
        }
    }

    private async Task<decimal> CalculateCurrentPromotionDiscountForOrderAsync(
        Promotion promotion,
        Order order,
        CancellationToken ct)
    {
        var items = order.Items
            .Where(item => !item.IsDeleted && item.Quantity > 0)
            .OrderBy(item => item.ProductId)
            .ThenBy(item => item.VariantId ?? Guid.Empty)
            .ThenBy(item => item.Id)
            .ToList();

        if (items.Count == 0)
        {
            return 0m;
        }

        if (promotion.Scope == PromotionScope.Cart)
        {
            return NormalizeMoney(promotion.CalculateDiscount(order.Subtotal), order.Currency);
        }

        Dictionary<Guid, Guid> categoryLookup = new();
        if (promotion.Scope == PromotionScope.Category)
        {
            var productIds = items
                .Select(item => item.ProductId)
                .Where(productId => productId != Guid.Empty)
                .Distinct()
                .ToList();

            categoryLookup = await _db.Products
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(product => product.TenantId == order.TenantId &&
                                  productIds.Contains(product.Id) &&
                                  !product.IsDeleted)
                .Select(product => new { product.Id, product.CategoryId })
                .ToDictionaryAsync(product => product.Id, product => product.CategoryId, ct);
        }

        var discount = 0m;
        foreach (var item in items)
        {
            var applies = promotion.Scope switch
            {
                PromotionScope.Product => !promotion.AppliesToId.HasValue || promotion.AppliesToId.Value == item.ProductId,
                PromotionScope.Category => !promotion.AppliesToId.HasValue ||
                                           categoryLookup.GetValueOrDefault(item.ProductId) == promotion.AppliesToId.Value,
                _ => false
            };

            if (!applies)
            {
                continue;
            }

            var lineSubtotal = NormalizeMoney(item.UnitPrice * item.Quantity, order.Currency);
            discount = NormalizeMoney(discount + promotion.CalculateDiscount(lineSubtotal), order.Currency);
        }

        return discount;
    }

    private async Task<Promotion> LockPromotionForUsageMutationAsync(
        Guid tenantId,
        Guid promotionId,
        Guid orderId,
        CancellationToken ct)
    {
        IQueryable<Promotion> query;

        if (IsPostgresProvider())
        {
            query = _db.Promotions.FromSqlInterpolated($@"
                SELECT xmin, *
                FROM ""Promotions""
                WHERE ""TenantId"" = {tenantId}
                  AND ""Id"" = {promotionId}
                  AND ""IsDeleted"" = false
                FOR UPDATE");
        }
        else if (IsSqlServerProvider())
        {
            query = _db.Promotions.FromSqlInterpolated($@"
                SELECT *
                FROM [Promotions] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [TenantId] = {tenantId}
                  AND [Id] = {promotionId}
                  AND [IsDeleted] = 0");
        }
        else
        {
            var providerName = _db.Database.ProviderName ?? "Unknown";
            if (_requireRowLevelLocking)
            {
                throw new InvalidOperationException(
                    $"Database provider '{providerName}' is not supported for production promotion usage flows. " +
                    "Row-level locking is required to keep offer usage counts consistent under concurrent payment confirmations.");
            }

            _logger.LogWarning(
                "Database provider '{ProviderName}' does not support promotion usage row locking. " +
                "Offer usage counts may be vulnerable to races outside test environments.",
                providerName);

            query = _db.Promotions;
        }

        return await query
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                entity => entity.TenantId == tenantId &&
                          entity.Id == promotionId &&
                          !entity.IsDeleted,
                ct)
            ?? throw new InvalidOperationException(
                $"Promotion {promotionId} could not be locked while confirming paid offer usage for order {orderId}.");
    }

    private List<PromotionAppliedItem> BuildPromotionAppliedItems(Order order)
    {
        var items = order.Items
            .Where(item => !item.IsDeleted && item.Quantity > 0)
            .OrderBy(item => item.Id)
            .ToList();

        if (items.Count == 0 || order.PromotionDiscountAmount <= 0m)
        {
            return new List<PromotionAppliedItem>();
        }

        var lineSubtotals = items
            .Select(item => NormalizeMoney(item.UnitPrice * item.Quantity, order.Currency))
            .ToArray();

        var discounts = AllocateDiscountAcrossItems(
            lineSubtotals,
            NormalizeMoney(order.PromotionDiscountAmount, order.Currency),
            order.Currency);

        var appliedItems = new List<PromotionAppliedItem>(items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var lineSubtotal = lineSubtotals[index];
            var discountAmount = discounts[index];

            appliedItems.Add(new PromotionAppliedItem
            {
                ProductId = item.ProductId,
                VariantId = item.VariantId,
                ProductName = string.IsNullOrWhiteSpace(item.VariantName)
                    ? item.ProductName
                    : $"{item.ProductName} / {item.VariantName}",
                Quantity = item.Quantity,
                OriginalPrice = lineSubtotal,
                DiscountAmount = discountAmount,
                FinalPrice = NormalizeMoney(Math.Max(0m, lineSubtotal - discountAmount), order.Currency)
            });
        }

        return appliedItems;
    }

    private static IReadOnlyList<decimal> AllocateDiscountAcrossItems(
        IReadOnlyList<decimal> lineSubtotals,
        decimal totalDiscount,
        string? currency)
    {
        if (lineSubtotals.Count == 0 || totalDiscount <= 0m)
        {
            return Array.Empty<decimal>();
        }

        var totalLineSubtotal = NormalizeMoney(lineSubtotals.Sum(), currency);
        if (totalLineSubtotal <= 0m)
        {
            return Enumerable.Repeat(0m, lineSubtotals.Count).ToArray();
        }

        var allocations = new decimal[lineSubtotals.Count];
        var allocatedAmount = 0m;
        var remainders = new List<(int Index, decimal FractionalRemainder, decimal Capacity)>(lineSubtotals.Count);
        var scale = (decimal)Math.Pow(10, CurrencyHelper.GetDecimalPlaces(currency));
        var smallestUnit = scale <= 0m ? 0.01m : 1m / scale;

        for (var index = 0; index < lineSubtotals.Count; index++)
        {
            var lineSubtotal = lineSubtotals[index];
            if (lineSubtotal <= 0m)
            {
                remainders.Add((index, 0m, 0m));
                continue;
            }

            var exactShare = totalDiscount * (lineSubtotal / totalLineSubtotal);
            var roundedDownShare = Math.Floor(exactShare * scale) / scale;
            var allocatedShare = NormalizeMoney(Math.Min(lineSubtotal, roundedDownShare), currency);

            allocations[index] = allocatedShare;
            allocatedAmount = NormalizeMoney(allocatedAmount + allocatedShare, currency);
            remainders.Add((index, exactShare - roundedDownShare, lineSubtotal - allocatedShare));
        }

        var remainingAmount = NormalizeMoney(totalDiscount - allocatedAmount, currency);
        foreach (var remainder in remainders
                     .Where(entry => entry.Capacity >= smallestUnit)
                     .OrderByDescending(entry => entry.FractionalRemainder)
                     .ThenBy(entry => entry.Index))
        {
            if (remainingAmount < smallestUnit)
            {
                break;
            }

            var nextAllocation = NormalizeMoney(allocations[remainder.Index] + smallestUnit, currency);
            if (nextAllocation > lineSubtotals[remainder.Index])
            {
                continue;
            }

            allocations[remainder.Index] = nextAllocation;
            remainingAmount = NormalizeMoney(remainingAmount - smallestUnit, currency);
        }

        if (remainingAmount > 0m)
        {
            var fallbackIndex = allocations.Length - 1;
            allocations[fallbackIndex] = NormalizeMoney(
                Math.Min(lineSubtotals[fallbackIndex], allocations[fallbackIndex] + remainingAmount),
                currency);
        }

        return allocations;
    }

    private static Guid? ResolvePromotionCustomerId(Order order)
    {
        if (order.OnlineCustomerId.HasValue && order.OnlineCustomerId.Value != Guid.Empty)
        {
            return order.OnlineCustomerId.Value;
        }

        if (order.CustomerId.HasValue && order.CustomerId.Value != Guid.Empty)
        {
            return order.CustomerId.Value;
        }

        return null;
    }

    /// <summary>
    /// Detects duplicate PromotionUsage violations by parsing exception messages.
    /// 
    /// WARNING: This detection is FRAGILE and DATABASE PROVIDER-DEPENDENT.
    /// 
    /// Detection Strategy:
    /// 1. Primary: Checks for exact index name "IX_PromotionUsages_TenantId_PromotionId_OrderId"
    /// 2. Fallback: Checks for all four terms: "PromotionUsages", "OrderId", "PromotionId", "TenantId"
    /// 
    /// MAINTENANCE REQUIREMENTS:
    /// - If the index "IX_PromotionUsages_TenantId_PromotionId_OrderId" is renamed in migrations,
    ///   update the index name string in this method.
    /// - If column names change, update the fallback term list: ["PromotionUsages", "OrderId", "PromotionId", "TenantId"]
    /// 
    /// TODO: Replace with provider-specific error code detection:
    /// - SQL Server: Check for error code 2601 (unique index violation) or 2627 (unique constraint violation)
    /// - PostgreSQL: Check for SQLSTATE 23505 and parse constraint name from exception details
    /// - This would eliminate fragile string parsing and work reliably across database providers
    /// 
    /// Related Index: IX_PromotionUsages_TenantId_PromotionId_OrderId (unique constraint on PromotionUsages table)
    /// </summary>
    private static bool IsDuplicatePromotionUsageOrderException(DbUpdateException exception)
    {
        var fullMessage = string.Join(
            Environment.NewLine,
            new[]
            {
                exception.Message,
                exception.InnerException?.Message ?? string.Empty
            });

        return fullMessage.Contains("IX_PromotionUsages_TenantId_PromotionId_OrderId", StringComparison.OrdinalIgnoreCase) ||
               (fullMessage.Contains("PromotionUsages", StringComparison.OrdinalIgnoreCase) &&
                fullMessage.Contains("OrderId", StringComparison.OrdinalIgnoreCase) &&
                fullMessage.Contains("PromotionId", StringComparison.OrdinalIgnoreCase) &&
                fullMessage.Contains("TenantId", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Detects duplicate OrderRefund violations by parsing exception messages.
    /// 
    /// WARNING: This detection is FRAGILE and DATABASE PROVIDER-DEPENDENT.
    /// 
    /// Detection Strategy:
    /// 1. Primary: Checks for exact index name "IX_OrderRefunds_TenantId_OrderId_RefundId"
    /// 2. Fallback: Checks for all four terms: "OrderRefunds", "OrderId", "RefundId", "TenantId"
    /// 
    /// MAINTENANCE REQUIREMENTS:
    /// - If the index "IX_OrderRefunds_TenantId_OrderId_RefundId" is renamed in migrations,
    ///   update the index name string in this method.
    /// - If column names change, update the fallback term list: ["OrderRefunds", "OrderId", "RefundId", "TenantId"]
    /// - See IsDuplicatePromotionUsageOrderException for parallel implementation pattern
    /// 
    /// TODO: Replace with provider-specific error code detection:
    /// - SQL Server: Check for error code 2601 (unique index violation) or 2627 (unique constraint violation)
    /// - PostgreSQL: Check for SQLSTATE 23505 and parse constraint name from exception details
    /// - This would eliminate fragile string parsing and work reliably across database providers
    /// 
    /// TESTING:
    /// - Unit/integration tests should cover duplicate refund detection behavior
    /// - Tests should verify both primary (index name) and fallback (term matching) detection paths
    /// 
    /// Related Index: IX_OrderRefunds_TenantId_OrderId_RefundId (unique constraint on OrderRefunds table)
    /// </summary>
    private static bool IsDuplicateOrderRefundException(DbUpdateException exception)
    {
        var fullMessage = string.Join(
            Environment.NewLine,
            new[]
            {
                exception.Message,
                exception.InnerException?.Message ?? string.Empty
            });

        return fullMessage.Contains("IX_OrderRefunds_TenantId_OrderId_RefundId", StringComparison.OrdinalIgnoreCase) ||
               (fullMessage.Contains("OrderRefunds", StringComparison.OrdinalIgnoreCase) &&
                fullMessage.Contains("OrderId", StringComparison.OrdinalIgnoreCase) &&
                fullMessage.Contains("RefundId", StringComparison.OrdinalIgnoreCase) &&
                fullMessage.Contains("TenantId", StringComparison.OrdinalIgnoreCase));
    }

    private string? NormalizeRefundId(string? refundId)
    {
        if (string.IsNullOrWhiteSpace(refundId))
        {
            return null;
        }

        var normalized = refundId.Trim();
        
        if (normalized.Length <= 120)
        {
            return normalized;
        }

        // For IDs exceeding 120 chars, we need to use a surrogate
        // Use a deterministic hash-based surrogate to preserve uniqueness
        // SHA-256 produces 32 bytes = 64 hex chars, so "HASH_{64 chars}" = 69 chars total (well within 120 limit)
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(normalized));
        var hashHex = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        
        // Use the full hash (64 chars) - total surrogate length is 69 chars (HASH_ + 64)
        var surrogate = $"HASH_{hashHex}";
        
        _logger.LogWarning(
            "RefundId exceeded 120 character limit (length: {OriginalLength}). Using hashed surrogate. Hash: {SurrogateHash}",
            normalized.Length,
            surrogate);
        
        return surrogate;
    }

    /// <summary>
    /// Finds an existing refund by ID, checking both new hash-based and legacy truncated forms.
    /// This preserves replay compatibility with refunds stored under the old truncation logic.
    /// For legacy matches, validates stable invariants (amount/currency) to prevent false positives.
    /// </summary>
    private OrderRefund? FindExistingRefund(Order order, string refundId, decimal? expectedAmount = null, string? expectedCurrency = null)
    {
        var normalized = refundId.Trim();
        
        // First, try exact match with the normalized/hashed ID
        var normalizedRefundId = NormalizeRefundId(refundId);
        if (normalizedRefundId != null)
        {
            var exactMatch = order.Refunds
                .FirstOrDefault(r => string.Equals(r.RefundId, normalizedRefundId, StringComparison.Ordinal));
            
            if (exactMatch != null)
            {
                return exactMatch;
            }
        }

        // If the original ID exceeds 120 chars and no match was found with the hash,
        // check for legacy truncated form (first 120 chars) for replay compatibility
        if (normalized.Length > 120)
        {
            var legacyTruncated = normalized[..120];
            var legacyMatch = order.Refunds
                .FirstOrDefault(r => string.Equals(r.RefundId, legacyTruncated, StringComparison.Ordinal));
            
            if (legacyMatch != null)
            {
                // Validate stable invariants to prevent false positives
                // Only accept legacy match if amount and currency match (when provided)
                var tolerance = CurrencyHelper.GetMonetaryTolerance(order.Currency);
                var amountMatches = !expectedAmount.HasValue || 
                    Math.Abs(legacyMatch.Amount - expectedAmount.Value) <= tolerance;
                var currencyMatches = string.IsNullOrEmpty(expectedCurrency) || 
                    string.Equals(legacyMatch.Currency, expectedCurrency, StringComparison.OrdinalIgnoreCase);

                if (amountMatches && currencyMatches)
                {
                    _logger.LogInformation(
                        "Found existing refund using legacy truncated ID (first 120 chars) with matching amount/currency. OrderId: {OrderId}, LegacyRefundId: {LegacyRefundId}, Amount: {Amount}, Currency: {Currency}",
                        order.Id,
                        legacyTruncated,
                        legacyMatch.Amount,
                        legacyMatch.Currency);
                    return legacyMatch;
                }
                else
                {
                    _logger.LogWarning(
                        "Found refund with legacy truncated ID but stable invariants do not match - treating as distinct refund. OrderId: {OrderId}, LegacyRefundId: {LegacyRefundId}, StoredAmount: {StoredAmount}, ExpectedAmount: {ExpectedAmount}, StoredCurrency: {StoredCurrency}, ExpectedCurrency: {ExpectedCurrency}",
                        order.Id,
                        legacyTruncated,
                        legacyMatch.Amount,
                        expectedAmount,
                        legacyMatch.Currency,
                        expectedCurrency);
                }
            }
            
            // No legacy match found or invariants didn't match, log that we're using the new hash-based form
            _logger.LogInformation(
                "No legacy truncated refund found or invariants mismatched. Using new hash-based surrogate for refund. OrderId: {OrderId}, HashedRefundId: {HashedRefundId}",
                order.Id,
                normalizedRefundId);
        }

        return null;
    }

    private void DetachEntity(object entity)
    {
        var entry = _db.Entry(entity);
        if (entry.State != EntityState.Detached)
        {
            entry.State = EntityState.Detached;
        }
    }

    private async Task<AccountingStageResult> EnqueueSuccessOutboxAsync(
        Order order,
        PaymentIntent? paymentIntent,
        HandlePaymentResultRequest request,
        decimal amount,
        bool routedToSuspense,
        string? resolvedPaymentReference,
        CancellationToken ct)
    {
        var gateway = ResolveGateway(order, request);

        switch (request.Source)
        {
            case PaymentLifecycleSource.OfflineCollection:
            {
                var outboxResult = await _accountingOutbox.EnqueueCodDeliveredAsync(order.TenantId, order.Id, ct);
                var message = await LoadRequiredAccountingOutboxMessageAsync(
                    order.TenantId,
                    BuildCodDeliveredAccountingIdempotencyKey(order.TenantId, order.Id),
                    ct);
                return new AccountingStageResult(message, outboxResult);
            }

            case PaymentLifecycleSource.PaymentReviewApproval:
                await _accountingOutbox.EnqueueWalletApprovedAsync(order.TenantId, order.Id, ct);
                return new AccountingStageResult(
                    await LoadRequiredAccountingOutboxMessageAsync(
                        order.TenantId,
                        BuildWalletApprovedAccountingIdempotencyKey(order.TenantId, order.Id),
                        ct),
                    null);

            default:
                if (routedToSuspense)
                {
                    await _accountingOutbox.EnqueueResolvedUnresolvedOnlinePaymentAsync(
                        order.TenantId,
                        order.Id,
                        amount,
                        gateway,
                        request.WebhookEventId!,
                        ct);
                    return new AccountingStageResult(
                        await LoadRequiredAccountingOutboxMessageAsync(
                            order.TenantId,
                            BuildResolvedUnresolvedPaymentAccountingIdempotencyKey(
                                order.TenantId,
                                order.Id,
                                request.WebhookEventId!),
                            ct),
                        null);
                }

                var paymentIntentId = resolvedPaymentReference ??
                                      throw new InvalidOperationException(
                                          $"Online payment accounting for order {order.Id} requires a payment reference.");
                await _accountingOutbox.EnqueueOnlinePaymentAsync(
                    order.TenantId,
                    order.Id,
                    paymentIntentId,
                    gateway,
                    ct);
                return new AccountingStageResult(
                    await LoadRequiredAccountingOutboxMessageAsync(
                        order.TenantId,
                        BuildOnlinePaymentAccountingIdempotencyKey(order.TenantId, order.Id, paymentIntentId),
                        ct),
                    null);
        }
    }

    private async Task PostCommittedCollectionAccountingFromSqlTruthAsync(
        Guid tenantId,
        Guid orderId,
        Guid accountingMessageId,
        DateTime postedAtUtc,
        CancellationToken ct)
    {
        _db.ChangeTracker.Clear();
        await using var accountingTransaction = await BeginLifecycleTransactionAsync(ct);

        try
        {
            var order = await LoadOrderForPaymentLifecycleMutationAsync(
                orderId,
                ignoreQueryFilters: true,
                ct);

            if (order == null || order.TenantId != tenantId)
            {
                throw new InvalidOperationException(
                    $"Cannot post collection accounting because order {orderId} was not found for tenant {tenantId}.");
            }

            if (!IsCollectionAccountingEligibleFromSqlTruth(order))
            {
                throw new InvalidOperationException(
                    $"Cannot post collection accounting for order {order.OrderNumber} from SQL payment status {order.PaymentStatus}.");
            }

            var accountingMessage = await LoadRequiredAccountingOutboxMessageByIdAsync(
                tenantId,
                accountingMessageId,
                ct);

            if (accountingMessage.ProcessedAtUtc.HasValue &&
                order.FinancialPostingStatus == FinancialPostingStatus.Posted)
            {
                await accountingTransaction.CommitAsync(ct);
                return;
            }

            var authoritativeRequest = BuildCollectionAccountingRequestFromSqlTruth(
                order,
                accountingMessage);
            var authoritativeAmount = ResolveCollectionAccountingAmountFromSqlTruth(
                order,
                accountingMessage);
            var routedToSuspense = accountingMessage.EventType == "ResolvedUnresolvedOnlinePayment";
            var resolvedPaymentReference = ResolvePaymentExternalReference(
                order,
                authoritativeRequest,
                accountingMessage);

            var postingResult = await PostSuccessfulCollectionAccountingAsync(
                order,
                authoritativeRequest,
                authoritativeAmount,
                routedToSuspense,
                resolvedPaymentReference,
                ct);

            EnsurePostingSucceeded(postingResult, order.Id, "collection");

            order.FinancialPostingStatus = FinancialPostingStatus.Posted;
            order.FinancialPostingPendingAtUtc ??= postedAtUtc;
            order.FinancialPostedAtUtc ??= postedAtUtc;
            order.FinancialPostingJournalId = postingResult.JournalId;
            order.FinancialPostingFailureReason = null;
            accountingMessage.MarkProcessed(postingResult.JournalId ?? Guid.Empty);

            await PublishOrderCollectionAccountingPostedEventAsync(
                order,
                authoritativeRequest,
                accountingMessage,
                postingResult.JournalId ?? Guid.Empty,
                postedAtUtc,
                authoritativeAmount,
                ct);

            await EnqueueAccountingConfirmedMarketplacePaymentCompletedAsync(
                order,
                authoritativeRequest,
                accountingMessage,
                postedAtUtc,
                ct);

            await _db.SaveChangesAsync(ct);
            await accountingTransaction.CommitAsync(ct);
        }
        catch
        {
            await accountingTransaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task PostCommittedRefundAccountingFromSqlTruthAsync(
        Guid tenantId,
        Guid orderId,
        Guid refundEntityId,
        Guid accountingMessageId,
        CancellationToken ct)
    {
        _db.ChangeTracker.Clear();
        await using var accountingTransaction = await BeginLifecycleTransactionAsync(ct);

        try
        {
            var order = await LoadOrderForRefundMutationAsync(orderId, ct);
            if (order == null || order.TenantId != tenantId)
            {
                throw new InvalidOperationException(
                    $"Cannot post refund accounting because order {orderId} was not found for tenant {tenantId}.");
            }

            var refund = order.Refunds.FirstOrDefault(candidate =>
                candidate.Id == refundEntityId &&
                !candidate.IsDeleted);

            if (refund == null)
            {
                throw new InvalidOperationException(
                    $"Cannot post refund accounting because refund {refundEntityId} was not found for order {orderId}.");
            }

            var accountingMessage = await LoadRequiredAccountingOutboxMessageByIdAsync(
                tenantId,
                accountingMessageId,
                ct);

            if (accountingMessage.ProcessedAtUtc.HasValue &&
                refund.FinancialPostingStatus == FinancialPostingStatus.Posted)
            {
                await accountingTransaction.CommitAsync(ct);
                return;
            }

            var refundAuthority = await AccountingLedgerAuthority.LoadCurrentRefundAsync(
                _db,
                tenantId,
                orderId,
                refund.RefundId,
                ct) ?? throw new InvalidOperationException(
                $"Cannot post refund accounting because refund {refund.RefundId} lost current SQL authority.");

            var authority = AccountingLedgerAuthority.EvaluateRefundPosting(refundAuthority);
            if (!authority.ShouldPost)
            {
                if (authority.ShouldRetry)
                {
                    throw new InvalidOperationException(authority.Reason);
                }

                await accountingTransaction.CommitAsync(ct);
                return;
            }

            var refundAccountingContext = AccountingLedgerAuthority.ToRefundAccountingContext(refundAuthority)
                ?? BuildRefundAccountingContextFromSqlTruth(refund);
            var refundPosting = await _commerceAccounting.PostEcommerceRefundAsync(
                order,
                refundAuthority.Amount,
                refundAuthority.RefundId,
                refundAccountingContext,
                ct);

            EnsurePostingSucceeded(refundPosting, orderId, "refund");

            refund.FinancialPostingStatus = FinancialPostingStatus.Posted;
            refund.FinancialPostedAtUtc ??= DateTime.UtcNow;
            refund.FinancialPostingJournalId = refundPosting.JournalId;
            refund.FinancialPostingFailureReason = null;
            accountingMessage.MarkProcessed(refundPosting.JournalId ?? Guid.Empty);

            await EnqueueAccountingConfirmedMarketplaceRefundProcessedAsync(
                order,
                refund,
                accountingMessage,
                ct);

            await _db.SaveChangesAsync(ct);
            await accountingTransaction.CommitAsync(ct);
        }
        catch
        {
            await accountingTransaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task PostCommittedGatewaySettlementAccountingFromSqlTruthAsync(
        Guid tenantId,
        Guid settlementBatchId,
        string accountingIdempotencyKey,
        CancellationToken ct)
    {
        _db.ChangeTracker.Clear();
        await using var accountingTransaction = await BeginLifecycleTransactionAsync(ct);

        try
        {
            var settlementBatch = await LoadSettlementBatchForAccountingPostAsync(
                tenantId,
                settlementBatchId,
                SettlementTypeGateway,
                ct);

            var accountingMessage = await LoadRequiredAccountingOutboxMessageAsync(
                tenantId,
                accountingIdempotencyKey,
                ct);

            if (accountingMessage.ProcessedAtUtc.HasValue)
            {
                await accountingTransaction.CommitAsync(ct);
                return;
            }

            var settlementAuthority = await LoadRequiredSettlementAuthorityForAccountingPostAsync(
                tenantId,
                settlementBatchId,
                SettlementTypeGateway,
                settlementBatch.SettlementId,
                ct);

            var postingResult = await _commerceAccounting.PostGatewaySettlementAsync(
                tenantId,
                settlementBatchId,
                settlementAuthority.Provider ?? throw new InvalidOperationException(
                    $"Gateway settlement batch {settlementBatchId} is missing provider authority."),
                settlementAuthority.GrossAmount,
                settlementAuthority.Fees,
                settlementAuthority.SettlementId,
                settlementAuthority.SettledAtUtc,
                ct);

            EnsurePostingSucceeded(postingResult, settlementBatchId, "settlement");

            accountingMessage.MarkProcessed(postingResult.JournalId ?? Guid.Empty);
            await _db.SaveChangesAsync(ct);
            await accountingTransaction.CommitAsync(ct);
        }
        catch
        {
            await accountingTransaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task PostCommittedCodSettlementAccountingFromSqlTruthAsync(
        Guid tenantId,
        Guid settlementBatchId,
        string accountingIdempotencyKey,
        CancellationToken ct)
    {
        _db.ChangeTracker.Clear();
        await using var accountingTransaction = await BeginLifecycleTransactionAsync(ct);

        try
        {
            var settlementBatch = await LoadSettlementBatchForAccountingPostAsync(
                tenantId,
                settlementBatchId,
                SettlementTypeOfflineCollection,
                ct);

            var accountingMessage = await LoadRequiredAccountingOutboxMessageAsync(
                tenantId,
                accountingIdempotencyKey,
                ct);

            if (accountingMessage.ProcessedAtUtc.HasValue)
            {
                await accountingTransaction.CommitAsync(ct);
                return;
            }

            var settlementAuthority = await LoadRequiredSettlementAuthorityForAccountingPostAsync(
                tenantId,
                settlementBatchId,
                SettlementTypeOfflineCollection,
                settlementBatch.SettlementId,
                ct);

            var postingResult = await _commerceAccounting.PostCodSettlementAsync(
                tenantId,
                settlementBatchId,
                settlementAuthority.GrossAmount,
                settlementAuthority.Fees,
                settlementAuthority.SettlementId,
                settlementAuthority.SettledAtUtc,
                ct);

            EnsurePostingSucceeded(postingResult, settlementBatchId, "settlement");

            accountingMessage.MarkProcessed(postingResult.JournalId ?? Guid.Empty);
            await _db.SaveChangesAsync(ct);
            await accountingTransaction.CommitAsync(ct);
        }
        catch
        {
            await accountingTransaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<PaymentSettlementBatch> LoadSettlementBatchForAccountingPostAsync(
        Guid tenantId,
        Guid settlementBatchId,
        string settlementType,
        CancellationToken ct)
    {
        IQueryable<PaymentSettlementBatch> query;

        if (IsPostgresProvider())
        {
            query = _db.PaymentSettlementBatches.FromSqlInterpolated($@"
                SELECT *
                FROM ""PaymentSettlementBatches""
                WHERE ""TenantId"" = {tenantId}
                  AND ""Id"" = {settlementBatchId}
                  AND ""SettlementType"" = {settlementType}
                  AND ""IsDeleted"" = false
                FOR UPDATE").IgnoreQueryFilters();
        }
        else if (IsSqlServerProvider())
        {
            query = _db.PaymentSettlementBatches.FromSqlInterpolated($@"
                SELECT *
                FROM [PaymentSettlementBatches] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [TenantId] = {tenantId}
                  AND [Id] = {settlementBatchId}
                  AND [SettlementType] = {settlementType}
                  AND [IsDeleted] = 0").IgnoreQueryFilters();
        }
        else
        {
            query = _db.PaymentSettlementBatches
                .IgnoreQueryFilters()
                .Where(batch =>
                    batch.TenantId == tenantId &&
                    batch.Id == settlementBatchId &&
                    batch.SettlementType == settlementType &&
                    !batch.IsDeleted);
        }

        return await query.FirstOrDefaultAsync(ct) ??
               throw new InvalidOperationException(
                   $"Settlement batch {settlementBatchId} was not found for tenant {tenantId}.");
    }

    private async Task<SettlementAccountingAuthoritySnapshot> LoadRequiredSettlementAuthorityForAccountingPostAsync(
        Guid tenantId,
        Guid settlementBatchId,
        string settlementType,
        string settlementId,
        CancellationToken ct)
    {
        var settlementAuthority = await AccountingLedgerAuthority.LoadCurrentSettlementAsync(
            _db,
            tenantId,
            settlementBatchId,
            settlementType,
            settlementId,
            ct);
        var authority = AccountingLedgerAuthority.EvaluateSettlementPosting(settlementAuthority);
        if (!authority.ShouldPost || settlementAuthority == null)
        {
            throw new InvalidOperationException(
                authority.Reason ?? $"Settlement batch {settlementBatchId} lost current SQL accounting authority.");
        }

        return settlementAuthority;
    }

    private async Task EnsureSettlementBatchRetainsCurrentOrderLineageAsync(
        PaymentSettlementBatch settlementBatch,
        Guid? requiredOrderId,
        CancellationToken ct)
    {
        var currentSettlementOrders = _db.PaymentSettlementOrders
            .IgnoreQueryFilters()
            .Where(order =>
                order.TenantId == settlementBatch.TenantId &&
                order.SettlementBatchId == settlementBatch.Id &&
                !order.IsDeleted);

        var currentOrderCount = await currentSettlementOrders.CountAsync(ct);

        if (currentOrderCount <= 0)
        {
            throw new InvalidOperationException(
                $"Settlement batch {settlementBatch.Id} has no current settlement-order lineage.");
        }

        if (requiredOrderId.HasValue)
        {
            var retainsOrder = await currentSettlementOrders.AnyAsync(
                order => order.OrderId == requiredOrderId.Value,
                ct);

            if (!retainsOrder)
            {
                throw new InvalidOperationException(
                    $"Settlement batch {settlementBatch.Id} no longer retains order {requiredOrderId.Value} in current settlement-order lineage.");
            }
        }

        if (settlementBatch.OrderCount != currentOrderCount)
        {
            throw new InvalidOperationException(
                $"Settlement batch {settlementBatch.Id} order lineage drifted from recorded count {settlementBatch.OrderCount} to current count {currentOrderCount}.");
        }
    }

    private static EcommerceRefundAccountingContext? BuildRefundAccountingContextFromSqlTruth(OrderRefund refund)
    {
        if (!refund.IsPostSettlementRefund)
        {
            return null;
        }

        return new EcommerceRefundAccountingContext(
            IsPostSettlementRefund: true,
            SettlementBatchId: refund.SettlementBatchId,
            SettlementType: refund.SettlementType,
            SettlementId: refund.SettlementId,
            PayoutBatchId: refund.PayoutBatchId,
            SettledAtUtc: refund.SettledAtUtc);
    }

    private static (string Message, string ErrorCode)? ValidateSettlementAmountBounds(
        string settlementLabel,
        decimal grossAmount,
        decimal fees)
    {
        if (grossAmount <= 0m)
        {
            return ($"{settlementLabel} settlement gross amount must be positive.", "invalid_gross_amount");
        }

        if (fees < 0m || fees > grossAmount)
        {
            return ($"{settlementLabel} settlement fees must be between zero and the gross amount.", "invalid_fees");
        }

        return null;
    }

    private static bool IsCollectionAccountingEligibleFromSqlTruth(Order order) =>
        order.PaymentStatus is PaymentStatus.Collected
            or PaymentStatus.Paid
            or PaymentStatus.Settled
            or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded;

    private static HandlePaymentResultRequest BuildCollectionAccountingRequestFromSqlTruth(
        Order order,
        AccountingOutboxMessage message)
    {
        var gateway = TryGetPayloadString(message.PayloadJson, "Gateway") ??
                      TryGetPayloadString(message.PayloadJson, "Provider") ??
                      order.PaymentGateway ??
                      order.PaymentMethodCode;
        var paymentReference = TryGetPayloadString(message.PayloadJson, "PaymentIntentId") ??
                               order.PaymentReference ??
                               message.IdempotencyKey;

        return new HandlePaymentResultRequest
        {
            OrderId = order.Id,
            Outcome = PaymentLifecycleOutcome.Succeeded,
            Source = message.EventType switch
            {
                "CodDelivered" => PaymentLifecycleSource.OfflineCollection,
                "WalletApproved" => PaymentLifecycleSource.PaymentReviewApproval,
                _ => PaymentLifecycleSource.GatewayWebhook
            },
            Gateway = gateway,
            PaymentReference = paymentReference,
            ProviderPaymentId = paymentReference,
            ProviderAmount = ResolveCollectionAccountingAmountFromSqlTruth(order, message),
            ProviderCurrency = order.Currency,
            Amount = ResolveCollectionAccountingAmountFromSqlTruth(order, message),
            Currency = order.Currency,
            WebhookEventId = TryGetPayloadString(message.PayloadJson, "ProviderEventId"),
            ProcessedAtUtc = message.ProcessedAtUtc ?? DateTime.UtcNow
        };
    }

    private static decimal ResolveCollectionAccountingAmountFromSqlTruth(
        Order order,
        AccountingOutboxMessage message)
    {
        return TryGetPayloadDecimal(message.PayloadJson, "AmountPaid") ??
               TryGetPayloadDecimal(message.PayloadJson, "AmountCaptured") ??
               TryGetPayloadDecimal(message.PayloadJson, "AmountCollected") ??
               TryGetPayloadDecimal(message.PayloadJson, "AmountApproved") ??
               TryGetPayloadDecimal(message.PayloadJson, "Amount") ??
               (order.TotalPaidAmount > 0m ? order.TotalPaidAmount : order.Total);
    }

    private static string? TryGetPayloadString(string payloadJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            return document.RootElement.TryGetProperty(propertyName, out var property)
                ? property.ValueKind == JsonValueKind.String
                    ? property.GetString()
                    : property.ToString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static decimal? TryGetPayloadDecimal(string payloadJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (!document.RootElement.TryGetProperty(propertyName, out var property))
            {
                return null;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var number))
            {
                return number;
            }

            return property.ValueKind == JsonValueKind.String &&
                   decimal.TryParse(property.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<PostingResult> PostSuccessfulCollectionAccountingAsync(
        Order order,
        HandlePaymentResultRequest request,
        decimal amount,
        bool routedToSuspense,
        string? resolvedPaymentReference,
        CancellationToken ct)
    {
        var gateway = ResolveGateway(order, request);

        if (request.Source == PaymentLifecycleSource.OfflineCollection)
        {
            return await _commerceAccounting.PostCodDeliveredAsync(order, ct);
        }

        if (request.Source == PaymentLifecycleSource.PaymentReviewApproval)
        {
            return await _commerceAccounting.PostWalletApprovedAsync(order, ct);
        }

        if (routedToSuspense)
        {
            return await _commerceAccounting.PostResolvedUnresolvedPaymentAsync(
                order,
                amount,
                gateway,
                request.WebhookEventId ?? throw new InvalidOperationException(
                    $"Resolved suspense posting for order {order.Id} requires WebhookEventId."),
                ct);
        }

        return await _commerceAccounting.PostOnlinePaymentCapturedAsync(
            order,
            resolvedPaymentReference ??
            throw new InvalidOperationException(
                $"Online payment posting for order {order.Id} requires a payment reference."),
            ResolveGateway(order, request),
            ct);
    }

    private static void EnsurePostingSucceeded(PostingResult result, Guid entityId, string operationName)
    {
        if (result.Success && result.JournalId.HasValue && result.JournalId.Value != Guid.Empty)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Synchronous {operationName} accounting failed for entity {entityId}: {result.ErrorMessage ?? "journal id missing"}");
    }

    private async Task<AccountingOutboxMessage> LoadRequiredAccountingOutboxMessageAsync(
        Guid tenantId,
        string idempotencyKey,
        CancellationToken ct)
    {
        var message = await _db.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate =>
                candidate.TenantId == tenantId &&
                candidate.IdempotencyKey == idempotencyKey,
                ct);

        return message ?? throw new InvalidOperationException(
            $"Accounting outbox message with key '{idempotencyKey}' was not staged.");
    }

    private async Task<AccountingOutboxMessage> LoadRequiredAccountingOutboxMessageByIdAsync(
        Guid tenantId,
        Guid messageId,
        CancellationToken ct)
    {
        var message = await _db.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate =>
                candidate.TenantId == tenantId &&
                candidate.Id == messageId,
                ct);

        return message ?? throw new InvalidOperationException(
            $"Accounting outbox message '{messageId}' was not staged for tenant {tenantId}.");
    }

    private async Task PublishOrderCollectionAccountingPostedEventAsync(
        Order order,
        HandlePaymentResultRequest request,
        AccountingOutboxMessage message,
        Guid journalId,
        DateTime postedAtUtc,
        decimal amount,
        CancellationToken ct)
    {
        var eventVersion = PointOfSale.Core.Helpers.ProductEventVersionHelper.ToProductEventVersion(
            order.ProjectionMutationVersion + 1);

        await _eventPublisher.PublishAsync(new OrderCollectionAccountingPostedEvent
        {
            EventId = BuildDeterministicEventId(
                "storefront:collection-accounting-posted",
                message.Id),
            TenantId = order.TenantId,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            Provider = ResolveStorefrontAccountingProvider(order, request, message),
            PaymentMethod = ResolveMarketplacePaymentMethod(order, request, message),
            AmountPosted = amount,
            Currency = order.Currency,
            FinancialPostingJournalId = journalId,
            NewFinancialPostingStatus = order.FinancialPostingStatus,
            AccountingEventType = message.EventType,
            ExternalReference = ResolvePaymentExternalReference(order, request, message),
            PostedAt = postedAtUtc,
            Version = eventVersion,
            CorrelationId = message.CorrelationId ?? _correlationIdAccessor.GetCorrelationId(),
            IdempotencyKey = $"ORDER:ACCOUNTING_POSTED:{message.Id:N}",
            OccurredAtUtc = postedAtUtc
        }, ct);
    }

    private async Task EnqueueAccountingConfirmedMarketplacePaymentCompletedAsync(
        Order order,
        HandlePaymentResultRequest request,
        AccountingOutboxMessage message,
        DateTime completedAtUtc,
        CancellationToken ct)
    {
        var eventVersion = PointOfSale.Core.Helpers.ProductEventVersionHelper.ToProductEventVersion(
            order.ProjectionMutationVersion + 1);

        await _marketplaceEventOutbox.EnqueueAsync(new PointOfSale.Core.Events.Marketplace.PaymentCompletedEvent
        {
            EventId = BuildDeterministicEventId(
                "marketplace:payment-completed:accounting-posted",
                message.Id),
            CorrelationId = message.CorrelationId ?? _correlationIdAccessor.GetCorrelationId(),
            EntityId = order.Id,
            AggregateRootId = order.Id,
            TenantId = order.TenantId,
            Version = eventVersion,
            OrderNumber = order.OrderNumber,
            PaymentMethod = ResolveMarketplacePaymentMethod(order, request, message),
            ExternalReference = ResolvePaymentExternalReference(order, request, message),
            Amount = order.Total,
            Currency = order.Currency,
            CompletionSource = $"AccountingPosted:{message.EventType}",
            CompletedAtUtc = completedAtUtc,
            OccurredAtUtc = completedAtUtc
        }, ct);
    }

    private async Task EnqueueAccountingConfirmedMarketplaceRefundProcessedAsync(
        Order order,
        OrderRefund refund,
        AccountingOutboxMessage message,
        CancellationToken ct)
    {
        var eventVersion = PointOfSale.Core.Helpers.ProductEventVersionHelper.ToProductEventVersion(
            order.ProjectionMutationVersion + 1);
        var processedAtUtc = refund.FinancialPostedAtUtc ?? refund.RefundedAtUtc;

        await _marketplaceEventOutbox.EnqueueAsync(new PointOfSale.Core.Events.Marketplace.RefundProcessedEvent
        {
            EventId = BuildDeterministicEventId(
                "marketplace:refund-processed:accounting-posted",
                message.Id),
            CorrelationId = message.CorrelationId ?? _correlationIdAccessor.GetCorrelationId(),
            EntityId = refund.OrderId,
            AggregateRootId = refund.OrderId,
            TenantId = refund.TenantId,
            Version = eventVersion,
            OrderNumber = order.OrderNumber,
            RefundId = refund.RefundId,
            Amount = refund.Amount,
            Currency = refund.Currency,
            IsFullRefund = refund.IsFullRefund,
            Reason = refund.Reason,
            PaymentMethod = refund.PaymentMethod,
            ProcessedAtUtc = processedAtUtc,
            OccurredAtUtc = processedAtUtc
        }, ct);
    }

    private static Guid BuildDeterministicEventId(string purpose, Guid sourceId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{purpose}:{sourceId:N}"));
        return new Guid(hash[..16]);
    }

    private static string ResolveMarketplacePaymentMethod(
        Order order,
        HandlePaymentResultRequest request,
        AccountingOutboxMessage message)
    {
        if (!string.IsNullOrWhiteSpace(order.PaymentMethodCode))
        {
            return order.PaymentMethodCode;
        }

        return message.EventType switch
        {
            "CodDelivered" => "COD",
            "WalletApproved" => "WALLET",
            "OnlinePayment" or "ResolvedUnresolvedOnlinePayment" => ResolveGateway(order, request),
            _ => message.EventType
        };
    }

    private static string ResolveStorefrontAccountingProvider(
        Order order,
        HandlePaymentResultRequest request,
        AccountingOutboxMessage message)
    {
        return message.EventType switch
        {
            "CodDelivered" => "COD",
            "WalletApproved" => "WALLET",
            "OnlinePayment" or "ResolvedUnresolvedOnlinePayment" =>
                ResolveGateway(order, request),
            _ => order.PaymentMethodCode ?? message.EventType
        };
    }

    private static string? ResolvePaymentExternalReference(
        Order order,
        HandlePaymentResultRequest request,
        AccountingOutboxMessage message)
    {
        return request.PaymentReference ??
               order.PaymentReference ??
               message.IdempotencyKey;
    }

    private static string BuildOnlinePaymentAccountingIdempotencyKey(Guid tenantId, Guid orderId, string paymentIntentId) =>
        $"ONLINE_PAYMENT:{tenantId}:{orderId}:{paymentIntentId}";

    private static string BuildResolvedUnresolvedPaymentAccountingIdempotencyKey(Guid tenantId, Guid orderId, string providerEventId) =>
        $"RESOLVED_UNRESOLVED_PAYMENT:{tenantId}:{orderId}:{providerEventId}";

    private static string BuildCodDeliveredAccountingIdempotencyKey(Guid tenantId, Guid orderId) =>
        $"COD_DELIVERED:{tenantId}:{orderId}";

    private static string BuildWalletApprovedAccountingIdempotencyKey(Guid tenantId, Guid orderId) =>
        $"WALLET_APPROVED:{tenantId}:{orderId}";

    private static string BuildEcommerceRefundAccountingIdempotencyKey(Guid tenantId, Guid orderId, string refundId) =>
        $"ECOM_REFUND:{tenantId}:{orderId}:{refundId}";

    private sealed record AccountingStageResult(
        AccountingOutboxMessage Message,
        OutboxEnqueueResult? OutboxResult);

    private async Task<bool> WasPreviouslyRoutedToSuspenseAsync(
        Order order,
        HandlePaymentResultRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.WebhookEventId) || string.IsNullOrWhiteSpace(request.Gateway))
        {
            return false;
        }

        return await _db.PaymentWebhookLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                log =>
                    !log.IsDeleted &&
                    log.TenantId == order.TenantId &&
                    log.Provider == request.Gateway &&
                    log.ProviderEventId == request.WebhookEventId &&
                    log.ProcessingError != null &&
                    (log.ProcessingError == "Payment intent not found - Routed to Suspense Ledger" ||
                     EF.Functions.Like(log.ProcessingError, "UNRESOLVED_SUSPENSE_ROUTED%")),
                ct);
    }

    private async Task PublishSuccessDomainEventAsync(
        Order order,
        HandlePaymentResultRequest request,
        decimal amount,
        DateTime now,
        CancellationToken ct)
    {
        var correlationId = _correlationIdAccessor.GetCorrelationId();

        switch (request.Source)
        {
            case PaymentLifecycleSource.OfflineCollection:
                await _eventPublisher.PublishAsync(new OfflinePaymentCollectedEvent
                {
                    TenantId = order.TenantId,
                    OrderId = order.Id,
                    OrderNumber = order.OrderNumber,
                    AmountCollected = order.Total,
                    DeliveryReference = request.PaymentReference,
                    ConfirmedBy = request.Actor,
                    DeliveredAt = now,
                    CorrelationId = correlationId,
                    IdempotencyKey = BuildCodDeliveredIdempotencyKey(order.TenantId, order.Id),
                    OccurredAtUtc = now
                }, ct);
                break;

            case PaymentLifecycleSource.PaymentReviewApproval:
                await _eventPublisher.PublishAsync(new ReviewedPaymentApprovedEvent
                {
                    TenantId = order.TenantId,
                    OrderId = order.Id,
                    OrderNumber = order.OrderNumber,
                    AmountApproved = amount,
                    ApprovedBy = request.Actor ?? "system",
                    ProviderTransactionId = request.ProviderPaymentId ?? request.PaymentReference,
                    ApprovedAt = now,
                    CorrelationId = correlationId,
                    IdempotencyKey = BuildWalletApprovedIdempotencyKey(order.TenantId, order.Id),
                    OccurredAtUtc = now
                }, ct);
                break;

            default:
                await _eventPublisher.PublishAsync(new OnlinePaymentCapturedEvent
                {
                    TenantId = order.TenantId,
                    OrderId = order.Id,
                    OrderNumber = order.OrderNumber,
                    Gateway = ResolveGateway(order, request),
                    PaymentIntentId = request.PaymentReference ?? order.PaymentReference,
                    AmountCaptured = amount,
                    Currency = request.Currency!,
                    CapturedAt = now,
                    CorrelationId = correlationId,
                    IdempotencyKey = BuildGatewayCaptureIdempotencyKey(order, request),
                    OccurredAtUtc = now
                }, ct);
                break;
        }
    }

    private async Task PublishFailureDomainEventAsync(
        Order order,
        HandlePaymentResultRequest request,
        DateTime now,
        CancellationToken ct)
    {
        var correlationId = _correlationIdAccessor.GetCorrelationId();

        // Wallet rejection - specific event for wallet payments
        if (request.Source == PaymentLifecycleSource.PaymentReviewRejection)
        {
            await _eventPublisher.PublishAsync(new ReviewedPaymentRejectedEvent
            {
                TenantId = order.TenantId,
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                RejectedBy = request.Actor ?? "system",
                Reason = request.Reason ?? "Payment rejected",
                RejectedAt = now,
                CorrelationId = correlationId,
                IdempotencyKey = BuildWalletRejectedIdempotencyKey(order.TenantId, order.Id),
                OccurredAtUtc = now
            }, ct);
        }
        else if (request.Outcome == PaymentLifecycleOutcome.Cancelled)
        {
            await _eventPublisher.PublishAsync(new PaymentCancelledEvent
            {
                TenantId = order.TenantId,
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                Gateway = ResolveGateway(order, request),
                PaymentIntentId = request.PaymentReference ?? order.PaymentReference,
                Amount = order.Total,
                Currency = request.Currency!,
                Reason = request.Reason ?? "Payment cancelled",
                CancelledBy = request.Actor,
                CancelledAt = now,
                CorrelationId = correlationId,
                IdempotencyKey = BuildPaymentCancelledIdempotencyKey(order, request),
                OccurredAtUtc = now
            }, ct);
        }
        else
        {
            await _eventPublisher.PublishAsync(new OnlinePaymentFailedEvent
            {
                TenantId = order.TenantId,
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                Gateway = ResolveGateway(order, request),
                PaymentIntentId = request.PaymentReference ?? order.PaymentReference,
                Amount = order.Total,
                Currency = request.Currency!,
                Reason = request.Reason ?? "Payment failed",
                FailureCode = request.FailureCode,
                FailedAt = now,
                CorrelationId = correlationId,
                IdempotencyKey = BuildPaymentFailedIdempotencyKey(order, request),
                OccurredAtUtc = now
            }, ct);
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            await _eventPublisher.PublishAsync(new OrderCancelledEvent
            {
                TenantId = order.TenantId,
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                CancellationReason = order.CancellationReason ?? request.Reason ?? request.Outcome.ToString(),
                CancelledAt = order.CancelledAt ?? now,
                RestoreInventory = false,
                CorrelationId = correlationId,
                IdempotencyKey = BuildOrderCancelledIdempotencyKey(order, request),
                OccurredAtUtc = now
            }, ct);
        }
    }

    private async Task PublishCanonicalAuthorizedOutcomeEventAsync(
        Order order,
        PaymentIntent? paymentIntent,
        HandlePaymentResultRequest request,
        DateTime now,
        CancellationToken ct)
    {
        var amount = request.Amount ?? paymentIntent?.Amount ?? order.Total;
        var currency = request.Currency ?? paymentIntent?.Currency ?? order.Currency;

        await _eventPublisher.PublishAsync(new PaymentAuthorizedEvent
        {
            TenantId = order.TenantId,
            OrderId = order.Id,
            PaymentIntentId = paymentIntent?.Id,
            OrderNumber = order.OrderNumber,
            Provider = ResolveGateway(order, request),
            PaymentMethod = order.PaymentMethod.ToString(),
            Amount = amount,
            Currency = currency,
            ProviderAuthorizationId = request.ProviderPaymentId ?? request.PaymentReference,
            AuthorizedAt = now,
            CorrelationId = _correlationIdAccessor.GetCorrelationId(),
            IdempotencyKey = BuildPaymentAuthorizedIdempotencyKey(order, paymentIntent, request),
            OccurredAtUtc = now
        }, ct);
    }

    private async Task PublishCompatibilityPaymentStatusChangedEventAsync(
        Order order,
        PaymentIntent? paymentIntent,
        HandlePaymentResultRequest request,
        PaymentStatus oldStatus,
        decimal? amountPaid,
        DateTime now,
        CancellationToken ct)
    {
        if (oldStatus == order.PaymentStatus)
        {
            return;
        }

        await _eventPublisher.PublishAsync(new OrderPaymentStatusChangedEvent
        {
            TenantId = order.TenantId,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            OldStatus = oldStatus,
            NewStatus = order.PaymentStatus,
            AmountPaid = amountPaid,
            PaymentReference = request.PaymentReference
                ?? paymentIntent?.PaymentReference
                ?? paymentIntent?.ProviderPaymentId
                ?? order.PaymentReference,
            Description = BuildCompatibilityPaymentStatusDescription(request),
            CorrelationId = _correlationIdAccessor.GetCorrelationId(),
            IdempotencyKey = BuildCompatibilityPaymentStatusChangedIdempotencyKey(
                order,
                request,
                oldStatus,
                order.PaymentStatus),
            OccurredAtUtc = now
        }, ct);
    }

    private Task PublishRefundPaymentStatusChangedEventAsync(
        Order order,
        PaymentStatus oldStatus,
        string refundId,
        DateTime now,
        CancellationToken ct)
    {
        return PublishCompatibilityPaymentStatusChangedEventAsync(
            order,
            paymentIntent: null,
            new HandlePaymentResultRequest
            {
                OrderId = order.Id,
                Outcome = PaymentLifecycleOutcome.Succeeded,
                Source = PaymentLifecycleSource.RefundAuthority,
                PaymentReference = refundId,
                Reason = order.PaymentStatus == PaymentStatus.Refunded
                    ? "Payment fully refunded"
                    : "Payment partially refunded",
                SkipPaymentIntentResolution = true
            },
            oldStatus,
            amountPaid: null,
            now,
            ct);
    }

    private Task PublishSettlementPaymentStatusChangedEventAsync(
        Order order,
        PaymentStatus oldStatus,
        string settlementId,
        DateTime now,
        CancellationToken ct)
    {
        return PublishCompatibilityPaymentStatusChangedEventAsync(
            order,
            paymentIntent: null,
            new HandlePaymentResultRequest
            {
                OrderId = order.Id,
                Outcome = PaymentLifecycleOutcome.Succeeded,
                Source = PaymentLifecycleSource.SettlementAuthority,
                PaymentReference = settlementId,
                Reason = "Payment settlement accepted",
                SkipPaymentIntentResolution = true
            },
            oldStatus,
            ResolvePaymentStatusVisibilityAmount(order),
            now,
            ct);
    }

    private async Task PublishCompatibilityOrderStatusChangedEventAsync(
        Order order,
        HandlePaymentResultRequest request,
        OrderStatus oldStatus,
        DateTime now,
        CancellationToken ct)
    {
        if (oldStatus == order.Status)
        {
            return;
        }

        await _eventPublisher.PublishAsync(new OrderStatusChangedEvent
        {
            TenantId = order.TenantId,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            OldStatus = oldStatus,
            NewStatus = order.Status,
            Reason = BuildCompatibilityOrderStatusReason(request),
            CorrelationId = _correlationIdAccessor.GetCorrelationId(),
            IdempotencyKey = BuildCompatibilityOrderStatusChangedIdempotencyKey(
                order,
                request,
                oldStatus,
                order.Status),
            OccurredAtUtc = now
        }, ct);
    }

    private async Task PublishCanonicalSuccessOutcomeEventsAsync(
        Order order,
        PaymentIntent? paymentIntent,
        HandlePaymentResultRequest request,
        decimal amount,
        DateTime now,
        CancellationToken ct)
    {
        var correlationId = _correlationIdAccessor.GetCorrelationId();
        var provider = ResolveGateway(order, request);
        var paymentMethod = order.PaymentMethod.ToString();
        var currency = request.Currency ?? paymentIntent?.Currency ?? order.Currency;

        await _eventPublisher.PublishAsync(new PaymentCapturedEvent
        {
            TenantId = order.TenantId,
            OrderId = order.Id,
            PaymentIntentId = paymentIntent?.Id,
            OrderNumber = order.OrderNumber,
            Provider = provider,
            PaymentMethod = paymentMethod,
            Amount = amount,
            Currency = currency,
            ProviderPaymentId = request.ProviderPaymentId ?? request.PaymentReference,
            CapturedAt = now,
            CorrelationId = correlationId,
            IdempotencyKey = BuildPaymentCapturedIdempotencyKey(order, paymentIntent, request),
            OccurredAtUtc = now
        }, ct);

        await _eventPublisher.PublishAsync(new OrderPaidEvent
        {
            TenantId = order.TenantId,
            OrderId = order.Id,
            PaymentIntentId = paymentIntent?.Id,
            OrderNumber = order.OrderNumber,
            Provider = provider,
            PaymentMethod = paymentMethod,
            AmountPaid = amount,
            Currency = currency,
            NewOrderStatus = order.Status,
            NewPaymentStatus = order.PaymentStatus,
            PaidAt = now,
            CorrelationId = correlationId,
            IdempotencyKey = BuildOrderPaidIdempotencyKey(order),
            OccurredAtUtc = now
        }, ct);

        var items = BuildInventoryConfirmationItems(order);
        if (items.Count > 0)
        {
            await _eventPublisher.PublishAsync(new InventoryConfirmedEvent
            {
                TenantId = order.TenantId,
                OrderId = order.Id,
                PaymentIntentId = paymentIntent?.Id,
                OrderNumber = order.OrderNumber,
                Provider = provider,
                PaymentMethod = paymentMethod,
                Items = items,
                ConfirmedAt = now,
                CorrelationId = correlationId,
                IdempotencyKey = BuildInventoryConfirmedIdempotencyKey(order),
                OccurredAtUtc = now
            }, ct);
        }
    }

    private async Task PublishCanonicalFailureOutcomeEventsAsync(
        Order order,
        PaymentIntent? paymentIntent,
        HandlePaymentResultRequest request,
        DateTime now,
        CancellationToken ct)
    {
        var correlationId = _correlationIdAccessor.GetCorrelationId();
        var provider = ResolveGateway(order, request);
        var paymentMethod = order.PaymentMethod.ToString();
        var currency = request.Currency ?? paymentIntent?.Currency ?? order.Currency;
        var reason = request.Reason ?? request.Outcome.ToString();

        await _eventPublisher.PublishAsync(new PaymentFailedEvent
        {
            TenantId = order.TenantId,
            OrderId = order.Id,
            PaymentIntentId = paymentIntent?.Id,
            OrderNumber = order.OrderNumber,
            Provider = provider,
            PaymentMethod = paymentMethod,
            Amount = request.Amount ?? paymentIntent?.Amount ?? order.Total,
            Currency = currency,
            FailureReason = reason,
            FailureCode = request.FailureCode,
            FailedAt = now,
            CorrelationId = correlationId,
            IdempotencyKey = BuildPaymentFailedOutcomeIdempotencyKey(order, request),
            OccurredAtUtc = now
        }, ct);

        var items = BuildInventoryConfirmationItems(order);
        if (items.Count > 0)
        {
            await _eventPublisher.PublishAsync(new InventoryReleasedEvent
            {
                TenantId = order.TenantId,
                OrderId = order.Id,
                PaymentIntentId = paymentIntent?.Id,
                OrderNumber = order.OrderNumber,
                Provider = provider,
                PaymentMethod = paymentMethod,
                ReleaseReason = reason,
                Items = items,
                ReleasedAt = now,
                CorrelationId = correlationId,
                IdempotencyKey = BuildInventoryReleasedIdempotencyKey(order, request),
                OccurredAtUtc = now
            }, ct);
        }
    }

    private static List<InventoryConfirmationItem> BuildInventoryConfirmationItems(Order order) =>
        order.Items
            .Where(item => !item.IsDeleted && item.Quantity > 0)
            .Select(item => new InventoryConfirmationItem
            {
                ProductId = item.ProductId,
                SKU = item.SKU,
                Quantity = item.Quantity,
                WarehouseId = item.WarehouseId
            })
            .ToList();

    private static string BuildCodDeliveredIdempotencyKey(Guid tenantId, Guid orderId) =>
        $"COD_DELIVERED:{tenantId}:{orderId}";

    private static string BuildPaymentAuthorizedIdempotencyKey(
        Order order,
        PaymentIntent? paymentIntent,
        HandlePaymentResultRequest request) =>
        $"PAYMENT_AUTHORIZED:{order.TenantId}:{order.Id}:{paymentIntent?.Id.ToString() ?? request.PaymentReference ?? "none"}";

    private static string BuildPaymentCapturedIdempotencyKey(
        Order order,
        PaymentIntent? paymentIntent,
        HandlePaymentResultRequest request) =>
        $"PAYMENT_CAPTURED:{order.TenantId}:{order.Id}:{paymentIntent?.Id.ToString() ?? request.PaymentReference ?? "none"}";

    private static string BuildOrderPaidIdempotencyKey(Order order) =>
        $"ORDER_PAID:{order.TenantId}:{order.Id}";

    private static string BuildCompatibilityPaymentStatusChangedIdempotencyKey(
        Order order,
        HandlePaymentResultRequest request,
        PaymentStatus oldStatus,
        PaymentStatus newStatus)
    {
        var paymentReference = request.PaymentReference ?? order.PaymentReference ?? order.Id.ToString("N");
        return $"ORDER_PAYMENT_STATUS_CHANGED:{order.TenantId}:{order.Id}:{oldStatus}:{newStatus}:{request.Source}:{paymentReference}";
    }

    private static string BuildCompatibilityOrderStatusChangedIdempotencyKey(
        Order order,
        HandlePaymentResultRequest request,
        OrderStatus oldStatus,
        OrderStatus newStatus)
    {
        var paymentReference = request.PaymentReference ?? order.PaymentReference ?? order.Id.ToString("N");
        return $"ORDER_STATUS_CHANGED:{order.TenantId}:{order.Id}:{oldStatus}:{newStatus}:{request.Source}:{paymentReference}";
    }

    private static string BuildInventoryConfirmedIdempotencyKey(Order order) =>
        $"INVENTORY_CONFIRMED:{order.TenantId}:{order.Id}";

    private static string BuildPaymentFailedOutcomeIdempotencyKey(
        Order order,
        HandlePaymentResultRequest request) =>
        $"PAYMENT_FAILED:{order.TenantId}:{order.Id}:{request.Outcome}:{request.WebhookEventId ?? request.PaymentReference ?? "none"}";

    private static string BuildInventoryReleasedIdempotencyKey(
        Order order,
        HandlePaymentResultRequest request) =>
        $"INVENTORY_RELEASED:{order.TenantId}:{order.Id}:{request.Outcome}";

    private static string BuildCodSettlementIdempotencyKey(Guid tenantId, string settlementId) =>
        $"COD_SETTLEMENT:{tenantId}:{settlementId}";

    private static string BuildGatewaySettlementIdempotencyKey(Guid tenantId, string settlementId) =>
        $"GATEWAY_SETTLEMENT:{tenantId}:{settlementId}";

    private static string BuildWalletApprovedIdempotencyKey(Guid tenantId, Guid orderId) =>
        $"WALLET_APPROVED:{tenantId}:{orderId}";

    private static string BuildWalletRejectedIdempotencyKey(Guid tenantId, Guid orderId) =>
        $"WALLET_REJECTED:{tenantId}:{orderId}";

    private static string BuildRefundIdempotencyKey(Guid tenantId, Guid orderId, string refundId) =>
        $"ECOM_REFUND:{tenantId}:{orderId}:{refundId}";

    private static string BuildRefundItemIdempotencyKey(Guid tenantId, Guid orderId, string refundId, Guid orderItemId) =>
        $"ECOM_REFUND_ITEM:{tenantId}:{orderId}:{refundId}:{orderItemId}";

    private static string BuildGatewayCaptureIdempotencyKey(Order order, HandlePaymentResultRequest request)
    {
        var paymentReference = request.PaymentReference ?? order.PaymentReference ?? order.Id.ToString("N");
        return $"GATEWAY_CAPTURE:{order.TenantId}:{paymentReference}";
    }

    private static string BuildPaymentCancelledIdempotencyKey(Order order, HandlePaymentResultRequest request)
    {
        var paymentReference = request.PaymentReference ?? order.PaymentReference ?? order.Id.ToString("N");
        return $"PAYMENT_CANCELLED:{order.TenantId}:{order.Id}:{paymentReference}";
    }

    private static string BuildOrderCancelledIdempotencyKey(Order order, HandlePaymentResultRequest request)
    {
        var paymentReference = request.PaymentReference ?? order.PaymentReference ?? order.Id.ToString("N");
        return $"ORDER_CANCELLED:{order.TenantId}:{order.Id}:{request.Outcome}:{paymentReference}";
    }

    private static string BuildPaymentFailedIdempotencyKey(Order order, HandlePaymentResultRequest request)
    {
        var paymentReference = request.PaymentReference ?? order.PaymentReference ?? order.Id.ToString("N");
        return $"GATEWAY_FAILED:{order.TenantId}:{order.Id}:{paymentReference}";
    }

    private static string? BuildCompatibilityPaymentStatusDescription(HandlePaymentResultRequest request)
    {
        return request.Source switch
        {
            PaymentLifecycleSource.PaymentRetry => "Payment reopened for a new provider attempt",
            PaymentLifecycleSource.PaymentReviewApproval => $"Payment review approved by {request.Actor ?? "system"}",
            PaymentLifecycleSource.PaymentReviewRejection => request.Reason ?? "Payment review rejected",
            PaymentLifecycleSource.OfflineCollection => "Offline payment collected on delivery",
            PaymentLifecycleSource.RefundAuthority => request.Reason ?? "Payment refunded",
            PaymentLifecycleSource.SettlementAuthority => request.Reason ?? "Payment settlement accepted",
            _ when request.Outcome == PaymentLifecycleOutcome.Cancelled => request.Reason ?? "Payment cancelled",
            _ when request.Outcome == PaymentLifecycleOutcome.Expired => request.Reason ?? "Payment expired",
            _ when request.Outcome != PaymentLifecycleOutcome.Succeeded => request.Reason ?? "Payment failed",
            _ => null
        };
    }

    private static string BuildCompatibilityOrderStatusReason(HandlePaymentResultRequest request)
    {
        return request.Source switch
        {
            PaymentLifecycleSource.OfflineCollection => "Offline payment collected on delivery",
            PaymentLifecycleSource.PaymentReviewApproval => $"Payment confirmed by review approval ({request.Actor ?? "system"})",
            PaymentLifecycleSource.PaymentReviewRejection => request.Reason ?? "Reviewed payment rejected",
            _ when request.Outcome == PaymentLifecycleOutcome.Cancelled => request.Reason ?? "Payment cancelled",
            _ when request.Outcome == PaymentLifecycleOutcome.Expired => request.Reason ?? "Payment expired",
            _ when request.Outcome != PaymentLifecycleOutcome.Succeeded => request.Reason ?? "Payment failed",
            _ => $"Payment confirmed (Source: {request.Source})"
        };
    }

    private static void TransitionCheckoutState(Order order, CheckoutState targetState)
    {
        if (order.CheckoutState == targetState)
        {
            return;
        }

        if (order.CheckoutState == CheckoutState.PendingPaymentIntent &&
            targetState == CheckoutState.Paid)
        {
            order.TransitionCheckoutState(CheckoutState.PaymentPending);
        }

        order.TransitionCheckoutState(targetState);
    }

    private static void UpdatePaymentIntentAuthorized(
        PaymentIntent? paymentIntent,
        HandlePaymentResultRequest request,
        DateTime now)
    {
        if (paymentIntent == null)
        {
            return;
        }

        paymentIntent.Status = PaymentIntentStatus.Authorized;
        paymentIntent.AuthorizedAt = now;
        paymentIntent.LastWebhookEventId = request.WebhookEventId ?? paymentIntent.LastWebhookEventId;
        paymentIntent.WebhookEventCount++;
    }

    private static void UpdatePaymentIntentSucceeded(
        PaymentIntent? paymentIntent,
        HandlePaymentResultRequest request,
        DateTime now)
    {
        if (paymentIntent == null || request.Source == PaymentLifecycleSource.OfflineCollection)
        {
            return;
        }

        paymentIntent.Status = PaymentIntentStatus.Paid;
        paymentIntent.ProviderPaymentId = request.ProviderPaymentId ?? paymentIntent.ProviderPaymentId;
        paymentIntent.CapturedAt = now;
        paymentIntent.LastWebhookEventId = request.WebhookEventId ?? paymentIntent.LastWebhookEventId;
        paymentIntent.WebhookEventCount++;
        paymentIntent.InventoryCommitted = true;

        if (request.Source == PaymentLifecycleSource.PaymentReviewApproval)
        {
            paymentIntent.VerifiedByAdmin = request.Actor;
            paymentIntent.VerifiedAtUtc = now;
            paymentIntent.ProofImageExpiresAtUtc = request.ReviewProofRetentionExpiresAtUtc;
            paymentIntent.ProofImageDeleted = false;
        }
    }

    private static void UpdatePaymentIntentFailed(
        PaymentIntent? paymentIntent,
        HandlePaymentResultRequest request,
        DateTime now)
    {
        if (paymentIntent == null)
        {
            return;
        }

        paymentIntent.Status = request.Outcome switch
        {
            PaymentLifecycleOutcome.Cancelled => PaymentIntentStatus.Cancelled,
            PaymentLifecycleOutcome.Rejected => PaymentIntentStatus.Rejected,
            PaymentLifecycleOutcome.Expired => PaymentIntentStatus.Expired,
            _ => PaymentIntentStatus.Failed
        };

        paymentIntent.ProviderPaymentId = request.ProviderPaymentId ?? paymentIntent.ProviderPaymentId;
        paymentIntent.FailedAt = now;
        paymentIntent.FailureReason = request.Reason ?? paymentIntent.FailureReason ?? "Payment failed";
        paymentIntent.FailureCode = request.FailureCode ?? paymentIntent.FailureCode;
        paymentIntent.LastWebhookEventId = request.WebhookEventId ?? paymentIntent.LastWebhookEventId;
        paymentIntent.WebhookEventCount++;
        paymentIntent.InventoryCommitted = false;

        if (request.Outcome == PaymentLifecycleOutcome.Rejected)
        {
            paymentIntent.VerifiedByAdmin = request.Actor;
            paymentIntent.VerifiedAtUtc = now;
            paymentIntent.RejectionReason = request.Reason;
            paymentIntent.ProofImageExpiresAtUtc = request.ReviewProofRetentionExpiresAtUtc;
            paymentIntent.ProofImageDeleted = false;
        }

        if (request.Outcome == PaymentLifecycleOutcome.Cancelled &&
            IsReviewablePaymentIntent(paymentIntent) &&
            !string.IsNullOrWhiteSpace(paymentIntent.ProofImageUrl))
        {
            paymentIntent.ProofImageExpiresAtUtc = request.ReviewProofRetentionExpiresAtUtc;
        }
    }

    private static bool IsValidManualWalletProofRetention(DateTime createdAtUtc, DateTime? expiresAtUtc)
    {
        return expiresAtUtc.HasValue &&
               expiresAtUtc.Value != default &&
               (createdAtUtc == default || expiresAtUtc.Value > createdAtUtc);
    }

    private static PaymentTransitionResult? ValidateManualWalletProofRetentionAuthority(
        PaymentIntent? paymentIntent,
        HandlePaymentResultRequest request)
    {
        if (paymentIntent is null ||
            !IsReviewablePaymentIntent(paymentIntent) ||
            string.IsNullOrWhiteSpace(paymentIntent.ProofImageUrl))
        {
            return null;
        }

        return IsValidManualWalletProofRetention(
            paymentIntent.CreatedAt,
            request.ReviewProofRetentionExpiresAtUtc)
            ? null
            : PaymentTransitionResult.Failed(
                "Manual Wallet proof retention authority is invalid.",
                "MANUAL_WALLET_RETENTION_INVALID");
    }

    private static void AppendAdminNote(
        Order order,
        string? reason,
        string? actor,
        DateTime now,
        PaymentLifecycleOutcome outcome)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        var action = outcome switch
        {
            PaymentLifecycleOutcome.Rejected => "Payment rejected",
            PaymentLifecycleOutcome.Cancelled => "Payment cancelled",
            PaymentLifecycleOutcome.Expired => "Payment expired",
            PaymentLifecycleOutcome.AmountMismatch => "Payment amount mismatch",
            _ => "Payment failed"
        };
        var actorSegment = string.IsNullOrWhiteSpace(actor)
            ? string.Empty
            : $" by {actor}";
        var note = $"[{now:yyyy-MM-dd HH:mm}] {action}{actorSegment}: {reason}";

        order.AdminNotes = string.IsNullOrWhiteSpace(order.AdminNotes)
            ? note
            : $"{order.AdminNotes}\n{note}";
    }

    private static bool IsCodOrder(Order order)
    {
        return order.PaymentMethodCode.Equals("COD", StringComparison.OrdinalIgnoreCase) ||
               order.PaymentMethodCode.Contains("CASH_ON_DELIVERY", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWalletOrder(Order order)
    {
        return order.PaymentMethodCode.Equals("MANUALWALLET", StringComparison.OrdinalIgnoreCase) ||
               order.PaymentMethodCode.Equals("WALLET", StringComparison.OrdinalIgnoreCase) ||
               order.PaymentMethodCode.Contains("VODAFONE", StringComparison.OrdinalIgnoreCase) ||
               order.PaymentMethodCode.Contains("INSTAPAY", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetStatusMessage(
        Order order,
        PaymentIntent? latestPaymentIntent,
        bool isCod,
        bool isWallet,
        bool isOnline)
    {
        if (isWallet && latestPaymentIntent?.Status == PaymentIntentStatus.AwaitingVerification)
        {
            return "Awaiting admin verification";
        }

        return order.PaymentStatus switch
        {
            PaymentStatus.Pending when isCod => "Awaiting delivery",
            PaymentStatus.Pending when isWallet => "Awaiting wallet payment",
            PaymentStatus.Pending when isOnline => "Awaiting payment",
            PaymentStatus.Authorized => "Payment authorized - awaiting capture",
            PaymentStatus.AwaitingVerification => "Awaiting admin verification",
            PaymentStatus.Collected => "Payment collected - awaiting settlement",
            PaymentStatus.Settled => "Payment settled",
            PaymentStatus.Refunded => "Fully refunded",
            PaymentStatus.PartiallyRefunded => "Partially refunded",
            PaymentStatus.Expired => "Payment expired - manual review required if provider money arrived",
            PaymentStatus.AmountMismatch => "Payment amount mismatch - manual review required",
            PaymentStatus.Failed => "Payment failed",
            PaymentStatus.Rejected => "Payment rejected",
            _ => order.PaymentStatus.ToString()
        };
    }

    /// <summary>
    /// Executes a payment lifecycle transaction with monitoring and telemetry.
    /// Tracks transaction duration, deadlocks, and retries for operational visibility.
    /// </summary>
    private async Task<T> ExecuteMonitoredTransactionAsync<T>(
        string operationName,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct)
    {
        var stopwatch = _enableTransactionTelemetry ? Stopwatch.StartNew() : null;
        
        try
        {
            _logger.LogDebug(
                "[Payment Lifecycle] Starting transaction | Operation: {Operation} | IsolationLevel: {IsolationLevel}",
                operationName,
                _transactionIsolationLevel);

            var result = await operation(ct);

            if (_enableTransactionTelemetry && stopwatch != null)
            {
                stopwatch.Stop();
                _reliabilityTelemetry?.RecordPaymentTransactionDuration(
                    operationName,
                    "success",
                    stopwatch.Elapsed.TotalSeconds);
            }

            _logger.LogDebug(
                "[Payment Lifecycle] Transaction completed | Operation: {Operation} | Duration: {DurationMs}ms",
                operationName,
                stopwatch?.ElapsedMilliseconds ?? 0);

            return result;
        }
        catch (Exception ex)
        {
            if (_enableTransactionTelemetry && stopwatch != null)
            {
                stopwatch.Stop();
                _reliabilityTelemetry?.RecordPaymentTransactionDuration(
                    operationName,
                    "failure",
                    stopwatch.Elapsed.TotalSeconds);
            }

            // Detect deadlock scenarios
            if (ex.Message.Contains("deadlock", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("was deadlocked", StringComparison.OrdinalIgnoreCase))
            {
                _reliabilityTelemetry?.RecordPaymentTransactionDeadlock(
                    operationName,
                    _transactionIsolationLevel.ToString());

                _logger.LogWarning(ex,
                    "[Payment Lifecycle] Transaction deadlock detected | Operation: {Operation} | IsolationLevel: {IsolationLevel}",
                    operationName,
                    _transactionIsolationLevel);
            }
            else
            {
                _logger.LogError(ex,
                    "[Payment Lifecycle] Transaction failed | Operation: {Operation} | Duration: {DurationMs}ms",
                    operationName,
                    stopwatch?.ElapsedMilliseconds ?? 0);
            }

            throw;
        }
    }

    private sealed class NullServiceScopeFactory : IServiceScopeFactory
    {
        public static readonly NullServiceScopeFactory Instance = new();

        private NullServiceScopeFactory()
        {
        }

        public IServiceScope CreateScope() => throw new InvalidOperationException(
            "Affiliate reconciliation scope factory is not available in this payment lifecycle context.");
    }

    private sealed record ReservationPaymentValidationResult(
        bool IsValid,
        string FailureCode,
        string Reason)
    {
        public static ReservationPaymentValidationResult Valid() =>
            new(true, string.Empty, string.Empty);

        public static ReservationPaymentValidationResult Invalid(string failureCode, string reason) =>
            new(false, failureCode, reason);
    }

    private sealed record PaymentLifecycleAggregate(
        Order? Order,
        PaymentIntent? PaymentIntent);

    private sealed record GatewaySettlementResolution(
        List<Order> Orders,
        IReadOnlyDictionary<Guid, Guid?> PaymentIntentIdsByOrder,
        int? RequestedOrderCount,
        PaymentTransitionResult? Failure)
    {
        public static GatewaySettlementResolution Failed(PaymentTransitionResult failure) =>
            new(new List<Order>(), new Dictionary<Guid, Guid?>(), null, failure);
    }
}
