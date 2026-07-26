using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.Payments;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.Adapters;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Payments;

public sealed class PaymentServiceCreatePaymentIntentGuardTests
{
    [Fact]
    public async Task CreatePaymentIntentAsync_WhenOrderStatusIsNotPending_RejectsFinalizedPayment()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var order = SeedOrder(tenantId, status: OrderStatus.Confirmed, paymentStatus: PaymentStatus.Pending);
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<PaymentOrderInvalidStateException>(() =>
            service.CreatePaymentIntentAsync(
                tenantId,
                order.Id,
                new CreatePaymentIntentRequestDto { Provider = "Fawry" },
                promotionCalculationFailed: false,
                requestId: Guid.NewGuid().ToString("N"),
                correlationId: "test-correlation",
                cancellationToken: CancellationToken.None));

        Assert.Equal("PAYMENT_ALREADY_FINALIZED", exception.ErrorCode);
        Assert.Empty(await dbContext.PaymentIntents.ToListAsync());
    }

    [Fact]
    public async Task CreatePaymentIntentAsync_WhenPaymentStatusIsNotPending_RejectsFinalizedPayment()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var order = SeedOrder(tenantId, status: OrderStatus.Pending, paymentStatus: PaymentStatus.Collected);
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<PaymentOrderInvalidStateException>(() =>
            service.CreatePaymentIntentAsync(
                tenantId,
                order.Id,
                new CreatePaymentIntentRequestDto { Provider = "Fawry" },
                promotionCalculationFailed: false,
                requestId: Guid.NewGuid().ToString("N"),
                correlationId: "test-correlation",
                cancellationToken: CancellationToken.None));

        Assert.Equal("PAYMENT_ALREADY_FINALIZED", exception.ErrorCode);
        Assert.Empty(await dbContext.PaymentIntents.ToListAsync());
    }

    [Fact]
    public async Task CreatePaymentIntentAsync_ReplayAfterSuccess_RejectsInsteadOfReturningCachedIntent()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var order = SeedOrder(tenantId, status: OrderStatus.Pending, paymentStatus: PaymentStatus.Pending);
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        const string requestId = "req-replay-after-success";

        var initialResponse = await service.CreatePaymentIntentAsync(
            tenantId,
            order.Id,
            new CreatePaymentIntentRequestDto { Provider = "Fawry" },
            promotionCalculationFailed: false,
            requestId,
            correlationId: "initial-attempt",
            cancellationToken: CancellationToken.None);

        var persistedOrder = await dbContext.Orders.SingleAsync(candidate => candidate.Id == order.Id);
        persistedOrder.PaymentStatus = PaymentStatus.Collected;
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<PaymentOrderInvalidStateException>(() =>
            service.CreatePaymentIntentAsync(
                tenantId,
                order.Id,
                new CreatePaymentIntentRequestDto { Provider = "Fawry" },
                promotionCalculationFailed: false,
                requestId,
                correlationId: "replay-attempt",
                cancellationToken: CancellationToken.None));

        Assert.Equal("PAYMENT_ALREADY_FINALIZED", exception.ErrorCode);
        Assert.Equal(1, await dbContext.PaymentIntents.CountAsync());
        Assert.NotEqual(Guid.Empty, initialResponse.PaymentIntentId);
    }

    [Fact]
    public async Task CreatePaymentIntentAsync_WhenManualWalletConfigMissing_ThrowsManualWalletNotConfigured()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var order = SeedOrder(
            tenantId,
            status: OrderStatus.Pending,
            paymentStatus: PaymentStatus.Pending,
            paymentMethod: PaymentMethod.Card,
            paymentMethodCode: "MANUALWALLET");
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<PaymentManualWalletNotConfiguredException>(() =>
            service.CreatePaymentIntentAsync(
                tenantId,
                order.Id,
                new CreatePaymentIntentRequestDto
                {
                    Provider = "ManualWallet",
                    WalletType = "VodafoneCash",
                    WalletPhoneNumber = "01011112222",
                    ProofFileKey = $"tenants/{tenantId:D}/manual-wallet-proofs/proof.png"
                },
                promotionCalculationFailed: false,
                requestId: Guid.NewGuid().ToString("N"),
                correlationId: "manual-wallet-missing-config",
                cancellationToken: CancellationToken.None));

        Assert.Equal("MANUAL_WALLET_NOT_CONFIGURED", exception.ErrorCode);
        Assert.Empty(await dbContext.PaymentIntents.ToListAsync());
    }

    [Fact]
    public async Task CreatePaymentIntentAsync_WhenManualWalletConfigExists_Proceeds()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        var dataProtector = dataProtectionProvider.CreateProtector("PaymentMethodSecrets");
        var order = SeedOrder(
            tenantId,
            status: OrderStatus.Pending,
            paymentStatus: PaymentStatus.Pending,
            paymentMethod: PaymentMethod.Card,
            paymentMethodCode: "MANUALWALLET");
        dbContext.Orders.Add(order);
        dbContext.TenantPaymentMethods.Add(new TenantPaymentMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "MANUALWALLET",
            DisplayName = "Manual Wallet",
            IsEnabled = true,
            SupportsOnline = true,
            SupportsOffline = false,
            ConfigEncrypted = dataProtector.Protect("""{"walletNumber":"01099998888"}""")
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, dataProtector);

        var response = await service.CreatePaymentIntentAsync(
            tenantId,
            order.Id,
            new CreatePaymentIntentRequestDto
            {
                Provider = "ManualWallet",
                WalletType = "VodafoneCash",
                WalletPhoneNumber = "01011112222",
                ProofFileKey = $"tenants/{tenantId:D}/manual-wallet-proofs/proof.png"
            },
            promotionCalculationFailed: false,
            requestId: Guid.NewGuid().ToString("N"),
            correlationId: "manual-wallet-configured",
            cancellationToken: CancellationToken.None);

        Assert.Equal(order.Id, response.OrderId);
        var paymentIntent = await dbContext.PaymentIntents.SingleAsync();
        Assert.Equal(PaymentProvider.ManualWallet, paymentIntent.Provider);
        Assert.Equal(
            $"tenants/{tenantId:D}/manual-wallet-proofs/proof.png",
            paymentIntent.ProofImageUrl);
        Assert.Contains("01099998888", paymentIntent.ProviderMetadata, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreatePaymentIntentAsync_WhenManualWalletProofIsExternalUrl_RejectsProofReference()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        var dataProtector = dataProtectionProvider.CreateProtector("PaymentMethodSecrets");
        var order = SeedOrder(
            tenantId,
            status: OrderStatus.Pending,
            paymentStatus: PaymentStatus.Pending,
            paymentMethod: PaymentMethod.Card,
            paymentMethodCode: "MANUALWALLET");
        dbContext.Orders.Add(order);
        dbContext.TenantPaymentMethods.Add(new TenantPaymentMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "MANUALWALLET",
            DisplayName = "Manual Wallet",
            IsEnabled = true,
            SupportsOnline = true,
            SupportsOffline = false,
            ConfigEncrypted = dataProtector.Protect("""{"walletNumber":"01099998888"}""")
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, dataProtector);

        var exception = await Assert.ThrowsAsync<PaymentManualWalletProofException>(() =>
            service.CreatePaymentIntentAsync(
                tenantId,
                order.Id,
                new CreatePaymentIntentRequestDto
                {
                    Provider = "ManualWallet",
                    WalletType = "VodafoneCash",
                    WalletPhoneNumber = "01011112222",
                    ProofFileKey = $"https://cdn.example.com/tenants/{tenantId:D}/manual-wallet-proofs/proof.png"
                },
                promotionCalculationFailed: false,
                requestId: Guid.NewGuid().ToString("N"),
                correlationId: "manual-wallet-invalid-proof",
                cancellationToken: CancellationToken.None));

        Assert.Equal("INVALID_MANUAL_WALLET_PROOF", exception.ErrorCode);
        Assert.Empty(await dbContext.PaymentIntents.ToListAsync());
    }

    [Fact]
    public async Task CreatePaymentIntentAsync_WhenOrderCurrencyIsMissing_RejectsRequest()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var order = SeedOrder(tenantId, status: OrderStatus.Pending, paymentStatus: PaymentStatus.Pending);
        order.Currency = string.Empty;
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<PaymentException>(() =>
            service.CreatePaymentIntentAsync(
                tenantId,
                order.Id,
                new CreatePaymentIntentRequestDto { Provider = "Fawry" },
                promotionCalculationFailed: false,
                requestId: Guid.NewGuid().ToString("N"),
                correlationId: "currency-missing",
                cancellationToken: CancellationToken.None));

        Assert.Equal("ORDER_CURRENCY_NOT_SET", exception.ErrorCode);
        Assert.Empty(await dbContext.PaymentIntents.ToListAsync());
    }

    [Fact]
    public async Task CreatePaymentIntentAsync_WhenOrderCurrencyDoesNotMatchTenant_RejectsRequest()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var order = SeedOrder(tenantId, status: OrderStatus.Pending, paymentStatus: PaymentStatus.Pending);
        order.Currency = "USD";
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<PaymentCurrencyMismatchException>(() =>
            service.CreatePaymentIntentAsync(
                tenantId,
                order.Id,
                new CreatePaymentIntentRequestDto { Provider = "Fawry" },
                promotionCalculationFailed: false,
                requestId: Guid.NewGuid().ToString("N"),
                correlationId: "currency-mismatch",
                cancellationToken: CancellationToken.None));

        Assert.Equal("USD", exception.OrderCurrency);
        Assert.Equal("EGP", exception.TenantCurrency);
        Assert.Empty(await dbContext.PaymentIntents.ToListAsync());
    }

    [Fact]
    public async Task ConfirmCodOrderAsync_PersistsDbBackedIdempotency_AndReplaysStoredResponse()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var order = SeedOrder(
            tenantId,
            status: OrderStatus.Pending,
            paymentStatus: PaymentStatus.Pending,
            paymentMethod: PaymentMethod.Cash,
            paymentMethodCode: "CASH");
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        const string requestId = "cod-confirm-replay";

        var initialResponse = await service.ConfirmCodOrderAsync(
            tenantId,
            order.Id,
            requestId,
            correlationId: "first-confirm",
            cancellationToken: CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        var replayResponse = await service.ConfirmCodOrderAsync(
            tenantId,
            order.Id,
            requestId,
            correlationId: "replay-confirm",
            cancellationToken: CancellationToken.None);

        Assert.Equal(initialResponse.OrderId, replayResponse.OrderId);
        Assert.Equal(initialResponse.OrderNumber, replayResponse.OrderNumber);
        Assert.Equal(initialResponse.ConfirmedAt, replayResponse.ConfirmedAt);

        var idempotencyRecord = await dbContext.OrderRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .SingleAsync();

        Assert.Equal(order.Id, idempotencyRecord.OrderId);
        Assert.Equal(StatusCodes.Status200OK, idempotencyRecord.ResponseStatusCode);
        Assert.False(string.IsNullOrWhiteSpace(idempotencyRecord.ResponsePayload));
        Assert.NotNull(idempotencyRecord.CompletedAtUtc);
    }

    [Fact]
    public async Task ConfirmCodOrderAsync_WhenIdempotencyKeyIsReusedForDifferentOrder_RejectsPayloadConflict()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var firstOrder = SeedOrder(
            tenantId,
            status: OrderStatus.Pending,
            paymentStatus: PaymentStatus.Pending,
            paymentMethod: PaymentMethod.Cash,
            paymentMethodCode: "CASH");
        var secondOrder = SeedOrder(
            tenantId,
            status: OrderStatus.Pending,
            paymentStatus: PaymentStatus.Pending,
            paymentMethod: PaymentMethod.Cash,
            paymentMethodCode: "CASH");
        dbContext.Orders.AddRange(firstOrder, secondOrder);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        const string requestId = "cod-confirm-conflict";

        await service.ConfirmCodOrderAsync(
            tenantId,
            firstOrder.Id,
            requestId,
            correlationId: "first-confirm",
            cancellationToken: CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<PaymentOrderInvalidStateException>(() =>
            service.ConfirmCodOrderAsync(
                tenantId,
                secondOrder.Id,
                requestId,
                correlationId: "second-confirm",
                cancellationToken: CancellationToken.None));

        Assert.Equal("IDEMPOTENCY_KEY_REUSED", exception.ErrorCode);
    }

    [Fact]
    public async Task ConfirmCodOrderAsync_WhenMatchingClaimIsAlreadyInProgress_RejectsConcurrentReplay()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var order = SeedOrder(
            tenantId,
            status: OrderStatus.Pending,
            paymentStatus: PaymentStatus.Pending,
            paymentMethod: PaymentMethod.Cash,
            paymentMethodCode: "CASH");
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        const string requestId = "cod-confirm-in-progress";
        var storageKey = $"PAYMENT:COD_CONFIRM:{tenantId:N}:{requestId}";
        var requestHash = DeterministicRequestHasher.ComputeHash(new
        {
            Operation = "COD_CONFIRM",
            OrderId = order.Id
        });

        dbContext.OrderRequestIdempotencyRecords.Add(new OrderRequestIdempotency
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Key = storageKey,
            RequestHash = requestHash,
            OrderId = order.Id,
            ClaimToken = Guid.NewGuid().ToString("N"),
            ClaimTimestampUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsDeleted = false
        });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<PaymentOrderInvalidStateException>(() =>
            service.ConfirmCodOrderAsync(
                tenantId,
                order.Id,
                requestId,
                correlationId: "concurrent-confirm",
                cancellationToken: CancellationToken.None));

        Assert.Equal("COD_CONFIRMATION_IN_PROGRESS", exception.ErrorCode);
    }

    private static PaymentService CreateService(
        ApplicationDbContext dbContext,
        IDataProtector? dataProtectorOverride = null)
    {
        var dataProtector = dataProtectorOverride ?? Mock.Of<IDataProtector>();
        var dataProtectionProvider = new Mock<IDataProtectionProvider>();
        dataProtectionProvider
            .Setup(provider => provider.CreateProtector(It.IsAny<string>()))
            .Returns(dataProtector);

        return new PaymentService(
            dbContext,
            new RedisConnectionFactory(
                Options.Create(new RedisSettings { Enabled = false, InstanceName = "test" }),
                NullLogger<RedisConnectionFactory>.Instance),
            Options.Create(new RedisSettings { Enabled = false, InstanceName = "test" }),
            Mock.Of<IEventPublisher>(),
            Array.Empty<IPaymentAdapter>(),
            Mock.Of<IPaymentLifecycleService>(),
            dataProtectionProvider.Object,
            Options.Create(new R2Options()),
            new CommerceReliabilityTelemetry(),
            NullLogger<PaymentService>.Instance);
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var context = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = $"tenant-{tenantId:N}",
            Slug = $"tenant-{tenantId:N}",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });
        context.SaveChanges();
        return context;
    }

    private static Order SeedOrder(
        Guid tenantId,
        OrderStatus status,
        PaymentStatus paymentStatus,
        PaymentMethod paymentMethod = PaymentMethod.Card,
        string? paymentMethodCode = null)
    {
        return new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            Status = status,
            PaymentStatus = paymentStatus,
            PaymentMethod = paymentMethod,
            PaymentMethodCode = paymentMethodCode ?? paymentMethod.ToString().ToUpperInvariant(),
            Total = 150m,
            Currency = "EGP",
            CreatedAt = DateTime.UtcNow
        };
    }
}
