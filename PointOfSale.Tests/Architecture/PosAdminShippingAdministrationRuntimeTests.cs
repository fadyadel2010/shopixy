using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.ShippingAdministration;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.ShippingAdministration;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Administration;

namespace PointOfSale.Tests.Architecture;

public sealed class PosAdminShippingAdministrationRuntimeTests
{
    [Fact]
    public async Task CarrierAccountLifecycle_ShouldRejectIllegalTransitionsAndAudit()
    {
        await using var db = CreateDbContext();
        var adapter = new FakeCarrierAdapter();
        var service = CreateService(db, adapter);
        var tenantId = Guid.NewGuid();
        var actor = new CarrierAdministrationActor("admin");

        var registered = await service.RegisterCarrierAccountAsync(tenantId, CarrierCodes.Bosta, actor, "corr-1");
        Assert.True(registered.Succeeded);

        var illegal = await service.TransitionStateAsync(
            tenantId,
            registered.CarrierAccountId!.Value,
            CarrierAccountState.Active,
            ReadyEvidence(),
            actor,
            "corr-2");
        Assert.False(illegal.Succeeded);
        Assert.Equal("UNKNOWN_STATE_TRANSITION", illegal.FailureCode);

        Assert.Contains(
            await db.CarrierAdministrationAudit.IgnoreQueryFilters().ToListAsync(),
            audit => audit.Action == CarrierAdministrationAuditAction.StateTransitionRejected);
    }

    [Fact]
    public async Task Activation_ShouldFailClosedUntilAllGatesPass()
    {
        await using var db = CreateDbContext();
        var adapter = new FakeCarrierAdapter();
        var service = CreateService(db, adapter);
        var tenantId = Guid.NewGuid();
        var actor = new CarrierAdministrationActor("admin");
        var warehouseId = SeedWarehouse(db, tenantId);

        var accountId = await PrepareReadyForActivationAsync(service, tenantId, warehouseId, actor);

        var blocked = await service.TransitionStateAsync(
            tenantId,
            accountId,
            CarrierAccountState.Active,
            new CarrierReadinessEvidence(null, true),
            actor,
            "corr-blocked");
        Assert.False(blocked.Succeeded);
        Assert.Contains("UNKNOWN_READINESS", blocked.Blockers);

        var activated = await service.TransitionStateAsync(
            tenantId,
            accountId,
            CarrierAccountState.Active,
            ReadyEvidence(),
            actor,
            "corr-active");
        Assert.True(activated.Succeeded);
        Assert.Equal(CarrierAccountState.Active, activated.State);
        Assert.Equal(CarrierAccountHealthState.Healthy, activated.HealthState);
    }

    [Fact]
    public async Task InternationalPolicy_ShouldRequirePublishedCustomsReadiness()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, new FakeCarrierAdapter());
        var tenantId = Guid.NewGuid();
        var actor = new CarrierAdministrationActor("admin");
        var warehouseId = SeedWarehouse(db, tenantId);

        var accountId = await PrepareReadyForActivationAsync(service, tenantId, warehouseId, actor);
        var policy = await service.SetPolicyAsync(
            tenantId,
            new CarrierPolicyCommand(accountId, CarrierPolicyScope.International, CarrierPolicyMode.Enabled, false, null),
            actor,
            "corr-policy");
        Assert.True(policy.Succeeded);

        var blocked = await service.TransitionStateAsync(tenantId, accountId, CarrierAccountState.Active, ReadyEvidence(), actor, "corr-active");
        Assert.False(blocked.Succeeded);
        Assert.Contains("CUSTOMS_READINESS_MISSING", blocked.Blockers);
    }

    [Fact]
    public async Task WarehouseAssignment_ShouldValidateTenantWarehouseLifecycleAndDuplicateDeterminism()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, new FakeCarrierAdapter());
        var tenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        var actor = new CarrierAdministrationActor("admin");
        var accountId = (await service.RegisterCarrierAccountAsync(tenantId, CarrierCodes.Bosta, actor, "corr-account")).CarrierAccountId!.Value;
        var activeWarehouseId = SeedWarehouse(db, tenantId);
        var foreignWarehouseId = SeedWarehouse(db, foreignTenantId);
        var inactiveWarehouseId = SeedWarehouse(db, tenantId, isActive: false);
        var deletedWarehouseId = SeedWarehouse(db, tenantId, isDeleted: true);

        var assigned = await service.AssignWarehouseAsync(
            tenantId,
            new CarrierWarehouseAssignmentCommand(accountId, activeWarehouseId, true, "origin-ready"),
            actor,
            "corr-wh-1");
        Assert.True(assigned.Succeeded);

        var duplicateUpdate = await service.AssignWarehouseAsync(
            tenantId,
            new CarrierWarehouseAssignmentCommand(accountId, activeWarehouseId, false, "origin-not-ready"),
            actor,
            "corr-wh-2");
        Assert.True(duplicateUpdate.Succeeded);

        var activeAssignments = await db.CarrierWarehouseAssignments
            .IgnoreQueryFilters()
            .Where(assignment => assignment.TenantId == tenantId &&
                                 assignment.CarrierAccountId == accountId &&
                                 assignment.WarehouseId == activeWarehouseId &&
                                 !assignment.IsDeleted)
            .ToListAsync();

        Assert.Single(activeAssignments);
        Assert.False(activeAssignments.Single().OriginReadinessPublished);
        Assert.Equal("origin-not-ready", activeAssignments.Single().ReadinessEvidenceReference);

        var foreign = await service.AssignWarehouseAsync(
            tenantId,
            new CarrierWarehouseAssignmentCommand(accountId, foreignWarehouseId, true, null),
            actor,
            "corr-wh-foreign");
        var inactive = await service.AssignWarehouseAsync(
            tenantId,
            new CarrierWarehouseAssignmentCommand(accountId, inactiveWarehouseId, true, null),
            actor,
            "corr-wh-inactive");
        var deleted = await service.AssignWarehouseAsync(
            tenantId,
            new CarrierWarehouseAssignmentCommand(accountId, deletedWarehouseId, true, null),
            actor,
            "corr-wh-deleted");

        Assert.Equal("UNKNOWN_WAREHOUSE", foreign.FailureCode);
        Assert.Equal("UNKNOWN_WAREHOUSE", inactive.FailureCode);
        Assert.Equal("UNKNOWN_WAREHOUSE", deleted.FailureCode);
    }

    [Fact]
    public async Task WarehouseAssignment_ShouldCreateNewActiveAssignmentWhenPriorAssignmentWasSoftDeleted()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, new FakeCarrierAdapter());
        var tenantId = Guid.NewGuid();
        var actor = new CarrierAdministrationActor("admin");
        var accountId = (await service.RegisterCarrierAccountAsync(tenantId, CarrierCodes.Bosta, actor, "corr-account")).CarrierAccountId!.Value;
        var warehouseId = SeedWarehouse(db, tenantId);

        db.CarrierWarehouseAssignments.Add(new CarrierWarehouseAssignment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CarrierAccountId = accountId,
            WarehouseId = warehouseId,
            IsEnabled = false,
            IsDeleted = true
        });
        await db.SaveChangesAsync();

        var result = await service.AssignWarehouseAsync(
            tenantId,
            new CarrierWarehouseAssignmentCommand(accountId, warehouseId, true, "origin-ready"),
            actor,
            "corr-wh");

        Assert.True(result.Succeeded);
        var assignments = await db.CarrierWarehouseAssignments
            .IgnoreQueryFilters()
            .Where(assignment => assignment.TenantId == tenantId &&
                                 assignment.CarrierAccountId == accountId &&
                                 assignment.WarehouseId == warehouseId)
            .ToListAsync();
        Assert.Equal(2, assignments.Count);
        Assert.Single(assignments.Where(assignment => !assignment.IsDeleted));
    }

    [Fact]
    public async Task WarehouseAssignment_ShouldRejectManualFulfillmentCarrierAccountIfLegacyDataExists()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, new FakeCarrierAdapter());
        var tenantId = Guid.NewGuid();
        var warehouseId = SeedWarehouse(db, tenantId);
        var manualAccountId = Guid.NewGuid();
        db.CarrierAccounts.Add(new CarrierAccount
        {
            Id = manualAccountId,
            TenantId = tenantId,
            CarrierCode = CarrierCodes.Manual,
            State = CarrierAccountState.Draft,
            OperationalState = CarrierOperationalState.NotReady,
            HealthState = CarrierAccountHealthState.Blocked
        });
        await db.SaveChangesAsync();

        var result = await service.AssignWarehouseAsync(
            tenantId,
            new CarrierWarehouseAssignmentCommand(manualAccountId, warehouseId, true, "origin-ready"),
            new CarrierAdministrationActor("admin"),
            "corr-manual");

        Assert.False(result.Succeeded);
        Assert.Equal("MANUAL_CARRIER_ACCOUNT_NOT_SUPPORTED", result.FailureCode);
        Assert.Empty(await db.CarrierWarehouseAssignments.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task ConnectionTest_ShouldInvokeAdapterValidationOnlyAndStoreNoPlaintextSecretInAudit()
    {
        await using var db = CreateDbContext();
        var adapter = new FakeCarrierAdapter();
        var service = CreateService(db, adapter);
        var tenantId = Guid.NewGuid();
        var actor = new CarrierAdministrationActor("admin");
        var accountId = (await service.RegisterCarrierAccountAsync(tenantId, CarrierCodes.Bosta, actor, "corr-1")).CarrierAccountId!.Value;

        await service.UpdateCredentialReferenceAsync(
            tenantId,
            new CarrierCredentialReferenceCommand(accountId, "vault://tenant/bosta/active", "****1234", null),
            actor,
            "corr-2");
        await service.SelectEnvironmentAsync(tenantId, accountId, CarrierAccountEnvironment.Sandbox, actor, "corr-3");

        var result = await service.RunConnectionTestAsync(
            tenantId,
            accountId,
            new CarrierConnectionTestCredentials("plain-api-key-for-boundary-only", null, null, null, true),
            actor,
            "corr-4");

        Assert.True(result.Succeeded);
        Assert.Equal(1, adapter.ValidationCalls);
        var auditText = string.Join(" ", await db.CarrierAdministrationAudit.IgnoreQueryFilters().Select(a => a.Reason).ToListAsync());
        Assert.DoesNotContain("plain-api-key-for-boundary-only", auditText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runtime_ShouldNotMutateShipmentOrAdapterFiles()
    {
        var serviceSource = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/Administration/CarrierAccountAdministrationRuntime.cs");
        Assert.DoesNotContain("CarrierShipmentService", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DbSet<Shipment>", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateShipmentAsync", serviceSource, StringComparison.Ordinal);
        Assert.Contains("ValidateCredentialsAsync", serviceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_ShouldExistAndRecordRequiredScope()
    {
        var report = CertificationSource.ReadRepoFile("docs/architecture/POSADMIN_SHIPPING_1_ADMINISTRATION_RUNTIME.md");
        foreach (var heading in new[]
                 {
                     "Executive Summary",
                     "Carrier Account Runtime",
                     "State Machine",
                     "Activation Gates",
                     "Health Model",
                     "Audit",
                     "Credential Runtime",
                     "Warehouse Assignment Runtime",
                     "Policy Runtime",
                     "Authority Matrix",
                     "Breaking Issues",
                     "Regression Coverage",
                     "Verification",
                     "Final Recommendation"
                 })
        {
            Assert.Contains($"## {heading}", report, StringComparison.Ordinal);
        }

        Assert.Contains("Reviewed ONLY Administration Runtime? **Yes.**", report, StringComparison.Ordinal);
        Assert.Contains("Frontend modified? **No.**", report, StringComparison.Ordinal);
        Assert.Contains("Shipment Runtime modified? **No.**", report, StringComparison.Ordinal);
        Assert.Contains("Carrier Adapters modified? **No.**", report, StringComparison.Ordinal);
    }

    private static async Task<Guid> PrepareReadyForActivationAsync(
        ICarrierAccountAdministrationRuntime service,
        Guid tenantId,
        Guid warehouseId,
        CarrierAdministrationActor actor)
    {
        var accountId = (await service.RegisterCarrierAccountAsync(tenantId, CarrierCodes.Bosta, actor, "corr-a")).CarrierAccountId!.Value;
        await service.UpdateCredentialReferenceAsync(
            tenantId,
            new CarrierCredentialReferenceCommand(accountId, "vault://tenant/bosta/active", "****1234", null),
            actor,
            "corr-b");
        await service.SelectEnvironmentAsync(tenantId, accountId, CarrierAccountEnvironment.Sandbox, actor, "corr-c");
        await service.RunConnectionTestAsync(
            tenantId,
            accountId,
            new CarrierConnectionTestCredentials("test", null, null, null, true),
            actor,
            "corr-d");
        await service.AssignWarehouseAsync(
            tenantId,
            new CarrierWarehouseAssignmentCommand(accountId, warehouseId, true, "origin-ready"),
            actor,
            "corr-e");
        await service.SetServiceLevelAsync(tenantId, accountId, "standard", true, actor, "corr-f");
        var ready = await service.TransitionStateAsync(tenantId, accountId, CarrierAccountState.ReadyForActivation, ReadyEvidence(), actor, "corr-g");
        Assert.True(ready.Succeeded);
        return accountId;
    }

    private static CarrierReadinessEvidence ReadyEvidence() => new(true, true);

    private static Guid SeedWarehouse(ApplicationDbContext db, Guid tenantId, bool isActive = true, bool isDeleted = false)
    {
        var warehouseId = Guid.NewGuid();
        db.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Main",
            IsActive = isActive,
            IsDeleted = isDeleted
        });
        db.SaveChanges();
        return warehouseId;
    }

    private static CarrierAccountAdministrationRuntime CreateService(ApplicationDbContext db, params IShippingCarrierAdapter[] adapters) =>
        new(
            db,
            new ShippingCarrierRegistry(adapters, NullLogger<ShippingCarrierRegistry>.Instance),
            NullLogger<CarrierAccountAdministrationRuntime>.Instance);

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class FakeCarrierAdapter : IShippingCarrierAdapter
    {
        public int ValidationCalls { get; private set; }
        public string CarrierCode => CarrierCodes.Bosta;
        public string DisplayName => "Bosta";

        public Task<ShippingQuoteResult> GetQuoteAsync(ShippingQuoteRequest request, CarrierCredentials credentials, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ShippingQuoteResult { Success = false });

        public Task<CreateShipmentResult> CreateShipmentAsync(CreateShipmentRequest request, CarrierCredentials credentials, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CreateShipmentResult { Success = false });

        public Task<CancelShipmentResult> CancelShipmentAsync(string trackingNumber, CarrierCredentials credentials, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CancelShipmentResult { Success = false });

        public Task<ShipmentTrackingResult> GetTrackingAsync(string trackingNumber, CarrierCredentials credentials, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ShipmentTrackingResult { Success = false, CurrentStatus = ShipmentStatus.Pending });

        public Task<CarrierValidationResult> ValidateCredentialsAsync(CarrierCredentials credentials, CancellationToken cancellationToken = default)
        {
            ValidationCalls++;
            return Task.FromResult(new CarrierValidationResult { Success = true });
        }

        public IReadOnlyList<ServiceLevelInfo> GetSupportedServiceLevels() =>
            new[] { new ServiceLevelInfo { Code = "standard", Name = "Standard" } };

        public WebhookParseResult? ParseWebhook(string payload, string? signature, string? webhookSecret) => null;
    }
}
