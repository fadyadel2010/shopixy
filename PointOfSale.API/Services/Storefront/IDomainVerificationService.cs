using PointOfSale.Core.Models;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Executes one domain ownership verification attempt.
/// Owns verification evaluation and persistence; callers such as the admin API
/// and scheduler coordinate when an attempt is requested.
/// </summary>
public interface IDomainVerificationService
{
    Task<VerificationResult> VerifyAsync(
        Guid domainId,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken = default);
}
