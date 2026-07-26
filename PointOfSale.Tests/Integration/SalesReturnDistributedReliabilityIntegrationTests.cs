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
using PointOfSale.Core.Models.Refunds;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Eventing;
using PointOfSale.Infrastructure.Treasury;
using PointOfSale.Tests.Infrastructure;
using Xunit.Sdk;
using UnitEntity = PointOfSale.Core.Entities.Unit;

namespace PointOfSale.Tests.Integration;

public sealed class SalesReturnDistributedReliabilityIntegrationTests
{
    private static readonly DateTime SeededUtc = new(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ReturnRequestUtc = new(2026, 5, 14, 12, 30, 0, DateTimeKind.Utc);

    private static readonly Guid TenantId = Guid.Parse("aeb9b238-6d60-41be-98a7-7d388f9d1001");
    private static readonly Guid CategoryId = Guid.Parse("aeb9b238-6d60-41be-98a7-7d388f9d1002");
    private static readonly Guid CustomerId = Guid.Parse("aeb9b238-6d60-41be-98a7-7d388f9d1003");
    private static readonly Guid ProductId = Guid.Parse("aeb9b238-6d60-41be-98a7-7d388f9d1004");
    private static readonly Guid VariantId = Guid.Parse("aeb9b238-6d60-41be-98a7-7d388f9d1005");
    private static readonly Guid UnitId = Guid.Parse("aeb9b238-6d60-41be-98a7-7d388f9d1006");
    private static readonly Guid WarehouseId = Guid.Parse("aeb9b238-6d60-41be-98a7-7d388f9d1007");
    private static readonly Guid ProductUnitId = Guid.Parse("aeb9b238-6d60-41be-98a7-7d388f9d1008");
    private static readonly Guid VariantStockId = Guid.Parse("aeb9b238-6d60-41be-98a7-7d388f9d1009");
    private static readonly Guid InvoiceId = Guid.Parse("aeb9b238-6d60-41be-98a7-7d388f9d1010");
    private static readonly Guid InvoiceItemId = Guid.Parse("aeb9b238-6d60-41be-98a7-7d388f9d1011");
    private static readonly Guid SaleMovementId = Guid.Parse("aeb9b238-6d60-41be-98a7-7d388f9d1012");
    private static readonly Guid SaleLedgerId = Guid.Parse("aeb9b238-6d60-41be-98a7-7d388f9d1013");
    private static readonly Guid PaymentLedgerId = Guid.Parse("aeb9b238-6d60-41be-98a7-7d388f9d1014");
    private static readonly Guid SaleCustomerTransactionId = Guid.Parse("aeb9b238-6d60-41be-98a7-7d388f9d1015");
    private static readonly Guid PaymentCustomerTransactionId = Guid.Parse("aeb9b238-6d60-41be-98a7-7d388f9d1016");
    private static readonly Guid ActorAUserId = Guid.Parse("aeb9b238-6d60-41be-98a7-7d388f9d1017");
    private static readonly Guid ActorBUserId = Guid.Parse("aeb9b238-6d60-41be-98a7-7d388f9d1018");

    private const decimal OriginalSoldQuantity = 5m;
    private const decimal RequestedReturnQuantity = 4m;
    private const decimal UnitPrice = 10m;
    private const decimal VariantCostPrice = 6m;
    private const decimal PostSaleStockQuantity = 7m;

    [Fact]
    public async Task SR_CON_01_Concurrent_Sales_Return_Against_Same_Invoice_Converges_Safely()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out _, out _) ||
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
        var diagnosticLogs = progressEntries;
        var overlapWindow = new SalesReturnCommitOverlapWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);

        var actorA = new ReturnActor(
            Name: "actor-a-original",
            RequestId: "sr-con-01-actor-a",
            TraceIdentifier: "sr-con-01-original",
            UserId: ActorAUserId);
        var actorB = new ReturnActor(
            Name: "actor-b-conflict",
            RequestId: "sr-con-01-actor-b",
            TraceIdentifier: "sr-con-01-conflict",
            UserId: ActorBUserId);

        var originalTask = ExecuteReturnActorAsync(
            connectionString,
            tenantContext,
            seeded,
            actorA,
            progressEntries,
            diagnosticLogs,
            null,
            null,
            new SalesReturnCommitPauseInterceptor(actorA.Name, overlapWindow));

        await overlapWindow.WaitForOriginalCommitPauseAsync();
        await AssertNoUncommittedReturnStateVisibleAsync(scope, tenantContext, seeded, progressEntries);

        var conflictingTask = ExecuteReturnActorAsync(
            connectionString,
            tenantContext,
            seeded,
            actorB,
            progressEntries,
            diagnosticLogs,
            null,
            null,
            new SalesReturnInvoiceLockAttemptInterceptor(actorB.Name, overlapWindow));

        await overlapWindow.WaitForConflictingLockAttemptAsync();
        Assert.True(
            overlapWindow.OriginalCommitPausedBeforeConflictAttempt,
            $"The conflicting return did not attempt the invoice lock while the original transaction was still active. Progress: {string.Join(" | ", progressEntries)}");

        overlapWindow.ReleaseOriginalCommit();

        ReturnActorResult[] results;
        try
        {
            results = await Task.WhenAll(originalTask, conflictingTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"SR-CON-01 timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.All(results, result => Assert.Null(result.Exception));

        await AssertSrCon01ConvergenceAsync(
            scope,
            tenantContext,
            seeded,
            results,
            progressEntries);
    }

    [Fact]
    public async Task SR_CON_02_Replay_During_Active_Sales_Return_Transaction_Converges_Safely()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out _, out _) ||
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
        var diagnosticLogs = progressEntries;
        var overlapWindow = new SalesReturnCommitOverlapWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var sharedRequestId = "sr-con-02-active-replay";
        const string sharedReason = "SR-CON-02 replay overlap validation";
        const string sharedNotes = "sr-con-02-shared-payload";

        var originalActor = new ReturnActor(
            Name: "request-a-original",
            RequestId: sharedRequestId,
            TraceIdentifier: "sr-con-02-original-active",
            UserId: ActorAUserId);
        var replayActor = new ReturnActor(
            Name: "request-b-replay",
            RequestId: sharedRequestId,
            TraceIdentifier: "sr-con-02-overlapping-replay",
            UserId: ActorBUserId);

        var originalTask = ExecuteReturnActorAsync(
            connectionString,
            tenantContext,
            seeded,
            originalActor,
            progressEntries,
            diagnosticLogs,
            sharedReason,
            sharedNotes,
            new SalesReturnCommitPauseInterceptor(originalActor.Name, overlapWindow));

        await overlapWindow.WaitForOriginalCommitPauseAsync();
        await AssertNoUncommittedReturnStateVisibleAsync(scope, tenantContext, seeded, progressEntries);

        var replayTask = ExecuteReturnActorAsync(
            connectionString,
            tenantContext,
            seeded,
            replayActor,
            progressEntries,
            diagnosticLogs,
            sharedReason,
            sharedNotes,
            new SalesReturnReplayClaimAttemptInterceptor(replayActor.Name, overlapWindow));

        await overlapWindow.WaitForReplayClaimAttemptAsync();
        Assert.True(
            overlapWindow.OriginalCommitPausedBeforeReplayClaim,
            $"Replay did not overlap the active sales-return transaction window. Progress: {string.Join(" | ", progressEntries)}");

        overlapWindow.ReleaseOriginalCommit();

        ReturnActorResult[] results;
        try
        {
            results = await Task.WhenAll(originalTask, replayTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"SR-CON-02 timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.True(
            overlapWindow.OriginalCommitPaused,
            $"Request A never paused before commit. Progress: {string.Join(" | ", progressEntries)}");
        Assert.True(
            overlapWindow.ReplayClaimAttempted,
            $"Request B never attempted the overlapping sales-return idempotency claim. Progress: {string.Join(" | ", progressEntries)}");
        Assert.All(results, result => Assert.Null(result.Exception));

        await AssertSrCon02ConvergenceAsync(
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
    public async Task SR_CON_03_Duplicate_Sales_Return_Worker_Delivery_During_Active_Processing_Converges_Safely()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out _, out _) ||
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
        var diagnosticLogs = progressEntries;
        var canonicalActor = new ReturnActor(
            Name: "sr-con-03-authoritative-return",
            RequestId: "sr-con-03-authoritative-return",
            TraceIdentifier: "sr-con-03-authoritative-return",
            UserId: ActorAUserId);

        var createResult = await ExecuteReturnActorAsync(
            connectionString,
            tenantContext,
            seeded,
            canonicalActor,
            progressEntries,
            diagnosticLogs,
            "SR-CON-03 duplicate worker overlap validation",
            "sr-con-03-authoritative-return");

        Assert.Null(createResult.Exception);
        Assert.True(createResult.IsSuccess, createResult.ResponseBody ?? "Canonical SR-CON-03 sales return did not succeed.");
        Assert.NotNull(createResult.Response);

        var returnId = createResult.Response!.ReturnId;
        var canonicalBaseline = await LoadSrCon03StateSnapshotAsync(scope, tenantContext, seeded, returnId);

        Assert.Equal(1, canonicalBaseline.SalesReturnCount);
        Assert.Equal(RequestedReturnQuantity, canonicalBaseline.TotalReturnedBaseQuantity);
        Assert.Equal(PostSaleStockQuantity + RequestedReturnQuantity, canonicalBaseline.StockQuantity);
        Assert.Equal(1, canonicalBaseline.SalesReturnStockMovementCount);
        Assert.Equal(1, canonicalBaseline.SalesReturnCustomerTransactionCount);
        Assert.Equal(RequestedReturnQuantity * UnitPrice, canonicalBaseline.CustomerCreditTotal);
        Assert.Equal(-(RequestedReturnQuantity * UnitPrice), canonicalBaseline.CustomerBalance);
        Assert.Equal(1, canonicalBaseline.SalesReturnLedgerEntryCount);
        Assert.Equal(1, canonicalBaseline.RefundOutboxCount);
        Assert.NotEqual(Guid.Empty, canonicalBaseline.RefundOutboxId);
        Assert.NotNull(canonicalBaseline.RefundOutboxJournalId);
        Assert.Equal(1, canonicalBaseline.RefundOutboxAggregateVersion);
        Assert.Equal(1, canonicalBaseline.RefundOutboxPayloadMutationVersion);
        Assert.Equal(seeded.InvoiceId, canonicalBaseline.RefundOutboxPayloadOriginalInvoiceId);
        Assert.Null(canonicalBaseline.RefundOutboxClaimedUntilUtc);
        Assert.NotNull(canonicalBaseline.RefundOutboxProcessedAtUtc);
        Assert.Equal(1, canonicalBaseline.RefundJournalCount);
        Assert.Equal(1, canonicalBaseline.RefundCogsJournalCount);
        Assert.Equal(1, canonicalBaseline.StockSyncOutboxCount);
        Assert.NotEqual(Guid.Empty, canonicalBaseline.StockSyncOutboxId);
        Assert.NotEqual(Guid.Empty, canonicalBaseline.StockSyncEventId);
        Assert.True(canonicalBaseline.StockSyncEventVersion > 0);
        Assert.Equal(nameof(ProductVariantStockChangedEvent), canonicalBaseline.StockSyncEventType);
        Assert.Null(canonicalBaseline.StockSyncPublishedAtUtc);
        Assert.Equal(0, canonicalBaseline.ProcessedEvents);
        Assert.Equal(0, canonicalBaseline.InventoryEventInbox);
        Assert.Equal(1, canonicalBaseline.IdempotencyRecordCount);
        Assert.Equal(canonicalActor.RequestId, canonicalBaseline.IdempotencyKey);
        Assert.Equal(StatusCodes.Status200OK, canonicalBaseline.IdempotencyResponseStatusCode);
        Assert.NotNull(canonicalBaseline.IdempotencyCompletedAtUtc);
        Assert.Equal(1, canonicalBaseline.InvoiceMutationVersion);
        Assert.Equal(OriginalSoldQuantity - RequestedReturnQuantity, canonicalBaseline.RemainingReturnableQuantity);

        // Reopen the same authoritative refund outbox row so the real AccountingOutboxProcessor
        // replays the persisted refund payload, event type, idempotency key, mutation version,
        // and journal affinity. This exercises duplicate redelivery against the production worker
        // instead of fabricating a second return or a synthetic approximation.
        await ReopenRefundAccountingOutboxMessageForWorkerReplayAsync(
            scope,
            tenantContext,
            seeded,
            returnId,
            progressEntries);
        var reopenedAccountingBaseline = await LoadSrCon03StateSnapshotAsync(scope, tenantContext, seeded, returnId);
        Assert.Null(reopenedAccountingBaseline.RefundOutboxJournalId);
        Assert.Null(reopenedAccountingBaseline.RefundOutboxProcessedAtUtc);

        var accountingWindow = new ActiveProcessingDuplicateWorkerWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var accountingWorkerA = new AccountingReplayWorker(
            Name: "sales-return-accounting-worker-a",
            UserId: ActorAUserId);
        var accountingWorkerB = new AccountingReplayWorker(
            Name: "sales-return-accounting-worker-b",
            UserId: ActorBUserId);

        using var accountingWorkerAServices = BuildAccountingOutboxProcessorServiceProvider(
            connectionString,
            tenantContext,
            new SalesReturnAccountingOutboxReturnLockPauseInterceptor(accountingWorkerA.Name, accountingWindow));
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
        await AssertSalesReturnAccountingWorkerClaimVisibleDuringActiveProcessingAsync(
            scope,
            tenantContext,
            seeded,
            returnId,
            reopenedAccountingBaseline,
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
                $"SR-CON-03 refund duplicate worker did not complete while the original worker held the return lock. Progress: {string.Join(" | ", progressEntries)}");
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
                $"SR-CON-03 refund worker overlap timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        await AssertSalesReturnAccountingDuplicateWorkerConvergenceAsync(
            scope,
            tenantContext,
            seeded,
            returnId,
            canonicalBaseline,
            originalAccountingWorkerResult,
            duplicateAccountingWorkerResult,
            progressEntries);

        var postAccountingBaseline = await LoadSrCon03StateSnapshotAsync(scope, tenantContext, seeded, returnId);
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
            "sales-return-stock-sync-worker-a",
            progressEntries);

        await stockSyncPublishWindow.WaitForFirstPublishAsync();
        await AssertSalesReturnStockSyncClaimVisibleDuringActivePublishAsync(
            scope,
            tenantContext,
            seeded,
            returnId,
            postAccountingBaseline,
            progressEntries);

        var stockSyncWorkerBTask = ExecuteStockSyncWorkerAsync(
            stockSyncProcessor,
            "sales-return-stock-sync-worker-b",
            progressEntries);

        StockSyncWorkerResult duplicateStockSyncWorkerResult;
        try
        {
            duplicateStockSyncWorkerResult = await stockSyncWorkerBTask.WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"SR-CON-03 stock-sync duplicate worker did not complete while the original publish was paused. Progress: {string.Join(" | ", progressEntries)}");
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
                $"SR-CON-03 stock-sync original worker did not finish after publish release. Progress: {string.Join(" | ", progressEntries)}");
        }

        await AssertSalesReturnStockSyncDuplicateWorkerConvergenceAsync(
            scope,
            tenantContext,
            seeded,
            returnId,
            postAccountingBaseline,
            originalStockSyncWorkerResult,
            duplicateStockSyncWorkerResult,
            stockSyncPublishWindow,
            progressEntries);
    }

    private static async Task<ReturnActorResult> ExecuteReturnActorAsync(
        string connectionString,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        ReturnActor actor,
        ConcurrentQueue<string> progressEntries,
        ConcurrentQueue<string> diagnosticLogs,
        string? requestReason = null,
        string? requestNotes = null,
        params IInterceptor[] interceptors)
    {
        await using var context = CreateDbContext(connectionString, tenantContext, interceptors);
        await ConfigureSessionTimeoutsAsync(context);

        var controller = BuildSalesReturnsController(context, tenantContext, actor, diagnosticLogs);
        controller.Request.Headers["Idempotency-Key"] = actor.RequestId;
        controller.Request.Headers["User-Agent"] = actor.TraceIdentifier;

        var request = new CreateSalesReturnRequest
        {
            CustomerId = seeded.CustomerId,
            WarehouseId = seeded.WarehouseId,
            ReturnDate = ReturnRequestUtc,
            OriginalInvoiceId = seeded.InvoiceId,
            RefundMethod = RefundMethod.AccountCredit,
            CashAmount = 0m,
            CardAmount = 0m,
            Reason = requestReason ?? "SR-CON-01 overlap validation",
            Notes = requestNotes ?? actor.Name,
            Items =
            [
                new SalesReturnItemRequest
                {
                    ProductId = seeded.ProductId,
                    ProductVariantId = seeded.VariantId,
                    UnitId = seeded.UnitId,
                    Quantity = RequestedReturnQuantity,
                    UnitPrice = UnitPrice,
                    TaxRate = 0m,
                    Reason = "Concurrent sales return against same invoice line"
                }
            ]
        };

        progressEntries.Enqueue($"{actor.Name}:request-ready");

        try
        {
            var actionResult = await controller.CreateSalesReturn(request);

            if (actionResult.Result is OkObjectResult okResult && okResult.Value is SalesReturnResponse okResponse)
            {
                progressEntries.Enqueue($"{actor.Name}:committed:{okResponse.ReturnId}");
                return ReturnActorResult.Success(actor, okResponse);
            }

            if (actionResult.Result is ObjectResult successResult &&
                (successResult.StatusCode is null or StatusCodes.Status200OK) &&
                TryReadSalesReturnResponse(successResult.Value, out var replayResponse))
            {
                progressEntries.Enqueue($"{actor.Name}:committed:{replayResponse!.ReturnId}");
                return ReturnActorResult.Success(actor, replayResponse);
            }

            if (actionResult.Result is BadRequestObjectResult badRequest)
            {
                var payload = JsonSerializer.Serialize(badRequest.Value);
                progressEntries.Enqueue($"{actor.Name}:failed-closed");
                return ReturnActorResult.Failed(actor, StatusCodes.Status400BadRequest, payload);
            }

            if (actionResult.Result is ObjectResult objectResult)
            {
                var payload = JsonSerializer.Serialize(objectResult.Value);
                progressEntries.Enqueue($"{actor.Name}:unexpected-status:{objectResult.StatusCode}");
                return ReturnActorResult.Failed(actor, objectResult.StatusCode ?? StatusCodes.Status500InternalServerError, payload);
            }

            throw new XunitException(
                $"Unexpected ActionResult shape for {actor.Name}. ResultType={actionResult.Result?.GetType().Name ?? "<null>"}.");
        }
        catch (Exception ex)
        {
            progressEntries.Enqueue($"{actor.Name}:exception:{DescribeException(ex)}");
            return ReturnActorResult.Exceptional(actor, ex);
        }
    }

    private static SalesReturnsController BuildSalesReturnsController(
        ApplicationDbContext context,
        TestTenantContext tenantContext,
        ReturnActor actor,
        ConcurrentQueue<string> diagnosticLogs)
    {
        var controllerLogger = new TestLogger<SalesReturnsController>(diagnosticLogs);
        var ledgerService = new LedgerService(
            context,
            tenantContext,
            NullLogger<LedgerService>.Instance,
            new NoOpAuditService());
        var accountResolver = new AccountResolver(context, NullLogger<AccountResolver>.Instance);
        var postingService = new FinancialPostingService(context, NullLogger<FinancialPostingService>.Instance);
        var cashboxLedgerAccountService = new CashboxLedgerAccountService(context, NullLogger<CashboxLedgerAccountService>.Instance);
        var commerceAccountingService = new CommerceAccountingService(
            postingService,
            accountResolver,
            cashboxLedgerAccountService,
            NullLogger<CommerceAccountingService>.Instance);
        var accountingOutboxService = new AccountingOutboxService(
            context,
            new FixedCorrelationIdAccessor(actor.TraceIdentifier),
            NullLogger<AccountingOutboxService>.Instance);

        var controller = new SalesReturnsController(
            context,
            tenantContext,
            controllerLogger,
            ledgerService,
            new NoOpEventPublisher(),
            accountingOutboxService,
            commerceAccountingService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext(actor.UserId, actor.TraceIdentifier)
            }
        };

        return controller;
    }

    private static async Task AssertNoUncommittedReturnStateVisibleAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        ConcurrentQueue<string> progressEntries)
    {
        var snapshot = await LoadSrCon01StateSnapshotAsync(scope, tenantContext, seeded, committedReturnId: null);

        Assert.Equal(0, snapshot.SalesReturnCount);
        Assert.Equal(0m, snapshot.TotalReturnedBaseQuantity);
        Assert.Equal(PostSaleStockQuantity, snapshot.StockQuantity);
        Assert.Equal(0, snapshot.SalesReturnStockMovementCount);
        Assert.Equal(0, snapshot.SalesReturnCustomerTransactionCount);
        Assert.Equal(0, snapshot.SalesReturnLedgerEntryCount);
        Assert.Equal(0, snapshot.RefundOutboxCount);
        Assert.Equal(0, snapshot.RefundJournalCount);
        Assert.Equal(0, snapshot.RefundCogsJournalCount);
        Assert.Equal(0m, snapshot.CustomerCreditTotal);
        Assert.Equal(0m, snapshot.CustomerBalance);
        Assert.Equal(0, snapshot.IdempotencyRecordCount);
        Assert.Equal(0, snapshot.InvoiceMutationVersion);
        Assert.Equal(OriginalSoldQuantity, snapshot.RemainingReturnableQuantity);

        progressEntries.Enqueue("verification:no-uncommitted-return-state-visible");
    }

    private static async Task AssertSrCon01ConvergenceAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        IReadOnlyList<ReturnActorResult> results,
        ConcurrentQueue<string> progressEntries)
    {
        var committed = results.Single(result => result.IsSuccess);
        var rejected = results.Single(result => !result.IsSuccess);

        Assert.Equal(StatusCodes.Status200OK, committed.StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, rejected.StatusCode);
        Assert.NotNull(committed.Response);
        Assert.Contains(
            "Cannot return more than sold quantity",
            rejected.ResponseBody ?? string.Empty,
            StringComparison.Ordinal);

        var snapshot = await LoadSrCon01StateSnapshotAsync(
            scope,
            tenantContext,
            seeded,
            committed.Response!.ReturnId);

        Assert.Equal(1, snapshot.SalesReturnCount);
        Assert.Equal(RequestedReturnQuantity, snapshot.TotalReturnedBaseQuantity);
        Assert.True(snapshot.TotalReturnedBaseQuantity <= OriginalSoldQuantity);
        Assert.Equal(PostSaleStockQuantity + RequestedReturnQuantity, snapshot.StockQuantity);
        Assert.Equal(1, snapshot.SalesReturnStockMovementCount);
        Assert.Equal(PostSaleStockQuantity + RequestedReturnQuantity, snapshot.StockMovementQuantityAfter);
        Assert.Equal(RequestedReturnQuantity, snapshot.StockMovementQuantityChange);
        Assert.Equal(1, snapshot.SalesReturnCustomerTransactionCount);
        Assert.Equal(RequestedReturnQuantity * UnitPrice, snapshot.CustomerCreditTotal);
        Assert.Equal(-(RequestedReturnQuantity * UnitPrice), snapshot.CustomerBalance);
        Assert.Equal(1, snapshot.SalesReturnLedgerEntryCount);
        Assert.Equal(3, snapshot.TotalCustomerLedgerEntries);
        Assert.Equal(3, snapshot.TotalCustomerTransactions);
        Assert.Equal(1, snapshot.RefundOutboxCount);
        Assert.Equal(1, snapshot.RefundOutboxAggregateVersion);
        Assert.Equal(1, snapshot.RefundOutboxPayloadMutationVersion);
        Assert.Equal(seeded.InvoiceId, snapshot.RefundOutboxPayloadOriginalInvoiceId);
        Assert.NotNull(snapshot.RefundOutboxProcessedAtUtc);
        Assert.NotNull(snapshot.RefundOutboxJournalId);
        Assert.Equal(1, snapshot.InvoiceMutationVersion);
        Assert.Equal(OriginalSoldQuantity - RequestedReturnQuantity, snapshot.RemainingReturnableQuantity);
        Assert.Equal(1, snapshot.RefundJournalCount);
        Assert.Equal(1, snapshot.RefundCogsJournalCount);
        Assert.Equal(1, snapshot.StockSyncOutboxCount);
        Assert.Equal(nameof(ProductVariantStockChangedEvent), snapshot.StockSyncEventType);
        Assert.Equal(1, snapshot.IdempotencyRecordCount);
        Assert.Equal(committed.Actor.RequestId, snapshot.IdempotencyKey);
        Assert.Equal(StatusCodes.Status200OK, snapshot.IdempotencyResponseStatusCode);
        Assert.NotNull(snapshot.IdempotencyCompletedAtUtc);
        Assert.Contains(
            committed.Response!.ReturnId.ToString("D"),
            snapshot.IdempotencyResponseJson ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

        progressEntries.Enqueue("verification:sr-con-01-safe-outcome-a-converged");
    }

    private static async Task AssertSrCon02ConvergenceAsync(
        TestDatabaseScope scope,
        string connectionString,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        ReturnActor originalActor,
        ReturnActor replayActor,
        IReadOnlyList<ReturnActorResult> results,
        ConcurrentQueue<string> progressEntries)
    {
        var original = results.Single(result => result.Actor.Name == originalActor.Name);
        var replay = results.Single(result => result.Actor.Name == replayActor.Name);

        Assert.True(original.IsSuccess, original.ResponseBody);
        Assert.NotNull(original.Response);

        var snapshot = await LoadSrCon01StateSnapshotAsync(
            scope,
            tenantContext,
            seeded,
            original.Response!.ReturnId);

        Assert.Equal(1, snapshot.SalesReturnCount);
        Assert.Equal(RequestedReturnQuantity, snapshot.TotalReturnedBaseQuantity);
        Assert.True(snapshot.TotalReturnedBaseQuantity <= OriginalSoldQuantity);
        Assert.Equal(PostSaleStockQuantity + RequestedReturnQuantity, snapshot.StockQuantity);
        Assert.Equal(1, snapshot.SalesReturnStockMovementCount);
        Assert.Equal(PostSaleStockQuantity + RequestedReturnQuantity, snapshot.StockMovementQuantityAfter);
        Assert.Equal(RequestedReturnQuantity, snapshot.StockMovementQuantityChange);
        Assert.Equal(1, snapshot.SalesReturnCustomerTransactionCount);
        Assert.Equal(RequestedReturnQuantity * UnitPrice, snapshot.CustomerCreditTotal);
        Assert.Equal(-(RequestedReturnQuantity * UnitPrice), snapshot.CustomerBalance);
        Assert.Equal(1, snapshot.SalesReturnLedgerEntryCount);
        Assert.Equal(3, snapshot.TotalCustomerLedgerEntries);
        Assert.Equal(3, snapshot.TotalCustomerTransactions);
        Assert.Equal(1, snapshot.RefundOutboxCount);
        Assert.Equal(1, snapshot.RefundOutboxAggregateVersion);
        Assert.Equal(1, snapshot.RefundOutboxPayloadMutationVersion);
        Assert.Equal(seeded.InvoiceId, snapshot.RefundOutboxPayloadOriginalInvoiceId);
        Assert.NotNull(snapshot.RefundOutboxProcessedAtUtc);
        Assert.NotNull(snapshot.RefundOutboxJournalId);
        Assert.Equal(1, snapshot.InvoiceMutationVersion);
        Assert.Equal(OriginalSoldQuantity - RequestedReturnQuantity, snapshot.RemainingReturnableQuantity);
        Assert.Equal(1, snapshot.RefundJournalCount);
        Assert.Equal(1, snapshot.RefundCogsJournalCount);
        Assert.Equal(1, snapshot.StockSyncOutboxCount);
        Assert.Equal(nameof(ProductVariantStockChangedEvent), snapshot.StockSyncEventType);
        Assert.Equal(1, snapshot.IdempotencyRecordCount);
        Assert.Equal(originalActor.RequestId, snapshot.IdempotencyKey);
        Assert.Equal(replayActor.RequestId, snapshot.IdempotencyKey);
        Assert.Equal(StatusCodes.Status200OK, snapshot.IdempotencyResponseStatusCode);
        Assert.NotNull(snapshot.IdempotencyCompletedAtUtc);
        Assert.Contains(
            original.Response.ReturnId.ToString("D"),
            snapshot.IdempotencyResponseJson ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

        if (replay.IsSuccess)
        {
            // Safe outcome A: PostgreSQL held the overlapping same-key replay behind the
            // authoritative in-flight idempotency row, then returned the committed response.
            Assert.NotNull(replay.Response);
            Assert.Equal(original.Response.ReturnId, replay.Response!.ReturnId);
            Assert.Equal(original.Response.ReturnNumber, replay.Response.ReturnNumber);
            progressEntries.Enqueue("verification:sr-con-02-safe-outcome-a-replay-converged");
        }
        else
        {
            // Safe outcome B: replay failed closed while the original owned the in-flight
            // claim. The durable-state assertions above prove no duplicate reversal effects survived.
            Assert.Contains(replay.StatusCode, new[] { StatusCodes.Status409Conflict, StatusCodes.Status500InternalServerError });
            progressEntries.Enqueue("verification:sr-con-02-safe-outcome-b-failed-closed");
        }

        await AssertSalesReturnReplaySafetyAsync(
            connectionString,
            tenantContext,
            seeded,
            original,
            progressEntries);
    }

    private static async Task AssertSalesReturnReplaySafetyAsync(
        string connectionString,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        ReturnActorResult original,
        ConcurrentQueue<string> progressEntries)
    {
        await using var replayContext = CreateDbContext(connectionString, tenantContext);
        await ConfigureSessionTimeoutsAsync(replayContext);

        var replayActor = new ReturnActor(
            Name: "post-commit-replay",
            RequestId: original.Actor.RequestId,
            TraceIdentifier: "sr-con-02-post-commit-replay",
            UserId: ActorAUserId);
        var controller = BuildSalesReturnsController(replayContext, tenantContext, replayActor, progressEntries);
        controller.Request.Headers["Idempotency-Key"] = original.Actor.RequestId;
        controller.Request.Headers["User-Agent"] = replayActor.TraceIdentifier;

        var replayResult = await controller.CreateSalesReturn(new CreateSalesReturnRequest
        {
            CustomerId = seeded.CustomerId,
            WarehouseId = seeded.WarehouseId,
            ReturnDate = ReturnRequestUtc,
            OriginalInvoiceId = seeded.InvoiceId,
            RefundMethod = RefundMethod.AccountCredit,
            CashAmount = 0m,
            CardAmount = 0m,
            Reason = "SR-CON-02 replay overlap validation",
            Notes = "sr-con-02-shared-payload",
            Items =
            [
                new SalesReturnItemRequest
                {
                    ProductId = seeded.ProductId,
                    ProductVariantId = seeded.VariantId,
                    UnitId = seeded.UnitId,
                    Quantity = RequestedReturnQuantity,
                    UnitPrice = UnitPrice,
                    TaxRate = 0m,
                    Reason = "Concurrent sales return against same invoice line"
                }
            ]
        });

        Assert.True(
            replayResult.Result is ObjectResult or OkObjectResult,
            $"Unexpected post-commit replay result type: {replayResult.Result?.GetType().Name ?? "<null>"}");

        var objectResult = replayResult.Result as ObjectResult;
        Assert.NotNull(objectResult);
        Assert.True(
            objectResult.StatusCode is null or StatusCodes.Status200OK,
            $"Expected post-commit replay to return 200, got {objectResult.StatusCode}");
        Assert.True(
            TryReadSalesReturnResponse(objectResult.Value, out var replayResponse),
            "Post-commit replay did not return a readable SalesReturnResponse payload.");
        Assert.NotNull(replayResponse);
        Assert.Equal(original.Response!.ReturnId, replayResponse!.ReturnId);
        Assert.Equal(original.Response.ReturnNumber, replayResponse.ReturnNumber);

        progressEntries.Enqueue("verification:post-commit-replay-returned-authoritative-result");
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

    private static async Task<SrCon03StateSnapshot> LoadSrCon03StateSnapshotAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid returnId)
    {
        await using var context = scope.CreateDbContext(tenantContext);

        var salesReturns = await context.Returns
            .Where(item =>
                item.TenantId == seeded.TenantId &&
                item.ReturnType == ReturnType.SalesReturn &&
                item.OriginalInvoiceId == seeded.InvoiceId)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync();
        var salesReturnIds = salesReturns.Select(item => item.Id).ToList();
        var returnItems = await context.ReturnItems
            .Where(item => salesReturnIds.Contains(item.ReturnId))
            .ToListAsync();
        var stockMovements = await context.StockMovements
            .Where(movement =>
                movement.TenantId == seeded.TenantId &&
                movement.MovementType == StockMovementType.SalesReturn &&
                movement.ReferenceId.HasValue &&
                salesReturnIds.Contains(movement.ReferenceId.Value))
            .OrderBy(movement => movement.CreatedAt)
            .ThenBy(movement => movement.Id)
            .ToListAsync();
        var customerTransactions = await context.CustomerTransactions
            .Where(transaction =>
                transaction.TenantId == seeded.TenantId &&
                transaction.CustomerId == seeded.CustomerId)
            .OrderBy(transaction => transaction.TransactionDate)
            .ThenBy(transaction => transaction.CreatedAt)
            .ThenBy(transaction => transaction.Id)
            .ToListAsync();
        var customerLedgers = await context.AccountLedgers
            .Where(entry =>
                entry.TenantId == seeded.TenantId &&
                entry.PartyType == PartyType.Customer &&
                entry.PartyId == seeded.CustomerId)
            .OrderBy(entry => entry.TransactionDate)
            .ThenBy(entry => entry.CreatedAt)
            .ThenBy(entry => entry.Id)
            .ToListAsync();
        var refundOutboxMessages = await context.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.EventType == "PosRefund" &&
                salesReturnIds.Contains(message.BusinessEntityId))
            .OrderBy(message => message.CreatedAt)
            .ToListAsync();
        var journalEntries = await context.JournalEntries
            .IgnoreQueryFilters()
            .Where(journal =>
                journal.TenantId == seeded.TenantId &&
                journal.SourceId == returnId)
            .OrderBy(journal => journal.CreatedAt)
            .ThenBy(journal => journal.Id)
            .ToListAsync();
        var stockSyncOutboxMessages = await context.StockSyncOutboxMessages
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.VariantId == seeded.VariantId &&
                message.WarehouseId == seeded.WarehouseId)
            .OrderBy(message => message.CreatedAt)
            .ToListAsync();
        var idempotencyRecords = await context.SalesRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .Where(record =>
                record.TenantId == seeded.TenantId &&
                record.Operation == "SALES_RETURN_CREATE")
            .OrderBy(record => record.CreatedAt)
            .ToListAsync();
        var customer = await context.Customers
            .SingleAsync(item => item.TenantId == seeded.TenantId && item.Id == seeded.CustomerId);
        var invoice = await context.SalesInvoices
            .SingleAsync(item => item.TenantId == seeded.TenantId && item.Id == seeded.InvoiceId);
        var stock = await context.ProductVariantStocks
            .SingleAsync(item =>
                item.TenantId == seeded.TenantId &&
                item.WarehouseId == seeded.WarehouseId &&
                item.VariantId == seeded.VariantId);

        var refundOutbox = refundOutboxMessages.SingleOrDefault();
        var stockSyncOutbox = stockSyncOutboxMessages.SingleOrDefault();
        var idempotencyRecord = idempotencyRecords.SingleOrDefault();
        Guid? originalInvoiceId = null;
        int? payloadMutationVersion = null;

        if (refundOutbox != null)
        {
            using var payload = JsonDocument.Parse(refundOutbox.PayloadJson);
            if (payload.RootElement.TryGetProperty("OriginalInvoiceId", out var originalInvoiceIdElement))
            {
                originalInvoiceId = originalInvoiceIdElement.GetGuid();
            }

            if (payload.RootElement.TryGetProperty("MutationVersion", out var mutationVersionElement) &&
                mutationVersionElement.ValueKind == JsonValueKind.Number)
            {
                payloadMutationVersion = mutationVersionElement.GetInt32();
            }
        }

        return new SrCon03StateSnapshot(
            SalesReturnCount: salesReturns.Count,
            TotalReturnedBaseQuantity: returnItems.Sum(item => item.BaseQuantity),
            StockQuantity: stock.Quantity,
            SalesReturnStockMovementCount: stockMovements.Count,
            SalesReturnCustomerTransactionCount: customerTransactions.Count(item => item.TransactionType == TransactionType.SalesReturn),
            TotalCustomerTransactions: customerTransactions.Count,
            CustomerCreditTotal: customerTransactions
                .Where(item => item.TransactionType == TransactionType.SalesReturn)
                .Sum(item => item.CreditAmount),
            CustomerBalance: customer.Balance,
            SalesReturnLedgerEntryCount: customerLedgers.Count(item => item.TransactionType == TransactionType.SalesReturn),
            TotalCustomerLedgerEntries: customerLedgers.Count,
            RefundOutboxCount: refundOutboxMessages.Count,
            RefundOutboxId: refundOutbox?.Id ?? Guid.Empty,
            RefundOutboxJournalId: refundOutbox?.JournalId,
            RefundOutboxClaimedUntilUtc: refundOutbox?.ClaimedUntilUtc,
            RefundOutboxProcessedAtUtc: refundOutbox?.ProcessedAtUtc,
            RefundOutboxAggregateVersion: refundOutbox?.AggregateVersion ?? 0,
            RefundOutboxPayloadMutationVersion: payloadMutationVersion ?? 0,
            RefundOutboxPayloadOriginalInvoiceId: originalInvoiceId,
            RefundJournalCount: journalEntries.Count(item => item.SourceType == "Return"),
            RefundCogsJournalCount: journalEntries.Count(item => item.SourceType == "Return_COGS"),
            RefundJournalFingerprint: string.Join(
                "|",
                journalEntries
                    .Select(journal => $"{journal.Id}:{journal.SourceType}:{journal.IdempotencyKey}")
                    .OrderBy(value => value, StringComparer.Ordinal)),
            IdempotencyRecordCount: idempotencyRecords.Count,
            IdempotencyKey: idempotencyRecord?.Key,
            IdempotencyResponseStatusCode: idempotencyRecord?.ResponseStatusCode,
            IdempotencyCompletedAtUtc: idempotencyRecord?.CompletedAtUtc,
            InvoiceMutationVersion: invoice.AccountingMutationVersion,
            RemainingReturnableQuantity: OriginalSoldQuantity - returnItems.Sum(item => item.BaseQuantity),
            StockSyncOutboxCount: stockSyncOutboxMessages.Count,
            StockSyncOutboxId: stockSyncOutbox?.Id ?? Guid.Empty,
            StockSyncEventId: stockSyncOutbox?.EventId ?? Guid.Empty,
            StockSyncEventVersion: stockSyncOutbox?.EventVersion ?? 0,
            StockSyncClaimedUntilUtc: stockSyncOutbox?.ClaimedUntilUtc,
            StockSyncPublishedAtUtc: stockSyncOutbox?.PublishedAtUtc,
            StockSyncAttemptCount: stockSyncOutbox?.AttemptCount ?? 0,
            StockSyncEventType: stockSyncOutbox?.EventType,
            ProcessedEvents: await context.Set<ProcessedEvent>()
                .IgnoreQueryFilters()
                .CountAsync(processed => processed.TenantId == seeded.TenantId),
            InventoryEventInbox: await context.InventoryEventInbox
                .IgnoreQueryFilters()
                .CountAsync(inbox => inbox.TenantId == seeded.TenantId));
    }

    private static async Task ReopenRefundAccountingOutboxMessageForWorkerReplayAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid returnId,
        ConcurrentQueue<string> progressEntries)
    {
        await using var context = scope.CreateDbContext(tenantContext);

        var outboxMessage = await context.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .SingleAsync(message =>
                message.TenantId == seeded.TenantId &&
                message.EventType == "PosRefund" &&
                message.BusinessEntityId == returnId);

        outboxMessage.ProcessedAtUtc = null;
        outboxMessage.JournalId = null;
        outboxMessage.ClaimedUntilUtc = null;
        outboxMessage.IsFailed = false;
        outboxMessage.LastError = null;
        outboxMessage.NextRetryAtUtc = null;
        outboxMessage.AttemptCount = 0;

        await context.SaveChangesAsync();
        progressEntries.Enqueue("setup:reopened-sales-return-refund-outbox-row-for-real-worker-overlap");
    }

    private static async Task AssertSalesReturnAccountingWorkerClaimVisibleDuringActiveProcessingAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid returnId,
        SrCon03StateSnapshot canonicalBaseline,
        ConcurrentQueue<string> progressEntries)
    {
        var visibleState = await LoadSrCon03StateSnapshotAsync(scope, tenantContext, seeded, returnId);

        Assert.Equal(canonicalBaseline.SalesReturnCount, visibleState.SalesReturnCount);
        Assert.Equal(canonicalBaseline.TotalReturnedBaseQuantity, visibleState.TotalReturnedBaseQuantity);
        Assert.Equal(canonicalBaseline.StockQuantity, visibleState.StockQuantity);
        Assert.Equal(canonicalBaseline.SalesReturnStockMovementCount, visibleState.SalesReturnStockMovementCount);
        Assert.Equal(canonicalBaseline.SalesReturnCustomerTransactionCount, visibleState.SalesReturnCustomerTransactionCount);
        Assert.Equal(canonicalBaseline.TotalCustomerTransactions, visibleState.TotalCustomerTransactions);
        Assert.Equal(canonicalBaseline.CustomerCreditTotal, visibleState.CustomerCreditTotal);
        Assert.Equal(canonicalBaseline.CustomerBalance, visibleState.CustomerBalance);
        Assert.Equal(canonicalBaseline.SalesReturnLedgerEntryCount, visibleState.SalesReturnLedgerEntryCount);
        Assert.Equal(canonicalBaseline.TotalCustomerLedgerEntries, visibleState.TotalCustomerLedgerEntries);
        Assert.Equal(canonicalBaseline.RefundOutboxCount, visibleState.RefundOutboxCount);
        Assert.Equal(canonicalBaseline.RefundOutboxId, visibleState.RefundOutboxId);
        Assert.Equal(canonicalBaseline.RefundOutboxJournalId, visibleState.RefundOutboxJournalId);
        Assert.Null(visibleState.RefundOutboxProcessedAtUtc);
        Assert.NotNull(visibleState.RefundOutboxClaimedUntilUtc);
        Assert.Equal(canonicalBaseline.RefundOutboxAggregateVersion, visibleState.RefundOutboxAggregateVersion);
        Assert.Equal(canonicalBaseline.RefundOutboxPayloadMutationVersion, visibleState.RefundOutboxPayloadMutationVersion);
        Assert.Equal(canonicalBaseline.RefundOutboxPayloadOriginalInvoiceId, visibleState.RefundOutboxPayloadOriginalInvoiceId);
        Assert.Equal(canonicalBaseline.RefundJournalCount, visibleState.RefundJournalCount);
        Assert.Equal(canonicalBaseline.RefundCogsJournalCount, visibleState.RefundCogsJournalCount);
        Assert.Equal(canonicalBaseline.RefundJournalFingerprint, visibleState.RefundJournalFingerprint);
        Assert.Equal(canonicalBaseline.IdempotencyRecordCount, visibleState.IdempotencyRecordCount);
        Assert.Equal(canonicalBaseline.IdempotencyKey, visibleState.IdempotencyKey);
        Assert.Equal(canonicalBaseline.IdempotencyResponseStatusCode, visibleState.IdempotencyResponseStatusCode);
        Assert.Equal(canonicalBaseline.IdempotencyCompletedAtUtc, visibleState.IdempotencyCompletedAtUtc);
        Assert.Equal(canonicalBaseline.InvoiceMutationVersion, visibleState.InvoiceMutationVersion);
        Assert.Equal(canonicalBaseline.RemainingReturnableQuantity, visibleState.RemainingReturnableQuantity);
        Assert.Equal(canonicalBaseline.StockSyncOutboxCount, visibleState.StockSyncOutboxCount);
        Assert.Equal(canonicalBaseline.StockSyncOutboxId, visibleState.StockSyncOutboxId);
        Assert.Equal(canonicalBaseline.StockSyncEventId, visibleState.StockSyncEventId);
        Assert.Equal(canonicalBaseline.StockSyncEventVersion, visibleState.StockSyncEventVersion);
        Assert.Equal(canonicalBaseline.StockSyncPublishedAtUtc, visibleState.StockSyncPublishedAtUtc);
        Assert.Equal(canonicalBaseline.StockSyncEventType, visibleState.StockSyncEventType);
        Assert.Equal(canonicalBaseline.ProcessedEvents, visibleState.ProcessedEvents);
        Assert.Equal(canonicalBaseline.InventoryEventInbox, visibleState.InventoryEventInbox);

        progressEntries.Enqueue("verification:sales-return-refund-worker-claim-visible-during-active-processing");
    }

    private static async Task AssertSalesReturnAccountingDuplicateWorkerConvergenceAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid returnId,
        SrCon03StateSnapshot canonicalBaseline,
        AccountingOutboxWorkerResult originalWorker,
        AccountingOutboxWorkerResult duplicateWorker,
        ConcurrentQueue<string> progressEntries)
    {
        var after = await LoadSrCon03StateSnapshotAsync(scope, tenantContext, seeded, returnId);

        Assert.Null(originalWorker.Exception);
        Assert.Null(duplicateWorker.Exception);
        Assert.Equal(1, originalWorker.ProcessedCount);
        Assert.Equal(0, duplicateWorker.ProcessedCount);

        Assert.Equal(canonicalBaseline.SalesReturnCount, after.SalesReturnCount);
        Assert.Equal(canonicalBaseline.TotalReturnedBaseQuantity, after.TotalReturnedBaseQuantity);
        Assert.Equal(canonicalBaseline.StockQuantity, after.StockQuantity);
        Assert.Equal(canonicalBaseline.SalesReturnStockMovementCount, after.SalesReturnStockMovementCount);
        Assert.Equal(canonicalBaseline.SalesReturnCustomerTransactionCount, after.SalesReturnCustomerTransactionCount);
        Assert.Equal(canonicalBaseline.TotalCustomerTransactions, after.TotalCustomerTransactions);
        Assert.Equal(canonicalBaseline.CustomerCreditTotal, after.CustomerCreditTotal);
        Assert.Equal(canonicalBaseline.CustomerBalance, after.CustomerBalance);
        Assert.Equal(canonicalBaseline.SalesReturnLedgerEntryCount, after.SalesReturnLedgerEntryCount);
        Assert.Equal(canonicalBaseline.TotalCustomerLedgerEntries, after.TotalCustomerLedgerEntries);
        Assert.Equal(canonicalBaseline.RefundOutboxCount, after.RefundOutboxCount);
        Assert.Equal(canonicalBaseline.RefundOutboxId, after.RefundOutboxId);
        Assert.Equal(canonicalBaseline.RefundOutboxJournalId, after.RefundOutboxJournalId);
        Assert.Null(after.RefundOutboxClaimedUntilUtc);
        Assert.NotNull(after.RefundOutboxProcessedAtUtc);
        Assert.Equal(canonicalBaseline.RefundOutboxAggregateVersion, after.RefundOutboxAggregateVersion);
        Assert.Equal(canonicalBaseline.RefundOutboxPayloadMutationVersion, after.RefundOutboxPayloadMutationVersion);
        Assert.Equal(canonicalBaseline.RefundOutboxPayloadOriginalInvoiceId, after.RefundOutboxPayloadOriginalInvoiceId);
        Assert.Equal(canonicalBaseline.RefundJournalCount, after.RefundJournalCount);
        Assert.Equal(canonicalBaseline.RefundCogsJournalCount, after.RefundCogsJournalCount);
        Assert.Equal(canonicalBaseline.RefundJournalFingerprint, after.RefundJournalFingerprint);
        Assert.Equal(canonicalBaseline.IdempotencyRecordCount, after.IdempotencyRecordCount);
        Assert.Equal(canonicalBaseline.IdempotencyKey, after.IdempotencyKey);
        Assert.Equal(canonicalBaseline.IdempotencyResponseStatusCode, after.IdempotencyResponseStatusCode);
        Assert.Equal(canonicalBaseline.IdempotencyCompletedAtUtc, after.IdempotencyCompletedAtUtc);
        Assert.Equal(canonicalBaseline.InvoiceMutationVersion, after.InvoiceMutationVersion);
        Assert.Equal(canonicalBaseline.RemainingReturnableQuantity, after.RemainingReturnableQuantity);
        Assert.Equal(canonicalBaseline.StockSyncOutboxCount, after.StockSyncOutboxCount);
        Assert.Equal(canonicalBaseline.StockSyncOutboxId, after.StockSyncOutboxId);
        Assert.Equal(canonicalBaseline.StockSyncEventId, after.StockSyncEventId);
        Assert.Equal(canonicalBaseline.StockSyncEventVersion, after.StockSyncEventVersion);
        Assert.Equal(canonicalBaseline.StockSyncPublishedAtUtc, after.StockSyncPublishedAtUtc);
        Assert.Equal(canonicalBaseline.StockSyncEventType, after.StockSyncEventType);
        Assert.Equal(canonicalBaseline.ProcessedEvents, after.ProcessedEvents);
        Assert.Equal(canonicalBaseline.InventoryEventInbox, after.InventoryEventInbox);

        progressEntries.Enqueue("verification:sr-con-03-refund-worker-overlap-converged");
    }

    private static async Task AssertSalesReturnStockSyncClaimVisibleDuringActivePublishAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid returnId,
        SrCon03StateSnapshot baseline,
        ConcurrentQueue<string> progressEntries)
    {
        var duringPublish = await LoadSrCon03StateSnapshotAsync(scope, tenantContext, seeded, returnId);

        Assert.Equal(baseline.SalesReturnCount, duringPublish.SalesReturnCount);
        Assert.Equal(baseline.TotalReturnedBaseQuantity, duringPublish.TotalReturnedBaseQuantity);
        Assert.Equal(baseline.StockQuantity, duringPublish.StockQuantity);
        Assert.Equal(baseline.SalesReturnStockMovementCount, duringPublish.SalesReturnStockMovementCount);
        Assert.Equal(baseline.SalesReturnCustomerTransactionCount, duringPublish.SalesReturnCustomerTransactionCount);
        Assert.Equal(baseline.TotalCustomerTransactions, duringPublish.TotalCustomerTransactions);
        Assert.Equal(baseline.CustomerCreditTotal, duringPublish.CustomerCreditTotal);
        Assert.Equal(baseline.CustomerBalance, duringPublish.CustomerBalance);
        Assert.Equal(baseline.SalesReturnLedgerEntryCount, duringPublish.SalesReturnLedgerEntryCount);
        Assert.Equal(baseline.TotalCustomerLedgerEntries, duringPublish.TotalCustomerLedgerEntries);
        Assert.Equal(baseline.RefundOutboxCount, duringPublish.RefundOutboxCount);
        Assert.Equal(baseline.RefundOutboxId, duringPublish.RefundOutboxId);
        Assert.Equal(baseline.RefundOutboxJournalId, duringPublish.RefundOutboxJournalId);
        Assert.Equal(baseline.RefundOutboxProcessedAtUtc, duringPublish.RefundOutboxProcessedAtUtc);
        Assert.Equal(baseline.RefundOutboxAggregateVersion, duringPublish.RefundOutboxAggregateVersion);
        Assert.Equal(baseline.RefundOutboxPayloadMutationVersion, duringPublish.RefundOutboxPayloadMutationVersion);
        Assert.Equal(baseline.RefundOutboxPayloadOriginalInvoiceId, duringPublish.RefundOutboxPayloadOriginalInvoiceId);
        Assert.Equal(baseline.RefundJournalCount, duringPublish.RefundJournalCount);
        Assert.Equal(baseline.RefundCogsJournalCount, duringPublish.RefundCogsJournalCount);
        Assert.Equal(baseline.RefundJournalFingerprint, duringPublish.RefundJournalFingerprint);
        Assert.Equal(baseline.IdempotencyRecordCount, duringPublish.IdempotencyRecordCount);
        Assert.Equal(baseline.IdempotencyKey, duringPublish.IdempotencyKey);
        Assert.Equal(baseline.IdempotencyResponseStatusCode, duringPublish.IdempotencyResponseStatusCode);
        Assert.Equal(baseline.IdempotencyCompletedAtUtc, duringPublish.IdempotencyCompletedAtUtc);
        Assert.Equal(baseline.InvoiceMutationVersion, duringPublish.InvoiceMutationVersion);
        Assert.Equal(baseline.RemainingReturnableQuantity, duringPublish.RemainingReturnableQuantity);
        Assert.Equal(baseline.ProcessedEvents, duringPublish.ProcessedEvents);
        Assert.Equal(baseline.InventoryEventInbox, duringPublish.InventoryEventInbox);
        Assert.Equal(baseline.StockSyncOutboxCount, duringPublish.StockSyncOutboxCount);
        Assert.Equal(baseline.StockSyncOutboxId, duringPublish.StockSyncOutboxId);
        Assert.Equal(baseline.StockSyncEventId, duringPublish.StockSyncEventId);
        Assert.Equal(baseline.StockSyncEventVersion, duringPublish.StockSyncEventVersion);
        Assert.Equal(baseline.StockSyncEventType, duringPublish.StockSyncEventType);
        Assert.Null(duringPublish.StockSyncPublishedAtUtc);
        Assert.NotNull(duringPublish.StockSyncClaimedUntilUtc);

        progressEntries.Enqueue("verification:sales-return-stock-sync-claim-visible-during-active-publish");
    }

    private static async Task AssertSalesReturnStockSyncDuplicateWorkerConvergenceAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid returnId,
        SrCon03StateSnapshot accountingBaseline,
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

        var after = await LoadSrCon03StateSnapshotAsync(scope, tenantContext, seeded, returnId);

        Assert.Equal(accountingBaseline.SalesReturnCount, after.SalesReturnCount);
        Assert.Equal(accountingBaseline.TotalReturnedBaseQuantity, after.TotalReturnedBaseQuantity);
        Assert.Equal(accountingBaseline.StockQuantity, after.StockQuantity);
        Assert.Equal(accountingBaseline.SalesReturnStockMovementCount, after.SalesReturnStockMovementCount);
        Assert.Equal(accountingBaseline.SalesReturnCustomerTransactionCount, after.SalesReturnCustomerTransactionCount);
        Assert.Equal(accountingBaseline.TotalCustomerTransactions, after.TotalCustomerTransactions);
        Assert.Equal(accountingBaseline.CustomerCreditTotal, after.CustomerCreditTotal);
        Assert.Equal(accountingBaseline.CustomerBalance, after.CustomerBalance);
        Assert.Equal(accountingBaseline.SalesReturnLedgerEntryCount, after.SalesReturnLedgerEntryCount);
        Assert.Equal(accountingBaseline.TotalCustomerLedgerEntries, after.TotalCustomerLedgerEntries);
        Assert.Equal(accountingBaseline.RefundOutboxCount, after.RefundOutboxCount);
        Assert.Equal(accountingBaseline.RefundOutboxId, after.RefundOutboxId);
        Assert.Equal(accountingBaseline.RefundOutboxJournalId, after.RefundOutboxJournalId);
        Assert.Equal(accountingBaseline.RefundOutboxProcessedAtUtc, after.RefundOutboxProcessedAtUtc);
        Assert.Equal(accountingBaseline.RefundOutboxAggregateVersion, after.RefundOutboxAggregateVersion);
        Assert.Equal(accountingBaseline.RefundOutboxPayloadMutationVersion, after.RefundOutboxPayloadMutationVersion);
        Assert.Equal(accountingBaseline.RefundOutboxPayloadOriginalInvoiceId, after.RefundOutboxPayloadOriginalInvoiceId);
        Assert.Equal(accountingBaseline.RefundJournalCount, after.RefundJournalCount);
        Assert.Equal(accountingBaseline.RefundCogsJournalCount, after.RefundCogsJournalCount);
        Assert.Equal(accountingBaseline.RefundJournalFingerprint, after.RefundJournalFingerprint);
        Assert.Equal(accountingBaseline.IdempotencyRecordCount, after.IdempotencyRecordCount);
        Assert.Equal(accountingBaseline.IdempotencyKey, after.IdempotencyKey);
        Assert.Equal(accountingBaseline.IdempotencyResponseStatusCode, after.IdempotencyResponseStatusCode);
        Assert.Equal(accountingBaseline.IdempotencyCompletedAtUtc, after.IdempotencyCompletedAtUtc);
        Assert.Equal(accountingBaseline.InvoiceMutationVersion, after.InvoiceMutationVersion);
        Assert.Equal(accountingBaseline.RemainingReturnableQuantity, after.RemainingReturnableQuantity);
        Assert.Equal(accountingBaseline.ProcessedEvents, after.ProcessedEvents);
        Assert.Equal(accountingBaseline.InventoryEventInbox, after.InventoryEventInbox);

        Assert.Equal(accountingBaseline.StockSyncOutboxCount, after.StockSyncOutboxCount);
        Assert.Equal(accountingBaseline.StockSyncOutboxId, after.StockSyncOutboxId);
        Assert.Equal(accountingBaseline.StockSyncEventId, after.StockSyncEventId);
        Assert.Equal(accountingBaseline.StockSyncEventVersion, after.StockSyncEventVersion);
        Assert.Equal(accountingBaseline.StockSyncEventType, after.StockSyncEventType);
        Assert.NotNull(after.StockSyncPublishedAtUtc);
        Assert.Null(after.StockSyncClaimedUntilUtc);
        Assert.Equal(0, after.StockSyncAttemptCount);

        progressEntries.Enqueue("verification:sr-con-03-stock-sync-overlap-converged");
    }

    private static async Task<SrCon01StateSnapshot> LoadSrCon01StateSnapshotAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid? committedReturnId)
    {
        await using var context = scope.CreateDbContext(tenantContext);

        var salesReturns = await context.Returns
            .Where(item =>
                item.TenantId == seeded.TenantId &&
                item.ReturnType == ReturnType.SalesReturn &&
                item.OriginalInvoiceId == seeded.InvoiceId)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync();

        var salesReturnIds = salesReturns.Select(item => item.Id).ToList();
        var returnItems = await context.ReturnItems
            .Where(item => salesReturnIds.Contains(item.ReturnId))
            .ToListAsync();
        var stockMovements = await context.StockMovements
            .Where(movement =>
                movement.TenantId == seeded.TenantId &&
                movement.MovementType == StockMovementType.SalesReturn &&
                movement.ReferenceId.HasValue &&
                salesReturnIds.Contains(movement.ReferenceId.Value))
            .OrderBy(movement => movement.CreatedAt)
            .ThenBy(movement => movement.Id)
            .ToListAsync();
        var customerTransactions = await context.CustomerTransactions
            .Where(transaction =>
                transaction.TenantId == seeded.TenantId &&
                transaction.CustomerId == seeded.CustomerId)
            .OrderBy(transaction => transaction.TransactionDate)
            .ThenBy(transaction => transaction.CreatedAt)
            .ThenBy(transaction => transaction.Id)
            .ToListAsync();
        var customerLedgers = await context.AccountLedgers
            .Where(entry =>
                entry.TenantId == seeded.TenantId &&
                entry.PartyType == PartyType.Customer &&
                entry.PartyId == seeded.CustomerId)
            .OrderBy(entry => entry.TransactionDate)
            .ThenBy(entry => entry.CreatedAt)
            .ThenBy(entry => entry.Id)
            .ToListAsync();
        var refundOutboxMessages = await context.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.EventType == "PosRefund" &&
                salesReturnIds.Contains(message.BusinessEntityId))
            .OrderBy(message => message.CreatedAt)
            .ToListAsync();
        var idempotencyRecords = await context.SalesRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .Where(record =>
                record.TenantId == seeded.TenantId &&
                record.Operation == "SALES_RETURN_CREATE")
            .OrderBy(record => record.CreatedAt)
            .ToListAsync();
        var journalEntries = committedReturnId.HasValue
            ? await context.JournalEntries
                .IgnoreQueryFilters()
                .Where(journal =>
                    journal.TenantId == seeded.TenantId &&
                    journal.SourceId == committedReturnId.Value)
                .OrderBy(journal => journal.CreatedAt)
                .ThenBy(journal => journal.Id)
                .ToListAsync()
            : new List<JournalEntry>();
        var stockSyncOutboxMessages = await context.StockSyncOutboxMessages
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.VariantId == seeded.VariantId &&
                message.WarehouseId == seeded.WarehouseId)
            .OrderBy(message => message.CreatedAt)
            .ToListAsync();

        var invoice = await context.SalesInvoices
            .SingleAsync(item => item.TenantId == seeded.TenantId && item.Id == seeded.InvoiceId);
        var stock = await context.ProductVariantStocks
            .SingleAsync(item =>
                item.TenantId == seeded.TenantId &&
                item.WarehouseId == seeded.WarehouseId &&
                item.VariantId == seeded.VariantId);
        var customer = await context.Customers
            .SingleAsync(item => item.TenantId == seeded.TenantId && item.Id == seeded.CustomerId);

        var refundOutbox = refundOutboxMessages.SingleOrDefault();
        var idempotencyRecord = idempotencyRecords.SingleOrDefault();
        Guid? originalInvoiceId = null;
        int? payloadMutationVersion = null;
        if (refundOutbox != null)
        {
            using var payload = JsonDocument.Parse(refundOutbox.PayloadJson);
            if (payload.RootElement.TryGetProperty("OriginalInvoiceId", out var originalInvoiceIdElement))
            {
                originalInvoiceId = originalInvoiceIdElement.GetGuid();
            }

            if (payload.RootElement.TryGetProperty("MutationVersion", out var mutationVersionElement) &&
                mutationVersionElement.ValueKind == JsonValueKind.Number)
            {
                payloadMutationVersion = mutationVersionElement.GetInt32();
            }
        }

        return new SrCon01StateSnapshot(
            SalesReturnCount: salesReturns.Count,
            TotalReturnedBaseQuantity: returnItems.Sum(item => item.BaseQuantity),
            StockQuantity: stock.Quantity,
            SalesReturnStockMovementCount: stockMovements.Count,
            StockMovementQuantityAfter: stockMovements.SingleOrDefault()?.QuantityAfter ?? 0m,
            StockMovementQuantityChange: stockMovements.SingleOrDefault()?.QuantityChange ?? 0m,
            SalesReturnCustomerTransactionCount: customerTransactions.Count(item => item.TransactionType == TransactionType.SalesReturn),
            TotalCustomerTransactions: customerTransactions.Count,
            CustomerCreditTotal: customerTransactions
                .Where(item => item.TransactionType == TransactionType.SalesReturn)
                .Sum(item => item.CreditAmount),
            CustomerBalance: customer.Balance,
            SalesReturnLedgerEntryCount: customerLedgers.Count(item => item.TransactionType == TransactionType.SalesReturn),
            TotalCustomerLedgerEntries: customerLedgers.Count,
            RefundOutboxCount: refundOutboxMessages.Count,
            RefundOutboxAggregateVersion: refundOutbox?.AggregateVersion ?? 0,
            RefundOutboxPayloadMutationVersion: payloadMutationVersion ?? 0,
            RefundOutboxPayloadOriginalInvoiceId: originalInvoiceId,
            RefundOutboxProcessedAtUtc: refundOutbox?.ProcessedAtUtc,
            RefundOutboxJournalId: refundOutbox?.JournalId,
            RefundJournalCount: journalEntries.Count(item => item.SourceType == "Return"),
            RefundCogsJournalCount: journalEntries.Count(item => item.SourceType == "Return_COGS"),
            IdempotencyRecordCount: idempotencyRecords.Count,
            IdempotencyKey: idempotencyRecord?.Key,
            IdempotencyResponseStatusCode: idempotencyRecord?.ResponseStatusCode,
            IdempotencyCompletedAtUtc: idempotencyRecord?.CompletedAtUtc,
            IdempotencyResponseJson: idempotencyRecord?.ResponseJson,
            InvoiceMutationVersion: invoice.AccountingMutationVersion,
            RemainingReturnableQuantity: OriginalSoldQuantity - returnItems.Sum(item => item.BaseQuantity),
            StockSyncOutboxCount: stockSyncOutboxMessages.Count,
            StockSyncEventType: stockSyncOutboxMessages.SingleOrDefault()?.EventType);
    }

    private static async Task<SeededScenario> SeedScenarioAsync(ApplicationDbContext context)
    {
        context.Tenants.Add(new Tenant
        {
            Id = TenantId,
            Name = "SR-CON-01 Tenant",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });

        context.Categories.Add(new Category
        {
            Id = CategoryId,
            TenantId = TenantId,
            Name = "SR-CON-01 Category",
            CreatedAt = SeededUtc
        });

        context.Warehouses.Add(new Warehouse
        {
            Id = WarehouseId,
            TenantId = TenantId,
            Name = "SR-CON-01 Warehouse",
            Code = "WH-SR-CON-01",
            IsActive = true,
            CreatedAt = SeededUtc
        });

        context.Customers.Add(new Customer
        {
            Id = CustomerId,
            TenantId = TenantId,
            Name = "SR-CON-01 Customer",
            Balance = 0m,
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
            Name = "SR-CON-01 Product",
            CategoryId = CategoryId,
            PurchasePrice = VariantCostPrice,
            SalePrice = UnitPrice,
            OnlinePrice = UnitPrice,
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
            SKU = "SR-CON-01-SKU",
            Price = UnitPrice,
            OnlinePrice = UnitPrice,
            CostPrice = VariantCostPrice,
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
            SellingPrice = UnitPrice,
            PurchasePrice = VariantCostPrice,
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
            Quantity = PostSaleStockQuantity,
            ReservedQuantity = 0m,
            DamagedQuantity = 0m,
            CreatedAt = SeededUtc,
            UpdatedAt = SeededUtc
        });

        var invoice = new SalesInvoice
        {
            Id = InvoiceId,
            TenantId = TenantId,
            InvoiceNumber = "INV-SR-CON-01",
            InvoiceDate = SeededUtc,
            CustomerId = CustomerId,
            WarehouseId = WarehouseId,
            Status = InvoiceStatus.Completed,
            Subtotal = OriginalSoldQuantity * UnitPrice,
            DiscountAmount = 0m,
            DiscountPercentage = 0m,
            TaxAmount = 0m,
            TotalAmount = OriginalSoldQuantity * UnitPrice,
            PaidAmount = OriginalSoldQuantity * UnitPrice,
            PaymentMethod = PointOfSale.Core.Enums.PaymentMethod.Cash,
            CashAmount = OriginalSoldQuantity * UnitPrice,
            CardAmount = 0m,
            AccountingMutationVersion = 0,
            CreatedAt = SeededUtc
        };

        invoice.Items.Add(new SalesInvoiceItem
        {
            Id = InvoiceItemId,
            SalesInvoiceId = InvoiceId,
            ProductId = ProductId,
            ProductVariantId = VariantId,
            UnitId = UnitId,
            Quantity = OriginalSoldQuantity,
            ConversionFactor = 1m,
            BaseQuantity = OriginalSoldQuantity,
            UnitPrice = UnitPrice,
            DiscountAmount = 0m,
            TaxRate = 0m,
            TaxAmount = 0m,
            TotalAmount = OriginalSoldQuantity * UnitPrice,
            CreatedAt = SeededUtc
        });
        context.SalesInvoices.Add(invoice);

        context.StockMovements.Add(new StockMovement
        {
            Id = SaleMovementId,
            TenantId = TenantId,
            WarehouseId = WarehouseId,
            ProductId = ProductId,
            ProductVariantId = VariantId,
            MovementType = StockMovementType.Sale,
            UnitId = UnitId,
            ConversionFactor = 1m,
            QuantityChange = -OriginalSoldQuantity,
            QuantityAfter = PostSaleStockQuantity,
            AvailableAfter = PostSaleStockQuantity,
            ReservedAfter = 0m,
            DamagedAfter = 0m,
            ReferenceId = InvoiceId,
            ReferenceNumber = invoice.InvoiceNumber,
            Price = UnitPrice,
            CreatedAt = SeededUtc
        });

        context.AccountLedgers.AddRange(
            new AccountLedger
            {
                Id = SaleLedgerId,
                TenantId = TenantId,
                PartyType = PartyType.Customer,
                PartyId = CustomerId,
                TransactionDate = SeededUtc,
                TransactionType = TransactionType.Sale,
                DocumentNumber = invoice.InvoiceNumber,
                DebitAmount = OriginalSoldQuantity * UnitPrice,
                CreditAmount = 0m,
                RunningBalance = OriginalSoldQuantity * UnitPrice,
                ReferenceId = InvoiceId,
                Notes = "Authoritative sale",
                CreatedAt = SeededUtc
            },
            new AccountLedger
            {
                Id = PaymentLedgerId,
                TenantId = TenantId,
                PartyType = PartyType.Customer,
                PartyId = CustomerId,
                TransactionDate = SeededUtc.AddMinutes(1),
                TransactionType = TransactionType.PaymentReceived,
                DocumentNumber = invoice.InvoiceNumber,
                DebitAmount = 0m,
                CreditAmount = OriginalSoldQuantity * UnitPrice,
                RunningBalance = 0m,
                ReferenceId = InvoiceId,
                Notes = "Authoritative sale payment",
                CreatedAt = SeededUtc.AddMinutes(1)
            });

        context.CustomerTransactions.AddRange(
            new CustomerTransaction
            {
                Id = SaleCustomerTransactionId,
                TenantId = TenantId,
                CustomerId = CustomerId,
                TransactionDate = SeededUtc,
                TransactionType = TransactionType.Sale,
                DebitAmount = OriginalSoldQuantity * UnitPrice,
                CreditAmount = 0m,
                BalanceAfter = OriginalSoldQuantity * UnitPrice,
                ReferenceId = InvoiceId,
                ReferenceNumber = invoice.InvoiceNumber,
                Notes = "Authoritative sale",
                CreatedAt = SeededUtc
            },
            new CustomerTransaction
            {
                Id = PaymentCustomerTransactionId,
                TenantId = TenantId,
                CustomerId = CustomerId,
                TransactionDate = SeededUtc.AddMinutes(1),
                TransactionType = TransactionType.PaymentReceived,
                DebitAmount = 0m,
                CreditAmount = OriginalSoldQuantity * UnitPrice,
                BalanceAfter = 0m,
                ReferenceId = InvoiceId,
                ReferenceNumber = invoice.InvoiceNumber,
                Notes = "Authoritative payment",
                CreatedAt = SeededUtc.AddMinutes(1)
            });

        SeedRequiredLedgerAccounts(context);
        await context.SaveChangesAsync();

        return new SeededScenario(
            TenantId,
            CustomerId,
            WarehouseId,
            ProductId,
            VariantId,
            UnitId,
            InvoiceId);
    }

    private static void SeedRequiredLedgerAccounts(ApplicationDbContext context)
    {
        context.LedgerAccounts.AddRange(
            Account.Create(TenantId, AccountCodes.PosSales, "POS Sales", AccountType.Revenue, isSystem: true),
            Account.Create(TenantId, AccountCodes.Inventory, "Inventory", AccountType.Asset, isSystem: true),
            Account.Create(TenantId, AccountCodes.CostOfGoodsSold, "Cost of Goods Sold", AccountType.Expense, isSystem: true),
            Account.Create(TenantId, AccountCodes.AccountsReceivable, "Accounts Receivable", AccountType.Asset, isSystem: true));
    }

    private static ApplicationDbContext CreateDbContext(
        string connectionString,
        ITenantContext tenantContext,
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

    private static DefaultHttpContext BuildHttpContext(Guid userId, string traceIdentifier)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, $"tester-{userId:N}")
        ], "TestAuth");

        return new DefaultHttpContext
        {
            TraceIdentifier = traceIdentifier,
            User = new ClaimsPrincipal(identity)
        };
    }

    private static async Task ConfigureSessionTimeoutsAsync(ApplicationDbContext context)
    {
        context.Database.SetCommandTimeout(TimeSpan.FromSeconds(30));
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("SET statement_timeout = '30s'; SET lock_timeout = '30s';");
    }

    private static bool TryReadSalesReturnResponse(object? payload, out SalesReturnResponse? response)
    {
        response = payload as SalesReturnResponse;
        if (response != null)
        {
            return true;
        }

        if (payload is JsonElement jsonElement)
        {
            response = jsonElement.Deserialize<SalesReturnResponse>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return response != null;
        }

        if (payload != null)
        {
            response = JsonSerializer.Deserialize<SalesReturnResponse>(
                JsonSerializer.Serialize(payload),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }

        return response != null;
    }

    private static string DescribeException(Exception ex) =>
        ex is XunitException
            ? ex.Message
            : $"{ex.GetType().Name}:{ex.Message}";

    private sealed record SeededScenario(
        Guid TenantId,
        Guid CustomerId,
        Guid WarehouseId,
        Guid ProductId,
        Guid VariantId,
        Guid UnitId,
        Guid InvoiceId);

    private sealed record ReturnActor(
        string Name,
        string RequestId,
        string TraceIdentifier,
        Guid UserId);

    private sealed record SrCon01StateSnapshot(
        int SalesReturnCount,
        decimal TotalReturnedBaseQuantity,
        decimal StockQuantity,
        int SalesReturnStockMovementCount,
        decimal StockMovementQuantityAfter,
        decimal StockMovementQuantityChange,
        int SalesReturnCustomerTransactionCount,
        int TotalCustomerTransactions,
        decimal CustomerCreditTotal,
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
        int IdempotencyRecordCount,
        string? IdempotencyKey,
        int? IdempotencyResponseStatusCode,
        DateTime? IdempotencyCompletedAtUtc,
        string? IdempotencyResponseJson,
        int InvoiceMutationVersion,
        decimal RemainingReturnableQuantity,
        int StockSyncOutboxCount,
        string? StockSyncEventType);

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

    private sealed record SrCon03StateSnapshot(
        int SalesReturnCount,
        decimal TotalReturnedBaseQuantity,
        decimal StockQuantity,
        int SalesReturnStockMovementCount,
        int SalesReturnCustomerTransactionCount,
        int TotalCustomerTransactions,
        decimal CustomerCreditTotal,
        decimal CustomerBalance,
        int SalesReturnLedgerEntryCount,
        int TotalCustomerLedgerEntries,
        int RefundOutboxCount,
        Guid RefundOutboxId,
        Guid? RefundOutboxJournalId,
        DateTime? RefundOutboxClaimedUntilUtc,
        DateTime? RefundOutboxProcessedAtUtc,
        int RefundOutboxAggregateVersion,
        int RefundOutboxPayloadMutationVersion,
        Guid? RefundOutboxPayloadOriginalInvoiceId,
        int RefundJournalCount,
        int RefundCogsJournalCount,
        string RefundJournalFingerprint,
        int IdempotencyRecordCount,
        string? IdempotencyKey,
        int? IdempotencyResponseStatusCode,
        DateTime? IdempotencyCompletedAtUtc,
        int InvoiceMutationVersion,
        decimal RemainingReturnableQuantity,
        int StockSyncOutboxCount,
        Guid StockSyncOutboxId,
        Guid StockSyncEventId,
        int StockSyncEventVersion,
        DateTime? StockSyncClaimedUntilUtc,
        DateTime? StockSyncPublishedAtUtc,
        int StockSyncAttemptCount,
        string? StockSyncEventType,
        int ProcessedEvents,
        int InventoryEventInbox);

    private sealed record ReturnActorResult(
        ReturnActor Actor,
        bool IsSuccess,
        int StatusCode,
        SalesReturnResponse? Response,
        string? ResponseBody,
        Exception? Exception)
    {
        public static ReturnActorResult Success(ReturnActor actor, SalesReturnResponse response) =>
            new(actor, true, StatusCodes.Status200OK, response, null, null);

        public static ReturnActorResult Failed(ReturnActor actor, int statusCode, string? responseBody) =>
            new(actor, false, statusCode, null, responseBody, null);

        public static ReturnActorResult Exceptional(ReturnActor actor, Exception exception) =>
            new(actor, false, StatusCodes.Status500InternalServerError, null, null, exception);
    }

    private sealed class SalesReturnCommitOverlapWindow
    {
        private readonly TimeSpan _timeout;
        private readonly ConcurrentQueue<string> _progressEntries;
        private readonly TaskCompletionSource<bool> _originalCommitPaused =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _conflictingInvoiceLockAttempted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _replayClaimAttempted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseOriginalCommit =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _originalCommitPausedBeforeConflictAttempt;
        private int _originalCommitPausedBeforeReplayClaim;

        public SalesReturnCommitOverlapWindow(
            TimeSpan timeout,
            ConcurrentQueue<string> progressEntries)
        {
            _timeout = timeout;
            _progressEntries = progressEntries;
        }

        public bool OriginalCommitPausedBeforeConflictAttempt =>
            Volatile.Read(ref _originalCommitPausedBeforeConflictAttempt) == 1;

        public bool OriginalCommitPaused =>
            _originalCommitPaused.Task.IsCompletedSuccessfully;

        public bool ReplayClaimAttempted =>
            _replayClaimAttempted.Task.IsCompletedSuccessfully;

        public bool OriginalCommitPausedBeforeReplayClaim =>
            Volatile.Read(ref _originalCommitPausedBeforeReplayClaim) == 1;

        public void MarkOriginalCommitPaused(string actorName)
        {
            _progressEntries.Enqueue($"{actorName}:commit-paused-before-return-visibility");
            _originalCommitPaused.TrySetResult(true);
        }

        public void MarkConflictingInvoiceLockAttempt(string actorName)
        {
            if (_originalCommitPaused.Task.IsCompletedSuccessfully)
            {
                Interlocked.Exchange(ref _originalCommitPausedBeforeConflictAttempt, 1);
            }

            _progressEntries.Enqueue($"{actorName}:attempted-invoice-lock-while-original-active");
            _conflictingInvoiceLockAttempted.TrySetResult(true);
        }

        public void MarkReplayClaimAttempt(string actorName)
        {
            if (_originalCommitPaused.Task.IsCompletedSuccessfully)
            {
                Interlocked.Exchange(ref _originalCommitPausedBeforeReplayClaim, 1);
            }

            _progressEntries.Enqueue($"{actorName}:attempted-idempotency-claim-while-original-active");
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
                    $"Timed out waiting for the original sales return to pause before commit. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public async Task WaitForConflictingLockAttemptAsync()
        {
            try
            {
                await _conflictingInvoiceLockAttempted.Task.WaitAsync(_timeout);
            }
            catch (TimeoutException)
            {
                throw new XunitException(
                    $"Timed out waiting for the conflicting sales return to attempt the original invoice lock. Progress: {string.Join(" | ", _progressEntries)}");
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
                    $"Timed out waiting for Request B to attempt the overlapping sales-return idempotency claim. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public async Task WaitUntilReleasedAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _releaseOriginalCommit.Task.WaitAsync(_timeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                throw new XunitException(
                    $"Timed out waiting to release the original sales return commit. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public void ReleaseOriginalCommit()
        {
            _progressEntries.Enqueue("orchestrator:release-original-sales-return-commit");
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
                    $"Timed out waiting for the original sales-return worker to pause before commit. Progress: {string.Join(" | ", _progressEntries)}");
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
                    $"Timed out waiting to release the original sales-return worker. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public void ReleaseOriginalWorker()
        {
            _progressEntries.Enqueue("orchestrator:release-original-sales-return-worker");
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
                    $"Timed out waiting to release the first sales-return stock-sync publish. Progress: {string.Join(" | ", _progressEntries)}");
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
                    $"Timed out waiting for the first sales-return stock-sync publish attempt. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public void ReleaseFirstPublish()
        {
            _progressEntries.Enqueue("orchestrator:release-first-sales-return-stock-sync-publish");
            _releaseFirstPublish.TrySetResult(true);
        }
    }

    private sealed class SalesReturnCommitPauseInterceptor(
        string actorName,
        SalesReturnCommitOverlapWindow overlapWindow) : DbTransactionInterceptor
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
                // SR-CON-01 critical window: the winning return has already staged the authoritative
                // historical-bound mutation, stock restoration, customer credit row, refund outbox row,
                // stock movement, and SQL idempotency completion, but PostgreSQL has not committed any of it yet.
                overlapWindow.MarkOriginalCommitPaused(actorName);
                await overlapWindow.WaitUntilReleasedAsync(cancellationToken);
            }

            return await base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }

    private sealed class SalesReturnInvoiceLockAttemptInterceptor(
        string actorName,
        SalesReturnCommitOverlapWindow overlapWindow) : DbCommandInterceptor
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
                overlapWindow.MarkConflictingInvoiceLockAttempt(actorName);
            }

            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private static bool IsOriginalInvoiceForUpdateQuery(string commandText)
        {
            return commandText.Contains("SalesInvoices", StringComparison.OrdinalIgnoreCase) &&
                   commandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class SalesReturnReplayClaimAttemptInterceptor(
        string actorName,
        SalesReturnCommitOverlapWindow overlapWindow) : DbCommandInterceptor
    {
        private int _intercepted;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (IsSalesReturnIdempotencyInsert(command.CommandText) &&
                Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0)
            {
                // SR-CON-02 critical window: Request B is submitting the same SQL-backed
                // idempotency claim while Request A still holds the uncommitted return,
                // refund outbox intent, stock restoration, customer credit, and mutation version.
                overlapWindow.MarkReplayClaimAttempt(actorName);
            }

            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private static bool IsSalesReturnIdempotencyInsert(string commandText)
        {
            return commandText.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase) &&
                   commandText.Contains("SalesRequestIdempotency", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class SalesReturnAccountingOutboxReturnLockPauseInterceptor(
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
            if (IsSalesReturnForUpdateQuery(command.CommandText) &&
                Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0)
            {
                // The refund worker has already claimed the authoritative outbox row. Pausing
                // after PostgreSQL executes the Return FOR UPDATE keeps Worker A active and
                // uncommitted while duplicate Worker B receives the same logical delivery and
                // must respect the claimed-row duplicate fence instead of reposting reversal truth.
                window.MarkOriginalPaused(workerName);
                await window.WaitUntilReleasedAsync(cancellationToken);
            }

            return await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }

        private static bool IsSalesReturnForUpdateQuery(string commandText)
        {
            return commandText.Contains("FROM \"Returns\"", StringComparison.OrdinalIgnoreCase) &&
                   commandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class FixedCorrelationIdAccessor(string correlationId) : ICorrelationIdAccessor
    {
        public string GetCorrelationId() => correlationId;
    }

    private sealed class TestLogger<T>(ConcurrentQueue<string> entries) : ILogger<T>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (exception != null)
            {
                message = $"{message} | {DescribeExceptionChain(exception)}";
            }

            entries.Enqueue($"{typeof(T).Name}:{logLevel}:{message}");
        }

        private static string DescribeExceptionChain(Exception exception)
        {
            var parts = new List<string>();
            Exception? current = exception;
            while (current != null)
            {
                parts.Add($"{current.GetType().Name}: {current.Message}");
                current = current.InnerException;
            }

            return string.Join(" --> ", parts);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
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
