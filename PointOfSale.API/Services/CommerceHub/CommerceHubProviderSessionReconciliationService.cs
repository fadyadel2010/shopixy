using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.Payments.Paymob;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICommerceHubProviderSessionReconciliationService
{
    Task<PaymentProviderSessionReconciliationResponse> ResolveNotCreatedAsync(
        string providerCode,
        PaymentProviderSessionReconciliationRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);
}

public sealed class CommerceHubProviderSessionReconciliationException : Exception
{
    public CommerceHubProviderSessionReconciliationException(int statusCode, string code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string Code { get; }
}

public sealed class CommerceHubProviderSessionReconciliationService
    : ICommerceHubProviderSessionReconciliationService
{
    public const string DashboardNoOrderEvidenceCode = "PAYMOB_DASHBOARD_NO_ORDER";
    private const string ProviderSessionOutcomeUnknownPrefix = "PROVIDER_SESSION_OUTCOME_UNKNOWN:";
    private const string ProviderSessionReinitFencePrefix = "PROVIDER_SESSION_REINIT_FENCE:";

    private readonly ApplicationDbContext _dbContext;
    private readonly IDistributedLockService _lockService;
    private readonly IPaymobSensitiveArtifactProtector _sensitiveArtifactProtector;
    private readonly ILogger<CommerceHubProviderSessionReconciliationService> _logger;

    public CommerceHubProviderSessionReconciliationService(
        ApplicationDbContext dbContext,
        IDistributedLockService lockService,
        IPaymobSensitiveArtifactProtector sensitiveArtifactProtector,
        ILogger<CommerceHubProviderSessionReconciliationService> logger)
    {
        _dbContext = dbContext;
        _lockService = lockService;
        _sensitiveArtifactProtector = sensitiveArtifactProtector;
        _logger = logger;
    }

    public async Task<PaymentProviderSessionReconciliationResponse> ResolveNotCreatedAsync(
        string providerCode,
        PaymentProviderSessionReconciliationRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        using var paymentBoundary = CommerceHubBoundaryMutationScope.BeginPayment();

        var normalizedProvider = providerCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!string.Equals(normalizedProvider, CommerceHubPaymentMethods.Paymob, StringComparison.Ordinal))
        {
            throw Error(StatusCodes.Status400BadRequest, "PROVIDER_RECONCILIATION_UNSUPPORTED", "Only Paymob provider-session reconciliation is supported.");
        }

        if (request.PaymentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.ProviderReference) ||
            !request.AdministratorConfirmation ||
            !string.Equals(request.EvidenceCode?.Trim(), DashboardNoOrderEvidenceCode, StringComparison.Ordinal))
        {
            throw Error(StatusCodes.Status400BadRequest, "PROVIDER_RECONCILIATION_CONFIRMATION_REQUIRED", "Payment id, exact provider reference, dashboard evidence, and administrator confirmation are required.");
        }

        var evidenceNote = request.EvidenceNote?.Trim() ?? string.Empty;
        if (evidenceNote.Length < 10 || evidenceNote.Length > 500)
        {
            throw Error(StatusCodes.Status400BadRequest, "PROVIDER_RECONCILIATION_EVIDENCE_INVALID", "Evidence note must be between 10 and 500 characters.");
        }

        var paymentAuthority = await _dbContext.CommerceHubPayments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(payment => payment.Id == request.PaymentId && !payment.IsDeleted)
            .Select(payment => new { payment.OrderId, payment.Provider })
            .SingleOrDefaultAsync(cancellationToken);
        if (paymentAuthority == null)
        {
            throw Error(StatusCodes.Status404NotFound, "PAYMENT_NOT_FOUND", "CommerceHub payment was not found.");
        }

        if (paymentAuthority.Provider != PaymentProvider.Paymob)
        {
            throw Error(StatusCodes.Status409Conflict, "PAYMENT_PROVIDER_MISMATCH", "The payment does not belong to Paymob.");
        }

        var lockKey = $"lock:payment:initiate:{paymentAuthority.OrderId:N}:paymob";
        var lockToken = Guid.NewGuid().ToString("N");
        var lockResult = await _lockService.TryAcquireLockAsync(lockKey, lockToken, TimeSpan.FromMinutes(2));
        if (lockResult.IsInfrastructureFailure)
        {
            throw Error(StatusCodes.Status503ServiceUnavailable, "LOCK_INFRASTRUCTURE_FAILURE", "Provider-session reconciliation is unavailable because lock infrastructure could not be reached.");
        }

        if (!lockResult.IsAcquired)
        {
            throw Error(StatusCodes.Status409Conflict, "PAYMENT_IN_PROGRESS", "The payment is currently being processed. Try reconciliation again after it finishes.");
        }

        try
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var payment = await _dbContext.CommerceHubPayments
                .IgnoreQueryFilters()
                .Include(item => item.Attempts)
                .Include(item => item.Logs)
                .SingleOrDefaultAsync(item => item.Id == request.PaymentId && !item.IsDeleted, cancellationToken);
            if (payment == null)
            {
                throw Error(StatusCodes.Status404NotFound, "PAYMENT_NOT_FOUND", "CommerceHub payment was not found.");
            }

            var order = await _dbContext.CommerceHubMarketplaceOrders
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(item => item.Id == payment.OrderId && !item.IsDeleted, cancellationToken);
            if (order == null)
            {
                throw Error(StatusCodes.Status409Conflict, "PAYMENT_ORDER_NOT_FOUND", "The payment's marketplace order was not found.");
            }

            ValidateResolutionAuthority(payment, order, request.ProviderReference.Trim());

            var hasUnresolvedProviderEvidence = await _dbContext.CommerceHubUnresolvedPaymentWebhooks
                .IgnoreQueryFilters()
                .AnyAsync(item =>
                    !item.IsDeleted &&
                    (item.LocalPaymentId == payment.Id || item.OrderId == payment.OrderId),
                    cancellationToken);
            if (hasUnresolvedProviderEvidence)
            {
                throw Error(StatusCodes.Status409Conflict, "PROVIDER_EVIDENCE_REQUIRES_RECONCILIATION", "Unresolved provider evidence exists for this payment or order; it cannot be resolved as not created.");
            }

            var hasPaidPayment = await _dbContext.CommerceHubPayments
                .IgnoreQueryFilters()
                .AnyAsync(item =>
                    item.OrderId == payment.OrderId &&
                    !item.IsDeleted &&
                    item.Status == CommerceHubPaymentRecordStatus.Paid,
                    cancellationToken);
            if (hasPaidPayment)
            {
                throw Error(StatusCodes.Status409Conflict, "ORDER_ALREADY_PAID", "The marketplace order already has a paid payment.");
            }

            var resolvedAtUtc = DateTime.UtcNow;
            var previousStatus = payment.Status.ToString();
            var resolutionReason = "PROVIDER_SESSION_RECONCILED_NOT_CREATED: Super Admin confirmed that the Paymob Test-mode dashboard contained no matching order or transaction.";
            payment.Status = CommerceHubPaymentRecordStatus.Failed;
            payment.FailureReason = resolutionReason;
            payment.PaidAtUtc = null;
            payment.IsProcessed = false;
            payment.ProcessedAtUtc = null;

            var latestAttempt = payment.Attempts.OrderByDescending(item => item.AttemptNumber).First();
            latestAttempt.Status = CommerceHubPaymentRecordStatus.Failed;
            latestAttempt.FailureReason = resolutionReason;
            latestAttempt.CompletedAtUtc ??= resolvedAtUtc;

            var reconciliationEvidence = JsonSerializer.Serialize(new
            {
                paymentId = payment.Id,
                orderId = payment.OrderId,
                provider = normalizedProvider,
                providerReference = payment.ProviderReference,
                evidenceCode = DashboardNoOrderEvidenceCode,
                evidenceNote,
                correlationId,
                resolvedAtUtc,
                providerPayloadPersisted = false
            });

            _dbContext.CommerceHubPaymentLogs.Add(new CommerceHubPaymentLog
            {
                Id = Guid.NewGuid(),
                PaymentId = payment.Id,
                EventType = "initiate.paymob.outcome_reconciled_not_created",
                ProviderEventId = null,
                RawResponse = _sensitiveArtifactProtector.ProtectWebhookPayload(reconciliationEvidence)
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogWarning(
                "CommerceHub provider-session outcome resolved as not created by Super Admin evidence | PaymentId: {PaymentId} | OrderId: {OrderId} | Provider: {Provider} | ProviderReference: {ProviderReference} | CorrelationId: {CorrelationId}",
                payment.Id,
                payment.OrderId,
                normalizedProvider,
                payment.ProviderReference,
                correlationId);

            return new PaymentProviderSessionReconciliationResponse
            {
                ProviderCode = normalizedProvider,
                PaymentId = payment.Id,
                OrderId = payment.OrderId,
                ProviderReference = payment.ProviderReference,
                PreviousStatus = previousStatus,
                Status = payment.Status.ToString(),
                ResolvedAtUtc = resolvedAtUtc,
                RetryAllowed = true,
                Message = "Provider-session outcome was reconciled as not created. The customer may retry through the CommerceHub frontend."
            };
        }
        finally
        {
            await _lockService.ReleaseLockAsync(lockKey, lockToken);
        }
    }

    private static void ValidateResolutionAuthority(
        CommerceHubPayment payment,
        CommerceHubMarketplaceOrder order,
        string suppliedProviderReference)
    {
        if (payment.Provider != PaymentProvider.Paymob ||
            payment.Status != CommerceHubPaymentRecordStatus.Pending ||
            string.IsNullOrWhiteSpace(payment.FailureReason) ||
            !payment.FailureReason.StartsWith(ProviderSessionOutcomeUnknownPrefix, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(payment.ProviderIntentId) ||
            !payment.ProviderIntentId.StartsWith(ProviderSessionReinitFencePrefix, StringComparison.Ordinal) ||
            !string.IsNullOrWhiteSpace(payment.ProviderTransactionId) ||
            !string.IsNullOrWhiteSpace(payment.CheckoutUrl) ||
            !string.IsNullOrWhiteSpace(payment.PaymentToken) ||
            payment.Logs.Any(log => !string.IsNullOrWhiteSpace(log.ProviderEventId)) ||
            payment.Attempts.Count == 0)
        {
            throw Error(StatusCodes.Status409Conflict, "PROVIDER_SESSION_NOT_ELIGIBLE", "This payment does not have an evidence-free, outcome-unknown Paymob session that can be resolved as not created.");
        }

        if (!string.Equals(payment.ProviderReference, suppliedProviderReference, StringComparison.Ordinal))
        {
            throw Error(StatusCodes.Status409Conflict, "PROVIDER_REFERENCE_MISMATCH", "The supplied provider reference does not exactly match the payment authority record.");
        }

        if (order.Status is not (OrderStatus.Pending or OrderStatus.OnHold) ||
            order.PaymentStatus != PaymentStatus.Pending)
        {
            throw Error(StatusCodes.Status409Conflict, "PAYMENT_ORDER_STATE_INVALID", "The marketplace order is no longer pending payment.");
        }
    }

    private static CommerceHubProviderSessionReconciliationException Error(int statusCode, string code, string message) =>
        new(statusCode, code, message);
}
