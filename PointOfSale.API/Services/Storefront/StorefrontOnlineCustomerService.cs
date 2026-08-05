using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Service interface for Storefront online customer operations.
/// READ-ONLY for Storefront.
/// 
/// Phase: 10.3.1 - Online Customer Identity (Core)
/// Phase: 10.3.1 Hardening - Security & Observability
/// </summary>
public interface IStorefrontOnlineCustomerService
{
    /// <summary>
    /// Gets the current customer's profile from PostgreSQL authority.
    /// </summary>
    Task<OnlineCustomerProfileDto?> GetProfileAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an email is already registered for the given tenant.
    /// Email is normalized before lookup (Trim + ToLowerInvariant).
    /// </summary>
    Task<bool> EmailExistsAsync(Guid tenantId, string email, CancellationToken cancellationToken = default);
}

/// <summary>
/// Storefront service for online customer operations.
/// Implements SQL-authoritative profile reads.
/// 
/// ARCHITECTURAL RULES:
/// - Read-only operations only
/// - PostgreSQL is read on every profile request
/// - Emails are normalized using EmailNormalizer.Normalize()
/// 
/// SECURITY NOTES:
/// - IsActive is included in response - caller MUST check it
/// 
/// Phase: 10.3.1 - Online Customer Identity (Core)
/// Phase: 10.3.1 Hardening - Security & Observability
/// </summary>
public class StorefrontOnlineCustomerService : IStorefrontOnlineCustomerService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<StorefrontOnlineCustomerService> _logger;

    public StorefrontOnlineCustomerService(
        ApplicationDbContext dbContext,
        ILogger<StorefrontOnlineCustomerService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<OnlineCustomerProfileDto?> GetProfileAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken = default)
    {
        // SECURITY: Reject Guid.Empty to prevent cross-tenant access
        if (tenantId == Guid.Empty || customerId == Guid.Empty)
        {
            _logger.LogWarning(
                "[StorefrontCustomerService] {Action} - Invalid request (empty GUID) | TenantId: {TenantId} | CustomerId: {CustomerId}",
                "GET_PROFILE",
                tenantId,
                customerId);
            return null;
        }

        var customer = await _dbContext.OnlineCustomers
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item =>
                item.TenantId == tenantId &&
                item.Id == customerId,
                cancellationToken);

        if (customer == null)
        {
            _logger.LogDebug(
                "[StorefrontCustomerService] {Action} - Customer not found in SQL | TenantId: {TenantId} | CustomerId: {CustomerId}",
                "GET_PROFILE",
                tenantId,
                customerId);
            return null;
        }

        return MapToProfileDto(customer);
    }

    public async Task<bool> EmailExistsAsync(Guid tenantId, string email, CancellationToken cancellationToken = default)
    {
        // SECURITY: Reject Guid.Empty to prevent cross-tenant access
        if (tenantId == Guid.Empty)
        {
            _logger.LogWarning(
                "[StorefrontCustomerService] {Action} - Invalid request (empty TenantId)",
                "EMAIL_EXISTS");
            return false;
        }

        // CRITICAL: Normalize email using EmailNormalizer (Trim + ToLowerInvariant)
        // The repository also normalizes, but we normalize here for consistency
        var normalizedEmail = EmailNormalizer.Normalize(email);
        
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            _logger.LogDebug(
                "[StorefrontCustomerService] {Action} - Empty email after normalization | TenantId: {TenantId}",
                "EMAIL_EXISTS",
                tenantId);
            return false;
        }

        var exists = await _dbContext.OnlineCustomers
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(item =>
                item.TenantId == tenantId &&
                item.NormalizedEmail == normalizedEmail,
                cancellationToken);

        _logger.LogDebug(
            "[StorefrontCustomerService] {Action} complete | TenantId: {TenantId} | Exists: {Exists}",
            "EMAIL_EXISTS",
            tenantId,
            exists);

        return exists;
    }

    private static OnlineCustomerProfileDto MapToProfileDto(OnlineCustomer customer)
    {
        return new OnlineCustomerProfileDto
        {
            Id = customer.Id,
            Email = customer.Email,
            DisplayName = customer.DisplayName,
            Phone = customer.Phone,
            Provider = customer.Provider.ToString().ToLowerInvariant(),
            IsActive = customer.IsActive,
            EmailVerified = customer.EmailVerified,
            CreatedAtUtc = customer.CreatedAtUtc,
            LastLoginAtUtc = customer.LastLoginAtUtc
        };
    }
}

/// <summary>
/// Online customer profile DTO for Storefront.
/// Contains only non-sensitive customer information.
/// 
/// SECURITY NOTES:
/// - No password hash
/// - No provider user ID
/// - IsActive MUST be checked by caller before allowing actions
/// </summary>
public class OnlineCustomerProfileDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Phone { get; set; }
    public string Provider { get; set; } = "local";
    
    /// <summary>
    /// Whether the customer account is active.
    /// CRITICAL: Caller MUST check this value and reject deactivated customers.
    /// </summary>
    public bool IsActive { get; set; }
    
    public bool EmailVerified { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
}
