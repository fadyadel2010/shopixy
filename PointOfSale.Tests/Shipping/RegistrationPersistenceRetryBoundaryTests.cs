using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.Tests.Shipping;

public sealed partial class ShipmentExternalRegistrationSafetyTests
{
    [Fact]
    public async Task PostProviderSaveRetry_RechecksIndependentCancellationBarrierBeforeAdvancing()
    {
        var tenant = Guid.NewGuid(); var id = Guid.NewGuid(); var store = $"OfflineSaveRace_{Guid.NewGuid():N}";
        var root = new InMemoryDatabaseRoot();
        var interceptor = new FailFirstCompletedSave(async () =>
        {
            await using var winner = CreateDbContext(tenant, store, storeRoot: root);
            var current = await winner.Shipments.SingleAsync();
            winner.ShipmentCancellationOperations.Add(new ShipmentCancellationOperation
            {
                Id = Guid.NewGuid(), TenantId = tenant, ShipmentId = id, OrderId = current.OrderId,
                ActorIdentity = "offline", Source = "PosAdmin", Reason = "Synthetic save interleaving",
                CorrelationId = "offline", CarrierCode = current.CarrierCode, RequestFingerprint = "retained-operation-fingerprint",
                Outcome = ShipmentCancellationOutcome.ReconciliationRequired, Code = "CANCELLATION_REPLAY_UNRESOLVED"
            });
            await winner.SaveChangesAsync();
        });
        await using var db = CreateDbContext(tenant, store, interceptor, root);
        SeedOrderAndShipment(db, tenant, id, ShipmentStatus.PendingExternalRegistration, DeterministicCarrierCode);
        var adapter = new DeterministicCarrierAdapter();
        var result = await CreateService(db, tenant, adapter).ExecuteCarrierForExistingShipmentAsync(tenant, id, "standard");
        Assert.Equal("CANCELLATION_BARRIER", result.ErrorCode);
        await using var inspect = CreateDbContext(tenant, store, storeRoot: root);
        var retained = await inspect.Shipments.SingleAsync();
        Assert.Equal(ShipmentStatus.PendingExternalRegistration, retained.Status);
        Assert.Equal("ReconciliationRequired", retained.ExternalRegistrationState);
        Assert.Equal("CANCELLATION_REPLAY_UNRESOLVED", (await inspect.ShipmentCancellationOperations.SingleAsync()).Code);
        Assert.Equal(1, adapter.CreateShipmentCallCount);
        Assert.NotNull(retained.CarrierCreateResponse);
        Assert.True(interceptor.Injected);
    }

    private sealed class FailFirstCompletedSave(Func<Task> beforeFailure) : SaveChangesInterceptor
    {
        public bool Injected { get; private set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Injected && eventData.Context!.ChangeTracker.Entries<Shipment>().Any(e =>
                e.State == EntityState.Modified && e.Entity.ExternalRegistrationState == "Completed"))
            {
                Injected = true;
                await beforeFailure();
                throw new DbUpdateException("Synthetic first business-save failure, no SQL involved.");
            }
            return result;
        }
    }
}
