using PointOfSale.API.DTOs.Admin;

namespace PointOfSale.API.Services.Admin;

public interface IPaymentProviderDashboardService
{
    Task<PaymentProviderDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default);
}
