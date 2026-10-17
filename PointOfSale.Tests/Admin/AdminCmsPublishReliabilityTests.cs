using System.Text.Json.Nodes;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.Services.Cms;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Admin;

public sealed class AdminCmsPublishReliabilityTests
{
    [Fact]
    public async Task PublishNavigationAsync_WhenOutboxPublishFails_DoesNotPersistLiveNavigationMutation()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();

        await using var seedContext = CreateDbContext(databaseName, tenantId);
        seedContext.StoreConfigs.Add(new StoreConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StoreName = "Reliable Navigation Store",
            ThemeId = "default",
            ThemePresetKey = "default",
            CustomizationJson = BuildNavigationCustomizationJson(),
            LiveVersion = 4,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await seedContext.SaveChangesAsync();

        await using var testContext = CreateDbContext(databaseName, tenantId);
        var publisher = new Mock<IEventPublisher>();
        publisher
            .Setup(endpoint => endpoint.PublishAsync(It.IsAny<NavigationPublishedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("outbox unavailable"));

        var service = new AdminCmsService(
            testContext,
            Mock.Of<ICmsPageService>(),
            Mock.Of<ICmsPageReadService>(),
            Mock.Of<ICmsPagePreviewTokenService>(),
            new NavigationAdmissionService(testContext),
            publisher.Object,
            NullLogger<AdminCmsService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PublishNavigationAsync(
            tenantId,
            0,
            "trace-navigation-publish",
            CancellationToken.None));

        await using var verifyContext = CreateDbContext(databaseName, tenantId);
        var config = await verifyContext.StoreConfigs.SingleAsync(x => x.TenantId == tenantId);
        var root = JsonNode.Parse(config.CustomizationJson)!.AsObject();

        Assert.Equal("Live Home", root["navigation"]?["live"]?["header"]?["links"]?[0]?["label"]?.GetValue<string>());
        Assert.Equal("Draft Home", root["navigation"]?["draft"]?["header"]?["links"]?[0]?["label"]?.GetValue<string>());
        Assert.Equal(4, config.LiveVersion);
    }

    [Fact]
    public async Task PublishNavigationAsync_AppendsVersionSnapshotForRollbackAuthority()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();

        await using var seedContext = CreateDbContext(databaseName, tenantId);
        seedContext.StoreConfigs.Add(new StoreConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StoreName = "Versioned Navigation Store",
            ThemeId = "default",
            ThemePresetKey = "default",
            ThemeLivePresetKey = "default",
            CustomizationJson = BuildNavigationCustomizationJson(),
            VersionsJson = "[]",
            LiveVersion = 0,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await seedContext.SaveChangesAsync();

        await using var testContext = CreateDbContext(databaseName, tenantId);
        var service = new AdminCmsService(
            testContext,
            Mock.Of<ICmsPageService>(),
            Mock.Of<ICmsPageReadService>(),
            Mock.Of<ICmsPagePreviewTokenService>(),
            new NavigationAdmissionService(testContext),
            Mock.Of<IEventPublisher>(),
            NullLogger<AdminCmsService>.Instance,
            new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "phase2-actor"), new Claim(ClaimTypes.Name, "Fixture administrator")
            ], "Fixture")) } });

        await service.PublishNavigationAsync(
            tenantId,
            0,
            "trace-navigation-version",
            CancellationToken.None);

        await using var verifyContext = CreateDbContext(databaseName, tenantId);
        var config = await verifyContext.StoreConfigs.SingleAsync(x => x.TenantId == tenantId);
        var versions = JsonNode.Parse(config.VersionsJson)!.AsArray();
        var snapshot = versions.Single()!.AsObject();
        var snapshotRoot = JsonNode.Parse(snapshot["CustomizationJson"]!.GetValue<string>())!.AsObject();

        Assert.Equal(1, config.LiveVersion);
        Assert.Equal(1, snapshot["Version"]!.GetValue<int>());
        Assert.Equal("Navigation Published", snapshot["ChangeType"]!.GetValue<string>());
        Assert.Equal("phase2-actor", snapshot["PublishedByUserId"]!.GetValue<string>());
        Assert.Equal("Fixture administrator", snapshot["PublishedBy"]!.GetValue<string>());
        Assert.Equal("Draft Home", snapshotRoot["navigation"]?["live"]?["header"]?["links"]?[0]?["label"]?.GetValue<string>());
    }

    [Fact]
    public async Task PublishNavigationAsync_WhenDraftAlreadyMatchesLive_IsIdempotent()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var publisher = new Mock<IEventPublisher>();
        var customization = BuildNavigationCustomizationJson();
        var root = JsonNode.Parse(customization)!.AsObject();
        root["navigation"]!["live"] = root["navigation"]!["draft"]!.DeepClone();

        await using var seedContext = CreateDbContext(databaseName, tenantId);
        seedContext.StoreConfigs.Add(new StoreConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StoreName = "Idempotent Navigation Store",
            ThemeId = "default",
            ThemePresetKey = "default",
            CustomizationJson = root.ToJsonString(),
            VersionsJson = "[]",
            LiveVersion = 4,
            RowVersion = 7,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await seedContext.SaveChangesAsync();

        await using var testContext = CreateDbContext(databaseName, tenantId);
        var service = new AdminCmsService(
            testContext,
            Mock.Of<ICmsPageService>(),
            Mock.Of<ICmsPageReadService>(),
            Mock.Of<ICmsPagePreviewTokenService>(),
            new NavigationAdmissionService(testContext),
            publisher.Object,
            NullLogger<AdminCmsService>.Instance);

        var result = await service.PublishNavigationAsync(
            tenantId,
            7,
            "trace-navigation-idempotent",
            CancellationToken.None);

        Assert.False(result.HasPendingChanges);
        Assert.Equal("7", result.RowVersion);
        Assert.Equal(4, testContext.StoreConfigs.Single().LiveVersion);
        Assert.Equal("[]", testContext.StoreConfigs.Single().VersionsJson);
        publisher.Verify(
            endpoint => endpoint.PublishAsync(It.IsAny<NavigationPublishedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static ApplicationDbContext CreateDbContext(string databaseName, Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, SharedInMemoryDatabaseRoot.Instance)
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static string BuildNavigationCustomizationJson()
    {
        return new JsonObject
        {
            ["navigation"] = new JsonObject
            {
                ["draft"] = new JsonObject
                {
                    ["header"] = new JsonObject
                    {
                        ["links"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["label"] = "Draft Home",
                                ["type"] = "internal",
                                ["path"] = "/"
                            }
                        }
                    },
                    ["footer"] = new JsonObject
                    {
                        ["links"] = new JsonArray()
                    }
                },
                ["live"] = new JsonObject
                {
                    ["header"] = new JsonObject
                    {
                        ["links"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["label"] = "Live Home",
                                ["type"] = "internal",
                                ["path"] = "/"
                            }
                        }
                    },
                    ["footer"] = new JsonObject
                    {
                        ["links"] = new JsonArray()
                    }
                }
            }
        }.ToJsonString();
    }
}
