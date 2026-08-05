using System.Collections.Concurrent;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MongoDB.Driver;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Consumers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Storefront;

public sealed class StorefrontConfigProjectionConsumerTests
{
    private static readonly ConcurrentDictionary<string, InMemoryDatabaseRoot> DatabaseRoots = new(StringComparer.Ordinal);

    [Fact]
    public async Task ConsumePublishedEvent_ProjectsLiveConfig()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var storeConfigId = Guid.NewGuid();

        await using var seedContext = CreateDbContext(databaseName, tenantId);
        seedContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Projection Tenant",
            Slug = "projection-tenant",
            Currency = "SAR",
            Status = TenantStatus.Active,
            IsActive = true
        });
        seedContext.ThemePresets.Add(new ThemePreset
        {
            Id = Guid.NewGuid(),
            Key = "default",
            Name = "Default",
            BaseThemeJson = """
            {
              "colors": {
                "primary": "#2563eb",
                "secondary": "#0f172a",
                "background": "#ffffff",
                "text": "#111827",
                "muted": "#64748b"
              },
              "typography": {
                "fontFamily": "Inter",
                "baseSize": "16px"
              },
              "radius": {
                "base": "8px"
              }
            }
            """
        });
        seedContext.StoreConfigs.Add(new StoreConfig
        {
            Id = storeConfigId,
            TenantId = tenantId,
            StoreName = "Projected Store",
            ThemeId = "default",
            ThemePresetKey = "default",
            CustomizationJson = """
            {
              "storefrontTheme": "modern",
              "pages": {
                "home": {
                  "draft": {
                    "sections": [
                      {
                        "id": "draft-home",
                        "type": "hero",
                        "enabled": true,
                        "order": 0
                      }
                    ]
                  },
                  "live": {
                    "sections": [
                      {
                        "id": "live-home",
                        "type": "hero",
                        "enabled": true,
                        "order": 0
                      }
                    ]
                  }
                }
              }
            }
            """
        });
        await seedContext.SaveChangesAsync();

        var mongoCollection = new Mock<IMongoCollection<StorefrontConfigDocument>>();
        var mongoDatabase = new Mock<IMongoDatabase>();
        mongoDatabase
            .Setup(database => database.GetCollection<StorefrontConfigDocument>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
            .Returns(mongoCollection.Object);

        var emptyCursor = new Mock<IAsyncCursor<StorefrontConfigDocument>>();
        emptyCursor
            .SetupSequence(cursor => cursor.MoveNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        emptyCursor
            .SetupGet(cursor => cursor.Current)
            .Returns(Array.Empty<StorefrontConfigDocument>());

        mongoCollection
            .Setup(collection => collection.FindAsync(
                It.IsAny<FilterDefinition<StorefrontConfigDocument>>(),
                It.IsAny<FindOptions<StorefrontConfigDocument, StorefrontConfigDocument>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyCursor.Object);
        mongoCollection
            .Setup(collection => collection.FindAsync(
                It.IsAny<FilterDefinition<StorefrontConfigDocument>>(),
                It.IsAny<FindOptions<StorefrontConfigDocument>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyCursor.Object);

        StorefrontConfigDocument? capturedDocument = null;
        mongoCollection
            .Setup(collection => collection.ReplaceOneAsync(
                It.IsAny<FilterDefinition<StorefrontConfigDocument>>(),
                It.IsAny<StorefrontConfigDocument>(),
                It.IsAny<ReplaceOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<StorefrontConfigDocument>, StorefrontConfigDocument, ReplaceOptions, CancellationToken>((_, document, _, _) =>
            {
                capturedDocument = document;
            })
            .ReturnsAsync(() =>
            {
                var result = new Mock<ReplaceOneResult>();
                result.SetupGet(replace => replace.IsAcknowledged).Returns(true);
                result.SetupGet(replace => replace.MatchedCount).Returns(1);
                return result.Object;
            });

        await using var testContext = CreateDbContext(databaseName, tenantId);
        var consumer = new StorefrontConfigProjectionConsumer(
            testContext,
            mongoDatabase.Object,
            new StorefrontPublishTelemetry(),
            new CommerceReliabilityTelemetry(),
            NullLogger<StorefrontConfigProjectionConsumer>.Instance);

        var context = new Mock<ConsumeContext<StoreConfigPublishedEvent>>();
        context.SetupGet(x => x.Message).Returns(new StoreConfigPublishedEvent
        {
            TenantId = tenantId,
            StoreConfigId = storeConfigId,
            ThemeId = "default",
            StoreName = "Projected Store",
            UpdatedAtUtc = DateTime.UtcNow,
            OccurredAtUtc = DateTime.UtcNow.AddSeconds(-15),
            ConfigVersion = 2
        });
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(context.Object);

        Assert.NotNull(capturedDocument);
        Assert.Equal(tenantId, capturedDocument!.TenantId);
        Assert.Equal("SAR", capturedDocument.Currency);
        Assert.Single(capturedDocument.Pages.Home.Sections);
        Assert.Equal("live-home", capturedDocument.Pages.Home.Sections[0].Id);
        Assert.DoesNotContain(capturedDocument.Pages.Home.Sections, section => section.Id == "draft-home");
    }

    [Fact]
    public async Task ConsumePublishedEvent_WhenTenantCurrencyMissing_ThrowsInsteadOfProjectingFallbackCurrency()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var storeConfigId = Guid.NewGuid();

        await using var seedContext = CreateDbContext(databaseName, tenantId);
        seedContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Projection Tenant",
            Slug = "projection-tenant",
            Currency = " ",
            Status = TenantStatus.Active,
            IsActive = true
        });
        seedContext.ThemePresets.Add(new ThemePreset
        {
            Id = Guid.NewGuid(),
            Key = "default",
            Name = "Default",
            BaseThemeJson = """{"colors":{"primary":"#2563eb"}}"""
        });
        seedContext.StoreConfigs.Add(new StoreConfig
        {
            Id = storeConfigId,
            TenantId = tenantId,
            StoreName = "Projected Store",
            ThemeId = "default",
            ThemePresetKey = "default"
        });
        await seedContext.SaveChangesAsync();

        var mongoCollection = new Mock<IMongoCollection<StorefrontConfigDocument>>();
        var mongoDatabase = new Mock<IMongoDatabase>();
        mongoDatabase
            .Setup(database => database.GetCollection<StorefrontConfigDocument>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
            .Returns(mongoCollection.Object);

        await using var testContext = CreateDbContext(databaseName, tenantId);
        var consumer = new StorefrontConfigProjectionConsumer(
            testContext,
            mongoDatabase.Object,
            new StorefrontPublishTelemetry(),
            new CommerceReliabilityTelemetry(),
            NullLogger<StorefrontConfigProjectionConsumer>.Instance);

        var context = new Mock<ConsumeContext<StoreConfigPublishedEvent>>();
        context.SetupGet(x => x.Message).Returns(new StoreConfigPublishedEvent
        {
            TenantId = tenantId,
            StoreConfigId = storeConfigId,
            ThemeId = "default",
            StoreName = "Projected Store",
            UpdatedAtUtc = DateTime.UtcNow,
            OccurredAtUtc = DateTime.UtcNow
        });
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.Consume(context.Object));
        mongoCollection.Verify(
            collection => collection.ReplaceOneAsync(
                It.IsAny<FilterDefinition<StorefrontConfigDocument>>(),
                It.IsAny<StorefrontConfigDocument>(),
                It.IsAny<ReplaceOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ConsumePublishedEvent_WhenTenantIsInactive_FailsClosedAndDeletesProjection()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var storeConfigId = Guid.NewGuid();

        await using var seedContext = CreateDbContext(databaseName, tenantId);
        seedContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Inactive Projection Tenant",
            Slug = "inactive-projection-tenant",
            Currency = "USD",
            Status = TenantStatus.Active,
            IsActive = false
        });
        seedContext.StoreConfigs.Add(new StoreConfig
        {
            Id = storeConfigId,
            TenantId = tenantId,
            StoreName = "Inactive Store",
            ThemeId = "default",
            ThemePresetKey = "default"
        });
        await seedContext.SaveChangesAsync();

        var mongoCollection = new Mock<IMongoCollection<StorefrontConfigDocument>>();
        mongoCollection
            .Setup(collection => collection.DeleteOneAsync(
                It.IsAny<FilterDefinition<StorefrontConfigDocument>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<DeleteResult>(result => result.DeletedCount == 1));

        var mongoDatabase = new Mock<IMongoDatabase>();
        mongoDatabase
            .Setup(database => database.GetCollection<StorefrontConfigDocument>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
            .Returns(mongoCollection.Object);

        await using var testContext = CreateDbContext(databaseName, tenantId);
        var consumer = new StorefrontConfigProjectionConsumer(
            testContext,
            mongoDatabase.Object,
            new StorefrontPublishTelemetry(),
            new CommerceReliabilityTelemetry(),
            NullLogger<StorefrontConfigProjectionConsumer>.Instance);

        var context = new Mock<ConsumeContext<StoreConfigPublishedEvent>>();
        context.SetupGet(x => x.Message).Returns(new StoreConfigPublishedEvent
        {
            TenantId = tenantId,
            StoreConfigId = storeConfigId,
            ThemeId = "default",
            StoreName = "Inactive Store",
            UpdatedAtUtc = DateTime.UtcNow,
            OccurredAtUtc = DateTime.UtcNow,
            ConfigVersion = 4
        });
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(context.Object);

        mongoCollection.Verify(
            collection => collection.ReplaceOneAsync(
                It.IsAny<FilterDefinition<StorefrontConfigDocument>>(),
                It.IsAny<StorefrontConfigDocument>(),
                It.IsAny<ReplaceOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        mongoCollection.Verify(
            collection => collection.DeleteOneAsync(
                It.IsAny<FilterDefinition<StorefrontConfigDocument>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static ApplicationDbContext CreateDbContext(string databaseName, Guid tenantId)
    {
        var databaseRoot = DatabaseRoots.GetOrAdd(databaseName, _ => new InMemoryDatabaseRoot());
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }
}
