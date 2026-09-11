using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Security;

namespace PointOfSale.Tests.Security;

public sealed class GuestTrackingTokenRenewalTests
{
    [Fact]
    public void GuestTrackingToken_CanBeRenewed_AndRemainsBoundToOrderAndSession()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var sessionId = Guid.NewGuid().ToString("D");
        var service = CreateTokenService();

        var initialToken = service.CreateGuestTrackingToken(tenantId, orderId, sessionId);
        var initialValidation = service.ValidateGuestTrackingToken(initialToken, tenantId);

        Assert.True(initialValidation.IsValid);
        Assert.Equal(orderId.ToString("N"), initialValidation.Subject);
        Assert.Equal(sessionId, initialValidation.Nonce);

        var renewedToken = service.CreateGuestTrackingToken(tenantId, orderId, initialValidation.Nonce!);
        var renewedValidation = service.ValidateGuestTrackingToken(renewedToken, tenantId);

        Assert.True(renewedValidation.IsValid);
        Assert.Equal(orderId.ToString("N"), renewedValidation.Subject);
        Assert.Equal(sessionId, renewedValidation.Nonce);
        // Verify that renewal actually extended the expiration time
        Assert.True(renewedValidation.ExpiresAtUtc > initialValidation.ExpiresAtUtc,
            "Renewed token expiry should be later than initial token expiry");
    }

    [Fact]
    public void GuestTrackingToken_CannotCrossTenantBoundary()
    {
        var tenantId = Guid.NewGuid();
        var service = CreateTokenService();
        var token = service.CreateGuestTrackingToken(
            tenantId,
            Guid.NewGuid(),
            Guid.NewGuid().ToString("D"));

        var validation = service.ValidateGuestTrackingToken(token, Guid.NewGuid());

        Assert.False(validation.IsValid);
        Assert.False(validation.IsExpired);
    }

    private static CheckoutSessionTokenService CreateTokenService()
    {
        var keyDirectory = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"pos-guest-tracking-{Guid.NewGuid():N}"));
        var provider = DataProtectionProvider.Create(keyDirectory);

        return new CheckoutSessionTokenService(
            provider,
            Options.Create(new CheckoutSessionSecurityOptions
            {
                GuestTrackingTokenTtlMinutes = 120
            }),
            NullLogger<CheckoutSessionTokenService>.Instance);
    }
}
