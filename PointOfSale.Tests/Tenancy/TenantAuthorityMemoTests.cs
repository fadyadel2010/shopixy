using PointOfSale.Infrastructure.Reliability;
using PointOfSale.Infrastructure.Tenancy;

namespace PointOfSale.Tests.Tenancy;

public sealed class TenantAuthorityMemoTests
{
    [Fact]
    public async Task RepeatedAuthorityChecks_ShareOneQueryWithinScope()
    {
        using var fixture = new TenantMemoTestFixture();
        Guid tenantId;
        using (var seed = fixture.CreateContext())
        {
            tenantId = TenantMemoTestFixture.SeedTenant(seed, "memo-active").Id;
        }

        using var request = fixture.CreateContext();
        fixture.Counter.Reset();

        var first = await TenantAuthorityMemo.GetTenantByIdAsync(request, tenantId);
        var second = await TenantAuthorityMemo.GetTenantByIdAsync(request, tenantId);
        var active = await TenantLineageAuthority.IsActiveTenantAsync(request, tenantId);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.True(active);
        Assert.Equal(1, fixture.Counter.Count);
    }

    [Fact]
    public async Task ConcurrentAuthorityChecks_PublishOneQueryTaskWithinScope()
    {
        using var fixture = new TenantMemoTestFixture();
        Guid tenantId;
        using (var seed = fixture.CreateContext())
        {
            tenantId = TenantMemoTestFixture.SeedTenant(seed, "memo-concurrent").Id;
        }

        using var request = fixture.CreateContext();
        fixture.Counter.Reset();

        var loads = Enumerable.Range(0, 100)
            .Select(_ => TenantAuthorityMemo.GetTenantByIdAsync(request, tenantId));
        var tenants = await Task.WhenAll(loads);

        Assert.All(tenants, Assert.NotNull);
        Assert.All(tenants, tenant => Assert.Same(tenants[0], tenant));
        Assert.Equal(1, fixture.Counter.Count);
    }

    [Fact]
    public async Task CancelledLoad_IsEvictedAndCanBeRetriedWithinScope()
    {
        using var fixture = new TenantMemoTestFixture();
        Guid tenantId;
        using (var seed = fixture.CreateContext())
        {
            tenantId = TenantMemoTestFixture.SeedTenant(seed, "memo-cancel-retry").Id;
        }

        using var request = fixture.CreateContext();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            TenantAuthorityMemo.GetTenantByIdAsync(request, tenantId, cancellation.Token));

        var retried = await TenantAuthorityMemo.GetTenantByIdAsync(request, tenantId);

        Assert.NotNull(retried);
    }

    [Fact]
    public async Task EmptyTenantId_ReturnsNullWithoutQuery()
    {
        using var fixture = new TenantMemoTestFixture();
        using var request = fixture.CreateContext();
        fixture.Counter.Reset();

        Assert.Null(await TenantAuthorityMemo.GetTenantByIdAsync(request, Guid.Empty));
        Assert.False(await TenantLineageAuthority.IsActiveTenantAsync(request, Guid.Empty));
        Assert.Equal(0, fixture.Counter.Count);
    }

    [Fact]
    public async Task InactiveVerdict_IsMemoizedButDoesNotLeakToAnotherScope()
    {
        using var fixture = new TenantMemoTestFixture();
        Guid tenantId;
        using (var seed = fixture.CreateContext())
        {
            tenantId = TenantMemoTestFixture.SeedTenant(seed, "memo-inactive", active: false).Id;
        }

        using (var firstRequest = fixture.CreateContext())
        {
            fixture.Counter.Reset();
            Assert.Null(await TenantAuthorityMemo.GetTenantByIdAsync(firstRequest, tenantId));
            Assert.Null(await TenantAuthorityMemo.GetTenantByIdAsync(firstRequest, tenantId));
            Assert.Equal(1, fixture.Counter.Count);
        }

        using var nextRequest = fixture.CreateContext();
        fixture.Counter.Reset();
        Assert.False(TenantAuthorityMemo.TryGetMemoized(nextRequest, tenantId, out _));
        Assert.Null(await TenantAuthorityMemo.GetTenantByIdAsync(nextRequest, tenantId));
        Assert.Equal(1, fixture.Counter.Count);
    }

    [Fact]
    public async Task StoredGuardedTenant_IsReusedWithoutQuery()
    {
        using var fixture = new TenantMemoTestFixture();
        Guid tenantId;
        using (var seed = fixture.CreateContext())
        {
            tenantId = TenantMemoTestFixture.SeedTenant(seed, "memo-stored").Id;
        }

        using var request = fixture.CreateContext();
        var tenant = request.Tenants.Single(item => item.Id == tenantId);
        request.ChangeTracker.Clear();
        fixture.Counter.Reset();

        TenantAuthorityMemo.Store(request, tenant);

        Assert.Same(tenant, await TenantAuthorityMemo.GetTenantByIdAsync(request, tenantId));
        Assert.Equal(0, fixture.Counter.Count);
    }
}
