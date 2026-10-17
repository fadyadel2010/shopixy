using MassTransit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.Consumers;
using PointOfSale.API.Services.Shipping;
using PointOfSale.API.Services.Pricing;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.DTOs.ShippingAdministration;
using PointOfSale.Core.Entities.ShippingAdministration;
using PointOfSale.Core.Interfaces.ShippingAdministration;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Customs;
using PointOfSale.Core.Interfaces.Fulfillment;
using PointOfSale.Core.Interfaces.Geographic;
using PointOfSale.Infrastructure.Consumers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Adapters;
using PointOfSale.Infrastructure.Shipping.Administration;
using IConnectionMultiplexer = StackExchange.Redis.IConnectionMultiplexer;

namespace PointOfSale.Tests.Shipping;

public sealed class MylerzContainmentRuntimeTests
{
    public static IEnumerable<object[]> PurposesAndCredentials() =>
        from purpose in Enum.GetValues<CarrierIntegrationPurpose>()
        from configured in new[] { false, true }
        from sandbox in new[] { false, true }
        select new object[] { purpose, configured, sandbox };

    [Theory]
    [MemberData(nameof(PurposesAndCredentials))]
    public async Task EveryPurpose_DeniesUnavailableCredentialsAndUnverifiedEnvironment(
        CarrierIntegrationPurpose purpose, bool configured, bool sandbox)
    {
        await using var fixture = new Fixture(configured, sandbox);
        var readiness = await fixture.Authority.GetIntegrationReadinessAsync(
            fixture.TenantId, CarrierCodes.Mylerz, purpose: purpose);
        var resolution = await fixture.Authority.ResolveCredentialsAsync(
            fixture.TenantId, CarrierCodes.Mylerz, purpose);

        Assert.False(readiness.CanExecute);
        Assert.Equal(CarrierProductionCapability.Unavailable, readiness.ProductionCapability);
        Assert.Equal("Unverified", readiness.CredentialAvailability);
        Assert.Null(readiness.Environment);
        Assert.Contains(MylerzAdapter.ContractUnavailableErrorCode, readiness.Blockers);
        Assert.False(resolution.Succeeded);
        Assert.Null(resolution.Credentials);
        Assert.Contains(MylerzAdapter.ContractUnavailableErrorCode, resolution.Blockers);
        Assert.False(resolution.Configuration!.SupportsCod);
        Assert.False(resolution.Configuration.SupportsTracking);
        Assert.False(resolution.Configuration.SupportsLabels);
        Assert.False(resolution.Configuration.SupportsQuotes);
        Assert.False(resolution.Configuration.SupportsWebhooks);
        Assert.False(resolution.Configuration.SupportsCancellation);
        fixture.Factory.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ValidationServicePolicyWebhookTrustAndFixedQuote_CannotActivateMylerz()
    {
        await using var fixture = new Fixture();
        var validation = await fixture.Authority.ValidateResolvedCredentialsAsync(fixture.TenantId, CarrierCodes.Mylerz);
        Assert.False(validation.Success);
        Assert.Equal("MYLERZ_ENVIRONMENT_QUALIFICATION_REQUIRED", validation.ErrorCode);
        Assert.Empty(await fixture.Authority.ListRuntimeConfigurationsAsync(fixture.TenantId, CarrierIntegrationPurpose.Quote));
        var service = await fixture.Authority.GetServiceLevelReadinessAsync(fixture.TenantId, CarrierCodes.Mylerz, "standard");
        Assert.False(service.CanExecute);
        Assert.Contains("SERVICE_LEVEL_UNSUPPORTED", service.Blockers);
        var trust = await fixture.Authority.ResolveWebhookTrustAsync(fixture.TenantId, CarrierCodes.Mylerz);
        Assert.False(trust.Succeeded);
        Assert.False(trust.SupportsWebhooks);
        Assert.Equal(CarrierWebhookValidationMode.Unsupported, trust.ValidationMode);
        Assert.Null(trust.WebhookSecret);

        var quotes = new ShippingQuoteService(fixture.Registry, fixture.Authority,
            Mock.Of<IConnectionMultiplexer>(MockBehavior.Strict), NullLogger<ShippingQuoteService>.Instance);
        var result = await quotes.GetQuotesAsync(fixture.TenantId, new GetShippingQuotesRequest
        {
            DestinationCity = "Cairo", DestinationCountry = "EG", IsCashOnDelivery = true, OrderSubtotal = 200
        });
        Assert.True(result.Success); // Successful empty catalog is not a provider quote.
        Assert.Empty(result.Options);
        fixture.Factory.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OtherTenant_CannotResolveConfigurationOrSecrets()
    {
        await using var fixture = new Fixture();
        var result = await fixture.Authority.ResolveCredentialsAsync(Guid.NewGuid(), CarrierCodes.Mylerz, CarrierIntegrationPurpose.Tracking);
        Assert.False(result.Succeeded);
        Assert.Null(result.Configuration);
        Assert.Null(result.Credentials);
        Assert.Contains("CARRIER_CONFIGURATION_MISSING", result.Blockers);
        fixture.Factory.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AdminProjectionAndCatalog_DoNotAdvertiseLegacyStoredSupport()
    {
        await using var fixture = new Fixture();
        var definition = new MylerzCarrierDefinition();
        Assert.False(definition.SupportsCod);
        Assert.False(definition.SupportsPrepaid);
        Assert.False(definition.SupportsTracking);
        Assert.False(definition.SupportsWebhooks);
        Assert.False(definition.SupportsLabels);
        Assert.False(definition.SupportsQuotes);
        Assert.False(definition.SupportsCancellation);
        Assert.False(definition.SupportsPickupScheduling);
        Assert.Empty(definition.SupportedCountries);
        Assert.Null(definition.LogoUrl);
        var capability = fixture.Capabilities.GetCapability(CarrierCodes.Mylerz)!;
        Assert.False(capability.EnvironmentSupported);
        Assert.True(capability.ConnectionTestSupported);
        Assert.False(capability.SupportsWebhooks);
        Assert.Equal(2, capability.CredentialFields.Count);
        Assert.All(capability.CredentialFields.Where(f => f.Target is "ApiKey" or "ApiSecret"), f => Assert.True(f.IsSecret));
        Assert.Empty(capability.SupportedServiceLevels);

        var controller = new AdminCarrierConfigController(fixture.Db, fixture.Registry, fixture.Capabilities,
            fixture.Authority, fixture.TenantContext, NullLogger<AdminCarrierConfigController>.Instance);
        var response = await controller.GetConfiguration(CarrierCodes.Mylerz, CancellationToken.None);
        var dto = Assert.IsType<CarrierConfigurationDto>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Equal("Unavailable", dto.ProductionCapability);
        Assert.False(dto.SupportsCod);
        Assert.False(dto.SupportsTracking);
        Assert.False(dto.SupportsWebhooks);
        Assert.False(dto.SupportsLabels);
        Assert.Empty(dto.ServiceLevels);
        Assert.IsType<BadRequestObjectResult>((await controller.ToggleEnabled(CarrierCodes.Mylerz, CancellationToken.None)).Result);
        var validation = Assert.IsType<ValidateCredentialsResponseDto>(Assert.IsType<OkObjectResult>(
            (await controller.ValidateCredentials(CarrierCodes.Mylerz, CancellationToken.None)).Result).Value);
        Assert.False(validation.Success);
        // Stored legacy settings remain available for investigation, not execution.
        Assert.True((await fixture.Db.CarrierConfigurations.SingleAsync()).SupportsTracking);
        fixture.Factory.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("attempt")]
    [InlineData("timestamp")]
    [InlineData("snapshot")]
    [InlineData("fingerprint")]
    [InlineData("response")]
    [InlineData("mixed-case")]
    [InlineData("identity")]
    [InlineData("completed")]
    [InlineData("unresolved")]
    public async Task SharedRegistrationAndConsumerRedelivery_PreserveUncertainEvidenceWithoutRecreation(string evidence)
    {
        await using var fixture = new Fixture();
        var shipment = await fixture.SeedShipmentAsync();
        var attemptedAt = DateTime.UtcNow.AddHours(-1);
        shipment.CarrierRequestId = $"ship-{fixture.TenantId:N}-{shipment.Id:N}";
        shipment.CarrierRequestSnapshotJson = evidence == "snapshot" ? "{\"request\":\"original\"}" : null;
        shipment.CarrierRequestFingerprint = evidence == "fingerprint" ? "original-fingerprint" : null;
        shipment.ExternalRegistrationAttemptCount = evidence is "attempt" or "mixed-case" ? 1 : 0;
        shipment.CarrierCreateResponse = evidence == "response" ? "{\"IsErrorState\":false,\"Value\":{}}" : null;
        if (evidence == "mixed-case") shipment.CarrierCode = "MYLERZ";
        shipment.ExternalRegistrationLastAttemptAtUtc = evidence == "timestamp" ? attemptedAt : null;
        shipment.ExternalRegistrationCompletedAtUtc = evidence == "completed" ? attemptedAt : null;
        shipment.TrackingNumber = evidence == "identity" ? "12345678901234" : null;
        shipment.ExternalRegistrationState = evidence == "unresolved" ? "ReconciliationRequired" : "PendingExternalCall";
        await fixture.Db.SaveChangesAsync();
        var original = (shipment.CarrierRequestId, shipment.CarrierRequestSnapshotJson, shipment.CarrierRequestFingerprint,
            shipment.ExternalRegistrationAttemptCount, shipment.ExternalRegistrationLastAttemptAtUtc,
            shipment.ExternalRegistrationCompletedAtUtc, shipment.TrackingNumber, shipment.ExternalRegistrationFailedAtUtc,
            shipment.CarrierCode, shipment.CarrierCreateResponse);
        fixture.Db.ChangeTracker.Clear();

        var result = await fixture.Shipments.ExecuteCarrierForExistingShipmentAsync(fixture.TenantId, shipment.Id, "invented-service");
        Assert.False(result.Success);
        Assert.Equal("EXTERNAL_REGISTRATION_RECONCILIATION_REQUIRED", result.ErrorCode);
        Assert.True(result.ExternalCallSkipped);
        Assert.False(result.ExternalCallAttempted);
        Assert.True(result.Reconciled); // Means reconciliation was attempted, not successful.

        var consumer = new ShipmentExternalRegistrationRequestedConsumer(fixture.Shipments, fixture.Telemetry,
            NullLogger<ShipmentExternalRegistrationRequestedConsumer>.Instance);
        var context = new Mock<ConsumeContext<ShipmentExternalRegistrationRequestedEvent>>();
        context.SetupGet(c => c.Message).Returns(new ShipmentExternalRegistrationRequestedEvent
        {
            TenantId = fixture.TenantId, ShipmentId = shipment.Id, OrderId = shipment.OrderId,
            CarrierCode = CarrierCodes.Mylerz, ServiceLevel = "standard"
        });
        await consumer.Consume(context.Object);
        fixture.Db.ChangeTracker.Clear();
        var persisted = await fixture.Db.Shipments.SingleAsync();
        Assert.Equal("ReconciliationRequired", persisted.ExternalRegistrationState);
        Assert.StartsWith("EXTERNAL_REGISTRATION_RECONCILIATION_REQUIRED:", persisted.ExternalRegistrationError);
        Assert.Equal(original, (persisted.CarrierRequestId, persisted.CarrierRequestSnapshotJson, persisted.CarrierRequestFingerprint,
            persisted.ExternalRegistrationAttemptCount, persisted.ExternalRegistrationLastAttemptAtUtc,
            persisted.ExternalRegistrationCompletedAtUtc, persisted.TrackingNumber, persisted.ExternalRegistrationFailedAtUtc,
            persisted.CarrierCode, persisted.CarrierCreateResponse));
        Assert.Equal(ShipmentStatus.PendingExternalRegistration, persisted.Status);
        Assert.Null((await fixture.Db.Orders.SingleAsync()).TrackingNumber);
        fixture.Factory.VerifyNoOtherCalls();
        fixture.Publisher.VerifyNoOtherCalls();
        fixture.Fulfillment.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdminShippingMethodWrite_DoesNotInventStandardServiceForEmptyMylerzCatalog(bool active)
    {
        await using var fixture = new Fixture();
        var controller = new ShippingMethodsController(fixture.Db, fixture.TenantContext, fixture.Publisher.Object,
            fixture.Registry, fixture.Authority, Mock.Of<IShippingCalculationService>(MockBehavior.Strict), fixture.Telemetry,
            NullLogger<ShippingMethodsController>.Instance);
        var response = await controller.CreateShippingMethod(new CreateShippingMethodDto
        {
            Name = "Uncertified carrier", CarrierCode = CarrierCodes.Mylerz, ServiceLevel = "standard",
            BaseRate = 25, PerKgRate = 1, EstimatedDeliveryDaysMin = 1, EstimatedDeliveryDaysMax = 3, IsActive = active
        });
        Assert.IsType<BadRequestObjectResult>(response.Result);
        Assert.Empty(fixture.Db.ShippingMethods);
        fixture.Factory.VerifyNoOtherCalls();
        fixture.Publisher.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("mylerz", CarrierAccountState.ConnectionTestPending)]
    [InlineData("mylerz", CarrierAccountState.ReadyForActivation)]
    [InlineData("MYLERZ", CarrierAccountState.Suspended)]
    public async Task LegacyAccountCannotActivateFromCallerAttestedReadiness(string code, CarrierAccountState state)
    {
        await using var fixture = new Fixture();
        var account = new CarrierAccount
        {
            Id = Guid.NewGuid(), TenantId = fixture.TenantId, CarrierCode = code, State = state,
            OperationalState = CarrierOperationalState.NotReady, HealthState = CarrierAccountHealthState.Blocked
        };
        fixture.Db.CarrierAccounts.Add(account);
        await fixture.Db.SaveChangesAsync();
        var runtime = new Mock<ICarrierAccountAdministrationRuntime>(MockBehavior.Strict);
        var service = new ShippingAdministrationApiService(fixture.Db, runtime.Object, fixture.Registry, fixture.Capabilities, fixture.Authority);

        var result = await service.ActivateAsync(fixture.TenantId, account.Id,
            new ReadinessEvidenceCommand(true, true), new CarrierAdministrationActor("synthetic-admin"), "test-only", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(MylerzAdapter.ContractUnavailableErrorCode, result.FailureCode);
        Assert.Equal(state, account.State);
        Assert.Null(account.ActivatedAtUtc);
        Assert.Empty(fixture.Db.CarrierAdministrationAudit);
        runtime.VerifyNoOtherCalls();
        fixture.Factory.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DraftAccountCreation_IsAvailableButUnqualifiedSandboxValidationIsRefused()
    {
        await using var fixture = new Fixture();
        var runtime = new Mock<ICarrierAccountAdministrationRuntime>(MockBehavior.Strict);
        runtime.Setup(r => r.RegisterCarrierAccountAsync(fixture.TenantId, "mylerz", It.IsAny<CarrierAdministrationActor>(), "test-only", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CarrierAccountRuntimeResult.Success(Guid.NewGuid(), CarrierAccountState.Draft, CarrierAccountHealthState.Blocked));
        var service = new ShippingAdministrationApiService(fixture.Db, runtime.Object, fixture.Registry, fixture.Capabilities, fixture.Authority);
        var actor = new CarrierAdministrationActor("synthetic-admin");
        var created = await service.CreateCarrierAccountAsync(fixture.TenantId, new CreateCarrierAccountCommand("mylerz"), actor, "test-only", CancellationToken.None);
        Assert.True(created.Succeeded);
        Assert.Equal("Draft", created.State);
        Assert.Empty(fixture.Db.CarrierAccounts);

        var account = new CarrierAccount { Id = Guid.NewGuid(), TenantId = fixture.TenantId, CarrierCode = "mylerz", State = CarrierAccountState.Draft };
        fixture.Db.CarrierAccounts.Add(account);
        await fixture.Db.SaveChangesAsync();
        var validation = await service.RunConnectionTestAsync(fixture.TenantId, account.Id,
            new ConnectionTestCommand("synthetic-input", null, null, null, true, null), actor, "test-only", CancellationToken.None);
        Assert.False(validation.Succeeded);
        Assert.Equal("MYLERZ_ENVIRONMENT_QUALIFICATION_REQUIRED", validation.FailureCode);
        Assert.Empty(fixture.Db.CarrierConnectionTestRecords);
        Assert.Empty(fixture.Db.CarrierAdministrationAudit);
        runtime.VerifyAll();
        runtime.Verify(r => r.RegisterCarrierAccountAsync(fixture.TenantId, "mylerz", actor, "test-only", It.IsAny<CancellationToken>()), Times.Once);
        fixture.Factory.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DirectAccountRuntimeCannotOverrideMylerzContractWithReadinessAttestation()
    {
        await using var fixture = new Fixture();
        var account = new CarrierAccount { Id = Guid.NewGuid(), TenantId = fixture.TenantId,
            CarrierCode = "mylerz", State = CarrierAccountState.ReadyForActivation };
        fixture.Db.CarrierAccounts.Add(account);
        await fixture.Db.SaveChangesAsync();
        var runtime = new CarrierAccountAdministrationRuntime(fixture.Db, fixture.Registry,
            NullLogger<CarrierAccountAdministrationRuntime>.Instance);
        var result = await runtime.TransitionStateAsync(fixture.TenantId, account.Id, CarrierAccountState.Active,
            new CarrierReadinessEvidence(true, true), new CarrierAdministrationActor("synthetic-admin"), "test-only");
        Assert.False(result.Succeeded);
        Assert.Contains(MylerzAdapter.ContractUnavailableErrorCode, result.Blockers);
        Assert.Equal(CarrierAccountState.ReadyForActivation, account.State);
        Assert.Null(account.ActivatedAtUtc);
        fixture.Factory.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OrchestrationAndPeriodicRecovery_DoNotReportUnresolvedMylerzAsCompleted()
    {
        await using var fixture = new Fixture();
        var shipment = await fixture.SeedShipmentAsync();
        shipment.ExternalRegistrationAttemptCount = 1;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var localShipments = new Mock<IShipmentService>(MockBehavior.Strict);
        localShipments.Setup(s => s.CreateShipmentAsync(fixture.TenantId, shipment.OrderId, shipment.ShippingMethodId!.Value,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ShipmentAlreadyExistsException(shipment.OrderId, shipment.Id));
        var logger = new CapturingLogger<OrderShipmentOrchestrationConsumer>();
        var orchestration = new OrderShipmentOrchestrationConsumer(fixture.Db, localShipments.Object, fixture.Shipments, logger);
        var context = new Mock<ConsumeContext<OrderConfirmedEvent>>();
        context.SetupGet(c => c.Message).Returns(new OrderConfirmedEvent { TenantId = fixture.TenantId, OrderId = shipment.OrderId });
        await orchestration.Consume(context.Object);
        Assert.DoesNotContain(logger.Messages, m => m.Contains("External registration completed", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, m => m.Contains("EXTERNAL_REGISTRATION_RECONCILIATION_REQUIRED", StringComparison.Ordinal));

        var recovery = new ShipmentAuthorityRecoveryHostedService(Mock.Of<IServiceScopeFactory>(MockBehavior.Strict),
            NullLogger<ShipmentAuthorityRecoveryHostedService>.Instance);
        // Invoke the production batch worker without starting its infinite hosted loop.
        var method = typeof(ShipmentAuthorityRecoveryHostedService).GetMethod("RecoverPendingCarrierRegistrationsAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var recovered = await (Task<int>)method.Invoke(recovery,
            new object[] { fixture.Db, fixture.TenantContext, fixture.Shipments, CancellationToken.None })!;
        Assert.Equal(0, recovered);
        var persisted = await fixture.Db.Shipments.AsNoTracking().SingleAsync();
        Assert.Equal(1, persisted.ExternalRegistrationAttemptCount);
        Assert.Null(persisted.ExternalRegistrationCompletedAtUtc);
        Assert.Equal("ReconciliationRequired", persisted.ExternalRegistrationState);
        fixture.Factory.VerifyNoOtherCalls();
        fixture.Publisher.VerifyNoOtherCalls();
        fixture.Fulfillment.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NewSharedCreationTrackingAndWebhook_DoNotClaimPersistOrAdvanceLifecycle()
    {
        await using var fixture = new Fixture();
        var shipment = await fixture.SeedShipmentAsync();
        shipment.Status = ShipmentStatus.InTransit;
        shipment.TrackingNumber = "12345678901234";
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var tracking = await fixture.Shipments.SyncTrackingAsync(fixture.TenantId, shipment.Id);
        Assert.False(tracking.Success);
        var webhook = await fixture.Shipments.ProcessWebhookAsync(CarrierCodes.Mylerz,
            "{\"tracking_number\":\"12345678901234\",\"status\":\"delivered\",\"cod_collected\":true}", "signature", "127.0.0.1", "test");
        Assert.False(webhook.Success);
        Assert.Equal("WEBHOOK_INVALID_PAYLOAD", webhook.ErrorCode);
        var persisted = await fixture.Db.Shipments.SingleAsync();
        Assert.Equal(ShipmentStatus.InTransit, persisted.Status);
        Assert.False(persisted.CodCollected);
        Assert.Null(persisted.LastCarrierSyncAtUtc);
        Assert.Empty(fixture.Db.ShippingWebhookLogs);

        persisted.TrackingNumber = null;
        persisted.Status = ShipmentStatus.PendingExternalRegistration;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var worker = await fixture.Shipments.ExecuteCarrierForExistingShipmentAsync(fixture.TenantId, shipment.Id, "standard");
        Assert.False(worker.Success);
        Assert.Equal("SERVICE_LEVEL_UNSUPPORTED", worker.ErrorCode);
        var legacy = await fixture.Shipments.CreateShipmentWithCarrierAsync(fixture.TenantId, shipment.OrderId, CarrierCodes.Mylerz, "standard");
        Assert.False(legacy.Success);
        Assert.Equal("SERVICE_LEVEL_UNSUPPORTED", legacy.ErrorCode);
        persisted = await fixture.Db.Shipments.AsNoTracking().SingleAsync();
        Assert.Equal(0, persisted.ExternalRegistrationAttemptCount);
        Assert.Null(persisted.ExternalRegistrationLastAttemptAtUtc);
        Assert.Null(persisted.CarrierRequestId);
        Assert.Null(persisted.ExternalRegistrationCompletedAtUtc);
        fixture.Factory.VerifyNoOtherCalls();
        fixture.Publisher.VerifyNoOtherCalls();
        fixture.Fulfillment.VerifyNoOtherCalls();
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public TenantContext TenantContext { get; } = new();
        public ApplicationDbContext Db { get; }
        public Mock<IHttpClientFactory> Factory { get; } = new(MockBehavior.Strict);
        public Mock<IEventPublisher> Publisher { get; } = new(MockBehavior.Strict);
        public Mock<IOrderFulfillmentService> Fulfillment { get; } = new(MockBehavior.Strict);
        public CommerceReliabilityTelemetry Telemetry { get; } = new();
        public ShippingCarrierRegistry Registry { get; }
        public CarrierCapabilityRegistry Capabilities { get; }
        public CarrierIntegrationAuthority Authority { get; }
        public CarrierShipmentService Shipments { get; }

        public Fixture(bool configured = true, bool sandbox = false)
        {
            TenantContext.SetTenantId(TenantId);
            Db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"MylerzContainment_{Guid.NewGuid():N}").Options, TenantContext);
            var encryption = new CarrierCredentialEncryptionService(new EphemeralDataProtectionProvider(),
                NullLogger<CarrierCredentialEncryptionService>.Instance);
            var adapter = new MylerzAdapter(Factory.Object, NullLogger<MylerzAdapter>.Instance);
            Registry = new ShippingCarrierRegistry([adapter], NullLogger<ShippingCarrierRegistry>.Instance);
            Capabilities = new CarrierCapabilityRegistry(Registry);
            Authority = new CarrierIntegrationAuthority(Db, Registry, Capabilities, encryption, NullLogger<CarrierIntegrationAuthority>.Instance);
            Shipments = new CarrierShipmentService(Db, Registry, encryption, Publisher.Object, Fulfillment.Object, Telemetry,
                TenantContext, Mock.Of<ICustomsEvidenceAdmissionService>(MockBehavior.Strict),
                Mock.Of<ICarrierGeographicMappingService>(MockBehavior.Strict), NullLogger<CarrierShipmentService>.Instance, Authority);
            Db.CarrierConfigurations.Add(new CarrierConfiguration
            {
                Id = Guid.NewGuid(), TenantId = TenantId, CarrierCode = CarrierCodes.Mylerz, DisplayName = "Mylerz",
                IsEnabled = true, UseSandbox = sandbox, ApiKey = configured ? encryption.Encrypt("private-key") : null,
                ApiSecret = configured ? encryption.Encrypt("private-password") : null, AccountId = configured ? "merchant" : null,
                ApiUrl = "https://untrusted.example/", SupportsCod = true, SupportsTracking = true,
                SupportsWebhooks = true, SupportsLabels = true, SupportsQuotes = true, SupportsCancellation = true,
                WebhookSecret = encryption.Encrypt("private-webhook"), UseCarrierRates = false, FixedShippingCost = 0,
                LastValidationSucceeded = true, LastValidatedAtUtc = DateTime.UtcNow
            });
            Db.SaveChanges();
            Db.ChangeTracker.Clear();
        }

        public async Task<Shipment> SeedShipmentAsync()
        {
            var order = new Order
            {
                Id = Guid.NewGuid(), TenantId = TenantId, OrderNumber = "ORDER-1", ShippingMethodId = Guid.NewGuid(),
                ShippingMethodName = "Purchased Mylerz", ShippingServiceLevel = "standard", FulfillmentMode = FulfillmentMode.Carrier,
                FulfillmentCarrierCode = CarrierCodes.Mylerz, PaymentMethodCode = "COD", Total = 200,
                PaymentStatus = PaymentStatus.Pending, Status = OrderStatus.Confirmed, Currency = "EGP"
            };
            var shipment = new Shipment
            {
                Id = Guid.NewGuid(), TenantId = TenantId, OrderId = order.Id, ShippingMethodId = order.ShippingMethodId.Value,
                ShippingMethodName = order.ShippingMethodName, ShipmentNumber = "SHIP-1", ServiceLevel = "standard",
                CarrierCode = CarrierCodes.Mylerz, Status = ShipmentStatus.PendingExternalRegistration,
                IsCashOnDelivery = true, CodAmount = 200, Currency = "EGP", CreatedAt = DateTime.UtcNow
            };
            Db.Orders.Add(order);
            Db.Shipments.Add(shipment);
            await Db.SaveChangesAsync();
            return shipment;
        }

        public async ValueTask DisposeAsync()
        {
            Telemetry.Dispose();
            await Db.DisposeAsync();
        }
    }
}
