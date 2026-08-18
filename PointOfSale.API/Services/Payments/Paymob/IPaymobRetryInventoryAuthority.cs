using PointOfSale.Core.Entities;

namespace PointOfSale.API.Services.Payments.Paymob;

public interface IPaymobRetryInventoryAuthority
{
    Task EnsureRetryReservationAuthorityAsync(
        Order order,
        DateTime reservationExpiresAtUtc,
        CancellationToken cancellationToken);
}
