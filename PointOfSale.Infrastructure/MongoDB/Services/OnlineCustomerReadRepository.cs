using MongoDB.Driver;
using PointOfSale.Core.Helpers;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Infrastructure.MongoDB.Services;

/// <summary>
/// Repository interface for reading online customer data from MongoDB.
/// Storefront READ-ONLY access.
/// 
/// Phase: 10.3.1 - Online Customer Identity (Core)
/// Phase: 10.3.1 Hardening - Email Normalization
/// </summary>
public interface IOnlineCustomerReadRepository
{
    /// <summary>
    /// Gets an online customer by their ID.
    /// </summary>
    Task<OnlineCustomerDocument?> GetByIdAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an online customer by their email address.
    /// Email is normalized before lookup (Trim + ToLowerInvariant).
    /// </summary>
    Task<OnlineCustomerDocument?> GetByEmailAsync(Guid tenantId, string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an email is already registered for the given tenant.
    /// Email is normalized before lookup (Trim + ToLowerInvariant).
    /// </summary>
    Task<bool> EmailExistsAsync(Guid tenantId, string email, CancellationToken cancellationToken = default);
}

/// <summary>
/// MongoDB repository implementation for OnlineCustomer read operations.
/// 
/// ARCHITECTURAL RULES:
/// - Read-only operations only
/// - Tenant isolation enforced on all queries
/// - Used by Storefront API
/// - Emails are normalized using EmailNormalizer.Normalize() (Trim + ToLowerInvariant)
/// 
/// SOFT DELETE POLICY:
/// - OnlineCustomers are NEVER deleted - only deactivated via IsActive = false
/// - This repository does NOT filter by IsActive - that's the caller's responsibility
/// - Legal/audit requirements mandate data retention
/// 
/// Phase: 10.3.1 - Online Customer Identity (Core)
/// Phase: 10.3.1 Hardening - Email Normalization & Soft Delete Enforcement
/// </summary>
public class OnlineCustomerReadRepository : IOnlineCustomerReadRepository
{
    private readonly IMongoCollection<OnlineCustomerDocument> _collection;

    public OnlineCustomerReadRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<OnlineCustomerDocument>("online_customers");
    }

    public async Task<OnlineCustomerDocument?> GetByIdAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken = default)
    {
        // SECURITY: Reject Guid.Empty to prevent cross-tenant access
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId cannot be empty", nameof(tenantId));

        var filter = Builders<OnlineCustomerDocument>.Filter.And(
            Builders<OnlineCustomerDocument>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<OnlineCustomerDocument>.Filter.Eq(x => x.CustomerId, customerId));

        return await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<OnlineCustomerDocument?> GetByEmailAsync(Guid tenantId, string email, CancellationToken cancellationToken = default)
    {
        // SECURITY: Reject Guid.Empty to prevent cross-tenant access
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId cannot be empty", nameof(tenantId));

        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email cannot be empty", nameof(email));

        // CRITICAL: Use EmailNormalizer for culture-safe normalization (Trim + ToLowerInvariant)
        var normalizedEmail = EmailNormalizer.Normalize(email);
        
        var filter = Builders<OnlineCustomerDocument>.Filter.And(
            Builders<OnlineCustomerDocument>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<OnlineCustomerDocument>.Filter.Eq(x => x.Email, normalizedEmail));

        return await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> EmailExistsAsync(Guid tenantId, string email, CancellationToken cancellationToken = default)
    {
        // SECURITY: Reject Guid.Empty to prevent cross-tenant access
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId cannot be empty", nameof(tenantId));

        if (string.IsNullOrWhiteSpace(email))
            return false;

        // CRITICAL: Use EmailNormalizer for culture-safe normalization (Trim + ToLowerInvariant)
        var normalizedEmail = EmailNormalizer.Normalize(email);
        
        var filter = Builders<OnlineCustomerDocument>.Filter.And(
            Builders<OnlineCustomerDocument>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<OnlineCustomerDocument>.Filter.Eq(x => x.Email, normalizedEmail));

        var count = await _collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        return count > 0;
    }

    // =========================================================================
    // SOFT DELETE ENFORCEMENT
    // =========================================================================
    // 
    // CRITICAL: There is NO DeleteAsync method intentionally.
    // OnlineCustomers MUST NEVER be hard-deleted due to:
    // 1. Legal/audit requirements (GDPR right to erasure handled via anonymization)
    // 2. Order history integrity
    // 3. Fraud prevention (prevent account re-creation)
    // 
    // To "delete" a customer:
    // - Set IsActive = false in PostgreSQL
    // - Emit OnlineCustomerDeactivatedEvent
    // - MongoDB projection will update IsActive = false
    // - Customer cannot log in but data is retained
    // =========================================================================
}
