using PointOfSale.Core.DTOs.Marketplace;
using PointOfSale.Core.Entities;

namespace PointOfSale.Core.Interfaces;

public interface IServiceRequestService
{
    Task<ServiceRequestEntryAuthority> GetRequestEntryAuthorityAsync(Guid serviceId, Guid buyerUserId, CancellationToken cancellationToken = default);
    Task<ServiceRequest> RequestServiceAsync(Guid buyerUserId, RequestServiceDto dto, CancellationToken cancellationToken = default);
    Task<ServiceRequest> AcceptRequestAsync(Guid requestId, Guid providerUserId, CancellationToken cancellationToken = default);
    Task<ServiceRequest> RejectRequestAsync(Guid requestId, Guid providerUserId, CancellationToken cancellationToken = default);
    Task<ServiceRequest> MarkCompletedByProviderAsync(Guid requestId, Guid providerUserId, CancellationToken cancellationToken = default);
    Task<ServiceRequest> ConfirmCompletedByBuyerAsync(Guid requestId, Guid buyerUserId, CancellationToken cancellationToken = default);
    Task<ServiceRequest> CancelRequestAsync(Guid requestId, Guid userId, string? reason = null, CancellationToken cancellationToken = default);
    Task<ServiceRequest?> GetRequestByIdAsync(Guid requestId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ServiceRequest>> GetRequestsByBuyerAsync(Guid buyerUserId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ServiceRequest>> GetRequestsByProviderAsync(Guid providerUserId, CancellationToken cancellationToken = default);
}
