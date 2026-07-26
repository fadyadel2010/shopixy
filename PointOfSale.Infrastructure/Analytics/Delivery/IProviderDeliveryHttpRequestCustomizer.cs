using System.Net.Http;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Analytics.Delivery;

public interface IProviderDeliveryHttpRequestCustomizer
{
    string ProviderName { get; }

    Task CustomizeAsync(
        ProviderDeliveryOutboxMessage message,
        HttpRequestMessage request,
        CancellationToken cancellationToken = default);
}
