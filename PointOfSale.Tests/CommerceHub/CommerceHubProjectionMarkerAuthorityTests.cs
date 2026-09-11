using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubProjectionMarkerAuthorityTests
{
    [Fact]
    public async Task ProjectionRetryMarkers_CanCompleteWithoutAdvancingBusinessMutationVersion()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var dbContext = new ApplicationDbContext(options);
        var order = new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            OrderNumber = "ORD-CH-PROJECTION-MARKER",
            FullName = "Projection Test",
            PhoneNumber = "01000000000",
            AddressLine = "Test address",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = "COD",
            PaymentMethodName = "Cash on Delivery"
        };

        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        {
            dbContext.CommerceHubMarketplaceOrders.Add(order);
            await dbContext.SaveChangesAsync();
        }

        var businessMutationVersion = order.ProjectionMutationVersion;
        using (CommerceHubBoundaryMutationScope.BeginProjection())
        {
            order.LastProjectionAttemptAt = DateTime.UtcNow;
            order.ProjectionAttemptCount = 10;
            order.IsProjectionTerminalFailure = true;
            await dbContext.SaveChangesAsync();
        }

        using (CommerceHubBoundaryMutationScope.BeginProjection())
        {
            order.IsProjected = true;
            order.ProjectedAtUtc = DateTime.UtcNow;
            order.IsProjectionTerminalFailure = false;
            await dbContext.SaveChangesAsync();
        }

        Assert.True(order.IsProjected);
        Assert.False(order.IsProjectionTerminalFailure);
        Assert.Equal(businessMutationVersion, order.ProjectionMutationVersion);
    }
}
