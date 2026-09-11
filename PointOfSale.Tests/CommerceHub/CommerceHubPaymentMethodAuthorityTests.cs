using Moq;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.Payments.ProviderContracts;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubPaymentMethodAuthorityTests
{
    [Fact]
    public void BuildAvailability_WhenPlatformProvidersAreUnavailable_OnlyAllowsCommerceHubCod()
    {
        var availability = CommerceHubPaymentMethodAuthority.BuildAvailability(
            new[] { Guid.NewGuid() },
            Array.Empty<IPaymentProviderAdapter>());

        Assert.True(availability.Single(method => method.Code == CommerceHubPaymentMethods.Cod).IsAvailable);
        Assert.False(availability.Single(method => method.Code == CommerceHubPaymentMethods.Paymob).IsAvailable);
        Assert.False(availability.Single(method => method.Code == CommerceHubPaymentMethods.Stripe).IsAvailable);
    }

    [Fact]
    public void BuildAvailability_WhenPlatformPaymobIsExecutable_AllowsItForMultiVendorOrder()
    {
        var paymobAdapter = CreateAdapter(CommerceHubPaymentMethods.Paymob, executable: true);

        var availability = CommerceHubPaymentMethodAuthority.BuildAvailability(
            new[] { Guid.NewGuid(), Guid.NewGuid() },
            new[] { paymobAdapter });

        Assert.True(availability.Single(method => method.Code == CommerceHubPaymentMethods.Cod).IsAvailable);
        Assert.True(availability.Single(method => method.Code == CommerceHubPaymentMethods.Paymob).IsAvailable);
        Assert.False(availability.Single(method => method.Code == CommerceHubPaymentMethods.Stripe).IsAvailable);
    }

    [Fact]
    public void BuildAvailability_WhenPlatformStripeCannotVerifyWebhooks_DoesNotAdvertiseStripe()
    {
        var stripeAdapter = CreateAdapter(
            CommerceHubPaymentMethods.Stripe,
            executable: true,
            supportsWebhookVerification: false);

        var availability = CommerceHubPaymentMethodAuthority.BuildAvailability(
            new[] { Guid.NewGuid() },
            new[] { stripeAdapter });

        Assert.False(availability.Single(method => method.Code == CommerceHubPaymentMethods.Stripe).IsAvailable);
    }

    private static IPaymentProviderAdapter CreateAdapter(
        string providerCode,
        bool executable,
        bool? supportsWebhookVerification = null)
    {
        var adapter = new Mock<IPaymentProviderAdapter>();
        adapter.SetupGet(current => current.ProviderCode).Returns(new ProviderCode(providerCode));
        adapter.SetupGet(current => current.Capabilities).Returns(new PaymentProviderCapabilities(
            SupportsAuthentication: executable,
            SupportsPaymentIntentionCreation: executable,
            SupportsPaymentSessionCreation: executable,
            SupportsHostedCheckout: executable,
            SupportsPaymentStatusLookup: executable,
            SupportsWebhookVerification: supportsWebhookVerification ?? executable,
            SupportsCallbackParsing: executable,
            SupportsRefunds: executable,
            Metadata: new Dictionary<string, string>()));
        return adapter.Object;
    }
}
