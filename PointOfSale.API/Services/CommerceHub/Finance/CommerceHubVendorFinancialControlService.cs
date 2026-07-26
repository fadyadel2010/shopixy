using System.Data;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub.FinancialLedger;
using PointOfSale.API.Services.CommerceHub.Wallet;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Financial;
using PointOfSale.Infrastructure.Data;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace PointOfSale.API.Services.CommerceHub.Finance;

public interface ICommerceHubVendorFinancialControlService
{
    Task<CommerceHubVendorFinancialControlDto> GetVendorControlAsync(Guid vendorId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Guid>> GetVendorsSuspendedFromNewOrdersAsync(
        IEnumerable<Guid> vendorIds,
        CancellationToken cancellationToken = default);

    Task<CommerceHubVendorFinancialControlActionResultDto> RecordSettlementPaymentAsync(
        Guid vendorId,
        CommerceHubRecordSettlementPaymentRequest request,
        Guid? actorId,
        string? actorEmail,
        CancellationToken cancellationToken = default);

    Task<CommerceHubVendorFinancialControlActionResultDto> AdjustVendorBalanceAsync(
        Guid vendorId,
        CommerceHubAdjustVendorBalanceRequest request,
        Guid? actorId,
        string? actorEmail,
        CancellationToken cancellationToken = default);

    Task<CommerceHubVendorFinancialControlActionResultDto> ClearVendorDebtAsync(
        Guid vendorId,
        CommerceHubClearVendorDebtRequest request,
        Guid? actorId,
        string? actorEmail,
        CancellationToken cancellationToken = default);
}

public sealed class CommerceHubVendorFinancialControlException : Exception
{
    public CommerceHubVendorFinancialControlException(
        string code,
        string message,
        int statusCode)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
}

internal static class CommerceHubVendorFinancialControlCalculator
{
    public static CommerceHubVendorFinancialControlDto Create(
        Guid vendorId,
        CommerceHubWalletBalanceDto balance,
        CommerceHubVendorDebtSnapshot debtSnapshot)
    {
        return new CommerceHubVendorFinancialControlDto
        {
            VendorId = vendorId,
            CurrentBalance = balance.Balance,
            AmountOwedByVendor = debtSnapshot.AmountOwedByVendor,
            DebtStatus = debtSnapshot.Status,
            IsAtRisk = debtSnapshot.IsAtRisk,
            IsSuspendedFromNewOrders = debtSnapshot.IsSuspendedFromNewOrders,
            IsGracePeriodActive = debtSnapshot.IsGracePeriodActive,
            IsNearThreshold = debtSnapshot.IsNearThreshold,
            HardLimitAmount = debtSnapshot.DebtThresholdAmount,
            GracePeriodDays = debtSnapshot.GracePeriodDays,
            ThresholdCrossedAtUtc = debtSnapshot.ThresholdCrossedAtUtc,
            GraceDeadlineUtc = debtSnapshot.GraceDeadlineUtc,
            MinimumPaymentToRestoreOrdering = debtSnapshot.MinimumPaymentToRestoreOrdering,
            TotalSettlementPaymentsToPlatform = debtSnapshot.TotalSettlementPaymentsToPlatform,
            Currency = balance.Currency,
            TransactionsCount = balance.TransactionsCount,
            LastTransactionAtUtc = balance.LastTransactionAtUtc,
            RequiredAction = debtSnapshot.RequiredAction,
            RecentSettlements = debtSnapshot.RecentSettlements.ToList()
        };
    }
}

public sealed class CommerceHubVendorFinancialControlService : ICommerceHubVendorFinancialControlService
{
    private const string DefaultCurrency = FinancialCurrencyGuard.PlatformBaseCurrencyCode;
    private readonly ApplicationDbContext _dbContext;
    private readonly ICommerceHubWalletService _walletService;
    private readonly ICommerceHubVendorDebtService _vendorDebtService;
    private readonly ICommerceHubFinancialLedgerService _financialLedgerService;
    private readonly ICommerceHubFinancialSafetyGuardService _financialSafetyGuardService;

    public CommerceHubVendorFinancialControlService(
        ApplicationDbContext dbContext,
        ICommerceHubWalletService walletService,
        ICommerceHubVendorDebtService vendorDebtService,
        ICommerceHubFinancialLedgerService financialLedgerService,
        ICommerceHubFinancialSafetyGuardService financialSafetyGuardService)
    {
        _dbContext = dbContext;
        _walletService = walletService;
        _vendorDebtService = vendorDebtService;
        _financialLedgerService = financialLedgerService;
        _financialSafetyGuardService = financialSafetyGuardService;
    }

    public async Task<CommerceHubVendorFinancialControlDto> GetVendorControlAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default)
    {
        var vendor = await LoadVendorAsync(vendorId, cancellationToken);
        var balance = await _walletService.GetVendorBalanceAsync(vendorId, cancellationToken);
        var debtSnapshot = await _vendorDebtService.GetVendorDebtSnapshotAsync(vendorId, cancellationToken);

        return CommerceHubVendorFinancialControlCalculator.Create(
            vendor.Id,
            balance,
            debtSnapshot);
    }

    public async Task<IReadOnlyCollection<Guid>> GetVendorsSuspendedFromNewOrdersAsync(
        IEnumerable<Guid> vendorIds,
        CancellationToken cancellationToken = default)
    {
        var normalizedVendorIds = vendorIds
            .Where(vendorId => vendorId != Guid.Empty)
            .Distinct()
            .ToList();

        if (normalizedVendorIds.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        var snapshots = await _vendorDebtService.GetVendorDebtSnapshotsAsync(normalizedVendorIds, cancellationToken);
        return snapshots.Values
            .Where(snapshot => snapshot.IsSuspendedFromNewOrders)
            .Select(snapshot => snapshot.VendorId)
            .ToList();
    }

    public Task<CommerceHubVendorFinancialControlActionResultDto> RecordSettlementPaymentAsync(
        Guid vendorId,
        CommerceHubRecordSettlementPaymentRequest request,
        Guid? actorId,
        string? actorEmail,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return RecordSettlementAsync(
            vendorId,
            request.Amount,
            request.Currency,
            request.IdempotencyKey,
            request.ExternalReference,
            request.Notes,
            actorId,
            actorEmail,
            CommerceHubVendorSettlementType.SettlementPayment,
            CommerceHubFinancialReferenceType.Settlement,
            CommerceHubFinancialTransactionDirection.Credit,
            CommerceHubFinancialTransactionDirection.Credit,
            cancellationToken);
    }

    public Task<CommerceHubVendorFinancialControlActionResultDto> AdjustVendorBalanceAsync(
        Guid vendorId,
        CommerceHubAdjustVendorBalanceRequest request,
        Guid? actorId,
        string? actorEmail,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Notes))
        {
            throw new CommerceHubVendorFinancialControlException(
                "ADJUSTMENT_NOTES_REQUIRED",
                "Adjustment notes are required for vendor balance changes.",
                StatusCodes.Status400BadRequest);
        }

        return RecordSettlementAsync(
            vendorId,
            request.Amount,
            request.Currency,
            request.IdempotencyKey,
            request.ExternalReference,
            request.Notes,
            actorId,
            actorEmail,
            CommerceHubVendorSettlementType.AdminAdjustment,
            CommerceHubFinancialReferenceType.Adjustment,
            request.Direction,
            request.Direction,
            cancellationToken);
    }

    public async Task<CommerceHubVendorFinancialControlActionResultDto> ClearVendorDebtAsync(
        Guid vendorId,
        CommerceHubClearVendorDebtRequest request,
        Guid? actorId,
        string? actorEmail,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Notes))
        {
            throw new CommerceHubVendorFinancialControlException(
                "DEBT_CLEARANCE_NOTES_REQUIRED",
                "Debt clearance notes are required.",
                StatusCodes.Status400BadRequest);
        }

        var control = await GetVendorControlAsync(vendorId, cancellationToken);
        if (control.AmountOwedByVendor <= 0m)
        {
            throw new CommerceHubVendorFinancialControlException(
                "NO_VENDOR_DEBT",
                "Vendor has no outstanding debt to clear.",
                StatusCodes.Status409Conflict);
        }

        return await RecordSettlementAsync(
            vendorId,
            control.AmountOwedByVendor,
            string.IsNullOrWhiteSpace(request.Currency) ? control.Currency : request.Currency,
            request.IdempotencyKey,
            request.ExternalReference,
            request.Notes,
            actorId,
            actorEmail,
            CommerceHubVendorSettlementType.DebtClearance,
            CommerceHubFinancialReferenceType.Adjustment,
            CommerceHubFinancialTransactionDirection.Credit,
            CommerceHubFinancialTransactionDirection.Credit,
            cancellationToken);
    }

    private async Task<CommerceHubVendorFinancialControlActionResultDto> RecordSettlementAsync(
        Guid vendorId,
        decimal amount,
        string? currency,
        string? idempotencyKey,
        string? externalReference,
        string? notes,
        Guid? actorId,
        string? actorEmail,
        CommerceHubVendorSettlementType settlementType,
        CommerceHubFinancialReferenceType referenceType,
        CommerceHubFinancialTransactionDirection vendorDirection,
        CommerceHubFinancialTransactionDirection platformDirection,
        CancellationToken cancellationToken)
    {
        var vendor = await LoadVendorAsync(vendorId, cancellationToken);

        if (amount <= 0m)
        {
            throw new CommerceHubVendorFinancialControlException(
                "AMOUNT_INVALID",
                "Amount must be greater than zero.",
                StatusCodes.Status400BadRequest);
        }

        var normalizedCurrency = NormalizeCurrency(currency);
        var normalizedIdempotencyKey = NormalizeRequiredOperationReference(
            idempotencyKey,
            "IDEMPOTENCY_KEY_REQUIRED",
            "An idempotency key is required for vendor financial control operations.",
            120);
        var normalizedExternalReference = NormalizeRequiredOperationReference(
            externalReference,
            "EXTERNAL_REFERENCE_REQUIRED",
            "External payment, approval, or adjustment evidence is required for vendor financial control operations.",
            160);
        var normalizedNotes = NormalizeNotes(notes);

        var existingByIdempotency = await _dbContext.CommerceHubVendorSettlements
            .AsNoTracking()
            .FirstOrDefaultAsync(entry =>
                entry.VendorId == vendorId &&
                entry.IdempotencyKey == normalizedIdempotencyKey,
                cancellationToken);

        if (existingByIdempotency != null)
        {
            EnsureSettlementReplayMatchesOriginal(
                existingByIdempotency,
                settlementType,
                vendorDirection,
                amount,
                normalizedCurrency,
                normalizedExternalReference);

            return new CommerceHubVendorFinancialControlActionResultDto
            {
                Settlement = MapSettlement(existingByIdempotency),
                Control = await GetVendorControlAsync(vendorId, cancellationToken)
            };
        }

        var existingExternalReference = await _dbContext.CommerceHubVendorSettlements
            .AsNoTracking()
            .Where(entry => entry.ExternalReference == normalizedExternalReference)
            .Select(entry => new
            {
                entry.Id,
                entry.VendorId,
                entry.SettlementType,
                entry.IdempotencyKey
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (existingExternalReference != null)
        {
            throw new CommerceHubVendorFinancialControlException(
                "EXTERNAL_REFERENCE_ALREADY_USED",
                "This vendor financial evidence reference has already been used for another settlement, clearance, or adjustment.",
                StatusCodes.Status409Conflict);
        }

        var settlement = CommerceHubVendorSettlement.Create(
            vendorId,
            settlementType,
            vendorDirection,
            amount,
            normalizedCurrency,
            normalizedIdempotencyKey,
            normalizedExternalReference,
            normalizedNotes,
            actorId,
            actorEmail);

        var vendorDescription = BuildVendorDescription(vendor.Name, settlementType, settlement.Id);
        var platformDescription = BuildPlatformDescription(vendor.Name, settlementType, settlement.Id);
        var ownsTransaction = _dbContext.Database.CurrentTransaction == null && _dbContext.Database.IsRelational();
        await using var transaction = ownsTransaction
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        try
        {
            _dbContext.CommerceHubVendorSettlements.Add(settlement);

            var stateBefore = await _financialSafetyGuardService.CaptureStateForUpdateAsync(
                vendorId,
                normalizedCurrency,
                cancellationToken);

            if (settlementType == CommerceHubVendorSettlementType.SettlementPayment &&
                amount > stateBefore.VendorDebt)
            {
                throw new CommerceHubVendorFinancialControlException(
                    "PAYMENT_AMOUNT_EXCEEDS_DEBT",
                    "Settlement payment amount cannot exceed the vendor's outstanding debt.",
                    StatusCodes.Status409Conflict);
            }

            if (settlementType == CommerceHubVendorSettlementType.DebtClearance &&
                amount > stateBefore.VendorDebt)
            {
                throw new CommerceHubVendorFinancialControlException(
                    "DEBT_CLEARANCE_EXCEEDS_DEBT",
                    "Debt clearance amount cannot exceed the vendor's outstanding debt.",
                    StatusCodes.Status409Conflict);
            }

            var mirroredPosting = ResolveMirroredPosting(settlementType, vendorDirection);
            var correlationId = BuildCorrelationId(referenceType, settlement.Id);

            await _financialLedgerService.RecordMirroredTransactionsAsync(
                vendorId,
                amount,
                normalizedCurrency,
                referenceType,
                settlement.Id,
                mirroredPosting.VendorTransactionType,
                vendorDirection,
                mirroredPosting.VendorBucket,
                vendorDescription,
                mirroredPosting.PlatformTransactionType,
                platformDirection,
                mirroredPosting.PlatformBucket,
                platformDescription,
                correlationId: correlationId,
                createdBy: actorId,
                reason: normalizedNotes,
                cancellationToken: cancellationToken);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException dbEx)
            {
                if (IsIdempotencyConstraintViolation(dbEx))
                {
                    var reloadedSettlement = await _dbContext.CommerceHubVendorSettlements
                        .AsNoTracking()
                        .FirstOrDefaultAsync(entry =>
                            entry.VendorId == vendorId &&
                            entry.IdempotencyKey == normalizedIdempotencyKey,
                            cancellationToken);

                    if (reloadedSettlement != null)
                    {
                        EnsureSettlementReplayMatchesOriginal(
                            reloadedSettlement,
                            settlementType,
                            vendorDirection,
                            amount,
                            normalizedCurrency,
                            normalizedExternalReference);

                        // Roll back transaction and remove only this operation's pending entities.
                        if (transaction != null)
                        {
                            await transaction.RollbackAsync(cancellationToken);
                        }
                        CleanupSettlementReplayEntries(ownsTransaction, settlement.Id, referenceType, correlationId);

                        return new CommerceHubVendorFinancialControlActionResultDto
                        {
                            Settlement = MapSettlement(reloadedSettlement),
                            Control = await GetVendorControlAsync(vendorId, cancellationToken)
                        };
                    }
                }

                if (IsExternalReferenceConstraintViolation(dbEx))
                {
                    var conflictingReference = await _dbContext.CommerceHubVendorSettlements
                        .AsNoTracking()
                        .Where(entry => entry.ExternalReference == normalizedExternalReference)
                        .Select(entry => new
                        {
                            entry.Id,
                            entry.VendorId,
                            entry.SettlementType,
                            entry.IdempotencyKey
                        })
                        .FirstOrDefaultAsync(cancellationToken);

                    if (conflictingReference != null)
                    {
                        throw new CommerceHubVendorFinancialControlException(
                            "EXTERNAL_REFERENCE_ALREADY_USED",
                            "This vendor financial evidence reference has already been used for another settlement, clearance, or adjustment.",
                            StatusCodes.Status409Conflict);
                    }
                }

                throw;
            }

            var stateAfter = await _financialSafetyGuardService.VerifyStateTransitionAsync(
                vendorId,
                normalizedCurrency,
                stateBefore,
                BuildExpectedDelta(settlementType, vendorDirection, amount),
                correlationId,
                expectedLedgerEntries: 2,
                referenceType,
                settlement.Id,
                cancellationToken);

            _financialSafetyGuardService.AppendCommittedOperationAudit(
                vendorId,
                actorId,
                ResolveActorType(actorId),
                settlementType.ToString(),
                amount,
                normalizedCurrency,
                stateBefore,
                stateAfter,
                correlationId,
                normalizedIdempotencyKey,
                referenceType,
                settlement.Id,
                normalizedNotes);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException dbEx)
            {
                if (IsIdempotencyConstraintViolation(dbEx))
                {
                    var reloadedSettlement = await _dbContext.CommerceHubVendorSettlements
                        .AsNoTracking()
                        .FirstOrDefaultAsync(entry =>
                            entry.VendorId == vendorId &&
                            entry.IdempotencyKey == normalizedIdempotencyKey,
                            cancellationToken);

                    if (reloadedSettlement != null)
                    {
                        EnsureSettlementReplayMatchesOriginal(
                            reloadedSettlement,
                            settlementType,
                            vendorDirection,
                            amount,
                            normalizedCurrency,
                            normalizedExternalReference);

                        // Roll back transaction and remove only this operation's pending entities.
                        if (transaction != null)
                        {
                            await transaction.RollbackAsync(cancellationToken);
                        }
                        CleanupSettlementReplayEntries(ownsTransaction, settlement.Id, referenceType, correlationId);

                        return new CommerceHubVendorFinancialControlActionResultDto
                        {
                            Settlement = MapSettlement(reloadedSettlement),
                            Control = await GetVendorControlAsync(vendorId, cancellationToken)
                        };
                    }
                }

                if (IsExternalReferenceConstraintViolation(dbEx))
                {
                    var conflictingReference = await _dbContext.CommerceHubVendorSettlements
                        .AsNoTracking()
                        .Where(entry => entry.ExternalReference == normalizedExternalReference)
                        .Select(entry => new
                        {
                            entry.Id,
                            entry.VendorId,
                            entry.SettlementType,
                            entry.IdempotencyKey
                        })
                        .FirstOrDefaultAsync(cancellationToken);

                    if (conflictingReference != null)
                    {
                        throw new CommerceHubVendorFinancialControlException(
                            "EXTERNAL_REFERENCE_ALREADY_USED",
                            "This vendor financial evidence reference has already been used for another settlement, clearance, or adjustment.",
                            StatusCodes.Status409Conflict);
                    }
                }

                throw;
            }

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            if (CommerceHubVendorSettlementStoreCompatibility.IsMissingSettlementTable(ex))
            {
                throw CommerceHubVendorSettlementStoreCompatibility.CreateSchemaMissingException();
            }

            throw;
        }

        return new CommerceHubVendorFinancialControlActionResultDto
        {
            Settlement = MapSettlement(settlement),
            Control = await GetVendorControlAsync(vendorId, cancellationToken)
        };
    }

    private void CleanupSettlementReplayEntries(
        bool ownsTransaction,
        Guid settlementId,
        CommerceHubFinancialReferenceType referenceType,
        string correlationId)
    {
        if (ownsTransaction)
        {
            _dbContext.ChangeTracker.Clear();
            return;
        }

        foreach (var entry in _dbContext.ChangeTracker
            .Entries<CommerceHubVendorSettlement>()
            .Where(entry => entry.Entity.Id == settlementId)
            .ToList())
        {
            entry.State = EntityState.Detached;
        }

        var settlementLedgerTransactionIds = _dbContext.ChangeTracker
            .Entries<CommerceHubFinancialTransaction>()
            .Where(entry =>
                (entry.Entity.ReferenceType == referenceType && entry.Entity.ReferenceId == settlementId) ||
                string.Equals(entry.Entity.CorrelationId, correlationId, StringComparison.Ordinal))
            .Select(entry => entry.Entity.Id)
            .ToHashSet();

        foreach (var entry in _dbContext.ChangeTracker
            .Entries<CommerceHubFinancialTransaction>()
            .Where(entry => settlementLedgerTransactionIds.Contains(entry.Entity.Id))
            .ToList())
        {
            entry.State = EntityState.Detached;
        }

        foreach (var entry in _dbContext.ChangeTracker
            .Entries<AccountingOutboxMessage>()
            .Where(entry =>
                string.Equals(entry.Entity.EntityTypeName, nameof(CommerceHubFinancialTransaction), StringComparison.Ordinal) &&
                settlementLedgerTransactionIds.Contains(entry.Entity.BusinessEntityId))
            .ToList())
        {
            entry.State = EntityState.Detached;
        }

        foreach (var entry in _dbContext.ChangeTracker
            .Entries<CommerceHubFinancialOperationAudit>()
            .Where(entry =>
                (entry.Entity.ReferenceType == referenceType && entry.Entity.ReferenceId == settlementId) ||
                string.Equals(entry.Entity.CorrelationId, correlationId, StringComparison.Ordinal))
            .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private async Task<VendorLookup> LoadVendorAsync(Guid vendorId, CancellationToken cancellationToken)
    {
        if (vendorId == Guid.Empty)
        {
            throw new CommerceHubVendorFinancialControlException(
                "VENDOR_REQUIRED",
                "VendorId is required.",
                StatusCodes.Status400BadRequest);
        }

        var vendor = await _dbContext.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.Id == vendorId && tenant.DeletedAtUtc == null)
            .Select(tenant => new VendorLookup(
                tenant.Id,
                string.IsNullOrWhiteSpace(tenant.Name) ? tenant.Id.ToString() : tenant.Name,
                FinancialCurrencyGuard.PlatformBaseCurrencyCode))
            .SingleOrDefaultAsync(cancellationToken);

        if (vendor == null)
        {
            throw new CommerceHubVendorFinancialControlException(
                "VENDOR_NOT_FOUND",
                "Vendor was not found.",
                StatusCodes.Status404NotFound);
        }

        return vendor;
    }

    private static CommerceHubVendorSettlementDto MapSettlement(CommerceHubVendorSettlement settlement)
    {
        return new CommerceHubVendorSettlementDto
        {
            SettlementId = settlement.Id,
            VendorId = settlement.VendorId,
            SettlementType = settlement.SettlementType.ToString(),
            Direction = settlement.Direction.ToString(),
            Amount = settlement.Amount,
            SignedAmount = settlement.Direction == CommerceHubFinancialTransactionDirection.Credit
                ? settlement.Amount
                : -settlement.Amount,
            Currency = settlement.Currency,
            IdempotencyKey = settlement.IdempotencyKey,
            ExternalReference = settlement.ExternalReference,
            Notes = settlement.Notes,
            RecordedByUserId = settlement.RecordedByUserId,
            RecordedByUserEmail = settlement.RecordedByUserEmail,
            CreatedAtUtc = settlement.CreatedAtUtc
        };
    }

    private static string BuildVendorDescription(
        string vendorName,
        CommerceHubVendorSettlementType settlementType,
        Guid settlementId)
    {
        return settlementType switch
        {
            CommerceHubVendorSettlementType.SettlementPayment =>
                $"Vendor settlement payment recorded for {vendorName}. Settlement {settlementId}.",
            CommerceHubVendorSettlementType.AdminAdjustment =>
                $"Admin balance adjustment recorded for vendor {vendorName}. Adjustment {settlementId}.",
            CommerceHubVendorSettlementType.DebtClearance =>
                $"Vendor debt clearance recorded for {vendorName}. Clearance {settlementId}.",
            _ => $"Vendor financial control entry recorded for {vendorName}. Reference {settlementId}."
        };
    }

    private static string BuildPlatformDescription(
        string vendorName,
        CommerceHubVendorSettlementType settlementType,
        Guid settlementId)
    {
        return settlementType switch
        {
            CommerceHubVendorSettlementType.SettlementPayment =>
                $"Platform received settlement payment from vendor {vendorName}. Settlement {settlementId}.",
            CommerceHubVendorSettlementType.AdminAdjustment =>
                $"Platform offset recorded for vendor balance adjustment on {vendorName}. Adjustment {settlementId}.",
            CommerceHubVendorSettlementType.DebtClearance =>
                $"Platform debt clearance recorded for vendor {vendorName}. Clearance {settlementId}.",
            _ => $"Platform financial control entry recorded for vendor {vendorName}. Reference {settlementId}."
        };
    }

    private static CommerceHubFinancialStateDelta BuildExpectedDelta(
        CommerceHubVendorSettlementType settlementType,
        CommerceHubFinancialTransactionDirection vendorDirection,
        decimal amount)
    {
        return settlementType switch
        {
            CommerceHubVendorSettlementType.SettlementPayment => new CommerceHubFinancialStateDelta(
                PlatformCashDelta: amount,
                VendorPayableDelta: 0m,
                VendorDebtDelta: -amount),
            CommerceHubVendorSettlementType.DebtClearance => new CommerceHubFinancialStateDelta(
                PlatformCashDelta: 0m,
                VendorPayableDelta: 0m,
                VendorDebtDelta: -amount),
            CommerceHubVendorSettlementType.AdminAdjustment when vendorDirection == CommerceHubFinancialTransactionDirection.Debit =>
                new CommerceHubFinancialStateDelta(
                    PlatformCashDelta: 0m,
                    VendorPayableDelta: 0m,
                    VendorDebtDelta: amount),
            CommerceHubVendorSettlementType.AdminAdjustment => new CommerceHubFinancialStateDelta(
                PlatformCashDelta: 0m,
                VendorPayableDelta: amount,
                VendorDebtDelta: 0m),
            _ => new CommerceHubFinancialStateDelta(0m, 0m, 0m)
        };
    }

    private static (CommerceHubFinancialTransactionType VendorTransactionType, CommerceHubFinancialBucket VendorBucket, CommerceHubFinancialTransactionType PlatformTransactionType, CommerceHubFinancialBucket PlatformBucket) ResolveMirroredPosting(
        CommerceHubVendorSettlementType settlementType,
        CommerceHubFinancialTransactionDirection vendorDirection)
    {
        return settlementType switch
        {
            CommerceHubVendorSettlementType.SettlementPayment => (
                CommerceHubFinancialTransactionType.Settlement,
                CommerceHubFinancialBucket.Receivable,
                CommerceHubFinancialTransactionType.Settlement,
                CommerceHubFinancialBucket.Cash),
            CommerceHubVendorSettlementType.DebtClearance => (
                CommerceHubFinancialTransactionType.Adjustment,
                CommerceHubFinancialBucket.Receivable,
                CommerceHubFinancialTransactionType.Adjustment,
                CommerceHubFinancialBucket.Receivable),
            CommerceHubVendorSettlementType.AdminAdjustment when vendorDirection == CommerceHubFinancialTransactionDirection.Debit => (
                CommerceHubFinancialTransactionType.Adjustment,
                CommerceHubFinancialBucket.Receivable,
                CommerceHubFinancialTransactionType.Adjustment,
                CommerceHubFinancialBucket.Receivable),
            CommerceHubVendorSettlementType.AdminAdjustment => (
                CommerceHubFinancialTransactionType.Adjustment,
                CommerceHubFinancialBucket.Payable,
                CommerceHubFinancialTransactionType.Adjustment,
                CommerceHubFinancialBucket.Payable),
            _ => throw new ArgumentOutOfRangeException(nameof(settlementType), "Settlement type is invalid.")
        };
    }

    private static string ResolveActorType(Guid? actorId) =>
        actorId.HasValue && actorId.Value != Guid.Empty ? "SUPER_ADMIN" : "SYSTEM";

    private static string BuildCorrelationId(CommerceHubFinancialReferenceType referenceType, Guid referenceId) =>
        $"{referenceType}:{referenceId:N}";

    private static string NormalizeCurrency(string? currency)
    {
        try
        {
            return FinancialCurrencyGuard.RequireEgpCurrency(
                string.IsNullOrWhiteSpace(currency) ? DefaultCurrency : currency,
                "commercehub.vendor_financial_control.currency",
                "CommerceHubVendorFinancialControlService");
        }
        catch (FinancialCurrencyMismatchException)
        {
            throw new CommerceHubVendorFinancialControlException(
                "CURRENCY_NOT_SUPPORTED",
                "Only EGP is supported for CommerceHub financial control operations.",
                StatusCodes.Status400BadRequest);
        }
        catch (FinancialCurrencyInvalidException)
        {
            throw new CommerceHubVendorFinancialControlException(
                "CURRENCY_INVALID",
                "Currency is invalid.",
                StatusCodes.Status400BadRequest);
        }
    }

    private static string NormalizeNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            throw new CommerceHubVendorFinancialControlException(
                "NOTES_REQUIRED",
                "Notes are required.",
                StatusCodes.Status400BadRequest);
        }

        return notes.Trim();
    }

    private static string NormalizeRequiredOperationReference(
        string? value,
        string code,
        string message,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CommerceHubVendorFinancialControlException(
                code,
                message,
                StatusCodes.Status400BadRequest);
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new CommerceHubVendorFinancialControlException(
                $"{code}_TOO_LONG",
                $"The reference must not exceed {maxLength} characters.",
                StatusCodes.Status400BadRequest);
        }

        return normalized;
    }

    private static void EnsureSettlementReplayMatchesOriginal(
        CommerceHubVendorSettlement existing,
        CommerceHubVendorSettlementType settlementType,
        CommerceHubFinancialTransactionDirection direction,
        decimal amount,
        string currency,
        string externalReference)
    {
        var normalizedAmount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (existing.SettlementType == settlementType &&
            existing.Direction == direction &&
            existing.Amount == normalizedAmount &&
            existing.Currency == currency &&
            existing.ExternalReference == externalReference)
        {
            return;
        }

        throw new CommerceHubVendorFinancialControlException(
            "IDEMPOTENCY_KEY_CONFLICT",
            "This idempotency key was already used for a different vendor financial control operation.",
            StatusCodes.Status409Conflict);
    }

    private sealed record VendorLookup(Guid Id, string Name, string? Currency);

    private static bool IsIdempotencyConstraintViolation(DbUpdateException dbEx)
    {
        var message = dbEx.InnerException?.Message ?? dbEx.Message;
        return message.Contains("UX_CommerceHubVendorSettlements_Vendor_IdempotencyKey", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("VendorId", StringComparison.OrdinalIgnoreCase) && message.Contains("IdempotencyKey", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExternalReferenceConstraintViolation(DbUpdateException dbEx)
    {
        var message = dbEx.InnerException?.Message ?? dbEx.Message;
        return message.Contains("UX_CommerceHubVendorSettlements_ExternalReference", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("ExternalReference", StringComparison.OrdinalIgnoreCase) && message.Contains("unique", StringComparison.OrdinalIgnoreCase);
    }
}
