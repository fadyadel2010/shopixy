using PointOfSale.Core.DTOs.Marketplace;
using PointOfSale.Core.Entities;

namespace PointOfSale.Core.Interfaces;

public interface IServiceCatalogService
{
    Task<Service> CreateServiceAsync(Guid profileId, Guid userId, CreateServiceDto dto, CancellationToken cancellationToken = default);
    Task<Service> UpdateServiceAsync(Guid serviceId, Guid userId, UpdateServiceDto dto, CancellationToken cancellationToken = default);
    Task<Service?> GetServiceByIdAsync(Guid serviceId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Service>> GetServicesByProfileIdAsync(Guid profileId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Service>> GetActiveServicesAsync(string? category = null, CancellationToken cancellationToken = default);
    Task DeactivateServiceAsync(Guid serviceId, Guid userId, CancellationToken cancellationToken = default);
    Task ActivateServiceAsync(Guid serviceId, Guid userId, CancellationToken cancellationToken = default);
}
