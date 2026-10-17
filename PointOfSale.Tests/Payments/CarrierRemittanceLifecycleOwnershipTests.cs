using Moq;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Payments;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Payments;

public sealed class CarrierRemittanceLifecycleOwnershipTests
{
    [Fact]
    public async Task Receive_ForwardsExactFactsAndCancellationToCanonicalOwner()
    {
        var tenant = Guid.NewGuid(); var actor = Guid.NewGuid();
        var request = new CarrierRemittanceRequest(Guid.NewGuid(), [Guid.NewGuid()], DateTimeOffset.UtcNow, "synthetic", true);
        using var cancellation = new CancellationTokenSource();
        var owner = new Mock<IPaymentLifecycleService>(MockBehavior.Strict);
        owner.Setup(l => l.RecordCarrierRemittanceAsync(tenant, actor, request, cancellation.Token))
            .ThrowsAsync(new CarrierRemittanceException("OWNER_REFUSAL"));
        var service = new CarrierRemittanceService(owner.Object, new TestTenantContext(tenant));
        var refusal = await Assert.ThrowsAsync<CarrierRemittanceException>(() => service.ReceiveAsync(tenant, actor, request, cancellation.Token));
        Assert.Equal("OWNER_REFUSAL", refusal.Code);
        owner.VerifyAll(); owner.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("receive")][InlineData("select")][InlineData("get")]
    public async Task ForeignTenant_FailsBeforeCanonicalDispatch(string operation)
    {
        var owner = new Mock<IPaymentLifecycleService>(MockBehavior.Strict);
        var service = new CarrierRemittanceService(owner.Object, new TestTenantContext(Guid.NewGuid()));
        var foreign = Guid.NewGuid(); var actor = Guid.NewGuid();
        var refusal = await Assert.ThrowsAsync<CarrierRemittanceException>(async () =>
        {
            if (operation == "receive") await service.ReceiveAsync(foreign, actor, new(Guid.NewGuid(), [Guid.NewGuid()], DateTimeOffset.UtcNow, "synthetic", true));
            else if (operation == "select") await service.SelectAsync(foreign, actor);
            else await service.GetAsync(foreign, actor, Guid.NewGuid());
        });
        Assert.Equal("REMITTANCE_TENANT_REQUIRED", refusal.Code);
        owner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReadAndRecovery_AlsoUseCanonicalOwner()
    {
        var tenant = Guid.NewGuid(); var actor = Guid.NewGuid(); var operation = Guid.NewGuid();
        var owner = new Mock<IPaymentLifecycleService>(MockBehavior.Strict);
        var selection = new CarrierRemittanceSelection([], "EGP", "1010", 2, false);
        owner.Setup(l => l.SelectCarrierRemittanceAsync(tenant, actor, 2, default)).ReturnsAsync(selection);
        owner.Setup(l => l.GetCarrierRemittanceAsync(tenant, actor, operation, default)).ReturnsAsync((CarrierRemittanceResult?)null);
        var service = new CarrierRemittanceService(owner.Object, new TestTenantContext(tenant));
        Assert.Same(selection, await service.SelectAsync(tenant, actor, 2));
        Assert.Null(await service.GetAsync(tenant, actor, operation));
        owner.VerifyAll(); owner.VerifyNoOtherCalls();
    }
}
