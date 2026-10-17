using System.Reflection;
using PointOfSale.API.Controllers;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.Services.Shipping;
using PointOfSale.Core.Entities;

namespace PointOfSale.Tests.Admin;

public sealed class Phase5TrackingUrlTests
{
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,unsafe")]
    [InlineData("https://username:password@example.test/track/{tracking_number}")]
    [InlineData("//example.test/track")]
    public void UnsafeTemplate_IsRejectedAtAuthoring(string template)
    {
        Assert.NotNull(new CreateShippingMethodDto { Name = "P5QA", TrackingUrlTemplate = template }.Validate());
    }

    [Theory]
    [InlineData("{tracking_number}")]
    [InlineData("{trackingNumber}")]
    public void BothShipmentWriters_EncodeTheSameSupportedTrackingPlaceholder(string placeholder)
    {
        var template = "https://example.test/track/" + placeholder;
        var method = typeof(AdminShipmentService).GetMethod("BuildTrackingUrl", BindingFlags.Static | BindingFlags.NonPublic)!;
        var admin = method.Invoke(null, [new ShippingMethod { TrackingUrlTemplate = template }, "P5QA/A?B#C"]);
        method = typeof(ShipmentService).GetMethod("BuildTrackingUrl", BindingFlags.Static | BindingFlags.NonPublic)!;
        var shipping = method.Invoke(null, [template, "P5QA/A?B#C"]);
        Assert.Equal("https://example.test/track/P5QA%2FA%3FB%23C", admin);
        Assert.Equal(admin, shipping);
    }
}
