using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PointOfSale.API.Services.ProductAuthoring;
using PointOfSale.Core.Application.ProductAuthoring;
using PointOfSale.Core.DTOs.Bulk;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Bulk;
using PointOfSale.Core.Interfaces.ProductAuthoring;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.API.Services.ProductAuthoring.Bulk;

public sealed class BulkProductLifecycleService : IBulkProductLifecycleService
{
    private const string DependencyBlockedMessage =
        "Product cannot be deleted because it has blocking dependencies.";
    private const string FinalizedFailureMessage =
        "Bulk product lifecycle processing was interrupted.";

    private readonly IBulkPreparationService _preparationService;
    private readonly IBulkIdempotencyService _idempotencyService;
    private readonly IBulkOperationResultAggregator _resultAggregator;
    private readonly IBulkProductLoader _productLoader;
    private readonly IProductAuthoringOrchestrator _orchestrator;
    private readonly IProductAuthoringCommandMapper _commandMapper;
    private readonly PointOfSale.API.Services.IValidationService _validationService;
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IServiceScopeFactory? _leaseRenewalScopeFactory;
    private readonly TimeSpan _leaseHeartbeatInterval;
    private readonly ILogger<BulkProductLifecycleService> _logger;

    /// <summary>
    /// Serializes every claim-state transition (heartbeat renewal, per-item
    /// ownership renewal, durable completion, failure finalization) so the
    /// in-memory lease token can never race in-process between the heartbeat
    /// and the execution flow.
    /// </summary>
    private readonly SemaphoreSlim _claimStateGate = new(1, 1);

    private volatile bool _leaseLost;
    private volatile bool _claimTerminalized;
    private CancellationTokenSource? _executionCancellation;

    public BulkProductLifecycleService(
        IBulkPreparationService preparationService,
        IBulkIdempotencyService idempotencyService,
        IBulkOperationResultAggregator resultAggregator,
        IBulkProductLoader productLoader,
        IProductAuthoringOrchestrator orchestrator,
        IProductAuthoringCommandMapper commandMapper,
        PointOfSale.API.Services.IValidationService validationService,
        ApplicationDbContext context,
        ITenantContext tenantContext,
        ILogger<BulkProductLifecycleService> logger,
        IServiceScopeFactory? leaseRenewalScopeFactory = null,
        TimeSpan? leaseHeartbeatInterval = null)
    {
        _preparationService = preparationService;
        _idempotencyService = idempotencyService;
        _resultAggregator = resultAggregator;
        _productLoader = productLoader;
        _orchestrator = orchestrator;
        _commandMapper = commandMapper;
        _validationService = validationService;
        _context = context;
        _tenantContext = tenantContext;
        _leaseRenewalScopeFactory = leaseRenewalScopeFactory;
        _leaseHeartbeatInterval = leaseHeartbeatInterval ?? BulkClaimLeaseHeartbeat.DefaultInterval;
        _logger = logger;
    }

    public async Task<BulkLifecycleOutcome> ExecuteLifecycleAsync(
        BulkOperationType operation,
        BulkProductRequest? request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (operation is not BulkOperationType.Activate and not BulkOperationType.Deactivate and not BulkOperationType.Publish and not BulkOperationType.Unpublish and not BulkOperationType.Delete)
        {
            throw new NotSupportedException($"Unsupported bulk lifecycle operation: {operation}");
        }

        var keyValidation = _idempotencyService.ValidateKey(idempotencyKey);
        if (!keyValidation.IsValid)
        {
            return BulkLifecycleOutcome.RequestValidationFailed(
                [new BulkRequestValidationError(keyValidation.ErrorCode!, "idempotencyKey", keyValidation.Message!)]);
        }

        var preparation = await _preparationService.PrepareAsync(request, cancellationToken);
        if (!preparation.IsValid)
        {
            return BulkLifecycleOutcome.RequestValidationFailed(preparation.Errors);
        }

        var normalizedItems = preparation.PreparedItems
            .Select(item => new NormalizedBulkProductItem(item.ProductId, item.ExpectedVersion))
            .ToArray();

        var fingerprint = _idempotencyService.BuildFingerprint(operation, normalizedItems);

        var claim = await _idempotencyService.TryClaimAsync(
            operation, idempotencyKey!, fingerprint, cancellationToken);

        if (claim.Replay is not null)
        {
            if (claim.Replay.Kind == BulkIdempotencyReplayKind.Completed)
            {
                if (claim.Replay.Record?.ResponseStatusCode != 200)
                {
                    return BulkLifecycleOutcome.Failed(
                        _idempotencyService.ReadStoredFailureMessage(claim.Replay.Record)
                        ?? FinalizedFailureMessage);
                }

                var stored = _idempotencyService.ReadStoredResponse(claim.Replay.Record);
                if (stored is not null)
                {
                    return BulkLifecycleOutcome.Success(stored);
                }

                return BulkLifecycleOutcome.IdempotencyInProgress(
                    "A bulk request with the same idempotency key is already being processed.");
            }

            if (claim.Replay.Kind == BulkIdempotencyReplayKind.Conflict)
            {
                return BulkLifecycleOutcome.IdempotencyConflict(
                    "Idempotency key was already used for a different bulk request.");
            }

            return BulkLifecycleOutcome.IdempotencyInProgress(
                "A bulk request with the same idempotency key is already being processed.");
        }

        _leaseLost = false;
        _claimTerminalized = false;

        var results = new List<BulkItemResult>(preparation.Accepted);
        var tenantId = _tenantContext.RequiredTenantId;
        CancellationTokenSource? executionCancellation = null;
        BulkClaimLeaseHeartbeat? heartbeat = null;
        try
        {
            _context.ChangeTracker.Clear();

            var preparedProductIds = preparation.PreparedItems
                .Where(item => item.State != BulkPreparedItemState.NotFound)
                .Select(item => item.ProductId)
                .ToHashSet();
            var currentProducts = _productLoader.LoadedProducts.Values
                .Where(product =>
                    product.TenantId == tenantId &&
                    preparedProductIds.Contains(product.Id))
                .ToDictionary(product => product.Id);

            // The execution now owns the outer claim: activate the periodic
            // lease heartbeat and a linked cancellation source so a lost lease
            // interrupts item work, including while one authoring call awaits.
            executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _executionCancellation = executionCancellation;
            heartbeat = StartLeaseHeartbeat(claim.Record!, preparation.CorrelationId);

            var interruptedRecovery = await TryReconcileRecoveredInterruptedExecutionAsync(
                operation,
                claim.Record!,
                idempotencyKey!,
                preparation,
                tenantId,
                forceReconciliation: _idempotencyService.IsInterruptedClaim(claim.Record),
                inspectRecoveredClaim: claim.Recovered);
            if (interruptedRecovery.ShouldComplete)
            {
                if (interruptedRecovery.StoredAggregate is not null)
                {
                    return BulkLifecycleOutcome.Success(interruptedRecovery.StoredAggregate);
                }

                if (_leaseLost)
                {
                    throw new BulkIdempotencyLeaseLostException(
                        "The bulk request lease expired and the claim is owned by another execution.");
                }

                throw new BulkAggregateCompletionUnverifiedException(
                    "Interrupted bulk recovery could not be durably verified.");
            }

            foreach (var item in preparation.PreparedItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await EnsureBulkClaimOwnershipAsync(claim.Record!, preparation.CorrelationId, cancellationToken);

                try
                {
                    results.Add(await ProcessPreparedItemAsync(
                        operation, item, idempotencyKey!, preparation.CorrelationId,
                        tenantId, currentProducts, executionCancellation.Token));
                }
                catch (OperationCanceledException) when (_leaseLost)
                {
                    throw new BulkIdempotencyLeaseLostException(
                        "The bulk request lease expired and the claim is owned by another execution.");
                }
                catch (OperationCanceledException)
                {
                    // An OCE can represent a provider/transport interruption
                    // even when the request token was not the source. Treat it
                    // as commit-ambiguous and route it through durable item
                    // reconciliation instead of storing a generic item failure.
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "Bulk product lifecycle item failed | Operation: {Operation} | ProductId: {ProductId} | CorrelationId: {CorrelationId}",
                        operation,
                        item.ProductId,
                        preparation.CorrelationId);
                    results.Add(BulkItemResult.AuthoringFailure(
                        item.ProductId,
                        item.ExpectedVersion,
                        "Product authoring failed unexpectedly."));
                }
                finally
                {
                    // Each authoring call owns its transaction. Detach its completed or
                    // failed graph so one item's EF state cannot leak into the next call.
                    _context.ChangeTracker.Clear();
                }
            }

            var aggregate = _resultAggregator.Aggregate(
                preparation.Requested,
                preparation.Accepted,
                preparation.CorrelationId,
                results);

            var storedAggregate = await PersistCompletedAggregateAsync(
                claim.Record!,
                aggregate,
                preparation.CorrelationId,
                cancellationToken);

            if (storedAggregate is not null)
            {
                return BulkLifecycleOutcome.Success(storedAggregate);
            }

            if (_leaseLost)
            {
                throw new BulkIdempotencyLeaseLostException(
                    "The bulk request lease expired and the claim is owned by another execution.");
            }

            // Mutations may already be durable. An unverified outer completion
            // must remain recoverable rather than being converted into a
            // permanent generic failure replay.
            throw new BulkAggregateCompletionUnverifiedException(
                "Bulk product lifecycle completion could not be durably verified.");
        }
        catch (BulkIdempotencyLeaseLostException)
        {
            // Another caller owns the claim now; leave its state untouched.
            throw;
        }
        catch (OperationCanceledException) when (!_leaseLost)
        {
            // Request cancellation is never a batch failure: per-product
            // mutations may already be durably committed. Reconcile a truthful
            // aggregate and attempt one cancellation-proof completion, then
            // rethrow the original cancellation. The claim is never finalized
            // as failed for a mere cancellation.
            await ReconcileCancelledExecutionAsync(
                operation, claim.Record!, idempotencyKey!, preparation, tenantId, results);

            if (_leaseLost)
            {
                throw new BulkIdempotencyLeaseLostException(
                    "The bulk request lease expired and the claim is owned by another execution.");
            }

            throw;
        }
        catch (BulkAggregateCompletionUnverifiedException)
        {
            // Product work may already be durable. Leave the incomplete claim
            // for fenced stale-takeover recovery instead of storing HTTP 500.
            throw;
        }
        catch (Exception exception)
        {
            var committedWorkExists = results.Any(result => result.Status == BulkItemStatus.Processed);
            if (!_leaseLost && !committedWorkExists)
            {
                if (await CompleteFailedClaimAsync(claim.Record, exception))
                {
                    // The same safe controller response is now durable. Return
                    // it to the first caller so later idempotent replays are
                    // byte-for-byte equivalent instead of surfacing a different
                    // global-exception response.
                    return BulkLifecycleOutcome.Failed(FinalizedFailureMessage);
                }
            }
            else if (committedWorkExists)
            {
                _logger.LogWarning(
                    exception,
                    "Bulk execution failed after committed item work; leaving the outer claim recoverable | CorrelationId: {CorrelationId}",
                    preparation.CorrelationId);
            }

            throw;
        }
        finally
        {
            // Keep the live owner protected while cancellation reconciliation
            // is running, then stop all background renewal deterministically.
            if (heartbeat is not null)
            {
                await heartbeat.DisposeAsync();
            }

            executionCancellation?.Dispose();
            _executionCancellation = null;
        }
    }

    private async Task EnsureBulkClaimOwnershipAsync(
        ProductRequestIdempotency? record,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (record is null)
        {
            return;
        }

        await _claimStateGate.WaitAsync(cancellationToken);
        try
        {
            if (_leaseLost)
            {
                throw new BulkIdempotencyLeaseLostException(
                    "The bulk request lease expired and the claim is owned by another execution.");
            }

            if (!await TryRenewOwnedLeaseAsync(record, cancellationToken))
            {
                MarkLeaseLost(correlationId);
                throw new BulkIdempotencyLeaseLostException(
                    "The bulk request lease expired and the claim is owned by another execution.");
            }
        }
        finally
        {
            _claimStateGate.Release();
        }
    }

    private BulkClaimLeaseHeartbeat StartLeaseHeartbeat(
        ProductRequestIdempotency? record,
        string correlationId)
    {
        if (record is null || record.Id == Guid.Empty)
        {
            return BulkClaimLeaseHeartbeat.Start(
                static _ => Task.FromResult(true),
                Timeout.InfiniteTimeSpan,
                static () => { },
                _logger);
        }

        // The heartbeat renews the owned claim on a fixed interval for the
        // whole execution, including while a single item authoring call is
        // awaiting. Every renewal is an atomic compare-and-swap over the
        // durable lease token and is serialized with completion and failure
        // finalization through the claim-state gate.
        return BulkClaimLeaseHeartbeat.Start(
            async cancellationToken =>
            {
                await _claimStateGate.WaitAsync(cancellationToken);
                try
                {
                    if (_leaseLost)
                    {
                        return false;
                    }

                    // Completion/failure owns the terminal row now. Treat the
                    // short interval before deterministic disposal as benign;
                    // a failed renewal against our own completed row is not a
                    // lease takeover.
                    if (_claimTerminalized)
                    {
                        return true;
                    }

                    return await TryRenewOwnedLeaseAsync(record, cancellationToken);
                }
                finally
                {
                    _claimStateGate.Release();
                }
            },
            _leaseHeartbeatInterval,
            () => MarkLeaseLost(correlationId),
            _logger);
    }

    private async Task<bool> TryRenewOwnedLeaseAsync(
        ProductRequestIdempotency record,
        CancellationToken cancellationToken)
    {
        if (_leaseRenewalScopeFactory is null)
        {
            // Hosts/tests without an isolation scope factory: the claim-state
            // gate still serializes every renewal against item work boundaries,
            // completion, and finalization in-process.
            return await _idempotencyService.RenewClaimLeaseAsync(record, cancellationToken);
        }

        // Heartbeat renewal must never run concurrent EF operations on the
        // scoped request context used by item authoring: resolve a fully
        // independent context/scope, carrying only the claim's tenant identity.
        using var scope = _leaseRenewalScopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenantId(record.TenantId);
        var isolatedIdempotency = scope.ServiceProvider.GetRequiredService<IBulkIdempotencyService>();
        return await isolatedIdempotency.RenewClaimLeaseAsync(record, cancellationToken);
    }

    private void MarkLeaseLost(string correlationId)
    {
        if (_leaseLost)
        {
            return;
        }

        _leaseLost = true;
        _logger.LogWarning(
            "Bulk idempotency claim lease was lost to another owner | CorrelationId: {CorrelationId}",
            correlationId);

        try
        {
            _executionCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The execution already exited; nothing left to interrupt.
        }
    }

    private async Task<BulkOperationResult?> PersistCompletedAggregateAsync(
        ProductRequestIdempotency record,
        BulkOperationResult aggregate,
        string correlationId,
        CancellationToken cancellationToken)
    {
        // Completion is a claim-state transition like any other: serialized
        // with heartbeat renewal so the lease token it compare-and-swaps on is
        // the latest one this execution observed.
        await _claimStateGate.WaitAsync(CancellationToken.None);
        try
        {
            if (_leaseLost)
            {
                return null;
            }

            try
            {
                var stored = await _idempotencyService.TryCompleteAndVerifyAsync(
                    record,
                    aggregate,
                    cancellationToken);
                if (stored is not null)
                {
                    _claimTerminalized = true;
                }

                return stored;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Retrying bulk completion persistence without request cancellation | CorrelationId: {CorrelationId}",
                    correlationId);
            }

            try
            {
                var stored = await _idempotencyService.TryCompleteAndVerifyAsync(
                    record,
                    aggregate,
                    CancellationToken.None);
                if (stored is not null)
                {
                    _claimTerminalized = true;
                }

                return stored;
            }
            catch (Exception retryException)
            {
                _logger.LogError(
                    retryException,
                    "Bulk completion persistence failed on retry | CorrelationId: {CorrelationId}",
                    correlationId);
                return null;
            }
        }
        finally
        {
            _claimStateGate.Release();
        }
    }

    private async Task<BulkItemResult> ProcessPreparedItemAsync(
        BulkOperationType operation,
        BulkPreparedItem item,
        string idempotencyKey,
        string correlationId,
        Guid tenantId,
        IReadOnlyDictionary<Guid, Product> currentProducts,
        CancellationToken cancellationToken)
    {
        if (item.State == BulkPreparedItemState.NotFound)
        {
            return await ReconcileCommittedMutationOrDefaultAsync(
                operation, idempotencyKey, item, tenantId, correlationId,
                BulkItemResult.NotFound(item.ProductId, item.ExpectedVersion),
                currentRowVersion: null,
                cancellationToken);
        }

        if (!currentProducts.TryGetValue(item.ProductId, out var product))
        {
            return await ReconcileCommittedMutationOrDefaultAsync(
                operation, idempotencyKey, item, tenantId, correlationId,
                BulkItemResult.NotFound(item.ProductId, item.ExpectedVersion),
                currentRowVersion: null,
                cancellationToken);
        }

        if (product.RowVersion != item.ExpectedVersion)
        {
            return await ReconcileCommittedMutationOrDefaultAsync(
                operation, idempotencyKey, item, tenantId, correlationId,
                BulkItemResult.ConcurrencyConflict(
                    item.ProductId, item.ExpectedVersion, product.RowVersion),
                currentRowVersion: product.RowVersion,
                cancellationToken);
        }

        if (operation == BulkOperationType.Activate && product.IsActive)
        {
            return BulkItemResult.AlreadyActive(
                item.ProductId, item.ExpectedVersion, product.RowVersion);
        }

        if (operation == BulkOperationType.Deactivate &&
            product is
            {
                IsActive: false,
                PublishStatus: PublishStatus.Archived,
                IsOnlineVisible: false
            })
        {
            return BulkItemResult.AlreadyInactive(
                item.ProductId, item.ExpectedVersion, product.RowVersion);
        }

        if (operation == BulkOperationType.Publish)
        {
            if (!product.IsActive)
            {
                return BulkItemResult.ProductInactive(item.ProductId, item.ExpectedVersion);
            }

            if (product is { PublishStatus: PublishStatus.Published, IsOnlineVisible: true })
            {
                return BulkItemResult.AlreadyPublished(
                    item.ProductId, item.ExpectedVersion, product.RowVersion);
            }
        }

        if (operation == BulkOperationType.Unpublish &&
            product is
            {
                IsOnlineVisible: false,
                PublishStatus: PublishStatus.Draft or PublishStatus.Published
            })
        {
            return BulkItemResult.AlreadyUnpublished(
                item.ProductId, item.ExpectedVersion, product.RowVersion);
        }

        if (operation == BulkOperationType.Delete)
        {
            var deletionValidation = await _validationService.CanDeleteProductAsync(
                item.ProductId,
                cancellationToken);
            if (!deletionValidation.IsValid)
            {
                return BulkItemResult.DependencyBlocked(
                    item.ProductId, item.ExpectedVersion, DependencyBlockedMessage);
            }
        }

        _context.Attach(product);
        var command = BuildLifecycleCommand(operation, product, item, idempotencyKey, correlationId);
        ProductAuthoringResult authoringResult;
        try
        {
            authoringResult = await _orchestrator.AuthorAsync(command, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (!_leaseLost)
        {
            _logger.LogWarning(
                exception,
                "Retrying ambiguous product authoring result | Operation: {Operation} | ProductId: {ProductId} | CorrelationId: {CorrelationId}",
                operation,
                item.ProductId,
                correlationId);
            _context.ChangeTracker.Clear();
            authoringResult = await _orchestrator.AuthorAsync(command, cancellationToken);
        }

        return await MapAuthoringResultAsync(authoringResult, operation, item, tenantId, cancellationToken);
    }

    /// <summary>
    /// Recovery reconciliation (P1-A). Before a preparation-derived NOT_FOUND
    /// or CONCURRENCY_CONFLICT result is classified, consult the durable
    /// per-product authoring idempotency record for this item's deterministic
    /// mutation identity. A durably completed accepted result proves the
    /// product mutation committed even though the original process died before
    /// the outer bulk claim completed, so the item is reported processed
    /// without invoking or mutating product authoring again. Any missing,
    /// incomplete, malformed, conflicting, or non-accepted record is not proof
    /// of a commit and the current-state classification stays authoritative.
    /// </summary>
    private async Task<BulkItemResult> ReconcileCommittedMutationOrDefaultAsync(
        BulkOperationType operation,
        string idempotencyKey,
        BulkPreparedItem item,
        Guid tenantId,
        string correlationId,
        BulkItemResult currentStateResult,
        uint? currentRowVersion,
        CancellationToken cancellationToken)
    {
        var committed = await _idempotencyService.TryResolveCommittedBulkItemMutationAsync(
            operation,
            idempotencyKey,
            tenantId,
            item.ProductId,
            cancellationToken);

        if (committed is null)
        {
            return currentStateResult;
        }

        _logger.LogInformation(
            "Recovered bulk item reflects its durably committed product mutation | Operation: {Operation} | ProductId: {ProductId} | CorrelationId: {CorrelationId}",
            operation,
            item.ProductId,
            correlationId);

        return BulkItemResult.Processed(
            item.ProductId,
            item.ExpectedVersion,
            currentRowVersion);
    }

    /// <summary>
    /// Request-cancellation reconciliation. Every prepared item without a
    /// result yet is resolved against the durable per-product authoring
    /// record for its deterministic mutation identity: a durably completed
    /// accepted result proves the mutation committed and is reported
    /// processed; anything else is reported as not processed due to
    /// cancellation. Results already produced are preserved verbatim. The
    /// truthful aggregate is then completed once under the claim gate/CAS
    /// with cancellation ignored, so retries replay it exactly. If completion
    /// cannot be proved, or the lease was lost, the claim stays in
    /// progress/recoverable and is never finalized as a failure.
    /// </summary>
    private async Task ReconcileCancelledExecutionAsync(
        BulkOperationType operation,
        ProductRequestIdempotency record,
        string idempotencyKey,
        BulkPreparationOutcome preparation,
        Guid tenantId,
        List<BulkItemResult> results)
    {
        try
        {
            if (!await TryMarkInterruptedClaimAsync(record))
            {
                _logger.LogWarning(
                    "Bulk request cancellation could not mark the owned claim interrupted; the claim stays recoverable | CorrelationId: {CorrelationId}",
                    preparation.CorrelationId);
                return;
            }

            var preparedItems = preparation.PreparedItems;
            for (var index = results.Count; index < preparedItems.Count; index++)
            {
                var item = preparedItems[index];
                results.Add(await ResolveCancelledItemAsync(
                    operation, idempotencyKey, tenantId, item));
            }

            var aggregate = _resultAggregator.Aggregate(
                preparation.Requested,
                preparation.Accepted,
                preparation.CorrelationId,
                results);

            var storedAggregate = await PersistCompletedAggregateAsync(
                record,
                aggregate,
                preparation.CorrelationId,
                CancellationToken.None);

            if (storedAggregate is null)
            {
                _logger.LogWarning(
                    "Bulk request cancellation could not durably prove aggregate completion; the claim stays recoverable | CorrelationId: {CorrelationId}",
                    preparation.CorrelationId);
            }
        }
        catch (Exception reconciliationException)
        {
            // Bounded best-effort reconciliation: the original cancellation
            // always propagates and the claim is left recoverable for retry.
            _logger.LogWarning(
                reconciliationException,
                "Bulk request cancellation reconciliation failed; the claim stays recoverable | CorrelationId: {CorrelationId}",
                preparation.CorrelationId);
        }
    }

    private async Task<bool> TryMarkInterruptedClaimAsync(ProductRequestIdempotency record)
    {
        await _claimStateGate.WaitAsync(CancellationToken.None);
        try
        {
            if (_leaseLost)
            {
                return false;
            }

            return await _idempotencyService.TryMarkInterruptedAsync(
                record,
                CancellationToken.None);
        }
        finally
        {
            _claimStateGate.Release();
        }
    }

    private async Task<InterruptedRecoveryOutcome> TryReconcileRecoveredInterruptedExecutionAsync(
        BulkOperationType operation,
        ProductRequestIdempotency record,
        string idempotencyKey,
        BulkPreparationOutcome preparation,
        Guid tenantId,
        bool forceReconciliation,
        bool inspectRecoveredClaim)
    {
        if (!forceReconciliation && !inspectRecoveredClaim)
        {
            return InterruptedRecoveryOutcome.NotApplicable;
        }

        try
        {
            var reconciled = new List<BulkItemResult>(preparation.Accepted);
            var committedMutationFound = false;
            foreach (var item in preparation.PreparedItems)
            {
                var committed = await _idempotencyService.TryResolveCommittedBulkItemMutationAsync(
                    operation,
                    idempotencyKey,
                    tenantId,
                    item.ProductId,
                    CancellationToken.None);

                if (committed is not null)
                {
                    committedMutationFound = true;
                    reconciled.Add(BulkItemResult.Processed(item.ProductId, item.ExpectedVersion));
                }
                else
                {
                    reconciled.Add(BulkItemResult.RequestCancelled(item.ProductId, item.ExpectedVersion));
                }
            }

            // Preserve Remediation-6F.2's resumable stale-claim behavior when
            // there is no durable evidence of mutation and no explicit
            // interruption marker. Once any item committed, an abandoned claim
            // is commit-ambiguous and must reconcile instead of starting the
            // remaining items.
            if (!forceReconciliation && !committedMutationFound)
            {
                return InterruptedRecoveryOutcome.NotApplicable;
            }

            var aggregate = _resultAggregator.Aggregate(
                preparation.Requested,
                preparation.Accepted,
                preparation.CorrelationId,
                reconciled);
            var stored = await PersistCompletedAggregateAsync(
                record,
                aggregate,
                preparation.CorrelationId,
                CancellationToken.None);
            return new InterruptedRecoveryOutcome(true, stored);
        }
        catch (Exception recoveryException)
        {
            _logger.LogWarning(
                recoveryException,
                "Interrupted bulk takeover reconciliation failed; the claim stays recoverable | CorrelationId: {CorrelationId}",
                preparation.CorrelationId);
            return new InterruptedRecoveryOutcome(true, null);
        }
    }

    private async Task<BulkItemResult> ResolveCancelledItemAsync(
        BulkOperationType operation,
        string idempotencyKey,
        Guid tenantId,
        BulkPreparedItem item)
    {
        var committed = await _idempotencyService.TryResolveCommittedBulkItemMutationAsync(
            operation,
            idempotencyKey,
            tenantId,
            item.ProductId,
            CancellationToken.None);

        if (committed is not null)
        {
            _logger.LogInformation(
                "Cancelled bulk item reflects its durably committed product mutation | Operation: {Operation} | ProductId: {ProductId}",
                operation,
                item.ProductId);
            return BulkItemResult.Processed(item.ProductId, item.ExpectedVersion);
        }

        return BulkItemResult.RequestCancelled(item.ProductId, item.ExpectedVersion);
    }

    private ProductAuthoringCommand BuildLifecycleCommand(
        BulkOperationType operation,
        Product product,
        BulkPreparedItem item,
        string idempotencyKey,
        string correlationId)
    {
        var perProductKey = BuildPerProductIdempotencyKey(operation, idempotencyKey, item.ProductId);

        if (operation == BulkOperationType.Delete)
        {
            var removeCommand = _commandMapper.FromLifecycleProduct(
                ProductAuthoringOperation.Remove, product);

            return removeCommand with
            {
                Metadata = removeCommand.Metadata with
                {
                    IdempotencyKey = perProductKey,
                    ExpectedVersion = item.ExpectedVersion,
                    CorrelationId = correlationId,
                    AdditionalMetadata = WithLifecycleMetadata(removeCommand.Metadata.AdditionalMetadata)
                }
            };
        }

        if (operation == BulkOperationType.Deactivate)
        {
            var baseCommand = _commandMapper.FromLifecycleProduct(
                ProductAuthoringOperation.Archive, product);

            return baseCommand with
            {
                Metadata = baseCommand.Metadata with
                {
                    IdempotencyKey = perProductKey,
                    ExpectedVersion = item.ExpectedVersion,
                    CorrelationId = correlationId,
                    AdditionalMetadata = WithLifecycleMetadata(baseCommand.Metadata.AdditionalMetadata)
                }
            };
        }

        if (operation == BulkOperationType.Unpublish)
        {
            var unpublishCommand = _commandMapper.FromLifecycleProduct(
                ProductAuthoringOperation.Update, product);

            return unpublishCommand with
            {
                Publication = new ProductAuthoringPublication(
                    PublishStatus.Draft,
                    IsActive: product.IsActive,
                    IsOnlineVisible: false,
                    IsSearchable: product.IsSearchable,
                    IsFeatured: product.IsFeatured),
                Metadata = unpublishCommand.Metadata with
                {
                    IdempotencyKey = perProductKey,
                    ExpectedVersion = item.ExpectedVersion,
                    CorrelationId = correlationId,
                    AdditionalMetadata = WithLifecycleMetadata(unpublishCommand.Metadata.AdditionalMetadata)
                }
            };
        }

        if (operation == BulkOperationType.Publish)
        {
            var publishCommand = _commandMapper.FromLifecycleProduct(
                ProductAuthoringOperation.Update, product);

            return publishCommand with
            {
                Publication = new ProductAuthoringPublication(
                    PublishStatus.Published,
                    IsActive: product.IsActive,
                    IsOnlineVisible: true,
                    IsSearchable: product.IsSearchable,
                    IsFeatured: product.IsFeatured),
                Metadata = publishCommand.Metadata with
                {
                    IdempotencyKey = perProductKey,
                    ExpectedVersion = item.ExpectedVersion,
                    CorrelationId = correlationId,
                    AdditionalMetadata = WithLifecycleMetadata(publishCommand.Metadata.AdditionalMetadata)
                }
            };
        }

        var targetPublishStatus = item.PublishStatus == PublishStatus.Published
            ? PublishStatus.Published
            : PublishStatus.Draft;

        var targetIsOnlineVisible = item.PublishStatus == PublishStatus.Published
            ? item.IsOnlineVisible!.Value
            : false;

        var activateCommand = _commandMapper.FromLifecycleProduct(
            ProductAuthoringOperation.Update, product);

        return activateCommand with
        {
            Publication = new ProductAuthoringPublication(
                targetPublishStatus,
                IsActive: true,
                IsOnlineVisible: targetIsOnlineVisible,
                IsSearchable: product.IsSearchable,
                IsFeatured: product.IsFeatured),
            Metadata = activateCommand.Metadata with
            {
                IdempotencyKey = perProductKey,
                ExpectedVersion = item.ExpectedVersion,
                CorrelationId = correlationId,
                AdditionalMetadata = WithLifecycleMetadata(activateCommand.Metadata.AdditionalMetadata)
            }
        };
    }

    private async Task<BulkItemResult> MapAuthoringResultAsync(
        ProductAuthoringResult authoringResult,
        BulkOperationType operation,
        BulkPreparedItem item,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (authoringResult.Status == ProductAuthoringStatus.Accepted)
        {
            var currentRowVersion = CurrentTrackedRowVersion(tenantId, item.ProductId);
            return BulkItemResult.Processed(
                item.ProductId,
                item.ExpectedVersion,
                currentRowVersion);
        }

        if (authoringResult.Status == ProductAuthoringStatus.ConcurrencyConflict)
        {
            var currentRowVersion = await CurrentDatabaseRowVersionAsync(
                tenantId, item.ProductId, cancellationToken);
            return BulkItemResult.ConcurrencyConflict(
                item.ProductId,
                item.ExpectedVersion,
                currentRowVersion ?? CurrentTrackedRowVersion(tenantId, item.ProductId) ?? item.ExpectedVersion);
        }

        if (authoringResult.Status == ProductAuthoringStatus.NotFound)
        {
            return BulkItemResult.NotFound(item.ProductId, item.ExpectedVersion);
        }

        if (authoringResult.Status == ProductAuthoringStatus.NotReadyForPublication &&
            operation == BulkOperationType.Publish)
        {
            var notReadyMessage = authoringResult.ValidationErrors.FirstOrDefault()?.Message
                ?? "Product is not ready for publication.";
            return BulkItemResult.PublicationNotReady(
                item.ProductId,
                item.ExpectedVersion,
                notReadyMessage);
        }

        if (authoringResult.Status is
            ProductAuthoringStatus.ValidationFailed or
            ProductAuthoringStatus.NotReadyForPublication or
            ProductAuthoringStatus.RejectedByProductTypePolicy)
        {
            var message = authoringResult.ValidationErrors.FirstOrDefault()?.Message
                ?? "Validation failed during product authoring.";
            return BulkItemResult.ValidationError(item.ProductId, item.ExpectedVersion, message);
        }

        var failureMessage = authoringResult.ValidationErrors.FirstOrDefault()?.Message
            ?? $"Product authoring did not complete ({authoringResult.Status}).";
        return BulkItemResult.AuthoringFailure(
            item.ProductId,
            item.ExpectedVersion,
            failureMessage);
    }

    private uint? CurrentTrackedRowVersion(Guid tenantId, Guid productId) =>
        _context.Products.Local
            .Where(product => product.TenantId == tenantId && product.Id == productId)
            .Select(product => (uint?)product.RowVersion)
            .SingleOrDefault();

    private Task<uint?> CurrentDatabaseRowVersionAsync(
        Guid tenantId,
        Guid productId,
        CancellationToken cancellationToken) =>
        _context.Products
            .AsNoTracking()
            .Where(product => product.TenantId == tenantId && product.Id == productId)
            .Select(product => (uint?)product.RowVersion)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<bool> CompleteFailedClaimAsync(
        ProductRequestIdempotency? record,
        Exception originalException)
    {
        if (record is null || record.Id == Guid.Empty || _leaseLost)
        {
            // A stale owner whose lease was taken over must never finalize the
            // recovered claim: the successor owns every subsequent transition.
            return false;
        }

        await _claimStateGate.WaitAsync(CancellationToken.None);
        try
        {
            if (_leaseLost)
            {
                return false;
            }

            // Durable direct update: immune to detached/tracked-entity no-ops,
            // and never leaves the claim stuck in progress because the original
            // entity instance was detached or already mutated.
            if (await _idempotencyService.FinalizeFailureAsync(
                record,
                FinalizedFailureMessage,
                CancellationToken.None))
            {
                _claimTerminalized = true;
                return true;
            }

            return false;
        }
        catch (Exception completionException)
        {
            _logger.LogError(
                completionException,
                "Failed to finalize interrupted bulk idempotency claim after {ExceptionType}",
                originalException.GetType().Name);
        }
        finally
        {
            _claimStateGate.Release();
        }

        return false;
    }

    private static IReadOnlyDictionary<string, string> WithLifecycleMetadata(
        IReadOnlyDictionary<string, string> source)
    {
        var metadata = source.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata["preserveImages"] = "true";
        metadata["preservePricingCompatibilityMirrors"] = "true";
        metadata["preloadedProduct"] = "true";
        metadata["stableOmittedCollectionsFingerprint"] = "true";
        return metadata;
    }

    private string BuildPerProductIdempotencyKey(
        BulkOperationType operation,
        string idempotencyKey,
        Guid productId) =>
        _idempotencyService.BuildPerProductIdempotencyKey(operation, idempotencyKey, productId);
}

internal sealed class BulkAggregateCompletionUnverifiedException(string message)
    : InvalidOperationException(message);

internal sealed record InterruptedRecoveryOutcome(
    bool ShouldComplete,
    BulkOperationResult? StoredAggregate)
{
    public static InterruptedRecoveryOutcome NotApplicable { get; } = new(false, null);
}
