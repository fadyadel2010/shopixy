using System.Reflection;
using System.Text.Json;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.OpenApi.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Events.Affiliate;
using PointOfSale.Infrastructure.Consumers.Affiliate;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.HealthChecks;
using PointOfSale.Infrastructure.Jobs;
using PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;
using PointOfSale.API.Controllers;
using Swashbuckle.AspNetCore.Swagger;

namespace PointOfSale.Tests.Architecture;

public sealed class O47GapClosureRegressionTests
{
    [Fact]
    public void EarnedProjectionUpsert_MustNotWriteOneMongoPathThroughMultipleOperators()
    {
        var evt = new AffiliateCommissionEarnedEvent
        {
            OrderId = Guid.NewGuid(),
            OrderNumber = "ORD-REGRESSION",
            AffiliateAccountId = Guid.NewGuid(),
            Currency = "USD",
            TotalCommission = 4.25m,
            EventVersion = 3
        };
        var tenantId = Guid.NewGuid();
        var items = new List<AffiliateOrderItemSubDocument>();
        var method = typeof(AffiliateCommissionEarnedProjectionConsumer).GetMethod(
            "BuildOrderUpdate",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var update = Assert.IsAssignableFrom<UpdateDefinition<AffiliateOrderDocument>>(
            method!.Invoke(null, [evt, tenantId, evt.EventId.ToString(), items]));
        var serializer = BsonSerializer.LookupSerializer<AffiliateOrderDocument>();
        var rendered = update.Render(new RenderArgs<AffiliateOrderDocument>(
            serializer,
            BsonSerializer.SerializerRegistry));
        var setPaths = rendered["$set"].AsBsonDocument.Names.ToHashSet(StringComparer.Ordinal);
        var setOnInsertPaths = rendered["$setOnInsert"].AsBsonDocument.Names.ToHashSet(StringComparer.Ordinal);

        Assert.Empty(setPaths.Intersect(setOnInsertPaths, StringComparer.Ordinal));
        Assert.Contains("orderNumber", setPaths);
        Assert.DoesNotContain("orderNumber", setOnInsertPaths);
        Assert.Contains("affiliateAccountId", setPaths);
        Assert.DoesNotContain("affiliateAccountId", setOnInsertPaths);
    }

    [Fact]
    public void ReversalProjection_MustAccumulatePartialThenFullAmounts()
    {
        var orderItemId = Guid.NewGuid();
        var order = new AffiliateOrderDocument
        {
            OrderId = Guid.NewGuid(),
            TotalCommission = 40m,
            TotalReversed = 0m
        };
        var item = new AffiliateOrderItemSubDocument
        {
            OrderItemId = orderItemId,
            CommissionAmount = 40m
        };
        var method = typeof(AffiliateCommissionReversedProjectionConsumer).GetMethod(
            "CalculateReversalState",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var partial = method!.Invoke(null, [order, item, 4m, order.OrderId, orderItemId]);
        Assert.NotNull(partial);
        Assert.Equal(4m, partial!.GetType().GetProperty("ItemReversedAmount")!.GetValue(partial));
        Assert.Equal(false, partial.GetType().GetProperty("IsItemFullyReversed")!.GetValue(partial));
        Assert.Equal("PartiallyReversed", partial.GetType().GetProperty("OrderStatus")!.GetValue(partial));

        item.ReversedAmount = 4m;
        order.TotalReversed = 4m;
        var full = method.Invoke(null, [order, item, 36m, order.OrderId, orderItemId]);
        Assert.NotNull(full);
        Assert.Equal(40m, full!.GetType().GetProperty("ItemReversedAmount")!.GetValue(full));
        Assert.Equal(true, full.GetType().GetProperty("IsItemFullyReversed")!.GetValue(full));
        Assert.Equal(40m, full.GetType().GetProperty("OrderReversedAmount")!.GetValue(full));
        Assert.Equal("Reversed", full.GetType().GetProperty("OrderStatus")!.GetValue(full));
    }

    [Fact]
    public void ReversalProjection_MustNotDiscardDistinctEventsAtTheSameAggregateVersion()
    {
        var consistencyType = typeof(AffiliateCommissionReversedProjectionConsumer).Assembly.GetType(
            "PointOfSale.Infrastructure.Consumers.Affiliate.AffiliateMoneyProjectionConsistency");
        Assert.NotNull(consistencyType);
        var method = consistencyType!.GetMethod(
            "BuildReversalFreshFilter",
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);
        var filter = Assert.IsAssignableFrom<FilterDefinition<AffiliateOrderDocument>>(
            method!.Invoke(null,
            [
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid().ToString(),
                4m
            ]));
        var serializer = BsonSerializer.LookupSerializer<AffiliateOrderDocument>();
        var rendered = filter.Render(new RenderArgs<AffiliateOrderDocument>(
            serializer,
            BsonSerializer.SerializerRegistry)).ToJson();

        Assert.Contains("processedEventIds", rendered, StringComparison.Ordinal);
        Assert.Contains("reversedAmount", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("lastCommissionReversedEventVersion", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenericIntegrationOutbox_PublishesAndAcknowledgesPersistedMessage()
    {
        var databaseName = $"outbox-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        var eventId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
        var evt = new PaymentIntentCreatedEvent
        {
            EventId = eventId,
            OccurredAtUtc = occurredAtUtc,
            TenantId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            OrderNumber = "ORD-OUTBOX-REGRESSION",
            PaymentIntentId = Guid.NewGuid(),
            PaymentMethod = "COD",
            Provider = "COD",
            Amount = 12m,
            Currency = "USD",
            IsOfflineCollection = true,
            EventVersion = 2
        };
        var message = new OutboxMessage
        {
            EventType = typeof(PaymentIntentCreatedEvent).FullName!,
            EventPayload = JsonSerializer.Serialize(evt, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            TenantId = evt.TenantId,
            AggregateId = evt.OrderId,
            EventVersion = evt.EventVersion,
            CreatedAtUtc = DateTime.UtcNow
        };

        await using (var seed = new ApplicationDbContext(options))
        {
            seed.OutboxMessages.Add(message);
            await seed.SaveChangesAsync();
        }

        object? published = null;
        var publishEndpoint = new Mock<IPublishEndpoint>(MockBehavior.Strict);
        publishEndpoint
            .Setup(endpoint => endpoint.Publish(
                It.IsAny<object>(),
                It.IsAny<Type>(),
                It.IsAny<IPipe<PublishContext>>(),
                It.IsAny<CancellationToken>()))
            .Callback<object, Type, IPipe<PublishContext>, CancellationToken>((value, _, _, _) => published = value)
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddScoped(_ => new ApplicationDbContext(options));
        services.AddScoped(_ => publishEndpoint.Object);
        await using var provider = services.BuildServiceProvider();
        var job = new OutboxPublisherJob(provider, NullLogger<OutboxPublisherJob>.Instance);

        await job.ExecuteAsync();

        await using var verification = new ApplicationDbContext(options);
        var persisted = await verification.OutboxMessages.SingleAsync(row => row.Id == message.Id);
        var publishedEvent = Assert.IsType<PaymentIntentCreatedEvent>(published);
        Assert.Equal(evt.TenantId, publishedEvent.TenantId);
        Assert.Equal(evt.OrderId, publishedEvent.OrderId);
        Assert.Equal(eventId, publishedEvent.EventId);
        Assert.Equal(occurredAtUtc, publishedEvent.OccurredAtUtc);
        Assert.Equal(2, publishedEvent.EventVersion);
        Assert.True(publishedEvent.IsOfflineCollection);
        Assert.NotNull(persisted.ProcessedAtUtc);
        Assert.Equal(0, persisted.RetryCount);
    }

    [Fact]
    public async Task IntegrationOutboxReadiness_StaleZeroAttemptRowIsUnhealthy()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"outbox-health-{Guid.NewGuid():N}")
            .Options;
        await using var dbContext = new ApplicationDbContext(options);
        dbContext.OutboxMessages.Add(new OutboxMessage
        {
            EventType = typeof(PaymentIntentCreatedEvent).FullName!,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-20),
            RetryCount = 0
        });
        await dbContext.SaveChangesAsync();
        var check = new IntegrationOutboxReadinessHealthCheck(
            dbContext,
            NullLogger<IntegrationOutboxReadinessHealthCheck>.Instance);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(1, result.Data["ZeroAttemptCriticalLagCount"]);
    }

    [Fact]
    public void OutboxPublisher_MustBeARegisteredContinuousHostedService()
    {
        Assert.True(typeof(BackgroundService).IsAssignableFrom(typeof(OutboxPublisherJob)));
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");
        Assert.Contains("AddHostedService<PointOfSale.Infrastructure.Jobs.OutboxPublisherJob>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void CodCheckoutEvent_MustDescribeOfflineCollectionWithoutRawReferralLogging()
    {
        var checkout = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var projection = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateProjectionConsumers.cs");

        Assert.Contains("IsOfflineCollection = paymentMethod.Equals(\"COD\"", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain("Code: {Code}", projection, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenApiDocument_MustGenerateWithoutAmbiguousControllerActions()
    {
        var services = new ServiceCollection();
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(value => value.ApplicationName).Returns(typeof(ReportsController).Assembly.GetName().Name!);
        environment.SetupGet(value => value.EnvironmentName).Returns(Environments.Development);
        environment.SetupGet(value => value.ContentRootPath).Returns(AppContext.BaseDirectory);
        services.AddSingleton(environment.Object);
        services.AddSingleton<IHostEnvironment>(environment.Object);
        services.AddLogging();
        services
            .AddControllers()
            .AddApplicationPart(typeof(ReportsController).Assembly);
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "PointOfSale", Version = "v1" });
            options.CustomSchemaIds(type => (type.FullName ?? type.Name).Replace('+', '.'));
        });
        using var provider = services.BuildServiceProvider();

        var document = provider.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        Assert.NotEmpty(document.Paths);
    }
}
