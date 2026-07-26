using MassTransit;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.CommerceHub.Handlers;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICommerceHubVendorVisibilityService
{
    Task<CommerceHubVendorVisibilitySyncResult> SyncVendorVisibilityAsync(
        Guid vendorId,
        UserGovernanceStatus targetStatus,
        CancellationToken cancellationToken = default);
}

public sealed class CommerceHubVendorVisibilitySnapshotItem
{
    public Guid ListingId { get; init; }
    public Guid ProductId { get; init; }
    public Guid VariantId { get; init; }
    public bool WasPublished { get; init; }
    public bool RestorePublicationOnActivateBefore { get; init; }
}

public sealed class CommerceHubVendorVisibilitySyncResult
{
    public Guid VendorId { get; init; }
    public UserGovernanceStatus TargetStatus { get; init; }
    public IReadOnlyList<CommerceHubVendorVisibilitySnapshotItem> ListingSnapshot { get; init; } =
        Array.Empty<CommerceHubVendorVisibilitySnapshotItem>();
    public int HiddenCount { get; init; }
    public int RestoredCount { get; init; }
    public int ClearedCount { get; init; }
    public int SearchVerificationEnqueuedCount { get; init; }
}

public sealed class CommerceHubVendorVisibilityService : ICommerceHubVendorVisibilityService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly ICommerceHubAutoHealingQueue _autoHealingQueue;
    private readonly ILogger<CommerceHubVendorVisibilityService> _logger;

    public CommerceHubVendorVisibilityService(
        ApplicationDbContext dbContext,
        IEventPublisher eventPublisher,
        ICommerceHubAutoHealingQueue autoHealingQueue,
        ILogger<CommerceHubVendorVisibilityService> logger)
    {
        _dbContext = dbContext;
        _eventPublisher = eventPublisher;
        _autoHealingQueue = autoHealingQueue;
        _logger = logger;
    }

    public async Task<CommerceHubVendorVisibilitySyncResult> SyncVendorVisibilityAsync(
        Guid vendorId,
        UserGovernanceStatus targetStatus,
        CancellationToken cancellationToken = default)
    {
        if (vendorId == Guid.Empty)
        {
            return new CommerceHubVendorVisibilitySyncResult
            {
                VendorId = vendorId,
                TargetStatus = targetStatus
            };
        }

        switch (targetStatus)
        {
            case UserGovernanceStatus.Suspended:
            case UserGovernanceStatus.Banned:
                return await HideVendorListingsAsync(vendorId, targetStatus, cancellationToken);
            case UserGovernanceStatus.Active:
                return await RestoreVendorListingsAsync(vendorId, cancellationToken);
            default:
                return new CommerceHubVendorVisibilitySyncResult
                {
                    VendorId = vendorId,
                    TargetStatus = targetStatus
                };
        }
    }

    private async Task<CommerceHubVendorVisibilitySyncResult> HideVendorListingsAsync(
        Guid vendorId,
        UserGovernanceStatus targetStatus,
        CancellationToken cancellationToken)
    {
        var listings = await _dbContext.CommerceHubListings
            .IgnoreQueryFilters()
            .Where(listing =>
                listing.TenantId == vendorId &&
                !listing.IsDeleted &&
                listing.IsPublished)
            .ToListAsync(cancellationToken);

        if (listings.Count == 0)
        {
            return new CommerceHubVendorVisibilitySyncResult
            {
                VendorId = vendorId,
                TargetStatus = targetStatus
            };
        }

        var snapshot = listings
            .Select(listing => new CommerceHubVendorVisibilitySnapshotItem
            {
                ListingId = listing.Id,
                ProductId = listing.ProductId,
                VariantId = listing.VariantId,
                WasPublished = listing.IsPublished,
                RestorePublicationOnActivateBefore = listing.RestorePublicationOnActivate
            })
            .ToList();

        var variants = await LoadVariantsAsync(
            vendorId,
            listings.Select(listing => listing.VariantId),
            includeInactive: true,
            cancellationToken);

        var changedCount = 0;
        foreach (var listing in listings)
        {
            if (!listing.HideByGovernance())
            {
                continue;
            }

            changedCount++;
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
                    Price = ResolveEffectivePrice(listing, variants)
                },
                cancellationToken);
        }

        if (changedCount == 0)
        {
            return new CommerceHubVendorVisibilitySyncResult
            {
                VendorId = vendorId,
                TargetStatus = targetStatus,
                ListingSnapshot = snapshot
            };
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        var enqueuedCount = ScheduleSearchVerification(vendorId, targetStatus, snapshot.Select(item => item.ListingId));

        _logger.LogInformation(
            "CommerceHub vendor listings hidden by governance action | VendorId: {VendorId} | Status: {Status} | ListingCount: {ListingCount} | SearchVerificationEnqueued: {SearchVerificationEnqueued}",
            vendorId,
            targetStatus,
            changedCount,
            enqueuedCount);

        return new CommerceHubVendorVisibilitySyncResult
        {
            VendorId = vendorId,
            TargetStatus = targetStatus,
            ListingSnapshot = snapshot,
            HiddenCount = changedCount,
            SearchVerificationEnqueuedCount = enqueuedCount
        };
    }

    private async Task<CommerceHubVendorVisibilitySyncResult> RestoreVendorListingsAsync(
        Guid vendorId,
        CancellationToken cancellationToken)
    {
        var listings = await _dbContext.CommerceHubListings
            .IgnoreQueryFilters()
            .Where(listing =>
                listing.TenantId == vendorId &&
                !listing.IsDeleted &&
                listing.RestorePublicationOnActivate)
            .ToListAsync(cancellationToken);

        if (listings.Count == 0)
        {
            return new CommerceHubVendorVisibilitySyncResult
            {
                VendorId = vendorId,
                TargetStatus = UserGovernanceStatus.Active
            };
        }

        var snapshot = listings
            .Select(listing => new CommerceHubVendorVisibilitySnapshotItem
            {
                ListingId = listing.Id,
                ProductId = listing.ProductId,
                VariantId = listing.VariantId,
                WasPublished = listing.IsPublished,
                RestorePublicationOnActivateBefore = listing.RestorePublicationOnActivate
            })
            .ToList();

        var variants = await LoadVariantsAsync(
            vendorId,
            listings.Select(listing => listing.VariantId),
            includeInactive: false,
            cancellationToken);

        var vendorAuthority = await CommerceHubVendorAuthority.LoadAsync(
            _dbContext,
            vendorId,
            cancellationToken);

        var productIds = listings
            .Select(listing => listing.ProductId)
            .Distinct()
            .ToList();

        var visibleProductIds = await _dbContext.Products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(product =>
                product.TenantId == vendorId &&
                productIds.Contains(product.Id) &&
                !product.IsDeleted &&
                product.IsActive &&
                product.IsOnlineVisible &&
                product.PublishStatus == PublishStatus.Published)
            .Select(product => product.Id)
            .ToListAsync(cancellationToken);

        var visibleProductIdSet = visibleProductIds.ToHashSet();
        var liveCategoryIds = await CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync(
            _dbContext,
            listings.Select(listing => listing.CommerceHubCategoryId),
            cancellationToken);
        var changedCount = 0;
        var restoredCount = 0;

        foreach (var listing in listings)
        {
            if (!vendorAuthority.IsMarketplaceEligible ||
                !variants.TryGetValue(listing.VariantId, out var variant) ||
                variant.ProductId != listing.ProductId ||
                !visibleProductIdSet.Contains(listing.ProductId) ||
                !liveCategoryIds.Contains(listing.CommerceHubCategoryId))
            {
                if (listing.ClearGovernanceRestoreState())
                {
                    changedCount++;
                }

                continue;
            }

            if (!listing.RestoreAfterGovernance())
            {
                continue;
            }

            changedCount++;
            restoredCount++;

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
                    Price = PublishToCommerceHubHandler.ResolveEffectivePrice(variant, listing.CommerceHubPrice),
                    AttributesJson = listing.AttributesJson
                },
                cancellationToken);
        }

        if (changedCount == 0)
        {
            return new CommerceHubVendorVisibilitySyncResult
            {
                VendorId = vendorId,
                TargetStatus = UserGovernanceStatus.Active,
                ListingSnapshot = snapshot
            };
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        var enqueuedCount = ScheduleSearchVerification(vendorId, UserGovernanceStatus.Active, snapshot.Select(item => item.ListingId));

        _logger.LogInformation(
            "CommerceHub vendor listings restored after governance reactivation | VendorId: {VendorId} | RestoredCount: {RestoredCount} | ClearedCount: {ClearedCount} | SearchVerificationEnqueued: {SearchVerificationEnqueued}",
            vendorId,
            restoredCount,
            changedCount - restoredCount,
            enqueuedCount);

        return new CommerceHubVendorVisibilitySyncResult
        {
            VendorId = vendorId,
            TargetStatus = UserGovernanceStatus.Active,
            ListingSnapshot = snapshot,
            RestoredCount = restoredCount,
            ClearedCount = changedCount - restoredCount,
            SearchVerificationEnqueuedCount = enqueuedCount
        };
    }

    private async Task<Dictionary<Guid, ProductVariant>> LoadVariantsAsync(
        Guid vendorId,
        IEnumerable<Guid> variantIds,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var normalizedVariantIds = variantIds
            .Where(variantId => variantId != Guid.Empty)
            .Distinct()
            .ToList();

        if (normalizedVariantIds.Count == 0)
        {
            return new Dictionary<Guid, ProductVariant>();
        }

        var query = _dbContext.ProductVariants
            .IgnoreQueryFilters()
            .Where(variant =>
                variant.TenantId == vendorId &&
                normalizedVariantIds.Contains(variant.Id) &&
                !variant.IsDeleted &&
                variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
                variant.SemanticSupersededAtUtc == null &&
                variant.SemanticReplacementVariantId == null);

        if (!includeInactive)
        {
            query = query.Where(variant => variant.IsActive);
        }

        var variants = await query.ToListAsync(cancellationToken);
        return variants.ToDictionary(variant => variant.Id);
    }

    private static decimal ResolveEffectivePrice(
        CommerceHubListing listing,
        IReadOnlyDictionary<Guid, ProductVariant> variants)
    {
        if (variants.TryGetValue(listing.VariantId, out var variant))
        {
            return PublishToCommerceHubHandler.ResolveEffectivePrice(variant, listing.CommerceHubPrice);
        }

        return 0m;
    }

    private int ScheduleSearchVerification(
        Guid vendorId,
        UserGovernanceStatus targetStatus,
        IEnumerable<Guid> listingIds)
    {
        var normalizedListingIds = listingIds
            .Where(listingId => listingId != Guid.Empty)
            .Distinct()
            .ToList();

        if (normalizedListingIds.Count == 0)
        {
            return 0;
        }

        var enqueuedCount = _autoHealingQueue.Enqueue(new CommerceHubAutoHealingTask
        {
            ListingIds = normalizedListingIds,
            Reason = targetStatus == UserGovernanceStatus.Active
                ? "GOVERNANCE_RESTORE"
                : "GOVERNANCE_HIDE",
            TriggeredAtUtc = DateTime.UtcNow,
            TenantId = vendorId,
            IsHighPriority = false
        });

        if (enqueuedCount == 0)
        {
            _logger.LogWarning(
                "CommerceHub governance search verification could not be enqueued | VendorId: {VendorId} | Status: {Status} | ListingCount: {ListingCount}",
                vendorId,
                targetStatus,
                normalizedListingIds.Count);
        }

        return enqueuedCount;
    }
}
