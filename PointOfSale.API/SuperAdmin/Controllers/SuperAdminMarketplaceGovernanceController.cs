using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.CommerceHub.Handlers;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Models;
using PointOfSale.API.SuperAdmin.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Pagination;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.SuperAdmin.Controllers;

[Route("api/super-admin/marketplace-governance")]
public sealed class SuperAdminMarketplaceGovernanceController : SuperAdminBaseController
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly ICommerceHubAutoHealingQueue _autoHealingQueue;
    private readonly ISuperAdminAuditService _auditService;
    private readonly IMarketplaceEventOutboxService _marketplaceEventOutbox;
    private readonly ILogger<SuperAdminMarketplaceGovernanceController> _logger;

    public SuperAdminMarketplaceGovernanceController(
        ApplicationDbContext dbContext,
        IEventPublisher eventPublisher,
        ICommerceHubAutoHealingQueue autoHealingQueue,
        ISuperAdminAuditService auditService,
        IMarketplaceEventOutboxService marketplaceEventOutbox,
        ILogger<SuperAdminMarketplaceGovernanceController> logger)
    {
        _dbContext = dbContext;
        _eventPublisher = eventPublisher;
        _autoHealingQueue = autoHealingQueue;
        _auditService = auditService;
        _marketplaceEventOutbox = marketplaceEventOutbox;
        _logger = logger;
    }

    [HttpGet("dashboard")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDashboard(CancellationToken cancellationToken)
    {
        var listings = _dbContext.CommerceHubListings
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(listing => !listing.IsDeleted);

        var vendors = _dbContext.UserGovernanceProfiles
            .AsNoTracking()
            .Where(profile => profile.Role == UserGovernanceRole.Vendor);

        var tenants = _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters();

        var sinceUtc = DateTime.UtcNow.AddDays(-7);
        var dashboard = new SuperAdminMarketplaceGovernanceDashboardDto
        {
            TotalListings = await listings.CountAsync(cancellationToken),
            PublishedListings = await listings.CountAsync(
                listing => listing.IsPublished && !listing.RestorePublicationOnActivate,
                cancellationToken),
            SuppressedListings = await listings.CountAsync(
                listing => listing.RestorePublicationOnActivate,
                cancellationToken),
            OrphanedListings = await CountOrphanedListingsAsync(cancellationToken),
            ActiveVendors = await vendors.CountAsync(
                profile => profile.Status == UserGovernanceStatus.Active,
                cancellationToken),
            SuspendedVendors = await vendors.CountAsync(
                profile => profile.Status == UserGovernanceStatus.Suspended,
                cancellationToken),
            BannedVendors = await vendors.CountAsync(
                profile => profile.Status == UserGovernanceStatus.Banned,
                cancellationToken),
            ActiveTenants = await tenants.CountAsync(
                tenant => tenant.Status == TenantStatus.Active && tenant.DeletedAtUtc == null,
                cancellationToken),
            SuspendedTenants = await tenants.CountAsync(
                tenant => tenant.Status == TenantStatus.Suspended,
                cancellationToken),
            DeletedTenants = await tenants.CountAsync(
                tenant => tenant.Status == TenantStatus.Deleted || tenant.DeletedAtUtc != null,
                cancellationToken),
            RecentGovernanceAuditCount = await _dbContext.UserGovernanceAuditLogs
                .AsNoTracking()
                .CountAsync(entry => entry.TimestampUtc >= sinceUtc, cancellationToken),
            GeneratedAtUtc = DateTime.UtcNow
        };

        await _auditService.LogActionAsync(
            action: "MARKETPLACE_GOVERNANCE_DASHBOARD_VIEWED",
            entityType: "MarketplaceGovernance",
            entityId: "dashboard",
            description: "Super Admin viewed the marketplace governance dashboard.",
            success: true,
            details: dashboard,
            actorId: CurrentSuperAdminId,
            actorEmail: CurrentSuperAdminEmail,
            cancellationToken: cancellationToken);

        return OkResponse(dashboard);
    }

    [HttpGet("listings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchListings(
        [FromQuery] SuperAdminMarketplaceGovernanceListingQuery request,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateListingQuery(request);
        if (validationError != null)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "INVALID_QUERY", validationError);
        }

        var page = PaginationGuard.NormalizePage(request.Page);
        var pageSize = PaginationGuard.NormalizePageSize(request.PageSize);
        var query = BuildListingGovernanceQuery();

        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var search = request.Q.Trim().ToLowerInvariant();
            query = query.Where(row =>
                row.ProductName.ToLower().Contains(search) ||
                (row.VariantName != null && row.VariantName.ToLower().Contains(search)) ||
                (row.Sku != null && row.Sku.ToLower().Contains(search)) ||
                (row.TenantName != null && row.TenantName.ToLower().Contains(search)) ||
                (row.TenantSlug != null && row.TenantSlug.ToLower().Contains(search)) ||
                (row.CategoryName != null && row.CategoryName.ToLower().Contains(search)));
        }

        if (request.TenantId.HasValue)
        {
            query = query.Where(row => row.TenantId == request.TenantId.Value);
        }

        if (request.CategoryId.HasValue)
        {
            query = query.Where(row => row.CategoryId == request.CategoryId.Value);
        }

        if (request.VendorStatus.HasValue)
        {
            var vendorStatus = request.VendorStatus.Value.ToString();
            query = query.Where(row => row.VendorGovernanceStatus == vendorStatus);
        }

        if (request.TenantStatus.HasValue)
        {
            var tenantStatus = request.TenantStatus.Value.ToString();
            query = query.Where(row => row.TenantStatus == tenantStatus);
        }

        if (request.OverrideEnabled.HasValue)
        {
            query = query.Where(row => row.OverridePresent == request.OverrideEnabled.Value);
        }

        query = (request.Status ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "published" => query.Where(row => row.IsPublished && !row.IsSuppressed && !row.IsOrphaned),
            "unpublished" => query.Where(row => !row.IsPublished && !row.IsSuppressed),
            "suppressed" => query.Where(row => row.IsSuppressed),
            "orphaned" => query.Where(row => row.IsOrphaned),
            "vendor_suspended" => query.Where(row =>
                row.VendorGovernanceStatus == UserGovernanceStatus.Suspended.ToString() ||
                row.VendorGovernanceStatus == UserGovernanceStatus.Banned.ToString()),
            "tenant_suspended" => query.Where(row => row.TenantStatus == TenantStatus.Suspended.ToString()),
            _ => query
        };

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(row => row.IsSuppressed)
            .ThenByDescending(row => row.IsOrphaned)
            .ThenBy(row => row.TenantName)
            .ThenBy(row => row.ProductName)
            .Skip(PaginationGuard.GetSkip(page, pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        await _auditService.LogActionAsync(
            action: "MARKETPLACE_GOVERNANCE_LISTINGS_SEARCHED",
            entityType: "MarketplaceGovernance",
            entityId: "listings",
            description: "Super Admin searched SQL-authoritative marketplace governance listings.",
            success: true,
            details: new
            {
                request.Page,
                request.PageSize,
                request.Q,
                request.Status,
                request.TenantId,
                request.CategoryId,
                request.VendorStatus,
                request.TenantStatus,
                request.OverrideEnabled,
                totalCount
            },
            actorId: CurrentSuperAdminId,
            actorEmail: CurrentSuperAdminEmail,
            cancellationToken: cancellationToken);

        return OkResponse(
            new SuperAdminMarketplaceGovernanceListingSearchResponseDto { Listings = rows },
            new Dictionary<string, object?>
            {
                ["page"] = page,
                ["pageSize"] = pageSize,
                ["totalCount"] = totalCount,
                ["totalPages"] = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
            });
    }

    [HttpPost("listings/{listingId:guid}/suppress")]
    [EnableRateLimiting(SuperAdminSecurityConstants.GovernanceWriteRateLimitPolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SuppressListing(
        Guid listingId,
        [FromBody] SuperAdminMarketplaceGovernanceActionRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!CurrentSuperAdminId.HasValue || CurrentSuperAdminId.Value == Guid.Empty)
        {
            return ErrorResponse(
                StatusCodes.Status403Forbidden,
                "SUPER_ADMIN_CONTEXT_REQUIRED",
                "A valid Super Admin identity is required for marketplace governance actions.");
        }

        var listing = await _dbContext.CommerceHubListings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == listingId && !item.IsDeleted, cancellationToken);

        if (listing == null)
        {
            return ErrorResponse(StatusCodes.Status404NotFound, "LISTING_NOT_FOUND", "CommerceHub listing was not found.");
        }

        var changed = listing.HideByGovernance();
        if (changed)
        {
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
                    Price = await ResolveEffectivePriceAsync(listing, cancellationToken)
                },
                cancellationToken);
            CommerceHubForensicEvidenceWriter.AppendMerchantLifecycle(
                _dbContext,
                listing,
                "GovernanceSuppression",
                CurrentSuperAdminId,
                HttpContext.TraceIdentifier,
                HttpContext.TraceIdentifier,
                request.Reason,
                new { listing.TenantId, listing.ProductId, listing.VariantId });
            await StageMarketplaceProductReadModelRebuildAsync(
                listing,
                "CommerceHubListingGovernanceSuppressed",
                cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
            EnqueueListingVerification(listing, "SUPER_ADMIN_LISTING_SUPPRESSION");
        }

        await LogMarketplaceGovernanceActionAsync(
            "MARKETPLACE_LISTING_SUPPRESSED",
            "CommerceHubListing",
            listing.Id,
            "Super Admin suppressed marketplace listing visibility.",
            request.Reason,
            new { listing.TenantId, listing.ProductId, listing.VariantId, changed },
            cancellationToken);

        return OkResponse(new SuperAdminMarketplaceGovernanceActionResultDto
        {
            EntityId = listing.Id,
            EntityType = "CommerceHubListing",
            Action = "suppress",
            Changed = changed,
            VisibilityState = "suppressed",
            Message = changed
                ? "Listing visibility was suppressed by marketplace governance."
                : "Listing was already hidden or suppressed.",
            AppliedAtUtc = DateTime.UtcNow
        });
    }

    [HttpPost("listings/{listingId:guid}/restore")]
    [EnableRateLimiting(SuperAdminSecurityConstants.GovernanceWriteRateLimitPolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> RestoreListing(
        Guid listingId,
        [FromBody] SuperAdminMarketplaceGovernanceActionRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!CurrentSuperAdminId.HasValue || CurrentSuperAdminId.Value == Guid.Empty)
        {
            return ErrorResponse(
                StatusCodes.Status403Forbidden,
                "SUPER_ADMIN_CONTEXT_REQUIRED",
                "A valid Super Admin identity is required for marketplace governance actions.");
        }

        var listing = await _dbContext.CommerceHubListings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == listingId && !item.IsDeleted, cancellationToken);

        if (listing == null)
        {
            return ErrorResponse(StatusCodes.Status404NotFound, "LISTING_NOT_FOUND", "CommerceHub listing was not found.");
        }

        var authorityFailure = await GetRestoreAuthorityFailureAsync(listing, cancellationToken);
        if (authorityFailure != null)
        {
            if (listing.ClearGovernanceRestoreState())
            {
                CommerceHubForensicEvidenceWriter.AppendMerchantLifecycle(
                    _dbContext,
                    listing,
                    "GovernanceRestoreRejected",
                    CurrentSuperAdminId,
                    HttpContext.TraceIdentifier,
                    HttpContext.TraceIdentifier,
                    request.Reason,
                    new { authorityFailure });
                await _dbContext.SaveChangesAsync(cancellationToken);
                EnqueueListingVerification(listing, "SUPER_ADMIN_LISTING_RESTORE_REJECTED");
            }

            await LogMarketplaceGovernanceActionAsync(
                "MARKETPLACE_LISTING_RESTORE_REJECTED",
                "CommerceHubListing",
                listing.Id,
                "Super Admin restore was rejected because current SQL authority no longer accepts visibility.",
                request.Reason,
                new { listing.TenantId, listing.ProductId, listing.VariantId, authorityFailure },
                cancellationToken);

            return ErrorResponse(StatusCodes.Status409Conflict, "RESTORE_AUTHORITY_REJECTED", authorityFailure);
        }

        var changed = listing.RestoreAfterGovernance();
        if (changed)
        {
            var variant = await _dbContext.ProductVariants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstAsync(variant => variant.Id == listing.VariantId, cancellationToken);

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
            CommerceHubForensicEvidenceWriter.AppendMerchantLifecycle(
                _dbContext,
                listing,
                "GovernanceRestore",
                CurrentSuperAdminId,
                HttpContext.TraceIdentifier,
                HttpContext.TraceIdentifier,
                request.Reason,
                new { listing.TenantId, listing.ProductId, listing.VariantId });
            await StageMarketplaceProductReadModelRebuildAsync(
                listing,
                "CommerceHubListingGovernanceRestored",
                cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
            EnqueueListingVerification(listing, "SUPER_ADMIN_LISTING_RESTORE");
        }

        await LogMarketplaceGovernanceActionAsync(
            "MARKETPLACE_LISTING_RESTORED",
            "CommerceHubListing",
            listing.Id,
            "Super Admin restored marketplace listing visibility after SQL authority validation.",
            request.Reason,
            new { listing.TenantId, listing.ProductId, listing.VariantId, changed },
            cancellationToken);

        return OkResponse(new SuperAdminMarketplaceGovernanceActionResultDto
        {
            EntityId = listing.Id,
            EntityType = "CommerceHubListing",
            Action = "restore",
            Changed = changed,
            VisibilityState = listing.IsPublished ? "published" : "unpublished",
            Message = changed
                ? "Listing visibility was restored after current SQL authority validation."
                : "Listing did not carry governance restore intent.",
            AppliedAtUtc = DateTime.UtcNow
        });
    }

    private async Task StageMarketplaceProductReadModelRebuildAsync(
        CommerceHubListing listing,
        string changedField,
        CancellationToken cancellationToken)
    {
        var product = await _dbContext.Products
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                item => item.Id == listing.ProductId && item.TenantId == listing.TenantId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"CommerceHub listing {listing.Id} is missing its authoritative product {listing.ProductId}.");

        await PublishToCommerceHubHandler.StageMarketplaceProductReadModelRebuildAsync(
            _dbContext,
            _marketplaceEventOutbox,
            product,
            listing,
            HttpContext.TraceIdentifier,
            changedField,
            cancellationToken);
    }

    [HttpPost("tenants/{tenantId:guid}/suspend")]
    [EnableRateLimiting(SuperAdminSecurityConstants.GovernanceWriteRateLimitPolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<IActionResult> SuspendTenant(
        Guid tenantId,
        [FromBody] SuperAdminMarketplaceGovernanceActionRequestDto request,
        CancellationToken cancellationToken) =>
        ApplyTenantGovernanceAsync(tenantId, TenantStatus.Suspended, request, cancellationToken);

    [HttpPost("tenants/{tenantId:guid}/restore")]
    [EnableRateLimiting(SuperAdminSecurityConstants.GovernanceWriteRateLimitPolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<IActionResult> RestoreTenant(
        Guid tenantId,
        [FromBody] SuperAdminMarketplaceGovernanceActionRequestDto request,
        CancellationToken cancellationToken) =>
        ApplyTenantGovernanceAsync(tenantId, TenantStatus.Active, request, cancellationToken);

    private async Task<IActionResult> ApplyTenantGovernanceAsync(
        Guid tenantId,
        TenantStatus targetStatus,
        SuperAdminMarketplaceGovernanceActionRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!CurrentSuperAdminId.HasValue || CurrentSuperAdminId.Value == Guid.Empty)
        {
            return ErrorResponse(
                StatusCodes.Status403Forbidden,
                "SUPER_ADMIN_CONTEXT_REQUIRED",
                "A valid Super Admin identity is required for tenant governance actions.");
        }

        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == tenantId, cancellationToken);

        if (tenant == null || tenant.DeletedAtUtc != null)
        {
            return ErrorResponse(StatusCodes.Status404NotFound, "TENANT_NOT_FOUND", "Tenant was not found.");
        }

        var wasActive = tenant.IsActive;
        var beforeStatus = tenant.Status;
        var now = DateTime.UtcNow;

        tenant.SetLifecycleStatus(
            targetStatus,
            targetStatus == TenantStatus.Suspended ? request.Reason.Trim() : null,
            now);

        await _dbContext.SaveChangesAsync(cancellationToken);
        EnqueueTenantListingVerification(tenant.Id, targetStatus == TenantStatus.Active ? "SUPER_ADMIN_TENANT_RESTORE" : "SUPER_ADMIN_TENANT_SUSPENSION");

        await LogMarketplaceGovernanceActionAsync(
            targetStatus == TenantStatus.Active ? "MARKETPLACE_TENANT_RESTORED" : "MARKETPLACE_TENANT_SUSPENDED",
            "Tenant",
            tenant.Id,
            targetStatus == TenantStatus.Active
                ? "Super Admin restored tenant marketplace governance status."
                : "Super Admin suspended tenant marketplace governance status.",
            request.Reason,
            new { beforeStatus, targetStatus, wasActive, tenant.IsActive },
            cancellationToken);

        _logger.LogInformation(
            "Tenant marketplace governance status changed | TenantId: {TenantId} | Before: {BeforeStatus} | After: {AfterStatus} | SuperAdminId: {SuperAdminId}",
            tenant.Id,
            beforeStatus,
            targetStatus,
            CurrentSuperAdminId);

        return OkResponse(new SuperAdminMarketplaceGovernanceActionResultDto
        {
            EntityId = tenant.Id,
            EntityType = "Tenant",
            Action = targetStatus == TenantStatus.Active ? "restore" : "suspend",
            Changed = beforeStatus != targetStatus || wasActive != tenant.IsActive,
            VisibilityState = targetStatus == TenantStatus.Active ? "active" : "suspended",
            Message = targetStatus == TenantStatus.Active
                ? "Tenant marketplace authority was restored."
                : "Tenant marketplace authority was suspended.",
            AppliedAtUtc = now
        });
    }

    private IQueryable<SuperAdminMarketplaceGovernanceListingRowDto> BuildListingGovernanceQuery()
    {
        return
            from listing in _dbContext.CommerceHubListings.AsNoTracking().IgnoreQueryFilters()
            join tenant in _dbContext.Tenants.AsNoTracking().IgnoreQueryFilters()
                on listing.TenantId equals tenant.Id into tenantJoin
            from tenant in tenantJoin.DefaultIfEmpty()
            join profile in _dbContext.UserGovernanceProfiles.AsNoTracking()
                .Where(profile => profile.Role == UserGovernanceRole.Vendor)
                on listing.TenantId equals profile.SubjectId into profileJoin
            from profile in profileJoin.DefaultIfEmpty()
            join product in _dbContext.Products.AsNoTracking().IgnoreQueryFilters()
                on listing.ProductId equals product.Id into productJoin
            from product in productJoin.DefaultIfEmpty()
            join variant in _dbContext.ProductVariants.AsNoTracking().IgnoreQueryFilters()
                on listing.VariantId equals variant.Id into variantJoin
            from variant in variantJoin.DefaultIfEmpty()
            join category in _dbContext.CommerceHubCategories.AsNoTracking().IgnoreQueryFilters()
                on listing.CommerceHubCategoryId equals category.Id into categoryJoin
            from category in categoryJoin.DefaultIfEmpty()
            where !listing.IsDeleted
            let tenantMissing = tenant == null || tenant.DeletedAtUtc != null
            let tenantSuspended = tenantMissing || tenant.Status == TenantStatus.Suspended
            let productInvalid = product == null ||
                                 product.IsDeleted ||
                                 !product.IsActive ||
                                 !product.IsOnlineVisible ||
                                 product.PublishStatus != PublishStatus.Published
            let variantInvalid = variant == null ||
                                 variant.IsDeleted ||
                                 !variant.IsActive ||
                                 variant.TenantId != listing.TenantId ||
                                 variant.ProductId != listing.ProductId ||
                                 variant.SemanticLifecycleState != ProductVariantSemanticLifecycleState.Current ||
                                 variant.SemanticSupersededAtUtc != null ||
                                 variant.SemanticReplacementVariantId != null
            let categoryInvalid = category == null || !category.IsActive
            let vendorStatus = profile == null ? UserGovernanceStatus.Active : profile.Status
            let governanceHidden = vendorStatus == UserGovernanceStatus.Suspended || vendorStatus == UserGovernanceStatus.Banned || tenantSuspended
            let isOrphaned = tenantMissing || productInvalid || variantInvalid || categoryInvalid
            select new SuperAdminMarketplaceGovernanceListingRowDto
            {
                ListingId = listing.Id,
                TenantId = listing.TenantId,
                TenantName = tenant == null ? "(missing tenant)" : tenant.Name,
                TenantSlug = tenant == null ? null : tenant.Slug,
                TenantStatus = tenant == null ? "Missing" : tenant.Status.ToString(),
                VendorGovernanceStatus = vendorStatus.ToString(),
                ProductId = listing.ProductId,
                VariantId = listing.VariantId,
                ProductName = product == null ? "(missing product)" : product.Name,
                VariantName = variant == null ? null : variant.AttributeCombinationKey,
                Sku = variant == null ? null : variant.SKU,
                IsPublished = listing.IsPublished,
                IsSuppressed = listing.RestorePublicationOnActivate || governanceHidden,
                VisibilityState = listing.RestorePublicationOnActivate || governanceHidden
                    ? "suppressed"
                    : listing.IsPublished && !isOrphaned
                        ? "published"
                        : "unpublished",
                SuppressionReason = listing.RestorePublicationOnActivate
                    ? "Listing hidden by marketplace governance"
                    : governanceHidden
                        ? "Vendor or tenant governance hides this listing"
                        : null,
                SuppressionTimestampUtc = listing.RestorePublicationOnActivate ? listing.UpdatedAt : null,
                SuppressionOwner = listing.RestorePublicationOnActivate || governanceHidden ? "MarketplaceGovernance" : null,
                IsOrphaned = isOrphaned,
                OrphanReason = tenantMissing
                    ? "Tenant authority is missing or deleted"
                    : productInvalid
                        ? "Product authority is inactive, offline, unpublished, or deleted"
                        : variantInvalid
                            ? "Variant authority is inactive, detached, superseded, or deleted"
                            : categoryInvalid
                                ? "Category authority is inactive or deleted"
                                : null,
                CategoryId = listing.CommerceHubCategoryId,
                CategoryName = category == null ? null : category.Name,
                OverridePresent = listing.CommerceHubPrice.HasValue,
                PriceOverrideMutationVersion = listing.CommerceHubPriceOverrideMutationVersion,
                CreatedAtUtc = listing.CreatedAt,
                UpdatedAtUtc = listing.UpdatedAt
            };
    }

    private async Task<int> CountOrphanedListingsAsync(CancellationToken cancellationToken)
    {
        return await BuildListingGovernanceQuery()
            .CountAsync(row => row.IsOrphaned, cancellationToken);
    }

    private async Task<string?> GetRestoreAuthorityFailureAsync(
        CommerceHubListing listing,
        CancellationToken cancellationToken)
    {
        var tenantIsActive = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                tenant =>
                    tenant.Id == listing.TenantId &&
                    tenant.Status == TenantStatus.Active &&
                    tenant.DeletedAtUtc == null,
                cancellationToken);

        if (!tenantIsActive)
        {
            return "Tenant SQL authority is not active.";
        }

        var vendorHidden = await _dbContext.UserGovernanceProfiles
            .AsNoTracking()
            .AnyAsync(
                profile =>
                    profile.SubjectId == listing.TenantId &&
                    profile.Role == UserGovernanceRole.Vendor &&
                    (profile.Status == UserGovernanceStatus.Suspended ||
                     profile.Status == UserGovernanceStatus.Banned),
                cancellationToken);

        if (vendorHidden)
        {
            return "Vendor governance authority is suspended or banned.";
        }

        var productIsVisible = await _dbContext.Products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                product =>
                    product.Id == listing.ProductId &&
                    product.TenantId == listing.TenantId &&
                    !product.IsDeleted &&
                    product.IsActive &&
                    product.IsOnlineVisible &&
                    product.PublishStatus == PublishStatus.Published,
                cancellationToken);

        if (!productIsVisible)
        {
            return "Product SQL authority is not publicly visible.";
        }

        var variantIsVisible = await _dbContext.ProductVariants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                variant =>
                    variant.Id == listing.VariantId &&
                    variant.TenantId == listing.TenantId &&
                    variant.ProductId == listing.ProductId &&
                    !variant.IsDeleted &&
                    variant.IsActive &&
                    variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
                    variant.SemanticSupersededAtUtc == null &&
                    variant.SemanticReplacementVariantId == null,
                cancellationToken);

        if (!variantIsVisible)
        {
            return "Variant SQL authority is not current and public.";
        }

        var liveCategoryIds = await CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync(
            _dbContext,
            new[] { listing.CommerceHubCategoryId },
            cancellationToken);

        return liveCategoryIds.Contains(listing.CommerceHubCategoryId)
            ? null
            : "CommerceHub category lineage authority is not live.";
    }

    private async Task<decimal> ResolveEffectivePriceAsync(
        CommerceHubListing listing,
        CancellationToken cancellationToken)
    {
        var variant = await _dbContext.ProductVariants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == listing.VariantId, cancellationToken);

        return variant == null
            ? 0m
            : PublishToCommerceHubHandler.ResolveEffectivePrice(variant, listing.CommerceHubPrice);
    }

    private void EnqueueListingVerification(CommerceHubListing listing, string reason)
    {
        _autoHealingQueue.Enqueue(new CommerceHubAutoHealingTask
        {
            ListingIds = new List<Guid> { listing.Id },
            TenantId = listing.TenantId,
            Reason = reason,
            TriggeredAtUtc = DateTime.UtcNow,
            IsHighPriority = true
        });
    }

    private void EnqueueTenantListingVerification(Guid tenantId, string reason)
    {
        _autoHealingQueue.Enqueue(new CommerceHubAutoHealingTask
        {
            TenantId = tenantId,
            Reason = reason,
            TriggeredAtUtc = DateTime.UtcNow,
            IsHighPriority = true
        });
    }

    private Task LogMarketplaceGovernanceActionAsync(
        string action,
        string entityType,
        Guid entityId,
        string description,
        string reason,
        object details,
        CancellationToken cancellationToken)
    {
        return _auditService.LogActionAsync(
            action: action,
            entityType: entityType,
            entityId: entityId.ToString(),
            description: description,
            success: true,
            details: new
            {
                reason,
                details
            },
            actorId: CurrentSuperAdminId,
            actorEmail: CurrentSuperAdminEmail,
            cancellationToken: cancellationToken);
    }

    private static string? ValidateListingQuery(SuperAdminMarketplaceGovernanceListingQuery request)
    {
        if (request.Page <= 0)
        {
            return "Page must be greater than zero.";
        }

        if (request.PageSize <= 0 || request.PageSize > SuperAdminMarketplaceGovernanceListingQuery.MaxPageSize)
        {
            return $"PageSize must be between 1 and {SuperAdminMarketplaceGovernanceListingQuery.MaxPageSize}.";
        }

        return null;
    }
}
