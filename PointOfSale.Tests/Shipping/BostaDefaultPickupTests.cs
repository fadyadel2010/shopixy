using System.Text.Json;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.Tests.Shipping;

public sealed class BostaDefaultPickupTests
{
    private static BostaPickupLocationOption Pickup(string id = "SYNTHETIC-PICKUP", bool primary = true) =>
        new(id, primary, "EG", "SYNTHETIC-CITY", "Test city", "SYNTHETIC-DISTRICT", "Test district") { CountryId = "SYNTHETIC-COUNTRY" };
    private static CarrierCredentials Credentials(string? selected = null) => new() { ApiKey = "SYNTHETIC-KEY-NOT-LIVE",
        ExtraConfig = selected == null ? [] : new() { ["businessLocationId"] = selected } };

    [Fact]
    public void DefaultMustBeUniqueMarkedAndComplete_NotJustTheOnlyLocation()
    {
        var credentials = Credentials();
        Assert.Equal("BOSTA_DEFAULT_PICKUP_MISSING", BostaPickupSelections.Resolve(credentials, new(true, [Pickup(primary: false)], null)).ErrorCode);
        Assert.Equal("BOSTA_DEFAULT_PICKUP_AMBIGUOUS", BostaPickupSelections.Resolve(credentials, new(true, [Pickup(), Pickup("ANOTHER")], null)).ErrorCode);
        Assert.Equal("BOSTA_PICKUP_GEOGRAPHY_INCOMPLETE", BostaPickupSelections.Resolve(credentials, new(true, [Pickup() with { CountryId = null }], null)).ErrorCode);
        Assert.Equal("BOSTA_PICKUP_RESPONSE_INVALID", BostaPickupSelections.Resolve(credentials, new(true, [Pickup(), Pickup()], null)).ErrorCode);
        var selection = BostaPickupSelections.Resolve(credentials, new(true, [Pickup(), Pickup("OVERRIDE", false)], null));
        Assert.True(selection.UsesProviderDefault); Assert.Equal("SYNTHETIC-PICKUP", selection.Location!.Id);
        Assert.Empty(credentials.ExtraConfig);
    }

    [Theory]
    [InlineData("")] [InlineData(" ")] [InlineData(" PICKUP")] [InlineData("PICKUP\r\n")]
    public void InvalidExplicitOverrideNeverFallsBack(string selected)
    {
        var result = BostaPickupSelections.Resolve(Credentials(selected), new(true, [Pickup()], null));
        Assert.Null(result.Location); Assert.False(result.UsesProviderDefault); Assert.Equal("BOSTA_BUSINESS_LOCATION_INVALID", result.ErrorCode);
    }

    [Fact]
    public void UnownedExplicitOverrideNeverFallsBackAndDiscoveryFailureIsNotEmptySuccess()
    {
        Assert.Equal("BOSTA_PICKUP_NOT_OWNED", BostaPickupSelections.Resolve(Credentials("FOREIGN"), new(true, [Pickup()], null)).ErrorCode);
        Assert.Equal("NETWORK_ERROR", BostaPickupSelections.Resolve(Credentials(), new(false, [], "NETWORK_ERROR")).ErrorCode);
    }

    [Fact]
    public void ReadinessProofIsBoundToTenantConfigurationAccountCredentialVersionEnvironmentAndMaterial()
    {
        var clock = new Clock(); using var cache = new BostaPickupVerificationCache(clock);
        var tenant = Guid.NewGuid(); var config = Guid.NewGuid(); var account = Guid.NewGuid(); var version = Guid.NewGuid(); var credentials = Credentials();
        var proof = cache.Observe(tenant, config, account, version, credentials, new(true, [Pickup()], null));
        Assert.NotNull(proof); Assert.DoesNotContain(credentials.ApiKey, JsonSerializer.Serialize(proof));
        Assert.NotNull(cache.Read(tenant, config, account, version, credentials));
        Assert.Null(cache.Read(Guid.NewGuid(), config, account, version, credentials));
        Assert.Null(cache.Read(tenant, Guid.NewGuid(), account, version, credentials));
        Assert.Null(cache.Read(tenant, config, Guid.NewGuid(), version, credentials));
        Assert.Null(cache.Read(tenant, config, account, Guid.NewGuid(), credentials));
        Assert.Null(cache.Read(tenant, config, account, version, new() { ApiKey = credentials.ApiKey, UseSandbox = true }));
        Assert.Null(cache.Read(tenant, config, account, version, new() { ApiKey = "ROTATED" }));
        Assert.Null(cache.Read(tenant, config, account, version, Credentials("EXPLICIT")));
        clock.Now = clock.Now.AddMinutes(10);
        Assert.Null(cache.Read(tenant, config, account, version, credentials));
    }

    [Fact]
    public void NewFailedVerificationRevokesProofAndAProcessRestartHasNoProof()
    {
        using var cache = new BostaPickupVerificationCache(); var tenant = Guid.NewGuid(); var config = Guid.NewGuid(); var account = Guid.NewGuid();
        var credentials = Credentials(); cache.Observe(tenant, config, account, null, credentials, new(true, [Pickup()], null));
        Assert.NotNull(cache.Read(tenant, config, account, null, credentials));
        Assert.Null(cache.Observe(tenant, config, account, null, credentials, new(false, [], "NETWORK_ERROR")));
        Assert.Null(cache.Read(tenant, config, account, null, credentials));
        using var restarted = new BostaPickupVerificationCache(); Assert.Null(restarted.Read(tenant, config, account, null, credentials));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
