using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Entities;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubPaymentMethodAuthorityTests
{
    [Fact]
    public void BuildAvailability_WhenStoreHasNoEnabledPaymentMethods_FailsClosedForAllProviders()
    {
        var tenantId = Guid.NewGuid();

        var availability = CommerceHubPaymentMethodAuthority.BuildAvailability(
            new[] { tenantId },
            Array.Empty<TenantPaymentMethod>());

        Assert.False(availability.Single(method => method.Code == CommerceHubPaymentMethods.Cod).IsAvailable);
        Assert.False(availability.Single(method => method.Code == CommerceHubPaymentMethods.Paymob).IsAvailable);
        Assert.False(availability.Single(method => method.Code == CommerceHubPaymentMethods.Stripe).IsAvailable);
    }

    [Fact]
    public void BuildAvailability_WhenEachStoreEnablesDifferentOnlineProvider_RequiresExactProviderIntersection()
    {
        var firstTenantId = Guid.NewGuid();
        var secondTenantId = Guid.NewGuid();
        var paymentMethods = new[]
        {
            CreatePaymentMethod(firstTenantId, CommerceHubPaymentMethods.Cod, supportsOffline: true),
            CreatePaymentMethod(secondTenantId, CommerceHubPaymentMethods.Cod, supportsOffline: true),
            CreatePaymentMethod(firstTenantId, CommerceHubPaymentMethods.Paymob, supportsOnline: true),
            CreatePaymentMethod(secondTenantId, CommerceHubPaymentMethods.Stripe, supportsOnline: true)
        };

        var availability = CommerceHubPaymentMethodAuthority.BuildAvailability(
            new[] { firstTenantId, secondTenantId },
            paymentMethods);

        Assert.True(availability.Single(method => method.Code == CommerceHubPaymentMethods.Cod).IsAvailable);
        Assert.False(availability.Single(method => method.Code == CommerceHubPaymentMethods.Paymob).IsAvailable);
        Assert.False(availability.Single(method => method.Code == CommerceHubPaymentMethods.Stripe).IsAvailable);
    }

    private static TenantPaymentMethod CreatePaymentMethod(
        Guid tenantId,
        string code,
        bool supportsOnline = false,
        bool supportsOffline = false)
    {
        return new TenantPaymentMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = code,
            DisplayName = code,
            IsEnabled = true,
            SupportsOnline = supportsOnline,
            SupportsOffline = supportsOffline
        };
    }
}
