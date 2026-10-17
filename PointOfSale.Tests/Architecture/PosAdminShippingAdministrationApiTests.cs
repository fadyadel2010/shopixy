using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.ShippingAdministration;
using PointOfSale.API.Services.Admin;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.ShippingAdministration;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.ShippingAdministration;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Administration;

namespace PointOfSale.Tests.Architecture;

public sealed class PosAdminShippingAdministrationApiTests
{
    [Fact]
    public void Controller_MustUsePosAdminRouteAndAuthorization()
    {
        var route = typeof(AdminShippingAdministrationController).GetCustomAttribute<RouteAttribute>();
        var authorize = typeof(AdminShippingAdministrationController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(route);
        Assert.Equal("api/admin/shipping", route!.Template);
        Assert.NotNull(authorize);
        Assert.Equal("Owner,Admin,Manager", authorize!.Roles);
        Assert.DoesNotContain("SuperAdmin", authorize.Roles, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dtos_MustNotExposeSecretsProviderIdsOrRuntimeInternals()
    {
        var responseDtoTypes = new[]
        {
            typeof(CarrierAccountAdministrationDto),
            typeof(CarrierCredentialReferenceDto),
            typeof(CarrierWarehouseAssignmentDto),
            typeof(CarrierPolicyDto),
            typeof(CarrierConnectionTestDto),
            typeof(CarrierAdministrationAuditDto),
            typeof(ShippingAdministrationResultDto),
            typeof(SupportedShippingCarrierDto),
            typeof(ShippingAdministrationServiceLevelDto)
        };

        var exposedNames = responseDtoTypes
            .Where(type => type.Namespace == "PointOfSale.API.DTOs.ShippingAdministration")
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .Select(property => property.Name)
            .ToList();

        Assert.DoesNotContain("ApiKey", exposedNames);
        Assert.DoesNotContain("ApiSecret", exposedNames);
        Assert.DoesNotContain("SecretReference", exposedNames);
        Assert.DoesNotContain("ProviderId", exposedNames);
        Assert.DoesNotContain("ShipmentSnapshot", exposedNames);
        Assert.DoesNotContain("CarrierMetadata", exposedNames);
        Assert.Contains("MaskedFingerprint", exposedNames);
    }

    [Fact]
    public async Task CommandsAndQueries_ShouldUseAdministrationRuntimeOnlyAndKeepAuditAppendOnlyForMutations()
    {
        await using var db = CreateDbContext();
        var service = CreateApiService(db, new FakeCarrierAdapter());
        var tenantId = Guid.NewGuid();
        var warehouseId = SeedWarehouse(db, tenantId);
        var actor = new CarrierAdministrationActor("admin-user");

        var created = await service.CreateCarrierAccountAsync(
            tenantId,
            new CreateCarrierAccountCommand(CarrierCodes.Bosta),
            actor,
            "corr-create",
            CancellationToken.None);
        Assert.True(created.Succeeded);

        var accountId = created.CarrierAccountId!.Value;
        await service.UpdateCredentialReferenceAsync(
            tenantId,
            accountId,
            new CredentialReferenceCommand("vault://tenant/bosta/active", "****1234", null),
            actor,
            "corr-credential",
            CancellationToken.None);
        await service.SelectEnvironmentAsync(
            tenantId,
            accountId,
            new EnvironmentCommand("Sandbox"),
            actor,
            "corr-env",
            CancellationToken.None);
        await service.RunConnectionTestAsync(
            tenantId,
            accountId,
            new ConnectionTestCommand("plaintext-boundary-input", null, null, null, true,
                new Dictionary<string, string>
                {
                    ["codCollectionCurrency"] = "EGP",
                    ["businessLocationId"] = "business-location-1"
                }, "test-webhook-authorization"),
            actor,
            "corr-test",
            CancellationToken.None);
        await service.AssignWarehouseAsync(
            tenantId,
            accountId,
            new WarehouseAssignmentCommand(warehouseId, true, "origin-ready"),
            actor,
            "corr-warehouse",
            CancellationToken.None);
        await service.SetServiceLevelAsync(
            tenantId,
            accountId,
            "standard",
            new ServiceLevelCommand(true),
            actor,
            "corr-service",
            CancellationToken.None);

        var auditAfterMutations = await db.CarrierAdministrationAudit.IgnoreQueryFilters().CountAsync();
        Assert.True(auditAfterMutations >= 6);

        var list = await service.ListCarrierAccountsAsync(tenantId, CancellationToken.None);
        var detail = await service.GetCarrierAccountAsync(tenantId, accountId, CancellationToken.None);
        var auditAfterReads = await db.CarrierAdministrationAudit.IgnoreQueryFilters().CountAsync();

        Assert.Single(list);
        Assert.NotNull(detail);
        Assert.Equal(auditAfterMutations, auditAfterReads);
        Assert.DoesNotContain(detail!.CredentialReferences, credential => credential.GetType().GetProperty("SecretReference") != null);
        Assert.Equal("********", detail.CredentialReferences.Single(reference => reference.State == "Active").MaskedFingerprint);
        Assert.Contains(detail.CredentialReferences, reference => reference.MaskedFingerprint == "****1234" && reference.State == "Rotating");
    }

    [Fact]
    public async Task BostaExplicitIntegrationActivation_DoesNotClaimProviderOrShipmentReadiness()
    {
        await using var db = CreateDbContext();
        var service = CreateApiService(db, new FakeCarrierAdapter());
        var tenantId = Guid.NewGuid();
        var warehouseId = SeedWarehouse(db, tenantId);
        var actor = new CarrierAdministrationActor("admin-user");
        var accountId = await PrepareConnectionTestPendingAccountAsync(service, tenantId, warehouseId, actor);

        var blocked = await service.ActivateAsync(
            tenantId,
            accountId,
            new ReadinessEvidenceCommand(null, true),
            actor,
            "corr-activate",
            CancellationToken.None);

        Assert.True(blocked.Succeeded);
        Assert.Contains("BOSTA_PROVIDER_ACCOUNT_ELIGIBILITY_UNVERIFIED", blocked.Blockers);
        var readback = await service.GetCarrierAccountAsync(tenantId, accountId, default);
        Assert.Equal("Active", readback!.State);
        Assert.True(readback.IntegrationReadiness!.Enabled);
        Assert.False(readback.IntegrationReadiness.CanExecute);
        Assert.Contains(
            await db.CarrierAdministrationAudit.IgnoreQueryFilters().ToListAsync(),
            audit => audit.Action == CarrierAdministrationAuditAction.Activation);
    }

    [Fact]
    public async Task Validation_ShouldRejectUnknownEnvironmentWarehousePolicyServiceLevelAndCredentialReference()
    {
        await using var db = CreateDbContext();
        var service = CreateApiService(db, new FakeCarrierAdapter());
        var tenantId = Guid.NewGuid();
        var actor = new CarrierAdministrationActor("admin-user");
        var account = await service.CreateCarrierAccountAsync(tenantId, new CreateCarrierAccountCommand(CarrierCodes.Bosta), actor, "corr", CancellationToken.None);
        var accountId = account.CarrierAccountId!.Value;

        Assert.Equal("UNKNOWN_ENVIRONMENT", (await service.SelectEnvironmentAsync(tenantId, accountId, new EnvironmentCommand("Mars"), actor, "corr-env", CancellationToken.None)).FailureCode);
        Assert.Equal("UNKNOWN_WAREHOUSE", (await service.AssignWarehouseAsync(tenantId, accountId, new WarehouseAssignmentCommand(Guid.Empty, true, null), actor, "corr-wh", CancellationToken.None)).FailureCode);
        Assert.Equal("UNKNOWN_POLICY", (await service.SetPolicyAsync(tenantId, accountId, "Domestic", new PolicyCommand("Maybe", false, null), actor, "corr-pol", CancellationToken.None)).FailureCode);
        Assert.Equal("UNKNOWN_SERVICE_LEVEL", (await service.SetServiceLevelAsync(tenantId, accountId, "", new ServiceLevelCommand(true), actor, "corr-svc", CancellationToken.None)).FailureCode);
        Assert.Equal("UNKNOWN_CREDENTIAL_REFERENCE", (await service.UpdateCredentialReferenceAsync(tenantId, accountId, new CredentialReferenceCommand("", null, null), actor, "corr-cred", CancellationToken.None)).FailureCode);
    }

    [Fact]
    public async Task CreateCarrierAccount_ShouldRejectManualFulfillmentCarrier()
    {
        await using var db = CreateDbContext();
        var service = CreateApiService(db, new FakeCarrierAdapter());
        var tenantId = Guid.NewGuid();
        var actor = new CarrierAdministrationActor("admin-user");

        var result = await service.CreateCarrierAccountAsync(
            tenantId,
            new CreateCarrierAccountCommand(CarrierCodes.Manual),
            actor,
            "corr-manual",
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("MANUAL_CARRIER_ACCOUNT_NOT_SUPPORTED", result.FailureCode);
        Assert.Empty(await db.CarrierAccounts.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public void ApiLayer_ShouldNotMutateShipmentRuntimeOrCarrierAdapters()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminShippingAdministrationController.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Admin/ShippingAdministrationApiService.cs");

        Assert.DoesNotContain("CarrierShipmentService", controller + service, StringComparison.Ordinal);
        Assert.DoesNotContain("ICarrierShipmentService", controller + service, StringComparison.Ordinal);
        Assert.DoesNotContain("DbSet<Shipment>", controller + service, StringComparison.Ordinal);
        Assert.DoesNotContain(".Shipments", controller + service, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateShipmentAsync", controller + service, StringComparison.Ordinal);
        Assert.DoesNotContain("GetTrackingAsync", controller + service, StringComparison.Ordinal);
        Assert.DoesNotContain("CancelShipmentAsync", controller + service, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_ShouldExistAndRecordRequiredApiCertification()
    {
        var report = CertificationSource.ReadRepoFile("docs/architecture/POSADMIN_SHIPPING_2_ADMINISTRATION_APIS.md");

        foreach (var heading in new[]
                 {
                     "Executive Summary",
                     "API Surface",
                     "Endpoint Matrix",
                     "Commands",
                     "Queries",
                     "DTO Contracts",
                     "Authorization",
                     "Validation",
                     "Audit",
                     "Authority Matrix",
                     "Regression Coverage",
                     "Verification",
                     "Final Recommendation"
                 })
        {
            Assert.Contains($"## {heading}", report, StringComparison.Ordinal);
        }

        Assert.Contains("Reviewed ONLY Administration APIs? **Yes.**", report, StringComparison.Ordinal);
        Assert.Contains("Shipment Runtime modified? **No.**", report, StringComparison.Ordinal);
        Assert.Contains("Carrier Adapters modified? **No.**", report, StringComparison.Ordinal);
    }

    private static async Task<Guid> PrepareConnectionTestPendingAccountAsync(
        IShippingAdministrationApiService service,
        Guid tenantId,
        Guid warehouseId,
        CarrierAdministrationActor actor)
    {
        var created = await service.CreateCarrierAccountAsync(tenantId, new CreateCarrierAccountCommand(CarrierCodes.Bosta), actor, "corr-a", CancellationToken.None);
        var accountId = created.CarrierAccountId!.Value;
        await service.UpdateCredentialReferenceAsync(tenantId, accountId, new CredentialReferenceCommand("vault://tenant/bosta/active", "****1234", null), actor, "corr-b", CancellationToken.None);
        await service.SelectEnvironmentAsync(tenantId, accountId, new EnvironmentCommand("Sandbox"), actor, "corr-c", CancellationToken.None);
        await service.RunConnectionTestAsync(tenantId, accountId, new ConnectionTestCommand("test", null, null, null, true,
            new Dictionary<string, string>
            {
                ["codCollectionCurrency"] = "EGP",
                ["businessLocationId"] = "business-location-1"
            }, "test-webhook-authorization"), actor, "corr-d", CancellationToken.None);
        await service.AssignWarehouseAsync(tenantId, accountId, new WarehouseAssignmentCommand(warehouseId, true, "origin-ready"), actor, "corr-e", CancellationToken.None);
        await service.SetServiceLevelAsync(tenantId, accountId, "standard", new ServiceLevelCommand(true), actor, "corr-f", CancellationToken.None);
        return accountId;
    }

    private static Guid SeedWarehouse(ApplicationDbContext db, Guid tenantId)
    {
        var warehouseId = Guid.NewGuid();
        db.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Main",
            IsActive = true
        });
        db.SaveChanges();
        return warehouseId;
    }

    private static ShippingAdministrationApiService CreateApiService(ApplicationDbContext db, params IShippingCarrierAdapter[] adapters)
    {
        var registry = new ShippingCarrierRegistry(adapters, NullLogger<ShippingCarrierRegistry>.Instance);
        var runtime = new CarrierAccountAdministrationRuntime(db, registry, NullLogger<CarrierAccountAdministrationRuntime>.Instance);
        var capabilityRegistry = new CarrierCapabilityRegistry(registry);
        var authority = new CarrierIntegrationAuthority(
            db,
            registry,
            capabilityRegistry,
            new FakeCredentialEncryptionService(),
            NullLogger<CarrierIntegrationAuthority>.Instance);
        return new ShippingAdministrationApiService(db, runtime, registry, capabilityRegistry, authority);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class FakeCarrierAdapter : IShippingCarrierAdapter, IBostaPickupLocationDiscovery
    {
        public string CarrierCode => CarrierCodes.Bosta;
        public string DisplayName => "Bosta";
        public Task<BostaPickupLocationResult> GetPickupLocationsAsync(CarrierCredentials credentials, CancellationToken ct = default) =>
            Task.FromResult(new BostaPickupLocationResult(true, [new("business-location-1", true, "EG", "synthetic-city", "Synthetic city", "synthetic-district", "Synthetic district") {CountryId = "synthetic-country"}], null));

        public Task<ShippingQuoteResult> GetQuoteAsync(ShippingQuoteRequest request, CarrierCredentials credentials, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ShippingQuoteResult { Success = false });

        public Task<CreateShipmentResult> CreateShipmentAsync(CreateShipmentRequest request, CarrierCredentials credentials, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CreateShipmentResult { Success = false });

        public Task<CancelShipmentResult> CancelShipmentAsync(string trackingNumber, CarrierCredentials credentials, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CancelShipmentResult { Success = false });

        public Task<ShipmentTrackingResult> GetTrackingAsync(string trackingNumber, CarrierCredentials credentials, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ShipmentTrackingResult { Success = false, CurrentStatus = ShipmentStatus.Pending });

        public Task<CarrierValidationResult> ValidateCredentialsAsync(CarrierCredentials credentials, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CarrierValidationResult { Success = true });

        public IReadOnlyList<ServiceLevelInfo> GetSupportedServiceLevels() =>
            new[] { new ServiceLevelInfo { Code = "standard", Name = "Standard" } };

        public WebhookParseResult? ParseWebhook(string payload, string? signature, string? webhookSecret) => null;
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
}
