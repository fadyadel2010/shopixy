using PointOfSale.Core.DTOs.Marketplace;
using PointOfSale.Core.Entities;

namespace PointOfSale.Core.Interfaces;

public interface IServiceReviewService
{
    Task<ServiceReview> CreateReviewAsync(Guid buyerUserId, CreateReviewDto dto, CancellationToken cancellationToken = default);
    Task<ServiceReview?> GetReviewByRequestIdAsync(Guid serviceRequestId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ServiceReview>> GetReviewsByServiceIdAsync(Guid serviceId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ServiceReview>> GetReviewsByProviderIdAsync(Guid providerUserId, CancellationToken cancellationToken = default);
}
