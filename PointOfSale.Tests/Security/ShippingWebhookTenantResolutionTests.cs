using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Services.Shipping;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.Tests.Security;

public sealed class ShippingWebhookTenantResolutionTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly TenantContext _tenantContext = new();

    public ShippingWebhookTenantResolutionTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"ShippingTenantResolution_{Guid.NewGuid():N}")
            .Options;

        _db = new ApplicationDbContext(options, _tenantContext);
    }

    [Fact]
    public async Task PrepareAsync_OnSharedHost_ResolvesTenantFromTrackingAndSetsTenantContext()
    {
        var targetTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var targetShipmentId = Guid.NewGuid();
        SeedShipment(targetTenantId, targetShipmentId, "TRACK-TARGET");
        SeedShipment(otherTenantId, Guid.NewGuid(), "TRACK-OTHER");

        var adapter = new FakeShippingCarrierAdapter();
        var registry = new Mock<IShippingCarrierRegistry>();
        registry
            .Setup(x => x.GetAdapter(CarrierCodes.Mylerz))
            .Returns(adapter);

        var resolver = new ShippingWebhookTenantResolver(
            _db,
            registry.Object,
            NullLogger<ShippingWebhookTenantResolver>.Instance);
        var pipeline = new ShippingWebhookRequestPipeline(
            resolver,
            _tenantContext,
            new CommerceReliabilityTelemetry(),
            NullLogger<ShippingWebhookRequestPipeline>.Instance);

        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "trace-shared-host"
        };
        var payload = JsonSerializer.Serialize(new
        {
            eventId = "evt-shared-host",
            trackingNumber = "TRACK-TARGET",
            status = "delivered"
        });

        var context = await pipeline.PrepareAsync(
            httpContext,
            CarrierCodes.Mylerz,
            payload,
            signature: "test-signature");

        Assert.Equal(targetTenantId, context.TenantId);
        Assert.Equal(targetShipmentId, context.Resolution.ShipmentId);
        Assert.Equal("TRACK-TARGET", context.Resolution.TrackingNumber);
        Assert.Equal(targetTenantId, _tenantContext.TenantId);
        Assert.Equal("tracking_number", context.Resolution.ResolutionSource);
    }

    private void SeedShipment(Guid tenantId, Guid shipmentId, string trackingNumber)
    {
        var orderId = Guid.NewGuid();
        _db.Orders.Add(new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = $"ORD-{trackingNumber}",
            FulfillmentMode = FulfillmentMode.Carrier,
            FulfillmentCarrierCode = CarrierCodes.Mylerz,
            ShippingAddress = "42 Test Street",
            ShippingCity = "Cairo",
            ShippingCountry = "EG",
            Currency = "EGP",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.Shipments.Add(new Shipment
        {
            Id = shipmentId,
            TenantId = tenantId,
            OrderId = orderId,
            CarrierCode = CarrierCodes.Mylerz,
            TrackingNumber = trackingNumber,
            ShipmentNumber = $"SHP-{trackingNumber}",
            Status = ShipmentStatus.ReadyForPickup,
            Currency = "EGP",
            ShippingAddress = "42 Test Street",
            ShippingCity = "Cairo",
            ShippingCountry = "EG",
            RecipientName = "Grace Hopper",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private sealed class FakeShippingCarrierAdapter : IShippingCarrierAdapter
    {
        public string CarrierCode => CarrierCodes.Mylerz;

        public string DisplayName => "Fake Mylerz";

        public Task<ShippingQuoteResult> GetQuoteAsync(ShippingQuoteRequest request, CarrierCredentials credentials, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CreateShipmentResult> CreateShipmentAsync(CreateShipmentRequest request, CarrierCredentials credentials, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CancelShipmentResult> CancelShipmentAsync(string trackingNumber, CarrierCredentials credentials, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ShipmentTrackingResult> GetTrackingAsync(string trackingNumber, CarrierCredentials credentials, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CarrierValidationResult> ValidateCredentialsAsync(CarrierCredentials credentials, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public IReadOnlyList<ServiceLevelInfo> GetSupportedServiceLevels() => [];

        public WebhookParseResult? ParseWebhook(string payload, string? signature, string? webhookSecret)
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (!root.TryGetProperty("trackingNumber", out var trackingProperty) ||
                !root.TryGetProperty("status", out var statusProperty))
            {
                return null;
            }

            var trackingNumber = trackingProperty.GetString();
            if (string.IsNullOrWhiteSpace(trackingNumber))
            {
                return null;
            }

            return new WebhookParseResult
            {
                TrackingNumber = trackingNumber,
                Status = statusProperty.GetString() == "delivered"
                    ? ShipmentStatus.Delivered
                    : ShipmentStatus.ReadyForPickup,
                RawPayload = payload
            };
        }
    }
}
