using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Consumers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Storefront;

// Opt-in real Mongo suite: run with STORECONFIG_MONGO_INTEGRATION=1. Content is
// written exclusively by the application consumer in a disposable database.
public sealed class StoreConfigConvergenceTests
{
    [MongoIntegrationFact]
    public async Task RealMongo_LegacySchemaFence_Duplicate_OldEvent_Rollback_DraftIsolation_Restart_MissingRebuild()
    {
        await using var fixture = await Fixture.Create();
        var publishedA = fixture.Event(1, DateTime.UtcNow.AddMinutes(-2));
        publishedA.EventVersion = 2; // Historical schema-v2 watermark that broke delivery.
        await fixture.Consume(publishedA);
        Assert.Equal("A", await fixture.Title());
        await fixture.SetState(2, "B", "PRIVATE DRAFT");
        var publishedB = fixture.Event(2, DateTime.UtcNow);
        publishedB.EventVersion = 1; // Current transport schema must not suppress SQL revision 2.
        await fixture.Consume(publishedB);
        Assert.Equal("B", await fixture.Title());
        await fixture.Consume(publishedB); // New consumer/context: restart + duplicate.
        await fixture.Consume(fixture.Event(1, DateTime.UtcNow.AddMinutes(-1)));
        Assert.Equal(publishedB.EventId, (await fixture.Document()).LastEventId);
        Assert.Equal("B", await fixture.Title());
        await fixture.SetState(2, "B", "CHANGED DRAFT ONLY");
        await fixture.Consume(new StoreConfigUpdatedEvent { TenantId = fixture.TenantId, StoreConfigId = fixture.ConfigId });
        Assert.Equal("B", await fixture.Title());
        await fixture.SetState(3, "A", "CHANGED DRAFT ONLY");
        await fixture.Consume(publishedB); // Completed older EventId still recomposes a newer SQL publication.
        Assert.Equal("A", await fixture.Title());
        Assert.Equal(3, (await fixture.Document()).LiveVersion);
        var rollback = new StoreConfigRolledBackEvent { TenantId = fixture.TenantId, StoreConfigId = fixture.ConfigId, ConfigVersion = 1 };
        await fixture.Consume(rollback);
        Assert.Equal("A", await fixture.Title());
        Assert.Equal(3, (await fixture.Document()).LiveVersion); // Rollback is a NEW publication.
        await fixture.Collection.DeleteOneAsync(x => x.TenantId == fixture.TenantId); // Simulated derivative loss, isolated test database only.
        await fixture.Consume(rollback); // Completed SQL fence must not prevent rebuilding a missing derivative.
        Assert.Equal("A", await fixture.Title());
        Assert.Single(await fixture.Collection.Find(x => x.TenantId == fixture.TenantId).ToListAsync());
    }

    [MongoIntegrationFact]
    public async Task RealMongo_FailureIsRetryable_AndForeignEventCannotProjectForeignContent()
    {
        await using var fixture = await Fixture.Create();
        var message = fixture.Event(1, DateTime.UtcNow);
        var collection = new Mock<IMongoCollection<StorefrontConfigDocument>>();
        collection.Setup(x => x.FindAsync(It.IsAny<FilterDefinition<StorefrontConfigDocument>>(),
                It.IsAny<FindOptions<StorefrontConfigDocument, StorefrontConfigDocument>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Simulated Mongo outage"));
        var mongo = new Mock<IMongoDatabase>();
        mongo.Setup(x => x.GetCollection<StorefrontConfigDocument>("storefront_configs", It.IsAny<MongoCollectionSettings>()))
            .Returns(collection.Object);
        await Assert.ThrowsAsync<TimeoutException>(() => fixture.Consume(message, mongo.Object));
        await fixture.Consume(message); // Same EventId after failed attempt, fresh consumer.
        Assert.Equal("A", await fixture.Title());
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Consume(new StoreConfigPublishedEvent
        { TenantId = fixture.TenantId, StoreConfigId = Guid.NewGuid() }));
        Assert.Equal("A", await fixture.Title());
        var foreignTenant = Guid.NewGuid();
        await fixture.Consume(new StoreConfigPublishedEvent { TenantId = foreignTenant, StoreConfigId = fixture.ConfigId });
        Assert.Null(await fixture.Collection.Find(x => x.TenantId == foreignTenant).FirstOrDefaultAsync());
        Assert.Equal("A", await fixture.Title());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid ConfigId { get; } = Guid.NewGuid();
        private readonly string _sqlName = Guid.NewGuid().ToString("N");
        private readonly MongoClient _client;
        private readonly IMongoDatabase _mongo;
        public IMongoCollection<StorefrontConfigDocument> Collection => _mongo.GetCollection<StorefrontConfigDocument>("storefront_configs");
        private Fixture(MongoClient client) { _client = client; _mongo = client.GetDatabase("storeconfig_cert_" + Guid.NewGuid().ToString("N")); }
        public static async Task<Fixture> Create()
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "PointOfSale.sln"))) root = root.Parent;
            var config = new ConfigurationBuilder().SetBasePath(Path.Combine(root!.FullName, "PointOfSale.API"))
                .AddJsonFile("appsettings.json")
                .AddJsonFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets", "bbdf2e74-8790-4753-8874-f20fcfbdbbd5", "secrets.json"), true)
                .AddJsonFile("appsettings.Development.local.json", true).AddEnvironmentVariables().Build();
            var settings = MongoClientSettings.FromConnectionString(config["MongoDB:ConnectionString"]);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(15);
            var fixture = new Fixture(new MongoClient(settings));
            await fixture.Collection.Indexes.CreateOneAsync(new CreateIndexModel<StorefrontConfigDocument>(
                Builders<StorefrontConfigDocument>.IndexKeys.Ascending(x => x.TenantId), new CreateIndexOptions { Unique = true }));
            await using var db = fixture.Db();
            db.Tenants.Add(new Tenant { Id = fixture.TenantId, Name = "Disposable projection fixture", Slug = "projection-fixture", Currency = "EGP", Status = TenantStatus.Active, IsActive = true });
            db.StoreConfigs.Add(new StoreConfig { Id = fixture.ConfigId, TenantId = fixture.TenantId, StoreName = "Fixture", LiveVersion = 1, CustomizationJson = State("A", "PRIVATE DRAFT") });
            await db.SaveChangesAsync();
            return fixture;
        }
        private ApplicationDbContext Db() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(_sqlName, SharedInMemoryDatabaseRoot.Instance).Options, new TestTenantContext(TenantId));
        private static string State(string live, string draft) => JsonSerializer.Serialize(new { pages = new { home = new {
            live = new { sections = new[] { new { id = "hero", type = "hero-banner", enabled = true, order = 0, settings = new { title = live } } } },
            draft = new { sections = new[] { new { id = "hero", type = "hero-banner", enabled = true, order = 0, settings = new { title = draft } } } }
        } } });
        public async Task SetState(int version, string live, string draft)
        { await using var db = Db(); var c = await db.StoreConfigs.SingleAsync(); c.LiveVersion = version; c.CustomizationJson = State(live, draft); await db.SaveChangesAsync(); }
        public StoreConfigPublishedEvent Event(int version, DateTime at) => new() { TenantId = TenantId, StoreConfigId = ConfigId, ConfigVersion = version, OccurredAtUtc = at };
        public async Task Consume(StoreConfigUpdatedEvent message, IMongoDatabase? mongo = null)
        {
            await using var db = Db();
            var consumer = new StorefrontConfigProjectionConsumer(db, mongo ?? _mongo, new StorefrontPublishTelemetry(), new CommerceReliabilityTelemetry(), NullLogger<StorefrontConfigProjectionConsumer>.Instance);
            if (message is StoreConfigRolledBackEvent rollback) await consumer.Consume(Context(rollback));
            else if (message is StoreConfigPublishedEvent published) await consumer.Consume(Context(published));
            else await consumer.Consume(Context(message));
        }
        private static ConsumeContext<T> Context<T>(T message) where T : class
        { var context = new Mock<ConsumeContext<T>>(); context.SetupGet(c => c.Message).Returns(message); return context.Object; }
        public async Task<StorefrontConfigDocument> Document() => await Collection.Find(x => x.TenantId == TenantId).SingleAsync();
        public async Task<string> Title() => (await Document()).Pages.Home.Sections.Single().Settings["title"].ToString()!;
        public async ValueTask DisposeAsync() => await _client.DropDatabaseAsync(_mongo.DatabaseNamespace.DatabaseName);
    }
}

public sealed class MongoIntegrationFactAttribute : FactAttribute
{
    public MongoIntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("STORECONFIG_MONGO_INTEGRATION") != "1")
            Skip = "Opt-in real Mongo certification: set STORECONFIG_MONGO_INTEGRATION=1.";
    }
}
