using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Infrastructure.MongoDB.Services;

/// <summary>
/// Repository interface for payment method read operations from MongoDB.
/// 
/// ARCHITECTURE (CQRS COMPLIANCE):
/// - MongoDB is the read-only projection source for Storefront/Checkout
/// - PostgreSQL is write-only for payment methods
/// - All queries MUST filter by TenantId
/// - NO SECRETS - ConfigEncrypted is NEVER exposed
/// </summary>
public interface IPaymentMethodReadRepository
{
    /// <summary>
    /// Gets all enabled payment methods for a tenant, ordered by SortOrder.
    /// </summary>
    Task<List<PaymentMethodDocument>> GetEnabledPaymentMethodsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a payment method by ID for a tenant.
    /// </summary>
    Task<PaymentMethodDocument?> GetByIdAsync(
        Guid tenantId,
        Guid paymentMethodId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a payment method by code for a tenant.
    /// Used during checkout to validate payment method selection.
    /// </summary>
    Task<PaymentMethodDocument?> GetByCodeAsync(
        Guid tenantId,
        string code,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts a payment method document.
    /// Idempotent by (TenantId, PaymentMethodId).
    /// </summary>
    Task<bool> UpsertAsync(
        PaymentMethodDocument document,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a payment method document.
    /// </summary>
    Task<bool> DeleteAsync(
        Guid tenantId,
        Guid paymentMethodId,
        CancellationToken cancellationToken = default);
}
