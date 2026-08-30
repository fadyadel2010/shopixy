using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.Paymob;
using PointOfSale.Core.Discounts;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Financial;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Observability;
using PointOfSale.Infrastructure.Payments;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Payments;

public sealed class PaymobSuccessPathSimulationTests
{
    private const long PaymobIntegrationId = 3153058;
    private const long PaymobMerchantOwnerId = 645875;
    private const string PaymobCurrency = "EGP";
    private const decimal OrderTotal = 410m;
    private const long OrderTotalCents = 41000;
    private const string HmacSecret = "p8-paymob-test-hmac-secret-not-production";

    [Fact]
    public async Task SignedSuccessWebhook_ThroughController_FinalizesPaymentOrderInventoryAndIsIdempotent()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var harness = CreateHarness(context, tenantId);
        var fixture = await SeedFreshPaymobOrderAsync(context, harness.StockRepository, tenantId, "ORD-P8-SUCCESS-0001");
        var payload = CreatePaymobWebhookPayload(
            transactionId: 880000001,
            paymobOrderId: fixture.ProviderIntentId,
            merchantOrderId: fixture.OrderId,
            success: true,
            pending: false,
            isAuth: true,
            isCapture: true,
            isStandalonePayment: false,
            isVoided: false,
            isRefunded: false,
            errorOccurred: false);
        var hmac = ComputePaymobWebhookHmac(payload);

        var initial = await SnapshotAsync(context, fixture);
        Assert.Equal(OrderStatus.Pending, initial.OrderStatus);
        Assert.Equal(PaymentStatus.Pending, initial.PaymentStatus);
        Assert.Equal(PaymentIntentStatus.Pending, initial.PaymentIntentStatus);
        Assert.Equal(1, initial.PaymentIntentCount);
        Assert.Equal(1, initial.OrderCount);
        Assert.Equal(1, initial.ReservationCount);
        Assert.Equal(1, initial.StockMovementCount);
        Assert.Equal(9m, initial.StockQuantity);
        Assert.Equal(1m, initial.ReservedQuantity);

        var first = await PostPaymobWebhookAsync(harness.Controller, payload, hmac);
        var afterFirst = await SnapshotAsync(context, fixture);
        var second = await PostPaymobWebhookAsync(harness.Controller, payload, hmac);
        var afterSecond = await SnapshotAsync(context, fixture);
        var third = await PostPaymobWebhookAsync(harness.Controller, payload, hmac);
        var afterThird = await SnapshotAsync(context, fixture);

        AssertOk(first);
        AssertOk(second);
        AssertOk(third);
        Assert.Equal(PaymentStatus.Collected, afterFirst.PaymentStatus);
        Assert.Equal(PaymentIntentStatus.Paid, afterFirst.PaymentIntentStatus);
        Assert.Equal(OrderStatus.Confirmed, afterFirst.OrderStatus);
        Assert.Equal(CheckoutState.Paid, afterFirst.CheckoutState);
        Assert.True(afterFirst.IsPaid);
        Assert.NotNull(afterFirst.PaidAt);
        Assert.Equal("paymob", afterFirst.PaymentGateway);
        Assert.Equal("880000001", afterFirst.ProviderPaymentId);
        Assert.Equal(fixture.ProviderIntentId, afterFirst.ProviderIntentId);
        Assert.Equal(OrderTotal, afterFirst.PaymentIntentAmount);
        Assert.Equal(PaymobCurrency, afterFirst.PaymentIntentCurrency);
        Assert.Equal(PaymentProvider.Paymob, afterFirst.Provider);
        Assert.Equal(1, afterFirst.PaymentIntentCount);
        Assert.Equal(1, afterFirst.OrderCount);
        Assert.Equal(1, afterFirst.ReservationCount);
        Assert.Equal(1, afterFirst.WebhookLogCount);
        Assert.Equal(1, afterFirst.ReplayRecordCount);
        Assert.Equal(8m, afterFirst.StockQuantity);
        Assert.Equal(0m, afterFirst.ReservedQuantity);
        Assert.Equal(1m, afterFirst.CommittedQuantity);
        Assert.Equal(InventoryReservationStatus.Committed, afterFirst.ReservationStatus);
        Assert.Equal(2, afterFirst.StockMovementCount);

        Assert.Equal(afterFirst, afterSecond);
        Assert.Equal(afterFirst, afterThird);

        var status = await harness.PaymobService.GetPaymentStatusAsync(
            tenantId,
            fixture.OrderId,
            customerId: null,
            correlationId: "p8-storefront-status",
            CancellationToken.None);
        Assert.Equal("Collected", status.PaymentStatus);
        Assert.True(status.IsPaid);
        Assert.Equal("captured", status.PaymobStatus);

        var adminVisible = await context.Orders.IgnoreQueryFilters().AnyAsync(order => order.Id == fixture.OrderId && order.PaymentStatus == PaymentStatus.Collected);
        Assert.True(adminVisible);
    }

    [Fact]
    public async Task ConcurrentSignedSuccessWebhooks_FinalizeExactlyOnce()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var harness = CreateHarness(context, tenantId);
        var fixture = await SeedFreshPaymobOrderAsync(context, harness.StockRepository, tenantId, "ORD-P8-CONCURRENT-0001");
        var payload = CreatePaymobWebhookPayload(
            transactionId: 880000002,
            paymobOrderId: fixture.ProviderIntentId,
            merchantOrderId: fixture.OrderId,
            success: true,
            pending: false,
            isAuth: true,
            isCapture: true,
            isStandalonePayment: false,
            isVoided: false,
            isRefunded: false,
            errorOccurred: false);
        var hmac = ComputePaymobWebhookHmac(payload);

        // The application DB context is scoped in production. For this direct-controller
        // harness, concurrent duplicate calls are serialized by using independent controller
        // instances over the same SQLite connection.
        var tasks = Enumerable.Range(0, 5)
            .Select(async _ =>
            {
                await using var workerContext = await CreateDbContextAsync(connection, tenantId);
                var workerHarness = CreateHarness(workerContext, tenantId);
                return await PostPaymobWebhookAsync(workerHarness.Controller, payload, hmac);
            })
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, AssertOk);

        var snapshot = await SnapshotAsync(context, fixture);
        Assert.Equal(PaymentStatus.Collected, snapshot.PaymentStatus);
        Assert.Equal(PaymentIntentStatus.Paid, snapshot.PaymentIntentStatus);
        Assert.Equal(OrderStatus.Confirmed, snapshot.OrderStatus);
        Assert.Equal(1, snapshot.PaymentIntentCount);
        Assert.Equal(1, snapshot.OrderCount);
        Assert.Equal(1, snapshot.WebhookLogCount);
        Assert.Equal(1, snapshot.ReplayRecordCount);
        Assert.Equal(2, snapshot.StockMovementCount);
        Assert.Equal(8m, snapshot.StockQuantity);
        Assert.Equal(0m, snapshot.ReservedQuantity);
        Assert.Equal(1m, snapshot.CommittedQuantity);
    }

    [Fact]
    public async Task FailureThenRetryThenSuccess_CreatesNewAttemptAndFinalizesSameOrder()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var harness = CreateHarness(context, tenantId);
        var fixture = await SeedFreshPaymobOrderAsync(context, harness.StockRepository, tenantId, "ORD-P8-RETRY-SUCCESS-0001");
        var failurePayload = CreatePaymobWebhookPayload(
            transactionId: 880000003,
            paymobOrderId: fixture.ProviderIntentId,
            merchantOrderId: fixture.OrderId,
            success: false,
            pending: false,
            isAuth: false,
            isCapture: false,
            isStandalonePayment: false,
            isVoided: false,
            isRefunded: false,
            errorOccurred: true);

        AssertOk(await PostPaymobWebhookAsync(harness.Controller, failurePayload, ComputePaymobWebhookHmac(failurePayload)));

        var afterFailure = await SnapshotAsync(context, fixture);
        Assert.Equal(PaymentStatus.Failed, afterFailure.PaymentStatus);
        Assert.Equal(PaymentIntentStatus.Failed, afterFailure.PaymentIntentStatus);
        Assert.Equal(OrderStatus.Pending, afterFailure.OrderStatus);
        Assert.Equal(CheckoutState.Failed, afterFailure.CheckoutState);
        Assert.Equal(9m, afterFailure.StockQuantity);
        Assert.Equal(0m, afterFailure.ReservedQuantity);
        Assert.Equal(1m, afterFailure.ReleasedQuantity);

        var retryableStatus = await harness.PaymobService.GetPaymentStatusAsync(
            tenantId,
            fixture.OrderId,
            customerId: null,
            correlationId: "p8-retry-authority-after-failure",
            CancellationToken.None);
        Assert.True(retryableStatus.CanRetryPayment);

        var retry = await harness.PaymobService.CreatePaymentIntentAsync(
            tenantId,
            fixture.OrderId,
            customerId: null,
            promotionCalculationFailed: false,
            correlationId: "p8-retry-after-failure",
            CancellationToken.None,
            idempotencyKey: "p8-retry-after-failure");

        Assert.NotEqual(fixture.PaymentIntentId, retry.PaymentIntentId);
        Assert.NotEqual(fixture.ProviderIntentId, retry.IntentionId);
        Assert.False(string.IsNullOrWhiteSpace(retry.PaymentKey));
        Assert.False(string.IsNullOrWhiteSpace(retry.IframeUrl));

        context.ChangeTracker.Clear();
        var attemptsAfterRetry = await context.PaymentIntents
            .IgnoreQueryFilters()
            .Where(intent => intent.OrderId == fixture.OrderId)
            .OrderBy(intent => intent.CreatedAt)
            .ThenBy(intent => intent.Id)
            .ToListAsync();
        Assert.Equal(2, attemptsAfterRetry.Count);
        Assert.Equal(PaymentIntentStatus.Failed, attemptsAfterRetry[0].Status);
        Assert.Equal(fixture.ProviderIntentId, attemptsAfterRetry[0].ProviderIntentId);
        Assert.Equal(PaymentIntentStatus.Pending, attemptsAfterRetry[1].Status);
        Assert.Equal(retry.IntentionId, attemptsAfterRetry[1].ProviderIntentId);
        Assert.Null(attemptsAfterRetry[1].ProviderPaymentId);

        var orderAfterRetry = await context.Orders
            .IgnoreQueryFilters()
            .SingleAsync(entity => entity.Id == fixture.OrderId);
        Assert.Equal(PaymentStatus.Pending, orderAfterRetry.PaymentStatus);
        Assert.Equal(CheckoutState.PaymentPending, orderAfterRetry.CheckoutState);
        Assert.False(orderAfterRetry.IsPaid);
        Assert.Null(orderAfterRetry.PaidAt);

        var activeRetryStatus = await harness.PaymobService.GetPaymentStatusAsync(
            tenantId,
            fixture.OrderId,
            customerId: null,
            correlationId: "p8-retry-authority-active-attempt",
            CancellationToken.None);
        Assert.False(activeRetryStatus.CanRetryPayment);

        var successPayload = CreatePaymobWebhookPayload(
            transactionId: 880000009,
            paymobOrderId: retry.IntentionId,
            merchantOrderId: fixture.OrderId,
            success: true,
            pending: false,
            isAuth: true,
            isCapture: true,
            isStandalonePayment: false,
            isVoided: false,
            isRefunded: false,
            errorOccurred: false);
        AssertOk(await PostPaymobWebhookAsync(harness.Controller, successPayload, ComputePaymobWebhookHmac(successPayload)));

        context.ChangeTracker.Clear();
        var order = await context.Orders.IgnoreQueryFilters().SingleAsync(entity => entity.Id == fixture.OrderId);
        var finalAttempts = await context.PaymentIntents
            .IgnoreQueryFilters()
            .Where(intent => intent.OrderId == fixture.OrderId)
            .OrderBy(intent => intent.CreatedAt)
            .ThenBy(intent => intent.Id)
            .ToListAsync();
        var stock = await context.ProductVariantStocks.IgnoreQueryFilters().SingleAsync(entity => entity.TenantId == tenantId && entity.VariantId == fixture.VariantId);

        Assert.Equal(1, await context.Orders.IgnoreQueryFilters().CountAsync(entity => entity.Id == fixture.OrderId));
        Assert.Equal(PaymentStatus.Collected, order.PaymentStatus);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(CheckoutState.Paid, order.CheckoutState);
        Assert.Equal(PaymentIntentStatus.Failed, finalAttempts[0].Status);
        Assert.Equal(PaymentIntentStatus.Paid, finalAttempts[1].Status);
        Assert.Equal("880000009", finalAttempts[1].ProviderPaymentId);
        Assert.Equal(8m, stock.Quantity);
        Assert.Equal(0m, stock.ReservedQuantity);
        Assert.Equal(1m, await context.InventoryReservations.IgnoreQueryFilters().SumAsync(reservation => reservation.CommittedQuantity));

        var status = await harness.PaymobService.GetPaymentStatusAsync(
            tenantId,
            fixture.OrderId,
            customerId: null,
            correlationId: "p8-retry-storefront-status",
            CancellationToken.None);
        Assert.Equal("Collected", status.PaymentStatus);
        Assert.True(status.IsPaid);

        var adminVisible = await context.Orders.IgnoreQueryFilters().AnyAsync(entity => entity.Id == fixture.OrderId && entity.PaymentStatus == PaymentStatus.Collected);
        Assert.True(adminVisible);
    }

    [Fact]
    public async Task FailureThenRetryThenFailureThenRetryThenSuccess_PreservesOneOrderAndAttemptHistory()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var harness = CreateHarness(context, tenantId);
        var fixture = await SeedFreshPaymobOrderAsync(context, harness.StockRepository, tenantId, "ORD-P9-MULTI-RETRY-0001");

        var firstFailure = CreatePaymobWebhookPayload(880000031, fixture.ProviderIntentId, fixture.PaymentIntentId, false, false, false, false, false, false, false, true);
        AssertOk(await PostPaymobWebhookAsync(harness.Controller, firstFailure, ComputePaymobWebhookHmac(firstFailure)));

        var secondAttempt = await harness.PaymobService.CreatePaymentIntentAsync(
            tenantId,
            fixture.OrderId,
            customerId: null,
            promotionCalculationFailed: false,
            correlationId: "p9-second-attempt",
            CancellationToken.None,
            idempotencyKey: "p9-second-attempt");
        var secondFailure = CreatePaymobWebhookPayload(880000032, secondAttempt.IntentionId, secondAttempt.PaymentIntentId, false, false, false, false, false, false, false, true);
        AssertOk(await PostPaymobWebhookAsync(harness.Controller, secondFailure, ComputePaymobWebhookHmac(secondFailure)));

        var failedAgainStatus = await harness.PaymobService.GetPaymentStatusAsync(
            tenantId,
            fixture.OrderId,
            customerId: null,
            correlationId: "p9-failed-again-status",
            CancellationToken.None);
        Assert.True(failedAgainStatus.CanRetryPayment);

        var thirdAttempt = await harness.PaymobService.CreatePaymentIntentAsync(
            tenantId,
            fixture.OrderId,
            customerId: null,
            promotionCalculationFailed: false,
            correlationId: "p9-third-attempt",
            CancellationToken.None,
            idempotencyKey: "p9-third-attempt");
        var thirdSuccess = CreatePaymobWebhookPayload(880000033, thirdAttempt.IntentionId, thirdAttempt.PaymentIntentId, true, false, true, true, false, false, false, false);
        AssertOk(await PostPaymobWebhookAsync(harness.Controller, thirdSuccess, ComputePaymobWebhookHmac(thirdSuccess)));

        context.ChangeTracker.Clear();
        var order = await context.Orders.IgnoreQueryFilters().SingleAsync(entity => entity.Id == fixture.OrderId);
        var attempts = await context.PaymentIntents
            .IgnoreQueryFilters()
            .Where(intent => intent.OrderId == fixture.OrderId)
            .OrderBy(intent => intent.CreatedAt)
            .ThenBy(intent => intent.Id)
            .ToListAsync();
        var stock = await context.ProductVariantStocks
            .IgnoreQueryFilters()
            .SingleAsync(entity => entity.TenantId == tenantId && entity.VariantId == fixture.VariantId);

        Assert.Equal(1, await context.Orders.IgnoreQueryFilters().CountAsync(entity => entity.Id == fixture.OrderId));
        Assert.Equal(3, attempts.Count);
        Assert.Equal(
            [PaymentIntentStatus.Failed, PaymentIntentStatus.Failed, PaymentIntentStatus.Paid],
            attempts.Select(attempt => attempt.Status).ToArray());
        Assert.Equal(3, attempts.Select(attempt => attempt.ProviderIntentId).Distinct().Count());
        Assert.Equal(PaymentStatus.Collected, order.PaymentStatus);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(CheckoutState.Paid, order.CheckoutState);
        Assert.Equal(8m, stock.Quantity);
        Assert.Equal(0m, stock.ReservedQuantity);
        Assert.Equal(1m, await context.InventoryReservations.IgnoreQueryFilters().SumAsync(reservation => reservation.CommittedQuantity));
    }

    [Fact]
    public async Task ExpiredCompleteSession_ReplacesAttemptWithoutReusingStaleHandoff()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var harness = CreateHarness(context, tenantId);
        var fixture = await SeedFreshPaymobOrderAsync(context, harness.StockRepository, tenantId, "ORD-P8-EXPIRED-REPLACE-0001");
        var expiredUrl = "https://accept.paymob.com/unifiedcheckout/?publicKey=pk_old&clientSecret=cs_old";
        var expiredIntent = await context.PaymentIntents.SingleAsync(intent => intent.Id == fixture.PaymentIntentId);
        expiredIntent.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        expiredIntent.ProviderMetadata = PaymentIntentRoutingMetadataSerializer.Serialize(
            tenantId,
            fixture.OrderId,
            "https://store.example.test",
            protectedPaymobSession: harness.ArtifactProtector.ProtectPaymentSession("cs_old", expiredUrl));
        var partialReservation = await context.InventoryReservations
            .IgnoreQueryFilters()
            .SingleAsync(entity => entity.OrderId == fixture.OrderId);
        var partialStock = await context.ProductVariantStocks
            .IgnoreQueryFilters()
            .SingleAsync(entity => entity.TenantId == tenantId && entity.VariantId == fixture.VariantId);
        partialReservation.ReservedQuantity = 0.5m;
        partialStock.ReservedQuantity = 0.5m;
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"OrderItems\" SET \"WarehouseId\" = NULL WHERE \"OrderId\" = {fixture.OrderId}");
        context.ChangeTracker.Clear();

        var replacementStartedAtUtc = DateTime.UtcNow;
        var replacement = await harness.PaymobService.CreatePaymentIntentAsync(
            tenantId,
            fixture.OrderId,
            customerId: null,
            promotionCalculationFailed: false,
            correlationId: "p8-expired-session-replacement",
            CancellationToken.None,
            idempotencyKey: "p8-expired-session-replacement");

        context.ChangeTracker.Clear();
        var attempts = await context.PaymentIntents
            .IgnoreQueryFilters()
            .Where(intent => intent.OrderId == fixture.OrderId)
            .OrderBy(intent => intent.CreatedAt)
            .ThenBy(intent => intent.Id)
            .ToListAsync();
        var oldAttempt = attempts.Single(intent => intent.Id == fixture.PaymentIntentId);
        var newAttempt = attempts.Single(intent => intent.Id == replacement.PaymentIntentId);
        var backfilledOrderItem = await context.OrderItems
            .IgnoreQueryFilters()
            .SingleAsync(item => item.OrderId == fixture.OrderId);
        var activeReservations = await context.InventoryReservations
            .IgnoreQueryFilters()
            .Where(entity =>
                entity.OrderId == fixture.OrderId &&
                entity.Status == InventoryReservationStatus.Active &&
                entity.ReservedQuantity > entity.CommittedQuantity + entity.ReleasedQuantity)
            .ToListAsync();

        Assert.Equal(2, attempts.Count);
        Assert.Equal(PaymentIntentStatus.Expired, oldAttempt.Status);
        Assert.Equal("PAYMOB_CHECKOUT_SESSION_EXPIRED_SUPERSEDED", oldAttempt.FailureCode);
        Assert.Equal(fixture.ProviderIntentId, oldAttempt.ProviderIntentId);
        Assert.Null(oldAttempt.RedirectUrl);
        Assert.Null(PaymentIntentRoutingMetadataSerializer.Deserialize(oldAttempt.ProviderMetadata)?.ProtectedPaymobSession);
        Assert.Equal(PaymentIntentStatus.Pending, newAttempt.Status);
        Assert.NotEqual(oldAttempt.Id, newAttempt.Id);
        Assert.NotEqual(oldAttempt.PaymentReference, newAttempt.PaymentReference);
        Assert.NotEqual(expiredUrl, replacement.IframeUrl);
        Assert.True(replacement.ExpiresAtUtc > replacementStartedAtUtc.AddMinutes(59));
        Assert.Null(backfilledOrderItem.WarehouseId);
        Assert.Equal(1m, activeReservations.Sum(reservation => reservation.ReservedQuantity - reservation.CommittedQuantity - reservation.ReleasedQuantity));
        Assert.All(activeReservations, reservation => Assert.Equal(fixture.WarehouseId, reservation.WarehouseId));
        Assert.All(activeReservations, reservation => Assert.True(reservation.ExpiresAtUtc >= replacement.ExpiresAtUtc));

        var replay = await harness.PaymobService.CreatePaymentIntentAsync(
            tenantId,
            fixture.OrderId,
            customerId: null,
            promotionCalculationFailed: false,
            correlationId: "p8-expired-session-replay",
            CancellationToken.None,
            idempotencyKey: "p8-expired-session-replacement");

        Assert.Equal(replacement.PaymentIntentId, replay.PaymentIntentId);
        Assert.Equal(replacement.IntentionId, replay.IntentionId);
        Assert.Equal(2, await context.PaymentIntents.IgnoreQueryFilters().CountAsync(intent => intent.OrderId == fixture.OrderId));
        harness.RuntimeApplication.Verify(
            service => service.CreatePaymentIntentAsync(
                It.IsAny<PaymobRuntimePaymentIntentApplicationRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        var staleSuccess = CreatePaymobWebhookPayload(
            880000020,
            fixture.ProviderIntentId,
            fixture.OrderId,
            success: true,
            pending: false,
            isAuth: true,
            isCapture: true,
            isStandalonePayment: false,
            isVoided: false,
            isRefunded: false,
            errorOccurred: false);
        AssertOk(await PostPaymobWebhookAsync(
            harness.Controller,
            staleSuccess,
            ComputePaymobWebhookHmac(staleSuccess)));

        var staleAuthorization = CreatePaymobWebhookPayload(
            880000021,
            fixture.ProviderIntentId,
            fixture.OrderId,
            success: true,
            pending: false,
            isAuth: true,
            isCapture: false,
            isStandalonePayment: false,
            isVoided: false,
            isRefunded: false,
            errorOccurred: false);
        AssertOk(await PostPaymobWebhookAsync(
            harness.Controller,
            staleAuthorization,
            ComputePaymobWebhookHmac(staleAuthorization)));

        var staleFailure = CreatePaymobWebhookPayload(
            880000022,
            fixture.ProviderIntentId,
            fixture.OrderId,
            success: false,
            pending: false,
            isAuth: false,
            isCapture: false,
            isStandalonePayment: false,
            isVoided: false,
            isRefunded: false,
            errorOccurred: true);
        AssertOk(await PostPaymobWebhookAsync(
            harness.Controller,
            staleFailure,
            ComputePaymobWebhookHmac(staleFailure)));

        context.ChangeTracker.Clear();
        var orderAfterStaleSuccess = await context.Orders.IgnoreQueryFilters().SingleAsync(order => order.Id == fixture.OrderId);
        var oldAttemptAfterStaleSuccess = await context.PaymentIntents.IgnoreQueryFilters().SingleAsync(intent => intent.Id == fixture.PaymentIntentId);
        var newAttemptAfterStaleSuccess = await context.PaymentIntents.IgnoreQueryFilters().SingleAsync(intent => intent.Id == replacement.PaymentIntentId);
        Assert.Equal(PaymentStatus.Pending, orderAfterStaleSuccess.PaymentStatus);
        Assert.Equal(PaymentIntentStatus.Expired, oldAttemptAfterStaleSuccess.Status);
        Assert.Equal(PaymentIntentStatus.Pending, newAttemptAfterStaleSuccess.Status);
        Assert.False(orderAfterStaleSuccess.IsPaid);

        newAttemptAfterStaleSuccess.Status = PaymentIntentStatus.Failed;
        newAttemptAfterStaleSuccess.FailureCode = "PAYMOB_INTENT_CREATE_FAILED";
        newAttemptAfterStaleSuccess.FailedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        var staleSuccessAfterReplacementFailure = CreatePaymobWebhookPayload(
            880000023,
            fixture.ProviderIntentId,
            fixture.OrderId,
            success: true,
            pending: false,
            isAuth: true,
            isCapture: true,
            isStandalonePayment: false,
            isVoided: false,
            isRefunded: false,
            errorOccurred: false);
        AssertOk(await PostPaymobWebhookAsync(
            harness.Controller,
            staleSuccessAfterReplacementFailure,
            ComputePaymobWebhookHmac(staleSuccessAfterReplacementFailure)));

        context.ChangeTracker.Clear();
        var finalOrder = await context.Orders.IgnoreQueryFilters().SingleAsync(order => order.Id == fixture.OrderId);
        var finalOldAttempt = await context.PaymentIntents.IgnoreQueryFilters().SingleAsync(intent => intent.Id == fixture.PaymentIntentId);
        var finalReplacementAttempt = await context.PaymentIntents.IgnoreQueryFilters().SingleAsync(intent => intent.Id == replacement.PaymentIntentId);
        Assert.Equal(PaymentStatus.Pending, finalOrder.PaymentStatus);
        Assert.Equal(PaymentIntentStatus.Expired, finalOldAttempt.Status);
        Assert.Equal(PaymentIntentStatus.Failed, finalReplacementAttempt.Status);
        Assert.False(finalOrder.IsPaid);
    }

    [Fact]
    public async Task ExpiredCompleteSession_RebuildsSurplusReservationSetBeforeReplacement()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var harness = CreateHarness(context, tenantId);
        var fixture = await SeedFreshPaymobOrderAsync(context, harness.StockRepository, tenantId, "ORD-P8-EXPIRED-SURPLUS-0001");
        var expiredIntent = await context.PaymentIntents.SingleAsync(intent => intent.Id == fixture.PaymentIntentId);
        expiredIntent.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        expiredIntent.ProviderMetadata = PaymentIntentRoutingMetadataSerializer.Serialize(
            tenantId,
            fixture.OrderId,
            "https://store.example.test",
            protectedPaymobSession: harness.ArtifactProtector.ProtectPaymentSession(
                "cs_old_surplus",
                "https://accept.paymob.com/unifiedcheckout/?publicKey=pk_old&clientSecret=cs_old_surplus"));

        var validReservation = await context.InventoryReservations
            .IgnoreQueryFilters()
            .SingleAsync(reservation => reservation.OrderId == fixture.OrderId);
        context.InventoryReservations.Add(new InventoryReservation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = fixture.OrderId,
            OrderItemId = Guid.Empty,
            ProductId = validReservation.ProductId,
            VariantId = validReservation.VariantId,
            WarehouseId = validReservation.WarehouseId,
            ReservedQuantity = 0.5m,
            Status = InventoryReservationStatus.Active,
            ReservationEventId = Guid.NewGuid(),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30)
        });
        var stock = await context.ProductVariantStocks
            .IgnoreQueryFilters()
            .SingleAsync(entity => entity.TenantId == tenantId && entity.VariantId == fixture.VariantId);
        stock.ReservedQuantity += 0.5m;
        await context.SaveChangesAsync();

        await harness.PaymobService.CreatePaymentIntentAsync(
            tenantId,
            fixture.OrderId,
            customerId: null,
            promotionCalculationFailed: false,
            correlationId: "p8-expired-surplus-replacement",
            CancellationToken.None,
            idempotencyKey: "p8-expired-surplus-replacement");

        context.ChangeTracker.Clear();
        var activeReservations = await context.InventoryReservations
            .IgnoreQueryFilters()
            .Where(reservation =>
                reservation.OrderId == fixture.OrderId &&
                reservation.Status == InventoryReservationStatus.Active &&
                reservation.ReservedQuantity > reservation.CommittedQuantity + reservation.ReleasedQuantity)
            .ToListAsync();
        var activeReservation = Assert.Single(activeReservations);
        Assert.NotEqual(Guid.Empty, activeReservation.OrderItemId);
        Assert.Equal(1m, activeReservation.RemainingReservedQuantity);
        Assert.Equal(1m, (await context.ProductVariantStocks
            .IgnoreQueryFilters()
            .SingleAsync(entity => entity.TenantId == tenantId && entity.VariantId == fixture.VariantId)).ReservedQuantity);
    }

    [Fact]
    public async Task ExpiredAmbiguousCheckpoint_RemainsFailClosedWithoutProviderCall()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var harness = CreateHarness(context, tenantId);
        var fixture = await SeedFreshPaymobOrderAsync(context, harness.StockRepository, tenantId, "ORD-P8-EXPIRED-CHECKPOINT-0001");
        var expiredIntent = await context.PaymentIntents.SingleAsync(intent => intent.Id == fixture.PaymentIntentId);
        expiredIntent.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        expiredIntent.ProviderMetadata = PaymentIntentRoutingMetadataSerializer.Serialize(
            tenantId,
            fixture.OrderId,
            "https://store.example.test",
            paymobMerchantOrderId: fixture.OrderId.ToString("D"),
            paymobCheckpointState: "provider_order_accepted",
            paymobCheckpointedAtUtc: DateTime.UtcNow.AddMinutes(-2));
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<PaymentException>(() =>
            harness.PaymobService.CreatePaymentIntentAsync(
                tenantId,
                fixture.OrderId,
                customerId: null,
                promotionCalculationFailed: false,
                correlationId: "p8-expired-ambiguous-checkpoint",
                CancellationToken.None,
                idempotencyKey: "p8-expired-ambiguous-checkpoint"));

        Assert.Equal("PAYMOB_PAYMENT_SESSION_EXPIRED", exception.ErrorCode);
        context.ChangeTracker.Clear();
        var persisted = await context.PaymentIntents.IgnoreQueryFilters().SingleAsync(intent => intent.Id == fixture.PaymentIntentId);
        Assert.Equal(PaymentIntentStatus.Pending, persisted.Status);
        Assert.Single(await context.PaymentIntents.IgnoreQueryFilters().Where(intent => intent.OrderId == fixture.OrderId).ToListAsync());
        harness.RuntimeApplication.Verify(
            service => service.CreatePaymentIntentAsync(
                It.IsAny<PaymobRuntimePaymentIntentApplicationRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExpiredMalformedCheckpointMetadata_RemainsFailClosedWithoutProviderCall()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var harness = CreateHarness(context, tenantId);
        var fixture = await SeedFreshPaymobOrderAsync(context, harness.StockRepository, tenantId, "ORD-P8-MALFORMED-CHECKPOINT-0001");
        var expiredIntent = await context.PaymentIntents.SingleAsync(intent => intent.Id == fixture.PaymentIntentId);
        expiredIntent.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        expiredIntent.ProviderIntentId = null;
        expiredIntent.ProviderMetadata = "{\"paymobCheckpointState\":\"registration_started\"";
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<PaymentException>(() =>
            harness.PaymobService.CreatePaymentIntentAsync(
                tenantId,
                fixture.OrderId,
                customerId: null,
                promotionCalculationFailed: false,
                correlationId: "p8-expired-malformed-checkpoint",
                CancellationToken.None,
                idempotencyKey: "p8-expired-malformed-checkpoint"));

        Assert.Equal("PAYMOB_PAYMENT_SESSION_EXPIRED", exception.ErrorCode);
        Assert.Single(await context.PaymentIntents.IgnoreQueryFilters().Where(intent => intent.OrderId == fixture.OrderId).ToListAsync());
        harness.RuntimeApplication.Verify(
            service => service.CreatePaymentIntentAsync(
                It.IsAny<PaymobRuntimePaymentIntentApplicationRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LateSuccessForExpiredAttempt_AfterReplacementPaid_DoesNotHideSecondChargeEvidence()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var harness = CreateHarness(context, tenantId);
        var fixture = await SeedFreshPaymobOrderAsync(context, harness.StockRepository, tenantId, "ORD-P8-EXPIRED-PAID-0001");
        var expiredIntent = await context.PaymentIntents.SingleAsync(intent => intent.Id == fixture.PaymentIntentId);
        expiredIntent.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        expiredIntent.ProviderMetadata = PaymentIntentRoutingMetadataSerializer.Serialize(
            tenantId,
            fixture.OrderId,
            "https://store.example.test",
            protectedPaymobSession: harness.ArtifactProtector.ProtectPaymentSession(
                "cs_old_paid",
                "https://accept.paymob.com/unifiedcheckout/?publicKey=pk_old&clientSecret=cs_old_paid"));
        await context.SaveChangesAsync();

        var replacement = await harness.PaymobService.CreatePaymentIntentAsync(
            tenantId,
            fixture.OrderId,
            customerId: null,
            promotionCalculationFailed: false,
            correlationId: "p8-expired-paid-replacement",
            CancellationToken.None,
            idempotencyKey: "p8-expired-paid-replacement");
        var replacementSuccess = CreatePaymobWebhookPayload(
            880000025,
            replacement.IntentionId,
            replacement.PaymentIntentId,
            success: true,
            pending: false,
            isAuth: true,
            isCapture: true,
            isStandalonePayment: false,
            isVoided: false,
            isRefunded: false,
            errorOccurred: false);
        AssertOk(await PostPaymobWebhookAsync(
            harness.Controller,
            replacementSuccess,
            ComputePaymobWebhookHmac(replacementSuccess)));

        var staleOldSuccess = CreatePaymobWebhookPayload(
            880000026,
            fixture.ProviderIntentId,
            fixture.PaymentIntentId,
            success: true,
            pending: false,
            isAuth: true,
            isCapture: true,
            isStandalonePayment: false,
            isVoided: false,
            isRefunded: false,
            errorOccurred: false);
        AssertOk(await PostPaymobWebhookAsync(
            harness.Controller,
            staleOldSuccess,
            ComputePaymobWebhookHmac(staleOldSuccess)));

        context.ChangeTracker.Clear();
        var finalOrder = await context.Orders.IgnoreQueryFilters().SingleAsync(order => order.Id == fixture.OrderId);
        var finalOldAttempt = await context.PaymentIntents.IgnoreQueryFilters().SingleAsync(intent => intent.Id == fixture.PaymentIntentId);
        var finalReplacementAttempt = await context.PaymentIntents.IgnoreQueryFilters().SingleAsync(intent => intent.Id == replacement.PaymentIntentId);
        Assert.Equal(PaymentStatus.Collected, finalOrder.PaymentStatus);
        Assert.Equal(PaymentIntentStatus.Expired, finalOldAttempt.Status);
        Assert.Equal(PaymentIntentStatus.Paid, finalReplacementAttempt.Status);
        Assert.True(finalOrder.IsPaid);
        Assert.Equal(2, await context.PaymentWebhookLogs.IgnoreQueryFilters().CountAsync(log => log.OrderId == fixture.OrderId));
        var quarantinedOldSuccess = await context.PaymentWebhookLogs
            .IgnoreQueryFilters()
            .SingleAsync(log => log.PaymentIntentId == fixture.PaymentIntentId);
        Assert.Contains("UNRESOLVED_SUSPENSE_MANUAL_REVIEW", quarantinedOldSuccess.ProcessingError);
        Assert.Contains("SUPERSEDED_PAYMENT_ATTEMPT_SUCCESS_REJECTED", quarantinedOldSuccess.ProcessingError);
        Assert.DoesNotContain("UNRESOLVED_SUSPENSE_ROUTED", quarantinedOldSuccess.ProcessingError);
        var suspenseOutbox = await context.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .SingleAsync(message =>
                message.BusinessEntityId == fixture.OrderId &&
                message.EventType == "UnresolvedOnlinePayment");
        Assert.Contains("880000026", suspenseOutbox.PayloadJson);
    }

    [Fact]
    public async Task AttemptScopedMerchantReference_RecoversMissingProviderIntentIdAndFinalizes()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var harness = CreateHarness(context, tenantId);
        var fixture = await SeedFreshPaymobOrderAsync(context, harness.StockRepository, tenantId, "ORD-P8-ATTEMPT-MERCHANT-0001");
        var intent = await context.PaymentIntents.SingleAsync(entity => entity.Id == fixture.PaymentIntentId);
        intent.ProviderIntentId = null;
        await context.SaveChangesAsync();

        const string recoveredProviderIntentId = "920000024";
        var success = CreatePaymobWebhookPayload(
            880000024,
            recoveredProviderIntentId,
            fixture.PaymentIntentId,
            success: true,
            pending: false,
            isAuth: true,
            isCapture: true,
            isStandalonePayment: false,
            isVoided: false,
            isRefunded: false,
            errorOccurred: false);
        AssertOk(await PostPaymobWebhookAsync(harness.Controller, success, ComputePaymobWebhookHmac(success)));

        context.ChangeTracker.Clear();
        var finalizedOrder = await context.Orders.IgnoreQueryFilters().SingleAsync(order => order.Id == fixture.OrderId);
        var finalizedIntent = await context.PaymentIntents.IgnoreQueryFilters().SingleAsync(entity => entity.Id == fixture.PaymentIntentId);
        Assert.Equal(PaymentStatus.Collected, finalizedOrder.PaymentStatus);
        Assert.Equal(PaymentIntentStatus.Paid, finalizedIntent.Status);
        Assert.Equal(recoveredProviderIntentId, finalizedIntent.ProviderIntentId);
    }

    [Fact]
    public async Task LateSuccessForFailedAttempt_WhenRetryAttemptIsActive_DoesNotFinalizeOrder()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var harness = CreateHarness(context, tenantId);
        var fixture = await SeedFreshPaymobOrderAsync(context, harness.StockRepository, tenantId, "ORD-P8-STALE-SUCCESS-0001");
        var failurePayload = CreatePaymobWebhookPayload(880000013, fixture.ProviderIntentId, fixture.OrderId, false, false, false, false, false, false, false, true);
        AssertOk(await PostPaymobWebhookAsync(harness.Controller, failurePayload, ComputePaymobWebhookHmac(failurePayload)));

        var retry = await harness.PaymobService.CreatePaymentIntentAsync(
            tenantId,
            fixture.OrderId,
            customerId: null,
            promotionCalculationFailed: false,
            correlationId: "p8-stale-success-retry",
            CancellationToken.None,
            idempotencyKey: "p8-stale-success-retry");

        var staleSuccessPayload = CreatePaymobWebhookPayload(880000014, fixture.ProviderIntentId, fixture.OrderId, true, false, true, true, false, false, false, false);
        AssertOk(await PostPaymobWebhookAsync(harness.Controller, staleSuccessPayload, ComputePaymobWebhookHmac(staleSuccessPayload)));

        context.ChangeTracker.Clear();
        var orderAfterStale = await context.Orders.IgnoreQueryFilters().SingleAsync(entity => entity.Id == fixture.OrderId);
        var originalAttemptAfterStale = await context.PaymentIntents.IgnoreQueryFilters().SingleAsync(entity => entity.Id == fixture.PaymentIntentId);
        var retryAttemptAfterStale = await context.PaymentIntents.IgnoreQueryFilters().SingleAsync(entity => entity.Id == retry.PaymentIntentId);

        Assert.Equal(PaymentStatus.Pending, orderAfterStale.PaymentStatus);
        Assert.Equal(OrderStatus.Pending, orderAfterStale.Status);
        Assert.Equal(PaymentIntentStatus.Failed, originalAttemptAfterStale.Status);
        Assert.Equal(PaymentIntentStatus.Pending, retryAttemptAfterStale.Status);
        Assert.False(orderAfterStale.IsPaid);

        var retrySuccessPayload = CreatePaymobWebhookPayload(880000015, retry.IntentionId, fixture.OrderId, true, false, true, true, false, false, false, false);
        AssertOk(await PostPaymobWebhookAsync(harness.Controller, retrySuccessPayload, ComputePaymobWebhookHmac(retrySuccessPayload)));

        context.ChangeTracker.Clear();
        var finalOrder = await context.Orders.IgnoreQueryFilters().SingleAsync(entity => entity.Id == fixture.OrderId);
        var finalOriginalAttempt = await context.PaymentIntents.IgnoreQueryFilters().SingleAsync(entity => entity.Id == fixture.PaymentIntentId);
        var finalRetryAttempt = await context.PaymentIntents.IgnoreQueryFilters().SingleAsync(entity => entity.Id == retry.PaymentIntentId);

        Assert.Equal(PaymentStatus.Collected, finalOrder.PaymentStatus);
        Assert.Equal(PaymentIntentStatus.Failed, finalOriginalAttempt.Status);
        Assert.Equal(PaymentIntentStatus.Paid, finalRetryAttempt.Status);
        Assert.Equal("880000015", finalRetryAttempt.ProviderPaymentId);
    }
    [Theory]
    [InlineData("bad-hmac", "INVALID_SIGNATURE")]
    public async Task ForgedSuccessWebhook_WithoutValidHmac_CannotFinalize(string hmac, string expectedCode)
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var harness = CreateHarness(context, tenantId);
        var fixture = await SeedFreshPaymobOrderAsync(context, harness.StockRepository, tenantId, "ORD-P8-SECURITY-0001");
        var payload = CreatePaymobWebhookPayload(
            transactionId: 880000004,
            paymobOrderId: fixture.ProviderIntentId,
            merchantOrderId: fixture.OrderId,
            success: true,
            pending: false,
            isAuth: true,
            isCapture: true,
            isStandalonePayment: false,
            isVoided: false,
            isRefunded: false,
            errorOccurred: false);

        var response = await PostPaymobWebhookAsync(harness.Controller, payload, hmac);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(response);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
        Assert.Contains(expectedCode, JsonSerializer.Serialize(unauthorized.Value), StringComparison.Ordinal);

        var snapshot = await SnapshotAsync(context, fixture);
        Assert.Equal(PaymentStatus.Pending, snapshot.PaymentStatus);
        Assert.Equal(PaymentIntentStatus.Pending, snapshot.PaymentIntentStatus);
        Assert.Equal(OrderStatus.Pending, snapshot.OrderStatus);
        Assert.Equal(1, snapshot.StockMovementCount);
        Assert.Equal(9m, snapshot.StockQuantity);
        Assert.Equal(1m, snapshot.ReservedQuantity);
    }

    [Fact]
    public async Task MissingHmac_CannotFinalize()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var harness = CreateHarness(context, tenantId);
        var fixture = await SeedFreshPaymobOrderAsync(context, harness.StockRepository, tenantId, "ORD-P8-MISSING-HMAC-0001");
        var payload = CreatePaymobWebhookPayload(880000017, fixture.ProviderIntentId, fixture.OrderId, true, false, true, true, false, false, false, false);

        var response = await PostPaymobWebhookAsync(harness.Controller, payload, hmac: null);

        var unauthorized = Assert.IsType<ObjectResult>(response);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
        var snapshot = await SnapshotAsync(context, fixture);
        Assert.Equal(PaymentStatus.Pending, snapshot.PaymentStatus);
        Assert.Equal(PaymentIntentStatus.Pending, snapshot.PaymentIntentStatus);
        Assert.Equal(OrderStatus.Pending, snapshot.OrderStatus);
        Assert.Equal(1, snapshot.StockMovementCount);
    }

    [Fact]
    public async Task PayloadModifiedAfterHmacComputation_CannotFinalize()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var harness = CreateHarness(context, tenantId);
        var fixture = await SeedFreshPaymobOrderAsync(context, harness.StockRepository, tenantId, "ORD-P8-TAMPERED-HMAC-0001");
        var originalPayload = CreatePaymobWebhookPayload(880000018, fixture.ProviderIntentId, fixture.OrderId, true, false, true, true, false, false, false, false);
        var originalHmac = ComputePaymobWebhookHmac(originalPayload);
        var tamperedPayload = originalPayload.Replace("\"amount_cents\":41000", "\"amount_cents\":41001", StringComparison.Ordinal);

        var response = await PostPaymobWebhookAsync(harness.Controller, tamperedPayload, originalHmac);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(response);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
        var snapshot = await SnapshotAsync(context, fixture);
        Assert.Equal(PaymentStatus.Pending, snapshot.PaymentStatus);
        Assert.Equal(PaymentIntentStatus.Pending, snapshot.PaymentIntentStatus);
        Assert.Equal(OrderStatus.Pending, snapshot.OrderStatus);
        Assert.Equal(1, snapshot.StockMovementCount);
    }

    [Fact]
    public async Task WrongAmountCurrencyIntegrationOrOrderReference_CannotFinalize()
    {
        await AssertSecurityMutationDoesNotFinalizeAsync(
            "wrong amount",
            fixture => CreatePaymobWebhookPayload(880000005, fixture.ProviderIntentId, fixture.OrderId, true, false, true, true, false, false, false, false, amountCents: 40900),
            expectedStatus: PaymentStatus.AmountMismatch,
            expectedIntentStatus: PaymentIntentStatus.AmountMismatch);

        await AssertSecurityMutationDoesNotFinalizeAsync(
            "wrong currency",
            fixture => CreatePaymobWebhookPayload(880000006, fixture.ProviderIntentId, fixture.OrderId, true, false, true, true, false, false, false, false, currency: "USD"),
            expectedStatus: PaymentStatus.AmountMismatch,
            expectedIntentStatus: PaymentIntentStatus.AmountMismatch);

        await AssertSecurityMutationDoesNotFinalizeAsync(
            "wrong integration",
            fixture => CreatePaymobWebhookPayload(880000007, fixture.ProviderIntentId, fixture.OrderId, true, false, true, true, false, false, false, false, integrationId: 123),
            expectedStatus: PaymentStatus.Pending,
            expectedIntentStatus: PaymentIntentStatus.Pending,
            expectControllerFailure: true);

        await AssertSecurityMutationDoesNotFinalizeAsync(
            "wrong order reference",
            fixture => CreatePaymobWebhookPayload(880000008, "999000888", Guid.NewGuid(), true, false, true, true, false, false, false, false),
            expectedStatus: PaymentStatus.Pending,
            expectedIntentStatus: PaymentIntentStatus.Pending,
            expectControllerFailure: true);
    }

    [Fact]
    public async Task WrongMerchantOwnerId_CannotFinalize()
    {
        await AssertSecurityMutationDoesNotFinalizeAsync(
            "wrong merchant owner",
            fixture => CreatePaymobWebhookPayload(
                880000016,
                fixture.ProviderIntentId,
                fixture.OrderId,
                true,
                false,
                true,
                true,
                false,
                false,
                false,
                false,
                owner: 999999),
            expectedStatus: PaymentStatus.Pending,
            expectedIntentStatus: PaymentIntentStatus.Pending,
            expectControllerFailure: true);
    }

    [Theory]
    [InlineData(40999)]
    [InlineData(41001)]
    [InlineData(40000)]
    [InlineData(0)]
    [InlineData(-41000)]
    [InlineData(999999999999)]
    public async Task WrongAmountVariants_CannotFinalize(long amountCents)
    {
        await AssertSecurityMutationDoesNotFinalizeAsync(
            $"wrong amount {amountCents}",
            fixture => CreatePaymobWebhookPayload(880000100 + Math.Abs(amountCents % 1000), fixture.ProviderIntentId, fixture.OrderId, true, false, true, true, false, false, false, false, amountCents: amountCents),
            expectedStatus: PaymentStatus.AmountMismatch,
            expectedIntentStatus: PaymentIntentStatus.AmountMismatch);
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("EUR")]
    [InlineData("")]
    [InlineData("egp")]
    [InlineData("SAR")]
    public async Task WrongCurrencyVariants_CannotFinalize(string currency)
    {
        var expectedPaymentStatus = string.Equals(currency, PaymobCurrency, StringComparison.OrdinalIgnoreCase)
            ? PaymentStatus.Collected
            : PaymentStatus.AmountMismatch;
        var expectedIntentStatus = string.Equals(currency, PaymobCurrency, StringComparison.OrdinalIgnoreCase)
            ? PaymentIntentStatus.Paid
            : PaymentIntentStatus.AmountMismatch;

        await AssertSecurityMutationDoesNotFinalizeAsync(
            $"wrong currency {currency}",
            fixture => CreatePaymobWebhookPayload(880001000 + currency.Length, fixture.ProviderIntentId, fixture.OrderId, true, false, true, true, false, false, false, false, currency: currency),
            expectedStatus: expectedPaymentStatus,
            expectedIntentStatus: expectedIntentStatus,
            shouldFinalize: expectedPaymentStatus == PaymentStatus.Collected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999999)]
    public async Task WrongMerchantOwnerVariants_CannotFinalize(long owner)
    {
        await AssertSecurityMutationDoesNotFinalizeAsync(
            $"wrong merchant owner {owner}",
            fixture => CreatePaymobWebhookPayload(880002000 + owner, fixture.ProviderIntentId, fixture.OrderId, true, false, true, true, false, false, false, false, owner: owner),
            expectedStatus: PaymentStatus.Pending,
            expectedIntentStatus: PaymentIntentStatus.Pending,
            expectControllerFailure: true);
    }

    private static async Task AssertSecurityMutationDoesNotFinalizeAsync(
        string scenarioName,
        Func<PaymobOrderFixture, string> payloadFactory,
        PaymentStatus expectedStatus,
        PaymentIntentStatus expectedIntentStatus,
        bool expectControllerFailure = false,
        bool shouldFinalize = false)
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var harness = CreateHarness(context, tenantId);
        var fixture = await SeedFreshPaymobOrderAsync(context, harness.StockRepository, tenantId, $"ORD-P8-SEC-{scenarioName.Replace(' ', '-').ToUpperInvariant()}");
        var payload = payloadFactory(fixture);
        var hmac = ComputePaymobWebhookHmac(payload);

        var response = await PostPaymobWebhookAsync(harness.Controller, payload, hmac);
        if (expectControllerFailure)
        {
            Assert.IsType<ObjectResult>(response);
        }
        else
        {
            AssertOk(response);
        }

        var snapshot = await SnapshotAsync(context, fixture);
        Assert.Equal(expectedStatus, snapshot.PaymentStatus);
        Assert.Equal(expectedIntentStatus, snapshot.PaymentIntentStatus);
        if (!shouldFinalize)
        {
            Assert.NotEqual(PaymentStatus.Collected, snapshot.PaymentStatus);
            Assert.NotEqual(PaymentIntentStatus.Paid, snapshot.PaymentIntentStatus);
            Assert.NotEqual(OrderStatus.Confirmed, snapshot.OrderStatus);
        }
        Assert.True(snapshot.StockQuantity is 9m or 8m, $"Unexpected stock quantity for {scenarioName}: {snapshot.StockQuantity}");
        Assert.True(snapshot.StockMovementCount is 1 or 2, $"Unexpected stock movement count for {scenarioName}: {snapshot.StockMovementCount}");
    }

    private static PaymobHarness CreateHarness(ApplicationDbContext context, Guid tenantId)
    {
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        var stockRepository = new ProductVariantStockRepository(context, NullLogger<ProductVariantStockRepository>.Instance);
        var lifecycleService = CreateLifecycleService(context, stockRepository);
        var tenantConfigurationResolver = new Mock<IPaymobTenantConfigurationResolver>();
        tenantConfigurationResolver
            .Setup(resolver => resolver.ResolveCredentialsAsync(
                It.IsAny<Guid>(),
                It.IsAny<PaymobOperationalCapability>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymobTenantRuntimeCredentials(
                tenantId,
                Guid.NewGuid(),
                "egy_pk_test_p8_redacted",
                "egy_sk_test_p8_redacted",
                PaymobIntegrationId.ToString(),
                HmacSecret,
                true,
                new Uri("https://api.example.test/api/webhooks/paymob"),
                new Uri("https://store.example.test/checkout/pending"),
                MerchantId: PaymobMerchantOwnerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                IframeId: "test-iframe"));

        var nextProviderOrderId = 910000001;
        var runtimeApplication = new Mock<IPaymobRuntimeApplicationService>();
        runtimeApplication
            .Setup(service => service.CreatePaymentIntentAsync(
                It.IsAny<PaymobRuntimePaymentIntentApplicationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymobRuntimePaymentIntentApplicationRequest request, CancellationToken _) =>
            {
                var providerOrderId = System.Threading.Interlocked.Increment(ref nextProviderOrderId).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var clientSecret = $"cs_p8_{providerOrderId}";
                var iframeUrl = $"https://accept.paymob.com/unifiedcheckout/?publicKey=pk_test_p8&clientSecret={clientSecret}";
                var evidence = new PointOfSale.Core.Payments.Runtime.Domain.ProviderEvidence();
                var runtimeResult = new PointOfSale.Core.Payments.Runtime.Results.RuntimeResult();
                var executionResult = new PaymobExecutionResult
                {
                    ProviderEvidence = evidence,
                    RuntimeResult = runtimeResult,
                    ExecutionStatus = PaymobExecutionStatus.RuntimeSucceeded
                };

                return new PaymobRuntimeApplicationResult(
                    iframeUrl,
                    clientSecret,
                    providerOrderId,
                    evidence,
                    runtimeResult,
                    executionResult);
            });
        var runtimeIntegration = new PaymobWebhookRuntimeIntegrationService(
            context,
            new PaymobWebhookEvidenceAdapter(new PointOfSale.Core.Payments.Runtime.Domain.PaymentRuntimeOrchestrator()),
            NullLogger<PaymobWebhookRuntimeIntegrationService>.Instance);
        var artifactProtector = new PaymobSensitiveArtifactProtector(dataProtectionProvider);
        var paymobService = new PaymobPaymentService(
            context,
            dataProtectionProvider,
            new RedisConnectionFactory(
                Options.Create(new RedisSettings { Enabled = false, InstanceName = "p8" }),
                NullLogger<RedisConnectionFactory>.Instance),
            Mock.Of<IEventPublisher>(),
            lifecycleService,
            tenantConfigurationResolver.Object,
            runtimeApplication.Object,
            runtimeIntegration,
            artifactProtector,
            CreateStorefrontUrlResolver(),
            new PaymobRetryInventoryAuthority(context, stockRepository),
            NullLogger<PaymobPaymentService>.Instance);

        var tenantResolver = new PaymentWebhookTenantResolver(
            context,
            NullLogger<PaymentWebhookTenantResolver>.Instance);
        var requestPipeline = new PaymentWebhookRequestPipeline(
            tenantResolver,
            NullLogger<PaymentWebhookRequestPipeline>.Instance);
        var controller = new PaymobWebhooksController(
            paymobService,
            requestPipeline,
            NullLogger<PaymobWebhooksController>.Instance);

        return new PaymobHarness(
            controller,
            paymobService,
            stockRepository,
            artifactProtector,
            runtimeApplication);
    }

    private static PaymentLifecycleService CreateLifecycleService(
        ApplicationDbContext context,
        ProductVariantStockRepository stockRepository)
    {
        var outbox = new Mock<IAccountingOutboxService>();
        outbox.SetReturnsDefault(Task.CompletedTask);
        outbox
            .Setup(service => service.EnqueueOnlinePaymentAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (Guid tenantId, Guid orderId, string paymentIntentId, string gateway, CancellationToken ct) =>
            {
                var idempotencyKey = $"ONLINE_PAYMENT:{tenantId}:{orderId}:{paymentIntentId}";
                if (await context.AccountingOutboxMessages.IgnoreQueryFilters().AnyAsync(message =>
                        message.TenantId == tenantId && message.IdempotencyKey == idempotencyKey, ct))
                {
                    return;
                }

                context.AccountingOutboxMessages.Add(PointOfSale.Core.Entities.Accounting.AccountingOutboxMessage.Create(
                    tenantId,
                    "OnlinePayment",
                    orderId,
                    "OnlinePayment",
                    JsonSerializer.Serialize(new { orderId, paymentIntentId, gateway }),
                    idempotencyKey,
                    "p8-paymob-simulation"));
                await context.SaveChangesAsync(ct);
            });
        outbox
            .Setup(service => service.EnqueueUnresolvedOnlinePaymentAsync(
                It.IsAny<Guid>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (Guid tenantId, decimal amount, string currency, string gateway, string providerEventId, Guid businessEntityId, string? paymentReference, CancellationToken ct) =>
            {
                var idempotencyKey = $"UNRESOLVED_PAYMENT:{tenantId}:{gateway}:{providerEventId}";
                if (await context.AccountingOutboxMessages.IgnoreQueryFilters().AnyAsync(message =>
                        message.TenantId == tenantId && message.IdempotencyKey == idempotencyKey, ct))
                {
                    return;
                }

                context.AccountingOutboxMessages.Add(PointOfSale.Core.Entities.Accounting.AccountingOutboxMessage.Create(
                    tenantId,
                    "UnresolvedOnlinePayment",
                    businessEntityId,
                    "UnresolvedOnlinePayment",
                    JsonSerializer.Serialize(new { amount, currency, gateway, providerEventId, paymentReference }),
                    idempotencyKey,
                    "p8-paymob-simulation"));
                await context.SaveChangesAsync(ct);
            });

        var commerceAccounting = new Mock<ICommerceAccountingService>();
        commerceAccounting
            .Setup(service => service.PostOnlinePaymentCapturedAsync(
                It.IsAny<Order>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostingResult.Succeeded(Guid.NewGuid(), "P8-ONLINE-JOURNAL"));
        commerceAccounting
            .Setup(service => service.PostResolvedUnresolvedPaymentAsync(
                It.IsAny<Order>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostingResult.Succeeded(Guid.NewGuid(), "P8-SUSPENSE-JOURNAL"));

        var couponValidation = new Mock<ICouponValidationService>();
        couponValidation
            .Setup(service => service.ConfirmCouponUsageAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CouponConfirmationDecision.None());
        couponValidation
            .Setup(service => service.ReleaseCouponReservationAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CouponReleaseDecision.None());

        var correlation = new Mock<ICorrelationIdAccessor>();
        correlation.Setup(accessor => accessor.GetCorrelationId()).Returns("p8-paymob-simulation");
        var marketplaceOutbox = new Mock<IMarketplaceEventOutboxService>();
        marketplaceOutbox.SetReturnsDefault(Task.CompletedTask);

        return new PaymentLifecycleService(
            context,
            outbox.Object,
            commerceAccounting.Object,
            Mock.Of<IEventPublisher>(),
            marketplaceOutbox.Object,
            correlation.Object,
            stockRepository,
            Mock.Of<IInventoryReservationMetricsService>(),
            couponValidation.Object,
            NullLogger<PaymentLifecycleService>.Instance,
            Options.Create(new PaymentLifecycleSettings
            {
                RequireRowLevelLocking = false
            }));
    }

    private static async Task<PaymobOrderFixture> SeedFreshPaymobOrderAsync(
        ApplicationDbContext context,
        ProductVariantStockRepository stockRepository,
        Guid tenantId,
        string orderNumber)
    {
        var now = DateTime.UtcNow;
        var warehouseId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var paymentIntentId = Guid.NewGuid();
        var providerIntentId = "910000001";
        var paymentReference = $"PMB-P8-{paymentIntentId:N}"[..24];

        if (!await context.Currencies.AnyAsync(currency => currency.Code == PaymobCurrency))
        {
            context.Currencies.Add(new Currency
            {
                Code = PaymobCurrency,
                Name = "Egyptian Pound",
                Symbol = "EGP",
                DecimalPlaces = 2
            });
        }

        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "P8 Paymob Tenant",
            Slug = $"p8-{tenantId:N}"[..20],
            Currency = PaymobCurrency,
            IsActive = true,
            Status = TenantStatus.Active,
        });
        context.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "P8 Main Warehouse",
            IsActive = true,
        });
        context.Categories.Add(new Category
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "Phones",
        });
        context.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Nokia N73",
            CategoryId = categoryId,
            SalePrice = OrderTotal,
            OnlinePrice = OrderTotal,
            PurchasePrice = 250m,
            IsActive = true,
        });
        context.ProductVariants.Add(new ProductVariant
        {
            Id = variantId,
            TenantId = tenantId,
            ProductId = productId,
            SKU = "NOKIA-N73-YELLOW-32",
            Price = OrderTotal,
            OnlinePrice = OrderTotal,
            CostPrice = 250m,
            IsDefault = false,
            IsActive = true,
        });
        context.ProductVariantStocks.Add(new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WarehouseId = warehouseId,
            VariantId = variantId,
            Quantity = 9m,
            ReservedQuantity = 0m,
            DamagedQuantity = 0m,
        });
        await context.SaveChangesAsync();

        var reserved = await stockRepository.ReserveAsync(
            tenantId,
            variantId,
            warehouseId,
            quantity: 1m,
            orderId,
            expiresAtUtc: now.AddMinutes(30));
        Assert.True(reserved);

        var order = new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = orderNumber,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            FulfillmentStatus = FulfillmentStatus.Created,
            FulfillmentMode = FulfillmentMode.Manual,
            ShippingAddress = "123 P8 Simulation Street, Cairo",
            Subtotal = OrderTotal,
            Total = OrderTotal,
            Currency = PaymobCurrency,
            PaymentMethod = PointOfSale.Core.Enums.PaymentMethod.Card,
            PaymentMethodCode = "PAYMOB",
            PaymentMethodName = "Paymob",
            PaymentGateway = "paymob",
            PaymentReference = paymentReference,
            CreatedAt = now
        };
        order.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            ProductId = productId,
            VariantId = variantId,
            WarehouseId = warehouseId,
            SKU = "NOKIA-N73-YELLOW-32",
            ProductName = "Nokia N73",
            VariantName = "yellow / 32",
            Quantity = 1,
            UnitPrice = OrderTotal,
            DiscountAmount = 0m,
            TaxAmount = 0m,
            Total = OrderTotal,
            CostPrice = 250m,
        });
        order.TransitionCheckoutState(CheckoutState.PaymentPending);
        context.Orders.Add(order);
        context.PaymentIntents.Add(new PaymentIntent
        {
            Id = paymentIntentId,
            TenantId = tenantId,
            OrderId = orderId,
            Amount = OrderTotal,
            Currency = PaymobCurrency,
            PaymentMethod = PointOfSale.Core.Enums.PaymentMethod.Card,
            Provider = PaymentProvider.Paymob,
            ProviderIntentId = providerIntentId,
            PaymentReference = paymentReference,
            IdempotencyKey = $"paymob:intent:p8:{orderId:N}",
            Status = PaymentIntentStatus.Pending,
            CreatedAt = now,
            ExpiresAtUtc = now.AddMinutes(60),
            ProviderMetadata = PaymentIntentRoutingMetadataSerializer.Serialize(
                tenantId,
                orderId,
                "https://p8-store.example.test",
                protectedPaymobSession: "p8-protected-session-placeholder")
        });
        await context.SaveChangesAsync();

        await stockRepository.BindOrderReservationsAsync(tenantId, orderId, order.Items.ToList());

        return new PaymobOrderFixture(
            tenantId,
            orderId,
            orderNumber,
            paymentIntentId,
            providerIntentId,
            paymentReference,
            warehouseId,
            variantId);
    }

    private static async Task<IActionResult> PostPaymobWebhookAsync(
        PaymobWebhooksController controller,
        string rawPayload,
        string? hmac)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            TraceIdentifier = $"p8-{Guid.NewGuid():N}"
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.ContentType = "application/json";
        if (hmac is not null)
        {
            context.Request.Headers["Hmac"] = hmac;
        }
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(rawPayload));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = context
        };

        return await controller.HandleWebhook(CancellationToken.None);
    }

    private static void AssertOk(IActionResult result)
    {
        if (result is not OkObjectResult ok)
        {
            var status = result is ObjectResult objectResult ? objectResult.StatusCode?.ToString() ?? "object-no-status" : result.GetType().Name;
            var body = result is ObjectResult objectResultWithBody ? JsonSerializer.Serialize(objectResultWithBody.Value) : string.Empty;
            throw new Xunit.Sdk.XunitException($"Expected 200 OK but received {status}: {body}");
        }

        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
    }

    private static string CreatePaymobWebhookPayload(
        long transactionId,
        string paymobOrderId,
        Guid merchantOrderId,
        bool success,
        bool pending,
        bool isAuth,
        bool isCapture,
        bool isStandalonePayment,
        bool isVoided,
        bool isRefunded,
        bool errorOccurred,
        long amountCents = OrderTotalCents,
        string currency = PaymobCurrency,
        long integrationId = PaymobIntegrationId,
        long owner = PaymobMerchantOwnerId)
    {
        return JsonSerializer.Serialize(new
        {
            type = "TRANSACTION",
            obj = new
            {
                id = transactionId,
                pending,
                amount_cents = amountCents,
                success,
                is_auth = isAuth,
                is_capture = isCapture,
                is_standalone_payment = isStandalonePayment,
                is_voided = isVoided,
                is_refunded = isRefunded,
                is_3d_secure = false,
                integration_id = integrationId,
                has_parent_transaction = false,
                order = new
                {
                    id = long.Parse(paymobOrderId),
                    merchant_order_id = merchantOrderId.ToString("D")
                },
                created_at = DateTime.UtcNow.AddMinutes(5).ToString("O"),
                currency,
                source_data = new
                {
                    pan = "411111******1111",
                    sub_type = "Visa",
                    type = "card"
                },
                owner,
                error_occured = errorOccurred,
                data = new
                {
                    message = success ? "Approved" : "Declined",
                    txn_response_code = success ? "APPROVED" : "DECLINED"
                }
            }
        });
    }

    private static string ComputePaymobWebhookHmac(string rawPayload)
    {
        var callback = PaymobWebhookPayloadParser.Parse(rawPayload);
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(HmacSecret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(callback.HmacPayload))).ToLowerInvariant();
    }

    private static async Task<PaymentSnapshot> SnapshotAsync(
        ApplicationDbContext context,
        PaymobOrderFixture fixture)
    {
        context.ChangeTracker.Clear();
        var order = await context.Orders
            .IgnoreQueryFilters()
            .SingleAsync(entity => entity.Id == fixture.OrderId);
        var paymentIntent = await context.PaymentIntents
            .IgnoreQueryFilters()
            .SingleAsync(entity => entity.Id == fixture.PaymentIntentId);
        var stock = await context.ProductVariantStocks
            .IgnoreQueryFilters()
            .SingleAsync(entity => entity.TenantId == fixture.TenantId && entity.VariantId == fixture.VariantId);
        var reservation = await context.InventoryReservations
            .IgnoreQueryFilters()
            .SingleAsync(entity => entity.TenantId == fixture.TenantId && entity.OrderId == fixture.OrderId);

        return new PaymentSnapshot(
            order.Status,
            order.PaymentStatus,
            order.CheckoutState,
            order.IsPaid,
            order.PaidAt,
            order.PaymentGateway,
            paymentIntent.Status,
            paymentIntent.Provider,
            paymentIntent.ProviderIntentId,
            paymentIntent.ProviderPaymentId,
            paymentIntent.Amount,
            paymentIntent.Currency,
            await context.PaymentIntents.IgnoreQueryFilters().CountAsync(entity => entity.OrderId == fixture.OrderId),
            await context.Orders.IgnoreQueryFilters().CountAsync(entity => entity.Id == fixture.OrderId),
            await context.InventoryReservations.IgnoreQueryFilters().CountAsync(entity => entity.OrderId == fixture.OrderId),
            await context.PaymentWebhookLogs.IgnoreQueryFilters().CountAsync(entity => entity.OrderId == fixture.OrderId),
            await context.PaymobWebhookReplayRecords.IgnoreQueryFilters().CountAsync(entity => entity.OrderId == fixture.OrderId),
            await context.StockMovements.IgnoreQueryFilters().CountAsync(entity => entity.ReferenceId == fixture.OrderId),
            stock.Quantity,
            stock.ReservedQuantity,
            reservation.CommittedQuantity,
            reservation.ReleasedQuantity,
            reservation.Status);
    }

    private static StorefrontUrlResolver CreateStorefrontUrlResolver()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storefront:UrlTemplate"] = "https://{slug}.example.test"
            })
            .Build();

        return new StorefrontUrlResolver(
            configuration,
            NullLogger<StorefrontUrlResolver>.Instance,
            services.GetRequiredService<IServiceScopeFactory>());
    }

    private static async Task<ApplicationDbContext> CreateDbContextAsync(SqliteConnection connection, Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new SqlitePaymobP8DbContext(options, new TestTenantContext(tenantId));
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private sealed class SqlitePaymobP8DbContext : ApplicationDbContext
    {
        public SqlitePaymobP8DbContext(
            DbContextOptions<ApplicationDbContext> options,
            TestTenantContext tenantContext)
            : base(options, tenantContext)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var constraint in entityType.GetCheckConstraints().ToArray())
                {
                    entityType.RemoveCheckConstraint(constraint.Name);
                }

                foreach (var property in entityType.GetProperties()
                             .Where(property => string.Equals(
                                 property.GetColumnName(),
                                 "xmin",
                                 StringComparison.OrdinalIgnoreCase)))
                {
                    property.SetColumnType("INTEGER");
                    property.ValueGenerated = ValueGenerated.Never;
                    property.IsConcurrencyToken = false;
                    property.SetDefaultValue(0u);
                }
            }
        }
    }

    private sealed record PaymobHarness(
        PaymobWebhooksController Controller,
        PaymobPaymentService PaymobService,
        ProductVariantStockRepository StockRepository,
        IPaymobSensitiveArtifactProtector ArtifactProtector,
        Mock<IPaymobRuntimeApplicationService> RuntimeApplication);

    private sealed record PaymobOrderFixture(
        Guid TenantId,
        Guid OrderId,
        string OrderNumber,
        Guid PaymentIntentId,
        string ProviderIntentId,
        string PaymentReference,
        Guid WarehouseId,
        Guid VariantId);

    private sealed record PaymentSnapshot(
        OrderStatus OrderStatus,
        PaymentStatus PaymentStatus,
        CheckoutState CheckoutState,
        bool IsPaid,
        DateTime? PaidAt,
        string? PaymentGateway,
        PaymentIntentStatus PaymentIntentStatus,
        PaymentProvider Provider,
        string? ProviderIntentId,
        string? ProviderPaymentId,
        decimal PaymentIntentAmount,
        string PaymentIntentCurrency,
        int PaymentIntentCount,
        int OrderCount,
        int ReservationCount,
        int WebhookLogCount,
        int ReplayRecordCount,
        int StockMovementCount,
        decimal StockQuantity,
        decimal ReservedQuantity,
        decimal CommittedQuantity,
        decimal ReleasedQuantity,
        InventoryReservationStatus ReservationStatus);
}
