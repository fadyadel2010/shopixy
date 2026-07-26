using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.Services.Cms;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Admin;

public sealed class AdminStoreConfigPublishReliabilityTests
{
    private static readonly ConcurrentDictionary<string, InMemoryDatabaseRoot> DatabaseRoots = new(StringComparer.Ordinal);

    [Fact]
    public async Task PublishPageAsync_WhenOutboxPublishFails_DoesNotPersistLiveMutation()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();

        await using var seedContext = CreateDbContext(databaseName, tenantId);
        seedContext.StoreConfigs.Add(new StoreConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StoreName = "Reliable Publish Store",
            ThemeId = "default",
            ThemePresetKey = "default",
            CustomizationJson = BuildPageCustomizationJson(),
            UpdatedAtUtc = DateTime.UtcNow
        });
        await seedContext.SaveChangesAsync();

        await using var testContext = CreateDbContext(databaseName, tenantId);
        var publishEndpoint = new Mock<IEventPublisher>();
        publishEndpoint
            .Setup(endpoint => endpoint.PublishAsync(It.IsAny<StoreConfigPublishedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("outbox unavailable"));

        var service = CreateService(testContext, publishEndpoint.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PublishPageAsync(
            tenantId,
            "home",
            "trace-publish",
            CancellationToken.None));

        await using var verifyContext = CreateDbContext(databaseName, tenantId);
        var config = await verifyContext.StoreConfigs.SingleAsync(x => x.TenantId == tenantId);
        var root = JsonNode.Parse(config.CustomizationJson)!.AsObject();

        Assert.Equal("Live hero", root["pages"]?["home"]?["live"]?["title"]?.GetValue<string>());
        Assert.Equal("Draft hero", root["pages"]?["home"]?["draft"]?["title"]?.GetValue<string>());
        Assert.Equal(0, config.LiveVersion);
    }

    private static ApplicationDbContext CreateDbContext(string databaseName, Guid tenantId)
    {
        var databaseRoot = DatabaseRoots.GetOrAdd(databaseName, _ => new InMemoryDatabaseRoot());
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static AdminStoreConfigService CreateService(ApplicationDbContext context, IEventPublisher publishEndpoint)
    {
        var publishLockService = new Mock<IPublishLockService>();
        publishLockService
            .Setup(x => x.TryAcquireAsync(It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(PublishLockResult.Success("test-lock"));

        var auditService = new Mock<IStoreConfigAuditService>();
        auditService.SetReturnsDefault(Task.CompletedTask);

        var cmsPageReadService = new Mock<ICmsPageReadService>(MockBehavior.Strict);
        var previewService = new StorefrontPreviewService(
            context,
            cmsPageReadService.Object,
            NullLogger<StorefrontPreviewService>.Instance);

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };

        return new AdminStoreConfigService(
            context,
            publishEndpoint,
            publishLockService.Object,
            auditService.Object,
            previewService,
            new StorefrontPublishTelemetry(),
            NullLogger<AdminStoreConfigService>.Instance,
            httpContextAccessor);
    }

    private static string BuildPageCustomizationJson()
    {
        return new JsonObject
        {
            ["pages"] = new JsonObject
            {
                ["home"] = new JsonObject
                {
                    ["draft"] = new JsonObject
                    {
                        ["title"] = "Draft hero",
                        ["sections"] = new JsonArray()
                    },
                    ["live"] = new JsonObject
                    {
                        ["title"] = "Live hero",
                        ["sections"] = new JsonArray()
                    }
                }
            }
        }.ToJsonString();
    }
}
