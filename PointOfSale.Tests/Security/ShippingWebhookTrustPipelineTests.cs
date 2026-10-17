using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
    private readonly Mock<IOrderFulfillmentService> _fulfillmentService;
    private readonly TenantContext _tenantContext;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _shipmentId = Guid.NewGuid();
    private readonly DbContextOptions<ApplicationDbContext> _options;
    private readonly Mock<IShippingCarrierRegistry> _registry;
    private readonly Mock<ICarrierCredentialEncryptionService> _encryption;
    private readonly RecordingEventPublisher _publisher = new();

    public ShippingWebhookTrustPipelineTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"ShippingWebhookTrust_{Guid.NewGuid()}")
            .Options;
        _options = options;

        _tenantContext = new TenantContext();
        _tenantContext.SetTenantId(_tenantId);
        _db = new ApplicationDbContext(options, _tenantContext);
        SeedShipment();

        var adapter = new FakeShippingCarrierAdapter();
        var registry = new Mock<IShippingCarrierRegistry>(MockBehavior.Strict);
        _registry = registry;
        registry.Setup(x => x.GetAdapter(It.Is<string>(code => code == CarrierCode))).Returns(adapter);
        registry.Setup(x => x.IsCarrierSupported(It.Is<string>(code => code == CarrierCode))).Returns(true);
        registry.Setup(x => x.GetAllAdapters()).Returns([adapter]);
        registry.Setup(x => x.GetSupportedCarrierCodes()).Returns([CarrierCode]);
        _fulfillmentService = new Mock<IOrderFulfillmentService>(MockBehavior.Strict);
        _fulfillmentService
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
        _fulfillmentService
            .Setup(x => x.MarkDeliveryFailedAsync(
                It.IsAny<Guid>(),
                It.IsAny<FulfillmentRequestSource>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<bool>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid orderId, FulfillmentRequestSource source, string reason, int attemptNumber, bool canRetry, string? actor, string? idempotencyKey, CancellationToken ct) =>
                OrderFulfillmentResult.Succeeded(orderId, "TEST", FulfillmentStatus.OutForDelivery, FulfillmentStatus.FailedDelivery, source, idempotencyKey));

        var encryption = new Mock<ICarrierCredentialEncryptionService>(MockBehavior.Strict);
        _encryption = encryption;
        encryption.Setup(service => service.Decrypt(It.IsAny<string?>()))
            .Returns<string?>(value => value ?? string.Empty);
        _service = CreateService(_db);
    }

    private CarrierShipmentService CreateService(ApplicationDbContext db, bool claimsEnabled = false)
    {
        var authority = new CarrierIntegrationAuthority(
            db,
            _registry.Object,
            new CarrierCapabilityRegistry(_registry.Object),
            _encryption.Object,
            NullLogger<CarrierIntegrationAuthority>.Instance);

        return new CarrierShipmentService(
            db,
            _registry.Object,
            _encryption.Object,
            _publisher,
            _fulfillmentService.Object,
            new CommerceReliabilityTelemetry(),
            _tenantContext,
            Mock.Of<ICustomsEvidenceAdmissionService>(),
            Mock.Of<ICarrierGeographicMappingService>(),
            NullLogger<CarrierShipmentService>.Instance,
            authority, webhookClaimOptions: Options.Create(new ShippingWebhookClaimOptions { Enabled = claimsEnabled }));
    }

    [Fact]
    public async Task EnabledClaims_OnInMemory_FailsExplicitlyWithoutWeakOwnershipOrEffects()
    {
        var result = await CreateService(_db, true).ProcessWebhookAsync(CarrierCode,
            BuildPayload("evt_provider", "delivered"), WebhookSecret, null, null);
        Assert.False(result.Success);
        Assert.Equal("WEBHOOK_CLAIM_PROVIDER_UNSUPPORTED", result.ErrorCode);
        Assert.Empty(await _db.ShippingWebhookLogs.ToListAsync());
        Assert.Empty(await _db.ShippingWebhookClaims.ToListAsync());
        Assert.Equal(0, _publisher.Count);
    }

    [Fact]
    public async Task DisabledClaims_NewCompletion_CreatesNoOwnershipMetadata()
    {
        Assert.True((await Deliver(BuildPayload("evt_disabled", "delivered"))).Success);
        Assert.Empty(await _db.ShippingWebhookClaims.ToListAsync());
    }

    [Theory]
    [InlineData(WebhookProcessingStatus.Completed)]
    [InlineData(WebhookProcessingStatus.Skipped)]
    public async Task MatchingTerminalLegacyEvent_RemainsSuccessfulIdempotentDuplicate(WebhookProcessingStatus status)
    {
        var payload = BuildPayload("evt_terminal_legacy", "delivered");
        await SeedLog(payload, "evt_terminal_legacy", status, 1440);
        var result = await Deliver(payload);
        Assert.True(result.Success); Assert.True(result.IsDuplicate);
        Assert.Equal(0, _publisher.Count); Assert.Empty(_fulfillmentService.Invocations);
        Assert.Empty(await _db.ShippingWebhookClaims.ToListAsync());
    }

    [Theory]
    [InlineData(WebhookProcessingStatus.Completed)]
    [InlineData(WebhookProcessingStatus.Processing)]
    public async Task PayloadMatch_WithDifferentProviderIdentity_IsNeverAcknowledged(WebhookProcessingStatus status)
    {
        var payload = BuildPayload("evt_actual", "delivered");
        await SeedLog(payload, "evt_other", status, 0);
        var result = await Deliver(payload);
        Assert.False(result.Success);
        Assert.Equal("WEBHOOK_EVENT_IDENTITY_CONFLICT", result.ErrorCode);
        Assert.Equal(0, _publisher.Count);
        var helper = typeof(CarrierShipmentService).GetMethod("TryResolveShippingWebhookConflictAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var resolved = await (Task<WebhookProcessResult?>)helper.Invoke(_service,
            [_tenantId, CarrierCode, "evt_actual", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))), _shipmentId, CancellationToken.None])!;
        Assert.Equal("WEBHOOK_EVENT_IDENTITY_CONFLICT", resolved!.ErrorCode);
    }

    // These exercise the real generic handler with synthetic InMemory state, not SQL leases/locks.
    // Legacy timestamps cannot establish abandonment; the safe recovery boundary must refuse replay.
    [Theory]
    [InlineData(0)]
    [InlineData(1440)]
    public async Task ProcessingClaim_RecentOrApparentlyStale_IsNotAcknowledgedOrStolen(int ageMinutes)
    {
        var payload = BuildPayload("evt_processing", "delivered");
        var log = await SeedLog(payload, "evt_processing", WebhookProcessingStatus.Processing, ageMinutes);
        var before = JsonSerializer.Serialize(log);
        var result = await Deliver(payload);
        Assert.False(result.Success);
        Assert.Equal(503, result.StatusCode);
        Assert.Equal("WEBHOOK_PROCESSING_UNRESOLVED", result.ErrorCode);
        Assert.False(result.IsDuplicate);
        Assert.Equal(before, JsonSerializer.Serialize(await _db.ShippingWebhookLogs.AsNoTracking().SingleAsync()));
        Assert.Equal(ShipmentStatus.OutForDelivery, (await _db.Shipments.SingleAsync()).Status);
        Assert.Equal(0, _publisher.Count);
        Assert.Empty(_fulfillmentService.Invocations);
    }

    [Theory]
    [InlineData(WebhookProcessingStatus.Failed)]
    [InlineData(WebhookProcessingStatus.Pending)]
    public async Task UnfinishedLegacyRetry_PreservesIdentityAndAudit_WithoutReplayingPartialEffects(WebhookProcessingStatus status)
    {
        var payload = BuildPayload("evt_unfinished", "delivered");
        var log = await SeedLog(payload, "evt_unfinished", status, 1440);
        var before = JsonSerializer.Serialize(log);
        var result = await Deliver(payload);
        Assert.False(result.Success);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("WEBHOOK_RECONCILIATION_REQUIRED", result.ErrorCode);
        Assert.Equal(before, JsonSerializer.Serialize(await _db.ShippingWebhookLogs.AsNoTracking().SingleAsync()));
        Assert.Equal(0, _publisher.Count);
        Assert.Empty(_fulfillmentService.Invocations);
    }

    [Fact]
    public async Task ConcurrentDuplicate_DuringActiveWork_DoesNotExecuteEffectOrAcknowledgeCompletion()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fulfillmentService.Setup(x => x.SynchronizeFromShipmentAsync(It.IsAny<Guid>(), It.IsAny<FulfillmentStatus>(),
                It.IsAny<FulfillmentRequestSource>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(async (Guid orderId, FulfillmentStatus target, FulfillmentRequestSource source, Guid? shipment,
                string? tracking, string? actor, string? key, CancellationToken ct) =>
            {
                entered.TrySetResult();
                await release.Task;
                return OrderFulfillmentResult.Succeeded(orderId, "TEST", target, target, source, key);
            });
        var payload = BuildPayload("evt_concurrent", "delivered");
        var first = Deliver(payload);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var secondDb = new ApplicationDbContext(_options, _tenantContext);
            var duplicate = await CreateService(secondDb).ProcessWebhookAsync(CarrierCode, payload, WebhookSecret,
                "203.0.113.10", "test-suite");
            Assert.False(duplicate.Success);
            Assert.Equal(503, duplicate.StatusCode);
            Assert.Equal(0, _publisher.Count);
            Assert.Single(_fulfillmentService.Invocations);
        }
        finally { release.TrySetResult(); }
        Assert.True((await first).Success);
        var effectCount = _publisher.Count;
        Assert.True(effectCount > 0);
        Assert.True((await Deliver(payload)).IsDuplicate);
        Assert.Equal(effectCount, _publisher.Count);
        Assert.Single(_fulfillmentService.Invocations);
        Assert.Single(_db.ShippingWebhookLogs);
    }

    [Fact]
    public async Task FailureAfterSimulatedPartialPublish_RetryCannotDuplicateDownstreamEffects()
    {
        _publisher.FailAfterRecording = true;
        var payload = BuildPayload("evt_partial_failure", "delivered");
        Assert.False((await Deliver(payload)).Success);
        Assert.Equal(1, _publisher.Count);
        var log = await _db.ShippingWebhookLogs.AsNoTracking().SingleAsync();
        Assert.Equal(WebhookProcessingStatus.Failed, log.ProcessingStatus);
        var evidence = JsonSerializer.Serialize(log);
        _publisher.FailAfterRecording = false;
        var retry = await Deliver(payload);
        Assert.False(retry.Success);
        Assert.Equal("WEBHOOK_RECONCILIATION_REQUIRED", retry.ErrorCode);
        Assert.Equal(1, _publisher.Count);
        Assert.Single(_fulfillmentService.Invocations);
        Assert.Equal(evidence, JsonSerializer.Serialize(await _db.ShippingWebhookLogs.AsNoTracking().SingleAsync()));
        Assert.Single(_db.Shipments);
        Assert.Empty(_db.CarrierCollectionEvidence);
        Assert.Empty(_db.AccountingOutboxMessages);
    }

    [Theory]
    [InlineData(WebhookProcessingStatus.Processing, false, 503)]
    [InlineData(WebhookProcessingStatus.Failed, false, 409)]
    [InlineData(WebhookProcessingStatus.Completed, true, 200)]
    public async Task UniqueInsertConflict_OnlyAcknowledgesFinishedEvidence(WebhookProcessingStatus status, bool success, int http)
    {
        var payload = BuildPayload("evt_conflict", "delivered");
        var log = await SeedLog(payload, "evt_conflict", status, 0);
        var method = typeof(CarrierShipmentService).GetMethod("TryResolveShippingWebhookConflictAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var result = await (Task<WebhookProcessResult?>)method.Invoke(_service,
            [_tenantId, CarrierCode, log.EventId, log.PayloadHash, _shipmentId, CancellationToken.None])!;
        Assert.NotNull(result);
        Assert.Equal(success, result.Success);
        Assert.Equal(http, result.StatusCode);
        Assert.Equal(success, result.IsDuplicate);
        Assert.Equal(0, _publisher.Count);
    }

    [Fact]
    public async Task FailureWriter_AfterAmbiguousCommit_DoesNotDowngradeCompletedAudit()
    {
        var payload = BuildPayload("evt_commit", "delivered");
        var log = await SeedLog(payload, "evt_commit", WebhookProcessingStatus.Completed, 0);
        var before = JsonSerializer.Serialize(log);
        var method = typeof(CarrierShipmentService).GetMethod("PersistShippingWebhookFailureAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await (Task)method.Invoke(_service, [log, "simulated lost commit acknowledgement", 1L, CancellationToken.None])!;
        Assert.Equal(before, JsonSerializer.Serialize(await _db.ShippingWebhookLogs.AsNoTracking().SingleAsync()));
    }

    private Task<WebhookProcessResult> Deliver(string payload) => _service.ProcessWebhookAsync(
        CarrierCode, payload, WebhookSecret, "203.0.113.10", "test-suite");

    [Fact]
    public async Task RequestCancellationAfterClaim_PersistsBoundedFailureAuditWithoutReplaying()
    {
        using var request = new CancellationTokenSource();
        _fulfillmentService.Setup(x => x.SynchronizeFromShipmentAsync(It.IsAny<Guid>(), It.IsAny<FulfillmentStatus>(),
                It.IsAny<FulfillmentRequestSource>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns((Guid order, FulfillmentStatus status, FulfillmentRequestSource source, Guid? shipment,
                string? tracking, string? actor, string? key, CancellationToken ct) =>
            {
                request.Cancel();
                return Task.FromException<OrderFulfillmentResult>(new OperationCanceledException(ct));
            });
        var payload = BuildPayload("evt_cancelled_worker", "delivered");
        Assert.False((await _service.ProcessWebhookAsync(CarrierCode, payload, WebhookSecret,
            "203.0.113.10", "test-suite", request.Token)).Success);
        var log = await _db.ShippingWebhookLogs.AsNoTracking().SingleAsync();
        Assert.Equal(WebhookProcessingStatus.Failed, log.ProcessingStatus);
        Assert.NotNull(log.ProcessedAt);
        Assert.Equal("evt_cancelled_worker", log.EventId);
        Assert.Equal("WEBHOOK_RECONCILIATION_REQUIRED", (await Deliver(payload)).ErrorCode);
        Assert.Single(_fulfillmentService.Invocations);
        Assert.Equal(0, _publisher.Count);
    }

    [Fact]
    public async Task UniqueConflict_EventIntegrityTakesPrecedenceOverAnotherCompletedPayloadRow()
    {
        var payload = BuildPayload("evt_integrity", "delivered");
        var hashMatch = await SeedLog(payload, "different-event", WebhookProcessingStatus.Completed, 0);
        await SeedLog(BuildPayload("evt_integrity", "failed"), "evt_integrity", WebhookProcessingStatus.Processing, 0);
        var method = typeof(CarrierShipmentService).GetMethod("TryResolveShippingWebhookConflictAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var result = await (Task<WebhookProcessResult?>)method.Invoke(_service,
            [_tenantId, CarrierCode, "evt_integrity", hashMatch.PayloadHash, _shipmentId, CancellationToken.None])!;
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal(401, result.StatusCode);
        Assert.Equal("WEBHOOK_TAMPERED", result.ErrorCode);
        Assert.False(result.IsDuplicate);
        Assert.Equal(0, _publisher.Count);
        Assert.Empty(_fulfillmentService.Invocations);
    }

    private async Task<ShippingWebhookLog> SeedLog(string payload, string eventId, WebhookProcessingStatus status, int ageMinutes)
    {
        var log = new ShippingWebhookLog
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, CarrierCode = CarrierCode, EventId = eventId,
            PayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))),
            ShipmentId = _shipmentId, OrderId = (await _db.Shipments.SingleAsync()).OrderId,
            ProcessingStatus = status, CreatedAt = DateTime.UtcNow.AddMinutes(-ageMinutes),
            EventType = "original-event-type", PayloadSummary = "original redacted evidence", Signature = "sha256:original",
            SignatureVerified = true, ValidationMethod = "original-validation", ProcessingError = "original failure evidence"
        };
        _db.ShippingWebhookLogs.Add(log);
        await _db.SaveChangesAsync();
        return log;
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

    [Fact]
    public async Task ProcessWebhookAsync_CarrierFailure_ConvergesToFailedDeliveryWithEvidence()
    {
        var payload = BuildPayload("evt_failed", "failed");

        var result = await _service.ProcessWebhookAsync(
            CarrierCode,
            payload,
            ComputeSignature(payload, WebhookSecret),
            sourceIp: "203.0.113.10",
            userAgent: "test-suite",
            CancellationToken.None);

        Assert.True(result.Success, $"{result.ErrorCode}: {result.ErrorMessage}");
        Assert.True(result.StatusChanged);

        var shipment = await _db.Shipments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(x => x.Id == _shipmentId);
        Assert.Equal(ShipmentStatus.Failed, shipment.Status);
        Assert.Equal(1, shipment.DeliveryAttempts);
        Assert.Equal("failed", shipment.FailureReason);
        Assert.NotNull(shipment.FailedAtUtc);

        _fulfillmentService.Verify(x => x.MarkDeliveryFailedAsync(
            shipment.OrderId,
            FulfillmentRequestSource.Webhook,
            "failed",
            1,
            false,
            "system:webhook",
            It.Is<string>(key => key.EndsWith(":failed-delivery", StringComparison.Ordinal)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessWebhookAsync_PreDeliveryReturn_ConvergesToFailedDeliveryNotCustomerReturn()
    {
        var payload = BuildPayload("evt_returned_to_origin", "returned");

        var result = await _service.ProcessWebhookAsync(
            CarrierCode,
            payload,
            ComputeSignature(payload, WebhookSecret),
            sourceIp: "203.0.113.10",
            userAgent: "test-suite",
            CancellationToken.None);

        Assert.True(result.Success, $"{result.ErrorCode}: {result.ErrorMessage}");
        Assert.True(result.StatusChanged);

        var shipment = await _db.Shipments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(x => x.Id == _shipmentId);
        Assert.Equal(ShipmentStatus.Returned, shipment.Status);
        Assert.Equal("returned", shipment.ReturnReason);
        Assert.NotNull(shipment.ReturnedAtUtc);

        _fulfillmentService.Verify(x => x.MarkDeliveryFailedAsync(
            shipment.OrderId,
            FulfillmentRequestSource.Webhook,
            "returned",
            1,
            false,
            "system:webhook",
            It.Is<string>(key => key.EndsWith(":return-to-origin-failed-delivery", StringComparison.Ordinal)),
            It.IsAny<CancellationToken>()), Times.Once);
        _fulfillmentService.Verify(x => x.MarkReturnedAsync(
            It.IsAny<Guid>(),
            It.IsAny<FulfillmentRequestSource>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<decimal?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
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
            FulfillmentStatus = FulfillmentStatus.OutForDelivery,
            Status = OrderStatus.Shipped,
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
                        "failed" => ShipmentStatus.Failed,
                        "returned" => ShipmentStatus.Returned,
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

    private sealed class RecordingEventPublisher : IEventPublisher
    {
        public int Count { get; private set; }
        public bool FailAfterRecording { get; set; }
        public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default) where T : IntegrationEvent
        {
            Count++;
            if (FailAfterRecording) throw new InvalidOperationException("Synthetic partial publish failure");
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
