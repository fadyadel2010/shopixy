using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.DTOs.ShippingAdministration;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.Services.Pricing;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.ShippingAdministration;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.ShippingAdministration;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Administration;

namespace PointOfSale.Tests.Architecture;

public sealed class CarrierIntegrationStateMachineConvergenceTests
{
    [Theory]
    [InlineData("bosta")][InlineData("mylerz")][InlineData("fedex")]
    public async Task DraftOnboardingAndCredentialOnlySave_NeverTestConnectivityOrConferActivation(string carrier)
    {
        await using var db = CreateDbContext();
        var adapter = RecordingCarrierAdapter.Success(carrier, carrier);
        var service = CreateAdministrationService(db, adapter, out var authority);
        var tenant = Guid.NewGuid();
        var created = await service.CreateCarrierAccountAsync(tenant, new(carrier), Actor(), "isolated-draft", default);
        Assert.True(created.Succeeded); Assert.Equal("Draft", created.State);
        var supported = Assert.Single((await service.ListSupportedCarriersAsync(default)).Where(c => c.CarrierCode == carrier));
        Assert.True(supported.AccountRequired); Assert.True(supported.ReadinessRequired);
        var command = new ConnectionTestCommand("isolated-secret-key", "isolated-secret-password", "isolated-account", null, false,
            new Dictionary<string, string> { ["businessLocationId"] = "isolated-location", ["codCollectionCurrency"] = "EGP", ["WarehouseName"] = "isolated-warehouse" },
            WebhookSecret: "isolated-webhook-secret", SaveOnly: true);
        var result = await service.RunConnectionTestAsync(tenant, created.CarrierAccountId!.Value, command, Actor(), "isolated-save-only", default);
        Assert.True(result.Succeeded); Assert.Contains("CREDENTIALS_SAVED_NOT_CONNECTION_VERIFIED", result.Blockers);
        var configuration = await db.CarrierConfigurations.IgnoreQueryFilters().SingleAsync();
        Assert.False(configuration.IsEnabled); Assert.Null(configuration.LastValidatedAtUtc); Assert.Null(configuration.LastValidationSucceeded);
        Assert.Null(adapter.CapturedCredentials); Assert.Empty(db.CarrierConnectionTestRecords);
        Assert.StartsWith("ENC:", configuration.ApiKey); Assert.StartsWith("ENC:", configuration.ApiSecret);
        var dto = await service.GetCarrierAccountAsync(tenant, created.CarrierAccountId.Value, default);
        Assert.False(dto!.IntegrationReadiness!.CanExecute); Assert.Equal("NotReady", dto.OperationalState); Assert.Equal("Blocked", dto.HealthState);
        Assert.Equal(6, dto.Operations!.Count);
        Assert.All(dto.Operations, op => { Assert.False(op.AccountReady); Assert.True(op.RequiresShipmentEvidence); });
        Assert.Equal("Unavailable", dto.Operations.Single(op => op.Operation == "quote").Implementation);
        Assert.Equal("ReconciliationOnly", dto.Operations.Single(op => op.Operation == "recovery").Implementation);
        var outward = System.Text.Json.JsonSerializer.Serialize(new { result, dto, audit = await service.GetAuditAsync(tenant, created.CarrierAccountId, 20, default) });
        Assert.DoesNotContain(command.ApiKey, outward); Assert.DoesNotContain(command.ApiSecret, outward); Assert.DoesNotContain(command.WebhookSecret, outward);
        Assert.False((await authority.GetIntegrationReadinessAsync(tenant, carrier)).CanExecute);
    }

    [Fact]
    public async Task DhlLocalValidationRecord_CannotPresentRemoteTrackingAccountReadiness()
    {
        await using var db = CreateDbContext();
        var service = CreateAdministrationService(db, RecordingCarrierAdapter.Success(CarrierCodes.Dhl, "DHL"), out _);
        var tenant = Guid.NewGuid();
        var created = await service.CreateCarrierAccountAsync(tenant, new(CarrierCodes.Dhl), Actor(), "synthetic-local-only", default);
        Assert.True(created.Succeeded);
        await service.RunConnectionTestAsync(tenant, created.CarrierAccountId!.Value,
            new ConnectionTestCommand("synthetic-local-key", "synthetic-local-secret", "synthetic-local-account", null, false, new Dictionary<string, string>()),
            Actor(), "synthetic-local-check", default);
        // The current writer performs versioned authentication. Append a genuine
        // legacy-shaped local-only record instead of mislabelling today's test.
        var account = await db.CarrierAccounts.IgnoreQueryFilters().SingleAsync();
        db.CarrierConnectionTestRecords.Add(new CarrierConnectionTestRecord
        {
            Id = Guid.NewGuid(), TenantId = tenant, CarrierAccountId = account.Id,
            CredentialReferenceId = account.ActiveCredentialReferenceId, Environment = account.Environment!.Value,
            Status = CarrierConnectionTestStatus.Succeeded, TestedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var dto = await service.GetCarrierAccountAsync(tenant, created.CarrierAccountId.Value, default);
        Assert.False(dto!.ConnectionTestIsCurrent);
        Assert.All(dto!.Operations!, operation =>
        {
            Assert.False(operation.AccountReady);
            Assert.Contains("ACCOUNT_CURRENT_VALIDATION_NOT_RECORDED", operation.Blockers);
        });
    }

    [Fact]
    public async Task BostaSaveAndValidate_StoresEncryptedConfigurationWithoutImplicitActivationOrBookingReadiness()
    {
        await using var db = CreateDbContext();
        var adapter = RecordingCarrierAdapter.Success(CarrierCodes.Bosta, "Bosta");
        var service = CreateAdministrationService(db, adapter, out var authority);
        var tenantId = Guid.NewGuid();
        var accountId = await CreateBostaAccountAsync(service, tenantId);

        var result = await service.RunConnectionTestAsync(
            tenantId,
            accountId,
            BostaConnection("bosta-live-key"),
            Actor(),
            "step-2m-bosta-success",
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var config = await db.CarrierConfigurations.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(CarrierCodes.Bosta, config.CarrierCode);
        Assert.StartsWith("ENC:", config.ApiKey);
        Assert.DoesNotContain("bosta-live-key", config.ApiKey, StringComparison.Ordinal);
        Assert.True(config.LastValidationSucceeded);
        Assert.Equal("bosta-live-key", adapter.CapturedCredentials?.ApiKey);

        var readiness = await authority.GetIntegrationReadinessAsync(tenantId, CarrierCodes.Bosta);
        Assert.Equal("Available", readiness.CredentialAvailability);
        Assert.False(readiness.CanExecute);
        Assert.DoesNotContain("CARRIER_GEOGRAPHY_MAPPING_UNPUBLISHED", readiness.Blockers);
        Assert.Contains("BOSTA_INTEGRATION_ACTIVATION_REQUIRED", readiness.Blockers);
        Assert.DoesNotContain("CARRIER_CONFIGURATION_MISSING", readiness.Blockers);
        var account = await service.GetCarrierAccountAsync(tenantId, accountId, default);
        Assert.True(account!.Operations!.Single(op => op.Operation == "tracking").AccountReady);
        Assert.False(account.Operations.Single(op => op.Operation == "create").AccountReady);
    }

    [Fact]
    public async Task BostaConnectionSuccess_CannotLeaveSucceededEvidenceWithMissingCredentials()
    {
        await using var db = CreateDbContext();
        var service = CreateAdministrationService(db, RecordingCarrierAdapter.Success(CarrierCodes.Bosta, "Bosta"), out var authority);
        var tenantId = Guid.NewGuid();
        var accountId = await CreateBostaAccountAsync(service, tenantId);

        await service.RunConnectionTestAsync(
            tenantId,
            accountId,
            BostaConnection("same-canonical-key"),
            Actor(),
            "step-2m-no-split",
            CancellationToken.None);

        var test = await db.CarrierConnectionTestRecords.IgnoreQueryFilters().SingleAsync();
        var readiness = await authority.GetIntegrationReadinessAsync(tenantId, CarrierCodes.Bosta);

        Assert.Equal(CarrierConnectionTestStatus.Succeeded, test.Status);
        Assert.Equal("Available", readiness.CredentialAvailability);
        Assert.False(readiness.CanExecute);
        Assert.DoesNotContain("CARRIER_GEOGRAPHY_MAPPING_UNPUBLISHED", readiness.Blockers);
        Assert.Contains("BOSTA_INTEGRATION_ACTIVATION_REQUIRED", readiness.Blockers);
        Assert.DoesNotContain("CARRIER_CONNECTION_NOT_VERIFIED", readiness.Blockers);
    }

    [Fact]
    public async Task BostaValidationFailure_KeepsCredentialsSavedButNotVerified()
    {
        await using var db = CreateDbContext();
        var adapter = RecordingCarrierAdapter.Failure(CarrierCodes.Bosta, "Bosta", "INVALID_CREDENTIALS", "Invalid API key");
        var service = CreateAdministrationService(db, adapter, out var authority);
        var tenantId = Guid.NewGuid();
        var accountId = await CreateBostaAccountAsync(service, tenantId);

        var result = await service.RunConnectionTestAsync(
            tenantId,
            accountId,
            BostaConnection("bad-but-saved-key"),
            Actor(),
            "step-2m-invalid",
            CancellationToken.None);

        Assert.False(result.Succeeded);
        var config = await db.CarrierConfigurations.IgnoreQueryFilters().SingleAsync();
        Assert.StartsWith("ENC:", config.ApiKey);
        Assert.False(config.LastValidationSucceeded);

        var readiness = await authority.GetIntegrationReadinessAsync(tenantId, CarrierCodes.Bosta);
        Assert.Equal("Available", readiness.CredentialAvailability);
        Assert.False(readiness.CanExecute);
        Assert.Contains("CARRIER_CONNECTION_NOT_VERIFIED", readiness.Blockers);
    }

    [Fact]
    public async Task BostaNetworkFailure_IsNotReportedAsInvalidCredentials()
    {
        await using var db = CreateDbContext();
        var adapter = RecordingCarrierAdapter.Failure(CarrierCodes.Bosta, "Bosta", "CONNECTION_ERROR", "DNS lookup failed");
        var service = CreateAdministrationService(db, adapter, out _);
        var tenantId = Guid.NewGuid();
        var accountId = await CreateBostaAccountAsync(service, tenantId);

        var result = await service.RunConnectionTestAsync(
            tenantId,
            accountId,
            BostaConnection("saved-key-during-network-failure"),
            Actor(),
            "step-2m-network",
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("CONNECTION_ERROR", result.FailureCode);
        var account = await db.CarrierAccounts.IgnoreQueryFilters().SingleAsync(item => item.Id == accountId);
        Assert.Equal(CarrierAccountHealthState.Warning, account.HealthState);
        Assert.Contains("not marked invalid", account.HealthReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ManualFulfillment_DoesNotCreateExternalCarrierRuntimeArtifacts()
    {
        await using var db = CreateDbContext();
        var authority = CreateAuthority(db, RecordingCarrierAdapter.Success(CarrierCodes.Bosta, "Bosta"));
        var readiness = await authority.GetIntegrationReadinessAsync(Guid.NewGuid(), CarrierCodes.Manual);

        Assert.True(readiness.IsManual);
        Assert.True(readiness.CanExecute);
        Assert.Equal("NotRequired", readiness.CredentialAvailability);
        Assert.Empty(await db.CarrierConfigurations.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.CarrierConnectionTestRecords.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.CarrierAccounts.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.CarrierWarehouseAssignments.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task CarrierAccountFacadeState_DoesNotOverrideCanonicalReadinessPresentation()
    {
        await using var db = CreateDbContext();
        var service = CreateAdministrationService(db, RecordingCarrierAdapter.Success(CarrierCodes.Bosta, "Bosta"), out _);
        var tenantId = Guid.NewGuid();
        var accountId = await CreateBostaAccountAsync(service, tenantId);
        await service.RunConnectionTestAsync(
            tenantId,
            accountId,
            BostaConnection("ready-key"),
            Actor(),
            "step-2m-facade",
            CancellationToken.None);

        var facade = await db.CarrierAccounts.IgnoreQueryFilters().SingleAsync(item => item.Id == accountId);
        facade.HealthState = CarrierAccountHealthState.Blocked;
        facade.HealthReason = "legacy facade blocker";
        await db.SaveChangesAsync();

        var account = await service.GetCarrierAccountAsync(tenantId, accountId, CancellationToken.None);

        Assert.NotNull(account);
        Assert.False(account!.IntegrationReadiness?.CanExecute);
        Assert.DoesNotContain("CARRIER_GEOGRAPHY_MAPPING_UNPUBLISHED", account.IntegrationReadiness!.Blockers);
        Assert.Contains("BOSTA_PROVIDER_ACCOUNT_ELIGIBILITY_UNVERIFIED", account.IntegrationReadiness.Blockers);
        Assert.Equal("Blocked", account.HealthState);
        Assert.Equal("legacy facade blocker", account.HealthReason);
    }

    [Fact]
    public async Task AramexExtraCredentialConfiguration_IsEncryptedAtRestAndSuppliedToAdapter()
    {
        await using var db = CreateDbContext();
        var adapter = RecordingCarrierAdapter.Success(CarrierCodes.Aramex, "Aramex");
        var authority = CreateAuthority(db, adapter);
        var tenantId = Guid.NewGuid();

        await authority.ValidateAndStoreIntegrationAsync(
            tenantId,
            CarrierCodes.Aramex,
            new CarrierIntegrationUpdate(
                DisplayName: null,
                IsEnabled: true,
                IsDefault: null,
                ApiKey: "aramex-user",
                ApiSecret: "aramex-password",
                AccountId: "acc-1",
                ApiUrl: null,
                UseSandbox: true,
                SupportsCod: null,
                SupportsTracking: null,
                SupportsWebhooks: null,
                SupportsLabels: null,
                Configuration: """{"AccountPin":"super-secret-pin","AccountEntity":"CAI","AccountCountryCode":"EG"}""",
                PickupAddress: null,
                PickupCity: null,
                PickupCountry: null,
                PickupPhone: null,
                PickupContactName: null,
                DefaultServiceLevel: null,
                UseCarrierRates: null,
                FixedShippingCost: null,
                MarkupPercentage: null,
                FreeShippingThreshold: null,
                SortOrder: null,
                Notes: null),
            CancellationToken.None);

        var stored = await db.CarrierConfigurations.IgnoreQueryFilters().SingleAsync();
        Assert.StartsWith("ENC:", stored.Configuration);
        Assert.DoesNotContain("super-secret-pin", stored.Configuration, StringComparison.Ordinal);

        var validation = await authority.ValidateResolvedCredentialsAsync(tenantId, CarrierCodes.Aramex);

        Assert.True(validation.Success);
        Assert.Equal("super-secret-pin", adapter.CapturedCredentials?.ExtraConfig["AccountPin"]);
        Assert.Equal("CAI", adapter.CapturedCredentials?.ExtraConfig["AccountEntity"]);
        Assert.Equal("EG", adapter.CapturedCredentials?.ExtraConfig["AccountCountryCode"]);
    }

    [Fact]
    public async Task StorefrontBostaMethod_RemainsHiddenUntilPublishedGeographyMappingExists()
    {
        await using var db = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        SeedTenantWarehouseAndMethod(db, tenantId, warehouseId, CarrierCodes.Bosta);
        var authority = CreateAuthority(db, RecordingCarrierAdapter.Success(CarrierCodes.Bosta, "Bosta"));
        var storefront = CreateStorefrontService(db, authority);

        var before = await storefront.GetShippingMethodsAvailabilityForConfiguredWarehousesAsync(
            tenantId,
            100m,
            0m,
            Array.Empty<CartItemDto>(),
            "step-2m-before",
            CancellationToken.None);
        Assert.Empty(before.ShippingMethods);

        await authority.ValidateAndStoreIntegrationAsync(
            tenantId,
            CarrierCodes.Bosta,
            new CarrierIntegrationUpdate(
                DisplayName: null,
                IsEnabled: true,
                IsDefault: null,
                ApiKey: "checkout-ready-key",
                ApiSecret: null,
                AccountId: null,
                ApiUrl: null,
                UseSandbox: true,
                SupportsCod: null,
                SupportsTracking: null,
                SupportsWebhooks: null,
                SupportsLabels: null,
                Configuration: null,
                PickupAddress: null,
                PickupCity: null,
                PickupCountry: null,
                PickupPhone: null,
                PickupContactName: null,
                DefaultServiceLevel: null,
                UseCarrierRates: null,
                FixedShippingCost: null,
                MarkupPercentage: null,
                FreeShippingThreshold: null,
                SortOrder: null,
                Notes: null),
            CancellationToken.None);
        await authority.ValidateResolvedCredentialsAsync(tenantId, CarrierCodes.Bosta);

        var after = await storefront.GetShippingMethodsAvailabilityForConfiguredWarehousesAsync(
            tenantId,
            100m,
            0m,
            Array.Empty<CartItemDto>(),
            "step-2m-after",
            CancellationToken.None);

        Assert.Empty(after.ShippingMethods);
        var readiness = await authority.GetIntegrationReadinessAsync(
            tenantId,
            CarrierCodes.Bosta,
            purpose: CarrierIntegrationPurpose.StorefrontAdmission);
        Assert.DoesNotContain("CARRIER_GEOGRAPHY_MAPPING_UNPUBLISHED", readiness.Blockers);
        Assert.Contains("BOSTA_INTEGRATION_ACTIVATION_REQUIRED", readiness.Blockers);
    }

    private static async Task<Guid> CreateBostaAccountAsync(
        ShippingAdministrationApiService service,
        Guid tenantId)
    {
        var created = await service.CreateCarrierAccountAsync(
            tenantId,
            new(CarrierCodes.Bosta),
            Actor(),
            "step-2m-create",
            CancellationToken.None);
        Assert.True(created.Succeeded);
        return created.CarrierAccountId!.Value;
    }

    private static ConnectionTestCommand BostaConnection(string apiKey) =>
        new(apiKey, null, null, null, true,
            new Dictionary<string, string>
            {
                ["codCollectionCurrency"] = "EGP",
                ["businessLocationId"] = "business-location-1"
            },
            "test-webhook-authorization");

    private static ShippingAdministrationApiService CreateAdministrationService(
        ApplicationDbContext db,
        IShippingCarrierAdapter adapter,
        out CarrierIntegrationAuthority authority)
    {
        var registry = new ShippingCarrierRegistry(new[] { adapter, new ManualCarrierAdapter() }, NullLogger<ShippingCarrierRegistry>.Instance);
        var runtime = new CarrierAccountAdministrationRuntime(db, registry, NullLogger<CarrierAccountAdministrationRuntime>.Instance);
        var capabilityRegistry = new CarrierCapabilityRegistry(registry);
        authority = new CarrierIntegrationAuthority(
            db,
            registry,
            capabilityRegistry,
            new FakeCredentialEncryptionService(),
            NullLogger<CarrierIntegrationAuthority>.Instance);
        return new ShippingAdministrationApiService(db, runtime, registry, capabilityRegistry, authority);
    }

    private static CarrierIntegrationAuthority CreateAuthority(ApplicationDbContext db, params IShippingCarrierAdapter[] adapters)
    {
        var registry = new ShippingCarrierRegistry(adapters.Concat(new[] { new ManualCarrierAdapter() }), NullLogger<ShippingCarrierRegistry>.Instance);
        return new CarrierIntegrationAuthority(
            db,
            registry,
            new CarrierCapabilityRegistry(registry),
            new FakeCredentialEncryptionService(),
            NullLogger<CarrierIntegrationAuthority>.Instance);
    }

    private static ShippingMethodService CreateStorefrontService(ApplicationDbContext db, ICarrierIntegrationAuthority authority)
    {
        return new ShippingMethodService(
            Mock.Of<IShippingMethodReadRepository>(),
            db,
            Mock.Of<IShippingMethodCacheService>(),
            new ShippingCalculationService(),
            new CommerceReliabilityTelemetry(),
            NullLogger<ShippingMethodService>.Instance,
            authority);
    }

    private static void SeedTenantWarehouseAndMethod(ApplicationDbContext db, Guid tenantId, Guid warehouseId, string carrierCode)
    {
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Tenant",
            Currency = "EGP",
            Status = TenantStatus.Active
        });
        db.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Main",
            IsActive = true
        });
        db.ShippingMethods.Add(new ShippingMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Bosta Standard",
            BaseRate = 25m,
            PerKgRate = 0m,
            IsActive = true,
            SortOrder = 1,
            CarrierCode = carrierCode,
            CarrierName = "Bosta",
            ServiceLevel = "standard",
            FulfillmentWarehouseId = warehouseId
        });
        db.SaveChanges();
    }

    private static CarrierAdministrationActor Actor() => new("step-2m-admin");

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class FakeCredentialEncryptionService : ICarrierCredentialEncryptionService
    {
        public string Encrypt(string plainText) =>
            string.IsNullOrEmpty(plainText) || IsEncrypted(plainText)
                ? plainText
                : $"ENC:{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plainText))}";

        public string Decrypt(string? encryptedOrPlainText) =>
            string.IsNullOrEmpty(encryptedOrPlainText)
                ? string.Empty
                : IsEncrypted(encryptedOrPlainText)
                    ? System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encryptedOrPlainText["ENC:".Length..]))
                    : encryptedOrPlainText;

        public bool IsEncrypted(string? value) =>
            value?.StartsWith("ENC:", StringComparison.Ordinal) == true;
    }

    private sealed class RecordingCarrierAdapter : IShippingCarrierAdapter, IBostaPickupLocationDiscovery
    {
        private readonly CarrierValidationResult _validationResult;

        private RecordingCarrierAdapter(string carrierCode, string displayName, CarrierValidationResult validationResult)
        {
            CarrierCode = carrierCode;
            DisplayName = displayName;
            _validationResult = validationResult;
        }

        public string CarrierCode { get; }
        public string DisplayName { get; }
        public CarrierCredentials? CapturedCredentials { get; private set; }
        public Task<BostaPickupLocationResult> GetPickupLocationsAsync(CarrierCredentials credentials, CancellationToken ct = default) =>
            Task.FromResult(new BostaPickupLocationResult(true,
                new[] { "isolated-location", "business-location-1" }.Select(id => new BostaPickupLocationOption(id, false, "EG",
                    "synthetic-city", "Synthetic city", "synthetic-district", "Synthetic district") { CountryId = "synthetic-country" }).ToArray(), null));

        public static RecordingCarrierAdapter Success(string carrierCode, string displayName) =>
            new(carrierCode, displayName, new CarrierValidationResult { Success = true, AccountName = $"{displayName} Account" });

        public static RecordingCarrierAdapter Failure(string carrierCode, string displayName, string errorCode, string errorMessage) =>
            new(carrierCode, displayName, new CarrierValidationResult { Success = false, ErrorCode = errorCode, ErrorMessage = errorMessage });

        public Task<CarrierValidationResult> ValidateCredentialsAsync(CarrierCredentials credentials, CancellationToken cancellationToken = default)
        {
            CapturedCredentials = credentials;
            return Task.FromResult(_validationResult);
        }

        public Task<ShippingQuoteResult> GetQuoteAsync(ShippingQuoteRequest request, CarrierCredentials credentials, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ShippingQuoteResult { Success = false });

        public Task<CreateShipmentResult> CreateShipmentAsync(CreateShipmentRequest request, CarrierCredentials credentials, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CreateShipmentResult { Success = false });

        public Task<CancelShipmentResult> CancelShipmentAsync(string trackingNumber, CarrierCredentials credentials, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CancelShipmentResult { Success = false });

        public Task<ShipmentTrackingResult> GetTrackingAsync(string trackingNumber, CarrierCredentials credentials, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ShipmentTrackingResult { Success = false, CurrentStatus = ShipmentStatus.Pending });

        public IReadOnlyList<ServiceLevelInfo> GetSupportedServiceLevels() =>
            new[] { new ServiceLevelInfo { Code = "standard", Name = "Standard" } };

        public WebhookParseResult? ParseWebhook(string payload, string? signature, string? webhookSecret) => null;
    }

    private sealed class ManualCarrierAdapter : IShippingCarrierAdapter
    {
        public string CarrierCode => CarrierCodes.Manual;
        public string DisplayName => "Manual Fulfillment";

        public Task<ShippingQuoteResult> GetQuoteAsync(ShippingQuoteRequest request, CarrierCredentials credentials, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ShippingQuoteResult { Success = true });

        public Task<CreateShipmentResult> CreateShipmentAsync(CreateShipmentRequest request, CarrierCredentials credentials, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CreateShipmentResult { Success = true });

        public Task<CancelShipmentResult> CancelShipmentAsync(string trackingNumber, CarrierCredentials credentials, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CancelShipmentResult { Success = true });

        public Task<ShipmentTrackingResult> GetTrackingAsync(string trackingNumber, CarrierCredentials credentials, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ShipmentTrackingResult { Success = true, CurrentStatus = ShipmentStatus.Pending });

        public Task<CarrierValidationResult> ValidateCredentialsAsync(CarrierCredentials credentials, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CarrierValidationResult { Success = true });

        public IReadOnlyList<ServiceLevelInfo> GetSupportedServiceLevels() =>
            new[] { new ServiceLevelInfo { Code = "standard", Name = "Standard" } };

        public WebhookParseResult? ParseWebhook(string payload, string? signature, string? webhookSecret) => null;
    }
}
