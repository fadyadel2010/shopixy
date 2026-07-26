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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MongoDB.Driver;
using PointOfSale.API.Controllers;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Audit;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Interfaces.Treasury;
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

public sealed class PurchaseReturnDistributedReliabilityIntegrationTests
{
    private static readonly DateTime SeededUtc = new(2026, 5, 14, 13, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ReturnRequestUtc = new(2026, 5, 14, 13, 30, 0, DateTimeKind.Utc);

    private static readonly Guid TenantId = Guid.Parse("7f0e2e17-90cc-4184-b676-d6aa20f14001");
    private static readonly Guid CategoryId = Guid.Parse("7f0e2e17-90cc-4184-b676-d6aa20f14002");
    private static readonly Guid SupplierId = Guid.Parse("7f0e2e17-90cc-4184-b676-d6aa20f14003");
    private static readonly Guid ProductId = Guid.Parse("7f0e2e17-90cc-4184-b676-d6aa20f14004");
    private static readonly Guid VariantId = Guid.Parse("7f0e2e17-90cc-4184-b676-d6aa20f14005");
    private static readonly Guid UnitId = Guid.Parse("7f0e2e17-90cc-4184-b676-d6aa20f14006");
    private static readonly Guid WarehouseId = Guid.Parse("7f0e2e17-90cc-4184-b676-d6aa20f14007");
    private static readonly Guid ProductUnitId = Guid.Parse("7f0e2e17-90cc-4184-b676-d6aa20f14008");
    private static readonly Guid VariantStockId = Guid.Parse("7f0e2e17-90cc-4184-b676-d6aa20f14009");
    private static readonly Guid InvoiceId = Guid.Parse("7f0e2e17-90cc-4184-b676-d6aa20f14010");
    private static readonly Guid InvoiceItemId = Guid.Parse("7f0e2e17-90cc-4184-b676-d6aa20f14011");
    private static readonly Guid OpeningStockMovementId = Guid.Parse("7f0e2e17-90cc-4184-b676-d6aa20f14012");
    private static readonly Guid PurchaseStockMovementId = Guid.Parse("7f0e2e17-90cc-4184-b676-d6aa20f14013");
    private static readonly Guid PurchaseSupplierTransactionId = Guid.Parse("7f0e2e17-90cc-4184-b676-d6aa20f14014");
    private static readonly Guid PurchaseSupplierLedgerId = Guid.Parse("7f0e2e17-90cc-4184-b676-d6aa20f14015");
    private static readonly Guid ActorAUserId = Guid.Parse("7f0e2e17-90cc-4184-b676-d6aa20f14016");
    private static readonly Guid ActorBUserId = Guid.Parse("7f0e2e17-90cc-4184-b676-d6aa20f14017");

    private const decimal HistoricalReceivedBaseQuantity = 5m;
    private const decimal RequestedReturnBaseQuantity = 4m;
    private const decimal PreexistingStockQuantity = 4m;
    private const decimal PostPurchaseStockQuantity = 9m;
    private const decimal UnitCost = 10m;
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
    public async Task PR_CON_01_Concurrent_Purchase_Return_Against_Same_Purchase_Invoice_Converges_Safely()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out _, out _) ||
            databaseScope is null)
        {
            return;
        }

        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);

        await using var seedContext = scope.CreateDbContext(tenantContext);
        await EnsureReliabilityScenarioSchemaAsync(seedContext);
        var seeded = await SeedScenarioAsync(seedContext);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");

        var progressEntries = new ConcurrentQueue<string>();
        var diagnosticLogs = progressEntries;
        var overlapWindow = new PurchaseReturnCommitOverlapWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);

        var actorA = new PurchaseReturnActor(
            Name: "actor-a-original",
            RequestId: "pr-con-01-actor-a",
            TraceIdentifier: "pr-con-01-original",
            UserId: ActorAUserId);
        var actorB = new PurchaseReturnActor(
            Name: "actor-b-conflict",
            RequestId: "pr-con-01-actor-b",
            TraceIdentifier: "pr-con-01-conflict",
            UserId: ActorBUserId);

        var originalTask = ExecuteReturnActorAsync(
            connectionString,
            tenantContext,
            seeded,
            actorA,
            progressEntries,
            diagnosticLogs,
            reason: "PR-CON-01 overlap validation",
            notes: actorA.Name,
            new PurchaseReturnCommitPauseInterceptor(actorA.Name, overlapWindow));

        await overlapWindow.WaitForOriginalCommitPauseAsync();
        await AssertNoUncommittedPurchaseReturnStateVisibleAsync(scope, tenantContext, seeded, progressEntries);

        var conflictingTask = ExecuteReturnActorAsync(
            connectionString,
            tenantContext,
            seeded,
            actorB,
            progressEntries,
            diagnosticLogs,
            reason: "PR-CON-01 overlap validation",
            notes: actorB.Name,
            new PurchaseReturnInvoiceLockAttemptInterceptor(actorB.Name, overlapWindow));

        await overlapWindow.WaitForConflictingInvoiceLockAttemptAsync();
        Assert.True(
            overlapWindow.OriginalCommitPausedBeforeConflictAttempt,
            $"The conflicting purchase return did not attempt the purchase invoice lock while the original transaction was still active. Progress: {string.Join(" | ", progressEntries)}");

        overlapWindow.ReleaseOriginalCommit();

        PurchaseReturnActorResult[] results;
        try
        {
            results = await Task.WhenAll(originalTask, conflictingTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"PR-CON-01 timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.All(results, result => Assert.Null(result.Exception));

        await AssertPrCon01ConvergenceAsync(
            scope,
            tenantContext,
            seeded,
            results,
            progressEntries);
    }

    [Fact]
    public async Task PR_CON_02_Replay_During_Active_Purchase_Return_Transaction_Converges_Safely()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out _, out _) ||
            databaseScope is null)
        {
            return;
        }

        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);

        await using var seedContext = scope.CreateDbContext(tenantContext);
        await EnsureReliabilityScenarioSchemaAsync(seedContext);
        var seeded = await SeedScenarioAsync(seedContext);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");

        var progressEntries = new ConcurrentQueue<string>();
        var diagnosticLogs = progressEntries;
        var overlapWindow = new PurchaseReturnCommitOverlapWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var sharedRequestId = "pr-con-02-active-replay";
        const string sharedReason = "PR-CON-02 replay overlap validation";
        const string sharedNotes = "pr-con-02-shared-payload";

        var originalActor = new PurchaseReturnActor(
            Name: "request-a-original",
            RequestId: sharedRequestId,
            TraceIdentifier: "pr-con-02-original-active",
            UserId: ActorAUserId);
        var replayActor = new PurchaseReturnActor(
            Name: "request-b-replay",
            RequestId: sharedRequestId,
            TraceIdentifier: "pr-con-02-overlapping-replay",
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
            new PurchaseReturnCommitPauseInterceptor(originalActor.Name, overlapWindow));

        await overlapWindow.WaitForOriginalCommitPauseAsync();

        // Request A has already staged the bounded historical reversal, supplier debit,
        // stock decrement, accounting intent, and idempotency completion inside its
        // transaction. Fresh readers must still observe none of that before commit.
        await AssertNoUncommittedPurchaseReturnStateVisibleAsync(scope, tenantContext, seeded, progressEntries);

        var replayTask = ExecuteReturnActorAsync(
            connectionString,
            tenantContext,
            seeded,
            replayActor,
            progressEntries,
            diagnosticLogs,
            sharedReason,
            sharedNotes,
            new PurchaseReturnReplayClaimAttemptInterceptor(replayActor.Name, overlapWindow));

        await overlapWindow.WaitForReplayClaimAttemptAsync();
        Assert.True(
            overlapWindow.OriginalCommitPausedBeforeReplayClaim,
            $"Replay did not overlap the active purchase-return transaction window. Progress: {string.Join(" | ", progressEntries)}");

        overlapWindow.ReleaseOriginalCommit();

        PurchaseReturnActorResult[] results;
        try
        {
            results = await Task.WhenAll(originalTask, replayTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"PR-CON-02 timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.True(
            overlapWindow.OriginalCommitPaused,
            $"Request A never paused before commit. Progress: {string.Join(" | ", progressEntries)}");
        Assert.True(
            overlapWindow.ReplayClaimAttempted,
            $"Request B never attempted the overlapping purchase-return idempotency claim. Progress: {string.Join(" | ", progressEntries)}");
        Assert.All(results, result => Assert.Null(result.Exception));

        await AssertPrCon02ConvergenceAsync(
            scope,
            connectionString,
            tenantContext,
            seeded,
            originalActor,
            replayActor,
            results,
            progressEntries);
    }

    private static Task<PurchaseReturnActorResult> ExecuteReturnActorAsync(
        string connectionString,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        PurchaseReturnActor actor,
        ConcurrentQueue<string> progressEntries,
        ConcurrentQueue<string> diagnosticLogs,
        string reason,
        string notes,
        params IInterceptor[] interceptors) =>
        ExecuteReturnActorAsync(
            connectionString,
            tenantContext,
            seeded,
            actor,
            progressEntries,
            diagnosticLogs,
            reason,
            notes,
            commerceAccountingOverride: null,
            interceptors: interceptors);

    private static async Task<PurchaseReturnActorResult> ExecuteReturnActorAsync(
        string connectionString,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        PurchaseReturnActor actor,
        ConcurrentQueue<string> progressEntries,
        ConcurrentQueue<string> diagnosticLogs,
        string reason,
        string notes,
        ICommerceAccountingService? commerceAccountingOverride = null,
        params IInterceptor[] interceptors)
    {
        await using var context = CreateDbContext(connectionString, tenantContext, interceptors);
        await ConfigureSessionTimeoutsAsync(context);

        var controller = BuildPurchaseReturnsController(context, tenantContext, actor, diagnosticLogs, commerceAccountingOverride);
        controller.Request.Headers["Idempotency-Key"] = actor.RequestId;

        var request = new CreatePurchaseReturnRequest
        {
            SupplierId = seeded.SupplierId,
            WarehouseId = seeded.WarehouseId,
            ReturnDate = ReturnRequestUtc,
            OriginalInvoiceId = seeded.InvoiceId,
            RefundAmount = RequestedReturnBaseQuantity * UnitCost,
            Reason = reason,
            Notes = notes,
            Items =
            [
                new PurchaseReturnItemRequest
                {
                    ProductId = seeded.ProductId,
                    ProductVariantId = seeded.VariantId,
                    UnitId = seeded.UnitId,
                    Quantity = RequestedReturnBaseQuantity,
                    UnitPrice = UnitCost,
                    TaxRate = 0m,
                    Reason = "Concurrent purchase return against same authoritative purchase line"
                }
            ]
        };

        progressEntries.Enqueue($"{actor.Name}:request-ready");

        try
        {
            var actionResult = await controller.CreatePurchaseReturn(request);

            if (actionResult.Result is OkObjectResult okResult && okResult.Value is PurchaseReturnResponse okResponse)
            {
                progressEntries.Enqueue($"{actor.Name}:committed:{okResponse.ReturnId}");
                return PurchaseReturnActorResult.Success(actor, okResponse);
            }

            if (actionResult.Result is ObjectResult objectResult &&
                (objectResult.StatusCode is null or StatusCodes.Status200OK) &&
                TryReadPurchaseReturnResponse(objectResult.Value, out var storedResponse) &&
                storedResponse is not null)
            {
                progressEntries.Enqueue($"{actor.Name}:replayed:{storedResponse.ReturnId}");
                return PurchaseReturnActorResult.Success(actor, storedResponse);
            }

            if (actionResult.Result is BadRequestObjectResult badRequest)
            {
                var payload = JsonSerializer.Serialize(badRequest.Value);
                progressEntries.Enqueue($"{actor.Name}:failed-closed");
                return PurchaseReturnActorResult.Failed(actor, StatusCodes.Status400BadRequest, payload);
            }

            if (actionResult.Result is ObjectResult unexpectedObjectResult)
            {
                var payload = JsonSerializer.Serialize(unexpectedObjectResult.Value);
                progressEntries.Enqueue($"{actor.Name}:unexpected-status:{unexpectedObjectResult.StatusCode}");
                return PurchaseReturnActorResult.Failed(actor, unexpectedObjectResult.StatusCode ?? StatusCodes.Status500InternalServerError, payload);
            }

            throw new XunitException(
                $"Unexpected ActionResult shape for {actor.Name}. ResultType={actionResult.Result?.GetType().Name ?? "<null>"}.");
        }
        catch (Exception ex)
        {
            progressEntries.Enqueue($"{actor.Name}:exception:{DescribeException(ex)}");
            return PurchaseReturnActorResult.Exceptional(actor, ex);
        }
    }

    private static PurchaseReturnsController BuildPurchaseReturnsController(
        ApplicationDbContext context,
        TestTenantContext tenantContext,
        PurchaseReturnActor actor,
        ConcurrentQueue<string> diagnosticLogs,
        ICommerceAccountingService? commerceAccountingOverride = null)
    {
        var controllerLogger = new TestLogger<PurchaseReturnsController>(diagnosticLogs);
        var ledgerService = new LedgerService(
            context,
            tenantContext,
            NullLogger<LedgerService>.Instance,
            new NoOpAuditService());
        var accountResolver = new AccountResolver(context, NullLogger<AccountResolver>.Instance);
        var postingService = new FinancialPostingService(context, NullLogger<FinancialPostingService>.Instance);
        var cashboxLedgerAccountService = new CashboxLedgerAccountService(context, NullLogger<CashboxLedgerAccountService>.Instance);
        var commerceAccountingService = commerceAccountingOverride ?? new CommerceAccountingService(
            postingService,
            accountResolver,
            cashboxLedgerAccountService,
            NullLogger<CommerceAccountingService>.Instance);
        var accountingOutboxService = new AccountingOutboxService(
            context,
            new FixedCorrelationIdAccessor(actor.TraceIdentifier),
            NullLogger<AccountingOutboxService>.Instance);

        return new PurchaseReturnsController(
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
    }

    private static async Task AssertNoUncommittedPurchaseReturnStateVisibleAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        ConcurrentQueue<string> progressEntries)
    {
        var snapshot = await LoadPrCon01StateSnapshotAsync(scope, tenantContext, seeded);

        Assert.Equal(0, snapshot.PurchaseReturnCount);
        Assert.Equal(0m, snapshot.TotalReturnedBaseQuantity);
        Assert.Equal(PostPurchaseStockQuantity, snapshot.StockQuantity);
        Assert.Equal(0, snapshot.PurchaseReturnStockMovementCount);
        Assert.Equal(0, snapshot.PurchaseReturnSupplierTransactionCount);
        Assert.Equal(0m, snapshot.PurchaseReturnSupplierDebitTotal);
        Assert.Equal(0, snapshot.PurchaseReturnSupplierLedgerCount);
        Assert.Equal(0m, snapshot.PurchaseReturnSupplierLedgerDebitTotal);
        Assert.Equal(0, snapshot.PurchaseReturnOutboxCount);
        Assert.Equal(0, snapshot.PurchaseReturnJournalCount);
        Assert.Equal(0, snapshot.PurchaseReturnJournalLineCount);
        Assert.Equal(0, snapshot.IdempotencyRecordCount);
        Assert.Equal(0, snapshot.InvoiceMutationVersion);
        Assert.Equal(HistoricalReceivedBaseQuantity, snapshot.RemainingReturnableQuantity);
        Assert.Equal(-HistoricalReceivedBaseQuantity * UnitCost, snapshot.SupplierBalance);
        Assert.Equal(0, snapshot.StockSyncOutboxCount);

        progressEntries.Enqueue("verification:no-uncommitted-purchase-return-state-visible");
    }

    private static async Task AssertPrCon01ConvergenceAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        IReadOnlyList<PurchaseReturnActorResult> results,
        ConcurrentQueue<string> progressEntries)
    {
        var committed = results.Single(result => result.IsSuccess);
        var rejected = results.Single(result => !result.IsSuccess);

        Assert.Equal(StatusCodes.Status200OK, committed.StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, rejected.StatusCode);
        Assert.NotNull(committed.Response);
        Assert.Contains(
            "Cannot return more than received quantity",
            rejected.ResponseBody ?? string.Empty,
            StringComparison.Ordinal);

        var snapshot = await LoadPrCon01StateSnapshotAsync(scope, tenantContext, seeded);

        Assert.Equal(1, snapshot.PurchaseReturnCount);
        Assert.Equal(RequestedReturnBaseQuantity, snapshot.TotalReturnedBaseQuantity);
        Assert.True(snapshot.TotalReturnedBaseQuantity <= HistoricalReceivedBaseQuantity);
        Assert.Equal(PostPurchaseStockQuantity - RequestedReturnBaseQuantity, snapshot.StockQuantity);
        Assert.Equal(1, snapshot.PurchaseReturnStockMovementCount);
        Assert.Equal(PostPurchaseStockQuantity - RequestedReturnBaseQuantity, snapshot.StockMovementQuantityAfter);
        Assert.Equal(-RequestedReturnBaseQuantity, snapshot.StockMovementQuantityChange);
        Assert.Equal(1, snapshot.PurchaseReturnSupplierTransactionCount);
        Assert.Equal(2, snapshot.TotalSupplierTransactions);
        Assert.Equal(RequestedReturnBaseQuantity * UnitCost, snapshot.PurchaseReturnSupplierDebitTotal);
        Assert.Equal(-(HistoricalReceivedBaseQuantity * UnitCost) + (RequestedReturnBaseQuantity * UnitCost), snapshot.SupplierBalance);
        Assert.Equal(1, snapshot.PurchaseReturnSupplierLedgerCount);
        Assert.Equal(2, snapshot.TotalSupplierLedgerEntries);
        Assert.Equal(RequestedReturnBaseQuantity * UnitCost, snapshot.PurchaseReturnSupplierLedgerDebitTotal);
        Assert.Equal(1, snapshot.PurchaseReturnOutboxCount);
        Assert.Equal(1, snapshot.PurchaseReturnOutboxAggregateVersion);
        Assert.Equal(1, snapshot.PurchaseReturnOutboxPayloadMutationVersion);
        Assert.Equal(seeded.InvoiceId, snapshot.PurchaseReturnOutboxPayloadOriginalInvoiceId);
        Assert.NotNull(snapshot.PurchaseReturnOutboxProcessedAtUtc);
        Assert.NotNull(snapshot.PurchaseReturnOutboxJournalId);
        Assert.Equal(1, snapshot.InvoiceMutationVersion);
        Assert.Equal(HistoricalReceivedBaseQuantity - RequestedReturnBaseQuantity, snapshot.RemainingReturnableQuantity);
        Assert.Equal(1, snapshot.PurchaseReturnJournalCount);
        Assert.Equal(2, snapshot.PurchaseReturnJournalLineCount);
        Assert.Equal(RequestedReturnBaseQuantity * UnitCost, snapshot.PurchaseReturnJournalTotalDebits);
        Assert.Equal(RequestedReturnBaseQuantity * UnitCost, snapshot.PurchaseReturnJournalTotalCredits);
        Assert.Equal(1, snapshot.StockSyncOutboxCount);
        Assert.Equal(nameof(ProductVariantStockChangedEvent), snapshot.StockSyncEventType);
        Assert.Equal(1, snapshot.IdempotencyRecordCount);
        Assert.Equal(BuildScopedIdempotencyKey(committed.Actor.RequestId), snapshot.IdempotencyKey);
        Assert.Equal(StatusCodes.Status200OK, snapshot.IdempotencyResponseStatusCode);
        Assert.NotNull(snapshot.IdempotencyCompletedAtUtc);
        Assert.Contains(
            committed.Response!.ReturnId.ToString("D"),
            snapshot.IdempotencyResponseJson ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

        progressEntries.Enqueue("verification:pr-con-01-safe-outcome-a-converged");
    }

    private static async Task AssertPrCon02ConvergenceAsync(
        TestDatabaseScope scope,
        string connectionString,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        PurchaseReturnActor originalActor,
        PurchaseReturnActor replayActor,
        IReadOnlyList<PurchaseReturnActorResult> results,
        ConcurrentQueue<string> progressEntries)
    {
        var original = results.Single(result => result.Actor.Name == originalActor.Name);
        var replay = results.Single(result => result.Actor.Name == replayActor.Name);

        Assert.True(original.IsSuccess, original.ResponseBody);
        Assert.NotNull(original.Response);

        var snapshot = await LoadPrCon01StateSnapshotAsync(scope, tenantContext, seeded);

        Assert.Equal(1, snapshot.PurchaseReturnCount);
        Assert.Equal(RequestedReturnBaseQuantity, snapshot.TotalReturnedBaseQuantity);
        Assert.True(snapshot.TotalReturnedBaseQuantity <= HistoricalReceivedBaseQuantity);
        Assert.Equal(PostPurchaseStockQuantity - RequestedReturnBaseQuantity, snapshot.StockQuantity);
        Assert.Equal(1, snapshot.PurchaseReturnStockMovementCount);
        Assert.Equal(PostPurchaseStockQuantity - RequestedReturnBaseQuantity, snapshot.StockMovementQuantityAfter);
        Assert.Equal(-RequestedReturnBaseQuantity, snapshot.StockMovementQuantityChange);
        Assert.Equal(1, snapshot.PurchaseReturnSupplierTransactionCount);
        Assert.Equal(2, snapshot.TotalSupplierTransactions);
        Assert.Equal(RequestedReturnBaseQuantity * UnitCost, snapshot.PurchaseReturnSupplierDebitTotal);
        Assert.Equal(-(HistoricalReceivedBaseQuantity * UnitCost) + (RequestedReturnBaseQuantity * UnitCost), snapshot.SupplierBalance);
        Assert.Equal(1, snapshot.PurchaseReturnSupplierLedgerCount);
        Assert.Equal(2, snapshot.TotalSupplierLedgerEntries);
        Assert.Equal(RequestedReturnBaseQuantity * UnitCost, snapshot.PurchaseReturnSupplierLedgerDebitTotal);
        Assert.Equal(1, snapshot.PurchaseReturnOutboxCount);
        Assert.Equal(1, snapshot.PurchaseReturnOutboxAggregateVersion);
        Assert.Equal(1, snapshot.PurchaseReturnOutboxPayloadMutationVersion);
        Assert.Equal(seeded.InvoiceId, snapshot.PurchaseReturnOutboxPayloadOriginalInvoiceId);
        Assert.NotNull(snapshot.PurchaseReturnOutboxProcessedAtUtc);
        Assert.NotNull(snapshot.PurchaseReturnOutboxJournalId);
        Assert.Equal(1, snapshot.InvoiceMutationVersion);
        Assert.Equal(HistoricalReceivedBaseQuantity - RequestedReturnBaseQuantity, snapshot.RemainingReturnableQuantity);
        Assert.Equal(1, snapshot.PurchaseReturnJournalCount);
        Assert.Equal(2, snapshot.PurchaseReturnJournalLineCount);
        Assert.Equal(RequestedReturnBaseQuantity * UnitCost, snapshot.PurchaseReturnJournalTotalDebits);
        Assert.Equal(RequestedReturnBaseQuantity * UnitCost, snapshot.PurchaseReturnJournalTotalCredits);
        Assert.Equal(1, snapshot.StockSyncOutboxCount);
        Assert.Equal(nameof(ProductVariantStockChangedEvent), snapshot.StockSyncEventType);
        Assert.Equal(1, snapshot.IdempotencyRecordCount);
        Assert.Equal(BuildScopedIdempotencyKey(originalActor.RequestId), snapshot.IdempotencyKey);
        Assert.Equal(BuildScopedIdempotencyKey(replayActor.RequestId), snapshot.IdempotencyKey);
        Assert.Equal(StatusCodes.Status200OK, snapshot.IdempotencyResponseStatusCode);
        Assert.NotNull(snapshot.IdempotencyCompletedAtUtc);
        Assert.Contains(
            original.Response.ReturnId.ToString("D"),
            snapshot.IdempotencyResponseJson ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

        if (replay.IsSuccess)
        {
            // Safe outcome A: PostgreSQL held the overlapping same-key replay behind the
            // authoritative in-flight claim, then Request B converged to the committed result.
            Assert.NotNull(replay.Response);
            Assert.Equal(original.Response.ReturnId, replay.Response!.ReturnId);
            Assert.Equal(original.Response.ReturnNumber, replay.Response.ReturnNumber);
            progressEntries.Enqueue("verification:pr-con-02-safe-outcome-a-replay-converged");
        }
        else
        {
            // Safe outcome B: replay failed closed while Request A still owned the in-flight
            // claim. The durable-state assertions above prove no duplicate reversal effects survived.
            Assert.Contains(replay.StatusCode, new[] { StatusCodes.Status409Conflict, StatusCodes.Status500InternalServerError });
            progressEntries.Enqueue("verification:pr-con-02-safe-outcome-b-failed-closed");
        }

        await AssertPurchaseReturnReplaySafetyAsync(
            connectionString,
            tenantContext,
            seeded,
            original,
            progressEntries);
    }

    [Fact]
    public async Task STOCK_CON_06_Replay_During_Purchase_Return_Stock_Deduction_Converges_Safely()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var mongoScope = await CreateIsolatedMongoDatabaseScopeAsync("stock-con-06");
        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);

        await using var seedContext = scope.CreateDbContext(tenantContext);
        await EnsureReliabilityScenarioSchemaAsync(seedContext);
        var seeded = await SeedScenarioAsync(seedContext);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");
        var mongoConnectionString = GetRequiredMongoConnectionString();

        await SeedStockCon06ReadModelsAsync(mongoScope.Database, seeded);

        var progressEntries = new ConcurrentQueue<string>();
        var diagnosticLogs = progressEntries;
        var overlapWindow = new PurchaseReturnCommitOverlapWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var sharedRequestId = "stock-con-06-active-purchase-return-replay";
        const string sharedReason = "PR-CON-02 replay overlap validation";
        const string sharedNotes = "pr-con-02-shared-payload";

        var requestA = new PurchaseReturnActor(
            Name: "stock-con-06-request-a",
            RequestId: sharedRequestId,
            TraceIdentifier: "stock-con-06-request-a",
            UserId: ActorAUserId);
        var requestB = new PurchaseReturnActor(
            Name: "stock-con-06-request-b-replay",
            RequestId: sharedRequestId,
            TraceIdentifier: "stock-con-06-request-b",
            UserId: ActorBUserId);

        var requestATask = ExecuteReturnActorAsync(
            connectionString,
            tenantContext,
            seeded,
            requestA,
            progressEntries,
            diagnosticLogs,
            sharedReason,
            sharedNotes,
            new PurchaseReturnCommitPauseInterceptor(requestA.Name, overlapWindow));

        await overlapWindow.WaitForOriginalCommitPauseAsync();
        await AssertStockCon06NoPartialDeductionVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            progressEntries);

        var requestBTask = ExecuteReturnActorAsync(
            connectionString,
            tenantContext,
            seeded,
            requestB,
            progressEntries,
            diagnosticLogs,
            sharedReason,
            sharedNotes,
            new PurchaseReturnReplayClaimAttemptInterceptor(requestB.Name, overlapWindow));

        await overlapWindow.WaitForReplayClaimAttemptAsync();
        Assert.True(
            overlapWindow.OriginalCommitPausedBeforeReplayClaim,
            $"Replay did not overlap the active purchase-return stock deduction. Progress: {string.Join(" | ", progressEntries)}");

        await AssertStockCon06NoPartialDeductionVisibilityAsync(
            scope,
            tenantContext,
            mongoScope.Database,
            seeded,
            progressEntries);

        overlapWindow.ReleaseOriginalCommit();

        PurchaseReturnActorResult[] results;
        try
        {
            results = await Task.WhenAll(requestATask, requestBTask).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"STOCK-CON-06 timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        Assert.True(overlapWindow.OriginalCommitPaused, $"Request A never paused before commit. Progress: {string.Join(" | ", progressEntries)}");
        Assert.True(overlapWindow.ReplayClaimAttempted, $"Request B never attempted the overlapping replay claim. Progress: {string.Join(" | ", progressEntries)}");
        Assert.All(results, result => Assert.Null(result.Exception));

        await AssertPrCon02ConvergenceAsync(
            scope,
            connectionString,
            tenantContext,
            seeded,
            requestA,
            requestB,
            results,
            progressEntries);

        var committedReturn = results.Single(result => result.Actor.Name == requestA.Name);
        var stockEvent = await LoadStockCon06PurchaseReturnStockEventAsync(
            scope,
            tenantContext,
            seeded,
            committedReturn.Response!.ReturnId);

        await ApplyStockCon06ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            stockEvent,
            progressEntries);
        await AssertStockCon06ProjectionVisibilityAsync(
            mongoScope.Database,
            seeded,
            expectedStock: PostPurchaseStockQuantity - RequestedReturnBaseQuantity,
            expectedEvent: stockEvent,
            progressEntries);

        await ApplyStockCon06ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            stockEvent,
            progressEntries);
        await AssertStockCon06ProjectionVisibilityAsync(
            mongoScope.Database,
            seeded,
            expectedStock: PostPurchaseStockQuantity - RequestedReturnBaseQuantity,
            expectedEvent: stockEvent,
            progressEntries);

        var staleReplay = CloneStockCon06StockEventForStaleReplay(stockEvent);
        await ApplyStockCon06ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            staleReplay,
            progressEntries);
        await AssertStockCon06ProjectionVisibilityAsync(
            mongoScope.Database,
            seeded,
            expectedStock: PostPurchaseStockQuantity - RequestedReturnBaseQuantity,
            expectedEvent: stockEvent,
            progressEntries);

        var rebuildEvent = await BuildStockCon06SqlTruthRebuildEventAsync(
            scope,
            tenantContext,
            seeded,
            stockEvent);
        await ApplyStockCon06ProjectionAsync(
            connectionString,
            tenantContext,
            mongoConnectionString,
            mongoScope.Database,
            rebuildEvent,
            progressEntries);
        await AssertStockCon06ProjectionVisibilityAsync(
            mongoScope.Database,
            seeded,
            expectedStock: PostPurchaseStockQuantity - RequestedReturnBaseQuantity,
            expectedEvent: rebuildEvent,
            progressEntries);

        var reconciliation = await ExecuteStockCon06InventoryReconciliationAsync(
            connectionString,
            tenantContext);
        Assert.Equal("OK", reconciliation.Status);
        Assert.Equal(0, reconciliation.InvalidStockRowCount);
        Assert.Equal(0, reconciliation.DuplicateEventReplayCount);
        Assert.Equal(0, reconciliation.OpenReservationsOnCompletedOrdersCount);

        var finalSnapshot = await LoadPrCon01StateSnapshotAsync(scope, tenantContext, seeded);
        Assert.Equal(PostPurchaseStockQuantity - RequestedReturnBaseQuantity, finalSnapshot.StockQuantity);
        Assert.Equal(1, finalSnapshot.PurchaseReturnStockMovementCount);
        Assert.Equal(1, finalSnapshot.IdempotencyRecordCount);
        Assert.Equal(1, finalSnapshot.PurchaseReturnSupplierTransactionCount);
        Assert.Equal(1, finalSnapshot.PurchaseReturnJournalCount);
        Assert.True(finalSnapshot.TotalReturnedBaseQuantity <= HistoricalReceivedBaseQuantity);

        progressEntries.Enqueue("verification:stock-con-06-purchase-return-deduction-replay-converged");
    }

    [Fact]
    public async Task PR_CON_03_Duplicate_Purchase_Return_Worker_Delivery_During_Active_Processing_Converges_Safely()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out _, out _) ||
            databaseScope is null)
        {
            return;
        }

        using var scope = databaseScope;
        var tenantContext = new TestTenantContext(TenantId);

        await using var seedContext = scope.CreateDbContext(tenantContext);
        await EnsureReliabilityScenarioSchemaAsync(seedContext);
        var seeded = await SeedScenarioAsync(seedContext);
        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test database connection string was not available.");

        var progressEntries = new ConcurrentQueue<string>();
        var diagnosticLogs = progressEntries;
        var deferredAccounting = new Mock<ICommerceAccountingService>(MockBehavior.Strict);
        deferredAccounting
            .Setup(service => service.PostPurchaseReturnAsync(
                It.IsAny<Return>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostingResult.Failed("PR-CON-03 intentionally defers synchronous purchase-return posting to the outbox worker."));
        var canonicalActor = new PurchaseReturnActor(
            Name: "pr-con-03-authoritative-return",
            RequestId: "pr-con-03-authoritative-return",
            TraceIdentifier: "pr-con-03-authoritative-return",
            UserId: ActorAUserId);

        var createResult = await ExecuteReturnActorAsync(
            connectionString,
            tenantContext,
            seeded,
            canonicalActor,
            progressEntries,
            diagnosticLogs,
            "PR-CON-03 duplicate worker overlap validation",
            "pr-con-03-authoritative-return",
            commerceAccountingOverride: deferredAccounting.Object);

        Assert.Null(createResult.Exception);
        Assert.True(createResult.IsSuccess, createResult.ResponseBody ?? "Canonical PR-CON-03 purchase return did not succeed.");
        Assert.NotNull(createResult.Response);

        var returnId = createResult.Response!.ReturnId;
        var canonicalBaseline = await LoadPrCon03StateSnapshotAsync(scope, tenantContext, seeded, returnId);

        Assert.Equal(1, canonicalBaseline.PurchaseReturnCount);
        Assert.Equal(RequestedReturnBaseQuantity, canonicalBaseline.TotalReturnedBaseQuantity);
        Assert.Equal(PostPurchaseStockQuantity - RequestedReturnBaseQuantity, canonicalBaseline.StockQuantity);
        Assert.Equal(1, canonicalBaseline.PurchaseReturnStockMovementCount);
        Assert.Equal(1, canonicalBaseline.PurchaseReturnSupplierTransactionCount);
        Assert.Equal(2, canonicalBaseline.TotalSupplierTransactions);
        Assert.Equal(RequestedReturnBaseQuantity * UnitCost, canonicalBaseline.PurchaseReturnSupplierDebitTotal);
        Assert.Equal(-(HistoricalReceivedBaseQuantity * UnitCost) + (RequestedReturnBaseQuantity * UnitCost), canonicalBaseline.SupplierBalance);
        Assert.Equal(1, canonicalBaseline.PurchaseReturnSupplierLedgerCount);
        Assert.Equal(2, canonicalBaseline.TotalSupplierLedgerEntries);
        Assert.Equal(RequestedReturnBaseQuantity * UnitCost, canonicalBaseline.PurchaseReturnSupplierLedgerDebitTotal);
        Assert.Equal(1, canonicalBaseline.PurchaseReturnOutboxCount);
        Assert.NotEqual(Guid.Empty, canonicalBaseline.PurchaseReturnOutboxId);
        Assert.Null(canonicalBaseline.PurchaseReturnOutboxJournalId);
        Assert.Null(canonicalBaseline.PurchaseReturnOutboxClaimedUntilUtc);
        Assert.Null(canonicalBaseline.PurchaseReturnOutboxProcessedAtUtc);
        Assert.Equal(1, canonicalBaseline.PurchaseReturnOutboxAggregateVersion);
        Assert.Equal(1, canonicalBaseline.PurchaseReturnOutboxPayloadMutationVersion);
        Assert.Equal(seeded.InvoiceId, canonicalBaseline.PurchaseReturnOutboxPayloadOriginalInvoiceId);
        Assert.Equal(0, canonicalBaseline.PurchaseReturnJournalCount);
        Assert.Equal(0, canonicalBaseline.PurchaseReturnJournalLineCount);
        Assert.Equal(0m, canonicalBaseline.PurchaseReturnJournalTotalDebits);
        Assert.Equal(0m, canonicalBaseline.PurchaseReturnJournalTotalCredits);
        Assert.Empty(canonicalBaseline.PurchaseReturnJournalFingerprint);
        Assert.Equal(1, canonicalBaseline.IdempotencyRecordCount);
        Assert.Equal(BuildScopedIdempotencyKey(canonicalActor.RequestId), canonicalBaseline.IdempotencyKey);
        Assert.Equal(StatusCodes.Status200OK, canonicalBaseline.IdempotencyResponseStatusCode);
        Assert.NotNull(canonicalBaseline.IdempotencyCompletedAtUtc);
        Assert.Equal(1, canonicalBaseline.InvoiceMutationVersion);
        Assert.Equal(HistoricalReceivedBaseQuantity - RequestedReturnBaseQuantity, canonicalBaseline.RemainingReturnableQuantity);
        Assert.Equal(1, canonicalBaseline.StockSyncOutboxCount);
        Assert.NotEqual(Guid.Empty, canonicalBaseline.StockSyncOutboxId);
        Assert.NotEqual(Guid.Empty, canonicalBaseline.StockSyncEventId);
        Assert.True(canonicalBaseline.StockSyncEventVersion > 0);
        Assert.Null(canonicalBaseline.StockSyncClaimedUntilUtc);
        Assert.Null(canonicalBaseline.StockSyncPublishedAtUtc);
        Assert.Equal(0, canonicalBaseline.StockSyncAttemptCount);
        Assert.Equal(nameof(ProductVariantStockChangedEvent), canonicalBaseline.StockSyncEventType);
        Assert.Equal(0, canonicalBaseline.ProcessedEvents);
        Assert.Equal(0, canonicalBaseline.InventoryEventInbox);

        var accountingWindow = new ActiveProcessingDuplicateWorkerWindow(
            timeout: TimeSpan.FromSeconds(30),
            progressEntries);
        var accountingWorkerA = new AccountingReplayWorker(
            Name: "purchase-return-accounting-worker-a",
            UserId: ActorAUserId);
        var accountingWorkerB = new AccountingReplayWorker(
            Name: "purchase-return-accounting-worker-b",
            UserId: ActorBUserId);

        using var accountingWorkerAServices = BuildAccountingOutboxProcessorServiceProvider(
            connectionString,
            tenantContext,
            new PurchaseReturnAccountingOutboxCommitPauseInterceptor(accountingWorkerA.Name, accountingWindow));
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
        await AssertPurchaseReturnAccountingWorkerClaimVisibleDuringActiveProcessingAsync(
            scope,
            tenantContext,
            seeded,
            returnId,
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
                $"PR-CON-03 purchase-return duplicate worker did not complete while the original worker held the return lock. Progress: {string.Join(" | ", progressEntries)}");
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
                $"PR-CON-03 purchase-return accounting overlap timed out. Progress: {string.Join(" | ", progressEntries)}");
        }

        await AssertPurchaseReturnAccountingDuplicateWorkerConvergenceAsync(
            scope,
            tenantContext,
            seeded,
            returnId,
            canonicalBaseline,
            originalAccountingWorkerResult,
            duplicateAccountingWorkerResult,
            progressEntries);

        var postAccountingBaseline = await LoadPrCon03StateSnapshotAsync(scope, tenantContext, seeded, returnId);
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
            "purchase-return-stock-sync-worker-a",
            progressEntries);

        await stockSyncPublishWindow.WaitForFirstPublishAsync();
        await AssertPurchaseReturnStockSyncClaimVisibleDuringActivePublishAsync(
            scope,
            tenantContext,
            seeded,
            returnId,
            postAccountingBaseline,
            progressEntries);

        var stockSyncWorkerBTask = ExecuteStockSyncWorkerAsync(
            stockSyncProcessor,
            "purchase-return-stock-sync-worker-b",
            progressEntries);

        StockSyncWorkerResult duplicateStockSyncWorkerResult;
        try
        {
            duplicateStockSyncWorkerResult = await stockSyncWorkerBTask.WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            throw new XunitException(
                $"PR-CON-03 purchase-return stock-sync duplicate worker did not complete while the original publish was paused. Progress: {string.Join(" | ", progressEntries)}");
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
                $"PR-CON-03 purchase-return stock-sync original worker did not finish after publish release. Progress: {string.Join(" | ", progressEntries)}");
        }

        await AssertPurchaseReturnStockSyncDuplicateWorkerConvergenceAsync(
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

    private static async Task<PrCon01StateSnapshot> LoadPrCon01StateSnapshotAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded)
    {
        await using var context = scope.CreateDbContext(tenantContext);

        var purchaseReturns = await context.Returns
            .Where(item =>
                item.TenantId == seeded.TenantId &&
                item.ReturnType == ReturnType.PurchaseReturn &&
                item.OriginalPurchaseInvoiceId == seeded.InvoiceId)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync();
        var purchaseReturnIds = purchaseReturns.Select(item => item.Id).ToList();
        var returnItems = await context.ReturnItems
            .Where(item => purchaseReturnIds.Contains(item.ReturnId))
            .ToListAsync();
        var stockMovements = await context.StockMovements
            .Where(movement =>
                movement.TenantId == seeded.TenantId &&
                movement.MovementType == StockMovementType.PurchaseReturn &&
                movement.ReferenceId.HasValue &&
                purchaseReturnIds.Contains(movement.ReferenceId.Value))
            .OrderBy(movement => movement.CreatedAt)
            .ThenBy(movement => movement.Id)
            .ToListAsync();
        var supplierTransactions = await context.SupplierTransactions
            .Where(transaction =>
                transaction.TenantId == seeded.TenantId &&
                transaction.SupplierId == seeded.SupplierId)
            .OrderBy(transaction => transaction.TransactionDate)
            .ThenBy(transaction => transaction.CreatedAt)
            .ThenBy(transaction => transaction.Id)
            .ToListAsync();
        var supplierLedgers = await context.AccountLedgers
            .Where(entry =>
                entry.TenantId == seeded.TenantId &&
                entry.PartyType == PartyType.Supplier &&
                entry.PartyId == seeded.SupplierId)
            .OrderBy(entry => entry.TransactionDate)
            .ThenBy(entry => entry.CreatedAt)
            .ThenBy(entry => entry.Id)
            .ToListAsync();
        var outboxMessages = await context.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .Where(message =>
                message.TenantId == seeded.TenantId &&
                message.EventType == "PurchaseReturn" &&
                purchaseReturnIds.Contains(message.BusinessEntityId))
            .OrderBy(message => message.CreatedAt)
            .ToListAsync();
        var journalEntries = await context.JournalEntries
            .IgnoreQueryFilters()
            .Include(journal => journal.LedgerEntries)
            .Where(journal =>
                journal.TenantId == seeded.TenantId &&
                journal.SourceType == "PurchaseReturn" &&
                purchaseReturnIds.Contains(journal.SourceId))
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
        var idempotencyRecords = await context.PurchaseReturnRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .Where(record =>
                record.TenantId == seeded.TenantId &&
                record.ReturnId.HasValue)
            .OrderBy(record => record.CreatedAt)
            .ToListAsync();
        var supplier = await context.Suppliers
            .SingleAsync(item => item.TenantId == seeded.TenantId && item.Id == seeded.SupplierId);
        var invoice = await context.PurchaseInvoices
            .SingleAsync(item => item.TenantId == seeded.TenantId && item.Id == seeded.InvoiceId);
        var stock = await context.ProductVariantStocks
            .SingleAsync(item =>
                item.TenantId == seeded.TenantId &&
                item.WarehouseId == seeded.WarehouseId &&
                item.VariantId == seeded.VariantId);

        var purchaseReturnOutbox = outboxMessages.SingleOrDefault();
        var purchaseReturnJournal = journalEntries.SingleOrDefault();
        var stockMovement = stockMovements.SingleOrDefault();
        var stockSyncOutbox = stockSyncOutboxMessages.SingleOrDefault();
        var idempotencyRecord = idempotencyRecords.SingleOrDefault();
        Guid? originalInvoiceId = null;
        int? payloadMutationVersion = null;

        if (purchaseReturnOutbox != null)
        {
            using var payload = JsonDocument.Parse(purchaseReturnOutbox.PayloadJson);
            if (payload.RootElement.TryGetProperty("OriginalInvoiceId", out var originalInvoiceIdElement) &&
                originalInvoiceIdElement.ValueKind == JsonValueKind.String)
            {
                originalInvoiceId = originalInvoiceIdElement.GetGuid();
            }

            if (payload.RootElement.TryGetProperty("MutationVersion", out var mutationVersionElement) &&
                mutationVersionElement.ValueKind == JsonValueKind.Number)
            {
                payloadMutationVersion = mutationVersionElement.GetInt32();
            }
        }

        return new PrCon01StateSnapshot(
            PurchaseReturnCount: purchaseReturns.Count,
            TotalReturnedBaseQuantity: returnItems.Sum(item => item.BaseQuantity),
            StockQuantity: stock.Quantity,
            PurchaseReturnStockMovementCount: stockMovements.Count,
            StockMovementQuantityAfter: stockMovement?.QuantityAfter ?? 0m,
            StockMovementQuantityChange: stockMovement?.QuantityChange ?? 0m,
            PurchaseReturnSupplierTransactionCount: supplierTransactions.Count(item => item.TransactionType == TransactionType.PurchaseReturn),
            TotalSupplierTransactions: supplierTransactions.Count,
            PurchaseReturnSupplierDebitTotal: supplierTransactions
                .Where(item => item.TransactionType == TransactionType.PurchaseReturn)
                .Sum(item => item.DebitAmount),
            SupplierBalance: supplier.Balance,
            PurchaseReturnSupplierLedgerCount: supplierLedgers.Count(item => item.TransactionType == TransactionType.PurchaseReturn),
            TotalSupplierLedgerEntries: supplierLedgers.Count,
            PurchaseReturnSupplierLedgerDebitTotal: supplierLedgers
                .Where(item => item.TransactionType == TransactionType.PurchaseReturn)
                .Sum(item => item.DebitAmount),
            PurchaseReturnOutboxCount: outboxMessages.Count,
            PurchaseReturnOutboxId: purchaseReturnOutbox?.Id ?? Guid.Empty,
            PurchaseReturnOutboxJournalId: purchaseReturnOutbox?.JournalId,
            PurchaseReturnOutboxClaimedUntilUtc: purchaseReturnOutbox?.ClaimedUntilUtc,
            PurchaseReturnOutboxProcessedAtUtc: purchaseReturnOutbox?.ProcessedAtUtc,
            PurchaseReturnOutboxAggregateVersion: purchaseReturnOutbox?.AggregateVersion ?? 0,
            PurchaseReturnOutboxPayloadMutationVersion: payloadMutationVersion ?? 0,
            PurchaseReturnOutboxPayloadOriginalInvoiceId: originalInvoiceId,
            PurchaseReturnJournalCount: journalEntries.Count,
            PurchaseReturnJournalLineCount: purchaseReturnJournal?.LedgerEntries.Count ?? 0,
            PurchaseReturnJournalTotalDebits: purchaseReturnJournal?.LedgerEntries.Sum(line => line.DebitAmount) ?? 0m,
            PurchaseReturnJournalTotalCredits: purchaseReturnJournal?.LedgerEntries.Sum(line => line.CreditAmount) ?? 0m,
            PurchaseReturnJournalFingerprint: string.Join(
                "|",
                journalEntries.Select(journal => $"{journal.Id}:{journal.IdempotencyKey}")
                    .OrderBy(value => value, StringComparer.Ordinal)),
            IdempotencyRecordCount: idempotencyRecords.Count,
            IdempotencyKey: idempotencyRecord?.Key,
            IdempotencyResponseStatusCode: idempotencyRecord?.ResponseStatusCode,
            IdempotencyResponseJson: idempotencyRecord?.ResponseJson,
            IdempotencyCompletedAtUtc: idempotencyRecord?.CompletedAtUtc,
            InvoiceMutationVersion: invoice.AccountingMutationVersion,
            RemainingReturnableQuantity: HistoricalReceivedBaseQuantity - returnItems.Sum(item => item.BaseQuantity),
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
                .CountAsync(processed =>
                    processed.TenantId == seeded.TenantId &&
                    !VariantStockProjectionFenceConsumerNames.Contains(processed.ConsumerName)),
            InventoryEventInbox: await context.InventoryEventInbox
                .IgnoreQueryFilters()
                .CountAsync(inbox => inbox.TenantId == seeded.TenantId));
    }

    private static Task<PrCon01StateSnapshot> LoadPrCon03StateSnapshotAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid returnId)
    {
        _ = returnId;
        return LoadPrCon01StateSnapshotAsync(scope, tenantContext, seeded);
    }

    private static async Task ReopenPurchaseReturnAccountingOutboxMessageForWorkerReplayAsync(
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
                message.EventType == "PurchaseReturn" &&
                message.BusinessEntityId == returnId);

        outboxMessage.ProcessedAtUtc = null;
        outboxMessage.ClaimedUntilUtc = null;
        outboxMessage.IsFailed = false;
        outboxMessage.LastError = null;
        outboxMessage.NextRetryAtUtc = null;
        outboxMessage.AttemptCount = 0;

        await context.SaveChangesAsync();
        progressEntries.Enqueue("setup:reopened-purchase-return-accounting-outbox-row-for-real-worker-overlap");
    }

    private static async Task AssertPurchaseReturnAccountingWorkerClaimVisibleDuringActiveProcessingAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid returnId,
        PrCon01StateSnapshot canonicalBaseline,
        ConcurrentQueue<string> progressEntries)
    {
        var visibleState = await LoadPrCon03StateSnapshotAsync(scope, tenantContext, seeded, returnId);

        Assert.Equal(canonicalBaseline.PurchaseReturnCount, visibleState.PurchaseReturnCount);
        Assert.Equal(canonicalBaseline.TotalReturnedBaseQuantity, visibleState.TotalReturnedBaseQuantity);
        Assert.Equal(canonicalBaseline.StockQuantity, visibleState.StockQuantity);
        Assert.Equal(canonicalBaseline.PurchaseReturnStockMovementCount, visibleState.PurchaseReturnStockMovementCount);
        Assert.Equal(canonicalBaseline.StockMovementQuantityAfter, visibleState.StockMovementQuantityAfter);
        Assert.Equal(canonicalBaseline.StockMovementQuantityChange, visibleState.StockMovementQuantityChange);
        Assert.Equal(canonicalBaseline.PurchaseReturnSupplierTransactionCount, visibleState.PurchaseReturnSupplierTransactionCount);
        Assert.Equal(canonicalBaseline.TotalSupplierTransactions, visibleState.TotalSupplierTransactions);
        Assert.Equal(canonicalBaseline.PurchaseReturnSupplierDebitTotal, visibleState.PurchaseReturnSupplierDebitTotal);
        Assert.Equal(canonicalBaseline.SupplierBalance, visibleState.SupplierBalance);
        Assert.Equal(canonicalBaseline.PurchaseReturnSupplierLedgerCount, visibleState.PurchaseReturnSupplierLedgerCount);
        Assert.Equal(canonicalBaseline.TotalSupplierLedgerEntries, visibleState.TotalSupplierLedgerEntries);
        Assert.Equal(canonicalBaseline.PurchaseReturnSupplierLedgerDebitTotal, visibleState.PurchaseReturnSupplierLedgerDebitTotal);
        Assert.Equal(canonicalBaseline.PurchaseReturnOutboxCount, visibleState.PurchaseReturnOutboxCount);
        Assert.Equal(canonicalBaseline.PurchaseReturnOutboxId, visibleState.PurchaseReturnOutboxId);
        Assert.Equal(canonicalBaseline.PurchaseReturnOutboxJournalId, visibleState.PurchaseReturnOutboxJournalId);
        Assert.Null(visibleState.PurchaseReturnOutboxProcessedAtUtc);
        Assert.NotNull(visibleState.PurchaseReturnOutboxClaimedUntilUtc);
        Assert.Equal(canonicalBaseline.PurchaseReturnOutboxAggregateVersion, visibleState.PurchaseReturnOutboxAggregateVersion);
        Assert.Equal(canonicalBaseline.PurchaseReturnOutboxPayloadMutationVersion, visibleState.PurchaseReturnOutboxPayloadMutationVersion);
        Assert.Equal(canonicalBaseline.PurchaseReturnOutboxPayloadOriginalInvoiceId, visibleState.PurchaseReturnOutboxPayloadOriginalInvoiceId);
        Assert.Equal(canonicalBaseline.PurchaseReturnJournalCount, visibleState.PurchaseReturnJournalCount);
        Assert.Equal(canonicalBaseline.PurchaseReturnJournalLineCount, visibleState.PurchaseReturnJournalLineCount);
        Assert.Equal(canonicalBaseline.PurchaseReturnJournalTotalDebits, visibleState.PurchaseReturnJournalTotalDebits);
        Assert.Equal(canonicalBaseline.PurchaseReturnJournalTotalCredits, visibleState.PurchaseReturnJournalTotalCredits);
        Assert.Equal(canonicalBaseline.PurchaseReturnJournalFingerprint, visibleState.PurchaseReturnJournalFingerprint);
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
        Assert.Equal(canonicalBaseline.StockSyncAttemptCount, visibleState.StockSyncAttemptCount);
        Assert.Equal(canonicalBaseline.StockSyncEventType, visibleState.StockSyncEventType);
        Assert.Equal(canonicalBaseline.ProcessedEvents, visibleState.ProcessedEvents);
        Assert.Equal(canonicalBaseline.InventoryEventInbox, visibleState.InventoryEventInbox);

        progressEntries.Enqueue("verification:purchase-return-accounting-worker-claim-visible-during-active-processing");
    }

    private static async Task AssertPurchaseReturnAccountingDuplicateWorkerConvergenceAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid returnId,
        PrCon01StateSnapshot canonicalBaseline,
        AccountingOutboxWorkerResult originalWorker,
        AccountingOutboxWorkerResult duplicateWorker,
        ConcurrentQueue<string> progressEntries)
    {
        var after = await LoadPrCon03StateSnapshotAsync(scope, tenantContext, seeded, returnId);

        Assert.Null(originalWorker.Exception);
        Assert.Null(duplicateWorker.Exception);
        Assert.Equal(1, originalWorker.ProcessedCount);
        Assert.Equal(0, duplicateWorker.ProcessedCount);

        Assert.Equal(canonicalBaseline.PurchaseReturnCount, after.PurchaseReturnCount);
        Assert.Equal(canonicalBaseline.TotalReturnedBaseQuantity, after.TotalReturnedBaseQuantity);
        Assert.Equal(canonicalBaseline.StockQuantity, after.StockQuantity);
        Assert.Equal(canonicalBaseline.PurchaseReturnStockMovementCount, after.PurchaseReturnStockMovementCount);
        Assert.Equal(canonicalBaseline.StockMovementQuantityAfter, after.StockMovementQuantityAfter);
        Assert.Equal(canonicalBaseline.StockMovementQuantityChange, after.StockMovementQuantityChange);
        Assert.Equal(canonicalBaseline.PurchaseReturnSupplierTransactionCount, after.PurchaseReturnSupplierTransactionCount);
        Assert.Equal(canonicalBaseline.TotalSupplierTransactions, after.TotalSupplierTransactions);
        Assert.Equal(canonicalBaseline.PurchaseReturnSupplierDebitTotal, after.PurchaseReturnSupplierDebitTotal);
        Assert.Equal(canonicalBaseline.SupplierBalance, after.SupplierBalance);
        Assert.Equal(canonicalBaseline.PurchaseReturnSupplierLedgerCount, after.PurchaseReturnSupplierLedgerCount);
        Assert.Equal(canonicalBaseline.TotalSupplierLedgerEntries, after.TotalSupplierLedgerEntries);
        Assert.Equal(canonicalBaseline.PurchaseReturnSupplierLedgerDebitTotal, after.PurchaseReturnSupplierLedgerDebitTotal);
        Assert.Equal(canonicalBaseline.PurchaseReturnOutboxCount, after.PurchaseReturnOutboxCount);
        Assert.Equal(canonicalBaseline.PurchaseReturnOutboxId, after.PurchaseReturnOutboxId);
        Assert.NotNull(after.PurchaseReturnOutboxJournalId);
        Assert.Null(after.PurchaseReturnOutboxClaimedUntilUtc);
        Assert.NotNull(after.PurchaseReturnOutboxProcessedAtUtc);
        Assert.Equal(canonicalBaseline.PurchaseReturnOutboxAggregateVersion, after.PurchaseReturnOutboxAggregateVersion);
        Assert.Equal(canonicalBaseline.PurchaseReturnOutboxPayloadMutationVersion, after.PurchaseReturnOutboxPayloadMutationVersion);
        Assert.Equal(canonicalBaseline.PurchaseReturnOutboxPayloadOriginalInvoiceId, after.PurchaseReturnOutboxPayloadOriginalInvoiceId);
        Assert.Equal(1, after.PurchaseReturnJournalCount);
        Assert.Equal(2, after.PurchaseReturnJournalLineCount);
        Assert.Equal(RequestedReturnBaseQuantity * UnitCost, after.PurchaseReturnJournalTotalDebits);
        Assert.Equal(RequestedReturnBaseQuantity * UnitCost, after.PurchaseReturnJournalTotalCredits);
        Assert.NotEmpty(after.PurchaseReturnJournalFingerprint);
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
        Assert.Equal(canonicalBaseline.StockSyncClaimedUntilUtc, after.StockSyncClaimedUntilUtc);
        Assert.Equal(canonicalBaseline.StockSyncPublishedAtUtc, after.StockSyncPublishedAtUtc);
        Assert.Equal(canonicalBaseline.StockSyncAttemptCount, after.StockSyncAttemptCount);
        Assert.Equal(canonicalBaseline.StockSyncEventType, after.StockSyncEventType);
        Assert.Equal(canonicalBaseline.ProcessedEvents, after.ProcessedEvents);
        Assert.Equal(canonicalBaseline.InventoryEventInbox, after.InventoryEventInbox);

        progressEntries.Enqueue("verification:pr-con-03-purchase-return-worker-overlap-converged");
    }

    private static async Task AssertPurchaseReturnStockSyncClaimVisibleDuringActivePublishAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid returnId,
        PrCon01StateSnapshot baseline,
        ConcurrentQueue<string> progressEntries)
    {
        var duringPublish = await LoadPrCon03StateSnapshotAsync(scope, tenantContext, seeded, returnId);

        Assert.Equal(baseline.PurchaseReturnCount, duringPublish.PurchaseReturnCount);
        Assert.Equal(baseline.TotalReturnedBaseQuantity, duringPublish.TotalReturnedBaseQuantity);
        Assert.Equal(baseline.StockQuantity, duringPublish.StockQuantity);
        Assert.Equal(baseline.PurchaseReturnStockMovementCount, duringPublish.PurchaseReturnStockMovementCount);
        Assert.Equal(baseline.StockMovementQuantityAfter, duringPublish.StockMovementQuantityAfter);
        Assert.Equal(baseline.StockMovementQuantityChange, duringPublish.StockMovementQuantityChange);
        Assert.Equal(baseline.PurchaseReturnSupplierTransactionCount, duringPublish.PurchaseReturnSupplierTransactionCount);
        Assert.Equal(baseline.TotalSupplierTransactions, duringPublish.TotalSupplierTransactions);
        Assert.Equal(baseline.PurchaseReturnSupplierDebitTotal, duringPublish.PurchaseReturnSupplierDebitTotal);
        Assert.Equal(baseline.SupplierBalance, duringPublish.SupplierBalance);
        Assert.Equal(baseline.PurchaseReturnSupplierLedgerCount, duringPublish.PurchaseReturnSupplierLedgerCount);
        Assert.Equal(baseline.TotalSupplierLedgerEntries, duringPublish.TotalSupplierLedgerEntries);
        Assert.Equal(baseline.PurchaseReturnSupplierLedgerDebitTotal, duringPublish.PurchaseReturnSupplierLedgerDebitTotal);
        Assert.Equal(baseline.PurchaseReturnOutboxCount, duringPublish.PurchaseReturnOutboxCount);
        Assert.Equal(baseline.PurchaseReturnOutboxId, duringPublish.PurchaseReturnOutboxId);
        Assert.Equal(baseline.PurchaseReturnOutboxJournalId, duringPublish.PurchaseReturnOutboxJournalId);
        Assert.Equal(baseline.PurchaseReturnOutboxProcessedAtUtc, duringPublish.PurchaseReturnOutboxProcessedAtUtc);
        Assert.Equal(baseline.PurchaseReturnOutboxAggregateVersion, duringPublish.PurchaseReturnOutboxAggregateVersion);
        Assert.Equal(baseline.PurchaseReturnOutboxPayloadMutationVersion, duringPublish.PurchaseReturnOutboxPayloadMutationVersion);
        Assert.Equal(baseline.PurchaseReturnOutboxPayloadOriginalInvoiceId, duringPublish.PurchaseReturnOutboxPayloadOriginalInvoiceId);
        Assert.Equal(baseline.PurchaseReturnJournalCount, duringPublish.PurchaseReturnJournalCount);
        Assert.Equal(baseline.PurchaseReturnJournalLineCount, duringPublish.PurchaseReturnJournalLineCount);
        Assert.Equal(baseline.PurchaseReturnJournalTotalDebits, duringPublish.PurchaseReturnJournalTotalDebits);
        Assert.Equal(baseline.PurchaseReturnJournalTotalCredits, duringPublish.PurchaseReturnJournalTotalCredits);
        Assert.Equal(baseline.PurchaseReturnJournalFingerprint, duringPublish.PurchaseReturnJournalFingerprint);
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

        progressEntries.Enqueue("verification:purchase-return-stock-sync-claim-visible-during-active-publish");
    }

    private static async Task AssertPurchaseReturnStockSyncDuplicateWorkerConvergenceAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid returnId,
        PrCon01StateSnapshot accountingBaseline,
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

        var after = await LoadPrCon03StateSnapshotAsync(scope, tenantContext, seeded, returnId);

        Assert.Equal(accountingBaseline.PurchaseReturnCount, after.PurchaseReturnCount);
        Assert.Equal(accountingBaseline.TotalReturnedBaseQuantity, after.TotalReturnedBaseQuantity);
        Assert.Equal(accountingBaseline.StockQuantity, after.StockQuantity);
        Assert.Equal(accountingBaseline.PurchaseReturnStockMovementCount, after.PurchaseReturnStockMovementCount);
        Assert.Equal(accountingBaseline.StockMovementQuantityAfter, after.StockMovementQuantityAfter);
        Assert.Equal(accountingBaseline.StockMovementQuantityChange, after.StockMovementQuantityChange);
        Assert.Equal(accountingBaseline.PurchaseReturnSupplierTransactionCount, after.PurchaseReturnSupplierTransactionCount);
        Assert.Equal(accountingBaseline.TotalSupplierTransactions, after.TotalSupplierTransactions);
        Assert.Equal(accountingBaseline.PurchaseReturnSupplierDebitTotal, after.PurchaseReturnSupplierDebitTotal);
        Assert.Equal(accountingBaseline.SupplierBalance, after.SupplierBalance);
        Assert.Equal(accountingBaseline.PurchaseReturnSupplierLedgerCount, after.PurchaseReturnSupplierLedgerCount);
        Assert.Equal(accountingBaseline.TotalSupplierLedgerEntries, after.TotalSupplierLedgerEntries);
        Assert.Equal(accountingBaseline.PurchaseReturnSupplierLedgerDebitTotal, after.PurchaseReturnSupplierLedgerDebitTotal);
        Assert.Equal(accountingBaseline.PurchaseReturnOutboxCount, after.PurchaseReturnOutboxCount);
        Assert.Equal(accountingBaseline.PurchaseReturnOutboxId, after.PurchaseReturnOutboxId);
        Assert.Equal(accountingBaseline.PurchaseReturnOutboxJournalId, after.PurchaseReturnOutboxJournalId);
        Assert.Equal(accountingBaseline.PurchaseReturnOutboxProcessedAtUtc, after.PurchaseReturnOutboxProcessedAtUtc);
        Assert.Equal(accountingBaseline.PurchaseReturnOutboxAggregateVersion, after.PurchaseReturnOutboxAggregateVersion);
        Assert.Equal(accountingBaseline.PurchaseReturnOutboxPayloadMutationVersion, after.PurchaseReturnOutboxPayloadMutationVersion);
        Assert.Equal(accountingBaseline.PurchaseReturnOutboxPayloadOriginalInvoiceId, after.PurchaseReturnOutboxPayloadOriginalInvoiceId);
        Assert.Equal(accountingBaseline.PurchaseReturnJournalCount, after.PurchaseReturnJournalCount);
        Assert.Equal(accountingBaseline.PurchaseReturnJournalLineCount, after.PurchaseReturnJournalLineCount);
        Assert.Equal(accountingBaseline.PurchaseReturnJournalTotalDebits, after.PurchaseReturnJournalTotalDebits);
        Assert.Equal(accountingBaseline.PurchaseReturnJournalTotalCredits, after.PurchaseReturnJournalTotalCredits);
        Assert.Equal(accountingBaseline.PurchaseReturnJournalFingerprint, after.PurchaseReturnJournalFingerprint);
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

        progressEntries.Enqueue("verification:pr-con-03-stock-sync-overlap-converged");
    }

    private static async Task<SeededScenario> SeedScenarioAsync(ApplicationDbContext context)
    {
        var supplierBalanceAfterPurchase = -(HistoricalReceivedBaseQuantity * UnitCost);
        var purchaseInvoice = new PurchaseInvoice
        {
            Id = InvoiceId,
            TenantId = TenantId,
            InvoiceNumber = "PINV-PR-CON-01",
            InvoiceDate = SeededUtc,
            SupplierId = SupplierId,
            WarehouseId = WarehouseId,
            Status = InvoiceStatus.Completed,
            AccountingMutationVersion = 0,
            Subtotal = HistoricalReceivedBaseQuantity * UnitCost,
            DiscountAmount = 0m,
            DiscountPercentage = 0m,
            TaxAmount = 0m,
            TotalAmount = HistoricalReceivedBaseQuantity * UnitCost,
            PaidAmount = 0m,
            PaymentMethod = PointOfSale.Core.Enums.PaymentMethod.Credit,
            Notes = "PR-CON-01 authoritative purchase invoice",
            CreatedAt = SeededUtc
        };

        purchaseInvoice.Items.Add(new PurchaseInvoiceItem
        {
            Id = InvoiceItemId,
            PurchaseInvoiceId = InvoiceId,
            ProductId = ProductId,
            ProductVariantId = VariantId,
            UnitId = UnitId,
            Quantity = HistoricalReceivedBaseQuantity,
            ConversionFactor = 1m,
            BaseQuantity = HistoricalReceivedBaseQuantity,
            UnitPrice = UnitCost,
            DiscountAmount = 0m,
            TaxRate = 0m,
            TaxAmount = 0m,
            TotalAmount = HistoricalReceivedBaseQuantity * UnitCost,
            CreatedAt = SeededUtc
        });

        context.Tenants.Add(new Tenant
        {
            Id = TenantId,
            Name = "PR-CON-01 Tenant",
            Slug = "pr-con-01-store",
            StorefrontBaseUrl = "https://pr-con-01-store.example.test",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });

        context.Categories.Add(new Category
        {
            Id = CategoryId,
            TenantId = TenantId,
            Name = "PR-CON-01 Category",
            CreatedAt = SeededUtc
        });

        context.Suppliers.Add(new Supplier
        {
            Id = SupplierId,
            TenantId = TenantId,
            Name = "PR-CON-01 Supplier",
            Balance = supplierBalanceAfterPurchase,
            CreatedAt = SeededUtc
        });

        context.Warehouses.Add(new Warehouse
        {
            Id = WarehouseId,
            TenantId = TenantId,
            Name = "PR-CON-01 Warehouse",
            Code = "WH-PR-CON-01",
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
            Name = "PR-CON-01 Product",
            Slug = "stock-con-06-product",
            CategoryId = CategoryId,
            PurchasePrice = UnitCost,
            SalePrice = UnitCost * 2m,
            OnlinePrice = UnitCost * 2m,
            PriceIncludesTax = false,
            TaxRate = 0m,
            Unit = "Piece",
            IsActive = true,
            IsOnlineVisible = true,
            TrackStock = true,
            CreatedAt = SeededUtc
        });

        context.ProductVariants.Add(new ProductVariant
        {
            Id = VariantId,
            TenantId = TenantId,
            ProductId = ProductId,
            SKU = "PR-CON-01-SKU",
            Price = UnitCost * 2m,
            OnlinePrice = UnitCost * 2m,
            CostPrice = UnitCost,
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
            SellingPrice = UnitCost * 2m,
            PurchasePrice = UnitCost,
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
            Quantity = PostPurchaseStockQuantity,
            ReservedQuantity = 0m,
            DamagedQuantity = 0m,
            CreatedAt = SeededUtc
        });

        context.StockMovements.AddRange(
            new StockMovement
            {
                Id = OpeningStockMovementId,
                TenantId = TenantId,
                WarehouseId = WarehouseId,
                ProductId = ProductId,
                ProductVariantId = VariantId,
                MovementType = StockMovementType.InitialStock,
                UnitId = UnitId,
                ConversionFactor = 1m,
                QuantityChange = PreexistingStockQuantity,
                QuantityAfter = PreexistingStockQuantity,
                ReferenceNumber = "PR-CON-01-INITIAL-STOCK",
                Price = UnitCost,
                Notes = "Authoritative preexisting stock before the historical purchase invoice",
                CreatedAt = SeededUtc.AddMinutes(-2)
            },
            new StockMovement
            {
                Id = PurchaseStockMovementId,
                TenantId = TenantId,
                WarehouseId = WarehouseId,
                ProductId = ProductId,
                ProductVariantId = VariantId,
                MovementType = StockMovementType.Purchase,
                UnitId = UnitId,
                ConversionFactor = 1m,
                QuantityChange = HistoricalReceivedBaseQuantity,
                QuantityAfter = PostPurchaseStockQuantity,
                ReferenceId = InvoiceId,
                ReferenceNumber = purchaseInvoice.InvoiceNumber,
                Price = UnitCost,
                Notes = "Authoritative purchase receipt",
                CreatedAt = SeededUtc
            });

        context.SupplierTransactions.Add(new SupplierTransaction
        {
            Id = PurchaseSupplierTransactionId,
            TenantId = TenantId,
            SupplierId = SupplierId,
            TransactionDate = SeededUtc,
            TransactionType = TransactionType.Purchase,
            DebitAmount = 0m,
            CreditAmount = HistoricalReceivedBaseQuantity * UnitCost,
            BalanceAfter = supplierBalanceAfterPurchase,
            ReferenceId = InvoiceId,
            ReferenceNumber = purchaseInvoice.InvoiceNumber,
            Notes = "Authoritative purchase payable",
            CreatedAt = SeededUtc
        });

        context.AccountLedgers.Add(new AccountLedger
        {
            Id = PurchaseSupplierLedgerId,
            TenantId = TenantId,
            PartyType = PartyType.Supplier,
            PartyId = SupplierId,
            TransactionDate = SeededUtc,
            TransactionType = TransactionType.Purchase,
            DocumentNumber = purchaseInvoice.InvoiceNumber,
            DebitAmount = 0m,
            CreditAmount = HistoricalReceivedBaseQuantity * UnitCost,
            RunningBalance = supplierBalanceAfterPurchase,
            ReferenceId = InvoiceId,
            Notes = "Authoritative purchase payable ledger",
            CreatedAt = SeededUtc
        });

        context.PurchaseInvoices.Add(purchaseInvoice);

        SeedRequiredLedgerAccounts(context);
        await context.SaveChangesAsync();

        return new SeededScenario(
            TenantId,
            SupplierId,
            WarehouseId,
            ProductId,
            VariantId,
            UnitId,
            InvoiceId);
    }

    private static void SeedRequiredLedgerAccounts(ApplicationDbContext context)
    {
        context.LedgerAccounts.AddRange(
            Account.Create(TenantId, AccountCodes.Inventory, "Inventory", AccountType.Asset, isSystem: true),
            Account.Create(TenantId, AccountCodes.SupplierPayables, "Supplier Payables", AccountType.Liability, isSystem: true));
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

    private static bool TryReadPurchaseReturnResponse(object? payload, out PurchaseReturnResponse? response)
    {
        response = payload as PurchaseReturnResponse;
        if (response != null)
        {
            return true;
        }

        if (payload is JsonElement jsonElement)
        {
            response = jsonElement.Deserialize<PurchaseReturnResponse>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return response != null;
        }

        if (payload != null)
        {
            response = JsonSerializer.Deserialize<PurchaseReturnResponse>(
                JsonSerializer.Serialize(payload),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }

        return response != null;
    }

    private static async Task AssertPurchaseReturnReplaySafetyAsync(
        string connectionString,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        PurchaseReturnActorResult original,
        ConcurrentQueue<string> progressEntries)
    {
        await using var replayContext = CreateDbContext(connectionString, tenantContext);
        await ConfigureSessionTimeoutsAsync(replayContext);

        var replayActor = new PurchaseReturnActor(
            Name: "post-commit-replay",
            RequestId: original.Actor.RequestId,
            TraceIdentifier: "pr-con-02-post-commit-replay",
            UserId: ActorAUserId);
        var controller = BuildPurchaseReturnsController(replayContext, tenantContext, replayActor, progressEntries);
        controller.Request.Headers["Idempotency-Key"] = original.Actor.RequestId;

        var replayResult = await controller.CreatePurchaseReturn(new CreatePurchaseReturnRequest
        {
            SupplierId = seeded.SupplierId,
            WarehouseId = seeded.WarehouseId,
            ReturnDate = ReturnRequestUtc,
            OriginalInvoiceId = seeded.InvoiceId,
            RefundAmount = RequestedReturnBaseQuantity * UnitCost,
            Reason = "PR-CON-02 replay overlap validation",
            Notes = "pr-con-02-shared-payload",
            Items =
            [
                new PurchaseReturnItemRequest
                {
                    ProductId = seeded.ProductId,
                    ProductVariantId = seeded.VariantId,
                    UnitId = seeded.UnitId,
                    Quantity = RequestedReturnBaseQuantity,
                    UnitPrice = UnitCost,
                    TaxRate = 0m,
                    Reason = "Concurrent purchase return against same authoritative purchase line"
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
            TryReadPurchaseReturnResponse(objectResult.Value, out var replayResponse),
            "Post-commit replay did not return a readable PurchaseReturnResponse payload.");
        Assert.NotNull(replayResponse);
        Assert.Equal(original.Response!.ReturnId, replayResponse!.ReturnId);
        Assert.Equal(original.Response.ReturnNumber, replayResponse.ReturnNumber);

        progressEntries.Enqueue("verification:post-commit-purchase-return-replay-returned-authoritative-result");
    }

    private static async Task AssertStockCon06NoPartialDeductionVisibilityAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        IMongoDatabase mongoDatabase,
        SeededScenario seeded,
        ConcurrentQueue<string> progressEntries)
    {
        await AssertNoUncommittedPurchaseReturnStateVisibleAsync(scope, tenantContext, seeded, progressEntries);

        var projection = await LoadStockCon06ProjectionVisibilityAsync(mongoDatabase, seeded);
        Assert.Equal(PostPurchaseStockQuantity, projection.StorefrontProductStockQuantity);
        Assert.Equal(PostPurchaseStockQuantity, projection.StorefrontVariantStockQuantity);
        Assert.Equal(PostPurchaseStockQuantity, projection.CommerceHubAvailableStock);
        Assert.True(projection.StorefrontProductInStock);
        Assert.True(projection.StorefrontVariantInStock);
        Assert.True(projection.CommerceHubInStock);
        Assert.True(projection.AffiliateVisible);
        Assert.Equal((int)PostPurchaseStockQuantity, projection.AffiliateTotalAvailableStock);
        Assert.True(projection.AffiliateInStock);

        progressEntries.Enqueue("verification:stock-con-06-no-partial-deduction-visibility");
    }

    private static async Task SeedStockCon06ReadModelsAsync(
        IMongoDatabase mongoDatabase,
        SeededScenario seeded)
    {
        var productDocument = new ProductDocument
        {
            TenantId = seeded.TenantId,
            ProductId = seeded.ProductId,
            Name = "STOCK-CON-06 Product",
            SKU = "STOCK-CON-06-SKU",
            Slug = "stock-con-06-product",
            Price = UnitCost * 2m,
            OnlinePrice = UnitCost * 2m,
            PurchasePrice = UnitCost,
            CategoryId = CategoryId,
            CategoryName = "PR-CON-01 Category",
            Images = "https://images.example.test/stock-con-06.png",
            IsOnlineVisible = true,
            IsActive = true,
            TrackStock = true,
            StockQuantity = (int)PostPurchaseStockQuantity,
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
                    SKU = "PR-CON-01-SKU",
                    Price = UnitCost * 2m,
                    OnlinePrice = UnitCost * 2m,
                    CostPrice = UnitCost,
                    IsDefault = true,
                    IsActive = true,
                    TotalStock = PostPurchaseStockQuantity,
                    SortOrder = 0,
                    SyncedAtUtc = SeededUtc,
                    LastEventVersion = 1
                }
            ]
        };

        var commerceHubDocument = new CommerceHubProductDocument
        {
            Id = Guid.Parse("7f0e2e17-90cc-4184-b676-d6aa20f14101"),
            TenantId = seeded.TenantId,
            StoreName = "STOCK-CON-06 Store",
            ProductId = seeded.ProductId,
            VariantId = seeded.VariantId,
            ProductName = productDocument.Name,
            Price = UnitCost * 2m,
            CategoryId = CategoryId,
            Currency = "EGP",
            StoreShippingFee = 0m,
            InStock = true,
            AvailableStock = PostPurchaseStockQuantity,
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
            ThumbnailUrl = "https://images.example.test/stock-con-06.png",
            Price = UnitCost * 2m,
            Currency = "EGP",
            StoreName = "PR-CON-01 Tenant",
            StoreSlug = "pr-con-01-store",
            CategoryName = "PR-CON-01 Category",
            HasVariants = false,
            TotalAvailableStock = (int)PostPurchaseStockQuantity,
            InStock = true,
            IsActive = true,
            IsAffiliateEnabled = true,
            AffiliateEnabledAtUtc = SeededUtc,
            TenantId = seeded.TenantId,
            StorefrontBaseUrl = "https://pr-con-01-store.example.test"
        };

        await mongoDatabase.GetCollection<ProductDocument>("products").InsertOneAsync(productDocument);
        await mongoDatabase.GetCollection<CommerceHubProductDocument>("commercehub_products").InsertOneAsync(commerceHubDocument);
        await mongoDatabase.GetCollection<PublicAffiliateProductDocument>("public_affiliate_products").InsertOneAsync(publicAffiliateDocument);
    }

    private static async Task<ProductVariantStockChangedEvent> LoadStockCon06PurchaseReturnStockEventAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        SeededScenario seeded,
        Guid returnId)
    {
        await using var context = scope.CreateDbContext(tenantContext);
        var message = await context.StockSyncOutboxMessages
            .IgnoreQueryFilters()
            .SingleAsync(item =>
                item.TenantId == seeded.TenantId &&
                item.VariantId == seeded.VariantId &&
                item.WarehouseId == seeded.WarehouseId);

        var stockEvent = JsonSerializer.Deserialize<ProductVariantStockChangedEvent>(message.PayloadJson)
            ?? throw new XunitException("STOCK-CON-06 purchase-return stock sync payload could not be deserialized.");

        Assert.Equal(PostPurchaseStockQuantity - RequestedReturnBaseQuantity, stockEvent.NewQuantity);
        Assert.Equal(PostPurchaseStockQuantity - RequestedReturnBaseQuantity, stockEvent.TotalStockQuantity);
        Assert.Equal("PurchaseReturn", stockEvent.Reason);
        Assert.Equal("PurchaseReturn", stockEvent.ReferenceType);
        Assert.Equal(returnId, stockEvent.ReferenceId);
        return stockEvent;
    }

    private static async Task ApplyStockCon06ProjectionAsync(
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
            Options.Create(new RedisSettings { InstanceName = "stock-con-06-test" }),
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
                PublicAffiliateProductsIndexName = "stock-con-06-affiliate-test"
            }),
            projectionContext,
            configuration,
            NullLogger<AffiliateProductPolicyProjectionConsumer>.Instance);

        await variantProjectionConsumer.Consume(BuildConsumeContext(stockEvent).Object);
        await productStockConsumer.Consume(BuildConsumeContext(stockEvent).Object);
        await commerceHubConsumer.Consume(BuildConsumeContext(stockEvent).Object);
        await affiliatePublicConsumer.Consume(BuildConsumeContext(stockEvent).Object);

        progressEntries.Enqueue($"projection:stock-con-06-applied:{stockEvent.EventId}");
    }

    private static async Task<StockCon06ProjectionVisibilitySnapshot> AssertStockCon06ProjectionVisibilityAsync(
        IMongoDatabase mongoDatabase,
        SeededScenario seeded,
        decimal expectedStock,
        ProductVariantStockChangedEvent expectedEvent,
        ConcurrentQueue<string> progressEntries)
    {
        var snapshot = await LoadStockCon06ProjectionVisibilityAsync(mongoDatabase, seeded);

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

        progressEntries.Enqueue("verification:stock-con-06-projection-visibility-converged");
        return snapshot;
    }

    private static async Task<StockCon06ProjectionVisibilitySnapshot> LoadStockCon06ProjectionVisibilityAsync(
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

        return new StockCon06ProjectionVisibilitySnapshot(
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

    private static ProductVariantStockChangedEvent CloneStockCon06StockEventForStaleReplay(
        ProductVariantStockChangedEvent current)
    {
        return new ProductVariantStockChangedEvent
        {
            EventId = Guid.Parse("7f0e2e17-90cc-4184-b676-d6aa20f14102"),
            TenantId = current.TenantId,
            VariantId = current.VariantId,
            ProductId = current.ProductId,
            SKU = current.SKU,
            WarehouseId = current.WarehouseId,
            WarehouseName = current.WarehouseName,
            OldQuantity = current.OldQuantity,
            NewQuantity = PostPurchaseStockQuantity,
            Reason = current.Reason,
            ReferenceId = current.ReferenceId,
            ReferenceType = current.ReferenceType,
            MutationSource = current.MutationSource,
            TotalStockQuantity = PostPurchaseStockQuantity,
            CorrelationId = current.CorrelationId,
            OccurredAtUtc = current.OccurredAtUtc.AddSeconds(-10),
            StockUpdatedAtUtc = current.StockUpdatedAtUtc.AddSeconds(-10),
            EventVersion = current.EventVersion
        };
    }

    private static async Task<ProductVariantStockChangedEvent> BuildStockCon06SqlTruthRebuildEventAsync(
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
            EventId = Guid.Parse("7f0e2e17-90cc-4184-b676-d6aa20f14103"),
            TenantId = seeded.TenantId,
            VariantId = seeded.VariantId,
            ProductId = seeded.ProductId,
            SKU = variant.SKU,
            WarehouseId = seeded.WarehouseId,
            WarehouseName = warehouse.Name,
            OldQuantity = Math.Max(0m, warehouseStock),
            NewQuantity = Math.Max(0m, warehouseStock),
            Reason = "StockCon06SqlTruthRebuild",
            ReferenceId = previousEvent.ReferenceId,
            ReferenceType = "StockCon06Rebuild",
            MutationSource = "StockCon06TestRebuild",
            TotalStockQuantity = Math.Max(0m, totalStock),
            CorrelationId = previousEvent.CorrelationId,
            OccurredAtUtc = rebuildTimestamp,
            StockUpdatedAtUtc = rebuildTimestamp,
            EventVersion = sqlEventVersion
        };
    }

    private static async Task<CommerceHubInventoryReconciliationResult> ExecuteStockCon06InventoryReconciliationAsync(
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

    private static async Task<StockCon06MongoDatabaseScope> CreateIsolatedMongoDatabaseScopeAsync(string scenarioKey)
    {
        var connectionString = GetRequiredMongoConnectionString();
        var client = new MongoClient(connectionString);
        var databaseName = $"stock_con_06_{TestConfiguration.BuildDeterministicSuffix($"{scenarioKey}_{Guid.NewGuid():N}")}";
        var database = client.GetDatabase(databaseName);

        await database.ListCollectionNamesAsync();

        return new StockCon06MongoDatabaseScope(client, databaseName, database);
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

        throw new InvalidOperationException("MongoDB connection string was not configured for STOCK-CON-06.");
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

    private static async Task EnsureReliabilityScenarioSchemaAsync(ApplicationDbContext context)
    {
        // Match the authoritative reliability suites so overlap tests fail only on
        // purchase-return behavior, not on local schema drift in reused integration databases.
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

    private static string BuildScopedIdempotencyKey(string requestId) =>
        $"SUPPLIER:{TenantId}:{requestId}";

    private static string DescribeException(Exception exception)
    {
        var chain = new List<string>();
        Exception? current = exception;
        while (current != null)
        {
            chain.Add($"{current.GetType().Name}:{current.Message}");
            current = current.InnerException;
        }

        return string.Join(" --> ", chain);
    }

    private sealed record SeededScenario(
        Guid TenantId,
        Guid SupplierId,
        Guid WarehouseId,
        Guid ProductId,
        Guid VariantId,
        Guid UnitId,
        Guid InvoiceId);

    private sealed record PurchaseReturnActor(
        string Name,
        string RequestId,
        string TraceIdentifier,
        Guid UserId);

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

    private sealed record PrCon01StateSnapshot(
        int PurchaseReturnCount,
        decimal TotalReturnedBaseQuantity,
        decimal StockQuantity,
        int PurchaseReturnStockMovementCount,
        decimal StockMovementQuantityAfter,
        decimal StockMovementQuantityChange,
        int PurchaseReturnSupplierTransactionCount,
        int TotalSupplierTransactions,
        decimal PurchaseReturnSupplierDebitTotal,
        decimal SupplierBalance,
        int PurchaseReturnSupplierLedgerCount,
        int TotalSupplierLedgerEntries,
        decimal PurchaseReturnSupplierLedgerDebitTotal,
        int PurchaseReturnOutboxCount,
        Guid PurchaseReturnOutboxId,
        Guid? PurchaseReturnOutboxJournalId,
        DateTime? PurchaseReturnOutboxClaimedUntilUtc,
        DateTime? PurchaseReturnOutboxProcessedAtUtc,
        int PurchaseReturnOutboxAggregateVersion,
        int PurchaseReturnOutboxPayloadMutationVersion,
        Guid? PurchaseReturnOutboxPayloadOriginalInvoiceId,
        int PurchaseReturnJournalCount,
        int PurchaseReturnJournalLineCount,
        decimal PurchaseReturnJournalTotalDebits,
        decimal PurchaseReturnJournalTotalCredits,
        string PurchaseReturnJournalFingerprint,
        int IdempotencyRecordCount,
        string? IdempotencyKey,
        int? IdempotencyResponseStatusCode,
        string? IdempotencyResponseJson,
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

    private sealed record StockCon06ProjectionVisibilitySnapshot(
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
        bool AffiliateInStock);

    private sealed class StockCon06MongoDatabaseScope : IAsyncDisposable
    {
        private readonly MongoClient _client;
        private readonly string _databaseName;

        public StockCon06MongoDatabaseScope(
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

    private sealed record PurchaseReturnActorResult(
        PurchaseReturnActor Actor,
        bool IsSuccess,
        int StatusCode,
        PurchaseReturnResponse? Response,
        string? ResponseBody,
        Exception? Exception)
    {
        public static PurchaseReturnActorResult Success(PurchaseReturnActor actor, PurchaseReturnResponse response) =>
            new(actor, true, StatusCodes.Status200OK, response, null, null);

        public static PurchaseReturnActorResult Failed(PurchaseReturnActor actor, int statusCode, string? responseBody) =>
            new(actor, false, statusCode, null, responseBody, null);

        public static PurchaseReturnActorResult Exceptional(PurchaseReturnActor actor, Exception exception) =>
            new(actor, false, StatusCodes.Status500InternalServerError, null, null, exception);
    }

    private sealed class PurchaseReturnCommitOverlapWindow
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

        public PurchaseReturnCommitOverlapWindow(
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
            _progressEntries.Enqueue($"{actorName}:commit-paused-before-purchase-return-visibility");
            _originalCommitPaused.TrySetResult(true);
        }

        public void MarkConflictingInvoiceLockAttempt(string actorName)
        {
            if (_originalCommitPaused.Task.IsCompletedSuccessfully)
            {
                Interlocked.Exchange(ref _originalCommitPausedBeforeConflictAttempt, 1);
            }

            _progressEntries.Enqueue($"{actorName}:attempted-purchase-invoice-lock-while-original-active");
            _conflictingInvoiceLockAttempted.TrySetResult(true);
        }

        public void MarkReplayClaimAttempt(string actorName)
        {
            if (_originalCommitPaused.Task.IsCompletedSuccessfully)
            {
                Interlocked.Exchange(ref _originalCommitPausedBeforeReplayClaim, 1);
            }

            _progressEntries.Enqueue($"{actorName}:attempted-purchase-return-idempotency-claim-while-original-active");
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
                    $"Timed out waiting for the original purchase return to pause before commit. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public async Task WaitForConflictingInvoiceLockAttemptAsync()
        {
            try
            {
                await _conflictingInvoiceLockAttempted.Task.WaitAsync(_timeout);
            }
            catch (TimeoutException)
            {
                throw new XunitException(
                    $"Timed out waiting for the conflicting purchase return to attempt the purchase invoice lock. Progress: {string.Join(" | ", _progressEntries)}");
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
                    $"Timed out waiting for Request B to attempt the overlapping purchase-return idempotency claim. Progress: {string.Join(" | ", _progressEntries)}");
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
                    $"Timed out waiting to release the original purchase return commit. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public void ReleaseOriginalCommit()
        {
            _progressEntries.Enqueue("orchestrator:release-original-purchase-return-commit");
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
                    $"Timed out waiting for the original purchase-return worker to pause before commit. Progress: {string.Join(" | ", _progressEntries)}");
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
                    $"Timed out waiting to release the original purchase-return worker. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public void ReleaseOriginalWorker()
        {
            _progressEntries.Enqueue("orchestrator:release-original-purchase-return-worker");
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
                    $"Timed out waiting to release the first purchase-return stock-sync publish. Progress: {string.Join(" | ", _progressEntries)}");
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
                    $"Timed out waiting for the first purchase-return stock-sync publish attempt. Progress: {string.Join(" | ", _progressEntries)}");
            }
        }

        public void ReleaseFirstPublish()
        {
            _progressEntries.Enqueue("orchestrator:release-first-purchase-return-stock-sync-publish");
            _releaseFirstPublish.TrySetResult(true);
        }
    }

    private sealed class PurchaseReturnCommitPauseInterceptor(
        string actorName,
        PurchaseReturnCommitOverlapWindow overlapWindow) : DbTransactionInterceptor
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
                // PR-CON-01 / PR-CON-02 critical window: the authoritative purchase return has
                // already staged the bounded historical reversal, stock decrement, supplier payable
                // reversal, accounting outbox intent, stock movement, and idempotency completion,
                // but PostgreSQL has not committed any of that durable state yet.
                overlapWindow.MarkOriginalCommitPaused(actorName);
                await overlapWindow.WaitUntilReleasedAsync(cancellationToken);
            }

            return await base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }

    private sealed class PurchaseReturnInvoiceLockAttemptInterceptor(
        string actorName,
        PurchaseReturnCommitOverlapWindow overlapWindow) : DbCommandInterceptor
    {
        private int _intercepted;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (IsPurchaseInvoiceForUpdateQuery(command.CommandText) &&
                Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0)
            {
                overlapWindow.MarkConflictingInvoiceLockAttempt(actorName);
            }

            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private static bool IsPurchaseInvoiceForUpdateQuery(string commandText)
        {
            return commandText.Contains("PurchaseInvoices", StringComparison.OrdinalIgnoreCase) &&
                   (commandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase) ||
                    commandText.Contains("UPDLOCK", StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed class PurchaseReturnReplayClaimAttemptInterceptor(
        string actorName,
        PurchaseReturnCommitOverlapWindow overlapWindow) : DbCommandInterceptor
    {
        private int _intercepted;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (IsPurchaseReturnIdempotencyInsert(command.CommandText) &&
                Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0)
            {
                // PR-CON-02 critical window: Request B is submitting the same SQL-backed
                // idempotency claim while Request A still owns the uncommitted purchase return,
                // supplier reversal, stock decrement, accounting outbox intent, and mutation version.
                overlapWindow.MarkReplayClaimAttempt(actorName);
            }

            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private static bool IsPurchaseReturnIdempotencyInsert(string commandText)
        {
            return commandText.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase) &&
                   commandText.Contains("PurchaseReturnRequestIdempotency", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class PurchaseReturnAccountingOutboxReturnLockPauseInterceptor(
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
            if (IsPurchaseReturnForUpdateQuery(command.CommandText) &&
                Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0)
            {
                // The purchase-return worker has already claimed the authoritative outbox row.
                // Pausing after PostgreSQL executes the Return FOR UPDATE keeps Worker A active
                // and uncommitted while duplicate Worker B receives the same logical delivery and
                // must respect the claimed-row duplicate fence instead of reposting supplier truth.
                window.MarkOriginalPaused(workerName);
                await window.WaitUntilReleasedAsync(cancellationToken);
            }

            return await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }

        private static bool IsPurchaseReturnForUpdateQuery(string commandText)
        {
            return commandText.Contains("FROM \"Returns\"", StringComparison.OrdinalIgnoreCase) &&
                   commandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class PurchaseReturnAccountingOutboxCommitPauseInterceptor(
        string workerName,
        ActiveProcessingDuplicateWorkerWindow window) : DbTransactionInterceptor
    {
        private int _commitSequence;

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _commitSequence) == 2)
            {
                // The first worker transaction claims the outbox row. The second transaction is
                // the authoritative purchase-return accounting post, where supplier reversal
                // journals have been staged but PostgreSQL has not committed them yet.
                window.MarkOriginalPaused(workerName);
                await window.WaitUntilReleasedAsync(cancellationToken);
            }

            return await base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
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
                message = $"{message} | {DescribeException(exception)}";
            }

            entries.Enqueue($"{typeof(T).Name}:{logLevel}:{message}");
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
