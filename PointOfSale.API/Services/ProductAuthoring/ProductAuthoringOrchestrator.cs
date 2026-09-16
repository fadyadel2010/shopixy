using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.API.Services.Catalog;
using PointOfSale.Core.Application.ProductAuthoring;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Interfaces.ProductAuthoring;
using PointOfSale.Core.Pricing;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.Services.ProductAuthoring;

public sealed class ProductAuthoringOrchestrator : IProductAuthoringOrchestrator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private sealed record AuthoredVariantSyncResult(
        IReadOnlyList<ProductVariant> Created,
        IReadOnlyList<(ProductVariant Variant, IReadOnlyList<string> ChangedFields)> Updated,
        IReadOnlyList<ProductVariant> Deleted);

    /// <summary>
    /// Wave 2: deterministic variant-identity conflict detected before persistence.
    /// Mapped to <see cref="ProductAuthoringStatus.Conflict"/> with the same error
    /// codes the database classifier reports, so pre-commit and commit-time
    /// conflicts share one API contract.
    /// </summary>
    private sealed class ProductAuthoringVariantIdentityConflictException(
        string code,
        string field,
        string message)
        : InvalidOperationException(message)
    {
        public string Code { get; } = code;
        public string Field { get; } = field;
    }

    /// <summary>
    /// WAVE 2A: an authoring save (or Remove) attempted to destroy a variant or
    /// product that still has protected financial, inventory, operational, or
    /// historical references. Mapped to
    /// <see cref="ProductAuthoringStatus.ValidationFailed"/> with one error per
    /// blocker so the UI can explain WHY the operation was refused.
    /// Historical rows are never mutated by this path.
    /// </summary>
    private sealed class ProductAuthoringProtectedHistoryException(
        IReadOnlyList<ProductVariantDeleteBlocker> blockers)
        : InvalidOperationException(
            blockers.Count == 0
                ? "Protected history blocks this operation."
                : string.Join(" ", blockers.Select(blocker => blocker.MessageEn)))
    {
        public IReadOnlyList<ProductVariantDeleteBlocker> Blockers { get; } = blockers;
    }

    private readonly IProductAuthoringPolicy _policy;
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IProductVariantService _productVariantService;
    private readonly IEventPublisher _eventPublisher;
    private readonly IMarketplaceEventOutboxService _marketplaceEventOutbox;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IProductMutationIdempotencyService _productMutationIdempotency;
    private readonly IProductAuthoringTransactionObserver _transactionObserver;
    private readonly IProductAuthoringReconstructionService _reconstructionService;
    private readonly ILogger<ProductAuthoringOrchestrator> _logger;

    public ProductAuthoringOrchestrator(
        IProductAuthoringPolicy policy,
        ApplicationDbContext context,
        ITenantContext tenantContext,
        IProductVariantService productVariantService,
        IEventPublisher eventPublisher,
        IMarketplaceEventOutboxService marketplaceEventOutbox,
        ILogger<ProductAuthoringOrchestrator> logger,
        IHttpContextAccessor httpContextAccessor,
        IProductMutationIdempotencyService productMutationIdempotency,
        IProductAuthoringTransactionObserver transactionObserver,
        IProductAuthoringReconstructionService reconstructionService)
    {
        _policy = policy;
        _context = context;
        _tenantContext = tenantContext;
        _productVariantService = productVariantService;
        _eventPublisher = eventPublisher;
        _marketplaceEventOutbox = marketplaceEventOutbox;
        _httpContextAccessor = httpContextAccessor;
        _productMutationIdempotency = productMutationIdempotency;
        _transactionObserver = transactionObserver;
        _reconstructionService = reconstructionService;
        _logger = logger;
    }

    public async Task<ProductAuthoringResult> AuthorAsync(
        ProductAuthoringCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var idempotencyAdmission = RequireProductAuthoringIdempotency(command);
        if (idempotencyAdmission is not null)
        {
            return idempotencyAdmission;
        }

        if (cancellationToken.IsCancellationRequested &&
            command.Identity.ProductId is { } productId &&
            productId != Guid.Empty)
        {
            var completedResult = await TryResolveExistingProductAuthoringIdempotencyAsync(
                command,
                _tenantContext!.RequiredTenantId,
                productId,
                CancellationToken.None);
            if (completedResult is not null)
            {
                return completedResult;
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        command = await _reconstructionService.ReconstructForAdmissionAsync(command, cancellationToken);

        var admission = await AdmitAsync(command, cancellationToken);
        if (admission is not null)
        {
            return admission;
        }

        return command.Operation switch
        {
            ProductAuthoringOperation.Create => await ExecuteCreateAsync(command, cancellationToken),
            ProductAuthoringOperation.Update => await ExecuteUpdateAsync(command, cancellationToken),
            ProductAuthoringOperation.Archive => await ExecuteArchiveOrRemoveAsync(command, remove: false, cancellationToken),
            ProductAuthoringOperation.Remove => await ExecuteArchiveOrRemoveAsync(command, remove: true, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported product authoring operation passed admission.")
        };
    }

    private async Task<ProductAuthoringResult?> AdmitAsync(
        ProductAuthoringCommand command,
        CancellationToken cancellationToken)
    {
        var facts = await LoadAdmissionFactsAsync(command, cancellationToken);
        var decision = await _policy.EvaluateAdmissionAsync(command, facts, cancellationToken);
        return decision.IsAllowed ? null : command.ToResult(decision.Status, decision.Errors);
    }

    private async Task<ProductAuthoringAdmissionFacts> LoadAdmissionFactsAsync(
        ProductAuthoringCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Operation is not (ProductAuthoringOperation.Create or ProductAuthoringOperation.Update) ||
            command.Identity.CategoryId == Guid.Empty)
        {
            return new ProductAuthoringAdmissionFacts(CategoryIsLive: false);
        }

        var category = await CategoryLineageAuthority.RequireLiveCategoryAsync(
            _context,
            _tenantContext.RequiredTenantId,
            command.Identity.CategoryId,
            cancellationToken);

        return new ProductAuthoringAdmissionFacts(CategoryIsLive: category is not null);
    }

    private static ProductAuthoringResult? RequireProductAuthoringIdempotency(ProductAuthoringCommand command)
    {
        if (!string.IsNullOrWhiteSpace(command.Metadata.IdempotencyKey))
        {
            return null;
        }

        return command.ToResult(
            ProductAuthoringStatus.ValidationFailed,
            [new ProductAuthoringValidationError(
                "PRODUCT_AUTHORING_IDEMPOTENCY_KEY_REQUIRED",
                "idempotencyKey",
                "Idempotency-Key is required for every product authoring mutation.")]);
    }

    private async Task<ProductAuthoringResult> ExecuteCreateAsync(
        ProductAuthoringCommand command,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext!.RequiredTenantId;
        var productId = command.Identity.ProductId ?? DeterministicProductId(tenantId, command.Metadata.IdempotencyKey);
        await using var slugTransaction = await BeginSlugAuthorityTransactionAsync(tenantId, cancellationToken);

        // WAVE 1 slug authority: resolved BEFORE the idempotency fingerprint is
        // taken, so replays of the same command converge on the same slug.
        // The deterministic product id is excluded from collision checks, matching
        // SKU resolution semantics for same-key retries.
        command = command with
        {
            Seo = command.Seo with
            {
                Slug = await ResolveTenantUniqueSlugAsync(
                    tenantId,
                    productId,
                    command.Seo.Slug,
                    command.Content.Name,
                    excludeProductId: productId,
                    cancellationToken)
            }
        };

        var existingIdempotencyResult = await TryResolveExistingProductAuthoringIdempotencyAsync(
            command,
            tenantId,
            productId,
            cancellationToken);
        if (existingIdempotencyResult is not null)
        {
            return existingIdempotencyResult;
        }

        var existingProduct = await _context!.Products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(product => product.TenantId == tenantId && product.Id == productId, cancellationToken);

        if (existingProduct is not null)
        {
            return ProductAuthoringResult.Accepted(
                existingProduct.Id,
                existingProduct.PublishStatus,
                command.Metadata.CorrelationId,
                existingProduct.ProjectionMutationVersion);
        }

        var barcodeIsOwnedByAnotherSemanticEntity = !command.Inventory.HasVariants &&
            await ProductVariantCloneImportAuthority.IsBarcodeOwnedByAnotherSemanticEntityAsync(
                    _context,
                    tenantId,
                    command.Identity.Barcode,
                    productId,
                    Guid.Empty,
                    cancellationToken);
        var identityDecision = await _policy.EvaluateSimpleProductIdentityAsync(
            command,
            new ProductAuthoringSimpleProductIdentityFacts(barcodeIsOwnedByAnotherSemanticEntity),
            cancellationToken);
        if (!identityDecision.IsAllowed)
        {
            return command.ToResult(identityDecision.Status, identityDecision.Errors);
        }

        var resolvedSimpleSku = command.Inventory.HasVariants
            ? null
            : await ProductVariantCloneImportAuthority.ResolveUniqueSkuAsync(
                _context,
                tenantId,
                command.Identity.Sku,
                command.Identity.Barcode,
                productId,
                Guid.Empty,
                cancellationToken);

        var authoredUnits = NormalizeAuthoredProductUnits(command.Units.Items);
        var unitLookup = await LoadTenantUnitLookupAsync(tenantId, authoredUnits, cancellationToken);
        var unitDecision = await _policy.EvaluateUnitReferencesAsync(
            command,
            new ProductAuthoringUnitReferenceFacts(
                AllAuthoredUnitsReferenceActiveTenantUnits: unitLookup.Count == authoredUnits.Select(unit => unit.UnitId).Distinct().Count()),
            cancellationToken);
        if (!unitDecision.IsAllowed)
        {
            return command.ToResult(unitDecision.Status, unitDecision.Errors);
        }

        try
        {
            // WAVE 2 SKU integrity: matrix SKUs/barcodes/combinations are validated
            // server-side before persistence with the same conflict contract the
            // database classifier reports. Keep these checks inside the conflict
            // mapping boundary so known create conflicts return 409 rather than 500.
            ThrowOnDuplicateAuthoredVariantIdentities(command.Variants);
            await ThrowOnOwnedAuthoredVariantIdentitiesForCreateAsync(
                tenantId,
                productId,
                command.Variants,
                cancellationToken);

            var product = BuildProduct(command, tenantId, productId, resolvedSimpleSku);

            return await CommitProductAuthoringTransactionAsync(
                command,
                tenantId,
                product,
                async () =>
                {
                    _context.Products.Add(product);
                    ApplyAuthoredProductUnits(product, authoredUnits, unitLookup);
                    await _transactionObserver.AfterUnitSynchronizationAsync(command, product, cancellationToken);
                    var authoredVariantSync = ApplyAuthoredVariants(product, command.Variants);
                    var defaultVariantSync = await _productVariantService!.EnsureDefaultVariantAlignedAsync(product, cancellationToken);
                    defaultVariantSync = ApplySimpleProductOnlinePrice(command, product, defaultVariantSync);
                    ProductVariantCloneImportAuthority.ApplySimpleProductIdentityFromDefaultVariant(product, defaultVariantSync.Variant);
                    ApplyProductPricingCompatibilityMirror(product);
                    await _transactionObserver.AfterVariantSynchronizationAsync(command, product, cancellationToken);

                    await StageAuthoritativeOutboxAsync(
                        command,
                        product,
                        previousPublishStatus: null,
                        previousIsOnlineVisible: null,
                        defaultVariantSync,
                        authoredVariantSync,
                        wasArchivedBeforeMutation: false,
                        remove: false,
                        liveVariants: Array.Empty<ProductVariant>(),
                        cancellationToken);
                },
                cancellationToken,
                existingTransaction: slugTransaction);
        }
        catch (ProductAuthoringVariantIdentityConflictException exception)
        {
            _context!.ChangeTracker.Clear();
            return command.ToResult(
                ProductAuthoringStatus.Conflict,
                [new ProductAuthoringValidationError(exception.Code, exception.Field, exception.Message)]);
        }
    }

    private async Task<ProductAuthoringResult> ExecuteUpdateAsync(
        ProductAuthoringCommand command,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext!.RequiredTenantId;
        var product = await LoadTrackedProductAsync(
            command.Identity.ProductId,
            tenantId,
            MetadataFlag(command, "preloadedProduct"),
            cancellationToken);
        var lifecycleDecision = await _policy.EvaluateLifecycleAsync(
            command,
            new ProductAuthoringLifecycleFacts(ProductExists: product is not null),
            cancellationToken);
        if (!lifecycleDecision.IsAllowed)
        {
            return command.ToResult(lifecycleDecision.Status, lifecycleDecision.Errors);
        }

        product = product ?? throw new InvalidOperationException("Product authoring lifecycle admitted a missing product.");
        await using var slugTransaction = await BeginSlugAuthorityTransactionAsync(tenantId, cancellationToken);

        // WAVE 1 slug stability: resolved BEFORE the idempotency fingerprint is
        // taken so replays converge. A blank slug preserves the stored slug
        // (renaming a product never rewrites its public URL); a missing stored
        // slug is derived deterministically instead of persisting a blank value.
        var preservedSlug = ProductSlugAuthority.Normalize(command.Seo.Slug) ?? product.Slug;
        command = command with
        {
            Seo = command.Seo with
            {
                Slug = await ResolveTenantUniqueSlugAsync(
                    tenantId,
                    product.Id,
                    preservedSlug,
                    command.Content.Name,
                    excludeProductId: product.Id,
                    cancellationToken)
            }
        };

        var existingIdempotencyResult = await TryResolveExistingProductAuthoringIdempotencyAsync(
            command,
            tenantId,
            product.Id,
            cancellationToken);
        if (existingIdempotencyResult is not null)
        {
            return existingIdempotencyResult;
        }

        LogRuntimeRowVersionTraceBeforeIsStale(command, product);
        if (IsStale(command, product))
        {
            return command.ToResult(
                ProductAuthoringStatus.ConcurrencyConflict,
                [new ProductAuthoringValidationError("PRODUCT_EXPECTED_VERSION_STALE", "expectedVersion", "Product has been changed by a newer authoring command.")]);
        }

        var currentDefaultVariantId = command.Inventory.HasVariants
            ? Guid.Empty
            : await _context!.ProductVariants
                .WhereCurrentSemanticOwner()
                .Where(variant => variant.TenantId == tenantId && variant.ProductId == product.Id && variant.IsDefault)
                .Select(variant => variant.Id)
                .FirstOrDefaultAsync(cancellationToken);

        var barcodeIsOwnedByAnotherSemanticEntity = !command.Inventory.HasVariants &&
            await ProductVariantCloneImportAuthority.IsBarcodeOwnedByAnotherSemanticEntityAsync(
                    _context!,
                    tenantId,
                    command.Identity.Barcode,
                    product.Id,
                    currentDefaultVariantId,
                    cancellationToken);
        var identityDecision = await _policy.EvaluateSimpleProductIdentityAsync(
            command,
            new ProductAuthoringSimpleProductIdentityFacts(barcodeIsOwnedByAnotherSemanticEntity),
            cancellationToken);
        if (!identityDecision.IsAllowed)
        {
            return command.ToResult(identityDecision.Status, identityDecision.Errors);
        }

        var resolvedSimpleSku = command.Inventory.HasVariants
            ? null
            : await ProductVariantCloneImportAuthority.ResolveUniqueSkuAsync(
                _context!,
                tenantId,
                command.Identity.Sku,
                command.Identity.Barcode,
                product.Id,
                currentDefaultVariantId,
                cancellationToken);

        // Product type is immutable after creation. Generic product updates do
        // not implement the unit, variant, SKU, stock, and price migration that
        // a safe conversion would require.
        if (command.Inventory.HasVariants != product.HasVariants)
        {
            return command.ToResult(
                ProductAuthoringStatus.ValidationFailed,
                [new ProductAuthoringValidationError(
                    "PRODUCT_TYPE_CONVERSION_UNSUPPORTED",
                    "hasVariants",
                    "Product type cannot be changed after creation. Create a new product instead.")]);
        }

        var previousPublishStatus = product.PublishStatus;
        var previousIsOnlineVisible = product.IsOnlineVisible;
        var wasArchivedBeforeMutation = !product.IsActive || product.PublishStatus == PublishStatus.Archived;
        var beforeAuditData = BuildProductAuthoringAuditData(command, product, null);
        var shouldSyncUnits = MetadataFlag(command, "productUnitsSubmitted");
        var authoredUnits = NormalizeAuthoredProductUnits(command.Units.Items);
        var unitLookup = shouldSyncUnits
            ? await LoadTenantUnitLookupAsync(tenantId, authoredUnits, cancellationToken)
            : new Dictionary<Guid, Unit>();

        var unitDecision = await _policy.EvaluateUnitReferencesAsync(
            command,
            new ProductAuthoringUnitReferenceFacts(
                AllAuthoredUnitsReferenceActiveTenantUnits: !shouldSyncUnits ||
                    unitLookup.Count == authoredUnits.Select(unit => unit.UnitId).Distinct().Count()),
            cancellationToken);
        if (!unitDecision.IsAllowed)
        {
            return command.ToResult(unitDecision.Status, unitDecision.Errors);
        }

        try
        {
            return await CommitProductAuthoringTransactionAsync(
                command,
                tenantId,
                product,
                async () =>
                {
                    ApplyProduct(command, product, resolvedSimpleSku);

                    if (shouldSyncUnits)
                    {
                        await SyncAuthoredProductUnitsAsync(product, authoredUnits, unitLookup, cancellationToken);
                        await _transactionObserver.AfterUnitSynchronizationAsync(command, product, cancellationToken);
                    }

                    var authoredVariantSync = MetadataFlag(command, "variantsSubmitted")
                        ? await SyncAuthoredVariantsAsync(product, command.Variants, cancellationToken)
                        : EmptyVariantSync();
                    await _transactionObserver.AfterVariantSynchronizationAsync(command, product, cancellationToken);
                    var defaultVariantSync = await _productVariantService!.EnsureDefaultVariantAlignedAsync(product, cancellationToken);
                    defaultVariantSync = ApplySimpleProductOnlinePrice(command, product, defaultVariantSync);
                    ProductVariantCloneImportAuthority.ApplySimpleProductIdentityFromDefaultVariant(product, defaultVariantSync.Variant);
                    ApplyProductPricingCompatibilityMirror(product);

                    await StageAuthoritativeOutboxAsync(
                        command,
                        product,
                        previousPublishStatus,
                        previousIsOnlineVisible,
                        defaultVariantSync,
                        authoredVariantSync,
                        wasArchivedBeforeMutation,
                        remove: false,
                        liveVariants: Array.Empty<ProductVariant>(),
                        cancellationToken);
                },
                cancellationToken,
                beforeAuditData,
                slugTransaction);
        }
        catch (ProductAuthoringVariantIdentityConflictException exception)
        {
            _context!.ChangeTracker.Clear();
            return command.ToResult(
                ProductAuthoringStatus.Conflict,
                [new ProductAuthoringValidationError(exception.Code, exception.Field, exception.Message)]);
        }
        catch (ProductAuthoringProtectedHistoryException exception)
        {
            _context!.ChangeTracker.Clear();
            return command.ToResult(
                ProductAuthoringStatus.ValidationFailed,
                exception.Blockers
                    .Select(blocker => new ProductAuthoringValidationError(blocker.Code, "variants", blocker.MessageEn))
                    .ToList());
        }
    }

    private async Task<ProductAuthoringResult> ExecuteArchiveOrRemoveAsync(
        ProductAuthoringCommand command,
        bool remove,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext!.RequiredTenantId;
        var productId = command.Identity.ProductId;
        if (productId.HasValue && productId.Value != Guid.Empty)
        {
            // Remove makes the product invisible to global filters. Resolve its
            // committed idempotency record first so ambiguous retries stay exact.
            var existingIdempotencyResult = await TryResolveExistingProductAuthoringIdempotencyAsync(
                command,
                tenantId,
                productId.Value,
                cancellationToken);
            if (existingIdempotencyResult is not null)
            {
                return existingIdempotencyResult;
            }
        }

        var product = await LoadTrackedProductAsync(
            productId,
            tenantId,
            MetadataFlag(command, "preloadedProduct"),
            cancellationToken);
        var lifecycleDecision = await _policy.EvaluateLifecycleAsync(
            command,
            new ProductAuthoringLifecycleFacts(ProductExists: product is not null),
            cancellationToken);
        if (!lifecycleDecision.IsAllowed)
        {
            return command.ToResult(lifecycleDecision.Status, lifecycleDecision.Errors);
        }

        product = product ?? throw new InvalidOperationException("Product authoring lifecycle admitted a missing product.");
        LogRuntimeRowVersionTraceBeforeIsStale(command, product);
        if (IsStale(command, product))
        {
            return command.ToResult(
                ProductAuthoringStatus.ConcurrencyConflict,
                [new ProductAuthoringValidationError("PRODUCT_EXPECTED_VERSION_STALE", "expectedVersion", "Product has been changed by a newer authoring command.")]);
        }

        try
        {
            if (remove)
            {
                // WAVE 2A: defense in depth. Controllers and bulk already
                // pre-validate, but direct orchestrator callers must not bypass
                // historical protection. A second check runs inside the
                // transaction below to narrow the check-then-mutate race.
                await ThrowOnProtectedProductRemoveAsync(product.Id, tenantId, cancellationToken);
            }

            var liveVariants = await _context!.ProductVariants
                .Where(variant => variant.TenantId == tenantId && variant.ProductId == product.Id && !variant.IsDeleted)
                .ToListAsync(cancellationToken);

            var previousPublishStatus = product.PublishStatus;
            var previousIsOnlineVisible = product.IsOnlineVisible;
            var wasArchivedBeforeMutation = !product.IsActive || product.PublishStatus == PublishStatus.Archived;
            var beforeAuditData = BuildProductAuthoringAuditData(command, product, null);

            return await CommitProductAuthoringTransactionAsync(
                command,
                tenantId,
                product,
                async () =>
                {
                    if (remove)
                    {
                        // WAVE 2A: re-validate inside the authoring transaction
                        // so history committed between admission and this commit
                        // is observed before any IsDeleted mutation.
                        await ThrowOnProtectedProductRemoveAsync(product.Id, tenantId, cancellationToken);
                    }

                    product.IsActive = false;
                    product.IsOnlineVisible = false;
                    product.PublishStatus = PublishStatus.Archived;
                    product.UpdatedAt = DateTime.UtcNow;

                    if (remove)
                    {
                        product.IsDeleted = true;
                    }

                    foreach (var variant in liveVariants)
                    {
                        if (remove)
                        {
                            variant.IsActive = false;
                            variant.IsDeleted = true;
                            variant.UpdatedAt = product.UpdatedAt;
                        }
                    }

                    await StageAuthoritativeOutboxAsync(
                        command,
                        product,
                        previousPublishStatus,
                        previousIsOnlineVisible,
                        defaultVariantSync: null,
                        authoredVariantSync: EmptyVariantSync(),
                        wasArchivedBeforeMutation,
                        remove,
                        liveVariants,
                        cancellationToken);
                },
                cancellationToken,
                beforeAuditData);
        }
        catch (ProductAuthoringProtectedHistoryException exception)
        {
            // The owned transaction (if any) rolls back on dispose; nothing
            // was mutated before the guard threw, and tracked state is
            // cleared so no partial delete can be flushed later.
            _context!.ChangeTracker.Clear();
            return command.ToResult(
                ProductAuthoringStatus.ValidationFailed,
                exception.Blockers
                    .Select(blocker => new ProductAuthoringValidationError(blocker.Code, "product", blocker.MessageEn))
                    .ToList());
        }
    }

    private async Task<ProductAuthoringResult> CommitProductAuthoringTransactionAsync(
        ProductAuthoringCommand command,
        Guid tenantId,
        Product product,
        Func<Task> mutateAndStageAsync,
        CancellationToken cancellationToken,
        object? beforeAuditData = null,
        IDbContextTransaction? existingTransaction = null)
    {
        await using var ownedTransaction = existingTransaction is null
            ? await _context!.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var transaction = existingTransaction ?? ownedTransaction!;
        var (idempotencyRecord, idempotencyResult) = await TryClaimProductAuthoringIdempotencyAsync(
            command,
            tenantId,
            product.Id,
            cancellationToken);
        if (idempotencyResult is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return idempotencyResult;
        }

        await mutateAndStageAsync();
        ApplyExpectedProductRowVersion(command, product);
        LogRuntimeRowVersionTraceBeforeSaveChanges(command, product);

        var acceptedResult = ProductAuthoringResult.Accepted(
            product.Id,
            product.PublishStatus,
            command.Metadata.CorrelationId,
            PendingProductMutationVersion(product));
        CompleteProductAuthoringIdempotency(idempotencyRecord, acceptedResult);
        StageProductAuthoringAudit(command, tenantId, product, beforeAuditData, acceptedResult);

        try
        {
            await _transactionObserver.BeforeCommitAsync(command, product, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await _transactionObserver.AfterCommitAsync(command, product, cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            LogRuntimeRowVersionTraceConcurrencyException(command, product);
            await LogDbUpdateConcurrencyExceptionEntriesAsync(exception, cancellationToken);
            await transaction.RollbackAsync(cancellationToken);
            return command.ToResult(
                ProductAuthoringStatus.ConcurrencyConflict,
                [new ProductAuthoringValidationError("PRODUCT_CONCURRENCY_CONFLICT", "expectedVersion", "Product has been changed by another transaction.")]);
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            if (ProductAuthoringDatabaseConflictClassifier.TryClassify(exception, out var conflict))
            {
                _logger.LogWarning(
                    exception,
                    "Product Authoring uniqueness conflict | Operation: {Operation} | ProductId: {ProductId} | ErrorCode: {ErrorCode} | CorrelationId: {CorrelationId}",
                    command.Operation,
                    product.Id,
                    conflict.Code,
                    command.Metadata.CorrelationId);
                _context.ChangeTracker.Clear();
                return command.ToResult(ProductAuthoringStatus.Conflict, [conflict]);
            }

            _logger.LogError(
                exception,
                "Product Authoring database commit failed | Operation: {Operation} | ProductId: {ProductId} | CorrelationId: {CorrelationId}",
                command.Operation,
                product.Id,
                command.Metadata.CorrelationId);
            throw;
        }

        _logger.LogInformation(
            "Product Authoring committed | Operation: {Operation} | ProductId: {ProductId} | Version: {Version} | CorrelationId: {CorrelationId}",
            command.Operation,
            product.Id,
            acceptedResult.MutationVersion,
            command.Metadata.CorrelationId);

        return acceptedResult;
    }

    private void StageProductAuthoringAudit(
        ProductAuthoringCommand command,
        Guid tenantId,
        Product product,
        object? beforeAuditData,
        ProductAuthoringResult acceptedResult)
    {
        var httpContext = _httpContextAccessor?.HttpContext;
        var user = httpContext?.User;
        var userIdClaim = user?.FindFirstValue(ClaimTypes.NameIdentifier);

        _context!.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            TimestampUtc = DateTime.UtcNow,
            UserId = Guid.TryParse(userIdClaim, out var userId) ? userId : null,
            UserNameSnapshot = user?.Identity?.Name,
            ActionType = ProductAuthoringAuditAction(command.Operation),
            EntityType = "Product",
            EntityId = product.Id.ToString(),
            Description = $"{ProductAuthoringAuditAction(command.Operation)} product {product.Name}",
            BeforeDataJson = beforeAuditData is null ? null : JsonSerializer.Serialize(beforeAuditData, JsonOptions),
            AfterDataJson = JsonSerializer.Serialize(
                BuildProductAuthoringAuditData(command, product, acceptedResult),
                JsonOptions),
            IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString(),
            UserAgent = httpContext?.Request.Headers["User-Agent"].ToString(),
            ReferenceNumber = product.Id.ToString(),
            Source = "ProductAuthoring",
            SuccessFlag = true
        });
    }

    private static object BuildProductAuthoringAuditData(
        ProductAuthoringCommand command,
        Product product,
        ProductAuthoringResult? result) =>
        new
        {
            command.Metadata.CorrelationId,
            command.Metadata.ExpectedVersion,
            command.Operation,
            ProductId = product.Id,
            product.Name,
            product.SKU,
            product.Barcode,
            product.CategoryId,
            product.PublishStatus,
            product.IsActive,
            product.IsOnlineVisible,
            product.HasVariants,
            LegacyProductPriceFieldsAreCompatibilityMirrors = true,
            UnitCount = command.Units.Items.Count,
            command.Units.BaseUnitId,
            command.Units.DefaultUnitId,
            VariantCount = command.Variants.Count,
            DefaultVariantId = command.Variants.FirstOrDefault(variant => variant.IsDefault)?.VariantId,
            result?.MutationVersion
        };

    private static string ProductAuthoringAuditAction(ProductAuthoringOperation operation) =>
        operation switch
        {
            ProductAuthoringOperation.Create => "Create",
            ProductAuthoringOperation.Update => "Update",
            ProductAuthoringOperation.Archive => "Archive",
            ProductAuthoringOperation.Remove => "Delete",
            _ => "Author"
        };

    private void LogProjectionEventTraceStageAuthoritativeOutbox(
        IntegrationEvent @event,
        Product product)
    {
        _logger.LogInformation(
            "PROJECTION_EVENT_TRACE | Stage=StageAuthoritativeOutboxAsync | EventType={EventType} | ProductId={ProductId} | EventId={EventId} | ProjectionMutationVersion={ProjectionMutationVersion} | CorrelationId={CorrelationId} | Timestamp={Timestamp:O}",
            @event.EventType,
            product.Id,
            @event.EventId,
            product.ProjectionMutationVersion,
            @event.CorrelationId,
            DateTime.UtcNow);
    }

    private async Task StageAuthoritativeOutboxAsync(
        ProductAuthoringCommand command,
        Product product,
        PublishStatus? previousPublishStatus,
        bool? previousIsOnlineVisible,
        DefaultVariantSyncResult? defaultVariantSync,
        AuthoredVariantSyncResult authoredVariantSync,
        bool wasArchivedBeforeMutation,
        bool remove,
        IReadOnlyList<ProductVariant> liveVariants,
        CancellationToken cancellationToken)
    {
        var eventVersion = ProductEventVersionHelper.ToProductEventVersion(
            PendingProductMutationVersion(product));
        var unitData = await BuildProductUnitEventDataAsync(product, cancellationToken);
        var defaultUnitData = unitData.FirstOrDefault(unit => unit.UnitId == command.Units.DefaultUnitId);
        var defaultUnit = ResolveTrackedDefaultProductUnit(product);
        var changedFields = ProductChangedFields();
        var idempotencyPrefix = BuildIdempotencyPrefix(command, product.Id);

        await _marketplaceEventOutbox!.EnqueueAsync(new PointOfSale.Core.Events.Marketplace.ProductUpdatedEvent
        {
            CorrelationId = command.Metadata.CorrelationId,
            EntityId = product.Id,
            AggregateRootId = product.Id,
            TenantId = product.TenantId,
            Version = eventVersion,
            ChangedFields = changedFields,
            Name = product.Name,
            Sku = product.SKU,
            Slug = product.Slug,
            SalePrice = defaultUnitData?.SalePrice ?? 0m,
            OnlinePrice = null,
            CategoryId = product.CategoryId,
            IsOnlineVisible = product.IsOnlineVisible,
            HasVariants = product.HasVariants
        }, cancellationToken);

        if (command.Operation == ProductAuthoringOperation.Create)
        {
            var productCreatedEvent = new ProductCreatedEvent
            {
                TenantId = product.TenantId,
                EventVersion = eventVersion,
                IdempotencyKey = $"{idempotencyPrefix}:product-created:{eventVersion}",
                CorrelationId = command.Metadata.CorrelationId,
                ProductId = product.Id,
                Name = product.Name,
                NameAr = product.NameAr,
                SKU = product.SKU,
                Barcode = product.Barcode,
                SalePrice = defaultUnitData?.SalePrice ?? 0m,
                OnlinePrice = null,
                PurchasePrice = CatalogPricingAuthority.ResolveSimplePurchasePrice(defaultUnit),
                CategoryId = product.CategoryId,
                Brand = product.Brand,
                Description = product.Description,
                DescriptionAr = product.DescriptionAr,
                MetaTitle = product.MetaTitle,
                MetaDescription = product.MetaDescription,
                Tags = command.Content.Tags.ToList(),
                Features = command.Content.Features.ToList(),
                Faqs = command.Content.Faqs.Select(MapFaqEventData).ToList(),
                AiGenerated = product.AiGenerated,
                AiMetadataJson = product.AiMetadataJson,
                Images = product.Images,
                IsOnlineVisible = product.IsOnlineVisible,
                IsSearchable = product.IsSearchable,
                IsFeatured = product.IsFeatured,
                Slug = product.Slug,
                ProductTemplate = product.ProductTemplate,
                DefaultUnit = defaultUnitData,
                Units = unitData,
                TrackStock = product.TrackStock,
                AllowBackorder = product.AllowBackorder,
                HasVariants = product.HasVariants,
                Weight = product.Weight
            };
            await _eventPublisher!.PublishAsync(productCreatedEvent, cancellationToken);
            LogProjectionEventTraceStageAuthoritativeOutbox(productCreatedEvent, product);

            if (defaultVariantSync is not null)
            {
                await _productVariantService!.PublishVariantCreatedEventAsync(
                    defaultVariantSync.Variant,
                    product,
                    cancellationToken);
            }

            foreach (var variant in authoredVariantSync.Created.Where(variant => defaultVariantSync?.Variant.Id != variant.Id))
            {
                await _productVariantService!.PublishVariantCreatedEventAsync(variant, product, cancellationToken);
            }

            return;
        }

        var productUpdatedEvent = new ProductUpdatedEvent
        {
            TenantId = product.TenantId,
            EventVersion = eventVersion,
            IdempotencyKey = $"{idempotencyPrefix}:product-updated:{eventVersion}",
            CorrelationId = command.Metadata.CorrelationId,
            ProductId = product.Id,
            ChangedFields = changedFields,
            Name = product.Name,
            NameAr = product.NameAr,
            SKU = product.SKU,
            Barcode = product.Barcode,
            SalePrice = defaultUnitData?.SalePrice ?? 0m,
            OnlinePrice = null,
            PurchasePrice = CatalogPricingAuthority.ResolveSimplePurchasePrice(defaultUnit),
            CategoryId = product.CategoryId,
            Brand = product.Brand,
            Description = product.Description,
            DescriptionAr = product.DescriptionAr,
            MetaTitle = product.MetaTitle,
            MetaDescription = product.MetaDescription,
            Tags = command.Content.Tags.ToList(),
            Features = command.Content.Features.ToList(),
            Faqs = command.Content.Faqs.Select(MapFaqEventData).ToList(),
            AiGenerated = product.AiGenerated,
            AiMetadataJson = product.AiMetadataJson,
            Images = product.Images,
            IsOnlineVisible = product.IsOnlineVisible,
            IsSearchable = product.IsSearchable,
            IsFeatured = product.IsFeatured,
            Slug = product.Slug,
            ProductTemplate = product.ProductTemplate,
            PublishStatus = product.PublishStatus.ToString(),
            DefaultUnit = defaultUnitData,
            Units = unitData,
            TrackStock = product.TrackStock,
            AllowBackorder = product.AllowBackorder,
            HasVariants = product.HasVariants,
            Weight = product.Weight
        };
        await _eventPublisher!.PublishAsync(productUpdatedEvent, cancellationToken);
        LogProjectionEventTraceStageAuthoritativeOutbox(productUpdatedEvent, product);

        if (previousPublishStatus.HasValue &&
            (previousPublishStatus.Value != product.PublishStatus ||
             previousIsOnlineVisible.GetValueOrDefault() != product.IsOnlineVisible))
        {
            var productPublishStatusChangedEvent = new ProductPublishStatusChangedEvent
            {
                TenantId = product.TenantId,
                EventVersion = eventVersion,
                IdempotencyKey = $"{idempotencyPrefix}:publish-status:{eventVersion}",
                CorrelationId = command.Metadata.CorrelationId,
                ProductId = product.Id,
                SKU = product.SKU,
                OldStatus = previousPublishStatus.Value.ToString(),
                NewStatus = product.PublishStatus.ToString(),
                IsOnlineVisible = product.IsOnlineVisible
            };
            await _eventPublisher.PublishAsync(productPublishStatusChangedEvent, cancellationToken);
            LogProjectionEventTraceStageAuthoritativeOutbox(productPublishStatusChangedEvent, product);
        }

        if (!wasArchivedBeforeMutation && (!product.IsActive || product.PublishStatus == PublishStatus.Archived))
        {
            var productArchivedEvent = new ProductArchivedEvent
            {
                TenantId = product.TenantId,
                EventVersion = eventVersion,
                IdempotencyKey = $"{idempotencyPrefix}:product-archived:{eventVersion}",
                CorrelationId = command.Metadata.CorrelationId,
                ProductId = product.Id
            };
            await _eventPublisher.PublishAsync(productArchivedEvent, cancellationToken);
            LogProjectionEventTraceStageAuthoritativeOutbox(productArchivedEvent, product);
        }

        if (defaultVariantSync is not null)
        {
            if (defaultVariantSync.WasCreated)
            {
                await _productVariantService!.PublishVariantCreatedEventAsync(
                    defaultVariantSync.Variant,
                    product,
                    cancellationToken);
            }
            else if (defaultVariantSync.ChangedFields.Count > 0)
            {
                var authoredChangedFields = authoredVariantSync.Updated
                    .Where(updated => updated.Variant.Id == defaultVariantSync.Variant.Id)
                    .Select(updated => updated.ChangedFields)
                    .FirstOrDefault();
                var combinedChangedFields = authoredChangedFields is null
                    ? defaultVariantSync.ChangedFields
                    : defaultVariantSync.ChangedFields.Concat(authoredChangedFields).Distinct(StringComparer.Ordinal).ToArray();

                await PublishDefaultVariantUpdatedAsync(
                    command,
                    defaultVariantSync.Variant,
                    combinedChangedFields,
                    idempotencyPrefix,
                    cancellationToken);
            }
        }

        foreach (var variant in authoredVariantSync.Created.Where(variant => defaultVariantSync?.Variant.Id != variant.Id))
        {
            await _productVariantService!.PublishVariantCreatedEventAsync(variant, product, cancellationToken);
        }

        foreach (var (variant, variantChangedFields) in authoredVariantSync.Updated)
        {
            if (defaultVariantSync?.Variant.Id == variant.Id &&
                defaultVariantSync.ChangedFields.Count > 0)
            {
                continue;
            }

            await PublishDefaultVariantUpdatedAsync(
                command,
                variant,
                variantChangedFields,
                idempotencyPrefix,
                cancellationToken);
        }

        foreach (var variant in authoredVariantSync.Deleted)
        {
            var variantEventVersion = ProductEventVersionHelper.ToProductEventVersion(
                PendingVariantMutationVersion(variant));

            await _eventPublisher.PublishAsync(new ProductVariantDeletedEvent
            {
                TenantId = product.TenantId,
                EventVersion = variantEventVersion,
                IdempotencyKey = $"{idempotencyPrefix}:variant-deleted:{variant.Id:N}:{variantEventVersion}",
                CorrelationId = command.Metadata.CorrelationId,
                VariantId = variant.Id,
                ProductId = product.Id,
                SKU = variant.SKU
            }, cancellationToken);

            await _eventPublisher.PublishAsync(new VariantDeletedEvent
            {
                TenantId = product.TenantId,
                EventVersion = variantEventVersion,
                IdempotencyKey = $"{idempotencyPrefix}:variant-retired:{variant.Id:N}:{variantEventVersion}",
                CorrelationId = command.Metadata.CorrelationId,
                ProductId = product.Id,
                VariantId = variant.Id
            }, cancellationToken);
        }

        if (!remove)
        {
            return;
        }

        var productDeletedEvent = new ProductDeletedEvent
        {
            TenantId = product.TenantId,
            EventVersion = eventVersion,
            IdempotencyKey = $"{idempotencyPrefix}:product-deleted:{eventVersion}",
            CorrelationId = command.Metadata.CorrelationId,
            ProductId = product.Id,
            SKU = product.SKU
        };
        await _eventPublisher.PublishAsync(productDeletedEvent, cancellationToken);
        LogProjectionEventTraceStageAuthoritativeOutbox(productDeletedEvent, product);

        foreach (var variant in liveVariants)
        {
            var variantEventVersion = ProductEventVersionHelper.ToProductEventVersion(
                PendingVariantMutationVersion(variant));

            await _eventPublisher.PublishAsync(new ProductVariantDeletedEvent
            {
                TenantId = product.TenantId,
                EventVersion = variantEventVersion,
                IdempotencyKey = $"{idempotencyPrefix}:variant-deleted:{variant.Id:N}:{variantEventVersion}",
                CorrelationId = command.Metadata.CorrelationId,
                VariantId = variant.Id,
                ProductId = product.Id,
                SKU = variant.SKU
            }, cancellationToken);

            await _eventPublisher.PublishAsync(new VariantDeletedEvent
            {
                TenantId = product.TenantId,
                EventVersion = variantEventVersion,
                IdempotencyKey = $"{idempotencyPrefix}:variant-retired:{variant.Id:N}:{variantEventVersion}",
                CorrelationId = command.Metadata.CorrelationId,
                ProductId = product.Id,
                VariantId = variant.Id
            }, cancellationToken);
        }
    }

    private async Task<ProductAuthoringResult?> TryResolveExistingProductAuthoringIdempotencyAsync(
        ProductAuthoringCommand command,
        Guid tenantId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Metadata.IdempotencyKey))
        {
            return null;
        }

        var key = NormalizeProductAuthoringIdempotencyKey(command.Metadata.IdempotencyKey);
        var operation = ProductAuthoringIdempotencyOperation(command.Operation);
        var requestFingerprint = BuildProductAuthoringRequestFingerprint(command);
        var existingRecord = await _productMutationIdempotency!.TryResolveAsync(
            tenantId,
            key,
            operation,
            cancellationToken);

        return existingRecord is null
            ? null
            : BuildProductAuthoringIdempotencyResult(existingRecord, requestFingerprint, command);
    }

    private async Task<(ProductRequestIdempotency? Record, ProductAuthoringResult? Result)>
        TryClaimProductAuthoringIdempotencyAsync(
            ProductAuthoringCommand command,
            Guid tenantId,
            Guid productId,
            CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Metadata.IdempotencyKey))
        {
            return (null, null);
        }

        var claim = await _productMutationIdempotency!.TryClaimAsync(
            new ProductMutationIdempotencyRequest(
                tenantId,
                NormalizeProductAuthoringIdempotencyKey(command.Metadata.IdempotencyKey),
                ProductAuthoringIdempotencyOperation(command.Operation),
                productId,
                null,
                BuildProductAuthoringRequestFingerprint(command)),
            cancellationToken);

        return claim.Replay is null
            ? (claim.Record, null)
            : (null, BuildProductAuthoringIdempotencyResult(claim.Replay, command));
    }

    private void CompleteProductAuthoringIdempotency(
        ProductRequestIdempotency? record,
        ProductAuthoringResult result)
    {
        _productMutationIdempotency!.Complete(
            record,
            result.Status switch
            {
                ProductAuthoringStatus.Accepted => StatusCodes.Status202Accepted,
                ProductAuthoringStatus.Conflict or ProductAuthoringStatus.ConcurrencyConflict => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            },
            result,
            result.ProductId);
    }

    private static ProductAuthoringResult BuildProductAuthoringIdempotencyResult(
        ProductMutationIdempotencyReplay replay,
        ProductAuthoringCommand command) =>
        replay.Kind switch
        {
            ProductMutationIdempotencyReplayKind.Conflict => command.ToResult(
                ProductAuthoringStatus.ValidationFailed,
                [new ProductAuthoringValidationError("PRODUCT_AUTHORING_IDEMPOTENCY_CONFLICT", "idempotencyKey", "Idempotency key was already used for a different product authoring command.")]),
            ProductMutationIdempotencyReplayKind.Completed when replay.Record is not null =>
                DeserializeProductAuthoringReplay(replay.Record, command),
            _ => ProductAuthoringIdempotencyInProgress(command)
        };

    private static ProductAuthoringResult BuildProductAuthoringIdempotencyResult(
        ProductRequestIdempotency record,
        string requestFingerprint,
        ProductAuthoringCommand command)
    {
        if (!string.Equals(record.RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
        {
            return command.ToResult(
                ProductAuthoringStatus.ValidationFailed,
                [new ProductAuthoringValidationError("PRODUCT_AUTHORING_IDEMPOTENCY_CONFLICT", "idempotencyKey", "Idempotency key was already used for a different product authoring command.")]);
        }

        if (!record.ResponseStatusCode.HasValue || string.IsNullOrWhiteSpace(record.ResponseJson))
        {
            return ProductAuthoringIdempotencyInProgress(command);
        }

        var storedResult = JsonSerializer.Deserialize<ProductAuthoringResult>(record.ResponseJson, JsonOptions);
        return storedResult ?? ProductAuthoringIdempotencyInProgress(command);
    }

    private static ProductAuthoringResult DeserializeProductAuthoringReplay(
        ProductRequestIdempotency record,
        ProductAuthoringCommand command)
    {
        if (string.IsNullOrWhiteSpace(record.ResponseJson))
        {
            return ProductAuthoringIdempotencyInProgress(command);
        }

        var storedResult = JsonSerializer.Deserialize<ProductAuthoringResult>(record.ResponseJson, JsonOptions);
        return storedResult ?? ProductAuthoringIdempotencyInProgress(command);
    }

    private static ProductAuthoringResult ProductAuthoringIdempotencyInProgress(
        ProductAuthoringCommand command) =>
        command.ToResult(
            ProductAuthoringStatus.ConcurrencyConflict,
            [new ProductAuthoringValidationError("PRODUCT_AUTHORING_IDEMPOTENCY_IN_PROGRESS", "idempotencyKey", "A product authoring command with the same idempotency key is already being processed.")]);

    private Product BuildProduct(
        ProductAuthoringCommand command,
        Guid tenantId,
        Guid productId,
        string? resolvedSimpleSku)
    {
        var product = new Product
        {
            Id = productId,
            TenantId = tenantId,
            CreatedAt = DateTime.UtcNow
        };

        ApplyProduct(command, product, resolvedSimpleSku);
        return product;
    }

    private static void ApplyProduct(
        ProductAuthoringCommand command,
        Product product,
        string? resolvedSimpleSku)
    {
        product.Name = command.Content.Name;
        product.NameAr = command.Content.NameAr;
        product.Barcode = command.Inventory.HasVariants ? null : command.Identity.Barcode;
        product.SKU = resolvedSimpleSku;
        product.Description = command.Content.Description;
        product.DescriptionAr = command.Content.DescriptionAr;
        product.CategoryId = command.Identity.CategoryId;
        if (!MetadataFlag(command, "preservePricingCompatibilityMirrors"))
        {
            product.PurchasePrice = 0m;
            product.SalePrice = 0m;
            product.OnlinePrice = null;
        }
        product.PriceIncludesTax = command.Pricing.PriceIncludesTax;
        product.TaxRate = command.Pricing.TaxRate;
        product.Unit = command.Inventory.Unit;
        product.MinimumStockLevel = command.Inventory.MinimumStockLevel;
        if (!MetadataFlag(command, "preserveImages"))
        {
            product.Images = null;
        }
        product.IsActive = command.Publication.IsActive;
        product.TrackStock = command.Inventory.TrackStock;
        product.HasVariants = command.Inventory.HasVariants;
        product.Slug = command.Seo.Slug;
        product.MetaTitle = command.Seo.MetaTitle;
        product.MetaDescription = command.Seo.MetaDescription;
        product.MetaKeywords = command.Seo.MetaKeywords;
        product.Weight = command.Shipping.Weight;
        product.Length = command.Shipping.Length;
        product.Width = command.Shipping.Width;
        product.Height = command.Shipping.Height;
        product.WeightUnit = command.Shipping.WeightUnit;
        product.DimensionUnit = command.Shipping.DimensionUnit;
        product.PublishStatus = command.Publication.PublishStatus;
        product.IsOnlineVisible = command.Publication.IsOnlineVisible;
        product.IsSearchable = command.Publication.IsSearchable;
        product.IsFeatured = command.Publication.IsFeatured;
        product.LowStockThreshold = command.Inventory.LowStockThreshold;
        product.AllowBackorder = command.Inventory.AllowBackorder;
        product.Brand = command.Content.Brand;
        product.Manufacturer = command.Content.Manufacturer;
        product.Tags = string.Join(",", command.Content.Tags);
        product.TagsJson = SerializeStringList(command.Content.Tags);
        product.FeaturesJson = SerializeStringList(command.Content.Features);
        product.FaqJson = SerializeFaqs(command.Content.Faqs);
        product.AiGenerated = command.Metadata.AiGenerated;
        product.AiMetadataJson = command.Metadata.AiMetadataJson;
        product.VideoUrl = command.Metadata.VideoUrl;
        product.ProductTemplate = command.Metadata.ProductTemplate;
        product.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// WAVE 1 slug authority: derives the deterministic base slug
    /// (<see cref="ProductSlugAuthority.GenerateBase"/>) and makes it unique
    /// within the tenant ("-2", "-3", ...) excluding the product being written.
    /// Comparison is case-insensitive because historical rows may predate
    /// lowercase normalization while reads case-fold.
    /// </summary>
    private async Task<string> ResolveTenantUniqueSlugAsync(
        Guid tenantId,
        Guid productId,
        string? requestedSlug,
        string? name,
        Guid? excludeProductId,
        CancellationToken cancellationToken)
    {
        var baseSlug = ProductSlugAuthority.GenerateBase(requestedSlug, name, productId);
        var collisionPrefix = baseSlug + "-";

        var taken = await _context.Products
            .AsNoTracking()
            .Where(product => product.TenantId == tenantId
                && product.Slug != null
                && (product.Slug.ToLower() == baseSlug || product.Slug.ToLower().StartsWith(collisionPrefix))
                && (excludeProductId == null || product.Id != excludeProductId.Value))
            .Select(product => product.Slug!)
            .ToListAsync(cancellationToken);

        var takenSet = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        return ProductSlugAuthority.EnsureUnique(baseSlug, takenSet.Contains);
    }

    private async Task<IDbContextTransaction?> BeginSlugAuthorityTransactionAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (!_context.Database.IsRelational())
        {
            return null;
        }

        var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        if (_context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"product-slug:{tenantId:N}"));
            var lockKey = BitConverter.ToInt64(hash, 0);
            await _context.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock({0});",
                new object[] { lockKey },
                cancellationToken);
        }

        return transaction;
    }

    private async Task<Product?> LoadTrackedProductAsync(
        Guid? productId,
        Guid tenantId,
        bool usePreloadedProduct,
        CancellationToken cancellationToken)
    {
        if (!productId.HasValue || productId.Value == Guid.Empty)
        {
            return null;
        }

        var trackedProduct = usePreloadedProduct
            ? _context!.Products.Local.FirstOrDefault(product =>
                product.TenantId == tenantId && product.Id == productId.Value)
            : null;

        return trackedProduct ?? await _context.Products
            .FirstOrDefaultAsync(product => product.TenantId == tenantId && product.Id == productId.Value, cancellationToken);
    }

    private async Task<Dictionary<Guid, Unit>> LoadTenantUnitLookupAsync(
        Guid tenantId,
        IReadOnlyList<ProductAuthoringUnit> units,
        CancellationToken cancellationToken)
    {
        var unitIds = units.Select(unit => unit.UnitId).Distinct().ToList();
        if (unitIds.Count == 0)
        {
            return new Dictionary<Guid, Unit>();
        }

        return await _context!.Units
            .Where(unit => unitIds.Contains(unit.Id) && unit.TenantId == tenantId && unit.IsActive)
            .ToDictionaryAsync(unit => unit.Id, cancellationToken);
    }

    private static List<ProductAuthoringUnit> NormalizeAuthoredProductUnits(
        IEnumerable<ProductAuthoringUnit> units) =>
        units
            .Where(unit => unit.UnitId != Guid.Empty)
            .GroupBy(unit => unit.UnitId)
            .Select(group => group.Last())
            .ToList();

    private static void ApplyAuthoredProductUnits(
        Product product,
        IReadOnlyList<ProductAuthoringUnit> authoredUnits,
        IReadOnlyDictionary<Guid, Unit> unitLookup)
    {
        foreach (var authoredUnit in authoredUnits)
        {
            product.ProductUnits.Add(new ProductUnit
            {
                Id = authoredUnit.ProductUnitId ?? Guid.NewGuid(),
                ProductId = product.Id,
                UnitId = authoredUnit.UnitId,
                ConversionFactor = authoredUnit.ConversionFactor,
                PurchasePrice = authoredUnit.PurchasePrice,
                SellingPrice = authoredUnit.SellingPrice,
                Barcode = authoredUnit.Barcode,
                IsDefault = authoredUnit.IsDefault,
                IsBaseUnit = authoredUnit.IsBaseUnit,
                Product = product,
                Unit = unitLookup[authoredUnit.UnitId]
            });
        }
    }

    private async Task SyncAuthoredProductUnitsAsync(
        Product product,
        IReadOnlyList<ProductAuthoringUnit> authoredUnits,
        IReadOnlyDictionary<Guid, Unit> unitLookup,
        CancellationToken cancellationToken)
    {
        var existingUnits = await _context!.ProductUnits
            .Include(productUnit => productUnit.Unit)
            .Where(productUnit => productUnit.ProductId == product.Id && !productUnit.IsDeleted)
            .ToListAsync(cancellationToken);
        var authoredByUnitId = authoredUnits.ToDictionary(unit => unit.UnitId);

        foreach (var existingUnit in existingUnits)
        {
            if (!authoredByUnitId.ContainsKey(existingUnit.UnitId))
            {
                existingUnit.IsDeleted = true;
            }
        }

        foreach (var authoredUnit in authoredUnits)
        {
            var existingUnit = existingUnits.FirstOrDefault(unit => unit.UnitId == authoredUnit.UnitId);
            if (existingUnit == null)
            {
                _context.ProductUnits.Add(new ProductUnit
                {
                    Id = authoredUnit.ProductUnitId ?? Guid.NewGuid(),
                    ProductId = product.Id,
                    UnitId = authoredUnit.UnitId,
                    ConversionFactor = authoredUnit.ConversionFactor,
                    PurchasePrice = authoredUnit.PurchasePrice,
                    SellingPrice = authoredUnit.SellingPrice,
                    Barcode = authoredUnit.Barcode,
                    IsDefault = authoredUnit.IsDefault,
                    IsBaseUnit = authoredUnit.IsBaseUnit,
                    Product = product,
                    Unit = unitLookup[authoredUnit.UnitId]
                });
                continue;
            }

            existingUnit.ConversionFactor = authoredUnit.ConversionFactor;
            existingUnit.PurchasePrice = authoredUnit.PurchasePrice;
            existingUnit.SellingPrice = authoredUnit.SellingPrice;
            existingUnit.Barcode = authoredUnit.Barcode;
            existingUnit.IsDefault = authoredUnit.IsDefault;
            existingUnit.IsBaseUnit = authoredUnit.IsBaseUnit;
            existingUnit.Unit = unitLookup[authoredUnit.UnitId];
        }
    }

    private async Task<List<ProductUnitData>> BuildProductUnitEventDataAsync(
        Product product,
        CancellationToken cancellationToken)
    {
        var trackedUnits = _context!.ChangeTracker
            .Entries<ProductUnit>()
            .Where(entry =>
                entry.State != EntityState.Deleted &&
                entry.State != EntityState.Detached &&
                entry.Entity.ProductId == product.Id &&
                !entry.Entity.IsDeleted)
            .Select(entry => entry.Entity)
            .ToList();

        if (trackedUnits.Count == 0 || trackedUnits.Any(productUnit => productUnit.Unit is null))
        {
            trackedUnits = await _context.ProductUnits
                .Include(productUnit => productUnit.Unit)
                .Where(productUnit => productUnit.ProductId == product.Id && !productUnit.IsDeleted)
                .ToListAsync(cancellationToken);
        }

        return trackedUnits
            .Where(productUnit => productUnit.Unit != null)
            .OrderByDescending(productUnit => productUnit.IsDefault)
            .ThenByDescending(productUnit => productUnit.IsBaseUnit)
            .ThenBy(productUnit => productUnit.Unit.Name)
            .ThenBy(productUnit => productUnit.Id)
            .Select(MapProductUnitData)
            .ToList();
    }

    private void ApplyProductPricingCompatibilityMirror(Product product)
    {
        if (product.HasVariants)
        {
            return;
        }

        var defaultUnit = ResolveTrackedDefaultProductUnit(product);
        if (ProductPricingCompatibilityMirror.MirrorSimpleProductFromDefaultUnit(product, defaultUnit).Count > 0)
        {
            product.UpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// WAVE 3 — persists the simple-product online price onto the simple
    /// default variant's <see cref="ProductVariant.OnlinePrice"/> (same convention
    /// as multi-variant products). The requested value comes from
    /// <c>command.Pricing.OnlinePrice</c> (the product authoring
    /// <c>onlinePrice</c> field). Null/zero clears the override so readers
    /// fall back to the POS price; negatives are rejected by
    /// validation before this point. POS truth (<c>Price</c>/<c>CostPrice</c>)
    /// is never touched here.
    /// </summary>
    private static DefaultVariantSyncResult ApplySimpleProductOnlinePrice(
        ProductAuthoringCommand command,
        Product product,
        DefaultVariantSyncResult defaultVariantSync)
    {
        if (product.HasVariants)
        {
            return defaultVariantSync;
        }

        var desired = CatalogPricingAuthority.NormalizeSimpleOnlinePrice(command.Pricing.OnlinePrice);
        var variant = defaultVariantSync.Variant;
        if (variant.OnlinePrice == desired)
        {
            return defaultVariantSync;
        }

        variant.OnlinePrice = desired;
        variant.UpdatedAt = DateTime.UtcNow;
        return defaultVariantSync with
        {
            ChangedFields = defaultVariantSync.ChangedFields
                .Concat([nameof(ProductVariant.OnlinePrice)])
                .Distinct(StringComparer.Ordinal)
                .ToArray()
        };
    }

    private ProductUnit? ResolveTrackedDefaultProductUnit(Product product)
    {
        var attachedDefaultUnit = product.ProductUnits
            .Where(unit => !unit.IsDeleted)
            .FirstOrDefault(unit => unit.IsDefault);
        if (attachedDefaultUnit is not null)
        {
            return attachedDefaultUnit;
        }

        return _context!.ChangeTracker
            .Entries<ProductUnit>()
            .Select(entry => entry.Entity)
            .Where(unit => unit.ProductId == product.Id && !unit.IsDeleted)
            .FirstOrDefault(unit => unit.IsDefault);
    }

    private async Task PublishDefaultVariantUpdatedAsync(
        ProductAuthoringCommand command,
        ProductVariant variant,
        IReadOnlyList<string> changedFields,
        string idempotencyPrefix,
        CancellationToken cancellationToken)
    {
        var eventVersion = ProductEventVersionHelper.ToProductEventVersion(
            PendingVariantMutationVersion(variant));

        await _eventPublisher!.PublishAsync(new ProductVariantUpdatedEvent
        {
            TenantId = _tenantContext!.RequiredTenantId,
            EventVersion = eventVersion,
            IdempotencyKey = $"{idempotencyPrefix}:variant-updated:{variant.Id:N}:{eventVersion}",
            CorrelationId = command.Metadata.CorrelationId,
            VariantId = variant.Id,
            ProductId = variant.ProductId,
            ChangedFields = changedFields.ToArray(),
            SKU = variant.SKU,
            Barcode = variant.Barcode,
            Price = variant.Price,
            OnlinePrice = variant.OnlinePrice,
            CostPrice = variant.CostPrice,
            IsDefault = variant.IsDefault,
            IsActive = variant.IsActive,
            MediaId = variant.MediaId,
            ImageUrl = variant.ImageUrl
        }, cancellationToken);

        if (!changedFields.Contains(nameof(ProductVariant.Price), StringComparer.Ordinal) &&
            !changedFields.Contains(nameof(ProductVariant.OnlinePrice), StringComparer.Ordinal))
        {
            return;
        }

        await _eventPublisher.PublishAsync(new VariantPriceUpdatedEvent
        {
            TenantId = _tenantContext.RequiredTenantId,
            EventVersion = eventVersion,
            IdempotencyKey = $"{idempotencyPrefix}:variant-price-updated:{variant.Id:N}:{eventVersion}",
            CorrelationId = command.Metadata.CorrelationId,
            ProductId = variant.ProductId,
            VariantId = variant.Id,
            NewPrice = CommerceHubPriceAuthority.ResolveBaseVariantPrice(variant.Price, variant.OnlinePrice)
        }, cancellationToken);
    }

    private static AuthoredVariantSyncResult ApplyAuthoredVariants(
        Product product,
        IReadOnlyList<ProductAuthoringVariant> authoredVariants)
    {
        if (authoredVariants.Count == 0)
        {
            return EmptyVariantSync();
        }

        var created = new List<ProductVariant>();
        var sortOrder = 0;
        foreach (var authoredVariant in authoredVariants)
        {
            var variant = new ProductVariant
            {
                // New variant identity is always server-owned. Client temporary IDs must
                // never become authoritative aggregate identity.
                Id = Guid.NewGuid(),
                TenantId = product.TenantId,
                ProductId = product.Id,
                SKU = authoredVariant.Sku,
                Barcode = authoredVariant.Barcode,
                Price = authoredVariant.Price,
                OnlinePrice = authoredVariant.OnlinePrice,
                CostPrice = authoredVariant.CostPrice,
                Weight = authoredVariant.Weight,
                MediaId = authoredVariant.MediaId,
                ImageUrl = authoredVariant.MediaId.HasValue ? null : authoredVariant.ImageUrl,
                IsActive = authoredVariant.IsActive,
                IsDefault = authoredVariant.IsDefault,
                SortOrder = sortOrder++,
                CreatedAt = DateTime.UtcNow,
                Product = product
            };

            ApplyVariantAttributes(variant, authoredVariant.Attributes);
            product.Variants.Add(variant);
            created.Add(variant);
        }

        EnsureSingleDefaultVariant(created);
        return new AuthoredVariantSyncResult(created, [], []);
    }

    /// <summary>
    /// WAVE 2A: refuses to soft-delete (via save omission) any existing variant
    /// that still has protected history or stock. The check runs inside the
    /// authoring transaction before any tracked mutation, so a concurrent
    /// history write that commits first is observed here rather than silently
    /// destroyed. Simple products are covered identically: their default
    /// variant is an ordinary variant row.
    /// </summary>
    private async Task ThrowOnProtectedAuthoredVariantOmissionsAsync(
        Product product,
        IReadOnlyList<ProductVariant> existingVariants,
        HashSet<Guid> desiredExistingIds,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext!.RequiredTenantId;
        foreach (var existing in existingVariants)
        {
            if (desiredExistingIds.Contains(existing.Id))
            {
                continue;
            }

            var blockers = await ProductVariantDeletionProtectionAuthority.GetBlockersAsync(
                _context!,
                tenantId,
                product.Id,
                existing.Id,
                cancellationToken);
            if (blockers.Count > 0)
            {
                throw new ProductAuthoringProtectedHistoryException(blockers);
            }
        }
    }

    /// <summary>
    /// WAVE 2A: refuses a Remove (destructive soft-delete) when the product or
    /// any of its variants still has protected history or stock. Archive
    /// (deactivate) is intentionally not guarded: retiring from active use is
    /// the safe lifecycle for history-bearing products.
    /// </summary>
    private async Task ThrowOnProtectedProductRemoveAsync(
        Guid productId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var blockers = await ProductVariantDeletionProtectionAuthority.GetBlockersAsync(
            _context!,
            tenantId,
            productId,
            variantId: null,
            cancellationToken);
        if (blockers.Count > 0)
        {
            throw new ProductAuthoringProtectedHistoryException(blockers);
        }
    }

    private async Task<AuthoredVariantSyncResult> SyncAuthoredVariantsAsync(
        Product product,
        IReadOnlyList<ProductAuthoringVariant> authoredVariants,
        CancellationToken cancellationToken)
    {
        if (authoredVariants.Count == 0)
        {
            return EmptyVariantSync();
        }

        var existingVariants = await _context!.ProductVariants
            .WhereCurrentSemanticOwner()
            .Include(variant => variant.Attributes)
            .Where(variant => variant.TenantId == product.TenantId && variant.ProductId == product.Id && !variant.IsDeleted)
            .ToListAsync(cancellationToken);

        var existingById = existingVariants.ToDictionary(variant => variant.Id);
        var existingByAttributeKey = existingVariants
            .Select(variant => new
            {
                Variant = variant,
                AttributeKey = variant.AttributeCombinationKey ?? ComputeExistingVariantAttributeCombinationKey(variant)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.AttributeKey))
            .GroupBy(item => item.AttributeKey!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Variant, StringComparer.Ordinal);
        var desiredExistingIds = ResolveAuthoredExistingVariantIds(authoredVariants, existingById, existingByAttributeKey);

        // WAVE 2 SKU integrity: reject duplicate combinations/SKUs/barcodes within
        // the request before any mutation so a conflicting save never partially
        // applies. Cross-request ownership is checked after identity resolution
        // below; the filtered unique indexes remain the final authority.
        ThrowOnDuplicateAuthoredVariantIdentities(authoredVariants);

        // WAVE 2A: variants omitted from the save must not silently destroy
        // protected financial/inventory/operational history. Throw before any
        // tracked mutation so the save is rejected atomically; the caller can
        // keep the variant and deactivate it instead.
        await ThrowOnProtectedAuthoredVariantOmissionsAsync(
            product,
            existingVariants,
            desiredExistingIds,
            cancellationToken);

        var created = new List<ProductVariant>();
        var updated = new List<(ProductVariant Variant, IReadOnlyList<string> ChangedFields)>();
        var deleted = new List<ProductVariant>();
        var sortOrder = 0;
        var matchedExistingIds = new HashSet<Guid>();

        foreach (var existing in existingVariants)
        {
            if (!desiredExistingIds.Contains(existing.Id))
            {
                existing.IsDeleted = true;
                existing.IsActive = false;
                existing.UpdatedAt = DateTime.UtcNow;
                deleted.Add(existing);
            }
        }

        var resolutions = new List<(ProductAuthoringVariant Authored, ProductVariant? Existing)>(authoredVariants.Count);
        foreach (var authoredVariant in authoredVariants)
        {
            resolutions.Add((
                authoredVariant,
                ResolveExistingAuthoredVariant(
                    authoredVariant,
                    existingById,
                    existingByAttributeKey,
                    matchedExistingIds)));
        }

        await ThrowOnOwnedAuthoredVariantIdentitiesForUpdateAsync(
            product,
            resolutions,
            desiredExistingIds,
            existingById,
            cancellationToken);

        foreach (var (authoredVariant, existing) in resolutions)
        {
            if (existing is null)
            {
                var createdVariant = new ProductVariant
                {
                    // A supplied ID that does not resolve to this product is a client-side
                    // temporary/stale identity, not authority to choose a persisted key.
                    Id = Guid.NewGuid(),
                    TenantId = product.TenantId,
                    ProductId = product.Id,
                    SKU = authoredVariant.Sku,
                    Barcode = authoredVariant.Barcode,
                    Price = authoredVariant.Price,
                    OnlinePrice = authoredVariant.OnlinePrice,
                    CostPrice = authoredVariant.CostPrice,
                    Weight = authoredVariant.Weight,
                    MediaId = authoredVariant.MediaId,
                    ImageUrl = authoredVariant.MediaId.HasValue ? null : authoredVariant.ImageUrl,
                    IsActive = authoredVariant.IsActive,
                    IsDefault = authoredVariant.IsDefault,
                    SortOrder = sortOrder++,
                    CreatedAt = DateTime.UtcNow,
                    Product = product
                };

                ApplyVariantAttributes(createdVariant, authoredVariant.Attributes);
                _context.ProductVariants.Add(createdVariant);
                created.Add(createdVariant);
                continue;
            }

            var changedFields = new List<string>();
            SetVariantIfChanged(existing, variant => variant.SKU, (variant, value) => variant.SKU = value, authoredVariant.Sku, nameof(ProductVariant.SKU), changedFields);
            SetVariantIfChanged(existing, variant => variant.Barcode, (variant, value) => variant.Barcode = value, authoredVariant.Barcode, nameof(ProductVariant.Barcode), changedFields);
            SetVariantIfChanged(existing, variant => variant.Price, (variant, value) => variant.Price = value, authoredVariant.Price, nameof(ProductVariant.Price), changedFields);
            SetVariantIfChanged(existing, variant => variant.OnlinePrice, (variant, value) => variant.OnlinePrice = value, authoredVariant.OnlinePrice, nameof(ProductVariant.OnlinePrice), changedFields);
            SetVariantIfChanged(existing, variant => variant.CostPrice, (variant, value) => variant.CostPrice = value, authoredVariant.CostPrice, nameof(ProductVariant.CostPrice), changedFields);
            SetVariantIfChanged(existing, variant => variant.Weight, (variant, value) => variant.Weight = value, authoredVariant.Weight, nameof(ProductVariant.Weight), changedFields);
            SetVariantIfChanged(existing, variant => variant.MediaId, (variant, value) => variant.MediaId = value, authoredVariant.MediaId, nameof(ProductVariant.MediaId), changedFields);
            SetVariantIfChanged(existing, variant => variant.ImageUrl, (variant, value) => variant.ImageUrl = value, authoredVariant.MediaId.HasValue ? null : authoredVariant.ImageUrl, nameof(ProductVariant.ImageUrl), changedFields);
            SetVariantIfChanged(existing, variant => variant.IsActive, (variant, value) => variant.IsActive = value, authoredVariant.IsActive, nameof(ProductVariant.IsActive), changedFields);
            SetVariantIfChanged(existing, variant => variant.IsDefault, (variant, value) => variant.IsDefault = value, authoredVariant.IsDefault, nameof(ProductVariant.IsDefault), changedFields);
            SetVariantIfChanged(existing, variant => variant.SortOrder, (variant, value) => variant.SortOrder = value, sortOrder++, nameof(ProductVariant.SortOrder), changedFields);

            if (ReplaceVariantAttributesIfChanged(existing, authoredVariant.Attributes))
            {
                changedFields.Add(nameof(ProductVariant.Attributes));
            }

            if (changedFields.Count > 0)
            {
                existing.UpdatedAt = DateTime.UtcNow;
                updated.Add((existing, changedFields));
            }
        }

        EnsureSingleDefaultVariant(existingVariants.Where(variant => !variant.IsDeleted).Concat(created).ToList());
        return new AuthoredVariantSyncResult(created, updated, deleted);
    }

    /// <summary>
    /// Wave 2: deterministic intra-request variant-identity conflicts. Uses the
    /// same error codes as <see cref="ProductAuthoringDatabaseConflictClassifier"/>
    /// so early and commit-time conflicts share one contract.
    /// </summary>
    private static void ThrowOnDuplicateAuthoredVariantIdentities(
        IReadOnlyList<ProductAuthoringVariant> authoredVariants)
    {
        if (ProductAuthoringVariantIdentityAuthority.FindDuplicateCombinationKey(authoredVariants) is not null)
        {
            throw new ProductAuthoringVariantIdentityConflictException(
                "PRODUCT_ATTRIBUTE_CONFLICT",
                "variants.attributes",
                "A product variant with the same attribute combination already exists.");
        }

        if (ProductAuthoringVariantIdentityAuthority.FindDuplicateSku(authoredVariants) is not null)
        {
            throw new ProductAuthoringVariantIdentityConflictException(
                "PRODUCT_SKU_CONFLICT",
                "variants.sku",
                "SKU is already assigned to another product variant.");
        }

        if (ProductAuthoringVariantIdentityAuthority.FindDuplicateBarcode(authoredVariants) is not null)
        {
            throw new ProductAuthoringVariantIdentityConflictException(
                "PRODUCT_BARCODE_CONFLICT",
                "variants.barcode",
                "Barcode is already assigned to another product variant.");
        }
    }

    private async Task ThrowOnOwnedAuthoredVariantIdentitiesForUpdateAsync(
        Product product,
        IReadOnlyList<(ProductAuthoringVariant Authored, ProductVariant? Existing)> resolutions,
        ISet<Guid> desiredExistingIds,
        IReadOnlyDictionary<Guid, ProductVariant> existingById,
        CancellationToken cancellationToken)
    {
        var excludedVariantIds = new HashSet<Guid>(
            resolutions
                .Select(resolution => resolution.Existing?.Id)
                .Where(id => id.HasValue)
                .Select(id => id!.Value));
        foreach (var existingId in existingById.Keys)
        {
            // Variants omitted from this save are soft-deleted atomically in the
            // same commit, so they must not block SKU/barcode reuse. Every other
            // surviving variant keeps blocking, exactly like the unique indexes.
            if (!desiredExistingIds.Contains(existingId))
            {
                excludedVariantIds.Add(existingId);
            }
        }

        await ThrowOnOwnedVariantIdentitiesAsync(
            product.TenantId,
            product.Id,
            CollectTrimmedCandidates(resolutions.Select(resolution => resolution.Authored.Sku)),
            CollectTrimmedCandidates(resolutions.Select(resolution => resolution.Authored.Barcode)),
            excludedVariantIds,
            cancellationToken);
    }

    private async Task ThrowOnOwnedAuthoredVariantIdentitiesForCreateAsync(
        Guid tenantId,
        Guid productId,
        IReadOnlyList<ProductAuthoringVariant> authoredVariants,
        CancellationToken cancellationToken)
    {
        await ThrowOnOwnedVariantIdentitiesAsync(
            tenantId,
            productId,
            CollectTrimmedCandidates(authoredVariants.Select(variant => variant.Sku)),
            CollectTrimmedCandidates(authoredVariants.Select(variant => variant.Barcode)),
            new HashSet<Guid>(),
            cancellationToken);
    }

    /// <summary>
    /// Wave 2: server-side SKU/barcode ownership for matrix writes. Batched so
    /// large matrices cost a constant number of queries. Equality is evaluated
    /// by the database (same collation the unique indexes enforce); the indexes
    /// remain the final authority for the residual race window.
    /// </summary>
    private async Task ThrowOnOwnedVariantIdentitiesAsync(
        Guid tenantId,
        Guid productId,
        IReadOnlyList<string> skuCandidates,
        IReadOnlyList<string> barcodeCandidates,
        ISet<Guid> excludedVariantIds,
        CancellationToken cancellationToken)
    {
        if (skuCandidates.Count > 0)
        {
            var conflictingVariantSku = await _context!.ProductVariants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .WhereCurrentSemanticOwner()
                .Where(variant =>
                    variant.TenantId == tenantId &&
                    !variant.IsDeleted &&
                    skuCandidates.Contains(variant.SKU) &&
                    !excludedVariantIds.Contains(variant.Id))
                .Select(variant => variant.SKU)
                .FirstOrDefaultAsync(cancellationToken);
            if (conflictingVariantSku is not null)
            {
                throw new ProductAuthoringVariantIdentityConflictException(
                    "PRODUCT_SKU_CONFLICT",
                    "variants.sku",
                    "SKU is already assigned to another product variant.");
            }

            var conflictingProductSku = await _context.Products
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(existingProduct =>
                    existingProduct.TenantId == tenantId &&
                    !existingProduct.IsDeleted &&
                    existingProduct.Id != productId &&
                    skuCandidates.Contains(existingProduct.SKU))
                .Select(existingProduct => existingProduct.SKU)
                .FirstOrDefaultAsync(cancellationToken);
            if (conflictingProductSku is not null)
            {
                throw new ProductAuthoringVariantIdentityConflictException(
                    "PRODUCT_SKU_CONFLICT",
                    "variants.sku",
                    "SKU is already assigned to another product variant.");
            }
        }

        if (barcodeCandidates.Count > 0)
        {
            var conflictingVariantBarcode = await _context!.ProductVariants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .WhereCurrentSemanticOwner()
                .Where(variant =>
                    variant.TenantId == tenantId &&
                    !variant.IsDeleted &&
                    barcodeCandidates.Contains(variant.Barcode) &&
                    !excludedVariantIds.Contains(variant.Id))
                .Select(variant => variant.Barcode)
                .FirstOrDefaultAsync(cancellationToken);
            if (conflictingVariantBarcode is not null)
            {
                throw new ProductAuthoringVariantIdentityConflictException(
                    "PRODUCT_BARCODE_CONFLICT",
                    "variants.barcode",
                    "Barcode is already assigned to another product variant.");
            }

            var conflictingProductBarcode = await _context.Products
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(existingProduct =>
                    existingProduct.TenantId == tenantId &&
                    !existingProduct.IsDeleted &&
                    existingProduct.Id != productId &&
                    barcodeCandidates.Contains(existingProduct.Barcode))
                .Select(existingProduct => existingProduct.Barcode)
                .FirstOrDefaultAsync(cancellationToken);
            if (conflictingProductBarcode is not null)
            {
                throw new ProductAuthoringVariantIdentityConflictException(
                    "PRODUCT_BARCODE_CONFLICT",
                    "variants.barcode",
                    "Barcode is already assigned to another product variant.");
            }

            var conflictingUnitBarcode = await _context.ProductUnits
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(productUnit =>
                    !productUnit.IsDeleted &&
                    productUnit.ProductId != productId &&
                    productUnit.Product.TenantId == tenantId &&
                    !productUnit.Product.IsDeleted &&
                    barcodeCandidates.Contains(productUnit.Barcode))
                .Select(productUnit => productUnit.Barcode)
                .FirstOrDefaultAsync(cancellationToken);
            if (conflictingUnitBarcode is not null)
            {
                throw new ProductAuthoringVariantIdentityConflictException(
                    "PRODUCT_BARCODE_CONFLICT",
                    "variants.barcode",
                    "Barcode is already assigned to another product variant.");
            }
        }
    }

    private static IReadOnlyList<string> CollectTrimmedCandidates(IEnumerable<string?> values) =>
        values
            .Select(value => value?.Trim())
            .Where(value => !string.IsNullOrEmpty(value))
            .Distinct(StringComparer.Ordinal)
            .ToList()!;

    private static IReadOnlyList<ProductVariantAttribute> ApplyVariantAttributes(
        ProductVariant variant,
        IReadOnlyDictionary<string, string> attributes)
    {
        variant.AttributeCombinationKey = ProductVariantAttributeCombinationKey.Compute(
            attributes.Select(attribute => (attribute.Key, attribute.Value)));
        var createdAttributes = new List<ProductVariantAttribute>(attributes.Count);
        var sortOrder = 0;
        foreach (var (name, value) in attributes.OrderBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase))
        {
            var attribute = new ProductVariantAttribute
            {
                Id = Guid.NewGuid(),
                VariantId = variant.Id,
                Name = name,
                Value = value,
                SortOrder = sortOrder++,
                Variant = variant
            };
            variant.Attributes.Add(attribute);
            createdAttributes.Add(attribute);
        }

        return createdAttributes;
    }

    private static HashSet<Guid> ResolveAuthoredExistingVariantIds(
        IReadOnlyList<ProductAuthoringVariant> authoredVariants,
        IReadOnlyDictionary<Guid, ProductVariant> existingById,
        IReadOnlyDictionary<string, ProductVariant> existingByAttributeKey)
    {
        var ids = new HashSet<Guid>();

        foreach (var authoredVariant in authoredVariants)
        {
            if (authoredVariant.VariantId.HasValue &&
                existingById.ContainsKey(authoredVariant.VariantId.Value))
            {
                ids.Add(authoredVariant.VariantId.Value);
                continue;
            }

            var attributeKey = ComputeAuthoredVariantAttributeCombinationKey(authoredVariant);
            if (attributeKey is not null &&
                existingByAttributeKey.TryGetValue(attributeKey, out var existing))
            {
                ids.Add(existing.Id);
            }
        }

        return ids;
    }

    private static ProductVariant? ResolveExistingAuthoredVariant(
        ProductAuthoringVariant authoredVariant,
        IReadOnlyDictionary<Guid, ProductVariant> existingById,
        IReadOnlyDictionary<string, ProductVariant> existingByAttributeKey,
        ISet<Guid> matchedExistingIds)
    {
        ProductVariant? existing = null;

        if (authoredVariant.VariantId.HasValue)
        {
            existingById.TryGetValue(authoredVariant.VariantId.Value, out existing);
        }

        if (existing is null)
        {
            var attributeKey = ComputeAuthoredVariantAttributeCombinationKey(authoredVariant);
            if (attributeKey is not null &&
                existingByAttributeKey.TryGetValue(attributeKey, out var attributeMatched) &&
                !matchedExistingIds.Contains(attributeMatched.Id))
            {
                existing = attributeMatched;
            }
        }

        if (existing is not null)
        {
            matchedExistingIds.Add(existing.Id);
        }

        return existing;
    }

    private static string? ComputeAuthoredVariantAttributeCombinationKey(ProductAuthoringVariant authoredVariant) =>
        ProductVariantAttributeCombinationKey.Compute(
            authoredVariant.Attributes.Select(attribute => (attribute.Key, attribute.Value)));

    private static string? ComputeExistingVariantAttributeCombinationKey(ProductVariant variant) =>
        ProductVariantAttributeCombinationKey.Compute(
            variant.Attributes
                .Where(attribute => !attribute.IsDeleted)
                .Select(attribute => (attribute.Name, attribute.Value)));

    private bool ReplaceVariantAttributesIfChanged(
        ProductVariant variant,
        IReadOnlyDictionary<string, string> attributes)
    {
        var current = variant.Attributes
            .Where(attribute => !attribute.IsDeleted)
            .OrderBy(attribute => attribute.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(attribute => attribute.Name, attribute => attribute.Value, StringComparer.OrdinalIgnoreCase);

        if (current.Count == attributes.Count &&
            current.All(pair => attributes.TryGetValue(pair.Key, out var value) && string.Equals(value, pair.Value, StringComparison.Ordinal)))
        {
            return false;
        }

        foreach (var attribute in variant.Attributes)
        {
            attribute.IsDeleted = true;
        }

        variant.Attributes.Clear();
        var replacementAttributes = ApplyVariantAttributes(variant, attributes);
        _context!.ProductVariantAttributes.AddRange(replacementAttributes);
        return true;
    }

    private static void EnsureSingleDefaultVariant(IReadOnlyList<ProductVariant> variants)
    {
        if (variants.Count == 0 || variants.Any(variant => variant.IsDefault))
        {
            return;
        }

        variants
            .OrderByDescending(variant => variant.IsActive)
            .ThenBy(variant => variant.SortOrder)
            .ThenBy(variant => variant.Id)
            .First()
            .IsDefault = true;
    }

    private static void SetVariantIfChanged<T>(
        ProductVariant variant,
        Func<ProductVariant, T> getter,
        Action<ProductVariant, T> setter,
        T value,
        string fieldName,
        ICollection<string> changedFields)
    {
        if (EqualityComparer<T>.Default.Equals(getter(variant), value))
        {
            return;
        }

        setter(variant, value);
        changedFields.Add(fieldName);
    }

    private static AuthoredVariantSyncResult EmptyVariantSync() =>
        new(Array.Empty<ProductVariant>(), Array.Empty<(ProductVariant Variant, IReadOnlyList<string> ChangedFields)>(), Array.Empty<ProductVariant>());

    private static ProductUnitData MapProductUnitData(ProductUnit productUnit) =>
        new()
        {
            UnitId = productUnit.UnitId,
            Name = productUnit.Unit.Name,
            Symbol = productUnit.Unit.Symbol,
            SalePrice = productUnit.SellingPrice ?? 0,
            ConversionFactor = productUnit.ConversionFactor,
            IsBaseUnit = productUnit.IsBaseUnit
        };

    private static ProductFaqData MapFaqEventData(ProductAuthoringFaq faq) =>
        new()
        {
            Question = faq.Question,
            Answer = faq.Answer
        };

    private static string[] ProductChangedFields() =>
    [
        "Name",
        "NameAr",
        "Description",
        "DescriptionAr",
        "Brand",
        "SalePrice",
        "OnlinePrice",
        "PublishStatus",
        "IsOnlineVisible",
        "IsSearchable",
        "IsFeatured",
        "Slug",
        "HasVariants",
        "Images",
        "ProductTemplate",
        "MetaTitle",
        "MetaDescription",
        "Tags",
        "Features",
        "Faqs",
        "AiGenerated",
        "AiMetadata",
        "DefaultUnit",
        "Units"
    ];

    private static long PendingProductMutationVersion(Product product) =>
        product.ProjectionMutationVersion <= 0 ? 1L : product.ProjectionMutationVersion + 1L;

    private static long PendingVariantMutationVersion(ProductVariant variant) =>
        variant.ProjectionMutationVersion <= 0 ? 1L : variant.ProjectionMutationVersion + 1L;

    private static bool MetadataFlag(ProductAuthoringCommand command, string key) =>
        command.Metadata.AdditionalMetadata.TryGetValue(key, out var value) &&
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static bool IsStale(ProductAuthoringCommand command, Product product) =>
        TryGetExpectedProductRowVersion(command, out var expectedRowVersion) &&
        product.RowVersion != expectedRowVersion;

    private void LogRuntimeRowVersionTraceBeforeIsStale(ProductAuthoringCommand command, Product product)
    {
        var hasExpectedVersion = TryGetExpectedProductRowVersion(command, out var expectedRowVersion);
        uint? expected = hasExpectedVersion ? expectedRowVersion : null;
        bool? expectedEqualsCurrent = hasExpectedVersion
            ? expectedRowVersion == product.RowVersion
            : null;

        _logger.LogInformation(
            "RUNTIME_ROWVERSION_TRACE | ProductAuthoringOrchestrator.BeforeIsStale | Operation: {Operation} | ProductId: {ProductId} | CommandExpectedVersion: {CommandExpectedVersion} | DatabaseRowVersion: {DatabaseRowVersion} | ExpectedEqualsCurrent: {ExpectedEqualsCurrent}",
            command.Operation,
            product.Id,
            expected,
            product.RowVersion,
            expectedEqualsCurrent);
    }

    private void LogRuntimeRowVersionTraceBeforeSaveChanges(ProductAuthoringCommand command, Product product)
    {
        var rowVersionProperty = _context.Entry(product).Property(entity => entity.RowVersion);
        _logger.LogInformation(
            "RUNTIME_ROWVERSION_TRACE | ProductAuthoringOrchestrator.BeforeSaveChanges | Operation: {Operation} | ProductId: {ProductId} | CommandExpectedVersion: {CommandExpectedVersion} | ProductRowVersion: {ProductRowVersion} | EFOriginalRowVersion: {EFOriginalRowVersion} | EFCurrentRowVersion: {EFCurrentRowVersion}",
            command.Operation,
            product.Id,
            command.Metadata.ExpectedVersion,
            product.RowVersion,
            rowVersionProperty.OriginalValue,
            rowVersionProperty.CurrentValue);
    }

    private void LogRuntimeRowVersionTraceConcurrencyException(ProductAuthoringCommand command, Product product)
    {
        var rowVersionProperty = _context.Entry(product).Property(entity => entity.RowVersion);
        _logger.LogWarning(
            "RUNTIME_ROWVERSION_TRACE | ProductAuthoringOrchestrator.DbUpdateConcurrencyException | Operation: {Operation} | ProductId: {ProductId} | CommandExpectedVersion: {CommandExpectedVersion} | ProductRowVersion: {ProductRowVersion} | EFOriginalRowVersion: {EFOriginalRowVersion} | EFCurrentRowVersion: {EFCurrentRowVersion}",
            command.Operation,
            product.Id,
            command.Metadata.ExpectedVersion,
            product.RowVersion,
            rowVersionProperty.OriginalValue,
            rowVersionProperty.CurrentValue);
    }

    private async Task LogDbUpdateConcurrencyExceptionEntriesAsync(
        DbUpdateConcurrencyException exception,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "RUNTIME_EF_CONCURRENCY_ENTRIES | EntriesCount: {EntriesCount}",
            exception.Entries.Count);

        for (var index = 0; index < exception.Entries.Count; index++)
        {
            var entry = exception.Entries[index];
            var databaseValues = await entry.GetDatabaseValuesAsync(cancellationToken);
            var tableName = FormatTableName(entry);

            _logger.LogWarning(
                "RUNTIME_EF_CONCURRENCY_ENTRY | Index: {Index} | EntityType: {EntityType} | EntityState: {EntityState} | TableName: {TableName} | OriginalValues.RowVersion: {OriginalRowVersion} | OriginalValues.xmin: {OriginalXmin} | CurrentValues.RowVersion: {CurrentRowVersion} | CurrentValues.xmin: {CurrentXmin} | DatabaseValuesFound: {DatabaseValuesFound} | DatabaseValues.RowVersion: {DatabaseRowVersion} | DatabaseValues.xmin: {DatabaseXmin}",
                index,
                entry.Entity.GetType().FullName,
                entry.State,
                tableName,
                TryReadPropertyValue(entry.OriginalValues, "RowVersion"),
                TryReadPropertyValue(entry.OriginalValues, "xmin"),
                TryReadPropertyValue(entry.CurrentValues, "RowVersion"),
                TryReadPropertyValue(entry.CurrentValues, "xmin"),
                databaseValues is not null,
                databaseValues is null ? null : TryReadPropertyValue(databaseValues, "RowVersion"),
                databaseValues is null ? null : TryReadPropertyValue(databaseValues, "xmin"));
        }
    }

    private static string FormatTableName(EntityEntry entry)
    {
        var tableName = entry.Metadata.GetTableName();
        var schema = entry.Metadata.GetSchema();

        if (string.IsNullOrWhiteSpace(tableName))
        {
            return "<unknown>";
        }

        return string.IsNullOrWhiteSpace(schema)
            ? tableName
            : $"{schema}.{tableName}";
    }

    private static object? TryReadPropertyValue(PropertyValues values, string propertyName)
    {
        var property = values.Properties.FirstOrDefault(property =>
            string.Equals(property.Name, propertyName, StringComparison.Ordinal));

        return property is null
            ? null
            : values[property];
    }

    private void ApplyExpectedProductRowVersion(ProductAuthoringCommand command, Product product)
    {
        if (!TryGetExpectedProductRowVersion(command, out var expectedRowVersion))
        {
            return;
        }

        _context.Entry(product)
            .Property(entity => entity.RowVersion)
            .OriginalValue = expectedRowVersion;
    }

    private static bool TryGetExpectedProductRowVersion(ProductAuthoringCommand command, out uint expectedRowVersion)
    {
        expectedRowVersion = 0;
        if (!command.Metadata.ExpectedVersion.HasValue)
        {
            return false;
        }

        var value = command.Metadata.ExpectedVersion.Value;
        if (value < 0 || value > uint.MaxValue)
        {
            return false;
        }

        expectedRowVersion = (uint)value;
        return true;
    }

    private static string BuildIdempotencyPrefix(ProductAuthoringCommand command, Guid productId)
    {
        if (!string.IsNullOrWhiteSpace(command.Metadata.IdempotencyKey))
        {
            return $"product-authoring:{command.Identity.TenantId:N}:{command.Metadata.IdempotencyKey.Trim()}";
        }

        return $"product-authoring:{command.Identity.TenantId:N}:{command.Operation}:{productId:N}";
    }

    private bool IsPostgresProvider() =>
        _context!.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private bool IsSqlServerProvider() =>
        _context!.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;

    private bool IsSqliteProvider() =>
        _context!.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    private static string ProductAuthoringIdempotencyOperation(ProductAuthoringOperation operation) =>
        operation switch
        {
            ProductAuthoringOperation.Create => "AuthoringCreate",
            ProductAuthoringOperation.Update => "AuthoringUpdate",
            ProductAuthoringOperation.Archive => "AuthoringArchive",
            ProductAuthoringOperation.Remove => "AuthoringRemove",
            _ => "AuthoringUnknown"
        };

    private static string NormalizeProductAuthoringIdempotencyKey(string key)
    {
        var scopedKey = $"authoring:{key.Trim()}";
        return scopedKey.Length <= 200
            ? scopedKey
            : $"authoring:sha256:{HashToHex(scopedKey)}";
    }

    private static string BuildProductAuthoringRequestFingerprint(ProductAuthoringCommand command)
    {
        var fingerprintCommand = command with
        {
            Metadata = command.Metadata with
            {
                CorrelationId = string.Empty,
                IdempotencyKey = null
            }
        };

        if (MetadataFlag(command, "stableOmittedCollectionsFingerprint"))
        {
            fingerprintCommand = fingerprintCommand with
            {
                Units = MetadataFlag(command, "productUnitsSubmitted")
                    ? command.Units
                    : new ProductAuthoringUnits(null, null, Array.Empty<ProductAuthoringUnit>()),
                Variants = MetadataFlag(command, "variantsSubmitted")
                    ? command.Variants
                    : Array.Empty<ProductAuthoringVariant>()
            };
        }

        return HashToHex(JsonSerializer.Serialize(fingerprintCommand, JsonOptions));
    }

    private static string HashToHex(string value) =>
        Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static Guid DeterministicProductId(Guid tenantId, string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Guid.NewGuid();
        }

        var bytes = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes($"product-authoring:create:{tenantId:N}:{idempotencyKey.Trim()}"));
        return new Guid(bytes[..16]);
    }

    private static string? SerializeStringList(IReadOnlyList<string> values) =>
        values.Count == 0 ? null : JsonSerializer.Serialize(values, JsonOptions);

    private static string? SerializeFaqs(IReadOnlyList<ProductAuthoringFaq> faqs) =>
        faqs.Count == 0 ? null : JsonSerializer.Serialize(faqs, JsonOptions);
}

public interface IProductAuthoringTransactionObserver
{
    Task AfterUnitSynchronizationAsync(
        ProductAuthoringCommand command,
        Product product,
        CancellationToken cancellationToken);

    Task AfterVariantSynchronizationAsync(
        ProductAuthoringCommand command,
        Product product,
        CancellationToken cancellationToken);

    Task BeforeCommitAsync(
        ProductAuthoringCommand command,
        Product product,
        CancellationToken cancellationToken);

    Task AfterCommitAsync(
        ProductAuthoringCommand command,
        Product product,
        CancellationToken cancellationToken);
}

public sealed class NoOpProductAuthoringTransactionObserver : IProductAuthoringTransactionObserver
{
    public static NoOpProductAuthoringTransactionObserver Instance { get; } = new();

    private NoOpProductAuthoringTransactionObserver()
    {
    }

    public Task AfterUnitSynchronizationAsync(
        ProductAuthoringCommand command,
        Product product,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task AfterVariantSynchronizationAsync(
        ProductAuthoringCommand command,
        Product product,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task BeforeCommitAsync(
        ProductAuthoringCommand command,
        Product product,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task AfterCommitAsync(
        ProductAuthoringCommand command,
        Product product,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
