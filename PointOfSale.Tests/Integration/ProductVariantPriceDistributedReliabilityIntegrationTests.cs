using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;
using Moq;
using Npgsql;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.Services.Background;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Cart;
using PointOfSale.API.Services.Pricing;
using PointOfSale.API.Services.ReadModels;
using PointOfSale.API.Services.Promotions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Infrastructure.Consumers.Affiliate;
using PointOfSale.Infrastructure.Consumers.Marketplace;
using PointOfSale.Infrastructure.Consumers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Configuration;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.Eventing;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Jobs;
using PointOfSale.Infrastructure.MongoDB.Configuration;
using PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;
using PointOfSale.Infrastructure.MongoDB.Documents.Marketplace;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services.Marketplace;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Observability;
using PointOfSale.Infrastructure.Promotions;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Infrastructure.ReadModels.Marketplace;
using PointOfSale.Tests.Infrastructure;
using StackExchange.Redis;
using Xunit.Sdk;
using MarketplaceEvents = PointOfSale.Core.Events.Marketplace;

namespace PointOfSale.Tests.Integration;

public sealed class ProductVariantPriceDistributedReliabilityIntegrationTests
{
    private static readonly DateTime SeededUtc = new(2026, 5, 15, 9, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions CaseInsensitiveJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Guid TenantId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90101");
    private static readonly Guid CategoryId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90102");
    private static readonly Guid ProductId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90103");
    private static readonly Guid DefaultVariantId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90104");
    private static readonly Guid TargetVariantId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90105");
    private static readonly Guid ActorAUserId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90106");
    private static readonly Guid ActorBUserId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90107");
    private static readonly Guid CommerceHubListingId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90108");
    private static readonly Guid BaselineStorefrontEventId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90109");
    private static readonly Guid BaselineCommerceHubEventId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90110");

    private const decimal InitialSellPrice = 100m;
    private const decimal ActorAWinningSellPrice = 120m;
    private const decimal ActorBLosingSellPrice = 140m;
    private const decimal ReplayWinningSellPrice = 150m;
    private const decimal PriceCon03WinningSellPrice = 165m;
    private const decimal PriceCon04FirstCommittedSellPrice = 120m;
    private const decimal PriceCon04SecondCommittedSellPrice = 150m;
    private const decimal PriceCon05FirstCommittedSellPrice = 120m;
    private const decimal PriceCon05SecondCommittedSellPrice = 180m;
    private const decimal PriceCon06CommittedSellPrice = 220m;
    private const decimal PriceCon07FirstCommittedSellPrice = 250m;
    private const decimal PriceCon07SecondCommittedSellPrice = 300m;
    private const decimal DefaultVariantSellPrice = 90m;
    private const decimal TargetVariantCostPrice = 60m;
    private const string DefaultVariantSku = "PRICE-CON-01-DEFAULT";
    private const string TargetVariantSku = "PRICE-CON-01-TARGET";
    private const string DefaultVariantBarcode = "PRICE-CON-01-DEFAULT-BAR";
    private const string TargetVariantBarcode = "PRICE-CON-01-TARGET-BAR";
    private const string ActorARequestId = "price-con-01-actor-a";
    private const string ActorBRequestId = "price-con-01-actor-b";
    private const string PriceCon02SharedRequestId = "price-con-02-active-replay";
    private const string PriceCon03RequestId = "price-con-03-default-variant";
    private const string PriceCon03StorefrontUrl = "https://price-con.example.test/products/price-con-default";
    private const string PriceCon04FirstRequestId = "price-con-04-v10";
    private const string PriceCon04SecondRequestId = "price-con-04-v11";
    private const string PriceCon05FirstRequestId = "price-con-05-v20";
    private const string PriceCon05SecondRequestId = "price-con-05-v21";
    private const string PriceCon06RequestId = "price-con-06-v30";
    private const string PriceCon07FirstRequestId = "price-con-07-v39";
    private const string PriceCon07SecondRequestId = "price-con-07-v40";
    private const long PriceCon05InitialProjectionMutationVersion = 19L;
    private const long PriceCon06InitialProjectionMutationVersion = 29L;
    private const long PriceCon07InitialProjectionMutationVersion = 38L;
    private const int PriceCon06DuplicateReplayAttempts = 3;
    private const int PriceCon07DuplicateRecoveryAttempts = 3;
    private static readonly Guid PriceCon07PreRecoveryStorefrontReplayEventId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90111");
    private static readonly Guid PriceCon07PreRecoveryVariantReplayEventId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90112");
    private static readonly Guid PriceCon07PreRecoveryCommerceHubReplayEventId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90113");
    private static readonly Guid PriceCon07PostRecoveryProductReplayEventId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90114");
    private static readonly Guid PriceCon07PostRecoveryVariantReplayEventId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90115");
    private static readonly Guid PriceCon07PostRecoveryCommerceHubReplayEventId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90116");
    private static readonly Guid PriceCon07PostRecoveryMarketplaceReplayEventId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90117");
    private static readonly Guid PriceCon08PromotionAId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90118");
    private static readonly Guid PriceCon08PromotionBId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90119");
    private static readonly Guid PriceCon08PromotionBReplayEventId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90120");
    private const string PriceCon08PromotionARequestId = "price-con-08-promo-a";
    private const string PriceCon08PromotionBRequestId = "price-con-08-promo-b";
    private const string PriceCon08ProductSlug = "price-con-08-default";
    private const decimal PriceCon08PromotionADiscountValue = 20m;
    private const decimal PriceCon08PromotionAMaxDiscountAmount = 15m;
    private const decimal PriceCon08PromotionBDiscountValue = 150m;
    private const decimal PriceCon08WinningVisiblePrice = 85m;
    private const int PriceCon08PromotionAPriority = 200;
    private const int PriceCon08PromotionBPriority = 100;
    private static readonly Guid PriceCon09ProductPromotionId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90121");
    private static readonly Guid PriceCon09CategoryPromotionId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90122");
    private static readonly Guid PriceCon09PausedProductReplayEventId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90123");
    private static readonly Guid PriceCon09PausedCategoryReplayEventId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90124");
    private static readonly Guid PriceCon09PostExpireProductReplayEventId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90125");
    private static readonly Guid PriceCon09PostExpireCategoryReplayEventId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90126");
    private static readonly Guid PriceCon09PostRebuildProductReplayEventId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90127");
    private static readonly Guid PriceCon09PostRebuildCategoryReplayEventId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90128");
    private static readonly Guid PriceCon09WarehouseId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90129");
    private static readonly Guid PriceCon09VariantStockId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90130");
    private const string PriceCon09ProductPromotionRequestId = "price-con-09-product-promo";
    private const string PriceCon09CategoryPromotionRequestId = "price-con-09-category-promo";
    private const string PriceCon09ProductSlug = "price-con-09-default";
    private const string PriceCon09CategorySlug = "price-con-09-category";
    private const string PriceCon09StoreSlug = "price-con-09-store";
    private const string PriceCon09CartOwnerId = "price-con-09-session";
    private const string PriceCon09CategoryBannerStyle = "gradient";
    private const decimal PriceCon09ProductPromotionDiscountValue = 20m;
    private const decimal PriceCon09ProductPromotionMaxDiscountAmount = 15m;
    private const decimal PriceCon09ExpiredVisiblePrice = 85m;
    private const decimal PriceCon09CategoryPromotionDiscountValue = 30m;
    private const int PriceCon09ProductPromotionPriority = 150;
    private const int PriceCon09CategoryPromotionPriority = 110;
    private static readonly Guid PriceCon10PostExpireProductReplayEventId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90131");
    private static readonly Guid PriceCon10PostExpireCategoryReplayEventId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90132");
    private static readonly Guid PriceCon10PostRepairProductReplayEventId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90133");
    private static readonly Guid PriceCon10PostRepairCategoryReplayEventId = Guid.Parse("f1f4d822-8e0f-4019-8cb6-0b0ff9c90134");

    [Fact]
    public void ProductDocument_ActivePromotionId_Deserializes_From_String_Field()
    {
        var promotionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var document = new BsonDocument
        {
            { "TenantId", Guid.NewGuid().ToString() },
            { "ProductId", Guid.NewGuid().ToString() },
            { "Name", "Diagnostic Product" },
            { "Price", 100m },
            { "PurchasePrice", 50m },
            { "CategoryId", Guid.NewGuid().ToString() },
            { "CreatedAtUtc", SeededUtc },
            { "SyncedAtUtc", SeededUtc },
            { "HasActivePromotion", true },
            { "ActivePromotionId", promotionId.ToString() }
        };

        var product = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<ProductDocument>(document);

        Assert.Equal(promotionId, product.ActivePromotionId);
    }

    [Fact]
    public async Task PRICE_CON_01_Concurrent_Sell_Price_Update_Against_Same_Variant_Converges_Safely()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var mongoScope = await CreateIsolatedMongoDatabaseScopeAsync("price-con-01");
        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);

        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext, mongoScope.Database);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");

        var progressEntries = new ConcurrentQueue<string>();
        var readRendezvous = new PriceVariantReadRendezvous(
            participantCount: 2,
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var overlapWindow = new PriceMutationOverlapWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);

        var actorA = new PriceActor(
            Name: "actor-a",
            RequestId: ActorARequestId,
            TraceIdentifier: "price-con-01-actor-a",
            UserId: ActorAUserId,
            RequestedSellPrice: ActorAWinningSellPrice);
        var actorB = new PriceActor(
            Name: "actor-b",
            RequestId: ActorBRequestId,
            TraceIdentifier: "price-con-01-actor-b",
            UserId: ActorBUserId,
            RequestedSellPrice: ActorBLosingSellPrice);

        var actorATask = ExecutePriceActorAsync(
            connectionString,
            tenantContext,
            seeded,
            actorA,
            progressEntries,
            new VariantPriceReadBarrierInterceptor(actorA.Name, readRendezvous, progressEntries),
            new PriceCommitPauseTransactionInterceptor(actorA.Name, overlapWindow));
        var actorBTask = ExecutePriceActorAsync(
            connectionString,
            tenantContext,
            seeded,
            actorB,
            progressEntries,
            new VariantPriceReadBarrierInterceptor(actorB.Name, readRendezvous, progressEntries),
            new StaleVariantPriceUpdateInterceptor(actorB.Name, overlapWindow, progressEntries));

        await overlapWindow.WaitForOriginalCommitPauseAsync();
        await overlapWindow.WaitForStaleUpdateAttemptAsync();

        Assert.Equal(2, readRendezvous.ArrivalCount);
        Assert.True(
            overlapWindow.OriginalPauseObservedBeforeStaleUpdateAttempt,
            $"PRICE-CON-01 did not force the stale writer to overlap the authoritative active transaction. Progress: {string.Join(" | ", progressEntries)}");

        await AssertNoUncommittedVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            progressEntries);

        overlapWindow.ReleaseOriginalCommit();

        PriceActorResult[] results;
        try
        {
            results = await Task.WhenAll(actorATask, actorBTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"PRICE-CON-01 timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.All(results, result => Assert.Null(result.Exception));
        Assert.True(
            overlapWindow.OriginalCommitPaused,
            $"PRICE-CON-01 never paused the winning pricing transaction before commit. Progress: {string.Join(" | ", progressEntries)}");

        var publishedEvents = await DispatchCommittedOutboxAsync(
            connectionString,
            tenantContext,
            progressEntries);

        await ApplyPublishedEventsAsync(
            connectionString,
            tenantContext,
            GetRequiredMongoConnectionString(),
            mongoScope.Database,
            publishedEvents);
        await ApplyPublishedEventsAsync(
            connectionString,
            tenantContext,
            GetRequiredMongoConnectionString(),
            mongoScope.Database,
            publishedEvents);

        await AssertFinalConvergenceAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            results,
            publishedEvents,
            progressEntries);
    }

    [Fact]
    public async Task PRICE_CON_02_Replay_During_Active_Price_Mutation_Converges_Safely()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var mongoScope = await CreateIsolatedMongoDatabaseScopeAsync("price-con-02");
        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);

        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext, mongoScope.Database);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");

        var progressEntries = new ConcurrentQueue<string>();
        var replayWindow = new PriceActiveTransactionReplayWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var originalActor = new PriceActor(
            Name: "request-a-original",
            RequestId: PriceCon02SharedRequestId,
            TraceIdentifier: "price-con-02-original-active",
            UserId: ActorAUserId,
            RequestedSellPrice: ReplayWinningSellPrice);
        var replayActor = new PriceActor(
            Name: "request-b-replay",
            RequestId: PriceCon02SharedRequestId,
            TraceIdentifier: "price-con-02-overlapping-replay",
            UserId: ActorBUserId,
            RequestedSellPrice: ReplayWinningSellPrice);

        var originalTask = ExecutePriceActorAsync(
            connectionString,
            tenantContext,
            seeded,
            originalActor,
            progressEntries,
            new PriceReplayCommitPauseTransactionInterceptor(originalActor.Name, replayWindow));

        await replayWindow.WaitForOriginalCommitPauseAsync();

        await AssertNoUncommittedReplayVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            progressEntries);

        var replayTask = ExecutePriceActorAsync(
            connectionString,
            tenantContext,
            seeded,
            replayActor,
            progressEntries,
            new PriceReplayClaimAttemptInterceptor(replayActor.Name, replayWindow));

        await replayWindow.WaitForReplayClaimAttemptAsync();
        Assert.True(
            replayWindow.OriginalWasPausedBeforeReplayClaim,
            $"PRICE-CON-02 did not force replay to overlap the active pricing mutation transaction. Progress: {string.Join(" | ", progressEntries)}");

        replayWindow.ReleaseOriginalCommit();

        PriceActorResult[] results;
        try
        {
            results = await Task.WhenAll(originalTask, replayTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"PRICE-CON-02 timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.True(
            replayWindow.OriginalCommitPaused,
            $"PRICE-CON-02 never paused the authoritative pricing transaction before commit. Progress: {string.Join(" | ", progressEntries)}");
        Assert.True(
            replayWindow.ReplayClaimAttempted,
            $"PRICE-CON-02 never attempted the overlapping replay idempotency claim. Progress: {string.Join(" | ", progressEntries)}");
        Assert.All(results, result => Assert.Null(result.Exception));

        var publishedEvents = await DispatchCommittedOutboxAsync(
            connectionString,
            tenantContext,
            progressEntries);

        await ApplyPublishedEventsAsync(
            connectionString,
            tenantContext,
            GetRequiredMongoConnectionString(),
            mongoScope.Database,
            publishedEvents);
        await ApplyPublishedEventsAsync(
            connectionString,
            tenantContext,
            GetRequiredMongoConnectionString(),
            mongoScope.Database,
            publishedEvents);

        await AssertReplayConvergenceAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            originalActor,
            replayActor,
            results,
            publishedEvents,
            progressEntries);
    }

    [Fact]
    public async Task PRICE_CON_03_Duplicate_Pricing_Worker_Delivery_During_Active_Processing_Converges_Safely()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var mongoScope = await CreateIsolatedMongoDatabaseScopeAsync("price-con-03");
        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);

        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedPriceCon03ScenarioAsync(seedContext, mongoScope.Database);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");

        var progressEntries = new ConcurrentQueue<string>();
        var canonicalActor = new PriceActor(
            Name: "price-con-03-authoritative-default-variant",
            RequestId: PriceCon03RequestId,
            TraceIdentifier: "price-con-03-authoritative-default-variant",
            UserId: ActorAUserId,
            RequestedSellPrice: PriceCon03WinningSellPrice);

        var canonicalResult = await ExecutePriceCon03CanonicalActorAsync(
            connectionString,
            tenantContext,
            seeded,
            canonicalActor,
            progressEntries);

        Assert.Null(canonicalResult.Exception);
        Assert.True(canonicalResult.Succeeded, canonicalResult.PayloadJson);
        Assert.NotNull(canonicalResult.Response);
        Assert.Equal(seeded.DefaultVariantId, canonicalResult.Response!.Id);
        Assert.Equal(PriceCon03WinningSellPrice, canonicalResult.Response.Price);

        var canonicalState = await LoadPriceCon03CanonicalStateAsync(
            scope,
            tenantContext,
            seeded,
            canonicalActor.RequestId);

        var marketplaceDispatch = await DispatchPriceCon03MarketplaceOutboxAsync(
            connectionString,
            tenantContext,
            progressEntries);

        Assert.Equal(1, marketplaceDispatch.ProcessedCount);
        Assert.NotNull(marketplaceDispatch.PublishedEvent);

        var overlapWindow = new PriceCon03ActiveProcessingOverlapWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var workerA = new PriceCon03MarketplaceWorker("price-con-03-marketplace-worker-a");
        var workerB = new PriceCon03MarketplaceWorker("price-con-03-marketplace-worker-b");

        var workerATask = ExecutePriceCon03MarketplaceWorkerAsync(
            connectionString,
            tenantContext,
            mongoScope.Database,
            marketplaceDispatch.PublishedEvent!,
            workerA,
            progressEntries,
            new PriceCon03ProjectionCommitPauseTransactionInterceptor(workerA.Name, overlapWindow));

        await overlapWindow.WaitForOriginalPauseAsync();
        await AssertPriceCon03NoUncommittedVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            canonicalState,
            marketplaceDispatch.PublishedEvent!,
            progressEntries);

        var workerBTask = ExecutePriceCon03MarketplaceWorkerAsync(
            connectionString,
            tenantContext,
            mongoScope.Database,
            marketplaceDispatch.PublishedEvent!,
            workerB,
            progressEntries,
            new PriceCon03ProjectionOutboxInsertAttemptInterceptor(workerB.Name, overlapWindow));

        await overlapWindow.WaitForDuplicateClaimAttemptAsync();
        Assert.True(
            overlapWindow.OriginalPauseObservedBeforeDuplicateClaimAttempt,
            $"PRICE-CON-03 duplicate marketplace pricing worker did not overlap the active SQL claim window. Progress: {string.Join(" | ", progressEntries)}");

        overlapWindow.ReleaseOriginalWorker();

        PriceCon03MarketplaceWorkerResult[] overlapResults;
        try
        {
            overlapResults = await Task.WhenAll(workerATask, workerBTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"PRICE-CON-03 marketplace pricing overlap timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.All(overlapResults, result => Assert.Null(result.Exception));

        var publishedEvents = await DispatchCommittedOutboxAsync(
            connectionString,
            tenantContext,
            progressEntries);

        await ApplyPublishedEventsAsync(
            connectionString,
            tenantContext,
            GetRequiredMongoConnectionString(),
            mongoScope.Database,
            publishedEvents);
        await ApplyPublishedEventsAsync(
            connectionString,
            tenantContext,
            GetRequiredMongoConnectionString(),
            mongoScope.Database,
            publishedEvents);

        await AssertPriceCon03ConvergenceAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            canonicalState,
            marketplaceDispatch.PublishedEvent!,
            overlapResults,
            publishedEvents,
            progressEntries);
    }

    [Fact]
    public async Task PRICE_CON_04_Out_Of_Order_Marketplace_Price_Propagation_Rejected_By_Version_Aware_Downstream_Consumers()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var mongoScope = await CreateIsolatedMongoDatabaseScopeAsync("price-con-04");
        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);

        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedPriceCon03ScenarioAsync(seedContext, mongoScope.Database);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");

        var progressEntries = new ConcurrentQueue<string>();
        var mutationA = new PriceActor(
            Name: "price-con-04-v10",
            RequestId: PriceCon04FirstRequestId,
            TraceIdentifier: "price-con-04-v10",
            UserId: ActorAUserId,
            RequestedSellPrice: PriceCon04FirstCommittedSellPrice);
        var mutationB = new PriceActor(
            Name: "price-con-04-v11",
            RequestId: PriceCon04SecondRequestId,
            TraceIdentifier: "price-con-04-v11",
            UserId: ActorBUserId,
            RequestedSellPrice: PriceCon04SecondCommittedSellPrice);

        var mutationAResult = await ExecutePriceCon03CanonicalActorAsync(
            connectionString,
            tenantContext,
            seeded,
            mutationA,
            progressEntries);
        var olderBundle = await CaptureCommittedPriceMutationBundleAsync(
            scope,
            connectionString,
            tenantContext,
            seeded,
            mutationA,
            mutationAResult,
            progressEntries);

        var mutationBResult = await ExecutePriceCon03CanonicalActorAsync(
            connectionString,
            tenantContext,
            seeded,
            mutationB,
            progressEntries);
        var newerBundle = await CaptureCommittedPriceMutationBundleAsync(
            scope,
            connectionString,
            tenantContext,
            seeded,
            mutationB,
            mutationBResult,
            progressEntries);

        Assert.True(
            olderBundle.ProductUpdatedEvent.EventVersion < newerBundle.ProductUpdatedEvent.EventVersion,
            "PRICE-CON-04 requires the older product event version to be lower than the newer committed product event version.");
        Assert.True(
            olderBundle.VariantUpdatedEvent.EventVersion < newerBundle.VariantUpdatedEvent.EventVersion,
            "PRICE-CON-04 requires the older variant projection version to be lower than the newer committed variant projection version.");
        Assert.True(
            olderBundle.PriceUpdatedEvent.EventVersion < newerBundle.PriceUpdatedEvent.EventVersion,
            "PRICE-CON-04 requires the older price projection version to be lower than the newer committed price projection version.");
        Assert.True(
            olderBundle.MarketplaceEvent.EventVersion < newerBundle.MarketplaceEvent.EventVersion,
            "PRICE-CON-04 requires the older marketplace projection version to be lower than the newer committed marketplace projection version.");

        // PRICE-CON-04 critical ordering:
        // V10 commits first in PostgreSQL, then V11 commits second, but downstream delivery is
        // intentionally inverted so every consumer sees V11 before the stale V10 replay.
        await ApplyPublishedEventsAsync(
            connectionString,
            tenantContext,
            GetRequiredMongoConnectionString(),
            mongoScope.Database,
            newerBundle.StandardPublishedEvents);

        var newerMarketplaceWorker = await ExecutePriceCon03MarketplaceWorkerAsync(
            connectionString,
            tenantContext,
            mongoScope.Database,
            newerBundle.MarketplaceEvent,
            new PriceCon03MarketplaceWorker("price-con-04-marketplace-worker-v11"),
            progressEntries);
        Assert.Null(newerMarketplaceWorker.Exception);

        var visibleAfterNewer = await CapturePriceCon04VisibilitySnapshotAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            olderBundle.ProductUpdatedEvent.EventId,
            newerBundle.ProductUpdatedEvent.EventId);

        await ApplyPublishedEventsAsync(
            connectionString,
            tenantContext,
            GetRequiredMongoConnectionString(),
            mongoScope.Database,
            olderBundle.StandardPublishedEvents);

        var staleMarketplaceWorker = await ExecutePriceCon03MarketplaceWorkerAsync(
            connectionString,
            tenantContext,
            mongoScope.Database,
            olderBundle.MarketplaceEvent,
            new PriceCon03MarketplaceWorker("price-con-04-marketplace-worker-v10"),
            progressEntries);
        Assert.Null(staleMarketplaceWorker.Exception);

        var visibleAfterStaleReplay = await CapturePriceCon04VisibilitySnapshotAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            olderBundle.ProductUpdatedEvent.EventId,
            newerBundle.ProductUpdatedEvent.EventId);

        Assert.Equal(PriceCon04SecondCommittedSellPrice, visibleAfterNewer.AuthoritativeProductPrice);
        Assert.Equal(PriceCon04SecondCommittedSellPrice, visibleAfterNewer.AuthoritativeVariantPrice);
        Assert.Equal(PriceCon04SecondCommittedSellPrice, visibleAfterNewer.StorefrontProductPrice);
        Assert.Equal(PriceCon04SecondCommittedSellPrice, visibleAfterNewer.StorefrontVariantPrice);
        Assert.Equal(PriceCon04SecondCommittedSellPrice, visibleAfterNewer.CommerceHubPrice);
        Assert.Equal(PriceCon04SecondCommittedSellPrice, visibleAfterNewer.AffiliateBasePrice);
        Assert.Equal(PriceCon04SecondCommittedSellPrice, visibleAfterNewer.MarketplaceReadModelPrice);

        Assert.Equal(visibleAfterNewer.AuthoritativeProductPrice, visibleAfterStaleReplay.AuthoritativeProductPrice);
        Assert.Equal(visibleAfterNewer.AuthoritativeVariantPrice, visibleAfterStaleReplay.AuthoritativeVariantPrice);
        Assert.Equal(visibleAfterNewer.StorefrontProductPrice, visibleAfterStaleReplay.StorefrontProductPrice);
        Assert.Equal(visibleAfterNewer.StorefrontVariantPrice, visibleAfterStaleReplay.StorefrontVariantPrice);
        Assert.Equal(visibleAfterNewer.StorefrontVariantEventVersion, visibleAfterStaleReplay.StorefrontVariantEventVersion);
        Assert.Equal(visibleAfterNewer.CommerceHubPrice, visibleAfterStaleReplay.CommerceHubPrice);
        Assert.Equal(visibleAfterNewer.CommerceHubLastEventId, visibleAfterStaleReplay.CommerceHubLastEventId);
        Assert.Equal(visibleAfterNewer.CommerceHubLastPriceEventVersion, visibleAfterStaleReplay.CommerceHubLastPriceEventVersion);
        Assert.Equal(visibleAfterNewer.AffiliateBasePrice, visibleAfterStaleReplay.AffiliateBasePrice);
        Assert.Equal(visibleAfterNewer.AffiliateLastProductEventVersion, visibleAfterStaleReplay.AffiliateLastProductEventVersion);
        Assert.Equal(visibleAfterNewer.MarketplaceReadModelPrice, visibleAfterStaleReplay.MarketplaceReadModelPrice);
        Assert.Equal(visibleAfterNewer.MarketplaceReadModelLastEventId, visibleAfterStaleReplay.MarketplaceReadModelLastEventId);
        Assert.Equal(visibleAfterNewer.MarketplaceReadModelLastEventVersion, visibleAfterStaleReplay.MarketplaceReadModelLastEventVersion);
        Assert.Equal(visibleAfterNewer.MarketplaceReadModelLastEntityMutationVersion, visibleAfterStaleReplay.MarketplaceReadModelLastEntityMutationVersion);

        Assert.False(visibleAfterStaleReplay.AffiliateContainsOlderProductEventId);
        Assert.True(visibleAfterStaleReplay.AffiliateContainsNewerProductEventId);
        Assert.Equal(2, visibleAfterStaleReplay.MarketplaceProcessedEventCount);
        Assert.Equal(0, visibleAfterStaleReplay.MarketplaceProjectionOutboxCount);
        Assert.Equal(1, visibleAfterStaleReplay.MarketplaceReadModelDocumentCount);

        Assert.Equal(newerBundle.ProductUpdatedEvent.EventVersion, visibleAfterStaleReplay.AffiliateLastProductEventVersion);
        Assert.Equal(newerBundle.PriceUpdatedEvent.EventVersion, visibleAfterStaleReplay.CommerceHubLastPriceEventVersion);
        Assert.Equal(newerBundle.PriceUpdatedEvent.EventId, visibleAfterStaleReplay.CommerceHubLastEventId);
        Assert.Equal(newerBundle.MarketplaceEvent.EventVersion, visibleAfterStaleReplay.MarketplaceReadModelLastEventVersion);
        Assert.Equal(newerBundle.MarketplaceEvent.EventId, visibleAfterStaleReplay.MarketplaceReadModelLastEventId);
        Assert.Equal(newerBundle.CanonicalState.ProductProjectionMutationVersion, visibleAfterStaleReplay.AuthoritativeProductProjectionMutationVersion);
        Assert.Equal(newerBundle.CanonicalState.VariantProjectionMutationVersion, visibleAfterStaleReplay.AuthoritativeVariantProjectionMutationVersion);

        await AssertPriceCon04DurableStateAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            olderBundle,
            newerBundle,
            progressEntries);
    }

    [Fact]
    public async Task PRICE_CON_05_Stale_Affiliate_Pricing_Replay_Rejected_After_Newer_Pricing_Truth_Propagates()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var mongoScope = await CreateIsolatedMongoDatabaseScopeAsync("price-con-05");
        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);

        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedPriceCon05ScenarioAsync(seedContext, mongoScope.Database);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");

        var progressEntries = new ConcurrentQueue<string>();
        var mutationA = new PriceActor(
            Name: "price-con-05-v20",
            RequestId: PriceCon05FirstRequestId,
            TraceIdentifier: "price-con-05-v20",
            UserId: ActorAUserId,
            RequestedSellPrice: PriceCon05FirstCommittedSellPrice);
        var mutationB = new PriceActor(
            Name: "price-con-05-v21",
            RequestId: PriceCon05SecondRequestId,
            TraceIdentifier: "price-con-05-v21",
            UserId: ActorBUserId,
            RequestedSellPrice: PriceCon05SecondCommittedSellPrice);

        var mutationAResult = await ExecutePriceCon03CanonicalActorAsync(
            connectionString,
            tenantContext,
            seeded,
            mutationA,
            progressEntries);
        var olderBundle = await CaptureCommittedPriceMutationBundleAsync(
            scope,
            connectionString,
            tenantContext,
            seeded,
            mutationA,
            mutationAResult,
            progressEntries);

        var mutationBResult = await ExecutePriceCon03CanonicalActorAsync(
            connectionString,
            tenantContext,
            seeded,
            mutationB,
            progressEntries);
        var newerBundle = await CaptureCommittedPriceMutationBundleAsync(
            scope,
            connectionString,
            tenantContext,
            seeded,
            mutationB,
            mutationBResult,
            progressEntries);

        var expectedV20 = ProductEventVersionHelper.ToProductEventVersion(
            PriceCon05InitialProjectionMutationVersion + 1L);
        var expectedV21 = ProductEventVersionHelper.ToProductEventVersion(
            PriceCon05InitialProjectionMutationVersion + 2L);

        Assert.Equal(expectedV20, olderBundle.ProductUpdatedEvent.EventVersion);
        Assert.Equal(expectedV20, olderBundle.VariantUpdatedEvent.EventVersion);
        Assert.Equal(expectedV20, olderBundle.PriceUpdatedEvent.EventVersion);
        Assert.Equal(expectedV20, olderBundle.MarketplaceEvent.EventVersion);
        Assert.Equal(expectedV21, newerBundle.ProductUpdatedEvent.EventVersion);
        Assert.Equal(expectedV21, newerBundle.VariantUpdatedEvent.EventVersion);
        Assert.Equal(expectedV21, newerBundle.PriceUpdatedEvent.EventVersion);
        Assert.Equal(expectedV21, newerBundle.MarketplaceEvent.EventVersion);

        await ApplyPublishedEventsAsync(
            connectionString,
            tenantContext,
            GetRequiredMongoConnectionString(),
            mongoScope.Database,
            newerBundle.StandardPublishedEvents);

        var newerMarketplaceWorker = await ExecutePriceCon03MarketplaceWorkerAsync(
            connectionString,
            tenantContext,
            mongoScope.Database,
            newerBundle.MarketplaceEvent,
            new PriceCon03MarketplaceWorker("price-con-05-marketplace-worker-v21"),
            progressEntries);
        Assert.Null(newerMarketplaceWorker.Exception);

        var visibleAfterNewer = await CapturePriceCon05VisibilitySnapshotAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            olderBundle.ProductUpdatedEvent.EventId,
            newerBundle.ProductUpdatedEvent.EventId);

        Assert.Equal(PriceCon05SecondCommittedSellPrice, visibleAfterNewer.AuthoritativeProductPrice);
        Assert.Equal(PriceCon05SecondCommittedSellPrice, visibleAfterNewer.AuthoritativeVariantPrice);
        Assert.Equal(PriceCon05SecondCommittedSellPrice, visibleAfterNewer.StorefrontProductPrice);
        Assert.Equal(PriceCon05SecondCommittedSellPrice, visibleAfterNewer.StorefrontVariantPrice);
        Assert.Equal(PriceCon05SecondCommittedSellPrice, visibleAfterNewer.CommerceHubPrice);
        Assert.Equal(PriceCon05SecondCommittedSellPrice, visibleAfterNewer.AffiliateBasePrice);
        Assert.Equal(PriceCon05SecondCommittedSellPrice, visibleAfterNewer.MarketplaceReadModelPrice);
        Assert.Equal(expectedV21, visibleAfterNewer.AffiliateLastProductEventVersion);
        Assert.False(visibleAfterNewer.AffiliateContainsOlderProductEventId);
        Assert.True(visibleAfterNewer.AffiliateContainsNewerProductEventId);
        Assert.Equal(1, visibleAfterNewer.AffiliateNewerProductEventIdOccurrenceCount);
        Assert.Equal(1, visibleAfterNewer.AffiliateProcessedEventIdCount);

        // PRICE-CON-05 critical timing:
        // first make V21 externally visible across the fresh-reader snapshots, then replay the
        // authoritative stale V20 affiliate event afterward. This proves correctness depends on
        // mutation version ordering, not on replay arrival timing.
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            progressEntries.Enqueue($"price-con-05:stale-affiliate-replay-attempt:{attempt}");
            await ReplayAffiliateProductUpdatedEventAsync(
                connectionString,
                tenantContext,
                GetRequiredMongoConnectionString(),
                mongoScope.Database,
                olderBundle.ProductUpdatedEvent);
        }

        var visibleAfterStaleReplay = await CapturePriceCon05VisibilitySnapshotAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            olderBundle.ProductUpdatedEvent.EventId,
            newerBundle.ProductUpdatedEvent.EventId);

        Assert.Equal(visibleAfterNewer.AuthoritativeProductPrice, visibleAfterStaleReplay.AuthoritativeProductPrice);
        Assert.Equal(visibleAfterNewer.AuthoritativeVariantPrice, visibleAfterStaleReplay.AuthoritativeVariantPrice);
        Assert.Equal(visibleAfterNewer.AuthoritativeProductProjectionMutationVersion, visibleAfterStaleReplay.AuthoritativeProductProjectionMutationVersion);
        Assert.Equal(visibleAfterNewer.AuthoritativeVariantProjectionMutationVersion, visibleAfterStaleReplay.AuthoritativeVariantProjectionMutationVersion);
        Assert.Equal(visibleAfterNewer.StorefrontProductPrice, visibleAfterStaleReplay.StorefrontProductPrice);
        Assert.Equal(visibleAfterNewer.StorefrontVariantPrice, visibleAfterStaleReplay.StorefrontVariantPrice);
        Assert.Equal(visibleAfterNewer.StorefrontVariantEventVersion, visibleAfterStaleReplay.StorefrontVariantEventVersion);
        Assert.Equal(visibleAfterNewer.CommerceHubPrice, visibleAfterStaleReplay.CommerceHubPrice);
        Assert.Equal(visibleAfterNewer.CommerceHubLastEventId, visibleAfterStaleReplay.CommerceHubLastEventId);
        Assert.Equal(visibleAfterNewer.CommerceHubLastPriceEventVersion, visibleAfterStaleReplay.CommerceHubLastPriceEventVersion);
        Assert.Equal(visibleAfterNewer.AffiliateBasePrice, visibleAfterStaleReplay.AffiliateBasePrice);
        Assert.Equal(visibleAfterNewer.AffiliateLastProductEventVersion, visibleAfterStaleReplay.AffiliateLastProductEventVersion);
        Assert.Equal(visibleAfterNewer.AffiliateLastUpdatedUtc, visibleAfterStaleReplay.AffiliateLastUpdatedUtc);
        Assert.Equal(visibleAfterNewer.AffiliateProcessedEventIdCount, visibleAfterStaleReplay.AffiliateProcessedEventIdCount);
        Assert.Equal(visibleAfterNewer.AffiliateNewerProductEventIdOccurrenceCount, visibleAfterStaleReplay.AffiliateNewerProductEventIdOccurrenceCount);
        Assert.Equal(visibleAfterNewer.AffiliateDocumentCount, visibleAfterStaleReplay.AffiliateDocumentCount);
        Assert.Equal(visibleAfterNewer.MarketplaceReadModelPrice, visibleAfterStaleReplay.MarketplaceReadModelPrice);
        Assert.Equal(visibleAfterNewer.MarketplaceReadModelLastEventId, visibleAfterStaleReplay.MarketplaceReadModelLastEventId);
        Assert.Equal(visibleAfterNewer.MarketplaceReadModelLastEventVersion, visibleAfterStaleReplay.MarketplaceReadModelLastEventVersion);
        Assert.Equal(visibleAfterNewer.MarketplaceReadModelLastEntityMutationVersion, visibleAfterStaleReplay.MarketplaceReadModelLastEntityMutationVersion);
        Assert.Equal(visibleAfterNewer.MarketplaceProcessedEventCount, visibleAfterStaleReplay.MarketplaceProcessedEventCount);
        Assert.Equal(visibleAfterNewer.MarketplaceProjectionOutboxCount, visibleAfterStaleReplay.MarketplaceProjectionOutboxCount);
        Assert.Equal(visibleAfterNewer.MarketplaceReadModelDocumentCount, visibleAfterStaleReplay.MarketplaceReadModelDocumentCount);

        Assert.False(visibleAfterStaleReplay.AffiliateContainsOlderProductEventId);
        Assert.True(visibleAfterStaleReplay.AffiliateContainsNewerProductEventId);
        Assert.Equal(1, visibleAfterStaleReplay.AffiliateNewerProductEventIdOccurrenceCount);
        Assert.Equal(1, visibleAfterStaleReplay.AffiliateProcessedEventIdCount);

        await AssertPriceCon05DurableStateAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            olderBundle,
            newerBundle,
            progressEntries);
    }

    [Fact]
    public async Task PRICE_CON_06_Duplicate_Marketplace_Price_Publish_Converges_To_One_Authoritative_Multi_Channel_Pricing_Truth()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var mongoScope = await CreateIsolatedMongoDatabaseScopeAsync("price-con-06");
        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);

        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedPriceCon06ScenarioAsync(seedContext, mongoScope.Database);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");

        var progressEntries = new ConcurrentQueue<string>();
        var canonicalActor = new PriceActor(
            Name: "price-con-06-v30",
            RequestId: PriceCon06RequestId,
            TraceIdentifier: "price-con-06-v30",
            UserId: ActorAUserId,
            RequestedSellPrice: PriceCon06CommittedSellPrice);

        var canonicalResult = await ExecutePriceCon03CanonicalActorAsync(
            connectionString,
            tenantContext,
            seeded,
            canonicalActor,
            progressEntries);
        var committedBundle = await CaptureCommittedPriceMutationBundleAsync(
            scope,
            connectionString,
            tenantContext,
            seeded,
            canonicalActor,
            canonicalResult,
            progressEntries);

        var expectedV30 = ProductEventVersionHelper.ToProductEventVersion(
            PriceCon06InitialProjectionMutationVersion + 1L);
        Assert.Equal(expectedV30, committedBundle.ProductUpdatedEvent.EventVersion);
        Assert.Equal(expectedV30, committedBundle.VariantUpdatedEvent.EventVersion);
        Assert.Equal(expectedV30, committedBundle.PriceUpdatedEvent.EventVersion);
        Assert.Equal(expectedV30, committedBundle.MarketplaceEvent.EventVersion);

        var overlapWindow = new PriceCon03ActiveProcessingOverlapWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var workerA = new PriceCon03MarketplaceWorker("price-con-06-marketplace-worker-a");
        var workerB = new PriceCon03MarketplaceWorker("price-con-06-marketplace-worker-b");

        var workerATask = ExecutePriceCon03MarketplaceWorkerAsync(
            connectionString,
            tenantContext,
            mongoScope.Database,
            committedBundle.MarketplaceEvent,
            workerA,
            progressEntries,
            new PriceCon03ProjectionCommitPauseTransactionInterceptor(workerA.Name, overlapWindow));

        await overlapWindow.WaitForOriginalPauseAsync();
        await AssertPriceCon06NoUncommittedVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            committedBundle,
            committedBundle.MarketplaceEvent,
            progressEntries);

        var workerBTask = ExecutePriceCon03MarketplaceWorkerAsync(
            connectionString,
            tenantContext,
            mongoScope.Database,
            committedBundle.MarketplaceEvent,
            workerB,
            progressEntries,
            new PriceCon03ProjectionOutboxInsertAttemptInterceptor(workerB.Name, overlapWindow));

        await overlapWindow.WaitForDuplicateClaimAttemptAsync();
        Assert.True(
            overlapWindow.OriginalPauseObservedBeforeDuplicateClaimAttempt,
            $"PRICE-CON-06 duplicate marketplace publish did not overlap the authoritative in-flight SQL claim window. Progress: {string.Join(" | ", progressEntries)}");

        overlapWindow.ReleaseOriginalWorker();

        PriceCon03MarketplaceWorkerResult[] overlapResults;
        try
        {
            overlapResults = await Task.WhenAll(workerATask, workerBTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"PRICE-CON-06 duplicate marketplace publish overlap timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.All(overlapResults, result => Assert.Null(result.Exception));

        // PRICE-CON-06 critical timing:
        // after the SQL-backed marketplace duplicate overlap converges, replay the same committed
        // storefront/CommerceHub/affiliate price events several more times to model publish storms,
        // retry amplification, and reclaim-safe duplicate downstream delivery.
        await ReplayDuplicatePriceCon06DownstreamPublishStormAsync(
            connectionString,
            tenantContext,
            GetRequiredMongoConnectionString(),
            mongoScope.Database,
            committedBundle.StandardPublishedEvents,
            PriceCon06DuplicateReplayAttempts,
            progressEntries);

        await AssertPriceCon06DurableStateAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            committedBundle,
            overlapResults,
            progressEntries);
    }

    [Fact]
    public async Task PRICE_CON_07_Partial_Multi_Channel_Pricing_Sync_Corruption_Converges_To_One_Authoritative_Pricing_Truth()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var mongoScope = await CreateIsolatedMongoDatabaseScopeAsync("price-con-07");
        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);

        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedPriceCon07ScenarioAsync(seedContext, mongoScope.Database);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");

        var progressEntries = new ConcurrentQueue<string>();
        var olderActor = new PriceActor(
            Name: "price-con-07-v39",
            RequestId: PriceCon07FirstRequestId,
            TraceIdentifier: "price-con-07-v39",
            UserId: ActorAUserId,
            RequestedSellPrice: PriceCon07FirstCommittedSellPrice);
        var newerActor = new PriceActor(
            Name: "price-con-07-v40",
            RequestId: PriceCon07SecondRequestId,
            TraceIdentifier: "price-con-07-v40",
            UserId: ActorBUserId,
            RequestedSellPrice: PriceCon07SecondCommittedSellPrice);

        var olderMutationResult = await ExecutePriceCon03CanonicalActorAsync(
            connectionString,
            tenantContext,
            seeded,
            olderActor,
            progressEntries);
        var olderBundle = await CaptureCommittedPriceMutationBundleAsync(
            scope,
            connectionString,
            tenantContext,
            seeded,
            olderActor,
            olderMutationResult,
            progressEntries);

        var expectedV39 = ProductEventVersionHelper.ToProductEventVersion(
            PriceCon07InitialProjectionMutationVersion + 1L);
        Assert.Equal(expectedV39, olderBundle.ProductUpdatedEvent.EventVersion);
        Assert.Equal(expectedV39, olderBundle.VariantUpdatedEvent.EventVersion);
        Assert.Equal(expectedV39, olderBundle.PriceUpdatedEvent.EventVersion);
        Assert.Equal(expectedV39, olderBundle.MarketplaceEvent.EventVersion);

        await ApplyPublishedEventsAsync(
            connectionString,
            tenantContext,
            GetRequiredMongoConnectionString(),
            mongoScope.Database,
            olderBundle.StandardPublishedEvents);

        var olderMarketplaceWorker = await ExecutePriceCon03MarketplaceWorkerAsync(
            connectionString,
            tenantContext,
            mongoScope.Database,
            olderBundle.MarketplaceEvent,
            new PriceCon03MarketplaceWorker("price-con-07-marketplace-worker-v39"),
            progressEntries);
        Assert.Null(olderMarketplaceWorker.Exception);

        var newerMutationResult = await ExecutePriceCon03CanonicalActorAsync(
            connectionString,
            tenantContext,
            seeded,
            newerActor,
            progressEntries);
        var newerBundle = await CaptureCommittedPriceMutationBundleAsync(
            scope,
            connectionString,
            tenantContext,
            seeded,
            newerActor,
            newerMutationResult,
            progressEntries);

        var expectedV40 = ProductEventVersionHelper.ToProductEventVersion(
            PriceCon07InitialProjectionMutationVersion + 2L);
        Assert.Equal(expectedV40, newerBundle.ProductUpdatedEvent.EventVersion);
        Assert.Equal(expectedV40, newerBundle.VariantUpdatedEvent.EventVersion);
        Assert.Equal(expectedV40, newerBundle.PriceUpdatedEvent.EventVersion);
        Assert.Equal(expectedV40, newerBundle.MarketplaceEvent.EventVersion);

        // PRICE-CON-07 critical split-brain setup:
        // commit V40 in PostgreSQL, let storefront and CommerceHub apply it, but intentionally
        // strand affiliate and marketplace on the already-converged V39 state. Recovery later
        // must heal the split without depending on queue timing or Redis.
        await ApplyPublishedEventsWithChannelControlAsync(
            connectionString,
            tenantContext,
            GetRequiredMongoConnectionString(),
            mongoScope.Database,
            newerBundle.StandardPublishedEvents,
            applyStorefrontProduct: true,
            applyStorefrontVariant: true,
            applyCommerceHub: true,
            applyAffiliate: false,
            progressEntries);

        var partiallyCorruptedVisibility = await CapturePriceCon05VisibilitySnapshotAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            olderBundle.ProductUpdatedEvent.EventId,
            newerBundle.ProductUpdatedEvent.EventId);

        Assert.Equal(PriceCon07SecondCommittedSellPrice, partiallyCorruptedVisibility.AuthoritativeProductPrice);
        Assert.Equal(PriceCon07SecondCommittedSellPrice, partiallyCorruptedVisibility.AuthoritativeVariantPrice);
        Assert.Equal(PriceCon07SecondCommittedSellPrice, partiallyCorruptedVisibility.StorefrontProductPrice);
        Assert.Equal(PriceCon07SecondCommittedSellPrice, partiallyCorruptedVisibility.StorefrontVariantPrice);
        Assert.Equal(PriceCon07SecondCommittedSellPrice, partiallyCorruptedVisibility.CommerceHubPrice);
        Assert.Equal(PriceCon07FirstCommittedSellPrice, partiallyCorruptedVisibility.AffiliateBasePrice);
        Assert.Equal(PriceCon07FirstCommittedSellPrice, partiallyCorruptedVisibility.MarketplaceReadModelPrice);
        Assert.Equal(expectedV40, partiallyCorruptedVisibility.StorefrontVariantEventVersion);
        Assert.Equal(expectedV40, partiallyCorruptedVisibility.CommerceHubLastPriceEventVersion);
        Assert.Equal(expectedV39, partiallyCorruptedVisibility.AffiliateLastProductEventVersion);
        Assert.Equal(expectedV39, partiallyCorruptedVisibility.MarketplaceReadModelLastEventVersion);
        Assert.True(partiallyCorruptedVisibility.AffiliateContainsOlderProductEventId);
        Assert.False(partiallyCorruptedVisibility.AffiliateContainsNewerProductEventId);
        Assert.Equal(1, partiallyCorruptedVisibility.AffiliateProcessedEventIdCount);
        Assert.Equal(1, partiallyCorruptedVisibility.MarketplaceProcessedEventCount);
        Assert.Equal(1, partiallyCorruptedVisibility.MarketplaceReadModelDocumentCount);
        Assert.Equal(0, partiallyCorruptedVisibility.MarketplaceProjectionOutboxCount);

        var preRecoveryReplayOccurredAtUtc = newerBundle.ProductUpdatedEvent.OccurredAtUtc.AddMinutes(5);
        var preRecoveryStorefrontReplay = CloneProductUpdatedEventForReplay(
            olderBundle.ProductUpdatedEvent,
            PriceCon07PreRecoveryStorefrontReplayEventId,
            preRecoveryReplayOccurredAtUtc,
            "price-con-07-pre-recovery-storefront-replay");
        var preRecoveryVariantReplay = CloneProductVariantUpdatedEventForReplay(
            olderBundle.VariantUpdatedEvent,
            PriceCon07PreRecoveryVariantReplayEventId,
            preRecoveryReplayOccurredAtUtc,
            "price-con-07-pre-recovery-variant-replay");
        var preRecoveryCommerceHubReplay = CloneVariantPriceUpdatedEventForReplay(
            olderBundle.PriceUpdatedEvent,
            PriceCon07PreRecoveryCommerceHubReplayEventId,
            preRecoveryReplayOccurredAtUtc,
            "price-con-07-pre-recovery-commercehub-replay");

        await ApplyPublishedEventsWithChannelControlAsync(
            connectionString,
            tenantContext,
            GetRequiredMongoConnectionString(),
            mongoScope.Database,
            [preRecoveryStorefrontReplay, preRecoveryVariantReplay, preRecoveryCommerceHubReplay],
            applyStorefrontProduct: true,
            applyStorefrontVariant: true,
            applyCommerceHub: true,
            applyAffiliate: false,
            progressEntries);

        var afterPreRecoveryStaleReplay = await CapturePriceCon05VisibilitySnapshotAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            olderBundle.ProductUpdatedEvent.EventId,
            newerBundle.ProductUpdatedEvent.EventId);

        Assert.Equal(partiallyCorruptedVisibility.StorefrontProductPrice, afterPreRecoveryStaleReplay.StorefrontProductPrice);
        Assert.Equal(partiallyCorruptedVisibility.StorefrontVariantPrice, afterPreRecoveryStaleReplay.StorefrontVariantPrice);
        Assert.Equal(partiallyCorruptedVisibility.StorefrontVariantEventVersion, afterPreRecoveryStaleReplay.StorefrontVariantEventVersion);
        Assert.Equal(partiallyCorruptedVisibility.CommerceHubPrice, afterPreRecoveryStaleReplay.CommerceHubPrice);
        Assert.Equal(partiallyCorruptedVisibility.CommerceHubLastPriceEventVersion, afterPreRecoveryStaleReplay.CommerceHubLastPriceEventVersion);
        Assert.Equal(partiallyCorruptedVisibility.AffiliateBasePrice, afterPreRecoveryStaleReplay.AffiliateBasePrice);
        Assert.Equal(partiallyCorruptedVisibility.MarketplaceReadModelPrice, afterPreRecoveryStaleReplay.MarketplaceReadModelPrice);

        var overlapWindow = new PriceCon03ActiveProcessingOverlapWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var workerA = new PriceCon03MarketplaceWorker("price-con-07-marketplace-worker-a");
        var workerB = new PriceCon03MarketplaceWorker("price-con-07-marketplace-worker-b");

        var workerATask = ExecutePriceCon03MarketplaceWorkerAsync(
            connectionString,
            tenantContext,
            mongoScope.Database,
            newerBundle.MarketplaceEvent,
            workerA,
            progressEntries,
            new PriceCon03ProjectionCommitPauseTransactionInterceptor(workerA.Name, overlapWindow));
        await overlapWindow.WaitForOriginalPauseAsync();

        var workerBTask = ExecutePriceCon03MarketplaceWorkerAsync(
            connectionString,
            tenantContext,
            mongoScope.Database,
            newerBundle.MarketplaceEvent,
            workerB,
            progressEntries,
            new PriceCon03ProjectionOutboxInsertAttemptInterceptor(workerB.Name, overlapWindow));
        await overlapWindow.WaitForDuplicateClaimAttemptAsync();

        Assert.True(
            overlapWindow.OriginalPauseObservedBeforeDuplicateClaimAttempt,
            $"PRICE-CON-07 duplicate marketplace recovery did not overlap the authoritative SQL claim window. Progress: {string.Join(" | ", progressEntries)}");

        overlapWindow.ReleaseOriginalWorker();

        var overlapResults = await Task.WhenAll(workerATask, workerBTask).WaitAsync(TimeSpan.FromSeconds(120));
        Assert.All(overlapResults, result => Assert.Null(result.Exception));

        var affiliateRecoveryTasks = Enumerable.Range(1, PriceCon07DuplicateRecoveryAttempts)
            .Select(async attempt =>
            {
                progressEntries.Enqueue($"price-con-07:affiliate-recovery-attempt-start:{attempt}");
                await ReplayAffiliateProductUpdatedEventAsync(
                    connectionString,
                    tenantContext,
                    GetRequiredMongoConnectionString(),
                    mongoScope.Database,
                    newerBundle.ProductUpdatedEvent);
                progressEntries.Enqueue($"price-con-07:affiliate-recovery-attempt-complete:{attempt}");
            });
        await Task.WhenAll(affiliateRecoveryTasks);

        await ReplayDuplicatePriceCon06DownstreamPublishStormAsync(
            connectionString,
            tenantContext,
            GetRequiredMongoConnectionString(),
            mongoScope.Database,
            newerBundle.StandardPublishedEvents,
            PriceCon07DuplicateRecoveryAttempts,
            progressEntries);

        var postRecoveryReplayOccurredAtUtc = newerBundle.ProductUpdatedEvent.OccurredAtUtc.AddMinutes(10);
        var postRecoveryProductReplay = CloneProductUpdatedEventForReplay(
            olderBundle.ProductUpdatedEvent,
            PriceCon07PostRecoveryProductReplayEventId,
            postRecoveryReplayOccurredAtUtc,
            "price-con-07-post-recovery-product-replay");
        var postRecoveryVariantReplay = CloneProductVariantUpdatedEventForReplay(
            olderBundle.VariantUpdatedEvent,
            PriceCon07PostRecoveryVariantReplayEventId,
            postRecoveryReplayOccurredAtUtc,
            "price-con-07-post-recovery-variant-replay");
        var postRecoveryCommerceHubReplay = CloneVariantPriceUpdatedEventForReplay(
            olderBundle.PriceUpdatedEvent,
            PriceCon07PostRecoveryCommerceHubReplayEventId,
            postRecoveryReplayOccurredAtUtc,
            "price-con-07-post-recovery-commercehub-replay");
        var postRecoveryMarketplaceReplay = CloneMarketplaceProductUpdatedEventForReplay(
            olderBundle.MarketplaceEvent,
            PriceCon07PostRecoveryMarketplaceReplayEventId,
            newerBundle.MarketplaceEvent.OccurredAtUtc.AddMinutes(10),
            "price-con-07-post-recovery-marketplace-replay");

        await ApplyPublishedEventsWithChannelControlAsync(
            connectionString,
            tenantContext,
            GetRequiredMongoConnectionString(),
            mongoScope.Database,
            [postRecoveryProductReplay, postRecoveryVariantReplay, postRecoveryCommerceHubReplay],
            applyStorefrontProduct: true,
            applyStorefrontVariant: true,
            applyCommerceHub: true,
            applyAffiliate: true,
            progressEntries);

        var staleMarketplaceWorker = await ExecutePriceCon03MarketplaceWorkerAsync(
            connectionString,
            tenantContext,
            mongoScope.Database,
            postRecoveryMarketplaceReplay,
            new PriceCon03MarketplaceWorker("price-con-07-marketplace-worker-stale-replay"),
            progressEntries);
        Assert.Null(staleMarketplaceWorker.Exception);

        var finalVisibility = await CapturePriceCon05VisibilitySnapshotAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            olderBundle.ProductUpdatedEvent.EventId,
            newerBundle.ProductUpdatedEvent.EventId);

        Assert.Equal(PriceCon07SecondCommittedSellPrice, finalVisibility.AuthoritativeProductPrice);
        Assert.Equal(PriceCon07SecondCommittedSellPrice, finalVisibility.AuthoritativeVariantPrice);
        Assert.Equal(PriceCon07SecondCommittedSellPrice, finalVisibility.StorefrontProductPrice);
        Assert.Equal(PriceCon07SecondCommittedSellPrice, finalVisibility.StorefrontVariantPrice);
        Assert.Equal(PriceCon07SecondCommittedSellPrice, finalVisibility.CommerceHubPrice);
        Assert.Equal(PriceCon07SecondCommittedSellPrice, finalVisibility.AffiliateBasePrice);
        Assert.Equal(PriceCon07SecondCommittedSellPrice, finalVisibility.MarketplaceReadModelPrice);
        Assert.Equal(expectedV40, finalVisibility.StorefrontVariantEventVersion);
        Assert.Equal(expectedV40, finalVisibility.CommerceHubLastPriceEventVersion);
        Assert.Equal(expectedV40, finalVisibility.AffiliateLastProductEventVersion);
        Assert.Equal(expectedV40, finalVisibility.MarketplaceReadModelLastEventVersion);
        Assert.True(finalVisibility.AffiliateContainsOlderProductEventId);
        Assert.True(finalVisibility.AffiliateContainsNewerProductEventId);
        Assert.Equal(2, finalVisibility.AffiliateProcessedEventIdCount);
        // The stale post-recovery marketplace replay can be fenced either by the
        // processed-event ledger or by a pre-completion stale guard. In both
        // cases the authoritative V40 read model must remain visible and the
        // replay must not amplify beyond a single additional durable record.
        Assert.InRange(finalVisibility.MarketplaceProcessedEventCount, 2, 3);
        Assert.Equal(1, finalVisibility.MarketplaceReadModelDocumentCount);
        Assert.Equal(0, finalVisibility.MarketplaceProjectionOutboxCount);

        await AssertPriceCon07DurableStateAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            olderBundle,
            newerBundle,
            progressEntries);
    }

    [Fact]
    public async Task PRICE_CON_08_Concurrent_Promotion_Activation_Against_Same_Variant_Converges_To_One_Deterministic_Overlay()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var mongoScope = await CreateIsolatedMongoDatabaseScopeAsync("price-con-08");
        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);

        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedPriceCon08ScenarioAsync(seedContext, mongoScope.Database);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");

        var progressEntries = new ConcurrentQueue<string>();
        var overlapWindow = new PromotionCommitPauseWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var actorA = new PromotionActor(
            Name: "promotion-a-actor",
            RequestId: PriceCon08PromotionARequestId,
            TraceIdentifier: "price-con-08-promotion-a",
            UserId: ActorAUserId,
            PromotionId: seeded.PromotionAId);
        var actorB = new PromotionActor(
            Name: "promotion-b-actor",
            RequestId: PriceCon08PromotionBRequestId,
            TraceIdentifier: "price-con-08-promotion-b",
            UserId: ActorBUserId,
            PromotionId: seeded.PromotionBId);

        var actorATask = ExecutePromotionActorAsync(
            connectionString,
            tenantContext,
            actorA,
            progressEntries,
            new PromotionCommitPauseTransactionInterceptor(actorA.Name, overlapWindow));

        await overlapWindow.WaitForOriginalCommitPauseAsync();
        await AssertPriceCon08NoPromotionVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            expectedActivePromotionCount: 0,
            expectedCommittedOutboxCount: 0,
            progressEntries);

        var actorBResult = await ExecutePromotionActorAsync(
            connectionString,
            tenantContext,
            actorB,
            progressEntries);

        Assert.Null(actorBResult.Exception);
        Assert.True(actorBResult.Succeeded, actorBResult.PayloadJson ?? "PRICE-CON-08 losing promotion activation did not commit.");
        Assert.True(
            overlapWindow.OriginalCommitPaused,
            $"PRICE-CON-08 failed to pause the higher-priority promotion activation before the competing activation committed. Progress: {string.Join(" | ", progressEntries)}");

        await AssertPriceCon08NoPromotionVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            expectedActivePromotionCount: 1,
            expectedCommittedOutboxCount: 1,
            progressEntries);

        overlapWindow.ReleaseOriginalCommit();

        PromotionActorResult actorAResult;
        try
        {
            actorAResult = await actorATask.WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"PRICE-CON-08 timed out waiting for the higher-priority promotion activation to commit. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.Null(actorAResult.Exception);
        Assert.True(actorAResult.Succeeded, actorAResult.PayloadJson ?? "PRICE-CON-08 winning promotion activation did not commit.");

        var publishedEvents = await DispatchCommittedOutboxAsync(
            connectionString,
            tenantContext,
            progressEntries);
        var promotionEvents = publishedEvents
            .OfType<PromotionUpdatedEvent>()
            .OrderBy(@event => @event.OccurredAtUtc)
            .ThenBy(@event => @event.PromotionId)
            .ToList();

        Assert.Equal(2, promotionEvents.Count);

        var promotionAEvent = Assert.Single(promotionEvents.Where(@event => @event.PromotionId == seeded.PromotionAId));
        var promotionBEvent = Assert.Single(promotionEvents.Where(@event => @event.PromotionId == seeded.PromotionBId));

        Assert.Equal("product", promotionAEvent.Scope);
        Assert.Equal(seeded.ProductId, promotionAEvent.AppliesToId);
        Assert.Equal("product", promotionBEvent.Scope);
        Assert.Equal(seeded.ProductId, promotionBEvent.AppliesToId);

        var promotionBReplay = ClonePromotionUpdatedEventForReplay(
            promotionBEvent,
            PriceCon08PromotionBReplayEventId,
            promotionAEvent.OccurredAtUtc.AddMinutes(5),
            "price-con-08-promotion-b-replay");

        // PRICE-CON-08 critical ordering:
        // The lower-priority fixed-amount overlay is replayed after the higher-priority
        // percentage overlay to prove visible pricing converges from current SQL truth,
        // not from transport arrival order or duplicate delivery timing.
        var stressedEvents = new IntegrationEvent[]
        {
            promotionAEvent,
            promotionBEvent,
            promotionBEvent,
            promotionBReplay
        };

        await ApplyPublishedEventsAsync(
            connectionString,
            tenantContext,
            GetRequiredMongoConnectionString(),
            mongoScope.Database,
            stressedEvents);
        await ApplyPublishedEventsAsync(
            connectionString,
            tenantContext,
            GetRequiredMongoConnectionString(),
            mongoScope.Database,
            stressedEvents);

        await AssertPriceCon08DurableStateAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            promotionAEvent,
            promotionBEvent,
            progressEntries);

        await using var repairDbContext = CreateDbContext(connectionString, tenantContext);
        var repairService = new DiscountReadModelRepairService(
            repairDbContext,
            CreateMongoDbContext(
                GetRequiredMongoConnectionString(),
                mongoScope.Database.DatabaseNamespace.DatabaseName),
            CreateUnifiedSearchIndexServiceMock().Object,
            CreateProductCacheInvalidatorMock().Object,
            NullLogger<DiscountReadModelRepairService>.Instance);
        await repairService.RebuildPromotionsAsync(TenantId);

        await AssertPriceCon08DurableStateAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            promotionAEvent,
            promotionBEvent,
            progressEntries);
    }

    [Fact]
    public async Task PRICE_CON_09_Promotion_Expiration_Replay_Overlap_Preserves_Authoritative_Expired_State()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var mongoScope = await CreateIsolatedMongoDatabaseScopeAsync("price-con-09");
        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);

        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedPriceCon09ScenarioAsync(seedContext, mongoScope.Database);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");
        var mongoConnectionString = GetRequiredMongoConnectionString();
        var progressEntries = new ConcurrentQueue<string>();

        var productPromotionActor = new PromotionActor(
            Name: "price-con-09-product-activation",
            RequestId: PriceCon09ProductPromotionRequestId,
            TraceIdentifier: "price-con-09-product-activation",
            UserId: ActorAUserId,
            PromotionId: seeded.ProductPromotionId);
        var categoryPromotionActor = new PromotionActor(
            Name: "price-con-09-category-activation",
            RequestId: PriceCon09CategoryPromotionRequestId,
            TraceIdentifier: "price-con-09-category-activation",
            UserId: ActorBUserId,
            PromotionId: seeded.CategoryPromotionId);

        var productActivationResult = await ExecutePromotionActorAsync(
            connectionString,
            tenantContext,
            productPromotionActor,
            progressEntries);
        var categoryActivationResult = await ExecutePromotionActorAsync(
            connectionString,
            tenantContext,
            categoryPromotionActor,
            progressEntries);

        Assert.Null(productActivationResult.Exception);
        Assert.Null(categoryActivationResult.Exception);
        Assert.True(productActivationResult.Succeeded, productActivationResult.PayloadJson ?? "PRICE-CON-09 product promotion activation did not commit.");
        Assert.True(categoryActivationResult.Succeeded, categoryActivationResult.PayloadJson ?? "PRICE-CON-09 category promotion activation did not commit.");

        var activationEvents = (await DispatchCommittedOutboxAsync(
                connectionString,
                tenantContext,
                progressEntries))
            .OfType<PromotionUpdatedEvent>()
            .OrderBy(@event => @event.OccurredAtUtc)
            .ThenBy(@event => @event.PromotionId)
            .ToList();

        Assert.Equal(2, activationEvents.Count);

        var productActivationEvent = Assert.Single(
            activationEvents.Where(@event => @event.PromotionId == seeded.ProductPromotionId));
        var categoryActivationEvent = Assert.Single(
            activationEvents.Where(@event => @event.PromotionId == seeded.CategoryPromotionId));

        Assert.Equal(2, productActivationEvent.Version);
        Assert.Equal(2, categoryActivationEvent.Version);
        Assert.True(productActivationEvent.IsActive);
        Assert.True(categoryActivationEvent.IsActive);

        await ApplyPublishedEventsAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            activationEvents);

        using var redisMultiplexer = CreatePriceCon09RedisMultiplexer();
        var distributedLockService = new InMemoryDistributedLockService();
        var cartOwner = new CartOwner
        {
            OwnerType = "session",
            OwnerId = $"{PriceCon09CartOwnerId}-{mongoScope.Database.DatabaseNamespace.DatabaseName}"
        };

        await using (var cartSeedService = CreatePriceCon09CartService(
                         connectionString,
                         tenantContext,
                         mongoConnectionString,
                         mongoScope.Database,
                         redisMultiplexer,
                         distributedLockService))
        {
            var seededCart = await cartSeedService.Service.AddItemAsync(
                TenantId,
                cartOwner,
                new AddCartItemRequest
                {
                    ProductId = seeded.ProductId,
                    Quantity = 1
                },
                affiliateModeOnly: false,
                correlationId: "price-con-09-cart-seed");

            var seededItem = Assert.Single(seededCart.Items);
            Assert.True(seededItem.HasActivePromotion);
            Assert.Equal(PriceCon09ExpiredVisiblePrice, seededItem.DiscountedUnitPrice);
        }

        await AssertPriceCon09ActiveStateAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            mongoConnectionString,
            redisMultiplexer,
            distributedLockService,
            cartOwner,
            seeded,
            productActivationEvent,
            categoryActivationEvent,
            progressEntries);

        var overlapWindow = new PromotionCommitPauseWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);

        using var expirationWorkerProvider = CreatePromotionExpirationWorkerServiceProvider(
            connectionString,
            tenantContext,
            correlationId: "price-con-09-expiration-worker",
            timeProvider: new FixedUtcTimeProvider(seeded.PromotionEndDateUtc.AddMinutes(5)),
            interceptors: new IInterceptor[]
            {
                new PromotionCommitPauseTransactionInterceptor("price-con-09-expiration-worker", overlapWindow)
            });
        var expirationWorker = new PromotionExpirationService(
            expirationWorkerProvider,
            NullLogger<PromotionExpirationService>.Instance,
            new FixedUtcTimeProvider(seeded.PromotionEndDateUtc.AddMinutes(5)));
        var expirationTask = ExecutePromotionExpirationWorkerOnceAsync(expirationWorker);

        await overlapWindow.WaitForOriginalCommitPauseAsync();

        var pausedReplayEvents = new IntegrationEvent[]
        {
            ClonePromotionUpdatedEventForReplay(
                productActivationEvent,
                PriceCon09PausedProductReplayEventId,
                seeded.PromotionEndDateUtc.AddMinutes(6),
                "price-con-09-paused-product-replay"),
            ClonePromotionUpdatedEventForReplay(
                categoryActivationEvent,
                PriceCon09PausedCategoryReplayEventId,
                seeded.PromotionEndDateUtc.AddMinutes(6),
                "price-con-09-paused-category-replay")
        };

        await ApplyPublishedEventsAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            pausedReplayEvents);

        await AssertPriceCon09ActiveStateAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            mongoConnectionString,
            redisMultiplexer,
            distributedLockService,
            cartOwner,
            seeded,
            productActivationEvent,
            categoryActivationEvent,
            progressEntries);

        overlapWindow.ReleaseOriginalCommit();
        await expirationTask.WaitAsync(TimeSpan.FromSeconds(120));

        var expiredEvents = (await DispatchCommittedOutboxAsync(
                connectionString,
                tenantContext,
                progressEntries))
            .OfType<PromotionExpiredEvent>()
            .OrderBy(@event => @event.OccurredAtUtc)
            .ThenBy(@event => @event.PromotionId)
            .ToList();

        Assert.Equal(2, expiredEvents.Count);
        Assert.All(expiredEvents, @event => Assert.Equal(3, @event.Version));

        var productExpiredEvent = Assert.Single(
            expiredEvents.Where(@event => @event.PromotionId == seeded.ProductPromotionId));
        var categoryExpiredEvent = Assert.Single(
            expiredEvents.Where(@event => @event.PromotionId == seeded.CategoryPromotionId));

        await ApplyPublishedEventsAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            expiredEvents);

        await AssertPriceCon09ExpiredStateAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            mongoConnectionString,
            redisMultiplexer,
            distributedLockService,
            cartOwner,
            seeded,
            productExpiredEvent,
            categoryExpiredEvent,
            progressEntries);

        var postExpireReplayEvents = new IntegrationEvent[]
        {
            ClonePromotionUpdatedEventForReplay(
                productActivationEvent,
                PriceCon09PostExpireProductReplayEventId,
                productExpiredEvent.OccurredAtUtc.AddMinutes(5),
                "price-con-09-post-expire-product-replay"),
            ClonePromotionUpdatedEventForReplay(
                categoryActivationEvent,
                PriceCon09PostExpireCategoryReplayEventId,
                categoryExpiredEvent.OccurredAtUtc.AddMinutes(5),
                "price-con-09-post-expire-category-replay")
        };

        await ApplyPublishedEventsAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            postExpireReplayEvents);
        await ApplyPublishedEventsAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            postExpireReplayEvents);

        await AssertPriceCon09ExpiredStateAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            mongoConnectionString,
            redisMultiplexer,
            distributedLockService,
            cartOwner,
            seeded,
            productExpiredEvent,
            categoryExpiredEvent,
            progressEntries);

        await PoisonPriceCon09ReadModelsAsync(mongoScope.Database, seeded);

        await using (var repairDbContext = CreateDbContext(connectionString, tenantContext))
        {
            var repairService = new DiscountReadModelRepairService(
                repairDbContext,
                CreateMongoDbContext(
                    mongoConnectionString,
                    mongoScope.Database.DatabaseNamespace.DatabaseName),
                CreateUnifiedSearchIndexServiceMock().Object,
                CreateProductCacheInvalidatorMock().Object,
                NullLogger<DiscountReadModelRepairService>.Instance);
            await repairService.RebuildPromotionsAsync(TenantId);
        }

        await AssertPriceCon09ExpiredStateAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            mongoConnectionString,
            redisMultiplexer,
            distributedLockService,
            cartOwner,
            seeded,
            productExpiredEvent,
            categoryExpiredEvent,
            progressEntries,
            requirePromotionLastEventIds: false);

        var postRebuildReplayEvents = new IntegrationEvent[]
        {
            ClonePromotionUpdatedEventForReplay(
                productActivationEvent,
                PriceCon09PostRebuildProductReplayEventId,
                productExpiredEvent.OccurredAtUtc.AddMinutes(10),
                "price-con-09-post-rebuild-product-replay"),
            ClonePromotionUpdatedEventForReplay(
                categoryActivationEvent,
                PriceCon09PostRebuildCategoryReplayEventId,
                categoryExpiredEvent.OccurredAtUtc.AddMinutes(10),
                "price-con-09-post-rebuild-category-replay")
        };

        await ApplyPublishedEventsAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            postRebuildReplayEvents);
        await ApplyPublishedEventsAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            postRebuildReplayEvents);

        await AssertPriceCon09ExpiredStateAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            mongoConnectionString,
            redisMultiplexer,
            distributedLockService,
            cartOwner,
            seeded,
            productExpiredEvent,
            categoryExpiredEvent,
            progressEntries,
            requirePromotionLastEventIds: false);
    }

    [Fact]
    public async Task PRICE_CON_10_Duplicate_Promotion_Sync_Worker_Delivery_Converges_Safely()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var mongoScope = await CreateIsolatedMongoDatabaseScopeAsync("price-con-10");
        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);

        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedPriceCon09ScenarioAsync(seedContext, mongoScope.Database);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");
        var mongoConnectionString = GetRequiredMongoConnectionString();
        var progressEntries = new ConcurrentQueue<string>();

        var productPromotionActor = new PromotionActor(
            Name: "price-con-10-product-activation",
            RequestId: $"{PriceCon09ProductPromotionRequestId}-duplicate-worker",
            TraceIdentifier: "price-con-10-product-activation",
            UserId: ActorAUserId,
            PromotionId: seeded.ProductPromotionId);
        var categoryPromotionActor = new PromotionActor(
            Name: "price-con-10-category-activation",
            RequestId: $"{PriceCon09CategoryPromotionRequestId}-duplicate-worker",
            TraceIdentifier: "price-con-10-category-activation",
            UserId: ActorBUserId,
            PromotionId: seeded.CategoryPromotionId);

        var productActivationResult = await ExecutePromotionActorAsync(
            connectionString,
            tenantContext,
            productPromotionActor,
            progressEntries);
        var categoryActivationResult = await ExecutePromotionActorAsync(
            connectionString,
            tenantContext,
            categoryPromotionActor,
            progressEntries);

        Assert.Null(productActivationResult.Exception);
        Assert.Null(categoryActivationResult.Exception);
        Assert.True(productActivationResult.Succeeded, productActivationResult.PayloadJson);
        Assert.True(categoryActivationResult.Succeeded, categoryActivationResult.PayloadJson);

        var activationEvents = (await DispatchCommittedOutboxAsync(
                connectionString,
                tenantContext,
                progressEntries))
            .OfType<PromotionUpdatedEvent>()
            .OrderBy(@event => @event.OccurredAtUtc)
            .ThenBy(@event => @event.PromotionId)
            .ToList();

        var productActivationEvent = Assert.Single(
            activationEvents.Where(@event => @event.PromotionId == seeded.ProductPromotionId));
        var categoryActivationEvent = Assert.Single(
            activationEvents.Where(@event => @event.PromotionId == seeded.CategoryPromotionId));

        await ApplyPublishedEventsAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            activationEvents);

        using var redisMultiplexer = CreatePriceCon09RedisMultiplexer();
        var distributedLockService = new InMemoryDistributedLockService();
        var cartOwner = new CartOwner
        {
            OwnerType = "session",
            OwnerId = $"{PriceCon09CartOwnerId}-price-con-10-{mongoScope.Database.DatabaseNamespace.DatabaseName}"
        };

        await using (var cartSeedService = CreatePriceCon09CartService(
                         connectionString,
                         tenantContext,
                         mongoConnectionString,
                         mongoScope.Database,
                         redisMultiplexer,
                         distributedLockService))
        {
            await cartSeedService.Service.AddItemAsync(
                TenantId,
                cartOwner,
                new AddCartItemRequest
                {
                    ProductId = seeded.ProductId,
                    Quantity = 1
                },
                affiliateModeOnly: false,
                correlationId: "price-con-10-cart-seed");
        }

        await AssertPriceCon09ActiveStateAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            mongoConnectionString,
            redisMultiplexer,
            distributedLockService,
            cartOwner,
            seeded,
            productActivationEvent,
            categoryActivationEvent,
            progressEntries);

        await PoisonPriceCon10PromotionDecorationsOnlyAsync(mongoScope.Database, seeded);

        // Same EventId/version duplicate delivery must rerun SQL-backed overlay recomposition.
        // This catches the partial-projection case where the promotion document exists but
        // product/category visibility was not yet refreshed.
        await ApplyPublishedEventsAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            [productActivationEvent, categoryActivationEvent, productActivationEvent, categoryActivationEvent]);

        await AssertPriceCon09ActiveStateAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            mongoConnectionString,
            redisMultiplexer,
            distributedLockService,
            cartOwner,
            seeded,
            productActivationEvent,
            categoryActivationEvent,
            progressEntries);

        var overlapWindow = new PriceCon10PromotionProjectionOverlapWindow(
            TimeSpan.FromSeconds(30),
            progressEntries);
        var workerATask = ConsumePriceCon10PromotionEventWithSearchPauseAsync(
            connectionString,
            tenantContext,
            mongoScope.Database,
            productActivationEvent,
            "price-con-10-worker-a",
            overlapWindow);

        await overlapWindow.WaitForWorkerPauseAsync();

        var workerBTask = ApplyPublishedEventsAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            [productActivationEvent, productActivationEvent]);

        await AssertPriceCon10ActiveOverlapVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            productActivationEvent,
            progressEntries);

        overlapWindow.ReleaseWorker();
        await Task.WhenAll(workerATask, workerBTask).WaitAsync(TimeSpan.FromSeconds(120));

        await AssertPriceCon09ActiveStateAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            mongoConnectionString,
            redisMultiplexer,
            distributedLockService,
            cartOwner,
            seeded,
            productActivationEvent,
            categoryActivationEvent,
            progressEntries);

        using var expirationWorkerProvider = CreatePromotionExpirationWorkerServiceProvider(
            connectionString,
            tenantContext,
            correlationId: "price-con-10-expiration-worker",
            timeProvider: new FixedUtcTimeProvider(seeded.PromotionEndDateUtc.AddMinutes(5)),
            interceptors: []);
        var expirationWorker = new PromotionExpirationService(
            expirationWorkerProvider,
            NullLogger<PromotionExpirationService>.Instance,
            new FixedUtcTimeProvider(seeded.PromotionEndDateUtc.AddMinutes(5)));
        await ExecutePromotionExpirationWorkerOnceAsync(expirationWorker);

        var expiredEvents = (await DispatchCommittedOutboxAsync(
                connectionString,
                tenantContext,
                progressEntries))
            .OfType<PromotionExpiredEvent>()
            .OrderBy(@event => @event.OccurredAtUtc)
            .ThenBy(@event => @event.PromotionId)
            .ToList();

        var productExpiredEvent = Assert.Single(
            expiredEvents.Where(@event => @event.PromotionId == seeded.ProductPromotionId));
        var categoryExpiredEvent = Assert.Single(
            expiredEvents.Where(@event => @event.PromotionId == seeded.CategoryPromotionId));

        await ApplyPublishedEventsAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            [productExpiredEvent, categoryExpiredEvent, productExpiredEvent, categoryExpiredEvent]);

        await AssertPriceCon09ExpiredStateAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            mongoConnectionString,
            redisMultiplexer,
            distributedLockService,
            cartOwner,
            seeded,
            productExpiredEvent,
            categoryExpiredEvent,
            progressEntries);

        var stalePostExpireReplayEvents = new IntegrationEvent[]
        {
            ClonePromotionUpdatedEventForReplay(
                productActivationEvent,
                PriceCon10PostExpireProductReplayEventId,
                productExpiredEvent.OccurredAtUtc.AddMinutes(5),
                "price-con-10-post-expire-product-replay"),
            ClonePromotionUpdatedEventForReplay(
                categoryActivationEvent,
                PriceCon10PostExpireCategoryReplayEventId,
                categoryExpiredEvent.OccurredAtUtc.AddMinutes(5),
                "price-con-10-post-expire-category-replay")
        };

        await ApplyPublishedEventsAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            stalePostExpireReplayEvents);
        await AssertPriceCon09ExpiredStateAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            mongoConnectionString,
            redisMultiplexer,
            distributedLockService,
            cartOwner,
            seeded,
            productExpiredEvent,
            categoryExpiredEvent,
            progressEntries);

        await PoisonPriceCon09ReadModelsAsync(mongoScope.Database, seeded);
        await RunPriceCon10DuplicatePromotionRepairAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database);

        await AssertPriceCon09ExpiredStateAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            mongoConnectionString,
            redisMultiplexer,
            distributedLockService,
            cartOwner,
            seeded,
            productExpiredEvent,
            categoryExpiredEvent,
            progressEntries,
            requirePromotionLastEventIds: false);

        var stalePostRepairReplayEvents = new IntegrationEvent[]
        {
            ClonePromotionUpdatedEventForReplay(
                productActivationEvent,
                PriceCon10PostRepairProductReplayEventId,
                productExpiredEvent.OccurredAtUtc.AddMinutes(10),
                "price-con-10-post-repair-product-replay"),
            ClonePromotionUpdatedEventForReplay(
                categoryActivationEvent,
                PriceCon10PostRepairCategoryReplayEventId,
                categoryExpiredEvent.OccurredAtUtc.AddMinutes(10),
                "price-con-10-post-repair-category-replay")
        };

        await ApplyPublishedEventsAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            stalePostRepairReplayEvents);

        await AssertPriceCon09ExpiredStateAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            mongoConnectionString,
            redisMultiplexer,
            distributedLockService,
            cartOwner,
            seeded,
            productExpiredEvent,
            categoryExpiredEvent,
            progressEntries,
            requirePromotionLastEventIds: false);
    }

    private static async Task<PriceScenarioSeed> SeedScenarioAsync(
        ApplicationDbContext context,
        IMongoDatabase mongoDatabase)
    {
        await EnsureOutboxSchemaAsync(context);

        context.Tenants.Add(new Tenant
        {
            Id = TenantId,
            Name = "PRICE-CON-01 Tenant",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });

        context.Categories.Add(new Category
        {
            Id = CategoryId,
            TenantId = TenantId,
            Name = "PRICE-CON-01 Category",
            CreatedAt = SeededUtc
        });

        context.Products.Add(new Product
        {
            Id = ProductId,
            TenantId = TenantId,
            Name = "PRICE-CON-01 Product",
            CategoryId = CategoryId,
            PurchasePrice = TargetVariantCostPrice,
            SalePrice = InitialSellPrice,
            OnlinePrice = null,
            PriceIncludesTax = false,
            TaxRate = 0m,
            Unit = "Piece",
            IsActive = true,
            TrackStock = false,
            HasVariants = true,
            IsOnlineVisible = true,
            PublishStatus = PublishStatus.Published,
            CreatedAt = SeededUtc
        });

        context.ProductVariants.AddRange(
            new ProductVariant
            {
                Id = DefaultVariantId,
                TenantId = TenantId,
                ProductId = ProductId,
                SKU = DefaultVariantSku,
                Barcode = DefaultVariantBarcode,
                Price = DefaultVariantSellPrice,
                OnlinePrice = null,
                CostPrice = TargetVariantCostPrice,
                IsDefault = true,
                IsActive = true,
                SortOrder = 0,
                CreatedAt = SeededUtc
            },
            new ProductVariant
            {
                Id = TargetVariantId,
                TenantId = TenantId,
                ProductId = ProductId,
                SKU = TargetVariantSku,
                Barcode = TargetVariantBarcode,
                Price = InitialSellPrice,
                OnlinePrice = null,
                CostPrice = TargetVariantCostPrice,
                IsDefault = false,
                IsActive = true,
                SortOrder = 1,
                CreatedAt = SeededUtc
            });

        await context.SaveChangesAsync();

        var product = await context.Products.SingleAsync(entry => entry.Id == ProductId);
        var defaultVariant = await context.ProductVariants.SingleAsync(entry => entry.Id == DefaultVariantId);
        var targetVariant = await context.ProductVariants.SingleAsync(entry => entry.Id == TargetVariantId);

        var seeded = new PriceScenarioSeed(
            TenantId,
            ProductId,
            DefaultVariantId,
            TargetVariantId,
            product.ProjectionMutationVersion,
            defaultVariant.ProjectionMutationVersion,
            targetVariant.ProjectionMutationVersion);

        await SeedMongoReadModelsAsync(mongoDatabase, seeded);
        return seeded;
    }

    private static async Task SeedMongoReadModelsAsync(
        IMongoDatabase mongoDatabase,
        PriceScenarioSeed seeded)
    {
        var initialEventVersion = ProductEventVersionHelper.ToProductEventVersion(seeded.TargetVariantProjectionMutationVersion);

        var productDocument = new ProductDocument
        {
            TenantId = seeded.TenantId,
            ProductId = seeded.ProductId,
            Name = "PRICE-CON-01 Product",
            SKU = DefaultVariantSku,
            Barcode = DefaultVariantBarcode,
            Price = InitialSellPrice,
            OnlinePrice = null,
            PurchasePrice = TargetVariantCostPrice,
            CategoryId = CategoryId,
            IsOnlineVisible = true,
            IsActive = true,
            TrackStock = false,
            HasVariants = true,
            CreatedAtUtc = SeededUtc,
            SyncedAtUtc = SeededUtc,
            LastEventId = BaselineStorefrontEventId,
            LastEventOccurredAtUtc = SeededUtc,
            LastEventVersion = ProductEventVersionHelper.ToProductEventVersion(seeded.ProductProjectionMutationVersion),
            Variants =
            [
                new ProductVariantDocument
                {
                    VariantId = DefaultVariantId,
                    SKU = DefaultVariantSku,
                    Barcode = DefaultVariantBarcode,
                    Price = DefaultVariantSellPrice,
                    OnlinePrice = null,
                    CostPrice = TargetVariantCostPrice,
                    IsDefault = true,
                    IsActive = true,
                    TotalStock = 0m,
                    SortOrder = 0,
                    SyncedAtUtc = SeededUtc,
                    LastEventOccurredAtUtc = SeededUtc,
                    LastEventVersion = ProductEventVersionHelper.ToProductEventVersion(seeded.DefaultVariantProjectionMutationVersion)
                },
                new ProductVariantDocument
                {
                    VariantId = seeded.TargetVariantId,
                    SKU = TargetVariantSku,
                    Barcode = TargetVariantBarcode,
                    Price = InitialSellPrice,
                    OnlinePrice = null,
                    CostPrice = TargetVariantCostPrice,
                    IsDefault = false,
                    IsActive = true,
                    TotalStock = 0m,
                    SortOrder = 1,
                    SyncedAtUtc = SeededUtc,
                    LastEventOccurredAtUtc = SeededUtc,
                    LastEventVersion = initialEventVersion
                }
            ]
        };

        var listingDocument = new CommerceHubProductDocument
        {
            Id = CommerceHubListingId,
            TenantId = seeded.TenantId,
            StoreName = "PRICE-CON Store",
            ProductId = seeded.ProductId,
            VariantId = seeded.TargetVariantId,
            ProductName = productDocument.Name,
            Price = InitialSellPrice,
            CategoryId = CategoryId,
            Currency = "EGP",
            StoreShippingFee = 0m,
            InStock = true,
            AvailableStock = 10m,
            IsPublished = true,
            CreatedAtUtc = SeededUtc,
            UpdatedAtUtc = SeededUtc,
            LastEventId = BaselineCommerceHubEventId,
            LastListingEventOccurredAtUtc = SeededUtc,
            LastListingEventVersion = initialEventVersion,
            LastPriceEventTimestamp = SeededUtc,
            LastPriceEventVersion = initialEventVersion,
            SearchSyncStatus = CommerceHubProductDocument.SearchSyncSynced,
            SearchRetryCount = 0
        };

        await mongoDatabase.GetCollection<ProductDocument>("products").InsertOneAsync(productDocument);
        await mongoDatabase.GetCollection<CommerceHubProductDocument>("commercehub_products").InsertOneAsync(listingDocument);
    }

    private static async Task<PriceCon03ScenarioSeed> SeedPriceCon03ScenarioAsync(
        ApplicationDbContext context,
        IMongoDatabase mongoDatabase,
        long initialProjectionMutationVersion = 1L)
    {
        await EnsureOutboxSchemaAsync(context);
        var defaultUnitId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        context.Tenants.Add(new Tenant
        {
            Id = TenantId,
            Name = "PRICE-CON-03 Tenant",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });

        context.Categories.Add(new Category
        {
            Id = CategoryId,
            TenantId = TenantId,
            Name = "PRICE-CON-03 Category",
            CreatedAt = SeededUtc
        });

        context.Units.Add(new PointOfSale.Core.Entities.Unit
        {
            Id = defaultUnitId,
            TenantId = TenantId,
            Name = "Piece",
            Symbol = "pc",
            CreatedAt = SeededUtc
        });

        context.Products.Add(new Product
        {
            Id = ProductId,
            TenantId = TenantId,
            Name = "PRICE-CON-03 Product",
            SKU = DefaultVariantSku,
            Barcode = DefaultVariantBarcode,
            Slug = "price-con-default",
            CategoryId = CategoryId,
            PurchasePrice = TargetVariantCostPrice,
            SalePrice = InitialSellPrice,
            OnlinePrice = null,
            PriceIncludesTax = false,
            TaxRate = 0m,
            Unit = "Piece",
            IsActive = true,
            TrackStock = false,
            HasVariants = false,
            IsOnlineVisible = true,
            PublishStatus = PublishStatus.Published,
            CreatedAt = SeededUtc
        });

        context.ProductUnits.Add(new ProductUnit
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            ProductId = ProductId,
            UnitId = defaultUnitId,
            ConversionFactor = 1m,
            PurchasePrice = TargetVariantCostPrice,
            SellingPrice = InitialSellPrice,
            IsDefault = true,
            IsBaseUnit = true,
            CreatedAt = SeededUtc
        });

        context.ProductVariants.Add(new ProductVariant
        {
            Id = DefaultVariantId,
            TenantId = TenantId,
            ProductId = ProductId,
            SKU = DefaultVariantSku,
            Barcode = DefaultVariantBarcode,
            Price = InitialSellPrice,
            OnlinePrice = null,
            CostPrice = TargetVariantCostPrice,
            IsDefault = true,
            IsActive = true,
            SortOrder = 0,
            CreatedAt = SeededUtc
        });

        if (initialProjectionMutationVersion > 1L)
        {
            context.Products.Local.Single(entry => entry.Id == ProductId)
                .AdvanceProjectionMutationVersion(initialProjectionMutationVersion);
            context.ProductVariants.Local.Single(entry => entry.Id == DefaultVariantId)
                .AdvanceProjectionMutationVersion(initialProjectionMutationVersion);
        }

        await context.SaveChangesAsync();

        var product = await context.Products.SingleAsync(entry => entry.Id == ProductId);
        var variant = await context.ProductVariants.SingleAsync(entry => entry.Id == DefaultVariantId);
        var seeded = new PriceCon03ScenarioSeed(
            TenantId,
            ProductId,
            DefaultVariantId,
            product.ProjectionMutationVersion,
            variant.ProjectionMutationVersion);

        await SeedPriceCon03MongoReadModelsAsync(mongoDatabase, seeded);
        return seeded;
    }

    private static Task<PriceCon03ScenarioSeed> SeedPriceCon05ScenarioAsync(
        ApplicationDbContext context,
        IMongoDatabase mongoDatabase)
    {
        // Seed V19 so the two authoritative commits in PRICE-CON-05 become deterministic V20/V21.
        return SeedPriceCon03ScenarioAsync(
            context,
            mongoDatabase,
            PriceCon05InitialProjectionMutationVersion);
    }

    private static Task<PriceCon03ScenarioSeed> SeedPriceCon06ScenarioAsync(
        ApplicationDbContext context,
        IMongoDatabase mongoDatabase)
    {
        // Seed V29 so the authoritative commit in PRICE-CON-06 becomes deterministic V30.
        return SeedPriceCon03ScenarioAsync(
            context,
            mongoDatabase,
            PriceCon06InitialProjectionMutationVersion);
    }

    private static Task<PriceCon03ScenarioSeed> SeedPriceCon07ScenarioAsync(
        ApplicationDbContext context,
        IMongoDatabase mongoDatabase)
    {
        // Seed V38 so the two authoritative commits in PRICE-CON-07 become deterministic V39/V40.
        return SeedPriceCon03ScenarioAsync(
            context,
            mongoDatabase,
            PriceCon07InitialProjectionMutationVersion);
    }

    private static async Task<PriceCon08ScenarioSeed> SeedPriceCon08ScenarioAsync(
        ApplicationDbContext context,
        IMongoDatabase mongoDatabase)
    {
        await EnsureOutboxSchemaAsync(context);

        var promotionNowUtc = DateTime.UtcNow;

        context.Tenants.Add(new Tenant
        {
            Id = TenantId,
            Name = "PRICE-CON-08 Tenant",
            Slug = "price-con-08-store",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });

        context.Categories.Add(new Category
        {
            Id = CategoryId,
            TenantId = TenantId,
            Name = "PRICE-CON-08 Category",
            CreatedAt = SeededUtc
        });

        context.Products.Add(new Product
        {
            Id = ProductId,
            TenantId = TenantId,
            Name = "PRICE-CON-08 Product",
            SKU = DefaultVariantSku,
            Barcode = DefaultVariantBarcode,
            Slug = PriceCon08ProductSlug,
            CategoryId = CategoryId,
            PurchasePrice = TargetVariantCostPrice,
            SalePrice = InitialSellPrice,
            OnlinePrice = null,
            PriceIncludesTax = false,
            TaxRate = 0m,
            Unit = "Piece",
            IsActive = true,
            TrackStock = false,
            HasVariants = false,
            IsOnlineVisible = true,
            PublishStatus = PublishStatus.Published,
            CreatedAt = SeededUtc
        });

        context.ProductVariants.Add(new ProductVariant
        {
            Id = DefaultVariantId,
            TenantId = TenantId,
            ProductId = ProductId,
            SKU = DefaultVariantSku,
            Barcode = DefaultVariantBarcode,
            Price = InitialSellPrice,
            OnlinePrice = null,
            CostPrice = TargetVariantCostPrice,
            IsDefault = true,
            IsActive = true,
            SortOrder = 0,
            CreatedAt = SeededUtc
        });

        context.Promotions.AddRange(
            new Promotion
            {
                Id = PriceCon08PromotionAId,
                TenantId = TenantId,
                Name = "PRICE-CON-08 Promotion A",
                DiscountType = DiscountType.Percentage,
                DiscountValue = PriceCon08PromotionADiscountValue,
                MaxDiscountAmount = PriceCon08PromotionAMaxDiscountAmount,
                Scope = PromotionScope.Product,
                AppliesToId = ProductId,
                StartDate = promotionNowUtc.AddMinutes(-5),
                EndDate = promotionNowUtc.AddDays(30),
                Priority = PriceCon08PromotionAPriority,
                IsActive = false,
                CreatedAt = SeededUtc,
                UpdatedAt = SeededUtc
            },
            new Promotion
            {
                Id = PriceCon08PromotionBId,
                TenantId = TenantId,
                Name = "PRICE-CON-08 Promotion B",
                DiscountType = DiscountType.FixedAmount,
                DiscountValue = PriceCon08PromotionBDiscountValue,
                Scope = PromotionScope.Product,
                AppliesToId = ProductId,
                StartDate = promotionNowUtc.AddMinutes(-5),
                EndDate = promotionNowUtc.AddDays(30),
                Priority = PriceCon08PromotionBPriority,
                IsActive = false,
                CreatedAt = SeededUtc,
                UpdatedAt = SeededUtc
            });

        await context.SaveChangesAsync();

        var product = await context.Products.SingleAsync(entry => entry.Id == ProductId);
        var variant = await context.ProductVariants.SingleAsync(entry => entry.Id == DefaultVariantId);
        var seeded = new PriceCon08ScenarioSeed(
            TenantId,
            ProductId,
            DefaultVariantId,
            PriceCon08PromotionAId,
            PriceCon08PromotionBId,
            product.ProjectionMutationVersion,
            variant.ProjectionMutationVersion);

        await SeedPriceCon08MongoReadModelsAsync(mongoDatabase, seeded);
        return seeded;
    }

    private static async Task SeedPriceCon08MongoReadModelsAsync(
        IMongoDatabase mongoDatabase,
        PriceCon08ScenarioSeed seeded)
    {
        var initialEventVersion = ProductEventVersionHelper.ToProductEventVersion(seeded.DefaultVariantProjectionMutationVersion);
        var productDocument = new ProductDocument
        {
            TenantId = seeded.TenantId,
            ProductId = seeded.ProductId,
            Name = "PRICE-CON-08 Product",
            SKU = DefaultVariantSku,
            Barcode = DefaultVariantBarcode,
            Slug = PriceCon08ProductSlug,
            Price = InitialSellPrice,
            OnlinePrice = null,
            PurchasePrice = TargetVariantCostPrice,
            CategoryId = CategoryId,
            CategoryName = "PRICE-CON-08 Category",
            Images = "https://images.example.test/price-con-08.png",
            IsOnlineVisible = true,
            IsActive = true,
            TrackStock = false,
            StockQuantity = 10,
            HasVariants = false,
            IsAffiliateEnabled = true,
            AffiliateEnabledAtUtc = SeededUtc,
            CreatedAtUtc = SeededUtc,
            SyncedAtUtc = SeededUtc,
            LastEventId = BaselineStorefrontEventId,
            LastEventOccurredAtUtc = SeededUtc,
            LastEventVersion = ProductEventVersionHelper.ToProductEventVersion(seeded.ProductProjectionMutationVersion),
            Variants =
            [
                new ProductVariantDocument
                {
                    VariantId = seeded.DefaultVariantId,
                    SKU = DefaultVariantSku,
                    Barcode = DefaultVariantBarcode,
                    Price = InitialSellPrice,
                    OnlinePrice = null,
                    CostPrice = TargetVariantCostPrice,
                    IsDefault = true,
                    IsActive = true,
                    TotalStock = 10m,
                    SortOrder = 0,
                    SyncedAtUtc = SeededUtc,
                    LastEventOccurredAtUtc = SeededUtc,
                    LastEventVersion = initialEventVersion
                }
            ]
        };

        var listingDocument = new CommerceHubProductDocument
        {
            Id = CommerceHubListingId,
            TenantId = seeded.TenantId,
            StoreName = "PRICE-CON-08 Store",
            ProductId = seeded.ProductId,
            VariantId = seeded.DefaultVariantId,
            ProductName = productDocument.Name,
            Price = InitialSellPrice,
            CategoryId = CategoryId,
            Currency = "EGP",
            StoreShippingFee = 0m,
            InStock = true,
            AvailableStock = 10m,
            IsPublished = true,
            CreatedAtUtc = SeededUtc,
            UpdatedAtUtc = SeededUtc,
            LastEventId = BaselineCommerceHubEventId,
            LastListingEventOccurredAtUtc = SeededUtc,
            LastListingEventVersion = initialEventVersion,
            LastPriceEventTimestamp = SeededUtc,
            LastPriceEventVersion = initialEventVersion,
            SearchSyncStatus = CommerceHubProductDocument.SearchSyncSynced,
            SearchRetryCount = 0
        };

        var publicAffiliateProductDocument = new PublicAffiliateProductDocument
        {
            ProductId = seeded.ProductId,
            ProductSlug = PriceCon08ProductSlug,
            ProductName = productDocument.Name,
            Description = string.Empty,
            ThumbnailUrl = "https://images.example.test/price-con-08.png",
            Price = InitialSellPrice,
            MinVariantPrice = null,
            MaxVariantPrice = null,
            Currency = "EGP",
            StoreName = "PRICE-CON-08 Tenant",
            StoreSlug = "price-con-08-store",
            CategoryName = "PRICE-CON-08 Category",
            HasVariants = false,
            TotalAvailableStock = 10,
            InStock = true,
            IsActive = true,
            IsAffiliateEnabled = true,
            AffiliateEnabledAtUtc = SeededUtc,
            TenantId = seeded.TenantId,
            StorefrontBaseUrl = "https://price-con-08-store.example.test"
        };

        await mongoDatabase.GetCollection<ProductDocument>("products").InsertOneAsync(productDocument);
        await mongoDatabase.GetCollection<CommerceHubProductDocument>("commercehub_products").InsertOneAsync(listingDocument);
        await mongoDatabase.GetCollection<PublicAffiliateProductDocument>("public_affiliate_products").InsertOneAsync(publicAffiliateProductDocument);
    }

    private static async Task<PriceCon09ScenarioSeed> SeedPriceCon09ScenarioAsync(
        ApplicationDbContext context,
        IMongoDatabase mongoDatabase)
    {
        await EnsureOutboxSchemaAsync(context);

        var promotionNowUtc = DateTime.UtcNow;
        var promotionStartDateUtc = promotionNowUtc.AddMinutes(-15);
        var promotionEndDateUtc = promotionNowUtc.AddMinutes(30);

        context.Tenants.Add(new Tenant
        {
            Id = TenantId,
            Name = "PRICE-CON-09 Tenant",
            Slug = PriceCon09StoreSlug,
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });

        context.Categories.Add(new Category
        {
            Id = CategoryId,
            TenantId = TenantId,
            Name = "PRICE-CON-09 Category",
            CreatedAt = SeededUtc
        });

        context.Warehouses.Add(new Warehouse
        {
            Id = PriceCon09WarehouseId,
            TenantId = TenantId,
            Name = "PRICE-CON-09 Warehouse",
            Code = "PC09-WH",
            IsActive = true,
            CreatedAt = SeededUtc
        });

        context.Products.Add(new Product
        {
            Id = ProductId,
            TenantId = TenantId,
            Name = "PRICE-CON-09 Product",
            SKU = DefaultVariantSku,
            Barcode = DefaultVariantBarcode,
            Slug = PriceCon09ProductSlug,
            CategoryId = CategoryId,
            PurchasePrice = TargetVariantCostPrice,
            SalePrice = InitialSellPrice,
            OnlinePrice = null,
            PriceIncludesTax = false,
            TaxRate = 0m,
            Unit = "Piece",
            IsActive = true,
            TrackStock = false,
            HasVariants = false,
            IsOnlineVisible = true,
            PublishStatus = PublishStatus.Published,
            CreatedAt = SeededUtc
        });

        context.ProductVariants.Add(new ProductVariant
        {
            Id = DefaultVariantId,
            TenantId = TenantId,
            ProductId = ProductId,
            SKU = DefaultVariantSku,
            Barcode = DefaultVariantBarcode,
            Price = InitialSellPrice,
            OnlinePrice = null,
            CostPrice = TargetVariantCostPrice,
            IsDefault = true,
            IsActive = true,
            SortOrder = 0,
            CreatedAt = SeededUtc
        });

        context.ProductVariantStocks.Add(new ProductVariantStock
        {
            Id = PriceCon09VariantStockId,
            TenantId = TenantId,
            WarehouseId = PriceCon09WarehouseId,
            VariantId = DefaultVariantId,
            Quantity = 10m,
            ReservedQuantity = 0m,
            DamagedQuantity = 0m,
            CreatedAt = SeededUtc,
            UpdatedAt = SeededUtc
        });

        context.Promotions.AddRange(
            new Promotion
            {
                Id = PriceCon09ProductPromotionId,
                TenantId = TenantId,
                Name = "PRICE-CON-09 Product Promotion",
                DiscountType = DiscountType.Percentage,
                DiscountValue = PriceCon09ProductPromotionDiscountValue,
                MaxDiscountAmount = PriceCon09ProductPromotionMaxDiscountAmount,
                Scope = PromotionScope.Product,
                AppliesToId = ProductId,
                StartDate = promotionStartDateUtc,
                EndDate = promotionEndDateUtc,
                Priority = PriceCon09ProductPromotionPriority,
                IsActive = false,
                CreatedAt = SeededUtc,
                UpdatedAt = SeededUtc
            },
            new Promotion
            {
                Id = PriceCon09CategoryPromotionId,
                TenantId = TenantId,
                Name = "PRICE-CON-09 Category Promotion",
                DiscountType = DiscountType.Percentage,
                DiscountValue = PriceCon09CategoryPromotionDiscountValue,
                Scope = PromotionScope.Category,
                AppliesToId = CategoryId,
                StartDate = promotionStartDateUtc,
                EndDate = promotionEndDateUtc,
                Priority = PriceCon09CategoryPromotionPriority,
                IsActive = false,
                BannerStyle = PriceCon09CategoryBannerStyle,
                CreatedAt = SeededUtc,
                UpdatedAt = SeededUtc
            });

        await context.SaveChangesAsync();

        var product = await context.Products.SingleAsync(entry => entry.Id == ProductId);
        var variant = await context.ProductVariants.SingleAsync(entry => entry.Id == DefaultVariantId);
        var seeded = new PriceCon09ScenarioSeed(
            TenantId,
            ProductId,
            DefaultVariantId,
            PriceCon09ProductPromotionId,
            PriceCon09CategoryPromotionId,
            promotionStartDateUtc,
            promotionEndDateUtc,
            product.ProjectionMutationVersion,
            variant.ProjectionMutationVersion);

        await SeedPriceCon09MongoReadModelsAsync(mongoDatabase, seeded);
        return seeded;
    }

    private static async Task SeedPriceCon09MongoReadModelsAsync(
        IMongoDatabase mongoDatabase,
        PriceCon09ScenarioSeed seeded)
    {
        var initialEventVersion = ProductEventVersionHelper.ToProductEventVersion(seeded.DefaultVariantProjectionMutationVersion);
        var productDocument = new ProductDocument
        {
            TenantId = seeded.TenantId,
            ProductId = seeded.ProductId,
            Name = "PRICE-CON-09 Product",
            SKU = DefaultVariantSku,
            Barcode = DefaultVariantBarcode,
            Slug = PriceCon09ProductSlug,
            Price = InitialSellPrice,
            OnlinePrice = null,
            PurchasePrice = TargetVariantCostPrice,
            CategoryId = CategoryId,
            CategoryName = "PRICE-CON-09 Category",
            Images = "https://images.example.test/price-con-09.png",
            IsOnlineVisible = true,
            IsActive = true,
            TrackStock = false,
            StockQuantity = 10,
            HasVariants = false,
            IsAffiliateEnabled = true,
            AffiliateEnabledAtUtc = SeededUtc,
            CreatedAtUtc = SeededUtc,
            SyncedAtUtc = SeededUtc,
            LastEventId = BaselineStorefrontEventId,
            LastEventOccurredAtUtc = SeededUtc,
            LastEventVersion = ProductEventVersionHelper.ToProductEventVersion(seeded.ProductProjectionMutationVersion),
            Variants =
            [
                new ProductVariantDocument
                {
                    VariantId = seeded.DefaultVariantId,
                    SKU = DefaultVariantSku,
                    Barcode = DefaultVariantBarcode,
                    Price = InitialSellPrice,
                    OnlinePrice = null,
                    CostPrice = TargetVariantCostPrice,
                    IsDefault = true,
                    IsActive = true,
                    TotalStock = 10m,
                    SortOrder = 0,
                    SyncedAtUtc = SeededUtc,
                    LastEventOccurredAtUtc = SeededUtc,
                    LastEventVersion = initialEventVersion
                }
            ]
        };

        var categoryDocument = new CategoryDocument
        {
            TenantId = seeded.TenantId,
            CategoryId = CategoryId,
            Name = "PRICE-CON-09 Category",
            Slug = PriceCon09CategorySlug,
            Description = "PRICE-CON-09 category",
            SortOrder = 0,
            IsActive = true,
            IsOnlineVisible = true,
            CreatedAtUtc = SeededUtc,
            SyncedAtUtc = SeededUtc
        };

        var storefrontConfigDocument = new StorefrontConfigDocument
        {
            Id = $"storefront-config:{seeded.TenantId:N}",
            TenantId = seeded.TenantId,
            TenantSlug = PriceCon09StoreSlug,
            UpdatedAt = SeededUtc,
            Currency = "EGP",
            StoreName = "PRICE-CON-09 Tenant"
        };

        var listingDocument = new CommerceHubProductDocument
        {
            Id = CommerceHubListingId,
            TenantId = seeded.TenantId,
            StoreName = "PRICE-CON-09 Store",
            ProductId = seeded.ProductId,
            VariantId = seeded.DefaultVariantId,
            ProductName = productDocument.Name,
            Price = InitialSellPrice,
            CategoryId = CategoryId,
            Currency = "EGP",
            StoreShippingFee = 0m,
            InStock = true,
            AvailableStock = 10m,
            IsPublished = true,
            CreatedAtUtc = SeededUtc,
            UpdatedAtUtc = SeededUtc,
            LastEventId = BaselineCommerceHubEventId,
            LastListingEventOccurredAtUtc = SeededUtc,
            LastListingEventVersion = initialEventVersion,
            LastPriceEventTimestamp = SeededUtc,
            LastPriceEventVersion = initialEventVersion,
            SearchSyncStatus = CommerceHubProductDocument.SearchSyncSynced,
            SearchRetryCount = 0
        };

        var publicAffiliateProductDocument = new PublicAffiliateProductDocument
        {
            ProductId = seeded.ProductId,
            ProductSlug = PriceCon09ProductSlug,
            ProductName = productDocument.Name,
            Description = string.Empty,
            ThumbnailUrl = "https://images.example.test/price-con-09.png",
            Price = InitialSellPrice,
            MinVariantPrice = null,
            MaxVariantPrice = null,
            Currency = "EGP",
            StoreName = "PRICE-CON-09 Tenant",
            StoreSlug = PriceCon09StoreSlug,
            CategoryName = "PRICE-CON-09 Category",
            HasVariants = false,
            TotalAvailableStock = 10,
            InStock = true,
            IsActive = true,
            IsAffiliateEnabled = true,
            AffiliateEnabledAtUtc = SeededUtc,
            TenantId = seeded.TenantId,
            StorefrontBaseUrl = "https://price-con-09-store.example.test"
        };

        await mongoDatabase.GetCollection<ProductDocument>("products").InsertOneAsync(productDocument);
        await mongoDatabase.GetCollection<CategoryDocument>("categories").InsertOneAsync(categoryDocument);
        await mongoDatabase.GetCollection<StorefrontConfigDocument>("storefront_configs").InsertOneAsync(storefrontConfigDocument);
        await mongoDatabase.GetCollection<CommerceHubProductDocument>("commercehub_products").InsertOneAsync(listingDocument);
        await mongoDatabase.GetCollection<PublicAffiliateProductDocument>("public_affiliate_products").InsertOneAsync(publicAffiliateProductDocument);
    }

    private static async Task SeedPriceCon03MongoReadModelsAsync(
        IMongoDatabase mongoDatabase,
        PriceCon03ScenarioSeed seeded)
    {
        var initialEventVersion = ProductEventVersionHelper.ToProductEventVersion(seeded.DefaultVariantProjectionMutationVersion);
        var defaultUnitId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var productDocument = new ProductDocument
        {
            TenantId = seeded.TenantId,
            ProductId = seeded.ProductId,
            Name = "PRICE-CON-03 Product",
            SKU = DefaultVariantSku,
            Barcode = DefaultVariantBarcode,
            Slug = "price-con-default",
            Price = InitialSellPrice,
            OnlinePrice = null,
            PurchasePrice = TargetVariantCostPrice,
            CategoryId = CategoryId,
            IsOnlineVisible = true,
            IsActive = true,
            TrackStock = false,
            HasVariants = false,
            DefaultUnit = new ProductUnitDocument
            {
                UnitId = defaultUnitId,
                Name = "Piece",
                Symbol = "pc",
                SalePrice = InitialSellPrice,
                ConversionFactor = 1m,
                IsBaseUnit = true
            },
            Units =
            [
                new ProductUnitDocument
                {
                    UnitId = defaultUnitId,
                    Name = "Piece",
                    Symbol = "pc",
                    SalePrice = InitialSellPrice,
                    ConversionFactor = 1m,
                    IsBaseUnit = true
                }
            ],
            CreatedAtUtc = SeededUtc,
            SyncedAtUtc = SeededUtc,
            LastEventId = BaselineStorefrontEventId,
            LastEventOccurredAtUtc = SeededUtc,
            LastEventVersion = ProductEventVersionHelper.ToProductEventVersion(seeded.ProductProjectionMutationVersion),
            Variants =
            [
                new ProductVariantDocument
                {
                    VariantId = seeded.DefaultVariantId,
                    SKU = DefaultVariantSku,
                    Barcode = DefaultVariantBarcode,
                    Price = InitialSellPrice,
                    OnlinePrice = null,
                    CostPrice = TargetVariantCostPrice,
                    IsDefault = true,
                    IsActive = true,
                    TotalStock = 0m,
                    SortOrder = 0,
                    SyncedAtUtc = SeededUtc,
                    LastEventOccurredAtUtc = SeededUtc,
                    LastEventVersion = initialEventVersion
                }
            ]
        };

        var listingDocument = new CommerceHubProductDocument
        {
            Id = CommerceHubListingId,
            TenantId = seeded.TenantId,
            StoreName = "PRICE-CON Store",
            ProductId = seeded.ProductId,
            VariantId = seeded.DefaultVariantId,
            ProductName = productDocument.Name,
            Price = InitialSellPrice,
            CategoryId = CategoryId,
            Currency = "EGP",
            StoreShippingFee = 0m,
            InStock = true,
            AvailableStock = 10m,
            IsPublished = true,
            CreatedAtUtc = SeededUtc,
            UpdatedAtUtc = SeededUtc,
            LastEventId = BaselineCommerceHubEventId,
            LastListingEventOccurredAtUtc = SeededUtc,
            LastListingEventVersion = initialEventVersion,
            LastPriceEventTimestamp = SeededUtc,
            LastPriceEventVersion = initialEventVersion,
            SearchSyncStatus = CommerceHubProductDocument.SearchSyncSynced,
            SearchRetryCount = 0
        };

        var affiliateDocument = new AffiliateProductIntelligenceDocument
        {
            ProductId = seeded.ProductId,
            TenantId = seeded.TenantId,
            StoreSlug = "price-con-store",
            StoreName = "PRICE-CON Store",
            StoreLogo = "",
            StorefrontUrl = PriceCon03StorefrontUrl,
            ProductName = productDocument.Name,
            ProductSlug = "price-con-default",
            Description = "",
            Images = [],
            BasePrice = InitialSellPrice,
            Currency = "EGP",
            CommissionRate = 0.10m,
            EstimatedCommission = 10m,
            TotalAvailableStock = 0,
            IsOutOfStock = false,
            LowStock = false,
            Variants =
            [
                new VariantStockSubDocument
                {
                    VariantId = seeded.DefaultVariantId,
                    Name = DefaultVariantSku,
                    Price = InitialSellPrice,
                    Stock = 0
                }
            ],
            LastProductEventVersion = ProductEventVersionHelper.ToProductEventVersion(seeded.ProductProjectionMutationVersion),
            LastProductEventOccurredAtUtc = SeededUtc,
            LastUpdatedUtc = SeededUtc,
            CreatedAtUtc = SeededUtc,
            ProcessedEventIds = []
        };

        await mongoDatabase.GetCollection<ProductDocument>("products").InsertOneAsync(productDocument);
        await mongoDatabase.GetCollection<CommerceHubProductDocument>("commercehub_products").InsertOneAsync(listingDocument);
        await mongoDatabase.GetCollection<AffiliateProductIntelligenceDocument>("affiliate_product_intelligence").InsertOneAsync(affiliateDocument);
    }

    private static async Task EnsureOutboxSchemaAsync(ApplicationDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = current_schema()
                      AND table_name = 'OutboxMessages')
                THEN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = current_schema()
                          AND table_name = 'OutboxMessages'
                          AND column_name = 'EventVersion') THEN
                        ALTER TABLE "OutboxMessages"
                        ADD COLUMN "EventVersion" integer NOT NULL DEFAULT 1;
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = current_schema()
                          AND table_name = 'OutboxMessages'
                          AND column_name = 'RequiresOperatorReconciliation') THEN
                        ALTER TABLE "OutboxMessages"
                        ADD COLUMN "RequiresOperatorReconciliation" boolean NOT NULL DEFAULT false;
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = current_schema()
                          AND table_name = 'OutboxMessages'
                          AND column_name = 'OperatorReconciliationRequiredAtUtc') THEN
                        ALTER TABLE "OutboxMessages"
                        ADD COLUMN "OperatorReconciliationRequiredAtUtc" timestamp with time zone NULL;
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = current_schema()
                          AND table_name = 'OutboxMessages'
                          AND column_name = 'OperatorReconciliationReason') THEN
                        ALTER TABLE "OutboxMessages"
                        ADD COLUMN "OperatorReconciliationReason" text NULL;
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = current_schema()
                          AND table_name = 'OutboxMessages'
                          AND column_name = 'LastErrorAtUtc') THEN
                        ALTER TABLE "OutboxMessages"
                        ADD COLUMN "LastErrorAtUtc" timestamp with time zone NULL;
                    END IF;
                END IF;
            END
            $$;
            """);
    }

    private static async Task<PriceActorResult> ExecutePriceActorAsync(
        string connectionString,
        TestTenantContext tenantContext,
        PriceScenarioSeed seeded,
        PriceActor actor,
        ConcurrentQueue<string> progressEntries,
        params IInterceptor[] interceptors)
    {
        await using var context = CreateDbContext(connectionString, tenantContext, interceptors);
        await ConfigureSessionTimeoutsAsync(context);

        var controllerLogger = new RecordingLogger<ProductVariantsController>();
        var controller = CreateController(context, tenantContext, actor, controllerLogger);
        var request = BuildUpdateVariantRequest(actor);

        progressEntries.Enqueue($"{actor.Name}:request-ready");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            progressEntries.Enqueue($"{actor.Name}:invoking-controller");
            var result = await controller.UpdateVariant(seeded.ProductId, seeded.TargetVariantId, request);
            if (controllerLogger.LastException is Exception loggedException)
            {
                progressEntries.Enqueue($"{actor.Name}:logged-exception:{DescribeException(loggedException)}");
            }

            stopwatch.Stop();
            return ParseActorResult(actor, result, stopwatch.Elapsed, progressEntries);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            progressEntries.Enqueue($"{actor.Name}:threw:{DescribeException(ex)}");
            return new PriceActorResult(
                Actor: actor,
                Succeeded: false,
                StatusCode: null,
                Response: null,
                PayloadJson: null,
                Duration: stopwatch.Elapsed,
                Exception: ex);
        }
    }

    private static async Task<PromotionActorResult> ExecutePromotionActorAsync(
        string connectionString,
        TestTenantContext tenantContext,
        PromotionActor actor,
        ConcurrentQueue<string> progressEntries,
        params IInterceptor[] interceptors)
    {
        await using var context = CreateDbContext(connectionString, tenantContext, interceptors);
        await ConfigureSessionTimeoutsAsync(context);

        var controllerLogger = new RecordingLogger<PromotionsController>();
        var controller = CreatePromotionController(context, tenantContext, actor, controllerLogger);

        progressEntries.Enqueue($"{actor.Name}:request-ready");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // The actor must submit the revision it reviewed. Without this,
            // lifecycle admission returns 428 before the commit-pause window,
            // so the intended distributed scenario only times out in setup.
            var reviewedVersion = await context.Promotions.AsNoTracking()
                .Where(promotion => promotion.TenantId == tenantContext.TenantId && promotion.Id == actor.PromotionId)
                .Select(promotion => promotion.RowVersion)
                .SingleAsync();
            controller.Request.Headers.IfMatch = $"\"{reviewedVersion}\"";
            progressEntries.Enqueue($"{actor.Name}:invoking-controller");
            var result = await controller.TogglePromotionStatus(actor.PromotionId);
            if (controllerLogger.LastException is Exception loggedException)
            {
                progressEntries.Enqueue($"{actor.Name}:logged-exception:{DescribeException(loggedException)}");
            }

            stopwatch.Stop();
            return ParsePromotionActorResult(actor, result, stopwatch.Elapsed, progressEntries);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            progressEntries.Enqueue($"{actor.Name}:threw:{DescribeException(ex)}");
            return new PromotionActorResult(
                Actor: actor,
                Succeeded: false,
                StatusCode: null,
                Response: null,
                PayloadJson: null,
                Duration: stopwatch.Elapsed,
                Exception: ex);
        }
    }

    private static async Task<PriceActorResult> ExecutePriceCon03CanonicalActorAsync(
        string connectionString,
        TestTenantContext tenantContext,
        PriceCon03ScenarioSeed seeded,
        PriceActor actor,
        ConcurrentQueue<string> progressEntries,
        params IInterceptor[] interceptors)
    {
        await using var context = CreateDbContext(connectionString, tenantContext, interceptors);
        await ConfigureSessionTimeoutsAsync(context);

        var controllerLogger = new RecordingLogger<ProductVariantsController>();
        var eventPublisher = new AuthoritativeOutboxTestPublisher(
            context,
            tenantContext.TenantId ?? throw new InvalidOperationException("PRICE-CON-03 requires a tenant id."),
            actor.TraceIdentifier);
        var productVariantService = new ProductVariantService(
            context,
            new NoOpEventPublisher(),
            NullLogger<ProductVariantService>.Instance);
        var marketplaceEventOutbox = new MarketplaceEventOutboxService(
            context,
            NullLogger<MarketplaceEventOutboxService>.Instance);
        var controller = CreateController(
            context,
            tenantContext,
            actor,
            controllerLogger,
            productVariantService,
            eventPublisher,
            marketplaceEventOutbox);
        var request = BuildUpdateVariantRequest(actor, DefaultVariantSku, DefaultVariantBarcode);

        progressEntries.Enqueue($"{actor.Name}:request-ready");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            progressEntries.Enqueue($"{actor.Name}:invoking-controller");
            var result = await controller.UpdateVariant(seeded.ProductId, seeded.DefaultVariantId, request);
            if (controllerLogger.LastException is Exception loggedException)
            {
                progressEntries.Enqueue($"{actor.Name}:logged-exception:{DescribeException(loggedException)}");
            }

            stopwatch.Stop();
            return ParseActorResult(actor, result, stopwatch.Elapsed, progressEntries);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            progressEntries.Enqueue($"{actor.Name}:threw:{DescribeException(ex)}");
            return new PriceActorResult(
                Actor: actor,
                Succeeded: false,
                StatusCode: null,
                Response: null,
                PayloadJson: null,
                Duration: stopwatch.Elapsed,
                Exception: ex);
        }
    }

    private static ApplicationDbContext CreateDbContext(
        string connectionString,
        TestTenantContext tenantContext,
        params IInterceptor[] interceptors)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .EnableDetailedErrors();

        if (interceptors.Length > 0)
        {
            optionsBuilder.AddInterceptors(interceptors);
        }

        return new ApplicationDbContext(optionsBuilder.Options, tenantContext);
    }

    private static ProductVariantsController CreateController(
        ApplicationDbContext context,
        TestTenantContext tenantContext,
        PriceActor actor,
        ILogger<ProductVariantsController> logger,
        IProductVariantService? productVariantServiceOverride = null,
        IEventPublisher? eventPublisherOverride = null,
        IMarketplaceEventOutboxService? marketplaceEventOutboxOverride = null)
    {
        var productVariantService = productVariantServiceOverride;
        if (productVariantService is null)
        {
            var serviceMock = new Mock<IProductVariantService>(MockBehavior.Strict);
            serviceMock
                .Setup(service => service.SyncSimpleProductFromDefaultVariantAsync(
                    It.IsAny<ProductVariant>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((SimpleProductMirrorSyncResult?)null);
            productVariantService = serviceMock.Object;
        }

        var controller = new ProductVariantsController(
            context,
            tenantContext,
            productVariantService,
            eventPublisherOverride ?? new AuthoritativeOutboxTestPublisher(
                context,
                tenantContext.TenantId ?? throw new InvalidOperationException("PRICE-CON-01 requires a tenant id."),
                actor.TraceIdentifier),
            marketplaceEventOutboxOverride ?? Mock.Of<IMarketplaceEventOutboxService>(),
            logger)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext(actor)
            }
        };

        controller.Request.Headers["Idempotency-Key"] = actor.RequestId;
        controller.Request.Headers["X-Request-Id"] = actor.RequestId;
        controller.Request.Headers["User-Agent"] = "PRICE-CON";

        return controller;
    }

    private static PromotionsController CreatePromotionController(
        ApplicationDbContext context,
        TestTenantContext tenantContext,
        PromotionActor actor,
        ILogger<PromotionsController> logger,
        IEventPublisher? eventPublisherOverride = null,
        IDiscountReadModelRepairService? readModelRepairServiceOverride = null)
    {
        var controller = new PromotionsController(
            context,
            tenantContext,
            eventPublisherOverride ?? new AuthoritativeOutboxTestPublisher(
                context,
                tenantContext.TenantId ?? throw new InvalidOperationException("PRICE-CON-08 requires a tenant id."),
                actor.TraceIdentifier),
            logger,
            serviceScopeFactory: null,
            readModelRepairService: readModelRepairServiceOverride)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext(actor.TraceIdentifier, actor.UserId, actor.Name)
            }
        };

        controller.Request.Headers["Idempotency-Key"] = actor.RequestId;
        controller.Request.Headers["X-Request-Id"] = actor.RequestId;
        controller.Request.Headers["User-Agent"] = "PRICE-CON";

        return controller;
    }

    private static DefaultHttpContext BuildHttpContext(PriceActor actor)
    {
        return BuildHttpContext(actor.TraceIdentifier, actor.UserId, actor.Name);
    }

    private static DefaultHttpContext BuildHttpContext(
        string traceIdentifier,
        Guid userId,
        string name)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, name)
        ], "TestAuth");

        return new DefaultHttpContext
        {
            TraceIdentifier = traceIdentifier,
            User = new ClaimsPrincipal(identity)
        };
    }

    private static UpdateVariantRequest BuildUpdateVariantRequest(PriceActor actor)
    {
        return BuildUpdateVariantRequest(actor, TargetVariantSku, TargetVariantBarcode);
    }

    private static UpdateVariantRequest BuildUpdateVariantRequest(
        PriceActor actor,
        string sku,
        string barcode)
    {
        return new UpdateVariantRequest
        {
            SKU = sku,
            Barcode = barcode,
            Price = actor.RequestedSellPrice,
            OnlinePrice = null,
            CostPrice = TargetVariantCostPrice,
            IsActive = true,
            MediaId = null,
            ImageUrl = null,
            SortOrder = 1,
            Attributes =
            [
                new CreateVariantAttributeRequest
                {
                    Name = "Color",
                    Value = "Blue",
                    SortOrder = 0
                },
                new CreateVariantAttributeRequest
                {
                    Name = "Size",
                    Value = "M",
                    SortOrder = 1
                }
            ],
            Stocks = null
        };
    }

    private static PriceActorResult ParseActorResult(
        PriceActor actor,
        ActionResult<ProductVariantDto> actionResult,
        TimeSpan duration,
        ConcurrentQueue<string> progressEntries)
    {
        if (actionResult.Result is OkObjectResult { Value: ProductVariantDto response })
        {
            progressEntries.Enqueue($"{actor.Name}:ok:{response.Id}:{response.Price}");
            return new PriceActorResult(
                actor,
                Succeeded: true,
                StatusCode: StatusCodes.Status200OK,
                Response: response,
                PayloadJson: JsonSerializer.Serialize(response),
                Duration: duration,
                Exception: null);
        }

        if (actionResult.Result is ObjectResult objectResult)
        {
            var payloadJson = objectResult.Value == null
                ? null
                : JsonSerializer.Serialize(objectResult.Value);

            if ((objectResult.StatusCode ?? StatusCodes.Status200OK) == StatusCodes.Status200OK &&
                TryParseVariantDto(payloadJson, out var storedResponse))
            {
                progressEntries.Enqueue($"{actor.Name}:ok:{storedResponse.Id}:{storedResponse.Price}");
                return new PriceActorResult(
                    actor,
                    Succeeded: true,
                    StatusCode: StatusCodes.Status200OK,
                    Response: storedResponse,
                    PayloadJson: payloadJson,
                    Duration: duration,
                    Exception: null);
            }

            progressEntries.Enqueue(
                $"{actor.Name}:status:{objectResult.StatusCode ?? StatusCodes.Status500InternalServerError}:{payloadJson}");
            return new PriceActorResult(
                actor,
                Succeeded: false,
                StatusCode: objectResult.StatusCode,
                Response: null,
                PayloadJson: payloadJson,
                Duration: duration,
                Exception: null);
        }

        if (actionResult.Result is StatusCodeResult statusCodeResult)
        {
            progressEntries.Enqueue($"{actor.Name}:status:{statusCodeResult.StatusCode}");
            return new PriceActorResult(
                actor,
                Succeeded: false,
                StatusCode: statusCodeResult.StatusCode,
                Response: null,
                PayloadJson: null,
                Duration: duration,
                Exception: null);
        }

        if (actionResult.Value is ProductVariantDto directResponse)
        {
            progressEntries.Enqueue($"{actor.Name}:ok:{directResponse.Id}:{directResponse.Price}");
            return new PriceActorResult(
                actor,
                Succeeded: true,
                StatusCode: StatusCodes.Status200OK,
                Response: directResponse,
                PayloadJson: JsonSerializer.Serialize(directResponse),
                Duration: duration,
                Exception: null);
        }

        throw new XunitException(
            $"Actor {actor.Name} returned an unexpected result shape: {actionResult.Result?.GetType().Name ?? "null"}");
    }

    private static PromotionActorResult ParsePromotionActorResult(
        PromotionActor actor,
        ActionResult<PromotionDto> actionResult,
        TimeSpan duration,
        ConcurrentQueue<string> progressEntries)
    {
        if (actionResult.Result is OkObjectResult { Value: PromotionDto response })
        {
            progressEntries.Enqueue($"{actor.Name}:ok:{response.Id}:{response.IsActive}");
            return new PromotionActorResult(
                actor,
                Succeeded: true,
                StatusCode: StatusCodes.Status200OK,
                Response: response,
                PayloadJson: JsonSerializer.Serialize(response),
                Duration: duration,
                Exception: null);
        }

        if (actionResult.Result is ObjectResult objectResult)
        {
            var payloadJson = objectResult.Value == null
                ? null
                : JsonSerializer.Serialize(objectResult.Value);

            if ((objectResult.StatusCode ?? StatusCodes.Status200OK) == StatusCodes.Status200OK &&
                actionResult.Value is PromotionDto storedResponse)
            {
                progressEntries.Enqueue($"{actor.Name}:ok:{storedResponse.Id}:{storedResponse.IsActive}");
                return new PromotionActorResult(
                    actor,
                    Succeeded: true,
                    StatusCode: StatusCodes.Status200OK,
                    Response: storedResponse,
                    PayloadJson: payloadJson,
                    Duration: duration,
                    Exception: null);
            }

            progressEntries.Enqueue(
                $"{actor.Name}:status:{objectResult.StatusCode ?? StatusCodes.Status500InternalServerError}:{payloadJson}");
            return new PromotionActorResult(
                actor,
                Succeeded: false,
                StatusCode: objectResult.StatusCode,
                Response: null,
                PayloadJson: payloadJson,
                Duration: duration,
                Exception: null);
        }

        if (actionResult.Result is StatusCodeResult statusCodeResult)
        {
            progressEntries.Enqueue($"{actor.Name}:status:{statusCodeResult.StatusCode}");
            return new PromotionActorResult(
                actor,
                Succeeded: false,
                StatusCode: statusCodeResult.StatusCode,
                Response: null,
                PayloadJson: null,
                Duration: duration,
                Exception: null);
        }

        if (actionResult.Value is PromotionDto directResponse)
        {
            progressEntries.Enqueue($"{actor.Name}:ok:{directResponse.Id}:{directResponse.IsActive}");
            return new PromotionActorResult(
                actor,
                Succeeded: true,
                StatusCode: StatusCodes.Status200OK,
                Response: directResponse,
                PayloadJson: JsonSerializer.Serialize(directResponse),
                Duration: duration,
                Exception: null);
        }

        throw new XunitException(
            $"Actor {actor.Name} returned an unexpected promotion result shape: {actionResult.Result?.GetType().Name ?? "null"}");
    }

    private static bool TryParseVariantDto(string? payloadJson, out ProductVariantDto response)
    {
        response = default!;

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ProductVariantDto>(payloadJson, CaseInsensitiveJsonOptions);
            if (parsed?.Id is { } id && id != Guid.Empty)
            {
                response = parsed;
                return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private static async Task AssertNoUncommittedVisibilityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        PriceScenarioSeed seeded,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var persistedVariant = await verifyContext.ProductVariants
            .AsNoTracking()
            .SingleAsync(variant => variant.Id == seeded.TargetVariantId);
        var persistedOutboxMessages = await verifyContext.OutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.AggregateId == seeded.TargetVariantId &&
                (message.EventType == typeof(ProductVariantUpdatedEvent).FullName ||
                 message.EventType == typeof(VariantPriceUpdatedEvent).FullName))
            .ToListAsync();
        var processedEvents = await verifyContext.Set<ProcessedEvent>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .ToListAsync();
        var projectionOutboxMessages = await verifyContext.MarketplaceProjectionOutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .ToListAsync();
        var stockSyncMessages = await verifyContext.StockSyncOutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message => message.VariantId == seeded.TargetVariantId)
            .ToListAsync();

        var productDocument = await mongoDatabase
            .GetCollection<ProductDocument>("products")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();
        var listingDocument = await mongoDatabase
            .GetCollection<CommerceHubProductDocument>("commercehub_products")
            .Find(document => document.TenantId == seeded.TenantId && document.Id == CommerceHubListingId)
            .FirstOrDefaultAsync();

        Assert.NotNull(productDocument);
        Assert.NotNull(listingDocument);
        Assert.Equal(InitialSellPrice, persistedVariant.Price);
        Assert.Equal(seeded.TargetVariantProjectionMutationVersion, persistedVariant.ProjectionMutationVersion);
        Assert.Empty(persistedOutboxMessages);
        Assert.Empty(processedEvents);
        Assert.Empty(projectionOutboxMessages);
        Assert.Empty(stockSyncMessages);

        var storefrontVariant = Assert.Single(
            productDocument!.Variants.Where(variant => variant.VariantId == seeded.TargetVariantId));
        Assert.Equal(InitialSellPrice, storefrontVariant.Price);
        Assert.Equal(
            ProductEventVersionHelper.ToProductEventVersion(seeded.TargetVariantProjectionMutationVersion),
            storefrontVariant.LastEventVersion);

        Assert.Equal(InitialSellPrice, listingDocument!.Price);
        Assert.Equal(
            ProductEventVersionHelper.ToProductEventVersion(seeded.TargetVariantProjectionMutationVersion),
            listingDocument.LastPriceEventVersion);
        Assert.Equal(SeededUtc, listingDocument.LastPriceEventTimestamp);

        progressEntries.Enqueue("verification:active-transaction-pricing-remained-invisible");
    }

    private static async Task AssertPriceCon08NoPromotionVisibilityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        PriceCon08ScenarioSeed seeded,
        int expectedActivePromotionCount,
        int expectedCommittedOutboxCount,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var product = await verifyContext.Products
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.ProductId);
        var variant = await verifyContext.ProductVariants
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.DefaultVariantId);
        var promotions = await verifyContext.Promotions
            .AsNoTracking()
            .Where(entry => entry.Id == seeded.PromotionAId || entry.Id == seeded.PromotionBId)
            .OrderBy(entry => entry.Id)
            .ToListAsync();
        var outboxMessages = await verifyContext.OutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                (message.AggregateId == seeded.PromotionAId || message.AggregateId == seeded.PromotionBId) &&
                message.EventType == typeof(PromotionUpdatedEvent).FullName)
            .OrderBy(message => message.CreatedAtUtc)
            .ThenBy(message => message.Id)
            .ToListAsync();

        var productDocument = await mongoDatabase
            .GetCollection<ProductDocument>("products")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();
        var listingDocument = await mongoDatabase
            .GetCollection<CommerceHubProductDocument>("commercehub_products")
            .Find(document => document.TenantId == seeded.TenantId && document.Id == CommerceHubListingId)
            .FirstOrDefaultAsync();
        var publicAffiliateProduct = await mongoDatabase
            .GetCollection<PublicAffiliateProductDocument>("public_affiliate_products")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();

        Assert.Equal(InitialSellPrice, product.SalePrice);
        Assert.Equal(InitialSellPrice, variant.Price);
        Assert.Equal(seeded.ProductProjectionMutationVersion, product.ProjectionMutationVersion);
        Assert.Equal(seeded.DefaultVariantProjectionMutationVersion, variant.ProjectionMutationVersion);
        Assert.Equal(expectedActivePromotionCount, promotions.Count(entry => entry.IsActive && !entry.IsDeleted));
        Assert.Equal(expectedCommittedOutboxCount, outboxMessages.Count);
        Assert.All(outboxMessages, message => Assert.Null(message.ProcessedAtUtc));

        Assert.NotNull(productDocument);
        Assert.NotNull(listingDocument);
        Assert.NotNull(publicAffiliateProduct);

        Assert.False(productDocument!.HasActivePromotion);
        Assert.Null(productDocument.ActivePromotionId);
        Assert.Null(productDocument.PromotionDiscountType);
        Assert.Null(productDocument.PromotionDiscountValue);
        Assert.Null(productDocument.DiscountedPrice);

        var storefrontVariant = Assert.Single(productDocument.Variants.Where(entry => entry.VariantId == seeded.DefaultVariantId));
        Assert.Equal(InitialSellPrice, productDocument.Price);
        Assert.Equal(InitialSellPrice, storefrontVariant.Price);
        Assert.Equal(InitialSellPrice, listingDocument!.Price);
        Assert.Equal(InitialSellPrice, publicAffiliateProduct!.Price);
        Assert.Null(publicAffiliateProduct.MinVariantPrice);
        Assert.Null(publicAffiliateProduct.MaxVariantPrice);

        progressEntries.Enqueue(
            $"verification:price-con-08-no-promotion-visibility:active-promotions={expectedActivePromotionCount}:outbox={expectedCommittedOutboxCount}");
    }

    private static async Task AssertPriceCon08DurableStateAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        PriceCon08ScenarioSeed seeded,
        PromotionUpdatedEvent promotionAEvent,
        PromotionUpdatedEvent expectedPromotionBEvent,
        ConcurrentQueue<string> progressEntries)
    {
        Exception? lastFailure = null;
        for (var attempt = 1; attempt <= 20; attempt++)
        {
            try
            {
                await AssertPriceCon08DurableStateCoreAsync(
                    scope,
                    tenantContext,
                    mongoDatabase,
                    seeded,
                    promotionAEvent,
                    expectedPromotionBEvent,
                    progressEntries);
                return;
            }
            catch (Exception ex) when (attempt < 20)
            {
                lastFailure = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }
        }

        ExceptionDispatchInfo.Capture(lastFailure!).Throw();
    }

    private static async Task AssertPriceCon08DurableStateCoreAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        PriceCon08ScenarioSeed seeded,
        PromotionUpdatedEvent promotionAEvent,
        PromotionUpdatedEvent expectedPromotionBEvent,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var product = await verifyContext.Products
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.ProductId);
        var variant = await verifyContext.ProductVariants
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.DefaultVariantId);
        var promotions = await verifyContext.Promotions
            .AsNoTracking()
            .Where(entry => entry.Id == seeded.PromotionAId || entry.Id == seeded.PromotionBId)
            .OrderBy(entry => entry.Priority)
            .ThenBy(entry => entry.Id)
            .ToListAsync();
        var winningPromotion = PromotionVisibilityProjectionSupport.SelectWinningProductPromotion(promotions, DateTime.UtcNow);
        var outboxMessages = await verifyContext.OutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                (message.AggregateId == seeded.PromotionAId || message.AggregateId == seeded.PromotionBId) &&
                message.EventType == typeof(PromotionUpdatedEvent).FullName)
            .OrderBy(message => message.CreatedAtUtc)
            .ThenBy(message => message.Id)
            .ToListAsync();

        var productDocument = await mongoDatabase
            .GetCollection<ProductDocument>("products")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();
        var listingDocument = await mongoDatabase
            .GetCollection<CommerceHubProductDocument>("commercehub_products")
            .Find(document => document.TenantId == seeded.TenantId && document.Id == CommerceHubListingId)
            .FirstOrDefaultAsync();
        var publicAffiliateProduct = await mongoDatabase
            .GetCollection<PublicAffiliateProductDocument>("public_affiliate_products")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();
        var promotionDocuments = await mongoDatabase
            .GetCollection<PromotionDocument>("promotions")
            .Find(document => document.TenantId == seeded.TenantId)
            .ToListAsync();
        var rawProductDocument = await mongoDatabase
            .GetCollection<BsonDocument>("products")
            .Find(new BsonDocument
            {
                { "Slug", PriceCon08ProductSlug }
            })
            .FirstOrDefaultAsync();
        var rawProductDocumentsByBusinessKey = await mongoDatabase
            .GetCollection<BsonDocument>("products")
            .Find(new BsonDocument
            {
                { "TenantId", seeded.TenantId.ToString() },
                { "ProductId", seeded.ProductId.ToString() }
            })
            .ToListAsync();
        var rawProductDocumentsBySlug = await mongoDatabase
            .GetCollection<BsonDocument>("products")
            .Find(new BsonDocument
            {
                { "Slug", PriceCon08ProductSlug }
            })
            .ToListAsync();

        Assert.Equal(InitialSellPrice, product.SalePrice);
        Assert.Equal(InitialSellPrice, variant.Price);
        Assert.Equal(seeded.ProductProjectionMutationVersion, product.ProjectionMutationVersion);
        Assert.Equal(seeded.DefaultVariantProjectionMutationVersion, variant.ProjectionMutationVersion);

        Assert.Equal(2, promotions.Count);
        Assert.All(promotions, entry => Assert.True(entry.IsActive));
        Assert.NotNull(winningPromotion);
        Assert.Equal(seeded.PromotionAId, winningPromotion!.Id);

        Assert.Equal(2, outboxMessages.Count);
        Assert.All(outboxMessages, message => Assert.NotNull(message.ProcessedAtUtc));
        Assert.All(outboxMessages, message => Assert.Equal(2, message.EventVersion));
        var actualAggregateIds = outboxMessages
            .Select(message => message.AggregateId ?? Guid.Empty)
            .OrderBy(value => value)
            .ToList();
        Assert.Equal(
            new[] { seeded.PromotionAId, seeded.PromotionBId }.OrderBy(value => value).ToList(),
            actualAggregateIds);

        Assert.NotNull(productDocument);
        Assert.NotNull(listingDocument);
        Assert.NotNull(publicAffiliateProduct);
        Assert.NotNull(rawProductDocument);
        Assert.Single(rawProductDocumentsByBusinessKey);
        Assert.Single(rawProductDocumentsBySlug);
        Assert.Equal(2, promotionDocuments.Count);
        Assert.Equal(2, promotionDocuments.Select(document => document.PromotionId).Distinct().Count());

        var promotionDocumentA = Assert.Single(promotionDocuments.Where(document => document.PromotionId == seeded.PromotionAId));
        var promotionDocumentB = Assert.Single(promotionDocuments.Where(document => document.PromotionId == seeded.PromotionBId));

        Assert.Equal(promotionAEvent.EventId, promotionDocumentA.LastEventId);
        Assert.Equal(expectedPromotionBEvent.EventId, promotionDocumentB.LastEventId);
        Assert.NotEqual(PriceCon08PromotionBReplayEventId, promotionDocumentB.LastEventId);
        Assert.Equal(2, promotionDocumentA.LastEventVersion);
        Assert.Equal(2, promotionDocumentB.LastEventVersion);

        Assert.True(productDocument!.HasActivePromotion);
        Assert.True(
            rawProductDocument!.Contains("ActivePromotionId"),
            $"Raw product document is missing ActivePromotionId. Raw: {rawProductDocument.ToJson(new JsonWriterSettings { Indent = false })}");
        Assert.Equal(
            seeded.PromotionAId.ToString(),
            rawProductDocument["ActivePromotionId"].ToString());
        var roundTrippedRawProductDocument = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<ProductDocument>(rawProductDocument);
        Assert.Equal(seeded.PromotionAId, roundTrippedRawProductDocument.ActivePromotionId);
        Assert.Equal(rawProductDocument["_id"].AsObjectId.ToString(), productDocument.Id);
        Assert.Equal(seeded.TenantId.ToString(), rawProductDocument["TenantId"].ToString());
        Assert.Equal(seeded.ProductId.ToString(), rawProductDocument["ProductId"].ToString());
        Assert.Equal(PriceCon08ProductSlug, productDocument.Slug);
        Assert.Equal(seeded.PromotionAId, productDocument.ActivePromotionId);
        Assert.Equal("20% OFF", productDocument.PromotionBadge);
        Assert.Equal("percentage", productDocument.PromotionDiscountType);
        Assert.Equal(PriceCon08PromotionADiscountValue, productDocument.PromotionDiscountValue);
        Assert.Equal(PriceCon08PromotionAMaxDiscountAmount, productDocument.PromotionMaxDiscountAmount);
        Assert.Equal(InitialSellPrice, productDocument.Price);
        Assert.Equal(PriceCon08WinningVisiblePrice, productDocument.DiscountedPrice);
        Assert.Equal(BaselineStorefrontEventId, productDocument.LastEventId);
        Assert.Equal(
            ProductEventVersionHelper.ToProductEventVersion(seeded.ProductProjectionMutationVersion),
            productDocument.LastEventVersion);

        var storefrontVariant = Assert.Single(productDocument.Variants.Where(entry => entry.VariantId == seeded.DefaultVariantId));
        Assert.Equal(InitialSellPrice, storefrontVariant.Price);
        Assert.Equal(
            ProductEventVersionHelper.ToProductEventVersion(seeded.DefaultVariantProjectionMutationVersion),
            storefrontVariant.LastEventVersion);

        Assert.Equal(InitialSellPrice, listingDocument!.Price);
        Assert.Equal(BaselineCommerceHubEventId, listingDocument.LastEventId);
        Assert.Equal(
            ProductEventVersionHelper.ToProductEventVersion(seeded.DefaultVariantProjectionMutationVersion),
            listingDocument.LastPriceEventVersion);
        Assert.Equal(SeededUtc, listingDocument.LastPriceEventTimestamp);

        Assert.Equal(InitialSellPrice, publicAffiliateProduct!.Price);
        Assert.Null(publicAffiliateProduct.MinVariantPrice);
        Assert.Null(publicAffiliateProduct.MaxVariantPrice);
        Assert.False(publicAffiliateProduct.HasVariants);
        Assert.True(publicAffiliateProduct.InStock);
        Assert.Equal(10, publicAffiliateProduct.TotalAvailableStock);

        Assert.All(
            new[] { productDocument.DiscountedPrice!.Value, listingDocument.Price, publicAffiliateProduct.Price!.Value },
            visiblePrice => Assert.InRange(visiblePrice, 0m, InitialSellPrice));

        progressEntries.Enqueue("verification:price-con-08-durable-state-converged");
    }

    private static async Task AssertPriceCon09ActiveStateAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        string mongoConnectionString,
        IConnectionMultiplexer redisMultiplexer,
        IDistributedLockService distributedLockService,
        CartOwner cartOwner,
        PriceCon09ScenarioSeed seeded,
        PromotionUpdatedEvent productActivationEvent,
        PromotionUpdatedEvent categoryActivationEvent,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var promotions = await verifyContext.Promotions
            .AsNoTracking()
            .Where(entry => entry.Id == seeded.ProductPromotionId || entry.Id == seeded.CategoryPromotionId)
            .OrderBy(entry => entry.Id)
            .ToListAsync();
        var activationOutboxMessages = await verifyContext.OutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                (message.AggregateId == seeded.ProductPromotionId || message.AggregateId == seeded.CategoryPromotionId) &&
                message.EventType == typeof(PromotionUpdatedEvent).FullName)
            .ToListAsync();
        var expiredOutboxMessages = await verifyContext.OutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                (message.AggregateId == seeded.ProductPromotionId || message.AggregateId == seeded.CategoryPromotionId) &&
                message.EventType == typeof(PromotionExpiredEvent).FullName)
            .ToListAsync();

        Assert.Equal(2, promotions.Count);
        Assert.All(promotions, promotion => Assert.True(promotion.IsActive));
        Assert.All(promotions, promotion => Assert.Equal(2, promotion.ProjectionMutationVersion));
        Assert.Equal(2, activationOutboxMessages.Count);
        Assert.All(activationOutboxMessages, message => Assert.NotNull(message.ProcessedAtUtc));
        Assert.Empty(expiredOutboxMessages);

        var productDocument = await mongoDatabase
            .GetCollection<ProductDocument>("products")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();
        var categoryDocument = await mongoDatabase
            .GetCollection<CategoryDocument>("categories")
            .Find(document => document.TenantId == seeded.TenantId && document.CategoryId == CategoryId)
            .FirstOrDefaultAsync();
        var promotionDocuments = await mongoDatabase
            .GetCollection<PromotionDocument>("promotions")
            .Find(document => document.TenantId == seeded.TenantId)
            .ToListAsync();
        var listingDocument = await mongoDatabase
            .GetCollection<CommerceHubProductDocument>("commercehub_products")
            .Find(document => document.TenantId == seeded.TenantId && document.Id == CommerceHubListingId)
            .FirstOrDefaultAsync();
        var publicAffiliateProduct = await mongoDatabase
            .GetCollection<PublicAffiliateProductDocument>("public_affiliate_products")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();
        var rawProductDocuments = await mongoDatabase
            .GetCollection<BsonDocument>("products")
            .Find(new BsonDocument
            {
                { "TenantId", seeded.TenantId.ToString() },
                { "ProductId", seeded.ProductId.ToString() }
            })
            .ToListAsync();
        var rawCategoryDocuments = await mongoDatabase
            .GetCollection<BsonDocument>("categories")
            .Find(new BsonDocument
            {
                { "TenantId", seeded.TenantId.ToString() },
                { "CategoryId", CategoryId.ToString() }
            })
            .ToListAsync();

        Assert.NotNull(productDocument);
        Assert.NotNull(categoryDocument);
        Assert.NotNull(listingDocument);
        Assert.NotNull(publicAffiliateProduct);
        Assert.Equal(2, promotionDocuments.Count);
        Assert.Single(rawProductDocuments);
        Assert.Single(rawCategoryDocuments);

        var productPromotionDocument = Assert.Single(
            promotionDocuments.Where(document => document.PromotionId == seeded.ProductPromotionId));
        var categoryPromotionDocument = Assert.Single(
            promotionDocuments.Where(document => document.PromotionId == seeded.CategoryPromotionId));

        Assert.True(productDocument!.HasActivePromotion);
        Assert.Equal(seeded.ProductPromotionId, productDocument.ActivePromotionId);
        Assert.Equal("20% OFF", productDocument.PromotionBadge);
        Assert.Equal("percentage", productDocument.PromotionDiscountType);
        Assert.Equal(PriceCon09ProductPromotionDiscountValue, productDocument.PromotionDiscountValue);
        Assert.Equal(PriceCon09ProductPromotionMaxDiscountAmount, productDocument.PromotionMaxDiscountAmount);
        Assert.Equal(PriceCon09ExpiredVisiblePrice, productDocument.DiscountedPrice);

        Assert.True(categoryDocument!.HasActivePromotion);
        Assert.Equal(seeded.CategoryPromotionId, categoryDocument.ActivePromotionId);
        Assert.Equal("30% OFF", categoryDocument.PromotionBannerText);
        Assert.Equal(PriceCon09CategoryBannerStyle, categoryDocument.PromotionBannerStyle);

        Assert.Equal(productActivationEvent.EventId, productPromotionDocument.LastEventId);
        Assert.Equal(categoryActivationEvent.EventId, categoryPromotionDocument.LastEventId);
        Assert.Equal(2, productPromotionDocument.LastEventVersion);
        Assert.Equal(2, categoryPromotionDocument.LastEventVersion);
        Assert.True(productPromotionDocument.IsActive);
        Assert.True(categoryPromotionDocument.IsActive);

        Assert.Equal(InitialSellPrice, listingDocument!.Price);
        Assert.Equal(InitialSellPrice, publicAffiliateProduct!.Price);
        Assert.Null(publicAffiliateProduct.MinVariantPrice);
        Assert.Null(publicAffiliateProduct.MaxVariantPrice);

        var cart = await GetPriceCon09CartAsync(
            connectionString: verifyContext.Database.GetConnectionString()
                              ?? throw new InvalidOperationException("The test database connection string was not available."),
            tenantContext,
            mongoConnectionString,
            mongoDatabase,
            redisMultiplexer,
            distributedLockService,
            cartOwner,
            correlationId: "price-con-09-active-cart-read");
        var cartItem = Assert.Single(cart.Items);
        Assert.True(cartItem.HasActivePromotion);
        Assert.Equal("20% OFF", cartItem.PromotionBadge);
        Assert.Equal("percentage", cartItem.PromotionDiscountType);
        Assert.Equal(PriceCon09ProductPromotionDiscountValue, cartItem.PromotionDiscountValue);
        Assert.Equal(PriceCon09ExpiredVisiblePrice, cartItem.DiscountedUnitPrice);
        Assert.Equal(15m, cart.PromotionDiscountTotal);
        Assert.Equal(InitialSellPrice, cart.Subtotal);
        Assert.Equal(PriceCon09ExpiredVisiblePrice, cart.GrandTotal);

        progressEntries.Enqueue("verification:price-con-09-active-state");
    }

    private static async Task AssertPriceCon09ExpiredStateAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        string mongoConnectionString,
        IConnectionMultiplexer redisMultiplexer,
        IDistributedLockService distributedLockService,
        CartOwner cartOwner,
        PriceCon09ScenarioSeed seeded,
        PromotionExpiredEvent productExpiredEvent,
        PromotionExpiredEvent categoryExpiredEvent,
        ConcurrentQueue<string> progressEntries,
        bool requirePromotionLastEventIds = true)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var promotions = await verifyContext.Promotions
            .AsNoTracking()
            .Where(entry => entry.Id == seeded.ProductPromotionId || entry.Id == seeded.CategoryPromotionId)
            .OrderBy(entry => entry.Id)
            .ToListAsync();
        var activationOutboxMessages = await verifyContext.OutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                (message.AggregateId == seeded.ProductPromotionId || message.AggregateId == seeded.CategoryPromotionId) &&
                message.EventType == typeof(PromotionUpdatedEvent).FullName)
            .ToListAsync();
        var expiredOutboxMessages = await verifyContext.OutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                (message.AggregateId == seeded.ProductPromotionId || message.AggregateId == seeded.CategoryPromotionId) &&
                message.EventType == typeof(PromotionExpiredEvent).FullName)
            .OrderBy(message => message.AggregateId)
            .ToListAsync();

        Assert.Equal(2, promotions.Count);
        Assert.All(promotions, promotion => Assert.False(promotion.IsActive));
        Assert.All(promotions, promotion => Assert.Equal(3, promotion.ProjectionMutationVersion));
        Assert.Equal(2, activationOutboxMessages.Count);
        Assert.Equal(2, expiredOutboxMessages.Count);
        Assert.All(expiredOutboxMessages, message => Assert.NotNull(message.ProcessedAtUtc));
        Assert.All(expiredOutboxMessages, message => Assert.Equal(3, message.EventVersion));

        var productDocument = await mongoDatabase
            .GetCollection<ProductDocument>("products")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();
        var categoryDocument = await mongoDatabase
            .GetCollection<CategoryDocument>("categories")
            .Find(document => document.TenantId == seeded.TenantId && document.CategoryId == CategoryId)
            .FirstOrDefaultAsync();
        var promotionDocuments = await mongoDatabase
            .GetCollection<PromotionDocument>("promotions")
            .Find(document => document.TenantId == seeded.TenantId)
            .ToListAsync();
        var listingDocument = await mongoDatabase
            .GetCollection<CommerceHubProductDocument>("commercehub_products")
            .Find(document => document.TenantId == seeded.TenantId && document.Id == CommerceHubListingId)
            .FirstOrDefaultAsync();
        var publicAffiliateProduct = await mongoDatabase
            .GetCollection<PublicAffiliateProductDocument>("public_affiliate_products")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();
        var rawProductDocuments = await mongoDatabase
            .GetCollection<BsonDocument>("products")
            .Find(new BsonDocument
            {
                { "TenantId", seeded.TenantId.ToString() },
                { "ProductId", seeded.ProductId.ToString() }
            })
            .ToListAsync();
        var rawCategoryDocuments = await mongoDatabase
            .GetCollection<BsonDocument>("categories")
            .Find(new BsonDocument
            {
                { "TenantId", seeded.TenantId.ToString() },
                { "CategoryId", CategoryId.ToString() }
            })
            .ToListAsync();

        Assert.NotNull(productDocument);
        Assert.NotNull(categoryDocument);
        Assert.NotNull(listingDocument);
        Assert.NotNull(publicAffiliateProduct);
        Assert.Single(rawProductDocuments);
        Assert.Single(rawCategoryDocuments);
        Assert.Equal(2, promotionDocuments.Count);
        Assert.Equal(2, promotionDocuments.Select(document => document.PromotionId).Distinct().Count());

        var productPromotionDocument = Assert.Single(
            promotionDocuments.Where(document => document.PromotionId == seeded.ProductPromotionId));
        var categoryPromotionDocument = Assert.Single(
            promotionDocuments.Where(document => document.PromotionId == seeded.CategoryPromotionId));

        Assert.False(productDocument!.HasActivePromotion);
        Assert.Null(productDocument.ActivePromotionId);
        Assert.Null(productDocument.PromotionBadge);
        Assert.Null(productDocument.PromotionDiscountType);
        Assert.Null(productDocument.PromotionDiscountValue);
        Assert.Null(productDocument.PromotionMaxDiscountAmount);
        Assert.Null(productDocument.DiscountedPrice);
        Assert.False(ProductSearchDocumentMapper.MapFromProductDocument(productDocument).HasActivePromotion);

        Assert.False(categoryDocument!.HasActivePromotion);
        Assert.Null(categoryDocument.ActivePromotionId);
        Assert.Null(categoryDocument.PromotionBannerText);
        Assert.Null(categoryDocument.PromotionEndDate);
        Assert.Null(categoryDocument.PromotionBannerStyle);

        Assert.False(productPromotionDocument.IsActive);
        Assert.False(categoryPromotionDocument.IsActive);
        if (requirePromotionLastEventIds)
        {
            Assert.Equal(productExpiredEvent.EventId, productPromotionDocument.LastEventId);
            Assert.Equal(categoryExpiredEvent.EventId, categoryPromotionDocument.LastEventId);
        }
        Assert.Equal(3, productPromotionDocument.LastEventVersion);
        Assert.Equal(3, categoryPromotionDocument.LastEventVersion);

        Assert.Equal(InitialSellPrice, listingDocument!.Price);
        Assert.Equal(InitialSellPrice, publicAffiliateProduct!.Price);
        Assert.Null(publicAffiliateProduct.MinVariantPrice);
        Assert.Null(publicAffiliateProduct.MaxVariantPrice);
        Assert.False(publicAffiliateProduct.HasVariants);
        Assert.True(publicAffiliateProduct.InStock);

        var cart = await GetPriceCon09CartAsync(
            connectionString: verifyContext.Database.GetConnectionString()
                              ?? throw new InvalidOperationException("The test database connection string was not available."),
            tenantContext,
            mongoConnectionString,
            mongoDatabase,
            redisMultiplexer,
            distributedLockService,
            cartOwner,
            correlationId: "price-con-09-expired-cart-read");
        var cartItem = Assert.Single(cart.Items);
        Assert.False(cartItem.HasActivePromotion);
        Assert.Null(cartItem.PromotionBadge);
        Assert.Null(cartItem.PromotionDiscountType);
        Assert.Null(cartItem.PromotionDiscountValue);
        Assert.Null(cartItem.DiscountedUnitPrice);
        Assert.Equal(0m, cart.PromotionDiscountTotal);
        Assert.Equal(InitialSellPrice, cart.Subtotal);
        Assert.Equal(InitialSellPrice, cart.GrandTotal);

        progressEntries.Enqueue("verification:price-con-09-expired-state");
    }

    private static async Task AssertPriceCon10ActiveOverlapVisibilityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        PriceCon09ScenarioSeed seeded,
        PromotionUpdatedEvent productActivationEvent,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var product = await verifyContext.Products
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.ProductId);
        var variant = await verifyContext.ProductVariants
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.DefaultVariantId);
        var processedEvents = await verifyContext.Set<ProcessedEvent>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(entry => entry.EventId == productActivationEvent.EventId)
            .ToListAsync();

        var productDocuments = await mongoDatabase
            .GetCollection<ProductDocument>("products")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .ToListAsync();
        var categoryDocuments = await mongoDatabase
            .GetCollection<CategoryDocument>("categories")
            .Find(document => document.TenantId == seeded.TenantId && document.CategoryId == CategoryId)
            .ToListAsync();
        var promotionDocuments = await mongoDatabase
            .GetCollection<PromotionDocument>("promotions")
            .Find(document => document.TenantId == seeded.TenantId)
            .ToListAsync();
        var listingDocument = await mongoDatabase
            .GetCollection<CommerceHubProductDocument>("commercehub_products")
            .Find(document => document.TenantId == seeded.TenantId && document.Id == CommerceHubListingId)
            .FirstOrDefaultAsync();
        var publicAffiliateProduct = await mongoDatabase
            .GetCollection<PublicAffiliateProductDocument>("public_affiliate_products")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();

        Assert.Equal(InitialSellPrice, product.SalePrice);
        Assert.Equal(InitialSellPrice, variant.Price);
        Assert.Empty(processedEvents);
        Assert.Single(productDocuments);
        Assert.Single(categoryDocuments);
        Assert.Equal(2, promotionDocuments.Count);
        Assert.Equal(2, promotionDocuments.Select(document => document.PromotionId).Distinct().Count());

        var productDocument = productDocuments.Single();
        Assert.True(productDocument.HasActivePromotion);
        Assert.Equal(seeded.ProductPromotionId, productDocument.ActivePromotionId);
        Assert.Equal(PriceCon09ExpiredVisiblePrice, productDocument.DiscountedPrice);
        Assert.True(ProductSearchDocumentMapper.MapFromProductDocument(productDocument).HasActivePromotion);

        var categoryDocument = categoryDocuments.Single();
        Assert.True(categoryDocument.HasActivePromotion);
        Assert.Equal(seeded.CategoryPromotionId, categoryDocument.ActivePromotionId);

        Assert.NotNull(listingDocument);
        Assert.NotNull(publicAffiliateProduct);
        Assert.Equal(InitialSellPrice, listingDocument!.Price);
        Assert.Equal(InitialSellPrice, publicAffiliateProduct!.Price);
        Assert.Null(publicAffiliateProduct.MinVariantPrice);
        Assert.Null(publicAffiliateProduct.MaxVariantPrice);

        progressEntries.Enqueue("verification:price-con-10-active-duplicate-overlap-visible-state-stable");
    }

    private static async Task<IReadOnlyList<IntegrationEvent>> DispatchCommittedOutboxAsync(
        string connectionString,
        TestTenantContext tenantContext,
        ConcurrentQueue<string> progressEntries)
    {
        var publishedEvents = new List<IntegrationEvent>();
        var publishEndpoint = new Mock<IPublishEndpoint>(MockBehavior.Strict);
        publishEndpoint
            .Setup(endpoint => endpoint.Publish(
                It.IsAny<object>(),
                It.IsAny<Type>(),
                It.IsAny<IPipe<PublishContext>>(),
                It.IsAny<CancellationToken>()))
            .Callback<object, Type, IPipe<PublishContext>, CancellationToken>((message, _, _, _) =>
            {
                if (message is IntegrationEvent integrationEvent)
                {
                    publishedEvents.Add(integrationEvent);
                }
            })
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<ITenantContext>(_ => tenantContext);
        services.AddScoped<IPublishEndpoint>(_ => publishEndpoint.Object);

        await using var serviceProvider = services.BuildServiceProvider();
        var job = new OutboxPublisherJob(serviceProvider, NullLogger<OutboxPublisherJob>.Instance);
        await job.ExecuteAsync();

        progressEntries.Enqueue($"price-con-01:outbox-dispatched:{publishedEvents.Count}");
        return publishedEvents;
    }

    private static async Task ApplyPublishedEventsAsync(
        string connectionString,
        TestTenantContext tenantContext,
        string mongoConnectionString,
        IMongoDatabase mongoDatabase,
        IReadOnlyList<IntegrationEvent> publishedEvents)
    {
        await ApplyPublishedEventsWithChannelControlAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoDatabase,
            publishedEvents,
            applyStorefrontProduct: true,
            applyStorefrontVariant: true,
            applyCommerceHub: true,
            applyAffiliate: true,
            progressEntries: null);
    }

    private static async Task ApplyPublishedEventsWithChannelControlAsync(
        string connectionString,
        TestTenantContext tenantContext,
        string mongoConnectionString,
        IMongoDatabase mongoDatabase,
        IReadOnlyList<IntegrationEvent> publishedEvents,
        bool applyStorefrontProduct,
        bool applyStorefrontVariant,
        bool applyCommerceHub,
        bool applyAffiliate,
        ConcurrentQueue<string>? progressEntries)
    {
        await using var projectionDbContext = CreateDbContext(connectionString, tenantContext);
        var mongoDbContext = CreateMongoDbContext(mongoConnectionString, mongoDatabase.DatabaseNamespace.DatabaseName);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storefront:UrlTemplate"] = "https://{slug}.example.test"
            })
            .Build();

        var productReadRepository = new ProductReadRepository(mongoDbContext);
        var storefrontProductMutationBoundary = new StorefrontProductProjectionMutationBoundary(productReadRepository);
        var productProjectionWriter = new ProductSqlAuthorityProjectionWriter(
            projectionDbContext,
            storefrontProductMutationBoundary);
        var productUpdatedEventConsumer = new ProductUpdatedEventConsumer(
            productReadRepository,
            productProjectionWriter,
            NullLogger<ProductUpdatedEventConsumer>.Instance);
        var productProjectionConsumer = new ProductVariantProjectionConsumer(
            mongoDatabase,
            Mock.Of<IStorefrontProductCacheInvalidator>(),
            CreateProductSearchIndexServiceMock().Object,
            NullLogger<ProductVariantProjectionConsumer>.Instance);
        var commerceHubProjectionConsumer = new CommerceHubProjectionConsumer(
            mongoDbContext,
            projectionDbContext,
            CreateCommerceHubSearchIndexServiceMock().Object,
            NullLogger<CommerceHubProjectionConsumer>.Instance);
        var promotionProjectionConsumer = new PromotionProjectionConsumer(
            mongoDatabase,
            projectionDbContext,
            Mock.Of<IStorefrontProductCacheInvalidator>(),
            CreateProductSearchIndexServiceMock().Object,
            NullLogger<PromotionProjectionConsumer>.Instance);
        var affiliateProjectionConsumer = new AffiliateProductIntelligenceProjectionConsumer(
            mongoDbContext,
            projectionDbContext,
            configuration,
            NullLogger<AffiliateProductIntelligenceProjectionConsumer>.Instance);
        var affiliatePublicProjectionConsumer = new AffiliateProductPolicyProjectionConsumer(
            mongoDbContext,
            CreateElasticClientMock().Object,
            Options.Create(new ElasticsearchSettings
            {
                Enabled = false,
                PublicAffiliateProductsIndexName = "price-con-affiliate-test"
            }),
            projectionDbContext,
            configuration,
            NullLogger<AffiliateProductPolicyProjectionConsumer>.Instance);

        foreach (var publishedEvent in publishedEvents)
        {
            switch (publishedEvent)
            {
                case ProductUpdatedEvent productUpdatedEvent:
                    if (applyStorefrontProduct)
                    {
                        await productUpdatedEventConsumer.Consume(
                            BuildConsumeContext(productUpdatedEvent).Object);
                        progressEntries?.Enqueue($"projection:storefront-product-applied:{productUpdatedEvent.EventId}");
                    }

                    if (applyAffiliate)
                    {
                        await affiliateProjectionConsumer.Consume(
                            BuildConsumeContext(productUpdatedEvent).Object);
                        progressEntries?.Enqueue($"projection:affiliate-product-applied:{productUpdatedEvent.EventId}");
                    }
                    break;
                case ProductVariantUpdatedEvent variantUpdatedEvent:
                    if (applyStorefrontVariant)
                    {
                        await productProjectionConsumer.Consume(
                            BuildConsumeContext(variantUpdatedEvent).Object);
                        progressEntries?.Enqueue($"projection:storefront-variant-applied:{variantUpdatedEvent.EventId}");
                    }
                    break;
                case VariantPriceUpdatedEvent variantPriceUpdatedEvent:
                    if (applyCommerceHub)
                    {
                        await commerceHubProjectionConsumer.Consume(
                            BuildConsumeContext(variantPriceUpdatedEvent).Object);
                        progressEntries?.Enqueue($"projection:commercehub-price-applied:{variantPriceUpdatedEvent.EventId}");
                    }
                    break;
                case PromotionCreatedEvent promotionCreatedEvent:
                    if (applyStorefrontProduct)
                    {
                        await promotionProjectionConsumer.Consume(
                            BuildConsumeContext(promotionCreatedEvent).Object);
                        progressEntries?.Enqueue($"projection:promotion-created-applied:{promotionCreatedEvent.EventId}");
                    }

                    if (applyCommerceHub)
                    {
                        await commerceHubProjectionConsumer.Consume(
                            BuildConsumeContext(promotionCreatedEvent).Object);
                        progressEntries?.Enqueue($"projection:commercehub-promotion-created-applied:{promotionCreatedEvent.EventId}");
                    }

                    if (applyAffiliate)
                    {
                        await affiliatePublicProjectionConsumer.Consume(
                            BuildConsumeContext(promotionCreatedEvent).Object);
                        progressEntries?.Enqueue($"projection:affiliate-promotion-created-applied:{promotionCreatedEvent.EventId}");
                    }
                    break;
                case PromotionUpdatedEvent promotionUpdatedEvent:
                    if (applyStorefrontProduct)
                    {
                        await promotionProjectionConsumer.Consume(
                            BuildConsumeContext(promotionUpdatedEvent).Object);
                        progressEntries?.Enqueue($"projection:promotion-updated-applied:{promotionUpdatedEvent.EventId}");
                    }

                    if (applyCommerceHub)
                    {
                        await commerceHubProjectionConsumer.Consume(
                            BuildConsumeContext(promotionUpdatedEvent).Object);
                        progressEntries?.Enqueue($"projection:commercehub-promotion-updated-applied:{promotionUpdatedEvent.EventId}");
                    }

                    if (applyAffiliate)
                    {
                        await affiliatePublicProjectionConsumer.Consume(
                            BuildConsumeContext(promotionUpdatedEvent).Object);
                        progressEntries?.Enqueue($"projection:affiliate-promotion-updated-applied:{promotionUpdatedEvent.EventId}");
                    }
                    break;
                case PromotionDeletedEvent promotionDeletedEvent:
                    if (applyStorefrontProduct)
                    {
                        await promotionProjectionConsumer.Consume(
                            BuildConsumeContext(promotionDeletedEvent).Object);
                        progressEntries?.Enqueue($"projection:promotion-deleted-applied:{promotionDeletedEvent.EventId}");
                    }

                    if (applyCommerceHub)
                    {
                        await commerceHubProjectionConsumer.Consume(
                            BuildConsumeContext(promotionDeletedEvent).Object);
                        progressEntries?.Enqueue($"projection:commercehub-promotion-deleted-applied:{promotionDeletedEvent.EventId}");
                    }

                    if (applyAffiliate)
                    {
                        await affiliatePublicProjectionConsumer.Consume(
                            BuildConsumeContext(promotionDeletedEvent).Object);
                        progressEntries?.Enqueue($"projection:affiliate-promotion-deleted-applied:{promotionDeletedEvent.EventId}");
                    }
                    break;
                case PromotionExpiredEvent promotionExpiredEvent:
                    if (applyStorefrontProduct)
                    {
                        await promotionProjectionConsumer.Consume(
                            BuildConsumeContext(promotionExpiredEvent).Object);
                        progressEntries?.Enqueue($"projection:promotion-expired-applied:{promotionExpiredEvent.EventId}");
                    }

                    if (applyCommerceHub)
                    {
                        await commerceHubProjectionConsumer.Consume(
                            BuildConsumeContext(promotionExpiredEvent).Object);
                        progressEntries?.Enqueue($"projection:commercehub-promotion-expired-applied:{promotionExpiredEvent.EventId}");
                    }

                    if (applyAffiliate)
                    {
                        await affiliatePublicProjectionConsumer.Consume(
                            BuildConsumeContext(promotionExpiredEvent).Object);
                        progressEntries?.Enqueue($"projection:affiliate-promotion-expired-applied:{promotionExpiredEvent.EventId}");
                    }
                    break;
            }
        }
    }

    private static async Task<CartDto> GetPriceCon09CartAsync(
        string connectionString,
        TestTenantContext tenantContext,
        string mongoConnectionString,
        IMongoDatabase mongoDatabase,
        IConnectionMultiplexer redisMultiplexer,
        IDistributedLockService distributedLockService,
        CartOwner cartOwner,
        string correlationId)
    {
        await using var cartService = CreatePriceCon09CartService(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoDatabase,
            redisMultiplexer,
            distributedLockService);

        return await cartService.Service.GetCartAsync(
            TenantId,
            cartOwner,
            correlationId);
    }

    private static PriceCon09CartServiceScope CreatePriceCon09CartService(
        string connectionString,
        TestTenantContext tenantContext,
        string mongoConnectionString,
        IMongoDatabase mongoDatabase,
        IConnectionMultiplexer redisMultiplexer,
        IDistributedLockService distributedLockService)
    {
        var dbContext = CreateDbContext(connectionString, tenantContext);
        var mongoDbContext = CreateMongoDbContext(
            mongoConnectionString,
            mongoDatabase.DatabaseNamespace.DatabaseName);
        var productRepository = new ProductReadRepository(mongoDbContext);
        var promotionEngine = new Mock<IPromotionEngine>(MockBehavior.Loose);
        promotionEngine
            .Setup(engine => engine.GetActiveCartPromotionAsync(
                It.IsAny<Guid>(),
                It.IsAny<decimal>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CartPromotionResult?)null);
        var taxCalculationService = new Mock<ITaxCalculationService>(MockBehavior.Strict);
        taxCalculationService
            .Setup(service => service.CalculateAsync(
                It.IsAny<Guid>(),
                It.IsAny<decimal>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaxComputationResult(0m, 0m, "test", null));

        var service = new CartService(
            dbContext,
            new RedisConnectionFactory(
                Options.Create(new RedisSettings
                {
                    Enabled = true,
                    InstanceName = "price-con-09"
                }),
                NullLogger<RedisConnectionFactory>.Instance,
                redisMultiplexer),
            Options.Create(new RedisSettings
            {
                Enabled = true,
                InstanceName = "price-con-09"
            }),
            productRepository,
            new NoOpEventPublisher(),
            mongoDatabase,
            promotionEngine.Object,
            taxCalculationService.Object,
            new CommerceReliabilityTelemetry(),
            distributedLockService,
            NullLogger<CartService>.Instance);

        return new PriceCon09CartServiceScope(service, dbContext, mongoDbContext);
    }

    private static IConnectionMultiplexer CreatePriceCon09RedisMultiplexer()
    {
        return ConnectionMultiplexer.Connect(GetRequiredRedisConnectionString());
    }

    private static ServiceProvider CreatePromotionExpirationWorkerServiceProvider(
        string connectionString,
        TestTenantContext tenantContext,
        string correlationId,
        TimeProvider timeProvider,
        IReadOnlyCollection<IInterceptor> interceptors)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(timeProvider);
        services.AddScoped<ITenantContext>(_ => tenantContext);
        services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(connectionString);
            if (interceptors.Count > 0)
            {
                options.AddInterceptors(interceptors);
            }
        });
        services.AddScoped<IEventPublisher>(serviceProvider =>
            new AuthoritativeOutboxTestPublisher(
                serviceProvider.GetRequiredService<ApplicationDbContext>(),
                tenantContext.TenantId ?? throw new InvalidOperationException("PRICE-CON-09 requires a tenant id."),
                correlationId));

        return services.BuildServiceProvider();
    }

    private static async Task ExecutePromotionExpirationWorkerOnceAsync(PromotionExpirationService service)
    {
        var checkMethod = typeof(PromotionExpirationService).GetMethod(
            "CheckAndExpirePromotionsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
                          ?? throw new InvalidOperationException(
                              "PRICE-CON-09 could not find the promotion expiration worker single-pass method.");

        var task = checkMethod.Invoke(service, new object[] { CancellationToken.None }) as Task
                   ?? throw new InvalidOperationException(
                       "PRICE-CON-09 failed to invoke the promotion expiration worker single-pass method.");

        await task;
    }

    private static async Task PoisonPriceCon09ReadModelsAsync(
        IMongoDatabase mongoDatabase,
        PriceCon09ScenarioSeed seeded)
    {
        await mongoDatabase.GetCollection<PromotionDocument>("promotions")
            .DeleteManyAsync(document => document.TenantId == seeded.TenantId);

        await mongoDatabase.GetCollection<ProductDocument>("products")
            .UpdateOneAsync(
                document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId,
                Builders<ProductDocument>.Update
                    .Set(document => document.HasActivePromotion, true)
                    .Set(document => document.PromotionBadge, "20% OFF")
                    .Set("ActivePromotionId", seeded.ProductPromotionId.ToString())
                    .Set(document => document.PromotionDiscountType, "percentage")
                    .Set(document => document.PromotionDiscountValue, PriceCon09ProductPromotionDiscountValue)
                    .Set(document => document.PromotionMaxDiscountAmount, PriceCon09ProductPromotionMaxDiscountAmount)
                    .Set(document => document.PromotionStartDate, seeded.PromotionStartDateUtc)
                    .Set(document => document.PromotionEndDate, seeded.PromotionEndDateUtc)
                    .Set(document => document.SearchBoostOverride, 0d));

        await mongoDatabase.GetCollection<CategoryDocument>("categories")
            .UpdateOneAsync(
                document => document.TenantId == seeded.TenantId && document.CategoryId == CategoryId,
                Builders<CategoryDocument>.Update
                    .Set(document => document.HasActivePromotion, true)
                    .Set(document => document.PromotionBannerText, "30% OFF")
                    .Set("ActivePromotionId", seeded.CategoryPromotionId.ToString())
                    .Set(document => document.PromotionEndDate, seeded.PromotionEndDateUtc)
                    .Set(document => document.PromotionBannerStyle, PriceCon09CategoryBannerStyle));
    }

    private static async Task PoisonPriceCon10PromotionDecorationsOnlyAsync(
        IMongoDatabase mongoDatabase,
        PriceCon09ScenarioSeed seeded)
    {
        await mongoDatabase.GetCollection<ProductDocument>("products")
            .UpdateOneAsync(
                document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId,
                PromotionVisibilityProjectionSupport.BuildClearProductPromotionUpdate());

        await mongoDatabase.GetCollection<CategoryDocument>("categories")
            .UpdateOneAsync(
                document => document.TenantId == seeded.TenantId && document.CategoryId == CategoryId,
                Builders<CategoryDocument>.Update
                    .Set(document => document.HasActivePromotion, false)
                    .Set(document => document.PromotionBannerText, (string?)null)
                    .Set("ActivePromotionId", (string?)null)
                    .Set(document => document.PromotionEndDate, (DateTime?)null)
                    .Set(document => document.PromotionBannerStyle, (string?)null));
    }

    private static async Task ConsumePriceCon10PromotionEventWithSearchPauseAsync(
        string connectionString,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        PromotionUpdatedEvent promotionUpdatedEvent,
        string workerName,
        PriceCon10PromotionProjectionOverlapWindow overlapWindow)
    {
        await using var projectionDbContext = CreateDbContext(connectionString, tenantContext);

        var searchIndexService = new Mock<IUnifiedSearchIndexService>(MockBehavior.Loose);
        searchIndexService
            .Setup(service => service.IndexProductAsync(
                It.IsAny<ProductSearchDocument>(),
                It.IsAny<CancellationToken>()))
            .Returns<ProductSearchDocument, CancellationToken>(async (document, cancellationToken) =>
            {
                await overlapWindow.PauseOnSearchIndexAsync(workerName, document.ProductId, cancellationToken);
                return true;
            });

        var consumer = new PromotionProjectionConsumer(
            mongoDatabase,
            projectionDbContext,
            CreateProductCacheInvalidatorMock().Object,
            searchIndexService.Object,
            NullLogger<PromotionProjectionConsumer>.Instance);

        await consumer.Consume(BuildConsumeContext(promotionUpdatedEvent).Object);
    }

    private static async Task RunPriceCon10DuplicatePromotionRepairAsync(
        string connectionString,
        TestTenantContext tenantContext,
        string mongoConnectionString,
        IMongoDatabase mongoDatabase)
    {
        async Task RunRepairAsync()
        {
            await using var repairDbContext = CreateDbContext(connectionString, tenantContext);
            var repairService = new DiscountReadModelRepairService(
                repairDbContext,
                CreateMongoDbContext(
                    mongoConnectionString,
                    mongoDatabase.DatabaseNamespace.DatabaseName),
                CreateUnifiedSearchIndexServiceMock().Object,
                CreateProductCacheInvalidatorMock().Object,
                NullLogger<DiscountReadModelRepairService>.Instance);

            await repairService.RebuildPromotionsAsync(TenantId);
        }

        await Task.WhenAll(RunRepairAsync(), RunRepairAsync());
    }

    private static ProductUpdatedEvent CloneProductUpdatedEventForReplay(
        ProductUpdatedEvent source,
        Guid eventId,
        DateTime occurredAtUtc,
        string correlationId)
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(source, source.GetType()))?.AsObject()
                   ?? throw new InvalidOperationException("Failed to clone ProductUpdatedEvent for deterministic replay.");
        node[nameof(IntegrationEvent.EventId)] = eventId;
        node[nameof(IntegrationEvent.OccurredAtUtc)] = occurredAtUtc;
        node[nameof(IntegrationEvent.CorrelationId)] = correlationId;
        node[nameof(IntegrationEvent.IdempotencyKey)] = $"{correlationId}:{nameof(ProductUpdatedEvent)}";
        return node.Deserialize<ProductUpdatedEvent>(CaseInsensitiveJsonOptions)
               ?? throw new InvalidOperationException("Failed to materialize ProductUpdatedEvent replay clone.");
    }

    private static ProductVariantUpdatedEvent CloneProductVariantUpdatedEventForReplay(
        ProductVariantUpdatedEvent source,
        Guid eventId,
        DateTime occurredAtUtc,
        string correlationId)
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(source, source.GetType()))?.AsObject()
                   ?? throw new InvalidOperationException("Failed to clone ProductVariantUpdatedEvent for deterministic replay.");
        node[nameof(IntegrationEvent.EventId)] = eventId;
        node[nameof(IntegrationEvent.OccurredAtUtc)] = occurredAtUtc;
        node[nameof(IntegrationEvent.CorrelationId)] = correlationId;
        node[nameof(IntegrationEvent.IdempotencyKey)] = $"{correlationId}:{nameof(ProductVariantUpdatedEvent)}";
        return node.Deserialize<ProductVariantUpdatedEvent>(CaseInsensitiveJsonOptions)
               ?? throw new InvalidOperationException("Failed to materialize ProductVariantUpdatedEvent replay clone.");
    }

    private static VariantPriceUpdatedEvent CloneVariantPriceUpdatedEventForReplay(
        VariantPriceUpdatedEvent source,
        Guid eventId,
        DateTime occurredAtUtc,
        string correlationId)
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(source, source.GetType()))?.AsObject()
                   ?? throw new InvalidOperationException("Failed to clone VariantPriceUpdatedEvent for deterministic replay.");
        node[nameof(IntegrationEvent.EventId)] = eventId;
        node[nameof(IntegrationEvent.OccurredAtUtc)] = occurredAtUtc;
        node[nameof(IntegrationEvent.CorrelationId)] = correlationId;
        node[nameof(IntegrationEvent.IdempotencyKey)] = $"{correlationId}:{nameof(VariantPriceUpdatedEvent)}";
        return node.Deserialize<VariantPriceUpdatedEvent>(CaseInsensitiveJsonOptions)
               ?? throw new InvalidOperationException("Failed to materialize VariantPriceUpdatedEvent replay clone.");
    }

    private static PromotionUpdatedEvent ClonePromotionUpdatedEventForReplay(
        PromotionUpdatedEvent source,
        Guid eventId,
        DateTime occurredAtUtc,
        string correlationId)
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(source, source.GetType()))?.AsObject()
                   ?? throw new InvalidOperationException("Failed to clone PromotionUpdatedEvent for deterministic replay.");
        node[nameof(IntegrationEvent.EventId)] = eventId;
        node[nameof(IntegrationEvent.OccurredAtUtc)] = occurredAtUtc;
        node[nameof(IntegrationEvent.CorrelationId)] = correlationId;
        node[nameof(IntegrationEvent.IdempotencyKey)] = $"{correlationId}:{nameof(PromotionUpdatedEvent)}";
        return node.Deserialize<PromotionUpdatedEvent>(CaseInsensitiveJsonOptions)
               ?? throw new InvalidOperationException("Failed to materialize PromotionUpdatedEvent replay clone.");
    }

    private static MarketplaceEvents.ProductUpdatedEvent CloneMarketplaceProductUpdatedEventForReplay(
        MarketplaceEvents.ProductUpdatedEvent source,
        Guid eventId,
        DateTime occurredAtUtc,
        string correlationId)
    {
        return source with
        {
            EventId = eventId,
            OccurredAtUtc = occurredAtUtc,
            CorrelationId = correlationId
        };
    }

    private static async Task AssertFinalConvergenceAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        PriceScenarioSeed seeded,
        IReadOnlyCollection<PriceActorResult> results,
        IReadOnlyList<IntegrationEvent> publishedEvents,
        ConcurrentQueue<string> progressEntries)
    {
        var winner = results.Single(result => result.Actor.Name == "actor-a");
        var loser = results.Single(result => result.Actor.Name == "actor-b");

        Assert.True(winner.Succeeded, winner.PayloadJson ?? "Actor A did not commit the authoritative sell price.");
        Assert.NotNull(winner.Response);
        Assert.Equal(StatusCodes.Status200OK, winner.StatusCode);
        Assert.Equal(ActorAWinningSellPrice, winner.Response!.Price);

        Assert.False(loser.Succeeded);
        Assert.Equal(StatusCodes.Status409Conflict, loser.StatusCode);
        Assert.Contains("VariantConcurrencyConflict", loser.PayloadJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var product = await verifyContext.Products
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.ProductId);
        var persistedVariant = await verifyContext.ProductVariants
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.TargetVariantId);
        var allVariantOutboxMessages = await verifyContext.OutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.AggregateId == seeded.TargetVariantId)
            .OrderBy(message => message.CreatedAtUtc)
            .ThenBy(message => message.Id)
            .ToListAsync();
        var priceOutboxMessages = allVariantOutboxMessages
            .Where(message =>
                message.EventType == typeof(ProductVariantUpdatedEvent).FullName ||
                message.EventType == typeof(VariantPriceUpdatedEvent).FullName)
            .ToList();
        var processedEvents = await verifyContext.Set<ProcessedEvent>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .ToListAsync();
        var projectionOutboxMessages = await verifyContext.MarketplaceProjectionOutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .ToListAsync();
        var stockSyncMessages = await verifyContext.StockSyncOutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message => message.VariantId == seeded.TargetVariantId)
            .ToListAsync();

        Assert.Equal(seeded.ProductProjectionMutationVersion, product.ProjectionMutationVersion);
        Assert.Equal(ActorAWinningSellPrice, persistedVariant.Price);
        Assert.Equal(seeded.TargetVariantProjectionMutationVersion + 1, persistedVariant.ProjectionMutationVersion);
        Assert.Equal(TargetVariantSku, persistedVariant.SKU);
        Assert.Equal(TargetVariantBarcode, persistedVariant.Barcode);

        Assert.Equal(2, priceOutboxMessages.Count);
        Assert.Empty(stockSyncMessages);
        Assert.Empty(processedEvents);
        Assert.Empty(projectionOutboxMessages);

        var expectedEventVersion = ProductEventVersionHelper.ToProductEventVersion(
            seeded.TargetVariantProjectionMutationVersion + 1);
        Assert.All(priceOutboxMessages, message =>
        {
            Assert.Equal(expectedEventVersion, message.EventVersion);
            Assert.NotNull(message.ProcessedAtUtc);
            Assert.Equal(seeded.TargetVariantId, message.AggregateId);
            Assert.Equal(seeded.TenantId, message.TenantId);
        });

        Assert.Single(priceOutboxMessages.Where(message => message.EventType == typeof(ProductVariantUpdatedEvent).FullName));
        Assert.Single(priceOutboxMessages.Where(message => message.EventType == typeof(VariantPriceUpdatedEvent).FullName));

        Assert.Equal(2, publishedEvents.Count);
        var variantUpdatedEvent = Assert.IsType<ProductVariantUpdatedEvent>(
            publishedEvents.Single(message => message is ProductVariantUpdatedEvent));
        var priceUpdatedEvent = Assert.IsType<VariantPriceUpdatedEvent>(
            publishedEvents.Single(message => message is VariantPriceUpdatedEvent));

        Assert.Equal(expectedEventVersion, variantUpdatedEvent.EventVersion);
        Assert.Equal(expectedEventVersion, priceUpdatedEvent.EventVersion);
        Assert.Equal(seeded.TargetVariantId, variantUpdatedEvent.VariantId);
        Assert.Equal(seeded.TargetVariantId, priceUpdatedEvent.VariantId);
        Assert.Equal(ActorAWinningSellPrice, variantUpdatedEvent.Price);
        Assert.Equal(ActorAWinningSellPrice, priceUpdatedEvent.NewPrice);

        var productDocument = await mongoDatabase
            .GetCollection<ProductDocument>("products")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();
        var listingDocument = await mongoDatabase
            .GetCollection<CommerceHubProductDocument>("commercehub_products")
            .Find(document => document.TenantId == seeded.TenantId && document.Id == CommerceHubListingId)
            .FirstOrDefaultAsync();

        Assert.NotNull(productDocument);
        Assert.NotNull(listingDocument);

        var storefrontVariant = Assert.Single(
            productDocument!.Variants.Where(variant => variant.VariantId == seeded.TargetVariantId));
        Assert.Equal(ActorAWinningSellPrice, storefrontVariant.Price);
        Assert.Equal(expectedEventVersion, storefrontVariant.LastEventVersion);
        Assert.Equal(ActorAWinningSellPrice, listingDocument!.Price);
        Assert.Equal(expectedEventVersion, listingDocument.LastPriceEventVersion);
        Assert.Equal(priceUpdatedEvent.EventId, listingDocument.LastEventId);
        Assert.NotNull(listingDocument.LastPriceEventTimestamp);
        Assert.Equal(
            TruncateToMilliseconds(priceUpdatedEvent.OccurredAtUtc),
            TruncateToMilliseconds(listingDocument.LastPriceEventTimestamp.Value));
        Assert.True(listingDocument.IsPublished);

        progressEntries.Enqueue("verification:price-con-01-converged");
    }

    private static async Task AssertNoUncommittedReplayVisibilityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        PriceScenarioSeed seeded,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var persistedVariant = await verifyContext.ProductVariants
            .AsNoTracking()
            .SingleAsync(variant => variant.Id == seeded.TargetVariantId);
        var persistedOutboxMessages = await verifyContext.OutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.AggregateId == seeded.TargetVariantId &&
                (message.EventType == typeof(ProductVariantUpdatedEvent).FullName ||
                 message.EventType == typeof(VariantPriceUpdatedEvent).FullName))
            .ToListAsync();
        var idempotencyRecords = await verifyContext.ProductRequestIdempotencyRecords
            .AsNoTracking()
            .Where(record =>
                record.TenantId == seeded.TenantId &&
                record.ProductId == seeded.ProductId &&
                record.ProductVariantId == seeded.TargetVariantId &&
                record.Operation == "UPDATE_VARIANT")
            .ToListAsync();
        var processedEvents = await verifyContext.Set<ProcessedEvent>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .ToListAsync();
        var projectionOutboxMessages = await verifyContext.MarketplaceProjectionOutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .ToListAsync();
        var stockSyncMessages = await verifyContext.StockSyncOutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message => message.VariantId == seeded.TargetVariantId)
            .ToListAsync();

        var productDocument = await mongoDatabase
            .GetCollection<ProductDocument>("products")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();
        var listingDocument = await mongoDatabase
            .GetCollection<CommerceHubProductDocument>("commercehub_products")
            .Find(document => document.TenantId == seeded.TenantId && document.Id == CommerceHubListingId)
            .FirstOrDefaultAsync();

        Assert.NotNull(productDocument);
        Assert.NotNull(listingDocument);
        Assert.Equal(InitialSellPrice, persistedVariant.Price);
        Assert.Equal(seeded.TargetVariantProjectionMutationVersion, persistedVariant.ProjectionMutationVersion);
        Assert.Empty(persistedOutboxMessages);
        Assert.Empty(idempotencyRecords);
        Assert.Empty(processedEvents);
        Assert.Empty(projectionOutboxMessages);
        Assert.Empty(stockSyncMessages);

        var storefrontVariant = Assert.Single(
            productDocument!.Variants.Where(variant => variant.VariantId == seeded.TargetVariantId));
        Assert.Equal(InitialSellPrice, storefrontVariant.Price);
        Assert.Equal(
            ProductEventVersionHelper.ToProductEventVersion(seeded.TargetVariantProjectionMutationVersion),
            storefrontVariant.LastEventVersion);

        Assert.Equal(InitialSellPrice, listingDocument!.Price);
        Assert.Equal(
            ProductEventVersionHelper.ToProductEventVersion(seeded.TargetVariantProjectionMutationVersion),
            listingDocument.LastPriceEventVersion);
        Assert.Equal(SeededUtc, listingDocument.LastPriceEventTimestamp);

        progressEntries.Enqueue("verification:price-con-02-active-transaction-remained-invisible");
    }

    private static async Task AssertReplayConvergenceAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        PriceScenarioSeed seeded,
        PriceActor originalActor,
        PriceActor replayActor,
        IReadOnlyCollection<PriceActorResult> results,
        IReadOnlyList<IntegrationEvent> publishedEvents,
        ConcurrentQueue<string> progressEntries)
    {
        var original = results.Single(result => result.Actor.Name == originalActor.Name);
        var replay = results.Single(result => result.Actor.Name == replayActor.Name);

        Assert.True(original.Succeeded, original.PayloadJson ?? "PRICE-CON-02 original request did not commit the authoritative price mutation.");
        Assert.True(replay.Succeeded, replay.PayloadJson ?? "PRICE-CON-02 replay did not converge to the authoritative committed pricing truth.");
        Assert.NotNull(original.Response);
        Assert.NotNull(replay.Response);
        Assert.Equal(StatusCodes.Status200OK, original.StatusCode);
        Assert.Equal(StatusCodes.Status200OK, replay.StatusCode);
        Assert.Equal(seeded.TargetVariantId, original.Response!.Id);
        Assert.Equal(seeded.TargetVariantId, replay.Response!.Id);
        Assert.Equal(ReplayWinningSellPrice, original.Response.Price);
        Assert.Equal(ReplayWinningSellPrice, replay.Response.Price);

        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var product = await verifyContext.Products
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.ProductId);
        var persistedVariant = await verifyContext.ProductVariants
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.TargetVariantId);
        var allVariantOutboxMessages = await verifyContext.OutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.AggregateId == seeded.TargetVariantId)
            .OrderBy(message => message.CreatedAtUtc)
            .ThenBy(message => message.Id)
            .ToListAsync();
        var priceOutboxMessages = allVariantOutboxMessages
            .Where(message =>
                message.EventType == typeof(ProductVariantUpdatedEvent).FullName ||
                message.EventType == typeof(VariantPriceUpdatedEvent).FullName)
            .ToList();
        var idempotencyRecords = await verifyContext.ProductRequestIdempotencyRecords
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(record =>
                record.TenantId == seeded.TenantId &&
                record.ProductId == seeded.ProductId &&
                record.ProductVariantId == seeded.TargetVariantId &&
                record.Operation == "UPDATE_VARIANT")
            .OrderBy(record => record.CreatedAt)
            .ToListAsync();
        var processedEvents = await verifyContext.Set<ProcessedEvent>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .ToListAsync();
        var projectionOutboxMessages = await verifyContext.MarketplaceProjectionOutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .ToListAsync();
        var stockSyncMessages = await verifyContext.StockSyncOutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message => message.VariantId == seeded.TargetVariantId)
            .ToListAsync();

        Assert.Equal(seeded.ProductProjectionMutationVersion, product.ProjectionMutationVersion);
        Assert.Equal(ReplayWinningSellPrice, persistedVariant.Price);
        Assert.Equal(seeded.TargetVariantProjectionMutationVersion + 1, persistedVariant.ProjectionMutationVersion);
        Assert.Equal(TargetVariantSku, persistedVariant.SKU);
        Assert.Equal(TargetVariantBarcode, persistedVariant.Barcode);

        Assert.Equal(2, priceOutboxMessages.Count);
        Assert.Single(idempotencyRecords);
        Assert.Empty(stockSyncMessages);
        Assert.Empty(processedEvents);
        Assert.Empty(projectionOutboxMessages);

        var expectedEventVersion = ProductEventVersionHelper.ToProductEventVersion(
            seeded.TargetVariantProjectionMutationVersion + 1);
        Assert.All(priceOutboxMessages, message =>
        {
            Assert.Equal(expectedEventVersion, message.EventVersion);
            Assert.NotNull(message.ProcessedAtUtc);
            Assert.Equal(seeded.TargetVariantId, message.AggregateId);
            Assert.Equal(seeded.TenantId, message.TenantId);
        });

        Assert.Single(priceOutboxMessages.Where(message => message.EventType == typeof(ProductVariantUpdatedEvent).FullName));
        Assert.Single(priceOutboxMessages.Where(message => message.EventType == typeof(VariantPriceUpdatedEvent).FullName));

        var idempotencyRecord = idempotencyRecords.Single();
        Assert.Equal(BuildScopedProductMutationIdempotencyKey(seeded.TenantId, PriceCon02SharedRequestId), idempotencyRecord.Key);
        Assert.Equal(replayActor.RequestId, originalActor.RequestId);
        Assert.Equal(seeded.TargetVariantId, idempotencyRecord.ProductVariantId);
        Assert.Equal(StatusCodes.Status200OK, idempotencyRecord.ResponseStatusCode);
        Assert.NotNull(idempotencyRecord.CompletedAtUtc);
        Assert.Contains(seeded.TargetVariantId.ToString("D"), idempotencyRecord.ResponseJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            ReplayWinningSellPrice.ToString(System.Globalization.CultureInfo.InvariantCulture),
            idempotencyRecord.ResponseJson ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(2, publishedEvents.Count);
        var variantUpdatedEvent = Assert.IsType<ProductVariantUpdatedEvent>(
            publishedEvents.Single(message => message is ProductVariantUpdatedEvent));
        var priceUpdatedEvent = Assert.IsType<VariantPriceUpdatedEvent>(
            publishedEvents.Single(message => message is VariantPriceUpdatedEvent));

        Assert.Equal(expectedEventVersion, variantUpdatedEvent.EventVersion);
        Assert.Equal(expectedEventVersion, priceUpdatedEvent.EventVersion);
        Assert.Equal(seeded.TargetVariantId, variantUpdatedEvent.VariantId);
        Assert.Equal(seeded.TargetVariantId, priceUpdatedEvent.VariantId);
        Assert.Equal(ReplayWinningSellPrice, variantUpdatedEvent.Price);
        Assert.Equal(ReplayWinningSellPrice, priceUpdatedEvent.NewPrice);

        var productDocument = await mongoDatabase
            .GetCollection<ProductDocument>("products")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();
        var listingDocument = await mongoDatabase
            .GetCollection<CommerceHubProductDocument>("commercehub_products")
            .Find(document => document.TenantId == seeded.TenantId && document.Id == CommerceHubListingId)
            .FirstOrDefaultAsync();

        Assert.NotNull(productDocument);
        Assert.NotNull(listingDocument);

        var storefrontVariant = Assert.Single(
            productDocument!.Variants.Where(variant => variant.VariantId == seeded.TargetVariantId));
        Assert.Equal(ReplayWinningSellPrice, storefrontVariant.Price);
        Assert.Equal(expectedEventVersion, storefrontVariant.LastEventVersion);
        Assert.Equal(ReplayWinningSellPrice, listingDocument!.Price);
        Assert.Equal(expectedEventVersion, listingDocument.LastPriceEventVersion);
        Assert.Equal(priceUpdatedEvent.EventId, listingDocument.LastEventId);
        Assert.NotNull(listingDocument.LastPriceEventTimestamp);
        Assert.Equal(
            TruncateToMilliseconds(priceUpdatedEvent.OccurredAtUtc),
            TruncateToMilliseconds(listingDocument.LastPriceEventTimestamp.Value));
        Assert.True(listingDocument.IsPublished);

        progressEntries.Enqueue("verification:price-con-02-replay-converged");
    }

    private static async Task<PriceCon03CanonicalState> LoadPriceCon03CanonicalStateAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        PriceCon03ScenarioSeed seeded,
        string requestId)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var product = await verifyContext.Products
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.ProductId);
        var variant = await verifyContext.ProductVariants
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.DefaultVariantId);
        var outboxMessages = await verifyContext.OutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                (message.AggregateId == seeded.ProductId || message.AggregateId == seeded.DefaultVariantId))
            .OrderBy(message => message.CreatedAtUtc)
            .ThenBy(message => message.Id)
            .ToListAsync();
        var marketplaceOutboxMessages = await verifyContext.Set<MarketplaceEventOutboxMessage>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.EntityId == seeded.ProductId)
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .ToListAsync();
        var idempotencyRecords = await verifyContext.ProductRequestIdempotencyRecords
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(record =>
                record.TenantId == seeded.TenantId &&
                record.ProductId == seeded.ProductId &&
                record.ProductVariantId == seeded.DefaultVariantId &&
                record.Operation == "UPDATE_VARIANT")
            .OrderBy(record => record.CreatedAt)
            .ToListAsync();

        Assert.Equal(PriceCon03WinningSellPrice, product.SalePrice);
        Assert.Equal(PriceCon03WinningSellPrice, variant.Price);
        Assert.Equal(seeded.ProductProjectionMutationVersion + 1, product.ProjectionMutationVersion);
        Assert.Equal(seeded.DefaultVariantProjectionMutationVersion + 1, variant.ProjectionMutationVersion);

        Assert.Equal(3, outboxMessages.Count);
        Assert.Single(outboxMessages.Where(message => message.EventType == typeof(ProductUpdatedEvent).FullName));
        Assert.Single(outboxMessages.Where(message => message.EventType == typeof(ProductVariantUpdatedEvent).FullName));
        Assert.Single(outboxMessages.Where(message => message.EventType == typeof(VariantPriceUpdatedEvent).FullName));
        Assert.All(outboxMessages, message => Assert.Null(message.ProcessedAtUtc));

        Assert.Single(marketplaceOutboxMessages);
        Assert.Null(marketplaceOutboxMessages.Single().PublishedAtUtc);

        Assert.Single(idempotencyRecords);
        Assert.Equal(BuildScopedProductMutationIdempotencyKey(seeded.TenantId, requestId), idempotencyRecords.Single().Key);
        Assert.NotNull(idempotencyRecords.Single().CompletedAtUtc);

        return new PriceCon03CanonicalState(
            ProductProjectionMutationVersion: product.ProjectionMutationVersion,
            VariantProjectionMutationVersion: variant.ProjectionMutationVersion,
            ProductSalePrice: product.SalePrice,
            VariantSalePrice: variant.Price,
            IdempotencyKey: idempotencyRecords.Single().Key);
    }

    private static async Task<PriceCanonicalState> LoadCommittedPriceMutationStateAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        PriceCon03ScenarioSeed seeded,
        string requestId,
        decimal expectedPrice)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var product = await verifyContext.Products
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.ProductId);
        var variant = await verifyContext.ProductVariants
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.DefaultVariantId);
        var idempotencyRecord = await verifyContext.ProductRequestIdempotencyRecords
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleAsync(record =>
                record.TenantId == seeded.TenantId &&
                record.ProductId == seeded.ProductId &&
                record.ProductVariantId == seeded.DefaultVariantId &&
                record.Operation == "UPDATE_VARIANT" &&
                record.Key == BuildScopedProductMutationIdempotencyKey(seeded.TenantId, requestId));

        Assert.Equal(expectedPrice, product.SalePrice);
        Assert.Equal(expectedPrice, variant.Price);
        Assert.NotNull(idempotencyRecord.CompletedAtUtc);

        return new PriceCanonicalState(
            ProductProjectionMutationVersion: product.ProjectionMutationVersion,
            VariantProjectionMutationVersion: variant.ProjectionMutationVersion,
            ProductSalePrice: product.SalePrice,
            VariantSalePrice: variant.Price,
            IdempotencyKey: idempotencyRecord.Key);
    }

    private static async Task<PriceCon03MarketplaceOutboxDispatchResult> DispatchPriceCon03MarketplaceOutboxAsync(
        string connectionString,
        TestTenantContext tenantContext,
        ConcurrentQueue<string> progressEntries)
    {
        MarketplaceEvents.ProductUpdatedEvent? publishedEvent = null;
        Type? publishedType = null;
        var publishEndpoint = new Mock<IPublishEndpoint>(MockBehavior.Strict);
        publishEndpoint
            .Setup(endpoint => endpoint.Publish(
                It.IsAny<object>(),
                It.IsAny<Type>(),
                It.IsAny<IPipe<PublishContext>>(),
                It.IsAny<CancellationToken>()))
            .Callback<object, Type, IPipe<PublishContext>, CancellationToken>((message, type, _, _) =>
            {
                publishedEvent = Assert.IsType<MarketplaceEvents.ProductUpdatedEvent>(message);
                publishedType = type;
            })
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<ITenantContext>(_ => tenantContext);
        services.AddScoped<IPublishEndpoint>(_ => publishEndpoint.Object);
        services.AddSingleton(Options.Create(new MarketplaceEventBackboneOptions()));

        await using var serviceProvider = services.BuildServiceProvider();
        var processor = new MarketplaceEventOutboxProcessor(
            serviceProvider,
            NullLogger<MarketplaceEventOutboxProcessor>.Instance,
            Options.Create(new MarketplaceEventBackboneOptions()));

        var processedCount = await processor.ProcessPendingMessagesAsync();
        progressEntries.Enqueue($"price-con-03:marketplace-outbox-processed:{processedCount}");

        return new PriceCon03MarketplaceOutboxDispatchResult(
            ProcessedCount: processedCount,
            PublishedEvent: publishedEvent,
            PublishedType: publishedType);
    }

    private static async Task<PriceCon03MarketplaceWorkerResult> ExecutePriceCon03MarketplaceWorkerAsync(
        string connectionString,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        MarketplaceEvents.ProductUpdatedEvent publishedEvent,
        PriceCon03MarketplaceWorker worker,
        ConcurrentQueue<string> progressEntries,
        params IInterceptor[] interceptors)
    {
        await using var context = CreateDbContext(connectionString, tenantContext, interceptors);
        await ConfigureSessionTimeoutsAsync(context);

        var projectionServices = new ServiceCollection();
        projectionServices.AddLogging();
        projectionServices.AddOptions();
        projectionServices.Configure<MarketplaceEventBackboneOptions>(_ => { });
        projectionServices.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
        projectionServices.AddSingleton<IMongoDatabase>(mongoDatabase);
        projectionServices.AddScoped<IMarketplaceProductReadRepository, MarketplaceProductReadRepository>();
        projectionServices.AddScoped<IMarketplaceProductReadModelProjectionService, MarketplaceProductReadModelProjectionService>();
        projectionServices.AddScoped<ProductReadModelConsumer>();
        projectionServices.AddScoped<IMarketplaceEventPipeline, ProductOnlyMarketplaceEventPipeline>();
        projectionServices.AddScoped<MarketplaceProjectionOutboxProcessor>();

        await using var projectionProvider = projectionServices.BuildServiceProvider();
        var projectionOutboxProcessor = projectionProvider.GetRequiredService<MarketplaceProjectionOutboxProcessor>();

        var productReadRepository = new MarketplaceProductReadRepository(mongoDatabase);
        var productProjectionService = new MarketplaceProductReadModelProjectionService(
            context,
            productReadRepository,
            NullLogger<MarketplaceProductReadModelProjectionService>.Instance);
        var productReadModelConsumer = new ProductReadModelConsumer(
            productProjectionService,
            NullLogger<ProductReadModelConsumer>.Instance);
        var pipeline = new ProductOnlyMarketplaceEventPipeline(productReadModelConsumer);
        var consumer = new MarketplaceProductUpdatedConsumer(
            context,
            pipeline,
            new NoOpEventLagTracker(),
            projectionOutboxProcessor,
            NullLogger<MarketplaceProductUpdatedConsumer>.Instance);

        progressEntries.Enqueue($"{worker.Name}:consume-started");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await consumer.Consume(BuildConsumeContext(publishedEvent).Object);
            stopwatch.Stop();
            progressEntries.Enqueue($"{worker.Name}:consume-completed");
            return new PriceCon03MarketplaceWorkerResult(worker.Name, stopwatch.Elapsed, null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            progressEntries.Enqueue($"{worker.Name}:threw:{DescribeException(ex)}");
            return new PriceCon03MarketplaceWorkerResult(worker.Name, stopwatch.Elapsed, ex);
        }
    }

    private static async Task<CommittedPriceMutationBundle> CaptureCommittedPriceMutationBundleAsync(
        TestDatabaseScope scope,
        string connectionString,
        TestTenantContext tenantContext,
        PriceCon03ScenarioSeed seeded,
        PriceActor actor,
        PriceActorResult mutationResult,
        ConcurrentQueue<string> progressEntries)
    {
        Assert.Null(mutationResult.Exception);
        Assert.True(mutationResult.Succeeded, mutationResult.PayloadJson ?? $"{actor.Name} did not commit the authoritative price mutation.");
        Assert.NotNull(mutationResult.Response);
        Assert.Equal(StatusCodes.Status200OK, mutationResult.StatusCode);
        Assert.Equal(seeded.DefaultVariantId, mutationResult.Response!.Id);
        Assert.Equal(actor.RequestedSellPrice, mutationResult.Response.Price);

        var canonicalState = await LoadCommittedPriceMutationStateAsync(
            scope,
            tenantContext,
            seeded,
            actor.RequestId,
            actor.RequestedSellPrice);

        var standardPublishedEvents = await DispatchCommittedOutboxAsync(
            connectionString,
            tenantContext,
            progressEntries);
        Assert.Equal(3, standardPublishedEvents.Count);

        var productUpdatedEvent = Assert.IsType<ProductUpdatedEvent>(
            standardPublishedEvents.Single(@event => @event is ProductUpdatedEvent));
        var variantUpdatedEvent = Assert.IsType<ProductVariantUpdatedEvent>(
            standardPublishedEvents.Single(@event => @event is ProductVariantUpdatedEvent));
        var priceUpdatedEvent = Assert.IsType<VariantPriceUpdatedEvent>(
            standardPublishedEvents.Single(@event => @event is VariantPriceUpdatedEvent));

        var expectedProductEventVersion = ProductEventVersionHelper.ToProductEventVersion(
            canonicalState.ProductProjectionMutationVersion);
        var expectedVariantEventVersion = ProductEventVersionHelper.ToProductEventVersion(
            canonicalState.VariantProjectionMutationVersion);

        Assert.Equal(expectedProductEventVersion, productUpdatedEvent.EventVersion);
        Assert.Equal(expectedVariantEventVersion, variantUpdatedEvent.EventVersion);
        Assert.Equal(expectedVariantEventVersion, priceUpdatedEvent.EventVersion);
        Assert.Equal(canonicalState.ProductSalePrice, productUpdatedEvent.SalePrice);
        Assert.Equal(canonicalState.VariantSalePrice, variantUpdatedEvent.Price);
        Assert.Equal(canonicalState.VariantSalePrice, priceUpdatedEvent.NewPrice);

        var marketplaceDispatch = await DispatchPriceCon03MarketplaceOutboxAsync(
            connectionString,
            tenantContext,
            progressEntries);
        Assert.Equal(1, marketplaceDispatch.ProcessedCount);
        Assert.NotNull(marketplaceDispatch.PublishedEvent);

        var marketplaceEvent = marketplaceDispatch.PublishedEvent!;
        Assert.Equal(expectedProductEventVersion, marketplaceEvent.EventVersion);
        Assert.Equal(canonicalState.ProductSalePrice, marketplaceEvent.SalePrice);

        return new CommittedPriceMutationBundle(
            Actor: actor,
            MutationResult: mutationResult,
            CanonicalState: canonicalState,
            StandardPublishedEvents: standardPublishedEvents,
            ProductUpdatedEvent: productUpdatedEvent,
            VariantUpdatedEvent: variantUpdatedEvent,
            PriceUpdatedEvent: priceUpdatedEvent,
            MarketplaceEvent: marketplaceEvent);
    }

    private static async Task AssertPriceCon03NoUncommittedVisibilityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        PriceCon03ScenarioSeed seeded,
        PriceCon03CanonicalState canonicalState,
        MarketplaceEvents.ProductUpdatedEvent publishedEvent,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var product = await verifyContext.Products
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.ProductId);
        var variant = await verifyContext.ProductVariants
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.DefaultVariantId);
        var regularOutboxMessages = await verifyContext.OutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                (message.AggregateId == seeded.ProductId || message.AggregateId == seeded.DefaultVariantId))
            .ToListAsync();
        var marketplaceOutboxMessages = await verifyContext.Set<MarketplaceEventOutboxMessage>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.EntityId == seeded.ProductId)
            .ToListAsync();
        var processedEvents = await verifyContext.Set<ProcessedEvent>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(processed =>
                processed.EventId == publishedEvent.EventId &&
                processed.ConsumerName == nameof(MarketplaceProductUpdatedConsumer))
            .ToListAsync();
        var projectionOutboxMessages = await verifyContext.Set<MarketplaceProjectionOutboxMessage>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.EventId == publishedEvent.EventId &&
                message.ConsumerName == nameof(MarketplaceProductUpdatedConsumer))
            .ToListAsync();

        var productDocument = await mongoDatabase.GetCollection<ProductDocument>("products")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();
        var listingDocument = await mongoDatabase.GetCollection<CommerceHubProductDocument>("commercehub_products")
            .Find(document => document.TenantId == seeded.TenantId && document.Id == CommerceHubListingId)
            .FirstOrDefaultAsync();
        var affiliateDocument = await mongoDatabase.GetCollection<AffiliateProductIntelligenceDocument>("affiliate_product_intelligence")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();
        var marketplaceReadModelCount = await mongoDatabase
            .GetCollection<MarketplaceProductReadModelDocument>(MarketplaceProductReadModelDocument.CollectionName)
            .CountDocumentsAsync(document =>
                document.TenantId == seeded.TenantId &&
                document.ProductId == seeded.ProductId &&
                document.LastEventId == publishedEvent.EventId);

        Assert.Equal(canonicalState.ProductSalePrice, product.SalePrice);
        Assert.Equal(canonicalState.VariantSalePrice, variant.Price);
        Assert.Equal(canonicalState.ProductProjectionMutationVersion, product.ProjectionMutationVersion);
        Assert.Equal(canonicalState.VariantProjectionMutationVersion, variant.ProjectionMutationVersion);

        Assert.Equal(3, regularOutboxMessages.Count);
        Assert.All(regularOutboxMessages, message => Assert.Null(message.ProcessedAtUtc));
        Assert.Single(marketplaceOutboxMessages);
        Assert.NotNull(marketplaceOutboxMessages.Single().PublishedAtUtc);
        Assert.Empty(processedEvents);
        Assert.Empty(projectionOutboxMessages);
        Assert.Equal(0, marketplaceReadModelCount);

        Assert.NotNull(productDocument);
        Assert.NotNull(listingDocument);
        Assert.NotNull(affiliateDocument);

        Assert.Equal(InitialSellPrice, productDocument!.Price);
        var storefrontVariant = Assert.Single(productDocument.Variants.Where(variantDoc => variantDoc.VariantId == seeded.DefaultVariantId));
        Assert.Equal(InitialSellPrice, storefrontVariant.Price);
        Assert.Equal(InitialSellPrice, listingDocument!.Price);
        Assert.Equal(InitialSellPrice, affiliateDocument!.BasePrice);
        Assert.DoesNotContain(publishedEvent.EventId.ToString(), affiliateDocument.ProcessedEventIds);

        progressEntries.Enqueue("verification:price-con-03-active-processing-remained-invisible");
    }

    private static async Task AssertPriceCon03ConvergenceAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        PriceCon03ScenarioSeed seeded,
        PriceCon03CanonicalState canonicalState,
        MarketplaceEvents.ProductUpdatedEvent marketplacePublishedEvent,
        IReadOnlyCollection<PriceCon03MarketplaceWorkerResult> overlapResults,
        IReadOnlyList<IntegrationEvent> publishedEvents,
        ConcurrentQueue<string> progressEntries)
    {
        var productUpdatedEvent = Assert.IsType<ProductUpdatedEvent>(
            publishedEvents.Single(@event => @event is ProductUpdatedEvent));
        var variantUpdatedEvent = Assert.IsType<ProductVariantUpdatedEvent>(
            publishedEvents.Single(@event => @event is ProductVariantUpdatedEvent));
        var priceUpdatedEvent = Assert.IsType<VariantPriceUpdatedEvent>(
            publishedEvents.Single(@event => @event is VariantPriceUpdatedEvent));

        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var product = await verifyContext.Products
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.ProductId);
        var variant = await verifyContext.ProductVariants
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.DefaultVariantId);
        var outboxMessages = await verifyContext.OutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                (message.AggregateId == seeded.ProductId || message.AggregateId == seeded.DefaultVariantId))
            .OrderBy(message => message.CreatedAtUtc)
            .ThenBy(message => message.Id)
            .ToListAsync();
        var marketplaceOutboxMessages = await verifyContext.Set<MarketplaceEventOutboxMessage>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.EntityId == seeded.ProductId)
            .ToListAsync();
        var processedEvents = await verifyContext.Set<ProcessedEvent>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(processed =>
                processed.EventId == marketplacePublishedEvent.EventId &&
                processed.ConsumerName == nameof(MarketplaceProductUpdatedConsumer))
            .OrderBy(processed => processed.CreatedAt)
            .ToListAsync();
        var projectionOutboxMessages = await verifyContext.Set<MarketplaceProjectionOutboxMessage>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.EventId == marketplacePublishedEvent.EventId &&
                message.ConsumerName == nameof(MarketplaceProductUpdatedConsumer))
            .ToListAsync();
        var idempotencyRecords = await verifyContext.ProductRequestIdempotencyRecords
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(record =>
                record.TenantId == seeded.TenantId &&
                record.ProductId == seeded.ProductId &&
                record.ProductVariantId == seeded.DefaultVariantId &&
                record.Operation == "UPDATE_VARIANT")
            .ToListAsync();

        var productDocument = await mongoDatabase.GetCollection<ProductDocument>("products")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();
        var listingDocument = await mongoDatabase.GetCollection<CommerceHubProductDocument>("commercehub_products")
            .Find(document => document.TenantId == seeded.TenantId && document.Id == CommerceHubListingId)
            .FirstOrDefaultAsync();
        var affiliateDocument = await mongoDatabase.GetCollection<AffiliateProductIntelligenceDocument>("affiliate_product_intelligence")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();
        var marketplaceReadModelDocuments = await mongoDatabase
            .GetCollection<MarketplaceProductReadModelDocument>(MarketplaceProductReadModelDocument.CollectionName)
            .Find(document =>
                document.TenantId == seeded.TenantId &&
                document.ProductId == seeded.ProductId &&
                document.LastEventId == marketplacePublishedEvent.EventId)
            .ToListAsync();

        Assert.Equal(canonicalState.ProductSalePrice, product.SalePrice);
        Assert.Equal(canonicalState.VariantSalePrice, variant.Price);
        Assert.Equal(canonicalState.ProductProjectionMutationVersion, product.ProjectionMutationVersion);
        Assert.Equal(canonicalState.VariantProjectionMutationVersion, variant.ProjectionMutationVersion);

        Assert.Equal(3, outboxMessages.Count);
        Assert.All(outboxMessages, message => Assert.NotNull(message.ProcessedAtUtc));
        Assert.Single(outboxMessages.Where(message => message.EventType == typeof(ProductUpdatedEvent).FullName));
        Assert.Single(outboxMessages.Where(message => message.EventType == typeof(ProductVariantUpdatedEvent).FullName));
        Assert.Single(outboxMessages.Where(message => message.EventType == typeof(VariantPriceUpdatedEvent).FullName));

        Assert.Single(marketplaceOutboxMessages);
        Assert.NotNull(marketplaceOutboxMessages.Single().PublishedAtUtc);
        Assert.Equal(marketplacePublishedEvent.EventId, marketplaceOutboxMessages.Single().EventId);
        Assert.Equal(marketplacePublishedEvent.Version, marketplaceOutboxMessages.Single().Version);

        Assert.Single(processedEvents);
        Assert.Empty(projectionOutboxMessages);
        Assert.Single(idempotencyRecords);
        Assert.Equal(canonicalState.IdempotencyKey, idempotencyRecords.Single().Key);

        Assert.NotNull(productDocument);
        Assert.NotNull(listingDocument);
        Assert.NotNull(affiliateDocument);
        Assert.Single(marketplaceReadModelDocuments);

        Assert.Equal(canonicalState.ProductSalePrice, productDocument!.Price);
        var storefrontVariant = Assert.Single(productDocument.Variants.Where(variantDoc => variantDoc.VariantId == seeded.DefaultVariantId));
        Assert.Equal(canonicalState.VariantSalePrice, storefrontVariant.Price);
        Assert.Equal(variantUpdatedEvent.EventVersion, storefrontVariant.LastEventVersion);

        Assert.Equal(canonicalState.VariantSalePrice, listingDocument!.Price);
        Assert.Equal(priceUpdatedEvent.EventId, listingDocument.LastEventId);
        Assert.Equal(priceUpdatedEvent.EventVersion, listingDocument.LastPriceEventVersion);
        Assert.NotNull(listingDocument.LastPriceEventTimestamp);
        Assert.Equal(
            TruncateToMilliseconds(priceUpdatedEvent.OccurredAtUtc),
            TruncateToMilliseconds(listingDocument.LastPriceEventTimestamp.Value));

        Assert.Equal(canonicalState.ProductSalePrice, affiliateDocument!.BasePrice);
        Assert.Equal(productUpdatedEvent.EventVersion, affiliateDocument.LastProductEventVersion);
        Assert.Equal(
            1,
            affiliateDocument.ProcessedEventIds.Count(value => value == productUpdatedEvent.EventId.ToString()));

        var marketplaceReadModel = marketplaceReadModelDocuments.Single();
        Assert.Equal(marketplacePublishedEvent.EventId, marketplaceReadModel.LastEventId);
        Assert.Equal(marketplacePublishedEvent.Version, marketplaceReadModel.LastEventVersion);

        Assert.All(overlapResults, result => Assert.Null(result.Exception));
        Assert.Equal(2, overlapResults.Count);
        Assert.True(overlapResults.Any(result => result.WorkerName == "price-con-03-marketplace-worker-a"));
        Assert.True(overlapResults.Any(result => result.WorkerName == "price-con-03-marketplace-worker-b"));

        progressEntries.Enqueue("verification:price-con-03-converged");
    }

    private static async Task AssertPriceCon06NoUncommittedVisibilityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        PriceCon03ScenarioSeed seeded,
        CommittedPriceMutationBundle committedBundle,
        MarketplaceEvents.ProductUpdatedEvent publishedEvent,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var product = await verifyContext.Products
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.ProductId);
        var variant = await verifyContext.ProductVariants
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.DefaultVariantId);
        var regularOutboxMessages = await verifyContext.OutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                (message.AggregateId == seeded.ProductId || message.AggregateId == seeded.DefaultVariantId))
            .ToListAsync();
        var marketplaceOutboxMessages = await verifyContext.Set<MarketplaceEventOutboxMessage>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.EntityId == seeded.ProductId)
            .ToListAsync();
        var processedEvents = await verifyContext.Set<ProcessedEvent>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(processed =>
                processed.EventId == publishedEvent.EventId &&
                processed.ConsumerName == nameof(MarketplaceProductUpdatedConsumer))
            .ToListAsync();
        var projectionOutboxMessages = await verifyContext.Set<MarketplaceProjectionOutboxMessage>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.EventId == publishedEvent.EventId &&
                message.ConsumerName == nameof(MarketplaceProductUpdatedConsumer))
            .ToListAsync();

        var productDocument = await mongoDatabase.GetCollection<ProductDocument>("products")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();
        var listingDocument = await mongoDatabase.GetCollection<CommerceHubProductDocument>("commercehub_products")
            .Find(document => document.TenantId == seeded.TenantId && document.Id == CommerceHubListingId)
            .FirstOrDefaultAsync();
        var affiliateDocument = await mongoDatabase.GetCollection<AffiliateProductIntelligenceDocument>("affiliate_product_intelligence")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();
        var marketplaceReadModelCount = await mongoDatabase
            .GetCollection<MarketplaceProductReadModelDocument>(MarketplaceProductReadModelDocument.CollectionName)
            .CountDocumentsAsync(document =>
                document.TenantId == seeded.TenantId &&
                document.ProductId == seeded.ProductId &&
                document.LastEventId == publishedEvent.EventId);

        Assert.Equal(committedBundle.CanonicalState.ProductSalePrice, product.SalePrice);
        Assert.Equal(committedBundle.CanonicalState.VariantSalePrice, variant.Price);
        Assert.Equal(committedBundle.CanonicalState.ProductProjectionMutationVersion, product.ProjectionMutationVersion);
        Assert.Equal(committedBundle.CanonicalState.VariantProjectionMutationVersion, variant.ProjectionMutationVersion);

        Assert.Equal(3, regularOutboxMessages.Count);
        Assert.All(regularOutboxMessages, message => Assert.NotNull(message.ProcessedAtUtc));
        Assert.Single(marketplaceOutboxMessages);
        Assert.NotNull(marketplaceOutboxMessages.Single().PublishedAtUtc);
        Assert.Empty(processedEvents);
        Assert.Empty(projectionOutboxMessages);
        Assert.Equal(0, marketplaceReadModelCount);

        Assert.NotNull(productDocument);
        Assert.NotNull(listingDocument);
        Assert.NotNull(affiliateDocument);

        Assert.Equal(InitialSellPrice, productDocument!.Price);
        var storefrontVariant = Assert.Single(productDocument.Variants.Where(variantDoc => variantDoc.VariantId == seeded.DefaultVariantId));
        Assert.Equal(InitialSellPrice, storefrontVariant.Price);
        Assert.Equal(InitialSellPrice, listingDocument!.Price);
        Assert.Equal(InitialSellPrice, affiliateDocument!.BasePrice);
        Assert.DoesNotContain(publishedEvent.EventId.ToString(), affiliateDocument.ProcessedEventIds);

        progressEntries.Enqueue("verification:price-con-06-active-marketplace-processing-remained-invisible");
    }

    private static async Task<PriceCon04VisibilitySnapshot> CapturePriceCon04VisibilitySnapshotAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        PriceCon03ScenarioSeed seeded,
        Guid olderProductEventId,
        Guid newerProductEventId)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var product = await verifyContext.Products
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.ProductId);
        var variant = await verifyContext.ProductVariants
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.DefaultVariantId);
        var processedEvents = await verifyContext.Set<ProcessedEvent>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(processed =>
                processed.ConsumerName == nameof(MarketplaceProductUpdatedConsumer) &&
                processed.EntityId == seeded.ProductId &&
                processed.TenantId == seeded.TenantId)
            .ToListAsync();
        var projectionOutboxCount = await verifyContext.Set<MarketplaceProjectionOutboxMessage>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(message =>
                message.ConsumerName == nameof(MarketplaceProductUpdatedConsumer) &&
                message.EntityId == seeded.ProductId &&
                message.TenantId == seeded.TenantId);

        var productDocument = await mongoDatabase
            .GetCollection<ProductDocument>("products")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();
        var listingDocument = await mongoDatabase
            .GetCollection<CommerceHubProductDocument>("commercehub_products")
            .Find(document => document.TenantId == seeded.TenantId && document.Id == CommerceHubListingId)
            .FirstOrDefaultAsync();
        var affiliateDocument = await mongoDatabase
            .GetCollection<AffiliateProductIntelligenceDocument>("affiliate_product_intelligence")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();
        var marketplaceReadModelDocuments = await mongoDatabase
            .GetCollection<MarketplaceProductReadModelDocument>(MarketplaceProductReadModelDocument.CollectionName)
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .ToListAsync();

        Assert.NotNull(productDocument);
        Assert.NotNull(listingDocument);
        Assert.NotNull(affiliateDocument);
        Assert.Single(marketplaceReadModelDocuments);

        var storefrontVariant = Assert.Single(
            productDocument!.Variants.Where(variantDoc => variantDoc.VariantId == seeded.DefaultVariantId));
        var marketplaceReadModel = marketplaceReadModelDocuments.Single();

        return new PriceCon04VisibilitySnapshot(
            AuthoritativeProductPrice: product.SalePrice,
            AuthoritativeVariantPrice: variant.Price,
            AuthoritativeProductProjectionMutationVersion: product.ProjectionMutationVersion,
            AuthoritativeVariantProjectionMutationVersion: variant.ProjectionMutationVersion,
            StorefrontProductPrice: productDocument.Price,
            StorefrontVariantPrice: storefrontVariant.Price,
            StorefrontVariantEventVersion: storefrontVariant.LastEventVersion,
            CommerceHubPrice: listingDocument!.Price,
            CommerceHubLastEventId: listingDocument.LastEventId,
            CommerceHubLastPriceEventVersion: listingDocument.LastPriceEventVersion,
            AffiliateBasePrice: affiliateDocument!.BasePrice,
            AffiliateLastProductEventVersion: affiliateDocument.LastProductEventVersion,
            AffiliateContainsOlderProductEventId: affiliateDocument.ProcessedEventIds.Contains(olderProductEventId.ToString()),
            AffiliateContainsNewerProductEventId: affiliateDocument.ProcessedEventIds.Contains(newerProductEventId.ToString()),
            MarketplaceReadModelPrice: marketplaceReadModel.Price,
            MarketplaceReadModelLastEventId: marketplaceReadModel.LastEventId,
            MarketplaceReadModelLastEventVersion: marketplaceReadModel.LastEventVersion,
            MarketplaceReadModelLastEntityMutationVersion: marketplaceReadModel.LastEntityMutationVersion,
            MarketplaceProcessedEventCount: processedEvents.Count,
            MarketplaceProjectionOutboxCount: projectionOutboxCount,
            MarketplaceReadModelDocumentCount: marketplaceReadModelDocuments.Count);
    }

    private static async Task<PriceCon05VisibilitySnapshot> CapturePriceCon05VisibilitySnapshotAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        PriceCon03ScenarioSeed seeded,
        Guid olderProductEventId,
        Guid newerProductEventId)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var product = await verifyContext.Products
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.ProductId);
        var variant = await verifyContext.ProductVariants
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.DefaultVariantId);
        var processedEvents = await verifyContext.Set<ProcessedEvent>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(processed =>
                processed.ConsumerName == nameof(MarketplaceProductUpdatedConsumer) &&
                processed.EntityId == seeded.ProductId &&
                processed.TenantId == seeded.TenantId)
            .ToListAsync();
        var projectionOutboxCount = await verifyContext.Set<MarketplaceProjectionOutboxMessage>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(message =>
                message.ConsumerName == nameof(MarketplaceProductUpdatedConsumer) &&
                message.EntityId == seeded.ProductId &&
                message.TenantId == seeded.TenantId);

        var productDocument = await mongoDatabase
            .GetCollection<ProductDocument>("products")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();
        var listingDocument = await mongoDatabase
            .GetCollection<CommerceHubProductDocument>("commercehub_products")
            .Find(document => document.TenantId == seeded.TenantId && document.Id == CommerceHubListingId)
            .FirstOrDefaultAsync();
        var affiliateDocuments = await mongoDatabase
            .GetCollection<AffiliateProductIntelligenceDocument>("affiliate_product_intelligence")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .ToListAsync();
        var marketplaceReadModelDocuments = await mongoDatabase
            .GetCollection<MarketplaceProductReadModelDocument>(MarketplaceProductReadModelDocument.CollectionName)
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .ToListAsync();

        Assert.NotNull(productDocument);
        Assert.NotNull(listingDocument);
        Assert.Single(affiliateDocuments);
        Assert.Single(marketplaceReadModelDocuments);

        var affiliateDocument = affiliateDocuments.Single();
        var storefrontVariant = Assert.Single(
            productDocument!.Variants.Where(variantDoc => variantDoc.VariantId == seeded.DefaultVariantId));
        var marketplaceReadModel = marketplaceReadModelDocuments.Single();

        return new PriceCon05VisibilitySnapshot(
            AuthoritativeProductPrice: product.SalePrice,
            AuthoritativeVariantPrice: variant.Price,
            AuthoritativeProductProjectionMutationVersion: product.ProjectionMutationVersion,
            AuthoritativeVariantProjectionMutationVersion: variant.ProjectionMutationVersion,
            StorefrontProductPrice: productDocument.Price,
            StorefrontVariantPrice: storefrontVariant.Price,
            StorefrontVariantEventVersion: storefrontVariant.LastEventVersion,
            CommerceHubPrice: listingDocument!.Price,
            CommerceHubLastEventId: listingDocument.LastEventId,
            CommerceHubLastPriceEventVersion: listingDocument.LastPriceEventVersion,
            AffiliateBasePrice: affiliateDocument.BasePrice,
            AffiliateLastProductEventVersion: affiliateDocument.LastProductEventVersion,
            AffiliateLastUpdatedUtc: affiliateDocument.LastUpdatedUtc,
            AffiliateProcessedEventIdCount: affiliateDocument.ProcessedEventIds.Count,
            AffiliateContainsOlderProductEventId: affiliateDocument.ProcessedEventIds.Contains(olderProductEventId.ToString()),
            AffiliateContainsNewerProductEventId: affiliateDocument.ProcessedEventIds.Contains(newerProductEventId.ToString()),
            AffiliateNewerProductEventIdOccurrenceCount: affiliateDocument.ProcessedEventIds.Count(value => value == newerProductEventId.ToString()),
            AffiliateDocumentCount: affiliateDocuments.Count,
            MarketplaceReadModelPrice: marketplaceReadModel.Price,
            MarketplaceReadModelLastEventId: marketplaceReadModel.LastEventId,
            MarketplaceReadModelLastEventVersion: marketplaceReadModel.LastEventVersion,
            MarketplaceReadModelLastEntityMutationVersion: marketplaceReadModel.LastEntityMutationVersion,
            MarketplaceProcessedEventCount: processedEvents.Count,
            MarketplaceProjectionOutboxCount: projectionOutboxCount,
            MarketplaceReadModelDocumentCount: marketplaceReadModelDocuments.Count);
    }

    private static async Task AssertPriceCon04DurableStateAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        PriceCon03ScenarioSeed seeded,
        CommittedPriceMutationBundle olderBundle,
        CommittedPriceMutationBundle newerBundle,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var product = await verifyContext.Products
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.ProductId);
        var variant = await verifyContext.ProductVariants
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.DefaultVariantId);
        var outboxMessages = await verifyContext.OutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                (message.AggregateId == seeded.ProductId || message.AggregateId == seeded.DefaultVariantId))
            .OrderBy(message => message.CreatedAtUtc)
            .ThenBy(message => message.Id)
            .ToListAsync();
        var marketplaceOutboxMessages = await verifyContext.Set<MarketplaceEventOutboxMessage>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.EntityId == seeded.ProductId)
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .ToListAsync();
        var processedEvents = await verifyContext.Set<ProcessedEvent>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(processed =>
                processed.ConsumerName == nameof(MarketplaceProductUpdatedConsumer) &&
                processed.EntityId == seeded.ProductId &&
                processed.TenantId == seeded.TenantId)
            .OrderBy(processed => processed.CreatedAt)
            .ToListAsync();
        var projectionOutboxMessages = await verifyContext.Set<MarketplaceProjectionOutboxMessage>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.ConsumerName == nameof(MarketplaceProductUpdatedConsumer) &&
                message.EntityId == seeded.ProductId &&
                message.TenantId == seeded.TenantId)
            .ToListAsync();
        var idempotencyRecords = await verifyContext.ProductRequestIdempotencyRecords
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(record =>
                record.TenantId == seeded.TenantId &&
                record.ProductId == seeded.ProductId &&
                record.ProductVariantId == seeded.DefaultVariantId &&
                record.Operation == "UPDATE_VARIANT")
            .OrderBy(record => record.CreatedAt)
            .ToListAsync();

        var productDocument = await mongoDatabase
            .GetCollection<ProductDocument>("products")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();
        var listingDocument = await mongoDatabase
            .GetCollection<CommerceHubProductDocument>("commercehub_products")
            .Find(document => document.TenantId == seeded.TenantId && document.Id == CommerceHubListingId)
            .FirstOrDefaultAsync();
        var affiliateDocument = await mongoDatabase
            .GetCollection<AffiliateProductIntelligenceDocument>("affiliate_product_intelligence")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();
        var marketplaceReadModelDocuments = await mongoDatabase
            .GetCollection<MarketplaceProductReadModelDocument>(MarketplaceProductReadModelDocument.CollectionName)
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .ToListAsync();

        Assert.Equal(newerBundle.CanonicalState.ProductSalePrice, product.SalePrice);
        Assert.Equal(newerBundle.CanonicalState.VariantSalePrice, variant.Price);
        Assert.Equal(newerBundle.CanonicalState.ProductProjectionMutationVersion, product.ProjectionMutationVersion);
        Assert.Equal(newerBundle.CanonicalState.VariantProjectionMutationVersion, variant.ProjectionMutationVersion);

        Assert.Equal(6, outboxMessages.Count);
        Assert.All(outboxMessages, message => Assert.NotNull(message.ProcessedAtUtc));
        Assert.Equal(2, outboxMessages.Count(message => message.EventType == typeof(ProductUpdatedEvent).FullName));
        Assert.Equal(2, outboxMessages.Count(message => message.EventType == typeof(ProductVariantUpdatedEvent).FullName));
        Assert.Equal(2, outboxMessages.Count(message => message.EventType == typeof(VariantPriceUpdatedEvent).FullName));

        Assert.Equal(2, marketplaceOutboxMessages.Count);
        Assert.All(marketplaceOutboxMessages, message => Assert.NotNull(message.PublishedAtUtc));
        Assert.Equal(
            1,
            marketplaceOutboxMessages.Count(message => message.EventId == olderBundle.MarketplaceEvent.EventId));
        Assert.Equal(
            1,
            marketplaceOutboxMessages.Count(message => message.EventId == newerBundle.MarketplaceEvent.EventId));
        Assert.Equal(2, processedEvents.Count);
        Assert.Equal(
            1,
            processedEvents.Count(processed => processed.EventId == olderBundle.MarketplaceEvent.EventId));
        Assert.Equal(
            1,
            processedEvents.Count(processed => processed.EventId == newerBundle.MarketplaceEvent.EventId));
        Assert.Empty(projectionOutboxMessages);

        Assert.Equal(2, idempotencyRecords.Count);
        Assert.Contains(idempotencyRecords, record => record.Key == olderBundle.CanonicalState.IdempotencyKey);
        Assert.Contains(idempotencyRecords, record => record.Key == newerBundle.CanonicalState.IdempotencyKey);

        Assert.NotNull(productDocument);
        Assert.NotNull(listingDocument);
        Assert.NotNull(affiliateDocument);
        Assert.Single(marketplaceReadModelDocuments);

        var storefrontVariant = Assert.Single(
            productDocument!.Variants.Where(variantDoc => variantDoc.VariantId == seeded.DefaultVariantId));
        Assert.Equal(newerBundle.CanonicalState.ProductSalePrice, productDocument.Price);
        Assert.Equal(newerBundle.CanonicalState.VariantSalePrice, storefrontVariant.Price);
        Assert.Equal(newerBundle.VariantUpdatedEvent.EventVersion, storefrontVariant.LastEventVersion);

        Assert.Equal(newerBundle.CanonicalState.VariantSalePrice, listingDocument!.Price);
        Assert.Equal(newerBundle.PriceUpdatedEvent.EventVersion, listingDocument.LastPriceEventVersion);
        Assert.Equal(newerBundle.PriceUpdatedEvent.EventId, listingDocument.LastEventId);
        Assert.NotEqual(olderBundle.PriceUpdatedEvent.EventId, listingDocument.LastEventId);

        Assert.Equal(newerBundle.CanonicalState.ProductSalePrice, affiliateDocument!.BasePrice);
        Assert.Equal(newerBundle.ProductUpdatedEvent.EventVersion, affiliateDocument.LastProductEventVersion);
        Assert.Contains(newerBundle.ProductUpdatedEvent.EventId.ToString(), affiliateDocument.ProcessedEventIds);
        Assert.DoesNotContain(olderBundle.ProductUpdatedEvent.EventId.ToString(), affiliateDocument.ProcessedEventIds);

        var marketplaceReadModel = marketplaceReadModelDocuments.Single();
        Assert.Equal(newerBundle.CanonicalState.ProductSalePrice, marketplaceReadModel.Price);
        Assert.Equal(newerBundle.MarketplaceEvent.EventVersion, marketplaceReadModel.LastEventVersion);
        Assert.Equal(newerBundle.MarketplaceEvent.EventId, marketplaceReadModel.LastEventId);
        Assert.NotEqual(olderBundle.MarketplaceEvent.EventId, marketplaceReadModel.LastEventId);
        Assert.Equal(newerBundle.CanonicalState.ProductProjectionMutationVersion, marketplaceReadModel.LastEntityMutationVersion);

        progressEntries.Enqueue("verification:price-con-04-out-of-order-propagation-converged");
    }

    private static async Task ReplayAffiliateProductUpdatedEventAsync(
        string connectionString,
        TestTenantContext tenantContext,
        string mongoConnectionString,
        IMongoDatabase mongoDatabase,
        ProductUpdatedEvent staleProductUpdatedEvent)
    {
        await using var projectionDbContext = CreateDbContext(connectionString, tenantContext);
        var mongoDbContext = CreateMongoDbContext(mongoConnectionString, mongoDatabase.DatabaseNamespace.DatabaseName);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storefront:UrlTemplate"] = "https://{slug}.example.test"
            })
            .Build();

        var affiliateProjectionConsumer = new AffiliateProductIntelligenceProjectionConsumer(
            mongoDbContext,
            projectionDbContext,
            configuration,
            NullLogger<AffiliateProductIntelligenceProjectionConsumer>.Instance);

        await affiliateProjectionConsumer.Consume(
            BuildConsumeContext(staleProductUpdatedEvent).Object);
    }

    private static async Task ReplayDuplicatePriceCon06DownstreamPublishStormAsync(
        string connectionString,
        TestTenantContext tenantContext,
        string mongoConnectionString,
        IMongoDatabase mongoDatabase,
        IReadOnlyList<IntegrationEvent> publishedEvents,
        int replayAttempts,
        ConcurrentQueue<string> progressEntries)
    {
        var replayTasks = Enumerable.Range(1, replayAttempts)
            .Select(async attempt =>
            {
                progressEntries.Enqueue($"price-con-06:duplicate-downstream-replay-start:{attempt}");
                await ApplyPublishedEventsAsync(
                    connectionString,
                    tenantContext,
                    mongoConnectionString,
                    mongoDatabase,
                    publishedEvents);
                progressEntries.Enqueue($"price-con-06:duplicate-downstream-replay-complete:{attempt}");
            });

        await Task.WhenAll(replayTasks);
    }

    private static async Task AssertPriceCon05DurableStateAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        PriceCon03ScenarioSeed seeded,
        CommittedPriceMutationBundle olderBundle,
        CommittedPriceMutationBundle newerBundle,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var product = await verifyContext.Products
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.ProductId);
        var variant = await verifyContext.ProductVariants
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.DefaultVariantId);
        var outboxMessages = await verifyContext.OutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                (message.AggregateId == seeded.ProductId || message.AggregateId == seeded.DefaultVariantId))
            .OrderBy(message => message.CreatedAtUtc)
            .ThenBy(message => message.Id)
            .ToListAsync();
        var marketplaceOutboxMessages = await verifyContext.Set<MarketplaceEventOutboxMessage>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.EntityId == seeded.ProductId)
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .ToListAsync();
        var processedEvents = await verifyContext.Set<ProcessedEvent>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(processed =>
                processed.ConsumerName == nameof(MarketplaceProductUpdatedConsumer) &&
                processed.EntityId == seeded.ProductId &&
                processed.TenantId == seeded.TenantId)
            .OrderBy(processed => processed.CreatedAt)
            .ToListAsync();
        var projectionOutboxMessages = await verifyContext.Set<MarketplaceProjectionOutboxMessage>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.ConsumerName == nameof(MarketplaceProductUpdatedConsumer) &&
                message.EntityId == seeded.ProductId &&
                message.TenantId == seeded.TenantId)
            .ToListAsync();
        var idempotencyRecords = await verifyContext.ProductRequestIdempotencyRecords
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(record =>
                record.TenantId == seeded.TenantId &&
                record.ProductId == seeded.ProductId &&
                record.ProductVariantId == seeded.DefaultVariantId &&
                record.Operation == "UPDATE_VARIANT")
            .OrderBy(record => record.CreatedAt)
            .ToListAsync();

        var productDocument = await mongoDatabase
            .GetCollection<ProductDocument>("products")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();
        var listingDocument = await mongoDatabase
            .GetCollection<CommerceHubProductDocument>("commercehub_products")
            .Find(document => document.TenantId == seeded.TenantId && document.Id == CommerceHubListingId)
            .FirstOrDefaultAsync();
        var affiliateDocuments = await mongoDatabase
            .GetCollection<AffiliateProductIntelligenceDocument>("affiliate_product_intelligence")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .ToListAsync();
        var marketplaceReadModelDocuments = await mongoDatabase
            .GetCollection<MarketplaceProductReadModelDocument>(MarketplaceProductReadModelDocument.CollectionName)
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .ToListAsync();

        Assert.Equal(newerBundle.CanonicalState.ProductSalePrice, product.SalePrice);
        Assert.Equal(newerBundle.CanonicalState.VariantSalePrice, variant.Price);
        Assert.Equal(newerBundle.CanonicalState.ProductProjectionMutationVersion, product.ProjectionMutationVersion);
        Assert.Equal(newerBundle.CanonicalState.VariantProjectionMutationVersion, variant.ProjectionMutationVersion);

        Assert.Equal(6, outboxMessages.Count);
        Assert.All(outboxMessages, message => Assert.NotNull(message.ProcessedAtUtc));
        Assert.Equal(2, outboxMessages.Count(message => message.EventType == typeof(ProductUpdatedEvent).FullName));
        Assert.Equal(2, outboxMessages.Count(message => message.EventType == typeof(ProductVariantUpdatedEvent).FullName));
        Assert.Equal(2, outboxMessages.Count(message => message.EventType == typeof(VariantPriceUpdatedEvent).FullName));

        Assert.Equal(2, marketplaceOutboxMessages.Count);
        Assert.All(marketplaceOutboxMessages, message => Assert.NotNull(message.PublishedAtUtc));
        Assert.Single(processedEvents);
        Assert.Equal(newerBundle.MarketplaceEvent.EventId, processedEvents.Single().EventId);
        Assert.Empty(projectionOutboxMessages);

        Assert.Equal(2, idempotencyRecords.Count);
        Assert.Contains(idempotencyRecords, record => record.Key == olderBundle.CanonicalState.IdempotencyKey);
        Assert.Contains(idempotencyRecords, record => record.Key == newerBundle.CanonicalState.IdempotencyKey);

        Assert.NotNull(productDocument);
        Assert.NotNull(listingDocument);
        Assert.Single(affiliateDocuments);
        Assert.Single(marketplaceReadModelDocuments);

        var affiliateDocument = affiliateDocuments.Single();
        var storefrontVariant = Assert.Single(
            productDocument!.Variants.Where(variantDoc => variantDoc.VariantId == seeded.DefaultVariantId));
        Assert.Equal(newerBundle.CanonicalState.ProductSalePrice, productDocument.Price);
        Assert.Equal(newerBundle.CanonicalState.VariantSalePrice, storefrontVariant.Price);
        Assert.Equal(newerBundle.VariantUpdatedEvent.EventVersion, storefrontVariant.LastEventVersion);

        Assert.Equal(newerBundle.CanonicalState.VariantSalePrice, listingDocument!.Price);
        Assert.Equal(newerBundle.PriceUpdatedEvent.EventVersion, listingDocument.LastPriceEventVersion);
        Assert.Equal(newerBundle.PriceUpdatedEvent.EventId, listingDocument.LastEventId);
        Assert.NotEqual(olderBundle.PriceUpdatedEvent.EventId, listingDocument.LastEventId);

        Assert.Equal(newerBundle.CanonicalState.ProductSalePrice, affiliateDocument.BasePrice);
        Assert.Equal(newerBundle.ProductUpdatedEvent.EventVersion, affiliateDocument.LastProductEventVersion);
        Assert.Contains(newerBundle.ProductUpdatedEvent.EventId.ToString(), affiliateDocument.ProcessedEventIds);
        Assert.DoesNotContain(olderBundle.ProductUpdatedEvent.EventId.ToString(), affiliateDocument.ProcessedEventIds);
        Assert.Equal(1, affiliateDocument.ProcessedEventIds.Count(value => value == newerBundle.ProductUpdatedEvent.EventId.ToString()));

        var marketplaceReadModel = marketplaceReadModelDocuments.Single();
        Assert.Equal(newerBundle.CanonicalState.ProductSalePrice, marketplaceReadModel.Price);
        Assert.Equal(newerBundle.MarketplaceEvent.EventVersion, marketplaceReadModel.LastEventVersion);
        Assert.Equal(newerBundle.MarketplaceEvent.EventId, marketplaceReadModel.LastEventId);
        Assert.NotEqual(olderBundle.MarketplaceEvent.EventId, marketplaceReadModel.LastEventId);
        Assert.Equal(newerBundle.CanonicalState.ProductProjectionMutationVersion, marketplaceReadModel.LastEntityMutationVersion);

        progressEntries.Enqueue("verification:price-con-05-stale-affiliate-replay-rejected");
    }

    private static async Task AssertPriceCon07DurableStateAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        PriceCon03ScenarioSeed seeded,
        CommittedPriceMutationBundle olderBundle,
        CommittedPriceMutationBundle newerBundle,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var product = await verifyContext.Products
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.ProductId);
        var variant = await verifyContext.ProductVariants
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.DefaultVariantId);
        var outboxMessages = await verifyContext.OutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                (message.AggregateId == seeded.ProductId || message.AggregateId == seeded.DefaultVariantId))
            .OrderBy(message => message.CreatedAtUtc)
            .ThenBy(message => message.Id)
            .ToListAsync();
        var marketplaceOutboxMessages = await verifyContext.Set<MarketplaceEventOutboxMessage>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.EntityId == seeded.ProductId)
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .ToListAsync();
        var processedEvents = await verifyContext.Set<ProcessedEvent>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(processed =>
                processed.ConsumerName == nameof(MarketplaceProductUpdatedConsumer) &&
                processed.EntityId == seeded.ProductId &&
                processed.TenantId == seeded.TenantId)
            .OrderBy(processed => processed.CreatedAt)
            .ToListAsync();
        var projectionOutboxMessages = await verifyContext.Set<MarketplaceProjectionOutboxMessage>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.ConsumerName == nameof(MarketplaceProductUpdatedConsumer) &&
                message.EntityId == seeded.ProductId &&
                message.TenantId == seeded.TenantId)
            .ToListAsync();
        var idempotencyRecords = await verifyContext.ProductRequestIdempotencyRecords
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(record =>
                record.TenantId == seeded.TenantId &&
                record.ProductId == seeded.ProductId &&
                record.ProductVariantId == seeded.DefaultVariantId &&
                record.Operation == "UPDATE_VARIANT")
            .OrderBy(record => record.CreatedAt)
            .ToListAsync();

        var productDocument = await mongoDatabase
            .GetCollection<ProductDocument>("products")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();
        var listingDocument = await mongoDatabase
            .GetCollection<CommerceHubProductDocument>("commercehub_products")
            .Find(document => document.TenantId == seeded.TenantId && document.Id == CommerceHubListingId)
            .FirstOrDefaultAsync();
        var affiliateDocument = await mongoDatabase
            .GetCollection<AffiliateProductIntelligenceDocument>("affiliate_product_intelligence")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();
        var marketplaceReadModelDocuments = await mongoDatabase
            .GetCollection<MarketplaceProductReadModelDocument>(MarketplaceProductReadModelDocument.CollectionName)
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .ToListAsync();

        Assert.Equal(newerBundle.CanonicalState.ProductSalePrice, product.SalePrice);
        Assert.Equal(newerBundle.CanonicalState.VariantSalePrice, variant.Price);
        Assert.Equal(newerBundle.CanonicalState.ProductProjectionMutationVersion, product.ProjectionMutationVersion);
        Assert.Equal(newerBundle.CanonicalState.VariantProjectionMutationVersion, variant.ProjectionMutationVersion);

        Assert.Equal(6, outboxMessages.Count);
        Assert.All(outboxMessages, message => Assert.NotNull(message.ProcessedAtUtc));
        Assert.Equal(2, outboxMessages.Count(message => message.EventType == typeof(ProductUpdatedEvent).FullName));
        Assert.Equal(2, outboxMessages.Count(message => message.EventType == typeof(ProductVariantUpdatedEvent).FullName));
        Assert.Equal(2, outboxMessages.Count(message => message.EventType == typeof(VariantPriceUpdatedEvent).FullName));

        // Only the two authoritative SQL commits should exist in the marketplace
        // event outbox. The stale post-recovery replay is injected directly into
        // the consumer path and must not fabricate a third SQL publish record.
        Assert.Equal(2, marketplaceOutboxMessages.Count);
        Assert.All(marketplaceOutboxMessages, message => Assert.NotNull(message.PublishedAtUtc));
        Assert.Equal(
            1,
            marketplaceOutboxMessages.Count(message => message.EventId == olderBundle.MarketplaceEvent.EventId));
        Assert.Equal(
            1,
            marketplaceOutboxMessages.Count(message => message.EventId == newerBundle.MarketplaceEvent.EventId));
        // PRICE-CON-07 injects a distinct stale marketplace replay after V40
        // convergence. Depending on where the stale replay is rejected, it may
        // or may not receive its own durable processed-event row. The critical
        // invariant is that older/newer authoritative deliveries are each fenced
        // once, the stale replay never amplifies beyond one row, and the read
        // model stays pinned to the authoritative V40 mutation/version pair.
        var olderMarketplaceProcessedCount = processedEvents.Count(
            processed => processed.EventId == olderBundle.MarketplaceEvent.EventId);
        var newerMarketplaceProcessedCount = processedEvents.Count(
            processed => processed.EventId == newerBundle.MarketplaceEvent.EventId);
        var staleMarketplaceReplayProcessedCount = processedEvents.Count(
            processed => processed.EventId == PriceCon07PostRecoveryMarketplaceReplayEventId);

        Assert.Equal(1, olderMarketplaceProcessedCount);
        Assert.Equal(1, newerMarketplaceProcessedCount);
        Assert.InRange(staleMarketplaceReplayProcessedCount, 0, 1);
        Assert.Equal(
            olderMarketplaceProcessedCount + newerMarketplaceProcessedCount + staleMarketplaceReplayProcessedCount,
            processedEvents.Count);
        Assert.Empty(projectionOutboxMessages);

        Assert.Equal(2, idempotencyRecords.Count);
        Assert.Contains(idempotencyRecords, record => record.Key == olderBundle.CanonicalState.IdempotencyKey);
        Assert.Contains(idempotencyRecords, record => record.Key == newerBundle.CanonicalState.IdempotencyKey);

        Assert.NotNull(productDocument);
        Assert.NotNull(listingDocument);
        Assert.NotNull(affiliateDocument);
        Assert.Single(marketplaceReadModelDocuments);

        var storefrontVariant = Assert.Single(
            productDocument!.Variants.Where(variantDoc => variantDoc.VariantId == seeded.DefaultVariantId));
        Assert.Equal(newerBundle.CanonicalState.ProductSalePrice, productDocument.Price);
        Assert.Equal(newerBundle.CanonicalState.VariantSalePrice, storefrontVariant.Price);
        Assert.Equal(newerBundle.VariantUpdatedEvent.EventVersion, storefrontVariant.LastEventVersion);

        Assert.Equal(newerBundle.CanonicalState.VariantSalePrice, listingDocument!.Price);
        Assert.Equal(newerBundle.PriceUpdatedEvent.EventVersion, listingDocument.LastPriceEventVersion);
        Assert.Equal(newerBundle.PriceUpdatedEvent.EventId, listingDocument.LastEventId);

        Assert.Equal(newerBundle.CanonicalState.ProductSalePrice, affiliateDocument!.BasePrice);
        Assert.Equal(newerBundle.ProductUpdatedEvent.EventVersion, affiliateDocument.LastProductEventVersion);
        Assert.Equal(
            1,
            affiliateDocument.ProcessedEventIds.Count(value => value == olderBundle.ProductUpdatedEvent.EventId.ToString()));
        Assert.Equal(
            1,
            affiliateDocument.ProcessedEventIds.Count(value => value == newerBundle.ProductUpdatedEvent.EventId.ToString()));

        var marketplaceReadModel = marketplaceReadModelDocuments.Single();
        Assert.Equal(newerBundle.CanonicalState.ProductSalePrice, marketplaceReadModel.Price);
        Assert.Equal(newerBundle.MarketplaceEvent.EventVersion, marketplaceReadModel.LastEventVersion);
        Assert.Equal(newerBundle.MarketplaceEvent.EventId, marketplaceReadModel.LastEventId);
        Assert.Equal(newerBundle.CanonicalState.ProductProjectionMutationVersion, marketplaceReadModel.LastEntityMutationVersion);

        progressEntries.Enqueue("verification:price-con-07-partial-sync-corruption-converged");
    }

    private static async Task AssertPriceCon06DurableStateAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        PriceCon03ScenarioSeed seeded,
        CommittedPriceMutationBundle committedBundle,
        IReadOnlyCollection<PriceCon03MarketplaceWorkerResult> overlapResults,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var product = await verifyContext.Products
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.ProductId);
        var variant = await verifyContext.ProductVariants
            .AsNoTracking()
            .SingleAsync(entry => entry.Id == seeded.DefaultVariantId);
        var outboxMessages = await verifyContext.OutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                (message.AggregateId == seeded.ProductId || message.AggregateId == seeded.DefaultVariantId))
            .OrderBy(message => message.CreatedAtUtc)
            .ThenBy(message => message.Id)
            .ToListAsync();
        var marketplaceOutboxMessages = await verifyContext.Set<MarketplaceEventOutboxMessage>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.EntityId == seeded.ProductId)
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .ToListAsync();
        var processedEvents = await verifyContext.Set<ProcessedEvent>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(processed =>
                processed.ConsumerName == nameof(MarketplaceProductUpdatedConsumer) &&
                processed.EntityId == seeded.ProductId &&
                processed.TenantId == seeded.TenantId)
            .OrderBy(processed => processed.CreatedAt)
            .ToListAsync();
        var projectionOutboxMessages = await verifyContext.Set<MarketplaceProjectionOutboxMessage>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.ConsumerName == nameof(MarketplaceProductUpdatedConsumer) &&
                message.EntityId == seeded.ProductId &&
                message.TenantId == seeded.TenantId)
            .ToListAsync();
        var idempotencyRecords = await verifyContext.ProductRequestIdempotencyRecords
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(record =>
                record.TenantId == seeded.TenantId &&
                record.ProductId == seeded.ProductId &&
                record.ProductVariantId == seeded.DefaultVariantId &&
                record.Operation == "UPDATE_VARIANT")
            .OrderBy(record => record.CreatedAt)
            .ToListAsync();

        var productDocument = await mongoDatabase
            .GetCollection<ProductDocument>("products")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();
        var listingDocuments = await mongoDatabase
            .GetCollection<CommerceHubProductDocument>("commercehub_products")
            .Find(document => document.TenantId == seeded.TenantId && document.Id == CommerceHubListingId)
            .ToListAsync();
        var affiliateDocuments = await mongoDatabase
            .GetCollection<AffiliateProductIntelligenceDocument>("affiliate_product_intelligence")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .ToListAsync();
        var marketplaceReadModelDocuments = await mongoDatabase
            .GetCollection<MarketplaceProductReadModelDocument>(MarketplaceProductReadModelDocument.CollectionName)
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .ToListAsync();

        Assert.Equal(PriceCon06CommittedSellPrice, product.SalePrice);
        Assert.Equal(PriceCon06CommittedSellPrice, variant.Price);
        Assert.Equal(committedBundle.CanonicalState.ProductProjectionMutationVersion, product.ProjectionMutationVersion);
        Assert.Equal(committedBundle.CanonicalState.VariantProjectionMutationVersion, variant.ProjectionMutationVersion);

        Assert.Equal(3, outboxMessages.Count);
        Assert.All(outboxMessages, message => Assert.NotNull(message.ProcessedAtUtc));
        Assert.Single(outboxMessages.Where(message => message.EventType == typeof(ProductUpdatedEvent).FullName));
        Assert.Single(outboxMessages.Where(message => message.EventType == typeof(ProductVariantUpdatedEvent).FullName));
        Assert.Single(outboxMessages.Where(message => message.EventType == typeof(VariantPriceUpdatedEvent).FullName));

        Assert.Single(marketplaceOutboxMessages);
        Assert.NotNull(marketplaceOutboxMessages.Single().PublishedAtUtc);
        Assert.Equal(committedBundle.MarketplaceEvent.EventId, marketplaceOutboxMessages.Single().EventId);
        Assert.Equal(committedBundle.MarketplaceEvent.Version, marketplaceOutboxMessages.Single().Version);

        Assert.Single(processedEvents);
        Assert.Equal(committedBundle.MarketplaceEvent.EventId, processedEvents.Single().EventId);
        Assert.Empty(projectionOutboxMessages);

        Assert.Single(idempotencyRecords);
        Assert.Equal(committedBundle.CanonicalState.IdempotencyKey, idempotencyRecords.Single().Key);

        Assert.NotNull(productDocument);
        Assert.Single(listingDocuments);
        Assert.Single(affiliateDocuments);
        Assert.Single(marketplaceReadModelDocuments);

        var listingDocument = listingDocuments.Single();
        var affiliateDocument = affiliateDocuments.Single();
        var marketplaceReadModel = marketplaceReadModelDocuments.Single();
        var storefrontVariant = Assert.Single(
            productDocument!.Variants.Where(variantDoc => variantDoc.VariantId == seeded.DefaultVariantId));

        Assert.Equal(PriceCon06CommittedSellPrice, productDocument.Price);
        Assert.Equal(PriceCon06CommittedSellPrice, storefrontVariant.Price);
        Assert.Equal(committedBundle.VariantUpdatedEvent.EventVersion, storefrontVariant.LastEventVersion);

        Assert.Equal(PriceCon06CommittedSellPrice, listingDocument.Price);
        Assert.Equal(committedBundle.PriceUpdatedEvent.EventId, listingDocument.LastEventId);
        Assert.Equal(committedBundle.PriceUpdatedEvent.EventVersion, listingDocument.LastPriceEventVersion);

        Assert.Equal(PriceCon06CommittedSellPrice, affiliateDocument.BasePrice);
        Assert.Equal(committedBundle.ProductUpdatedEvent.EventVersion, affiliateDocument.LastProductEventVersion);
        Assert.Equal(
            1,
            affiliateDocument.ProcessedEventIds.Count(value => value == committedBundle.ProductUpdatedEvent.EventId.ToString()));

        Assert.Equal(PriceCon06CommittedSellPrice, marketplaceReadModel.Price);
        Assert.Equal(committedBundle.MarketplaceEvent.EventId, marketplaceReadModel.LastEventId);
        Assert.Equal(committedBundle.MarketplaceEvent.EventVersion, marketplaceReadModel.LastEventVersion);
        Assert.Equal(committedBundle.CanonicalState.ProductProjectionMutationVersion, marketplaceReadModel.LastEntityMutationVersion);

        Assert.Equal(2, overlapResults.Count);
        Assert.True(overlapResults.Any(result => result.WorkerName == "price-con-06-marketplace-worker-a"));
        Assert.True(overlapResults.Any(result => result.WorkerName == "price-con-06-marketplace-worker-b"));

        progressEntries.Enqueue("verification:price-con-06-duplicate-marketplace-publish-converged");
    }

    private static string BuildScopedProductMutationIdempotencyKey(Guid tenantId, string operationId) =>
        $"PRODUCT_VARIANT:{tenantId}:{operationId}";

    private static DateTime TruncateToMilliseconds(DateTime value)
    {
        var utcValue = value.Kind == DateTimeKind.Utc
            ? value
            : value.ToUniversalTime();

        var ticks = utcValue.Ticks - (utcValue.Ticks % TimeSpan.TicksPerMillisecond);
        return new DateTime(ticks, DateTimeKind.Utc);
    }

    private static Mock<ConsumeContext<T>> BuildConsumeContext<T>(T message)
        where T : class
    {
        var context = new Mock<ConsumeContext<T>>();
        context.SetupGet(entry => entry.Message).Returns(message);
        context.SetupGet(entry => entry.CancellationToken).Returns(CancellationToken.None);
        context.SetupGet(entry => entry.CorrelationId).Returns((Guid?)null);
        return context;
    }

    private static Mock<IConnectionMultiplexer> CreateRedisMultiplexerMock()
    {
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        database
            .Setup(entry => entry.KeyDeleteAsync(
                It.IsAny<RedisKey[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(0L);

        var multiplexer = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        multiplexer
            .Setup(entry => entry.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);
        return multiplexer;
    }

    private static Mock<IUnifiedSearchIndexService> CreateProductSearchIndexServiceMock()
    {
        var service = new Mock<IUnifiedSearchIndexService>(MockBehavior.Loose);
        service
            .Setup(entry => entry.IndexProductAsync(
                It.IsAny<ProductSearchDocument>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        service
            .Setup(entry => entry.DeleteProductAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return service;
    }

    private static Mock<IUnifiedSearchIndexService> CreateUnifiedSearchIndexServiceMock()
    {
        var service = new Mock<IUnifiedSearchIndexService>(MockBehavior.Loose);
        service
            .Setup(entry => entry.IndexProductAsync(
                It.IsAny<ProductSearchDocument>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        service
            .Setup(entry => entry.DeleteProductAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return service;
    }

    private static Mock<IStorefrontProductCacheInvalidator> CreateProductCacheInvalidatorMock()
    {
        var service = new Mock<IStorefrontProductCacheInvalidator>(MockBehavior.Loose);
        service
            .Setup(entry => entry.InvalidateProductAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        service
            .Setup(entry => entry.InvalidateTenantAsync(
                It.IsAny<Guid>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return service;
    }

    private static Mock<Nest.IElasticClient> CreateElasticClientMock()
    {
        return new Mock<Nest.IElasticClient>(MockBehavior.Loose);
    }

    private static Mock<ICommerceHubSearchIndexService> CreateCommerceHubSearchIndexServiceMock()
    {
        var service = new Mock<ICommerceHubSearchIndexService>(MockBehavior.Loose);
        service
            .Setup(entry => entry.IndexAsync(
                It.IsAny<CommerceHubProductSearchDocument>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        service
            .Setup(entry => entry.DeleteAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return service;
    }

    private static MongoDbContext CreateMongoDbContext(string mongoConnectionString, string databaseName)
    {
        return new MongoDbContext(
            Options.Create(new MongoDbSettings
            {
                ConnectionString = mongoConnectionString,
                DatabaseName = databaseName,
                ProductsCollectionName = "products",
                CategoriesCollectionName = "categories"
            }),
            NullLogger<MongoDbContext>.Instance);
    }

    private static async Task<PriceMongoDatabaseScope> CreateIsolatedMongoDatabaseScopeAsync(string scenarioKey)
    {
        var connectionString = GetRequiredMongoConnectionString();
        var client = new MongoClient(connectionString);
        var databaseName = $"price_con_01_{TestConfiguration.BuildDeterministicSuffix($"{scenarioKey}_{Guid.NewGuid():N}")}";
        var database = client.GetDatabase(databaseName);

        await database.ListCollectionNamesAsync();
        await CreateMongoDbContext(connectionString, databaseName).EnsureIndexesCreatedAsync();

        return new PriceMongoDatabaseScope(client, databaseName, database);
    }

    private static string GetRequiredMongoConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("MongoDB__ConnectionString");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        var solutionRoot = TestConfiguration.GetSolutionRoot();
        var settingsPath = Path.Combine(solutionRoot, "PointOfSale.API", "appsettings.Test.json");
        var localSettingsPath = Path.Combine(solutionRoot, "PointOfSale.API", "appsettings.Test.local.json");

        if (TryReadJsonString(localSettingsPath, out var localConnectionString, "MongoDB", "ConnectionString"))
        {
            return localConnectionString;
        }

        if (TryReadJsonString(settingsPath, out var baseConnectionString, "MongoDB", "ConnectionString"))
        {
            return baseConnectionString;
        }

        throw new InvalidOperationException("MongoDB connection string was not configured for the test environment.");
    }

    private static string GetRequiredRedisConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("Redis__ConnectionString");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        var solutionRoot = TestConfiguration.GetSolutionRoot();
        var settingsPath = Path.Combine(solutionRoot, "PointOfSale.API", "appsettings.Test.json");
        var localSettingsPath = Path.Combine(solutionRoot, "PointOfSale.API", "appsettings.Test.local.json");

        if (TryReadJsonString(localSettingsPath, out var localConnectionString, "Redis", "ConnectionString"))
        {
            return localConnectionString;
        }

        if (TryReadJsonString(settingsPath, out var baseConnectionString, "Redis", "ConnectionString"))
        {
            return baseConnectionString;
        }

        throw new InvalidOperationException("Redis connection string was not configured for the test environment.");
    }

    private static bool TryReadJsonString(string path, out string value, params string[] segments)
    {
        value = string.Empty;
        if (!File.Exists(path))
        {
            return false;
        }

        using var document = JsonDocument.Parse(
            File.ReadAllText(path),
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

        JsonElement current = document.RootElement;
        foreach (var segment in segments)
        {
            if (!current.TryGetProperty(segment, out current))
            {
                return false;
            }
        }

        if (current.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = current.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static async Task ConfigureSessionTimeoutsAsync(ApplicationDbContext context)
    {
        context.Database.SetCommandTimeout(TimeSpan.FromSeconds(30));
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("SET statement_timeout = '30s'; SET lock_timeout = '30s';");
    }

    private static string DescribeException(Exception exception)
    {
        if (exception is DbUpdateException dbUpdateException)
        {
            var entryTypes = dbUpdateException.Entries
                .Select(entry => entry.Metadata.ClrType.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var baseException = dbUpdateException.GetBaseException();

            return $"{dbUpdateException.GetType().Name}:{dbUpdateException.Message} | Entries=[{string.Join(", ", entryTypes)}] | Base={baseException.GetType().Name}:{baseException.Message}";
        }

        var segments = new List<string>();
        Exception? current = exception;
        while (current != null)
        {
            segments.Add($"{current.GetType().Name}:{current.Message}");
            current = current.InnerException;
        }

        return string.Join(" --> ", segments);
    }

    private sealed class AuthoritativeOutboxTestPublisher : IEventPublisher
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly Guid _tenantId;
        private readonly string _correlationId;

        public AuthoritativeOutboxTestPublisher(
            ApplicationDbContext dbContext,
            Guid tenantId,
            string correlationId)
        {
            _dbContext = dbContext;
            _tenantId = tenantId;
            _correlationId = correlationId;
        }

        public async Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            await PublishCoreAsync(@event, cancellationToken);
        }

        public async Task PublishBatchAsync<T>(IEnumerable<T> events, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            foreach (var @event in events)
            {
                await PublishCoreAsync(@event, cancellationToken);
            }
        }

        public Task PublishSensitiveAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent, ISensitiveIntegrationEvent
        {
            return PublishCoreAsync(@event, cancellationToken);
        }

        private async Task PublishCoreAsync(IntegrationEvent @event, CancellationToken cancellationToken)
        {
            _ = cancellationToken;

            @event.TenantId ??= _tenantId;
            @event.CorrelationId ??= _correlationId;
            @event.IdempotencyKey ??= $"{_correlationId}:{@event.EventType}";

            var aggregateId = IntegrationEventAuthority.TryGetAggregateId(@event)
                ?? throw new InvalidOperationException(
                    $"PRICE-CON-01 could not resolve an authoritative aggregate id for {@event.GetType().Name}.");
            var eventVersion = await ResolveAggregateProjectionVersionAsync(aggregateId);
            if (eventVersion.HasValue)
            {
                @event.EventVersion = eventVersion.Value;
            }

            _dbContext.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                EventType = @event.GetType().FullName ?? @event.GetType().Name,
                EventPayload = JsonSerializer.Serialize(@event, @event.GetType()),
                TenantId = @event.TenantId,
                AggregateId = aggregateId,
                CreatedAtUtc = @event.OccurredAtUtc,
                EventVersion = @event.EventVersion
            });
        }

        private async Task<int?> ResolveAggregateProjectionVersionAsync(Guid aggregateId)
        {
            var trackedVariantEntry = _dbContext.ChangeTracker.Entries<ProductVariant>()
                .FirstOrDefault(entry => entry.State != EntityState.Detached && entry.Entity.Id == aggregateId);
            if (trackedVariantEntry != null)
            {
                return ProductEventVersionHelper.ToProductEventVersion(
                    ResolvePendingProjectionVersion(trackedVariantEntry));
            }

            var trackedProductEntry = _dbContext.ChangeTracker.Entries<Product>()
                .FirstOrDefault(entry => entry.State != EntityState.Detached && entry.Entity.Id == aggregateId);
            if (trackedProductEntry != null)
            {
                return ProductEventVersionHelper.ToProductEventVersion(
                    ResolvePendingProjectionVersion(trackedProductEntry));
            }

            var trackedPromotionEntry = _dbContext.ChangeTracker.Entries<Promotion>()
                .FirstOrDefault(entry => entry.State != EntityState.Detached && entry.Entity.Id == aggregateId);
            if (trackedPromotionEntry != null)
            {
                return ProductEventVersionHelper.ToProductEventVersion(
                    ResolvePendingProjectionVersion(trackedPromotionEntry));
            }

            var variantProjectionVersion = await _dbContext.ProductVariants
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(variant => variant.Id == aggregateId)
                .Select(variant => (long?)variant.ProjectionMutationVersion)
                .FirstOrDefaultAsync();
            if (variantProjectionVersion.HasValue)
            {
                return ProductEventVersionHelper.ToProductEventVersion(variantProjectionVersion.Value);
            }

            var productProjectionVersion = await _dbContext.Products
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(product => product.Id == aggregateId)
                .Select(product => (long?)product.ProjectionMutationVersion)
                .FirstOrDefaultAsync();
            if (productProjectionVersion.HasValue)
            {
                return ProductEventVersionHelper.ToProductEventVersion(productProjectionVersion.Value);
            }

            var promotionProjectionVersion = await _dbContext.Promotions
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(promotion => promotion.Id == aggregateId)
                .Select(promotion => (long?)promotion.ProjectionMutationVersion)
                .FirstOrDefaultAsync();
            if (promotionProjectionVersion.HasValue)
            {
                return ProductEventVersionHelper.ToProductEventVersion(promotionProjectionVersion.Value);
            }

            return null;
        }

        private static long ResolvePendingProjectionVersion<TEntity>(
            Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity> entry)
            where TEntity : class, IProjectionVersioned
        {
            return entry.State switch
            {
                EntityState.Added => entry.Entity.ProjectionMutationVersion > 0
                    ? entry.Entity.ProjectionMutationVersion
                    : 1,
                EntityState.Modified or EntityState.Deleted => IsProjectionVersionManuallyAdvanced(entry)
                    ? entry.Entity.ProjectionMutationVersion
                    : entry.Entity.ProjectionMutationVersion + 1,
                _ => entry.Entity.ProjectionMutationVersion
            };
        }

        private static bool IsProjectionVersionManuallyAdvanced<TEntity>(
            Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity> entry)
            where TEntity : class, IProjectionVersioned
        {
            var property = entry.Property(nameof(IProjectionVersioned.ProjectionMutationVersion));
            return property.IsModified;
        }
    }

    private sealed class ProductOnlyMarketplaceEventPipeline : IMarketplaceEventPipeline
    {
        private readonly ProductReadModelConsumer _productReadModelConsumer;

        public ProductOnlyMarketplaceEventPipeline(ProductReadModelConsumer productReadModelConsumer)
        {
            _productReadModelConsumer = productReadModelConsumer;
        }

        public Task ProcessAsync(MarketplaceEvents.OrderCreatedEvent @event, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ProcessAsync(MarketplaceEvents.PaymentCompletedEvent @event, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ProcessAsync(MarketplaceEvents.OrderDeliveredEvent @event, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ProcessAsync(MarketplaceEvents.RefundProcessedEvent @event, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ProcessAsync(MarketplaceEvents.ProductUpdatedEvent @event, CancellationToken cancellationToken = default) =>
            _productReadModelConsumer.ConsumeAsync(@event, cancellationToken);
    }

    private sealed class NoOpEventLagTracker : IEventLagTracker
    {
        public void TrackEventProcessed(
            string eventType,
            DateTime? publishedAtUtc,
            TimeSpan processingDuration,
            string? correlationId = null)
        {
        }

        public void TrackEventFailed(
            string eventType,
            DateTime? publishedAtUtc,
            string errorMessage,
            string? correlationId = null)
        {
        }
    }

    private sealed class NoOpEventPublisher : IEventPublisher
    {
        public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent =>
            Task.CompletedTask;

        public Task PublishBatchAsync<T>(IEnumerable<T> events, CancellationToken cancellationToken = default)
            where T : IntegrationEvent =>
            Task.CompletedTask;

        public Task PublishSensitiveAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent, ISensitiveIntegrationEvent =>
            Task.CompletedTask;
    }

    private sealed class PriceCon03ActiveProcessingOverlapWindow
    {
        private readonly TimeSpan _timeout;
        private readonly ConcurrentQueue<string> _progressEntries;
        private readonly TaskCompletionSource<bool> _originalPause =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _duplicateAttempt =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseOriginal =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _originalPausedBeforeDuplicateAttempt;

        public PriceCon03ActiveProcessingOverlapWindow(
            TimeSpan timeout,
            ConcurrentQueue<string> progressEntries)
        {
            _timeout = timeout;
            _progressEntries = progressEntries;
        }

        public bool OriginalPauseObservedBeforeDuplicateClaimAttempt =>
            Volatile.Read(ref _originalPausedBeforeDuplicateAttempt) == 1;

        public bool TryPauseOriginalWorker(string workerName)
        {
            if (!_originalPause.TrySetResult(true))
            {
                return false;
            }

            _progressEntries.Enqueue($"{workerName}:price-con-03-projection-outbox-paused-before-sql-visibility");
            return true;
        }

        public void MarkDuplicateClaimAttempt(string workerName)
        {
            if (_originalPause.Task.IsCompletedSuccessfully)
            {
                Interlocked.Exchange(ref _originalPausedBeforeDuplicateAttempt, 1);
            }

            _progressEntries.Enqueue($"{workerName}:price-con-03-duplicate-claim-attempted");
            _duplicateAttempt.TrySetResult(true);
        }

        public async Task WaitForOriginalPauseAsync()
        {
            try
            {
                await _originalPause.Task.WaitAsync(_timeout);
            }
            catch (TimeoutException)
            {
                throw new XunitException(
                    $"Timed out waiting for the original PRICE-CON-03 marketplace worker to pause before commit. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public async Task WaitForDuplicateClaimAttemptAsync()
        {
            try
            {
                await _duplicateAttempt.Task.WaitAsync(_timeout);
            }
            catch (TimeoutException)
            {
                throw new XunitException(
                    $"Timed out waiting for the duplicate PRICE-CON-03 marketplace worker to attempt the overlapping processed-event claim. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public async Task WaitUntilReleaseAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _releaseOriginal.Task.WaitAsync(_timeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                throw new XunitException(
                    $"Timed out waiting to release the original PRICE-CON-03 marketplace worker. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public void ReleaseOriginalWorker()
        {
            _progressEntries.Enqueue("orchestrator:release-original-price-con-03-marketplace-worker");
            _releaseOriginal.TrySetResult(true);
        }
    }

    private sealed class PriceCon10PromotionProjectionOverlapWindow
    {
        private readonly TimeSpan _timeout;
        private readonly ConcurrentQueue<string> _progressEntries;
        private readonly TaskCompletionSource<bool> _workerPaused =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseWorker =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _paused;

        public PriceCon10PromotionProjectionOverlapWindow(
            TimeSpan timeout,
            ConcurrentQueue<string> progressEntries)
        {
            _timeout = timeout;
            _progressEntries = progressEntries;
        }

        public async Task PauseOnSearchIndexAsync(
            string workerName,
            Guid productId,
            CancellationToken cancellationToken)
        {
            if (Interlocked.CompareExchange(ref _paused, 1, 0) == 0)
            {
                _progressEntries.Enqueue($"{workerName}:price-con-10-paused-before-search-cache-completion:{productId}");
                _workerPaused.TrySetResult(true);
                await _releaseWorker.Task.WaitAsync(_timeout, cancellationToken);
            }
        }

        public async Task WaitForWorkerPauseAsync()
        {
            try
            {
                await _workerPaused.Task.WaitAsync(_timeout);
            }
            catch (TimeoutException)
            {
                throw new XunitException(
                    $"Timed out waiting for PRICE-CON-10 promotion worker overlap. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public void ReleaseWorker()
        {
            _progressEntries.Enqueue("orchestrator:release-price-con-10-promotion-worker");
            _releaseWorker.TrySetResult(true);
        }
    }

    private sealed class PriceCon03ProjectionCommitPauseTransactionInterceptor(
        string workerName,
        PriceCon03ActiveProcessingOverlapWindow overlapWindow) : DbTransactionInterceptor
    {
        private int _intercepted;

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0 &&
                overlapWindow.TryPauseOriginalWorker(workerName))
            {
                // PRICE-CON-03 critical window:
                // the authoritative marketplace pricing worker has staged the SQL-backed
                // projection outbox handoff, but PostgreSQL has not committed visibility yet.
                await overlapWindow.WaitUntilReleaseAsync(cancellationToken);
            }

            return await base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }

    private sealed class PriceCon03ProjectionOutboxInsertAttemptInterceptor(
        string workerName,
        PriceCon03ActiveProcessingOverlapWindow overlapWindow) : DbCommandInterceptor
    {
        private int _intercepted;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (IsProjectionOutboxInsert(command.CommandText) &&
                Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0)
            {
                overlapWindow.MarkDuplicateClaimAttempt(workerName);
            }

            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (IsProjectionOutboxInsert(command.CommandText) &&
                Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0)
            {
                overlapWindow.MarkDuplicateClaimAttempt(workerName);
            }

            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private static bool IsProjectionOutboxInsert(string commandText)
        {
            return commandText.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase) &&
                   commandText.Contains("MarketplaceProjectionOutboxMessages", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class PriceVariantReadRendezvous
    {
        private readonly Barrier _barrier;
        private readonly TimeSpan _timeout;
        private readonly ConcurrentQueue<string> _progressEntries;
        private int _arrivalCount;

        public PriceVariantReadRendezvous(
            int participantCount,
            TimeSpan timeout,
            ConcurrentQueue<string> progressEntries)
        {
            _barrier = new Barrier(participantCount);
            _timeout = timeout;
            _progressEntries = progressEntries;
        }

        public int ArrivalCount => Volatile.Read(ref _arrivalCount);

        public async Task WaitAsync(string actorName, CancellationToken cancellationToken)
        {
            var arrival = Interlocked.Increment(ref _arrivalCount);
            _progressEntries.Enqueue($"{actorName}:variant-read-arrived:{arrival}");

            var barrierTask = Task.Run(() => _barrier.SignalAndWait(_timeout, cancellationToken), cancellationToken);
            try
            {
                var completed = await barrierTask;
                if (!completed)
                {
                    throw new XunitException(
                        $"Timed out synchronizing PRICE-CON-01 variant reads. Progress: {string.Join(" | ", _progressEntries)}");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is BarrierPostPhaseException or InvalidOperationException)
            {
                throw new XunitException(
                    $"Timed out synchronizing PRICE-CON-01 variant reads. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }
    }

    private sealed class PriceMutationOverlapWindow
    {
        private readonly TimeSpan _timeout;
        private readonly ConcurrentQueue<string> _progressEntries;
        private readonly TaskCompletionSource<bool> _originalCommitPaused =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _staleUpdateAttempted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseOriginalCommit =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _originalPausedBeforeStaleUpdateAttempt;

        public PriceMutationOverlapWindow(
            TimeSpan timeout,
            ConcurrentQueue<string> progressEntries)
        {
            _timeout = timeout;
            _progressEntries = progressEntries;
        }

        public bool OriginalCommitPaused => _originalCommitPaused.Task.IsCompletedSuccessfully;

        public bool OriginalPauseObservedBeforeStaleUpdateAttempt =>
            Volatile.Read(ref _originalPausedBeforeStaleUpdateAttempt) == 1;

        public bool TryPauseOriginalCommit(string actorName)
        {
            if (!_originalCommitPaused.TrySetResult(true))
            {
                return false;
            }

            _progressEntries.Enqueue($"{actorName}:price-commit-paused-before-sql-visibility");
            return true;
        }

        public void MarkStaleUpdateAttempt(string actorName)
        {
            if (_originalCommitPaused.Task.IsCompletedSuccessfully)
            {
                Interlocked.Exchange(ref _originalPausedBeforeStaleUpdateAttempt, 1);
            }

            _progressEntries.Enqueue($"{actorName}:stale-price-update-attempted");
            _staleUpdateAttempted.TrySetResult(true);
        }

        public async Task WaitForOriginalCommitPauseAsync()
        {
            try
            {
                await _originalCommitPaused.Task.WaitAsync(_timeout);
            }
            catch (TimeoutException)
            {
                throw new XunitException(
                    $"Timed out waiting for the winning PRICE-CON-01 transaction to pause before commit. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public async Task WaitForStaleUpdateAttemptAsync()
        {
            try
            {
                await _staleUpdateAttempted.Task.WaitAsync(_timeout);
            }
            catch (TimeoutException)
            {
                throw new XunitException(
                    $"Timed out waiting for the stale PRICE-CON-01 update attempt. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public async Task WaitUntilCommitReleasedAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _releaseOriginalCommit.Task.WaitAsync(_timeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                throw new XunitException(
                    $"Timed out waiting to release the authoritative PRICE-CON-01 commit. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public async Task WaitUntilOriginalPauseFromInterceptorAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _originalCommitPaused.Task.WaitAsync(_timeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                throw new XunitException(
                    $"Timed out waiting for actor A to pause before actor B attempted its stale price write. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public void ReleaseOriginalCommit()
        {
            _progressEntries.Enqueue("orchestrator:release-authoritative-price-commit");
            _releaseOriginalCommit.TrySetResult(true);
        }
    }

    private sealed class PromotionCommitPauseWindow
    {
        private readonly TimeSpan _timeout;
        private readonly ConcurrentQueue<string> _progressEntries;
        private readonly TaskCompletionSource<bool> _originalCommitPaused =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseOriginalCommit =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PromotionCommitPauseWindow(
            TimeSpan timeout,
            ConcurrentQueue<string> progressEntries)
        {
            _timeout = timeout;
            _progressEntries = progressEntries;
        }

        public bool OriginalCommitPaused => _originalCommitPaused.Task.IsCompletedSuccessfully;

        public bool TryPauseOriginalCommit(string actorName)
        {
            if (!_originalCommitPaused.TrySetResult(true))
            {
                return false;
            }

            _progressEntries.Enqueue($"{actorName}:promotion-commit-paused-before-visibility");
            return true;
        }

        public async Task WaitForOriginalCommitPauseAsync()
        {
            try
            {
                await _originalCommitPaused.Task.WaitAsync(_timeout);
            }
            catch (TimeoutException)
            {
                throw new XunitException(
                    $"Timed out waiting for the authoritative promotion mutation to pause before commit. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public async Task WaitUntilCommitReleasedAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _releaseOriginalCommit.Task.WaitAsync(_timeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                throw new XunitException(
                    $"Timed out waiting to release the paused authoritative promotion commit. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public void ReleaseOriginalCommit()
        {
            _progressEntries.Enqueue("orchestrator:release-authoritative-promotion-commit");
            _releaseOriginalCommit.TrySetResult(true);
        }
    }

    private sealed class PriceActiveTransactionReplayWindow
    {
        private readonly TimeSpan _timeout;
        private readonly ConcurrentQueue<string> _progressEntries;
        private readonly TaskCompletionSource<bool> _originalCommitPaused =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _replayClaimAttempted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseOriginalCommit =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _originalPausedBeforeReplayClaim;

        public PriceActiveTransactionReplayWindow(
            TimeSpan timeout,
            ConcurrentQueue<string> progressEntries)
        {
            _timeout = timeout;
            _progressEntries = progressEntries;
        }

        public bool OriginalCommitPaused => _originalCommitPaused.Task.IsCompletedSuccessfully;
        public bool ReplayClaimAttempted => _replayClaimAttempted.Task.IsCompletedSuccessfully;
        public bool OriginalWasPausedBeforeReplayClaim =>
            Volatile.Read(ref _originalPausedBeforeReplayClaim) == 1;

        public void MarkOriginalCommitPaused(string actorName)
        {
            if (_originalCommitPaused.TrySetResult(true))
            {
                _progressEntries.Enqueue($"{actorName}:price-replay-commit-paused-before-sql-visibility");
            }
        }

        public void MarkReplayClaimAttempted(string actorName)
        {
            if (_originalCommitPaused.Task.IsCompletedSuccessfully)
            {
                Interlocked.Exchange(ref _originalPausedBeforeReplayClaim, 1);
            }

            _progressEntries.Enqueue($"{actorName}:price-replay-idempotency-claim-attempted");
            _replayClaimAttempted.TrySetResult(true);
        }

        public async Task WaitForOriginalCommitPauseAsync()
        {
            try
            {
                await _originalCommitPaused.Task.WaitAsync(_timeout);
            }
            catch (TimeoutException)
            {
                throw new XunitException(
                    $"Timed out waiting for the authoritative PRICE-CON-02 transaction to pause before commit. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public async Task WaitForReplayClaimAttemptAsync()
        {
            try
            {
                await _replayClaimAttempted.Task.WaitAsync(_timeout);
            }
            catch (TimeoutException)
            {
                throw new XunitException(
                    $"Timed out waiting for the overlapping PRICE-CON-02 replay idempotency claim. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public async Task WaitUntilCommitReleasedAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _releaseOriginalCommit.Task.WaitAsync(_timeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                throw new XunitException(
                    $"Timed out waiting to release the authoritative PRICE-CON-02 commit. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public void ReleaseOriginalCommit()
        {
            _progressEntries.Enqueue("orchestrator:release-authoritative-price-replay-commit");
            _releaseOriginalCommit.TrySetResult(true);
        }
    }

    private sealed class VariantPriceReadBarrierInterceptor : DbCommandInterceptor
    {
        private readonly string _actorName;
        private readonly PriceVariantReadRendezvous _readRendezvous;
        private readonly ConcurrentQueue<string> _progressEntries;
        private int _intercepted;

        public VariantPriceReadBarrierInterceptor(
            string actorName,
            PriceVariantReadRendezvous readRendezvous,
            ConcurrentQueue<string> progressEntries)
        {
            _actorName = actorName;
            _readRendezvous = readRendezvous;
            _progressEntries = progressEntries;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (IsTargetVariantRead(command.CommandText) &&
                Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0)
            {
                _progressEntries.Enqueue($"{_actorName}:target-variant-read-detected");
                await _readRendezvous.WaitAsync(_actorName, cancellationToken);
            }

            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private static bool IsTargetVariantRead(string commandText)
        {
            return commandText.Contains("SELECT", StringComparison.OrdinalIgnoreCase) &&
                   commandText.Contains("FROM \"ProductVariants\"", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class PriceReplayCommitPauseTransactionInterceptor(
        string actorName,
        PriceActiveTransactionReplayWindow replayWindow) : DbTransactionInterceptor
    {
        private int _intercepted;

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0)
            {
                // PRICE-CON-02 critical window:
                // the original request already staged the authoritative sell-price mutation,
                // the completed update idempotency response, and the two durable price outbox
                // intents inside PostgreSQL, but none of that state is committed or externally visible yet.
                replayWindow.MarkOriginalCommitPaused(actorName);
                await replayWindow.WaitUntilCommitReleasedAsync(cancellationToken);
            }

            return await base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }

    private sealed class PriceReplayClaimAttemptInterceptor(
        string actorName,
        PriceActiveTransactionReplayWindow replayWindow) : DbCommandInterceptor
    {
        private int _intercepted;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (IsUpdateIdempotencyClaimInsert(command.CommandText) &&
                Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0)
            {
                replayWindow.MarkReplayClaimAttempted(actorName);
            }

            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private static bool IsUpdateIdempotencyClaimInsert(string commandText)
        {
            return commandText.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase) &&
                   commandText.Contains("ProductRequestIdempotency", StringComparison.OrdinalIgnoreCase) &&
                   commandText.Contains("ON CONFLICT", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class PriceCommitPauseTransactionInterceptor(
        string actorName,
        PriceMutationOverlapWindow overlapWindow) : DbTransactionInterceptor
    {
        private int _intercepted;

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0 &&
                overlapWindow.TryPauseOriginalCommit(actorName))
            {
                // PRICE-CON-01 critical window:
                // PostgreSQL already contains the winning sell-price mutation and both
                // authoritative outbox rows inside the transaction, but none of that
                // pricing truth is committed or externally visible yet.
                await overlapWindow.WaitUntilCommitReleasedAsync(cancellationToken);
            }

            return await base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }

    private sealed class PromotionCommitPauseTransactionInterceptor(
        string actorName,
        PromotionCommitPauseWindow overlapWindow) : DbTransactionInterceptor
    {
        private int _intercepted;

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0 &&
                overlapWindow.TryPauseOriginalCommit(actorName))
            {
                // Promotion mutation critical window:
                // the authoritative promotion change and outbox intent are staged in
                // the active SQL transaction, but none of that visibility state is
                // committed or externally observable until the orchestrator releases
                // this gate.
                await overlapWindow.WaitUntilCommitReleasedAsync(cancellationToken);
            }

            return await base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }

    private sealed class StaleVariantPriceUpdateInterceptor : DbCommandInterceptor
    {
        private readonly string _actorName;
        private readonly PriceMutationOverlapWindow _overlapWindow;
        private readonly ConcurrentQueue<string> _progressEntries;
        private int _intercepted;

        public StaleVariantPriceUpdateInterceptor(
            string actorName,
            PriceMutationOverlapWindow overlapWindow,
            ConcurrentQueue<string> progressEntries)
        {
            _actorName = actorName;
            _overlapWindow = overlapWindow;
            _progressEntries = progressEntries;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await WaitForOriginalPauseIfNeededAsync(command.CommandText, cancellationToken);

            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            await WaitForOriginalPauseIfNeededAsync(command.CommandText, cancellationToken);

            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private async Task WaitForOriginalPauseIfNeededAsync(
            string commandText,
            CancellationToken cancellationToken)
        {
            if (IsVariantUpdate(commandText) &&
                Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0)
            {
                _progressEntries.Enqueue($"{_actorName}:variant-update-detected");
                await _overlapWindow.WaitUntilOriginalPauseFromInterceptorAsync(cancellationToken);
                _overlapWindow.MarkStaleUpdateAttempt(_actorName);
            }
        }

        private static bool IsVariantUpdate(string commandText)
        {
            return commandText.Contains("UPDATE \"ProductVariants\"", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public Exception? LastException { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (exception != null)
            {
                LastException = exception;
            }
        }
    }

    private sealed record PriceScenarioSeed(
        Guid TenantId,
        Guid ProductId,
        Guid DefaultVariantId,
        Guid TargetVariantId,
        long ProductProjectionMutationVersion,
        long DefaultVariantProjectionMutationVersion,
        long TargetVariantProjectionMutationVersion);

    private sealed record PriceCon03ScenarioSeed(
        Guid TenantId,
        Guid ProductId,
        Guid DefaultVariantId,
        long ProductProjectionMutationVersion,
        long DefaultVariantProjectionMutationVersion);

    private sealed record PriceCon03CanonicalState(
        long ProductProjectionMutationVersion,
        long VariantProjectionMutationVersion,
        decimal ProductSalePrice,
        decimal VariantSalePrice,
        string IdempotencyKey);

    private sealed record PriceCanonicalState(
        long ProductProjectionMutationVersion,
        long VariantProjectionMutationVersion,
        decimal ProductSalePrice,
        decimal VariantSalePrice,
        string IdempotencyKey);

    private sealed record PriceActor(
        string Name,
        string RequestId,
        string TraceIdentifier,
        Guid UserId,
        decimal RequestedSellPrice);

    private sealed record PriceActorResult(
        PriceActor Actor,
        bool Succeeded,
        int? StatusCode,
        ProductVariantDto? Response,
        string? PayloadJson,
        TimeSpan Duration,
        Exception? Exception);

    private sealed record PromotionActor(
        string Name,
        string RequestId,
        string TraceIdentifier,
        Guid UserId,
        Guid PromotionId);

    private sealed record PromotionActorResult(
        PromotionActor Actor,
        bool Succeeded,
        int? StatusCode,
        PromotionDto? Response,
        string? PayloadJson,
        TimeSpan Duration,
        Exception? Exception);

    private sealed record PriceCon03MarketplaceWorker(
        string Name);

    private sealed record PriceCon03MarketplaceWorkerResult(
        string WorkerName,
        TimeSpan Duration,
        Exception? Exception);

    private sealed record PriceCon03MarketplaceOutboxDispatchResult(
        int ProcessedCount,
        MarketplaceEvents.ProductUpdatedEvent? PublishedEvent,
        Type? PublishedType);

    private sealed record CommittedPriceMutationBundle(
        PriceActor Actor,
        PriceActorResult MutationResult,
        PriceCanonicalState CanonicalState,
        IReadOnlyList<IntegrationEvent> StandardPublishedEvents,
        ProductUpdatedEvent ProductUpdatedEvent,
        ProductVariantUpdatedEvent VariantUpdatedEvent,
        VariantPriceUpdatedEvent PriceUpdatedEvent,
        MarketplaceEvents.ProductUpdatedEvent MarketplaceEvent);

    private sealed record PriceCon08ScenarioSeed(
        Guid TenantId,
        Guid ProductId,
        Guid DefaultVariantId,
        Guid PromotionAId,
        Guid PromotionBId,
        long ProductProjectionMutationVersion,
        long DefaultVariantProjectionMutationVersion);

    private sealed record PriceCon09ScenarioSeed(
        Guid TenantId,
        Guid ProductId,
        Guid DefaultVariantId,
        Guid ProductPromotionId,
        Guid CategoryPromotionId,
        DateTime PromotionStartDateUtc,
        DateTime PromotionEndDateUtc,
        long ProductProjectionMutationVersion,
        long DefaultVariantProjectionMutationVersion);

    private sealed record PriceCon04VisibilitySnapshot(
        decimal AuthoritativeProductPrice,
        decimal AuthoritativeVariantPrice,
        long AuthoritativeProductProjectionMutationVersion,
        long AuthoritativeVariantProjectionMutationVersion,
        decimal StorefrontProductPrice,
        decimal StorefrontVariantPrice,
        int StorefrontVariantEventVersion,
        decimal CommerceHubPrice,
        Guid? CommerceHubLastEventId,
        int CommerceHubLastPriceEventVersion,
        decimal AffiliateBasePrice,
        int AffiliateLastProductEventVersion,
        bool AffiliateContainsOlderProductEventId,
        bool AffiliateContainsNewerProductEventId,
        decimal MarketplaceReadModelPrice,
        Guid? MarketplaceReadModelLastEventId,
        int MarketplaceReadModelLastEventVersion,
        long MarketplaceReadModelLastEntityMutationVersion,
        int MarketplaceProcessedEventCount,
        int MarketplaceProjectionOutboxCount,
        int MarketplaceReadModelDocumentCount);

    private sealed record PriceCon05VisibilitySnapshot(
        decimal AuthoritativeProductPrice,
        decimal AuthoritativeVariantPrice,
        long AuthoritativeProductProjectionMutationVersion,
        long AuthoritativeVariantProjectionMutationVersion,
        decimal StorefrontProductPrice,
        decimal StorefrontVariantPrice,
        int StorefrontVariantEventVersion,
        decimal CommerceHubPrice,
        Guid? CommerceHubLastEventId,
        int CommerceHubLastPriceEventVersion,
        decimal AffiliateBasePrice,
        int AffiliateLastProductEventVersion,
        DateTime AffiliateLastUpdatedUtc,
        int AffiliateProcessedEventIdCount,
        bool AffiliateContainsOlderProductEventId,
        bool AffiliateContainsNewerProductEventId,
        int AffiliateNewerProductEventIdOccurrenceCount,
        int AffiliateDocumentCount,
        decimal MarketplaceReadModelPrice,
        Guid? MarketplaceReadModelLastEventId,
        int MarketplaceReadModelLastEventVersion,
        long MarketplaceReadModelLastEntityMutationVersion,
        int MarketplaceProcessedEventCount,
        int MarketplaceProjectionOutboxCount,
        int MarketplaceReadModelDocumentCount);

    private sealed class PriceCon09CartServiceScope : IAsyncDisposable
    {
        private readonly ApplicationDbContext _dbContext;

        public PriceCon09CartServiceScope(
            CartService service,
            ApplicationDbContext dbContext,
            MongoDbContext mongoDbContext)
        {
            Service = service;
            _dbContext = dbContext;
            _ = mongoDbContext;
        }

        public CartService Service { get; }

        public async ValueTask DisposeAsync()
        {
            await _dbContext.DisposeAsync();
        }
    }

    private sealed class FixedUtcTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedUtcTimeProvider(DateTime utcNow)
        {
            _utcNow = new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class InMemoryDistributedLockService : IDistributedLockService
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, string> _owners = new(StringComparer.Ordinal);

        public Task<DistributedLockResult> TryAcquireLockAsync(string lockKey, string lockValue, TimeSpan ttl)
        {
            _ = ttl;

            lock (_gate)
            {
                if (_owners.ContainsKey(lockKey))
                {
                    return Task.FromResult(DistributedLockResult.AlreadyHeld());
                }

                _owners[lockKey] = lockValue;
                return Task.FromResult(DistributedLockResult.Acquired());
            }
        }

        public Task<DistributedLockResult> RefreshLockAsync(string lockKey, string lockValue, TimeSpan ttl)
        {
            _ = ttl;

            lock (_gate)
            {
                return Task.FromResult(
                    _owners.TryGetValue(lockKey, out var owner) &&
                    string.Equals(owner, lockValue, StringComparison.Ordinal)
                        ? DistributedLockResult.Acquired()
                        : DistributedLockResult.AlreadyHeld());
            }
        }

        public Task<bool> ReleaseLockAsync(string lockKey, string lockValue)
        {
            lock (_gate)
            {
                if (_owners.TryGetValue(lockKey, out var owner) &&
                    string.Equals(owner, lockValue, StringComparison.Ordinal))
                {
                    _owners.Remove(lockKey);
                    return Task.FromResult(true);
                }

                return Task.FromResult(false);
            }
        }

        public Task<bool> IsLockedAsync(string lockKey)
        {
            lock (_gate)
            {
                return Task.FromResult(_owners.ContainsKey(lockKey));
            }
        }
    }

    private sealed class PriceMongoDatabaseScope : IAsyncDisposable
    {
        private readonly MongoClient _client;
        private readonly string _databaseName;

        public PriceMongoDatabaseScope(
            MongoClient client,
            string databaseName,
            IMongoDatabase database)
        {
            _client = client;
            _databaseName = databaseName;
            Database = database;
        }

        public IMongoDatabase Database { get; }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _client.DropDatabaseAsync(_databaseName);
            }
            catch
            {
                // Best-effort cleanup for isolated test data.
            }
        }
    }
}
