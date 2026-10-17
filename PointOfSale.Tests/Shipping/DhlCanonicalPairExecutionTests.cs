using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Customs;
using PointOfSale.Core.Interfaces.Fulfillment;
using PointOfSale.Core.Interfaces.Geographic;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Adapters;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Shipping;

public sealed partial class WarehouseAddressAdmissionTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    public async Task DhlV2_ProductionQuoteCreateAndRecovery_UseCanonicalPairAndMeasuredParcelWithoutCrosswalk(bool timeout, bool inconsistentCounter, bool changedSecret)
    {
        // Offline qualification of wiring only. Synthetic catalog/account and
        // transport responses cannot activate real DHL or prove entitlement.
        var pair = await CreateCanonicalPairAsync("dhl", "PREPAID", "express");
        await using var db = Db();
        var s = await db.Shipments.SingleAsync();
        var o = await db.Orders.Include(o => o.Items).SingleAsync();
        db.OrderItems.Add(new OrderItem { Id = Guid.NewGuid(), OrderId = o.Id, ProductId = Guid.NewGuid(), Quantity = 1 });
        Assert.Single(o.Items);
        s.ParcelEvidenceJson = JsonSerializer.Serialize(ParcelMeasurements.Admit(new(30, 20, 10, "cm", 2500, "g", 1, "CANONICAL-PARCEL"), s, o, actor));
        await db.SaveChangesAsync();
        var config = Guid.NewGuid();
        var credentials = new CarrierCredentials { ApiKey = "synthetic", ApiSecret = "synthetic", AccountId = "synthetic-account", UseSandbox = true };
        var authority = new Mock<ICarrierIntegrationAuthority>(MockBehavior.Strict);
        authority.Setup(x => x.GetIntegrationReadinessAsync(tenant, "dhl", s.ShippingMethodId, o.WarehouseId,
                CarrierIntegrationPurpose.Quote, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarrierIntegrationReadiness("dhl", "Offline fake", false, true, true, "Sandbox", CarrierProductionCapability.Partial, [], true, true, "Available"));
        authority.Setup(x => x.GetServiceLevelReadinessAsync(tenant, "dhl", "express", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarrierServiceLevelReadiness("dhl", "express", true, true, true, true, []));
        authority.Setup(x => x.ResolveCredentialsAsync(tenant, "dhl", It.IsAny<CarrierIntegrationPurpose>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarrierCredentialsResolution(true, "dhl", credentials,
                JsonSerializer.Deserialize<CarrierIntegrationConfigurationSnapshot>($$"""{"Id":"{{config}}","CarrierCode":"dhl","DisplayName":"Synthetic DHL","IsEnabled":true,"UseSandbox":true}""")!, []));
        var handler = new CanonicalDhlHandler { TimeoutCreation = timeout };
        var factory = new Mock<IHttpClientFactory>(); factory.Setup(x => x.CreateClient("DHL")).Returns(new HttpClient(handler));
        var registry = new Mock<IShippingCarrierRegistry>(); registry.Setup(x => x.GetAdapter("dhl")).Returns(new DhlAdapter(factory.Object, NullLogger<DhlAdapter>.Instance));
        var events = new Mock<IEventPublisher>(); events.SetReturnsDefault(Task.CompletedTask);
        var fulfillment = new Mock<IOrderFulfillmentService>();
        fulfillment.Setup(x => x.SynchronizeFromShipmentAsync(It.IsAny<Guid>(), It.IsAny<FulfillmentStatus>(), It.IsAny<FulfillmentRequestSource>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, FulfillmentStatus status, FulfillmentRequestSource source, Guid? shipment, string? tracking, string? name, string? key, CancellationToken _) => OrderFulfillmentResult.Succeeded(id, "SYNTHETIC", status, status, source, key));
        using var telemetry = new CommerceReliabilityTelemetry();
        var mappings = new Mock<ICarrierGeographicMappingService>(MockBehavior.Strict);
        var encryption = new CarrierCredentialEncryptionService(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider(), NullLogger<CarrierCredentialEncryptionService>.Instance);
        var carrier = new CarrierShipmentService(db, registry.Object, encryption, events.Object, fulfillment.Object, telemetry,
            new TestTenantContext(tenant), Mock.Of<ICustomsEvidenceAdmissionService>(MockBehavior.Strict), mappings.Object, NullLogger<CarrierShipmentService>.Instance, authority.Object);
        var quote = await carrier.QuoteExistingShipmentAsync(tenant, s.Id);
        Assert.True(quote.Success, quote.ErrorCode);
        Assert.Equal("USD", Assert.Single(quote.Quotes).Currency);
        Assert.Equal(23m, quote.Quotes.Single().Cost);
        Assert.Null(s.CarrierRequestSnapshotJson);
        var result = await carrier.ExecuteCarrierForExistingShipmentAsync(tenant, s.Id, "untrusted-caller-service");
        Assert.Equal(!timeout, result.Success);
        Assert.Equal(1, handler.CreateCalls);
        db.ChangeTracker.Clear(); s = await db.Shipments.SingleAsync(); o = await db.Orders.Include(o => o.Items).SingleAsync();
        var retained = Assert.IsType<string>(s.CarrierRequestSnapshotJson);
        using var intent = JsonDocument.Parse(retained);
        Assert.Equal(2, intent.RootElement.GetProperty("Version").GetInt32());
        Assert.Equal(CanonicalShipmentAddresses.Restore(s, o, pair.Origin, pair.Destination).Fingerprint, intent.RootElement.GetProperty("CanonicalPairFingerprint").GetString());
        Assert.Equal(2.5m, intent.RootElement.GetProperty("Request").GetProperty("WeightKg").GetDecimal());
        Assert.Equal(1, s.ExternalRegistrationAttemptCount);
        Assert.Null(s.CarrierGeographicMappingSnapshotJson);
        // Simulated inconsistent legacy counter: retained intent/timestamp still
        // forbid repost. This changes only the process-local offline fixture.
        if (inconsistentCounter) s.ExternalRegistrationAttemptCount = 0;
        // Current warehouse/catalog data is not a reconstruction source after attempt.
        (await db.Warehouses.SingleAsync()).Address = "Later mutable warehouse";
        (await db.GeographicCountryProfiles.SingleAsync()).IsActive = false;
        await db.SaveChangesAsync();
        if (changedSecret) credentials.ApiSecret = "different-original-account-secret";
        var replay = await carrier.ExecuteCarrierForExistingShipmentAsync(tenant, s.Id, "express");
        Assert.Equal(!timeout && !changedSecret, replay.Success);
        if (changedSecret) Assert.Equal("DHL_REQUEST_IDENTITY_CHANGED_RECONCILIATION_REQUIRED", replay.ErrorCode);
        if (timeout) Assert.Equal("EXTERNAL_REGISTRATION_RECONCILIATION_REQUIRED", replay.ErrorCode);
        Assert.Equal(1, handler.CreateCalls);
        Assert.Equal(retained, (await db.Shipments.SingleAsync()).CarrierRequestSnapshotJson);
        var lockedQuote = await carrier.QuoteExistingShipmentAsync(tenant, s.Id);
        Assert.Equal("PARCEL_QUOTE_LOCKED", lockedQuote.ErrorCode);
        mappings.VerifyNoOtherCalls();
    }

    private sealed class CanonicalDhlHandler : HttpMessageHandler
    {
        public bool TimeoutCreation; public int CreateCalls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/rates"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"products":[{"productCode":"N","totalPrice":[{"currencyType":"BILLC","priceCurrency":"USD","price":23}]}]}""") });
            CreateCalls++;
            if (TimeoutCreation) throw new TaskCanceledException("synthetic uncertain outcome");
            var response = new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("""{"shipmentTrackingNumber":"1234567890","packages":[{"trackingNumber":"JD0123456789"}],"documents":[{"typeCode":"label","imageFormat":"PDF","content":"JVBERi0xLjcKJSVFT0Y="}]}""") };
            response.Headers.Add("Message-Reference", request.Headers.GetValues("Message-Reference").Single());
            return Task.FromResult(response);
        }
    }
}
