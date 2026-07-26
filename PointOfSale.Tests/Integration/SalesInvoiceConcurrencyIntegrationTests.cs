using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MongoDB.Driver;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Audit;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Events;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Consumers;
using PointOfSale.Infrastructure.Consumers.Affiliate;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Configuration;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.Eventing;
using PointOfSale.Infrastructure.MongoDB.Configuration;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Treasury;
using PointOfSale.Tests.Infrastructure;
using StackExchange.Redis;
using Xunit.Sdk;
using UnitEntity = PointOfSale.Core.Entities.Unit;

namespace PointOfSale.Tests.Integration;

public sealed class SalesInvoiceConcurrencyIntegrationTests
{
    private static readonly DateTime SeededUtc = new(2026, 5, 13, 12, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions CaseInsensitiveJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Guid TenantId = Guid.Parse("7f0f08b8-3e8a-4d42-91f4-8435d6d4b001");
    private static readonly Guid CategoryId = Guid.Parse("7f0f08b8-3e8a-4d42-91f4-8435d6d4b002");
    private static readonly Guid CustomerId = Guid.Parse("7f0f08b8-3e8a-4d42-91f4-8435d6d4b003");
    private static readonly Guid ProductId = Guid.Parse("7f0f08b8-3e8a-4d42-91f4-8435d6d4b004");
    private static readonly Guid VariantId = Guid.Parse("7f0f08b8-3e8a-4d42-91f4-8435d6d4b005");
    private static readonly Guid UnitId = Guid.Parse("7f0f08b8-3e8a-4d42-91f4-8435d6d4b006");
    private static readonly Guid WarehouseId = Guid.Parse("7f0f08b8-3e8a-4d42-91f4-8435d6d4b007");
    private static readonly Guid ProductUnitId = Guid.Parse("7f0f08b8-3e8a-4d42-91f4-8435d6d4b008");
    private static readonly Guid VariantStockId = Guid.Parse("7f0f08b8-3e8a-4d42-91f4-8435d6d4b009");
    private static readonly Guid ActorAUserId = Guid.Parse("7f0f08b8-3e8a-4d42-91f4-8435d6d4b00a");
    private static readonly Guid ActorBUserId = Guid.Parse("7f0f08b8-3e8a-4d42-91f4-8435d6d4b00b");

    private const decimal InitialStockQuantity = 3m;
    private const decimal ContestedSaleQuantity = 2m;
    private const decimal StockCon01InitialStockQuantity = 1m;
    private const decimal StockCon01SaleQuantity = 1m;
    private const decimal AuthoritativeUnitPrice = 10m;
    private const decimal ProductCostPrice = 4m;
    private const decimal PriceCon11InitialCostPrice = 50m;
    private const decimal PriceCon11MutatedCostPrice = 80m;
    private const decimal PriceCon11SaleUnitPrice = 100m;
    private const decimal PriceCon11ExpectedCostAmount = PriceCon11InitialCostPrice * ContestedSaleQuantity;
    private const decimal PriceCon11ExpectedSaleAmount = PriceCon11SaleUnitPrice * ContestedSaleQuantity;
    private const decimal PriceCon12ReturnQuantity = 1m;
    private const decimal PriceCon12ExpectedReturnCostAmount = PriceCon11InitialCostPrice * PriceCon12ReturnQuantity;
    private const int ScenarioRepetitions = 5;
    private const string SalesInvoiceSequenceName = "PosSalesInvoiceNumberSeq";
    private static readonly Guid PriceCon12RecalculationEventId = Guid.Parse("7f0f08b8-3e8a-4d42-91f4-8435d6d4b012");
    private static readonly Guid PriceCon13ProjectionEventId = Guid.Parse("7f0f08b8-3e8a-4d42-91f4-8435d6d4b013");
    private static readonly Guid PriceCon13StaleProjectionEventId = Guid.Parse("7f0f08b8-3e8a-4d42-91f4-8435d6d4b014");
    private static readonly Guid StockCon05CommerceCustomerId = Guid.Parse("7f0f08b8-3e8a-4d42-91f4-8435d6d4b015");
    private static readonly Guid StockCon05MarketplaceOrderId = Guid.Parse("7f0f08b8-3e8a-4d42-91f4-8435d6d4b016");
    private static readonly Guid StockCon05SubOrderId = Guid.Parse("7f0f08b8-3e8a-4d42-91f4-8435d6d4b017");
    private static readonly Guid StockCon05OrderItemId = Guid.Parse("7f0f08b8-3e8a-4d42-91f4-8435d6d4b018");
    private static readonly Guid StockCon05RefundRequestId = Guid.Parse("7f0f08b8-3e8a-4d42-91f4-8435d6d4b019");
    private static readonly Guid StockCon05ReturnRequestId = Guid.Parse("7f0f08b8-3e8a-4d42-91f4-8435d6d4b01a");
    private static readonly DateTime PriceCon13ProjectionDateUtc = new(2026, 5, 13, 0, 0, 0, DateTimeKind.Utc);
    private const string PriceCon13ProjectionConsumerName = "PRICE-CON-13-FinancialProjection";
    private const string PriceCon13ProjectionEventType = "FinancialProjectionDelivered";
    private static readonly string[] VariantStockProjectionFenceConsumerNames =
    [
        nameof(ProductVariantProjectionConsumer) + ":" + nameof(ProductVariantStockChangedEvent),
        nameof(ProductStockChangedConsumer) + ":" + nameof(ProductVariantStockChangedEvent),
        nameof(ProductStockPriceSearchIndexConsumer) + ":" + nameof(ProductVariantStockChangedEvent),
        nameof(AffiliateProductIntelligenceProjectionConsumer) + ":" + nameof(ProductVariantStockChangedEvent),
        nameof(AffiliateProductPolicyProjectionConsumer) + ":" + nameof(ProductVariantStockChangedEvent),
        nameof(CommerceHubProjectionConsumer) + ":" + nameof(ProductVariantStockChangedEvent)
    ];

    [Fact]
    public async Task SI_CON_01_Concurrent_Sales_On_Contested_Stock_Converge_Safely_And_Are_Replay_Safe()
    {
        for (var iteration = 0; iteration < ScenarioRepetitions; iteration++)
        {
            await ExecuteScenarioIterationAsync(iteration);
        }
    }

    [Fact]
    public async Task SI_CON_02_Replay_During_Active_Transaction_Converges_Safely()
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
        var replayWindow = new ActiveTransactionReplayWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var sharedRequestId = "si-con-02-active-replay";
        var originalActor = new SaleActor(
            Name: "request-a-original",
            RequestId: sharedRequestId,
            TraceIdentifier: "si-con-02-original-active",
            UserId: ActorAUserId);
        var replayActor = new SaleActor(
            Name: "request-b-replay",
            RequestId: sharedRequestId,
            TraceIdentifier: "si-con-02-overlapping-replay",
            UserId: ActorBUserId);

        var originalTask = ExecuteSaleActorAsync(
            connectionString,
            tenantContext,
            seeded,
            originalActor,
            progressEntries,
            new CommitPauseTransactionInterceptor(originalActor.Name, replayWindow));

        await replayWindow.WaitForOriginalCommitPauseAsync();

        // A fresh DbContext must not see Request A's invoice, stock decrement, outbox row,
        // customer effects, or completed idempotency row while Request A is paused before commit.
        await AssertNoUncommittedDurableStateVisibleAsync(scope, tenantContext, seeded, progressEntries);

        var replayTask = ExecuteSaleActorAsync(
            connectionString,
            tenantContext,
            seeded,
            replayActor,
            progressEntries,
            new ReplayClaimAttemptInterceptor(replayActor.Name, replayWindow));

        await replayWindow.WaitForReplayClaimAttemptAsync();
        Assert.True(
            replayWindow.OriginalWasPausedBeforeReplayClaim,
            $"Replay did not overlap the original active transaction window. Progress: {string.Join(" | ", progressEntries)}");

        replayWindow.ReleaseOriginalCommit();

        SaleActorResult[] results;
        try
        {
            results = await Task.WhenAll(originalTask, replayTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"SI-CON-02 timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.True(
            replayWindow.OriginalCommitPaused,
            $"Request A never paused before commit. Progress: {string.Join(" | ", progressEntries)}");
        Assert.True(
            replayWindow.ReplayClaimAttempted,
            $"Request B never attempted the overlapping idempotency claim. Progress: {string.Join(" | ", progressEntries)}");
        Assert.All(results, result => Assert.Null(result.Exception));

        await AssertActiveReplayConvergenceAsync(
            scope,
            tenantContext,
            seeded,
            originalActor,
            replayActor,
            results,
            progressEntries);
    }

    [Fact]
    public async Task SI_CON_03_Duplicate_Worker_Delivery_During_Active_Processing_Converges_Safely()
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

        var canonicalActor = new SaleActor(
            Name: "si-con-03-authoritative-create",
            RequestId: "si-con-03-authoritative-create",
            TraceIdentifier: "si-con-03-authoritative-create",
            UserId: ActorAUserId);

        var createResult = await ExecuteSaleActorAsync(
            connectionString,
            tenantContext,
            seeded,
            canonicalActor,
            progressEntries);

        Assert.Null(createResult.Exception);
        Assert.True(createResult.Succeeded, createResult.PayloadJson ?? "Canonical SI-CON-03 sale did not succeed.");

        var invoiceId = createResult.Response!.InvoiceId;
        var accountingBaseline = await LoadSiCon03StateSnapshotAsync(scope, tenantContext, seeded, invoiceId);

        Assert.Equal(1, accountingBaseline.SalesInvoices);
        Assert.Equal(1m, accountingBaseline.StockQuantity);
        Assert.Equal(1, accountingBaseline.StockMovements);
        Assert.Equal(2, accountingBaseline.CustomerTransactions);
        Assert.Equal(2, accountingBaseline.AccountLedgers);
        Assert.Equal(0m, accountingBaseline.CustomerBalance);
        Assert.Equal(1, accountingBaseline.AccountingOutboxMessages);
        Assert.NotEqual(Guid.Empty, accountingBaseline.AccountingOutboxId);
        Assert.NotNull(accountingBaseline.AccountingOutboxJournalId);
        Assert.Null(accountingBaseline.AccountingOutboxClaimedUntilUtc);
        Assert.NotNull(accountingBaseline.AccountingOutboxProcessedAtUtc);
        Assert.Equal(1, accountingBaseline.StockSyncOutboxMessages);
        Assert.NotEqual(Guid.Empty, accountingBaseline.StockSyncOutboxId);
        Assert.NotEqual(Guid.Empty, accountingBaseline.StockSyncEventId);
        Assert.Null(accountingBaseline.StockSyncPublishedAtUtc);
        Assert.Equal(0, accountingBaseline.ProcessedEvents);
        Assert.Equal(0, accountingBaseline.InventoryEventInbox);

        // The create-sale path posts accounting synchronously, so the canonical outbox row is
        // already processed when the sale commits. To exercise real duplicate worker overlap
        // against the authoritative worker code, reopen that same persisted row as pending and
        // drive AccountingOutboxProcessor directly. This preserves the original payload,
        // idempotency key, aggregate version, invoice identity, and JournalId while avoiding
        // a fake second sale event.
        await ReopenAccountingOutboxMessageForWorkerReplayAsync(
            scope,
            tenantContext,
            seeded,
            invoiceId,
            progressEntries);

        var accountingWindow = new ActiveProcessingDuplicateWorkerWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var accountingWorkerA = new AccountingReplayWorker(
            Name: "accounting-worker-a",
            UserId: ActorAUserId);
        var accountingWorkerB = new AccountingReplayWorker(
            Name: "accounting-worker-b",
            UserId: ActorBUserId);

        using var accountingWorkerAServices = BuildAccountingOutboxProcessorServiceProvider(
            connectionString,
            tenantContext,
            new AccountingOutboxInvoiceLockPauseInterceptor(accountingWorkerA.Name, accountingWindow));
        using var accountingWorkerBServices = BuildAccountingOutboxProcessorServiceProvider(
            connectionString,
            tenantContext);
        using var accountingWorkerATelemetry = new PointOfSale.Infrastructure.Instrumentation.CommerceReliabilityTelemetry();
        using var accountingWorkerBTelemetry = new PointOfSale.Infrastructure.Instrumentation.CommerceReliabilityTelemetry();

        var accountingWorkerAProcessor = new AccountingOutboxProcessor(
            accountingWorkerAServices,
            NullLogger<AccountingOutboxProcessor>.Instance,
            Options.Create(new AccountingOutboxOptions { BatchSize = 1, ProcessingLeaseSeconds = 30 }),
            reliabilityTelemetry: accountingWorkerATelemetry);
        var accountingWorkerBProcessor = new AccountingOutboxProcessor(
            accountingWorkerBServices,
            NullLogger<AccountingOutboxProcessor>.Instance,
            Options.Create(new AccountingOutboxOptions { BatchSize = 1, ProcessingLeaseSeconds = 30 }),
            reliabilityTelemetry: accountingWorkerBTelemetry);

        var accountingOriginalTask = ExecuteAccountingOutboxWorkerAsync(
            accountingWorkerAProcessor,
            accountingWorkerA,
            progressEntries);

        await accountingWindow.WaitForOriginalPauseAsync();
        await AssertAccountingWorkerClaimVisibleDuringActiveProcessingAsync(
            scope,
            tenantContext,
            seeded,
            invoiceId,
            accountingBaseline,
            progressEntries);

        var accountingDuplicateTask = ExecuteAccountingOutboxWorkerAsync(
            accountingWorkerBProcessor,
            accountingWorkerB,
            progressEntries);

        AccountingOutboxWorkerResult duplicateAccountingWorkerResult;
        try
        {
            duplicateAccountingWorkerResult = await accountingDuplicateTask.WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"SI-CON-03 accounting duplicate worker did not complete while the original worker held the invoice lock. Progress: {string.Join(" | ", progressEntries)}");
        }

        accountingWindow.ReleaseOriginalWorker();

        AccountingOutboxWorkerResult originalAccountingWorkerResult;
        try
        {
            originalAccountingWorkerResult = await accountingOriginalTask.WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"SI-CON-03 accounting overlap timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        await AssertAccountingDuplicateWorkerConvergenceAsync(
            scope,
            tenantContext,
            seeded,
            invoiceId,
            accountingBaseline,
            originalAccountingWorkerResult,
            duplicateAccountingWorkerResult,
            progressEntries);
        var postAccountingBaseline = await LoadSiCon03StateSnapshotAsync(scope, tenantContext, seeded, invoiceId);

        var stockSyncPublishWindow = new BlockingPublishWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var publishEndpoint = BuildBlockingPublishEndpoint(stockSyncPublishWindow);
        using var stockSyncServices = BuildStockSyncProcessorServiceProvider(
            connectionString,
            tenantContext,
            publishEndpoint.Object);
        using var stockSyncTelemetry = new PointOfSale.Infrastructure.Instrumentation.CommerceReliabilityTelemetry();

        var stockSyncProcessor = new StockSyncOutboxProcessor(
            stockSyncServices,
            stockSyncTelemetry,
            NullLogger<StockSyncOutboxProcessor>.Instance,
            Options.Create(new StockSyncOutboxOptions()));

        var stockSyncWorkerATask = ExecuteStockSyncWorkerAsync(
            stockSyncProcessor,
            "stock-sync-worker-a",
            progressEntries);

        await stockSyncPublishWindow.WaitForFirstPublishAsync();
        await AssertStockSyncClaimVisibleDuringActivePublishAsync(
            scope,
            tenantContext,
            seeded,
            invoiceId,
            postAccountingBaseline,
            progressEntries);

        var stockSyncWorkerBTask = ExecuteStockSyncWorkerAsync(
            stockSyncProcessor,
            "stock-sync-worker-b",
            progressEntries);

        StockSyncWorkerResult duplicateWorkerResult;
        try
        {
            duplicateWorkerResult = await stockSyncWorkerBTask.WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"SI-CON-03 stock-sync duplicate worker did not complete while the original publish was paused. Progress: {string.Join(" | ", progressEntries)}");
        }

        stockSyncPublishWindow.ReleaseFirstPublish();

        StockSyncWorkerResult originalWorkerResult;
        try
        {
            originalWorkerResult = await stockSyncWorkerATask.WaitAsync(TimeSpan.FromSeconds(60));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"SI-CON-03 stock-sync original worker did not finish after publish release. Progress: {string.Join(" | ", progressEntries)}");
        }

        await AssertStockSyncDuplicateWorkerConvergenceAsync(
            scope,
            tenantContext,
            seeded,
            invoiceId,
            postAccountingBaseline,
            originalWorkerResult,
            duplicateWorkerResult,
            stockSyncPublishWindow,
            progressEntries);
    }

    [Fact]
    public async Task STOCK_CON_01_Concurrent_Last_Unit_Stock_Deduction_Converges_Safely()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var mongoScope = await CreateIsolatedMongoDatabaseScopeAsync("stock-con-01");
        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);
        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext, StockCon01InitialStockQuantity);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");
        var mongoConnectionString = GetRequiredMongoConnectionString();

        await SeedStockCon01ReadModelsAsync(mongoScope.Database, seeded);

        var progressEntries = new ConcurrentQueue<string>();
        var commitWindow = new ActiveTransactionReplayWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var stockLockAttempt = new StockLockRendezvous(
            participantCount: 1,
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);

        var actorA = new SaleActor(
            Name: "stock-con-01-actor-a",
            RequestId: "stock-con-01-actor-a",
            TraceIdentifier: "stock-con-01-a",
            UserId: ActorAUserId);
        var actorB = new SaleActor(
            Name: "stock-con-01-actor-b",
            RequestId: "stock-con-01-actor-b",
            TraceIdentifier: "stock-con-01-b",
            UserId: ActorBUserId);
        var request = BuildStockCon01CreateSaleRequest(seeded);

        var actorATask = ExecuteSaleActorAsync(
            connectionString,
            tenantContext,
            seeded,
            actorA,
            request,
            progressEntries,
            new CommitPauseTransactionInterceptor(actorA.Name, commitWindow));

        await commitWindow.WaitForOriginalCommitPauseAsync();

        var actorBTask = ExecuteSaleActorAsync(
            connectionString,
            tenantContext,
            seeded,
            actorB,
            request,
            progressEntries,
            new ProductVariantStockLockInterceptor(actorB.Name, stockLockAttempt, progressEntries));

        await WaitForStockLockArrivalAsync(stockLockAttempt, progressEntries);
        await AssertStockCon01NoPartialVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            progressEntries);

        commitWindow.ReleaseOriginalCommit();

        SaleActorResult[] results;
        try
        {
            results = await Task.WhenAll(actorATask, actorBTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"STOCK-CON-01 timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.Equal(1, stockLockAttempt.ArrivalCount);
        Assert.All(results, result => Assert.Null(result.Exception));

        var winner = await AssertStockCon01FinalAuthorityAsync(
            scope,
            tenantContext,
            seeded,
            actorA,
            actorB,
            request,
            results,
            progressEntries);

        var stockEvent = await LoadSingleStockCon01StockEventAsync(scope, tenantContext, seeded, winner.Response!.InvoiceId);
        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            stockEvent,
            progressEntries);
        await AssertStockCon01ProjectionConvergenceAsync(mongoScope.Database, seeded, expectedStock: 0m);

        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            stockEvent,
            progressEntries);
        await AssertStockCon01ProjectionConvergenceAsync(mongoScope.Database, seeded, expectedStock: 0m);

        var staleReplay = CloneStockEventForStaleReplay(stockEvent);
        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            staleReplay,
            progressEntries);
        await AssertStockCon01ProjectionConvergenceAsync(mongoScope.Database, seeded, expectedStock: 0m);

        progressEntries.Enqueue("verification:stock-con-01-last-unit-authority-converged");
    }

    [Fact]
    public async Task STOCK_CON_02_Replay_During_Active_Stock_Mutation_Converges_Safely()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var mongoScope = await CreateIsolatedMongoDatabaseScopeAsync("stock-con-02");
        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);
        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext, StockCon01InitialStockQuantity);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");
        var mongoConnectionString = GetRequiredMongoConnectionString();

        await SeedStockCon01ReadModelsAsync(mongoScope.Database, seeded);

        var progressEntries = new ConcurrentQueue<string>();
        var replayWindow = new ActiveTransactionReplayWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var sharedRequestId = "stock-con-02-active-stock-replay";
        var originalActor = new SaleActor(
            Name: "stock-con-02-request-a",
            RequestId: sharedRequestId,
            TraceIdentifier: "stock-con-02-original-active",
            UserId: ActorAUserId);
        var replayActor = new SaleActor(
            Name: "stock-con-02-request-b",
            RequestId: sharedRequestId,
            TraceIdentifier: "stock-con-02-overlapping-replay",
            UserId: ActorBUserId);
        var request = BuildStockCon02CreateSaleRequest(seeded);

        var originalTask = ExecuteSaleActorAsync(
            connectionString,
            tenantContext,
            seeded,
            originalActor,
            request,
            progressEntries,
            new CommitPauseTransactionInterceptor(originalActor.Name, replayWindow));

        await replayWindow.WaitForOriginalCommitPauseAsync();

        await AssertStockCon01NoPartialVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            progressEntries);

        var replayTask = ExecuteSaleActorAsync(
            connectionString,
            tenantContext,
            seeded,
            replayActor,
            request,
            progressEntries,
            new ReplayClaimAttemptInterceptor(replayActor.Name, replayWindow));

        await replayWindow.WaitForReplayClaimAttemptAsync();
        Assert.True(
            replayWindow.OriginalWasPausedBeforeReplayClaim,
            $"Replay did not overlap the active stock mutation window. Progress: {string.Join(" | ", progressEntries)}");

        replayWindow.ReleaseOriginalCommit();

        SaleActorResult[] results;
        try
        {
            results = await Task.WhenAll(originalTask, replayTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"STOCK-CON-02 timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.True(
            replayWindow.OriginalCommitPaused,
            $"Request A never paused before stock mutation commit. Progress: {string.Join(" | ", progressEntries)}");
        Assert.True(
            replayWindow.ReplayClaimAttempted,
            $"Request B never attempted the overlapping idempotency claim. Progress: {string.Join(" | ", progressEntries)}");
        Assert.All(results, result => Assert.Null(result.Exception));

        var winner = await AssertStockCon02FinalAuthorityAsync(
            scope,
            tenantContext,
            seeded,
            originalActor,
            replayActor,
            request,
            results,
            progressEntries);

        var stockEvent = await LoadSingleStockCon01StockEventAsync(scope, tenantContext, seeded, winner.Response!.InvoiceId);
        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            stockEvent,
            progressEntries);
        await AssertStockCon01ProjectionConvergenceAsync(mongoScope.Database, seeded, expectedStock: 0m);

        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            stockEvent,
            progressEntries);
        await AssertStockCon01ProjectionConvergenceAsync(mongoScope.Database, seeded, expectedStock: 0m);

        var staleReplay = CloneStockEventForStaleReplay(stockEvent);
        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            staleReplay,
            progressEntries);
        await AssertStockCon01ProjectionConvergenceAsync(mongoScope.Database, seeded, expectedStock: 0m);

        progressEntries.Enqueue("verification:stock-con-02-active-replay-stock-authority-converged");
    }

    [Fact]
    public async Task STOCK_CON_03_Duplicate_Inventory_Worker_Delivery_During_Active_Processing_Converges_Safely()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var mongoScope = await CreateIsolatedMongoDatabaseScopeAsync("stock-con-03");
        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);
        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext, StockCon01InitialStockQuantity);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");
        var mongoConnectionString = GetRequiredMongoConnectionString();

        await SeedStockCon01ReadModelsAsync(mongoScope.Database, seeded);

        var progressEntries = new ConcurrentQueue<string>();
        var actor = new SaleActor(
            Name: "stock-con-03-authoritative-sale",
            RequestId: "stock-con-03-authoritative-sale",
            TraceIdentifier: "stock-con-03-authoritative-sale",
            UserId: ActorAUserId);
        var request = BuildStockCon02CreateSaleRequest(seeded);

        var saleResult = await ExecuteSaleActorAsync(
            connectionString,
            tenantContext,
            seeded,
            actor,
            request,
            progressEntries);

        Assert.Null(saleResult.Exception);
        Assert.True(saleResult.Succeeded, saleResult.PayloadJson ?? "STOCK-CON-03 authoritative sale did not succeed.");

        var invoiceId = saleResult.Response!.InvoiceId;
        await AssertStockCon03AuthoritativeSqlStateAsync(
            scope,
            tenantContext,
            seeded,
            invoiceId,
            expectedPublished: false,
            expectedAttemptCount: 0,
            progressEntries);

        var stockEvent = await LoadSingleStockCon01StockEventAsync(scope, tenantContext, seeded, invoiceId);
        await AssertStockCon01ProjectionConvergenceAsync(mongoScope.Database, seeded, expectedStock: StockCon01InitialStockQuantity);

        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            stockEvent,
            progressEntries);
        var convergedVisibility = await AssertStockCon03ProjectionVisibilityAsync(
            mongoScope.Database,
            seeded,
            expectedStock: 0m,
            expectedEvent: stockEvent,
            progressEntries);

        var duplicatePublishWindow = new BlockingPublishWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var duplicatePublishEndpoint = BuildBlockingProjectionPublishEndpoint(
            duplicatePublishWindow,
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            progressEntries);
        using var stockSyncServices = BuildStockSyncProcessorServiceProvider(
            connectionString,
            tenantContext,
            duplicatePublishEndpoint.Object);
        using var stockSyncTelemetry = new PointOfSale.Infrastructure.Instrumentation.CommerceReliabilityTelemetry();
        var stockSyncProcessor = new StockSyncOutboxProcessor(
            stockSyncServices,
            stockSyncTelemetry,
            NullLogger<StockSyncOutboxProcessor>.Instance,
            Options.Create(new StockSyncOutboxOptions()));

        var workerATask = ExecuteStockSyncWorkerAsync(
            stockSyncProcessor,
            "stock-con-03-worker-a",
            progressEntries);

        await duplicatePublishWindow.WaitForFirstPublishAsync();
        await AssertStockSyncClaimVisibleDuringActivePublishAsync(
            scope,
            tenantContext,
            seeded,
            invoiceId,
            await LoadSiCon03StateSnapshotAsync(scope, tenantContext, seeded, invoiceId),
            progressEntries);
        await AssertStockCon03NoPartialProjectionVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            convergedVisibility,
            progressEntries);

        var workerBTask = ExecuteStockSyncWorkerAsync(
            stockSyncProcessor,
            "stock-con-03-worker-b",
            progressEntries);

        StockSyncWorkerResult duplicateWorkerResult;
        try
        {
            duplicateWorkerResult = await workerBTask.WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"STOCK-CON-03 duplicate worker did not complete while Worker A was active. Progress: {string.Join(" | ", progressEntries)}");
        }

        await AssertStockCon03NoPartialProjectionVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            convergedVisibility,
            progressEntries);

        duplicatePublishWindow.ReleaseFirstPublish();

        StockSyncWorkerResult originalWorkerResult;
        try
        {
            originalWorkerResult = await workerATask.WaitAsync(TimeSpan.FromSeconds(60));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"STOCK-CON-03 Worker A did not finish after projection release. Progress: {string.Join(" | ", progressEntries)}");
        }

        await AssertStockCon03DuplicateWorkerConvergenceAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            invoiceId,
            stockEvent,
            originalWorkerResult,
            duplicateWorkerResult,
            duplicatePublishWindow,
            progressEntries);

        var afterDuplicateWorker = await AssertStockCon03ProjectionVisibilityAsync(
            mongoScope.Database,
            seeded,
            expectedStock: 0m,
            expectedEvent: stockEvent,
            progressEntries);
        Assert.Equal(convergedVisibility.AuthoritativeFingerprint, afterDuplicateWorker.AuthoritativeFingerprint);

        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            stockEvent,
            progressEntries);
        var afterDuplicateProjection = await AssertStockCon03ProjectionVisibilityAsync(
            mongoScope.Database,
            seeded,
            expectedStock: 0m,
            expectedEvent: stockEvent,
            progressEntries);
        Assert.Equal(afterDuplicateWorker.AuthoritativeFingerprint, afterDuplicateProjection.AuthoritativeFingerprint);

        var staleReplay = CloneStockEventForStaleReplay(stockEvent);
        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            staleReplay,
            progressEntries);
        var afterStaleReplay = await AssertStockCon03ProjectionVisibilityAsync(
            mongoScope.Database,
            seeded,
            expectedStock: 0m,
            expectedEvent: stockEvent,
            progressEntries);
        Assert.Equal(afterDuplicateWorker.AuthoritativeFingerprint, afterStaleReplay.AuthoritativeFingerprint);

        var rebuildEvent = await BuildStockCon03SqlTruthRebuildEventAsync(
            scope,
            tenantContext,
            seeded,
            stockEvent);
        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            rebuildEvent,
            progressEntries);
        await AssertStockCon03ProjectionVisibilityAsync(
            mongoScope.Database,
            seeded,
            expectedStock: 0m,
            expectedEvent: rebuildEvent,
            progressEntries);

        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            staleReplay,
            progressEntries);
        await AssertStockCon03ProjectionVisibilityAsync(
            mongoScope.Database,
            seeded,
            expectedStock: 0m,
            expectedEvent: rebuildEvent,
            progressEntries);

        var reconciliation = await ExecuteStockCon03InventoryReconciliationAsync(
            connectionString,
            tenantContext);
        Assert.Equal("OK", reconciliation.Status);
        Assert.Equal(0, reconciliation.InvalidStockRowCount);
        Assert.Equal(0, reconciliation.DuplicateEventReplayCount);
        Assert.Equal(0, reconciliation.OpenReservationsOnCompletedOrdersCount);

        await AssertStockCon03AuthoritativeSqlStateAsync(
            scope,
            tenantContext,
            seeded,
            invoiceId,
            expectedPublished: true,
            expectedAttemptCount: 0,
            progressEntries);

        progressEntries.Enqueue("verification:stock-con-03-duplicate-worker-inventory-projection-converged");
    }

    [Fact]
    public async Task STOCK_CON_04_Replay_During_Sales_Return_Stock_Restoration_Converges_Safely()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var mongoScope = await CreateIsolatedMongoDatabaseScopeAsync("stock-con-04");
        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);
        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext, StockCon01InitialStockQuantity);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");
        var mongoConnectionString = GetRequiredMongoConnectionString();

        await SeedStockCon01ReadModelsAsync(mongoScope.Database, seeded);

        var progressEntries = new ConcurrentQueue<string>();
        var saleActor = new SaleActor(
            Name: "stock-con-04-authoritative-sale",
            RequestId: "stock-con-04-authoritative-sale",
            TraceIdentifier: "stock-con-04-authoritative-sale",
            UserId: ActorAUserId);
        var saleResult = await ExecuteSaleActorAsync(
            connectionString,
            tenantContext,
            seeded,
            saleActor,
            BuildStockCon02CreateSaleRequest(seeded),
            progressEntries);

        Assert.Null(saleResult.Exception);
        Assert.True(saleResult.Succeeded, saleResult.PayloadJson ?? "STOCK-CON-04 authoritative sale did not commit.");
        var invoiceId = saleResult.Response!.InvoiceId;

        var saleStockEvent = await LoadSingleStockCon01StockEventAsync(scope, tenantContext, seeded, invoiceId);
        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            saleStockEvent,
            progressEntries);
        var soldOutVisibility = await AssertStockCon03ProjectionVisibilityAsync(
            mongoScope.Database,
            seeded,
            expectedStock: 0m,
            expectedEvent: saleStockEvent,
            progressEntries);

        var overlapWindow = new ActiveTransactionReplayWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var sharedRequestId = "stock-con-04-return-restoration-replay";
        var sharedRequest = BuildStockCon04SalesReturnRequest(seeded, invoiceId);
        var originalActor = new SaleActor(
            Name: "stock-con-04-request-a",
            RequestId: sharedRequestId,
            TraceIdentifier: "stock-con-04-original-active",
            UserId: ActorAUserId);
        var replayActor = new SaleActor(
            Name: "stock-con-04-request-b",
            RequestId: sharedRequestId,
            TraceIdentifier: "stock-con-04-overlapping-replay",
            UserId: ActorBUserId);

        var originalTask = ExecuteSalesReturnRequestActorAsync(
            connectionString,
            tenantContext,
            originalActor,
            sharedRequest,
            progressEntries,
            new CommitPauseTransactionInterceptor(originalActor.Name, overlapWindow));

        await overlapWindow.WaitForOriginalCommitPauseAsync();
        await AssertStockCon04NoPartialRestorationVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            invoiceId,
            soldOutVisibility,
            progressEntries);

        var replayTask = ExecuteSalesReturnRequestActorAsync(
            connectionString,
            tenantContext,
            replayActor,
            sharedRequest,
            progressEntries,
            new ReplayClaimAttemptInterceptor(replayActor.Name, overlapWindow));

        await overlapWindow.WaitForReplayClaimAttemptAsync();
        Assert.True(
            overlapWindow.OriginalWasPausedBeforeReplayClaim,
            $"Replay did not overlap the active sales-return stock restoration. Progress: {string.Join(" | ", progressEntries)}");

        await AssertStockCon04NoPartialRestorationVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            invoiceId,
            soldOutVisibility,
            progressEntries);

        overlapWindow.ReleaseOriginalCommit();

        PriceCon12ReturnActorResult[] results;
        try
        {
            results = await Task.WhenAll(originalTask, replayTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"STOCK-CON-04 timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.True(overlapWindow.OriginalCommitPaused, $"Request A never paused before commit. Progress: {string.Join(" | ", progressEntries)}");
        Assert.True(overlapWindow.ReplayClaimAttempted, $"Request B never attempted the overlapping return claim. Progress: {string.Join(" | ", progressEntries)}");
        Assert.All(results, result => Assert.Null(result.Exception));

        var committedReturn = await AssertStockCon04FinalAuthorityAsync(
            scope,
            tenantContext,
            seeded,
            invoiceId,
            originalActor,
            replayActor,
            sharedRequest,
            results,
            progressEntries);

        var returnStockEvent = await LoadStockCon04ReturnStockEventAsync(
            scope,
            tenantContext,
            seeded,
            committedReturn.Response!.ReturnId);
        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            returnStockEvent,
            progressEntries);
        var restoredVisibility = await AssertStockCon03ProjectionVisibilityAsync(
            mongoScope.Database,
            seeded,
            expectedStock: StockCon01SaleQuantity,
            expectedEvent: returnStockEvent,
            progressEntries);

        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            returnStockEvent,
            progressEntries);
        var afterDuplicateReturnDelivery = await AssertStockCon03ProjectionVisibilityAsync(
            mongoScope.Database,
            seeded,
            expectedStock: StockCon01SaleQuantity,
            expectedEvent: returnStockEvent,
            progressEntries);
        Assert.Equal(restoredVisibility.AuthoritativeFingerprint, afterDuplicateReturnDelivery.AuthoritativeFingerprint);

        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            saleStockEvent,
            progressEntries);
        var afterStaleSaleReplay = await AssertStockCon03ProjectionVisibilityAsync(
            mongoScope.Database,
            seeded,
            expectedStock: StockCon01SaleQuantity,
            expectedEvent: returnStockEvent,
            progressEntries);
        Assert.Equal(restoredVisibility.AuthoritativeFingerprint, afterStaleSaleReplay.AuthoritativeFingerprint);

        var rebuildEvent = await BuildStockCon03SqlTruthRebuildEventAsync(
            scope,
            tenantContext,
            seeded,
            returnStockEvent);
        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            rebuildEvent,
            progressEntries);
        await AssertStockCon03ProjectionVisibilityAsync(
            mongoScope.Database,
            seeded,
            expectedStock: StockCon01SaleQuantity,
            expectedEvent: rebuildEvent,
            progressEntries);

        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            saleStockEvent,
            progressEntries);
        await AssertStockCon03ProjectionVisibilityAsync(
            mongoScope.Database,
            seeded,
            expectedStock: StockCon01SaleQuantity,
            expectedEvent: rebuildEvent,
            progressEntries);

        var reconciliation = await ExecuteStockCon03InventoryReconciliationAsync(
            connectionString,
            tenantContext);
        Assert.Equal("OK", reconciliation.Status);
        Assert.Equal(0, reconciliation.InvalidStockRowCount);
        Assert.Equal(0, reconciliation.DuplicateEventReplayCount);
        Assert.Equal(0, reconciliation.OpenReservationsOnCompletedOrdersCount);

        progressEntries.Enqueue("verification:stock-con-04-return-restoration-replay-converged");
    }

    [Fact]
    public async Task STOCK_CON_07_Concurrent_Sales_Return_Against_Same_Historical_Sale_Quantity_Converges_Safely()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var mongoScope = await CreateIsolatedMongoDatabaseScopeAsync("stock-con-07");
        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);
        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext, StockCon01InitialStockQuantity);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");
        var mongoConnectionString = GetRequiredMongoConnectionString();

        await SeedStockCon01ReadModelsAsync(mongoScope.Database, seeded);

        var progressEntries = new ConcurrentQueue<string>();
        var saleActor = new SaleActor(
            Name: "stock-con-07-authoritative-sale",
            RequestId: "stock-con-07-authoritative-sale",
            TraceIdentifier: "stock-con-07-authoritative-sale",
            UserId: ActorAUserId);
        var saleResult = await ExecuteSaleActorAsync(
            connectionString,
            tenantContext,
            seeded,
            saleActor,
            BuildStockCon02CreateSaleRequest(seeded),
            progressEntries);

        Assert.Null(saleResult.Exception);
        Assert.True(saleResult.Succeeded, saleResult.PayloadJson ?? "STOCK-CON-07 authoritative sale did not commit.");
        var invoiceId = saleResult.Response!.InvoiceId;

        var saleStockEvent = await LoadSingleStockCon01StockEventAsync(scope, tenantContext, seeded, invoiceId);
        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            saleStockEvent,
            progressEntries);
        var soldOutVisibility = await AssertStockCon03ProjectionVisibilityAsync(
            mongoScope.Database,
            seeded,
            expectedStock: 0m,
            expectedEvent: saleStockEvent,
            progressEntries);

        var overlapWindow = new ActiveTransactionReplayWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var request = BuildStockCon07SalesReturnRequest(seeded, invoiceId);
        var requestA = new SaleActor(
            Name: "stock-con-07-request-a",
            RequestId: "stock-con-07-request-a",
            TraceIdentifier: "stock-con-07-original-active",
            UserId: ActorAUserId);
        var requestB = new SaleActor(
            Name: "stock-con-07-request-b",
            RequestId: "stock-con-07-request-b",
            TraceIdentifier: "stock-con-07-overlapping-return",
            UserId: ActorBUserId);

        var originalTask = ExecuteSalesReturnRequestActorAsync(
            connectionString,
            tenantContext,
            requestA,
            request,
            progressEntries,
            new CommitPauseTransactionInterceptor(requestA.Name, overlapWindow));

        await overlapWindow.WaitForOriginalCommitPauseAsync();
        await AssertStockCon04NoPartialRestorationVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            invoiceId,
            soldOutVisibility,
            progressEntries);

        var overlappingTask = ExecuteSalesReturnRequestActorAsync(
            connectionString,
            tenantContext,
            requestB,
            request,
            progressEntries,
            new ConcurrentSalesReturnInvoiceLockAttemptInterceptor(requestB.Name, overlapWindow));

        await overlapWindow.WaitForReplayClaimAttemptAsync();
        Assert.True(
            overlapWindow.OriginalWasPausedBeforeReplayClaim,
            $"The overlapping return did not attempt the historical invoice lock while Request A was active. Progress: {string.Join(" | ", progressEntries)}");

        await AssertStockCon04NoPartialRestorationVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            invoiceId,
            soldOutVisibility,
            progressEntries);

        overlapWindow.ReleaseOriginalCommit();

        PriceCon12ReturnActorResult[] results;
        try
        {
            results = await Task.WhenAll(originalTask, overlappingTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"STOCK-CON-07 timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.True(overlapWindow.OriginalCommitPaused, $"Request A never paused before commit. Progress: {string.Join(" | ", progressEntries)}");
        Assert.True(overlapWindow.ReplayClaimAttempted, $"Request B never attempted the overlapping historical invoice lock. Progress: {string.Join(" | ", progressEntries)}");
        Assert.All(results, result => Assert.Null(result.Exception));

        var committedReturn = await AssertStockCon07FinalAuthorityAsync(
            scope,
            tenantContext,
            seeded,
            invoiceId,
            requestA,
            requestB,
            request,
            results,
            progressEntries);

        var returnStockEvent = await LoadStockCon04ReturnStockEventAsync(
            scope,
            tenantContext,
            seeded,
            committedReturn.Response!.ReturnId);
        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            returnStockEvent,
            progressEntries);
        var restoredVisibility = await AssertStockCon03ProjectionVisibilityAsync(
            mongoScope.Database,
            seeded,
            expectedStock: StockCon01SaleQuantity,
            expectedEvent: returnStockEvent,
            progressEntries);

        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            returnStockEvent,
            progressEntries);
        var afterDuplicateReturnDelivery = await AssertStockCon03ProjectionVisibilityAsync(
            mongoScope.Database,
            seeded,
            expectedStock: StockCon01SaleQuantity,
            expectedEvent: returnStockEvent,
            progressEntries);
        Assert.Equal(restoredVisibility.AuthoritativeFingerprint, afterDuplicateReturnDelivery.AuthoritativeFingerprint);

        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            saleStockEvent,
            progressEntries);
        var afterStaleSaleReplay = await AssertStockCon03ProjectionVisibilityAsync(
            mongoScope.Database,
            seeded,
            expectedStock: StockCon01SaleQuantity,
            expectedEvent: returnStockEvent,
            progressEntries);
        Assert.Equal(restoredVisibility.AuthoritativeFingerprint, afterStaleSaleReplay.AuthoritativeFingerprint);

        var rebuildEvent = await BuildStockCon03SqlTruthRebuildEventAsync(
            scope,
            tenantContext,
            seeded,
            returnStockEvent);
        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            rebuildEvent,
            progressEntries);
        await AssertStockCon03ProjectionVisibilityAsync(
            mongoScope.Database,
            seeded,
            expectedStock: StockCon01SaleQuantity,
            expectedEvent: rebuildEvent,
            progressEntries);

        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            saleStockEvent,
            progressEntries);
        await AssertStockCon03ProjectionVisibilityAsync(
            mongoScope.Database,
            seeded,
            expectedStock: StockCon01SaleQuantity,
            expectedEvent: rebuildEvent,
            progressEntries);

        var reconciliation = await ExecuteStockCon03InventoryReconciliationAsync(
            connectionString,
            tenantContext);
        Assert.Equal("OK", reconciliation.Status);
        Assert.Equal(0, reconciliation.InvalidStockRowCount);
        Assert.Equal(0, reconciliation.DuplicateEventReplayCount);
        Assert.Equal(0, reconciliation.OpenReservationsOnCompletedOrdersCount);

        progressEntries.Enqueue("verification:stock-con-07-concurrent-historical-return-boundary-converged");
    }

    [Fact]
    public async Task STOCK_CON_05_Duplicate_CommerceHub_Return_Delivery_Converges_Safely()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var mongoScope = await CreateIsolatedMongoDatabaseScopeAsync("stock-con-05");
        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);
        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext, StockCon01InitialStockQuantity);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");
        var mongoConnectionString = GetRequiredMongoConnectionString();

        await SeedStockCon01ReadModelsAsync(mongoScope.Database, seeded);

        var progressEntries = new ConcurrentQueue<string>();
        var commerceHubGraph = await SeedStockCon05CommerceHubDeliveredReturnAsync(
            seedContext,
            seeded,
            progressEntries);

        var deliveryEvent = await LoadStockCon05StockEventAsync(
            scope,
            tenantContext,
            seeded,
            commerceHubGraph.MarketplaceOrderId,
            "CommerceHubDelivery");
        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            deliveryEvent,
            progressEntries);
        var soldOutVisibility = await AssertStockCon03ProjectionVisibilityAsync(
            mongoScope.Database,
            seeded,
            expectedStock: 0m,
            expectedEvent: deliveryEvent,
            progressEntries);

        var overlapWindow = new ActiveTransactionReplayWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var deliveryA = new SaleActor(
            Name: "stock-con-05-delivery-a",
            RequestId: "stock-con-05-return-delivery",
            TraceIdentifier: "stock-con-05-delivery-a",
            UserId: ActorAUserId);
        var deliveryB = new SaleActor(
            Name: "stock-con-05-delivery-b-duplicate",
            RequestId: "stock-con-05-return-delivery",
            TraceIdentifier: "stock-con-05-delivery-b-duplicate",
            UserId: ActorBUserId);

        var deliveryATask = ExecuteCommerceHubReturnDeliveryActorAsync(
            connectionString,
            tenantContext,
            commerceHubGraph,
            deliveryA,
            progressEntries,
            new CommitPauseTransactionInterceptor(deliveryA.Name, overlapWindow));

        await overlapWindow.WaitForOriginalCommitPauseAsync();
        await AssertStockCon05NoPartialReturnDeliveryVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            commerceHubGraph,
            soldOutVisibility,
            progressEntries);

        var deliveryBTask = ExecuteCommerceHubReturnDeliveryActorAsync(
            connectionString,
            tenantContext,
            commerceHubGraph,
            deliveryB,
            progressEntries,
            new CommerceHubReturnLockAttemptInterceptor(deliveryB.Name, overlapWindow));

        await overlapWindow.WaitForReplayClaimAttemptAsync();
        Assert.True(
            overlapWindow.OriginalWasPausedBeforeReplayClaim,
            $"Duplicate CommerceHub return delivery did not overlap the active authoritative receipt. Progress: {string.Join(" | ", progressEntries)}");

        await AssertStockCon05NoPartialReturnDeliveryVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            commerceHubGraph,
            soldOutVisibility,
            progressEntries);

        overlapWindow.ReleaseOriginalCommit();

        CommerceHubReturnDeliveryActorResult[] results;
        try
        {
            results = await Task.WhenAll(deliveryATask, deliveryBTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"STOCK-CON-05 timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.True(overlapWindow.OriginalCommitPaused, $"Delivery A never paused before commit. Progress: {string.Join(" | ", progressEntries)}");
        Assert.True(overlapWindow.ReplayClaimAttempted, $"Delivery B never attempted the overlapping CommerceHub return row lock. Progress: {string.Join(" | ", progressEntries)}");
        Assert.All(results, result => Assert.Null(result.Exception));

        var authoritativeReturn = await AssertStockCon05FinalAuthorityAsync(
            scope,
            tenantContext,
            seeded,
            commerceHubGraph,
            deliveryA,
            deliveryB,
            results,
            progressEntries);

        var returnEvent = await LoadStockCon05StockEventAsync(
            scope,
            tenantContext,
            seeded,
            authoritativeReturn.Response!.ReturnId,
            "CommerceHubReturn");
        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            returnEvent,
            progressEntries);
        var restoredVisibility = await AssertStockCon03ProjectionVisibilityAsync(
            mongoScope.Database,
            seeded,
            expectedStock: StockCon01SaleQuantity,
            expectedEvent: returnEvent,
            progressEntries);

        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            returnEvent,
            progressEntries);
        var afterDuplicateReturnProjection = await AssertStockCon03ProjectionVisibilityAsync(
            mongoScope.Database,
            seeded,
            expectedStock: StockCon01SaleQuantity,
            expectedEvent: returnEvent,
            progressEntries);
        Assert.Equal(restoredVisibility.AuthoritativeFingerprint, afterDuplicateReturnProjection.AuthoritativeFingerprint);

        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            deliveryEvent,
            progressEntries);
        var afterStaleDeliveryReplay = await AssertStockCon03ProjectionVisibilityAsync(
            mongoScope.Database,
            seeded,
            expectedStock: StockCon01SaleQuantity,
            expectedEvent: returnEvent,
            progressEntries);
        Assert.Equal(restoredVisibility.AuthoritativeFingerprint, afterStaleDeliveryReplay.AuthoritativeFingerprint);

        await AssertCommerceHubReturnDuplicateDeliveryDoesNotMutateStockAsync(
            scope,
            connectionString,
            tenantContext,
            seeded,
            commerceHubGraph,
            deliveryA,
            progressEntries);

        await AssertCommerceHubReturnOutOfOrderDeliveryDoesNotMutateStockAsync(
            scope,
            connectionString,
            tenantContext,
            seeded,
            commerceHubGraph,
            progressEntries);

        var rebuildEvent = await BuildStockCon03SqlTruthRebuildEventAsync(
            scope,
            tenantContext,
            seeded,
            returnEvent);
        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            rebuildEvent,
            progressEntries);
        await AssertStockCon03ProjectionVisibilityAsync(
            mongoScope.Database,
            seeded,
            expectedStock: StockCon01SaleQuantity,
            expectedEvent: rebuildEvent,
            progressEntries);

        await ApplyStockCon01ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            deliveryEvent,
            progressEntries);
        await AssertStockCon03ProjectionVisibilityAsync(
            mongoScope.Database,
            seeded,
            expectedStock: StockCon01SaleQuantity,
            expectedEvent: rebuildEvent,
            progressEntries);

        var reconciliation = await ExecuteStockCon03InventoryReconciliationAsync(
            connectionString,
            tenantContext);
        Assert.Equal("OK", reconciliation.Status);
        Assert.Equal(0, reconciliation.InvalidStockRowCount);
        Assert.Equal(0, reconciliation.DuplicateEventReplayCount);
        Assert.Equal(0, reconciliation.OpenReservationsOnCompletedOrdersCount);

        progressEntries.Enqueue("verification:stock-con-05-duplicate-commercehub-return-delivery-converged");
    }

    [Fact]
    public async Task PRICE_CON_11_Concurrent_Cost_Price_Mutation_During_Sales_Activity_Preserves_Historical_COGS_And_Replay_Safety()
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
        await ConfigurePriceCon11SeedAsync(seedContext, seeded);

        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");
        var progressEntries = new ConcurrentQueue<string>();

        var saleActor = new SaleActor(
            Name: "price-con-11-sale-a",
            RequestId: "price-con-11-sale-a",
            TraceIdentifier: "price-con-11-sale-a-active",
            UserId: ActorAUserId);
        var costActor = new SaleActor(
            Name: "price-con-11-cost-b",
            RequestId: "price-con-11-cost-b",
            TraceIdentifier: "price-con-11-cost-b-active",
            UserId: ActorBUserId);

        var saleWindow = new ActiveTransactionReplayWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var costWindow = new ActiveTransactionReplayWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);

        var saleTask = ExecuteSaleActorAsync(
            connectionString,
            tenantContext,
            seeded,
            saleActor,
            BuildPriceCon11CreateSaleRequest(seeded),
            progressEntries,
            new FailedCommandProgressInterceptor(saleActor.Name, progressEntries),
            new SalePreCommitStockProjectionPauseInterceptor(saleActor.Name, saleWindow));

        await saleWindow.WaitForOriginalCommitPauseAsync();
        await AssertPriceCon11VisibilityAsync(
            scope,
            tenantContext,
            seeded,
            expectedVariantCostPrice: PriceCon11InitialCostPrice,
            expectedStockQuantity: InitialStockQuantity,
            expectedSalesInvoices: 0,
            expectedStockMovements: 0,
            progressEntries,
            "sale-staged-but-uncommitted");

        var costTask = ExecuteVariantCostMutationActorAsync(
            connectionString,
            tenantContext,
            seeded,
            costActor,
            PriceCon11MutatedCostPrice,
            progressEntries,
            new CommitPauseTransactionInterceptor(costActor.Name, costWindow));

        await costWindow.WaitForOriginalCommitPauseAsync();
        await AssertPriceCon11VisibilityAsync(
            scope,
            tenantContext,
            seeded,
            expectedVariantCostPrice: PriceCon11InitialCostPrice,
            expectedStockQuantity: InitialStockQuantity,
            expectedSalesInvoices: 0,
            expectedStockMovements: 0,
            progressEntries,
            "cost-mutation-staged-but-uncommitted");

        costWindow.ReleaseOriginalCommit();
        var costResult = await costTask.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Null(costResult.Exception);
        Assert.True(costResult.Succeeded, costResult.PayloadJson ?? "PRICE-CON-11 cost mutation did not commit.");

        await AssertPriceCon11VisibilityAsync(
            scope,
            tenantContext,
            seeded,
            expectedVariantCostPrice: PriceCon11MutatedCostPrice,
            expectedStockQuantity: InitialStockQuantity,
            expectedSalesInvoices: 0,
            expectedStockMovements: 0,
            progressEntries,
            "cost-committed-while-sale-uncommitted");

        saleWindow.ReleaseOriginalCommit();
        SaleActorResult saleResult;
        try
        {
            saleResult = await saleTask.WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"PRICE-CON-11 sale actor timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.Null(saleResult.Exception);
        Assert.True(
            saleResult.Succeeded,
            $"{saleResult.PayloadJson ?? "PRICE-CON-11 sale did not commit."} Progress: {string.Join(" | ", progressEntries)}");

        var committed = await AssertPriceCon11FinalConvergenceAsync(
            scope,
            tenantContext,
            seeded,
            saleResult.Response!.InvoiceId,
            progressEntries);

        await AssertPriceCon11ReplayProbeUsesHistoricalCostBasisAsync(
            scope,
            tenantContext,
            seeded,
            committed,
            progressEntries);

        await ReopenAccountingOutboxMessageForWorkerReplayAsync(
            scope,
            tenantContext,
            seeded,
            committed.InvoiceId,
            progressEntries);

        using var accountingWorkerServices = BuildAccountingOutboxProcessorServiceProvider(
            connectionString,
            tenantContext);
        using var accountingTelemetry = new PointOfSale.Infrastructure.Instrumentation.CommerceReliabilityTelemetry();
        var accountingWorker = new AccountingOutboxProcessor(
            accountingWorkerServices,
            NullLogger<AccountingOutboxProcessor>.Instance,
            Options.Create(new AccountingOutboxOptions { BatchSize = 1, ProcessingLeaseSeconds = 30 }),
            reliabilityTelemetry: accountingTelemetry);

        var replayWorkerResult = await ExecuteAccountingOutboxWorkerAsync(
            accountingWorker,
            new AccountingReplayWorker("price-con-11-duplicate-accounting-worker", ActorBUserId),
            progressEntries);

        Assert.Null(replayWorkerResult.Exception);
        Assert.Equal(1, replayWorkerResult.ProcessedCount);

        await AssertPriceCon11DurableFinancialFingerprintUnchangedAsync(
            scope,
            tenantContext,
            seeded,
            committed,
            progressEntries);
    }

    [Fact]
    public async Task PRICE_CON_12_Replay_During_Cost_Basis_Recalculation_Converges_Safely()
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
        await ConfigurePriceCon11SeedAsync(seedContext, seeded);
        await SeedPriceCon12OpeningValuationMovementAsync(seedContext, seeded);

        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");
        var progressEntries = new ConcurrentQueue<string>();

        var saleResult = await ExecuteSaleActorAsync(
            connectionString,
            tenantContext,
            seeded,
            new SaleActor(
                Name: "price-con-12-sale",
                RequestId: "price-con-12-sale",
                TraceIdentifier: "price-con-12-sale",
                UserId: ActorAUserId),
            BuildPriceCon11CreateSaleRequest(seeded),
            progressEntries);

        Assert.Null(saleResult.Exception);
        Assert.True(saleResult.Succeeded, saleResult.PayloadJson ?? "PRICE-CON-12 sale did not commit.");

        var costMutationResult = await ExecuteVariantCostMutationActorAsync(
            connectionString,
            tenantContext,
            seeded,
            new SaleActor(
                Name: "price-con-12-future-cost",
                RequestId: "price-con-12-future-cost",
                TraceIdentifier: "price-con-12-future-cost",
                UserId: ActorBUserId),
            PriceCon11MutatedCostPrice,
            progressEntries);

        Assert.Null(costMutationResult.Exception);
        Assert.True(costMutationResult.Succeeded, costMutationResult.PayloadJson ?? "PRICE-CON-12 future cost mutation did not commit.");

        var returnResult = await ExecuteSalesReturnActorAsync(
            connectionString,
            tenantContext,
            seeded,
            saleResult.Response!.InvoiceId,
            new SaleActor(
                Name: "price-con-12-return",
                RequestId: "price-con-12-return",
                TraceIdentifier: "price-con-12-return",
                UserId: ActorAUserId),
            progressEntries);

        Assert.Null(returnResult.Exception);
        Assert.True(returnResult.Succeeded, returnResult.PayloadJson ?? "PRICE-CON-12 return did not commit.");

        var baseline = await LoadPriceCon12StateSnapshotAsync(
            scope,
            tenantContext,
            seeded,
            saleResult.Response.InvoiceId,
            returnResult.Response!.ReturnId);

        Assert.Equal(PriceCon11InitialCostPrice, baseline.SaleCostPriceSnapshot);
        Assert.Equal(PriceCon11ExpectedCostAmount, baseline.SaleCostAmountSnapshot);
        Assert.Equal(PriceCon11InitialCostPrice, baseline.ReturnCostPriceSnapshot);
        Assert.Equal(PriceCon12ExpectedReturnCostAmount, baseline.ReturnCostAmountSnapshot);
        Assert.Equal(PriceCon11MutatedCostPrice, baseline.CurrentVariantCostPrice);
        Assert.Equal(InitialStockQuantity - ContestedSaleQuantity + PriceCon12ReturnQuantity, baseline.StockQuantity);
        Assert.Equal(PriceCon11InitialCostPrice, baseline.WeightedAverageMovementCost);
        Assert.Equal(baseline.StockQuantity * PriceCon11InitialCostPrice, baseline.WeightedMovementInventoryValue);
        Assert.Equal(baseline.StockQuantity * PriceCon11MutatedCostPrice, baseline.CurrentCostInventoryValue);
        Assert.Equal(0, baseline.RecalculationProcessedEvents);

        await AssertPriceCon12HistoricalSnapshotMutationIsBlockedAsync(
            scope,
            tenantContext,
            saleResult.Response.InvoiceId,
            returnResult.Response.ReturnId,
            progressEntries);

        var replayWindow = new ActiveTransactionReplayWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);

        var workerATask = ExecuteCostBasisRecalculationWorkerAsync(
            connectionString,
            tenantContext,
            seeded,
            saleResult.Response.InvoiceId,
            returnResult.Response.ReturnId,
            "price-con-12-worker-a",
            progressEntries,
            new CommitPauseTransactionInterceptor("price-con-12-worker-a", replayWindow));

        await replayWindow.WaitForOriginalCommitPauseAsync();

        await AssertPriceCon12NoPartialRecalculationVisibleAsync(
            scope,
            tenantContext,
            seeded,
            saleResult.Response.InvoiceId,
            returnResult.Response.ReturnId,
            baseline,
            progressEntries);

        var workerBTask = ExecuteCostBasisRecalculationWorkerAsync(
            connectionString,
            tenantContext,
            seeded,
            saleResult.Response.InvoiceId,
            returnResult.Response.ReturnId,
            "price-con-12-worker-b-replay",
            progressEntries,
            new PriceCon12ProcessedEventInsertAttemptInterceptor("price-con-12-worker-b-replay", replayWindow));

        await replayWindow.WaitForReplayClaimAttemptAsync();
        Assert.True(
            replayWindow.OriginalWasPausedBeforeReplayClaim,
            $"PRICE-CON-12 did not overlap replay with the active recalculation transaction. Progress: {string.Join(" | ", progressEntries)}");

        replayWindow.ReleaseOriginalCommit();

        CostBasisRecalculationWorkerResult[] workerResults;
        try
        {
            workerResults = await Task.WhenAll(workerATask, workerBTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"PRICE-CON-12 recalculation overlap timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.All(workerResults, result => Assert.Null(result.Exception));
        Assert.Contains(workerResults, result => result.InsertedProcessedEvent);
        Assert.Contains(workerResults, result => !result.InsertedProcessedEvent);
        Assert.All(workerResults, result => Assert.Equal(PriceCon11InitialCostPrice, result.WeightedAverageMovementCost));

        var retryResult = await ExecuteCostBasisRecalculationWorkerAsync(
            connectionString,
            tenantContext,
            seeded,
            saleResult.Response.InvoiceId,
            returnResult.Response.ReturnId,
            "price-con-12-retry-reclaim",
            progressEntries);

        Assert.Null(retryResult.Exception);
        Assert.False(retryResult.InsertedProcessedEvent);
        Assert.Equal(PriceCon11InitialCostPrice, retryResult.WeightedAverageMovementCost);

        await ReopenAccountingOutboxMessageForWorkerReplayAsync(
            scope,
            tenantContext,
            seeded,
            saleResult.Response.InvoiceId,
            progressEntries);

        using var accountingWorkerServices = BuildAccountingOutboxProcessorServiceProvider(
            connectionString,
            tenantContext);
        using var accountingTelemetry = new PointOfSale.Infrastructure.Instrumentation.CommerceReliabilityTelemetry();
        var accountingWorker = new AccountingOutboxProcessor(
            accountingWorkerServices,
            NullLogger<AccountingOutboxProcessor>.Instance,
            Options.Create(new AccountingOutboxOptions { BatchSize = 1, ProcessingLeaseSeconds = 30 }),
            reliabilityTelemetry: accountingTelemetry);

        var accountingReplayResult = await ExecuteAccountingOutboxReplayUntilInvoiceProcessedAsync(
            accountingWorker,
            new AccountingReplayWorker("price-con-12-accounting-replay", ActorBUserId),
            scope,
            tenantContext,
            seeded,
            saleResult.Response.InvoiceId,
            progressEntries);

        Assert.Null(accountingReplayResult.Exception);
        Assert.Equal(1, accountingReplayResult.ProcessedCount);

        var final = await LoadPriceCon12StateSnapshotAsync(
            scope,
            tenantContext,
            seeded,
            saleResult.Response.InvoiceId,
            returnResult.Response.ReturnId);

        Assert.Equal(baseline.FinancialFingerprint, final.FinancialFingerprint);
        Assert.Equal(1, final.RecalculationProcessedEvents);
        Assert.Equal(PriceCon11MutatedCostPrice, final.CurrentVariantCostPrice);
        Assert.Equal(baseline.StockQuantity, final.StockQuantity);
        Assert.Equal(baseline.WeightedAverageMovementCost, final.WeightedAverageMovementCost);
        Assert.Equal(baseline.WeightedMovementInventoryValue, final.WeightedMovementInventoryValue);
        Assert.Equal(baseline.CurrentCostInventoryValue, final.CurrentCostInventoryValue);

        progressEntries.Enqueue("verification:price-con-12-replay-recalculation-converged");
    }

    [Fact]
    public async Task PRICE_CON_13_Duplicate_Financial_Pricing_Projection_Delivery_Converges_Safely()
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
        await ConfigurePriceCon11SeedAsync(seedContext, seeded);
        await SeedPriceCon12OpeningValuationMovementAsync(seedContext, seeded);

        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");
        var progressEntries = new ConcurrentQueue<string>();

        var saleResult = await ExecuteSaleActorAsync(
            connectionString,
            tenantContext,
            seeded,
            new SaleActor(
                Name: "price-con-13-sale",
                RequestId: "price-con-13-sale",
                TraceIdentifier: "price-con-13-sale",
                UserId: ActorAUserId),
            BuildPriceCon11CreateSaleRequest(seeded),
            progressEntries);

        Assert.Null(saleResult.Exception);
        Assert.True(saleResult.Succeeded, saleResult.PayloadJson ?? "PRICE-CON-13 sale did not commit.");

        var costMutationResult = await ExecuteVariantCostMutationActorAsync(
            connectionString,
            tenantContext,
            seeded,
            new SaleActor(
                Name: "price-con-13-future-cost",
                RequestId: "price-con-13-future-cost",
                TraceIdentifier: "price-con-13-future-cost",
                UserId: ActorBUserId),
            PriceCon11MutatedCostPrice,
            progressEntries);

        Assert.Null(costMutationResult.Exception);
        Assert.True(costMutationResult.Succeeded, costMutationResult.PayloadJson ?? "PRICE-CON-13 future cost mutation did not commit.");

        var returnResult = await ExecuteSalesReturnActorAsync(
            connectionString,
            tenantContext,
            seeded,
            saleResult.Response!.InvoiceId,
            new SaleActor(
                Name: "price-con-13-return",
                RequestId: "price-con-13-return",
                TraceIdentifier: "price-con-13-return",
                UserId: ActorAUserId),
            progressEntries);

        Assert.Null(returnResult.Exception);
        Assert.True(returnResult.Succeeded, returnResult.PayloadJson ?? "PRICE-CON-13 return did not commit.");

        await SeedPriceCon13ProjectionBaselineAsync(scope, tenantContext);

        var baseline = await LoadPriceCon13StateSnapshotAsync(
            scope,
            tenantContext,
            seeded,
            saleResult.Response.InvoiceId,
            returnResult.Response!.ReturnId);

        Assert.Equal(PriceCon11InitialCostPrice, baseline.ImmutableFinancialState.SaleCostPriceSnapshot);
        Assert.Equal(PriceCon11InitialCostPrice, baseline.ImmutableFinancialState.ReturnCostPriceSnapshot);
        Assert.Equal(PriceCon11MutatedCostPrice, baseline.ImmutableFinancialState.CurrentVariantCostPrice);
        Assert.Equal(0, baseline.ProjectionProcessedEvents);
        Assert.Equal(0m, baseline.BiSnapshot.GrossSales);
        Assert.Equal(0m, baseline.BiSnapshot.NetRevenue);

        await AssertPriceCon12HistoricalSnapshotMutationIsBlockedAsync(
            scope,
            tenantContext,
            saleResult.Response.InvoiceId,
            returnResult.Response.ReturnId,
            progressEntries);

        var projectionBoundaryUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
        var replayWindow = new ActiveTransactionReplayWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);

        var workerATask = ExecutePriceCon13FinancialProjectionWorkerAsync(
            connectionString,
            tenantContext,
            seeded,
            saleResult.Response.InvoiceId,
            returnResult.Response.ReturnId,
            "price-con-13-worker-a",
            PriceCon13ProjectionEventId,
            projectionBoundaryUtc,
            progressEntries,
            new CommitPauseTransactionInterceptor("price-con-13-worker-a", replayWindow));

        await replayWindow.WaitForOriginalCommitPauseAsync();

        await AssertPriceCon13NoPartialProjectionVisibleAsync(
            scope,
            tenantContext,
            seeded,
            saleResult.Response.InvoiceId,
            returnResult.Response.ReturnId,
            baseline,
            progressEntries);

        var workerBTask = ExecutePriceCon13FinancialProjectionWorkerAsync(
            connectionString,
            tenantContext,
            seeded,
            saleResult.Response.InvoiceId,
            returnResult.Response.ReturnId,
            "price-con-13-worker-b-duplicate",
            PriceCon13ProjectionEventId,
            projectionBoundaryUtc,
            progressEntries,
            new PriceCon12ProcessedEventInsertAttemptInterceptor("price-con-13-worker-b-duplicate", replayWindow));

        await replayWindow.WaitForReplayClaimAttemptAsync();
        Assert.True(
            replayWindow.OriginalWasPausedBeforeReplayClaim,
            $"PRICE-CON-13 did not overlap duplicate financial projection delivery with Worker A. Progress: {string.Join(" | ", progressEntries)}");

        replayWindow.ReleaseOriginalCommit();

        PriceCon13ProjectionWorkerResult[] workerResults;
        try
        {
            workerResults = await Task.WhenAll(workerATask, workerBTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"PRICE-CON-13 financial projection overlap timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.All(workerResults, result => Assert.Null(result.Exception));
        Assert.Contains(workerResults, result => result.InsertedProcessedEvent && result.AppliedProjection);
        Assert.Contains(workerResults, result => !result.InsertedProcessedEvent && !result.AppliedProjection);

        var afterOverlap = await LoadPriceCon13StateSnapshotAsync(
            scope,
            tenantContext,
            seeded,
            saleResult.Response.InvoiceId,
            returnResult.Response.ReturnId);

        Assert.Equal(baseline.ImmutableFinancialState.FinancialFingerprint, afterOverlap.ImmutableFinancialState.FinancialFingerprint);
        Assert.Equal(1, afterOverlap.ProjectionProcessedEvents);
        Assert.Equal(PriceCon11ExpectedSaleAmount, afterOverlap.BiSnapshot.GrossSales);
        Assert.Equal(PriceCon11ExpectedSaleAmount / 2m, afterOverlap.BiSnapshot.RefundAmount);
        Assert.Equal(50m, afterOverlap.BiSnapshot.NetRevenue);
        Assert.Equal(50m, afterOverlap.ProfitabilityNetProfit);
        Assert.Equal(50m, afterOverlap.MarginPercentage);
        Assert.Equal(afterOverlap.BiSnapshot.Fingerprint, afterOverlap.ReportingProjectionFingerprint);

        var duplicateRetry = await ExecutePriceCon13FinancialProjectionWorkerAsync(
            connectionString,
            tenantContext,
            seeded,
            saleResult.Response.InvoiceId,
            returnResult.Response.ReturnId,
            "price-con-13-retry-reclaim",
            PriceCon13ProjectionEventId,
            projectionBoundaryUtc,
            progressEntries);

        Assert.Null(duplicateRetry.Exception);
        Assert.False(duplicateRetry.InsertedProcessedEvent);
        Assert.False(duplicateRetry.AppliedProjection);

        var afterDuplicateRetry = await LoadPriceCon13StateSnapshotAsync(
            scope,
            tenantContext,
            seeded,
            saleResult.Response.InvoiceId,
            returnResult.Response.ReturnId);

        Assert.Equal(afterOverlap.ImmutableFinancialState.FinancialFingerprint, afterDuplicateRetry.ImmutableFinancialState.FinancialFingerprint);
        Assert.Equal(afterOverlap.BiSnapshot.Fingerprint, afterDuplicateRetry.BiSnapshot.Fingerprint);
        Assert.Equal(1, afterDuplicateRetry.ProjectionProcessedEvents);

        var staleReplay = await ExecutePriceCon13FinancialProjectionWorkerAsync(
            connectionString,
            tenantContext,
            seeded,
            saleResult.Response.InvoiceId,
            returnResult.Response.ReturnId,
            "price-con-13-stale-replay",
            PriceCon13StaleProjectionEventId,
            projectionBoundaryUtc.AddMinutes(-5),
            progressEntries);

        Assert.Null(staleReplay.Exception);
        Assert.True(staleReplay.InsertedProcessedEvent);
        Assert.False(staleReplay.AppliedProjection);
        Assert.True(staleReplay.RejectedAsStale);

        var afterStaleReplay = await LoadPriceCon13StateSnapshotAsync(
            scope,
            tenantContext,
            seeded,
            saleResult.Response.InvoiceId,
            returnResult.Response.ReturnId);

        Assert.Equal(afterOverlap.ImmutableFinancialState.FinancialFingerprint, afterStaleReplay.ImmutableFinancialState.FinancialFingerprint);
        Assert.Equal(afterOverlap.BiSnapshot.Fingerprint, afterStaleReplay.BiSnapshot.Fingerprint);
        Assert.Equal(2, afterStaleReplay.ProjectionProcessedEvents);

        var rebuiltFingerprint = await ExecutePriceCon13FinancialProjectionRebuildAsync(
            scope,
            tenantContext,
            seeded,
            saleResult.Response.InvoiceId,
            returnResult.Response.ReturnId,
            projectionBoundaryUtc.AddMinutes(1),
            progressEntries);

        var afterRebuild = await LoadPriceCon13StateSnapshotAsync(
            scope,
            tenantContext,
            seeded,
            saleResult.Response.InvoiceId,
            returnResult.Response.ReturnId);

        Assert.Equal(afterOverlap.ImmutableFinancialState.FinancialFingerprint, afterRebuild.ImmutableFinancialState.FinancialFingerprint);
        Assert.Equal(rebuiltFingerprint, afterRebuild.BiSnapshot.Fingerprint);
        Assert.Equal(afterOverlap.ProfitabilityNetProfit, afterRebuild.ProfitabilityNetProfit);
        Assert.Equal(afterOverlap.MarginPercentage, afterRebuild.MarginPercentage);
        Assert.Equal(2, afterRebuild.ProjectionProcessedEvents);

        progressEntries.Enqueue("verification:price-con-13-duplicate-financial-projection-converged");
    }

    private static async Task ExecuteScenarioIterationAsync(int iteration)
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
        var stockLockRendezvous = new StockLockRendezvous(
            participantCount: 2,
            timeout: TimeSpan.FromSeconds(20),
            progressEntries);

        var actorA = new SaleActor(
            Name: "actor-a",
            RequestId: "si-con-01-actor-a",
            TraceIdentifier: $"si-con-01-a-{iteration:D2}",
            UserId: ActorAUserId);
        var actorB = new SaleActor(
            Name: "actor-b",
            RequestId: "si-con-01-actor-b",
            TraceIdentifier: $"si-con-01-b-{iteration:D2}",
            UserId: ActorBUserId);

        Task<SaleActorResult> actorATask;
        Task<SaleActorResult> actorBTask;

        // Alternate task creation order across repetitions to prove the winner is not tied to
        // deterministic task scheduling alone. The rendezvous barrier still forces both actors
        // to meet at the ProductVariantStocks FOR UPDATE statement.
        if (iteration % 2 == 0)
        {
            actorATask = ExecuteSaleActorAsync(connectionString, tenantContext, seeded, actorA, stockLockRendezvous, progressEntries);
            actorBTask = ExecuteSaleActorAsync(connectionString, tenantContext, seeded, actorB, stockLockRendezvous, progressEntries);
        }
        else
        {
            actorBTask = ExecuteSaleActorAsync(connectionString, tenantContext, seeded, actorB, stockLockRendezvous, progressEntries);
            actorATask = ExecuteSaleActorAsync(connectionString, tenantContext, seeded, actorA, stockLockRendezvous, progressEntries);
        }

        SaleActorResult[] results;
        try
        {
            results = await Task.WhenAll(actorATask, actorBTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"SI-CON-01 iteration {iteration} timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.True(
            stockLockRendezvous.ArrivalCount == 2,
            $"Expected both actors to reach the contested stock lock, but observed {stockLockRendezvous.ArrivalCount}. " +
            $"Progress: {string.Join(" | ", progressEntries)}");
        Assert.All(results, result => Assert.Null(result.Exception));

        await AssertAuthoritativeConvergenceAsync(
            scope,
            tenantContext,
            seeded,
            actorA,
            actorB,
            results,
            progressEntries);
    }

    private static async Task<SaleActorResult> ExecuteSaleActorAsync(
        string connectionString,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        SaleActor actor,
        StockLockRendezvous stockLockRendezvous,
        ConcurrentQueue<string> progressEntries)
    {
        return await ExecuteSaleActorAsync(
            connectionString,
            tenantContext,
            seeded,
            actor,
            progressEntries,
            new ProductVariantStockLockInterceptor(actor.Name, stockLockRendezvous, progressEntries));
    }

    private static async Task<SaleActorResult> ExecuteSaleActorAsync(
        string connectionString,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        SaleActor actor,
        ConcurrentQueue<string> progressEntries,
        params IInterceptor[] interceptors)
    {
        return await ExecuteSaleActorAsync(
            connectionString,
            tenantContext,
            seeded,
            actor,
            BuildCreateSaleRequest(seeded),
            progressEntries,
            interceptors);
    }

    private static async Task<SaleActorResult> ExecuteSaleActorAsync(
        string connectionString,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        SaleActor actor,
        CreateSaleRequest request,
        ConcurrentQueue<string> progressEntries,
        params IInterceptor[] interceptors)
    {
        await using var context = CreateDbContext(connectionString, tenantContext, interceptors);
        await ConfigureSessionTimeoutsAsync(context);
        var controllerLogger = new RecordingLogger<SalesController>();

        var controller = CreateController(context, tenantContext, actor, controllerLogger);

        progressEntries.Enqueue($"{actor.Name}:request-ready");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            progressEntries.Enqueue($"{actor.Name}:invoking-controller");
            var result = await controller.CreateSale(request, paymentAttemptId: actor.RequestId);
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
            progressEntries.Enqueue($"{actor.Name}:threw:{ex.GetType().Name}");

            return new SaleActorResult(
                actor,
                Succeeded: false,
                StatusCode: null,
                Response: null,
                PayloadJson: null,
                Duration: stopwatch.Elapsed,
                Exception: ex);
        }
    }

    private static async Task<AccountingOutboxWorkerResult> ExecuteAccountingOutboxWorkerAsync(
        AccountingOutboxProcessor processor,
        AccountingReplayWorker worker,
        ConcurrentQueue<string> progressEntries)
    {
        progressEntries.Enqueue($"{worker.Name}:accounting-worker-ready");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var processedCount = await processor.ProcessPendingMessagesAsync(batchSize: 1);
            stopwatch.Stop();
            progressEntries.Enqueue($"{worker.Name}:accounting-worker-finished:{processedCount}");

            return new AccountingOutboxWorkerResult(
                Worker: worker,
                ProcessedCount: processedCount,
                Duration: stopwatch.Elapsed,
                Exception: null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            progressEntries.Enqueue($"{worker.Name}:accounting-worker-threw:{DescribeException(ex)}");

            return new AccountingOutboxWorkerResult(
                Worker: worker,
                ProcessedCount: 0,
                Duration: stopwatch.Elapsed,
                Exception: ex);
        }
    }

    private static async Task<AccountingOutboxWorkerResult> ExecuteAccountingOutboxReplayUntilInvoiceProcessedAsync(
        AccountingOutboxProcessor processor,
        AccountingReplayWorker worker,
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId,
        ConcurrentQueue<string> progressEntries)
    {
        progressEntries.Enqueue($"{worker.Name}:accounting-replay-ready");
        var stopwatch = Stopwatch.StartNew();
        var totalProcessed = 0;

        try
        {
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                var processedCount = await processor.ProcessPendingMessagesAsync(batchSize: 10);
                totalProcessed += processedCount;
                progressEntries.Enqueue($"{worker.Name}:accounting-replay-pass-{attempt}:{processedCount}");

                await using var verificationContext = scope.CreateDbContext(tenantContext);
                var outboxMessage = await verificationContext.AccountingOutboxMessages
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .SingleAsync(message =>
                        message.TenantId == seeded.TenantId &&
                        message.EventType == "PosSale" &&
                        message.BusinessEntityId == invoiceId);

                if (outboxMessage.ProcessedAtUtc.HasValue && outboxMessage.JournalId.HasValue)
                {
                    stopwatch.Stop();
                    progressEntries.Enqueue($"{worker.Name}:accounting-replay-confirmed:{totalProcessed}");

                    return new AccountingOutboxWorkerResult(
                        Worker: worker,
                        ProcessedCount: totalProcessed,
                        Duration: stopwatch.Elapsed,
                        Exception: null);
                }

                if (processedCount == 0)
                {
                    break;
                }
            }

            await using var diagnosticContext = scope.CreateDbContext(tenantContext);
            var pendingDiagnostics = await diagnosticContext.AccountingOutboxMessages
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(message =>
                    message.TenantId == seeded.TenantId &&
                    message.ProcessedAtUtc == null &&
                    !message.IsFailed)
                .OrderBy(message => message.EventType)
                .Select(message =>
                    $"{message.EventType}:{message.BusinessEntityId}:processed={message.ProcessedAtUtc.HasValue}:" +
                    $"journal={message.JournalId.HasValue}:claimed={message.ClaimedUntilUtc.HasValue}:" +
                    $"failed={message.IsFailed}:attempts={message.AttemptCount}:retry={message.NextRetryAtUtc.HasValue}:" +
                    $"error={message.LastError}")
                .ToListAsync();

            throw new XunitException(
                $"PRICE-CON-12 accounting replay did not reprocess the reopened sale outbox row. Pending: {string.Join(",", pendingDiagnostics)}");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            progressEntries.Enqueue($"{worker.Name}:accounting-replay-threw:{DescribeException(ex)}");

            return new AccountingOutboxWorkerResult(
                Worker: worker,
                ProcessedCount: totalProcessed,
                Duration: stopwatch.Elapsed,
                Exception: ex);
        }
    }

    private static async Task<StockSyncWorkerResult> ExecuteStockSyncWorkerAsync(
        StockSyncOutboxProcessor processor,
        string workerName,
        ConcurrentQueue<string> progressEntries)
    {
        progressEntries.Enqueue($"{workerName}:stock-sync-ready");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var processedCount = await processor.ProcessPendingMessagesAsync(batchSize: 1);
            stopwatch.Stop();
            progressEntries.Enqueue($"{workerName}:stock-sync-finished:{processedCount}");

            return new StockSyncWorkerResult(
                WorkerName: workerName,
                ProcessedCount: processedCount,
                Duration: stopwatch.Elapsed,
                Exception: null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            progressEntries.Enqueue($"{workerName}:stock-sync-threw:{DescribeException(ex)}");

            return new StockSyncWorkerResult(
                WorkerName: workerName,
                ProcessedCount: 0,
                Duration: stopwatch.Elapsed,
                Exception: ex);
        }
    }

    private static ServiceProvider BuildAccountingOutboxProcessorServiceProvider(
        string connectionString,
        TestTenantContext tenantContext,
        params IInterceptor[] interceptors)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
        {
            options
                .UseNpgsql(connectionString)
                .EnableDetailedErrors();

            if (interceptors.Length > 0)
            {
                options.AddInterceptors(interceptors);
            }
        });
        services.AddScoped<IFinancialPostingService, FinancialPostingService>();
        services.AddScoped<IAccountResolver, AccountResolver>();
        services.AddScoped<ICashboxLedgerAccountService, CashboxLedgerAccountService>();
        services.AddScoped<ICommerceAccountingService, CommerceAccountingService>();

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static ServiceProvider BuildStockSyncProcessorServiceProvider(
        string connectionString,
        TestTenantContext tenantContext,
        IPublishEndpoint publishEndpoint)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(publishEndpoint);
        services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
        {
            options
                .UseNpgsql(connectionString)
                .EnableDetailedErrors();
        });

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static Mock<IPublishEndpoint> BuildBlockingPublishEndpoint(BlockingPublishWindow publishWindow)
    {
        var publishEndpoint = new Mock<IPublishEndpoint>(MockBehavior.Strict);
        publishEndpoint
            .Setup(endpoint => endpoint.Publish(
                It.IsAny<object>(),
                It.IsAny<Type>(),
                It.IsAny<IPipe<PublishContext>>(),
                It.IsAny<CancellationToken>()))
            .Returns<object, Type, IPipe<PublishContext>, CancellationToken>(async (message, _, _, cancellationToken) =>
            {
                await publishWindow.OnPublishAsync(message, cancellationToken);
            });

        return publishEndpoint;
    }

    private static Mock<IPublishEndpoint> BuildBlockingProjectionPublishEndpoint(
        BlockingPublishWindow publishWindow,
        string connectionString,
        TestTenantContext tenantContext,
        string mongoConnectionString,
        IMongoDatabase mongoDatabase,
        ConcurrentQueue<string> progressEntries)
    {
        var publishEndpoint = new Mock<IPublishEndpoint>(MockBehavior.Strict);
        publishEndpoint
            .Setup(endpoint => endpoint.Publish(
                It.IsAny<object>(),
                It.IsAny<Type>(),
                It.IsAny<IPipe<PublishContext>>(),
                It.IsAny<CancellationToken>()))
            .Returns<object, Type, IPipe<PublishContext>, CancellationToken>(async (message, _, _, cancellationToken) =>
            {
                await publishWindow.OnPublishAsync(message, cancellationToken);

                var stockEvent = Assert.IsType<ProductVariantStockChangedEvent>(message);
                await ApplyStockCon01ProjectionAsync(
                    connectionString,
                    tenantContext,
                    mongoConnectionString,
                    mongoDatabase,
                    stockEvent,
                    progressEntries);
            });

        return publishEndpoint;
    }

    private static async Task<SiCon03StateSnapshot> LoadSiCon03StateSnapshotAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId)
    {
        await using var context = scope.CreateDbContext(tenantContext);

        var customer = await context.Customers.SingleAsync(item => item.Id == seeded.CustomerId);
        var accountingOutboxMessages = await context.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.EventType == "PosSale" &&
                message.BusinessEntityId == invoiceId)
            .OrderBy(message => message.CreatedAt)
            .ToListAsync();
        var stockSyncOutboxMessages = await context.StockSyncOutboxMessages
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.VariantId == seeded.VariantId &&
                message.WarehouseId == seeded.WarehouseId)
            .OrderBy(message => message.CreatedAt)
            .ToListAsync();
        var journalEntries = await context.JournalEntries
            .IgnoreQueryFilters()
            .Where(journal => journal.TenantId == seeded.TenantId && journal.SourceId == invoiceId)
            .OrderBy(journal => journal.CreatedAt)
            .ThenBy(journal => journal.Id)
            .ToListAsync();

        var accountingOutbox = accountingOutboxMessages.SingleOrDefault();
        var stockSyncOutbox = stockSyncOutboxMessages.SingleOrDefault();

        return new SiCon03StateSnapshot(
            SalesInvoices: await context.SalesInvoices.CountAsync(invoice => invoice.TenantId == seeded.TenantId),
            StockQuantity: await context.ProductVariantStocks
                .Where(stock =>
                    stock.TenantId == seeded.TenantId &&
                    stock.WarehouseId == seeded.WarehouseId &&
                    stock.VariantId == seeded.VariantId)
                .Select(stock => stock.Quantity)
                .SingleAsync(),
            StockMovements: await context.StockMovements.CountAsync(movement =>
                movement.TenantId == seeded.TenantId &&
                movement.WarehouseId == seeded.WarehouseId &&
                movement.ProductVariantId == seeded.VariantId &&
                movement.MovementType == StockMovementType.Sale),
            CustomerTransactions: await context.CustomerTransactions.CountAsync(transaction =>
                transaction.TenantId == seeded.TenantId &&
                transaction.CustomerId == seeded.CustomerId),
            AccountLedgers: await context.AccountLedgers.CountAsync(ledger =>
                ledger.TenantId == seeded.TenantId &&
                ledger.PartyType == PartyType.Customer &&
                ledger.PartyId == seeded.CustomerId),
            CustomerBalance: customer.Balance,
            AccountingOutboxMessages: accountingOutboxMessages.Count,
            AccountingOutboxId: accountingOutbox?.Id ?? Guid.Empty,
            AccountingOutboxJournalId: accountingOutbox?.JournalId,
            AccountingOutboxClaimedUntilUtc: accountingOutbox?.ClaimedUntilUtc,
            AccountingOutboxProcessedAtUtc: accountingOutbox?.ProcessedAtUtc,
            StockSyncOutboxMessages: stockSyncOutboxMessages.Count,
            StockSyncOutboxId: stockSyncOutbox?.Id ?? Guid.Empty,
            StockSyncEventId: stockSyncOutbox?.EventId ?? Guid.Empty,
            StockSyncClaimedUntilUtc: stockSyncOutbox?.ClaimedUntilUtc,
            StockSyncPublishedAtUtc: stockSyncOutbox?.PublishedAtUtc,
            StockSyncAttemptCount: stockSyncOutbox?.AttemptCount ?? 0,
            ProcessedEvents: await context.Set<ProcessedEvent>()
                .IgnoreQueryFilters()
                .CountAsync(processed =>
                    processed.TenantId == seeded.TenantId &&
                    !VariantStockProjectionFenceConsumerNames.Contains(processed.ConsumerName)),
            InventoryEventInbox: await context.InventoryEventInbox
                .IgnoreQueryFilters()
                .CountAsync(inbox => inbox.TenantId == seeded.TenantId),
            JournalEntries: journalEntries.Count,
            JournalFingerprint: string.Join(
                "|",
                journalEntries.Select(journal => $"{journal.Id}:{journal.IdempotencyKey}")
                    .OrderBy(value => value, StringComparer.Ordinal)));
    }

    private static async Task ReopenAccountingOutboxMessageForWorkerReplayAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId,
        ConcurrentQueue<string> progressEntries)
    {
        await using var context = scope.CreateDbContext(tenantContext);

        var outboxMessage = await context.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .SingleAsync(message =>
                message.TenantId == seeded.TenantId &&
                message.EventType == "PosSale" &&
                message.BusinessEntityId == invoiceId);

        outboxMessage.ProcessedAtUtc = null;
        outboxMessage.ClaimedUntilUtc = null;
        outboxMessage.IsFailed = false;
        outboxMessage.LastError = null;
        outboxMessage.NextRetryAtUtc = null;
        outboxMessage.AttemptCount = 0;

        await context.SaveChangesAsync();
        progressEntries.Enqueue("setup:reopened-accounting-outbox-row-for-real-worker-overlap");
    }

    private static async Task AssertAccountingWorkerClaimVisibleDuringActiveProcessingAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId,
        SiCon03StateSnapshot canonicalBaseline,
        ConcurrentQueue<string> progressEntries)
    {
        var visibleState = await LoadSiCon03StateSnapshotAsync(scope, tenantContext, seeded, invoiceId);

        Assert.Equal(canonicalBaseline.SalesInvoices, visibleState.SalesInvoices);
        Assert.Equal(canonicalBaseline.StockQuantity, visibleState.StockQuantity);
        Assert.Equal(canonicalBaseline.StockMovements, visibleState.StockMovements);
        Assert.Equal(canonicalBaseline.CustomerTransactions, visibleState.CustomerTransactions);
        Assert.Equal(canonicalBaseline.AccountLedgers, visibleState.AccountLedgers);
        Assert.Equal(canonicalBaseline.CustomerBalance, visibleState.CustomerBalance);
        Assert.Equal(canonicalBaseline.AccountingOutboxMessages, visibleState.AccountingOutboxMessages);
        Assert.Equal(canonicalBaseline.AccountingOutboxId, visibleState.AccountingOutboxId);
        Assert.Equal(canonicalBaseline.AccountingOutboxJournalId, visibleState.AccountingOutboxJournalId);
        Assert.Null(visibleState.AccountingOutboxProcessedAtUtc);
        Assert.NotNull(visibleState.AccountingOutboxClaimedUntilUtc);
        Assert.NotEqual(Guid.Empty, visibleState.StockSyncOutboxId);
        Assert.Equal(canonicalBaseline.StockSyncOutboxMessages, visibleState.StockSyncOutboxMessages);
        Assert.Equal(canonicalBaseline.StockSyncEventId, visibleState.StockSyncEventId);
        Assert.Equal(canonicalBaseline.ProcessedEvents, visibleState.ProcessedEvents);
        Assert.Equal(canonicalBaseline.InventoryEventInbox, visibleState.InventoryEventInbox);
        Assert.Equal(canonicalBaseline.JournalEntries, visibleState.JournalEntries);
        Assert.Equal(canonicalBaseline.JournalFingerprint, visibleState.JournalFingerprint);

        progressEntries.Enqueue("verification:accounting-worker-claim-visible-during-active-processing");
    }

    private static async Task AssertAccountingDuplicateWorkerConvergenceAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId,
        SiCon03StateSnapshot canonicalBaseline,
        AccountingOutboxWorkerResult originalWorker,
        AccountingOutboxWorkerResult duplicateWorker,
        ConcurrentQueue<string> progressEntries)
    {
        var after = await LoadSiCon03StateSnapshotAsync(scope, tenantContext, seeded, invoiceId);

        Assert.Null(originalWorker.Exception);
        Assert.Null(duplicateWorker.Exception);
        Assert.Equal(1, originalWorker.ProcessedCount);
        Assert.Equal(0, duplicateWorker.ProcessedCount);

        Assert.Equal(canonicalBaseline.SalesInvoices, after.SalesInvoices);
        Assert.Equal(canonicalBaseline.StockQuantity, after.StockQuantity);
        Assert.Equal(canonicalBaseline.StockMovements, after.StockMovements);
        Assert.Equal(canonicalBaseline.CustomerTransactions, after.CustomerTransactions);
        Assert.Equal(canonicalBaseline.AccountLedgers, after.AccountLedgers);
        Assert.Equal(canonicalBaseline.CustomerBalance, after.CustomerBalance);
        Assert.Equal(canonicalBaseline.AccountingOutboxMessages, after.AccountingOutboxMessages);
        Assert.Equal(canonicalBaseline.AccountingOutboxId, after.AccountingOutboxId);
        Assert.Equal(canonicalBaseline.AccountingOutboxJournalId, after.AccountingOutboxJournalId);
        Assert.Null(after.AccountingOutboxClaimedUntilUtc);
        Assert.NotNull(after.AccountingOutboxProcessedAtUtc);
        Assert.Equal(canonicalBaseline.StockSyncOutboxMessages, after.StockSyncOutboxMessages);
        Assert.Equal(canonicalBaseline.StockSyncOutboxId, after.StockSyncOutboxId);
        Assert.Equal(canonicalBaseline.StockSyncEventId, after.StockSyncEventId);
        Assert.Equal(canonicalBaseline.ProcessedEvents, after.ProcessedEvents);
        Assert.Equal(canonicalBaseline.InventoryEventInbox, after.InventoryEventInbox);
        Assert.Equal(canonicalBaseline.JournalEntries, after.JournalEntries);
        Assert.Equal(canonicalBaseline.JournalFingerprint, after.JournalFingerprint);

        progressEntries.Enqueue("verification:accounting-overlap-converged");
    }

    private static async Task AssertStockSyncClaimVisibleDuringActivePublishAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId,
        SiCon03StateSnapshot baseline,
        ConcurrentQueue<string> progressEntries)
    {
        var duringPublish = await LoadSiCon03StateSnapshotAsync(scope, tenantContext, seeded, invoiceId);

        Assert.Equal(baseline.SalesInvoices, duringPublish.SalesInvoices);
        Assert.Equal(baseline.StockQuantity, duringPublish.StockQuantity);
        Assert.Equal(baseline.StockMovements, duringPublish.StockMovements);
        Assert.Equal(baseline.CustomerTransactions, duringPublish.CustomerTransactions);
        Assert.Equal(baseline.AccountLedgers, duringPublish.AccountLedgers);
        Assert.Equal(baseline.CustomerBalance, duringPublish.CustomerBalance);
        Assert.Equal(baseline.AccountingOutboxMessages, duringPublish.AccountingOutboxMessages);
        Assert.Equal(baseline.AccountingOutboxId, duringPublish.AccountingOutboxId);
        Assert.Equal(baseline.AccountingOutboxJournalId, duringPublish.AccountingOutboxJournalId);
        Assert.Equal(baseline.ProcessedEvents, duringPublish.ProcessedEvents);
        Assert.Equal(baseline.InventoryEventInbox, duringPublish.InventoryEventInbox);
        Assert.Equal(baseline.JournalEntries, duringPublish.JournalEntries);
        Assert.Equal(baseline.JournalFingerprint, duringPublish.JournalFingerprint);
        Assert.Equal(baseline.StockSyncOutboxId, duringPublish.StockSyncOutboxId);
        Assert.Equal(baseline.StockSyncEventId, duringPublish.StockSyncEventId);
        Assert.Null(duringPublish.StockSyncPublishedAtUtc);
        Assert.NotNull(duringPublish.StockSyncClaimedUntilUtc);

        progressEntries.Enqueue("verification:stock-sync-claim-visible-during-active-publish");
    }

    private static async Task AssertStockSyncDuplicateWorkerConvergenceAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId,
        SiCon03StateSnapshot accountingBaseline,
        StockSyncWorkerResult originalWorker,
        StockSyncWorkerResult duplicateWorker,
        BlockingPublishWindow publishWindow,
        ConcurrentQueue<string> progressEntries)
    {
        Assert.Null(originalWorker.Exception);
        Assert.Null(duplicateWorker.Exception);
        Assert.Equal(1, originalWorker.ProcessedCount);
        Assert.Equal(0, duplicateWorker.ProcessedCount);
        Assert.Equal(1, publishWindow.PublishCount);

        var after = await LoadSiCon03StateSnapshotAsync(scope, tenantContext, seeded, invoiceId);

        Assert.Equal(accountingBaseline.SalesInvoices, after.SalesInvoices);
        Assert.Equal(accountingBaseline.StockQuantity, after.StockQuantity);
        Assert.Equal(accountingBaseline.StockMovements, after.StockMovements);
        Assert.Equal(accountingBaseline.CustomerTransactions, after.CustomerTransactions);
        Assert.Equal(accountingBaseline.AccountLedgers, after.AccountLedgers);
        Assert.Equal(accountingBaseline.CustomerBalance, after.CustomerBalance);
        Assert.Equal(accountingBaseline.AccountingOutboxMessages, after.AccountingOutboxMessages);
        Assert.Equal(accountingBaseline.AccountingOutboxId, after.AccountingOutboxId);
        Assert.Equal(accountingBaseline.AccountingOutboxJournalId, after.AccountingOutboxJournalId);
        Assert.Equal(accountingBaseline.AccountingOutboxProcessedAtUtc, after.AccountingOutboxProcessedAtUtc);
        Assert.Equal(accountingBaseline.ProcessedEvents, after.ProcessedEvents);
        Assert.Equal(accountingBaseline.InventoryEventInbox, after.InventoryEventInbox);
        Assert.Equal(accountingBaseline.JournalEntries, after.JournalEntries);
        Assert.Equal(accountingBaseline.JournalFingerprint, after.JournalFingerprint);

        Assert.Equal(accountingBaseline.StockSyncOutboxMessages, after.StockSyncOutboxMessages);
        Assert.Equal(accountingBaseline.StockSyncOutboxId, after.StockSyncOutboxId);
        Assert.Equal(accountingBaseline.StockSyncEventId, after.StockSyncEventId);
        Assert.NotNull(after.StockSyncPublishedAtUtc);
        Assert.Null(after.StockSyncClaimedUntilUtc);
        Assert.Equal(0, after.StockSyncAttemptCount);

        progressEntries.Enqueue("verification:stock-sync-overlap-converged");
    }

    private static async Task AssertStockCon03AuthoritativeSqlStateAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId,
        bool expectedPublished,
        int expectedAttemptCount,
        ConcurrentQueue<string> progressEntries)
    {
        await using var context = scope.CreateDbContext(tenantContext);

        var stock = await context.ProductVariantStocks
            .IgnoreQueryFilters()
            .SingleAsync(item =>
                item.TenantId == seeded.TenantId &&
                item.WarehouseId == seeded.WarehouseId &&
                item.VariantId == seeded.VariantId);
        var movements = await context.StockMovements
            .IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == seeded.TenantId &&
                item.ProductVariantId == seeded.VariantId &&
                item.WarehouseId == seeded.WarehouseId &&
                item.MovementType == StockMovementType.Sale)
            .ToListAsync();
        var stockSyncOutbox = await context.StockSyncOutboxMessages
            .IgnoreQueryFilters()
            .SingleAsync(item =>
                item.TenantId == seeded.TenantId &&
                item.VariantId == seeded.VariantId &&
                item.WarehouseId == seeded.WarehouseId);
        var invoice = await context.SalesInvoices
            .IgnoreQueryFilters()
            .Include(item => item.Items)
            .SingleAsync(item => item.TenantId == seeded.TenantId && item.Id == invoiceId);

        Assert.Equal(0m, stock.Quantity);
        Assert.Equal(0m, stock.ReservedQuantity);
        Assert.Equal(0m, stock.AvailableQuantity);
        var movement = Assert.Single(movements);
        Assert.Equal(-StockCon01SaleQuantity, movement.QuantityChange);
        Assert.Equal(0m, movement.QuantityAfter);
        Assert.Equal(invoiceId, movement.ReferenceId);
        Assert.Single(invoice.Items);
        Assert.Equal(expectedPublished, stockSyncOutbox.PublishedAtUtc.HasValue);
        Assert.Equal(expectedAttemptCount, stockSyncOutbox.AttemptCount);
        Assert.False(stockSyncOutbox.IsFailed);

        Assert.Equal(0, await context.InventoryReservations
            .IgnoreQueryFilters()
            .CountAsync(item => item.TenantId == seeded.TenantId));
        Assert.Equal(0, await context.InventoryEventInbox
            .IgnoreQueryFilters()
            .CountAsync(item => item.TenantId == seeded.TenantId));
        Assert.Equal(0, await context.Set<ProcessedEvent>()
            .IgnoreQueryFilters()
            .CountAsync(item =>
                item.TenantId == seeded.TenantId &&
                !VariantStockProjectionFenceConsumerNames.Contains(item.ConsumerName)));

        progressEntries.Enqueue("verification:stock-con-03-sql-authority-stable");
    }

    private static async Task AssertStockCon03NoPartialProjectionVisibilityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        SeededScenario seeded,
        StockCon03ProjectionVisibilitySnapshot expectedVisibility,
        ConcurrentQueue<string> progressEntries)
    {
        await using var context = scope.CreateDbContext(tenantContext);

        Assert.Equal(0m, await context.ProductVariantStocks
            .IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == seeded.TenantId &&
                item.WarehouseId == seeded.WarehouseId &&
                item.VariantId == seeded.VariantId)
            .Select(item => item.Quantity)
            .SingleAsync());
        Assert.Equal(1, await context.StockMovements
            .IgnoreQueryFilters()
            .CountAsync(item =>
                item.TenantId == seeded.TenantId &&
                item.ProductVariantId == seeded.VariantId &&
                item.WarehouseId == seeded.WarehouseId &&
                item.MovementType == StockMovementType.Sale));

        var actualVisibility = await LoadStockCon03ProjectionVisibilityAsync(mongoDatabase, seeded);
        Assert.Equal(expectedVisibility.AuthoritativeFingerprint, actualVisibility.AuthoritativeFingerprint);
        Assert.Equal(0m, actualVisibility.StorefrontProductStockQuantity);
        Assert.Equal(0m, actualVisibility.StorefrontVariantStockQuantity);
        Assert.Equal(0m, actualVisibility.CommerceHubAvailableStock);
        Assert.False(actualVisibility.StorefrontProductInStock);
        Assert.False(actualVisibility.StorefrontVariantInStock);
        Assert.False(actualVisibility.CommerceHubInStock);
        Assert.False(actualVisibility.AffiliateVisible);

        progressEntries.Enqueue("verification:stock-con-03-no-partial-projection-visibility");
    }

    private static async Task AssertStockCon03DuplicateWorkerConvergenceAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        SeededScenario seeded,
        Guid invoiceId,
        ProductVariantStockChangedEvent expectedEvent,
        StockSyncWorkerResult originalWorker,
        StockSyncWorkerResult duplicateWorker,
        BlockingPublishWindow publishWindow,
        ConcurrentQueue<string> progressEntries)
    {
        Assert.Null(originalWorker.Exception);
        Assert.Null(duplicateWorker.Exception);
        Assert.Equal(1, originalWorker.ProcessedCount);
        Assert.Equal(0, duplicateWorker.ProcessedCount);
        Assert.Equal(1, publishWindow.PublishCount);

        await AssertStockCon03AuthoritativeSqlStateAsync(
            scope,
            tenantContext,
            seeded,
            invoiceId,
            expectedPublished: true,
            expectedAttemptCount: 0,
            progressEntries);
        await AssertStockCon03ProjectionVisibilityAsync(
            mongoDatabase,
            seeded,
            expectedStock: 0m,
            expectedEvent,
            progressEntries);

        progressEntries.Enqueue("verification:stock-con-03-duplicate-worker-overlap-converged");
    }

    private static SalesController CreateController(
        ApplicationDbContext context,
        TestTenantContext tenantContext,
        SaleActor actor,
        ILogger<SalesController> logger)
    {
        var auditService = new NoOpAuditService();
        var ledgerService = new LedgerService(
            context,
            tenantContext,
            NullLogger<LedgerService>.Instance,
            auditService);
        var postingService = new FinancialPostingService(
            context,
            NullLogger<FinancialPostingService>.Instance);
        var accountResolver = new AccountResolver(
            context,
            NullLogger<AccountResolver>.Instance);
        var cashboxLedgerAccountService = new CashboxLedgerAccountService(
            context,
            NullLogger<CashboxLedgerAccountService>.Instance);
        var commerceAccounting = new CommerceAccountingService(
            postingService,
            accountResolver,
            cashboxLedgerAccountService,
            NullLogger<CommerceAccountingService>.Instance);
        var accountingOutbox = new AccountingOutboxService(
            context,
            new FixedCorrelationIdAccessor(actor.TraceIdentifier),
            NullLogger<AccountingOutboxService>.Instance);

        var controller = new SalesController(
            context,
            tenantContext,
            logger,
            ledgerService,
            ledgerService,
            Mock.Of<ISaleFinancialIntegrityService>(),
            Mock.Of<IValidationService>(),
            auditService,
            new NoOpEventPublisher(),
            accountingOutbox,
            commerceAccounting,
            postingService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext(actor)
            }
        };

        controller.Request.Headers["X-Payment-Attempt-Id"] = actor.RequestId;
        controller.Request.Headers["User-Agent"] = "SI-CON-01";

        return controller;
    }

    private static DefaultHttpContext BuildHttpContext(SaleActor actor)
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

    private static CreateSaleRequest BuildCreateSaleRequest(SeededScenario seeded)
    {
        return new CreateSaleRequest
        {
            CustomerId = seeded.CustomerId,
            WarehouseId = seeded.WarehouseId,
            PaymentMethod = PointOfSale.Core.Enums.PaymentMethod.Cash,
            PaidAmount = 20m,
            CashAmount = 20m,
            CardAmount = 0m,
            Notes = "SI-CON-01 contested stock sale",
            Items =
            [
                new SaleItemRequest
                {
                    ProductId = seeded.ProductId,
                    ProductVariantId = seeded.VariantId,
                    UnitId = seeded.UnitId,
                    Quantity = ContestedSaleQuantity,
                    UnitPrice = AuthoritativeUnitPrice,
                    DiscountAmount = 0m,
                    TaxRate = 0m
                }
            ]
        };
    }

    private static CreateSaleRequest BuildStockCon01CreateSaleRequest(SeededScenario seeded)
    {
        return new CreateSaleRequest
        {
            CustomerId = seeded.CustomerId,
            WarehouseId = seeded.WarehouseId,
            PaymentMethod = PointOfSale.Core.Enums.PaymentMethod.Cash,
            PaidAmount = AuthoritativeUnitPrice,
            CashAmount = AuthoritativeUnitPrice,
            CardAmount = 0m,
            Notes = "STOCK-CON-01 last-unit stock authority sale",
            Items =
            [
                new SaleItemRequest
                {
                    ProductId = seeded.ProductId,
                    ProductVariantId = seeded.VariantId,
                    UnitId = seeded.UnitId,
                    Quantity = StockCon01SaleQuantity,
                    UnitPrice = AuthoritativeUnitPrice,
                    DiscountAmount = 0m,
                    TaxRate = 0m
                }
            ]
        };
    }

    private static CreateSaleRequest BuildStockCon02CreateSaleRequest(SeededScenario seeded)
    {
        return new CreateSaleRequest
        {
            CustomerId = seeded.CustomerId,
            WarehouseId = seeded.WarehouseId,
            PaymentMethod = PointOfSale.Core.Enums.PaymentMethod.Cash,
            PaidAmount = AuthoritativeUnitPrice,
            CashAmount = AuthoritativeUnitPrice,
            CardAmount = 0m,
            Notes = "STOCK-CON-02 replay during active stock mutation",
            Items =
            [
                new SaleItemRequest
                {
                    ProductId = seeded.ProductId,
                    ProductVariantId = seeded.VariantId,
                    UnitId = seeded.UnitId,
                    Quantity = StockCon01SaleQuantity,
                    UnitPrice = AuthoritativeUnitPrice,
                    DiscountAmount = 0m,
                    TaxRate = 0m
                }
            ]
        };
    }

    private static CreateSalesReturnRequest BuildStockCon04SalesReturnRequest(
        SeededScenario seeded,
        Guid invoiceId)
    {
        return new CreateSalesReturnRequest
        {
            CustomerId = seeded.CustomerId,
            WarehouseId = seeded.WarehouseId,
            ReturnDate = SeededUtc.AddHours(1),
            OriginalInvoiceId = invoiceId,
            RefundMethod = RefundMethod.AccountCredit,
            CashAmount = 0m,
            CardAmount = 0m,
            Reason = "STOCK-CON-04 replay-safe stock restoration",
            Notes = "stock-con-04-shared-return-payload",
            Items =
            [
                new SalesReturnItemRequest
                {
                    ProductId = seeded.ProductId,
                    ProductVariantId = seeded.VariantId,
                    UnitId = seeded.UnitId,
                    Quantity = StockCon01SaleQuantity,
                    UnitPrice = AuthoritativeUnitPrice,
                    TaxRate = 0m,
                    Reason = "STOCK-CON-04 deterministic restoration"
                }
            ]
        };
    }

    private static CreateSalesReturnRequest BuildStockCon07SalesReturnRequest(
        SeededScenario seeded,
        Guid invoiceId)
    {
        return new CreateSalesReturnRequest
        {
            CustomerId = seeded.CustomerId,
            WarehouseId = seeded.WarehouseId,
            ReturnDate = SeededUtc.AddHours(1),
            OriginalInvoiceId = invoiceId,
            RefundMethod = RefundMethod.AccountCredit,
            CashAmount = 0m,
            CardAmount = 0m,
            Reason = "STOCK-CON-07 concurrent historical return boundary",
            Notes = "stock-con-07-overlapping-return-payload",
            Items =
            [
                new SalesReturnItemRequest
                {
                    ProductId = seeded.ProductId,
                    ProductVariantId = seeded.VariantId,
                    UnitId = seeded.UnitId,
                    Quantity = StockCon01SaleQuantity,
                    UnitPrice = AuthoritativeUnitPrice,
                    TaxRate = 0m,
                    Reason = "STOCK-CON-07 same sold quantity overlap"
                }
            ]
        };
    }

    private static CreateSaleRequest BuildPriceCon11CreateSaleRequest(SeededScenario seeded)
    {
        return new CreateSaleRequest
        {
            CustomerId = seeded.CustomerId,
            WarehouseId = seeded.WarehouseId,
            PaymentMethod = PointOfSale.Core.Enums.PaymentMethod.Cash,
            PaidAmount = PriceCon11ExpectedSaleAmount,
            CashAmount = PriceCon11ExpectedSaleAmount,
            CardAmount = 0m,
            Notes = "PRICE-CON-11 cost mutation overlap sale",
            Items =
            [
                new SaleItemRequest
                {
                    ProductId = seeded.ProductId,
                    ProductVariantId = seeded.VariantId,
                    UnitId = seeded.UnitId,
                    Quantity = ContestedSaleQuantity,
                    UnitPrice = PriceCon11SaleUnitPrice,
                    DiscountAmount = 0m,
                    TaxRate = 0m
                }
            ]
        };
    }

    private static async Task<VariantCostMutationResult> ExecuteVariantCostMutationActorAsync(
        string connectionString,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        SaleActor actor,
        decimal newCostPrice,
        ConcurrentQueue<string> progressEntries,
        params IInterceptor[] interceptors)
    {
        await using var context = CreateDbContext(connectionString, tenantContext, interceptors);
        await ConfigureSessionTimeoutsAsync(context);

        var variant = await context.ProductVariants
            .AsNoTracking()
            .SingleAsync(item => item.Id == seeded.VariantId);

        var request = new UpdateVariantRequest
        {
            SKU = variant.SKU,
            Barcode = variant.Barcode,
            Price = variant.Price,
            OnlinePrice = variant.OnlinePrice,
            CostPrice = newCostPrice,
            IsActive = variant.IsActive,
            MediaId = variant.MediaId,
            SortOrder = variant.SortOrder,
            Attributes = []
        };

        var controller = CreateProductVariantController(context, tenantContext, actor);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            progressEntries.Enqueue($"{actor.Name}:invoking-product-variant-cost-update");
            var result = await controller.UpdateVariant(seeded.ProductId, seeded.VariantId, request);
            stopwatch.Stop();

            if (result.Result is OkObjectResult { Value: ProductVariantDto dto })
            {
                progressEntries.Enqueue($"{actor.Name}:cost-update-completed:{dto.CostPrice}");
                return new VariantCostMutationResult(
                    actor,
                    Succeeded: true,
                    StatusCode: StatusCodes.Status200OK,
                    CostPrice: dto.CostPrice,
                    PayloadJson: JsonSerializer.Serialize(dto),
                    Duration: stopwatch.Elapsed,
                    Exception: null);
            }

            if (result.Result is ObjectResult objectResult)
            {
                var payloadJson = objectResult.Value == null
                    ? null
                    : JsonSerializer.Serialize(objectResult.Value);
                progressEntries.Enqueue(
                    $"{actor.Name}:cost-update-status:{objectResult.StatusCode ?? StatusCodes.Status500InternalServerError}:{payloadJson}");

                return new VariantCostMutationResult(
                    actor,
                    Succeeded: false,
                    StatusCode: objectResult.StatusCode,
                    CostPrice: null,
                    PayloadJson: payloadJson,
                    Duration: stopwatch.Elapsed,
                    Exception: null);
            }

            throw new XunitException(
                $"Actor {actor.Name} returned an unexpected variant update result shape: {result.Result?.GetType().Name ?? "null"}");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            progressEntries.Enqueue($"{actor.Name}:cost-update-threw:{DescribeException(ex)}");

            return new VariantCostMutationResult(
                actor,
                Succeeded: false,
                StatusCode: null,
                CostPrice: null,
                PayloadJson: null,
                Duration: stopwatch.Elapsed,
                Exception: ex);
        }
    }

    private static async Task<PriceCon12ReturnActorResult> ExecuteSalesReturnActorAsync(
        string connectionString,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId,
        SaleActor actor,
        ConcurrentQueue<string> progressEntries,
        params IInterceptor[] interceptors)
    {
        var request = new CreateSalesReturnRequest
        {
            CustomerId = seeded.CustomerId,
            WarehouseId = seeded.WarehouseId,
            ReturnDate = SeededUtc.AddHours(1),
            OriginalInvoiceId = invoiceId,
            RefundMethod = RefundMethod.AccountCredit,
            CashAmount = 0m,
            CardAmount = 0m,
            Reason = "PRICE-CON-12 return cost snapshot inheritance",
            Notes = actor.Name,
            Items =
            [
                new SalesReturnItemRequest
                {
                    ProductId = seeded.ProductId,
                    ProductVariantId = seeded.VariantId,
                    UnitId = seeded.UnitId,
                    Quantity = PriceCon12ReturnQuantity,
                    UnitPrice = PriceCon11SaleUnitPrice,
                    TaxRate = 0m,
                    Reason = "PRICE-CON-12 deterministic return"
                }
            ]
        };

        return await ExecuteSalesReturnRequestActorAsync(
            connectionString,
            tenantContext,
            actor,
            request,
            progressEntries,
            interceptors);
    }

    private static async Task<PriceCon12ReturnActorResult> ExecuteSalesReturnRequestActorAsync(
        string connectionString,
        TestTenantContext tenantContext,
        SaleActor actor,
        CreateSalesReturnRequest request,
        ConcurrentQueue<string> progressEntries,
        params IInterceptor[] interceptors)
    {
        await using var context = CreateDbContext(connectionString, tenantContext, interceptors);
        await ConfigureSessionTimeoutsAsync(context);

        var controller = CreateSalesReturnsController(context, tenantContext, actor);
        controller.Request.Headers["Idempotency-Key"] = actor.RequestId;
        controller.Request.Headers["User-Agent"] = actor.TraceIdentifier;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            progressEntries.Enqueue($"{actor.Name}:invoking-sales-return");
            var result = await controller.CreateSalesReturn(request);
            stopwatch.Stop();

            if (result.Result is OkObjectResult { Value: SalesReturnResponse response })
            {
                progressEntries.Enqueue($"{actor.Name}:return-committed:{response.ReturnId}");
                return new PriceCon12ReturnActorResult(
                    actor,
                    Succeeded: true,
                    StatusCode: StatusCodes.Status200OK,
                    Response: response,
                    PayloadJson: JsonSerializer.Serialize(response),
                    Duration: stopwatch.Elapsed,
                    Exception: null);
            }

            if (result.Result is ObjectResult objectResult)
            {
                var payloadJson = objectResult.Value == null
                    ? null
                    : JsonSerializer.Serialize(objectResult.Value);

                if ((objectResult.StatusCode ?? StatusCodes.Status200OK) == StatusCodes.Status200OK &&
                    TryParseStoredSalesReturnResponse(payloadJson, out var storedResponse))
                {
                    progressEntries.Enqueue($"{actor.Name}:return-committed-stored:{storedResponse.ReturnId}");
                    return new PriceCon12ReturnActorResult(
                        actor,
                        Succeeded: true,
                        StatusCode: StatusCodes.Status200OK,
                        Response: storedResponse,
                        PayloadJson: payloadJson,
                        Duration: stopwatch.Elapsed,
                        Exception: null);
                }

                progressEntries.Enqueue($"{actor.Name}:return-status:{objectResult.StatusCode}:{payloadJson}");
                return new PriceCon12ReturnActorResult(
                    actor,
                    Succeeded: false,
                    StatusCode: objectResult.StatusCode,
                    Response: null,
                    PayloadJson: payloadJson,
                    Duration: stopwatch.Elapsed,
                    Exception: null);
            }

            throw new XunitException(
                $"Actor {actor.Name} returned an unexpected sales-return result shape: {result.Result?.GetType().Name ?? "null"}");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            progressEntries.Enqueue($"{actor.Name}:return-threw:{DescribeException(ex)}");

            return new PriceCon12ReturnActorResult(
                actor,
                Succeeded: false,
                StatusCode: null,
                Response: null,
                PayloadJson: null,
                Duration: stopwatch.Elapsed,
                Exception: ex);
        }
    }

    private static bool TryParseStoredSalesReturnResponse(string? payloadJson, out SalesReturnResponse response)
    {
        response = default!;

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<SalesReturnResponse>(payloadJson, CaseInsensitiveJsonOptions);
            if (parsed?.ReturnId is { } returnId && returnId != Guid.Empty)
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

    private static async Task<CostBasisRecalculationWorkerResult> ExecuteCostBasisRecalculationWorkerAsync(
        string connectionString,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId,
        Guid returnId,
        string workerName,
        ConcurrentQueue<string> progressEntries,
        params IInterceptor[] interceptors)
    {
        await using var context = CreateDbContext(connectionString, tenantContext, interceptors);
        await ConfigureSessionTimeoutsAsync(context);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            progressEntries.Enqueue($"{workerName}:recalculation-ready");
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

            var derived = await CalculatePriceCon12DerivedValuationAsync(context, seeded);
            if (derived.StockQuantity <= 0m ||
                derived.WeightedAverageMovementCost != PriceCon11InitialCostPrice)
            {
                throw new InvalidOperationException(
                    $"Unexpected PRICE-CON-12 weighted valuation. Quantity={derived.StockQuantity}, Cost={derived.WeightedAverageMovementCost}.");
            }

            var nowUtc = DateTime.UtcNow;
            var processedId = Guid.NewGuid();
            var inserted = await context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ""ProcessedEvents""
                    (""Id"", ""EventId"", ""ConsumerName"", ""EventType"", ""EntityId"", ""TenantId"", ""CorrelationId"", ""ProcessedAtUtc"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                VALUES
                    ({processedId}, {PriceCon12RecalculationEventId}, {"PRICE-CON-12-CostBasisRecalculation"}, {"CostBasisRecalculated"}, {seeded.VariantId}, {seeded.TenantId}, {"price-con-12-replay"}, {nowUtc}, {nowUtc}, NULL, FALSE)
                ON CONFLICT (""EventId"", ""ConsumerName"", ""EventType"") WHERE ""IsDeleted"" = FALSE DO NOTHING;");

            var variant = await context.ProductVariants
                .FromSqlInterpolated($@"
                    SELECT *, xmin FROM ""ProductVariants""
                    WHERE ""TenantId"" = {seeded.TenantId}
                      AND ""Id"" = {seeded.VariantId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .SingleAsync();

            // Recalculation workers may observe current future-cost authority, but they
            // must not rewrite it from historical movement valuation.
            Assert.Equal(PriceCon11MutatedCostPrice, variant.CostPrice);

            await transaction.CommitAsync();
            stopwatch.Stop();
            progressEntries.Enqueue($"{workerName}:recalculation-committed:inserted={inserted}");

            return new CostBasisRecalculationWorkerResult(
                workerName,
                InsertedProcessedEvent: inserted == 1,
                WeightedAverageMovementCost: derived.WeightedAverageMovementCost,
                WeightedMovementInventoryValue: derived.WeightedMovementInventoryValue,
                Duration: stopwatch.Elapsed,
                Exception: null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            progressEntries.Enqueue($"{workerName}:recalculation-threw:{DescribeException(ex)}");

            return new CostBasisRecalculationWorkerResult(
                workerName,
                InsertedProcessedEvent: false,
                WeightedAverageMovementCost: 0m,
                WeightedMovementInventoryValue: 0m,
                Duration: stopwatch.Elapsed,
                Exception: ex);
        }
    }

    private static ProductVariantsController CreateProductVariantController(
        ApplicationDbContext context,
        TestTenantContext tenantContext,
        SaleActor actor)
    {
        var productVariantService = new Mock<IProductVariantService>();
        productVariantService
            .Setup(service => service.SyncSimpleProductFromDefaultVariantAsync(
                It.IsAny<ProductVariant>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SimpleProductMirrorSyncResult?)null);

        var marketplaceOutbox = new Mock<IMarketplaceEventOutboxService>();

        var controller = new ProductVariantsController(
            context,
            tenantContext,
            productVariantService.Object,
            new NoOpEventPublisher(),
            marketplaceOutbox.Object,
            NullLogger<ProductVariantsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext(actor)
            }
        };

        controller.Request.Headers["Idempotency-Key"] = actor.RequestId;
        controller.Request.Headers["X-Request-Id"] = actor.RequestId;

        return controller;
    }

    private static SalesReturnsController CreateSalesReturnsController(
        ApplicationDbContext context,
        TestTenantContext tenantContext,
        SaleActor actor)
    {
        var ledgerService = new LedgerService(
            context,
            tenantContext,
            NullLogger<LedgerService>.Instance,
            new NoOpAuditService());
        var postingService = new FinancialPostingService(
            context,
            NullLogger<FinancialPostingService>.Instance);
        var accountResolver = new AccountResolver(
            context,
            NullLogger<AccountResolver>.Instance);
        var cashboxLedgerAccountService = new CashboxLedgerAccountService(
            context,
            NullLogger<CashboxLedgerAccountService>.Instance);
        var commerceAccounting = new CommerceAccountingService(
            postingService,
            accountResolver,
            cashboxLedgerAccountService,
            NullLogger<CommerceAccountingService>.Instance);
        var accountingOutbox = new AccountingOutboxService(
            context,
            new FixedCorrelationIdAccessor(actor.TraceIdentifier),
            NullLogger<AccountingOutboxService>.Instance);

        return new SalesReturnsController(
            context,
            tenantContext,
            NullLogger<SalesReturnsController>.Instance,
            ledgerService,
            new NoOpEventPublisher(),
            accountingOutbox,
            commerceAccounting)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext(actor)
            }
        };
    }

    private static SaleActorResult ParseActorResult(
        SaleActor actor,
        ActionResult<SaleResponse> actionResult,
        TimeSpan duration,
        ConcurrentQueue<string> progressEntries)
    {
        if (actionResult.Result is OkObjectResult { Value: SaleResponse response })
        {
            progressEntries.Enqueue($"{actor.Name}:completed:{response.InvoiceId}");
            return new SaleActorResult(
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
                TryParseStoredSaleResponse(payloadJson, out var storedResponse))
            {
                progressEntries.Enqueue($"{actor.Name}:completed-stored:{storedResponse.InvoiceId}");
                return new SaleActorResult(
                    actor,
                    Succeeded: true,
                    StatusCode: StatusCodes.Status200OK,
                    Response: storedResponse,
                    PayloadJson: payloadJson,
                    Duration: duration,
                    Exception: null);
            }

            progressEntries.Enqueue(
                $"{actor.Name}:status:{objectResult.StatusCode ?? StatusCodes.Status500InternalServerError}:" +
                $"{payloadJson}");
            return new SaleActorResult(
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
            return new SaleActorResult(
                actor,
                Succeeded: false,
                StatusCode: statusCodeResult.StatusCode,
                Response: null,
                PayloadJson: null,
                Duration: duration,
                Exception: null);
        }

        if (actionResult.Value is SaleResponse directResponse)
        {
            progressEntries.Enqueue($"{actor.Name}:completed:{directResponse.InvoiceId}");
            return new SaleActorResult(
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

    private static bool TryParseStoredSaleResponse(string? payloadJson, out SaleResponse response)
    {
        response = default!;

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<SaleResponse>(payloadJson, CaseInsensitiveJsonOptions);
            if (parsed?.InvoiceId is { } invoiceId && invoiceId != Guid.Empty)
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

    private static async Task AssertAuthoritativeConvergenceAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        SaleActor actorA,
        SaleActor actorB,
        IReadOnlyCollection<SaleActorResult> results,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var persistedInvoices = await verifyContext.SalesInvoices
            .Where(invoice => invoice.TenantId == seeded.TenantId)
            .OrderBy(invoice => invoice.CreatedAt)
            .ToListAsync();
        var persistedStocks = await verifyContext.ProductVariantStocks
            .Where(stock =>
                stock.TenantId == seeded.TenantId &&
                stock.WarehouseId == seeded.WarehouseId &&
                stock.VariantId == seeded.VariantId)
            .ToListAsync();
        var persistedMovements = await verifyContext.StockMovements
            .Where(movement =>
                movement.TenantId == seeded.TenantId &&
                movement.ProductVariantId == seeded.VariantId &&
                movement.WarehouseId == seeded.WarehouseId &&
                movement.MovementType == StockMovementType.Sale)
            .OrderBy(movement => movement.CreatedAt)
            .ThenBy(movement => movement.Id)
            .ToListAsync();
        var accountingOutboxMessages = await verifyContext.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .Where(message => message.TenantId == seeded.TenantId)
            .OrderBy(message => message.CreatedAt)
            .ToListAsync();
        var customerTransactions = await verifyContext.CustomerTransactions
            .Where(transaction =>
                transaction.TenantId == seeded.TenantId &&
                transaction.CustomerId == seeded.CustomerId)
            .OrderBy(transaction => transaction.CreatedAt)
            .ThenBy(transaction => transaction.Id)
            .ToListAsync();
        var accountLedgers = await verifyContext.AccountLedgers
            .Where(ledger =>
                ledger.TenantId == seeded.TenantId &&
                ledger.PartyType == PartyType.Customer &&
                ledger.PartyId == seeded.CustomerId)
            .OrderBy(ledger => ledger.CreatedAt)
            .ThenBy(ledger => ledger.Id)
            .ToListAsync();
        var journalEntries = await verifyContext.JournalEntries
            .IgnoreQueryFilters()
            .Include(journal => journal.LedgerEntries)
            .Where(journal => journal.TenantId == seeded.TenantId)
            .OrderBy(journal => journal.CreatedAt)
            .ToListAsync();
        var idempotencyRecords = await verifyContext.SalesRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .Where(record => record.TenantId == seeded.TenantId && record.Operation == "CREATE")
            .OrderBy(record => record.CreatedAt)
            .ToListAsync();
        var customer = await verifyContext.Customers.SingleAsync(item => item.Id == seeded.CustomerId);

        Assert.Single(persistedStocks);
        var stock = persistedStocks.Single();
        Assert.True(stock.Quantity >= 0m, $"Stock went negative. Progress: {string.Join(" | ", progressEntries)}");

        var successes = results.Where(result => result.Succeeded).ToList();
        var failures = results.Where(result => !result.Succeeded).ToList();

        Assert.True(
            successes.Count <= 1,
            $"Unsafe convergence: {successes.Count} invoices committed. Progress: {string.Join(" | ", progressEntries)}");

        if (successes.Count == 1)
        {
            var winner = successes.Single();
            var loser = failures.SingleOrDefault()
                ?? throw new XunitException("Expected one losing actor when one actor committed.");

            Assert.Equal(1m, stock.Quantity);
            Assert.Equal(1, persistedInvoices.Count);
            Assert.Equal(1, persistedMovements.Count);
            Assert.Equal(1, accountingOutboxMessages.Count);
            Assert.Equal(2, customerTransactions.Count);
            Assert.Equal(2, accountLedgers.Count);
            Assert.Single(idempotencyRecords);

            var invoice = persistedInvoices.Single();
            var movement = persistedMovements.Single();
            var outboxMessage = accountingOutboxMessages.Single();
            var winnerRecord = idempotencyRecords.Single();

            Assert.Equal(winner.Response!.InvoiceId, invoice.Id);
            Assert.Equal(seeded.TenantId, invoice.TenantId);
            Assert.Equal(seeded.CustomerId, invoice.CustomerId);
            Assert.Equal(seeded.WarehouseId, invoice.WarehouseId);
            Assert.Equal(20m, invoice.TotalAmount);
            Assert.Equal(20m, invoice.PaidAmount);
            Assert.Equal(PointOfSale.Core.Enums.PaymentMethod.Cash, invoice.PaymentMethod);

            Assert.Equal(-ContestedSaleQuantity, movement.QuantityChange);
            Assert.Equal(1m, movement.QuantityAfter);
            Assert.Equal(1m, movement.AvailableAfter);
            Assert.Equal(invoice.Id, movement.ReferenceId);
            Assert.Equal(invoice.InvoiceNumber, movement.ReferenceNumber);
            Assert.Equal(seeded.VariantId, movement.ProductVariantId);

            Assert.Equal("PosSale", outboxMessage.EventType);
            Assert.Equal("SalesInvoice", outboxMessage.SourceType);
            Assert.Equal(invoice.Id, outboxMessage.BusinessEntityId);
            Assert.Equal($"SALE:{seeded.TenantId}:{invoice.Id}", outboxMessage.IdempotencyKey);
            Assert.Equal(invoice.AccountingMutationVersion, outboxMessage.AggregateVersion);
            Assert.False(outboxMessage.IsFailed);
            Assert.NotNull(outboxMessage.ProcessedAtUtc);
            Assert.NotNull(outboxMessage.JournalId);
            Assert.True(string.IsNullOrWhiteSpace(outboxMessage.LastError));

            using (var payload = JsonDocument.Parse(outboxMessage.PayloadJson))
            {
                Assert.Equal(invoice.Id, payload.RootElement.GetProperty("InvoiceId").GetGuid());
                Assert.Equal(invoice.AccountingMutationVersion, payload.RootElement.GetProperty("MutationVersion").GetInt32());
            }

            Assert.All(customerTransactions, transaction => Assert.Equal(invoice.Id, transaction.ReferenceId));
            Assert.Contains(customerTransactions, transaction =>
                transaction.TransactionType == TransactionType.Sale &&
                transaction.DebitAmount == 20m &&
                transaction.CreditAmount == 0m);
            Assert.Contains(customerTransactions, transaction =>
                transaction.TransactionType == TransactionType.PaymentReceived &&
                transaction.DebitAmount == 0m &&
                transaction.CreditAmount == 20m);

            Assert.All(accountLedgers, ledger => Assert.Equal(invoice.Id, ledger.ReferenceId));
            Assert.Contains(accountLedgers, ledger =>
                ledger.TransactionType == TransactionType.Sale &&
                ledger.DebitAmount == 20m &&
                ledger.CreditAmount == 0m);
            Assert.Contains(accountLedgers, ledger =>
                ledger.TransactionType == TransactionType.PaymentReceived &&
                ledger.DebitAmount == 0m &&
                ledger.CreditAmount == 20m);

            Assert.Equal(0m, customer.Balance);

            Assert.NotEmpty(journalEntries);
            Assert.All(journalEntries, journal => Assert.Equal(invoice.Id, journal.SourceId));
            Assert.Contains(journalEntries, journal => journal.Id == outboxMessage.JournalId);
            Assert.Equal(
                journalEntries.Count,
                journalEntries.Select(journal => journal.IdempotencyKey).Distinct(StringComparer.Ordinal).Count());

            Assert.Equal(winner.Actor.RequestId, winnerRecord.Key);
            Assert.NotNull(winnerRecord.CompletedAtUtc);
            Assert.Equal(StatusCodes.Status200OK, winnerRecord.ResponseStatusCode);
            Assert.DoesNotContain(idempotencyRecords, record => record.Key == loser.Actor.RequestId);

            await AssertReplaySafetyAsync(
                scope,
                tenantContext,
                seeded,
                winner.Actor,
                winner.Response.InvoiceId,
                expectedSuccess: true);
            await AssertReplaySafetyAsync(
                scope,
                tenantContext,
                seeded,
                loser.Actor,
                expectedInvoiceId: null,
                expectedSuccess: false);
        }
        else
        {
            Assert.Equal(0, persistedInvoices.Count);
            Assert.Equal(InitialStockQuantity, stock.Quantity);
            Assert.Empty(persistedMovements);
            Assert.Empty(accountingOutboxMessages);
            Assert.Empty(customerTransactions);
            Assert.Empty(accountLedgers);
            Assert.Empty(journalEntries);
            Assert.Empty(idempotencyRecords);
            Assert.Equal(0m, customer.Balance);

            await AssertReplaySafetyAsync(
                scope,
                tenantContext,
                seeded,
                actorA,
                expectedInvoiceId: null,
                expectedSuccess: false);
            await AssertReplaySafetyAsync(
                scope,
                tenantContext,
                seeded,
                actorB,
                expectedInvoiceId: null,
                expectedSuccess: false);
        }
    }

    private static async Task AssertReplaySafetyAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        SaleActor actor,
        Guid? expectedInvoiceId,
        bool expectedSuccess)
    {
        var before = await SnapshotDurableStateAsync(scope, tenantContext, seeded);

        await using var replayContext = scope.CreateDbContext(tenantContext);
        await ConfigureSessionTimeoutsAsync(replayContext);
        var replayController = CreateController(
            replayContext,
            tenantContext,
            actor,
            NullLogger<SalesController>.Instance);
        var replayRequest = BuildCreateSaleRequest(seeded);

        var replayResult = await replayController.CreateSale(replayRequest, paymentAttemptId: actor.RequestId);

        if (expectedSuccess)
        {
            SaleResponse response;
            if (replayResult.Result is OkObjectResult ok)
            {
                response = Assert.IsType<SaleResponse>(ok.Value);
            }
            else
            {
                var stored = Assert.IsType<ObjectResult>(replayResult.Result);
                Assert.Equal(StatusCodes.Status200OK, stored.StatusCode ?? StatusCodes.Status200OK);
                var storedJson = stored.Value == null ? null : JsonSerializer.Serialize(stored.Value);
                Assert.True(
                    TryParseStoredSaleResponse(storedJson, out response),
                    $"Expected a stored sale replay response but received: {storedJson}");
            }

            Assert.Equal(expectedInvoiceId, response.InvoiceId);
        }
        else
        {
            Assert.False(
                replayResult.Result is OkObjectResult,
                $"Replay for actor {actor.Name} unexpectedly succeeded.");
        }

        var after = await SnapshotDurableStateAsync(scope, tenantContext, seeded);
        Assert.Equal(before, after);
    }

    private static async Task AssertNoUncommittedDurableStateVisibleAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        ConcurrentQueue<string> progressEntries)
    {
        var snapshot = await SnapshotDurableStateAsync(scope, tenantContext, seeded);

        Assert.Equal(0, snapshot.SalesInvoices);
        Assert.Equal(InitialStockQuantity, snapshot.StockQuantity);
        Assert.Equal(0, snapshot.StockMovements);
        Assert.Equal(0, snapshot.AccountingOutboxMessages);
        Assert.Equal(0, snapshot.CustomerTransactions);
        Assert.Equal(0, snapshot.AccountLedgers);
        Assert.Equal(0, snapshot.JournalEntries);
        Assert.Equal(0, snapshot.IdempotencyRecords);

        progressEntries.Enqueue("verification:no-uncommitted-durable-state-visible");
    }

    private static async Task WaitForStockLockArrivalAsync(
        StockLockRendezvous stockLockAttempt,
        ConcurrentQueue<string> progressEntries)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (stockLockAttempt.ArrivalCount < 1 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.True(
            stockLockAttempt.ArrivalCount == 1,
            $"Actor B did not reach the contested ProductVariantStocks lock. Progress: {string.Join(" | ", progressEntries)}");
    }

    private static async Task AssertStockCon01NoPartialVisibilityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        SeededScenario seeded,
        ConcurrentQueue<string> progressEntries)
    {
        var snapshot = await SnapshotDurableStateAsync(scope, tenantContext, seeded);

        Assert.Equal(0, snapshot.SalesInvoices);
        Assert.Equal(StockCon01InitialStockQuantity, snapshot.StockQuantity);
        Assert.Equal(0, snapshot.StockMovements);
        Assert.Equal(0, snapshot.AccountingOutboxMessages);
        Assert.Equal(0, snapshot.CustomerTransactions);
        Assert.Equal(0, snapshot.AccountLedgers);
        Assert.Equal(0, snapshot.JournalEntries);
        Assert.Equal(0, snapshot.IdempotencyRecords);

        await AssertStockCon01ProjectionConvergenceAsync(
            mongoDatabase,
            seeded,
            expectedStock: StockCon01InitialStockQuantity);

        progressEntries.Enqueue("verification:stock-con-01-no-partial-visibility");
    }

    private static async Task<SaleActorResult> AssertStockCon01FinalAuthorityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        SaleActor actorA,
        SaleActor actorB,
        CreateSaleRequest request,
        IReadOnlyCollection<SaleActorResult> results,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var successes = results.Where(result => result.Succeeded).ToList();
        var failures = results.Where(result => !result.Succeeded).ToList();
        Assert.Single(successes);
        Assert.Single(failures);

        var winner = successes.Single();
        var loser = failures.Single();
        Assert.Equal(StatusCodes.Status409Conflict, loser.StatusCode);

        var stock = await verifyContext.ProductVariantStocks
            .SingleAsync(item =>
                item.TenantId == seeded.TenantId &&
                item.WarehouseId == seeded.WarehouseId &&
                item.VariantId == seeded.VariantId);
        var invoice = await verifyContext.SalesInvoices
            .Include(item => item.Items)
            .SingleAsync(item => item.TenantId == seeded.TenantId);
        var movement = await verifyContext.StockMovements
            .SingleAsync(item =>
                item.TenantId == seeded.TenantId &&
                item.ProductVariantId == seeded.VariantId &&
                item.WarehouseId == seeded.WarehouseId &&
                item.MovementType == StockMovementType.Sale);
        var stockSyncOutbox = await verifyContext.StockSyncOutboxMessages
            .IgnoreQueryFilters()
            .SingleAsync(item =>
                item.TenantId == seeded.TenantId &&
                item.VariantId == seeded.VariantId &&
                item.WarehouseId == seeded.WarehouseId);
        var accountingOutbox = await verifyContext.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .SingleAsync(item => item.TenantId == seeded.TenantId && item.EventType == "PosSale");
        var customer = await verifyContext.Customers.SingleAsync(item => item.Id == seeded.CustomerId);
        var idempotencyRecords = await verifyContext.SalesRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == seeded.TenantId && item.Operation == "CREATE")
            .ToListAsync();
        var journalEntries = await verifyContext.JournalEntries
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == seeded.TenantId && item.SourceId == invoice.Id)
            .ToListAsync();

        Assert.Equal(0m, stock.Quantity);
        Assert.Equal(0m, stock.ReservedQuantity);
        Assert.Equal(0m, stock.AvailableQuantity);
        Assert.Equal(invoice.Id, winner.Response!.InvoiceId);
        Assert.Equal(AuthoritativeUnitPrice, invoice.TotalAmount);
        Assert.Equal(AuthoritativeUnitPrice, invoice.PaidAmount);
        Assert.Equal(InvoiceStatus.Completed, invoice.Status);

        var invoiceItem = Assert.Single(invoice.Items);
        Assert.Equal(StockCon01SaleQuantity, invoiceItem.BaseQuantity);
        Assert.Equal(ProductCostPrice, invoiceItem.CostPriceSnapshot);
        Assert.Equal(ProductCostPrice * StockCon01SaleQuantity, invoiceItem.CostAmountSnapshot);

        Assert.Equal(-StockCon01SaleQuantity, movement.QuantityChange);
        Assert.Equal(0m, movement.QuantityAfter);
        Assert.Equal(0m, movement.AvailableAfter);
        Assert.Equal(0m, movement.ReservedAfter);
        Assert.Equal(invoice.Id, movement.ReferenceId);
        Assert.Equal(invoiceItem.Id, movement.ReferenceLineId);
        Assert.Equal(ProductCostPrice, movement.Price);

        var stockSyncEvent = JsonSerializer.Deserialize<ProductVariantStockChangedEvent>(stockSyncOutbox.PayloadJson)
            ?? throw new XunitException("STOCK-CON-01 stock sync outbox payload could not be deserialized.");
        Assert.Equal(nameof(ProductVariantStockChangedEvent), stockSyncOutbox.EventType);
        Assert.Equal(0m, stockSyncEvent.NewQuantity);
        Assert.Equal(invoice.Id, stockSyncEvent.ReferenceId);
        Assert.Null(stockSyncOutbox.PublishedAtUtc);
        Assert.False(stockSyncOutbox.IsFailed);

        Assert.Equal("PosSale", accountingOutbox.EventType);
        Assert.Equal(invoice.Id, accountingOutbox.BusinessEntityId);
        Assert.NotNull(accountingOutbox.ProcessedAtUtc);
        Assert.NotNull(accountingOutbox.JournalId);
        Assert.NotEmpty(journalEntries);
        Assert.Equal(0m, customer.Balance);

        Assert.Single(idempotencyRecords);
        Assert.Equal(winner.Actor.RequestId, idempotencyRecords.Single().Key);
        Assert.DoesNotContain(idempotencyRecords, item => item.Key == loser.Actor.RequestId);

        Assert.Equal(0, await verifyContext.InventoryReservations
            .IgnoreQueryFilters()
            .CountAsync(item => item.TenantId == seeded.TenantId));
        Assert.Equal(0, await verifyContext.InventoryEventInbox
            .IgnoreQueryFilters()
            .CountAsync(item => item.TenantId == seeded.TenantId));
        Assert.Equal(0, await verifyContext.Set<ProcessedEvent>()
            .IgnoreQueryFilters()
            .CountAsync(item =>
                item.TenantId == seeded.TenantId &&
                !VariantStockProjectionFenceConsumerNames.Contains(item.ConsumerName)));

        await AssertSaleReplaySafetyAsync(
            scope,
            tenantContext,
            seeded,
            winner.Actor,
            request,
            expectedInvoiceId: winner.Response.InvoiceId,
            expectedSuccess: true);
        await AssertSaleReplaySafetyAsync(
            scope,
            tenantContext,
            seeded,
            loser.Actor,
            request,
            expectedInvoiceId: null,
            expectedSuccess: false);

        Assert.Contains(results, result => result.Actor == actorA);
        Assert.Contains(results, result => result.Actor == actorB);
        progressEntries.Enqueue("verification:stock-con-01-authoritative-sql-final");

        return winner;
    }

    private static async Task<SaleActorResult> AssertStockCon02FinalAuthorityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        SaleActor originalActor,
        SaleActor replayActor,
        CreateSaleRequest request,
        IReadOnlyCollection<SaleActorResult> results,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var original = results.Single(result => result.Actor.Name == originalActor.Name);
        var replay = results.Single(result => result.Actor.Name == replayActor.Name);
        Assert.True(original.Succeeded, original.PayloadJson ?? "Original STOCK-CON-02 mutation did not commit.");

        var stock = await verifyContext.ProductVariantStocks
            .SingleAsync(item =>
                item.TenantId == seeded.TenantId &&
                item.WarehouseId == seeded.WarehouseId &&
                item.VariantId == seeded.VariantId);
        var invoice = await verifyContext.SalesInvoices
            .Include(item => item.Items)
            .SingleAsync(item => item.TenantId == seeded.TenantId);
        var movement = await verifyContext.StockMovements
            .SingleAsync(item =>
                item.TenantId == seeded.TenantId &&
                item.ProductVariantId == seeded.VariantId &&
                item.WarehouseId == seeded.WarehouseId &&
                item.MovementType == StockMovementType.Sale);
        var stockSyncOutbox = await verifyContext.StockSyncOutboxMessages
            .IgnoreQueryFilters()
            .SingleAsync(item =>
                item.TenantId == seeded.TenantId &&
                item.VariantId == seeded.VariantId &&
                item.WarehouseId == seeded.WarehouseId);
        var accountingOutbox = await verifyContext.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .SingleAsync(item => item.TenantId == seeded.TenantId && item.EventType == "PosSale");
        var customer = await verifyContext.Customers.SingleAsync(item => item.Id == seeded.CustomerId);
        var idempotencyRecord = await verifyContext.SalesRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .SingleAsync(item => item.TenantId == seeded.TenantId && item.Operation == "CREATE");
        var journalEntries = await verifyContext.JournalEntries
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == seeded.TenantId && item.SourceId == invoice.Id)
            .ToListAsync();

        Assert.Equal(0m, stock.Quantity);
        Assert.Equal(0m, stock.ReservedQuantity);
        Assert.Equal(0m, stock.AvailableQuantity);
        Assert.Equal(invoice.Id, original.Response!.InvoiceId);
        Assert.Equal(AuthoritativeUnitPrice, invoice.TotalAmount);
        Assert.Equal(AuthoritativeUnitPrice, invoice.PaidAmount);
        Assert.Equal(InvoiceStatus.Completed, invoice.Status);

        var invoiceItem = Assert.Single(invoice.Items);
        Assert.Equal(StockCon01SaleQuantity, invoiceItem.BaseQuantity);
        Assert.Equal(ProductCostPrice, invoiceItem.CostPriceSnapshot);
        Assert.Equal(ProductCostPrice * StockCon01SaleQuantity, invoiceItem.CostAmountSnapshot);

        Assert.Equal(-StockCon01SaleQuantity, movement.QuantityChange);
        Assert.Equal(0m, movement.QuantityAfter);
        Assert.Equal(0m, movement.AvailableAfter);
        Assert.Equal(0m, movement.ReservedAfter);
        Assert.Equal(invoice.Id, movement.ReferenceId);
        Assert.Equal(invoiceItem.Id, movement.ReferenceLineId);
        Assert.Equal(ProductCostPrice, movement.Price);

        var stockSyncEvent = JsonSerializer.Deserialize<ProductVariantStockChangedEvent>(stockSyncOutbox.PayloadJson)
            ?? throw new XunitException("STOCK-CON-02 stock sync outbox payload could not be deserialized.");
        Assert.Equal(nameof(ProductVariantStockChangedEvent), stockSyncOutbox.EventType);
        Assert.Equal(0m, stockSyncEvent.NewQuantity);
        Assert.Equal(0m, stockSyncEvent.TotalStockQuantity);
        Assert.Equal(invoice.Id, stockSyncEvent.ReferenceId);
        Assert.Null(stockSyncOutbox.PublishedAtUtc);
        Assert.False(stockSyncOutbox.IsFailed);

        Assert.Equal("PosSale", accountingOutbox.EventType);
        Assert.Equal(invoice.Id, accountingOutbox.BusinessEntityId);
        Assert.NotNull(accountingOutbox.ProcessedAtUtc);
        Assert.NotNull(accountingOutbox.JournalId);
        Assert.NotEmpty(journalEntries);
        Assert.Equal(0m, customer.Balance);

        Assert.Equal(originalActor.RequestId, idempotencyRecord.Key);
        Assert.Equal(replayActor.RequestId, idempotencyRecord.Key);
        Assert.NotNull(idempotencyRecord.CompletedAtUtc);
        Assert.Equal(StatusCodes.Status200OK, idempotencyRecord.ResponseStatusCode);
        Assert.Contains(invoice.Id.ToString("D"), idempotencyRecord.ResponseJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        if (replay.Succeeded)
        {
            Assert.Equal(invoice.Id, replay.Response!.InvoiceId);
        }
        else
        {
            Assert.Contains(replay.StatusCode, new int?[]
            {
                StatusCodes.Status409Conflict,
                StatusCodes.Status500InternalServerError
            });
        }

        Assert.Equal(0, await verifyContext.InventoryReservations
            .IgnoreQueryFilters()
            .CountAsync(item => item.TenantId == seeded.TenantId));
        Assert.Equal(0, await verifyContext.InventoryEventInbox
            .IgnoreQueryFilters()
            .CountAsync(item => item.TenantId == seeded.TenantId));
        Assert.Equal(0, await verifyContext.Set<ProcessedEvent>()
            .IgnoreQueryFilters()
            .CountAsync(item =>
                item.TenantId == seeded.TenantId &&
                !VariantStockProjectionFenceConsumerNames.Contains(item.ConsumerName)));

        await AssertSaleReplaySafetyAsync(
            scope,
            tenantContext,
            seeded,
            originalActor,
            request,
            expectedInvoiceId: invoice.Id,
            expectedSuccess: true);
        await AssertSaleReplaySafetyAsync(
            scope,
            tenantContext,
            seeded,
            replayActor,
            request,
            expectedInvoiceId: invoice.Id,
            expectedSuccess: true);

        progressEntries.Enqueue("verification:stock-con-02-authoritative-sql-final");
        return original;
    }

    private static async Task AssertStockCon04NoPartialRestorationVisibilityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        SeededScenario seeded,
        Guid invoiceId,
        StockCon03ProjectionVisibilitySnapshot soldOutVisibility,
        ConcurrentQueue<string> progressEntries)
    {
        var snapshot = await LoadStockCon04StateSnapshotAsync(scope, tenantContext, seeded, invoiceId);
        var projection = await LoadStockCon03ProjectionVisibilityAsync(mongoDatabase, seeded);

        Assert.Equal(1, snapshot.SalesInvoiceCount);
        Assert.Equal(0m, snapshot.StockQuantity);
        Assert.Equal(0, snapshot.SalesReturnCount);
        Assert.Equal(0m, snapshot.TotalReturnedBaseQuantity);
        Assert.Equal(0, snapshot.SalesReturnStockMovementCount);
        Assert.Equal(0, snapshot.SalesReturnCustomerTransactionCount);
        Assert.Equal(0, snapshot.SalesReturnLedgerEntryCount);
        Assert.Equal(0, snapshot.RefundOutboxCount);
        Assert.Equal(0, snapshot.RefundJournalCount);
        Assert.Equal(0, snapshot.RefundCogsJournalCount);
        Assert.Equal(0, snapshot.SalesReturnIdempotencyRecordCount);
        Assert.Equal(0, snapshot.InventoryEventInboxCount);
        Assert.Equal(0, snapshot.ProcessedEventCount);
        Assert.Equal(StockCon01SaleQuantity, snapshot.RemainingReturnableQuantity);
        Assert.Equal(1, snapshot.SaleStockMovementCount);
        Assert.Equal(0m, snapshot.SaleStockMovementQuantityAfter);
        Assert.Equal(0m, snapshot.CustomerBalance);
        Assert.Equal(soldOutVisibility.AuthoritativeFingerprint, projection.AuthoritativeFingerprint);

        progressEntries.Enqueue("verification:stock-con-04-no-partial-restoration-visible");
    }

    private static async Task<PriceCon12ReturnActorResult> AssertStockCon04FinalAuthorityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId,
        SaleActor originalActor,
        SaleActor replayActor,
        CreateSalesReturnRequest request,
        IReadOnlyCollection<PriceCon12ReturnActorResult> results,
        ConcurrentQueue<string> progressEntries)
    {
        var original = results.Single(result => result.Actor.Name == originalActor.Name);
        var replay = results.Single(result => result.Actor.Name == replayActor.Name);

        Assert.True(original.Succeeded, original.PayloadJson ?? "Original STOCK-CON-04 restoration did not commit.");
        Assert.NotNull(original.Response);

        var snapshot = await LoadStockCon04StateSnapshotAsync(
            scope,
            tenantContext,
            seeded,
            invoiceId,
            original.Response!.ReturnId);

        Assert.Equal(1, snapshot.SalesInvoiceCount);
        Assert.Equal(StockCon01SaleQuantity, snapshot.StockQuantity);
        Assert.Equal(1, snapshot.SalesReturnCount);
        Assert.Equal(StockCon01SaleQuantity, snapshot.TotalReturnedBaseQuantity);
        Assert.Equal(0m, snapshot.RemainingReturnableQuantity);
        Assert.Equal(1, snapshot.SaleStockMovementCount);
        Assert.Equal(1, snapshot.SalesReturnStockMovementCount);
        Assert.Equal(-StockCon01SaleQuantity, snapshot.SaleStockMovementQuantityChange);
        Assert.Equal(0m, snapshot.SaleStockMovementQuantityAfter);
        Assert.Equal(StockCon01SaleQuantity, snapshot.ReturnStockMovementQuantityChange);
        Assert.Equal(StockCon01SaleQuantity, snapshot.ReturnStockMovementQuantityAfter);
        Assert.Equal(ProductCostPrice, snapshot.ReturnStockMovementPrice);
        Assert.Equal(AuthoritativeUnitPrice, snapshot.ReturnTotalAmount);
        Assert.Equal(ProductCostPrice, snapshot.ReturnCostPriceSnapshot);
        Assert.Equal(ProductCostPrice * StockCon01SaleQuantity, snapshot.ReturnCostAmountSnapshot);
        Assert.Equal(1, snapshot.SalesReturnCustomerTransactionCount);
        Assert.Equal(1, snapshot.SalesReturnLedgerEntryCount);
        Assert.Equal(3, snapshot.TotalCustomerTransactions);
        Assert.Equal(3, snapshot.TotalCustomerLedgerEntries);
        Assert.Equal(0m - AuthoritativeUnitPrice, snapshot.CustomerBalance);
        Assert.Equal(1, snapshot.RefundOutboxCount);
        Assert.Equal(1, snapshot.RefundOutboxAggregateVersion);
        Assert.Equal(1, snapshot.RefundOutboxPayloadMutationVersion);
        Assert.Equal(invoiceId, snapshot.RefundOutboxPayloadOriginalInvoiceId);
        Assert.NotNull(snapshot.RefundOutboxProcessedAtUtc);
        Assert.NotNull(snapshot.RefundOutboxJournalId);
        Assert.Equal(1, snapshot.RefundJournalCount);
        Assert.Equal(1, snapshot.RefundCogsJournalCount);
        Assert.Equal(2, snapshot.StockSyncOutboxCount);
        Assert.Equal(1, snapshot.SalesReturnIdempotencyRecordCount);
        Assert.Equal(originalActor.RequestId, snapshot.SalesReturnIdempotencyKey);
        Assert.Equal(replayActor.RequestId, snapshot.SalesReturnIdempotencyKey);
        Assert.Equal(StatusCodes.Status200OK, snapshot.SalesReturnIdempotencyResponseStatusCode);
        Assert.NotNull(snapshot.SalesReturnIdempotencyCompletedAtUtc);
        Assert.Contains(
            original.Response.ReturnId.ToString("D"),
            snapshot.SalesReturnIdempotencyResponseJson ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, snapshot.InvoiceMutationVersion);
        Assert.Equal(0, snapshot.InventoryEventInboxCount);
        Assert.Equal(0, snapshot.ProcessedEventCount);

        if (replay.Succeeded)
        {
            Assert.Equal(original.Response.ReturnId, replay.Response!.ReturnId);
            Assert.Equal(original.Response.ReturnNumber, replay.Response.ReturnNumber);
        }
        else
        {
            Assert.Contains(replay.StatusCode, new int?[]
            {
                StatusCodes.Status409Conflict,
                StatusCodes.Status500InternalServerError
            });
        }

        await AssertSalesReturnReplayDoesNotMutateStockAsync(
            scope,
            tenantContext,
            seeded,
            invoiceId,
            originalActor,
            request,
            original.Response,
            progressEntries);

        await AssertSalesReturnReplayDoesNotMutateStockAsync(
            scope,
            tenantContext,
            seeded,
            invoiceId,
            replayActor,
            request,
            original.Response,
            progressEntries);

        progressEntries.Enqueue("verification:stock-con-04-authoritative-sql-final");
        return original;
    }

    private static async Task<PriceCon12ReturnActorResult> AssertStockCon07FinalAuthorityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId,
        SaleActor requestA,
        SaleActor requestB,
        CreateSalesReturnRequest request,
        IReadOnlyCollection<PriceCon12ReturnActorResult> results,
        ConcurrentQueue<string> progressEntries)
    {
        var original = results.Single(result => result.Actor.Name == requestA.Name);
        var overlapping = results.Single(result => result.Actor.Name == requestB.Name);

        Assert.True(original.Succeeded, original.PayloadJson ?? "Request A STOCK-CON-07 restoration did not commit.");
        Assert.NotNull(original.Response);
        Assert.False(overlapping.Succeeded, overlapping.PayloadJson ?? "Request B unexpectedly restored the same historical sold quantity.");
        Assert.Equal(StatusCodes.Status400BadRequest, overlapping.StatusCode);
        Assert.Contains(
            "Cannot return more than sold quantity",
            overlapping.PayloadJson ?? string.Empty,
            StringComparison.Ordinal);

        var snapshot = await LoadStockCon04StateSnapshotAsync(
            scope,
            tenantContext,
            seeded,
            invoiceId,
            original.Response!.ReturnId);

        Assert.Equal(1, snapshot.SalesInvoiceCount);
        Assert.Equal(StockCon01SaleQuantity, snapshot.StockQuantity);
        Assert.Equal(1, snapshot.SalesReturnCount);
        Assert.Equal(StockCon01SaleQuantity, snapshot.TotalReturnedBaseQuantity);
        Assert.Equal(0m, snapshot.RemainingReturnableQuantity);
        Assert.Equal(1, snapshot.SaleStockMovementCount);
        Assert.Equal(1, snapshot.SalesReturnStockMovementCount);
        Assert.Equal(-StockCon01SaleQuantity, snapshot.SaleStockMovementQuantityChange);
        Assert.Equal(0m, snapshot.SaleStockMovementQuantityAfter);
        Assert.Equal(StockCon01SaleQuantity, snapshot.ReturnStockMovementQuantityChange);
        Assert.Equal(StockCon01SaleQuantity, snapshot.ReturnStockMovementQuantityAfter);
        Assert.Equal(ProductCostPrice, snapshot.ReturnStockMovementPrice);
        Assert.Equal(AuthoritativeUnitPrice, snapshot.ReturnTotalAmount);
        Assert.Equal(ProductCostPrice, snapshot.ReturnCostPriceSnapshot);
        Assert.Equal(ProductCostPrice * StockCon01SaleQuantity, snapshot.ReturnCostAmountSnapshot);
        Assert.Equal(1, snapshot.SalesReturnCustomerTransactionCount);
        Assert.Equal(1, snapshot.SalesReturnLedgerEntryCount);
        Assert.Equal(3, snapshot.TotalCustomerTransactions);
        Assert.Equal(3, snapshot.TotalCustomerLedgerEntries);
        Assert.Equal(0m - AuthoritativeUnitPrice, snapshot.CustomerBalance);
        Assert.Equal(1, snapshot.RefundOutboxCount);
        Assert.Equal(1, snapshot.RefundOutboxAggregateVersion);
        Assert.Equal(1, snapshot.RefundOutboxPayloadMutationVersion);
        Assert.Equal(invoiceId, snapshot.RefundOutboxPayloadOriginalInvoiceId);
        Assert.NotNull(snapshot.RefundOutboxProcessedAtUtc);
        Assert.NotNull(snapshot.RefundOutboxJournalId);
        Assert.Equal(1, snapshot.RefundJournalCount);
        Assert.Equal(1, snapshot.RefundCogsJournalCount);
        Assert.Equal(2, snapshot.StockSyncOutboxCount);
        Assert.Equal(1, snapshot.SalesReturnIdempotencyRecordCount);
        Assert.Equal(requestA.RequestId, snapshot.SalesReturnIdempotencyKey);
        Assert.NotEqual(requestB.RequestId, snapshot.SalesReturnIdempotencyKey);
        Assert.Equal(StatusCodes.Status200OK, snapshot.SalesReturnIdempotencyResponseStatusCode);
        Assert.NotNull(snapshot.SalesReturnIdempotencyCompletedAtUtc);
        Assert.Contains(
            original.Response.ReturnId.ToString("D"),
            snapshot.SalesReturnIdempotencyResponseJson ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, snapshot.InvoiceMutationVersion);
        Assert.Equal(0, snapshot.InventoryEventInboxCount);
        Assert.Equal(0, snapshot.ProcessedEventCount);

        await AssertSalesReturnReplayDoesNotMutateStockAsync(
            scope,
            tenantContext,
            seeded,
            invoiceId,
            requestA,
            request,
            original.Response,
            progressEntries);

        await AssertRejectedSalesReturnDoesNotMutateStockAsync(
            scope,
            tenantContext,
            seeded,
            invoiceId,
            requestB,
            request,
            original.Response,
            progressEntries);

        progressEntries.Enqueue("verification:stock-con-07-authoritative-sql-final");
        return original;
    }

    private static async Task AssertSalesReturnReplayDoesNotMutateStockAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId,
        SaleActor actor,
        CreateSalesReturnRequest request,
        SalesReturnResponse expectedResponse,
        ConcurrentQueue<string> progressEntries)
    {
        var before = await LoadStockCon04StateSnapshotAsync(scope, tenantContext, seeded, invoiceId, expectedResponse.ReturnId);

        await using var connectionContext = scope.CreateDbContext(tenantContext);
        var connectionString = connectionContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");

        var result = await ExecuteSalesReturnRequestActorAsync(
            connectionString,
            tenantContext,
            actor,
            request,
            progressEntries);

        Assert.Null(result.Exception);
        Assert.True(result.Succeeded, result.PayloadJson ?? "Post-commit sales-return replay did not return stored success.");
        Assert.Equal(expectedResponse.ReturnId, result.Response!.ReturnId);
        Assert.Equal(expectedResponse.ReturnNumber, result.Response.ReturnNumber);

        var after = await LoadStockCon04StateSnapshotAsync(scope, tenantContext, seeded, invoiceId, expectedResponse.ReturnId);
        Assert.Equal(before, after);
    }

    private static async Task AssertRejectedSalesReturnDoesNotMutateStockAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId,
        SaleActor actor,
        CreateSalesReturnRequest request,
        SalesReturnResponse expectedResponse,
        ConcurrentQueue<string> progressEntries)
    {
        var before = await LoadStockCon04StateSnapshotAsync(scope, tenantContext, seeded, invoiceId, expectedResponse.ReturnId);

        await using var connectionContext = scope.CreateDbContext(tenantContext);
        var connectionString = connectionContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");

        var result = await ExecuteSalesReturnRequestActorAsync(
            connectionString,
            tenantContext,
            actor,
            request,
            progressEntries);

        Assert.Null(result.Exception);
        Assert.False(result.Succeeded, result.PayloadJson ?? "Rejected overlapping return replay unexpectedly succeeded.");
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Contains(
            "Cannot return more than sold quantity",
            result.PayloadJson ?? string.Empty,
            StringComparison.Ordinal);

        var after = await LoadStockCon04StateSnapshotAsync(scope, tenantContext, seeded, invoiceId, expectedResponse.ReturnId);
        Assert.Equal(before, after);
        progressEntries.Enqueue("verification:stock-con-07-rejected-return-replay-failed-closed");
    }

    private static async Task<StockCon04StateSnapshot> LoadStockCon04StateSnapshotAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId,
        Guid? returnId = null)
    {
        await using var context = scope.CreateDbContext(tenantContext);

        var invoice = await context.SalesInvoices
            .AsNoTracking()
            .SingleAsync(item => item.TenantId == seeded.TenantId && item.Id == invoiceId);
        var salesReturns = await context.Returns
            .AsNoTracking()
            .Where(item =>
                item.TenantId == seeded.TenantId &&
                item.ReturnType == ReturnType.SalesReturn &&
                item.OriginalInvoiceId == invoiceId)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync();
        var returnIds = salesReturns.Select(item => item.Id).ToList();
        var returnItems = await context.ReturnItems
            .AsNoTracking()
            .Where(item => returnIds.Contains(item.ReturnId))
            .ToListAsync();
        var stock = await context.ProductVariantStocks
            .AsNoTracking()
            .SingleAsync(item =>
                item.TenantId == seeded.TenantId &&
                item.WarehouseId == seeded.WarehouseId &&
                item.VariantId == seeded.VariantId);
        var stockMovements = await context.StockMovements
            .AsNoTracking()
            .Where(item =>
                item.TenantId == seeded.TenantId &&
                item.WarehouseId == seeded.WarehouseId &&
                item.ProductVariantId == seeded.VariantId)
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToListAsync();
        var saleMovement = stockMovements.SingleOrDefault(item => item.MovementType == StockMovementType.Sale);
        var returnMovement = stockMovements.SingleOrDefault(item => item.MovementType == StockMovementType.SalesReturn);
        var customerTransactions = await context.CustomerTransactions
            .AsNoTracking()
            .Where(item =>
                item.TenantId == seeded.TenantId &&
                item.CustomerId == seeded.CustomerId)
            .ToListAsync();
        var customerLedgers = await context.AccountLedgers
            .AsNoTracking()
            .Where(item =>
                item.TenantId == seeded.TenantId &&
                item.PartyType == PartyType.Customer &&
                item.PartyId == seeded.CustomerId)
            .ToListAsync();
        var refundOutboxMessages = await context.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item =>
                item.TenantId == seeded.TenantId &&
                item.EventType == "PosRefund" &&
                returnIds.Contains(item.BusinessEntityId))
            .OrderBy(item => item.CreatedAt)
            .ToListAsync();
        var stockSyncOutboxMessages = await context.StockSyncOutboxMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item =>
                item.TenantId == seeded.TenantId &&
                item.VariantId == seeded.VariantId &&
                item.WarehouseId == seeded.WarehouseId)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync();
        var idempotencyRecords = await context.SalesRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item =>
                item.TenantId == seeded.TenantId &&
                item.Operation == "SALES_RETURN_CREATE")
            .OrderBy(item => item.CreatedAt)
            .ToListAsync();
        var customer = await context.Customers
            .AsNoTracking()
            .SingleAsync(item => item.TenantId == seeded.TenantId && item.Id == seeded.CustomerId);
        var journalEntries = returnId.HasValue
            ? await context.JournalEntries
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(item =>
                    item.TenantId == seeded.TenantId &&
                    item.SourceId == returnId.Value)
                .ToListAsync()
            : new List<JournalEntry>();

        var refundOutbox = refundOutboxMessages.SingleOrDefault();
        var idempotencyRecord = idempotencyRecords.SingleOrDefault();
        var singleReturn = salesReturns.SingleOrDefault();
        var singleReturnItem = returnItems.SingleOrDefault();
        Guid? refundPayloadOriginalInvoiceId = null;
        int refundPayloadMutationVersion = 0;

        if (refundOutbox != null)
        {
            using var payload = JsonDocument.Parse(refundOutbox.PayloadJson);
            if (payload.RootElement.TryGetProperty("OriginalInvoiceId", out var originalInvoiceIdElement))
            {
                refundPayloadOriginalInvoiceId = originalInvoiceIdElement.GetGuid();
            }

            if (payload.RootElement.TryGetProperty("MutationVersion", out var mutationVersionElement) &&
                mutationVersionElement.ValueKind == JsonValueKind.Number)
            {
                refundPayloadMutationVersion = mutationVersionElement.GetInt32();
            }
        }

        return new StockCon04StateSnapshot(
            SalesInvoiceCount: 1,
            StockQuantity: stock.Quantity,
            SaleStockMovementCount: stockMovements.Count(item => item.MovementType == StockMovementType.Sale),
            SaleStockMovementQuantityChange: saleMovement?.QuantityChange ?? 0m,
            SaleStockMovementQuantityAfter: saleMovement?.QuantityAfter ?? 0m,
            SalesReturnCount: salesReturns.Count,
            ReturnTotalAmount: singleReturn?.TotalAmount ?? 0m,
            TotalReturnedBaseQuantity: returnItems.Sum(item => item.BaseQuantity),
            ReturnCostPriceSnapshot: singleReturnItem?.CostPriceSnapshot ?? 0m,
            ReturnCostAmountSnapshot: singleReturnItem?.CostAmountSnapshot ?? 0m,
            SalesReturnStockMovementCount: stockMovements.Count(item => item.MovementType == StockMovementType.SalesReturn),
            ReturnStockMovementQuantityChange: returnMovement?.QuantityChange ?? 0m,
            ReturnStockMovementQuantityAfter: returnMovement?.QuantityAfter ?? 0m,
            ReturnStockMovementPrice: returnMovement?.Price ?? 0m,
            SalesReturnCustomerTransactionCount: customerTransactions.Count(item => item.TransactionType == TransactionType.SalesReturn),
            TotalCustomerTransactions: customerTransactions.Count,
            CustomerBalance: customer.Balance,
            SalesReturnLedgerEntryCount: customerLedgers.Count(item => item.TransactionType == TransactionType.SalesReturn),
            TotalCustomerLedgerEntries: customerLedgers.Count,
            RefundOutboxCount: refundOutboxMessages.Count,
            RefundOutboxAggregateVersion: refundOutbox?.AggregateVersion ?? 0,
            RefundOutboxPayloadMutationVersion: refundPayloadMutationVersion,
            RefundOutboxPayloadOriginalInvoiceId: refundPayloadOriginalInvoiceId,
            RefundOutboxProcessedAtUtc: refundOutbox?.ProcessedAtUtc,
            RefundOutboxJournalId: refundOutbox?.JournalId,
            RefundJournalCount: journalEntries.Count(item => item.SourceType == "Return"),
            RefundCogsJournalCount: journalEntries.Count(item => item.SourceType == "Return_COGS"),
            StockSyncOutboxCount: stockSyncOutboxMessages.Count,
            SalesReturnIdempotencyRecordCount: idempotencyRecords.Count,
            SalesReturnIdempotencyKey: idempotencyRecord?.Key,
            SalesReturnIdempotencyResponseStatusCode: idempotencyRecord?.ResponseStatusCode,
            SalesReturnIdempotencyCompletedAtUtc: idempotencyRecord?.CompletedAtUtc,
            SalesReturnIdempotencyResponseJson: idempotencyRecord?.ResponseJson,
            InvoiceMutationVersion: invoice.AccountingMutationVersion,
            RemainingReturnableQuantity: StockCon01SaleQuantity - returnItems.Sum(item => item.BaseQuantity),
            InventoryEventInboxCount: await context.InventoryEventInbox
                .IgnoreQueryFilters()
                .CountAsync(item => item.TenantId == seeded.TenantId),
            ProcessedEventCount: await context.Set<ProcessedEvent>()
                .IgnoreQueryFilters()
                .CountAsync(item =>
                    item.TenantId == seeded.TenantId &&
                    !VariantStockProjectionFenceConsumerNames.Contains(item.ConsumerName)));
    }

    private static async Task<StockCon05CommerceHubGraph> SeedStockCon05CommerceHubDeliveredReturnAsync(
        ApplicationDbContext context,
        SeededScenario seeded,
        ConcurrentQueue<string> progressEntries)
    {
        var customer = CommerceHubCustomer.Create(
            "stock-con-05@example.test",
            "STOCK-CON-05 Customer",
            "01055555555",
            "hash");
        customer.Id = StockCon05CommerceCustomerId;

        var order = new CommerceHubMarketplaceOrder
        {
            Id = StockCon05MarketplaceOrderId,
            CustomerId = customer.Id,
            Customer = customer,
            OrderNumber = "CH-STOCK-05",
            FullName = "STOCK-CON-05 Customer",
            PhoneNumber = "01055555555",
            AddressLine = "5 CommerceHub Return Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = "COD",
            PaymentMethodName = "Cash on delivery",
            Subtotal = AuthoritativeUnitPrice,
            ShippingTotal = 0m,
            TotalAmount = AuthoritativeUnitPrice,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.Collected,
            CreatedAt = SeededUtc
        };

        var subOrder = new CommerceHubSubOrder
        {
            Id = StockCon05SubOrderId,
            MarketplaceOrderId = order.Id,
            MarketplaceOrder = order,
            OrderNumber = "CH-SUB-STOCK-05",
            StoreId = seeded.TenantId,
            StoreName = "STOCK-CON-05 Store",
            Currency = "EGP",
            Subtotal = AuthoritativeUnitPrice,
            Shipping = 0m,
            PaymentMethod = CommerceHubPaymentMethod.CashOnDelivery,
            PaymentMethodCode = "COD",
            PaymentMethodName = "Cash on delivery",
            Total = AuthoritativeUnitPrice,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.Collected,
            CreatedAt = SeededUtc
        };

        var item = new CommerceHubSubOrderItem
        {
            Id = StockCon05OrderItemId,
            SubOrderId = subOrder.Id,
            SubOrder = subOrder,
            ListingId = Guid.Parse("7f0f08b8-3e8a-4d42-91f4-8435d6d4b01b"),
            ProductId = seeded.ProductId,
            VariantId = seeded.VariantId,
            ProductName = "SI-CON-01 Product",
            VariantName = "Default",
            Quantity = (int)StockCon01SaleQuantity,
            UnitPrice = AuthoritativeUnitPrice,
            PlatformCommissionAmount = 0m,
            Total = AuthoritativeUnitPrice * StockCon01SaleQuantity,
            Status = CommerceHubOrderItemStatus.Delivered,
            DeliveredAt = SeededUtc.AddMinutes(20),
            CreatedAt = SeededUtc
        };

        var refundRequest = new CommerceHubRefundRequest
        {
            Id = StockCon05RefundRequestId,
            OrderId = order.Id,
            Order = order,
            OrderItemId = item.Id,
            OrderItem = item,
            CustomerId = customer.Id,
            Customer = customer,
            VendorId = seeded.TenantId,
            Quantity = (int)StockCon01SaleQuantity,
            UnitPriceSnapshot = AuthoritativeUnitPrice,
            TotalAmount = AuthoritativeUnitPrice * StockCon01SaleQuantity,
            Reason = "STOCK-CON-05 duplicate CommerceHub return delivery",
            ReasonCode = RefundReasonCode.Other,
            Status = CommerceHubRefundRequestStatus.WaitingForReturn,
            RefundExecutionTrigger = RefundExecutionTrigger.OnVendorConfirmation,
            PayoutMethodSnapshot = RefundPayoutMethod.VodafoneCash,
            PayoutAccountSnapshot = "ENC:stock-con-05",
            PayoutAccountMaskedSnapshot = "010****5555",
            PayoutAccountHolderNameSnapshot = "STOCK-CON-05 Customer",
            CreatedAt = SeededUtc
        };

        var returnRequest = new CommerceHubReturnRequest
        {
            Id = StockCon05ReturnRequestId,
            RefundId = refundRequest.Id,
            Refund = refundRequest,
            OrderItemId = item.Id,
            OrderItem = item,
            CustomerId = customer.Id,
            Customer = customer,
            VendorId = seeded.TenantId,
            Status = CommerceHubReturnStatus.DeliveredToVendor,
            PickupScheduledAt = SeededUtc.AddMinutes(40),
            PickedUpAt = SeededUtc.AddMinutes(50),
            InTransitAt = SeededUtc.AddMinutes(60),
            DeliveredAt = SeededUtc.AddMinutes(70),
            TrackingNumber = "TRACK-STOCK-CON-05",
            CourierName = "CommerceHub Carrier",
            Notes = "Seeded delivered return awaiting vendor confirmation.",
            RequestedQuantity = (int)StockCon01SaleQuantity,
            CreatedAt = SeededUtc.AddMinutes(30)
        };

        refundRequest.ReturnRequestId = returnRequest.Id;
        order.SubOrders.Add(subOrder);
        subOrder.Items.Add(item);

        context.CommerceHubCustomers.Add(customer);
        context.CommerceHubMarketplaceOrders.Add(order);
        context.CommerceHubRefundRequests.Add(refundRequest);
        context.CommerceHubReturnRequests.Add(returnRequest);

        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPaymentLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPayment())
        using (CommerceHubBoundaryMutationScope.BeginRefund())
        {
            await context.SaveChangesAsync();
        }

        var inventory = new InventoryConsistencyService(
            context,
            NullLogger<InventoryConsistencyService>.Instance);
        var reserveEventId = InventoryEventIdFactory.Create("stock-con-05-reserve", item.Id);
        var deliveryEventId = InventoryEventIdFactory.Create("stock-con-05-delivery", item.Id);

        var reserved = await inventory.ReserveOrderItemAsync(
            new InventoryReservationRequest(
                seeded.TenantId,
                order.Id,
                subOrder.Id,
                item.Id,
                seeded.ProductId,
                seeded.VariantId,
                StockCon01SaleQuantity,
                seeded.WarehouseId,
                null,
                reserveEventId,
                "stock-con-05-reserve",
                "CommerceHubWebhook"));
        Assert.True(reserved, "STOCK-CON-05 seed reservation failed.");

        await inventory.CommitDeliveryAsync(
            new InventoryDeliveryCommitRequest(
                seeded.TenantId,
                order.Id,
                subOrder.Id,
                item.Id,
                seeded.ProductId,
                seeded.VariantId,
                StockCon01SaleQuantity,
                deliveryEventId,
                "stock-con-05-delivery",
                "CommerceHubWebhook"));

        progressEntries.Enqueue("seed:stock-con-05-commercehub-delivered-order-and-return");

        return new StockCon05CommerceHubGraph(
            MarketplaceOrderId: order.Id,
            SubOrderId: subOrder.Id,
            OrderItemId: item.Id,
            RefundRequestId: refundRequest.Id,
            ReturnRequestId: returnRequest.Id,
            CustomerId: customer.Id,
            VendorId: seeded.TenantId);
    }

    private static async Task<CommerceHubReturnDeliveryActorResult> ExecuteCommerceHubReturnDeliveryActorAsync(
        string connectionString,
        TestTenantContext tenantContext,
        StockCon05CommerceHubGraph graph,
        SaleActor actor,
        ConcurrentQueue<string> progressEntries,
        params IInterceptor[] interceptors)
    {
        await using var context = CreateDbContext(connectionString, tenantContext, interceptors);
        await ConfigureSessionTimeoutsAsync(context);

        var inventory = new InventoryConsistencyService(
            context,
            NullLogger<InventoryConsistencyService>.Instance);
        var service = new ReturnManagementService(context, inventory);
        var started = Stopwatch.GetTimestamp();
        progressEntries.Enqueue($"{actor.Name}:commercehub-return-delivery-ready");

        try
        {
            var response = await service.ConfirmReceiptAsync(
                graph.ReturnRequestId,
                graph.VendorId,
                SeededUtc.AddMinutes(80),
                "STOCK-CON-05 duplicate delivery receipt.",
                resellableQuantity: (int)StockCon01SaleQuantity,
                damagedQuantity: 0,
                actor.UserId,
                $"{actor.Name}@stock-con-05.test",
                actor.TraceIdentifier,
                "CommerceHubWebhook");

            progressEntries.Enqueue($"{actor.Name}:commercehub-return-delivery-completed:{response.ReturnId}");
            return new CommerceHubReturnDeliveryActorResult(
                actor,
                Succeeded: true,
                StatusCode: StatusCodes.Status200OK,
                Response: response,
                PayloadJson: JsonSerializer.Serialize(response),
                Duration: Stopwatch.GetElapsedTime(started),
                Exception: null);
        }
        catch (CommerceHubCheckoutException ex)
        {
            progressEntries.Enqueue($"{actor.Name}:commercehub-return-delivery-rejected:{ex.StatusCode}:{ex.Code}");
            return new CommerceHubReturnDeliveryActorResult(
                actor,
                Succeeded: false,
                StatusCode: ex.StatusCode,
                Response: null,
                PayloadJson: ex.Code,
                Duration: Stopwatch.GetElapsedTime(started),
                Exception: null);
        }
        catch (Exception ex)
        {
            progressEntries.Enqueue($"{actor.Name}:commercehub-return-delivery-exception:{DescribeException(ex)}");
            return new CommerceHubReturnDeliveryActorResult(
                actor,
                Succeeded: false,
                StatusCode: null,
                Response: null,
                PayloadJson: null,
                Duration: Stopwatch.GetElapsedTime(started),
                Exception: ex);
        }
    }

    private static async Task AssertStockCon05NoPartialReturnDeliveryVisibilityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        SeededScenario seeded,
        StockCon05CommerceHubGraph graph,
        StockCon03ProjectionVisibilitySnapshot soldOutVisibility,
        ConcurrentQueue<string> progressEntries)
    {
        var snapshot = await LoadStockCon05StateSnapshotAsync(scope, tenantContext, seeded, graph);
        var projection = await LoadStockCon03ProjectionVisibilityAsync(mongoDatabase, seeded);

        Assert.Equal(0m, snapshot.StockQuantity);
        Assert.Equal(0m, snapshot.StockReservedQuantity);
        Assert.Equal(0m, snapshot.StockAvailableQuantity);
        Assert.Equal(1, snapshot.ReservationCount);
        Assert.Equal(StockCon01SaleQuantity, snapshot.ReservationCommittedQuantity);
        Assert.Equal(0m, snapshot.ReservationReturnedQuantity);
        Assert.Equal(0m, snapshot.ReservationDamagedReturnedQuantity);
        Assert.Equal(InventoryReservationStatus.Committed, snapshot.ReservationStatus);
        Assert.Equal(CommerceHubReturnStatus.DeliveredToVendor, snapshot.ReturnStatus);
        Assert.Equal(0, snapshot.ReceivedResellableQuantity);
        Assert.Equal(0, snapshot.ReceivedDamagedQuantity);
        Assert.Equal(CommerceHubRefundRequestStatus.WaitingForReturn, snapshot.RefundStatus);
        Assert.Equal(1, snapshot.ReservationMovementCount);
        Assert.Equal(1, snapshot.DeliveryMovementCount);
        Assert.Equal(0, snapshot.ReturnMovementCount);
        Assert.Equal(2, snapshot.StockSyncOutboxCount);
        Assert.Equal(2, snapshot.InventoryEventInboxCount);
        Assert.Equal(2, snapshot.AppliedInventoryEventInboxCount);
        Assert.Equal(0, snapshot.ProcessedEventCount);
        Assert.Equal(0, snapshot.RefundAuditLogCount);
        Assert.Equal(0, snapshot.ReturnConfirmedAuditLogCount);
        Assert.Equal(soldOutVisibility.AuthoritativeFingerprint, projection.AuthoritativeFingerprint);

        progressEntries.Enqueue("verification:stock-con-05-no-partial-return-delivery-visible");
    }

    private static async Task<CommerceHubReturnDeliveryActorResult> AssertStockCon05FinalAuthorityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        StockCon05CommerceHubGraph graph,
        SaleActor deliveryA,
        SaleActor deliveryB,
        IReadOnlyCollection<CommerceHubReturnDeliveryActorResult> results,
        ConcurrentQueue<string> progressEntries)
    {
        var original = results.Single(result => result.Actor.Name == deliveryA.Name);
        var duplicate = results.Single(result => result.Actor.Name == deliveryB.Name);

        Assert.True(original.Succeeded, original.PayloadJson ?? "Original CommerceHub return delivery did not commit.");
        Assert.True(duplicate.Succeeded, duplicate.PayloadJson ?? "Duplicate CommerceHub return delivery did not converge to the committed receipt.");
        Assert.NotNull(original.Response);
        Assert.NotNull(duplicate.Response);
        Assert.Equal(original.Response!.ReturnId, duplicate.Response!.ReturnId);
        Assert.Equal(original.Response.RefundId, duplicate.Response.RefundId);

        var snapshot = await LoadStockCon05StateSnapshotAsync(scope, tenantContext, seeded, graph);

        Assert.Equal(StockCon01SaleQuantity, snapshot.StockQuantity);
        Assert.Equal(0m, snapshot.StockReservedQuantity);
        Assert.Equal(StockCon01SaleQuantity, snapshot.StockAvailableQuantity);
        Assert.Equal(1, snapshot.ReturnRequestCount);
        Assert.Equal(1, snapshot.RefundRequestCount);
        Assert.Equal(graph.ReturnRequestId, snapshot.RefundReturnRequestId);
        Assert.Equal(graph.RefundRequestId, snapshot.ReturnRefundId);
        Assert.Equal(CommerceHubReturnStatus.Confirmed, snapshot.ReturnStatus);
        Assert.Equal(CommerceHubRefundRequestStatus.AwaitingPayout, snapshot.RefundStatus);
        Assert.Equal(StockCon01SaleQuantity, snapshot.ReceivedResellableQuantity);
        Assert.Equal(0, snapshot.ReceivedDamagedQuantity);
        Assert.Equal(1, snapshot.ReservationCount);
        Assert.Equal(StockCon01SaleQuantity, snapshot.ReservationCommittedQuantity);
        Assert.Equal(StockCon01SaleQuantity, snapshot.ReservationReturnedQuantity);
        Assert.Equal(0m, snapshot.ReservationDamagedReturnedQuantity);
        Assert.Equal(InventoryReservationStatus.Returned, snapshot.ReservationStatus);
        Assert.Equal(1, snapshot.ReservationMovementCount);
        Assert.Equal(1, snapshot.DeliveryMovementCount);
        Assert.Equal(1, snapshot.ReturnMovementCount);
        Assert.Equal(StockCon01SaleQuantity, snapshot.ReturnMovementQuantityChange);
        Assert.Equal(StockCon01SaleQuantity, snapshot.ReturnMovementQuantityAfter);
        Assert.Equal(StockCon01SaleQuantity, snapshot.ReturnMovementAvailableAfter);
        Assert.Equal(graph.ReturnRequestId, snapshot.ReturnMovementReferenceId);
        Assert.Equal(graph.OrderItemId, snapshot.ReturnMovementReferenceLineId);
        Assert.Equal("CommerceHubReturn", snapshot.ReturnMovementReferenceType);
        Assert.NotEqual(Guid.Empty, snapshot.ReturnMovementInventoryEventId);
        Assert.Equal(snapshot.ReturnInventoryEventId, snapshot.ReturnMovementInventoryEventId);
        Assert.Equal(3, snapshot.StockSyncOutboxCount);
        Assert.Equal(3, snapshot.InventoryEventInboxCount);
        Assert.Equal(3, snapshot.AppliedInventoryEventInboxCount);
        Assert.Equal(1, snapshot.ReturnInventoryEventInboxCount);
        Assert.Equal(InventoryEventInboxStatus.Applied, snapshot.ReturnInventoryEventStatus);
        Assert.Equal(0, snapshot.ProcessedEventCount);
        Assert.Equal(1, snapshot.ReturnConfirmedAuditLogCount);
        Assert.Equal(1, snapshot.RefundAuditLogCount);
        Assert.Equal(0, snapshot.CommerceHubRefundCount);
        Assert.Equal(0, snapshot.CommerceHubRefundExecutionCount);

        progressEntries.Enqueue("verification:stock-con-05-authoritative-sql-final");
        return original;
    }

    private static async Task AssertCommerceHubReturnDuplicateDeliveryDoesNotMutateStockAsync(
        TestDatabaseScope scope,
        string connectionString,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        StockCon05CommerceHubGraph graph,
        SaleActor actor,
        ConcurrentQueue<string> progressEntries)
    {
        var before = await LoadStockCon05StateSnapshotAsync(scope, tenantContext, seeded, graph);

        var duplicate = await ExecuteCommerceHubReturnDeliveryActorAsync(
            connectionString,
            tenantContext,
            graph,
            actor with
            {
                Name = "stock-con-05-delayed-duplicate",
                TraceIdentifier = "stock-con-05-delayed-duplicate"
            },
            progressEntries);

        Assert.Null(duplicate.Exception);
        Assert.True(duplicate.Succeeded, duplicate.PayloadJson ?? "Delayed duplicate CommerceHub return delivery did not converge.");
        Assert.Equal(graph.ReturnRequestId, duplicate.Response!.ReturnId);

        var after = await LoadStockCon05StateSnapshotAsync(scope, tenantContext, seeded, graph);
        Assert.Equal(before, after);
        progressEntries.Enqueue("verification:stock-con-05-delayed-duplicate-safe");
    }

    private static async Task AssertCommerceHubReturnOutOfOrderDeliveryDoesNotMutateStockAsync(
        TestDatabaseScope scope,
        string connectionString,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        StockCon05CommerceHubGraph graph,
        ConcurrentQueue<string> progressEntries)
    {
        var before = await LoadStockCon05StateSnapshotAsync(scope, tenantContext, seeded, graph);

        await using var context = CreateDbContext(connectionString, tenantContext);
        await ConfigureSessionTimeoutsAsync(context);
        var inventory = new InventoryConsistencyService(
            context,
            NullLogger<InventoryConsistencyService>.Instance);
        var service = new ReturnManagementService(context, inventory);

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            service.MarkDeliveredAsync(
                graph.ReturnRequestId,
                SeededUtc.AddMinutes(75),
                "TRACK-STOCK-CON-05",
                "CommerceHub Carrier",
                "Out-of-order delivered webhook after confirmed receipt.",
                ActorBUserId,
                "stock-con-05-delayed-webhook@example.test",
                "stock-con-05-out-of-order-delivered",
                "CommerceHubWebhook"));

        Assert.Equal("RETURN_TRANSITION_NOT_ALLOWED", exception.Code);

        var after = await LoadStockCon05StateSnapshotAsync(scope, tenantContext, seeded, graph);
        Assert.Equal(before, after);
        progressEntries.Enqueue("verification:stock-con-05-out-of-order-delivery-safe");
    }

    private static async Task<StockCon05StateSnapshot> LoadStockCon05StateSnapshotAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        StockCon05CommerceHubGraph graph)
    {
        await using var context = scope.CreateDbContext(tenantContext);

        var stock = await context.ProductVariantStocks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(item =>
                item.TenantId == seeded.TenantId &&
                item.WarehouseId == seeded.WarehouseId &&
                item.VariantId == seeded.VariantId);
        var reservations = await context.InventoryReservations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item =>
                item.TenantId == seeded.TenantId &&
                item.OrderItemId == graph.OrderItemId)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync();
        var returnRequest = await context.CommerceHubReturnRequests
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(item => item.Id == graph.ReturnRequestId);
        var refundRequest = await context.CommerceHubRefundRequests
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(item => item.Id == graph.RefundRequestId);
        var movements = await context.StockMovements
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item =>
                item.TenantId == seeded.TenantId &&
                item.ProductVariantId == seeded.VariantId &&
                item.WarehouseId == seeded.WarehouseId)
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToListAsync();
        var returnMovement = movements.SingleOrDefault(item =>
            item.ReferenceId == graph.ReturnRequestId &&
            item.ReferenceType == "CommerceHubReturn");
        var inboxRows = await context.InventoryEventInbox
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == seeded.TenantId)
            .ToListAsync();
        var returnEventId = InventoryEventIdFactory.Create("commercehub-return-confirm", graph.ReturnRequestId);
        var returnInbox = inboxRows.SingleOrDefault(item => item.EventId == returnEventId);
        var stockSyncCount = await context.StockSyncOutboxMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(item =>
                item.TenantId == seeded.TenantId &&
                item.VariantId == seeded.VariantId &&
                item.WarehouseId == seeded.WarehouseId);
        var auditLogs = await context.AuditLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item =>
                item.TenantId == seeded.TenantId &&
                (item.EntityId == graph.ReturnRequestId.ToString() ||
                 item.EntityId == graph.RefundRequestId.ToString()))
            .ToListAsync();

        var reservation = reservations.SingleOrDefault();

        return new StockCon05StateSnapshot(
            StockQuantity: stock.Quantity,
            StockReservedQuantity: stock.ReservedQuantity,
            StockAvailableQuantity: stock.AvailableQuantity,
            ReservationCount: reservations.Count,
            ReservationCommittedQuantity: reservation?.CommittedQuantity ?? 0m,
            ReservationReturnedQuantity: reservation?.ReturnedQuantity ?? 0m,
            ReservationDamagedReturnedQuantity: reservation?.DamagedReturnedQuantity ?? 0m,
            ReservationStatus: reservation?.Status,
            ReturnRequestCount: await context.CommerceHubReturnRequests
                .IgnoreQueryFilters()
                .CountAsync(item => item.Id == graph.ReturnRequestId),
            RefundRequestCount: await context.CommerceHubRefundRequests
                .IgnoreQueryFilters()
                .CountAsync(item => item.Id == graph.RefundRequestId),
            ReturnStatus: returnRequest.Status,
            RefundStatus: refundRequest.Status,
            ReceivedResellableQuantity: returnRequest.ReceivedResellableQuantity,
            ReceivedDamagedQuantity: returnRequest.ReceivedDamagedQuantity,
            ReturnRefundId: returnRequest.RefundId,
            RefundReturnRequestId: refundRequest.ReturnRequestId,
            ReservationMovementCount: movements.Count(item =>
                item.InventoryEventType == InventoryEventType.StockReserved &&
                item.ReferenceType == "CommerceHubOrder"),
            DeliveryMovementCount: movements.Count(item =>
                item.InventoryEventType == InventoryEventType.StockDecreased &&
                item.ReferenceType == "CommerceHubDelivery"),
            ReturnMovementCount: movements.Count(item => item.ReferenceType == "CommerceHubReturn"),
            ReturnMovementQuantityChange: returnMovement?.QuantityChange ?? 0m,
            ReturnMovementQuantityAfter: returnMovement?.QuantityAfter ?? 0m,
            ReturnMovementAvailableAfter: returnMovement?.AvailableAfter ?? 0m,
            ReturnMovementReferenceId: returnMovement?.ReferenceId,
            ReturnMovementReferenceLineId: returnMovement?.ReferenceLineId,
            ReturnMovementReferenceType: returnMovement?.ReferenceType,
            ReturnMovementInventoryEventId: returnMovement?.InventoryEventId,
            ReturnInventoryEventId: returnEventId,
            StockSyncOutboxCount: stockSyncCount,
            InventoryEventInboxCount: inboxRows.Count,
            AppliedInventoryEventInboxCount: inboxRows.Count(item => item.Status == InventoryEventInboxStatus.Applied),
            ReturnInventoryEventInboxCount: inboxRows.Count(item => item.EventId == returnEventId),
            ReturnInventoryEventStatus: returnInbox?.Status,
            ProcessedEventCount: await context.Set<ProcessedEvent>()
                .IgnoreQueryFilters()
                .CountAsync(item =>
                    item.TenantId == seeded.TenantId &&
                    !VariantStockProjectionFenceConsumerNames.Contains(item.ConsumerName)),
            ReturnConfirmedAuditLogCount: auditLogs.Count(item => item.ActionType == "RETURN_CONFIRMED"),
            RefundAuditLogCount: auditLogs.Count(item => item.ActionType == "REFUND_AWAITING_PAYOUT"),
            CommerceHubRefundCount: await context.CommerceHubRefunds
                .IgnoreQueryFilters()
                .CountAsync(item => item.RefundRequestId == graph.RefundRequestId),
            CommerceHubRefundExecutionCount: await context.CommerceHubRefundExecutions
                .IgnoreQueryFilters()
                .CountAsync(item => item.RefundId == graph.RefundRequestId));
    }

    private static async Task<ProductVariantStockChangedEvent> LoadStockCon05StockEventAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid referenceId,
        string referenceType)
    {
        await using var context = scope.CreateDbContext(tenantContext);
        var messages = await context.StockSyncOutboxMessages
            .IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == seeded.TenantId &&
                item.VariantId == seeded.VariantId &&
                item.WarehouseId == seeded.WarehouseId)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync();

        var stockEvents = messages
            .Select(message => JsonSerializer.Deserialize<ProductVariantStockChangedEvent>(message.PayloadJson)
                               ?? throw new XunitException("STOCK-CON-05 stock sync payload could not be deserialized."))
            .ToList();

        var stockEvent = stockEvents.Single(item =>
            item.ReferenceId == referenceId &&
            string.Equals(item.ReferenceType, referenceType, StringComparison.Ordinal));

        if (referenceType == "CommerceHubReturn")
        {
            Assert.Equal(StockCon01SaleQuantity, stockEvent.NewQuantity);
            Assert.Equal(StockCon01SaleQuantity, stockEvent.TotalStockQuantity);
            Assert.Equal("CommerceHub Return Received", stockEvent.Reason);
        }
        else if (referenceType == "CommerceHubDelivery")
        {
            Assert.Equal(0m, stockEvent.NewQuantity);
            Assert.Equal(0m, stockEvent.TotalStockQuantity);
            Assert.Equal("CommerceHub Delivery Committed", stockEvent.Reason);
        }

        return stockEvent;
    }

    private static async Task AssertSaleReplaySafetyAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        SaleActor actor,
        CreateSaleRequest request,
        Guid? expectedInvoiceId,
        bool expectedSuccess)
    {
        var before = await SnapshotDurableStateAsync(scope, tenantContext, seeded);

        await using var replayContext = scope.CreateDbContext(tenantContext);
        await ConfigureSessionTimeoutsAsync(replayContext);
        var replayController = CreateController(
            replayContext,
            tenantContext,
            actor,
            NullLogger<SalesController>.Instance);

        var replayResult = await replayController.CreateSale(request, paymentAttemptId: actor.RequestId);

        if (expectedSuccess)
        {
            SaleResponse response;
            if (replayResult.Result is OkObjectResult ok)
            {
                response = Assert.IsType<SaleResponse>(ok.Value);
            }
            else
            {
                var stored = Assert.IsType<ObjectResult>(replayResult.Result);
                Assert.Equal(StatusCodes.Status200OK, stored.StatusCode ?? StatusCodes.Status200OK);
                var storedJson = stored.Value == null ? null : JsonSerializer.Serialize(stored.Value);
                Assert.True(
                    TryParseStoredSaleResponse(storedJson, out response),
                    $"Expected a stored sale replay response but received: {storedJson}");
            }

            Assert.Equal(expectedInvoiceId, response.InvoiceId);
        }
        else
        {
            Assert.False(
                replayResult.Result is OkObjectResult,
                $"Replay for actor {actor.Name} unexpectedly succeeded.");
        }

        var after = await SnapshotDurableStateAsync(scope, tenantContext, seeded);
        Assert.Equal(before, after);
    }

    private static async Task AssertActiveReplayConvergenceAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        SaleActor originalActor,
        SaleActor replayActor,
        IReadOnlyCollection<SaleActorResult> results,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var persistedInvoices = await verifyContext.SalesInvoices
            .Where(invoice => invoice.TenantId == seeded.TenantId)
            .OrderBy(invoice => invoice.CreatedAt)
            .ToListAsync();
        var persistedStocks = await verifyContext.ProductVariantStocks
            .Where(stock =>
                stock.TenantId == seeded.TenantId &&
                stock.WarehouseId == seeded.WarehouseId &&
                stock.VariantId == seeded.VariantId)
            .ToListAsync();
        var persistedMovements = await verifyContext.StockMovements
            .Where(movement =>
                movement.TenantId == seeded.TenantId &&
                movement.ProductVariantId == seeded.VariantId &&
                movement.WarehouseId == seeded.WarehouseId &&
                movement.MovementType == StockMovementType.Sale)
            .OrderBy(movement => movement.CreatedAt)
            .ThenBy(movement => movement.Id)
            .ToListAsync();
        var stockSyncOutboxMessages = await verifyContext.StockSyncOutboxMessages
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.VariantId == seeded.VariantId &&
                message.WarehouseId == seeded.WarehouseId)
            .OrderBy(message => message.CreatedAt)
            .ToListAsync();
        var accountingOutboxMessages = await verifyContext.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .Where(message => message.TenantId == seeded.TenantId)
            .OrderBy(message => message.CreatedAt)
            .ToListAsync();
        var customerTransactions = await verifyContext.CustomerTransactions
            .Where(transaction =>
                transaction.TenantId == seeded.TenantId &&
                transaction.CustomerId == seeded.CustomerId)
            .OrderBy(transaction => transaction.CreatedAt)
            .ThenBy(transaction => transaction.Id)
            .ToListAsync();
        var accountLedgers = await verifyContext.AccountLedgers
            .Where(ledger =>
                ledger.TenantId == seeded.TenantId &&
                ledger.PartyType == PartyType.Customer &&
                ledger.PartyId == seeded.CustomerId)
            .OrderBy(ledger => ledger.CreatedAt)
            .ThenBy(ledger => ledger.Id)
            .ToListAsync();
        var journalEntries = await verifyContext.JournalEntries
            .IgnoreQueryFilters()
            .Include(journal => journal.LedgerEntries)
            .Where(journal => journal.TenantId == seeded.TenantId)
            .OrderBy(journal => journal.CreatedAt)
            .ToListAsync();
        var idempotencyRecords = await verifyContext.SalesRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .Where(record => record.TenantId == seeded.TenantId && record.Operation == "CREATE")
            .OrderBy(record => record.CreatedAt)
            .ToListAsync();
        var customer = await verifyContext.Customers.SingleAsync(item => item.Id == seeded.CustomerId);

        Assert.Single(persistedStocks);
        var stock = persistedStocks.Single();
        Assert.Equal(1m, stock.Quantity);
        Assert.True(stock.Quantity >= 0m, $"Stock went negative. Progress: {string.Join(" | ", progressEntries)}");

        Assert.Single(persistedInvoices);
        Assert.Single(persistedMovements);
        Assert.Single(stockSyncOutboxMessages);
        Assert.Single(accountingOutboxMessages);
        Assert.Equal(2, customerTransactions.Count);
        Assert.Equal(2, accountLedgers.Count);
        Assert.Single(idempotencyRecords);

        var invoice = persistedInvoices.Single();
        var movement = persistedMovements.Single();
        var outboxMessage = accountingOutboxMessages.Single();
        var idempotencyRecord = idempotencyRecords.Single();

        Assert.Equal(seeded.TenantId, invoice.TenantId);
        Assert.Equal(seeded.CustomerId, invoice.CustomerId);
        Assert.Equal(seeded.WarehouseId, invoice.WarehouseId);
        Assert.Equal(20m, invoice.TotalAmount);
        Assert.Equal(20m, invoice.PaidAmount);

        Assert.Equal(-ContestedSaleQuantity, movement.QuantityChange);
        Assert.Equal(1m, movement.QuantityAfter);
        Assert.Equal(invoice.Id, movement.ReferenceId);
        Assert.Equal(invoice.InvoiceNumber, movement.ReferenceNumber);

        Assert.Equal("PosSale", outboxMessage.EventType);
        Assert.Equal("SalesInvoice", outboxMessage.SourceType);
        Assert.Equal(invoice.Id, outboxMessage.BusinessEntityId);
        Assert.Equal($"SALE:{seeded.TenantId}:{invoice.Id}", outboxMessage.IdempotencyKey);
        Assert.Equal(invoice.AccountingMutationVersion, outboxMessage.AggregateVersion);
        Assert.False(outboxMessage.IsFailed);
        Assert.NotNull(outboxMessage.ProcessedAtUtc);
        Assert.NotNull(outboxMessage.JournalId);

        Assert.Equal(originalActor.RequestId, idempotencyRecord.Key);
        Assert.Equal(replayActor.RequestId, idempotencyRecord.Key);
        Assert.NotNull(idempotencyRecord.CompletedAtUtc);
        Assert.Equal(StatusCodes.Status200OK, idempotencyRecord.ResponseStatusCode);
        Assert.Contains(invoice.Id.ToString("D"), idempotencyRecord.ResponseJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var original = results.Single(result => result.Actor.Name == originalActor.Name);
        var replay = results.Single(result => result.Actor.Name == replayActor.Name);

        Assert.True(original.Succeeded, original.PayloadJson);
        if (replay.Succeeded)
        {
            // Safe outcome A: replay waited on PostgreSQL's in-flight unique-key authority,
            // then returned the stored result after the original transaction committed.
            Assert.Equal(original.Response!.InvoiceId, replay.Response!.InvoiceId);
            Assert.Equal(invoice.Id, replay.Response.InvoiceId);
        }
        else
        {
            // Safe outcome B: replay failed closed while the original completed; no second
            // durable business effects may survive.
            Assert.Contains(replay.StatusCode, new int?[]
            {
                StatusCodes.Status409Conflict,
                StatusCodes.Status500InternalServerError
            });
        }

        Assert.All(customerTransactions, transaction => Assert.Equal(invoice.Id, transaction.ReferenceId));
        Assert.Contains(customerTransactions, transaction =>
            transaction.TransactionType == TransactionType.Sale &&
            transaction.DebitAmount == 20m &&
            transaction.CreditAmount == 0m);
        Assert.Contains(customerTransactions, transaction =>
            transaction.TransactionType == TransactionType.PaymentReceived &&
            transaction.DebitAmount == 0m &&
            transaction.CreditAmount == 20m);

        Assert.All(accountLedgers, ledger => Assert.Equal(invoice.Id, ledger.ReferenceId));
        Assert.Contains(accountLedgers, ledger =>
            ledger.TransactionType == TransactionType.Sale &&
            ledger.DebitAmount == 20m &&
            ledger.CreditAmount == 0m);
        Assert.Contains(accountLedgers, ledger =>
            ledger.TransactionType == TransactionType.PaymentReceived &&
            ledger.DebitAmount == 0m &&
            ledger.CreditAmount == 20m);

        Assert.Equal(0m, customer.Balance);
        Assert.NotEmpty(journalEntries);
        Assert.All(journalEntries, journal => Assert.Equal(invoice.Id, journal.SourceId));
        Assert.Equal(
            journalEntries.Count,
            journalEntries.Select(journal => journal.IdempotencyKey).Distinct(StringComparer.Ordinal).Count());

        await AssertReplaySafetyAsync(
            scope,
            tenantContext,
            seeded,
            originalActor,
            invoice.Id,
            expectedSuccess: true);
    }

    private static async Task<DurableStateSnapshot> SnapshotDurableStateAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded)
    {
        await using var context = scope.CreateDbContext(tenantContext);

        return new DurableStateSnapshot(
            SalesInvoices: await context.SalesInvoices.CountAsync(invoice => invoice.TenantId == seeded.TenantId),
            StockQuantity: await context.ProductVariantStocks
                .Where(stock =>
                    stock.TenantId == seeded.TenantId &&
                    stock.WarehouseId == seeded.WarehouseId &&
                    stock.VariantId == seeded.VariantId)
                .Select(stock => stock.Quantity)
                .SingleAsync(),
            StockMovements: await context.StockMovements.CountAsync(movement =>
                movement.TenantId == seeded.TenantId &&
                movement.WarehouseId == seeded.WarehouseId &&
                movement.ProductVariantId == seeded.VariantId &&
                movement.MovementType == StockMovementType.Sale),
            AccountingOutboxMessages: await context.AccountingOutboxMessages
                .IgnoreQueryFilters()
                .CountAsync(message => message.TenantId == seeded.TenantId),
            CustomerTransactions: await context.CustomerTransactions.CountAsync(transaction =>
                transaction.TenantId == seeded.TenantId &&
                transaction.CustomerId == seeded.CustomerId),
            AccountLedgers: await context.AccountLedgers.CountAsync(ledger =>
                ledger.TenantId == seeded.TenantId &&
                ledger.PartyType == PartyType.Customer &&
                ledger.PartyId == seeded.CustomerId),
            JournalEntries: await context.JournalEntries
                .IgnoreQueryFilters()
                .CountAsync(journal => journal.TenantId == seeded.TenantId),
            IdempotencyRecords: await context.SalesRequestIdempotencyRecords
                .IgnoreQueryFilters()
                .CountAsync(record => record.TenantId == seeded.TenantId && record.Operation == "CREATE"));
    }

    private static async Task AssertPriceCon11VisibilityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        decimal expectedVariantCostPrice,
        decimal expectedStockQuantity,
        int expectedSalesInvoices,
        int expectedStockMovements,
        ConcurrentQueue<string> progressEntries,
        string checkpoint)
    {
        await using var context = scope.CreateDbContext(tenantContext);

        var variantCostPrice = await context.ProductVariants
            .Where(variant => variant.Id == seeded.VariantId)
            .Select(variant => variant.CostPrice)
            .SingleAsync();
        var stockQuantity = await context.ProductVariantStocks
            .Where(stock =>
                stock.TenantId == seeded.TenantId &&
                stock.WarehouseId == seeded.WarehouseId &&
                stock.VariantId == seeded.VariantId)
            .Select(stock => stock.Quantity)
            .SingleAsync();

        Assert.Equal(expectedVariantCostPrice, variantCostPrice);
        Assert.Equal(expectedStockQuantity, stockQuantity);
        Assert.Equal(expectedSalesInvoices, await context.SalesInvoices.CountAsync(invoice => invoice.TenantId == seeded.TenantId));
        Assert.Equal(expectedStockMovements, await context.StockMovements.CountAsync(movement =>
            movement.TenantId == seeded.TenantId &&
            movement.ProductVariantId == seeded.VariantId &&
            movement.WarehouseId == seeded.WarehouseId &&
            movement.MovementType == StockMovementType.Sale));
        Assert.Equal(0, await context.SalesInvoiceItems.CountAsync(item => item.ProductVariantId == seeded.VariantId));
        Assert.Equal(0, await context.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .CountAsync(message => message.TenantId == seeded.TenantId && message.EventType == "PosSale"));
        Assert.Equal(0, await context.JournalEntries
            .IgnoreQueryFilters()
            .CountAsync(journal => journal.TenantId == seeded.TenantId));
        Assert.Equal(0, await context.CustomerTransactions.CountAsync(transaction =>
            transaction.TenantId == seeded.TenantId &&
            transaction.CustomerId == seeded.CustomerId));
        Assert.Equal(0, await context.AccountLedgers.CountAsync(ledger =>
            ledger.TenantId == seeded.TenantId &&
            ledger.PartyType == PartyType.Customer &&
            ledger.PartyId == seeded.CustomerId));

        progressEntries.Enqueue($"verification:price-con-11-visible:{checkpoint}");
    }

    private static async Task<PriceCon11CommittedFinancialState> AssertPriceCon11FinalConvergenceAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId,
        ConcurrentQueue<string> progressEntries)
    {
        await using var context = scope.CreateDbContext(tenantContext);

        var invoice = await context.SalesInvoices
            .Include(item => item.Items)
            .SingleAsync(item => item.Id == invoiceId);
        var item = Assert.Single(invoice.Items);
        var variant = await context.ProductVariants.SingleAsync(entity => entity.Id == seeded.VariantId);
        var stock = await context.ProductVariantStocks.SingleAsync(entity =>
            entity.TenantId == seeded.TenantId &&
            entity.WarehouseId == seeded.WarehouseId &&
            entity.VariantId == seeded.VariantId);
        var movement = await context.StockMovements.SingleAsync(entity =>
            entity.TenantId == seeded.TenantId &&
            entity.ReferenceId == invoice.Id &&
            entity.MovementType == StockMovementType.Sale);
        var outboxMessage = await context.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .SingleAsync(message =>
                message.TenantId == seeded.TenantId &&
                message.EventType == "PosSale" &&
                message.BusinessEntityId == invoice.Id);
        var journalEntries = await context.JournalEntries
            .IgnoreQueryFilters()
            .Include(journal => journal.LedgerEntries)
            .Where(journal => journal.TenantId == seeded.TenantId && journal.SourceId == invoice.Id)
            .OrderBy(journal => journal.SourceType)
            .ThenBy(journal => journal.Id)
            .ToListAsync();

        Assert.Equal(PriceCon11ExpectedSaleAmount, invoice.TotalAmount);
        Assert.Equal(PriceCon11ExpectedSaleAmount, invoice.PaidAmount);
        Assert.Equal(PriceCon11SaleUnitPrice, item.UnitPrice);
        Assert.Equal(ContestedSaleQuantity, item.BaseQuantity);
        Assert.Equal(PriceCon11InitialCostPrice, item.CostPriceSnapshot);
        Assert.Equal(PriceCon11ExpectedCostAmount, item.CostAmountSnapshot);

        Assert.Equal(PriceCon11MutatedCostPrice, variant.CostPrice);
        Assert.Equal(1m, stock.Quantity);

        Assert.Equal(-ContestedSaleQuantity, movement.QuantityChange);
        Assert.Equal(1m, movement.QuantityAfter);
        Assert.Equal(item.Id, movement.ReferenceLineId);
        Assert.Equal(PriceCon11InitialCostPrice, movement.Price);

        Assert.NotNull(outboxMessage.ProcessedAtUtc);
        Assert.NotNull(outboxMessage.JournalId);
        Assert.False(outboxMessage.IsFailed);
        Assert.Equal(invoice.AccountingMutationVersion, outboxMessage.AggregateVersion);

        Assert.Equal(2, journalEntries.Count);
        var revenueJournal = journalEntries.Single(journal => journal.SourceType == "SalesInvoice");
        var cogsJournal = journalEntries.Single(journal => journal.SourceType == "SalesInvoice_COGS");

        Assert.Equal(PriceCon11ExpectedSaleAmount, revenueJournal.LedgerEntries.Sum(entry => entry.DebitAmount));
        Assert.Equal(PriceCon11ExpectedSaleAmount, revenueJournal.LedgerEntries.Sum(entry => entry.CreditAmount));
        Assert.Equal(PriceCon11ExpectedCostAmount, cogsJournal.LedgerEntries.Sum(entry => entry.DebitAmount));
        Assert.Equal(PriceCon11ExpectedCostAmount, cogsJournal.LedgerEntries.Sum(entry => entry.CreditAmount));

        var historicalMargin = invoice.TotalAmount - item.CostAmountSnapshot!.Value;
        var currentCostMargin = invoice.TotalAmount - (variant.CostPrice * item.BaseQuantity);
        Assert.Equal(100m, historicalMargin);
        Assert.NotEqual(currentCostMargin, historicalMargin);

        var fingerprint = BuildPriceCon11FinancialFingerprint(invoice, item, movement, outboxMessage, journalEntries);
        progressEntries.Enqueue("verification:price-con-11-final-convergence");

        return new PriceCon11CommittedFinancialState(
            invoice.Id,
            item.Id,
            outboxMessage.Id,
            fingerprint);
    }

    private static async Task AssertPriceCon11ReplayProbeUsesHistoricalCostBasisAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        PriceCon11CommittedFinancialState committed,
        ConcurrentQueue<string> progressEntries)
    {
        var before = await LoadPriceCon11FinancialFingerprintAsync(scope, tenantContext, seeded, committed.InvoiceId);

        await using var context = scope.CreateDbContext(tenantContext);
        await ConfigureSessionTimeoutsAsync(context);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var invoice = await context.SalesInvoices
            .Include(entity => entity.Items)
                .ThenInclude(item => item.Product)
            .Include(entity => entity.Items)
                .ThenInclude(item => item.ProductVariant)
            .SingleAsync(entity => entity.Id == committed.InvoiceId);

        var postingService = new FinancialPostingService(
            context,
            NullLogger<FinancialPostingService>.Instance);
        var accountResolver = new AccountResolver(
            context,
            NullLogger<AccountResolver>.Instance);
        var cashboxLedgerAccountService = new CashboxLedgerAccountService(
            context,
            NullLogger<CashboxLedgerAccountService>.Instance);
        var commerceAccounting = new CommerceAccountingService(
            postingService,
            accountResolver,
            cashboxLedgerAccountService,
            NullLogger<CommerceAccountingService>.Instance);

        var result = await commerceAccounting.PostPosSaleAsync(
            invoice,
            operationKey: "PRICE-CON-11-REPLAY-PROBE");

        Assert.True(result.Success, result.ErrorMessage);

        var probeCogsJournal = await context.JournalEntries
            .IgnoreQueryFilters()
            .Include(journal => journal.LedgerEntries)
            .SingleAsync(journal =>
                journal.TenantId == seeded.TenantId &&
                journal.SourceId == committed.InvoiceId &&
                journal.SourceType == "SalesInvoice_COGS" &&
                journal.IdempotencyKey == $"COGS:{seeded.TenantId}:{committed.InvoiceId}:PRICE-CON-11-REPLAY-PROBE");

        Assert.Equal(PriceCon11ExpectedCostAmount, probeCogsJournal.LedgerEntries.Sum(entry => entry.DebitAmount));
        Assert.Equal(PriceCon11ExpectedCostAmount, probeCogsJournal.LedgerEntries.Sum(entry => entry.CreditAmount));

        await transaction.RollbackAsync();

        var after = await LoadPriceCon11FinancialFingerprintAsync(scope, tenantContext, seeded, committed.InvoiceId);
        Assert.Equal(before, after);
        Assert.Equal(committed.FinancialFingerprint, after);

        progressEntries.Enqueue("verification:price-con-11-replay-probe-used-historical-cost-basis");
    }

    private static async Task AssertPriceCon11DurableFinancialFingerprintUnchangedAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        PriceCon11CommittedFinancialState committed,
        ConcurrentQueue<string> progressEntries)
    {
        var afterReplay = await LoadPriceCon11FinancialFingerprintAsync(scope, tenantContext, seeded, committed.InvoiceId);
        Assert.Equal(committed.FinancialFingerprint, afterReplay);

        progressEntries.Enqueue("verification:price-con-11-duplicate-replay-left-financial-fingerprint-unchanged");
    }

    private static async Task<string> LoadPriceCon11FinancialFingerprintAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId)
    {
        await using var context = scope.CreateDbContext(tenantContext);

        var invoice = await context.SalesInvoices
            .Include(entity => entity.Items)
            .SingleAsync(entity => entity.Id == invoiceId);
        var item = Assert.Single(invoice.Items);
        var movement = await context.StockMovements.SingleAsync(entity =>
            entity.TenantId == seeded.TenantId &&
            entity.ReferenceId == invoice.Id &&
            entity.MovementType == StockMovementType.Sale);
        var outbox = await context.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .SingleAsync(message =>
                message.TenantId == seeded.TenantId &&
                message.EventType == "PosSale" &&
                message.BusinessEntityId == invoice.Id);
        var journals = await context.JournalEntries
            .IgnoreQueryFilters()
            .Include(journal => journal.LedgerEntries)
            .Where(journal => journal.TenantId == seeded.TenantId && journal.SourceId == invoice.Id)
            .OrderBy(journal => journal.SourceType)
            .ThenBy(journal => journal.Id)
            .ToListAsync();

        return BuildPriceCon11FinancialFingerprint(invoice, item, movement, outbox, journals);
    }

    private static async Task AssertPriceCon12NoPartialRecalculationVisibleAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId,
        Guid returnId,
        PriceCon12StateSnapshot baseline,
        ConcurrentQueue<string> progressEntries)
    {
        var current = await LoadPriceCon12StateSnapshotAsync(scope, tenantContext, seeded, invoiceId, returnId);

        Assert.Equal(baseline.FinancialFingerprint, current.FinancialFingerprint);
        Assert.Equal(0, current.RecalculationProcessedEvents);
        Assert.Equal(PriceCon11MutatedCostPrice, current.CurrentVariantCostPrice);
        Assert.Equal(baseline.StockQuantity, current.StockQuantity);
        Assert.Equal(baseline.WeightedAverageMovementCost, current.WeightedAverageMovementCost);
        Assert.Equal(baseline.CurrentCostInventoryValue, current.CurrentCostInventoryValue);

        progressEntries.Enqueue("verification:price-con-12-no-partial-recalculation-visible");
    }

    private static async Task AssertPriceCon12HistoricalSnapshotMutationIsBlockedAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        Guid invoiceId,
        Guid returnId,
        ConcurrentQueue<string> progressEntries)
    {
        await using (var saleContext = scope.CreateDbContext(tenantContext))
        {
            var saleItem = await saleContext.SalesInvoiceItems
                .SingleAsync(item => item.SalesInvoiceId == invoiceId);
            saleItem.CostPriceSnapshot = 999m;

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                saleContext.SaveChangesAsync());
            Assert.Contains("immutable historical POS cost snapshot", exception.Message, StringComparison.Ordinal);
        }

        await using (var returnContext = scope.CreateDbContext(tenantContext))
        {
            var returnItem = await returnContext.ReturnItems
                .SingleAsync(item => item.ReturnId == returnId);
            returnItem.CostAmountSnapshot = 999m;

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                returnContext.SaveChangesAsync());
            Assert.Contains("immutable historical POS cost snapshot", exception.Message, StringComparison.Ordinal);
        }

        progressEntries.Enqueue("verification:price-con-12-historical-snapshot-mutation-blocked");
    }

    private static async Task<PriceCon12StateSnapshot> LoadPriceCon12StateSnapshotAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId,
        Guid returnId)
    {
        await using var context = scope.CreateDbContext(tenantContext);

        var invoice = await context.SalesInvoices
            .Include(entity => entity.Items)
            .SingleAsync(entity => entity.Id == invoiceId);
        var saleItem = Assert.Single(invoice.Items);
        var salesReturn = await context.Returns
            .Include(entity => entity.Items)
            .SingleAsync(entity => entity.Id == returnId);
        var returnItem = Assert.Single(salesReturn.Items);
        var stock = await context.ProductVariantStocks
            .SingleAsync(entity =>
                entity.TenantId == seeded.TenantId &&
                entity.WarehouseId == seeded.WarehouseId &&
                entity.VariantId == seeded.VariantId);
        var variant = await context.ProductVariants
            .SingleAsync(entity => entity.Id == seeded.VariantId);
        var movements = await context.StockMovements
            .Where(entity =>
                entity.TenantId == seeded.TenantId &&
                entity.WarehouseId == seeded.WarehouseId &&
                entity.ProductVariantId == seeded.VariantId)
            .OrderBy(entity => entity.CreatedAt)
            .ThenBy(entity => entity.Id)
            .ToListAsync();
        var saleMovement = movements.Single(entity =>
            entity.ReferenceId == invoiceId &&
            entity.MovementType == StockMovementType.Sale);
        var returnMovement = movements.Single(entity =>
            entity.ReferenceId == returnId &&
            entity.MovementType == StockMovementType.SalesReturn);
        var journals = await context.JournalEntries
            .IgnoreQueryFilters()
            .Include(journal => journal.LedgerEntries)
            .Where(journal =>
                journal.TenantId == seeded.TenantId &&
                (journal.SourceId == invoiceId || journal.SourceId == returnId))
            .OrderBy(journal => journal.SourceType)
            .ThenBy(journal => journal.Id)
            .ToListAsync();
        var outboxMessages = await context.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                (message.BusinessEntityId == invoiceId || message.BusinessEntityId == returnId))
            .OrderBy(message => message.EventType)
            .ThenBy(message => message.Id)
            .ToListAsync();
        var derived = await CalculatePriceCon12DerivedValuationAsync(context, seeded);
        var processedEvents = await context.Set<ProcessedEvent>()
            .IgnoreQueryFilters()
            .CountAsync(entity =>
                entity.EventId == PriceCon12RecalculationEventId &&
                entity.ConsumerName == "PRICE-CON-12-CostBasisRecalculation" &&
                entity.EventType == "CostBasisRecalculated" &&
                entity.TenantId == seeded.TenantId);

        Assert.Equal(PriceCon11ExpectedCostAmount, journals
            .Single(journal => journal.SourceType == "SalesInvoice_COGS")
            .LedgerEntries
            .Sum(entry => entry.DebitAmount));
        Assert.Equal(PriceCon12ExpectedReturnCostAmount, journals
            .Single(journal => journal.SourceType == "Return_COGS")
            .LedgerEntries
            .Sum(entry => entry.DebitAmount));

        var journalFingerprint = string.Join(
            "|",
            journals.Select(journal =>
                $"{journal.Id}:{journal.SourceType}:{journal.IdempotencyKey}:" +
                $"{journal.LedgerEntries.Sum(entry => entry.DebitAmount):0.0000}:" +
                $"{journal.LedgerEntries.Sum(entry => entry.CreditAmount):0.0000}"));
        var outboxFingerprint = string.Join(
            "|",
            outboxMessages.Select(message =>
                $"{message.Id}:{message.EventType}:{message.BusinessEntityId}:{message.IdempotencyKey}:{message.JournalId}:{message.AggregateVersion}"));
        var financialFingerprint = string.Join(
            ";",
            invoice.Id,
            invoice.TotalAmount,
            invoice.AccountingMutationVersion,
            saleItem.Id,
            saleItem.CostPriceSnapshot,
            saleItem.CostAmountSnapshot,
            salesReturn.Id,
            salesReturn.TotalAmount,
            returnItem.Id,
            returnItem.CostPriceSnapshot,
            returnItem.CostAmountSnapshot,
            saleMovement.Id,
            saleMovement.Price,
            saleMovement.QuantityChange,
            returnMovement.Id,
            returnMovement.Price,
            returnMovement.QuantityChange,
            stock.Quantity,
            journalFingerprint,
            outboxFingerprint);

        return new PriceCon12StateSnapshot(
            saleItem.CostPriceSnapshot ?? 0m,
            saleItem.CostAmountSnapshot ?? 0m,
            returnItem.CostPriceSnapshot ?? 0m,
            returnItem.CostAmountSnapshot ?? 0m,
            variant.CostPrice,
            stock.Quantity,
            derived.WeightedAverageMovementCost,
            derived.WeightedMovementInventoryValue,
            stock.Quantity * variant.CostPrice,
            processedEvents,
            financialFingerprint);
    }

    private static async Task SeedPriceCon13ProjectionBaselineAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext)
    {
        await using var context = scope.CreateDbContext(tenantContext);
        await ConfigureSessionTimeoutsAsync(context);

        context.CommerceHubDailyFinancialAggregates.Add(new CommerceHubDailyFinancialAggregate
        {
            DateUtc = PriceCon13ProjectionDateUtc,
            CurrencyCode = "EGP",
            BaseCurrencyCode = "EGP",
            ExchangeRateToBase = 1m,
            AggregatedThroughUtc = SeededUtc.AddMinutes(-1),
            CalculatedAtUtc = SeededUtc,
            LastValidatedAtUtc = SeededUtc,
            IsValid = true
        });

        await context.SaveChangesAsync();
    }

    private static async Task AssertPriceCon13NoPartialProjectionVisibleAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId,
        Guid returnId,
        PriceCon13StateSnapshot baseline,
        ConcurrentQueue<string> progressEntries)
    {
        var current = await LoadPriceCon13StateSnapshotAsync(scope, tenantContext, seeded, invoiceId, returnId);

        Assert.Equal(baseline.ImmutableFinancialState.FinancialFingerprint, current.ImmutableFinancialState.FinancialFingerprint);
        Assert.Equal(baseline.BiSnapshot.Fingerprint, current.BiSnapshot.Fingerprint);
        Assert.Equal(0, current.ProjectionProcessedEvents);
        Assert.Equal(0m, current.ProfitabilityNetProfit);
        Assert.Equal(0m, current.MarginPercentage);

        progressEntries.Enqueue("verification:price-con-13-no-partial-projection-visible");
    }

    private static async Task<PriceCon13StateSnapshot> LoadPriceCon13StateSnapshotAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId,
        Guid returnId)
    {
        var immutableState = await LoadPriceCon12StateSnapshotAsync(scope, tenantContext, seeded, invoiceId, returnId);

        await using var context = scope.CreateDbContext(tenantContext);
        var aggregate = await context.CommerceHubDailyFinancialAggregates
            .AsNoTracking()
            .SingleAsync(row =>
                row.DateUtc == PriceCon13ProjectionDateUtc &&
                row.CurrencyCode == "EGP");
        var processedEvents = await context.ProcessedEvents
            .IgnoreQueryFilters()
            .CountAsync(entity =>
                entity.ConsumerName == PriceCon13ProjectionConsumerName &&
                entity.EventType == PriceCon13ProjectionEventType &&
                entity.TenantId == seeded.TenantId);

        var snapshot = PriceCon13BiSnapshot.FromAggregate(aggregate);
        var netRevenueAfterReturns = Math.Max(0m, snapshot.GrossSales - snapshot.RefundAmount);
        var netProfit = snapshot.NetRevenue;
        var marginPercentage = netRevenueAfterReturns == 0m
            ? 0m
            : Math.Round((netProfit / netRevenueAfterReturns) * 100m, 2, MidpointRounding.AwayFromZero);

        return new PriceCon13StateSnapshot(
            immutableState,
            snapshot,
            processedEvents,
            netProfit,
            marginPercentage,
            snapshot.Fingerprint);
    }

    private static async Task<PriceCon13ProjectionWorkerResult> ExecutePriceCon13FinancialProjectionWorkerAsync(
        string connectionString,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId,
        Guid returnId,
        string workerName,
        Guid eventId,
        DateTime eventOccurredAtUtc,
        ConcurrentQueue<string> progressEntries,
        params IInterceptor[] interceptors)
    {
        await using var context = CreateDbContext(connectionString, tenantContext, interceptors);
        await ConfigureSessionTimeoutsAsync(context);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            progressEntries.Enqueue($"{workerName}:financial-projection-ready");
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

            var nowUtc = DateTime.UtcNow;
            var processedId = Guid.NewGuid();
            var inserted = await context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ""ProcessedEvents""
                    (""Id"", ""EventId"", ""ConsumerName"", ""EventType"", ""EntityId"", ""TenantId"", ""CorrelationId"", ""ProcessedAtUtc"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                VALUES
                    ({processedId}, {eventId}, {PriceCon13ProjectionConsumerName}, {PriceCon13ProjectionEventType}, {invoiceId}, {seeded.TenantId}, {workerName}, {nowUtc}, {nowUtc}, NULL, FALSE)
                ON CONFLICT (""EventId"", ""ConsumerName"", ""EventType"") WHERE ""IsDeleted"" = FALSE DO NOTHING;");

            var aggregate = await context.CommerceHubDailyFinancialAggregates
                .SingleAsync(row =>
                    row.DateUtc == PriceCon13ProjectionDateUtc &&
                    row.CurrencyCode == "EGP");

            if (inserted == 0)
            {
                await transaction.CommitAsync();
                stopwatch.Stop();
                progressEntries.Enqueue($"{workerName}:financial-projection-duplicate-skipped");

                return new PriceCon13ProjectionWorkerResult(
                    workerName,
                    InsertedProcessedEvent: false,
                    AppliedProjection: false,
                    RejectedAsStale: false,
                    SnapshotFingerprint: PriceCon13BiSnapshot.FromAggregate(aggregate).Fingerprint,
                    Duration: stopwatch.Elapsed,
                    Exception: null);
            }

            if (aggregate.AggregatedThroughUtc.HasValue &&
                aggregate.AggregatedThroughUtc.Value >= eventOccurredAtUtc)
            {
                await transaction.CommitAsync();
                stopwatch.Stop();
                progressEntries.Enqueue($"{workerName}:financial-projection-stale-rejected");

                return new PriceCon13ProjectionWorkerResult(
                    workerName,
                    InsertedProcessedEvent: true,
                    AppliedProjection: false,
                    RejectedAsStale: true,
                    SnapshotFingerprint: PriceCon13BiSnapshot.FromAggregate(aggregate).Fingerprint,
                    Duration: stopwatch.Elapsed,
                    Exception: null);
            }

            var projection = await BuildPriceCon13ProjectionFromSqlAuthorityAsync(
                context,
                invoiceId,
                returnId,
                eventOccurredAtUtc);

            ApplyPriceCon13Projection(projection, aggregate);

            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            stopwatch.Stop();
            progressEntries.Enqueue($"{workerName}:financial-projection-committed");

            return new PriceCon13ProjectionWorkerResult(
                workerName,
                InsertedProcessedEvent: true,
                AppliedProjection: true,
                RejectedAsStale: false,
                SnapshotFingerprint: projection.Fingerprint,
                Duration: stopwatch.Elapsed,
                Exception: null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            progressEntries.Enqueue($"{workerName}:financial-projection-threw:{DescribeException(ex)}");

            return new PriceCon13ProjectionWorkerResult(
                workerName,
                InsertedProcessedEvent: false,
                AppliedProjection: false,
                RejectedAsStale: false,
                SnapshotFingerprint: string.Empty,
                Duration: stopwatch.Elapsed,
                Exception: ex);
        }
    }

    private static async Task<string> ExecutePriceCon13FinancialProjectionRebuildAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId,
        Guid returnId,
        DateTime rebuildBoundaryUtc,
        ConcurrentQueue<string> progressEntries)
    {
        await using var context = scope.CreateDbContext(tenantContext);
        await ConfigureSessionTimeoutsAsync(context);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var aggregate = await context.CommerceHubDailyFinancialAggregates
            .SingleAsync(row =>
                row.DateUtc == PriceCon13ProjectionDateUtc &&
                row.CurrencyCode == "EGP");
        var projection = await BuildPriceCon13ProjectionFromSqlAuthorityAsync(
            context,
            invoiceId,
            returnId,
            rebuildBoundaryUtc);

        ApplyPriceCon13Projection(projection, aggregate);

        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        progressEntries.Enqueue("verification:price-con-13-rebuild-recomputed-from-sql-authority");
        return projection.Fingerprint;
    }

    private static async Task<PriceCon13BiSnapshot> BuildPriceCon13ProjectionFromSqlAuthorityAsync(
        ApplicationDbContext context,
        Guid invoiceId,
        Guid returnId,
        DateTime aggregatedThroughUtc)
    {
        var invoice = await context.SalesInvoices
            .Include(entity => entity.Items)
            .SingleAsync(entity => entity.Id == invoiceId);
        var saleItem = Assert.Single(invoice.Items);
        var salesReturn = await context.Returns
            .Include(entity => entity.Items)
            .SingleAsync(entity => entity.Id == returnId);
        var returnItem = Assert.Single(salesReturn.Items);

        var saleRevenue = invoice.TotalAmount;
        var saleCost = saleItem.CostAmountSnapshot ?? 0m;
        var returnRevenue = salesReturn.TotalAmount;
        var returnCost = returnItem.CostAmountSnapshot ?? 0m;
        var grossProfitBeforeReturns = saleRevenue - saleCost;
        var netProfitAfterReturns = (saleRevenue - returnRevenue) - (saleCost - returnCost);

        return new PriceCon13BiSnapshot(
            GrossSales: saleRevenue,
            GrossCommission: grossProfitBeforeReturns,
            PromoCost: 0m,
            PromoReversalAmount: returnCost,
            RefundAmount: returnRevenue,
            RefundImpactOnRevenue: returnRevenue,
            NetRevenue: netProfitAfterReturns,
            OrdersCount: 1,
            RefundCount: 1,
            RefundedOrdersCount: 1,
            AggregatedThroughUtc: aggregatedThroughUtc,
            CalculatedAtUtc: DateTime.UtcNow);
    }

    private static void ApplyPriceCon13Projection(
        PriceCon13BiSnapshot source,
        CommerceHubDailyFinancialAggregate target)
    {
        target.BaseCurrencyCode = "EGP";
        target.ExchangeRateToBase = 1m;
        target.GrossSales = source.GrossSales;
        target.GrossCommission = source.GrossCommission;
        target.PromoCost = source.PromoCost;
        target.PromoReversalAmount = source.PromoReversalAmount;
        target.RefundAmount = source.RefundAmount;
        target.RefundImpactOnRevenue = source.RefundImpactOnRevenue;
        target.NetRevenue = source.NetRevenue;
        target.GrossSalesBase = source.GrossSales;
        target.GrossCommissionBase = source.GrossCommission;
        target.PromoCostBase = source.PromoCost;
        target.PromoReversalAmountBase = source.PromoReversalAmount;
        target.RefundAmountBase = source.RefundAmount;
        target.RefundImpactOnRevenueBase = source.RefundImpactOnRevenue;
        target.NetRevenueBase = source.NetRevenue;
        target.OrdersCount = source.OrdersCount;
        target.RefundCount = source.RefundCount;
        target.RefundedOrdersCount = source.RefundedOrdersCount;
        target.IsValid = true;
        target.ValidationError = null;
        target.LastValidatedAtUtc = source.CalculatedAtUtc;
        target.AggregatedThroughUtc = source.AggregatedThroughUtc;
        target.CalculatedAtUtc = source.CalculatedAtUtc;
    }

    private static async Task<PriceCon12DerivedValuation> CalculatePriceCon12DerivedValuationAsync(
        ApplicationDbContext context,
        SeededScenario seeded)
    {
        var stockQuantity = await context.ProductVariantStocks
            .Where(stock =>
                stock.TenantId == seeded.TenantId &&
                stock.WarehouseId == seeded.WarehouseId &&
                stock.VariantId == seeded.VariantId)
            .Select(stock => stock.Quantity)
            .SingleAsync();
        var movementValue = await context.StockMovements
            .Where(movement =>
                movement.TenantId == seeded.TenantId &&
                movement.WarehouseId == seeded.WarehouseId &&
                movement.ProductVariantId == seeded.VariantId &&
                movement.Price.HasValue)
            .SumAsync(movement => movement.QuantityChange * movement.Price!.Value);
        var weightedAverageCost = stockQuantity > 0m
            ? Math.Round(movementValue / stockQuantity, 6, MidpointRounding.AwayFromZero)
            : 0m;

        return new PriceCon12DerivedValuation(
            stockQuantity,
            weightedAverageCost,
            movementValue);
    }

    private static string BuildPriceCon11FinancialFingerprint(
        SalesInvoice invoice,
        SalesInvoiceItem item,
        StockMovement movement,
        AccountingOutboxMessage outbox,
        IReadOnlyCollection<PointOfSale.Core.Entities.Accounting.JournalEntry> journals)
    {
        var journalFingerprint = string.Join(
            "|",
            journals
                .OrderBy(journal => journal.SourceType, StringComparer.Ordinal)
                .ThenBy(journal => journal.Id)
                .Select(journal =>
                    $"{journal.Id}:{journal.SourceType}:{journal.IdempotencyKey}:" +
                    $"{journal.LedgerEntries.Sum(entry => entry.DebitAmount):0.0000}:" +
                    $"{journal.LedgerEntries.Sum(entry => entry.CreditAmount):0.0000}"));

        return string.Join(
            ";",
            invoice.Id,
            invoice.TotalAmount,
            invoice.PaidAmount,
            invoice.AccountingMutationVersion,
            item.Id,
            item.CostPriceSnapshot,
            item.CostAmountSnapshot,
            movement.Id,
            movement.Price,
            movement.QuantityChange,
            movement.QuantityAfter,
            outbox.Id,
            outbox.JournalId,
            outbox.AggregateVersion,
            journalFingerprint);
    }

    private static async Task ConfigurePriceCon11SeedAsync(
        ApplicationDbContext context,
        SeededScenario seeded)
    {
        var product = await context.Products.SingleAsync(entity => entity.Id == seeded.ProductId);
        var variant = await context.ProductVariants.SingleAsync(entity => entity.Id == seeded.VariantId);
        var productUnit = await context.ProductUnits.SingleAsync(entity =>
            entity.ProductId == seeded.ProductId &&
            entity.UnitId == seeded.UnitId);

        product.PurchasePrice = PriceCon11InitialCostPrice;
        product.SalePrice = PriceCon11SaleUnitPrice;
        product.OnlinePrice = PriceCon11SaleUnitPrice;
        variant.Price = PriceCon11SaleUnitPrice;
        variant.OnlinePrice = PriceCon11SaleUnitPrice;
        variant.CostPrice = PriceCon11InitialCostPrice;
        productUnit.SellingPrice = PriceCon11SaleUnitPrice;
        productUnit.PurchasePrice = PriceCon11InitialCostPrice;

        await context.SaveChangesAsync();
    }

    private static async Task SeedPriceCon12OpeningValuationMovementAsync(
        ApplicationDbContext context,
        SeededScenario seeded)
    {
        context.StockMovements.Add(new StockMovement
        {
            Id = Guid.NewGuid(),
            TenantId = seeded.TenantId,
            WarehouseId = seeded.WarehouseId,
            ProductId = seeded.ProductId,
            ProductVariantId = seeded.VariantId,
            MovementType = StockMovementType.OpeningBalance,
            UnitId = seeded.UnitId,
            ConversionFactor = 1m,
            QuantityChange = InitialStockQuantity,
            QuantityAfter = InitialStockQuantity,
            AvailableAfter = InitialStockQuantity,
            ReservedAfter = 0m,
            DamagedAfter = 0m,
            ReferenceNumber = "PRICE-CON-12-OPENING",
            Notes = "PRICE-CON-12 deterministic opening valuation basis",
            Price = PriceCon11InitialCostPrice,
            IsOpeningBalance = true,
            CreatedAt = SeededUtc.AddMinutes(-5)
        });

        await context.SaveChangesAsync();
    }

    private static async Task SeedStockCon01ReadModelsAsync(
        IMongoDatabase mongoDatabase,
        SeededScenario seeded)
    {
        var productDocument = new ProductDocument
        {
            TenantId = seeded.TenantId,
            ProductId = seeded.ProductId,
            Name = "STOCK-CON-01 Product",
            SKU = "STOCK-CON-01-VAR",
            Slug = "stock-con-01-product",
            Price = AuthoritativeUnitPrice,
            OnlinePrice = AuthoritativeUnitPrice,
            PurchasePrice = ProductCostPrice,
            CategoryId = CategoryId,
            CategoryName = "Reliability Tests",
            Images = "https://images.example.test/stock-con-01.png",
            IsOnlineVisible = true,
            IsActive = true,
            TrackStock = true,
            StockQuantity = (int)StockCon01InitialStockQuantity,
            HasVariants = false,
            IsAffiliateEnabled = true,
            AffiliateEnabledAtUtc = SeededUtc,
            CreatedAtUtc = SeededUtc,
            SyncedAtUtc = SeededUtc,
            LastEventVersion = 1,
            Variants =
            [
                new ProductVariantDocument
                {
                    VariantId = seeded.VariantId,
                    SKU = "STOCK-CON-01-VAR",
                    Price = AuthoritativeUnitPrice,
                    OnlinePrice = AuthoritativeUnitPrice,
                    CostPrice = ProductCostPrice,
                    IsDefault = true,
                    IsActive = true,
                    TotalStock = StockCon01InitialStockQuantity,
                    SortOrder = 0,
                    SyncedAtUtc = SeededUtc,
                    LastEventVersion = 1
                }
            ]
        };

        var commerceHubDocument = new CommerceHubProductDocument
        {
            Id = Guid.Parse("7f0f08b8-3e8a-4d42-91f4-8435d6d4b101"),
            TenantId = seeded.TenantId,
            StoreName = "STOCK-CON-01 Store",
            ProductId = seeded.ProductId,
            VariantId = seeded.VariantId,
            ProductName = productDocument.Name,
            Price = AuthoritativeUnitPrice,
            CategoryId = CategoryId,
            Currency = "EGP",
            StoreShippingFee = 0m,
            InStock = true,
            AvailableStock = StockCon01InitialStockQuantity,
            IsPublished = true,
            CreatedAtUtc = SeededUtc,
            UpdatedAtUtc = SeededUtc,
            LastListingEventOccurredAtUtc = SeededUtc,
            LastListingEventVersion = 1,
            SearchSyncStatus = CommerceHubProductDocument.SearchSyncSynced,
            SearchRetryCount = 0
        };

        var publicAffiliateDocument = new PublicAffiliateProductDocument
        {
            ProductId = seeded.ProductId,
            ProductSlug = productDocument.Slug!,
            ProductName = productDocument.Name,
            Description = string.Empty,
            ThumbnailUrl = "https://images.example.test/stock-con-01.png",
            Price = AuthoritativeUnitPrice,
            Currency = "EGP",
            StoreName = "SI-CON-01 Tenant",
            StoreSlug = "si-con-01-store",
            CategoryName = "Reliability Tests",
            HasVariants = false,
            TotalAvailableStock = (int)StockCon01InitialStockQuantity,
            InStock = true,
            IsActive = true,
            IsAffiliateEnabled = true,
            AffiliateEnabledAtUtc = SeededUtc,
            TenantId = seeded.TenantId,
            StorefrontBaseUrl = "https://si-con-01-store.example.test"
        };

        await mongoDatabase.GetCollection<ProductDocument>("products").InsertOneAsync(productDocument);
        await mongoDatabase.GetCollection<CommerceHubProductDocument>("commercehub_products").InsertOneAsync(commerceHubDocument);
        await mongoDatabase.GetCollection<PublicAffiliateProductDocument>("public_affiliate_products").InsertOneAsync(publicAffiliateDocument);
    }

    private static async Task<ProductVariantStockChangedEvent> LoadSingleStockCon01StockEventAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId)
    {
        await using var context = scope.CreateDbContext(tenantContext);
        var message = await context.StockSyncOutboxMessages
            .IgnoreQueryFilters()
            .SingleAsync(item =>
                item.TenantId == seeded.TenantId &&
                item.VariantId == seeded.VariantId &&
                item.WarehouseId == seeded.WarehouseId);

        var stockEvent = JsonSerializer.Deserialize<ProductVariantStockChangedEvent>(message.PayloadJson)
            ?? throw new XunitException("STOCK-CON-01 stock sync payload could not be deserialized.");

        Assert.Equal(0m, stockEvent.TotalStockQuantity);
        Assert.Equal(0m, stockEvent.NewQuantity);
        Assert.Equal("Sale", stockEvent.Reason);
        Assert.Equal("SalesInvoice", stockEvent.ReferenceType);
        Assert.Equal(invoiceId, stockEvent.ReferenceId);
        return stockEvent;
    }

    private static async Task<ProductVariantStockChangedEvent> LoadStockCon04ReturnStockEventAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid returnId)
    {
        await using var context = scope.CreateDbContext(tenantContext);
        var messages = await context.StockSyncOutboxMessages
            .IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == seeded.TenantId &&
                item.VariantId == seeded.VariantId &&
                item.WarehouseId == seeded.WarehouseId)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync();

        var stockEvents = messages
            .Select(message => JsonSerializer.Deserialize<ProductVariantStockChangedEvent>(message.PayloadJson)
                               ?? throw new XunitException("STOCK-CON-04 stock sync payload could not be deserialized."))
            .ToList();

        var returnEvent = stockEvents.Single(item =>
            item.ReferenceId == returnId &&
            string.Equals(item.ReferenceType, "SalesReturn", StringComparison.Ordinal));

        Assert.Equal(StockCon01SaleQuantity, returnEvent.NewQuantity);
        Assert.Equal(StockCon01SaleQuantity, returnEvent.TotalStockQuantity);
        Assert.Equal("SalesReturn", returnEvent.Reason);
        return returnEvent;
    }

    private static async Task ApplyStockCon01ProjectionAsync(
        string connectionString,
        TestTenantContext tenantContext,
        string mongoConnectionString,
        IMongoDatabase mongoDatabase,
        ProductVariantStockChangedEvent stockEvent,
        ConcurrentQueue<string> progressEntries)
    {
        await using var projectionContext = CreateDbContext(connectionString, tenantContext);
        var mongoDbContext = CreateMongoDbContext(mongoConnectionString, mongoDatabase.DatabaseNamespace.DatabaseName);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storefront:UrlTemplate"] = "https://{slug}.example.test"
            })
            .Build();

        var variantProjectionConsumer = new ProductVariantProjectionConsumer(
            mongoDatabase,
            CreateRedisMultiplexerMock().Object,
            CreateProductSearchIndexServiceMock().Object,
            Options.Create(new RedisSettings { InstanceName = "stock-con-test" }),
            NullLogger<ProductVariantProjectionConsumer>.Instance);
        var productStockConsumer = new ProductStockChangedConsumer(
            mongoDatabase,
            NullLogger<ProductStockChangedConsumer>.Instance);
        var commerceHubConsumer = new CommerceHubProjectionConsumer(
            mongoDbContext,
            projectionContext,
            CreateCommerceHubSearchIndexServiceMock().Object,
            NullLogger<CommerceHubProjectionConsumer>.Instance);
        var affiliatePublicConsumer = new AffiliateProductPolicyProjectionConsumer(
            mongoDbContext,
            CreateElasticClientMock().Object,
            Options.Create(new ElasticsearchSettings
            {
                Enabled = false,
                PublicAffiliateProductsIndexName = "stock-con-affiliate-test"
            }),
            projectionContext,
            configuration,
            NullLogger<AffiliateProductPolicyProjectionConsumer>.Instance);

        await variantProjectionConsumer.Consume(BuildConsumeContext(stockEvent).Object);
        await productStockConsumer.Consume(BuildConsumeContext(stockEvent).Object);
        await commerceHubConsumer.Consume(BuildConsumeContext(stockEvent).Object);
        await affiliatePublicConsumer.Consume(BuildConsumeContext(stockEvent).Object);

        progressEntries.Enqueue($"projection:stock-con-01-applied:{stockEvent.EventId}");
    }

    private static async Task AssertStockCon01ProjectionConvergenceAsync(
        IMongoDatabase mongoDatabase,
        SeededScenario seeded,
        decimal expectedStock)
    {
        var product = await mongoDatabase.GetCollection<ProductDocument>("products")
            .Find(item => item.TenantId == seeded.TenantId && item.ProductId == seeded.ProductId)
            .SingleAsync();
        var variant = product.Variants.Single(item => item.VariantId == seeded.VariantId);
        var listing = await mongoDatabase.GetCollection<CommerceHubProductDocument>("commercehub_products")
            .Find(item => item.TenantId == seeded.TenantId && item.VariantId == seeded.VariantId)
            .SingleAsync();
        var publicAffiliateProduct = await mongoDatabase.GetCollection<PublicAffiliateProductDocument>("public_affiliate_products")
            .Find(item => item.TenantId == seeded.TenantId && item.ProductId == seeded.ProductId)
            .FirstOrDefaultAsync();

        Assert.Equal((int)expectedStock, product.StockQuantity);
        Assert.Equal(expectedStock, variant.TotalStock);
        Assert.Equal(expectedStock > 0m, product.InStock);
        Assert.Equal(expectedStock > 0m, variant.InStock);
        Assert.Equal(expectedStock, listing.AvailableStock);
        Assert.Equal(expectedStock > 0m, listing.InStock);

        if (expectedStock > 0m)
        {
            Assert.NotNull(publicAffiliateProduct);
            Assert.Equal((int)expectedStock, publicAffiliateProduct!.TotalAvailableStock);
            Assert.True(publicAffiliateProduct.InStock);
        }
        else
        {
            Assert.Null(publicAffiliateProduct);
        }
    }

    private static async Task<StockCon03ProjectionVisibilitySnapshot> AssertStockCon03ProjectionVisibilityAsync(
        IMongoDatabase mongoDatabase,
        SeededScenario seeded,
        decimal expectedStock,
        ProductVariantStockChangedEvent expectedEvent,
        ConcurrentQueue<string> progressEntries)
    {
        var snapshot = await LoadStockCon03ProjectionVisibilityAsync(mongoDatabase, seeded);

        Assert.Equal(expectedStock, snapshot.StorefrontProductStockQuantity);
        Assert.Equal(expectedStock, snapshot.StorefrontVariantStockQuantity);
        Assert.Equal(expectedStock, snapshot.CommerceHubAvailableStock);
        Assert.Equal(expectedStock > 0m, snapshot.StorefrontProductInStock);
        Assert.Equal(expectedStock > 0m, snapshot.StorefrontVariantInStock);
        Assert.Equal(expectedStock > 0m, snapshot.CommerceHubInStock);
        Assert.Equal(expectedStock > 0m, snapshot.AffiliateVisible);
        Assert.Equal(expectedEvent.EventId, snapshot.StorefrontVariantLastStockEventId);
        Assert.Equal(expectedEvent.EventId, snapshot.StorefrontProductLastStockEventId);
        Assert.Equal(expectedEvent.EventId, snapshot.CommerceHubLastEventId);
        Assert.Equal(expectedEvent.EventVersion, snapshot.StorefrontVariantLastStockEventVersion);
        Assert.Equal(expectedEvent.EventVersion, snapshot.StorefrontProductLastStockEventVersion);
        Assert.Equal(expectedEvent.EventVersion, snapshot.CommerceHubLastStockEventVersion);
        Assert.Equal(
            PointOfSale.Infrastructure.Utilities.StockProjectionTimestamp.NormalizeUtc(expectedEvent.StockUpdatedAtUtc),
            snapshot.StorefrontVariantLastStockUpdatedAtUtc);
        Assert.Equal(
            PointOfSale.Infrastructure.Utilities.StockProjectionTimestamp.NormalizeUtc(expectedEvent.StockUpdatedAtUtc),
            snapshot.StorefrontProductLastStockUpdatedAtUtc);
        Assert.Equal(
            PointOfSale.Infrastructure.Utilities.StockProjectionTimestamp.NormalizeUtc(expectedEvent.StockUpdatedAtUtc),
            snapshot.CommerceHubStockUpdatedAtUtc);

        if (expectedStock > 0m)
        {
            Assert.Equal((int)expectedStock, snapshot.AffiliateTotalAvailableStock);
            Assert.True(snapshot.AffiliateInStock);
        }
        else
        {
            Assert.Null(snapshot.AffiliateTotalAvailableStock);
            Assert.False(snapshot.AffiliateInStock);
        }

        progressEntries.Enqueue("verification:stock-con-03-projection-visibility-converged");
        return snapshot;
    }

    private static async Task<StockCon03ProjectionVisibilitySnapshot> LoadStockCon03ProjectionVisibilityAsync(
        IMongoDatabase mongoDatabase,
        SeededScenario seeded)
    {
        var products = await mongoDatabase.GetCollection<ProductDocument>("products")
            .Find(item => item.TenantId == seeded.TenantId && item.ProductId == seeded.ProductId)
            .ToListAsync();
        var listings = await mongoDatabase.GetCollection<CommerceHubProductDocument>("commercehub_products")
            .Find(item => item.TenantId == seeded.TenantId && item.VariantId == seeded.VariantId)
            .ToListAsync();
        var publicAffiliateProducts = await mongoDatabase.GetCollection<PublicAffiliateProductDocument>("public_affiliate_products")
            .Find(item => item.TenantId == seeded.TenantId && item.ProductId == seeded.ProductId)
            .ToListAsync();

        var product = Assert.Single(products);
        var variant = Assert.Single(product.Variants.Where(item => item.VariantId == seeded.VariantId));
        var listing = Assert.Single(listings);
        var publicAffiliateProduct = publicAffiliateProducts.SingleOrDefault();

        return new StockCon03ProjectionVisibilitySnapshot(
            StorefrontProductStockQuantity: product.StockQuantity,
            StorefrontProductInStock: product.InStock,
            StorefrontProductLastStockEventId: product.LastStockEventId,
            StorefrontProductLastStockUpdatedAtUtc: PointOfSale.Infrastructure.Utilities.StockProjectionTimestamp.NormalizeUtc(product.LastStockUpdatedAtUtc),
            StorefrontProductLastStockEventVersion: product.LastStockEventVersion,
            StorefrontVariantStockQuantity: variant.TotalStock,
            StorefrontVariantInStock: variant.InStock,
            StorefrontVariantLastStockEventId: variant.LastStockEventId,
            StorefrontVariantLastStockUpdatedAtUtc: PointOfSale.Infrastructure.Utilities.StockProjectionTimestamp.NormalizeUtc(
                variant.LastStockUpdatedAtUtc ?? variant.StockUpdatedAtUtc),
            StorefrontVariantLastStockEventVersion: variant.LastStockEventVersion,
            CommerceHubAvailableStock: listing.AvailableStock,
            CommerceHubInStock: listing.InStock,
            CommerceHubLastEventId: listing.LastEventId,
            CommerceHubStockUpdatedAtUtc: PointOfSale.Infrastructure.Utilities.StockProjectionTimestamp.NormalizeUtc(listing.StockUpdatedAtUtc),
            CommerceHubLastStockEventVersion: listing.LastStockEventVersion,
            AffiliateVisible: publicAffiliateProduct != null,
            AffiliateTotalAvailableStock: publicAffiliateProduct?.TotalAvailableStock,
            AffiliateInStock: publicAffiliateProduct?.InStock ?? false);
    }

    private static async Task<ProductVariantStockChangedEvent> BuildStockCon03SqlTruthRebuildEventAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        ProductVariantStockChangedEvent previousEvent)
    {
        await using var context = scope.CreateDbContext(tenantContext);

        var variant = await context.ProductVariants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(item => item.TenantId == seeded.TenantId && item.Id == seeded.VariantId);
        var warehouse = await context.Warehouses
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(item => item.TenantId == seeded.TenantId && item.Id == seeded.WarehouseId);
        var totalStock = await context.ProductVariantStocks
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == seeded.TenantId && item.VariantId == seeded.VariantId)
            .SumAsync(item => item.Quantity - item.ReservedQuantity);
        var warehouseStock = await context.ProductVariantStocks
            .IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == seeded.TenantId &&
                item.VariantId == seeded.VariantId &&
                item.WarehouseId == seeded.WarehouseId)
            .Select(item => item.Quantity - item.ReservedQuantity)
            .SingleAsync();
        var sqlEventVersion = ProductEventVersionHelper.ToProductEventVersion(variant.ProjectionMutationVersion);
        var rebuildTimestamp = variant.StockUpdatedAtUtc ?? previousEvent.StockUpdatedAtUtc;

        return new ProductVariantStockChangedEvent
        {
            EventId = Guid.Parse("7f0f08b8-3e8a-4d42-91f4-8435d6d4b103"),
            TenantId = seeded.TenantId,
            VariantId = seeded.VariantId,
            ProductId = seeded.ProductId,
            SKU = variant.SKU,
            WarehouseId = seeded.WarehouseId,
            WarehouseName = warehouse.Name,
            OldQuantity = Math.Max(0m, warehouseStock),
            NewQuantity = Math.Max(0m, warehouseStock),
            Reason = "StockCon03SqlTruthRebuild",
            ReferenceId = previousEvent.ReferenceId,
            ReferenceType = "StockCon03Rebuild",
            MutationSource = "StockCon03TestRebuild",
            TotalStockQuantity = Math.Max(0m, totalStock),
            CorrelationId = previousEvent.CorrelationId,
            OccurredAtUtc = rebuildTimestamp,
            StockUpdatedAtUtc = rebuildTimestamp,
            EventVersion = sqlEventVersion
        };
    }

    private static async Task<CommerceHubInventoryReconciliationResult> ExecuteStockCon03InventoryReconciliationAsync(
        string connectionString,
        TestTenantContext tenantContext)
    {
        await using var context = CreateDbContext(connectionString, tenantContext);
        var consistencyService = new InventoryConsistencyService(
            context,
            NullLogger<InventoryConsistencyService>.Instance);
        var reconciliation = new CommerceHubInventoryReconciliationJob(
            context,
            consistencyService,
            NullLogger<CommerceHubInventoryReconciliationJob>.Instance);

        return await reconciliation.ExecuteAsync();
    }

    private static ProductVariantStockChangedEvent CloneStockEventForStaleReplay(
        ProductVariantStockChangedEvent current)
    {
        return new ProductVariantStockChangedEvent
        {
            EventId = Guid.Parse("7f0f08b8-3e8a-4d42-91f4-8435d6d4b102"),
            TenantId = current.TenantId,
            VariantId = current.VariantId,
            ProductId = current.ProductId,
            SKU = current.SKU,
            WarehouseId = current.WarehouseId,
            WarehouseName = current.WarehouseName,
            OldQuantity = current.OldQuantity,
            NewQuantity = StockCon01InitialStockQuantity,
            Reason = current.Reason,
            ReferenceId = current.ReferenceId,
            ReferenceType = current.ReferenceType,
            MutationSource = current.MutationSource,
            TotalStockQuantity = StockCon01InitialStockQuantity,
            CorrelationId = current.CorrelationId,
            OccurredAtUtc = current.OccurredAtUtc.AddSeconds(-10),
            StockUpdatedAtUtc = current.StockUpdatedAtUtc.AddSeconds(-10),
            EventVersion = current.EventVersion
        };
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

    private static async Task<StockCon01MongoDatabaseScope> CreateIsolatedMongoDatabaseScopeAsync(string scenarioKey)
    {
        var connectionString = GetRequiredMongoConnectionString();
        var client = new MongoClient(connectionString);
        var databaseName = $"stock_con_01_{TestConfiguration.BuildDeterministicSuffix($"{scenarioKey}_{Guid.NewGuid():N}")}";
        var database = client.GetDatabase(databaseName);

        await database.ListCollectionNamesAsync();

        return new StockCon01MongoDatabaseScope(client, databaseName, database);
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

        throw new InvalidOperationException("MongoDB connection string was not configured for STOCK-CON-01.");
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

    private static Mock<IProductSearchIndexService> CreateProductSearchIndexServiceMock()
    {
        var service = new Mock<IProductSearchIndexService>(MockBehavior.Loose);
        service
            .Setup(entry => entry.IndexAsync(
                It.IsAny<ProductSearchDocument>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        service
            .Setup(entry => entry.DeleteAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return service;
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

    private static Mock<Nest.IElasticClient> CreateElasticClientMock()
    {
        return new Mock<Nest.IElasticClient>(MockBehavior.Loose);
    }

    private static async Task<SeededScenario> SeedScenarioAsync(
        ApplicationDbContext context,
        decimal initialStockQuantity = InitialStockQuantity)
    {
        await EnsureSalesInvoiceNumberSequenceAsync(context);
        await EnsureReliabilityScenarioSchemaAsync(context);

        context.Tenants.Add(new Tenant
        {
            Id = TenantId,
            Name = "SI-CON-01 Tenant",
            Slug = "si-con-01-store",
            StorefrontBaseUrl = "https://si-con-01-store.example.test",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });

        await context.SaveChangesAsync();

        var accountSeeder = new TenantAccountSeeder(
            context,
            NullLogger<TenantAccountSeeder>.Instance);
        await accountSeeder.SeedAsync(TenantId);

        context.Categories.Add(new Category
        {
            Id = CategoryId,
            TenantId = TenantId,
            Name = "Reliability Tests",
            CreatedAt = SeededUtc
        });

        context.Customers.Add(new Customer
        {
            Id = CustomerId,
            TenantId = TenantId,
            Name = "SI-CON-01 Customer",
            Balance = 0m,
            CreatedAt = SeededUtc
        });

        context.Warehouses.Add(new Warehouse
        {
            Id = WarehouseId,
            TenantId = TenantId,
            Name = "SI-CON-01 Warehouse",
            Code = "SI-CON-01-WH",
            IsActive = true,
            CreatedAt = SeededUtc
        });

        context.Units.Add(new UnitEntity
        {
            Id = UnitId,
            TenantId = TenantId,
            Name = "Piece",
            Symbol = "pc",
            IsActive = true,
            CreatedAt = SeededUtc
        });

        context.Products.Add(new Product
        {
            Id = ProductId,
            TenantId = TenantId,
            Name = "SI-CON-01 Product",
            Slug = "si-con-01-product",
            CategoryId = CategoryId,
            PurchasePrice = ProductCostPrice,
            SalePrice = AuthoritativeUnitPrice,
            OnlinePrice = AuthoritativeUnitPrice,
            PriceIncludesTax = false,
            TaxRate = 0m,
            Unit = "Piece",
            TrackStock = true,
            IsActive = true,
            IsOnlineVisible = true,
            CreatedAt = SeededUtc
        });

        context.ProductVariants.Add(new ProductVariant
        {
            Id = VariantId,
            TenantId = TenantId,
            ProductId = ProductId,
            SKU = "SI-CON-01-VAR",
            Price = AuthoritativeUnitPrice,
            OnlinePrice = AuthoritativeUnitPrice,
            CostPrice = ProductCostPrice,
            IsDefault = true,
            IsActive = true,
            CreatedAt = SeededUtc
        });

        context.ProductUnits.Add(new ProductUnit
        {
            Id = ProductUnitId,
            ProductId = ProductId,
            UnitId = UnitId,
            ConversionFactor = 1m,
            SellingPrice = AuthoritativeUnitPrice,
            PurchasePrice = ProductCostPrice,
            IsDefault = true,
            IsBaseUnit = true,
            CreatedAt = SeededUtc
        });

        context.ProductVariantStocks.Add(new ProductVariantStock
        {
            Id = VariantStockId,
            TenantId = TenantId,
            WarehouseId = WarehouseId,
            VariantId = VariantId,
            Quantity = initialStockQuantity,
            ReservedQuantity = 0m,
            DamagedQuantity = 0m,
            CreatedAt = SeededUtc,
            UpdatedAt = SeededUtc
        });

        await context.SaveChangesAsync();

        return new SeededScenario(
            TenantId,
            CustomerId,
            WarehouseId,
            ProductId,
            VariantId,
            UnitId);
    }

    private static async Task EnsureSalesInvoiceNumberSequenceAsync(ApplicationDbContext context)
    {
        // SI-CON-01 targets contested stock mutation semantics. Create the sales invoice sequence
        // explicitly so the scenario is not derailed by unrelated document-number schema drift.
        await context.Database.ExecuteSqlRawAsync(
            $"""
            CREATE SEQUENCE IF NOT EXISTS "{SalesInvoiceSequenceName}"
            START WITH 1
            INCREMENT BY 1
            NO MINVALUE
            NO MAXVALUE
            CACHE 1;
            ALTER SEQUENCE "{SalesInvoiceSequenceName}" RESTART WITH 1;
            """);
    }

    private static async Task EnsureReliabilityScenarioSchemaAsync(ApplicationDbContext context)
    {
        // Historical migrations created AccountingOutboxMessages.CorrelationId as uuid in some
        // test database chains. Current code stores string correlation IDs, and SI-CON scenarios
        // need the accounting outbox to behave like the current model before testing replay safety.
        await context.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = current_schema()
                      AND table_name = 'AccountingOutboxMessages'
                      AND column_name = 'CorrelationId'
                      AND udt_name = 'uuid') THEN
                    ALTER TABLE "AccountingOutboxMessages"
                    ALTER COLUMN "CorrelationId" TYPE character varying(100)
                    USING "CorrelationId"::text;
                END IF;

                ALTER TABLE "StockSyncOutboxMessages"
                    ADD COLUMN IF NOT EXISTS "WarehouseId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';

                ALTER TABLE "StockSyncOutboxMessages"
                    ADD COLUMN IF NOT EXISTS "EventVersion" integer NOT NULL DEFAULT 1;

                ALTER TABLE "SalesInvoiceItems"
                    ADD COLUMN IF NOT EXISTS "CostPriceSnapshot" numeric(18,6) NULL;

                ALTER TABLE "SalesInvoiceItems"
                    ADD COLUMN IF NOT EXISTS "CostAmountSnapshot" numeric(18,2) NULL;

                ALTER TABLE "ReturnItems"
                    ADD COLUMN IF NOT EXISTS "CostPriceSnapshot" numeric(18,6) NULL;

                ALTER TABLE "ReturnItems"
                    ADD COLUMN IF NOT EXISTS "CostAmountSnapshot" numeric(18,2) NULL;

                CREATE SEQUENCE IF NOT EXISTS "return_number_seq"
                    START WITH 1
                    INCREMENT BY 1
                    NO MINVALUE
                    NO MAXVALUE
                    CACHE 1;
            END
            $$;
            """);
    }

    private static ApplicationDbContext CreateDbContext(
        string connectionString,
        TestTenantContext tenantContext,
        params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .EnableDetailedErrors();

        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        return new ApplicationDbContext(builder.Options, tenantContext);
    }

    private static async Task ConfigureSessionTimeoutsAsync(ApplicationDbContext context)
    {
        context.Database.SetCommandTimeout(TimeSpan.FromSeconds(30));
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("SET statement_timeout = '30s'; SET lock_timeout = '30s';");
    }

    private static string DescribeException(Exception exception)
    {
        var parts = new List<string>();
        Exception? current = exception;

        while (current != null)
        {
            parts.Add($"{current.GetType().Name}:{current.Message}");
            current = current.InnerException;
        }

        return string.Join(" -> ", parts);
    }

    private sealed record SeededScenario(
        Guid TenantId,
        Guid CustomerId,
        Guid WarehouseId,
        Guid ProductId,
        Guid VariantId,
        Guid UnitId);

    private sealed record SaleActor(
        string Name,
        string RequestId,
        string TraceIdentifier,
        Guid UserId);

    private sealed record SaleActorResult(
        SaleActor Actor,
        bool Succeeded,
        int? StatusCode,
        SaleResponse? Response,
        string? PayloadJson,
        TimeSpan Duration,
        Exception? Exception);

    private sealed record VariantCostMutationResult(
        SaleActor Actor,
        bool Succeeded,
        int? StatusCode,
        decimal? CostPrice,
        string? PayloadJson,
        TimeSpan Duration,
        Exception? Exception);

    private sealed record PriceCon12ReturnActorResult(
        SaleActor Actor,
        bool Succeeded,
        int? StatusCode,
        SalesReturnResponse? Response,
        string? PayloadJson,
        TimeSpan Duration,
        Exception? Exception);

    private sealed record CommerceHubReturnDeliveryActorResult(
        SaleActor Actor,
        bool Succeeded,
        int? StatusCode,
        CommerceHubReturnDetailsDto? Response,
        string? PayloadJson,
        TimeSpan Duration,
        Exception? Exception);

    private sealed record StockCon05CommerceHubGraph(
        Guid MarketplaceOrderId,
        Guid SubOrderId,
        Guid OrderItemId,
        Guid RefundRequestId,
        Guid ReturnRequestId,
        Guid CustomerId,
        Guid VendorId);

    private sealed record CostBasisRecalculationWorkerResult(
        string WorkerName,
        bool InsertedProcessedEvent,
        decimal WeightedAverageMovementCost,
        decimal WeightedMovementInventoryValue,
        TimeSpan Duration,
        Exception? Exception);

    private sealed record PriceCon12DerivedValuation(
        decimal StockQuantity,
        decimal WeightedAverageMovementCost,
        decimal WeightedMovementInventoryValue);

    private sealed record PriceCon12StateSnapshot(
        decimal SaleCostPriceSnapshot,
        decimal SaleCostAmountSnapshot,
        decimal ReturnCostPriceSnapshot,
        decimal ReturnCostAmountSnapshot,
        decimal CurrentVariantCostPrice,
        decimal StockQuantity,
        decimal WeightedAverageMovementCost,
        decimal WeightedMovementInventoryValue,
        decimal CurrentCostInventoryValue,
        int RecalculationProcessedEvents,
        string FinancialFingerprint);

    private sealed record PriceCon13BiSnapshot(
        decimal GrossSales,
        decimal GrossCommission,
        decimal PromoCost,
        decimal PromoReversalAmount,
        decimal RefundAmount,
        decimal RefundImpactOnRevenue,
        decimal NetRevenue,
        int OrdersCount,
        int RefundCount,
        int RefundedOrdersCount,
        DateTime? AggregatedThroughUtc,
        DateTime CalculatedAtUtc)
    {
        public string Fingerprint => string.Join(
            ";",
            FormatMoney(GrossSales),
            FormatMoney(GrossCommission),
            FormatMoney(PromoCost),
            FormatMoney(PromoReversalAmount),
            FormatMoney(RefundAmount),
            FormatMoney(RefundImpactOnRevenue),
            FormatMoney(NetRevenue),
            OrdersCount,
            RefundCount,
            RefundedOrdersCount,
            FormatInstant(AggregatedThroughUtc));

        private static string FormatMoney(decimal value) => value.ToString("0.0000");

        private static string FormatInstant(DateTime? value)
        {
            if (value is not { } instant)
                return "null";

            return new DateTime(instant.Ticks - instant.Ticks % 10, instant.Kind).ToString("O");
        }

        public static PriceCon13BiSnapshot FromAggregate(CommerceHubDailyFinancialAggregate aggregate) =>
            new(
                aggregate.GrossSales,
                aggregate.GrossCommission,
                aggregate.PromoCost,
                aggregate.PromoReversalAmount,
                aggregate.RefundAmount,
                aggregate.RefundImpactOnRevenue,
                aggregate.NetRevenue,
                aggregate.OrdersCount,
                aggregate.RefundCount,
                aggregate.RefundedOrdersCount,
                aggregate.AggregatedThroughUtc,
                aggregate.CalculatedAtUtc);
    }

    private sealed record PriceCon13StateSnapshot(
        PriceCon12StateSnapshot ImmutableFinancialState,
        PriceCon13BiSnapshot BiSnapshot,
        int ProjectionProcessedEvents,
        decimal ProfitabilityNetProfit,
        decimal MarginPercentage,
        string ReportingProjectionFingerprint);

    private sealed record PriceCon13ProjectionWorkerResult(
        string WorkerName,
        bool InsertedProcessedEvent,
        bool AppliedProjection,
        bool RejectedAsStale,
        string SnapshotFingerprint,
        TimeSpan Duration,
        Exception? Exception);

    private sealed record PriceCon11CommittedFinancialState(
        Guid InvoiceId,
        Guid InvoiceItemId,
        Guid AccountingOutboxId,
        string FinancialFingerprint);

    private sealed record DurableStateSnapshot(
        int SalesInvoices,
        decimal StockQuantity,
        int StockMovements,
        int AccountingOutboxMessages,
        int CustomerTransactions,
        int AccountLedgers,
        int JournalEntries,
        int IdempotencyRecords);

    private sealed record AccountingReplayWorker(
        string Name,
        Guid UserId);

    private sealed record AccountingOutboxWorkerResult(
        AccountingReplayWorker Worker,
        int ProcessedCount,
        TimeSpan Duration,
        Exception? Exception);

    private sealed record StockSyncWorkerResult(
        string WorkerName,
        int ProcessedCount,
        TimeSpan Duration,
        Exception? Exception);

    private sealed record StockCon03ProjectionVisibilitySnapshot(
        decimal StorefrontProductStockQuantity,
        bool StorefrontProductInStock,
        Guid? StorefrontProductLastStockEventId,
        DateTime? StorefrontProductLastStockUpdatedAtUtc,
        int StorefrontProductLastStockEventVersion,
        decimal StorefrontVariantStockQuantity,
        bool StorefrontVariantInStock,
        Guid? StorefrontVariantLastStockEventId,
        DateTime? StorefrontVariantLastStockUpdatedAtUtc,
        int StorefrontVariantLastStockEventVersion,
        decimal CommerceHubAvailableStock,
        bool CommerceHubInStock,
        Guid? CommerceHubLastEventId,
        DateTime? CommerceHubStockUpdatedAtUtc,
        int CommerceHubLastStockEventVersion,
        bool AffiliateVisible,
        int? AffiliateTotalAvailableStock,
        bool AffiliateInStock)
    {
        public string AuthoritativeFingerprint =>
            string.Join(
                "|",
                StorefrontProductStockQuantity,
                StorefrontProductInStock,
                StorefrontProductLastStockEventId,
                StorefrontProductLastStockUpdatedAtUtc?.ToString("O"),
                StorefrontProductLastStockEventVersion,
                StorefrontVariantStockQuantity,
                StorefrontVariantInStock,
                StorefrontVariantLastStockEventId,
                StorefrontVariantLastStockUpdatedAtUtc?.ToString("O"),
                StorefrontVariantLastStockEventVersion,
                CommerceHubAvailableStock,
                CommerceHubInStock,
                CommerceHubLastEventId,
                CommerceHubStockUpdatedAtUtc?.ToString("O"),
                CommerceHubLastStockEventVersion,
                AffiliateVisible,
                AffiliateTotalAvailableStock,
                AffiliateInStock);
    }

    private sealed record StockCon04StateSnapshot(
        int SalesInvoiceCount,
        decimal StockQuantity,
        int SaleStockMovementCount,
        decimal SaleStockMovementQuantityChange,
        decimal SaleStockMovementQuantityAfter,
        int SalesReturnCount,
        decimal ReturnTotalAmount,
        decimal TotalReturnedBaseQuantity,
        decimal ReturnCostPriceSnapshot,
        decimal ReturnCostAmountSnapshot,
        int SalesReturnStockMovementCount,
        decimal ReturnStockMovementQuantityChange,
        decimal ReturnStockMovementQuantityAfter,
        decimal ReturnStockMovementPrice,
        int SalesReturnCustomerTransactionCount,
        int TotalCustomerTransactions,
        decimal CustomerBalance,
        int SalesReturnLedgerEntryCount,
        int TotalCustomerLedgerEntries,
        int RefundOutboxCount,
        int RefundOutboxAggregateVersion,
        int RefundOutboxPayloadMutationVersion,
        Guid? RefundOutboxPayloadOriginalInvoiceId,
        DateTime? RefundOutboxProcessedAtUtc,
        Guid? RefundOutboxJournalId,
        int RefundJournalCount,
        int RefundCogsJournalCount,
        int StockSyncOutboxCount,
        int SalesReturnIdempotencyRecordCount,
        string? SalesReturnIdempotencyKey,
        int? SalesReturnIdempotencyResponseStatusCode,
        DateTime? SalesReturnIdempotencyCompletedAtUtc,
        string? SalesReturnIdempotencyResponseJson,
        int InvoiceMutationVersion,
        decimal RemainingReturnableQuantity,
        int InventoryEventInboxCount,
        int ProcessedEventCount);

    private sealed record StockCon05StateSnapshot(
        decimal StockQuantity,
        decimal StockReservedQuantity,
        decimal StockAvailableQuantity,
        int ReservationCount,
        decimal ReservationCommittedQuantity,
        decimal ReservationReturnedQuantity,
        decimal ReservationDamagedReturnedQuantity,
        InventoryReservationStatus? ReservationStatus,
        int ReturnRequestCount,
        int RefundRequestCount,
        CommerceHubReturnStatus ReturnStatus,
        CommerceHubRefundRequestStatus RefundStatus,
        int ReceivedResellableQuantity,
        int ReceivedDamagedQuantity,
        Guid ReturnRefundId,
        Guid? RefundReturnRequestId,
        int ReservationMovementCount,
        int DeliveryMovementCount,
        int ReturnMovementCount,
        decimal ReturnMovementQuantityChange,
        decimal ReturnMovementQuantityAfter,
        decimal ReturnMovementAvailableAfter,
        Guid? ReturnMovementReferenceId,
        Guid? ReturnMovementReferenceLineId,
        string? ReturnMovementReferenceType,
        Guid? ReturnMovementInventoryEventId,
        Guid ReturnInventoryEventId,
        int StockSyncOutboxCount,
        int InventoryEventInboxCount,
        int AppliedInventoryEventInboxCount,
        int ReturnInventoryEventInboxCount,
        InventoryEventInboxStatus? ReturnInventoryEventStatus,
        int ProcessedEventCount,
        int ReturnConfirmedAuditLogCount,
        int RefundAuditLogCount,
        int CommerceHubRefundCount,
        int CommerceHubRefundExecutionCount);

    private sealed class StockCon01MongoDatabaseScope : IAsyncDisposable
    {
        private readonly MongoClient _client;
        private readonly string _databaseName;

        public StockCon01MongoDatabaseScope(
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
            await _client.DropDatabaseAsync(_databaseName);
        }
    }

    private sealed record SiCon03StateSnapshot(
        int SalesInvoices,
        decimal StockQuantity,
        int StockMovements,
        int CustomerTransactions,
        int AccountLedgers,
        decimal CustomerBalance,
        int AccountingOutboxMessages,
        Guid AccountingOutboxId,
        Guid? AccountingOutboxJournalId,
        DateTime? AccountingOutboxClaimedUntilUtc,
        DateTime? AccountingOutboxProcessedAtUtc,
        int StockSyncOutboxMessages,
        Guid StockSyncOutboxId,
        Guid StockSyncEventId,
        DateTime? StockSyncClaimedUntilUtc,
        DateTime? StockSyncPublishedAtUtc,
        int StockSyncAttemptCount,
        int ProcessedEvents,
        int InventoryEventInbox,
        int JournalEntries,
        string JournalFingerprint);

    private sealed class ActiveProcessingDuplicateWorkerWindow
    {
        private readonly TimeSpan _timeout;
        private readonly ConcurrentQueue<string> _progressEntries;
        private readonly TaskCompletionSource<bool> _originalPaused =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _duplicateAttempted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseOriginal =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _originalPausedBeforeDuplicateAttempt;

        public ActiveProcessingDuplicateWorkerWindow(
            TimeSpan timeout,
            ConcurrentQueue<string> progressEntries)
        {
            _timeout = timeout;
            _progressEntries = progressEntries;
        }

        public bool OriginalWasPausedBeforeDuplicateAttempt =>
            Volatile.Read(ref _originalPausedBeforeDuplicateAttempt) == 1;

        public void MarkOriginalPaused(string workerName)
        {
            _progressEntries.Enqueue($"{workerName}:active-processing-paused-before-commit");
            _originalPaused.TrySetResult(true);
        }

        public void MarkDuplicateAttempt(string workerName)
        {
            if (_originalPaused.Task.IsCompletedSuccessfully)
            {
                Interlocked.Exchange(ref _originalPausedBeforeDuplicateAttempt, 1);
            }

            _progressEntries.Enqueue($"{workerName}:duplicate-attempt-entered-while-original-active");
            _duplicateAttempted.TrySetResult(true);
        }

        public async Task WaitForOriginalPauseAsync()
        {
            try
            {
                await _originalPaused.Task.WaitAsync(_timeout);
            }
            catch (TimeoutException)
            {
                throw new XunitException(
                    $"Timed out waiting for the original worker to pause before commit. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public async Task WaitForDuplicateAttemptAsync()
        {
            try
            {
                await _duplicateAttempted.Task.WaitAsync(_timeout);
            }
            catch (TimeoutException)
            {
                throw new XunitException(
                    $"Timed out waiting for the duplicate worker to attempt processing. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public async Task WaitUntilReleasedAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _releaseOriginal.Task.WaitAsync(_timeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                throw new XunitException(
                    $"Timed out waiting to release the original worker. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public void ReleaseOriginalWorker()
        {
            _progressEntries.Enqueue("orchestrator:release-original-worker");
            _releaseOriginal.TrySetResult(true);
        }
    }

    private sealed class BlockingPublishWindow
    {
        private readonly TimeSpan _timeout;
        private readonly ConcurrentQueue<string> _progressEntries;
        private readonly TaskCompletionSource<bool> _firstPublishEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseFirstPublish =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _publishCount;

        public BlockingPublishWindow(
            TimeSpan timeout,
            ConcurrentQueue<string> progressEntries)
        {
            _timeout = timeout;
            _progressEntries = progressEntries;
        }

        public int PublishCount => Volatile.Read(ref _publishCount);

        public async Task OnPublishAsync(object message, CancellationToken cancellationToken)
        {
            var publishIndex = Interlocked.Increment(ref _publishCount);
            _progressEntries.Enqueue($"{message.GetType().Name}:publish-entered:{publishIndex}");

            if (publishIndex != 1)
            {
                return;
            }

            _firstPublishEntered.TrySetResult(true);

            try
            {
                await _releaseFirstPublish.Task.WaitAsync(_timeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                throw new XunitException(
                    $"Timed out waiting to release the first stock-sync publish. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public async Task WaitForFirstPublishAsync()
        {
            try
            {
                await _firstPublishEntered.Task.WaitAsync(_timeout);
            }
            catch (TimeoutException)
            {
                throw new XunitException(
                    $"Timed out waiting for the first stock-sync publish attempt. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public void ReleaseFirstPublish()
        {
            _progressEntries.Enqueue("orchestrator:release-first-stock-sync-publish");
            _releaseFirstPublish.TrySetResult(true);
        }
    }

    private sealed class StockLockRendezvous
    {
        private readonly int _participantCount;
        private readonly TimeSpan _timeout;
        private readonly ConcurrentQueue<string> _progressEntries;
        private readonly TaskCompletionSource<bool> _allArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivalCount;

        public StockLockRendezvous(
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
            _progressEntries.Enqueue($"{actorName}:stock-lock-arrived:{arrival}");

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
                    $"Timed out waiting for both SI-CON-01 actors to reach the contested stock lock. " +
                    $"Progress: {string.Join(" | ", _progressEntries)}");
            }

            _progressEntries.Enqueue($"{actorName}:stock-lock-released");
        }
    }

    private sealed class ProductVariantStockLockInterceptor : DbCommandInterceptor
    {
        private readonly string _actorName;
        private readonly StockLockRendezvous _stockLockRendezvous;
        private readonly ConcurrentQueue<string> _progressEntries;
        private int _intercepted;

        public ProductVariantStockLockInterceptor(
            string actorName,
            StockLockRendezvous stockLockRendezvous,
            ConcurrentQueue<string> progressEntries)
        {
            _actorName = actorName;
            _stockLockRendezvous = stockLockRendezvous;
            _progressEntries = progressEntries;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (IsContestedStockLockQuery(command.CommandText) &&
                Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0)
            {
                _progressEntries.Enqueue($"{_actorName}:stock-lock-query-detected");
                await _stockLockRendezvous.WaitAsync(_actorName, cancellationToken);
            }

            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private static bool IsContestedStockLockQuery(string commandText)
        {
            return commandText.Contains("ProductVariantStocks", StringComparison.OrdinalIgnoreCase) &&
                   commandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class ActiveTransactionReplayWindow
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

        public ActiveTransactionReplayWindow(
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

            _progressEntries.Enqueue($"{actorName}:same-key-claim-attempted-while-original-active");
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
                    $"Timed out waiting for Request A to pause before commit. Progress: {string.Join(" | ", _progressEntries)}");
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
                    $"Timed out waiting for Request B to attempt the overlapping idempotency claim. " +
                    $"Progress: {string.Join(" | ", _progressEntries)}");
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
                    $"Timed out waiting to release Request A commit. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public void ReleaseOriginalCommit()
        {
            _progressEntries.Enqueue("orchestrator:release-original-commit");
            _releaseOriginalCommit.TrySetResult(true);
        }
    }

    private sealed class CommitPauseTransactionInterceptor(
        string actorName,
        ActiveTransactionReplayWindow replayWindow) : DbTransactionInterceptor
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
                // SI-CON-02 critical window: all Request A mutations have been staged
                // and saved in its transaction, but PostgreSQL has not committed them.
                replayWindow.MarkOriginalCommitPaused(actorName);
                await replayWindow.WaitUntilCommitReleasedAsync(cancellationToken);
            }

            return await base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }

    private sealed class ReplayClaimAttemptInterceptor(
        string actorName,
        ActiveTransactionReplayWindow replayWindow) : DbCommandInterceptor
    {
        private int _intercepted;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (IsSalesIdempotencyClaimInsert(command.CommandText) &&
                Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0)
            {
                // Request B is about to submit the same idempotency claim while
                // Request A's uncommitted claim is still the PostgreSQL authority fence.
                replayWindow.MarkReplayClaimAttempted(actorName);
            }

            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private static bool IsSalesIdempotencyClaimInsert(string commandText)
        {
            return commandText.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase) &&
                   commandText.Contains("SalesRequestIdempotency", StringComparison.OrdinalIgnoreCase) &&
                   commandText.Contains("ON CONFLICT", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class ConcurrentSalesReturnInvoiceLockAttemptInterceptor(
        string actorName,
        ActiveTransactionReplayWindow replayWindow) : DbCommandInterceptor
    {
        private int _intercepted;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (IsOriginalInvoiceForUpdateQuery(command.CommandText) &&
                Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0)
            {
                // STOCK-CON-07 critical window: Request B owns a different request
                // identity and is about to block on the original invoice row while
                // Request A's same sold-quantity restoration is still uncommitted.
                replayWindow.MarkReplayClaimAttempted(actorName);
            }

            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private static bool IsOriginalInvoiceForUpdateQuery(string commandText)
        {
            return commandText.Contains("FROM \"SalesInvoices\"", StringComparison.OrdinalIgnoreCase) &&
                   commandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class CommerceHubReturnLockAttemptInterceptor(
        string actorName,
        ActiveTransactionReplayWindow replayWindow) : DbCommandInterceptor
    {
        private int _intercepted;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (IsCommerceHubReturnLockQuery(command.CommandText) &&
                Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0)
            {
                replayWindow.MarkReplayClaimAttempted(actorName);
            }

            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private static bool IsCommerceHubReturnLockQuery(string commandText)
        {
            return commandText.Contains("CommerceHubReturnRequests", StringComparison.OrdinalIgnoreCase) &&
                   commandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class PriceCon12ProcessedEventInsertAttemptInterceptor(
        string workerName,
        ActiveTransactionReplayWindow replayWindow) : DbCommandInterceptor
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
                replayWindow.MarkReplayClaimAttempted(workerName);
            }

            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private static bool IsProcessedEventInsert(string commandText)
        {
            return commandText.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase) &&
                   commandText.Contains("ProcessedEvents", StringComparison.OrdinalIgnoreCase) &&
                   commandText.Contains("ON CONFLICT", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class SalePreCommitStockProjectionPauseInterceptor(
        string actorName,
        ActiveTransactionReplayWindow replayWindow) : DbCommandInterceptor
    {
        private int _intercepted;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (IsVariantStockProjectionLockQuery(command.CommandText) &&
                Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0)
            {
                // PRICE-CON-11 critical window: sale COGS/accounting intent is staged
                // inside Request A's transaction, but none of those rows are committed.
                // Pause before the stock projection variant lock so Request B can commit
                // the future cost mutation while Request A remains uncommitted.
                replayWindow.MarkOriginalCommitPaused(actorName);
                await replayWindow.WaitUntilCommitReleasedAsync(cancellationToken);
            }

            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private static bool IsVariantStockProjectionLockQuery(string commandText)
        {
            return commandText.Contains("FROM \"ProductVariants\"", StringComparison.OrdinalIgnoreCase) &&
                   commandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class AccountingOutboxInvoiceLockPauseInterceptor(
        string workerName,
        ActiveProcessingDuplicateWorkerWindow window) : DbCommandInterceptor
    {
        private int _intercepted;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (IsSalesInvoiceForUpdateQuery(command.CommandText) &&
                Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0)
            {
                // The outbox worker has already claimed the authoritative row. Pausing after
                // PostgreSQL executes the invoice FOR UPDATE keeps Worker A active and
                // uncommitted while Worker B attempts the same logical delivery and must
                // respect the claimed-row duplicate fence.
                window.MarkOriginalPaused(workerName);
                await window.WaitUntilReleasedAsync(cancellationToken);
            }

            return await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }

        private static bool IsSalesInvoiceForUpdateQuery(string commandText)
        {
            return commandText.Contains("FROM \"SalesInvoices\"", StringComparison.OrdinalIgnoreCase) &&
                   commandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class FailedCommandProgressInterceptor(
        string actorName,
        ConcurrentQueue<string> progressEntries) : DbCommandInterceptor
    {
        public override Task CommandFailedAsync(
            DbCommand command,
            CommandErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            progressEntries.Enqueue(
                $"{actorName}:failed-sql:{NormalizeSqlForProgress(command.CommandText)}");

            return base.CommandFailedAsync(command, eventData, cancellationToken);
        }

        private static string NormalizeSqlForProgress(string commandText)
        {
            var normalized = string.Join(
                ' ',
                commandText
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            return normalized.Length <= 900
                ? normalized
                : normalized[..900];
        }
    }

    private sealed class FixedCorrelationIdAccessor(string correlationId) : ICorrelationIdAccessor
    {
        public string GetCorrelationId()
        {
            // Some historical reliability-test schemas still have AccountingOutboxMessages.CorrelationId
            // as uuid while the current domain model treats it as a string. The SI-CON scenarios do not
            // assert trace correlation, so omit it to keep the test focused on durable business effects.
            _ = correlationId;
            return string.Empty;
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

    private sealed class NoOpAuditService : IAuditService
    {
        public Task LogCreate(string entityType, string entityId, string description, object? afterData, string? reference = null) => Task.CompletedTask;
        public Task LogUpdate(string entityType, string entityId, string description, object? beforeData, object? afterData, string? reference = null) => Task.CompletedTask;
        public Task LogDelete(string entityType, string entityId, string description, object? beforeData, string? reference = null) => Task.CompletedTask;
        public Task LogInactivate(string entityType, string entityId, string description, string? reference = null) => Task.CompletedTask;
        public Task LogPost(string entityType, string entityId, string description, string? reference = null) => Task.CompletedTask;
        public Task LogUnpost(string entityType, string entityId, string description, string? reference = null) => Task.CompletedTask;
        public Task LogError(string description, string? reference = null) => Task.CompletedTask;
        public Task LogLogin(string? userId, bool successFlag, string description) => Task.CompletedTask;
        public Task LogStockMovement(string productId, decimal qty, Guid warehouseId, string reference, string description) => Task.CompletedTask;
        public Task LogAction(string actionType, string entityType, string entityId, string description, object? beforeData, object? afterData, string? reference, bool success, string? source = "Web") => Task.CompletedTask;
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
}
