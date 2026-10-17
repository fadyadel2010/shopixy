using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.API.Services.Storefront;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.AI;
using PointOfSale.API.Services.AI.Models;
using PointOfSale.Core.Enums;

namespace PointOfSale.Tests.Security;

public sealed class Phase7ContextTests
{
    [Fact]
    public async Task ShippingContext_PreservesAuthoritativeZeroQuote()
    {
        var tenant = Guid.NewGuid(); await using var db = Phase7ProviderTests.Context(tenant);
        var shipping = new Mock<IShippingMethodService>();
        shipping.Setup(x => x.GetShippingMethodsAsync(tenant, 0, 0, "P7QA", null, default)).ReturnsAsync([new() { Id = Guid.NewGuid(), Name = "P7QA free", Price = 0, BaseRate = 10 }]);
        var builder = new CommerceContextBuilder(null!, shipping.Object, null!, db, null!, Options.Create(new CommerceAIOptions()), NullLogger<CommerceContextBuilder>.Instance);
        var method = typeof(CommerceContextBuilder).GetMethod("BuildShippingContextAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var result = await (Task<ShippingContext>)method.Invoke(builder, [tenant, null, null, "P7QA"])!;
        Assert.Equal(0m, Assert.Single(result.Methods).Price);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task ProductContext_CannotExposePrivateOrMissingSqlProductThroughForgedVisibleIds(bool exists)
    {
        var tenant = Guid.NewGuid(); var id = Guid.NewGuid(); await using var db = Phase7ProviderTests.Context(tenant);
        if (exists) { db.Products.Add(new() { Id = id, TenantId = tenant, Name = "PRIVATE SQL PRODUCT", IsActive = true, IsOnlineVisible = false, PublishStatus = PublishStatus.Draft }); await db.SaveChangesAsync(); }
        var repository = new Mock<IProductReadRepository>();
        repository.Setup(x => x.GetByProductIdsAsync(tenant, It.IsAny<List<Guid>>(), false, It.IsAny<CancellationToken>())).ReturnsAsync([new() { ProductId = id, TenantId = tenant, Name = "PRIVATE PROJECTED PRODUCT", IsActive = true, IsOnlineVisible = true }]);
        var builder = new CommerceContextBuilder(repository.Object, null!, null!, db, null!, Options.Create(new CommerceAIOptions()), NullLogger<CommerceContextBuilder>.Instance);
        var method = typeof(CommerceContextBuilder).GetMethod("BuildProductsContextAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var result = await (Task<List<ProductContext>>)method.Invoke(builder, [Task.FromResult<StorefrontConfigDocument?>(new() { Currency = "EGP" }), tenant, null, new List<Guid> { id }, null, "P7QA"])!;
        Assert.Empty(result);
    }
    [Fact]
    public async Task Tracking_UsesCanonicalOwnedSqlSnapshotNotAnotherOrderOrTenant()
    {
        var tenant = Guid.NewGuid(); var customer = Guid.NewGuid(); await using var db = Phase7ProviderTests.Context(tenant);
        var target = new PointOfSale.Core.Entities.Order { Id = Guid.NewGuid(), TenantId = tenant, OnlineCustomerId = customer, OrderNumber = "ORD-C9C5-2026-01054", Total = 35, Currency = "EGP", Status = OrderStatus.Cancelled, CreatedAt = DateTime.UtcNow.AddDays(-2) };
        db.Orders.Add(target);
        db.Orders.Add(new() { Id = Guid.NewGuid(), TenantId = tenant, OnlineCustomerId = customer, OrderNumber = "ORD-C9C5-2026-01055", Total = 40, CreatedAt = DateTime.UtcNow }); await db.SaveChangesAsync();
        var builder = new CommerceContextBuilder(null!, null!, null!, db, null!, Options.Create(new CommerceAIOptions()), NullLogger<CommerceContextBuilder>.Instance);
        var method = typeof(CommerceContextBuilder).GetMethod("BuildOrderTrackingContextAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        async Task<OrderTrackingContext?> Read(Guid t, Guid c, string message) => await (Task<OrderTrackingContext?>)method.Invoke(builder, [t, c, message, "P7QA"])!;
        var result = await Read(tenant, customer, "Track ORD-C9C5-2026-01054 please");
        Assert.NotNull(result); Assert.Equal(target.Id, result.Id); Assert.Equal(35, result.Total); Assert.Equal("Cancelled", result.Status); Assert.Equal("EGP", result.Currency);
        Assert.Null(await Read(Guid.NewGuid(), customer, "Track ORD-C9C5-2026-01054"));
        Assert.Null(await Read(tenant, Guid.NewGuid(), "Track ORD-C9C5-2026-01054"));
        Assert.Null(await Read(tenant, customer, "Track ORD-C9C5-2026-99999"));
    }

    [Fact]
    public async Task MissingShipment_DoesNotInventHistoricalProviderActivation()
    {
        await using var db = Phase7ProviderTests.Context(Guid.NewGuid());
        var builder = new CommerceContextBuilder(null!, null!, null!, db, null!, Options.Create(new CommerceAIOptions()), NullLogger<CommerceContextBuilder>.Instance);
        var method = typeof(CommerceContextBuilder).GetMethod("BuildHistoricalContextAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var result = await (Task<HistoricalContext?>)method.Invoke(builder, [new OrderTrackingContext { Status = "Completed", CreatedAt = DateTime.UtcNow }, null])!;
        Assert.NotNull(result); Assert.Null(result.ShippingIntegrationEnabledAtCreation); Assert.Null(result.PaymentIntegrationEnabledAtCreation);
        Assert.DoesNotContain("before automated tracking was enabled", result.ConfidenceNote);
    }
}
