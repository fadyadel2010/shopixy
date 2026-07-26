using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Instrumentation;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubPaymentServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<ICommerceHubFinancialWorkflowService> _financialWorkflowServiceMock = new();
    private readonly Mock<IInventoryConsistencyService> _inventoryConsistencyServiceMock = new();
    private readonly Mock<ICommerceHubFinancialReadinessGate> _financialReadinessGateMock = new();
    private readonly Mock<ICommerceHubOrderAccessService> _orderAccessServiceMock = new();
    private readonly Mock<IEventPublisher> _eventPublisherMock = new();
    private readonly Mock<IDistributedLockService> _lockServiceMock = new();
    private readonly Mock<IPaymentProviderAdapter> _paymobAdapterMock = new();
    private readonly CommerceHubPaymentService _service;

    public CommerceHubPaymentServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ApplicationDbContext(options);

        _inventoryConsistencyServiceMock
            .Setup(service => service.EnsureMarketplaceOrderReservationsActiveAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _inventoryConsistencyServiceMock
            .Setup(service => service.FinalizeMarketplaceOrderReservationsForPaymentAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _financialWorkflowServiceMock
            .Setup(service => service.ApplyOnlinePaymentSuccessAsync(
                It.IsAny<CommerceHubMarketplaceOrder>(),
                It.IsAny<CommerceHubPayment>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _financialReadinessGateMock
            .Setup(gate => gate.EnsureCommerceIntakeAllowedAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _lockServiceMock
            .Setup(service => service.TryAcquireLockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>()))
            .ReturnsAsync(DistributedLockResult.Acquired());
        _lockServiceMock
            .Setup(service => service.ReleaseLockAsync(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .ReturnsAsync(true);
        _paymobAdapterMock
            .SetupGet(adapter => adapter.ProviderCode)
            .Returns(new ProviderCode(CommerceHubPaymentMethods.Paymob));

        _service = new CommerceHubPaymentService(
            _dbContext,
            _financialWorkflowServiceMock.Object,
            _inventoryConsistencyServiceMock.Object,
            _financialReadinessGateMock.Object,
            _orderAccessServiceMock.Object,
            _eventPublisherMock.Object,
            _lockServiceMock.Object,
            new[] { _paymobAdapterMock.Object },
            Options.Create(new CommerceHubPaymentsOptions()),
            new CommerceHubPaymentOrchestrationTelemetry(),
            NullLogger<CommerceHubPaymentService>.Instance);
    }

    private void VerifyPaymobAdapterNotCalled()
    {
        _paymobAdapterMock.Verify(adapter => adapter.CreateSessionAsync(
            It.IsAny<PaymentProviderSessionRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InitiateAsync_WhenDurableReinitFenceExistsWithoutProviderIdentity_ShouldBlockAnotherSession()
    {
        var order = await SeedPendingPaymobOrderWithReinitFenceAsync();

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _service.InitiateAsync(
                new CommerceHubPaymentInitiateRequestDto
                {
                    OrderId = order.Id.ToString(),
                    Provider = CommerceHubPaymentMethods.Paymob
                },
                order.CustomerId,
                "trace-reinit-fence"));

        var storedPayment = await _dbContext.CommerceHubPayments
            .Include(payment => payment.Attempts)
            .SingleAsync();

        Assert.Equal("PAYMENT_PROVIDER_SESSION_RECONCILIATION_REQUIRED", exception.Code);
        Assert.Equal(409, exception.StatusCode);
        Assert.StartsWith("PROVIDER_SESSION_REINIT_FENCE:", storedPayment.FailureReason, StringComparison.Ordinal);
        Assert.Single(storedPayment.Attempts);
        Assert.StartsWith("PROVIDER_SESSION_REINIT_FENCE:", storedPayment.Attempts.Single().FailureReason, StringComparison.Ordinal);

        _financialReadinessGateMock.Verify(gate => gate.EnsureCommerceIntakeAllowedAsync(
            It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _paymobAdapterMock.Verify(adapter => adapter.CreateSessionAsync(
            It.IsAny<PaymentProviderSessionRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InitiateAsync_WhenAuthenticatedCustomerDoesNotOwnOrder_AndTokenIsInvalid_ShouldDenyAccess()
    {
        var order = await SeedPendingPaymobOrderWithReinitFenceAsync();
        _orderAccessServiceMock
            .Setup(service => service.ValidateAccessToken(order.Id, order.OrderNumber, It.IsAny<string?>()))
            .Returns(false);

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _service.InitiateAsync(
                new CommerceHubPaymentInitiateRequestDto
                {
                    OrderId = order.Id.ToString(),
                    Provider = CommerceHubPaymentMethods.Paymob
                },
                Guid.NewGuid(),
                "trace-access-denied"));

        Assert.Equal("ORDER_ACCESS_DENIED", exception.Code);
        Assert.Equal(403, exception.StatusCode);

        _financialReadinessGateMock.Verify(gate => gate.EnsureCommerceIntakeAllowedAsync(
            It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        VerifyPaymobAdapterNotCalled();
    }

    [Fact]
    public async Task InitiateAsync_WhenSignedOrderAccessTokenIsValid_ShouldContinueToPaymentAuthorityChecks()
    {
        var order = await SeedPendingPaymobOrderWithReinitFenceAsync();
        _orderAccessServiceMock
            .Setup(service => service.ValidateAccessToken(order.Id, order.OrderNumber, "valid-token"))
            .Returns(true);

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _service.InitiateAsync(
                new CommerceHubPaymentInitiateRequestDto
                {
                    OrderId = order.Id.ToString(),
                    Provider = CommerceHubPaymentMethods.Paymob,
                    OrderAccessToken = "valid-token"
                },
                null,
                "trace-valid-token"));

        Assert.Equal("PAYMENT_PROVIDER_SESSION_RECONCILIATION_REQUIRED", exception.Code);
        VerifyPaymobAdapterNotCalled();
    }

    [Theory]
    [InlineData("PendingTenantResolution")]
    [InlineData("RoutedToSuspense")]
    [InlineData("SuspenseMatchedPaymentPendingClearance")]
    public async Task InitiateAsync_WhenOrderHasUnresolvedCapturedPaymentEvidence_ShouldBlockRetry(string status)
    {
        var order = await SeedPendingPaymobOrderAsync();
        _dbContext.CommerceHubUnresolvedPaymentWebhooks.Add(new CommerceHubUnresolvedPaymentWebhook
        {
            Id = Guid.NewGuid(),
            Provider = PaymentProvider.Paymob,
            ProviderEventId = $"evt_{Guid.NewGuid():N}",
            ProviderReference = order.PaymentReference ?? order.OrderNumber,
            OrderId = order.Id,
            TenantId = order.SubOrders.Single().StoreId,
            Amount = order.TotalAmount,
            Currency = order.Currency,
            EventType = "transaction_processed",
            PayloadHash = new string('a', 64),
            RawPayload = "{}",
            Status = status
        });
        await _dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            _service.InitiateAsync(
                new CommerceHubPaymentInitiateRequestDto
                {
                    OrderId = order.Id.ToString(),
                    Provider = CommerceHubPaymentMethods.Paymob
                },
                order.CustomerId,
                "trace-unresolved-captured-payment"));

        Assert.Equal("PAYMENT_RECONCILIATION_REQUIRED", exception.Code);
        Assert.Equal(409, exception.StatusCode);

        _inventoryConsistencyServiceMock.Verify(service => service.EnsureMarketplaceOrderReservationsActiveAsync(
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _lockServiceMock.Verify(service => service.TryAcquireLockAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<TimeSpan>()), Times.Never);
        VerifyPaymobAdapterNotCalled();
    }

    [Fact]
    public async Task PersistUnresolvedPaymentWebhookAsync_WhenKnownOrderLockIsBusy_ShouldPersistDurableEvidenceWithoutRetryDependency()
    {
        var order = await SeedPendingPaymobOrderAsync();
        const string providerEventId = "evt_test_lock_contention";
        const string providerIntentId = "pi_test_lock_contention";
        const string providerTransactionId = "ch_test_lock_contention";

        _lockServiceMock
            .Setup(service => service.TryAcquireLockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>()))
            .ReturnsAsync(DistributedLockResult.AlreadyHeld());

        var method = typeof(CommerceHubPaymentService).GetMethod(
            "PersistUnresolvedPaymentWebhookAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);

        var operation = (Task<bool>)method!.Invoke(_service, new object?[]
        {
            PaymentProvider.Stripe,
            CommerceHubPaymentMethods.Stripe,
            "payment_intent.succeeded",
            providerEventId,
            order.PaymentReference,
            null,
            providerIntentId,
            providerTransactionId,
            order.TotalAmount,
            order.Currency,
            "{\"id\":\"evt_test_lock_contention\"}",
            "trace-lock-contention",
            CancellationToken.None,
            true,
            null,
            null
        })!;

        var stored = await operation;

        Assert.True(stored);

        var unresolved = await _dbContext.CommerceHubUnresolvedPaymentWebhooks.SingleAsync();

        Assert.Equal(PaymentProvider.Stripe, unresolved.Provider);
        Assert.Equal(providerEventId, unresolved.ProviderEventId);
        Assert.Equal(order.PaymentReference, unresolved.ProviderReference);
        Assert.Equal(providerIntentId, unresolved.ProviderIntentId);
        Assert.Equal(providerTransactionId, unresolved.ProviderTransactionId);
        Assert.Equal(order.Id, unresolved.OrderId);
        Assert.Equal(order.SubOrders.Single().StoreId, unresolved.TenantId);
        Assert.Equal("PendingTenantResolution", unresolved.Status);
        Assert.Equal("[]", unresolved.SuspenseRoutedStoreIdsJson);
        Assert.Contains("payment authority lock was unavailable", unresolved.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deferred until reconciliation", unresolved.LastError, StringComparison.OrdinalIgnoreCase);

        _lockServiceMock.Verify(service => service.TryAcquireLockAsync(
            It.Is<string>(key => key.Contains(order.Id.ToString("N"), StringComparison.OrdinalIgnoreCase)),
            It.IsAny<string>(),
            It.IsAny<TimeSpan>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateLegacyPaymobPaymentAsync_WhenOrderIsAlreadyFinalized_ShouldNotRecreatePaymentAuthority()
    {
        var order = await SeedPendingPaymobOrderAsync();
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPayment())
        using (CommerceHubBoundaryMutationScope.BeginPaymentLifecycle())
        {
            order.Status = OrderStatus.Confirmed;
            order.PaymentStatus = PaymentStatus.Collected;
            await _dbContext.SaveChangesAsync();
        }

        var method = typeof(CommerceHubPaymentService).GetMethod(
            "CreateLegacyPaymobPaymentAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);

        var operation = (Task<CommerceHubPayment?>)method!.Invoke(_service, new object?[]
        {
            order.PaymentReference,
            "paymob-order-finalized-replay",
            CancellationToken.None
        })!;

        var payment = await operation;

        Assert.Null(payment);
        Assert.Empty(await _dbContext.CommerceHubPayments.ToListAsync());

        _inventoryConsistencyServiceMock.Verify(service => service.EnsureMarketplaceOrderReservationsActiveAsync(
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplyWebhookOutcomeAsync_WhenProviderSuccessIsVerified_CompletesCanonicalPipelineOnce()
    {
        var payment = await SeedPendingPaymentAsync();

        var result = await InvokeApplyWebhookOutcomeAsync(
            payment,
            isSuccess: true,
            transactionId: "txn-success-1");

        var storedPayment = await _dbContext.CommerceHubPayments.SingleAsync(item => item.Id == payment.Id);
        var storedOrder = await _dbContext.CommerceHubMarketplaceOrders
            .Include(order => order.SubOrders)
            .SingleAsync(order => order.Id == payment.OrderId);

        Assert.True(result.Processed);
        Assert.Equal(CommerceHubPaymentRecordStatus.Paid, storedPayment.Status);
        Assert.True(storedPayment.IsProcessed);
        Assert.Equal(PaymentStatus.Collected, storedOrder.PaymentStatus);
        Assert.All(storedOrder.SubOrders, subOrder => Assert.Equal(PaymentStatus.Collected, subOrder.PaymentStatus));

        _inventoryConsistencyServiceMock.Verify(service => service.FinalizeMarketplaceOrderReservationsForPaymentAsync(
            storedOrder.Id,
            payment.Id,
            payment.Id,
            "trace-canonical-success",
            "CommerceHubCanonicalPaymentCompletion",
            It.IsAny<CancellationToken>()), Times.Once);
        _financialWorkflowServiceMock.Verify(service => service.ApplyOnlinePaymentSuccessAsync(
            It.Is<CommerceHubMarketplaceOrder>(order => order.Id == storedOrder.Id),
            It.Is<CommerceHubPayment>(item => item.Id == payment.Id),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _eventPublisherMock.Verify(publisher => publisher.PublishAsync(
            It.IsAny<CommerceHubPaymentCompletedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _eventPublisherMock.Verify(publisher => publisher.PublishAsync(
            It.IsAny<CommerceHubPaymentNotificationsRequestedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyWebhookOutcomeAsync_WhenPaymentAlreadyCompleted_DoesNotDuplicateCompletionSideEffects()
    {
        var payment = await SeedPendingPaymentAsync();
        await InvokeApplyWebhookOutcomeAsync(payment, isSuccess: true, transactionId: "txn-duplicate-1");
        _eventPublisherMock.Invocations.Clear();

        var replay = await _dbContext.CommerceHubPayments.SingleAsync(item => item.Id == payment.Id);
        var result = await InvokeApplyWebhookOutcomeAsync(replay, isSuccess: true, transactionId: "txn-duplicate-1");

        Assert.True(result.Processed);
        Assert.Equal("Webhook was already applied.", result.Message);
        _inventoryConsistencyServiceMock.Verify(service => service.FinalizeMarketplaceOrderReservationsForPaymentAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<string?>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _financialWorkflowServiceMock.Verify(service => service.ApplyOnlinePaymentSuccessAsync(
            It.IsAny<CommerceHubMarketplaceOrder>(),
            It.IsAny<CommerceHubPayment>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _eventPublisherMock.Verify(publisher => publisher.PublishAsync(
            It.IsAny<CommerceHubPaymentCompletedEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private async Task<CommerceHubPaymentWebhookResultDto> InvokeApplyWebhookOutcomeAsync(
        CommerceHubPayment payment,
        bool isSuccess,
        string transactionId)
    {
        var method = typeof(CommerceHubPaymentService).GetMethod(
            "ApplyWebhookOutcomeAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);

        var operation = (Task<CommerceHubPaymentWebhookResultDto>)method!.Invoke(_service, new object?[]
        {
            payment,
            CommerceHubPaymentMethods.Paymob,
            isSuccess,
            payment.Amount,
            payment.Currency,
            payment.ProviderIntentId,
            transactionId,
            "TRANSACTION",
            transactionId,
            "{}",
            isSuccess ? null : "Provider failed payment.",
            false,
            isSuccess ? "trace-canonical-success" : "trace-canonical-failed",
            CancellationToken.None
        })!;

        return await operation;
    }

    private async Task<CommerceHubMarketplaceOrder> SeedPendingPaymobOrderWithReinitFenceAsync()
    {
        const string reinitFenceReason =
            "PROVIDER_SESSION_REINIT_FENCE: A PAYMOB provider session attempt may already exist externally. Reconcile or persist provider identity before creating another session.";

        var order = new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            OrderNumber = $"CH-PAY-{Guid.NewGuid():N}"[..15],
            FullName = "Payment Fence Test",
            PhoneNumber = "01000000000",
            AddressLine = "123 Fence Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = CommerceHubPaymentMethods.Paymob,
            PaymentMethodName = "Paymob",
            Subtotal = 100m,
            ShippingTotal = 0m,
            TotalAmount = 100m,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending
        };

        var subOrder = new CommerceHubSubOrder
        {
            Id = Guid.NewGuid(),
            MarketplaceOrderId = order.Id,
            MarketplaceOrder = order,
            OrderNumber = $"{order.OrderNumber}-1",
            StoreId = Guid.NewGuid(),
            StoreName = "Fence Store",
            Currency = "EGP",
            Subtotal = 100m,
            Shipping = 0m,
            PaymentMethod = CommerceHubPaymentMethod.Online,
            PaymentMethodCode = CommerceHubPaymentMethods.Paymob,
            PaymentMethodName = "Paymob",
            Total = 100m,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending
        };

        var payment = new CommerceHubPayment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Order = order,
            Amount = 100m,
            Currency = "EGP",
            Status = CommerceHubPaymentRecordStatus.Pending,
            Provider = PaymentProvider.Paymob,
            ProviderReference = $"paymob-{Guid.NewGuid():N}"[..39],
            FailureReason = reinitFenceReason,
            IsProcessed = false
        };

        var attempt = new CommerceHubPaymentAttempt
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            Payment = payment,
            Provider = PaymentProvider.Paymob,
            AttemptNumber = 1,
            ProviderReference = payment.ProviderReference,
            Status = CommerceHubPaymentRecordStatus.Pending,
            FailureReason = reinitFenceReason
        };

        order.SubOrders.Add(subOrder);
        order.Payments.Add(payment);
        payment.Attempts.Add(attempt);

        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPayment())
        {
            _dbContext.CommerceHubMarketplaceOrders.Add(order);
            await _dbContext.SaveChangesAsync();
        }

        return order;
    }

    private async Task<CommerceHubMarketplaceOrder> SeedPendingPaymobOrderAsync()
    {
        var order = new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            OrderNumber = $"CH-ORDER-{Guid.NewGuid():N}"[..17],
            FullName = "Pending Payment Test",
            PhoneNumber = "01000000000",
            AddressLine = "123 Pending Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = CommerceHubPaymentMethods.Paymob,
            PaymentMethodName = "Paymob",
            PaymentReference = $"payref-{Guid.NewGuid():N}"[..20],
            Subtotal = 120m,
            ShippingTotal = 0m,
            TotalAmount = 120m,
            Status = OrderStatus.OnHold,
            PaymentStatus = PaymentStatus.Pending
        };

        order.SubOrders.Add(new CommerceHubSubOrder
        {
            Id = Guid.NewGuid(),
            MarketplaceOrderId = order.Id,
            MarketplaceOrder = order,
            OrderNumber = $"{order.OrderNumber}-1",
            StoreId = Guid.NewGuid(),
            StoreName = "Pending Store",
            Currency = "EGP",
            Subtotal = 120m,
            Shipping = 0m,
            PaymentMethod = CommerceHubPaymentMethod.Online,
            PaymentMethodCode = CommerceHubPaymentMethods.Paymob,
            PaymentMethodName = "Paymob",
            Total = 120m,
            Status = OrderStatus.OnHold,
            PaymentStatus = PaymentStatus.Pending
        });

        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPayment())
        {
            _dbContext.CommerceHubMarketplaceOrders.Add(order);
            await _dbContext.SaveChangesAsync();
        }

        return order;
    }

    private async Task<CommerceHubPayment> SeedPendingPaymentAsync()
    {
        var order = await SeedPendingPaymobOrderAsync();
        var payment = new CommerceHubPayment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Amount = order.TotalAmount,
            Currency = order.Currency,
            Status = CommerceHubPaymentRecordStatus.Pending,
            Provider = PaymentProvider.Paymob,
            ProviderReference = $"payref-{Guid.NewGuid():N}"[..24],
            ProviderIntentId = $"intent-{Guid.NewGuid():N}"[..24]
        };

        payment.Attempts.Add(new CommerceHubPaymentAttempt
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            Provider = PaymentProvider.Paymob,
            AttemptNumber = 1,
            ProviderReference = payment.ProviderReference,
            ProviderIntentId = payment.ProviderIntentId,
            Status = CommerceHubPaymentRecordStatus.Pending
        });

        using (CommerceHubBoundaryMutationScope.BeginPayment())
        {
            _dbContext.CommerceHubPayments.Add(payment);
            await _dbContext.SaveChangesAsync();
        }

        return payment;
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
