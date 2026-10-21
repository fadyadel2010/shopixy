using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.Infrastructure.Services;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Data.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace PointOfSale.Tests.Affiliate;

public sealed class SyntheticQaDatabaseBoundaryTests
{
    [Fact]
    public async Task AdminOrderList_ExecutesActualSqlUnion_WithExplicitSyntheticAndMarketplaceShapes()
    {
        await using var database = TestDatabaseScope.Open();
        await using var db = database.CreateDbContext();
        var service = new AdminOrderService(db, Mock.Of<IEventPublisher>(), Mock.Of<IRefundEvidenceService>(),
            Mock.Of<ICouponValidationService>(), Mock.Of<IPaymentLifecycleService>(),
            Mock.Of<IProductVariantStockRepository>(), Mock.Of<IR2StorageService>(), NullLogger<AdminOrderService>.Instance);
        var result = await service.ListOrdersAsync(Guid.NewGuid(), new AdminOrderListQuery(), "synthetic-qa-list-contract");
        Assert.Empty(result.Items);
    }
    [Fact]
    public async Task ActualConnectedDatabaseIdentity_AndExplicitVerifierAreRequired()
    {
        await using var database = TestDatabaseScope.Open();
        await using var db = database.CreateDbContext();
        var identity = await db.Database.SqlQueryRaw<Identity>("SELECT current_database() AS \"Database\", host(inet_server_addr()) AS \"Server\"").SingleAsync();
        var value = new SyntheticQaOptions { Enabled = true, TenantId = SyntheticQaCertificationAuthority.AhramStoreId,
            Database = identity.Database, Server = identity.Server, SigningKey = new string('a', 64), VerifierIds = [] };
        var monitor = Mock.Of<IOptionsMonitor<SyntheticQaOptions>>(x => x.CurrentValue == value);
        var environment = new Mock<IHostEnvironment>(); environment.SetupGet(x => x.EnvironmentName).Returns("Test");
        var authority = new SyntheticQaCertificationAuthority(db, environment.Object, monitor);
        await authority.EnsureEnabledAsync();
        // Owning the correct tenant is NOT verifier authority.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authority.EnsureVerifierAsync(value.TenantId, Guid.NewGuid()));
        value.Database = "not-the-connected-database";
        await Assert.ThrowsAsync<InvalidOperationException>(() => authority.EnsureEnabledAsync());
        value.Database = identity.Database; value.Server = "203.0.113.123";
        await Assert.ThrowsAsync<InvalidOperationException>(() => authority.EnsureEnabledAsync());
        value.Server = identity.Server; value.Enabled = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => authority.EnsureEnabledAsync());
        value.Enabled = true; environment.SetupGet(x => x.EnvironmentName).Returns("Production");
        await Assert.ThrowsAsync<InvalidOperationException>(() => authority.EnsureEnabledAsync());
        Assert.Equal(0, await db.SyntheticQaRuns.CountAsync());
        Assert.Equal(0, await db.SyntheticQaEvidence.CountAsync());
    }

    private sealed class Identity { public string Database { get; set; } = ""; public string Server { get; set; } = ""; }
}
