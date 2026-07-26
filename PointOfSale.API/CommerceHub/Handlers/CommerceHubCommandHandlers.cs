using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.CommerceHub.Commands;
using PointOfSale.API.CommerceHub.Services;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.Governance;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Financial;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Pricing;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.CommerceHub.Handlers;

public interface ICommerceHubCommandHandler<in TCommand, TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}

public interface IPublishToCommerceHubHandler : ICommerceHubCommandHandler<PublishToCommerceHubCommand, CommerceHubAdminListingResponse>
{
}

public interface IUpdateCommerceHubListingHandler : ICommerceHubCommandHandler<UpdateCommerceHubListingCommand, CommerceHubAdminListingResponse>
{
}

public interface IUnpublishFromCommerceHubHandler : ICommerceHubCommandHandler<UnpublishFromCommerceHubCommand, CommerceHubAdminListingResponse>
{
}

public class CommerceHubValidationException : Exception
{
    public CommerceHubValidationException(string message) : base(message)
    {
    }
}

public class CommerceHubNotFoundException : Exception
{
    public CommerceHubNotFoundException(string message) : base(message)
    {
    }
}

internal static class CommerceHubTenantIsolationGuard
{
    public static void EnsureTenantAccess(
        ITenantContext tenantContext,
        Guid requestedTenantId,
        ILogger logger,
        string operation)
    {
        if (!tenantContext.HasTenant)
        {
            return;
        }

        var currentTenantId = tenantContext.RequiredTenantId;
        if (requestedTenantId == currentTenantId)
        {
            return;
        }

        logger.LogWarning(
            "[SECURITY] Cross-tenant access attempt detected | TenantId: {TenantId} | EntityTenantId: {EntityTenantId} | Operation: {Operation}",
            currentTenantId,
            requestedTenantId,
            operation);

        throw new UnauthorizedAccessException("Tenant isolation violation");
    }
}

internal static class CommerceHubPublicationCurrencyPolicy
{
    public const string UnsupportedCurrencyMessagePrefix =
        "CommerceHub currently supports EGP products only.";

    public static void EnsureEgpPricedProduct(string? currency)
    {
        var normalizedCurrency = Normalize(currency);
        if (string.Equals(
                normalizedCurrency,
                FinancialCurrencyGuard.PlatformBaseCurrencyCode,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new CommerceHubValidationException(BuildUnsupportedCurrencyMessage(normalizedCurrency));
    }

    public static string Normalize(string? currency) =>
        string.IsNullOrWhiteSpace(currency)
            ? string.Empty
            : currency.Trim().ToUpperInvariant();

    public static bool IsEgpPricedProduct(string? currency) =>
        string.Equals(
            Normalize(currency),
            FinancialCurrencyGuard.PlatformBaseCurrencyCode,
            StringComparison.OrdinalIgnoreCase);

    public static string BuildUnsupportedCurrencyMessage(string? currency)
    {
        var normalizedCurrency = Normalize(currency);
        return string.IsNullOrWhiteSpace(normalizedCurrency)
            ? $"{UnsupportedCurrencyMessagePrefix} This product has no authoritative currency and cannot be published."
            : $"{UnsupportedCurrencyMessagePrefix} This product uses {normalizedCurrency} and cannot be published.";
    }
}

public class PublishToCommerceHubHandler : IPublishToCommerceHubHandler
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly ICommerceHubAttributeService _attributeService;
    private readonly ITenantContext _tenantContext;
    private readonly IUserGovernanceService _userGovernanceService;
    private readonly ICommerceHubPriceAuthority _priceAuthority;
    private readonly ILogger<PublishToCommerceHubHandler> _logger;

    public PublishToCommerceHubHandler(
        ApplicationDbContext dbContext,
        IEventPublisher eventPublisher,
        ICommerceHubAttributeService attributeService,
        ITenantContext tenantContext,
        IUserGovernanceService userGovernanceService,
        ICommerceHubPriceAuthority priceAuthority,
        ILogger<PublishToCommerceHubHandler> logger)
    {
        _dbContext = dbContext;
        _eventPublisher = eventPublisher;
        _attributeService = attributeService;
        _tenantContext = tenantContext;
        _userGovernanceService = userGovernanceService;
        _priceAuthority = priceAuthority;
        _logger = logger;
    }

    public async Task<CommerceHubAdminListingResponse> HandleAsync(
        PublishToCommerceHubCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidatePublishOrUpdate(command.TenantId, command.VariantId, command.CategoryId, command.CommerceHubPrice);
        var tenant = await _dbContext.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == command.TenantId, cancellationToken);
        if (tenant == null)
        {
            throw new CommerceHubValidationException("Tenant not found.");
        }
        CommerceHubTenantIsolationGuard.EnsureTenantAccess(
            _tenantContext,
            command.TenantId,
            _logger,
            "PublishToCommerceHub");
        try
        {
            await _userGovernanceService.EnsureVendorListingAllowedAsync(command.TenantId, cancellationToken);
        }
        catch (UserGovernanceException exception)
        {
            throw new CommerceHubValidationException(exception.Message);
        }
        await EnsureTenantHasActiveShippingAsync(command.TenantId, cancellationToken);
        await EnsureLiveCommerceHubCategoryAsync(command.CategoryId, cancellationToken);

        _logger.LogInformation(
            "CommerceHub publish requested | TenantId: {TenantId} | VariantId: {VariantId} | CategoryId: {CategoryId} | AttributesJson: {AttributesJson}",
            command.TenantId,
            command.VariantId,
            command.CategoryId,
            command.AttributesJson);

        var variant = await LoadVariantAsync(command.TenantId, command.VariantId, cancellationToken);
        CommerceHubPublicationCurrencyPolicy.EnsureEgpPricedProduct(tenant.Currency);

        // ── Attribute validation & normalization ──
        var attrResult = await _attributeService.ValidateAndNormalizeAsync(
            command.CategoryId, command.AttributesJson, cancellationToken);
        if (!attrResult.IsValid)
        {
            throw new CommerceHubValidationException(
                "Attribute validation failed: " + string.Join("; ", attrResult.Errors));
        }
        var normalizedAttrs = attrResult.NormalizedJson;
        var schemaSnapshot = await BuildSchemaSnapshotAsync(command.CategoryId, cancellationToken);

        var listing = await _dbContext.CommerceHubListings
            .FirstOrDefaultAsync(
                x => x.TenantId == command.TenantId && x.VariantId == command.VariantId,
                cancellationToken);

        var effectivePrice = (await _priceAuthority.ResolveAsync(
            variant,
            command.CommerceHubPrice,
            cancellationToken)).FinalPrice;

        if (listing == null)
        {
            listing = CommerceHubListing.Create(
                command.TenantId,
                variant.ProductId,
                variant.Id,
                command.CategoryId,
                command.CommerceHubPrice,
                normalizedAttrs,
                schemaSnapshot,
                command.ActorUserId,
                command.OverrideReason);

            _dbContext.CommerceHubListings.Add(listing);

            await _eventPublisher.PublishAsync(
                BuildCreatedEvent(listing, effectivePrice),
                cancellationToken);
            await PublishPriceOverrideEventAsync(
                _eventPublisher,
                previousValue: null,
                listing,
                command.ActorUserId,
                command.CorrelationId,
                cancellationToken);
            CommerceHubForensicEvidenceWriter.AppendMerchantLifecycle(
                _dbContext,
                listing,
                "Publish",
                command.ActorUserId,
                command.CorrelationId,
                command.RequestId,
                command.OverrideReason ?? "merchant_publish",
                new { listing.CommerceHubCategoryId, listing.AttributesJson, listing.CommerceHubPrice });

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "CommerceHub listing created | TenantId: {TenantId} | ListingId: {ListingId} | VariantId: {VariantId} | CategoryId: {CategoryId} | AttributesJson: {AttributesJson}",
                command.TenantId,
                listing.Id,
                command.VariantId,
                listing.CommerceHubCategoryId,
                listing.AttributesJson);

            return MapResponse(listing, effectivePrice, wasChanged: true);
        }

        var previousOverride = listing.CommerceHubPrice;
        var changed = listing.Publish(command.CategoryId, normalizedAttrs, schemaSnapshot);
        try
        {
            changed |= listing.Update(
                command.CategoryId,
                command.CommerceHubPrice,
                normalizedAttrs,
                schemaSnapshot,
                command.ActorUserId,
                command.OverrideReason,
                command.ExpectedPriceOverrideMutationVersion);
        }
        catch (InvalidOperationException exception)
        {
            throw new CommerceHubValidationException(exception.Message);
        }
        effectivePrice = (await _priceAuthority.ResolveAsync(variant, listing, cancellationToken)).FinalPrice;
        if (!changed)
        {
            return MapResponse(listing, effectivePrice, wasChanged: false);
        }

        await _eventPublisher.PublishAsync(
            BuildUpdatedEvent(listing, effectivePrice),
            cancellationToken);
        await PublishPriceOverrideEventAsync(
            _eventPublisher,
            previousOverride,
            listing,
            command.ActorUserId,
            command.CorrelationId,
            cancellationToken);
        CommerceHubForensicEvidenceWriter.AppendMerchantLifecycle(
            _dbContext,
            listing,
            "PublishOrUpdate",
            command.ActorUserId,
            command.CorrelationId,
            command.RequestId,
            command.OverrideReason ?? "merchant_publish_or_update",
            new { listing.CommerceHubCategoryId, listing.AttributesJson, listing.CommerceHubPrice });

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "CommerceHub listing published/updated | TenantId: {TenantId} | ListingId: {ListingId} | VariantId: {VariantId} | CategoryId: {CategoryId} | AttributesJson: {AttributesJson}",
            command.TenantId,
            listing.Id,
            command.VariantId,
            listing.CommerceHubCategoryId,
            listing.AttributesJson);

        return MapResponse(listing, effectivePrice, wasChanged: true);
    }

    private async Task<ProductVariant> LoadVariantAsync(Guid tenantId, Guid variantId, CancellationToken cancellationToken)
    {
        var variant = await _dbContext.ProductVariants
            .Include(x => x.Product)
            .WherePubliclyVisibleLineage(tenantId)
            .FirstOrDefaultAsync(
                x => x.Id == variantId &&
                     x.TenantId == tenantId &&
                     x.IsActive &&
                     !x.IsDeleted &&
                     !x.Product.IsDeleted &&
                     x.Product.IsActive &&
                     x.Product.IsOnlineVisible &&
                     x.Product.PublishStatus == PointOfSale.Core.Enums.PublishStatus.Published &&
                     x.Product.TenantId == tenantId,
                cancellationToken);

        if (variant == null)
        {
            throw new CommerceHubNotFoundException("Variant not found for this tenant.");
        }

        return variant;
    }

    private async Task EnsureTenantHasActiveShippingAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var hasActiveShipping = await _dbContext.ShippingMethods
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                method => method.TenantId == tenantId &&
                          method.IsActive &&
                          !method.IsDeleted,
                cancellationToken);

        if (hasActiveShipping)
        {
            return;
        }

        throw new CommerceHubValidationException(
            "Configure at least one active shipping method before publishing products to CommerceHub.");
    }

    private async Task EnsureLiveCommerceHubCategoryAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        if (!await CommerceHubCategoryLineageAuthority.IsLiveAsync(_dbContext, categoryId, cancellationToken))
        {
            throw new CommerceHubValidationException("CommerceHub category is not currently available for listing publication.");
        }
    }

    private static CommerceHubListingCreatedEvent BuildCreatedEvent(CommerceHubListing listing, decimal effectivePrice) =>
        new()
        {
            TenantId = listing.TenantId,
            ListingId = listing.Id,
            ProductId = listing.ProductId,
            VariantId = listing.VariantId,
            IsPublished = listing.IsPublished,
            CategoryId = listing.CommerceHubCategoryId,
            CommerceHubPriceOverride = listing.CommerceHubPrice,
            Price = effectivePrice,
            AttributesJson = listing.AttributesJson
        };

    private static CommerceHubListingUpdatedEvent BuildUpdatedEvent(CommerceHubListing listing, decimal effectivePrice) =>
        new()
        {
            TenantId = listing.TenantId,
            ListingId = listing.Id,
            ProductId = listing.ProductId,
            VariantId = listing.VariantId,
            IsPublished = listing.IsPublished,
            CategoryId = listing.CommerceHubCategoryId,
            CommerceHubPriceOverride = listing.CommerceHubPrice,
            Price = effectivePrice,
            AttributesJson = listing.AttributesJson
        };

    internal static CommerceHubAdminListingResponse MapResponse(CommerceHubListing? listing, decimal effectivePrice, bool wasChanged)
    {
        if (listing == null)
        {
            return new CommerceHubAdminListingResponse
            {
                EffectivePrice = effectivePrice,
                WasChanged = wasChanged
            };
        }

        return new CommerceHubAdminListingResponse
        {
            ListingId = listing.Id,
            TenantId = listing.TenantId,
            ProductId = listing.ProductId,
            VariantId = listing.VariantId,
            IsPublished = listing.IsPublished,
            CommerceHubCategoryId = listing.CommerceHubCategoryId,
            CategoryId = listing.CommerceHubCategoryId,
            CommerceHubPrice = listing.CommerceHubPrice,
            PriceOverrideMutationVersion = listing.CommerceHubPriceOverrideMutationVersion,
            EffectivePrice = effectivePrice,
            CreatedAtUtc = listing.CreatedAt,
            UpdatedAtUtc = listing.UpdatedAt ?? listing.CreatedAt,
            WasChanged = wasChanged,
            AttributesJson = listing.AttributesJson
        };
    }

    internal static decimal ResolveEffectivePrice(ProductVariant variant, decimal? overridePrice)
    {
        return new CommerceHubPriceAuthority()
            .ResolveAsync(variant, overridePrice)
            .GetAwaiter()
            .GetResult()
            .FinalPrice;
    }

    internal static void ValidatePublishOrUpdate(Guid tenantId, Guid variantId, Guid categoryId, decimal? commerceHubPrice)
    {
        if (tenantId == Guid.Empty)
            throw new CommerceHubValidationException("TenantId is required.");
        if (variantId == Guid.Empty)
            throw new CommerceHubValidationException("VariantId is required.");
        if (categoryId == Guid.Empty)
            throw new CommerceHubValidationException("CategoryId is required.");
        if (commerceHubPrice is <= 0m)
            throw new CommerceHubValidationException("CommerceHubPrice override must be greater than zero.");
        if (commerceHubPrice.HasValue &&
            !CommerceHubPriceOverrideRuntimePolicy.IsEnabledForTenant(tenantId))
            throw new CommerceHubValidationException("CommerceHub price overrides are disabled for this tenant.");
    }

    internal static async Task PublishPriceOverrideEventAsync(
        IEventPublisher eventPublisher,
        decimal? previousValue,
        CommerceHubListing listing,
        Guid? actorUserId,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        if (previousValue == listing.CommerceHubPrice)
        {
            return;
        }

        if (!actorUserId.HasValue || actorUserId == Guid.Empty)
        {
            throw new CommerceHubValidationException("Merchant actor is required for CommerceHub price override changes.");
        }

        CommerceHubPriceOverrideChangedEvent auditEvent = previousValue switch
        {
            null when listing.CommerceHubPrice.HasValue => new CommerceHubPriceOverrideSavedEvent(),
            not null when listing.CommerceHubPrice is null => new CommerceHubPriceOverrideRemovedEvent(),
            _ => new CommerceHubPriceOverrideUpdatedEvent()
        };

        auditEvent.TenantId = listing.TenantId;
        auditEvent.ListingId = listing.Id;
        auditEvent.ActorUserId = actorUserId.Value;
        auditEvent.UserId = actorUserId.Value;
        auditEvent.PreviousValue = previousValue;
        auditEvent.NewValue = listing.CommerceHubPrice;
        auditEvent.MutationVersion = listing.CommerceHubPriceOverrideMutationVersion;
        auditEvent.Reason = listing.CommerceHubPriceOverrideReason ?? "merchant_settings_update";
        auditEvent.CorrelationId = correlationId;
        await eventPublisher.PublishAsync(auditEvent, cancellationToken);
    }

    /// <summary>
    /// Builds a JSON snapshot of the category's current attribute schema.
    /// Stored on the listing to protect against future schema changes.
    /// </summary>
    internal async Task<string?> BuildSchemaSnapshotAsync(Guid categoryId, CancellationToken ct)
    {
        var attrs = await _dbContext.CommerceHubCategoryAttributes
            .Where(a => a.CategoryId == categoryId)
            .OrderBy(a => a.SortOrder)
            .Select(a => new { a.Name, a.Type, a.IsRequired, a.OptionsJson })
            .ToListAsync(ct);

        if (attrs.Count == 0) return null;
        return JsonSerializer.Serialize(attrs);
    }
}

public class UpdateCommerceHubListingHandler : IUpdateCommerceHubListingHandler
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly ICommerceHubAttributeService _attributeService;
    private readonly ITenantContext _tenantContext;
    private readonly IUserGovernanceService _userGovernanceService;
    private readonly ICommerceHubPriceAuthority _priceAuthority;
    private readonly ILogger<UpdateCommerceHubListingHandler> _logger;

    public UpdateCommerceHubListingHandler(
        ApplicationDbContext dbContext,
        IEventPublisher eventPublisher,
        ICommerceHubAttributeService attributeService,
        ITenantContext tenantContext,
        IUserGovernanceService userGovernanceService,
        ICommerceHubPriceAuthority priceAuthority,
        ILogger<UpdateCommerceHubListingHandler> logger)
    {
        _dbContext = dbContext;
        _eventPublisher = eventPublisher;
        _attributeService = attributeService;
        _tenantContext = tenantContext;
        _userGovernanceService = userGovernanceService;
        _priceAuthority = priceAuthority;
        _logger = logger;
    }

    public async Task<CommerceHubAdminListingResponse> HandleAsync(
        UpdateCommerceHubListingCommand command,
        CancellationToken cancellationToken = default)
    {
        PublishToCommerceHubHandler.ValidatePublishOrUpdate(command.TenantId, command.VariantId, command.CategoryId, command.CommerceHubPrice);
        var tenant = await _dbContext.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == command.TenantId, cancellationToken);
        if (tenant == null)
        {
            throw new CommerceHubValidationException("Tenant not found.");
        }
        CommerceHubTenantIsolationGuard.EnsureTenantAccess(
            _tenantContext,
            command.TenantId,
            _logger,
            "UpdateCommerceHubListing");
        try
        {
            await _userGovernanceService.EnsureVendorListingAllowedAsync(command.TenantId, cancellationToken);
        }
        catch (UserGovernanceException exception)
        {
            throw new CommerceHubValidationException(exception.Message);
        }
        await EnsureLiveCommerceHubCategoryAsync(command.CategoryId, cancellationToken);

        _logger.LogInformation(
            "CommerceHub update requested | TenantId: {TenantId} | VariantId: {VariantId} | CategoryId: {CategoryId} | AttributesJson: {AttributesJson}",
            command.TenantId,
            command.VariantId,
            command.CategoryId,
            command.AttributesJson);

        var variant = await _dbContext.ProductVariants
            .Include(x => x.Product)
            .WherePubliclyVisibleLineage(command.TenantId)
            .FirstOrDefaultAsync(
                x => x.Id == command.VariantId &&
                     x.TenantId == command.TenantId &&
                     x.IsActive &&
                     !x.IsDeleted &&
                     !x.Product.IsDeleted &&
                     x.Product.IsActive &&
                     x.Product.IsOnlineVisible &&
                     x.Product.PublishStatus == PointOfSale.Core.Enums.PublishStatus.Published &&
                     x.Product.TenantId == command.TenantId,
                cancellationToken);

        if (variant == null)
        {
            throw new CommerceHubNotFoundException("Variant not found for this tenant.");
        }
        CommerceHubPublicationCurrencyPolicy.EnsureEgpPricedProduct(tenant.Currency);

        var listing = await _dbContext.CommerceHubListings
            .FirstOrDefaultAsync(
                x => x.TenantId == command.TenantId && x.VariantId == command.VariantId,
                cancellationToken);

        if (listing == null)
        {
            throw new CommerceHubNotFoundException("CommerceHub listing not found for this variant.");
        }

        // ── Attribute validation & normalization ──
        var attrResult = await _attributeService.ValidateAndNormalizeAsync(
            command.CategoryId, command.AttributesJson, cancellationToken);
        if (!attrResult.IsValid)
        {
            throw new CommerceHubValidationException(
                "Attribute validation failed: " + string.Join("; ", attrResult.Errors));
        }
        var normalizedAttrs = attrResult.NormalizedJson;

        // Load schema snapshot (same helper pattern as publish handler)
        var schemaSnapshot = await BuildSchemaSnapshotAsync(command.CategoryId, cancellationToken);

        var previousOverride = listing.CommerceHubPrice;
        bool changed;
        try
        {
            changed = listing.Update(
                command.CategoryId,
                command.CommerceHubPrice,
                normalizedAttrs,
                schemaSnapshot,
                command.ActorUserId,
                command.OverrideReason,
                command.ExpectedPriceOverrideMutationVersion);
        }
        catch (InvalidOperationException exception)
        {
            throw new CommerceHubValidationException(exception.Message);
        }
        var effectivePrice = (await _priceAuthority.ResolveAsync(
            variant,
            listing,
            cancellationToken)).FinalPrice;
        if (!changed)
        {
            return PublishToCommerceHubHandler.MapResponse(listing, effectivePrice, wasChanged: false);
        }

        await _eventPublisher.PublishAsync(
            new CommerceHubListingUpdatedEvent
            {
                TenantId = listing.TenantId,
                ListingId = listing.Id,
                ProductId = listing.ProductId,
                VariantId = listing.VariantId,
                IsPublished = listing.IsPublished,
                CategoryId = listing.CommerceHubCategoryId,
                CommerceHubPriceOverride = listing.CommerceHubPrice,
                Price = effectivePrice,
                AttributesJson = listing.AttributesJson
            },
            cancellationToken);
        await PublishToCommerceHubHandler.PublishPriceOverrideEventAsync(
            _eventPublisher,
            previousOverride,
            listing,
            command.ActorUserId,
            command.CorrelationId,
            cancellationToken);
        CommerceHubForensicEvidenceWriter.AppendMerchantLifecycle(
            _dbContext,
            listing,
            "Update",
            command.ActorUserId,
            command.CorrelationId,
            command.RequestId,
            command.OverrideReason ?? "merchant_update",
            new { listing.CommerceHubCategoryId, listing.AttributesJson, listing.CommerceHubPrice });

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "CommerceHub listing updated | TenantId: {TenantId} | ListingId: {ListingId} | VariantId: {VariantId} | CategoryId: {CategoryId} | AttributesJson: {AttributesJson}",
            listing.TenantId,
            listing.Id,
            listing.VariantId,
            listing.CommerceHubCategoryId,
            listing.AttributesJson);

        return PublishToCommerceHubHandler.MapResponse(listing, effectivePrice, wasChanged: true);
    }

    private async Task<string?> BuildSchemaSnapshotAsync(Guid categoryId, CancellationToken ct)
    {
        var attrs = await _dbContext.CommerceHubCategoryAttributes
            .Where(a => a.CategoryId == categoryId)
            .OrderBy(a => a.SortOrder)
            .Select(a => new { a.Name, a.Type, a.IsRequired, a.OptionsJson })
            .ToListAsync(ct);

        if (attrs.Count == 0) return null;
        return JsonSerializer.Serialize(attrs);
    }

    private async Task EnsureLiveCommerceHubCategoryAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        if (!await CommerceHubCategoryLineageAuthority.IsLiveAsync(_dbContext, categoryId, cancellationToken))
        {
            throw new CommerceHubValidationException("CommerceHub category is not currently available for listing publication.");
        }
    }
}

public class UnpublishFromCommerceHubHandler : IUnpublishFromCommerceHubHandler
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly ITenantContext _tenantContext;
    private readonly ICommerceHubPriceAuthority _priceAuthority;
    private readonly ILogger<UnpublishFromCommerceHubHandler> _logger;

    public UnpublishFromCommerceHubHandler(
        ApplicationDbContext dbContext,
        IEventPublisher eventPublisher,
        ITenantContext tenantContext,
        ICommerceHubPriceAuthority priceAuthority,
        ILogger<UnpublishFromCommerceHubHandler> logger)
    {
        _dbContext = dbContext;
        _eventPublisher = eventPublisher;
        _tenantContext = tenantContext;
        _priceAuthority = priceAuthority;
        _logger = logger;
    }

    public async Task<CommerceHubAdminListingResponse> HandleAsync(
        UnpublishFromCommerceHubCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.TenantId == Guid.Empty)
            throw new CommerceHubValidationException("TenantId is required.");
        if (command.VariantId == Guid.Empty)
            throw new CommerceHubValidationException("VariantId is required.");

        CommerceHubTenantIsolationGuard.EnsureTenantAccess(
            _tenantContext,
            command.TenantId,
            _logger,
            "UnpublishFromCommerceHub");

        var listing = await _dbContext.CommerceHubListings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                x => x.TenantId == command.TenantId &&
                     x.VariantId == command.VariantId &&
                     !x.IsDeleted,
                cancellationToken);

        // Withdrawal only reduces visibility. Keep it available after catalog
        // deletion or semantic replacement so stale restore intent is cleared.
        var variant = await _dbContext.ProductVariants
            .IgnoreQueryFilters()
            .Include(x => x.Product)
            .FirstOrDefaultAsync(
                x => x.Id == command.VariantId &&
                     x.TenantId == command.TenantId &&
                     x.Product.TenantId == command.TenantId,
                cancellationToken);

        if (listing == null && variant == null)
        {
            throw new CommerceHubNotFoundException("Variant not found for this tenant.");
        }

        var effectivePrice = variant == null
            ? 0m
            : (await _priceAuthority.ResolveAsync(
                variant,
                listing,
                cancellationToken)).FinalPrice;

        if (listing == null)
        {
            return new CommerceHubAdminListingResponse
            {
                TenantId = command.TenantId,
                ProductId = variant!.ProductId,
                VariantId = variant.Id,
                IsPublished = false,
                EffectivePrice = effectivePrice,
                WasChanged = false
            };
        }

        var changed = listing.Unpublish();
        if (!changed)
        {
            return PublishToCommerceHubHandler.MapResponse(listing, effectivePrice, wasChanged: false);
        }

        await _eventPublisher.PublishAsync(
            new CommerceHubListingUnpublishedEvent
            {
                TenantId = listing.TenantId,
                ListingId = listing.Id,
                ProductId = listing.ProductId,
                VariantId = listing.VariantId,
                IsPublished = listing.IsPublished,
                CategoryId = listing.CommerceHubCategoryId,
                CommerceHubPriceOverride = listing.CommerceHubPrice,
                Price = effectivePrice
            },
            cancellationToken);
        CommerceHubForensicEvidenceWriter.AppendMerchantLifecycle(
            _dbContext,
            listing,
            "Unpublish",
            command.ActorUserId,
            command.CorrelationId,
            command.RequestId,
            command.Reason ?? "merchant_unpublish",
            new { listing.CommerceHubCategoryId, listing.AttributesJson, listing.CommerceHubPrice });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return PublishToCommerceHubHandler.MapResponse(listing, effectivePrice, wasChanged: true);
    }
}
