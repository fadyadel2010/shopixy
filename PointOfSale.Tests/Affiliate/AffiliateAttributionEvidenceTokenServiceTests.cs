using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Security;

namespace PointOfSale.Tests.Affiliate;

public sealed class AffiliateAttributionEvidenceTokenServiceTests
{
    [Fact]
    public void ProtectedEvidence_RoundTripsOnlyForOwningTenant()
    {
        var clock = new MutableTimeProvider(
            new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(clock);
        var tenantId = Guid.NewGuid();
        var affiliateId = Guid.NewGuid();
        var promotionLinkId = Guid.NewGuid();

        var token = service.Create(tenantId, affiliateId, " ref123 ", promotionLinkId);

        Assert.True(service.TryValidate(token, tenantId, out var evidence));
        Assert.Equal(affiliateId, evidence.AffiliateAccountId);
        Assert.Equal("REF123", evidence.ReferralCode);
        Assert.Equal(promotionLinkId, evidence.PromotionLinkId);
        Assert.False(service.TryValidate(token, Guid.NewGuid(), out _));
    }

    [Fact]
    public void ProtectedEvidence_RejectsTamperingAndExpiration()
    {
        var clock = new MutableTimeProvider(
            new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(clock);
        var tenantId = Guid.NewGuid();
        var token = service.Create(tenantId, Guid.NewGuid(), "REF123", null);

        Assert.False(service.TryValidate($"{token}x", tenantId, out _));

        clock.Advance(TimeSpan.FromDays(30).Add(TimeSpan.FromSeconds(1)));

        Assert.False(service.TryValidate(token, tenantId, out _));
    }

    private static AffiliateAttributionEvidenceTokenService CreateService(
        TimeProvider timeProvider) =>
        new(
            new EphemeralDataProtectionProvider(),
            timeProvider,
            NullLogger<AffiliateAttributionEvidenceTokenService>.Instance);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
