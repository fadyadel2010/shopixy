using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Admin;

public sealed class AbandonedCheckoutPhase4Tests
{
    [Fact]
    public async Task CurrentListAndDetail_AreSqlAuthoritativeWithoutProjectionAvailability()
    {
        var tenant = Guid.NewGuid(); var id = Guid.NewGuid();
        var context = new PointOfSale.Tests.Infrastructure.TestTenantContext(tenant);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, context);
        db.CheckoutSessions.Add(new CheckoutSession { Id = id, TenantId = tenant, CartId = Guid.NewGuid(), SessionId = "P4QA", RecoveryToken = "", Status = CheckoutSessionStatus.ConvertedToOrder, CartTotal = 25m, Currency = "EGP" });
        db.CheckoutSessions.Add(new CheckoutSession { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), CartId = Guid.NewGuid(), SessionId = "foreign", RecoveryToken = "", Status = CheckoutSessionStatus.Abandoned });
        await db.SaveChangesAsync();
        var controller = new AdminAbandonedCheckoutsController(null!, db, context, null!, null!, NullLogger<AdminAbandonedCheckoutsController>.Instance);
        var list = Assert.IsType<OkObjectResult>(await controller.GetList(status: "ConvertedToOrder"));
        var data = Assert.IsType<PointOfSale.API.DTOs.Checkout.AbandonedCheckoutPagedResponse>(list.Value);
        Assert.Equal(id, Assert.Single(data.Items).CheckoutSessionId);
        var detail = Assert.IsType<PointOfSale.API.DTOs.Checkout.AbandonedCheckoutDetail>(Assert.IsType<OkObjectResult>(await controller.GetDetail(id, CancellationToken.None)).Value);
        Assert.Equal("ConvertedToOrder", detail.Status);
        await db.CheckoutSessions.Where(x => x.Id == id).ForEachAsync(x => x.InvalidatedAtUtc = DateTime.UtcNow);
        await db.SaveChangesAsync();
        Assert.IsType<NotFoundObjectResult>(await controller.GetDetail(id, CancellationToken.None));
    }
    [Theory]
    [InlineData("USD", "EGP")]
    [InlineData("EGP", null)]
    public async Task MixedOrMissingCurrency_ReturnsUnavailableValue_NotCrashOrInventedTotal(string first, string? second)
    {
        var tenant = Guid.NewGuid();
        var context = new Mock<ITenantContext>();
        context.SetupGet(x => x.TenantId).Returns(tenant);
        context.SetupGet(x => x.RequiredTenantId).Returns(tenant);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        foreach (var currency in new[] { first, second }) db.CheckoutSessions.Add(new CheckoutSession {
            Id = Guid.NewGuid(), TenantId = tenant, CartId = Guid.NewGuid(), SessionId = "P4QA", RecoveryToken = Guid.NewGuid().ToString(),
            Status = CheckoutSessionStatus.Abandoned, CartTotal = 10m, Currency = currency });
        await db.SaveChangesAsync();
        var controller = new AdminAbandonedCheckoutsController(null!, db, context.Object, null!, null!, NullLogger<AdminAbandonedCheckoutsController>.Instance);
        var result = Assert.IsType<OkObjectResult>(await controller.GetStats(CancellationToken.None));
        var data = JsonSerializer.SerializeToElement(result.Value);
        Assert.False(data.GetProperty("abandonedValueAvailable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("totalAbandonedRevenue").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("abandonedValueCurrency").ValueKind);
    }
}
