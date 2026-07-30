using System.Text;
using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
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
            cancellationToken);
    }

    private async Task<ProductAuthoringResult> ExecuteUpdateAsync(
        ProductAuthoringCommand command,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext!.RequiredTenantId;
        var product = await LoadTrackedProductAsync(command.Identity.ProductId, tenantId, cancellationToken);
        var lifecycleDecision = await _policy.EvaluateLifecycleAsync(
            command,
            new ProductAuthoringLifecycleFacts(ProductExists: product is not null),
            cancellationToken);
        if (!lifecycleDecision.IsAllowed)
        {
            return command.ToResult(lifecycleDecision.Status, lifecycleDecision.Errors);
        }

        product = product!;

        var existingIdempotencyResult = await TryResolveExistingProductAuthoringIdempotencyAsync(
            command,
            tenantId,
            product.Id,
            cancellationToken);
        if (existingIdempotencyResult is not null)
        {
            return existingIdempotencyResult;
        }

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
            beforeAuditData);
    }

    private async Task<ProductAuthoringResult> ExecuteArchiveOrRemoveAsync(
        ProductAuthoringCommand command,
        bool remove,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext!.RequiredTenantId;
        var product = await LoadTrackedProductAsync(command.Identity.ProductId, tenantId, cancellationToken);
        var lifecycleDecision = await _policy.EvaluateLifecycleAsync(
            command,
            new ProductAuthoringLifecycleFacts(ProductExists: product is not null),
            cancellationToken);
        if (!lifecycleDecision.IsAllowed)
        {
            return command.ToResult(lifecycleDecision.Status, lifecycleDecision.Errors);
        }

        product = product!;

        var existingIdempotencyResult = await TryResolveExistingProductAuthoringIdempotencyAsync(
            command,
            tenantId,
            product.Id,
            cancellationToken);
        if (existingIdempotencyResult is not null)
        {
            return existingIdempotencyResult;
        }

        if (IsStale(command, product))
        {
            return command.ToResult(
                ProductAuthoringStatus.ConcurrencyConflict,
                [new ProductAuthoringValidationError("PRODUCT_EXPECTED_VERSION_STALE", "expectedVersion", "Product has been changed by a newer authoring command.")]);
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
                    variant.IsActive = false;
                    variant.UpdatedAt = product.UpdatedAt;

                    if (remove)
                    {
                        variant.IsDeleted = true;
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

    private async Task<ProductAuthoringResult> CommitProductAuthoringTransactionAsync(
        ProductAuthoringCommand command,
        Guid tenantId,
        Product product,
        Func<Task> mutateAndStageAsync,
        CancellationToken cancellationToken,
        object? beforeAuditData = null)
    {
        await using var transaction = await _context!.Database.BeginTransactionAsync(cancellationToken);
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
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return command.ToResult(
                ProductAuthoringStatus.ConcurrencyConflict,
                [new ProductAuthoringValidationError("PRODUCT_CONCURRENCY_CONFLICT", "expectedVersion", "Product has been changed by another transaction.")]);
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
            command.Metadata.IdempotencyKey,
            command.Metadata.ExpectedVersion,
            command.Operation,
            ProductId = product.Id,
            product.TenantId,
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

        if (command.Operation == ProductAuthoringOperation.Create)
        {
            await _eventPublisher!.PublishAsync(new ProductCreatedEvent
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
            }, cancellationToken);

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

        await _eventPublisher!.PublishAsync(new ProductUpdatedEvent
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
        }, cancellationToken);

        if (previousPublishStatus.HasValue &&
            (previousPublishStatus.Value != product.PublishStatus ||
             previousIsOnlineVisible.GetValueOrDefault() != product.IsOnlineVisible))
        {
            await _eventPublisher.PublishAsync(new ProductPublishStatusChangedEvent
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
            }, cancellationToken);
        }

        if (!wasArchivedBeforeMutation && (!product.IsActive || product.PublishStatus == PublishStatus.Archived))
        {
            await _eventPublisher.PublishAsync(new ProductArchivedEvent
            {
                TenantId = product.TenantId,
                EventVersion = eventVersion,
                IdempotencyKey = $"{idempotencyPrefix}:product-archived:{eventVersion}",
                CorrelationId = command.Metadata.CorrelationId,
                ProductId = product.Id
            }, cancellationToken);
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
                await PublishDefaultVariantUpdatedAsync(defaultVariantSync.Variant, defaultVariantSync.ChangedFields, cancellationToken);
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

            await PublishDefaultVariantUpdatedAsync(variant, variantChangedFields, cancellationToken);
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

        await _eventPublisher.PublishAsync(new ProductDeletedEvent
        {
            TenantId = product.TenantId,
            EventVersion = eventVersion,
            IdempotencyKey = $"{idempotencyPrefix}:product-deleted:{eventVersion}",
            CorrelationId = command.Metadata.CorrelationId,
            ProductId = product.Id,
            SKU = product.SKU
        }, cancellationToken);

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
            result.Status == ProductAuthoringStatus.Accepted ? 202 : 400,
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
        product.PurchasePrice = 0m;
        product.SalePrice = 0m;
        product.OnlinePrice = null;
        product.PriceIncludesTax = command.Pricing.PriceIncludesTax;
        product.TaxRate = command.Pricing.TaxRate;
        product.Unit = command.Inventory.Unit;
        product.MinimumStockLevel = command.Inventory.MinimumStockLevel;
        product.Images = null;
        product.IsActive = command.Publication.IsActive;
        product.TrackStock = command.Inventory.HasVariants ? false : command.Inventory.TrackStock;
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

    private async Task<Product?> LoadTrackedProductAsync(
        Guid? productId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (!productId.HasValue || productId.Value == Guid.Empty)
        {
            return null;
        }

        return await _context!.Products
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

        if (trackedUnits.Count == 0)
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
        ProductVariant variant,
        IReadOnlyList<string> changedFields,
        CancellationToken cancellationToken)
    {
        await _eventPublisher!.PublishAsync(new ProductVariantUpdatedEvent
        {
            TenantId = _tenantContext!.RequiredTenantId,
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
                Id = authoredVariant.VariantId ?? Guid.NewGuid(),
                TenantId = product.TenantId,
                ProductId = product.Id,
                SKU = authoredVariant.Sku,
                Barcode = authoredVariant.Barcode,
                Price = authoredVariant.Price,
                OnlinePrice = authoredVariant.OnlinePrice,
                CostPrice = authoredVariant.CostPrice,
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

        var authoredById = authoredVariants
            .Where(variant => variant.VariantId.HasValue && variant.VariantId.Value != Guid.Empty)
            .ToDictionary(variant => variant.VariantId!.Value);

        var created = new List<ProductVariant>();
        var updated = new List<(ProductVariant Variant, IReadOnlyList<string> ChangedFields)>();
        var deleted = new List<ProductVariant>();
        var sortOrder = 0;

        foreach (var existing in existingVariants)
        {
            if (!authoredById.ContainsKey(existing.Id))
            {
                existing.IsDeleted = true;
                existing.IsActive = false;
                existing.UpdatedAt = DateTime.UtcNow;
                deleted.Add(existing);
            }
        }

        foreach (var authoredVariant in authoredVariants)
        {
            var existing = authoredVariant.VariantId.HasValue
                ? existingVariants.FirstOrDefault(variant => variant.Id == authoredVariant.VariantId.Value)
                : null;

            if (existing is null)
            {
                var createdVariant = new ProductVariant
                {
                    Id = authoredVariant.VariantId ?? Guid.NewGuid(),
                    TenantId = product.TenantId,
                    ProductId = product.Id,
                    SKU = authoredVariant.Sku,
                    Barcode = authoredVariant.Barcode,
                    Price = authoredVariant.Price,
                    OnlinePrice = authoredVariant.OnlinePrice,
                    CostPrice = authoredVariant.CostPrice,
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

    private static void ApplyVariantAttributes(
        ProductVariant variant,
        IReadOnlyDictionary<string, string> attributes)
    {
        variant.AttributeCombinationKey = ProductVariantAttributeCombinationKey.Compute(
            attributes.Select(attribute => (attribute.Key, attribute.Value)));
        var sortOrder = 0;
        foreach (var (name, value) in attributes.OrderBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase))
        {
            variant.Attributes.Add(new ProductVariantAttribute
            {
                Id = Guid.NewGuid(),
                VariantId = variant.Id,
                Name = name,
                Value = value,
                SortOrder = sortOrder++,
                Variant = variant
            });
        }
    }

    private static bool ReplaceVariantAttributesIfChanged(
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
        ApplyVariantAttributes(variant, attributes);
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
