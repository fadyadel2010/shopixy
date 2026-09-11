using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.API.Services.CommerceHub;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;
using MassTransitOutboxMessage = MassTransit.EntityFrameworkCoreIntegration.OutboxMessage;

namespace PointOfSale.API.Services.CommerceHub.Finance;

public interface ICommerceHubFinancialReadinessGate
{
    Task EnsureOutwardMoneyMovementAllowedAsync(
        Guid tenantId,
        string operationName,
        CancellationToken cancellationToken = default);

    Task EnsureCommerceIntakeAllowedAsync(
        IReadOnlyCollection<Guid> tenantIds,
        string operationName,
        CancellationToken cancellationToken = default);
}

public sealed class CommerceHubFinancialReadinessGate : ICommerceHubFinancialReadinessGate
{
    private static readonly TimeSpan AccountingOutboxMaxLag = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan PaymentCompensationMaxLag = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan IntegrationBackboneMaxLag = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ExternalRefundProcessingMaxLag = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ReconciliationMaxAge = TimeSpan.FromHours(2);
    private const int TenantScopeInspectionWarningThreshold = 500;
    private static readonly string[] BlockingUnresolvedPaymentStatuses =
    [
        "PendingResolution",
        "PendingTenantResolution",
        "RoutedToSuspense",
        "SuspenseMatchedPaymentPendingClearance"
    ];

    private static readonly string[] BlockingUnresolvedRefundStatuses =
    [
        "ProviderRefundPendingReconciliation"
    ];

    private readonly ApplicationDbContext _dbContext;
    private readonly IOptionsMonitor<CommerceHubEmergencyStopOptions> _emergencyStops;
    private readonly ILogger<CommerceHubFinancialReadinessGate> _logger;

    public CommerceHubFinancialReadinessGate(
        ApplicationDbContext dbContext,
        IOptionsMonitor<CommerceHubEmergencyStopOptions> emergencyStops,
        ILogger<CommerceHubFinancialReadinessGate> logger)
    {
        _dbContext = dbContext;
        _emergencyStops = emergencyStops;
        _logger = logger;
    }

    public async Task EnsureOutwardMoneyMovementAllowedAsync(
        Guid tenantId,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required for financial readiness checks.", nameof(tenantId));
        }

        EnsureEmergencyOutwardMoneyMovementAllowed(tenantId, operationName);

        var nowUtc = DateTime.UtcNow;
        var staleCutoffUtc = nowUtc.Subtract(IntegrationBackboneMaxLag);
        await EnsureUnscopedUnresolvedExternalMoneyHealthyAsync(operationName, cancellationToken);
        await EnsureAmbiguousMarketplaceAggregateBackboneHealthyAsync(
            tenantId,
            operationName,
            staleCutoffUtc,
            cancellationToken);
        await EnsureAccountingOutboxHealthyAsync(tenantId, operationName, nowUtc, cancellationToken);
        await EnsurePaymentCompensationHealthyAsync(tenantId, operationName, nowUtc, cancellationToken);
        await EnsureUnresolvedCapturedPaymentsHealthyAsync(tenantId, operationName, cancellationToken);
        await EnsureUnresolvedRefundsHealthyAsync(tenantId, operationName, cancellationToken);
        await EnsureExternalRefundsReconciledAsync(tenantId, operationName, nowUtc, cancellationToken);
        await EnsureProcessingPayoutsReconciledAsync(tenantId, operationName, nowUtc, cancellationToken);
        await EnsureCommerceHubLedgerAccountingCoverageAsync(tenantId, operationName, cancellationToken);
        await EnsureCommerceHubReconciliationHealthyAsync(tenantId, operationName, nowUtc, allowFreshStartupBypass: false, cancellationToken);
        await EnsureIntegrationBackboneHealthyAsync(tenantId, operationName, nowUtc, cancellationToken);
    }

    public async Task EnsureCommerceIntakeAllowedAsync(
        IReadOnlyCollection<Guid> tenantIds,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantIds);

        var distinctTenantIds = tenantIds
            .Where(tenantId => tenantId != Guid.Empty)
            .Distinct()
            .ToList();

        if (distinctTenantIds.Count == 0)
        {
            throw new ArgumentException("At least one tenant id is required for CommerceHub intake readiness checks.", nameof(tenantIds));
        }

        EnsureEmergencyCommerceIntakeAllowed(distinctTenantIds, operationName);

        var nowUtc = DateTime.UtcNow;
        var staleCutoffUtc = nowUtc.Subtract(IntegrationBackboneMaxLag);

        // PERFORMANCE FIX: Run global backbone health check once before tenant loop to avoid redundant scans
        await EnsureUnscopedUnresolvedExternalMoneyHealthyAsync(operationName, cancellationToken);
        await EnsureAmbiguousMarketplaceAggregateBackboneHealthyAsync(
            Guid.Empty, // tenantId only used for logging, not filtering
            operationName,
            staleCutoffUtc,
            cancellationToken);

        foreach (var tenantId in distinctTenantIds)
        {
            await EnsureAccountingOutboxHealthyAsync(tenantId, operationName, nowUtc, cancellationToken);
            await EnsurePaymentCompensationHealthyAsync(tenantId, operationName, nowUtc, cancellationToken);
            await EnsureUnresolvedCapturedPaymentsHealthyAsync(tenantId, operationName, cancellationToken);
            await EnsureUnresolvedRefundsHealthyAsync(tenantId, operationName, cancellationToken);
            await EnsureExternalRefundsReconciledAsync(tenantId, operationName, nowUtc, cancellationToken);
            await EnsureProcessingPayoutsReconciledAsync(tenantId, operationName, nowUtc, cancellationToken);
            await EnsureCommerceHubLedgerAccountingCoverageAsync(tenantId, operationName, cancellationToken);
            await EnsureCommerceHubReconciliationHealthyAsync(tenantId, operationName, nowUtc, allowFreshStartupBypass: true, cancellationToken);
            await EnsureIntegrationBackboneHealthyAsync(tenantId, operationName, nowUtc, cancellationToken);
        }
    }

    private async Task EnsureAccountingOutboxHealthyAsync(
        Guid tenantId,
        string operationName,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var staleCutoffUtc = nowUtc.Subtract(AccountingOutboxMaxLag);
        var blocker = await _dbContext.Set<AccountingOutboxMessage>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.TenantId == tenantId &&
                message.ProcessedAtUtc == null &&
                AccountingOutboxProcessor.CriticalFinancialTruthEventTypes.Contains(message.EventType) &&
                (message.IsFailed || message.OccurredAtUtc < staleCutoffUtc))
            .OrderByDescending(message => message.IsFailed)
            .ThenBy(message => message.OccurredAtUtc)
            .Select(message => new
            {
                message.Id,
                message.EventType,
                message.BusinessEntityId,
                message.IsFailed,
                message.OccurredAtUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (blocker == null)
        {
            return;
        }

        _logger.LogError(
            "Blocking CommerceHub outward money movement because accounting truth is not ready | Operation: {Operation} | TenantId: {TenantId} | MessageId: {MessageId} | EventType: {EventType} | EntityId: {EntityId} | IsFailed: {IsFailed} | OccurredAtUtc: {OccurredAtUtc}",
            operationName,
            tenantId,
            blocker.Id,
            blocker.EventType,
            blocker.BusinessEntityId,
            blocker.IsFailed,
            blocker.OccurredAtUtc);

        throw new CommerceHubCheckoutException(
            "FINANCIAL_AUTHORITY_NOT_READY",
            "Outward money movement is blocked until critical accounting postings are processed or repaired.",
            StatusCodes.Status409Conflict);
    }

    private async Task EnsurePaymentCompensationHealthyAsync(
        Guid tenantId,
        string operationName,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var staleCutoffUtc = nowUtc.Subtract(PaymentCompensationMaxLag);
        var tenantIdText = tenantId.ToString();
        var inspectedMessages = 0;

        await foreach (var message in _dbContext.CommerceHubPaymentCompensationOutboxMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.ProcessedAtUtc == null &&
                !message.IsDeleted &&
                (message.IsFailed ||
                 message.AttemptCount >= 3 ||
                 message.CreatedAt < staleCutoffUtc) &&
                (message.TenantId == tenantId ||
                 message.AffectedStoreIdsJson.Contains(tenantIdText)))
            .OrderBy(message => message.CreatedAt)
            .Select(message => new
            {
                message.Id,
                message.PaymentId,
                message.OrderId,
                message.TenantId,
                message.AffectedStoreIdsJson,
                message.AttemptCount,
                message.LastError,
                message.CreatedAt
            })
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken))
        {
            inspectedMessages++;
            if (!CompensationMayAffectTenant(message.TenantId, message.AffectedStoreIdsJson, tenantId))
            {
                continue;
            }

            _logger.LogError(
                "Blocking CommerceHub outward money movement because captured payment compensation is unresolved | Operation: {Operation} | TenantId: {TenantId} | CompensationMessageId: {MessageId} | PaymentId: {PaymentId} | OrderId: {OrderId} | AttemptCount: {AttemptCount} | CreatedAtUtc: {CreatedAtUtc} | LastError: {LastError}",
                operationName,
                tenantId,
                message.Id,
                message.PaymentId,
                message.OrderId,
                message.AttemptCount,
                message.CreatedAt,
                message.LastError);

            throw new CommerceHubCheckoutException(
                "PAYMENT_COMPENSATION_NOT_READY",
                "Outward money movement is blocked until captured payment refund compensation is processed or reconciled.",
                StatusCodes.Status409Conflict);
        }

        LogLargeTenantScopeInspection(
            "payment compensation outbox",
            operationName,
            tenantId,
            inspectedMessages,
            staleCutoffUtc);
    }

    private async Task EnsureUnscopedUnresolvedExternalMoneyHealthyAsync(
        string operationName,
        CancellationToken cancellationToken)
    {
        await EnsureUnscopedPaymentCompensationHealthyAsync(operationName, cancellationToken);

        await EnsureUnscopedUnresolvedWebhookHealthyAsync(
            BlockingUnresolvedPaymentStatuses,
            operationName,
            "captured provider cash",
            "UNRESOLVED_CAPTURED_PAYMENT_NOT_RECONCILED",
            "CommerceHub money movement is blocked until unresolved provider cash is assigned durable tenant scope and reconciled.",
            cancellationToken);

        await EnsureUnscopedUnresolvedWebhookHealthyAsync(
            BlockingUnresolvedRefundStatuses,
            operationName,
            "unmatched provider refund",
            "UNMATCHED_PROVIDER_REFUND",
            "CommerceHub money movement is blocked until unresolved provider refunds are assigned durable tenant scope and reconciled with a known payment or manual resolution.",
            cancellationToken);
    }

    private async Task EnsureUnscopedPaymentCompensationHealthyAsync(
        string operationName,
        CancellationToken cancellationToken)
    {
        await foreach (var message in _dbContext.CommerceHubPaymentCompensationOutboxMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.ProcessedAtUtc == null &&
                !message.IsDeleted &&
                (message.TenantId == null || message.TenantId == Guid.Empty))
            .OrderBy(message => message.CreatedAt)
            .Select(message => new
            {
                message.Id,
                message.PaymentId,
                message.OrderId,
                message.TenantId,
                message.AffectedStoreIdsJson,
                message.Provider,
                message.ProviderIntentId,
                message.ProviderTransactionId,
                message.Amount,
                message.Currency,
                message.AttemptCount,
                message.LastError,
                message.CreatedAt
            })
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken))
        {
            if (HasExplicitTenantScope(message.AffectedStoreIdsJson))
            {
                continue;
            }

            _logger.LogError(
                "Blocking CommerceHub money movement because captured-payment compensation has no durable tenant scope | Operation: {Operation} | CompensationMessageId: {MessageId} | PaymentId: {PaymentId} | OrderId: {OrderId} | Provider: {Provider} | ProviderIntentId: {ProviderIntentId} | ProviderTransactionId: {ProviderTransactionId} | Amount: {Amount} {Currency} | AttemptCount: {AttemptCount} | CreatedAtUtc: {CreatedAtUtc} | LastError: {LastError}",
                operationName,
                message.Id,
                message.PaymentId,
                message.OrderId,
                message.Provider,
                message.ProviderIntentId,
                message.ProviderTransactionId,
                message.Amount,
                message.Currency,
                message.AttemptCount,
                message.CreatedAt,
                message.LastError);

            throw new CommerceHubCheckoutException(
                "PAYMENT_COMPENSATION_SCOPE_UNDETERMINED",
                "CommerceHub money movement is blocked until captured-payment compensation is assigned durable tenant scope or reconciled.",
                StatusCodes.Status409Conflict);
        }
    }

    private async Task EnsureUnscopedUnresolvedWebhookHealthyAsync(
        IReadOnlyCollection<string> blockingStatuses,
        string operationName,
        string unresolvedAuthorityDescription,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        await foreach (var webhook in _dbContext.CommerceHubUnresolvedPaymentWebhooks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(webhook =>
                !webhook.IsDeleted &&
                webhook.Amount > 0m &&
                blockingStatuses.Contains(webhook.Status) &&
                (webhook.TenantId == null || webhook.TenantId == Guid.Empty))
            .OrderBy(webhook => webhook.CreatedAt)
            .Select(webhook => new
            {
                webhook.Id,
                webhook.Provider,
                webhook.ProviderEventId,
                webhook.TenantId,
                webhook.OrderId,
                webhook.AffectedStoreIdsJson,
                webhook.SuspenseRoutedStoreIdsJson,
                webhook.Amount,
                webhook.Currency,
                webhook.Status,
                webhook.CreatedAt,
                webhook.LastError
            })
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken))
        {
            if (HasDurableTenantScope(
                    webhook.TenantId,
                    webhook.AffectedStoreIdsJson,
                    webhook.SuspenseRoutedStoreIdsJson))
            {
                continue;
            }

            _logger.LogError(
                "Blocking CommerceHub money movement because unresolved external-money authority has no durable tenant scope | Operation: {Operation} | Authority: {Authority} | UnresolvedWebhookId: {WebhookId} | Provider: {Provider} | ProviderEventId: {ProviderEventId} | OrderId: {OrderId} | Status: {Status} | Amount: {Amount} {Currency} | CreatedAtUtc: {CreatedAtUtc} | LastError: {LastError}",
                operationName,
                unresolvedAuthorityDescription,
                webhook.Id,
                webhook.Provider,
                webhook.ProviderEventId,
                webhook.OrderId,
                webhook.Status,
                webhook.Amount,
                webhook.Currency,
                webhook.CreatedAt,
                webhook.LastError);

            throw new CommerceHubCheckoutException(
                errorCode,
                errorMessage,
                StatusCodes.Status409Conflict);
        }
    }

    private async Task EnsureUnresolvedCapturedPaymentsHealthyAsync(
        Guid tenantId,
        string operationName,
        CancellationToken cancellationToken)
    {
        var tenantIdText = tenantId.ToString();
        var inspectedWebhooks = 0;

        await foreach (var webhook in _dbContext.CommerceHubUnresolvedPaymentWebhooks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(webhook =>
                !webhook.IsDeleted &&
                webhook.Amount > 0m &&
                BlockingUnresolvedPaymentStatuses.Contains(webhook.Status) &&
                (webhook.TenantId == tenantId ||
                 webhook.AffectedStoreIdsJson.Contains(tenantIdText) ||
                 webhook.SuspenseRoutedStoreIdsJson.Contains(tenantIdText)))
            .OrderBy(webhook => webhook.CreatedAt)
            .Select(webhook => new
            {
                webhook.Id,
                webhook.Provider,
                webhook.ProviderEventId,
                webhook.TenantId,
                webhook.OrderId,
                webhook.AffectedStoreIdsJson,
                webhook.SuspenseRoutedStoreIdsJson,
                webhook.Amount,
                webhook.Currency,
                webhook.Status,
                webhook.CreatedAt,
                webhook.LastError
            })
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken))
        {
            inspectedWebhooks++;
            if (!UnresolvedCapturedPaymentMayAffectTenant(
                    webhook.TenantId,
                    webhook.AffectedStoreIdsJson,
                    webhook.SuspenseRoutedStoreIdsJson,
                    tenantId))
            {
                continue;
            }

            _logger.LogError(
                "Blocking CommerceHub outward money movement because captured provider cash is unresolved | Operation: {Operation} | TenantId: {TenantId} | UnresolvedWebhookId: {WebhookId} | Provider: {Provider} | ProviderEventId: {ProviderEventId} | OrderId: {OrderId} | Status: {Status} | Amount: {Amount} {Currency} | CreatedAtUtc: {CreatedAtUtc} | LastError: {LastError}",
                operationName,
                tenantId,
                webhook.Id,
                webhook.Provider,
                webhook.ProviderEventId,
                webhook.OrderId,
                webhook.Status,
                webhook.Amount,
                webhook.Currency,
                webhook.CreatedAt,
                webhook.LastError);

            throw new CommerceHubCheckoutException(
                "UNRESOLVED_CAPTURED_PAYMENT_NOT_RECONCILED",
                "Outward money movement is blocked until captured provider payments are linked to an order or manually reconciled after any tenant suspense posting.",
                StatusCodes.Status409Conflict);
        }

        LogLargeTenantScopeInspection(
            "unresolved captured payment webhooks",
            operationName,
            tenantId,
            inspectedWebhooks,
            null);
    }

    private async Task EnsureUnresolvedRefundsHealthyAsync(
        Guid tenantId,
        string operationName,
        CancellationToken cancellationToken)
    {
        var tenantIdText = tenantId.ToString();
        var inspectedWebhooks = 0;

        await foreach (var webhook in _dbContext.CommerceHubUnresolvedPaymentWebhooks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(webhook =>
                !webhook.IsDeleted &&
                webhook.Amount > 0m &&
                BlockingUnresolvedRefundStatuses.Contains(webhook.Status) &&
                (webhook.TenantId == tenantId ||
                 webhook.AffectedStoreIdsJson.Contains(tenantIdText) ||
                 webhook.SuspenseRoutedStoreIdsJson.Contains(tenantIdText)))
            .OrderBy(webhook => webhook.CreatedAt)
            .Select(webhook => new
            {
                webhook.Id,
                webhook.Provider,
                webhook.ProviderEventId,
                webhook.TenantId,
                webhook.OrderId,
                webhook.AffectedStoreIdsJson,
                webhook.SuspenseRoutedStoreIdsJson,
                webhook.Amount,
                webhook.Currency,
                webhook.Status,
                webhook.CreatedAt,
                webhook.LastError
            })
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken))
        {
            inspectedWebhooks++;
            if (!UnresolvedCapturedPaymentMayAffectTenant(
                    webhook.TenantId,
                    webhook.AffectedStoreIdsJson,
                    webhook.SuspenseRoutedStoreIdsJson,
                    tenantId))
            {
                continue;
            }

            _logger.LogError(
                "Blocking CommerceHub outward money movement because unmatched provider refund is unresolved | Operation: {Operation} | TenantId: {TenantId} | UnresolvedWebhookId: {WebhookId} | Provider: {Provider} | ProviderEventId: {ProviderEventId} | OrderId: {OrderId} | Status: {Status} | Amount: {Amount} {Currency} | CreatedAtUtc: {CreatedAtUtc} | LastError: {LastError}",
                operationName,
                tenantId,
                webhook.Id,
                webhook.Provider,
                webhook.ProviderEventId,
                webhook.OrderId,
                webhook.Status,
                webhook.Amount,
                webhook.Currency,
                webhook.CreatedAt,
                webhook.LastError);

            throw new CommerceHubCheckoutException(
                "UNMATCHED_PROVIDER_REFUND",
                "Outward money movement is blocked until unmatched provider refunds are reconciled with a known payment or manually resolved.",
                StatusCodes.Status409Conflict);
        }

        LogLargeTenantScopeInspection(
            "unresolved refund webhooks",
            operationName,
            tenantId,
            inspectedWebhooks,
            null);
    }

    private async Task EnsureProcessingPayoutsReconciledAsync(
        Guid tenantId,
        string operationName,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var staleCutoffUtc = nowUtc.Subtract(CommerceHubFinancialReadinessThresholds.ProcessingPayoutMaxLag);

        var reconciliationRequiredPayout = await _dbContext.CommerceHubPayouts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(payout =>
                payout.TenantId == tenantId &&
                !payout.IsDeleted &&
                payout.Status == CommerceHubPayoutStatus.ReconciliationRequired)
            .OrderBy(payout => payout.ProcessedAtUtc ?? payout.CreatedAt)
            .Select(payout => new
            {
                payout.Id,
                payout.Amount,
                payout.Currency,
                payout.ProcessedAtUtc,
                payout.CreatedAt,
                payout.Reference,
                payout.ExternalReferenceId,
                payout.BankTransactionId,
                payout.ExternalTransferIdempotencyKey,
                payout.FailureReason
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (reconciliationRequiredPayout != null)
        {
            _logger.LogError(
                "Blocking CommerceHub outward money movement because a payout is quarantined for external transfer reconciliation | Operation: {Operation} | TenantId: {TenantId} | PayoutId: {PayoutId} | Amount: {Amount} {Currency} | ProcessedAtUtc: {ProcessedAtUtc} | CreatedAtUtc: {CreatedAtUtc} | Reference: {Reference} | ExternalReferenceId: {ExternalReferenceId} | BankTransactionId: {BankTransactionId} | ExternalTransferIdempotencyKey: {ExternalTransferIdempotencyKey} | FailureReason: {FailureReason}",
                operationName,
                tenantId,
                reconciliationRequiredPayout.Id,
                reconciliationRequiredPayout.Amount,
                reconciliationRequiredPayout.Currency,
                reconciliationRequiredPayout.ProcessedAtUtc,
                reconciliationRequiredPayout.CreatedAt,
                reconciliationRequiredPayout.Reference,
                reconciliationRequiredPayout.ExternalReferenceId,
                reconciliationRequiredPayout.BankTransactionId,
                reconciliationRequiredPayout.ExternalTransferIdempotencyKey,
                reconciliationRequiredPayout.FailureReason);

            throw new CommerceHubCheckoutException(
                "PAYOUT_EXTERNAL_TRANSFER_RECONCILIATION_REQUIRED",
                "Outward money movement is blocked until payouts quarantined for external-transfer reconciliation are explicitly completed with execution evidence or failed with confirmed non-execution evidence.",
                StatusCodes.Status409Conflict);
        }

        var staleProcessingPayout = await _dbContext.CommerceHubPayouts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(payout =>
                payout.TenantId == tenantId &&
                !payout.IsDeleted &&
                payout.Status == CommerceHubPayoutStatus.Processing &&
                (payout.ProcessedAtUtc ?? payout.CreatedAt) < staleCutoffUtc)
            .OrderBy(payout => payout.ProcessedAtUtc ?? payout.CreatedAt)
            .Select(payout => new
            {
                payout.Id,
                payout.Amount,
                payout.Currency,
                payout.ProcessedAtUtc,
                payout.CreatedAt,
                payout.Reference,
                payout.ExternalReferenceId,
                payout.BankTransactionId,
                payout.ExternalTransferIdempotencyKey
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (staleProcessingPayout == null)
        {
            return;
        }

        _logger.LogError(
            "Blocking CommerceHub outward money movement because a payout external transfer is stuck in Processing and needs reconciliation | Operation: {Operation} | TenantId: {TenantId} | PayoutId: {PayoutId} | Amount: {Amount} {Currency} | ProcessedAtUtc: {ProcessedAtUtc} | CreatedAtUtc: {CreatedAtUtc} | Reference: {Reference} | ExternalReferenceId: {ExternalReferenceId} | BankTransactionId: {BankTransactionId} | ExternalTransferIdempotencyKey: {ExternalTransferIdempotencyKey}",
            operationName,
            tenantId,
            staleProcessingPayout.Id,
            staleProcessingPayout.Amount,
            staleProcessingPayout.Currency,
            staleProcessingPayout.ProcessedAtUtc,
            staleProcessingPayout.CreatedAt,
            staleProcessingPayout.Reference,
            staleProcessingPayout.ExternalReferenceId,
            staleProcessingPayout.BankTransactionId,
            staleProcessingPayout.ExternalTransferIdempotencyKey);

        throw new CommerceHubCheckoutException(
            "PAYOUT_EXTERNAL_TRANSFER_RECONCILIATION_REQUIRED",
            "Outward money movement is blocked until stale processing payouts are quarantined for reconciliation and then completed with external evidence or failed with confirmed non-execution evidence.",
            StatusCodes.Status409Conflict);
    }

    private async Task EnsureExternalRefundsReconciledAsync(
        Guid tenantId,
        string operationName,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var staleCutoffUtc = nowUtc.Subtract(ExternalRefundProcessingMaxLag);

        var unresolvedRefund = await _dbContext.CommerceHubRefunds
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(refund =>
                refund.TenantId == tenantId &&
                !refund.IsDeleted &&
                (refund.Status == CommerceHubRefundStatus.ReconciliationRequired ||
                 (refund.Status == CommerceHubRefundStatus.Processing &&
                  (refund.GatewayStatus == "execution_unknown" ||
                   refund.GatewayStatus == null ||
                   ((refund.ProcessingStartedAtUtc ?? refund.CreatedAt) < staleCutoffUtc &&
                    (refund.GatewayStatus == "processing" ||
                     refund.GatewayStatus == "pending" ||
                     refund.GatewayStatus == "pending_confirmation"))))))
            .OrderBy(refund => refund.ProcessingStartedAtUtc ?? refund.CreatedAt)
            .Select(refund => new
            {
                refund.Id,
                refund.SubOrderId,
                refund.PaymentId,
                refund.Amount,
                refund.Currency,
                refund.GatewayStatus,
                refund.ProcessingStartedAtUtc,
                refund.CreatedAt,
                refund.FailureReason
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (unresolvedRefund == null)
        {
            return;
        }

        _logger.LogError(
            "Blocking CommerceHub money movement because an external refund outcome is unresolved | Operation: {Operation} | TenantId: {TenantId} | RefundId: {RefundId} | SubOrderId: {SubOrderId} | PaymentId: {PaymentId} | Amount: {Amount} {Currency} | GatewayStatus: {GatewayStatus} | ProcessingStartedAtUtc: {ProcessingStartedAtUtc} | CreatedAtUtc: {CreatedAtUtc} | FailureReason: {FailureReason}",
            operationName,
            tenantId,
            unresolvedRefund.Id,
            unresolvedRefund.SubOrderId,
            unresolvedRefund.PaymentId,
            unresolvedRefund.Amount,
            unresolvedRefund.Currency,
            unresolvedRefund.GatewayStatus,
            unresolvedRefund.ProcessingStartedAtUtc,
            unresolvedRefund.CreatedAt,
            unresolvedRefund.FailureReason);

        throw new CommerceHubCheckoutException(
            "EXTERNAL_REFUND_RECONCILIATION_REQUIRED",
            "CommerceHub money movement is blocked until the external refund is confirmed, failed with non-refund proof, or manually reconciled.",
            StatusCodes.Status409Conflict);
    }

    private async Task EnsureCommerceHubLedgerAccountingCoverageAsync(
        Guid tenantId,
        string operationName,
        CancellationToken cancellationToken)
    {
        var uncoveredTransaction = await _dbContext.Set<CommerceHubFinancialTransaction>()
            .AsNoTracking()
            .Where(transaction =>
                ((transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                  transaction.EntityId == tenantId) ||
                 (transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                  (_dbContext.AccountingOutboxMessages
                       .IgnoreQueryFilters()
                       .Any(message =>
                           message.TenantId == tenantId &&
                           message.BusinessEntityId == transaction.Id &&
                           message.EventType == "CommerceHubLedgerPosting") ||
                   _dbContext.JournalEntries
                       .IgnoreQueryFilters()
                       .Any(journal =>
                           journal.TenantId == tenantId &&
                           journal.SourceType == "CommerceHubFinancialTransaction" &&
                           journal.SourceId == transaction.Id) ||
                   _dbContext.Set<CommerceHubFinancialTransaction>()
                       .AsNoTracking()
                       .Any(vendorTransaction =>
                           vendorTransaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                           vendorTransaction.EntityId == tenantId &&
                           vendorTransaction.ReferenceType == transaction.ReferenceType &&
                           vendorTransaction.ReferenceId == transaction.ReferenceId &&
                           vendorTransaction.Currency == transaction.Currency) ||
                   (transaction.ReferenceType == CommerceHubFinancialReferenceType.Order &&
                    _dbContext.CommerceHubSubOrders
                        .IgnoreQueryFilters()
                        .Any(subOrder =>
                            subOrder.StoreId == tenantId &&
                            subOrder.MarketplaceOrderId == transaction.ReferenceId &&
                            !subOrder.IsDeleted)) ||
                   (transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund &&
                    _dbContext.CommerceHubRefunds
                        .IgnoreQueryFilters()
                        .Any(refund =>
                            refund.TenantId == tenantId &&
                            refund.Id == transaction.ReferenceId &&
                            !refund.IsDeleted)) ||
                   (transaction.ReferenceType == CommerceHubFinancialReferenceType.Payout &&
                    _dbContext.CommerceHubPayouts
                        .IgnoreQueryFilters()
                        .Any(payout =>
                            payout.TenantId == tenantId &&
                            payout.Id == transaction.ReferenceId &&
                            !payout.IsDeleted)) ||
                   (transaction.ReferenceType == CommerceHubFinancialReferenceType.Settlement &&
                    _dbContext.CommerceHubVendorSettlements
                        .IgnoreQueryFilters()
                        .Any(settlement =>
                            settlement.VendorId == tenantId &&
                            settlement.Id == transaction.ReferenceId))))) &&
                !(_dbContext.JournalEntries
                      .IgnoreQueryFilters()
                      .Any(journal =>
                          journal.TenantId == tenantId &&
                          journal.SourceType == "CommerceHubFinancialTransaction" &&
                          journal.SourceId == transaction.Id &&
                          journal.Status == JournalStatus.Posted) ||
                  (transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                   transaction.EntityId == tenantId &&
                   transaction.TransactionType == CommerceHubFinancialTransactionType.Refund &&
                   transaction.Direction == CommerceHubFinancialTransactionDirection.Debit &&
                   transaction.FinancialBucket == CommerceHubFinancialBucket.Payable &&
                   transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund &&
                   _dbContext.Set<CommerceHubFinancialTransaction>()
                       .AsNoTracking()
                       .Any(platformAdjustment =>
                           platformAdjustment.EntityType == CommerceHubFinancialEntityType.Platform &&
                           platformAdjustment.TransactionType == CommerceHubFinancialTransactionType.Adjustment &&
                           platformAdjustment.Direction == CommerceHubFinancialTransactionDirection.Debit &&
                           platformAdjustment.FinancialBucket == CommerceHubFinancialBucket.Payable &&
                           platformAdjustment.ReferenceType == CommerceHubFinancialReferenceType.Refund &&
                           platformAdjustment.ReferenceId == transaction.ReferenceId &&
                           platformAdjustment.Amount == transaction.Amount &&
                           platformAdjustment.Currency == transaction.Currency &&
                           _dbContext.JournalEntries
                               .IgnoreQueryFilters()
                               .Any(journal =>
                                    journal.TenantId == tenantId &&
                                    journal.SourceType == "CommerceHubFinancialTransaction" &&
                                    journal.SourceId == platformAdjustment.Id &&
                                    journal.Status == JournalStatus.Posted))) ||
                  (transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                   transaction.EntityId == tenantId &&
                   transaction.TransactionType == CommerceHubFinancialTransactionType.Payout &&
                   transaction.Direction == CommerceHubFinancialTransactionDirection.Debit &&
                   transaction.FinancialBucket == CommerceHubFinancialBucket.Payable &&
                   transaction.ReferenceType == CommerceHubFinancialReferenceType.Payout &&
                   _dbContext.Set<CommerceHubFinancialTransaction>()
                       .AsNoTracking()
                       .Any(platformPayout =>
                           platformPayout.EntityType == CommerceHubFinancialEntityType.Platform &&
                           platformPayout.TransactionType == CommerceHubFinancialTransactionType.Payout &&
                           platformPayout.Direction == CommerceHubFinancialTransactionDirection.Debit &&
                           platformPayout.FinancialBucket == CommerceHubFinancialBucket.Payable &&
                           platformPayout.ReferenceType == CommerceHubFinancialReferenceType.Payout &&
                           platformPayout.ReferenceId == transaction.ReferenceId &&
                           platformPayout.Amount == transaction.Amount &&
                           platformPayout.Currency == transaction.Currency &&
                           _dbContext.JournalEntries
                               .IgnoreQueryFilters()
                               .Any(journal =>
                                   journal.TenantId == tenantId &&
                                   journal.SourceType == "CommerceHubFinancialTransaction" &&
                                   journal.SourceId == platformPayout.Id &&
                                   journal.Status == JournalStatus.Posted)))))
            .OrderBy(transaction => transaction.CreatedAtUtc)
            .Select(transaction => new
            {
                transaction.Id,
                transaction.ReferenceType,
                transaction.ReferenceId,
                transaction.TransactionType,
                transaction.Direction,
                transaction.Amount,
                transaction.Currency
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (uncoveredTransaction == null)
        {
            return;
        }

        _logger.LogError(
            "Blocking CommerceHub outward money movement because CommerceHub operational ledger is not covered by GL | Operation: {Operation} | VendorId: {VendorId} | TransactionId: {TransactionId} | ReferenceType: {ReferenceType} | ReferenceId: {ReferenceId} | TransactionType: {TransactionType} | Direction: {Direction} | Amount: {Amount} {Currency}",
            operationName,
            tenantId,
            uncoveredTransaction.Id,
            uncoveredTransaction.ReferenceType,
            uncoveredTransaction.ReferenceId,
            uncoveredTransaction.TransactionType,
            uncoveredTransaction.Direction,
            uncoveredTransaction.Amount,
            uncoveredTransaction.Currency);

        throw new CommerceHubCheckoutException(
            "COMMERCEHUB_LEDGER_ACCOUNTING_COVERAGE_REQUIRED",
            "Outward money movement is blocked until CommerceHub financial ledger entries are posted to the general ledger.",
            StatusCodes.Status409Conflict);
    }

    private async Task EnsureCommerceHubReconciliationHealthyAsync(
        Guid tenantId,
        string operationName,
        DateTime nowUtc,
        bool allowFreshStartupBypass,
        CancellationToken cancellationToken)
    {
        var latestReport = await _dbContext.Set<CommerceHubFinancialReconciliationReport>()
            .AsNoTracking()
            .OrderByDescending(report => report.TimestampUtc)
            .Select(report => new
            {
                report.Id,
                report.Status,
                report.TimestampUtc,
                report.MismatchCount,
                report.DetailsJson
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (latestReport == null)
        {
            if (allowFreshStartupBypass)
            {
                // STARTUP TOLERANCE: On a fresh system with no reconciliation reports yet,
                // allow intake to proceed. There's nothing to reconcile on first startup,
                // and blocking intake would create a chicken-and-egg problem where intake
                // is blocked until reconciliation runs, but reconciliation has nothing to
                // check until intake creates financial records.
                //
                // SECURITY: Verify this is truly a fresh system by checking for existing
                // CommerceHub financial records. Only allow bypass if no financial records exist.
                var hasFinancialRecords = await HasAnyCommerceHubFinancialRecordsAsync(cancellationToken);
                
                if (!hasFinancialRecords)
                {
                    // Once the first reconciliation report is generated (by the scheduled job),
                    // subsequent intake operations will be subject to reconciliation health checks.
                    _logger.LogWarning(
                        "Allowing CommerceHub intake despite no financial reconciliation report (fresh system startup scenario) | Operation: {Operation} | VendorId: {VendorId}",
                        operationName,
                        tenantId);

                    return;
                }
                
                // FAIL-CLOSED: Financial records exist but no reconciliation report.
                // This indicates a data integrity issue (report was deleted or never generated).
                _logger.LogError(
                    "Blocking CommerceHub intake because financial records exist but no reconciliation report found | Operation: {Operation} | VendorId: {VendorId}",
                    operationName,
                    tenantId);

                throw new CommerceHubCheckoutException(
                    "COMMERCEHUB_FINANCIAL_RECONCILIATION_REPORT_REQUIRED",
                    "Intake is blocked because financial records exist but no reconciliation report is available.",
                    StatusCodes.Status409Conflict);
            }

            // FAIL-CLOSED: Outward money movement requires a reconciliation report to exist.
            _logger.LogError(
                "Blocking CommerceHub outward money movement because no financial reconciliation report exists | Operation: {Operation} | VendorId: {VendorId}",
                operationName,
                tenantId);

            throw new CommerceHubCheckoutException(
                "COMMERCEHUB_FINANCIAL_RECONCILIATION_REPORT_REQUIRED",
                "Outward money movement is blocked until a financial reconciliation report exists.",
                StatusCodes.Status409Conflict);
        }

        var scope = ParseReconciliationScope(latestReport.DetailsJson);
        if (!scope.IsVendorScoped || !scope.CheckedVendorIds.Contains(tenantId))
        {
            if (allowFreshStartupBypass &&
                !await HasVendorFinancialFootprintAsync(tenantId, cancellationToken))
            {
                _logger.LogWarning(
                    "Allowing CommerceHub intake for a vendor outside the latest reconciliation scope because no prior financial footprint exists | Operation: {Operation} | VendorId: {VendorId} | ReportId: {ReportId} | TimestampUtc: {TimestampUtc}",
                    operationName,
                    tenantId,
                    latestReport.Id,
                    latestReport.TimestampUtc);

                return;
            }

            _logger.LogError(
                "Blocking CommerceHub outward money movement because latest reconciliation report does not prove vendor coverage | Operation: {Operation} | VendorId: {VendorId} | ReportId: {ReportId} | Status: {Status} | TimestampUtc: {TimestampUtc}",
                operationName,
                tenantId,
                latestReport.Id,
                latestReport.Status,
                latestReport.TimestampUtc);

            throw new CommerceHubCheckoutException(
                "COMMERCEHUB_FINANCIAL_RECONCILIATION_VENDOR_SCOPE_REQUIRED",
                "Outward money movement is blocked until financial reconciliation proves this vendor was checked.",
                StatusCodes.Status409Conflict);
        }

        var isCritical = string.Equals(latestReport.Status, "Critical", StringComparison.Ordinal);
        var isStale = latestReport.TimestampUtc < nowUtc.Subtract(ReconciliationMaxAge);
        var hasVendorCriticalMismatch = isCritical &&
            (scope.AffectedVendorIds.Count == 0 || scope.AffectedVendorIds.Contains(tenantId));

        if (!hasVendorCriticalMismatch && !isStale)
        {
            return;
        }

        _logger.LogError(
            "Blocking CommerceHub outward money movement because financial reconciliation is unhealthy for this vendor | Operation: {Operation} | VendorId: {VendorId} | ReportId: {ReportId} | Status: {Status} | TimestampUtc: {TimestampUtc} | MismatchCount: {MismatchCount} | IsStale: {IsStale} | AffectedVendorCount: {AffectedVendorCount}",
            operationName,
            tenantId,
            latestReport.Id,
            latestReport.Status,
            latestReport.TimestampUtc,
            latestReport.MismatchCount,
            isStale,
            scope.AffectedVendorIds.Count);

        throw new CommerceHubCheckoutException(
            "COMMERCEHUB_FINANCIAL_RECONCILIATION_NOT_READY",
            "Outward money movement is blocked until CommerceHub financial reconciliation is healthy.",
            StatusCodes.Status409Conflict);
    }

    private static ReconciliationScope ParseReconciliationScope(string? detailsJson)
    {
        if (string.IsNullOrWhiteSpace(detailsJson))
        {
            return ReconciliationScope.Unscoped;
        }

        try
        {
            using var document = JsonDocument.Parse(detailsJson);
            var root = document.RootElement;
            var checkedVendorIds = ReadGuidArray(root, "checkedVendorIds");
            if (checkedVendorIds.Count == 0)
            {
                return ReconciliationScope.Unscoped;
            }

            return new ReconciliationScope(
                true,
                checkedVendorIds,
                ReadGuidArray(root, "affectedVendorIds"));
        }
        catch (JsonException)
        {
            return ReconciliationScope.Unscoped;
        }
    }

    private static HashSet<Guid> ReadGuidArray(JsonElement root, string propertyName)
    {
        var values = new HashSet<Guid>();
        if (!root.TryGetProperty(propertyName, out var element) ||
            element.ValueKind != JsonValueKind.Array)
        {
            return values;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String &&
                Guid.TryParse(item.GetString(), out var value) &&
                value != Guid.Empty)
            {
                values.Add(value);
            }
        }

        return values;
    }

    private sealed record ReconciliationScope(
        bool IsVendorScoped,
        HashSet<Guid> CheckedVendorIds,
        HashSet<Guid> AffectedVendorIds)
    {
        public static ReconciliationScope Unscoped { get; } = new(false, [], []);
    }

    private static bool HasDurableTenantScope(
        Guid? baseTenantId,
        string? affectedStoreIdsJson,
        string? suspenseRoutedStoreIdsJson)
    {
        if (baseTenantId.HasValue && baseTenantId.Value != Guid.Empty)
        {
            return true;
        }

        return HasExplicitTenantScope(affectedStoreIdsJson) ||
               HasExplicitTenantScope(suspenseRoutedStoreIdsJson);
    }

    private static bool HasExplicitTenantScope(string? tenantIdsJson)
    {
        if (string.IsNullOrWhiteSpace(tenantIdsJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(tenantIdsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var foundAnyScope = false;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String ||
                    !Guid.TryParse(item.GetString(), out var tenantId) ||
                    tenantId == Guid.Empty)
                {
                    return false;
                }

                foundAnyScope = true;
            }

            return foundAnyScope;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool CompensationMayAffectTenant(Guid? messageTenantId, string? affectedStoreIdsJson, Guid tenantId)
    {
        if (messageTenantId == tenantId)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(affectedStoreIdsJson))
        {
            return !messageTenantId.HasValue || messageTenantId.Value == Guid.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(affectedStoreIdsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return true; // Fail-closed: malformed scope data may affect tenant
            }

            var foundAnyScope = false;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String ||
                    !Guid.TryParse(item.GetString(), out var affectedTenantId) ||
                    affectedTenantId == Guid.Empty)
                {
                    return true; // Fail-closed: malformed scope data may affect tenant
                }

                foundAnyScope = true;
                if (affectedTenantId == tenantId)
                {
                    return true;
                }
            }

            return !foundAnyScope && (!messageTenantId.HasValue || messageTenantId.Value == Guid.Empty);
        }
        catch (JsonException)
        {
            return true; // Fail-closed: unparseable scope data may affect tenant
        }
    }

    private static bool UnresolvedCapturedPaymentMayAffectTenant(
        Guid? webhookTenantId,
        string? affectedStoreIdsJson,
        string? suspenseRoutedStoreIdsJson,
        Guid tenantId)
    {
        if (CompensationMayAffectTenant(webhookTenantId, affectedStoreIdsJson, tenantId))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(suspenseRoutedStoreIdsJson) &&
               CompensationMayAffectTenant(null, suspenseRoutedStoreIdsJson, tenantId);
    }

    private void LogLargeTenantScopeInspection(
        string backlogName,
        string operationName,
        Guid tenantId,
        int inspectedRecordCount,
        DateTime? staleCutoffUtc)
    {
        if (inspectedRecordCount < TenantScopeInspectionWarningThreshold)
        {
            return;
        }

        _logger.LogWarning(
            "CommerceHub readiness gate scanned a large tenant-scoped backlog before clearing the operation | Operation: {Operation} | TenantId: {TenantId} | Backlog: {Backlog} | InspectedRecords: {InspectedRecords} | StaleCutoffUtc: {StaleCutoffUtc}",
            operationName,
            tenantId,
            backlogName,
            inspectedRecordCount,
            staleCutoffUtc);
    }

    private async Task EnsureIntegrationBackboneHealthyAsync(
        Guid tenantId,
        string operationName,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var staleCutoffUtc = nowUtc.Subtract(IntegrationBackboneMaxLag);
        var tenantIdText = tenantId.ToString();

        // Note: EnsureAmbiguousMarketplaceAggregateBackboneHealthyAsync is called once before the tenant loop
        // in EnsureCommerceIntakeAllowedAsync to avoid redundant global scans

        var authoritativeMessagesInspected = 0;

        await foreach (var message in _dbContext.Set<OutboxMessage>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.ProcessedAtUtc == null &&
                (message.RetryCount >= OutboxMessage.MaxRetries ||
                 (message.LastErrorAtUtc != null && message.CreatedAtUtc < staleCutoffUtc)))
            .Where(BuildCriticalEventTypeFilter())
            .Where(message =>
                message.TenantId == tenantId ||
                message.EventPayload.Contains(tenantIdText))
            .OrderByDescending(message => message.RetryCount >= OutboxMessage.MaxRetries || message.LastErrorAtUtc != null)
            .ThenBy(message => message.CreatedAtUtc)
            .Select(message => new
            {
                message.Id,
                message.EventType,
                message.TenantId,
                message.EventPayload,
                message.AggregateId,
                message.RetryCount,
                message.LastErrorAtUtc,
                message.CreatedAtUtc
            })
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken))
        {
            authoritativeMessagesInspected++;
            if (!EventMayAffectTenant(message.TenantId, message.EventPayload, tenantId))
            {
                continue;
            }

            _logger.LogError(
                "Blocking CommerceHub outward money movement because the authoritative integration outbox has an unhealthy message | Operation: {Operation} | TenantId: {TenantId} | MessageId: {MessageId} | EventType: {EventType} | AggregateId: {AggregateId} | RetryCount: {RetryCount} | LastErrorAtUtc: {LastErrorAtUtc} | CreatedAtUtc: {CreatedAtUtc}",
                operationName,
                tenantId,
                message.Id,
                message.EventType,
                message.AggregateId,
                message.RetryCount,
                message.LastErrorAtUtc,
                message.CreatedAtUtc);

            throw new CommerceHubCheckoutException(
                "AUTHORITATIVE_EVENT_BACKBONE_NOT_READY",
                "Outward money movement is blocked until critical integration events are published or repaired.",
                StatusCodes.Status409Conflict);
        }

        LogLargeTenantScopeInspection(
            "authoritative integration outbox",
            operationName,
            tenantId,
            authoritativeMessagesInspected,
            staleCutoffUtc);

        var busMessagesInspected = 0;

        await foreach (var message in (
            from message in _dbContext.Set<MassTransitOutboxMessage>().AsNoTracking()
            join state in _dbContext.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxState>().AsNoTracking()
                on message.OutboxId equals state.OutboxId into stateGroup
            from state in stateGroup.DefaultIfEmpty()
            where message.SentTime < staleCutoffUtc &&
                  (state == null || message.SequenceNumber > state.LastSequenceNumber)
            select message)
            .Where(BuildCriticalMassTransitOutboxMessageTypeFilter())
            .Where(message => message.Body.Contains(tenantIdText))
            .OrderBy(message => message.SentTime)
            .Select(message => new
            {
                message.SequenceNumber,
                message.MessageId,
                message.MessageType,
                message.SentTime,
                message.Body
            })
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken))
        {
            busMessagesInspected++;
            if (!EventMayAffectTenant(null, message.Body, tenantId))
            {
                continue;
            }

            _logger.LogError(
                "Blocking CommerceHub outward money movement because the MassTransit bus outbox has a stale authoritative message | Operation: {Operation} | TenantId: {TenantId} | SequenceNumber: {SequenceNumber} | MessageId: {MessageId} | MessageType: {MessageType} | SentTime: {SentTime}",
                operationName,
                tenantId,
                message.SequenceNumber,
                message.MessageId,
                message.MessageType,
                message.SentTime);

            throw new CommerceHubCheckoutException(
                "MASS_TRANSIT_BUS_OUTBOX_NOT_READY",
                "Outward money movement is blocked until authoritative bus outbox messages are delivered.",
                StatusCodes.Status409Conflict);
        }

        LogLargeTenantScopeInspection(
            "MassTransit bus outbox",
            operationName,
            tenantId,
            busMessagesInspected,
            staleCutoffUtc);

        var marketplaceMessagesInspected = 0;

        await foreach (var message in _dbContext.Set<MarketplaceEventOutboxMessage>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.PublishedAtUtc == null &&
                (message.IsFailed || message.OccurredAtUtc < staleCutoffUtc))
            .Where(message =>
                message.TenantId == tenantId ||
                message.PayloadJson.Contains(tenantIdText))
            .OrderBy(message => message.OccurredAtUtc)
            .Select(message => new
            {
                message.Id,
                message.EventType,
                message.EntityId,
                message.TenantId,
                message.PayloadJson,
                message.IsFailed,
                message.OccurredAtUtc
            })
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken))
        {
            marketplaceMessagesInspected++;
            if (!EventMayAffectTenant(message.TenantId, message.PayloadJson, tenantId))
            {
                continue;
            }

            _logger.LogError(
                "Blocking CommerceHub outward money movement because marketplace event backbone has an unhealthy unpublished message | Operation: {Operation} | TenantId: {TenantId} | MessageId: {MessageId} | EventType: {EventType} | EntityId: {EntityId} | IsFailed: {IsFailed} | OccurredAtUtc: {OccurredAtUtc}",
                operationName,
                tenantId,
                message.Id,
                message.EventType,
                message.EntityId,
                message.IsFailed,
                message.OccurredAtUtc);

            throw new CommerceHubCheckoutException(
                "MARKETPLACE_EVENT_BACKBONE_NOT_READY",
                "Outward money movement is blocked until failed marketplace events are repaired.",
                StatusCodes.Status409Conflict);
        }

        LogLargeTenantScopeInspection(
            "marketplace event outbox",
            operationName,
            tenantId,
            marketplaceMessagesInspected,
            staleCutoffUtc);

        var failedStockEvent = await _dbContext.Set<StockSyncOutboxMessage>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.TenantId == tenantId &&
                message.PublishedAtUtc == null &&
                (message.IsFailed || message.OccurredAtUtc < staleCutoffUtc))
            .OrderBy(message => message.StockUpdatedAtUtc)
            .Select(message => new { message.Id, message.VariantId, message.IsFailed, message.OccurredAtUtc })
            .FirstOrDefaultAsync(cancellationToken);

        if (failedStockEvent == null)
        {
            return;
        }

        _logger.LogError(
            "Blocking CommerceHub outward money movement because stock sync backbone has an unhealthy unpublished message | Operation: {Operation} | TenantId: {TenantId} | MessageId: {MessageId} | VariantId: {VariantId} | IsFailed: {IsFailed} | OccurredAtUtc: {OccurredAtUtc}",
            operationName,
            tenantId,
            failedStockEvent.Id,
            failedStockEvent.VariantId,
            failedStockEvent.IsFailed,
            failedStockEvent.OccurredAtUtc);

        throw new CommerceHubCheckoutException(
            "STOCK_SYNC_BACKBONE_NOT_READY",
            "Outward money movement is blocked until failed stock synchronization events are repaired.",
            StatusCodes.Status409Conflict);
    }

    private async Task EnsureAmbiguousMarketplaceAggregateBackboneHealthyAsync(
        Guid tenantId,
        string operationName,
        DateTime staleCutoffUtc,
        CancellationToken cancellationToken)
    {
        await foreach (var message in _dbContext.Set<OutboxMessage>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.ProcessedAtUtc == null &&
                (message.RetryCount >= OutboxMessage.MaxRetries ||
                 (message.LastErrorAtUtc != null && message.CreatedAtUtc < staleCutoffUtc)))
            .Where(BuildCriticalEventTypeFilter())
            // SECURITY FIX: Removed prefilter for "MarketplaceOrderId" to ensure malformed payloads
            // reach IsAmbiguousCriticalMarketplaceAggregate for fail-closed evaluation
            .OrderByDescending(message => message.RetryCount >= OutboxMessage.MaxRetries || message.LastErrorAtUtc != null)
            .ThenBy(message => message.CreatedAtUtc)
            .Select(message => new
            {
                message.Id,
                message.EventType,
                message.TenantId,
                message.EventPayload,
                message.AggregateId,
                message.RetryCount,
                message.LastErrorAtUtc,
                message.CreatedAtUtc
            })
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken))
        {
            if (!IsAmbiguousCriticalMarketplaceAggregate(message.TenantId, message.EventPayload))
            {
                continue;
            }

            _logger.LogError(
                "Blocking CommerceHub money movement because an ambiguous critical marketplace aggregate event is unhealthy and cannot prove affected tenant scope | Operation: {Operation} | TenantId: {TenantId} | MessageId: {MessageId} | EventType: {EventType} | AggregateId: {AggregateId} | RetryCount: {RetryCount} | LastErrorAtUtc: {LastErrorAtUtc} | CreatedAtUtc: {CreatedAtUtc}",
                operationName,
                tenantId,
                message.Id,
                message.EventType,
                message.AggregateId,
                message.RetryCount,
                message.LastErrorAtUtc,
                message.CreatedAtUtc);

            throw new CommerceHubCheckoutException(
                "AUTHORITATIVE_EVENT_BACKBONE_SCOPE_UNDETERMINED",
                "CommerceHub money movement is blocked until ambiguous critical marketplace aggregate events carry explicit affected-tenant scope or are repaired.",
                StatusCodes.Status409Conflict);
        }

        await foreach (var message in (
            from message in _dbContext.Set<MassTransitOutboxMessage>().AsNoTracking()
            join state in _dbContext.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxState>().AsNoTracking()
                on message.OutboxId equals state.OutboxId into stateGroup
            from state in stateGroup.DefaultIfEmpty()
            where message.SentTime < staleCutoffUtc &&
                  (state == null || message.SequenceNumber > state.LastSequenceNumber)
            select message)
            .Where(BuildCriticalMassTransitOutboxMessageTypeFilter())
            // SECURITY FIX: Removed prefilter for "MarketplaceOrderId" to ensure malformed payloads
            // reach IsAmbiguousCriticalMarketplaceAggregate for fail-closed evaluation
            .OrderBy(message => message.SentTime)
            .Select(message => new
            {
                message.SequenceNumber,
                message.MessageId,
                message.MessageType,
                message.SentTime,
                message.Body
            })
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken))
        {
            if (!IsAmbiguousCriticalMarketplaceAggregate(null, message.Body))
            {
                continue;
            }

            _logger.LogError(
                "Blocking CommerceHub money movement because an ambiguous critical marketplace aggregate bus outbox message is stale and cannot prove affected tenant scope | Operation: {Operation} | TenantId: {TenantId} | SequenceNumber: {SequenceNumber} | MessageId: {MessageId} | MessageType: {MessageType} | SentTime: {SentTime}",
                operationName,
                tenantId,
                message.SequenceNumber,
                message.MessageId,
                message.MessageType,
                message.SentTime);

            throw new CommerceHubCheckoutException(
                "MASS_TRANSIT_BUS_OUTBOX_SCOPE_UNDETERMINED",
                "CommerceHub money movement is blocked until ambiguous critical marketplace aggregate bus messages carry explicit affected-tenant scope or are delivered and repaired.",
                StatusCodes.Status409Conflict);
        }

        await foreach (var message in _dbContext.Set<MarketplaceEventOutboxMessage>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.PublishedAtUtc == null &&
                (message.IsFailed || message.OccurredAtUtc < staleCutoffUtc))
            // SECURITY FIX: Removed prefilter for "MarketplaceOrderId" to ensure malformed payloads
            // reach IsAmbiguousCriticalMarketplaceAggregate for fail-closed evaluation
            .OrderBy(message => message.OccurredAtUtc)
            .Select(message => new
            {
                message.Id,
                message.EventType,
                message.EntityId,
                message.TenantId,
                message.PayloadJson,
                message.IsFailed,
                message.OccurredAtUtc
            })
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken))
        {
            if (!IsAmbiguousCriticalMarketplaceAggregate(message.TenantId, message.PayloadJson))
            {
                continue;
            }

            _logger.LogError(
                "Blocking CommerceHub money movement because an ambiguous marketplace event backbone message is unhealthy and cannot prove affected tenant scope | Operation: {Operation} | TenantId: {TenantId} | MessageId: {MessageId} | EventType: {EventType} | EntityId: {EntityId} | IsFailed: {IsFailed} | OccurredAtUtc: {OccurredAtUtc}",
                operationName,
                tenantId,
                message.Id,
                message.EventType,
                message.EntityId,
                message.IsFailed,
                message.OccurredAtUtc);

            throw new CommerceHubCheckoutException(
                "MARKETPLACE_EVENT_BACKBONE_SCOPE_UNDETERMINED",
                "CommerceHub money movement is blocked until ambiguous marketplace event backbone messages carry explicit affected-tenant scope or are repaired.",
                StatusCodes.Status409Conflict);
        }
    }

    private static bool EventMayAffectTenant(Guid? baseTenantId, string? eventPayload, Guid tenantId)
    {
        try
        {
            if (AffectedTenantScopeResolver.ContainsTenant(baseTenantId, eventPayload, tenantId))
            {
                return true;
            }

            // Fail closed for critical financial/integration blockers whose payload no
            // longer exposes a tenant scope we can prove unrelated to this payout.
            return AffectedTenantScopeResolver.Resolve(baseTenantId, eventPayload).Count == 0 ||
                   AffectedTenantScopeResolver.RequiresGlobalFailClosedForAmbiguousMarketplaceAggregate(
                       baseTenantId,
                       eventPayload);
        }
        catch (InvalidEventPayloadException)
        {
            return true;
        }
    }

    private static bool IsAmbiguousCriticalMarketplaceAggregate(Guid? baseTenantId, string? eventPayload)
    {
        try
        {
            return AffectedTenantScopeResolver.RequiresGlobalFailClosedForAmbiguousMarketplaceAggregate(
                baseTenantId,
                eventPayload);
        }
        catch (InvalidEventPayloadException)
        {
            return true;
        }
    }

    private static Expression<Func<OutboxMessage, bool>> BuildCriticalEventTypeFilter()
    {
        var parameter = Expression.Parameter(typeof(OutboxMessage), "message");
        var eventType = Expression.Property(parameter, nameof(OutboxMessage.EventType));
        
        // EF Core translation: Use ToUpper() for case-insensitive comparison (translates to SQL UPPER())
        // instead of StringComparison overloads which EF Core cannot translate
        var toUpperMethod = typeof(string).GetMethod(nameof(string.ToUpper), Type.EmptyTypes)!;
        var eventTypeUpper = Expression.Call(eventType, toUpperMethod);
        
        var indexOfMethod = typeof(string).GetMethod(
            nameof(string.IndexOf), 
            [typeof(string)])!;
        var endsWithMethod = typeof(string).GetMethod(
            nameof(string.EndsWith), 
            [typeof(string)])!;
        var zeroConstant = Expression.Constant(0);

        Expression? includedExpression = null;
        foreach (var fragment in IntegrationEventAuthority.CriticalEventNameFragments)
        {
            var fragmentConstant = Expression.Constant(fragment.ToUpperInvariant());
            var indexOfCall = Expression.Call(eventTypeUpper, indexOfMethod, fragmentConstant);
            var containsCheck = Expression.GreaterThanOrEqual(indexOfCall, zeroConstant);
            includedExpression = includedExpression == null
                ? containsCheck
                : Expression.OrElse(includedExpression, containsCheck);
        }

        Expression? excludedExpression = null;
        foreach (var eventTypeName in IntegrationEventAuthority.NonAuthoritativeEventTypeNames)
        {
            var eventTypeConstant = Expression.Constant(eventTypeName.ToUpperInvariant());
            var excludedTypeCall = Expression.Call(eventTypeUpper, endsWithMethod, eventTypeConstant);
            excludedExpression = excludedExpression == null
                ? excludedTypeCall
                : Expression.OrElse(excludedExpression, excludedTypeCall);
        }

        if (excludedExpression != null)
        {
            includedExpression = Expression.AndAlso(includedExpression!, Expression.Not(excludedExpression));
        }

        return Expression.Lambda<Func<OutboxMessage, bool>>(includedExpression!, parameter);
    }

    private static Expression<Func<MassTransitOutboxMessage, bool>> BuildCriticalMassTransitOutboxMessageTypeFilter()
    {
        var parameter = Expression.Parameter(typeof(MassTransitOutboxMessage), "message");
        var messageType = Expression.Property(parameter, nameof(MassTransitOutboxMessage.MessageType));
        
        // EF Core translation: Use ToUpper() for case-insensitive comparison (translates to SQL UPPER())
        // instead of StringComparison overloads which EF Core cannot translate
        var toUpperMethod = typeof(string).GetMethod(nameof(string.ToUpper), Type.EmptyTypes)!;
        var messageTypeUpper = Expression.Call(messageType, toUpperMethod);
        
        var indexOfMethod = typeof(string).GetMethod(
            nameof(string.IndexOf), 
            [typeof(string)])!;
        var zeroConstant = Expression.Constant(0);

        Expression? includedExpression = null;
        foreach (var fragment in IntegrationEventAuthority.CriticalEventNameFragments)
        {
            var fragmentConstant = Expression.Constant(fragment.ToUpperInvariant());
            var indexOfCall = Expression.Call(messageTypeUpper, indexOfMethod, fragmentConstant);
            var containsCheck = Expression.GreaterThanOrEqual(indexOfCall, zeroConstant);
            includedExpression = includedExpression == null
                ? containsCheck
                : Expression.OrElse(includedExpression, containsCheck);
        }

        foreach (var eventTypeName in IntegrationEventAuthority.NonAuthoritativeEventTypeNames)
        {
            var eventTypeConstant = Expression.Constant(eventTypeName.ToUpperInvariant());
            var excludedIndexOfCall = Expression.Call(messageTypeUpper, indexOfMethod, eventTypeConstant);
            var excludedContainsCheck = Expression.GreaterThanOrEqual(excludedIndexOfCall, zeroConstant);
            includedExpression = Expression.AndAlso(includedExpression!, Expression.Not(excludedContainsCheck));
        }

        return Expression.Lambda<Func<MassTransitOutboxMessage, bool>>(includedExpression!, parameter);
    }

    private void EnsureEmergencyOutwardMoneyMovementAllowed(Guid tenantId, string operationName)
    {
        var options = _emergencyStops.CurrentValue;
        if (!options.OutwardMoneyMovementStopped &&
            !(options.PayoutsStopped && IsPayoutOperation(operationName)) &&
            !(options.RefundsStopped && IsRefundOperation(operationName)))
        {
            return;
        }

        var stopReason = BuildEmergencyStopReason(options, operationName);
        _logger.LogCritical(
            "Blocking CommerceHub outward money movement because an emergency stop is active | Operation: {Operation} | TenantId: {TenantId} | Reason: {Reason}",
            operationName,
            tenantId,
            stopReason);

        throw new CommerceHubCheckoutException(
            "COMMERCEHUB_EMERGENCY_MONEY_MOVEMENT_STOPPED",
            stopReason,
            StatusCodes.Status503ServiceUnavailable);
    }

    private void EnsureEmergencyCommerceIntakeAllowed(
        IReadOnlyCollection<Guid> tenantIds,
        string operationName)
    {
        var options = _emergencyStops.CurrentValue;
        if (!options.CommerceIntakeStopped &&
            !(options.PaymentIntakeStopped && IsPaymentIntakeOperation(operationName)))
        {
            return;
        }

        var stopReason = BuildEmergencyStopReason(options, operationName);
        _logger.LogCritical(
            "Blocking CommerceHub payment intake because an emergency stop is active | Operation: {Operation} | TenantCount: {TenantCount} | Reason: {Reason}",
            operationName,
            tenantIds.Count,
            stopReason);

        throw new CommerceHubCheckoutException(
            "COMMERCEHUB_EMERGENCY_PAYMENT_INTAKE_STOPPED",
            stopReason,
            StatusCodes.Status503ServiceUnavailable);
    }

    private static bool IsPayoutOperation(string operationName) =>
        operationName.Contains("payout", StringComparison.OrdinalIgnoreCase);

    private static bool IsRefundOperation(string operationName) =>
        operationName.Contains("refund", StringComparison.OrdinalIgnoreCase);

    private static bool IsPaymentIntakeOperation(string operationName) =>
        operationName.Contains("payment", StringComparison.OrdinalIgnoreCase) ||
        operationName.Contains("checkout", StringComparison.OrdinalIgnoreCase) ||
        operationName.Contains("intake", StringComparison.OrdinalIgnoreCase);

    private static string BuildEmergencyStopReason(
        CommerceHubEmergencyStopOptions options,
        string operationName)
    {
        var configuredReason = string.IsNullOrWhiteSpace(options.Reason)
            ? null
            : options.Reason.Trim();

        return configuredReason ??
               $"CommerceHub emergency stop is active for operation '{operationName}'.";
    }

    private async Task<bool> HasAnyCommerceHubFinancialRecordsAsync(CancellationToken cancellationToken)
    {
        // Check for any CommerceHub financial transactions (the core ledger)
        var hasTransactions = await _dbContext.Set<CommerceHubFinancialTransaction>()
            .AsNoTracking()
            .AnyAsync(cancellationToken);

        if (hasTransactions)
        {
            return true;
        }

        // Check for any vendor settlements (outward money movement records)
        var hasSettlements = await _dbContext.CommerceHubVendorSettlements
            .AsNoTracking()
            .AnyAsync(cancellationToken);

        if (hasSettlements)
        {
            return true;
        }

        // Check for any financial audit logs (evidence of financial operations)
        var hasAuditLogs = await _dbContext.CommerceHubFinancialAuditLogs
            .AsNoTracking()
            .AnyAsync(cancellationToken);

        if (hasAuditLogs)
        {
            return true;
        }

        // PRODUCTION HARDENING: Check for unresolved payment webhooks (evidence of provider cash flow)
        // Unresolved webhooks represent captured payments or refunds that haven't been reconciled yet.
        // Their presence indicates prior financial activity and should prevent fresh startup bypass.
        var hasUnresolvedWebhooks = await _dbContext.CommerceHubUnresolvedPaymentWebhooks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(webhook => !webhook.IsDeleted, cancellationToken);

        return hasUnresolvedWebhooks;
    }

    private async Task<bool> HasVendorFinancialFootprintAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (await _dbContext.CommerceHubSubOrders
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(subOrder => subOrder.StoreId == tenantId, cancellationToken) ||
            await _dbContext.Set<CommerceHubFinancialTransaction>()
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(transaction =>
                    transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                    transaction.EntityId == tenantId,
                    cancellationToken) ||
            await _dbContext.CommerceHubVendorEarnings
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(earning => earning.TenantId == tenantId, cancellationToken) ||
            await _dbContext.CommerceHubPayouts
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(payout => payout.TenantId == tenantId, cancellationToken) ||
            await _dbContext.CommerceHubVendorSettlements
                .AsNoTracking()
                .AnyAsync(settlement => settlement.VendorId == tenantId, cancellationToken) ||
            await _dbContext.CommerceHubFinancialAuditLogs
                .AsNoTracking()
                .AnyAsync(audit => audit.TenantId == tenantId, cancellationToken))
        {
            return true;
        }

        return false;
    }
}
