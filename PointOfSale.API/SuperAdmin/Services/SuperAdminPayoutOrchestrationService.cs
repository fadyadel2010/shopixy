using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Instrumentation;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Financial;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.SuperAdmin.Services;

public interface ISuperAdminPayoutOrchestrationService
{
    Task<CommerceHubPayoutDto> ExecuteAsync(
        Guid vendorId,
        SuperAdminExecuteVendorPayoutRequestDto request,
        Guid? actorId,
        string? actorEmail,
        CancellationToken cancellationToken = default);
}

public sealed class SuperAdminPayoutOrchestrationService : ISuperAdminPayoutOrchestrationService
{
    private const string CanonicalExecutionBoundary = "CommerceHubPayoutService";

    private readonly ApplicationDbContext _dbContext;
    private readonly ICommerceHubPayoutService _canonicalPayoutService;
    private readonly SuperAdminPayoutOrchestrationTelemetry _telemetry;
    private readonly ILogger<SuperAdminPayoutOrchestrationService> _logger;

    public SuperAdminPayoutOrchestrationService(
        ApplicationDbContext dbContext,
        ICommerceHubPayoutService canonicalPayoutService,
        SuperAdminPayoutOrchestrationTelemetry telemetry,
        ILogger<SuperAdminPayoutOrchestrationService> logger)
    {
        _dbContext = dbContext;
        _canonicalPayoutService = canonicalPayoutService;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task<CommerceHubPayoutDto> ExecuteAsync(
        Guid vendorId,
        SuperAdminExecuteVendorPayoutRequestDto request,
        Guid? actorId,
        string? actorEmail,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _telemetry.RecordExecutionStarted();
        _logger.LogInformation(
            "Super Admin payout orchestration started for vendor {VendorId}. Event={EventName}; Boundary={Boundary}; CanonicalService={CanonicalService}",
            vendorId,
            SuperAdminPayoutOrchestrationTelemetry.PayoutExecutionStarted,
            CanonicalExecutionBoundary,
            _canonicalPayoutService.GetType().Name);

        try
        {
            var amount = NormalizeAmount(request.Amount);
            var idempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey);
            var processedBy = NormalizeProcessedBy(actorEmail, NormalizeActorId(actorId));
            var transferReference = NormalizeOptional(request.TransferReference, 200);
            var externalReferenceId = NormalizeOptional(request.ExternalReferenceId, 200);
            var bankTransactionId = NormalizeOptional(request.BankTransactionId, 200);
            var payoutMethod = await LoadPayoutMethodAsync(
                vendorId,
                request,
                cancellationToken);
            var earningIds = await SelectCanonicalPayoutEarningsAsync(vendorId, amount, cancellationToken);
            var correlationId = $"SuperAdminPayout:{vendorId:N}:{idempotencyKey}";

            var created = await _canonicalPayoutService.CreatePayoutAsync(
                vendorId,
                new CommerceHubCreatePayoutRequestDto
                {
                    EarningIds = earningIds,
                    Method = payoutMethod.MethodType,
                    IdempotencyKey = idempotencyKey,
                    Reference = transferReference
                },
                correlationId,
                cancellationToken);

            EnsurePayoutAmountMatchesRequest(created, amount);

            var processingRequest = new CommerceHubUpdatePayoutRequestDto
            {
                Reference = transferReference,
                ExternalReferenceId = externalReferenceId,
                BankTransactionId = bankTransactionId,
                ExternalTransferIdempotencyKey = created.ExternalTransferIdempotencyKey
            };
            var processing = await _canonicalPayoutService.MarkProcessingAsync(
                vendorId,
                created.PayoutId,
                processingRequest,
                correlationId,
                cancellationToken,
                processedBy);

            EnsureCommittedExternalHandoff(processing);

            var payout = await _canonicalPayoutService.MarkCompletedAsync(
                vendorId,
                processing.PayoutId,
                new CommerceHubUpdatePayoutRequestDto
                {
                    Reference = transferReference,
                    ExternalReferenceId = externalReferenceId,
                    BankTransactionId = bankTransactionId,
                    ExternalTransferIdempotencyKey = processing.ExternalTransferIdempotencyKey,
                    ExternalTransferHandoffToken = processing.ExternalTransferHandoffToken,
                    ExternalTransferConfirmedExecuted = true
                },
                correlationId,
                cancellationToken,
                processedBy);

            _telemetry.RecordExecutionSucceeded(payout.Status);
            _logger.LogInformation(
                "Super Admin payout orchestration succeeded for vendor {VendorId} and payout {PayoutId}. Event={EventName}; Status={Status}",
                vendorId,
                payout.PayoutId,
                SuperAdminPayoutOrchestrationTelemetry.PayoutExecutionSucceeded,
                payout.Status);

            return payout;
        }
        catch (CommerceHubVendorFinancialControlException exception)
        {
            RecordBusinessFailure(vendorId, exception.Code, exception);
            throw;
        }
        catch (CommerceHubCheckoutException exception)
        {
            RecordBusinessFailure(vendorId, exception.Code, exception);
            throw;
        }
        catch (OperationCanceledException)
        {
            _telemetry.RecordExecutionFailed("OPERATION_CANCELLED");
            throw;
        }
        catch (Exception exception)
        {
            _telemetry.RecordExecutionFailed("UNEXPECTED_INFRASTRUCTURE_FAILURE");
            _logger.LogError(
                exception,
                "Super Admin payout orchestration failed unexpectedly for vendor {VendorId}. Event={EventName}",
                vendorId,
                SuperAdminPayoutOrchestrationTelemetry.PayoutExecutionFailed);

            throw new SuperAdminPayoutOrchestrationException(
                "Unexpected infrastructure failure during Super Admin payout orchestration.",
                exception);
        }
    }

    private void RecordBusinessFailure(Guid vendorId, string reasonCode, Exception exception)
    {
        _telemetry.RecordExecutionFailed(reasonCode);
        _logger.LogWarning(
            exception,
            "Super Admin payout orchestration failed for vendor {VendorId}. Event={EventName}; ReasonCode={ReasonCode}",
            vendorId,
            SuperAdminPayoutOrchestrationTelemetry.PayoutExecutionFailed,
            reasonCode);
    }

    private async Task<PayoutMethodSelection> LoadPayoutMethodAsync(
        Guid vendorId,
        SuperAdminExecuteVendorPayoutRequestDto request,
        CancellationToken cancellationToken)
    {
        var payoutMethod = await _dbContext.CommerceHubVendorPayoutMethods
            .AsNoTracking()
            .Where(method =>
                method.Id == request.PayoutMethodId &&
                method.VendorId == vendorId &&
                !method.IsDeleted)
            .Select(method => new
            {
                method.Id,
                method.MethodType
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (payoutMethod != null)
        {
            return new PayoutMethodSelection(payoutMethod.Id, payoutMethod.MethodType);
        }

        throw new CommerceHubVendorFinancialControlException(
            "PAYOUT_METHOD_NOT_FOUND",
            "Payout method was not found for this vendor.",
            StatusCodes.Status404NotFound);
    }

    private async Task<List<Guid>> SelectCanonicalPayoutEarningsAsync(
        Guid vendorId,
        decimal requestedAmount,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var platformCurrency = FinancialCurrencyGuard.PlatformBaseCurrencyCode;
        var negativeAdjustmentTotal = await _dbContext.CommerceHubVendorEarnings
            .AsNoTracking()
            .Where(earning =>
                earning.TenantId == vendorId &&
                earning.Status == CommerceHubVendorEarningStatus.Available &&
                earning.Amount < 0m &&
                !earning.IsDeleted &&
                !earning.PayoutId.HasValue &&
                (!earning.AvailableAtUtc.HasValue || earning.AvailableAtUtc <= nowUtc) &&
                earning.SubOrder.Currency == platformCurrency)
            .SumAsync(earning => (decimal?)earning.Amount, cancellationToken) ?? 0m;
        var positiveTarget = RoundCurrency(requestedAmount - negativeAdjustmentTotal);
        var candidates = await _dbContext.CommerceHubVendorEarnings
            .AsNoTracking()
            .Where(earning =>
                earning.TenantId == vendorId &&
                earning.Status == CommerceHubVendorEarningStatus.Available &&
                earning.Amount > 0m &&
                !earning.IsDeleted &&
                (!earning.AvailableAtUtc.HasValue || earning.AvailableAtUtc <= nowUtc) &&
                earning.SubOrder.Currency == platformCurrency)
            .OrderBy(earning => earning.AvailableAtUtc ?? earning.CreatedAt)
            .ThenBy(earning => earning.CreatedAt)
            .ThenBy(earning => earning.Id)
            .Select(earning => new
            {
                earning.Id,
                AvailableAmount = earning.Amount - earning.PaidOutAmount - earning.PayoutReservedAmount
            })
            .ToListAsync(cancellationToken);

        var selected = new List<Guid>();
        var selectedTotal = 0m;
        foreach (var candidate in candidates)
        {
            var availableAmount = RoundCurrency(candidate.AvailableAmount);
            if (availableAmount <= 0m || selectedTotal + availableAmount > positiveTarget)
            {
                continue;
            }

            selected.Add(candidate.Id);
            selectedTotal = RoundCurrency(selectedTotal + availableAmount);
            if (selectedTotal == positiveTarget)
            {
                return selected;
            }
        }

        throw new CommerceHubVendorFinancialControlException(
            "PAYOUT_EARNING_ALLOCATION_CONFLICT",
            "The payout amount could not be mapped to canonical eligible earning reservations. Refresh payout readiness and retry.",
            StatusCodes.Status409Conflict);
    }

    private static void EnsureCommittedExternalHandoff(CommerceHubPayoutDto payout)
    {
        if (!string.IsNullOrWhiteSpace(payout.ExternalTransferHandoffToken) &&
            payout.ExternalTransferHandoffCommittedAtUtc.HasValue)
        {
            return;
        }

        throw new CommerceHubCheckoutException(
            "PAYOUT_EXTERNAL_HANDOFF_NOT_COMMITTED",
            "Payout completion requires a committed canonical external handoff before executed transfer reconciliation.",
            StatusCodes.Status409Conflict);
    }

    private static void EnsurePayoutAmountMatchesRequest(CommerceHubPayoutDto payout, decimal requestedAmount)
    {
        if (payout.Amount == requestedAmount)
        {
            return;
        }

        throw new CommerceHubVendorFinancialControlException(
            "IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD",
            "This idempotency key was already used with a different payout payload.",
            StatusCodes.Status409Conflict);
    }

    private static Guid NormalizeActorId(Guid? actorId)
    {
        if (actorId.HasValue && actorId.Value != Guid.Empty)
        {
            return actorId.Value;
        }

        throw new CommerceHubVendorFinancialControlException(
            "SUPER_ADMIN_ACTOR_REQUIRED",
            "Authenticated Super Admin actor is required for payout execution.",
            StatusCodes.Status401Unauthorized);
    }

    private static decimal NormalizeAmount(decimal amount)
    {
        if (amount > 0m)
        {
            return RoundCurrency(amount);
        }

        throw new CommerceHubVendorFinancialControlException(
            "AMOUNT_INVALID",
            "Amount must be greater than zero.",
            StatusCodes.Status400BadRequest);
    }

    private static string NormalizeIdempotencyKey(string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new CommerceHubVendorFinancialControlException(
                "IDEMPOTENCY_KEY_REQUIRED",
                "IdempotencyKey is required.",
                StatusCodes.Status400BadRequest);
        }

        var normalized = idempotencyKey.Trim();
        if (normalized.Length <= 200)
        {
            return normalized;
        }

        throw new CommerceHubVendorFinancialControlException(
            "IDEMPOTENCY_KEY_INVALID",
            "IdempotencyKey must not exceed 200 characters.",
            StatusCodes.Status400BadRequest);
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        throw new CommerceHubVendorFinancialControlException(
            "TRANSFER_REFERENCE_INVALID",
            $"Transfer reference must not exceed {maxLength} characters.",
            StatusCodes.Status400BadRequest);
    }

    private static string NormalizeProcessedBy(string? actorEmail, Guid actorId) =>
        string.IsNullOrWhiteSpace(actorEmail) ? actorId.ToString("N") : actorEmail.Trim();

    private static decimal RoundCurrency(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private sealed record PayoutMethodSelection(Guid Id, CommerceHubPayoutMethod MethodType);
}

public sealed class SuperAdminPayoutOrchestrationException : Exception
{
    public SuperAdminPayoutOrchestrationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
