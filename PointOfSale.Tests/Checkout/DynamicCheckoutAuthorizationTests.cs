using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using PointOfSale.API.Controllers;

namespace PointOfSale.Tests.Checkout;

public sealed class DynamicCheckoutAuthorizationTests
{
    [Theory]
    [InlineData(nameof(OrdersController.GetOrder))]
    [InlineData(nameof(OrdersController.GetOrderByNumber))]
    public void LegacyOrderDetails_RequireOrderReadRoles(string methodName)
    {
        var method = typeof(OrdersController).GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        var authorize = method?.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Owner,Admin,Manager", authorize!.Roles);
    }
}
