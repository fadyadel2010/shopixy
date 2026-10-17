using System.Net;
using System.Reflection;
using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using PointOfSale.API.Services.Shipping;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Customs;
using PointOfSale.Core.Interfaces.Fulfillment;
using PointOfSale.Core.Interfaces.Geographic;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Consumers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Observability;
using PointOfSale.Infrastructure.Payments;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Adapters;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Shipping;

public sealed class BostaCodCollectionEvidencePostgresTests : IDisposable
{
    private readonly TestDatabaseScope scope = TestDatabaseScope.Open();
    private readonly Guid tenant = Guid.NewGuid();
    private readonly Guid orderId = Guid.NewGuid();
    private readonly Guid shipmentId = Guid.NewGuid();
    private readonly Guid configId = Guid.NewGuid();
    private readonly CommerceReliabilityTelemetry telemetry = new();
    private readonly Mock<IEventPublisher> events = new();
    private readonly List<ShipmentCodCollectedEvent> collections = [];
    private readonly BostaAdapter adapter = new(new BostaOfficialCollectionParserTests.Factory(new HttpClient(new NoNetwork())), NullLogger<BostaAdapter>.Instance);
    private readonly Mock<IShippingCarrierRegistry> registry = new();
    private readonly long eventTime = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds();

    public BostaCodCollectionEvidencePostgresTests()
    {
        events.SetReturnsDefault(Task.CompletedTask);
        events.Setup(e => e.PublishAsync(It.IsAny<ShipmentCodCollectedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ShipmentCodCollectedEvent, CancellationToken>((e, _) => collections.Add(e)).Returns(Task.CompletedTask);
        registry.Setup(r => r.GetAdapter("bosta")).Returns(adapter);
        registry.Setup(r => r.GetAllAdapters()).Returns([adapter]);
    }

    private async Task Seed(bool binding = true, string? currency = "EGP", string payment = "COD", string orderCurrency = "EGP")
    {
        await using var db = scope.CreateDbContext(tenant);
        db.Tenants.Add(new Tenant { Id = tenant, Name = "Isolated Bosta COD", Slug = $"bosta-{tenant:N}", Currency = "EGP", IsActive = true, Status = TenantStatus.Active });
        var configuration = new CarrierConfiguration { Id = configId, TenantId = tenant, CarrierCode = "bosta", DisplayName = "Bosta fixture", IsEnabled = true,
            AccountId = "account-1", UseSandbox = true, SupportsCod = true, SupportsWebhooks = true, ApiKey = "mock-only", WebhookSecret = "mock-webhook-secret",
            Configuration = currency == null ? "{}" : JsonSerializer.Serialize(new { codCollectionCurrency = currency }) };
        db.CarrierConfigurations.Add(configuration);
        db.Orders.Add(new Order { Id = orderId, TenantId = tenant, OrderNumber = $"BOSTA-{orderId:N}", Total = 100m, Currency = orderCurrency, PaymentMethodCode = payment,
            PaymentStatus = PaymentStatus.Pending, Status = OrderStatus.Confirmed, FulfillmentMode = FulfillmentMode.Carrier, FulfillmentCarrierCode = "bosta", FulfillmentStatus = FulfillmentStatus.Delivered });
        var shipment = new Shipment { Id = shipmentId, TenantId = tenant, OrderId = orderId, CarrierCode = "bosta", ShipmentNumber = $"SHP-{shipmentId:N}", TrackingNumber = "12345678901234567890",
            ExternalReferenceId = "delivery-1", Currency = "EGP", IsCashOnDelivery = payment == "COD", CodAmount = 100m, Status = ShipmentStatus.Delivered,
            ExternalRegistrationCompletedAtUtc = DateTime.UtcNow.AddHours(-1), ExternalRegistrationState = "Completed" };
        db.Shipments.Add(shipment);
        using var authority = CarrierMoneyPersistenceAuthority.Begin();
        if (binding && currency is { Length: 3 } && currency.All(c => c is >= 'A' and <= 'Z')) db.CarrierCodShipmentBindings.Add(new CarrierCodShipmentBinding { Id = Guid.NewGuid(), TenantId = tenant, OrderId = orderId, ShipmentId = shipmentId,
            ConfigurationId = configId, CarrierCode = "bosta", AccountId = "account-1", Environment = "Sandbox", Currency = currency, TrackingNumber = "12345678901234567890",
            DeliveryReference = "delivery-1", ExternalReferenceId = "delivery-1",
            Fingerprint = CarrierCollectionMoneyAuthority.BindingFingerprint(configuration, shipment, "account-1", currency, "delivery-1"),
            CreatedAt = DateTime.UtcNow.AddHours(-1) });
        await db.SaveChangesAsync();
        await new TenantAccountSeeder(db, NullLogger<TenantAccountSeeder>.Instance).SeedAsync(tenant);
    }

    private CarrierShipmentService Shipping(ApplicationDbContext db, Guid? contextTenant = null)
    {
        var encryption = new Mock<ICarrierCredentialEncryptionService>();
        encryption.Setup(e => e.Decrypt(It.IsAny<string?>())).Returns<string?>(s => s ?? "");
        var integration = new CarrierIntegrationAuthority(db, registry.Object, new CarrierCapabilityRegistry(registry.Object), encryption.Object, NullLogger<CarrierIntegrationAuthority>.Instance);
        var fulfillment = new Mock<IOrderFulfillmentService>();
        fulfillment.SetReturnsDefault(Task.FromResult(OrderFulfillmentResult.Succeeded(orderId, "TEST", FulfillmentStatus.Delivered, FulfillmentStatus.Delivered, FulfillmentRequestSource.Webhook, "test")));
        return new(db, registry.Object, encryption.Object, events.Object, fulfillment.Object, telemetry, new TestTenantContext(contextTenant ?? tenant),
            Mock.Of<ICustomsEvidenceAdmissionService>(), Mock.Of<ICarrierGeographicMappingService>(), NullLogger<CarrierShipmentService>.Instance, integration);
    }

    private PaymentLifecycleService Payment(ApplicationDbContext db, bool postingFailure = false)
    {
        var correlation = new Mock<ICorrelationIdAccessor>(); correlation.Setup(c => c.GetCorrelationId()).Returns("isolated-bosta-cod");
        var resolver = new AccountResolver(db, NullLogger<AccountResolver>.Instance);
        ICommerceAccountingService commerce = new CommerceAccountingService(new FinancialPostingService(db, NullLogger<FinancialPostingService>.Instance), resolver,
            Mock.Of<ICashboxLedgerAccountService>(), NullLogger<CommerceAccountingService>.Instance, db);
        if (postingFailure)
        {
            var mock = new Mock<ICommerceAccountingService>();
            mock.Setup(c => c.PostCodDeliveredAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>())).ReturnsAsync(PostingResult.Failed("Injected isolated posting failure"));
            commerce = mock.Object;
        }
        var stock = new Mock<IProductVariantStockRepository>(); stock.SetReturnsDefault(Task.CompletedTask);
        var marketplace = new Mock<IMarketplaceEventOutboxService>(); marketplace.SetReturnsDefault(Task.CompletedTask);
        return new(db, new AccountingOutboxService(db, correlation.Object, NullLogger<AccountingOutboxService>.Instance), commerce, events.Object,
            marketplace.Object, correlation.Object, stock.Object, Mock.Of<IInventoryReservationMetricsService>(), Mock.Of<ICouponValidationService>(),
            NullLogger<PaymentLifecycleService>.Instance, Options.Create(new PaymentLifecycleSettings()));
    }

    private string Payload(string amount = "100", int state = 45, int type = 10, string extra = "") => BostaOfficialCollectionParserTests.Payload(amount, state: state, type: type, time: eventTime, extra: extra);
    private async Task<WebhookProcessResult> Observe(string? payload = null, string? secret = "mock-webhook-secret", Guid? contextTenant = null)
    {
        await using var db = scope.CreateDbContext(contextTenant ?? tenant);
        return await Shipping(db, contextTenant).ProcessWebhookAsync("bosta", payload ?? Payload(), secret, "203.0.113.10", "isolated-test", CancellationToken.None);
    }

    private async Task AssertNoMoney()
    {
        await using var db = scope.CreateDbContext(tenant);
        Assert.Equal(PaymentStatus.Pending, (await db.Orders.IgnoreQueryFilters().SingleAsync(o => o.TenantId == tenant)).PaymentStatus);
        Assert.Empty(await db.JournalEntries.ToListAsync());
        Assert.Empty(await db.AccountingOutboxMessages.ToListAsync());
        Assert.All(await db.LedgerAccounts.ToListAsync(), a => Assert.Equal(0m, a.CurrentBalance));
        Assert.Empty(await db.PaymentSettlementBatches.ToListAsync());
    }

    [Fact]
    public async Task OfficialObservation_ResolvesExactShipment_ProducesImmutableProvenance_AndPostsCanonicalMoneyOnce()
    {
        await Seed();
        var documentedPayload = Payload().Replace("\"type\":10", "\"type\":\"SEND\"");
        await using (var db = scope.CreateDbContext(tenant))
        {
            var resolution = await new ShippingWebhookTenantResolver(db, registry.Object, NullLogger<ShippingWebhookTenantResolver>.Instance).ResolveAsync("bosta", documentedPayload);
            Assert.True(resolution.Success);
            Assert.Equal(shipmentId, resolution.Resolution!.ShipmentId);
        }
        var first = await Observe(documentedPayload); Assert.True(first.Success, first.ErrorCode);
        var duplicate = await Observe(documentedPayload); Assert.True(duplicate.IsDuplicate);
        await using var verify = scope.CreateDbContext(tenant);
        var evidence = await verify.CarrierCollectionEvidence.SingleAsync();
        Assert.Equal("Qualified", evidence.AdmissionOutcome);
        Assert.Equal(100m, evidence.ObservedAmount); Assert.Equal("EGP", evidence.Currency);
        Assert.Equal(configId, evidence.ConfigurationId); Assert.Equal("account-1", evidence.AccountId); Assert.Equal("Sandbox", evidence.Environment);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(eventTime).UtcDateTime, evidence.ProviderEventAtUtc);
        Assert.True(evidence.ReceivedAtUtc > evidence.ProviderEventAtUtc); Assert.Equal(64, evidence.PayloadHash.Length);
        Assert.Equal("delivery-1", evidence.DeliveryReference); Assert.Equal("12345678901234567890", evidence.TrackingNumber);
        Assert.Equal(evidence.Id, Assert.Single(collections).CollectionEvidenceId);
        var message = collections[0]; message.CodAmount = 999m; message.Currency = "USD";
        var consumer = new ShipmentCodCollectedConsumer(Payment(verify), NullLogger<ShipmentCodCollectedConsumer>.Instance);
        var context = new Mock<ConsumeContext<ShipmentCodCollectedEvent>>(); context.SetupGet(c => c.Message).Returns(message);
        await consumer.Consume(context.Object); await consumer.Consume(context.Object);
        verify.ChangeTracker.Clear();
        var order = await verify.Orders.SingleAsync();
        Assert.Equal(PaymentStatus.Collected, order.PaymentStatus); Assert.Equal(FinancialPostingStatus.Posted, order.FinancialPostingStatus);
        var journal = await verify.JournalEntries.Include(j => j.LedgerEntries).SingleAsync();
        Assert.Equal(order.FinancialPostingJournalId, journal.Id); Assert.Equal("Order_COD", journal.SourceType);
        Assert.Equal($"COD_DELIVERED:{tenant}:{orderId}", journal.IdempotencyKey);
        var accounts = await verify.LedgerAccounts.ToDictionaryAsync(a => a.AccountCode);
        Assert.Equal(100m, Assert.Single(journal.LedgerEntries, l => l.AccountId == accounts["1130"].Id).DebitAmount);
        Assert.Equal(100m, Assert.Single(journal.LedgerEntries, l => l.AccountId == accounts["4020"].Id).CreditAmount);
        Assert.Equal(100m, accounts["1130"].CurrentBalance); Assert.Equal(0m, accounts["1010"].CurrentBalance); Assert.Equal(0m, accounts["1030"].CurrentBalance);
        var outbox = await verify.AccountingOutboxMessages.SingleAsync(); Assert.NotNull(outbox.ProcessedAtUtc);
        Assert.Single(await verify.CarrierCollectionEvidence.ToListAsync()); Assert.Single(await verify.ShippingWebhookLogs.ToListAsync());
        Assert.Empty(await verify.PaymentSettlementBatches.ToListAsync());
    }

    [Theory]
    [InlineData("absent", "COD_AMOUNT_ABSENT")]
    [InlineData("0", "COD_AMOUNT_ZERO")]
    [InlineData("99", "COD_WHOLE_ORDER_AMOUNT_MISMATCH")]
    [InlineData("101", "COD_WHOLE_ORDER_AMOUNT_MISMATCH")]
    [InlineData("-1", "COD_AMOUNT_INVALID")]
    [InlineData("\"NaN\"", "COD_AMOUNT_INVALID")]
    [InlineData("1e999", "COD_AMOUNT_INVALID")]
    [InlineData("100.001", "COD_AMOUNT_PRECISION_UNSUPPORTED")]
    public async Task MoneyDiscrepancy_IsDurableReview_NeverFullOrderRecognition(string amount, string code)
    {
        await Seed(); Assert.True((await Observe(Payload(amount))).Success);
        await using var db = scope.CreateDbContext(tenant);
        var evidence = await db.CarrierCollectionEvidence.SingleAsync(); Assert.Equal("Review", evidence.AdmissionOutcome); Assert.Equal(code, evidence.AdmissionCode);
        Assert.False((await db.Shipments.SingleAsync()).CodCollected); Assert.Empty(collections); await AssertNoMoney();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("egp")]
    [InlineData("XYZ")]
    [InlineData("USD")]
    public async Task MissingInvalidOrMismatchedCurrency_NeverGuesses(string? currency)
    {
        await Seed(currency: currency); Assert.True((await Observe()).Success);
        await using var db = scope.CreateDbContext(tenant);
        var evidence = await db.CarrierCollectionEvidence.SingleAsync(); Assert.Equal("Review", evidence.AdmissionOutcome);
        Assert.Equal(currency is "egp" or null ? null : currency, evidence.Currency); Assert.Empty(collections); await AssertNoMoney();
    }

    [Theory]
    [InlineData("PREPAID")]
    [InlineData("WALLET")]
    public async Task PrepaidDelivery_CannotBecomeCod(string payment)
    {
        await Seed(payment: payment); Assert.True((await Observe()).Success);
        await using var db = scope.CreateDbContext(tenant);
        Assert.Equal("COD_ORDER_REQUIRED", (await db.CarrierCollectionEvidence.SingleAsync()).AdmissionCode); await AssertNoMoney();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-secret")]
    public async Task UntrustedWebhook_CannotMutateEvidenceOrMoney(string? secret)
    {
        await Seed(); Assert.False((await Observe(secret: secret)).Success);
        await using var db = scope.CreateDbContext(tenant); Assert.Empty(await db.CarrierCollectionEvidence.ToListAsync());
        Assert.Empty(await db.ShippingWebhookLogs.ToListAsync()); Assert.False((await db.Shipments.SingleAsync()).CodCollected); await AssertNoMoney();
    }

    [Fact]
    public async Task MissingRegistrationBinding_FailsClosed_AndLaterStatusesAreNotDeliveryLevelDuplicates()
    {
        await Seed(binding: false); Assert.True((await Observe()).Success);
        await using var db = scope.CreateDbContext(tenant);
        Assert.Equal("COD_ACCOUNT_BINDING_REQUIRED", (await db.CarrierCollectionEvidence.SingleAsync()).AdmissionCode);
        Assert.False((await Observe(Payload(state: 46))).IsDuplicate); Assert.Equal(2, await db.ShippingWebhookLogs.CountAsync()); await AssertNoMoney();
    }

    [Theory]
    [InlineData(15, "", "COD_COLLECTION_CONTEXT_UNSUPPORTED")]
    [InlineData(10, ",\"isConfirmedDelivery\":false", "COD_DELIVERY_UNCONFIRMED")]
    public async Task UnsupportedCollectionContext_IsNotMoneyAuthority(int type, string extra, string code)
    {
        await Seed(); Assert.True((await Observe(Payload(type: type, extra: extra))).Success);
        await using var db = scope.CreateDbContext(tenant); Assert.Equal(code, (await db.CarrierCollectionEvidence.SingleAsync()).AdmissionCode); await AssertNoMoney();
    }

    [Fact]
    public async Task DeliveryCanAdvanceWithoutMoneyQualification_WhileActualLaterReturnRemainsDistinct()
    {
        await Seed(binding: false, currency: null);
        await using (var db = scope.CreateDbContext(tenant)) { (await db.Shipments.SingleAsync()).Status = ShipmentStatus.OutForDelivery; await db.SaveChangesAsync(); }
        var delivered = await Observe(Payload("absent")); Assert.True(delivered.StatusChanged);
        await using var verify = scope.CreateDbContext(tenant); Assert.Equal(ShipmentStatus.Delivered, (await verify.Shipments.SingleAsync()).Status);
        Assert.Empty(collections); await AssertNoMoney();
        var returned = await Observe(Payload(state: 46)); Assert.True(returned.StatusChanged); Assert.False(returned.IsDuplicate);
        verify.ChangeTracker.Clear(); Assert.Equal(ShipmentStatus.Returned, (await verify.Shipments.SingleAsync()).Status); Assert.Equal(2, await verify.ShippingWebhookLogs.CountAsync());
    }

    [Fact]
    public async Task ConflictingActualAmountAfterQualification_BlocksPreviouslyQualifiedEvidence()
    {
        await Seed(); Assert.True((await Observe()).Success);
        Assert.True((await Observe(Payload("99"))).Success);
        await using var db = scope.CreateDbContext(tenant);
        var result = await Payment(db).RecordCarrierCodCollectionAsync(tenant, orderId, shipmentId, collections[0].CollectionEvidenceId!.Value);
        Assert.True(result.RequiresManualReview); Assert.Equal("COD_OBSERVATION_CONFLICT_REVIEW", result.FailureCode);
        Assert.Equal(3, await db.CarrierCollectionEvidence.CountAsync()); await AssertNoMoney();
    }

    [Fact]
    public async Task NumericTrackingIsExact_ResolverRejectsPaddedIdentityAndForeignAmbiguity()
    {
        await Seed(); await using var db = scope.CreateDbContext(tenant);
        var resolver = new ShippingWebhookTenantResolver(db, registry.Object, NullLogger<ShippingWebhookTenantResolver>.Instance);
        Assert.False((await resolver.ResolveAsync("bosta", BostaOfficialCollectionParserTests.Payload(tracking: "\"012345678901234567890\""))).Success);
        var foreign = Guid.NewGuid(); var otherOrder = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = foreign, Name = "Foreign isolated", Slug = $"foreign-{foreign:N}", IsActive = true });
        db.Orders.Add(new Order { Id = otherOrder, TenantId = foreign, OrderNumber = $"FOREIGN-{otherOrder:N}" });
        db.Shipments.Add(new Shipment { TenantId = foreign, OrderId = otherOrder, CarrierCode = "bosta", ShipmentNumber = $"SHP-{otherOrder:N}", TrackingNumber = "12345678901234567890" });
        await db.SaveChangesAsync();
        var ambiguous = await resolver.ResolveAsync("bosta", Payload()); Assert.False(ambiguous.Success); Assert.Equal("WEBHOOK_TENANT_AMBIGUOUS", ambiguous.Failure!.ErrorCode);
        Assert.Empty(await db.CarrierCollectionEvidence.ToListAsync()); await AssertNoMoney();
    }

    [Theory]
    [InlineData("account")]
    [InlineData("environment")]
    [InlineData("delivery-reference")]
    [InlineData("expected-cod")]
    [InlineData("order-currency")]
    public async Task WebhookAdmission_CurrentBindingsAndExpectedEconomicsMustMatch(string defect)
    {
        await Seed(orderCurrency: defect == "order-currency" ? "USD" : "EGP");
        await using (var db = scope.CreateDbContext(tenant))
        {
            if (defect == "account") (await db.CarrierConfigurations.SingleAsync()).AccountId = "account-2";
            if (defect == "environment") (await db.CarrierConfigurations.SingleAsync()).UseSandbox = false;
            if (defect == "delivery-reference") (await db.Shipments.SingleAsync()).ExternalReferenceId = "different-delivery";
            if (defect == "expected-cod") (await db.Shipments.SingleAsync()).CodAmount = 99m;
            await db.SaveChangesAsync();
        }
        Assert.True((await Observe()).Success);
        await using var verify = scope.CreateDbContext(tenant); Assert.Equal("Review", (await verify.CarrierCollectionEvidence.SingleAsync()).AdmissionOutcome);
        Assert.Empty(collections); await AssertNoMoney();
    }

    [Theory]
    [InlineData("EGP", false, true)]
    [InlineData(null, false, false)]
    [InlineData("EGP", true, false)]
    public async Task RegistrationPersistence_CapturesOnlyPreCallExplicitAccountCurrencyBinding(string? currency, bool accountChanged, bool expectedBinding)
    {
        await Seed(binding: false, currency: currency);
        await using var db = scope.CreateDbContext(tenant);
        var shipment = await db.Shipments.SingleAsync(); var order = await db.Orders.SingleAsync();
        shipment.Status = ShipmentStatus.PendingExternalRegistration; shipment.TrackingNumber = null; shipment.ExternalReferenceId = null;
        shipment.ExternalRegistrationCompletedAtUtc = null; shipment.ExternalRegistrationState = "PendingExternalCall";
        await db.SaveChangesAsync();
        var config = await db.CarrierConfigurations.SingleAsync();
        var snapshot = JsonSerializer.Deserialize<CarrierIntegrationConfigurationSnapshot>(JsonSerializer.Serialize(new { config.Id, config.CarrierCode, config.DisplayName, config.IsEnabled, config.Configuration, config.UseSandbox, config.SupportsCod }))!;
        var resolution = new CarrierCredentialsResolution(true, "bosta", new CarrierCredentials
        {
            ApiKey = "mock-only",
            AccountId = accountChanged ? "previous-account" : "account-1",
            UseSandbox = true,
            ExtraConfig = currency == null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string> { ["codCollectionCurrency"] = currency }
        }, snapshot, []);
        var result = new CreateShipmentResult { Success = true, TrackingNumber = "12345678901234567890", ExternalReferenceId = "delivery-1", InitialStatus = ShipmentStatus.ReadyForPickup, CarrierMetadata = "{\"bostaId\":\"delivery-1\"}" };
        var method = typeof(CarrierShipmentService).GetMethod("PersistSuccessfulCarrierRegistrationAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var persisted = await (Task<CarrierShipmentResult>)method.Invoke(Shipping(db), [shipment, order, "Bosta", "standard", result, false, CancellationToken.None, resolution])!;
        Assert.True(persisted.Success, persisted.ErrorCode);
        Assert.Equal(expectedBinding ? 1 : 0, await db.CarrierCodShipmentBindings.CountAsync());
        if (expectedBinding) { var binding = await db.CarrierCodShipmentBindings.SingleAsync(); Assert.Equal(configId, binding.ConfigurationId); Assert.Equal("EGP", binding.Currency); Assert.Equal("account-1", binding.AccountId); Assert.Equal("delivery-1", binding.DeliveryReference); }
        await AssertNoMoney();
    }

    [Fact]
    public async Task RegistrationPersistence_UsesCredentialVersionScopeAndDecryptedExtraConfigForSupportedBostaSetup()
    {
        await Seed(binding: false, currency: "EGP");
        await using var db = scope.CreateDbContext(tenant);
        var shipment = await db.Shipments.SingleAsync();
        var order = await db.Orders.SingleAsync();
        var config = await db.CarrierConfigurations.SingleAsync();
        config.AccountId = null;
        config.ApiKey = "ENC:opaque-api-key-ciphertext";
        config.Configuration = "ENC:opaque-extra-config-ciphertext";
        shipment.Status = ShipmentStatus.PendingExternalRegistration;
        shipment.TrackingNumber = null;
        shipment.ExternalReferenceId = null;
        shipment.ExternalRegistrationCompletedAtUtc = null;
        shipment.ExternalRegistrationState = "PendingExternalCall";
        await db.SaveChangesAsync();

        var snapshot = JsonSerializer.Deserialize<CarrierIntegrationConfigurationSnapshot>(JsonSerializer.Serialize(new
        {
            config.Id,
            config.CarrierCode,
            config.DisplayName,
            config.IsEnabled,
            config.Configuration,
            config.UseSandbox,
            config.SupportsCod
        }))!;
        var resolution = new CarrierCredentialsResolution(true, "bosta", new CarrierCredentials
        {
            ApiKey = "decrypted-api-key",
            AccountId = null,
            UseSandbox = true,
            ExtraConfig = new Dictionary<string, string> { ["codCollectionCurrency"] = "EGP" }
        }, snapshot, []);
        var result = new CreateShipmentResult
        {
            Success = true,
            TrackingNumber = "12345678901234567890",
            ExternalReferenceId = "delivery-1",
            InitialStatus = ShipmentStatus.ReadyForPickup,
            CarrierMetadata = "{\"bostaId\":\"delivery-1\"}"
        };
        var method = typeof(CarrierShipmentService).GetMethod("PersistSuccessfulCarrierRegistrationAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var persisted = await (Task<CarrierShipmentResult>)method.Invoke(
            Shipping(db), [shipment, order, "Bosta", "standard", result, false, CancellationToken.None, resolution])!;

        Assert.True(persisted.Success, persisted.ErrorCode);
        var binding = await db.CarrierCodShipmentBindings.SingleAsync();
        Assert.StartsWith("bosta-credential:", binding.AccountId, StringComparison.Ordinal);
        Assert.Equal("EGP", binding.Currency);
        Assert.Equal(64, binding.Fingerprint.Length);
        await AssertNoMoney();
    }

    [Fact]
    public async Task PublicationFailure_RollsBackEvidenceLogAndCollectionFlag_ThenWebhookCanRetry()
    {
        await Seed();
        events.Setup(e => e.PublishAsync(It.IsAny<ShipmentCodCollectedEvent>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("Injected publication failure"));
        Assert.False((await Observe()).Success);
        await using var verify = scope.CreateDbContext(tenant); Assert.Empty(await verify.CarrierCollectionEvidence.ToListAsync()); Assert.Empty(await verify.ShippingWebhookLogs.ToListAsync());
        Assert.False((await verify.Shipments.SingleAsync()).CodCollected); await AssertNoMoney();
        events.Setup(e => e.PublishAsync(It.IsAny<ShipmentCodCollectedEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        Assert.True((await Observe()).Success); Assert.Single(await verify.CarrierCollectionEvidence.ToListAsync());
    }

    [Theory]
    [InlineData("tracking")]
    [InlineData("reference")]
    [InlineData("account")]
    [InlineData("environment")]
    [InlineData("configuration-disabled")]
    [InlineData("cancelled")]
    [InlineData("tenant-inactive")]
    [InlineData("shipment-deleted")]
    [InlineData("order-deleted")]
    [InlineData("shipment-returned")]
    [InlineData("carrier")]
    [InlineData("prepaid")]
    public async Task FinancialAdmission_RevalidatesChangedSqlAuthority_AndPersistsReview(string change)
    {
        await Seed(); Assert.True((await Observe()).Success); var message = Assert.Single(collections);
        await using (var db = scope.CreateDbContext(tenant))
        {
            var shipment = await db.Shipments.SingleAsync(); var config = await db.CarrierConfigurations.SingleAsync(); var order = await db.Orders.SingleAsync();
            switch (change)
            {
                case "tracking": shipment.TrackingNumber = "OTHER"; break;
                case "reference": shipment.ExternalReferenceId = "OTHER"; break;
                case "account": config.AccountId = "account-2"; break;
                case "environment": config.UseSandbox = false; break;
                case "configuration-disabled": config.IsEnabled = false; break;
                case "cancelled": order.Status = OrderStatus.Cancelled; break;
                case "tenant-inactive": (await db.Tenants.SingleAsync()).IsActive = false; break;
                case "shipment-deleted": shipment.IsDeleted = true; break;
                case "order-deleted": order.IsDeleted = true; break;
                case "shipment-returned": shipment.Status = ShipmentStatus.Returned; break;
                case "carrier": shipment.CarrierCode = "aramex"; break;
                // SQL fixture corruption exercises revalidation independently of the already-enforced immutable order snapshot guard.
                case "prepaid": await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Orders\" SET \"PaymentMethodCode\"='PREPAID' WHERE \"Id\"={orderId} AND \"TenantId\"={tenant}"); break;
            }
            await db.SaveChangesAsync();
        }
        await using var admission = scope.CreateDbContext(tenant);
        var result = await Payment(admission).RecordCarrierCodCollectionAsync(tenant, orderId, shipmentId, message.CollectionEvidenceId!.Value);
        Assert.True(result.RequiresManualReview, result.ErrorMessage);
        Assert.Single(await admission.CarrierCollectionEvidence.Where(e => e.AdmissionOutcome == "RevalidationReview").ToListAsync()); await AssertNoMoney();
        if (change == "account")
        {
            await using (var restoration = scope.CreateDbContext(tenant))
            {
                (await restoration.CarrierConfigurations.SingleAsync()).AccountId = "account-1";
                await restoration.SaveChangesAsync();
            }
            var replay = await Payment(admission).RecordCarrierCodCollectionAsync(tenant, orderId, shipmentId, message.CollectionEvidenceId!.Value);
            Assert.True(replay.RequiresManualReview); Assert.Equal(result.FailureCode, replay.FailureCode);
            Assert.Single(await admission.CarrierCollectionEvidence.Where(e => e.AdmissionOutcome == "RevalidationReview").ToListAsync()); await AssertNoMoney();
        }
    }

    [Fact]
    public async Task ForeignTenantAndWrongEvidenceLocator_NeverRecognizeMoney()
    {
        await Seed(); Assert.False((await Observe(contextTenant: Guid.NewGuid())).Success);
        Assert.True((await Observe()).Success);
        await using var db = scope.CreateDbContext(tenant);
        Assert.False((await Payment(db).RecordCarrierCodCollectionAsync(Guid.NewGuid(), orderId, shipmentId, collections[0].CollectionEvidenceId!.Value)).Success);
        Assert.False((await Payment(db).RecordCarrierCodCollectionAsync(tenant, orderId, Guid.NewGuid(), collections[0].CollectionEvidenceId!.Value)).Success);
        Assert.False((await Payment(db).RecordCarrierCodCollectionAsync(tenant, orderId, shipmentId)).Success); await AssertNoMoney();
        await using var foreign = scope.CreateDbContext(Guid.NewGuid()); Assert.Empty(await foreign.CarrierCollectionEvidence.ToListAsync()); Assert.Empty(await foreign.CarrierCodShipmentBindings.ToListAsync());
    }

    [Fact]
    public async Task EvidenceIsImmutable_AndPostgresPermanentIdentityConstraintsAreReal()
    {
        await Seed(); Assert.True((await Observe()).Success);
        await using var db = scope.CreateDbContext(tenant); var evidence = await db.CarrierCollectionEvidence.SingleAsync();
        evidence.ObservedAmount = 200m; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"CarrierCollectionEvidence\" SET \"IsDeleted\"=true WHERE \"Id\"={evidence.Id}"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"CarrierCollectionEvidence\" WHERE \"Id\"={evidence.Id}"));
        var binding = await db.CarrierCodShipmentBindings.AsNoTracking().SingleAsync();
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"CarrierCodShipmentBindings\" SET \"IsDeleted\"=true WHERE \"Id\"={binding.Id}"));
        var unauthorized = JsonSerializer.Deserialize<CarrierCollectionEvidence>(JsonSerializer.Serialize(await db.CarrierCollectionEvidence.AsNoTracking().SingleAsync()))!;
        unauthorized.Id = Guid.NewGuid(); db.CarrierCollectionEvidence.Add(unauthorized);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        using var authority = CarrierMoneyPersistenceAuthority.Begin();
        var clone = JsonSerializer.Deserialize<CarrierCollectionEvidence>(JsonSerializer.Serialize(await db.CarrierCollectionEvidence.AsNoTracking().SingleAsync()))!;
        clone.Id = Guid.NewGuid(); db.CarrierCollectionEvidence.Add(clone);
        var conflict = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)conflict.InnerException!).SqlState);
        db.ChangeTracker.Clear(); clone.Id = Guid.NewGuid(); clone.ObservationFingerprint = new string('B', 64); db.CarrierCollectionEvidence.Add(clone);
        conflict = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)conflict.InnerException!).SqlState);
        db.ChangeTracker.Clear(); binding.Id = Guid.NewGuid(); db.CarrierCodShipmentBindings.Add(binding);
        conflict = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)conflict.InnerException!).SqlState);
        await AssertNoMoney();
    }

    [Fact]
    public async Task PostingFailure_RollsBackPaymentJournalBalancesAndAccountingOutbox_ThenRealRetryPosts()
    {
        await Seed(); Assert.True((await Observe()).Success); var id = collections[0].CollectionEvidenceId!.Value;
        await using (var db = scope.CreateDbContext(tenant)) Assert.False((await Payment(db, true).RecordCarrierCodCollectionAsync(tenant, orderId, shipmentId, id)).Success);
        await AssertNoMoney();
        await using var retry = scope.CreateDbContext(tenant); Assert.True((await Payment(retry).RecordCarrierCodCollectionAsync(tenant, orderId, shipmentId, id)).Success);
        Assert.Single(await retry.JournalEntries.ToListAsync()); Assert.Single(await retry.AccountingOutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentWebhookAndConsumers_DoNotDuplicateObservationOrFinancialRecognition()
    {
        await Seed(); var observations = await Task.WhenAll(Observe(), Observe());
        Assert.All(observations, r => Assert.True(r.Success, r.ErrorCode)); Assert.Single(observations, r => r.IsDuplicate);
        var id = Assert.Single(collections).CollectionEvidenceId!.Value;
        async Task<PaymentTransitionResult> Collect() { await using var db = scope.CreateDbContext(tenant); return await Payment(db).RecordCarrierCodCollectionAsync(tenant, orderId, shipmentId, id); }
        var results = await Task.WhenAll(Collect(), Collect());
        foreach (var failure in results.Where(r => !r.Success)) { Assert.Equal("PAYMENT_CONCURRENCY_CONFLICT", failure.FailureCode); Assert.True((await Collect()).Success); }
        await using var verify = scope.CreateDbContext(tenant); Assert.Single(await verify.CarrierCollectionEvidence.ToListAsync());
        Assert.Single(await verify.JournalEntries.ToListAsync()); Assert.Single(await verify.AccountingOutboxMessages.ToListAsync());
        Assert.Equal(100m, (await verify.LedgerAccounts.SingleAsync(a => a.AccountCode == "1130")).CurrentBalance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealAccountingOutboxRecovery_RevalidatesMoneyEvidenceAndCannotPostAfterAccountChange(bool changedAccount)
    {
        await Seed(); Assert.True((await Observe()).Success);
        await using (var db = scope.CreateDbContext(tenant))
        {
            // Reconstruct an older pending financial recovery state in isolated SQL, not a merchant command.
            var order = await db.Orders.SingleAsync(); order.PaymentStatus = PaymentStatus.Collected; order.IsPaid = true; order.FinancialPostingStatus = FinancialPostingStatus.Pending;
            order.FinancialPostingPendingAtUtc = DateTime.UtcNow;
            if (changedAccount) (await db.CarrierConfigurations.SingleAsync()).AccountId = "changed-account";
            db.AccountingOutboxMessages.Add(PointOfSale.Core.Entities.Accounting.AccountingOutboxMessage.Create(tenant, "CodDelivered", orderId, "CodDelivered",
                JsonSerializer.Serialize(new { OrderId = orderId, MutationVersion = 1, AmountCollected = 999999m, Currency = "USD" }), $"COD_DELIVERED:{tenant}:{orderId}", "isolated-recovery", 1));
            await db.SaveChangesAsync();
        }
        var services = new ServiceCollection();
        services.AddScoped(_ => scope.CreateDbContext(tenant));
        services.AddScoped<IFinancialPostingService>(s => new FinancialPostingService(s.GetRequiredService<ApplicationDbContext>(), NullLogger<FinancialPostingService>.Instance));
        services.AddScoped<ICommerceAccountingService>(s => new CommerceAccountingService(s.GetRequiredService<IFinancialPostingService>(),
            new AccountResolver(s.GetRequiredService<ApplicationDbContext>(), NullLogger<AccountResolver>.Instance), Mock.Of<ICashboxLedgerAccountService>(),
            NullLogger<CommerceAccountingService>.Instance, s.GetRequiredService<ApplicationDbContext>()));
        services.AddSingleton(events.Object);
        var marketplace = new Mock<IMarketplaceEventOutboxService>(); marketplace.SetReturnsDefault(Task.CompletedTask); services.AddSingleton(marketplace.Object);
        using var provider = services.BuildServiceProvider();
        var processor = new AccountingOutboxProcessor(provider, NullLogger<AccountingOutboxProcessor>.Instance);
        await processor.ProcessPendingMessagesAsync(); await processor.ProcessPendingMessagesAsync();
        await using var verify = scope.CreateDbContext(tenant); var message = await verify.AccountingOutboxMessages.SingleAsync();
        if (changedAccount)
        {
            Assert.Null(message.ProcessedAtUtc); Assert.Contains("COD_ACCOUNT_BINDING_CHANGED", message.LastError);
            Assert.Empty(await verify.JournalEntries.ToListAsync()); Assert.All(await verify.LedgerAccounts.ToListAsync(), a => Assert.Equal(0m, a.CurrentBalance));
            Assert.Single(await verify.CarrierCollectionEvidence.Where(e => e.AdmissionOutcome == "RevalidationReview").ToListAsync());
        }
        else
        {
            Assert.NotNull(message.ProcessedAtUtc); var journal = await verify.JournalEntries.Include(j => j.LedgerEntries).SingleAsync();
            Assert.Equal(100m, journal.TotalDebits); Assert.Equal(100m, journal.TotalCredits); Assert.Equal(2, journal.LedgerEntries.Count);
            Assert.Equal(FinancialPostingStatus.Posted, (await verify.Orders.SingleAsync()).FinancialPostingStatus);
            Assert.Equal(100m, (await verify.LedgerAccounts.SingleAsync(a => a.AccountCode == "1130")).CurrentBalance);
            Assert.Equal(0m, (await verify.LedgerAccounts.SingleAsync(a => a.AccountCode == "1010")).CurrentBalance);
        }
    }

    public void Dispose() { telemetry.Dispose(); scope.Dispose(); }
    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new InvalidOperationException("Live carrier HTTP is forbidden in these fixtures.");
    }
}
