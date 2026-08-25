using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Moq;
using Npgsql;
using PointOfSale.API.Controllers;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Inventory;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Marketplace;
using MarketplaceEvents = PointOfSale.Core.Events.Marketplace;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Consumers;
using PointOfSale.Infrastructure.Consumers.Marketplace;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.Eventing;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;
using PointOfSale.Infrastructure.MongoDB.Documents.Marketplace;
using PointOfSale.Infrastructure.MongoDB.Services.Marketplace;
using PointOfSale.Infrastructure.Observability;
using PointOfSale.Infrastructure.ReadModels.Marketplace;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Tests.Infrastructure;
using StackExchange.Redis;
using Xunit.Sdk;

namespace PointOfSale.Tests.Integration;

public sealed class ProductVariantDistributedReliabilityIntegrationTests
{
    private static readonly DateTime SeededUtc = new(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions CaseInsensitiveJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Guid TenantId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c101");
    private static readonly Guid CategoryId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c102");
    private static readonly Guid ProductId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c103");
    private static readonly Guid DefaultVariantId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c104");
    private static readonly Guid WarehouseId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c105");
    private static readonly Guid MediaId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c106");
    private static readonly Guid ActorAUserId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c107");
    private static readonly Guid ActorBUserId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c108");
    private static readonly Guid CommerceHubCategoryId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c109");
    private static readonly Guid ProdCon03EventId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c110");
    private static readonly Guid VarCon02VariantId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c111");
    private static readonly Guid VarCon02StockId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c112");
    private static readonly Guid VarCon02CommerceHubDocumentId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c113");
    private static readonly Guid VarCon03EventId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c114");
    private static readonly Guid VarCon04VariantAStockId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c116");
    private static readonly Guid VarCon04VariantBStockId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c117");
    private static readonly Guid VarCon04OrderId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c118");
    private static readonly Guid VarCon04OrderItemId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c119");
    private static readonly Guid VarCon04SalesInvoiceId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c120");
    private static readonly Guid VarCon04SalesInvoiceItemId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c121");
    private static readonly Guid VarCon04StockMovementId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c122");
    private static readonly Guid VarCon04EventId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c123");
    private static readonly Guid VarCon04StaleReplayEventId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c124");
    private static readonly Guid VarCon05HistoricalStockMovementId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c125");
    private static readonly Guid VarCon05ActiveStockMovementId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c126");
    private static readonly Guid VarCon05DeleteEventId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c127");
    private static readonly Guid VarCon05StaleStockEventId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c128");
    private static readonly Guid VarCon05StaleCreateEventId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c129");
    private static readonly Guid VarCon06V10EventId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c130");
    private static readonly Guid VarCon06V11EventId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c131");
    private static readonly Guid VarCon07V10ProjectionEventId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c132");
    private static readonly Guid VarCon07V11ProjectionEventId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c133");
    private static readonly Guid VarCon07V10MarketplaceEventId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c134");
    private static readonly Guid VarCon07V11MarketplaceEventId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c135");
    private static readonly Guid VarCon07MarketplaceRepairEventId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c136");
    private static readonly Guid VarCon08ProjectionEventId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c137");
    private static readonly Guid VarCon08StaleProjectionEventId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c138");
    private static readonly Guid VarCon08RebuildEventId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c139");
    private static readonly Guid VarCon09V10MediaId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c140");
    private static readonly Guid VarCon09V11MediaId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c141");
    private static readonly Guid VarCon09V10ProjectionEventId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c142");
    private static readonly Guid VarCon09V11ProjectionEventId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c143");
    private static readonly Guid VarCon09V10MediaEventId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c144");
    private static readonly Guid VarCon09V11MediaEventId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c145");
    private static readonly Guid VarCon09RebuildEventId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c146");
    private static readonly Guid VarCon09BValidCorruptedMediaEventId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c147");
    private static readonly Guid VarCon09BStaleMediaEventId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c148");
    private static readonly Guid VarCon09BFutureMediaEventId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c149");

    private const string ContestedSku = "PROD-RED-L-001";
    private const string ContestedBarcode = "PROD-RED-L-001-BAR";
    private const string DefaultVariantSku = "PROD-CON-01-DEFAULT";
    private const decimal VariantPrice = 155m;
    private const decimal VariantOnlinePrice = 149m;
    private const decimal VariantCostPrice = 80m;
    private const decimal InitialStockQuantity = 8m;
    private const string ProdCon01ActorARequestId = "prod-con-01-actor-a";
    private const string ProdCon01ActorBRequestId = "prod-con-01-actor-b";
    private const string ProdCon02SharedRequestId = "prod-con-02-active-replay";
    private const string ProdCon03CorrelationId = "prod-con-03-product-sync";
    private const string VarCon01ActorARequestId = "var-con-01-actor-a";
    private const string VarCon01ActorBRequestId = "var-con-01-actor-b";
    private const string VarCon01ActorASku = "VAR-CON-01-RED-L-A";
    private const string VarCon01ActorBSku = "VAR-CON-01-RED-L-B";
    private const string VarCon01ActorABarcode = "VAR-CON-01-RED-L-A-BAR";
    private const string VarCon01ActorBBarcode = "VAR-CON-01-RED-L-B-BAR";
    private const string VarCon01AttributeCombinationKey = "sha256:1abc08641fb39aff5230c41801221592eb28252133282e4958fe516ecedeb65a";
    private const string VarCon02SharedRequestId = "var-con-02-active-mutation-replay";
    private const string VarCon02BaselineSku = "VAR-CON-02-RED-L";
    private const string VarCon02BaselineBarcode = "VAR-CON-02-RED-L-BAR";
    private const string VarCon02MutatedSku = "VAR-CON-02-RED-XL";
    private const string VarCon02MutatedBarcode = "VAR-CON-02-RED-XL-BAR";
    private const string VarCon03MutationRequestId = "var-con-03-authoritative-mutation";
    private const string VarCon03ConsumerName = "VAR-CON-03-VariantProjectionDeliveryWorker";
    private const string VarCon08MutationRequestId = "var-con-08-authoritative-mutation";
    private const string VarCon08ConsumerName = "VAR-CON-08-DuplicateVariantProjectionDeliveryWorker";
    private const string VarCon09V10RequestId = "var-con-09-media-v10";
    private const string VarCon09V11RequestId = "var-con-09-media-v11";
    private const string VarCon09ConsumerName = "VAR-CON-09-VariantMediaProjectionWorker";
    private const string VarCon09V10MediaUrl = "https://cdn.example.invalid/var-con-09/v10-stale.webp";
    private const string VarCon09V11MediaUrl = "https://cdn.example.invalid/var-con-09/v11-authoritative.webp";
    private const string VarCon04ActorARequestId = "var-con-04-actor-a-winning-reassignment";
    private const string VarCon04ActorBRequestId = "var-con-04-actor-b-losing-reassignment";
    private const string VarCon04BaselineSkuA = "VAR-CON-04-RED-L";
    private const string VarCon04BaselineSkuB = "VAR-CON-04-BLUE-L";
    private const string VarCon04BaselineBarcodeA = "VAR-CON-04-RED-L-BAR";
    private const string VarCon04BaselineBarcodeB = "VAR-CON-04-BLUE-L-BAR";
    private const string VarCon04ContestedSku = "VAR-CON-04-REASSIGNED-L";
    private const string VarCon04WinningBarcode = "VAR-CON-04-REASSIGNED-A-BAR";
    private const string VarCon04LosingBarcode = "VAR-CON-04-REASSIGNED-B-BAR";
    private const decimal VarCon04VariantAPrice = 155m;
    private const decimal VarCon04VariantAOnlinePrice = 149m;
    private const decimal VarCon04VariantACostPrice = 80m;
    private const decimal VarCon04VariantBPrice = 165m;
    private const decimal VarCon04VariantBOnlinePrice = 159m;
    private const decimal VarCon04VariantBCostPrice = 82m;
    private const decimal VarCon04VariantAStockQuantity = 8m;
    private const decimal VarCon04VariantBStockQuantity = 6m;
    private const decimal VarCon05InventoryDeductionQuantity = 2m;
    private const decimal VarCon02BaselinePrice = 155m;
    private const decimal VarCon02BaselineOnlinePrice = 149m;
    private const decimal VarCon02BaselineCostPrice = 80m;
    private const decimal VarCon02MutatedPrice = 166m;
    private const decimal VarCon02MutatedOnlinePrice = 159m;
    private const decimal VarCon02MutatedCostPrice = 88m;
    private const decimal VarCon02StockQuantity = 8m;
    private const string VarCon06V10RequestId = "var-con-06-mutation-v10";
    private const string VarCon06V11RequestId = "var-con-06-mutation-v11";
    private const string VarCon07V10RequestId = "var-con-07-mutation-v10";
    private const string VarCon07V11RequestId = "var-con-07-mutation-v11";
    private const string VarCon06V10Sku = "VAR-CON-06-RED-XL";
    private const string VarCon06V11Sku = "VAR-CON-06-RED-XXL";
    private const string VarCon06V10Barcode = "VAR-CON-06-RED-XL-BAR";
    private const string VarCon06V11Barcode = "VAR-CON-06-RED-XXL-BAR";
    private const decimal VarCon06V10Price = 176m;
    private const decimal VarCon06V10OnlinePrice = 169m;
    private const decimal VarCon06V10CostPrice = 91m;
    private const decimal VarCon06V11Price = 188m;
    private const decimal VarCon06V11OnlinePrice = 179m;
    private const decimal VarCon06V11CostPrice = 97m;
    private const int VarCon06BaselineProjectionVersion = 9;
    private const int VarCon06V10ProjectionVersion = 10;
    private const int VarCon06V11ProjectionVersion = 11;

    [Fact]
    public async Task PROD_CON_01_Concurrent_Product_Variant_Creation_With_Same_SKU_Converges_Safely()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);
        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");

        var progressEntries = new ConcurrentQueue<string>();
        var insertRendezvous = new VariantInsertRendezvous(
            participantCount: 2,
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var commitWindow = new VariantCommitOverlapWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);

        var actorA = new VariantActor(
            Name: "actor-a",
            RequestId: ProdCon01ActorARequestId,
            TraceIdentifier: "prod-con-01-a",
            UserId: ActorAUserId);
        var actorB = new VariantActor(
            Name: "actor-b",
            RequestId: ProdCon01ActorBRequestId,
            TraceIdentifier: "prod-con-01-b",
            UserId: ActorBUserId);

        var actorATask = ExecuteVariantActorAsync(
            connectionString,
            tenantContext,
            seeded,
            actorA,
            progressEntries,
            new ProductVariantInsertBarrierInterceptor(actorA.Name, insertRendezvous, progressEntries),
            new ProductVariantCommitPauseTransactionInterceptor(actorA.Name, commitWindow));
        var actorBTask = ExecuteVariantActorAsync(
            connectionString,
            tenantContext,
            seeded,
            actorB,
            progressEntries,
            new ProductVariantInsertBarrierInterceptor(actorB.Name, insertRendezvous, progressEntries),
            new ProductVariantCommitPauseTransactionInterceptor(actorB.Name, commitWindow));

        await commitWindow.WaitForOriginalCommitPauseAsync();

        Assert.Equal(
            2,
            insertRendezvous.ArrivalCount);

        await AssertNoUncommittedDurableStateVisibleAsync(
            scope,
            tenantContext,
            seeded,
            progressEntries);

        commitWindow.ReleaseOriginalCommit();

        VariantActorResult[] results;
        try
        {
            results = await Task.WhenAll(actorATask, actorBTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"PROD-CON-01 timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.True(
            commitWindow.OriginalCommitPaused,
            $"No authoritative product-variant transaction paused before commit. Progress: {string.Join(" | ", progressEntries)}");
        Assert.All(results, result => Assert.Null(result.Exception));

        await AssertAuthoritativeConvergenceAsync(
            scope,
            tenantContext,
            seeded,
            results,
            progressEntries);
    }

    [Fact]
    public async Task VAR_CON_01_Concurrent_Same_Attribute_Combination_Creation_Converges_Safely()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);
        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");

        var progressEntries = new ConcurrentQueue<string>();
        var insertRendezvous = new VariantInsertRendezvous(
            participantCount: 2,
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var commitWindow = new VariantCommitOverlapWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);

        var actorA = new VariantActor(
            Name: "var-con-01-actor-a",
            RequestId: VarCon01ActorARequestId,
            TraceIdentifier: "var-con-01-a",
            UserId: ActorAUserId,
            RequestedSku: VarCon01ActorASku,
            RequestedBarcode: VarCon01ActorABarcode);
        var actorB = new VariantActor(
            Name: "var-con-01-actor-b",
            RequestId: VarCon01ActorBRequestId,
            TraceIdentifier: "var-con-01-b",
            UserId: ActorBUserId,
            RequestedSku: VarCon01ActorBSku,
            RequestedBarcode: VarCon01ActorBBarcode);

        var actorATask = ExecuteVariantActorAsync(
            connectionString,
            tenantContext,
            seeded,
            actorA,
            progressEntries,
            new ProductVariantInsertBarrierInterceptor(actorA.Name, insertRendezvous, progressEntries),
            new ProductVariantCommitPauseTransactionInterceptor(actorA.Name, commitWindow));
        var actorBTask = ExecuteVariantActorAsync(
            connectionString,
            tenantContext,
            seeded,
            actorB,
            progressEntries,
            new ProductVariantInsertBarrierInterceptor(actorB.Name, insertRendezvous, progressEntries),
            new ProductVariantCommitPauseTransactionInterceptor(actorB.Name, commitWindow));

        await commitWindow.WaitForOriginalCommitPauseAsync();

        Assert.Equal(2, insertRendezvous.ArrivalCount);

        await AssertNoUncommittedVariantCombinationVisibleAsync(
            scope,
            tenantContext,
            seeded,
            progressEntries);

        commitWindow.ReleaseOriginalCommit();

        VariantActorResult[] results;
        try
        {
            results = await Task.WhenAll(actorATask, actorBTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"VAR-CON-01 timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.True(
            commitWindow.OriginalCommitPaused,
            $"No authoritative variant-combination transaction paused before commit. Progress: {string.Join(" | ", progressEntries)}");
        Assert.All(results, result => Assert.Null(result.Exception));

        await AssertVariantCombinationConvergenceAsync(
            scope,
            connectionString,
            tenantContext,
            seeded,
            results,
            progressEntries);
    }

    [Fact]
    public async Task VAR_CON_02_Replay_During_Active_Variant_Attribute_Mutation_Converges_Safely()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var mongoScope = await CreateIsolatedMongoDatabaseScopeAsync("var-con-02");
        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);

        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext);
        var varCon02Seeded = await SeedVarCon02ScenarioAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");

        var progressEntries = new ConcurrentQueue<string>();
        var replayWindow = new ProductVariantActiveTransactionReplayWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var originalActor = new VariantActor(
            Name: "var-con-02-request-a-original",
            RequestId: VarCon02SharedRequestId,
            TraceIdentifier: "var-con-02-original-active",
            UserId: ActorAUserId);
        var replayActor = new VariantActor(
            Name: "var-con-02-request-b-replay",
            RequestId: VarCon02SharedRequestId,
            TraceIdentifier: "var-con-02-overlapping-replay",
            UserId: ActorBUserId);

        var originalTask = ExecuteVariantUpdateActorAsync(
            connectionString,
            tenantContext,
            varCon02Seeded,
            originalActor,
            progressEntries,
            new ProductVariantActiveReplayCommitPauseTransactionInterceptor(originalActor.Name, replayWindow));

        await replayWindow.WaitForOriginalCommitPauseAsync();
        await AssertVarCon02ActiveOverlapVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon02Seeded,
            progressEntries);

        var replayTask = ExecuteVariantUpdateActorAsync(
            connectionString,
            tenantContext,
            varCon02Seeded,
            replayActor,
            progressEntries,
            new ProductVariantReplayClaimAttemptInterceptor(replayActor.Name, replayWindow));

        await replayWindow.WaitForReplayClaimAttemptAsync();
        Assert.True(
            replayWindow.OriginalWasPausedBeforeReplayClaim,
            $"VAR-CON-02 replay did not overlap the active variant mutation transaction. Progress: {string.Join(" | ", progressEntries)}");

        await AssertVarCon02ActiveOverlapVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon02Seeded,
            progressEntries);

        replayWindow.ReleaseOriginalCommit();

        VariantActorResult[] results;
        try
        {
            results = await Task.WhenAll(originalTask, replayTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"VAR-CON-02 timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.True(
            replayWindow.OriginalCommitPaused,
            $"Request A never paused before VAR-CON-02 commit. Progress: {string.Join(" | ", progressEntries)}");
        Assert.True(
            replayWindow.ReplayClaimAttempted,
            $"Request B never attempted the overlapping variant-update idempotency claim. Progress: {string.Join(" | ", progressEntries)}");
        Assert.All(results, result => Assert.Null(result.Exception));

        await RebuildVarCon02VisibilityFromSqlAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon02Seeded,
            progressEntries);
        await RebuildVarCon02VisibilityFromSqlAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon02Seeded,
            progressEntries);

        await AssertVarCon02ConvergenceAsync(
            scope,
            connectionString,
            tenantContext,
            mongoScope.Database,
            varCon02Seeded,
            originalActor,
            replayActor,
            results,
            progressEntries);
    }

    [Fact]
    public async Task VAR_CON_03_Duplicate_Variant_Projection_Delivery_During_Active_Processing_Converges_Safely()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var mongoScope = await CreateIsolatedMongoDatabaseScopeAsync("var-con-03");
        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);

        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext);
        var varCon03Seeded = await SeedVarCon02ScenarioAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");

        var progressEntries = new ConcurrentQueue<string>();
        var baselineVisibility = await CaptureVarCon03VisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon03Seeded);

        var mutationActor = new VariantActor(
            Name: "var-con-03-authoritative-sql-mutation",
            RequestId: VarCon03MutationRequestId,
            TraceIdentifier: "var-con-03-sql-mutation",
            UserId: ActorAUserId);
        var mutationResult = await ExecuteVariantUpdateActorAsync(
            connectionString,
            tenantContext,
            varCon03Seeded,
            mutationActor,
            progressEntries);

        Assert.Null(mutationResult.Exception);
        Assert.True(mutationResult.Succeeded, mutationResult.PayloadJson);

        await AssertVarCon03SqlMutatedButProjectionBaselineAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon03Seeded,
            baselineVisibility,
            progressEntries);

        var projectionEvent = await BuildVarCon03VariantProjectionEventFromSqlAsync(
            scope,
            tenantContext,
            varCon03Seeded,
            VarCon03EventId,
            "var-con-03-duplicate-delivery");
        var staleProjectionEvent = BuildVarCon03StaleBaselineProjectionEvent(
            varCon03Seeded,
            projectionEvent);

        var overlapWindow = new VariantProjectionDeliveryOverlapWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var workerA = new VariantProjectionWorker("variant-projection-worker-a");
        var workerB = new VariantProjectionWorker("variant-projection-worker-b");

        var workerATask = ExecuteVarCon03ProjectionWorkerAsync(
            connectionString,
            tenantContext,
            scope,
            mongoScope.Database,
            varCon03Seeded,
            projectionEvent,
            workerA,
            overlapWindow,
            pauseBeforeProjectionCompletion: true,
            progressEntries);

        await overlapWindow.WaitForOriginalPauseAsync();
        await AssertVarCon03ActiveOverlapVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon03Seeded,
            baselineVisibility,
            progressEntries);

        var workerBTask = ExecuteVarCon03ProjectionWorkerAsync(
            connectionString,
            tenantContext,
            scope,
            mongoScope.Database,
            varCon03Seeded,
            projectionEvent,
            workerB,
            overlapWindow,
            pauseBeforeProjectionCompletion: false,
            progressEntries);

        await overlapWindow.WaitForDuplicateClaimAttemptAsync();
        Assert.True(
            overlapWindow.OriginalPauseObservedBeforeDuplicateClaimAttempt,
            $"Duplicate variant projection delivery did not overlap the active worker. Progress: {string.Join(" | ", progressEntries)}");

        await AssertVarCon03ActiveOverlapVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon03Seeded,
            baselineVisibility,
            progressEntries);

        overlapWindow.ReleaseOriginalWorker();

        VariantProjectionWorkerResult[] results;
        try
        {
            results = await Task.WhenAll(workerATask, workerBTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"VAR-CON-03 duplicate projection overlap timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.All(results, result => Assert.Null(result.Exception));

        await AssertVarCon03ConvergenceAsync(
            scope,
            connectionString,
            tenantContext,
            mongoScope.Database,
            varCon03Seeded,
            projectionEvent,
            staleProjectionEvent,
            baselineVisibility,
            results,
            progressEntries);
    }

    [Fact]
    public async Task VAR_CON_04_Concurrent_SKU_Reassignment_Race_Converges_Safely()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var mongoScope = await CreateIsolatedMongoDatabaseScopeAsync("var-con-04");
        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);

        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext);
        var varCon04Seeded = await SeedVarCon04ScenarioAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");

        var progressEntries = new ConcurrentQueue<string>();
        var overlapWindow = new SkuReassignmentOverlapWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var actorA = new VariantActor(
            Name: "var-con-04-request-a-winning-reassignment",
            RequestId: VarCon04ActorARequestId,
            TraceIdentifier: "var-con-04-winning-reassignment",
            UserId: ActorAUserId);
        var actorB = new VariantActor(
            Name: "var-con-04-request-b-losing-reassignment",
            RequestId: VarCon04ActorBRequestId,
            TraceIdentifier: "var-con-04-losing-reassignment",
            UserId: ActorBUserId);

        var actorATask = ExecuteVariantUpdateActorAsync(
            connectionString,
            tenantContext,
            varCon04Seeded,
            varCon04Seeded.VariantAId,
            BuildVarCon04UpdateRequest(varCon04Seeded, winningVariant: true),
            actorA,
            progressEntries,
            new SkuReassignmentCommitPauseTransactionInterceptor(actorA.Name, overlapWindow));

        var actorBTask = ExecuteVariantUpdateActorAsync(
            connectionString,
            tenantContext,
            varCon04Seeded,
            varCon04Seeded.VariantBId,
            BuildVarCon04UpdateRequest(varCon04Seeded, winningVariant: false),
            actorB,
            progressEntries,
            new SkuReassignmentUpdateAttemptInterceptor(actorB.Name, overlapWindow, progressEntries));

        await overlapWindow.WaitForOriginalCommitPauseAsync();
        await overlapWindow.WaitForConflictingUpdateAttemptAsync();

        Assert.True(
            overlapWindow.OriginalPauseObservedBeforeConflictingUpdateAttempt,
            $"VAR-CON-04 did not force the conflicting SKU writer to overlap the active transaction. Progress: {string.Join(" | ", progressEntries)}");

        await AssertVarCon04ActiveOverlapVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon04Seeded,
            progressEntries);

        overlapWindow.ReleaseOriginalCommit();

        VariantActorResult[] results;
        try
        {
            results = await Task.WhenAll(actorATask, actorBTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"VAR-CON-04 SKU reassignment overlap timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.True(
            overlapWindow.OriginalCommitPaused,
            $"Request A never paused before VAR-CON-04 commit. Progress: {string.Join(" | ", progressEntries)}");
        Assert.True(
            overlapWindow.ConflictingUpdateAttempted,
            $"Request B never attempted the overlapping SKU reassignment. Progress: {string.Join(" | ", progressEntries)}");
        Assert.All(results, result => Assert.Null(result.Exception));

        await RebuildVarCon02VisibilityFromSqlAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon04Seeded.ToVisibilityScenario(),
            progressEntries);
        await RebuildVarCon02VisibilityFromSqlAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon04Seeded.ToVisibilityScenario(),
            progressEntries);

        await AssertVarCon04ConvergenceAsync(
            scope,
            connectionString,
            tenantContext,
            mongoScope.Database,
            varCon04Seeded,
            actorA,
            actorB,
            results,
            progressEntries);
    }

    [Fact]
    public async Task VAR_CON_05_Variant_Delete_During_Active_Inventory_Mutation_Converges_Safely()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var mongoScope = await CreateIsolatedMongoDatabaseScopeAsync("var-con-05");
        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);

        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext);
        var varCon05Seeded = await SeedVarCon05ScenarioAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");

        var progressEntries = new ConcurrentQueue<string>();
        var overlapWindow = new VariantDeleteInventoryOverlapWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);

        var inventoryTask = ExecuteVarCon05InventoryMutationAsync(
            connectionString,
            tenantContext,
            varCon05Seeded,
            overlapWindow,
            progressEntries);

        await overlapWindow.WaitForInventoryMutationPauseAsync();

        var deleteActor = new VariantActor(
            Name: "var-con-05-request-b-delete",
            RequestId: "var-con-05-delete-request",
            TraceIdentifier: "var-con-05-delete",
            UserId: ActorBUserId);
        var deleteTask = ExecuteVarCon05DeleteActorAsync(
            connectionString,
            tenantContext,
            varCon05Seeded,
            deleteActor,
            overlapWindow,
            progressEntries);

        await overlapWindow.WaitForDeleteCommitPauseAsync();

        Assert.True(
            overlapWindow.InventoryPausedBeforeDeleteCommit,
            $"VAR-CON-05 did not force deletion to overlap the active inventory transaction. Progress: {string.Join(" | ", progressEntries)}");

        await AssertVarCon05ActiveOverlapVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon05Seeded,
            progressEntries);

        overlapWindow.ReleaseDeleteCommit();

        var deleteResult = await deleteTask.WaitAsync(TimeSpan.FromSeconds(120));
        Assert.Null(deleteResult.Exception);
        Assert.True(deleteResult.Succeeded, deleteResult.PayloadJson ?? "VAR-CON-05 deletion did not commit.");
        Assert.Equal(StatusCodes.Status204NoContent, deleteResult.StatusCode);

        overlapWindow.ReleaseInventoryMutation();

        var inventoryResult = await inventoryTask.WaitAsync(TimeSpan.FromSeconds(120));
        Assert.False(inventoryResult.Succeeded, "VAR-CON-05 inventory mutation unexpectedly committed after the variant was deleted.");
        Assert.NotNull(inventoryResult.Exception);

        await ApplyVarCon05DeleteProjectionEventAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon05Seeded);

        await RebuildVarCon02VisibilityFromSqlAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon05Seeded.ToVisibilityScenario(),
            progressEntries);
        await RebuildVarCon02VisibilityFromSqlAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon05Seeded.ToVisibilityScenario(),
            progressEntries);

        await AssertVarCon05ConvergenceAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon05Seeded,
            progressEntries);
    }

    [Fact]
    public async Task VAR_CON_06_Out_Of_Order_Variant_Projection_Propagation_Rejects_Stale_Replay()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var mongoScope = await CreateIsolatedMongoDatabaseScopeAsync("var-con-06");
        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);

        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext);
        var varCon06Seeded = await SeedVarCon02ScenarioAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded);
        await SetVarCon06BaselineProjectionVersionAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon06Seeded);

        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");
        var progressEntries = new ConcurrentQueue<string>();

        var v10Actor = new VariantActor(
            Name: "var-con-06-authoritative-v10",
            RequestId: VarCon06V10RequestId,
            TraceIdentifier: "var-con-06-v10",
            UserId: ActorAUserId);
        var v10Result = await ExecuteVariantUpdateActorAsync(
            connectionString,
            tenantContext,
            varCon06Seeded,
            BuildVarCon06UpdateRequest(
                VarCon06V10Sku,
                VarCon06V10Barcode,
                "XL",
                VarCon06V10Price,
                VarCon06V10OnlinePrice,
                VarCon06V10CostPrice),
            v10Actor,
            progressEntries);

        Assert.Null(v10Result.Exception);
        Assert.True(v10Result.Succeeded, v10Result.PayloadJson ?? "VAR-CON-06 V10 mutation did not commit.");

        var v10ProjectionEvent = await BuildVarCon03VariantProjectionEventFromSqlAsync(
            scope,
            tenantContext,
            varCon06Seeded,
            VarCon06V10EventId,
            "var-con-06-v10-stale-replay");
        Assert.Equal(VarCon06V10ProjectionVersion, v10ProjectionEvent.EventVersion);

        var v11Actor = new VariantActor(
            Name: "var-con-06-authoritative-v11",
            RequestId: VarCon06V11RequestId,
            TraceIdentifier: "var-con-06-v11",
            UserId: ActorBUserId);
        var v11Result = await ExecuteVariantUpdateActorAsync(
            connectionString,
            tenantContext,
            varCon06Seeded,
            BuildVarCon06UpdateRequest(
                VarCon06V11Sku,
                VarCon06V11Barcode,
                "XXL",
                VarCon06V11Price,
                VarCon06V11OnlinePrice,
                VarCon06V11CostPrice),
            v11Actor,
            progressEntries);

        Assert.Null(v11Result.Exception);
        Assert.True(v11Result.Succeeded, v11Result.PayloadJson ?? "VAR-CON-06 V11 mutation did not commit.");

        var v11ProjectionEvent = await BuildVarCon03VariantProjectionEventFromSqlAsync(
            scope,
            tenantContext,
            varCon06Seeded,
            VarCon06V11EventId,
            "var-con-06-v11-first-delivery");
        Assert.Equal(VarCon06V11ProjectionVersion, v11ProjectionEvent.EventVersion);

        await AssertVarCon06SqlTruthAsync(
            scope,
            tenantContext,
            varCon06Seeded,
            BuildVarCon06ExpectedState(v11ProjectionEvent));

        var v11Delivery = await ExecuteVarCon06ProjectionDeliveryAsync(
            connectionString,
            tenantContext,
            scope,
            mongoScope.Database,
            varCon06Seeded,
            v11ProjectionEvent,
            new VariantProjectionWorker("var-con-06-v11-first-delivery"),
            progressEntries);
        Assert.Null(v11Delivery.WorkerResult.Exception);
        Assert.Equal(VariantProjectionWorkerOutcome.Processed, v11Delivery.WorkerResult.Outcome);
        Assert.True(v11Delivery.SideEffects.CacheKeyDeleteCount > 0);

        await AssertVarCon06VisibilityConvergedAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon06Seeded,
            BuildVarCon06ExpectedState(v11ProjectionEvent),
            progressEntries);

        var duplicateV11Delivery = await ExecuteVarCon06ProjectionDeliveryAsync(
            connectionString,
            tenantContext,
            scope,
            mongoScope.Database,
            varCon06Seeded,
            v11ProjectionEvent,
            new VariantProjectionWorker("var-con-06-v11-duplicate-delivery"),
            progressEntries);
        Assert.Null(duplicateV11Delivery.WorkerResult.Exception);
        Assert.Equal(VariantProjectionWorkerOutcome.DuplicateSkipped, duplicateV11Delivery.WorkerResult.Outcome);
        Assert.Equal(0, duplicateV11Delivery.SideEffects.CacheKeyDeleteCount);

        var beforeStaleReplay = await CaptureVarCon03VisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon06Seeded);
        var staleV10SideEffects = await ApplyVarCon06StorefrontVariantProjectionEventAsync(
            mongoScope.Database,
            v10ProjectionEvent);
        var afterStaleReplay = await CaptureVarCon03VisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon06Seeded);

        Assert.Equal(beforeStaleReplay, afterStaleReplay);
        Assert.Equal(0, staleV10SideEffects.CacheKeyDeleteCount);
        Assert.Equal(0, staleV10SideEffects.SearchIndexCount);

        await using (var processedContext = scope.CreateDbContext(tenantContext))
        {
            Assert.Equal(1, await CountVarCon03ProcessedEventsAsync(processedContext, VarCon06V11EventId));
            Assert.Equal(0, await CountVarCon03ProcessedEventsAsync(processedContext, VarCon06V10EventId));
        }

        await RebuildVarCon02VisibilityFromSqlAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon06Seeded,
            progressEntries);
        await RebuildVarCon02VisibilityFromSqlAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon06Seeded,
            progressEntries);

        await AssertVarCon06VisibilityConvergedAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon06Seeded,
            BuildVarCon06ExpectedState(v11ProjectionEvent),
            progressEntries);

        progressEntries.Enqueue("verification:var-con-06-out-of-order-v11-wins-stale-v10-failed-closed");
    }

    [Fact]
    public async Task VAR_CON_07_Stale_Marketplace_Variant_Replay_Fails_Closed_After_Newer_SQL_Truth_Propagates()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var mongoScope = await CreateIsolatedMongoDatabaseScopeAsync("var-con-07");
        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);

        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext);
        var varCon07Seeded = await SeedVarCon02ScenarioAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded);
        await SetVarCon06BaselineProjectionVersionAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon07Seeded);

        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");
        var progressEntries = new ConcurrentQueue<string>();

        var v10Result = await ExecuteVariantUpdateActorAsync(
            connectionString,
            tenantContext,
            varCon07Seeded,
            BuildVarCon06UpdateRequest(
                VarCon06V10Sku,
                VarCon06V10Barcode,
                "XL",
                VarCon06V10Price,
                VarCon06V10OnlinePrice,
                VarCon06V10CostPrice),
            new VariantActor(
                Name: "var-con-07-authoritative-v10",
                RequestId: VarCon07V10RequestId,
                TraceIdentifier: "var-con-07-v10",
                UserId: ActorAUserId),
            progressEntries);

        Assert.Null(v10Result.Exception);
        Assert.True(v10Result.Succeeded, v10Result.PayloadJson ?? "VAR-CON-07 V10 mutation did not commit.");

        var v10ProjectionEvent = await BuildVarCon03VariantProjectionEventFromSqlAsync(
            scope,
            tenantContext,
            varCon07Seeded,
            VarCon07V10ProjectionEventId,
            "var-con-07-v10-stale-marketplace-source");
        Assert.Equal(VarCon06V10ProjectionVersion, v10ProjectionEvent.EventVersion);

        var staleMarketplaceV10 = BuildVarCon07MarketplaceProductEvent(
            varCon07Seeded,
            v10ProjectionEvent,
            VarCon07V10MarketplaceEventId,
            "var-con-07-external-stale-v10-replay");

        var v11Result = await ExecuteVariantUpdateActorAsync(
            connectionString,
            tenantContext,
            varCon07Seeded,
            BuildVarCon06UpdateRequest(
                VarCon06V11Sku,
                VarCon06V11Barcode,
                "XXL",
                VarCon06V11Price,
                VarCon06V11OnlinePrice,
                VarCon06V11CostPrice),
            new VariantActor(
                Name: "var-con-07-authoritative-v11",
                RequestId: VarCon07V11RequestId,
                TraceIdentifier: "var-con-07-v11",
                UserId: ActorBUserId),
            progressEntries);

        Assert.Null(v11Result.Exception);
        Assert.True(v11Result.Succeeded, v11Result.PayloadJson ?? "VAR-CON-07 V11 mutation did not commit.");

        var v11ProjectionEvent = await BuildVarCon03VariantProjectionEventFromSqlAsync(
            scope,
            tenantContext,
            varCon07Seeded,
            VarCon07V11ProjectionEventId,
            "var-con-07-v11-first-delivery");
        Assert.Equal(VarCon06V11ProjectionVersion, v11ProjectionEvent.EventVersion);

        var expectedV11 = BuildVarCon06ExpectedState(v11ProjectionEvent);
        await AssertVarCon07SqlTruthAsync(
            scope,
            tenantContext,
            varCon07Seeded,
            expectedV11);

        var v11Delivery = await ExecuteVarCon06ProjectionDeliveryAsync(
            connectionString,
            tenantContext,
            scope,
            mongoScope.Database,
            varCon07Seeded,
            v11ProjectionEvent,
            new VariantProjectionWorker("var-con-07-v11-storefront-affiliate-commercehub-delivery"),
            progressEntries);
        Assert.Null(v11Delivery.WorkerResult.Exception);
        Assert.Equal(VariantProjectionWorkerOutcome.Processed, v11Delivery.WorkerResult.Outcome);
        Assert.True(v11Delivery.SideEffects.CacheKeyDeleteCount > 0);

        await AssertVarCon07DerivedVisibilityConvergedAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon07Seeded,
            expectedV11,
            progressEntries);

        var freshMarketplaceV11 = BuildVarCon07MarketplaceProductEvent(
            varCon07Seeded,
            v11ProjectionEvent,
            VarCon07V11MarketplaceEventId,
            "var-con-07-marketplace-v11-first-delivery");
        var v11MarketplaceDelivery = await ExecuteProductSyncWorkerAsync(
            connectionString,
            tenantContext,
            mongoScope.Database,
            freshMarketplaceV11,
            new ProductSyncWorker("var-con-07-marketplace-v11-first-delivery"),
            progressEntries);
        Assert.Null(v11MarketplaceDelivery.Exception);

        await AssertVarCon07MarketplaceReadModelConvergedAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon07Seeded,
            expectedV11,
            freshMarketplaceV11,
            progressEntries);

        var beforeStaleMarketplaceReplay = await CaptureVarCon07VisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon07Seeded);

        var staleMarketplaceReplay = await ExecuteProductSyncWorkerAsync(
            connectionString,
            tenantContext,
            mongoScope.Database,
            staleMarketplaceV10,
            new ProductSyncWorker("var-con-07-marketplace-stale-v10-replay"),
            progressEntries);
        Assert.Null(staleMarketplaceReplay.Exception);

        var afterStaleMarketplaceReplay = await CaptureVarCon07VisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon07Seeded);
        Assert.Equal(beforeStaleMarketplaceReplay, afterStaleMarketplaceReplay);

        var duplicateStaleMarketplaceReplay = await ExecuteProductSyncWorkerAsync(
            connectionString,
            tenantContext,
            mongoScope.Database,
            staleMarketplaceV10,
            new ProductSyncWorker("var-con-07-marketplace-stale-v10-duplicate-replay"),
            progressEntries);
        Assert.Null(duplicateStaleMarketplaceReplay.Exception);
        Assert.Equal(afterStaleMarketplaceReplay, await CaptureVarCon07VisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon07Seeded));

        await using (var processedContext = scope.CreateDbContext(tenantContext))
        {
            Assert.Equal(1, await CountMarketplaceProductProcessedEventsAsync(processedContext, VarCon07V11MarketplaceEventId));
            Assert.Equal(1, await CountMarketplaceProductProcessedEventsAsync(processedContext, VarCon07V10MarketplaceEventId));
            Assert.Equal(0, await CountVarCon03ProcessedEventsAsync(processedContext, VarCon07V10ProjectionEventId));
            Assert.Equal(1, await CountVarCon03ProcessedEventsAsync(processedContext, VarCon07V11ProjectionEventId));
        }

        await RebuildVarCon02VisibilityFromSqlAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon07Seeded,
            progressEntries);
        await RebuildVarCon07MarketplaceReadModelFromSqlAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon07Seeded,
            VarCon07MarketplaceRepairEventId,
            expectedV11.EventVersion,
            progressEntries);

        await AssertVarCon07VisibilityConvergedAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon07Seeded,
            expectedV11,
            progressEntries);

        progressEntries.Enqueue("verification:var-con-07-stale-external-marketplace-replay-failed-closed");
    }

    [Fact]
    public async Task VAR_CON_08_Duplicate_Variant_Projection_Delivery_During_Active_Processing_Is_Visibility_Safe()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var mongoScope = await CreateIsolatedMongoDatabaseScopeAsync("var-con-08");
        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);

        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext);
        var varCon08Seeded = await SeedVarCon02ScenarioAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");

        var progressEntries = new ConcurrentQueue<string>();
        var baselineVisibility = await CaptureVarCon08VisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon08Seeded,
            VarCon08ProjectionEventId);

        var mutationResult = await ExecuteVariantUpdateActorAsync(
            connectionString,
            tenantContext,
            varCon08Seeded,
            new VariantActor(
                Name: "var-con-08-authoritative-sql-mutation",
                RequestId: VarCon08MutationRequestId,
                TraceIdentifier: "var-con-08-sql-mutation",
                UserId: ActorAUserId),
            progressEntries);

        Assert.Null(mutationResult.Exception);
        Assert.True(mutationResult.Succeeded, mutationResult.PayloadJson ?? "VAR-CON-08 authoritative variant mutation did not commit.");

        await AssertVarCon03SqlMutatedButProjectionBaselineAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon08Seeded,
            baselineVisibility.VariantVisibility,
            progressEntries);

        var projectionEvent = await BuildVarCon03VariantProjectionEventFromSqlAsync(
            scope,
            tenantContext,
            varCon08Seeded,
            VarCon08ProjectionEventId,
            "var-con-08-duplicate-delivery");
        var staleProjectionEvent = BuildVarCon03StaleBaselineProjectionEvent(
            varCon08Seeded,
            projectionEvent,
            VarCon08StaleProjectionEventId,
            "var-con-08-stale-duplicate-replay",
            $"var-con-08-stale-duplicate-replay:{varCon08Seeded.VariantId}");
        var expected = BuildVarCon06ExpectedState(projectionEvent);

        var overlapWindow = new VariantProjectionDeliveryOverlapWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var workerA = new VariantProjectionWorker("var-con-08-worker-a");
        var workerB = new VariantProjectionWorker("var-con-08-worker-b-duplicate");

        var workerATask = ExecuteVarCon08ProjectionWorkerAsync(
            connectionString,
            tenantContext,
            scope,
            mongoScope.Database,
            varCon08Seeded,
            projectionEvent,
            workerA,
            overlapWindow,
            pauseBeforeProjectionCompletion: true,
            progressEntries);

        await overlapWindow.WaitForOriginalPauseAsync();

        await AssertVarCon08ActiveOverlapVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon08Seeded,
            baselineVisibility,
            progressEntries);

        var workerBTask = ExecuteVarCon08ProjectionWorkerAsync(
            connectionString,
            tenantContext,
            scope,
            mongoScope.Database,
            varCon08Seeded,
            projectionEvent,
            workerB,
            overlapWindow,
            pauseBeforeProjectionCompletion: false,
            progressEntries);

        await overlapWindow.WaitForDuplicateClaimAttemptAsync();
        Assert.True(
            overlapWindow.OriginalPauseObservedBeforeDuplicateClaimAttempt,
            $"VAR-CON-08 duplicate projection delivery did not overlap active processing. Progress: {string.Join(" | ", progressEntries)}");

        await AssertVarCon08ActiveOverlapVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon08Seeded,
            baselineVisibility,
            progressEntries);

        overlapWindow.ReleaseOriginalWorker();

        VarCon08ProjectionWorkerResult[] results;
        try
        {
            results = await Task.WhenAll(workerATask, workerBTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"VAR-CON-08 duplicate projection overlap timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.All(results, result => Assert.Null(result.WorkerResult.Exception));

        await AssertVarCon08ConvergenceAsync(
            scope,
            connectionString,
            tenantContext,
            mongoScope.Database,
            varCon08Seeded,
            projectionEvent,
            staleProjectionEvent,
            expected,
            baselineVisibility,
            results,
            progressEntries);
    }

    [Fact]
    public async Task VAR_CON_09_Variant_Media_Synchronization_Corruption_Converges_From_SQL_Truth()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var mongoScope = await CreateIsolatedMongoDatabaseScopeAsync("var-con-09");
        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);

        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext);
        var varCon09Seeded = await SeedVarCon02ScenarioAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded);
        await SetVarCon06BaselineProjectionVersionAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon09Seeded);
        await RebuildVarCon09MediaVisibilityFromSqlAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon09Seeded,
            new ConcurrentQueue<string>());

        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");
        var progressEntries = new ConcurrentQueue<string>();
        var baselineVisibility = await CaptureVarCon09MediaVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon09Seeded,
            VarCon09V10ProjectionEventId);
        await SeedVarCon09MediaRowsAsync(scope, tenantContext, varCon09Seeded);

        var v10Result = await ExecuteVariantUpdateActorAsync(
            connectionString,
            tenantContext,
            varCon09Seeded,
            BuildVarCon09MediaUpdateRequest(VarCon09V10MediaId),
            new VariantActor(
                Name: "var-con-09-authoritative-media-v10",
                RequestId: VarCon09V10RequestId,
                TraceIdentifier: "var-con-09-v10",
                UserId: ActorAUserId),
            progressEntries);

        Assert.Null(v10Result.Exception);
        Assert.True(v10Result.Succeeded, v10Result.PayloadJson ?? "VAR-CON-09 V10 media mutation did not commit.");

        var v10ProjectionEvent = await BuildVarCon09VariantMediaProjectionEventFromSqlAsync(
            scope,
            tenantContext,
            varCon09Seeded,
            VarCon09V10ProjectionEventId,
            "var-con-09-v10-stale-media-replay");
        var v10MediaEvent = BuildVarCon09ProductMediaChangedEvent(
            varCon09Seeded,
            VarCon09V10MediaEventId,
            "var-con-09-v10-gallery-replay",
            v10ProjectionEvent.OccurredAtUtc,
            v10ProjectionEvent.EventVersion,
            [(VarCon09V10MediaId, VarCon09V10MediaUrl, 1)]);

        var v11Result = await ExecuteVariantUpdateActorAsync(
            connectionString,
            tenantContext,
            varCon09Seeded,
            BuildVarCon09MediaUpdateRequest(VarCon09V11MediaId),
            new VariantActor(
                Name: "var-con-09-authoritative-media-v11",
                RequestId: VarCon09V11RequestId,
                TraceIdentifier: "var-con-09-v11",
                UserId: ActorBUserId),
            progressEntries);

        Assert.Null(v11Result.Exception);
        Assert.True(v11Result.Succeeded, v11Result.PayloadJson ?? "VAR-CON-09 V11 media mutation did not commit.");

        await DeleteVarCon09NonWinnerMediaRowsAsync(scope, tenantContext);

        var v11ProjectionEvent = await BuildVarCon09VariantMediaProjectionEventFromSqlAsync(
            scope,
            tenantContext,
            varCon09Seeded,
            VarCon09V11ProjectionEventId,
            "var-con-09-v11-first-media-delivery");
        var v11MediaEvent = BuildVarCon09ProductMediaChangedEvent(
            varCon09Seeded,
            VarCon09V11MediaEventId,
            "var-con-09-v11-gallery-delivery",
            v11ProjectionEvent.OccurredAtUtc,
            v11ProjectionEvent.EventVersion,
            [(VarCon09V11MediaId, VarCon09V11MediaUrl, 1)]);

        Assert.Equal(10, v10ProjectionEvent.EventVersion);
        Assert.Equal(11, v11ProjectionEvent.EventVersion);

        await AssertVarCon09SqlMediaTruthAsync(
            scope,
            tenantContext,
            varCon09Seeded,
            expectedMediaId: VarCon09V11MediaId,
            expectedMediaUrl: VarCon09V11MediaUrl);

        var overlapWindow = new VariantProjectionDeliveryOverlapWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var workerA = new VariantProjectionWorker("var-con-09-worker-a-v11");
        var workerB = new VariantProjectionWorker("var-con-09-worker-b-duplicate-v11");

        var workerATask = ExecuteVarCon09MediaProjectionWorkerAsync(
            connectionString,
            tenantContext,
            scope,
            mongoScope.Database,
            varCon09Seeded,
            v11ProjectionEvent,
            workerA,
            overlapWindow,
            pauseBeforeProjectionCompletion: true,
            progressEntries);

        await overlapWindow.WaitForOriginalPauseAsync();

        await AssertVarCon09ActiveOverlapVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon09Seeded,
            baselineVisibility,
            progressEntries);

        var workerBTask = ExecuteVarCon09MediaProjectionWorkerAsync(
            connectionString,
            tenantContext,
            scope,
            mongoScope.Database,
            varCon09Seeded,
            v11ProjectionEvent,
            workerB,
            overlapWindow,
            pauseBeforeProjectionCompletion: false,
            progressEntries);

        await overlapWindow.WaitForDuplicateClaimAttemptAsync();
        Assert.True(
            overlapWindow.OriginalPauseObservedBeforeDuplicateClaimAttempt,
            $"VAR-CON-09 duplicate media delivery did not overlap active processing. Progress: {string.Join(" | ", progressEntries)}");

        await AssertVarCon09ActiveOverlapVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon09Seeded,
            baselineVisibility,
            progressEntries);

        overlapWindow.ReleaseOriginalWorker();

        VarCon08ProjectionWorkerResult[] results;
        try
        {
            results = await Task.WhenAll(workerATask, workerBTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"VAR-CON-09 duplicate media projection overlap timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.All(results, result => Assert.Null(result.WorkerResult.Exception));

        var v11MediaSideEffects = await ApplyVarCon09ProductMediaChangedEventAsync(
            mongoScope.Database,
            v11MediaEvent);
        Assert.Equal(1, v11MediaSideEffects.SearchIndexCount);

        await AssertVarCon09MediaConvergenceAsync(
            scope,
            connectionString,
            tenantContext,
            mongoScope.Database,
            varCon09Seeded,
            v10ProjectionEvent,
            v11ProjectionEvent,
            v10MediaEvent,
            v11MediaEvent,
            baselineVisibility,
            results,
            progressEntries);
    }

    [Fact]
    public async Task VAR_CON_09B_Product_Media_Consumer_Enforces_SQL_Authority_And_Duplicate_Side_Effect_Collapse()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var mongoScope = await CreateIsolatedMongoDatabaseScopeAsync("var-con-09b");
        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);
        var progressEntries = new ConcurrentQueue<string>();

        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext);
        var varCon09Seeded = await SeedVarCon02ScenarioAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded);
        await SeedVarCon09MediaRowsAsync(scope, tenantContext, varCon09Seeded);
        var currentMediaEventVersion = await PrepareVarCon09BSqlMediaAuthorityAsync(
            scope,
            tenantContext,
            varCon09Seeded);
        await RebuildVarCon09MediaVisibilityFromSqlAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon09Seeded,
            progressEntries);

        var sameVersionCorruptedPayload = BuildVarCon09ProductMediaChangedEvent(
            varCon09Seeded,
            VarCon09BValidCorruptedMediaEventId,
            "var-con-09b-valid-event-corrupted-payload",
            SeededUtc.AddMinutes(20),
            currentMediaEventVersion,
            [(VarCon09V10MediaId, "https://cdn.example.invalid/var-con-09b/corrupted-v10-resurrection.webp", 1)]);

        var firstDelivery = await ApplyVarCon09ProductMediaChangedEventWithSqlAuthorityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            sameVersionCorruptedPayload);

        Assert.Equal(1, firstDelivery.SearchIndexCount);
        Assert.Equal(1, firstDelivery.CacheInvalidationCount);
        var indexedDocument = Assert.Single(firstDelivery.IndexedDocuments);
        Assert.Equal(VarCon09V11MediaUrl, indexedDocument.ImageUrl);

        var afterFirstDelivery = await CaptureVarCon09MediaVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon09Seeded,
            sameVersionCorruptedPayload.EventId);

        Assert.Equal(VarCon09V11MediaId, afterFirstDelivery.SqlVariantMediaId);
        Assert.Equal(VarCon09V11MediaId, afterFirstDelivery.StorefrontVariantMediaId);
        Assert.Contains(VarCon09V11MediaUrl, afterFirstDelivery.StorefrontMediaFingerprint, StringComparison.Ordinal);
        Assert.DoesNotContain("corrupted-v10-resurrection", afterFirstDelivery.StorefrontMediaFingerprint, StringComparison.Ordinal);
        Assert.Equal(VarCon09V11MediaUrl, afterFirstDelivery.SearchImageUrl);

        var duplicateDelivery = await ApplyVarCon09ProductMediaChangedEventWithSqlAuthorityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            sameVersionCorruptedPayload);

        Assert.Equal(0, duplicateDelivery.SearchIndexCount);
        Assert.Equal(0, duplicateDelivery.CacheInvalidationCount);

        var staleReplay = BuildVarCon09ProductMediaChangedEvent(
            varCon09Seeded,
            VarCon09BStaleMediaEventId,
            "var-con-09b-stale-media-replay",
            SeededUtc.AddMinutes(19),
            currentMediaEventVersion - 1,
            [(VarCon09V10MediaId, VarCon09V10MediaUrl, 1)]);
        var futureCorruptedReplay = BuildVarCon09ProductMediaChangedEvent(
            varCon09Seeded,
            VarCon09BFutureMediaEventId,
            "var-con-09b-future-corrupted-media-replay",
            SeededUtc.AddMinutes(21),
            currentMediaEventVersion + 1,
            [(VarCon09V10MediaId, "https://cdn.example.invalid/var-con-09b/future-corrupted-overwrite.webp", 1)]);

        var staleReplayResult = await ApplyVarCon09ProductMediaChangedEventWithSqlAuthorityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            staleReplay);
        var futureReplayResult = await ApplyVarCon09ProductMediaChangedEventWithSqlAuthorityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            futureCorruptedReplay);

        Assert.Equal(0, staleReplayResult.SearchIndexCount);
        Assert.Equal(0, futureReplayResult.SearchIndexCount);
        Assert.Equal(0, staleReplayResult.CacheInvalidationCount);
        Assert.Equal(0, futureReplayResult.CacheInvalidationCount);

        var afterRejectedReplays = await CaptureVarCon09MediaVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            varCon09Seeded,
            sameVersionCorruptedPayload.EventId);
        Assert.Equal(afterFirstDelivery, afterRejectedReplays);

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        Assert.Equal(1, await CountProductMediaProcessedEventsAsync(verifyContext, VarCon09BValidCorruptedMediaEventId));
        Assert.Equal(1, await CountProductMediaProcessedEventsAsync(verifyContext, VarCon09BStaleMediaEventId));
        Assert.Equal(1, await CountProductMediaProcessedEventsAsync(verifyContext, VarCon09BFutureMediaEventId));
    }

    [Fact]
    public async Task PROD_CON_02_Replay_During_Active_Product_Variant_Creation_Converges_Safely()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);
        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");

        var progressEntries = new ConcurrentQueue<string>();
        var replayWindow = new ProductVariantActiveTransactionReplayWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var originalActor = new VariantActor(
            Name: "request-a-original",
            RequestId: ProdCon02SharedRequestId,
            TraceIdentifier: "prod-con-02-original-active",
            UserId: ActorAUserId);
        var replayActor = new VariantActor(
            Name: "request-b-replay",
            RequestId: ProdCon02SharedRequestId,
            TraceIdentifier: "prod-con-02-overlapping-replay",
            UserId: ActorBUserId);

        var originalTask = ExecuteVariantActorAsync(
            connectionString,
            tenantContext,
            seeded,
            originalActor,
            progressEntries,
            new ProductVariantActiveReplayCommitPauseTransactionInterceptor(originalActor.Name, replayWindow));

        await replayWindow.WaitForOriginalCommitPauseAsync();
        await AssertNoUncommittedDurableStateVisibleAsync(
            scope,
            tenantContext,
            seeded,
            progressEntries);

        var replayTask = ExecuteVariantActorAsync(
            connectionString,
            tenantContext,
            seeded,
            replayActor,
            progressEntries,
            new ProductVariantReplayClaimAttemptInterceptor(replayActor.Name, replayWindow));

        await replayWindow.WaitForReplayClaimAttemptAsync();
        Assert.True(
            replayWindow.OriginalWasPausedBeforeReplayClaim,
            $"Replay did not overlap the active product variant create transaction. Progress: {string.Join(" | ", progressEntries)}");

        replayWindow.ReleaseOriginalCommit();

        VariantActorResult[] results;
        try
        {
            results = await Task.WhenAll(originalTask, replayTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"PROD-CON-02 timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.True(
            replayWindow.OriginalCommitPaused,
            $"Request A never paused before commit. Progress: {string.Join(" | ", progressEntries)}");
        Assert.True(
            replayWindow.ReplayClaimAttempted,
            $"Request B never attempted the overlapping product idempotency claim. Progress: {string.Join(" | ", progressEntries)}");
        Assert.All(results, result => Assert.Null(result.Exception));

        await AssertActiveReplayConvergenceAsync(
            scope,
            connectionString,
            tenantContext,
            seeded,
            originalActor,
            replayActor,
            results,
            progressEntries);
    }

    [Fact]
    public async Task PROD_CON_03_Duplicate_Product_Sync_Worker_Delivery_During_Active_Processing_Converges_Safely()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var mongoScope = await CreateIsolatedMongoDatabaseScopeAsync("prod-con-03");
        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);

        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");

        var progressEntries = new ConcurrentQueue<string>();
        var canonicalActor = new VariantActor(
            Name: "prod-con-03-authoritative-create",
            RequestId: "prod-con-03-authoritative-create",
            TraceIdentifier: "prod-con-03-authoritative-create",
            UserId: ActorAUserId);

        var createResult = await ExecuteVariantActorAsync(
            connectionString,
            tenantContext,
            seeded,
            canonicalActor,
            progressEntries);

        Assert.Null(createResult.Exception);
        Assert.True(createResult.Succeeded, createResult.PayloadJson);
        Assert.NotNull(createResult.Response);

        var canonicalVariantId = createResult.Response!.Id;
        await SeedCommerceHubListingAsync(scope, tenantContext, seeded, canonicalVariantId);

        var canonicalState = await LoadProdCon03CanonicalStateAsync(
            scope,
            tenantContext,
            canonicalVariantId);
        var productSyncEvent = BuildProdCon03ProductSyncEvent(canonicalState);
        await EnqueueProdCon03MarketplaceOutboxAsync(
            scope,
            tenantContext,
            productSyncEvent,
            progressEntries);

        var outboxDispatch = await DispatchProdCon03MarketplaceOutboxAsync(
            connectionString,
            tenantContext,
            progressEntries);

        Assert.Equal(1, outboxDispatch.ProcessedCount);
        Assert.NotNull(outboxDispatch.PublishedEvent);

        var baselineVisibility = await CaptureProdCon03VisibilitySnapshotAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            canonicalVariantId,
            productSyncEvent);

        var overlapWindow = new ProductSyncActiveProcessingOverlapWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var workerA = new ProductSyncWorker("product-sync-worker-a");
        var workerB = new ProductSyncWorker("product-sync-worker-b");

        var workerATask = ExecuteProductSyncWorkerAsync(
            connectionString,
            tenantContext,
            mongoScope.Database,
            outboxDispatch.PublishedEvent!,
            workerA,
            progressEntries,
            new ProductSyncCommitPauseTransactionInterceptor(workerA.Name, overlapWindow));

        await overlapWindow.WaitForOriginalPauseAsync();
        await AssertProdCon03NoUncommittedVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            canonicalVariantId,
            productSyncEvent,
            baselineVisibility,
            progressEntries);

        var workerBTask = ExecuteProductSyncWorkerAsync(
            connectionString,
            tenantContext,
            mongoScope.Database,
            outboxDispatch.PublishedEvent!,
            workerB,
            progressEntries,
            new ProductSyncProjectionOutboxInsertAttemptInterceptor(workerB.Name, overlapWindow));

        await overlapWindow.WaitForDuplicateClaimAttemptAsync();
        Assert.True(
            overlapWindow.OriginalPauseObservedBeforeDuplicateClaimAttempt,
            $"Duplicate product-sync delivery did not overlap the active worker transaction. Progress: {string.Join(" | ", progressEntries)}");

        overlapWindow.ReleaseOriginalWorker();

        ProductSyncWorkerResult[] results;
        try
        {
            results = await Task.WhenAll(workerATask, workerBTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"PROD-CON-03 product-sync overlap timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.All(results, result => Assert.Null(result.Exception));

        await AssertProdCon03ConvergenceAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            canonicalVariantId,
            canonicalState,
            productSyncEvent,
            baselineVisibility,
            results,
            progressEntries);
    }

    private static async Task<SeededScenario> SeedScenarioAsync(ApplicationDbContext context)
    {
        await EnsureVariantIdentitySchemaAsync(context);
        await EnsureProductVariantIdempotencySchemaAsync(context);

        context.Tenants.Add(new Tenant
        {
            Id = TenantId,
            Name = "PROD-CON-01 Tenant",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });

        context.Categories.Add(new Category
        {
            Id = CategoryId,
            TenantId = TenantId,
            Name = "PROD-CON-01 Category",
            CreatedAt = SeededUtc
        });

        context.Warehouses.Add(new Warehouse
        {
            Id = WarehouseId,
            TenantId = TenantId,
            Name = "PROD-CON-01 Warehouse",
            Code = "WH-PROD-CON-01",
            IsActive = true,
            CreatedAt = SeededUtc
        });

        context.Products.Add(new Product
        {
            Id = ProductId,
            TenantId = TenantId,
            Name = "PROD-CON-01 Parent Product",
            CategoryId = CategoryId,
            PurchasePrice = 70m,
            SalePrice = 120m,
            OnlinePrice = 115m,
            PriceIncludesTax = false,
            TaxRate = 0m,
            Unit = "Piece",
            IsActive = true,
            PublishStatus = PublishStatus.Published,
            IsOnlineVisible = true,
            IsSearchable = true,
            TrackStock = false,
            HasVariants = true,
            CreatedAt = SeededUtc
        });

        context.ProductVariants.Add(new ProductVariant
        {
            Id = DefaultVariantId,
            TenantId = TenantId,
            ProductId = ProductId,
            SKU = DefaultVariantSku,
            Barcode = "PROD-CON-01-DEFAULT-BAR",
            Price = 120m,
            OnlinePrice = 115m,
            CostPrice = 70m,
            IsDefault = true,
            IsActive = true,
            CreatedAt = SeededUtc,
            StockUpdatedAtUtc = SeededUtc
        });

        context.ProductMedia.Add(new ProductMedia
        {
            Id = MediaId,
            TenantId = TenantId,
            ProductId = ProductId,
            MediaType = "image",
            Url = "https://example.invalid/prod-con-01/hero.png",
            Provider = "s3",
            Position = 1,
            CreatedAtUtc = SeededUtc
        });

        await context.SaveChangesAsync();

        var seededProduct = await context.Products.SingleAsync(product => product.Id == ProductId);
        var seededDefaultVariant = await context.ProductVariants.SingleAsync(variant => variant.Id == DefaultVariantId);

        return new SeededScenario(
            TenantId,
            ProductId,
            DefaultVariantId,
            MediaId,
            WarehouseId,
            seededProduct.ProjectionMutationVersion,
            seededDefaultVariant.ProjectionMutationVersion);
    }

    private static async Task<VarCon02Scenario> SeedVarCon02ScenarioAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        SeededScenario seeded)
    {
        var baselineCombinationKey = ProductVariantAttributeCombinationKey.Compute(
        [
            ("Color", "Red"),
            ("Size", "L")
        ]) ?? throw new InvalidOperationException("VAR-CON-02 baseline combination key was not computed.");
        var mutatedCombinationKey = ProductVariantAttributeCombinationKey.Compute(
        [
            ("Color", "Red"),
            ("Size", "XL")
        ]) ?? throw new InvalidOperationException("VAR-CON-02 mutated combination key was not computed.");

        await using (var context = scope.CreateDbContext(tenantContext))
        {
            await EnsureVarCon02OutboxSchemaAsync(context);

            context.ProductVariants.Add(new ProductVariant
            {
                Id = VarCon02VariantId,
                TenantId = seeded.TenantId,
                ProductId = seeded.ProductId,
                SKU = VarCon02BaselineSku,
                Barcode = VarCon02BaselineBarcode,
                AttributeCombinationKey = baselineCombinationKey,
                Price = VarCon02BaselinePrice,
                OnlinePrice = VarCon02BaselineOnlinePrice,
                CostPrice = VarCon02BaselineCostPrice,
                IsDefault = false,
                IsActive = true,
                MediaId = seeded.MediaId,
                SortOrder = 20,
                CreatedAt = SeededUtc,
                StockUpdatedAtUtc = SeededUtc
            });

            context.ProductVariantAttributes.AddRange(
                new ProductVariantAttribute
                {
                    Id = Guid.NewGuid(),
                    VariantId = VarCon02VariantId,
                    Name = "Color",
                    Value = "Red",
                    SortOrder = 0,
                    CreatedAt = SeededUtc
                },
                new ProductVariantAttribute
                {
                    Id = Guid.NewGuid(),
                    VariantId = VarCon02VariantId,
                    Name = "Size",
                    Value = "L",
                    SortOrder = 1,
                    CreatedAt = SeededUtc
                });

            context.ProductVariantStocks.Add(new ProductVariantStock
            {
                Id = VarCon02StockId,
                TenantId = seeded.TenantId,
                VariantId = VarCon02VariantId,
                WarehouseId = seeded.WarehouseId,
                Quantity = VarCon02StockQuantity,
                ReservedQuantity = 0m,
                DamagedQuantity = 0m,
                CreatedAt = SeededUtc
            });

            await context.SaveChangesAsync();
        }

        await SeedCommerceHubListingAsync(scope, tenantContext, seeded, VarCon02VariantId);

        var seededScenario = new VarCon02Scenario(
            seeded.TenantId,
            seeded.ProductId,
            VarCon02VariantId,
            seeded.DefaultVariantId,
            seeded.WarehouseId,
            baselineCombinationKey,
            mutatedCombinationKey);

        await RebuildVarCon02VisibilityFromSqlAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seededScenario,
            new ConcurrentQueue<string>());

        return seededScenario;
    }

    private static async Task<VarCon04Scenario> SeedVarCon04ScenarioAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        SeededScenario seeded)
    {
        var variantACombinationKey = ProductVariantAttributeCombinationKey.Compute(
        [
            ("Color", "Red"),
            ("Size", "L")
        ]) ?? throw new InvalidOperationException("VAR-CON-04 variant A combination key was not computed.");
        var variantBCombinationKey = ProductVariantAttributeCombinationKey.Compute(
        [
            ("Color", "Blue"),
            ("Size", "L")
        ]) ?? throw new InvalidOperationException("VAR-CON-04 variant B combination key was not computed.");

        await using (var context = scope.CreateDbContext(tenantContext))
        {
            await EnsureVarCon02OutboxSchemaAsync(context);

            var variantA = await context.ProductVariants.SingleAsync(variant => variant.Id == seeded.DefaultVariantId);
            variantA.SKU = VarCon04BaselineSkuA;
            variantA.Barcode = VarCon04BaselineBarcodeA;
            variantA.AttributeCombinationKey = variantACombinationKey;
            variantA.Price = VarCon04VariantAPrice;
            variantA.OnlinePrice = VarCon04VariantAOnlinePrice;
            variantA.CostPrice = VarCon04VariantACostPrice;
            variantA.SortOrder = 10;
            variantA.StockUpdatedAtUtc = SeededUtc;

            context.ProductVariantAttributes.AddRange(
                new ProductVariantAttribute
                {
                    Id = Guid.NewGuid(),
                    VariantId = seeded.DefaultVariantId,
                    Name = "Color",
                    Value = "Red",
                    SortOrder = 0,
                    CreatedAt = SeededUtc
                },
                new ProductVariantAttribute
                {
                    Id = Guid.NewGuid(),
                    VariantId = seeded.DefaultVariantId,
                    Name = "Size",
                    Value = "L",
                    SortOrder = 1,
                    CreatedAt = SeededUtc
                });

            context.ProductVariantStocks.Add(new ProductVariantStock
            {
                Id = VarCon04VariantAStockId,
                TenantId = seeded.TenantId,
                VariantId = seeded.DefaultVariantId,
                WarehouseId = seeded.WarehouseId,
                Quantity = VarCon04VariantAStockQuantity,
                ReservedQuantity = 0m,
                DamagedQuantity = 0m,
                CreatedAt = SeededUtc
            });

            context.ProductVariants.Add(new ProductVariant
            {
                Id = VarCon02VariantId,
                TenantId = seeded.TenantId,
                ProductId = seeded.ProductId,
                SKU = VarCon04BaselineSkuB,
                Barcode = VarCon04BaselineBarcodeB,
                AttributeCombinationKey = variantBCombinationKey,
                Price = VarCon04VariantBPrice,
                OnlinePrice = VarCon04VariantBOnlinePrice,
                CostPrice = VarCon04VariantBCostPrice,
                IsDefault = false,
                IsActive = true,
                MediaId = seeded.MediaId,
                SortOrder = 20,
                CreatedAt = SeededUtc,
                StockUpdatedAtUtc = SeededUtc
            });

            context.ProductVariantAttributes.AddRange(
                new ProductVariantAttribute
                {
                    Id = Guid.NewGuid(),
                    VariantId = VarCon02VariantId,
                    Name = "Color",
                    Value = "Blue",
                    SortOrder = 0,
                    CreatedAt = SeededUtc
                },
                new ProductVariantAttribute
                {
                    Id = Guid.NewGuid(),
                    VariantId = VarCon02VariantId,
                    Name = "Size",
                    Value = "L",
                    SortOrder = 1,
                    CreatedAt = SeededUtc
                });

            context.ProductVariantStocks.Add(new ProductVariantStock
            {
                Id = VarCon04VariantBStockId,
                TenantId = seeded.TenantId,
                VariantId = VarCon02VariantId,
                WarehouseId = seeded.WarehouseId,
                Quantity = VarCon04VariantBStockQuantity,
                ReservedQuantity = 0m,
                DamagedQuantity = 0m,
                CreatedAt = SeededUtc
            });

            context.SalesInvoices.Add(new SalesInvoice
            {
                Id = VarCon04SalesInvoiceId,
                TenantId = seeded.TenantId,
                InvoiceNumber = "VAR-CON-04-SALE",
                InvoiceDate = SeededUtc,
                WarehouseId = seeded.WarehouseId,
                Subtotal = VarCon04VariantAOnlinePrice,
                DiscountAmount = 0m,
                DiscountPercentage = 0m,
                TaxAmount = 0m,
                TotalAmount = VarCon04VariantAOnlinePrice,
                PaidAmount = VarCon04VariantAOnlinePrice,
                PaymentMethod = PaymentMethod.Cash,
                CreatedAt = SeededUtc
            });

            context.SalesInvoiceItems.Add(new SalesInvoiceItem
            {
                Id = VarCon04SalesInvoiceItemId,
                SalesInvoiceId = VarCon04SalesInvoiceId,
                ProductId = seeded.ProductId,
                ProductVariantId = seeded.DefaultVariantId,
                Quantity = 1m,
                ConversionFactor = 1m,
                BaseQuantity = 1m,
                UnitPrice = VarCon04VariantAOnlinePrice,
                DiscountAmount = 0m,
                TaxRate = 0m,
                TaxAmount = 0m,
                TotalAmount = VarCon04VariantAOnlinePrice,
                CostPriceSnapshot = VarCon04VariantACostPrice,
                CostAmountSnapshot = VarCon04VariantACostPrice,
                CreatedAt = SeededUtc
            });

            context.StockMovements.Add(new StockMovement
            {
                Id = VarCon04StockMovementId,
                TenantId = seeded.TenantId,
                WarehouseId = seeded.WarehouseId,
                ProductId = seeded.ProductId,
                ProductVariantId = seeded.DefaultVariantId,
                MovementType = StockMovementType.Adjustment,
                QuantityChange = 0m,
                ReservedQuantityChange = 0m,
                DamagedQuantityChange = 0m,
                QuantityAfter = VarCon04VariantAStockQuantity,
                AvailableAfter = VarCon04VariantAStockQuantity,
                ReservedAfter = 0m,
                DamagedAfter = 0m,
                ReferenceId = seeded.DefaultVariantId,
                ReferenceType = "VAR-CON-04-LinkageBaseline",
                CorrelationId = "var-con-04-baseline",
                IdempotencyKey = "var-con-04-baseline",
                Source = "VAR-CON-04",
                CreatedAt = SeededUtc
            });

            await context.SaveChangesAsync();

            await EnsureVarCon04FulfillmentLinkageSchemaAsync(context);
            await InsertVarCon04FulfillmentLinkageAsync(context, seeded);
        }

        await SeedCommerceHubListingAsync(scope, tenantContext, seeded, seeded.DefaultVariantId);

        var seededScenario = new VarCon04Scenario(
            seeded.TenantId,
            seeded.ProductId,
            seeded.DefaultVariantId,
            VarCon02VariantId,
            seeded.WarehouseId,
            variantACombinationKey,
            variantBCombinationKey);

        await RebuildVarCon02VisibilityFromSqlAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seededScenario.ToVisibilityScenario(),
            new ConcurrentQueue<string>());

        return seededScenario;
    }

    private static async Task<VarCon04Scenario> SeedVarCon05ScenarioAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        SeededScenario seeded)
    {
        var scenario = await SeedVarCon04ScenarioAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded);

        await using var context = scope.CreateDbContext(tenantContext);
        context.StockMovements.Add(new StockMovement
        {
            Id = VarCon05HistoricalStockMovementId,
            TenantId = seeded.TenantId,
            WarehouseId = seeded.WarehouseId,
            ProductId = seeded.ProductId,
            ProductVariantId = scenario.VariantBId,
            MovementType = StockMovementType.Adjustment,
            QuantityChange = 0m,
            ReservedQuantityChange = 0m,
            DamagedQuantityChange = 0m,
            QuantityAfter = VarCon04VariantBStockQuantity,
            AvailableAfter = VarCon04VariantBStockQuantity,
            ReservedAfter = 0m,
            DamagedAfter = 0m,
            ReferenceId = scenario.VariantBId,
            ReferenceType = "VAR-CON-05-HistoricalMovementBaseline",
            CorrelationId = "var-con-05-historical-baseline",
            IdempotencyKey = "var-con-05-historical-baseline",
            Source = "VAR-CON-05",
            CreatedAt = SeededUtc
        });
        await context.SaveChangesAsync();

        return scenario;
    }

    private static async Task EnsureVarCon02OutboxSchemaAsync(ApplicationDbContext context)
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

    private static async Task EnsureVariantIdentitySchemaAsync(ApplicationDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE EXTENSION IF NOT EXISTS pgcrypto;

            DO $$
            BEGIN
                ALTER TABLE "ProductVariants"
                    ADD COLUMN IF NOT EXISTS "AttributeCombinationKey" character varying(80) NULL;

                IF EXISTS (
                    SELECT 1
                    FROM pg_class index_class
                    INNER JOIN pg_index index_def ON index_def.indexrelid = index_class.oid
                    WHERE index_class.relname = 'IX_ProductVariants_SKU'
                      AND index_def.indisunique = false) THEN
                    DROP INDEX IF EXISTS "IX_ProductVariants_SKU";
                END IF;

                DROP INDEX IF EXISTS "IX_ProductVariants_SKU";
                CREATE UNIQUE INDEX "IX_ProductVariants_SKU"
                    ON "ProductVariants" ("TenantId", "SKU")
                    WHERE "IsDeleted" = false AND "SemanticLifecycleState" = 0 AND "SKU" IS NOT NULL;

                IF EXISTS (
                    SELECT 1
                    FROM pg_class index_class
                    INNER JOIN pg_index index_def ON index_def.indexrelid = index_class.oid
                    WHERE index_class.relname = 'IX_ProductVariants_Barcode'
                      AND index_def.indisunique = false) THEN
                    DROP INDEX IF EXISTS "IX_ProductVariants_Barcode";
                END IF;

                DROP INDEX IF EXISTS "IX_ProductVariants_Barcode";
                CREATE UNIQUE INDEX "IX_ProductVariants_Barcode"
                    ON "ProductVariants" ("TenantId", "Barcode")
                    WHERE "IsDeleted" = false AND "SemanticLifecycleState" = 0 AND "Barcode" IS NOT NULL;

                IF NOT EXISTS (
                    SELECT 1
                    FROM pg_class index_class
                    INNER JOIN pg_index index_def ON index_def.indexrelid = index_class.oid
                    WHERE index_class.relname = 'IX_ProductVariants_TenantId_ProductId_AttributeCombinationKey'
                      AND index_def.indisunique = true) THEN
                    CREATE UNIQUE INDEX "IX_ProductVariants_TenantId_ProductId_AttributeCombinationKey"
                        ON "ProductVariants" ("TenantId", "ProductId", "AttributeCombinationKey")
                        WHERE "IsDeleted" = false AND "AttributeCombinationKey" IS NOT NULL;
                END IF;
            END
            $$;
            """);
    }

    private static async Task EnsureVarCon04FulfillmentLinkageSchemaAsync(ApplicationDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "TenantId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "CheckoutState" integer NOT NULL DEFAULT 0;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "FulfillmentStatus" integer NOT NULL DEFAULT 0;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "FulfillmentMode" integer NOT NULL DEFAULT 0;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "FulfillmentCarrierCode" text NULL;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "IsGuestCheckout" boolean NOT NULL DEFAULT false;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "IpAddress" character varying(45) NULL;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "OnlineCustomerId" uuid NULL;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "SessionId" character varying(100) NULL;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "CartId" uuid NULL;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "AffiliateAccountId" uuid NULL;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "AffiliateReferralCode" text NULL;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "PromotionLinkId" uuid NULL;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "AppliedPromotionId" uuid NULL;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "AppliedPromotionName" text NULL;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "AppliedPromotionType" text NULL;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "PromotionCalculationFailed" boolean NOT NULL DEFAULT false;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "PromotionWarningMessage" text NULL;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "PromotionDiscountAmount" numeric(18,2) NOT NULL DEFAULT 0;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "ManualDiscountAmount" numeric(18,2) NOT NULL DEFAULT 0;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "TotalDiscountAmount" numeric(18,2) NOT NULL DEFAULT 0;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "PaymentFee" numeric(18,2) NOT NULL DEFAULT 0;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "PaymentMethodCode" text NOT NULL DEFAULT '';
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "PaymentMethodName" text NULL;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "PricingSnapshotJson" text NULL;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "EstimatedDeliveryDays" text NULL;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "ProjectionMutationVersion" bigint NOT NULL DEFAULT 0;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "IsPaid" boolean NOT NULL DEFAULT false;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "FinancialPostingStatus" integer NOT NULL DEFAULT 0;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "FinancialPostingPendingAtUtc" timestamp with time zone NULL;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "FinancialPostedAtUtc" timestamp with time zone NULL;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "FinancialPostingJournalId" uuid NULL;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "FinancialPostingFailureReason" character varying(1000) NULL;

            ALTER TABLE "OrderItems" ADD COLUMN IF NOT EXISTS "VariantId" uuid NULL;
            ALTER TABLE "OrderItems" ADD COLUMN IF NOT EXISTS "VariantAttributesJson" text NULL;
            ALTER TABLE "OrderItems" ADD COLUMN IF NOT EXISTS "PaidAmount" numeric(18,2) NOT NULL DEFAULT 0;
            ALTER TABLE "OrderItems" ADD COLUMN IF NOT EXISTS "RefundedAmount" numeric(18,2) NOT NULL DEFAULT 0;
            ALTER TABLE "OrderItems" ADD COLUMN IF NOT EXISTS "AffiliateCommissionEligible" boolean NOT NULL DEFAULT false;
            ALTER TABLE "OrderItems" ADD COLUMN IF NOT EXISTS "AffiliateCommissionPolicyIdSnapshot" uuid NULL;
            ALTER TABLE "OrderItems" ADD COLUMN IF NOT EXISTS "AffiliateCommissionRateSnapshot" numeric(18,6) NULL;
            ALTER TABLE "OrderItems" ADD COLUMN IF NOT EXISTS "AffiliateMaxCommissionPerItemSnapshot" numeric(18,4) NULL;
            ALTER TABLE "OrderItems" ADD COLUMN IF NOT EXISTS "AffiliateCommissionBaseAmountSnapshot" numeric(18,4) NULL;
            ALTER TABLE "OrderItems" ADD COLUMN IF NOT EXISTS "AffiliateCommissionAmountSnapshot" numeric(18,4) NULL;
            ALTER TABLE "OrderItems" ADD COLUMN IF NOT EXISTS "AffiliateCommissionSnapshotCapturedAtUtc" timestamp with time zone NULL;
            """);
    }

    private static async Task InsertVarCon04FulfillmentLinkageAsync(ApplicationDbContext context, SeededScenario seeded)
    {
        var variantAttributesJson = """[{"Name":"Color","Value":"Blue"},{"Name":"Size","Value":"L"}]""";

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "Orders"
                ("Id", "TenantId", "OrderNumber", "Status", "PaymentStatus", "CheckoutState",
                 "FulfillmentStatus", "FulfillmentMode", "IsGuestCheckout", "GuestEmail", "GuestName",
                 "ShippingAddress", "ShippingCity", "ShippingCountry", "Subtotal", "ShippingCost",
                 "DiscountAmount", "ManualDiscountAmount", "PromotionDiscountAmount", "TotalDiscountAmount",
                 "TaxAmount", "Total", "Currency", "PaymentMethod", "PaymentMethodCode", "PaymentMethodName",
                 "PaymentFee", "Source", "WarehouseId", "ProjectionMutationVersion", "IsPaid",
                 "FinancialPostingStatus", "PromotionCalculationFailed", "CreatedAt", "IsDeleted")
            VALUES
                ({VarCon04OrderId}, {seeded.TenantId}, 'VAR-CON-04-ORDER', 0, 0, 0,
                 0, 0, true, 'var-con-04@example.invalid', 'VAR-CON-04 Customer',
                 'VAR-CON-04 Address', 'Cairo', 'EG', {VarCon04VariantBOnlinePrice}, 0,
                 0, 0, 0, 0, 0, {VarCon04VariantBOnlinePrice}, 'EGP', 0, 'VAR-CON-04-CASH', 'VAR-CON-04 Cash',
                 0, 0, {seeded.WarehouseId}, 0, false, 0, false, {SeededUtc}, false);
            """);

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "OrderItems"
                ("Id", "OrderId", "ProductId", "VariantId", "WarehouseId", "SKU", "ProductName",
                 "VariantName", "VariantAttributesJson", "Quantity", "UnitPrice", "DiscountAmount",
                 "TaxAmount", "Total", "CostPrice", "PaidAmount", "RefundedAmount",
                 "AffiliateCommissionEligible", "CreatedAt", "IsDeleted")
            VALUES
                ({VarCon04OrderItemId}, {VarCon04OrderId}, {seeded.ProductId}, {VarCon02VariantId},
                 {seeded.WarehouseId}, {VarCon04BaselineSkuB}, 'PROD-CON-01 Parent Product',
                 'Blue / L', {variantAttributesJson},
                 1, {VarCon04VariantBOnlinePrice}, 0, 0, {VarCon04VariantBOnlinePrice},
                 {VarCon04VariantBCostPrice}, 0, 0, false, {SeededUtc}, false);
            """);
    }

    private static async Task EnsureProductVariantIdempotencySchemaAsync(ApplicationDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "ProductRequestIdempotency"
            (
                "Id" uuid NOT NULL,
                "TenantId" uuid NOT NULL,
                "Key" character varying(200) NOT NULL,
                "Operation" character varying(32) NOT NULL,
                "ProductId" uuid NOT NULL,
                "ProductVariantId" uuid NULL,
                "RequestFingerprint" character varying(64) NOT NULL,
                "ResponseStatusCode" integer NULL,
                "ResponseJson" text NULL,
                "CompletedAtUtc" timestamp with time zone NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL,
                "CreatedBy" uuid NULL,
                "UpdatedBy" uuid NULL,
                "IsDeleted" boolean NOT NULL DEFAULT false,
                CONSTRAINT "PK_ProductRequestIdempotency" PRIMARY KEY ("Id")
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_ProductRequestIdempotency_TenantId_Key_Operation"
                ON "ProductRequestIdempotency" ("TenantId", "Key", "Operation");

            CREATE INDEX IF NOT EXISTS "IX_ProductRequestIdempotency_TenantId_ProductId"
                ON "ProductRequestIdempotency" ("TenantId", "ProductId");

            CREATE INDEX IF NOT EXISTS "IX_ProductRequestIdempotency_TenantId_ProductVariantId"
                ON "ProductRequestIdempotency" ("TenantId", "ProductVariantId");
            """);
    }

    private static async Task<VariantActorResult> ExecuteVariantActorAsync(
        string connectionString,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        VariantActor actor,
        ConcurrentQueue<string> progressEntries,
        params IInterceptor[] interceptors)
    {
        await using var context = CreateDbContext(connectionString, tenantContext, interceptors);
        await ConfigureSessionTimeoutsAsync(context);
        var controllerLogger = new RecordingLogger<ProductVariantsController>();
        var controller = CreateController(context, tenantContext, actor, controllerLogger);
        var request = BuildCreateVariantRequest(seeded, actor);

        progressEntries.Enqueue($"{actor.Name}:request-ready");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            progressEntries.Enqueue($"{actor.Name}:invoking-controller");
            var result = await controller.CreateVariant(seeded.ProductId, request);
            if (controllerLogger.LastException is Exception loggedException)
            {
                progressEntries.Enqueue(
                    $"{actor.Name}:logged-exception:{DescribeException(loggedException)}");
            }

            stopwatch.Stop();
            return ParseActorResult(actor, result, stopwatch.Elapsed, progressEntries);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            progressEntries.Enqueue($"{actor.Name}:threw:{DescribeException(ex)}");

            return new VariantActorResult(
                Actor: actor,
                Succeeded: false,
                StatusCode: null,
                Response: null,
                PayloadJson: null,
                Duration: stopwatch.Elapsed,
                Exception: ex);
        }
    }

    private static async Task<VariantActorResult> ExecuteVariantUpdateActorAsync(
        string connectionString,
        TestTenantContext tenantContext,
        VarCon02Scenario seeded,
        VariantActor actor,
        ConcurrentQueue<string> progressEntries,
        params IInterceptor[] interceptors)
    {
        return await ExecuteVariantUpdateActorAsync(
            connectionString,
            tenantContext,
            seeded,
            BuildVarCon02UpdateRequest(),
            actor,
            progressEntries,
            interceptors);
    }

    private static async Task<VariantActorResult> ExecuteVariantUpdateActorAsync(
        string connectionString,
        TestTenantContext tenantContext,
        VarCon02Scenario seeded,
        UpdateVariantRequest request,
        VariantActor actor,
        ConcurrentQueue<string> progressEntries,
        params IInterceptor[] interceptors)
    {
        await using var context = CreateDbContext(connectionString, tenantContext, interceptors);
        await ConfigureSessionTimeoutsAsync(context);
        var controllerLogger = new RecordingLogger<ProductVariantsController>();
        var controller = CreateUpdateController(context, tenantContext, actor, controllerLogger);

        progressEntries.Enqueue($"{actor.Name}:update-request-ready");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            progressEntries.Enqueue($"{actor.Name}:invoking-update-controller");
            var result = await controller.UpdateVariant(seeded.ProductId, seeded.VariantId, request);
            if (controllerLogger.LastException is Exception loggedException)
            {
                progressEntries.Enqueue(
                    $"{actor.Name}:logged-exception:{DescribeException(loggedException)}");
            }

            stopwatch.Stop();
            return ParseUpdateActorResult(actor, result, stopwatch.Elapsed, progressEntries);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            progressEntries.Enqueue($"{actor.Name}:threw:{DescribeException(ex)}");

            return new VariantActorResult(
                Actor: actor,
                Succeeded: false,
                StatusCode: null,
                Response: null,
                PayloadJson: null,
                Duration: stopwatch.Elapsed,
            Exception: ex);
        }
    }

    private static async Task<VariantActorResult> ExecuteVariantUpdateActorAsync(
        string connectionString,
        TestTenantContext tenantContext,
        VarCon04Scenario seeded,
        Guid variantId,
        UpdateVariantRequest request,
        VariantActor actor,
        ConcurrentQueue<string> progressEntries,
        params IInterceptor[] interceptors)
    {
        await using var context = CreateDbContext(connectionString, tenantContext, interceptors);
        await ConfigureSessionTimeoutsAsync(context);
        var controllerLogger = new RecordingLogger<ProductVariantsController>();
        var controller = CreateUpdateController(context, tenantContext, actor, controllerLogger);

        progressEntries.Enqueue($"{actor.Name}:sku-reassignment-request-ready:{variantId}");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            progressEntries.Enqueue($"{actor.Name}:invoking-sku-reassignment-controller");
            var result = await controller.UpdateVariant(seeded.ProductId, variantId, request);
            if (controllerLogger.LastException is Exception loggedException)
            {
                progressEntries.Enqueue(
                    $"{actor.Name}:logged-exception:{DescribeException(loggedException)}");
            }

            stopwatch.Stop();
            return ParseUpdateActorResult(actor, result, stopwatch.Elapsed, progressEntries);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            progressEntries.Enqueue($"{actor.Name}:threw:{DescribeException(ex)}");

            return new VariantActorResult(
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
        VariantActor actor,
        ILogger<ProductVariantsController> logger)
    {
        var controller = new ProductVariantsController(
            context,
            tenantContext,
            Mock.Of<IProductVariantService>(),
            new NoOpEventPublisher(),
            Mock.Of<IMarketplaceEventOutboxService>(),
            logger)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext(actor)
            }
        };

        controller.Request.Headers["Idempotency-Key"] = actor.RequestId;
        controller.Request.Headers["X-Request-Id"] = actor.RequestId;
        controller.Request.Headers["User-Agent"] = "PROD-CON";

        return controller;
    }

    private static ProductVariantsController CreateUpdateController(
        ApplicationDbContext context,
        TestTenantContext tenantContext,
        VariantActor actor,
        ILogger<ProductVariantsController> logger)
    {
        var productVariantService = new Mock<IProductVariantService>(MockBehavior.Strict);
        productVariantService
            .Setup(service => service.SyncSimpleProductFromDefaultVariantAsync(
                It.IsAny<ProductVariant>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SimpleProductMirrorSyncResult?)null);

        var controller = new ProductVariantsController(
            context,
            tenantContext,
            productVariantService.Object,
            new AuthoritativeOutboxTestPublisher(
                context,
                tenantContext.TenantId ?? throw new InvalidOperationException("VAR-CON-02 requires a tenant id."),
                actor.TraceIdentifier),
            Mock.Of<IMarketplaceEventOutboxService>(),
            logger)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext(actor)
            }
        };

        controller.Request.Headers["Idempotency-Key"] = actor.RequestId;
        controller.Request.Headers["X-Request-Id"] = actor.RequestId;
        controller.Request.Headers["User-Agent"] = "VAR-CON";

        return controller;
    }

    private static ProductVariantsController CreateDeleteController(
        ApplicationDbContext context,
        TestTenantContext tenantContext,
        VariantActor actor,
        ILogger<ProductVariantsController> logger)
    {
        var controller = new ProductVariantsController(
            context,
            tenantContext,
            Mock.Of<IProductVariantService>(),
            new AuthoritativeOutboxTestPublisher(
                context,
                tenantContext.TenantId ?? throw new InvalidOperationException("VAR-CON-05 requires a tenant id."),
                actor.TraceIdentifier),
            Mock.Of<IMarketplaceEventOutboxService>(),
            logger)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext(actor)
            }
        };

        controller.Request.Headers["Idempotency-Key"] = actor.RequestId;
        controller.Request.Headers["X-Request-Id"] = actor.RequestId;
        controller.Request.Headers["X-Confirm-Delete"] = "true";
        controller.Request.Headers["User-Agent"] = "VAR-CON-05";

        return controller;
    }

    private static DefaultHttpContext BuildHttpContext(VariantActor actor)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, actor.UserId.ToString()),
            new Claim(ClaimTypes.Name, actor.Name)
        ], "TestAuth");

        return new DefaultHttpContext
        {
            TraceIdentifier = actor.TraceIdentifier,
            User = new ClaimsPrincipal(identity)
        };
    }

    private static CreateVariantRequest BuildCreateVariantRequest(
        SeededScenario seeded,
        VariantActor? actor = null)
    {
        return new CreateVariantRequest
        {
            SKU = actor?.RequestedSku ?? ContestedSku,
            Barcode = actor?.RequestedBarcode ?? ContestedBarcode,
            Price = VariantPrice,
            OnlinePrice = VariantOnlinePrice,
            CostPrice = VariantCostPrice,
            IsActive = true,
            MediaId = seeded.MediaId,
            SortOrder = 10,
            Attributes =
            [
                new CreateVariantAttributeRequest
                {
                    Name = "Color",
                    Value = "Red",
                    SortOrder = 0
                },
                new CreateVariantAttributeRequest
                {
                    Name = "Size",
                    Value = "L",
                    SortOrder = 1
                }
            ],
            Stocks =
            [
                new CreateVariantStockRequest
                {
                    WarehouseId = seeded.WarehouseId,
                    Quantity = InitialStockQuantity
                }
            ]
        };
    }

    private static UpdateVariantRequest BuildVarCon02UpdateRequest()
    {
        return new UpdateVariantRequest
        {
            SKU = VarCon02MutatedSku,
            Barcode = VarCon02MutatedBarcode,
            Price = VarCon02MutatedPrice,
            OnlinePrice = VarCon02MutatedOnlinePrice,
            CostPrice = VarCon02MutatedCostPrice,
            IsActive = true,
            MediaId = MediaId,
            SortOrder = 25,
            Attributes =
            [
                new CreateVariantAttributeRequest
                {
                    Name = "Color",
                    Value = "Red",
                    SortOrder = 0
                },
                new CreateVariantAttributeRequest
                {
                    Name = "Size",
                    Value = "XL",
                    SortOrder = 1
                }
            ],
            Stocks = null
        };
    }

    private static UpdateVariantRequest BuildVarCon06UpdateRequest(
        string sku,
        string barcode,
        string size,
        decimal price,
        decimal onlinePrice,
        decimal costPrice)
    {
        return new UpdateVariantRequest
        {
            SKU = sku,
            Barcode = barcode,
            Price = price,
            OnlinePrice = onlinePrice,
            CostPrice = costPrice,
            IsActive = true,
            MediaId = MediaId,
            SortOrder = 25,
            Attributes =
            [
                new CreateVariantAttributeRequest
                {
                    Name = "Color",
                    Value = "Red",
                    SortOrder = 0
                },
                new CreateVariantAttributeRequest
                {
                    Name = "Size",
                    Value = size,
                    SortOrder = 1
                }
            ],
            Stocks = null
        };
    }

    private static UpdateVariantRequest BuildVarCon04UpdateRequest(
        VarCon04Scenario seeded,
        bool winningVariant)
    {
        return new UpdateVariantRequest
        {
            SKU = VarCon04ContestedSku,
            Barcode = winningVariant ? VarCon04WinningBarcode : VarCon04LosingBarcode,
            Price = winningVariant ? VarCon04VariantAPrice : VarCon04VariantBPrice,
            OnlinePrice = winningVariant ? VarCon04VariantAOnlinePrice : VarCon04VariantBOnlinePrice,
            CostPrice = winningVariant ? VarCon04VariantACostPrice : VarCon04VariantBCostPrice,
            IsActive = true,
            MediaId = MediaId,
            SortOrder = winningVariant ? 10 : 20,
            Attributes = winningVariant
                ? new List<CreateVariantAttributeRequest>
                {
                    new CreateVariantAttributeRequest
                    {
                        Name = "Color",
                        Value = "Red",
                        SortOrder = 0
                    },
                    new CreateVariantAttributeRequest
                    {
                        Name = "Size",
                        Value = "L",
                        SortOrder = 1
                    }
                }
                : new List<CreateVariantAttributeRequest>
                {
                    new CreateVariantAttributeRequest
                    {
                        Name = "Color",
                        Value = "Blue",
                        SortOrder = 0
                    },
                    new CreateVariantAttributeRequest
                    {
                        Name = "Size",
                        Value = "L",
                        SortOrder = 1
                    }
                },
            Stocks = null
        };
    }

    private static VariantActorResult ParseActorResult(
        VariantActor actor,
        ActionResult<ProductVariantDto> actionResult,
        TimeSpan duration,
        ConcurrentQueue<string> progressEntries)
    {
        if (actionResult.Result is CreatedAtActionResult { Value: ProductVariantDto response })
        {
            progressEntries.Enqueue($"{actor.Name}:created:{response.Id}");
            return new VariantActorResult(
                actor,
                Succeeded: true,
                StatusCode: StatusCodes.Status201Created,
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

            if ((objectResult.StatusCode ?? StatusCodes.Status200OK) == StatusCodes.Status201Created &&
                TryParseCreatedVariant(payloadJson, out var storedResponse))
            {
                progressEntries.Enqueue($"{actor.Name}:created:{storedResponse.Id}");
                return new VariantActorResult(
                    actor,
                    Succeeded: true,
                    StatusCode: StatusCodes.Status201Created,
                    Response: storedResponse,
                    PayloadJson: payloadJson,
                    Duration: duration,
                    Exception: null);
            }

            progressEntries.Enqueue(
                $"{actor.Name}:status:{objectResult.StatusCode ?? StatusCodes.Status500InternalServerError}:{payloadJson}");
            return new VariantActorResult(
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
            return new VariantActorResult(
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
            progressEntries.Enqueue($"{actor.Name}:created:{directResponse.Id}");
            return new VariantActorResult(
                actor,
                Succeeded: true,
                StatusCode: StatusCodes.Status201Created,
                Response: directResponse,
                PayloadJson: JsonSerializer.Serialize(directResponse),
                Duration: duration,
                Exception: null);
        }

        throw new XunitException(
            $"Actor {actor.Name} returned an unexpected result shape: {actionResult.Result?.GetType().Name ?? "null"}");
    }

    private static VariantActorResult ParseUpdateActorResult(
        VariantActor actor,
        ActionResult<ProductVariantDto> actionResult,
        TimeSpan duration,
        ConcurrentQueue<string> progressEntries)
    {
        if (actionResult.Result is OkObjectResult { Value: ProductVariantDto response })
        {
            progressEntries.Enqueue($"{actor.Name}:updated:{response.Id}:{response.SKU}");
            return new VariantActorResult(
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
                TryParseCreatedVariant(payloadJson, out var storedResponse))
            {
                progressEntries.Enqueue($"{actor.Name}:updated:{storedResponse.Id}:{storedResponse.SKU}");
                return new VariantActorResult(
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
            return new VariantActorResult(
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
            return new VariantActorResult(
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
            progressEntries.Enqueue($"{actor.Name}:updated:{directResponse.Id}:{directResponse.SKU}");
            return new VariantActorResult(
                actor,
                Succeeded: true,
                StatusCode: StatusCodes.Status200OK,
                Response: directResponse,
                PayloadJson: JsonSerializer.Serialize(directResponse),
                Duration: duration,
                Exception: null);
        }

        throw new XunitException(
            $"Actor {actor.Name} returned an unexpected update result shape: {actionResult.Result?.GetType().Name ?? "null"}");
    }

    private static VariantDeleteActorResult ParseDeleteActorResult(
        VariantActor actor,
        IActionResult actionResult,
        TimeSpan duration,
        ConcurrentQueue<string> progressEntries)
    {
        if (actionResult is NoContentResult)
        {
            progressEntries.Enqueue($"{actor.Name}:deleted");
            return new VariantDeleteActorResult(
                actor,
                Succeeded: true,
                StatusCode: StatusCodes.Status204NoContent,
                PayloadJson: null,
                Duration: duration,
                Exception: null);
        }

        if (actionResult is ObjectResult objectResult)
        {
            var payloadJson = objectResult.Value == null
                ? null
                : JsonSerializer.Serialize(objectResult.Value);
            progressEntries.Enqueue(
                $"{actor.Name}:delete-status:{objectResult.StatusCode ?? StatusCodes.Status500InternalServerError}:{payloadJson}");

            return new VariantDeleteActorResult(
                actor,
                Succeeded: false,
                StatusCode: objectResult.StatusCode,
                PayloadJson: payloadJson,
                Duration: duration,
                Exception: null);
        }

        if (actionResult is StatusCodeResult statusCodeResult)
        {
            progressEntries.Enqueue($"{actor.Name}:delete-status:{statusCodeResult.StatusCode}");
            return new VariantDeleteActorResult(
                actor,
                Succeeded: false,
                StatusCode: statusCodeResult.StatusCode,
                PayloadJson: null,
                Duration: duration,
                Exception: null);
        }

        throw new XunitException(
            $"Actor {actor.Name} returned an unexpected delete result shape: {actionResult.GetType().Name}");
    }

    private static bool TryParseCreatedVariant(string? payloadJson, out ProductVariantDto response)
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

    private static async Task AssertNoUncommittedDurableStateVisibleAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var visibleVariantCount = await verifyContext.ProductVariants
            .CountAsync(variant =>
                variant.ProductId == seeded.ProductId &&
                variant.SKU == ContestedSku);
        var visibleBarcodeCount = await verifyContext.ProductVariants
            .CountAsync(variant =>
                variant.ProductId == seeded.ProductId &&
                variant.Barcode == ContestedBarcode);
        var visibleAttributeCount = await verifyContext.ProductVariantAttributes
            .CountAsync(attribute =>
                attribute.Variant.ProductId == seeded.ProductId &&
                attribute.Variant.SKU == ContestedSku);
        var visibleStockCount = await verifyContext.ProductVariantStocks
            .CountAsync(stock =>
                stock.Variant.ProductId == seeded.ProductId &&
                stock.Variant.SKU == ContestedSku);
        var visibleIdempotencyCount = await verifyContext.ProductRequestIdempotencyRecords
            .CountAsync(record =>
                record.ProductId == seeded.ProductId &&
                record.Operation == "CREATE_VARIANT");
        var visibleStockSyncOutboxCount = await verifyContext.StockSyncOutboxMessages
            .IgnoreQueryFilters()
            .CountAsync(message =>
                message.TenantId == seeded.TenantId &&
                message.ProductId == seeded.ProductId &&
                message.VariantId != seeded.DefaultVariantId);

        Assert.Equal(0, visibleVariantCount);
        Assert.Equal(0, visibleBarcodeCount);
        Assert.Equal(0, visibleAttributeCount);
        Assert.Equal(0, visibleStockCount);
        Assert.Equal(0, visibleIdempotencyCount);
        Assert.Equal(0, visibleStockSyncOutboxCount);

        progressEntries.Enqueue("verification:no-uncommitted-product-variant-visibility");
    }

    private static async Task AssertNoUncommittedVariantCombinationVisibleAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var visibleCombinationOwners = await verifyContext.ProductVariants
            .CountAsync(variant =>
                variant.TenantId == seeded.TenantId &&
                variant.ProductId == seeded.ProductId &&
                variant.AttributeCombinationKey == VarCon01AttributeCombinationKey);
        var visibleActorASkuOwners = await verifyContext.ProductVariants
            .CountAsync(variant =>
                variant.TenantId == seeded.TenantId &&
                variant.ProductId == seeded.ProductId &&
                variant.SKU == VarCon01ActorASku);
        var visibleActorBSkuOwners = await verifyContext.ProductVariants
            .CountAsync(variant =>
                variant.TenantId == seeded.TenantId &&
                variant.ProductId == seeded.ProductId &&
                variant.SKU == VarCon01ActorBSku);
        var visibleActorABarcodeOwners = await verifyContext.ProductVariants
            .CountAsync(variant =>
                variant.TenantId == seeded.TenantId &&
                variant.ProductId == seeded.ProductId &&
                variant.Barcode == VarCon01ActorABarcode);
        var visibleActorBBarcodeOwners = await verifyContext.ProductVariants
            .CountAsync(variant =>
                variant.TenantId == seeded.TenantId &&
                variant.ProductId == seeded.ProductId &&
                variant.Barcode == VarCon01ActorBBarcode);
        var visibleStockCount = await verifyContext.ProductVariantStocks
            .CountAsync(stock =>
                stock.TenantId == seeded.TenantId &&
                stock.Variant.ProductId == seeded.ProductId &&
                stock.Variant.AttributeCombinationKey == VarCon01AttributeCombinationKey);
        var visibleIdempotencyCount = await verifyContext.ProductRequestIdempotencyRecords
            .CountAsync(record =>
                record.TenantId == seeded.TenantId &&
                record.ProductId == seeded.ProductId &&
                record.Operation == "CREATE_VARIANT" &&
                (record.Key.Contains(VarCon01ActorARequestId) ||
                 record.Key.Contains(VarCon01ActorBRequestId)));
        var visibleStockSyncOutboxCount = await verifyContext.StockSyncOutboxMessages
            .IgnoreQueryFilters()
            .CountAsync(message =>
                message.TenantId == seeded.TenantId &&
                message.ProductId == seeded.ProductId &&
                message.VariantId != seeded.DefaultVariantId);

        Assert.Equal(0, visibleCombinationOwners);
        Assert.Equal(0, visibleActorASkuOwners);
        Assert.Equal(0, visibleActorBSkuOwners);
        Assert.Equal(0, visibleActorABarcodeOwners);
        Assert.Equal(0, visibleActorBBarcodeOwners);
        Assert.Equal(0, visibleStockCount);
        Assert.Equal(0, visibleIdempotencyCount);
        Assert.Equal(0, visibleStockSyncOutboxCount);

        progressEntries.Enqueue("verification:var-con-01-no-uncommitted-combination-visibility");
    }

    private static async Task AssertAuthoritativeConvergenceAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        IReadOnlyCollection<VariantActorResult> results,
        ConcurrentQueue<string> progressEntries)
    {
        var successfulResults = results
            .Where(result => result.Succeeded && result.Response is not null)
            .ToList();
        var failedResults = results
            .Where(result => !result.Succeeded)
            .ToList();

        Assert.InRange(successfulResults.Count, 0, 1);
        Assert.Equal(2 - successfulResults.Count, failedResults.Count);

        foreach (var failedResult in failedResults)
        {
            Assert.Contains(failedResult.StatusCode, new int?[]
            {
                StatusCodes.Status400BadRequest,
                StatusCodes.Status409Conflict
            });
        }

        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var seededProduct = await verifyContext.Products
            .SingleAsync(product => product.Id == seeded.ProductId);
        var defaultVariant = await verifyContext.ProductVariants
            .SingleAsync(variant => variant.Id == seeded.DefaultVariantId);
        var targetVariants = await verifyContext.ProductVariants
            .Include(variant => variant.Attributes)
            .Include(variant => variant.Stocks)
            .Where(variant =>
                variant.ProductId == seeded.ProductId &&
                variant.SKU == ContestedSku)
            .ToListAsync();
        var barcodeOwnerCount = await verifyContext.ProductVariants
            .CountAsync(variant =>
                variant.ProductId == seeded.ProductId &&
                variant.Barcode == ContestedBarcode);
        var sameAttributeOwnerCount = await verifyContext.ProductVariants
            .CountAsync(variant =>
                variant.ProductId == seeded.ProductId &&
                variant.Attributes.Count == 2 &&
                variant.Attributes.Any(attribute => attribute.Name == "Color" && attribute.Value == "Red") &&
                variant.Attributes.Any(attribute => attribute.Name == "Size" && attribute.Value == "L"));
        var mediaOwnerCount = await verifyContext.ProductVariants
            .CountAsync(variant =>
                variant.ProductId == seeded.ProductId &&
                variant.SKU == ContestedSku &&
                variant.MediaId == seeded.MediaId);
        var listingCount = await verifyContext.CommerceHubListings
            .CountAsync(listing => listing.ProductId == seeded.ProductId);
        var orphanAttributeCount = await ExecuteScalarIntAsync(
            verifyContext,
            """
            SELECT COUNT(*)
            FROM "ProductVariantAttributes" attribute
            LEFT JOIN "ProductVariants" variant ON variant."Id" = attribute."VariantId"
            WHERE variant."Id" IS NULL;
            """);
        var orphanStockCount = await ExecuteScalarIntAsync(
            verifyContext,
            """
            SELECT COUNT(*)
            FROM "ProductVariantStocks" stock
            LEFT JOIN "ProductVariants" variant ON variant."Id" = stock."VariantId"
            WHERE variant."Id" IS NULL;
            """);

        Assert.Equal(successfulResults.Count, targetVariants.Count);
        Assert.Equal(targetVariants.Count, barcodeOwnerCount);
        Assert.Equal(targetVariants.Count, sameAttributeOwnerCount);
        Assert.Equal(targetVariants.Count, mediaOwnerCount);
        Assert.Equal(0, listingCount);
        Assert.Equal(0, orphanAttributeCount);
        Assert.Equal(0, orphanStockCount);
        Assert.Equal(seeded.ProductProjectionMutationVersion, seededProduct.ProjectionMutationVersion);
        Assert.Equal(seeded.DefaultVariantProjectionMutationVersion, defaultVariant.ProjectionMutationVersion);
        Assert.Equal(DefaultVariantSku, defaultVariant.SKU);
        Assert.True(defaultVariant.IsDefault);

        if (targetVariants.Count == 1)
        {
            var variant = targetVariants[0];
            Assert.Equal(TenantId, variant.TenantId);
            Assert.Equal(seeded.ProductId, variant.ProductId);
            Assert.Equal(ContestedSku, variant.SKU);
            Assert.Equal(ContestedBarcode, variant.Barcode);
            Assert.Equal(VariantPrice, variant.Price);
            Assert.Equal(VariantOnlinePrice, variant.OnlinePrice);
            Assert.Equal(VariantCostPrice, variant.CostPrice);
            Assert.False(variant.IsDefault);
            Assert.True(variant.IsActive);
            Assert.Equal(seeded.MediaId, variant.MediaId);
            Assert.True(variant.ProjectionMutationVersion > 0);

            var attributes = variant.Attributes
                .OrderBy(attribute => attribute.SortOrder)
                .ToList();
            Assert.Equal(2, attributes.Count);
            Assert.Equal(("Color", "Red"), (attributes[0].Name, attributes[0].Value));
            Assert.Equal(("Size", "L"), (attributes[1].Name, attributes[1].Value));

            var stock = Assert.Single(variant.Stocks);
            Assert.Equal(seeded.WarehouseId, stock.WarehouseId);
            Assert.Equal(InitialStockQuantity, stock.Quantity);
            Assert.Equal(0m, stock.ReservedQuantity);
            Assert.Equal(0m, stock.DamagedQuantity);

            progressEntries.Enqueue("verification:prod-con-01-safe-outcome-a-single-owner");
            return;
        }

        progressEntries.Enqueue("verification:prod-con-01-safe-outcome-b-failed-closed");
    }

    private static async Task AssertActiveReplayConvergenceAsync(
        TestDatabaseScope scope,
        string connectionString,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        VariantActor originalActor,
        VariantActor replayActor,
        IReadOnlyCollection<VariantActorResult> results,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var targetVariants = await verifyContext.ProductVariants
            .Include(variant => variant.Attributes)
            .Include(variant => variant.Stocks)
            .Where(variant =>
                variant.TenantId == seeded.TenantId &&
                variant.ProductId == seeded.ProductId &&
                variant.SKU == ContestedSku)
            .OrderBy(variant => variant.CreatedAt)
            .ToListAsync();
        var barcodeOwnerCount = await verifyContext.ProductVariants
            .CountAsync(variant =>
                variant.TenantId == seeded.TenantId &&
                variant.ProductId == seeded.ProductId &&
                variant.Barcode == ContestedBarcode);
        var mediaOwnerCount = await verifyContext.ProductVariants
            .CountAsync(variant =>
                variant.TenantId == seeded.TenantId &&
                variant.ProductId == seeded.ProductId &&
                variant.SKU == ContestedSku &&
                variant.MediaId == seeded.MediaId);
        var attributeCount = await verifyContext.ProductVariantAttributes
            .CountAsync(attribute =>
                attribute.Variant.TenantId == seeded.TenantId &&
                attribute.Variant.ProductId == seeded.ProductId &&
                attribute.Variant.SKU == ContestedSku);
        var stockRows = await verifyContext.ProductVariantStocks
            .Where(stock =>
                stock.TenantId == seeded.TenantId &&
                stock.Variant.ProductId == seeded.ProductId &&
                stock.Variant.SKU == ContestedSku)
            .OrderBy(stock => stock.WarehouseId)
            .ToListAsync();
        var stockSyncOutboxMessages = await verifyContext.StockSyncOutboxMessages
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.ProductId == seeded.ProductId &&
                message.VariantId != seeded.DefaultVariantId)
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .ToListAsync();
        var idempotencyRecords = await verifyContext.ProductRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .Where(record =>
                record.TenantId == seeded.TenantId &&
                record.ProductId == seeded.ProductId &&
                record.Operation == "CREATE_VARIANT")
            .OrderBy(record => record.CreatedAt)
            .ToListAsync();
        var marketplaceOutboxCount = await CountMarketplaceOutboxMessagesForProductAsync(
            verifyContext,
            seeded);
        var listingCount = await verifyContext.CommerceHubListings
            .CountAsync(listing => listing.ProductId == seeded.ProductId);
        var orphanAttributeCount = await ExecuteScalarIntAsync(
            verifyContext,
            """
            SELECT COUNT(*)
            FROM "ProductVariantAttributes" attribute
            LEFT JOIN "ProductVariants" variant ON variant."Id" = attribute."VariantId"
            WHERE variant."Id" IS NULL;
            """);
        var orphanStockCount = await ExecuteScalarIntAsync(
            verifyContext,
            """
            SELECT COUNT(*)
            FROM "ProductVariantStocks" stock
            LEFT JOIN "ProductVariants" variant ON variant."Id" = stock."VariantId"
            WHERE variant."Id" IS NULL;
            """);

        Assert.Single(targetVariants);
        var variant = targetVariants.Single();
        Assert.Equal(1, barcodeOwnerCount);
        Assert.Equal(1, mediaOwnerCount);
        Assert.Equal(2, attributeCount);
        Assert.Single(stockRows);
        Assert.Single(stockSyncOutboxMessages);
        Assert.Single(idempotencyRecords);
        Assert.Equal(0, marketplaceOutboxCount);
        Assert.Equal(0, listingCount);
        Assert.Equal(0, orphanAttributeCount);
        Assert.Equal(0, orphanStockCount);

        Assert.Equal(seeded.TenantId, variant.TenantId);
        Assert.Equal(seeded.ProductId, variant.ProductId);
        Assert.Equal(ContestedSku, variant.SKU);
        Assert.Equal(ContestedBarcode, variant.Barcode);
        Assert.Equal(VariantPrice, variant.Price);
        Assert.Equal(VariantOnlinePrice, variant.OnlinePrice);
        Assert.Equal(VariantCostPrice, variant.CostPrice);
        Assert.Equal(seeded.MediaId, variant.MediaId);
        Assert.True(variant.ProjectionMutationVersion > 0);

        var stock = stockRows.Single();
        Assert.Equal(seeded.WarehouseId, stock.WarehouseId);
        Assert.Equal(InitialStockQuantity, stock.Quantity);
        Assert.Equal(0m, stock.ReservedQuantity);
        Assert.True(stock.ProjectionMutationVersion > 0);

        var stockSyncMessage = stockSyncOutboxMessages.Single();
        Assert.Equal(nameof(ProductVariantStockChangedEvent), stockSyncMessage.EventType);
        Assert.Equal(seeded.TenantId, stockSyncMessage.TenantId);
        Assert.Equal(seeded.ProductId, stockSyncMessage.ProductId);
        Assert.Equal(variant.Id, stockSyncMessage.VariantId);
        Assert.Equal(seeded.WarehouseId, stockSyncMessage.WarehouseId);
        Assert.True(stockSyncMessage.EventVersion > 0);
        Assert.Null(stockSyncMessage.PublishedAtUtc);
        Assert.False(stockSyncMessage.IsFailed);

        var idempotencyRecord = idempotencyRecords.Single();
        var expectedKey = BuildProductMutationIdempotencyKey(seeded.TenantId, originalActor.RequestId);
        Assert.Equal(expectedKey, idempotencyRecord.Key);
        Assert.Equal(replayActor.RequestId, originalActor.RequestId);
        Assert.Equal(variant.Id, idempotencyRecord.ProductVariantId);
        Assert.Equal(StatusCodes.Status201Created, idempotencyRecord.ResponseStatusCode);
        Assert.NotNull(idempotencyRecord.CompletedAtUtc);
        Assert.Contains(variant.Id.ToString("D"), idempotencyRecord.ResponseJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var original = results.Single(result => result.Actor.Name == originalActor.Name);
        var replay = results.Single(result => result.Actor.Name == replayActor.Name);

        Assert.True(original.Succeeded, original.PayloadJson);
        Assert.NotNull(original.Response);
        Assert.Equal(variant.Id, original.Response!.Id);

        if (replay.Succeeded)
        {
            Assert.NotNull(replay.Response);
            Assert.Equal(variant.Id, replay.Response!.Id);
            progressEntries.Enqueue("verification:prod-con-02-safe-outcome-a-replay-converged");
        }
        else
        {
            Assert.Contains(replay.StatusCode, new int?[]
            {
                StatusCodes.Status409Conflict,
                StatusCodes.Status500InternalServerError
            });
            progressEntries.Enqueue("verification:prod-con-02-safe-outcome-b-failed-closed");
        }

        await AssertReplaySafetyAsync(
            connectionString,
            tenantContext,
            seeded,
            original,
            progressEntries);
    }

    private static async Task AssertVariantCombinationConvergenceAsync(
        TestDatabaseScope scope,
        string connectionString,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        IReadOnlyCollection<VariantActorResult> results,
        ConcurrentQueue<string> progressEntries)
    {
        var successfulResults = results
            .Where(result => result.Succeeded && result.Response is not null)
            .ToList();
        var failedResults = results
            .Where(result => !result.Succeeded)
            .ToList();

        Assert.Single(successfulResults);
        Assert.Single(failedResults);
        Assert.Contains(failedResults.Single().StatusCode, new int?[]
        {
            StatusCodes.Status400BadRequest,
            StatusCodes.Status409Conflict
        });

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var expectedCombinationKey = ProductVariantAttributeCombinationKey.Compute(
        [
            ("Size", "L"),
            ("Color", "Red")
        ]);

        Assert.Equal(VarCon01AttributeCombinationKey, expectedCombinationKey);

        var combinationVariants = await verifyContext.ProductVariants
            .Include(variant => variant.Attributes)
            .Include(variant => variant.Stocks)
            .Where(variant =>
                variant.TenantId == seeded.TenantId &&
                variant.ProductId == seeded.ProductId &&
                variant.AttributeCombinationKey == VarCon01AttributeCombinationKey)
            .ToListAsync();
        var variant = Assert.Single(combinationVariants);
        var successful = successfulResults.Single();

        Assert.Equal(successful.Response!.Id, variant.Id);
        Assert.Contains(variant.SKU, new[] { VarCon01ActorASku, VarCon01ActorBSku });
        Assert.Contains(variant.Barcode, new[] { VarCon01ActorABarcode, VarCon01ActorBBarcode });
        Assert.Equal(VariantPrice, variant.Price);
        Assert.Equal(VariantOnlinePrice, variant.OnlinePrice);
        Assert.Equal(VariantCostPrice, variant.CostPrice);
        Assert.False(variant.IsDefault);
        Assert.True(variant.IsActive);
        Assert.Equal(seeded.MediaId, variant.MediaId);

        var attributeRows = variant.Attributes
            .OrderBy(attribute => attribute.SortOrder)
            .ToList();
        Assert.Equal(2, attributeRows.Count);
        Assert.Equal(("Color", "Red"), (attributeRows[0].Name, attributeRows[0].Value));
        Assert.Equal(("Size", "L"), (attributeRows[1].Name, attributeRows[1].Value));

        var stock = Assert.Single(variant.Stocks);
        Assert.Equal(seeded.TenantId, stock.TenantId);
        Assert.Equal(variant.Id, stock.VariantId);
        Assert.Equal(seeded.WarehouseId, stock.WarehouseId);
        Assert.Equal(InitialStockQuantity, stock.Quantity);
        Assert.Equal(0m, stock.ReservedQuantity);
        Assert.Equal(0m, stock.DamagedQuantity);

        if (string.Equals(variant.SKU, VarCon01ActorBSku, StringComparison.Ordinal))
        {
            Assert.Equal(0, await verifyContext.ProductVariants.CountAsync(variant =>
                variant.TenantId == seeded.TenantId &&
                variant.ProductId == seeded.ProductId &&
                variant.SKU == VarCon01ActorASku));
            Assert.Equal(1, await verifyContext.ProductVariants.CountAsync(variant =>
                variant.TenantId == seeded.TenantId &&
                variant.ProductId == seeded.ProductId &&
                variant.SKU == VarCon01ActorBSku));
        }
        else
        {
            Assert.Equal(1, await verifyContext.ProductVariants.CountAsync(variant =>
                variant.TenantId == seeded.TenantId &&
                variant.ProductId == seeded.ProductId &&
                variant.SKU == VarCon01ActorASku));
            Assert.Equal(0, await verifyContext.ProductVariants.CountAsync(variant =>
                variant.TenantId == seeded.TenantId &&
                variant.ProductId == seeded.ProductId &&
                variant.SKU == VarCon01ActorBSku));
        }

        var skuOwnerCount = await verifyContext.ProductVariants
            .CountAsync(item =>
                item.TenantId == seeded.TenantId &&
                item.ProductId == seeded.ProductId &&
                (item.SKU == VarCon01ActorASku || item.SKU == VarCon01ActorBSku));
        var barcodeOwnerCount = await verifyContext.ProductVariants
            .CountAsync(item =>
                item.TenantId == seeded.TenantId &&
                item.ProductId == seeded.ProductId &&
                (item.Barcode == VarCon01ActorABarcode || item.Barcode == VarCon01ActorBBarcode));
        var attributeOwnerCount = await verifyContext.ProductVariants
            .CountAsync(item =>
                item.TenantId == seeded.TenantId &&
                item.ProductId == seeded.ProductId &&
                item.Attributes.Count == 2 &&
                item.Attributes.Any(attribute => attribute.Name == "Color" && attribute.Value == "Red") &&
                item.Attributes.Any(attribute => attribute.Name == "Size" && attribute.Value == "L"));
        var idempotencyRecords = await verifyContext.ProductRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .Where(record =>
                record.TenantId == seeded.TenantId &&
                record.ProductId == seeded.ProductId &&
                record.Operation == "CREATE_VARIANT" &&
                record.ProductVariantId == variant.Id)
            .ToListAsync();
        var stockSyncOutboxMessages = await verifyContext.StockSyncOutboxMessages
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.ProductId == seeded.ProductId &&
                message.VariantId == variant.Id)
            .ToListAsync();
        var orphanAttributeCount = await ExecuteScalarIntAsync(
            verifyContext,
            """
            SELECT COUNT(*)
            FROM "ProductVariantAttributes" attribute
            LEFT JOIN "ProductVariants" variant ON variant."Id" = attribute."VariantId"
            WHERE variant."Id" IS NULL;
            """);
        var orphanStockCount = await ExecuteScalarIntAsync(
            verifyContext,
            """
            SELECT COUNT(*)
            FROM "ProductVariantStocks" stock
            LEFT JOIN "ProductVariants" variant ON variant."Id" = stock."VariantId"
            WHERE variant."Id" IS NULL;
            """);
        var orphanMovementCount = await ExecuteScalarIntAsync(
            verifyContext,
            """
            SELECT COUNT(*)
            FROM "StockMovements" movement
            LEFT JOIN "ProductVariants" variant ON variant."Id" = movement."ProductVariantId"
            WHERE movement."ProductVariantId" IS NOT NULL
              AND variant."Id" IS NULL;
            """);
        var processedEventCount = await verifyContext.Set<ProcessedEvent>()
            .IgnoreQueryFilters()
            .CountAsync(processed =>
                processed.TenantId == seeded.TenantId &&
                processed.EntityId == variant.Id);
        var listingCount = await verifyContext.CommerceHubListings
            .CountAsync(listing =>
                listing.TenantId == seeded.TenantId &&
                listing.ProductId == seeded.ProductId &&
                listing.VariantId == variant.Id);

        Assert.Equal(1, skuOwnerCount);
        Assert.Equal(1, barcodeOwnerCount);
        Assert.Equal(1, attributeOwnerCount);
        Assert.Single(idempotencyRecords);
        Assert.Single(stockSyncOutboxMessages);
        Assert.Equal(0, orphanAttributeCount);
        Assert.Equal(0, orphanStockCount);
        Assert.Equal(0, orphanMovementCount);
        Assert.Equal(0, processedEventCount);
        Assert.Equal(0, listingCount);

        await AssertVariantCombinationReplaySafetyAsync(
            connectionString,
            tenantContext,
            seeded,
            successful,
            progressEntries);

        progressEntries.Enqueue("verification:var-con-01-converged-to-one-authoritative-combination-owner");
    }

    private static async Task AssertVarCon02ActiveOverlapVisibilityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var variant = await verifyContext.ProductVariants
            .AsNoTracking()
            .Include(item => item.Attributes)
            .Include(item => item.Stocks)
            .SingleAsync(item => item.Id == seeded.VariantId);
        var updateIdempotencyCount = await verifyContext.ProductRequestIdempotencyRecords
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(record =>
                record.TenantId == seeded.TenantId &&
                record.ProductId == seeded.ProductId &&
                record.ProductVariantId == seeded.VariantId &&
                record.Operation == "UPDATE_VARIANT");
        var outboxCount = await verifyContext.OutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(message =>
                message.TenantId == seeded.TenantId &&
                message.AggregateId == seeded.VariantId);
        var mutatedCombinationOwnerCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .CountAsync(item =>
                item.TenantId == seeded.TenantId &&
                item.ProductId == seeded.ProductId &&
                item.AttributeCombinationKey == seeded.MutatedCombinationKey);
        var mutatedSkuOwnerCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .CountAsync(item => item.SKU == VarCon02MutatedSku);
        var mutatedBarcodeOwnerCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .CountAsync(item => item.Barcode == VarCon02MutatedBarcode);
        var orphanAttributeCount = await CountOrphanVariantAttributesAsync(verifyContext);
        var orphanStockCount = await CountOrphanVariantStocksAsync(verifyContext);
        var storefront = await LoadVarCon02StorefrontDocumentAsync(mongoDatabase, seeded);
        var affiliate = await LoadVarCon02AffiliateDocumentAsync(mongoDatabase, seeded);
        var commerceHub = await LoadVarCon02CommerceHubDocumentAsync(mongoDatabase, seeded);

        Assert.Equal(VarCon02BaselineSku, variant.SKU);
        Assert.Equal(VarCon02BaselineBarcode, variant.Barcode);
        Assert.Equal(seeded.BaselineCombinationKey, variant.AttributeCombinationKey);
        Assert.Equal(VarCon02BaselinePrice, variant.Price);
        Assert.Equal(VarCon02BaselineOnlinePrice, variant.OnlinePrice);
        Assert.Equal(VarCon02BaselineCostPrice, variant.CostPrice);
        Assert.Equal(0, updateIdempotencyCount);
        Assert.Equal(0, outboxCount);
        Assert.Equal(0, mutatedCombinationOwnerCount);
        Assert.Equal(0, mutatedSkuOwnerCount);
        Assert.Equal(0, mutatedBarcodeOwnerCount);
        Assert.Equal(0, orphanAttributeCount);
        Assert.Equal(0, orphanStockCount);

        var stock = Assert.Single(variant.Stocks);
        Assert.Equal(VarCon02StockId, stock.Id);
        Assert.Equal(seeded.VariantId, stock.VariantId);
        Assert.Equal(VarCon02StockQuantity, stock.Quantity);

        var attributes = variant.Attributes
            .OrderBy(attribute => attribute.SortOrder)
            .Select(attribute => (attribute.Name, attribute.Value))
            .ToArray();
        Assert.Equal([("Color", "Red"), ("Size", "L")], attributes);

        var storefrontVariant = Assert.Single(storefront.Variants.Where(item => item.VariantId == seeded.VariantId));
        Assert.Equal(VarCon02BaselineSku, storefrontVariant.SKU);
        Assert.Equal(VarCon02BaselineBarcode, storefrontVariant.Barcode);
        Assert.Equal(VarCon02BaselineOnlinePrice, storefrontVariant.EffectivePrice);
        Assert.Equal([("Color", "Red"), ("Size", "L")], storefrontVariant.Attributes
            .OrderBy(attribute => attribute.SortOrder)
            .Select(attribute => (attribute.Name, attribute.Value))
            .ToArray());
        Assert.Equal(VarCon02BaselineOnlinePrice, affiliate.MaxVariantPrice);
        Assert.Equal(VarCon02BaselineOnlinePrice, commerceHub.Price);
        Assert.Equal(seeded.VariantId, commerceHub.VariantId);

        progressEntries.Enqueue("verification:var-con-02-active-overlap-baseline-only-visible");
    }

    private static async Task AssertVarCon04ActiveOverlapVisibilityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon04Scenario seeded,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var variants = await verifyContext.ProductVariants
            .AsNoTracking()
            .Include(item => item.Attributes)
            .Include(item => item.Stocks)
            .Where(item => item.TenantId == seeded.TenantId && item.ProductId == seeded.ProductId)
            .ToDictionaryAsync(item => item.Id);
        var variantA = variants[seeded.VariantAId];
        var variantB = variants[seeded.VariantBId];
        var contestedSkuOwnerCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .CountAsync(item => item.SKU == VarCon04ContestedSku);
        var updateIdempotencyCount = await verifyContext.ProductRequestIdempotencyRecords
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(record =>
                record.TenantId == seeded.TenantId &&
                record.ProductId == seeded.ProductId &&
                record.Operation == "UPDATE_VARIANT");
        var outboxCount = await verifyContext.OutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(message =>
                message.TenantId == seeded.TenantId &&
                (message.AggregateId == seeded.VariantAId ||
                 message.AggregateId == seeded.VariantBId));
        var storefront = await LoadVarCon02StorefrontDocumentAsync(
            mongoDatabase,
            seeded.ToVisibilityScenario());
        var affiliate = await LoadVarCon02AffiliateDocumentAsync(
            mongoDatabase,
            seeded.ToVisibilityScenario());
        var commerceHub = await LoadVarCon02CommerceHubDocumentAsync(
            mongoDatabase,
            seeded.ToVisibilityScenario());

        Assert.Equal(VarCon04BaselineSkuA, variantA.SKU);
        Assert.Equal(VarCon04BaselineBarcodeA, variantA.Barcode);
        Assert.Equal(seeded.VariantACombinationKey, variantA.AttributeCombinationKey);
        Assert.Equal(VarCon04VariantAPrice, variantA.Price);
        Assert.Equal(VarCon04VariantAOnlinePrice, variantA.OnlinePrice);
        Assert.Equal(VarCon04VariantACostPrice, variantA.CostPrice);
        Assert.Equal(VarCon04BaselineSkuB, variantB.SKU);
        Assert.Equal(VarCon04BaselineBarcodeB, variantB.Barcode);
        Assert.Equal(seeded.VariantBCombinationKey, variantB.AttributeCombinationKey);
        Assert.Equal(VarCon04VariantBPrice, variantB.Price);
        Assert.Equal(VarCon04VariantBOnlinePrice, variantB.OnlinePrice);
        Assert.Equal(VarCon04VariantBCostPrice, variantB.CostPrice);
        Assert.Equal(0, contestedSkuOwnerCount);
        Assert.Equal(0, updateIdempotencyCount);
        Assert.Equal(0, outboxCount);
        Assert.Equal(0, await CountOrphanVariantAttributesAsync(verifyContext));
        Assert.Equal(0, await CountOrphanVariantStocksAsync(verifyContext));

        Assert.Equal(VarCon04VariantAStockId, Assert.Single(variantA.Stocks).Id);
        Assert.Equal(VarCon04VariantAStockQuantity, variantA.Stocks.Single().Quantity);
        Assert.Equal(VarCon04VariantBStockId, Assert.Single(variantB.Stocks).Id);
        Assert.Equal(VarCon04VariantBStockQuantity, variantB.Stocks.Single().Quantity);

        Assert.Equal([("Color", "Red"), ("Size", "L")], variantA.Attributes
            .OrderBy(attribute => attribute.SortOrder)
            .Select(attribute => (attribute.Name, attribute.Value))
            .ToArray());
        Assert.Equal([("Color", "Blue"), ("Size", "L")], variantB.Attributes
            .OrderBy(attribute => attribute.SortOrder)
            .Select(attribute => (attribute.Name, attribute.Value))
            .ToArray());

        Assert.Equal(1, storefront.Variants.Count(item => item.SKU == VarCon04BaselineSkuA));
        Assert.Equal(1, storefront.Variants.Count(item => item.SKU == VarCon04BaselineSkuB));
        Assert.Equal(0, storefront.Variants.Count(item => item.SKU == VarCon04ContestedSku));
        Assert.Equal(VarCon04VariantBOnlinePrice, affiliate.MaxVariantPrice);
        Assert.Equal(seeded.VariantAId, commerceHub.VariantId);
        Assert.Equal(VarCon04VariantAOnlinePrice, commerceHub.Price);

        await AssertVarCon04HistoricalLinkageAsync(verifyContext, seeded, expectContestedSkuOwner: false);

        progressEntries.Enqueue("verification:var-con-04-active-overlap-baseline-only-visible");
    }

    private static async Task AssertVarCon02ConvergenceAsync(
        TestDatabaseScope scope,
        string connectionString,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded,
        VariantActor originalActor,
        VariantActor replayActor,
        IReadOnlyCollection<VariantActorResult> results,
        ConcurrentQueue<string> progressEntries)
    {
        var original = results.Single(result => result.Actor.Name == originalActor.Name);
        var replay = results.Single(result => result.Actor.Name == replayActor.Name);

        Assert.True(original.Succeeded, original.PayloadJson ?? "VAR-CON-02 original request did not commit.");
        Assert.True(replay.Succeeded, replay.PayloadJson ?? "VAR-CON-02 replay did not converge to the committed mutation.");
        Assert.Equal(StatusCodes.Status200OK, original.StatusCode);
        Assert.Equal(StatusCodes.Status200OK, replay.StatusCode);
        Assert.NotNull(original.Response);
        Assert.NotNull(replay.Response);
        Assert.Equal(seeded.VariantId, original.Response!.Id);
        Assert.Equal(seeded.VariantId, replay.Response!.Id);
        Assert.Equal(VarCon02MutatedSku, original.Response.SKU);
        Assert.Equal(VarCon02MutatedSku, replay.Response.SKU);
        Assert.Equal(VarCon02MutatedBarcode, original.Response.Barcode);
        Assert.Equal(VarCon02MutatedBarcode, replay.Response.Barcode);

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var variant = await verifyContext.ProductVariants
            .AsNoTracking()
            .Include(item => item.Attributes)
            .Include(item => item.Stocks)
            .SingleAsync(item => item.Id == seeded.VariantId);
        var combinationOwnerCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .CountAsync(item =>
                item.TenantId == seeded.TenantId &&
                item.ProductId == seeded.ProductId &&
                item.AttributeCombinationKey == seeded.MutatedCombinationKey);
        var staleCombinationOwnerCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .CountAsync(item =>
                item.TenantId == seeded.TenantId &&
                item.ProductId == seeded.ProductId &&
                item.AttributeCombinationKey == seeded.BaselineCombinationKey);
        var skuOwnerCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .CountAsync(item => item.SKU == VarCon02MutatedSku);
        var staleSkuOwnerCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .CountAsync(item => item.SKU == VarCon02BaselineSku);
        var barcodeOwnerCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .CountAsync(item => item.Barcode == VarCon02MutatedBarcode);
        var staleBarcodeOwnerCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .CountAsync(item => item.Barcode == VarCon02BaselineBarcode);
        var stockRows = await verifyContext.ProductVariantStocks
            .AsNoTracking()
            .Where(stock => stock.VariantId == seeded.VariantId)
            .ToListAsync();
        var idempotencyRecords = await verifyContext.ProductRequestIdempotencyRecords
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(record =>
                record.TenantId == seeded.TenantId &&
                record.ProductId == seeded.ProductId &&
                record.ProductVariantId == seeded.VariantId &&
                record.Operation == "UPDATE_VARIANT")
            .ToListAsync();
        var outboxMessages = await verifyContext.OutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.AggregateId == seeded.VariantId)
            .ToListAsync();
        var processedEventCount = await verifyContext.Set<ProcessedEvent>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(processed =>
                processed.TenantId == seeded.TenantId &&
                processed.EntityId == seeded.VariantId);

        Assert.Equal(VarCon02MutatedSku, variant.SKU);
        Assert.Equal(VarCon02MutatedBarcode, variant.Barcode);
        Assert.Equal(seeded.MutatedCombinationKey, variant.AttributeCombinationKey);
        Assert.Equal(VarCon02MutatedPrice, variant.Price);
        Assert.Equal(VarCon02MutatedOnlinePrice, variant.OnlinePrice);
        Assert.Equal(VarCon02MutatedCostPrice, variant.CostPrice);
        Assert.Equal(1, combinationOwnerCount);
        Assert.Equal(0, staleCombinationOwnerCount);
        Assert.Equal(1, skuOwnerCount);
        Assert.Equal(0, staleSkuOwnerCount);
        Assert.Equal(1, barcodeOwnerCount);
        Assert.Equal(0, staleBarcodeOwnerCount);
        Assert.Single(stockRows);
        Assert.Equal(VarCon02StockId, stockRows.Single().Id);
        Assert.Equal(VarCon02StockQuantity, stockRows.Single().Quantity);
        Assert.Single(idempotencyRecords);
        Assert.Equal(2, outboxMessages.Count);
        Assert.Single(outboxMessages.Where(message => message.EventType == typeof(ProductVariantUpdatedEvent).FullName));
        Assert.Single(outboxMessages.Where(message => message.EventType == typeof(VariantPriceUpdatedEvent).FullName));
        Assert.Equal(0, processedEventCount);
        Assert.Equal(0, await CountOrphanVariantAttributesAsync(verifyContext));
        Assert.Equal(0, await CountOrphanVariantStocksAsync(verifyContext));

        var idempotency = idempotencyRecords.Single();
        Assert.Equal(BuildProductMutationIdempotencyKey(seeded.TenantId, VarCon02SharedRequestId), idempotency.Key);
        Assert.Equal(StatusCodes.Status200OK, idempotency.ResponseStatusCode);
        Assert.NotNull(idempotency.CompletedAtUtc);
        Assert.Contains(seeded.VariantId.ToString("D"), idempotency.ResponseJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(VarCon02MutatedSku, idempotency.ResponseJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var storefront = await LoadVarCon02StorefrontDocumentAsync(mongoDatabase, seeded);
        var affiliate = await LoadVarCon02AffiliateDocumentAsync(mongoDatabase, seeded);
        var commerceHub = await LoadVarCon02CommerceHubDocumentAsync(mongoDatabase, seeded);
        var storefrontVariant = Assert.Single(storefront.Variants.Where(item => item.VariantId == seeded.VariantId));
        Assert.Equal(VarCon02MutatedSku, storefrontVariant.SKU);
        Assert.Equal(VarCon02MutatedBarcode, storefrontVariant.Barcode);
        Assert.Equal(VarCon02MutatedOnlinePrice, storefrontVariant.EffectivePrice);
        Assert.Equal([("Color", "Red"), ("Size", "XL")], storefrontVariant.Attributes
            .OrderBy(attribute => attribute.SortOrder)
            .Select(attribute => (attribute.Name, attribute.Value))
            .ToArray());
        Assert.Equal(VarCon02MutatedOnlinePrice, affiliate.MaxVariantPrice);
        Assert.Equal(VarCon02MutatedOnlinePrice, commerceHub.Price);
        Assert.Equal(seeded.VariantId, commerceHub.VariantId);

        await AssertVarCon02PostCommitReplaySafetyAsync(
            connectionString,
            tenantContext,
            seeded,
            originalActor,
            progressEntries);

        progressEntries.Enqueue("verification:var-con-02-converged-to-one-mutated-variant-owner");
    }

    private static async Task AssertVarCon04ConvergenceAsync(
        TestDatabaseScope scope,
        string connectionString,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon04Scenario seeded,
        VariantActor actorA,
        VariantActor actorB,
        IReadOnlyCollection<VariantActorResult> results,
        ConcurrentQueue<string> progressEntries)
    {
        var winner = results.Single(result => result.Actor.Name == actorA.Name);
        var loser = results.Single(result => result.Actor.Name == actorB.Name);

        Assert.True(winner.Succeeded, winner.PayloadJson ?? "VAR-CON-04 winning SKU reassignment did not commit.");
        Assert.False(loser.Succeeded, loser.PayloadJson ?? "VAR-CON-04 losing SKU reassignment unexpectedly succeeded.");
        Assert.Equal(StatusCodes.Status200OK, winner.StatusCode);
        Assert.Equal(StatusCodes.Status409Conflict, loser.StatusCode);
        Assert.NotNull(winner.Response);
        Assert.Equal(seeded.VariantAId, winner.Response!.Id);
        Assert.Equal(VarCon04ContestedSku, winner.Response.SKU);
        Assert.Equal(VarCon04WinningBarcode, winner.Response.Barcode);
        Assert.Contains("VariantIdentityConflict", loser.PayloadJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        await using (var verifyContext = scope.CreateDbContext(tenantContext))
        {
            var variants = await verifyContext.ProductVariants
                .AsNoTracking()
                .Include(item => item.Attributes)
                .Include(item => item.Stocks)
                .Where(item => item.TenantId == seeded.TenantId && item.ProductId == seeded.ProductId)
                .ToDictionaryAsync(item => item.Id);
            var variantA = variants[seeded.VariantAId];
            var variantB = variants[seeded.VariantBId];
            var contestedSkuOwnerCount = await verifyContext.ProductVariants
                .AsNoTracking()
                .CountAsync(item => item.SKU == VarCon04ContestedSku);
            var baselineSkuAOwnerCount = await verifyContext.ProductVariants
                .AsNoTracking()
                .CountAsync(item => item.SKU == VarCon04BaselineSkuA);
            var baselineSkuBOwnerCount = await verifyContext.ProductVariants
                .AsNoTracking()
                .CountAsync(item => item.SKU == VarCon04BaselineSkuB);
            var idempotencyRecords = await verifyContext.ProductRequestIdempotencyRecords
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(record =>
                    record.TenantId == seeded.TenantId &&
                    record.ProductId == seeded.ProductId &&
                    record.Operation == "UPDATE_VARIANT")
                .OrderBy(record => record.Key)
                .ToListAsync();
            var outboxMessages = await verifyContext.OutboxMessages
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(message =>
                    message.TenantId == seeded.TenantId &&
                    (message.AggregateId == seeded.VariantAId ||
                     message.AggregateId == seeded.VariantBId))
                .ToListAsync();

            Assert.Equal(VarCon04ContestedSku, variantA.SKU);
            Assert.Equal(VarCon04WinningBarcode, variantA.Barcode);
            Assert.Equal(seeded.VariantACombinationKey, variantA.AttributeCombinationKey);
            Assert.Equal(VarCon04BaselineSkuB, variantB.SKU);
            Assert.Equal(VarCon04BaselineBarcodeB, variantB.Barcode);
            Assert.Equal(seeded.VariantBCombinationKey, variantB.AttributeCombinationKey);
            Assert.Equal(1, contestedSkuOwnerCount);
            Assert.Equal(0, baselineSkuAOwnerCount);
            Assert.Equal(1, baselineSkuBOwnerCount);
            Assert.Equal(VarCon04VariantAPrice, variantA.Price);
            Assert.Equal(VarCon04VariantAOnlinePrice, variantA.OnlinePrice);
            Assert.Equal(VarCon04VariantACostPrice, variantA.CostPrice);
            Assert.Equal(VarCon04VariantBPrice, variantB.Price);
            Assert.Equal(VarCon04VariantBOnlinePrice, variantB.OnlinePrice);
            Assert.Equal(VarCon04VariantBCostPrice, variantB.CostPrice);
            Assert.Equal(VarCon04VariantAStockId, Assert.Single(variantA.Stocks).Id);
            Assert.Equal(VarCon04VariantAStockQuantity, variantA.Stocks.Single().Quantity);
            Assert.Equal(VarCon04VariantBStockId, Assert.Single(variantB.Stocks).Id);
            Assert.Equal(VarCon04VariantBStockQuantity, variantB.Stocks.Single().Quantity);
            Assert.Single(idempotencyRecords);
            Assert.Equal(BuildProductMutationIdempotencyKey(seeded.TenantId, VarCon04ActorARequestId), idempotencyRecords.Single().Key);
            Assert.Equal(StatusCodes.Status200OK, idempotencyRecords.Single().ResponseStatusCode);
            Assert.NotNull(idempotencyRecords.Single().CompletedAtUtc);
            Assert.Single(outboxMessages.Where(message => message.EventType == typeof(ProductVariantUpdatedEvent).FullName));
            Assert.Equal(0, await CountOrphanVariantAttributesAsync(verifyContext));
            Assert.Equal(0, await CountOrphanVariantStocksAsync(verifyContext));

            await AssertVarCon04HistoricalLinkageAsync(verifyContext, seeded, expectContestedSkuOwner: true);
        }

        await AssertVarCon04VisibilityConvergedAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            progressEntries);

        await AssertVarCon04PostCommitReplaySafetyAsync(
            connectionString,
            tenantContext,
            seeded,
            actorA,
            progressEntries);

        var projectionEvent = await BuildVarCon04VariantProjectionEventFromSqlAsync(
            scope,
            tenantContext,
            seeded,
            VarCon04EventId,
            "var-con-04-duplicate-delivery");
        var duplicateWorkerWindow = new VariantProjectionDeliveryOverlapWindow(
            TimeSpan.FromSeconds(30),
            progressEntries);
        var duplicateReplay = await ExecuteVarCon03ProjectionWorkerAsync(
            connectionString,
            tenantContext,
            scope,
            mongoDatabase,
            seeded.ToVisibilityScenario(),
            projectionEvent,
            new VariantProjectionWorker("var-con-04-post-commit-projection"),
            duplicateWorkerWindow,
            pauseBeforeProjectionCompletion: false,
            progressEntries);
        var duplicateReplayAgain = await ExecuteVarCon03ProjectionWorkerAsync(
            connectionString,
            tenantContext,
            scope,
            mongoDatabase,
            seeded.ToVisibilityScenario(),
            projectionEvent,
            new VariantProjectionWorker("var-con-04-duplicate-projection"),
            duplicateWorkerWindow,
            pauseBeforeProjectionCompletion: false,
            progressEntries);

        Assert.Null(duplicateReplay.Exception);
        Assert.Null(duplicateReplayAgain.Exception);
        Assert.Equal(VariantProjectionWorkerOutcome.Processed, duplicateReplay.Outcome);
        Assert.Equal(VariantProjectionWorkerOutcome.DuplicateSkipped, duplicateReplayAgain.Outcome);

        await using (var processedContext = scope.CreateDbContext(tenantContext))
        {
            Assert.Equal(1, await CountVarCon03ProcessedEventsAsync(processedContext, VarCon04EventId));
        }

        await ApplyVarCon03StorefrontVariantProjectionEventAsync(
            mongoDatabase,
            BuildVarCon04StaleLosingProjectionEvent(seeded, projectionEvent));
        await AssertVarCon04VisibilityConvergedAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            progressEntries);

        await RebuildVarCon02VisibilityFromSqlAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded.ToVisibilityScenario(),
            progressEntries);
        await RebuildVarCon02VisibilityFromSqlAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded.ToVisibilityScenario(),
            progressEntries);
        await AssertVarCon04VisibilityConvergedAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            progressEntries);

        progressEntries.Enqueue("verification:var-con-04-sku-reassignment-race-converged-from-sql-truth");
    }

    private static async Task<VariantInventoryMutationResult> ExecuteVarCon05InventoryMutationAsync(
        string connectionString,
        TestTenantContext tenantContext,
        VarCon04Scenario seeded,
        VariantDeleteInventoryOverlapWindow overlapWindow,
        ConcurrentQueue<string> progressEntries)
    {
        await using var context = CreateDbContext(connectionString, tenantContext);
        await ConfigureSessionTimeoutsAsync(context);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            var stock = await ProductVariantStockMutationSupport.LockStockForMutationAsync(
                    context,
                    seeded.TenantId,
                    seeded.WarehouseId,
                    seeded.VariantBId,
                    createIfMissing: false)
                ?? throw new InvalidOperationException("VAR-CON-05 could not lock the target stock row.");

            ProductVariantStockMutationSupport.ValidateQuantityChange(
                stock,
                -VarCon05InventoryDeductionQuantity,
                allowNegativeStock: false,
                operationName: "VAR-CON-05 active inventory mutation");

            var oldQuantity = stock.Quantity;
            var newQuantity = oldQuantity - VarCon05InventoryDeductionQuantity;
            stock.Quantity = newQuantity;
            stock.UpdatedAt = DateTime.UtcNow;

            context.StockMovements.Add(new StockMovement
            {
                Id = VarCon05ActiveStockMovementId,
                TenantId = seeded.TenantId,
                WarehouseId = seeded.WarehouseId,
                ProductId = seeded.ProductId,
                ProductVariantId = seeded.VariantBId,
                MovementType = StockMovementType.Adjustment,
                QuantityChange = -VarCon05InventoryDeductionQuantity,
                ReservedQuantityChange = 0m,
                DamagedQuantityChange = 0m,
                QuantityAfter = newQuantity,
                AvailableAfter = newQuantity,
                ReservedAfter = 0m,
                DamagedAfter = 0m,
                ReferenceId = seeded.VariantBId,
                ReferenceType = "VAR-CON-05-ActiveInventoryMutation",
                CorrelationId = "var-con-05-active-inventory",
                IdempotencyKey = "var-con-05-active-inventory",
                Source = "VAR-CON-05",
                CreatedAt = DateTime.UtcNow
            });

            await context.SaveChangesAsync();

            overlapWindow.MarkInventoryMutationPaused();
            await overlapWindow.WaitUntilInventoryMutationReleasedAsync(CancellationToken.None);

            await ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync(
                context,
                seeded.TenantId,
                [
                    ProductVariantStockMutationSupport.BuildPendingStockChange(
                        seeded.VariantBId,
                        seeded.ProductId,
                        seeded.WarehouseId,
                        oldQuantity,
                        newQuantity,
                        "VAR-CON-05 active inventory mutation",
                        referenceId: VarCon05ActiveStockMovementId,
                        referenceType: nameof(StockMovement),
                        sku: VarCon04BaselineSkuB,
                        warehouseName: "VAR-CON-05 Warehouse")
                ]);

            await transaction.CommitAsync();
            progressEntries.Enqueue("var-con-05-inventory:committed");
            return new VariantInventoryMutationResult(Succeeded: true, Exception: null);
        }
        catch (Exception ex)
        {
            try
            {
                await transaction.RollbackAsync();
            }
            catch
            {
            }

            progressEntries.Enqueue($"var-con-05-inventory:failed-closed:{DescribeException(ex)}");
            return new VariantInventoryMutationResult(Succeeded: false, Exception: ex);
        }
    }

    private static async Task<VariantDeleteActorResult> ExecuteVarCon05DeleteActorAsync(
        string connectionString,
        TestTenantContext tenantContext,
        VarCon04Scenario seeded,
        VariantActor actor,
        VariantDeleteInventoryOverlapWindow overlapWindow,
        ConcurrentQueue<string> progressEntries)
    {
        await using var context = CreateDbContext(
            connectionString,
            tenantContext,
            new VariantDeleteCommitPauseTransactionInterceptor(overlapWindow));
        await ConfigureSessionTimeoutsAsync(context);
        var controller = CreateDeleteController(
            context,
            tenantContext,
            actor,
            new RecordingLogger<ProductVariantsController>());

        progressEntries.Enqueue($"{actor.Name}:invoking-delete-controller");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await controller.DeleteVariant(seeded.ProductId, seeded.VariantBId);
            stopwatch.Stop();
            return ParseDeleteActorResult(actor, result, stopwatch.Elapsed, progressEntries);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            progressEntries.Enqueue($"{actor.Name}:delete-threw:{DescribeException(ex)}");
            return new VariantDeleteActorResult(
                actor,
                Succeeded: false,
                StatusCode: null,
                PayloadJson: null,
                Duration: stopwatch.Elapsed,
                Exception: ex);
        }
    }

    private static async Task AssertVarCon05ActiveOverlapVisibilityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon04Scenario seeded,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var variantB = await verifyContext.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleAsync(variant => variant.Id == seeded.VariantBId);
        var stockB = await verifyContext.ProductVariantStocks
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleAsync(stock => stock.Id == VarCon04VariantBStockId);
        var activeMovementCount = await verifyContext.StockMovements
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(movement => movement.Id == VarCon05ActiveStockMovementId);
        var deleteOutboxCount = await verifyContext.OutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(message =>
                message.TenantId == seeded.TenantId &&
                message.AggregateId == seeded.VariantBId &&
                (message.EventType == typeof(ProductVariantDeletedEvent).FullName ||
                 message.EventType == typeof(VariantDeletedEvent).FullName));
        var stockSyncOutboxCount = await verifyContext.StockSyncOutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(message =>
                message.TenantId == seeded.TenantId &&
                message.VariantId == seeded.VariantBId);
        var storefront = await LoadVarCon02StorefrontDocumentAsync(
            mongoDatabase,
            seeded.ToVisibilityScenario());
        var affiliate = await LoadVarCon02AffiliateDocumentAsync(
            mongoDatabase,
            seeded.ToVisibilityScenario());
        var commerceHub = await LoadVarCon02CommerceHubDocumentAsync(
            mongoDatabase,
            seeded.ToVisibilityScenario());

        Assert.False(variantB.IsDeleted);
        Assert.Equal(VarCon04BaselineSkuB, variantB.SKU);
        Assert.Equal(VarCon04VariantBPrice, variantB.Price);
        Assert.Equal(VarCon04VariantBOnlinePrice, variantB.OnlinePrice);
        Assert.Equal(VarCon04VariantBCostPrice, variantB.CostPrice);
        Assert.Equal(VarCon04VariantBStockQuantity, stockB.Quantity);
        Assert.Equal(0m, stockB.ReservedQuantity);
        Assert.Equal(0, activeMovementCount);
        Assert.Equal(0, deleteOutboxCount);
        Assert.Equal(0, stockSyncOutboxCount);
        Assert.Equal(1, storefront.Variants.Count(variant => variant.VariantId == seeded.VariantBId));
        Assert.Equal(VarCon04VariantBOnlinePrice, affiliate.MaxVariantPrice);
        Assert.Equal(seeded.VariantAId, commerceHub.VariantId);
        Assert.Equal(VarCon04VariantAOnlinePrice, commerceHub.Price);

        await AssertVarCon05LinkageIntegrityAsync(
            verifyContext,
            seeded,
            expectVariantBDeleted: false,
            expectActiveMovement: false);

        progressEntries.Enqueue("verification:var-con-05-active-overlap-no-partial-delete-or-inventory-visible");
    }

    private static async Task AssertVarCon05ConvergenceAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon04Scenario seeded,
        ConcurrentQueue<string> progressEntries)
    {
        await using (var verifyContext = scope.CreateDbContext(tenantContext))
        {
            var variantA = await verifyContext.ProductVariants
                .AsNoTracking()
                .IgnoreQueryFilters()
                .SingleAsync(variant => variant.Id == seeded.VariantAId);
            var variantB = await verifyContext.ProductVariants
                .AsNoTracking()
                .IgnoreQueryFilters()
                .SingleAsync(variant => variant.Id == seeded.VariantBId);
            var activeVariantCount = await verifyContext.ProductVariants
                .AsNoTracking()
                .CountAsync(variant =>
                    variant.TenantId == seeded.TenantId &&
                    variant.ProductId == seeded.ProductId);
            var stockB = await verifyContext.ProductVariantStocks
                .AsNoTracking()
                .IgnoreQueryFilters()
                .SingleAsync(stock => stock.Id == VarCon04VariantBStockId);
            var activeMovementCount = await verifyContext.StockMovements
                .AsNoTracking()
                .IgnoreQueryFilters()
                .CountAsync(movement => movement.Id == VarCon05ActiveStockMovementId);
            var deleteOutboxCount = await verifyContext.OutboxMessages
                .AsNoTracking()
                .IgnoreQueryFilters()
                .CountAsync(message =>
                    message.TenantId == seeded.TenantId &&
                    message.AggregateId == seeded.VariantBId &&
                    (message.EventType == typeof(ProductVariantDeletedEvent).FullName ||
                     message.EventType == typeof(VariantDeletedEvent).FullName));
            var processedEventCount = await verifyContext.ProcessedEvents
                .AsNoTracking()
                .IgnoreQueryFilters()
                .CountAsync(processed =>
                    processed.TenantId == seeded.TenantId &&
                    processed.EntityId == seeded.VariantBId);

            Assert.False(variantA.IsDeleted);
            Assert.True(variantB.IsDeleted);
            Assert.Equal(1, activeVariantCount);
            Assert.Equal(VarCon04BaselineSkuB, variantB.SKU);
            Assert.Equal(VarCon04VariantBPrice, variantB.Price);
            Assert.Equal(VarCon04VariantBOnlinePrice, variantB.OnlinePrice);
            Assert.Equal(VarCon04VariantBCostPrice, variantB.CostPrice);
            Assert.Equal(VarCon04VariantBStockQuantity, stockB.Quantity);
            Assert.Equal(0m, stockB.ReservedQuantity);
            Assert.Equal(0, activeMovementCount);
            Assert.Equal(2, deleteOutboxCount);
            Assert.Equal(0, processedEventCount);
            Assert.Equal(0, await CountOrphanVariantAttributesAsync(verifyContext));
            Assert.Equal(0, await CountOrphanVariantStocksAsync(verifyContext));
            Assert.Equal(0, await CountOrphanStockMovementsAsync(verifyContext));

            await AssertVarCon05LinkageIntegrityAsync(
                verifyContext,
                seeded,
                expectVariantBDeleted: true,
                expectActiveMovement: false);
        }

        await AssertVarCon05VisibilityConvergedAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            progressEntries);

        await ApplyVarCon05StaleStockReplayAsync(mongoDatabase, seeded);
        await ApplyVarCon05StaleCreateReplayAsync(mongoDatabase, seeded);
        await AssertVarCon05VisibilityConvergedAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            progressEntries);

        await RebuildVarCon02VisibilityFromSqlAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded.ToVisibilityScenario(),
            progressEntries);
        await ApplyVarCon05StaleCreateReplayAsync(mongoDatabase, seeded);
        await AssertVarCon05VisibilityConvergedAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            progressEntries);

        progressEntries.Enqueue("verification:var-con-05-delete-inventory-overlap-converged-from-sql-truth");
    }

    private static async Task AssertVarCon05LinkageIntegrityAsync(
        ApplicationDbContext verifyContext,
        VarCon04Scenario seeded,
        bool expectVariantBDeleted,
        bool expectActiveMovement)
    {
        var orderItem = await verifyContext.OrderItems
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleAsync(item => item.Id == VarCon04OrderItemId);
        var historicalMovement = await verifyContext.StockMovements
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleAsync(item => item.Id == VarCon05HistoricalStockMovementId);
        var activeMovementCount = await verifyContext.StockMovements
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(item => item.Id == VarCon05ActiveStockMovementId);
        var variantB = await verifyContext.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleAsync(item => item.Id == seeded.VariantBId);
        var stockB = await verifyContext.ProductVariantStocks
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleAsync(item => item.Id == VarCon04VariantBStockId);

        Assert.Equal(seeded.VariantBId, orderItem.VariantId);
        Assert.Equal(VarCon04BaselineSkuB, orderItem.SKU);
        Assert.Equal(seeded.VariantBId, historicalMovement.ProductVariantId);
        Assert.Equal(VarCon04VariantBStockQuantity, historicalMovement.QuantityAfter);
        Assert.Equal(seeded.VariantBId, stockB.VariantId);
        Assert.Equal(expectVariantBDeleted, variantB.IsDeleted);
        Assert.Equal(expectActiveMovement ? 1 : 0, activeMovementCount);
    }

    private static async Task AssertVarCon05VisibilityConvergedAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon04Scenario seeded,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var storefront = await LoadVarCon02StorefrontDocumentAsync(
            mongoDatabase,
            seeded.ToVisibilityScenario());
        var affiliate = await LoadVarCon02AffiliateDocumentAsync(
            mongoDatabase,
            seeded.ToVisibilityScenario());
        var commerceHub = await LoadVarCon02CommerceHubDocumentAsync(
            mongoDatabase,
            seeded.ToVisibilityScenario());

        Assert.Equal(1, storefront.Variants.Count);
        var visibleVariant = Assert.Single(storefront.Variants);
        Assert.Equal(seeded.VariantAId, visibleVariant.VariantId);
        Assert.Equal(VarCon04BaselineSkuA, visibleVariant.SKU);
        Assert.Equal(0, storefront.Variants.Count(variant => variant.VariantId == seeded.VariantBId));
        Assert.Contains(
            storefront.DeletedVariantTombstones,
            tombstone => tombstone.VariantId == seeded.VariantBId);
        Assert.Equal(VarCon04VariantAOnlinePrice, affiliate.MinVariantPrice);
        Assert.Equal(VarCon04VariantAOnlinePrice, affiliate.MaxVariantPrice);
        Assert.Equal((int)VarCon04VariantAStockQuantity, affiliate.TotalAvailableStock);
        Assert.True(affiliate.IsActive);
        Assert.Equal(seeded.VariantAId, commerceHub.VariantId);
        Assert.Equal(VarCon04VariantAOnlinePrice, commerceHub.Price);
        Assert.Equal(VarCon04VariantAStockQuantity, commerceHub.AvailableStock);
        Assert.True(commerceHub.IsPublished);

        await AssertVarCon05LinkageIntegrityAsync(
            verifyContext,
            seeded,
            expectVariantBDeleted: true,
            expectActiveMovement: false);

        progressEntries.Enqueue("verification:var-con-05-visibility-converged-without-deleted-variant");
    }

    private static async Task ApplyVarCon05DeleteProjectionEventAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon04Scenario seeded)
    {
        await using var context = scope.CreateDbContext(tenantContext);
        var variant = await context.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleAsync(item => item.Id == seeded.VariantBId);

        var deleteEvent = new ProductVariantDeletedEvent
        {
            EventId = VarCon05DeleteEventId,
            OccurredAtUtc = variant.UpdatedAt ?? DateTime.UtcNow,
            TenantId = seeded.TenantId,
            CorrelationId = "var-con-05-delete-projection",
            IdempotencyKey = $"var-con-05-delete-projection:{seeded.VariantBId}",
            EventVersion = ProductEventVersionHelper.ToProductEventVersion(variant.ProjectionMutationVersion),
            ProductId = seeded.ProductId,
            VariantId = seeded.VariantBId,
            SKU = variant.SKU
        };

        var consumer = new ProductVariantProjectionConsumer(
            mongoDatabase,
            Mock.Of<IStorefrontProductCacheInvalidator>(),
            CreateProductSearchIndexServiceMock().Object,
            NullLogger<ProductVariantProjectionConsumer>.Instance);

        await consumer.Consume(BuildConsumeContext(deleteEvent).Object);
    }

    private static async Task ApplyVarCon05StaleStockReplayAsync(
        IMongoDatabase mongoDatabase,
        VarCon04Scenario seeded)
    {
        var stockEvent = new ProductVariantStockChangedEvent
        {
            EventId = VarCon05StaleStockEventId,
            OccurredAtUtc = SeededUtc.AddMinutes(-10),
            TenantId = seeded.TenantId,
            VariantId = seeded.VariantBId,
            ProductId = seeded.ProductId,
            SKU = VarCon04BaselineSkuB,
            WarehouseId = seeded.WarehouseId,
            WarehouseName = "VAR-CON-05 Warehouse",
            OldQuantity = VarCon04VariantBStockQuantity,
            NewQuantity = VarCon04VariantBStockQuantity + 100m,
            Reason = "VAR-CON-05 stale stock replay",
            ReferenceId = VarCon05ActiveStockMovementId,
            ReferenceType = nameof(StockMovement),
            MutationSource = MutationSources.POS,
            TotalStockQuantity = VarCon04VariantBStockQuantity + 100m,
            StockUpdatedAtUtc = SeededUtc.AddMinutes(-10),
            EventVersion = 0,
            CorrelationId = "var-con-05-stale-stock-replay",
            IdempotencyKey = $"var-con-05-stale-stock-replay:{seeded.VariantBId}"
        };

        var consumer = new ProductVariantProjectionConsumer(
            mongoDatabase,
            Mock.Of<IStorefrontProductCacheInvalidator>(),
            CreateProductSearchIndexServiceMock().Object,
            NullLogger<ProductVariantProjectionConsumer>.Instance);

        await consumer.Consume(BuildConsumeContext(stockEvent).Object);
    }

    private static async Task ApplyVarCon05StaleCreateReplayAsync(
        IMongoDatabase mongoDatabase,
        VarCon04Scenario seeded)
    {
        var createEvent = new ProductVariantCreatedEvent
        {
            EventId = VarCon05StaleCreateEventId,
            OccurredAtUtc = SeededUtc.AddMinutes(-20),
            TenantId = seeded.TenantId,
            CorrelationId = "var-con-05-stale-create-replay",
            IdempotencyKey = $"var-con-05-stale-create-replay:{seeded.VariantBId}",
            EventVersion = 0,
            ProductId = seeded.ProductId,
            VariantId = seeded.VariantBId,
            SKU = VarCon04BaselineSkuB,
            Barcode = VarCon04BaselineBarcodeB,
            Price = VarCon04VariantBPrice,
            OnlinePrice = VarCon04VariantBOnlinePrice,
            CostPrice = VarCon04VariantBCostPrice,
            IsDefault = false,
            IsActive = true,
            MediaId = MediaId,
            SortOrder = 20,
            Attributes =
            [
                new VariantAttributeData { Name = "Color", Value = "Blue", SortOrder = 0 },
                new VariantAttributeData { Name = "Size", Value = "L", SortOrder = 1 }
            ]
        };

        var consumer = new ProductVariantProjectionConsumer(
            mongoDatabase,
            Mock.Of<IStorefrontProductCacheInvalidator>(),
            CreateProductSearchIndexServiceMock().Object,
            NullLogger<ProductVariantProjectionConsumer>.Instance);

        await consumer.Consume(BuildConsumeContext(createEvent).Object);
    }

    private static async Task AssertVarCon03SqlMutatedButProjectionBaselineAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded,
        VarCon03VisibilitySnapshot baselineVisibility,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var variant = await verifyContext.ProductVariants
            .AsNoTracking()
            .Include(item => item.Attributes)
            .Include(item => item.Stocks)
            .SingleAsync(item => item.Id == seeded.VariantId);

        Assert.Equal(VarCon02MutatedSku, variant.SKU);
        Assert.Equal(VarCon02MutatedBarcode, variant.Barcode);
        Assert.Equal(seeded.MutatedCombinationKey, variant.AttributeCombinationKey);
        Assert.Equal(VarCon02MutatedPrice, variant.Price);
        Assert.Equal(VarCon02MutatedOnlinePrice, variant.OnlinePrice);
        Assert.Equal(VarCon02MutatedCostPrice, variant.CostPrice);
        Assert.Single(variant.Stocks);
        Assert.Equal(VarCon02StockId, variant.Stocks.Single().Id);

        var currentVisibility = await CaptureVarCon03VisibilityAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded);

        Assert.Equal(baselineVisibility, currentVisibility);
        progressEntries.Enqueue("verification:var-con-03-sql-mutated-projections-still-baseline");
    }

    private static async Task AssertVarCon04HistoricalLinkageAsync(
        ApplicationDbContext verifyContext,
        VarCon04Scenario seeded,
        bool expectContestedSkuOwner)
    {
        var orderItem = await verifyContext.OrderItems
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleAsync(item => item.Id == VarCon04OrderItemId);
        var saleItem = await verifyContext.SalesInvoiceItems
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleAsync(item => item.Id == VarCon04SalesInvoiceItemId);
        var movement = await verifyContext.StockMovements
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleAsync(item => item.Id == VarCon04StockMovementId);
        var stocks = await verifyContext.ProductVariantStocks
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(stock =>
                stock.TenantId == seeded.TenantId &&
                (stock.VariantId == seeded.VariantAId ||
                 stock.VariantId == seeded.VariantBId))
            .OrderBy(stock => stock.VariantId)
            .ToListAsync();

        Assert.Equal(seeded.VariantBId, orderItem.VariantId);
        Assert.Equal(VarCon04BaselineSkuB, orderItem.SKU);
        Assert.Equal(seeded.WarehouseId, orderItem.WarehouseId);
        Assert.Equal(seeded.VariantAId, saleItem.ProductVariantId);
        Assert.Equal(VarCon04VariantACostPrice, saleItem.CostPriceSnapshot);
        Assert.Equal(VarCon04VariantACostPrice, saleItem.CostAmountSnapshot);
        Assert.Equal(seeded.VariantAId, movement.ProductVariantId);
        Assert.Equal(VarCon04VariantAStockQuantity, movement.QuantityAfter);
        Assert.Equal(2, stocks.Count);
        Assert.Contains(stocks, stock => stock.Id == VarCon04VariantAStockId && stock.VariantId == seeded.VariantAId);
        Assert.Contains(stocks, stock => stock.Id == VarCon04VariantBStockId && stock.VariantId == seeded.VariantBId);

        var contestedOwnerCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(variant => variant.SKU == VarCon04ContestedSku);
        Assert.Equal(expectContestedSkuOwner ? 1 : 0, contestedOwnerCount);
    }

    private static async Task AssertVarCon04VisibilityConvergedAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon04Scenario seeded,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var variantA = await verifyContext.ProductVariants
            .AsNoTracking()
            .SingleAsync(variant => variant.Id == seeded.VariantAId);
        var variantB = await verifyContext.ProductVariants
            .AsNoTracking()
            .SingleAsync(variant => variant.Id == seeded.VariantBId);
        var storefront = await LoadVarCon02StorefrontDocumentAsync(
            mongoDatabase,
            seeded.ToVisibilityScenario());
        var affiliate = await LoadVarCon02AffiliateDocumentAsync(
            mongoDatabase,
            seeded.ToVisibilityScenario());
        var commerceHub = await LoadVarCon02CommerceHubDocumentAsync(
            mongoDatabase,
            seeded.ToVisibilityScenario());

        Assert.Equal(VarCon04ContestedSku, variantA.SKU);
        Assert.Equal(VarCon04BaselineSkuB, variantB.SKU);
        Assert.Equal(1, storefront.Variants.Count(variant => variant.SKU == VarCon04ContestedSku));
        Assert.Equal(0, storefront.Variants.Count(variant => variant.SKU == VarCon04BaselineSkuA));
        Assert.Equal(1, storefront.Variants.Count(variant => variant.SKU == VarCon04BaselineSkuB));

        var visibleVariantA = Assert.Single(storefront.Variants.Where(variant => variant.VariantId == seeded.VariantAId));
        var visibleVariantB = Assert.Single(storefront.Variants.Where(variant => variant.VariantId == seeded.VariantBId));
        Assert.Equal(VarCon04ContestedSku, visibleVariantA.SKU);
        Assert.Equal(VarCon04WinningBarcode, visibleVariantA.Barcode);
        Assert.Equal(VarCon04VariantAOnlinePrice, visibleVariantA.EffectivePrice);
        Assert.Equal(VarCon04VariantAStockQuantity, visibleVariantA.TotalStock);
        Assert.Equal(VarCon04BaselineSkuB, visibleVariantB.SKU);
        Assert.Equal(VarCon04BaselineBarcodeB, visibleVariantB.Barcode);
        Assert.Equal(VarCon04VariantBOnlinePrice, visibleVariantB.EffectivePrice);
        Assert.Equal(VarCon04VariantBStockQuantity, visibleVariantB.TotalStock);
        Assert.Equal(VarCon04VariantBOnlinePrice, affiliate.MaxVariantPrice);
        Assert.Equal((int)(VarCon04VariantAStockQuantity + VarCon04VariantBStockQuantity), affiliate.TotalAvailableStock);
        Assert.Equal(seeded.VariantAId, commerceHub.VariantId);
        Assert.Equal(VarCon04VariantAOnlinePrice, commerceHub.Price);
        Assert.Equal(VarCon04VariantAStockQuantity, commerceHub.AvailableStock);
        Assert.True(affiliate.IsActive);
        Assert.True(commerceHub.IsPublished);

        await AssertVarCon04HistoricalLinkageAsync(verifyContext, seeded, expectContestedSkuOwner: true);

        progressEntries.Enqueue("verification:var-con-04-visibility-converged-from-sql-truth");
    }

    private static async Task AssertVarCon04PostCommitReplaySafetyAsync(
        string connectionString,
        TestTenantContext tenantContext,
        VarCon04Scenario seeded,
        VariantActor actor,
        ConcurrentQueue<string> progressEntries)
    {
        await using var beforeContext = CreateDbContext(connectionString, tenantContext);
        var before = await SnapshotVarCon04DurableStateAsync(beforeContext, seeded);

        await using var replayContext = CreateDbContext(connectionString, tenantContext);
        await ConfigureSessionTimeoutsAsync(replayContext);
        var replayController = CreateUpdateController(
            replayContext,
            tenantContext,
            actor,
            new RecordingLogger<ProductVariantsController>());
        var replayResult = await replayController.UpdateVariant(
            seeded.ProductId,
            seeded.VariantAId,
            BuildVarCon04UpdateRequest(seeded, winningVariant: true));
        var replayParsed = ParseUpdateActorResult(
            actor,
            replayResult,
            TimeSpan.Zero,
            progressEntries);

        Assert.True(replayParsed.Succeeded, replayParsed.PayloadJson ?? "Post-commit VAR-CON-04 replay did not return the committed SKU owner.");
        Assert.NotNull(replayParsed.Response);
        Assert.Equal(seeded.VariantAId, replayParsed.Response!.Id);
        Assert.Equal(VarCon04ContestedSku, replayParsed.Response.SKU);
        Assert.Equal(VarCon04WinningBarcode, replayParsed.Response.Barcode);

        await using var afterContext = CreateDbContext(connectionString, tenantContext);
        var after = await SnapshotVarCon04DurableStateAsync(afterContext, seeded);

        Assert.Equal(before, after);
        progressEntries.Enqueue("verification:var-con-04-post-commit-replay-idempotent");
    }

    private static async Task<VarCon04DurableSnapshot> SnapshotVarCon04DurableStateAsync(
        ApplicationDbContext context,
        VarCon04Scenario seeded)
    {
        var variants = await context.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == seeded.TenantId &&
                item.ProductId == seeded.ProductId)
            .OrderBy(item => item.Id)
            .Select(item => $"{item.Id}:{item.SKU}:{item.Barcode}:{item.ProjectionMutationVersion}")
            .ToArrayAsync();
        var stocks = await context.ProductVariantStocks
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == seeded.TenantId &&
                (item.VariantId == seeded.VariantAId ||
                 item.VariantId == seeded.VariantBId))
            .OrderBy(item => item.Id)
            .Select(item => $"{item.Id}:{item.VariantId}:{item.Quantity}:{item.ReservedQuantity}")
            .ToArrayAsync();
        var idempotency = await context.ProductRequestIdempotencyRecords
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == seeded.TenantId &&
                item.ProductId == seeded.ProductId &&
                item.Operation == "UPDATE_VARIANT")
            .OrderBy(item => item.Id)
            .Select(item => $"{item.Id}:{item.Key}:{item.ResponseStatusCode}:{item.ProductVariantId}")
            .ToArrayAsync();
        var outbox = await context.OutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == seeded.TenantId &&
                (item.AggregateId == seeded.VariantAId ||
                 item.AggregateId == seeded.VariantBId))
            .OrderBy(item => item.Id)
            .Select(item => $"{item.Id}:{item.AggregateId}:{item.EventType}:{item.EventVersion}")
            .ToArrayAsync();
        var orderItem = await context.OrderItems
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.Id == VarCon04OrderItemId)
            .Select(item => $"{item.Id}:{item.VariantId}:{item.SKU}")
            .SingleAsync();
        var saleItem = await context.SalesInvoiceItems
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.Id == VarCon04SalesInvoiceItemId)
            .Select(item => $"{item.Id}:{item.ProductVariantId}:{item.CostPriceSnapshot}:{item.CostAmountSnapshot}")
            .SingleAsync();
        var movement = await context.StockMovements
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.Id == VarCon04StockMovementId)
            .Select(item => $"{item.Id}:{item.ProductVariantId}:{item.QuantityAfter}:{item.AvailableAfter}")
            .SingleAsync();

        return new VarCon04DurableSnapshot(
            string.Join("|", variants),
            string.Join("|", stocks),
            string.Join("|", idempotency),
            string.Join("|", outbox),
            orderItem,
            saleItem,
            movement);
    }

    private static async Task AssertVarCon03ActiveOverlapVisibilityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded,
        VarCon03VisibilitySnapshot baselineVisibility,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var processedEventCount = await CountVarCon03ProcessedEventsAsync(
            verifyContext,
            VarCon03EventId);
        var currentVisibility = await CaptureVarCon03VisibilityAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded);

        Assert.Equal(0, processedEventCount);
        Assert.Equal(baselineVisibility, currentVisibility);
        Assert.Equal(VarCon02BaselineSku, currentVisibility.StorefrontSku);
        Assert.Equal(VarCon02BaselineBarcode, currentVisibility.StorefrontBarcode);
        Assert.Equal(VarCon02BaselineOnlinePrice, currentVisibility.StorefrontEffectivePrice);
        Assert.Equal("Color=Red|Size=L", currentVisibility.StorefrontAttributeFingerprint);
        Assert.Equal(VarCon02BaselineOnlinePrice, currentVisibility.AffiliateMaxVariantPrice);
        Assert.Equal(VarCon02BaselineOnlinePrice, currentVisibility.CommerceHubPrice);
        Assert.Equal(seeded.VariantId, currentVisibility.CommerceHubVariantId);

        progressEntries.Enqueue("verification:var-con-03-active-overlap-baseline-only-visible");
    }

    private static async Task<VariantProjectionWorkerResult> ExecuteVarCon03ProjectionWorkerAsync(
        string connectionString,
        TestTenantContext tenantContext,
        TestDatabaseScope scope,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded,
        ProductVariantUpdatedEvent projectionEvent,
        VariantProjectionWorker worker,
        VariantProjectionDeliveryOverlapWindow overlapWindow,
        bool pauseBeforeProjectionCompletion,
        ConcurrentQueue<string> progressEntries)
    {
        await using var context = CreateDbContext(connectionString, tenantContext);
        await ConfigureSessionTimeoutsAsync(context);
        var stopwatch = Stopwatch.StartNew();
        var eventType = typeof(ProductVariantUpdatedEvent).FullName ?? nameof(ProductVariantUpdatedEvent);

        try
        {
            var alreadyCompleted = await context.ProcessedEvents
                .IgnoreQueryFilters()
                .AnyAsync(item =>
                    item.EventId == projectionEvent.EventId &&
                    item.ConsumerName == VarCon03ConsumerName &&
                    item.EventType == eventType);

            if (alreadyCompleted)
            {
                stopwatch.Stop();
                progressEntries.Enqueue($"{worker.Name}:duplicate-variant-projection-already-completed");
                return new VariantProjectionWorkerResult(worker.Name, VariantProjectionWorkerOutcome.DuplicateSkipped, stopwatch.Elapsed, null);
            }

            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable);

            context.ProcessedEvents.Add(ProcessedEvent.Create(
                projectionEvent.EventId,
                VarCon03ConsumerName,
                eventType,
                projectionEvent.VariantId,
                projectionEvent.TenantId,
                projectionEvent.CorrelationId ?? projectionEvent.EventId.ToString()));

            if (!pauseBeforeProjectionCompletion)
            {
                overlapWindow.MarkDuplicateClaimAttempt(worker.Name);
            }

            try
            {
                await context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                await transaction.RollbackAsync();
                stopwatch.Stop();
                progressEntries.Enqueue($"{worker.Name}:duplicate-variant-projection-collapsed-by-processed-event");
                return new VariantProjectionWorkerResult(worker.Name, VariantProjectionWorkerOutcome.DuplicateSkipped, stopwatch.Elapsed, null);
            }

            if (pauseBeforeProjectionCompletion)
            {
                overlapWindow.MarkOriginalPaused(worker.Name);
                await overlapWindow.WaitUntilReleaseAsync(CancellationToken.None);
                await ApplyVarCon03ProjectionFromSqlAsync(
                    scope,
                    tenantContext,
                    mongoDatabase,
                    seeded,
                    projectionEvent,
                    progressEntries);
            }

            await transaction.CommitAsync();
            stopwatch.Stop();
            progressEntries.Enqueue($"{worker.Name}:variant-projection-completed");
            return new VariantProjectionWorkerResult(worker.Name, VariantProjectionWorkerOutcome.Processed, stopwatch.Elapsed, null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            progressEntries.Enqueue($"{worker.Name}:threw:{DescribeException(ex)}");
            return new VariantProjectionWorkerResult(worker.Name, VariantProjectionWorkerOutcome.Failed, stopwatch.Elapsed, ex);
        }
    }

    private static async Task ApplyVarCon03ProjectionFromSqlAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded,
        ProductVariantUpdatedEvent projectionEvent,
        ConcurrentQueue<string> progressEntries)
    {
        await ApplyVarCon03StorefrontVariantProjectionEventAsync(
            mongoDatabase,
            projectionEvent);
        await RebuildVarCon02VisibilityFromSqlAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            progressEntries);

        progressEntries.Enqueue($"projection:var-con-03-applied-from-sql-truth:{projectionEvent.EventId}");
    }

    private static async Task ApplyVarCon03StorefrontVariantProjectionEventAsync(
        IMongoDatabase mongoDatabase,
        ProductVariantUpdatedEvent projectionEvent)
    {
        var consumer = new ProductVariantProjectionConsumer(
            mongoDatabase,
            Mock.Of<IStorefrontProductCacheInvalidator>(),
            CreateProductSearchIndexServiceMock().Object,
            NullLogger<ProductVariantProjectionConsumer>.Instance);

        await consumer.Consume(BuildConsumeContext(projectionEvent).Object);
    }

    private static async Task<VarCon06ProjectionDeliveryResult> ExecuteVarCon06ProjectionDeliveryAsync(
        string connectionString,
        TestTenantContext tenantContext,
        TestDatabaseScope scope,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded,
        ProductVariantUpdatedEvent projectionEvent,
        VariantProjectionWorker worker,
        ConcurrentQueue<string> progressEntries)
    {
        await using var context = CreateDbContext(connectionString, tenantContext);
        await ConfigureSessionTimeoutsAsync(context);
        var stopwatch = Stopwatch.StartNew();
        var eventType = typeof(ProductVariantUpdatedEvent).FullName ?? nameof(ProductVariantUpdatedEvent);

        try
        {
            var alreadyCompleted = await context.ProcessedEvents
                .IgnoreQueryFilters()
                .AnyAsync(item =>
                    item.EventId == projectionEvent.EventId &&
                    item.ConsumerName == VarCon03ConsumerName &&
                    item.EventType == eventType);

            if (alreadyCompleted)
            {
                stopwatch.Stop();
                progressEntries.Enqueue($"{worker.Name}:var-con-06-duplicate-projection-skipped");
                return new VarCon06ProjectionDeliveryResult(
                    new VariantProjectionWorkerResult(worker.Name, VariantProjectionWorkerOutcome.DuplicateSkipped, stopwatch.Elapsed, null),
                    new VarCon06ProjectionSideEffects(0, 0, 0));
            }

            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            context.ProcessedEvents.Add(ProcessedEvent.Create(
                projectionEvent.EventId,
                VarCon03ConsumerName,
                eventType,
                projectionEvent.VariantId,
                projectionEvent.TenantId,
                projectionEvent.CorrelationId ?? projectionEvent.EventId.ToString()));

            try
            {
                await context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                await transaction.RollbackAsync();
                stopwatch.Stop();
                progressEntries.Enqueue($"{worker.Name}:var-con-06-duplicate-projection-collapsed-by-processed-event");
                return new VarCon06ProjectionDeliveryResult(
                    new VariantProjectionWorkerResult(worker.Name, VariantProjectionWorkerOutcome.DuplicateSkipped, stopwatch.Elapsed, null),
                    new VarCon06ProjectionSideEffects(0, 0, 0));
            }

            var sideEffects = await ApplyVarCon06StorefrontVariantProjectionEventAsync(
                mongoDatabase,
                projectionEvent);
            await RebuildVarCon02VisibilityFromSqlAsync(
                scope,
                tenantContext,
                mongoDatabase,
                seeded,
                progressEntries);

            stopwatch.Stop();
            progressEntries.Enqueue($"{worker.Name}:var-con-06-projection-completed:{projectionEvent.EventVersion}");
            return new VarCon06ProjectionDeliveryResult(
                new VariantProjectionWorkerResult(worker.Name, VariantProjectionWorkerOutcome.Processed, stopwatch.Elapsed, null),
                sideEffects);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            progressEntries.Enqueue($"{worker.Name}:var-con-06-threw:{DescribeException(ex)}");
            return new VarCon06ProjectionDeliveryResult(
                new VariantProjectionWorkerResult(worker.Name, VariantProjectionWorkerOutcome.Failed, stopwatch.Elapsed, ex),
                new VarCon06ProjectionSideEffects(0, 0, 0));
        }
    }

    private static async Task<VarCon06ProjectionSideEffects> ApplyVarCon06StorefrontVariantProjectionEventAsync(
        IMongoDatabase mongoDatabase,
        ProductVariantUpdatedEvent projectionEvent)
    {
        var invalidationCount = 0;
        var indexedDocuments = new ConcurrentBag<ProductSearchDocument>();
        var deletedSearchDocuments = new ConcurrentBag<(Guid TenantId, Guid ProductId)>();
        var cacheInvalidator = new Mock<IStorefrontProductCacheInvalidator>(MockBehavior.Loose);
        cacheInvalidator
            .Setup(entry => entry.InvalidateProductAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => Interlocked.Increment(ref invalidationCount))
            .Returns(Task.CompletedTask);

        var searchIndexService = new Mock<IUnifiedSearchIndexService>(MockBehavior.Loose);
        searchIndexService
            .Setup(entry => entry.IndexProductAsync(
                It.IsAny<ProductSearchDocument>(),
                It.IsAny<CancellationToken>()))
            .Callback<ProductSearchDocument, CancellationToken>((document, _) => indexedDocuments.Add(document))
            .ReturnsAsync(true);
        searchIndexService
            .Setup(entry => entry.DeleteProductAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, CancellationToken>((tenantId, productId, _) => deletedSearchDocuments.Add((tenantId, productId)))
            .ReturnsAsync(true);

        var consumer = new ProductVariantProjectionConsumer(
            mongoDatabase,
            cacheInvalidator.Object,
            searchIndexService.Object,
            NullLogger<ProductVariantProjectionConsumer>.Instance);

        await consumer.Consume(BuildConsumeContext(projectionEvent).Object);

        return new VarCon06ProjectionSideEffects(
            invalidationCount,
            indexedDocuments.Count,
            deletedSearchDocuments.Count);
    }

    private static async Task<ProductVariantUpdatedEvent> BuildVarCon03VariantProjectionEventFromSqlAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        VarCon02Scenario seeded,
        Guid eventId,
        string correlationId)
    {
        await using var context = scope.CreateDbContext(tenantContext);
        var variant = await context.ProductVariants
            .AsNoTracking()
            .Include(item => item.Attributes)
            .SingleAsync(item => item.Id == seeded.VariantId);

        return new ProductVariantUpdatedEvent
        {
            EventId = eventId,
            OccurredAtUtc = variant.UpdatedAt ?? DateTime.UtcNow,
            TenantId = seeded.TenantId,
            CorrelationId = correlationId,
            IdempotencyKey = $"{correlationId}:{seeded.VariantId}",
            EventVersion = ProductEventVersionHelper.ToProductEventVersion(variant.ProjectionMutationVersion),
            ProductId = seeded.ProductId,
            VariantId = seeded.VariantId,
            ChangedFields =
            [
                "SKU",
                "Barcode",
                "Price",
                "OnlinePrice",
                "CostPrice",
                "IsActive",
                "MediaId",
                "SortOrder",
                "Attributes"
            ],
            SKU = variant.SKU,
            Barcode = variant.Barcode,
            Price = variant.Price,
            OnlinePrice = variant.OnlinePrice,
            CostPrice = variant.CostPrice,
            IsActive = variant.IsActive,
            MediaId = variant.MediaId,
            SortOrder = variant.SortOrder,
            Attributes = variant.Attributes
                .OrderBy(attribute => attribute.SortOrder)
                .Select(attribute => new VariantAttributeData
                {
                    Name = attribute.Name,
                    Value = attribute.Value,
                    SortOrder = attribute.SortOrder
                })
                .ToList()
        };
    }

    private static ProductVariantUpdatedEvent BuildVarCon03StaleBaselineProjectionEvent(
        VarCon02Scenario seeded,
        ProductVariantUpdatedEvent freshEvent,
        Guid? eventId = null,
        string? correlationId = null,
        string? idempotencyKey = null)
    {
        var resolvedEventId = eventId ?? Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4c115");
        var resolvedCorrelationId = correlationId ?? "var-con-03-stale-replay";

        return new ProductVariantUpdatedEvent
        {
            EventId = resolvedEventId,
            OccurredAtUtc = SeededUtc.AddMinutes(-5),
            TenantId = seeded.TenantId,
            CorrelationId = resolvedCorrelationId,
            IdempotencyKey = idempotencyKey ?? $"{resolvedCorrelationId}:{seeded.VariantId}",
            EventVersion = Math.Max(1, freshEvent.EventVersion - 1),
            ProductId = seeded.ProductId,
            VariantId = seeded.VariantId,
            ChangedFields =
            [
                "SKU",
                "Barcode",
                "Price",
                "OnlinePrice",
                "CostPrice",
                "IsActive",
                "MediaId",
                "SortOrder",
                "Attributes"
            ],
            SKU = VarCon02BaselineSku,
            Barcode = VarCon02BaselineBarcode,
            Price = VarCon02BaselinePrice,
            OnlinePrice = VarCon02BaselineOnlinePrice,
            CostPrice = VarCon02BaselineCostPrice,
            IsActive = true,
            MediaId = MediaId,
            SortOrder = 20,
            Attributes =
            [
                new VariantAttributeData { Name = "Color", Value = "Red", SortOrder = 0 },
                new VariantAttributeData { Name = "Size", Value = "L", SortOrder = 1 }
            ]
        };
    }

    private static async Task<ProductVariantUpdatedEvent> BuildVarCon04VariantProjectionEventFromSqlAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        VarCon04Scenario seeded,
        Guid eventId,
        string correlationId)
    {
        await using var context = scope.CreateDbContext(tenantContext);
        var variant = await context.ProductVariants
            .AsNoTracking()
            .Include(item => item.Attributes)
            .SingleAsync(item => item.Id == seeded.VariantAId);

        return new ProductVariantUpdatedEvent
        {
            EventId = eventId,
            OccurredAtUtc = variant.UpdatedAt ?? DateTime.UtcNow,
            TenantId = seeded.TenantId,
            CorrelationId = correlationId,
            IdempotencyKey = $"{correlationId}:{seeded.VariantAId}",
            EventVersion = ProductEventVersionHelper.ToProductEventVersion(variant.ProjectionMutationVersion),
            ProductId = seeded.ProductId,
            VariantId = seeded.VariantAId,
            ChangedFields =
            [
                "SKU",
                "Barcode",
                "Price",
                "OnlinePrice",
                "CostPrice",
                "IsActive",
                "MediaId",
                "SortOrder",
                "Attributes"
            ],
            SKU = variant.SKU,
            Barcode = variant.Barcode,
            Price = variant.Price,
            OnlinePrice = variant.OnlinePrice,
            CostPrice = variant.CostPrice,
            IsActive = variant.IsActive,
            MediaId = variant.MediaId,
            SortOrder = variant.SortOrder,
            Attributes = variant.Attributes
                .OrderBy(attribute => attribute.SortOrder)
                .Select(attribute => new VariantAttributeData
                {
                    Name = attribute.Name,
                    Value = attribute.Value,
                    SortOrder = attribute.SortOrder
                })
                .ToList()
        };
    }

    private static ProductVariantUpdatedEvent BuildVarCon04StaleLosingProjectionEvent(
        VarCon04Scenario seeded,
        ProductVariantUpdatedEvent freshEvent)
    {
        return new ProductVariantUpdatedEvent
        {
            EventId = VarCon04StaleReplayEventId,
            OccurredAtUtc = freshEvent.OccurredAtUtc.AddMinutes(-5),
            TenantId = seeded.TenantId,
            CorrelationId = "var-con-04-stale-losing-replay",
            IdempotencyKey = $"var-con-04-stale-losing-replay:{seeded.VariantBId}",
            EventVersion = 0,
            ProductId = seeded.ProductId,
            VariantId = seeded.VariantBId,
            ChangedFields =
            [
                "SKU",
                "Barcode",
                "Price",
                "OnlinePrice",
                "CostPrice",
                "IsActive",
                "MediaId",
                "SortOrder",
                "Attributes"
            ],
            SKU = VarCon04ContestedSku,
            Barcode = VarCon04LosingBarcode,
            Price = VarCon04VariantBPrice,
            OnlinePrice = VarCon04VariantBOnlinePrice,
            CostPrice = VarCon04VariantBCostPrice,
            IsActive = true,
            MediaId = MediaId,
            SortOrder = 20,
            Attributes =
            [
                new VariantAttributeData { Name = "Color", Value = "Blue", SortOrder = 0 },
                new VariantAttributeData { Name = "Size", Value = "L", SortOrder = 1 }
            ]
        };
    }

    private static async Task AssertVarCon03ConvergenceAsync(
        TestDatabaseScope scope,
        string connectionString,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded,
        ProductVariantUpdatedEvent projectionEvent,
        ProductVariantUpdatedEvent staleProjectionEvent,
        VarCon03VisibilitySnapshot baselineVisibility,
        IReadOnlyCollection<VariantProjectionWorkerResult> results,
        ConcurrentQueue<string> progressEntries)
    {
        Assert.Equal(2, results.Count);
        Assert.Single(results.Where(result => result.Outcome == VariantProjectionWorkerOutcome.Processed));
        Assert.Single(results.Where(result => result.Outcome == VariantProjectionWorkerOutcome.DuplicateSkipped));

        await AssertVarCon03MutatedVisibilityAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            projectionEvent,
            progressEntries);

        await using (var verifyContext = scope.CreateDbContext(tenantContext))
        {
            var processedEventCount = await CountVarCon03ProcessedEventsAsync(
                verifyContext,
                projectionEvent.EventId);
            var staleProcessedEventCount = await CountVarCon03ProcessedEventsAsync(
                verifyContext,
                staleProjectionEvent.EventId);
            var idempotencyCount = await verifyContext.ProductRequestIdempotencyRecords
                .AsNoTracking()
                .IgnoreQueryFilters()
                .CountAsync(record =>
                    record.TenantId == seeded.TenantId &&
                    record.ProductId == seeded.ProductId &&
                    record.ProductVariantId == seeded.VariantId &&
                    record.Operation == "UPDATE_VARIANT");
            var outboxCount = await verifyContext.OutboxMessages
                .AsNoTracking()
                .IgnoreQueryFilters()
                .CountAsync(message =>
                    message.TenantId == seeded.TenantId &&
                    message.AggregateId == seeded.VariantId);

            Assert.Equal(1, processedEventCount);
            Assert.Equal(0, staleProcessedEventCount);
            Assert.Equal(1, idempotencyCount);
            Assert.Equal(2, outboxCount);
            Assert.Equal(0, await CountOrphanVariantAttributesAsync(verifyContext));
            Assert.Equal(0, await CountOrphanVariantStocksAsync(verifyContext));
        }

        var beforeDuplicateReplay = await CaptureVarCon03VisibilityAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded);
        var duplicateReplayResult = await ExecuteVarCon03ProjectionWorkerAsync(
            connectionString,
            tenantContext,
            scope,
            mongoDatabase,
            seeded,
            projectionEvent,
            new VariantProjectionWorker("variant-projection-post-commit-duplicate"),
            new VariantProjectionDeliveryOverlapWindow(TimeSpan.FromSeconds(30), progressEntries),
            pauseBeforeProjectionCompletion: false,
            progressEntries);
        var afterDuplicateReplay = await CaptureVarCon03VisibilityAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded);

        Assert.Null(duplicateReplayResult.Exception);
        Assert.Equal(VariantProjectionWorkerOutcome.DuplicateSkipped, duplicateReplayResult.Outcome);
        Assert.Equal(beforeDuplicateReplay, afterDuplicateReplay);

        await ApplyVarCon03StorefrontVariantProjectionEventAsync(
            mongoDatabase,
            staleProjectionEvent);
        await AssertVarCon03MutatedVisibilityAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            projectionEvent,
            progressEntries);

        await RebuildVarCon02VisibilityFromSqlAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            progressEntries);
        await RebuildVarCon02VisibilityFromSqlAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            progressEntries);
        await AssertVarCon03MutatedVisibilityAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            projectionEvent,
            progressEntries);

        Assert.NotEqual(baselineVisibility, await CaptureVarCon03VisibilityAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded));

        progressEntries.Enqueue("verification:var-con-03-duplicate-delivery-converged-from-sql-truth");
    }

    private static async Task<VarCon08ProjectionWorkerResult> ExecuteVarCon08ProjectionWorkerAsync(
        string connectionString,
        TestTenantContext tenantContext,
        TestDatabaseScope scope,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded,
        ProductVariantUpdatedEvent projectionEvent,
        VariantProjectionWorker worker,
        VariantProjectionDeliveryOverlapWindow overlapWindow,
        bool pauseBeforeProjectionCompletion,
        ConcurrentQueue<string> progressEntries)
    {
        await using var context = CreateDbContext(connectionString, tenantContext);
        await ConfigureSessionTimeoutsAsync(context);
        var stopwatch = Stopwatch.StartNew();
        var eventType = typeof(ProductVariantUpdatedEvent).FullName ?? nameof(ProductVariantUpdatedEvent);

        try
        {
            var alreadyCompleted = await context.ProcessedEvents
                .IgnoreQueryFilters()
                .AnyAsync(item =>
                    item.EventId == projectionEvent.EventId &&
                    item.ConsumerName == VarCon08ConsumerName &&
                    item.EventType == eventType);

            if (alreadyCompleted)
            {
                stopwatch.Stop();
                progressEntries.Enqueue($"{worker.Name}:var-con-08-duplicate-projection-already-completed");
                return new VarCon08ProjectionWorkerResult(
                    new VariantProjectionWorkerResult(worker.Name, VariantProjectionWorkerOutcome.DuplicateSkipped, stopwatch.Elapsed, null),
                    new VarCon06ProjectionSideEffects(0, 0, 0));
            }

            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            context.ProcessedEvents.Add(ProcessedEvent.Create(
                projectionEvent.EventId,
                VarCon08ConsumerName,
                eventType,
                projectionEvent.VariantId,
                projectionEvent.TenantId,
                projectionEvent.CorrelationId ?? projectionEvent.EventId.ToString()));

            if (!pauseBeforeProjectionCompletion)
            {
                overlapWindow.MarkDuplicateClaimAttempt(worker.Name);
            }

            try
            {
                await context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                await transaction.RollbackAsync();
                stopwatch.Stop();
                progressEntries.Enqueue($"{worker.Name}:var-con-08-duplicate-collapsed-by-processed-event");
                return new VarCon08ProjectionWorkerResult(
                    new VariantProjectionWorkerResult(worker.Name, VariantProjectionWorkerOutcome.DuplicateSkipped, stopwatch.Elapsed, null),
                    new VarCon06ProjectionSideEffects(0, 0, 0));
            }

            var sideEffects = new VarCon06ProjectionSideEffects(0, 0, 0);
            if (pauseBeforeProjectionCompletion)
            {
                overlapWindow.MarkOriginalPaused(worker.Name);
                await overlapWindow.WaitUntilReleaseAsync(CancellationToken.None);
                sideEffects = await ApplyVarCon08ProjectionFromSqlAsync(
                    scope,
                    tenantContext,
                    mongoDatabase,
                    seeded,
                    projectionEvent,
                    progressEntries);
            }

            await transaction.CommitAsync();
            stopwatch.Stop();
            progressEntries.Enqueue($"{worker.Name}:var-con-08-variant-projection-completed");
            return new VarCon08ProjectionWorkerResult(
                new VariantProjectionWorkerResult(worker.Name, VariantProjectionWorkerOutcome.Processed, stopwatch.Elapsed, null),
                sideEffects);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            progressEntries.Enqueue($"{worker.Name}:var-con-08-threw:{DescribeException(ex)}");
            return new VarCon08ProjectionWorkerResult(
                new VariantProjectionWorkerResult(worker.Name, VariantProjectionWorkerOutcome.Failed, stopwatch.Elapsed, ex),
                new VarCon06ProjectionSideEffects(0, 0, 0));
        }
    }

    private static async Task<VarCon06ProjectionSideEffects> ApplyVarCon08ProjectionFromSqlAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded,
        ProductVariantUpdatedEvent projectionEvent,
        ConcurrentQueue<string> progressEntries)
    {
        var sideEffects = await ApplyVarCon06StorefrontVariantProjectionEventAsync(
            mongoDatabase,
            projectionEvent);

        await RebuildVarCon02VisibilityFromSqlAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            progressEntries);

        progressEntries.Enqueue($"projection:var-con-08-applied-from-sql-truth:{projectionEvent.EventId}");
        return sideEffects;
    }

    private static async Task AssertVarCon08ActiveOverlapVisibilityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded,
        VarCon08VisibilitySnapshot baselineVisibility,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);
        Assert.Equal(0, await CountVarCon08ProcessedEventsAsync(verifyContext, VarCon08ProjectionEventId));

        var currentVisibility = await CaptureVarCon08VisibilityAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            VarCon08ProjectionEventId);

        Assert.Equal(baselineVisibility, currentVisibility);
        Assert.Equal(VarCon02BaselineSku, currentVisibility.VariantVisibility.StorefrontSku);
        Assert.Equal(VarCon02BaselineBarcode, currentVisibility.VariantVisibility.StorefrontBarcode);
        Assert.Equal(VarCon02BaselineOnlinePrice, currentVisibility.VariantVisibility.StorefrontEffectivePrice);
        Assert.Equal("Color=Red|Size=L", currentVisibility.VariantVisibility.StorefrontAttributeFingerprint);
        Assert.Equal(seeded.VariantId, currentVisibility.VariantVisibility.CommerceHubVariantId);

        progressEntries.Enqueue("verification:var-con-08-active-overlap-baseline-only-visible");
    }

    private static async Task AssertVarCon08ConvergenceAsync(
        TestDatabaseScope scope,
        string connectionString,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded,
        ProductVariantUpdatedEvent projectionEvent,
        ProductVariantUpdatedEvent staleProjectionEvent,
        VarCon06ExpectedState expected,
        VarCon08VisibilitySnapshot baselineVisibility,
        IReadOnlyCollection<VarCon08ProjectionWorkerResult> results,
        ConcurrentQueue<string> progressEntries)
    {
        Assert.Equal(2, results.Count);
        var processed = Assert.Single(results.Where(result => result.WorkerResult.Outcome == VariantProjectionWorkerOutcome.Processed));
        var duplicate = Assert.Single(results.Where(result => result.WorkerResult.Outcome == VariantProjectionWorkerOutcome.DuplicateSkipped));
        Assert.True(processed.SideEffects.CacheKeyDeleteCount > 0);
        Assert.Equal(0, duplicate.SideEffects.CacheKeyDeleteCount);
        Assert.Equal(0, duplicate.SideEffects.SearchIndexCount);
        Assert.Equal(0, duplicate.SideEffects.SearchDeleteCount);

        await AssertVarCon08VisibilityConvergedAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            projectionEvent,
            expected,
            progressEntries);

        await using (var verifyContext = scope.CreateDbContext(tenantContext))
        {
            Assert.Equal(1, await CountVarCon08ProcessedEventsAsync(verifyContext, projectionEvent.EventId));
            Assert.Equal(0, await CountVarCon08ProcessedEventsAsync(verifyContext, staleProjectionEvent.EventId));
            Assert.Equal(1, await verifyContext.ProductVariants
                .AsNoTracking()
                .IgnoreQueryFilters()
                .CountAsync(variant => variant.TenantId == seeded.TenantId && variant.ProductId == seeded.ProductId && variant.Id == seeded.VariantId));
            Assert.Equal(1, await verifyContext.ProductMedia
                .AsNoTracking()
                .IgnoreQueryFilters()
                .CountAsync(media => media.TenantId == seeded.TenantId && media.ProductId == seeded.ProductId && media.Id == MediaId));
        }

        var beforeDuplicateReplay = await CaptureVarCon08VisibilityAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            projectionEvent.EventId);
        var duplicateReplay = await ExecuteVarCon08ProjectionWorkerAsync(
            connectionString,
            tenantContext,
            scope,
            mongoDatabase,
            seeded,
            projectionEvent,
            new VariantProjectionWorker("var-con-08-post-commit-duplicate-delivery"),
            new VariantProjectionDeliveryOverlapWindow(TimeSpan.FromSeconds(30), progressEntries),
            pauseBeforeProjectionCompletion: false,
            progressEntries);
        var afterDuplicateReplay = await CaptureVarCon08VisibilityAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            projectionEvent.EventId);

        Assert.Null(duplicateReplay.WorkerResult.Exception);
        Assert.Equal(VariantProjectionWorkerOutcome.DuplicateSkipped, duplicateReplay.WorkerResult.Outcome);
        Assert.Equal(0, duplicateReplay.SideEffects.CacheKeyDeleteCount);
        Assert.Equal(beforeDuplicateReplay, afterDuplicateReplay);

        var staleReplaySideEffects = await ApplyVarCon06StorefrontVariantProjectionEventAsync(
            mongoDatabase,
            staleProjectionEvent);
        Assert.Equal(0, staleReplaySideEffects.CacheKeyDeleteCount);
        Assert.Equal(0, staleReplaySideEffects.SearchIndexCount);
        await AssertVarCon08VisibilityConvergedAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            projectionEvent,
            expected,
            progressEntries);

        await RebuildVarCon02VisibilityFromSqlAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            progressEntries);
        await RebuildVarCon02VisibilityFromSqlAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            progressEntries);

        await AssertVarCon08VisibilityConvergedAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            projectionEvent,
            expected,
            progressEntries);

        var rebuiltVisibility = await CaptureVarCon08VisibilityAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            VarCon08RebuildEventId);
        Assert.NotEqual(baselineVisibility, rebuiltVisibility);
        Assert.Equal(expected.Sku, rebuiltVisibility.VariantVisibility.StorefrontSku);
        Assert.Equal(expected.Barcode, rebuiltVisibility.VariantVisibility.StorefrontBarcode);

        progressEntries.Enqueue("verification:var-con-08-duplicate-delivery-converged-from-sql-truth");
    }

    private static async Task AssertVarCon08VisibilityConvergedAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded,
        ProductVariantUpdatedEvent projectionEvent,
        VarCon06ExpectedState expected,
        ConcurrentQueue<string> progressEntries)
    {
        await AssertVarCon03MutatedVisibilityAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            projectionEvent,
            progressEntries);

        var visibility = await CaptureVarCon08VisibilityAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            projectionEvent.EventId);

        Assert.Equal($"{seeded.TenantId}_{seeded.ProductId}", visibility.SearchDocumentId);
        Assert.Equal(projectionEvent.EventId, visibility.SearchLastEventId);
        Assert.True(visibility.SearchHasVariants);
        Assert.True(visibility.SearchInStock);
        Assert.Equal((int)VarCon02StockQuantity, visibility.SearchStockQuantity);
        Assert.Equal(MediaId, visibility.StorefrontMediaId);
        Assert.Equal("https://example.invalid/var-con-02.png", visibility.CommerceHubImage);

        progressEntries.Enqueue("verification:var-con-08-search-cache-media-visibility-safe");
    }

    private static async Task<VarCon08VisibilitySnapshot> CaptureVarCon08VisibilityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded,
        Guid searchEventId)
    {
        var variantVisibility = await CaptureVarCon03VisibilityAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded);
        var storefront = await LoadVarCon02StorefrontDocumentAsync(mongoDatabase, seeded);
        var storefrontVariant = Assert.Single(storefront.Variants.Where(variant => variant.VariantId == seeded.VariantId));
        var searchDocument = ProductSearchDocumentMapper.MapFromProductDocument(
            storefront,
            searchEventId);
        var commerceHub = await LoadVarCon02CommerceHubDocumentAsync(mongoDatabase, seeded);

        return new VarCon08VisibilitySnapshot(
            variantVisibility,
            searchDocument.GetDocumentId(),
            searchDocument.LastEventId,
            searchDocument.HasVariants,
            searchDocument.IsInStock,
            searchDocument.StockQuantity,
            storefrontVariant.MediaId,
            storefrontVariant.ImageUrl,
            commerceHub.Image);
    }

    private static async Task SeedVarCon09MediaRowsAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        VarCon02Scenario seeded)
    {
        await using var context = scope.CreateDbContext(tenantContext);
        if (!await context.ProductMedia.AnyAsync(media => media.Id == VarCon09V10MediaId))
        {
            context.ProductMedia.Add(new ProductMedia
            {
                Id = VarCon09V10MediaId,
                TenantId = seeded.TenantId,
                ProductId = seeded.ProductId,
                MediaType = "image",
                Url = VarCon09V10MediaUrl,
                Provider = null,
                Position = 1,
                CreatedAtUtc = SeededUtc.AddMinutes(10)
            });
        }

        if (!await context.ProductMedia.AnyAsync(media => media.Id == VarCon09V11MediaId))
        {
            context.ProductMedia.Add(new ProductMedia
            {
                Id = VarCon09V11MediaId,
                TenantId = seeded.TenantId,
                ProductId = seeded.ProductId,
                MediaType = "image",
                Url = VarCon09V11MediaUrl,
                Provider = null,
                Position = 1,
                CreatedAtUtc = SeededUtc.AddMinutes(11)
            });
        }

        await context.SaveChangesAsync();
    }

    private static async Task DeleteVarCon09NonWinnerMediaRowsAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext)
    {
        await using var context = scope.CreateDbContext(tenantContext);
        var staleRows = await context.ProductMedia
            .Where(media => media.Id == VarCon09V10MediaId || media.Id == MediaId)
            .ToListAsync();
        context.ProductMedia.RemoveRange(staleRows);
        await context.SaveChangesAsync();
    }

    private static async Task<int> PrepareVarCon09BSqlMediaAuthorityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        VarCon02Scenario seeded)
    {
        await using (var context = scope.CreateDbContext(tenantContext))
        {
            var product = await context.Products
                .IgnoreQueryFilters()
                .SingleAsync(item => item.Id == seeded.ProductId);
            var variant = await context.ProductVariants
                .IgnoreQueryFilters()
                .SingleAsync(item => item.Id == seeded.VariantId);
            var nextVersion = Math.Max(product.ProjectionMutationVersion, variant.ProjectionMutationVersion) + 1;

            product.AdvanceProjectionMutationVersion(nextVersion);
            product.UpdatedAt = SeededUtc.AddMinutes(20);
            variant.AdvanceProjectionMutationVersion(nextVersion);
            variant.MediaId = VarCon09V11MediaId;
            variant.ImageUrl = null;
            variant.UpdatedAt = SeededUtc.AddMinutes(20);
            await context.SaveChangesAsync();
        }

        await using (var context = scope.CreateDbContext(tenantContext))
        {
            var nonAuthoritativeRows = await context.ProductMedia
                .Where(media => media.Id == VarCon09V10MediaId || media.Id == MediaId)
                .ToListAsync();
            context.ProductMedia.RemoveRange(nonAuthoritativeRows);
            await context.SaveChangesAsync();
        }

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var currentVersion = await verifyContext.Products
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.Id == seeded.ProductId)
            .Select(item => item.ProjectionMutationVersion)
            .SingleAsync();

        return ProductEventVersionHelper.ToProductEventVersion(currentVersion);
    }

    private static UpdateVariantRequest BuildVarCon09MediaUpdateRequest(Guid mediaId)
    {
        return new UpdateVariantRequest
        {
            SKU = VarCon02BaselineSku,
            Barcode = VarCon02BaselineBarcode,
            Price = VarCon02BaselinePrice,
            OnlinePrice = VarCon02BaselineOnlinePrice,
            CostPrice = VarCon02BaselineCostPrice,
            IsActive = true,
            MediaId = mediaId,
            SortOrder = 20,
            Attributes =
            [
                new CreateVariantAttributeRequest
                {
                    Name = "Color",
                    Value = "Red",
                    SortOrder = 0
                },
                new CreateVariantAttributeRequest
                {
                    Name = "Size",
                    Value = "L",
                    SortOrder = 1
                }
            ],
            Stocks = null
        };
    }

    private static async Task<ProductVariantUpdatedEvent> BuildVarCon09VariantMediaProjectionEventFromSqlAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        VarCon02Scenario seeded,
        Guid eventId,
        string correlationId)
    {
        await using var context = scope.CreateDbContext(tenantContext);
        var variant = await context.ProductVariants
            .AsNoTracking()
            .Include(item => item.Attributes)
            .SingleAsync(item => item.Id == seeded.VariantId);

        return new ProductVariantUpdatedEvent
        {
            EventId = eventId,
            OccurredAtUtc = variant.UpdatedAt ?? DateTime.UtcNow,
            TenantId = seeded.TenantId,
            CorrelationId = correlationId,
            IdempotencyKey = $"{correlationId}:{seeded.VariantId}",
            EventVersion = ProductEventVersionHelper.ToProductEventVersion(variant.ProjectionMutationVersion),
            ProductId = seeded.ProductId,
            VariantId = seeded.VariantId,
            ChangedFields = ["MediaId", "ImageUrl"],
            SKU = variant.SKU,
            Barcode = variant.Barcode,
            Price = variant.Price,
            OnlinePrice = variant.OnlinePrice,
            CostPrice = variant.CostPrice,
            IsActive = variant.IsActive,
            MediaId = variant.MediaId,
            ImageUrl = variant.ImageUrl,
            SortOrder = variant.SortOrder,
            Attributes = variant.Attributes
                .OrderBy(attribute => attribute.SortOrder)
                .Select(attribute => new VariantAttributeData
                {
                    Name = attribute.Name,
                    Value = attribute.Value,
                    SortOrder = attribute.SortOrder
                })
                .ToList()
        };
    }

    private static ProductMediaChangedEvent BuildVarCon09ProductMediaChangedEvent(
        VarCon02Scenario seeded,
        Guid eventId,
        string correlationId,
        DateTime occurredAtUtc,
        int eventVersion,
        IReadOnlyList<(Guid Id, string Url, int Position)> media)
    {
        return new ProductMediaChangedEvent
        {
            EventId = eventId,
            OccurredAtUtc = occurredAtUtc,
            TenantId = seeded.TenantId,
            ProductId = seeded.ProductId,
            CorrelationId = correlationId,
            IdempotencyKey = $"{correlationId}:{seeded.ProductId}",
            EventVersion = eventVersion,
            Media = media
                .OrderBy(item => item.Position)
                .Select(item => new ProductMediaItemData
                {
                    Id = item.Id,
                    MediaType = "image",
                    Url = item.Url,
                    Position = item.Position
                })
                .ToList()
        };
    }

    private static async Task<VarCon08ProjectionWorkerResult> ExecuteVarCon09MediaProjectionWorkerAsync(
        string connectionString,
        TestTenantContext tenantContext,
        TestDatabaseScope scope,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded,
        ProductVariantUpdatedEvent projectionEvent,
        VariantProjectionWorker worker,
        VariantProjectionDeliveryOverlapWindow overlapWindow,
        bool pauseBeforeProjectionCompletion,
        ConcurrentQueue<string> progressEntries)
    {
        await using var context = CreateDbContext(connectionString, tenantContext);
        await ConfigureSessionTimeoutsAsync(context);
        var stopwatch = Stopwatch.StartNew();
        var eventType = typeof(ProductVariantUpdatedEvent).FullName ?? nameof(ProductVariantUpdatedEvent);

        try
        {
            var alreadyCompleted = await context.ProcessedEvents
                .IgnoreQueryFilters()
                .AnyAsync(item =>
                    item.EventId == projectionEvent.EventId &&
                    item.ConsumerName == VarCon09ConsumerName &&
                    item.EventType == eventType);

            if (alreadyCompleted)
            {
                stopwatch.Stop();
                progressEntries.Enqueue($"{worker.Name}:var-con-09-duplicate-media-projection-already-completed");
                return new VarCon08ProjectionWorkerResult(
                    new VariantProjectionWorkerResult(worker.Name, VariantProjectionWorkerOutcome.DuplicateSkipped, stopwatch.Elapsed, null),
                    new VarCon06ProjectionSideEffects(0, 0, 0));
            }

            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            context.ProcessedEvents.Add(ProcessedEvent.Create(
                projectionEvent.EventId,
                VarCon09ConsumerName,
                eventType,
                projectionEvent.VariantId,
                projectionEvent.TenantId,
                projectionEvent.CorrelationId ?? projectionEvent.EventId.ToString()));

            if (!pauseBeforeProjectionCompletion)
            {
                overlapWindow.MarkDuplicateClaimAttempt(worker.Name);
            }

            try
            {
                await context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                await transaction.RollbackAsync();
                stopwatch.Stop();
                progressEntries.Enqueue($"{worker.Name}:var-con-09-duplicate-media-collapsed-by-processed-event");
                return new VarCon08ProjectionWorkerResult(
                    new VariantProjectionWorkerResult(worker.Name, VariantProjectionWorkerOutcome.DuplicateSkipped, stopwatch.Elapsed, null),
                    new VarCon06ProjectionSideEffects(0, 0, 0));
            }

            var sideEffects = new VarCon06ProjectionSideEffects(0, 0, 0);
            if (pauseBeforeProjectionCompletion)
            {
                overlapWindow.MarkOriginalPaused(worker.Name);
                await overlapWindow.WaitUntilReleaseAsync(CancellationToken.None);
                sideEffects = await ApplyVarCon09MediaProjectionFromSqlAsync(
                    scope,
                    tenantContext,
                    mongoDatabase,
                    seeded,
                    projectionEvent,
                    progressEntries);
            }

            await transaction.CommitAsync();
            stopwatch.Stop();
            progressEntries.Enqueue($"{worker.Name}:var-con-09-media-projection-completed");
            return new VarCon08ProjectionWorkerResult(
                new VariantProjectionWorkerResult(worker.Name, VariantProjectionWorkerOutcome.Processed, stopwatch.Elapsed, null),
                sideEffects);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            progressEntries.Enqueue($"{worker.Name}:var-con-09-threw:{DescribeException(ex)}");
            return new VarCon08ProjectionWorkerResult(
                new VariantProjectionWorkerResult(worker.Name, VariantProjectionWorkerOutcome.Failed, stopwatch.Elapsed, ex),
                new VarCon06ProjectionSideEffects(0, 0, 0));
        }
    }

    private static async Task<VarCon06ProjectionSideEffects> ApplyVarCon09MediaProjectionFromSqlAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded,
        ProductVariantUpdatedEvent projectionEvent,
        ConcurrentQueue<string> progressEntries)
    {
        var sideEffects = await ApplyVarCon06StorefrontVariantProjectionEventAsync(
            mongoDatabase,
            projectionEvent);

        await RebuildVarCon09MediaVisibilityFromSqlAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            progressEntries);

        progressEntries.Enqueue($"projection:var-con-09-media-applied-from-sql-truth:{projectionEvent.EventId}");
        return sideEffects;
    }

    private static async Task<VarCon06ProjectionSideEffects> ApplyVarCon09ProductMediaChangedEventAsync(
        IMongoDatabase mongoDatabase,
        ProductMediaChangedEvent mediaEvent)
    {
        var indexedDocuments = new ConcurrentBag<ProductSearchDocument>();
        var searchIndexService = new Mock<IUnifiedSearchIndexService>(MockBehavior.Loose);
        searchIndexService
            .Setup(entry => entry.IndexProductAsync(
                It.IsAny<ProductSearchDocument>(),
                It.IsAny<CancellationToken>()))
            .Callback<ProductSearchDocument, CancellationToken>((document, _) => indexedDocuments.Add(document))
            .ReturnsAsync(true);

        var consumer = new ProductMediaChangedEventConsumer(
            mongoDatabase,
            searchIndexService.Object,
            Mock.Of<IStorefrontProductCacheInvalidator>(),
            NullLogger<ProductMediaChangedEventConsumer>.Instance);

        await consumer.Consume(BuildConsumeContext(mediaEvent).Object);

        return new VarCon06ProjectionSideEffects(0, indexedDocuments.Count, 0);
    }

    private static async Task<VarCon09BMediaConsumerResult> ApplyVarCon09ProductMediaChangedEventWithSqlAuthorityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        ProductMediaChangedEvent mediaEvent)
    {
        await using var dbContext = scope.CreateDbContext(tenantContext);
        var indexedDocuments = new ConcurrentBag<ProductSearchDocument>();
        var searchIndexService = new Mock<IUnifiedSearchIndexService>(MockBehavior.Loose);
        searchIndexService
            .Setup(entry => entry.IndexProductAsync(
                It.IsAny<ProductSearchDocument>(),
                It.IsAny<CancellationToken>()))
            .Callback<ProductSearchDocument, CancellationToken>((document, _) => indexedDocuments.Add(document))
            .ReturnsAsync(true);

        var cacheInvalidationCount = 0;
        var cacheInvalidator = new Mock<IStorefrontProductCacheInvalidator>(MockBehavior.Strict);
        cacheInvalidator
            .Setup(entry => entry.InvalidateProductAsync(
                mediaEvent.TenantId!.Value,
                mediaEvent.ProductId,
                It.IsAny<string?>(),
                true,
                It.IsAny<CancellationToken>()))
            .Callback(() => cacheInvalidationCount++)
            .Returns(Task.CompletedTask);

        var consumer = new ProductMediaChangedEventConsumer(
            mongoDatabase,
            searchIndexService.Object,
            cacheInvalidator.Object,
            NullLogger<ProductMediaChangedEventConsumer>.Instance,
            dbContext);

        await consumer.Consume(BuildConsumeContext(mediaEvent).Object);

        return new VarCon09BMediaConsumerResult(
            indexedDocuments.Count,
            indexedDocuments.ToList(),
            cacheInvalidationCount);
    }

    private static async Task RebuildVarCon09MediaVisibilityFromSqlAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded,
        ConcurrentQueue<string> progressEntries)
    {
        var productCollection = mongoDatabase.GetCollection<ProductDocument>("products");
        var existingProductDocument = await productCollection
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();

        await RebuildVarCon02VisibilityFromSqlAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            progressEntries);

        await using var context = scope.CreateDbContext(tenantContext);
        var mediaRows = await context.ProductMedia
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(media => media.TenantId == seeded.TenantId && media.ProductId == seeded.ProductId)
            .OrderBy(media => media.Position)
            .ThenBy(media => media.CreatedAtUtc)
            .ToListAsync();
        var variant = await context.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleAsync(item => item.Id == seeded.VariantId);
        var selectedMediaUrl = mediaRows
            .FirstOrDefault(media => media.Id == variant.MediaId)
            ?.Url;
        var productDocument = await LoadVarCon02StorefrontDocumentAsync(mongoDatabase, seeded);

        productDocument.Media = mediaRows
            .Select(media => new ProductMediaDocument
            {
                Id = media.Id,
                Type = media.MediaType,
                Url = media.Url,
                Provider = media.Provider,
                Position = media.Position
            })
            .ToList();
        productDocument.LastMediaEventId = existingProductDocument?.LastMediaEventId;
        productDocument.LastMediaEventOccurredAtUtc = existingProductDocument?.LastMediaEventOccurredAtUtc;
        productDocument.LastMediaEventVersion = existingProductDocument?.LastMediaEventVersion ?? 0;
        foreach (var variantDocument in productDocument.Variants.Where(item => item.VariantId == seeded.VariantId))
        {
            variantDocument.MediaId = variant.MediaId;
            variantDocument.ImageUrl = variant.ImageUrl;
        }

        await productCollection.ReplaceOneAsync(
            document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId,
            productDocument,
            new ReplaceOptions { IsUpsert = true });

        await mongoDatabase.GetCollection<PublicAffiliateProductDocument>("public_affiliate_products").UpdateOneAsync(
            document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId,
            Builders<PublicAffiliateProductDocument>.Update.Set(document => document.ThumbnailUrl, selectedMediaUrl),
            new UpdateOptions { IsUpsert = false });

        await mongoDatabase.GetCollection<CommerceHubProductDocument>("commercehub_products").UpdateOneAsync(
            document => document.TenantId == seeded.TenantId && document.Id == VarCon02CommerceHubDocumentId,
            Builders<CommerceHubProductDocument>.Update.Set(document => document.Image, selectedMediaUrl),
            new UpdateOptions { IsUpsert = false });

        progressEntries.Enqueue("verification:var-con-09-media-visibility-rebuilt-from-sql-truth");
    }

    private static async Task AssertVarCon09ActiveOverlapVisibilityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded,
        VarCon09MediaVisibilitySnapshot baselineVisibility,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);
        Assert.Equal(0, await CountVarCon09ProcessedEventsAsync(verifyContext, VarCon09V11ProjectionEventId));

        var currentVisibility = await CaptureVarCon09MediaVisibilityAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            VarCon09V11ProjectionEventId);

        Assert.Equal(VarCon09V11MediaId, currentVisibility.SqlVariantMediaId);
        Assert.Equal(1, currentVisibility.SqlV11MediaCount);
        Assert.Equal(0, currentVisibility.SqlV10MediaCount);
        Assert.Equal(0, currentVisibility.SqlOrphanVariantMediaLinks);
        Assert.Equal(baselineVisibility.StorefrontVariantMediaId, currentVisibility.StorefrontVariantMediaId);
        Assert.Equal(baselineVisibility.StorefrontMediaFingerprint, currentVisibility.StorefrontMediaFingerprint);
        Assert.Equal(baselineVisibility.AffiliateThumbnailUrl, currentVisibility.AffiliateThumbnailUrl);
        Assert.Equal(baselineVisibility.CommerceHubImage, currentVisibility.CommerceHubImage);
        Assert.Equal(baselineVisibility.SearchImageUrl, currentVisibility.SearchImageUrl);
        Assert.Equal(MediaId, currentVisibility.StorefrontVariantMediaId);
        Assert.DoesNotContain(VarCon09V10MediaUrl, currentVisibility.StorefrontMediaFingerprint, StringComparison.Ordinal);
        Assert.DoesNotContain(VarCon09V11MediaUrl, currentVisibility.StorefrontMediaFingerprint, StringComparison.Ordinal);

        progressEntries.Enqueue("verification:var-con-09-active-overlap-baseline-media-only-visible");
    }

    private static async Task AssertVarCon09MediaConvergenceAsync(
        TestDatabaseScope scope,
        string connectionString,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded,
        ProductVariantUpdatedEvent staleV10ProjectionEvent,
        ProductVariantUpdatedEvent v11ProjectionEvent,
        ProductMediaChangedEvent staleV10MediaEvent,
        ProductMediaChangedEvent v11MediaEvent,
        VarCon09MediaVisibilitySnapshot baselineVisibility,
        IReadOnlyCollection<VarCon08ProjectionWorkerResult> results,
        ConcurrentQueue<string> progressEntries)
    {
        Assert.Equal(2, results.Count);
        var processed = Assert.Single(results.Where(result => result.WorkerResult.Outcome == VariantProjectionWorkerOutcome.Processed));
        var duplicate = Assert.Single(results.Where(result => result.WorkerResult.Outcome == VariantProjectionWorkerOutcome.DuplicateSkipped));
        Assert.True(processed.SideEffects.CacheKeyDeleteCount > 0);
        Assert.Equal(0, duplicate.SideEffects.CacheKeyDeleteCount);
        Assert.Equal(0, duplicate.SideEffects.SearchIndexCount);
        Assert.Equal(0, duplicate.SideEffects.SearchDeleteCount);

        await AssertVarCon09MediaVisibilityConvergedAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            v11ProjectionEvent,
            v11MediaEvent,
            progressEntries);

        await using (var verifyContext = scope.CreateDbContext(tenantContext))
        {
            Assert.Equal(1, await CountVarCon09ProcessedEventsAsync(verifyContext, v11ProjectionEvent.EventId));
            Assert.Equal(0, await CountVarCon09ProcessedEventsAsync(verifyContext, staleV10ProjectionEvent.EventId));
        }

        var beforeDuplicateReplay = await CaptureVarCon09MediaVisibilityAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            v11ProjectionEvent.EventId);
        var duplicateReplay = await ExecuteVarCon09MediaProjectionWorkerAsync(
            connectionString,
            tenantContext,
            scope,
            mongoDatabase,
            seeded,
            v11ProjectionEvent,
            new VariantProjectionWorker("var-con-09-post-commit-duplicate-v11-delivery"),
            new VariantProjectionDeliveryOverlapWindow(TimeSpan.FromSeconds(30), progressEntries),
            pauseBeforeProjectionCompletion: false,
            progressEntries);
        var afterDuplicateReplay = await CaptureVarCon09MediaVisibilityAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            v11ProjectionEvent.EventId);

        Assert.Null(duplicateReplay.WorkerResult.Exception);
        Assert.Equal(VariantProjectionWorkerOutcome.DuplicateSkipped, duplicateReplay.WorkerResult.Outcome);
        Assert.Equal(beforeDuplicateReplay, afterDuplicateReplay);

        var staleVariantSideEffects = await ApplyVarCon06StorefrontVariantProjectionEventAsync(
            mongoDatabase,
            staleV10ProjectionEvent);
        var staleMediaSideEffects = await ApplyVarCon09ProductMediaChangedEventAsync(
            mongoDatabase,
            staleV10MediaEvent);

        Assert.Equal(0, staleVariantSideEffects.CacheKeyDeleteCount);
        Assert.Equal(0, staleVariantSideEffects.SearchIndexCount);
        Assert.Equal(0, staleMediaSideEffects.SearchIndexCount);

        await AssertVarCon09MediaVisibilityConvergedAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            v11ProjectionEvent,
            v11MediaEvent,
            progressEntries);

        await RebuildVarCon09MediaVisibilityFromSqlAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            progressEntries);
        await RebuildVarCon09MediaVisibilityFromSqlAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            progressEntries);

        await AssertVarCon09MediaVisibilityConvergedAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            v11ProjectionEvent,
            v11MediaEvent,
            progressEntries);

        var rebuiltVisibility = await CaptureVarCon09MediaVisibilityAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            VarCon09RebuildEventId);
        Assert.NotEqual(baselineVisibility, rebuiltVisibility);
        Assert.Equal(VarCon09V11MediaId, rebuiltVisibility.StorefrontVariantMediaId);
        Assert.Equal(VarCon09V11MediaUrl, rebuiltVisibility.SearchImageUrl);

        progressEntries.Enqueue("verification:var-con-09-media-sync-corruption-converged-from-sql-truth");
    }

    private static async Task AssertVarCon09SqlMediaTruthAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        VarCon02Scenario seeded,
        Guid expectedMediaId,
        string expectedMediaUrl)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var variant = await verifyContext.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleAsync(item => item.Id == seeded.VariantId);
        var winnerMedia = await verifyContext.ProductMedia
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleAsync(media => media.Id == expectedMediaId);
        var v10MediaCount = await verifyContext.ProductMedia
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(media => media.Id == VarCon09V10MediaId);

        Assert.Equal(expectedMediaId, variant.MediaId);
        Assert.Null(variant.ImageUrl);
        Assert.Equal(expectedMediaUrl, winnerMedia.Url);
        Assert.Equal(0, v10MediaCount);
        Assert.Equal(0, await CountOrphanVariantMediaLinksAsync(verifyContext));
    }

    private static async Task AssertVarCon09MediaVisibilityConvergedAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded,
        ProductVariantUpdatedEvent projectionEvent,
        ProductMediaChangedEvent mediaEvent,
        ConcurrentQueue<string> progressEntries)
    {
        await AssertVarCon09SqlMediaTruthAsync(
            scope,
            tenantContext,
            seeded,
            VarCon09V11MediaId,
            VarCon09V11MediaUrl);

        var visibility = await CaptureVarCon09MediaVisibilityAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            projectionEvent.EventId);

        Assert.Equal(1, visibility.SqlVariantCount);
        Assert.Equal(1, visibility.StorefrontVariantCount);
        Assert.Equal(1, visibility.SqlV11MediaCount);
        Assert.Equal(0, visibility.SqlV10MediaCount);
        Assert.Equal(0, visibility.SqlOrphanVariantMediaLinks);
        Assert.Equal(VarCon09V11MediaId, visibility.SqlVariantMediaId);
        Assert.Equal(VarCon09V11MediaId, visibility.StorefrontVariantMediaId);
        Assert.Null(visibility.StorefrontVariantImageUrl);
        Assert.Contains(VarCon09V11MediaUrl, visibility.StorefrontMediaFingerprint, StringComparison.Ordinal);
        Assert.DoesNotContain(VarCon09V10MediaUrl, visibility.StorefrontMediaFingerprint, StringComparison.Ordinal);
        Assert.Equal(mediaEvent.EventId, visibility.StorefrontLastMediaEventId);
        Assert.Equal(mediaEvent.EventVersion, visibility.StorefrontLastMediaEventVersion);
        Assert.Equal(VarCon09V11MediaUrl, visibility.AffiliateThumbnailUrl);
        Assert.Equal(VarCon09V11MediaUrl, visibility.CommerceHubImage);
        Assert.Equal(VarCon09V11MediaUrl, visibility.SearchImageUrl);
        Assert.Equal(projectionEvent.EventId, visibility.SearchLastEventId);
        Assert.True(visibility.SearchHasVariants);
        Assert.True(visibility.SearchInStock);

        progressEntries.Enqueue("verification:var-con-09-v11-media-visible-no-stale-resurrection");
    }

    private static async Task<VarCon09MediaVisibilitySnapshot> CaptureVarCon09MediaVisibilityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded,
        Guid searchEventId)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var sqlVariantCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(variant => variant.TenantId == seeded.TenantId && variant.ProductId == seeded.ProductId && variant.Id == seeded.VariantId);
        var sqlV11MediaCount = await verifyContext.ProductMedia
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(media => media.TenantId == seeded.TenantId && media.ProductId == seeded.ProductId && media.Id == VarCon09V11MediaId);
        var sqlV10MediaCount = await verifyContext.ProductMedia
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(media => media.TenantId == seeded.TenantId && media.ProductId == seeded.ProductId && media.Id == VarCon09V10MediaId);
        var sqlVariantMediaId = await verifyContext.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(variant => variant.Id == seeded.VariantId)
            .Select(variant => variant.MediaId)
            .SingleAsync();
        var sqlOrphanLinks = await CountOrphanVariantMediaLinksAsync(verifyContext);

        var storefront = await LoadVarCon02StorefrontDocumentAsync(mongoDatabase, seeded);
        var storefrontVariant = Assert.Single(storefront.Variants.Where(variant => variant.VariantId == seeded.VariantId));
        var affiliate = await LoadVarCon02AffiliateDocumentAsync(mongoDatabase, seeded);
        var commerceHub = await LoadVarCon02CommerceHubDocumentAsync(mongoDatabase, seeded);
        var searchDocument = ProductSearchDocumentMapper.MapFromProductDocument(
            storefront,
            searchEventId);

        return new VarCon09MediaVisibilitySnapshot(
            SqlVariantCount: sqlVariantCount,
            SqlV11MediaCount: sqlV11MediaCount,
            SqlV10MediaCount: sqlV10MediaCount,
            SqlOrphanVariantMediaLinks: sqlOrphanLinks,
            SqlVariantMediaId: sqlVariantMediaId,
            StorefrontVariantCount: storefront.Variants.Count(variant => variant.VariantId == seeded.VariantId),
            StorefrontVariantMediaId: storefrontVariant.MediaId,
            StorefrontVariantImageUrl: storefrontVariant.ImageUrl,
            StorefrontMediaFingerprint: string.Join("|", storefront.Media
                .OrderBy(media => media.Position)
                .ThenBy(media => media.Id)
                .Select(media => $"{media.Id}:{media.Url}")),
            StorefrontLastMediaEventId: storefront.LastMediaEventId,
            StorefrontLastMediaEventVersion: storefront.LastMediaEventVersion,
            AffiliateThumbnailUrl: affiliate.ThumbnailUrl,
            CommerceHubImage: commerceHub.Image,
            SearchImageUrl: searchDocument.ImageUrl,
            SearchLastEventId: searchDocument.LastEventId,
            SearchHasVariants: searchDocument.HasVariants,
            SearchInStock: searchDocument.IsInStock);
    }

    private static async Task<int> CountVarCon09ProcessedEventsAsync(
        ApplicationDbContext context,
        Guid eventId)
    {
        return await CountVariantProjectionProcessedEventsAsync(
            context,
            eventId,
            VarCon09ConsumerName);
    }

    private static async Task<int> CountProductMediaProcessedEventsAsync(
        ApplicationDbContext context,
        Guid eventId)
    {
        var eventType = typeof(ProductMediaChangedEvent).FullName ?? nameof(ProductMediaChangedEvent);
        return await context.ProcessedEvents
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(item =>
                item.EventId == eventId &&
                item.ConsumerName == nameof(ProductMediaChangedEventConsumer) &&
                item.EventType == eventType);
    }

    private static async Task<int> CountOrphanVariantMediaLinksAsync(ApplicationDbContext context)
    {
        return await ExecuteScalarIntAsync(
            context,
            """
            SELECT COUNT(*)
            FROM "ProductVariants" variant
            LEFT JOIN "ProductMedia" media ON media."Id" = variant."MediaId"
            WHERE variant."MediaId" IS NOT NULL
              AND media."Id" IS NULL;
            """);
    }

    private static async Task AssertVarCon03MutatedVisibilityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded,
        ProductVariantUpdatedEvent projectionEvent,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var variant = await verifyContext.ProductVariants
            .AsNoTracking()
            .Include(item => item.Attributes)
            .Include(item => item.Stocks)
            .SingleAsync(item => item.Id == seeded.VariantId);
        var combinationOwnerCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .CountAsync(item =>
                item.TenantId == seeded.TenantId &&
                item.ProductId == seeded.ProductId &&
                item.AttributeCombinationKey == seeded.MutatedCombinationKey);
        var staleCombinationOwnerCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .CountAsync(item =>
                item.TenantId == seeded.TenantId &&
                item.ProductId == seeded.ProductId &&
                item.AttributeCombinationKey == seeded.BaselineCombinationKey);
        var skuOwnerCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .CountAsync(item => item.SKU == VarCon02MutatedSku);
        var staleSkuOwnerCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .CountAsync(item => item.SKU == VarCon02BaselineSku);
        var barcodeOwnerCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .CountAsync(item => item.Barcode == VarCon02MutatedBarcode);
        var staleBarcodeOwnerCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .CountAsync(item => item.Barcode == VarCon02BaselineBarcode);
        var stockRows = await verifyContext.ProductVariantStocks
            .AsNoTracking()
            .Where(stock => stock.VariantId == seeded.VariantId)
            .ToListAsync();
        var visibility = await CaptureVarCon03VisibilityAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded);

        Assert.Equal(VarCon02MutatedSku, variant.SKU);
        Assert.Equal(VarCon02MutatedBarcode, variant.Barcode);
        Assert.Equal(seeded.MutatedCombinationKey, variant.AttributeCombinationKey);
        Assert.Equal(VarCon02MutatedPrice, variant.Price);
        Assert.Equal(VarCon02MutatedOnlinePrice, variant.OnlinePrice);
        Assert.Equal(VarCon02MutatedCostPrice, variant.CostPrice);
        Assert.Equal(1, combinationOwnerCount);
        Assert.Equal(0, staleCombinationOwnerCount);
        Assert.Equal(1, skuOwnerCount);
        Assert.Equal(0, staleSkuOwnerCount);
        Assert.Equal(1, barcodeOwnerCount);
        Assert.Equal(0, staleBarcodeOwnerCount);
        Assert.Single(stockRows);
        Assert.Equal(VarCon02StockId, stockRows.Single().Id);
        Assert.Equal(VarCon02StockQuantity, stockRows.Single().Quantity);

        Assert.Equal(1, visibility.StorefrontVariantCount);
        Assert.Equal(VarCon02MutatedSku, visibility.StorefrontSku);
        Assert.Equal(VarCon02MutatedBarcode, visibility.StorefrontBarcode);
        Assert.Equal(VarCon02MutatedOnlinePrice, visibility.StorefrontEffectivePrice);
        Assert.Equal("Color=Red|Size=XL", visibility.StorefrontAttributeFingerprint);
        Assert.Equal(projectionEvent.EventVersion, visibility.StorefrontLastEventVersion);
        Assert.Equal(VarCon02StockQuantity, visibility.StorefrontTotalStock);
        Assert.Equal(VarCon02MutatedOnlinePrice, visibility.AffiliateMaxVariantPrice);
        Assert.Equal(VarCon02StockQuantity, visibility.AffiliateTotalAvailableStock);
        Assert.Equal(VarCon02MutatedOnlinePrice, visibility.CommerceHubPrice);
        Assert.Equal(VarCon02StockQuantity, visibility.CommerceHubAvailableStock);
        Assert.Equal(seeded.VariantId, visibility.CommerceHubVariantId);
        Assert.True(visibility.AffiliateVisible);
        Assert.True(visibility.CommerceHubPublished);

        progressEntries.Enqueue("verification:var-con-03-mutated-visibility-safe");
    }

    private static async Task<VarCon03VisibilitySnapshot> CaptureVarCon03VisibilityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var sqlVariantCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .CountAsync(item => item.TenantId == seeded.TenantId && item.ProductId == seeded.ProductId);
        var sqlStockCount = await verifyContext.ProductVariantStocks
            .AsNoTracking()
            .CountAsync(item => item.TenantId == seeded.TenantId && item.Variant.ProductId == seeded.ProductId);

        var storefront = await LoadVarCon02StorefrontDocumentAsync(mongoDatabase, seeded);
        var storefrontVariants = storefront.Variants
            .Where(item => item.VariantId == seeded.VariantId)
            .ToList();
        var storefrontVariant = Assert.Single(storefrontVariants);
        var affiliate = await LoadVarCon02AffiliateDocumentAsync(mongoDatabase, seeded);
        var commerceHub = await LoadVarCon02CommerceHubDocumentAsync(mongoDatabase, seeded);

        return new VarCon03VisibilitySnapshot(
            SqlVariantCount: sqlVariantCount,
            SqlStockCount: sqlStockCount,
            StorefrontVariantCount: storefrontVariants.Count,
            StorefrontSku: storefrontVariant.SKU,
            StorefrontBarcode: storefrontVariant.Barcode,
            StorefrontEffectivePrice: storefrontVariant.EffectivePrice,
            StorefrontTotalStock: storefrontVariant.TotalStock,
            StorefrontLastEventVersion: storefrontVariant.LastEventVersion,
            StorefrontAttributeFingerprint: string.Join(
                "|",
                storefrontVariant.Attributes
                    .OrderBy(attribute => attribute.SortOrder)
                    .Select(attribute => $"{attribute.Name}={attribute.Value}")),
            AffiliateVisible: affiliate.IsActive,
            AffiliateMaxVariantPrice: affiliate.MaxVariantPrice ?? 0m,
            AffiliateTotalAvailableStock: affiliate.TotalAvailableStock,
            CommerceHubVariantId: commerceHub.VariantId,
            CommerceHubPrice: commerceHub.Price,
            CommerceHubAvailableStock: commerceHub.AvailableStock,
            CommerceHubPublished: commerceHub.IsPublished);
    }

    private static async Task<int> CountVarCon03ProcessedEventsAsync(
        ApplicationDbContext context,
        Guid eventId)
    {
        return await CountVariantProjectionProcessedEventsAsync(
            context,
            eventId,
            VarCon03ConsumerName);
    }

    private static async Task<int> CountVarCon08ProcessedEventsAsync(
        ApplicationDbContext context,
        Guid eventId)
    {
        return await CountVariantProjectionProcessedEventsAsync(
            context,
            eventId,
            VarCon08ConsumerName);
    }

    private static async Task<int> CountVariantProjectionProcessedEventsAsync(
        ApplicationDbContext context,
        Guid eventId,
        string consumerName)
    {
        var eventType = typeof(ProductVariantUpdatedEvent).FullName ?? nameof(ProductVariantUpdatedEvent);
        return await context.ProcessedEvents
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(item =>
                item.EventId == eventId &&
                item.ConsumerName == consumerName &&
                item.EventType == eventType);
    }

    private static async Task RebuildVarCon02VisibilityFromSqlAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded,
        ConcurrentQueue<string> progressEntries)
    {
        var productCollection = mongoDatabase.GetCollection<ProductDocument>("products");
        var existingProductDocument = await productCollection
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();
        await using var context = scope.CreateDbContext(tenantContext);
        var product = await context.Products
            .AsNoTracking()
            .SingleAsync(item => item.Id == seeded.ProductId);
        var category = await context.Categories
            .AsNoTracking()
            .SingleAsync(item => item.Id == product.CategoryId);
        var variants = await context.ProductVariants
            .AsNoTracking()
            .Include(item => item.Attributes)
            .Include(item => item.Stocks)
            .Where(item => item.TenantId == seeded.TenantId && item.ProductId == seeded.ProductId)
            .OrderByDescending(item => item.IsDefault)
            .ThenBy(item => item.SortOrder)
            .ToListAsync();
        var targetVariant = variants.Single(item => item.Id == seeded.VariantId);
        var targetStock = targetVariant.Stocks.Single();
        var effectiveTargetPrice = targetVariant.OnlinePrice is > 0
            ? targetVariant.OnlinePrice.Value
            : targetVariant.Price;

        var storefrontProduct = new ProductDocument
        {
            TenantId = seeded.TenantId,
            ProductId = seeded.ProductId,
            Name = product.Name,
            SKU = product.SKU,
            Barcode = product.Barcode,
            Price = product.SalePrice,
            OnlinePrice = product.OnlinePrice,
            PurchasePrice = product.PurchasePrice,
            CategoryId = product.CategoryId,
            CategoryName = category.Name,
            Slug = "var-con-02-product",
            IsOnlineVisible = true,
            IsActive = true,
            TrackStock = product.TrackStock,
            AllowBackorder = product.AllowBackorder,
            StockQuantity = (int)variants.SelectMany(item => item.Stocks).Sum(stock => stock.Quantity),
            HasVariants = true,
            CreatedAtUtc = SeededUtc,
            SyncedAtUtc = DateTime.UtcNow,
            LastEventVersion = ProductEventVersionHelper.ToProductEventVersion(product.ProjectionMutationVersion),
            DeletedVariantTombstones = existingProductDocument?.DeletedVariantTombstones.ToList() ?? new List<ProductVariantDeletionTombstoneDocument>(),
            Variants = variants.Select(item => new ProductVariantDocument
            {
                VariantId = item.Id,
                SKU = item.SKU,
                Barcode = item.Barcode,
                Price = item.Price,
                OnlinePrice = item.OnlinePrice,
                CostPrice = item.CostPrice,
                MediaId = item.MediaId,
                ImageUrl = item.ImageUrl,
                IsDefault = item.IsDefault,
                IsActive = item.IsActive,
                TotalStock = item.Stocks.Sum(stock => stock.Quantity - stock.ReservedQuantity),
                SortOrder = item.SortOrder,
                SyncedAtUtc = DateTime.UtcNow,
                StockUpdatedAtUtc = item.StockUpdatedAtUtc,
                LastStockUpdatedAtUtc = item.StockUpdatedAtUtc,
                LastStockEventVersion = ProductEventVersionHelper.ToProductEventVersion(item.ProjectionMutationVersion),
                LastEventOccurredAtUtc = item.UpdatedAt ?? item.CreatedAt,
                LastEventVersion = ProductEventVersionHelper.ToProductEventVersion(item.ProjectionMutationVersion),
                Attributes = item.Attributes
                    .OrderBy(attribute => attribute.SortOrder)
                    .Select(attribute => new VariantAttributeDocument
                    {
                        Name = attribute.Name,
                        Value = attribute.Value,
                        SortOrder = attribute.SortOrder
                    })
                    .ToList()
            }).ToList()
        };

        await productCollection.ReplaceOneAsync(
            document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId,
            storefrontProduct,
            new ReplaceOptions { IsUpsert = true });

        var activeVariantPrices = storefrontProduct.Variants
            .Where(item => item.IsActive)
            .Select(item => item.EffectivePrice)
            .ToArray();
        var affiliateProduct = new PublicAffiliateProductDocument
        {
            ProductId = seeded.ProductId,
            ProductSlug = storefrontProduct.Slug ?? string.Empty,
            ProductName = product.Name,
            Description = product.Description ?? string.Empty,
            ThumbnailUrl = "https://example.invalid/var-con-02.png",
            Price = null,
            MinVariantPrice = activeVariantPrices.Min(),
            MaxVariantPrice = activeVariantPrices.Max(),
            Currency = "EGP",
            StoreName = "VAR-CON-02 Tenant",
            StoreSlug = "var-con-02",
            CategoryName = category.Name,
            HasVariants = true,
            TotalAvailableStock = storefrontProduct.StockQuantity,
            InStock = storefrontProduct.Variants.Any(item => item.TotalStock > 0),
            IsActive = true,
            IsAffiliateEnabled = true,
            AffiliateEnabledAtUtc = SeededUtc,
            TenantId = seeded.TenantId,
            StorefrontBaseUrl = "https://var-con-02.example.test"
        };

        await mongoDatabase.GetCollection<PublicAffiliateProductDocument>("public_affiliate_products").ReplaceOneAsync(
            document => document.ProductId == seeded.ProductId,
            affiliateProduct,
            new ReplaceOptions { IsUpsert = true });

        var commerceHubProduct = new CommerceHubProductDocument
        {
            Id = VarCon02CommerceHubDocumentId,
            TenantId = seeded.TenantId,
            StoreName = "VAR-CON-02 Tenant",
            ProductId = seeded.ProductId,
            VariantId = seeded.VariantId,
            ProductName = product.Name,
            Description = product.Description,
            Image = "https://example.invalid/var-con-02.png",
            Price = effectiveTargetPrice,
            CategoryId = product.CategoryId,
            CategoryName = category.Name,
            Currency = "EGP",
            InStock = targetStock.Quantity - targetStock.ReservedQuantity > 0,
            AvailableStock = targetStock.Quantity - targetStock.ReservedQuantity,
            LastStockEventVersion = ProductEventVersionHelper.ToProductEventVersion(targetStock.ProjectionMutationVersion),
            IsPublished = true,
            AttributesJson = JsonSerializer.Serialize(targetVariant.Attributes
                .OrderBy(attribute => attribute.SortOrder)
                .ToDictionary(attribute => attribute.Name, attribute => attribute.Value)),
            CreatedAtUtc = SeededUtc,
            UpdatedAtUtc = DateTime.UtcNow,
            LastPriceEventVersion = ProductEventVersionHelper.ToProductEventVersion(targetVariant.ProjectionMutationVersion),
            LastPriceEventTimestamp = targetVariant.UpdatedAt ?? targetVariant.CreatedAt,
            SearchSyncStatus = CommerceHubProductDocument.SearchSyncSynced,
            LastSyncedAt = DateTime.UtcNow
        };

        await mongoDatabase.GetCollection<CommerceHubProductDocument>("commercehub_products").ReplaceOneAsync(
            document => document.TenantId == seeded.TenantId && document.Id == VarCon02CommerceHubDocumentId,
            commerceHubProduct,
            new ReplaceOptions { IsUpsert = true });

        progressEntries.Enqueue("verification:var-con-02-visibility-rebuilt-from-sql-truth");
    }

    private static async Task SetVarCon06BaselineProjectionVersionAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded)
    {
        await using (var context = scope.CreateDbContext(tenantContext))
        {
            var variant = await context.ProductVariants
                .SingleAsync(item => item.Id == seeded.VariantId);
            variant.AdvanceProjectionMutationVersion(VarCon06BaselineProjectionVersion);
            await context.SaveChangesAsync();
        }

        await using (var verifyContext = scope.CreateDbContext(tenantContext))
        {
            var currentVersion = await verifyContext.ProductVariants
                .AsNoTracking()
                .Where(item => item.Id == seeded.VariantId)
                .Select(item => ProductEventVersionHelper.ToProductEventVersion(item.ProjectionMutationVersion))
                .SingleAsync();
            Assert.Equal(VarCon06BaselineProjectionVersion, currentVersion);
        }

        await RebuildVarCon02VisibilityFromSqlAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            new ConcurrentQueue<string>());
    }

    private static VarCon06ExpectedState BuildVarCon06ExpectedState(
        ProductVariantUpdatedEvent projectionEvent)
    {
        var attributes = projectionEvent.Attributes
            ?? throw new InvalidOperationException("VAR-CON-06 projection event must carry variant attributes.");
        var attributePairs = attributes
            .OrderBy(attribute => attribute.SortOrder)
            .Select(attribute => (attribute.Name, attribute.Value))
            .ToArray();
        var combinationKey = ProductVariantAttributeCombinationKey.Compute(attributePairs)
            ?? throw new InvalidOperationException("VAR-CON-06 expected combination key was not computed.");

        return new VarCon06ExpectedState(
            projectionEvent.SKU ?? string.Empty,
            projectionEvent.Barcode,
            projectionEvent.Price ?? 0m,
            projectionEvent.OnlinePrice ?? 0m,
            projectionEvent.CostPrice ?? 0m,
            projectionEvent.EventVersion,
            combinationKey,
            string.Join("|", attributePairs.Select(item => $"{item.Name}={item.Value}")));
    }

    private static async Task AssertVarCon06SqlTruthAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        VarCon02Scenario seeded,
        VarCon06ExpectedState expected)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var variant = await verifyContext.ProductVariants
            .AsNoTracking()
            .Include(item => item.Attributes)
            .Include(item => item.Stocks)
            .SingleAsync(item => item.Id == seeded.VariantId);
        var v11SkuOwnerCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .CountAsync(item => item.SKU == VarCon06V11Sku);
        var v10SkuOwnerCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .CountAsync(item => item.SKU == VarCon06V10Sku);
        var v11BarcodeOwnerCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .CountAsync(item => item.Barcode == VarCon06V11Barcode);
        var v10BarcodeOwnerCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .CountAsync(item => item.Barcode == VarCon06V10Barcode);
        var v11CombinationOwnerCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .CountAsync(item =>
                item.TenantId == seeded.TenantId &&
                item.ProductId == seeded.ProductId &&
                item.AttributeCombinationKey == expected.AttributeCombinationKey);
        var v10CombinationKey = ProductVariantAttributeCombinationKey.Compute(
        [
            ("Color", "Red"),
            ("Size", "XL")
        ]) ?? throw new InvalidOperationException("VAR-CON-06 V10 combination key was not computed.");
        var v10CombinationOwnerCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .CountAsync(item =>
                item.TenantId == seeded.TenantId &&
                item.ProductId == seeded.ProductId &&
                item.AttributeCombinationKey == v10CombinationKey);
        var idempotencyRecords = await verifyContext.ProductRequestIdempotencyRecords
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(record =>
                record.TenantId == seeded.TenantId &&
                record.ProductId == seeded.ProductId &&
                record.ProductVariantId == seeded.VariantId &&
                record.Operation == "UPDATE_VARIANT")
            .OrderBy(record => record.Key)
            .ToListAsync();
        var outboxMessages = await verifyContext.OutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.AggregateId == seeded.VariantId)
            .ToListAsync();

        Assert.Equal(expected.Sku, variant.SKU);
        Assert.Equal(expected.Barcode, variant.Barcode);
        Assert.Equal(expected.AttributeCombinationKey, variant.AttributeCombinationKey);
        Assert.Equal(expected.Price, variant.Price);
        Assert.Equal(expected.OnlinePrice, variant.OnlinePrice);
        Assert.Equal(expected.CostPrice, variant.CostPrice);
        Assert.Equal(expected.EventVersion, ProductEventVersionHelper.ToProductEventVersion(variant.ProjectionMutationVersion));
        Assert.Equal(expected.AttributeFingerprint, string.Join(
            "|",
            variant.Attributes
                .OrderBy(attribute => attribute.SortOrder)
                .Select(attribute => $"{attribute.Name}={attribute.Value}")));
        Assert.Equal(1, v11SkuOwnerCount);
        Assert.Equal(0, v10SkuOwnerCount);
        Assert.Equal(1, v11BarcodeOwnerCount);
        Assert.Equal(0, v10BarcodeOwnerCount);
        Assert.Equal(1, v11CombinationOwnerCount);
        Assert.Equal(0, v10CombinationOwnerCount);
        Assert.Single(variant.Stocks);
        Assert.Equal(VarCon02StockQuantity, variant.Stocks.Single().Quantity);
        Assert.Equal(2, idempotencyRecords.Count);
        Assert.Contains(idempotencyRecords, record => record.Key == BuildProductMutationIdempotencyKey(seeded.TenantId, VarCon06V10RequestId));
        Assert.Contains(idempotencyRecords, record => record.Key == BuildProductMutationIdempotencyKey(seeded.TenantId, VarCon06V11RequestId));
        Assert.Equal(2, outboxMessages.Count(message => message.EventType == typeof(ProductVariantUpdatedEvent).FullName));
        Assert.Equal(2, outboxMessages.Count(message => message.EventType == typeof(VariantPriceUpdatedEvent).FullName));
        Assert.Equal(0, await CountOrphanVariantAttributesAsync(verifyContext));
        Assert.Equal(0, await CountOrphanVariantStocksAsync(verifyContext));
    }

    private static async Task AssertVarCon06VisibilityConvergedAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded,
        VarCon06ExpectedState expected,
        ConcurrentQueue<string> progressEntries)
    {
        await AssertVarCon06SqlTruthAsync(scope, tenantContext, seeded, expected);

        var visibility = await CaptureVarCon03VisibilityAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded);
        var storefront = await LoadVarCon02StorefrontDocumentAsync(mongoDatabase, seeded);
        var productSearchDocument = ProductSearchDocumentMapper.MapFromProductDocument(
            storefront,
            VarCon06V11EventId);
        var commerceHub = await LoadVarCon02CommerceHubDocumentAsync(mongoDatabase, seeded);

        Assert.Equal(1, visibility.StorefrontVariantCount);
        Assert.Equal(expected.Sku, visibility.StorefrontSku);
        Assert.Equal(expected.Barcode, visibility.StorefrontBarcode);
        Assert.Equal(expected.OnlinePrice, visibility.StorefrontEffectivePrice);
        Assert.Equal(expected.EventVersion, visibility.StorefrontLastEventVersion);
        Assert.Equal(expected.AttributeFingerprint, visibility.StorefrontAttributeFingerprint);
        Assert.Equal(VarCon02StockQuantity, visibility.StorefrontTotalStock);
        Assert.Equal(expected.OnlinePrice, visibility.AffiliateMaxVariantPrice);
        Assert.Equal(VarCon02StockQuantity, visibility.AffiliateTotalAvailableStock);
        Assert.Equal(expected.OnlinePrice, visibility.CommerceHubPrice);
        Assert.Equal(VarCon02StockQuantity, visibility.CommerceHubAvailableStock);
        Assert.Equal(seeded.VariantId, visibility.CommerceHubVariantId);
        Assert.True(visibility.AffiliateVisible);
        Assert.True(visibility.CommerceHubPublished);
        Assert.DoesNotContain(VarCon06V10Sku, storefront.Variants.Select(variant => variant.SKU));
        Assert.DoesNotContain(VarCon06V10Barcode, storefront.Variants.Select(variant => variant.Barcode));
        Assert.Contains("XXL", commerceHub.AttributesJson ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("XL", (commerceHub.AttributesJson ?? string.Empty).Replace("XXL", string.Empty, StringComparison.Ordinal));
        Assert.True(productSearchDocument.HasVariants);
        Assert.True(productSearchDocument.IsInStock);
        Assert.Equal((int)VarCon02StockQuantity, productSearchDocument.StockQuantity);

        progressEntries.Enqueue("verification:var-con-06-v11-visible-across-derived-systems");
    }

    private static async Task AssertVarCon07SqlTruthAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        VarCon02Scenario seeded,
        VarCon06ExpectedState expected)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var variant = await verifyContext.ProductVariants
            .AsNoTracking()
            .Include(item => item.Attributes)
            .Include(item => item.Stocks)
            .SingleAsync(item => item.Id == seeded.VariantId);
        var v11SkuOwnerCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .CountAsync(item => item.SKU == VarCon06V11Sku);
        var v10SkuOwnerCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .CountAsync(item => item.SKU == VarCon06V10Sku);
        var v11BarcodeOwnerCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .CountAsync(item => item.Barcode == VarCon06V11Barcode);
        var v10BarcodeOwnerCount = await verifyContext.ProductVariants
            .AsNoTracking()
            .CountAsync(item => item.Barcode == VarCon06V10Barcode);
        var idempotencyRecords = await verifyContext.ProductRequestIdempotencyRecords
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(record =>
                record.TenantId == seeded.TenantId &&
                record.ProductId == seeded.ProductId &&
                record.ProductVariantId == seeded.VariantId &&
                record.Operation == "UPDATE_VARIANT")
            .OrderBy(record => record.Key)
            .ToListAsync();

        Assert.Equal(expected.Sku, variant.SKU);
        Assert.Equal(expected.Barcode, variant.Barcode);
        Assert.Equal(expected.AttributeCombinationKey, variant.AttributeCombinationKey);
        Assert.Equal(expected.Price, variant.Price);
        Assert.Equal(expected.OnlinePrice, variant.OnlinePrice);
        Assert.Equal(expected.CostPrice, variant.CostPrice);
        Assert.Equal(expected.EventVersion, ProductEventVersionHelper.ToProductEventVersion(variant.ProjectionMutationVersion));
        Assert.Equal(expected.AttributeFingerprint, string.Join(
            "|",
            variant.Attributes
                .OrderBy(attribute => attribute.SortOrder)
                .Select(attribute => $"{attribute.Name}={attribute.Value}")));
        Assert.Equal(1, v11SkuOwnerCount);
        Assert.Equal(0, v10SkuOwnerCount);
        Assert.Equal(1, v11BarcodeOwnerCount);
        Assert.Equal(0, v10BarcodeOwnerCount);
        Assert.Single(variant.Stocks);
        Assert.Equal(VarCon02StockQuantity, variant.Stocks.Single().Quantity);
        Assert.Equal(2, idempotencyRecords.Count);
        Assert.Contains(idempotencyRecords, record => record.Key == BuildProductMutationIdempotencyKey(seeded.TenantId, VarCon07V10RequestId));
        Assert.Contains(idempotencyRecords, record => record.Key == BuildProductMutationIdempotencyKey(seeded.TenantId, VarCon07V11RequestId));
        Assert.Equal(0, await CountOrphanVariantAttributesAsync(verifyContext));
        Assert.Equal(0, await CountOrphanVariantStocksAsync(verifyContext));
    }

    private static async Task AssertVarCon07VisibilityConvergedAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded,
        VarCon06ExpectedState expected,
        ConcurrentQueue<string> progressEntries)
    {
        await AssertVarCon07SqlTruthAsync(scope, tenantContext, seeded, expected);
        await AssertVarCon07DerivedVisibilityConvergedAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            expected,
            progressEntries);

        var repairEvent = new MarketplaceEvents.ProductUpdatedEvent
        {
            EventId = VarCon07MarketplaceRepairEventId,
            OccurredAtUtc = DateTime.UtcNow,
            CorrelationId = "var-con-07-marketplace-repair-assertion",
            EntityId = seeded.ProductId,
            AggregateRootId = seeded.ProductId,
            TenantId = seeded.TenantId,
            Version = expected.EventVersion,
            ChangedFields = ["Repair"],
            HasVariants = true
        };

        await AssertVarCon07MarketplaceReadModelConvergedAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            expected,
            repairEvent,
            progressEntries);
    }

    private static async Task AssertVarCon07MarketplaceReadModelConvergedAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded,
        VarCon06ExpectedState expected,
        MarketplaceEvents.ProductUpdatedEvent triggeringEvent,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var marketplaceDocuments = await mongoDatabase
            .GetCollection<MarketplaceProductReadModelDocument>(MarketplaceProductReadModelDocument.CollectionName)
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .ToListAsync();
        var storefront = await LoadVarCon02StorefrontDocumentAsync(mongoDatabase, seeded);
        var productSearchDocument = ProductSearchDocumentMapper.MapFromProductDocument(
            storefront,
            triggeringEvent.EventId);
        var projectionOutboxResidue = await verifyContext.Set<MarketplaceProjectionOutboxMessage>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(message =>
                message.EventId == triggeringEvent.EventId &&
                message.ConsumerName == nameof(MarketplaceProductUpdatedConsumer));
        var expectedMarketplacePrice = await ResolveVarCon07SqlMarketplacePriceAsync(
            verifyContext,
            seeded);
        var expectedMarketplaceAvailability = await ResolveVarCon07SqlMarketplaceAvailabilityAsync(
            verifyContext,
            seeded);

        var marketplace = Assert.Single(marketplaceDocuments);
        Assert.Equal(expectedMarketplacePrice, marketplace.Price);
        Assert.NotEqual(VarCon06V10OnlinePrice, marketplace.Price);
        Assert.Equal(expected.EventVersion, marketplace.LastEventVersion);
        Assert.True(marketplace.LastEntityMutationVersion >= expected.EventVersion);
        Assert.Equal(expectedMarketplaceAvailability, marketplace.IsAvailable);
        Assert.NotEmpty(marketplace.Images);
        Assert.DoesNotContain(VarCon06V10Sku, marketplace.Slug ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(VarCon06V10Sku, marketplace.Name, StringComparison.Ordinal);
        Assert.True(productSearchDocument.HasVariants);
        Assert.True(productSearchDocument.IsInStock);
        Assert.Equal((int)VarCon02StockQuantity, productSearchDocument.StockQuantity);
        Assert.Equal(0, projectionOutboxResidue);

        progressEntries.Enqueue("verification:var-con-07-marketplace-read-model-v11-visible");
    }

    private static async Task<decimal> ResolveVarCon07SqlMarketplacePriceAsync(
        ApplicationDbContext context,
        VarCon02Scenario seeded)
    {
        var activeVariants = await context.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(variant =>
                variant.TenantId == seeded.TenantId &&
                variant.ProductId == seeded.ProductId &&
                variant.IsActive &&
                !variant.IsDeleted)
            .OrderByDescending(variant => variant.IsDefault)
            .ThenBy(variant => variant.SortOrder)
            .Select(variant => new
            {
                variant.Price,
                variant.OnlinePrice
            })
            .ToListAsync();

        var defaultVariant = activeVariants.FirstOrDefault()
            ?? throw new XunitException("VAR-CON-07 marketplace price could not resolve an active SQL variant.");

        return defaultVariant.OnlinePrice is > 0
            ? defaultVariant.OnlinePrice.Value
            : defaultVariant.Price;
    }

    private static async Task<bool> ResolveVarCon07SqlMarketplaceAvailabilityAsync(
        ApplicationDbContext context,
        VarCon02Scenario seeded)
    {
        var product = await context.Products
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleAsync(item => item.TenantId == seeded.TenantId && item.Id == seeded.ProductId);
        var availableStock = await context.ProductVariantStocks
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(stock =>
                stock.TenantId == seeded.TenantId &&
                stock.Variant.ProductId == seeded.ProductId &&
                stock.Variant.IsActive &&
                !stock.Variant.IsDeleted &&
                !stock.IsDeleted)
            .SumAsync(stock => stock.Quantity - stock.ReservedQuantity);

        return !product.IsDeleted &&
               product.IsActive &&
               product.IsOnlineVisible &&
               (!product.TrackStock || availableStock > 0);
    }

    private static async Task AssertVarCon07DerivedVisibilityConvergedAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded,
        VarCon06ExpectedState expected,
        ConcurrentQueue<string> progressEntries)
    {
        await AssertVarCon07SqlTruthAsync(scope, tenantContext, seeded, expected);

        var visibility = await CaptureVarCon03VisibilityAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded);
        var storefront = await LoadVarCon02StorefrontDocumentAsync(mongoDatabase, seeded);
        var productSearchDocument = ProductSearchDocumentMapper.MapFromProductDocument(
            storefront,
            VarCon07V11ProjectionEventId);
        var commerceHub = await LoadVarCon02CommerceHubDocumentAsync(mongoDatabase, seeded);

        Assert.Equal(1, visibility.StorefrontVariantCount);
        Assert.Equal(expected.Sku, visibility.StorefrontSku);
        Assert.Equal(expected.Barcode, visibility.StorefrontBarcode);
        Assert.Equal(expected.OnlinePrice, visibility.StorefrontEffectivePrice);
        Assert.Equal(expected.EventVersion, visibility.StorefrontLastEventVersion);
        Assert.Equal(expected.AttributeFingerprint, visibility.StorefrontAttributeFingerprint);
        Assert.Equal(VarCon02StockQuantity, visibility.StorefrontTotalStock);
        Assert.Equal(expected.OnlinePrice, visibility.AffiliateMaxVariantPrice);
        Assert.Equal(VarCon02StockQuantity, visibility.AffiliateTotalAvailableStock);
        Assert.Equal(expected.OnlinePrice, visibility.CommerceHubPrice);
        Assert.Equal(VarCon02StockQuantity, visibility.CommerceHubAvailableStock);
        Assert.Equal(seeded.VariantId, visibility.CommerceHubVariantId);
        Assert.True(visibility.AffiliateVisible);
        Assert.True(visibility.CommerceHubPublished);
        Assert.DoesNotContain(VarCon06V10Sku, storefront.Variants.Select(variant => variant.SKU));
        Assert.DoesNotContain(VarCon06V10Barcode, storefront.Variants.Select(variant => variant.Barcode));
        Assert.Contains("XXL", commerceHub.AttributesJson ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("XL", (commerceHub.AttributesJson ?? string.Empty).Replace("XXL", string.Empty, StringComparison.Ordinal));
        Assert.True(productSearchDocument.HasVariants);
        Assert.True(productSearchDocument.IsInStock);
        Assert.Equal((int)VarCon02StockQuantity, productSearchDocument.StockQuantity);

        progressEntries.Enqueue("verification:var-con-07-v11-visible-across-derived-systems");
    }

    private static async Task<VarCon07VisibilitySnapshot> CaptureVarCon07VisibilityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded)
    {
        var variantVisibility = await CaptureVarCon03VisibilityAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded);
        var storefront = await LoadVarCon02StorefrontDocumentAsync(mongoDatabase, seeded);
        var searchDocument = ProductSearchDocumentMapper.MapFromProductDocument(
            storefront,
            VarCon07V11MarketplaceEventId);
        var marketplaceDocuments = await mongoDatabase
            .GetCollection<MarketplaceProductReadModelDocument>(MarketplaceProductReadModelDocument.CollectionName)
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .ToListAsync();
        var marketplace = Assert.Single(marketplaceDocuments);

        return new VarCon07VisibilitySnapshot(
            variantVisibility,
            marketplace.Price,
            marketplace.IsAvailable,
            marketplace.LastEventId,
            marketplace.LastEventVersion,
            marketplace.LastEntityMutationVersion,
            string.Join("|", marketplace.Images.OrderBy(item => item, StringComparer.Ordinal)),
            searchDocument.HasVariants,
            searchDocument.IsInStock,
            searchDocument.StockQuantity);
    }

    private static async Task<int> CountMarketplaceProductProcessedEventsAsync(
        ApplicationDbContext context,
        Guid eventId)
    {
        var eventType = typeof(MarketplaceEvents.ProductUpdatedEvent).FullName ?? nameof(MarketplaceEvents.ProductUpdatedEvent);
        var legacyEventType = nameof(MarketplaceEvents.ProductUpdatedEvent);
        return await context.Set<ProcessedEvent>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(item =>
                item.EventId == eventId &&
                item.ConsumerName == nameof(MarketplaceProductUpdatedConsumer) &&
                (item.EventType == eventType || item.EventType == legacyEventType));
    }

    private static async Task AssertVarCon02PostCommitReplaySafetyAsync(
        string connectionString,
        TestTenantContext tenantContext,
        VarCon02Scenario seeded,
        VariantActor actor,
        ConcurrentQueue<string> progressEntries)
    {
        await using var beforeContext = CreateDbContext(connectionString, tenantContext);
        var before = await SnapshotVarCon02DurableStateAsync(beforeContext, seeded);

        await using var replayContext = CreateDbContext(connectionString, tenantContext);
        await ConfigureSessionTimeoutsAsync(replayContext);
        var replayController = CreateUpdateController(
            replayContext,
            tenantContext,
            actor,
            new RecordingLogger<ProductVariantsController>());
        var replayResult = await replayController.UpdateVariant(
            seeded.ProductId,
            seeded.VariantId,
            BuildVarCon02UpdateRequest());
        var replayParsed = ParseUpdateActorResult(
            actor,
            replayResult,
            TimeSpan.Zero,
            progressEntries);

        Assert.True(replayParsed.Succeeded, replayParsed.PayloadJson ?? "Post-commit VAR-CON-02 replay did not return the committed variant mutation.");
        Assert.NotNull(replayParsed.Response);
        Assert.Equal(seeded.VariantId, replayParsed.Response!.Id);
        Assert.Equal(VarCon02MutatedSku, replayParsed.Response.SKU);
        Assert.Equal(VarCon02MutatedBarcode, replayParsed.Response.Barcode);

        await using var afterContext = CreateDbContext(connectionString, tenantContext);
        var after = await SnapshotVarCon02DurableStateAsync(afterContext, seeded);

        Assert.Equal(before, after);
        progressEntries.Enqueue("verification:var-con-02-post-commit-replay-idempotent");
    }

    private static async Task<VarCon02DurableSnapshot> SnapshotVarCon02DurableStateAsync(
        ApplicationDbContext context,
        VarCon02Scenario seeded)
    {
        var variantIds = await context.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == seeded.TenantId && item.ProductId == seeded.ProductId)
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .ToArrayAsync();
        var stockIds = await context.ProductVariantStocks
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == seeded.TenantId && item.Variant.ProductId == seeded.ProductId)
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .ToArrayAsync();
        var idempotencyIds = await context.ProductRequestIdempotencyRecords
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == seeded.TenantId &&
                item.ProductId == seeded.ProductId &&
                item.Operation == "UPDATE_VARIANT")
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .ToArrayAsync();
        var outboxIds = await context.OutboxMessages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == seeded.TenantId && item.AggregateId == seeded.VariantId)
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .ToArrayAsync();

        return new VarCon02DurableSnapshot(
            string.Join("|", variantIds),
            string.Join("|", stockIds),
            string.Join("|", idempotencyIds),
            string.Join("|", outboxIds));
    }

    private static async Task<ProductDocument> LoadVarCon02StorefrontDocumentAsync(
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded)
    {
        return await mongoDatabase.GetCollection<ProductDocument>("products")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync()
            ?? throw new XunitException("VAR-CON-02 storefront product document was not visible.");
    }

    private static async Task<PublicAffiliateProductDocument> LoadVarCon02AffiliateDocumentAsync(
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded)
    {
        return await mongoDatabase.GetCollection<PublicAffiliateProductDocument>("public_affiliate_products")
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync()
            ?? throw new XunitException("VAR-CON-02 affiliate product document was not visible.");
    }

    private static async Task<CommerceHubProductDocument> LoadVarCon02CommerceHubDocumentAsync(
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded)
    {
        return await mongoDatabase.GetCollection<CommerceHubProductDocument>("commercehub_products")
            .Find(document => document.TenantId == seeded.TenantId && document.Id == VarCon02CommerceHubDocumentId)
            .FirstOrDefaultAsync()
            ?? throw new XunitException("VAR-CON-02 CommerceHub product document was not visible.");
    }

    private static async Task<int> CountOrphanVariantAttributesAsync(ApplicationDbContext context)
    {
        return await ExecuteScalarIntAsync(
            context,
            """
            SELECT COUNT(*)
            FROM "ProductVariantAttributes" attribute
            LEFT JOIN "ProductVariants" variant ON variant."Id" = attribute."VariantId"
            WHERE variant."Id" IS NULL;
            """);
    }

    private static async Task<int> CountOrphanVariantStocksAsync(ApplicationDbContext context)
    {
        return await ExecuteScalarIntAsync(
            context,
            """
            SELECT COUNT(*)
            FROM "ProductVariantStocks" stock
            LEFT JOIN "ProductVariants" variant ON variant."Id" = stock."VariantId"
            WHERE variant."Id" IS NULL;
            """);
    }

    private static async Task<int> CountOrphanStockMovementsAsync(ApplicationDbContext context)
    {
        return await ExecuteScalarIntAsync(
            context,
            """
            SELECT COUNT(*)
            FROM "StockMovements" movement
            LEFT JOIN "ProductVariants" variant ON variant."Id" = movement."ProductVariantId"
            WHERE movement."ProductVariantId" IS NOT NULL
              AND variant."Id" IS NULL;
            """);
    }

    private static async Task<ProdCon03CanonicalState> LoadProdCon03CanonicalStateAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        Guid variantId)
    {
        await using var context = scope.CreateDbContext(tenantContext);

        var product = await context.Products
            .AsNoTracking()
            .SingleAsync(product => product.Id == ProductId);
        var variant = await context.ProductVariants
            .AsNoTracking()
            .Include(item => item.Stocks)
            .SingleAsync(item => item.Id == variantId);
        var stock = Assert.Single(variant.Stocks);
        var variantSku = variant.SKU
            ?? throw new XunitException("PROD-CON-03 canonical variant SKU was unexpectedly null.");

        return new ProdCon03CanonicalState(
            ProductProjectionMutationVersion: product.ProjectionMutationVersion,
            VariantProjectionMutationVersion: variant.ProjectionMutationVersion,
            StockProjectionMutationVersion: stock.ProjectionMutationVersion,
            ProductName: product.Name,
            ProductSlug: product.Slug,
            ProductSalePrice: product.SalePrice,
            VariantSku: variantSku,
            VariantBarcode: variant.Barcode,
            VariantOnlinePrice: variant.OnlinePrice,
            VariantMediaId: variant.MediaId,
            StockId: stock.Id,
            StockQuantity: stock.Quantity,
            StockWarehouseId: stock.WarehouseId);
    }

    private static MarketplaceEvents.ProductUpdatedEvent BuildProdCon03ProductSyncEvent(ProdCon03CanonicalState canonicalState)
    {
        var eventVersion = ProductEventVersionHelper.ToProductEventVersion(
            Math.Max(
                canonicalState.ProductProjectionMutationVersion,
                Math.Max(canonicalState.VariantProjectionMutationVersion, canonicalState.StockProjectionMutationVersion)) + 1);

        return new MarketplaceEvents.ProductUpdatedEvent
        {
            EventId = ProdCon03EventId,
            CorrelationId = ProdCon03CorrelationId,
            EntityId = ProductId,
            AggregateRootId = ProductId,
            TenantId = TenantId,
            Version = eventVersion,
            ChangedFields = ["Name", "Slug", "Images", "Variants", "Stock"],
            Name = canonicalState.ProductName,
            Sku = canonicalState.VariantSku,
            Slug = canonicalState.ProductSlug,
            SalePrice = canonicalState.ProductSalePrice,
            OnlinePrice = canonicalState.VariantOnlinePrice,
            CategoryId = CategoryId,
            IsOnlineVisible = true,
            HasVariants = true
        };
    }

    private static MarketplaceEvents.ProductUpdatedEvent BuildVarCon07MarketplaceProductEvent(
        VarCon02Scenario seeded,
        ProductVariantUpdatedEvent variantEvent,
        Guid eventId,
        string correlationId)
    {
        return new MarketplaceEvents.ProductUpdatedEvent
        {
            EventId = eventId,
            OccurredAtUtc = variantEvent.OccurredAtUtc,
            CorrelationId = correlationId,
            EntityId = seeded.ProductId,
            AggregateRootId = seeded.ProductId,
            TenantId = seeded.TenantId,
            Version = variantEvent.EventVersion,
            ChangedFields =
            [
                "Variants",
                "SKU",
                "Barcode",
                "Images",
                "Search",
                "Cache"
            ],
            Name = "VAR-CON-07 stale marketplace payload must not become authority",
            Sku = variantEvent.SKU,
            Slug = "var-con-02-product",
            SalePrice = variantEvent.Price,
            OnlinePrice = variantEvent.OnlinePrice,
            CategoryId = CategoryId,
            IsOnlineVisible = true,
            HasVariants = true
        };
    }

    private static async Task RebuildVarCon07MarketplaceReadModelFromSqlAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        VarCon02Scenario seeded,
        Guid eventId,
        int eventVersion,
        ConcurrentQueue<string> progressEntries)
    {
        await using var context = scope.CreateDbContext(tenantContext);
        var repository = new MarketplaceProductReadRepository(mongoDatabase);
        var service = new MarketplaceProductReadModelProjectionService(
            context,
            repository,
            NullLogger<MarketplaceProductReadModelProjectionService>.Instance);

        var result = await service.RebuildAsync(
            seeded.ProductId,
            seeded.TenantId,
            eventId,
            DateTime.UtcNow,
            eventVersion);

        Assert.True(
            result is MarketplaceProjectionResult.Applied or MarketplaceProjectionResult.SkippedStale,
            $"Unexpected VAR-CON-07 marketplace rebuild result: {result}");
        progressEntries.Enqueue($"verification:var-con-07-marketplace-rebuilt-from-sql-truth:{result}");
    }

    private static async Task EnqueueProdCon03MarketplaceOutboxAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        MarketplaceEvents.ProductUpdatedEvent productSyncEvent,
        ConcurrentQueue<string> progressEntries)
    {
        await using var context = scope.CreateDbContext(tenantContext);
        var outboxService = new MarketplaceEventOutboxService(
            context,
            NullLogger<MarketplaceEventOutboxService>.Instance);

        await outboxService.EnqueueAsync(productSyncEvent);
        await context.SaveChangesAsync();

        progressEntries.Enqueue($"prod-con-03:marketplace-outbox-enqueued:{productSyncEvent.EventId}");
    }

    private static async Task<ProductSyncOutboxDispatchResult> DispatchProdCon03MarketplaceOutboxAsync(
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
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString));
        services.AddScoped(_ => publishEndpoint.Object);
        services.AddSingleton(Options.Create(new MarketplaceEventBackboneOptions()));
        services.AddSingleton(NullLogger<MarketplaceEventOutboxProcessor>.Instance);

        await using var serviceProvider = services.BuildServiceProvider();
        var processor = new MarketplaceEventOutboxProcessor(
            serviceProvider,
            NullLogger<MarketplaceEventOutboxProcessor>.Instance,
            Options.Create(new MarketplaceEventBackboneOptions()));

        var processedCount = await processor.ProcessPendingMessagesAsync();
        progressEntries.Enqueue($"prod-con-03:marketplace-outbox-processed:{processedCount}");

        return new ProductSyncOutboxDispatchResult(
            ProcessedCount: processedCount,
            PublishedEvent: publishedEvent,
            PublishedType: publishedType);
    }

    private static async Task<ProductSyncWorkerResult> ExecuteProductSyncWorkerAsync(
        string connectionString,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        MarketplaceEvents.ProductUpdatedEvent productSyncEvent,
        ProductSyncWorker worker,
        ConcurrentQueue<string> progressEntries,
        params IInterceptor[] interceptors)
    {
        await using var context = CreateDbContext(connectionString, tenantContext, interceptors);
        await ConfigureSessionTimeoutsAsync(context);

        var projectionServices = new ServiceCollection();
        projectionServices.AddLogging();
        projectionServices.AddOptions();
        projectionServices.Configure<MarketplaceEventBackboneOptions>(_ => { });
        projectionServices.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString));
        projectionServices.AddSingleton<IMongoDatabase>(mongoDatabase);
        projectionServices.AddScoped<PointOfSale.Infrastructure.MongoDB.Services.Marketplace.IMarketplaceProductReadRepository, MarketplaceProductReadRepository>();
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

        var consumeContext = BuildConsumeContext(productSyncEvent);
        progressEntries.Enqueue($"{worker.Name}:consume-started");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await consumer.Consume(consumeContext.Object);
            stopwatch.Stop();
            progressEntries.Enqueue($"{worker.Name}:consume-completed");

            return new ProductSyncWorkerResult(worker.Name, stopwatch.Elapsed, null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            progressEntries.Enqueue($"{worker.Name}:threw:{DescribeException(ex)}");
            return new ProductSyncWorkerResult(worker.Name, stopwatch.Elapsed, ex);
        }
    }

    private static Mock<ConsumeContext<TEvent>> BuildConsumeContext<TEvent>(
        TEvent message)
        where TEvent : class
    {
        var context = new Mock<ConsumeContext<TEvent>>();
        context.SetupGet(entry => entry.Message).Returns(message);
        context.SetupGet(entry => entry.CancellationToken).Returns(CancellationToken.None);
        context.SetupGet(entry => entry.CorrelationId).Returns((Guid?)null);
        return context;
    }

    private static async Task AssertProdCon03NoUncommittedVisibilityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        SeededScenario seeded,
        Guid canonicalVariantId,
        MarketplaceEvents.ProductUpdatedEvent productSyncEvent,
        ProdCon03VisibilitySnapshot baselineVisibility,
        ConcurrentQueue<string> progressEntries)
    {
        var currentVisibility = await CaptureProdCon03VisibilitySnapshotAsync(
            scope,
            tenantContext,
            mongoDatabase,
            seeded,
            canonicalVariantId,
            productSyncEvent);

        Assert.Equal(baselineVisibility, currentVisibility);
        Assert.Equal(baselineVisibility.CommerceHubListingCount, currentVisibility.CommerceHubListingCount);
        Assert.Equal(baselineVisibility.ProductMediaCount, currentVisibility.ProductMediaCount);
        Assert.Equal(baselineVisibility.StockSyncOutboxCount, currentVisibility.StockSyncOutboxCount);
        Assert.Equal(baselineVisibility.ProductRequestIdempotencyCount, currentVisibility.ProductRequestIdempotencyCount);
        Assert.Equal(baselineVisibility.MarketplaceOutboxCount, currentVisibility.MarketplaceOutboxCount);
        Assert.Equal(baselineVisibility.ProductVariantCount, currentVisibility.ProductVariantCount);
        Assert.Equal(baselineVisibility.ProductVariantStockCount, currentVisibility.ProductVariantStockCount);
        Assert.Equal(0, currentVisibility.ProjectionOutboxCount);
        Assert.Equal(0, currentVisibility.MarketplaceReadModelCount);
        Assert.Equal(0, currentVisibility.ProcessedEventCount);
        progressEntries.Enqueue("verification:prod-con-03-baseline-only-visible-during-active-window");
    }

    private static async Task AssertProdCon03ConvergenceAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        SeededScenario seeded,
        Guid canonicalVariantId,
        ProdCon03CanonicalState canonicalState,
        MarketplaceEvents.ProductUpdatedEvent productSyncEvent,
        ProdCon03VisibilitySnapshot baselineVisibility,
        IReadOnlyCollection<ProductSyncWorkerResult> results,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var processedEvents = await verifyContext.Set<ProcessedEvent>()
            .IgnoreQueryFilters()
            .Where(processed =>
                processed.EventId == productSyncEvent.EventId &&
                processed.ConsumerName == nameof(MarketplaceProductUpdatedConsumer))
            .OrderBy(processed => processed.CreatedAt)
            .ToListAsync();
        var projectionOutboxMessages = await verifyContext.Set<MarketplaceProjectionOutboxMessage>()
            .IgnoreQueryFilters()
            .Where(message =>
                message.EventId == productSyncEvent.EventId &&
                message.ConsumerName == nameof(MarketplaceProductUpdatedConsumer))
            .OrderBy(message => message.CreatedAt)
            .ToListAsync();
        var outboxMessages = await verifyContext.Set<MarketplaceEventOutboxMessage>()
            .IgnoreQueryFilters()
            .Where(message => message.EventId == productSyncEvent.EventId)
            .ToListAsync();
        var commerceHubListings = await verifyContext.CommerceHubListings
            .Where(listing =>
                listing.TenantId == seeded.TenantId &&
                listing.ProductId == seeded.ProductId &&
                listing.VariantId == canonicalVariantId)
            .ToListAsync();
        var productVariant = await verifyContext.ProductVariants
            .Include(variant => variant.Attributes)
            .Include(variant => variant.Stocks)
            .SingleAsync(variant => variant.Id == canonicalVariantId);
        var productMediaCount = await verifyContext.ProductMedia
            .CountAsync(media => media.TenantId == seeded.TenantId && media.ProductId == seeded.ProductId);
        var stockSyncOutboxMessages = await verifyContext.StockSyncOutboxMessages
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.ProductId == seeded.ProductId &&
                message.VariantId == canonicalVariantId)
            .ToListAsync();
        var productRequestIdempotencyRecords = await verifyContext.ProductRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .Where(record =>
                record.TenantId == seeded.TenantId &&
                record.ProductId == seeded.ProductId &&
                record.ProductVariantId == canonicalVariantId)
            .ToListAsync();
        var productMediaRecords = await verifyContext.ProductMedia
            .Where(media =>
                media.TenantId == seeded.TenantId &&
                media.ProductId == seeded.ProductId &&
                media.Id == seeded.MediaId)
            .ToListAsync();
        var productVariants = await verifyContext.ProductVariants
            .Include(variant => variant.Attributes)
            .Include(variant => variant.Stocks)
            .Where(variant =>
                variant.TenantId == seeded.TenantId &&
                variant.ProductId == seeded.ProductId &&
                variant.SKU == ContestedSku)
            .ToListAsync();
        var productVariantStocks = await verifyContext.ProductVariantStocks
            .Where(stock =>
                stock.TenantId == seeded.TenantId &&
                stock.Variant.ProductId == seeded.ProductId &&
                stock.Variant.SKU == ContestedSku)
            .ToListAsync();
        var marketplaceOutboxCount = await CountMarketplaceOutboxMessagesForProductAsync(
            verifyContext,
            seeded);

        var productCollection = mongoDatabase.GetCollection<MarketplaceProductReadModelDocument>(
            MarketplaceProductReadModelDocument.CollectionName);
        var readModelDocuments = await productCollection
            .Find(document => document.TenantId == seeded.TenantId && document.ProductId == seeded.ProductId)
            .ToListAsync();

        Assert.Equal(baselineVisibility.CommerceHubListingCount, commerceHubListings.Count);
        Assert.Equal(baselineVisibility.ProductMediaCount, productMediaRecords.Count);
        Assert.Equal(baselineVisibility.StockSyncOutboxCount, stockSyncOutboxMessages.Count);
        Assert.Equal(baselineVisibility.ProductRequestIdempotencyCount, productRequestIdempotencyRecords.Count);
        Assert.Equal(baselineVisibility.ProductVariantCount, productVariants.Count);
        Assert.Equal(baselineVisibility.ProductVariantStockCount, productVariantStocks.Count);
        Assert.Equal(baselineVisibility.MarketplaceOutboxCount, marketplaceOutboxCount);
        Assert.Equal(baselineVisibility.ProjectionOutboxCount, projectionOutboxMessages.Count);
        Assert.Equal(baselineVisibility.ProcessedEventCount + 1, processedEvents.Count);
        Assert.Equal(baselineVisibility.MarketplaceReadModelCount + 1, readModelDocuments.Count);

        Assert.Single(processedEvents);
        Assert.Empty(projectionOutboxMessages);
        Assert.Single(outboxMessages);
        Assert.Single(commerceHubListings);
        Assert.Single(stockSyncOutboxMessages);
        Assert.Single(productRequestIdempotencyRecords);
        Assert.Single(readModelDocuments);
        Assert.Single(productMediaRecords);
        Assert.Single(productVariants);
        Assert.Single(productVariantStocks);

        var processed = processedEvents.Single();
        Assert.Equal(productSyncEvent.EventId, processed.EventId);
        var allowedProcessedEventTypes = new[]
        {
            typeof(MarketplaceEvents.ProductUpdatedEvent).FullName ?? nameof(MarketplaceEvents.ProductUpdatedEvent),
            nameof(MarketplaceEvents.ProductUpdatedEvent)
        };
        Assert.Contains(processed.EventType, allowedProcessedEventTypes);
        Assert.Equal(seeded.TenantId, processed.TenantId);

        var outbox = outboxMessages.Single();
        Assert.NotNull(outbox.PublishedAtUtc);
        Assert.False(outbox.IsFailed);
        Assert.Equal(productSyncEvent.EventId, outbox.EventId);
        Assert.Equal(productSyncEvent.Version, outbox.Version);

        var listing = commerceHubListings.Single();
        Assert.True(listing.IsPublished);
        Assert.Equal(CommerceHubCategoryId, listing.CommerceHubCategoryId);
        Assert.Equal(canonicalVariantId, listing.VariantId);

        Assert.Equal(canonicalState.ProductProjectionMutationVersion, (await verifyContext.Products.SingleAsync(product => product.Id == seeded.ProductId)).ProjectionMutationVersion);
        Assert.Equal(canonicalState.VariantProjectionMutationVersion, productVariant.ProjectionMutationVersion);
        Assert.Equal(canonicalState.VariantSku, productVariant.SKU);
        Assert.Equal(canonicalState.VariantBarcode, productVariant.Barcode);
        Assert.Equal(canonicalState.VariantMediaId, productVariant.MediaId);
        Assert.Equal(canonicalState.StockWarehouseId, productVariant.Stocks.Single().WarehouseId);
        Assert.Equal(canonicalState.StockQuantity, productVariant.Stocks.Single().Quantity);
        Assert.Equal(canonicalState.StockProjectionMutationVersion, productVariant.Stocks.Single().ProjectionMutationVersion);

        var stockSync = stockSyncOutboxMessages.Single();
        Assert.Equal(nameof(ProductVariantStockChangedEvent), stockSync.EventType);
        Assert.Equal(canonicalVariantId, stockSync.VariantId);
        Assert.Equal(seeded.WarehouseId, stockSync.WarehouseId);

        var idempotency = productRequestIdempotencyRecords.Single();
        Assert.Equal(BuildProductMutationIdempotencyKey(seeded.TenantId, "prod-con-03-authoritative-create"), idempotency.Key);
        Assert.NotNull(idempotency.CompletedAtUtc);
        Assert.Equal(canonicalVariantId, idempotency.ProductVariantId);

        var expectedReadModelMutationVersion = Math.Max(
            canonicalState.ProductProjectionMutationVersion,
            Math.Max(
                canonicalState.VariantProjectionMutationVersion,
                canonicalState.StockProjectionMutationVersion));

        var readModel = readModelDocuments.Single();
        Assert.Equal(seeded.TenantId, readModel.TenantId);
        Assert.Equal(seeded.ProductId, readModel.ProductId);
        Assert.Equal(productSyncEvent.EventId, readModel.LastEventId);
        Assert.Equal(productSyncEvent.Version, readModel.LastEventVersion);
        Assert.Equal(expectedReadModelMutationVersion, readModel.LastEntityMutationVersion);
        Assert.NotEmpty(readModel.Images);

        var productMedia = productMediaRecords.Single();
        Assert.Equal(seeded.TenantId, productMedia.TenantId);
        Assert.Equal(seeded.ProductId, productMedia.ProductId);
        Assert.Equal(seeded.MediaId, productMedia.Id);

        var persistedVariant = productVariants.Single();
        Assert.Equal(canonicalVariantId, persistedVariant.Id);
        Assert.Equal(canonicalState.VariantProjectionMutationVersion, persistedVariant.ProjectionMutationVersion);
        Assert.Equal(canonicalState.VariantSku, persistedVariant.SKU);
        Assert.Equal(canonicalState.VariantBarcode, persistedVariant.Barcode);
        Assert.Equal(canonicalState.VariantMediaId, persistedVariant.MediaId);

        var persistedVariantStock = productVariantStocks.Single();
        Assert.Equal(canonicalState.StockId, persistedVariantStock.Id);
        Assert.Equal(canonicalState.StockQuantity, persistedVariantStock.Quantity);
        Assert.Equal(canonicalState.StockWarehouseId, persistedVariantStock.WarehouseId);
        Assert.Equal(canonicalState.StockProjectionMutationVersion, persistedVariantStock.ProjectionMutationVersion);

        Assert.All(results, result => Assert.Null(result.Exception));
        Assert.Equal(2, results.Count);
        Assert.True(results.Any(result => result.WorkerName == "product-sync-worker-a"));
        Assert.True(results.Any(result => result.WorkerName == "product-sync-worker-b"));
        progressEntries.Enqueue("verification:prod-con-03-converged");
    }

    private static async Task<ProdCon03VisibilitySnapshot> CaptureProdCon03VisibilitySnapshotAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        SeededScenario seeded,
        Guid canonicalVariantId,
        MarketplaceEvents.ProductUpdatedEvent productSyncEvent)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var processedEventCount = await verifyContext.Set<ProcessedEvent>()
            .IgnoreQueryFilters()
            .CountAsync(processed =>
                processed.EventId == productSyncEvent.EventId &&
                processed.ConsumerName == nameof(MarketplaceProductUpdatedConsumer));
        var projectionOutboxCount = await verifyContext.Set<MarketplaceProjectionOutboxMessage>()
            .IgnoreQueryFilters()
            .CountAsync(message =>
                message.EventId == productSyncEvent.EventId &&
                message.ConsumerName == nameof(MarketplaceProductUpdatedConsumer));

        var commerceHubListingCount = await verifyContext.CommerceHubListings
            .CountAsync(listing =>
                listing.TenantId == seeded.TenantId &&
                listing.ProductId == seeded.ProductId &&
                listing.VariantId == canonicalVariantId);

        var productMediaCount = await verifyContext.ProductMedia
            .CountAsync(media =>
                media.TenantId == seeded.TenantId &&
                media.ProductId == seeded.ProductId &&
                media.Id == seeded.MediaId);

        var stockSyncOutboxCount = await verifyContext.StockSyncOutboxMessages
            .IgnoreQueryFilters()
            .CountAsync(message =>
                message.TenantId == seeded.TenantId &&
                message.ProductId == seeded.ProductId &&
                message.VariantId == canonicalVariantId);

        var productRequestIdempotencyCount = await verifyContext.ProductRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .CountAsync(record =>
                record.TenantId == seeded.TenantId &&
                record.ProductId == seeded.ProductId &&
                record.ProductVariantId == canonicalVariantId &&
                record.Operation == "CREATE_VARIANT");

        var marketplaceOutboxCount = await CountMarketplaceOutboxMessagesForProductAsync(
            verifyContext,
            seeded);

        var productVariantCount = await verifyContext.ProductVariants
            .CountAsync(variant =>
                variant.TenantId == seeded.TenantId &&
                variant.ProductId == seeded.ProductId &&
                variant.SKU == ContestedSku);

        var productVariantStockCount = await verifyContext.ProductVariantStocks
            .CountAsync(stock =>
                stock.TenantId == seeded.TenantId &&
                stock.Variant.ProductId == seeded.ProductId &&
                stock.Variant.SKU == ContestedSku);

        var readModelCount = await mongoDatabase.GetCollection<MarketplaceProductReadModelDocument>(
                MarketplaceProductReadModelDocument.CollectionName)
            .CountDocumentsAsync(document =>
                document.TenantId == seeded.TenantId &&
                document.ProductId == seeded.ProductId &&
                document.LastEventId == productSyncEvent.EventId);

        return new ProdCon03VisibilitySnapshot(
            ProcessedEventCount: processedEventCount,
            ProjectionOutboxCount: projectionOutboxCount,
            MarketplaceReadModelCount: readModelCount,
            CommerceHubListingCount: commerceHubListingCount,
            ProductMediaCount: productMediaCount,
            StockSyncOutboxCount: stockSyncOutboxCount,
            ProductRequestIdempotencyCount: productRequestIdempotencyCount,
            MarketplaceOutboxCount: marketplaceOutboxCount,
            ProductVariantCount: productVariantCount,
            ProductVariantStockCount: productVariantStockCount);
    }

    private static async Task<ProdCon03MongoDatabaseScope> CreateIsolatedMongoDatabaseScopeAsync(string scenarioKey)
    {
        var connectionString = GetRequiredMongoConnectionString();
        var client = new MongoClient(connectionString);
        var databaseName = $"prod_con_03_{TestConfiguration.BuildDeterministicSuffix($"{scenarioKey}_{Guid.NewGuid():N}")}";
        var database = client.GetDatabase(databaseName);

        await database.ListCollectionNamesAsync();

        return new ProdCon03MongoDatabaseScope(client, databaseName, database);
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

    private static async Task SeedCommerceHubListingAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid variantId)
    {
        await using var context = scope.CreateDbContext(tenantContext);

        if (await context.CommerceHubCategories.AnyAsync(category => category.Id == CommerceHubCategoryId) == false)
        {
            var connectionString = context.Database.GetConnectionString()
                ?? throw new InvalidOperationException("The test database connection string was not available.");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            await using var columnCommand = new NpgsqlCommand(
                """
                SELECT column_name
                FROM information_schema.columns
                WHERE table_schema = current_schema()
                  AND table_name = 'CommerceHubCategories'
                  AND column_name IN ('CreatedAtUtc', 'CreatedAt')
                ORDER BY CASE WHEN column_name = 'CreatedAtUtc' THEN 0 ELSE 1 END
                LIMIT 1;
                """,
                connection);

            var createdAtColumn = (string?)await columnCommand.ExecuteScalarAsync()
                ?? throw new InvalidOperationException("The CommerceHubCategories created-at column was not available.");

            await using var insertCommand = new NpgsqlCommand(
                $"""
                INSERT INTO "CommerceHubCategories" ("Id", "Name", "Slug", "Level", "IsActive", "SortOrder", "{createdAtColumn}")
                VALUES (@id, @name, @slug, @level, @isActive, @sortOrder, @createdAt)
                ON CONFLICT ("Id") DO NOTHING;
                """,
                connection);
            insertCommand.Parameters.AddWithValue("id", CommerceHubCategoryId);
            insertCommand.Parameters.AddWithValue("name", "PROD-CON-03 CommerceHub Category");
            insertCommand.Parameters.AddWithValue("slug", "prod-con-03-commercehub");
            insertCommand.Parameters.AddWithValue("level", 1);
            insertCommand.Parameters.AddWithValue("isActive", true);
            insertCommand.Parameters.AddWithValue("sortOrder", 1);
            insertCommand.Parameters.AddWithValue("createdAt", SeededUtc);

            await insertCommand.ExecuteNonQueryAsync();
        }

        if (await context.CommerceHubListings.AnyAsync(listing =>
                listing.TenantId == seeded.TenantId &&
                listing.ProductId == seeded.ProductId &&
                listing.VariantId == variantId) == false)
        {
            await InsertProdCon03CommerceHubListingAsync(context, seeded, variantId);
        }
    }

    private static async Task InsertProdCon03CommerceHubListingAsync(
        ApplicationDbContext context,
        SeededScenario seeded,
        Guid variantId)
    {
        var connectionString = context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        static async Task<bool> ColumnExistsAsync(
            NpgsqlConnection connection,
            string tableName,
            string columnName)
        {
            await using var command = new NpgsqlCommand(
                """
                SELECT EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = current_schema()
                      AND table_name = @tableName
                      AND column_name = @columnName
                );
                """,
                connection);
            command.Parameters.AddWithValue("tableName", tableName);
            command.Parameters.AddWithValue("columnName", columnName);
            return (bool)(await command.ExecuteScalarAsync() ?? false);
        }

        var createdAtColumn = await ColumnExistsAsync(connection, "CommerceHubListings", "CreatedAtUtc")
            ? "CreatedAtUtc"
            : "CreatedAt";
        var hasAttributesJson = await ColumnExistsAsync(connection, "CommerceHubListings", "AttributesJson");
        var hasAttributeSchemaSnapshotJson = await ColumnExistsAsync(connection, "CommerceHubListings", "AttributeSchemaSnapshotJson");
        var hasCommerceHubPriceOverrideMutationVersion = await ColumnExistsAsync(
            connection,
            "CommerceHubListings",
            "CommerceHubPriceOverrideMutationVersion");

        var columns = new List<string>
        {
            "Id",
            "TenantId",
            "ProductId",
            "VariantId",
            "IsPublished",
            "RestorePublicationOnActivate",
            "CommerceHubCategoryId",
            "IsDeleted"
        };

        var values = new List<string>
        {
            "@id",
            "@tenantId",
            "@productId",
            "@variantId",
            "@isPublished",
            "@restorePublicationOnActivate",
            "@commerceHubCategoryId",
            "@isDeleted"
        };

        if (hasAttributesJson)
        {
            columns.Add("AttributesJson");
            values.Add("@attributesJson");
        }

        if (hasAttributeSchemaSnapshotJson)
        {
            columns.Add("AttributeSchemaSnapshotJson");
            values.Add("@attributeSchemaSnapshotJson");
        }
        if (hasCommerceHubPriceOverrideMutationVersion)
        {
            columns.Add("CommerceHubPriceOverrideMutationVersion");
            values.Add("@commerceHubPriceOverrideMutationVersion");
        }

        columns.Add(createdAtColumn);
        values.Add("@createdAt");

        await using var insertCommand = new NpgsqlCommand(
            $"""
            INSERT INTO "CommerceHubListings" ({string.Join(", ", columns.Select(column => $"\"{column}\""))})
            VALUES ({string.Join(", ", values)})
            ON CONFLICT ("TenantId", "VariantId") DO NOTHING;
            """,
            connection);

        insertCommand.Parameters.AddWithValue("id", Guid.NewGuid());
        insertCommand.Parameters.AddWithValue("tenantId", seeded.TenantId);
        insertCommand.Parameters.AddWithValue("productId", seeded.ProductId);
        insertCommand.Parameters.AddWithValue("variantId", variantId);
        insertCommand.Parameters.AddWithValue("isPublished", true);
        insertCommand.Parameters.AddWithValue("restorePublicationOnActivate", false);
        insertCommand.Parameters.AddWithValue("commerceHubCategoryId", CommerceHubCategoryId);
        insertCommand.Parameters.AddWithValue("isDeleted", false);
        if (hasAttributesJson)
        {
            insertCommand.Parameters.AddWithValue("attributesJson", "{\"source\":\"prod-con-03\"}");
        }
        if (hasAttributeSchemaSnapshotJson)
        {
            insertCommand.Parameters.AddWithValue("attributeSchemaSnapshotJson", "{\"source\":\"prod-con-03\"}");
        }
        if (hasCommerceHubPriceOverrideMutationVersion)
        {
            insertCommand.Parameters.AddWithValue("commerceHubPriceOverrideMutationVersion", 0);
        }
        insertCommand.Parameters.AddWithValue("createdAt", SeededUtc);

        await insertCommand.ExecuteNonQueryAsync();
    }

    private static async Task AssertReplaySafetyAsync(
        string connectionString,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        VariantActorResult successfulResult,
        ConcurrentQueue<string> progressEntries)
    {
        await using var beforeContext = CreateDbContext(connectionString, tenantContext);
        var before = await SnapshotDurableStateAsync(beforeContext, seeded);

        await using var replayContext = CreateDbContext(connectionString, tenantContext);
        await ConfigureSessionTimeoutsAsync(replayContext);
        var replayController = CreateController(
            replayContext,
            tenantContext,
            successfulResult.Actor,
            new RecordingLogger<ProductVariantsController>());
        var replayResult = await replayController.CreateVariant(
            seeded.ProductId,
            BuildCreateVariantRequest(seeded));
        var replayParsed = ParseActorResult(
            successfulResult.Actor,
            replayResult,
            TimeSpan.Zero,
            progressEntries);

        Assert.True(replayParsed.Succeeded, replayParsed.PayloadJson ?? "Replay did not converge to the committed product variant.");
        Assert.NotNull(replayParsed.Response);
        Assert.Equal(successfulResult.Response!.Id, replayParsed.Response!.Id);
        Assert.Equal(successfulResult.Response.SKU, replayParsed.Response.SKU);
        Assert.Equal(successfulResult.Response.Barcode, replayParsed.Response.Barcode);

        await using var afterContext = CreateDbContext(connectionString, tenantContext);
        var after = await SnapshotDurableStateAsync(afterContext, seeded);

        Assert.Equal(before.VariantIds, after.VariantIds);
        Assert.Equal(before.StockIds, after.StockIds);
        Assert.Equal(before.StockSyncOutboxIds, after.StockSyncOutboxIds);
        Assert.Equal(before.IdempotencyIds, after.IdempotencyIds);
        progressEntries.Enqueue("verification:prod-con-02-post-commit-replay-safe");
    }

    private static async Task AssertVariantCombinationReplaySafetyAsync(
        string connectionString,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        VariantActorResult successfulResult,
        ConcurrentQueue<string> progressEntries)
    {
        await using var beforeContext = CreateDbContext(connectionString, tenantContext);
        var before = await SnapshotVariantCombinationDurableStateAsync(beforeContext, seeded);

        await using var replayContext = CreateDbContext(connectionString, tenantContext);
        await ConfigureSessionTimeoutsAsync(replayContext);
        var replayController = CreateController(
            replayContext,
            tenantContext,
            successfulResult.Actor,
            new RecordingLogger<ProductVariantsController>());
        var replayResult = await replayController.CreateVariant(
            seeded.ProductId,
            BuildCreateVariantRequest(seeded, successfulResult.Actor));
        var replayParsed = ParseActorResult(
            successfulResult.Actor,
            replayResult,
            TimeSpan.Zero,
            progressEntries);

        Assert.True(replayParsed.Succeeded, replayParsed.PayloadJson ?? "Variant combination replay did not converge to the committed product variant.");
        Assert.NotNull(replayParsed.Response);
        Assert.Equal(successfulResult.Response!.Id, replayParsed.Response!.Id);
        Assert.Equal(successfulResult.Response.SKU, replayParsed.Response.SKU);
        Assert.Equal(successfulResult.Response.Barcode, replayParsed.Response.Barcode);

        await using var afterContext = CreateDbContext(connectionString, tenantContext);
        var after = await SnapshotVariantCombinationDurableStateAsync(afterContext, seeded);

        Assert.Equal(before.VariantIds, after.VariantIds);
        Assert.Equal(before.StockIds, after.StockIds);
        Assert.Equal(before.StockSyncOutboxIds, after.StockSyncOutboxIds);
        Assert.Equal(before.IdempotencyIds, after.IdempotencyIds);
        progressEntries.Enqueue("verification:var-con-01-post-commit-replay-safe");
    }

    private static async Task ConfigureSessionTimeoutsAsync(ApplicationDbContext context)
    {
        context.Database.SetCommandTimeout(TimeSpan.FromSeconds(30));
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("SET statement_timeout = '30s'; SET lock_timeout = '30s';");
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException postgresException &&
               postgresException.SqlState == PostgresErrorCodes.UniqueViolation;
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

    private static async Task<int> ExecuteScalarIntAsync(
        ApplicationDbContext context,
        string sql)
    {
        var connectionString = context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync();

        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task<int> CountMarketplaceOutboxMessagesForProductAsync(
        ApplicationDbContext context,
        SeededScenario seeded)
    {
        var connectionString = context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var schemaCommand = new NpgsqlCommand(
            """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = current_schema()
              AND table_name = 'MarketplaceEventOutboxMessages'
              AND column_name IN ('TenantId', 'EntityId');
            """,
            connection);
        var schemaResult = await schemaCommand.ExecuteScalarAsync();
        var matchedColumns = Convert.ToInt32(schemaResult, CultureInfo.InvariantCulture);

        if (matchedColumns < 2)
        {
            return 0;
        }

        await using var countCommand = new NpgsqlCommand(
            """
            SELECT COUNT(*)
            FROM "MarketplaceEventOutboxMessages"
            WHERE "TenantId" = @tenantId
              AND "EntityId" = @entityId;
            """,
            connection);
        countCommand.Parameters.AddWithValue("tenantId", seeded.TenantId);
        countCommand.Parameters.AddWithValue("entityId", seeded.ProductId);

        var countResult = await countCommand.ExecuteScalarAsync();
        return Convert.ToInt32(countResult, CultureInfo.InvariantCulture);
    }

    private static async Task<DurableStateSnapshot> SnapshotDurableStateAsync(
        ApplicationDbContext context,
        SeededScenario seeded)
    {
        var variants = await context.ProductVariants
            .Where(variant =>
                variant.TenantId == seeded.TenantId &&
                variant.ProductId == seeded.ProductId &&
                variant.SKU == ContestedSku)
            .Select(variant => variant.Id)
            .ToListAsync();
        var stocks = await context.ProductVariantStocks
            .Where(stock =>
                stock.TenantId == seeded.TenantId &&
                stock.Variant.ProductId == seeded.ProductId &&
                stock.Variant.SKU == ContestedSku)
            .Select(stock => stock.Id)
            .ToListAsync();
        var stockSyncIds = await context.StockSyncOutboxMessages
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.ProductId == seeded.ProductId &&
                message.VariantId != seeded.DefaultVariantId)
            .Select(message => message.Id)
            .ToListAsync();
        var idempotencyIds = await context.ProductRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .Where(record =>
                record.TenantId == seeded.TenantId &&
                record.ProductId == seeded.ProductId &&
                record.Operation == "CREATE_VARIANT")
            .Select(record => record.Id)
            .ToListAsync();

        return new DurableStateSnapshot(
            variants.OrderBy(id => id).ToArray(),
            stocks.OrderBy(id => id).ToArray(),
            stockSyncIds.OrderBy(id => id).ToArray(),
            idempotencyIds.OrderBy(id => id).ToArray());
    }

    private static async Task<DurableStateSnapshot> SnapshotVariantCombinationDurableStateAsync(
        ApplicationDbContext context,
        SeededScenario seeded)
    {
        var variants = await context.ProductVariants
            .Where(variant =>
                variant.TenantId == seeded.TenantId &&
                variant.ProductId == seeded.ProductId &&
                variant.AttributeCombinationKey == VarCon01AttributeCombinationKey)
            .Select(variant => variant.Id)
            .ToListAsync();
        var stocks = await context.ProductVariantStocks
            .Where(stock =>
                stock.TenantId == seeded.TenantId &&
                stock.Variant.ProductId == seeded.ProductId &&
                stock.Variant.AttributeCombinationKey == VarCon01AttributeCombinationKey)
            .Select(stock => stock.Id)
            .ToListAsync();
        var stockSyncIds = await context.StockSyncOutboxMessages
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.ProductId == seeded.ProductId &&
                variants.Contains(message.VariantId))
            .Select(message => message.Id)
            .ToListAsync();
        var idempotencyIds = await context.ProductRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .Where(record =>
                record.TenantId == seeded.TenantId &&
                record.ProductId == seeded.ProductId &&
                record.Operation == "CREATE_VARIANT" &&
                record.ProductVariantId.HasValue &&
                variants.Contains(record.ProductVariantId.Value))
            .Select(record => record.Id)
            .ToListAsync();

        return new DurableStateSnapshot(
            variants.OrderBy(id => id).ToArray(),
            stocks.OrderBy(id => id).ToArray(),
            stockSyncIds.OrderBy(id => id).ToArray(),
            idempotencyIds.OrderBy(id => id).ToArray());
    }

    private sealed record SeededScenario(
        Guid TenantId,
        Guid ProductId,
        Guid DefaultVariantId,
        Guid MediaId,
        Guid WarehouseId,
        long ProductProjectionMutationVersion,
        long DefaultVariantProjectionMutationVersion);

    private sealed record VariantActor(
        string Name,
        string RequestId,
        string TraceIdentifier,
        Guid UserId,
        string? RequestedSku = null,
        string? RequestedBarcode = null);

    private sealed record VariantActorResult(
        VariantActor Actor,
        bool Succeeded,
        int? StatusCode,
        ProductVariantDto? Response,
        string? PayloadJson,
        TimeSpan Duration,
        Exception? Exception);

    private sealed record VariantDeleteActorResult(
        VariantActor Actor,
        bool Succeeded,
        int? StatusCode,
        string? PayloadJson,
        TimeSpan Duration,
        Exception? Exception);

    private sealed record VariantInventoryMutationResult(
        bool Succeeded,
        Exception? Exception);

    private sealed record DurableStateSnapshot(
        Guid[] VariantIds,
        Guid[] StockIds,
        Guid[] StockSyncOutboxIds,
        Guid[] IdempotencyIds);

    private sealed class VariantInsertRendezvous
    {
        private readonly int _participantCount;
        private readonly TimeSpan _timeout;
        private readonly ConcurrentQueue<string> _progressEntries;
        private readonly TaskCompletionSource<bool> _allArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivalCount;

        public VariantInsertRendezvous(
            int participantCount,
            TimeSpan timeout,
            ConcurrentQueue<string> progressEntries)
        {
            _participantCount = participantCount;
            _timeout = timeout;
            _progressEntries = progressEntries;
        }

        public int ArrivalCount => Volatile.Read(ref _arrivalCount);

        public async Task WaitAsync(string actorName, CancellationToken cancellationToken)
        {
            var arrival = Interlocked.Increment(ref _arrivalCount);
            _progressEntries.Enqueue($"{actorName}:variant-insert-arrived:{arrival}");

            if (arrival == _participantCount)
            {
                _allArrived.TrySetResult(true);
            }

            try
            {
                await _allArrived.Task.WaitAsync(_timeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                throw new XunitException(
                    $"Timed out waiting for both PROD-CON-01 actors to reach the contested variant insert. Progress: {string.Join(" | ", _progressEntries)}");
            }

            _progressEntries.Enqueue($"{actorName}:variant-insert-released");
        }
    }

    private sealed class VariantCommitOverlapWindow
    {
        private readonly TimeSpan _timeout;
        private readonly ConcurrentQueue<string> _progressEntries;
        private readonly TaskCompletionSource<bool> _originalCommitPaused =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseOriginalCommit =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _commitPauseClaimed;

        public VariantCommitOverlapWindow(
            TimeSpan timeout,
            ConcurrentQueue<string> progressEntries)
        {
            _timeout = timeout;
            _progressEntries = progressEntries;
        }

        public bool OriginalCommitPaused => _originalCommitPaused.Task.IsCompletedSuccessfully;

        public bool TryPauseOriginalCommit(string actorName)
        {
            if (Interlocked.CompareExchange(ref _commitPauseClaimed, 1, 0) != 0)
            {
                return false;
            }

            _progressEntries.Enqueue($"{actorName}:variant-commit-paused-before-durable-visibility");
            _originalCommitPaused.TrySetResult(true);
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
                    $"Timed out waiting for the authoritative product-variant actor to pause before commit. Progress: {string.Join(" | ", _progressEntries)}");
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
                    $"Timed out waiting to release the authoritative product-variant commit. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public void ReleaseOriginalCommit()
        {
            _progressEntries.Enqueue("orchestrator:release-original-product-variant-commit");
            _releaseOriginalCommit.TrySetResult(true);
        }
    }

    private sealed class ProductVariantActiveTransactionReplayWindow
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

        public ProductVariantActiveTransactionReplayWindow(
            TimeSpan timeout,
            ConcurrentQueue<string> progressEntries)
        {
            _timeout = timeout;
            _progressEntries = progressEntries;
        }

        public bool OriginalCommitPaused => _originalCommitPaused.Task.IsCompletedSuccessfully;

        public bool ReplayClaimAttempted => _replayClaimAttempted.Task.IsCompletedSuccessfully;

        public bool OriginalWasPausedBeforeReplayClaim => Volatile.Read(ref _originalPausedBeforeReplayClaim) == 1;

        public void MarkOriginalCommitPaused(string actorName)
        {
            _progressEntries.Enqueue($"{actorName}:commit-paused-before-durable-visibility");
            _originalCommitPaused.TrySetResult(true);
        }

        public void MarkReplayClaimAttempted(string actorName)
        {
            if (OriginalCommitPaused)
            {
                Interlocked.Exchange(ref _originalPausedBeforeReplayClaim, 1);
            }

            _progressEntries.Enqueue($"{actorName}:same-key-product-claim-attempted-while-original-active");
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
                    $"Timed out waiting for Request A to pause before product variant commit. Progress: {string.Join(" | ", _progressEntries)}");
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
                    $"Timed out waiting for Request B to attempt the overlapping product idempotency claim. Progress: {string.Join(" | ", _progressEntries)}");
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
                    $"Timed out waiting to release Request A product commit. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public void ReleaseOriginalCommit()
        {
            _progressEntries.Enqueue("orchestrator:release-original-product-commit");
            _releaseOriginalCommit.TrySetResult(true);
        }
    }

    private sealed class SkuReassignmentOverlapWindow
    {
        private readonly TimeSpan _timeout;
        private readonly ConcurrentQueue<string> _progressEntries;
        private readonly TaskCompletionSource<bool> _originalCommitPaused =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _conflictingUpdateAttempted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseOriginalCommit =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _originalPausedBeforeConflictingUpdateAttempt;

        public SkuReassignmentOverlapWindow(
            TimeSpan timeout,
            ConcurrentQueue<string> progressEntries)
        {
            _timeout = timeout;
            _progressEntries = progressEntries;
        }

        public bool OriginalCommitPaused => _originalCommitPaused.Task.IsCompletedSuccessfully;

        public bool ConflictingUpdateAttempted => _conflictingUpdateAttempted.Task.IsCompletedSuccessfully;

        public bool OriginalPauseObservedBeforeConflictingUpdateAttempt =>
            Volatile.Read(ref _originalPausedBeforeConflictingUpdateAttempt) == 1;

        public bool TryPauseOriginalCommit(string actorName)
        {
            if (!_originalCommitPaused.TrySetResult(true))
            {
                return false;
            }

            _progressEntries.Enqueue($"{actorName}:sku-reassignment-commit-paused-before-sql-visibility");
            return true;
        }

        public void MarkConflictingUpdateAttempt(string actorName)
        {
            if (_originalCommitPaused.Task.IsCompletedSuccessfully)
            {
                Interlocked.Exchange(ref _originalPausedBeforeConflictingUpdateAttempt, 1);
            }

            _progressEntries.Enqueue($"{actorName}:conflicting-sku-reassignment-update-attempted");
            _conflictingUpdateAttempted.TrySetResult(true);
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
                    $"Timed out waiting for Request A to pause before VAR-CON-04 SKU reassignment commit. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public async Task WaitForConflictingUpdateAttemptAsync()
        {
            try
            {
                await _conflictingUpdateAttempted.Task.WaitAsync(_timeout);
            }
            catch (TimeoutException)
            {
                throw new XunitException(
                    $"Timed out waiting for Request B to attempt the overlapping VAR-CON-04 SKU reassignment. Progress: {string.Join(" | ", _progressEntries)}");
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
                    $"Timed out waiting to release Request A VAR-CON-04 SKU reassignment commit. Progress: {string.Join(" | ", _progressEntries)}");
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
                    $"Timed out waiting for Request A to pause before Request B attempted its conflicting SKU write. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public void ReleaseOriginalCommit()
        {
            _progressEntries.Enqueue("orchestrator:release-var-con-04-winning-sku-commit");
            _releaseOriginalCommit.TrySetResult(true);
        }
    }

    private sealed class ProductVariantInsertBarrierInterceptor : DbCommandInterceptor
    {
        private readonly string _actorName;
        private readonly VariantInsertRendezvous _insertRendezvous;
        private readonly ConcurrentQueue<string> _progressEntries;
        private int _intercepted;

        public ProductVariantInsertBarrierInterceptor(
            string actorName,
            VariantInsertRendezvous insertRendezvous,
            ConcurrentQueue<string> progressEntries)
        {
            _actorName = actorName;
            _insertRendezvous = insertRendezvous;
            _progressEntries = progressEntries;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await WaitAtInsertBarrierIfNeededAsync(command.CommandText, cancellationToken);

            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            await WaitAtInsertBarrierIfNeededAsync(command.CommandText, cancellationToken);

            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private async Task WaitAtInsertBarrierIfNeededAsync(
            string commandText,
            CancellationToken cancellationToken)
        {
            if (IsContestedVariantInsert(commandText) &&
                Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0)
            {
                _progressEntries.Enqueue($"{_actorName}:variant-insert-detected");
                await _insertRendezvous.WaitAsync(_actorName, cancellationToken);
            }
        }

        private static bool IsContestedVariantInsert(string commandText)
        {
            return commandText.Contains("INSERT INTO \"ProductVariants\"", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class ProductVariantCommitPauseTransactionInterceptor(
        string actorName,
        VariantCommitOverlapWindow overlapWindow) : DbTransactionInterceptor
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
                // PROD-CON-01 critical window: variant row, attributes, stock ownership,
                // and media reference are staged inside the transaction, but PostgreSQL has
                // not yet made that SKU identity visible to other transactions.
                await overlapWindow.WaitUntilCommitReleasedAsync(cancellationToken);
            }

            return await base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }

    private sealed class ProductVariantActiveReplayCommitPauseTransactionInterceptor(
        string actorName,
        ProductVariantActiveTransactionReplayWindow replayWindow) : DbTransactionInterceptor
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
                // PROD-CON-02 critical window: Request A has already staged the authoritative
                // variant row, barcode owner, attributes, stock ownership, stock-sync outbox,
                // media reference, and completed idempotency response inside one transaction,
                // but PostgreSQL has not committed any of it yet.
                replayWindow.MarkOriginalCommitPaused(actorName);
                await replayWindow.WaitUntilCommitReleasedAsync(cancellationToken);
            }

            return await base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }

    private sealed class ProductVariantReplayClaimAttemptInterceptor(
        string actorName,
        ProductVariantActiveTransactionReplayWindow replayWindow) : DbCommandInterceptor
    {
        private int _intercepted;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (IsProductIdempotencyClaimInsert(command.CommandText) &&
                Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0)
            {
                replayWindow.MarkReplayClaimAttempted(actorName);
            }

            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private static bool IsProductIdempotencyClaimInsert(string commandText)
        {
            return commandText.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase) &&
                   commandText.Contains("ProductRequestIdempotency", StringComparison.OrdinalIgnoreCase) &&
                   commandText.Contains("ON CONFLICT", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class SkuReassignmentCommitPauseTransactionInterceptor(
        string actorName,
        SkuReassignmentOverlapWindow overlapWindow) : DbTransactionInterceptor
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
                // VAR-CON-04 critical window: Request A has staged a new SKU owner,
                // idempotency completion, and projection outbox intent, but SQL has
                // not committed the reassigned SKU owner to fresh readers yet.
                await overlapWindow.WaitUntilCommitReleasedAsync(cancellationToken);
            }

            return await base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }

    private sealed class SkuReassignmentUpdateAttemptInterceptor : DbCommandInterceptor
    {
        private readonly string _actorName;
        private readonly SkuReassignmentOverlapWindow _overlapWindow;
        private readonly ConcurrentQueue<string> _progressEntries;
        private int _intercepted;

        public SkuReassignmentUpdateAttemptInterceptor(
            string actorName,
            SkuReassignmentOverlapWindow overlapWindow,
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
                _progressEntries.Enqueue($"{_actorName}:sku-reassignment-update-detected");
                await _overlapWindow.WaitUntilOriginalPauseFromInterceptorAsync(cancellationToken);
                _overlapWindow.MarkConflictingUpdateAttempt(_actorName);
            }
        }

        private static bool IsVariantUpdate(string commandText)
        {
            return commandText.Contains("UPDATE \"ProductVariants\"", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class VariantDeleteInventoryOverlapWindow
    {
        private readonly TimeSpan _timeout;
        private readonly ConcurrentQueue<string> _progressEntries;
        private readonly TaskCompletionSource<bool> _inventoryMutationPaused =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _deleteCommitPaused =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseDeleteCommit =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseInventoryMutation =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _inventoryPausedBeforeDeleteCommit;

        public VariantDeleteInventoryOverlapWindow(
            TimeSpan timeout,
            ConcurrentQueue<string> progressEntries)
        {
            _timeout = timeout;
            _progressEntries = progressEntries;
        }

        public bool InventoryPausedBeforeDeleteCommit =>
            Volatile.Read(ref _inventoryPausedBeforeDeleteCommit) == 1;

        public void MarkInventoryMutationPaused()
        {
            _progressEntries.Enqueue("var-con-05-inventory:paused-after-uncommitted-stock-and-movement");
            _inventoryMutationPaused.TrySetResult(true);
        }

        public void MarkDeleteCommitPaused()
        {
            if (_inventoryMutationPaused.Task.IsCompletedSuccessfully)
            {
                Interlocked.Exchange(ref _inventoryPausedBeforeDeleteCommit, 1);
            }

            _progressEntries.Enqueue("var-con-05-delete:commit-paused-before-durable-visibility");
            _deleteCommitPaused.TrySetResult(true);
        }

        public async Task WaitForInventoryMutationPauseAsync()
        {
            try
            {
                await _inventoryMutationPaused.Task.WaitAsync(_timeout);
            }
            catch (TimeoutException)
            {
                throw new XunitException(
                    $"Timed out waiting for VAR-CON-05 inventory mutation pause. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public async Task WaitForDeleteCommitPauseAsync()
        {
            try
            {
                await _deleteCommitPaused.Task.WaitAsync(_timeout);
            }
            catch (TimeoutException)
            {
                throw new XunitException(
                    $"Timed out waiting for VAR-CON-05 delete commit pause. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public async Task WaitUntilDeleteCommitReleasedAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _releaseDeleteCommit.Task.WaitAsync(_timeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                throw new XunitException(
                    $"Timed out waiting to release VAR-CON-05 delete commit. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public async Task WaitUntilInventoryMutationReleasedAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _releaseInventoryMutation.Task.WaitAsync(_timeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                throw new XunitException(
                    $"Timed out waiting to release VAR-CON-05 inventory mutation. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public void ReleaseDeleteCommit()
        {
            _progressEntries.Enqueue("orchestrator:release-var-con-05-delete-commit");
            _releaseDeleteCommit.TrySetResult(true);
        }

        public void ReleaseInventoryMutation()
        {
            _progressEntries.Enqueue("orchestrator:release-var-con-05-inventory-mutation");
            _releaseInventoryMutation.TrySetResult(true);
        }
    }

    private sealed class VariantDeleteCommitPauseTransactionInterceptor(
        VariantDeleteInventoryOverlapWindow overlapWindow) : DbTransactionInterceptor
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
                overlapWindow.MarkDeleteCommitPaused();
                await overlapWindow.WaitUntilDeleteCommitReleasedAsync(cancellationToken);
            }

            return await base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }

    private sealed class ProductSyncActiveProcessingOverlapWindow
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

        public ProductSyncActiveProcessingOverlapWindow(
            TimeSpan timeout,
            ConcurrentQueue<string> progressEntries)
        {
            _timeout = timeout;
            _progressEntries = progressEntries;
        }

        public bool OriginalPauseObservedBeforeDuplicateClaimAttempt => Volatile.Read(ref _originalPausedBeforeDuplicateAttempt) == 1;

        public bool TryPauseOriginalWorker(string workerName)
        {
            if (!_originalPause.TrySetResult(true))
            {
                return false;
            }

            _progressEntries.Enqueue($"{workerName}:product-sync-commit-paused-before-sql-visibility");
            return true;
        }

        public void MarkDuplicateClaimAttempt(string workerName)
        {
            if (_originalPause.Task.IsCompletedSuccessfully)
            {
                Interlocked.Exchange(ref _originalPausedBeforeDuplicateAttempt, 1);
            }

            _progressEntries.Enqueue($"{workerName}:duplicate-product-sync-claim-attempted");
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
                    $"Timed out waiting for the original product-sync worker to pause before commit. Progress: {string.Join(" | ", _progressEntries)}");
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
                    $"Timed out waiting for the duplicate product-sync worker to attempt the overlapping processed-event claim. Progress: {string.Join(" | ", _progressEntries)}");
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
                    $"Timed out waiting to release the original product-sync worker. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public void ReleaseOriginalWorker()
        {
            _progressEntries.Enqueue("orchestrator:release-original-product-sync-worker");
            _releaseOriginal.TrySetResult(true);
        }
    }

    private sealed class ProductSyncCommitPauseTransactionInterceptor(
        string workerName,
        ProductSyncActiveProcessingOverlapWindow overlapWindow) : DbTransactionInterceptor
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
                // PROD-CON-03 critical window:
                // the product-sync worker has already staged the marketplace projection outbox,
                // but PostgreSQL has not yet made the SQL handoff visible.
                await overlapWindow.WaitUntilReleaseAsync(cancellationToken);
            }

            return await base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }

    private sealed class ProductSyncProjectionOutboxInsertAttemptInterceptor(
        string workerName,
        ProductSyncActiveProcessingOverlapWindow overlapWindow) : DbCommandInterceptor
    {
        private int _intercepted;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (IsProcessedEventInsert(command.CommandText) &&
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
            if (IsProcessedEventInsert(command.CommandText) &&
                Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0)
            {
                overlapWindow.MarkDuplicateClaimAttempt(workerName);
            }

            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private static bool IsProcessedEventInsert(string commandText)
        {
            return commandText.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase) &&
                   commandText.Contains("MarketplaceProjectionOutboxMessages", StringComparison.OrdinalIgnoreCase);
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

    private static Mock<IConnectionMultiplexer> CreateRedisMultiplexerMock()
    {
        var database = new Mock<IDatabase>(MockBehavior.Loose);
        database
            .Setup(entry => entry.KeyDeleteAsync(
                It.IsAny<RedisKey[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(0L);

        var multiplexer = new Mock<IConnectionMultiplexer>(MockBehavior.Loose);
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
                    $"VAR-CON-02 could not resolve an authoritative aggregate id for {@event.GetType().Name}.");
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
                EntityState.Modified or EntityState.Deleted => entry.Entity.ProjectionMutationVersion + 1,
                _ => entry.Entity.ProjectionMutationVersion
            };
        }
    }

    private sealed record VarCon02Scenario(
        Guid TenantId,
        Guid ProductId,
        Guid VariantId,
        Guid DefaultVariantId,
        Guid WarehouseId,
        string BaselineCombinationKey,
        string MutatedCombinationKey);

    private sealed record VarCon02DurableSnapshot(
        string VariantIds,
        string StockIds,
        string IdempotencyIds,
        string OutboxIds);

    private sealed record VarCon04Scenario(
        Guid TenantId,
        Guid ProductId,
        Guid VariantAId,
        Guid VariantBId,
        Guid WarehouseId,
        string VariantACombinationKey,
        string VariantBCombinationKey)
    {
        public VarCon02Scenario ToVisibilityScenario() =>
            new(
                TenantId,
                ProductId,
                VariantAId,
                VariantBId,
                WarehouseId,
                VariantACombinationKey,
                VariantACombinationKey);
    }

    private sealed record VarCon04DurableSnapshot(
        string VariantState,
        string StockState,
        string IdempotencyState,
        string OutboxState,
        string OrderItemState,
        string SalesInvoiceItemState,
        string StockMovementState);

    private sealed record VarCon06ExpectedState(
        string Sku,
        string? Barcode,
        decimal Price,
        decimal OnlinePrice,
        decimal CostPrice,
        int EventVersion,
        string AttributeCombinationKey,
        string AttributeFingerprint);

    private sealed record VarCon06ProjectionSideEffects(
        int CacheKeyDeleteCount,
        int SearchIndexCount,
        int SearchDeleteCount);

    private sealed record VarCon09BMediaConsumerResult(
        int SearchIndexCount,
        IReadOnlyList<ProductSearchDocument> IndexedDocuments,
        int CacheInvalidationCount);

    private sealed record VarCon06ProjectionDeliveryResult(
        VariantProjectionWorkerResult WorkerResult,
        VarCon06ProjectionSideEffects SideEffects);

    private sealed record VarCon08ProjectionWorkerResult(
        VariantProjectionWorkerResult WorkerResult,
        VarCon06ProjectionSideEffects SideEffects);

    private sealed record VarCon08VisibilitySnapshot(
        VarCon03VisibilitySnapshot VariantVisibility,
        string SearchDocumentId,
        Guid? SearchLastEventId,
        bool SearchHasVariants,
        bool SearchInStock,
        int SearchStockQuantity,
        Guid? StorefrontMediaId,
        string? StorefrontImageUrl,
        string? CommerceHubImage);

    private sealed record VarCon09MediaVisibilitySnapshot(
        long SqlVariantCount,
        long SqlV11MediaCount,
        long SqlV10MediaCount,
        long SqlOrphanVariantMediaLinks,
        Guid? SqlVariantMediaId,
        int StorefrontVariantCount,
        Guid? StorefrontVariantMediaId,
        string? StorefrontVariantImageUrl,
        string StorefrontMediaFingerprint,
        Guid? StorefrontLastMediaEventId,
        int StorefrontLastMediaEventVersion,
        string? AffiliateThumbnailUrl,
        string? CommerceHubImage,
        string? SearchImageUrl,
        Guid? SearchLastEventId,
        bool SearchHasVariants,
        bool SearchInStock);

    private sealed record VarCon07VisibilitySnapshot(
        VarCon03VisibilitySnapshot VariantVisibility,
        decimal MarketplacePrice,
        bool MarketplaceAvailable,
        Guid? MarketplaceLastEventId,
        int MarketplaceLastEventVersion,
        long MarketplaceLastEntityMutationVersion,
        string MarketplaceImagesFingerprint,
        bool SearchHasVariants,
        bool SearchInStock,
        int SearchStockQuantity);

    private sealed record ProdCon03CanonicalState(
        long ProductProjectionMutationVersion,
        long VariantProjectionMutationVersion,
        long StockProjectionMutationVersion,
        string ProductName,
        string? ProductSlug,
        decimal ProductSalePrice,
        string VariantSku,
        string? VariantBarcode,
        decimal? VariantOnlinePrice,
        Guid? VariantMediaId,
        Guid StockId,
        decimal StockQuantity,
        Guid StockWarehouseId);

    private sealed record ProdCon03VisibilitySnapshot(
        long ProcessedEventCount,
        long ProjectionOutboxCount,
        long MarketplaceReadModelCount,
        long CommerceHubListingCount,
        long ProductMediaCount,
        long StockSyncOutboxCount,
        long ProductRequestIdempotencyCount,
        long MarketplaceOutboxCount,
        long ProductVariantCount,
        long ProductVariantStockCount);

    private sealed record ProductSyncOutboxDispatchResult(
        int ProcessedCount,
        MarketplaceEvents.ProductUpdatedEvent? PublishedEvent,
        Type? PublishedType);

    private sealed record ProductSyncWorker(
        string Name);

    private sealed record ProductSyncWorkerResult(
        string WorkerName,
        TimeSpan Duration,
        Exception? Exception);

    private sealed record VarCon03VisibilitySnapshot(
        long SqlVariantCount,
        long SqlStockCount,
        int StorefrontVariantCount,
        string StorefrontSku,
        string? StorefrontBarcode,
        decimal StorefrontEffectivePrice,
        decimal StorefrontTotalStock,
        int StorefrontLastEventVersion,
        string StorefrontAttributeFingerprint,
        bool AffiliateVisible,
        decimal AffiliateMaxVariantPrice,
        int AffiliateTotalAvailableStock,
        Guid CommerceHubVariantId,
        decimal CommerceHubPrice,
        decimal CommerceHubAvailableStock,
        bool CommerceHubPublished);

    private sealed record VariantProjectionWorker(
        string Name);

    private enum VariantProjectionWorkerOutcome
    {
        Processed,
        DuplicateSkipped,
        Failed
    }

    private sealed record VariantProjectionWorkerResult(
        string WorkerName,
        VariantProjectionWorkerOutcome Outcome,
        TimeSpan Duration,
        Exception? Exception);

    private sealed class VariantProjectionDeliveryOverlapWindow
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

        public VariantProjectionDeliveryOverlapWindow(
            TimeSpan timeout,
            ConcurrentQueue<string> progressEntries)
        {
            _timeout = timeout;
            _progressEntries = progressEntries;
        }

        public bool OriginalPauseObservedBeforeDuplicateClaimAttempt =>
            Volatile.Read(ref _originalPausedBeforeDuplicateAttempt) == 1;

        public void MarkOriginalPaused(string workerName)
        {
            _progressEntries.Enqueue($"{workerName}:variant-projection-claim-paused-before-visible-projection");
            _originalPause.TrySetResult(true);
        }

        public void MarkDuplicateClaimAttempt(string workerName)
        {
            if (_originalPause.Task.IsCompletedSuccessfully)
            {
                Interlocked.Exchange(ref _originalPausedBeforeDuplicateAttempt, 1);
            }

            _progressEntries.Enqueue($"{workerName}:duplicate-variant-projection-claim-attempted");
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
                    $"Timed out waiting for the original variant projection worker to pause. Progress: {string.Join(" | ", _progressEntries)}");
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
                    $"Timed out waiting for duplicate variant projection delivery. Progress: {string.Join(" | ", _progressEntries)}");
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
                    $"Timed out waiting to release the original variant projection worker. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public void ReleaseOriginalWorker()
        {
            _progressEntries.Enqueue("orchestrator:release-original-variant-projection-worker");
            _releaseOriginal.TrySetResult(true);
        }
    }

    private sealed class ProdCon03MongoDatabaseScope : IAsyncDisposable
    {
        private readonly MongoClient _client;
        private readonly string _databaseName;

        public ProdCon03MongoDatabaseScope(
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

    private static string BuildProductMutationIdempotencyKey(Guid tenantId, string requestId) =>
        $"PRODUCT_VARIANT:{tenantId}:{requestId}";
}
