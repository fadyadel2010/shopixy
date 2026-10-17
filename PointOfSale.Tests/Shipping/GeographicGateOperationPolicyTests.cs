using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Geographic;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.Tests.Shipping;

// Characterize the existing operation-specific policy. No carrier transport,
// relational database, catalog publication, or account acceptance is exercised.
public sealed class GeographicGateOperationPolicyTests
{
    [Theory]
    [InlineData("bosta", CarrierIntegrationPurpose.Quote, false)]
    [InlineData("bosta", CarrierIntegrationPurpose.ShipmentCreation, false)]
    [InlineData("bosta", CarrierIntegrationPurpose.ExternalRegistration, false)]
    [InlineData("bosta", CarrierIntegrationPurpose.Tracking, false)]
    [InlineData("aramex", CarrierIntegrationPurpose.Quote, false)]
    [InlineData("aramex", CarrierIntegrationPurpose.ShipmentCreation, true)]
    [InlineData("aramex", CarrierIntegrationPurpose.ExternalRegistration, true)]
    [InlineData("aramex", CarrierIntegrationPurpose.Tracking, false)]
    [InlineData("dhl", CarrierIntegrationPurpose.Quote, false)]
    [InlineData("dhl", CarrierIntegrationPurpose.ShipmentCreation, false)]
    [InlineData("dhl", CarrierIntegrationPurpose.ExternalRegistration, false)]
    [InlineData("dhl", CarrierIntegrationPurpose.Tracking, false)]
    [InlineData("mylerz", CarrierIntegrationPurpose.Quote, false)]
    [InlineData("mylerz", CarrierIntegrationPurpose.ShipmentCreation, false)]
    [InlineData("mylerz", CarrierIntegrationPurpose.ExternalRegistration, false)]
    [InlineData("mylerz", CarrierIntegrationPurpose.Tracking, false)]
    public async Task MissingConfiguration_SeparatesTopLevelMappingPolicyFromIndependentBlockers(
        string carrier, CarrierIntegrationPurpose purpose, bool mappingBlocked)
    {
        await using var fixture = new Fixture(carrier);
        var readiness = await fixture.Authority.GetIntegrationReadinessAsync(
            fixture.TenantId, carrier, purpose: purpose);

        Assert.Equal(carrier != "bosta", fixture.Capabilities.GetCapability(carrier)!.RequiresGeographyMapping);
        Assert.Equal(mappingBlocked, readiness.Blockers.Contains("CARRIER_GEOGRAPHY_MAPPING_UNPUBLISHED"));
        Assert.Contains("CARRIER_CONFIGURATION_MISSING", readiness.Blockers);
        Assert.False(readiness.CanExecute);
        if (carrier == "dhl" && purpose != CarrierIntegrationPurpose.Tracking)
            Assert.Contains("DHL_REMOTE_ACCOUNT_CERTIFICATION_REQUIRED", readiness.Blockers);
        if (carrier == "mylerz")
        {
            Assert.Contains(PointOfSale.Infrastructure.Shipping.Adapters.MylerzAdapter.ContractUnavailableErrorCode, readiness.Blockers);
            Assert.Contains("CARRIER_UNAVAILABLE", readiness.Blockers);
        }
        Assert.Empty(fixture.Db.ChangeTracker.Entries());
        fixture.VerifyNoProviderCalls();
    }

    [Theory]
    [InlineData("bosta")]
    [InlineData("aramex")]
    [InlineData("dhl")]
    public async Task TrackingCredentialResolution_DoesNotAcquireNewGeographyOrAuthorizeNewRegistration(string carrier)
    {
        await using var fixture = new Fixture(carrier);
        fixture.Db.CarrierConfigurations.Add(new CarrierConfiguration
        {
            Id = Guid.NewGuid(), TenantId = fixture.TenantId, CarrierCode = carrier,
            DisplayName = carrier, IsEnabled = true, ApiKey = "offline-key"
        });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var tracking = await fixture.Authority.ResolveCredentialsAsync(fixture.TenantId, carrier, CarrierIntegrationPurpose.Tracking);
        var fresh = await fixture.Authority.ResolveCredentialsAsync(fixture.TenantId, carrier, CarrierIntegrationPurpose.ExternalRegistration);
        var substituted = await fixture.Authority.ResolveCredentialsAsync(Guid.NewGuid(), carrier, CarrierIntegrationPurpose.Tracking);

        Assert.True(tracking.Succeeded); // Credential resolution only, not tracking success.
        Assert.DoesNotContain("CARRIER_GEOGRAPHY_MAPPING_UNPUBLISHED", tracking.Blockers);
        Assert.False(fresh.Succeeded);
        Assert.False(substituted.Succeeded);
        Assert.Contains("CARRIER_CONFIGURATION_MISSING", substituted.Blockers);
        Assert.Empty(fixture.Db.CarrierGeographicMappingReleases);
        Assert.Empty(fixture.Db.Set<GeographicAudit>());
        Assert.All(fixture.Db.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
        fixture.VerifyNoProviderCalls();
    }

    [Fact]
    public async Task MylerzLegacyConfiguration_DoesNotBorrowMappingIndependentTrackingAdmission()
    {
        await using var fixture = new Fixture("mylerz");
        fixture.Db.CarrierConfigurations.Add(new CarrierConfiguration
        {
            Id = Guid.NewGuid(), TenantId = fixture.TenantId, CarrierCode = "mylerz",
            DisplayName = "Mylerz", IsEnabled = true, ApiKey = "offline-legacy-key", SupportsTracking = true
        });
        await fixture.Db.SaveChangesAsync();
        var result = await fixture.Authority.ResolveCredentialsAsync(fixture.TenantId, "mylerz", CarrierIntegrationPurpose.Tracking);
        Assert.False(result.Succeeded);
        Assert.Null(result.Credentials);
        Assert.Contains(PointOfSale.Infrastructure.Shipping.Adapters.MylerzAdapter.ContractUnavailableErrorCode, result.Blockers);
        fixture.VerifyNoProviderCalls();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public ApplicationDbContext Db { get; }
        public Mock<IShippingCarrierAdapter> Adapter { get; }
        public CarrierCapabilityRegistry Capabilities { get; }
        public CarrierIntegrationAuthority Authority { get; }

        public Fixture(string carrier)
        {
            Db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"GeographicGatePolicy_{Guid.NewGuid():N}").Options);
            Adapter = new Mock<IShippingCarrierAdapter>(MockBehavior.Strict);
            Adapter.SetupGet(a => a.DisplayName).Returns(carrier);
            Adapter.Setup(a => a.GetSupportedServiceLevels()).Returns(Array.Empty<ServiceLevelInfo>());
            var registry = new Mock<IShippingCarrierRegistry>(MockBehavior.Strict);
            registry.Setup(r => r.GetAdapter(carrier)).Returns(Adapter.Object);
            Capabilities = new CarrierCapabilityRegistry(registry.Object);
            var encryption = new Mock<ICarrierCredentialEncryptionService>(MockBehavior.Strict);
            encryption.Setup(e => e.Decrypt(It.IsAny<string?>())).Returns((string? value) => value ?? string.Empty);
            Authority = new CarrierIntegrationAuthority(Db, registry.Object, Capabilities, encryption.Object,
                NullLogger<CarrierIntegrationAuthority>.Instance);
        }

        public void VerifyNoProviderCalls()
        {
            Adapter.VerifyGet(a => a.DisplayName, Times.AtLeastOnce());
            Adapter.Verify(a => a.GetSupportedServiceLevels(), Times.AtLeastOnce());
            Adapter.VerifyNoOtherCalls();
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
