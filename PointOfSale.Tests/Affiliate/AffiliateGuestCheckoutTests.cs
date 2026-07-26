using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.Events;
using PointOfSale.Core.Entities;
using Xunit;

namespace PointOfSale.Tests.Affiliate;

/// <summary>
/// AF-13B: Affiliate Guest Checkout architecture tests.
///
/// Validates:
/// 1. Order entity has IsGuestCheckout flag
/// 2. CheckoutOrderCreatedEvent has guest checkout fields
/// 3. CheckoutController allows unauthenticated access (no [Authorize])
/// 4. Guest fields are nullable (no breaking changes)
/// 5. Existing normal checkout behavior is preserved
/// </summary>
public class AffiliateGuestCheckoutTests
{
    // ═══════════════════════════════════════════════════════════════════
    // ORDER ENTITY — GUEST CHECKOUT FLAG
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Order_HasIsGuestCheckoutProperty()
    {
        var prop = typeof(Order).GetProperty("IsGuestCheckout");
        Assert.NotNull(prop);
        Assert.Equal(typeof(bool), prop!.PropertyType);
    }

    [Fact]
    public void Order_IsGuestCheckout_DefaultsFalse()
    {
        var order = new Order();
        Assert.False(order.IsGuestCheckout);
    }

    [Fact]
    public void Order_HasGuestFields()
    {
        var props = typeof(Order).GetProperties().Select(p => p.Name).ToList();

        Assert.Contains("GuestName", props);
        Assert.Contains("GuestPhone", props);
        Assert.Contains("GuestEmail", props);
        Assert.Contains("IsGuestCheckout", props);
    }

    [Fact]
    public void Order_GuestFields_AreNullable()
    {
        // GuestName, GuestPhone, GuestEmail must be nullable strings
        var nameType = typeof(Order).GetProperty("GuestName")!.PropertyType;
        var phoneType = typeof(Order).GetProperty("GuestPhone")!.PropertyType;
        var emailType = typeof(Order).GetProperty("GuestEmail")!.PropertyType;

        // string? in C# is still typeof(string), but must be nullable
        Assert.True(nameType == typeof(string));
        Assert.True(phoneType == typeof(string));
        Assert.True(emailType == typeof(string));

        // Verify defaults are null (not empty string)
        var order = new Order();
        Assert.Null(order.GuestName);
        Assert.Null(order.GuestPhone);
        Assert.Null(order.GuestEmail);
    }

    [Fact]
    public void Order_CustomerIds_AreNullable()
    {
        // Both customer FKs must be nullable to support guest checkout
        var customerId = typeof(Order).GetProperty("CustomerId");
        var onlineCustomerId = typeof(Order).GetProperty("OnlineCustomerId");

        Assert.NotNull(customerId);
        Assert.NotNull(onlineCustomerId);
        Assert.Equal(typeof(Guid?), customerId!.PropertyType);
        Assert.Equal(typeof(Guid?), onlineCustomerId!.PropertyType);
    }

    // ═══════════════════════════════════════════════════════════════════
    // CHECKOUT EVENT — GUEST FIELDS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CheckoutOrderCreatedEvent_HasIsGuestCheckout()
    {
        var prop = typeof(CheckoutOrderCreatedEvent).GetProperty("IsGuestCheckout");
        Assert.NotNull(prop);
        Assert.Equal(typeof(bool), prop!.PropertyType);
    }

    [Fact]
    public void CheckoutOrderCreatedEvent_HasGuestPhone()
    {
        var prop = typeof(CheckoutOrderCreatedEvent).GetProperty("GuestPhone");
        Assert.NotNull(prop);
        Assert.Equal(typeof(string), prop!.PropertyType);
    }

    [Fact]
    public void CheckoutOrderCreatedEvent_HasGuestEmail()
    {
        var prop = typeof(CheckoutOrderCreatedEvent).GetProperty("GuestEmail");
        Assert.NotNull(prop);
        Assert.Equal(typeof(string), prop!.PropertyType);
    }

    [Fact]
    public void CheckoutOrderCreatedEvent_CustomerId_IsNullable()
    {
        var prop = typeof(CheckoutOrderCreatedEvent).GetProperty("CustomerId");
        Assert.NotNull(prop);
        Assert.Equal(typeof(Guid?), prop!.PropertyType);
    }

    // ═══════════════════════════════════════════════════════════════════
    // CHECKOUT CONTROLLER — NO [AUTHORIZE] ATTRIBUTE
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CheckoutController_HasNoAuthorizeAttribute()
    {
        // CheckoutController must NOT have [Authorize] — guest checkout requires open access
        var controllerType = typeof(PointOfSale.API.Controllers.CheckoutController);
        var authorizeAttr = controllerType.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>();
        Assert.Null(authorizeAttr);
    }

    [Fact]
    public void CheckoutController_IsApiController()
    {
        var controllerType = typeof(PointOfSale.API.Controllers.CheckoutController);
        var apiAttr = controllerType.GetCustomAttribute<ApiControllerAttribute>();
        Assert.NotNull(apiAttr);
    }

    // ═══════════════════════════════════════════════════════════════════
    // INVARIANTS — IsGuestCheckout CONSISTENCY
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GuestOrder_HasNullCustomerIds()
    {
        var order = new Order
        {
            IsGuestCheckout = true,
            GuestName = "Test Guest",
            GuestPhone = "01234567890",
            CustomerId = null,
            OnlineCustomerId = null
        };

        Assert.True(order.IsGuestCheckout);
        Assert.Null(order.CustomerId);
        Assert.Null(order.OnlineCustomerId);
        Assert.NotNull(order.GuestName);
        Assert.NotNull(order.GuestPhone);
    }

    [Fact]
    public void AuthenticatedOrder_HasFalseIsGuestCheckout()
    {
        var order = new Order
        {
            IsGuestCheckout = false,
            OnlineCustomerId = Guid.NewGuid(),
            CustomerId = null, // Storefront orders don't use POS CustomerId
        };

        Assert.False(order.IsGuestCheckout);
        Assert.NotNull(order.OnlineCustomerId);
    }

    // ═══════════════════════════════════════════════════════════════════
    // BACKWARDS COMPATIBILITY — EXISTING ORDERS UNAFFECTED
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void NewOrder_DefaultsToNonGuest()
    {
        // An order created with default constructor should NOT be a guest checkout
        var order = new Order();
        Assert.False(order.IsGuestCheckout);
    }

    [Fact]
    public void ExistingOrderShape_Intact()
    {
        // Verify existing order properties still exist
        var props = typeof(Order).GetProperties().Select(p => p.Name).ToList();

        // Core fields that must never be removed
        Assert.Contains("TenantId", props);
        Assert.Contains("OrderNumber", props);
        Assert.Contains("CustomerId", props);
        Assert.Contains("OnlineCustomerId", props);
        Assert.Contains("Status", props);
        Assert.Contains("PaymentStatus", props);
        Assert.Contains("Total", props);
        Assert.Contains("Currency", props);
        Assert.Contains("Items", props);
    }
}
