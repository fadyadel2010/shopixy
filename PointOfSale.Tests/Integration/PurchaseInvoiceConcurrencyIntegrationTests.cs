using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
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
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Audit;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Eventing;
using PointOfSale.Infrastructure.Treasury;
using PointOfSale.Tests.Infrastructure;
using Xunit.Sdk;
using PaymentMethodEnum = PointOfSale.Core.Enums.PaymentMethod;
using UnitEntity = PointOfSale.Core.Entities.Unit;

namespace PointOfSale.Tests.Integration;

public sealed class PurchaseInvoiceConcurrencyIntegrationTests
{
    private static readonly DateTime SeededUtc = new(2026, 5, 13, 12, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions CaseInsensitiveJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Guid TenantId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4b001");
    private static readonly Guid CategoryId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4b002");
    private static readonly Guid SupplierId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4b003");
    private static readonly Guid ProductId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4b004");
    private static readonly Guid VariantId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4b005");
    private static readonly Guid UnitId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4b006");
    private static readonly Guid WarehouseId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4b007");
    private static readonly Guid ProductUnitId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4b008");
    private static readonly Guid VariantStockId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4b009");
    private static readonly Guid ActorAUserId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4b00a");
    private static readonly Guid ActorBUserId = Guid.Parse("970f08b8-3e8a-4d42-91f4-8435d6d4b00b");

    private const decimal InitialStockQuantity = 0m;
    private const decimal ActorAQuantity = 5m;
    private const decimal ActorBQuantity = 7m;
    private const decimal AuthoritativeUnitCost = 20m;
    private const decimal ActorATotalAmount = ActorAQuantity * AuthoritativeUnitCost;
    private const decimal ActorBTotalAmount = ActorBQuantity * AuthoritativeUnitCost;
    private const decimal AggregateCommittedQuantity = ActorAQuantity + ActorBQuantity;
    private const decimal AggregateCommittedAmount = ActorATotalAmount + ActorBTotalAmount;
    private const int ScenarioRepetitions = 5;

    [Fact]
    public async Task PI_CON_01_Concurrent_Purchase_Creation_Against_Same_Variant_Converges_Safely_And_Is_Replay_Safe()
    {
        for (var iteration = 0; iteration < ScenarioRepetitions; iteration++)
        {
            await ExecuteScenarioIterationAsync(iteration);
        }
    }

    [Fact]
    public async Task PI_CON_02_Replay_During_Active_Purchase_Transaction_Converges_Safely()
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
        var replayWindow = new PurchaseActiveTransactionReplayWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var sharedRequestId = "pi-con-02-active-replay";
        var sharedPurchaseAttemptId = "pi-con-02-active-replay-attempt";
        var originalActor = new PurchaseActor(
            Name: "request-a-original",
            RequestId: sharedRequestId,
            PurchaseAttemptId: sharedPurchaseAttemptId,
            TraceIdentifier: "pi-con-02-original-active",
            UserId: ActorAUserId,
            Quantity: ActorAQuantity);
        var replayActor = new PurchaseActor(
            Name: "request-b-replay",
            RequestId: sharedRequestId,
            PurchaseAttemptId: sharedPurchaseAttemptId,
            TraceIdentifier: "pi-con-02-overlapping-replay",
            UserId: ActorBUserId,
            Quantity: ActorAQuantity);

        var originalTask = ExecutePurchaseActorAsync(
            connectionString,
            tenantContext,
            seeded,
            originalActor,
            progressEntries,
            new PurchaseActiveReplayCommitPauseTransactionInterceptor(originalActor.Name, replayWindow));

        await replayWindow.WaitForOriginalCommitPauseAsync();

        // Request A has already staged purchase invoice, stock, supplier payable, journal intent,
        // and idempotency completion inside its transaction. A fresh DbContext must still observe
        // none of those durable effects until PostgreSQL commits the authoritative transaction.
        await AssertNoUncommittedDurableStateVisibleAsync(
            scope,
            tenantContext,
            seeded,
            progressEntries);

        var replayTask = ExecutePurchaseActorAsync(
            connectionString,
            tenantContext,
            seeded,
            replayActor,
            progressEntries,
            new PurchaseReplayClaimAttemptInterceptor(replayActor.Name, replayWindow));

        await replayWindow.WaitForReplayClaimAttemptAsync();
        Assert.True(
            replayWindow.OriginalWasPausedBeforeReplayClaim,
            $"Replay did not overlap the active purchase transaction window. Progress: {string.Join(" | ", progressEntries)}");

        replayWindow.ReleaseOriginalCommit();

        PurchaseActorResult[] results;
        try
        {
            results = await Task.WhenAll(originalTask, replayTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"PI-CON-02 timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.True(
            replayWindow.OriginalCommitPaused,
            $"Request A never paused before commit. Progress: {string.Join(" | ", progressEntries)}");
        Assert.True(
            replayWindow.ReplayClaimAttempted,
            $"Request B never attempted the overlapping purchase idempotency claim. Progress: {string.Join(" | ", progressEntries)}");
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
    public async Task PI_CON_03_Duplicate_Purchase_Worker_Delivery_During_Active_Processing_Converges_Safely()
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

        var canonicalActor = new PurchaseActor(
            Name: "pi-con-03-authoritative-create",
            RequestId: "pi-con-03-authoritative-create",
            PurchaseAttemptId: "pi-con-03-authoritative-create-attempt",
            TraceIdentifier: "pi-con-03-authoritative-create",
            UserId: ActorAUserId,
            Quantity: ActorAQuantity);

        var createResult = await ExecutePurchaseActorAsync(
            connectionString,
            tenantContext,
            seeded,
            canonicalActor,
            progressEntries);

        Assert.Null(createResult.Exception);
        Assert.True(createResult.Succeeded, createResult.PayloadJson ?? "Canonical PI-CON-03 purchase did not succeed.");

        var invoiceId = createResult.Response!.InvoiceId;
        var canonicalBaseline = await LoadPiCon03StateSnapshotAsync(scope, tenantContext, seeded, invoiceId);

        Assert.Equal(1, canonicalBaseline.PurchaseInvoices);
        Assert.Equal(ActorAQuantity, canonicalBaseline.StockQuantity);
        Assert.Equal(1, canonicalBaseline.StockMovements);
        Assert.Equal(1, canonicalBaseline.SupplierTransactions);
        Assert.Equal(1, canonicalBaseline.SupplierLedgers);
        Assert.Equal(-ActorATotalAmount, canonicalBaseline.SupplierBalance);
        Assert.Equal(1, canonicalBaseline.AccountingOutboxMessages);
        Assert.NotEqual(Guid.Empty, canonicalBaseline.AccountingOutboxId);
        Assert.NotNull(canonicalBaseline.AccountingOutboxJournalId);
        Assert.Null(canonicalBaseline.AccountingOutboxClaimedUntilUtc);
        Assert.NotNull(canonicalBaseline.AccountingOutboxProcessedAtUtc);
        Assert.Equal(1, canonicalBaseline.StockSyncOutboxMessages);
        Assert.NotEqual(Guid.Empty, canonicalBaseline.StockSyncOutboxId);
        Assert.NotEqual(Guid.Empty, canonicalBaseline.StockSyncEventId);
        Assert.True(canonicalBaseline.StockSyncEventVersion > 0);
        Assert.Null(canonicalBaseline.StockSyncPublishedAtUtc);
        Assert.Equal(0, canonicalBaseline.ProcessedEvents);
        Assert.Equal(0, canonicalBaseline.InventoryEventInbox);
        Assert.Equal(1, canonicalBaseline.JournalEntries);
        Assert.Equal(InvoiceStatus.Completed, canonicalBaseline.InvoiceStatus);
        Assert.Equal(ActorATotalAmount, canonicalBaseline.InvoiceTotalAmount);
        Assert.Equal(ActorATotalAmount, canonicalBaseline.InvoiceRemainingAmount);
        Assert.Equal(0m, canonicalBaseline.InvoicePaidAmount);
        Assert.Equal(0, canonicalBaseline.InvoiceMutationVersion);

        // Reopen the same persisted purchase accounting outbox row so the real worker path
        // replays the original payload, event id, mutation version, and idempotency key.
        await ReopenPurchaseAccountingOutboxMessageForWorkerReplayAsync(
            scope,
            tenantContext,
            seeded,
            invoiceId,
            progressEntries);

        var accountingWindow = new ActiveProcessingDuplicateWorkerWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var accountingWorkerA = new AccountingReplayWorker(
            Name: "purchase-accounting-worker-a",
            UserId: ActorAUserId);
        var accountingWorkerB = new AccountingReplayWorker(
            Name: "purchase-accounting-worker-b",
            UserId: ActorBUserId);

        using var accountingWorkerAServices = BuildAccountingOutboxProcessorServiceProvider(
            connectionString,
            tenantContext,
            new PurchaseAccountingOutboxInvoiceLockPauseInterceptor(accountingWorkerA.Name, accountingWindow));
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
        await AssertPurchaseAccountingWorkerClaimVisibleDuringActiveProcessingAsync(
            scope,
            tenantContext,
            seeded,
            invoiceId,
            canonicalBaseline,
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
                $"PI-CON-03 accounting duplicate worker did not complete while the original worker held the invoice lock. Progress: {string.Join(" | ", progressEntries)}");
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
                $"PI-CON-03 accounting overlap timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        await AssertPurchaseAccountingDuplicateWorkerConvergenceAsync(
            scope,
            tenantContext,
            seeded,
            invoiceId,
            canonicalBaseline,
            originalAccountingWorkerResult,
            duplicateAccountingWorkerResult,
            progressEntries);

        var postAccountingBaseline = await LoadPiCon03StateSnapshotAsync(scope, tenantContext, seeded, invoiceId);

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
            "purchase-stock-sync-worker-a",
            progressEntries);

        await stockSyncPublishWindow.WaitForFirstPublishAsync();
        await AssertPurchaseStockSyncClaimVisibleDuringActivePublishAsync(
            scope,
            tenantContext,
            seeded,
            invoiceId,
            postAccountingBaseline,
            progressEntries);

        var stockSyncWorkerBTask = ExecuteStockSyncWorkerAsync(
            stockSyncProcessor,
            "purchase-stock-sync-worker-b",
            progressEntries);

        StockSyncWorkerResult duplicateStockSyncWorkerResult;
        try
        {
            duplicateStockSyncWorkerResult = await stockSyncWorkerBTask.WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"PI-CON-03 stock-sync duplicate worker did not complete while the original publish was paused. Progress: {string.Join(" | ", progressEntries)}");
        }

        stockSyncPublishWindow.ReleaseFirstPublish();

        StockSyncWorkerResult originalStockSyncWorkerResult;
        try
        {
            originalStockSyncWorkerResult = await stockSyncWorkerATask.WaitAsync(TimeSpan.FromSeconds(60));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"PI-CON-03 stock-sync original worker did not finish after publish release. Progress: {string.Join(" | ", progressEntries)}");
        }

        await AssertPurchaseStockSyncDuplicateWorkerConvergenceAsync(
            scope,
            tenantContext,
            seeded,
            invoiceId,
            postAccountingBaseline,
            originalStockSyncWorkerResult,
            duplicateStockSyncWorkerResult,
            stockSyncPublishWindow,
            progressEntries);
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
        var stockLockRendezvous = new PurchaseStockLockRendezvous(
            participantCount: 2,
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var commitWindow = new PurchaseCommitOverlapWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);

        var actorA = new PurchaseActor(
            Name: "actor-a",
            RequestId: "pi-con-01-actor-a",
            PurchaseAttemptId: "pi-con-01-attempt-a",
            TraceIdentifier: $"pi-con-01-a-{iteration:D2}",
            UserId: ActorAUserId,
            Quantity: ActorAQuantity);
        var actorB = new PurchaseActor(
            Name: "actor-b",
            RequestId: "pi-con-01-actor-b",
            PurchaseAttemptId: "pi-con-01-attempt-b",
            TraceIdentifier: $"pi-con-01-b-{iteration:D2}",
            UserId: ActorBUserId,
            Quantity: ActorBQuantity);

        Task<PurchaseActorResult> actorATask;
        Task<PurchaseActorResult> actorBTask;

        // Alternate launch order across repetitions so the eventual invoice order is not
        // coupled to a single task-scheduling pattern. The authoritative stock-row barrier
        // still forces both requests to contend at ProductVariantStocks FOR UPDATE.
        if (iteration % 2 == 0)
        {
            actorATask = ExecutePurchaseActorAsync(
                connectionString,
                tenantContext,
                seeded,
                actorA,
                progressEntries,
                new ProductVariantStockLockInterceptor(actorA.Name, stockLockRendezvous, progressEntries),
                new PurchaseCommitPauseTransactionInterceptor(actorA.Name, commitWindow));
            actorBTask = ExecutePurchaseActorAsync(
                connectionString,
                tenantContext,
                seeded,
                actorB,
                progressEntries,
                new ProductVariantStockLockInterceptor(actorB.Name, stockLockRendezvous, progressEntries),
                new PurchaseCommitPauseTransactionInterceptor(actorB.Name, commitWindow));
        }
        else
        {
            actorBTask = ExecutePurchaseActorAsync(
                connectionString,
                tenantContext,
                seeded,
                actorB,
                progressEntries,
                new ProductVariantStockLockInterceptor(actorB.Name, stockLockRendezvous, progressEntries),
                new PurchaseCommitPauseTransactionInterceptor(actorB.Name, commitWindow));
            actorATask = ExecutePurchaseActorAsync(
                connectionString,
                tenantContext,
                seeded,
                actorA,
                progressEntries,
                new ProductVariantStockLockInterceptor(actorA.Name, stockLockRendezvous, progressEntries),
                new PurchaseCommitPauseTransactionInterceptor(actorA.Name, commitWindow));
        }

        await commitWindow.WaitForOriginalCommitPauseAsync();

        Assert.Equal(
            2,
            stockLockRendezvous.ArrivalCount);

        await AssertNoUncommittedDurableStateVisibleAsync(
            scope,
            tenantContext,
            seeded,
            progressEntries);

        commitWindow.ReleaseOriginalCommit();

        PurchaseActorResult[] results;
        try
        {
            results = await Task.WhenAll(actorATask, actorBTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"PI-CON-01 iteration {iteration} timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.True(
            commitWindow.OriginalCommitPaused,
            $"The authoritative purchase transaction never paused before commit. Progress: {string.Join(" | ", progressEntries)}");
        Assert.All(results, result => Assert.Null(result.Exception));

        var successfulResults = results
            .Where(result => result.Succeeded && result.Response is not null)
            .ToList();
        Assert.InRange(successfulResults.Count, 1, 2);

        await AssertAuthoritativeConvergenceAsync(
            scope,
            tenantContext,
            seeded,
            actorA,
            actorB,
            results,
            progressEntries);

        await AssertReplaySafetyAsync(
            connectionString,
            tenantContext,
            seeded,
            successfulResults[0],
            progressEntries);
    }

    private static async Task<PurchaseActorResult> ExecutePurchaseActorAsync(
        string connectionString,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        PurchaseActor actor,
        ConcurrentQueue<string> progressEntries,
        params IInterceptor[] interceptors)
    {
        await using var context = CreateDbContext(connectionString, tenantContext, interceptors);
        await ConfigureSessionTimeoutsAsync(context);
        var controllerLogger = new RecordingLogger<PurchasesController>();
        var controller = CreateController(context, tenantContext, actor, controllerLogger);
        var request = BuildCreatePurchaseRequest(seeded, actor);

        progressEntries.Enqueue($"{actor.Name}:request-ready");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            progressEntries.Enqueue($"{actor.Name}:invoking-controller");
            var result = await controller.CreatePurchase(request);
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

            return new PurchaseActorResult(
                Actor: actor,
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

    private static PurchasesController CreateController(
        ApplicationDbContext context,
        TestTenantContext tenantContext,
        PurchaseActor actor,
        ILogger<PurchasesController> logger)
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
        var purchaseFinancialIntegrity = new PurchaseFinancialIntegrityService(
            context,
            NullLogger<PurchaseFinancialIntegrityService>.Instance);

        var controller = new PurchasesController(
            context,
            tenantContext,
            logger,
            ledgerService,
            ledgerService,
            Mock.Of<IValidationService>(),
            auditService,
            new NoOpEventPublisher(),
            accountingOutbox,
            commerceAccounting,
            postingService,
            accountResolver,
            Mock.Of<ITreasuryReadService>(),
            cashboxLedgerAccountService,
            purchaseFinancialIntegrity)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext(actor)
            }
        };

        controller.Request.Headers["Idempotency-Key"] = actor.RequestId;
        controller.Request.Headers["X-Request-Id"] = actor.RequestId;
        controller.Request.Headers["X-Payment-Attempt-Id"] = actor.PurchaseAttemptId;
        controller.Request.Headers["User-Agent"] = "PI-CON-01";

        return controller;
    }

    private static DefaultHttpContext BuildHttpContext(PurchaseActor actor)
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

    private static CreatePurchaseRequest BuildCreatePurchaseRequest(
        SeededScenario seeded,
        PurchaseActor actor)
    {
        return new CreatePurchaseRequest
        {
            SupplierId = seeded.SupplierId,
            WarehouseId = seeded.WarehouseId,
            InvoiceDate = SeededUtc,
            PaymentMethod = PaymentMethodEnum.Credit,
            PaidAmount = 0m,
            Notes = $"PI-CON-01 {actor.Name}",
            Items =
            [
                new PurchaseItemRequest
                {
                    ProductId = seeded.ProductId,
                    ProductVariantId = seeded.VariantId,
                    UnitId = seeded.UnitId,
                    Quantity = actor.Quantity,
                    UnitCost = AuthoritativeUnitCost,
                    TaxRate = 0m
                }
            ]
        };
    }

    private static PurchaseActorResult ParseActorResult(
        PurchaseActor actor,
        ActionResult<PurchaseResponse> actionResult,
        TimeSpan duration,
        ConcurrentQueue<string> progressEntries)
    {
        if (actionResult.Result is OkObjectResult { Value: PurchaseResponse response })
        {
            progressEntries.Enqueue($"{actor.Name}:completed:{response.InvoiceId}");
            return new PurchaseActorResult(
                Actor: actor,
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
                TryParseStoredPurchaseResponse(payloadJson, out var storedResponse))
            {
                progressEntries.Enqueue($"{actor.Name}:completed-stored:{storedResponse.InvoiceId}");
                return new PurchaseActorResult(
                    Actor: actor,
                    Succeeded: true,
                    StatusCode: StatusCodes.Status200OK,
                    Response: storedResponse,
                    PayloadJson: payloadJson,
                    Duration: duration,
                    Exception: null);
            }

            progressEntries.Enqueue(
                $"{actor.Name}:status:{objectResult.StatusCode ?? StatusCodes.Status500InternalServerError}:{payloadJson}");
            return new PurchaseActorResult(
                Actor: actor,
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
            return new PurchaseActorResult(
                Actor: actor,
                Succeeded: false,
                StatusCode: statusCodeResult.StatusCode,
                Response: null,
                PayloadJson: null,
                Duration: duration,
                Exception: null);
        }

        if (actionResult.Value is PurchaseResponse directResponse)
        {
            progressEntries.Enqueue($"{actor.Name}:completed-direct:{directResponse.InvoiceId}");
            return new PurchaseActorResult(
                Actor: actor,
                Succeeded: true,
                StatusCode: StatusCodes.Status200OK,
                Response: directResponse,
                PayloadJson: JsonSerializer.Serialize(directResponse),
                Duration: duration,
                Exception: null);
        }

        progressEntries.Enqueue($"{actor.Name}:unknown-result-shape");
        return new PurchaseActorResult(
            Actor: actor,
            Succeeded: false,
            StatusCode: null,
            Response: null,
            PayloadJson: null,
            Duration: duration,
            Exception: null);
    }

    private static bool TryParseStoredPurchaseResponse(string? payloadJson, out PurchaseResponse response)
    {
        response = new PurchaseResponse();

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<PurchaseResponse>(payloadJson, CaseInsensitiveJsonOptions);
            if (parsed == null || parsed.InvoiceId == Guid.Empty)
            {
                return false;
            }

            response = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task AssertNoUncommittedDurableStateVisibleAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var stock = await verifyContext.ProductVariantStocks.SingleAsync(item =>
            item.TenantId == seeded.TenantId &&
            item.WarehouseId == seeded.WarehouseId &&
            item.VariantId == seeded.VariantId);

        Assert.Equal(InitialStockQuantity, stock.Quantity);
        Assert.Equal(0, await verifyContext.PurchaseInvoices.CountAsync(invoice => invoice.TenantId == seeded.TenantId));
        Assert.Equal(0, await verifyContext.StockMovements.CountAsync(movement =>
            movement.TenantId == seeded.TenantId &&
            movement.WarehouseId == seeded.WarehouseId &&
            movement.ProductVariantId == seeded.VariantId &&
            movement.MovementType == StockMovementType.Purchase));
        Assert.Equal(0, await verifyContext.SupplierTransactions.CountAsync(transaction =>
            transaction.TenantId == seeded.TenantId &&
            transaction.SupplierId == seeded.SupplierId));
        Assert.Equal(0, await verifyContext.AccountLedgers.CountAsync(ledger =>
            ledger.TenantId == seeded.TenantId &&
            ledger.PartyType == PartyType.Supplier &&
            ledger.PartyId == seeded.SupplierId));
        Assert.Equal(0m, await verifyContext.Suppliers
            .Where(supplier => supplier.TenantId == seeded.TenantId && supplier.Id == seeded.SupplierId)
            .Select(supplier => supplier.Balance)
            .SingleAsync());
        Assert.Equal(0, await verifyContext.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .CountAsync(message =>
                message.TenantId == seeded.TenantId &&
                message.EventType == "PosPurchase"));
        Assert.Equal(0, await verifyContext.StockSyncOutboxMessages
            .IgnoreQueryFilters()
            .CountAsync(message =>
                message.TenantId == seeded.TenantId &&
                message.VariantId == seeded.VariantId &&
                message.WarehouseId == seeded.WarehouseId));
        Assert.Equal(0, await verifyContext.JournalEntries
            .IgnoreQueryFilters()
            .CountAsync(journal =>
                journal.TenantId == seeded.TenantId &&
                journal.SourceType == "PurchaseInvoice"));
        Assert.Equal(0, await verifyContext.PurchaseRequestIdempotencyRecords.CountAsync());

        progressEntries.Enqueue("verification:no-uncommitted-purchase-state-visible");
    }

    private static async Task AssertAuthoritativeConvergenceAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        PurchaseActor actorA,
        PurchaseActor actorB,
        IReadOnlyCollection<PurchaseActorResult> results,
        ConcurrentQueue<string> progressEntries)
    {
        var successfulResults = results
            .Where(result => result.Succeeded && result.Response is not null)
            .ToList();
        var failedResults = results
            .Where(result => !result.Succeeded)
            .ToList();

        Assert.InRange(successfulResults.Count, 1, 2);

        var successfulActors = successfulResults.Select(result => result.Actor).ToList();
        var successfulInvoiceIds = successfulResults
            .Select(result => result.Response!.InvoiceId)
            .OrderBy(id => id)
            .ToArray();
        var expectedQuantity = successfulActors.Sum(actor => actor.Quantity);
        var expectedAmount = successfulActors.Sum(actor => actor.Quantity * AuthoritativeUnitCost);

        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var invoices = await verifyContext.PurchaseInvoices
            .AsNoTracking()
            .Where(invoice => invoice.TenantId == seeded.TenantId)
            .OrderBy(invoice => invoice.InvoiceNumber)
            .ToListAsync();

        Assert.Equal(successfulResults.Count, invoices.Count);
        Assert.Equal(
            successfulInvoiceIds,
            invoices.Select(invoice => invoice.Id).OrderBy(id => id).ToArray());
        Assert.All(invoices, invoice =>
        {
            Assert.Equal(seeded.TenantId, invoice.TenantId);
            Assert.Equal(seeded.SupplierId, invoice.SupplierId);
            Assert.Equal(seeded.WarehouseId, invoice.WarehouseId);
            Assert.Equal(InvoiceStatus.Completed, invoice.Status);
            Assert.Equal(0, invoice.AccountingMutationVersion);
            Assert.Equal(invoice.TotalAmount, invoice.RemainingAmount);
            Assert.Equal(0m, invoice.PaidAmount);
        });

        var invoiceNumberSuffixes = invoices
            .Select(invoice => invoice.InvoiceNumber.Split('-').Last())
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(successfulResults.Count, invoiceNumberSuffixes.Distinct(StringComparer.Ordinal).Count());

        var stock = await verifyContext.ProductVariantStocks.SingleAsync(item =>
            item.TenantId == seeded.TenantId &&
            item.WarehouseId == seeded.WarehouseId &&
            item.VariantId == seeded.VariantId);
        Assert.Equal(expectedQuantity, stock.Quantity);

        var movements = await verifyContext.StockMovements
            .AsNoTracking()
            .Where(movement =>
                movement.TenantId == seeded.TenantId &&
                movement.WarehouseId == seeded.WarehouseId &&
                movement.ProductVariantId == seeded.VariantId &&
                movement.MovementType == StockMovementType.Purchase)
            .OrderBy(movement => movement.QuantityAfter)
            .ThenBy(movement => movement.Id)
            .ToListAsync();

        Assert.Equal(successfulResults.Count, movements.Count);
        Assert.Equal(expectedQuantity, movements.Sum(movement => movement.QuantityChange));
        Assert.All(movements, movement =>
        {
            Assert.Contains(movement.ReferenceId ?? Guid.Empty, successfulInvoiceIds);
            Assert.Equal(movement.QuantityAfter, movement.AvailableAfter);
        });

        if (movements.Count == 1)
        {
            Assert.Equal(expectedQuantity, movements[0].QuantityChange);
            Assert.Equal(expectedQuantity, movements[0].QuantityAfter);
        }
        else
        {
            Assert.Equal(movements[0].QuantityChange, movements[0].QuantityAfter);
            Assert.Equal(movements[0].QuantityAfter + movements[1].QuantityChange, movements[1].QuantityAfter);
            Assert.Equal(expectedQuantity, movements[1].QuantityAfter);
        }

        var supplier = await verifyContext.Suppliers.SingleAsync(item =>
            item.TenantId == seeded.TenantId &&
            item.Id == seeded.SupplierId);
        var expectedSupplierBalance = -expectedAmount;
        Assert.Equal(expectedSupplierBalance, supplier.Balance);

        var supplierTransactions = await verifyContext.SupplierTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.TenantId == seeded.TenantId &&
                transaction.SupplierId == seeded.SupplierId)
            .OrderBy(transaction => transaction.TransactionDate)
            .ThenBy(transaction => transaction.CreatedAt)
            .ThenBy(transaction => transaction.Id)
            .ToListAsync();

        Assert.Equal(successfulResults.Count, supplierTransactions.Count);
        Assert.All(supplierTransactions, transaction =>
        {
            Assert.Equal(TransactionType.Purchase, transaction.TransactionType);
            Assert.Equal(0m, transaction.DebitAmount);
            Assert.Contains(transaction.ReferenceId ?? Guid.Empty, successfulInvoiceIds);
        });
        Assert.Equal(expectedAmount, supplierTransactions.Sum(transaction => transaction.CreditAmount));
        Assert.Equal(expectedSupplierBalance, supplierTransactions[^1].BalanceAfter);
        if (supplierTransactions.Count == 1)
        {
            Assert.Equal(expectedSupplierBalance, supplierTransactions[0].BalanceAfter);
        }
        else
        {
            Assert.Equal(-supplierTransactions[0].CreditAmount, supplierTransactions[0].BalanceAfter);
        }

        var supplierLedgers = await verifyContext.AccountLedgers
            .AsNoTracking()
            .Where(ledger =>
                ledger.TenantId == seeded.TenantId &&
                ledger.PartyType == PartyType.Supplier &&
                ledger.PartyId == seeded.SupplierId)
            .OrderBy(ledger => ledger.TransactionDate)
            .ThenBy(ledger => ledger.CreatedAt)
            .ThenBy(ledger => ledger.Id)
            .ToListAsync();

        Assert.Equal(successfulResults.Count, supplierLedgers.Count);
        Assert.Equal(expectedAmount, supplierLedgers.Sum(ledger => ledger.CreditAmount));
        Assert.All(supplierLedgers, ledger => Assert.Equal(0m, ledger.DebitAmount));
        Assert.Equal(expectedSupplierBalance, supplierLedgers[^1].RunningBalance);

        var outboxMessages = await verifyContext.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.EventType == "PosPurchase")
            .OrderBy(message => message.BusinessEntityId)
            .ToListAsync();

        Assert.Equal(successfulResults.Count, outboxMessages.Count);
        Assert.All(outboxMessages, message =>
        {
            Assert.Equal("PurchaseInvoice", message.SourceType);
            Assert.Equal(0, message.AggregateVersion);
            Assert.NotNull(message.ProcessedAtUtc);
            Assert.NotNull(message.JournalId);
            Assert.Null(message.ClaimedUntilUtc);
            Assert.False(message.IsFailed);
            Assert.Contains(message.BusinessEntityId, successfulInvoiceIds);
        });
        Assert.Equal(outboxMessages.Count, outboxMessages.Select(message => message.IdempotencyKey).Distinct(StringComparer.Ordinal).Count());

        var stockSyncMessages = await verifyContext.StockSyncOutboxMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.VariantId == seeded.VariantId &&
                message.WarehouseId == seeded.WarehouseId)
            .OrderBy(message => message.EventVersion)
            .ThenBy(message => message.Id)
            .ToListAsync();

        Assert.Equal(successfulResults.Count, stockSyncMessages.Count);
        Assert.All(stockSyncMessages, message =>
        {
            Assert.Equal(seeded.ProductId, message.ProductId);
            Assert.Equal(nameof(ProductVariantStockChangedEvent), message.EventType);
            Assert.Null(message.PublishedAtUtc);
            Assert.False(message.IsFailed);
            Assert.True(message.EventVersion > 0);
        });

        var journals = await verifyContext.JournalEntries
            .IgnoreQueryFilters()
            .Include(journal => journal.LedgerEntries)
            .AsNoTracking()
            .Where(journal =>
                journal.TenantId == seeded.TenantId &&
                journal.SourceType == "PurchaseInvoice")
            .OrderBy(journal => journal.SourceId)
            .ToListAsync();

        Assert.Equal(successfulResults.Count, journals.Count);
        Assert.All(journals, journal =>
        {
            Assert.Equal(JournalStatus.Posted, journal.Status);
            Assert.True(journal.IsBalanced);
            Assert.Contains(journal.SourceId, successfulInvoiceIds);
            Assert.Equal(2, journal.LedgerEntries.Count);
        });
        Assert.Equal(journals.Count, journals.Select(journal => journal.IdempotencyKey).Distinct(StringComparer.Ordinal).Count());

        var idempotencyRecords = await verifyContext.PurchaseRequestIdempotencyRecords
            .AsNoTracking()
            .OrderBy(record => record.Key)
            .ToListAsync();

        Assert.Equal(successfulResults.Count, idempotencyRecords.Count);
        foreach (var successfulResult in successfulResults)
        {
            var expectedKey = BuildSupplierMutationIdempotencyKey(seeded.TenantId, successfulResult.Actor.RequestId);
            var record = Assert.Single(idempotencyRecords.Where(item => item.Key == expectedKey));
            Assert.Equal(PurchaseRequestIdempotencyStatus.Completed, record.Status);
            Assert.Equal(successfulResult.Response!.InvoiceId, record.PurchaseInvoiceId);
            Assert.Equal(successfulResult.Actor.PurchaseAttemptId, record.PurchaseAttemptId);
            Assert.Equal(StatusCodes.Status200OK, record.ResponseStatusCode);
            Assert.NotNull(record.CompletedAtUtc);
            Assert.False(string.IsNullOrWhiteSpace(record.RequestFingerprint));
        }

        Assert.All(failedResults, failedResult =>
        {
            Assert.DoesNotContain(
                failedResult.Actor.RequestId,
                idempotencyRecords.Select(record => record.Key),
                StringComparer.Ordinal);
        });

        if (successfulResults.Count == 2)
        {
            Assert.Equal(AggregateCommittedQuantity, expectedQuantity);
            Assert.Equal(AggregateCommittedAmount, expectedAmount);
            progressEntries.Enqueue("verification:pi-con-01-safe-outcome-a-both-committed");
        }
        else
        {
            Assert.Single(failedResults);
            var failedActor = failedResults[0].Actor;
            Assert.DoesNotContain(
                failedActor.RequestId,
                idempotencyRecords.Select(record => record.Key),
                StringComparer.Ordinal);
            progressEntries.Enqueue("verification:pi-con-01-safe-outcome-b-failed-closed");
        }
    }

    private static async Task AssertReplaySafetyAsync(
        string connectionString,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        PurchaseActorResult successfulResult,
        ConcurrentQueue<string> progressEntries)
    {
        await using var replayContext = CreateDbContext(connectionString, tenantContext);
        await ConfigureSessionTimeoutsAsync(replayContext);
        var controller = CreateController(replayContext, tenantContext, successfulResult.Actor, new RecordingLogger<PurchasesController>());
        var replayResult = await controller.CreatePurchase(BuildCreatePurchaseRequest(seeded, successfulResult.Actor));
        var replayParsed = ParseActorResult(
            successfulResult.Actor,
            replayResult,
            TimeSpan.Zero,
            progressEntries);

        Assert.True(replayParsed.Succeeded, replayParsed.PayloadJson ?? "Replay did not succeed.");
        Assert.NotNull(replayParsed.Response);
        Assert.Equal(successfulResult.Response!.InvoiceId, replayParsed.Response!.InvoiceId);
        Assert.Equal(successfulResult.Response.InvoiceNumber, replayParsed.Response.InvoiceNumber);
        Assert.Equal(successfulResult.Response.TotalAmount, replayParsed.Response.TotalAmount);

        await using var verifyContext = CreateDbContext(connectionString, tenantContext);
        var invoiceIds = await verifyContext.PurchaseInvoices
            .Where(invoice => invoice.TenantId == seeded.TenantId)
            .Select(invoice => invoice.Id)
            .ToListAsync();
        var outboxIds = await verifyContext.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .Where(message => message.TenantId == seeded.TenantId && message.EventType == "PosPurchase")
            .Select(message => message.BusinessEntityId)
            .ToListAsync();
        var journalSourceIds = await verifyContext.JournalEntries
            .IgnoreQueryFilters()
            .Where(journal => journal.TenantId == seeded.TenantId && journal.SourceType == "PurchaseInvoice")
            .Select(journal => journal.SourceId)
            .ToListAsync();

        Assert.Equal(invoiceIds.Count, invoiceIds.Distinct().Count());
        Assert.Equal(invoiceIds.Count, outboxIds.Distinct().Count());
        Assert.Equal(invoiceIds.Count, journalSourceIds.Distinct().Count());
        Assert.Equal(
            invoiceIds.OrderBy(id => id).ToArray(),
            outboxIds.OrderBy(id => id).ToArray());
        Assert.Equal(
            invoiceIds.OrderBy(id => id).ToArray(),
            journalSourceIds.OrderBy(id => id).ToArray());

        progressEntries.Enqueue("verification:pi-con-01-same-key-replay-safe");
    }

    private static async Task<PiCon03StateSnapshot> LoadPiCon03StateSnapshotAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId)
    {
        await using var context = scope.CreateDbContext(tenantContext);

        var invoice = await context.PurchaseInvoices.SingleAsync(item =>
            item.TenantId == seeded.TenantId &&
            item.Id == invoiceId);
        var supplier = await context.Suppliers.SingleAsync(item =>
            item.TenantId == seeded.TenantId &&
            item.Id == seeded.SupplierId);
        var accountingOutboxMessages = await context.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.EventType == "PosPurchase" &&
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
            .ThenBy(message => message.Id)
            .ToListAsync();
        var supplierTransactions = await context.SupplierTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.TenantId == seeded.TenantId &&
                transaction.SupplierId == seeded.SupplierId &&
                transaction.ReferenceId == invoiceId)
            .OrderBy(transaction => transaction.CreatedAt)
            .ThenBy(transaction => transaction.Id)
            .ToListAsync();
        var supplierLedgers = await context.AccountLedgers
            .AsNoTracking()
            .Where(ledger =>
                ledger.TenantId == seeded.TenantId &&
                ledger.PartyType == PartyType.Supplier &&
                ledger.PartyId == seeded.SupplierId &&
                ledger.ReferenceId == invoiceId)
            .OrderBy(ledger => ledger.CreatedAt)
            .ThenBy(ledger => ledger.Id)
            .ToListAsync();
        var journalEntries = await context.JournalEntries
            .IgnoreQueryFilters()
            .Where(journal =>
                journal.TenantId == seeded.TenantId &&
                journal.SourceType == "PurchaseInvoice" &&
                journal.SourceId == invoiceId)
            .OrderBy(journal => journal.CreatedAt)
            .ThenBy(journal => journal.Id)
            .ToListAsync();

        var accountingOutbox = accountingOutboxMessages.SingleOrDefault();
        var stockSyncOutbox = stockSyncOutboxMessages.SingleOrDefault();

        return new PiCon03StateSnapshot(
            PurchaseInvoices: await context.PurchaseInvoices.CountAsync(item => item.TenantId == seeded.TenantId),
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
                movement.ReferenceId == invoiceId &&
                movement.MovementType == StockMovementType.Purchase),
            SupplierTransactions: supplierTransactions.Count,
            SupplierLedgers: supplierLedgers.Count,
            SupplierBalance: supplier.Balance,
            AccountingOutboxMessages: accountingOutboxMessages.Count,
            AccountingOutboxId: accountingOutbox?.Id ?? Guid.Empty,
            AccountingOutboxJournalId: accountingOutbox?.JournalId,
            AccountingOutboxClaimedUntilUtc: accountingOutbox?.ClaimedUntilUtc,
            AccountingOutboxProcessedAtUtc: accountingOutbox?.ProcessedAtUtc,
            StockSyncOutboxMessages: stockSyncOutboxMessages.Count,
            StockSyncOutboxId: stockSyncOutbox?.Id ?? Guid.Empty,
            StockSyncEventId: stockSyncOutbox?.EventId ?? Guid.Empty,
            StockSyncEventVersion: stockSyncOutbox?.EventVersion ?? 0,
            StockSyncClaimedUntilUtc: stockSyncOutbox?.ClaimedUntilUtc,
            StockSyncPublishedAtUtc: stockSyncOutbox?.PublishedAtUtc,
            StockSyncAttemptCount: stockSyncOutbox?.AttemptCount ?? 0,
            ProcessedEvents: await context.Set<ProcessedEvent>()
                .IgnoreQueryFilters()
                .CountAsync(processed => processed.TenantId == seeded.TenantId),
            InventoryEventInbox: await context.InventoryEventInbox
                .IgnoreQueryFilters()
                .CountAsync(inbox => inbox.TenantId == seeded.TenantId),
            JournalEntries: journalEntries.Count,
            JournalFingerprint: string.Join(
                "|",
                journalEntries.Select(journal => $"{journal.Id}:{journal.IdempotencyKey}")
                    .OrderBy(value => value, StringComparer.Ordinal)),
            InvoiceStatus: invoice.Status,
            InvoiceTotalAmount: invoice.TotalAmount,
            InvoiceRemainingAmount: invoice.RemainingAmount,
            InvoicePaidAmount: invoice.PaidAmount,
            InvoiceMutationVersion: invoice.AccountingMutationVersion);
    }

    private static async Task ReopenPurchaseAccountingOutboxMessageForWorkerReplayAsync(
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
                message.EventType == "PosPurchase" &&
                message.BusinessEntityId == invoiceId);

        outboxMessage.ProcessedAtUtc = null;
        outboxMessage.ClaimedUntilUtc = null;
        outboxMessage.IsFailed = false;
        outboxMessage.LastError = null;
        outboxMessage.NextRetryAtUtc = null;
        outboxMessage.AttemptCount = 0;

        await context.SaveChangesAsync();
        progressEntries.Enqueue("setup:reopened-purchase-accounting-outbox-row-for-real-worker-overlap");
    }

    private static async Task AssertPurchaseAccountingWorkerClaimVisibleDuringActiveProcessingAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId,
        PiCon03StateSnapshot canonicalBaseline,
        ConcurrentQueue<string> progressEntries)
    {
        var visibleState = await LoadPiCon03StateSnapshotAsync(scope, tenantContext, seeded, invoiceId);

        Assert.Equal(canonicalBaseline.PurchaseInvoices, visibleState.PurchaseInvoices);
        Assert.Equal(canonicalBaseline.StockQuantity, visibleState.StockQuantity);
        Assert.Equal(canonicalBaseline.StockMovements, visibleState.StockMovements);
        Assert.Equal(canonicalBaseline.SupplierTransactions, visibleState.SupplierTransactions);
        Assert.Equal(canonicalBaseline.SupplierLedgers, visibleState.SupplierLedgers);
        Assert.Equal(canonicalBaseline.SupplierBalance, visibleState.SupplierBalance);
        Assert.Equal(canonicalBaseline.AccountingOutboxMessages, visibleState.AccountingOutboxMessages);
        Assert.Equal(canonicalBaseline.AccountingOutboxId, visibleState.AccountingOutboxId);
        Assert.Equal(canonicalBaseline.AccountingOutboxJournalId, visibleState.AccountingOutboxJournalId);
        Assert.Null(visibleState.AccountingOutboxProcessedAtUtc);
        Assert.NotNull(visibleState.AccountingOutboxClaimedUntilUtc);
        Assert.Equal(canonicalBaseline.StockSyncOutboxMessages, visibleState.StockSyncOutboxMessages);
        Assert.Equal(canonicalBaseline.StockSyncOutboxId, visibleState.StockSyncOutboxId);
        Assert.Equal(canonicalBaseline.StockSyncEventId, visibleState.StockSyncEventId);
        Assert.Equal(canonicalBaseline.StockSyncEventVersion, visibleState.StockSyncEventVersion);
        Assert.Equal(canonicalBaseline.StockSyncPublishedAtUtc, visibleState.StockSyncPublishedAtUtc);
        Assert.Equal(canonicalBaseline.ProcessedEvents, visibleState.ProcessedEvents);
        Assert.Equal(canonicalBaseline.InventoryEventInbox, visibleState.InventoryEventInbox);
        Assert.Equal(canonicalBaseline.JournalEntries, visibleState.JournalEntries);
        Assert.Equal(canonicalBaseline.JournalFingerprint, visibleState.JournalFingerprint);
        Assert.Equal(canonicalBaseline.InvoiceStatus, visibleState.InvoiceStatus);
        Assert.Equal(canonicalBaseline.InvoiceTotalAmount, visibleState.InvoiceTotalAmount);
        Assert.Equal(canonicalBaseline.InvoiceRemainingAmount, visibleState.InvoiceRemainingAmount);
        Assert.Equal(canonicalBaseline.InvoicePaidAmount, visibleState.InvoicePaidAmount);
        Assert.Equal(canonicalBaseline.InvoiceMutationVersion, visibleState.InvoiceMutationVersion);

        progressEntries.Enqueue("verification:purchase-accounting-worker-claim-visible-during-active-processing");
    }

    private static async Task AssertPurchaseAccountingDuplicateWorkerConvergenceAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId,
        PiCon03StateSnapshot canonicalBaseline,
        AccountingOutboxWorkerResult originalWorker,
        AccountingOutboxWorkerResult duplicateWorker,
        ConcurrentQueue<string> progressEntries)
    {
        var after = await LoadPiCon03StateSnapshotAsync(scope, tenantContext, seeded, invoiceId);

        Assert.Null(originalWorker.Exception);
        Assert.Null(duplicateWorker.Exception);
        Assert.Equal(1, originalWorker.ProcessedCount);
        Assert.Equal(0, duplicateWorker.ProcessedCount);

        Assert.Equal(canonicalBaseline.PurchaseInvoices, after.PurchaseInvoices);
        Assert.Equal(canonicalBaseline.StockQuantity, after.StockQuantity);
        Assert.Equal(canonicalBaseline.StockMovements, after.StockMovements);
        Assert.Equal(canonicalBaseline.SupplierTransactions, after.SupplierTransactions);
        Assert.Equal(canonicalBaseline.SupplierLedgers, after.SupplierLedgers);
        Assert.Equal(canonicalBaseline.SupplierBalance, after.SupplierBalance);
        Assert.Equal(canonicalBaseline.AccountingOutboxMessages, after.AccountingOutboxMessages);
        Assert.Equal(canonicalBaseline.AccountingOutboxId, after.AccountingOutboxId);
        Assert.Equal(canonicalBaseline.AccountingOutboxJournalId, after.AccountingOutboxJournalId);
        Assert.Null(after.AccountingOutboxClaimedUntilUtc);
        Assert.NotNull(after.AccountingOutboxProcessedAtUtc);
        Assert.Equal(canonicalBaseline.StockSyncOutboxMessages, after.StockSyncOutboxMessages);
        Assert.Equal(canonicalBaseline.StockSyncOutboxId, after.StockSyncOutboxId);
        Assert.Equal(canonicalBaseline.StockSyncEventId, after.StockSyncEventId);
        Assert.Equal(canonicalBaseline.StockSyncEventVersion, after.StockSyncEventVersion);
        Assert.Equal(canonicalBaseline.StockSyncPublishedAtUtc, after.StockSyncPublishedAtUtc);
        Assert.Equal(canonicalBaseline.ProcessedEvents, after.ProcessedEvents);
        Assert.Equal(canonicalBaseline.InventoryEventInbox, after.InventoryEventInbox);
        Assert.Equal(canonicalBaseline.JournalEntries, after.JournalEntries);
        Assert.Equal(canonicalBaseline.JournalFingerprint, after.JournalFingerprint);
        Assert.Equal(canonicalBaseline.InvoiceStatus, after.InvoiceStatus);
        Assert.Equal(canonicalBaseline.InvoiceTotalAmount, after.InvoiceTotalAmount);
        Assert.Equal(canonicalBaseline.InvoiceRemainingAmount, after.InvoiceRemainingAmount);
        Assert.Equal(canonicalBaseline.InvoicePaidAmount, after.InvoicePaidAmount);
        Assert.Equal(canonicalBaseline.InvoiceMutationVersion, after.InvoiceMutationVersion);

        progressEntries.Enqueue("verification:pi-con-03-accounting-overlap-converged");
    }

    private static async Task AssertPurchaseStockSyncClaimVisibleDuringActivePublishAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId,
        PiCon03StateSnapshot baseline,
        ConcurrentQueue<string> progressEntries)
    {
        var duringPublish = await LoadPiCon03StateSnapshotAsync(scope, tenantContext, seeded, invoiceId);

        Assert.Equal(baseline.PurchaseInvoices, duringPublish.PurchaseInvoices);
        Assert.Equal(baseline.StockQuantity, duringPublish.StockQuantity);
        Assert.Equal(baseline.StockMovements, duringPublish.StockMovements);
        Assert.Equal(baseline.SupplierTransactions, duringPublish.SupplierTransactions);
        Assert.Equal(baseline.SupplierLedgers, duringPublish.SupplierLedgers);
        Assert.Equal(baseline.SupplierBalance, duringPublish.SupplierBalance);
        Assert.Equal(baseline.AccountingOutboxMessages, duringPublish.AccountingOutboxMessages);
        Assert.Equal(baseline.AccountingOutboxId, duringPublish.AccountingOutboxId);
        Assert.Equal(baseline.AccountingOutboxJournalId, duringPublish.AccountingOutboxJournalId);
        Assert.Equal(baseline.AccountingOutboxProcessedAtUtc, duringPublish.AccountingOutboxProcessedAtUtc);
        Assert.Equal(baseline.ProcessedEvents, duringPublish.ProcessedEvents);
        Assert.Equal(baseline.InventoryEventInbox, duringPublish.InventoryEventInbox);
        Assert.Equal(baseline.JournalEntries, duringPublish.JournalEntries);
        Assert.Equal(baseline.JournalFingerprint, duringPublish.JournalFingerprint);
        Assert.Equal(baseline.InvoiceStatus, duringPublish.InvoiceStatus);
        Assert.Equal(baseline.InvoiceTotalAmount, duringPublish.InvoiceTotalAmount);
        Assert.Equal(baseline.InvoiceRemainingAmount, duringPublish.InvoiceRemainingAmount);
        Assert.Equal(baseline.InvoicePaidAmount, duringPublish.InvoicePaidAmount);
        Assert.Equal(baseline.InvoiceMutationVersion, duringPublish.InvoiceMutationVersion);
        Assert.Equal(baseline.StockSyncOutboxMessages, duringPublish.StockSyncOutboxMessages);
        Assert.Equal(baseline.StockSyncOutboxId, duringPublish.StockSyncOutboxId);
        Assert.Equal(baseline.StockSyncEventId, duringPublish.StockSyncEventId);
        Assert.Equal(baseline.StockSyncEventVersion, duringPublish.StockSyncEventVersion);
        Assert.Null(duringPublish.StockSyncPublishedAtUtc);
        Assert.NotNull(duringPublish.StockSyncClaimedUntilUtc);

        progressEntries.Enqueue("verification:purchase-stock-sync-claim-visible-during-active-publish");
    }

    private static async Task AssertPurchaseStockSyncDuplicateWorkerConvergenceAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid invoiceId,
        PiCon03StateSnapshot accountingBaseline,
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

        var after = await LoadPiCon03StateSnapshotAsync(scope, tenantContext, seeded, invoiceId);

        Assert.Equal(accountingBaseline.PurchaseInvoices, after.PurchaseInvoices);
        Assert.Equal(accountingBaseline.StockQuantity, after.StockQuantity);
        Assert.Equal(accountingBaseline.StockMovements, after.StockMovements);
        Assert.Equal(accountingBaseline.SupplierTransactions, after.SupplierTransactions);
        Assert.Equal(accountingBaseline.SupplierLedgers, after.SupplierLedgers);
        Assert.Equal(accountingBaseline.SupplierBalance, after.SupplierBalance);
        Assert.Equal(accountingBaseline.AccountingOutboxMessages, after.AccountingOutboxMessages);
        Assert.Equal(accountingBaseline.AccountingOutboxId, after.AccountingOutboxId);
        Assert.Equal(accountingBaseline.AccountingOutboxJournalId, after.AccountingOutboxJournalId);
        Assert.Equal(accountingBaseline.AccountingOutboxProcessedAtUtc, after.AccountingOutboxProcessedAtUtc);
        Assert.Equal(accountingBaseline.ProcessedEvents, after.ProcessedEvents);
        Assert.Equal(accountingBaseline.InventoryEventInbox, after.InventoryEventInbox);
        Assert.Equal(accountingBaseline.JournalEntries, after.JournalEntries);
        Assert.Equal(accountingBaseline.JournalFingerprint, after.JournalFingerprint);
        Assert.Equal(accountingBaseline.InvoiceStatus, after.InvoiceStatus);
        Assert.Equal(accountingBaseline.InvoiceTotalAmount, after.InvoiceTotalAmount);
        Assert.Equal(accountingBaseline.InvoiceRemainingAmount, after.InvoiceRemainingAmount);
        Assert.Equal(accountingBaseline.InvoicePaidAmount, after.InvoicePaidAmount);
        Assert.Equal(accountingBaseline.InvoiceMutationVersion, after.InvoiceMutationVersion);
        Assert.Equal(accountingBaseline.StockSyncOutboxMessages, after.StockSyncOutboxMessages);
        Assert.Equal(accountingBaseline.StockSyncOutboxId, after.StockSyncOutboxId);
        Assert.Equal(accountingBaseline.StockSyncEventId, after.StockSyncEventId);
        Assert.Equal(accountingBaseline.StockSyncEventVersion, after.StockSyncEventVersion);
        Assert.NotNull(after.StockSyncPublishedAtUtc);
        Assert.Null(after.StockSyncClaimedUntilUtc);
        Assert.Equal(0, after.StockSyncAttemptCount);

        progressEntries.Enqueue("verification:pi-con-03-stock-sync-overlap-converged");
    }

    private static async Task AssertActiveReplayConvergenceAsync(
        TestDatabaseScope scope,
        string connectionString,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        PurchaseActor originalActor,
        PurchaseActor replayActor,
        IReadOnlyCollection<PurchaseActorResult> results,
        ConcurrentQueue<string> progressEntries)
    {
        await using var verifyContext = scope.CreateDbContext(tenantContext);

        var persistedInvoices = await verifyContext.PurchaseInvoices
            .AsNoTracking()
            .Where(invoice => invoice.TenantId == seeded.TenantId)
            .OrderBy(invoice => invoice.CreatedAt)
            .ToListAsync();
        var persistedStocks = await verifyContext.ProductVariantStocks
            .AsNoTracking()
            .Where(stock =>
                stock.TenantId == seeded.TenantId &&
                stock.WarehouseId == seeded.WarehouseId &&
                stock.VariantId == seeded.VariantId)
            .ToListAsync();
        var persistedMovements = await verifyContext.StockMovements
            .AsNoTracking()
            .Where(movement =>
                movement.TenantId == seeded.TenantId &&
                movement.ProductVariantId == seeded.VariantId &&
                movement.WarehouseId == seeded.WarehouseId &&
                movement.MovementType == StockMovementType.Purchase)
            .OrderBy(movement => movement.CreatedAt)
            .ThenBy(movement => movement.Id)
            .ToListAsync();
        var stockSyncOutboxMessages = await verifyContext.StockSyncOutboxMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.VariantId == seeded.VariantId &&
                message.WarehouseId == seeded.WarehouseId)
            .OrderBy(message => message.CreatedAt)
            .ToListAsync();
        var accountingOutboxMessages = await verifyContext.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.EventType == "PosPurchase")
            .OrderBy(message => message.CreatedAt)
            .ToListAsync();
        var supplierTransactions = await verifyContext.SupplierTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.TenantId == seeded.TenantId &&
                transaction.SupplierId == seeded.SupplierId)
            .OrderBy(transaction => transaction.CreatedAt)
            .ThenBy(transaction => transaction.Id)
            .ToListAsync();
        var supplierLedgers = await verifyContext.AccountLedgers
            .AsNoTracking()
            .Where(ledger =>
                ledger.TenantId == seeded.TenantId &&
                ledger.PartyType == PartyType.Supplier &&
                ledger.PartyId == seeded.SupplierId)
            .OrderBy(ledger => ledger.CreatedAt)
            .ThenBy(ledger => ledger.Id)
            .ToListAsync();
        var journalEntries = await verifyContext.JournalEntries
            .IgnoreQueryFilters()
            .Include(journal => journal.LedgerEntries)
            .AsNoTracking()
            .Where(journal =>
                journal.TenantId == seeded.TenantId &&
                journal.SourceType == "PurchaseInvoice")
            .OrderBy(journal => journal.CreatedAt)
            .ThenBy(journal => journal.Id)
            .ToListAsync();
        var idempotencyRecords = await verifyContext.PurchaseRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(record => record.TenantId == seeded.TenantId && record.Operation == "CREATE")
            .OrderBy(record => record.CreatedAt)
            .ToListAsync();
        var supplier = await verifyContext.Suppliers.SingleAsync(item => item.Id == seeded.SupplierId);

        Assert.Single(persistedStocks);
        var stock = persistedStocks.Single();
        Assert.Equal(ActorAQuantity, stock.Quantity);

        Assert.Single(persistedInvoices);
        Assert.Single(persistedMovements);
        Assert.Single(stockSyncOutboxMessages);
        Assert.Single(accountingOutboxMessages);
        Assert.Single(supplierTransactions);
        Assert.Single(supplierLedgers);
        Assert.Single(idempotencyRecords);

        var invoice = persistedInvoices.Single();
        var movement = persistedMovements.Single();
        var stockSyncMessage = stockSyncOutboxMessages.Single();
        var outboxMessage = accountingOutboxMessages.Single();
        var supplierTransaction = supplierTransactions.Single();
        var supplierLedger = supplierLedgers.Single();
        var idempotencyRecord = idempotencyRecords.Single();

        Assert.Equal(seeded.TenantId, invoice.TenantId);
        Assert.Equal(seeded.SupplierId, invoice.SupplierId);
        Assert.Equal(seeded.WarehouseId, invoice.WarehouseId);
        Assert.Equal(InvoiceStatus.Completed, invoice.Status);
        Assert.Equal(ActorATotalAmount, invoice.TotalAmount);
        Assert.Equal(ActorATotalAmount, invoice.RemainingAmount);
        Assert.Equal(0m, invoice.PaidAmount);
        Assert.Equal(0, invoice.AccountingMutationVersion);

        Assert.Equal(ActorAQuantity, movement.QuantityChange);
        Assert.Equal(ActorAQuantity, movement.QuantityAfter);
        Assert.Equal(ActorAQuantity, movement.AvailableAfter);
        Assert.Equal(invoice.Id, movement.ReferenceId);
        Assert.Equal(invoice.InvoiceNumber, movement.ReferenceNumber);

        Assert.Equal("PosPurchase", outboxMessage.EventType);
        Assert.Equal("PurchaseInvoice", outboxMessage.SourceType);
        Assert.Equal(invoice.Id, outboxMessage.BusinessEntityId);
        Assert.Equal(0, outboxMessage.AggregateVersion);
        Assert.False(outboxMessage.IsFailed);
        Assert.NotNull(outboxMessage.ProcessedAtUtc);
        Assert.NotNull(outboxMessage.JournalId);
        Assert.Null(outboxMessage.ClaimedUntilUtc);

        Assert.Equal(nameof(ProductVariantStockChangedEvent), stockSyncMessage.EventType);
        Assert.Equal(seeded.ProductId, stockSyncMessage.ProductId);
        Assert.Equal(seeded.VariantId, stockSyncMessage.VariantId);
        Assert.Equal(seeded.WarehouseId, stockSyncMessage.WarehouseId);
        Assert.True(stockSyncMessage.EventVersion > 0);
        Assert.Null(stockSyncMessage.PublishedAtUtc);
        Assert.False(stockSyncMessage.IsFailed);

        Assert.Equal(TransactionType.Purchase, supplierTransaction.TransactionType);
        Assert.Equal(0m, supplierTransaction.DebitAmount);
        Assert.Equal(ActorATotalAmount, supplierTransaction.CreditAmount);
        Assert.Equal(-ActorATotalAmount, supplierTransaction.BalanceAfter);
        Assert.Equal(invoice.Id, supplierTransaction.ReferenceId);
        Assert.Equal(invoice.InvoiceNumber, supplierTransaction.ReferenceNumber);

        Assert.Equal(TransactionType.Purchase, supplierLedger.TransactionType);
        Assert.Equal(0m, supplierLedger.DebitAmount);
        Assert.Equal(ActorATotalAmount, supplierLedger.CreditAmount);
        Assert.Equal(-ActorATotalAmount, supplierLedger.RunningBalance);
        Assert.Equal(invoice.Id, supplierLedger.ReferenceId);

        Assert.Equal(-ActorATotalAmount, supplier.Balance);

        Assert.Single(journalEntries);
        Assert.All(journalEntries, journal =>
        {
            Assert.Equal(invoice.Id, journal.SourceId);
            Assert.Equal(JournalStatus.Posted, journal.Status);
            Assert.True(journal.IsBalanced);
        });
        Assert.Equal(
            journalEntries.Count,
            journalEntries.Select(journal => journal.IdempotencyKey).Distinct(StringComparer.Ordinal).Count());

        var expectedKey = BuildSupplierMutationIdempotencyKey(seeded.TenantId, originalActor.RequestId);
        Assert.Equal(expectedKey, idempotencyRecord.Key);
        Assert.Equal(replayActor.RequestId, originalActor.RequestId);
        Assert.Equal(PurchaseRequestIdempotencyStatus.Completed, idempotencyRecord.Status);
        Assert.Equal(invoice.Id, idempotencyRecord.PurchaseInvoiceId);
        Assert.Equal(originalActor.PurchaseAttemptId, idempotencyRecord.PurchaseAttemptId);
        Assert.Equal(StatusCodes.Status200OK, idempotencyRecord.ResponseStatusCode);
        Assert.NotNull(idempotencyRecord.CompletedAtUtc);
        Assert.Contains(invoice.Id.ToString("D"), idempotencyRecord.ResponseJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var original = results.Single(result => result.Actor.Name == originalActor.Name);
        var replay = results.Single(result => result.Actor.Name == replayActor.Name);

        Assert.True(original.Succeeded, original.PayloadJson);
        if (replay.Succeeded)
        {
            // Safe outcome A: PostgreSQL made Request B wait on the active same-key claim,
            // then Request B converged to the committed stored response without creating
            // a second supplier payable, stock increment, or accounting intent.
            Assert.Equal(original.Response!.InvoiceId, replay.Response!.InvoiceId);
            Assert.Equal(invoice.Id, replay.Response.InvoiceId);
            progressEntries.Enqueue("verification:pi-con-02-safe-outcome-a-replay-converged");
        }
        else
        {
            // Safe outcome B: Request B failed closed while Request A still owned the
            // authoritative in-flight claim. The durable state above proves no second
            // invoice, supplier mutation, stock movement, or outbox row survived.
            Assert.Contains(replay.StatusCode, new int?[]
            {
                StatusCodes.Status409Conflict,
                StatusCodes.Status500InternalServerError
            });
            progressEntries.Enqueue("verification:pi-con-02-safe-outcome-b-failed-closed");
        }

        await AssertReplaySafetyAsync(
            connectionString,
            tenantContext,
            seeded,
            original,
            progressEntries);
    }

    private static async Task<SeededScenario> SeedScenarioAsync(ApplicationDbContext context)
    {
        await EnsurePurchaseInvoiceNumberSequenceAsync(context);
        await EnsureReliabilityScenarioSchemaAsync(context);

        context.Tenants.Add(new Tenant
        {
            Id = TenantId,
            Name = "PI-CON-01 Tenant",
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
            Name = "PI-CON-01 Category",
            CreatedAt = SeededUtc
        });

        context.Suppliers.Add(new Supplier
        {
            Id = SupplierId,
            TenantId = TenantId,
            Name = "PI-CON-01 Supplier",
            Balance = 0m,
            CreatedAt = SeededUtc
        });

        context.Warehouses.Add(new Warehouse
        {
            Id = WarehouseId,
            TenantId = TenantId,
            Name = "PI-CON-01 Warehouse",
            Code = "WH-PI-CON-01",
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
            Name = "PI-CON-01 Product",
            CategoryId = CategoryId,
            PurchasePrice = AuthoritativeUnitCost,
            SalePrice = AuthoritativeUnitCost * 2m,
            OnlinePrice = AuthoritativeUnitCost * 2m,
            PriceIncludesTax = false,
            TaxRate = 0m,
            Unit = "Piece",
            IsActive = true,
            TrackStock = true,
            CreatedAt = SeededUtc
        });

        context.ProductVariants.Add(new ProductVariant
        {
            Id = VariantId,
            TenantId = TenantId,
            ProductId = ProductId,
            SKU = "PI-CON-01-SKU",
            Price = AuthoritativeUnitCost * 2m,
            OnlinePrice = AuthoritativeUnitCost * 2m,
            CostPrice = AuthoritativeUnitCost,
            IsDefault = true,
            IsActive = true,
            StockUpdatedAtUtc = SeededUtc,
            CreatedAt = SeededUtc
        });

        context.ProductUnits.Add(new ProductUnit
        {
            Id = ProductUnitId,
            ProductId = ProductId,
            UnitId = UnitId,
            ConversionFactor = 1m,
            SellingPrice = AuthoritativeUnitCost * 2m,
            PurchasePrice = AuthoritativeUnitCost,
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
            Quantity = InitialStockQuantity,
            ReservedQuantity = 0m,
            DamagedQuantity = 0m,
            CreatedAt = SeededUtc,
            UpdatedAt = SeededUtc
        });

        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlRawAsync("SELECT setval('purchase_invoice_seq', 1000, false);");

        return new SeededScenario(
            TenantId,
            SupplierId,
            WarehouseId,
            ProductId,
            VariantId,
            UnitId);
    }

    private static async Task EnsurePurchaseInvoiceNumberSequenceAsync(ApplicationDbContext context)
    {
        // PI-CON-01 validates contested purchase mutation semantics. Keep the document-number
        // sequence deterministic so the scenario cannot fail because of unrelated schema drift.
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE SEQUENCE IF NOT EXISTS "purchase_invoice_seq"
            START WITH 1000
            INCREMENT BY 1
            MINVALUE 1000
            NO MAXVALUE
            CACHE 1;
            ALTER SEQUENCE "purchase_invoice_seq" RESTART WITH 1000;
            """);
    }

    private static async Task EnsureReliabilityScenarioSchemaAsync(ApplicationDbContext context)
    {
        // Reuse the same reliability baseline hardening as the authoritative sales scenarios.
        // Historical migration chains can leave these columns/types behind the current model,
        // which would create false negatives unrelated to PI-CON-01 overlap behavior.
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

                ALTER TABLE "PurchaseInvoices"
                    ADD COLUMN IF NOT EXISTS "AccountingMutationVersion" integer NOT NULL DEFAULT 0;
            END
            $$;
            """);
    }

    private static async Task ConfigureSessionTimeoutsAsync(ApplicationDbContext context)
    {
        context.Database.SetCommandTimeout(TimeSpan.FromSeconds(30));
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("SET statement_timeout = '30s'; SET lock_timeout = '30s';");
    }

    private static string BuildSupplierMutationIdempotencyKey(Guid tenantId, string requestId) =>
        $"SUPPLIER:{tenantId}:{requestId}";

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

    private sealed record SeededScenario(
        Guid TenantId,
        Guid SupplierId,
        Guid WarehouseId,
        Guid ProductId,
        Guid VariantId,
        Guid UnitId);

    private sealed record PurchaseActor(
        string Name,
        string RequestId,
        string PurchaseAttemptId,
        string TraceIdentifier,
        Guid UserId,
        decimal Quantity);

    private sealed record PurchaseActorResult(
        PurchaseActor Actor,
        bool Succeeded,
        int? StatusCode,
        PurchaseResponse? Response,
        string? PayloadJson,
        TimeSpan Duration,
        Exception? Exception);

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

    private sealed record PiCon03StateSnapshot(
        int PurchaseInvoices,
        decimal StockQuantity,
        int StockMovements,
        int SupplierTransactions,
        int SupplierLedgers,
        decimal SupplierBalance,
        int AccountingOutboxMessages,
        Guid AccountingOutboxId,
        Guid? AccountingOutboxJournalId,
        DateTime? AccountingOutboxClaimedUntilUtc,
        DateTime? AccountingOutboxProcessedAtUtc,
        int StockSyncOutboxMessages,
        Guid StockSyncOutboxId,
        Guid StockSyncEventId,
        int StockSyncEventVersion,
        DateTime? StockSyncClaimedUntilUtc,
        DateTime? StockSyncPublishedAtUtc,
        int StockSyncAttemptCount,
        int ProcessedEvents,
        int InventoryEventInbox,
        int JournalEntries,
        string JournalFingerprint,
        InvoiceStatus InvoiceStatus,
        decimal InvoiceTotalAmount,
        decimal InvoiceRemainingAmount,
        decimal InvoicePaidAmount,
        int InvoiceMutationVersion);

    private sealed class PurchaseStockLockRendezvous
    {
        private readonly int _participantCount;
        private readonly TimeSpan _timeout;
        private readonly ConcurrentQueue<string> _progressEntries;
        private readonly TaskCompletionSource<bool> _allArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivalCount;

        public PurchaseStockLockRendezvous(
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
                    $"Timed out waiting for both PI-CON-01 actors to reach the contested stock lock. Progress: {string.Join(" | ", _progressEntries)}");
            }

            _progressEntries.Enqueue($"{actorName}:stock-lock-released");
        }
    }

    private sealed class PurchaseCommitOverlapWindow
    {
        private readonly TimeSpan _timeout;
        private readonly ConcurrentQueue<string> _progressEntries;
        private readonly TaskCompletionSource<bool> _originalCommitPaused =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseOriginalCommit =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _commitPauseClaimed;

        public PurchaseCommitOverlapWindow(
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

            _progressEntries.Enqueue($"{actorName}:commit-paused-before-durable-visibility");
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
                    $"Timed out waiting for the authoritative purchase actor to pause before commit. Progress: {string.Join(" | ", _progressEntries)}");
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
                    $"Timed out waiting to release the authoritative purchase commit. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public void ReleaseOriginalCommit()
        {
            _progressEntries.Enqueue("orchestrator:release-original-purchase-commit");
            _releaseOriginalCommit.TrySetResult(true);
        }
    }

    private sealed class PurchaseActiveTransactionReplayWindow
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

        public PurchaseActiveTransactionReplayWindow(
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

            _progressEntries.Enqueue($"{actorName}:same-key-purchase-claim-attempted-while-original-active");
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
                    $"Timed out waiting for Request A to pause before purchase commit. Progress: {string.Join(" | ", _progressEntries)}");
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
                    $"Timed out waiting for Request B to attempt the overlapping purchase idempotency claim. Progress: {string.Join(" | ", _progressEntries)}");
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
                    $"Timed out waiting to release Request A purchase commit. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public void ReleaseOriginalCommit()
        {
            _progressEntries.Enqueue("orchestrator:release-original-purchase-commit");
            _releaseOriginalCommit.TrySetResult(true);
        }
    }

    private sealed class ActiveProcessingDuplicateWorkerWindow
    {
        private readonly TimeSpan _timeout;
        private readonly ConcurrentQueue<string> _progressEntries;
        private readonly TaskCompletionSource<bool> _originalPaused =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseOriginal =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ActiveProcessingDuplicateWorkerWindow(
            TimeSpan timeout,
            ConcurrentQueue<string> progressEntries)
        {
            _timeout = timeout;
            _progressEntries = progressEntries;
        }

        public void MarkOriginalPaused(string workerName)
        {
            _progressEntries.Enqueue($"{workerName}:active-processing-paused-before-commit");
            _originalPaused.TrySetResult(true);
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
                    $"Timed out waiting for the original purchase worker to pause before commit. Progress: {string.Join(" | ", _progressEntries)}");
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
                    $"Timed out waiting to release the original purchase worker. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public void ReleaseOriginalWorker()
        {
            _progressEntries.Enqueue("orchestrator:release-original-purchase-worker");
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
                    $"Timed out waiting to release the first purchase stock-sync publish. Progress: {string.Join(" | ", _progressEntries)}");
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
                    $"Timed out waiting for the first purchase stock-sync publish attempt. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public void ReleaseFirstPublish()
        {
            _progressEntries.Enqueue("orchestrator:release-first-purchase-stock-sync-publish");
            _releaseFirstPublish.TrySetResult(true);
        }
    }

    private sealed class ProductVariantStockLockInterceptor : DbCommandInterceptor
    {
        private readonly string _actorName;
        private readonly PurchaseStockLockRendezvous _stockLockRendezvous;
        private readonly ConcurrentQueue<string> _progressEntries;
        private int _intercepted;

        public ProductVariantStockLockInterceptor(
            string actorName,
            PurchaseStockLockRendezvous stockLockRendezvous,
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

    private sealed class PurchaseCommitPauseTransactionInterceptor(
        string actorName,
        PurchaseCommitOverlapWindow overlapWindow) : DbTransactionInterceptor
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
                // PI-CON-01 critical window: invoice rows, stock movements, supplier ledger,
                // accounting outbox, stock-sync outbox, and idempotency completion have all
                // been written inside the transaction, but PostgreSQL has not committed them.
                if (overlapWindow.TryPauseOriginalCommit(actorName))
                {
                    await overlapWindow.WaitUntilCommitReleasedAsync(cancellationToken);
                }
            }

            return await base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }

    private sealed class PurchaseActiveReplayCommitPauseTransactionInterceptor(
        string actorName,
        PurchaseActiveTransactionReplayWindow replayWindow) : DbTransactionInterceptor
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
                // PI-CON-02 critical window: purchase invoice, stock increment, supplier payable,
                // ledger intent, outbox row, stock-sync outbox row, and idempotency completion
                // have been staged inside Request A's transaction but are still uncommitted.
                replayWindow.MarkOriginalCommitPaused(actorName);
                await replayWindow.WaitUntilCommitReleasedAsync(cancellationToken);
            }

            return await base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }

    private sealed class PurchaseReplayClaimAttemptInterceptor(
        string actorName,
        PurchaseActiveTransactionReplayWindow replayWindow) : DbCommandInterceptor
    {
        private int _intercepted;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (IsPurchaseIdempotencyClaimInsert(command.CommandText) &&
                Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0)
            {
                // Request B is attempting the same PurchaseRequestIdempotency claim while
                // Request A still owns the uncommitted PostgreSQL authority fence.
                replayWindow.MarkReplayClaimAttempted(actorName);
            }

            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private static bool IsPurchaseIdempotencyClaimInsert(string commandText)
        {
            return commandText.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase) &&
                   commandText.Contains("PurchaseRequestIdempotency", StringComparison.OrdinalIgnoreCase) &&
                   commandText.Contains("ON CONFLICT", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class PurchaseAccountingOutboxInvoiceLockPauseInterceptor(
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
            if (IsPurchaseInvoiceForUpdateQuery(command.CommandText) &&
                Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0)
            {
                // The outbox worker has already claimed the authoritative row. Pausing after
                // PostgreSQL executes the invoice FOR UPDATE keeps Worker A active and
                // uncommitted while Worker B receives the same logical delivery and must
                // respect the claimed-row duplicate fence instead of reposting supplier truth.
                window.MarkOriginalPaused(workerName);
                await window.WaitUntilReleasedAsync(cancellationToken);
            }

            return await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }

        private static bool IsPurchaseInvoiceForUpdateQuery(string commandText)
        {
            return commandText.Contains("FROM \"PurchaseInvoices\"", StringComparison.OrdinalIgnoreCase) &&
                   commandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class FixedCorrelationIdAccessor(string correlationId) : ICorrelationIdAccessor
    {
        public string GetCorrelationId()
        {
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
