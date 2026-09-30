using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.Json;
using System.Text.RegularExpressions;
using PointOfSale.API.DTOs.Checkout;
using PointOfSale.API.Security;
using PointOfSale.API.Services;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Admin dashboard controller for viewing abandoned checkouts (lost orders).
/// 
/// Phase: Abandoned Checkout / Lost Orders
/// 
/// Reads the MongoDB projection when it agrees with PostgreSQL lifecycle counts.
/// PostgreSQL remains checkout-session authority and is used as a read-only fallback
/// when projection loss would otherwise report successful but incorrect empty data.
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
    private readonly ApplicationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICheckoutSessionTokenService _checkoutSessionTokenService;
    private readonly StorefrontUrlResolver _urlResolver;
    private readonly ILogger<AdminAbandonedCheckoutsController> _logger;

    public AdminAbandonedCheckoutsController(
        MongoDbContext mongoContext,
        ApplicationDbContext dbContext,
        ITenantContext tenantContext,
        ICheckoutSessionTokenService checkoutSessionTokenService,
        StorefrontUrlResolver urlResolver,
        ILogger<AdminAbandonedCheckoutsController> logger)
    {
        _mongoContext = mongoContext;
        _dbContext = dbContext;
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

        CheckoutSessionStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<CheckoutSessionStatus>(status, ignoreCase: true, out var value) ||
                !Enum.IsDefined(value))
            {
                return BadRequest(new
                {
                    error = "INVALID_CHECKOUT_SESSION_STATUS",
                    message = "Status must be Active, Abandoned, or ConvertedToOrder."
                });
            }

            parsedStatus = value;
            status = value.ToString();
        }

        if (await RequiresSqlFallbackAsync(tenantId, ct))
        {
            return await GetSqlListAsync(tenantId, parsedStatus, search, page, pageSize, ct);
        }

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
            .ThenByDescending(x => x.CheckoutSessionId)
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
        {
            var session = await _dbContext.CheckoutSessions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.TenantId == tenantId &&
                            item.Id == sessionId &&
                            item.InvalidatedAtUtc == null,
                    ct);

            if (session == null)
                return NotFound(new { error = "Abandoned checkout not found" });

            _logger.LogWarning(
                "[AbandonedCheckouts] Serving authoritative SQL detail because the MongoDB projection is missing | TenantId: {TenantId}",
                tenantId);

            return Ok(await MapSqlDetailAsync(session, ct));
        }

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

        var sessions = _dbContext.CheckoutSessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(session => session.TenantId == tenantId && session.InvalidatedAtUtc == null);

        var counts = await sessions
            .GroupBy(session => session.Status)
            .Select(group => new { Status = group.Key, Count = group.LongCount() })
            .ToListAsync(ct);

        var totalCount = counts.Sum(item => item.Count);
        var activeCount = counts.FirstOrDefault(item => item.Status == CheckoutSessionStatus.Active)?.Count ?? 0;
        var abandonedCount = counts.FirstOrDefault(item => item.Status == CheckoutSessionStatus.Abandoned)?.Count ?? 0;
        var convertedCount = counts.FirstOrDefault(item => item.Status == CheckoutSessionStatus.ConvertedToOrder)?.Count ?? 0;

        var abandonedValues = await sessions
            .Where(session => session.Status == CheckoutSessionStatus.Abandoned)
            .Select(session => new { session.CartTotal, session.Currency })
            .ToListAsync(ct);
        var abandonedCurrencies = abandonedValues
            .Where(item => item.CartTotal.HasValue && !string.IsNullOrWhiteSpace(item.Currency))
            .Select(item => item.Currency!.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var abandonedValueAvailable = abandonedValues.Count == 0 ||
            (abandonedValues.All(item => item.CartTotal.HasValue) && abandonedCurrencies.Count <= 1);
        decimal? totalAbandonedRevenue = abandonedValueAvailable
            ? abandonedValues.Sum(item => item.CartTotal ?? 0m)
            : null;

        var conversionRate = totalCount > 0
            ? Math.Round((double)convertedCount / totalCount * 100, 1)
            : 0;
        var abandonmentRate = totalCount > 0
            ? Math.Round((double)abandonedCount / totalCount * 100, 1)
            : 0;

        return Ok(new
        {
            totalSessions = totalCount,
            activeSessions = activeCount,
            abandonedSessions = abandonedCount,
            convertedSessions = convertedCount,
            conversionRate = conversionRate,
            abandonmentRate = abandonmentRate,
            totalAbandonedRevenue = totalAbandonedRevenue,
            abandonedValueAvailable,
            abandonedValueCurrency = abandonedCurrencies.SingleOrDefault(),
        });
    }

    private async Task<bool> RequiresSqlFallbackAsync(Guid tenantId, CancellationToken ct)
    {
        var sqlCounts = await _dbContext.CheckoutSessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(session => session.TenantId == tenantId && session.InvalidatedAtUtc == null)
            .GroupBy(session => session.Status)
            .Select(group => new { Status = group.Key, Count = group.LongCount() })
            .ToListAsync(ct);

        var mongoCounts = await _mongoContext.AbandonedCheckouts.Aggregate()
            .Match(item => item.TenantId == tenantId)
            .Group(item => item.Status, group => new { Status = group.Key, Count = group.LongCount() })
            .ToListAsync(ct);

        var sqlByStatus = sqlCounts.ToDictionary(item => item.Status.ToString(), item => item.Count, StringComparer.Ordinal);
        var mongoByStatus = mongoCounts.ToDictionary(item => item.Status, item => item.Count, StringComparer.Ordinal);
        var requiresFallback = !sqlByStatus.OrderBy(item => item.Key)
            .SequenceEqual(mongoByStatus.OrderBy(item => item.Key));

        if (requiresFallback)
        {
            _logger.LogWarning(
                "[AbandonedCheckouts] MongoDB lifecycle projection drift detected; using authoritative SQL visibility | TenantId: {TenantId} | SqlCount: {SqlCount} | MongoCount: {MongoCount}",
                tenantId,
                sqlByStatus.Values.Sum(),
                mongoByStatus.Values.Sum());
        }

        return requiresFallback;
    }

    private async Task<IActionResult> GetSqlListAsync(
        Guid tenantId,
        CheckoutSessionStatus? status,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var query = _dbContext.CheckoutSessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(session => session.TenantId == tenantId && session.InvalidatedAtUtc == null);

        if (status.HasValue)
            query = query.Where(session => session.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLikePattern(search.Trim())}%";
            query = query.Where(session =>
                (session.CustomerName != null && EF.Functions.ILike(session.CustomerName, pattern, "\\")) ||
                (session.Phone != null && EF.Functions.ILike(session.Phone, pattern, "\\")) ||
                (session.Email != null && EF.Functions.ILike(session.Email, pattern, "\\")));
        }

        var totalCount = await query.LongCountAsync(ct);
        var sessions = await query
            .OrderByDescending(session => session.CreatedAtUtc)
            .ThenByDescending(session => session.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var storefrontBaseUrl = await TryResolveStorefrontBaseUrlAsync(tenantId);
        var items = sessions.Select(session => MapSqlListItem(session, storefrontBaseUrl)).ToList();

        return Ok(new AbandonedCheckoutPagedResponse
        {
            Items = items,
            TotalCount = checked((int)totalCount),
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
            HasMore = page * (long)pageSize < totalCount,
        });
    }

    private AbandonedCheckoutListItem MapSqlListItem(CheckoutSession session, string? storefrontBaseUrl) => new()
    {
        CheckoutSessionId = session.Id,
        CartId = session.CartId,
        CustomerName = session.CustomerName,
        Phone = session.Phone,
        Email = session.Email,
        City = session.City,
        CartTotal = session.CartTotal,
        Currency = session.Currency,
        ItemCount = DeserializeItems(session.ItemsSnapshotJson).Count,
        Status = session.Status.ToString(),
        DeviceType = session.DeviceType,
        Source = session.Source,
        CreatedAtUtc = session.CreatedAtUtc,
        FirstActivityAtUtc = session.FirstActivityAtUtc,
        LastActivityAtUtc = session.LastActivityAtUtc,
        AbandonedAtUtc = session.AbandonedAtUtc,
        ConvertedAtUtc = session.ConvertedAtUtc,
        ConvertedOrderNumber = session.ConvertedOrderNumber,
        TimeSpentSeconds = session.FirstActivityAtUtc.HasValue
            ? (session.LastActivityAtUtc - session.FirstActivityAtUtc.Value).TotalSeconds
            : null,
        RecoveryUrl = !string.IsNullOrWhiteSpace(session.RecoveryToken) && storefrontBaseUrl != null
            ? $"{storefrontBaseUrl}/recover-checkout/{CreateSignedRecoveryToken(session.TenantId, session.Id, session.RecoveryToken)}"
            : null,
    };

    private async Task<AbandonedCheckoutDetail> MapSqlDetailAsync(CheckoutSession session, CancellationToken ct)
    {
        var recoveryUrl = !string.IsNullOrWhiteSpace(session.RecoveryToken)
            ? await TryBuildRecoveryUrlAsync(session.TenantId, session.Id, session.RecoveryToken)
            : null;

        return new AbandonedCheckoutDetail
        {
            CheckoutSessionId = session.Id,
            CartId = session.CartId,
            CustomerName = session.CustomerName,
            Phone = session.Phone,
            Email = session.Email,
            City = session.City,
            Area = session.Area,
            Address = session.Address,
            Notes = session.Notes,
            Items = DeserializeItems(session.ItemsSnapshotJson),
            CartTotal = session.CartTotal,
            Currency = session.Currency,
            ShippingMethodId = session.ShippingMethodId,
            PaymentMethodCode = session.PaymentMethodCode,
            WalletType = session.WalletType,
            Status = session.Status.ToString(),
            DeviceType = session.DeviceType,
            Source = session.Source,
            CreatedAtUtc = session.CreatedAtUtc,
            FirstActivityAtUtc = session.FirstActivityAtUtc,
            LastActivityAtUtc = session.LastActivityAtUtc,
            AbandonedAtUtc = session.AbandonedAtUtc,
            ConvertedAtUtc = session.ConvertedAtUtc,
            ConvertedOrderId = session.ConvertedOrderId,
            ConvertedOrderNumber = session.ConvertedOrderNumber,
            TimeSpentSeconds = session.FirstActivityAtUtc.HasValue
                ? (session.LastActivityAtUtc - session.FirstActivityAtUtc.Value).TotalSeconds
                : null,
            RecoveryUrl = recoveryUrl,
        };
    }

    private async Task<string?> TryResolveStorefrontBaseUrlAsync(Guid tenantId)
    {
        try
        {
            return await _urlResolver.ResolveByTenantIdAsync(tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AbandonedCheckouts] Could not resolve storefront URL for tenant {TenantId}", tenantId);
            return null;
        }
    }

    private async Task<string?> TryBuildRecoveryUrlAsync(Guid tenantId, Guid sessionId, string recoveryNonce)
    {
        try
        {
            return await _urlResolver.BuildRecoveryUrlAsync(
                tenantId,
                CreateSignedRecoveryToken(tenantId, sessionId, recoveryNonce));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AbandonedCheckouts] Could not build recovery URL for tenant {TenantId}", tenantId);
            return null;
        }
    }

    private static List<AbandonedCheckoutItemDto> DeserializeItems(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new();

        try
        {
            return JsonSerializer.Deserialize<List<AbandonedCheckoutItemDto>>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

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
