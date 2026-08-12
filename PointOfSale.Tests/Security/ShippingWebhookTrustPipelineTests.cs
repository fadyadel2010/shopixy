using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Customs;
using PointOfSale.Core.Interfaces.Fulfillment;
using PointOfSale.Core.Interfaces.Geographic;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.Tests.Security;

public sealed class ShippingWebhookTrustPipelineTests : IDisposable, IAsyncDisposable
{
    private const string CarrierCode = CarrierCodes.Bosta;
    private const string TrackingNumber = "MYL-TRACK-123";
    private const string WebhookSecret = "topsecret";

    private readonly ApplicationDbContext _db;
    private readonly CarrierShipmentService _service;
    private readonly TenantContext _tenantContext;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _shipmentId = Guid.NewGuid();

    public ShippingWebhookTrustPipelineTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"ShippingWebhookTrust_{Guid.NewGuid()}")
            .Options;

        _tenantContext = new TenantContext();
        _tenantContext.SetTenantId(_tenantId);
        _db = new ApplicationDbContext(options, _tenantContext);
        SeedShipment();

        var adapter = new FakeShippingCarrierAdapter();
        var registry = new Mock<IShippingCarrierRegistry>(MockBehavior.Strict);
        registry.Setup(x => x.GetAdapter(It.Is<string>(code => code == CarrierCode))).Returns(adapter);
        registry.Setup(x => x.IsCarrierSupported(It.Is<string>(code => code == CarrierCode))).Returns(true);
        registry.Setup(x => x.GetAllAdapters()).Returns([adapter]);
        registry.Setup(x => x.GetSupportedCarrierCodes()).Returns([CarrierCode]);
        var fulfillmentService = new Mock<IOrderFulfillmentService>(MockBehavior.Strict);
        fulfillmentService
            .Setup(x => x.SynchronizeFromShipmentAsync(
                It.IsAny<Guid>(),
                It.IsAny<FulfillmentStatus>(),
                It.IsAny<FulfillmentRequestSource>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid orderId, FulfillmentStatus targetStatus, FulfillmentRequestSource source, Guid? shipmentId, string? trackingNumber, string? actor, string? idempotencyKey, CancellationToken ct) =>
                OrderFulfillmentResult.Succeeded(orderId, "TEST", targetStatus, targetStatus, source, idempotencyKey));

        var encryption = new Mock<ICarrierCredentialEncryptionService>(MockBehavior.Strict);
        encryption.Setup(service => service.Decrypt(It.IsAny<string?>()))
            .Returns<string?>(value => value ?? string.Empty);
        var authority = new CarrierIntegrationAuthority(
            _db,
            registry.Object,
            new CarrierCapabilityRegistry(registry.Object),
            encryption.Object,
            NullLogger<CarrierIntegrationAuthority>.Instance);

        _service = new CarrierShipmentService(
            _db,
            registry.Object,
            encryption.Object,
            new NoOpEventPublisher(),
            fulfillmentService.Object,
            new CommerceReliabilityTelemetry(),
            _tenantContext,
            Mock.Of<ICustomsEvidenceAdmissionService>(),
            Mock.Of<ICarrierGeographicMappingService>(),
            NullLogger<CarrierShipmentService>.Instance,
            authority);
    }

    [Fact]
    public async Task ProcessWebhookAsync_MissingSignature_FailsClosed_AndDoesNotMutateShipment()
    {
        var payload = BuildPayload("evt_missing_sig", "delivered");

        var result = await _service.ProcessWebhookAsync(
            CarrierCode,
            payload,
            signature: null,
            sourceIp: "203.0.113.10",
            userAgent: "test-suite",
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(401, result.StatusCode);
        Assert.Equal("WEBHOOK_SIGNATURE_MISSING", result.ErrorCode);

        var shipment = await _db.Shipments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(x => x.Id == _shipmentId);
        Assert.Equal(ShipmentStatus.OutForDelivery, shipment.Status);
        Assert.Empty(_db.ShippingWebhookLogs.IgnoreQueryFilters());
    }

    [Fact]
    public async Task ProcessWebhookAsync_InvalidSignature_FailsClosed_AndDoesNotMutateShipment()
    {
        var payload = BuildPayload("evt_invalid_sig", "delivered");

        var result = await _service.ProcessWebhookAsync(
            CarrierCode,
            payload,
            signature: "bad-signature",
            sourceIp: "203.0.113.10",
            userAgent: "test-suite",
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(401, result.StatusCode);
        Assert.Equal("WEBHOOK_SIGNATURE_INVALID", result.ErrorCode);

        var shipment = await _db.Shipments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(x => x.Id == _shipmentId);
        Assert.Equal(ShipmentStatus.OutForDelivery, shipment.Status);
        Assert.Empty(_db.ShippingWebhookLogs.IgnoreQueryFilters());
    }

    [Fact]
    public async Task ProcessWebhookAsync_ValidSignature_ProcessesWebhook_AndPersistsCompletedLog()
    {
        var payload = BuildPayload("evt_valid", "delivered");
        var signature = ComputeSignature(payload, WebhookSecret);

        var result = await _service.ProcessWebhookAsync(
            CarrierCode,
            payload,
            signature,
            sourceIp: "203.0.113.10",
            userAgent: "test-suite",
            CancellationToken.None);

        Assert.True(result.Success, $"{result.ErrorCode}: {result.ErrorMessage}");
        Assert.True(result.StatusChanged);
        Assert.Equal(_shipmentId, result.ShipmentId);

        var shipment = await _db.Shipments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(x => x.Id == _shipmentId);
        Assert.Equal(ShipmentStatus.Delivered, shipment.Status);

        var webhookLog = await _db.ShippingWebhookLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal("evt_valid", webhookLog.EventId);
        Assert.Equal(CarrierCode, webhookLog.CarrierCode);
        Assert.True(webhookLog.SignatureVerified);
        Assert.NotEqual(WebhookSecret, webhookLog.Signature);
        Assert.StartsWith("sha256:", webhookLog.Signature, StringComparison.Ordinal);
        Assert.Equal(WebhookProcessingStatus.Completed, webhookLog.ProcessingStatus);
        Assert.Equal(_shipmentId, webhookLog.ShipmentId);
    }

    [Fact]
    public async Task ProcessWebhookAsync_DuplicateEvent_IsRejectedIdempotently()
    {
        var payload = BuildPayload("evt_duplicate", "delivered");
        var signature = ComputeSignature(payload, WebhookSecret);

        var firstResult = await _service.ProcessWebhookAsync(
            CarrierCode,
            payload,
            signature,
            sourceIp: "203.0.113.10",
            userAgent: "test-suite",
            CancellationToken.None);

        var secondResult = await _service.ProcessWebhookAsync(
            CarrierCode,
            payload,
            signature,
            sourceIp: "203.0.113.10",
            userAgent: "test-suite",
            CancellationToken.None);

        Assert.True(firstResult.Success, $"{firstResult.ErrorCode}: {firstResult.ErrorMessage}");
        Assert.True(secondResult.Success);
        Assert.True(secondResult.IsDuplicate);
        Assert.False(secondResult.StatusChanged);
        Assert.Equal(1, await _db.ShippingWebhookLogs.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task ProcessWebhookAsync_MixedCaseBearerPrefix_IsNormalizedAndAccepted()
    {
        var payload = BuildPayload("evt_uppercase_sig", "delivered");
        var bearerSignature = $"bEaReR {WebhookSecret}";

        var result = await _service.ProcessWebhookAsync(
            CarrierCode,
            payload,
            bearerSignature,
            sourceIp: "203.0.113.10",
            userAgent: "test-suite",
            CancellationToken.None);

        Assert.True(result.Success, $"{result.ErrorCode}: {result.ErrorMessage}");
        Assert.True(result.StatusChanged);
        Assert.Equal(_shipmentId, result.ShipmentId);

        var shipment = await _db.Shipments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(x => x.Id == _shipmentId);
        Assert.Equal(ShipmentStatus.Delivered, shipment.Status);

        var webhookLog = await _db.ShippingWebhookLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(x => x.EventId == "evt_uppercase_sig");
        Assert.True(webhookLog.SignatureVerified);
        Assert.Equal(WebhookProcessingStatus.Completed, webhookLog.ProcessingStatus);
    }

    private void SeedShipment()
    {
        var orderId = Guid.NewGuid();
        var order = new Order
        {
            Id = orderId,
            TenantId = _tenantId,
            OrderNumber = "ORD-1001",
            FulfillmentMode = FulfillmentMode.Carrier,
            FulfillmentCarrierCode = CarrierCode,
            ShippingAddress = "42 Test Street",
            ShippingCity = "Cairo",
            ShippingCountry = "EG",
            Currency = "EGP",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var shipment = new Shipment
        {
            Id = _shipmentId,
            TenantId = _tenantId,
            OrderId = orderId,
            Order = order,
            CarrierCode = CarrierCode,
            TrackingNumber = TrackingNumber,
            ShipmentNumber = "SHP-1001",
            Status = ShipmentStatus.OutForDelivery,
            Currency = "EGP",
            ShippingAddress = "42 Test Street",
            ShippingCity = "Cairo",
            ShippingCountry = "EG",
            RecipientName = "Ada Lovelace",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var carrierConfig = new CarrierConfiguration
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            CarrierCode = CarrierCode,
            DisplayName = "Bosta",
            IsEnabled = true,
            SupportsWebhooks = true,
            WebhookSecret = WebhookSecret,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Orders.Add(order);
        _db.Shipments.Add(shipment);
        _db.CarrierConfigurations.Add(carrierConfig);
        _db.SaveChanges();
    }

    private static string BuildPayload(string eventId, string status)
    {
        return JsonSerializer.Serialize(new
        {
            eventId,
            trackingNumber = TrackingNumber,
            status
        });
    }

    private static string ComputeSignature(string payload, string secret)
        => secret;

    public void Dispose()
    {
        _db.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        return _db.DisposeAsync();
    }

    private sealed class FakeShippingCarrierAdapter : IShippingCarrierAdapter
    {
        public string CarrierCode => CarrierCodes.Bosta;

        public string DisplayName => "Fake Bosta";

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
            try
            {
                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;

                if (!root.TryGetProperty("trackingNumber", out var trackingProperty) ||
                    !root.TryGetProperty("status", out var statusProperty))
                {
                    return null;
                }

                var trackingNumber = trackingProperty.GetString();
                var status = statusProperty.GetString();
                if (string.IsNullOrWhiteSpace(trackingNumber) || string.IsNullOrWhiteSpace(status))
                {
                    return null;
                }

                return new WebhookParseResult
                {
                    TrackingNumber = trackingNumber,
                    Status = status.ToLowerInvariant() switch
                    {
                        "delivered" => ShipmentStatus.Delivered,
                        "out_for_delivery" => ShipmentStatus.OutForDelivery,
                        "in_transit" => ShipmentStatus.InTransit,
                        _ => ShipmentStatus.ReadyForPickup
                    },
                    StatusDescription = status,
                    EventTimestamp = DateTime.UtcNow,
                    RawPayload = payload
                };
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    private sealed class NoOpEventPublisher : IEventPublisher
    {
        public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default) where T : IntegrationEvent
        {
            return Task.CompletedTask;
        }

        public Task PublishBatchAsync<T>(IEnumerable<T> events, CancellationToken cancellationToken = default) where T : IntegrationEvent
        {
            return Task.CompletedTask;
        }

        public Task PublishSensitiveAsync<T>(T @event, CancellationToken cancellationToken = default) where T : IntegrationEvent, ISensitiveIntegrationEvent
        {
            return Task.CompletedTask;
        }
    }
}
