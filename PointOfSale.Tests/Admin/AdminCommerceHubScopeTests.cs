using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.CommerceHub.Handlers;
using PointOfSale.API.Controllers;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.Governance;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.Jobs;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Admin;

public sealed class AdminCommerceHubScopeTests
{
    [Fact]
    public async Task FinalizeSearchMigration_WhenCalledFromTenantAdmin_ReturnsForbiddenWithoutGlobalSearchMutation()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new TestTenantContext(tenantId);
        var searchIndexService = new Mock<ICommerceHubSearchIndexService>(MockBehavior.Strict);
        var reindexService = new Mock<ICommerceHubReindexService>(MockBehavior.Strict);

        await using var dbContext = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenantContext);

        var controller = new AdminCommerceHubController(
            tenantContext,
            dbContext,
            Mock.Of<IPublishToCommerceHubHandler>(),
            Mock.Of<IUpdateCommerceHubListingHandler>(),
            Mock.Of<IUnpublishFromCommerceHubHandler>(),
            Mock.Of<ICommissionService>(),
            searchIndexService.Object,
            reindexService.Object,
            Mock.Of<IUserGovernanceService>(),
            NullLogger<AdminCommerceHubController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await controller.FinalizeSearchMigration("samsung", CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        Assert.Equal("GLOBAL_SEARCH_CONTROL_FORBIDDEN", GetStringProperty(forbidden.Value, "code"));
        Assert.Contains(
            "global platform recovery primitive",
            GetStringProperty(forbidden.Value, "message") ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

        searchIndexService.Verify(
            service => service.FinalizeAliasMigrationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        reindexService.Verify(
            service => service.CatchUpReindexAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        reindexService.Verify(
            service => service.ReindexAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static string? GetStringProperty(object? value, string propertyName) =>
        value?.GetType().GetProperty(propertyName)?.GetValue(value) as string;
}
