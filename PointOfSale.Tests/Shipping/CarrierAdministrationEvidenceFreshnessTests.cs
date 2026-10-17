using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Entities.ShippingAdministration;
using PointOfSale.Core.Interfaces.ShippingAdministration;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Administration;

namespace PointOfSale.Tests.Shipping;

public sealed class CarrierAdministrationEvidenceFreshnessTests
{
    [Theory]
    [InlineData("environment")]
    [InlineData("credential")]
    [InlineData("latest-failed")]
    public async Task Activation_CannotReuseAStaleOrSupersededConnectionTest(string scenario)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"OfflineAccountFreshness_{Guid.NewGuid():N}").Options);
        var tenant = Guid.NewGuid(); var id = Guid.NewGuid(); var credential = Guid.NewGuid();
        db.CarrierAccounts.Add(new CarrierAccount { Id = id, TenantId = tenant, CarrierCode = "bosta",
            State = CarrierAccountState.ReadyForActivation, Environment = CarrierAccountEnvironment.Production,
            ActiveCredentialReferenceId = credential });
        db.CarrierCredentialReferences.Add(new CarrierCredentialReference { Id = credential, TenantId = tenant,
            CarrierAccountId = id, State = CarrierCredentialReferenceState.Active, SecretReference = "offline://credential" });
        db.CarrierConnectionTestRecords.Add(new CarrierConnectionTestRecord { Id = Guid.NewGuid(), TenantId = tenant,
            CarrierAccountId = id, CredentialReferenceId = scenario == "credential" ? Guid.NewGuid() : credential,
            Environment = scenario == "environment" ? CarrierAccountEnvironment.Sandbox : CarrierAccountEnvironment.Production,
            Status = CarrierConnectionTestStatus.Succeeded, TestedAtUtc = DateTime.UtcNow.AddMinutes(-5) });
        if (scenario == "latest-failed") db.CarrierConnectionTestRecords.Add(new CarrierConnectionTestRecord { Id = Guid.NewGuid(),
            TenantId = tenant, CarrierAccountId = id, CredentialReferenceId = credential, Environment = CarrierAccountEnvironment.Production,
            Status = CarrierConnectionTestStatus.Failed, TestedAtUtc = DateTime.UtcNow.AddMinutes(-1) });
        db.CarrierWarehouseAssignments.Add(new CarrierWarehouseAssignment { Id = Guid.NewGuid(), TenantId = tenant,
            CarrierAccountId = id, WarehouseId = Guid.NewGuid(), IsEnabled = true, OriginReadinessPublished = true });
        await db.SaveChangesAsync();
        var runtime = new CarrierAccountAdministrationRuntime(db, Mock.Of<IShippingCarrierRegistry>(),
            NullLogger<CarrierAccountAdministrationRuntime>.Instance);
        var result = await runtime.TransitionStateAsync(tenant, id, CarrierAccountState.Active, new CarrierReadinessEvidence(true, true),
            new CarrierAdministrationActor("offline"), "offline-stale-validation");
        Assert.False(result.Succeeded);
        Assert.Contains("CONNECTION_TEST_MISSING", result.Blockers);
        Assert.Equal(CarrierAccountState.ReadyForActivation, (await db.CarrierAccounts.IgnoreQueryFilters().SingleAsync()).State);
    }
}
