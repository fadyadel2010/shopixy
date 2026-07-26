using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Controllers;

/// <summary>
/// POS Admin Controller for managing Online Customers.
/// 
/// ARCHITECTURAL RULES:
/// - POS is the ONLY admin for Storefront customers
/// - Storefront customers cannot be created via POS (only via Storefront registration)
/// - Only enable/disable and view operations allowed here
/// - TenantId from JWT - strict tenant isolation
/// 
/// SOFT DELETE POLICY:
/// - OnlineCustomers are NEVER deleted - only deactivated via IsActive = false
/// - There is NO delete endpoint intentionally
/// - Legal/audit requirements mandate data retention
/// 
/// SECURITY NOTES:
/// - All state changes emit events for MongoDB projection and cache invalidation
/// - Deactivation invalidates cache to prevent stale data usage
/// - TODO (Phase 10.3.2): Deactivation must also revoke/invalidate JWTs
/// 
/// Phase: 10.3.1 - Online Customer Identity (Core)
/// Phase: 10.3.1 Hardening - Security & Correctness Fixes
/// Phase: 10.3.6 - Wishlist (added wishlist visibility)
/// </summary>
[ApiController]
[Route("api/admin/online-customers")]
[Authorize(Roles = "Admin,Owner,Manager")]
public class OnlineCustomersAdminController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<OnlineCustomersAdminController> _logger;
    private readonly PointOfSale.API.Services.Admin.IOnlineCustomerSessionAdminService _sessionService;
    private readonly IStorefrontWishlistService _wishlistService;

    public OnlineCustomersAdminController(
        ApplicationDbContext dbContext,
        ITenantContext tenantContext,
        IEventPublisher eventPublisher,
        ILogger<OnlineCustomersAdminController> logger,
        PointOfSale.API.Services.Admin.IOnlineCustomerSessionAdminService sessionService,
        IStorefrontWishlistService wishlistService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _eventPublisher = eventPublisher;
        _logger = logger;
        _sessionService = sessionService;
        _wishlistService = wishlistService;
    }

    private Guid TenantId => _tenantContext.RequiredTenantId;

    /// <summary>
    /// Gets a paginated list of online customers for the current tenant.
    /// </summary>
    /// <param name="page">Page number (1-indexed)</param>
    /// <param name="pageSize">Items per page (max 100)</param>
    /// <param name="search">Optional search term (email or display name)</param>
    /// <param name="isActive">Optional filter by active status</param>
    /// <param name="provider">Optional filter by provider (Local, Google, Facebook)</param>
    /// <returns>Paginated list of online customers</returns>
    [HttpGet]
    [ProducesResponseType(typeof(OnlineCustomerListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetOnlineCustomers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] bool? emailVerified = null, // Added parameter
        [FromQuery] string? provider = null,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var tenantId = TenantId;

        // Validate pagination
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        _logger.LogInformation(
            "[OnlineCustomer] {Action} request | TenantId: {TenantId} | Page: {Page} | PageSize: {PageSize} | Search: {Search} | IsActive: {IsActive} | EmailVerified: {EmailVerified} | Provider: {Provider} | TraceId: {TraceId}",
            "LIST",
            tenantId,
            page,
            pageSize,
            search,
            isActive,
            emailVerified,
            provider,
            traceId);

        try
        {
            var query = _dbContext.OnlineCustomers
                .AsNoTracking()
                .Where(c => c.TenantId == tenantId);

            // Apply filters
            if (!string.IsNullOrWhiteSpace(search))
            {
                // CRITICAL: Use ToLowerInvariant for culture-safe search
                var searchNormalized = EmailNormalizer.Normalize(search);
                query = query.Where(c =>
                    c.Email.ToLower().Contains(searchNormalized) ||
                    (c.DisplayName != null && c.DisplayName.ToLower().Contains(searchNormalized)));
            }

            if (isActive.HasValue)
            {
                query = query.Where(c => c.IsActive == isActive.Value);
            }

            if (emailVerified.HasValue) // Added filter
            {
                query = query.Where(c => c.EmailVerified == emailVerified.Value);
            }

            if (!string.IsNullOrWhiteSpace(provider) && Enum.TryParse<AuthProvider>(provider, true, out var providerEnum))
            {
                query = query.Where(c => c.Provider == providerEnum);
            }

            // Get total count
            var totalCount = await query.CountAsync(cancellationToken);

            // Get page
            // Get page
            var customers = await query
                .OrderByDescending(c => c.CreatedAtUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new 
                {
                    c.Id,
                    c.Email,
                    c.DisplayName,
                    c.Phone,
                    Provider = c.Provider,
                    c.IsActive,
                    c.EmailVerified,
                    c.CreatedAtUtc,
                    c.LastLoginAtUtc
                })
                .ToListAsync(cancellationToken);

            // Fetch active session counts for the current page
            // This avoids N+1 by fetching counts in a separate query for the displayed IDs
            var customerIds = customers.Select(c => c.Id).ToList();
            var sessionCounts = await _dbContext.OnlineCustomerRefreshTokens
                .Where(t =>
                    t.TenantId == tenantId &&
                    customerIds.Contains(t.OnlineCustomerId) &&
                    !t.IsRevoked &&
                    t.ExpiresAtUtc > DateTime.UtcNow)
                .GroupBy(t => t.OnlineCustomerId)
                .Select(g => new { CustomerId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.CustomerId, x => x.Count, cancellationToken);

            var items = customers.Select(c => new OnlineCustomerListItem
            {
                Id = c.Id,
                Email = c.Email,
                DisplayName = c.DisplayName,
                Phone = c.Phone,
                Provider = c.Provider.ToString().ToLowerInvariant(),
                IsActive = c.IsActive,
                IsEmailVerified = c.EmailVerified,
                CreatedAtUtc = c.CreatedAtUtc,
                LastLoginAtUtc = c.LastLoginAtUtc,
                ActiveSessions = sessionCounts.TryGetValue(c.Id, out var count) ? count : 0
            }).ToList();

            var response = new OnlineCustomerListResponse
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
            };

            _logger.LogInformation(
                "[OnlineCustomer] {Action} success | TenantId: {TenantId} | TotalCount: {TotalCount} | ItemsReturned: {ItemsReturned} | TraceId: {TraceId}",
                "LIST",
                tenantId,
                totalCount,
                items.Count,
                traceId);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[OnlineCustomer] {Action} failed | TenantId: {TenantId} | TraceId: {TraceId}",
                "LIST",
                tenantId,
                traceId);
            return StatusCode(500, new { error = "INTERNAL_ERROR", message = "Failed to retrieve online customers" });
        }
    }

    /// <summary>
    /// Gets a single online customer by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OnlineCustomerDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetOnlineCustomer(Guid id, CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var tenantId = TenantId;

        _logger.LogInformation(
            "[OnlineCustomer] {Action} request | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
            "GET",
            tenantId,
            id,
            traceId);

        var customer = await _dbContext.OnlineCustomers
            .AsNoTracking()
            .Where(c => c.Id == id && c.TenantId == tenantId)
            .Select(c => new OnlineCustomerDetail
            {
                Id = c.Id,
                Email = c.Email,
                DisplayName = c.DisplayName,
                Phone = c.Phone,
                Provider = c.Provider.ToString().ToLowerInvariant(),
                ProviderUserId = c.ProviderUserId,
                IsActive = c.IsActive,
                IsEmailVerified = c.EmailVerified,
                CreatedAtUtc = c.CreatedAtUtc,
                LastLoginAtUtc = c.LastLoginAtUtc,
                UpdatedAtUtc = c.UpdatedAtUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (customer != null)
        {
            customer.ActiveSessions = await _dbContext.OnlineCustomerRefreshTokens
                .CountAsync(
                    t => t.TenantId == tenantId &&
                         t.OnlineCustomerId == id &&
                         !t.IsRevoked &&
                         t.ExpiresAtUtc > DateTime.UtcNow,
                    cancellationToken);
        }

        if (customer == null)
        {
            _logger.LogWarning(
                "[OnlineCustomer] {Action} - Customer not found | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                "GET",
                tenantId,
                id,
                traceId);
            return NotFound(new { error = "CUSTOMER_NOT_FOUND", message = "Online customer not found" });
        }

        _logger.LogInformation(
            "[OnlineCustomer] {Action} success | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
            "GET",
            tenantId,
            id,
            traceId);

        return Ok(customer);
    }

    /// <summary>
    /// Activates an online customer account.
    /// Enables login for the customer.
    /// </summary>
    [HttpPost("{id:guid}/activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ActivateCustomer(Guid id, CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var tenantId = TenantId;

        _logger.LogInformation(
            "[OnlineCustomer] {Action} request | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
            "ACTIVATION",
            tenantId,
            id,
            traceId);

        var customer = await _dbContext.OnlineCustomers
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, cancellationToken);

        if (customer == null)
        {
            _logger.LogWarning(
                "[OnlineCustomer] {Action} - Customer not found | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                "ACTIVATION",
                tenantId,
                id,
                traceId);
            return NotFound(new { error = "CUSTOMER_NOT_FOUND", message = "Online customer not found" });
        }

        if (customer.IsActive)
        {
            _logger.LogDebug(
                "[OnlineCustomer] {Action} - Already active (idempotent) | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                "ACTIVATION",
                tenantId,
                id,
                traceId);
            return NoContent(); // Already active - idempotent
        }

        // Update PostgreSQL (Write Model - Source of Truth)
        customer.IsActive = true;
        customer.UpdatedAtUtc = DateTime.UtcNow;

        // Publish event for MongoDB projection & cache invalidation BEFORE SaveChanges
        // This ensures the event is staged in the outbox as part of the same transaction
        await _eventPublisher.PublishAsync(new OnlineCustomerActivatedEvent
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            Email = customer.Email,
            ActivatedAtUtc = DateTime.UtcNow
        }, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[OnlineCustomer] {Action} success - Customer can now authenticate | TenantId: {TenantId} | CustomerId: {CustomerId} | Email: {Email} | TraceId: {TraceId}",
            "ACTIVATION",
            tenantId,
            id,
            MaskEmail(customer.Email),
            traceId);

        return NoContent();
    }

    /// <summary>
    /// Deactivates an online customer account.
    /// Disables login and invalidates tokens.
    /// 
    /// SECURITY NOTES:
    /// - This is a SOFT DELETE operation - data is retained
    /// - There is NO hard delete for online customers
    /// - Cache is invalidated to prevent stale data usage
    /// - TODO (Phase 10.3.2): JWT tokens must be invalidated/rejected for deactivated customers
    /// </summary>
    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeactivateCustomer(
        Guid id,
        [FromBody] DeactivateCustomerRequest? request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var tenantId = TenantId;

        // Log at WARNING level - deactivation is a critical security event
        _logger.LogWarning(
            "[OnlineCustomer] {Action} request | TenantId: {TenantId} | CustomerId: {CustomerId} | Reason: {Reason} | TraceId: {TraceId}",
            "DEACTIVATION",
            tenantId,
            id,
            request?.Reason ?? "Not specified",
            traceId);

        var customer = await _dbContext.OnlineCustomers
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, cancellationToken);

        if (customer == null)
        {
            _logger.LogWarning(
                "[OnlineCustomer] {Action} - Customer not found | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                "DEACTIVATION",
                tenantId,
                id,
                traceId);
            return NotFound(new { error = "CUSTOMER_NOT_FOUND", message = "Online customer not found" });
        }

        if (!customer.IsActive)
        {
            _logger.LogDebug(
                "[OnlineCustomer] {Action} - Already inactive (idempotent) | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                "DEACTIVATION",
                tenantId,
                id,
                traceId);
            return NoContent(); // Already inactive - idempotent
        }

        // Update PostgreSQL (Write Model - Source of Truth)
        customer.IsActive = false;
        customer.UpdatedAtUtc = DateTime.UtcNow;
        
        // SECURITY: Set durable cutoff timestamp to invalidate all tokens
        // This ensures tokens are rejected even if blacklist store is unavailable
        customer.TokensValidAfterUtc = DateTime.UtcNow;

        // Publish event for MongoDB projection, cache invalidation & token revocation BEFORE SaveChanges
        // This ensures the event is staged in the outbox as part of the same transaction
        // TODO (Phase 10.3.2): OnlineCustomerDeactivatedEvent consumer should also revoke active JWTs
        await _eventPublisher.PublishAsync(new OnlineCustomerDeactivatedEvent
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            Email = customer.Email,
            DeactivatedAtUtc = DateTime.UtcNow,
            Reason = request?.Reason
        }, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // CRITICAL: Immediately revoke all sessions and blacklist tokens
        // This ensures the deactivation is effective IMMEDIATELY even for logged-in users
        var adminUserId = Guid.Empty.ToString();
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim != null)
        {
            adminUserId = userIdClaim.Value;
        }

        var revokeReason = $"Account Deactivation: {request?.Reason ?? "No reason provided"}";
        await _sessionService.RevokeAllSessionsAsync(tenantId, id, adminUserId, revokeReason, cancellationToken);

        // Log at WARNING level - deactivation is a critical security event
        _logger.LogWarning(
            "[OnlineCustomer] {Action} success - Customer can NO LONGER authenticate | TenantId: {TenantId} | CustomerId: {CustomerId} | Email: {Email} | Reason: {Reason} | TraceId: {TraceId}",
            "DEACTIVATION",
            tenantId,
            id,
            MaskEmail(customer.Email),
            request?.Reason ?? "Not specified",
            traceId);

        return NoContent();
    }

    // =========================================================================
    // SOFT DELETE ENFORCEMENT
    // =========================================================================
    // 
    // CRITICAL: There is NO DeleteCustomer endpoint intentionally.
    // OnlineCustomers MUST NEVER be hard-deleted due to:
    // 1. Legal/audit requirements (GDPR right to erasure handled via anonymization)
    // 2. Order history integrity
    // 3. Fraud prevention (prevent account re-creation)
    // 
    // To "delete" a customer:
    // - Use the DeactivateCustomer endpoint
    // - Customer cannot log in but data is retained
    // =========================================================================

    private string GetTraceId() => Activity.Current?.Id ?? HttpContext.TraceIdentifier;

    /// <summary>
    /// Masks email for logging to prevent PII leakage.
    /// Example: "user@example.com" -> "u***@example.com"
    /// </summary>
    private static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "[empty]";

        var atIndex = email.IndexOf('@');
        if (atIndex <= 1)
            return "***@" + (atIndex >= 0 ? email[(atIndex + 1)..] : "***");

        return email[0] + "***" + email[(atIndex - 1)..];
    }

    #region Wishlist Endpoints (Phase 10.3.6)

    /// <summary>
    /// Gets a customer's wishlist (read-only admin view).
    /// For customer insight, marketing, and fraud detection.
    /// </summary>
    [HttpGet("{id:guid}/wishlist")]
    [ProducesResponseType(typeof(AdminCustomerWishlistResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCustomerWishlist(Guid id, CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var tenantId = TenantId;

        _logger.LogInformation(
            "[OnlineCustomer] {Action} request | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
            "GET_WISHLIST",
            tenantId,
            id,
            traceId);

        // Verify customer exists and belongs to tenant
        var customerExists = await _dbContext.OnlineCustomers
            .AsNoTracking()
            .AnyAsync(c => c.Id == id && c.TenantId == tenantId, cancellationToken);

        if (!customerExists)
        {
            _logger.LogWarning(
                "[OnlineCustomer] {Action} - Customer not found | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                "GET_WISHLIST",
                tenantId,
                id,
                traceId);
            return NotFound(new { error = "CUSTOMER_NOT_FOUND", message = "Online customer not found" });
        }

        var wishlist = await _wishlistService.GetWishlistForAdminAsync(tenantId, id, cancellationToken);

        _logger.LogInformation(
            "[OnlineCustomer] {Action} success | TenantId: {TenantId} | CustomerId: {CustomerId} | ItemCount: {ItemCount} | TraceId: {TraceId}",
            "GET_WISHLIST",
            tenantId,
            id,
            wishlist.TotalCount,
            traceId);

        return Ok(new AdminCustomerWishlistResponse
        {
            CustomerId = id,
            Items = wishlist.Items.Select(i => new AdminWishlistItemResponse
            {
                ProductId = i.ProductId,
                AddedAtUtc = i.AddedAtUtc,
                ProductName = i.ProductName,
                ProductImage = i.ProductImage,
                ProductSku = i.ProductSku,
                ProductPrice = i.ProductPrice,
                ProductDeleted = i.ProductDeleted
            }).ToList(),
            TotalCount = wishlist.TotalCount,
            LastUpdatedAtUtc = wishlist.LastUpdatedAtUtc
        });
    }

    #endregion
}

#region DTOs

public class OnlineCustomerListResponse
{
    public List<OnlineCustomerListItem> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}

public class OnlineCustomerListItem
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Phone { get; set; }
    public string Provider { get; set; } = "local";
    public bool IsActive { get; set; }
    public bool IsEmailVerified { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
    public int ActiveSessions { get; set; }
}

public class OnlineCustomerDetail : OnlineCustomerListItem
{
    public string? ProviderUserId { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public class DeactivateCustomerRequest
{
    public string? Reason { get; set; }
}

// Wishlist DTOs (Phase 10.3.6)
public class AdminCustomerWishlistResponse
{
    public Guid CustomerId { get; set; }
    public List<AdminWishlistItemResponse> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public DateTime? LastUpdatedAtUtc { get; set; }
}

public class AdminWishlistItemResponse
{
    public Guid ProductId { get; set; }
    public DateTime AddedAtUtc { get; set; }
    
    // Product details (fetched from catalog)
    public string? ProductName { get; set; }
    public string? ProductImage { get; set; }
    public string? ProductSku { get; set; }
    public decimal? ProductPrice { get; set; }
    public bool ProductDeleted { get; set; } = false;
}

#endregion
