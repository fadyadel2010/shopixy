using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.Services.CommerceHub.FinancialLedger;
using PointOfSale.API.Services.Governance;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Financial;
using PointOfSale.Infrastructure.Data;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICommerceHubPayoutService
{
    Task<IReadOnlyList<CommerceHubPayoutDto>> GetPayoutsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<CommerceHubPayoutDto> CreatePayoutAsync(
        Guid tenantId,
        CommerceHubCreatePayoutRequestDto request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CommerceHubPayoutDto> MarkProcessingAsync(
        Guid tenantId,
        Guid payoutId,
        CommerceHubUpdatePayoutRequestDto request,
        string correlationId,
        CancellationToken cancellationToken = default,
        string? processedBy = null);

    Task<CommerceHubPayoutDto> MarkCompletedAsync(
        Guid tenantId,
        Guid payoutId,
        CommerceHubUpdatePayoutRequestDto request,
        string correlationId,
        CancellationToken cancellationToken = default,
        string? processedBy = null);

    Task<CommerceHubPayoutDto> MarkFailedAsync(
        Guid tenantId,
        Guid payoutId,
        CommerceHubUpdatePayoutRequestDto request,
        string correlationId,
        CancellationToken cancellationToken = default,
        string? processedBy = null);
}

public class CommerceHubPayoutService : ICommerceHubPayoutService
{
    // Unique constraint names for external payout evidence
    private const string PayoutCompletedExternalReferenceIdUnique = "UX_CommerceHubPayouts_Completed_ExternalReferenceId";
    private const string PayoutCompletedBankTransactionIdUnique = "UX_CommerceHubPayouts_Completed_BankTransactionId";
    private const string PayoutLiveExternalReferenceIdUnique = "UX_CommerceHubPayouts_Live_ExternalReferenceId";
    private const string PayoutLiveBankTransactionIdUnique = "UX_CommerceHubPayouts_Live_BankTransactionId";

    private readonly ApplicationDbContext _dbContext;
    private readonly ICommerceHubFinancialLedgerService _financialLedgerService;
    private readonly ICommerceHubFinancialSafetyGuardService _financialSafetyGuardService;
    private readonly ICommerceHubFinancialReadinessGate _financialReadinessGate;
    private readonly ICommerceHubFinancialAuditService _auditService;
    private readonly IUserGovernanceService _userGovernanceService;
    private readonly ILogger<CommerceHubPayoutService> _logger;

    public CommerceHubPayoutService(
        ApplicationDbContext dbContext,
        ICommerceHubFinancialLedgerService financialLedgerService,
        ICommerceHubFinancialSafetyGuardService financialSafetyGuardService,
        ICommerceHubFinancialReadinessGate financialReadinessGate,
        ICommerceHubFinancialAuditService auditService,
        IUserGovernanceService userGovernanceService,
        ILogger<CommerceHubPayoutService> logger)
    {
        _dbContext = dbContext;
        _financialLedgerService = financialLedgerService;
        _financialSafetyGuardService = financialSafetyGuardService;
        _financialReadinessGate = financialReadinessGate;
        _auditService = auditService;
        _userGovernanceService = userGovernanceService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CommerceHubPayoutDto>> GetPayoutsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var payouts = await _dbContext.CommerceHubPayouts
            .AsNoTracking()
            .Include(payout => payout.Items)
            .ThenInclude(item => item.Earning)
            .ThenInclude(earning => earning.SubOrder)
            .Where(payout => payout.TenantId == tenantId && !payout.IsDeleted)
            .OrderByDescending(payout => payout.CreatedAt)
            .ToListAsync(cancellationToken);

        return payouts.Select(MapPayout).ToList();
    }

    public async Task<CommerceHubPayoutDto> CreatePayoutAsync(
        Guid tenantId,
        CommerceHubCreatePayoutRequestDto request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        using var commerceHubBoundary = CommerceHubBoundaryMutationScope.BeginPayout();
        ArgumentNullException.ThrowIfNull(request);

        var idempotencyKey = NormalizeRequired(request.IdempotencyKey, "PAYOUT_IDEMPOTENCY_REQUIRED", "Payout idempotency key is required.");
        var earningIds = request.EarningIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (earningIds.Count == 0)
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_EARNINGS_REQUIRED",
                "At least one available earning is required to create a payout.",
                StatusCodes.Status400BadRequest);
        }

        var existing = await FindPayoutByIdempotencyAsync(tenantId, idempotencyKey, cancellationToken);
        if (existing != null)
        {
            return MapPayout(existing);
        }

        await _userGovernanceService.EnsureVendorPayoutAllowedAsync(tenantId, cancellationToken);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        // Re-check idempotency after acquiring transaction lock to prevent race conditions
        // where concurrent requests both pass the first check before either commits.
        var existingAfterLock = await FindPayoutByIdempotencyAsync(tenantId, idempotencyKey, cancellationToken);
        if (existingAfterLock != null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return MapPayout(existingAfterLock);
        }

        var payoutMethod = request.PayoutMethodId.HasValue
            ? await _dbContext.CommerceHubVendorPayoutMethods
                .AsNoTracking()
                .FirstOrDefaultAsync(method =>
                    method.Id == request.PayoutMethodId.Value &&
                    method.VendorId == tenantId &&
                    !method.IsDeleted,
                    cancellationToken)
            : null;

        if (request.PayoutMethodId.HasValue && payoutMethod == null)
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_METHOD_NOT_FOUND",
                "Payout method was not found for this vendor.",
                StatusCodes.Status404NotFound);
        }

        if (payoutMethod != null && payoutMethod.MethodType != request.Method)
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_METHOD_TYPE_MISMATCH",
                "Payout method type does not match the selected vendor payout method.",
                StatusCodes.Status409Conflict);
        }

        await _financialReadinessGate.EnsureOutwardMoneyMovementAllowedAsync(
            tenantId,
            "CommerceHubPayout.Create",
            cancellationToken);

        await LockVendorEarningsForPayoutCreationAsync(
            tenantId,
            earningIds,
            "selected",
            cancellationToken);

        var earnings = await _dbContext.CommerceHubVendorEarnings
            .Include(earning => earning.SubOrder)
            .Where(earning =>
                earning.TenantId == tenantId &&
                earningIds.Contains(earning.Id) &&
                !earning.IsDeleted)
            .ToListAsync(cancellationToken);

        if (earnings.Count != earningIds.Count)
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_EARNING_NOT_FOUND",
                "One or more earnings could not be found for this vendor.",
                StatusCodes.Status404NotFound);
        }

        var paymentIds = earnings
            .Where(earning => earning.PaymentId.HasValue)
            .Select(earning => earning.PaymentId!.Value)
            .Distinct()
            .ToList();
        var includesDevelopmentSimulation = paymentIds.Count > 0 &&
            await _dbContext.CommerceHubPayments
                .AsNoTracking()
                .AnyAsync(payment =>
                    paymentIds.Contains(payment.Id) &&
                    payment.ProviderTransactionId != null &&
                    payment.ProviderTransactionId.StartsWith("DEV-SIM:ONLINE:"),
                    cancellationToken);
        if (includesDevelopmentSimulation)
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_DEVELOPMENT_SIMULATION_NOT_TRANSFERABLE",
                "Development-simulated payment earnings cannot enter an external payout.",
                StatusCodes.Status409Conflict);
        }

        var hasExternallyLockedPayout = await _dbContext.CommerceHubPayouts
            .AnyAsync(
                payout => payout.TenantId == tenantId &&
                          (payout.Status == CommerceHubPayoutStatus.Processing ||
                           payout.Status == CommerceHubPayoutStatus.ReconciliationRequired) &&
                          !payout.IsDeleted,
                cancellationToken);
        if (hasExternallyLockedPayout)
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_SLOT_LOCKED",
                "Cannot create a new payout while another payout is still externally processing or awaiting reconciliation for this vendor.",
                StatusCodes.Status409Conflict);
        }

        var nowUtc = DateTime.UtcNow;
        if (earnings.Any(earning => earning.Status != CommerceHubVendorEarningStatus.Available))
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_EARNING_NOT_AVAILABLE",
                "All selected earnings must be in the Available state before payout creation.",
                StatusCodes.Status409Conflict);
        }

        if (earnings.Any(earning => earning.AvailableAtUtc.HasValue && earning.AvailableAtUtc.Value > nowUtc))
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_DELAY_NOT_ELAPSED",
                "One or more selected earnings are still within the payout settlement delay window.",
                StatusCodes.Status409Conflict);
        }

        var currencies = earnings
            .Select(earning => earning.SubOrder.Currency)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (currencies.Count != 1)
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_CURRENCY_MISMATCH",
                "A payout can only include earnings from a single currency.",
                StatusCodes.Status409Conflict);
        }

        var payoutCurrency = RequireEgpCurrency(
            currencies[0],
            "commercehub.payout.creation.currency",
            $"tenantId:{tenantId:D};idempotencyKey:{idempotencyKey}");

        var negativeAdjustmentIds = await _dbContext.CommerceHubVendorEarnings
            .Where(earning =>
                earning.TenantId == tenantId &&
                earning.Status == CommerceHubVendorEarningStatus.Available &&
                earning.Amount < 0 &&
                !earning.IsDeleted &&
                !earning.PayoutId.HasValue &&
                !earningIds.Contains(earning.Id) &&
                (!earning.AvailableAtUtc.HasValue || earning.AvailableAtUtc <= nowUtc) &&
                earning.SubOrder.Currency == payoutCurrency)
            .OrderBy(earning => earning.Id)
            .Select(earning => earning.Id)
            .ToListAsync(cancellationToken);

        await LockVendorEarningsForPayoutCreationAsync(
            tenantId,
            negativeAdjustmentIds,
            "negative-adjustment",
            cancellationToken);

        var negativeAdjustments = negativeAdjustmentIds.Count == 0
            ? []
            : await _dbContext.CommerceHubVendorEarnings
            .Include(earning => earning.SubOrder)
            .Where(earning =>
                earning.TenantId == tenantId &&
                negativeAdjustmentIds.Contains(earning.Id) &&
                earning.Status == CommerceHubVendorEarningStatus.Available &&
                earning.Amount < 0 &&
                !earning.IsDeleted &&
                !earning.PayoutId.HasValue &&
                (!earning.AvailableAtUtc.HasValue || earning.AvailableAtUtc <= nowUtc) &&
                earning.SubOrder.Currency == payoutCurrency)
            .ToListAsync(cancellationToken);

        var payoutEarnings = earnings
            .Concat(negativeAdjustments)
            .DistinctBy(earning => earning.Id)
            .ToList();

        var payoutAllocations = payoutEarnings
            .Select(earning => new
            {
                Earning = earning,
                Amount = ResolvePayoutAllocatableAmount(earning)
            })
            .Where(item => item.Amount != 0m)
            .ToList();

        var totalAmount = RoundCurrency(payoutAllocations.Sum(item => item.Amount));
        if (totalAmount <= 0)
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_AMOUNT_INVALID",
                "The selected earnings do not produce a positive payout amount after netting outstanding vendor adjustments.",
                StatusCodes.Status409Conflict);
        }

        await EnsureEarningsHaveLedgerConfirmationAsync(
            tenantId,
            payoutCurrency,
            payoutEarnings,
            cancellationToken);

        var payout = new CommerceHubPayout
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Amount = totalAmount,
            Currency = payoutCurrency,
            Status = CommerceHubPayoutStatus.Pending,
            PayoutMethodId = payoutMethod?.Id,
            Method = request.Method,
            DestinationValueSnapshot = payoutMethod?.DestinationValue,
            Reference = NormalizeOptional(request.Reference),
            IdempotencyKey = idempotencyKey
        };
        payout.ExternalTransferIdempotencyKey = BuildExternalTransferIdempotencyKey(payout);

        _dbContext.CommerceHubPayouts.Add(payout);

        foreach (var allocation in payoutAllocations)
        {
            var earning = allocation.Earning;
            if (earning.Amount > 0m)
            {
                earning.PayoutReservedAmount = RoundCurrency(earning.PayoutReservedAmount + allocation.Amount);
                earning.Status = CommerceHubVendorEarningStatus.InPayout;
                earning.PayoutId = payout.Id;
            }
            else
            {
                earning.Status = CommerceHubVendorEarningStatus.InPayout;
                earning.PayoutId = payout.Id;
            }

            _dbContext.CommerceHubPayoutItems.Add(new CommerceHubPayoutItem
            {
                Id = Guid.NewGuid(),
                PayoutId = payout.Id,
                EarningId = earning.Id,
                Amount = allocation.Amount
            });
        }

        try
        {
            await SavePayoutCreationWithConcurrencyConflictHandlingAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsIdempotencyKeyUniqueViolation(ex))
        {
            // Idempotency key conflict: another concurrent request won the race and created the payout.
            // Roll back our transaction and replay the winner's result.
            await transaction.RollbackAsync(cancellationToken);
            
            // Clear the ChangeTracker to detach the attempted payout, modified earnings, and payout items.
            // This prevents the abandoned mutations from being persisted by a later SaveChangesAsync.
            _dbContext.ChangeTracker.Clear();
            
            var existingPayout = await FindPayoutByIdempotencyAsync(tenantId, idempotencyKey, cancellationToken);
            if (existingPayout != null)
            {
                return MapPayout(existingPayout);
            }

            // If we can't find the existing payout, rethrow the original exception
            throw;
        }

        return await GetRequiredPayoutAsync(tenantId, payout.Id, cancellationToken);
    }

    public async Task<CommerceHubPayoutDto> MarkProcessingAsync(
        Guid tenantId,
        Guid payoutId,
        CommerceHubUpdatePayoutRequestDto request,
        string correlationId,
        CancellationToken cancellationToken = default,
        string? processedBy = null)
    {
        using var commerceHubBoundary = CommerceHubBoundaryMutationScope.BeginPayout();
        await _userGovernanceService.EnsureVendorPayoutAllowedAsync(tenantId, cancellationToken);
        EnsureProcessingRequestDoesNotClaimExecutedTransfer(request);
        _ = correlationId;
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var payout = await LoadTrackedPayoutAsync(tenantId, payoutId, cancellationToken);
        
        // Short-circuit for terminal states (idempotent retry handling)
        if (payout.Status == CommerceHubPayoutStatus.Completed || payout.Status == CommerceHubPayoutStatus.Failed)
        {
            return MapPayout(payout);
        }

        // Short-circuit if already Processing (idempotent retry handling)
        if (IsExternallyLockedPayoutStatus(payout.Status))
        {
            // PRODUCTION HARDENING: Backfill ExternalTransferIdempotencyKey if missing (for legacy payouts)
            if (string.IsNullOrWhiteSpace(payout.ExternalTransferIdempotencyKey))
            {
                payout.ExternalTransferIdempotencyKey = BuildExternalTransferIdempotencyKey(payout);
            }
            EnsureProcessingHandoffCommitted(payout);

            EnsureExternalTransferIntentMatchesPayout(payout, request);
            await EnsureExternalPayoutEvidenceIsUniqueAsync(payout, request, cancellationToken);
            
            // PRODUCTION HARDENING: Validate that incoming evidence matches stored values before applying ??= logic
            // This prevents silently ignoring conflicting evidence on idempotent retries
            EnsureRequestedPayoutEvidenceMatchesStored(payout, request);
            
            payout.Reference ??= NormalizeOptional(request.Reference);
            payout.ExternalReferenceId ??= NormalizeOptional(request.ExternalReferenceId);
            payout.BankTransactionId ??= NormalizeOptional(request.BankTransactionId);
            EnsureProcessingHandoffReference(payout, request);

            await SaveWithExternalEvidenceConflictHandlingAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return MapPayout(payout);
        }

        // Financial readiness gate: check AFTER terminal state check but BEFORE state mutation
        await _financialReadinessGate.EnsureOutwardMoneyMovementAllowedAsync(
            tenantId,
            "CommerceHubPayout.MarkProcessing",
            cancellationToken);

        if (await _financialSafetyGuardService.HasBlockingPayoutLockAsync(tenantId, cancellationToken))
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_BLOCKED_BY_REFUND_LOCK",
                "Payout processing is blocked while CommerceHub refund or return workflows are still active.",
                StatusCodes.Status409Conflict);
        }

        await CaptureAndValidatePayoutFinancialReadinessAsync(payout, cancellationToken);

        // PRODUCTION HARDENING: Enforce single-processing-payout invariant
        // Prevent flipping a pending payout to Processing when another Processing payout exists
        var hasConflictingExternallyLockedPayout = await _dbContext.CommerceHubPayouts
            .AnyAsync(
                payout => payout.TenantId == tenantId &&
                          payout.Id != payoutId &&
                          (payout.Status == CommerceHubPayoutStatus.Processing ||
                           payout.Status == CommerceHubPayoutStatus.ReconciliationRequired) &&
                          !payout.IsDeleted,
                cancellationToken);
        if (hasConflictingExternallyLockedPayout)
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_SLOT_LOCKED",
                "Cannot mark payout as Processing while another payout is still externally processing or awaiting reconciliation for this vendor.",
                StatusCodes.Status409Conflict);
        }

        payout.Status = CommerceHubPayoutStatus.Processing;
        payout.Reference ??= NormalizeOptional(request.Reference);
        payout.ExternalReferenceId ??= NormalizeOptional(request.ExternalReferenceId);
        payout.BankTransactionId ??= NormalizeOptional(request.BankTransactionId);
        if (string.IsNullOrWhiteSpace(payout.ExternalTransferIdempotencyKey))
        {
            payout.ExternalTransferIdempotencyKey = BuildExternalTransferIdempotencyKey(payout);
        }
        EnsureProcessingHandoffCommitted(payout);
        EnsureExternalTransferIntentMatchesPayout(payout, request);
        await EnsureExternalPayoutEvidenceIsUniqueAsync(payout, request, cancellationToken);
        EnsureProcessingHandoffReference(payout, request);
        payout.ProcessedBy ??= NormalizeOptional(processedBy);
        payout.ProcessedAtUtc ??= DateTime.UtcNow;

        await SaveWithExternalEvidenceConflictHandlingAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MapPayout(payout);
    }

    public async Task<CommerceHubPayoutDto> MarkCompletedAsync(
        Guid tenantId,
        Guid payoutId,
        CommerceHubUpdatePayoutRequestDto request,
        string correlationId,
        CancellationToken cancellationToken = default,
        string? processedBy = null)
    {
        using var commerceHubBoundary = CommerceHubBoundaryMutationScope.BeginPayout();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var payout = await LoadTrackedPayoutAsync(tenantId, payoutId, cancellationToken);

        // Short-circuit for terminal Completed state (idempotent retry handling)
        if (payout.Status == CommerceHubPayoutStatus.Completed)
        {
            EnsureRequestedPayoutEvidenceMatchesStored(payout, request);
            payout.Reference ??= NormalizeOptional(request.Reference);
            payout.ExternalReferenceId ??= NormalizeOptional(request.ExternalReferenceId);
            payout.BankTransactionId ??= NormalizeOptional(request.BankTransactionId);
            if (string.IsNullOrWhiteSpace(payout.ExternalTransferIdempotencyKey))
            {
                payout.ExternalTransferIdempotencyKey = BuildExternalTransferIdempotencyKey(payout);
            }
            payout.ProcessedBy ??= NormalizeOptional(processedBy);
            EnsureTerminalPayoutEvidence(payout, request);
            EnsureCompletionEvidenceFingerprint(payout);
            
            await SaveWithExternalEvidenceConflictHandlingAsync(cancellationToken);
            
            await transaction.CommitAsync(cancellationToken);
            return MapPayout(payout);
        }

        // Short-circuit for terminal Failed state (idempotent retry handling)
        if (payout.Status == CommerceHubPayoutStatus.Failed)
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_ALREADY_FAILED",
                "A failed payout cannot be completed. Create a new payout after reviewing the earnings.",
                StatusCodes.Status409Conflict);
        }

        if (!IsExternallyLockedPayoutStatus(payout.Status))
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_MUST_BE_PROCESSING_BEFORE_COMPLETION",
                "Payout completion requires a previously committed processing or reconciliation-required state before any external transfer is marked completed.",
                StatusCodes.Status409Conflict);
        }

        // MarkProcessing is the only transition allowed to authorize new outward money movement.
        // Once a payout is Processing or ReconciliationRequired, MarkCompleted is reconciliation of an external transfer that
        // may already have executed; blocking it here would desynchronize bank/provider cash from
        // POS ledger truth. Evidence, earning reservation, and ledger delta checks below remain
        // mandatory and keep the transition idempotent.

        EnsureExternalTransferExecutionConfirmed(request);
        EnsureExternalTransferHandoffMatchesPayout(payout, request);
        var stateBefore = await CapturePayoutFinancialStateForCompletionAsync(payout, cancellationToken);

        EnsureRequestedPayoutEvidenceMatchesStored(payout, request);
        EnsureTerminalPayoutEvidence(payout, request);
        await EnsureExternalPayoutEvidenceIsUniqueAsync(payout, request, cancellationToken);

        foreach (var item in payout.Items)
        {
            if (item.Earning.Status != CommerceHubVendorEarningStatus.InPayout || item.Earning.PayoutId != payout.Id)
            {
                throw new CommerceHubCheckoutException(
                    "PAYOUT_EARNING_STATE_INVALID",
                    "One or more earnings are no longer reserved for this payout.",
                    StatusCodes.Status409Conflict);
            }

            ApplyCompletedPayoutAllocation(item.Earning, item.Amount, DateTime.UtcNow);
        }

        payout.Status = CommerceHubPayoutStatus.Completed;
        payout.Reference = NormalizeOptional(request.Reference) ?? payout.Reference;
        payout.ExternalReferenceId = NormalizeOptional(request.ExternalReferenceId) ?? payout.ExternalReferenceId;
        payout.BankTransactionId = NormalizeOptional(request.BankTransactionId) ?? payout.BankTransactionId;
        payout.ProcessedBy = NormalizeOptional(processedBy) ?? payout.ProcessedBy;
        var completedAtUtc = DateTime.UtcNow;
        payout.ProcessedAtUtc ??= completedAtUtc;
        payout.CompletedAtUtc ??= completedAtUtc;
        payout.FailureReason = null;
        payout.CompletionEvidenceFingerprint = BuildCompletionEvidenceFingerprint(payout);

        _auditService.RecordPayoutCompleted(
            tenantId,
            payout.Id,
            payout.Amount,
            payout.Currency,
            correlationId);

        // HARDENING: Wrap both RecordPayoutAsync and SaveChangesAsync in conflict handling
        // because RecordPayoutAsync may internally call SaveChangesAsync and trigger the unique constraint
        try
        {
            await _financialLedgerService.RecordPayoutAsync(payout, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsExternalPayoutEvidenceUniqueViolation(ex))
        {
            // Race condition: another request completed with the same external evidence
            // Translate DB constraint violation into domain-level conflict response
            throw new CommerceHubCheckoutException(
                "PAYOUT_EXTERNAL_EVIDENCE_ALREADY_USED",
                "Payout completion evidence is already attached to another completed payout.",
                StatusCodes.Status409Conflict);
        }

        var stateAfter = await _financialSafetyGuardService.VerifyStateTransitionAsync(
            tenantId,
            payout.Currency,
            stateBefore,
            new CommerceHubFinancialStateDelta(
                PlatformCashDelta: -payout.Amount,
                VendorPayableDelta: -payout.Amount,
                VendorDebtDelta: 0m),
            $"Payout:{payout.Id:N}",
            expectedLedgerEntries: 3,
            CommerceHubFinancialReferenceType.Payout,
            payout.Id,
            cancellationToken);

        _financialSafetyGuardService.AppendCommittedOperationAudit(
            tenantId,
            actorId: null,
            actorType: "SYSTEM",
            operationType: "Payout",
            amount: payout.Amount,
            currency: payout.Currency,
            before: stateBefore,
            after: stateAfter,
            correlationId: $"Payout:{payout.Id:N}",
            idempotencyKey: payout.IdempotencyKey,
            referenceType: CommerceHubFinancialReferenceType.Payout,
            referenceId: payout.Id,
            notes: NormalizeOptional(request.Reference) ?? payout.Reference);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return MapPayout(payout);
    }

    private static void EnsureRequestedPayoutEvidenceMatchesStored(
        CommerceHubPayout payout,
        CommerceHubUpdatePayoutRequestDto request)
    {
        EnsureRequestedEvidenceValueMatchesStored(
            "ExternalReferenceId",
            payout.ExternalReferenceId,
            NormalizeOptional(request.ExternalReferenceId));
        EnsureRequestedEvidenceValueMatchesStored(
            "BankTransactionId",
            payout.BankTransactionId,
            NormalizeOptional(request.BankTransactionId));
    }

    private static void EnsureRequestedEvidenceValueMatchesStored(
        string fieldName,
        string? storedValue,
        string? requestedValue)
    {
        if (string.IsNullOrWhiteSpace(storedValue) ||
            string.IsNullOrWhiteSpace(requestedValue) ||
            string.Equals(storedValue.Trim(), requestedValue.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        throw new CommerceHubCheckoutException(
            "PAYOUT_EXTERNAL_CONFIRMATION_CONFLICT",
            $"Payout completion evidence field {fieldName} does not match the already recorded transfer evidence.",
            StatusCodes.Status409Conflict);
    }

    private static void EnsureExternalTransferIntentMatchesPayout(
        CommerceHubPayout payout,
        CommerceHubUpdatePayoutRequestDto request)
    {
        var expectedKey = NormalizeOptional(payout.ExternalTransferIdempotencyKey);
        var requestedKey = NormalizeOptional(request.ExternalTransferIdempotencyKey);

        if (string.IsNullOrWhiteSpace(expectedKey))
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_EXTERNAL_TRANSFER_INTENT_MISSING",
                "Payout processing requires a POS-issued external transfer idempotency key before external money movement starts.",
                StatusCodes.Status409Conflict);
        }

        if (string.IsNullOrWhiteSpace(requestedKey))
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_EXTERNAL_TRANSFER_INTENT_REQUIRED",
                "Payout processing requires the POS-issued external transfer idempotency key to be echoed by the transfer executor.",
                StatusCodes.Status409Conflict);
        }

        if (string.Equals(expectedKey, requestedKey, StringComparison.Ordinal))
        {
            return;
        }

        throw new CommerceHubCheckoutException(
            "PAYOUT_EXTERNAL_TRANSFER_INTENT_MISMATCH",
            "Payout processing was rejected because the external transfer intent key does not match the durable POS payout record.",
            StatusCodes.Status409Conflict);
    }

    private static void EnsureCompletionEvidenceFingerprint(CommerceHubPayout payout)
    {
        payout.CompletedAtUtc ??= payout.ProcessedAtUtc ?? DateTime.UtcNow;
        var fingerprint = BuildCompletionEvidenceFingerprint(payout);

        if (!string.IsNullOrWhiteSpace(payout.CompletionEvidenceFingerprint) &&
            !string.Equals(payout.CompletionEvidenceFingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_COMPLETION_EVIDENCE_TAMPERED",
                "Completed payout transfer evidence no longer matches the immutable completion fingerprint.",
                StatusCodes.Status409Conflict);
        }

        payout.CompletionEvidenceFingerprint ??= fingerprint;
    }

    private async Task EnsureExternalPayoutEvidenceIsUniqueAsync(
        CommerceHubPayout payout,
        CommerceHubUpdatePayoutRequestDto request,
        CancellationToken cancellationToken)
    {
        var externalReferenceId = NormalizeOptional(request.ExternalReferenceId) ?? NormalizeOptional(payout.ExternalReferenceId);
        var bankTransactionId = NormalizeOptional(request.BankTransactionId) ?? NormalizeOptional(payout.BankTransactionId);

        if (string.IsNullOrWhiteSpace(externalReferenceId) && string.IsNullOrWhiteSpace(bankTransactionId))
        {
            return;
        }

        var evidenceInUse = await _dbContext.CommerceHubPayouts
            .IgnoreQueryFilters()
            .AnyAsync(other =>
                    other.Id != payout.Id &&
                    !other.IsDeleted &&
                    other.Status != CommerceHubPayoutStatus.Failed &&
                    ((!string.IsNullOrWhiteSpace(externalReferenceId) && other.ExternalReferenceId == externalReferenceId) ||
                     (!string.IsNullOrWhiteSpace(bankTransactionId) && other.BankTransactionId == bankTransactionId)),
                cancellationToken);

        if (evidenceInUse)
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_EXTERNAL_EVIDENCE_ALREADY_USED",
                "Payout external transfer evidence is already attached to another active payout.",
                StatusCodes.Status409Conflict);
        }
    }

    private static string BuildCompletionEvidenceFingerprint(CommerceHubPayout payout)
    {
        var evidence = string.Join(
            "|",
            payout.TenantId.ToString("N"),
            payout.Id.ToString("N"),
            payout.Method.ToString(),
            payout.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            payout.Currency.Trim().ToUpperInvariant(),
            NormalizeOptional(payout.ExternalReferenceId) ?? string.Empty,
            NormalizeOptional(payout.BankTransactionId) ?? string.Empty);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence)));
    }

    public async Task<CommerceHubPayoutDto> MarkFailedAsync(
        Guid tenantId,
        Guid payoutId,
        CommerceHubUpdatePayoutRequestDto request,
        string correlationId,
        CancellationToken cancellationToken = default,
        string? processedBy = null)
    {
        using var commerceHubBoundary = CommerceHubBoundaryMutationScope.BeginPayout();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var payout = await LoadTrackedPayoutAsync(tenantId, payoutId, cancellationToken);

        if (payout.Status == CommerceHubPayoutStatus.Completed)
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_ALREADY_COMPLETED",
                "A completed payout cannot be failed or rolled back.",
                StatusCodes.Status409Conflict);
        }

        if (payout.Status == CommerceHubPayoutStatus.Failed)
        {
            return MapPayout(payout);
        }

        if (IsExternallyLockedPayoutStatus(payout.Status) &&
            !request.ExternalTransferConfirmedNotExecuted)
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_EXTERNAL_TRANSFER_RECONCILIATION_REQUIRED",
                "A processing or reconciliation-required payout cannot be failed and released unless the external transfer is confirmed as not executed. Complete the payout if money moved externally.",
                StatusCodes.Status409Conflict);
        }

        if (IsExternallyLockedPayoutStatus(payout.Status))
        {
            EnsureExternalTransferHandoffMatchesPayout(payout, request);
            EnsureProcessingFailureNonExecutionEvidence(request);
        }

        if (IsExternallyLockedPayoutStatus(payout.Status) &&
            (HasExternalPayoutEvidence(request) || HasExternalPayoutEvidence(payout)))
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_FAILURE_WITH_EXTERNAL_EVIDENCE_BLOCKED",
                "Payout failure cannot include external transfer evidence. Use payout completion for executed transfers or omit external evidence after confirming no transfer occurred.",
                StatusCodes.Status409Conflict);
        }

        if (!IsExternallyLockedPayoutStatus(payout.Status))
        {
            await _userGovernanceService.EnsureVendorPayoutAllowedAsync(tenantId, cancellationToken);
        }

        var failureReason = NormalizeOptional(request.FailureReason) ?? "Payout execution failed.";

        foreach (var item in payout.Items)
        {
            if (item.Earning.Status == CommerceHubVendorEarningStatus.InPayout && item.Earning.PayoutId == payout.Id)
            {
                ReleasePayoutAllocation(item.Earning, item.Amount);
            }
        }

        payout.Status = CommerceHubPayoutStatus.Failed;
        payout.Reference = NormalizeOptional(request.Reference) ?? payout.Reference;
        payout.ExternalReferenceId = NormalizeOptional(request.ExternalReferenceId) ?? payout.ExternalReferenceId;
        payout.BankTransactionId = NormalizeOptional(request.BankTransactionId) ?? payout.BankTransactionId;
        payout.ProcessedBy = NormalizeOptional(processedBy) ?? payout.ProcessedBy;
        payout.FailureReason = failureReason;
        payout.ProcessedAtUtc ??= DateTime.UtcNow;

        _auditService.RecordPayoutFailed(
            tenantId,
            payout.Id,
            payout.Currency,
            $"Vendor payout failed and earnings were released | Reason: {failureReason}",
            correlationId);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return MapPayout(payout);
    }

    private static bool HasExternalPayoutEvidence(CommerceHubUpdatePayoutRequestDto request) =>
        !string.IsNullOrWhiteSpace(request.ExternalReferenceId) ||
        !string.IsNullOrWhiteSpace(request.BankTransactionId);

    private static bool HasExternalPayoutEvidence(CommerceHubPayout payout) =>
        !string.IsNullOrWhiteSpace(payout.ExternalReferenceId) ||
        !string.IsNullOrWhiteSpace(payout.BankTransactionId);

    private static bool IsExternallyLockedPayoutStatus(CommerceHubPayoutStatus status) =>
        status is CommerceHubPayoutStatus.Processing or CommerceHubPayoutStatus.ReconciliationRequired;

    private static void EnsureProcessingRequestDoesNotClaimExecutedTransfer(CommerceHubUpdatePayoutRequestDto request)
    {
        if (!request.ExternalTransferConfirmedExecuted)
        {
            return;
        }

        throw new CommerceHubCheckoutException(
            "PAYOUT_PROCESSING_CANNOT_RECONCILE_EXECUTED_TRANSFER",
            "MarkProcessing only authorizes an external transfer handoff. Executed transfer evidence must be reconciled through payout completion.",
            StatusCodes.Status409Conflict);
    }

    private static void EnsureExternalTransferExecutionConfirmed(CommerceHubUpdatePayoutRequestDto request)
    {
        if (request.ExternalTransferConfirmedExecuted)
        {
            return;
        }

        throw new CommerceHubCheckoutException(
            "PAYOUT_EXTERNAL_TRANSFER_EXECUTION_CONFIRMATION_REQUIRED",
            "Completing a processing payout requires explicit confirmation that the external bank or provider transfer executed. Fail the payout with non-execution proof if no money moved.",
            StatusCodes.Status409Conflict);
    }

    private static void EnsureProcessingFailureNonExecutionEvidence(CommerceHubUpdatePayoutRequestDto request)
    {
        if (!string.IsNullOrWhiteSpace(request.Reference) &&
            !string.IsNullOrWhiteSpace(request.FailureReason))
        {
            return;
        }

        throw new CommerceHubCheckoutException(
            "PAYOUT_NON_EXECUTION_EVIDENCE_REQUIRED",
            "Failing a processing payout requires both a failure reason and a bank/provider non-execution reference. Complete the payout if money moved externally.",
            StatusCodes.Status409Conflict);
    }

    private static void EnsureProcessingHandoffReference(
        CommerceHubPayout payout,
        CommerceHubUpdatePayoutRequestDto request)
    {
        var handoffReference =
            NormalizeOptional(request.Reference) ??
            NormalizeOptional(request.ExternalReferenceId) ??
            NormalizeOptional(request.BankTransactionId) ??
            NormalizeOptional(payout.Reference) ??
            NormalizeOptional(payout.ExternalReferenceId) ??
            NormalizeOptional(payout.BankTransactionId);

        if (!string.IsNullOrWhiteSpace(handoffReference))
        {
            return;
        }

        throw new CommerceHubCheckoutException(
            "PAYOUT_EXTERNAL_HANDOFF_REFERENCE_REQUIRED",
            "Payout processing requires a provider, bank, or operator handoff reference before external money movement starts.",
            StatusCodes.Status409Conflict);
    }

    private async Task<CommerceHubFinancialStateSnapshot> CaptureAndValidatePayoutFinancialReadinessAsync(
        CommerceHubPayout payout,
        CancellationToken cancellationToken)
    {
        RequireEgpCurrency(
            payout.Currency,
            "commercehub.payout.financial_readiness.currency",
            $"tenantId:{payout.TenantId:D};payoutId:{payout.Id:D}");

        var state = await _financialSafetyGuardService.CaptureStateForUpdateAsync(
            payout.TenantId,
            payout.Currency,
            cancellationToken);

        await EnsurePayoutEarningsHaveLedgerConfirmationAsync(payout, cancellationToken);

        if (payout.Amount > state.VendorPayable)
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_AMOUNT_EXCEEDS_PAYABLE",
                "Payout amount exceeds the ledger-backed vendor payable balance.",
                StatusCodes.Status409Conflict);
        }

        if (state.PlatformCash < payout.Amount)
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_PLATFORM_CASH_INSUFFICIENT",
                "Platform cash is insufficient to process this payout.",
                StatusCodes.Status409Conflict);
        }

        return state;
    }

    private async Task<CommerceHubFinancialStateSnapshot> CapturePayoutFinancialStateForCompletionAsync(
        CommerceHubPayout payout,
        CancellationToken cancellationToken)
    {
        RequireEgpCurrency(
            payout.Currency,
            "commercehub.payout.completion.currency",
            $"tenantId:{payout.TenantId:D};payoutId:{payout.Id:D}");

        var state = await _financialSafetyGuardService.CaptureStateForUpdateAsync(
            payout.TenantId,
            payout.Currency,
            cancellationToken);

        await EnsurePayoutEarningsHaveLedgerConfirmationAsync(payout, cancellationToken);

        return state;
    }

    private async Task EnsurePayoutEarningsHaveLedgerConfirmationAsync(
        CommerceHubPayout payout,
        CancellationToken cancellationToken)
    {
        await EnsureEarningsHaveLedgerConfirmationAsync(
            payout.TenantId,
            payout.Currency,
            payout.Items.Select(item => item.Earning).ToList(),
            cancellationToken);
    }

    private async Task EnsureEarningsHaveLedgerConfirmationAsync(
        Guid tenantId,
        string currency,
        IReadOnlyCollection<CommerceHubVendorEarning> earnings,
        CancellationToken cancellationToken)
    {
        var authorityCurrency = RequireEgpCurrency(
            currency,
            "commercehub.payout.ledger_confirmation.currency",
            $"tenantId:{tenantId:D}");

        var positiveOrderSubOrderIds = earnings
            .Where(earning => earning.Amount > 0m)
            .Select(earning => earning.SubOrderId)
            .Distinct()
            .ToList();

        var negativeRefundIds = earnings
            .Where(earning => earning.Amount < 0m && earning.RefundId.HasValue)
            .Select(earning => earning.RefundId!.Value)
            .Distinct()
            .ToList();

        var confirmedOrderSubOrderIds = positiveOrderSubOrderIds.Count == 0
            ? new HashSet<Guid>()
            : (await _dbContext.CommerceHubFinancialTransactions
                .AsNoTracking()
                .Where(transaction =>
                    transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                    transaction.EntityId == tenantId &&
                    transaction.TransactionType == CommerceHubFinancialTransactionType.OrderPayment &&
                    transaction.Direction == CommerceHubFinancialTransactionDirection.Credit &&
                    transaction.ReferenceType == CommerceHubFinancialReferenceType.Order &&
                    positiveOrderSubOrderIds.Contains(transaction.ReferenceId) &&
                    transaction.Currency == authorityCurrency)
                .Select(transaction => transaction.ReferenceId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var confirmedRefundIds = negativeRefundIds.Count == 0
            ? new HashSet<Guid>()
            : (await _dbContext.CommerceHubFinancialTransactions
                .AsNoTracking()
                .Where(transaction =>
                    transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                    transaction.EntityId == tenantId &&
                    transaction.TransactionType == CommerceHubFinancialTransactionType.Refund &&
                    transaction.Direction == CommerceHubFinancialTransactionDirection.Debit &&
                    transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund &&
                    negativeRefundIds.Contains(transaction.ReferenceId) &&
                    transaction.Currency == authorityCurrency)
                .Select(transaction => transaction.ReferenceId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var missingLedgerEarningIds = earnings
            .Where(earning =>
                (earning.Amount > 0m && !confirmedOrderSubOrderIds.Contains(earning.SubOrderId)) ||
                (earning.Amount < 0m && (!earning.RefundId.HasValue || !confirmedRefundIds.Contains(earning.RefundId.Value))))
            .Select(earning => earning.Id)
            .Distinct()
            .ToList();

        if (missingLedgerEarningIds.Count > 0)
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_LEDGER_CONFIRMATION_REQUIRED",
                "Payout cannot proceed until every selected earning is confirmed in the CommerceHub financial ledger.",
                StatusCodes.Status409Conflict,
                new { earningIds = missingLedgerEarningIds });
        }
    }

    private static decimal ResolvePayoutAllocatableAmount(CommerceHubVendorEarning earning)
    {
        if (earning.Amount <= 0m)
        {
            return RoundCurrency(earning.Amount);
        }

        return RoundCurrency(Math.Max(0m, earning.Amount - earning.PaidOutAmount - earning.PayoutReservedAmount));
    }

    private static void ApplyCompletedPayoutAllocation(
        CommerceHubVendorEarning earning,
        decimal payoutItemAmount,
        DateTime paidOutAtUtc)
    {
        if (earning.Amount <= 0m)
        {
            earning.Status = CommerceHubVendorEarningStatus.PaidOut;
            earning.PaidOutAtUtc = paidOutAtUtc;
            earning.PayoutReservedAmount = 0m;
            return;
        }

        var allocation = RoundCurrency(Math.Max(0m, payoutItemAmount));
        earning.PayoutReservedAmount = RoundCurrency(Math.Max(0m, earning.PayoutReservedAmount - allocation));
        earning.PaidOutAmount = RoundCurrency(Math.Min(earning.Amount, earning.PaidOutAmount + allocation));

        if (earning.PaidOutAmount >= earning.Amount)
        {
            earning.Status = CommerceHubVendorEarningStatus.PaidOut;
            earning.PaidOutAtUtc = paidOutAtUtc;
            earning.PayoutId = null;
        }
        else
        {
            earning.Status = CommerceHubVendorEarningStatus.Available;
            earning.PayoutId = null;
        }
    }

    private static void ReleasePayoutAllocation(CommerceHubVendorEarning earning, decimal payoutItemAmount)
    {
        if (earning.Amount > 0m)
        {
            var allocation = RoundCurrency(Math.Max(0m, payoutItemAmount));
            earning.PayoutReservedAmount = RoundCurrency(Math.Max(0m, earning.PayoutReservedAmount - allocation));
        }

        earning.Status = CommerceHubVendorEarningStatus.Available;
        earning.PayoutId = null;
    }

    private async Task<CommerceHubPayout?> FindPayoutByIdempotencyAsync(
        Guid tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return await _dbContext.CommerceHubPayouts
            .AsNoTracking()
            .Include(payout => payout.Items)
            .ThenInclude(item => item.Earning)
            .ThenInclude(earning => earning.SubOrder)
            .FirstOrDefaultAsync(
                payout => payout.TenantId == tenantId &&
                          payout.IdempotencyKey == idempotencyKey &&
                          !payout.IsDeleted,
                cancellationToken);
    }

    private async Task<CommerceHubPayoutDto> GetRequiredPayoutAsync(
        Guid tenantId,
        Guid payoutId,
        CancellationToken cancellationToken)
    {
        var payout = await _dbContext.CommerceHubPayouts
            .AsNoTracking()
            .Include(item => item.Items)
            .ThenInclude(item => item.Earning)
            .ThenInclude(earning => earning.SubOrder)
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId &&
                        item.Id == payoutId &&
                        !item.IsDeleted,
                cancellationToken);

        if (payout == null)
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_NOT_FOUND",
                "Payout was not found.",
                StatusCodes.Status404NotFound);
        }

        return MapPayout(payout);
    }

    private async Task LockVendorEarningsForPayoutCreationAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> earningIds,
        string lockScope,
        CancellationToken cancellationToken)
    {
        var distinctIds = earningIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        if (distinctIds.Count == 0)
        {
            return;
        }

        var providerName = _dbContext.Database.ProviderName ?? string.Empty;

        if (providerName.Contains("InMemory", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "LockVendorEarningsForPayoutCreation | TenantId: {TenantId} | Scope: {Scope} | IN_MEMORY_PROVIDER | Skipping database row locks for test provider",
                tenantId,
                lockScope);
            return;
        }

        foreach (var earningId in distinctIds)
        {
            if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
                providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $@"SELECT 1 FROM ""CommerceHubVendorEarnings""
                           WHERE ""Id"" = {earningId}
                             AND ""TenantId"" = {tenantId}
                             AND ""IsDeleted"" = false
                           FOR UPDATE NOWAIT",
                        cancellationToken);
                }
                catch (PostgresException ex) when (ex.SqlState == "55P03")
                {
                    throw new CommerceHubCheckoutException(
                        "PAYOUT_EARNING_LOCKED",
                        "One or more selected earnings are currently being reserved by another payout request. Please retry.",
                        StatusCodes.Status409Conflict,
                        new { earningId, lockScope });
                }

                continue;
            }

            if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $@"SELECT 1 FROM [CommerceHubVendorEarnings] WITH (UPDLOCK, ROWLOCK, NOWAIT)
                           WHERE [Id] = {earningId}
                             AND [TenantId] = {tenantId}
                             AND [IsDeleted] = 0",
                        cancellationToken);
                }
                catch (DbException ex) when (IsSqlServerLockTimeout(ex))
                {
                    throw new CommerceHubCheckoutException(
                        "PAYOUT_EARNING_LOCKED",
                        "One or more selected earnings are currently being reserved by another payout request. Please retry.",
                        StatusCodes.Status409Conflict,
                        new { earningId, lockScope });
                }

                continue;
            }

            _logger.LogError(
                "LockVendorEarningsForPayoutCreation | TenantId: {TenantId} | Scope: {Scope} | UNSUPPORTED_PROVIDER | Provider: {Provider}",
                tenantId,
                lockScope,
                providerName);
            throw new InvalidOperationException(
                $"Database provider '{providerName}' does not support pessimistic row-level locking required for payout earning reservations. " +
                "Supported providers: PostgreSQL (Npgsql), SQL Server (Microsoft.Data.SqlClient).");
        }
    }

    private async Task<CommerceHubPayout> LoadTrackedPayoutAsync(
        Guid tenantId,
        Guid payoutId,
        CancellationToken cancellationToken)
    {
        // PRODUCTION HARDENING: Pessimistic Row-Level Locking
        // Acquire an exclusive lock on the payout row before evaluating state transitions
        // to prevent concurrent overlapping state modifications that bypass application logic.
        var providerName = _dbContext.Database.ProviderName ?? string.Empty;

        if (providerName.Contains("InMemory", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "LoadTrackedPayout | TenantId: {TenantId} | PayoutId: {PayoutId} | IN_MEMORY_PROVIDER | Skipping database row lock for test provider",
                tenantId, payoutId);
        }
        else if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            // PostgreSQL: FOR UPDATE with NOWAIT to fail fast on lock contention
            try
            {
                await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $@"SELECT 1 FROM ""CommerceHubPayouts"" 
                       WHERE ""Id"" = {payoutId} AND ""TenantId"" = {tenantId} 
                       FOR UPDATE NOWAIT", 
                    cancellationToken);
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "55P03") // lock_not_available
            {
                _logger.LogWarning(
                    "LoadTrackedPayout | TenantId: {TenantId} | PayoutId: {PayoutId} | LOCK_CONTENTION | Another process holds the lock",
                    tenantId, payoutId);
                throw new CommerceHubCheckoutException(
                    "PAYOUT_LOCKED",
                    "Payout is currently being processed by another request. Please retry.",
                    StatusCodes.Status409Conflict);
            }
        }
        else if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            // SQL Server: UPDLOCK + ROWLOCK with NOWAIT-equivalent behavior
            try
            {
                await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $@"SELECT 1 FROM [CommerceHubPayouts] WITH (UPDLOCK, ROWLOCK, NOWAIT)
                       WHERE [Id] = {payoutId} AND [TenantId] = {tenantId}", 
                    cancellationToken);
            }
            catch (DbException ex) when (IsSqlServerLockTimeout(ex))
            {
                _logger.LogWarning(
                    "LoadTrackedPayout | TenantId: {TenantId} | PayoutId: {PayoutId} | LOCK_CONTENTION | Another process holds the lock",
                    tenantId, payoutId);
                throw new CommerceHubCheckoutException(
                    "PAYOUT_LOCKED",
                    "Payout is currently being processed by another request. Please retry.",
                    StatusCodes.Status409Conflict);
            }
        }
        else
        {
            // Unsupported provider: fail fast to prevent silent race conditions
            _logger.LogError(
                "LoadTrackedPayout | TenantId: {TenantId} | PayoutId: {PayoutId} | UNSUPPORTED_PROVIDER | Provider: {Provider} | Row-level locking not implemented",
                tenantId, payoutId, providerName);
            throw new InvalidOperationException(
                $"Database provider '{providerName}' does not support pessimistic row-level locking required for payout state transitions. " +
                "Supported providers: PostgreSQL (Npgsql), SQL Server (Microsoft.Data.SqlClient).");
        }

        var payout = await _dbContext.CommerceHubPayouts
            .Include(item => item.Items)
            .ThenInclude(item => item.Earning)
            .ThenInclude(earning => earning.SubOrder)
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId &&
                        item.Id == payoutId &&
                        !item.IsDeleted,
                cancellationToken);

        if (payout == null)
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_NOT_FOUND",
                "Payout was not found.",
                StatusCodes.Status404NotFound);
        }

        return payout;
    }

    private static CommerceHubPayoutDto MapPayout(CommerceHubPayout payout)
    {
        var currency = RequireEgpCurrency(
            payout.Currency,
            "commercehub.payout.dto.currency",
            $"tenantId:{payout.TenantId:D};payoutId:{payout.Id:D}");

        return new CommerceHubPayoutDto
        {
            PayoutId = payout.Id,
            TenantId = payout.TenantId,
            PayoutMethodId = payout.PayoutMethodId,
            Amount = payout.Amount,
            Currency = currency,
            Status = payout.Status.ToString(),
            Method = payout.Method.ToString(),
            DestinationValueSnapshot = payout.DestinationValueSnapshot,
            Reference = payout.Reference,
            ExternalReferenceId = payout.ExternalReferenceId,
            BankTransactionId = payout.BankTransactionId,
            ExternalTransferIdempotencyKey = payout.ExternalTransferIdempotencyKey,
            ExternalTransferHandoffToken = payout.ExternalTransferHandoffToken,
            ExternalTransferHandoffCommittedAtUtc = payout.ExternalTransferHandoffCommittedAtUtc,
            ProcessedBy = payout.ProcessedBy,
            FailureReason = payout.FailureReason,
            IdempotencyKey = payout.IdempotencyKey,
            CreatedAtUtc = payout.CreatedAt,
            ProcessedAtUtc = payout.ProcessedAtUtc,
            Items = payout.Items
                .OrderBy(item => item.Earning.SubOrder.OrderNumber, StringComparer.OrdinalIgnoreCase)
                .Select(item => new CommerceHubPayoutItemDto
                {
                    TenantId = payout.TenantId,
                    PayoutId = payout.Id,
                    PayoutItemId = item.Id,
                    EarningId = item.EarningId,
                    SubOrderId = item.Earning.SubOrderId,
                    OrderNumber = item.Earning.SubOrder.OrderNumber,
                    Amount = item.Amount
                })
                .ToList()
        };
    }

    private static string NormalizeRequired(string? value, string code, string message)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        throw new CommerceHubCheckoutException(code, message, StatusCodes.Status400BadRequest);
    }

    private static string BuildExternalTransferIdempotencyKey(CommerceHubPayout payout) =>
        $"CH-PAYOUT:{payout.TenantId:N}:{payout.Id:N}";

    private static string BuildExternalTransferHandoffToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static void EnsureProcessingHandoffCommitted(CommerceHubPayout payout)
    {
        payout.ExternalTransferHandoffToken ??= BuildExternalTransferHandoffToken();
        payout.ExternalTransferHandoffCommittedAtUtc ??= DateTime.UtcNow;
    }

    private static void EnsureExternalTransferHandoffMatchesPayout(
        CommerceHubPayout payout,
        CommerceHubUpdatePayoutRequestDto request)
    {
        var expectedToken = NormalizeOptional(payout.ExternalTransferHandoffToken);
        var requestedToken = NormalizeOptional(request.ExternalTransferHandoffToken);

        if (string.IsNullOrWhiteSpace(expectedToken) ||
            !payout.ExternalTransferHandoffCommittedAtUtc.HasValue)
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_EXTERNAL_HANDOFF_NOT_COMMITTED",
                "Payout reconciliation requires a POS-issued handoff token from a committed Processing transition.",
                StatusCodes.Status409Conflict);
        }

        if (string.IsNullOrWhiteSpace(requestedToken))
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_EXTERNAL_HANDOFF_TOKEN_REQUIRED",
                "Completing or failing a processing payout requires echoing the POS-issued committed handoff token.",
                StatusCodes.Status409Conflict);
        }

        if (string.Equals(expectedToken, requestedToken, StringComparison.Ordinal))
        {
            return;
        }

        throw new CommerceHubCheckoutException(
            "PAYOUT_EXTERNAL_HANDOFF_TOKEN_MISMATCH",
            "Payout reconciliation was rejected because the handoff token does not match the committed POS processing transition.",
            StatusCodes.Status409Conflict);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsSqlServerLockTimeout(DbException exception) =>
        exception.GetType().FullName == "Microsoft.Data.SqlClient.SqlException" &&
        exception.GetType().GetProperty("Number")?.GetValue(exception) is int number &&
        number == 1222;

    private static void EnsureTerminalPayoutEvidence(
        CommerceHubPayout payout,
        CommerceHubUpdatePayoutRequestDto request)
    {
        var externalReference = NormalizeOptional(request.ExternalReferenceId) ?? payout.ExternalReferenceId;
        var bankReference = NormalizeOptional(request.BankTransactionId) ?? payout.BankTransactionId;
        if (!string.IsNullOrWhiteSpace(externalReference) || !string.IsNullOrWhiteSpace(bankReference))
        {
            return;
        }

        throw new CommerceHubCheckoutException(
            "PAYOUT_EXTERNAL_CONFIRMATION_REQUIRED",
            "Completed payouts require a terminal bank, provider, or manual transfer reference before completion.",
            StatusCodes.Status409Conflict);
    }

    private static decimal RoundCurrency(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static string RequireEgpCurrency(string? currency, string component, string context) =>
        FinancialCurrencyGuard.RequireEgpCurrency(currency, component, context);

    /// <summary>
    /// Detects if a DbUpdateException is caused by a unique constraint violation
    /// on the external payout evidence indexes (ExternalReferenceId or BankTransactionId).
    /// </summary>
    private static bool IsExternalPayoutEvidenceUniqueViolation(DbUpdateException exception)
    {
        // Check for PostgreSQL unique constraint violation
        if (exception.InnerException is PostgresException postgresException)
        {
            if (postgresException.SqlState != PostgresErrorCodes.UniqueViolation)
            {
                return false;
            }

            // Check if the constraint name matches either of the external evidence unique indexes
            return string.Equals(
                       postgresException.ConstraintName,
                       PayoutCompletedExternalReferenceIdUnique,
                       StringComparison.Ordinal) ||
                   string.Equals(
                       postgresException.ConstraintName,
                       PayoutCompletedBankTransactionIdUnique,
                       StringComparison.Ordinal) ||
                   string.Equals(
                       postgresException.ConstraintName,
                       PayoutLiveExternalReferenceIdUnique,
                       StringComparison.Ordinal) ||
                   string.Equals(
                       postgresException.ConstraintName,
                       PayoutLiveBankTransactionIdUnique,
                       StringComparison.Ordinal);
        }

        // Fallback: check error message for SQLite or other databases
        var errorMessage = exception.InnerException?.Message ?? exception.Message;
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return false;
        }

        return errorMessage.Contains(PayoutCompletedExternalReferenceIdUnique, StringComparison.Ordinal) ||
               errorMessage.Contains(PayoutCompletedBankTransactionIdUnique, StringComparison.Ordinal) ||
               errorMessage.Contains(PayoutLiveExternalReferenceIdUnique, StringComparison.Ordinal) ||
               errorMessage.Contains(PayoutLiveBankTransactionIdUnique, StringComparison.Ordinal);
    }

    private static bool IsPayoutCreationConcurrencyConflict(DbUpdateException exception)
    {
        if (exception.InnerException is PostgresException postgresException)
        {
            return postgresException.SqlState is PostgresErrorCodes.SerializationFailure or
                PostgresErrorCodes.DeadlockDetected or
                PostgresErrorCodes.UniqueViolation;
        }

        if (exception.InnerException is DbException dbException)
        {
            return IsSqlServerDeadlockOrSerializationConflict(dbException);
        }

        var errorMessage = exception.InnerException?.Message ?? exception.Message;
        return errorMessage.Contains("40001", StringComparison.OrdinalIgnoreCase) ||
               errorMessage.Contains("deadlock", StringComparison.OrdinalIgnoreCase) ||
               errorMessage.Contains("serialization", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSqlServerDeadlockOrSerializationConflict(DbException exception) =>
        exception.GetType().FullName == "Microsoft.Data.SqlClient.SqlException" &&
        exception.GetType().GetProperty("Number")?.GetValue(exception) is int number &&
        number is 1205 or 3960 or 2627 or 2601;

    private async Task SavePayoutCreationWithConcurrencyConflictHandlingAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsPayoutCreationConcurrencyConflict(ex) && !IsIdempotencyKeyUniqueViolation(ex))
        {
            throw new CommerceHubCheckoutException(
                "PAYOUT_CREATION_CONCURRENCY_CONFLICT",
                "Payout creation conflicted with another concurrent payout or earning mutation. Please refresh and retry.",
                StatusCodes.Status409Conflict);
        }
        // Note: Idempotency key unique violations are NOT caught here - they're handled by the caller
        // to enable replay of the winner's payout result
    }

    private static bool IsIdempotencyKeyUniqueViolation(DbUpdateException exception)
    {
        if (exception.InnerException is PostgresException postgresException)
        {
            // Check if it's a unique violation on the IdempotencyKey index
            return postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
                   ((postgresException.ConstraintName?.Contains("IdempotencyKey", StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (postgresException.MessageText?.Contains("IdempotencyKey", StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (exception.InnerException is DbException dbException)
        {
            var errorMessage = dbException.Message;
            return errorMessage.Contains("IdempotencyKey", StringComparison.OrdinalIgnoreCase) &&
                   (errorMessage.Contains("unique", StringComparison.OrdinalIgnoreCase) ||
                    errorMessage.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    /// <summary>
    /// Saves changes with automatic handling of external payout evidence unique constraint violations.
    /// Translates DB constraint violations into domain-level conflict exceptions.
    /// </summary>
    private async Task SaveWithExternalEvidenceConflictHandlingAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsExternalPayoutEvidenceUniqueViolation(ex))
        {
            // Race condition: another request completed with the same external evidence
            // Translate DB constraint violation into domain-level conflict response
            throw new CommerceHubCheckoutException(
                "PAYOUT_EXTERNAL_EVIDENCE_ALREADY_USED",
                "Payout external transfer evidence is already attached to another active payout.",
                StatusCodes.Status409Conflict);
        }
    }
}
