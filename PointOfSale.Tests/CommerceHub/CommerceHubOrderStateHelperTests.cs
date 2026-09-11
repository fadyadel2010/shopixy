using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubOrderStateHelperTests
{
    [Fact]
    public void AggregateMarketplaceStatus_DeliveredAndRefunded_RemainsDelivered()
    {
        var status = CommerceHubOrderStateHelper.AggregateMarketplaceStatus(
            new[]
            {
                new CommerceHubSubOrder { Status = OrderStatus.Delivered },
                new CommerceHubSubOrder { Status = OrderStatus.Refunded }
            },
            OrderStatus.Delivered);

        Assert.Equal(OrderStatus.Delivered, status);
    }
}
