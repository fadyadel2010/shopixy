using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.RegularExpressions;
using PointOfSale.API.DTOs.Checkout;
using PointOfSale.API.Security;
using PointOfSale.API.Services;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Admin dashboard controller for viewing abandoned checkouts (lost orders).
/// 
/// Phase: Abandoned Checkout / Lost Orders
/// 
/// READS FROM MongoDB ONLY (CQRS — never from PostgreSQL).
/// 
/// Endpoints:
/// - GET  /api/admin/abandoned-checkouts          → Paged list
/// - GET  /api/admin/abandoned-checkouts/{id}      → Detail view
/// - GET  /api/admin/abandoned-checkouts/stats     → Summary statistics
/// 
/// Tenant Scope:
/// - Trusted tenant context resolved by TenantResolutionMiddleware
/// 
/// Authorization: Bearer JWT (POS Admin)
/// </summary>
[ApiController]
[Route("api/admin/abandoned-checkouts")]
[Authorize(Roles = "Owner,Admin,Manager")]
public class AdminAbandonedCheckoutsController : ControllerBase
{
    private readonly MongoDbContext _mongoContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICheckoutSessionTokenService _checkoutSessionTokenService;
    private readonly StorefrontUrlResolver _urlResolver;
    private readonly ILogger<AdminAbandonedCheckoutsController> _logger;

    public AdminAbandonedCheckoutsController(
        MongoDbContext mongoContext,
        ITenantContext tenantContext,
        ICheckoutSessionTokenService checkoutSessionTokenService,
        StorefrontUrlResolver urlResolver,
        ILogger<AdminAbandonedCheckoutsController> logger)
    {
        _mongoContext = mongoContext;
        _tenantContext = tenantContext;
        _checkoutSessionTokenService = checkoutSessionTokenService;
        _urlResolver = urlResolver;
        _logger = logger;
    }

    /// <summary>
    /// Get paged list of abandoned checkouts for the admin dashboard.
    /// </summary>
    /// <param name="status">Filter by status: Active, Abandoned, ConvertedToOrder, or null for all</param>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="pageSize">Items per page (max 100)</param>
    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var collection = _mongoContext.AbandonedCheckouts;

        var filterBuilder = Builders<AbandonedCheckoutDocument>.Filter;
        var filter = filterBuilder.Eq(x => x.TenantId, tenantId);

        if (!string.IsNullOrEmpty(status))
        {
            filter = filter & filterBuilder.Eq(x => x.Status, status);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var escapedSearch = Regex.Escape(search.Trim());
            var regex = new BsonRegularExpression(escapedSearch, "i");
            var searchFilter = filterBuilder.Or(
                filterBuilder.Regex(x => x.CustomerName, regex),
                filterBuilder.Regex(x => x.Phone, regex),
                filterBuilder.Regex(x => x.Email, regex));
            filter = filter & searchFilter;
        }

        var totalCount = await collection.CountDocumentsAsync(filter, cancellationToken: ct);

        var documents = await collection.Find(filter)
            .SortByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);

        // Resolve storefront base URL once for all items (same tenant)
        string? storefrontBaseUrl = null;
        try
        {
            storefrontBaseUrl = await _urlResolver.ResolveByTenantIdAsync(tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AbandonedCheckouts] Could not resolve storefront URL for tenant {TenantId}", tenantId);
        }

        var items = documents.Select(d => new AbandonedCheckoutListItem
        {
            MongoId = d.Id,
            CheckoutSessionId = d.CheckoutSessionId,
            CartId = d.CartId,
            CustomerName = d.CustomerName,
            Phone = d.Phone,
            Email = d.Email,
            City = d.City,
            CartTotal = d.CartTotal,
            Currency = d.Currency,
            ItemCount = d.Items?.Count ?? 0,
            Status = d.Status,
            DeviceType = d.DeviceType,
            Source = d.Source,
            CreatedAtUtc = d.CreatedAtUtc,
            FirstActivityAtUtc = d.FirstActivityAtUtc,
            LastActivityAtUtc = d.LastActivityAtUtc,
            AbandonedAtUtc = d.AbandonedAtUtc,
            ConvertedAtUtc = d.ConvertedAtUtc,
            ConvertedOrderNumber = d.ConvertedOrderNumber,
            TimeSpentSeconds = d.FirstActivityAtUtc.HasValue
                ? (d.LastActivityAtUtc - d.FirstActivityAtUtc.Value).TotalSeconds
                : null,
            RecoveryUrl = !string.IsNullOrEmpty(d.RecoveryToken) && storefrontBaseUrl != null
                ? $"{storefrontBaseUrl}/recover-checkout/{CreateSignedRecoveryToken(tenantId, d.CheckoutSessionId, d.RecoveryToken)}"
                : null,
        }).ToList();

        return Ok(new AbandonedCheckoutPagedResponse
        {
            Items = items,
            TotalCount = (int)totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
            HasMore = (page * pageSize) < totalCount,
        });
    }

    /// <summary>
    /// Get detailed view of a single abandoned checkout.
    /// </summary>
    [HttpGet("{sessionId:guid}")]
    public async Task<IActionResult> GetDetail(Guid sessionId, CancellationToken ct)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        var collection = _mongoContext.AbandonedCheckouts;

        var filter = Builders<AbandonedCheckoutDocument>.Filter.And(
            Builders<AbandonedCheckoutDocument>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<AbandonedCheckoutDocument>.Filter.Eq(x => x.CheckoutSessionId, sessionId));

        var doc = await collection.Find(filter).FirstOrDefaultAsync(ct);

        if (doc == null)
            return NotFound(new { error = "Abandoned checkout not found" });

        // Build recovery URL from backend
        string? recoveryUrl = null;
        if (!string.IsNullOrEmpty(doc.RecoveryToken))
        {
            try
            {
                recoveryUrl = await _urlResolver.BuildRecoveryUrlAsync(
                    tenantId,
                    CreateSignedRecoveryToken(tenantId, doc.CheckoutSessionId, doc.RecoveryToken));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AbandonedCheckouts] Could not build recovery URL for tenant {TenantId}", tenantId);
            }
        }

        var detail = new AbandonedCheckoutDetail
        {
            CheckoutSessionId = doc.CheckoutSessionId,
            CartId = doc.CartId,
            CustomerName = doc.CustomerName,
            Phone = doc.Phone,
            Email = doc.Email,
            City = doc.City,
            Area = doc.Area,
            Address = doc.Address,
            Notes = doc.Notes,
            Items = doc.Items?.Select(i => new AbandonedCheckoutItemDto
            {
                VariantId = i.VariantId,
                ProductName = i.ProductName,
                VariantName = i.VariantName,
                Quantity = i.Quantity,
                Price = i.Price,
                ImageUrl = i.ImageUrl,
            }).ToList() ?? new(),
            CartTotal = doc.CartTotal,
            Currency = doc.Currency,
            ShippingMethodId = doc.ShippingMethodId,
            PaymentMethodCode = doc.PaymentMethodCode,
            WalletType = doc.WalletType,
            Status = doc.Status,
            RecoveryToken = !string.IsNullOrEmpty(doc.RecoveryToken)
                ? CreateSignedRecoveryToken(tenantId, doc.CheckoutSessionId, doc.RecoveryToken)
                : null,
            DeviceType = doc.DeviceType,
            Source = doc.Source,
            CreatedAtUtc = doc.CreatedAtUtc,
            FirstActivityAtUtc = doc.FirstActivityAtUtc,
            LastActivityAtUtc = doc.LastActivityAtUtc,
            AbandonedAtUtc = doc.AbandonedAtUtc,
            ConvertedAtUtc = doc.ConvertedAtUtc,
            ConvertedOrderId = doc.ConvertedOrderId,
            ConvertedOrderNumber = doc.ConvertedOrderNumber,
            TimeSpentSeconds = doc.FirstActivityAtUtc.HasValue
                ? (doc.LastActivityAtUtc - doc.FirstActivityAtUtc.Value).TotalSeconds
                : null,
            RecoveryUrl = recoveryUrl,
        };

        return Ok(detail);
    }

    /// <summary>
    /// Get summary statistics for the abandoned checkouts dashboard.
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        var collection = _mongoContext.AbandonedCheckouts;
        var tenantFilter = Builders<AbandonedCheckoutDocument>.Filter.Eq(x => x.TenantId, tenantId);

        var totalCount = await collection.CountDocumentsAsync(tenantFilter, cancellationToken: ct);

        var abandonedFilter = tenantFilter &
            Builders<AbandonedCheckoutDocument>.Filter.Eq(x => x.Status, "Abandoned");
        var abandonedCount = await collection.CountDocumentsAsync(abandonedFilter, cancellationToken: ct);

        var convertedFilter = tenantFilter &
            Builders<AbandonedCheckoutDocument>.Filter.Eq(x => x.Status, "ConvertedToOrder");
        var convertedCount = await collection.CountDocumentsAsync(convertedFilter, cancellationToken: ct);

        var activeFilter = tenantFilter &
            Builders<AbandonedCheckoutDocument>.Filter.Eq(x => x.Status, "Active");
        var activeCount = await collection.CountDocumentsAsync(activeFilter, cancellationToken: ct);

        var revenueAggregate = await collection.Aggregate()
            .Match(abandonedFilter)
            .Group(_ => 1, group => new
            {
                TotalAbandonedRevenue = group.Sum(item => item.CartTotal ?? 0m)
            })
            .FirstOrDefaultAsync(ct);
        var totalAbandonedRevenue = revenueAggregate?.TotalAbandonedRevenue ?? 0m;

        var conversionRate = totalCount > 0
            ? Math.Round((double)convertedCount / totalCount * 100, 1)
            : 0;

        return Ok(new
        {
            totalSessions = totalCount,
            activeSessions = activeCount,
            abandonedSessions = abandonedCount,
            convertedSessions = convertedCount,
            conversionRate = conversionRate,
            totalAbandonedRevenue = totalAbandonedRevenue,
        });
    }

    private string CreateSignedRecoveryToken(Guid tenantId, Guid checkoutSessionId, string recoveryNonce)
    {
        return _checkoutSessionTokenService.CreateRecoveryToken(new CheckoutSession
        {
            Id = checkoutSessionId,
            TenantId = tenantId,
            RecoveryToken = recoveryNonce
        });
    }
}
