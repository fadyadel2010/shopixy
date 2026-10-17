using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PointOfSale.API.Services.Shipping;
using PointOfSale.API.Controllers;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.ShippingAdministration;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.Tests.Shipping;

public sealed class CarrierIntegrationAuthorityRemediationTests
{
    [Fact]
    public async Task Bosta_AuthenticationAndCheckoutDoNotRequirePickup_BookingStillDoes()
    {
        await using var fixture = CreateFixture();
        await fixture.Authority.ValidateAndStoreIntegrationAsync(fixture.TenantId, CarrierCodes.Bosta,
            CreateUpdate(apiKey: "synthetic-auth-only") with { Configuration = "{}" });
        Assert.True((await fixture.Authority.ValidateResolvedCredentialsAsync(fixture.TenantId, CarrierCodes.Bosta)).Success);
        var auth = await fixture.Authority.GetIntegrationReadinessAsync(fixture.TenantId, CarrierCodes.Bosta, purpose: CarrierIntegrationPurpose.AdminValidation);
        Assert.DoesNotContain("BOSTA_BUSINESS_LOCATION_REQUIRED", auth.Blockers);
        var checkout = await fixture.Authority.GetIntegrationReadinessAsync(fixture.TenantId, CarrierCodes.Bosta, purpose: CarrierIntegrationPurpose.StorefrontAdmission);
        Assert.False(checkout.CanExecute); Assert.DoesNotContain("BOSTA_PICKUP_VERIFICATION_REQUIRED", checkout.Blockers);
        var booking = await fixture.Authority.GetIntegrationReadinessAsync(fixture.TenantId, CarrierCodes.Bosta, purpose: CarrierIntegrationPurpose.ShipmentCreation);
        Assert.Contains("BOSTA_PICKUP_VERIFICATION_REQUIRED", booking.Blockers);
        Assert.DoesNotContain("BOSTA_BUSINESS_LOCATION_REQUIRED", checkout.Blockers);
    }

    [Fact]
    public async Task VerifiedDefaultRemovesOnlyPickupBlocker_WithoutHttpInsideReadinessOrAccountActivation()
    {
        using var cache = new BostaPickupVerificationCache(); await using var fixture = CreateFixture(cache);
        await fixture.Authority.ValidateAndStoreIntegrationAsync(fixture.TenantId, CarrierCodes.Bosta,
            CreateUpdate(apiKey: "synthetic-default") with { Configuration = "{}", IsEnabled = false });
        Assert.True((await fixture.Authority.ValidateResolvedCredentialsAsync(fixture.TenantId, CarrierCodes.Bosta)).Success);
        var account = new CarrierAccount { Id = Guid.NewGuid(), TenantId = fixture.TenantId, CarrierCode = "bosta",
            ActiveCredentialReferenceId = Guid.NewGuid(), Environment = CarrierAccountEnvironment.Production, State = CarrierAccountState.Disabled };
        fixture.Db.CarrierAccounts.Add(account); await fixture.Db.SaveChangesAsync();
        var resolution = await fixture.Authority.ResolveConnectionCredentialsAsync(fixture.TenantId, "bosta");
        cache.Observe(fixture.TenantId, resolution.Configuration!.Id, account.Id, account.ActiveCredentialReferenceId, resolution.Credentials!,
            new(true, [new("SYNTHETIC-DEFAULT", true, "EG", "CITY", "Test city", "DISTRICT", "Test district") { CountryId = "COUNTRY" }], null));
        var ready = await fixture.Authority.GetIntegrationReadinessAsync(fixture.TenantId, "bosta", purpose: CarrierIntegrationPurpose.ShipmentCreation);
        Assert.DoesNotContain("BOSTA_PICKUP_VERIFICATION_REQUIRED", ready.Blockers);
        Assert.Contains("CARRIER_CONFIGURATION_DISABLED", ready.Blockers); Assert.DoesNotContain("CARRIER_GEOGRAPHY_MAPPING_UNPUBLISHED", ready.Blockers);
        Assert.Contains("BOSTA_PROVIDER_ACCOUNT_ELIGIBILITY_UNVERIFIED", ready.Blockers);
        Assert.False(ready.CanExecute); Assert.Equal(CarrierAccountState.Disabled, account.State);
        account.ActiveCredentialReferenceId = Guid.NewGuid(); await fixture.Db.SaveChangesAsync();
        Assert.Contains("BOSTA_PICKUP_VERIFICATION_REQUIRED", (await fixture.Authority.GetIntegrationReadinessAsync(fixture.TenantId, "bosta", purpose: CarrierIntegrationPurpose.ShipmentCreation)).Blockers);
    }

    [Fact]
    public async Task CredentialRotation_InvalidatesVersionBoundValidation_UntilCurrentCredentialsValidate()
    {
        await using var fixture = CreateFixture();

        await fixture.Authority.ValidateAndStoreIntegrationAsync(
            fixture.TenantId,
            CarrierCodes.Bosta,
            CreateUpdate(apiKey: "credential-a", webhookSecret: "webhook-a"));
        Assert.False((await ReadinessAsync(fixture)).CanExecute);

        fixture.Adapter.ValidationResult = new CarrierValidationResult { Success = true };
        Assert.True((await fixture.Authority.ValidateResolvedCredentialsAsync(
            fixture.TenantId,
            CarrierCodes.Bosta)).Success);
        Assert.True((await ReadinessAsync(fixture)).CanExecute);

        var rotated = await fixture.Authority.ValidateAndStoreIntegrationAsync(
            fixture.TenantId,
            CarrierCodes.Bosta,
            CreateUpdate(apiKey: "credential-b", webhookSecret: "webhook-b"));

        Assert.False(rotated.Configuration!.ValidationMatchesCurrentConfiguration);
        Assert.False((await ReadinessAsync(fixture)).CanExecute);
        var persisted = await fixture.Db.CarrierConfigurations.IgnoreQueryFilters().SingleAsync();
        Assert.Null(persisted.LastValidationSucceeded);
        Assert.Null(persisted.LastValidatedConfigurationFingerprint);

        Assert.True((await fixture.Authority.ValidateResolvedCredentialsAsync(
            fixture.TenantId,
            CarrierCodes.Bosta)).Success);
        Assert.True((await ReadinessAsync(fixture)).CanExecute);
    }

    [Fact]
    public async Task FailedValidation_RemainsBoundToCurrentVersion_AndReadinessFailsClosed()
    {
        await using var fixture = CreateFixture();
        await fixture.Authority.ValidateAndStoreIntegrationAsync(
            fixture.TenantId,
            CarrierCodes.Bosta,
            CreateUpdate(apiKey: "credential-a"));
        fixture.Adapter.ValidationResult = new CarrierValidationResult
        {
            Success = false,
            ErrorCode = "PROVIDER_UNAVAILABLE",
            ErrorMessage = "Provider unavailable"
        };

        var validation = await fixture.Authority.ValidateResolvedCredentialsAsync(
            fixture.TenantId,
            CarrierCodes.Bosta);

        Assert.False(validation.Success);
        Assert.False((await ReadinessAsync(fixture)).CanExecute);
        var persisted = await fixture.Db.CarrierConfigurations.IgnoreQueryFilters().SingleAsync();
        Assert.False(persisted.LastValidationSucceeded);
        Assert.Equal(persisted.CredentialConfigurationFingerprint, persisted.LastValidatedConfigurationFingerprint);
    }

    [Fact]
    public async Task ProviderTimeout_AfterRotation_CannotRestorePriorValidationEvidence()
    {
        await using var fixture = CreateFixture();
        await fixture.Authority.ValidateAndStoreIntegrationAsync(
            fixture.TenantId,
            CarrierCodes.Bosta,
            CreateUpdate(apiKey: "credential-a"));
        Assert.True((await fixture.Authority.ValidateResolvedCredentialsAsync(
            fixture.TenantId,
            CarrierCodes.Bosta)).Success);

        await fixture.Authority.ValidateAndStoreIntegrationAsync(
            fixture.TenantId,
            CarrierCodes.Bosta,
            CreateUpdate(apiKey: "credential-b"));
        fixture.Adapter.ValidationException = new TimeoutException("provider timed out");

        var failed = await fixture.Authority.ValidateResolvedCredentialsAsync(fixture.TenantId, CarrierCodes.Bosta);
        Assert.False(failed.Success);
        Assert.Equal("CONNECTION_TIMEOUT", failed.ErrorCode);

        var persisted = await fixture.Db.CarrierConfigurations.IgnoreQueryFilters().SingleAsync();
        Assert.False(persisted.LastValidationSucceeded);
        Assert.Equal(persisted.CredentialConfigurationFingerprint, persisted.LastValidatedConfigurationFingerprint);
        Assert.False((await ReadinessAsync(fixture)).CanExecute);
    }

    [Fact]
    public async Task WebhookSecret_IsEncryptedWriteOnlyRotatableAndNeverLogged()
    {
        await using var fixture = CreateFixture();

        var stored = await fixture.Authority.ValidateAndStoreIntegrationAsync(
            fixture.TenantId,
            CarrierCodes.Bosta,
            CreateUpdate(apiKey: "credential-a", webhookSecret: "super-secret-a"));
        var persisted = await fixture.Db.CarrierConfigurations.IgnoreQueryFilters().SingleAsync();
        var trust = await fixture.Authority.ResolveWebhookTrustAsync(fixture.TenantId, CarrierCodes.Bosta);

        Assert.StartsWith("ENC:", persisted.WebhookSecret, StringComparison.Ordinal);
        Assert.True(stored.Configuration!.HasWebhookSecret);
        Assert.Equal("super-secret-a", trust.WebhookSecret);
        Assert.DoesNotContain("super-secret-a", fixture.Logger.Messages, StringComparer.Ordinal);

        var json = JsonSerializer.Serialize(new CarrierConfigurationDto
        {
            CarrierCode = CarrierCodes.Bosta,
            DisplayName = "Bosta",
            HasWebhookSecret = true
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("\"webhookSecret\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"hasWebhookSecret\":true", json, StringComparison.OrdinalIgnoreCase);

        await fixture.Authority.ValidateAndStoreIntegrationAsync(
            fixture.TenantId,
            CarrierCodes.Bosta,
            CreateUpdate(webhookSecret: "super-secret-b"));
        var rotatedTrust = await fixture.Authority.ResolveWebhookTrustAsync(fixture.TenantId, CarrierCodes.Bosta);
        Assert.Equal("super-secret-b", rotatedTrust.WebhookSecret);
        Assert.DoesNotContain("super-secret-b", fixture.Logger.Messages, StringComparer.Ordinal);
    }

    [Fact]
    public async Task LegacyWebhookSecretBackfill_EncryptsInPlace_AndInvalidatesOldTrustEvidence()
    {
        await using var fixture = CreateFixture();
        fixture.Db.CarrierConfigurations.Add(new CarrierConfiguration
        {
            Id = Guid.NewGuid(),
            TenantId = fixture.TenantId,
            CarrierCode = CarrierCodes.Bosta,
            DisplayName = "Bosta",
            IsEnabled = true,
            SupportsWebhooks = true,
            WebhookSecret = "legacy-plain-secret",
            CredentialConfigurationFingerprint = "old-current",
            LastValidatedConfigurationFingerprint = "old-validated",
            LastValidatedAtUtc = DateTime.UtcNow,
            LastValidationSucceeded = true
        });
        await fixture.Db.SaveChangesAsync();

        var changed = await CarrierWebhookSecretBackfillHostedService.ProtectLegacySecretsAsync(
            fixture.Db,
            new TestEncryptionService(),
            CancellationToken.None);

        var persisted = await fixture.Db.CarrierConfigurations.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(1, changed);
        Assert.Equal("ENC:legacy-plain-secret", persisted.WebhookSecret);
        Assert.Null(persisted.CredentialConfigurationFingerprint);
        Assert.Null(persisted.LastValidatedConfigurationFingerprint);
        Assert.Null(persisted.LastValidatedAtUtc);
        Assert.Null(persisted.LastValidationSucceeded);
    }

    [Fact]
    public async Task WebhookTrust_IsTenantIsolated()
    {
        await using var fixture = CreateFixture();
        var otherTenantId = Guid.NewGuid();
        await fixture.Authority.ValidateAndStoreIntegrationAsync(
            fixture.TenantId,
            CarrierCodes.Bosta,
            CreateUpdate(apiKey: "tenant-a-key", webhookSecret: "tenant-a-secret"));
        await fixture.Authority.ValidateAndStoreIntegrationAsync(
            otherTenantId,
            CarrierCodes.Bosta,
            CreateUpdate(apiKey: "tenant-b-key", webhookSecret: "tenant-b-secret"));

        var tenantATrust = await fixture.Authority.ResolveWebhookTrustAsync(
            fixture.TenantId,
            CarrierCodes.Bosta);
        var tenantBTrust = await fixture.Authority.ResolveWebhookTrustAsync(
            otherTenantId,
            CarrierCodes.Bosta);

        Assert.Equal("tenant-a-secret", tenantATrust.WebhookSecret);
        Assert.Equal("tenant-b-secret", tenantBTrust.WebhookSecret);
        Assert.NotEqual(tenantATrust.WebhookSecret, tenantBTrust.WebhookSecret);
    }

    [Fact]
    public async Task ServiceLevelAuthority_RejectsUnsupportedDisabledAndCrossTenantPolicy()
    {
        await using var fixture = CreateFixture();
        var accountId = Guid.NewGuid();
        fixture.Db.CarrierAccounts.Add(new CarrierAccount
        {
            Id = accountId,
            TenantId = fixture.TenantId,
            CarrierCode = CarrierCodes.Bosta
        });
        fixture.Db.CarrierServiceLevelEnablements.Add(new CarrierServiceLevelEnablement
        {
            Id = Guid.NewGuid(),
            TenantId = fixture.TenantId,
            CarrierAccountId = accountId,
            ServiceLevelCode = "standard",
            IsEnabled = false
        });
        fixture.Db.CarrierServiceLevelEnablements.Add(new CarrierServiceLevelEnablement
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            CarrierAccountId = Guid.NewGuid(),
            ServiceLevelCode = "express",
            IsEnabled = true
        });
        await fixture.Db.SaveChangesAsync();

        var disabled = await fixture.Authority.GetServiceLevelReadinessAsync(
            fixture.TenantId,
            CarrierCodes.Bosta,
            "standard");
        var unsupported = await fixture.Authority.GetServiceLevelReadinessAsync(
            fixture.TenantId,
            CarrierCodes.Bosta,
            "invented-level");
        var crossTenantOnly = await fixture.Authority.GetServiceLevelReadinessAsync(
            fixture.TenantId,
            CarrierCodes.Bosta,
            "express");

        Assert.Contains("SERVICE_LEVEL_DISABLED", disabled.Blockers);
        Assert.Contains("SERVICE_LEVEL_UNSUPPORTED", unsupported.Blockers);
        Assert.Contains("SERVICE_LEVEL_DISABLED", crossTenantOnly.Blockers);
    }

    [Fact]
    public void CapabilityRegistry_AdvertisesWebhooksOnlyForImplementedBostaAdapter()
    {
        using var fixture = CreateFixture();

        Assert.True(fixture.Capabilities.GetCapability(CarrierCodes.Bosta)!.SupportsWebhooks);
        Assert.False(fixture.Capabilities.GetCapability(CarrierCodes.Aramex)!.SupportsWebhooks);
        Assert.False(fixture.Capabilities.GetCapability(CarrierCodes.Dhl)!.SupportsWebhooks);
    }

    [Fact]
    public void DhlCapability_RetainsCurrentPlatformMappingPolicyAndBlocksWebhooks()
    {
        using var fixture = CreateFixture();

        var dhl = fixture.Capabilities.GetCapability(CarrierCodes.Dhl)!;
        Assert.True(dhl.RequiresGeographyMapping); // Platform policy, not a MyDHL wire requirement.
        Assert.False(dhl.SupportsWebhooks);
        Assert.True(dhl.CredentialFields.Single(f => f.Target == "ApiKey").IsSecret);
    }

    [Fact]
    public void BostaCapability_ExposesOnlySupportedSetupAuthorityFields()
    {
        using var fixture = CreateFixture();
        var bosta = fixture.Capabilities.GetCapability(CarrierCodes.Bosta)!;

        Assert.Equal(
            new[] { "ApiKey", "WebhookSecret", "ExtraConfig.codCollectionCurrency", "ExtraConfig.businessLocationId" },
            bosta.CredentialFields.Select(field => field.Target));
        Assert.True(bosta.CredentialFields.Single(field => field.Target == "ApiKey").Required);
        Assert.False(bosta.CredentialFields.Single(field => field.Target == "WebhookSecret").Required);
        Assert.False(bosta.CredentialFields.Single(field => field.Target == "ExtraConfig.codCollectionCurrency").Required);
        Assert.False(bosta.CredentialFields.Single(field => field.Target == "ExtraConfig.businessLocationId").Required);
        Assert.False(bosta.RequiresOriginReadiness);
    }

    [Fact]
    public async Task NewBostaConfiguration_UsesImplementedCapabilitiesAndRequiresExplicitActivationNotNationalMapping()
    {
        await using var fixture = CreateFixture();
        await fixture.Authority.ValidateAndStoreIntegrationAsync(
            fixture.TenantId,
            CarrierCodes.Bosta,
            CreateUpdate(apiKey: "credential-a", webhookSecret: "webhook-a"));
        Assert.True((await fixture.Authority.ValidateResolvedCredentialsAsync(fixture.TenantId, CarrierCodes.Bosta)).Success);

        var config = await fixture.Db.CarrierConfigurations.IgnoreQueryFilters().SingleAsync();
        Assert.True(config.SupportsCod);
        Assert.True(config.SupportsTracking);
        Assert.True(config.SupportsWebhooks);
        Assert.True(config.SupportsCancellation);
        Assert.False(config.SupportsLabels);
        Assert.False(config.SupportsQuotes);

        var readiness = await fixture.Authority.GetIntegrationReadinessAsync(
            fixture.TenantId,
            CarrierCodes.Bosta,
            purpose: CarrierIntegrationPurpose.StorefrontAdmission);
        Assert.False(readiness.CanExecute);
        Assert.DoesNotContain("CARRIER_GEOGRAPHY_MAPPING_UNPUBLISHED", readiness.Blockers);
        Assert.Contains("BOSTA_INTEGRATION_ACTIVATION_REQUIRED", readiness.Blockers);
        Assert.DoesNotContain("CARRIER_CONNECTION_NOT_VERIFIED", readiness.Blockers);
    }

    [Fact]
    public async Task AramexReadiness_BlocksStorefrontWithoutPublishedMapping()
    {
        await using var fixture = CreateFixture();
        await fixture.Authority.ValidateAndStoreIntegrationAsync(
            fixture.TenantId,
            CarrierCodes.Aramex,
            CreateAramexUpdate());
        Assert.True((await fixture.Authority.ValidateResolvedCredentialsAsync(
            fixture.TenantId,
            CarrierCodes.Aramex)).Success);

        var readiness = await fixture.Authority.GetIntegrationReadinessAsync(
            fixture.TenantId,
            CarrierCodes.Aramex,
            purpose: CarrierIntegrationPurpose.StorefrontAdmission);

        Assert.False(readiness.CanExecute);
        Assert.Contains("CARRIER_GEOGRAPHY_MAPPING_UNPUBLISHED", readiness.Blockers);
        Assert.DoesNotContain("CARRIER_CONNECTION_NOT_VERIFIED", readiness.Blockers);
    }

    [Fact]
    public async Task Dhl_LocalCredentialSuccessCannotCertifyRemoteAccountOrEnableCreation()
    {
        await using var fixture = CreateFixture();
        await fixture.Authority.ValidateAndStoreIntegrationAsync(fixture.TenantId, CarrierCodes.Dhl,
            CreateUpdate(apiKey: "dhl-key") with { ApiSecret = "dhl-secret", AccountId = "123456789", SupportsWebhooks = false });
        Assert.True((await fixture.Authority.ValidateResolvedCredentialsAsync(fixture.TenantId, CarrierCodes.Dhl)).Success);
        var readiness = await fixture.Authority.GetIntegrationReadinessAsync(fixture.TenantId, CarrierCodes.Dhl,
            purpose: CarrierIntegrationPurpose.ExternalRegistration);
        Assert.False(readiness.CanExecute);
        Assert.Contains("DHL_REMOTE_ACCOUNT_CERTIFICATION_REQUIRED", readiness.Blockers);
        var otherTenant = await fixture.Authority.GetIntegrationReadinessAsync(Guid.NewGuid(), CarrierCodes.Dhl,
            purpose: CarrierIntegrationPurpose.ExternalRegistration);
        Assert.Contains("CARRIER_CONFIGURATION_MISSING", otherTenant.Blockers);
    }

    private static async Task<CarrierIntegrationReadiness> ReadinessAsync(Fixture fixture) =>
        await fixture.Authority.GetIntegrationReadinessAsync(
            fixture.TenantId,
            CarrierCodes.Bosta,
            purpose: CarrierIntegrationPurpose.WebhookTrust);

    private static CarrierIntegrationUpdate CreateUpdate(
        string? apiKey = null,
        string? webhookSecret = null) =>
        new(
            DisplayName: "Bosta",
            IsEnabled: true,
            IsDefault: null,
            ApiKey: apiKey,
            ApiSecret: null,
            AccountId: null,
            ApiUrl: null,
            UseSandbox: false,
            SupportsCod: null,
            SupportsTracking: null,
            SupportsWebhooks: true,
            SupportsLabels: null,
            Configuration: "{\"codCollectionCurrency\":\"EGP\",\"businessLocationId\":\"business-location-1\"}",
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
            Notes: null,
            WebhookSecret: webhookSecret);

    private static CarrierIntegrationUpdate CreateAramexUpdate() =>
        new(
            DisplayName: "Aramex",
            IsEnabled: true,
            IsDefault: null,
            ApiKey: "sender@example.com",
            ApiSecret: "password",
            AccountId: "12345",
            ApiUrl: null,
            UseSandbox: true,
            SupportsCod: true,
            SupportsTracking: true,
            SupportsWebhooks: false,
            SupportsLabels: true,
            Configuration: "{\"AccountPin\":\"1234\",\"AccountEntity\":\"CAI\",\"AccountCountryCode\":\"EG\"}",
            PickupAddress: null,
            PickupCity: null,
            PickupCountry: null,
            PickupPhone: null,
            PickupContactName: null,
            DefaultServiceLevel: "standard",
            UseCarrierRates: true,
            FixedShippingCost: null,
            MarkupPercentage: null,
            FreeShippingThreshold: null,
            SortOrder: null,
            Notes: null,
            WebhookSecret: null);

    private static Fixture CreateFixture(BostaPickupVerificationCache? cache = null)
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"CarrierAuthorityRemediation_{Guid.NewGuid():N}")
            .Options;
        var db = new ApplicationDbContext(options);
        var adapter = new TestCarrierAdapter();
        var registry = new Mock<IShippingCarrierRegistry>(MockBehavior.Strict);
        registry.Setup(service => service.GetAdapter(It.IsAny<string>())).Returns(adapter);
        registry.Setup(service => service.GetAllAdapters()).Returns([adapter]);
        var capabilities = new CarrierCapabilityRegistry(registry.Object);
        var encryption = new TestEncryptionService();
        var logger = new CapturingLogger<CarrierIntegrationAuthority>();
        var authority = new CarrierIntegrationAuthority(
            db,
            registry.Object,
            capabilities,
            encryption,
            logger, cache);
        return new Fixture(tenantId, db, adapter, capabilities, authority, logger);
    }

    private sealed record Fixture(
        Guid TenantId,
        ApplicationDbContext Db,
        TestCarrierAdapter Adapter,
        CarrierCapabilityRegistry Capabilities,
        CarrierIntegrationAuthority Authority,
        CapturingLogger<CarrierIntegrationAuthority> Logger) : IDisposable, IAsyncDisposable
    {
        public void Dispose() => Db.Dispose();
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class TestEncryptionService : ICarrierCredentialEncryptionService
    {
        public string Encrypt(string plainText) => IsEncrypted(plainText) ? plainText : $"ENC:{plainText}";
        public string Decrypt(string? encryptedOrPlainText) =>
            encryptedOrPlainText?.StartsWith("ENC:", StringComparison.Ordinal) == true
                ? encryptedOrPlainText[4..]
                : encryptedOrPlainText ?? string.Empty;
        public bool IsEncrypted(string? value) => value?.StartsWith("ENC:", StringComparison.Ordinal) == true;
    }

    private sealed class TestCarrierAdapter : IShippingCarrierAdapter
    {
        public string CarrierCode => CarrierCodes.Bosta;
        public string DisplayName => "Bosta";
        public CarrierValidationResult ValidationResult { get; set; } = new() { Success = true };
        public Exception? ValidationException { get; set; }
        public Task<ShippingQuoteResult> GetQuoteAsync(ShippingQuoteRequest request, CarrierCredentials credentials, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CreateShipmentResult> CreateShipmentAsync(CreateShipmentRequest request, CarrierCredentials credentials, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CancelShipmentResult> CancelShipmentAsync(string trackingNumber, CarrierCredentials credentials, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShipmentTrackingResult> GetTrackingAsync(string trackingNumber, CarrierCredentials credentials, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CarrierValidationResult> ValidateCredentialsAsync(CarrierCredentials credentials, CancellationToken cancellationToken = default) =>
            ValidationException == null
                ? Task.FromResult(ValidationResult)
                : Task.FromException<CarrierValidationResult>(ValidationException);
        public WebhookParseResult? ParseWebhook(string payload, string? signature, string? webhookSecret) => null;
        public IReadOnlyList<ServiceLevelInfo> GetSupportedServiceLevels() =>
        [
            new() { Code = "standard", Name = "Standard" },
            new() { Code = "express", Name = "Express" }
        ];
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
