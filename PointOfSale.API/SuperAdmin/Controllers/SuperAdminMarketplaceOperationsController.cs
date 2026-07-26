using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Models;
using PointOfSale.API.SuperAdmin.Services;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.Jobs;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.API.SuperAdmin.Controllers;

[Route("api/super-admin/marketplace-operations")]
public sealed class SuperAdminMarketplaceOperationsController : SuperAdminBaseController
{
    private readonly ApplicationDbContext _dbContext;
    private readonly MongoDbContext _mongoDbContext;
    private readonly ICommerceHubSearchIndexService _searchIndexService;
    private readonly ICommerceHubReindexService _reindexService;
    private readonly ICommerceHubAutoHealingQueue _autoHealingQueue;
    private readonly ICommerceHubAutoHealingMetrics _autoHealingMetrics;
    private readonly ISuperAdminAuditService _auditService;
    private readonly ILogger<SuperAdminMarketplaceOperationsController> _logger;

    public SuperAdminMarketplaceOperationsController(
        ApplicationDbContext dbContext,
        MongoDbContext mongoDbContext,
        ICommerceHubSearchIndexService searchIndexService,
        ICommerceHubReindexService reindexService,
        ICommerceHubAutoHealingQueue autoHealingQueue,
        ICommerceHubAutoHealingMetrics autoHealingMetrics,
        ISuperAdminAuditService auditService,
        ILogger<SuperAdminMarketplaceOperationsController> logger)
    {
        _dbContext = dbContext;
        _mongoDbContext = mongoDbContext;
        _searchIndexService = searchIndexService;
        _reindexService = reindexService;
        _autoHealingQueue = autoHealingQueue;
        _autoHealingMetrics = autoHealingMetrics;
        _auditService = auditService;
        _logger = logger;
    }

    [HttpGet("dashboard")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] string? q,
        CancellationToken cancellationToken)
    {
        var listings = _dbContext.CommerceHubListings
            .AsNoTracking()
            .IgnoreQueryFilters();

        var mongoAllFilter = Builders<CommerceHubProductDocument>.Filter.Empty;
        var mongoPublishedFilter = Builders<CommerceHubProductDocument>.Filter.Eq(document => document.IsPublished, true);
        var searchPendingFilter = Builders<CommerceHubProductDocument>.Filter.Eq(
            document => document.SearchSyncStatus,
            CommerceHubProductDocument.SearchSyncPending);
        var searchFailedFilter = Builders<CommerceHubProductDocument>.Filter.Or(
            Builders<CommerceHubProductDocument>.Filter.Eq(document => document.SearchSyncStatus, CommerceHubProductDocument.SearchSyncFailed),
            Builders<CommerceHubProductDocument>.Filter.Eq(document => document.SearchSyncStatus, CommerceHubProductDocument.SearchSyncTerminalFailure));

        var searchStatus = await _searchIndexService.GetStatusAsync(cancellationToken);
        var verification = await _searchIndexService.VerifyMigrationAsync(
            string.IsNullOrWhiteSpace(q) ? "samsung" : q.Trim(),
            cancellationToken);

        var dashboard = new SuperAdminMarketplaceOperationsDashboardDto
        {
            SqlListings = await listings.CountAsync(listing => !listing.IsDeleted, cancellationToken),
            SqlPublishedListings = await listings.CountAsync(
                listing => !listing.IsDeleted && listing.IsPublished && !listing.RestorePublicationOnActivate,
                cancellationToken),
            SqlSuppressedListings = await listings.CountAsync(
                listing => !listing.IsDeleted && listing.RestorePublicationOnActivate,
                cancellationToken),
            SqlDeletedListings = await listings.CountAsync(listing => listing.IsDeleted, cancellationToken),
            MongoProjectedListings = await _mongoDbContext.CommerceHubProducts.CountDocumentsAsync(mongoAllFilter, cancellationToken: cancellationToken),
            MongoPublishedListings = await _mongoDbContext.CommerceHubProducts.CountDocumentsAsync(mongoPublishedFilter, cancellationToken: cancellationToken),
            SearchSyncPendingListings = await _mongoDbContext.CommerceHubProducts.CountDocumentsAsync(searchPendingFilter, cancellationToken: cancellationToken),
            SearchSyncFailedListings = await _mongoDbContext.CommerceHubProducts.CountDocumentsAsync(searchFailedFilter, cancellationToken: cancellationToken),
            ElasticsearchActiveDocumentCount = await _searchIndexService.CountAsync(searchStatus.ActiveIndexName, cancellationToken),
            SearchIndexStatus = searchStatus,
            SearchIndexVerification = verification,
            AutoHealing = _autoHealingMetrics.GetSnapshot(),
            GeneratedAtUtc = DateTime.UtcNow
        };

        await LogOperationsActionAsync(
            "MARKETPLACE_OPERATIONS_DASHBOARD_VIEWED",
            "MarketplaceOperations",
            "dashboard",
            "Super Admin viewed marketplace operations diagnostics.",
            null,
            dashboard,
            cancellationToken);

        return OkResponse(dashboard);
    }

    [HttpPost("reindex/global")]
    [EnableRateLimiting(SuperAdminSecurityConstants.GovernanceWriteRateLimitPolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReindexGlobal(
        [FromBody] SuperAdminMarketplaceOperationsActionRequestDto request,
        CancellationToken cancellationToken)
    {
        var reasonError = ValidateReason(request.Reason);
        if (reasonError != null)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "REASON_REQUIRED", reasonError);
        }

        var startedAtUtc = DateTime.UtcNow;
        var result = await _reindexService.ReindexAsync(cancellationToken);
        var response = new SuperAdminMarketplaceReindexResultDto
        {
            Scope = "global",
            Result = result,
            CompletedAtUtc = DateTime.UtcNow
        };
        await PersistRecoveryOperationAsync(
            "Reindex",
            null,
            null,
            "global",
            result.Failed == 0 ? "Succeeded" : "CompletedWithFailures",
            startedAtUtc,
            request.Reason,
            response,
            cancellationToken);

        await LogOperationsActionAsync(
            "MARKETPLACE_GLOBAL_REINDEX_EXECUTED",
            "MarketplaceOperations",
            "global-reindex",
            "Super Admin executed global SQL-authoritative CommerceHub reindex.",
            request.Reason,
            response,
            cancellationToken);

        _logger.LogWarning(
            "Super Admin marketplace global reindex executed | SuperAdminId: {SuperAdminId} | Reason: {Reason} | Processed: {Processed} | Failed: {Failed}",
            CurrentSuperAdminId,
            request.Reason,
            result.Processed,
            result.Failed);

        return OkResponse(response);
    }

    [HttpPost("reindex/tenants/{tenantId:guid}")]
    [EnableRateLimiting(SuperAdminSecurityConstants.GovernanceWriteRateLimitPolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReindexTenant(
        Guid tenantId,
        [FromBody] SuperAdminMarketplaceOperationsActionRequestDto request,
        CancellationToken cancellationToken)
    {
        var reasonError = ValidateReason(request.Reason);
        if (reasonError != null)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "REASON_REQUIRED", reasonError);
        }

        if (!await TenantExistsAsync(tenantId, cancellationToken))
        {
            return ErrorResponse(StatusCodes.Status404NotFound, "TENANT_NOT_FOUND", "Tenant was not found.");
        }

        var startedAtUtc = DateTime.UtcNow;
        var result = await _reindexService.ReindexTenantAsync(tenantId, cancellationToken);
        var response = new SuperAdminMarketplaceReindexResultDto
        {
            Scope = "tenant",
            TenantId = tenantId,
            Result = result,
            CompletedAtUtc = DateTime.UtcNow
        };
        await PersistRecoveryOperationAsync(
            "Reindex",
            tenantId,
            null,
            "tenant",
            result.Failed == 0 ? "Succeeded" : "CompletedWithFailures",
            startedAtUtc,
            request.Reason,
            response,
            cancellationToken);

        await LogOperationsActionAsync(
            "MARKETPLACE_TENANT_REINDEX_EXECUTED",
            "Tenant",
            tenantId.ToString(),
            "Super Admin executed tenant-scoped SQL-authoritative CommerceHub reindex.",
            request.Reason,
            response,
            cancellationToken);

        return OkResponse(response);
    }

    [HttpPost("reindex/catch-up")]
    [EnableRateLimiting(SuperAdminSecurityConstants.GovernanceWriteRateLimitPolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> CatchUpReindex(
        [FromBody] SuperAdminMarketplaceOperationsActionRequestDto request,
        CancellationToken cancellationToken)
    {
        var reasonError = ValidateReason(request.Reason);
        if (reasonError != null)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "REASON_REQUIRED", reasonError);
        }

        var startedAtUtc = DateTime.UtcNow;
        var result = await _reindexService.CatchUpReindexAsync(cancellationToken);
        var response = new SuperAdminMarketplaceCatchUpResultDto
        {
            Result = result,
            CompletedAtUtc = DateTime.UtcNow
        };
        await PersistRecoveryOperationAsync(
            "CatchUpReindex",
            null,
            null,
            "global",
            "Succeeded",
            startedAtUtc,
            request.Reason,
            response,
            cancellationToken);

        await LogOperationsActionAsync(
            "MARKETPLACE_SEARCH_CATCH_UP_REINDEX_EXECUTED",
            "MarketplaceOperations",
            "catch-up-reindex",
            "Super Admin executed CommerceHub search catch-up reindex.",
            request.Reason,
            response,
            cancellationToken);

        return OkResponse(response);
    }

    [HttpPost("listings/{listingId:guid}/verify")]
    [EnableRateLimiting(SuperAdminSecurityConstants.GovernanceWriteRateLimitPolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> VerifyListing(
        Guid listingId,
        [FromBody] SuperAdminMarketplaceVerificationRequestDto request,
        CancellationToken cancellationToken)
    {
        var reasonError = ValidateReason(request.Reason);
        if (reasonError != null)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "REASON_REQUIRED", reasonError);
        }

        var listing = await _dbContext.CommerceHubListings
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == listingId, cancellationToken);

        if (listing == null)
        {
            return ErrorResponse(StatusCodes.Status404NotFound, "LISTING_NOT_FOUND", "CommerceHub listing was not found.");
        }

        var enqueuedAtUtc = DateTime.UtcNow;
        var enqueued = _autoHealingQueue.Enqueue(new CommerceHubAutoHealingTask
        {
            ListingIds = new[] { listing.Id },
            TenantId = listing.TenantId,
            Reason = $"SUPER_ADMIN_MANUAL_VERIFY:{request.Reason.Trim()}",
            TriggeredAtUtc = enqueuedAtUtc,
            IsHighPriority = request.HighPriority
        });

        var response = new SuperAdminMarketplaceVerificationResultDto
        {
            Scope = "listing",
            TenantId = listing.TenantId,
            ListingId = listing.Id,
            Enqueued = enqueued,
            Reason = request.Reason.Trim(),
            EnqueuedAtUtc = enqueuedAtUtc
        };
        await PersistRecoveryOperationAsync(
            "VerificationEnqueue",
            listing.TenantId,
            listing.Id,
            "listing",
            enqueued > 0 ? "Enqueued" : "NoOp",
            enqueuedAtUtc,
            request.Reason,
            response,
            cancellationToken);

        await LogOperationsActionAsync(
            "MARKETPLACE_LISTING_VERIFICATION_ENQUEUED",
            "CommerceHubListing",
            listing.Id.ToString(),
            "Super Admin enqueued SQL-authoritative listing verification.",
            request.Reason,
            response,
            cancellationToken);

        return OkResponse(response);
    }

    [HttpPost("tenants/{tenantId:guid}/verify")]
    [EnableRateLimiting(SuperAdminSecurityConstants.GovernanceWriteRateLimitPolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> VerifyTenant(
        Guid tenantId,
        [FromBody] SuperAdminMarketplaceVerificationRequestDto request,
        CancellationToken cancellationToken)
    {
        var reasonError = ValidateReason(request.Reason);
        if (reasonError != null)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "REASON_REQUIRED", reasonError);
        }

        if (!await TenantExistsAsync(tenantId, cancellationToken))
        {
            return ErrorResponse(StatusCodes.Status404NotFound, "TENANT_NOT_FOUND", "Tenant was not found.");
        }

        var enqueuedAtUtc = DateTime.UtcNow;
        var enqueued = _autoHealingQueue.Enqueue(new CommerceHubAutoHealingTask
        {
            ListingIds = await _dbContext.CommerceHubListings
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(listing => listing.TenantId == tenantId && !listing.IsDeleted)
                .OrderByDescending(listing => listing.UpdatedAt)
                .Select(listing => listing.Id)
                .Take(500)
                .ToListAsync(cancellationToken),
            TenantId = tenantId,
            Reason = $"SUPER_ADMIN_TENANT_VERIFY:{request.Reason.Trim()}",
            TriggeredAtUtc = enqueuedAtUtc,
            IsHighPriority = request.HighPriority
        });

        var response = new SuperAdminMarketplaceVerificationResultDto
        {
            Scope = "tenant",
            TenantId = tenantId,
            Enqueued = enqueued,
            Reason = request.Reason.Trim(),
            EnqueuedAtUtc = enqueuedAtUtc
        };
        await PersistRecoveryOperationAsync(
            "VerificationEnqueue",
            tenantId,
            null,
            "tenant",
            enqueued > 0 ? "Enqueued" : "NoOp",
            enqueuedAtUtc,
            request.Reason,
            response,
            cancellationToken);

        await LogOperationsActionAsync(
            "MARKETPLACE_TENANT_VERIFICATION_ENQUEUED",
            "Tenant",
            tenantId.ToString(),
            "Super Admin enqueued tenant-scoped CommerceHub verification.",
            request.Reason,
            response,
            cancellationToken);

        return OkResponse(response);
    }

    private Task<bool> TenantExistsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        return _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(
                tenant => tenant.Id == tenantId &&
                          tenant.Status != TenantStatus.Deleted &&
                          tenant.DeletedAtUtc == null,
                cancellationToken);
    }

    private Task LogOperationsActionAsync(
        string action,
        string entityType,
        string entityId,
        string description,
        string? reason,
        object details,
        CancellationToken cancellationToken)
    {
        return _auditService.LogActionAsync(
            action: action,
            entityType: entityType,
            entityId: entityId,
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

    private async Task PersistRecoveryOperationAsync(
        string operationType,
        Guid? tenantId,
        Guid? listingId,
        string scope,
        string outcome,
        DateTime startedAtUtc,
        string? reason,
        object details,
        CancellationToken cancellationToken)
    {
        CommerceHubForensicEvidenceWriter.AppendRecoveryOperation(
            _dbContext,
            operationType,
            CurrentSuperAdminEmail ?? CurrentSuperAdminId?.ToString() ?? "super-admin",
            CurrentSuperAdminId,
            tenantId,
            listingId,
            scope,
            outcome,
            startedAtUtc,
            HttpContext.TraceIdentifier,
            HttpContext.TraceIdentifier,
            reason,
            details);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string? ValidateReason(string? reason)
    {
        return string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 3
            ? "A reason of at least 3 characters is required for marketplace operations."
            : null;
    }
}
